#!/usr/bin/env python3
"""Extracts the documented object *properties* out of the R2021b dump into a checked-in CSV.

This is the second axis ADR 0069 named and the first thing M69 left unmeasured. ``set(h, 'Prop', v)``
and ``get(h, 'Prop')`` are how ported code configures a figure, so a missing property is a script
that stops — and nothing in the repository knew which ones existed. The dump has carried them all
along under the ``property`` kind, spelled ``Class.Property``.

Output: ``matlab-r2021b-properties.csv`` — one row per documented property, ``class,property,summary``.
It covers **every** documented class, not only the graphics ones, so the table stays stable if a
later milestone measures a different family; ``probe-properties.py`` picks the classes it can build.

Usage:
    python build-properties-csv.py <dump.html> [--out <tools/matlab-checklist>]

Re-run it only when the dump itself is replaced.
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
from pathlib import Path

F_NAME, F_KIND, F_DESC = 1, 2, 5


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dump", type=Path)
    parser.add_argument("--out", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()

    html = args.dump.read_text(encoding="utf-8")
    island = re.search(r'<script id="data" type="application/json">(.*?)</script>', html, re.S)
    if not island:
        print("error: no data island found — is this the command-dump HTML?", file=sys.stderr)
        return 1

    data = json.loads(island.group(1))
    kinds = data["kinds"]

    rows: list[tuple[str, str, str]] = []
    for row in data["rows"]:
        if kinds[row[F_KIND]] != "property" or not (row[7] & 1):
            continue

        name = row[F_NAME]
        if "." not in name:
            # A property row with no class in front of it names nothing a script can ask for, and
            # counting it would inflate a denominator this repository has corrected six times.
            continue

        owner, prop = name.rsplit(".", 1)
        rows.append((owner, prop, row[F_DESC] or ""))

    path = args.out / "matlab-r2021b-properties.csv"
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["class", "property", "summary"])
        writer.writerows(sorted(set(rows)))

    classes = {owner for owner, _, _ in rows}
    print(f"{path.name}: {len(rows)} documented properties over {len(classes)} classes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
