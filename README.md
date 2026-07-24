# JGraph

A professional, extensible scientific graphing framework for **C# / .NET 8 / WPF**. JGraph recreates
the workflow of a technical-computing figure window — an object model of figures, axes, and plots you can build,
edit, and interact with — while following modern MVVM and SOLID design. It is renderer-agnostic and
built for performance (millions of points).

> Status: Milestones 1–29 complete. A working, interactive, editable figure window with line,
> scatter, bar, stem, histogram, error-bar, and image/heatmap plots; interactive 3D surfaces
> (surf/mesh/meshc with drag rotation), contour/filled-contour plots, and colorbars;
> technical-computing JGS scripting (semicolon echo suppression, colon ranges, uniform 0-based
> indexing in both `a[i]` and `a(i)` spellings with `end`, slice and mask writes, `for … end`
> blocks, complex numbers) with DSP/audio
> builtins (`fft` at any length, `filter`/`butter`/`firpm`/`freqz`, `audioread`/`sound`) that run
> real lab scripts nearly verbatim; large-dataset performance (packed flat
> script arrays with SIMD elementwise math and RAM-aware managed/native/memory-mapped storage,
> instant hover over million-point lines, compact binary series in `.graph` v4);
> persistent draggable data tips, a pan-plus-pick default
> pointer, and a plot right-click menu (zoom constraints, restore view);
> an RF core (Touchstone `.sNp` files, S/Z/Y/ABCD conversions, 2-port cascading, Smith charts,
> microstrip and stripline calculators) and an image-processing core (read/display images as a
> first-class value type, point and geometry ops, 2-D filtering, edge detection including the Hough
> transform, morphology, and region analysis);
> engineering plots (Bode,
> Nyquist, polar, Smith, spectrogram, eye diagram) with an FFT/DSP library; subplots, linked axes,
> date/time and category scales, annotations, an Edit mode, a property inspector, a plot browser,
> a versioned `.graph` save/open format with figure copy/paste, export
> (PNG/JPEG/BMP/TIFF/SVG/PDF + clipboard), CSV/Excel/clipboard data import with a mapping wizard,
> in-app scripting in C#, Python, and a built-in language (JGS) inside a full scripting
> workspace (file tree, multi-tab editors, console, variables panel) with an interactive JGS
> debugger (breakpoints, stepping, set next statement, live code edits while paused, live
> variables, call stack, and a tabular data viewer), code completion with signature help, two APIs,
> a plugin system with
> Light/Dark/Presentation/IEEE themes, undo/redo, and a command-line launcher
> (`-batch`/`-r`/`-logfile`/`-help`) that runs scripts headlessly. See
> [docs/architecture.md](docs/architecture.md) for the design.

## Highlights

- **One object model, two APIs** — build figures with a fluent object-oriented API or a concise
  functional API; both drive the same model.
- **Many plot types** — line, scatter, bar, stem, histogram, error bar, and image/heatmap (with
  perceptually uniform colormaps).
- **Engineering plots** — Bode, Nyquist, polar, Smith, spectrogram, and eye diagrams, backed by a
  built-in signal-processing library (FFT, windows, spectrum, STFT, transfer functions).
- **Interactive 3D** — `surf`/`mesh`/`meshc` surfaces you rotate with the mouse
  (undoable camera), plus `contour`/`contourf`, `imagesc`/`pcolor`, colormaps, and colorbars —
  from scripts (`meshgrid`, matrix math) or either API.
- **Runs real technical-computing scripts** — semicolon suppression and echo, colon ranges, 0-based
  indexing with `end`, slice and mask writes, `for … end` blocks, complex numbers, and the DSP/audio
  surface (`fft`/`ifft` at any length, `filter`, `butter`, `firpm`, `freqz`, `audioread`, `sound`)
  let existing lab scripts run with little more than `let` and the index base touched up.
- **Data tips** — the default pointer pans, shows a crosshair over data, and pins persistent,
  draggable point labels that save with the figure; right-click for zoom modes and tip management.
- **Multi-panel & synchronized** — tile axes into a subplot grid and link their axes so panning or
  zooming one moves the others together.
- **Numeric and non-numeric axes** — linear and logarithmic, plus date/time and category scales with
  automatic, human-readable ticks.
