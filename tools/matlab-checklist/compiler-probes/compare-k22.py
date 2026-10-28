"""Compares the acceptance set's two recordings (compiler and SDK plan, stage K22): what R2025b's
builds and clients printed (recordings/k22_build.out.txt, k22_accept.out.txt) against JGraph's
(recordings-jgraph/). Every line is a key: a build line's label, or a client line's section and
label. A key whose value differs is a recorded divergence when a KNOWN pattern names it, and
unexplained otherwise. Writes recordings-jgraph/k22_differences.txt and exits 1 when anything is
unexplained."""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
THEIRS = HERE / "recordings"
OURS = HERE / "recordings-jgraph"

# Each entry: a name, a regular expression over "section/label", and the reason, in the ADR's words.
KNOWN = [
    ("EXCEL", r"^(build|files)\.excel$|^excel/", "The Excel add-in is stage K17's: R2025b builds it and JGraph has no compiler.build.excelAddIn yet."),
    ("WINDOW_LEAVE", r"^windowed/", "R2025b's windowed program is a MATLAB window and ran only if the user gave leave; JGraph's ran as a JGraph window."),
    ("ASYNC", r"^server/async\.", "muserve, R2025b's one-archive server, has no asynchronous requests (400 'mode=async is not supported.'); jgraph-mps answers the RESTful API's protocol (ADR 0271)."),
    ("FILES", r"^files\.", "The files a build leaves: R2025b's logs, readme and GettingStarted beside the deliverables, JGraph's own set (ADRs 0262-0270)."),
    ("BUILD_TYPE", r"^build\.", "What a build prints: R2025b's Results display line against JGraph's."),
    ("EXIT_TEXT", r"/EXIT$", "The exit code of a program that ended on an error: R2025b's runtime exits 249 after its report (ADR 0256)."),
    ("STDERR", r"/STDERR$", "What goes to standard error."),
]


def read(path):
    """The keyed lines of a recording: build lines by label, client lines by section and label."""
    keyed = {}
    order = []
    section = None
    if not path.exists():
        return keyed, order
    for raw in path.read_text(encoding="utf-8").split("\n"):
        if raw.startswith("== "):
            section = raw[3:].strip()
            continue
        line = raw[2:] if raw.startswith("  ") else raw
        if not line.strip():
            continue
        if "\t" in line:
            label, value = line.split("\t", 1)
        elif section is None:
            # A build's own chatter (mbuild's and midl's lines, dotnet's summary) is not a key.
            continue
        else:
            label, value = line, ""
        key = ("%s/%s" % (section, label)) if section else label
        n = 1
        base = key
        while key in keyed:
            n += 1
            key = "%s#%d" % (base, n)
        keyed[key] = value
        order.append(key)
    return keyed, order


def canonical(value):
    value = re.sub(r"\(request id=[0-9:]+\)", "(request id=<N>)", value)
    # The runtime cache's version folder: R2025b's release name against JGraph's own version (ADR 0258).
    value = re.sub(r"\\cache\\(R2025b|\d+\.\d+\.\d+)\\", r"\\cache\\<VERSION>\\", value)
    value = re.sub(r"\s+$", "", value)
    return value


def main():
    theirs, order = {}, []
    ours = {}
    for name in ("k22_build.out.txt", "k22_accept.out.txt"):
        t, o = read(THEIRS / name)
        theirs.update(t)
        order += [k for k in o if k not in order]
        u, uo = read(OURS / name)
        ours.update(u)
        order += [k for k in uo if k not in order]
    agree = []
    differ = []
    for key in order:
        if key in theirs and key in ours and canonical(theirs[key]) == canonical(ours[key]):
            agree.append(key)
        else:
            differ.append(key)
    explained = {}
    unexplained = []
    for key in differ:
        for name, pattern, _ in KNOWN:
            if re.search(pattern, key):
                explained.setdefault(name, []).append(key)
                break
        else:
            unexplained.append(key)
    lines = ["acceptance set (stage K22): %d keys, %d agree, %d differ (%d recorded divergences, %d unexplained)"
             % (len(order), len(agree), len(differ), len(differ) - len(unexplained), len(unexplained)), ""]
    if unexplained:
        lines.append("UNEXPLAINED")
        for key in unexplained:
            lines.append("  %s" % key)
            lines.append("    R2025b\t%s" % theirs.get(key, "<absent>"))
            lines.append("    JGraph\t%s" % ours.get(key, "<absent>"))
        lines.append("")
    for name, pattern, reason in KNOWN:
        keys = explained.get(name, [])
        if not keys:
            continue
        lines.append("%s (%d): %s" % (name, len(keys), reason))
        for key in keys:
            lines.append("  %s" % key)
            lines.append("    R2025b\t%s" % theirs.get(key, "<absent>"))
            lines.append("    JGraph\t%s" % ours.get(key, "<absent>"))
        lines.append("")
    text = "\n".join(lines).rstrip("\n") + "\n"
    (OURS / "k22_differences.txt").write_text(text, encoding="utf-8")
    print(lines[0])
    for key in unexplained[:40]:
        print("  UNEXPLAINED %s\n    R2025b\t%s\n    JGraph\t%s" % (key, theirs.get(key, "<absent>"), ours.get(key, "<absent>")))
    return 1 if unexplained else 0


if __name__ == "__main__":
    sys.exit(main())
