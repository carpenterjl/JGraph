"""A client of a Production Server's protobuf interface (compiler and SDK plan, stage K16).

    python k16client.py <base url> <component> <MathWorks Python client's dist folder> <mode>...

MathWorks' own Python client library (R2025b's toolbox/compiler_sdk/mps_clients/python/dist, the
matlab.production_server package over its vendored protobuf) is the oracle's own client: mode proto
calls the server through it (MWHttpClient, its builders and its decoders), mode raw posts
MATLAB_Params messages built by hand from its generated classes, so every array type, shape and fault
the schema allows is sent, and prints each MATLAB_Result as protobuf text and as the bytes the server
wrote; mode hidden repeats the errors for a session with --hide-matlab-error-stack and mode size sends
a body past --request-size-limit. One line per case, label<TAB>status reason<TAB>headers<TAB>body, the
volatile parts replaced by tokens, so a run against R2025b's muserve (record-k16.ps1) and one against
jgraph-mps (run-k16.ps1, the server tests) compare line for line.
"""
import hashlib
import http.client
import os
import re
import sys
import time
from urllib.parse import urlparse

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "k14client"))
import k14client  # noqa: E402

PROTOBUF = "application/x-google-protobuf"


def setup(dist):
    """Imports MathWorks' client package from its dist folder (an installed copy would do the same)."""
    sys.path.insert(0, dist)
    global matlab, client, mpsexceptions, MATLABArray, MATLABParams, MATLABResult, text_format, A
    import matlab  # noqa: F401
    from matlab.production_server import client, mpsexceptions
    from matlab.production_server._proto_generated import MATLABArray_pb2 as MATLABArray
    from matlab.production_server._proto_generated import MATLABParams_pb2 as MATLABParams
    from matlab.production_server._proto_generated import MATLABResult_pb2 as MATLABResult
    from google.protobuf_ml import text_format
    A = MATLABArray.MATLAB_Array


def normalize(line):
    """k14client's tokens, and the file of a stack frame in protobuf text."""
    line = k14client.normalize(line)
    return re.sub(r'file: "(?:[^"\\]|\\\\|\\/)*?[\\/]+([^"\\/]+)"', r'file: "<F>/\1"', line)


def out(label, text):
    print(normalize("%s\t%s" % (label, text)), flush=True)


# --- the values the official client decodes, as text -------------------------------------------------

def show(v):
    if isinstance(v, bool):
        return "bool %s" % v
    if isinstance(v, int):
        return "int %d" % v
    if isinstance(v, float):
        return "float %r" % v
    if isinstance(v, complex):
        return "complex %r" % v
    if isinstance(v, str):
        return "str '%s'" % v
    if isinstance(v, (bytes, bytearray)):
        return "bytes %s" % bytes(v).hex()
    if isinstance(v, dict):
        return "dict {" + ", ".join("%s=%s" % (k, show(v[k])) for k in sorted(v)) + "}"
    if isinstance(v, (list, tuple)):
        return "list [" + "; ".join(show(e) for e in v) + "]"
    name = type(v).__name__
    try:
        size = list(v.size)
        if getattr(v, "_is_complex", False):
            data = [complex(r, i) for r, i in zip(v._real, v._imag)]
        else:
            data = list(v._data)
        return "%s %s %s" % (name, size, data)
    except Exception as ex:  # a class without the array attributes
        return "%s <%s: %s>" % (name, type(ex).__name__, ex)


def exc(ex):
    if isinstance(ex, mpsexceptions.MATLABException):
        # A frame's file is the server's own extraction of the archive: its name alone is kept.
        frames = ";".join("%s@<F>/%s:%d" % (f.name, re.split(r"[\\/]", f.file)[-1], f.line) for f in ex.ml_error_stack)
        return 'MATLABException id=%s message=%s stack=[%s]' % (ex.ml_error_identifier, ex.ml_error_message.replace("\n", "\\n"), frames)
    try:
        text = str(ex)
    except Exception:  # NargoutMismatchError's own __str__ fails
        text = "<unprintable>"
    return "EXC %s %s" % (type(ex).__name__, text.replace("\n", "\\n"))


# --- the official client --------------------------------------------------------------------------------

