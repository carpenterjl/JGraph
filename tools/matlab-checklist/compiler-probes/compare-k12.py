"""Stage K12's comparison (compiler and SDK plan): R2025b's and JGraph's transcripts of the k12 probes.

recordings/k12_*.out.txt (R2025b's compiler.build.cppSharedLibrary and mcc -W cpplib, and k12client
against the libraries R2025b built) against recordings-jgraph/k12_*.out.txt (the same probe under
jgraph -batch, the same client compiled against JGraph's headers and the header JGraph generated,
run against JGraph's archives):

  build   key by key: each build's answer, the files it wrote (by name: JGraph's archive, readmes
          and pages are its own size), the generated header line by line (its version line, which
          names each engine, aside), the options' answers
  client  case by case, each mode's cases and the lines printed between them
  paths   case by case, where the client finds an archive in record-k12.ps1's four setups
  stderr  the error reports each engine's runtime wrote, report by report

A difference is put down to the divergence it is recorded as (ADR 0268) or reported as unexplained.
Writes recordings-jgraph/k12_differences.txt and exits 1 when anything is unexplained.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
R2025B = HERE / "recordings"
JGRAPH = HERE / "recordings-jgraph"

LEGACY = "JGraph builds no legacy mwArray interface of a C++ shared library (decision KD7): R2025b's library, its source and its import library are not written"

# Build keys whose difference is recorded, by key pattern.
KNOWN_BUILD = [
    (r"^(mwarray|mwarrayver)(\..+)?$", LEGACY + "; Interface 'mwarray' is refused"),
    (r"^mcc_(default|all|verfirst)(\..+)?$", LEGACY + "; -W cpplib:name writes the generic interface's archive alone, with JGraph's warning"),
    (r"^mcc_legacy(\..+)?$", LEGACY + "; -W cpplib:name,legacy is refused"),
    (r"\.(results|Options|options)$|^opts\.default$", "an object's display is JGraph's own layout, not R2025b's aligned one (open item 194)"),
    (r"\.(readme|generic\.readme|GettingStarted)$", "readme.txt and GettingStarted.html are in JGraph's words, for the JGraph Runtime"),
]

# The lines of a build's printed text both engines print alike: compiler.build's mcc command, the
# header's count line, the analysis and the files generated; the version line names each engine.
SHARED_PRINTED = re.compile(r"^  \|(mcc |\d+ class\(es\)|Analyzing|Parsing|\t\(referenced|Generating file)")

# Client and paths cases whose difference is recorded, by label.
KNOWN_CLIENT = [
    (r"^cell\.nested\.index$", "a cell's element read as an Array past the end throws InvalidArrayIndexException, as R2025b's own header documents; "
     "R2025b throws its internal foundation::matlabdata::EArrayIndexOutOfRange, a bare std::exception (ADR 0268)"),
    (r"^(in|echo)\.(int64|uint64)$|^make\.uint64$", "JGraph keeps a 64-bit integer as a double, exact to 2^53: a value past it is refused, not rounded (ADR 0266)"),
    (r"^make\.int64$", "JGraph's intmin and intmax answer a double, not the class asked for (open item 199)"),
    (r"^(make|in|echo)\.complexint8$|^t\.ints\.complex$", "JGraph keeps no complex integer arrays"),
    (r"^(make|in|echo)\.sparselogical$", "JGraph has no sparse logical arrays: one becomes sparse double"),
    (r"^(make|in|echo)\.sparsecomplex$", "JGraph has no complex sparse arrays"),
    (r"^(in|echo)\.complexzero$", "JGraph keeps an element with no imaginary part as a real number"),
    (r"^(in|echo)\.empty03$", "an empty array keeps its shape (0-by-3); R2025b's Data API hands MATLAB 0-by-0 (ADR 0268)"),
    (r"^obj\.across\.libraries$", "an object goes back only to the library that returned it; R2025b's passes it to another library of the application (ADR 0268)"),
    (r"^lib\.two\.of\.one$", "each library runs in a session of its own, as R2025b's does (since stage K13, ADR 0269), so an object of one is "
     "refused by the other, in JGraph's words and without R2025b's identifier MATLAB:matlab_data_array:invalidHandleObjectRef"),
    (r"^(print|print\.buffers|print\.output\.only|native\.buffers)$", "a value's display is JGraph's own (open item 194), and a deployed warning prints no '> In' line in JGraph (open item 170)"),
    (r"^native\.tuple$", "a char result is taken as text in a typed tuple, as it is alone; R2025b's tuple refuses it (ADR 0268)"),
    (r"^native\.error$", "R2025b's stack has a frame of its own, fevalWithCustomTypeConversion, below the function's (ADR 0268)"),
    (r"^t\.nv(\.complex)?$", "the generated header passes a name-value argument with its name; R2025b's passes it by position, which the function refuses (ADR 0268)"),
    (r"^t\.inner$", "JGraph has no packages, so k9pkg.k9inner is not callable by its package name (open item 195)"),
]

# Lines printed between cases that differ by construction.
KNOWN_LINES = [
    (r"^ *1$", "a value's display is JGraph's own (open item 194)"),
    (r"^Warning: Property '\w+' in class 'k9point' uses an obsolete validation datatype", "R2025b's warning about k9point's own syntax, printed when another library loads the class"),
    (r"^\[Warning: Function version has the same name as a MATLAB built-in", "R2025b's warning when its out-of-process runtime starts"),
]

# Error reports one engine's runtime wrote and the other's did not, by their first line: the
# functions the cases above fail in.
KNOWN_REPORTS = [
    (r"^Error using k9nv \(line 1\)$", "the generated header passes k9nv's name-value arguments with their names, so the call succeeds (ADR 0268)"),
    (r"^(int8 expects a number|sparse needs numbers)|^Error in k9make \(line (30|61)\)$",
     "JGraph keeps no complex integer arrays and no complex sparse arrays, so k9make fails making one"),
    (r"^Error using \*$|^Incorrect dimensions for matrix multiplication",
     "a deployed report does not head an operator's error with the operator, Error using * (open item 170)"),
    (r"^(Unrecognized function or variable|Undefined function) 'k9nosuchfunction'",
     "an undefined function is named in JGraph's words, where R2025b's deployed report says Unrecognized function or variable (open item 203)"),
    (r"^Error in matlab\.engine\.internal\.fevalWithCustomTypeConversion",
     "R2025b's stack has a frame of its own, fevalWithCustomTypeConversion, below the function's (ADR 0268)"),
    (r"^Error using clear$", "R2025b's own error as it clears k9color while the runtime ends"),
    (r"^feval: 'k9pkg\.k9inner' is not a function\.$", "JGraph has no packages, so k9pkg.k9inner is not callable by its package name (open item 195)"),
    (r"^Invalid HandleObjectRef", "an object of another library is refused in JGraph's words, which the refusal does not also write to standard error (ADR 0269)"),
]


def known(table, key):
    for pattern, reason in table:
        if re.search(pattern, key):
            return reason
    return None


def without_warnings(lines):
    """Lines with the warnings each console echoes left out: R2025b's bracketed message and stack,
    JGraph's Warning: line and its > In lines. The .warn keys hold them."""
    kept, skipping = [], 0
    for line in lines:
        bare = line[3:] if line.startswith("  |") else line
        if skipping:
            if bare.rstrip().endswith("]"):
                skipping -= 1
            continue
        if bare.startswith("[Warning:") or bare.startswith("[> In"):
            skipping = 0 if bare.rstrip().endswith("]") else 1
            continue
        if bare.startswith(("Warning: ", "> In ")):
            continue
        kept.append(line)
    return kept


