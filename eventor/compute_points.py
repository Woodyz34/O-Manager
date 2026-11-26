#!/usr/bin/env python3
"""Compute physic and technique points for every runner from eventor/data/races.

Makes no API calls. Reads every race file (Eventor and WinSplits) and writes
eventor/data/runners.json plus eventor/data/points_report.md.

Per race and class (runners on the same course):
  * Leg reference = mean of the fastest 25 % of leg times.
  * Runner level = median of leg_time / reference over the runner's legs.
  * A leg is a mistake when the time lost against reference * level is
    more than 15 s and more than 25 %. Mistake time = sum of those losses.
  * Clean time c = finish time - mistake time.
  * Technique T = 100 - 2 * mistake_minutes * 100 / controls (finish not a control),
    so 1 minute lost per 100 controls gives 98 (user's choice 2026-11-02, was 1 point).
  * Physic P = min(100, 100 * c100 / c). Double the clean time = 50.

Calibration of c100 (separate scales for women and men, since classes never mix):
  * VM/EM/VC finals (WinSplits, level 6) are the anchor: best clean time = 100.
    Qualifications and B/C finals of VM/EM/VC are NOT anchors (user 2026-11-07): their
    winner does not get 100 automatically, they are calibrated like any other race.
  * Other races: c100 = median of c * R / 100 over runners who already have a
    rating R (MIN_RACES_TO_CALIBRATE adjusted races, default 1, so juniors can
    chain in through juniors who also ran senior/international races).
    At least 3 such runners are needed.
  * Otherwise the race is "ojusterad": the best clean time gets 100 for display,
    but the race does not count. Everything is solved again until it stops
    changing, so a race can become adjusted through runners met later (or earlier).

Sprint (distance "Sprint", WinSplits files with _sprint_, added 2026-11-13) is a separate scale:
  * VM and World Cup sprint finals are the anchors (best clean time = 100); VM qualifications only chain,
    SM, Swedish League, DM, O-Ringen and the rest give everyone a number. No minimum number of races.
  * A sprint race with too few runners who have a sprint rating (the H/D18 sprints) is calibrated against its
    runners' forest physics instead, since both scales put the world's best at 100.
  * A leg that comes out negative (a wrongly recorded split) is joined with the next leg (sprint only).
  * The result is the cards' "speed" (sprint speed, -1 = none, "-" on the card). Sprint technique is stored as
    "sprintTechnique" but not shown. Sprint races never make a runner visible: that still takes forest races.
  * The forest speed is now called physics ("physics" in the game file, "speed" before 2026-11-13).

Rating per runner, over the last 365 days:
  * weight = 0.5 ** (age_days / 180)
  * physic: Tukey biweight (c = 4.685, scale = max(1.4826 * MAD, 5)) times weight
  * technique: weighted mean
  * visible in the game with >= 5 adjusted races, or any VM/EM/VC race (finals,
    qualifications and B finals alike; national qualifications do not count).

Merits (best races of the last 365 days, forest and sprint together since 2026-11-14, shown on the card close-up):
  * race score = 100 * c100 / finish time, mistakes included, only adjusted races.
    c100 already carries the strength of the field, so 20th against a strong field
    can beat a win against a weak one.
  * field = mean physic rating of the 5 best rated runners in that class.
  * behind = seconds behind the class winner.
  * The whole class of every merit race, with split times, is written to
    Assets/Resources/Runners/Results/<name>.json for the game's result view.
"""

import collections
import datetime as dt
import glob
import hashlib
import json
import os
import re
import statistics
import unicodedata

HERE = os.path.dirname(os.path.abspath(__file__))
RACES_DIR = os.path.join(HERE, "data", "races")
OUT_JSON = os.path.join(HERE, "data", "runners.json")
OUT_REPORT = os.path.join(HERE, "data", "points_report.md")
# What the game reads (Resources.Load<TextAsset>("Runners/runners"))
GAME_JSON = os.path.join(os.path.dirname(HERE), "Assets", "Resources", "Runners", "runners.json")
RESULTS_DIR = os.path.join(os.path.dirname(HERE), "Assets", "Resources", "Runners", "Results")
EMBLEM_DIR = os.path.join(os.path.dirname(HERE), "Assets", "Resources", "Emblem")
CLUBS_TXT = os.path.join(HERE, "klubbar.txt")
COUNTRY_TXT = os.path.join(HERE, "nationalitet.txt")

COUNTRIES = {
    "SWE": "SWEDEN", "NOR": "NORWAY", "FIN": "FINLAND", "DEN": "DENMARK", "SUI": "SWITZERLAND",
    "CZE": "CZECHIA", "FRA": "FRANCE", "GER": "GERMANY", "GBR": "GREAT BRITAIN", "AUT": "AUSTRIA",
    "HUN": "HUNGARY", "LAT": "LATVIA", "LTU": "LITHUANIA", "EST": "ESTONIA", "POL": "POLAND",
    "SVK": "SLOVAKIA", "NED": "NETHERLANDS", "ESP": "SPAIN", "ITA": "ITALY", "UKR": "UKRAINE",
    "AUS": "AUSTRALIA", "NZL": "NEW ZEALAND", "USA": "UNITED STATES", "CAN": "CANADA",
    "CRO": "CROATIA", "BEL": "BELGIUM", "POR": "PORTUGAL", "JPN": "JAPAN", "ISL": "ICELAND",
    "IRL": "IRELAND", "SLO": "SLOVENIA", "BUL": "BULGARIA", "ROU": "ROMANIA", "RUS": "RUSSIA",
    "BLR": "BELARUS", "ISR": "ISRAEL", "TUR": "TURKEY", "SRB": "SERBIA", "CHN": "CHINA",
    "HKG": "HONG KONG", "KOR": "SOUTH KOREA", "RSA": "SOUTH AFRICA", "BRA": "BRAZIL",
}

WINDOW_DAYS = 365
HALF_LIFE = 180
MIN_CALIBRATORS = 3
MIN_RACES_TO_CALIBRATE = 1     # 3 left every H/D18 and H/D20 race unadjusted with one year of data
MIN_RACES_VISIBLE = 5
# File name text that marks a VM/EM/VC race as a side race (not an anchor), on top of _kval
SIDE_RACE = re.compile(r"(?<![a-z])(final[ _-]?[b-z]|[b-z][ _-]?final|semi|heat|kval|qual)", re.I)
MISTAKE_SECONDS = 15            # user chose 15 s (was 30) on 2026-11-02
MISTAKE_SHARE = 0.25
TUKEY_C = 4.685
MAD_FLOOR = 5.0
MIN_GROUP = 3
MAX_ITER = 400
TECHNIQUE_PER_MINUTE = 2.0      # points lost per mistake minute per 100 controls
MERITS_PER_RUNNER = 3
FIELD_TOP = 5


