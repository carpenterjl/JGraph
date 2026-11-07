"""Compares the P7 acceptance's two sets of recordings (packages and class folders plan, stage P7):
what R2025b printed (p7_accept.out, p7_build.out, p7_tool.out, p7_project.out) against what JGraph
printed (the .jgraph.out files beside them). Every line is a key - the file's stem and the label
before the tab. A key whose value differs is a recorded divergence when a KNOWN pattern names it,
and unexplained otherwise. Writes p7_differences.txt and exits 1 when anything is unexplained."""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
STEMS = ["p7_accept", "p7_build", "p7_tool", "p7_project"]

# Each entry: a name, a regular expression over "stem/label", and the reason, in the ADR's words.
KNOWN = [
    ("ARCHIVE", r"^p7_build/(archive|manifest\.dirs)", "The archive's own entries - R2025b's .META files and its toolbox/compiler and toolbox/local patches, with their manifest folders - are the MATLAB Runtime's; JGraph's archive holds the program's files (ADR 0258). The library's files are the same in both."),
    ("EXIT", r"^p7_tool/EXIT$", "The exit code line is the runner's, not the program's."),
    ("CLASS_FOLDER_CACHE", r"^p7_project/resolves\.(closed|rehash)$", "After close(proj) takes the library off the path, R2025b still answers exist('Series') 2 for the class folder it had seen, rehash or not (its class-folder cache, the mirror of P4's 'a new method file needs clear classes'); JGraph forgets the folder with the path entry, as it sees a new one at once (ADR 0310)."),
]


def read(path):
    keyed = {}
    order = []
    if not path.exists():
        return keyed, order
    for raw in path.read_text(encoding="utf-8").split("\n"):
        line = raw[2:] if raw.startswith("  ") else raw
        if not line.strip() or line.startswith("== "):
            continue
        if "\t" in line:
            label, value = line.split("\t", 1)
        else:
            label, value = line, ""
        key = label
        n = 1
        while key in keyed:
            n += 1
            key = "%s#%d" % (label, n)
        keyed[key] = value
        order.append(key)
    return keyed, order


def canonical(value):
    return re.sub(r"\s+$", "", value)


def main():
    theirs, ours, order = {}, {}, []
    for stem in STEMS:
        t, o = read(HERE / (stem + ".out"))
        u, uo = read(HERE / (stem + ".jgraph.out"))
        for k, v in t.items():
            theirs[stem + "/" + k] = v
        for k, v in u.items():
            ours[stem + "/" + k] = v
        order += [stem + "/" + k for k in o]
        order += [stem + "/" + k for k in uo if k not in o]
    agree, differ = [], []
    for key in order:
        if key in theirs and key in ours and canonical(theirs[key]) == canonical(ours[key]):
            agree.append(key)
        else:
            differ.append(key)
    lines = ["P7 acceptance: %d keys, %d agree, %d differ" % (len(order), len(agree), len(differ)), ""]
    unexplained = 0
    for key in differ:
        reason = next((name for name, pattern, _ in KNOWN if re.search(pattern, key)), None)
        if reason is None:
            unexplained += 1
        lines.append("%s %s" % ("DIV " + reason if reason else "UNEXPLAINED", key))
        lines.append("  R2025b: %s" % theirs.get(key, "<missing>"))
        lines.append("  JGraph: %s" % ours.get(key, "<missing>"))
    lines.append("")
    lines.append("%d recorded divergences, %d unexplained" % (len(differ) - unexplained, unexplained))
    lines.append("")
    for name, _, reason in KNOWN:
        lines.append("%s: %s" % (name, reason))
    text = "\n".join(lines) + "\n"
    (HERE / "p7_differences.txt").write_text(text, encoding="utf-8")
    print(text)
    return 1 if unexplained else 0


if __name__ == "__main__":
    sys.exit(main())
