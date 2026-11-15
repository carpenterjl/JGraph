# ADR 0372 — A figure has R2025b's default menus, toolbar and annotation pane, and the plotting and patch leftovers after ADRs 0360 and 0361

## Status

Accepted, 2026-11-14. Open items 326 and 327 (chips wave 6, lane 6A).

## Context

- **Item 326**, ADR 0360's still-open list. Probes `p6a` (190 rows), `p6b` (the figure's tree, its
  bars and pane under each switch, 140 lines), `p6c` (transforms) and `p6d` (a tiled layout's tree)
  measured R2025b 25.2:
  - `figure('Visible', 'off')` holds a `uitoolbar` tagged `FigureToolBar` with nine tools (four
    push tools, five toggle tools) and six menus (`figMenuHelp` ... `figMenuFile`) with 83 entries
    under them, every handle hidden: `findall(f)` lists 100 objects, `allchild(f)` the toolbar and the
    six menus, `f.Children` and `findobj(f)` none. Each entry has R2025b's `Text`, `Accelerator`,
    `Separator` and `Enable`, each tool its `Tooltip` and `Separator`. The menus exist while `MenuBar`
    is `'figure'`; the toolbar while `ToolBar` is `'figure'`, or `'auto'` with the menus. `clf` and
    `clf reset` keep them; `delete(allchild(f))` takes them; deleting a menu takes its entries. A
    script's own `uimenu` stands in front of them in `allchild`. JGraph had none.
  - The figure's annotation pane (`scribeOverlay`) is made with the figure's first axes (a plot,
    `polaraxes`, a heatmap and a tiled layout too), stands in front of the axes and behind every
    component, and stays after the axes are deleted and after `clf`. JGraph made it with the first
    annotation only. A panel holding an axes gets a pane of its own.
  - A new legend's `Tag` is `'legend'` and a new colour bar's `'Colorbar'`; a pair in the call writes
    over it, and a legend made again after `legend off` is `'legend'` again.
  - `get([], 'Type')`, `get(gobjects(0), ...)`, `get('', ...)` and `get([])` are `[]`; a cell of names
    is a 0-by-1 cell. JGraph refused them.
  - `legend(ax)` after a group was made lists what is in no group, and a group whose
    `IconDisplayStyle` is `'on'` as one row standing for it (`{'grp', 'plain'}`, its `PlotChildren`
    the hggroup); JGraph listed the members.
  - A legend standing for a group comes back from R2025b's own `.fig` as one row named by the group's
    words with the group as its `PlotChildren`; JGraph's file and `.graph` document wrote its first
    member's row (`data1`, a line).
  - `stem` reads any start of `filled`, in any case, among its leading words (`'f'`, `'fi'`,
    `'FILL'`); JGraph knew the whole word.
  - `plot("a", x, y)` (a string, not a char row) is "Invalid data argument.", in `semilogx` and
    `loglog` too, and `plot3("a", ...)` is `MATLAB:plot3:InvalidFirstInput`.
  - `xscale(h)` of a heatmap, and `xscale('log')` with a heatmap current, is
    `MATLAB:Chart:UnsupportedConvenienceFunction`, "Using xscale with heatmap is not supported.".
  - A scatter's table channel named by a variable number reads back the number (`YVariable` 2, and
    `logical([1 0 0])` reads 1); several numbers make one chart each reading its own; setting the
    property to a number keeps the number. JGraph refused a number outright (`scatter(tbl, 1, 2)`)
    and read names back.
