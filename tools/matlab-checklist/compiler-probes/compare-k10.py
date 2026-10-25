"""Stage K10's comparison (compiler and SDK plan): R2025b's and JGraph's transcripts of the k10 C clients.

recordings/k10_*.out.txt (R2025b's runtime: k10mx and k10app against mclmcrrt, k10feval against the
C shared library R2025b's mcc built) against recordings-jgraph/k10_*.out.txt (the same sources
compiled against the JGraph Runtime's headers and jgrt.lib, k10feval against R2025b's generated
k10lib.c with JGraph's archive appended), section by section ("== safe", "== risky N") and, within
a section, case by case: each "label: value" line, the handler lines (OUT<<...>>, ERR<<...>>) in
order, and how the process ended. Names that differ by construction are made alike first: an
M-file's path in either runtime's cache is its file name, and the run's time is dropped. A
difference is put down to the divergence it is recorded as (ADR 0266) or reported as unexplained.
Writes recordings-jgraph/k10_differences.txt.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
R2025B = HERE / "recordings"
JGRAPH = HERE / "recordings-jgraph"
FILES = ["k10_mx_separate", "k10_mx_interleaved", "k10_app", "k10_feval_separate", "k10_feval_interleaved"]

# What a difference is, by the case it is in: (file pattern, label pattern, the recorded divergence).
KNOWN = [
    (r"feval", r"^(echo|info)\.(int64|uint64|uint64\.odd)$",
     "JGraph keeps a 64-bit integer as a double, exact to 2^53 (JGraph:interop:int64Precision): a value past it is refused, not rounded"),
    (r"feval", r"^make\.(int64|uint64)(\..+)?$", "JGraph's intmin and intmax answer a double, not the class asked for (open item 199)"),
    (r"feval", r"^echo\.sparse$", "a sparse array passed through keeps no spare room (nzmax): JGraph's sparse arrays hold exactly their nonzeros"),
    (r"feval", r"^(echo|info)\.sparse\.complex$|^make\.sparsecomplex(\..+)?$", "JGraph has no complex sparse arrays"),
    (r"feval", r"^(echo|info)\.sparse\.logical$|^make\.sparselogical(\..+)?$", "JGraph has no sparse logical arrays: one becomes sparse double"),
    (r"feval", r"^(echo|info)\.complex\.int8$|^make\.complexint(\..+)?$", "JGraph keeps no complex integer arrays"),
    (r"feval", r"^(echo|info)\.complex\.zeroimag$", "JGraph keeps an element with no imaginary part as a real number"),
    (r"feval", r"^make\.map\.(info|echo\.info|dup\.info)$", "size of a containers.Map is [1 1] in JGraph, [Count 1] in R2025b (open item 200)"),
    (r"feval", r"^make\.structstring(\..+)?$", "struct('s', \"text\") keeps a char, not the string, in JGraph (open item 201)"),
    (r"feval", r"^object\.isclass\.k10obj$", "mxIsClass answers true for an object's own class; R2025b's answers false (ADR 0266)"),
    (r"feval", r"^value\.dup\.set\.original$", "mxDuplicateArray of a value object is a copy; R2025b's shares the original (ADR 0266)"),
    (r"feval", r"^risky\.struct\.invalidname\.info$", "a field read by a name MATLAB could not write ('1bad') is refused by R2025b, read by JGraph (open item 202)"),
    (r"feval", r"^risky\.struct\.(duplicate\.info|echo\.ok|echo)$", "a struct with one field name twice is refused; R2025b passes it in (ADR 0266)"),
    (r"feval", r"^risky\.negative\.nlhs\.ok$", "a negative output count is refused in JGraph's words; R2025b's reports a C++ 'vector too long' (ADR 0266)"),
    (r"app", r"^logfile\.logfile$", "the log file's name, without the '>' R2025b puts before it (ADR 0266)"),
    (r"app", r"^logfile\.text$", "JGraph's runtime makes the log file when it starts; R2025b's when something is written"),
]

# Handler and printed lines that differ by a recorded divergence, by their text.
KNOWN_LINES = [
    (r"^OUT<<(     4\\n\\n|     1     2     3\\n\\n|\\nx =\\n\\n     8\\n\\n|4\\n|\[1, 2, 3\]\\n|x = 8\\n)>>$",
     "a value's display is JGraph's own, not MATLAB's layout (open item 194)"),
    (r"^OUT<<> In k10print \(line 10\)\\n>>$", "a deployed warning prints no '> In' line in JGraph (open item 170)"),
    (r"^ERR<<(Unrecognized function or variable|Undefined function) 'k10_no_such_function'",
     "an undefined function is named in the desktop's words, which R2025b's deployed report does not use"),
]

CRASH = re.compile(r"EXIT\t-\d+ \(0x(C0000005|E06D7363)\)")


def normal(line):
    line = re.sub(r"file .*?\\\\k10lib\\\\(\w+\.m), name", r"file <ctfroot>\\\\\1, name", line)
    return line


def sections(path):
    """The transcript's sections: their cases (label -> the value and the handler lines the call
    printed before it), and how they ended."""
    found, current, pending = {}, None, []
    for raw in path.read_text(encoding="utf-8").splitlines():
        if raw.startswith("== "):
            if current is not None and pending:
                current["cases"]["(end)"] = ("", tuple(pending))
            current, pending = {"cases": {}, "exit": "", "crashed": False}, []
            found[raw[3:]] = current
            continue
        line = normal(raw[2:] if raw.startswith("  ") else raw)
        if current is None or line.startswith(("SECONDS", "STDERR")):
            continue
        if line.startswith("EXIT"):
            current["exit"] = line
            current["crashed"] = bool(CRASH.match(line))
        elif re.match(r"^[a-z][\w.]*: ", line):
            label, value = line.split(": ", 1)
            current["cases"][label] = (value, tuple(l for l in pending if not known_line(l)))
            pending = []
        else:
            pending.append(line)
    if current is not None and pending:
        current["cases"]["(end)"] = ("", tuple(l for l in pending if not known_line(l)))
    return found


def known(file, label):
    for files, pattern, reason in KNOWN:
        if re.search(files, file) and re.match(pattern, label):
            return reason
    return None


def known_line(line):
    for pattern, reason in KNOWN_LINES:
        if re.match(pattern, line):
            return reason
    return None


def show(case):
    if case is None:
        return "(missing)"
    value, lines = case
    return value + ("" if not lines else "  [after " + " | ".join(lines) + "]")


def main():
    out, summary = [], []
    total_unexplained = 0
    for name in FILES:
        theirs_path, ours_path = R2025B / f"{name}.out.txt", JGRAPH / f"{name}.out.txt"
        if not theirs_path.exists() or not ours_path.exists():
            summary.append(f"{name}: not run")
            continue
        theirs, ours = sections(theirs_path), sections(ours_path)
        agree = differ = explained = unexplained = 0
        for title, section in theirs.items():
            mine = ours.get(title, {"cases": {}, "exit": "(missing)", "crashed": False})
            if section["crashed"] and not mine["crashed"]:
                # R2025b's process died in the case; JGraph's answered (ADR 0266).
                differ += 1
                explained += 1
                out.append(f"{name} [{title}]\n  R2025b: crashed ({section['exit']})\n  JGraph: "
                           + "; ".join(f"{k}: {show(v)}" for k, v in mine["cases"].items())
                           + "\n  recorded: R2025b's runtime ends the process here; JGraph's refuses or answers NULL (ADR 0266)")
                continue
            for label, value in section["cases"].items():
                got = mine["cases"].get(label)
                if got == value:
                    agree += 1
                    continue
                differ += 1
                reason = known(name, label)
                if reason:
                    explained += 1
                else:
                    unexplained += 1
                out.append(f"{name} [{title}] {label}\n  R2025b: {show(value)}\n  JGraph: {show(got)}\n  "
                           + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
            for label in mine["cases"]:
                if label not in section["cases"]:
                    reason = known(name, label)
                    if not reason:
                        unexplained += 1
                    out.append(f"{name} [{title}] {label}\n  R2025b: (missing)\n  JGraph: {show(mine['cases'][label])}\n  "
                               + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
        summary.append(f"{name}: {agree} of {agree + differ} cases agree; {explained} recorded divergences, {unexplained} unexplained")
        total_unexplained += unexplained

    text = "\n".join(summary) + "\n\n" + "\n\n".join(out) + "\n"
    (JGRAPH / "k10_differences.txt").write_text(text, encoding="utf-8")
    print("\n".join(summary))
    return 1 if total_unexplained else 0


if __name__ == "__main__":
    sys.exit(main())
