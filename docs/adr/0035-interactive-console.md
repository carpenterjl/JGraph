# ADR 0035 — The interactive console and the session model

## Status

Accepted (M31, 2026-07-25).

## Context

JGraph could run a script, and that was all. Every run started from nothing:
`JgsRunner.Run` called `JG.Reset()`, released the previous run's packed buffers, and rebuilt the
built-in scope before parsing a line. That is exactly right for a one-shot run and exactly wrong for a
prompt, where the whole value is that `x` is still there on the next line.

The user asked for the MATLAB Command Window: type a statement, see the result, watch the Workspace
pane fill in, plot from the prompt — in any of the four languages, with a picker to switch between
them.

Most of what was needed already existed. The interpreter has implemented MATLAB console echo
semantics since M21b (`;` suppression, `ans` binding, a bare variable displaying itself), the
Variables pane and Data Viewer have been wired end to end since M15, and `ScriptRunResult` already
carries the variables a run left behind. What was missing was a **session**.

## Decision

### The seam

`IScriptRepl` / `IScriptSession` sit beside `IScriptEngine`, not inside it — a capability hosts
feature-detect, exactly as they already do for `IJgsDebuggable`. An engine that only knows how to run
a whole file stays a valid engine.

`ExecuteAsync` returns `ScriptRunResult`, the same type a script run returns, so the host reports a
typed statement and an F5 run through one code path (`ShowRunResult`). It takes a `sourceId` because
the host runs whole documents through the session too, and an error should name the file it came
from.

### What must *not* happen between statements

Three things a one-shot run does are deliberately absent, and only happen in `Clear()` or on disposal:

- **`JG.Reset()`** — figures have to survive. `figure(1)` at the prompt must keep meaning the window
  it already opened, and a script that plots into figure 1 must hit that same window.
- **Releasing packed buffers** — live variables still point at them. This is the real cost of the
  decision: a long session accumulates buffers until `clear`, where before every run freed the last
  one's. That is the price of a workspace, and it is stated here so nobody "fixes" it later by
  reinstating the per-run disposal.
- **Rebuilding the built-in scope** — it is created once, along with the interpreter and the `run()`
  include builtin.

The interpreter gained `BeginStatement(token)`: a fresh cancellation token per statement (each prompt
gets its own Stop) and a fresh step budget (the runaway-statement limit must not accumulate over a
session alive for hours). It is safe because a session executes one statement at a time and the
per-chunk cancel check reads the field rather than capturing the token.

### `clear` is two things

`IScriptSession.Clear()` — the host's **Clear Workspace** command — rebuilds everything: buffers
released, `JG.Reset()`, a new scope. The `clear` *builtin* runs mid-statement, where rebuilding would
orphan the interpreter's own reference to the environment, so it empties the scope **in place**
(`JgsEnvironment.RetainOnly`) and leaves figures alone. That split matches MATLAB, where `clear`
clears variables and `close all` closes figures.

`clear` is in `JgsBuiltinCatalog` and `BuiltinNames` alongside `run`: neither is seeded by
`CreateGlobals` (one needs the interpreter, the other the session), but editors should still know
them.

### F5 runs inside the session — unless there is a breakpoint

Per the user's decision, the console and the Run button share one workspace. `RunActiveAsync` picks
one of three paths, in order:

1. **A breakpoint is set anywhere** → the debugger, which keeps its own fresh environment. A debug
   run therefore does *not* see console variables, and the console says so when it starts.
2. **The engine offers a session** → run the document through it, so the script and the prompt share
   a workspace.
3. **Otherwise** → a plain one-shot run.

Attaching the debugger unconditionally (as before) would have meant an ordinary F5 never shared the
workspace, defeating the point. Attaching the debugger *to* a live session is a much larger change —
the pause gate assumes it owns the interpreter thread — and is explicitly out of scope. Pause (Break
All) remains the escape hatch for a run that turns out to need stopping.

### C#

`CSharpReplSession` holds Roslyn's `ScriptState<object>` and continues it with `ContinueWithAsync`,
the scripting API's own REPL primitive. Two consequences are inherent and documented rather than
worked around: the first statement pays a ~1 s compile warm-up, and **cancellation only takes effect
between statements** — a C# `while (true) {}` at the prompt cannot be interrupted, because the running
code is ordinary IL with no cooperative check, unlike JGS whose interpreter we own.

A statement that fails to compile leaves the state untouched, so a typo cannot destroy a workspace.
The projection of `state.Variables` was widened to treat `int`/`long`/`float` as values — `var n = 3;`
produces an `int`, and without it the most ordinary C# number in existence showed as an opaque object.

### Python runs out of process

The console launches `python -u -X utf8 python/jgraph_console.py` and speaks **newline-delimited
JSON** over stdin/stdout. Framing is one message per line: JSON never contains a raw newline, so a
line is a complete message.

| Direction | Message |
|---|---|
| host → child | `{"id":N,"op":"exec","code":…}` · `{"op":"vars"}` · `{"op":"shutdown"}` |
| child → host | `{"type":"ready"}` · `{"id":N,"type":"out"\|"err","text":…}` · `{"id":N,"type":"done","ok":…,"line":…,"exit":…}` |
| child → host | `{"type":"call","seq":M,"fn":"plot","args":[…]}` → host replies `{"type":"return","seq":M,…}` |
| child → host | `{"type":"vars","items":[{name,type,repr,data}]}` |

