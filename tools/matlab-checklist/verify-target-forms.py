#!/usr/bin/env python3
"""Checks that every command documented to take a target axes actually accepts one.

MATLAB lets a script name the axes a verb draws into, as the leading argument: `surf(ax, Z)`,
`stem(ax, x, y)`, `caxis(ax, [0 1])`. The R2021b dump records which commands take one, in the role
it gives an argument — `Target axes`, `Axes object`, `Axes to plot in`, `Polar axes`. That is a
list this repository can check itself against, which is what makes this a verifier rather than a
survey.

**The check isolates the target argument, and that is the whole design.** For each command it takes
a form the build *already accepts* — read from `form-probe-results.csv`, so the claim is measured
rather than assumed — and re-issues that exact call with `ax` in front. If `surf(X, Y, Z)` runs and
`surf(ax, X, Y, Z)` does not, nothing but the handle changed, so nothing but the handle can be
blamed. A verifier that built its own call from scratch could not say that: M69's form prober spent
three rounds discovering that its own samples were what several "failures" were measuring.

A command with no accepted form is reported as `unprobed` and never scored either way, for the
reason the coverage documents next door have been corrected six times.

Usage:
    python verify-target-forms.py [--exe path/to/jgraph.exe] [--verbose]

Exit code 0 when every checked command accepts a target, 1 otherwise.
"""

from __future__ import annotations

import argparse
import csv
import importlib.util
import re
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
ARGS = HERE / "matlab-r2021b-args.csv"
RESULTS = HERE / "form-probe-results.csv"
DEFAULT_EXE = REPO / "src/JGraph.Cli/bin/Release/net8.0/jgraph.exe"

# The roles that name an axes. `Parent container` and `Target figure` are deliberately absent: a
# uipanel and a figure are a different kind of target, and widening PeelAxes to swallow them
# without a probe first is how a verifier starts asserting something it never checked.
AXES_ROLES = re.compile(r"^target axes|^axes object|^axes to plot in|^polar axes|^polaraxes|^axes$",
                        re.IGNORECASE)

# Commands whose target is real but which cannot be checked this way, each for a stated reason
# rather than because they were inconvenient.
SKIP = {
    # Draw nothing on their own; the target is the axes they *read*.
    "getframe", "rendererinfo", "refreshdata",
    # Chart objects that own their axes rather than draw into one.
    "heatmap", "stackedplot", "parallelplot", "scatterhistogram", "wordcloud", "geobubble",
    # App Designer and geographic surfaces this build has no model for.
    "uiaxes", "geoaxes", "geoplot", "geoscatter", "geodensityplot", "geotickformat",
    # Create the target rather than take one.
    "axes", "polaraxes", "figure", "subplot", "tiledlayout", "gca",
    # Lays out its own grid of axes and says so by name. A loud refusal is the answer here, not a
    # gap: aiming it at one axes has no meaning.
    "plotmatrix",
}

# Verbs that mean nothing on the Cartesian axes `gca` makes by default. Probing them there measures
# the prober standing in the wrong room — the same correction M69's form prober needed.
POLAR = {"rlim", "rticks", "rticklabels", "rtickformat", "rtickangle",
         "thetalim", "thetaticks", "thetaticklabels", "thetatickformat",
         "polarplot", "polarscatter", "polarhistogram", "polarbubblechart"}


