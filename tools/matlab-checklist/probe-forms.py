#!/usr/bin/env python3
"""Runs every documented syntax form of every implemented command and records what happened.

This is the measurement M69 exists for. The repository has always counted MATLAB compatibility by
**name**: ``sort`` is one implemented name, and MATLAB documents five syntax forms for it. Nothing
knew whether four of those five worked, because nothing had ever run them.

The prober turns each documented form into a call, runs it through ``jgraph.exe -batch``, and puts
the outcome in one of five buckets:

  accepted   the call returned without error
  refused    JGraph refused it deliberately, with a message that names what is missing
  undefined  the name did not resolve at all
  error      it failed some other way — **which may be this script's sample argument, not the
             build**. Kept separate from `refused` for exactly that reason; an `error` is a lead to
             follow by hand, not a finding.
  unprobed   no call could be built: a Name,Value form (the dump does not carry the pair names), a
             `___` continuation with nothing to continue, or an argument whose documented value
             type has no sample here

**`unprobed` is never folded into either success or failure.** The coverage document it feeds has
been corrected five times for arithmetic, and the sixth was a numerator and a denominator counting
different populations; a bucket quietly dropped would be the seventh.

Forms are batched into `.m` files — roughly fifty per file, each wrapped in try/catch — because
`jgraph -batch "statement"` evaluates an inline statement as *JGS*, and only a file gets the MATLAB
dialect. When a whole batch dies (a form that takes the process down with it), its forms are re-run
one per file so the casualty is isolated rather than losing its forty-nine neighbours.

    python tools/matlab-checklist/probe-forms.py [--exe <jgraph.exe>] [--out docs/matlab-form-coverage.md]
"""

from __future__ import annotations

import argparse
import csv
import re
import shutil
import subprocess
import sys
import tempfile
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
FORMS = REPO / "tools/matlab-checklist/matlab-r2021b-forms.csv"
ARGS = REPO / "tools/matlab-checklist/matlab-r2021b-args.csv"
CATALOG = REPO / "src/JGraph.Scripting/Jgs/JgsBuiltinCatalog.cs"
GRAPHICS3D = REPO / "src/JGraph.Scripting/Jgs/JgsBuiltins.Graphics3D.cs"
DEFAULT_EXE = REPO / "src/JGraph.Cli/bin/Release/net8.0/jgraph.exe"

BATCH = 50

# A sample for each documented value-type phrase, chosen by the first keyword that matches. Order
# matters: "positive integer scalar" must be read as a positive integer before it is read as a
# scalar, and "character vector, string scalar" as text before "vector" claims it.
SAMPLES: list[tuple[str, str]] = [
    ("function handle", "@sin"),
    ("colormap", "parula"),
    ("character vector, string scalar", "'a'"),
    ("character vector", "'a'"),
    ("string scalar", "'a'"),
    ("string array", "'a'"),
    ("cell array of character vectors", "{'a', 'b'}"),
    ("cell array", "{1, 2}"),
    ("table", "table([1;2], [3;4])"),
    ("axes object", "gca"),
    ("polaraxes", "gca"),
    # Bare "axes" — the dump writes hold's target as "axes, array of axes", and without this row
    # "array" won at position six and handed hold a vector where it documents a handle.
    ("axes", "gca"),
    ("figure", "gcf"),
    ("graphics object", "gca"),
    ("rgb triplet", "[0 0 1]"),
    ("colorspec", "'r'"),
    ("logical", "true"),
    ("positive integer", "2"),
    ("nonnegative integer", "2"),
    ("integer", "2"),
    ("positive scalar", "2"),
    ("numeric scalar", "2"),
    ("multidimensional array", "[1 2 3; 4 5 6]"),
    ("matrix", "[1 2 3; 4 5 6]"),
    ("column vector", "[1; 2; 3]"),
    ("row vector", "[1 2 3]"),
    ("vector", "[1 2 3]"),
    ("scalar", "2"),
    ("array", "[1 2 3]"),
    ("number", "2"),
]

# Names whose forms are probed but whose calls must not be allowed to open a window or block. The
# graphics verbs draw into the batch figure, which is suppressed, so they are safe; these are the
# ones that wait for a person or end the process.
# waitfor left this set in M71: with no event pump installed — and a batch has none — it returns
# at once by contract, so probing it can no longer hang the run.
SKIP_NAMES = {"input", "keyboard", "pause", "exit", "quit", "waitforbuttonpress", "ginput",
              "uiwait", "gtext", "menu", "questdlg", "inputdlg", "msgbox"}


