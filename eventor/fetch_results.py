#!/usr/bin/env python3
"""Fetch Eventor results with split times and store the classes the game uses.

Modes
  once  Past 365 days: level 1 without DM (district championships), events
        Eventor returns without a level (Swedish League / WRE), and level 2
        events whose name contains a line of eventor/extra_events.txt.
  week  Past 7 days: levels 1 (including DM), 2, 6 and events without a level.

Load on Eventor
  1 call for the event list, then 1 call per race that is foot-O,
  individual and long or middle distance. Races already stored in
  eventor/data/races/ are skipped, so a re-run does not call again.
  The script waits --pause seconds (default 20) between calls.

Output
  eventor/data/races/<eventId>_<eventRaceId>.json   compact results, only
      classes with a standalone 21, 20 or 18 in the name (H21, D20, M21E1,
      H21 Motion, ...) plus Men/Women/Herrar/Damer.
  eventor/data/unknown_classes.txt   class names that matched neither the
      game classes nor the usual age-class pattern, for review by hand.
  eventor/cache/  raw XML responses (not committed, uploaded as an artifact).

The API key is read from EVENTOR_API_KEY and is never printed or stored.

Offline testing (no API calls):
  python eventor/fetch_results.py --mode once --events-file list.xml \
      --results-dir saved_results/   # files named <eventId>[_<eventRaceId>].xml
"""

import argparse
import datetime as dt
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

BASE_URL = "https://eventor.orientering.se/api"
HERE = os.path.dirname(os.path.abspath(__file__))
DATA_DIR = os.path.join(HERE, "data")
RACES_DIR = os.path.join(DATA_DIR, "races")
CACHE_DIR = os.path.join(HERE, "cache")

PARSER_VERSION = 2               # bump to rebuild stored races from cached raw XML
FOOT_O = "1"
INDIVIDUAL_FORMS = {"IndSingleDay", "IndMultiDay"}
WANTED_DISTANCES = {"Long", "Middle"}
CANCELLED_STATUS = "10"

EXTRA_EVENTS_FILE = os.path.join(HERE, "extra_events.txt")
DM_RE = re.compile(r"(^|[^A-Za-zÅÄÖåäö])DM([^A-Za-zÅÄÖåäö]|$)|distriktsmästerskap", re.I)
GAME_CLASS_RE = re.compile(r"(?<!\d)(21|20|18)(?!\d)|^(men|women|herrar|damer)\b", re.I)
NO_GAME_CLASSES_RE = re.compile(r"ungdoms-?sm|veteran-?sm|(^|\W)usm(\W|$)", re.I)
AGE_CLASS_RE = re.compile(r"^\s*(H|D|M|W|HD|DH|D/H|H/D)\s*\d", re.I)


# ---------- small XML helpers (namespace-agnostic) ----------

def local(tag):
    return tag.rsplit("}", 1)[-1]


def child(elem, name):
    if elem is None:
        return None
    for c in elem:
        if local(c.tag) == name:
            return c
    return None


def children(elem, name):
    return [] if elem is None else [c for c in elem if local(c.tag) == name]


def text(elem, *path):
    cur = elem
    for name in path:
        cur = child(cur, name)
    return (cur.text or "").strip() if cur is not None and cur.text else ""


def num(s):
    try:
        return float(s)
    except (TypeError, ValueError):
        return None


# ---------- Eventor calls ----------

class Eventor:
    def __init__(self, key, pause):
        self.key = key
        self.pause = pause
        self.calls = 0

    def get(self, path, params):
        if self.calls:
            time.sleep(self.pause)
        self.calls += 1
        print(f"Anrop {self.calls}: {path} {params.get('eventId', '')} {params.get('eventRaceId', '')}".rstrip(), flush=True)
        url = f"{BASE_URL}/{path}?{urllib.parse.urlencode(params)}"
        req = urllib.request.Request(url, headers={"ApiKey": self.key, "User-Agent": "O-Manager-stats/0.2"})
        try:
            with urllib.request.urlopen(req, timeout=300) as resp:
                return resp.read()
        except urllib.error.HTTPError as e:
            if e.code < 500:
                raise
            print(f"Serverfel {e.code}, försöker igen om 60 s (samma anrop).", flush=True)
            time.sleep(60)
            with urllib.request.urlopen(req, timeout=300) as resp:
                return resp.read()


