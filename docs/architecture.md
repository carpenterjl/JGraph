# JGraph Architecture

JGraph is a modular, extensible scientific graphing framework for .NET 8 / WPF. It recreates the
workflow of the MATLAB figure window while following modern MVVM and SOLID design. This document
describes the layering and the load-bearing design decisions. Point-in-time rationale for individual
decisions lives in [Architecture Decision Records](adr/).

## Layering

Projects are organized so that dependencies flow in one direction only (no cycles). Lower layers know
nothing about higher ones.

```
        Core  ◄─────────────────────────────────────────────┐
         ▲                                                   │
        Math  ◄──────────────────────────────┐               │
         ▲                                    │               │
      Rendering  ◄── Rendering.Skia           │               │
         ▲                                    │               │
      Objects  ◄──────── Interaction   Api ───┘               │
         ▲   └── Signal, Data, Serialization, Plugins, Scripting ▲ │
      Controls (WPF) ─────────┴──────────┘                    │
         ▲                                                    │
      Application (WPF)      Demo (WPF)      Tests / Benchmarks┘
```

| Project | Target | Responsibility |
| --- | --- | --- |
| `JGraph.Core` | net8.0 | Object model (`FigureModel` → `AxesModel` → `PlotObject`), primitives (`Point2D`, `Rect2D`, `Color`, `DataRange`), styles, data-series abstraction, invalidation/event system. No UI or graphics-engine dependency. |
| `JGraph.Math` | net8.0 | Numeric services: scale transforms, the data↔pixel `AxisTransform`, tick generation, min/max decimation. |
| `JGraph.Signal` | net8.0 | Signal-processing services (FFT, windows, amplitude spectrum, STFT spectrogram, transfer-function frequency response) for the engineering plots, plus the `Rf/` RF core (Touchstone reader, S/Z/Y/ABCD conversions, microstrip/stripline calculators). Pure numerics, BCL-only; a leaf like `JGraph.Math`. |
| `JGraph.Numerics` | net8.0 | Flat contiguous numeric storage for large datasets: the dual-strategy `NumericBuffer` (managed / `NativeMemory` / memory-mapped temp file, chosen by the RAM-aware `BufferAllocator`) and the chunked, cancellable `PackedMath` SIMD kernels over `TensorPrimitives`. The only project that compiles unsafe code. A leaf; consumed by `JGraph.Scripting`. |
| `JGraph.Imaging` | net8.0 | Image-processing core: the `ImageBuffer` value type (interleaved `[0,1]` samples backed by a `NumericBuffer`) and the codec-free algorithms — point ops, histograms, geometry, 2-D filters/kernels, edge detection and gradients, the Hough line transform, morphology, connected-component labeling and region measurement. Depends only on `JGraph.Numerics`. |
| `JGraph.Imaging.Codecs` | net8.0 | Image file decoding/encoding (PNG/JPEG/BMP) via SkiaSharp, bridging bytes to `ImageBuffer`. The only image project that touches a native codec; referenced directly by `JGraph.Scripting`. |
| `JGraph.Data` | net8.0 | Tabular data: an immutable column-oriented `Table`, delimited-text/xlsx/clipboard readers with type inference, and the UI-free import-wizard model. A `Core`-only, BCL-only leaf. |
| `JGraph.Rendering` | net8.0 | Rendering abstractions: `IRenderContext`, `RenderState`, `IDrawable`, and the layout engine. Contains no concrete graphics library. |
| `JGraph.Rendering.Skia` | net8.0 | Implements `IRenderContext` over SkiaSharp. |
| `JGraph.Export` | net8.0 | Raster (PNG/JPEG/BMP/TIFF) and vector (SVG/PDF) export through the shared renderer. Part of the Skia backend family (references SkiaSharp). |
| `JGraph.Serialization` | net8.0 | Reads/writes the versioned `.graph` document format (JSON, `System.Text.Json`) via an explicit DTO layer that mirrors the model. |
| `JGraph.Plugins` | net8.0 | Plugin discovery/registration: an `IPlugin`/`PluginRegistry`/`PluginLoader` catalog of themes and colormaps, plus the built-in standard library (Light/Dark/Presentation/IEEE). A `Core`-only leaf. |
| `JGraph.Objects` | net8.0 | Concrete plot objects (line, scatter, bar, stem, histogram, error bar, image/heatmap) and their drawing logic. |
| `JGraph.Interaction` | net8.0 | UI-agnostic interaction modes (zoom, pan, …) driven by abstract input events. |
| `JGraph.Api` | net8.0 | MATLAB-like functional facade (`JG.Plot`, `JG.Title`, …). |
| `JGraph.Scripting` | net8.0 | Scripting hosts: the `IScriptEngine` seam, a Roslyn C# engine, a pythonnet (CPython) engine, and the built-in **JGS** language (a self-contained lexer/parser/interpreter under `Jgs/`); the `JGraphScriptGlobals` bridge drives the `JG` API and the `Table` readers. Also the UI-free `Workspace/` layer: `ScriptWorkspace` (folder enumeration, watcher, bare-filename resolution) and the session/document state models behind the scripting window. WPF-free, no dependency for JGS. |
| `JGraph.Controls` | net8.0-windows | WPF `FigureControl` hosting the Skia surface, the WPF→interaction input adapter, and the AvalonEdit-based `ScriptEditorControl`. |
| `JGraph.Application` | net8.0-windows | MVVM application shell, figure window, DI composition root. |
| `JGraph.Demo` | net8.0-windows | Gallery exercising both APIs. |
| `JGraph.Tests` | net8.0 | Unit tests. |
| `JGraph.Benchmarks` | net8.0 | Performance benchmarks (decimation, packed elementwise math, end-to-end JGS runs, FFT, hover hit-testing, `.graph` save/load). |

