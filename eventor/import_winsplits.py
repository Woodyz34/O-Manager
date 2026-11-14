#!/usr/bin/env python3
"""Import WinSplits txt exports (WC/EC/WCh and chosen level 2 races).

Put the files in eventor/winsplits/ named

    YYYY-MM-DD_<VM|EM|VC|niva2>[-quali<n>]_<lang|medel|sprint>_<class>[_kval][_race name].txt

(nivå2 works as niva2. A qualification is "_kval" after the class, "VM-quali", "VM-quali2" ... in the kind, or
for a national race the word "kval" in its name, e.g. "SM, sprint (Swedish League, #4), kval". VM files name
the federation, "Swiss Orienteering", which becomes the country. "[14896] Heming O" becomes "Heming O".)

The class is H or D (meaning H21/D21), or the whole class name, e.g. D20, H18 or H21Kort.
Two classes of the same race need different file names, and the class tells them apart:
2026-07-19_niva2_medel_D20_O-Ringen_E1.txt and 2026-07-19_niva2_medel_D_O-Ringen_E1.txt.

for example 2026-07-14_VM_medel_H.txt, 2026-05-30_VC_lang_D_kval.txt or
2026-07-19_niva2_medel_H_O-Ringen_E1.txt. The race name in the game is in English
("World Championships Middle"); the free text at the end, with _ as spaces, is added to it,
and for niva2 it is the race's own name ("O-Ringen E1, Middle").
Each file holds one class. The importer writes one JSON file per txt file to
eventor/data/races/ws_<file name>.json, in the same shape as the Eventor
data, so the points step can treat both sources alike. Files that are
already imported and unchanged are skipped.

No Eventor calls are made. Runners are matched to Eventor runners later, in
the points step (name + country/club).

    python eventor/import_winsplits.py
"""

import hashlib
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
IN_DIR = os.path.join(HERE, "winsplits")
OUT_DIR = os.path.join(HERE, "data", "races")

NAME_RE = re.compile(
    r"^(?P<date>\d{4}-\d{2}-\d{2})_(?P<kind>VM|EM|VC|niva2|nivå2)(?:-(?P<quali>quali|kval)(?P<heat>\d*))?"
    r"_(?P<dist>lang|lång|long|medel|mellan|middle|sprint)_(?P<sex>[HD])(?P<age>\d{2}[A-Za-zÅÄÖåäö0-9]*)?"
    r"(?P<kval>_kval)?(?:_(?P<free>.*))?\.txt$", re.I)
IMPORTER_VERSION = 6          # bump to re-import every file after a parser change
MIDDLE = ("medel", "mellan", "middle")
# "kval" as a word in the free text of a national race (e.g. "SM, sprint (Swedish League, #4), kval")
KVAL_WORD = re.compile(r"(?<![a-zåäö])(kval|quali\w*)(?![a-zåäö])", re.I)
# VM files name the national federation instead of the country
FEDERATIONS = {
    "paraguaya": "Paraguay", "austrian": "Austria", "belgian": "Belgium", "brazilian": "Brazil",
    "british": "Great Britain", "bulgarian": "Bulgaria", "chinese": "China", "colombian": "Colombia",
    "croatian": "Croatia", "czech": "Czechia", "danish": "Denmark", "ecuadorian": "Ecuador", "estonian": "Estonia",
    "finnish": "Finland", "french": "France", "hungarian": "Hungary", "iraq": "Iraq", "israel": "Israel",
    "italian": "Italy", "japanese": "Japan", "latvian": "Latvia", "lithuanian": "Lithuania",
    "macedonian": "North Macedonia", "moldavian": "Moldova", "nederlandse": "Netherlands", "dutch": "Netherlands",
    "norwegian": "Norway", "mexico": "Mexico", "hong kong": "Hong Kong China", "australia": "Australia",
    "canada": "Canada", "germany": "Germany", "german": "Germany", "ireland": "Ireland", "irish": "Ireland",
    "new zealand": "New Zealand", "usa": "United States", "polish": "Poland", "portuguese": "Portugal",
    "romanian": "Romania", "serbian": "Serbia", "slovak": "Slovakia", "slovenian": "Slovenia", "spanish": "Spain",
    "swedish": "Sweden", "swiss": "Switzerland", "egyptian": "Egypt", "turkiye": "Turkey", "turkish": "Turkey",
    "ukrainian": "Ukraine", "russian": "Russia", "korea": "South Korea", "south african": "South Africa",
}


