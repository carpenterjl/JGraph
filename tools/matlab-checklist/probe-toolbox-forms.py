#!/usr/bin/env python3
"""Runs the documented syntax forms of the *toolbox functions* and records what happened.

``probe-forms.py`` measures one population: the commands MATLAB documents with kind ``builtin``,
plus the ones it documents with kind ``function`` that are graphics. That is deliberate and its
coverage document says so. It leaves out the third population entirely — the mathematical library
functions MATLAB writes in MATLAB and ships on its default path, ``fminsearch`` and ``ode45`` and
``spline`` and the rest — which is why whole folders sat at nought probed and nobody could see it.

This is the same measurement over that third population. It reuses ``probe-forms.py``'s machinery
wholesale: the same argument samples, the same batching, the same five verdicts, the same rule that
``unprobed`` is never folded into either success or failure. Only the filter differs.

    python tools/matlab-checklist/probe-toolbox-forms.py [--exe <jgraph.exe>]

The document it writes counts by MATLAB toolbox folder, because that is the unit these arrive in and
the unit a milestone takes on: ``optimfun`` is six names, and closing it is one milestone's work.
"""

from __future__ import annotations

import argparse
import csv
import importlib.util
import shutil
import sys
import tempfile
from collections import Counter, defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


def load_prober():
    """Imports ``probe-forms.py``, whose name is not an identifier, as a module."""
    path = Path(__file__).with_name("probe-forms.py")
    spec = importlib.util.spec_from_file_location("probe_forms", path)
    if spec is None or spec.loader is None:
        raise SystemExit(f"cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


# The folders whose names are the mathematical library: everything a script doing numerical work
# reaches for. The GUI, file-format, interop and desktop folders are out of scope for the same
# reason they are out of scope for the builtin document — they are not what JGraph is for.
MATH_FOLDERS = [
    "optimfun", "funfun", "polyfun", "specfun", "matfun", "datafun", "elmat", "elfun",
    "ops", "sparfun", "strfun", "validators", "timefun", "randfun", "datatypes", "lang",
]


def in_scope(row: dict) -> bool:
    """Whether a documented form belongs to the toolbox-function population."""
    return (row["kind"] == "function"
            and row["graphics"] != "yes"
            and row["folder"] in MATH_FOLDERS
            and bool(row["syntax"]))


def main() -> int:
    prober = load_prober()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, default=prober.DEFAULT_EXE)
    parser.add_argument("--out", type=Path, default=REPO / "docs/matlab-toolbox-coverage.md")
    parser.add_argument("--csv", type=Path,
                        default=REPO / "tools/matlab-checklist/toolbox-probe-results.csv")
    parser.add_argument("--limit", type=int, default=0, help="probe only the first N forms")
    options = parser.parse_args()

    if not options.exe.exists():
        print(f"error: {options.exe} not found — build Release first", file=sys.stderr)
        return 2

    registered = prober.catalog_names()
    rows = [r for r in csv.DictReader(prober.FORMS.open(encoding="utf-8", newline=""))
            if in_scope(r)]

    arg_types: dict[str, dict[str, str]] = {}
    for row in csv.DictReader(prober.ARGS.open(encoding="utf-8", newline="")):
        arg_types.setdefault(row["name"], {})[row["argument"]] = row["value_types"]

    # Only implemented names are probed; the rest are counted as missing rather than run, exactly as
    # the builtin prober does. A name that does not resolve has no form to measure.
    implemented = [r for r in rows if r["name"] in registered]

    first_form: dict[str, list[str]] = {}
    for row in sorted(implemented, key=lambda r: (r["name"], int(r["form_index"]))):
        if row["name"] in first_form:
            continue
        parsed = prober.parse_syntax(row["syntax"], row["name"])
        if parsed and not any(t.strip() in ("___", "Name,Value") for t in parsed[1]):
            built = prober.build_call(row["name"], row["syntax"],
                                      arg_types.get(row["name"], {}), None)
            if built:
                first_form[row["name"]] = built[2]

    probes: list[dict] = []
    for row in implemented:
        record = {"name": row["name"], "folder": row["folder"], "form_index": row["form_index"],
                  "syntax": row["syntax"], "verdict": "", "detail": ""}
        if row["name"] in prober.SKIP_NAMES:
            record.update(verdict="unprobed", detail="waits for a person or ends the process")
        elif row["name_value"] == "yes":
            record.update(verdict="unprobed", detail="Name,Value form; the dump lists no pair names")
        else:
            built = prober.build_call(row["name"], row["syntax"],
                                      arg_types.get(row["name"], {}), first_form.get(row["name"]))
            if built is None:
                record.update(verdict="unprobed", detail="no sample for this form's arguments")
            else:
                record["statement"] = built[0]
        probes.append(record)

    runnable = [p for p in probes if not p["verdict"]]
    if options.limit:
        runnable = runnable[:options.limit]
    print(f"{len(rows)} forms in scope across {len({r['name'] for r in rows})} names; "
          f"{len(probes)} of them implemented, {len(runnable)} runnable")

    work = Path(tempfile.mkdtemp(prefix="jgraph-toolbox-"))
    try:
        prober.run_batches(runnable, options.exe, work)
    finally:
        shutil.rmtree(work, ignore_errors=True)

    write_outputs(rows, probes, registered, options.csv, options.out)
    return 0


