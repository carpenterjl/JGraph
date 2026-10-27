"""Compares JGraph's K18 run with R2025b's recording (compiler and SDK plan, stage K18).

    python compare-k18.py <jgraph k18_package out>

The recording is key<TAB>value lines with a display, a file's text or what a call printed indented
under its key. Every key of R2025b's is one of: agrees, a recorded divergence (ADR 0273, named below
with its reason), or unexplained. The exit code is the number unexplained.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REC = HERE / "recordings"

RUNTIME_NAME = "JGraph's installer names the JGraph Runtime where R2025b's names the MATLAB Runtime (ADR 0273)"
WEB_NEEDS_MSI = "a 'web' installer needs the runtime's MSI on this machine for its hash, so a build without one is refused where R2025b builds (ADR 0273)"
FILE_SIZES = "the installer is a WiX bundle of JGraph's own, so its size is its own; the names are checked by the tests (ADR 0273)"

# The keys whose JGraph answer differs by design, by pattern (the first matching pattern's reason).
KNOWN = [
    (r"^(build|build\.py)\.(Files|seconds)$", "a build's own files and timing (ADR 0262, 0269)"),
    (r"^build\.requiredMCRProducts$", "JGraph names its own runtime product (ADR 0260)"),
    (r"^.*\.seconds$", "a timing"),
    (r"^opts\.(default|results|results\.win|results\.py|results\.nv|results\.properties)$|^derived\.display$|^set\.AdditionalFiles\.\d+$",
     "an object's or a cell's display is JGraph's own layout, not R2025b's (an empty cell prints {} where R2025b prints nothing; open item 194)"),
    (r"^(installer\.files|mcrinstaller\.(outputs|printed))$",
     "the JGraph Runtime installer is on this machine (a checkout's build of the MSI), so mcrinstaller names it and an offline installer is built, where R2025b had no MATLAB Runtime installer (ADR 0273)"),
    (r"^exist\.mcrinstaller$", "exist answers 5 for a built-in that is a file in R2025b (ADR 0271)"),
    (r"^derived\.saveload$", "an options object saved to a MAT-file and loaded back keeps JGraph's class, which answers no saveobj (open item 215)"),
    (r"^(web|quiet|named|zip|optional\.none|options|results\.win|results\.py|files\.(exe|shortcut|string|folder|folder\.shortcut|onetxt|results\.txt)|results\.(noshortcut|exe\.additional)|outdir\.(relative|default|default\.named)|verbose\.logical)\.files$",
     FILE_SIZES),
    (r"^(options\.(extra|wrongclass|other)|nargs|nargs\.odd|unknown|shortcut\.(notpackaged|byname))\.files$", FILE_SIZES),
    (r"^custom\..*\.files$", FILE_SIZES),
    (r"^(installer)(\.printed)?$", RUNTIME_NAME),
    (r"^none(\.zip)?(\.printed)?$|^none(\.zip)?\.files$",
     "an application-only installer needs nothing from the runtime's installer, where R2025b needs its install agent; JGraph builds it (ADR 0273)"),
    (r"^custom\.(installer|zip\.installer)$", RUNTIME_NAME),
    (r"^(web|quiet|named|zip|optional\.none|options|results\.win|results\.py|files\.(exe|shortcut|string|folder|folder\.shortcut|onetxt|results\.txt)|results\.(noshortcut|exe\.additional)|outdir\.(relative|default|default\.named)|verbose\.logical|icon\.deleted|addremove\.deleted|shortcut\.(notpackaged|byname)|installer|none|none\.zip)\.warn$",
     "R2025b's fileattrib warns about user characters on Windows; JGraph checks the folder without the warning (ADR 0273)"),
    (r"^custom\.(files|files\.two|files\.string|badtxt|emptytxt|notxt)\.warn$|^files\.(noname|badtxt|emptytxt|notxt)\.warn$|^shortcut\.(notpackaged|byname)\.warn$",
     "the requiredMCRProducts.txt deprecation warning is raised as R2025b raises it; its text agrees, its '> In' lines are MATLAB's (open item 170)"),
    (r"^(files\.(exe|shortcut|string|folder|folder\.shortcut|onetxt)|results\.noshortcut)\.printed$",
     "a warning's '> In' stack lines are MATLAB's; JGraph prints the warning without them (open item 170)"),
    (r"^results\.exe\.additional$", "an additional file of a packaged file's name is refused (noRepeats) where R2025b packages both (ADR 0273)"),
    (r"^mcrinstaller\.printed$", RUNTIME_NAME),
    (r"^(url|installerkey|expectedMcrVersion|notes|imgformats|optionalproducts|deliveries|packagetypes|scrub|ext)$",
     "R2025b's internal helpers (compiler.internal.*) are not JGraph's to answer"),
    (r"^exist\.compiler\.", "exist answers 2 for a package function that is a file in R2025b and a built-in here (ADR 0271)"),
    (r"^nargin\.compiler\.runtime\.installer$|^nargout\.compiler\.runtime\.installer$|^exist\.compiler\.runtime\.installer$",
     "compiler.runtime.installer, an undocumented p-file, is not built (open item 215)"),
    (r"^runtime\.version\.text$", "a display is JGraph's own layout (open item 194)"),
]

# Keys JGraph's run cannot have at all (R2025b-only artifacts of the probe), which count neither way.
SKIPPED = [
    r"^custom\.k18 rt",
]


KEY = re.compile(r"^[\w.:-]+$")


def read(path):
    keys = {}
    current = None
    for line in Path(path).read_text(encoding="utf-8").split("\n"):
        if not line.strip():
            continue
        # A key line is key<TAB>value, or a display's key alone; everything else (a display's indented
        # lines, a warning R2025b printed while a key ran and its continuation, standard error) belongs
        # to the key before it.
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
    # MATLAB marks a warning's text as a link ([\b...]\b) and follows it with '> In' stack lines that name
    # the probe's frames and R2025b's files; JGraph prints the warning's text alone.
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
    expected = read(REC / "k18_package.out.txt")
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
            divergent.append((key, reason))
        else:
            unexplained.append((key, value, mine))
    print(f"k18_package: {len(agree)} of {len(agree) + len(divergent) + len(unexplained)} agree, {len(divergent)} recorded divergences, {len(unexplained)} unexplained")
    for key, value, mine in unexplained:
        print(f"  ? {key}")
        print(f"      R2025b: {value[:400]!r}")
        print(f"      JGraph: {None if mine is None else mine[:400]!r}")
    print()
    print("recorded divergences:")
    for key, reason in divergent:
        print(f"  {key}: {reason}")
    return len(unexplained)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
