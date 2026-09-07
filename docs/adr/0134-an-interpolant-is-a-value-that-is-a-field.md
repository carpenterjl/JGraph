# ADR 0134 — An interpolant is a value that is a field

## Status

Accepted (M129, 2026-09-07). Built by an implementation agent in its own worktree and landed by
the integrator under the arrangement ADR 0131 records; landed fourth.

## Context

`polyfun` had `delaunay`, `delaunay3`, `griddata` in two of its five methods, `interp2` and its
relatives, and not much else that concerns points off a grid. What MATLAB users reach for when
their samples are scattered is `griddata` with `'natural'`, `'cubic'` or `'v4'`; `scatteredInterpolant`
when they mean to ask the same surface more than once; `griddedInterpolant` for the gridded case;
`delaunayn`, `tsearchn`, `dsearchn`, `convhulln` for the geometry underneath; `boundary` for the
outline of a cloud; and the STL pair for meshes on disk. Nine of the eleven were missing, and the
two interpolant objects had no precedent here beyond ADR 0102's `pp`.

The plan named one divergence in advance — MATLAB's triangulation is Qhull's, and co-circular
points triangulate differently — and asked that the fixtures compare values rather than triangle
lists. It did not anticipate the one that took the most work: MATLAB's `'cubic'` surface, whose
gradient rule was recovered by measurement but whose interior element was not.

## Decision

### An interpolant is a struct that is called

ADR 0102 made a piecewise polynomial a value that is a curve — a struct with MATLAB's field names,
built once and read once. `scatteredInterpolant` and `griddedInterpolant` are the same shape: a
struct holding exactly the properties MATLAB documents, tagged with the class name so `class(F)`
answers it, and called with parentheses. Being a struct is what makes `F.Values = 2*v` and
`F.Method = 'natural'` work with nothing written for them — they are field writes, and the next call
reads what they left. One branch in `Interpreter.EvaluateCall`, above the struct-subscript branch
for the same reason the `containers.Map` branch sits there, is what turns `F(xq, yq)` into a reading
instead of an index.

MATLAB documents that replacing `Values` re-uses the triangulation, which is a promise about cost.
It is kept by a cache hung off the struct's own field table: a call whose points have not moved
re-uses the surface and re-hangs the values; a call whose points have moved rebuilds. Comparing the
points costs a pass over them, nothing beside a Delaunay.

### One Bowyer–Watson, written once for any number of directions

`DelaunayN` is `Delaunay` and `Delaunay3D` with the dimension as a parameter. The in-sphere test is
the lifted-paraboloid determinant on a simplex wound to positive volume, and both determinants are
taken by Gaussian elimination on a small dense matrix rather than by an expansion, because the
expansion has a different shape in every dimension. Written as rows `(pᵢ − q, |pᵢ − q|²)`, that
determinant is positive exactly when the point is inside, in every dimension — assuming a parity
factor made the three-dimensional tessellation come back *empty*, since no cavity was ever found,
a silent failure that only showed two verbs downstream. `delaunayn.m` supplied the two rules that
are not the algorithm: the zero-volume strip after the tessellation, with MATLAB's own tolerance,
and the orientation convention when there are exactly `n + 1` points.

`SimplexMesh` is point location as a bucketed walk rather than a scan: the bounding box is cut into
about one cell per simplex, each remembering a simplex that overlaps it, and a query starts from its
own cell and crosses the face whose barycentric coordinate is most negative. That is what makes
`griddata` over a fine grid linear in the two sizes. The walk gives up after a bounded number of
steps and falls back to a scan, so the fallback is what makes the answer right and the walk only
what makes it quick. `convhulln` is the tessellation's own outside — a facet belonging to one
simplex rather than two — with the planar facets wound once round as `convhulln.m` does after Qhull,
and it agrees with MATLAB index for index.

### The five methods

