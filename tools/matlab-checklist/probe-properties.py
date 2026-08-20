#!/usr/bin/env python3
"""Measures per-class graphics property coverage by building each object and asking it.

The second axis ADR 0069 named. JGraph builds its property table by reflection over the model's CLR
types plus about 430 hand-written aliases (``JgsGraphicsProperties.AddReflected``), so what a given
object answers to is knowable only at runtime — which is why this asks the running interpreter
rather than reading the source.

For each object kind below it writes one ``.m`` file that builds an instance, reads
``get(h, 'Type')`` and ``fieldnames(get(h))``, and prints them. Those names are then compared with
the documented property list for the matching MATLAB class in ``matlab-r2021b-properties.csv``.

**One file per kind, deliberately.** M69's syntax-form prober batched fifty forms into a file and
recorded 120 of them as failures when a neighbour took the process down — a statement about position
in a file, not about the forms. A drawing verb is far likelier to take a process down than an
expression is, so here each kind pays for its own launch and no kind can libel another.

Three verdicts, and the third is the one worth reading:

  * ``ok``          — built, and ``get(h)`` answered with a property list.
  * ``no-handle``   — the verb ran but what it returned is not a live handle, so no property of the
                      object it drew can be read or written. ``h = colorbar; h.Label.String = 'x'``
                      is ordinary MATLAB and cannot work here.
  * ``failed``      — the verb itself refused or is not implemented.

Usage:
    python probe-properties.py [--exe <jgraph.exe>] [--out docs/matlab-property-coverage.md]
"""

from __future__ import annotations

import argparse
import csv
import re
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
DEFAULT_EXE = REPO / "src/JGraph.Cli/bin/Release/net8.0/jgraph.exe"

# (label, documented MATLAB class, the statement that builds one).
#
# The class column is the join key into the documented table and is a judgement, not a lookup: MATLAB
# splits objects JGraph models as one (a stairstep is a Line with a property here, a Stair there) and
# models as several what MATLAB calls one. Where the mapping is a choice it is commented, because a
# denominator picked silently is how docs/matlab-builtin-coverage.md earned six corrections.
KINDS: list[tuple[str, str, str]] = [
    ("figure", "matlab.ui.Figure", "h = figure;"),
    ("axes", "matlab.graphics.axis.Axes", "h = axes;"),
    ("polaraxes", "matlab.graphics.axis.PolarAxes", "h = polaraxes;"),
    ("line", "matlab.graphics.chart.primitive.Line", "h = plot([1 2 3]);"),
    ("scatter", "matlab.graphics.chart.primitive.Scatter", "h = scatter([1 2 3], [1 2 3]);"),
    ("surface", "matlab.graphics.chart.primitive.Surface", "h = surf(peaks(8));"),
    ("patch", "matlab.graphics.primitive.Patch", "h = patch([0 1 1], [0 0 1], 'r');"),
    ("text", "matlab.graphics.primitive.Text", "h = text(0.5, 0.5, 'label');"),
    ("legend", "matlab.graphics.illustration.Legend", "plot([1 2 3]); h = legend('a');"),
    ("colorbar", "matlab.graphics.illustration.ColorBar", "surf(peaks(8)); h = colorbar();"),
    ("bar", "matlab.graphics.chart.primitive.Bar", "h = bar([1 2 3]);"),
    ("histogram", "matlab.graphics.chart.primitive.Histogram", "h = histogram([1 2 2 3]);"),
    ("area", "matlab.graphics.chart.primitive.Area", "h = area([1 2 3]);"),
    ("stair", "matlab.graphics.chart.primitive.Stair", "h = stairs([1 2 3]);"),
    ("stem", "matlab.graphics.chart.primitive.Stem", "h = stem([1 2 3]);"),
    ("errorbar", "matlab.graphics.chart.primitive.ErrorBar",
     "h = errorbar([1 2 3], [1 2 3], [.1 .1 .1]);"),
    ("contour", "matlab.graphics.chart.primitive.Contour", "h = contour(peaks(8));"),
    ("quiver", "matlab.graphics.chart.primitive.Quiver",
     "h = quiver([0 1], [0 1], [1 1], [1 1]);"),
    ("image", "matlab.graphics.primitive.Image", "h = image(magic(4));"),
    ("constantline", "matlab.graphics.chart.decoration.ConstantLine", "h = xline(1);"),
    ("boxchart", "matlab.graphics.chart.primitive.BoxChart", "h = boxchart([1 2 3 4]);"),
    ("heatmap", "matlab.graphics.chart.HeatmapChart", "h = heatmap(magic(4));"),
    # bubblechart answers 'scatter' here — it is a scatter with a size channel — so it is measured
    # against MATLAB's BubbleChart, which is what a script porting from MATLAB will have written.
    ("bubblechart", "matlab.graphics.chart.primitive.BubbleChart",
     "h = bubblechart([1 2], [1 2], [10 20]);"),
    ("light", "matlab.graphics.primitive.Light", "surf(peaks(8)); h = light('Position', [1 1 1]);"),
    ("bubblelegend", "matlab.graphics.illustration.BubbleLegend",
     "bubblechart([1 2], [1 2], [10 20]); h = bubblelegend();"),
    # MATLAB has no Pie class: pie makes Patch objects. JGraph models the whole chart as one object,
    # so this row is measured against Patch — the properties a script would reach for on a slice.
    ("pie", "matlab.graphics.primitive.Patch", "h = pie([1 2 3]);"),

    # M71 built the callback seam and, with it, the first two ui objects. Their documented rows were
    # already in the properties CSV; these join keys simply reach them now that the verbs exist.
    ("uicontextmenu", "matlab.ui.container.ContextMenu", "figure; h = uicontextmenu;"),
    ("uimenu", "matlab.ui.container.Menu",
     "figure; cm = uicontextmenu; h = uimenu(cm, 'Text', 'Copy');"),
]

