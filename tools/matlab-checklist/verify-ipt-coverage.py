#!/usr/bin/env python3
"""Check that docs/matlab-ipt-coverage.md agrees with the R2021b list and with the live catalog.

The base-MATLAB tracker (build-checklist.py) reads a scraped install dump. There is no such dump for
the Image Processing Toolbox — the machine the dump came from did not have it installed — so the IPT
list is transcribed by hand into matlab-r2021b-ipt.csv and the coverage doc is written by hand
against it. That is exactly the arrangement where counts rot silently, so this script is the guard:

  * every name in the CSV appears in exactly one bucket of the doc,
  * every name the doc mentions is really in the CSV (catches typos and invented functions),
  * every name the doc calls implemented is registered in JgsBuiltinCatalog.cs,
  * the counts the doc states match the counts computed from its own tables.

Exits non-zero, and says what is wrong, if any of that fails.

    python tools/matlab-checklist/verify-ipt-coverage.py
"""

from __future__ import annotations

import csv
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
CSV_PATH = REPO / "tools/matlab-checklist/matlab-r2021b-ipt.csv"
DOC_PATH = REPO / "docs/matlab-ipt-coverage.md"
CATALOG = REPO / "src/JGraph.Scripting/Jgs/JgsBuiltinCatalog.cs"

# Sections the doc must contain, and whether names under them are claimed as implemented.
BUCKETS = {
    "## Implemented": True,
    "## Not implemented": False,
    "## Excluded": False,
}


def documented_names() -> dict[str, tuple[str, str]]:
    """name -> (category, kind) from the transcribed R2021b list."""
    with CSV_PATH.open(encoding="utf-8", newline="") as f:
        return {r["name"]: (r["category"], r["kind"]) for r in csv.DictReader(f)}


def catalog_names() -> set[str]:
    """Every name JgsBuiltinCatalog registers.

    Same two shapes build-checklist.py reads: an ``Add("name", ...)`` literal, with the name possibly
    wrapped to the next line, and names registered from a loop over a table. A name registered from a
    loop is invisible here — that mistake cost M45 a wrong count — so add such families explicitly.
    """
    text = CATALOG.read_text(encoding="utf-8")
    return set(re.findall(r'(?:Add|Constant)\(\s*"([^"]+)"', text))


def doc_buckets(text: str) -> dict[str, set[str]]:
    """The backticked names under each top-level bucket heading."""
    found: dict[str, set[str]] = {b: set() for b in BUCKETS}
    current: str | None = None
    for line in text.splitlines():
        if line.startswith("## "):
            current = next((b for b in BUCKETS if line.startswith(b)), None)
            continue
        if current is not None:
            found[current].update(re.findall(r"`([A-Za-z][A-Za-z0-9_.]*)`", line))
    return found


def main() -> int:
    problems: list[str] = []
    listed = documented_names()
    text = DOC_PATH.read_text(encoding="utf-8")
    buckets = doc_buckets(text)
    registered = catalog_names()

    placed: dict[str, list[str]] = {}
    for bucket, names in buckets.items():
        for name in names:
            placed.setdefault(name, []).append(bucket)

    for name in sorted(listed):
        where = placed.get(name, [])
        if not where:
            problems.append(f"{name}: in the R2021b list but in no bucket of the coverage doc")
        elif len(where) > 1:
            problems.append(f"{name}: claimed in more than one bucket ({', '.join(sorted(where))})")

    for name in sorted(placed):
        if name not in listed:
            problems.append(f"{name}: named in the coverage doc but not in the R2021b list")

    for name in sorted(buckets["## Implemented"]):
        if name in listed and name not in registered:
            problems.append(f"{name}: called implemented but has no Add(\"{name}\") in the catalog")

    # The doc states its own headline counts; recompute them so they cannot drift.
    stated = re.search(r"\*\*(\d+) of (\d+) documented\b", text)
    if stated is None:
        problems.append("the coverage doc states no '**N of M documented' headline count")
    else:
        claimed_done, claimed_total = int(stated.group(1)), int(stated.group(2))
        real_done = len(buckets["## Implemented"] & set(listed))
        real_total = sum(1 for k in listed.values() if k[1] != "family")
        if claimed_done != real_done:
            problems.append(f"headline says {claimed_done} implemented; the tables list {real_done}")
        if claimed_total != real_total:
            problems.append(f"headline says {claimed_total} documented; the list holds {real_total}")

    if problems:
        print(f"{DOC_PATH.name}: {len(problems)} problem(s)")
        for p in problems:
            print("  -", p)
        return 1

    done = len(buckets["## Implemented"] & set(listed))
    total = sum(1 for k in listed.values() if k[1] != "family")
    print(f"{DOC_PATH.name}: OK — {done} of {total} documented names implemented, "
          f"{len(listed)} rows checked against the catalog")
    return 0


if __name__ == "__main__":
    sys.exit(main())
