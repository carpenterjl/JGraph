"""JGraph's out-of-process Python console.

The JGraph host launches this script with ``python -u -X utf8 jgraph_console.py`` and talks to it in
newline-delimited JSON over stdin/stdout. It keeps one module-level namespace alive for the whole
session, so a name bound at the prompt is still bound at the next one.

stdout is the protocol channel, so the real stdout is captured at import time and everything the
user's code prints is redirected into ``out`` messages instead. Nothing else may write to fd 1.

Protocol
--------
host -> child   {"id": N, "op": "exec", "code": "..."}
                {"op": "vars"}
                {"op": "shutdown"}
                {"type": "return", "seq": M, "value": ..., "message": "..."}
child -> host   {"type": "ready"}
                {"id": N, "type": "out"|"err", "text": "..."}
                {"id": N, "type": "done", "ok": bool, "message": "...", "line": N, "exit": N}
                {"type": "call", "seq": M, "fn": "plot", "args": [...]}
                {"type": "vars", "items": [{"name", "type", "repr", "data"}]}
"""

import json
import sys
import traceback

# Captured before anything can replace them: these are the protocol channel and must stay clean.
_PROTOCOL_OUT = sys.stdout
_PROTOCOL_IN = sys.stdin

# An array larger than this reports its shape and sends no data — the same policy the host applies to
# its own variables, for the same reason: nobody inspects ten million cells in a grid.
MAX_DATA_ELEMENTS = 2_000_000

# Truncation width for a variable's display string.
MAX_REPR = 200

_session = {"__name__": "__console__", "__builtins__": __builtins__}
_current_id = 0
_next_seq = 0


def _send(message):
    _PROTOCOL_OUT.write(json.dumps(message) + "\n")
    _PROTOCOL_OUT.flush()


def _read():
    line = _PROTOCOL_IN.readline()
    if not line:
        return None
    line = line.strip()
    if not line:
        return {}
    try:
        return json.loads(line)
    except ValueError:
        return {}


class _Redirect:
    """A file-like object that turns writes into protocol messages tagged with the current statement."""

    def __init__(self, kind):
        self._kind = kind

    def write(self, text):
        if text:
            _send({"id": _current_id, "type": self._kind, "text": text})
        return len(text) if text else 0

    def flush(self):
        pass

    def isatty(self):
        return False


def _call_host(fn, *args):
    """Sends a plotting call to the host and blocks until it answers.

    The child is single-threaded and the host runs one statement at a time, so the very next message
    on stdin is this call's reply. Anything else means the host is gone.
    """
    global _next_seq
    _next_seq += 1
    seq = _next_seq
    _send({"type": "call", "seq": seq, "fn": fn, "args": [_to_json(a) for a in args]})

    reply = _read()
    if reply is None:
        raise RuntimeError("The JGraph host closed the connection.")
    if reply.get("message"):
        raise RuntimeError(reply["message"])
    return reply.get("value")


def _to_json(value):
    """Flattens a Python value into something the host's JSON reader understands.

    Sequences (including numpy arrays, via ``tolist``) become JSON arrays of numbers; everything else
    is passed through and the host reports a type error if it cannot use it.
    """
    if value is None or isinstance(value, (bool, int, float, str)):
        return value
    tolist = getattr(value, "tolist", None)
    if callable(tolist):
        return tolist()
    if isinstance(value, (list, tuple, range)):
        return [_to_json(v) for v in value]
    return str(value)


def _install_jgraph_module():
    """Defines the ``jgraph`` module of RPC proxies and exposes its verbs as bare names."""
    import types

    module = types.ModuleType("jgraph")
    module.__doc__ = "JGraph plotting verbs, proxied to the host application."

    def make(name):
        def proxy(*args):
            return _call_host(name, *args)

        proxy.__name__ = name
        proxy.__doc__ = f"JGraph {name}(), executed in the JGraph host process."
        return proxy

    for name in (
        "figure", "subplot", "plot", "scatter", "bar", "stem", "histogram",
        "title", "xlabel", "ylabel", "legend", "grid", "xlim", "ylim", "hold",
        "colorbar", "show",
    ):
        setattr(module, name, make(name))

    sys.modules["jgraph"] = module
    # Bare names too, so `plot(x, y)` works at the prompt exactly as it does in the other languages.
    for name in dir(module):
        if not name.startswith("_"):
            _session[name] = getattr(module, name)
    _session["jgraph"] = module


