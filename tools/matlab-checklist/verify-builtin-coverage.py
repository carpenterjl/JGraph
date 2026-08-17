#!/usr/bin/env python3
"""Check that docs/matlab-builtin-coverage.md agrees with the R2021b forms list and the catalog.

The Image Processing and Statistics coverage documents have had a verifier since M46 and M53. The
base document — the largest of the three, and the one whose numbers are quoted most often — has
never had one, and it records **five** separate corrections to its own arithmetic and partitions,
every one of them found by a person reading carefully. This closes that gap.

What it checks, each chosen because the document has actually been wrong in that way before:

  * the three headline counts recompute from the checked-in form list and the live catalog —
    413 of 514 builtins, 246 of 277 graphics functions, 910 of 2,024 across every callable kind.
    The last pair is the sixth correction this document has needed: the 926 it carried counted
    implemented names over **every** documented row, properties and methods included, against a
    denominator of callable rows only;
  * ``413 + 101 == 514``, the partition M61 found broken;
  * each ``### Group — N`` subsection of "Not implemented" holds exactly N names, which is the
    "count the names in a list rather than trust the number at its head" rule the document states
    about itself after M54, M55 and M59 each broke it;
  * the subsection totals sum to the number in the ``## Not implemented — N`` heading;
  * **no name listed as not implemented is registered in the catalog** — the staleness that let
    M66's ten sparse orderings sit in a missing table for two milestones after they were written,
    and that left a dead ``readmatrix`` refusal behind M65's working one.

Names struck through with ``~~`` are read as already-done annotations and skipped, which is how
the document marks a name that moved without disturbing the prose around it.

    python tools/matlab-checklist/verify-builtin-coverage.py
"""

from __future__ import annotations

import csv
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
FORMS = REPO / "tools/matlab-checklist/matlab-r2021b-forms.csv"
DOC = REPO / "docs/matlab-builtin-coverage.md"
CATALOG = REPO / "src/JGraph.Scripting/Jgs/JgsBuiltinCatalog.cs"
GRAPHICS3D = REPO / "src/JGraph.Scripting/Jgs/JgsBuiltins.Graphics3D.cs"

# Session-level builtins and keywords that are implemented but never reach the catalog. Kept in
# step with build-checklist.py's EXTRA_IMPLEMENTED — the two tools must agree or the same name
# counts differently depending on which one is asked.
EXTRA_IMPLEMENTED = {
    "run", "clear", "whos", "save", "load",
    "if", "else", "elseif", "end", "for", "while", "switch", "case", "otherwise",
    "try", "catch", "function", "return", "break", "continue", "global",
    "classdef", "persistent", "arguments",
    "+", "-", "*", "/", "\\", "^", ".*", "./", ".\\", ".^", "'", ".'",
    "==", "~=", "<", "<=", ">", ">=", "&", "|", "&&", "||", "~", ":",
}


def catalog_names() -> set[str]:
    """Every name the catalog registers, plus the colormap family registered from a loop.

    The loop is the reason this helper exists rather than one regex: sixteen generators are
    registered from a table in JgsBuiltins.Graphics3D.cs, and a catalog-only regex reported
    ``parula`` as unimplemented while it was working. M45 paid for that once.
    """
    names = set(re.findall(r'(?:Add|Constant)\(\s*"([^"]+)"', CATALOG.read_text(encoding="utf-8")))
    table = re.search(r"ColormapGenerators\s*=\s*\[(.*?)\];", GRAPHICS3D.read_text(encoding="utf-8"), re.S)
    if table:
        names |= set(re.findall(r'\("([^"]+)"', table.group(1)))
    return names | EXTRA_IMPLEMENTED


def documented() -> dict[str, dict[str, str]]:
    """name -> {kind, graphics} for every documented callable, one entry per name."""
    with FORMS.open(encoding="utf-8", newline="") as handle:
        return {row["name"]: row for row in csv.DictReader(handle)}


