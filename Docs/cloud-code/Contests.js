// Cloud Code script "Contests" for O-Manager (see Assets/Scripts/Contests/ContestCloud.cs).
//
// The weekly and daily contests: single player, one try each, a shared leaderboard and prize money when the
// contest is over. Four contests run every week (Monday 00:00 to Sunday 23:59, Swedish time) and four every day:
//   rc-forest    route choice game on an AI course on a forest map
//   rc-sprint    route choice game on an AI course on a sprint map
//   race-forest  a normal race, physics/speed 100 and a technique drawn for the contest (0-100)
//   race-sprint  a normal race, every value 100
//
// Everything is kept where players can not write it themselves, so nobody can run twice, peek at the results
// before running or give themselves prize money:
//   Private custom data "contest_<id>"          key "board": { course, entries: { <playerId>: entry } }
//   Private custom data "contest_<id>_r"        key "<playerId>": the replay of that player (route, route choices)
//   Protected player data of each player        key "contests": { list: [ { id, paid } ] }
// <id> is the period and the kind, e.g. "w2026-47_rc-forest" or "d2026-11-19_race-sprint".
//   Private custom data "contests_meta"         key "cleaned": { ids: [ contests already cleaned up ] }
//
// Clean-up: once a contest is over (and its 3 late hours have passed) the next "status" call deletes its replays
// and its course. The small list of names and results stays KEEP_DAYS (30) more days, so players who come back
// later still get their prize money; then it is deleted too.
//
// The first player of a contest lays its AI course in the game and sends it with "start"; everyone after runs
// the same course. The course (and so the map) is only ever sent to a player who has used their try, or when the
// contest is over: before that "status" tells only how long it is and how many controls it has. Prizes are worked out when a player asks for them after the contest is over ("claim").
//
// Set up once: Unity Cloud dashboard > Cloud Code > Scripts > Create script, name "Contests", type API, paste this
// file and publish. The parameters below are read from the script itself.
//
// Parameters: action (String, required) and payload (String, JSON, optional).
// Actions:
//   status                       the current contests, my entry in each, entries so far; the course's length and
//                                controls when laid, the course itself only when I have started it
//   start      { id, name, maps, course? }   use my one try: answers with the course to run. "course" lays it when
//                                nobody has yet (else the first one stays); "maps" lists the maps this game has,
//                                and a course on a map not in it is refused without using the try
//   submit     { id, score, seconds, replay }   my result (after start, once)
//   board      { id }            the leaderboard: only after my own run, or when the contest is over
//   replays    { id, players }   replays of up to 6 players (comma separated ids), same rule as board
//   claim                        the prize money of contests that are over and not paid yet (paid once)

const { DataApi } = require("@unity-services/cloud-save-1.4");

const KINDS = ["rc-forest", "rc-sprint", "race-forest", "race-sprint"];
const MAX_COURSE_CHARS = 120000;
const MAX_REPLAY_CHARS = 400000;
const MAX_NAME = 24;
const LATE_MINUTES = 180;          // a run started before the end may be handed in up to 3 hours after it
const HISTORY_KEEP = 120;
const CLEAN_DAYS = 28;             // how far back clean-up looks (in case nobody opened the game for a while)
const KEEP_DAYS = 30;              // the leaderboard (names and results) is kept this long for late prize claims
const CLEANED_KEEP = 400;

// Prize money (kr). The top three's prizes grow with every player: the pot (sum of the top three) gets
// GROWTH more for each player after the first three, shared out in the same proportions.
const PRIZES = {
  w: { top: [1000, 700, 500], next: [300, 275, 250], tookPart: 200, growth: 50 },
  d: { top: [700, 500, 300], next: [150, 130, 115], tookPart: 100, growth: 25 }
};

