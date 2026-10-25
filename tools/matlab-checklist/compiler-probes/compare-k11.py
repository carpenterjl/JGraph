"""Stage K11's comparison (compiler and SDK plan): R2025b's and JGraph's transcripts of the k11 probes.

recordings/k11_*.out.txt (R2025b's mcc -W lib, compiler.build.cSharedLibrary, and k11client against
the library R2025b built) against recordings-jgraph/k11_*.out.txt (the same probe .m files under
jgraph -batch, the same client against the library JGraph's mcc built):

  build   key by key: each build's answer, the files it wrote, the generated text line by line
          (the header's first line, which names the compiler, aside), the options' answers
  names   the generated wrappers of the odd signatures and long names, line by line
  client  case by case, as compare-k10.py compares its clients
  feval   stage K10's k10feval run against JGraph's own generated k10lib (recordings-jgraph/
          k11_feval_<model>) against R2025b's run against R2025b's (recordings/k10_feval_<model>),
          with stage K10's recorded divergences
  pe      each DLL's version block and what is appended to it

A difference is put down to the divergence it is recorded as (ADR 0267) or reported as unexplained.
Writes recordings-jgraph/k11_differences.txt and exits 1 when anything is unexplained.
"""
import importlib.util
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
R2025B = HERE / "recordings"
JGRAPH = HERE / "recordings-jgraph"

_spec = importlib.util.spec_from_file_location("compare_k10", HERE / "compare-k10.py")
k10 = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(k10)

# Build keys whose difference is recorded, by key pattern.
KNOWN_BUILD = [
    (r"^clash(\..+)?$", "an argument named nargout is renamed nargout0 so the library compiles; R2025b's wrapper declares nargout twice and its build fails"),
    (r"\.printed$", "what a build prints past mbuild's 'Building with' line is JGraph's own: its compiler's command lines, not mbuild's search"),
    (r"\.(readme|gettingstarted)$", "readme.txt and GettingStarted.html are in JGraph's words, for the JGraph Runtime"),
    (r"\.(results|Options)$|^opts\.(default|named)$", "an object's display is JGraph's own layout, not R2025b's aligned one (open item 194)"),
    (r"^compile\.files$", "-T compile:lib writes the archive beside the object file, which R2025b's does not write at all"),
]

# The lines of a build's printed text both engines print alike: compiler.build's mcc command, the
# analysis, the files generated, and the compiler named; the version line names each engine.
SHARED_PRINTED = re.compile(r"^  \|(mcc |Analyzing|Parsing|\t\(referenced|Generating file|Building with)")

# Names whose wrapper text differs by a recorded divergence, by build.
KNOWN_NAMES = [
    (r"^k11cpp\.", "JGraph cannot read a function whose argument is named true (open item 205), so its wrappers take no arguments"),
    (r"^k11cw3\.", "restrict, mxArray and MW_CALL_CONV are renamed with a 0 so the library compiles; R2025b keeps them and its source does not"),
    (r"^k11cw4\.", "an argument named mclMlfFeval is renamed with a 0 so the wrapper's call compiles; R2025b keeps it"),
    (r"^k11nargin\.", "an argument named nargout is renamed nargout0 beside the wrapper's own nargout; R2025b declares nargout twice"),
]

# Client cases whose difference is recorded, by label: none expected.
KNOWN_CLIENT = []

# DLL facts that differ by construction.
KNOWN_PE = [
    (r"^import\t", "the library imports jgrt.dll and links the C runtime statically; R2025b's imports mclmcrrt25_2.dll and the Visual C++ runtime"),
    (r"^sections\t", "the static C runtime brings its own sections (.fptable)"),
    (r"^fixed\.FileType\t", "the version block names a DLL (VFT_DLL); R2025b's writes 0, the unknown type"),
]