# Drawing verbs MATLAB documents as returning an object. A verb that draws and returns nothing is
# not a missing property — it is every property of that object out of reach at once — so it is
# measured separately from the table above. The list is hand-written because there is no way to
# derive a runnable call from a documented signature, and it is not every graphics verb — a verb
# absent from it is unmeasured, not passing.
#
# M69 wrote 45 rows, covering the families the four verbs it fixed turned up in. M70 added 28 more
# and the choice of *how* is worth recording, because the obvious way is wrong. The R2021b dump
# names, per command, which argument is a target axes, and driving the sweep off that role looks
# like a way to grow this list by measurement rather than by memory. It is not: the dump describes
# *arguments*, and "takes a target axes" is a different question from "returns an object". Built
# that way the sweep picked up `axis`, `daspect`, `rlim` and 28 other query verbs and would have
# reported every one of them as handing back no handle — 31 verbs libelled by a filter that was
# measuring the wrong thing. The rows below were each run at the CLI first instead.
RETURNS: list[tuple[str, str]] = [
    ("plot", "h = plot([1 2 3]);"),
    ("plot3", "h = plot3([1 2], [1 2], [1 2]);"),
    ("scatter", "h = scatter([1 2], [1 2]);"),
    ("scatter3", "h = scatter3([1 2], [1 2], [1 2]);"),
    ("surf", "h = surf(peaks(8));"),
    ("mesh", "h = mesh(peaks(8));"),
    ("surfc", "h = surfc(peaks(8));"),
    ("surfl", "h = surfl(peaks(8));"),
    ("meshc", "h = meshc(peaks(8));"),
    ("meshz", "h = meshz(peaks(8));"),
    ("waterfall", "h = waterfall(peaks(8));"),
    ("ribbon", "h = ribbon(peaks(8));"),
    ("contour", "h = contour(peaks(8));"),
    ("contourf", "h = contourf(peaks(8));"),
    ("contour3", "h = contour3(peaks(8));"),
    # Two triangles, not one: the connectivity argument has to arrive as a matrix, and a single row
    # written [1 2 3] is a vector. The first draft of this line measured that mistake, not the verb.
    ("trisurf", "h = trisurf([1 2 3; 2 3 4], [0 1 0 1], [0 0 1 1], [0 0 1 1]);"),
    ("trimesh", "h = trimesh([1 2 3; 2 3 4], [0 1 0 1], [0 0 1 1], [0 0 1 1]);"),
    ("quiver", "h = quiver([0 1], [0 1], [1 1], [1 1]);"),
    ("quiver3", "h = quiver3([0 1], [0 1], [0 1], [1 1], [1 1], [1 1]);"),
    ("image", "h = image(magic(4));"),
    ("imagesc", "h = imagesc(magic(4));"),
    ("pcolor", "h = pcolor(peaks(8));"),
    ("bar", "h = bar([1 2 3]);"),
    ("barh", "h = barh([1 2 3]);"),
    ("area", "h = area([1 2 3]);"),
    ("stairs", "h = stairs([1 2 3]);"),
    ("stem", "h = stem([1 2 3]);"),
    ("stem3", "h = stem3([1 2], [1 2], [1 2]);"),
    ("errorbar", "h = errorbar([1 2 3], [1 2 3], [.1 .1 .1]);"),
    ("histogram", "h = histogram([1 2 2 3]);"),
    ("patch", "h = patch([0 1 1], [0 0 1], 'r');"),
    ("fill", "h = fill([0 1 1], [0 0 1], 'r');"),
    ("text", "h = text(0.5, 0.5, 'x');"),
    ("line", "h = line([0 1], [0 1]);"),
    ("legend", "plot([1 2 3]); h = legend('a');"),
    ("colorbar", "surf(peaks(8)); h = colorbar();"),
    ("light", "surf(peaks(8)); h = light('Position', [1 1 1]);"),
    ("xline", "h = xline(1);"),
    ("yline", "h = yline(1);"),
    ("polarplot", "h = polarplot([0 1], [1 2]);"),
    ("boxchart", "h = boxchart([1 2 3 4]);"),
    ("heatmap", "h = heatmap(magic(4));"),
    ("pie", "h = pie([1 2 3]);"),
    ("fplot", "h = fplot(@sin);"),
    ("fsurf", "h = fsurf(@(x, y) x + y);"),

    # M70 additions. Each was run at the CLI before being written here, per the standing rule, and
    # the two whose first snippet failed failed because the snippet was wrong — tetramesh wants an
    # m-by-3 matrix of vertices, not a list — which is the prober measuring itself if left in.
    ("semilogx", "h = semilogx([1 10 100], [1 2 3]);"),
    ("semilogy", "h = semilogy([1 2 3], [1 10 100]);"),
    ("loglog", "h = loglog([1 10 100], [1 10 100]);"),
    ("fill3", "h = fill3([0 1 1], [0 0 1], [0 0 0], 'r');"),
    ("surface", "h = surface(peaks(8));"),
    ("ribbon", "h = ribbon(peaks(8));"),
    ("polarscatter", "polaraxes; h = polarscatter([0 1], [1 2]);"),
    ("polarhistogram", "polaraxes; h = polarhistogram([0 1 2]);"),
    ("compass", "h = compass([1 2], [1 2]);"),
    ("feather", "h = feather([1 2], [1 2]);"),
    ("bar3", "h = bar3(magic(4));"),
    ("pie3", "h = pie3([1 2 3]);"),
    ("swarmchart", "h = swarmchart([1 2 3], [1 2 3]);"),
    ("bubblechart", "h = bubblechart([1 2], [1 2], [10 20]);"),
    ("bubblechart3", "h = bubblechart3([1 2], [1 2], [1 2], [10 20]);"),
    ("binscatter", "h = binscatter([1 2 3], [1 2 3]);"),
    ("triplot", "h = triplot([1 2 3; 2 3 4], [0 1 0 1], [0 0 1 1]);"),
    ("tetramesh", "h = tetramesh([1 2 3 4], [0 0 0; 1 0 0; 0 1 0; 0 0 1]);"),
    ("fmesh", "h = fmesh(@(x, y) x + y);"),
    ("fcontour", "h = fcontour(@(x, y) x + y);"),
    ("fimplicit", "h = fimplicit(@(x, y) x.^2 + y.^2 - 1);"),
    ("fplot3", "h = fplot3(@sin, @cos, @(t) t);"),
    ("stem3", "h = stem3([1 2], [1 2], [1 2]);"),
    ("ezplot", "h = ezplot(@sin);"),
    ("ezsurf", "h = ezsurf(@(x, y) x + y);"),
    ("annotation", "h = annotation('arrow', [.2 .4], [.2 .4]);"),
    ("yyaxis", "yyaxis left; h = plot([1 2 3]);"),
    ("waterfall", "h = waterfall(peaks(8));"),

    # M71 additions, run at the CLI first per the standing rule.
    ("uicontextmenu", "figure; h = uicontextmenu;"),
    ("uimenu", "figure; cm = uicontextmenu; h = uimenu(cm, 'Text', 'Copy');"),

    # Deliberately absent, each for a reason rather than an oversight:
    #   * sphere and cylinder answer coordinates, not a handle, so "did it return a handle" is the
    #     wrong question for them. (They answer an empty value here where MATLAB answers the X grid,
    #     which is a real divergence and a different one — recorded in ADR 0070.)
    #   * streamline's documented 2-D form, streamline(X, Y, U, V, sx, sy), is refused by this build:
    #     it reads the grids as volumes. Also ADR 0070, also not a handle question.
]