def proto(base, component):
    c = client.MWHttpClient(base)
    proxy = getattr(c, component)

    def call(label, function, *args, **kwargs):
        try:
            result = getattr(proxy, function)(*args, **kwargs)
            out(label, show(result))
        except Exception as ex:
            out(label, exc(ex))

    call("basic", "k14add", 1.0, 2.0)
    call("ints", "k14add", 1, 2)
    call("mixed", "k14add", 1, 2.5)
    call("nargout.0", "k14add", 1.0, 2.0, nargout=0)
    call("nargout.2", "k14add", 1.0, 2.0, nargout=2)
    call("nargout.text", "k14add", 1.0, 2.0, nargout="1")
    call("nargout.negative", "k14add", 1.0, 2.0, nargout=-1)
    call("nargout.keyword", "k14add", 1.0, 2.0, foo=1)
    call("unsupported.type", "k14echo", object())
    call("unsupported.none", "k14echo", None)
    call("unsupported.set", "k14echo", {1, 2})
    # Values in, as the client's builders send them, described by the function.
    for label, value in [
        ("float", 5.0), ("int", 5), ("bool", True), ("complex", 1 + 2j), ("str", "abc"), ("empty.str", ""), ("unicode", "λ€A"),
        ("bytes", b"ab"), ("bytearray", bytearray(b"xyz")), ("list", [1, "a", True]), ("tuple", (1.0, 2.0)), ("empty.list", []),
        ("dict", {"b": 1, "a": "x"}), ("empty.dict", {}), ("nested", {"a": [1, {"b": 2.5}]}),
        ("double", matlab.double([1.0, 2.0, 3.0])), ("matrix", matlab.double([[1, 2], [3, 4]])), ("nd", matlab.double([[[1, 2], [3, 4]], [[5, 6], [7, 8]]])),
        ("empty.double", matlab.double([])), ("single", matlab.single([1.5])), ("int8", matlab.int8([1, -2])), ("uint8", matlab.uint8([1, 255])),
        ("int16", matlab.int16([-5, 7])), ("uint16", matlab.uint16([65535])), ("int32", matlab.int32([-100000])), ("uint32", matlab.uint32([4294967295])),
        ("int64", matlab.int64([-9007199254740993])), ("uint64", matlab.uint64([2 ** 64 - 1])), ("logical", matlab.logical([True, False])),
        ("complex.double", matlab.double([1 + 2j, 3 - 4j], is_complex=True)), ("complex.single", matlab.single([2 + 3j], is_complex=True)),
        ("complex.int8", matlab.int8([1 + 2j], is_complex=True)), ("nan", float("nan")), ("inf", float("-inf")), ("negzero", -0.0),
        ("big.int", 2 ** 62), ("huge.int", 2 ** 70),
    ]:
        call("in." + label, "k14describe", value)
    # Values out, as the client's decoders read them (a shape or type it has no form for is its own refusal).
    for kind in ["double", "negzero", "row", "column", "matrix", "nd", "empty", "empty03", "single", "int8", "int8s", "uint8", "int32",
                 "int64", "uint64", "complex", "complexrow", "complexsingle", "logical", "logicals", "char", "charmatrix", "emptychar",
                 "unicode", "quote", "controls", "string", "strings", "stringmissing", "cell", "cellcol", "nestedcell", "emptycell",
                 "struct", "structnested", "structarray", "emptystruct", "nofields", "sparse", "sparselogical", "inf", "nan", "pi",
                 "big", "enum", "enums", "handle", "datetime", "datetimeutc", "table", "map", "categorical", "duration", "dict", "large",
                 "nosuchkind"]:
        call("out." + kind, "k14make", kind)
    for kind in ["plain", "id", "nested", "cause", "builtin", "undefined", "multiline", "quote", "none"]:
        call("err." + kind, "k14err", kind)
    call("err.too.many.in", "k14add", 1.0, 2.0, 3.0)
    call("err.too.few.in", "k14add", 1.0)
    call("multi.3", "k14multi", 2.0, nargout=3)
    call("multi.1", "k14multi", 2.0, nargout=1)
    call("multi.0", "k14multi", 2.0, nargout=0)
    call("multi.4", "k14multi", 2.0, nargout=4)
    call("echo.0.0", "k14echo", nargout=0)
    call("echo.0.1", "k14echo", nargout=1)
    call("echo.3.3", "k14echo", 1.0, "a", True, nargout=3)
    call("echo.3.1", "k14echo", 1.0, "a", True, nargout=1)
    call("echo.3.4", "k14echo", 1.0, "a", True, nargout=4)
    call("struct", "k14struct", {"a": 1, "b": [1, 2]})
    call("noout.0", "k14noout", 5.0, nargout=0)
    call("noout.1", "k14noout", 5.0, nargout=1)
    call("print", "k14print", 2.0)
    for n_in in [0, 1, 2]:
        for n_out in [0, 1, 2, 3]:
            call("nargs.%d.%d" % (n_in, n_out), "k14nargs", *[float(i) for i in range(1, n_in + 1)], nargout=n_out)
    for k in range(2):
        call("state.%d" % k, "k14state")
    call("missing.function", "nosuch", 1.0)
    try:
        out("missing.component", show(getattr(getattr(c, "nosuch"), "k14add")(1.0, 2.0)))
    except Exception as ex:
        out("missing.component", exc(ex))
    call("large.in", "k14nargs", matlab.double(list(range(100000))))
    c.close()
    call("closed", "k14add", 1.0, 2.0)


