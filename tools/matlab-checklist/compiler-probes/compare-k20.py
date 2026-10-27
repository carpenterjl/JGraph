"""Compares JGraph's K20 run with R2025b's recording (compiler and SDK plan, stage K20).

    python compare-k20.py <jgraph k20_projects out>

The recording is key<TAB>value lines with what a call printed indented under <key>.printed and what a
step wrote under tree.<label>. Every key of R2025b's is one of: agrees, a recorded divergence (ADR 0275,
named below with its reason), or unexplained. The exit code is the number unexplained. Uuids and the
definition files' hashed names (which carry uuids) are canonicalized before comparing; the hash rule
itself is checked by ProjectTests against the recording's fixed names (id.* lines).
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REC = HERE / "recordings"

EXIST = "exist answers 0 for R2025b's package functions (p-files) and 5 for JGraph's built-ins (ADR 0271)"
WHICH = "which names R2025b's file; JGraph's built-in has none"
H1 = "the first help line is R2025b's H1 comment; JGraph's is the catalog's signature (open item 194)"
NARGIN = "nargin of R2025b's p-coded project functions; JGraph's built-ins take their inputs by name"
INTERNAL = "R2025b's internal helpers (compiler.internal.*, the deploy script builder, the enumerations' classes) are not JGraph's to answer"
CATALOG = "a message catalog R2025b reads through message(); JGraph has no catalog and its windows carry the words (ADR 0275)"
DISPLAY = "a display in R2025b's layout where JGraph's differs in a detail (an echo's header, a method listing, an enumeration's class)"
TREE = "the definition files are written with JGraph's uuids and the hashed names they give, so a step's file list differs in its names and count while the nodes are the same (ADR 0275)"
OUT_OF_SCOPE = "a task for a target outside JGraph's scope (Java, COM, web apps, toolboxes: decisions KD1 and KD2) is refused where R2025b makes it (ADR 0275)"
SCRIPT = "the script's lines where JGraph writes the options' meaning rather than R2025b's generator's slip (standaloneWindowsApplication for a suppressed prompt, RuntimeDelivery for 'installer', the installer's additional files) or where the task is one JGraph does not build (ADR 0275)"
NORMALIZED = "R2025b refuses an output folder it calls not normalized; JGraph takes a relative folder without its refused characters (ADR 0275)"
FOLDER_CLOSE = "closing a project takes the session back to the folder it was in; R2025b's folder after close is its own (ADR 0275)"
METHODS = "R2025b's handle methods listing; JGraph lists the class's own methods and the handle's (open item 217)"
DEPENDENCIES = "the dependency graph is a digraph in R2025b; JGraph answers a struct of Nodes and Edges (open item 217)"
LABEL_ORDER = "the order of a file's labels is R2025b's storage order; JGraph keeps the order they were attached in"
DEFAULT_FOLDER = "a project created without a folder lands in R2025b's default folder under the user's documents; JGraph's is its own (ADR 0275)"
SECOND = "a second project's creation and the nested one: R2025b allows a project inside another's root; JGraph does too, with its own stubs"

KNOWN = [
    (r"^exist\.", EXIST),
    (r"^which\.", WHICH),
    (r"^h1\.", H1),
    (r"^nargin\.", NARGIN),
    (r"^nargout\.(compiler\.internal|deployment\.compiler|matlab\.project\.(Project|ProjectFile|Label|LabelDefinition|Category|PathFolder|Shortcut|DefinitionFiles|extractProject|convertDefinitionFiles|show)|simulinkproject)", INTERNAL),
    (r"^(exist|nargin|nargout|which|h1)\.(compiler\.internal|deployment\.compiler|simulinkproject|matlab\.project\.(extractProject|convertDefinitionFiles|show))", INTERNAL),
    (r"^enum\.", INTERNAL),
    (r"^tree\.", TREE),
    (r"^id\.", TREE),
    (r"^text\.", CATALOG),
    (r"^(create|labels\.category|path\.addPath\.lib|entry\.addShortcut|files\.addFile\.hello|task\.\w+|set\.\w+)\.disp(\.printed)?$", DISPLAY),
    (r"^task\.\w+\.methods$", METHODS),
    (r"^create\.methods$", METHODS),
    (r"^files\.Dependencies\.", DEPENDENCIES),
    (r"^(create|reopen)\.prop\.Dependencies$", DEPENDENCIES),
    (r"^(task\.\w+\.prop|set\.\w+|reopen\.task\.\w+)\.(RuntimeInstallType|Interface)(\.\w+)*$", "R2025b's MCRInstallType and MatlabAPIOptionType derive from a numeric class, so the probe's mat2str prints the member; JGraph's enumeration values are objects whose string() answers the member (ADR 0275)"),
    (r"^task\.DotNET\.label\.StrongNameKeyFileLabel$", "R2025b's StrongNameKeyFileLabel names the Sample File label (a slip in its class); JGraph names the Strong Key File label the task's category carries (ADR 0275)"),
    (r"^reopen\.task\.\w+\.(BuildOutputFolder|PackageOutputFolder)$", NORMALIZED),
    (r"^task\.(Java|COM|WebApp|Toolbox)\.", OUT_OF_SCOPE),
    (r"^(task|reopen)\.Categories$", "the categories count the tasks R2025b made for the targets JGraph refuses (Java, COM, web app, toolbox) as well: " + OUT_OF_SCOPE),
    (r"^set\.(WebApp)\.", OUT_OF_SCOPE),
    (r"^script\.(Java|COM|WebApp|Toolbox)\.", OUT_OF_SCOPE),
    (r"^script\.", SCRIPT),
    (r"^manage\.", "R2025b's compiler.internal.project.rename, remove and openEditor are opaque helpers that changed nothing the probe could see; JGraph's Compiler Task Manager renames and removes tasks itself (ADR 0275)"),
    (r"^set\.\w+\.(BuildOutputFolder|PackageOutputFolder)(\.back)?$", NORMALIZED),
    (r"^close\.pwd\.root$", FOLDER_CLOSE),
    (r"^none\.createProject\.number$", DEFAULT_FOLDER),
    (r"^second\.create\.(nv|folderonly|existingfolder)", SECOND),
    (r"^(labels|set)\.\w+\.labels(\.after)?$", LABEL_ORDER),
    (r"^reopen\.hello\.labels$", LABEL_ORDER),
    (r"^set\.Standalone\.vals\.labels$|^set\.Standalone\.icon\.labels$|^set\.PSA\.hello\.labels$|^manage\.hello\.labels$", LABEL_ORDER),
    (r"^reopen\.(reload|runChecks)", "reload and runChecks: JGraph's reload reopens the project (the startup files run again), runChecks is not in JGraph (open item 217)"),
    (r"^task\.get\.(number|cell)$", "a non-text task name: R2025b reads it as an empty name; JGraph's refusal is the same identifier with the same words"),
    (r"^set\.Standalone\.SupportPackages$", "support packages: JGraph has none, and refuses a struct with the right fields as well as the wrong ones (open item 217)"),
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


def canonical(key, value):
    out = []
    for l in value.replace("\x08", "").split("\n"):
        if re.search(r"^\s*\|?\s*(\[> )?In |^\s*\|In |style=\"font-weight:bold\"", l):
            continue
        l = re.sub(r"^(\s*\|?)\[Warning: (.*)\]\s*$", r"\1Warning: \2", l)
        l = UUID.sub("<UUID>", l)
        l = HASHED.sub("<ID>", l)
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
    expected = read(REC / "k20_projects.out.txt")
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
