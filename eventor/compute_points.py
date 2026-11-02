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
  * VM/EM/VC races (WinSplits, level 6) are the anchor: best clean time = 100.
  * Other races: c100 = median of c * R / 100 over runners who already have a
    rating R (MIN_RACES_TO_CALIBRATE adjusted races, default 1, so juniors can
    chain in through juniors who also ran senior/international races).
    At least 3 such runners are needed.
  * Otherwise the race is "ojusterad": the best clean time gets 100 for display,
    but the race does not count. Everything is solved again until it stops
    changing, so a race can become adjusted through runners met later (or earlier).

Rating per runner, over the last 365 days:
  * weight = 0.5 ** (age_days / 180)
  * physic: Tukey biweight (c = 4.685, scale = max(1.4826 * MAD, 5)) times weight
  * technique: weighted mean
  * visible in the game with >= 5 adjusted races, or any VM/EM/VC race.

Merits (best races of the last 365 days, shown on the card close-up):
  * race score = 100 * c100 / finish time, mistakes included, only adjusted races.
    c100 already carries the strength of the field, so 20th against a strong field
    can beat a win against a weak one.
  * field = mean physic rating of the 5 best rated runners in that class.
"""

import collections
import datetime as dt
import glob
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
EMBLEM_DIR = os.path.join(os.path.dirname(HERE), "Assets", "Resources", "Emblem")

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


class Identities:
    """Eventor person id is the key. WinSplits runners are matched by name and sex."""

    def __init__(self):
        self.by_name = collections.defaultdict(set)      # (norm name, sex) -> eventor keys
        self.info = {}                                   # key -> dict
        self.ambiguous = []

    def add_eventor(self, r):
        key = f"e{r['pid']}" if r.get("pid") else f"n:{norm_name(r['name'])}:{r.get('sex')}"
        self.by_name[(norm_name(r["name"]), r.get("sex"))].add(key)
        inf = self.info.setdefault(key, {"name": r["name"], "sex": r.get("sex"), "birth_year": r.get("birth_year"),
                                         "club": None, "country": None})
        if r.get("club"):
            inf["club"] = r["club"]
        if r.get("country"):
            inf["country"] = r["country"]
        return key

    def match_winsplits(self, r, source):
        cands = self.by_name.get((norm_name(r["name"]), r.get("sex")), set())
        if len(cands) == 1:
            key = next(iter(cands))
        else:
            if len(cands) > 1:
                self.ambiguous.append((r["name"], r.get("country") or r.get("club"), source, sorted(cands)))
            key = f"w:{norm_name(r['name'])}:{r.get('sex')}"
        inf = self.info.setdefault(key, {"name": r["name"], "sex": r.get("sex"), "birth_year": None,
                                         "club": None, "country": None})
        if r.get("country"):
            inf["country"] = r["country"]
        if r.get("club") and not inf["club"]:
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


def analyse_group(runners):
    """runners: OK runners on the same course. Adds mistake time and clean time in place."""
    legs = [leg_times(r) for r in runners]
    n_legs = len(legs[0])
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


def load_races():
    races = []
    for p in sorted(glob.glob(os.path.join(RACES_DIR, "*.json"))):
        with open(p, encoding="utf-8") as f:
            races.append(json.load(f))
    return races


def build_entries(races, ids):
    """One entry per (race, class/course group) with OK runners and clean times."""
    eventor = [r for r in races if r.get("source") != "winsplits"]
    winsplits = [r for r in races if r.get("source") == "winsplits"]
    groups = []
    for race in eventor + winsplits:     # Eventor first so WinSplits can match to Eventor ids
        date = dt.date.fromisoformat(race["date"])
        is_ws = race.get("source") == "winsplits"
        anchor = is_ws and race.get("level") == 6
        for cls in race["classes"]:
            ok = [r for r in cls["runners"] if r["status"] == "OK" and r.get("time")]
            by_course = collections.defaultdict(list)
            for r in ok:
                by_course[tuple(s[0] for s in r["splits"])].append(r)
            for codes, rs in by_course.items():
                if len(rs) < MIN_GROUP or not codes:
                    continue
                rs = [dict(r) for r in rs]
                for r in rs:
                    r["sex"] = r.get("sex") or ("F" if cls["class"][:1] in "DW" else "M")
                    r["_key"] = ids.match_winsplits(r, race["event_name"]) if is_ws else ids.add_eventor(r)
                analyse_group(rs)
                groups.append({
                    "id": f"{race['event_id']}|{race.get('race_id', '')}|{cls['class']}|{len(groups)}",
                    "event": race["event_name"], "class": cls["class"], "date": date,
                    "anchor": anchor, "level": race.get("level"), "controls": len(codes),
                    "runners": rs, "class_ok": len(ok),
                })
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


def solve(groups, today):
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

def merits(groups, adjusted, c100, rating, today):
    """key -> the runner's best races of the last year, best first."""
    out = collections.defaultdict(list)
    for g in groups:
        if g["id"] not in adjusted or (today - g["date"]).days > WINDOW_DAYS:
            continue
        rated = sorted((rating[r["_key"]][0] for r in g["runners"] if r["_key"] in rating), reverse=True)
        field = sum(rated[:FIELD_TOP]) / len(rated[:FIELD_TOP]) if rated else 0.0
        by_time = sorted(g["runners"], key=lambda r: r["time"])
        for i, r in enumerate(by_time):
            place = int(r["position"]) if r.get("position") else i + 1
            out[r["_key"]].append({
                "race": g["event"], "date": g["date"].isoformat(), "klass": g["class"],
                "place": place, "of": max(g["class_ok"], place),
                "score": round(100.0 * c100[g["id"]] / r["time"], 2), "field": round(field, 2),
            })
    for key in out:
        out[key].sort(key=lambda m: (-m["score"], m["date"]))
        out[key] = out[key][:MERITS_PER_RUNNER]
    return out


