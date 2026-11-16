# ADR 0382 — A histogram's colour words, Children as a column, a layout's Text and a layout in a panel, and the other graphics leftovers of ADR 0376

## Status

Accepted, 2026-11-16. Open item 348 (chips wave 8, lane 8E), all but its window checks.

## Context

Every expectation below was measured in R2025b 25.2 headless (`-noFigureWindows -batch`), with
probes `p8ea` to `p8ei` in lane 8E's scratch (about 420 lines), one line per case in both engines and
diffed.

- **A histogram's `FaceColor`** reads `'auto'` until a colour is written (bars, categorical, 2-D,
  horizontal, given counts, a face alpha of 0, `get(h)`); `'auto'` and `'none'` are taken in any
  case and from two letters (`'AUTO'`, `'au'`), `'flat'` is `MATLAB:datatypes:RGBAColor:ParseError`
  naming `'auto'` and `'none'`. A **stairs** histogram's face reads `'none'` and refuses anything else
  (`MATLAB:histogram:InvalidStairsFaceColor`, "'FaceColor' value must be 'none' when 'DisplayStyle'
  value is 'stairs'.", from the call's options in either order, from `set`, `'auto'` included); its
  **edge** reads `'auto'`. R2025b keeps one edge for the bars and one for the stairs: bars edged
  green shown as stairs read `'auto'`, and back as bars green again; stairs edged magenta shown as
  bars read the 33/255 grey, and back as stairs magenta. JGraph read the series colour for the face,
  `'none'` for a stairs edge, refused `'auto'` (`histogram: unknown color 'auto'`), took any face on
  stairs, and kept one edge across the styles.
- **`Children` is a column** in R2025b - an axes', a figure's, a group's, a transform's, a panel's, a
  tiled layout's and the root's, through the dot and through `get` - and 0-by-0 when there is none;
  JGraph's was a row (1-by-0 empty), so `[ax.Children, ax.Children]` was one row here and two
  columns there, and `get([ax1 ax2], 'Children')` held rows.
- **A replacing plot** resets the axes' `Tag` and `UserData` (plot, scatter, bar, surf, histogram,
  imagesc, area, stem, contour, fill, plot3, errorbar, stairs, semilogy, mesh, image, quiver, pie, a
  tile of a layout); `line`, `patch`, `text` and `rectangle` add without replacing and keep them, and
  so do hold and `'replacechildren'`. R2025b's `cla reset` puts `NextPlot` back to `'replace'`;
  JGraph's kept `'replacechildren'`, so the next plot did not replace. R2025b's replacing plot also
  puts back the axes' colour, font size and line width.