# ---------- identity ----------

def norm_name(name):
    s = unicodedata.normalize("NFKD", name or "")
    s = "".join(ch for ch in s if not unicodedata.combining(ch))
    s = s.replace("ø", "o").replace("Ø", "O").replace("æ", "ae").replace("ß", "ss").replace("đ", "d")
    return re.sub(r"\s+", " ", re.sub(r"[^a-z ]", " ", s.lower())).strip()


def short_name(name):
    """First and last name only, from match_name: "Tuva Enlund Östangård" and "Tuva Ostangard"
    are both "tuva ostangard". Only used when the whole name finds nobody."""
    parts = match_name(name).split()
    return f"{parts[0]} {parts[-1]}" if len(parts) > 1 else ""


def match_name(name):
    """Looser than norm_name, only for finding the same person under two spellings:
    WinSplits writes Ødum as Oedum, Nørgård as Noergaard and Örnhagen as Ornhagen.
    Both spellings end up the same here (odum, norgard, ornhagen)."""
    s = (name or "").lower()
    for a, b in (("ø", "oe"), ("æ", "ae"), ("å", "aa"), ("ä", "ae"), ("ö", "oe"), ("ü", "ue")):
        s = s.replace(a, b)
    s = norm_name(s)
    for a, b in (("oe", "o"), ("ae", "a"), ("aa", "a"), ("ue", "u")):
        s = s.replace(a, b)
    return s


def useful_club(club):
    """Exports from abroad (Portugal O Meeting) give short codes like "HSK" or "Individual SUI": not shown."""
    c = club.strip()
    return not (c.lower().startswith("individual") or (len(c) <= 4 and c.upper() == c))


class Identities:
    """Eventor person id is the key. WinSplits runners, and Eventor results without a person id,
    are matched to it by name and sex. Ids made from names use norm_name, so they stay the same
    as before; the matching uses the looser match_name."""

    def __init__(self):
        self.by_name = collections.defaultdict(set)      # (match name, sex) -> eventor keys
        self.person_ids = collections.defaultdict(set)   # (match name, sex) -> e-keys, from all Eventor races
        self.by_short = collections.defaultdict(set)     # (first + last name, sex) -> eventor keys
        self.info = {}                                   # key -> dict
        self.ambiguous = []
        self.merged = {}                                 # WinSplits id it would have had -> Eventor key

    def prescan(self, races):
        """Notes every Eventor person id first, so a result without an id can join the person
        even when it comes earlier in the list."""
        for race in races:
            for cls in race["classes"]:
                for r in cls["runners"]:
                    if r.get("pid"):
                        sex = r.get("sex") or ("F" if cls["class"][:1] in "DW" else "M")
                        self.person_ids[(match_name(r["name"]), sex)].add(f"e{r['pid']}")

    def add_eventor(self, r):
        name_key = (match_name(r["name"]), r.get("sex"))
        if r.get("pid"):
            key = f"e{r['pid']}"
        elif len(self.person_ids.get(name_key, ())) == 1:
            key = next(iter(self.person_ids[name_key]))
        else:
            key = f"n:{norm_name(r['name'])}:{r.get('sex')}"
        self.by_name[name_key].add(key)
        if short_name(r["name"]):
            self.by_short[(short_name(r["name"]), r.get("sex"))].add(key)
        inf = self.info.setdefault(key, {"name": r["name"], "sex": r.get("sex"), "birth_year": r.get("birth_year"),
                                         "club": None, "country": None, "country_sure": False})
        if r.get("club"):
            inf["club"] = r["club"]
        # Eventor gives the country of the club, not of the runner: a guess, which a country
        # from VM, EM or the World Cup (WinSplits) replaces. A national team is a sure country.
        if r.get("national_team"):
            inf["country"] = r["country"]
            inf["country_sure"] = True
        elif r.get("country") and not inf["country_sure"]:
            inf["country"] = r["country"]
        return key

    def peek(self, r, is_ws):
        """The key a runner would get, without adding anything. For runners who are only shown in a
        results list (their course had too few runners to be rated)."""
        if not is_ws:
            if r.get("pid"):
                return f"e{r['pid']}"
            found = self.person_ids.get((match_name(r["name"]), r.get("sex")), ())
            return next(iter(found)) if len(found) == 1 else f"n:{norm_name(r['name'])}:{r.get('sex')}"
        cands = self.by_name.get((match_name(r["name"]), r.get("sex")), set())
        return next(iter(cands)) if len(cands) == 1 else f"w:{norm_name(r['name'])}:{r.get('sex')}"

    def match_winsplits(self, r, source):
        cands = self.by_name.get((match_name(r["name"]), r.get("sex")), set())
        if not cands and short_name(r["name"]):
            # Eventor may have a middle name that WinSplits leaves out ("Tuva Enlund Östangård")
            short = self.by_short.get((short_name(r["name"]), r.get("sex")), set())
            if len(short) == 1:
                cands = short
                self.merged[f"w:{norm_name(r['name'])}:{r.get('sex')}"] = next(iter(short))
        if len(cands) == 1:
            key = next(iter(cands))
        else:
            if len(cands) > 1:
                self.ambiguous.append((r["name"], r.get("country") or r.get("club"), source, sorted(cands)))
            key = f"w:{norm_name(r['name'])}:{r.get('sex')}"
        inf = self.info.setdefault(key, {"name": r["name"], "sex": r.get("sex"), "birth_year": None,
                                         "club": None, "country": None, "country_sure": False})
        if r.get("country"):
            # VM, EM and the World Cup are run for a country, so this one is sure
            inf["country"] = r["country"]
            inf["country_sure"] = True
        if r.get("club") and not inf["club"] and useful_club(r["club"]):
            inf["club"] = r["club"]
        return key


# ---------- one class in one race ----------

def leg_times(r):
    """Leg times from cumulative splits; None where a split is missing."""
    cum = [s[1] for s in r["splits"]] + [r["time"]]
    out, prev = [], 0.0
    for t in cum:
        if t is None or prev is None:
            out.append(None)
        else:
            out.append(t - prev)
        prev = t
    return out


