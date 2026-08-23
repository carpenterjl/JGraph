# 0078 — The furniture a chart is read and legended by

Date: 2026-08-23 · Milestone: M78 · Status: accepted

## Context

M73–M75 closed the axes and the figure; M77 closed the chart primitives. What was left was
everything in between: the rulers of a polar axes, the boxes that explain a chart, the label a script
writes on one, and the matrix charts M77 did not reach.

The numbers before this wave: polaraxes 87 of 107, legend 23 of 39, text 28 of 41, colorbar 19 of 42,
surface 36 of 60, contour 28 of 46, heatmap 20 of 39 — and beside them, sharing every seam, patch
37/56, quiver 35/52, image 21/27 and bubblelegend 26/37.

Reconnaissance found four shapes again, and the wave is organized around them rather than around the
kinds:

- **A block that already existed under another set of letters.** MATLAB gives a polar axes exactly
  the ruler block it gives a Cartesian one with R and Theta in place of X and Y. `AddRulerWave` had
  served X, Y and Z since M73, so seventeen of the polar names are that method called twice more.
- **Furniture with a model too thin to answer.** `LegendModel` had no width for its border, no
  orientation, no column count and no rectangle; `ColorbarModel` had a width, a label and a text
  style, and the strip it drew was hardwired to the right-hand edge with ticks generated on the spot.
- **Chart families needing model and renderer work**: a surface's mesh style, vertex markers and
  colour mapping; a contour's own ink, its level step and the matrix its curves are in; a heatmap
  that can be reordered, narrowed, relabelled and re-summarised.
- **Names with nothing under them**: `Layout` (a tiled-layout cell) and `Interactions`, the same two
  the axes wave left, plus `Toolbar`.

One thing the probe could not see was found the same way M77's three were — by running the forms
rather than counting the names. A legend's `Position` *answered*, and answered with the name of a
corner where MATLAB answers with a rectangle. The count was right and the property was wrong.

## Decisions taken before any code

1. **The polar rulers are served by the Cartesian block, not by a polar copy of it.** One method,
   called with two more letters. The consequence is that a mode means on a circle exactly what it
   means on a grid, and a later wave that fixes one fixes both.

2. **Rings and spokes get their own switches.** The polar renderer took a single `ShowMajor` for the
   whole grid, so `RGrid` and `ThetaGrid` could only have been the same flag twice. `GridModel`
   gained four fields and the renderer four branches; `grid on` still speaks for all of them through
   the aggregate.

3. **`Position` on a legend becomes a rectangle**, and `Location` keeps the word. This overrides a
   reflected model property with an alias — the first time this build has done so to *replace* an
   answer rather than to add one — because the reflected answer was the wrong shape.

4. **The colorbar becomes a real ruler on a strip that can stand anywhere.** Four sides, inside and
   outside, plus a pinned rectangle; explicit ticks, labels and limits; a direction, a tick
   direction, a tick length, a box and an ink. `MeasureReservedWidth` became `MeasureReserved`
   returning a `Thickness`, so the band comes out of whichever margin the strip stands in.

5. **The heatmap's table family is implemented, not declined.** M77 declined `SourceTable` and the
   `*Variable` block on scatter because no table-backed scatter exists. A heatmap is different:
   `heatmap(tbl, xvar, yvar)` has worked here since it was written, and the chart simply forgot what
   it had been built from. The table is kept on the handle entry — script-only state, like the
   `*DataSource` family M77 put there — so changing `ColorVariable` re-runs the summary rather than
   answering a question the script cannot act on.

6. **Normals are computed, never stored.** A surface's and a patch's `FaceNormals`/`VertexNormals`
   are worked out from the geometry each time they are asked for, so they agree with the lighting by
   construction. Writing one is refused by name: a normal this build did not compute is one its
   lighting would not use, and storing it would be the property answering something never drawn.

7. **The adjacent kinds are done in the same wave.** Patch, image, quiver and bubblelegend share
   almost the whole of the surface's and the legend's blocks. Serving them separately later would
   mean writing the same four accessors twice; serving them here cost four hook-ups and closed three
   more kinds outright.

## What each part is built on

| Part | Built on |
|---|---|
| R and θ rulers | `AddRulerWave`, unchanged, called with two more letters |
| Rings and spokes | four new `GridModel` flags; the polar branch of `FigureRenderer` |
| Legend font and ink | a new `AddTextStyleBlock` over any object's `TextStyle` |
| Legend columns | one layout law: entries dealt down each column, columns laid left to right |
| Legend and colorbar `Position` | `AddFurniturePosition`, sharing the Y flip `AddAxesLayout` performs |
| Text turn | `IRenderContext.DrawPolygon` for the box, `DrawText`'s rotation for the run |
| Text `Extent` | `AnnotationObject.RenderedBounds` inverted through `AxesModel.LastLayout` |
| Colorbar ruler | `LinearTickGenerator`/`LogarithmicTickGenerator`, the ones the rulers use |
| Surface and patch markers | `AddMarkerBlock`, one method serving surface, patch and quiver |
| Surface and image colour mapping | the colormap's own `Stops`, indexed rather than sampled |
| Alpha mapping | the `AlphaResolver`/`AlphaLookup` pair M74 built for the axes |
| Heatmap rectangle | `AddAxesLayout`, run against the parent axes through `AddChartLayout` |
| Heatmap table summary | `CountedTable`'s reduction, split out as `Summarise` and re-runnable |
| Data sources | `AddDataSources` from M77, given four more channels and then six |

