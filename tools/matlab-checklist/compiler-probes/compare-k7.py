"""Compares JGraph's run of a stage K7 probe with R2025b's recording (compiler and SDK plan, stage K7).

    python compare-k7.py <R2025b recording> <JGraph's output> <JGraph's work folder> [<differences file>]

Each case's status (ok, or the refusal's identifier and text), its warning, and the names of the
files it wrote are compared; the files' sizes are not (JGraph's launcher and archive are its own).
Machine paths in JGraph's output become R2025b's tokens first. JGraph's files are listed from its
work folder (<work>\\b\\<case> and <work>\\b2\\<case>), as JGraph's dir does not yet search ** (open
item 188). Prints, and with a fourth argument writes, one block per case that differs.
"""
import os
import re
import sys


def listing(folder):
    names = []
    for root, _, files in os.walk(folder):
        for f in files:
            names.append(os.path.relpath(os.path.join(root, f), folder))
    return " | ".join(sorted(names))


def cases(path, work=None):
    text = open(path, encoding="utf-8-sig").read().replace("\r\n", "\n")
    if work:
        for w in (work, work.replace("\\", "/")):
            text = text.replace(w, "<W>")
    out = {}
    order = []
    for line in text.split("\n"):
        m = re.match(r"^(mcc\.[A-Za-z0-9_]+|after\.[A-Za-z0-9_.]+)(\.warn|\.files|\.cwd\.files)?\t(.*)$", line)
        if not m:
            continue
        key, part, value = m.group(1), m.group(2) or "", m.group(3)
        if part == "" and value.startswith("ok"):
            value = "ok"  # the build's time is not compared
        if part.endswith("files"):
            value = " | ".join(re.sub(r" \(\d+\)$", "", f) for f in value.split(" | ") if f)
            if work and part == ".files":
                for sub in ("b", "b2"):
                    folder = os.path.join(work, sub, key[len("mcc."):])
                    if os.path.isdir(folder):
                        value = listing(folder)
                        break
        if key not in out:
            out[key] = {}
            order.append(key)
        out[key][part] = value
    return out, order


def main():
    r2025b, order = cases(sys.argv[1])
    jgraph, _ = cases(sys.argv[2], sys.argv[3])
    blocks = []
    for key in order:
        mine = jgraph.get(key, {})
        theirs = r2025b[key]
        diffs = []
        for part in sorted(set(theirs) | set(mine)):
            if theirs.get(part) != mine.get(part):
                diffs.append(f"  {part or 'status'}\n    R2025b: {theirs.get(part)}\n    JGraph: {mine.get(part)}")
        if diffs:
            blocks.append(f"CASE\t{key}\n" + "\n".join(diffs))
    report = "\n".join(blocks) + ("\n" if blocks else "")
    print(report, end="")
    print(f"{len(blocks)} of {len(order)} cases differ")
    if len(sys.argv) > 4:
        with open(sys.argv[4], "w", encoding="utf-8", newline="\n") as f:
            f.write(report + f"{len(blocks)} of {len(order)} cases differ\n")


main()
