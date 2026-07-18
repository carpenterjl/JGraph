# ADR 0022 — Interactive 3D plotting via axonometric projection over the 2D pipeline

## Status

Accepted (M20b, 2026-07-18).

## Context

JGraph's rendering stack is strictly 2D: `IRenderContext` primitives, a 2D `AxisTransform`, and a
2D-only axis model. The user wants MATLAB-style `meshgrid`/`mesh`/`surf`/`meshc` with interactive
mouse rotation, plus `contour`/`contourf`, `imagesc`/`pcolor`, `colormap`, and a colorbar — from JGS,
C#, and Python alike.

## Decision

1. **3D is a mode of `AxesModel`, not a new axes class**: `Is3D`, an always-constructed `ZAxis`
   (`AxisModel`), and camera angles `Azimuth`/`Elevation` (MATLAB `view` defaults −37.5/30).
   `RecomputeDataBounds` unions `IHasZData.GetZDataBounds()` into the Z axis. Everything keyed to
   `AxesModel` (serialization, subplot layout, inspector, plot browser) keeps working.
2. **Projection, not a 3D engine.** `Projection3D` (JGraph.Math) implements MATLAB's `viewmtx`
   orthographic camera over a normalized data cube, scale-fit per frame to the plot rectangle, and
   returns screen position + viewer depth. `FigureRenderer` gains an `Is3D` branch that draws the
   three far box faces with grid lines, dispatches plots implementing `I3DDrawable` with the shared
   projection, and places tick labels along adaptively chosen front edges. No `IRenderContext`
   changes: surfaces are per-cell `DrawPolygon` quads, depth-sorted with a painter's algorithm.
   Known artifacts on self-intersecting geometry are accepted (MATLAB's `painters` shares them).
3. **`SurfacePlot`** covers surf/mesh/meshc via a `SurfaceStyle` enum + `ShowContourBelow`;
   **`ContourPlot`** (2D) covers contour/contourf using new `MarchingSquares` math (16-case lines;
   per-cell band clipping for fills — adjacent cells share interpolated crossings, so bands tile
   without global polygon assembly). `imagesc`/`pcolor` reuse `ImagePlot`.
4. **Colorbar**: `ColorbarModel` on the axes (like the legend); `ColorbarRenderer` reserves right
   margin during layout and draws a gradient strip + value scale from the first visible plot
   implementing `IColorMapped` (ImagePlot, SurfacePlot, ContourPlot). `Colormap.TryGetByName`
   backs the `colormap("jet")` verb.
5. **Interaction**: dragging in `PanMode` rotates when the axes under the pointer is 3D
   (0.4°/px, elevation clamped ±90); the wheel dollies all three ranges about their centers;
   rectangle-zoom and the data cursor are inert on 3D axes. `AxesViewState` now captures the camera
   and Z range, so rotate/dolly/reset ride the existing undo pipeline unchanged.
6. **Serialization**: format version 2 — `AxesDto` gains `Is3D`/`Azimuth`/`Elevation`/`ZAxis`/
   `Colorbar` (optional, defaulted → v1 documents load unchanged); new `SurfacePlotDto` ("surface")
   and `ContourPlotDto` ("contour").
7. **JGS matrices are arrays of row arrays** — no new value type. `meshgrid` returns `[X, Y]`
   (destructured via M20a's `let [X, Y] = ...`); elementwise arithmetic and the `Math1` builtins
   recurse into nested arrays, so `sin(X * X + Y * Y)` works on matrices; `zeros/ones(r, c)` build
   matrices; a shared `Matrix()` argument helper converts (with a clear ragged-row error).
   Comparisons/masks stay flat-array-only (M18 semantics untouched). 13 new builtins registered in
   both `JgsBuiltins` and `JgsBuiltinCatalog` (pinned by test).

## Consequences

- Practical surface size is ~150×150 cells (per-frame project + sort during drag); documented.
- The data cursor reports nothing in 3D axes (the 2D mapper cannot invert the projection).
- A `.graph` document written by this version is rejected by older builds (format version 2).
- New tests: projection/marching-squares math, 3D model/rendering/interaction/serialization, and
  JGS matrix + 3D-verbs suites (704 total, all green).
