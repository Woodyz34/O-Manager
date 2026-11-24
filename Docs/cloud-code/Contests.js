// Cloud Code script "Contests" for O-Manager (see Assets/Scripts/Contests/ContestCloud.cs).
//
// The weekly and daily contests: single player, one try each, a shared leaderboard and prize money when the
// contest is over. Six contests run every week (Monday 00:00 to Sunday 23:59, Swedish time):
//   rc-forest    route choice game on an AI course on a forest map
//   rc-sprint    route choice game on an AI course on a sprint map
//   race-forest  a normal race, physics/speed 100 and a technique drawn for the contest (0-100)
//   race-sprint  a normal race, every value 100
//   run-forest   run it yourself in 3D on a forest map
//   run-sprint   run it yourself in 3D on a sprint map (a town)
// and three every day (2026-11-20, the user), each on a sprint or a forest map, half and half, drawn from the id:
//   rc           route choice game
//   race         a normal race (forest: middle or long, half and half, like every forest contest)
//   run          run it yourself in 3D
// "sprint" in the status tells the game which it is.
//
// Everything is kept where players can not write it themselves, so nobody can run twice, peek at the results
// before running or give themselves prize money:
//   Private custom data "contest_<id>"          key "board": { course, entries: { <playerId>: entry } }
//   Private custom data "contest_<id>_r"        key "<playerId>": the replay of that player (route, route choices)
//   Protected player data of each player        key "contests": { list: [ { id, paid } ] }
// <id> is the period and the kind, e.g. "w2026-47_rc-forest" or "d2026-11-19_race-sprint".
//   Private custom data "contests_meta"         key "cleaned": { ids: [ contests already cleaned up ] }
//
// Clean-up: the replays (the GPS routes) and the course of a contest that is over are kept REPLAY_DAYS, so the
// players can look at the leaderboard and everyone's routes afterwards (2026-11-24, the user): a daily contest 7
// days, a weekly one 30 days. Then the next "status" call deletes them. The small list of names and results stays
// KEEP_DAYS (30) days after the end, so players who come back later still get their prize money; then it is
// deleted too.
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
//   replays    { id, players }   replays of up to 10 players (comma separated ids), same rule as board
//   claim                        the prizes (money and packs) of contests that are over and not paid yet (paid once)
//   recent                       the contests that ended lately and still have their leaderboard and GPS routes
//                                (daily 7 days, weekly 30 days): winner, number of runners and my place
//   ranking                      the world ranking (2026-11-24): the best players of the last 30 days, overall and
//                                per game and map type, see "World ranking" below; brings it up to date first
//   rankHistory { player }       one player's ranking day by day, for the line chart
//
// The world ranking is kept in:
//   Private custom data "contests_rank"         key "window": the contests of the last RANK_DAYS days with every
//                                                player's points; key "lists": the ranking lists as last worked out
//   Private custom data "contests_rank_hist_<n>" key "h": the ranking history of the players whose id hashes to n
//                                                (RANK_HIST_SHARDS parts), one entry per day, kept for years

const { DataApi } = require("@unity-services/cloud-save-1.4");

const KINDS = {
  w: ["rc-forest", "rc-sprint", "race-forest", "race-sprint", "run-forest", "run-sprint"],
  d: ["rc", "race", "run"]
};
// Kinds that are no longer run, still known so their prizes are paid and their data cleaned up
const OLD_KINDS = ["rc-forest", "rc-sprint", "race-forest", "race-sprint"];

function kindsOf(span) { return span === "w" ? KINDS.w : KINDS.d.concat(OLD_KINDS); }

