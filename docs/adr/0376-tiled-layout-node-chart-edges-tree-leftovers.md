# ADR 0376 — A tiled layout is a node of the figure tree, the filled charts' factory edge, and the other leftovers of ADR 0372

## Status

Accepted, 2026-11-15. Open item 343 (chips wave 7, lane 7F).

## Context

Every expectation below was measured in R2025b 25.2 headless (`-noFigureWindows -batch`), with
probes `p7fa` (item 343's small defaults, 83 lines), `p7fb` and `p7fd` (a tiled layout's tree, its
deletion, `clf`, `savefig` and `openfig`), `p7fc` (the annotation pane, a panel's pane, what `findall`
answers, spans) and `p7fe` (25 transform matrices and the limits they fit), one line per case in
both engines and diffed.

- **A stem made `'filled'`** reads `MarkerFaceColor` `'auto'` (stem and stem3, every series, any
  start of the word); a face written `'auto'` reads `'auto'`, a colour or `'none'` reads back as
  written. Its unset `MarkerEdgeColor` reads `'auto'`, filled or not. A line takes
  `MarkerFaceColor` `'auto'` too. JGraph read the colour, refused `'auto'` (`stem: unknown color`),
  and stem3 `'filled'` left the face `'none'` (the stem's colour was still null when it was read).
- **An area's, a bar's (barh, bar3) and a histogram's own edge** is 33/255 grey, half a point wide
  (categorical histograms and histogram2 too); a histogram's stairs keep their `'auto'` edge and take
  the width. **A rectangle's** is the same. JGraph's were `'none'` 1 (area, bar, histogram), 38/255
  (histogram2) and black 0.75 (rectangle).
- **`patch('XData', ..., 'YData', ...)`, `patch()` and `patch('Faces', ..., 'Vertices', ...)`** with no
  colour named are filled 33/255 grey whether the axes came first or as `'Parent'`; a held
  `DefaultPatchFaceColor` wins (`oi_findprop_detach_computed`). JGraph filled 64/255 (its ink), or the
  colour order's blue for the face-vertex form and for `'Parent'`.
- **`MarkerEdgeColor` and `MarkerFaceColor` `'flat'`** on a surface (surf, surface, mesh) and a patch
  read back `'flat'`, and the face takes `'auto'` there; a word that names no colour (`'interp'`) is
  `MATLAB:datatypes:RGBAColor:ParseError`, "Error setting property 'MarkerEdgeColor' of class
  'Surface': Invalid color name or hexadecimal color code. Valid names include: ... 'flat', 'auto',
  and 'none'. ...". A line refuses `'flat'` in the same words without it. JGraph refused `'flat'` and
  read `'interp'` as red (the `r` in it).
- **`plot(tbl, 1, 2)`**: a variable number, a logical vector or a name per channel; several y
  numbers make a line each; each line reads `XVariable` and `YVariable` back as given (a number as
  the number) and its `SourceTable`; writing `YVariable = 3` redraws it. A number past the table is
  `MATLAB:graphics:chart:SubscriptOutOfBounds` ("Invalid subscript for Y. Index exceeds array
  bounds."), a fraction `MATLAB:graphics:chart:InvalidSubscriptValue`. Neither ruler is labelled from
  the names, on a fresh axes or a held one. JGraph refused a number and labelled the rulers.
- **`strjoin` of a char array** (a column, a row, a matrix, `''`) or a number is
  `MATLAB:strjoin:InvalidCellType`; JGraph joined a column's rows.
- **A tiled layout** (probes `p7fb`, `p7fd`): the figure's child is the layout (`f.Children` is
  `tiledlayout` from `tiledlayout(f, ...)` on), each tile's `Parent` is the layout, and a tile's
  legend and colour bar are the layout's children. `t.Children` lists tiles and furniture newest first
  by when each tile was made and each legend or colour bar last shown (`legend legend Colorbar a3
  Colorbar a2 a1`). `findobj`, `findall` and `allchild` walk through it, `findobj(f, '-depth', 1)` is
  `figure tiledlayout`, `findobj(f, 'Parent', t)` finds the four. Deleting a tile takes its colour
  bar and legend; `delete(t)` takes every tile; `clf` and `clf reset` take the layout. `axes(t)` is
  the layout's next tile. A second `tiledlayout` takes the first with its tiles; `tiledlayout` takes
  an axes the figure held; `subplot` takes the layout; an axes made after it stands beside it
  (`axes tiledlayout`). `savefig` and `openfig` keep the layout, its spacing and the furniture.
  JGraph listed the tiles as the figure's children, their legends and colour bars with them.
- **The annotation pane** (probe `p7fc`): `clf reset` lists the pane the figure's axes made again at
  once, the same handle (`pane_clfreset_same` `[1 1]`), and takes a pane an annotation made; a plain
  `clf` keeps the pane; a pane a script deleted is not made again by the axes still there. A panel or
  a tab holding an axes has a pane of its own (`allchild(p)` `annotationpane axes`, tag
  `scribeOverlay`, `Parent` the panel), kept after the axes go; it is the `u2_tree` divergence of ADR
  0199. JGraph made the pane again with the next axes after `clf reset`, a plain `clf` took a pane no
  listing had minted, and a panel had none.
- **What `findall` answers**: objects there, numbers here - every handle is a number in this build
  (ADRs 0202, 0208), not a lane's change. An empty `findall` or `findobj` is 0-by-0 there and was
  0-by-1 here.
- **A rotation by `pi/2`** leaves `cos(pi/2) = 6e-17`, which R2025b fits as a zero span (`[-1 1]`).
  Over 25 matrices one rule fits every case: when the 3-by-3 part of the matrix is exactly orthogonal
  (`M'M` the identity to the bit), its entries under one eps fit as zero. A shear or a scaling entry
  of `1e-17` or `6e-17` keeps its span (`M'M` is not the identity), so does a rotation's own
  cancellation (`pi/4` of a diagonal line, `[0 1.1e-16]`: no entry is small), and so does a rotation
  by `1e-15` (an entry over eps). A translation of `6e-17` is not touched.

## Decision

### A stem's and a line's face 'auto' (`MarkerFaceAuto`)

A `ConditionalWeakTable` marks the stems (2-D and 3-D) and lines whose face is R2025b's `'auto'`: a
MATLAB-dialect `'filled'` with no face named in the call (`MarkMarkerFaceAuto`), or `'auto'` written
(`WritesMarkerFaceAuto`). The model keeps the colour it is drawn in - the stem's own, a line's axes
colour - so the window draws as it did; the MATLAB dialect reads the word. A stem's and a stem3's
unset marker edge reads `'auto'` there and `'auto'` writes the null (`AddSpatialMarkerColours`'
`readOwnEdge`). stem3 `'filled'` fills with the palette colour when the stem's own is unset. The JGS
dialect reads colours as before.

### The filled charts' factory edge (`MatlabChartEdge`)

`area`, `bar`/`barh`, `histogram` (bars; stairs take the width only), `histogram2` and `rectangle` in
the MATLAB dialect set the edge to `MatlabFactoryEdge` (33/255), 0.5 wide, before the call's options,
as ADR 0372 did for the patch. `bar(..., 'EdgeColor', 'none')` now also clears the edge colour, so it
reads `'none'` over the grey. The model defaults are unchanged, so the JGS dialect and old documents
draw as they did.

### A patch with no colour named (`HoldsDefault`)

`patch('XData', ...)`, `patch()` and the face-vertex form fill with `MatlabFactoryEdge` in the MATLAB
dialect unless a `DefaultPatchFaceColor` is held above the patch (`HoldsDefault`, the store
`ApplyDefaults` reads), replacing the `'Parent'` exception. The JGS dialect keeps its ink.

### 'flat' and 'auto' on a surface's and a patch's markers (`MarkerEdgeWord`, `MarkerFaceWord`)

`AddMarkerBlock` (surfaces and patches) remembers `'flat'` on either slot and `'auto'` on the face,
writes null to the model (an unfilled marker, an edge that follows the object's), and reads the word.
A text that names no colour is refused in R2025b's words (`MarkerColourRefusal`, with the class word
`Surface` or `Patch`); a line's `MarkerEdgeColor` refuses `'flat'` the same way.

### plot of a table (`MatlabTablePlot`, `AddLineSourceBlock`)

The MATLAB-dialect `plot(tbl, x, y)` reads each channel as a name, a cell or string array of names,
variable numbers or a logical vector (`TablePlotChannels`), draws a line per pair, hangs a
`ScatterSource` on each (the record scatter's table form has used since M79) and puts back the rulers'
labels the model's table form wrote. A line answers `SourceTable`, `XVariable` and `YVariable` in the
MATLAB dialect only (`OnlyWhen`), a written channel re-reading the table (`ReplotFromSource` learnt
`LinePlot`).

### strjoin

The MATLAB-dialect `strjoin` refuses a first argument that is a char array or numbers in R2025b's
words; a string array, a cell, this build's own lists of texts (`colnames`) and the categorical and
object refusals are as they were.

### The tiled layout as a node (`LayoutHolding`, `AddFigureAxes`, `LayoutChildren`, `RemoveLayout`)

In a MATLAB-dialect script:

- `ParentOf` answers the layout for a tile (an axes of the figure's `TiledLayout.Tiles`, no panel),
  and through it for the tile's legend and colour bars.
- `ChildrenOf(figure)` lists the layout in its tiles' place, among the figure's other axes by
  creation; `ChildrenOf(layout)` lists the live tiles and their shown furniture by when each was made
  or shown. `AxesModel.ShownOrder` now reads the creation clock (`GraphObject.NextOrder`), so a legend
  shown after a tile was made orders after it. The layout's `Children` property reads the visible
  children there; `allchild`, `findobj` and `findall` follow from the tree.
- `delete(t)` (`RemoveLayout`) takes the tiles out of the figure and forgets the layout; `clf` reaches
  it through the children it removes. `tiledlayout` removes the figure's layout and its top-level axes
  first (`ClearForLayout`), and `subplot` the layout.
- `axes(t, ...)` and `axes('Parent', t, ...)` hand out the next tile (`AxesInLayout`). `nexttile`'s
  cursor is the layout's own (`PlaceTile`, `TileCursors`) where it was one count for the session.
- `title(t, txt)` titles the grid (both dialects); it used to read the handle as the current axes'
  title and make an axes beside the layout.
- The `.fig` writer writes a layout's tiles as the figure's axes where the layout placed them
  (hgsave's form; the `.graph` document beside it carries the layout), so the MATLAB copy is what it
  was.
- A layout a MATLAB-dialect script made stays alive through a sweep run outside one
  (`MatlabLayouts`), as the figure's pane does.

The JGS dialect's tree is unchanged: a tile is its figure's child.

### The annotation pane (`JgsAnnotationPane.CameWithAxes`, `ListedIn`, `Owner`)

- `clf reset` in the MATLAB dialect keeps a pane the figure's axes made, deleting what it holds; a
  pane an annotation made goes as before. A plain `clf` keeps a pane no listing has minted (it is
  hidden, as `VisibleChildrenOf` already treated it).
- A deleted pane is made again by axes made after the deletion only (`DeletedAt`), not by the ones it
  stood beside.
- A panel or a tab of a classic figure lists a pane of its own once it held an axes (`ListedIn`), in
  front of its axes and behind its components; its `Parent` is the container (`Owner`), it holds
  nothing, `delete` forgets it, and the `.fig` writer passes it by. A figure's pane is unchanged.

### findall and findobj of nothing

The handle search answers 0-by-0 for no match in the MATLAB dialect.

### A rotation's residue (`GraphicsGroupIndex.WithoutRotationResidue`)

`GraphicsGroupIndex.Move` writes a matrix's 3-by-3 entries under eps as zero when that part is
exactly orthogonal, before moving the box. The rule lives in `JGraph.Core`, so both dialects fit a
`pi/2` rotation's line as R2025b does.

## Consequences

- New parity fixture `oi_figure_tree_leftovers` (188 rows from R2025b 25.2, every row agreeing,
  packed and boxed).
- `oi_patch_rows_axes_font_short_rulers`' `patchxy_facecolor` agrees (ADR 0232's row closed);
  `u2_tree`'s `allchild_count_with_furniture` agrees (ADR 0199's row closed);
  `oi_crowded_ruler_mixed_axis`' `pv_hist` and `pv_hist_num` divergence rows restamped (the edge and
  width now agree inside them).
- `MatlabChart3DTests` and `MatlabM77PlotPropertyTests` ask a `'filled'` stem's face for `'auto'`,
  `MatlabBarAndStairsTests` a bar's `LineWidth` for 0.5.
- A MATLAB-dialect area, bar, histogram and rectangle draw with a thinner grey edge, and a tiled
  figure's `findall` lists the layout.

## Divergences

None opened.

## Still open

- **A handle is a number here and an object in R2025b**, so `class(findall(...))` is `double` here
  (ADRs 0202, 0208); item 343's `findall` part is that.
- **A histogram's `FaceColor` reads its colour here and `'auto'` there**, and its stairs' `EdgeColor`
  reads `'none'` here and `'auto'` there.
- **`t.Title` is the text here and a `matlab.graphics.layout.Text` there**; `title(t, ...)` answers the
  layout's handle and takes no options. A tiled layout in a panel is refused here.
- **`plot3(tbl, ...)`** is refused here; a line from `plot(tbl, ...)` keeps the variable name as its
  `DisplayName` here (R2025b's is empty for a number, not measured for a name).
- **R2025b's `plot` into an axes with `NextPlot` `'replace'` resets its `Tag`** (`p7fd`: a tile tagged
  before the plot reads its type); this build keeps it.
- **`Children` reads a row here and a column in R2025b** (`isequal(t.Children, [a2; a1])` is false
  here); the fixture compares `Children(:)`.
- **A rotation's limits**: R2025b fits a transform's points where this build fits the eight corners of
  the data box (`pi/4` of a diagonal line), keeps limits computed under an earlier matrix after a
  `Matrix`-only change, and rounds a span of `1e-15` to `[0 1.2e-15]` where this build answers
  `[0 1e-15]`.
- **The marker words `'flat'` and `'auto'`** on a surface or a patch draw an unfilled marker and an
  edge that follows the object's; not looked at in a window.
- **Not looked at in a window**: ADR 0372's thinner grey patch edge and grey flat face, and this ADR's
  area, bar, histogram and rectangle edges.
