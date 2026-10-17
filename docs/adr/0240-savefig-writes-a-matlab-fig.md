# ADR 0240 — `savefig` writes a MATLAB `.fig`: the struct tree for MATLAB, this build's document beside it: open item 77

## Status

Accepted, 2026-10-17. The twenty-seventh batch of the open-items work
(`docs/plans/open_task_chips_09_12_2026.md`, gitignored): item 77, found in app-building stage U11
(ADR 0210). The recordings are the probes in `tools/matlab-checklist/ui-probes/oi77/`, all
headless, 2026-10-17: `oi77_shapes` (what R2025b's own `hgsave` writes, and what it rebuilds from
the struct tree alone), `oi77_hand` (107 trees built by hand: what the loader needs, tolerates and
crashes on), `oi77_fh` (how R2025b writes a function handle into a MAT-file) and `oi77_verify`
(R2025b opening, listing, running and printing the files JGraph wrote with `oi77_make`).

## Context

Since ADR 0060 `savefig`, `hgsave` and `saveas(…, '.fig')` wrote this build's own `.graph` document
under the `.fig` name, which MATLAB cannot open; ADR 0210 taught `openfig` and `hgload` to read
MATLAB's own files - the struct tree `hgS_070000` that `hgsave` and every release up to R2024a
write, and the subsystem form R2025b's `savefig` writes - and recorded that R2025b still reads a
file holding the struct tree alone (`u11_hgsonly`). Item 77 asked for that form from `savefig`, and
for a decision: whether `openfig` then tells the two kinds of file apart by more than their first
bytes, or `savefig` chooses by an option.

What the document holds and the tree cannot is real: the stress scripts reopen figures with
annotations, polar axes, heat maps and a figure's whole family to the same pixels (`stess_32`,
`stess_47`, `stess_50`, `stess_55`), and the tree has no node for several of those kinds and no
place for this build's own properties. So the file had to serve both readers.

## Decision

### One file, two copies

A `.fig` this build writes is a level-5 MAT-file holding two variables: **`hgS_070000`**, the struct
tree MATLAB rebuilds the figure from, and **`jgraph_document`**, this build's `.graph` document as
UTF-8 bytes in a `uint8` column. MATLAB reads the tree and ignores the second variable (measured: a
second variable in the file changes nothing, `oi77_hand` B7); `openfig` and `hgload` here read the
document back whole when the file holds one, and go through the tree or the subsystem as ADR 0210
had them when it does not (a file MATLAB wrote). A `.graph` document under a `.fig` name still
opens by its first bytes. So the kinds are told apart by what the file holds, not by an option:
`savefig` has nothing to choose, and `'compact'` is accepted as before and changes nothing.
`savefigure` and a `.graph` name write the document alone, as they did, and a save still sets the
figure's `FileName` to the name it was given (ADR 0060), which R2025b's `savefig` and `saveas` do
not set at all (measured; a kept extra, below).

The writer is `JgsFigFile.Write.cs`; the reader side of the same file (`JgsFigFile.Document`,
`JGraphScriptGlobals.loadfigure(document, origin)`) sits beside the ADR 0210 reader. The parity
test host gained the figure file services, so a fixture can `savefig` and `openfig` as it can in
MATLAB.

### The tree, in hgsave's shapes

`oi77_shapes` had R2025b's `hgsave` write six figures (lines, labels, a legend, a text, an image
with a colour bar; nine chart kinds; surfaces, patches, groups, transforms, lights, images; a
classic GUI; annotations and a polar axes; four kinds of callback), stripped each to its tree and
reopened it: **R2025b rebuilds everything from the tree alone**, legend and colour bar, every chart
kind, the annotations, the polar axes and the callbacks included. `oi77_hand` then found what a
tree must have:

- **Nodes** of `type`, `handle`, `properties`, `children`, `special`; children in a column, oldest
  first; a leaf's `children` and `special` 0-by-0 doubles; a missing field is "Invalid structure
  format for graphical object". An **axes' `special` is four places** (title, x, y and z label
  among its children, 1-based, 0 for none): an empty one is an **access violation** in R2025b,
  which is what crashed the first hand-built tree. Handles are written fractional
  (`k + 0.0001220703125`) but a figure's, as hgsave's are; the loader took NaN, duplicate and
  integer handles too.
- **Type words** of the old graphics system, as hgsave writes them: `graph2d.lineseries` (a chart
  line; `line` makes a primitive), `specgraph.barseries`, `.scattergroup`, `.stemseries`,
  `.stairseries` (the word `stairs` is unknown, `stair` known), `.areaseries`, `.errorbarseries`,
  `.quivergroup`, `.contourgroup`, `graph3d.surfaceplot`, `histogram`, `scribe.legend`,
  `scribe.colorbar`, `scribe.textbox`, `scribe.scribeellipse`, `scribe.scriberect`,
  `scribe.arrow`, `scribe.doublearrow`, `scribe.textarrow`, `scribe.line`,
  `uitools.uibuttongroup`, `matlab.graphics.axis.PolarAxes`; the modern words work for the charts,
  the legend, the colour bar, the polar axes and the button group as well.
