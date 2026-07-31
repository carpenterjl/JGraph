# ADR 0048 — The 3-D command surface: parametric surfaces, lighting control, camera, primitives, variants

## Status

Accepted (M45, 2026-07-31). Builds on [ADR 0047](0047-surface-rendering-quality-and-performance.md)
(M44's batched triangles, cached contours, parula and lighting model) and the 3-D pipeline of
[ADR 0022](0022-3d-plotting-over-the-2d-pipeline.md). **Supersedes [ADR 0046](0046-data-types-and-graphics-verbs.md)
§6**, whose meshgrid collapse only worked because every grid was rectilinear.

## Context

M44 built a lighting model, a material model, a colormap family and a light collection on the axes,
and then had almost no way for a script to reach any of it. `camlight` and `material` existed as
model concepts with no verb; `caxis` did not exist at all; the sixteen colormaps could be selected
by name but not *returned*, so `colormap(parula(64))` — the form half of MATLAB's colormap examples
use — was a syntax error.

Underneath that was a measurement problem, and it is the reason this gap survived forty-four
milestones. `docs/matlab-builtin-coverage.md` tracked the 515 commands MATLAB documents as kind
**builtin**. Almost the entire plotting surface is documented as kind **function**: `surf`, `mesh`,
`contour`, `view`, `colormap`, `caxis`, `material`, `campos`, `daspect`, `sphere`, `quiver3`,
`trisurf` and the colormap generators all live under `toolbox/matlab/graph3d` and friends. A command
JGraph had never written looked exactly like a command it had — absent from a table that did not
cover it. Nothing was hiding; the instrument simply did not point that way.

And the checklist tool had a smaller version of the same fault. `catalog_names` matched a name
literal directly inside `Add(`, so the sixteen colormap generators — registered from a loop over a
table — were invisible to it, and it reported `parula` as unimplemented for as long as `parula`
worked.

## Decisions

### 1. `SurfacePlot` stores full X/Y matrices, and the vector form stays the fast path

ADR 0046 §6 accepted `surf(X, Y, Z)` with meshgrid matrices by **collapsing X and Y back to their
generating vectors** — the first row and the first column. That is exact for anything `meshgrid`
produced and wrong for everything else, and `sphere`, `cylinder`, `ellipsoid` and `ribbon` are
everything else: their grids are genuinely two-dimensional in X and Y, and a sphere collapsed to
generating vectors is a flat sheet.

`SurfacePlot` now carries `double[,]? _xGrid`/`_yGrid` alongside `double[] _x`/`_y`, with
`IsParametric` reading which is in use and a private `Xat(r, c)` hiding the difference from every
consumer. The collapse still happens — but only when the matrices really are a meshgrid, which is
the common case and the one worth the cheaper storage and the analytic sweep.

**This is the second reason M44 kept the depth sort alive.** `GridIsMonotone()` returns false
whenever `_xGrid is not null`, so a parametric surface takes the comparative depth sort rather than
the analytic sweep. The sweep is a theorem about height fields — occlusion depends only on the
ground footprint — and a sphere is not a height field. ADR 0047 predicted this precisely; it cost
one clause.

### 2. Color and lighting control reach the M44 model, and the generators return tables

`caxis`/`clim` on the axes color limits, `material` on the five surface coefficients, `light` and
`lightangle` and `camlight` creating `LightModel`s, `colororder` on a per-axes series palette,
`brighten` on the current map, and the sixteen generators returning an *m*-by-3 table so
`colormap(parula(64))` and `colormap(map)` both work on M44's `Colormap.Resample` and
`Colormap.FromRows`.

`surfl` is `surf` plus a light placed 45° round from the view, which composes with `material` and is
the reading a script wants. It is a **recorded divergence**: MATLAB's `surfl` maps the *reflectance*
through the colormap rather than the height, so its picture is grey-scaled lighting where this one
is a lit colormapped surface.

### 3. The camera verbs map onto an orthographic projection, and say so where they cannot

`Projection3D` is orthographic with an automatic fit (ADR 0022). `view`, `camorbit` and `camzoom`
land exactly. The rest land approximately and are documented rather than faked:

- **`campos` reads only the direction** from the box centre — an orthographic camera has no distance.
- **`camtarget` is always the centre of the data box; `camup` is always +z.** Both are accepted, both
  report the fixed answer, neither can be set to something else.
- **`camva` is applied as a zoom** about the default framing, so it always reports 6.6086° back —
  MATLAB's default view angle, which is the only angle an orthographic fit can be said to have.
- **`axis vis3d` is an accepted no-op**, because nothing here rescales during a rotate; the thing it
  exists to freeze never moved.

`pbaspect` is a real `Vector3D` on the axes, and `daspect` is that aspect divided through by the
data spans — which is why `daspect([1 1 1])` gives a cube only when the spans are equal, exactly as
in MATLAB.

### 4. Four new plot objects, which is what "the most useful thing left" actually cost

The coverage doc had called `fill`/`fill3`/`patch`/`plot3`/`line`/`text` the most useful thing left
and correctly noted that each is a figure-model slice rather than a builtin. Built here:
`Line3DPlot`, `Scatter3DPlot`, `PatchPlot`, `QuiverPlot`, plus a 3-D anchor on the existing
`TextAnnotation`. Each is a plot object, an `IDrawable`/`I3DDrawable` branch, a `.graph` DTO with a
mapper arm, and inspector support through the existing reflection.

`.graph` **stays at version 5** — every addition is a new derived DTO or a new property with a
default that reproduces the old behavior, so an M44 file loads unchanged and an M45 file with no new
objects in it is byte-comparable.

Two design calls inside that:

- **`PatchPlot.FaceVisible`** exists because `FaceColor = null` already means "use the series color",
  so there was no way to say "no fill". With the fill off and `CData` present, the colormapped color
  **moves onto the stroke** — which is the entire difference between `trisurf` and `trimesh`.
  Without it `trimesh` would have been a silently identical `trisurf`, which is the kind of feature
  that ships and stays broken.
- **A 3-element color argument in [0, 1] is an RGB triplet**, not per-vertex data. That is MATLAB's
  reading and what nearly every call means; the cost is that per-vertex data on a three-vertex patch
  has to go through `'CData'` explicitly.

### 5. The surface variants are geometry, not rendering

Nine of the twelve M45.E verbs needed no new drawing at all:

- **`meshz`** rings the grid with one extra ring of vertices repeating the border *positions* at the
  base height, so the curtain is vertical walls inside the same mesh — MATLAB's own trick. The
  duplicated coordinates make the grid non-monotone, which correctly routes it to the depth sort.
- **`waterfall`** is a `PatchPlot` with one closed polygon per row: the curve, plus a drop to the
  base at each end. The fill is what hides the rows behind, and M44's face depth sort orders them.
  Modelling it as a new `SurfacePlot` edge style was considered and rejected — the skirt is per-row
  and independent, not a padded mesh.
- **`ribbon`** is one two-column parametric surface per column, which is the case decision 1 exists
  for. **`trisurf`/`trimesh`** are a `PatchPlot` over a one-based triangle table.
  **`sphere`/`cylinder`/`ellipsoid`** are a new `JGraph.Math/Geometry/ShapeGrids`.
- **`contour3`** re-signatures `ContourPlot.DrawLines` from a coordinate mapper to a
  `Func<double, double, double, Point2D> place` and passes each vertex its own level as the height.
  The traced geometry is shared with the 2-D path; the whole feature is one delegate.

Only `quiver`/`quiver3` needed a new object. Its one non-obvious decision: **arrowheads are built in
screen space, from the projected shaft.** A head sized in data units is stretched by the axis scales
and squashed by the projection, so an arrow pointing away from the viewer would end in a smear
instead of a point. Auto-scale reads a spacing off the data itself — the cell each point would own
if the points were spread evenly over their own bounding box — rather than assuming a `meshgrid`, so
a scattered field scales sensibly too.

### 6. `[x, y, z] = sphere` reaches the multi-output form

The shape generators set `AutoCallsBare` so a bare `sphere` draws, which is the demo form. But
`EvaluateForOutputs` consulted `CallMultiple` only for an actual `CallExpr`, so the bare form
evaluated the name through the zero-argument path — **drawing a sphere on the way** — and then
reported an output shortfall. Wrong answer twice over.

Fixed generally rather than by dropping `AutoCallsBare`: a bare name that auto-calls is treated as a
zero-argument call whenever more than one output is asked for. That is the correct reading for every
such builtin, not just these three.

### 7. The coverage doc tracks graphics functions, and the tool reads loops

`docs/matlab-builtin-coverage.md` gains a **graphics-functions section** covering the 263 documented
commands under `graph2d`, `graph3d`, `specgraph`, `graphics`, `plottools` and `scribe` — 78
implemented, and the remaining 185 partitioned exactly (33 chart types with no plot object, 16
function plotters, 23 volume-visualization verbs, 43 handle-graphics and figure-tooling entries, 70
property/ruler/legacy-appearance commands). The partition is checked, not asserted: the buckets sum
to 185 with no leftovers and no strays.

`build-checklist.py` now reads both registration shapes — the `Add("name", …)` literal, including
when the name wraps to the next line, and the colormap-generator table. The callable-kind count is
**596 of 2,027**, of which 36 are M45's work and 15 were always implemented and never counted.

## Consequences

**372 of 514 documented builtins** (up from 364), the eight being `plot3`, `line`, `text`, `fill`,
`fill3`, `patch`, `surface` and `light` — precisely the entries the coverage doc had flagged as the
most useful thing left. **78 of 263 documented graphics functions**, 28 of them new here — 77 by a
strict reading, since `slice` is counted for a name JGraph registers for something else.

`.graph` stays v5, and **2019 tests pass** at 0 build warnings. A new `stess_21.m` exercises the
whole surface in sixteen self-checking sections — the generators' table shapes, the unit sphere's
radius, the cylinder's profile radii, the ellipsoid's centre and semi-axes, `surfnorm`'s unit
lengths, and every camera verb's read-back — and all twenty-one stress scripts exit 0.

**Recorded divergences**, each a consequence of a decision above rather than an oversight: no depth
buffer, so 3-D plots in one axes interleave by draw order and a `plot3` line does not interpenetrate
a surface; `campos` ignores distance and `camtarget`/`camup` are fixed; `camva` reads as a zoom;
`axis vis3d` does nothing; `surfl` colors by height; `surfnorm` has no plotting form; `camlight`
follows the camera where MATLAB's does not; `surface` switches the axes into 3-D; `meshc` on a
parametric grid records the flag and draws nothing; `ribbon` colors by height where MATLAB colors by
column; `waterfall` fills through the colormap and drops a row containing a non-finite height; a
filled contour in a 3-D axes draws iso-lines rather than bands. All are listed in
`docs/matlab-builtin-coverage.md`.

**`slice` is excluded permanently.** It has been the JGS array builtin since M18, and MATLAB's
volume `slice` would have to shadow it. Adding a verb JGraph cannot draw — there is no 3-D field
value type — by breaking a working one is a straight loss, so it is recorded as excluded rather than
pending.

**`gradient` is not implemented**, found while writing M45's smoke script. It is a numeric
`kind: function` command rather than a graphics one, so it belongs to a later coverage batch; it is
written down because nothing else in the process would have caught it.

## Alternatives considered

- **Keeping the meshgrid collapse and special-casing the generators.** Every parametric verb would
  have needed its own drawing path, and `ribbon` — twelve two-column strips — would have been twelve
  special cases. Widening the storage once cost `Xat(r, c)` and left the rest of the class alone.
- **A `SurfaceEdges { Both, Rows, Columns }` property for `waterfall`.** Worked out and rejected:
  MATLAB's skirt is per-row and closed, so a mesh with its column edges suppressed is not the same
  picture, and it would have added a serialized property to `SurfacePlot` for one verb.
- **Dropping `AutoCallsBare` from the shape generators** to fix `[x, y, z] = sphere`. That would have
  broken the bare `sphere` demo form to fix the destructuring form, trading one wrong answer for
  another. The interpreter fix serves both.
- **Implementing MATLAB's volume `slice` under another name.** Rejected: a MATLAB script that calls
  `slice` would still fail, and a JGraph script would gain a verb under a name no documentation
  uses.
