"""Compares JGraph's K21 run with R2025b's recording (compiler and SDK plan, stage K21).

    python compare-k21.py <jgraph k21_legacy out>

The recording is key<TAB>value lines with what a call printed indented under <key>.printed and a
script's text under <key>.text. Every key of R2025b's is one of: agrees, a recorded divergence
(ADR 0276, named below with its reason), or unexplained. The exit code is the number unexplained.
Paths are tokenized by the recorders; uuids and the definition files' hashed names are canonicalized
before comparing.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REC = HERE / "recordings"

EXIST = "exist answers 0, 2 or 6 for R2025b's files and 5 for JGraph's built-ins (ADR 0271)"
NARGIN = "nargin and nargout of R2025b's p-coded functions; JGraph's built-ins take their inputs by name"
INTERNAL = "R2025b's internal helpers are not JGraph's to answer"
CATALOG = "a message catalog R2025b reads through message(); JGraph has no catalog and its refusals carry the words (ADR 0276)"
BUILD_OUTPUT = "what a build prints and leaves behind is JGraph's compiler's (its mcc line, its log files, its generated files), measured by stages K6-K14; the Build Complete and Packaging Complete lines are the same (ADR 0276)"
OUT_OF_SCOPE = "a legacy project for a target outside JGraph's scope (Java, COM, web apps, the Excel client for Production Server: decisions KD1 and KD2) is read and its script written, but not built, packaged or upgraded into a task JGraph opens (ADR 0276)"
EXCEL = "an Excel add-in is built by stage K17, not yet; JGraph's build of one refuses where R2025b builds (ADR 0276)"
NO_RUNTIME = "R2025b's -package turns a no-runtime project into one that downloads the runtime under the packaged runtime's name and then wants the runtime installer; JGraph packages the runtime delivery the project asked for (none), and carries the runtime from the checkout's build for 'installer' (ADR 0276, MATLAB_Behavioral_Fixes)"
PACKAGE_CRASH = "R2025b's -package of a library or package with an empty dependency file set fails in its own packager (noSuchMethodOrField on a string); JGraph packages it (ADR 0276, MATLAB_Behavioral_Fixes)"
UPGRADE_TREE = "the upgraded project's definition files are JGraph's: their count and the uuids differ while the nodes are the same (ADR 0275)"
UPGRADE_VALUES = "a value the upgrade carries where R2025b's drops or inverts it: SuppressWindowsCmdPrompt kept as the legacy flag means (R2025b stores its inverse), the example text kept, a C++ library's version kept, the installer's icon for Apps & Features (ADR 0276, MATLAB_Behavioral_Fixes)"
UPGRADE_DISPLAY = "the upgraded task's properties in JGraph's display (a label struct's order, an empty list's shape)"
STUB = "a legacy file without the mcc options element: R2025b's reader trips on the missing field; JGraph refuses it the same way for the upgrade and reads it as not a project otherwise"
ICON = "R2025b takes the executable's icon from the first of param.icons and passes param.icon by; JGraph the same, and a .png icon is written as R2025b writes it (ADR 0276)"

MATLAB_ROOT = "${MATLAB_ROOT} is R2025b's root in one engine and JGraph's own in the other, so the sorted file list puts it elsewhere"
ROOT_SPELLING = "the probe's work folder is spelled with forward slashes under jgraph -batch (JGS escapes backslashes), so the project's root is not made relative in JGraph's transcript; the names agree"
K20_SCRIPT = "the upgraded task's script is stage K20's DeploymentScript, which writes the installer's additional files where R2025b's generator drops them (ADR 0275)"
OUTPUTS = "the files under the task's build and package folders are what each compiler left there (JGraph's installer packaging writes no for_redistribution_files_only folder, its C library build no .exp or .def; stages K6-K18), and their labels with them"
KEY_FILE = "a strong name key file the legacy project names but that does not exist: R2025b refuses the whole upgrade (FILE_NOT_IN_PROJECT) where it warns for every other missing file; JGraph warns for this one too (ADR 0276)"

KNOWN = [
    (r"^exist\.", EXIST),
    (r"^script\.k21abs\.text$", MATLAB_ROOT),
    (r"^script\.k21excelnoclass$", "an Excel add-in project without a class: R2025b's reader fails on its own string (noSuchMethodOrField); JGraph refuses it as the class definitions being invalid, as R2025b does for a .NET project (ADR 0276)"),
    (r"^script\.k21nomcr\.text$", NO_RUNTIME),
    (r"^upgrade\.\w+\.project$", ROOT_SPELLING),
    (r"^upgrade\.(k21std|k21c|k21cC|k21cpp|k21cpplegacy|k21net|k21tsa|k21net5|k21netpkg|k21python|k21twoclass|k21excel|k21icons|k21plain|k21console|k21mps|k21mpsexcel|k21noexports|k21badsub|k21moved|k21missingmain|k21mainsub|k21appmain)\.task\.script$", K20_SCRIPT),
    (r"^upgrade\.\w+\.project\.files$", OUTPUTS),
    (r"^upgrade\.\w+\.project\.labels\.\w+/", OUTPUTS),
    (r"^upgrade\.k21net5$", KEY_FILE),
    (r"^upgrade\.k21noclass$", "a .NET project without a class: R2025b's upgrade fails on its own string (noSuchMethodOrField); JGraph makes the task with Class1 as createDeploymentScript's refusal names the fault (ADR 0276)"),
    (r"^upgrade\.k21appmain$", "an .mlapp main file: R2025b reads the app's name out of the file and refuses the probe's test app, which has none; JGraph takes the file as the main file (ADR 0276)"),
    (r"^open\.pwd$", "closing a project takes the session back to the folder before it; R2025b's folder after close is its own (ADR 0275)"),
    (r"^upgrade\.(k21java|k21com|k21web)\.project\.(category|labels\.)", OUT_OF_SCOPE),
    (r"^(nargin|nargout)\.", NARGIN),
    (r"^text\.", CATALOG),
    (r"^build\.(k21\w+|applicationCompiler|libraryCompiler|productionServerCompiler|legacy\.result)(\.printed|\.files|\.warn)?$", BUILD_OUTPUT),
    (r"^package\.(k21plain|k21cdep|k21mps|k21libraryCompiler|libraryCompiler)(\.printed|\.files|\.warn)?$", BUILD_OUTPUT),
    (r"^(build|package)\.(k21web|k21java|k21com|k21mpsexcel)", OUT_OF_SCOPE),
    (r"^(build|package)\.k21excel", EXCEL),
    (r"^package\.(k21nomcr|k21mcr)", NO_RUNTIME),
    (r"^package\.(k21python|c)(\.printed|\.files|\.warn)?$", PACKAGE_CRASH),
    (r"^upgrade\.\w+\.defs$", UPGRADE_TREE),
    (r"^open\.\w+\.defs$", UPGRADE_TREE),
    (r"^upgrade\.(k21java|k21com|k21web|k21mpsexcel)\.(config|task\.script)", OUT_OF_SCOPE),
    (r"^upgrade\.\w+\.config$", UPGRADE_DISPLAY),
    (r"^upgrade\.stub$", STUB),
]

SKIPPED = []

KEY = re.compile(r"^[\w.:|-]+$")
UUID = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
HASHED = re.compile(r"(?<![\w-])[A-Za-z0-9_-]{27}(?=[pd]\.xml|/)")


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


def joined_warnings(lines):
    """R2025b wraps a long warning over several printed lines; JGraph prints it on one."""
    out = []
    i = 0
    while i < len(lines):
        l = lines[i]
        if re.match(r"^\s*\|?\[Warning: ", l) and not re.search(r"\]\s*$", l):
            while i + 1 < len(lines) and not re.search(r"\]\s*$", l):
                i += 1
                l = l.rstrip() + " " + re.sub(r"^\s*\|", "", lines[i]).strip()
        out.append(l)
        i += 1
    return out


def canonical(key, value):
    out = []
    for l in joined_warnings(value.replace("\x08", "").split("\n")):
        if re.search(r"^\s*\|?\s*(\[> )?In |^\s*\|In |style=\"font-weight:bold\"", l):
            continue
        l = re.sub(r"^(\s*\|?)\[Warning: (.*)\]\s*$", r"\1Warning: \2", l)
        l = UUID.sub("<UUID>", l)
        l = HASHED.sub("<ID>", l)
        l = re.sub(r"^char \[1 \d+\] '", "char [1 N] '", l)  # a description's length counts the work folder's path
        out.append(l)
    return "\n".join(out)


def known(key):
    for pattern, reason in KNOWN:
        if re.match(pattern, key):
            return reason
    return None


def main(argv):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if len(argv) != 2:
        print(__doc__)
        return 2
    expected = read(REC / "k21_legacy.out.txt")
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