def catalog_names() -> set[str]:
    names = set(re.findall(r'(?:Add|Constant)\(\s*"([^"]+)"', CATALOG.read_text(encoding="utf-8")))
    table = re.search(r"ColormapGenerators\s*=\s*\[(.*?)\];", GRAPHICS3D.read_text(encoding="utf-8"), re.S)
    if table:
        names |= set(re.findall(r'\("([^"]+)"', table.group(1)))
    return names


# Samples for one argument of one command, where the generic phrase is true and useless (M76). The
# decompositions document A as a "matrix" and then need a square one; `chol` needs a positive
# definite one; the hull and triangulation verbs need points that are not all in a line. Probing
# them with the generic matrix measured the prober's own sample and recorded the refusal as a gap.
SQUARE = "[1 2 3; 4 5 6; 7 8 10]"
PENCIL = "[2 0 1; 0 3 0; 1 0 4]"
DEFINITE = "[4 2 1; 2 3 1; 1 1 5]"
COLUMN = "[1; 2; 3]"

NAME_ARG_SAMPLES: dict[tuple[str, str], str] = {
    ("eig", "A"): SQUARE, ("eig", "B"): PENCIL,
    ("lu", "A"): SQUARE, ("lu", "S"): SQUARE,
    ("chol", "A"): DEFINITE, ("chol", "S"): DEFINITE,
    ("qr", "S"): SQUARE, ("qr", "B"): COLUMN,
    ("linsolve", "A"): SQUARE, ("linsolve", "B"): COLUMN,
    ("qz", "A"): SQUARE, ("qz", "B"): PENCIL,
    ("hess", "A"): SQUARE, ("hess", "B"): PENCIL,
    ("balance", "A"): SQUARE,
    ("svd", "A"): SQUARE,
    ("expm", "A"): SQUARE, ("logm", "A"): SQUARE, ("sqrtm", "A"): SQUARE,
    ("rcond", "A"): SQUARE, ("schur", "A"): SQUARE, ("ldl", "A"): DEFINITE,
    # Eight corners of a cube: a hull and a triangulation in the plane and in space alike.
    ("convhull", "x"): "[0 1 0 1 0 1 0 1]",
    ("convhull", "y"): "[0 0 1 1 0 0 1 1]",
    ("convhull", "z"): "[0 0 0 0 1 1 1 1]",
    ("convhull", "P"): "[0 0; 1 0; 1 1; 0 1]",
    ("delaunay", "x"): "[0 1 0 1 0 1 0 1]",
    ("delaunay", "y"): "[0 0 1 1 0 0 1 1]",
    ("delaunay", "z"): "[0 0 0 0 1 1 1 1]",
    ("delaunay", "P"): "[0 0; 1 0; 1 1; 0 1]",
}

# Placeholders the documented type phrase cannot describe well enough to sample, whatever command
# they belong to. `thresh` is documented "scalar" and refused unless it is in [0, 1]; `sz` is
# documented "two-element row vector" while the generic vector sample has three.
NAMED_SAMPLES: dict[str, str] = {
    "thresh": "0.5",
    "sz": "[2 3]",
    # "character vector" is true of a format and useless as one: sampled as 'a' it asks a scanner to
    # match a literal letter against digits, which measures nothing about the conversions.
    "formatSpec": "'%f'",
    # Likewise a LineSpec: 'a' is a character vector and not a line spec, and MATLAB refuses it too.
    # Sampled that way the probe measured whether a verb would swallow nonsense, which is the
    # opposite of what the form documents. M77 found it by making two verbs strict enough to say no.
    "LineSpec": "'r--o'",
    "lineSpec": "'r--o'",
    "linespec": "'r--o'",
}


def literal_choice(value_types: str) -> str | None:
    """The first documented literal in a phrase that lists them — `'matrix', 'vector'` (M76).

    An enumerated argument's types column *is* its list of legal words, so the sample can be read
    straight off it instead of being guessed from a keyword. Reading it as prose was actively
    wrong: `outputForm` is documented `'matrix', 'vector'`, the keyword table saw the word "matrix"
    and handed `chol` a 2-by-3 matrix where it wanted the word, and the resulting complaint was
    recorded as the build refusing a form it in fact never saw.
    """
    literals = re.findall(r"'([^']*)'", value_types or "")
    return f"'{literals[0]}'" if literals else None


