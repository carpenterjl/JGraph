# ADR 0360 — A group is its axes' own object, findall reaches what R2025b's does, and the makers' leftovers after ADR 0349

## Status

Accepted, 2026-11-14. Open items 296, 297 and 313 (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), chips wave 4, agent W. The recordings are six headless probes of 2026-11-14 (kept outside
git): `p296` (32 readings of a group's Visible, a transform's Matrix and both through each engine's
own figure file), `p297` and `p297b` (what `findall`, `findobj`, `allchild` and `Children` list for an
axes with a group, a legend, a colour bar, a toolbar, a grid and labels, and where a legend and a
colour bar live), `p313` and `p313b` (a legend of a group, `cla` of a group, the axes verbs given a
deleted object, plot's value refusals, stem and stairs counting their words, text first in plot,
scatter of a table with several variables, and `xscale`, `yscale`, `zscale`).

## Context

- **Item 296.** ADR 0342 put a group in the tree a script walks but kept it beside the model: a book
  of members in the scripting layer. Three things followed, recorded there as divergences. The
  `.graph` document saved the members and forgot the group. Hiding a group wrote its members' own
  `Visible` (R2025b leaves a member's `Visible` `'on'` and does not draw it). A transform rewrote its
  members' coordinates, so a member read back moved data and a `.fig` this build wrote held the moved
  data under an identity matrix. R2025b, measured: a member keeps its own data and its own `Visible`;
  a hidden group's members are left out of the limits as well as the drawing; a transform's members
  are fitted where its matrix puts them (`translate` `[0 5 0]` of `[0 1]`/`[0 0]` gives `YLim`
  `[4 6]`, `scale` 3 gives `XLim` `[0 3]`, nested transforms compose); a text in a transform reads
  back its own `Position`; a member taken out keeps its own data; R2025b's own file keeps the group,
  its `Visible`, its `Matrix` and the order of the axes' children (`last xf grp plain`).
- **Item 297.** This build's `findall(ax)` walked an axes' parts: five rulers, the grid, the toolbar
  and its six buttons, and the legend, colour bar and bubble legend whether shown or not. R2025b's
  `findall(ax)` is the axes, its children, then its hidden children - the title, subtitle and labels
  that exist, in the order Title, Subtitle, XLabel, YLabel, ZLabel - and then the next level. A label
  exists once a script made it or read it (`ax.Title` read makes one); `title(ax, 'T')` makes no
  subtitle. A legend and a colour bar are the children of the axes' figure (or panel, or tiled
  layout), standing just in front of their axes, newest showing first (`legend colorbar axes legend
  axes` for two axes); `ancestor(lg, 'axes')` is empty; `findall` of a legend or a colour bar is the
  object alone. With `ShowHiddenHandles` on, `findobj` is `findall`.
- **Item 313.** ADR 0349's still-open list: `legend(g, 'a')` is one row standing for the group (its
  `PlotChildren` is the group, the group's `DisplayName` takes the label, the group's icon stays
  `'off'`); `cla(g)` is cla.m's `cla(gca, hsave)` - the current axes is cleared of everything but the
  objects named (a deleted one is `MATLAB:clo:invalidHandle` before anything is cleared, and one the
  clearing takes is the same error after); `title(dead)` and fourteen other axes verbs refuse a
  deleted group or axes in their own words; plot's value refusals are `MATLAB:plot:InvalidPropertyValue`
  with the property's own message, and refuse `LineStyle 'q'`, `Marker 'w'`, `MarkerSize -1`,
  `LineWidth -1` and `Color [2 0 0]`, which this build took; stem and stairs run R2025b's
  `checkpvpairs`; text first in plot is "Invalid first data argument." without an axes before it;
  `scatter(tbl, 'Var1', {'Var2', 'Var3'})` makes two scatters; `xscale`, `yscale` and `zscale` are
  R2025b's ruler functions (`rulerFunctions.m`, read).

## Decision

### A group lives in its axes' model (`GraphicsGroupModel`, `AxesModel.Groups`)

The scripting layer's `JgsGraphicsGroup` moves to the core model as `GraphicsGroupModel`; the
scripting name stays as a global alias (`JgsGraphicsGroup.cs`), so none of the verbs changes its
spelling. The axes keeps its groups, nested ones included, oldest first, in `AxesModel.Groups` (a
snapshot list: adding or removing a group replaces it), and the group's parent is its axes, so a
group's change invalidates the figure. The static book of groups is gone: `Remember` adds the group to
its axes, `GroupsIn`, `GroupOwning`, `DeleteGroupsIn` and `DeleteGroup` read and change the axes'
list, and `ForgetGroups`, `ForgetGroupsOutside`, `TakeGroups` and `PutGroups` have nothing left to do.
A member still lives in its axes' `Plots`, `Annotations` or `Lights`.

### A group's Visible and a transform's Matrix are read when drawing and fitting (`GraphicsGroupIndex`)