def keyed(path):
    """Each key's value, a display's or a file's lines joined under it; the build times, the header's
    version line and the sizes in a file list left out."""
    keys, current = {}, None
    # JGraph's -batch writes a warning to standard error, where R2025b's prints it among the output.
    lines = without_warnings([l.replace("\b", "") for l in path.read_text(encoding="utf-8").splitlines()
                              if not l.startswith(("STDERR\tWarning: ", "STDERR\t> In "))])
    for line in lines:
        if line.startswith(("  ", "STDERR")) and current is not None:
            if re.match(r"^  \|\* (MATLAB|JGraph) Strongly Typed Interface Version: ", line):
                continue
            keys[current] = keys.get(current, "") + "\n" + line.rstrip()
            continue
        key, _, value = line.partition("\t")
        if key.endswith(".seconds") or key == "":
            current = None
            continue
        value = re.sub(r"^\d+ chars, ", "", value)
        if key.endswith(".files"):
            value = re.sub(r" \(\d+\)", "", value)
        current = key
        keys[key] = value
    return keys


def header_by_name(theirs, mine):
    """Whether a generated header differs from R2025b's only by the name-value arguments' names."""
    named = [l for l in mine.split("\n") if '_arrayFactory.createCharArray("' not in l]
    return "\n".join(named) == theirs