def load_raw(name):
    """A raw response saved by an earlier run (eventor/cache, restored from artifacts in Actions)."""
    path = os.path.join(CACHE_DIR, name)
    if os.path.exists(path):
        with open(path, "rb") as f:
            return f.read()
    return None


def needs_reparse(out_path, race):
    """Rebuild an older JSON file when its raw response is still in the cache (no API call)."""
    with open(out_path, encoding="utf-8") as f:
        old = json.load(f).get("parser_version", 1)
    return old < PARSER_VERSION and load_raw(f"results_{race['event_id']}_{race['race_id']}.xml") is not None


def save_raw(name, data):
    os.makedirs(CACHE_DIR, exist_ok=True)
    with open(os.path.join(CACHE_DIR, name), "wb") as f:
        f.write(data)


# ---------- choosing races ----------

def load_extra_events():
    if not os.path.exists(EXTRA_EVENTS_FILE):
        return []
    with open(EXTRA_EVENTS_FILE, encoding="utf-8") as f:
        return [l.strip().lower() for l in f if l.strip() and not l.lstrip().startswith("#")]


def select_races(events_xml, mode, extra=()):
    """Return a list of dicts, one per race to fetch, plus a list of skipped events with reasons."""
    root = ET.fromstring(events_xml)
    picked, skipped = [], []
    for ev in (e for e in root.iter() if local(e.tag) == "Event"):
        name = text(ev, "Name")
        level = int(text(ev, "EventClassificationId") or 0)
        event_id = text(ev, "EventId")

        def skip(reason):
            skipped.append((text(ev, "StartDate", "Date"), level, name, reason))

        if text(ev, "EventStatusId") == CANCELLED_STATUS:
            skip("inställd"); continue
        discs = {d.text.strip() for d in ev.iter() if local(d.tag) == "DisciplineId" and d.text}
        if discs != {FOOT_O}:
            skip(f"gren {sorted(discs)}"); continue
        if ev.get("eventForm", "IndSingleDay") not in INDIVIDUAL_FORMS:
            skip(ev.get("eventForm")); continue
        if mode == "once":
            if level not in (0, 1) and not any(x in name.lower() for x in extra):
                continue                      # level 2 back in time only for the chosen events
            if DM_RE.search(name):
                skip("DM"); continue
            if NO_GAME_CLASSES_RE.search(name):
                skip("inga klasser 18/20/21"); continue

        races = children(ev, "EventRace")
        good = [r for r in races if r.get("raceDistance", "Middle") in WANTED_DISTANCES]
        if not good:
            skip("ingen lång/medel"); continue
        for r in good:
            picked.append({
                "race_number": races.index(r) + 1,
                "event_id": event_id,
                "race_id": text(r, "EventRaceId"),
                "single_race": len(races) == 1,
                "event_name": name,
                "race_name": text(r, "Name"),
                "date": text(r, "RaceDate", "Date") or text(ev, "StartDate", "Date"),
                "level": level,
                "distance": r.get("raceDistance", "Middle"),
                "light": r.get("raceLightCondition", "Day"),
            })
    return picked, skipped


# ---------- parsing IOF XML 3.0 results ----------

def person_name(person):
    n = child(person, "Name")
    return " ".join(x for x in (text(n, "Given"), text(n, "Family")) if x)


def result_number(r):
    rn = r.get("raceNumber") or text(r, "RaceNumber")
    return int(rn) if rn.isdigit() else None


def pick_result(results, race):
    """The Result element for this race. Eventor can return the whole multi-race
    event even when one race is asked for, so pick by raceNumber."""
    if not results:
        return None
    numbered = [(result_number(r), r) for r in results]
    if any(n is not None for n, _ in numbered):
        for n, r in numbered:
            if n == race["race_number"]:
                return r
        return None
    if len(results) == 1:
        return results[0]
    i = race["race_number"] - 1
    return results[i] if i < len(results) else None


