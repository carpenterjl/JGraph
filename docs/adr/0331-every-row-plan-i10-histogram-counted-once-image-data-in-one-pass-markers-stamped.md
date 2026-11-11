# ADR 0331 — The every-row plan's I10: a histogram keeps its bins and counts once, an image's numbers are laid out in one pass, and a coloured scatter is stamped from masks

## Status

Accepted, 2026-11-10. Stage I10 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 10). **Done**: all three rows are under R2025b's median, headless, in the app and
at steady state (`d04_scatter_export` 0.064 s against 0.477, `d04_hist_build` 0.015 against
0.077, `d06_panel_build` 0.052 against 0.192). Every CHK line is byte-identical. A histogram
made with a count, a width or edges now has those bins, which it did not before. Landed together
with I9 (ADR 0330).

## Context

The GUI round (ADR 0322) left three figure rows to I10, 0.76 s of excess between them:
`d04_scatter_export` at 0.949 s against R2025b's 0.477, `d04_hist_build` at 0.295 against 0.077
and `d06_panel_build` at 0.264 against 0.192. As in I9 the timing run came once, at the end, so
each row was read down to what it does.

- **`histogram(x, 200)` chose its bins twice, and the second choice was wrong.** The builtin
  built the plot from the edges for 200 bins and then set `plot.BinMethod = rule`. That setter
  re-bins: it cut the readings again by the automatic rule and counted them again. So the row
  ranged and counted ten million readings twice (the range through a LINQ `Where` over an
  `IEnumerable<double>`, the count on one thread), and the histogram that came out had the
  automatic rule's bins, not the 200 asked for. The same happened to named edges and a named
  width: `histogram(x, [-4 -1 0 2 5])` drew 35 automatic bins. R2025b draws what was asked for
  and reads the method of such a histogram back as `'manual'` (probe `chk4.m`). No fixture had
  asked for `NumBins` after a count, so nothing had shown it.
