# ADR 0070 — Remediating what the form probe found

## Status

Accepted (M70). A remediation milestone: the counterpart to M69's audit. Its deliverable is
documented MATLAB forms that now run, and the measurement moving to prove it.

## Context

M69 stopped measuring MATLAB compatibility by **name** and started measuring it by **form**
(ADR 0069). It ended with a worklist and an unusually honest caveat about it: of 2,422 documented
syntax forms, 526 came back with an `error` verdict, and **`error` is a lead, not a finding** —
roughly a third of those verdicts were the prober's own sample being wrong rather than the build.

A milestone that started from that bucket would have spent its length re-discovering which third.

## Decision

### Work from the arity refusal, not from the error bucket

There is one signal inside `error` that cannot be the prober's fault: **the build's own arity check,
refusing by name the command being probed**. When `surf(Z, C)` comes back with

> `surf expects 3 argument(s), but got 2.`

no sample table is to blame. That message is this build saying it does not accept a form MATLAB
documents. Filtering the bucket that way left **190 refusals across 97 names** — a worklist about a
third the size of the raw one and made of leads that are already findings.

**Forty of the 190 were re-run by hand at the CLI before the plan was written**, per the standing
M46 rule. **34 were genuine.** The milestone was scoped from those 34, not from the 526.

### Wave A — the target argument, and a verifier that isolates it

The largest single finding. `PeelAxes` had existed since M51, with 59 callers and a doc-comment
opening "Every drawing verb takes one." It was not true: `surf(ax, Z)`, `mesh(ax, Z)`,
`stem(ax, x, y)` and about a hundred others read the handle as *data*, which is why
`line(ax, x, y)` complained that its three coordinates had lengths 1, 3 and 3. A script that says
`ax = subplot(2, 2, 1); surf(ax, Z)` — the ordinary way to draw into a panel — did not work.

One wrapper, `OnNamedAxes`, now peels the handle before a verb reads its own arguments and runs the
body against that axes without making it current. About 35 verbs across seven files opt in by
wrapping. This is **purely additive**: a leading handle was an error in every verb it touches.

The wave's real deliverable is `tools/matlab-checklist/verify-target-forms.py`, and its design is
the point. For each command the R2021b dump gives an axes-target role, it takes a form the build is
**measured** to accept — read from `form-probe-results.csv` — and re-issues that exact call with
`ax` in front. If `surf(X, Y, Z)` runs and `surf(ax, X, Y, Z)` does not, nothing but the handle
changed, so nothing but the handle can be blamed. It went **49 of 86 → 86 of 86**.

**The verifier needed correcting twice, and both corrections are the point of having written it
down.** Its first run reported `cla`, `bubblesize` and the three tickformat verbs as failures —
because the accepted form it was re-issuing already carried `gca` as a sample, so it was passing two
targets. It was measuring itself. And a hand probe afterwards found three failures the verifier
missed (`caxis`, `light`, `image`), because **it checks only one form per command**. A verifier that
mis-classifies is worse than no verifier; both limits are commented at the site.

### Wave B — the argument tails

`image` and `imagesc` took one argument and refused the rest, which is thirteen documented forms
between them: `(x, y, C)`, the `'CData'`/`'XData'`/`'YData'` pairs, and `imagesc`'s trailing
`clims`. `x` and `y` give the two ends of the span the raster covers — MATLAB reads only their first
and last element, whatever their length — so the whole family lands on `ImagePlot.XExtent` and
`YExtent`, which the model has carried since M6.

`errorbar` documented five forms it would not take. `errorbar(y, err)`, `errorbar(x, y, neg, pos)`
and a trailing `LineSpec` now run; the asymmetric case needed nothing new below the builtin, because
`ErrorBarPlot` has held a separate low and high array since M6 and the symmetric constructor passes
the same one twice. **The horizontal forms are refused by name** rather than drawn vertically — this
renderer draws the whisker along y only, and accepting `'horizontal'` silently would be worse than
refusing it.

`pcolor(C)` generates the grid its cells sit on. `subplot`'s fourth word and `tiledlayout('flow')`
are accepted, and any other word still refuses by name.

