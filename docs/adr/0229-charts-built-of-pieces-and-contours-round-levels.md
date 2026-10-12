# ADR 0229 — The charts R2025b builds out of pieces, and a contour's round levels: open items 112 and 113

## Status

Accepted, 2026-10-12. The sixteenth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), the two items batch 15 filed (ADR 0228). Every answer was recorded from R2025b (probes
`probe_112` to `probe_112c` and `probe_113` to `probe_113b` in the open-items scratch, then the fixture
`oi_handle_counts_contour_levels`), and the geometry was transcribed from R2025b's own files —
`pie.m`, `pie3.m`, `makebars.m`, `compass.m`, `feather.m`, `meshc.m`, `surfc.m` — rather than
reimplemented from their pictures. As in every batch since 12, each item was wider than filed.

## Context

- **Item 112.** Filed as "handle counts of the multi-piece charts": `pie`, `pie3`, `meshc`, `surfc`,
  `bar3`, `compass`, `feather` and the matrix forms of `errorbar`, `fill` and `polarplot` answered
  one handle where R2025b answers one per piece; `fplot` of a cell was refused, `stairs` of a matrix
  died inside the interpreter (a packed-array walk), and `plotmatrix`'s diagonal handles were 0.
  Underneath the counts:
  - R2025b's `pie` is a patch and a text per slice (handles in a row, patch then text); `pie3` a
    bottom patch, a side surface, a top patch and a text per slice; `bar3` a surface per column, six
    rows of four vertices a box with NaN rows between the boxes; `compass` a line per arrow, the five
    points of `compass.m`'s arrow turned by the reading; `feather` the same with a NaN between shaft
    and head and a baseline; `fill` a patch per polygon and per `(x, y, c)` group; `errorbar` a
    series per column with every matrix argument lending its column; `meshc` and `surfc` the surface
    and a contour of the same grid on the floor (`ZLocation` 'zmin'). This build drew each as one
    object of its own kind, so `h(2).XData`, `set(h(2:2:end), …)` and a loop over the handles read
    the wrong object or failed.
  - `plot(X, Y)` with two matrices of one shape drew one line through every point; R2025b pairs the
    columns, and `polarplot` of two matrices with it.
  - A pie refused a negative share, a short label list and a short explode vector in JGraph's words;
    R2025b drops a negative share with `MATLAB:pie:NegativeData` ("Ignoring negative data."), refuses
    all of them negative, and names `StringLengthMismatch`, `ExplodeLengthMismatch`, `NonFiniteData`
    and `NonNumericData`. A char row is a printf format for the percentages (`'%.1f%%'`), and a
    share under one hundredth reads `'< 1%'`.
  - A patch coloured through its `CData` read its `FaceColor` as a colour; R2025b reads `'flat'` or
    `'interp'`. A text had no `DisplayName`. A contour's `XData`, `YData` and `ZData` were read-only;
    R2025b redraws a contour whose `ZData` is replaced. `fplot`'s line had no `DisplayName`; R2025b
    names it `{sin}`. A patch's material numbers were not refused out of range (a pie's were).
- **Item 113.** Filed as "contour's automatic levels": `contour(magic(4))` drew ten levels spaced
  evenly from 2.36 where R2025b draws `2:2:16`, so every automatic contour drew different lines.
  Measured over a hundred ranges (`probe_113` to `probe_113c`):
  - R2025b's drawn levels are the multiples of a round step up to the data's maximum, and the step
    is the first of the 1, 2, 5 times a power of ten ladder that gives at most eleven of them: for
    data from zero that is the smallest round step past a twelfth of the range (11.9 steps by 1, 12
    by 2, 23.9 by 2, 24 by 5, 56 by 5, 60 by 10). A negative minimum counts as a level and a zero or
    positive one does not — `-magic(4)` lists −16, `[-1 0]` lists both ends, a field from 0 to 1
    starts at 0.1 and one from 2 to 12 at 3 — which is measured rather than explained.
  - `contourc(Z)` follows a rule of its own: the first step of the ladder with at most nine levels
    strictly inside the data (11 steps by 2 where the contour steps by 1; `[-5 15]` by 5 where it
    steps by 2).
  - `contour(Z, n)` is `floor(n)` evenly spaced interior levels for every scalar — 1 is one level at
    the middle, 2.5 is two, 0 is none (an empty manual list and a 2-by-0 matrix), a negative is
    `MATLAB:contour:NMustNotBeNegative`. A vector lists a level once (`[5 5]` reads 5). `LevelStep`
    reads the data's round step whatever list is drawn, and a written step reaches the maximum
    (`[4 8 12 16]`). A constant field warns `MATLAB:contour:ConstantData`.
  - `contourf` lists the data's minimum before its automatic levels and before a count's, not before
    a list a script wrote; `fcontour` with `'Fill'` the same.
  - `ZLocation` is 0 (`ZLocationMode` 'auto') on a contour and a contour3 alike, and takes `'zmin'`,
    `'zmax'` or a height; `'auto'` and `'zero'` are refused with
    `MATLAB:datatypes:InvalidZLocationValue`. `fcontour`'s `LevelStep` is kept as the step
    (`LevelStepMode` 'manual', the list automatic).

