#!/usr/bin/env python3
"""Builds ``matlab-r2025b-dispatch.csv``, the table that says when a built-in method answers a call.

MATLAB resolves a function name in a fixed order (variables, nested, local, private, class methods,
current folder, path), but whether a *built-in* class method applies to a call — and so beats a
user file of the same name in the current folder — is decided per name and per argument position,
by no rule the documentation states: ``numel(1, "x")`` reaches a user ``numel.m`` while
``numel("x", 1)`` keeps the built-in, ``plus(1, "x")`` keeps the built-in, ``height(table)`` goes to
the file, ``minus(datetime)`` keeps its method. So the decision is a table, measured in R2025b, and
this script assembles that table from the raw sweep outputs in ``dispatch/``:

  * ``classify*.csv`` — ``which(name, '-all')`` for every catalog name (``classify_all.m``): the
    kind of the first hit and every ``@class`` method folder the name has;
  * ``dispatch-probe*.csv`` — every catalog name, shadowed one at a time by a file returning a
    sentinel and called under 37 argument patterns (``dispatch_probe_all.m`` for every name,
    ``dispatch_probe2.m``/``dispatch_probe6.m`` for the earlier pass over the names with an
    ``@class`` folder, ``dispatch_probe3.m`` for the names the harness itself uses,
    ``dispatch_probe4.m`` and ``dispatch_probe5.m`` for the six that had to be reached through
    ``builtin()``). Where two sweeps measured one name they must agree, and the script refuses
    the table when they do not — which is also how the earlier pass checks the later one.

Every name is swept, not only the ones ``which -all`` reports a method for: a method written
inside a class file (``tabular``'s ``height``, ``containers.Map``'s ``keys``) never shows in
``which -all``, and both keep the method under some patterns. Every row of the output therefore
carries ``status`` ``measured`` with a verdict in every pattern column; the ``no-method`` status —
a name no sweep covered whose ``which -all`` is empty, with its pattern columns left blank — is
kept so that a name added to the catalog after the sweep is visibly unmeasured rather than
defaulted, and ``verify-method-classes.py`` reports every such row. A name with a method that no
sweep covered is an error, not a default: the fix is to run the sweep for it.

The verdict vocabulary is kept as the harness wrote it — ``builtin`` (the built-in answered),
``builtin-err`` (the built-in was reached and refused the arguments, which is the same fact for
resolution), ``file`` (the shadowing file answered), ``undefined`` — so the file stays a record of
the measurement rather than an interpretation of it.

    python tools/matlab-checklist/build-dispatch-table.py

``verify-method-classes.py`` rebuilds the table in memory and refuses a committed copy that differs,
which is what makes a hand-edited row impossible to pass off as a measurement.
"""

from __future__ import annotations

import csv
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
RAW = HERE / "dispatch"
OUT = HERE / "matlab-r2025b-dispatch.csv"

PATTERNS = [
    "none", "double", "string", "cell", "struct", "fh", "char", "logical", "int8", "table",
    "datetime", "duration", "categorical", "map",
    "double,double", "double,string", "string,double", "double,cell", "cell,double",
    "double,struct", "double,fh", "int8,double", "double,char", "char,double", "string,string",
    "double,table", "table,double", "double,datetime", "datetime,double", "double,duration",
    "double,categorical", "double,map", "map,double", "string,cell", "cell,string",
    "double,double,string", "double,double,cell",
]
VERDICTS = {"builtin", "builtin-err", "file", "undefined"}
STATUSES = ("measured", "no-method")


def read_classifications() -> dict[str, tuple[str, str]]:
    """name -> (kind, method_classes) from every classify*.csv, which must agree where they overlap."""
    out: dict[str, tuple[str, str]] = {}
    for path in sorted(RAW.glob("classify*.csv")):
        with path.open(encoding="utf-8", newline="") as f:
            reader = csv.reader(f)
            header = next(reader)
            if header != ["name", "kind", "method_classes"]:
                raise SystemExit(f"{path.name}: unexpected header {header}")
            for row in reader:
                if not row:
                    continue
                name, kind, classes = row[0], row[1], row[2] if len(row) > 2 else ""
                entry = (kind, classes)
                if name in out and out[name] != entry:
                    raise SystemExit(f"{path.name}: {name} classified twice and differently: {out[name]} vs {entry}")
                out[name] = entry
    return out


def read_sweeps() -> dict[str, list[str]]:
    """name -> 37 verdicts from every dispatch-probe*.csv; a row the harness broke on is skipped."""
    out: dict[str, list[str]] = {}
    for path in sorted(RAW.glob("dispatch-probe*.csv")):
        with path.open(encoding="utf-8", newline="") as f:
            reader = csv.reader(f)
            header = next(reader)
            if header[0] != "name" or header[1:] != PATTERNS:
                raise SystemExit(f"{path.name}: pattern columns differ from the canonical 37")
            for row in reader:
                if not row:
                    continue
                name, verdicts = row[0], row[1:]
                if len(verdicts) != len(PATTERNS):
                    # The harness shadowed one of its own tools and the row ended early; a later
                    # sweep measured this name with a harness that reaches its tools through builtin().
                    continue
                bad = sorted(set(verdicts) - VERDICTS)
                if bad:
                    raise SystemExit(f"{path.name}: {name} carries verdicts outside the vocabulary: {bad}")
                if name in out and out[name] != verdicts:
                    raise SystemExit(f"{path.name}: {name} swept twice and differently")
                out[name] = verdicts
    return out


def build() -> list[list[str]]:
    classified = read_classifications()
    swept = read_sweeps()
    unmeasured = sorted(n for n, (_, classes) in classified.items() if classes and n not in swept)
    if unmeasured:
        raise SystemExit(
            "names with a class method that no sweep measured (run dispatch_probe2.m over them): "
            + ", ".join(unmeasured))
    unknown = sorted(set(swept) - set(classified))
    if unknown:
        raise SystemExit("names swept but never classified (run classify_all.m over them): " + ", ".join(unknown))
    rows = []
    for name in sorted(classified, key=lambda n: (n.lower(), n)):
        kind, classes = classified[name]
        if name in swept:
            rows.append([name, "measured", kind, classes, *swept[name]])
        else:
            rows.append([name, "no-method", kind, classes, *([""] * len(PATTERNS))])
    return rows


def header() -> list[str]:
    return ["name", "status", "kind", "method_classes", *PATTERNS]


def write(rows: list[list[str]]) -> None:
    with OUT.open("w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f, lineterminator="\n")
        writer.writerow(header())
        writer.writerows(rows)


def main() -> int:
    rows = build()
    write(rows)
    measured = sum(1 for r in rows if r[1] == "measured")
    print(f"wrote {OUT.relative_to(HERE.parent.parent)}: {len(rows)} names, {measured} measured, "
          f"{len(rows) - measured} with no class method")
    return 0


if __name__ == "__main__":
    sys.exit(main())