- **Item 327**, ADR 0361's still-open list:
  - a patch's `CDataMode` written `'auto'` reads `'auto'` while its colours stay, until colours are
    written again; JGraph kept reading `'manual'`;
  - `patch(x, y, 'b', 'FaceColor', 'flat')` reads `'flat'` and its unset 33/255 `CData` (fill too);
    JGraph read the blue;
  - a patch's, a fill's and a fill3's own edge is 33/255 grey half a point wide, on every form of the
    call; JGraph's was black 0.75;
  - a surface's (`surf`, `surface`, `mesh`, `pcolor`) and a patch's unset `MarkerEdgeColor` reads
    `'auto'`; JGraph read `'none'`;
  - `histogram([], n)` and `histcounts([], n)`, and readings none of which is finite, have edges `0:n`
    (binpicker.m's empty branch); JGraph had `[0 1]` cut in n.

## Decision

### A figure's default menus and toolbar are handles alone (`JgsFigureChrome`)

`JgsFigureChrome` makes, the first time a MATLAB-dialect `findall` or `allchild` reaches a figure
(not a uifigure), the toolbar, its nine tools and the 89 menus and entries from R2025b's recorded
table, as ordinary `UiToolbarModel`, `UiToolModel`/`UiToggleToolModel` and `MenuItemModel` objects
that are never put in `FigureModel.Menus` or `Toolbars`: no window draws them, no `.graph` document
saves them, no `.fig` writes them, and `f.Children`, `findobj`, `clf` and the `.fig` writer never see
them (`ChildrenOf` is unchanged). A `ConditionalWeakTable` keys them to their figure.

- `JgsGraphicsProperties.AllChildrenOf` (used by `allchild`) and `SearchedUnder` (used by `findall`)
  list them, behind the figure's own components and in front of its axes and pane (`WithChrome`);
  `DescendantsOf` lists the ones that exist whatever the dialect, so their handles live as long as the
  figure. `Listing` reads `MenuBar` and `ShowsToolBar` each time.
- Their handles mint hidden (`JgsHandleRegistry`), and `VisibleChildrenOf` treats an unminted default
  entry (or pane) as hidden, so a menu's `Children` is empty as R2025b's is.
- `ParentOf` answers the figure for a top-level menu and the toolbar; `delete` takes one off its
  figure (`JgsFigureChrome.Remove`), entries going with their menu as any menu's do.

### The annotation pane comes with the first axes (`JgsAnnotationPane.Listed`)

In the MATLAB dialect a figure lists its pane once a listing has found it holding a top-level axes
(`HadAxes`), and keeps listing one after the axes go and after a plain `clf`. A pane deleted - by
`delete(allchild(f))`, `delete(pane)` or `clf reset` - is not made again until the next axes or
annotation (`ForgetDeleted`; `u8_bars` records R2025b doing so for the first; its handle dies, as
`oi_group_tree_annotation_pane` records). A panel's pane is left undone.

### clf reset keeps MenuBar

R2025b's `clf(f, 'reset')` leaves `MenuBar` and `ToolBar` as they were (probe `p6j`: `none none`
after a reset, no menus, and no menus put back that a script had deleted). This build reset
`MenuBar` to `'figure'`, which with the default menus made them appear (`u2_tree`'s `clf_reset_left`);
in the MATLAB dialect `MenuBar` is now kept.

### The plotting leftovers

- **Default tags**: the MATLAB `legend` sets a new legend's `Tag` to `'legend'` (`MakeLegend`, before
  its pairs), and `colorbar` a new colour bar's to `'Colorbar'` before its pairs.
