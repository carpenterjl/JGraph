# ADR 0014 — Script workspace, docking shell, and Python runtime paths

Date: 2026-07-16
Status: Accepted

## Context

M11/M12 delivered three script engines behind a single-editor `ScriptWindow`. The next arc raises the
scripting experience to MATLAB parity: a workspace folder whose data files scripts find by bare name,
a multi-pane scripting window (files, tabs, console, variables), script composition across files, and
a debugger (M14–M15). Separately, `import numpy` failed in the Python engine even when numpy was
installed, because the embedded interpreter never learned the probed Python's home or search paths.

## Decisions

### 1. A UI-free `ScriptWorkspace` with one resolver seam

`JGraph.Scripting/Workspace/ScriptWorkspace` owns the root folder: enumeration (tree + scripts),
a debounced `FileSystemWatcher`, and `Resolve(path, currentScriptDirectory)` with the probe order
**absolute → script's own folder → workspace root → original**. `ScriptContext` gained exactly one
member — `Func<string, string>? ResolvePath` — and `JGraphScriptGlobals.Resolve` consults it before
the old `WorkingDirectory` fallback. Every file access a script makes (`readcsv`/`readxlsx`/
`readtable`, and JGS `run()`) flows through that single seam, so "data files discoverable by bare
filename" is implemented in one place and the engines needed no per-engine work.

### 2. Cross-file composition via a `run()` builtin, not a module system

JGS gained `run(path)`: resolve like the table readers, parse, and execute **into the global scope**
(functions hoisted first) — MATLAB script semantics. A cycle guard fails circular includes with a
clear error. No auto-loading of workspace scripts: implicit loading changes program meaning silently
and would complicate the debugger's live-edit story. `run()` is defined by the shared `JgsRunner`
(not `JgsBuiltins`) because it needs the interpreter instance; `JgsRunner` is now the single body
both the plain engine and the future debug session execute, so their behavior cannot drift.

### 3. Post-run variable snapshots on the engine seam

`ScriptRunResult` gained `Variables` — a list of `ScriptVariable(Name, Type, DisplayValue, RawValue)`
projections (default empty, so `IScriptEngine` implementors are unaffected). JGS snapshots its global
environment (untouched builtins hidden; rebindings shown); C# projects Roslyn's `ScriptState.Variables`;
Python enumerates the scope dict, skipping dunders/modules/callables. Snapshots are wrapped so they can
never fail an otherwise-successful run. `RawValue` carries `double[]`/`Table`/scalars for the future
data viewer; functions project as null. The debugger (M14) will reuse the same projection for its
live variables panel.

### 4. AvalonDock workspace window, docking confined to the Application layer

`ScriptWorkspaceWindow` (Dirkster.AvalonDock 4.72.1 + VS2013 theme) replaces `ScriptWindow` behind the
unchanged `IScriptingService`: a document pane of editor tabs (language by file extension), and
anchorable Files / Console / Variables panes. The docking dependency lives **only** in
`JGraph.Application`; `JGraph.Controls`' `ScriptEditorControl` slimmed to a pure editing surface
(AvalonEdit + highlighting) ready to grow the breakpoint margin and completion. The UI-free state —
extension→language mapping, dirty tracking, and the one-run-at-a-time command machine — lives in
`ScriptSessionModel`/`ScriptDocumentModel` in `JGraph.Scripting` so net8.0 tests cover it.

### 5. Persistence in the sanctioned STJ home, forgiving by design

Workspace state (last root, open files, active file, breakpoints per file, dock-layout XML) is a
versioned JSON document, `ScriptWorkspaceStateFormat` in `JGraph.Serialization` — the only project
allowed System.Text.Json (ADR 0009 discipline). The Application-side `WorkspaceStateService` owns the
`%AppData%\JGraph\workspace.json` path. Loading is deliberately forgiving: corrupt, mistagged, or
newer-versioned state yields null and the window starts fresh — losing a layout must never break the
app. Breakpoints are round-tripped now so the M14 debugger inherits persistence for free.

### 6. Python runtime: probe the user's `python`, propagate home and paths, skip Store Python

`PythonLocator` now returns `PythonRuntimeInfo(Dll, Home, SearchPaths)`: the DLL still derives from
`sys.base_prefix` (venvs do not copy it), but `Home = sys.prefix` (venv-aware) and `SearchPaths` is
the probed interpreter's real `sys.path`. The engine sets `PythonEngine.PythonHome` before
`Initialize()` and, belt-and-braces against pythonnet path quirks, prepends any missing search paths
in the per-run scope setup — this is what makes `import numpy` work. Two probe-order decisions:
`python` is tried **before** `py -3` (PATH — and an activated venv — is what the user means), and
Microsoft Store Python is skipped entirely (pythonnet cannot bind its exports; a granted
`PYTHONNET_PYDLL` override is enriched only when a probe corroborates the same DLL).

## Consequences

- Bare-name file access works identically in all three languages, and `run()` gives JGS multi-file
  programs — both prerequisites the M14 debugger builds on (cross-file step-in).
- The window special-cases nothing per language except availability; adding debug commands in M14 is
  additive (the session model grows a `Paused` state).
- Known limits: one script runs at a time (enforced by `ScriptSessionModel`); unsaved documents are
  not persisted across sessions; the Store-Python skip means users with only Store Python fall back
  to "runtime not found" guidance rather than a broken embed.