# --- messages by hand -----------------------------------------------------------------------------------

TYPES = {
    "uint8": ("UINT8", "uint8"), "int8": ("INT8", "int8"), "uint16": ("UINT16", "uint16"), "int16": ("INT16", "int16"),
    "uint32": ("UINT32", "uint32"), "int32": ("INT32", "int32"), "uint64": ("UINT64", "uint64"), "int64": ("INT64", "int64"),
    "single": ("SINGLE", "single"), "double": ("DOUBLE", "mwdouble"),
}


def num(kind, dims, elements, imag=None, complex_flag=None, type_name=None, payload=None, layout=None):
    """A numeric array: the type's payload (int8 and uint8 as bytes), the imaginary parts beside it."""
    a = A()
    mwtype, field = TYPES[kind]
    a.type = getattr(A, type_name or mwtype)
    if dims is not None:
        a.dimension.extend(dims)
    if layout is not None:
        a.memoryLayout = layout
    if complex_flag is not None:
        a.isComplex = complex_flag
    if elements is None:
        return a
    message = getattr(a, payload or field)
    if kind in ("int8", "uint8") and (payload or field) in ("int8", "uint8"):
        message.elements = bytes(bytearray(x & 0xFF for x in elements))
        if imag is not None:
            message.imag_elements = bytes(bytearray(x & 0xFF for x in imag))
    else:
        message.elements.extend(elements)
        if imag is not None:
            message.imag_elements.extend(imag)
    return a


def dbl(dims, elements, imag=None, **options):
    return num("double", dims, elements, imag, **options)


def logical(dims, values):
    a = A()
    a.type = A.LOGICAL
    a.dimension.extend(dims)
    a.logical.elements.extend(values)
    return a


def chars(dims, text):
    a = A()
    a.type = A.CHAR
    a.dimension.extend(dims)
    a.mwchar.elements.extend(ord(ch) for ch in text)
    return a


def chars16(dims, units):
    a = A()
    a.type = A.CHAR
    a.dimension.extend(dims)
    a.mwchar.elements.extend(units)
    return a


def cell(dims, items):
    a = A()
    a.type = A.CELL
    a.dimension.extend(dims)
    a.cell.elements.extend(items)
    return a


def struct(dims, names, elements):
    a = A()
    a.type = A.STRUCT
    a.dimension.extend(dims)
    a.struct_.field_names.extend(names)
    a.struct_.elements.extend(elements)
    return a


def strings(dims, texts, sizes=None):
    a = A()
    a.type = A.STRING
    a.dimension.extend(dims)
    for text in texts:
        a.stringElements.strings.extend(ord(ch) for ch in text)
    a.stringElements.stringSizes.extend([len(t) for t in texts] if sizes is None else sizes)
    return a


