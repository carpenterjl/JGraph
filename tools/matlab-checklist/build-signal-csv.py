#!/usr/bin/env python3
"""Builds matlab-r2024a-signal.csv: the Signal Processing Toolbox population, read from the install.

The IPT list (matlab-r2021b-ipt.csv) was transcribed by hand because the machine the base dump came
from had no IPT. This machine has R2024a with Signal installed, and every one of its public names is
a readable `.m` file, so the list is harvested rather than typed — and the script is kept so the
harvest can be repeated and checked.

What counts as a public name is the rule the 2026-09-01 gap report's inventory used, so the two
agree on the total (351): a `.m`/`.p`/`.mlx` file at the top of `toolbox/signal/signal` whose stem
is an identifier, is not `Contents`, and does not start with `ut` or `test`; plus the class name of
every `@class` folder.

Columns:

    name     the function or class name
    section  the toc*.m page that lists it (its title line), or `unlisted` — the toc pages are the
             toolbox's own table of contents, so `unlisted` is a strong hint the name is internal
    kind     class | function | mex (a MEX binary with a help-only .m beside it)
    forms    the call syntaxes the help block documents, `;`-separated — the lines of the form
             `Y = NAME(X,...)` — so a milestone's capability probe has its list

    python tools/matlab-checklist/build-signal-csv.py [--root E:/Matlab/toolbox/signal/signal]
"""

from __future__ import annotations

import argparse
import csv
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DEFAULT_ROOT = Path("E:/Matlab/toolbox/signal/signal")
OUT = REPO / "tools/matlab-checklist/matlab-r2024a-signal.csv"
IDENT = re.compile(r"^[A-Za-z][A-Za-z0-9_]*$")


def public_names(root: Path) -> dict[str, str]:
    """name -> kind, by the inventory's rule."""
    names: dict[str, str] = {}
    for p in root.iterdir():
        if p.is_file() and p.suffix in (".m", ".p", ".mlx"):
            n = p.stem
            if IDENT.match(n) and n != "Contents" and not n.startswith(("ut", "test")):
                names[n] = "function"
        elif p.is_dir() and p.name.startswith("@"):
            names[p.name[1:]] = "class"
    for p in root.glob("*.mexw64"):
        if p.stem in names and names[p.stem] == "function":
            names[p.stem] = "mex"
    return names


def toc_sections(root: Path) -> dict[str, str]:
    """name -> the title of the first toc page that lists it."""
    sections: dict[str, str] = {}
    for toc in sorted(root.glob("toc*.m")):
        lines = toc.read_text(encoding="utf-8", errors="replace").splitlines()
        title = ""
        for line in lines[1:6]:
            text = line.lstrip("%").strip()
            if text and not text.startswith("-"):
                title = text
                break
        for m in re.finditer(r'matlab:help ([A-Za-z][A-Za-z0-9_]*)"', "\n".join(lines)):
            sections.setdefault(m.group(1), title or toc.stem)
    return sections


def help_block(path: Path) -> list[str]:
    """The leading comment block of a .m file, with the % stripped."""
    out: list[str] = []
    started = False
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        s = line.strip()
        if s.startswith("%"):
            started = True
            out.append(s[1:].rstrip())
        elif started and s:
            break
        elif started and not s:
            # a blank line inside the block ends MATLAB's help too
            break
    return out


def syntax_forms(name: str, block: list[str]) -> list[str]:
    """The documented call forms: `[a,b] = NAME(...)`, `NAME(...)`.

    Most help blocks write the name in capitals; `rcosdesign`, `pentropy` and a few others write it
    as it is spelled, so the match is case-insensitive on the name and the form is normalised.
    """
    call = re.compile(
        r"((?:\[[^\]\n]*\]|[A-Za-z_]\w*)\s*=\s*)?(?i:" + re.escape(name) + r")\s*\(")
    forms: list[str] = []
    seen: set[str] = set()
    for line in block:
        for m in call.finditer(line):
            # walk to the matching close paren
            depth = 0
            end = None
            for i in range(m.end() - 1, len(line)):
                c = line[i]
                if c == "(":
                    depth += 1
                elif c == ")":
                    depth -= 1
                    if depth == 0:
                        end = i + 1
                        break
            if end is None:
                continue
            form = line[m.start():end].strip()
            form = re.sub(r"\s+", " ", form)
            form = re.sub(r"\(\s+", "(", form)
            form = re.sub(r"\s+\)", ")", form)
            lowered = re.sub(r"(?i)" + re.escape(name) + r"\s*\(", name + "(", form)
            if lowered not in seen:
                seen.add(lowered)
                forms.append(lowered)
    return forms


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    parser.add_argument("--out", type=Path, default=OUT)
    args = parser.parse_args()
    if not args.root.is_dir():
        print(f"error: {args.root} is not a folder", file=sys.stderr)
        return 2

    names = public_names(args.root)
    sections = toc_sections(args.root)
    rows = []
    for name in sorted(names, key=str.lower):
        kind = names[name]
        m_file = args.root / f"{name}.m"
        forms = syntax_forms(name, help_block(m_file)) if m_file.exists() else []
        rows.append({
            "name": name,
            "section": sections.get(name, "unlisted"),
            "kind": kind,
            "forms": ";".join(forms),
        })

    with args.out.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["name", "section", "kind", "forms"], lineterminator="\n")
        w.writeheader()
        w.writerows(rows)

    listed = sum(1 for r in rows if r["section"] != "unlisted")
    forms_total = sum(len(r["forms"].split(";")) for r in rows if r["forms"])
    print(f"{args.out.name}: {len(rows)} names ({listed} on a toc page), {forms_total} documented forms")
    return 0


if __name__ == "__main__":
    sys.exit(main())