## Core principles

- **One object model, two APIs.** The object-oriented API (`figure.AddAxes()`, `axes.AddLine(x, y)`)
  and the MATLAB-like API (`JG.Plot(x, y, "r--")`) both manipulate the same `FigureModel` tree.
- **Rendering is a seam, not a dependency.** All drawing goes through `IRenderContext`. The model and
  the figure renderer never reference SkiaSharp or WPF, so a GPU, SVG, or PDF backend is a new
  `IRenderContext` implementation with no architectural change. See [ADR 0001](adr/0001-rendering-backend.md).
- **Data flows one way.** Model → renderer → `IRenderContext`. Interaction mutates the model only;
  rendering never reads UI or input state.
- **Change is observable and coalesced.** Every `GraphObject` raises a bubbling `Invalidated` event
  tagged with an `InvalidationKind` (Render < Layout < Data < Structure) so a surface repaints only
  what is needed. See [ADR 0002](adr/0002-object-model.md).
- **Performance is designed in.** Series data lives behind `IDataSeries`; array-backed sources expose
  spans so `MinMaxDecimator` can reduce millions of points to a per-pixel envelope before drawing.
  Since M22, JGS script arrays are *packed* — flat `NumericBuffer` storage (managed, native, or
  SSD-mapped by available RAM) with SIMD elementwise kernels — hover hit-testing binary-searches
  ascending series, and large `.graph` series persist as base64 blocks. See
  [ADR 0026](adr/0026-packed-numeric-arrays-and-large-dataset-performance.md).

## Rendering pipeline (target shape)

1. A WPF `FigureControl` hosts a Skia surface and subscribes to the `FigureModel.Invalidated` event.
2. On invalidation it requests a repaint; the paint callback wraps the Skia canvas in a
   `SkiaRenderContext` (an `IRenderContext`).
3. The `FigureRenderer` clears the background, and for each `AxesModel`: recomputes data bounds and
   auto-scaled ranges, computes the plot rectangle via the `LayoutEngine`, builds an `AxisTransform`,
   draws grid + axes chrome, clips to the plot area, and invokes `IDrawable.Render` on each plot.
4. Concrete plots (in `JGraph.Objects`) map their data to pixels through the `RenderState.Mapper` and
   issue backend-independent draw calls.

## Interaction pipeline

Input flows one way and never touches rendering internals:

1. The WPF `FigureControl` translates mouse/keyboard events into UI-independent `PointerEventArgs` /
   `WheelEventArgs` / `KeyEventArgs` and forwards them to an `InteractionController`.
2. The controller dispatches to the active `IInteractionMode` (pan, rectangle-zoom, data-cursor) and
   itself handles wheel zoom about the cursor.
3. Modes read the last paint's geometry through `IInteractionSurface` (implemented by the control from
   the renderer's `FigureRenderResult`) and mutate axis ranges via the pure, scale-correct
   `Navigation` math.
4. Each gesture snapshots the axes view state before/after and pushes one `AxesViewChangeAction` onto
   the shared `UndoStack`, so navigation is undoable atomically.

See [ADR 0004](adr/0004-interaction-system.md).

## Editing pipeline

Editing reuses the same seams instead of adding new ones:

1. **Annotations** (`TextAnnotation`, `ArrowAnnotation`, `RectangleAnnotation`, `EllipseAnnotation`
   in `JGraph.Objects`) derive from `AnnotationObject` (Core) and implement `IDrawable`, exactly like
   plots. They live in two spaces: `AxesModel.Annotations` (data coordinates — drawn over the plots,
   clipped, following zoom/pan) and `FigureModel.Annotations` (normalized [0, 1] figure coordinates —
   drawn last, pinned to the window). Geometry is a uniform anchor-point list, so moving and undo
   snapshots are type-independent; each annotation records its painted pixel bounds for hit-testing.
2. **Selection** is a single shared `SelectionManager` on the `InteractionController`. The `EditMode`
   sets it from clicks (annotations, then plots, then the axes), the plot browser sets it from the
   tree, and the property inspector displays whatever it holds. The selection highlight is drawn by
   the control's overlay, never by the objects themselves.
3. **Editing is undoable** on the same `UndoStack` as navigation: `PropertyChangeAction` (inspector
   and visibility edits; mergeable for continuous gestures via `PushOrMerge`), `MoveAnnotationAction`
   (one per drag), and `RemoveAnnotationAction` (delete + undo re-insert). Plot creation/removal is
   deliberately not undoable.
4. **The property inspector** is reflection-driven: the UI-free `EditablePropertyFactory`
   (`JGraph.Interaction.Editing`) turns ComponentModel attributes (`[Category]`, `[DisplayName]`,
   `[Browsable(false)]`) into typed editor descriptors with culture-aware parsing; the WPF
   `PropertyInspectorControl` and `PlotBrowserControl` (in `JGraph.Controls`) are thin views over
   those descriptors and the model tree.

See [ADR 0005](adr/0005-editing-and-annotations.md).

## Export pipeline

