"""An HTTP client of a Production Server's whole interface (compiler and SDK plan, stage K15).

    python k15client.py <base url> <component> <mode>...

Modes: sync, cors and api are stage K14's (k14client.py, imported from beside this file); async
(the asynchronous request protocol: mode=async, the request's representation, collection, state,
result, cancel and delete), routes (a routes file's web handlers), metrics (custom metrics and the
Prometheus text), env (what a deployed function sees), size (--request-size-limit), hidden
(--hide-matlab-error-stack), restart (--worker-restart-interval) and disabled (the API switched
off). One line per case, as k14client prints them; the recording is of R2025b's muserve, the
local server R2025b ships for matlabtest (record-k15.ps1), and the same client runs against
jgraph-mps (run-k15.ps1, the server tests).
"""
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "k14client"))
import k14client  # noqa: E402

from k14client import Client  # noqa: E402


def normalize(line):
    """k14client's tokens, and the Host header's port a web handler echoes."""
    line = k14client.normalize(line)
    return re.sub(r'"value":"(localhost|127\.0\.0\.1):[0-9]+"', '"value":"localhost:<PORT>"', line)


REQUEST_BODY = b'{"rhs":[1,2],"nargout":1,"outputFormat":{"mode":"small","nanType":"string"}}'


class K15Client(Client):
    """k14client's client with a raw sender that keeps the response for the caller."""

    def send(self, label, method, path, body=None, headers=None, raw=False):
        """k14client's line, with this client's tokens."""
        import io
        from contextlib import redirect_stdout

        buffer = io.StringIO()
        with redirect_stdout(buffer):
            super().send(label, method, path, body, headers, raw)
        print(normalize(buffer.getvalue().rstrip("\n")), flush=True)

    def send_raw(self, label, method, path, body=None, headers=None, print_line=True):
        import hashlib
        import http.client

        if body is not None and not isinstance(body, (bytes, bytearray)):
            body = json.dumps(body, separators=(",", ":")).encode("utf-8")
        hdrs = {"Content-Type": "application/json"} if body is not None else {}
        if headers is not None:
            hdrs = dict(headers)
        connection = http.client.HTTPConnection(self.host, self.port, timeout=self.timeout)
        status, reason, got_headers, data = 0, "", [], b""
        try:
            connection.request(method, path, body, hdrs)
            response = connection.getresponse()
            data = response.read()
            status, reason, got_headers = response.status, response.reason, response.getheaders()
            shown = sorted("%s=%s" % (k.lower(), v) for k, v in got_headers if k.lower() not in k14client.DROP_HEADERS)
            text = data.decode("utf-8", "replace")
            if len(data) > 1500:
                text = "<%d bytes md5 %s>" % (len(data), hashlib.md5(data).hexdigest())
            line = "%s\t%d %s\t%s\t%s" % (label, status, reason, "; ".join(shown), text)
        except Exception as ex:  # a closed connection, a timeout
            line = "%s\tEXC %s" % (label, type(ex).__name__)
        finally:
            connection.close()
        if print_line:
            print(normalize(line), flush=True)
        return status, reason, dict((k.lower(), v) for k, v in got_headers), data