module.exports = async ({ params, context, logger }) => {
  const action = String(params.action || "");
  let payload = {};
  if (params.payload) {
    try { payload = JSON.parse(params.payload); } catch (e) { throw new Error("Bad payload"); }
  }
  const api = new DataApi({ accessToken: context.serviceToken });
  const me = context.playerId;
  if (!me) throw new Error("No player");
  const now = Date.now();

  switch (action) {
    case "status": return await status(api, context, me, now, logger);
    case "start": return await start(api, context, me, payload, now);
    case "submit": return await submit(api, context, me, payload, now);
    case "board": return await board(api, context, me, payload, now);
    case "replays": return await replays(api, context, me, payload, now);
    case "claim": return await claim(api, context, me, now, logger);
    default: throw new Error("Unknown action " + action);
  }
};

module.exports.params = {
  action: { type: "String", required: true },
  payload: { type: "String", required: false }
};

// ---- Time: Swedish time (CET/CEST), worked out by hand so it never depends on the server's time zone data ----

function lastSundayUtc(year, month) {      // month 0-11; 01:00 UTC on the last Sunday is when the clocks change
  const d = new Date(Date.UTC(year, month + 1, 0, 1, 0, 0));
  d.setUTCDate(d.getUTCDate() - d.getUTCDay());
  return d.getTime();
}

function swedishOffsetMs(utcMs) {
  const year = new Date(utcMs).getUTCFullYear();
  const summer = utcMs >= lastSundayUtc(year, 2) && utcMs < lastSundayUtc(year, 9);
  return (summer ? 2 : 1) * 3600000;
}

// Swedish wall clock as a Date whose UTC fields are the Swedish ones
function swedish(utcMs) { return new Date(utcMs + swedishOffsetMs(utcMs)); }

// UTC milliseconds of a Swedish wall-clock time
function fromSwedish(year, month, day) {
  const guess = Date.UTC(year, month, day, 0, 0, 0);
  let utc = guess - swedishOffsetMs(guess);
  utc = guess - swedishOffsetMs(utc);
  return utc;
}

function pad(n) { return (n < 10 ? "0" : "") + n; }

// ISO week of a date given by its Swedish wall-clock fields
function isoWeek(local) {
  const d = new Date(Date.UTC(local.getUTCFullYear(), local.getUTCMonth(), local.getUTCDate()));
  const day = d.getUTCDay() || 7;
  d.setUTCDate(d.getUTCDate() + 4 - day);
  const yearStart = Date.UTC(d.getUTCFullYear(), 0, 1);
  return { year: d.getUTCFullYear(), week: Math.ceil(((d.getTime() - yearStart) / 86400000 + 1) / 7) };
}

// The week and the day running at utcMs: { id, startsAt, endsAt } in UTC milliseconds
function periods(utcMs) {
  const local = swedish(utcMs);
  const y = local.getUTCFullYear(), m = local.getUTCMonth(), d = local.getUTCDate();
  const dayStart = fromSwedish(y, m, d);
  const dayEnd = fromSwedish(y, m, d + 1);
  const weekday = local.getUTCDay() || 7;             // Monday 1 ... Sunday 7
  const weekStart = fromSwedish(y, m, d - (weekday - 1));
  const weekEnd = fromSwedish(y, m, d - (weekday - 1) + 7);
  const w = isoWeek(local);
  return {
    w: { id: "w" + w.year + "-" + pad(w.week), startsAt: weekStart, endsAt: weekEnd },
    d: { id: "d" + y + "-" + pad(m + 1) + "-" + pad(d), startsAt: dayStart, endsAt: dayEnd }
  };
}

// When a contest id ends (UTC ms), worked out from the id itself
function endOf(id) {
  let match = /^d(\d{4})-(\d{2})-(\d{2})_/.exec(id);
  if (match) return fromSwedish(+match[1], +match[2] - 1, +match[3] + 1);
  match = /^w(\d{4})-(\d{2})_/.exec(id);
  if (match) {
    // Monday of ISO week 1 is the Monday of the week with 4 January in it
    const jan4 = new Date(Date.UTC(+match[1], 0, 4));
    const day = jan4.getUTCDay() || 7;
    const monday = new Date(Date.UTC(+match[1], 0, 4 - (day - 1) + (+match[2] - 1) * 7));
    return fromSwedish(monday.getUTCFullYear(), monday.getUTCMonth(), monday.getUTCDate() + 7);
  }
  return 0;
}

