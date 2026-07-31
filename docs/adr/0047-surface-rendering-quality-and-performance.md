# ADR 0047 — Surface rendering: batched triangles, cached contours, parula, and lighting

## Status

Accepted (M44, 2026-07-30). Corrects [ADR 0046](0046-data-types-and-graphics-verbs.md) §5, which
claimed surfaces were "always smoothly shaded and lit"; extends the 3-D pipeline of
[ADR 0022](0022-3d-plotting-over-the-2d-pipeline.md) and the performance work of
[ADR 0026](0026-packed-numeric-arrays-and-large-dataset-performance.md) into the rendering layer.

## Context

The user asked why MATLAB's surfaces look brighter and render faster than JGraph's. Three separate
findings came out of looking, and only one of them was the one being asked about.

**There was no lighting anywhere in the repo.** A grep for `shading|lighting|diffuse|specular|normal`
returned zero hits across Objects, Rendering, Math and Core. Every facet was flat-filled with the
colormap sampled at the mean of its four corner Z values, and `shading`/`lighting`/`camlight` parsed
their argument and discarded it. The catalog help strings and ADR 0046 §5 asserted otherwise.

**But lighting is not why MATLAB looks brighter.** MATLAB applies no lighting to a surface until a
light object exists; a default `surf` is flat colormap color there too. The actual cause was the
colormap: JGraph defaulted to Viridis, which starts at dark purple `#440154`, and **parula did not
exist in this repo at all**.

**Rendering was per-facet, and MATLAB's "trick" is that it is not.** `SurfacePlot.Render3D` allocated
four arrays per frame, sorted every cell by mean vertex depth, and issued one `DrawPolygon` per cell —
and `SkiaRenderContext.DrawPolygon` constructed and destroyed a native `SKPath` per call, so
`surf(peaks(200))` was ~40,000 path construct/destruct pairs per frame, doubled when edged. Filled
contours were worse: a full grid sweep *per band* and two Skia draws per cell per band. There was no
rendering benchmark of any kind against which to say any of this.

## Decisions

1. **Measure first, in two instruments, because one of them lies.** New
   `tests/JGraph.Benchmarks/SurfaceRenderBenchmarks.cs` runs `surf` at 100²/250²/500² and `contourf`
   at 200²/20 levels. The counting render context gives pipeline cost with rasterization removed —
   but on its own it is the **wrong instrument for batching**, because it charges nothing for a draw
   call and so shows all of the cost and none of the benefit. A `Surface{250,500}Raster` pair against
   a real `SKSurface` was added, and that is the number that decides whether a rotate drag keeps up.

2. **The painter's-order sort is analytic, not comparative.** Under orthographic projection A occludes
   B iff `(xa-xb, ya-yb) = s·(dx, dy)` with `s > 0` — **z never appears** — so ordering cells by their
   ground footprint is correct independent of height, and is *strictly more correct* than the mean-
   depth sort it replaced, which mis-orders a tall spike against a flat neighbour. The two sweep
   directions come from three `projection.Project` calls rather than from exposed fields, which
   absorbs a descending `_x`, a signed axis span, and any future non-linear normalization. It is valid
   only for a monotone-gridded height field, so **the depth sort stays live and tested** behind a
   per-`SetData` monotonicity check — and stays for a second reason: M45.A's parametric surfaces will
   not be height fields.

3. **Two new seam members on `IRenderContext`: `DrawTriangles` and `DrawPaths.`** A **non-indexed**
   triangle soup, not an indexed mesh: SkiaSharp 2.88 exposes indices only as `UInt16[]`, which caps a
   mesh at 65,536 vertices (a 257×257 grid overflows), and MATLAB's default `shading faceted` needs a
   flat per-facet color that shared vertices cannot carry. `DrawPaths` draws many sub-paths as one
   path so adjacent sub-paths tile without antialiasing seams.