def probe_forms_module():
    """The form prober, imported for its sample table and call builder.

    Reusing it rather than re-deriving samples is deliberate: two tables that drift apart would let
    this verifier pass a call the form prober fails, and neither number would mean anything.
    """
    spec = importlib.util.spec_from_file_location("probe_forms", HERE / "probe-forms.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def target_taking_names() -> set[str]:
    """Every command the dump gives an axes-target argument."""
    rows = csv.DictReader(ARGS.open(encoding="utf-8"))
    return {r["name"] for r in rows if AXES_ROLES.match(r["role"] or "")}


def accepted_forms() -> dict[str, list[str]]:
    """Per command, the documented syntaxes the build is measured to accept."""
    found: dict[str, list[str]] = {}
    for row in csv.DictReader(RESULTS.open(encoding="utf-8")):
        if row["verdict"] == "accepted":
            found.setdefault(row["name"], []).append(row["syntax"])
    return found


def argument_types() -> dict[str, dict[str, str]]:
    types: dict[str, dict[str, str]] = {}
    for row in csv.DictReader(ARGS.open(encoding="utf-8")):
        types.setdefault(row["name"], {})[row["argument"]] = row["value_types"]
    return types


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, default=DEFAULT_EXE)
    parser.add_argument("--verbose", action="store_true")
    options = parser.parse_args()

    if not options.exe.exists():
        print(f"verify-target-forms: {options.exe} not found — build the CLI first.")
        return 1

    probe = probe_forms_module()
    names = sorted(target_taking_names() - SKIP)
    accepted = accepted_forms()
    types = argument_types()

    checks: list[tuple[str, str]] = []       # (name, statement)
    unprobed: list[str] = []
    for name in names:
        statement = None
        for syntax in accepted.get(name, []):
            built = probe.build_call(name, syntax, types.get(name, {}), first_args=None)
            if built is None:
                continue
            call, _ = built
            # The sample for an axes-typed argument is `gca`, so a form whose accepted call already
            # carries one would be re-issued with *two* targets and refuse for that reason. The
            # first run of this verifier reported `cla`, `bubblesize` and the tickformat trio as
            # failures on exactly that, which is the verifier measuring itself.
            if re.search(r"\bgca\b", call):
                continue
            # Re-issue the accepted call with the handle in front. The call text is `verb(a, b);`
            # or `o = verb(a, b);`, so the insertion point is the first open parenthesis after the
            # name — a bare `verb;` form has nowhere to put a target and is not a check.
            match = re.search(rf"\b{re.escape(name)}\(", call)
            if not match:
                continue
            statement = call[:match.end()] + "ax, " + call[match.end():]
            break
        if statement is None:
            unprobed.append(name)
        else:
            checks.append((name, statement))

    failures = run(checks, options.exe, probe)

    print(f"verify-target-forms: {len(checks) - len(failures)} of {len(checks)} commands "
          f"accept a target axes, {len(unprobed)} unprobed")
    if unprobed and options.verbose:
        print("  unprobed (no accepted form to re-issue): " + " ".join(unprobed))
    for name, statement, message in failures:
        print(f"  FAIL {name}: {statement.strip()} -> {message}")

    return 1 if failures else 0


def run(checks: list[tuple[str, str]], exe: Path, probe) -> list[tuple[str, str, str]]:
    """Run each check in its own process, and report what refused.

    One launch per command rather than one per batch: a drawing verb can take the process down, and
    M69 recorded 120 forms as failures because a neighbour in the same file did exactly that. A
    verdict about position in a file is not a verdict about the verb.
    """
    failures: list[tuple[str, str, str]] = []
    with tempfile.TemporaryDirectory() as work:
        folder = Path(work)
        for index, (name, statement) in enumerate(checks):
            axes = "polaraxes" if name in POLAR else "axes"
            script = folder / f"target_{index}.m"
            script.write_text(
                "figure;\n"
                f"ax = {axes};\n"
                "try\n"
                f"    {statement}\n"
                "    fprintf('PASS\\n');\n"
                "catch err\n"
                "    fprintf('FAIL %s\\n', err.message);\n"
                "end\n",
                encoding="utf-8")
            try:
                done = subprocess.run([str(exe), "-batch", script.name, "-sd", str(folder)],
                                      capture_output=True, text=True, timeout=90)
                output = done.stdout
            except subprocess.TimeoutExpired:
                output = "FAIL the call did not return"

            line = next((l for l in output.splitlines() if l.startswith(("PASS", "FAIL"))), None)
            if line is None:
                failures.append((name, statement, "took the process down"))
            elif line.startswith("FAIL"):
                failures.append((name, statement, line[5:].strip()))
    return failures


if __name__ == "__main__":
    raise SystemExit(main())