def parse_results(xml_bytes, race):
    """Compact results for the game classes. Returns (classes, unknown_class_names, diagnostics)."""
    root = ET.fromstring(xml_bytes)
    out, unknown = [], set()
    diag = {"results_per_person": {}, "race_numbers": {}}
    for cr in (e for e in root.iter() if local(e.tag) == "ClassResult"):
        cls = child(cr, "Class")
        cname = text(cls, "Name") or text(cls, "ShortName")
        if not GAME_CLASS_RE.search(cname):
            if not AGE_CLASS_RE.match(cname):
                unknown.add(cname)
            continue
        course = child(cr, "Course")
        runners = []
        for pr in children(cr, "PersonResult"):
            person = child(pr, "Person")
            results = children(pr, "Result")
            k = str(len(results))
            diag["results_per_person"][k] = diag["results_per_person"].get(k, 0) + 1
            for r in results:
                n = str(result_number(r))
                diag["race_numbers"][n] = diag["race_numbers"].get(n, 0) + 1
            res = pick_result(results, race)
            if res is None:
                continue
            rc = child(res, "Course")
            if rc is None:
                rc = course
            birth = text(person, "BirthDate")
            org = child(pr, "Organisation")
            country = child(org, "Country")
            sex = (person.get("sex") if person is not None else None) or (cls.get("sex") if cls is not None else None)
            runners.append({
                "pid": text(person, "Id"),
                "name": person_name(person),
                "sex": sex,
                "birth_year": int(birth[:4]) if birth[:4].isdigit() else None,
                "club": text(org, "Name"),
                "country": country.get("code") if country is not None else None,
                "status": text(res, "Status") or "OK",
                "time": num(text(res, "Time")),
                "position": num(text(res, "Position")),
                "splits": [[text(s, "ControlCode"), num(text(s, "Time")), s.get("status", "OK")]
                           for s in children(res, "SplitTime")],
                "course_length": num(text(rc, "Length")),
                "controls": num(text(rc, "NumberOfControls")),
            })
        if not runners:
            continue
        out.append({
            "class": cname,
            "course_length": num(text(course, "Length")),
            "controls": num(text(course, "NumberOfControls")),
            "runners": runners,
        })
    return out, unknown, diag


