# ADR 0230 — R2025b's plot box in the MATLAB dialect, arrays of value objects, a table's display order, and scrolling panels and figures: open items 47, 60, 65, 87 and 105

## Status

Accepted, 2026-10-12. The seventeenth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored). Every answer was recorded from R2025b headless (probes `probe_87`, `probe_99`, `probe_65`,
`probe_60`, `probe_60b` and `probe_47` in the open-items scratch, then the fixture
`oi_axes_box_styles_tables_scroll`), and three decisions were the user's: the automatic limits stay
padded in both dialects (87), the classic colour order and the `[0.251 0.251 0.251]` ink stay in both
dialects (105), and the tick rule (99) waits on a decision this ADR states the question for. As in
every batch since 12, each item was wider than filed.

## Context

- **Item 87.** Filed as "automatic limits and the default axes `Position`": a new axes' plot box was
  the tight one JGraph has always drawn (`[0.0719 0.0625 0.9094 0.9167]` of a 640-by-480 figure),
  where R2025b's is `[0.13 0.11 0.775 0.815]` of its `OuterPosition`, scaled with a written outer box
  (`[0 0 0.5 1]` gives `[0.065 0.11 0.3875 0.815]`), unchanged by a title or a label, and shrunk only
  by a colorbar. Any script that placed a second axes by the first one's `Position` saw other
  numbers. The limits: R2025b's `XLimitMethod` defaults to `'tickaligned'` ([1 3] for `plot(1:3)`,
  rounded out to its own tick step), its `'padded'` is about seven per cent with an asymmetric outward
  rounding, and its refusal of a wrong word is `MATLAB:datatypes:InvalidEnumValue` with a prefix
  (`'tick'`) accepted; this build pads five per cent, refused prefixes in its own words, and keeps
  its own 1-2-5 ladder for `'tickaligned'`.
- **Item 65.** Filed as "StyleConfigurations as R2025b's table": R2025b's `Target` is a categorical
  over the component's target words, its `Style` an n-by-1 array of `matlab.ui.style.Style` read as
  `T.Style(1).FontWeight`; this build held text and a cell (`T.Style{1}`). Underneath: this build had
  no arrays of objects at all — `[s s2]` was refused, `s(2)` likewise — where R2025b makes a 1-by-2
  `Style` array, indexes it, grows it (`a(3) = s2`), shrinks it (`a(2) = []`), writes an element's
  property as a copy (`a(1).FontWeight = 'normal'` leaves `s` bold), refuses `[s 1]`
  (`MATLAB:class:NoConversionDefined`) and `addStyle` of an array
  (`MATLAB:ui:Table:invalidStyleObject`), and shows a style with only its set properties and an
  offer of the rest.
- **Item 60.** Filed as "finish a table's view": a sorted column changed only what the WPF grid
  showed, so `DisplayData` was `Data`, `DisplaySelection` was `Selection`, and `DisplayDataChangedFcn`
  never ran; a dragged column likewise. R2025b keeps a hidden pair, `DisplayRowOrder` and
  `DisplayColumnOrder` (public, `[]` by default, a permutation when written), behind `DisplayData`,
  and its `DisplayDataChangedData` carries `DisplayRowName`, `DisplayColumnName`, `DisplaySelection`,
  `PreviousDisplaySelection`, `Interaction`, `InteractionColumn`, `InteractionDisplayColumn` and
  `InteractionVariable`. Also found: a datetime variable of a table edited in place read back with a
  time of day (`05-May-2025 00:00:00` for `datetime(2025, 5, 5)`), because a datetime column kept its
  days and lost its `Format`; `Extent` answers `[0 0 340 340]` in R2025b whatever the table's size,
  data or font — a constant, not a rule; the `scroll` verb on a table, its words and its refusals,
  already agreed; `KeyPressFcn` and `KeyReleaseFcn` already ran through the window's key road.
