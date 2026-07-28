#!/usr/bin/env python3
"""Builds the documented-command checklist from the raw R2021b command dump.

The source file (``matlab-r2021b-commands.html``) is a self-contained checklist app holding all
56,824 names scraped from a MATLAB R2021b install tree — far too much to track. This script keeps
the app shell as-is and:

  * filters the data island down to the documented rows (FLAGS bit 1),
  * recomputes the header stats for the filtered set,
  * seeds the click-to-check state with every command JGraph already implements, read straight out
    of ``JgsBuiltinCatalog.cs`` (plus the session-level builtins, keywords, and operators), applied
    only when the browser has no saved state yet — existing ticks always win.

Usage:
    python build-checklist.py <source.html> <output.html> [--repo <jgraph-repo-root>]

Re-run it whenever the catalog grows; implemented commands then start checked on a fresh browser.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# Row field order (mirrors the app's F table).
F_ID, F_NAME, F_KIND, F_FLAGS = 0, 1, 2, 7

# Names JGraph implements that never appear in JgsBuiltinCatalog.cs (session-owned builtins),
# plus language keywords and operators as the dump spells them.
EXTRA_IMPLEMENTED = {
    "run", "clear", "whos", "save", "load",
    "if", "else", "elseif", "end", "for", "while", "switch", "case", "otherwise",
    "try", "catch", "function", "return", "break", "continue", "global",
    "+", "-", "*", "/", "\\", "^", ".*", "./", ".\\", ".^", "'", ".'",
    "==", "~=", "<", "<=", ">", ">=", "&", "|", "&&", "||", "~", ":",
}


def catalog_names(repo: Path) -> set[str]:
    """Every name JgsBuiltinCatalog registers (Add/Constant entries)."""
    source = (repo / "src/JGraph.Scripting/Jgs/JgsBuiltinCatalog.cs").read_text(encoding="utf-8")
    return set(re.findall(r'(?:Add|Constant)\("([^"]+)"', source))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()

    html = args.source.read_text(encoding="utf-8")
    island = re.search(r'(<script id="data" type="application/json">)(.*?)(</script>)', html, re.S)
    if not island:
        print("error: no data island found — is this the command-dump HTML?", file=sys.stderr)
        return 1

    data = json.loads(island.group(2))
    rows = [r for r in data["rows"] if r[F_FLAGS] & 1]
    data["rows"] = rows

    kinds = data["kinds"]
    callable_kinds = {"function", "builtin", "operator", "keyword", "script"}
    callable_rows = sum(1 for r in rows if kinds[r[F_KIND]] in callable_kinds)
    data["stats"] = {
        "total": len(rows),
        "documented": len(rows),
        "callablePublic": callable_rows,
        "undocumented": 0,
        "internal": 0,
        "described": len(rows),
        "withSyntax": sum(1 for r in rows if r[8]),
        "withArgs": sum(1 for r in rows if r[9]),
        "nameOnly": 0,
        "tiers": {"1": len(rows)},
    }

    implemented = catalog_names(args.repo) | EXTRA_IMPLEMENTED
    preset = sorted({(r[F_ID] or r[F_NAME]) for r in rows if r[F_NAME] in implemented})

    seed = (
        "<script>\n"
        "// Pre-seeded by build-checklist.py: commands JGraph already implements start checked.\n"
        "// Applies only when this browser has no saved progress — your own ticks always win.\n"
        "(function(){try{var K='matlab-r2021b-commands.v1';"
        "if(!localStorage.getItem(K)){localStorage.setItem(K,JSON.stringify("
        + json.dumps(preset, separators=(",", ":"))
        + "));}}catch(e){}})();\n</script>\n"
    )

    compact = json.dumps(data, separators=(",", ":"), ensure_ascii=False)
    html = html[: island.start()] + seed + island.group(1) + compact + island.group(3) + html[island.end():]
    html = html.replace(
        "MATLAB R2021b &mdash; Command Checklist",
        "MATLAB R2021b &mdash; Documented Command Checklist",
    )

    args.output.write_text(html, encoding="utf-8")
    print(f"{args.output}: {len(rows)} documented rows ({callable_rows} callable), "
          f"{len(preset)} pre-checked as implemented")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