- **Properties:** every listed name a script could set, that MATLAB does not compute (a limit, a
  tick, a tick label, a camera, an aspect ratio, a font size, a colour map or a colour bar's
  ticks whose `Mode` is `'auto'`, left for MATLAB to compute again: a value followed by its mode
  `'auto'` comes to the same, B5a), that a MAT-file can hold, and that does not name another
  object. The look is written as this build drew it, line widths, grids and boxes included,
  because this build's defaults are not MATLAB's factory values (a line 1.5 wide, a grid on, an
  image scaled) and the loader **passes over a name it does not know, a read-only one, a bad
  value and a wrong type** (B2, B3, C20, C21) and takes the newer names (C22). `Units` and
  `Style` go first, `Value` and `Position` last; an axes' `Position` is always written (its place
  is what a subplot is).
- **A legend** is a `scribe.legend` among the figure's children after its axes, with `Axes` and
  `PlotChildren` (the handles the loader connects it by), `String`, and the application data its
  post-deserialize function reads (`NonDataObject`, `PostDeserializeFcn` `@legendpostdeserialize`,
  `PlotChildren`, `PeerAxes`). Without the application data, or without `PlotChildren`, the
  loader is an **access violation** (B9b, B9e, B9z2, B9z5); with the function alone the rows read
  `data1`, `data2` (B9z4). A legend that lists nothing is left out. **A colour bar** is a
  `scribe.colorbar` with `Axes`, its `Limits`, and the limits of the axes it is made of (the span
  along it, `[-1 2]` across), without which the loader refuses with
  `MATLAB:hg:shaped_arrays:LimitsPredicate` (C4–C7). Neither carries a `Position` unless its
  `Location` is `manual`: a position beside a location word had MATLAB squeeze the axes for a bar
  it then placed elsewhere.
- **A bar** needs `BarPeers = 0` and **an area** `AreaPeers = 0`, or the loader leaves a
  `GraphicsPlaceholder` (B11, C10, C15); **a scatter** its `SizeData` (and its colours), or "Data
  lengths must match" (B11, C16–C18); a patch given by faces and vertices gets those alone.
- **A context menu** is connected through the menu node's `special`, which lists the handles of
  the objects that use it (hgsave's form; a handle written as `UIContextMenu` or `ContextMenu` is
  not connected, B15a–c); **a button group's selection** is the radio whose `Value` is 1 (hgsave
  writes no `SelectedObject`). The reader learned the menu's `special` too.
- **Annotations** sit on one `scribe.scribeaxes` overlay (`Tag` `scribeOverlay`, handle hidden);
  one written straight under the figure is not made (B19). **A polar axes** is written by its
  class name with its lines' `ThetaData` and `RData` (B18); its labels are not written.
- **Function handles** in MATLAB's own form (`oi77_fh`): the function element holds one nested
  struct of `matlabroot`, `separator`, `sentinel` `'@'` and `function_handle`, the struct
  `functions` reports. This build's own function element (ADR 0167) makes the **whole file
  unreadable** to MATLAB ("Cannot read file"), so `MatFileWriter` writes MATLAB's form on request
  (`matlabFunctions`). A named handle and a cell callback load and run. An anonymous function's
  workspace is an object of MATLAB's own: the cell of one struct `functions` answers in its place
  is an access violation, and no workspace loads as an `UNKNOWN Function`. So **a callback that is
  an anonymous function capturing nothing is written as the command that re-makes and calls it**
  (`feval(str2func('…'), gcbo, [])`, which runs: `oi77_verify`), and **one that captured
  variables is left out** and named in the save's warning. The reader accepts the one-struct
  cell as a workspace.
- **Kinds the tree has no node for** - a heat map, a pie, a box chart, a bubble legend, the app
  components, a tiled layout, a data tip - are left out of MATLAB's copy and named, with the
  capturing callbacks, in one warning (`JGraph:savefig:NotInMatlabCopy`); the document keeps them.

### The reader

`JgsFigFile.Read` maps the old type words (`specgraph.barseries` → `bar`, `graph3d.surfaceplot` →
`surface`, `uitools.uibuttongroup` → `uibuttongroup`, `matlab.graphics.axis.PolarAxes` →
`polaraxes`, and the rest), so a file of either writer names its kinds alike; the builder still
makes only the kinds ADR 0210 listed, and names the others in its warning (open item 76). The
builder passes over any refusal of a saved value, in a model's words as in a script's, as R2025b's
loader does.

## Measured

- **`oi77_shapes`:** six hgsave figures, each reopened from its tree alone with every object back
  (the type listings and tags in the `.out`); the trees dumped node by node (`u11dump`).
- **`oi77_hand`:** 107 cases; the ones above and, besides: a mode after its value (B5a–c), units
  before or after a position (B6), a second variable (B7), labels by `special` (B8), the chart
  words (B11), text, patch by faces and by coordinates, controls, a surface's view, an image's
  `CLim` and `YDir`, rectangles, groups, transforms, lights (B12), an axes' and a figure's looks
  (B13, B14), menus, tables, toolbars, panels, button groups (B15), char `CreateFcn`s running
  (B16), `'invisible'` over a visible tree (B17), the grafted hgsave legend and colour bar nodes cut
  down (B9z, C1–C3). Six cases are access violations, recorded above.