# ---------- main ----------

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--mode", choices=["once", "week"], required=True)
    ap.add_argument("--pause", type=float, default=20, help="seconds between Eventor calls")
    ap.add_argument("--dry-run", action="store_true", help="only list the races, fetch no results")
    ap.add_argument("--events-file", help="offline: saved /api/events response")
    ap.add_argument("--results-dir", help="offline: folder with saved iofxml results")
    args = ap.parse_args()

    today = dt.date.today()
    days = 365 if args.mode == "once" else 7
    from_date = today - dt.timedelta(days=days)
    levels = "1,2,6"   # events without a level (Swedish League/WRE) came back for this query in step 0

    api = None
    if not (args.events_file and args.results_dir):
        key = os.environ.get("EVENTOR_API_KEY", "").strip()
        if not key:
            sys.exit("EVENTOR_API_KEY saknas. Sätt den som miljövariabel eller GitHub-hemlighet.")
        api = Eventor(key, args.pause)

    cached_list = None
    if args.mode == "once" and os.path.isdir(CACHE_DIR):
        lists = sorted(n for n in os.listdir(CACHE_DIR) if n.startswith("events_once_"))
        cached_list = load_raw(lists[-1]) if lists else None
    if args.events_file:
        with open(args.events_file, "rb") as f:
            events_xml = f.read()
    elif cached_list is not None:
        print(f"Använder sparad tävlingslista {lists[-1]} (inget anrop).", flush=True)
        events_xml = cached_list
    else:
        events_xml = api.get("events", {
            "fromDate": from_date.strftime("%Y-%m-%d 00:00:00"),
            "toDate": today.strftime("%Y-%m-%d 23:59:59"),
            "classificationIds": levels,
        })
        save_raw(f"events_{args.mode}_{from_date}_{today}.xml", events_xml)

    races, skipped = select_races(events_xml, args.mode, load_extra_events())
    os.makedirs(RACES_DIR, exist_ok=True)

    report = [f"# Eventor {args.mode}: {from_date} till {today}", ""]
    fetched, cached, failed = 0, 0, []
    all_unknown = set()
    rows = []
    for race in sorted(races, key=lambda r: (r["date"], r["event_id"], r["race_id"])):
        out_path = os.path.join(RACES_DIR, f"{race['event_id']}_{race['race_id']}.json")
        label = f"{race['event_name']}" + (f" ({race['race_name']})" if race["race_name"] and not race["single_race"] else "")
        if os.path.exists(out_path) and not needs_reparse(out_path, race):
            cached += 1
            rows.append((race["date"], race["level"], label, "fanns redan", ""))
            continue
        if args.dry_run:
            rows.append((race["date"], race["level"], label, "skulle hämtas", ""))
            continue
        try:
            if args.results_dir:
                base = race["event_id"] if race["single_race"] else f"{race['event_id']}_{race['race_id']}"
                with open(os.path.join(args.results_dir, base + ".xml"), "rb") as f:
                    xml = f.read()
            else:
                raw_name = f"results_{race['event_id']}_{race['race_id']}.xml"
                xml = load_raw(raw_name)
                if xml is None:
                    params = {"eventId": race["event_id"], "includeSplitTimes": "true"}
                    if not race["single_race"]:
                        params["eventRaceId"] = race["race_id"]
                    xml = api.get("results/event/iofxml", params)
                    save_raw(raw_name, xml)
                else:
                    print(f"Använder sparat svar {raw_name} (inget anrop).", flush=True)
            classes, unknown, diag = parse_results(xml, race)
        except Exception as e:  # keep going; report at the end
            failed.append((label, f"{type(e).__name__}: {e}"))
            rows.append((race["date"], race["level"], label, "FEL", ""))
            continue
        all_unknown |= unknown
        n = sum(len(c["runners"]) for c in classes)
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump({**race, "parser_version": PARSER_VERSION, "fetched": today.isoformat(),
                       "diagnostics": diag, "classes": classes}, f, ensure_ascii=False, separators=(",", ":"))
        fetched += 1
        extra = "" if race["single_race"] else f" (resultat/person {diag['results_per_person']}, raceNumber {diag['race_numbers']})"
        rows.append((race["date"], race["level"], label, "hämtad", f"{len(classes)} klasser, {n} löpare{extra}"))

    if all_unknown:
        path = os.path.join(DATA_DIR, "unknown_classes.txt")
        old = set()
        if os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                old = {l.strip() for l in f if l.strip()}
        with open(path, "w", encoding="utf-8") as f:
            f.write("\n".join(sorted(old | all_unknown)) + "\n")

    calls = api.calls if api else 0
    report += [
        f"Anrop till Eventor: **{calls}**. Hämtade etapper: {fetched}. Fanns redan: {cached}. Fel: {len(failed)}.",
        "",
        "| Datum | Nivå | Tävling | Status | Innehåll |",
        "| --- | --- | --- | --- | --- |",
        *[f"| {d} | {l} | {n} | {s} | {c} |" for d, l, n, s, c in rows],
        "",
    ]
    if failed:
        report += ["## Fel", ""] + [f"- {n}: {e}" for n, e in failed] + [""]
    if all_unknown:
        report += ["## Okända klassnamn (inte sparade)", "", ", ".join(sorted(all_unknown)), ""]
    if args.mode == "once":
        dm = [s for s in skipped if s[3] == "DM"]
        other = [s for s in skipped if s[3] != "DM" and s[1] in (0, 1)]
        report += [f"Hoppade över {len(dm)} DM-tävlingar.", ""]
        if other:
            report += ["## Andra överhoppade på nivå 0 och 1", ""] + [f"- {d} {n}: {r}" for d, l, n, r in other] + [""]

    text_report = "\n".join(report)
    print(text_report)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as f:
            f.write(text_report + "\n")
    if failed and not fetched:
        sys.exit(1)


if __name__ == "__main__":
    main()