def without_warnings(lines):
    """Lines with the warnings each console echoes left out, as compare-k8.py leaves them: R2025b's
    bracketed message and stack, JGraph's Warning: line and its > In lines. The .warn keys hold them."""
    kept, skipping = [], 0
    for line in lines:
        bare = line[3:] if line.startswith("  |") else line
        if skipping:
            if bare.rstrip().endswith("]"):
                skipping -= 1
            continue
        if bare.startswith("[Warning:"):
            skipping = 0 if bare.rstrip().endswith("]") else 1
            continue
        if bare.startswith("[> In"):
            skipping = 0 if bare.rstrip().endswith("]") else 1
            continue
        if bare.startswith(("Warning: ", "> In ")) or (line.startswith("  |") and bare == "" and kept and kept[-1].endswith(".printed")):
            continue
        kept.append(line)
    return kept


def keyed(path):
    """Each key's value, a display's or a file's lines joined under it; the build times and the
    generated header's compiler line (R2025b's or JGraph's name) left out."""
    keys, current = {}, None
    lines = without_warnings([l.replace("\b", "") for l in path.read_text(encoding="utf-8").splitlines()])
    for line in lines:
        if line.startswith(("  ", "STDERR")) and current is not None:
            if re.match(r"^  \| \* (MATLAB|JGraph) Compiler: ", line):
                continue
            keys[current] = keys.get(current, "") + "\n" + line.rstrip()
            continue
        key, _, value = line.partition("\t")
        if key.endswith(".seconds") or key == "":
            current = None
            continue
        # A generated file's size counts its header, which names the compiler: compared by its lines.
        value = re.sub(r"^\d+ chars, ", "", value)
        current = key
        keys[key] = value
    return keys


def known(table, key):
    for pattern, reason in table:
        if re.search(pattern, key):
            return reason
    return None


