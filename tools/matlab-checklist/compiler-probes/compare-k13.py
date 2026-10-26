"""Stage K13's comparison (compiler and SDK plan): R2025b's and JGraph's transcripts of the k13 probes.

recordings/k13_*.out.txt (R2025b's compiler.build.pythonPackage and mcc -W python, and k13client.py
against the package R2025b built) against recordings-jgraph/k13_*.out.txt (the same probe under
jgraph -batch, the same client against JGraph's package on the JGraph Runtime):

  build   key by key: each build's answer, the files it wrote (by name: JGraph's archive, inits,
          setup.py, readme and page are its own size), the options' answers
  client  case by case, each mode's cases and the lines printed between them, each runtime's
          error reports, the cache folder of the extracted archive (R2025b's release, JGraph's
          version) read as one

A difference is put down to the divergence it is recorded as (ADR 0269) or reported as unexplained.
Writes recordings-jgraph/k13_differences.txt and exits 1 when anything is unexplained.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
compare_k12 = __import__("compare-k12")

R2025B = HERE / "recordings"
JGRAPH = HERE / "recordings-jgraph"

OWN_WORDS = "JGraph writes the package's __init__.py, setup.py, pyproject.toml, readme.txt and GettingStarted.html in its own words, for the JGraph Runtime (ADR 0269)"

# Build keys whose difference is recorded, by key pattern.
KNOWN_BUILD = [
    (r"\.(results|Options|options)$|^opts\.default$", "an object's display is JGraph's own layout, not R2025b's aligned one (open item 194)"),
    (r"\.RuntimeDependencies$", "JGraph's runtime is one install, so a build lists it alone where R2025b lists its Python add-on too (ADR 0267)"),
    (r"\.(init|toml|file)\.|\.(readme|setup|GettingStarted)$", OWN_WORDS),
    (r"^samples\.(Files|files)$", "JGraph writes no samples from SampleGenerationFiles (open item 207)"),
]

# Client cases whose difference is recorded, by label.
KNOWN_CLIENT = [
    (r"^hash$", "an array is unhashable as R2025b's is (__hash__ is None), so Python's refusal names the class without its module (ADR 0269)"),
    (r"^(print\.capture|print\.stdout|stdout\.after)$", "a value's display is JGraph's own (open item 194), and a deployed warning prints no '> In' line in JGraph (open item 170)"),
    (r"^error\.builtin$", "a deployed report does not head an operator's error with the operator, Error using * (open item 170)"),
    (r"^error\.undefined$", "an undefined function is named in JGraph's words, where R2025b's deployed report says Unrecognized function or variable (open item 203)"),
    (r"^out\.int64$", "JGraph keeps an int64 as a double, so k9make's -9007199254740993 is made as -9007199254740992 (ADR 0266)"),
    (r"^(out|in\.m|echo\.m)\.uint64$|^(in|echo)\.int\.(2\^53\+1|2\^62|max|min)$|^in\.m\.int64$",
     "JGraph keeps a 64-bit integer as a double, exact to 2^53: a value past it is refused, not rounded (ADR 0266)"),
    (r"^(out\.complexint8|in\.m\.int8\.complex)$", "JGraph keeps no complex integer arrays"),
    (r"^out\.sparsecomplex$", "JGraph has no complex sparse arrays, so k9make fails making one"),
    (r"^(in\.complex\.zero|in\.m\.complex\.realvalues)$", "JGraph keeps an element with no imaginary part as a real number"),
    (r"^out\.nestedcell$", "an empty cell inside a cell comes back as an empty list, as an empty cell does alone; R2025b's gives matlab.double([]) inside a cell (ADR 0269)"),
    (r"^in\.(range|range\.list)$", "a range is a sequence as a list or tuple is, a cell row of int64; R2025b's runtime takes it for a dictionary and fails (ADR 0269)"),
    (r"^(in\.datetime|dt\.(tz|offset|list)\.in)$", "a datetime made from Python keeps its TimeZone as char, as JGraph's datetimes do; R2025b's keeps the string it was made with (ADR 0269)"),
    (r"^dt\.micro\.in$", "a datetime keeps its moment as milliseconds since 1899 in a double, about half a microsecond apart near 2026 (ADR 0269)"),
    (r"^dt\.delta\.echo$", "a duration keeps a timedelta's microseconds; R2025b's drops what is finer than a millisecond (ADR 0269)"),
    (r"^after\.terminate_runtime$", "a package started after terminate_runtime() is refused in JGraph's words; R2025b's says MclJmiUtils plugin not available (ADR 0269)"),
    (r"^runtime\.None$", "initialize_runtime(None) starts the runtime with no options, as an empty list does; R2025b's raises SystemError from its extension (ADR 0269)"),
    (r"^runtime\.initialize$", "initialize() after terminate_runtime() is refused at once; R2025b's never returns (ADR 0269)"),
    (r"^end$", "the run went on to its end where R2025b's hung (ADR 0269)"),
]

# Error reports one engine's runtime wrote and the other's did not, by their first line.
KNOWN_REPORTS = [
    (r"^(int8 expects a number|sparse needs numbers)|^Error in k9make \(line (30|61)\)$",
     "JGraph keeps no complex integer arrays and no complex sparse arrays, so k9make fails making one"),
    (r"^Error using \*$|^Incorrect dimensions for matrix multiplication",
     "a deployed report does not head an operator's error with the operator, Error using * (open item 170)"),
    (r"^(Unrecognized function or variable|Undefined function) 'k9nosuchfunction'",
     "an undefined function is named in JGraph's words, where R2025b's deployed report says Unrecognized function or variable (open item 203)"),
    (r"^TypeError: (must be real number, not matlab\.|'matlab\.\w+' object cannot be interpreted)",
     "R2025b's toarray() prints the TypeError it meets to standard error before it refuses an array of two dimensions; JGraph's refuses without it (ADR 0269)"),
]

# Lines printed between cases that differ by construction.
KNOWN_LINES = [
    (r"^ *1$", "a value's display is JGraph's own (open item 194)"),
    (r"^> In k9print", "a deployed warning prints no '> In' line in JGraph (open item 170)"),
]


def known(table, key):
    return compare_k12.known(table, key)


def same_cache(text):
    """The extracted archive's cache folder read as one: R2025b's names its release, JGraph's its version."""
    return re.sub(r"(\\\\?)cache(\\\\?)(R2025b|\d+\.\d+\.\d+)(\\\\?)", r"\1cache\2<V>\4", text)


