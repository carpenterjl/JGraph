# ADR 0349 — pcolor makes a flat surface, the axes verbs refuse a group, and plot reads a malformed call in its own words

## Status

Accepted, 2026-11-12. Open items 167, 168 and 198, the graphics makers' leftovers from batch 30
(ADR 0255).

## Context

- **Item 167.** `h = pcolor(C)` answered `Type` 'image' here. R2025b's `pcolor.m` is `newplot`, then
  `surface(x, y, zeros(size(c)), c)` (or `surface(zeros(size(c)), c)` for `pcolor(C)`) with
  `AlignVertexCenters` on, then, only when the axes is not held, `View` [0 90], `Box` on and both
  limits set to the data's span. A script that set `'EdgeColor', 'none'` or ran `shading flat` on it,
  or looked for it by `'Type', 'surface'`, reached nothing. Probe `pc1` (71 lines) recorded the
  object's properties, the axes after it, the three shadings, vectors and matrices, a held axes, the
  options, and pcolor.m's refusals.
- **Item 168.** Four leftovers of ADR 0255's probes, and one seen in wave 1 (ADR 0342):
  - `title(g, 'x')`, `hold(g, 'on')` and the other axes verbs took a group or a transform as an axes,
    or read its handle as data. Probes `gv1` to `gv3` ran some 65 verbs with a group and with a transform
    first. R2025b refuses most of them, each in its own words (`Using title with Group is not
    supported.`, `Using hold with hggroup is not supported.`, `Invalid or deleted graphics object.`
    for the ruler verbs, `First argument must be an axes handle.` for `grid` and `shading`, and so on).
    It takes `legend(g, 'a')`, `text(g, ...)` and `cla(g)`.
  - `patch(x, y, c)` with a three-element row `c`: R2025b reads it as one colour, whatever the vertex
    count, and refuses one outside [0, 1] (`MATLAB:hg:ColorBase:BadColorValue`), a logical one
    (`MATLAB:fill:InvalidColorInput`), and scales a `uint8` row from 0 to 255 (probe `pl1`).
  - A malformed `plot` met this build's own words ("plot: unknown option 'Tag'. Use LineWidth, ...")
    where R2025b has three of its own: `MATLAB:plot:InvalidFirstInput` ("Invalid data argument."),
    `MATLAB:plot:DataPairsMismatch`, and `MATLAB:hg:InvalidProperty` for a word it reads as a name. A
    read-only name was `MATLAB:class:SetProhibited` here and `MATLAB:plot:InvalidPropertyValue` there,
    with the same message. Probes `pl1` to `pl3` gave 45 tails after `plot(ax, x, y, ...)`, the log
    plots and `plot3`.
  - `line(g, ...)` on a deleted group read the handle as data. R2025b's makers refuse a deleted parent
    in three ways (probe `dl1`, 26 makers, group, transform and axes).
