"""Compares JGraph's K15 server run with R2025b's muserve recording (compiler and SDK plan, stage K15).

    python compare-k15.py <jgraph server out>

The recording is "== session" sections of "== mode" sections of label<TAB>status<TAB>headers<TAB>body
lines. Every label of R2025b's is one of: agrees, a recorded divergence (ADR 0271, named below with
its reason), or unexplained. A web handler's echo of the request headers is compared with the headers
sorted by name, since Kestrel keeps them in its own order. The exit code is the number unexplained.
"""
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REC = HERE / "recordings"

# --- the labels whose JGraph answer differs by design ----------------------------------------------
KNOWN = {
    # stage K14's (ADR 0270), as muserve answers them the same as the in-MATLAB test server
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
    "in.big.number": "JGraph prints the number with its own digits (k14describe's %.17g)",
    "in.tiny.number": "JGraph prints the number with its own digits (k14describe's %.17g)",
    "path.space": "the client itself refuses a space in a URL",
    "path.unexported": "k14struct's struct from the request's cell: JGraph's words for the field access on a cell (open item 213)",
    # stage K15's (ADR 0271)
    "path.query.async": "muserve has no asynchronous requests; JGraph starts one (ADR 0271)",
    "path.query.async.client": "muserve has no asynchronous requests; JGraph starts one (ADR 0271)",
    "get.metrics": "the server's own metric values (the metrics mode compares the names and the custom values)",
    "metrics.accept.json": "the server's own metric values (the metrics mode compares the names and the custom values)",
    "route.othercomp": "muserve's phrase leaks a C++ function's name after 'component not found'; JGraph's stops there (ADR 0271)",
    "bad.headerslength": "a handler's Content-Length header: muserve sends it beside the body's, JGraph sends the body's alone (ADR 0271)",
    "env": "ctfroot's parent folder and pwd are the server's own; JGraph's which() answers the extracted file's path (ADR 0271)",
    "env.again": "as env (ADR 0271)",
    "env.nosuchkey": "as env (ADR 0271)",
    "metric.badname": "an error's frames: the metric function's own frame is JGraph's builtin's, not an M-file's (ADR 0271)",
    "metric.dash": "as metric.badname (ADR 0271)",
    "metric.emptyname": "as metric.badname (ADR 0271)",
    "metric.cellname": "as metric.badname (ADR 0271)",
    "metric.zero": "as metric.badname (ADR 0271)",
    "metric.negative": "as metric.badname (ADR 0271)",
    "metric.textvalue": "as metric.badname (ADR 0271)",
    "metric.vector": "as metric.badname (ADR 0271)",
    "metric.complex": "as metric.badname (ADR 0271)",
    "metric.logical": "as metric.badname (ADR 0271)",
    "metric.noargs": "as metric.badname (ADR 0271)",
    "metric.onearg": "as metric.badname (ADR 0271)",
    "metric.threeargs": "no mpsExecuteInMatlab frame (ADR 0270)",
    "metric.output": "no mpsExecuteInMatlab frame (ADR 0270)",
    "metric.nosuchkind": "no mpsExecuteInMatlab frame (ADR 0270)",
    "hidden.builtin": "JGraph's message for a dimension mismatch (ADR 0270)",
}

# The asynchronous protocol: muserve has none (400 "mode=async is not supported."), JGraph answers the documented one.
ASYNC_REASON = "muserve has no asynchronous requests; JGraph answers the RESTful API's documented protocol (ADR 0271)"


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
        if label in ("EXIT", "SECONDS"):
            continue
        cases[session + "/" + mode + "/" + label] = rest
    return cases


def canonical(key, value):
    """A web handler's echo with its headers sorted; everything else as it is."""
    if "/routes/" not in key:
        return value
    # The value is status, headers and body (a HEAD's has no body part; a body echoing a tab was split at it).
    parts = value.split("\t")
    if len(parts) < 2 or "x-k15=handled" not in parts[1]:
        return value
    if len(parts) > 3:
        parts = [parts[0], parts[1], "\t".join(parts[2:])]
    # The echo's length follows the Host header's port digits, which each run's port sets (a HEAD has the length alone).
    parts[1] = re.sub(r"content-length=\d+", "content-length=<L>", parts[1])
    if len(parts) < 3:
        return "\t".join(parts)
    if parts[-1].startswith("<") and "bytes md5" in parts[-1]:
        parts[-1] = "<echo>"
        return "\t".join(parts)
    try:
        body = json.loads(parts[-1], strict=False)
    except ValueError:
        return "\t".join(parts)
    if isinstance(body, dict) and "Headers" in body:
        body["Headers"] = sorted(body["Headers"], key=lambda h: h["name"].lower())
        parts[-1] = json.dumps(body, sort_keys=True)
    return "\t".join(parts)


def main(argv):
    if len(argv) != 2:
        print(__doc__)
        return 2
    expected = read_server(REC / "k15_server.out.txt")
    actual = read_server(argv[1])
    agree = []
    divergent = []
    unexplained = []
    for key, value in expected.items():
        mine = actual.get(key)
        if mine is not None and canonical(key, mine) == canonical(key, value):
            agree.append(key)
            continue
        label = key.split("/", 2)[2]
        mode = key.split("/", 2)[1]
        reason = ASYNC_REASON if mode == "async" else KNOWN.get(label)
        if reason:
            divergent.append((key, reason))
        else:
            unexplained.append((key, value, mine))
    print(f"k15_server: {len(agree)} of {len(expected)} agree, {len(divergent)} recorded divergences, {len(unexplained)} unexplained")
    for key, value, mine in unexplained:
        print(f"  ? {key}")
        print(f"      R2025b: {value[:300]!r}")
        print(f"      JGraph: {None if mine is None else mine[:300]!r}")
    return len(unexplained)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