- **`get` of no handles** answers `[]` (a 0-by-1 cell for a cell of names) in the MATLAB dialect.
- **`legend(ax)` beside groups** (`RowsLeavingGroupsOut`): with no handles named, rows whose series is
  in a group are left out, and an outermost group whose icon is `'on'` takes one row through its
  first member a legend can show, standing for the group (ADR 0360's `LegendEntryModel.Group`).
- **A legend of a group saved**: `LegendEntryDto.GroupIndex` (null in every older document) names the
  group by its index in the axes' groups, put back after the groups (`ApplyLegendGroups`); the `.fig`
  writer names the group in the legend's `PlotChildren`. R2025b's `openfig` of such a file reads one
  legend, `{'grp'}`, standing for an hggroup (probes `p6h`, `p6i`).
- **stem** (`WholeFilledWord`): in the MATLAB dialect the first two text words, when a start of
  `filled`, are the flag.
- **plot of a string**: the text-first refusal wrapper keeps string scalars
  (`JgsCategoricalCharts.Wrap(..., keepStrings: true)`; the verb under it demotes them as every call
  does) and tells `RefuseTextData` whether a string scalar stood first, so in the plot family a string
  first is "Invalid data argument." and plot3 names itself; every other verb sees the char row it saw.
- **xscale of a heatmap** (`ScaleVerb`): a `HeatmapPlot` handle, or an axes holding one (this build's
  heatmap lives in an axes; R2025b's `gca` is the chart), is refused in R2025b's words.
- **Table channels by number** (`TableChannel`, `NumberedVariable`, `ScatterSource.Given`): a number
  or a logical vector picking one variable names it, and the channel reads back the number; a run of
  several numbers passes each on; a number written to the property is kept. Polar and plot table
  forms are as they were.

### The patch leftovers

- **CDataMode** (`PatchCDataWrittenAuto`): writing `'auto'` marks the patch, which then reads `'auto'`
  until its colours are written (`SetPatchColorData`) or `'manual'` is; the JGS dialect reads as it
  did.
- **A colour argument, then a face word** (`SetPatchFaceByData`, `PatchFaceByWord`): `FaceColor`
  `'flat'` or `'interp'`, as an option or a property, in the MATLAB dialect, drops the face's own
  colour; a patch with no colour data then reads the word and is filled in the 33/255 grey of its
  unset `CData` (`FaceColors`). A colour or `'none'` written after forgets it.
- **The edge** (`MatlabPatchEdge`): `patch`, `fill` and `fill3` in the MATLAB dialect set the edge to
  33/255 grey, 0.5 wide, before the call's options. The model's default stays black 0.75, so the JGS
  dialect, `PatchPlot_KeepsItsDefaultBlackEdge` and documents without an edge are unchanged; a
  MATLAB-made patch carries its edge into the document explicitly.
- **Marker edge** (`AddMarkerBlock(..., edgeAutoWhenUnset: true)` for surfaces and patches): in the
  MATLAB dialect the model's null reads `'auto'` (it already draws as the edge, which is what R2025b's
  `'auto'` means), `'auto'` writes the null, and `'none'` written is remembered beside it
  (`MarkerEdgeWrittenNone`) and read back.
- **histogram of nothing** (`Binning.UnitEdges`): the counted-edges road of `histogram` and
  `histcounts` takes `0:n` in the MATLAB dialect when no reading is finite; JGS keeps (0, 1) cut in n.

## Consequences

- New parity fixture `oi_graphics_defaults` (188 rows from R2025b 25.2, every row agreeing, packed
  and boxed). New `GraphicsDefaultsLeftoverTests` (the bars are handles alone, a default menu deleted,
  the JGS patch edge and histogram kept, a legend of a group through the document).
- `FigureBuildI10Tests.AHistogramOfTheFewAndTheMissingIsWhatItWas` asserted `[0 0.25 0.5 0.75 1]` for
  `histogram(zeros(1, 0), 4)`; it asserts R2025b's `0:4`.
- `findall(f)` of a MATLAB-dialect figure lists about a hundred more handles, and a script that
  deletes, hides or relabels a default menu or tool by its tag now finds it (it does nothing to any
  window). A figure with axes lists its pane in `allchild` and `findall`.
- A MATLAB-dialect patch or fill is drawn with a thinner grey edge.

## Divergences

None opened.

## Still open

- **A tiled layout is not in this build's figure tree**: a tile's `Parent` is the figure here and the
  layout in R2025b, the figure lists the tiles rather than the layout, and so a tiled layout's legend
  and colour bar are the figure's children here (R2025b: the layout's, `t.Children` `colorbar legend
  axes`). Making the layout a node of the tree reaches `clf`, deletion and the `.fig` writer
  (probe `p6d`).
- **A rotation by `pi/2`** leaves a span of 6e-17 that R2025b widens to `[-1 1]`. No rule was found:
  R2025b keeps a 1e-17 span made by a shear entry and the 1.1e-16 of a `pi/4` rotation's cancellation
  (`[0 1.2e-16]`), and fits a transform's points where this build fits the eight corners of the data
  box (a `pi/4` rotation of a diagonal line, `rot45` in `p6c`). R2025b also kept limits computed under
  an earlier matrix after a `Matrix`-only change (`p6c`'s shear rows), so its limits need a data change
  to follow the matrix.
- **The pane after `clf reset`**: R2025b lists a new pane at once (`p6b`'s `pane_none_clfreset`), this
  build with the next axes.
- **A panel's annotation pane** (R2025b gives a panel holding an axes a pane of its own; u2_tree's
  `allchild_count_with_furniture` divergence, ADR 0199).
- **A stem's `MarkerFaceColor` under `filled`** reads `'auto'` in R2025b and the colour here.
- **Neighbours measured on the way**: an area's, a bar's and a histogram's edge (33/255 grey, 0.5
  there; `'none'`, 1 here), a rectangle's (black 0.75 here); `patch('XData', ..., 'YData', ...)`'s
  face (33/255 there, 64/255 here); `MarkerEdgeColor` `'flat'` on a surface (refused here);
  `plot(tbl, 1, 2)` (a number refused here; a line from a table has no `YVariable` here); a string
  given as a table channel reads back a char row here; `CDataMode 'x'`'s refusal words; a patch given
  an RGB colour and `FaceColor 'interp'` warns `NumColorsNotEqualNumVertsException` there; `[5 5+1e-14]`
  widens to ±6e-13 there; `strjoin` of a char column is refused there and joined here; `findall`
  answers objects there and numbers here.