def sparse(dims, ir, jc, values, kind="DOUBLE"):
    a = A()
    a.type = A.SPARSE
    a.dimension.extend(dims)
    a.sparse.type = getattr(A.Sparse, kind)
    a.sparse.ir.extend(ir)
    a.sparse.jc.extend(jc)
    if kind == "DOUBLE":
        a.sparse.mwsparsedouble.elements.extend(values)
    elif kind == "LOGICAL":
        a.sparse.logical.elements.extend(values)
    else:
        a.sparse.mwsparsesingle.elements.extend(values)
    return a


def enumeration(dims, class_name, names):
    """An enumeration array as the schema shapes it: the names in a shared string table, each element an Object naming its member."""
    a = A()
    a.type = A.ENUMERATION
    a.dimension.extend(dims)
    table = a.sharedData.stringTable
    table.stringEntry.append(class_name.encode("utf-8"))
    for name in names:
        table.stringEntry.append(name.encode("utf-8"))
    a.enumerationClassName = 0
    for i, _ in enumerate(names):
        element = a.enumerations.elements.add()
        element.enumerationNameIndex = i + 1
    return a


def params(nargout, *arrays):
    p = MATLABParams.MATLAB_Params()
    if nargout is not None:
        p.nargout = nargout
    p.rhs.extend(arrays)
    return p


class Raw:
    def __init__(self, base, component):
        parsed = urlparse(base)
        self.host = parsed.hostname
        self.port = parsed.port or 80
        self.component = component

    def post(self, label, function, body, headers=None, hexed=True, query="", component=None, method="POST"):
        if not isinstance(body, (bytes, bytearray)) and body is not None:
            body = body.SerializeToString()
        path = "/%s/%s%s" % (component or self.component, function, query)
        hdrs = {"Content-Type": PROTOBUF} if headers is None else headers
        connection = http.client.HTTPConnection(self.host, self.port, timeout=120)
        data = b""
        try:
            connection.request(method, path, body, hdrs)
            response = connection.getresponse()
            data = response.read()
            shown = sorted("%s=%s" % (k.lower(), v) for k, v in response.getheaders() if k.lower() not in k14client.DROP_HEADERS)
            line = "%s\t%d %s\t%s\t%s" % (label, response.status, response.reason, "; ".join(shown), self.describe(data, response.getheader("Content-Type"), hexed))
        except Exception as ex:  # a closed connection, a timeout
            line = "%s\tEXC %s" % (label, type(ex).__name__)
        finally:
            connection.close()
        out(label, line.split("\t", 1)[1])
        return data

    @staticmethod
    def describe(data, content_type, hexed):
        """A protobuf body as MATLAB_Result's text and its bytes; any other as text."""
        if len(data) == 0:
            return ""
        if content_type and PROTOBUF in content_type.lower():
            result = MATLABResult.MATLAB_Result()
            try:
                result.ParseFromString(data)
            except Exception as ex:
                return "unparsed %s hex=%s" % (type(ex).__name__, Raw.hexed(data))
            text = text_format.MessageToString(result, as_one_line=True)
            # A char array's elements as its text, for a readable line (the bytes stay the measure).
            text = re.sub(r"mwchar \{ ((?:elements: \d+ ?)+)\}",
                          lambda m: 'mwchar "' + "".join(chr(int(c)) for c in re.findall(r"elements: (\d+)", m.group(1))) + '"', text)
            # An error's frames name the server's own files, so their bytes are not the recording's.
            if hexed and not result.HasField("error"):
                text += " | hex=" + Raw.hexed(data)
            return text
        text = data.decode("utf-8", "replace")
        if len(data) > 1500:
            text = "<%d bytes md5 %s>" % (len(data), hashlib.md5(data).hexdigest())
        return text

    @staticmethod
    def hexed(data):
        if len(data) > 1500:
            return "<%d bytes md5 %s>" % (len(data), hashlib.md5(data).hexdigest())
        return data.hex()


