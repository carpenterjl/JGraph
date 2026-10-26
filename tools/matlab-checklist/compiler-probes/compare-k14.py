"""Compares JGraph's K14 runs with R2025b's recordings (compiler and SDK plan, stage K14).

    python compare-k14.py <jgraph build out> <jgraph server out> [<jgraph routes out>]

The build recording is key<TAB>value lines with a display or file text indented under its key; the
server recording is "== mode" sections of label<TAB>status<TAB>headers<TAB>body lines. Every key or
label of R2025b's is one of: agrees, a recorded divergence (ADR 0270, named below with its reason),
or unexplained. The exit code is the number unexplained.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REC = HERE / "recordings"

# --- k14_build: the keys whose JGraph answer differs by design ---------------------------------
KNOWN_BUILD = {
    # The readme and the generated text are in JGraph's words.
    r"^.*\.readme$": "the readme is in JGraph's words (ADR 0270)",
    r"^.*\.file\.readme\.txt$": "the readme is in JGraph's words (ADR 0270)",
    r"^.*\.file\.buildresult\.json$": "JGraph's build result names its own runtime (ADR 0260)",
    r"^.*\.file\.requiredMCRProducts\.txt$": "JGraph names its own runtime product (ADR 0260)",
    r"^.*\.file\.mccExcludedFiles\.log$": "JGraph's analysis excludes by its own rules (ADR 0257)",
    r"^.*\.file\.unresolvedSymbols\.txt$": "JGraph's analysis (ADR 0257)",
    r"^main\.printed$": "mcc -v prints JGraph's lines (ADR 0263)",
    r"^mcc_verbose$": "mcc -v prints JGraph's lines (ADR 0263)",
    r"^.*\.seconds$": "a timing",
    r"^.*\.files$": "the files' sizes are JGraph's (the archive and the logs; the names are checked by the tests)",
    r"^mcc_default\.requiredMCRProducts$": "JGraph names its own runtime product (ADR 0260)",
    r"^mcc_default\.mccExcludedFiles$": "JGraph's analysis (ADR 0257)",
    r"^nooutdir\.files$": "the files' sizes are JGraph's",
    r"^mcc_u_alone(\..*)?$": "-U alone names the archive after the first file where R2025b writes .ctf (ADR 0270)",
    r"^mcc_tlib(\..*)?$": "-T link:lib builds the archive where R2025b fails on no source files (ADR 0270)",
    r"^mcc_texe(\..*)?$": "-T link:exe builds the archive alone where R2025b builds a program too (ADR 0270)",
    r"^mcc_runtime\.warn$": "JGraph refuses -R before warning about -nojvm (ADR 0270)",
    r"^mcc_disc_notjson$": "a JSON fault's text is JGraph's parser's (ADR 0270)",
    r"^mcc_routes_badregex\.warn$": "a regular expression's fault is .NET's (ADR 0270)",
    r"^r_badregex\.warn$": "a regular expression's fault is .NET's (ADR 0270)",
    r"^api_ok$": "R2025b prints the routes warning with its stack on standard output during the build; JGraph's goes to standard error (open item 170)",
    r"^mcc_routes_notjson\.warn$": "a JSON fault's text is .NET's (ADR 0270)",
    r"^opts\.sig\.k14notjson$": "a JSON fault's text is JGraph's parser's (ADR 0270)",
    r"^opts\.sig\.k14array$": "a JSON fault's text is JGraph's parser's (ADR 0270)",
    r"^.*\.printed$": "what a build prints is JGraph's (ADR 0263)",
    r"^build\.nargout$": "R2025b's too-many-outputs message ends with a line break here; JGraph's does not (ADR 0270)",
    r"^.*\.(results|Options|options)$|^opts\.default$": "an object's display is JGraph's own layout, not R2025b's aligned one (open item 194)",
    r"^obfuscate(\..*)?$": "ObfuscateArchive builds nothing: JGraph builds no encrypted archives (decision K3b, ADR 0259)",
    r"^mcc_disc_emptyobj$|^opts\.sig\.k14emptyobj$": "a warning's '> In' lines and link markers are R2025b's (open item 170)",
}

# --- k14_server: the labels whose JGraph answer differs by design ------------------------------
KNOWN_SERVER = {
    "error.plain": "an error's frames: JGraph has no mpsExecuteInMatlab frame (ADR 0270)",
    "error.id": "no mpsExecuteInMatlab frame (ADR 0270)",
    "error.nested": "no mpsExecuteInMatlab frame (ADR 0270)",
    "error.cause": "no mpsExecuteInMatlab frame (ADR 0270)",
    "error.builtin": "no mpsExecuteInMatlab frame; JGraph's message for a dimension mismatch (ADR 0270)",
    "error.undefined": "no mpsExecuteInMatlab frame (ADR 0270)",
    "error.multiline": "no mpsExecuteInMatlab frame (ADR 0270)",
    "error.quote": "no mpsExecuteInMatlab frame; error()'s format with a stray backslash (ADR 0270)",
    "error.id.large": "no mpsExecuteInMatlab frame (ADR 0270)",
    "error.id.nargout0": "no mpsExecuteInMatlab frame (ADR 0270)",
    "error.too.many.in": "no mpsExecuteInMatlab frame (ADR 0270)",
    "error.too.few.in": "no mpsExecuteInMatlab frame (ADR 0270)",
    "error.make.unknown": "no mpsExecuteInMatlab frame (ADR 0270)",
    "out.nosuchkind": "no mpsExecuteInMatlab frame (ADR 0270)",
    "out.large.nosuchkind": "no mpsExecuteInMatlab frame (ADR 0270)",
    "rhs.number": "no mpsExecuteInMatlab frame (ADR 0270)",
    "rhs.object": "no mpsExecuteInMatlab frame (ADR 0270)",
    "nargout.huge": "too many outputs asked: JGraph's refusal, no mpsExecuteInMatlab frame (ADR 0270)",
    "nargout.two.add": "too many outputs: no mpsExecuteInMatlab frame (ADR 0270)",
    "noout.nargout1": "too many outputs: no mpsExecuteInMatlab frame (ADR 0270)",
    "multi.4": "too many outputs: no mpsExecuteInMatlab frame (ADR 0270)",
    "echo.0.1": "an unassigned varargout: JGraph's words, no mpsExecuteInMatlab frame (ADR 0270)",
    "echo.3.4": "an unassigned varargout: JGraph's words, no mpsExecuteInMatlab frame (ADR 0270)",
    "post.origin.error": "no mpsExecuteInMatlab frame (ADR 0270)",
    "out.int64": "int64 past 2^53 is a double in JGraph (ADR 0174)",
    "out.large.int64": "int64 past 2^53 is a double in JGraph (ADR 0174)",
    "out.uint64": "uint64 past 2^53 is a double in JGraph (ADR 0174)",
    "out.large.uint64": "uint64 past 2^53 is a double in JGraph (ADR 0174)",
    "out.sparselogical": "a sparse array is double in JGraph (ADR 0266)",
    "out.large.sparselogical": "a sparse array is double in JGraph (ADR 0266)",
    "get.health": "the health check answers 200 while the server listens; its body comes with stage K15",
    "get.origin.health": "the health check answers 200 while the server listens; its body comes with stage K15",
    "in.big.number": "JGraph prints the number with its own digits (k14describe's %.17g)",
    "in.tiny.number": "JGraph prints the number with its own digits (k14describe's %.17g)",
    "path.space": "the client itself refuses a space in a URL",
    "path.query.async": "the in-MATLAB test server has no asynchronous requests; JGraph starts one (ADR 0271)",
    "path.query.async.client": "the in-MATLAB test server has no asynchronous requests; JGraph starts one (ADR 0271)",
    "get.origin.discovery": "the archive's uuid: the in-MATLAB test server wrote none, muserve and JGraph write <name>_<uuid> (ADR 0271)",
    "get.discovery": "the archive's uuid: the in-MATLAB test server wrote none, muserve and JGraph write <name>_<uuid> (ADR 0271)",
    "quiet": "the in-MATLAB server inherits the probe's beep('off'); JGraph's worker starts with it on, as muserve's does (ADR 0271)",
}


def read_build(path):
    keys = {}
    current = None
    for line in Path(path).read_text(encoding="utf-8").split("\n"):
        if not line.strip():
            continue
        # A display is indented under its key; a warning R2025b printed while a key ran is not, but has no tab.
        if line.startswith("  ") or ("\t" not in line and current is not None):
            if current is not None:
                keys[current] = keys[current] + "\n" + line
            continue
        key, _, value = line.partition("\t")
        current = key
        keys[key] = value
    return keys


def read_server(path):
    cases = {}
    mode = ""
    for line in Path(path).read_text(encoding="utf-8").split("\n"):
        if line.startswith("== "):
            mode = line[3:]
            continue
        stripped = line.strip()
        if stripped.startswith("== "):
            mode = stripped[3:]
            continue
        if not stripped or "\t" not in stripped:
            continue
        label, _, rest = stripped.partition("\t")
        if label in ("EXIT", "SECONDS"):
            continue
        cases[mode + "/" + label] = rest
    return cases


def known_build(key):
    for pattern, reason in KNOWN_BUILD.items():
        if re.match(pattern, key):
            return reason
    return None


def report(title, expected, actual, known):
    agree = []
    divergent = []
    unexplained = []
    for key, value in expected.items():
        mine = actual.get(key)
        if mine == value:
            agree.append(key)
            continue
        reason = known(key)
        if reason:
            divergent.append((key, reason))
        else:
            unexplained.append((key, value, mine))
    print(f"{title}: {len(agree)} of {len(expected)} agree, {len(divergent)} recorded divergences, {len(unexplained)} unexplained")
    for key, value, mine in unexplained:
        print(f"  ? {key}")
        print(f"      R2025b: {value[:300]!r}")
        print(f"      JGraph: {None if mine is None else mine[:300]!r}")
    return len(unexplained)


def main(argv):
    if len(argv) not in (3, 4):
        print(__doc__)
        return 2
    bad = report("k14_build", read_build(REC / "k14_build.out.txt"), read_build(argv[1]), known_build)
    server_known = lambda key: KNOWN_SERVER.get(key.split("/", 1)[1])
    bad += report("k14_server", read_server(REC / "k14_server.out.txt"), read_server(argv[2]), server_known)
    if len(argv) == 4:
        bad += report("k14_routes", read_build(REC / "k14_routes.out.txt"), read_build(argv[3]), known_build)
    return bad


if __name__ == "__main__":
    sys.exit(main(sys.argv))