- **Item 47.** Filed as "finish scrolling": `scroll(container, …)` and `ScrollableViewportLocation`
  were missing, a scrollable `uipanel` and `uifigure` scrolled nothing, and the wheel over a grid's
  bare background did nothing. Measured (probe_47): the location is `[x y]` in the container's own
  pixels — a grid's `y` is 1 at its top and falls below it as the grid scrolls down (`[1 -409]` at
  the bottom of a 610-pixel grid in a 200-pixel box), a panel's and a figure's `y` is 1 at the bottom
  and rises; a pair a script writes reads back as written (`[1 99999]`, `[0 0]`, `[1.5 2.5]`) until the
  container scrolls again; `scroll` keeps its location within range (`scroll(g, 1, 100)` is the top,
  `scroll(g, -5, -5)` five pixels down); the words are case-sensitive (`'BOTTOM'` is
  `MATLAB:uicontainer:InvalidScrollTarget`, "Invalid location", as are `scroll(g, 1)`,
  `scroll(g, [1 2 3])` and `scroll(g, 'bottom', 1)`); a write that is not numeric, not two numbers or
  not finite is `MATLAB:datatypes:Point2dDataType:ArrayClass` / `ArrayShape` / `ArrayData`; a
  container that is not scrollable answers `[1 1]` and `scroll` warns
  (`MATLAB:uicontainer:ScrollableOff`); a panel's and a figure's content reaches from the bottom-left
  corner up and right as far as the farthest child's position plus its size, never below or left of
  it (a label at `y = -50` is unreachable); a bar is 16 pixels on a panel or a figure and 17 on a
  grid; the property is hidden (`isprop` true, not in `get` or `set`).
- **Item 105.** R2025b's default `ColorOrder` is a new seven-colour palette and its axes ink
  `[0.1294 0.1294 0.1294]`; this build keeps the classic seven and `[0.251 0.251 0.251]`.
- **Item 99.** Measured but not changed (see Still open): R2025b's ticks over 77 ranges at six figure
  sizes and three font sizes (probe_99).

## Decision

### Item 87 — the plot box, and the limit-method words

- `AxesModel.PlotBoxMargins` (saved as `PlotBoxMargins`, four fractions of the cell) is the least
  margin the plot box keeps inside its cell while the cell is what was asked for. A MATLAB-dialect
  script's new axes takes `AxesModel.MatlabPlotBoxMargins` — `[0.13, 0.075, 0.095, 0.11]` left, top,
  right, bottom, which is R2025b's `Position [0.13 0.11 0.775 0.815]` in an `OuterPosition` of
  `[0 0 1 1]` — at adoption (`JgsGraphicsProperties.GiveMatlabPlotBox`); the JGS dialect keeps the
  tight plot box it has always drawn, and an axes read from a document keeps what the document says.
  The renderer and the pre-first-frame estimate widen the measured decoration margins to those
  fractions (`AxesModel.MarginsWithin`), so a title and two labels leave `Position` where R2025b
  leaves it, a written `OuterPosition` scales the inner box with it, and a pinned plot box
  (`InnerTarget`) and a `uiaxes` are untouched. `PositionConstraint` still reads `'outerposition'`.
- `XLimitMethod` (and `Y`, `Z`) takes the word in any case or a start of it that only one word has,
  and refuses the rest in R2025b's words (`MATLAB:datatypes:InvalidEnumValue`, "Error setting
  property 'XLimitMethod' of class 'Axes': 'bogus' is not a valid value. Use one of these values:
  'tickaligned' | 'tight' | 'padded'.").
- The limits themselves are unchanged by decision: both dialects keep the five per cent padding, the
  default method reads `'padded'`, `axis auto` keeps it, and `'tickaligned'` keeps JGraph's ladder.

### Item 65 — arrays of value objects, and StyleConfigurations

- `JgsObjectArray` is the payload of a `JgsType.Object` value holding none or several instances of
  one value class, column-major, as `JgsStructArray` is for a struct; one object keeps its
  `JgsObject` payload, so every reader of `AsObject` goes on reading one instance and an array
  reached through it says so (`JgsValue.ObjectClass` answers the class of either). Each element is a
  payload of its own with a holder count: a selection shares the instance and a later write through
  either side copies first.