## Decision

**Item 112.** The MATLAB-dialect verbs build what R2025b builds (`JgsBuiltins.Pieces.cs`); JGS keeps
its one-object charts and is never routed there.

- `MatlabPieVerb` reads `pie`'s and `pie3`'s arguments in R2025b's words — the negative-share
  warning and refusal, the two length refusals, a format char row — and `MatlabPie` draws
  `pie.m`'s geometry: `max(1, ceil(100 x))` arc points a slice plus the centre twice, from the top
  counter-clockwise, each slice a `PatchPlot` whose `ColorData` is its number over `CLim` `[1 N]`
  (`AutoScaleColor` off, so the colormap colours it as R2025b's does), a `TextAnnotation` at the
  middle angle aligned by `pie.m`'s 45-degree rule, and for `pie3` a side `SurfacePlot` and a top
  patch 0.35 high with `pie3.m`'s lowered front labels. `axis equal off [-1.2 1.2 -1.2 1.2]` when the
  pie owns its axes, `view(3)` for `pie3`. The JGraph extras (`StartAngle`, `Clockwise`,
  `ShowLabels`, `LabelRadius`, `Height`, `Colormap`, `EdgeColor`, `LineWidth`, `FaceAlpha`,
  `DisplayName`) still shape the slices at construction; they are no longer properties of a pie
  object, because there is none.
- `MatlabBar3` transcribes `makebars.m`'s 3-D branch — the box layout for detached, grouped and
  stacked columns, uniform and unequal row positions, the NaN rows and columns — into a parametric
  `SurfacePlot` per column with `CData` its number over `[1 m]`, and sets what `bar3.m` sets on an
  axes it owns: the row ticks, the column ticks and limits, `YDir` reverse, `view(3)`, the grid. The
  renderer's per-object depth sort orders the columns, which is why JGS kept one object.
- `MatlabArrowLines` draws `compass.m`'s and `feather.m`'s arrows as a `LinePlot` each — on the polar
  axes as angle and radius for `compass`, shifted a sample along with a NaN break and a baseline for
  `feather` — all on the first line's colour-order seat; `feather` hands the next plot the seat after
  it, `compass` leaves the count where its lines took it, as the two files do. `ShowArrowHead` off
  (a JGraph extra) draws shafts.
- `MatlabFillPolygons` makes a patch per column and per group, coloured by a colour, by one value a
  polygon (`'flat'`), or by a column of values a vertex (`'interp'`); `patch` stays one object.
- `errorbar` draws a series per column of y, every matrix argument lending its column and a vector
  shared; the handles come back in a row. `PlotColumns` pairs a matrix x with a matrix y of its
  shape. `fplot({f, g})` draws each as `fplot(f)` with the same tail. `stairs` of a matrix walks its
  handles element by element. `plotmatrix(X)` hides a scatter of each column against itself under
  its histogram, so the matrix of handles is full.
- `MatlabSurfaceWithContour` adds a `ContourPlot` of the surface's grid with `RoundLevels` and
  `ZLocation` 'zmin' after the surface, answering `[surface; contour]`.
- A patch's `FaceColor` reads `'flat'` or `'interp'` while it is coloured through its data and takes
  the two words back; the four material fractions and the exponent are refused out of range on every
  patch (`AddPatchMaterialBlock`), and a patch's material is saved with it (it was the pie's alone).
  A text has a stored `DisplayName` (saved). A MATLAB `fplot` line of a named function is named
  `{name}`.

**Item 113.**

- `ContourPlot.RoundLevels` (saved) switches the automatic levels to R2025b's rule:
  `RoundStepFor(min, max, limit, inclusive)` walks the ladder and `MultiplesInside(min, max, step,
  inclusive)` lists the levels (the drawn kind keeps a negative minimum, the strict kind neither
  end); they are the model's own arithmetic, `AutomaticStep` the step for the data, and a written
  `LevelStep` reaches the maximum under it. `LevelsFromCount` marks a list a count made; `LevelList` (what a script reads) puts the
  minimum first on a filled contour with automatic or counted levels. A list a script wrote is drawn
  as written, an empty one included. The MATLAB-dialect `contour`, `contourf`, `contour3`,
  `fcontour`, `meshc` and `surfc` set the flag; JGS and the model's default keep the even spacing.
- `contourc(Z)` in the MATLAB dialect takes `ContourcLevels` (nine strictly inside at most);
  `contourc(Z, n)` and `contour(Z, n)` take `CountedLevels` (floor of the count, none for zero, the
  refusal below zero), a vector of levels is listed once each, and a constant field warns.