**Surface colour data is the part of this wave the plan got wrong**, and the correction is worth
recording. The plan said `SurfacePlot` "already carries a colour source for parametric surfaces
(M45.A); this is routing an explicit array into it, not new rendering." It does not: `parametric` is
about *geometry*, and colour is derived from Z. Doing it properly meant a real `CData` grid on the
model, the palette reading it in place of the height, the autoscaled colour range spanning it rather
than Z, and a field on the surface DTO. That last needs **no format bump**: an absent key reads as
the old behaviour rather than as a missing one, which is what every document written before M70
meant. `surf(Z, C)`, `surf(X, Y, Z, C)`, `mesh`, `meshc`, `meshz`, `surfc` and `surface` read it.

Two exclusions, each for a stated reason rather than an oversight:

- **`surfl` does not take a trailing C.** Its second argument is the light source's direction, so
  reading it as colour would take a documented argument and quietly mean something else by it. The
  shared dispatcher takes a `takesColorData` flag so that this is spelled out rather than assumed.
- **`waterfall(Z, C)` is not done.** `waterfall` draws a `PatchPlot`, not a surface, so there is no
  colour grid to route into. That is a different model change and it is not in this milestone.

`meshz` needed one accommodation: it hangs a skirt off the edge, so the grid it draws is two rows
and columns larger than the `Z` it was handed, and `meshz(Z, C)` would otherwise refuse a colour
array of exactly the documented size. The skirt takes the colour of the edge it hangs from, which is
what MATLAB shows and the only reading that does not invent a value.

### Wave C — the five verbs that drew and handed back nothing

`surfl`, `waterfall`, `ribbon`, `trisurf` and `trimesh` now answer with the object they drew.
M69 fixed four of this family and recorded these five with a number attached; this closes the
remainder. Each is registered so that a bare unsuppressed call still echoes nothing — the mistake
M69 caught in `quiver` before it shipped. `ribbon` answers with a **row** of handles, one per strip,
which is both what MATLAB gives back and what a following `set(h, …)` walks.

### Wave D — the reduction dimension

`sum(A, [1 2])` — MATLAB's `vecdim` — now runs, along with `prod`, `all`, `any`, `max` and `min`.
The column-wise reduction wrapper already cut an array into slices along a named dimension; a vector
of dimensions walks that once per dimension. Each pass leaves the dimension it reduced a
**singleton** rather than dropping it, which is why the order of the vector cannot change the answer
and why the result keeps its trailing shape — `sum(reshape(1:24,2,3,4), [1 2])` is 1-by-1-by-4, as
in MATLAB. A second output is refused for a vecdim, as MATLAB refuses it and for the same reason: a
position inside a slice means nothing once several dimensions have been collapsed in turn.

Only a reduction that gives the same answer applied one dimension at a time earns this. `median` and
`mode` are deliberately absent: the median of the medians is not the median.

Three neighbours took no dimension at all and now do. `vecnorm(A, p, dim)` and `issorted(A, dim)`
were argument work. `cummax` and `cummin` were **not**: the body underneath flattened whatever it was
handed, so `cummax` of a matrix ran one sequence through the whole of it rather than one down each
column. Wrapping them fixed the column, the dimension and `'reverse'` together — and surfaced a
second thing, that MATLAB splits the cumulative family on NaN. `cumsum` keeps NaN; `cummax` steps
over it by default. That is now a property of the name rather than of the family.

**This closed a recorded divergence, so three artifacts moved in the same commit**: ADR 0069's
bullet, `docs/matlab-divergences.md` (re-harvested, **40 → 37 rows**), and section 13 of
`stess_41.m`, which turned from asserting that `sum(A, [1 2])` refuses into asserting that it
answers 136. Three rows left the index and four arrived from this ADR's own list below, so the
harvested total reads 41 rather than 37 — the closures and the new records are separate movements
and are stated separately for that reason. That is the machinery M69 built doing what it was built for. ADR 0069's closed bullets
moved into a heading the harvester deliberately does not match, so the index cannot go on naming a
divergence that no longer exists.

### Two forms that turned out not to be gaps

`sum(A, 'all')` and `max(A, [], 'all')` were on the worklist and **already worked**. They are
recorded here so a later milestone does not re-open them.

## Consequences

### What moved

| Measurement | Before | After |
|---|---|---|
| Forms accepted (of 2,422) | 949 | **1,011** |
| Forms with an `error` verdict | 526 | **464** |
| Forms unprobed | 917 | 917 |
| Commands accepting a target axes | 49 of 86 | **86 of 86** |
| Drawing verbs handing back a handle | 39 of 45 | **73 of 73** |
| Recorded divergences (index rows) | 40 | **41** |
| Tests | 4,678 | **4,706** |
| Stress scripts | 41 | **42** |