def sample_for(value_types: str) -> str | None:
    """A runnable sample for a documented value-type phrase, or None when there is no honest one.

    The **earliest** keyword in the phrase wins, not the first in the table. MATLAB's documentation
    lists an argument's primary type first, so "column vector, matrix, or cell array of index
    vectors" wants a column vector; a table-order match read that as a cell and handed `accumarray`
    something it rightly refused, which would have been recorded as a gap in `accumarray`.
    """
    lowered = (value_types or "").lower()
    best: tuple[int, str] | None = None
    for keyword, sample in SAMPLES:
        at = lowered.find(keyword)
        if at >= 0 and (best is None or at < best[0]):
            best = (at, sample)
    return best[1] if best else None


# The field family: verbs whose arguments are a grid and readings on it. The generic samples above
# read "3-D array" as a matrix and every grid placeholder as a vector, which handed `slice` an X of
# three positions and a V of two rows and got back a size complaint — the prober standing in the
# wrong room, exactly as it once stood in a Cartesian one to probe `rlim`. M72 fixed the forms
# themselves; these samples are what let the measurement see it. A form naming Z or W is read in
# space and everything else in a plane, which is how the two families of form differ.
FIELD_VERBS = {
    "slice", "streamline", "streamslice", "stream2", "stream3", "coneplot", "streamtube",
    "streamribbon", "streamparticles", "curl", "divergence", "isosurface", "isonormals",
    "isocaps", "isocolors", "smooth3", "subvolume", "reducevolume", "volumebounds", "interp3",
}

# Both readings are laid out by every field-verb probe, under names that cannot collide. The ___
# forms reuse the first form's argument text, and the first form of a verb like `slice` is the
# spatial one, so a plane-only prelude would leave those arguments naming variables that do not
# exist — which is a fault in the probe and would be recorded as one in the build.
FIELD_PRELUDE = (
    "[vX, vY, vZ] = meshgrid(1:3); vU = ones(3, 3, 3); vV = vU; vW = vU; "
    "[pX, pY] = meshgrid(1:3); pU = ones(3, 3); pV = pU;"
)

SPATIAL_FIELD = {
    "X": "vX", "Y": "vY", "Z": "vZ", "V": "vV", "U": "vU", "W": "vW",
    "startx": "2", "starty": "2", "startz": "2",
    "xslice": "2", "yslice": "2", "zslice": "2", "isovalue": "2",
}

PLANE_FIELD = {
    "X": "pX", "Y": "pY", "V": "pV", "U": "pU",
    "startx": "2", "starty": "2", "xslice": "2", "yslice": "2", "isovalue": "2",
}


# The file family. Their first argument is an open file id, and the prober had none to give: it
# sampled `fileID` from the phrase "integer" as 2, and every read verb answered "2 is not an open
# file" — sixteen forms recorded as errors that only ever measured the prober's own empty hand
# (M76). A real file is written, closed and reopened for update, so that a form which reads, one
# which writes and one which asks the id its own name all have something true to answer about.
FILE_VERBS = {"fopen", "fclose", "fread", "fwrite", "fgetl", "fgets", "fscanf", "fprintf",
              "feof", "ferror", "ftell", "fseek", "frewind", "textscan"}

FILE_PRELUDE = ("fpName = [tempname '.txt']; fpTmp = fopen(fpName, 'w'); "
                "fprintf(fpTmp, '1 2 3\\n4 5 6\\n'); fclose(fpTmp); fpTmp = fopen(fpName, 'r+');")

FILE_NAMES = {"fileID": "fpTmp", "fid": "fpTmp", "filename": "fpName"}


def field_samples(tokens: list[str]) -> tuple[str, dict[str, str]]:
    """Which of the two readings a field verb's form is in, by whether it names a third direction."""
    bare = {t.strip() for t in tokens}
    spatial = "Z" in bare or "W" in bare or "startz" in bare or "zslice" in bare
    return FIELD_PRELUDE, SPATIAL_FIELD if spatial else PLANE_FIELD