Out of process, rather than the in-process pythonnet the *script* path uses, for two reasons.
**Cancellation actually works**: an interrupted statement kills the child, which is the only reliable
way to stop arbitrary CPython on Windows — `GenerateConsoleCtrlEvent` across process groups is not
worth building on. And a segfaulting C extension takes down a child instead of JGraph.

The cost, stated plainly: **the two Python paths cannot share state.** A variable created at the
prompt is invisible to a `.py` file run with F5, and vice versa. Migrating the script path onto this
protocol is a later milestone; the protocol is deliberately capable enough to carry it.

Implementation notes that are load-bearing:

- stdout **is** the protocol channel, so the child captures the real stdout at import time and
  redirects `sys.stdout`/`sys.stderr` into `out`/`err` messages. A line the host cannot parse is
  printed rather than dropped — it is usually a C extension writing to fd 1.
- Both pipes are drained on their own threads. A child that fills an undrained pipe blocks forever,
  which looks exactly like a hung statement.
- Statements compile in `'single'` mode so a bare expression echoes, falling back to `'exec'` for a
  multi-statement paste (the same trade the standard Python REPL makes).
- `GetVariables()` is synchronous on the interface, so the session requests a snapshot right after
  each statement and caches it — a blocking cross-process round trip from the UI thread would be a
  much worse design than a one-statement-stale cache that is never actually stale.
- Only **plotting verbs** are proxied to the host (`PythonHostBridge`). Anything that would hand a
  live object back across the boundary — a `Table` from `readcsv`, a figure handle — is absent: Python
  has its own readers, and a cross-process handle table is not worth inventing until something needs
  one. An unknown verb is reported as an error so the user sees a Python exception naming the
  function, not a silent no-op.
- `PythonRuntimeInfo` gained `Executable` (`sys.executable`). The DLL it already carried is only
  usable by the embedded runtime.

### The console panel stayed in the application

The plan put a reusable `ConsolePanel` in `JGraph.Controls`. It is in `JGraph.Application` instead:
the prompt needs the engine list, the run-state model, the workspace path resolver and the figure
bridge, none of which Controls can reference. What is genuinely reusable — the session seam, the
interpreters, the Python protocol — is already in `JGraph.Scripting`, which is where the testable
value is.

The existing coalesced scrollback is kept as-is, budgets included, and script output still lands in
it: one Command Window, one transcript. `StartupStatement.Resolve` is reused, so `analysis.jgs` at
the prompt runs that file while `disp(1)` runs as source — the disambiguation `-r` already does.

### Values with a table shape

`ScriptValueGrid` (`kind`, column names, rows of text) is how a matrix, cell array or struct reaches
the Data Viewer. The engine flattens to text on its side, so `JgsValue` stays internal and the host
grids anything without knowing the value model. `TableGridAdapter.ForGrid` consumes it. Bounded by
`ScriptValueGrid.MaxCells`: a grid is for reading, and a 5000×5000 matrix would cost 25 million
formatted strings to show something nobody can look at.

Rows are read with `ElementAt`/`ArrayLength`, never `AsArray` — a numeric row is packed, and asking a
packed array for boxed elements throws by design. Ragged rows pad rather than fail; JGS arrays are not
required to be rectangular.

## Consequences

- **Memory**: see above. `clear`, **Clear Workspace**, and closing the window are the release points.
- **Sessions own a process.** `DisposeSessions()` in `OnClosed` is what stops an orphaned Python
  child; verified by launching, closing, and confirming exit code 0 with no `jgraph_console` process
  left.
- Creating a session calls `JG.Reset()` — a new session is a new workspace. With two sessions alive
  (JGS and C#, say) they share the one static figure registry, as every path in JGraph already does.
- A failed statement leaves the workspace as it stands. Whatever ran before the error keeps its
  effect, exactly as MATLAB does.
- The C# console auto-shows figures (`ShowUnshownFigures` after each statement) where the C# *script*
  path only shows on an explicit `show()`. That is a deliberate REPL affordance: `Plot(x, y)` at a
  prompt should open a window.

## Alternatives considered

- **Putting `CreateSession` on `IScriptEngine`.** Would force every engine to implement a REPL or
  throw. The capability-interface pattern was already established by `IJgsDebuggable`.
- **In-process Python for the console** (reusing the pythonnet path, so both share a namespace).
  Rejected: no working cancellation, and a native crash would take JGraph with it. The split
  namespace is the lesser cost, and it goes away when the script path migrates.
- **Killing the child on every statement** to guarantee isolation. That is not a session.
- **A `close all` builtin** to match `clear`. Needs a host callback to close the window, not just the
  registry entry; deferred. The window's close button and **Clear Workspace** cover it for now.
  *(Superseded: ADR 0038 adds that callback along with `close`, `clf`, `gcf`, and `gca`.)*
- **`clc`.** Clearing the scrollback is a host concern, and adding `Clear()` to `IScriptOutput` would
  change an interface with several implementations for a cosmetic verb. Deferred.
  *(Superseded: ADR 0037 adds it as a default interface method.)*

## Testing

The session model, the C# session, the JSON protocol and the grid projection are all net8.0 and
covered by the standard gate. The live Python child round trip needs a real CPython, so it sits behind
the `!~Python` filter like every other Python test — but the protocol codec and `PythonHostBridge`
were deliberately named to *avoid* that filter, because they need no interpreter and must not be
skipped.
