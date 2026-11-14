# ADR 0361 — A colormap reads back its rows at the figure's height, a surface reads R2025b's defaults, and a histogram's limits keep its width

## Status

Accepted, 2026-11-14. Open items 293, 314 and 271 (chips wave 4, agent V).

## Context

- **Item 293.** ADR 0343 built the generators. What it left: a generator named in `colormap('hot')`
  was a 256-row gradient whatever the figure held (R2025b's `colormap.m` calls `feval('hot')`, which
  is `hot(colormapheight)`, the current figure's map's length); `f.Colormap`, `ax.Colormap` and
  `get(gca, 'Colormap')` answered the map's drawing stops at eight bits (33 rows for parula); and a
  bare `prism` answered its table. Probes `cm1` and `cm2` found more: an unaddressed `colormap(map)`
  lands on the figure in R2025b (`set(gcf, 'Colormap', ...)`), here on the current axes, so
  `f.Colormap` read the default after `colormap(jet(5))`; setting a figure's map, by
  `colormap(f, ...)`, `f.Colormap = ...`, `set(f, ...)` or an unaddressed `colormap`, takes every
  axes in it back from a map of its own (`ColormapMode` 'auto' after, the axes reading the figure's
  rows), which contradicts the documented rule and a test here that asserted it; `c =
  colormap('gray')` answers the map; `colormap('default')` and `colormap('hot(3)')` are read by
  colormap.m; and `ax.ColormapMode` did not exist. `prism.m` with `nargin + nargout == 0` sets the
  `Color` of each line among `get(gca, 'Child')` to its row of prism; `flag.m` has no such branch.
- **Item 314.** Probe `sf1` (374 lines) read a plain `surface`, `surf`, `mesh`, `pcolor` and colour
  patches. R2025b's `surface` and `surf` edge is 33/255 grey half a point wide (here 'flat' and
  0.75); counted positions run 1 to n across and `(1:m)'` down (here from 0, in a row); a meshgrid
  pair reads back as its matrices (here the vectors it was collapsed to); `surface(...)` reads
  `XDataMode`, `YDataMode` and `CDataMode` 'manual' where `surf` reads 'auto' for counted positions
  and colours; a `mesh` lights its edges and not its faces (`FaceLighting` 'none', `EdgeLighting`
  'flat'). A patch's unset `CData` and `FaceVertexCData` are 33/255 (here this build's 0.251 ink,
  ADR 0232's divergence), and a patch coloured by values reads `CDataMode` 'manual'. A surface in a
  flat axes beside a log ruler was not drawn at all (ADR 0349's `FlatProjection` had no camera for
  it).
- **Item 271.** I10 (ADR 0331) made `histogram` keep the bins a count, a width or edges name. Under
  `'BinLimits'` the automatic rules still chose a count by rounding the span over a nice width (30
  bins where R2025b has 35 over the probe's readings, 151 for 158 under `'sqrt'`), the integer rule
  was not applied under limits at all, a width under limits closed past the high limit (2.5 for 2),
  and `histcounts2` had its own approximation. Setting `BinLimits` afterwards cut the bins again by
  the automatic rule. R2025b's `histcounts.m`, `binpickerbl.m` and `integerrule.m` are readable;
  the histogram class is compiled, so its setter was measured (probes `hp1`, `hp2`, 113 lines).

## Decision

### A colormap reads back the rows that were set, at the figure's height (`MatlabNamedColormap`, `SetFigureColormap`)

The figure's and the axes' `Colormap` read through `JgsBuiltins.ColormapRowsOf` (made internal), as
`colormap` with no map already did since ADR 0343: the rows a table was made from, R2025b's 256 rows
of a named map, or else the map's stops (ADR 0343 sampled those at 256, which turned a `hot(16)`
reopened from a saved figure, rebuilt from its stops, into 256 rows; `oi_fig_struct_form` caught it).
In the MATLAB dialect, read at the call:
- `colormap(map)` with no target sets the current figure, as colormap.m's `gcf` does; JGS keeps the
  current axes.