function checkId(id) {
  id = String(id || "");
  const match = /^([wd])[0-9-]{7,10}_([a-z-]+)$/.exec(id);
  if (!match || KINDS.indexOf(match[2]) < 0) throw new Error("Bad contest id");
  return id;
}

// A number from the contest id, the same every time (the map and the technique of the week)
function seedOf(id) {
  let h = 2166136261;
  for (let i = 0; i < id.length; i++) { h ^= id.charCodeAt(i); h = Math.imul(h, 16777619); }
  return h >>> 0;
}

// ---- Storage ----

async function getItem(api, projectId, customId, key) {
  try {
    const response = await api.getPrivateCustomItems(projectId, customId, [key]);
    const results = response.data && response.data.results ? response.data.results : [];
    const item = results.find(r => r.key === key);
    return item ? { value: item.value, writeLock: item.writeLock } : null;
  } catch (err) {
    if (err && err.response && err.response.status === 404) return null;
    throw err;
  }
}

function emptyBoard() { return { course: null, entries: {} }; }

async function loadBoard(api, context, id) {
  const item = await getItem(api, context.projectId, "contest_" + id, "board");
  if (!item) return { board: emptyBoard(), writeLock: undefined };
  const value = typeof item.value === "string" ? JSON.parse(item.value) : item.value;
  if (!value.entries) value.entries = {};
  return { board: value, writeLock: item.writeLock };
}

// Read, change and write the board; tried again when someone else wrote it in between
async function updateBoard(api, context, id, change) {
  for (let attempt = 0; attempt < 6; attempt++) {
    const { board, writeLock } = await loadBoard(api, context, id);
    const result = change(board);
    if (result && result.unchanged) return { board, result };
    try {
      const body = { key: "board", value: board };
      if (writeLock) body.writeLock = writeLock;
      await api.setPrivateCustomItem(context.projectId, "contest_" + id, body);
      return { board, result };
    } catch (err) {
      const status = err && err.response ? err.response.status : 0;
      if (status !== 409 && status !== 412) throw err;
      await new Promise(r => setTimeout(r, 80 + Math.random() * 200));
    }
  }
  throw new Error("The contest is busy, try again");
}

async function loadHistory(api, context, playerId) {
  try {
    const response = await api.getProtectedItems(context.projectId, playerId, ["contests"]);
    const results = response.data && response.data.results ? response.data.results : [];
    const item = results.find(r => r.key === "contests");
    if (!item) return { list: [] };
    const value = typeof item.value === "string" ? JSON.parse(item.value) : item.value;
    return value && Array.isArray(value.list) ? value : { list: [] };
  } catch (err) {
    if (err && err.response && err.response.status === 404) return { list: [] };
    throw err;
  }
}

async function saveHistory(api, context, playerId, history) {
  if (history.list.length > HISTORY_KEEP) history.list = history.list.slice(history.list.length - HISTORY_KEEP);
  await api.setProtectedItem(context.projectId, playerId, { key: "contests", value: history });
}

// ---- Standings and prizes ----

function better(kind, a, b) {        // negative when a is ahead
  if (kind.indexOf("rc-") === 0) return b.score - a.score;
  return a.seconds - b.seconds;
}