// A sprint map or a forest map: in the kind, or for the daily contests drawn from the id
function isSprint(id) {
  const kind = id.substring(id.indexOf("_") + 1);
  if (/-sprint$/.test(kind)) return true;
  if (/-forest$/.test(kind)) return false;
  return ((seedOf(id + "#sprint") >>> 13) & 1) === 0;   // a high bit: the lowest bit of this hash hardly varies
}
const MAX_COURSE_CHARS = 120000;
const MAX_REPLAY_CHARS = 400000;
const MAX_NAME = 24;
const LATE_MINUTES = 180;          // a run started before the end may be handed in up to 3 hours after it
const HISTORY_KEEP = 120;
const CLEAN_DAYS = 45;             // how far back clean-up looks (in case nobody opened the game for a while)
const KEEP_DAYS = 30;              // the leaderboard (names and results) is kept this long for late prize claims
// How long the replays (GPS routes) and the course are kept after the end (2026-11-24, the user: "radera dem en
// vecka efter eller en månad efter"): daily contests 7 days, weekly 30 days
const REPLAY_DAYS = { d: 7, w: 30 };
const MAX_REPLAY_PLAYERS = 10;

function replaysUntil(id) { return endOf(id) + REPLAY_DAYS[id[0]] * 86400000; }
const CLEANED_KEEP = 400;

// Prizes (2026-11-19, the user): runner packs for the best, money for the rest.
//   top      money (kr) for places 1-3, on top of their packs
//   growth   kr added to a bonus pot for every player who finishes (2026-11-20, the user: more players
//            give more money, never more packs); places 1-3 share the pot as in "share" (percent)
//   packs    runner packs for places 1-3: "weekly" (Elite 15 %, Gold 35 %, Silver 50 %), "daily" (Elite 5 %,
//            Gold 25 %, Silver 70 %), "normal" (the shop's pack). The odds are in the game (PrizePacks.cs).
//   next     money for places 4-6, tookPart for everyone else who finished. A tie shares the place and its prize.
const PRIZES = {
  w: { top: [0, 0, 0], packs: [["weekly"], ["daily", "normal"], ["daily"]], next: [300, 275, 250], tookPart: 200, growth: 50, share: [50, 30, 20] },
  d: { top: [0, 500, 300], packs: [["daily"], [], []], next: [150, 130, 115], tookPart: 100, growth: 25, share: [50, 30, 20] }
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
    case "recent": return await recent(api, context, me, now, logger);
    case "ranking": return await ranking(api, context, me, now, logger);
    case "rankHistory": return await rankHistory(api, context, payload);
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
  if (!match || kindsOf(match[1]).indexOf(match[2]) < 0) throw new Error("Bad contest id");
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
  if (kind === "rc" || kind.indexOf("rc-") === 0) return b.score - a.score;
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
  const bonus = rules.growth * list.length;
  for (const row of list) {
    row.prize = prize(rules, row.place, bonus);
    row.packs = row.place <= 3 ? rules.packs[row.place - 1] : [];
  }
  return list;
}

function prize(rules, place, bonus) {
  if (place <= 3) return Math.round((rules.top[place - 1] + bonus * rules.share[place - 1] / 100) / 5) * 5;
  if (place <= 6) return rules.next[place - 4];
  return rules.tookPart;
}

function publicRows(list, me) {
  return list.map(r => ({ playerId: r.playerId, name: r.name, score: r.score, seconds: r.seconds, place: r.place, prize: r.prize, packs: r.packs, me: r.playerId === me }));
}

// ---- Actions ----