def async_mode(c):
    """The asynchronous protocol as the RESTful API documents it; muserve answers 400 to all of it."""
    def start(label, function, rhs, client="k15c", query=None, component=None):
        q = query if query is not None else "?mode=async&client=" + client
        status, _, headers, data = c.send_raw("", "POST", c.path(function, component) + q, {"rhs": rhs, "nargout": 1}, print_line=False)
        info = {}
        try:
            info = json.loads(data)
        except ValueError:
            pass
        # The ids and sequence numbers are the server's own; the recording keeps their shapes.
        shown = {k: ("<ID>" if k == "id" else "<SEQ>" if k == "lastModifiedSeq" else tokens(v)) for k, v in sorted(info.items())} if isinstance(info, dict) else info
        head = "; ".join("%s=%s" % (k, tokens(v)) for k, v in sorted(headers.items()) if k not in k14client.DROP_HEADERS and k != "content-length")
        print(normalize("%s\t%d\t%s\t%s" % (label, status, head, json.dumps(shown, sort_keys=True))), flush=True)
        return status, info

    def tokens(value):
        text = str(value)
        text = re.sub(r"/~[^/]+/requests/[0-9a-f-]{36}", "/~<CLIENT>/requests/<ID>", text)
        text = re.sub(r"/~[^/]+/requests", "/~<CLIENT>/requests", text)
        text = re.sub(r"^[0-9a-f-]{36}$", "<ID>", text)
        return text

    def show(label, status, reason, headers, data):
        try:
            body = json.loads(data)
        except ValueError:
            body = data.decode("utf-8", "replace")
        if isinstance(body, dict):
            body = {k: ("<ID>" if k == "id" else "<SEQ>" if k in ("lastModifiedSeq", "createdSeq") else
                        [{kk: ("<ID>" if kk == "id" else "<SEQ>" if kk == "lastModifiedSeq" else tokens(vv)) for kk, vv in sorted(e.items())} for e in v] if k == "data" else tokens(v))
                    for k, v in sorted(body.items())}
            body = json.dumps(body, sort_keys=True)
        head = "; ".join("%s=%s" % (k, tokens(v)) for k, v in sorted(headers.items()) if k not in k14client.DROP_HEADERS)
        print(normalize("%s\t%d %s\t%s\t%s" % (label, status, reason, head, body)), flush=True)

    def get(label, path, method="GET", body=None):
        status, reason, headers, data = c.send_raw("", method, path, body, print_line=False)
        show(label, status, reason, headers, data)
        return status, data

    def wait_until(self_url, states, tries=40):
        last = ""
        for _ in range(tries):
            status, _, _, data = c.send_raw("", "GET", self_url, print_line=False)
            try:
                last = json.loads(data).get("state", "")
            except ValueError:
                return "<no json>"
            if last in states:
                return last
            time.sleep(0.25)
        return last

    status, info = start("post", "k14add", [1, 2])
    if status != 201 or "self" not in info:
        # No asynchronous protocol here: the paths a client would use.
        for label, method, path in [("self", "GET", "/~k15c/requests/x"), ("info", "GET", "/~k15c/requests/x/info"),
                                    ("result", "GET", "/~k15c/requests/x/result"), ("cancel", "POST", "/~k15c/requests/x/cancel"),
                                    ("delete", "DELETE", "/~k15c/requests/x"), ("up", "GET", "/~k15c/requests?since=0&clients=k15c")]:
            get("absent." + label, path, method)
        start("post.noclient", "k14add", [1, 2], query="?mode=async")
        start("post.emptyclient", "k14add", [1, 2], query="?mode=async&client=")
        start("post.sync", "k14add", [1, 2], query="?mode=sync")
        return
    self_url, up = info["self"], info["up"]
    get("representation", self_url)
    get("info", self_url + "/info")
    state = wait_until(self_url, ("READY", "ERROR"))
    print("ready.state\t" + state, flush=True)
    get("representation.ready", self_url)
    get("info.ready", self_url + "/info")
    get("result", self_url + "/result")
    get("result.again", self_url + "/result")
    get("up.since0.ids", up + "?since=0&ids=" + info["id"])
    get("up.since0.clients", up + "?since=0&clients=k15c")
    get("up.since0.both", up + "?since=0&clients=k15c&ids=" + info["id"])
    get("up.since0.otherclient", up + "?since=0&clients=nosuchclient")
    get("up.nosince", up + "?clients=k15c")
    get("up.badsince", up + "?since=abc&clients=k15c")
    get("up.negsince", up + "?since=-1&clients=k15c")
    get("up.noquery", up)
    get("up.badid", up + "?since=0&ids=nosuch")
    get("up.post", up + "?since=0&clients=k15c", "POST", b"")
    get("cancel.ready", self_url + "/cancel", "POST", b"")
    get("cancel.get", self_url + "/cancel")
    get("delete.ready", self_url, "DELETE")
    get("representation.deleted", self_url)
    get("info.deleted", self_url + "/info")
    get("result.deleted", self_url + "/result")
    get("cancel.deleted", self_url + "/cancel", "POST", b"")
    get("delete.deleted", self_url, "DELETE")
    # A slow request: cancelled while it runs, then deleted.
    status, slow = start("post.slow", "k14slow", [3])
    slow_url = slow["self"]
    state = wait_until(slow_url, ("PROCESSING", "READY", "ERROR"), tries=20)
    print("slow.state\t" + state, flush=True)
    get("slow.result.running", slow_url + "/result")
    get("slow.delete.running", slow_url, "DELETE")
    get("slow.cancel", slow_url + "/cancel", "POST", b"")
    state = wait_until(slow_url, ("CANCELLED", "READY", "ERROR"))
    print("slow.cancelled.state\t" + state, flush=True)
    get("slow.cancel.again", slow_url + "/cancel", "POST", b"")
    get("slow.result.cancelled", slow_url + "/result")
    get("slow.delete.cancelled", slow_url, "DELETE")
    # An erroring function: ERROR state, the error in the result.
    status, err = start("post.error", "k14err", ["id"])
    state = wait_until(err["self"], ("READY", "ERROR"))
    print("error.state\t" + state, flush=True)
    get("error.result", err["self"] + "/result")
    get("error.delete", err["self"], "DELETE")
    # The request's shape faults are answered at the POST.
    start("post.nosuchfn", "nosuch", [1])
    start("post.nosuchcomp", "k14add", [1, 2], component="nosuch")
    status, bad = start("post.badbody", "k14add", "notalist")
    if "self" in bad:
        state = wait_until(bad["self"], ("READY", "ERROR"))
        print("badbody.state\t" + state, flush=True)
        get("badbody.result", bad["self"] + "/result")
        get("badbody.delete", bad["self"], "DELETE")
    start("post.noclient", "k14add", [1, 2], query="?mode=async")
    start("post.emptyclient", "k14add", [1, 2], query="?mode=async&client=")
    start("post.clientspace", "k14add", [1, 2], query="?mode=async&client=k15%20c")
    start("post.sync", "k14add", [1, 2], query="?mode=sync&client=k15c")
    start("post.modeupper", "k14add", [1, 2], query="?mode=ASYNC&client=k15c")
    start("post.clientfirst", "k14add", [1, 2], query="?client=k15c&mode=async")
    start("post.extraparam", "k14add", [1, 2], query="?mode=async&client=k15c&x=1")
    get("unknown.client", "/~nosuchclient/requests?since=0&clients=nosuchclient")
    get("unknown.request", "/~k15c/requests/00000000-0000-0000-0000-000000000000")
    get("unknown.request.result", "/~k15c/requests/00000000-0000-0000-0000-000000000000/result")
    get("unknown.request.info", "/~k15c/requests/00000000-0000-0000-0000-000000000000/info")
    get("unknown.request.cancel", "/~k15c/requests/00000000-0000-0000-0000-000000000000/cancel", "POST", b"")
    get("unknown.request.delete", "/~k15c/requests/00000000-0000-0000-0000-000000000000", "DELETE")
    get("malformed.id", "/~k15c/requests/not-a-uuid")
    get("tilde.only", "/~k15c")
    get("tilde.other", "/~k15c/other")
    # Two requests of two clients; the collection of each.
    status, a = start("post.two.a", "k14add", [1, 2], client="k15a")
    status, b = start("post.two.b", "k14add", [3, 4], client="k15b")
    wait_until(a["self"], ("READY", "ERROR"))
    wait_until(b["self"], ("READY", "ERROR"))
    get("two.up.a", a["up"] + "?since=0&clients=k15a")
    get("two.up.b", b["up"] + "?since=0&clients=k15b")
    get("two.up.both", a["up"] + "?since=0&clients=k15a,k15b")
    get("two.up.ids", a["up"] + "?since=0&ids=" + a["id"] + "," + b["id"])
    get("two.a.by.b.up", b["up"] + "?since=0&ids=" + a["id"])
    get("two.delete.a", a["self"], "DELETE")
    get("two.delete.b", b["self"], "DELETE")


