# ADR 0012: Scripting hosts (C# and Python)

- Status: Accepted
- Date: 2026-07-16

## Context

Milestone 11 lets users drive JGraph from a script instead of compiled code: parse their data and render it
with the full plotting API exposed, in **C#** and in **Python**, from an in-app editor. This is the second
step of the data-and-scripting arc (M10 added data import; M12 will add a custom in-app language). It
introduces the first new third-party dependency family since SkiaSharp — a C# scripting engine and a real
Python runtime — so the boundaries need to be deliberate.

## Decision

1. **A new project, `JGraph.Scripting`** (net8.0, **WPF-free**). It references the framework libraries a
   script needs — `Core`, `Math`, `Signal`, `Data`, `Objects`, and `Api` — and adds two packages:
   `Microsoft.CodeAnalysis.CSharp.Scripting` (Roslyn) and `pythonnet`. Keeping it free of WPF means the
   engines are unit-testable headlessly and reusable outside the app. New one-way edges:
   `Scripting → {Core, Math, Signal, Data, Objects, Api}`, `Controls → Scripting`-free (the editor is
   engine-agnostic), and `Application → Scripting`.

2. **One language-agnostic seam, `IScriptEngine`** (`Language`, `IsAvailable`,
   `RunAsync(code, ScriptContext, CancellationToken) → ScriptRunResult`). Engines **never throw for a
   script-level failure** — syntax errors, runtime exceptions, and a missing runtime all come back as a
   failed `ScriptRunResult` with mapped `ScriptDiagnostic`s (1-based line/column). A host picks an engine
   by `Language` and streams results to its console. A third engine (M12's DSL) drops in with no other
   change.

3. **Scripts drive the existing `JG` facade — there is no new plotting surface.** The C# engine imports
   `JGraph.Api.JG` *statically* (so `Plot(...)`, `Title(...)`, `Legend(...)` need no qualifier) and the
   Python engine imports the `JG` type; both therefore expose every plot type, scale, subplot, and option
   the functional API already has. The only things a script needs beyond `JG` are host-backed: reading
   tables, printing to the console, and displaying a finished figure. Those live on a small
   `JGraphScriptGlobals` object — the C# engine surfaces it as the globals type, the Python engine injects
   the same helpers into the module scope. `readcsv`/`readxlsx`/`readtable` reuse the M10 `Table` readers;
   `show()` hands the current figure to the host; `print()` (and Python's redirected `stdout`) write to the
   console. Each run starts with `JG.Reset()`.

4. **The C# engine uses Roslyn scripting.** It compiles with a fixed set of references and imports, maps
   Roslyn diagnostics onto `ScriptDiagnostic`, surfaces warnings to the console, and returns a captured
   runtime exception as a failure rather than letting it escape. It runs on a background thread so a slow
   script never blocks the caller.

5. **The Python engine embeds real CPython via pythonnet** — not a reimplementation. A `PythonLocator`
   finds a CPython shared library from `PYTHONNET_PYDLL` or by probing the `python`/`py -3` launchers;
   when none is found `IsAvailable` is false and `RunAsync` returns a clear "Python runtime not found"
   message instead of failing hard (**graceful degradation**). CPython is initialised **once per process**
   (`PythonEngine.Initialize` + `BeginAllowThreads`, never shut down — pythonnet cannot re-initialise), and
   each run takes the GIL (`Py.GIL()`). The setup preamble (redirect `stdout`/`stderr`, load the JGraph
   assemblies, define the helpers) is executed **separately** from the user's code so traceback line
   numbers refer to the script the user actually wrote.

6. **Threading and the UI live in the host, not the engine.** A script builds a `FigureModel` (a WPF-free
   Core object) on a background thread; it is not attached to any renderer until `show()`, at which point
   the host marshals the figure onto the UI thread. The editor is a reusable, engine-agnostic
   `ScriptEditorControl` in `JGraph.Controls` (AvalonEdit + language selector + Run/Cancel + output
   console) that only raises `RunRequested`/`CancelRequested` and receives console text. `JGraph.Application`
   owns the engines and the run policy: `IScriptingService` opens a modeless `ScriptWindow` that runs the
   selected engine on a background task and marshals its output and figures onto the dispatcher — the same
   service-plus-thin-window shape as the Export/Open/Save/Import features.

## Consequences

- Scripting reuses the whole functional API unchanged: anything expressible as `JG.*` calls is scriptable
  in both languages, and the M10 table readers are the on-ramp for data. M12's custom language will
  implement `IScriptEngine` and reuse the same editor, service, and window.
- The app gains an **optional** native dependency (CPython). It is never required: the probe is lazy, the
  C# engine is always available, and Python degrades to a friendly message when absent. Python-dependent
  tests branch on `IsAvailable` so the suite is green on machines with or without Python.
- Because the `JG` facade is process-global static state, **one script runs at a time** (the editor
  disables Run while a script is running). Cancellation is cooperative and limited — it is honoured before
  a run starts and at `await` points, but cannot interrupt a tight CPU loop inside a running script.
- The engines materialise and display whole figures; there is no incremental/streaming figure update from
  a long-running script. That is consistent with the batch nature of the `JG` API and is out of scope here.