KINDS = ["double", "negzero", "row", "column", "matrix", "nd", "empty", "empty03", "single", "int8", "int8s", "uint8", "int32",
         "int64", "uint64", "complex", "complexrow", "complexsingle", "logical", "logicals", "char", "charmatrix", "emptychar",
         "unicode", "quote", "controls", "string", "strings", "stringmissing", "cell", "cellcol", "nestedcell", "emptycell",
         "struct", "structnested", "structarray", "emptystruct", "nofields", "sparse", "sparselogical", "inf", "nan", "pi",
         "big", "enum", "enums", "handle", "datetime", "datetimeutc", "table", "map", "categorical", "duration", "dict", "large",
         "nosuchkind"]


def raw(base, component):
    r = Raw(base, component)
    one = dbl([1, 1], [1.0])
    two = dbl([1, 1], [2.0])
    r.post("basic", "k14add", params(1, one, two))
    # Values out, as the server writes them.
    for kind in KINDS:
        r.post("out." + kind, "k14make", params(1, chars([1, len(kind)], kind)))
    r.post("out.nargout0", "k14make", params(0, chars([1, 6], "double")))
    r.post("echo.3.3", "k14echo", params(3, one, chars([1, 1], "a"), logical([1, 1], [True])))
    r.post("echo.roundtrip", "k14echo", params(1, cell([1, 4], [num("int16", [2, 2], [1, 2, 3, 4]), chars([2, 2], "abcd"), dbl([1, 1], [1.0], [2.0]), struct([1, 2], ["a"], [one, two])])))
    # Where the string table sits for a nested enumeration, and what an echoed one looks like.
    r.post("echo.enum", "k14echo", params(1, enumeration([1, 2], "matlab.lang.OnOffSwitchState", ["on", "off"])))
    r.post("echo.cellenum", "k14echo", params(1, cell([1, 2], [enumeration([1, 1], "matlab.lang.OnOffSwitchState", ["on"]), one])))
    r.post("echo.structenum", "k14echo", params(1, struct([1, 1], ["e"], [enumeration([1, 1], "matlab.lang.OnOffSwitchState", ["off"])])))
    r.post("echo.twoenums", "k14echo", params(2, enumeration([1, 1], "matlab.lang.OnOffSwitchState", ["on"]), enumeration([1, 1], "matlab.lang.OnOffSwitchState", ["off"])))
    r.post("multi.3", "k14multi", params(3, two))
    r.post("multi.1", "k14multi", params(1, two))
    r.post("multi.0", "k14multi", params(0, two))
    r.post("multi.4", "k14multi", params(4, two), hexed=False)
    r.post("print", "k14print", params(1, two))
    r.post("noout.0", "k14noout", params(0, two))
    r.post("noout.1", "k14noout", params(1, two), hexed=False)
    r.post("state.0", "k14state", params(1))
    r.post("state.1", "k14state", params(1))
    for kind in ["plain", "id", "nested", "cause", "builtin", "undefined", "multiline", "quote", "none"]:
        r.post("err." + kind, "k14err", params(1, chars([1, len(kind)], kind)), hexed=False)
    r.post("err.too.many.in", "k14add", params(1, one, two, two), hexed=False)
    r.post("err.too.few.in", "k14add", params(1, one), hexed=False)
    r.post("err.unassigned", "k14echo", params(1), hexed=False)
    r.post("nargout.absent", "k14add", params(None, one, two))
    r.post("nargout.0", "k14add", params(0, one, two))
    r.post("nargout.2", "k14add", params(2, one, two), hexed=False)
    r.post("nargout.negative", "k14echo", params(-1, one), hexed=False)
    r.post("nargout.huge", "k14echo", params(1000000, one), hexed=False)
    r.post("rhs.empty", "k14nargs", params(1))
    # Values in, described by the function.
    cases = [
        ("double.scalar", dbl([1, 1], [2.5])), ("double.row", dbl([1, 3], [1, 2, 3])), ("double.column", dbl([3, 1], [1, 2, 3])),
        ("double.matrix", dbl([2, 3], [1, 4, 2, 5, 3, 6])), ("double.nd", dbl([2, 2, 2], [1, 5, 3, 7, 2, 6, 4, 8])),
        ("double.empty", dbl([0, 0], [])), ("double.empty03", dbl([0, 3], [])), ("double.empty10", dbl([1, 0], [])),
        ("double.specials", dbl([1, 4], [float("nan"), float("inf"), float("-inf"), -0.0])), ("double.precise", dbl([1, 2], [0.1, 1e300])),
        ("single.scalar", num("single", [1, 1], [1.5])), ("single.row", num("single", [1, 3], [1.5, float("nan"), 3e38])),
        ("int8.scalar", num("int8", [1, 1], [-5])), ("int8.row", num("int8", [1, 3], [-128, 0, 127])), ("int8.matrix", num("int8", [2, 2], [1, 2, 3, 4])),
        ("uint8.scalar", num("uint8", [1, 1], [200])), ("uint8.row", num("uint8", [1, 3], [0, 128, 255])),
        ("int16.scalar", num("int16", [1, 1], [-5])), ("int16.row", num("int16", [1, 3], [-32768, 0, 32767])),
        ("uint16.row", num("uint16", [1, 2], [0, 65535])), ("int32.row", num("int32", [1, 3], [-2147483648, -100000, 2147483647])),
        ("uint32.row", num("uint32", [1, 2], [0, 4294967295])), ("int64.row", num("int64", [1, 4], [-9223372036854775808, -9007199254740993, 9007199254740993, 9223372036854775807])),
        ("uint64.row", num("uint64", [1, 3], [0, 9007199254740993, 18446744073709551615])),
        ("logical.scalar", logical([1, 1], [True])), ("logical.row", logical([1, 3], [True, False, True])), ("logical.matrix", logical([2, 2], [True, False, False, True])),
        ("logical.empty", logical([0, 0], [])),
        ("complex.double", dbl([1, 1], [1.0], [-1.0])), ("complex.row", dbl([1, 2], [1, 3], [2, -4])), ("complex.single", num("single", [1, 1], [2.0], [3.0])),
        ("complex.int8", num("int8", [1, 1], [1], [2])), ("complex.uint8", num("uint8", [1, 2], [1, 2], [3, 4])), ("complex.int16", num("int16", [1, 1], [-1], [-2])),
        ("complex.int32", num("int32", [1, 1], [5], [6])), ("complex.int64", num("int64", [1, 1], [7], [8])), ("complex.uint64", num("uint64", [1, 1], [9], [10])),
        ("complex.flag.false", dbl([1, 1], [1.0], [2.0], complex_flag=False)),
        ("complex.imag.short", dbl([1, 2], [1, 2], [3])), ("complex.imag.long", dbl([1, 1], [1], [2, 3])), ("complex.zero.imag", dbl([1, 1], [1.0], [0.0])),
        ("char.row", chars([1, 5], "hello")), ("char.empty", chars([0, 0], "")), ("char.matrix", chars([2, 3], "adbecf")), ("char.column", chars([3, 1], "abc")),
        ("char.unicode", chars([1, 3], "λ€A")), ("char.supplementary", chars([1, 1], "😀")), ("char.surrogates", chars16([1, 2], [0xD83D, 0xDE00])),
        ("char.controls", chars([1, 3], "a\tb")), ("char.nul", chars16([1, 2], [0, 65])), ("char.past.utf16", chars16([1, 1], [0x110000])),
        ("char.empty.1x0", chars([1, 0], "")), ("char.empty.0x3", chars([0, 3], "")), ("char.nd", chars([1, 2, 2], "abcd")),
        ("cell.row", cell([1, 3], [one, chars([1, 3], "two"), dbl([2, 2], [3, 5, 4, 6])])), ("cell.column", cell([2, 1], [one, chars([1, 3], "two")])),
        ("cell.matrix", cell([2, 2], [one, two, chars([1, 1], "a"), logical([1, 1], [True])])), ("cell.empty", cell([0, 0], [])),
        ("cell.nested", cell([1, 2], [cell([1, 1], [one]), cell([0, 0], [])])), ("cell.nd", cell([1, 1, 2], [one, two])),
        ("struct.scalar", struct([1, 1], ["name", "value"], [chars([1, 5], "probe"), dbl([1, 3], [1, 2, 3])])),
        ("struct.nofields", struct([1, 1], [], [])), ("struct.empty", struct([0, 0], ["a"], [])), ("struct.empty.nofields", struct([0, 0], [], [])),
        ("struct.array", struct([1, 3], ["a"], [one, two, dbl([1, 1], [3.0])])),
        ("struct.array.twofields", struct([1, 2], ["a", "b"], [one, chars([1, 1], "x"), two, chars([1, 1], "y")])),
        ("struct.nested", struct([1, 1], ["inner", "list"], [struct([1, 1], ["x"], [one]), cell([1, 2], [one, chars([1, 1], "a")])])),
        ("struct.badname", struct([1, 1], ["1a"], [one])), ("struct.dupname", struct([1, 1], ["a", "a"], [one, two])),
        ("struct.long", struct([1, 1], ["a"], [one, two])),
        ("string.scalar", strings([1, 1], ["alpha"])), ("string.row", strings([1, 2], ["alpha", "beta"])), ("string.empty.text", strings([1, 1], [""])),
        ("string.empty", strings([0, 0], [])), ("string.missing", strings([1, 1], [], sizes=[0])),
        ("sparse.double", sparse([3, 3], [0, 2], [0, 1, 2, 2], [5.0, 6.0])), ("sparse.logical", sparse([2, 2], [0, 1], [0, 1, 2], [True, True], "LOGICAL")),
        ("sparse.single", sparse([2, 2], [0], [0, 1, 1], [1.5], "SINGLE")), ("sparse.empty", sparse([2, 2], [], [0, 0, 0], [])),
        ("sparse.badir", sparse([2, 2], [7], [0, 1, 1], [1.0])),
        ("enum.scalar", enumeration([1, 1], "matlab.lang.OnOffSwitchState", ["on"])), ("enum.row", enumeration([1, 2], "matlab.lang.OnOffSwitchState", ["on", "off"])),
        ("enum.nosuch", enumeration([1, 1], "matlab.lang.OnOffSwitchState", ["maybe"])), ("enum.noclass", enumeration([1, 1], "nosuch.Class", ["on"])),
        ("layout.row.major", num("double", [2, 3], [1, 2, 3, 4, 5, 6], layout=A.ROW_MAJOR)),
        ("count.short", dbl([1, 3], [1, 2])), ("count.long", dbl([1, 1], [1, 2])), ("count.zero.dims", dbl([0, 2], [1, 2])),
        ("dims.absent", dbl(None, [1, 2, 3])), ("dims.one", dbl([3], [1, 2, 3])), ("dims.trailing.one", dbl([2, 2, 1], [1, 2, 3, 4])),
        ("dims.ones", dbl([1, 1, 1], [5])), ("dims.zero.one", dbl([0, 1], [])), ("dims.absent.empty", dbl(None, [])),
        ("type.mismatch", num("double", [1, 2], [1, 2], type_name="INT32")), ("type.mismatch.char", num("double", [1, 2], [1, 2], type_name="CHAR")),
        ("payload.absent", dbl([1, 1], None)), ("payload.two", num("double", [1, 1], [1.5], payload="single")),
        ("type.absent", num("double", [1, 2], [1, 2], type_name=None)), ("type.arrayref", num("double", [1, 1], [1], type_name="ARRAYREF")),
    ]
    for label, value in cases:
        if value is None:
            continue
        r.post("in." + label, "k14describe", params(1, value))
    unknown = A()
    unknown.type = A.DOUBLE
    unknown.dimension.extend([1, 1])
    unknown.mwdouble.elements.append(4.0)
    unknown.arrayID = 7
    unknown.dataFrame = 3
    r.post("in.ids.set", "k14describe", params(1, unknown))
    # Shapes the request itself can take.
    body = params(1, one, two).SerializeToString()
    r.post("body.empty", "k14add", b"")
    r.post("body.garbage", "k14add", b"\xff\xff\xff\xff")
    r.post("body.truncated", "k14add", body[:-3])
    r.post("body.extra.field", "k14add", body + b"\xf8\x3e\x01")
    r.post("body.json", "k14add", b'{"rhs":[1,2],"nargout":1}')
    r.post("body.protobuf.as.json", "k14add", body, {"Content-Type": "application/json"})
    r.post("body.no.type", "k14add", body, {})
    r.post("type.charset", "k14add", body, {"Content-Type": PROTOBUF + "; charset=utf-8"})
    r.post("type.upper", "k14add", body, {"Content-Type": "Application/X-Google-Protobuf"})
    r.post("accept.json", "k14add", body, {"Content-Type": PROTOBUF, "Accept": "application/json"})
    r.post("accept.text", "k14add", body, {"Content-Type": PROTOBUF, "Accept": "text/plain"})
    r.post("query.sync", "k14add", body, query="?mode=sync")
    r.post("query.async", "k14add", body, query="?mode=async&client=k16c")
    r.post("missing.function", "nosuch", body)
    r.post("missing.component", "k14add", body, component="nosuch")
    r.post("method.PUT", "k14add", body, method="PUT")
    r.post("large.in", "k14nargs", params(1, dbl([1, 100000], [float(i) for i in range(100000)])))
    r.post("two.requests.one.connection", "k14add", body)