- `[s s2]`, `[s; s2]`, `[a a]` and `[a; a]` join objects of one class (`Interpreter.ConcatenateObjects`);
  `[]` joins as nothing; another kind is R2025b's `NoConversionDefined` (the `Style` sentence names
  `ComponentStyle` as R2025b's does); a handle class is refused. `a(2)`, `a(1:2)`, `a(end)`,
  `a([])` and `s(2)` go through a stand-in array of positions, so every refusal is the array's own.
  `a(k) = s` replaces or grows (the gap filled with the class's default objects), `a(k) = []`
  deletes, `a = []; a(2) = s` starts one, `a(k).p = v` writes the element inside the array
  (`ResolveObjectElementForWrite`, through the class's set method when it has one), and `for q = a`
  walks a row's elements. `numel`, `size`, `isempty`, `length`, `class`, `isa`, `isobject`,
  `properties` and `fieldnames` answer for the array.
- A `uistyle` displays as R2025b shows it: `Style with properties:`, only the properties set (in
  R2025b's own order, `Icon` before `IconAlignment`), and `Use properties to show all properties`
  when some are not; none or all set lists all ten. An array shows `1×2 Style array with properties:`
  and the names. The echo of either prints the name, a blank line, the display and a blank line.
- `StyleConfigurations` is a table whose `Target` is a string array (`T.Target == 'row'`,
  `string`, `char`, `cellstr`, `ismember`, `T(T.Target == 'row', :)` answer as R2025b's categorical
  does), whose `TargetIndex` is a cell, and whose `Style` is an n-by-1 array of style objects (0-by-0
  for a component with none), each a share of the row's own, so a copy written through the table
  leaves the component's style as it was. `JgsValueColumn` holds an object or an array of them, grows
  with default objects, and prints a cell as `1×1 matlab.ui.style.Style`. `addStyle` of an array is
  `invalidStyleObject`. The `u9_styles` fixture and its helper are unchanged.

### Item 60 — a table's display order

- `JgsTableState.RowOrder` and `ColumnOrder` (the data row each display row shows, the data column
  each display column shows; null in data order) are behind R2025b's hidden `DisplayRowOrder` and
  `DisplayColumnOrder` (public, `[]` or a permutation of `1:n`; another value is refused), and
  `DisplayData` is the data in that order — a table through `Table.Select`, a cell, a matrix or a
  string array by element, the class kept. New `Data` shows in its own order.
- The window reports a sort (`sorted`: the display column and the direction, the grid's own sort
  cancelled and its arrow kept) and a drag (`rearranged`: the new display order). The script side
  sorts the rows (numbers by value with NaN last, text by its letters without regard to case,
  numbers before text, ties in data order) or takes the column order, rebuilds the picture in display
  order, and runs `DisplayDataChangedFcn` with R2025b's `DisplayDataChangedData`: the headings as
  shown, the selection as shown before and after, `'sort'` or `'rearrange'`, the data column, the
  display column, and the variable name when `Data` is a table.
- A cell edit, a selection and a click the window names in display positions reach the data cell
  they stand for: `CellEditData.Indices` is the data cell and `DisplayIndices` the shown one,
  `Selection` is kept in data positions and the window's selection in display positions
  (`DisplaySelection` reads the window's, which a scripted write of the order properties leaves
  where it was, as R2025b does), `ClickedData`'s `Row`/`Column` are data and
  `DisplayRow`/`DisplayColumn` shown.
- A press on a table in a `uifigure` runs its `ButtonDownFcn` after the window's. A datetime table
  variable is a `JgsTimeColumn` as a duration already was, so its `Format` survives a rebuild and an
  edit in place reads back `05-May-2025`.

### Item 47 — scrolling panels, tabs, button groups and figures

- `UiContainerModel.ScrollX`/`ScrollY` (moved up from the grid) and `FigureModel.ScrollX`/`ScrollY`
  are pixels from the left and the top of the content; `ViewportLocationWritten` is the pair a script
  wrote, read back as written until the container scrolls again. `UiScrolling` measures a container:
  the content is the inner area or the farthest child's position plus its size, the viewport the
  inner area less a 16-pixel bar along each edge the content overflows (two passes, as the grid's
  `ArrangeScrolling` does with its 17); a grid measures through `UiGridLayoutModel.Scrolled`.
- The layout (`UiLayout`) places a scrollable panel's children in the whole of the content, moved
  under a viewport in the top-left of its inner area, with the bars where the grid's are and the
  panel's own axes placed in the moved content too; a scrollable `uifigure` the same for its
  components and its own axes (`UiLayoutResult.FigureScroll`), with bars along the window's edges.
  The window's bars are keyed by the container or the figure, their values go back as a grid's do,
  and the wheel moves the innermost scrollable container under it, the figure when none — asked by
  the window before the canvas zooms, so a grid's bare background scrolls as its components do.
- `scroll(c, 'top' | 'bottom' | 'left' | 'right')` and `scroll(c, x, y)` keep the location within
  range (`JgsGraphicsProperties.ScrollTo`); `ScrollableViewportLocation` on a grid, a panel, a button
  group, a tab and a figure reads `[1 + x, 1 - y]` on a grid and `[1 + x, 1 + (max - y)]` elsewhere,
  `[1 1]` when not scrollable, the written pair verbatim, and refuses as R2025b refuses. A panel and
  a figure start at their top.

### Item 105 — the colour order and the ink

By the user's decision (2026-10-12) the classic colour order and the `[0.251 0.251 0.251]` ink stay in
both dialects; the divergence rows of ADR 0226 and ADR 0228 stand. The item is closed; item 114's ink
half is decided with it, its font size is not.

## Divergences

- **A new axes' `XLimitMethod` reads `'padded'` and its automatic limits are padded five per cent**
  (`[0.9 3.1]` for `plot(1:3)`; a `uiaxes` too), where R2025b's is `'tickaligned'` and `[1 3]`
  (ADR 0002, ADR 0008; kept by decision). `axis auto` keeps `'padded'` where R2025b sets
  `'tickaligned'`.
- **`'padded'` pads five per cent symmetrically**; R2025b pads about seven per cent and rounds the
  ends outward (`[0.8 3.14]` for `[1 3]`, `[-1 10.7]` for `[0 10]`, `[9e4 2.1e5]` for `[1e5 2e5]`).
- **`'tickaligned'` rounds to JGraph's own 1-2-5 ladder** (`[0.1 1]` is `[0 1]`, `[-3 11]` is `[-5 15]`,
  `[5 5]` is `[4.7 5.3]`); R2025b rounds to its own tick step (`[0.1 1]`, `[-4 12]`, `[4 6]`).
- **A colorbar does not shrink the plot box**; R2025b's moves `Position` to `[0.1232 0.11 0.7343 0.815]`.
- **The left margin is the measured one when the tick labels need more than 13 per cent of a narrow
  cell** (`[0.0719 …]` after `OuterPosition = [0 0 0.5 1]` in a 640-pixel figure; R2025b's `0.065`);
  `TightInset` is this build's own measurement throughout.
- **A `uiaxes` keeps its tight inner box**; R2025b's default inner box inside its `Position` was not
  measured.
- **`StyleConfigurations.Target` is a string array**; R2025b's is a categorical: `class` answers
  `'string'`, `strcmp(T.Target, 'row')` compares (`[1;0;0]` where R2025b answers `false`), and
  `T.Target{1}` reads `'row'` where R2025b refuses (`MATLAB:categorical:CellReferenceNotAllowed`).
- **A style's display offers the rest as `Use properties to show all properties`**, the words R2025b
  prints without hotlinks; its console prints a link whose text is `Show all properties`.
- **`a.p` on an array of objects is refused** ("would be a comma-separated list"); R2025b answers the
  list. Arrays of handle objects and two-subscript writes (`a(1, 2) = s`) are refused; a gap an
  element write opens (`a(5) = s` on three) is filled with the class's default objects where R2025b
  fills it with base-class `ComponentStyle` objects.
- **A table's `Extent` is its size and 40 pixels each way**; R2025b answers `[0 0 340 340]` for every
  table asked, whatever its size, data or font (a constant, kept as this build's own by the rule that
  correctness outranks parity).
- **`DisplayRowName`, `DisplayColumnName` and `InteractionVariable` of `DisplayDataChangedData`, the
  sort order of text and NaN, and the refusal of a bad `DisplayRowOrder`** are this build's own
  (R2025b runs none of it headless); a table's `KeyPressFcn` is told by a
  `UIClientComponentKeyEvent` as every component's is, where R2025b's class was not measured.
- **A titled panel's scroll location counts from this build's inner area**: `[1 100]` at the top of
  probe_47's panel where R2025b answers `108.77` (its title band is 8.77 pixels shorter and its
  positions carry DPI fractions, `0.765625` at the bottom); a tab's is one pixel off (`17` for `18`).
- **A panel whose children overflow only to the right scrolls 16 pixels vertically**, the room its
  horizontal bar takes; R2025b's does not.
- **A scrollable `uifigure` starts at its top**; headless R2025b answered its start as `[1 1]` once
  and `[1 137]` twice, and `scroll(uf, 'top')` as `[1 1]`, so the figure's locations are not in the
  fixture; the probe's `fig_top` `[1 137]` and `fig_right` `[217 137]` are what this build answers.
- **A `uifigure` scrolls its axes with its components; a panel too**; R2025b was not measured with
  axes in a scrollable container.
- **The MATLAB dialect's `s(2)` on one object** says "Index exceeds the number of array elements.
  Index must not exceed 1." as R2025b does; `Children` is a row (ADR 0202).

## Consequences

- Silent wrong answers fixed: every MATLAB-dialect script that read an axes' `Position` or placed a
  second axes by it; `DisplayData` after a sort; a datetime table variable's format after an edit.
  Refusals that are now R2025b's: `XLimitMethod`, `ScrollableViewportLocation`, `scroll` on a
  container, `[s 1]`, `s(2)`.
- Three model fields are saved: `PlotBoxMargins` on an axes; a container's scroll offsets are not (a
  document opens at the top).
- Fixture `oi_axes_box_styles_tables_scroll`, 154 lines, 11 of them divergences; `OpenItemsBatch17Tests`
  (9 cases: the two dialects' plot boxes, the margins' growth, the saved margins, a JGS bracket of
  objects staying a list, an object array's value semantics, a sort and a drag reported by the
  window, the scroll measure, a scrollable panel's and figure's layout, a bar's move and a written
  location). Full suite 11,026 of 11,026; stress 89 of 89 (stess_47's assertion 2 had frozen the tight
  plot box as outer = inner + TightInset, which R2025b contradicts too; it now holds R2025b's box).
  `MatlabM75FigurePropertyTests` held the same statement and now holds R2025b's. A bracket of handle
  objects keeps the road it had (a boxed array released in index order, `destructor_lifetime`); the
  object-array road is for value classes only. `struct(a)` of an array of value objects is its first
  element's struct, as before (`oi_class_answers`). `oi_findprop_detach_computed`'s `title_position`
  divergence was re-stamped: a title's pixel position moved with the plot box. Two raw writes joined
  the ownership gate's allow list (an object array's shared copy, a loop variable over one).
- Six R2025b recordings of the fixture were needed (one ran before an edit landed, one was overwritten by a stamp taken from the test's stale copy): an output-less verb refuses "Too many output
  arguments" before it runs when called for a value (`oi_ran` runs it for its effect); `evalc` inside
  an anonymous function cannot see the script's variables; a display link's text differs with where
  `evalc` runs; a figure's scroll location is not stable headless.

## Still open

- **Item 99**, measured and waiting on a decision. R2025b's ticks are not a ladder: over 77 ranges
  and six axes sizes its count is bounded by the axis length (10 ticks on a 434-pixel X axis, 12 on
  anything longer; 12 on a 342-pixel Y axis, 8 at 171), it prefers a step whose multiples land on
  both limits (`[0 15]` takes 5 over 2, `[0 1.5]` 0.5 over 0.2) and otherwise the densest step that
  fits, and font size does not enter (14 and 6 point answered alike). That is about twice this build's
  density (`[0 50000]` in steps of 5000, `[0 2]` in 0.2) and changes how every figure looks. The
  question: (A) both dialects take R2025b's rule, (B) the MATLAB dialect only, (C) keep JGraph's
  ladder and record the divergence.
- Filed: 115 (a categorical type), 116 (comma lists from object arrays, handle-object arrays,
  two-subscript writes), 117 (the window-only answers of a table's sort and keys, and a scrollable
  figure's start, to measure in a MATLAB window session).