- **Renderer-agnostic** — all drawing goes through an `IRenderContext` seam (SkiaSharp today; SVG,
  PDF, or GPU backends drop in without touching the model).
- **Performance-first** — packed script arrays (flat buffers + SIMD, spilling to native or
  SSD-mapped memory before ever throwing OutOfMemory), and array-backed series with windowed
  min/max decimation that render millions of
  points smoothly.
- **Interactive** — mouse-wheel zoom, drag pan, rubber-band zoom, data cursor, and undo/redo of
  navigation.
- **Editable** — annotations (text, arrows, shapes) in data or figure space; select, drag, and
  delete them in Edit mode; edit any object — down to a title's font, size, weight and colour — in the
  VS-style property inspector or the plot browser tree, with full undo of property edits and moves.
  The legend is a first-class object: rename, reorder, hide, or add its entries, and drag it anywhere.
  Missing pieces (titles, legend, colorbar, extra axes, subplot grids, annotations) are added from the
  browser's context menu or "Add ▾" button.
- **Publishable** — export to PNG/JPEG/BMP/TIFF (print-quality supersampling) or true-vector
  SVG/PDF at exact physical size, and copy the figure image with Ctrl+C; headless export works on
  any .NET 8 platform.
- **Saveable** — save and reopen figures in a versioned, human-readable `.graph` JSON document, and
  copy/paste whole figures as editable object graphs.
- **Themeable** — built-in Light, Dark, Presentation, and IEEE themes (color *and* typography); fully
  customizable.
- **Extensible** — a small plugin system discovers `IPlugin`s from assemblies or a `plugins` folder and
  registers the themes and colormaps they contribute; new themes appear in the app with no recompile.
- **Data-driven** — import CSV/TSV, Excel `.xlsx`, and clipboard tables into an immutable typed `Table`
  (auto-detecting delimiter, header, culture, and column types), then plot columns from the API or the
  figure window's **Import Data…** wizard.
- **Scriptable** — build figures from **C#** (Roslyn), **Python** (real CPython via pythonnet, with your
  installed packages like numpy importable), the built-in **JGS** language (a dependency-free, sandboxed
  interpreter), or **MATLAB** — a `.m` file runs unmodified, with `%` comments, 1-based indexing,
  `function` definitions, cells, and structs (see [the scripting guide](docs/jgs-scripting-guide.html)) —
  in the in-app editor; every script calls the same `JG` API, so every plot type and option is available.
- **A scripting workspace** — open a folder of scripts and data files in the docking scripting window
  (file tree, multi-tab editors, console, and a variables panel). **New Script** asks which language
  you want and opens `NewScript.jgs` / `.m` / `.csx` / `.py`, so highlighting and the Run engine are right
  before you save anything; scripts find workspace data files by
  bare name (`readcsv("data.csv")`), JGS scripts compose via `run("helpers.jgs")`, and the window
  remembers your workspace, open files, breakpoints, and layout between sessions. The Files pane is a
  Current Folder browser: an address bar and Up button, double-click a folder (or
  right-click → "Set as workspace root") to browse into it, and double-click files to open them by
  type — scripts in tabs, csv/tsv/xlsx in the Data Viewer, saved `.graph` documents as live figures,
  text files as plain tabs. Hidden tool panes come back from the toolbar's View menu.
- **Debuggable** — JGS scripts run under a real debugger: click the gutter (or F9) for breakpoints,
  F5 to run/continue, pause any time (even inside `while true {}`), step in/over/out — including into
  functions defined in other workspace scripts, which open in their own tab — with a live variables
  panel and call stack while paused. While paused you can also **drag the execution arrow** (set next
  statement — skip code or run it again) and **edit the code live**: compatible edits apply on resume,
  down to the remaining iterations of the loop you're standing in.
- **A data viewer** — open CSV/Excel files from the workspace tree, or double-click any array/table
  variable, to inspect it in a paged spreadsheet grid.
- **Code completion** — Ctrl+Space (or just keep typing) completes JGS keywords, builtins, your
  variables, and functions from every workspace script; typing `plot(` pops signature help with the
  active parameter in bold; completing a function inserts its parameters ready to overtype. C# and
  Python tabs complete keywords and the `JG` API. Inside the file-reading builtins — `readcsv("`,
  `readtable("`, `readxlsx("`, `audioread("`, `imread("`, `sparameters("`, `loadfigure("`, or
  `run("` — the list offers matching workspace files and folders, so data files complete like code
  (JGS strings may be single- or double-quoted).
