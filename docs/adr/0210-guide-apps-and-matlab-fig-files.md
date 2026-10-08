# ADR 0210 — GUIDE apps and MATLAB `.fig` files

## Status

Accepted, 2026-10-07. Stage U11 of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored). It builds on ADR 0060 (`savefig` and
`openfig` over this build's own `.graph` document), ADR 0167 (V6: the MAT reader's objects and
function handles, the workspace binder), ADR 0198–0201 (U1–U4: `uicontrol`, containers, units,
`guidata`/`guihandles`, appdata, `HandleVisibility`) and ADR 0051 (a graphics handle is a number).

## Context

A GUIDE app is a pair: a `.fig` holding the figure and its controls, and an `.m` whose main
function hands a `gui_State` struct to `gui_mainfcn`, which opens the figure and dispatches the
callbacks stored in it back to local functions of the `.m`. R2025b removed GUIDE itself but still
runs the pairs, and still ships `gui_mainfcn` (research C §4). JGraph's `openfig` read only its
own JSON document under a `.fig` name; a MATLAB `.fig` was refused, so no GUIDE app, and no figure
anyone saved from MATLAB, could be opened.

Research C expected a `.fig` to be a MAT-file whose `hgS_070000` variable is a plain struct tree,
and so it is for files written by `hgsave` and by every release up to R2024a. **The stage's first
probe found that R2025b's `savefig` no longer writes that tree** (`u11_figs`, `u11_hgm`): its
`hgS_080000` is an empty 0×0 struct, and the whole figure lives only in `hgM_080000`, whose
`GraphicsObjects` is an MCOS object whose `Format3Data` is the `matlab.ui.Figure` itself,
serialized in the MAT-file's subsystem. Meeting the plan's exit criterion — a figure saved by
R2025b's `savefig` opens with its axes and lines — therefore needed a reader for MATLAB's MCOS
subsystem, which no part of JGraph had.

The probes in `tools/matlab-checklist/ui-probes/u11` measured, headless:

- **The subsystem** (`u11_hgm`, then a Python prototype over the bytes): a `uint8` variable whose
  bytes are a small MAT stream holding one struct whose `MCOS` field is the `FileWrapper__` object;
  its content is a cell whose first element is a byte table — a version, the count of names, eight
  segment offsets, the names, then the classes (package and name), the property lists of objects
  saved through `saveobj`, the objects (class, the property list each uses), and the property lists
  of the rest. A list holds (name, kind, value) triples: kind 0 is a word from the name table, 1 an
  index into the cell, 2 the value itself. A reference to objects is a `uint32` column
  `[0xDD000000, ndims, dims…, ids…, class]`. A function handle held by an object is a two-element
  cell of that mark and the struct `functions` reports; its workspace is another object whose
  `any` cell carries the struct of captured variables.