def country_of(org):
    """'Swiss Orienteering' -> 'Switzerland'; a plain country name ('Sweden') stays as it is."""
    low = (org or "").lower()
    if "orient" not in low and "association" not in low and "federation" not in low:
        return org
    for word, land in FEDERATIONS.items():
        if re.search(r"(?<![a-z])" + re.escape(word) + r"(?![a-z])", low):
            return land
    return org


def clean_club(org):
    """Exports from other countries' Eventor put the club id first: '[14896] Heming O' -> 'Heming O'."""
    return re.sub(r"^\[\d+\]\s*", "", org or "").strip()
KIND_LEVEL = {"vm": 6, "em": 6, "vc": 6, "niva2": 2}
KIND_NAME = {"vm": "World Championships", "em": "European Championships", "vc": "World Cup", "niva2": "National race"}
STATUS = {"mp": "MissingPunch", "dns": "DidNotStart", "dnf": "DidNotFinish",
          "dsq": "Disqualified", "-": "NotFinished", "": "NotFinished",
          # Swedish WinSplits exports
          "felst": "MissingPunch", "felst.": "MissingPunch", "ej start": "DidNotStart",
          "utg": "DidNotFinish", "utg.": "DidNotFinish", "brutit": "DidNotFinish", "disk": "Disqualified"}
LEG_RE = re.compile(r"^(S|\d+)-(\d+|F|M)(?:\s*\((\w+)\))?$")      # F = finish, M = mål in Swedish files


def parse_time(s):
    """'36.22' -> 2182, '1:16.30' -> 4590, '31.50.80' -> 1910.8, '' -> None."""
    s = s.strip().lstrip("+")
    m = re.fullmatch(r"(?:(\d+):)?(\d+)\.(\d{2})(?:[.,](\d{1,3}))?", s)
    if not m:
        return None
    h, mi, se = int(m.group(1) or 0), int(m.group(2)), int(m.group(3))
    frac = float("0." + m.group(4)) if m.group(4) else 0
    return round(h * 3600 + mi * 60 + se + frac, 2)


def parse_file(text):
    lines = [l.rstrip("\r\n") for l in text.splitlines()]
    while lines and not lines[-1].strip():
        lines.pop()
    header = lines[0].split("\t")
    if not header or header[0].strip() not in ("Pos", "Pl"):
        raise ValueError("första raden ska börja med Pos eller Pl")
    legs = []                                 # (column, control code or None for finish)
    for col, h in enumerate(header):
        m = LEG_RE.match(h.strip())
        if m:
            legs.append((col, None if m.group(2) in ("F", "M") else (m.group(3) or m.group(2))))
    if not legs or legs[-1][1] is not None:
        raise ValueError("hittar inga sträckor S-1 ... -F i rubriken")
    controls = len(legs) - 1
    # The finish time column ("Finish time" / "Sluttid"). Some exports have none; then the
    # cumulative time at the finish on the runner's second row is the finish time.
    names = [h.strip().lower() for h in header]
    finish_col = next((names.index(n) for n in ("finish time", "sluttid", "tid", "time") if n in names), None)

    runners = []
    rows = lines[2:]                          # line 2 is the Club header
    for i in range(0, len(rows) - 1, 2):
        r1, r2 = rows[i].split("\t"), rows[i + 1].split("\t")
        if len(r1) < 3 or not r1[1].strip():
            continue
        cell = lambda r, c: r[c].strip() if c < len(r) else ""
        pos, name = cell(r1, 0), cell(r1, 1)
        finish = cell(r1, finish_col) if finish_col is not None else cell(r2, legs[-1][0])
        org = cell(r2, 1)
        t = parse_time(finish)
        status = "OK" if pos.isdigit() and t is not None else STATUS.get(finish.lower(), "NotFinished")
        splits = []
        for col, code in legs[:-1]:
            cum = parse_time(cell(r2, col))
            splits.append([code, cum, "OK" if cum is not None else "Missing"])
        runners.append({
            "pid": None, "name": name, "sex": None, "birth_year": None,
            "club": org, "country": None,
            "status": status, "time": t if status == "OK" else None,
            "position": int(pos) if pos.isdigit() else None,
            "splits": splits, "course_length": None, "controls": controls,
        })
    return controls, runners