Export is the rendering seam paying off: `JGraph.Export` runs the same `FigureRenderer` against
different Skia canvases — a bitmap for PNG/JPEG/BMP/TIFF (with a supersampling `Scale` for print
quality), `SKSvgCanvas` for SVG, and an `SKDocument` page for PDF (sized in points so the physical
print size is exact). SVG and PDF contain real vector paths and text. BMP and TIFF are written by
small built-in encoders (Skia has none), and dashed strokes are flattened into segments for SVG
(Skia's SVG backend drops dash path effects). `FigureClipboard` (WPF layer) puts the same PNG
pipeline's output on the clipboard, and the figure window's Export dialog drives it all through an
`IFigureExportService`. Exports never mutate the figure model. See [ADR 0006](adr/0006-export.md).

## Plot types, subplots, and scales

Milestone 6 widened the plotting surface while reusing every existing seam:

1. **More plot types.** `StemPlot`, `HistogramPlot`, `ErrorBarPlot`, and `ImagePlot` (heatmap) are
   ordinary `PlotObject`/`IDrawable` implementations in `JGraph.Objects`, each with an
   `AxesExtensions` method and a `JG` facade call. They report data bounds that include their
   baselines/whiskers so auto-scaling never clips them. Heatmaps introduced the one new rendering
   primitive of the milestone — `IRenderContext.DrawImage` — which blits a colormapped pixel tile
   (built once via a `Colormap`) into a data-space rectangle; vector exports embed it as a raster
   region.
2. **Subplots.** Because an `AxesModel` occupies a `NormalizedBounds` fraction of the figure, a
   subplot grid is pure geometry: `FigureModel.AddSubplot(rows, cols, index[, lastIndex])` (MATLAB
   row-major, 1-based, with a gutter and spanning) and `JG.Subplot`. Each panel keeps its own axes,
   scales, grid, and legend, and the renderer lays them out independently.
3. **Linked axes.** `AxisLinkGroup` (Core) keeps several axes' primary ranges synchronized: it
   unifies them to their union, disables auto-scale (as MATLAB `linkaxes` does), and mirrors later
   range changes through the bubbling invalidation event, guarded against feedback loops. It is
   model-only, so it works headlessly and with the UI-free interaction layer.
4. **Date/time and category scales.** Both map linearly to the axis, so only tick generation and
   labeling differ. Date/time values are OLE automation dates (see `DateTimeAxis`);
   `DateTimeTickGenerator` chooses a natural calendar/clock step and formats by resolution, and
   `CategoryTickGenerator` labels integer positions from an axis's category list. Rendering resolves
   the generator via the axis-aware `TickGenerators.For(AxisModel)`.

See [ADR 0007](adr/0007-plot-types-and-scales.md).

## Engineering plots

Milestone 7 added the engineering/scientific plot types (Bode, Nyquist, polar, Smith, spectrogram,
eye diagram) and the signal-processing math behind them, again without a new rendering primitive.

1. **A dedicated DSP library.** `JGraph.Signal` (a BCL-only leaf beside `JGraph.Math`) holds the
   `Fft` (radix-2 with a direct-DFT fallback), tapering `Window`s, the amplitude `Spectrum`, the STFT
   `Spectrogram`, and the `TransferFunction` frequency response. It knows nothing of the model or the
   renderer, so it is unit-tested in isolation and reused by the plot helpers.
2. **Polar and Smith are Cartesian underneath.** Rather than teach the renderer a polar coordinate
   system, polar and Smith data are converted to Cartesian before plotting ((θ, r) → (x, y);
   impedance z → reflection coefficient Γ). The circular grid is an ordinary `IDrawable`
   (`PolarGrid`, `SmithGrid`) that samples its rings and arcs through the normal coordinate mapper, so
   every existing pipeline (transform, decimation, export) applies unchanged.
3. **Equal aspect makes circles round.** `AxesModel.EqualAspect` shrinks the plot area to a centered
   square-per-unit rectangle so a data circle maps to a pixel circle, and `AxesModel.FrameVisible`
   lets the circular charts drop the rectangular frame. These are the only Core/renderer additions —
   no new `IRenderContext` member.
4. **The rest are compositions.** Bode is two stacked subplots on a shared logarithmic frequency
   axis; Nyquist is the H(jω) locus (both branches) with the critical (−1, 0) point marked on an
   equal-aspect axes; a spectrogram is an `ImagePlot` of the STFT magnitude. Fluent helpers
   (`AddBode`, `AddNyquist`, `AddSpectrogram`, `AddPolar`, `AddSmith`, `AddEyeDiagram`) and `JG`
   facade methods build them; only the eye diagram is a bespoke `PlotObject`. Logarithmic auto-scale
   padding is applied in decade space so log frequency axes fit their swept band cleanly.

See [ADR 0008](adr/0008-engineering-plots.md).

## Serialization

Milestone 8 makes figures persistent through a versioned `.graph` document format, without coupling the
model to serialization:

1. **A dedicated project and an explicit DTO layer.** `JGraph.Serialization` (referencing
   `JGraph.Objects`, using `System.Text.Json`) defines DTO records mirroring the model and a mapper
   between them. The on-disk shape is therefore a deliberate contract, decoupled from internal property
   names, and the model stays free of serialization attributes — the same seam discipline as the
   renderer and exporter.
2. **A single, versioned entry point.** `GraphFormat` writes `{ format, formatVersion, figure }` and
   reads it back, rejecting a wrong tag, a newer version, or inconsistent content with a
   `GraphFormatException`. Colors are hex, enums are names, non-finite data (line gaps) is preserved, and
   heterogeneous plots/annotations carry a `type` discriminator — so adding a type is a new DTO plus one
   mapper arm.
3. **Copy/paste reuses the format.** `FigureClipboard` puts a figure on the clipboard as both a PNG
   image and `.graph` JSON, and reads the JSON back; the figure window's Open/Save and Copy/Paste-figure
   commands run over an `IFigureDocumentService`, keeping the view model free of WPF.

See [ADR 0009](adr/0009-serialization.md).

## Plugins and themes

Milestone 9 opens the framework to outside extension and ships the last of the built-in look-and-feel:

1. **A registry of contributions.** `JGraph.Plugins` (a `Core`-only leaf) defines
   `IPlugin` — a `Name`, a `Version`, and a single `Configure(IPluginRegistry)` — and a `PluginRegistry`
   that is both the write side plugins register into (`AddTheme`, `AddColormap`) and the read side the
   app queries (`Themes`, `Colormaps`, `TryGetTheme`). Names are unique and order is preserved. The
   built-in `StandardLibraryPlugin` seeds the Light/Dark/Presentation/IEEE themes and the standard
   colormaps, and is the worked example of a plugin.
2. **Reflection-based discovery.** `PluginLoader` finds concrete `IPlugin` types in assemblies and can
   load `*.dll` files dropped into a plugins directory (via the default `AssemblyLoadContext`) before
   scanning them. Discovery is deterministic; a missing directory means "no plugins"; load/config
   failures surface as a `PluginException`. `PluginLoader.LoadDefault(dir)` is the startup entry point.
3. **Themes carry typography.** `ITheme` now includes a font family, per-role sizes (figure/axes title,
   axis label, tick label), and a bold-titles flag; `Theme.Apply` sets them alongside colors (Light/Dark
   keep the model defaults, so nothing regresses). **Presentation** is large, bold, and saturated for
   slides; **IEEE** is a compact Times New Roman face with faint gridlines for two-column papers.
4. **The app resolves themes through the registry.** The DI container registers the `PluginRegistry`
   from `LoadDefault`; the view model exposes `AvailableThemes` and a settable `CurrentTheme`; the
   toolbar theme selector is bound to them — so a plugin's theme appears in the menu with no app change.

See [ADR 0010](adr/0010-plugins-and-themes.md).

## Data import

Milestone 10 lets data enter from files and the clipboard, feeding the existing `IDataSeries` seam:

1. **A tabular data model.** `JGraph.Data` (a `Core`-only, BCL-only leaf) defines an immutable,
   column-oriented `Table` of typed columns — numbers (NaN = missing), dates (stored as OLE automation
   dates, so they plot straight onto a date axis), and text (whose distinct values form a category set).
   A table is a data *source* like `ArrayDataSeries`, not a `GraphObject`; `TableSeries` turns a column
   pair into an `IDataSeries`, sharing the backing array with zero copy for numeric columns.
2. **Readers with deterministic detection.** `DelimitedTextReader` is RFC 4180-aware and auto-detects the
   delimiter, header row, and number culture (each overridable via `ImportOptions`); `ClipboardTableParser`
   handles Excel-style tab-delimited paste; `XlsxReader` is a hand-rolled reader over
   `System.IO.Compression` + `System.Xml.Linq` that reads a worksheet's cached cell values (strings,
   numbers, booleans, and date-formatted numbers) with no formula evaluation or styling. Recoverable
   issues become warnings; only hard failures throw `ImportException`.
