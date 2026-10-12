# ADR 0228 — `get`'s order, R2025b's legend, the low-level verbs and dead handles, and the yyaxis and region remainders: open items 109 to 111

## Status

Accepted, 2026-10-11. The fifteenth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), the three items batch 14 filed (ADR 0227). Every answer was recorded from R2025b
(probes `probe_109` to `probe_109c`, `probe_110`, `probe_111`, `probe_111b`, `probe_ro` and
`probe_stress` in the open-items scratch, then the fixture `oi_get_legend_handles_sides`). As in
batches 12 to 14, each item was wider than filed. Item 110's rule — a handle to what a replacing plot
deleted is dead — then caught two verbs that had been drawing only their last piece.

## Context

- **Item 109.** Filed as three gaps in what `get` answers:
  - `get(h)` listed fields alphabetically; R2025b lists them in `properties(h)`'s order, and `set(h)`
    likewise. `get` of several objects of one kind answered a cell; R2025b's is a struct column.
    `groot` had no row in the class tables.
  - An unset `UserData` read `zeros(1, 0)`; R2025b's is `[]`, 0-by-0, on every object.
  - A legend row for a series with no `DisplayName` read `''`. R2025b's legend names it `data1`,
    `data2`, … in the order of the visible rows, writes that name into the series' `DisplayName`
    while the script has set none, renumbers when a row goes, and gives the names back when the
    legend is deleted. Underneath that, this build's `legend` was the JGS verb: `legend off`,
    `hide`, `show`, `toggle`, `boxoff` and `boxon` were read as names; name-value options after the
    labels were read as labels; extra labels went unwarned; the legend's `String` was read-only;
    `PlotChildren` was missing; the axes' `Legend` read a hidden legend rather than `[]`; and a
    replacing plot left the legend where R2025b's `newplot` deletes it, while `cla` deleted it where
    R2025b's keeps it.