def write_outputs(rows: list[dict], probes: list[dict], registered: set[str],
                  csv_path: Path, doc_path: Path) -> None:
    with csv_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["name", "folder", "form_index", "syntax", "verdict", "detail"])
        for probe in sorted(probes, key=lambda p: (p["folder"], p["name"], int(p["form_index"]))):
            writer.writerow([probe["name"], probe["folder"], probe["form_index"], probe["syntax"],
                             probe["verdict"], probe["detail"]])

    names_by_folder: dict[str, set[str]] = defaultdict(set)
    forms_by_folder: Counter = Counter()
    for row in rows:
        names_by_folder[row["folder"]].add(row["name"])
        forms_by_folder[row["folder"]] += 1

    accepted_by_folder: Counter = Counter()
    for probe in probes:
        if probe["verdict"] == "accepted":
            accepted_by_folder[probe["folder"]] += 1

    all_names = {r["name"] for r in rows}
    have = {n for n in all_names if n in registered}
    counts = Counter(p["verdict"] for p in probes)

    table = []
    for folder in sorted(names_by_folder, key=lambda f: -len(names_by_folder[f])):
        names = names_by_folder[folder]
        present = sorted(n for n in names if n in registered)
        missing = sorted(n for n in names if n not in registered)
        table.append(
            f"| `{folder}` | {len(present)} / {len(names)} | {forms_by_folder[folder]} | "
            f"{accepted_by_folder[folder]} | {len(missing)} |")

    missing_sections = []
    for folder in sorted(names_by_folder):
        missing = sorted(n for n in names_by_folder[folder] if n not in registered)
        if missing:
            missing_sections.append(
                f"### `{folder}` — {len(missing)}\n\n" + " ".join(f"`{n}`" for n in missing) + "\n")

    doc_path.write_text(f"""# MATLAB toolbox-function coverage

Where JGraph stands against the third population: the mathematical library functions MATLAB writes
**in MATLAB** and ships on its default path. Generated by
`tools/matlab-checklist/probe-toolbox-forms.py`; checked by `verify-toolbox-coverage.py`.

## Why this file exists

MATLAB's `toolbox/matlab` tree holds 1,759 `.m` files at depth two, and they are two different
things. **518 are help text only** — the implementation is in the MATLAB kernel, `which` says
"built-in", and `exist` answers 5. **1,241 hold real MATLAB source**: `which` answers a file path,
`exist` answers 2, and the code is interpreted exactly like a script's own. `fminsearch` is one of
those. So are `fzero`, `ode45`, `spline`, `roots` and `integral`.

The two documents beside this one could not see them. `matlab-builtin-coverage.md` counts the
commands MATLAB documents with kind **builtin**; `matlab-form-coverage.md` measures those plus the
graphics functions. Neither population includes a non-graphics `function`, which is what every name
below is — and that is why an entire folder could sit at nought implemented with no number moving.

## Where it stands

**{len(have)} of {len(all_names)} names implemented**, and
**{counts['accepted']} of {len(rows)} documented syntax forms accepted**.

| Folder | Names | Forms documented | Forms accepted | Names missing |
|---|---:|---:|---:|---:|
{chr(10).join(table)}

| Verdict | Forms | What it means |
|---|---:|---|
| accepted | {counts['accepted']} | the call returned without error |
| refused | {counts['refused']} | refused deliberately, with a message naming what is missing |
| undefined | {counts['undefined']} | the name did not resolve at all |
| error | {counts['error']} | failed some other way — **may be the prober's sample, not the build** |
| unprobed | {counts['unprobed']} | no call could be built |

Only implemented names are probed, so "forms accepted" is bounded by the names column above it. As
in the sibling document, **`accepted` is the trustworthy column** and every other one is a worklist:
a `refused` or an `error` is as likely to be the prober's generic sample argument meeting a
command that quite rightly rejects it.

## Not implemented

{chr(10).join(missing_sections)}
## Keeping this current

Re-run `python tools/matlab-checklist/probe-toolbox-forms.py` after any milestone that adds a name
here, then `python tools/matlab-checklist/verify-toolbox-coverage.py` to check the arithmetic. The
verifier refuses a name listed as missing that is registered in the catalog, which is the mistake
`matlab-builtin-coverage.md` was corrected for six times before it had a checker.
""", encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())
