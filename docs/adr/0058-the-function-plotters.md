# ADR 0058 — The function plotters

## Status

Accepted (M58, 2026-08-13). The fourth chart milestone of the M55–M60 arc, and the one that adds no
chart. `.graph` stays at version 6: nothing here is a new plot object, so there is no new
discriminator for a reader to cope with.

## Context

Sixteen names — `fplot` `fplot3` `fsurf` `fmesh` `fcontour` `fimplicit` `fimplicit3` and the nine
`ez*` spellings — take a *function* where every other verb takes data. `docs/matlab-builtin-coverage.md`
had read them correctly for three milestones: the sampler is the work and the drawing is not.

What that reading did not anticipate is that only half of them have anything to sample.

## Decision

### A straight line between two readings is accepted when the curve does not depart from it

`JGraph.Math/Sampling/AdaptiveSampler1D.cs` is the milestone. It takes an even set of readings, then
repeatedly probes inside every interval it has not yet accepted and splits the ones whose probes miss
the chord. Curvature buys points and flatness does not, which is the whole reason to sample
adaptively — a uniform grid dense enough for the sharp part of a curve is wasted everywhere else on
it. `fplot(@(x) 2*x + 1)` takes twenty-three readings and stops; `fplot(@(x) atan(50*x))` spends more
than half of its readings on the twentieth of the domain where the curve turns.

Two details are decisions rather than defaults.

The probes sit at a third and two thirds of the way across an interval rather than at its middle. A
single midpoint probe can be fooled by a curve that happens to cross its own chord there, which is not
a rare accident but exactly what a periodic function does when the grid lands on its zeros.

And the function is asked for a whole round of probes at once. A handle written the MATLAB way
answers an array, and asking it once per reading would be needlessly slow; a handle that cannot answer
that way is asked one parameter at a time instead. The choice is made from the answer's *length*
rather than from the handle's text, and it is made once per function.

### A surface is a grid, so there is nothing to refine into

`fsurf`, `fmesh`, `fcontour`, `fimplicit` and `fimplicit3` read an even grid and do not refine. This
is not a shortcut taken for time: a surface in this build is a rectangle of readings with rows and
columns, so there is nowhere to put an extra reading that belongs to one part of the picture and not
to the rest of its row. Refining would mean a new plot object holding an irregular mesh, which is a
different milestone with a different reason to exist.

Density is therefore the whole of the control a caller has over those five, which is exactly what
MATLAB calls it — so `MeshDensity` passes straight through, at MATLAB's own defaults: 23 readings for
a curve, 35 for a surface, 71 for a contour, 151 for an implicit curve.

### A curve that runs away is a break, and the readings that ran away are dropped

`1/x` over `[-5, 5]` has no value at the origin and enormous ones beside it. Drawing those readings
puts a wall across the picture and flattens everything else into its base; MATLAB avoids this by
choosing axes limits that leave the pole out.

Here the decision is made about the *readings* instead: a value further from the middle of the
readings than twenty spreads is the curve leaving rather than a reading of it, and becomes a gap. The
spread is measured over the even pass with the outer twentieth at each end left out, so one reading
taken beside a pole cannot pass itself off as the curve's range — and it is then held fixed, because
letting it follow the refinement would move the target every time a probe landed nearer the pole.

Two things follow. Refinement is what makes the gap *narrow*, so the deciding happens after the
refinement rather than during it; and a run of readings crowded against one pole is collapsed to a
single gap, because one pole is one break. `'ShowPoles', 'off'` keeps every reading, which is that
option's documented meaning read literally.

A curve that will not flatten is not the same thing as a curve running away. `sin(1/x)` oscillates
without bound in frequency near the origin and stays inside [-1, 1], so it is sampled densely and left
whole.

### Six tetrahedra, not fourteen cases

`fimplicit3` needed the surface where a field on a grid is zero, so `JGraph.Math/Contours/MarchingTetrahedra.cs`
was built here — deliberately ahead of M59, which asks the same question of measured data instead of a
formula.

Each cell is cut along a fixed main diagonal into six tetrahedra, and a tetrahedron is crossed by the
surface in one of only three ways: not at all, in a triangle cutting off one corner, or in a
quadrilateral separating two corners from the other two. Marching cubes has fourteen distinguishable
cases and several of them are genuinely ambiguous — two opposite corners inside can mean one surface
or two, and a table has to pick. A tetrahedron has no such case, so there is nothing to pick and no
256-entry table to get wrong. The cost is more triangles for the same surface.