def analyse_group(runners, merge_bad_legs=False):
    """runners: OK runners on the same course. Adds mistake time and clean time in place.
    merge_bad_legs (sprint only, so the forest numbers stay as they were): a leg that comes out negative for
    anyone (a split time recorded wrongly, as at Sprint by Night 2026-11-06) is joined with the next leg for
    everybody, so the error is not counted as a mistake."""
    legs = [leg_times(r) for r in runners]
    n_legs = len(legs[0])
    if merge_bad_legs:
        for i in range(n_legs):
            if not any(l[i] is not None and l[i] < 0 for l in legs):
                continue
            for l in legs:
                if i + 1 < n_legs:
                    l[i + 1] = l[i] + l[i + 1] if l[i] is not None and l[i + 1] is not None else None
                l[i] = None
    refs = []
    for i in range(n_legs):
        vals = sorted(l[i] for l in legs if l[i] is not None and l[i] > 0)
        if not vals:
            refs.append(None)
            continue
        k = max(1, round(len(vals) * 0.25))
        refs.append(sum(vals[:k]) / k)
    for r, l in zip(runners, legs):
        ratios = [l[i] / refs[i] for i in range(n_legs) if l[i] and refs[i]]
        level = statistics.median(ratios) if ratios else 1.0
        mistake = 0.0
        for i in range(n_legs):
            if not l[i] or not refs[i]:
                continue
            expected = refs[i] * level
            lost = l[i] - expected
            if lost > MISTAKE_SECONDS and lost > MISTAKE_SHARE * expected:
                mistake += lost
        r["_mistake"] = mistake
        r["_clean"] = r["time"] - mistake


def national_team(club):
    """A national team entered as a club ("Team Fin", "Team Spain" at O-Ringen or a junior championship):
    the country code (FIN, ESP), else None. "Team Jizerky" is a real club and stays one."""
    c = (club or "").strip()
    m = (re.fullmatch(r"(?i)(?:national\s+team|team|landslaget?)\s+(.+?)", c)
         or re.fullmatch(r"(?i)(.+?)\s+national\s+team", c))
    # A plain country name as club ("Australia", "China" at O-Ringen) is a national team too
    word = (m.group(1) if m else c).strip().upper()
    if not word:
        return None
    if word in COUNTRIES:
        return word
    for code, name in COUNTRIES.items():
        if name == word:
            return code
    swedish = SWEDISH_COUNTRY.get(word.lower())
    for code, name in COUNTRIES.items():
        if swedish and name == swedish:
            return code
    return None


def load_races():
    races = []
    for p in sorted(glob.glob(os.path.join(RACES_DIR, "*.json"))):
        with open(p, encoding="utf-8") as f:
            race = json.load(f)
        # A national team is the runner's country, not a club (2026-11-19: Florian Fritzen had the club
        # "Team Fin" and no country)
        for cls in race["classes"]:
            for r in cls["runners"]:
                team = national_team(r.get("club"))
                if team:
                    r["country"], r["club"], r["national_team"] = team, None, True
                elif re.match(r"(?i)team\s", r.get("club") or ""):
                    # No real club starts with "Team" (the user, 2026-11-19): a team that is not a
                    # country ("Team Jizerky") is no club either
                    r["club"] = None
        races.append(race)
    return races


def is_side_race(race):
    """VM/EM/VC qualification or B/C final: rated against known runners, winner not 100."""
    name = os.path.splitext(os.path.basename(race.get("source_file") or ""))[0]
    return bool(race.get("qualification")) or bool(SIDE_RACE.search(name.split("_", 4)[-1] if name.count("_") >= 4 else ""))


def build_entries(races, ids):
    """One entry per (race, class/course group) with OK runners and clean times."""
    eventor = [r for r in races if r.get("source") != "winsplits"]
    winsplits = [r for r in races if r.get("source") == "winsplits"]
    # Sprints last, so names, clubs and countries come from the forest races first and the cards stay the same
    winsplits.sort(key=lambda r: (r.get("distance") or "").lower() == "sprint")
    groups = []
    ids.prescan(eventor)
    # The same race can come from Eventor and from a WinSplits file (e.g. SM sprint): the WinSplits copy is skipped.
    # Known by date and the three fastest runners with their times.
    seen_results = set()

    def podium(date, rs):
        top = sorted(rs, key=lambda r: r["time"])[:3]
        return (date, tuple((match_name(r["name"]), int(round(r["time"]))) for r in top))

    for race in eventor + winsplits:     # Eventor first so WinSplits can match to Eventor ids
        date = dt.date.fromisoformat(race["date"])
        is_ws = race.get("source") == "winsplits"
        international = is_ws and race.get("level") == 6
        anchor = international and not is_side_race(race)
        for cls in race["classes"]:
            ok = [r for r in cls["runners"] if r["status"] == "OK" and r.get("time")]
            if ok:
                sig = podium(date, ok)
                if is_ws and sig in seen_results:
                    continue
                seen_results.add(sig)
            by_course = collections.defaultdict(list)
            for r in ok:
                by_course[tuple(s[0] for s in r["splits"])].append(r)
            class_best = min((r["time"] for r in ok), default=0)
            sex = "F" if cls["class"][:1] in "DW" else "M"
            # The whole class for the results list, also when it ran forked courses (each course
            # is rated on its own below, but the list shows everyone)
            class_file = f"{slug(str(race['event_id']))}_{slug(str(race.get('race_id') or 0))}_{slug(cls['class'])}"
            class_rows = {}
            for course_no, (codes, rs) in enumerate(by_course.items()):
                if len(rs) < MIN_GROUP or not codes:
                    continue
                copies = []
                for original in rs:
                    r = dict(original)
                    r["sex"] = r.get("sex") or sex
                    r["_key"] = ids.match_winsplits(r, race["event_name"]) if is_ws else ids.add_eventor(r)
                    class_rows[id(original)] = r
                    copies.append(r)
                rs = copies
                analyse_group(rs, merge_bad_legs=(race.get("distance") or "").lower() == "sprint")
                groups.append({
                    "id": f"{race['event_id']}|{race.get('race_id', '')}|{cls['class']}|{len(groups)}",
                    "event": race["event_name"], "class": cls["class"], "date": date,
                    "sprint": (race.get("distance") or "").lower() == "sprint",
                    "anchor": anchor, "international": international, "level": race.get("level"), "controls": len(codes),
                    "runners": rs, "class_ok": len(ok), "class_best": class_best,
                    "file": class_file, "class_rows": None,
                    "codes": list(codes), "distance": race.get("distance") or "",
                    "length": rs[0].get("course_length") or cls.get("course_length") or 0,
                })
            if not class_rows:
                continue
            everyone = []
            for original in ok:
                r = class_rows.get(id(original))
                if r is None:
                    r = dict(original)
                    r["sex"] = r.get("sex") or sex
                    r["_key"] = ids.peek(r, is_ws)
                everyone.append(r)
            everyone.sort(key=lambda r: r["time"])
            for i, r in enumerate(everyone):
                r["_place"] = int(r["position"]) if r.get("position") else i + 1
            for g in groups:
                if g["file"] == class_file and g["class_rows"] is None:
                    g["class_rows"] = everyone
    return groups