// The finished entries in order with places (a tie shares the place) and prize money
function standings(id, board) {
  const kind = id.substring(id.indexOf("_") + 1);
  const list = [];
  for (const playerId of Object.keys(board.entries)) {
    const e = board.entries[playerId];
    if (e && e.state === "done") list.push({ playerId, name: e.name, score: e.score || 0, seconds: e.seconds || 0, at: e.at || 0 });
  }
  list.sort((a, b) => better(kind, a, b) || a.at - b.at);
  for (let i = 0; i < list.length; i++) {
    list[i].place = i > 0 && better(kind, list[i - 1], list[i]) === 0 ? list[i - 1].place : i + 1;
  }
  const rules = PRIZES[id[0]];
  const pot = rules.top[0] + rules.top[1] + rules.top[2];
  const growth = 1 + rules.growth * Math.max(0, list.length - 3) / pot;
  for (const row of list) row.prize = prize(rules, row.place, growth);
  return list;
}

function prize(rules, place, growth) {
  if (place <= 3) return Math.round(rules.top[place - 1] * growth / 5) * 5;
  if (place <= 6) return rules.next[place - 4];
  return rules.tookPart;
}

function publicRows(list, me) {
  return list.map(r => ({ playerId: r.playerId, name: r.name, score: r.score, seconds: r.seconds, place: r.place, prize: r.prize, me: r.playerId === me }));
}

// ---- Actions ----

async function status(api, context, me, now, logger) {
  const p = periods(now);
  const contests = [];
  for (const span of ["w", "d"]) {
    for (const kind of KINDS) {
      const id = p[span].id + "_" + kind;
      let board = emptyBoard();
      try { board = (await loadBoard(api, context, id)).board; } catch (err) { logger.warning("status: " + id + " " + err); }
      const mine = board.entries[me];
      const facts = courseFacts(board.course);
      let entries = 0;
      for (const key of Object.keys(board.entries)) if (board.entries[key].state === "done") entries++;
      contests.push({
        id, span, kind,
        startsAt: p[span].startsAt, endsAt: p[span].endsAt,
        seed: seedOf(id),
        technique: seedOf(id + "#tech") % 101,
        course: mine || now > p[span].endsAt ? board.course : null,
        laid: !!board.course,
        length: facts.length,
        controls: facts.controls,
        entries,
        mine: mine ? { state: mine.state, score: mine.score || 0, seconds: mine.seconds || 0 } : null
      });
    }
  }
  try { await cleanUp(api, context, now, logger); } catch (err) { logger.warning("cleanUp: " + err); }
  return { now, contests, prizes: { w: PRIZES.w, d: PRIZES.d } };
}

// ---- Clean-up of contests that are over ----

// The contests of the last daysBack days (weekly ones included) that ended more than minAge ms ago
function finishedIds(now, minAge, daysBack) {
  const ids = [];
  const add = periodId => {
    for (const kind of KINDS) {
      const id = periodId + "_" + kind;
      if (ids.indexOf(id) < 0 && now > endOf(id) + minAge) ids.push(id);
    }
  };
  for (let k = 1; k <= daysBack; k++) {
    const p = periods(now - k * 86400000);
    add(p.w.id);
    add(p.d.id);
  }
  return ids;
}