# ---------- game file ----------

def country_name(r):
    c = (r.get("country") or "").strip()
    if not c:
        return "SWEDEN" if r.get("club") else ""
    return COUNTRIES.get(c.upper(), c.upper())


def game_class(r, today):
    prefix = "H" if r.get("sex") == "M" else "D"
    by = r.get("birth_year")
    age = today.year - by if by else 99
    return prefix + ("18" if age <= 18 else "20" if age <= 20 else "21")


def write_game_file(runners, today):
    """Cards for the game. Runners in packs get today's numbers. A runner who was a card
    before but is no longer eligible keeps the last numbers and leaves the packs (frozen)."""
    previous = {}
    if os.path.exists(GAME_JSON):
        with open(GAME_JSON, encoding="utf-8") as f:
            previous = {c["id"]: c for c in json.load(f).get("runners", [])}
    cards = []
    for r in runners:
        if not r["visible"]:
            continue
        cards.append({
            "id": r["id"], "name": r["name"], "man": r["sex"] == "M", "klass": game_class(r, today),
            "country": country_name(r), "club": r.get("club") or "",
            "speed": round(r["physic"], 2), "technique": round(r["technique"], 2), "inPacks": True,
            "merits": r.get("merits", []),
        })
    now = {c["id"] for c in cards}
    frozen = 0
    for cid, old in previous.items():
        if cid not in now:
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
    cards.sort(key=lambda c: (-(c["speed"] + c["technique"]), c["name"]))
    os.makedirs(os.path.dirname(GAME_JSON), exist_ok=True)
    with open(GAME_JSON, "w", encoding="utf-8") as f:
        json.dump({"computed": today.isoformat(), "runners": cards}, f, ensure_ascii=False, indent=0)
    write_emblem_list(cards, today)
    return frozen


def emblem_key(text):
    """Same rule as Emblems.Key in the game: lower case letters and digits only."""
    return "".join(ch for ch in text.lower() if ch.isalnum())


def photo_section(cards, found):
    """Runners in card order (best first), with or without a real photo."""
    missing, done = [], []
    for c in cards:
        base = c["name"].split(" (")[0]
        ok = emblem_key(c["name"]) in found or emblem_key(base) in found
        (done if ok else missing).append(f"  {c['name']}.jpg   ({c['klass']}, {c['speed']}/{c['technique']})")
    return [f"PROFILBILDER ({len(done)} av {len(cards)} har foto)", "", "Har foto:", *(done or ["  (inga än)"]), "",
            "Saknar foto, bästa löparna först:", *missing, ""]


def write_emblem_list(cards, today):
    """List every country and club on the cards, and whether its picture is in the folder yet."""
    def have(folder):
        path = os.path.join(EMBLEM_DIR, folder) if folder != "Profilbilder" else os.path.join(
            os.path.dirname(EMBLEM_DIR), "Profilbilder")
        if not os.path.isdir(path):
            return set()
        return {emblem_key(os.path.splitext(n)[0]) for n in os.listdir(path)
                if os.path.splitext(n)[1].lower() in (".png", ".jpg", ".jpeg", ".gif", ".tga", ".psd")}

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
        "Flaggor får också heta på svenska (Danmark.png), men då ser den här listan inte att de finns.",
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
    groups = build_entries(races, ids)
    c100, adjusted, rating, iterations = solve(groups, today)

    best = merits(groups, adjusted, c100, rating, today)
    anchored_keys = {r["_key"] for g in groups if g["anchor"] for r in g["runners"]}
    all_keys = {r["_key"] for g in groups for r in g["runners"]}
    runners = []
    for key in all_keys:
        inf = ids.info[key]
        rt = rating.get(key)
        n_all = sum(1 for g in groups for r in g["runners"] if r["_key"] == key
                    and (today - g["date"]).days <= WINDOW_DAYS)
        visible = bool(rt) and (rt[2] >= MIN_RACES_VISIBLE or key in anchored_keys)
        runners.append({
            "id": key, "name": inf["name"], "sex": inf["sex"], "birth_year": inf["birth_year"],
            "club": inf["club"], "country": inf["country"],
            "physic": round(rt[0], 2) if rt else None,
            "technique": round(rt[1], 2) if rt else None,
            "adjusted_races": rt[2] if rt else 0,
            "all_races": n_all,
            "international": key in anchored_keys,
            "visible": visible,
            "merits": best.get(key, []) if visible else [],
        })
    runners.sort(key=lambda r: (r["sex"] or "", -(r["physic"] or 0)))
    with open(OUT_JSON, "w", encoding="utf-8") as f:
        json.dump({"computed": today.isoformat(), "runners": runners}, f, ensure_ascii=False, indent=0)
    frozen = write_game_file(runners, today)

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
    if ids.ambiguous:
        rep += ["## Tvetydiga namn i WinSplits-filer (granska)", ""]
        rep += [f"- {n} ({o}), {src}: {', '.join(c)}" for n, o, src, c in ids.ambiguous]
        rep.append("")
    unmatched = sorted({(ids.info[r['_key']]['name'], ids.info[r['_key']]['country'] or '') for g in groups if g["anchor"]
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