- **Data analysis in JGS** — comparisons work element-wise and produce masks (`ids == "SN-1"`,
  `temp > 85`), arrays index with masks and index arrays (`data(parameter > threshold)`
  or `data[mask]`, with `find` mapping matches back to original row numbers), and a compact stdlib
  covers statistics (`std`, `median`, `percentile`, …), array ops (`sort`, `unique`, `slice`,
  `concat`, …), strings (`sprintf`, `split`/`join`, `num`/`str`), and table inspection
  (`colnames`, `rowcount`, `textcolumn` for serial-number columns). Messy files parse too:
  `readcsv("log.csv", 6)` skips six junk preamble lines above the real table.
- **Script-managed figure windows** — `figure()` returns a numbered handle and each
  shown figure opens its **own full figure window** (pan/zoom, edit mode, inspector, export — the
  works); re-running the script updates the same windows instead of spawning more, and the main
  window is left alone. Scripts also `savefigure("run.graph")`, `loadfigure` (a saved figure comes
  back as a live handle you can keep plotting into), and `exportfigure("run.png")` straight into
  the workspace.

## Two APIs, one model

Object-oriented:

```csharp
var figure = new FigureModel();
var axes = figure.AddAxes();
var line = axes.AddLine(x, y);
line.Color = Colors.Red;
line.LineWidth = 2;
axes.Title = "Voltage";
axes.Legend.Visible = true;
axes.AddArrow(2.5, 0.8, 1.6, 1.0);          // annotations, in data coordinates
axes.AddText(2.6, 0.8, "overshoot");
```

Functional:

```csharp
JG.Figure();
JG.Plot(x, y, "r--o");
JG.Title("Voltage");
JG.Grid(true);
JG.Legend("signal");
JG.Text(2.6, 0.8, "overshoot");
```

## More plot types, subplots, and scales

```csharp
var figure = new FigureModel();

// A 2x1 subplot grid with linked X axes.
var top = figure.AddSubplot(2, 1, 1);
top.AddStem(n, h);                       // stem, histogram, error bar, image/heatmap, …
top.AddErrorBar(x, y, error);

var bottom = figure.AddSubplot(2, 1, 2);
var heat = bottom.AddImage(field, new DataRange(-6, 6), new DataRange(-6, 6));
heat.Colormap = Colormap.Viridis;

new AxisLinkGroup(AxisLinkMode.X, top, bottom);   // pan/zoom them together

// Non-numeric axes.
axes.AddBar(new[] { "North", "South", "East" }, new double[] { 42, 30, 55 }); // category axis
axes.AddLine(times, values);            // DateTime[] X → date/time axis with calendar ticks
```

## Engineering plots

```csharp
var figure = new FigureModel();

// Bode plot of 100 / (s² + 10s + 100), as two stacked log-frequency panels.
figure.AddBode(new double[] { 100 }, new double[] { 1, 10, 100 }, omegaMin: 0.1, omegaMax: 1000);

var axes = new FigureModel().AddAxes();
axes.AddNyquist(new double[] { 1 }, new double[] { 1, 1, 1 }, 0.01, 100);  // H(jω) locus, −1 marked
axes.AddPolar(theta, radius);                                             // circular grid, θ in radians
axes.AddSmith(impedanceReal, impedanceImag);                             // z → Γ on a Smith chart
axes.AddSpectrogram(signal, sampleRate: 2000);                           // STFT magnitude heatmap
axes.AddEyeDiagram(signal, samplesPerSymbol: 32);                        // overlaid symbol traces
```

## Export

```csharp
FigureExporter.Export(figure, "figure.pdf");   // true vector, exact physical size
FigureExporter.Export(figure, "figure.svg");   // true vector
FigureExporter.Export(figure, "figure.png", new ExportOptions { Scale = 2.0 }); // 192-DPI raster
```

In the figure window: the **Export…** toolbar button writes any of the six formats, and **Ctrl+C**
copies the figure image to the clipboard.

## Save and open