class _ConsoleExit(Exception):
    """Raised by exit()/quit() at the prompt; carries the code the host should report."""

    def __init__(self, code):
        super().__init__(code)
        self.code = code


def _install_exit():
    def _exit(code=0):
        raise _ConsoleExit(int(code))

    _session["exit"] = _exit
    _session["quit"] = _exit


def _error_line(exc_info):
    """The 1-based line in the user's statement the error came from, or 0 when it was elsewhere."""
    line = 0
    for frame in traceback.extract_tb(exc_info[2]):
        if frame.filename == "<console>":
            line = frame.lineno or 0
    return line


def _execute(message):
    global _current_id
    _current_id = message.get("id", 0)
    code = message.get("code", "")

    try:
        # 'single' mode is what makes a bare expression echo its value, the way a REPL should. It only
        # accepts one statement, so a multi-statement paste falls back to 'exec' (which echoes
        # nothing — the same trade the standard Python REPL makes for a pasted block).
        try:
            compiled = compile(code, "<console>", "single")
        except SyntaxError:
            compiled = compile(code, "<console>", "exec")

        exec(compiled, _session)  # noqa: S102 - executing user input is this program's purpose
        _send({"id": _current_id, "type": "done", "ok": True})
    except _ConsoleExit as stop:
        _send({"id": _current_id, "type": "done", "ok": True, "exit": stop.code})
    except SystemExit as stop:
        code = stop.code if isinstance(stop.code, int) else 0
        _send({"id": _current_id, "type": "done", "ok": True, "exit": code})
    except SyntaxError as error:
        _send({
            "id": _current_id,
            "type": "done",
            "ok": False,
            "message": f"{type(error).__name__}: {error.msg}",
            "line": error.lineno or 0,
        })
    except BaseException as error:  # noqa: BLE001 - a failed statement must not end the session
        info = sys.exc_info()
        sys.stderr.write("".join(traceback.format_exception_only(type(error), error)))
        _send({
            "id": _current_id,
            "type": "done",
            "ok": False,
            "message": f"{type(error).__name__}: {error}",
            "line": _error_line(info),
        })
    finally:
        _current_id = 0


def _project(name, value):
    kind = type(value).__name__
    data = None

    if isinstance(value, bool):
        mapped = "bool"
    elif isinstance(value, (int, float)):
        mapped = "number"
    elif isinstance(value, str):
        mapped = "string"
    elif isinstance(value, (list, tuple)) or kind == "ndarray":
        mapped = "array"
        data = _numeric_data(value)
    else:
        mapped = kind

    try:
        text = repr(value)
    except BaseException:  # noqa: BLE001 - a broken __repr__ must not break the snapshot
        text = f"<{kind}>"

    if len(text) > MAX_REPR:
        text = text[:MAX_REPR] + "…"

    return {"name": name, "type": mapped, "repr": text, "data": data}


def _numeric_data(value):
    try:
        tolist = getattr(value, "tolist", None)
        items = tolist() if callable(tolist) else list(value)
        if len(items) > MAX_DATA_ELEMENTS:
            return None
        return [float(v) for v in items]
    except BaseException:  # noqa: BLE001 - a non-numeric sequence simply has no grid view
        return None


def _snapshot():
    items = []
    for name, value in list(_session.items()):
        if name.startswith("_") or callable(value):
            continue
        if type(value).__name__ == "module":
            continue
        items.append(_project(name, value))
    items.sort(key=lambda item: item["name"])
    _send({"type": "vars", "items": items})


def main():
    sys.stdout = _Redirect("out")
    sys.stderr = _Redirect("err")
    _install_jgraph_module()
    _install_exit()
    _send({"type": "ready"})

    while True:
        message = _read()
        if message is None:
            return
        op = message.get("op")
        if op == "exec":
            _execute(message)
        elif op == "vars":
            _snapshot()
        elif op == "shutdown":
            return


if __name__ == "__main__":
    main()
