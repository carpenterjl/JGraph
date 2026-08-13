# ADR 0059 — Volume visualization

## Status

Accepted (M59, 2026-08-13). The fifth chart milestone of the M55–M60 arc, and the second in a row
that adds no chart. `.graph` stays at version 6: nothing here is a new plot object, so there is no
new discriminator for a reader to cope with.

## Context

Twenty-three names — the isosurface family, the stream family, and the handful of verbs that reshape
a field before it is drawn — were the largest remaining family in
`docs/matlab-builtin-coverage.md`, and the one it had the least to say about. Both of the blockers it
had recorded were already gone: a scalar field needs no value type, because since M41 it is a plain
three-dimensional array, and the surface finder arrived in M58, built there deliberately so that the
milestone that needs it would not also be the milestone that debugs it.

What the file did not record is that almost nothing in this family could be *called* yet. A volume
field is written `[X, Y, Z] = meshgrid(x, y, z)`, and `meshgrid` took two vectors.

## Decision

### The grid is either given or it is the whole numbers, and one reader knows how

Every verb in this family takes the same shape of argument list: `isosurface(V, 0.5)` and
`isosurface(X, Y, Z, V, 0.5)` are the same call with and without a grid, and leaving the grid out
means the readings sit on the whole numbers. Two functions in `JgsBuiltins.Volumes.cs` know that —
one for a scalar field and one for a vector field — and every verb reads its field through them.

The alternative was to let each verb count its own arguments, and the cost of that would have been
paid twenty-one times over in exactly the way M46's imaging surface was: each verb correct for the
shapes its own tests fed it, and subtly different from its neighbours everywhere else.

Two details are decisions rather than defaults. The grid may be given as vectors or as the full
arrays `meshgrid` hands back, and a full array is read along the one dimension its coordinate varies
in rather than being required to be a vector — which is what lets a script pass `X` straight through
from where it built it. And whether a grid is present at all is decided by *counting* the arguments,
not by looking at their shapes: for a plane field the grid is two arrays rather than three, so the
count differs per verb and each verb says which it expects.

### A verb draws when nobody wanted the shape, and answers with it when somebody did

`fv = isosurface(X, Y, Z, V, 1)` is a struct of faces and vertices; `isosurface(X, Y, Z, V, 1)` on
its own is a picture. That is MATLAB's rule for this family and it is a real distinction, not a
convenience: colouring a surface before it is drawn is the documented way to use these verbs, and it
needs the shape in hand first.

It is also a distinction that cannot be made after the fact — by the time the call has been
evaluated, "nobody wanted this" looks exactly like "somebody wanted one of these" — so it rides on
`BuiltinFunction.KnowsWhenDiscarded`, the seam M53 built for `ecdf`. Four verbs use it: `isosurface`,
`isocaps`, `reducepatch` and `shrinkfaces`.

**The struct these verbs answer with is the struct `patch` reads**, which is the loop the whole
family exists for, and it did not work: `patch` had no struct form at all. It has one now, matching
`faces` and `vertices` without regard to case because MATLAB writes them one way in a struct and
another as properties.

### Normals come from the field, because the triangles cannot say

The obvious way to find which way a surface faces is to average the directions of the triangles
meeting at each vertex. It does not work here, and finding out why is worth recording: `MarchingTetrahedra`
does not make its winding consistent — ADR 0058 says so plainly — so two triangles sharing a vertex
may report opposite directions and cancel. The first version of this returned zero-length normals on
about a third of a sphere.

The field has no such ambiguity. A surface sits across the slope of the field it was cut from, so the
slope at a vertex is the direction that vertex faces, and it is the same answer however the triangles
around it happen to be wound. That is also what MATLAB's `isonormals` does, so the fix and the
fidelity are the same change. The slope is negated, so normals point away from the higher readings.

Making the winding consistent instead would also have worked, and would be the right fix if anything
here ever lit a face from one side only. Nothing does.

### A tube's width is what the field is doing, and it is clamped

`streamtube` widens where the flow spreads out, which is what `divergence` measures, and the reading
is taken at each point of the line. It is then clamped to between a quarter and four times the base
width. A field can spread arbitrarily fast, and a tube that is a thousand times thinner at one end
than the other has stopped saying anything — the clamp is the difference between a picture of the
flow and a picture of one outlier.

`streamribbon` twists about the line by the curl, worked out once for the whole field and then read
at each point. Writing it the other way round — asking the field for its curl at every vertex — is
the same answer and rebuilds every reading of the curl per vertex; the first draft did that.

### Reduction is vertex clustering, and it searches for its lattice

