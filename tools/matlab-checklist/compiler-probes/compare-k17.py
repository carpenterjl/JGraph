"""Compares JGraph's K17 recordings with R2025b's (compiler and SDK plan, stage K17).

    python compare-k17.py <folder holding JGraph's k17_*.out.txt> [--only excel,com,cells,mps,payload,pe,strings,registry]

Each recording is key<TAB>value lines with a display, a file's text or what a call printed indented
under its key (record-k17.ps1 writes R2025b's into recordings/; the later stages write JGraph's with
the same names into a folder of their own). Every key of R2025b's is one of: agrees, a recorded
divergence (named below with its reason and the ADR that records it), or unexplained. The exit code
is the number unexplained. Stage K17a wrote the recordings and this comparer; the KNOWN lists fill
as K17b to K17g land. Run against recordings/ itself, every key agrees.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REC = HERE / "recordings"

FILES = ["excel", "com_preinit", "com", "cells", "mps", "payload", "pe", "strings", "registry"]

# The keys whose JGraph answer differs by design, per recording, by pattern (the first matching
# pattern's reason). Timings and the build's own byte counts never agree.
KNOWN = {
    "excel": [
        (r"^.*\.seconds$", "a timing"),
        (r"^(matlab\.version|compiler\.version|mbuild\.cpp)$", "the engine's own version; JGraph's build needs no C++ compiler (KO9)"),
    ],
    "com_preinit": [
        (r"^(create\.)?seconds$", "a timing"),
        (r"^host\.", "the client host's own versions"),
    ],
    "com": [
        (r"^(create\.)?seconds$", "a timing"),
        (r"^host\.", "the client host's own versions"),
    ],
    "cells": [
        (r"^seconds$", "a timing"),
    ],
    "mps": [
        (r"^seconds$", "a timing"),
    ],
    "payload": [
        (r"\.bytes$", "the installer's own size (its template is JGraph's, as K18's)"),
    ],
    "pe": [
        (r"\.bytes$|\.section\.", "the DLL's own layout: JGraph's template is its own (KO9)"),
    ],
    "strings": [
        (r".*", "the string heaps are MathWorks' .NET client's; JGraph's client is native (KO9), so this recording is read, not matched"),
    ],
    "registry": [],
}

# Keys JGraph's run cannot have at all (R2025b-only artifacts of the probe), which count neither way.
SKIPPED = {
    "excel": [r"^license\.java$"],
    "com": [],
    "cells": [],
    "mps": [],
    "payload": [],
    "pe": [],
    "strings": [],
    "registry": [],
}

KEY = re.compile(r"^[\w.:\-/ ]+$")


def read(path):
    keys = {}
    current = None
    for line in Path(path).read_text(encoding="utf-8").split("\n"):
        if not line.strip():
            continue
        # A key line is key<TAB>value, or a display's key alone; everything else (a display's indented
        # lines, a warning printed while a key ran, standard error) belongs to the key before it.
        if "\t" not in line and not KEY.match(line):
            if current is not None:
                keys[current] = keys[current] + "\n" + line
            continue
        key, _, value = line.partition("\t")
        if key.startswith("STDERR") or key.startswith("[") or key.startswith("  ") or key.startswith("=="):
            if current is not None:
                keys[current] = keys[current] + "\n" + line
            continue
        current = key
        keys[key] = value
    return keys


def canonical(value):
    # MATLAB marks a warning's text as a link and follows it with '> In' stack lines that name the
    # probe's frames and R2025b's files; JGraph prints the warning's text alone.
    out = []
    for l in value.replace("\x08", "").split("\n"):
        if re.search(r"^\s*\|?\s*(\[> )?In |^\s*\|In |style=\"font-weight:bold\"", l):
            continue
        l = re.sub(r"^(\s*\|?)\[Warning: (.*)\]\s*$", r"\1Warning: \2", l)
        out.append(l)
    return "\n".join(out)


def known(name, key):
    for pattern, reason in KNOWN.get(name, []):
        if re.match(pattern, key):
            return reason
    return None


def compare(name, expected_path, actual_path):
    expected = read(expected_path)
    actual = read(actual_path)
    agree, divergent, unexplained = [], [], []
    for key, value in expected.items():
        if any(re.match(p, key) for p in SKIPPED.get(name, [])):
            continue
        mine = actual.get(key)
        if mine is not None and canonical(mine) == canonical(value):
            agree.append(key)
            continue
        reason = known(name, key)
        if reason:
            divergent.append((key, reason))
        else:
            unexplained.append((key, value, mine))
    extra = [k for k in actual if k not in expected]
    return agree, divergent, unexplained, extra


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    folder = Path(argv[1])
    only = FILES
    if "--only" in argv:
        only = argv[argv.index("--only") + 1].split(",")
    total_unexplained = 0
    for name in only:
        expected_path = REC / ("k17_%s.out.txt" % name)
        actual_path = folder / ("k17_%s.out.txt" % name)
        if not expected_path.exists():
            print("== %s: no R2025b recording" % name)
            continue
        if not actual_path.exists():
            print("== %s: JGraph has no %s yet" % (name, actual_path.name))
            continue
        agree, divergent, unexplained, extra = compare(name, expected_path, actual_path)
        print("== %s: %d keys, %d agree, %d recorded divergences, %d unexplained, %d only in JGraph's"
              % (name, len(agree) + len(divergent) + len(unexplained), len(agree), len(divergent), len(unexplained), len(extra)))
        reasons = {}
        for key, reason in divergent:
            reasons.setdefault(reason, []).append(key)
        for reason, keys in reasons.items():
            print("  divergence (%d): %s" % (len(keys), reason))
            print("    " + ", ".join(keys[:12]) + (" ..." if len(keys) > 12 else ""))
        for key, value, mine in unexplained:
            print("  UNEXPLAINED %s" % key)
            print("    R2025b: %s" % value.replace("\n", "\n            "))
            print("    JGraph: %s" % ("(missing)" if mine is None else mine.replace("\n", "\n            ")))
        total_unexplained += len(unexplained)
    return total_unexplained


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    sys.exit(main(sys.argv))