async function status(api, context, me, now, logger) {
  const p = periods(now);
  const list = [];
  for (const span of ["w", "d"]) for (const kind of KINDS[span]) list.push({ span, kind, id: p[span].id + "_" + kind });
  const boards = await Promise.all(list.map(async x => {
    try { return (await loadBoard(api, context, x.id)).board; } catch (err) { logger.warning("status: " + x.id + " " + err); return emptyBoard(); }
  }));
  const contests = [];
  for (let i = 0; i < list.length; i++) {
    {
      const { span, kind, id } = list[i];
      const board = boards[i];
      const mine = board.entries[me];
      const facts = courseFacts(board.course);
      let entries = 0;
      for (const key of Object.keys(board.entries)) if (board.entries[key].state === "done") entries++;
      contests.push({
        id, span, kind,
        sprint: isSprint(id),
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
    for (const kind of kindsOf(periodId[0])) {
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

// Two steps: REPLAY_DAYS after a contest ends its replays and course go; KEEP_DAYS after the end the leaderboard
// goes too. Does at most one contest with something in it per call (and skips up to 8 empty ones), so opening the
// contests page stays quick.
async function cleanUp(api, context, now, logger) {
  const item = await getItem(api, context.projectId, "contests_meta", "cleaned");
  let meta = item ? (typeof item.value === "string" ? JSON.parse(item.value) : item.value) : null;
  if (!meta || !Array.isArray(meta.ids)) meta = { ids: [] };
  if (!Array.isArray(meta.dropped)) meta.dropped = [];

  const todo = [];
  const drops = finishedIds(now, KEEP_DAYS * 86400000, KEEP_DAYS + CLEAN_DAYS);
  for (const id of finishedIds(now, LATE_MINUTES * 60000, CLEAN_DAYS)) {
    if (now <= replaysUntil(id)) continue;     // the GPS routes are still kept
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

// Running now, and of a kind that is still run
function isCurrent(id, now) {
  const p = periods(now);
  const kind = id.substring(id.indexOf("_") + 1);
  return (id.indexOf(p.w.id + "_") === 0 && KINDS.w.indexOf(kind) >= 0) || (id.indexOf(p.d.id + "_") === 0 && KINDS.d.indexOf(kind) >= 0);
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
  // A started run counts as your try even when it never finished (a crash, or leaving), so it opens the board too
  // (2026-11-20, the user)
  if (!mine && !over) throw new Error("Run the contest first");
  return board;
}

async function board(api, context, me, payload, now) {
  const id = checkId(payload.id);
  const b = await mayLook(api, context, me, id, now);
  const over = now > endOf(id);
  const rows = publicRows(standings(id, b), me);
  // Started but no result: "Did not finish" at the bottom. While the contest runs only your own such row is shown,
  // since the others may still be running
  for (const playerId of Object.keys(b.entries)) {
    const e = b.entries[playerId];
    if (!e || e.state === "done" || (!over && playerId !== me)) continue;
    rows.push({ playerId, name: e.name, score: 0, seconds: 0, place: 0, prize: 0, packs: [], dnf: true, me: playerId === me });
  }
  return { rows, course: b.course, over, sprint: isSprint(id), endsAt: endOf(id), replaysUntil: replaysUntil(id) };
}

async function replays(api, context, me, payload, now) {
  const id = checkId(payload.id);
  await mayLook(api, context, me, id, now);
  const players = String(payload.players || "").split(",").map(s => s.trim()).filter(s => /^[A-Za-z0-9_-]{1,64}$/.test(s)).slice(0, MAX_REPLAY_PLAYERS);
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
    paid.push({ id: h.id, sprint: isSprint(h.id), place: row ? row.place : 0, of: list.length, prize: row ? row.prize : 0, packs: row ? row.packs : [], finished: !!row });
  }
  if (changed) await saveHistory(api, context, me, history);
  // The game claims every time the main menu opens, so this keeps the world ranking (and everyone's ranking
  // history) up to date once a day even when nobody opens the ranking page
  try { await updateRanking(api, context, now, logger); } catch (err) { logger.warning("updateRanking: " + err); }
  return { paid };
}

// The contests that ended lately and still have their GPS routes (daily 7 days, weekly 30 days), newest first, each
// with its winner, how many finished and my own place. Opened from the contests page ("Results") and from the prize
// pop-up, so the players can go through the leaderboard and the routes after they got their prizes (2026-11-24).
async function recent(api, context, me, now, logger) {
  const history = await loadHistory(api, context, me);
  const ran = history.list.map(h => h.id);
  const days = Math.max(REPLAY_DAYS.d, REPLAY_DAYS.w) + 7;
  const ids = finishedIds(now, 0, days).filter(id => {
    if (now > replaysUntil(id)) return false;
    const kind = id.substring(id.indexOf("_") + 1);
    return KINDS[id[0]].indexOf(kind) >= 0 || ran.indexOf(id) >= 0;
  });
  ids.sort((a, b) => endOf(b) - endOf(a));
  const out = [];
  for (let i = 0; i < ids.length; i += 10) {
    const part = ids.slice(i, i + 10);
    const loaded = await Promise.all(part.map(async id => {
      try { return await loadBoard(api, context, id); } catch (err) { logger.warning("recent: " + id + " " + err); return null; }
    }));
    for (let k = 0; k < part.length; k++) {
      const id = part[k];
      const kind = id.substring(id.indexOf("_") + 1);
      const item = loaded[k];
      const took = ran.indexOf(id) >= 0;
      if (!item || !item.writeLock) {
        if (took) out.push({ id, span: id[0], kind, sprint: isSprint(id), endsAt: endOf(id), replaysUntil: replaysUntil(id), entries: 0, winner: null, mine: { place: 0, finished: false } });
        continue;
      }
      const list = standings(id, item.board);
      if (list.length === 0 && !took) continue;
      const row = list.find(r => r.playerId === me);
      const win = list[0];
      out.push({
        id, span: id[0], kind, sprint: isSprint(id), endsAt: endOf(id), replaysUntil: replaysUntil(id),
        entries: list.length,
        winner: win ? { name: win.name, score: win.score, seconds: win.seconds } : null,
        mine: took ? (row ? { place: row.place, score: row.score, seconds: row.seconds, finished: true } : { place: 0, finished: false }) : null
      });
    }
  }
  return { contests: out };
}

// ---- World ranking (2026-11-24, the user: "vem är den bästa spelaren i O-Manager") ----
//
// Every weekly and daily contest that is over gives each player who finished it points:
//   races and 3D runs        100 * winner's time / own time       (the winner 100, twice the winner's time 50)
//   route choice game        100 * own score / winner's score
// so the gap to the winner counts, not only the place. A contest counts only when at least RANK_MIN_FINISHERS
// finished (winning alone is no feat). A weekly contest weighs RANK_WEIGHT.w (2), a daily one RANK_WEIGHT.d (1).
//
// A player's points in a list are the weighted average of their contests in the list during the last RANK_DAYS (30)
// days, but divided by at least RANK_FULL (4): with less weight than that the missing part counts as 0, so one lucky
// win is not enough to top the list (one daily win alone gives 25). From 4 dailies, or 2 weeklies, it is the plain
// average.
//   Lists: rc, race, run (each game), and each game on forest and on sprint maps ("rc-forest" ...).
//   overall = the average of the three games (a game never played counts as 0): the best all-round player.
//
// The ranking is worked out again once a day, when the daily contest is final (3 hours after midnight, when late
// runs are in): the first "ranking" or "claim" call after that does it. Each player's places and points of that day
// are then added to their ranking history (kept for years, for the line chart).

const RANK_DAYS = 30;
const RANK_WEIGHT = { d: 1, w: 2 };
const RANK_FULL = 4;
const RANK_MIN_FINISHERS = 2;
const RANK_LOADS_PER_CALL = 400;   // contest boards read per call (the first time reads 30 days back, ~250 boards, most of them empty)
const RANK_TOP = 200;              // rows sent per list (plus my own row)
const RANK_HIST_SHARDS = 32;
const RANK_HIST_KEEP = 3650;       // days of history per player
const RANK_GAMES = ["rc", "race", "run"];
const RANK_LISTS = ["overall", "rc", "race", "run", "rc-forest", "rc-sprint", "race-forest", "race-sprint", "run-forest", "run-sprint"];

function gameOf(id) { return id.substring(id.indexOf("_") + 1).split("-")[0]; }
function round1(x) { return Math.round(x * 10) / 10; }

// The last day whose daily contest is final: { id: "2026-11-23", end: UTC ms of its end }
function lastRankDay(now) {
  const running = periods(now - LATE_MINUTES * 60000).d;
  const prev = periods(running.startsAt - 1).d;
  return { id: prev.id.substring(1), end: running.startsAt };
}

// Each finisher's ranking points in one contest, or null when the contest does not count
function contestPoints(id, list) {
  if (list.length < RANK_MIN_FINISHERS) return null;
  const rc = gameOf(id) === "rc";
  const best = rc ? list[0].score : list[0].seconds;
  if (!(best > 0)) return null;
  return list.map(r => {
    let pts = 0;
    if (rc) pts = 100 * r.score / best;
    else if (r.seconds > 0) pts = 100 * best / r.seconds;
    return [r.playerId, Math.round(Math.max(0, Math.min(100, pts)) * 100) / 100, r.place];
  });
}

async function loadRankItem(api, context, customId, key) {
  const item = await getItem(api, context.projectId, customId, key);
  if (!item) return { value: null, writeLock: undefined };
  return { value: typeof item.value === "string" ? JSON.parse(item.value) : item.value, writeLock: item.writeLock };
}

// All lists from the contests in the window. prev: the lists of the last update, for the change in place.
function computeLists(contests, names, prev) {
  const per = {};
  const add = (pid, key, w, pts) => {
    const p = per[pid] || (per[pid] = {});
    const a = p[key] || (p[key] = { w: 0, wp: 0, c: 0 });
    a.w += w; a.wp += w * pts; a.c++;
  };
  for (const c of contests) {
    for (const [pid, pts] of c.rows) {
      add(pid, c.g, c.w, pts);
      add(pid, c.g + "-" + (c.s ? "sprint" : "forest"), c.w, pts);
    }
  }
  const value = a => a.wp / Math.max(a.w, RANK_FULL);
  const lists = {};
  for (const key of RANK_LISTS) {
    const rows = [];
    for (const pid of Object.keys(per)) {
      const p = per[pid];
      if (key === "overall") {
        const parts = RANK_GAMES.map(g => p[g] ? round1(value(p[g])) : 0);
        const c = RANK_GAMES.reduce((sum, g) => sum + (p[g] ? p[g].c : 0), 0);
        rows.push({ p: pid, n: names[pid] ? names[pid].n : "Runner", pts: round1((parts[0] + parts[1] + parts[2]) / 3), c, parts });
      } else if (p[key]) {
        const a = p[key];
        rows.push({ p: pid, n: names[pid] ? names[pid].n : "Runner", pts: round1(value(a)), avg: round1(a.wp / a.w), c: a.c });
      }
    }
    rows.sort((a, b) => b.pts - a.pts || b.c - a.c || (a.n < b.n ? -1 : a.n > b.n ? 1 : 0));
    const was = {};
    if (prev && prev[key]) for (const r of prev[key]) was[r.p] = r.place;
    for (let i = 0; i < rows.length; i++) {
      rows[i].place = i > 0 && rows[i - 1].pts === rows[i].pts ? rows[i - 1].place : i + 1;
      rows[i].was = was[rows[i].p] || 0;
    }
    lists[key] = rows;
  }
  return lists;
}

function histShard(pid) { return seedOf(pid) % RANK_HIST_SHARDS; }

// Brings the ranking up to date when a new day's contests are final. Returns true when it changed it.
async function updateRanking(api, context, now, logger) {
  const day = lastRankDay(now);
  const w = await loadRankItem(api, context, "contests_rank", "window");
  let win = w.value;
  if (!win || !Array.isArray(win.contests)) win = { day: "", done: [], contests: [], names: {} };
  if (!Array.isArray(win.done)) win.done = [];
  if (!win.names) win.names = {};
  if (win.day === day.id) return false;

  const from = day.end - RANK_DAYS * 86400000;
  const todo = finishedIds(now, LATE_MINUTES * 60000, RANK_DAYS + 8)
    .filter(id => endOf(id) > from && endOf(id) <= day.end && win.done.indexOf(id) < 0);
  const part = todo.slice(0, RANK_LOADS_PER_CALL);
  for (let i = 0; i < part.length; i += 20) {
    const ids = part.slice(i, i + 20);
    const loaded = await Promise.all(ids.map(async id => {
      try { return await loadBoard(api, context, id); } catch (err) { logger.warning("ranking: " + id + " " + err); return undefined; }
    }));
    for (let k = 0; k < ids.length; k++) {
      const id = ids[k];
      if (loaded[k] === undefined) continue;          // try again next time
      win.done.push(id);
      if (!loaded[k].writeLock) continue;             // nobody ran it
      const list = standings(id, loaded[k].board);
      const rows = contestPoints(id, list);
      if (!rows) continue;
      const end = endOf(id);
      win.contests.push({ id, end, g: gameOf(id), s: isSprint(id), w: RANK_WEIGHT[id[0]], rows });
      for (const r of list) {
        const old = win.names[r.playerId];
        if (!old || old.at <= end) win.names[r.playerId] = { n: r.name, at: end };
      }
    }
  }
  win.contests = win.contests.filter(c => c.end > from);
  win.done = win.done.filter(id => endOf(id) > from - 10 * 86400000);
  const complete = todo.length <= part.length;
  if (complete) {
    const playing = {};
    for (const c of win.contests) for (const r of c.rows) playing[r[0]] = true;
    for (const pid of Object.keys(win.names)) if (!playing[pid]) delete win.names[pid];
    win.day = day.id;
  }
  const windowBody = { key: "window", value: win };
  if (w.writeLock) windowBody.writeLock = w.writeLock;
  try {
    await api.setPrivateCustomItem(context.projectId, "contests_rank", windowBody);
  } catch (err) {
    const status = err && err.response ? err.response.status : 0;
    if (status === 409 || status === 412) return false;   // someone else is updating it right now
    throw err;
  }
  if (!complete) return false;

  const before = await loadRankItem(api, context, "contests_rank", "lists");
  const lists = computeLists(win.contests, win.names, before.value ? before.value.lists : null);
  await api.setPrivateCustomItem(context.projectId, "contests_rank", { key: "lists", value: { day: day.id, updatedAt: now, lists } });
  logger.info("ranking: " + day.id + ", " + win.contests.length + " contests, " + lists.overall.length + " players");

  // Today's places and points into each player's history
  const entries = {};
  for (const key of RANK_LISTS) {
    const of = lists[key].length;
    for (const r of lists[key]) {
      const e = entries[r.p] || (entries[r.p] = {});
      e[key] = [r.pts, r.place, of];
    }
  }
  const shards = {};
  for (const pid of Object.keys(entries)) {
    const n = histShard(pid);
    (shards[n] || (shards[n] = [])).push(pid);
  }
  const numbers = Object.keys(shards);
  for (let i = 0; i < numbers.length; i += 8) {
    await Promise.all(numbers.slice(i, i + 8).map(async n => {
      try {
        const item = await loadRankItem(api, context, "contests_rank_hist_" + n, "h");
        const hist = item.value && item.value.players ? item.value : { players: {} };
        for (const pid of shards[n]) {
          let list = hist.players[pid] || [];
          list = list.filter(x => x.d !== day.id);
          list.push({ d: day.id, n: win.names[pid] ? win.names[pid].n : undefined, r: entries[pid] });
          if (list.length > RANK_HIST_KEEP) list = list.slice(list.length - RANK_HIST_KEEP);
          hist.players[pid] = list;
        }
        await api.setPrivateCustomItem(context.projectId, "contests_rank_hist_" + n, { key: "h", value: hist });
      } catch (err) { logger.warning("ranking history " + n + ": " + err); }
    }));
  }
  return true;
}

async function ranking(api, context, me, now, logger) {
  try { await updateRanking(api, context, now, logger); } catch (err) { logger.warning("updateRanking: " + err); }
  const item = await loadRankItem(api, context, "contests_rank", "lists");
  const stored = item.value || { day: "", updatedAt: 0, lists: {} };
  const lists = {}, mine = {}, counts = {};
  for (const key of RANK_LISTS) {
    const rows = stored.lists && stored.lists[key] ? stored.lists[key] : [];
    lists[key] = rows.slice(0, RANK_TOP);
    counts[key] = rows.length;
    mine[key] = rows.find(r => r.p === me) || null;
  }
  return {
    day: stored.day, updatedAt: stored.updatedAt, me, lists, mine, counts,
    rules: { days: RANK_DAYS, weekly: RANK_WEIGHT.w, daily: RANK_WEIGHT.d, full: RANK_FULL, minFinishers: RANK_MIN_FINISHERS }
  };
}

async function rankHistory(api, context, payload) {
  const pid = String(payload.player || "");
  if (!/^[A-Za-z0-9_-]{1,64}$/.test(pid)) throw new Error("Bad player");
  const item = await loadRankItem(api, context, "contests_rank_hist_" + histShard(pid), "h");
  const list = item.value && item.value.players && item.value.players[pid] ? item.value.players[pid] : [];
  return { player: pid, days: list };
}