4. **`supportsMeshes` is a correctness guard, not a defensive one.** A spike measured against
   SkiaSharp 2.88.8 found `drawVertices` works on the raster backend (a red/green/blue triangle
   samples a correct barycentric blend) and is **silently dropped by both the SVG and the PDF
   backends** — 200 triangles produced byte-for-byte the same 563-byte PDF as drawing nothing. An
   unguarded `DrawTriangles` would have erased every surface from every vector export while the screen
   looked perfect. Export passes `supportsMeshes: false` and falls back to one path fill per triangle
   using the mean of its three vertex colors (exact for faceted shading). The permanent guard is
   `FigureExporterTests.Surface_ExportsToVectorFormats_WithItsGeometry`, written *before* the
   primitive landed.

5. **Batch by anti-diagonal wavefront, not by row.** Row banding is not sufficient: within one row,
   a cell further along the sweep can occlude an earlier one, so drawing a band's facets then its
   edges paints hidden mesh lines over nearer facets. Grouping by `k = sweepRow(r) + sweepCol(c)` is
   *exactly* correct — if A occludes B then both sweep indices dominate, so `kA > kB`, and cells
   within a wavefront can never occlude each other. `rows + cols - 3` batches replace `rows·cols`
   draw calls. Batches are capped at `MaxCellsPerBatch = 4096` regardless, since painter order is
   preserved inside a batch and a single 500² batch would need ~24 MB of scratch.

6. **Edge ownership fixed a pre-existing bug.** Every cell used to stroke all four of its edges, so
   every interior edge was stroked twice — and since the default edge color carries `opacity * 0.8`,
   translucent surfaces double-darkened every interior line. Each edge is now assigned to the nearer
   of its two cells, drawn exactly once.

7. **Contour geometry is a function of the data and the levels and of nothing else**, so it now
   outlives the frame that produced it. New `JGraph.Math/Contours/ContourBands` extracts every band in
   **one sweep**, clipping each cell against only the bands its own corner values can reach and
   counting-sorting the results into per-band runs over shared buffers; `ContourLineSet` holds the
   assembled iso-lines flattened and indexed by level. A pan, a zoom, a resize, a theme switch or a
   colormap change re-maps cached data-space geometry into pixels; only a data or level change
   re-extracts. Wiring lines through `ContourPaths.Assemble` (which already existed, used only by
   `contourc`) is a **correctness** fix first: dashed contours were broken, because the dash pattern
   restarted on every 2-point segment. `SurfacePlot.DrawFloorContours` had the same disease — eight
   full marching-squares sweeps per frame inside the rotate loop, with the level count hardcoded — and
   now shares the cache behind a real `ContourLevels` property.

8. **Parula is the default colormap** for `SurfacePlot`, `ContourPlot` and `ImagePlot`, alongside
   `hsv`/`bone`/`copper`/`pink`/`spring`/`summer`/`autumn`/`winter`/`lines` — sixteen maps where there
   were six. This is the single cheapest step toward MATLAB's look and a deliberate, visible default
   change; saved `.graph` files store the colormap by name, so existing figures are unaffected. Each
   map carries **exactly the stop count its own definition needs**, so it is an exact reproduction
   rather than an approximation. `lines` is a **discrete** palette — a new flag on `Colormap` that bins
   the range one stop each instead of blending, serialized so a saved palette does not come back as a
   gradient. `Resample(n)` and `FromRows(name, rgb)` are the two halves of M45.B's `parula(64)` /
   `colormap(map)` pair.

9. **Lighting ships off by default, because that is what MATLAB does.** `LightingModel` (MATLAB's five
   material coefficients plus a Blinn-Phong shader) and `LightSource` live in `JGraph.Core/Drawing`;
   `LightModel : GraphObject` and `AxesModel.Lights` in `JGraph.Core/Model`; the six material
   coefficients are ordinary `SetProperty` surface properties. **An axes with no lights renders exactly
   what it always did**, which is both MATLAB-faithful and what keeps every pre-existing test green.
   `lighting`, `material`, `light`, `lightangle` and `camlight` stopped being no-ops; `rotate3d` is the
   only accepted no-op left, and honestly so — rotation really is always interactive.

