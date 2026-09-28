# ADR 0183 — methodsview, the app's view of external values, and the interop gate

## Status

Accepted. Stage I10, the last stage of the .NET and shared-library interop plan
(`docs/plans/net-and-shared-library-interop-plan.md`), after stage I9 (ADR 0182, c75e210). One
commit, this ADR's. It adds `methodsview` and `libfunctionsview`, teaches `methods` libraries,
pointers and libstructs, and gives the app four things:
- a window for those tables;
- a Workspace-pane summary of .NET and C library values;
- editor completion for .NET names and `calllib`'s strings;
- a console double-click that opens the `file(line,col)` an error names.

It also writes the documentation, the stress script and the timing, and closes the plan.

## Context

Stages I1–I9 built the interface itself: .NET objects, assemblies, imports, arrays, generics,
enums, events, delegates and inline C#, then `loadlibrary`, `calllib`, `libpointer` and `libstruct`
over a native host process. The plan left for its last stage what a user sees around the language:

- `methodsview` and `libfunctionsview`, which R2025b ships as function files that open a window;
- how the app shows a value it cannot grid;
- what the editor offers after `System.`;
- what the console does with the `file(line,col)` layout that `jgraph.net.compile`'s errors use
  (stage I6 finding 5).

It also left the documentation, the coverage split, a stress script and the timing.

Stage I9's findings bore on this stage twice. `reshape`, `setdatatype` and the other pointer
methods are methods, not builtins, so completion offers them from the value rather than the
catalog. And JGS reaches pointer values through `get` and `set`.

## Decision

### `methodsview` and `libfunctionsview` are R2025b's files, transcribed

`methodsview.m` and `libfunctionsview.m` are readable in R2025b's toolbox. JGraph follows them step
by step:
- **Arguments.** An option is `'noUI'` or `'libfunctionsview'`, in any case. Each refusal has
  R2025b's identifier and sentence (`nargin`, `InvalidInput`, `InvalidInputOption`,
  `TooManyOutputs`, `InvalidNumberOfOutputs`, `UnknownClassOrMethod`, and libfunctionsview's
  `NumberOfInputArguments` and `InputType`).
- **Imports.** A bare class name is resolved through the caller's imports.
- **The table** is built from `methods(x, '-full')`. Each line splits into qualifiers, return type,
  name, arguments and the defining class and name. The columns are reordered, the rows sorted by
  name with an ordinal sort, and only the columns some row fills are kept.
- **`libfunctionsview`** drops the "Inherited From" column and says "library". Every `lib.` class,
  `lib.pointer` included, is titled "Functions in library …", because `makeTitle` tests the prefix.
- **`[h, d] = methodsview(x, 'noUI')`** answers the headers as a string column and the rows as a
  string matrix, as the file does. It is internal in R2025b, but it makes the table observable
  under `-batch`, which is how the fixture pins it.

**One step is not transcribed.** `methodsview.m` blanks the "Inherited From" of every line named
like the first line whose defining name ends in its own name, and which line that is depends on the
order `methods` hands back. R2025b does not keep that order: `lib.pointer`'s `addlistener` rows
read `handle` in one run and nothing in the next (probe_views, then the recording), and
`lib.jg_point`'s `notify` rows changed between two recordings. JGraph names the class every
inherited line comes from, which is also the true answer. See Divergences.

**The window** is the host's. A new `IScriptTableViewer` on `ScriptContext` (`TableViewer`) takes
the title, the headers and the rows:
- The app's `AppScriptTableViewer` opens a `MethodsTableWindow`: a read-only WPF `DataGrid` over a
  `DataTable`, so every column sorts, with the name column bold. It opens one window per call and
  does not wait for the window to close, as R2025b's `uifigure` does not.
- The CLI and batch runs have no viewer and print the same table with aligned columns.
- R2025b under `-batch` was not measured, because its table is a `uifigure` and the probe would
  open a window.

### `methods` of a library, a pointer and a libstruct

`probe_views2` recorded what `methods` says of `lib.jgtestlib`, `lib.pointer` and `lib.jg_point`,
by value and by name. `LibMethodsListing` gives each one.

