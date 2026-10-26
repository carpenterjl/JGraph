"""An HTTP client of a Production Server's synchronous JSON interface (compiler and SDK plan, stage K14).

    python k14client.py <base url> <component> <mode>...

Modes: sync (the requests, values both ways, errors, outputs, headers, methods and paths), cors
(preflight and the Origin header), api (the server's own endpoints). One line per case:
label<TAB>status reason<TAB>headers<TAB>body, the volatile parts replaced by tokens, so a run against
R2025b's in-MATLAB test server (record-k14.ps1) and one against JGraph's server (run-k14.ps1, the
server tests) compare line for line. Nothing here depends on either server's own libraries.
"""
import hashlib
import http.client
import json
import re
import sys
import time
from urllib.parse import urlparse

DROP_HEADERS = {"date", "server", "connection", "keep-alive", "transfer-encoding"}


class Client:
    def __init__(self, base, component):
        parsed = urlparse(base)
        self.host = parsed.hostname
        self.port = parsed.port or 80
        self.component = component
        self.timeout = 60

    def send(self, label, method, path, body=None, headers=None, raw=False):
        """One request; the response as a line. A bytes body is sent as it is, anything else as JSON."""
        if body is not None and not isinstance(body, (bytes, bytearray)):
            body = json.dumps(body, separators=(",", ":")).encode("utf-8")
        hdrs = {"Content-Type": "application/json"} if body is not None else {}
        if headers is not None:
            hdrs = dict(headers)
        connection = http.client.HTTPConnection(self.host, self.port, timeout=self.timeout)
        try:
            connection.request(method, path, body, hdrs)
            response = connection.getresponse()
            data = response.read()
            status = "%d %s" % (response.status, response.reason)
            shown = sorted(
                "%s=%s" % (k.lower(), v) for k, v in response.getheaders() if k.lower() not in DROP_HEADERS
            )
            text = data.decode("utf-8", "replace")
            if len(data) > 1500:
                text = "<%d bytes md5 %s>" % (len(data), hashlib.md5(data).hexdigest())
            line = "%s\t%s\t%s\t%s" % (label, status, "; ".join(shown), text)
        except Exception as ex:  # a closed connection, a timeout
            line = "%s\tEXC %s" % (label, type(ex).__name__)
        finally:
            connection.close()
        print(normalize(line), flush=True)

    def call(self, label, function, rhs, nargout=1, mode="small", nan="string", extra=None, component=None, query=""):
        body = {"rhs": rhs, "nargout": nargout, "outputFormat": {"mode": mode, "nanType": nan}}
        if extra:
            body.update(extra)
        self.send(label, "POST", "/%s/%s%s" % (component or self.component, function, query), body)

    def path(self, function, component=None):
        return "/%s/%s" % (component or self.component, function)


def normalize(line):
    line = re.sub(r"\(request id=[0-9:]+\)", "(request id=<N>)", line)
    # The recording is ASCII whatever the console's code page: a character past it as \uXXXX.
    line = "".join(c if 32 <= ord(c) < 127 or c == "\t" else "\\u%04x" % ord(c) for c in line)
    # An error's stack names the function's file where the server has it: the name alone is kept.
    line = re.sub(r'"file":"(?:[^"\\]|\\\\|\\/)*?[\\/]+([^"\\/]+)"', r'"file":"<F>/\1"', line)
    line = re.sub(r'"file":"([A-Za-z0-9_]+\.m)"', r'"file":"<F>/\1"', line)
    return line


LARGE_INT8 = {"mwdata": [-1, 2], "mwsize": [1, 2], "mwtype": "int8"}
LARGE_CHARM = {"mwdata": ["adbecf"], "mwsize": [2, 3], "mwtype": "char"}
LARGE_STRING = {"mwdata": ["a", "bc"], "mwsize": [1, 2], "mwtype": "string"}
LARGE_CELL = {"mwdata": [1, "a", True], "mwsize": [1, 3], "mwtype": "cell"}
LARGE_STRUCTS = {"mwdata": {"a": [1, 2]}, "mwsize": [1, 2], "mwtype": "struct"}
LARGE_SPARSE = {"mwdata": [[0, 1], [0, 1], [1, 2]], "mwsize": [2, 2], "mwsparse": True, "mwtype": "double"}
LARGE_COMPLEX = {"mwcomplex": True, "mwdata": [1, 2], "mwsize": [1, 1], "mwtype": "double"}
LARGE_LOGICAL = {"mwdata": [True, False], "mwsize": [1, 2], "mwtype": "logical"}
LARGE_ENUM = {"mwdata": ["on"], "mwsize": [1, 1], "mwtype": "matlab.lang.OnOffSwitchState"}
LARGE_DATETIME = {"mwdata": {"TimeStamp": 1.5779232e12}, "mwmetadata": {"Format": "dd-MMM-uuuu", "TimeZone": ""},
                  "mwsize": [1, 1], "mwtype": "datetime"}