# ---------- rating ----------

def weighted_median(pairs):
    pairs = sorted(pairs)
    total = sum(w for _, w in pairs)
    acc = 0.0
    for x, w in pairs:
        acc += w
        if acc >= total / 2:
            return x
    return pairs[-1][0]


def biweight_mean(values, weights):
    if not values:
        return None
    m = weighted_median(list(zip(values, weights)))
    for _ in range(10):
        mad = statistics.median(abs(v - m) for v in values)
        s = max(1.4826 * mad, MAD_FLOOR)
        num = den = 0.0
        for v, w in zip(values, weights):
            u = (v - m) / (TUKEY_C * s)
            bw = (1 - u * u) ** 2 if abs(u) < 1 else 0.0
            num += v * w * bw
            den += w * bw
        if den == 0:
            break
        new = num / den
        if abs(new - m) < 1e-6:
            m = new
            break
        m = new
    return m


def ratings(per_runner, today):
    """per_runner: key -> list of (date, physic, technique). Returns key -> (physic, technique, n)."""
    out = {}
    for key, rows in per_runner.items():
        rows = [r for r in rows if (today - r[0]).days <= WINDOW_DAYS]
        if not rows:
            continue
        w = [0.5 ** (max(0, (today - d).days) / HALF_LIFE) for d, _, _ in rows]
        phys = biweight_mean([p for _, p, _ in rows], w)
        tech = sum(t * wi for (_, _, t), wi in zip(rows, w)) / sum(w)
        out[key] = (phys, tech, len(rows))
    return out


def solve(groups, today, fallback=None, via_fallback=None):
    """fallback (sprint only): the forest ratings. A sprint race with too few runners who have a sprint rating
    (the H/D18 sprints had nobody who also ran a senior sprint) is calibrated against its runners' physics
    instead, since both scales put the world's best at 100. Such races are added to via_fallback."""
    c100 = {}
    for g in groups:
        if g["anchor"]:
            c100[g["id"]] = min(r["_clean"] for r in g["runners"])
    adjusted = {g["id"] for g in groups if g["anchor"]}
    rating = {}
    for it in range(MAX_ITER):
        per_runner = collections.defaultdict(list)
        for g in groups:
            if g["id"] not in adjusted:
                continue
            for r in g["runners"]:
                p = min(100.0, 100.0 * c100[g["id"]] / r["_clean"])
                t = max(0.0, min(100.0, 100.0 - TECHNIQUE_PER_MINUTE * (r["_mistake"] / 60.0) * 100.0 / g["controls"]))
                per_runner[r["_key"]].append((g["date"], p, t))
        rating = ratings(per_runner, today)
        changed = False
        new_adjusted = {g["id"] for g in groups if g["anchor"]}
        for g in groups:
            if g["anchor"]:
                continue
            est = [r["_clean"] * rating[r["_key"]][0] / 100.0 for r in g["runners"]
                   if r["_key"] in rating and rating[r["_key"]][2] >= MIN_RACES_TO_CALIBRATE]
            if fallback and len(est) < MIN_CALIBRATORS:
                bridge = [r["_clean"] * fallback[r["_key"]][0] / 100.0 for r in g["runners"]
                          if r["_key"] in fallback and fallback[r["_key"]][2] >= MIN_RACES_TO_CALIBRATE]
                if len(bridge) >= MIN_CALIBRATORS:
                    est = bridge
                    if via_fallback is not None:
                        via_fallback.add(g["id"])
                elif via_fallback is not None:
                    via_fallback.discard(g["id"])
            elif via_fallback is not None:
                via_fallback.discard(g["id"])
            if len(est) >= MIN_CALIBRATORS:
                val = statistics.median(est)
                new_adjusted.add(g["id"])
            else:
                val = min(r["_clean"] for r in g["runners"])
            if abs(c100.get(g["id"], 0) - val) > 0.5:
                changed = True
            c100[g["id"]] = val
        if new_adjusted != adjusted:
            changed = True
        adjusted = new_adjusted
        if not changed:
            break
    return c100, adjusted, rating, it + 1


# ---------- merits ----------

def merits(scales, today):
    """key -> the runner's best races of the last year, best first. scales = one (groups, adjusted, c100, rating)
    per scale: forest and sprint (since 2026-11-14, user's wish: sprint-strong runners show their best sprints).
    Both scales put the world's best at 100, so a sprint score and a forest score compare directly."""
    out = collections.defaultdict(list)
    for groups, adjusted, c100, rating in scales:
        add_merits(out, groups, adjusted, c100, rating, today)
    for key in out:
        out[key].sort(key=lambda m: (-m["score"], m["date"]))
        out[key] = out[key][:MERITS_PER_RUNNER]
    return out


def add_merits(out, groups, adjusted, c100, rating, today):
    for g in groups:
        if g["id"] not in adjusted or (today - g["date"]).days > WINDOW_DAYS:
            continue
        rated = sorted((rating[r["_key"]][0] for r in g["runners"] if r["_key"] in rating), reverse=True)
        field = sum(rated[:FIELD_TOP]) / len(rated[:FIELD_TOP]) if rated else 0.0
        for r in g["runners"]:
            place = r["_place"]
            out[r["_key"]].append({
                "race": g["event"], "date": g["date"].isoformat(), "klass": g["class"],
                "place": place, "of": max(g["class_ok"], place),
                "behind": int(round(max(0.0, r["time"] - g["class_best"]))),
                "score": round(100.0 * c100[g["id"]] / r["time"], 2), "field": round(field, 2),
                "results": g["file"],
            })


# ---------- full results for the merit races ----------

def slug(text):
    return re.sub(r"[^A-Za-z0-9]+", "", unicodedata.normalize("NFKD", text).encode("ascii", "ignore").decode()) or "x"