**A library:**
- The printed listing has only a `Static methods:` part, in the 180-column layout.
- Each `-full` line starts with `Static`. A numeric parameter reads `double scalar rhs1`, and an
  exported variable's output reads `lib.pointer scalar`, where `libfunctions -full` has plain
  `double` and `lib.pointer`. This is `LibraryModel.MethodsSignature`.

**A pointer and a libstruct:**
- Their own names print in columns, followed by the line about `handle`.
- The `-full` listing is one list ordered by name, with `handle`'s and `matlab.mixin.SetGet`'s
  lines marked `% Inherited from …`. The cell `methods` answers leaves those notes off.

**A name that names nothing:**
- `methods('lib.nolib')` prints `No class 'lib.nolib'.`, and answers `[]` when asked for a value.

### The Workspace pane sums up an external value without running it

The pane used to show a value's `disp` text. For a .NET object that runs every property getter
(`NetDisplay`), and for a libstruct it reads the native host, both after every statement. So:
- Each external value now has a `Summary()` that runs none of the object's code and reads no native
  memory:
  - a string's text and an enum's member name;
  - a .NET array's element count and a Nullable's value;
  - a boxed primitive's value, formatted by the runtime rather than the object;
  - a pointer's type, with NULL or its size;
  - "deleted" for a deleted handle.
- The pane's Value column reads `1×1 System.String: hi`, and its Type column still says what
  `class` says. Grid cells that hold an external value use the same text.
- A test holds that a counting getter and `ToString` are not called.

### The variable editor refuses an external value by name

An external value's raw value is a `ScriptExternalValue` marker (its class, summary and kind),
not a grid. Opening one in the data viewer puts a sentence in the status bar that says what it is
and where to look instead. For a .NET object that is `methods(x)` and `properties(x)`, for a
pointer `x.Value`, and for a libstruct `get(x)`.

### Completion for .NET names and `calllib`'s strings

`JgsCompletionEngine.GetCompletions` takes an optional `IScriptCompletionSource`. The console's
MATLAB session is one, and `InteropCompletion.Framework` answers without a session. In a MATLAB
buffer:
- **After `System.`** (any dotted name ending in a dot) it offers what follows: namespaces and
  types from `NetCatalog.Children`, then static members and enum members.
- **After any other dot** it offers nothing, where it used to offer every builtin.
- **Inside the first string of `calllib`, `libfunctions`, `libfunctionsview`, `unloadlibrary` or
  `libisloaded`** it offers the session's loaded libraries.
- **Inside `calllib`'s second string** it offers that library's functions, each with its
  `libfunctions -full` signature.

Indexing the framework reads every framework assembly, which the UI thread must not wait for. The
first dot therefore starts the index on a pool thread and offers nothing. Once the index is built,
a dot offers the names; in practice that is the next keystroke. The pointer methods come from the
value, as stage I9 found, so the catalog does not list them.

### The console opens the place an error names

`ScriptLocation.Find` reads `C:\work\f.m(4,12): …` and a C# compiler's
`C:\src\Helper.cs(12,5): error CS…`. Double-clicking such a console line opens that file at that
line. A bare `(4,12): …`, at the start of the line or after a space or colon, goes to that line of
the active document. A location glued to something else is not one: `<string>(1,40)` from C#
compiled from text, or `f(2,3):`.

## Probes

Three new probes sit beside the others and run through `run-probes.ps1`. None opens a window.

- `probe_views` records:
  - methodsview's `noUI` rows for `System.Math`, three `JGTest` types, `System.Text.StringBuilder`,
    `lib.jgtestlib`, `lib.pointer` and `lib.jg_point`;
  - the two-output `methods -full`;
  - the refusals, although those were asked through `ip_pr`, whose output request met
    `TooManyOutputs` first.
- `probe_views2` records `methods` of a library, a pointer and a libstruct, printed and as cells,
  with and without `-full`.
- `probe_views3` records:
  - the refusals asked without an output;
  - `methods` of an unloaded library and of an unknown class;
  - `methodsview` under an import;
  - a libstruct's constructor rows.

The window title strings come from R2025b's message catalog (`resources/MATLAB/en/methodsview.xml`):
`Methods for class {0}`, `Functions in library {0}` and the six column labels.

## Measured