LARGE_EMPTY = {"mwdata": [], "mwsize": [0, 0], "mwtype": "double"}
LARGE_NAN = {"mwdata": "NaN"}


def sync(c):
    c.call("quiet", "k14quiet", [])
    # The request's shape.
    c.call("basic", "k14add", [1, 2])
    c.call("large.out", "k14add", [1, 2], mode="large")
    c.send("no.outputformat", "POST", c.path("k14add"), {"rhs": [1, 2], "nargout": 1})
    c.send("no.nargout", "POST", c.path("k14add"), {"rhs": [1, 2]})
    c.send("no.rhs", "POST", c.path("k14add"), {"nargout": 1})
    c.send("rhs.number", "POST", c.path("k14add"), {"rhs": 5, "nargout": 1})
    c.send("rhs.object", "POST", c.path("k14add"), {"rhs": {"a": 1}, "nargout": 1})
    c.send("rhs.null", "POST", c.path("k14add"), {"rhs": None, "nargout": 1})
    c.send("rhs.empty", "POST", c.path("k14nargs"), {"rhs": [], "nargout": 1})
    c.send("nargout.text", "POST", c.path("k14add"), {"rhs": [1, 2], "nargout": "1"})
    c.send("nargout.negative", "POST", c.path("k14add"), {"rhs": [1, 2], "nargout": -1})
    c.send("nargout.fraction", "POST", c.path("k14add"), {"rhs": [1, 2], "nargout": 1.5})
    c.send("nargout.float", "POST", c.path("k14add"), {"rhs": [1, 2], "nargout": 1.0})
    c.send("nargout.null", "POST", c.path("k14add"), {"rhs": [1, 2], "nargout": None})
    c.send("nargout.bool", "POST", c.path("k14add"), {"rhs": [1, 2], "nargout": True})
    c.send("nargout.huge", "POST", c.path("k14echo"), {"rhs": [1], "nargout": 1000000})
    c.call("nargout.zero", "k14add", [1, 2], nargout=0)
    c.call("nargout.zero.large", "k14add", [1, 2], nargout=0, mode="large")
    c.call("nargout.two.add", "k14add", [1, 2], nargout=2)
    c.call("mode.bad", "k14add", [1, 2], mode="medium")
    c.call("mode.upper", "k14add", [1, 2], mode="LARGE")
    c.call("mode.empty", "k14add", [1, 2], mode="")
    c.call("nan.bad", "k14add", [1, 2], nan="text")
    c.call("nan.upper", "k14make", ["nan"], nan="OBJECT")
    c.send("outputformat.text", "POST", c.path("k14add"), {"rhs": [1, 2], "nargout": 1, "outputFormat": "small"})
    c.send("outputformat.mode.only", "POST", c.path("k14add"), {"rhs": [1, 2], "nargout": 1, "outputFormat": {"mode": "small"}})
    c.send("outputformat.nan.only", "POST", c.path("k14make"), {"rhs": ["nan"], "nargout": 1, "outputFormat": {"nanType": "object"}})
    c.send("outputformat.empty", "POST", c.path("k14add"), {"rhs": [1, 2], "nargout": 1, "outputFormat": {}})
    c.send("outputformat.extra", "POST", c.path("k14add"), {"rhs": [1, 2], "nargout": 1, "outputFormat": {"mode": "small", "nanType": "string", "x": 1}})
    c.send("outputformat.mode.number", "POST", c.path("k14add"), {"rhs": [1, 2], "nargout": 1, "outputFormat": {"mode": 1, "nanType": "string"}})
    c.call("extra.key", "k14add", [1, 2], extra={"foo": 1})
    c.call("extra.lhs", "k14add", [1, 2], extra={"lhs": [5]})
    c.send("body.empty", "POST", c.path("k14add"), b"")
    c.send("body.text", "POST", c.path("k14add"), b"hello")
    c.send("body.array", "POST", c.path("k14add"), b"[1,2]")
    c.send("body.number", "POST", c.path("k14add"), b"5")
    c.send("body.null", "POST", c.path("k14add"), b"null")
    c.send("body.truncated", "POST", c.path("k14add"), b'{"rhs":[1,2')
    c.send("body.trailing", "POST", c.path("k14add"), b'{"rhs":[1,2],"nargout":1} x')
    c.send("body.whitespace", "POST", c.path("k14add"), b'  \n{"rhs":[1,2],"nargout":1}\n  ')
    c.send("body.bom", "POST", c.path("k14add"), b'\xef\xbb\xbf{"rhs":[1,2],"nargout":1}')
    c.send("body.latin1", "POST", c.path("k14describe"), b'{"rhs":["\xe9"],"nargout":1}')
    c.send("body.duplicate", "POST", c.path("k14add"), b'{"rhs":[1,2],"nargout":1,"nargout":2}')
    c.send("body.pretty", "POST", c.path("k14add"), b'{\n  "rhs": [1, 2],\n  "nargout": 1,\n  "outputFormat": {"mode": "small", "nanType": "string"}\n}')
    c.call("rhs.large.notation", "k14add", [{"mwdata": [1], "mwsize": [1, 1], "mwtype": "double"}, 2])
    c.call("rhs.nan.object", "k14add", [LARGE_NAN, 1])
    c.call("rhs.nan.object.out.object", "k14add", [LARGE_NAN, 1], nan="object")
    c.call("rhs.bad.small", "k14add", [[1, "a"], 1])
    c.call("rhs.bad.large", "k14add", [{"mwdata": [1, 2], "mwsize": [1, 1], "mwtype": "double"}, 1])
    c.call("rhs.unknown.type", "k14add", [{"mwdata": [1], "mwsize": [1, 1], "mwtype": "foo"}, 1])
    c.call("rhs.ragged", "k14add", [[[1, 2], [3]], 1])
    # Values in.
    for label, value in [
        ("number", 5), ("row", [[1, 2, 3]]), ("column", [1, 2, 3]), ("matrix", [[1, 2], [3, 4]]), ("nd", [[[1, 2], [3, 4]], [[5, 6], [7, 8]]]),
        ("text", "abc"), ("empty.text", ""), ("bool", True), ("bools", [[True, False]]), ("object", {"a": 1, "b": "x"}),
        ("nested.object", {"a": {"b": [1, 2]}}), ("empty.array", []), ("empty.object", {}), ("nan", LARGE_NAN), ("inf", {"mwdata": "-Inf"}),
        ("row.nan", [[1, LARGE_NAN]]), ("int8", LARGE_INT8), ("charm", LARGE_CHARM), ("string", LARGE_STRING), ("cell", LARGE_CELL),
        ("structs", LARGE_STRUCTS), ("sparse", LARGE_SPARSE), ("complex", LARGE_COMPLEX), ("logical", LARGE_LOGICAL), ("enum", LARGE_ENUM),
        ("datetime", LARGE_DATETIME), ("empty.large", LARGE_EMPTY), ("unicode", "λ€A"), ("escapes", "a\"b\\c/d\n\t"),
        ("big.number", 1e300), ("tiny.number", 5e-324), ("long.number", 12345678901234567890), ("negzero", -0.0),
    ]:
        c.call("in." + label, "k14describe", [value])
    # Values out, in both notations.
    for kind in ["double", "negzero", "row", "column", "matrix", "nd", "empty", "empty03", "single", "int8", "int8s", "uint8", "int32",
                 "int64", "uint64", "complex", "complexrow", "complexsingle", "logical", "logicals", "char", "charmatrix", "emptychar",
                 "unicode", "quote", "controls", "string", "strings", "stringmissing", "cell", "cellcol", "nestedcell", "emptycell",
                 "struct", "structnested", "structarray", "emptystruct", "nofields", "sparse", "sparselogical", "inf", "nan", "pi",
                 "big", "enum", "enums", "handle", "datetime", "datetimeutc", "table", "map", "categorical", "duration", "dict", "large",
                 "nosuchkind"]:
        c.call("out." + kind, "k14make", [kind])
        c.call("out.large." + kind, "k14make", [kind], mode="large")
    c.call("out.inf.object", "k14make", ["inf"], nan="object")
    c.call("out.large.inf.object", "k14make", ["inf"], mode="large", nan="object")
    c.call("out.nan.object", "k14make", ["nan"], nan="object")
    # Errors.
    for kind in ["plain", "id", "nested", "cause", "builtin", "undefined", "multiline", "quote"]:
        c.call("error." + kind, "k14err", [kind])
    c.call("error.id.large", "k14err", ["id"], mode="large")
    c.call("error.id.nargout0", "k14err", ["id"], nargout=0)
    c.call("error.none", "k14err", ["none"])
    c.call("error.too.many.in", "k14add", [1, 2, 3])
    c.call("error.too.few.in", "k14add", [1])
    c.call("error.make.unknown", "k14make", ["nosuch"])
    # Outputs and their counts.
    c.call("noout.nargout0", "k14noout", [5], nargout=0)
    c.call("noout.nargout1", "k14noout", [5], nargout=1)
    c.call("print", "k14print", [2])
    c.call("print.nargout0", "k14print", [2], nargout=0)
    for n_in in [0, 1, 2]:
        for n_out in [0, 1, 2, 3]:
            c.call("nargs.%d.%d" % (n_in, n_out), "k14nargs", list(range(1, n_in + 1)), nargout=n_out)
    c.call("echo.0.0", "k14echo", [], nargout=0)
    c.call("echo.0.1", "k14echo", [], nargout=1)
    c.call("echo.3.3", "k14echo", [1, "a", True], nargout=3)
    c.call("echo.3.1", "k14echo", [1, "a", True], nargout=1)
    c.call("echo.3.4", "k14echo", [1, "a", True], nargout=4)
    c.call("echo.3.3.large", "k14echo", [1, "a", True], nargout=3, mode="large")
    c.call("echo.unicode", "k14echo", ["λ€A"])
    c.call("echo.unicode.large", "k14echo", ["λ€A"], mode="large")
    c.call("echo.struct", "k14struct", [{"a": 1, "b": [1, 2]}])
    c.call("echo.struct.large", "k14struct", [{"a": 1, "b": [1, 2]}], mode="large")
    c.call("multi.3", "k14multi", [2], nargout=3)
    c.call("multi.1", "k14multi", [2], nargout=1)
    c.call("multi.0", "k14multi", [2], nargout=0)
    c.call("multi.4", "k14multi", [2], nargout=4)
    for k in range(3):
        c.call("state.%d" % k, "k14state", [])
    c.call("slow", "k14slow", [2])
    # Headers, methods and paths.
    body = b'{"rhs":[1,2],"nargout":1,"outputFormat":{"mode":"small","nanType":"string"}}'
    for label, content_type in [("none", None), ("text.plain", "text/plain"), ("json.charset", "application/json; charset=utf-8"),
                                ("json.upper", "Application/JSON"), ("protobuf", "application/x-google-protobuf"), ("xml", "application/xml"),
                                ("octet", "application/octet-stream"), ("form", "application/x-www-form-urlencoded"), ("empty", "")]:
        c.send("content." + label, "POST", c.path("k14add"), body, {} if content_type is None else {"Content-Type": content_type})
    c.send("accept.text", "POST", c.path("k14add"), body, {"Content-Type": "application/json", "Accept": "text/plain"})
    c.send("accept.protobuf", "POST", c.path("k14add"), body, {"Content-Type": "application/json", "Accept": "application/x-google-protobuf"})
    for method in ["GET", "PUT", "DELETE", "HEAD", "PATCH", "OPTIONS"]:
        c.send("method." + method, method, c.path("k14add"), body if method in ("PUT", "PATCH") else None,
               {"Content-Type": "application/json"} if method in ("PUT", "PATCH") else None)
    c.send("method.GET.body", "GET", c.path("k14add"), body)
    for label, path in [("component.only", "/" + c.component), ("component.slash", "/" + c.component + "/"),
                        ("trailing.slash", c.path("k14add") + "/"), ("extra.segment", c.path("k14add") + "/extra"),
                        ("root", "/"), ("function.only", "/k14add"), ("upper.function", "/" + c.component + "/K14ADD"),
                        ("upper.component", "/" + c.component.upper() + "/k14add"), ("encoded", "/" + c.component + "/k14%61dd"),
                        ("encoded.slash", "/" + c.component + "%2Fk14add"), ("query", c.path("k14add") + "?x=1"),
                        ("query.sync", c.path("k14add") + "?mode=sync"), ("query.async", c.path("k14add") + "?mode=async"),
                        ("query.async.client", c.path("k14add") + "?mode=async&client=k14c"), ("query.bogus", c.path("k14add") + "?mode=bogus"),
                        ("double.slash", "//" + c.component + "/k14add"), ("dot.segment", "/" + c.component + "/./k14add"),
                        ("unexported", c.path("k14struct")), ("missing.function", c.path("nosuch")),
                        ("missing.component", "/nosuch/k14add"), ("api.like", "/api/k14add"), ("space", "/" + c.component + "/k14 add"),
                        ("unicode.path", "/" + c.component + "/k14%CE%BB")]:
        c.send("path." + label, "POST", path, body)
    c.send("large.in", "POST", c.path("k14nargs"), {"rhs": [list(range(100000))], "nargout": 1, "outputFormat": {"mode": "small", "nanType": "string"}})
    c.send("deep.in", "POST", c.path("k14describe"), ("{\"rhs\":[" + "[" * 50 + "1" + "]" * 50 + "],\"nargout\":1}").encode())
    c.send("two.requests.one.connection", "POST", c.path("k14add"), body)