def compare_build(out):
    theirs, ours = keyed(R2025B / "k12_build.out.txt"), keyed(JGRAPH / "k12_build.out.txt")
    agree = differ = explained = unexplained = 0
    for key, value in theirs.items():
        mine = ours.get(key)
        if mine == value:
            agree += 1
            continue
        differ += 1
        reason = known(KNOWN_BUILD, key)
        if ".hpp." in key and mine is not None and header_by_name(value, mine):
            reason = "the generated header passes a name-value argument with its name before it; R2025b's passes it by position (ADR 0268)"
        # A build's printed text: compiler.build's (.printed) or an mcc form's own lines under its key.
        if (key.endswith(".printed") or "\n  |" in value) and mine is not None and reason is None:
            shared = [[l for l in v.split("\n") if SHARED_PRINTED.match(l)] for v in (value, mine)]
            rest = [[l for l in v.split("\n") if not l.startswith("  |Compiler version: ")] for v in (value, mine)]
            if shared[0] == shared[1] and (key.endswith(".printed") or rest[0] == rest[1]):
                reason = "the version line names JGraph's compiler"
        if reason:
            explained += 1
        else:
            unexplained += 1
        out.append(f"k12_build {key}\n  R2025b: {value.strip()[:600]}\n  JGraph: {(mine or '(missing)').strip()[:600]}\n  "
                   + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
    for key in ours:
        if key not in theirs:
            reason = known(KNOWN_BUILD, key)
            if not reason:
                unexplained += 1
            out.append(f"k12_build {key}\n  R2025b: (missing)\n  JGraph: {ours[key].strip()[:600]}\n  "
                       + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
    return f"k12_build: {agree} of {agree + differ} keys agree; {explained} recorded divergences, {unexplained} unexplained", unexplained


def sections(path):
    """Each mode's cases (label -> value and the lines printed before it) and its standard error."""
    found, current, pending = {}, None, []
    for raw in path.read_text(encoding="utf-8").splitlines():
        if raw.startswith("== "):
            current, pending = {"cases": {}, "stderr": [], "exit": ""}, []
            found[raw[3:]] = current
            continue
        line = (raw[2:] if raw.startswith("  ") else raw).replace("\b", "")
        if current is None or line.startswith("SECONDS"):
            continue
        if line.startswith("STDERR"):
            current["stderr"].append(line.partition("\t")[2])
            continue
        if line.startswith("EXIT"):
            current["exit"] = line
            continue
        label, tab, value = line.partition("\t")
        if tab and re.match(r"^[A-Za-z][\w.]*$", label):
            current["cases"][label] = (value, tuple(l for l in pending if not known(KNOWN_LINES, l)))
            pending = []
        else:
            pending.append(line)
    return found


def reports(lines):
    """Standard error's error reports, each its lines up to the blank line that ends it."""
    found, current = [], []
    for line in lines:
        if line.strip() == "":
            if current:
                found.append(tuple(current))
            current = []
        else:
            current.append(line)
    if current:
        found.append(tuple(current))
    return found


def compare_sections(name, theirs_path, ours_path, out):
    theirs, ours = sections(theirs_path), sections(ours_path)
    agree = differ = explained = unexplained = 0
    for title, section in theirs.items():
        mine = ours.get(title, {"cases": {}, "stderr": [], "exit": "(missing)"})
        if section["exit"] != mine["exit"]:
            unexplained += 1
            out.append(f"{name} [{title}] exit\n  R2025b: {section['exit']}\n  JGraph: {mine['exit']}\n  UNEXPLAINED")
        for label, value in section["cases"].items():
            got = mine["cases"].get(label)
            if got == value:
                agree += 1
                continue
            differ += 1
            reason = known(KNOWN_CLIENT, label)
            if reason:
                explained += 1
            else:
                unexplained += 1
            out.append(f"{name} [{title}] {label}\n  R2025b: {value}\n  JGraph: {got}\n  " + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
        for label in mine["cases"]:
            if label not in section["cases"]:
                reason = known(KNOWN_CLIENT, label)
                if not reason:
                    unexplained += 1
                out.append(f"{name} [{title}] {label}\n  R2025b: (missing)\n  JGraph: {mine['cases'][label]}\n  "
                           + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
        theirs_reports, our_reports = reports(section["stderr"]), reports(mine["stderr"])
        for report in theirs_reports:
            if report in our_reports:
                our_reports.remove(report)
                agree += 1
                continue
            differ += 1
            reason = known(KNOWN_REPORTS, report[0])
            explained += 1 if reason else 0
            unexplained += 0 if reason else 1
            out.append(f"{name} [{title}] standard error, R2025b's report\n  " + " | ".join(report) + "\n  "
                       + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
        for report in our_reports:
            differ += 1
            reason = known(KNOWN_REPORTS, report[0])
            explained += 1 if reason else 0
            unexplained += 0 if reason else 1
            out.append(f"{name} [{title}] standard error, JGraph's report\n  " + " | ".join(report) + "\n  "
                       + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
    return f"{name}: {agree} of {agree + differ} cases and reports agree; {explained} recorded divergences, {unexplained} unexplained", unexplained


def main():
    out, summary = [], []
    total = 0
    if (R2025B / "k12_build.out.txt").exists() and (JGRAPH / "k12_build.out.txt").exists():
        line, bad = compare_build(out)
        summary.append(line)
        total += bad
    else:
        summary.append("k12_build: not run")

    for name in ("k12_client", "k12_paths"):
        theirs_path, ours_path = R2025B / f"{name}.out.txt", JGRAPH / f"{name}.out.txt"
        if theirs_path.exists() and ours_path.exists():
            line, bad = compare_sections(name, theirs_path, ours_path, out)
            summary.append(line)
            total += bad
        else:
            summary.append(f"{name}: not run")

    text = "\n".join(summary) + "\n\n" + "\n\n".join(out) + "\n"
    (JGRAPH / "k12_differences.txt").write_text(text, encoding="utf-8")
    print("\n".join(summary))
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main())
