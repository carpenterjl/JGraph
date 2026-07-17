# ADR 0007: Plot types, subplots, linked axes, and non-numeric scales

- Status: Accepted
- Date: 2026-07-16

## Context

Milestone 6 broadens JGraph from three plot types on a single linear/log axes to a set that covers
most everyday scientific plotting: statistical and discrete plots (stem, histogram, error bar),
raster fields (image/heatmap), multi-panel figures (subplots), synchronized navigation (linked
axes), and non-numeric axes (date/time and category). The constraint was to add all of this without
disturbing the load-bearing seams — the object model, the single `IRenderContext`, the tick/scale
strategy interfaces, and the one-way layering.

## Decision

1. **New plots are ordinary `PlotObject`/`IDrawable` implementations.** `StemPlot`, `ErrorBarPlot`
   (both `XYPlot`), `HistogramPlot`, and `ImagePlot` (both `PlotObject`) live in `JGraph.Objects`,
   implement `IDrawable` (and `ILegendItem` where a key makes sense), report their own data bounds
   (stems/error bars/histograms expand the vertical extent to include baselines and whiskers, so
   auto-scaling never clips them), and each gained an `AxesExtensions` fluent method plus a `JG`
   facade method. No renderer changes were needed — `FigureRenderer.DrawPlots` already dispatches to
   any `IDrawable`. `HistogramPlot` bins raw samples lazily and caches the result, supporting count,
   probability, density, and cumulative normalizations.

2. **A raster image is a genuine new primitive, so it is a new `IRenderContext` member.** Heatmaps
   need to blit a pixel field, which no combination of the existing vector primitives expresses
   efficiently. `IRenderContext.DrawImage(pixelsArgb, w, h, destRect, interpolate)` was added — the
   first new seam member since M1 — implemented once in `SkiaRenderContext` (via `SKImage` +
   `DrawImage`) and trivially in the test double. `ImagePlot` builds its tile once (mapping the
   scalar field through a `Colormap` and a color range), caches it, and lets the renderer scale it,
   so pan/zoom stays cheap. Vector exports embed the tile as a raster region, which is the standard
   representation for a heatmap in SVG/PDF. `Colormap` (perceptually uniform Viridis, plus Jet, Hot,
   Cool, Grayscale) is an engine-independent model type like the rest of `JGraph.Core.Drawing`.

3. **Subplots are just axes with normalized bounds.** An `AxesModel` already occupies a
   `NormalizedBounds` rectangle of the figure, so a subplot grid is pure geometry:
   `FigureModel.AddSubplot(rows, cols, index[, lastIndex])` computes a cell (MATLAB 1-based,
   row-major, with a gutter and optional spanning) and `JG.Subplot` selects or creates the cell.
   The renderer already lays out every axes independently, so multi-panel figures needed no
   rendering changes and each panel keeps its own axes, scales, grid, and legend.

4. **Linked axes live in the model and mirror ranges through the existing event.** `AxisLinkGroup`
   (`JGraph.Core.Model`) unifies the linked ranges to their union, turns off auto-scale (matching
   MATLAB `linkaxes`), and listens on each member's bubbling invalidation to mirror subsequent range
   changes — guarded by a re-entrancy flag and a per-axis last-range cache so no feedback loop can
   form. It touches only the model, so it composes with the UI-free interaction layer and works
   headlessly.

5. **Date/time and category are tick/format concerns, not new transforms.** Both map linearly to the
   axis, so the coordinate transform and the whole decimation/interaction pipeline are unchanged;
   only tick generation and labeling differ. Date/time values are OLE automation dates (a `double`
   day count, reversible to `DateTime`, sub-microsecond over the useful range — see `DateTimeAxis`).
   `DateTimeTickGenerator` picks the calendar/clock step (second → year) closest to the requested
   spacing, aligns ticks to natural boundaries, and formats by resolution. `CategoryTickGenerator`
   places one thinned tick per category label at integer positions. Because a category generator
   needs the axis's labels, tick-generator resolution gained an axis-aware overload,
   `TickGenerators.For(AxisModel)`, which the renderer now uses; the old scale-type overload remains.

## Consequences

- The plot-type roster now covers discrete, statistical, and field data, and figures can be tiled and
  their axes linked — all reusing the M1 seams, which validates them again.
- `DrawImage` is the one new obligation on future `IRenderContext` backends; everything else was
  additive within existing layers. A backend that cannot raster could fall back to per-cell
  rectangles, but every practical target can blit.
- Date/time and category axes are limited to a linear underlying scale (no log-time), which is the
  correct and expected behavior; richer calendar features (business-day skips, timezone display) can
  extend `DateTimeTickGenerator` without touching the model.
- Colormaps and the image tile are computed on the CPU; very large fields would benefit from a GPU
  path later, but that is a backend optimization behind the same `DrawImage` seam.
