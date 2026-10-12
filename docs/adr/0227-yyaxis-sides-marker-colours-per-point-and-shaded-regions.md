# ADR 0227 — Both sides of a yyaxis axes, a marker chart's colour per point and alphas, and shaded regions: open items 106 to 108

## Status

Accepted, 2026-10-11. The fourteenth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), the three items batch 13 filed that were ready (ADR 0226). Every answer was recorded
from R2025b (probes `probe_106` to `probe_106d`, `probe_107`, `probe_108` and `probe_108b` in the
open-items scratch, then the fixture `oi_yyaxis_markers_regions`). As in batches 12 and 13, each item
turned out wider than filed. Item 105's histogram half was decided by the user the same day: a
histogram stays solid (below).

## Context

- **Item 106.** Filed as "`ax.YAxis` under `yyaxis` is one ruler". Under R2025b's `yyaxis` the axes
  has two sides, and this build had only the rulers:
  - `ax.YAxis` answered the active ruler; R2025b's is 2-by-1, left first, and
    `ax.YAxis(2).Limits = …` failed here in the interpreter ("Cannot index into 'level' …"), as did
    any write through an indexed handle a property answers.
  - R2025b inks each ruler in the colour order's entry for its side (the left one only when the
    axes was empty), draws every line on a side in that colour, and sets the line-style order to
    `- -- : -. o- ^- *-`, which each side steps through one plot at a time; here every plot took
    the next colour and a solid line.
  - `ax.Children` lists only the active side's plots and `cla` empties only the active side; here
    both sides. `YAxisLocation` names the active side; here always the left. The legend lists the
    left side first.
  - A ruler's `Color` read its tick-label ink rather than the colour `YColor` sets, so the two
    disagreed; a ruler had no `ColorMode`, and its `LimitsMode` was a constant `'auto'`.
  - `yyaxis` refused a bad side and a polar axes in JGraph's words.