# Verbs that only mean anything on polar axes. Probing them on the Cartesian axes `gca` makes by
# default produced thirty "aims at a polar axes" errors that were the prober standing in the wrong
# room, not the build refusing anything.
POLAR_VERBS = {"rlim", "rticks", "rticklabels", "rtickformat", "rtickangle",
               "thetalim", "thetaticks", "thetaticklabels", "thetatickformat",
               "polarplot", "polarscatter", "polarhistogram", "polarbubblechart"}


def split_arguments(text: str) -> list[str]:
    """Split a syntax's argument text on top-level commas."""
    parts, depth, current = [], 0, ""
    for character in text:
        if character in "([{":
            depth += 1
        elif character in ")]}":
            depth -= 1
        if character == "," and depth == 0:
            parts.append(current.strip())
            current = ""
        else:
            current += character
    if current.strip():
        parts.append(current.strip())
    return parts


def parse_syntax(syntax: str, name: str) -> tuple[int, list[str]] | None:
    """(output count, argument tokens) for one documented syntax, or None when it is not a call."""
    text = syntax.strip()
    outputs = 1
    if "=" in text:
        left, _, right = text.partition("=")
        left, text = left.strip(), right.strip()
        if left.startswith("["):
            targets = split_arguments(left.strip("[]"))
            # `[B1,...,Bm]` names an open-ended output list. Asking for the literal three that
            # spelling parses into makes a one-output function look broken, so an open list is
            # probed at two — the smallest count that still exercises the multi-output path.
            outputs = 2 if any("..." in t for t in targets) else max(1, len(targets))
        else:
            outputs = 1
    else:
        outputs = 0

    call = re.match(rf"{re.escape(name)}\s*\((.*)\)\s*$", text)
    if not call:
        if text == name:
            return outputs, []

        # Command syntax: `drawnow limitrate`, `close all force`, `hold on`. Each word is the
        # string argument MATLAB's command-function duality makes it, so these forms are probed as
        # the calls they stand for rather than left unprobed as prose.
        words = re.fullmatch(rf"{re.escape(name)}((?:\s+[A-Za-z]\w*)+)", text)
        if words and outputs == 0:
            return 0, [f"'{word}'" for word in words.group(1).split()]

        return None  # an operator spelling, or a form written as prose
    return outputs, split_arguments(call.group(1))


def build_call(name: str, syntax: str, arg_types: dict[str, str],
               first_args: list[str] | None) -> tuple[str, int, list[str]] | None:
    """A runnable MATLAB statement for one documented form, or None when it cannot be built.

    Answers the statement, its output count, and **the argument texts it was built from**. That
    third item is what the `___` forms reuse, and it is returned rather than recovered from the
    statement because recovering it was this script's own bug (M76): the old reader took the last
    piece of `built[0].split("; ")`, which is a sound way to drop a field verb's prelude and a
    catastrophic one the moment an argument is a matrix literal. `chol([1 2 3; 4 5 6])` was cut at
    the semicolon inside its own brackets, leaving `chol(])` — a parse error that failed the whole
    probe file, which the runner then reported as the *builtin* taking the process down. Four
    forms across `chol`, `eig`, `lu` and `linsolve` were recorded as crashes that never happened.
    """
    parsed = parse_syntax(syntax, name)
    if parsed is None:
        return None
    outputs, tokens = parsed

    prelude, named = ("", {})
    if name in FIELD_VERBS:
        prelude, named = field_samples(tokens)
    elif name in FILE_VERBS:
        prelude, named = FILE_PRELUDE, FILE_NAMES

    values: list[str] = []
    for token in tokens:
        bare = token.strip()
        if bare in named:
            values.append(named[bare])
            continue
        if bare in ("Name,Value", "Name=Value", "Name", "Value"):
            return None  # the dump does not carry which pairs a command takes
        if bare == "___":
            if first_args is None:
                return None
            values.extend(first_args)
            continue
        if bare.startswith(("'", '"')):
            values.append(bare)
            continue
        if bare.startswith("{"):
            values.append(bare)
            continue
        if bare.startswith("["):
            # A bracket in a documented syntax is usually a shape or a limit pair written with
            # placeholder names — `alim([amin amax])`. Passing it through verbatim asks the build
            # to resolve variables that do not exist, which is the prober's error, not a gap.
            inner_tokens = re.split(r"[\s,]+", bare.strip("[]").strip())
            if all(re.fullmatch(r"[A-Za-z_]\w*", t or "") for t in inner_tokens if t):
                values.append("[" + " ".join(str(i + 1) for i in range(len(inner_tokens))) + "]")
            else:
                values.append(bare)
            continue
        if re.fullmatch(r"-?\d+(\.\d+)?", bare):
            values.append(bare)
            continue
        key = re.sub(r"[^A-Za-z0-9_]", "", bare)
        types = arg_types.get(bare) or arg_types.get(key) or ""
        sample = (NAME_ARG_SAMPLES.get((name, bare))
                  or NAME_ARG_SAMPLES.get((name, key))
                  or NAMED_SAMPLES.get(bare)
                  or literal_choice(types)
                  or sample_for(types))
        if sample is None:
            return None
        values.append(sample)

    inner = f"{name}({', '.join(values)})" if values else name
    lead = f"{prelude} " if prelude else ""
    if outputs >= 2:
        targets = ", ".join(f"o{i}" for i in range(outputs))
        return f"{lead}[{targets}] = {inner};", outputs, values
    return (f"{lead}o = {inner};" if outputs else f"{lead}{inner};"), outputs, values