def routes(c):
    """The web handlers a routes file maps paths to."""
    body = b'{"x":1}'
    for label, method, path, sent, headers in [
        ("get", "GET", "/k15/web", None, None),
        ("post", "POST", "/k15/web", body, {"Content-Type": "application/json"}),
        ("post.text", "POST", "/k15/web", b"hello there", {"Content-Type": "text/plain"}),
        ("post.notype", "POST", "/k15/web", body, {}),
        ("post.headers", "POST", "/k15/web", body, {"Content-Type": "application/json", "X-Custom": "yes", "Accept": "text/plain"}),
        ("put", "PUT", "/k15/web", body, {"Content-Type": "application/json"}),
        ("delete", "DELETE", "/k15/web", None, None),
        ("head", "HEAD", "/k15/web", None, None),
        ("options", "OPTIONS", "/k15/web", None, None),
        ("patch", "PATCH", "/k15/web", body, {"Content-Type": "application/json"}),
        ("sub", "GET", "/k15/web/more/segments", None, None),
        ("query", "GET", "/k15/q?name=value&n=2", None, None),
        ("query.plain", "GET", "/k15/q", None, None),
        ("query.slash", "GET", "/k15/q/", None, None),
        ("query.web", "GET", "/k15/web?name=value", None, None),
        ("nomatch", "GET", "/k15/nothing", None, None),
        ("nomatch.post", "POST", "/k15/nothing", body, {"Content-Type": "application/json"}),
        ("anywhere", "GET", "/x/k15/anywhere/y", None, None),
        ("first", "GET", "/k15/first", None, None),
        ("missingfn", "GET", "/k15/missingfn", None, None),
        ("othercomp", "GET", "/k15/othercomp", None, None),
        ("plain.fn", "GET", "/k15/plain", None, None),
        ("plain.fn.post", "POST", "/k15/plain", REQUEST_BODY, {"Content-Type": "application/json"}),
        ("direct.web", "POST", c.path("k15web"), REQUEST_BODY, {"Content-Type": "application/json"}),
        ("direct.web.route", "POST", "/k15comp/k15web", body, {"Content-Type": "application/json"}),
        ("inst", "GET", "/k15/inst", None, None),
        ("archonly", "GET", "/k15/archonly", None, None),
        ("arch.web", "GET", "/arch/web", None, None),
        ("arch.comp.web", "GET", "/" + c.component + "/arch/web", None, None),
        ("arch.comp.named", "GET", "/" + c.component + "/arch/named", None, None),
        ("arch.comp.bad", "GET", "/" + c.component + "/arch/bad/ok", None, None),
        ("arch.comp.inst", "GET", "/" + c.component + "/k15/inst", None, None),
        ("arch.comp.archonly", "GET", "/" + c.component + "/k15/archonly", None, None),
        ("encoded", "GET", "/k15/web/a%20b", None, None),
        ("unicode", "GET", "/k15/web/%CE%BB", None, None),
        ("dots", "GET", "/k15/web/../web", None, None),
        ("case", "GET", "/K15/WEB", None, None),
        ("large", "POST", "/k15/web", b"x" * 3000, {"Content-Type": "application/octet-stream"}),
        ("binary", "POST", "/k15/web", bytes(range(256)), {"Content-Type": "application/octet-stream"}),
        ("origin", "GET", "/k15/web", None, {"Origin": "http://client.k14.test"}),
        ("preflight", "OPTIONS", "/k15/web", None, {"Origin": "http://client.k14.test", "Access-Control-Request-Method": "POST"}),
    ]:
        c.send("route." + label, method, path, sent, headers)
    for kind in ["ok", "nocode", "nomessage", "noapi", "noheaders", "nobody", "empty", "number", "text", "bodychar", "bodydouble", "bodyempty",
                 "bodycolumn", "code404", "code201", "code599", "codeint", "codetext", "codefraction", "messageempty", "messagestring", "headersrow",
                 "headersnumber", "headersempty", "headerslength", "headersmany", "headersstruct", "apiother", "apirow", "extra", "array", "error",
                 "slow", "unknown"]:
        c.send("bad." + kind, "GET", "/k15/bad/" + kind)
    c.send("bad.query", "GET", "/k15/bad/ok?q=1")
    c.send("bad.post", "POST", "/k15/bad/ok", body, {"Content-Type": "application/json"})