**The `accepted` movement is accounted for form by form, not asserted in aggregate.** Diffing the
old and new probe results gives **62 forms gained, 0 lost, 0 otherwise changed**, and every command
in that list is one this milestone touched: `errorbar` 5, `meshz`/`surf`/`surface`/`mesh`/`pcolor`/
`surfc` 3 each, `meshc`/`subplot` 2 each, and one apiece for 34 more.

**Property coverage did not move — 436 of 1,361 — and that is the honest outcome rather than a
disappointment.** The plan predicted it would rise, because Wave A and Wave C make objects reachable
that were not. It did not, for a reason that is checkable: the five verbs Wave C fixed draw objects
of kinds (`surface`, `patch`) that `surf` and `patch` already reached, and the coverage number is
per *kind*. No new kind became reachable, so no property did. What moved instead is the sweep beside
it, 39/45 → 73/73.

### The handle sweep grew by hand, and the automated way was wrong

The plan asked to widen the sweep past its 45 hand-written verbs. The obvious way is to drive it off
the R2021b dump's axes-target role — the same list Wave A used, so the sweep would grow with the
measurement instead of with whoever last edited it. **That was built, run, and thrown away.** The
dump describes *arguments*, and taking a target axes is a different question from returning an
object: built that way the sweep collected `axis`, `daspect`, `rlim` and 28 other **query** verbs and
would have reported all 31 as handing back no handle. Thirty-one verbs libelled by a filter
measuring the wrong thing — the exact failure the coverage documents here have been corrected six
times for.

The sweep was widened by hand instead, to 73, each row run at the CLI first. The reasoning is
written into `probe-properties.py` beside the list so the next milestone does not re-derive it.

### Deferred to M71, deliberately

- **The callback seam.** `ButtonDownFcn`, `CreateFcn`, `DeleteFcn`, `Interruptible` and `BusyAction`
  have no dispatch behind them. Storing them inertly would add roughly 280 to a coverage number
  while making `set(h, 'ButtonDownFcn', @f)` a silent no-op. **The decision is to wire them for real
  in M71 rather than store them here**, and it is recorded so M71 starts from it rather than
  re-deciding it.
- **The numeric and file leftovers** — about 55 forms across `fft`, `eig`, `lu`, `filter`, `cast`,
  `convhull`, `delaunay`, `speye`, `fgets`, `fwrite`, `textscan`, `fopen`. Independent of each other
  and of everything here.

## Recorded divergences

- **`errorbar` draws its whisker along y only.** `'horizontal'` and `'both'` are refused by name;
  there is no model for an x-direction whisker. `waterfall(Z, C)` is refused for the neighbouring
  reason — a waterfall is a patch, not a surface, and has no colour grid.
- **`sphere(n)` and `cylinder(n)` answer an empty value where MATLAB answers the coordinate grids.**
  Found while widening the handle sweep, where they do not belong: they return coordinates rather
  than a handle, so "did it hand back a handle" is the wrong question for them. The divergence is a
  different one and is recorded rather than fixed.
- **`streamline(X, Y, U, V, sx, sy)`, the documented 2-D form, is refused**: this build reads the
  grids as volumes. Found the same way.
- **An explicit surface `CData` survives a save; a surface *texture* still does not.** `TextureData`
  (M67.C) has never been serialized. M70 did not widen that gap and did not close it either, which
  is worth writing down rather than leaving to be discovered.

## What is not done

- `waterfall(Z, C)`, above.
- **`image` accepts `'XData'` as an argument name but has no `XData` property**, so a script can set
  the span at construction and cannot read it back. A property-table gap the new form exposed.
- **`ErrorBarPlot`'s two error arrays are not reachable from a script.** The asymmetric form now
  reaches the model, but nothing in the property table answers to MATLAB's `YNegativeDelta` /
  `YPositiveDelta`, so a script cannot tell `errorbar(x, y, lo, hi)` from `errorbar(x, y, lo)`. The
  unit tests assert it through the model instead. Property-table work, so M71.
- The `unprobed` bucket is **unchanged at 917**. The plan intended to teach the form prober samples
  for a slice of it; that did not happen, and the number is reported as it is rather than folded
  into either side.
- The remaining 464 `error` forms. The arity-refusal filter that scoped this milestone can be
  re-applied to them, and the 34-of-40 hand-probe rate is the best estimate available of how many
  are real.
