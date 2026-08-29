#!/usr/bin/env python3
"""Checks that ``docs/matlab-toolbox-coverage.md`` agrees with the catalog and the form dump.

The sibling checker, ``verify-builtin-coverage.py``, exists because its document had been corrected
for arithmetic six times. This one exists so that its document never has to be. It re-derives every
number in the file rather than reading it, and refuses the one mistake that matters most: a name
listed as **not implemented** that is in fact registered, which is how a document goes on describing
a gap that was closed two milestones ago.

    python tools/matlab-checklist/verify-toolbox-coverage.py

Exit code 0 when the document is consistent, 1 when it is not, and the problems are printed.
"""

from __future__ import annotations

import csv
import importlib.util
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DOC = REPO / "docs/matlab-toolbox-coverage.md"
RESULTS = REPO / "tools/matlab-checklist/toolbox-probe-results.csv"


def load(name: str, filename: str):
    """Imports a sibling script whose name is not a Python identifier."""
    path = Path(__file__).with_name(filename)
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise SystemExit(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main() -> int:
    prober = load("probe_forms", "probe-forms.py")
    toolbox = load("probe_toolbox_forms", "probe-toolbox-forms.py")

    if not DOC.exists():
        print(f"error: {DOC} not found — run probe-toolbox-forms.py first", file=sys.stderr)
        return 1

    registered = prober.catalog_names()
    rows = [r for r in csv.DictReader(prober.FORMS.open(encoding="utf-8", newline=""))
            if toolbox.in_scope(r)]

    names_by_folder: dict[str, set[str]] = defaultdict(set)
    forms_by_folder: Counter = Counter()
    for row in rows:
        names_by_folder[row["folder"]].add(row["name"])
        forms_by_folder[row["folder"]] += 1

    all_names = {r["name"] for r in rows}
    have = {n for n in all_names if n in registered}
    text = DOC.read_text(encoding="utf-8")
    problems: list[str] = []

    # 1. The two headline counts.
    headline = re.search(r"\*\*(\d+) of (\d+) names implemented\*\*", text)
    if not headline:
        problems.append("no headline name count found")
    elif (int(headline.group(1)), int(headline.group(2))) != (len(have), len(all_names)):
        problems.append(
            f"names: document says {headline.group(1)} of {headline.group(2)}, "
            f"the catalog and the dump give {len(have)} of {len(all_names)}")

    accepted = 0
    if RESULTS.exists():
        verdicts = Counter(r["verdict"]
                           for r in csv.DictReader(RESULTS.open(encoding="utf-8", newline="")))
        accepted = verdicts["accepted"]

    forms = re.search(r"\*\*(\d+) of (\d+) documented syntax forms accepted\*\*", text)
    if not forms:
        problems.append("no headline form count found")
    elif (int(forms.group(1)), int(forms.group(2))) != (accepted, len(rows)):
        problems.append(
            f"forms: document says {forms.group(1)} of {forms.group(2)}, "
            f"the probe results and the dump give {accepted} of {len(rows)}")

    # 2. Every folder row, against the dump and the catalog.
    for match in re.finditer(
            r"^\| `([a-z0-9_]+)` \| (\d+) / (\d+) \| (\d+) \| (\d+) \| (\d+) \|$", text, re.M):
        folder, present, total, documented, _, missing = match.groups()
        if folder not in names_by_folder:
            problems.append(f"row for `{folder}`, which is not a folder in scope")
            continue

        names = names_by_folder[folder]
        actually_present = sum(1 for n in names if n in registered)
        if int(total) != len(names):
            problems.append(f"`{folder}`: row says {total} names, the dump has {len(names)}")
        if int(present) != actually_present:
            problems.append(
                f"`{folder}`: row says {present} present, the catalog has {actually_present}")
        if int(missing) != len(names) - actually_present:
            problems.append(
                f"`{folder}`: row says {missing} missing, "
                f"the catalog leaves {len(names) - actually_present}")
        if int(documented) != forms_by_folder[folder]:
            problems.append(
                f"`{folder}`: row says {documented} forms documented, "
                f"the dump has {forms_by_folder[folder]}")

    # 3. The mistake this checker exists for: a name listed as missing that is registered.
    for section in re.finditer(r"^### `([a-z0-9_]+)` — (\d+)\n\n(.+)$", text, re.M):
        folder, count, listed = section.groups()
        names = re.findall(r"`([A-Za-z_][A-Za-z0-9_]*)`", listed)
        if len(names) != int(count):
            problems.append(
                f"`{folder}` missing section: heading says {count}, it lists {len(names)}")
        for name in names:
            if name in registered:
                problems.append(
                    f"`{folder}`: `{name}` is listed as not implemented but is in the catalog")
            if name not in names_by_folder.get(folder, ()):
                problems.append(f"`{folder}`: `{name}` is not a documented name in that folder")

    if problems:
        print(f"{DOC.name}: {len(problems)} problem(s)")
        for problem in problems:
            print(f"  - {problem}")
        return 1

    print(f"{DOC.name}: consistent "
          f"({len(have)} of {len(all_names)} names, {accepted} of {len(rows)} forms)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