def classify(message: str) -> str:
    lowered = message.lower()
    if "is not recognized as a variable or a function" in lowered:
        return "undefined"
    if "is not supported in jgraph" in lowered or "is not available" in lowered:
        return "refused"
    markers = ("not supported", "is not implemented", "unknown option", "does not take",
               "is not one of", "has no option", "not available here", "only supports",
               "is not a ", "cannot ", "which jgraph does not")
    if any(marker in lowered for marker in markers):
        return "refused"
    return "error"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, default=DEFAULT_EXE)
    parser.add_argument("--out", type=Path, default=REPO / "docs/matlab-form-coverage.md")
    parser.add_argument("--csv", type=Path, default=REPO / "tools/matlab-checklist/form-probe-results.csv")
    parser.add_argument("--limit", type=int, default=0, help="probe only the first N forms (smoke test)")
    options = parser.parse_args()

    if not options.exe.exists():
        print(f"error: {options.exe} not found — build Release first", file=sys.stderr)
        return 2

    registered = catalog_names()
    rows = [r for r in csv.DictReader(FORMS.open(encoding="utf-8", newline=""))]
    in_scope = [r for r in rows
                if r["name"] in registered
                and (r["kind"] == "builtin" or (r["kind"] == "function" and r["graphics"] == "yes"))
                and r["syntax"]]

    arg_types: dict[str, dict[str, str]] = {}
    for row in csv.DictReader(ARGS.open(encoding="utf-8", newline="")):
        arg_types.setdefault(row["name"], {})[row["argument"]] = row["value_types"]

    first_form: dict[str, list[str]] = {}
    for row in sorted(in_scope, key=lambda r: (r["name"], int(r["form_index"]))):
        if row["name"] in first_form:
            continue
        parsed = parse_syntax(row["syntax"], row["name"])
        if parsed and not any(t.strip() in ("___", "Name,Value") for t in parsed[1]):
            built = build_call(row["name"], row["syntax"], arg_types.get(row["name"], {}), None)
            if built:
                # The arguments as they were built. A field verb's call carries a prelude of
                # assignments in front of it and an argument may be a matrix literal with a
                # semicolon in it; neither can disturb this, because nothing is re-parsed.
                first_form[row["name"]] = built[2]

    probes: list[dict] = []
    for row in in_scope:
        record = {"name": row["name"], "kind": row["kind"], "form_index": row["form_index"],
                  "syntax": row["syntax"], "verdict": "", "detail": ""}
        if row["name"] in SKIP_NAMES:
            record.update(verdict="unprobed", detail="waits for a person or ends the process")
        elif row["name_value"] == "yes":
            record.update(verdict="unprobed", detail="Name,Value form; the dump lists no pair names")
        else:
            built = build_call(row["name"], row["syntax"],
                               arg_types.get(row["name"], {}), first_form.get(row["name"]))
            if built is None:
                record.update(verdict="unprobed", detail="no sample for this form's arguments")
            else:
                record["statement"] = built[0]
        probes.append(record)

    runnable = [p for p in probes if not p["verdict"]]
    if options.limit:
        runnable = runnable[:options.limit]
    print(f"{len(probes)} forms in scope; {len(runnable)} runnable, "
          f"{len(probes) - len(runnable)} unprobed before running")

    work = Path(tempfile.mkdtemp(prefix="jgraph-forms-"))
    try:
        run_batches(runnable, options.exe, work)
    finally:
        shutil.rmtree(work, ignore_errors=True)

    write_outputs(probes, options.csv, options.out)
    return 0


