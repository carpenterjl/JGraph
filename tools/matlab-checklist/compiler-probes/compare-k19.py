"""Compares JGraph's K19 run with R2025b's recording (compiler and SDK plan, stage K19).

    python compare-k19.py <jgraph k19_apps out>

The recording is key<TAB>value lines with what a call printed indented under <key>.printed. Every
key of R2025b's is one of: agrees, a recorded divergence (ADR 0274, named below with its reason), or
unexplained. The exit code is the number unexplained.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REC = HERE / "recordings"

NO_WINDOW = "R2025b publishes to its app gallery (a desktop that a -batch session lacks, silently) where JGraph's session without windows refuses to open the app (ADR 0274)"
LEGACY_OPENS = "the legacy app warns, then R2025b publishes to its app gallery where JGraph's session without windows refuses to open the app (ADR 0274)"
NO_PROJECT = "R2025b opens the file in its editor or project through open(), silently in -batch; JGraph refuses a project (none until stage K20) and opens the app on a MATLAB file, which a session without windows refuses (ADR 0274)"
HELP = "R2025b prints the function's help block for -?; JGraph prints the catalog's entry as help does (ADR 0274)"
OUT_OF_SCOPE = "an app for a target outside JGraph's scope (Java, COM, web apps: decisions KD1 and KD2) is refused where R2025b opens it (ADR 0274)"
INTERNAL = "R2025b's internal helpers (compiler.internal.*, message.publish, licenses, feature, the capability) are not JGraph's to answer"
CATALOG = "a message catalog R2025b reads through message(); JGraph has no catalog and its windows carry the words (ADR 0274)"
STACK = "a warning's '> In' stack lines are MATLAB's; JGraph prints the warning without them (open item 170)"
EXIST = "exist answers 5 for a built-in that is a file in R2025b (ADR 0271)"
WHICH = "which names R2025b's file; JGraph's built-in has none"
H1 = "the first help line is R2025b's H1 comment; JGraph's is the catalog's signature (open item 194)"
NARGIN = "nargin of a legacy app is -1 for its varargin in R2025b; JGraph's built-in takes the two inputs by name"
SCRIPT = "createDeploymentScript arrives with stage K21 (open item 216)"

LEGACY = r"(applicationCompiler|libraryCompiler|productionServerCompiler|deploytool|webAppCompiler)"
NEW = r"(standaloneApplicationCompiler|productionServerArchiveCompiler|excelAddinCompiler|dotNetAssemblyCompiler|pythonPackageCompiler|cppSharedLibraryCompiler|cSharedLibraryCompiler)"
OUT = r"(javaPackageCompiler|comComponentCompiler)"

KNOWN = [
    (r"^exist\.", EXIST),
    (r"^which\.", WHICH),
    (r"^h1\.", H1),
    (r"^nargin\." + LEGACY + r"$|^nargin\.hadoopCompiler$", NARGIN),
    (r"^(nargin|nargout)\.(createDeploymentScript|compiler\.internal\.)", "createDeploymentScript (stage K21) and R2025b's internal helpers are not built (open item 216)"),
    (r"^" + NEW + r"\.noargs(\.printed|\.warn)?$", NO_WINDOW),
    (r"^(standaloneApplicationCompiler|productionServerArchiveCompiler)\.(stub|text|mfile|txt)$", NO_PROJECT),
    (r"^" + OUT + r"\.noargs$", OUT_OF_SCOPE),
    (r"^webAppCompiler\.noargs(\.printed|\.warn)?$", OUT_OF_SCOPE),
    (r"^" + LEGACY + r"\.noargs(\.printed)?$", LEGACY_OPENS),
    (r"^" + LEGACY + r"\.(help|help\.slash|help\.string)(\.printed)?$", HELP),
    (r"^" + LEGACY + r"\..*\.printed$", STACK),
    (r"^(publish|uiwarning|launchui|license|webui|capability)(\.|$)", INTERNAL),
    (r"^(nargin|nargout)\.excelAddInCompiler$", "R2025b's nargin finds excelAddinCompiler.m case-insensitively on Windows while calling the name is undefined; JGraph knows no such name"),
    (r"^prjstruct\.", INTERNAL),
    (r"^open\.", "R2025b's open() of a project or MATLAB file opens nothing in -batch; JGraph's open takes a VideoWriter or a context menu (ADR 0274)"),
    (r"^script\.", SCRIPT),
    (r"^text\.", CATALOG),
    (r"^excelAddInCompiler\.", "a name R2025b does not have either; its refusal is JGraph's own wording"),
]

SKIPPED = []

KEY = re.compile(r"^[\w.:|-]+$")


def read(path):
    keys = {}
    current = None
    for line in Path(path).read_text(encoding="utf-8").split("\n"):
        if not line.strip():
            continue
        if "\t" not in line and not KEY.match(line):
            if current is not None:
                keys[current] = keys[current] + "\n" + line
            continue
        key, _, value = line.partition("\t")
        if key.startswith("STDERR") or key.startswith("[") or key.startswith("  "):
            if current is not None:
                keys[current] = keys[current] + "\n" + line
            continue
        current = key
        keys[key] = value
    return keys


def canonical(key, value):
    out = []
    for l in value.replace("\x08", "").split("\n"):
        if re.search(r"^\s*\|?\s*(\[> )?In |^\s*\|In |style=\"font-weight:bold\"", l):
            continue
        l = re.sub(r"^(\s*\|?)\[Warning: (.*)\]\s*$", r"\1Warning: \2", l)
        out.append(l)
    return "\n".join(out)


def known(key):
    for pattern, reason in KNOWN:
        if re.match(pattern, key):
            return reason
    return None


def main(argv):
    if len(argv) != 2:
        print(__doc__)
        return 2
    expected = read(REC / "k19_apps.out.txt")
    actual = read(argv[1])
    agree, divergent, unexplained = [], [], []
    for key, value in expected.items():
        if any(re.match(p, key) for p in SKIPPED):
            continue
        mine = actual.get(key)
        if mine is not None and canonical(key, mine) == canonical(key, value):
            agree.append(key)
            continue
        reason = known(key)
        if reason:
            divergent.append((key, reason, value, mine))
        else:
            unexplained.append((key, value, mine))
    extra = [k for k in actual if k not in expected and not any(re.match(p, k) for p in SKIPPED)]
    print(f"{len(agree)} of {len(expected)} keys agree, {len(divergent)} recorded divergences, {len(unexplained)} unexplained, {len(extra)} keys JGraph alone has")
    print()
    if unexplained:
        print("UNEXPLAINED")
        for key, value, mine in unexplained:
            print(f"  {key}")
            print(f"    R2025b: {value}")
            print(f"    JGraph: {mine}")
        print()
    if extra:
        print("JGRAPH ONLY")
        for key in extra:
            print(f"  {key}\t{actual[key]}")
        print()
    print("DIVERGENCES")
    for key, reason, value, mine in divergent:
        print(f"  {key}: {reason}")
        print(f"    R2025b: {value}")
        print(f"    JGraph: {mine}")
    return len(unexplained)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