`GraphicsGroupIndex.For(axes)` (null for an axes without groups, the common case) answers, for a
member, whether every group around it is visible and the matrix the transforms around it compose to
(outermost last). The renderer's flat, 3-D and polar passes and the axes' annotations skip a member a
hidden group holds; a moved member is drawn through `GroupMatrixMapper` (the axes' mapper behind the
matrix's flat part, a 2-by-2 and an offset) in a flat axes, and through `Projection3D.WithModel` (the
camera seeing each point after the matrix) in a 3-D axes and for a surface drawn flat. The
autoscaler leaves a hidden group's members out and fits a moved member by its data box moved
(`GraphicsGroupIndex.Move`, the eight corners: exact for `makehgtform`'s translations and scalings, a
box round a rotation). A group's `Visible` is its own (the property writes the group alone) and
`hggroup(..., 'Visible', 'off')` is now read. `SetMatrix` stores the matrix; a member's coordinates
are never written.

### The `.graph` document keeps the groups (`FigureMapper.Groups`, `GroupDto`)

An axes' DTO carries `Groups` (null when it has none, and in every older document, which reads back
as before): each group's kind, tag, visibility, display name, matrix (row-major, null for the
identity), its outer group by index, its members in the order they joined (a plot, a text, a light or
a group, each by its index in its list), and the drawn object it stood before among the axes'
children. Reading puts every group back in its axes, the members in order, and gives each group a
`Seat` - the creation number of the object it stood before - which `AddDrawnChildren` places it by,
so `ax.Children` comes back in its saved order. ADR 0240's `.fig` writer writes a transform's `Matrix`
now that the members hold their own data.

### findall reaches what R2025b's reaches (`SearchedUnder`, `AddAxesAndFurniture`)

In a MATLAB-dialect script:

- **`findall`** (and `findobj` while the root shows hidden handles) walks `SearchedUnder`: an axes'
  children front first, then the title, subtitle and labels that exist, in R2025b's order; nothing
  under a legend, a colour bar or a bubble legend; every other object as before (`DescendantsOf`).
  The rulers, the toolbar and its buttons and the grid stay in `DescendantsOf`, which keeps their
  handles alive and which a JGS script's `findall` still walks.
- **A legend, a colour bar and a bubble legend are their axes' figure's or panel's children.**
  `ChildrenOf(axes)` leaves them out; `ChildrenOf(figure)` and `ChildrenOf(panel)` list each axes
  followed by its shown furniture, oldest showing first, so `Children` reads it newest first in front
  of its axes. `AxesModel.ShownOrder` stamps each one when it is shown. `ParentOf` answers the axes'
  parent. The `.fig` writer takes the furniture from the axes itself and leaves it out wherever the
  tree lists it, so hgsave's layout is unchanged.
- **`title(ax, 'T')` makes no subtitle object**: the subtitle is made when a second output or a
  subtitle text asks for it.

### The makers' and axes verbs' leftovers

- **`legend(g, ...)`** (`LegendTargets`): a group or a transform in the leading handle array is one
  row, its `Plot` the group's first member a legend can show (the swatch) and its new
  `LegendEntryModel.Group` the group. The row's words, `String`, `PlotChildren` and a written `String`
  read and write the group's `DisplayName` (a new property of the group); such a row takes no
  `dataN` number. A legend made again forgets the groups it is not asked for.
- **`cla(h, ...)`** in the MATLAB dialect (`MatlabCla`): an axes first is cleared as before; any other
  handle is kept (`ClearAxesKeeping` clears the current axes of the rest, a group not kept with
  everything it holds), with `'reset'` resetting the axes after. A word other than `reset` is
  `MATLAB:clo:unknownOption`. `cla` answers its axes when an output is asked for.