- **Item 110.** Filed as "`patch` clears the axes; a replaced plot still takes writes":
  - `patch`, `surface` and `rectangle` are low-level in R2025b: they never call `newplot`, so they
    add to the axes whatever `hold` says and leave its title, limits and view alone. Here all three
    replaced, `patch` and `surface` with a 3-D view (R2025b's `view` stays `[0 90]` on a flat axes).
  - What a replacing plot or `cla` removed answered `ishghandle` false but still took
    `h.LineWidth = 2`, `set`, `get` and a dot read; R2025b refuses each with
    `MATLAB:class:InvalidHandle`, "Invalid or deleted object.", and `delete` of it does nothing.
- **Item 111.** ADR 0227's yyaxis and region divergences:
  - Under `yyaxis`, R2025b's `ax.ColorOrder` holds the active side's colours only (one colour each
    at first), `colororder` and a `ColorOrder` write set that side's order, its plots cycle through
    it colour first and then style, and `ColorOrderIndex` counts within it. Here one order served
    both sides.
  - A ruler `yyaxis` inked read `ColorMode` `'manual'`; R2025b's reads `'auto'`.
  - The yyaxis line-style order read back as a cell of `-o`-style words; R2025b's is the char matrix
    `['- ';'--';': ';'-.';'o-';'^-';'*-']`. ADR 0227's "fourteen entries" was `numel` of that 7-by-2
    matrix, not a longer order. A written order reads back as written: a char row or char matrix as
    char, a cell or string array as a column cell.
  - Writing a read-only property refused in JGraph's words; R2025b's sentence is
    `MATLAB:class:SetProhibited`, "Unable to set the 'X' property of class ''Word'' because it is
    read-only.", on every graphics object, `ax.YAxis` among them. `ax.XAxis = ax.XAxis` is allowed,
    and another axes' ruler is `MATLAB:hg:TryingToReparentConnectedObject`.
  - `scatter3` and `bubblechart3` had no `AlphaData` or `AlphaDataMapping`.
  - A region's `Value` written as a column read back as a row; its `FaceColorMode` `'auto'` put the
    grey back where R2025b keeps the colour until the series asks for another; `SeriesIndex` 0 was
    refused; datetime and duration bands were refused.
  - `SeriesIndex` 0 on a line or scatter was refused; R2025b takes it as "no series" and draws the
    order's first colour.

## Decision

**Item 109.**

- `JgsGraphicsProperties.NamesOf` orders a listing by R2025b's `properties` order (`MembersOf`) in
  the MATLAB dialect; JGraph-only names follow. `get` of several handles of one kind is a struct
  column there. `groot` joins the class probes (`graphics_class_names.m`, `graphics_class_members.m`)
  and the regenerated tables.
- `UserData` defaults to `[]` in the MATLAB dialect.
- A MATLAB-dialect `legend` is its own verb (`JgsBuiltins.Legend.cs`): the six commands, a leading
  handle array, labels, then options found by name among the legend's properties, and
  `MATLAB:legend:IgnoringExtraEntries` for labels past the series. `legend` alone on a legend there
  leaves it as it is. JGS's `legend` is unchanged.
- `PlotObject.DisplayNameIsManual` tells a script's name from one the legend gave
  (`NameAutomatically`); `LegendModel.NamesUnnamed` marks a MATLAB legend, whose `NameUnnamedRows`
  numbers the visible unnamed rows and whose `ClearUnnamedRows` gives the names back. Both are saved
  (`DisplayNameManual`; an older document's non-empty name counts as the script's).
- `LegendModel.Hidden` is `legend hide`: the legend stays, its `Visible` reads `'off'`, and it is not
  drawn (`Shown`, which the renderer and the figure control now read). The legend's `String` is
  writable and lists the visible rows; `PlotChildren` answers the series. The axes' `Legend` is `[]`
  without one. A replacing plot deletes the legend (`ResetAxesForReplace` announces it); MATLAB's
  `cla` keeps it and `cla reset` deletes it.

**Item 110.**

- `JG.AddingWithoutReplace()` is a scope in which `PrepareAxes` answers the current axes untouched.
  In the MATLAB dialect `patch`, `surface` and `rectangle` draw inside it; `patch` and `surface`
  leave a flat axes flat and `surface` leaves the grid alone. `fill` still replaces, as R2025b's does.
  JGS's `patch` still replaces.
- `view` and `View` answer `[0 90]` for a flat axes, `[az, el] = view(ax)` gives two outputs, and a
  `View` write other than `[0 90]` makes the axes 3-D.
- `JgsHandleRegistry.TryGet` treats an object as gone in the MATLAB dialect when it is
  `BeingDeleted` and parented nowhere, or a deleted legend, and retires its handle on the spot, so
  every dot, `set` and `get` refuses it as R2025b does. A queued `DeleteFcn` still reads its own
  object (`DeliveringDeletionOf`). `GraphObjectLifecycle.Revive` is public so a legend made again
  comes back live under a new handle.
- That rule caught two verbs that each drew only their last piece, because every piece after the
  first replaced the one before: `boxchart(…, 'GroupByColor', g)` drew one colour group and `slice`
  with several planes drew one plane. Both now hold after the first piece and restore `hold` after.

**Item 111.**

- `AxesModel.SideColorOrder(side)` is a side's own order, set by `yyaxis`: the left side gets the
  colour its ruler was inked in (its ruler's colour when plots were there), the right side the next
  colour in the cycle. `TakeSeriesSlot` and `PeekSeriesColor` cycle a side's colour first, then its
  style; `ColorOrder`, `colororder` and `ColorOrderIndex` read and write the active side's order.
  Replacing and `cla` clear the side orders.
- `AxisModel.InkIsAutomatic` marks ink `yyaxis` gave, so a ruler's `ColorMode` reads `'auto'` until a
  script inks it.
- `AxesModel.LineStyleOrderWords` and `LineStyleOrderIsList` keep the order as written; the yyaxis
  words are R2025b's.
- In the MATLAB dialect a read-only write is refused with R2025b's `SetProhibited` sentence on every
  graphics object, `YAxis` included; `XAxis` and `ZAxis` take their own ruler back and refuse
  another.
- `Scatter3DPlot.AlphaData` and `AlphaDataMapping`: any length, a scalar for every point, R2025b's
  refusals (`MustBeVector`, `NumericMatrixType`, `InvalidEnumValue`); with `MarkerFaceAlpha` or
  `MarkerEdgeAlpha` `'flat'` each point takes its own opacity. The 2-D scatter shares the reader.
- `ConstantRegionPlot` keeps a column `Value` as a column; under `FaceColorMode` `'auto'` it keeps a
  written colour while it has no series and takes its series' colour when it has one or is given
  one; it takes `SeriesIndex` 0, drawn grey (stored as −2); and it holds datetime and duration
  bands (`ValueKind`, `TimeFormat`, `TimeZone`), which configure the ruler as R2025b's do and refuse
  R2025b's mixings in its words. All saved.
- `SeriesIndex` 0 on a line or scatter is taken in the MATLAB dialect.

## Divergences

- **The default colour order and the axes' ink are this build's**: R2025b's order is open item 105,
  and its ruler colour is `[0.1294 0.1294 0.1294]` where this build's is `[0.251 0.251 0.251]`.
  The fixture compares a colour with the order's entry, not with a value.
- **The axes answer no `ColorOrderMode` or `LineStyleOrderMode`**, which R2025b has.
- **A ruler cannot be constructed by name** (`matlab.graphics.axis.decorator.DatetimeRuler`), and
  `class` of a ruler or any graphics handle is `double` (ADR 0051).
- **A ruler answers `Type`** and refuses its write as read-only; R2025b's ruler has no `Type`. A
  ruler's `UserData` reads; R2025b's ruler has none.
- **`xlim` and `ylim` of a datetime or duration ruler answer numbers**, not datetimes or durations.
- **A categorical band is refused**; R2025b draws it.
- **`SeriesIndex` `'none'` on a line or scatter is refused.**
- **`hggroup` and `hgtransform` are not counted among the axes' children.**
- **`legend show`, `toggle` and `boxoff` answer the legend's number**; R2025b's answer the `Legend`
  object (ADR 0051), and a `Visible` or `Box` read displays as `OnOffSwitchState` where this build's
  is a char row.
- **Padded limits (ADR 0073) apply to a patch** as to any data: `[0.75 6.25]` where R2025b's are
  `[1 6]`.

## Consequences

- Silent wrong answers fixed: `boxchart` with `GroupByColor` drew only its last group, and `slice`
  with several planes only its last plane; `patch`, `surface` and `rectangle` cleared the axes; a
  replaced plot took writes; every unnamed legend row read `''`; `legend off` and its kin were
  names; a yyaxis side drew its plots in colours from the other side's order.
- Three stress scripts (outside git) read a plot that a later hold-off call had replaced; R2025b
  refuses each with `MATLAB:class:InvalidHandle` (`probe_stress`), so they now hold: `stess_39`,
  `stess_44` and `stess_49`. Building the test project does not rebuild `jgraph.exe`; `stess_27`
  failed on a stale CLI until it was built.
- Lifted: ADR 0227's yyaxis `ColorOrder`, ruler `ColorMode`, line-style order spelling and "fourteen
  entries", the `YAxis` refusal's words, the scatter3 and bubblechart3 alphas, and the region `Value`
  column, `FaceColorMode` and datetime/duration rows.
- Fixture `oi_get_legend_handles_sides`, 120 lines, all exact. `OpenItemsBatch15Tests` (18 cases).
  Tests that pinned the old answers now hold R2025b's: `MatlabBinScatterTests` and
  `MatlabHandleGraphicsVerbTests` (the `SetProhibited` sentence), and `MatlabHistogram2M122Tests` and
  `MatlabLivingGraphicsTests` (scripts that read a replaced plot now hold).

## Still open

Filed in the open-items list: 112 (handle counts: `pie`, `pie3`, `meshc`, `surfc`, `bar3`,
`compass`, `feather`, matrix `errorbar`, `fill` and `polarplot` answer one handle where R2025b
answers one per piece; `fplot` of a cell, `stairs` of a matrix and `plotmatrix`'s diagonal fail),
113 (contour's automatic levels), and 105 (R2025b's default colour order and ink).