3. **Columns to plots, once.** `TablePlotBuilder` turns a `TablePlotSpec` (kind, X column, Y columns,
   optional error column) into plots — one per Y column, enabling the legend when there is more than one —
   and configures the axes for the X column's type (date or category). The same builder backs the
   table-aware fluent API (`axes.AddLine(table, "x", "y")`), the `JG` facade (`JG.ReadTable`,
   `JG.Plot(table, …)`), and the wizard.
4. **A UI-free wizard model.** `ImportWizardModel` owns source loading, re-parsing on option changes, the
   column mapping, the rules for which plot kinds a mapping allows, and build validation — all
   unit-tested. The WPF `ImportWizardWindow` and `DataImportService` (in `JGraph.Application`) are a thin
   view and dialog host, reached from the figure window's **Import Data…** button, mirroring the existing
   Open/Save/Export services.

See [ADR 0011](adr/0011-tabular-data-and-import.md).

## Scripting

Milestones 11 and 12 let users drive the framework from a script — in C#, Python, or the built-in JGS
language — reusing the whole functional API rather than exposing a new one:

1. **One engine seam.** `JGraph.Scripting` (net8.0, WPF-free) defines `IScriptEngine`
   (`Language`, `IsAvailable`, `RunAsync(code, ScriptContext, ct) → ScriptRunResult`). Engines report
   syntax errors, runtime exceptions, and a missing runtime as a failed result with 1-based
   `ScriptDiagnostic`s — never by throwing. A host selects an engine by language and streams its output
   to a console.
2. **Scripts drive `JG`.** There is no new plotting surface: the C# engine imports the static `JG`
   facade (so `Plot(...)`, `Title(...)` are top-level), and the Python engine imports the `JG` type — so
   every plot type, scale, and option the functional API has is scriptable in both languages. The few
   host-backed helpers a script needs — `readcsv`/`readxlsx`/`readtable` (the M10 `Table` readers),
   `print`, and `show` — live on a small `JGraphScriptGlobals` object the engines expose.
3. **Two engines.** `CSharpScriptEngine` compiles with Roslyn scripting, maps diagnostics with
   line/column, and runs on a background thread. `PythonScriptEngine` embeds real CPython through
   pythonnet: a `PythonLocator` finds the runtime (env var or launcher probe) and the engine degrades
   gracefully when none is present; CPython is initialised once per process and each run takes the GIL;
   `stdout`/`stderr` are redirected to the console and the setup preamble runs separately from the user's
   code so traceback line numbers line up.
4. **The UI lives in the host.** A script builds a WPF-free `FigureModel` on a background thread; the
   host marshals its output and its `show()` figures onto the dispatcher. The engine-agnostic
   `ScriptEditorControl` (AvalonEdit with per-language highlighting) sits in `JGraph.Controls` as a pure
   editing surface; `JGraph.Application` owns the engines and the modeless workspace window behind an
   `IScriptingService`, reached from the figure window's **Script…** button — the same
   service-plus-thin-window shape as import/export/open/save.