`reducepatch` moves every vertex to the centre of the lattice cell it falls in, merges the ones that
now coincide, and drops the triangles that collapse. It is cruder than MATLAB's error-driven edge
collapse and it *moves* vertices where MATLAB's keeps a subset of the originals — a recorded
divergence rather than an approximation of the same answer.

The face count a given lattice yields cannot be predicted from the mesh, so the lattice is found by
search: double until the target is reached, then narrow between the last two. Stopping at the first
doubling that succeeds is what the first version did, and it routinely overshot — a mesh asked for a
fifth of its faces was handed half of them. The narrowing is what makes the number mean what was
asked for.

### What was drawn with

Nothing here is a new plot object, and the ADR 0054 claim holds a fifth time. The isosurface family
draws with `patch`, the stream lines with `plot3`, the ribbons and tubes with `surf`, the cones with
`patch` again (all of a call's cones are one patch, so a script gets one handle rather than a cloud
of them), and `contourslice` with `plot3` — one line per level per plane, with the pieces of a
contour that comes apart joined by a break, the way M58's `fimplicit` does. A saved figure therefore
holds the drawing and not the field.

### What had to be built before any of this could be called

Five things, each required by a verb here rather than added alongside them:

- **`meshgrid` in three dimensions**, and `ndgrid` beside it, which had never existed. They differ
  only in which vector runs along which dimension, so they are one function and a swap.
- **`gradient` over an array of any number of dimensions.** `curl` and `divergence` are defined on
  it, and it refused anything past a matrix. The walk is now written against a dimension number,
  because MATLAB reports the columns first and the rows second and getting those two the wrong way
  round would turn a field inside out with nothing to show for it.
- **`patch(fv)`**, above.
- **`interp3`**, which is the trilinear reading the streamlines needed anyway, under the name a
  script would look for.
- **A patch's `Vertices`**, which could not be read back while `Faces` could — the M54 gap again,
  and the fifth instance of it. `Faces` was aliased in M58 and `Vertices` was not, so a patch could
  be asked for half of what a script wrote it with.

## Consequences

`docs/matlab-builtin-coverage.md` moves from **192 to 213 of 267 documented graphics functions**, and
the across-every-kind total by the same twenty-one plus `ndgrid` and `interp3`, which are documented
functions rather than graphics ones.

**The denominator is corrected here, and the correction is the point of this paragraph.** That file
recorded the volume family as 22 names while listing 23 of them, so its graphics denominator was
understated by one: the partition read 22 + 36 + 14 + 2 = 74 against a total of 266, and the truth is
23 + 36 + 14 + 2 = 75 against 267. This is the third time this file's arithmetic has been off and the
third time in the same way — a name present in a list but absent from its count. M54 found `slice`
double-counted, M55 found `bubblesize` and `bubblelim` in neither list, and this is the same shape
again. The check that found it is worth keeping: count the names in the list rather than trusting the
number at the head of it.

**Two of the twenty-three are excluded**, both animation rather than drawing: `streamparticles` moves
markers along a streamline over time and `interpstreamspeed` exists to feed it. The figure model has
no loop, which is what M60's animation seam is for; if that seam lands, these two are a short
follow-on rather than a milestone.

What is left of the graphics remainder is **54 names in four families**: the two excluded volume
names, thirty-six figure-tooling names (M60), fourteen properties and legacy appearance verbs, and
the two chart containers M57 excluded. **After M60 there is nothing left that draws.**

`stess_31.m` is the live check, and its twenty-seven sections found one thing — a claim of mine about
`gradient`'s default spacing that was wrong in the script rather than in the code.

## Live checks for the user

Batch cannot see any of these, so they are listed here rather than claimed:

- An `isosurface` of a real field under rotation, at a grid fine enough to be worth looking at. This
  is the largest patch this build has drawn — a 21³ sphere is about 2,700 triangles and a 40³ field
  is ten times that — and the painter's-algorithm sort is the thing to watch.
- `isosurface` and `isocaps` drawn together, which is the pair that makes a cut-open volume read as
  solid rather than hollow. Whether the cap and the surface meet cleanly at the wall is a thing only
  the eye can check.
- `streamtube` on a field that genuinely spreads, to see whether the clamp on the width is in the
  right place — whether the tube says what the flow is doing or flattens it out.
- `coneplot` at a real cone count, where the cones are one patch: whether they read as arrows at a
  glance, and at what count they stop being distinguishable.
- `contourslice` on three planes at once with a colormap, to see whether the three sets of lines read
  as three planes or as one tangle.
