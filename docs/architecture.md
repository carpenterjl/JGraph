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
| `JGraph.Signal` | net8.0 | Signal-processing services (FFT, windows, amplitude spectrum, STFT spectrogram, transfer-function frequency response) for the engineering plots, plus the `Rf/` RF core (Touchstone reader, S/Z/Y/ABCD conversions, microstrip/stripline calculators). Pure numerics and a leaf, but no longer BCL-only: since M96a (ADR 0096) the transform itself lives in `JGraph.Numerics.FftKernels` — the scripting layer has to run it over packed storage, and one transform means one set of answers — so `Fft` is the boxed door onto it and this project takes a reference on `JGraph.Numerics`, the way `JGraph.Imaging` already did. |
| `JGraph.Numerics` | net8.0 | Flat contiguous numeric storage for large datasets: the dual-strategy `NumericBuffer` (managed / `NativeMemory` / memory-mapped temp file, chosen by the RAM-aware `BufferAllocator`) and the chunked, cancellable `PackedMath` SIMD kernels over `TensorPrimitives`. Each unary kernel carries a determinism tier (ADR 0092): `Exact` where the vector form and the scalar loop agree bit for bit and is wired unconditionally, `Approximate` where they differ by a few ulps and waits behind `ApproximateThreshold` (32K elements, `JGRAPH_FAST_MATH=0` to disable — ADR 0093), with `UnaryTiered` making the choice so callers need not know which is which. Those kernels sweep their buffers through `ParallelKernels.For` in fixed 64K grains — a decomposition that depends on length alone, so a thread count is never an input to an answer — on up to `MaxDegree` threads (`JGRAPH_THREADS`, default logical processors capped at 16, which is the opposite of the native side's physical-core rule and for the reason ADR 0093 records); the cancellation poll runs between grains and `OperationCanceledException` comes back out unwrapped, while the reductions that fold values stay serial and in index order. The dimension reductions carry their own kernels in `ReduceKernels` (ADR 0094): a reduction along one dimension decomposes as `(inner, n, outer)`, every output is folded whole by one thread in the boxed fold's own order — contiguous slices when the dimension is the first, one running accumulator per output across each interleaved panel otherwise — over blocks `ParallelKernels.ForBlocks` cuts at boundaries that are a function of the shape alone, so `sum(A, 1)`, `max(A, [], 2)`, `cumsum` and their kin answer the boxed bits at any thread count. `sort` has its own kernels in `SortKernels` (ADR 0095) over the same decomposition: one long slice is cut into buckets by value — splitters from a strided sample, a counting pass, a stable scatter, then every bucket sorted on its own thread with nothing to merge, because the buckets already sit in order end to end — while many short slices take a thread each. What a sort has to defend is not a fold order but a tie rule: values compared with `<` so the two zeros tie, equal values left in the order they arrived whichever direction is asked for, NaN lifted out and put back where `MissingPlacement` names it with its own bits intact. The discrete Fourier transform is `FftKernels` (ADR 0096), over planar storage — real parts in one span, imaginary in another — so a butterfly is doubles rather than boxed `Complex` and eight signals ride one SIMD register side by side; the bit-reversal, stage order, twiddle table and product spelling are the pre-M96 radix-2's, so every length under 32K points answers the old bits, while a longer one is factored into two batched passes with the transpose folded into the gather's stride and rounds differently by length alone. `FilterKernels` does `filter(b, a, x)` where the denominator has nothing past `a(1)`: the recurrence unrolls to one right-nested chain per output, so vectorising goes across outputs and never across taps and the bits are the recurrence's own — except that a NaN now reaches only as far as the filter is long, which is what MATLAB does and what `0 · y` did not. A numeric class is a rule the kernels carry rather than a sweep of their own (ADR 0098): `PackedMath.Rounding` is `single`'s round to float precision, or an integer class's round half away from zero, saturate into the class range and read NaN as zero — spelled once as a scalar and once in vector registers, comparing the fraction against a half rather than adding one to the element and selecting the step away from zero rather than adding it, so that neither a value one ulp under a tie nor a negative signed zero moves where `Math.Round` would leave it. The elementwise kernels take that rule as an argument and apply it to each tile in the pass that computed the tile, which is what makes an `int32` addition cost what a `double` addition costs. Dense linear algebra answers through the `DenseLinalg` provider seam (ADR 0088–0091): the bundled OpenBLAS (`native\win-x64`, loaded and configured by `OpenBlasLoader`, one thread per physical core) when it loads, the managed kernels otherwise, forced either way by `JGRAPH_LINALG`. The seam carries the product and the symmetric rank-k product, the LU family with its solves, inverse and condition estimate, Cholesky, the triangular solve, least squares, Householder QR with and without pivoting and the two ways of applying its reflectors, the singular value decomposition with its fallback driver, the symmetric and general eigensolvers, the generalized (pencil) eigensolvers, the real Schur form with its reorder, the QZ factorization, and the complex z-family (product, LU trio, eigensolver, SVD — `Span<Complex>` crosses the boundary as LAPACK's own interleaved layout); `LuDecomposition`, `Cholesky`, `QrDecomposition`, `Svd` and `Eigen` hold their factors flat and column-major, which is both LAPACK's layout and the script's. The only project that compiles unsafe code or touches native math. A leaf; consumed by `JGraph.Scripting`. |
| `JGraph.Imaging` | net8.0 | Image-processing core: the `ImageBuffer` value type (interleaved `[0,1]` samples backed by a `NumericBuffer`) and the codec-free algorithms — point ops, histograms, geometry, 2-D filters/kernels, edge detection and gradients, the Hough line transform, morphology, connected-component labeling and region measurement. `conv2(u, v, A)` runs as `Filters.SeparableConvolve2` — a row pass and a column pass, threaded over bands of output rows, never building the outer product (ADR 0096); the general `Convolve2` is untouched and still exact. Depends only on `JGraph.Numerics`. |
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
| `JGraph.Scripting` | net8.0 | Scripting hosts: the `IScriptEngine` seam, a Roslyn C# engine, a pythonnet (CPython) engine, and the built-in **JGS** language (a self-contained lexer/parser/interpreter under `Jgs/`). The same pipeline runs **MATLAB** (`.m`) as a second dialect: a `JgsDialect` record threads through the lexer, parser, and interpreter, and `MatlabScriptEngine` fixes it to MATLAB (1-based, `%` comments, `function` defs, cells/structs, value semantics) — see ADR 0031. A MATLAB `for` over a range or a `while` whose body works entirely in scalar doubles compiles once (`LoopCompiler` → `RegisterProgram`, ADR 0099) to a linear op array over an unboxed double register file and runs without the tree walk — same arithmetic bound from the same scalar kernels, step counting and cancellation preserved, variables spilled to the environment exactly where the walk would have left them, and every case a register cannot hold (an answer leaving the reals, an error the walk would throw, a shadowed builtin) handed back to the walk; `JGRAPH_LOOP_JIT=0` disables it, and the two roads must print byte-identical output. The `JGraphScriptGlobals` bridge drives the `JG` API and the `Table` readers. Also the UI-free `Workspace/` layer: `ScriptWorkspace` (folder enumeration, watcher, bare-filename resolution) and the session/document state models behind the scripting window. WPF-free, no dependency for JGS. Also `Startup/`: the shared command-line parser, statement resolution, output sinks (console/file/tee) and `BatchRunner` that both executables run. |
| `JGraph.Controls` | net8.0-windows | WPF `FigureControl` hosting the Skia surface, the WPF→interaction input adapter, and the AvalonEdit-based `ScriptEditorControl`. |
| `JGraph.Application` | net8.0-windows | MVVM application shell and DI composition root. The **scripting workspace is the main window** (menu bar, toolbar, docked panes, `RoutedUICommand`s in `WorkspaceCommands`, panes in `PaneCatalog`), brought up behind a splash by `Startup/InteractiveStartup`; figure windows are transient and number-keyed, opened by scripts, `.graph` files, or Tools → New Figure Window. See ADR 0033. Acts on the startup options: `-r` runs a statement and leaves the session open, `-batch -showfigures` runs one with no shell at all. Holds the user settings (`SettingsService` over `%AppData%\JGraph\settings.json`) and the Tools → Options dialog; the JGS engine and the plugin loader read from them. See ADR 0032. |
| `JGraph.Cli` | net8.0 | `jgraph.exe` — the command-line launcher. Parses the startup options, owns stdout/stderr and the process exit code, and runs `-batch` **in-process with no WPF and no display**. References `JGraph.Application` for build order only (`ReferenceOutputAssembly="false"`); the two meet as processes. See ADR 0030. |
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
5. Bulk geometry goes through two batching primitives rather than one call per cell:
   `DrawTriangles` (a non-indexed triangle soup with per-vertex colors) and `DrawPaths` (many
   sub-paths filled and stroked as one path, so adjacent sub-paths tile without antialiasing seams).
   Skia's SVG and PDF backends silently drop `drawVertices`, so the context carries a
   `supportsMeshes` flag — false for vector export, which falls back to one path fill per triangle.
   See [ADR 0047](adr/0047-surface-rendering-quality-and-performance.md).

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
   those descriptors and the model tree. Struct-valued properties (`TextStyle`, `LineStyle`,
   `Rect2D`, `Size2D`, `Point2D`) expand into a collapsible header row plus a child row per member;
   a child reads the whole struct, swaps one member and writes it back, and records undo against the
   root property so one step restores the whole style (ADR 0029).