- **Item 198.** `scatter(tbl, 'Var1', 'Var2')` and `scatter3` of a table were refused after ADR 0255.
  That was fixed by 76393de0 (ADR 0277's gate, `MadeShape.TableNames`), but only with the table first.
  `scatter(ax, tbl, 'Var1', 'Var2')` was still refused: with an axes first the wrapper did not see the
  table, and peeled the capitalised names `Var1` and `Var2` as a property pair (probe `sc1`).

## Decision

### pcolor is R2025b's surface (`JgsBuiltins.Pcolor`)

In the MATLAB dialect `pcolor` runs pcolor.m's own checks in its order: no argument, a vector colour
(`isvector`, a scalar too), mismatched sizes (`LdimMismatch` word for word), complex data, and two
data arguments (R2025b's `deal(args{1:3})` fails with `MATLAB:badsubscript`). Then it makes a surface
at height zero through `JG.Surf`, with `CData` the colour matrix, `FaceColor` 'flat', the surface's
dark grey edge (33/255) half a point wide, and `AlignVertexCenters` on. Vectors stay vectors; a matrix
stays a matrix (a vector beside a matrix is spread over the grid). `pcolor(C)`'s counted `YData` reads
back as a column, as R2025b's does (`SurfacePlot.YColumn`, which a written `YData` also sets). On an
axes that is not held the axes is laid flat, boxed, and given both limits, opened to one unit when the
span is none. A held axes keeps its view and its limits. The `Making` wrapper still takes the trailing
options, so `'EdgeColor', 'none'` and a start of a name work, and `Foo` is refused as `Unrecognized
property Foo for class Surface.` with nothing left behind. A JGS script keeps the heatmap image
that ADR 0022 gave `pcolor`.

### A surface draws in a flat axes, its faces by their first corner

A surface in an axes that is not 3-D was not drawn at all: the 2-D pass drew `IDrawable` plots only.
`I3DDrawable.DrawsInFlatAxes` (a default member, false) lets an object ask to be drawn there.
`SurfacePlot` asks. `FigureRenderer.FlatProjection` gives it `view(0, 90)` through a box shaped like
the plot area, so x and y land where the 2-D rulers put them, a reversed ruler included. A log ruler
has no such camera, and a surface beside one is still not drawn. The same path draws `surf` after
`view(2)`.

A flat face took the mean of its four corners' colours. R2025b's `FaceColor` 'flat' takes the first
corner's, `C(r, c)`, and that is what makes pcolor's cells the colours of `C` and leaves its last row
and column colouring nothing. `SurfacePlot.Palette` now reads the first corner for every surface.

### The axes verbs refuse a group (`JgsBuiltins.GroupRefusals`)

`RefuseGroupsAtAxesVerbs` runs last in `JgsBuiltins.Create`. For each verb R2025b refuses a group, it
wraps whatever that verb's registration ended up as, at every door: one output, several, a
statement. A group or a transform first is refused in that verb's measured words. The verbs are:
- `title`, `subtitle`, `xlabel`, `ylabel`, `zlabel` ("Using title with Group|Transform is not
  supported.");
- `axis`, `hold` and `ishold` (by `hggroup` or `hgtransform`);
- `grid`, `box` (the class by its last word when setting, in full when reading), `colorbar`;
- the ruler verbs: `xlim`, `ylim`, `zlim`, `clim`, `caxis`, `rlim`, `thetalim`, the tick, tick-label,
  tick-angle and tick-format verbs, and `xscale`, `yscale` and `zscale` where they exist;
- `view`, `colormap`, `daspect` and `pbaspect` (a query and a setting refused differently),
  `shading`, `lighting`, `xline`, `yline`, `colororder`, `camlight` and `annotation`.

`axes(g)` names the class R2025b names (`JgsGraphicsCallbackValues.ClassWord`: Group, Transform).
`shading(ax, mode)` now takes its axes, which pcolor's probes needed. `shading faceted` in the MATLAB
dialect turns an edge with a colour of its own black, as shading.m does.

### patch's three-element row is a colour (`RgbRowColour`)

In the MATLAB dialect a 1-by-3 numeric colour argument to `patch`, and to `fill` and `fill3` with one
polygon, is one colour. It is scaled from `uint8` or `uint16`, refused outside [0, 1] or when logical,
and never read as a value a vertex. A column keeps its old reading.

### plot reads a malformed call before it draws (`CheckPlotArguments`)

`MadeShape` gained `PlotGroups` (2 for `plot`, `semilogx`, `semilogy` and `loglog`, the new
`PlotArgumentsMade`; 3 for `plot3`). With it set, the wrapper reads the arguments as R2025b does
before the maker runs. Every one of the 45 tails probed is read this way:
- After the first group the arguments are read a run at a time. Data starts a group.
- A word with an odd count left is a line spec.
- A word with an even count left is a spec when a whole group follows it. The call is refused
  ("Invalid data argument.") when it and the next word are both specs. Otherwise the rest are
  name-value pairs.
- Then data in a name's place is "Invalid data argument.", and a group left short is the pairs
  mismatch.
- Every name is checked before any spec: an unknown one is refused in the property table's words
  (`JgsGraphicsProperties.RefusalOfName`), and a word a spec cannot be is
  `MATLAB:plot:UnrecognizedLineStyleParameter`.

A call this reads as well formed goes to the maker as before. A read-only name refused by
`plot`'s family becomes `MATLAB:plot:InvalidPropertyValue` (`SetMadeProperty`); `line`, `bar`, `stem`
and the rest keep `MATLAB:class:SetProhibited`, as in R2025b. `stairs` keeps the old shape
(`PlotMade`), because R2025b's stairs reads its arguments otherwise. Text first is left to open item
126's refusal, which runs before this.

### A deleted parent is refused (`IsDeadHandle`, `DeadParent`)

A handle minted here that names nothing now, first where a maker takes a group (or for `hggroup` and
`hgtransform`), or after `'Parent'`, is refused before anything is made. `MadeShape.Deleted` picks
the words:
- `line`, `text`, `patch`, `surface`, `image`, `imagesc`, `light`, `rectangle` and the groups:
  `MATLAB:hg:DeletedObject`;
- `plot`, its log forms, `plot3`, `fill` and `fill3` (the new `FillMade`): `MATLAB:hg:InvalidHandle`;
- `stairs`, `stem`, `stem3`, `scatter`, `bar`, `barh`, `area`, `errorbar` and `histogram`:
  `axescheck`'s words, and its "deleted Axes" when the handle was an axes.

### The table after an axes

`PeelMadeProperties` looks for the table after a leading axes too, so `scatter(ax, tbl, 'Var1',
'Var2', ...)` and `scatter3(ax, tbl, ...)` keep their variable names as data.

## Consequences

- New parity fixture `oi_maker_words_group_verbs` (566 rows from R2025b 25.2, every one agreeing,
  none a divergence): pcolor's object, axes and refusals; 45 verbs with a group and with a transform
  and what each leaves; 16 patch and fill colours; 35 plot tails and 10 other makers' words; 26
  makers with a deleted group and with a deleted transform; scatter and scatter3 of a table after an
  axes.
- `oi_maker_options_group_parent`'s row `mk_pcolor` is an agreeing line (re-recorded).
- New tests in `MakerOptionsTests` (48 cases). `MatlabDocumentedChartFormTests`' pcolor test asks for
  a surface. Four rendering tests (`SurfaceLightingTests` three, `Surface3DRenderingTests` one) asked
  for the four-corner mean and now ask for the first corner.
- Every surface's flat faces change colour slightly, to R2025b's. A surface in a flat axes,
  `surf` then `view(2)` among them, is now drawn.

## Measured

- The probes in both engines, after the change: `pc1` differs only in `class(h)` (a handle is a number
  here, as for every object) and in a plain `surface`'s edge colour (below). `gv2` differs only in
  `legend`, `cla` and `xscale`. `pl3` agrees except for the colours later lines took (below). `dl1` and
  `sc1` agree where they were run (a table's several y-variables aside, below).
- `pcolor(magic(4))` and a shaded `pcolor` exported headless in both engines draw the same cells, the
  same colours and the same edges.
- Tests: the two fixtures in both lanes; `MakerOptionsTests`, `MatlabM79TablePiePropertyTests` and the
  graphics, surface and rendering classes listed in the handoff (985 and 461 tests), and 23 parity
  fixtures that draw patches, surfaces or groups, in both lanes.

## Not measured

- A pcolor in a JGraph window: only exported pictures were compared, because a window waits on the
  user.

## Divergences

None opened. Item 167's divergence of ADR 0255 is closed.

## Still open

- **`legend(g, 'a')`** makes a legend in R2025b that labels the group as one entry. Here it is
  refused ("legend wants handles to plotted series").
- **`cla(g)`** in R2025b clears the group's axes of everything but the group. Here it is refused
  ("cla expects a string").
- **An axes verb given a deleted group**: `title(dead)` is `MATLAB:title:InvalidTarget` in R2025b and
  makes a title here. Only the makers were taught deleted handles.
- **`plot`'s value refusals** (`'LineWidth', 'abc'`, `'Tag', 5`, `'Visible', 'maybe'`, `'Color',
  'zz'`) are `MATLAB:plot:InvalidPropertyValue` with the property's own message in R2025b, and this
  build's words here.
- **R2025b leaves the line behind** when `plot` refuses a value or a read-only name. Its colour
  advances the colour order. Here the refusal takes it back, by ADR 0255's rule (probe `pl4`). This
  is measured, not decided.
- **`stem` and `stairs` with a pair after the spec** are `MATLAB:checkpvpairs:EvenPropValuePairs` in
  R2025b.
- **Text first in `plot`**: `plot(ax, 'a', x, y)` is "Invalid data argument." and `plot('a', x, y)`
  "Invalid first data argument." in R2025b 25.2. Open item 126's rule (`RefuseTextData`) answers
  `MATLAB:minrhs` for both.
- **A plain `surface`'s readings**, seen beside pcolor (probe `sf1`): its edge reads 'flat' here and
  [0.1294 0.1294 0.1294] there, its `LineWidth` 0.75 and 0.5, `surface(Z, C)`'s `XData` counts from 0
  here and 1 there with `XDataMode` 'auto' for 'manual', and a meshgrid pair is kept as vectors here
  and matrices there.
- **A colour patch's `CData`** reads [0.251 0.251 0.251] here and [0.1294 0.1294 0.1294] in R2025b.
- **`scatter(tbl, 'Var1', {'Var2', 'Var3'})`** makes two scatters in R2025b and is refused here.
- **`xscale`, `yscale` and `zscale`** do not exist here.
- **A surface beside a log ruler** in a flat axes is not drawn.