Warm, Release, this machine, three passes after a warm-up in each engine, with
`tools/interop/timing_interop.m` (new) running the same file in both:

| Per call | JGraph | R2025b |
|---|---|---|
| `System.Math.Max(3, 7)` | 2.4–2.5 µs | 8.5–10.6 µs |
| `sb.Append('a')` on a `StringBuilder` | 1.8 µs | 15.8–20.7 µs |
| `List<double>.Add(k)` | 0.6 µs | 6.2–6.4 µs |
| `List<double>.Item(k)` | 0.8 µs | 7.9–8.6 µs |
| `sb.Length` | 0.34–0.35 µs | 1.9–2.1 µs |
| `calllib(lib, 'jg_double', 2.5)` | 14.5–15.5 µs | 2.2–2.6 µs |
| `jg_scale_double` on a 1e6-double array, in and out | 12.1–14.0 ms | 3.7 ms |

R2025b's column is its three passes, which were steady. JGraph ran the script twice. Its .NET rows
are pass 3 of each run: until the tiered JIT settles, an early pass runs up to 4x slower (one pass 2
timed `Math.Max` at 11 µs). Its `calllib` rows are all six passes.

- **.NET calls** are 3–9x faster than R2025b's.
- **A scalar `calllib`** is about 6x slower, and **a 1e6-element array** about 3.5x. Both are over
  the plan's 3x line, so the round trip to the native host has a follow-up of its own (open-items
  entry 18). ADR 0182 recorded 34–35 µs for the scalar call with a script it did not keep; this
  script is kept, so the next measurement is comparable.

## Live checks for the user

Batch runs cannot see these; they need the app:

1. `methodsview System.Math` and, after
   `loadlibrary('jgtestlib', @jgtestlib_proto)` in `tests/JGraph.Tests/MatlabParity/fixtures/interop`,
   `libfunctionsview jgtestlib`. Each should open a window with a sortable table and bold names,
   titled "Methods for class System.Math" and "Functions in library jgtestlib".
2. At the console, make a `System.IO.FileSystemWatcher` on a folder, give it
   `EnableRaisingEvents = true` and an `addlistener(w, 'Created', @(s, e) disp(char(e.Name)))`, then
   create a file in the folder from Explorer. The name should print at the idle prompt without a
   statement being run.
3. `calllib('jgtestlib', 'jg_crash')`. The app should survive and the console should show
   `JGraph:loadlibrary:HostExited`. The next `calllib` should work.
4. `calllib('jgtestlib', 'jg_sleep', 60000)`, then Stop. The run should end as stopped within a
   moment.
5. In a `.m` editor tab, type `System.` and then `IO.` (the first dot may offer nothing while the
   framework is indexed). After loading a library at the console, `calllib('` should offer it and
   `calllib('jgtestlib', 'jg_` its functions.
6. `sb = System.Text.StringBuilder('x'); lp = libpointer('doublePtr', 1:3);`. The Workspace pane
   should show `1×1 System.Text.StringBuilder` and `1×1 lib.pointer: doublePtr, 1×3`, and
   double-clicking either should explain itself in the status bar.
7. Save a `.cs` file with a syntax error beside a script and pass its name to
   `jgraph.net.compile`. Double-clicking the error line should open the file at that line. C# given
   as text reports `<string>(1,40)`, which names no file, so double-clicking it does nothing.

## What moves

- **New `JgsBuiltins.MethodsView.cs`:**
  - `methodsview`, `libfunctionsview` and `MethodsTable`, the transcription;
  - `LibListingOf`, which `methods` asks about libraries, pointers and libstructs;
  - the printed-table form.
- **New `Jgs/Native/LibMethodsListing.cs`:** the `-full` lines of `lib.pointer` and a libstruct
  class; a library's `Static` lines; the listings.
- **`LibraryModel.MethodsSignature` and `LibTypes.IsNumeric`.**
- **`methods`** (`JgsBuiltins.Classes.cs`) answers and prints for the three.
- **`IJgsExternal`** gains `Summary()` and `Kind`, answered by `NetObject`, `LibPointer` and
  `LibStructValue`.
- **`JgsRunner`** uses the summary for the pane and the grid cells, and hands a
  `ScriptExternalValue` as the raw value.