```csharp
GraphFormat.Save(figure, "figure.graph");        // versioned, human-readable JSON
FigureModel reopened = GraphFormat.Load("figure.graph");

string json = GraphFormat.Serialize(figure);     // or round-trip in memory
FigureModel copy = GraphFormat.Deserialize(json);
```

In the figure window: **Open…**/**Save…** read and write `.graph` documents, and **Copy Figure**/
**Paste Figure** move an editable figure through the clipboard.

## Import data

```csharp
Table table = Table.ReadCsv("measurements.csv");   // or JG.ReadTable(path) / Table.ReadXlsx(path)

axes.AddLine(table, "time", "voltage");            // a date/time column → a date axis, automatically
axes.AddLine(table, "time", "current");
JG.Plot(table, "time", "current", "r--");          // same from the functional facade
```

The reader auto-detects the delimiter, header row, number culture, and each column's type (number,
date/time, or category); pass `ImportOptions` to override any of them. In the figure window, the
**Import Data…** wizard loads a CSV/TSV/Excel file or pasted clipboard table, previews it, and maps
columns onto a plot type — into a new figure or the current axes.

## Script it (C#, Python, or JGS)

The figure window's **Script…** editor runs scripts that build figures with the same API. Scripts call
`JG` directly; `readcsv`, `print`, and `show` are provided by the host. Runnable examples for all three
languages live in [`examples/`](examples/).

C#:

```csharp
var t = readcsv("measurements.csv");
Plot(t, "time", "voltage", "b-");
Title("Voltage");
Legend("voltage");
show();
```