def edge_cases():
    """Messages that end or hang muserve's worker (k16_server: edge), each run against a session of its own."""
    one = dbl([1, 1], [1.0])
    two = dbl([1, 1], [2.0])
    body = params(1, one, two).SerializeToString()
    return [
        ("complex.flag.only", "k14describe", params(1, dbl([1, 2], [1, 2], complex_flag=True))),
        ("struct.short", "k14describe", params(1, struct([1, 1], ["a", "b"], [one]))),
        ("sparse.badjc", "k14describe", params(1, sparse([2, 2], [0], [0, 5], [1.0]))),
        ("layout.unknown", "k14describe", params(1, num("double", [2, 3], [1, 2, 3, 4, 5, 6], layout=A.UNKNOWN))),
        ("type.unknown", "k14describe", params(1, num("double", [1, 2], [1, 2], type_name="OBJECT"))),
        ("type.handleref", "k14describe", params(1, num("double", [1, 1], [1], type_name="HANDLE_OBJECT_REF"))),
        ("string.sizes.short", "k14describe", params(1, strings([1, 2], ["ab", "cd"], sizes=[2]))),
        # Two zero bytes after a message: answered, and the worker hangs on whatever request comes next.
        ("body.trailing", "k14add", body + b"\x00\x00"),
        ("body.trailing.then", "k14add", body),
    ]