def distance_of(dist):
    d = dist.lower()
    return "Sprint" if d == "sprint" else "Middle" if d in MIDDLE else "Long"


def race_name(kind, dist, kval, free, heat=""):
    """The name shown in the game, in English: "World Cup Long", "European Championships Middle
    Qualification (final A)", "World Championships Sprint Qualification 2", or for a national race its
    own name, "O-Ringen E1, Middle" (a name that already says sprint gets no ", Sprint")."""
    distance = distance_of(dist)
    extra = " ".join((free or "").replace("_", " ").split())
    if kind == "niva2" and extra:
        if distance.lower() in extra.lower():
            return extra
        name = f"{extra}, {distance}"
        return name + (" Qualification" if kval and not KVAL_WORD.search(extra) else "")
    name = f"{KIND_NAME[kind]} {distance}" + (f" Qualification{(' ' + heat) if heat else ''}" if kval else "")
    return name + (f" ({extra})" if extra else "")


def main():
    if not os.path.isdir(IN_DIR):
        print("Mappen eventor/winsplits finns inte, inget att importera.")
        return
    os.makedirs(OUT_DIR, exist_ok=True)
    report, errors = [], []
    for fname in sorted(os.listdir(IN_DIR)):
        if not fname.lower().endswith(".txt"):
            continue
        m = NAME_RE.match(fname)
        if not m:
            errors.append(f"{fname}: namnet följer inte mönstret ÅÅÅÅ-MM-DD_VM|EM|VC|niva2[-quali]_lang|medel|sprint_H|D|klass[_kval][_namn].txt")
            continue
        with open(os.path.join(IN_DIR, fname), "rb") as f:
            raw = f.read()
        digest = hashlib.sha1(raw).hexdigest()
        stem = fname[:-4]
        out_path = os.path.join(OUT_DIR, f"ws_{stem}.json")
        if os.path.exists(out_path):
            with open(out_path, encoding="utf-8") as f:
                old = json.load(f)
                if old.get("source_sha1") == digest and old.get("importer_version") == IMPORTER_VERSION:
                    report.append(f"| {fname} | fanns redan | |")
                    continue
        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError:
            text = raw.decode("cp1252")
        try:
            controls, runners = parse_file(text)
        except Exception as e:
            errors.append(f"{fname}: {e}")
            continue
        kind, sex = m.group("kind").lower().replace("å", "a"), m.group("sex").upper()
        international = kind != "niva2"
        # Qualification: "_kval" after the class, "VM-quali2" in the kind, or "kval" in a national race's name
        kval = bool(m.group("kval") or m.group("quali") or (kind == "niva2" and KVAL_WORD.search(m.group("free") or "")))
        # Plain H or D is the top class: H21/D21 at VM, EM and World Cup, H21 Elit/D21 Elit at a
        # national race, where H21 and D21 are other classes
        klass = sex + (m.group("age") or ("21" if international else "21 Elit"))
        for r in runners:
            r["sex"] = "M" if sex == "H" else "F"
            if international:
                r["country"], r["club"] = country_of(r["club"]), None
            else:
                r["club"] = clean_club(r["club"])
        data = {
            "source": "winsplits", "source_file": fname, "source_sha1": digest,
            "importer_version": IMPORTER_VERSION,
            "event_id": f"ws:{stem}", "race_id": "", "single_race": True,
            "event_name": race_name(kind, m.group("dist"), kval, m.group("free"), m.group("heat") or ""),
            "race_name": "", "date": m.group("date"), "level": KIND_LEVEL[kind],
            "distance": distance_of(m.group("dist")),
            "light": "Day", "qualification": kval,
            "classes": [{"class": klass, "course_length": None, "controls": controls, "runners": runners}],
        }
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
        ok = sum(r["status"] == "OK" for r in runners)
        if not ok:
            errors.append(f"{fname}: ingen godkänd löpare hittades, kontrollera formatet")
        report.append(f"| {fname} | importerad | {len(runners)} löpare ({ok} godkända), {controls} kontroller |")

    out = ["# WinSplits-import", "", "| Fil | Status | Innehåll |", "| --- | --- | --- |", *report, ""]
    if errors:
        out += ["## Fel", ""] + [f"- {e}" for e in errors] + [""]
    text = "\n".join(out)
    print(text)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as f:
            f.write(text + "\n")
    if errors:
        sys.exit(1)


if __name__ == "__main__":
    main()