10. **Normals and light positions live in the projection's normalized cube space**, which is what the
    new `Projection3D.Normalize(x, y, z)` exists for. A surface with X in ones and Z in millions would
    otherwise have a normal pointing almost straight along X everywhere and light like a vertical
    wall. The stencil is a **non-uniform** three-point central difference, because the X and Y vectors
    are whatever the caller handed in and a logarithmic sweep is legal; it degrades to one-sided at a
    border *and beside a NaN*, which is what keeps the rim of a hole lit rather than black.

11. **`lighting gouraud` promotes the palette to per-vertex.** The renderer holds one color per *cell*
    for a flat-colored surface, so there is physically no slot to interpolate a smoothly-lit facet
    into; `PaletteCache` is therefore keyed on a `perVertex` flag rather than on `SurfaceShading`.
    Without this, `lighting gouraud` would have been silently identical to `lighting flat`.

12. **Caching is split by what can invalidate it.** View-independent results (Z bounds, the drawable
    mask, the color palette, monotonicity, assembled contours) are cached on the plot and nulled in the
    setters. **View-dependent results are not cached at all** — projected points and shaded colors
    change with azimuth, elevation and plot area, none of which are plot properties and none of which
    raise the plot's `Invalidated`, so no hook could invalidate them correctly. The arrays are *kept*
    and refilled each frame through a new `RenderScratch`, since removing the allocation was the goal;
    a caller that loses `Interlocked.Exchange(ref _rendering, 1)` takes freshly allocated arrays for
    that pass rather than tearing the shared ones.

13. **Figure viewer panels start hidden.** `FigureViewModel._showPlotBrowser` and `_showInspector`
    default to `false`; the toolbar toggles pick the value up through their existing two-way bindings.

14. **Wave 5 (level-of-detail during a gesture) was planned and deliberately not built.** The premise
    was already wrong — WPF coalesces `InvalidateVisual` and Win32 coalesces `WM_MOUSEMOVE`, so the
    pipeline never ran at pointer rate — and the waves above removed the cost it was meant to hide. It
    is recorded here rather than left as an open TODO.

## Consequences

- **Measured, `surf` (raster frame, real `SKSurface`):**

  | | before | after | |
  |---|---|---|---|
  | 250² | 125.8 ms, 6.87 MB | 48.4 ms, 39.8 KB | 2.6× |
  | 500² | 497.7 ms, 27.57 MB | 142.6 ms, 79.9 KB | 3.5×, **350× less garbage** |

- **Measured, `contourf` 200²/20 levels:** a raster frame went 96.0 ms / 12.2 MB → **36.7 ms / 27 B**;
  the pipeline repaint went 22,993 µs / 9.15 MB → **1,162 µs / 1 B** (**19.8×**), and a cold first
  draw is 5,862 µs — still 3.9× better than every frame used to cost.

- **Measured, lighting** (500² pipeline, counting context): 8.07 ms unlit → 21.7 ms lit flat / 20.9 ms
  lit gouraud. That ~13 ms is the standing price of turning lighting on and cannot be cached, since it
  depends on the camera. `Math.Pow` for the specular falloff was the suspect and was replaced with
  repeated squaring for the whole-number exponents every preset uses; it bought about 1.5 ms, so the
  cost is spread across the shader rather than concentrated — recorded because the obvious suspect was
  mostly wrong.

- **Diagonal traversal is a cache trap.** The first cut of wavefront batching was **4× slower** than
  the baseline (500²: 9.2 ms → 39.1 ms) purely from walking a row-major `double[,]` diagonally — four
  Z lookups per cell corner, every one a miss on a 500-row grid. Two view-independent caches keyed by
  the cell's **top-left vertex index** fixed it and then some, and removed the integer division that
  cost ~50 cycles a cell along the way.