def unity_meta(path):
    """A .meta with a GUID made from the file name, so it stays the same every week."""
    guid = hashlib.md5(os.path.basename(path).encode("utf-8")).hexdigest()
    with open(path + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(f"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n"
                "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def write_results(groups, cards):
    """One file per race and class that a card's merit points to, with every finisher and split time.
    The game shows it when a merit is clicked. Files nobody points to any more are removed."""
    wanted = {m["results"] for c in cards for m in c.get("merits", []) if m.get("results")}
    by_file = {g["file"]: g for g in groups}
    os.makedirs(RESULTS_DIR, exist_ok=True)
    if not os.path.exists(RESULTS_DIR + ".meta"):
        unity_meta_folder(RESULTS_DIR)
    written = set()
    for name in sorted(wanted):
        g = by_file.get(name)
        if g is None:
            continue
        everyone = g["class_rows"]
        # The controls most runners had are the columns. Runners on another fork (same number of
        # controls) keep their own order; runners with a missing or extra punch are lined up by
        # control code. Each row then carries its controls, so the game ranks a leg only among
        # those who ran the same two controls.
        courses = collections.Counter(tuple(str(s[0]) for s in r["splits"]) for r in everyone)
        controls = list(courses.most_common(1)[0][0])
        forked = len(courses) > 1
        rows = []
        for r in everyone:
            info = r.get("club") or r.get("country") or ""
            codes, times = line_up(r["splits"], controls)
            row = {
                "id": r["_key"], "name": r["name"], "club": info, "place": r["_place"],
                "time": int(round(r["time"])), "splits": times,
            }
            if forked:
                row["codes"] = codes
            rows.append(row)
        data = {"race": g["event"], "klass": g["class"], "date": g["date"].isoformat(),
                "distance": g["distance"], "length": int(g["length"] or 0), "finishers": g["class_ok"],
                "forked": forked, "controls": controls, "runners": rows}
        path = os.path.join(RESULTS_DIR, name + ".json")
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
        if not os.path.exists(path + ".meta"):
            unity_meta(path)
        written.add(name + ".json")
    for old in os.listdir(RESULTS_DIR):
        base = old[:-5] if old.endswith(".meta") else old
        if base not in written:
            os.remove(os.path.join(RESULTS_DIR, old))
    return len(written)


def line_up(splits, controls):
    """A runner's splits as one per column. The same number of controls: taken in the runner's own
    order (a forked course). Otherwise each column takes the runner's split at that control, if any."""
    own = [str(s[0]) for s in splits]
    times = [int(round(s[1])) if s[1] is not None else -1 for s in splits]
    if len(own) == len(controls):
        return own, times
    codes, out, at = [], [], 0
    for code in controls:
        try:
            found = own.index(code, at)
        except ValueError:
            codes.append("")
            out.append(-1)
            continue
        codes.append(code)
        out.append(times[found])
        at = found + 1
    return codes, out


def unity_meta_folder(path):
    guid = hashlib.md5(os.path.basename(path).encode("utf-8") + b"/folder").hexdigest()
    with open(path + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n"
                "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


# ---------- game file ----------

def country_name(r):
    c = (r.get("country") or "").strip()
    if not c:
        return "SWEDEN" if r.get("club") else ""
    return COUNTRIES.get(c.upper(), c.upper())


def junior_age_limits(groups, today):
    """Highest age each runner can have this year, from the junior classes (H18/D20/M20 ...) they ran in.
    For runners without a birth year (WinSplits, foreign runners in Eventor): a runner in H20 in July is at
    most 20 that year, so at most 21 the year after. Only races in the rating window count."""
    limit = {}
    for g in groups:
        if (today - g["date"]).days > WINDOW_DAYS:
            continue
        m = re.match(r"[HDMW](\d{2})", (g.get("class") or "").strip().upper())
        if not m or int(m.group(1)) > 20:
            continue
        top = int(m.group(1)) + (today.year - g["date"].year)
        for r in g["runners"]:
            limit[r["_key"]] = min(limit.get(r["_key"], 99), top)
    return limit


def game_class(r, today, age_limits=None):
    prefix = "H" if r.get("sex") == "M" else "D"
    by = r.get("birth_year")
    # No birth year: the junior classes the runner ran decide, otherwise H21/D21 (was always H21/D21 before 2026-11-26)
    age = today.year - by if by else (age_limits or {}).get(r["id"], 99)
    return prefix + ("18" if age <= 18 else "20" if age <= 20 else "21")


def country_by_hand():
    """eventor/nationalitet.txt: "Namn = Land" for runners whose country is only guessed from the
    club. The country may be in Swedish (Storbritannien), English (Great Britain) or a code (GBR)."""
    given = {}
    if os.path.exists(COUNTRY_TXT):
        with open(COUNTRY_TXT, encoding="utf-8") as f:
            for line in f:
                line = line.split("#", 1)[0]
                if "=" in line:
                    name, land = (s.strip() for s in line.split("=", 1))
                    if name and land:
                        given[match_name(name)] = (name, land)
    return given


def english_country(land):
    up = land.strip().upper()
    return COUNTRIES.get(up) or SWEDISH_COUNTRY.get(land.strip().lower()) or up


def countries_by_hand(cards):
    """Sets the countries from eventor/nationalitet.txt, and writes the file again: filled lines first,
    then every card whose country is a guess from a club outside Sweden, with an empty right side."""
    given = country_by_hand()
    for c in cards:
        hand = given.get(match_name(c["name"].split(" (")[0]))
        if hand:
            c["country"] = english_country(hand[1])
            c.pop("countryGuessed", None)
    filled = sorted(given.values(), key=lambda nl: nl[0])
    unsure = sorted((c["name"], c["country"], c["club"]) for c in cards
                    if c.get("countryGuessed") and c["country"] not in ("SWEDEN", ""))
    lines = ["# Nationalitet för löpare där landet bara är gissat från klubben (Eventor ger klubbens land).",
             "# Skriv landet efter likhetstecknet, t.ex.  Förnamn Efternamn = Storbritannien  (eller GBR).",
             "# Alla löpare går att skriva här, även de som inte står i listan (t.ex. en britt i en svensk klubb).",
             "# Rader utan land gör ingenting. Filen skrivs om när poängen räknas: ifyllda rader står kvar överst.",
             ""]
    lines += [f"{n} = {l}" for n, l in filled]
    if filled:
        lines.append("")
    lines.append("# Gissat från en klubb utanför Sverige:")
    lines += [f"{n} =    # gissat {l.title()} ({club})" for n, l, club in unsure]
    with open(COUNTRY_TXT, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


def write_game_file(runners, today, groups, merged_ids=None):
    """Cards for the game. Runners in packs get today's numbers. A runner who was a card
    before but is no longer eligible keeps the last numbers and leaves the packs (frozen)."""
    previous = {}
    renamed = []
    if os.path.exists(GAME_JSON):
        with open(GAME_JSON, encoding="utf-8") as f:
            old_file = json.load(f)
        previous = {c["id"]: c for c in old_file.get("runners", [])}
        renamed = old_file.get("renamed", [])
    cards = []
    info_by_id = {r["id"]: r for r in runners}
    age_limits = junior_age_limits(groups, today)
    for r in runners:
        if not r["visible"]:
            continue
        cards.append({
            "id": r["id"], "name": r["name"], "man": r["sex"] == "M", "klass": game_class(r, today, age_limits),
            "country": country_name(r), "club": r.get("club") or "",
            **({} if r.get("country_sure") else {"countryGuessed": True}),
            # physics = forest speed (was "speed" before 2026-11-13); speed = sprint speed, -1 = none ("-" on the card)
            "physics": round(r["physic"], 2), "speed": sprint_value(r["sprint_speed"]),
            "technique": round(r["technique"], 2), "sprintTechnique": sprint_value(r["sprint_technique"]), "inPacks": True,
            "merits": r.get("merits", []),
        })
    clubs_by_hand(cards)
    countries_by_hand(cards)
    add_history(cards, previous, old_file.get("computed") if previous else None, today)
    now = {c["id"] for c in cards}
    by_id = {c["id"]: c for c in cards}
    merged_ids = merged_ids or {}
    by_match = {(match_name(c["name"]), c["man"]): c for c in cards}
    merged = []
    frozen = 0
    for cid, old in previous.items():
        if cid not in now:
            # The same person found under another spelling is now one card: the old card goes,
            # and the game moves it in players' collections to the new name ("renamed" below)
            same = by_id.get(merged_ids.get(cid)) or by_match.get((match_name(old["name"].split(" (")[0]), old["man"]))
            if same is not None and not cid.startswith("e"):
                merged.append((old["name"], same))
                continue
            old = upgrade_card(old)
            now_info = info_by_id.get(cid)
            if now_info is not None and now_info.get("sprint_speed") is not None:
                old["speed"] = sprint_value(now_info["sprint_speed"])
                old["sprintTechnique"] = sprint_value(now_info["sprint_technique"])
            cards.append({**old, "inPacks": False})
            frozen += 1
    # Two runners with the same name get their club (or birth year) added, because the
    # player's collection and multiplayer use the name.
    by_name = collections.defaultdict(list)
    for c in cards:
        by_name[c["name"].split(" (")[0]].append(c)
    info = {r["id"]: r for r in runners}
    for base, same in by_name.items():
        if len(same) > 1:
            for c in same:
                extra = c["club"] or str(info.get(c["id"], {}).get("birth_year") or c["id"])
                c["name"] = f"{base} ({extra})"
    for old_name, card in merged:
        renamed = [r for r in renamed if r["from"] != old_name]
        if old_name != card["name"]:
            renamed.append({"from": old_name, "to": card["name"]})
    # A name that was renamed earlier and is a card again is no longer moved
    names = {c["name"] for c in cards}
    renamed = [r for r in renamed if r["from"] not in names]
    # overall = 40 % physics, 40 % speed (physics when there is no sprint speed), 20 % technique, as RunnerCards.OverallExact
    cards.sort(key=lambda c: (-(0.4 * c["physics"] + 0.4 * (c["speed"] if c["speed"] >= 0 else c["physics"])
                                + 0.2 * c["technique"]), c["name"]))
    os.makedirs(os.path.dirname(GAME_JSON), exist_ok=True)
    with open(GAME_JSON, "w", encoding="utf-8") as f:
        json.dump({"computed": today.isoformat(), "renamed": renamed, "runners": cards}, f, ensure_ascii=False, indent=0)
    write_emblem_list(cards, today)
    write_results(groups, cards)
    return frozen


def add_history(cards, previous, previous_day, today):
    """Each card keeps its physics, speed and technique from every update ("history", oldest first), so My Runners
    can show a line chart of how the runner has changed (2026-11-24, the user; the chart shows from 3 updates).
    A card that had no history yet starts with the numbers it had in the last file, on that file's day.
    Running again on the same day replaces that day's point. Frozen cards (no longer in packs) keep theirs as it is."""
    day = today.isoformat()
    for c in cards:
        old = previous.get(c["id"])
        history = list(old.get("history", [])) if old else []
        if old and not history and previous_day:
            o = upgrade_card(old)
            history.append({"d": previous_day, "p": o.get("physics", 0), "s": o.get("speed", -1), "t": o.get("technique", 0)})
        history = [h for h in history if h.get("d") != day]
        history.append({"d": day, "p": c["physics"], "s": c["speed"], "t": c["technique"]})
        c["history"] = history


def sprint_value(v):
    return round(v, 2) if v is not None else -1


def upgrade_card(old):
    """A card written before sprint speed existed had its forest speed in "speed": it becomes "physics"."""
    if "physics" in old:
        return dict(old)
    card = dict(old)
    card["physics"] = card.pop("speed", 0)
    card["speed"] = -1
    card["sprintTechnique"] = -1
    return card


def clubs_by_hand(cards):
    """WinSplits results (VM, EM, World Cup) only give the country, so those runners have no club.
    eventor/klubbar.txt lets you write it by hand, one runner per line: "Namn = Klubb".
    The file is written again each time, with every card that still has no club and an empty right side."""
    given = {}
    if os.path.exists(CLUBS_TXT):
        with open(CLUBS_TXT, encoding="utf-8") as f:
            for line in f:
                if "=" in line and not line.lstrip().startswith("#"):
                    name, club = (s.strip() for s in line.split("=", 1))
                    if name and club:
                        given[match_name(name)] = (name, club)
    for c in cards:
        hand = given.get(match_name(c["name"].split(" (")[0]))
        if hand and not c["club"]:
            c["club"] = hand[1]
    filled = sorted(given.values(), key=lambda nc: nc[0])
    filled_keys = set(given)
    missing = sorted(c["name"] for c in cards if not c["club"] and match_name(c["name"]) not in filled_keys)
    lines = ["# Klubbar för löpare som saknar klubb (oftast utländska från VM, EM och världscupen).",
             "# Skriv klubben efter likhetstecknet, t.ex.  Förnamn Efternamn = Klubbens namn",
             "# Klubbmärket läggs i Assets/Resources/Emblem/Klubbar med samma namn som klubben.",
             "# Rader utan klubb gör ingenting. Filen skrivs om när poängen räknas: ifyllda rader står kvar överst.",
             ""]
    lines += [f"{n} = {c}" for n, c in filled]
    if filled:
        lines.append("")
    lines += [f"{n} = " for n in missing]
    with open(CLUBS_TXT, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


def emblem_key(text):
    """Same rule as Emblems.Key in the game: lower case letters and digits only."""
    return "".join(ch for ch in text.lower() if ch.isalnum())


def photo_section(cards, found):
    """Runners in card order (best first), with or without a real photo."""
    missing, done = [], []
    for c in cards:
        base = c["name"].split(" (")[0]
        ok = emblem_key(c["name"]) in found or emblem_key(base) in found
        (done if ok else missing).append(f"  {c['name']}.jpg   ({c['klass']}, {c['physics']}/{c['technique']})")
    return [f"PROFILBILDER ({len(done)} av {len(cards)} har foto)", "", "Har foto:", *(done or ["  (inga än)"]), "",
            "Saknar foto, bästa löparna först:", *missing, ""]


# Swedish flag file names, the same list as RunnerCards.EnglishCountry in the game
SWEDISH_COUNTRY = {
    "sverige": "SWEDEN", "norge": "NORWAY", "suomi": "FINLAND", "danmark": "DENMARK", "schweiz": "SWITZERLAND",
    "frankrike": "FRANCE", "tyskland": "GERMANY", "storbritannien": "GREAT BRITAIN", "tjeckien": "CZECHIA",
    "estland": "ESTONIA", "lettland": "LATVIA", "ukraina": "UKRAINE", "ungern": "HUNGARY", "österrike": "AUSTRIA",
    "polen": "POLAND", "spanien": "SPAIN", "nederländerna": "NETHERLANDS", "holland": "NETHERLANDS",
    "slovakien": "SLOVAKIA", "litauen": "LITHUANIA", "kanada": "CANADA", "italien": "ITALY",
    "australien": "AUSTRALIA", "usa": "UNITED STATES", "nya zeeland": "NEW ZEALAND", "kroatien": "CROATIA",
    "hongkong": "HONG KONG CHINA", "hong kong": "HONG KONG CHINA", "belgien": "BELGIUM", "ryssland": "RUSSIA",
    "vitryssland": "BELARUS", "irland": "IRELAND", "slovenien": "SLOVENIA", "rumänien": "ROMANIA",
    "bulgarien": "BULGARIA", "turkiet": "TURKEY", "kina": "CHINA", "sydafrika": "SOUTH AFRICA", "brasilien": "BRAZIL",
}


def write_emblem_list(cards, today):
    """List every country and club on the cards, and whether its picture is in the folder yet."""
    def have(folder):
        path = os.path.join(EMBLEM_DIR, folder) if folder != "Profilbilder" else os.path.join(
            os.path.dirname(EMBLEM_DIR), "Profilbilder")
        if not os.path.isdir(path):
            return set()
        names = [os.path.splitext(n)[0] for n in os.listdir(path)
                 if os.path.splitext(n)[1].lower() in (".png", ".jpg", ".jpeg", ".gif", ".tga", ".psd")]
        return {emblem_key(n) for n in names} | {emblem_key(SWEDISH_COUNTRY.get(n.strip().lower(), n)) for n in names}

    def section(title, folder, counts, short_ok):
        found = have(folder)
        rows_missing, rows_done = [], []
        for name, n in sorted(counts.items(), key=lambda kv: (-kv[1], kv[0])):
            first = name.split(" ")[0]
            ok = emblem_key(name) in found or (short_ok and len(first) >= 3 and first.isupper()
                                               and emblem_key(first) in found)
            (rows_done if ok else rows_missing).append(f"  {name}.png   ({n} kort)")
        return [f"{title} ({len(rows_done)} av {len(counts)} har bild)", "", "Saknar bild:", *rows_missing, "",
                "Har bild:", *(rows_done or ["  (inga än)"]), "", ""]

    cards = [c for c in cards if c.get("inPacks", True)]
    countries = collections.Counter(c["country"].title() for c in cards if c.get("country"))
    clubs = collections.Counter(c["club"] for c in cards if c.get("club"))
    lines = [
        "Bilder till löparkorten: flaggor, klubbmärken och profilbilder",
        f"Uppdaterad {today.isoformat()} av eventor/compute_points.py. Listan skrivs om varje gång poängen räknas.",
        "",
        "Lägg flaggor i Emblem/Flaggor och klubbmärken i Emblem/Klubbar, med namnen nedan.",
        "Riktiga profilbilder läggs i Resources/Profilbilder och heter som löparen, t.ex. Tove Alexandersson.jpg.",
        "Använd bara foton som du har lov att använda. Löpare utan foto får en ritad basbild.",
        "Stora och små bokstäver, mellanslag och bindestreck spelar ingen roll.",
        "Flaggor får också heta på svenska (Danmark.png).",
        "Ett klubbmärke kan också heta som klubbens förkortning när den står först i namnet (IKHP.png).",
        "PNG med genomskinlig bakgrund blir finast. Kort som saknar bild visar namnet i text i stället.",
        "",
        "",
        *section("FLAGGOR", "Flaggor", countries, False),
        *section("KLUBBAR", "Klubbar", clubs, True),
        *photo_section(cards, have("Profilbilder")),
    ]
    os.makedirs(EMBLEM_DIR, exist_ok=True)
    with open(os.path.join(EMBLEM_DIR, "Bildlista.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


# ---------- main ----------

def main():
    today = dt.date.today()
    ids = Identities()
    races = load_races()
    all_groups = build_entries(races, ids)
    # Forest (long and middle) gives physics and technique, and decides who is in the game. Sprint is rated on its
    # own scale (VM and World Cup sprint finals = 100) and gives the cards' sprint speed (user 2026-11-13).
    groups = [g for g in all_groups if not g["sprint"]]
    sprint_groups = [g for g in all_groups if g["sprint"]]
    c100, adjusted, rating, iterations = solve(groups, today)
    via_physics = set()
    s_c100, s_adjusted, s_rating, s_iterations = (solve(sprint_groups, today, fallback=rating, via_fallback=via_physics)
                                                  if sprint_groups else ({}, set(), {}, 0))

    best = merits([(groups, adjusted, c100, rating), (sprint_groups, s_adjusted, s_c100, s_rating)], today)
    anchored_keys = {r["_key"] for g in groups if g["international"] for r in g["runners"]}
    all_keys = {r["_key"] for g in all_groups for r in g["runners"]}
    runners = []
    for key in all_keys:
        inf = ids.info[key]
        rt = rating.get(key)
        st = s_rating.get(key)
        n_all = sum(1 for g in groups for r in g["runners"] if r["_key"] == key
                    and (today - g["date"]).days <= WINDOW_DAYS)
        # Only forest races count here: a runner with sprints alone is saved but not in the game
        visible = bool(rt) and (rt[2] >= MIN_RACES_VISIBLE or key in anchored_keys)
        runners.append({
            "id": key, "name": inf["name"], "sex": inf["sex"], "birth_year": inf["birth_year"],
            "club": inf["club"], "country": inf["country"], "country_sure": inf["country_sure"],
            "physic": round(rt[0], 2) if rt else None,
            "technique": round(rt[1], 2) if rt else None,
            "adjusted_races": rt[2] if rt else 0,
            "all_races": n_all,
            # Sprint speed from clean sprint times, any number of races; sprint technique is kept for later
            "sprint_speed": round(st[0], 2) if st else None,
            "sprint_technique": round(st[1], 2) if st else None,
            "sprint_races": st[2] if st else 0,
            "international": key in anchored_keys,
            "visible": visible,
            "merits": best.get(key, []) if visible else [],
        })
    runners.sort(key=lambda r: (r["sex"] or "", -(r["physic"] or 0), -(r["sprint_speed"] or 0)))
    with open(OUT_JSON, "w", encoding="utf-8") as f:
        json.dump({"computed": today.isoformat(), "runners": runners}, f, ensure_ascii=False, indent=0)
    frozen = write_game_file(runners, today, all_groups, ids.merged)
    for ws_id, key in sorted(ids.merged.items()):
        print(f"Samma löpare utan mellannamn: {ws_id} -> {ids.info[key]['name']}")

    # report
    vis = [r for r in runners if r["visible"]]
    rep = [f"# Poäng {today}", "",
           f"Grupper (klass/bana per lopp): {len(groups)}, justerade: {len(adjusted)}, "
           f"ojusterade: {len(groups) - len(adjusted)}. Varv: {iterations}.",
           f"Löpare: {len(runners)}, med betyg: {sum(1 for r in runners if r['physic'] is not None)}, "
           f"synliga i spelet: {len(vis)} ({sum(1 for r in vis if r['sex'] == 'M')} herrar, "
           f"{sum(1 for r in vis if r['sex'] == 'F')} damer). Frysta kort (inte längre med i paketen): {frozen}.", ""]
    for sex, title in (("M", "Herrar"), ("F", "Damer")):
        rep += [f"## {title}, topp 25 (synliga)", "", "| # | Namn | Klubb/land | Fysik | Teknik | Lopp |", "| --- | --- | --- | --- | --- | --- |"]
        top = [r for r in vis if r["sex"] == sex][:25]
        for i, r in enumerate(top, 1):
            rep.append(f"| {i} | {r['name']} | {r['country'] or r['club'] or ''} | {r['physic']} | {r['technique']} | {r['adjusted_races']} |")
        rep.append("")
    rep += ["## Lopp", "", "| Datum | Tävling | Klass | Löpare | Justerad | c100 |", "| --- | --- | --- | --- | --- | --- |"]
    for g in sorted(groups, key=lambda g: (g["date"], g["event"], g["class"])):
        m, s = divmod(int(round(c100[g["id"]])), 60)
        rep.append(f"| {g['date']} | {g['event']} | {g['class']} | {len(g['runners'])} | "
                   f"{'ankare' if g['anchor'] else ('ja' if g['id'] in adjusted else 'ojusterad')} | {m}:{s:02d} |")
    rep.append("")
    with_sprint = [r for r in vis if r["sprint_speed"] is not None]
    rep += ["## Sprint", "",
            f"Sprintgrupper: {len(sprint_groups)}, justerade: {len(s_adjusted)}, ojusterade: {len(sprint_groups) - len(s_adjusted)}. "
            f"Varv: {s_iterations}. Löpare med sprintfart: {len(s_rating)}, varav i spelet: {len(with_sprint)} av {len(vis)} "
            f"(övriga visar - och springer med sin fysik).", ""]
    for sex, title in (("M", "Herrar"), ("F", "Damer")):
        rep += [f"### {title}, sprintfart topp 15 (synliga)", "", "| # | Namn | Speed | Fysik | Sprintlopp |", "| --- | --- | --- | --- | --- |"]
        top = sorted((r for r in with_sprint if r["sex"] == sex), key=lambda r: -r["sprint_speed"])[:15]
        for i, r in enumerate(top, 1):
            rep.append(f"| {i} | {r['name']} | {r['sprint_speed']} | {r['physic']} | {r['sprint_races']} |")
        rep.append("")
    rep += ["### Sprintlopp", "", "| Datum | Tävling | Klass | Löpare | Justerad | c100 |", "| --- | --- | --- | --- | --- | --- |"]
    for g in sorted(sprint_groups, key=lambda g: (g["date"], g["event"], g["class"])):
        m, s = divmod(int(round(s_c100[g["id"]])), 60)
        rep.append(f"| {g['date']} | {g['event']} | {g['class']} | {len(g['runners'])} | "
                   f"{'ankare' if g['anchor'] else ('via fysik' if g['id'] in via_physics and g['id'] in s_adjusted else 'ja' if g['id'] in s_adjusted else 'ojusterad')} | {m}:{s:02d} |")
    rep.append("")
    if ids.ambiguous:
        rep += ["## Tvetydiga namn i WinSplits-filer (granska)", ""]
        rep += [f"- {n} ({o}), {src}: {', '.join(c)}" for n, o, src, c in ids.ambiguous]
        rep.append("")
    unmatched = sorted({(ids.info[r['_key']]['name'], ids.info[r['_key']]['country'] or '') for g in all_groups if g["international"]
                        for r in g["runners"] if r["_key"].startswith("w:")})
    rep += [f"WinSplits-löpare utan Eventor-träff (oftast utländska): {len(unmatched)}.", ""]
    text = "\n".join(rep)
    with open(OUT_REPORT, "w", encoding="utf-8") as f:
        f.write(text + "\n")
    print(text)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as f:
            f.write(text + "\n")


if __name__ == "__main__":
    main()