5. **The legend is an editable object.** `LegendModel` owns an ordered list of `LegendEntryModel`
   rows, each bound to a plot with a label override and an include flag; `SyncEntries` (run from the
   renderer's pre-layout pass, idempotent) keeps the rows in step with the plots. It has a `Custom`
   position with a fractional `Location`, is draggable in `EditMode` via bounds the renderer
   publishes on `AxesRenderInfo`, and its multi-property drag undoes in one step through
   `CompositeAction`. New elements are added through the UI-free `FigureElementCommands` and the
   descriptor-based `ElementMenuBuilder`, surfaced as the plot browser's context menu and "Add ▾"
   button (ADR 0029).

See [ADR 0005](adr/0005-editing-and-annotations.md) and [ADR 0029](adr/0029-editable-figure-legend-and-add-elements.md).

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
   to a console. **Interactivity is a separate capability**: `IScriptRepl.CreateSession(context)` hands
   back an `IScriptSession` whose variables, functions and figures survive from one `ExecuteAsync` to
   the next, and which only gives its memory back on `Clear()` or disposal. Hosts feature-detect it
   (`engine is IScriptRepl`), exactly as they do for `IJgsDebuggable` — an engine that only knows how
   to run a whole file stays valid (ADR 0035).
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

Implemented through Milestone 45 — a working figure window you can edit, save, publish, extend, feed with imported data, and drive with scripts:

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
- **M24d** correctness follow-ups: `imcentroid` (`Regions.WeightedCentroid`) measures the
  intensity-weighted centre of a whole masked image, so a speckled spot broken into many
  components cannot bias the answer the way picking one region does; `JG.Legend` assigns names
  only to plots that can appear in a legend, so an `imshow` backdrop no longer swallows the first
  label; filename completion covers every file-reading builtin (`imread`, `sparameters`,
  `loadfigure`, `audioread`, …), not just the table readers.
- **M25** consistency pass: **New Script** is a language menu (JGS / C# / Python / plain text) that
  names the tab `NewScript` with the right extension, so highlighting and the Run engine are correct
  before the first save; and **JGS indexing is uniformly 0-based** (ADR 0028, superseding ADR 0023
  §3) — `a[i]` and `a(i)` are one operation with `end`, `:`, slice and mask writes, and image
  subscripts in both spellings; `find`/`houghpeaks` and every pixel coordinate in `JGraph.Imaging`
  moved with it. Figure handles, `subplot` cells, and RF port numbers stay 1-based, being names
  rather than offsets.
- **M26** a fully editable figure: the inspector expands struct-valued properties (`TextStyle`,
  `LineStyle`, `Rect2D`, …) into editable child rows with an editable font-family picker; the legend
  became a first-class object with reorderable, renamable, includable `LegendEntryModel` rows, a
  `Custom` free placement it can be dragged to, and the standard selection box; and elements
  (titles, legend, colorbar, extra/secondary axes, subplot grids, annotations) can be added from the
  plot browser's context menu or "Add ▾" button through the UI-free `FigureElementCommands`. The
  `.graph` format stayed at version 5 — the new legend fields are optional (ADR 0029).
- **M30** the scripting workspace became the application shell: it is the main window, brought up
  behind a progress splash whose artwork is replaceable without a rebuild; every action is a
  `RoutedUICommand` shared by a real menu bar, the toolbar and the keyboard; tool panes come from a
  `PaneCatalog` that also decides where a pane missing from a saved layout is put back; the window's
  placement is remembered; and a first run seeds and opens a workspace of the shipped examples
  (ADR 0033).
- **M31** the console became interactive. `IScriptRepl`/`IScriptSession` is a capability beside
  `IScriptEngine` (feature-detected like `IJgsDebuggable`) giving each language a live workspace that
  survives between statements: `JgsReplSession` for JGS and MATLAB, `CSharpReplSession` over Roslyn's
  `ContinueWithAsync`, and `PythonReplSession` running CPython **out of process** over
  newline-delimited JSON so Ctrl+C can actually interrupt it. F5 runs the active document *inside*
  that session, so a script and the prompt share one workspace — unless a breakpoint is set, which
  still routes the run through the debugger and its own environment. Matrices, cells and structs reach
  the Data Viewer as a `ScriptValueGrid` (ADR 0035).
- **M32** the application got a light and a dark theme. `JGraph.Controls/Themes/` holds the key
  contract (`ThemeKeys`), the two value dictionaries and the implicit styles;
  `JGraph.Application/Theming/ThemeManager` swaps **one** merged dictionary, found by the
  `JG.Theme.Id` sentinel each theme carries, and everything reads its colours through
  `DynamicResource` so the change is live. The docking frame is handed AvalonDock's matching
  `Vs2013{Light,Dark}Theme`; the code editor gets both syntax palettes, and AvalonEdit's own
  light-tuned C#/Python definitions are re-contrasted by rule against the editor background. App
  chrome stays separate from `JGraph.Core.Drawing.ITheme`, which is plot ink and ends up inside
  `.graph` files (ADR 0034).
- **M33** JGraph got an installer. `installer/build-installer.ps1` publishes both executables into
  one staging folder — the "deployed layout" `GuiLauncher` already expected — and builds a
  per-machine MSI from it with WiX 6 (`installer/JGraph.Installer`, deliberately outside the
  solution). The MSI harvests the staging folder wholesale, adds a Start Menu shortcut, and offers
  an "add JGraph to PATH" checkbox whose choice is remembered across upgrades; re-running the MSI
  updates in place (`MajorUpgrade`, immutable UpgradeCode) and uninstalling removes the PATH entry
  with the product (ADR 0036).
- **M34** MATLAB function files run, and the editor stopped losing work. A file that is nothing but
  function definitions now auto-invokes its first function on a file run (`ExecuteFileAsync` on
  `IScriptSession` + `JgsRunner.InvokeMainIfFunctionFile`; prompt input still only defines). The
  File menu gained Save As…; closing a dirty tab or the app prompts Save/Don't Save/Cancel through
  the same `TrySave` path, and saving onto a read-only file offers to strip the attribute or divert
  to a writable copy instead of failing into the status bar. The console gained `clc` (a `Clear()`
  default method on `IScriptOutput`), `dir` (a cell of names via the host resolver), and a
  display-only `path`; `addpath`/`rmpath` name the missing search path explicitly (ADR 0037).
- **M35** Running a script twice does the same thing twice. Figure display became per run: `JG`
  stamps every figure a run touches, and the run displays exactly those — so a re-run brings its
  windows back (recreating any the user closed) and the figure count stops counting figures the run
  never touched. Closing a figure window now retires the figure itself, and scripts gained `close`,
  `close all`, `clf`, `gcf`, and `gca` over a new `ScriptContext.CloseFigure` host callback.
  `figure(n)` as a statement prints nothing and sets no `ans`, and `ans` inside a function body stays
  in the call frame. `hold` moved onto the axes (so it ends with them), and `hold off`/`grid off`
  read the word instead of its truthiness — they used to turn the feature *on*. Plotting without hold
  resets the axes the way MATLAB's `NextPlot='replace'` does, so a re-run no longer inherits the
  previous run's title or frozen limits. F5 resolves a script's relative paths beside the script
  again (ADR 0038).
- **M36** The MATLAB foundational core: every item of the minimum-viable command set is in.
  Builtins produce multiple outputs (`[X, Y] = meshgrid`, `[m, i] = max`, `[V, D] = eig`) through a
  `MultiOutput` seam on `BuiltinFunction`; `JGraph.Numerics.LinearAlgebra` gained LU/QR/SVD/eigen
  kernels behind the new `\` `/` `^` matrix operators and the `inv`/`det`/`rank`/`norm`/`trace`/
  `eig`/`lu`/`qr`/`svd`/`dot` builtins; the shape family arrived (`eye`, `diag`, `magic`,
  `logspace`, `reshape`, `cat`, flips, `permute`, `prod`, `ismember`) and MATLAB-dialect reductions
  go column-wise over matrices with `dim`/`'all'` arguments while JGS stays flat. `whos`, `help`
  (reading the builtin catalog), and `format` cover the environment; `save`/`load` speak real
  MAT-file v5 (compressed files included) plus `-ascii`, and `fopen`/`fclose`/`fread`/`fwrite`/
  `fgetl`/`fprintf(fid, …)` give byte-level access over a per-run file-id table. Command syntax
  learned dotted file names and `-option` words (`save state.mat -ascii`), and
  `tools/matlab-checklist/` regenerates the documented-command tracker (ADR 0039,
  [matlab-foundational-coverage.md](matlab-foundational-coverage.md)).
- **M37** The first coverage batch off the documented-builtin tracker, 109 → 185 of 515: the numeric
  constants and limits (`Inf`, `NaN`, `i`/`j`, `newline`, `eps`, `realmax`, `realmin`, `flintmax`,
  `intmax`, `intmin`), the type predicates and `class`/`isa`/`cast`/`logical`, the trigonometry
  MATLAB has beyond the original six (degree forms exact at the quadrants, hyperbolics, the
  reciprocal family), and the operator function forms (`plus` … `mldivide`, `xor`, `colon`) — which
  are the interpreter's own operators under function names, declared by the interpreter itself so
  there is only one definition of what `\` means. `BuiltinFunction.AutoCallsBare` lets a builtin be
  a value on sight (`x = eps`) and a function when called (`eps(x)`). `startswith`/`endswith` were
  renamed to MATLAB's `startsWith`/`endsWith`, and `isequal(NaN, NaN)` is false again, with
  `isequaln` for the other reading (ADR 0040,
  [matlab-builtin-coverage.md](matlab-builtin-coverage.md)).
- **M38** The second coverage batch, 185 → 326 of 515 — essentially everything the value model can
  already express. `JGraph.Numerics` gained `SpecialFunctions` (the gamma and error families, the
  incomplete gamma and beta integrals with their inverses, polygamma) built on one Lanczos log-gamma
  and modified-Lentz continued fractions, so `erfcx(30)` is exact where `erfc(30)` underflows to
  zero, and `LinearAlgebra/Factorizations` (Cholesky, LDLᵀ, Hessenberg, the matrix exponential)
  behind `chol`/`ldl`/`hess`/`expm`/`linsolve`/`rcond`/`null`/`orth`/`pinv`. Script-side: bit
  manipulation and radix conversion, the accuracy-preserving elementary functions, the matrix shape
  questions, full regular expressions with MATLAB's option-word output ordering, `sscanf`, the array
  and nine `mov*` windowed statistics, `accumarray`/`arrayfun`/`bsxfun`, and a file and environment
  layer (`cd`/`pwd`/`mkdir`/`isfile`/`fseek`/`jsonencode`/…). `eval`, `evalin`, `assignin`,
  `exist`, `who` and the argument checks need the running scope, so the workspace owners declare them
  through `RegisterEvalBuiltins` against a new `Interpreter.CurrentFrame`; `evalc` captures console
  output through a buffer on `JGraphScriptGlobals`. `true(n)`/`false(n)` are logical-array
  constructors recognized in the parser, since both words are lexer keywords. The Bessel family is
  deliberately still absent — it needs a dedicated kernel to be worth the name (ADR 0041).
- **M39** The third coverage batch, 326 → 363 of 514, which is the whole implementable remainder.
  `JGraph.Numerics` gained `BesselFunctions` — Steed's method for J, Y, I and K of real order, with
  the I/K pair worked in exponentially scaled terms so `besselk(0, 800, 1)` is an ordinary number
  where the plain call correctly underflows, and Airy built on top of it — and `LinearAlgebra/Schur`,
  the Francis double-shift iteration behind `schur`, `ordeig` and `ordschur`, whose block exchange
  solves a small Sylvester equation rather than casing on the four block-size pairings.
  **Validating that turned up a real bug: `eig` on a general non-symmetric matrix was returning wrong
  eigenvalues**, and now reads them off the Schur form, where they reproduce the trace and the
  determinant to fourteen digits. `qrupdate` needed the full orthogonal factor, so `[Q, R] = qr(A)`
  is now MATLAB's full factorization with `qr(A, 0)` for the economy one. `JGraph.Maths` gained
  `Contours/ContourPaths` (chaining marching-squares segments into polylines) and `Geometry/Delaunay`
  behind `contourc` and `delaunay`. Script-side: `func2str` prints from a new `AstPrinter` rather
  than from retained source text, `inputname` reads the `CallExpr` the interpreter hands to each new
  frame, and the console-session and installation queries (`diary`, `lookfor`, `what`, `version`,
  `computer`, `memory`, …) arrive through `RegisterSessionBuiltins`. Every zero-argument question now
  answers to its bare name, which fixes `disp(pwd)` and its neighbours from M38 as well. Six of what
  is left — `fill`, `fill3`, `patch`, `plot3`, `line`, `text` — are figure-model work rather than
  builtin coverage: each needs a drawing primitive the object model does not have (ADR 0042).
- **M40** Arrays carry a rows-by-columns shape over flat **column-major** storage, which closes the
  two fidelity gaps the coverage document had been recording. `A(i, j)`, `A(i, :)`, `A(:, j)`,
  submatrix reads and writes, growth (`A(3, 4) = 1` zero-fills), deletion (`A(i, :) = []`) and a
  per-dimension `end` all work; a single subscript on a matrix is column-major linear, so an index
  from `find` reads back the element it found; `A(:)` is MATLAB's flatten; and a transposed vector is
  a genuine column, so `[(1:3)', (4:6)']` builds a 3×2. Column-major was chosen because it is what
  MATLAB means by linear order, what `reshape` already assumed, and how a MAT-file is laid out on
  disk. `JgsMatrix` is the single place that knows the layout, and pointing the four pre-existing
  helpers at it is why **fifty call sites across the linear algebra, reductions, geometry and Schur
  builtins needed no edit**. Shape lives on the value wrapper, so every path that mints a new
  wrapper — a copy, an elementwise map, a comparison, a gather, a transpose — carries it across
  explicitly. Bracket literals became real MATLAB concatenation behind a new dialect flag, since in
  JGS `[[1, 2], [3, 4]]` is still a list of lists. Separately, **`true == 1` is now true** and
  **`NaN == NaN` is now false** — the packed comparison path carried its own copy of the strict rule
  and changed with it — and `isequal` compares sizes, not just elements (ADR 0043).
- **M41** The language and value-model half of making the sixteen MATLAB stress-test scripts run
  clean. Scripts execute on dedicated 16 MB-stack threads (`ScriptThread`), so 400-deep recursion is
  survivable and the interpreter's limit (512) trips as a catchable error instead of a process-killing
  stack overflow; a packed matrix grows **in place with amortized capacity** under MATLAB's
  copy-on-assign (the 5000-step `A(i,i)=i` loop went from hours to a quarter-second), with `AsBuffer`
  compacting on sight so no raw-buffer consumer can observe the slack. The parser gained MATLAB's
  comma statement separator (`if cond, stmt; end`, one-line `function` bodies), nested functions in
  end-closed files (a token pre-scan settles the file's style; nested bodies close over the parent's
  live frame), and `persistent`. Arrays became **N-D** — an `int[]` of dimensions over the same flat
  column-major storage, with `_rows`/`_cols` holding MATLAB's own 2-D fold so two-subscript readers
  needed no edits — and elementwise operators, comparisons and `bsxfun` all route through one
  **implicit-expansion** engine (`JgsBroadcast`), so a column plus a row is their outer sum. Cells
  carry the same wrapper shape (`cell(r,c)`, `C{r,c}`), a struct array is a cell of structs grown by
  `S(n).f = v`, `for` iterates matrix columns, `run('file.m')` executes under the file's dialect, and
  `clear`/`clearvars`/`whos` are workspace builtins in every host, with plain `clear` sparing
  script-defined functions (ADR 0044).
- **M42** The numerics half of the same campaign. A real sparse class: `JgsType.Sparse` over an
  immutable CSC `CscMatrix` (`JGraph.Numerics/Sparse`), operators dispatched ahead of the dense
  machinery (sparse±sparse and sparse×sparse stay sparse, sparse×dense densifies, anything else
  errors by name and points at `full()`), two-output `lu` by Gilbert–Peierls with the permutation
  folded into L, and `eigs` by one generous Arnoldi expansion with Ritz vectors recovered by shifted
  inverse iteration — the 5000×5000 `sprand` script factors and runs end-to-end in half a minute.
  Integer classes are MATLAB conversions (round half away from zero, saturate, NaN→0) on double
  storage, with `uint8.empty(0, 5)` reached by letting member access on a builtin consult a statics
  table. The dense `*` moved from a per-element delegate loop to a **parallel column-major saxpy
  kernel** (`DenseProduct`) — 100 iterations of `A*A'` at n = 1000 in ~80 s instead of hours.
  Complex `det`/`inv`/`trace`/`eig`/`svd` and complex-producing `exp`/`log`/`sqrt` dispatch
  additively off `HasComplexElements` (a new `ComplexEigen` QR kernel); `sqrtm` is Denman–Beavers,
  `logm` inverse scaling-and-squaring over it, and `ode45` is Dormand–Prince 5(4) with FSAL, with
  `plot` accepting name-value pairs and matrix columns so the results can be looked at (ADR 0045).
- **M43** The data types and graphics verbs that finished the campaign: `table(...)`/`timetable`
  construct the existing `JGraph.Data.Table` (member access reads a column — text columns as cells,
  so `T.Code{2}` braces in), `categorical`/`summary`, `seconds`, and `missing` are documented
  untyped stand-ins, and the string family completed (1-arg `split`/`join`, `string`, `cellstr`,
  `compose`, `ismissing`). MATLAB's `sprintf`/`fprintf` **flatten arrays and cycle the format**,
  `~` is element-wise over arrays, `num2str` honours a format string, and brace assignment reaches
  through dot chains. `tiledlayout`/`nexttile` ride on the subplot grid, `axis` learned its aspect
  words and limit vector, `colormap` gained turbo, `surf`/`contourf` accept full meshgrid matrices
  (and a scalar level count), and `shading`/`lighting`/`camlight`/`rotate3d` are accepted no-op
  verbs. **All twenty stress scripts — the sixteen user-written ones plus the four self-checking
  Fable scripts (stess_17–20) — run clean under `jgraph -batch`** (ADR 0046).
- **M44** Surface and contour rendering, measured rather than guessed. Two new batching primitives on
  the render seam — `DrawTriangles` (a non-indexed soup, since Skia 2.88 caps indexed meshes at 65,536
  vertices and faceted shading needs unshared vertices) and `DrawPaths` — turn `rows·cols` draw calls
  into `rows + cols - 3`, batched by **anti-diagonal wavefront** because row banding lets a cell
  occlude its own neighbour. Painter order became analytic: under orthographic projection occlusion
  depends only on the ground footprint, so sorting by the sweep direction is *more* correct than the
  mean-depth sort it replaced, which mis-ordered a spike against a flat neighbour — the depth sort
  stays live behind a monotonicity check, and stays for M45's parametric surfaces. Contour geometry
  now outlives its frame: one sweep extracts every band (`ContourBands`), assembled iso-lines are
  cached (`ContourLineSet`, which also fixes dashed contours restarting their pattern every two
  points), and a pan/zoom/resize/theme change only re-maps. A 500² `surf` frame went 498 ms and
  27.6 MB to **143 ms and 80 KB**; a `contourf` repaint went 23.0 ms and 9.15 MB to **1.16 ms and one
  byte**. **Parula** exists and is the default for surfaces, contours and images, alongside eight more
  MATLAB maps, each carrying exactly the stop count its own definition turns at. **Lighting is real
  and off by default** — `LightingModel` (MATLAB's material presets over Blinn-Phong) plus
  `AxesModel.Lights`, with normals in the projection's normalized cube space and a non-uniform
  stencil, so `lighting`/`material`/`light`/`lightangle`/`camlight` stopped being no-ops. An axes with
  no lights renders exactly what it always did, which is MATLAB's behavior too. Figure windows now
  open with both side panels hidden (ADR 0047).
- **M45** The 3-D command surface — 36 verbs, and the reason the gap was invisible. The coverage doc
  tracked only what MATLAB documents as kind *builtin*; nearly the whole plotting surface is kind
  *function*, so a command never written looked exactly like one already there. It now tracks both:
  **372 of 514 builtins** and **78 of 263 graphics functions**, with the remaining 185 partitioned
  into five families that sum exactly. `SurfacePlot` gained full **X/Y matrices** alongside the
  vectors, reversing ADR 0046 §6's meshgrid collapse — a sphere is not a height field, which is also
  why it takes M44's depth-sort fallback rather than the analytic sweep. Four new plot objects
  (`Line3DPlot`, `Scatter3DPlot`, `PatchPlot`, `QuiverPlot`) plus a 3-D anchor on `TextAnnotation`
  finish the drawing primitives the coverage doc had called the most useful thing left — `plot3`,
  `line`, `text`, `fill`, `fill3`, `patch`, `surface`, `light`. The colormap generators return
  m-by-3 tables so `colormap(parula(64))` works, and `caxis`/`material`/`lightangle`/`camlight`
  reach M44's lighting model. The camera verbs map onto the orthographic projection and **say where
  they cannot** — `campos` reads direction only, `camtarget`/`camup` are fixed, `camva` is a zoom.
  Nine of the twelve surface variants turned out to be geometry rather than rendering: `meshz` rings
  the grid with a curtain at the base height, `waterfall` is one closed polygon per row, `contour3`
  is the existing tracer with each vertex placed at its own level. `.graph` stays v5, every addition
  being a new derived DTO or a defaulted property (ADR 0048).
- **M46** The Image Processing Toolbox surface — **266 of the 409 documented names**, in twelve
  waves, with **zero pending**: everything outside the recorded exclusions is implemented, and each
  exclusion names the subsystem it would need. The list had to be transcribed by hand from MathWorks'
  reference, because the R2021b dump the base tracker reads came from an install without the toolbox,
  so `verify-ipt-coverage.py` was written before the first wave rather than after the last. The
  algorithm layer went from 12 files to 51 and the builtin layer from one 871-line file to thirteen
  partials. **A class tag rides on `ImageBuffer`** — `imread` answers `uint8`, `class(I)` says so,
  `I(1, 1)` reads 0–255 — while the *visible scale* is dialect-scoped, so JGS keeps its documented
  `[0, 1]`, zero-based surface and every existing JGS test passes unchanged. **A volume is a plain
  N-D array**, not a third value type and not an image with a third size: an image's third dimension
  is colour and a volume's is depth, so the volume functions refuse an image outright rather than
  filtering its channels as though they were slices. Options are parsed once by a shared spec —
  exact case-insensitive matching, no prefix matching, and an unknown option lists the documented
  spellings. Under MATLAB `regionprops` returns a struct array and `bwconncomp` MATLAB's struct;
  under JGS both stay Tables, branched once at registration time. **Eighteen answers were corrected
  rather than added** — `imrotate` turned the wrong way, `imresize` sampled on the wrong grid,
  `imdilate` did not reflect its structuring element, `eig` on a general matrix returned wrong
  eigenvalues — and four of those came from the milestone's own stress script: a picture could not be
  sliced, `cat` refused any dimension past the second, fourteen functions refused a plain matrix, and
  a two-subscript write on a JGS matrix threw out of the interpreter. `.graph` stays v5, since every
  display verb bakes to RGB (ADR 0049).
- **M47** The three base-language gaps M46 recorded and left, closed. **A number remembers the class
  it was asked for**: `JgsValue` carries a `NumericClass` tag, so `class(uint8(7))` answers `'uint8'`
  and `isinteger` is true, while storage stays doubles — the same shape ADR 0043 chose for shape
  itself, and for the same reason. Arithmetic keeps the answer inside the narrower class
  (`uint8(200) + uint8(100)` is 255) and refuses what MATLAB refuses: two different integer classes,
  or an integer array beside a non-scalar double. Concatenation follows the same precedence, so
  `[int8(1) 300]` saturates its second element. The tag rides along through the three paths that mint
  wrappers — `KeepShape`, `IndexInto`, and the literal builders — which is the M40 lesson applied
  unchanged. **A `for` loop walks a cell array** a column at a time, binding a cell, so
  `for name = {'line', 'diamond'}` reads the way scripts write it; **`height`/`width`** read the first
  two dimensions of anything, and a table's are its rows and its variables, which meant teaching
  `size` tables at the same time (it had been answering `[1 1]` for one). Two further defects
  surfaced while closing these and were fixed: a rowed cell literal was flattened row-major into a
  1-by-n cell, and the assignment copy dropped the class (ADR 0050).
- **M48** `max` and `min` reduce along **any dimension of any shape**. Both were counted as
  implemented while `max(A, [], 3)` gave the wrong answer: they folded the array into rows first, and
  an N-D array read as rows is its pages laid side by side, so the reduction went along the fold.
  They now read their slices straight out of column-major storage through a new
  `JgsMatrix.SlicesAlong`, which is one rule — a slice steps by the product of the dimensions below
  the reduced one — for a row vector, a column, a matrix and a volume alike, so the four shape
  branches the wrapper used to carry are gone. `max(A)` picks the first non-singleton dimension,
  `max(A, [], 'all')` reduces everything with a linear index, and the second output composes with all
  of it. The other reductions still fold, and that is recorded in `matlab-builtin-coverage.md` rather
  than left to be rediscovered.
- **M49** the **other twelve reductions** follow. `sum` `prod` `mean` `median` `std` `variance` `mode`
  `any` `all` and the shape-keeping `cumsum` `cumprod` `diff` `sort` shared a wrapper that folded into
  rows exactly as max/min had, so `sum(A, 3)` summed the pages laid side by side. They now gather
  through `JgsMatrix.SlicesAlong` and — this is what M48 deferred them for — scatter back through a
  new `JgsMatrix.JoinAlong`, which writes one whole vector per slice to where the slice came from and
  reports the shape that makes. One rule covers both groups: a scalar per slice is the same scatter
  with a length of 1, so the reduced shape is not a separate calculation. A non-numeric array (a
  string array, a complex one) and an empty one still go straight to the builtin that already knows
  them, which is why `sort(["b" "a"])` and `sum([])` did not move. `sort` also learned MATLAB's
  `'ascend'`/`'descend'`.
- **M50** gives `diff` **MATLAB's own signature**, `diff(X, n, dim)`. It was the one name in that
  wrapper's list whose second argument is not the dimension, so `diff(A, 2)` meant dimension 2 here and
  the second difference in MATLAB, and repeated differencing had no spelling at all. `WrapColumnwise`
  takes a flag for it rather than growing a bespoke wrapper, and differencing *n* times along a
  dimension is the base builtin applied *n* times to each slice — the slices are walked independently,
  so nothing about the gather or the scatter changes. `diff(A, 0)` is `A`, `diff(A, [], dim)` takes the
  default, and the JGS dialect (which never calls the wrapper) keeps its own one-argument `diff`.

- **M51** gives a script **handles on figure objects**, and gives a table subscripts
  ([ADR 0051](adr/0051-handles-on-figure-objects.md)). It came from running an ordinary user analysis
  script, which failed on its sixth line and then on most of what followed. A handle is an ordinary
  number keyed into `JgsHandleRegistry` — pre-HG2 MATLAB's own model, chosen because handles then live
  in arrays, compare by identity, and gather out of struct-array fields with no new machinery.
  `subplot` and `plot` hand them back, `plot(ax, …)` and `title(ax, …)` aim a verb at a named axes
  without moving `gca`, `p.Color` and `p.Visible = 'off'` read and write properties, and
  `legend(ax, h, 'Location', 'best')` returns a legend handle whose `ItemHitFcn` **runs when the row
  is clicked in the window** — `LegendRenderer` publishes each row's rectangle, the click travels out
  through `IInteractionSurface` to `ScriptGraphicsCallbacks`, and the live console session runs the
  callback under a one-slot busy flag so it can never interleave with a running statement. On the data
  side, `T{rows, vars}` and `T(rows, vars)` reuse the array subscript machinery (so `:`, `end`, ranges
  and masks come free), `readtable` finds the data block under a file's preamble by width, `unique`
  accepts a cell of char, and the lexer now records which quote a string literal used, because
  `['SN:' id]` is one char row where `["a" "b"]` is two strings.

- **M52** makes every registered name take **the arguments MATLAB documents for it**
  ([ADR 0052](adr/0052-the-documented-argument-surface.md)). Three pieces of shared machinery carry it:
  `OptionSpec`/`ParsedArgs` moved out of the imaging namespace into `JgsBuiltins.Options.cs` — it was
  never about pictures, and a spec that knows every legal word can *name the alternatives* where a
  hand-rolled tail silently ignored what it did not recognize; `WrapColumnwise` now reads a
  `ReductionSpec` row per name instead of assuming slot two is the dimension, which is what
  `std(x, 1)` had been paying for; and a new `JgsRandomSource` gives every generator one seedable
  stream behind `rng`, deterministic under a seed but deliberately not bit-compatible with MATLAB's.
  On top of those, the option surfaces of `unique`, `sort`, the `mov*` family, the tolerance pair,
  `strsplit`, `strjoin`, `regexprep`, `cellfun` and `num2str`; a scalar accepted wherever an array is
  asked for; `find(X, k)` split by dialect; `max`/`min` omitting NaN as MATLAB does; and the twelve
  data-analysis names the base language never had, `interp1` among them — its spline and pchip differ
  only in which slopes they choose, so `JGraph.Numerics/Interpolation.cs` is one kernel rather than
  two. An audit of ninety documented call forms found seven everyday names missing that **no coverage
  arithmetic could have reported**, because the tables track builtins and graphics functions and these
  are documented as plain functions; `tools/run-stress.ps1` turned the stress gate from a manual pass
  into a repeatable one, and `stess_24.m` is its twenty-two-section proof.

The `JGraph.Demo` gallery exercises the plot types, annotations, and both APIs;
`JGraph.Application` is the interactive figure window with data import and scripting.
