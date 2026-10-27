"""Compares JGraph's K16 server run with R2025b's muserve recording (compiler and SDK plan, stage K16).

    python compare-k16.py <jgraph server out>

The recording is "== session" sections (full, limited, edge) of "== mode" sections (the Python client's
proto, raw, hidden, size and edge modes, and the dotnet, c and java clients' lines) of
label<TAB>text lines. Every label of R2025b's is one of: agrees, a recorded divergence (ADR 0272,
named below with its reason), or unexplained. The exit code is the number unexplained.
"""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REC = HERE / "recordings"

# --- the labels whose JGraph answer differs by design ----------------------------------------------
NO_FRAME = "an error's frames: JGraph has no mpsExecuteInMatlab frame (ADR 0270)"
KNOWN = {
    # the values an engine prints with its own digits or holds in its own class (ADRs 0174, 0266)
    "raw/in.uint64.row": "uint64 past 2^53 is a double in JGraph (ADR 0174)",
    "raw/in.int64.row": "int64 past 2^53 is a double in JGraph (ADR 0174)",
    "raw/in.sparse.logical": "a sparse array is double in JGraph (ADR 0266)",
    "raw/in.sparse.single": "a sparse array is double in JGraph (ADR 0266)",
    "raw/in.double.precise": "JGraph prints the number with its own digits (k14describe's %.17g)",
    "raw/in.single.row": "JGraph prints the number with its own digits (k14describe's %.17g)",
    "proto/in.int64": "int64 past 2^53 is a double in JGraph (ADR 0174)",
    "proto/out.int64": "int64 past 2^53 is a double in JGraph (ADR 0174)",
    "raw/out.int64": "int64 past 2^53 is a double in JGraph (ADR 0174)",
    "raw/out.sparselogical": "a sparse array is double in JGraph (ADR 0266)",
    "raw/in.complex.zero.imag": "a complex number whose imaginary part is zero is real in JGraph (ADR 0272)",
    "raw/in.char.nd": "JGraph holds a char array of two dimensions (ADR 0270)",
    "raw/out.handle": "a handle reference names muserve's own session and object; JGraph's names its own (ADR 0272)",
    "raw/out.map": "a handle reference names muserve's own session and object; JGraph's names its own (ADR 0272)",
    "raw/out.table": "muserve serializes a table's internals; JGraph writes the bare object (ADR 0272)",
    "raw/out.dict": "muserve serializes a dictionary's internals; JGraph writes the bare object (ADR 0272)",
    # the .NET, C and Java clients: the same 64-bit and sparse classes
    "dotnet/in.longs": "int64 past 2^53 is a double in JGraph (ADR 0174)",
    "dotnet/out.int64": "int64 past 2^53 is a double in JGraph (ADR 0174)",
    "dotnet/typed.int64": "int64 past 2^53 is a double in JGraph (ADR 0174)",
    "c/in.int64": "int64 past 2^53 is a double in JGraph (ADR 0174)",
    "c/in.uint64": "uint64 past 2^53 is a double in JGraph (ADR 0174)",
    "c/out.int64": "int64 past 2^53 is a double in JGraph (ADR 0174)",
    "c/in.sparse.logical": "a sparse array is double in JGraph (ADR 0266)",
    "c/in.sparse.complex": "a sparse array is double and real in JGraph (ADR 0266)",
    "c/out.sparselogical": "a sparse array is double in JGraph (ADR 0266)",
    "java/in.long": "int64 past 2^53 is a double in JGraph (ADR 0174)",
    # the rest of ADR 0272's
    "raw/query.async": "muserve has no asynchronous requests; JGraph starts one (ADR 0271)",
    "raw/echo.twoenums": "muserve writes the first output's string table only and the second output's member index points into a table it never wrote; JGraph writes each output's own (ADR 0272)",
    "dotnet/state.0": "muserve's worker ended on in.sparse.badir and was replaced, so its call count restarted; JGraph's worker ran on (ADR 0272)",
    "dotnet/state.1": "as dotnet/state.0 (ADR 0272)",
    "c/state.0": "as dotnet/state.0 (ADR 0272)",
    "c/state.1": "as dotnet/state.0 (ADR 0272)",
}