- **New public types in `JGraph.Scripting`:**
  - `IScriptTableViewer` and `ScriptContext.TableViewer`;
  - `ScriptExternalValue` and `ScriptLocation`;
  - `Completion.IScriptCompletionSource`, `Jgs.Completion.InteropCompletion`, and the
    `CompletionItemKind` values `Namespace`, `Type`, `Member` and `Library`.
- **`NetCatalog`:**
  - `Children` and a children map in each index, dropped when an assembly is added;
  - `FrameworkChildren`, `FrameworkType`, `FrameworkReady` and `WarmFramework`.
- **`JgsReplSession`** is an `IScriptCompletionSource`.
- **The app:**
  - `AppScriptTableViewer` and `MethodsTableWindow`;
  - `TableViewer` on both contexts it builds;
  - `CompletionLiveNames` wired to the console session;
  - `CompletionSupport` opening on a `.` in MATLAB buffers;
  - the data viewer's refusal;
  - the console's `OnConsoleDoubleClick`.
- **Fixtures:**
  - `views_methods` (new, 61 rows): refusals, `noUI` tables and `methods` listings, recorded from
    R2025b and stamped in both lanes. Its rows that list or count the library's functions are
    `div=ADR0181`: JGraph loads `jg_point_make`, a function returning a struct by value, which
    R2025b cannot;
  - `net_jgs_smoke` gains `methods_library` and `methodsview_noUI`.
- **Tests:** `InteropViewsM183Tests` (9 tests, 12 cases):
  - the table handed to a viewer, and libfunctionsview's columns;
  - the printed table;
  - every inherited line naming its class;
  - the pane's summaries with no getter run;
  - dotted completion, including after a non-.NET dot;
  - `calllib` completion from a live session;
  - console locations (4 cases and 4 misses).
- **Documentation:**
  - `jgs-scripting-guide.html` gains "Calling .NET", "Calling a C library" and "Inline C#", with a
    trust statement: what you load runs with your permissions, and .NET runs in JGraph's process
    while C runs in the host;
  - `matlab-builtin-coverage.md` writes the interop section's 20 missing names out one by one (every
    `NET.*` builtin is now done), so the verifier counts it for the first time, and splits its prose
    into what the plan did and what is out of scope, with the reason for each;
  - `matlab-toolbox-coverage.md` moves `methodsview`.
- **Tools:** `tools/interop/timing_interop.m`, and the three probes.
- **Stress:** `stess_88.m`, outside git, has ten sections: Stopwatch, FileSystemWatcher events
  during a loop, StringBuilder, a `List<double>` round trip, Regex, the environment, a .NET
  exception, inline C#, methodsview's table, and `GetTickCount64`/`GetSystemInfo` from kernel32
  through a hand-written header, which needs a C compiler and says so when there is none. All ten
  pass.

## Consequences

- Every name the interop plan set out to build exists. What is left of MATLAB's interop list is out
  of scope, and the coverage document says why for each family.
- A window-opening builtin now has a host interface to reach for. A future `inspect` or
  `openvar`-like table can reuse `IScriptTableViewer`.
- The Workspace pane no longer runs user code. A .NET property getter with side effects, or one
  that blocks, no longer runs after every statement.
- A dot in a MATLAB buffer no longer offers the builtin list. It offers .NET names or nothing.

## Divergences

- **`methodsview`'s "Inherited From" names the defining class on every inherited row.** R2025b
  blanks the rows named like whichever row its unordered `methods` list happens to put first,
  which changes from run to run (`views_methods` `mv_pointer`, `mv_pointer_object`, `mv_struct`,
  `mv_struct_object`).
- **Without a window, the table is printed.** The CLI and a batch run print `methodsview`'s and
  `libfunctionsview`'s table. R2025b shows a `uifigure`, and what it does under `-batch` was not
  measured, because measuring it opens a window.

## Still open

Four entries in `docs/plans/open_task_chips_09_12_2026.md`:
- **18:** `calllib`'s round trip is past 3x R2025b.
- **19:** the data viewer says "no tabular view" for an oversize numeric array.
- **20:** `mfilename` answers `''` in a script started by `run` from the command line.
- **21:** an unsuppressed multiple assignment echoes nothing.