- **A scatter coloured point by point was two Skia calls a marker.** With colour data the
  scatter cannot go out as one call: each of the 200,000 markers was a `DrawCircle` for the fill
  and another for the outline, the second in a fully transparent colour for a `'filled'` scatter
  (MATLAB's filled markers have no edge), each through its own P/Invoke and its own small
  anti-aliased rasterisation.
- **`imagesc` copied a 2048-square matrix three times, one of them through a stride.**
  `SetImageCData` copied the packed numbers to a managed array, filled the `double[,]` the plot
  holds element by element with a column-major read against a row-major write, and copied the
  numbers a third time for the value `CData` reads back. Four images a pass. The plot's colour
  range was a `foreach` over the two-dimensional array.

## Decision

### A histogram keeps the bins it was asked for, and counts once

The builtin ranges and counts with the kernels `histcounts` already had (ADR 0158): a named
count needs only the finite range, one threaded scan (`PackedMath.FiniteRange`), and the tally
is cut into blocks across threads and added in bin order (`CountBins`). The rule that picks the
edges (`Binning.CountedEdges`, `Binning.EdgesFor`) and the rule that picks a bin
(`Binning.BinFinder`) are the ones they were, so `h.Values` and `h.BinEdges` are `histcounts`'
own. The readings the plot keeps are copied grain by grain across threads into an array nobody
zeroes first.

The plot is then built from the readings, the edges and the counts together
(`HistogramPlot.Counted`) and the rule and the limits are written down beside them
(`HistogramPlot.NameBinning`) instead of being applied through the setters. `BinMethod` reads
`'manual'` when a count, a width or edges were named, as R2025b's does, and setting `NumBins`,
`BinWidth` or `BinEdges` afterwards makes it `'manual'` too. Setting `BinMethod` to a rule still
cuts the bins by that rule; setting it to `'manual'` leaves them where they are.

This is a correctness fix that the timing row was hiding: `histogram(x, n)`, `histogram(x,
edges)`, `histogram(x, 'BinWidth', w)`, `histogram(x, 'NumBins', n)` and `histogram(x, n,
'BinLimits', [a b])` now draw the bins they name.

### An image's numbers are laid out in one pass

`SetImageCData` lays a packed real matrix out into the plot's row-major grid with
`MatrixLayout.Transpose` (tiled, threaded) straight from where the numbers lie, and the copy
`CData` reads back is made grain by grain across threads into unzeroed storage. The plot's
automatic colour range (`ImagePlot.FiniteBounds`) scans bands of rows across threads from a
million values up; a least and a greatest do not depend on the order they are found in.

### A cloud of own-coloured markers is one call, and a bitmap stamps it

`IRenderContext.DrawMarkerCloud(points, colorsArgb, style)` draws one marker per point, each in
its own colour, in order. Its default body is the old behaviour exactly, one `DrawMarkers` call a
point, so the recording context of the tests, the SVG and PDF exporters and the screen see the
calls they always saw. `ScatterPlot.Render` gathers consecutive points of one size whose marker
is one colour (a fill with an edge of the same colour, a fill with no edge, or an edge alone)
into a run and hands the run over; a point that is not (a fill and an edge of different colours)
is drawn on its own after the run before it, so the order on the page is still the order of the
data.

`SkiaRenderContext` overrides the call when it has been handed the bitmap its canvas paints into
(the raster exporter passes it), the canvas matrix is a plain scale and shift, the clip is a
rectangle, every colour is opaque, the run has at least 256 markers and the marker is at most 64
device pixels across. It then asks Skia to draw the glyph once for each of 32 by 32 positions
within a pixel, in white onto a coverage-only bitmap (fill, then outline when there is one, as
the one-at-a-time road composes them), and writes each marker as its colour blended through the
mask nearest its own position, inside the device clip. The clip is tracked by the context itself
in whole device pixels as Skia's unsmoothed clip applies it, because the canvas reports its clip
rounded outwards. `JGRAPH_MARKER_SPRITES=0` turns the stamping off.

## Measured

`head2head_v3\measure_i9.ps1` and `compare_i9.py` (one run for I9 and I10, which share two
scripts), `d04_plot2d.m` and `d06_image.m` unchanged, one session per condition (`runs\i9-first`):
three headless sessions of this build and three of the build before it (f937a6fb) alternated, one
app session, and one eight-pass headless session of each build read over passes 4 to 8 (steady).
Medians of the timed passes, in seconds.

| Row | R2025b | before | I10 | I10 app | I10 steady | before, steady |
|---|---|---|---|---|---|---|
| `d04_scatter_export` | 0.477 | 0.886 | 0.064 | 0.067 | 0.057 | 0.865 |
| `d04_hist_build` | 0.077 | 0.284 | 0.015 | 0.014 | 0.015 | 0.288 |
| `d06_panel_build` | 0.192 | 0.209 | 0.052 | 0.051 | 0.052 | 0.210 |
| `d04_plot2d` wall | 4.149 | 1.605 | 0.533 | 0.695 | 0.463 | 1.575 |
| `d06_image` wall | 3.948 | 2.707 | 1.196 | 1.220 | 1.180 | 2.756 |

The split probe (`probes\instrumented\split_i9.m`, mean milliseconds a call over five calls
after a warming one, one session per engine, figures invisible):

| Piece | I10 | before | R2025b |
|---|---|---|---|
| `histcounts(x, 200)`, 10M | 9.3 | 8.2 | 13.2 |
| a figure, `histogram(x, 200)`, close | 15.5 | 308.1 | 130.4 |
| a figure, `imagesc` of 2048 by 2048, close | 12.2 | 56.4 | 105.3 |
| the same with `colorbar` | 14.2 | 61.3 | 142.0 |
| `exportgraphics` of the 200k coloured scatter, 150 dpi | 76.4 | 851.2 | 517.4 |
| `exportgraphics` of a ten-point figure, 150 dpi | 30.6 | 29.2 | 223.4 |

The histogram is `histcounts` plus one threaded copy of the readings. The scatter's export is
46 ms over what an almost empty figure of the same size costs to draw and encode. The probe's
checksum, which includes `NumBins`, agrees with R2025b's on this build (200) and not on the one
before (260).

### Checks

- All 8 CHK lines of `d04_plot2d` and `d06_image` are byte-identical in all nine sessions (the
  figure rows print none of their own; the scripts' data lines are the check that nothing else
  moved).
- The exported figures against the build before, pixel by pixel: `d04_lines.png`,
  `d04_panel.png`, `d06_pipeline.png` and `d06_pcolor.png` are identical; `d04_scatter.png`
  differs in 0.24 per cent of its pixels (2,310 of 957,250, nine of them by more than a quarter
  of full scale, where thousands of markers are stacked on the curve's edge); `d04_hist.png` is
  a different picture, 200 bars for 260.
- `FigureBuildI10Tests` (14 tests): R2025b's answers for a named count, edges, a count under
  limits, a width, `NumBins`, a named rule and the automatic one, with `BinMethod` read back,
  and for the same set afterwards; the few and the missing (an empty input, all NaN, infinities,
  the counts form); a three-million-reading histogram against `histcounts` on one thread and on
  many; `CData` and `caxis` for small, vector, `uint8`, logical, NaN-holding and
  million-element images, and that the plot's copy is its own; a stamped cloud against the
  one-at-a-time picture for filled circles, clipped ones, open circles, squares, plus signs and
  one colour with sizes (no pixel more than 72 levels away, under 5 per cent of pixels touched,
  ink within 2 per cent); and that a see-through cloud, a short one and one with a black edge
  are drawn exactly as before.
- `MatlabM77PlotPropertyTests.AHistogramTakesItsOptionsInTheCallAsWellAsAfterIt` expected three
  bins of `histogram([1 2 2 3 3 3], 'BinWidth', 1)`; R2025b has two (edges `[1 2 3]`), and the
  expectation is corrected.
- Gate: the CLI's Debug build; the 3,478 tests of the areas the two stages touch (the new
  classes, histograms, scatters, imaging, codecs, gradients, powers, the packed kernels, the loop
  and body compilers, the ODE solvers, export and rendering) and the 285 parity fixtures, each in
  packed and in boxed storage; the stress suite 88 of 89 (only `stess_40`, open item 254); the
  seven `verify-*.py`; the divergence harvest. The linear-algebra lanes were not run: nothing
  here reaches LAPACK.

## Consequences

- `histogram` with a named count, width, edges or `NumBins` draws those bins. Every figure such
  a call made before this stage had the automatic rule's bins instead; `d04_hist.png` has 200
  bars where it had 260.
- `h.BinMethod` is `'manual'` for those calls and after `NumBins`, `BinWidth` or `BinEdges` is
  set, where it read `'auto'`.
- `h.Values` and `h.BinEdges` are `histcounts`' answers for the same call, at any thread count.
- An exported PNG, JPEG, BMP or TIFF of a scatter coloured point by point is not pixel for pixel
  what it was. A stamped marker is the marker Skia draws at most a sixty-fourth of a pixel away;
  Skia's own anti-aliasing moves an edge in quarter-pixel steps on each of four sub-rows of a
  pixel, so a pixel on a marker's edge can come out up to a quarter of full ink different, in
  either direction. On the benchmark's scatter, where 200,000 markers are stacked along a curve,
  0.25 per cent of the pixels differ, nearly all by a few levels. SVG and PDF output and the
  screen are unchanged, and so is any scatter the stamping declines (a see-through colour, an
  edge of another colour, fewer than 256 markers, a rotated canvas).
- `imagesc`, `image` and a `CData` assignment hold the numbers they held; `caxis` answers the
  range it answered.
- One switch, `JGRAPH_MARKER_SPRITES`, for the one change here that alters pixels.

## Divergences

- **Setting `BinLimits` on a histogram whose bins were named keeps neither their width nor their
  count.** R2025b keeps the bin width of a histogram made with a count or a width when its
  limits are then set (`histogram(x, 200); h.BinLimits = [-1 2]` has 86 bins there) and rescales
  named edges onto the new limits; here the bins are cut again by the automatic rule (30 bins).
  Only the calls that name the bins when the histogram is made were put right in this stage.
- **The automatic and the square-root rule choose a different count under `BinLimits`.**
  `histogram(x, 'BinLimits', [-1 2])` over the probe's 30,000 readings has 21 bins in R2025b and
  30 here; with `'BinMethod', 'sqrt'` it is 101 there and 100 here. Both differences were there
  before this stage; `histcounts` shares them.

## Still open

- **`BinLimits` set after the fact, and the rules under limits** (open item 271): the two
  divergences above.
- **The image tile is still built on one thread** when a figure with an image is drawn
  (`ImagePlot.BuildTile`, a colormap lookup a pixel): `d06_panel_export` is 0.60 s against
  R2025b's 1.48, a row JGraph already wins, so it is headroom and has no item.