- **`drawVertices` does not antialias**, measured: zero antialiased pixels with the AA flag on or off,
  where the same triangle through `DrawPath` produces 60. Both halves of that prediction held — it is
  why each batch's outer boundary is stroked with a 1 px AA polyline, and why separate batches still
  tile with **zero** seam pixels. `DrawVertices` also takes exactly-sized arrays, so wavefronts of
  varying size would allocate per call; batches are padded to a power-of-two triangle count with
  degenerate transparent triangles, one buffer pair per size class.

- **Removing the contour seam stroke was half a fix.** Batching a band into one path does make its
  cells tile seamlessly, but two *adjacent bands* are two separate paths, so the shared edge is
  antialiased from both sides and lets a hairline of background through. A threshold test would have
  missed it; the check asserts the scan across the strips brightens monotonically, which a bleed breaks
  as a lightness spike. Each band now traces its own outline in its own fill color — one stroke per
  band rather than one per cell, and 12 ms of the 36.7 — left off when the plot is translucent, since
  there it would darken every band's rim, which was the original artifact.

- **`ContourPaths.Assemble` was dropping fragments.** A sample sitting exactly on a level makes the
  cells around it interpolate both crossings to the same grid vertex, so marching squares emits a
  segment from a point to itself: `z = x² + y²` at level 1 assembled into one 71-point circle plus six
  2-point stubs. Filtering degenerate segments fixes `contourc` as well as rendering.

- **Parula's numbers are the R2017a table, not the widely copied port.** MATLAB has shipped two
  parulas — R2014b starting at `[0.2081 0.1663 0.5292]` and R2017a at `[0.2422 0.1504 0.6603]` — and
  the Python port that circulates is the old one. The 80-knot R2017a table resampled onto 33 stops
  tracks the original to within 5/255 on every channel; 17 stops left 14/255, which is visible.

- **Two plan assumptions about colormaps were wrong.** "The 2-stop maps band visibly" is false —
  `cool` and `gray` are straight lines in every channel, so two stops reproduce them exactly. What
  matters is not stop count but whether a stop lands where the definition *turns*, and the map that
  actually needed them was `hot`, whose four evenly spaced stops put its turns at the thirds instead
  of at 3/8 and 6/8. And the 256-entry LUT was built, measured and **backed out**: MATLAB sizes each
  map's rows to its own definition, so a 256-row grid imposed on a nine-stop definition lands between
  the turns and rounds them off — with the LUT in, `hot`'s yellow turn came out `#FFFE00`. It was
  buying a multiply on a path whose results are already cached a layer up, in exchange for undoing the
  fidelity work.

- **'reverselit' follows the camera, not the light.** Flipping a normal that points away from the
  *viewer* is what keeps the underside of a folded surface readable; a facet facing the camera but lit
  from behind is correctly ambient-only. The first draft of that test asserted the opposite and was
  wrong, not the code.

- **Divergence recorded:** MATLAB's `camlight` resolves a fixed world position at call time and does
  not track the camera, so its highlight is left behind on the first drag. `LightModel.FollowsCamera`
  defaults false but the `camlight` verb sets it, which is the useful reading for a figure you rotate;
  clearing it restores MATLAB's behavior.

- **Not done, recorded:** the contour band outline strokes every sub-path, which means every cell
  perimeter in the band, where only the band's outer boundary needs covering. Identifying boundary
  edges is possible — a clipped edge has both ends at a clip level — but it is real complexity for a
  frame that already fits in 37 ms.

- Serialization moved in two places for lighting (the six surface properties and the new axes light
  list) and one for contours (`ContourLevels`, the CLim trio, the discrete-colormap flag). Every new
  DTO field defaults to what a plot has always had, so old documents read back unchanged;
  `Surface3DSerializationTests` pins both directions, including a document with no lights.