- `ContourPlot.ZLocation` (`ContourZLocation`: at its levels, zero, the data's minimum or maximum, or
  `ZLocationHeight`; saved, with `ContoursAtZero` kept as the zero case) is what `Render3D` lays the
  curves on. The MATLAB-dialect `ZLocation` reads 0, `'zmin'`, `'zmax'` or the height, takes those
  and refuses the rest in R2025b's words; JGS keeps `'auto'` and `'zero'` too. `ZLocationMode` reads
  'manual' once one is written.
- A contour's `XData`, `YData` and `ZData` are writable; a `ZData` of another size counts its
  positions out again when they were counted in the first place.
- `fcontour`'s `LevelStep` is the plot's step in the MATLAB dialect; JGS still spells it out as a list.

## Divergences

- **`compass` draws on a polar axes**, its lines' `XData` and `YData` the angles and radii; R2025b's
  draws through the legacy `polar` on a plain `axes` with Cartesian `XData` (`[0 1 0.72 1 0.88]` for
  the arrow `1 + i`). The count, the seats and the colours agree.
- **A patch's `XData`, `YData`, `ZData` and `CData` are rows** and its `Vertices` carry a third
  column; R2025b's are columns (`[20 1]` for a slice) and `Vertices` is n-by-2 for a flat patch.
- **A text's default `FontSize` is 12 and its `Color` reads `'none'`** (the theme's ink); R2025b's
  text is 10 point in `[0.1294 0.1294 0.1294]`, the ink open item 105 covers.
- **A `bar3` surface's `EdgeColor` reads `'flat'`**; R2025b's is the ink. Its `Tag` is `'bar3'` in
  both.
- **`meshc(Z)` and `surfc(Z)` of one matrix give the contour vector `XData` and `YData`**; R2025b's
  are the meshgrid matrices, because `meshc.m` makes them. The three-argument forms agree.
- **A surface over a grid that is not a mesh of two vectors gets no floor contour** from `meshc` and
  `surfc` (one handle); R2025b contours it.
- **`fplot` of an anonymous function has no `DisplayName`**; R2025b's is the TeX of its body
  (`{x}^{2}`, `{sin}({x})`).
- **`pie` and `pie3` take JGraph's option pairs** after their data; R2025b refuses them
  (`MATLAB:pie:InvalidExplodeTypeNumeric`, `MATLAB:pie3:TooManyArguments`).
- **A level equal to the data's maximum draws nothing**; R2025b's contour matrix carries a one-point
  column for it, and its column counts differ from this build's tracer throughout (ADR 0167).
- **A grid holding NaN or Inf loses its lowest curves in the contour matrix** (`contourc` of
  `[1 NaN 3; 4 5 6; 7 8 16]` lists 6 to 14 where R2025b lists 2 to 14): the tracer gives up a cell
  with a non-finite corner where R2025b traces round it. The levels drawn are the same.
- **A flat contour added to a 3-D axes rides at its levels**, as a `contour3` does, though its
  `ZLocation` reads 0; R2025b lays it on z = 0.
- **The axes' limits after `surfc` are padded** (ADR 0073): `[0.25 16.75]` where R2025b's are `[1 16]`.
- **`Children` is a row** (ADR 0202).

## Consequences

- Silent wrong answers fixed: every automatic contour drew different lines from R2025b's and
  `contourc` listed different levels; `plot(X, Y)` of two matrices drew one line; a pie's, a raised
  pie's, a bar field's, a compass's, a feather's, a filled polygon's, an error bar's and a floor
  contour's pieces could not be reached by handle; `contour(Z, 1)` drew a level at one;
  `stairs(magic(3))` failed; a negative pie share was refused.
- The M78 furniture wave's `ZLocation` vocabulary ('auto' and 'zero', never recorded as a
  divergence) is R2025b's in the MATLAB dialect; JGS keeps both words.
- Five stress scripts (outside git) had frozen the one-object answers — a pie's `Values` and
  `Labels`, a compass's `VData`, a `bar3` surface's `Horizontal`, `ZLocation` 'zero' — which R2025b
  refuses as this build now does; `stess_27`, `stess_28`, `stess_29`, `stess_50` and `stess_51`
  read the pieces instead. A targeted `dotnet test --filter FullyQualifiedName~<fixture>` matches no
  parity fixture (the name is a theory argument); `DisplayName~` does.
- Fixture `oi_handle_counts_contour_levels`, 230 lines, all exact (the contour matrices compared by
  the levels they carry, without the maximum's one-point column and without the NaN grids).
  `OpenItemsBatch16Tests` (21 cases). Full suite 11,016 of 11,016; stress 89 of 89.
  Tests that pinned the old answers now hold R2025b's: `MatlabPieTests`,
  `MatlabM79TablePiePropertyTests` (a slice is a patch), `MatlabChart3DTests` (`bar3` and `pie3`),
  `MatlabCompassFeatherTests`, `MatlabDecorationTests` (nine round levels), `MatlabM78FurniturePropertyTests`
  (`ZLocation` 'zmin'), `MatlabCompositionTests` (the hidden diagonal scatter).

## Still open

Filed in the open-items list: 105 (R2025b's default colour order and ink), and 114 (the row shapes
of a patch's data and a text's default font, above).
