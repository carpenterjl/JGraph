#!/usr/bin/env python3
"""Checks the R2025b dispatch table and the class mapping the resolver will read it through.

``matlab-r2025b-dispatch.csv`` decides, per catalog name and per argument pattern, whether a
built-in class method answers a call and so outranks a same-named user file (M145). A table like
that goes wrong in four quiet ways, and this checker refuses each of them:

  * **a catalog name with no row** — a builtin registered after the sweep would otherwise be
    resolved by whatever default the reader picks, so every name in ``JgsBuiltinCatalog`` must be
    in the table (a name in the table that has left the catalog is reported, not refused);
  * **a row that was written rather than measured** — the table is rebuilt in memory from the raw
    sweep files by ``build-dispatch-table.py`` and must match the committed copy byte for byte, so
    a hand-edited verdict, or a ``measured`` status on a row no sweep produced, cannot pass;
  * **a class the interpreter can name that the mapping does not know** — every answer
    ``ClassOf`` can give (read from the source: the numeric class names, the time classes, the
    literal answers in ``ClassOf`` itself, every tagged-struct class and every ``ClassName`` the
    library assigns) must map to one of the sweep's pattern classes in
    ``dispatch-class-map.csv``; a new library type therefore fails here rather than being read
    silently as ``struct``;
  * **prose that has drifted from the data** — the verdicts the plan and ADR 0149 quote
    (``numel("x", 1)`` keeps the built-in, ``numel(1, "x")`` reaches the file, ``height(table)``
    reaches the file, …) are re-read from the table, so the documents cannot describe a sweep
    the table no longer contains.

It also prints the headline counts the plan quotes, so they can be checked rather than remembered.

    python tools/matlab-checklist/verify-method-classes.py

Exit code 0 when everything holds, 1 otherwise, with each problem on its own line.
"""

from __future__ import annotations

import csv
import importlib.util
import re
import sys
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
TABLE = HERE / "matlab-r2025b-dispatch.csv"
CLASS_MAP = HERE / "dispatch-class-map.csv"
SCRIPTING = REPO / "src/JGraph.Scripting/Jgs"

PATTERN_CLASSES = {
    "double", "string", "cell", "struct", "fh", "char", "logical", "int8", "table", "datetime",
    "duration", "categorical", "map",
}

# (name, pattern, verdict) as the plan's probe tables and the ADR state them. ``builtin`` here means
# the built-in was reached, whether it answered or refused the arguments.
CITED: list[tuple[str, str, str]] = [
    ("max", "double", "builtin"), ("max", "cell", "file"), ("max", "string", "file"),
    ("max", "char,double", "file"), ("max", "double,cell", "file"), ("max", "double,fh", "file"),
    ("max", "int8,double", "builtin"), ("max", "logical", "builtin"), ("max", "double,char", "builtin"),
    ("numel", "double,string", "file"), ("numel", "string,double", "builtin"),
    ("plus", "double,string", "builtin"), ("plus", "double,double,string", "builtin"),
    ("isequal", "double,double,string", "builtin"),
    ("height", "table", "file"), ("height", "double,table", "file"),
    ("minus", "datetime,double", "builtin"), ("minus", "double,datetime", "builtin"),
    ("keys", "map", "builtin"), ("keys", "double,map", "file"),
    ("sum", "double,char", "builtin"), ("sum", "double", "builtin"),
    ("class", "double", "file"),
    ("eps", "none", "file"), ("eps", "double", "builtin"), ("eps", "string", "file"),
    ("string", "none", "builtin"),
    ("size", "none", "file"), ("size", "double", "builtin"), ("size", "string", "builtin"),
    ("size", "cell", "file"),
    ("fopen", "double", "file"), ("fprintf", "double", "file"), ("feval", "double", "file"),
    ("diff", "double", "builtin"), ("diff", "double,string", "file"),
    # mean and linspace are plain .m files in MATLAB: the current folder beats them outright.
    ("mean", "double", "file"), ("linspace", "double,double", "file"),
]


