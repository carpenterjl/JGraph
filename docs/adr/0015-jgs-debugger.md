# ADR 0015 — The JGS debugger: hook seam and session threading model

Date: 2026-07-16
Status: Accepted

## Context

M13 delivered the scripting workspace; the arc's centerpiece is an interactive debugger — breakpoints,
step in/over/out, pause, live variables, and a call stack — for scripts in the built-in JGS language.
Debugging is deliberately **JGS-only**: we own that interpreter, so a debugger is a seam away; the C#
and Python engines host external runtimes where real stepping would mean embedding a full debugger
protocol for marginal benefit.

## Decisions

### 1. Statement-level source identity

`Parser.Parse(code, sourceId)` stamps every **statement** with the source it came from (a file path,
or "" for unsaved code); the `run()` include parses with the resolved file path. All statements flow
through one point in the parser, so the stamp is a single line of code. Expressions carry no source
identity — breakpoints, the current-line marker, and stack frames are statement-granular. This is what
makes multi-file programs debuggable: a breakpoint in a `run()`-included file hits, and the UI opens
the right tab from `Paused.Location.SourceId`.

### 2. An internal hook seam with a zero-cost null path

The interpreter gained one optional constructor argument, an internal `IJgsDebugHook`:
`BeforeStatement(block, index, env, callDepth) → int?` before every statement (the return value is a
jump index, reserved for M15's set-next-statement), and `EnterFunction`/`ExitFunction` bracketing user
calls (try/finally, so unwinding never corrupts the depth). `ExecuteBlock` keeps its original
allocation-free loop when the hook is null — plain runs pay one null check per statement. The hooked
path wraps the statement list in a `BlockExecution` cursor whose `Replace` is M15's live-edit seam.
The top level now runs through the same block executor (`Run` delegates to `ExecuteBlock`), so
top-level statements are debuggable too; re-executing a hoisted `fn` declaration just re-creates the
same binding.

### 3. A public session that blocks the interpreter thread

`JgsScriptEngine.CreateDebugSession()` returns a `JgsDebugSession` (one script, one run) that
implements the hook privately. The engine seam (`IScriptEngine`) is untouched.

- **Pausing** blocks the interpreter thread on a `ManualResetEventSlim` inside `BeforeStatement`.
  `Paused`/`Resumed` are raised on the interpreter thread; the UI marshals with `BeginInvoke` (never
  a blocking `Invoke` — the interpreter must reach its gate without waiting on the UI).
- **Inspection safety is the pause itself**: while `IsPaused`, the interpreter thread is blocked, so
  reading environments from the UI thread is race-free. `GetCallStack`/`GetVariables` throw when not
  paused. That single invariant is the whole synchronization story for inspection.
- **Stepping is pure depth comparison.** The hook fires exactly once per statement execution, so:
  StepIn = pause at the very next statement; StepOver = the next at or above the starting depth (calls
  run to completion); StepOut = the next above it. No line comparison — which is what makes stepping
  in loops correct (re-executing the same line is a new statement execution and pauses again).
  A location-differs rule was considered and rejected: it silently runs a whole loop when the body is
  one line. Known quirk, documented: `run()` includes execute at the caller's depth (it is a builtin,
  not a user function), so StepOver at a `run()` line steps *through* the included file — MATLAB-like
  script semantics.
- **Breakpoints** are a copy-on-write dictionary (`sourceId → lines`, case-insensitive paths on
  Windows) swapped under a lock and read lock-free by the interpreter — settable at any time,
  including mid-run.
- **Stop-while-paused needs no special case**: the gate waits on the run's cancellation token, so
  cancelling unwinds through the same `OperationCanceledException` path as the interpreter's
  cooperative cancellation, and `Resumed` fires from a finally so the UI always clears its markers.
- **Frames** are a list the session maintains from Enter/ExitFunction (name, function source, call
  site, local environment), mutated only on the interpreter thread. The projected stack is
  innermost-first; caller frames sit at their call sites; frame 0's variables come from the exact
  paused environment (nested block scopes included), caller frames from their function-local chain,
  the script frame from the globals. Variable projection reuses M13's `ScriptVariable` shape and hides
  builtin bindings the script never rebound.

### 4. UI: a custom gutter, one debug path for JGS

AvalonEdit ships no icon margin, so `BreakpointMargin` is a custom `AbstractMargin` (click toggles the
red dot, also renders the yellow current-statement arrow) plus a `CurrentLineRenderer`
(`IBackgroundRenderer`) for the line band — both in `JGraph.Controls`, dock-free. In the workspace
window, **JGS always runs under a debug session** (the hook's cost is irrelevant interactively, and
Pause/breakpoints then just work); C#/Python keep the plain path. F5 doubles as Run/Continue; F9
toggles a breakpoint; F10/F11/Shift+F11 step. Breakpoints ride the M13 persistence (already
round-tripped), and a layout saved by an older build gets any missing tool pane re-added after restore
(`EnsureKnownPane`) — found when the pre-M14 layout swallowed the new Call Stack pane.

## Consequences

- The debugger is fully testable UI-free: the test suite drives the public session lock-step
  (breakpoints, stepping across a `run()` file, pause-in-tight-loop, stop-while-paused, shadowing,
  frame scopes) with timeouts, and the plain-run path is regression-guarded by the untouched JGS suite.
- M15 plugs into reserved seams: `BeforeStatement`'s jump return (set next statement) and
  `BlockExecution.Replace` (live edit).
- One debug session per run, one run per session — enforced, keeping the threading story simple.