- **The axes verbs given a deleted object** (`RefuseDeadTargetsAtAxesVerbs`): `title`, `subtitle`, the
  labels, `hold`, `grid`, `box`, `axis`, the ruler verbs, `legend`, `cla`, `colorbar` and `view` refuse
  a handle minted here that names nothing now, in R2025b's words for a deleted group or a deleted axes
  (the handle's retired class tells them apart).
- **plot's values** (`PlotValueRefusal`, at the end of `CheckPlotArguments`): after the names, each
  value given to `LineWidth`, `MarkerSize`, `Tag`, `DisplayName`, `Visible`, `Color`, `LineStyle` or
  `Marker` is read as R2025b reads it, in order, and refused as `MATLAB:plot:InvalidPropertyValue` with
  that property's message. Other names keep their old path.
- **stem and stairs** (`CheckPvPairs`, `MadeShape.PairFlags`, `StairsMade`): the words from the first
  one on, a leading `filled` flag (any start of it, stem only) and a leading line spec counted two to
  a part; one word left is `MATLAB:checkpvpairs:UnrecognizedLineStyleParameter`, an odd count
  `MATLAB:checkpvpairs:EvenPropValuePairs`.
- **Text first in plot** (`RefuseTextData`): `plot`, `semilogx`, `semilogy`, `loglog` and `plot3`
  answer `MATLAB:plot:InvalidFirstInput`, "Invalid first data argument." for a char row with no axes
  before it and "Invalid data argument." otherwise.
- **A table channel naming several variables** (`TableRuns`, `ChartPerRun`): a cell of names, a string
  array or variable numbers make one scatter (or scatter3) per variable, paired where several channels
  name several (`MATLAB:graphics:chart:NumVariablesMismatch` when the counts differ), each held beside
  the last and named by its varying variable; the axes' `NextPlot` is put back.
- **`xscale`, `yscale`, `zscale`** (`JgsBuiltins.RulerScale`): rulerFunctions' reading - an axes, an
  array of them or `gca`; a colour bar or a polar axes refused in their words; a value `'linear'` or
  `'log'`, any start of one in any case, a string or a one-element cell; anything else
  `MATLAB:rulerFunctions:InvalidValueScaling`; a setting call asked for an output
  `MATLAB:nargoutchk:tooManyOutputs`; a datetime ruler refuses a log scale.

## Measured

- **The probes in both engines after the change.** `p296` agrees but for `tf_rot_xlim` (below). `p297`
  and `p297b` agree on every axes row; the figure-level rows differ where this build has no default
  menus and toolbar in a figure (below). `p313` and `p313b` agree where the fixture keeps them.
- **R2025b reading this build's `.fig`** (`pfig_w` here, `pfig_r` there): the group comes back hidden
  with its member's own `Visible` `'on'`, the transform with its `Matrix` (`[0 5 0]` translation) and
  its member's own `YData` `[0 0]`, the children in their order (`xf grp plain`).
- **New parity fixture `oi_group_own_findall_makers`** (335 rows from R2025b 25.2, one divergence),
  packed and boxed.
- **`oi_group_tree_annotation_pane`** re-recorded: its row `file_group` is an agreeing line (ADR
  0342's divergences closed).
- **`oi_onoff_states_save_v73`**: its row `ob_findobj` (ADR 0235's "findobj counts one handle more")
  agrees now - the extra handles were the axes' legend and colour bar - and is an agreeing line.
- **`oi_categorical_ruler`, `oi_crowded_ruler_mixed_axis`** keep R2025b's `MATLAB:minrhs` for text and
  one argument after it in plot (probe_126), which the text-first rule leaves alone.
- **Unit tests**: `MatlabLivingGraphicsTests` (a hidden group's member keeps its `Visible`; a
  transform's member keeps its data and the limits move), `MatlabHandleGraphicsVerbTests` (findall
  reaches no ruler), and the new `GroupOwnObjectTests`.

## Not measured

- **A JGraph window.** The drawing was checked through offscreen exports only.
- **Any release but R2025b.**
- **A light in a hidden group** (it is still lit), a hit test through a transform, and a legend of a
  group with nothing a legend can show (R2025b makes a row; here none).

## Consequences

- A script that hides a group and reads a member's `Visible`, or moves a transform and reads a
  member's data, gets R2025b's answers; one that relied on the old ones gets different numbers.
- A group, a transform and their matrix survive `savefig` and `openfig` of this build's own file and
  `copyobj` of an axes.
- `findall(ax)` lists far fewer objects; a script that found an axes' rulers with `findall` finds
  none, as in R2025b. A legend's or colour bar's `Parent` is the figure (or panel) in MATLAB code, and
  `ax.Children` no longer lists them.
- plot refuses values it used to take quietly; stem and stairs refuse odd word counts.
- New builtins `xscale`, `yscale`, `zscale` (dispatch sweep `45w`).

## Divergences

- **plot leaves nothing behind when it refuses a value.** R2025b draws the line first and refuses the
  value after, so the refused lines stay in the axes (fixture row `pv_left`: 42 lines there, 10 here, the ten calls it took); this
  build reads every value before it draws, as ADR 0255's take-back rule wants.

## Still open

- **A figure's default menus and toolbar** (`uimenu`, `uitoolbar` and their tools under
  `figure('Visible', 'off')`) and its annotation pane before any annotation are in R2025b's
  `findall(f)` and `allchild(f)`; this build has none of them.
- **A legend's and a colour bar's default `Tag`** are `'legend'` and `'Colorbar'` in R2025b and empty
  here; a tiled layout's legend has the layout as its parent there and the figure here.
- **`get([], 'Type')`** is `[]` in R2025b and refused here.
- **A rotation by `pi/2`** leaves a span of 6e-17 that R2025b widens to `[-1 1]` and this build's tick
  rule keeps (`tf_rot_xlim`).
- **`legend(ax)` after a group was made** lists the group's members here; R2025b lists only what is
  not in a group (and a group whose icon is `'on'`).
- **A legend standing for a group** is written to a `.fig` and to the `.graph` document as its first
  member's row.
- **`stem(x, y, 'f')`**: R2025b reads any start of `filled`; this build's stem knows the whole word.
- **`plot("a", x, y)`** is "Invalid data argument." in R2025b (a string, not a char row); here the
  string reaches the refusal as a char row and is "Invalid first data argument."
- **`xscale(h)` of a chart** (a heatmap) is `MATLAB:Chart:UnsupportedConvenienceFunction` there and
  `MATLAB:rulerFunctions:InvalidObject` here.
- **A table channel given by variable numbers** keeps the numbers as `YVariable` in R2025b; here it
  reads back the names.