def edge(base, component, which):
    """One edge case by its index, or all of them (which='all'); 'count' prints how many there are."""
    cases = edge_cases()
    if which == "count":
        print("COUNT\t%d" % (len(cases) - 1), flush=True)  # the two trailing-bytes cases share a session
        return
    r = Raw(base, component)
    if which == "all":
        chosen = cases
    else:
        index = int(which)
        chosen = cases[index:index + 2] if cases[index][0] == "body.trailing" else [cases[index]]
    for label, function, message in chosen:
        r.post("edge." + label, function, message)


def hidden(base, component):
    """--hide-matlab-error-stack: what the error message carries."""
    r = Raw(base, component)
    for kind in ["plain", "id", "nested", "builtin"]:
        r.post("hidden." + kind, "k14err", params(1, chars([1, len(kind)], kind)))
    r.post("hidden.too.many.in", "k14add", params(1, dbl([1, 1], [1.0]), dbl([1, 1], [2.0]), dbl([1, 1], [2.0])))


def size(base, component):
    """--request-size-limit 1KB: a protobuf body past it."""
    r = Raw(base, component)
    r.post("size.small", "k14nargs", params(1, dbl([1, 10], [float(i) for i in range(10)])))
    r.post("size.large", "k14nargs", params(1, dbl([1, 1000], [float(i) for i in range(1000)])))


def main(argv):
    if len(argv) < 5:
        print(__doc__)
        return 2
    sys.stdout.reconfigure(encoding="utf-8")
    setup(argv[3])
    started = time.perf_counter()
    for mode in argv[4:]:
        if mode.startswith("edge:"):
            edge(argv[1], argv[2], mode[5:])
            if mode == "edge:count":
                return 0  # nothing on standard error, so a script can read the count
            continue
        print("== " + mode, flush=True)
        {"proto": proto, "raw": raw, "hidden": hidden, "size": size}[mode](argv[1], argv[2])
    print("SECONDS\t%.1f" % (time.perf_counter() - started), file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