def metrics(c):
    """Custom metrics and the Prometheus text."""
    def show(label):
        status, reason, headers, data = c.send_raw("", "GET", "/api/metrics", print_line=False)
        head = "; ".join("%s=%s" % (k, v) for k, v in sorted(headers.items()) if k not in k14client.DROP_HEADERS and k != "content-length")
        lines = []
        for line in data.decode("utf-8", "replace").replace("\r\n", "\n").split("\n"):
            if not line:
                continue
            if line.startswith("# "):
                lines.append(line)
            elif line.startswith("matlabprodserver_"):
                # The server's own numbers move; their names stay.
                lines.append(re.sub(r" .*$", " <V>", line))
            else:
                lines.append(line)
        print(normalize("%s\t%d %s\t%s\t%s" % (label, status, reason, head, " || ".join(lines))), flush=True)

    show("metrics.before")
    for kind in ["counter", "counter", "counter2", "gauge", "gauge2", "gaugeint", "both", "big", "small", "badname", "dash", "emptyname", "cellname",
                 "zero", "negative", "textvalue", "vector", "complex", "nan", "inf", "logical", "noargs", "onearg", "threeargs", "output",
                 "raw", "rawgauge", "rawbad", "rawnoarchive", "rawspace", "rawmid", "rawdisp", "rawstderr", "rawdash", "rawtwo", "rawcounterdown",
                 "rawtype", "rawgaugeascounter", "nosuchkind"]:
        c.call("metric." + kind, "k15metric", [kind])
    show("metrics.after")
    c.call("metric.counter.again", "k15metric", ["counter"])
    show("metrics.again")
    c.send("metrics.post", "POST", "/api/metrics", b"")
    c.send("metrics.head", "HEAD", "/api/metrics")
    c.send("metrics.query", "GET", "/api/metrics?x=1")
    c.send("metrics.slash", "GET", "/api/metrics/")
    c.send("metrics.accept.json", "GET", "/api/metrics", None, {"Accept": "application/json"})