# Where muserve's worker reads past what the message holds, JGraph refuses the request (ADR 0272).
GARBAGE = {
    "raw/in.count.short": "muserve reads elements past the payload's end; JGraph refuses (ADR 0272)",
    "raw/in.payload.absent": "muserve reads a payload that is not there; JGraph refuses (ADR 0272)",
    "raw/in.payload.two": "muserve reads one of two payloads; JGraph refuses (ADR 0272)",
    "raw/in.type.mismatch": "muserve reads the payload's bytes as the type's; JGraph refuses (ADR 0272)",
    "raw/in.type.mismatch.char": "muserve reads the payload's bytes as the type's; JGraph refuses (ADR 0272)",
    "raw/in.complex.imag.short": "muserve reads imaginary parts past the payload's end; JGraph refuses (ADR 0272)",
    "raw/in.sparse.badir": "muserve drops a row index past the array or its worker ends on it; JGraph refuses (ADR 0272)",
    "raw/in.struct.badname": "muserve makes the struct and fails on its use; JGraph refuses the request (ADR 0272)",
}

EDGE_REASON = "muserve's worker ends or hangs on the message; JGraph refuses it (ADR 0272)"


def read_server(path):
    cases = {}
    session = ""
    mode = ""
    for line in Path(path).read_text(encoding="utf-8").split("\n"):
        if line.startswith("== "):
            session = line[3:]
            continue
        stripped = line.strip()
        if stripped.startswith("== "):
            mode = stripped[3:]
            continue
        if not stripped or "\t" not in stripped:
            continue
        label, _, rest = stripped.partition("\t")
        if label in ("EXIT", "SECONDS", "COUNT"):
            continue
        cases[session + "/" + mode + "/" + label] = rest
    return cases


def canonical(value):
    """An error reply's length follows its frames' file paths, each server's own: the length is a token there."""
    if "error {" in value and "stack {" in value:
        return re.sub(r"content-length=\d+", "content-length=<L>", value)
    return value


def main(argv):
    if len(argv) != 2:
        print(__doc__)
        return 2
    expected = read_server(REC / "k16_server.out.txt")
    actual = read_server(argv[1])
    agree = []
    divergent = []
    unexplained = []
    for key, value in expected.items():
        mine = actual.get(key)
        if mine is not None and canonical(mine) == canonical(value):
            agree.append(key)
            continue
        session, mode, label = key.split("/", 2)
        short = mode + "/" + label
        reason = KNOWN.get(short) or GARBAGE.get(short)
        if reason is None and session == "edge":
            reason = EDGE_REASON
        if reason is None:
            for pattern, text in KNOWN_PATTERNS:
                if re.match(pattern, short):
                    reason = text
                    break
        if reason:
            divergent.append((key, reason))
        else:
            unexplained.append((key, value, mine))
    print(f"k16_server: {len(agree)} of {len(expected)} agree, {len(divergent)} recorded divergences, {len(unexplained)} unexplained")
    for key, value, mine in unexplained:
        print(f"  ? {key}")
        print(f"      R2025b: {value[:400]!r}")
        print(f"      JGraph: {None if mine is None else mine[:400]!r}")
    return len(unexplained)


# Labels by pattern (re.match, so anchor with ^.*): an error's frames differ in every client's view of them.
KNOWN_PATTERNS = [
    (r"^(proto|raw|dotnet|c|java|hidden)/err\.(plain|id|nested|cause|builtin|undefined|multiline|quote)$", NO_FRAME),
    (r"^(proto|raw|dotnet|c|java)/out\.nosuchkind$", NO_FRAME),
    (r"^(proto|raw|dotnet|c|java)/err\.too\.few\.in$", NO_FRAME),
    (r"^(proto|raw|dotnet|c|java)/rest\.err$", NO_FRAME),
]


if __name__ == "__main__":
    sys.exit(main(sys.argv))