// Two steps: 3 hours after a contest ends its replays and course go; KEEP_DAYS after that the leaderboard goes
// too. Does at most one contest with something in it per call (and skips up to 8 empty ones), so opening the
// contests page stays quick.
async function cleanUp(api, context, now, logger) {
  const item = await getItem(api, context.projectId, "contests_meta", "cleaned");
  let meta = item ? (typeof item.value === "string" ? JSON.parse(item.value) : item.value) : null;
  if (!meta || !Array.isArray(meta.ids)) meta = { ids: [] };
  if (!Array.isArray(meta.dropped)) meta.dropped = [];

  const todo = [];
  const drops = finishedIds(now, KEEP_DAYS * 86400000, KEEP_DAYS + CLEAN_DAYS);
  for (const id of finishedIds(now, LATE_MINUTES * 60000, CLEAN_DAYS)) {
    if (meta.ids.indexOf(id) < 0 && drops.indexOf(id) < 0) todo.push({ id, drop: false });
  }
  for (const id of drops) {
    if (meta.dropped.indexOf(id) < 0) todo.push({ id, drop: true });
  }
  if (todo.length === 0) return;

  for (const t of todo.slice(0, 8)) {
    const { board, writeLock } = await loadBoard(api, context, t.id);
    const players = Object.keys(board.entries).filter(key => board.entries[key] && board.entries[key].state === "done");
    if (meta.ids.indexOf(t.id) < 0) {
      for (let i = 0; i < players.length; i += 10) {
        await Promise.all(players.slice(i, i + 10).map(key => deleteItem(api, context, "contest_" + t.id + "_r", key)));
      }
      // Checked, so a delete that silently did nothing is noticed (warning in the Cloud Code logs) and tried again
      if (players.length > 0 && await getItem(api, context.projectId, "contest_" + t.id + "_r", players[0]))
        throw new Error("replays of " + t.id + " were not deleted");
      if (board.course && !t.drop) {
        await updateBoard(api, context, t.id, b => {
          if (!b.course) return { unchanged: true };
          b.course = null;
        });
      }
      meta.ids.push(t.id);
    }
    if (t.drop) {
      if (writeLock) {
        await deleteItem(api, context, "contest_" + t.id, "board");
        if (await getItem(api, context.projectId, "contest_" + t.id, "board")) throw new Error("board of " + t.id + " was not deleted");
      }
      meta.dropped.push(t.id);
    }
    if (writeLock) {
      logger.info("cleanUp: " + t.id + (t.drop ? " deleted" : ", " + players.length + " replays deleted"));
      break;
    }
  }
  if (meta.ids.length > CLEANED_KEEP) meta.ids = meta.ids.slice(meta.ids.length - CLEANED_KEEP);
  if (meta.dropped.length > CLEANED_KEEP) meta.dropped = meta.dropped.slice(meta.dropped.length - CLEANED_KEEP);
  await api.setPrivateCustomItem(context.projectId, "contests_meta", { key: "cleaned", value: meta });
}

async function deleteItem(api, context, customId, key) {
  try {
    await api.deletePrivateCustomItem(key, context.projectId, customId);
  } catch (err) {
    if (err && err.response && err.response.status === 404) return;   // never saved, or already gone
    throw err;
  }
}

function isCurrent(id, now) {
  const p = periods(now);
  return id.indexOf(p.w.id + "_") === 0 || id.indexOf(p.d.id + "_") === 0;
}

// The straight-line length (m, start - controls - finish) and the number of controls of a course
function courseFacts(courseJson) {
  if (!courseJson) return { length: 0, controls: 0 };
  try {
    const c = JSON.parse(courseJson);
    const points = [c.start].concat(c.controls || []).concat([c.finish]).filter(x => x && typeof x.e === "number");
    let length = 0;
    for (let i = 1; i < points.length; i++) length += Math.hypot(points[i].e - points[i - 1].e, points[i].n - points[i - 1].n);
    return { length: Math.round(length), controls: (c.controls || []).length };
  } catch (err) { return { length: 0, controls: 0 }; }
}

function mapOf(courseJson) {
  try { return String(JSON.parse(courseJson).mapId || ""); } catch (err) { return ""; }
}

async function start(api, context, me, payload, now) {
  const id = checkId(payload.id);
  if (!isCurrent(id, now)) throw new Error("This contest is over");
  const name = String(payload.name || "Runner").replace(/[<>\n\r\t]/g, "").trim().substring(0, MAX_NAME) || "Runner";
  const maps = String(payload.maps || "").split(",").map(x => x.trim()).filter(x => x);
  let course = payload.course ? String(payload.course) : "";
  if (course) {
    if (course.length < 10 || course.length > MAX_COURSE_CHARS) throw new Error("Bad course");
    JSON.parse(course);
  }
  const { board, result } = await updateBoard(api, context, id, b => {
    if (b.entries[me]) return { unchanged: true, error: "You have already run this contest" };
    if (!b.course && !course) return { unchanged: true, error: "The course is not laid yet" };
    const use = b.course || course;
    if (maps.length > 0 && maps.indexOf(mapOf(use)) < 0) return { unchanged: true, error: "The map of this contest is missing" };
    if (!b.course) b.course = course;
    b.entries[me] = { name, state: "started", at: now };
  });
  if (result && result.error) throw new Error(result.error);
  const history = await loadHistory(api, context, me);
  if (!history.list.some(h => h.id === id)) {
    history.list.push({ id, paid: false });
    await saveHistory(api, context, me, history);
  }
  return { course: board.course };
}

