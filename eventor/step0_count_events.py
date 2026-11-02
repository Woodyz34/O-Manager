#!/usr/bin/env python3
"""Step 0: count the Eventor competitions the statistics job would read.

Makes exactly ONE call to Eventor (GET /api/events) for the past 365 days,
levels 1 (championship), 2 (national) and 6 (international, e.g. Swedish
World Cup rounds). Everything else is filtered locally, so the load on
Eventor is a single request.

The API key is read from the EVENTOR_API_KEY environment variable and is
never printed or written to disk.

Usage:
    EVENTOR_API_KEY=... python eventor/step0_count_events.py
    python eventor/step0_count_events.py --file saved_events.xml   # no API call

Output: a summary on stdout (also Markdown in $GITHUB_STEP_SUMMARY when run
in GitHub Actions) and the raw response saved to eventor/cache/ so later
steps can reuse it instead of calling Eventor again.
"""

import argparse
import datetime as dt
import os
import statistics
import sys
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict

BASE_URL = "https://eventor.orientering.se/api"
LEVELS = {1: "Nivå 1 (mästerskap)", 2: "Nivå 2 (nationell)", 6: "Nivå 6 (internationell)"}
FOOT_O = "1"                      # Eventor DisciplineId for orienteering running
INDIVIDUAL_FORMS = {"IndSingleDay", "IndMultiDay"}
WANTED_DISTANCES = {"Long", "Middle"}
CANCELLED_STATUS = "10"           # EventStatusId for a cancelled event


def local(tag):
    """Tag name without XML namespace."""
    return tag.rsplit("}", 1)[-1]


def child(elem, name):
    for c in elem:
        if local(c.tag) == name:
            return c
    return None


def children(elem, name):
    return [c for c in elem if local(c.tag) == name]


def text(elem, *path):
    """Text of a nested child, e.g. text(event, 'StartDate', 'Date')."""
    cur = elem
    for name in path:
        cur = child(cur, name) if cur is not None else None
    return (cur.text or "").strip() if cur is not None else ""


def fetch_events(api_key, from_date, to_date):
    params = urllib.parse.urlencode({
        "fromDate": from_date.strftime("%Y-%m-%d 00:00:00"),
        "toDate": to_date.strftime("%Y-%m-%d 23:59:59"),
        "classificationIds": ",".join(str(k) for k in LEVELS),
    })
    req = urllib.request.Request(
        f"{BASE_URL}/events?{params}",
        headers={"ApiKey": api_key, "User-Agent": "O-Manager-stats/0.1"},
    )
    with urllib.request.urlopen(req, timeout=120) as resp:
        return resp.read()


def analyse(xml_bytes):
    root = ET.fromstring(xml_bytes)
    events = [e for e in root.iter() if local(e.tag) == "Event"]

    per_level = defaultdict(Counter)       # level -> counters
    disciplines = Counter()
    forms = Counter()
    distances = Counter()
    statuses = Counter()
    weekly_calls = Counter()               # ISO week -> result calls (all levels)
    matching = []                          # (date, level, name, races)

    for ev in events:
        level = int(text(ev, "EventClassificationId") or 0)
        c = per_level[level]
        c["events"] += 1

        status = text(ev, "EventStatusId")
        statuses[status] += 1
        if status == CANCELLED_STATUS:
            c["cancelled"] += 1
            continue

        discs = {d.text.strip() for d in ev.iter() if local(d.tag) == "DisciplineId" and d.text}
        for d in discs:
            disciplines[d] += 1
        form = ev.get("eventForm", "IndSingleDay")
        forms[form] += 1
        if FOOT_O not in discs:
            continue
        c["foot_o"] += 1
        if form not in INDIVIDUAL_FORMS:
            continue
        c["individual"] += 1

        races = children(ev, "EventRace")
        good = []
        for r in races:
            dist = r.get("raceDistance", "Middle")
            distances[dist] += 1
            if dist in WANTED_DISTANCES:
                good.append(r)
        c["races_total"] += len(races)
        if not good:
            continue
        c["events_matching"] += 1
        c["races_matching"] += len(good)
        if any(r.get("raceLightCondition") in ("Night", "DayAndNight") for r in good):
            c["night_races"] += 1

        # One results call per matching race (per-stage call when only some
        # stages match, one call for the whole event otherwise).
        calls = 1 if len(good) == len(races) else len(good)
        c["result_calls"] += calls

        date_s = text(ev, "StartDate", "Date")
        try:
            d = dt.date.fromisoformat(date_s)
            weekly_calls[d.isocalendar()[:2]] += calls
        except ValueError:
            pass
        matching.append((date_s, level, text(ev, "Name"), len(good)))

    return {
        "events": len(events), "per_level": per_level, "disciplines": disciplines,
        "forms": forms, "distances": distances, "statuses": statuses,
        "weekly_calls": weekly_calls, "matching": sorted(matching),
    }