def run_batches(runnable: list[dict], exe: Path, work: Path) -> None:
    for start in range(0, len(runnable), BATCH):
        chunk = runnable[start:start + BATCH]
        results = run_chunk(chunk, exe, work, start // BATCH)

        # Two ways a batch can fail its neighbours: it dies outright (no output at all), or a form
        # part-way through takes the process down and the ones after it never run. Both leave forms
        # whose verdict says more about their position in the file than about the build, so both are
        # re-run one per file. Recording "the batch stopped before this form ran" as an *error* was
        # this script's own worst bug: 120 unmeasured forms counted as failures.
        stragglers = [p for p in chunk
                      if p.get("verdict") in (None, "", "error")
                      and p.get("detail") == "the batch stopped before this form ran"]
        if results is None or stragglers:
            isolate = chunk if results is None else stragglers
            print(f"  batch {start // BATCH}: isolating {len(isolate)} form(s)")
            for index, probe in enumerate(isolate):
                probe["verdict"] = ""
                probe["detail"] = ""
                single = run_chunk([probe], exe, work, f"{start // BATCH}-{index}")
                if single is None or not probe["verdict"]:
                    probe.update(verdict="error", detail="took the process down")
        print(f"  {min(start + BATCH, len(runnable))}/{len(runnable)}", end="\r", flush=True)
    print()


def run_chunk(chunk: list[dict], exe: Path, work: Path, tag) -> bool | None:
    script = work / f"probe_{tag}.m"
    lines = []
    for index, probe in enumerate(chunk):
        # Every form starts from an empty workspace. Fifty forms share one file, and without this
        # they shared one set of variables: `save(filename)` failed because an earlier form's catch
        # block had left an `ME` lying about, and the verdict recorded was "MException cannot be
        # written to a MAT-file" — a true sentence about a workspace the prober built, and nothing
        # at all about `save`.
        lines.append("clear;")
        lines.append("try")
        if probe["name"] in POLAR_VERBS:
            lines.append("    figure; polaraxes;")
        lines.append(f"    {probe['statement']}")
        lines.append(f"    fprintf('@@%d|ok|\\n', {index});")
        lines.append("catch ME")
        lines.append(f"    fprintf('@@%d|err|%s\\n', {index}, ME.message);")
        lines.append("end")
    script.write_text("\n".join(lines) + "\n", encoding="utf-8")

    try:
        finished = subprocess.run([str(exe), "-batch", script.name, "-sd", str(work)],
                                  capture_output=True, text=True, timeout=180)
    except subprocess.TimeoutExpired:
        return None

    seen = set()
    for line in finished.stdout.splitlines():
        match = re.match(r"@@(\d+)\|(ok|err)\|(.*)", line)
        if not match:
            continue
        index, kind, message = int(match.group(1)), match.group(2), match.group(3)
        if index >= len(chunk):
            continue
        seen.add(index)
        if kind == "ok":
            chunk[index].update(verdict="accepted", detail="")
        else:
            chunk[index].update(verdict=classify(message), detail=message.strip())

    if not seen:
        return None
    for index, probe in enumerate(chunk):
        if index not in seen and not probe["verdict"]:
            probe.update(verdict="error", detail="the batch stopped before this form ran")
    return True


def write_outputs(probes: list[dict], csv_path: Path, doc_path: Path) -> None:
    with csv_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["name", "kind", "form_index", "syntax", "verdict", "detail"])
        for probe in sorted(probes, key=lambda p: (p["name"], int(p["form_index"]))):
            writer.writerow([probe["name"], probe["kind"], probe["form_index"], probe["syntax"],
                             probe["verdict"], probe["detail"]])

    counts = Counter(p["verdict"] for p in probes)
    names = {p["name"] for p in probes}
    whole = sum(1 for n in names
                if all(p["verdict"] == "accepted" for p in probes if p["name"] == n))
    partial = sum(1 for n in names
                  if any(p["verdict"] == "accepted" for p in probes if p["name"] == n)
                  and any(p["verdict"] in ("refused", "undefined", "error")
                          for p in probes if p["name"] == n))

    total = len(probes)
    doc_path.write_text(f"""# MATLAB syntax-form coverage

Where JGraph stands against the *forms* MATLAB documents, rather than the names. Generated by
`tools/matlab-checklist/probe-forms.py`, which runs every form through `jgraph.exe -batch` and
records what came back; re-run it after any milestone that changes builtin arguments.

The three coverage documents beside this one count **names**. This one exists because
`docs/matlab-builtin-coverage.md` says what is wrong with that in its own words — "this file counts
names, and a script that fails to run rarely fails for want of one". `sort` is one implemented name
and five documented syntax forms.

## Where it stands

**{counts['accepted']} of {total} documented syntax forms are accepted** across
{len(names)} implemented commands (the base builtins and the graphics functions; the Image
Processing and Statistics surfaces are a later pass).

| Verdict | Forms | What it means |
|---|---:|---|
| accepted | {counts['accepted']} | the call returned without error |
| refused | {counts['refused']} | refused deliberately, with a message naming what is missing |
| undefined | {counts['undefined']} | the name did not resolve at all |
| error | {counts['error']} | failed some other way — **may be the prober's sample, not the build** |
| unprobed | {counts['unprobed']} | no call could be built; see below |
| **total** | **{total}** | |

**{whole} commands accept every form they document. {partial} accept some and not others** — the
number the name count could never show, and the one worth working from.

## What this sweep does and does not establish

**`accepted` is the trustworthy column.** A form that ran without error ran; there is nothing to
second-guess. {counts['accepted']} of {total} documented forms are confirmed working by execution
rather than by assumption, which is {counts['accepted']} more than were confirmed before M69.

**Every other column is a worklist, not a finding, and the spot-check is why that sentence is here.**
Twenty forms were re-run by hand against their verdicts. The `accepted` ones held. The `refused` and
`error` ones mostly did not mean what their bucket suggested:

- Most `refused` verdicts are the prober's own generic text argument being correctly rejected. The
  prober hands a `character vector` argument `'a'`; `xtickformat('a')` then answers, quite rightly,
  that `'a'` is not a tick format. That is the build working, recorded as a refusal.
- `undefined` verdicts are all `eval('a')` and its relatives evaluating the sample *as code*.
- `error` is dominated by samples of the wrong shape for the particular command — a vector where a
  surface verb wants a matrix, a one-output `@sin` where `arrayfun` was asked for two.

So the honest reading is: **one column measures, and the rest trace where to look.** Each of the
findings below came out of that tracing and was then confirmed by hand at the command line; none of
them is quoted from a bucket.

## Confirmed by hand

| What | Evidence |
|---|---|
| `Inf(n)`, `Inf(sz)`, `NaN(n)`, `NaN(sz)` build nothing | MATLAB makes an n-by-n matrix. Here `Inf` is a constant with `AutoCallsBare`, so `Inf(2)` *indexes* the scalar and answers "Index 2 is out of range for length 1". `zeros(2)` and `ones(2)` work, so the gap is these two names, not the shape family. |
| A reduction takes one dimension, never a vector of them | `sum(A,[1 2])`, `all(A,[1 2])` and `max(A,[],[1 2])` all refuse. MATLAB's `vecdim` collapses several dimensions at once; this is one gap across the whole reduction family rather than one per name. |
| `regexp`/`regexpi` do not take `'forceCellOutput'` | Refused by name, with the options it does take listed — the house style working as intended. |
| `axis('state')` is not read | The legacy three-output query form. |

## How to read `error` and `unprobed`

`error` is a lead, not a finding, for the reason the spot-check showed. Each one needs a person
before it becomes a gap.

`unprobed` is counted and never hidden — {counts['unprobed']} forms, {counts['unprobed'] * 100 // total}% of the total. Three things land
there: a **Name,Value** form, because the dump records *that* a command takes pairs but not *which*
pairs; a form whose arguments have no sample in the prober's table; and the handful of commands that
wait for a person or end the process. Folding these into either success or failure would flatter or
libel the build, and this document's neighbour has been corrected six times for exactly that kind of
arithmetic.

Every form and its verdict is in `tools/matlab-checklist/form-probe-results.csv`.
""", encoding="utf-8")

    print(f"accepted {counts['accepted']}  refused {counts['refused']}  "
          f"undefined {counts['undefined']}  error {counts['error']}  unprobed {counts['unprobed']}"
          f"  (total {total})")
    print(f"{whole} commands accept every documented form; {partial} accept some but not all")


if __name__ == "__main__":
    sys.exit(main())
