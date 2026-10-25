"""Stage K9's comparison (compiler and SDK plan): R2025b's and JGraph's transcripts of the k9 probes.

recordings/k9_client.out.txt (R2025b's MATLAB Data API and K9 archive) against
recordings-jgraph/k9_client.out.txt (JGraph's, the client's using lines changed), case by case:
each label<TAB>result line, the lines the component printed, and the error reports on standard
error. Then recordings/k9_build.out.txt against JGraph's, key by key, and the main build's
strongly typed interface file by file (recordings/k9_out against recordings-jgraph/k9_out). Names
that differ by construction are made alike first: MathWorks' namespaces are JGraph's, the
interface's header names JGraph's version, an M-file's path in either runtime's cache is its file
name, a compiler-made task type is the task. Writes recordings-jgraph/k9_differences.txt.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
R2025B = HERE / "recordings"
JGRAPH = HERE / "recordings-jgraph"


def normal(text):
    text = text.replace("MathWorks.MATLAB.", "JGraph.MATLAB.")
    text = re.sub(r"(MATLAB|JGraph) Strongly Typed Interface Version: [^\n]*", "Strongly Typed Interface Version: <VERSION>", text)
    # An M-file in the runtime cache: R2025b's <W>\cache_client\R2025b\K9_f5b0\K9\k9err.m, JGraph's own cache.
    text = re.sub(r"in file: [^\s|]*[\\/]([^\\/\s|]+\.m)", r"in file: <cache>\\\1", text)
    text = re.sub(r"file [^,]*[\\/]([^\\/,]+\.m), name", r"file <cache>\\\1, name", text)
    # The compiler numbers an async method's state machine by its position in the class.
    text = re.sub(r"<StartMATLABAsync>d__\d+", "<StartMATLABAsync>d__N", text)
    return text


def client(path):
    """The cases, the component's printed lines in order, and the standard error lines."""
    cases, printed, errors = {}, [], []
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw[2:] if raw.startswith("  ") else raw
        if line.startswith(("BUILD\t", "EXIT", "SECONDS")):
            continue
        if line.startswith("STDERR\t"):
            errors.append(normal(line[len("STDERR\t"):]))
        elif "\t" in line and re.match(r"^[a-z][^\t ]*\t", line):
            label, value = line.split("\t", 1)
            cases[label] = normal(value)
        else:
            printed.append(normal(line))
    return cases, printed, errors


def without_warnings(lines):
    """The lines with the warnings each console echoes left out: R2025b's bracketed message and stack
    (wrapped at its width), JGraph's Warning: line and its > In lines. The .warn keys hold them."""
    kept, skipping = [], 0
    for line in lines:
        if skipping:
            if line.rstrip().endswith("]"):
                skipping -= 1
            continue
        body = line[3:] if line.startswith("  |") else line
        if body.startswith("[Warning:"):
            skipping = 2 if not body.rstrip().endswith("]") else 1
            continue
        if body.startswith("[> In"):
            skipping = 0 if body.rstrip().endswith("]") else 1
            continue
        if body.startswith(("Warning: ", "> In ")):
            continue
        kept.append(line)
    return kept


# msbuild's summary, which R2025b's builds print and JGraph's, compiled in process, do not.
MSBUILD = re.compile(r"^\s*(Build succeeded\.|Build FAILED\.|\d+ Warning\(s\)|\d+ Error\(s\)|Time Elapsed .*)?\s*$")


def build(path):
    keys, current = {}, None
    lines = [l.replace("\b", "") for l in path.read_text(encoding="utf-8").splitlines()]
    for line in without_warnings(lines):
        if line.startswith(("  ", "STDERR")) and current is not None:
            body = line[3:] if line.startswith("  |") else line
            if MSBUILD.match(body) or body.startswith(("Parsing file", "\t(referenced")):
                continue
            keys[current] = keys.get(current, "") + "\n" + " ".join(normal(line).split())
            continue
        key, _, value = line.partition("\t")
        if key.endswith(".seconds"):
            current = None
            continue
        current = key
        if key.endswith(".cs"):
            # The header's version and the namespaces' names make the files' lengths differ by construction.
            value = re.sub(r"^\d+ chars, ", "", value)
        keys[key] = normal(value)
    return keys


def interface(root):
    folder = root / "k9_out" / "strongly_typed_interface"
    return {p.name: normal(p.read_text(encoding="utf-8")) for p in sorted(folder.glob("*.cs"))} if folder.exists() else {}


def main():
    out = []
    summary = []
    if (JGRAPH / "k9_client.out.txt").exists():
        agree = differ = 0
        ours, theirs = client(JGRAPH / "k9_client.out.txt"), client(R2025B / "k9_client.out.txt")
        for label, value in theirs[0].items():
            mine = ours[0].get(label)
            if mine == value:
                agree += 1
                continue
            differ += 1
            out.append(f"{label}\n  R2025b: {value}\n  JGraph: {mine if mine is not None else '(missing)'}")
        for label in ours[0]:
            if label not in theirs[0]:
                out.append(f"{label}\n  R2025b: (missing)\n  JGraph: {ours[0][label]}")
        summary.append(f"client: {agree} of {agree + differ} cases agree")
        if ours[1] != theirs[1]:
            out.append("printed lines\n  R2025b: " + " | ".join(theirs[1]) + "\n  JGraph: " + " | ".join(ours[1]))
        summary.append("printed lines: " + ("same" if ours[1] == theirs[1] else "differ"))
        if ours[2] != theirs[2]:
            out.append("standard error\n  R2025b: " + " | ".join(theirs[2]) + "\n  JGraph: " + " | ".join(ours[2]))
        summary.append("standard error: " + ("same" if ours[2] == theirs[2] else "differs"))

    if (JGRAPH / "k9_build.out.txt").exists():
        mine, them = build(JGRAPH / "k9_build.out.txt"), build(R2025B / "k9_build.out.txt")
        b_agree = b_differ = 0
        for key, value in them.items():
            if mine.get(key) == value:
                b_agree += 1
            else:
                b_differ += 1
                out.append(f"build {key}\n  R2025b: {value}\n  JGraph: {mine.get(key, '(missing)')}")
        for key in mine:
            if key not in them:
                out.append(f"build {key}\n  R2025b: (missing)\n  JGraph: {mine[key]}")
        summary.append(f"build: {b_agree} of {b_agree + b_differ} keys agree")

    mine, them = interface(JGRAPH), interface(R2025B)
    same = sum(1 for name, text in them.items() if mine.get(name) == text)
    for name, text in them.items():
        if mine.get(name) != text:
            out.append(f"interface {name}\n  R2025b:\n{text}\n  JGraph:\n{mine.get(name, '(missing)')}")
    summary.append(f"interface: {same} of {len(them)} files the same")

    text = "\n".join(summary) + "\n\n" + "\n\n".join(out) + "\n"
    (JGRAPH / "k9_differences.txt").write_text(text, encoding="utf-8")
    print("\n".join(summary))
    return 0


if __name__ == "__main__":
    sys.exit(main())