async function submit(api, context, me, payload, now) {
  const id = checkId(payload.id);
  const end = endOf(id);
  if (end > 0 && now > end + LATE_MINUTES * 60000) throw new Error("Too late to hand in");
  const score = Math.max(0, Math.min(1000000, Math.round(Number(payload.score) || 0)));
  const seconds = Math.max(0, Math.min(86400 * 3, Math.round(Number(payload.seconds) || 0)));
  const replay = payload.replay ? String(payload.replay) : "";
  if (replay.length > MAX_REPLAY_CHARS) throw new Error("Replay too big");
  const { board, result } = await updateBoard(api, context, id, b => {
    const e = b.entries[me];
    if (!e) return { unchanged: true, error: "Start the contest first" };
    if (e.state === "done") return { unchanged: true, error: "Already handed in" };
    e.state = "done";
    e.score = score;
    e.seconds = seconds;
    e.at = now;
  });
  if (result && result.error) throw new Error(result.error);
  if (replay.length > 0) {
    await api.setPrivateCustomItem(context.projectId, "contest_" + id + "_r", { key: me, value: replay });
  }
  return { rows: publicRows(standings(id, board), me) };
}

async function mayLook(api, context, me, id, now) {
  const { board } = await loadBoard(api, context, id);
  const mine = board.entries[me];
  const over = endOf(id) > 0 && now > endOf(id);
  if (!(mine && mine.state === "done") && !over) throw new Error("Run the contest first");
  return board;
}

async function board(api, context, me, payload, now) {
  const id = checkId(payload.id);
  const b = await mayLook(api, context, me, id, now);
  return { rows: publicRows(standings(id, b), me), course: b.course, over: now > endOf(id) };
}

async function replays(api, context, me, payload, now) {
  const id = checkId(payload.id);
  await mayLook(api, context, me, id, now);
  const players = String(payload.players || "").split(",").map(s => s.trim()).filter(s => /^[A-Za-z0-9_-]{1,64}$/.test(s)).slice(0, 6);
  const out = {};
  if (players.length === 0) return { replays: out };
  try {
    const response = await api.getPrivateCustomItems(context.projectId, "contest_" + id + "_r", players);
    const results = response.data && response.data.results ? response.data.results : [];
    for (const item of results) out[item.key] = typeof item.value === "string" ? item.value : JSON.stringify(item.value);
  } catch (err) {
    if (!(err && err.response && err.response.status === 404)) throw err;
  }
  return { replays: out };
}

async function claim(api, context, me, now, logger) {
  const history = await loadHistory(api, context, me);
  const paid = [];
  let changed = false;
  for (const h of history.list) {
    if (h.paid) continue;
    const end = endOf(h.id);
    if (end <= 0 || now <= end + LATE_MINUTES * 60000) continue;   // late runs may still come in
    let list = [];
    let gone = false;
    try {
      const loaded = await loadBoard(api, context, h.id);
      gone = !loaded.writeLock;                 // deleted KEEP_DAYS after the end: too late for its prize
      list = standings(h.id, loaded.board);
    } catch (err) { logger.warning("claim: " + h.id + " " + err); continue; }
    h.paid = true;
    changed = true;
    if (gone) continue;
    const row = list.find(r => r.playerId === me);
    paid.push({ id: h.id, place: row ? row.place : 0, of: list.length, prize: row ? row.prize : 0, finished: !!row });
  }
  if (changed) await saveHistory(api, context, me, history);
  return { paid };
}