Python (a real in-process CPython via [pythonnet](https://github.com/pythonnet/pythonnet)):

```python
t = readcsv("measurements.csv")
JG.Plot(t, "time", "voltage", "b-")
JG.Title("Voltage")
JG.Legend("voltage")
show()
```

JGS — a small language built into JGraph (no external runtime), with element-wise array math and
built-ins that mirror the API:

```
let x = linspace(0, 6.28, 200)
plot(x, sin(x), "b-")
title("Sine wave")
legend("sin")
show()
```

The C# and JGS engines are always available; the Python engine runs when a CPython 3.x runtime is found
(from the `PYTHONNET_PYDLL` environment variable or the `python`/`py` launcher) and reports a clear
message when it is not.

## Run it from the command line

`jgraph.exe` (the `JGraph.Cli` project) is JGraph's command-line face. `-batch` runs a script with no
window at all — no display needed, so it works over a remote session or as a build step — and returns
a real exit code.

```sh
jgraph                                          # open the interactive application
jgraph -batch "let x = 1:10; disp(sum(x))"      # run, log to stdout, exit
jgraph -batch "analysis.m" -logfile run.log     # run a MATLAB (or .jgs) file, tee to a log
jgraph -r "plot(1:10)"                          # run, then leave the session open
jgraph -help                                    # the flag reference, plus the scripting guide
```

| Flag | Behaviour |
| --- | --- |
| `-batch "statement"` | Runs non-interactively, logs to standard output, and exits. |
| `-r "statement"` | Runs the statement, then keeps the interactive session open until the script calls `exit`. |
| `-logfile "path"` | Also writes all output, results and errors to a text file. |
| `-showfigures` | With `-batch`: show figures in standalone windows (no main window) instead of suppressing them; the process exits when the last one closes. |
| `-sd "directory"` | Use this directory instead of the shell's current one for relative paths. |
| `-h`, `-help` | Show the startup options and open the scripting guide. |

The statement is JGS source unless it names a file that exists, in which case that file runs and its
extension picks the language (`.jgs`, `.m`, `.csx`/`.cs`, `.py`). Relative paths — including
`exportfigure`'s output — resolve against the shell's current directory. Exit codes: `0` finished,
`1` failed, `2` bad command line, or whatever the script passed to `exit(n)`.

## Themes and plugins

```csharp
Theme.Presentation.Apply(figure);   // large, bold, saturated — for slides
Theme.Ieee.Apply(figure);           // compact Times New Roman — for two-column papers

// Discover plugins (from a folder of DLLs) and list every theme they contribute.
PluginRegistry registry = PluginLoader.LoadDefault(pluginDirectory: "plugins");
foreach (ITheme theme in registry.Themes)
{
    Console.WriteLine(theme.Name);   // Light, Dark, Presentation, IEEE, …plus any plugins
}

// A plugin contributes named themes and colormaps.
public sealed class MyThemePlugin : IPlugin
{
    public string Name => "My Theme Pack";
    public string Version => "1.0.0";
    public void Configure(IPluginRegistry registry) => registry.AddTheme(myTheme);
}
```

In the figure window, the **Theme** selector lists everything in the registry, so a plugin's theme
appears with no code change.

## Settings

**Tools → Options** (the **Options…** button on the figure toolbar, or the script workspace's View
menu) saves your preferences to `%AppData%\JGraph\settings.json`: whether `.jgs` scripts require `let`
and index from 0 or 1, the default figure theme and new-script language, the default script folder, and
which discovered plugins to load. The JGS language options take effect on the next run; disabling a
plugin takes effect on the next launch. MATLAB (`.m`) files are unaffected — they always run with
MATLAB's own rules. The command-line launcher reads the same file, so a `-batch` run honours your
plugin and language choices too.

## Solution layout

| Project | Responsibility |
| --- | --- |
| `JGraph.Core` | Object model, primitives, styles, data-series abstraction, invalidation, undo. |
| `JGraph.Math` | Scale transforms, coordinate mapping, tick generation, decimation. |
| `JGraph.Signal` | Signal processing (FFT, windows, spectrum, spectrogram, transfer functions) for the engineering plots. |
| `JGraph.Numerics` | Flat packed numeric storage (managed / native / memory-mapped by available RAM) and chunked SIMD kernels for large script arrays. |
| `JGraph.Data` | Tabular data: an immutable column `Table`, CSV/TSV/xlsx/clipboard readers with type inference, and the import-wizard model. |
| `JGraph.Rendering` | Rendering abstractions (`IRenderContext`), figure renderer, layout. |
| `JGraph.Rendering.Skia` | SkiaSharp implementation of `IRenderContext`. |
| `JGraph.Export` | PNG/JPEG/BMP/TIFF/SVG/PDF export through the shared renderer. |
| `JGraph.Serialization` | Versioned `.graph` document format (JSON) for saving, opening, and copy/paste. |
| `JGraph.Plugins` | Plugin discovery/registration; the theme + colormap registry and the built-in Light/Dark/Presentation/IEEE themes. |
| `JGraph.Objects` | Concrete plots (line, scatter, bar), annotations, and the fluent OO API. |
| `JGraph.Interaction` | UI-independent interaction modes, selection, editing descriptors, navigation. |
| `JGraph.Api` | Functional `JG` facade. |
| `JGraph.Scripting` | Scripting hosts: the `IScriptEngine` seam with a Roslyn C# engine, a pythonnet (CPython) engine, and the built-in **JGS** language (a self-contained interpreter), all driving the `JG` API and the table readers. |
| `JGraph.Controls` | WPF `FigureControl`, property inspector, plot browser, input adapter, and the script editor. |
| `JGraph.Application` | MVVM figure window and DI composition root; also runs `-batch -showfigures` and `-r`. |
| `JGraph.Cli` | `jgraph.exe` — the command-line launcher. Runs `-batch` headlessly (no WPF, no display) and starts the application for the modes that need a window. |
| `JGraph.Demo` | Example gallery. |
| `JGraph.Tests` / `JGraph.Benchmarks` | Unit tests and performance benchmarks. |

## Build, test, run

```sh
dotnet build JGraph.sln
dotnet test tests/JGraph.Tests/JGraph.Tests.csproj
dotnet run --project demo/JGraph.Demo          # example gallery
dotnet run --project src/JGraph.Application     # interactive figure window
dotnet run --project src/JGraph.Cli -- -help    # the command-line launcher
dotnet run -c Release --project tests/JGraph.Benchmarks   # decimation benchmarks
```

Requires the .NET 8 SDK (or newer) on Windows (the UI projects target `net8.0-windows` / WPF).

## Documentation

- [Architecture overview](docs/architecture.md)
- [Importing and graphing data — a walkthrough](docs/import-guide.md)
- [Example scripts (C#, Python, JGS)](examples/)
- [Architecture Decision Records](docs/adr/)

## License

MIT.