def env(c):
    c.call("env", "k15env", ["k15key"])
    c.call("env.again", "k15env", ["k15key"])
    c.call("env.nosuchkey", "k15env", ["nosuchkey"])
    c.call("env.describe", "k14describe", [{"mwdata": [1], "mwsize": [1, 1], "mwtype": "double"}])
    c.send("health", "GET", "/api/health")
    c.send("health.head", "HEAD", "/api/health")
    c.send("health.options", "OPTIONS", "/api/health")
    c.send("health.delete", "DELETE", "/api/health")
    c.send("health.query", "GET", "/api/health?x=1")
    c.send("health.slash", "GET", "/api/health/")
    c.send("discovery", "GET", "/api/discovery")


def size(c):
    c.send("size.small", "POST", c.path("k14add"), REQUEST_BODY)
    c.send("size.900", "POST", c.path("k14nargs"), ("{\"rhs\":[[" + ",".join(["1"] * 400) + "]],\"nargout\":1}").encode())
    c.send("size.1100", "POST", c.path("k14nargs"), ("{\"rhs\":[[" + ",".join(["1"] * 520) + "]],\"nargout\":1}").encode())
    c.send("size.2000", "POST", c.path("k14nargs"), ("{\"rhs\":[[" + ",".join(["1"] * 980) + "]],\"nargout\":1}").encode())
    c.send("size.100000", "POST", c.path("k14nargs"), ("{\"rhs\":[[" + ",".join(["1"] * 50000) + "]],\"nargout\":1}").encode())
    c.send("size.route.2000", "POST", "/k15/web", b"x" * 2000, {"Content-Type": "text/plain"})
    c.send("size.after", "POST", c.path("k14add"), REQUEST_BODY)


def hidden(c):
    for kind in ["plain", "id", "nested", "cause", "builtin"]:
        c.call("hidden." + kind, "k14err", [kind])
    c.call("hidden.large", "k14err", ["id"], mode="large")


def restart(c, wait=20):
    c.call("restart.state.1", "k14state", [])
    c.call("restart.state.2", "k14state", [])
    time.sleep(wait)
    c.call("restart.state.3", "k14state", [])
    time.sleep(2)
    c.call("restart.state.4", "k14state", [])


def disabled(c):
    for label, path in [("discovery", "/api/discovery"), ("metrics", "/api/metrics"), ("health", "/api/health"), ("statistics", "/api/statistics"),
                        ("archives", "/api/archives")]:
        c.send("disabled." + label, "GET", path)
    c.send("disabled.discovery.post", "POST", "/api/discovery", b"{}")
    c.send("disabled.metrics.post", "POST", "/api/metrics", b"")
    c.send("disabled.discovery.head", "HEAD", "/api/discovery")
    c.send("disabled.metrics.head", "HEAD", "/api/metrics")
    c.call("disabled.call", "k14add", [1, 2])
    c.call("disabled.metric", "k15metric", ["counter"])
    c.send("disabled.metrics.after", "GET", "/api/metrics")


def main(argv):
    if len(argv) < 4:
        print(__doc__)
        return 2
    sys.stdout.reconfigure(encoding="utf-8")
    c = K15Client(argv[1], argv[2])
    started = time.perf_counter()
    for mode in argv[3:]:
        print("== " + mode, flush=True)
        {"sync": k14client.sync, "cors": k14client.cors, "api": k14client.api, "async": async_mode, "routes": routes, "metrics": metrics,
         "env": env, "size": size, "hidden": hidden, "restart": restart, "disabled": disabled}[mode](c)
    print("SECONDS\t%.1f" % (time.perf_counter() - started), file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