def load(name: str, filename: str):
    path = HERE / filename
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise SystemExit(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def source(name: str) -> str:
    return (SCRIPTING / name).read_text(encoding="utf-8")


def class_of_answers() -> set[str]:
    """Every literal ``ClassOf`` can answer, read from the source rather than remembered."""
    answers: set[str] = set()
    numeric = re.search(r"static string MatlabName\(this JgsNumericClass numericClass\)(.*?)};",
                        source("JgsNumericClass.cs"), re.S)
    if not numeric:
        raise SystemExit("cannot find JgsNumericClass.MatlabName")
    answers |= set(re.findall(r'"([a-z0-9]+)"', numeric.group(1)))
    time = re.search(r"static string TimeClassName\(JgsValue value\)(.*?)};", source("JgsBuiltins.Time.cs"), re.S)
    if not time:
        raise SystemExit("cannot find TimeClassName")
    answers |= set(re.findall(r'"([A-Za-z]+)"', time.group(1)))
    class_of = re.search(r"internal static string ClassOf\(JgsValue value, JgsDialect dialect\)(.*?)\n    };",
                         source("JgsBuiltins.Elementary.cs"), re.S)
    if not class_of:
        raise SystemExit("cannot find ClassOf")
    answers |= set(re.findall(r'"([A-Za-z_]+)"', class_of.group(1)))
    tagged = re.search(r"private static string\? TaggedClassOf\(JgsValue value\)(.*?)\n    }\n",
                       source("JgsBuiltins.Imaging.Geometry.cs"), re.S)
    if not tagged:
        raise SystemExit("cannot find TaggedClassOf")
    answers |= set(re.findall(r'"([A-Za-z0-9]+)"', tagged.group(1)))
    if 'StartsWith("prob."' in tagged.group(1):
        answers.add("prob.*")
    for path in SCRIPTING.glob("*.cs"):
        answers |= set(re.findall(r'ClassName\s*=\s*"([A-Za-z0-9_.]+)"', path.read_text(encoding="utf-8")))
    answers.discard("prob")
    return answers


def read_class_map() -> dict[str, str]:
    with CLASS_MAP.open(encoding="utf-8", newline="") as f:
        rows = list(csv.DictReader(f))
    return {r["class"]: r["pattern"] for r in rows}


def main() -> int:
    problems: list[str] = []
    builder = load("build_dispatch_table", "build-dispatch-table.py")
    prober = load("probe_forms", "probe-forms.py")

    if not TABLE.exists():
        print(f"error: {TABLE} not found — run build-dispatch-table.py", file=sys.stderr)
        return 1
    with TABLE.open(encoding="utf-8", newline="") as f:
        reader = csv.reader(f)
        header = next(reader)
        committed = [row for row in reader if row]
    if header != builder.header():
        problems.append("the table's header is not the one build-dispatch-table.py writes")
    rebuilt = builder.build()
    if committed != rebuilt:
        changed = {r[0] for r in committed} ^ {r[0] for r in rebuilt}
        edited = [c[0] for c, r in zip(committed, rebuilt) if c != r and c[0] == r[0]]
        problems.append("the committed table differs from what the raw sweeps produce"
                        + (f" (names present on one side only: {sorted(changed)[:10]})" if changed else "")
                        + (f" (rows that differ: {edited[:10]})" if edited else "")
                        + " — run build-dispatch-table.py rather than editing the CSV")

    rows = {r[0]: r for r in committed}
    patterns = header[4:]
    catalog = prober.catalog_names()
    missing = sorted(catalog - rows.keys())
    if missing:
        problems.append(f"{len(missing)} catalog names have no row (classify and sweep them): {missing[:15]}")
    extra = sorted(rows.keys() - catalog)
    if extra:
        print(f"note: {len(extra)} table names are no longer in the catalog: {extra[:10]}")

    unmeasured = sorted(n for n, r in rows.items() if r[1] == "no-method")
    if unmeasured:
        problems.append(f"{len(unmeasured)} names were never swept (run dispatch_probe_all.m over them): {unmeasured[:15]}")
    for name, row in rows.items():
        status, classes, verdicts = row[1], row[3], row[4:]
        if status not in builder.STATUSES:
            problems.append(f"{name}: status {status!r} is neither measured nor no-method")
        elif status == "measured":
            if any(v not in builder.VERDICTS for v in verdicts):
                problems.append(f"{name}: a measured row with a verdict outside the vocabulary")
        else:
            if classes:
                problems.append(f"{name}: has class methods ({classes}) but is marked no-method")
            if any(verdicts):
                problems.append(f"{name}: a no-method row carrying verdicts")

    class_map = read_class_map()
    for cls, pattern in class_map.items():
        if pattern not in PATTERN_CLASSES:
            problems.append(f"class map: {cls} maps to {pattern!r}, which is not a sweep pattern class")
    for answer in sorted(class_of_answers()):
        if answer not in class_map:
            problems.append(f"class map: ClassOf can answer {answer!r} and dispatch-class-map.csv has no row for it")

    def verdict(name: str, pattern: str) -> str | None:
        row = rows.get(name)
        if row is None or row[1] != "measured":
            return None
        v = row[4 + patterns.index(pattern)]
        return "builtin" if v.startswith("builtin") else v

    for name, pattern, expected in CITED:
        got = verdict(name, pattern)
        if got != expected:
            problems.append(f"cited: {name}({pattern}) is documented as {expected} but the table says {got}")
    measured = [r for r in committed if r[1] == "measured"]
    all_file = sum(1 for r in measured if all(v == "file" for v in r[4:]))
    signatures = Counter(tuple("builtin" if v.startswith("builtin") else v for v in r[4:]) for r in measured)
    double_first = sum(1 for r in measured if r[4 + patterns.index("double")].startswith("builtin"))
    none_builtin = sum(1 for r in measured if r[4 + patterns.index("none")].startswith("builtin"))
    print(f"{len(committed)} names: {len(measured)} measured, {len(committed) - len(measured)} with no class method; "
          f"{all_file} measured names go to the file under every pattern, {len(measured) - all_file} keep the "
          f"built-in under at least one, in {len(signatures)} distinct verdict signatures; "
          f"{double_first} keep it with a double first, {none_builtin} with no argument")

    for problem in problems:
        print(f"problem: {problem}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