- **A table's lines**: one line from `plot(tbl, ...)` or `plot3(tbl, ...)` has an empty
  `DisplayName` (its legend entry is `data1`); several take the names of the variables that vary
  (`'b'`, `'c'`; `'a, b'` when two channels vary; variable numbers and logical vectors name the
  variables), and a written channel renames its line. JGraph named every line after its y variable.
  `plot3(tbl, xvar, yvar, zvar)` takes the same channels as `plot`, makes a 3-D line per variable
  with `XVariable`, `YVariable`, `ZVariable` and `SourceTable`, labels no ruler, and refuses a line
  spec (`MATLAB:plot3:InvalidTableArguments`, "Invalid number of input arguments. Specify three table
  subscripts after the table."); a name the table lacks is `MATLAB:graphics:chart:VariableNotFound`
  ("The variable 'zz' was not found.", `plot` too); counts that disagree are
  `MATLAB:graphics:chart:NumVariablesMismatch`; writing `XData`, `YData` or `ZData` while its variable
  is named is `MATLAB:graphics:chart:TableToMatrixNotSupported`. JGraph refused `plot3` of a table,
  and refused `plot(ax, tbl, {'a', 'b'}, 'c')` as text data ("Invalid data argument.").
- **A tiled layout's `Title`, `Subtitle`, `XLabel` and `YLabel`** are `matlab.graphics.layout.Text`
  objects with fourteen properties (`String FontName FontSize FontAngle FontWeight Rotation
  HorizontalAlignment VerticalAlignment Interpreter Color Children Parent Visible HandleVisibility`,
  a hidden `FontSizeMode`), no `Type`, `Tag`, `Position`, `Units` or `BackgroundColor`
  (`MATLAB:class:InvalidProperty` through `set`), `HandleVisibility` `'off'` (no search finds them),
  the title 13 points and the others 12, none bold, all 33/255 grey in Helvetica, the y label at 90
  degrees and the x label top-aligned. `title(t, ...)`, `subtitle(t, ...)`, `xlabel(t, ...)` and
  `ylabel(t, ...)` answer the object and take its properties as options (an unknown name is
  `MATLAB:hg:InvalidProperty`, "Unrecognized property Bogus for class Text."; a text property it
  lacks the `set` refusal); `title(t)` is `MATLAB:title:InvalidNumberOfInputs`; writing `t.Title`
  whole is `MATLAB:class:RequireClass`; `delete(t.Title)` keeps it and its words; it dies with its
  layout. JGraph's `t.Title` was the words, `title(t, ...)` took no options and answered the layout,
  and `xlabel(t, ...)`/`subtitle(t, ...)` were refused.
- **A tiled layout in a panel or a tab** (`tiledlayout(p, m, n)`, `'flow'`): the layout is the
  container's child, its tiles the layout's, laid out in the container's own box; a second layout in
  the container takes the first with its tiles; deleting the container takes the layout. JGraph
  refused it ("tiledlayout expects argument 1 to be a whole number").
- **A transform's limits** fit the moved points of a line, a marker chart, a 3-D line and a patch,
  not the moved corners of the data box: a diagonal line turned by `pi/6` fits `XLim [0 1.4]` where
  the box gave `[-0.5 2]`; a gap (NaN) is skipped; a rectangle and a surface fit their corners, which
  are their points. ADR 0376 recorded that R2025b keeps limits fitted under an earlier matrix after a
  `Matrix`-only change; p8ec and p8ee did not reproduce it (rotate then identity, then a translation,
  a scale before and after a `drawnow`, all refit at once).
- **A JGS-dialect tiled layout** stands in no tree walk (its tiles are the figure's children there),
  so the first sweep after it was made - a `delete` or a `cla` runs one - reaped its handle:
  `ishandle(t)` went false, and `nexttile(t)` read the dead handle as a tile location
  ("nexttile: tilelocation is a positive whole number, but got 1E+06."). Verified with
  `jgraph -batch` before the fix.

## Decision

### A histogram's colour words (`JgsGraphicsProperties.Leftovers348.cs`)

In the MATLAB dialect `FaceColor`, `EdgeColor` and `DisplayStyle` of a histogram wrap the entries
the earlier blocks made (`AddHistogramWords348`, run last from `AddAliases` through
`AddLeftovers348`): an unchosen face (null) reads `'auto'`, a stairs face `'none'`
(`HistogramStairsFaceRefusal348` for anything else), an unchosen edge `'auto'`; `'none'` is written
as a transparent colour, so a bar edge or face written `'none'` is drawn as none (null had drawn the
derived edge). Switching the display style swaps the shown edge with the one the other style keeps
(`HistogramOtherEdges348`; bars never shown keep R2025b's grey, `HistogramMadeAsStairs348`). The
builtin's `FaceColor` and `EdgeColor` options go through the same words (`WriteHistogramFace348`,
`HistogramColourWord348`). The JGS dialect reads and writes as before.

### Children as a column (`ChildrenList348`)

The `Children` of a graphics object, a tiled layout and the root are a column in the MATLAB dialect,
0-by-0 when empty; the JGS dialect keeps its row.

### A replacing plot (`ReplacingResets348`, `ResetClearedAxes`)

`JG.AxesEmptying` resets the axes' `Tag` and `UserData` in the MATLAB dialect unless it only
replaces children; `cla reset` puts `ReplaceChildrenOnly` back (a uiaxes' own `'replacechildren'`).

### A table's lines and plot3 of a table (`JgsBuiltins.Leftovers348.cs`)

`plot(tbl, ...)` names its lines with `NameTableLines348` (one unnamed, `RestoreDisplayName` with
the mode automatic; several by the varying variables, `VaryingChannels348`), and a written channel
renames its line (`FollowChannelNames348`). `plot3(tbl, ...)` is `MatlabTablePlot3_348`, an early
block of `Plot3`; `ReplotFromSource` learnt `Line3DPlot`; a 3-D line answers `SourceTable` and the
three channels (`AddChannelVariables348`). `TableVariable348` and `TableLineCount348` refuse in
R2025b's words; `RefuseDataUnderVariables348` refuses a coordinate written under a named variable.
`RefuseTextData` lets a table first through (it took the table form's cell of names for text data).

### A tiled layout's Text (`JgsLayoutText`)

A `GraphObject` per layout and role, made when first reached, with its own property block
(`AddLayoutTextBlock348`, an early return of `JgsGraphicsProperties.Build` as the legend's title
has): the words and the size, weight, angle, interpreter, colour and font live on the layout's
`TextStyle`s and are drawn; the angle, alignments, font name, `FontSizeMode` and visibility are kept
on the object (hidden, the layout holds no words for it). A MATLAB-dialect layout is made with
R2025b's text styles (`MatlabLayoutTextStyles348`). The layout's four names answer the object in the
MATLAB dialect (`AddLayoutTextHandles348`) and the words in the JGS dialect. `title`, `subtitle`,
`xlabel` and `ylabel` of a layout go through `LayoutTextCall348` (the JGS dialect: the words, the
layout answered). The class rows (`layouttext`, `matlab.graphics.layout.Text`) were recorded in
R2025b and added to `u7_classes.out.txt`, `graphics_class_names.txt` and `graphics_class_members.txt`,
and the three generated tables regenerated.

### A tiled layout in a panel or a tab

`tiledlayout(container, ...)` (`TiledLayoutInContainer348`, an early block of the builtin, both
dialects) records the container's layout (`PlaceLayoutIn348`); a tile is an axes of the figure placed
in the container (`TileInContainer348`, from `PlaceTile`), so its fractions are the container's and
the layout's arithmetic is unchanged. In the MATLAB dialect the tree answers R2025b's shape:
`ParentOf` a tile is the layout and of the layout the container, `ChildrenOf` the container lists the
layout for its tiles (`AddContainerAxes348`), `LayoutChildren` and `LayoutHolding` know the container's
layout; `RemoveLayout` takes a container layout's tiles and forgets it. `nexttile(t)` accepts a
container's layout.

### A transform's limits (`PlotObject.TransformPoints`, `GraphicsGroupIndex.MovePlot`)

`PlotObject.TransformPoints` gives the points a transform moves: a line's samples (not a staircase's),
a marker chart's drawn points, a 3-D line's and a patch's vertices. `MovePlot` fits their moved
extents, with ADR 0376's rotation residue cleaned first, and falls back to the box's corners for
every other object. Both dialects.

### The sweep (`KeepLayoutsAlive348`)

`DropUnreachable` keeps every live figure's and container's tiled layout and the text objects reached
on it, whichever dialect made it.

## Consequences

- New parity fixture `oi348_graphics_leftovers` (274 rows from R2025b 25.2, every row agreeing, packed
  and boxed) and test class `GraphicsLeftovers348Tests` (11 cases: the JGS layout through a sweep and
  the JGS dialect's kept answers, and five MATLAB-dialect checks).
- `oi_crowded_ruler_mixed_axis`' divergence rows `pv_ruler`, `pv_ruler_num`, `pv_hist` and
  `pv_hist_num` (ADR 0234) were restamped: the empty `Children` and the histogram's `FaceColor` now
  print R2025b's 0-by-0 and `'auto'` inside them.
- `MatlabCompositionTests` reads a figure's Children as R2025b's column, and `MatlabM80MachineryTests`
  titles a grid with `title(t, ...)` (writing `t.Title` whole is R2025b's refusal now).
- `openfig` rebuilding a file is exempt from the replace reset (`BuildingFigureFile348`): it writes an
  axes' saved Tag before drawing into it with the makers.
- A MATLAB-dialect histogram drawn with `'EdgeColor', 'none'` now has no edge, and a MATLAB-dialect
  layout's shared text is drawn in R2025b's 13/12-point normal-weight grey (window checks).

## Divergences

None opened.

## Still open

- **A replacing plot's colour, font size and line width**: R2025b's newplot puts the axes' `Color`,
  `FontSize` and `LineWidth` back to their defaults (probe p8ea `rep_color`, `rep_fontsize`,
  `rep_lw`); this build keeps them (JG's `ResetAxesForReplace` keeps the background on purpose).
- **Tiny spans**: R2025b's limits around a span of `1e-15` are `[0 1.2e-15]`, of `1e-14` exactly
  `[0 1e-14]`, of `3e-16` `[0 3e-16]`, and a span of `1e-14` at 5 widens to
  `[4.9999999999994005 5.0000000000006004]`; this build answers `[0 1.0000000000000001e-15]`,
  `[0 1.0000000000000002e-14]`, `[0 3.0000000000000004e-16]` and `[5 5.0000000000000098]`, and a
  one-ulp span (a diagonal line turned by `pi/4`, `[0 1.1e-16]` there, `[0 1.2e-16]` in R2025b; a
  turned and moved line at 0.7071) is a single value here (`[-1 1]`, `[-0.5 2]`). The tick rule's
  scored bands were measured from ordinary spans; these need their own regime (probes p8ec, p8ee).
- **The tiled grid's geometry**: a tile's `Position` and `OuterPosition`, the layout's `Position`
  (R2025b `[0.13 0.11 0.775 0.815]`, here `[0 0 1 1]`), a flowing grid's shape (2 tiles in a
  400-by-300 panel: R2025b `[2 1]`, here `[1 2]`) and `'TileSpacing', 'none'` with `'Padding',
  'tight'` differ in a figure and in a panel alike (probe p8eb); a layout nested in a layout
  (`tiledlayout(t, 1, 1)`) is refused. A container's layout draws its tiles; its shared text is not
  drawn (the renderer draws only a figure's layout's).
- **`plot3`**: R2025b's line reads its colour from the colour order (`[0.066 0.443 0.745]`, the
  2-D `plot`'s too), keeps a held axes' 2-D view and turns no grid or box on; this build's reads
  `'none'`, turns the view 3-D under hold and draws the grid and box - `plot3` of arrays as much as of
  a table. A 3-D line lacks `ZDataSource` and `ZDataMode`; `plot3({1}, ...)` is refused in this
  build's words.
- **A layout's Text** displays as a handle number here (a handle is a number, ADRs 0202, 0208), and
  its `Children` reads a 0-by-0 double where R2025b's is a `GraphicsPlaceholder`; `findall(f)` of a
  figure with a layout counts one object fewer than R2025b (101, 102). `text(..., 'Parent', tr)` of a
  transform is refused here ("Invalid or deleted object.").
- **A histogram refused for a stairs face** stays drawn in R2025b and is not drawn here.
- **The window checks** of item 348 (ADR 0372's patch edge and face, ADR 0376's grey edges, the
  `'flat'`/`'auto'` marker words), and this ADR's: a `'none'` histogram edge drawn as none, a stairs
  histogram's `'auto'` edge (drawn in the derived colour, not the series colour), the MATLAB-dialect
  layout text styles, and a layout in a panel or a tab.