5. **A built-in language, JGS (M12).** `JgsScriptEngine` runs a small, hand-rolled, dependency-free
   language defined entirely under `JGraph.Scripting/Jgs/` (lexer → recursive-descent parser → tree-walking
   interpreter). It supports `let`/assignment, arrays, arithmetic/comparison/logical operators (numeric ops
   are element-wise over arrays), `if`/`while`/`for`, `fn` functions with closures and recursion, and
   indexing; its built-ins mirror the `JG` verbs and the `Table` readers, so a JGS script plots the same way
   a C# or Python script does. Because the interpreter is ours it is sandboxed by construction (the readers
   are its only IO) and interruptible even inside a tight loop (a step budget, a call-depth limit, and a
   cancellation check per statement). It slotted in as a third `IScriptEngine` with no host change beyond DI
   registration and a JGS syntax-highlighting definition in the editor.

6. **A MATLAB-style workspace (M13).** The scripting window is a docking workspace
   (`ScriptWorkspaceWindow`, AvalonDock — the docking dependency confined to `JGraph.Application`): a
   workspace folder's file tree, multi-tab editors whose language follows the file extension, a console
   pane, and a variables pane fed by the post-run snapshot every engine now returns on
   `ScriptRunResult.Variables`. The UI-free `ScriptWorkspace` resolves the file names scripts use
   (script's folder → workspace root) through a single `ScriptContext.ResolvePath` seam shared by the
   table readers and the JGS `run()` include builtin (cycle-guarded, executes into the global scope).
   Window state — last workspace, open files, breakpoints, dock layout — persists as versioned JSON via
   `JGraph.Serialization`. The Python engine now propagates the probed interpreter's home prefix and
   `sys.path` into the embedded runtime, so installed packages (numpy, …) import correctly; the probe
   prefers the user's `python` (PATH/venv) and skips the un-embeddable Microsoft Store Python.

7. **A JGS debugger (M14).** Because JGS is our own interpreter, it debugs like a first-class
   language: a breakpoint gutter, F5/continue, pause, step in/over/out (across files included with
   `run()` — every statement carries a `SourceId`), a live variables panel, and a call stack. The
   interpreter exposes one internal hook (`IJgsDebugHook`, called before each statement; a null hook
   costs a single null check, so plain runs stay full speed); the public `JgsDebugSession`
   (`JgsScriptEngine.CreateDebugSession()`) implements it, pausing by blocking the interpreter thread
   on a gate — which is also what makes variable inspection race-free while paused. Stepping is pure
   call-depth comparison; stop-while-paused rides the ordinary cancellation path. Debugging is
   deliberately JGS-only: the hosted C#/Python engines run plain.

8. **Debugger UX (M15).** The paused session is malleable: drag the execution arrow (or right-click
   the gutter) to set the next statement within the paused block — skipped statements never run,
   backwards jumps re-execute — and edit the code itself, applied on resume when the code that
   already ran is untouched (`AstEquals`, ignoring positions). Live edits mutate the parser's shared
   statement lists in place while the interpreter thread is blocked, so they reach later loop
   iterations and closures; edited functions not on the stack are refreshed (or re-hoisted when new
   or re-signatured), and incompatible edits change nothing and offer a restart. A MATLAB-style
   **Data Viewer** (the UI-free, paged `TableGridAdapter` in JGraph.Data under a virtualized grid in
   JGraph.Controls) opens tables and arrays from the Files tree or the Variables panel.

9. **Code completion (M16).** JGS gets the smart treatment because we own the language:
   `JgsBuiltinCatalog` describes every builtin once (parameters, summary, derived signature) and feeds
   completion, signature help, *and* the runtime-generated `.xshd` highlighting word lists — with a
   test pinning the catalog to the live registration, nothing can drift. The UI-free
   `JgsCompletionEngine` never parses (the parser throws on the first error and a mid-keystroke buffer
   is routinely broken): a tolerant lexer mode harvests `let`/loop bindings (offered below their
   declaration), `fn`s (offered anywhere — they hoist), and the innermost open call for signature help
   with the active argument counted by a bracket-stack walk. Other workspace `.jgs` files contribute
   their `fn`s (open tabs from live buffers, the rest from disk through a timestamp cache). C# and
   Python get curated word lists (keywords + reflected `JG` members). One WPF class
   (`CompletionSupport`) wires it all to AvalonEdit: Ctrl+Space and auto-trigger, parameter-placeholder
   insertion, and a caret-tracking signature tooltip whose bold active parameter advances on commas.

10. **Workspace browser & filename completion (M17).** The Files pane is a MATLAB-style Current
    Folder browser — address bar, Up button, double-click-a-folder (or its context menu) re-roots the
    workspace, and because the browsed folder *is* the workspace root, script path resolution and
    persistence follow for free. Files open by what they are (scripts → tabs, csv/tsv/xlsx → Data
    Viewer, `.graph` → a live figure in the main window, txt/md/json → plain non-runnable text tabs).
    Tool panes hide rather than close (AvalonDock keeps them in the layout), and the toolbar's View
    menu re-shows them. Inside the string argument of `readcsv`/`readxlsx`/`readtable`/`run`, the
    UI-free `PathCompletion` offers workspace file names (filtered by the function's accepted
    extensions, folders composing with `/`, rooted paths excluded) in all three languages; JGS also
    accepts MATLAB-style single-quoted strings.

11. **Data analysis in JGS (M18).** Comparisons (`< <= > >=` and `==`/`!=`) are element-wise over
    arrays, returning bool masks (`ids == "SN-1"` works on string columns), and indexing an array
    with an array gathers — through both `data[mask]` and MATLAB-style `data(mask)` (a scalar
    element, a length-checked bool mask, or an index array; strings gather to strings). Array
    truthiness became MATLAB's non-empty-and-all-true; bools count as 0/1 in arithmetic
    (`sum(mask)`). On top sits a 33-builtin stdlib — statistics (`std`, `variance`, `median`,
    `mode`, `percentile`, `cumsum`, `cumprod`, `diff`; NaN propagates, cleaning is explicit),
    array ops (`sort`, `unique`, `find`, `any`, `all`, `concat`, `slice`, `indexof`, `reverse`,
    `isnan`, `isequal`, `and`/`or`/`not`, `numel`), strings (`sprintf` with a fixed C-verb subset,
    `str`/`num`, `split`/`join`, case/trim/search helpers, polymorphic `contains`), and table
    inspection (`colnames`, `rowcount`, `textcolumn`) — with `readcsv(path, skiprows)` (and the
    other readers) skipping junk preamble rows via the existing `ImportOptions.SkipRows`.

12. **Script-managed figure windows (M19).** The `JG` facade keeps a MATLAB-style numbered figure
    registry (`figure()` returns a 1-based handle, `figure(n)` selects-or-creates, `Reset()` at run
    start clears it), the `ScriptContext.ShowFigure` seam carries the number, and the app's
    `FigureWindowService` opens one full `FigureWindow` per number — pan/zoom, edit mode,
    inspector, export, everything — reusing (content-swapping) the same window when a re-run shows
    the same number, and never touching the main window. `savefigure`/`loadfigure`/`exportfigure`
    reach `GraphFormat` and `FigureExporter` through the host-callback `IScriptFigureFiles`
    (Scripting still references neither project); loaded figures join the registry and behave like
    any other handle. Workspace `.graph` double-clicks open numbered windows through the same path.

13. **C-style expression syntax (M20a).** Assignment became a lowest-precedence, right-associative
    *expression* (`AssignExpr`/`IncDecExpr` replaced the old assignment statements): `+= -= *= /= %=`
    and prefix/postfix `++`/`--` work on variables and array elements with single evaluation of
    index targets, compound forms reuse the shared binary-operator dispatch (so `xs += 1`
    broadcasts), and `let` is still required for first bindings. The parser also allows newlines
    before a block's `{`, between a function name and its parameter list, and after `else`; and
    `let [X, Y] = expr` destructures an array into names (the consumer of `meshgrid`).

14. **Interactive 3D plotting (M20b).** 3D is a mode of `AxesModel` (`Is3D`, an owned `ZAxis`,
    `Azimuth`/`Elevation`); `Projection3D` (JGraph.Math) implements MATLAB's `view` camera as a pure
    normalized-cube axonometric projection, and `FigureRenderer`'s 3D branch draws the far box faces
    plus grid and dispatches plots implementing `I3DDrawable` — no render-context changes, surfaces
    are depth-sorted `DrawPolygon` quads (painter's algorithm). `SurfacePlot` covers
    surf/mesh/meshc; `ContourPlot` covers contour/contourf via `MarchingSquares` (per-cell band
    fills); `imagesc`/`pcolor` reuse `ImagePlot`; `ColorbarRenderer` legends the first
    `IColorMapped` plot. Dragging rotates (PanMode when `Is3D`), the wheel dollies X/Y/Z, and the
    camera rides the existing `AxesViewState` undo. JGS matrices are arrays of row arrays —
    elementwise operators and math builtins recurse into nested arrays, `meshgrid` returns `[X, Y]`,
    and 13 new builtins expose the verbs. `.graph` format version 2 adds the 3D axes fields and the
    surface/contour DTOs (v1 documents load unchanged).

15. **MATLAB-compatible surface (M21).** Semicolon echo suppression with `ans` and MATLAB-style
    console echo (an `echo` sink on the interpreter, wired by `JgsRunner`), inclusive colon ranges,
    1-based paren indexing with `end`, `:`, and slice/mask writes (brackets stay 0-based; `find`
    went 1-based to match), `for k = 2:n … end`/`elseif` blocks alongside braces, `~=`/`.*`/`^`
    operators, `[a; b]` rows/vertical concat, bare-builtin command form (`figure;`), automatic
    display of unshown figures after a run, and first-class complex numbers (`JgsType.Complex`
    boxing `System.Numerics.Complex`, normalizing zero-imaginary back to Number). `JGraph.Signal`
    gained Bluestein arbitrary-length FFT, `DigitalFilter` (filter/freqz), `IirDesign`
    (Butterworth), `FirDesign` (Parks–McClellan), and the hand-rolled `WaveFile` codec; scripts
    reach them through 14 dual-registered builtins plus the `IScriptAudio` playback seam on
    `ScriptContext` (app: `SoundPlayer` over an in-memory WAV; `pause` waits on the run's
    cancellation token). Two real MATLAB lab scripts run end to end as the acceptance tests.

16. **Figure-window QOL (M21).** The default tool is the new `PointerMode`: drag pans/rotates via
    the shared `PanDragGesture`, hovering near a data point shows a crosshair (dynamic mode
    cursor), and a click pins a persistent `DataTipAnnotation` — a real model object (pin
    coordinates + movable label anchor + leader line) that serializes (`.graph` v3), edits in the
    inspector, and rides `Add/Move/RemoveAnnotationAction` undo. `DataTipsMode` replaced the
    transient data cursor with a roving tip that replaces only its own last placement. The plot
    surface gained a right-click menu built from a UI-free `ContextMenuItem` model
    (`InteractionController.BuildContextMenu` + `IContextMenuSource`): zoom-constraint choices
    (`RectangleZoomMode.Constraint` — horizontal/vertical bands that restore the free axis
    exactly), data-tip deletion, and per-axes Restore View.

17. **Large-dataset performance (M22).** JGS numeric arrays are packed: `Type` stays `Array`, but
    homogeneous numeric data lives in a flat `NumericBuffer` (or planar `JgsPackedComplex` for
    spectra) instead of one heap object per element — 8 bytes per double instead of ~48. The
    `BufferAllocator` picks the backing per allocation (managed under 1M elements; `NativeMemory`
    while physical RAM has headroom; an SSD-backed delete-on-close mapped file beyond that, so big
    arrays degrade instead of OOM). Elementwise operators, comparisons, ranges, slices, and the hot
    builtins run as chunked `TensorPrimitives` SIMD kernels with a cancellation poll between chunks;
    a single wrapper per buffer preserves reference/aliasing semantics, and any write outside the
    numeric fast path demotes the array to boxed in place for every alias at once. `AsArray` throws
    on packed values so unmigrated code fails loudly; a parity suite runs a script corpus with
    packing forced on and off and demands byte-identical output. Hover hit-testing
    (`SeriesHitTester`) binary-searches ascending series instead of scanning every point, and
    `.graph` v4 stores large series as base64 double blocks with streamed save/load.

See [ADR 0012](adr/0012-scripting-hosts.md), [ADR 0013](adr/0013-custom-scripting-language.md),
[ADR 0014](adr/0014-script-workspace-and-docking-shell.md),
[ADR 0015](adr/0015-jgs-debugger.md),
[ADR 0016](adr/0016-set-next-statement-and-live-edit.md),
[ADR 0017](adr/0017-completion-and-signature-help.md),
[ADR 0018](adr/0018-workspace-browser-and-path-completion.md),
[ADR 0019](adr/0019-jgs-data-analysis-stdlib.md),
[ADR 0020](adr/0020-script-managed-figure-windows.md),
[ADR 0021](adr/0021-jgs-c-style-expression-semantics.md),
[ADR 0022](adr/0022-3d-plotting-over-the-2d-pipeline.md),
[ADR 0023](adr/0023-matlab-compatible-jgs-surface.md),
[ADR 0024](adr/0024-dsp-builtins-and-audio-seam.md),
[ADR 0025](adr/0025-pointer-mode-data-tips-context-menu.md), and
[ADR 0026](adr/0026-packed-numeric-arrays-and-large-dataset-performance.md); the
[data-import walkthrough](import-guide.md) and the `examples/` scripts show all three languages in use.

## Status

Implemented through Milestone 22 — a working, Matlab-like figure window you can edit, save, publish, extend, feed with imported data, and drive with scripts:

- **M1** object model, math services (transforms, ticks, decimation), rendering abstractions.
- **M2** SkiaSharp render context, `FigureRenderer` (chrome + plots), WPF `FigureControl`,
  line/scatter/bar plots with automatic decimation, light/dark themes, and both public APIs.
- **M3** modular interaction (wheel zoom, drag pan, rubber-band zoom, data cursor), navigation
  undo/redo, and an MVVM figure window (toolbar, status bar, DI composition root).
- **M4** editing: annotations (text/arrow/rectangle/ellipse in data or figure space), an Edit mode
  with click selection and drag-moving, property-edit/move/delete undo, the reflection-based
  property inspector, and the plot browser tree — all wired into the figure window as collapsible
  side panels.
- **M5** export: PNG/JPEG/BMP/TIFF raster (with print-quality supersampling), true-vector SVG and
  PDF, clipboard copy (Ctrl+C), and the figure window's Export dialog.
- **M6** plot types and layout: stem, histogram, error bar, and image/heatmap plots (with
  colormaps); a subplot grid; linked axes; and date/time and category scales.
- **M7** engineering plots: the `JGraph.Signal` DSP library (FFT, windows, spectrum, spectrogram,
  transfer functions) and Bode, Nyquist, polar, Smith, spectrogram, and eye-diagram plots, with
  equal-aspect axes for the circular charts.
- **M8** serialization: the versioned `.graph` document format (`JGraph.Serialization`), figure
  save/open in the window, and figure copy/paste through the clipboard.
- **M9** plugins and themes: `JGraph.Plugins` (an `IPlugin`/`PluginRegistry`/`PluginLoader` catalog of
  themes and colormaps, discovered from assemblies or a plugins folder), theme typography, the
  Presentation and IEEE presets, and a registry-driven theme selector in the app and demo.
- **M10** data import: `JGraph.Data` (an immutable column `Table` with delimited-text, xlsx, and
  clipboard readers and type inference), the table-aware fluent and `JG` APIs, and the figure window's
  **Import Data…** wizard for mapping columns onto plots.
- **M11** scripting: `JGraph.Scripting` (the `IScriptEngine` seam with a Roslyn C# engine and a pythonnet
  CPython engine, both driving the `JG` API), the reusable `ScriptEditorControl`, and the figure window's
  **Script…** editor for building figures in C# or Python.
- **M12** the built-in **JGS** language: a dependency-free lexer/parser/tree-walking interpreter (a third
  `IScriptEngine`) whose built-ins mirror the `JG` API, with vectorized array math, closures, sandboxing,
  and in-loop cancellation — plus the arc's deliverables (example scripts for all three languages and the
  [data-import walkthrough](import-guide.md)).
- **M13** the scripting workspace: a docking scripting window (file tree, multi-tab editors, console,
  variables panel), the `ScriptWorkspace` folder model with bare-filename resolution for script data
  files, the JGS `run()` include builtin, post-run variable snapshots from all three engines, persisted
  window/workspace state, and the Python engine fix that makes installed packages (numpy, …) importable.
- **M14** the JGS debugger: breakpoints (gutter + F9), pause, step in/over/out (F10/F11/Shift+F11,
  across `run()`-included files with the right tab opening automatically), a live variables panel and
  call stack while paused — built on an internal interpreter hook with a zero-cost null path and a
  public `JgsDebugSession` that blocks the interpreter thread to pause.
- **M15** debugger UX: set next statement (drag the execution arrow or right-click the gutter), live
  code edits while paused (applied on resume via in-place AST list mutation, with a precise
  compatibility rule and a restart offer when an edit can't apply), and the paged tabular **Data
  Viewer** for tables and arrays (Files tree, Variables panel, csv/xlsx).
- **M16** code completion: the `JgsBuiltinCatalog` single registry (feeding completion, signature
  help, and the runtime-generated JGS highlighting word lists, pinned to the interpreter by a sync
  test), the tolerant-lexing `JgsCompletionEngine` (buffer + cross-file workspace symbols, signature
  help with the active parameter tracked through nested calls), curated C#/Python word lists, and the
  AvalonEdit wiring (Ctrl+Space, auto-trigger, placeholder insertion, bold-active-parameter tooltip).
- **M17** workspace UX: the MATLAB-style Current Folder browser (address bar, Up, re-root by
  double-click/context menu), extension-aware file opening (`.graph` → live figure, text files →
  plain tabs), hide-not-close tool panes with a View menu to restore them, workspace filename
  completion inside file-function string arguments (all three languages), and single-quoted JGS
  strings.
- **M18** JGS data analysis: element-wise comparisons/equality producing bool masks, MATLAB-style
  logical indexing (`data(parameter > threshold)` and `data[mask]`, plus index-array gathers),
  MATLAB array truthiness, and a 33-builtin stdlib (statistics, array ops, `sprintf`/strings,
  table inspection) with junk-preamble skipping on the table readers (`readcsv(path, skiprows)`).
- **M19** script-managed figure windows: MATLAB-style numbered figure handles in `JG`
  (`figure()`/`figure(n)`), a number-carrying show seam, the app's `FigureWindowService` opening one
  full figure window per handle (reused across re-runs; main window untouched), and the
  `savefigure`/`loadfigure`/`exportfigure` builtins over the host-callback `IScriptFigureFiles`.
- **M20** C-style JGS syntax and interactive 3D plotting: compound assignment and `++`/`--` with
  full expression semantics, lenient brace/newline placement, destructuring `let [X, Y] = ...`;
  rotatable `surf`/`mesh`/`meshc` surfaces (drag to rotate, wheel to dolly, undoable camera),
  `contour`/`contourf`, `imagesc`/`pcolor`, `colormap` + a rendered colorbar, `zlabel`/`zlim`/`view`,
  matrix-aware JGS arithmetic (`meshgrid`, `zeros(r, c)`, recursive elementwise ops), and `.graph`
  format version 2 persisting 3D axes and surfaces.
- **M21** MATLAB compatibility and figure QOL: semicolon echo suppression with `ans`, colon ranges,
  1-based paren indexing with `end`/slices (with `find` 1-based to match), `for … end`/`elseif`
  blocks, `~=`/`.*`/`^`, `[a; b]`, `figure;` command form with automatic figure display, complex
  numbers; the DSP/audio builtins (`fft` at any length via Bluestein, `filter`, `butter`, `firpm`,
  `freqz`, `audioread`/`sound`/`pause`) over new `JGraph.Signal` algorithms and the `IScriptAudio`
  seam — two real MATLAB lab scripts run unmodified except comments/`let`/commas; plus the default
  Pointer tool (pan + hover crosshair + click-to-pin persistent data tips), the roving Data Tips
  tool, the plot right-click menu (zoom constraints, tip deletion, per-axes Restore View), and
  `.graph` format version 3 persisting data tips.
- **M22** large-dataset performance: the `JGraph.Numerics` project (dual-strategy
  managed/native/memory-mapped `NumericBuffer` storage picked by available RAM, plus chunked
  cancellable `TensorPrimitives` SIMD kernels), packed JGS numeric and planar complex arrays with
  full boxed-mode parity (kill switch + byte-identical corpus tests), bounded display/snapshot of
  huge arrays, Stop working mid-operation, a pooled direct-sincos FFT twiddle table, windowed
  binary-search hover hit-testing, reuse of the Skia polyline path, and `.graph` format version 4
  (packed base64 series, streamed save/load).
- **M23** RF core: the `JGraph.Signal/Rf/` folder (Touchstone reader, S/Z/Y/ABCD conversions,
  cascade, Γ/VSWR, microstrip/stripline calculators), S-parameter networks carried as `Table`
  values, a Γ-direct `smithplot`, and ~20 RF JGS builtins.
- **M24** image-processing core: the `JGraph.Imaging` + `JGraph.Imaging.Codecs` projects, a new
  `JgsType.Image` value (`ImageBuffer`, `[0,1]` samples on a `NumericBuffer`), the true-colour
  `RgbImagePlot` (`.graph` format version 5), and ~35 image JGS builtins spanning IO/display,
  point/histogram/geometry ops, 2-D filtering, edge detection, morphology, and region analysis.
- **M24c** image-processing extensions: Roberts and LoG `edge` methods plus `imgradient`/
  `imgradientxy` (`Gradients.cs`), the Hough line trio `hough`/`houghpeaks`/`houghlines`
  (`HoughTransform.cs`), binary cleanup (`imfill`, `bwareaopen`), `immultiply`, `regionprops` on a
  binary image with optional intensity weighting, image-wide `sum`/`mean`/`min`/`max`, and the
  `size(x, dim)`, `isempty`, and `fprintf` script utilities.

The `JGraph.Demo` gallery exercises the plot types, annotations, and both APIs;
`JGraph.Application` is the interactive figure window with data import and scripting.