- A name is read as colormap.m reads it: `'default'` is the figure's default map, `'name(k)'` is the
  generator at `k` rows, and a bare name is the generator at the current figure's map's height
  (`ColormapHeight`, now the figure's map alone in the MATLAB dialect). At 256 rows the named map
  object itself is kept, so a named map draws exactly as before; at any other height it is
  `Colormap.FromRows(name, rows)`.
- Setting a figure's map (`SetFigureColormap`, from `colormap(f, ...)`, an unaddressed `colormap`,
  `f.Colormap` and `set(f, 'Colormap', ...)`) lets every axes' own map go in the MATLAB dialect. JGS
  keeps an axes' own map.
- `colormap` takes its output count (`TakesOutputCount`): `c = colormap(...)` answers the map the
  target holds after the set, and a statement that sets prints nothing.
- `ax.ColormapMode` reads 'manual' while the axes holds a map of its own; writing 'auto' lets it go.
- `prism` takes its output count too: with no size and no output, in the MATLAB dialect, each line
  among the current axes' children (newest first) is given its row of prism, as prism.m does.

### A surface reads R2025b's defaults (`AsR2025bMade`), and a log ruler is read in decades

In the MATLAB dialect `surf`, `surface` and `mesh` (the names probed) give what `Surface3D` makes
R2025b's readings: counted positions 1 to n and `(1:m)'` (`YColumn`); a meshgrid pair flagged to read
back as its matrices (`SurfacePlot.XReadsAsGrid`, `YReadsAsGrid`; it is still drawn over its two
vectors, the rectilinear fast path, and a vector written to `XData` or `YData` clears the flag);
`EdgeWidth` 0.5; a surf's and a surface's edge the explicit 33/255 grey pcolor already used (it was
drawn #303030 at 80 per cent); a mesh's `FaceLighting` 'none' and `EdgeLighting` 'flat'; and
`surface(...)` holding its positions and colours as given (`XImplied` false, `CData` the heights).
Options written in the call apply after these, so `surface(Z, 'EdgeColor', 'flat')` reads 'flat'.
JGS's surfaces, and `surfc`, `meshc`, `surfl` and the rest, are what they were.

A patch's unset `CData` and `FaceVertexCData` read R2025b's factory 33/255 grey whatever its face is
drawn in; `CDataMode` reads 'manual' when the patch is coloured by values (`ColorData`), and writing
either word leaves the colours as they are.

`Projection3D.WithLogarithmicRulers` is the same camera with x or y read in decades: the coordinate
is `log10`'d before it is placed (in `Project`, `Normalize`, and back in `Denormalize`) and the
ruler's range is its decades. `FigureRenderer.FlatProjection` uses it for a log ruler whose range is
positive, so a surface beside one lands where that ruler's ticks are; a log ruler reaching zero still
has no camera.

### The rules under limits are histcounts.m's (`Binning.EdgesWithinLimits`)

`Binning.EdgesFor`'s limits branch is a transcription of histcounts.m's `BinLimits` branch:
- a count is `minx + (0:n-1).*((maxx-minx)/n)` closed on `maxx` (histcounts2: `linspace`);
- a width is `minx:binWidth:maxx` with `maxx` appended when the colon stops short or takes one step,
  the width held to at least the span over 65,536 (histcounts2: 1,024) (`LimitedByWidth`, through the
  project's own colon arithmetic);
- a rule reads only the readings inside the limits: `auto` is the integer rule when they are all whole
  and the span is at most 50, else Scott; Scott and Freedman–Diaconis (with fdrule's guard, at least a
  tenth of the range, and a width of one for a single reading) go through `binpickerbl`, which fits
  `ceil(span / width)` bins by `linspace`, or one bin when the readings inside are none or constant;
  Sturges and the square root are their count by `linspace`; the integer rule is the limits with the
  half-integers inside them (`IntegerEdgesWithinLimits`).
- Equal limits are edges on the one value (`[2 2 2 2]` for three bins), as R2025b answers; this used
  to widen them by a half each way.

Without limits, the integer rule is integerrule.m's (unit bins, or a power of ten when the readings
span more than the most bins allowed, the ends rounded half away from zero onto the width), and
Freedman–Diaconis has the same guard. The counted width is binpicker's `p10*ceil(ll/p10)`
transcribed with R2025b's own `10^e` (`Binning.MatlabPow10`: the correctly rounded decimal except at
12 exponents between −40 and 40, −5 among them, probe `pw2`; probe `bp1` read the same 10^-5 inside
binpicker). ADR 0158's decimal spelling (`units / 10^-e`) missed twice: `histogram(x, 7)`'s fourth
edge was -0.8000000000000007 where R2025b's is -0.7999999999999998, and 256 bins over
[-1.25, 1.31512] are 1003 hundred-thousandths wide in R2025b (`ceil(ll / p10)` over the ulp-low
power is 1003), 1002 here. `LoopBuiltinsM158Tests.ARequestedBinCountsWidthIsWrittenAsItsDecimal`
asserted the 1002 and now asserts `bp1`'s edges.

`histcounts2` sends every limits case to the same branch with the fourth root, and a rule reads the
pairs inside both limits when both are named (histcounts2.m's `inrange`).

### A histogram's limits set afterwards keep its width (`HistogramPlot.BinLimits`)

Measured, not read: setting `BinLimits` on a histogram with data steps its bin width from the low
limit by the colon and closes the last bin on the high one, whatever chose the bins, and reads
`BinMethod` 'manual'. The width is the one kept by the last such set, else the first bin's width
when the bins are evenly spread (`EdgesEvenlySpread`, to a millionth of a millionth of a bin or 16
ulps of the largest edge); bins that are not evenly spread keep their count, stepped at the span
over the count. `BinWidth` reads that kept width, else the first bin's width, else (MATLAB dialect)
'nonuniform' (`BinWidthNonuniform`). `BinLimitsMode` 'auto' (`ReleaseBinLimits`) lays the bins over
all the readings again at the width they had (the span over the count for uneven bins), method
'manual'; 'manual' (`FreezeBinLimits`) takes the span showing. A histogram made with named edges
reads `BinLimitsMode` 'manual', as R2025b's does. A document reopened with its edges keeps them
(`PlotMapper.ToHistogram` writes the rule and the limits down with `NameBinning` rather than
setting them).

## Consequences

- New parity fixtures `oi_histogram_bin_limits` (123 rows), `oi_colormap_reads` (73 rows) and
  `oi_surface_patch_defaults` (201 rows), from R2025b 25.2, every row agreeing.
- `oi_patch_rows_axes_font_short_rulers`'s ten CData and FaceVertexCData rows agree (they were
  ADR 0232's divergence).
- `MatlabM75FigurePropertyTests.AnAxesThatChoseItsOwnColormapKeepsIt` asserted the documented rule
  and is now `...FollowsTheFiguresNextOne`, R2025b's answer.
- New tests in `ColormapSurfaceHistogramLeftoverTests`.
- Drawing changes, MATLAB dialect: an unaddressed `colormap` colours every axes in the figure that
  has no map of its own, as R2025b's does; a surf's and a surface's edges are 33/255 opaque and half
  a point wide; a surface beside a log ruler is drawn; `histogram` under `'BinLimits'` and after a
  `BinLimits` set has R2025b's bins; `histcounts(x, n)` and `histogram(x, n)` edges can move by an ulp
  where the width is a whole number of tenths.

## Divergences

None opened.

## Still open

- **A patch's `CDataMode` written 'auto'** reads 'auto' in R2025b while the patch keeps its values;
  here it keeps reading 'manual'.
- **`patch(x, y, 'b', 'FaceColor', 'flat')`** reads `FaceColor` 'flat' in R2025b and the blue here (the
  option seems to be applied before the colour argument).
- **A patch's and a fill's default edge** is 33/255 grey half a point wide in R2025b, black 0.75 here
  (probe `sf1`); changing it reaches the `.graph` document's defaults (`PatchPlot_KeepsItsDefaultBlackEdge`).
- **A surface's `MarkerEdgeColor`** reads 'auto' in R2025b and 'none' here; 'none' and 'auto' are one
  null in the model.
- **`histogram([], n)`** has edges `0:n` in R2025b (binpicker of nothing); here `[0 1]` cut in n, which
  `FigureBuildI10Tests` asserts as "what it was".