- **`oi77_verify`** over the files JGraph wrote (`jg_plots`, `jg_gui`, `jg_annot`, `jg_odd`,
  `jg_fh_named`, `jg_fh_cell`, `jg_fh_anon0`, `jg_fh_anon`): R2025b lists the figure, the four
  axes with their positions, the lines with colour, style, width and marker, the title and labels,
  the text, the legend on its axes, both colour bars with their limits, the image scaled, the
  surface in its view, the bar, the scatter with its sizes and the stairs; the panel and button
  group with their titles, every control with its style, string, value, units and position, the
  edit box's context menu, the menus with their accelerator, the table, the toolbar and its
  toggle's state; the three annotations, the polar axes with its zero at the top and its line's
  `RData`, the patch, the rectangle and the group's line; the named, the cell and the capture-free
  anonymous callbacks running; the heat-map figure without its heat map; and prints each figure
  (`rt/jg_*_matlab.png`, set beside JGraph's `rt/jg_*_jgraph.png`).
- **Unit tests** (`FigFileWriteTests`): the file's two variables and the document; the tree's
  shapes node by node (the column, the leaves, the special, the legend's handles and application
  data, the menu's special, the control's order); computed values out and the look in; the
  callbacks' three fates; the document copy back through `openfig` with the warning; the tree
  alone reopening here; `saveas` and `hgsave` the same form, a `.graph` name the document; a
  file MATLAB wrote carrying no document.
- **Fixture `oi_fig_struct_form`** (47 rows, 3 divergences): `helpers/oi77_jgraph.fig` (written
  by this build from `helpers/oi77_figure.m`) opened in R2025b from its tree and here from its
  document, and the same figure saved and reopened live in each engine: the figure, the four
  axes, the lines, the title and labels, the text, the legend, the image and its colour bar, the
  colour map, the bar, the scatter, the stairs, the patch, the controls and the text box agree;
  the divergences are the image axes' position beside an outside colour bar (ADR 0230's) and the
  text box's size in MATLAB's reading of the tree (its `FitBoxToText`, item 139).
- **Stress:** 89 of 89 scripts, the pixel round trips of `stess_32`, `stess_47`, `stess_50` and
  `stess_55` among them, through the document copy.

## Not measured

- **MATLAB's window** on a file this build wrote: the prints are headless (`print -dpng`), and
  R2025b refuses to print a figure with controls headless ("UI components are not supported").
- **Any release but R2025b** reading the file. The form is the one every release reads
  (`hgS_070000`); none was run.
- **Loading the file's handles into a GUIDE app** (`gui_mainfcn` over a `.fig` this build wrote).

## Consequences

- A figure made here opens in MATLAB with its plots, labels, legend, colour bars, controls, menus,
  annotations and callbacks, and opens here as it was, from the same file.
- A `.fig` is larger than the document alone: the tree writes the look whole (a line is some forty
  properties). `MatFileWriter` writes MATLAB's function form on request; `MatFileReader` reads a
  one-struct cell as a workspace. `JgsFigFile` is two files: the reader and the writer.
- The parity test host has figure file services.
- The two gaps `oi77_make` met in this build's own makers are open items 136 (`plot` refuses
  `'Tag'` among its options) and 137 (`line(g, x, y)` with a group parent); the image axes' limits
  are item 138, the writer's leftovers item 139, and the tags a text, a rectangle and an annotation
  lose through this build's own document (met recording the fixture) item 140.

## Divergences

- **A `.fig` this build writes holds two copies, and MATLAB and this build read different ones.**
  MATLAB's copy leaves out the kinds the tree has no node for (a heat map, a pie, a box chart, a
  bubble legend, the app components, a tiled layout, a data tip), a callback that captured
  variables, a colour bar's label, a legend's title, an axes' subtitle and a polar axes' labels;
  a capture-free anonymous callback is a char command there and a function handle here. The save
  names what it left out in one warning.
- **MATLAB's reading shows this build's look, not MATLAB's defaults:** a line 1.5 points wide, a
  white figure, this build's colour map, a grid where this build drew one. R2025b's own `hgsave`
  writes its values with their modes.
- **An image's axes reads `[0 4.5]` by `[0 5]` in MATLAB from this build's tree** where R2025b's
  own `imagesc` sets the limits by hand to `[0.5 4.5]`: this build leaves them automatic at
  `[0 5]` (open item 138).
- **A save sets the figure's `FileName` to the name it was given;** R2025b's `savefig` and
  `saveas` leave it empty (only `openfig` sets it there). Kept since ADR 0060 for the script that
  saves again over the same file.
- **The tree alone, read here, lacks the chart kinds** (bar, scatter, stairs, stem, area, error
  bars, histograms, quivers, contours): the builder makes only ADR 0210's kinds (open item 76).

## Still open

- The chart kinds in the reader's builder (item 76); `plot`'s options and the primitives' group
  parent (items 136, 137); an image's axes limits (item 138); the writer's leftovers (item 139);
  the tags the document drops (item 140).