def cors(c):
    body = b'{"rhs":[1,2],"nargout":1,"outputFormat":{"mode":"small","nanType":"string"}}'
    origin = "http://client.k14.test"
    c.send("preflight", "OPTIONS", c.path("k14add"), None,
           {"Origin": origin, "Access-Control-Request-Method": "POST", "Access-Control-Request-Headers": "content-type"})
    c.send("preflight.headers.many", "OPTIONS", c.path("k14add"), None,
           {"Origin": origin, "Access-Control-Request-Method": "POST", "Access-Control-Request-Headers": "Content-Type, X-Custom, Accept"})
    c.send("preflight.no.headers", "OPTIONS", c.path("k14add"), None, {"Origin": origin, "Access-Control-Request-Method": "POST"})
    c.send("preflight.get", "OPTIONS", c.path("k14add"), None, {"Origin": origin, "Access-Control-Request-Method": "GET"})
    c.send("preflight.put", "OPTIONS", c.path("k14add"), None, {"Origin": origin, "Access-Control-Request-Method": "PUT"})
    c.send("preflight.no.method", "OPTIONS", c.path("k14add"), None, {"Origin": origin})
    c.send("preflight.no.origin", "OPTIONS", c.path("k14add"), None, {"Access-Control-Request-Method": "POST"})
    c.send("preflight.missing.function", "OPTIONS", c.path("nosuch"), None, {"Origin": origin, "Access-Control-Request-Method": "POST"})
    c.send("preflight.api", "OPTIONS", "/api/discovery", None, {"Origin": origin, "Access-Control-Request-Method": "GET"})
    c.send("preflight.null.origin", "OPTIONS", c.path("k14add"), None, {"Origin": "null", "Access-Control-Request-Method": "POST"})
    c.send("post.origin", "POST", c.path("k14add"), body, {"Content-Type": "application/json", "Origin": origin})
    c.send("post.origin.https", "POST", c.path("k14add"), body, {"Content-Type": "application/json", "Origin": "https://other.k14.test:8443"})
    c.send("post.origin.error", "POST", c.path("k14err"), b'{"rhs":["id"],"nargout":1}', {"Content-Type": "application/json", "Origin": origin})
    c.send("post.origin.bad.json", "POST", c.path("k14add"), b"{", {"Content-Type": "application/json", "Origin": origin})
    c.send("post.origin.missing", "POST", c.path("nosuch"), body, {"Content-Type": "application/json", "Origin": origin})
    c.send("get.origin.discovery", "GET", "/api/discovery", None, {"Origin": origin})
    c.send("get.origin.health", "GET", "/api/health", None, {"Origin": origin})
    c.send("post.no.origin", "POST", c.path("k14add"), body)