def report(a, size_bytes, from_date, to_date, source):
    out = []
    p = out.append
    p(f"# Steg 0: Eventor {from_date} till {to_date}")
    p("")
    p(f"{source} Svaret var {size_bytes / 1e6:.2f} MB med {a['events']} tävlingar på nivå 1, 2 och 6.")
    p("")
    p("| Nivå | Tävlingar | Inställda | Fot-O | Individuella | Med lång/medel | Etapper lång/medel | Resultatanrop |")
    p("| --- | --- | --- | --- | --- | --- | --- | --- |")
    tot = Counter()
    for lvl in sorted(a["per_level"]):
        c = a["per_level"][lvl]
        tot.update(c)
        p(f"| {LEVELS.get(lvl, lvl)} | {c['events']} | {c['cancelled']} | {c['foot_o']} | {c['individual']} | "
          f"{c['events_matching']} | {c['races_matching']} | {c['result_calls']} |")
    p(f"| **Totalt** | {tot['events']} | {tot['cancelled']} | {tot['foot_o']} | {tot['individual']} | "
      f"{tot['events_matching']} | {tot['races_matching']} | {tot['result_calls']} |")
    p("")

    lvl1 = a["per_level"].get(1, Counter())["result_calls"]
    lvl6 = a["per_level"].get(6, Counter())["result_calls"]
    p("## Vad det betyder")
    p("")
    p(f"- **Engångsjobbet (bara nivå 1):** {lvl1 + 1} anrop (1 lista + {lvl1} resultat).")
    p(f"- **Med nivå 6 också (svenska världscuplopp):** {lvl1 + lvl6 + 1} anrop.")
    weeks = a["weekly_calls"]
    if weeks:
        vals = list(weeks.values())
        all_weeks = 52
        p(f"- **Veckojobbet (nivå 1, 2, 6):** i snitt {sum(vals) / all_weeks + 1:.1f} anrop per vecka, "
          f"högst {max(vals) + 1} en enskild vecka, median bland veckor med tävling {statistics.median(vals) + 1:.0f} "
          f"(alla siffror inklusive listanropet). {all_weeks - len(vals)} veckor saknade tävlingar.")
    p("")
    p(f"Natt-etapper bland träffarna: {tot['night_races']}.")
    p("")
    p("## Kontroll av filtren")
    p("")
    p(f"- DisciplineId: {dict(a['disciplines'])} (1 bör vara orienteringslöpning)")
    p(f"- eventForm: {dict(a['forms'])}")
    p(f"- raceDistance: {dict(a['distances'])}")
    p(f"- EventStatusId: {dict(a['statuses'])} (10 = inställd)")
    p("")
    p("## Tävlingar som skulle läsas")
    p("")
    p("| Datum | Nivå | Tävling | Etapper |")
    p("| --- | --- | --- | --- |")
    for date_s, lvl, name, n in a["matching"]:
        p(f"| {date_s} | {lvl} | {name} | {n} |")
    return "\n".join(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--file", help="Read a saved /api/events response instead of calling Eventor")
    ap.add_argument("--days", type=int, default=365)
    args = ap.parse_args()

    to_date = dt.date.today()
    from_date = to_date - dt.timedelta(days=args.days)

    if args.file:
        source = f"Läste den sparade filen {os.path.basename(args.file)} (inget anrop till Eventor)."
        with open(args.file, "rb") as f:
            data = f.read()
    else:
        key = os.environ.get("EVENTOR_API_KEY", "").strip()
        if not key:
            sys.exit("EVENTOR_API_KEY saknas. Sätt den som miljövariabel eller GitHub-hemlighet.")
        source = "Ett anrop till Eventor gjordes."
        data = fetch_events(key, from_date, to_date)
        cache_dir = os.path.join(os.path.dirname(__file__), "cache")
        os.makedirs(cache_dir, exist_ok=True)
        with open(os.path.join(cache_dir, f"events_{from_date}_{to_date}.xml"), "wb") as f:
            f.write(data)

    text_report = report(analyse(data), len(data), from_date, to_date, source)
    print(text_report)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as f:
            f.write(text_report + "\n")


if __name__ == "__main__":
    main()