- **Graphics objects in the subsystem** save what differs from their class's default as `Name_I`
  (or `Name_IS`) beside `NameMode`; a mode marked `'manual'` with no value means the default.
  Some of what a script sees sits in helper objects: a figure's and a container's place in its
  `UnitPos` (`PositionCache` in that object's units), an axes's limits, directions and scales in its
  data space (a limit set by `imagesc` is marked through `XLimWithInfsMode`, not `XLimMode`), its
  `CLim` in its colour space, its labels on its rulers (`Label_IS`), its title in `Title_IS`;
  children are the class-qualified `SerializableChildren`, newest first; a legend and a colour bar
  are the figure's children and name their axes through `Axes_I`.
- **The struct form** keeps public property names and old handle numbers; an axes's `special` is
  `[title; xlabel; ylabel; zlabel]` as 1-based places among its children; a legend is a
  figure-level `scribe.legend` naming its axes; **children are stored oldest first**, the reverse
  of `Children` (`u11_guide`, `order7s`); an empty field is a matrix element with nothing in it;
  a function handle is MATLAB's function element, one nested struct whose anonymous text carries a
  `sf%N` slot prefix, its workspace an opaque element pointing into the subsystem.
- **`openfig`** (`u11_figs`, `u11_open`): a new figure each time, current, its `FileName` the full
  path; `'reuse'` answers the first open figure read from that file; the figure is visible as it was
  saved unless `'visible'` or `'invisible'` says otherwise; a bad option is
  `MATLAB:openfig:InvalidOption`, a missing file `MATLAB:load:couldNotReadFile` naming the file as
  given, a MAT-file with no figure `MATLAB:loadFigure:InvalidFigFile`. `hgload` answers the figure
  and a cell holding a struct of the old values of the properties it was given, `Visible` left out.
- **CreateFcns on open** (`u11_guide`): for a struct-tree file alone, parent first, children in
  `Children` order; for the subsystem form, R2025b runs one axes's lines before the figure and the
  rest in that order — the deserializer's artefact.
- **`gui_mainfcn`** (`u11_guide`, over research C's pair and four GUIDE-shaped apps): a name is a
  callback only when the second argument is a graphics object whose `Tag` and an underscore it
  contains, or when it is a `_CreateFcn` (`'nosuch_Callback'` opens the app instead); the figure is
  invisible through the opening function, which sees `HandleVisibility` `'on'` and the appdata
  `InGUIInitialization`; arguments that name figure properties are set, others ignored, `'Visible'`
  kept for the end; the figure is shown before the output function only if it was saved visible
  and not asked to stay hidden; the output function is asked for what the caller asked for, zero
  included, and no `ans` is left; a singleton reuses the open figure read from the app's `.fig` and
  runs the opening function again; an app exported to a layout function is handed `'reuse'` or
  `'new'` and keeps its own singleton; `gui_mainfcn()` is `MATLAB:minrhs`.

## Decision

### Reading MATLAB's files

- **`MatMcos`** decodes the subsystem's `FileWrapper__` cell into object records: each object's
  class and saved properties in order, words as text, references left as references
  (`MatMcos.Refs`). It re-makes a function handle an object holds through the workspace binder,
  with its captured variables, and strips the `sf%N` slot prefix (`MatMcos.FunctionText`). The
  layout was read from R2025b's own files; there is no published specification.
- **`MatFileReader.ReadWithSubsystem`** decodes the subsystem first (the header's offset at bytes
  116–123) and resolves each opaque element through it — a function handle's workspace to the
  struct it captured, anything else to its reference. The reader also learned MATLAB's own form of
  a function element (one nested struct) beside this build's, and reads an empty matrix element as
  `[]`. A file read without the subsystem still refuses an opaque element, now by its class name.
- **`JgsFigFile.Read`** reads a `.fig` into `FigNode`s: the struct tree when the file has a
  non-empty one, else the subsystem's figure. Both forms come out the same — public names, children
  newest first, an axes's labels apart, links to other saved objects by key, appdata. A function
  handle to a function that is not on the path (a legend's `legendpostdeserialize`) is kept as its
  name, which fails as R2025b's handle does: when called.
- **`JgsFigFile.Build`** makes the figure through this build's own makers and property table, so a
  file can set nothing a script could not. Each object is made with its parent and its data, then
  its saved properties are set one by one: a property whose mode is `'auto'` is left to compute
  again, a mode is never set, the names others are read against go first (`Style`, `Units`,
  `String`, `Max`, `Min`, `Data`) and `Value` and `Position` last, and a name or value this build
  refuses is left at its default. Legends and colour bars are made once their axes exist; links
  (`ContextMenu`, `SelectedObject`) once their targets do; an axes's saved limits and position are
  set again after its children and annotations; then each object's `CreateFcn` is set and run,
  parent first in `Children` order; then the figure is shown as saved or as asked. A patch saved
  as faces and vertices is made from coordinates, one column per face.
- Rebuilt: `figure`, `axes` (title and labels), `line`, `text`, `patch`, `surface`, `image`,
  `rectangle`, `light`, `hggroup`, `hgtransform`, `uicontrol`, `uipanel`, `uibuttongroup`,
  `uimenu`, `uicontextmenu`, `uitable`, `uitoolbar`, `uipushtool`, `uitoggletool`, `legend` and
  `colorbar`. Anything else is left out, and `openfig` says which kinds in one warning
  (`JGraph:openfig:NotRebuilt`).

### The builtins

- **`openfig` and `hgload`** are declared with the interpreter (`JgsBuiltins.FigFiles.cs`) and
  read either kind of file by its first bytes: a MAT-file through `JgsFigFile`, this build's
  document through the host as before. The options, `'reuse'`, `FileName`, the refusals and
  `hgload`'s second output follow R2025b.
- **`gui_mainfcn`** is a builtin written from the contract above, not from MathWorks' file. It
  looks for the app's `.fig` beside the app's file, as the app's name is found. A GUIDE options
  struct the figure does not carry is not required (R2025b's own `gui_mainfcn` fails on a layout
  function that leaves `GUIDEOptions` out).
- **`guide`** answers R2025b's `MATLAB:guide:GUIDEHasBeenRemoved`.

### What the stage changed elsewhere

- **`str2func` of a name nothing answers** is a handle refused only when called
  (`MATLAB:UndefinedFunction`), as R2025b's is: every GUIDE main function turns its first argument
  into a handle, `'Visible'` included. It reuses the late-bound handle U6 made for methods
  (`Interpreter.UnansweredHandle`). A written `@name` is unchanged.
- **A legend's `String` brings its rows up to date before it is read.** Only the renderer did, so
  every run that draws nothing — every `-batch` run — read an empty cell back from `legend(…)`.
- **`get(0, 'defaultUicontrol…')`** answers from a fresh `uicontrol`, as the line, axes, figure
  and text defaults did; GUIDE's `CreateFcn` template for an edit box asks for
  `defaultUicontrolBackgroundColor`.

## Found on the way

- **R2025b's `savefig` writes no struct tree.** See Context; the plan assumed one.
- **A script figure's `Color` is painted over in the window.** The app re-applies its theme to a
  figure it displays (`FigureViewModel.DisplayFigure`), which overwrites the figure's and every
  axes's `Color`. A GUIDE figure saved grey shows white. Not changed here: it is open item 40, a
  decision about the IDE theme against a script's colours.
- **`@nosuchfn` is refused where it is written**, where R2025b makes the handle (open item 73).
- **A `.graph` document does not keep a line's `Tag`** through `savefig` and `openfig` (open
  item 74).

## Consequences

- Research C's hand-built GUIDE pair (`myguide.m`/`myguide.fig`, written by `hgsave`) runs
  unchanged, headless and in the window: its handles, its `CreateFcn`, its callbacks through the
  stored anonymous handles and by name, its singleton.
- Figures saved by R2025b's `savefig`, and by `hgsave` and older releases, open with their axes,
  lines, labels, legends, images, colour bars, surfaces, patches and classic controls.
- The callable count is 1,222 of 2,024: `guide` is one of the 2,024 and now answers as R2025b's
  does; `openfig` and `hgload` were counted already, and `gui_mainfcn` is not among them.

## Measured

- **Headless probes** (`ui-probes/u11/`): `u11_figs`, `u11_hgm`, `u11_lte`, `u11_guide` (with the
  pair `guide/myguide.m` and the layout app `guide/myguidex.m`), and `u11_open` and `u11_guiderun`,
  run in both engines and diffed; `u11_makefix` writes the fixtures' figure files with R2025b's own
  `savefig` and `hgsave`.
- **Parity fixtures**, recorded with `-noFigureWindows`: `u11_figfile` (58 lines) over R2025b's
  figure files in both forms, and `u11_guide` (45) over research C's pair and the apps
  `u11_guide1` (struct form), `u11_guide2`, `u11_guidev`, `u11_guide0` (subsystem form) and
  `u11_guidex` (layout function). Four lines are recorded divergences.
- **Unit tests** (`FigFilesU11Tests`): the subsystem behind R2025b's wrapper, the struct form's
  empty elements and MATLAB-form function handles, the slot prefix, the model `openfig` builds,
  `str2func` of an unknown name, a legend's `String` before drawing, the root's `uicontrol`
  defaults.
- **The JGraph window check**, in the Release app under `-batch -showfigures`
  (`ui-probes/research/apps/examples/ex9_guide.m`), by UI Automation patterns only (Invoke, Value),
  the windows captured alone with PrintWindow, `workspace.json` unchanged: research C's pair showed
  "Count: 0"; Add made it "Count: 2"; typing 5 into the edit box and Add made it "Count: 7"; the
  R2025b plot figure showed both axes with the title, labels, grid, lines and markers, legend, the
  green note, the image in `hot` and its colour bar.
- This build's own document under a `.fig` name still opens through `openfig`, with `'reuse'` and
  `FileName` (checked in the CLI host; the parity test host cannot save a figure).

## Not measured

- **R2025b's look** of a GUIDE app or an opened figure in a window: no MATLAB window session.
- **A `uifigure` saved by `savefig`**, and the App Designer components in one.
- **Chart objects in a figure file** (`bar`, `scatter`, `area`, `stem`, `stairs`, `histogram`,
  `errorbar`, contours, annotations): left out with the warning.
- **A `.fig` written by `savefig(…, 'compactv7.3')`** (HDF5).
- **`load` of a `.fig` with `-mat`**, and `load` of MATLAB strings, datetimes and other MCOS
  values: the decoder is there, `load` does not use it yet.

## Divergences

- **A figure file's CreateFcns run parent first in `Children` order whatever its form.** R2025b
  runs one axes's lines before the figure when it reads its own `savefig` form, and when it reads
  an `hgsave` file (whose subsystem it prefers) (`u11_figfile`: `surf_creates`, `order_savefig`,
  `order_hgsave`). The struct form alone runs in this order in R2025b too.
- **`guide`'s message names the migration tool plainly**, without R2025b's link to a tool this
  build does not have (`u11_guide`, `guide_msg`).
- **An object of a kind this build does not rebuild is left out of an opened figure**, with
  `JGraph:openfig:NotRebuilt` naming the kinds. R2025b opens them all.
- **`gui_mainfcn` does not require `GUIDEOptions`** on the figure a layout function makes; R2025b's
  fails on one without it.
- **A handle in a list is a number** (ADR 0051): `class(h)` of an opened figure, and a callback's
  `hObject` in an error message's "input arguments of type …", read `double`.
- **`savefig` still writes this build's document** (ADR 0060): MATLAB cannot open a figure JGraph
  saved.

## Still open

- The IDE theme painting over a script figure's colours (open item 40, seen again here).
- `@nosuchfn` refused where it is written (open item 73).
- A line's `Tag` lost through a `.graph` round trip (open item 74).
- `load` of MATLAB's MCOS values, and `load(file.fig, '-mat')` (open item 75).
- Chart objects and annotations in a figure file (open item 76).
- Writing a MATLAB `.fig` (the struct form R2025b still reads) from `savefig` (open item 77).
