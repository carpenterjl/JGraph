# ADR 0020 — Script-managed figure windows, figure handles, and figure file builtins

Date: 2026-07-17
Status: Accepted

## Context

Scripts could build any figure but had nowhere to put more than one: `figure()` only swapped the
static `JG` current-figure, `show()` overwrote the single main-window figure through a
`Action<FigureModel>` seam, and there was no way to save, reload, or image-export a figure from a
script. The goal: MATLAB's model — numbered figures, each in its own fully capable window, reused
across runs — plus `savefigure`/`loadfigure`/`exportfigure`.

## Decisions

### 1. `JG` gains a numbered-figure registry, not an instance redesign

The static facade (ADR 0012's one-script-at-a-time model, unchanged) now keeps a
`number → FigureModel` dictionary: `Figure()` takes the lowest free number, `Figure(n)`
selects-or-creates (returning to a figure also restores its last axes as current, so interleaved
plotting works), `RegisterFigure` adopts externally loaded figures, and `Reset()` — still called at
every run start — clears the registry, which is exactly why re-runs land on the same numbers.
Numbers are 1-based. C#/Python scripts get the same numbering through the same facade with no
changes. The JGS `figure()` builtin now returns the handle (a plain number — no new value kind),
`figure(n)` selects, and `show(fig)` shows a specific figure; `show()` stays explicit — no
auto-show at run end.

### 2. The show seam carries the figure number; the app keys windows on it

`ScriptContext.ShowFigure` became `Action<int, FigureModel>`. On the app side a new singleton
`IFigureWindowService`/`FigureWindowService` holds a number-keyed map of DI-minted
`FigureWindow`/`FigureViewModel` pairs (both were already transient): first show of a number
creates a window titled "Figure n" (the script figure replaces the view model's sample figure
*before* `Show()`, so nothing flashes), later shows swap content in place — re-running a script
updates its windows rather than spawning more — and closing a window evicts it for clean
recreation. Every script window is the full figure window: pan/zoom, edit mode, inspector,
browser, export, themes. **The main window is fully decoupled from script output**:
`IScriptingService.OpenEditor()` lost its figure callback and `FigureViewModel` no longer swaps
in script figures — the intended UX change. Double-clicking a `.graph` in the workspace tree now
also opens a numbered window (via `RegisterFigure`) instead of replacing the main figure.

### 3. Figure files are host callbacks, not new project references

`savefigure(path, fig?)`, `loadfigure(path)` (registers, becomes current, returns the handle),
and `exportfigure(path, fig?)` (png/jpg/bmp/tiff/svg/pdf by extension) reach disk through a new
`IScriptFigureFiles` on `ScriptContext` (null → a clear "not supported by this host" error),
implemented in the app by `AppScriptFigureFiles` over `GraphFormat` and `FigureExporter`. This
keeps JGraph.Scripting free of Serialization/Export references — consistent with the
`ShowFigure`/`ResolvePath` seam pattern and ADR 0012's auditable-IO stance. Paths resolve through
the workspace resolver, so `savefigure("run.graph")` lands next to the script and appears in the
Current Folder browser. Export uses the exporter's default theme; a themed export can load the
`.graph` into a window and use its Export dialog. The same verbs exist for C#/Python as
`JGraphScriptGlobals` methods.

## Consequences

- 20 new tests: registry numbering/reuse/reset, handle returns and per-number show routing,
  interleaved plotting, re-run number stability, save→load round-trips (loaded figure becomes
  current and accepts more plots), decodable PNG + SVG exports, unknown-handle/missing-file/no-host
  errors, and a full E2E (CSV → two figures → show both → save/export → reload → show).
- The `ScriptContext.ShowFigure` signature change touched every test that builds a context —
  mechanical, compiler-guided.
- Script figure windows keep the app alive until closed (standard WPF last-window shutdown);
  `IFigureWindowService.CloseAll()` exists for hosts that want bulk cleanup.
