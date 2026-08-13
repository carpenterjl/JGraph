# ADR 0057 — The tier-two chart types

## Status

Accepted (M57, 2026-08-12). The third chart-type milestone of the M55–M60 arc. `.graph` stays at
version 6: the milestone adds four plot discriminators (`binscatter`, `stem3d`, `bar3d`, `pie3d`),
which is what [ADR 0055](0055-the-everyday-2d-charts.md) bumped the version for in advance.

## Context

After M55 took the everyday 2-D charts and M56 the angular family, the coverage doc's "chart types
with no plot object" family still held fifteen names. This milestone takes thirteen of them and
excludes two with reasons, which empties the family: `bar3` `bar3h` `stem3` `pie3` `binscatter`
`bubblechart3` `swarmchart` `swarmchart3` `stackedplot` `scatterhistogram` `voronoi` `triplot`
`tetramesh`, plus the numeric `voronoin` that the drawing verbs needed anyway.

They have almost nothing in common as charts, and that is the interesting part: thirteen names in
one milestone forced the question of how much of a chart is actually new each time. The answer, wave
after wave, was *less than the name suggests*.

## Decision

### Four new plot objects out of thirteen verbs

`Stem3DPlot`, `Bar3DPlot`, `Pie3DPlot` and `BinScatterPlot` are the only new objects. The other nine
verbs draw with objects that already existed:

- **`swarmchart`, `swarmchart3`, `bubblechart3`** are properties, not objects. A swarm chart is a
  scatter whose points are nudged aside where they would overlap, so `XJitter`/`YJitter`/`ZJitter`
  and their widths went onto `ScatterPlot` and `Scatter3DPlot` — which means
  `scatter(x, y, 'XJitter', 'density')` works, exactly as it does in MATLAB, and `bubblechart3` is
  `scatter3` with its sizes read as values rather than as areas.
- **`voronoi`, `triplot`** are line plots with gaps in them, and **`tetramesh`** is a patch. A
  diagram and a triangulation are sets of straight segments; the wave's work is the arithmetic that
  turns a table of vertex numbers into those segments, not a way to draw them.
- **`stackedplot`, `scatterhistogram`** are compositions that draw nothing of their own — a column
  of ordinary axes, and a scatter with a marginal along each edge.

The M54 claim held again: not one of the four new objects wrote a line of property, inspector or
`findobj` code, because the property table is reflection over the model's own browsable properties.
What each new object *did* need was a data alias (`XData` and its siblings), which is the same small
tax M55's five paid.

### The spread is a drawing offset, never a change to the reading

`JGraph.Math/Swarm.cs` answers one question — *how far sideways* — and the reading itself is left
alone. That is what lets `get(h, 'XData')` on a swarm chart answer the x it was given, which is what
a script that drew the swarm to *look* at a distribution and then went on to compute with it
expects. It is also the only reading under which `swarmchart` can be a property on `scatter`: if the
offset were baked into the data, the two verbs would be different charts rather than one chart with
a setting.

The density spread groups points by the coordinate being spread and fans each group by how crowded
the *other* coordinate is, so two columns of a swarm are laid out separately rather than as one
crowd. `Rand` and `Randn` come off the M52 seeded RNG service, so a swarm under `rng(k)` is the same
swarm twice.

### A Voronoi diagram is the dual of a triangulation, and both come from one kernel

`JGraph.Math/Geometry/` gained Bowyer–Watson and the dual: every Delaunay triangle contributes its
circumcentre, every shared edge a segment between two of them, every hull edge a ray running outward
forever. `delaunay` already existed from M39 and now shares the kernel; `voronoin` and the drawing
verbs fall out of it.

Two divergences are recorded rather than papered over. Circumcentres of cocircular points coincide —
four points on a square give two triangles with one centre — and the diagram merges them, because
the duplicate is an artefact of which diagonal the triangulation happened to pick, not a feature of
the diagram; the merge tolerance scales with the point set's span. And MATLAB computes this through
Qhull, whose joggling and merging make different calls on degenerate input: the shape of the answer
agrees, vertex order and exact ties may not. `voronoin` is plane-only, which is stated in its
refusal rather than left to be discovered.

`voronoi` and `triplot` draw when asked for one output and answer with the geometry when asked for
two — the rule `rose` already followed in M56, and the reason each verb is written as "work out the
segments, then draw them" with a function for each half.

### One pie, two drawings

`PiePlot` lost its wedge arithmetic to a shared `PieGeometry`, and `Pie3DPlot` uses the same. MATLAB's
normalization rule — a total above one is normalized, a total at or below it is taken as the shares
themselves, which is the only way to ask for a pie with a piece missing — now has one implementation,
so a `pie3` and a `pie` of the same numbers divide the circle identically and only the drawing
differs.