def compare_keyed(name, theirs_path, ours_path, table, out):
    theirs, ours = keyed(theirs_path), keyed(ours_path)
    agree = differ = explained = unexplained = 0
    for key, value in theirs.items():
        mine = ours.get(key)
        if mine == value:
            agree += 1
            continue
        differ += 1
        reason = known(table, key)
        if key.endswith(".printed") and mine is not None and not key.startswith("clash"):
            shared = [[l for l in v.split("\n") if SHARED_PRINTED.match(l)] for v in (value, mine)]
            if shared[0] != shared[1]:
                reason = None
        if reason:
            explained += 1
        else:
            unexplained += 1
        out.append(f"{name} {key}\n  R2025b: {value.strip()[:600]}\n  JGraph: {(mine or '(missing)').strip()[:600]}\n  "
                   + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
    for key in ours:
        if key not in theirs:
            reason = known(table, key)
            if not reason:
                unexplained += 1
            out.append(f"{name} {key}\n  R2025b: (missing)\n  JGraph: {ours[key].strip()[:600]}\n  "
                       + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
    return f"{name}: {agree} of {agree + differ} keys agree; {explained} recorded divergences, {unexplained} unexplained", unexplained


def compare_sections(name, theirs_path, ours_path, known_case, out):
    """Case by case, as compare-k10.py compares a client's transcript."""
    theirs, ours = k10.sections(theirs_path), k10.sections(ours_path)
    agree = differ = explained = unexplained = 0
    for title, section in theirs.items():
        mine = ours.get(title, {"cases": {}, "exit": "(missing)", "crashed": False})
        if section["crashed"] and not mine["crashed"]:
            differ += 1
            explained += 1
            out.append(f"{name} [{title}]\n  R2025b: crashed ({section['exit']})\n  JGraph: answered\n"
                       "  recorded: R2025b's runtime ends the process here; JGraph's refuses or answers NULL (ADR 0266)")
            continue
        for label, value in section["cases"].items():
            got = mine["cases"].get(label)
            if got == value:
                agree += 1
                continue
            differ += 1
            reason = known_case(label)
            if reason:
                explained += 1
            else:
                unexplained += 1
            out.append(f"{name} [{title}] {label}\n  R2025b: {k10.show(value)}\n  JGraph: {k10.show(got)}\n  "
                       + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
        for label in mine["cases"]:
            if label not in section["cases"]:
                reason = known_case(label)
                if not reason:
                    unexplained += 1
                out.append(f"{name} [{title}] {label}\n  R2025b: (missing)\n  JGraph: {k10.show(mine['cases'][label])}\n  "
                           + (f"recorded: {reason}" if reason else "UNEXPLAINED"))
    return f"{name}: {agree} of {agree + differ} cases agree; {explained} recorded divergences, {unexplained} unexplained", unexplained


def pe(path):
    """Each DLL's facts, the build folder's file name as the key."""
    facts, current = {}, None
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("== "):
            current = line[3:]
            facts[current] = []
        elif current is not None and line.strip():
            facts[current].append(line.strip())
    return facts


def compare_pe(out):
    theirs, ours = pe(R2025B / "k11_pe.out.txt"), pe(JGRAPH / "k11_pe.out.txt")
    agree = differ = explained = unexplained = 0
    for dll, lines in theirs.items():
        mine = ours.get(dll)
        if mine is None:
            # A build that failed in one engine has no DLL there (the clash case in R2025b).
            continue
        for fact in sorted(set(lines) | set(mine)):
            if (fact in lines) == (fact in mine):
                agree += 1
                continue
            differ += 1
            reason = known(KNOWN_PE, fact)
            if reason:
                explained += 1
            else:
                unexplained += 1
                out.append(f"pe {dll}\n  {'R2025b' if fact in lines else 'JGraph'} only: {fact}\n  UNEXPLAINED")
    for dll in ours:
        if dll not in theirs and not re.match(r"^clash\\", dll):
            unexplained += 1
            out.append(f"pe {dll}\n  JGraph only\n  UNEXPLAINED")
    return f"pe: {agree} of {agree + differ} facts agree; {explained} recorded divergences, {unexplained} unexplained", unexplained


def main():
    out, summary = [], []
    total = 0
    for name, table in [("k11_build", KNOWN_BUILD), ("k11_names", KNOWN_NAMES)]:
        theirs_path, ours_path = R2025B / f"{name}.out.txt", JGRAPH / f"{name}.out.txt"
        if theirs_path.exists() and ours_path.exists():
            line, bad = compare_keyed(name, theirs_path, ours_path, table, out)
            summary.append(line)
            total += bad
        else:
            summary.append(f"{name}: not run")

    client = (R2025B / "k11_client.out.txt", JGRAPH / "k11_client.out.txt")
    if all(p.exists() for p in client):
        line, bad = compare_sections("k11_client", *client, lambda label: known(KNOWN_CLIENT, label), out)
        summary.append(line)
        total += bad
        # What PrintStackTrace and the default error handler wrote to standard error, which the
        # cases leave out: each runtime's cache is named for its release or version, and numbers
        # an archive's folder by how many archives of that name it already holds.
        streams = [[re.sub(r"\\cache\\[^\\]+\\(\w+?)\d+\\", r"\\cache\\<V>\\\1<n>\\", l)
                    for l in p.read_text(encoding="utf-8").splitlines() if "STDERR\t" in l] for p in client]
        if streams[0] == streams[1]:
            summary.append(f"k11_client standard error: {len(streams[0])} lines, the same")
        else:
            total += 1
            summary.append("k11_client standard error: differs")
            out.append("k11_client standard error\n  R2025b: " + " | ".join(streams[0]) + "\n  JGraph: " + " | ".join(streams[1]) + "\n  UNEXPLAINED")

    for model in ("separate", "interleaved"):
        theirs_path, ours_path = R2025B / f"k10_feval_{model}.out.txt", JGRAPH / f"k11_feval_{model}.out.txt"
        if theirs_path.exists() and ours_path.exists():
            line, bad = compare_sections(f"k11_feval_{model}", theirs_path, ours_path,
                                         lambda label: k10.known("k10_feval", label), out)
            summary.append(line + " (stage K10's client on JGraph's generated library)")
            total += bad

    if (R2025B / "k11_pe.out.txt").exists() and (JGRAPH / "k11_pe.out.txt").exists():
        line, bad = compare_pe(out)
        summary.append(line)
        total += bad

    text = "\n".join(summary) + "\n\n" + "\n\n".join(out) + "\n"
    (JGRAPH / "k11_differences.txt").write_text(text, encoding="utf-8")
    print("\n".join(summary))
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main())
