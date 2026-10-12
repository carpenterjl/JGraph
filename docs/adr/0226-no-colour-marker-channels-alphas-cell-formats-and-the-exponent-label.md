# ADR 0226 — No colour, a marker chart's own channels, transparencies as written, a heatmap's printf, and the ×10ⁿ as an object: open items 100 to 104

## Status

Accepted, 2026-10-11. The thirteenth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), the items batch 12 filed (ADR 0225). Every answer was recorded from R2025b (probes
`probe_100` to `probe_104`, `probe_co` and `probe_cdm` in the open-items scratch, then the fixture
`oi_none_scatter_alpha_numel`). As in batch 12, each item turned out wider than filed.

## Context

- **Item 100.** Filed as "a line's `Color` `'none'` reads back empty". It was refused outright:
  `line(..., 'Color', 'none')` failed with "unknown color 'none'", and so did `plot`, `plot3`, `stem`,
  `stairs`, `errorbar`, `xline`, `fplot`, `animatedline`, `text`'s `Color`, a line's
  `MarkerEdgeColor`, a scatter's `MarkerEdgeColor`, an axes' `Color` and `XColor`, and `contour`'s
  `LineColor` on an unfilled contour (refused in JGraph's own words). R2025b takes `'none'`, and its
  starts `'n'` and `'no'`, everywhere, and also `[r g b a]`, which it draws at that alpha and reads
  back as `[r g b]`. A line's unset `MarkerEdgeColor` read back the colour drawn; R2025b's is `'auto'`.
- **Item 101.** A scatter's `CData` read `zeros(1,0)` unless it held a value per point; R2025b's
  is the colour the cloud is drawn in, the series colour until one is given (`CDataMode`). Its
  `MarkerEdgeColor` and `MarkerFaceColor` read back colours where R2025b answers `'flat'` and `'none'`
  (`'none'` and `'flat'` once filled). Worse, the renderer filled every marker — a plain `scatter`
  drew solid dots where R2025b draws open circles — although the model's own doc said a null fill was
  an open marker. `scatter(x, y, [], c)` was refused, `scatter3(x, y, z, 'g')` was refused,
  `scatter3` lacked `CDataMode` and the two marker colours, and its `Marker` read `'circle'`.
- **Item 102.** A scatter's and a bubble chart's `MarkerFaceAlpha` and `MarkerEdgeAlpha` lived in a
  colour's alpha byte, so 0.25 read back 0.25098; `'flat'` was not taken. `patch`'s and `fill`'s
  `FaceAlpha` given at creation set the object's overall opacity, so the patch drew translucent but
  read back 1; `EdgeAlpha` was refused by `patch`, `bar` and `area`, and `alpha(h, value)` was refused.
- **Item 103.** A heatmap's `CellLabelFormat` still went through the .NET translation batch 12 retired
  for rulers: it read back in .NET spelling (`'0.00'`, default `'auto'`), wrote `'%g%%'` a hundred
  times too large, wrote the default as .NET's `G4` (`1.235E+06`), and refused in JGraph's words. A
  missing cell showed `MissingDataLabel`; R2025b leaves it blank (probe_103 read R2025b's drawn labels
  out of its heatmap's hidden label grid).
- **Item 104.** The legacy `numel(A, idx1, …)`, which `numel(s.f)` on a struct array spreads into, was
  refused with `MATLAB:maxrhs`. A ruler's hidden `SecondaryLabel`, the text R2025b draws the ×10ⁿ with,
  was missing. The probe also found that R2025b draws no ×10ⁿ beside a polar r ruler: it writes the
  power into each label (`1\times10^{4}`), and that ruler's `SecondaryLabel` is empty — batch 12 had
  drawn a mark there.

## Decision

**Item 100.** `OptionColor` reads `'none'` and its starts as no colour, `Colors.Transparent`, which
draws nothing, and `ColorRow` reads that back as `'none'`. One change reaches every colour property
and option that goes through the two (118 call sites). `[r g b a]` is taken, its alpha drawn. A line's
`MarkerEdgeColor` reads `'auto'` while it follows the line in the MATLAB dialect and takes `'auto'`
back; `contour`'s `LineColor` takes `'none'` filled or not.

**Item 101.** The scatter model gains R2025b's channels: `DataColor` (`CData` as one colour, null for
the series colour), `FillFollowsData` (R2025b's `'flat'` face), and `Color` (the edge) null for
`'flat'`. A null `Fill` is an open marker, as the model's doc always said, so a plain scatter now draws
open circles; `'filled'` fills in the data colour with no edge, as R2025b's does; a value per point
colours the edges, and the faces too once filled. The verbs write `CData` where R2025b's do (a spec
letter, the colour argument, `'CData'`), `[]` is the default size, a colour name given to the `CData`
property or option is refused with `MATLAB:scatter:InvalidCData`, and `CDataMode` reads, freezes and
releases the colour. `scatter3` takes a colour word after the coordinates, reads `CData`, `CDataMode`
and the two marker colours in R2025b's words, and its `Marker` in R2025b's letters. A bubble chart is
`'flat'` both ways at alpha 0.6, from a script or from the C# API.

**Item 102.** `FaceAlpha` and `EdgeAlpha` on the scatter model are doubles kept as written, NaN for
`'flat'`; the face and edge colours no longer carry them. `patch` and `fill` write their own
`FaceAlpha`; `patch`, `bar` and `area` take `EdgeAlpha`; `alpha(obj, value)` sets one object's
`FaceAlpha`, or a marker chart's `MarkerFaceAlpha`.

**Item 103.** `TickFormats.Sprintf` writes a value as R2025b's `sprintf` does: `%e` as `1.5e+03`,
capital `E` for `%E` and `%G`, a `%d` or `%i` given a fraction as `%e`, and `\n`, `\t`, `\r` as
characters. A heatmap keeps the printf format as written, `'%0.4g'` by default, writes its cells
through `Sprintf` (a trailing newline dropped), and leaves a missing cell blank; a .NET format saved
before this is still honoured. The property and the verb's option refuse as R2025b does, through one
`PrintfRefusal` shared with the ruler's `TickLabelFormat`; `TickFormats.Check` now calls a non-C
conversion (`%q`) invalid rather than of the wrong type, and a `*` width `DynamicNumber`.

**Item 104.**

- `numel(A, idx…)` counts what `A(idx…)` would hold, as R2025b does without checking the subscripts:
  a number or a char row by its elements, a logical by its trues, a `':'` by the extent it spans — the
  last one folding every dimension after it — and refuses a string or a cell with
  `MATLAB:numel:BadSubscriptingIndex`.
- `AxisModel` gains `ExponentLabel` (null for the automatic `\times10^{n}`) and `ShowExponentLabel`,
  both saved, and `ExponentMark`, which every ×10ⁿ the renderer draws now goes through. A ruler's
  hidden `SecondaryLabel` is a text handle onto them (`JgsTextLabel`'s `SecondaryLabel` role): its
  `String` is the mark, empty at 10⁰, on a log ruler and on a polar r ruler; a `String` written to it
  is kept through later powers (`StringMode` manual) until `'auto'`; `Visible` hides the mark. It is
  listed by neither `properties` nor `get`, as R2025b's is not.
- A polar r ruler writes the power into each label, and no mark (`ExponentTickGenerator`'s
  `eachLabel`).

## Divergences

- **R2025b's default colour order is a new palette** (`[0.066 0.443 0.745; 0.866 0.329 0; …]`, measured
  in probe_co) where this build keeps the classic one, and a histogram's default `FaceAlpha` is 0.6
  there and 1 here. Both change how every figure looks; open item 105. The fixture reads an automatic
  scatter colour as a word for that reason.
- ~~**Under `yyaxis`, `ax.YAxis` is one ruler here**, the active side; R2025b's is both. Open item 106.~~ Lifted by ADR 0227.
- ~~**A scatter's `CData` as an m-by-3 colour per point is refused**, and a column of values reads back
  as a row. A patch's `FaceAlpha` does not take `'flat'`; `scatter3` and `bubblechart3` keep no alphas
  and a `scatter3`'s `MarkerEdgeColor` option paints the whole cloud. Open item 107.~~ Lifted by ADR 0227.
- ~~**`xregion` and `yregion`** are not built (open item 108); `polyshape` is ADR 0134's.~~ Lifted by ADR 0227.
- **A `SecondaryLabel` answers an ordinary label's** alignment, `Position`, font and
  `HandleVisibility`; a style written to it is not kept (it is drawn in the tick labels' font);
  assigning a text object to the property is refused where R2025b takes it; and a datetime ruler's
  (R2025b's shows the year) is empty.
- ~~**Out-of-range alphas and `scatter(x, y, 20, 'q')`** are refused in JGraph's words.~~ Lifted by ADR 0227.
- **A heatmap's `'%,.2f'`** is taken and written with grouping; R2025b takes it but its drawn labels
  could not be read (its label grid kept the previous strings).
- **`[0 0 0 0]`**, black at no opacity, reads back `'none'`.
- **The JGS dialect draws scatters open and heatmap cells in printf**, and reads the new words,
  because the model does not know which dialect drew it.

## Consequences

- Silent wrong answers fixed: every plain scatter drawn filled; a patch's `FaceAlpha` read back 1;
  `'%g%%'` cell labels a hundred times too large; `CData` empty for a one-colour scatter.
- Lifted: ADR 0225's "a ruler's hidden `SecondaryLabel` is not answered" and "`numel(A, idx...)` is
  refused", and its polar r mark.
- Fixture `oi_none_scatter_alpha_numel`, 147 lines, all exact. `OpenItemsBatch13Tests` (39 cases,
  R2025b's drawn heatmap labels among them). Tests that pinned the old answers now hold R2025b's:
  `HeatmapLayoutTests`, `BubbleChartLayoutTests`, `JgsPrimitive3DTests`, `MatlabBubbleChartTests`,
  `MatlabPolarPlotTests`, `MatlabM77PlotPropertyTests` (`CDataMode` `'manual'` is R2025b's to take),
  `MatlabM79TablePiePropertyTests` and `MatlabMeshPlotTests`.

## Still open

Filed in the open-items list: 105 (R2025b's default colour order and histogram alpha), 106 (`ax.YAxis`
under `yyaxis`), 107 (the marker-chart remainders above), 108 (`xregion` and `yregion`).
