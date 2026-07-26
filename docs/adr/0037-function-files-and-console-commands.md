# ADR 0037 — MATLAB function files, close-time safety, and console commands

## Status

Accepted (M34).

## Context

Running a real MATLAB function file — `function test1` followed by plotting, no terminating
`end` — parsed fine but did nothing visible: the whole file became one function definition that
was declared and never called, and the run reported "0 figure(s) displayed". MATLAB's rule is
that a file whose first non-comment token is `function` is a *function file*, and running it
invokes its main function. Separately, the editor could silently lose work (no Save As, no
unsaved-changes prompt on close, read-only save failures buried in the status bar), and the
console lacked `clc`, `dir`, and `path`.

## Decision

### Function files auto-invoke on *file* runs only

A program whose top-level statements are all function definitions is a function file;
`JgsRunner.InvokeMainIfFunctionFile` calls its **first** function with no arguments after the
definitions load. MATLAB dispatches on the file name, but with only buffer text available the
first function is the equivalent — and MATLAB itself runs the first function even when its name
disagrees with the file's.

The rule must never fire for console input: defining a function at the prompt only defines it.
F5 and the prompt share `JgsReplSession`, and `sourceId` cannot distinguish them (an unsaved
document runs with `""`), so the seam is explicit: `IScriptSession.ExecuteFileAsync`, a default
interface method that falls back to `ExecuteAsync` so the C# and Python sessions are untouched,
overridden only by `JgsReplSession`. One-shot `IScriptEngine.RunAsync` is by contract a
whole-file run, so `JgsRunner.Run` applies the rule unconditionally — which covers `-batch`,
`-r`, and debug runs (the debugger funnels through `JgsRunner.Run`, so breakpoints inside a
function file's body hit). Both dialects get the behaviour: a functions-only `.jgs` file had the
same dead-run trap. `run()` is unchanged — it is an include, not a file run.

### The editor asks before losing work

`SaveActive` was refactored into `TrySave` / `TrySaveAs` / `TryWriteDocument`, all returning
success, and every close path runs through one gate (`ConfirmDiscardOrSave`): a dirty tab —
or each dirty document at app close — asks Save / Don't Save / Cancel, and a failed or cancelled
save keeps the document open. A never-saved document says explicitly that its whole content will
be lost, because session restore only persists documents with a path. `_shutdownApproved` stops
the per-tab AvalonDock `Closing` handlers re-prompting during window teardown, and the script
`exit()`/`quit()` path pre-approves shutdown deliberately: a script that says exit means it, and
a batch run must never block on a dialog.

Saving onto a read-only file now asks: strip the attribute and save, divert to Save As for a
writable copy, or cancel. Save As itself (File menu, Ctrl+Shift+S) writes first and re-homes the
document — path, language, tab identity — only after the write succeeds.

### `clc`, `dir`, `path`

- **`clc`** is a `void Clear() {}` *default* method on `IScriptOutput`, so the null, batch, and
  test sinks need no code; only the console pane overrides it. The console's implementation
  drops the pending write buffer under the lock before queueing the UI clear, because a flush
  already queued on the dispatcher would otherwise resurrect text after the clear.
- **`dir`** returns a **cell array of names** (folders suffixed with the path separator, ordinal
  sort, empty cell for a missing folder), not MATLAB's struct array: builtins have no nargout
  plumbing, and the bare-name echo of the cell *is* the listing. This deliberately narrows the
  guide's "no arbitrary file access" doctrine to read-only directory *listing* through the same
  host resolution the table readers use — absolute paths list, exactly as `readcsv` reads them.
- **`path`** is display-only: it reports the working directory bare names resolve against.
  There is still no search path — resolution stays "script folder, then workspace root" — and
  `addpath`/`rmpath` join the unsupported-functions table with a message that says so, rather
  than pretending a path list exists.

## Consequences

- The user's original repro (`function test1` + `figure(1)`/`plot`/`title`, no `end`) shows its
  figure, from F5, the console's file-name shortcut, and `jgraph -batch` alike.
- A function file whose main function requires arguments fails with an ordinary arity/undefined
  diagnostic — the MATLAB "not enough input arguments" analogue.
- `d = dir('*.m')` gives script code the names; anything wanting sizes/dates per entry is a
  later slice (it needs multi-output builtins or a struct-array return).
- Closing anything dirty now always costs one dialog; scripts and batch runs are exempt by the
  `exit()` pre-approval.
