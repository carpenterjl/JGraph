#!/usr/bin/env python3
"""Extracts the documented *syntax forms* out of the R2021b command dump into checked-in CSVs.

Until M69 this repository measured MATLAB compatibility by **name**: a command counted as
implemented when ``JgsBuiltinCatalog.cs`` held an ``Add("name", ...)`` for it. The coverage
document says what is wrong with that in its own words — "this file counts names, and a script
that fails to run rarely fails for want of one". ``sort`` is one name and five documented syntax
forms, and nothing here knew whether four of those five worked.

The dump has carried the forms all along; only the names were being read. This script lifts them
out into two normalized tables so the form prober has a source of truth that lives *in the
repository*:

  * ``matlab-r2021b-forms.csv`` — one row per documented syntax form.
  * ``matlab-r2021b-args.csv``  — one row per documented argument, with its role and the value
    types the documentation lists for it. These are what the prober's sample table is keyed on.

Both cover every documented **callable** row (kinds function/builtin/operator/keyword/script), not
only the implemented ones, so the tables stay stable as the catalog grows and so the later Image
Processing and Statistics pass needs no new extraction.

Why this exists separately from ``build-checklist.py``: that script reads the same dump but writes
an HTML tracker, and it reads a 25 MB file that lives **outside version control**, in the demo
workspace. The base coverage number therefore could not be re-derived from a clean clone. These
CSVs can be, and they are small enough to diff.

Usage:
    python build-forms-csv.py <dump.html> [--out <tools/matlab-checklist>]

Re-run it only when the dump itself is replaced.
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
from pathlib import Path

# Row field order in the dump's data island (mirrors the app's F table).
F_NAME, F_KIND, F_DESC, F_SYNTAX, F_ARGS, F_SOURCE = 1, 2, 5, 8, 9, 11

# The kinds that can appear in a script as a call. Matches build-checklist.py's `callable_kinds`
# so the two tools cannot disagree about the denominator.
CALLABLE_KINDS = {"function", "builtin", "operator", "keyword", "script"}

# The six toolbox folders docs/matlab-builtin-coverage.md counts as "graphics functions". Recorded
# here rather than in the prober because the denominator is a documented decision (ADR 0060 fixed
# it at six folders after four milestones of arithmetic corrections), not an implementation detail.
GRAPHICS_FOLDERS = ("graph2d", "graph3d", "specgraph", "graphics", "scribe", "plottools")


def folder_of(source: str) -> str:
    """The toolbox folder a command's source file sits in, or '' when the dump names no source."""
    parts = (source or "").split("/")
    return parts[-2] if len(parts) >= 2 else ""


def is_graphics(source: str) -> bool:
    """
    Whether a command belongs to one of the six folders the coverage document counts as graphics.

    The test is over the whole path, not just the immediate parent: MATLAB files the dump places
    at ``toolbox/matlab/graphics/chart/...`` are still under ``graphics``, and reading only the
    parent folder drops 49 of the 246 names — which would have quietly restated a denominator ADR
    0060 spent a milestone getting right.
    """
    return any(f"/{folder}/" in (source or "") for folder in GRAPHICS_FOLDERS)


def load_rows(dump: Path) -> list:
    html = dump.read_text(encoding="utf-8")
    island = re.search(r'<script id="data" type="application/json">(.*?)</script>', html, re.S)
    if not island:
        print("error: no data island found — is this the command-dump HTML?", file=sys.stderr)
        raise SystemExit(1)
    return json.loads(island.group(1))["rows"], json.loads(island.group(1))["kinds"]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dump", type=Path)
    parser.add_argument("--out", type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()

    rows, kinds = load_rows(args.dump)

    forms: list[tuple] = []
    arguments: list[tuple] = []
    seen: set[str] = set()

    for row in rows:
        kind = kinds[row[F_KIND]]
        if kind not in CALLABLE_KINDS:
            continue

        name = row[F_NAME]
        if name in seen:
            continue  # the dump lists a handful of names twice; the first wins, as the checklist does
        seen.add(name)

        source = row[F_SOURCE] if len(row) > F_SOURCE else ""
        folder = folder_of(source)
        graphics = is_graphics(source)

        syntaxes = row[F_SYNTAX] or []
        for index, syntax in enumerate(syntaxes):
            forms.append((
                name,
                kind,
                folder,
                "yes" if graphics else "no",
                index,
                syntax,
                "yes" if ("Name,Value" in syntax or "Name=Value" in syntax) else "no",
            ))

        if not syntaxes:
            # A documented callable with no syntax block still needs a row, or the form count and
            # the name count stop reconciling and the reader cannot tell "no forms" from "dropped".
            forms.append((name, kind, folder, "yes" if graphics else "no", 0, "", "no"))

        for argument in (row[F_ARGS] or []):
            padded = list(argument) + ["", "", ""]
            arguments.append((name, padded[0], padded[1], padded[2]))

    forms_path = args.out / "matlab-r2021b-forms.csv"
    with forms_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["name", "kind", "folder", "graphics", "form_index", "syntax", "name_value"])
        writer.writerows(sorted(forms))

    args_path = args.out / "matlab-r2021b-args.csv"
    with args_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["name", "argument", "role", "value_types"])
        writer.writerows(sorted(arguments))

    named = sum(1 for f in forms if f[6] == "yes")
    print(f"{forms_path.name}: {len(forms)} forms over {len(seen)} documented callables "
          f"({named} take Name,Value)")
    print(f"{args_path.name}: {len(arguments)} documented arguments")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