def public(value):
    """A dir() line with the private names left out: each engine's FutureResult keeps its own."""
    return " ".join(n for n in value.split() if not n.startswith("_"))


def compare_build(out):
    theirs, ours = compare_k12.keyed(R2025B / "k13_build.out.txt"), compare_k12.keyed(JGRAPH / "k13_build.out.txt")
    agree = differ = explained = unexplained = 0
    for key, value in theirs.items():
        mine = ours.get(key)
        if mine == value:
            agree += 1
            continue
        differ += 1
        reason = known(KNOWN_BUILD, key)
        if (key.endswith(".printed") or "\n  |" in value) and mine is not None and reason is None:
            shared = [[l for l in v.split("\n") if compare_k12.SHARED_PRINTED.match(l)] for v in (value, mine)]
            rest = [[l for l in v.split("\n") if not l.startswith("  |Compiler version: ")] for v in (value, mine)]
            if shared[0] == shared[1] and (key.endswith(".printed") or rest[0] == rest[1]):
                reason = "the version line names JGraph's compiler"
        if reason:
            explained += 1
        else:
            unexplained += 1
        out.append(f"k13_build {key}\n  R2025b: {value.strip()[:600]}\n  JGraph: {(mine or '(missing)').strip()[:600]}\n  "
                   + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
    for key in ours:
        if key not in theirs:
            reason = known(KNOWN_BUILD, key)
            if not reason:
                unexplained += 1
            out.append(f"k13_build {key}\n  R2025b: (missing)\n  JGraph: {ours[key].strip()[:600]}\n  "
                       + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
    return f"k13_build: {agree} of {agree + differ} keys agree; {explained} recorded divergences, {unexplained} unexplained", unexplained


def sections(path):
    """Each mode's cases (label -> value and the lines printed before it), standard error and exit."""
    found, current, pending = {}, None, []
    for raw in path.read_text(encoding="utf-8").splitlines():
        if raw.startswith("== "):
            current, pending = {"cases": {}, "stderr": [], "exit": ""}, []
            found[raw[3:]] = current
            continue
        line = raw[2:] if raw.startswith("  ") else raw
        if current is None or line.startswith("SECONDS"):
            continue
        if line.startswith("STDERR"):
            text = line.partition("\t")[2]
            if not text.startswith("TIMES\t"):
                current["stderr"].append(same_cache(text))
            continue
        if line.startswith("EXIT"):
            current["exit"] = line
            continue
        label, tab, value = line.partition("\t")
        if tab and re.match(r"^[A-Za-z_][\w.^+#-]*$", label):
            value = same_cache(value)
            if label.endswith(".public"):
                value = public(value)
            current["cases"][label] = (value, tuple(l for l in pending if not known(KNOWN_LINES, l)))
            pending = []
        else:
            pending.append(line)
    return found


def compare_client(out):
    theirs, ours = sections(R2025B / "k13_client.out.txt"), sections(JGRAPH / "k13_client.out.txt")
    agree = differ = explained = unexplained = 0
    for title, section in theirs.items():
        mine = ours.get(title, {"cases": {}, "stderr": [], "exit": "(missing)"})
        if section["exit"] != mine["exit"]:
            reason = "R2025b's initialize() after terminate_runtime() never returns (ADR 0269)" if title == "runtime terminate" else None
            explained += 1 if reason else 0
            unexplained += 0 if reason else 1
            out.append(f"k13_client [{title}] exit\n  R2025b: {section['exit']}\n  JGraph: {mine['exit']}\n  "
                       + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
        for label, value in section["cases"].items():
            got = mine["cases"].get(label)
            if got == value:
                agree += 1
                continue
            differ += 1
            reason = known(KNOWN_CLIENT, label)
            explained += 1 if reason else 0
            unexplained += 0 if reason else 1
            out.append(f"k13_client [{title}] {label}\n  R2025b: {value}\n  JGraph: {got}\n  " + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
        for label in mine["cases"]:
            if label not in section["cases"]:
                reason = known(KNOWN_CLIENT, label)
                if not reason:
                    unexplained += 1
                out.append(f"k13_client [{title}] {label}\n  R2025b: (missing)\n  JGraph: {mine['cases'][label]}\n  "
                           + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
        theirs_reports, our_reports = compare_k12.reports(section["stderr"]), compare_k12.reports(mine["stderr"])
        for report in theirs_reports:
            if report in our_reports:
                our_reports.remove(report)
                agree += 1
                continue
            differ += 1
            reason = known(KNOWN_REPORTS, report[0])
            explained += 1 if reason else 0
            unexplained += 0 if reason else 1
            out.append(f"k13_client [{title}] standard error, R2025b's report\n  " + " | ".join(report) + "\n  "
                       + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
        for report in our_reports:
            differ += 1
            reason = known(KNOWN_REPORTS, report[0])
            explained += 1 if reason else 0
            unexplained += 0 if reason else 1
            out.append(f"k13_client [{title}] standard error, JGraph's report\n  " + " | ".join(report) + "\n  "
                       + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
    return f"k13_client: {agree} of {agree + differ} cases and reports agree; {explained} recorded divergences, {unexplained} unexplained", unexplained


def main():
    out, summary, total = [], [], 0
    for name, compare in (("k13_build", compare_build), ("k13_client", compare_client)):
        if (R2025B / f"{name}.out.txt").exists() and (JGRAPH / f"{name}.out.txt").exists():
            line, bad = compare(out)
            summary.append(line)
            total += bad
        else:
            summary.append(f"{name}: not run")
    text = "\n".join(summary) + "\n\n" + "\n\n".join(out) + "\n"
    (JGRAPH / "k13_differences.txt").write_text(text, encoding="utf-8")
    print("\n".join(summary))
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main())