`'nearest'` and `'linear'` are the located simplex. `'natural'` is Sibson: the region a natural
neighbour loses to the query is the intersection of two convex cells, so it is convex, and its
corners are the circumcentres of the triangles the query destroys together with those of the two
virtual triangles the query makes with that neighbour; collecting the corners and taking the area of
their hull avoids walking two cells in step, and agrees with R2025b to the last bits on all 25
fixture points. `'v4'` is Sandwell's biharmonic spline as `griddata.m` writes it — Green's function
`d²(log d − 1)`, zero diagonal, a dense solve — through a fresh partial-pivoting LU rather than `\`,
which is why the fixture pins it at the plan's `rel=1e-6` while the observed agreement is 1e-14.

`'cubic'` is where MATLAB's surface was measured rather than read, because no source describes it.
`griddata` is linear in `v`, so probing with unit basis vectors gives the exact weight each data
point carries at a query, and fitting along a triangulation edge recovers the edge polynomial.
Every edge is a cubic Hermite (residual 2e-15 over sixteen samples) whose end slopes define one
gradient per vertex, consistent to 3e-15 across five directions — and that gradient is the
**area-weighted average of the plane gradients of the triangles sharing the vertex**, to 1e-14 at
every interior vertex tested. That rule is implemented here. The interior is where the two part,
and the divergence below records what was tried. What is written is the reduced Clough–Tocher
element on the same boundary data: C¹ everywhere, cubic along every edge, exact on a linear field.

### `boundary` is an alpha shape, and `boundary.m` says which one

The alpha spectrum is the distinct circumradii of the Delaunay simplices, sorted downwards —
measured against R2025b's `alphaSpectrum`, the same 61 numbers. The critical alpha is the smallest
that leaves the set in one piece, where simplices sharing a corner are connected and a point no
simplex touches is a piece of its own; that definition reproduces `criticalAlpha(shp, 'one-region')`
exactly, and the two nearby definitions do not (facet connectivity gives 11.39, components of kept
triangles alone give 4.81, against MATLAB's 8.32). The shrink factor then indexes the spectrum by
`boundary.m`'s own arithmetic, including its guard that a spectrum whose top is within a thousandth
of the critical value takes the convex hull. Holes are suppressed as `HoleThreshold` does, and where
the shape is pinched at a point the walk passes through the pinch rather than closing — the
furthest turn to the right, not the left — which is why MATLAB's answer at a shrink of one names a
vertex twice. The loop starts at its lowest-numbered point, where MATLAB's starts. None of this
needed an `alphaShape` object, which is why that class could be declined.

### STL is welded on read

The format stores three loose corners per triangle and no vertex numbering, so reading one means
welding: bit-for-bit equal triples become one vertex, in first-seen order. A triangle is wound to
agree with the normal the file recorded — `stlread.m`'s `product < −0.1` test, with that constant.
Binary and text are told apart by the length the triangle count implies (`84 + 50·n`), because a
binary file may also begin with the word `solid`.

### What is declined, by name

`alphaShape`; `polyshape`, `polybuffer`, `nsidedpoly`, `boundaryshape`; `triangulation`,
`delaunayTriangulation`, `DelaunayTri`, `TriRep`, `TriScatteredInterp` — three object families with
their own method tables, the last two names being MathWorks' own deprecated spellings. `polyfun` is
left with two names, both `polyshape`'s.

### Reference sources

Read for the documented behaviour and the constants that define it, nothing copied: R2025b's
`toolbox/matlab/polyfun/griddata.m`, `griddatan.m`, `delaunayn.m`, `tsearchn.m`, `dsearchn.m`,
`convhulln.m`, `boundary.m`, `stlread.m`, `stlwrite.m`, `xyzchk.m`, `xychk.m`, `qhull.m`. Taken from
them: `griddata`'s method list and identifiers, `gdatav4`'s Green's function and diagonal,
`griddatan`'s duplicate averaging and its `linear`/`nearest` split onto `tsearchn`/`dsearchn` (which
is why one answers NaN outside the hull and the other answers everywhere), `delaunayn`'s zero-volume
strip and tolerance, `convhulln`'s planar re-winding, `boundary`'s alpha selection including the
1e-3 guard, and `stlread`'s normal test. `scatteredInterpolant`'s default extrapolation follows its
method (`'linear'` for `'linear'` and `'natural'`, `'nearest'` for `'nearest'`); `griddedInterpolant`'s
is the method's own name, including after MATLAB has silently changed `'cubic'` to `'spline'` on an
uneven grid.

## Consequences

**The fixture.** `m129_scattered.m`: 200 lines, 0 unexplained against R2025b, eight `div=` lines,
all of which differ. The documented examples, all five `griddata` methods on a forty-point set,
the three extrapolation modes, `boundary` at shrink 0, 0.5 and 1 by count and by area, `convhulln`
index for index, `delaunayn`'s set, `dsearchn`, `griddatan`, both interpolant objects, and an STL
round trip in both formats. No line pins a simplex row index: every line is a value, a total, a
count or a sum of indices, because the tessellation order is Qhull's there and Bowyer–Watson's
here.

**Tests.** `MatlabScatteredDataM129Tests` 17; the assembly at 7,742 on the default lane; four lanes
green. `stess_77.m` is eleven sections and passes on `jgraph.exe`; its STL section spells the
triangulation as a struct, which R2025b would refuse — the third divergence below, and one more
reason the suite is not a MATLAB program. The seventeen `d14` forms and the `d16` row — `griddata`
linear, 1e5 points onto a million-point grid — are written and wait for the arc's timing session.

**Coverage.** `polyfun` goes from 23 to 32 of 34 names; the toolbox total from 322 to 331 of 377.

## Divergences

- **`griddata(…, 'cubic')` draws a different surface inside each triangle**, agreeing with MATLAB
  on every edge and differing by up to 7e-3 in a triangle's middle. On the fixture's forty points
  the five pinned grid values are, MATLAB then JGraph: `-0.025397246374758625` /
  `-0.025329852455970615`, `-0.15919178515383289` / `-0.15918225961897048`,
  `0.055114382604848466` / `0.054862862808529311`, `0.26879122070472966` / `0.26937651301161974`,
  `0.044411768759479192` / `0.044243989757805885`; the 5×5 total `0.39638597096908479` against
  `0.40066831981120488`. MathWorks' patch is not a single cubic (residual 6.5e-3 over 66 samples),
  nor three cubics split at the centroid (1.1e-3), the incentre (5.5e-4) or any of 150 other split
  points (best 5.7e-4), nor six at the centroid and edge midpoints (6.0e-4), nor a polynomial of any
  degree up to seven — plausibly a rational side-vertex blend. The gradient rule it shares with
  this build was measured to 1e-14. Fixture lines `griddata_cubic_1..5` and `griddata_cubic_total`,
  `div=ADR0134`.
- **A scattered interpolant's `'linear'` extrapolation continues a different boundary simplex than
  MATLAB's.** `F(5, 5)` on the fixture's data is `-0.059633373211630814` in R2025b and
  `-0.063987332929066415` here; both continue the surface affinely and differ in whose affine
  function is used. Measuring the distance to a facet properly rather than to its middle made the
  disagreement worse (`0.50775607278613499`), so the middle is kept and MathWorks' rule for the
  choice is unknown. `'none'` and `'nearest'` agree exactly. Fixture line `scattered_outside_linear`,
  `div=ADR0134`.
- **`stlread` answers a struct with `Points` and `ConnectivityList` where MATLAB answers a
  `triangulation`, and `stlwrite` takes the same.** `triangulation` is declined above, and ADR
  0072's rule stands: a value with the properties a script reads beats no value at all. Every
  property the STL pair's own documentation names is present and agrees. Fixture line
  `stl_input_class`, `div=ADR0134`.
- **`delaunayn`, `tsearchn` and `convhulln` list the same simplices in a different order** — Qhull's
  there, Bowyer–Watson's here. On points in general position the two agree about the set (61
  triangles, the same index sum, area, vertex degrees, and the same seventeen hull edges with the
  same winding) and not about the rows, so `tsearchn`'s `t` is a different number for the same
  query. No fixture line pins a row, so no `div=` line carries it; it is recorded here because a
  script that stores a simplex index will see it.

## Still open

- **The macro element behind MATLAB's `'cubic'` is unidentified.** The experiment that would
  finish it is one MATLAB run plus a fit against the next candidate, most likely Nielson's
  rational side-vertex blend; the probing technique — unit basis vectors through a linear
  interpolator — would also answer ADR 0102's open question about `'makima'`'s cross terms.
- **`griddedInterpolant`'s `'makima'` over more than one direction is refused by name**, carrying
  ADR 0102's decision forward: the cross terms are not MATLAB's.
- **The method-change warnings are not printed by the interpolant object** (`'cubic'` → `'spline'`
  on an uneven grid; `'next'`/`'previous'`/`'pchip'` → `'linear'` beyond one direction). The change
  itself is made; `interp2`/`interpn` print the text.
- **`boundary` in space pins its volume only** (`rel=1e-10`, agreeing to 5e-14), not its facet
  count: a three-dimensional alpha shape has cavities as well as holes, and `boundary.m`'s hole
  suppression was reasoned through in the plane.
- **The prober's `stlread` sample needs a file `stlwrite` has written**, and its `stlwrite` sample
  spells `triangulation(...)`, so on a clean run both read as refused in the coverage document —
  the third divergence showing up as a number, not a new problem.