RETURNS_TEMPLATE = """\
try
    {snippet}
    if isa(h, 'function_handle')
        fprintf('VERDICT not-called\\n');
    elseif isempty(h)
        fprintf('VERDICT no-handle\\n');
    elseif all(ishandle(h(:)))
        fprintf('VERDICT handle %s\\n', get(h(1), 'Type'));
    else
        fprintf('VERDICT not-a-handle\\n');
    end
catch ME
    fprintf('VERDICT error %s\\n', ME.message);
end
"""

TEMPLATE = """\
try
    {snippet}
    fprintf('TYPE %s\\n', get(h(1), 'Type'));
    names = fieldnames(get(h(1)));
    for k = 1:numel(names)
        fprintf('PROP %s\\n', names{{k}});
    end
catch ME
    fprintf('ERROR %s\\n', ME.message);
end
"""


def documented(path: Path) -> dict[str, set[str]]:
    """The documented property names, by class."""
    table: dict[str, set[str]] = {}
    with path.open(encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            table.setdefault(row["class"], set()).add(row["property"])
    return table


def run(exe: Path, folder: Path, label: str, snippet: str) -> tuple[str, str, list[str]]:
    """Runs one kind's script. Returns (verdict, type name, property names)."""
    script = folder / f"prop_{label}.m"
    script.write_text(TEMPLATE.format(snippet=snippet), encoding="utf-8")

    try:
        done = subprocess.run(
            [str(exe), "-batch", script.name, "-sd", str(folder)],
            capture_output=True, text=True, timeout=120)
    except subprocess.TimeoutExpired:
        return "failed", "", ["the verb did not finish inside two minutes"]

    text = done.stdout + done.stderr
    if match := re.search(r"^ERROR (.*)$", text, re.M):
        message = match.group(1).strip()
        # The handle registry's own words for "this is not something you can ask about". The verb
        # itself ran — what it handed back is the problem — and that is a different finding from a
        # verb that refused, so it gets a verdict of its own rather than being filed under failure.
        if "not a handle" in message or "a null" in message or "got a function" in message:
            return "no-handle", "", [message]
        return "failed", "", [message]

    kind = (re.search(r"^TYPE (.*)$", text, re.M) or re.match("", "")).group(1).strip() \
        if re.search(r"^TYPE (.*)$", text, re.M) else ""
    names = [line[5:].strip() for line in text.splitlines() if line.startswith("PROP ")]
    if not names:
        return "no-handle", kind, ["the object was built but answered no properties"]
    return "ok", kind, names


def returns_handle(exe: Path, folder: Path, verb: str, snippet: str) -> str:
    """Whether `h = verb(...)` yields something get/set can be used on."""
    script = folder / f"ret_{verb}.m"
    script.write_text(RETURNS_TEMPLATE.format(snippet=snippet), encoding="utf-8")
    try:
        done = subprocess.run(
            [str(exe), "-batch", script.name, "-sd", str(folder)],
            capture_output=True, text=True, timeout=120)
    except subprocess.TimeoutExpired:
        return "error the verb did not finish inside two minutes"

    text = done.stdout + done.stderr
    match = re.search(r"^VERDICT (.*)$", text, re.M)
    return match.group(1).strip() if match else "error the script produced no verdict"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, default=DEFAULT_EXE)
    parser.add_argument("--properties", type=Path,
                        default=Path(__file__).resolve().parent / "matlab-r2021b-properties.csv")
    parser.add_argument("--out", type=Path, default=REPO / "docs/matlab-property-coverage.md")
    parser.add_argument("--csv", type=Path,
                        default=Path(__file__).resolve().parent / "property-probe-results.csv")
    args = parser.parse_args()

    if not args.exe.exists():
        print(f"error: {args.exe} not found — build the solution first", file=sys.stderr)
        return 1

    table = documented(args.properties)
    results = []

    with tempfile.TemporaryDirectory(prefix="jgraph-props-") as temporary:
        folder = Path(temporary)
        for label, klass, snippet in KINDS:
            verdict, kind, names = run(args.exe, folder, label, snippet)
            want = table.get(klass, set())
            if not want:
                print(f"warning: {klass} has no documented properties in the CSV", file=sys.stderr)
            have = set(names) if verdict == "ok" else set()
            results.append({
                "label": label,
                "class": klass,
                "type": kind,
                "verdict": verdict,
                "documented": len(want),
                "implemented": len(want & have),
                "extra": len(have - want),
                "missing": sorted(want - have),
                "note": "" if verdict == "ok" else (names[0] if names else ""),
            })
            print(f"  {label:<14} {verdict:<10} {len(want & have)}/{len(want)}")

        returns = [(verb, returns_handle(args.exe, folder, verb, snippet))
                   for verb, snippet in RETURNS]

    with args.csv.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["label", "class", "verdict", "documented", "implemented", "missing"])
        for row in results:
            writer.writerow([row["label"], row["class"], row["verdict"],
                             row["documented"], row["implemented"], " ".join(row["missing"])])

    ok = [r for r in results if r["verdict"] == "ok"]
    total_documented = sum(r["documented"] for r in ok)
    total_implemented = sum(r["implemented"] for r in ok)

    lines = [
        "# MATLAB property coverage",
        "",
        "Generated by `tools/matlab-checklist/probe-properties.py`. Every number here came from",
        "building the object in `jgraph.exe` and asking it, not from reading the source: the property",
        "table is built by reflection over the model's CLR types, so what an object answers to is a",
        "runtime fact.",
        "",
        f"**{total_implemented} of {total_documented} documented properties** are answered across the",
        f"{len(ok)} object kinds that could be built and asked. The other",
        f"{len(results) - len(ok)} kinds are counted separately below rather than scored as zero —",
        "an object that cannot be reached is not the same measurement as one missing properties, and",
        "folding the two together is the mistake `docs/matlab-builtin-coverage.md` has been corrected",
        "for six times.",
        "",
        "| Kind | MATLAB class | Answered | Documented | Extra |",
        "|---|---|---:|---:|---:|",
    ]
    for row in sorted(ok, key=lambda r: -r["documented"]):
        lines.append(f"| `{row['label']}` | `{row['class'].split('.')[-1]}` | "
                     f"{row['implemented']} | {row['documented']} | {row['extra']} |")

    unreachable = [r for r in results if r["verdict"] != "ok"]
    if unreachable:
        lines += [
            "",
            "## Objects with no reachable handle",
            "",
            "These verbs draw. What they hand back is not something `get`/`set` can be used on, so no",
            "property of the object is reachable however many the model carries. `h = colorbar;`",
            "followed by a property write is ordinary MATLAB.",
            "",
            "| Kind | Verdict | What happened |",
            "|---|---|---|",
        ]
        for row in unreachable:
            lines.append(f"| `{row['label']}` | {row['verdict']} | {row['note']} |")

    handed_back = [verb for verb, verdict in returns if verdict.startswith("handle")]
    withheld = [(verb, verdict) for verb, verdict in returns if not verdict.startswith("handle")]
    lines += [
        "",
        "## Does the verb hand back what it drew?",
        "",
        "A property table is only reachable through a handle, so this is the question underneath the",
        f"one above. Of **{len(returns)} drawing verbs** probed, **{len(handed_back)} return a usable",
        f"handle** and **{len(withheld)} do not**. A verb that draws and hands back nothing puts every",
        "property of the object it drew out of reach at once, whatever the model carries.",
        "",
        "This list is hand-written and is **not** every graphics verb. M69 wrote 45 rows, covering the",
        "families the four verbs it fixed turned up in; M70 added 28 more and M71 the two menu verbs,",
        "each run at the CLI before it was written down. A verb absent from it is unmeasured, not passing.",
        "",
        "It is hand-written for a reason worth stating, because the obvious alternative is wrong. The",
        "R2021b dump records which argument of a command is a target axes, and driving this sweep off",
        "that role looks like a way to grow it by measurement instead of by memory. It is not: the",
        "dump describes *arguments*, and taking a target axes is a different question from returning",
        "an object. Built that way the sweep collected `axis`, `daspect`, `rlim` and 28 other query",
        "verbs, and would have reported all 31 as handing back no handle.",
        "",
    ]
    if withheld:
        lines += ["| Verb | What `h = verb(…)` gives |", "|---|---|"]
        lines += [f"| `{verb}` | {verdict} |" for verb, verdict in withheld]
    else:
        lines.append("Every verb probed hands back a handle.")

    lines += [
        "",
        "## What the missing column is, and is not",
        "",
        "A name in `property-probe-results.csv`'s missing column is a property MATLAB documents for",
        "that class and this object did not answer to. It is a worklist. A few entries are the join",
        "being approximate — JGraph's `pie` is one object where MATLAB has a patch per slice, so it",
        "is measured against `Patch` and inherits every patch property it does not carry. And a name",
        "answering `get` says nothing about behaviour: M71 wired the callback block for real",
        "(ButtonDownFcn fires on a click, DeleteFcn on a deletion), but that is proven by tests and",
        "stress scripts, not by this table — the table only sees that the name is served.",
        "The `Answered` column is the measured one.",
        "",
    ]

    args.out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"\n{args.out.name}: {total_implemented} of {total_documented} across {len(ok)} kinds, "
          f"{len(unreachable)} kinds with no reachable handle")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