- **Item 107.** What batch 13 left of R2025b's marker charts:
  - `CData` as an m-by-3 colour per point was refused; a column of values read back as a row; a
    three-number colour read back rounded to bytes; a property write with the wrong count of rows
    was refused (R2025b keeps it).
  - `scatter3`'s `MarkerEdgeColor` painted the whole cloud and its `MarkerFaceColor` filled it;
    `scatter3` and `bubblechart3` had no `MarkerFaceAlpha` or `MarkerEdgeAlpha` (refused as options),
    and `bubblechart3` drew open and opaque.
  - A patch's `FaceAlpha` and `EdgeAlpha` refused `'flat'` and `'interp'` and clamped out-of-range
    numbers silently; a bar's clamped too; a scatter's took NaN as `'flat'`. All the refusals were
    in JGraph's words. A patch's two alphas were not saved at all (since batch 13 moved them off the
    object's opacity).
  - `scatter(x, y, sz, 'q')` and `scatter3`'s refused a bad colour word in JGraph's words.
- **Item 108.** `xregion` and `yregion` did not exist.

## Decision

**Item 106.**

- `YAxis` answers both rulers as a column whenever the axes has two. The interpreter writes through a
  property that answers handles and a subscript (`ax.YAxis(2).Color = c`, `ax.XAxis(1).Color = c`):
  the scratch slot's one handle is an array of one, and a property that answered several is a
  reference when the write goes on to a property of the one it picks — `p.YData(2) = 9` stays a
  write into numbers, however much `[1 2 3]` looks like figures 1 to 3. `{ax.YAxis.Color}` spreads.
- `JG.YyAxis` makes both sides at once, inks the rulers (`AxisModel.Ink`, which `YColor` and a
  ruler's `Color` now share) and sets the yyaxis line-style order. `AxesModel.TakeSeriesSlot(side)`
  counts each side on its own: the colour is the side's entry, the style steps a plot, and
  `PlotObject.SeriesSide` (saved) records that the seat is a side's, which the renderer and
  `PeekSeriesColor` read. A line given its colour still steps its side's style; `stem` takes a side's
  seat. Replacing and `cla` restart a side; `delete` does not; `cla reset` makes the axes one-sided.
- `Children` filters to the active side; `findobj` still sees both. `YAxisLocation` reads the active
  side. A new legend row goes after the last row of its own ruler or an earlier one.
- A ruler answers `ColorMode` and a real `LimitsMode`. `yyaxis` refuses in R2025b's words
  (`MATLAB:axis:UnknownOption`, `NotAxesGCA`, `NotAxesArgument`), and `LineStyleOrderIndex` reads the
  active side's count.

**Item 107.**

- One reader, `ReadMarkerColors`, takes R2025b's three shapes for every marker chart: a triplet,
  an m-by-3 colour per point (`ScatterPlot`/`Scatter3DPlot.PointColors`, saved), or a value per point.
  The numbers as written, with their shape, are kept in `WrittenColorData` (saved), which `CData`
  reads back while the chart is drawn from them; every channel's setter drops it, so no other road
  can leave it stale. The verb refuses another shape with `MATLAB:scatter:InvalidCData`; the
  property keeps it.
- `Scatter3DPlot` gains `EdgeColor`, `FaceColor`, `FaceAlpha` and `EdgeAlpha` (saved), read and written
  in the 2-D chart's words; `'filled'` leaves the edges off; a bubble chart in space is filled at 0.6.
- `CheckedAlpha` refuses an alpha as R2025b does on the class it is set on: the datatypes family
  for scatter, bubble, bar and region, the `dt_conv` family for a patch (whose creation-time refusal
  carries no property lead, as R2025b's does not). A patch's alphas take `'flat'` and `'interp'`
  (`PatchAlphaSource`, saved, with the two numbers); one `FaceVertexAlphaData` value is the whole
  patch's. A colour word refused in a marker chart's own arguments is
  `MATLAB:checkpvpairs:UnrecognizedLineStyleParameter`.

**Item 108.** `ConstantRegionPlot` is a band across the axes between two values, kept in the order
written; it reports its finite extent, so the limits widen to show it, as R2025b's do; it draws under
the data (`Layer` 'bottom' is ZOrder −1) in R2025b's grey at 0.3, or the colour order's entry once a
`SeriesIndex` names one. `xregion` and `yregion` take a pair, two vectors, a two-row matrix (a band a
column) or a two-column one (a band a row), on a named axes or the current one, then any of the band's
properties; they do not clear the axes, return a column of handles for several bands, and refuse in
R2025b's words. The class, its `properties` order and its `isa` chain were regenerated from R2025b's
probes (`graphics_class_names.m`, `graphics_class_members.m`, `u7_classes.m`), and dispatch sweep 14
measured the two names.

**Item 105, the histogram half.** A histogram's default `FaceAlpha` stays 1, where R2025b's is 0.6:
the user's decision (2026-10-11). The default colour order is still open item 105.

## Divergences

- **A histogram is solid by default** (`FaceAlpha` 1); R2025b's is 0.6. Kept by decision.
- ~~**Under `yyaxis`, `ax.ColorOrder` is the whole order**; R2025b's holds only the active side's colour.
  `colororder` after `yyaxis` leaves a side's plots in the side's colour where R2025b recolours them.~~
  Lifted by ADR 0228.
- ~~**A ruler `yyaxis` inked reads `ColorMode` `'manual'`**; R2025b's reads `'auto'`.~~ Lifted by ADR 0228.
- ~~**The yyaxis line-style order reads `-o`, `-^`, `-*`** where R2025b writes `o-`, `^-`, `*-`, and it
  has seven entries even when the axes held plots already (R2025b's then has fourteen).~~ Lifted by
  ADR 0228; the fourteen was `numel` of R2025b's 7-by-2 char matrix, not a longer order.
- ~~**`ax.YAxis = …` is refused in JGraph's words**; R2025b's is `MATLAB:class:SetProhibited`.~~ Lifted
  by ADR 0228.
- ~~**`scatter3` and `bubblechart3` answer no `AlphaData` or `AlphaDataMapping`.**~~ Lifted by ADR 0228.
- **A `CData` property written with the wrong count of rows is drawn in the chart's one colour.**
- **A patch draws `'interp'` alpha as `'flat'`**, a value per face.
- ~~**A region's `Value` written as a column reads back as a row**; **`FaceColorMode` `'auto'` puts the
  grey back** where R2025b keeps the colour; **datetime and duration bands are refused**.~~ Lifted by
  ADR 0228 (a categorical band is still refused there).
- **Padded limits (ADR 0073) take regions into account** as they take any data.

## Consequences

- Silent wrong answers fixed: a yyaxis axes drew every plot in the next colour, solid; `cla` and
  `Children` reached both sides; `scatter3`'s edge colour painted the cloud; out-of-range bar and
  patch alphas clamped; a saved patch lost its alphas.
- Lifted: ADR 0226's "`ax.YAxis` is one ruler", the m-by-3 `CData`, the patch `'flat'` alpha, the
  scatter3/bubblechart3 alphas and edge colour, the alpha refusals' words, and "`xregion` and
  `yregion` are not built".
- Fixture `oi_yyaxis_markers_regions`, 135 lines, all exact. `OpenItemsBatch14Tests` (38 cases).
  Tests that pinned the old answers now hold R2025b's: `MatlabYyAxisTests` (a bad side), `MatlabPolarPlotTests` (a bad colour word) and `MatlabM79TablePiePropertyTests` (a table variable's `CData` is a column).

## Still open

Filed in the open-items list: 105 (R2025b's default colour order), 109 (`get(h)`'s field order,
`UserData`'s empty and the legend's `data1`), 110 (`patch` clears the axes; a replaced plot's handle
still takes writes), 111 (the yyaxis and region remainders above).
