"""Stage K8's comparison (compiler and SDK plan): R2025b's and JGraph's transcripts of the k8 probes.

recordings/k8_client.out.txt (R2025b's MWArray.dll and K8 assembly) against
recordings-jgraph/k8_client.out.txt (JGraph's, the client's using lines changed), case by case:
each label<TAB>result line, the lines the component printed, and the error reports on standard
error. Then recordings/k8_build.out.txt against JGraph's, key by key. Names and paths that differ by
construction are made alike first: MathWorks' namespace is JGraph's, an M-file's path in either
runtime's cache is its file name. Writes recordings-jgraph/k8_differences.txt.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
R2025B = HERE / "recordings"
JGRAPH = HERE / "recordings-jgraph"


def normal(text):
    text = text.replace("MathWorks.MATLAB.", "JGraph.MATLAB.")
    # An M-file in the runtime cache: R2025b's <W>\cache_client\R2025b\K8_f5b0\K8\k8make.m, JGraph's own cache.
    text = re.sub(r"file [^,]*[\\/]([^\\/,]+\.m), name", r"file <cache>\\\1, name", text)
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
        elif "\t" in line and re.match(r"^[a-z][\w.]*\t", line):
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
        if line.startswith("[Warning:"):
            skipping = 2 if not line.rstrip().endswith("]") else 1
            continue
        if line.startswith("[> In"):
            skipping = 0 if line.rstrip().endswith("]") else 1
            continue
        if line.startswith(("Warning: ", "> In ")):
            continue
        kept.append(line)
    return kept


def build(path):
    keys, current = {}, None
    for line in without_warnings([l.replace("\b", "") for l in path.read_text(encoding="utf-8").splitlines()]):
        if line.startswith(("  ", "STDERR")) and current is not None:
            keys[current] = keys.get(current, "") + "\n" + " ".join(line.split())
            continue
        key, _, value = line.partition("\t")
        if key.endswith(".seconds"):
            current = None
            continue
        current = key
        keys[key] = value
    return keys


def main():
    out = []
    agree = differ = 0
    ours, theirs = client(JGRAPH / "k8_client.out.txt"), client(R2025B / "k8_client.out.txt")
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
    summary = [f"client: {agree} of {agree + differ} cases agree"]
    if ours[1] != theirs[1]:
        out.append("printed lines\n  R2025b: " + " | ".join(theirs[1]) + "\n  JGraph: " + " | ".join(ours[1]))
    summary.append("printed lines: " + ("same" if ours[1] == theirs[1] else "differ"))
    if ours[2] != theirs[2]:
        out.append("standard error\n  R2025b: " + " | ".join(theirs[2]) + "\n  JGraph: " + " | ".join(ours[2]))
    summary.append("standard error: " + ("same" if ours[2] == theirs[2] else "differs"))

    if (JGRAPH / "k8_build.out.txt").exists():
        mine, them = build(JGRAPH / "k8_build.out.txt"), build(R2025B / "k8_build.out.txt")
        b_agree = b_differ = 0
        for key, value in them.items():
            if mine.get(key) == value:
                b_agree += 1
            else:
                b_differ += 1
                out.append(f"build {key}\n  R2025b: {value}\n  JGraph: {mine.get(key, '(missing)')}")
        summary.append(f"build: {b_agree} of {b_agree + b_differ} keys agree")

    text = "\n".join(summary) + "\n\n" + "\n\n".join(out) + "\n"
    (JGRAPH / "k8_differences.txt").write_text(text, encoding="utf-8")
    print("\n".join(summary))
    return 0


if __name__ == "__main__":
    sys.exit(main())