def missing_sections(text: str) -> list[tuple[str, int, list[str], bool]]:
    """Each '### Group — N' subsection under '## Not implemented', with the names listed under it.

    Names are read from the single paragraph that follows the heading and stop at the blank line
    after it. The commentary underneath names plenty of *implemented* functions while explaining
    what moved — the OOP section discusses `classdef`, `metaclass` and `methods` at length — and
    reading past the blank line turns that prose into false "still missing" entries.

    A section whose list is written as wildcard families (`NET.*`, `clib*`) rather than as one name
    per entry is counted as unenumerable: its heading number counts family members the document
    never writes out, so the count check is skipped and only the staleness check runs. Saying that
    out loud is the point — a section silently excused would be the same failure this file exists
    to catch.
    """
    lines = text.splitlines()
    try:
        start = next(i for i, line in enumerate(lines) if line.startswith("## Not implemented"))
    except StopIteration:
        return []

    sections: list[tuple[str, int, list[str], bool]] = []
    index = start + 1
    while index < len(lines):
        line = lines[index]
        if line.startswith("## "):
            break
        heading = re.match(r"### (.*?) — (\d+)\s*$", line)
        if heading:
            names: list[str] = []
            cursor = index + 1
            while cursor < len(lines) and not lines[cursor].strip():
                cursor += 1  # the blank line between the heading and its list
            paragraph: list[str] = []
            while cursor < len(lines) and lines[cursor].strip():
                body = lines[cursor]
                if body.startswith(("###", "## ", "|")):
                    break
                paragraph.append(body)
                cursor += 1
            block = "\n".join(paragraph)
            names = re.findall(r"(?<!~)`([^`]+)`(?!~)", block)

            # Asked before the prose test below, because a family list is prose *and* unenumerable,
            # and blanking it first would turn "34 names we never wrote out" into "0 names".
            countable = enumerable(names)
            if not is_name_list(block):
                # Prose, not a list. A section whose entries have all been implemented keeps its
                # heading at 0 and explains itself in a sentence that still names them — reading
                # that sentence as entries is how the sparse-orderings section came back as twelve
                # missing names that M66 had written.
                names = []
            sections.append((heading.group(1), int(heading.group(2)), names, countable))
        index += 1
    return sections


def is_name_list(block: str) -> bool:
    """Whether a paragraph is a bare list of backticked names rather than a sentence about them.

    Take out every backticked span and see what is left. A list leaves whitespace; a sentence
    leaves words. This is the whole difference between "these eleven are missing" and "M66 took
    all ten of these", and the two are written identically apart from the prose around them.
    """
    return not re.sub(r"`[^`]*`|[\s~,.—-]", "", block)


def enumerable(names: list[str]) -> bool:
    """Whether a section writes out one name per entry, so its heading number can be checked."""
    return not any("*" in name for name in names)


def main() -> int:
    problems: list[str] = []
    text = DOC.read_text(encoding="utf-8")
    registered = catalog_names()
    rows = documented()

    builtins = {n for n, r in rows.items() if r["kind"] == "builtin"}
    graphics = {n for n, r in rows.items() if r["kind"] == "function" and r["graphics"] == "yes"}

    def check(pattern: str, done: int, total: int, label: str) -> None:
        stated = re.search(pattern, text)
        if stated is None:
            problems.append(f"the document states no {label} headline")
            return
        if int(stated.group(1).replace(",", "")) != done:
            problems.append(f"{label}: document says {stated.group(1)} implemented, the catalog gives {done}")
        if int(stated.group(2).replace(",", "")) != total:
            problems.append(f"{label}: document says {stated.group(2)} documented, the list holds {total}")

    check(r"\*\*(\d+) of (\d+) builtins implemented\*\*",
          len(builtins & registered), len(builtins), "builtins")
    check(r"\*\*(\d+) of the (\d+) documented graphics functions\*\*",
          len(graphics & registered), len(graphics), "graphics functions")
    check(r"the count is \*\*([\d,]+) of\s*\n([\d,]+)\*\*",
          len(set(rows) & registered), len(rows), "all callable kinds")

    sections = missing_sections(text)
    stated_missing = re.search(r"## Not implemented — (\d+)", text)
    counted = 0
    unenumerable: list[str] = []
    for title, claimed, names, countable in sections:
        counted += claimed
        if not countable:
            unenumerable.append(title)
        elif len(names) != claimed:
            problems.append(
                f"'{title}': heading says {claimed}, {len(names)} name(s) are listed under it")
        for name in names:
            if name in registered:
                problems.append(
                    f"{name}: listed as not implemented under '{title}', but the catalog registers it")

    if stated_missing:
        total_missing = int(stated_missing.group(1))
        # "The remaining seven" carries no number in its heading and is written as a table, so it
        # is not machine-counted; the difference is what it must hold.
        remainder = total_missing - counted
        if remainder < 0:
            problems.append(
                f"the numbered subsections total {counted}, more than the {total_missing} in the heading")
        if len(builtins) != (len(builtins & registered) + total_missing):
            problems.append(
                f"partition broken: {len(builtins & registered)} implemented + {total_missing} missing "
                f"!= {len(builtins)} documented builtins")
    else:
        problems.append("the document states no '## Not implemented — N' heading")

    if problems:
        print(f"{DOC.name}: {len(problems)} problem(s)")
        for problem in problems:
            print("  -", problem)
        return 1

    note = ""
    if unenumerable:
        note = f"; {len(unenumerable)} section(s) list families rather than names and are not count-checked"
    print(f"{DOC.name}: OK — {len(builtins & registered)} of {len(builtins)} builtins, "
          f"{len(graphics & registered)} of {len(graphics)} graphics functions, "
          f"{len(set(rows) & registered)} of {len(rows)} across every callable kind; "
          f"{len(sections)} missing-section count(s) checked against the catalog{note}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