def api(c):
    for label, path in [("discovery", "/api/discovery"), ("health", "/api/health"), ("metrics", "/api/metrics"),
                        ("statistics", "/api/statistics"), ("archives", "/api/archives"), ("archives.slash", "/api/archives/"),
                        ("nosuch", "/api/nosuch"), ("discovery.slash", "/api/discovery/"), ("discovery.query", "/api/discovery?x=1"),
                        ("discovery.component", "/api/discovery/" + c.component), ("root", "/"), ("api", "/api"), ("api.slash", "/api/"),
                        ("upper", "/API/discovery")]:
        c.send("get." + label, "GET", path)
    c.send("post.discovery", "POST", "/api/discovery", b"{}")
    c.send("post.health", "POST", "/api/health", b"")
    c.send("delete.discovery", "DELETE", "/api/discovery")
    c.send("head.discovery", "HEAD", "/api/discovery")


def main(argv):
    if len(argv) < 4:
        print(__doc__)
        return 2
    sys.stdout.reconfigure(encoding="utf-8")
    c = Client(argv[1], argv[2])
    started = time.perf_counter()
    for mode in argv[3:]:
        print("== " + mode, flush=True)
        {"sync": sync, "cors": cors, "api": api}[mode](c)
    print("SECONDS\t%.1f" % (time.perf_counter() - started), file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