Two `pie3` divergences, both deliberate. MATLAB builds a raised pie out of a surface, two patches and
a text object per wedge and hands back all of them; this is **one object and one handle**, because
the faces are painted back to front and a depth sort is only right when it can see the whole chart.
And the sides are shaded by a fixed step from the lid rather than lit: there is no light model in
this pipeline, and a flat-coloured solid reads as a flat shape. `Bar3DPlot` sorts its cuboids the
same way, through M44's batched-triangle path.

### binscatter bins once and keeps its bins

MATLAB rebins as the axes are zoomed, so the picture sharpens as you go in. Here the bins are worked
out once from the data and stay put. That is a divergence, and it buys two things worth more than the
sharpening: `XBinEdges` is answerable at any moment rather than only between redraws, and a saved
figure is identical to the one that was saved. MATLAB does not document how it chooses a bin count,
so an unasked-for one is the square-root choice, shared with `histcounts` through `Binning`.

### The compositions are linked, not merely equal

`stackedplot`'s panels go through `LinkAxes` along x, and each `scatterhistogram` marginal is linked
to the single ruler it describes. Equal-today limits would drift apart the moment anyone panned one
panel, and readings taken over one run are only comparable while every panel is looking at the same
stretch of it; a marginal still describing the whole sample while the scatter shows part of it would
be answering a different question from the one on screen. Each stacked panel keeps its own y — that
is what "stacked" means — and only the bottom one prints x tick labels.

MATLAB makes both of these a single chart-container object, so `s.LineWidth = 2` works there. Here
they are real axes holding real plots: the parts answer to `get`/`set` individually, every axes verb
works on them, and since `set` takes a vector of handles the one-line form is
`set(s, 'LineWidth', 2)` over what the verb hands back. That is the recorded divergence, and it is
the same trade M55 made for `heatmap`.

### Excluded, with reasons

**`wordcloud`** and **`parallelplot`** are chart containers whose whole content is a layout
algorithm, with no axes a reader can measure against — the same reading that excluded `bubblecloud`
in M55. A word cloud sizes text by frequency, which a `bar` of the same counts says more precisely; a
parallel-coordinates plot of engineering data is `plotmatrix` or a stacked plot with linked axes,
both of which this build now has.

### What the stress script found: a lone number could not be subscripted

`stess_29.m` refused to run its first draft, and the reason had nothing to do with charts. A verb that
drew one thing handed back one handle, and `h(1)` — the spelling that works the moment it draws
several — answered *"Cannot call a number; it is not a function."* MATLAB has no such distinction: a
scalar **is** a one-by-one array, so `x(1)`, `x(1,1)`, `x([1 1])` and `x(:)` all read it.

Fixed rather than recorded, because it is the M52 scalar-promotion decision one level down and the
direction is the same: error becomes an answer, so no script can be reading differently now. A
Number or Bool reached by a subscript is wrapped as a one-element array, stamped with the class it
was in so `class(x(1))` still answers `'uint8'`, and handed to the ordinary indexing path — which is
what makes the JGS spelling (`x(0)`, `x[0]`) come out right for free rather than needing its own
rule. Reaching past the one element is still out of bounds. One M12-era test pinned the old refusal
and was rewritten: what cannot be subscripted is now a value with no elements to read, not a number.

The type names are the other thing the script settled. `bar3` and `pie3` answer `'surface'` and
`stem3` answers `'stem'`, because MATLAB builds those charts out of surfaces and that is what its own
`get` reports — so `findobj(gcf, 'Type', 'bar3')` correctly finds nothing in either build.

## Consequences

The coverage doc's chart-type family is empty but for those two. `docs/matlab-builtin-coverage.md`
moves from **163 to 176 of 266 documented graphics functions**, and the across-every-kind total from
**728 to 742 of 2,027** — thirteen graphics names plus `voronoin`, which MATLAB documents under
`matlab/polyfun` rather than under any graphics folder and which therefore moves neither table. The
builtin table is unchanged: not one of the fourteen is documented as kind *builtin*.

What is left of the graphics remainder is **90 names in four families**: the sixteen function
plotters (M58), the twenty-two volume names (M59), thirty-six figure-tooling names (M60), fourteen
properties and legacy appearance verbs, and the two chart containers excluded above.

`stess_29.m` is the live check, and it is where the milestone's claims are asserted against each
other rather than in isolation: a Voronoi diagram's edge count for a known four-point square, a
swarm's spread bounded by its own width while its `XData` still answers the given x, `bar3`'s bar
count through `findobj`, `binscatter`'s counts summing to the sample size, and a figure holding every
new type through a save and a load.

## Live checks for the user

Batch cannot see any of these, so they are listed here rather than claimed:

- A raised pie and a 3-D bar chart under rotation — whether the depth sort holds as the camera turns,
  and whether the fixed-step side shading still reads as a solid from behind.
- `binscatter` on a large sample: whether the transparent empty bins and the automatic colorbar
  placement work at real sizes.
- A swarm chart live — whether the density spread stays legible when the window is resized, since the
  spread is worked out in data units and the window is not.
- A stacked plot panned by hand: whether it moves as one chart across its panels.
- `scatterhistogram`'s marginals at the sizes the four-by-four layout gives them.