The fixed diagonal is what keeps neighbouring cells agreeing about where their shared face is cut, and
crossings are named by the pair of grid corners whose edge they sit on so that two tetrahedra meeting
along that edge share one vertex. Together those two make the result watertight, which is what the
unit suite checks: every edge of a closed surface belongs to exactly two triangles. Face winding is
not made consistent, because nothing in this pipeline lights a face from one side only.

### The legacy nine are the modern seven with two differences

`ezplot` and its family are their modern counterparts with a domain that runs over a turn of the
circle rather than from −5 to 5, and a function that may be written as text. Everything else — where
to read, what to draw, what to hand back — is the same code, which is why a script can move from
`ezsurf` to `fsurf` and get the same picture.

The text form is why they are declared beside `eval` rather than beside `fplot`: turning `'x*sin(y)'`
into something callable means naming its variables and then evaluating a handle, and only something
holding the running interpreter can do the second half. A name in the text that nothing in the
workspace answers to is a variable, taken in alphabetical order; an expression naming fewer variables
than the verb needs is filled out from x, y and z, which is what makes `ezsurf('x^2')` a surface
rather than an error.

One deliberate divergence there. MATLAB reads a name the workspace already answers to as that value,
so `x = 1:10` earlier in a script quietly turns `ezplot('x^2')` into something else. The six letters
these verbs are documented in terms of — `x y z t u v` — are read as variables here even when the
workspace has one of its own. The change can only turn a broken call into a working one.

`ezplot` is also the one verb that decides what to draw from what it was handed: text naming two
variables is an equation, and the curve drawn is where it holds. A function handle is always read as a
function of one variable, because a handle carries no count of its own arguments in this build —
`fimplicit` is the same drawing under the name that says so.

### What was drawn with

Nothing here is a new plot object, and the ADR 0054 claim holds a fourth time. `fplot` hands its
readings to a line, `fplot3` to a line in space, `fsurf`/`fmesh` to the surface, `fcontour` to the
contour machinery, `fimplicit` to a line again (marching squares at one level, with the branches of a
curve that comes apart joined by a gap so one object holds all of it), and `fimplicit3` to a patch. A
saved figure therefore holds the drawing and not the function, which is the one thing about these
verbs a script has to know.

### What the probes found: four objects could not be asked for their own data

Following the CLI probe rule turned up the same gap four times. `get(h, 'ZData')` on a `plot3` handle
named a property the object did not answer to, while the same call on a `plot` handle worked — and so
did `XData` on a surface, a contour and a patch.

The cause is M54's: the property table is reflection over browsable properties, and a plot's
coordinates are plain arrays behind a setter that takes all of them at once, so reflection does not
carry them. The alias layer had been written for `XYPlot` and for `Scatter3DPlot` and stopped there.
All four are aliased now — writing one coordinate of a 3-D line keeps the other two, and a patch
answers with its faces counting from one, the way a script wrote them.

It surfaced writing `fplot3`, but the gap belongs to `plot3`, `surf`, `contour` and `patch`, all of
which have had it since M54. It is fixed rather than recorded for the reason M57's scalar subscript
was: the direction is error to answer, so no script can be reading differently now.

## Consequences

`docs/matlab-builtin-coverage.md` moves from **176 to 192 of 266 documented graphics functions** and
the across-every-kind total from **742 to 758 of 2,027** — all sixteen names, the seven `f*` verbs
documented under `matlab/graphics` and the nine `ez*` ones under `matlab/specgraph`. The builtin table
is unchanged: not one of the sixteen is documented as kind *builtin*.

What is left of the graphics remainder is **74 names in four families**: twenty-two volume names
(M59), thirty-six figure-tooling names (M60), fourteen properties and legacy appearance verbs, and the
two chart containers M57 excluded.

`MarchingTetrahedra` is the piece M59 will draw its isosurfaces with, and it arrives tested on its own
rather than through the verb that first needed it — which is the point of building it here.

`stess_30.m` is the live check.

## Live checks for the user

Batch cannot see any of these, so they are listed here rather than claimed:

- `fplot(@(x) 1./x)` on screen — whether the break reads as a break rather than as a missing piece,
  and whether the y range that is left is a useful one.
- A `fimplicit3` surface under rotation, which is the first patch in this build with thousands of
  small triangles: whether the painter's-algorithm sort holds up at that face count and at what size
  it starts to cost.
- `fsurf` with `ShowContours` on, at a real window size, where the floor contours and the surface
  share a colormap.
- `ezpolar` of a rose curve — whether the angles the sampler chose are dense enough where the petals
  turn.
- `fcontour` with `Fill` on and a `LevelStep`, to see whether the bands and the colorbar agree.