## Verification

**Properties answered — the kinds this wave aimed at:**

| kind | before | after | what is left |
|---|---:|---:|---|
| surface | 36/60 | **60/60** | — |
| contour | 28/46 | **46/46** | — |
| polaraxes | 87/107 | 104/107 | Interactions, Layout, Toolbar |
| legend | 23/39 | 38/39 | Layout |
| text | 28/41 | 40/41 | Interactions |
| colorbar | 19/42 | 41/42 | Layout |
| heatmap | 20/39 | 38/39 | Layout |

**The adjacent kinds, done in the same wave:**

| kind | before | after | what is left |
|---|---:|---:|---|
| patch | 37/56 | **56/56** | — |
| quiver | 35/52 | **52/52** | — |
| image | 21/27 | **27/27** | — |
| bubblelegend | 26/37 | 36/37 | Layout |

**Totals: 1,132 → 1,310 of 1,394.** Eleven kinds now answer every documented name.

Gate: 0 warnings in Release and Debug · 5,112 tests · 50 of 50 stress scripts, the new `stess_50.m`
proving each visual family by exported pixel counts and round-tripping every new property through a
save and a load.

## Divergences recorded

- **A polar axes reports `ThetaLimMode` as `'manual'`** where MATLAB reports `'auto'`. The angular
  ruler is pinned to a full turn when the axes is created, and that pin is what keeps a circle a
  circle; the mode reads the pin honestly rather than claiming an auto-scale that is not happening.

- **`Layout` is unanswered on legend, colorbar, heatmap and bubblelegend**, as it is on axes. It
  names a cell in a tiled layout, and this build has no tiled layout.

- **`Interactions` stays unanswered on polar axes and on text, and `Toolbar` on polar axes** — the
  same ceiling the axes wave took, for the same reason.

- **A text label's `Extent` is a measurement of a drawing.** A label that has never been drawn has no
  measured size and answers the empty box at its own anchor, where MATLAB measures the font without
  drawing. `stess_50` §11 pins both halves.

- **`Editing` answers `'off'` and refuses `'on'`.** MATLAB's in-place text cursor has no equivalent
  here; the plot browser is where a label is edited.

- **`FontUnits` answers `'points'` and refuses every other word,** and `Units` on figure furniture
  answers `'normalized'` and refuses every other word. Nothing here measures in pixels or inches, and
  a property that accepted the word without honouring it would report a size that is not drawn.

- **`FaceNormals` and `VertexNormals` refuse a write**, and their modes are always `'auto'`: they are
  worked out from the geometry each time, so a given one is a normal the lighting would not use.

- **`LabelSpacing` decides which curves carry a label rather than repeating one along a curve.** A
  curve shorter than the spacing carries none, so a wider spacing labels fewer curves; MATLAB places
  a label every `LabelSpacing` points along each one.

- **A patch's `FaceVertexAlphaData` is averaged per face** rather than interpolated across it. A
  polygon filled in one colour is as far as a single fill can honestly carry a per-vertex alpha.

- **`LineColor 'none'` on an unfilled contour is refused** rather than drawing nothing. A contour
  with neither lines nor bands is an object that draws nothing at all, which is more likely a mistake
  than an intention.

- **`image` and `imagesc` differ only in their colour mapping.** MATLAB's `image` reads its numbers as
  colour numbers and `imagesc` stretches them; before M78 both stretched. The model's default is
  scaled — every other verb that builds one means scaled — and the `image` verb sets direct for
  itself, so a figure saved before M78 and loaded after it still scales.

- **A heatmap narrowed by `XDisplayData` has dropped the categories it no longer shows**, so its
  `ColorDisplayData` and its `ColorData` are the same grid. MATLAB keeps the unshown categories and
  the two differ.

- **`EdgeLighting` and `BackFaceLighting` are stored and read but do not yet change the drawing.**
  They are the two names in this wave whose behaviour is not under them, recorded here rather than
  left to be discovered.

## What is not done

- `Layout`, `Interactions` and `Toolbar`, per the divergences above.
- **Pie stays at 25 of 56.** Its missing names are the vertex-list family — `Faces`, `Vertices`,
  `XData`, the normals — which describe a patch, and JGraph models a whole pie as one object rather
  than as one patch per slice. Closing it means either changing that model or answering a vertex list
  the chart does not have; neither belongs in a property wave.
- Lit edges and lit back faces, per the last divergence above.
