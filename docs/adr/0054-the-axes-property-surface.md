# ADR 0054 — The axes property surface and handle graphics

## Status

Accepted (M54, 2026-08-10). Completes [ADR 0051](0051-handles-on-figure-objects.md), which made a
handle an ordinary number and gave the dot a fixed set of properties per kind of object. This is the
milestone that replaced "a fixed set per kind" with a table, and it is the first of the graphics
milestones M55–M60 because everything they add inherits that table.

## Context

M51 gave a script handles. What it did not give was a way to ask an object what it has: `p.Color`
worked because a switch statement listed `Color` for a line, and every new kind of object meant
another arm of another switch. M53 proved the shape of that problem by having to widen one the moment
its statistics verbs drew something that was not a line. Meanwhile MATLAB's own answer — `get`, `set`
and `findobj`, a name-driven interface over every object there is — was recorded in
`matlab-builtin-coverage.md` as "a natural next step rather than a barrier" and had been sitting there
since M45.

The sequencing argument is what put this first among the graphics milestones. M55 through M60 add
somewhere near a hundred plot objects and figure verbs. If each one has to be hand-registered into a
property switch, a type-name table and an inspector list, then every one of those milestones carries a
tax and every one of them can forget to pay it. If instead the property surface reads the model, they
arrive complete by existing. That is worth doing before the objects, not after.

Two smaller things rode along because they belong to the same surface. The **tick and ruler verbs**
(`xticks`, `xticklabels`, `xtickangle`, `xtickformat` and their y and z counterparts) are the axes'
own properties spelled as commands, and needed a model field that did not exist: an axis could not be
told where to put its ticks. And **`yyaxis`** is the one axes feature that changes what every other
axes verb means, so leaving it for later would have meant revisiting all of them.

## Decision

### The property table is reflection over the model, plus a curated alias layer

`JgsGraphicsProperties` builds a per-CLR-type table on first use. The lower layer is reflection over
the model's `[Browsable]` properties — **the same metadata the property inspector reads**. The upper
layer is a small hand-written alias set for the places MATLAB names something differently or splits it
apart: `XLim` is the primary X ruler's range, `Position` is the axes' normalized bounds, `Color` on an
axes is its background, `Parent` and `Children` mint handles, a colour reads as a 1×3 row, a boolean
reads as `'on'`/`'off'`.

The consequence is the point of the whole milestone, and it is enforced rather than hoped for: **a
test asserts that every public browsable property of every model type is reachable through `get`.** A
chart type added in M55 that forgets to think about handles still gets its whole property surface, and
one that somehow does not will fail the suite rather than ship half-visible.

`JgsHandleEntry` lost its `kind` field in the same change. The object's CLR type already answers what
it is; `TypeNameOf` reads it and strips a `Plot`/`Model`/`Annotation` suffix, with a table of MATLAB
spellings in front for the names MATLAB gives differently. A sync test pins those names to the DTO
discriminators, so `findobj(gcf, 'Type', 'line')` cannot drift from what a save writes.

### `copyobj` copies through serialization

A clone needs to reproduce everything a plot object holds, which is precisely what the `.graph` mapper
already knows how to do. `copyobj` therefore round-trips through the DTO layer instead of growing
clone code of its own: no second definition of "everything this object is" to keep in step, and a
property that serialization forgets is a bug in one place rather than two. The cost is a new
JGraph.Scripting → JGraph.Serialization reference, which is a legal direction and now used once.

### Handles are dropped when their figure is

The registry holds model objects by reference. Closing a window retires the figure, but the entries
would have kept the whole subtree alive for the life of the session — a leak that only shows up in the
long sessions the console makes possible. `DropUnreachable` walks the live figures and forgets every
handle not reachable from one. A dropped handle then fails with "it may belong to a figure that has
since been cleared", which is the true answer.

### Manual ticks wrap the generator rather than replacing it

`ManualTickGenerator` puts an axis' own values and labels in front of whatever generator the scale
already has, and either half can be manual alone: naming positions leaves them labelled with their own
numbers, naming labels leaves the generator to choose where they go. Wrapping rather than replacing is
what keeps a logarithmic axis logarithmic when a script only wanted its labels renamed — a manual
generator that owned the whole job would have had to reimplement every scale to avoid regressing it.

The model fields (`TickPositions`, `TickLabelOverrides`, `TickLabelAngle`, `TickLabelFormat`) are
defaulted DTO fields, so **`.graph` stays at v5** — the ADR 0048 precedent. So do `AxesModel.Roll` and
`SurfacePlot.FaceColor`, added in wave F.

### `yyaxis` partitions the autoscale pass, and `xline`/`yline` opt out of it

`AxesModel` already carried a `YAxes` collection with a per-plot `YAxisIndex`; what it lacked was an
active index and a bounds pass that respected the partition. Both arrived here: `GetYAxisFor(plot)`
routes a plot to its ruler, and the fit pass computes each ruler's range from its own series only.
Everything that reads or writes "the y axis" — `ylabel`, `ylim`, `yticks`, the tick verbs — goes
through one helper that asks for the active ruler, so `yyaxis right` changed all of them at once.

`ConstantLinePlot` is the milestone's one new plot object, and the interesting thing about it is that
it reports **no data extent at all**. A threshold at 1000 drawn beside a series that reaches 3 must not
flatten the series; MATLAB agrees, and the same choice makes the line span the visible axes rather than
stopping at coordinates that could fall short.

### The camera verbs land on the camera this build actually has

`viewmtx` and `makehgtform` are pure arithmetic and went into `JGraph.Math` as `CameraMatrices`, where
they are right regardless of what gets drawn — `viewmtx(az, el)` deliberately shares its rotation rows
with `Projection3D`, so a script that projects points by hand lands where the figure drew them.

The five view-moving verbs land on the only state an orthographic auto-fitting camera has: two angles,
a roll, and the limits. `camroll` is the one real rendering change — the roll turns the screen-right and
screen-up rows *before* the corner-fit loop, which is what keeps a rolled axes inside its plot
rectangle. `camdolly`, `campan` and `camlookat` are limit changes. What that camera cannot express is
**refused with the reason** rather than accepted and ignored: `camdolly`'s `'fixtarget'` and
`'pixels'`, `campan`'s direction argument. `camproj` is the exception — it accepts `'perspective'`,
does nothing, and always reads back `'orthographic'`, because that one is asked reflexively.

### `hidden` became a property rather than a renderer branch

MATLAB's `hidden on` removes the lines a mesh's own faces would cover. A JGraph mesh draws no faces, so
there was nothing to remove. Rather than thread a background colour through the 3-D render path for one
legacy verb, `SurfacePlot` gained `FaceColor` — a real MATLAB property in its own right — and `hidden
on` sets the style and paints the faces the axes background. The recorded divergence is that **`hidden`
defaults off** where MATLAB defaults it on, because every mesh in this build has been transparent since
M20b and flipping the default would silently change existing figures.

## Consequences

**382 of 514 documented builtins** and **133 of 264 documented graphics functions**, the latter up 52.
The graphics denominator moved too: 263 was one short, because `slice` was counted in both the
implemented table and the missing list while `close` and `linkaxes` were in neither.
`docs/matlab-builtin-coverage.md` carries the reconciliation.

The remaining graphics surface is now **131 names in five families**, each waiting on a named
milestone rather than on nothing: 33 chart types (M55–M57), 16 function plotters (M58), 22 volume names
(M59), 36 figure-tooling names (M60), and 24 properties-and-legacy names, of which 9 are the polar
rulers M56 fills, 12 are excluded with reasons, and the rest are placed.

**The checklist tool now overstates, for the first time, and the doc subtracts by hand.** M54 registers
the nine polar ruler verbs as names that refuse with a reason — a better answer than "undefined
function", and it reserves the name — but `build-checklist.py` equates catalogued with implemented and
ticks them. Any later milestone that registers a placeholder owes the same subtraction and a line
saying so.

**Divergences**, all recorded in the coverage doc's graphics section: `findobj` matches properties,
`'flat'` and `'-depth'` only and refuses the logical operator words by name; `gobjects` fills with
handle 0; `ax.YAxis` answers the active ruler rather than both, and the active side is not serialized;
gridlines follow the left ruler and the side rulers are suppressed on 3-D axes; `xtickformat` queries
answer in this build's format spelling; tick label angles are 2-D only; a single-output `contour(…)`
returns the handle, and contour labels are 2-D with `'manual'` refused; a cell-array title joins to one
line and the titling verbs return nothing; `texlabel` covers the documented subset; `camproj` always
reports orthographic; `orient` always reports portrait and `opengl` is an accepted no-op; `colorcube`'s
rows are our construction to the documented description; `cmpermute`'s shuffle comes off the session
generator, so `rng(seed)` repeats it.

**Incidental fixes the milestone's own probes forced**, each a documented form that used to error:
`gcf` did not auto-call on its bare name; `xlim`/`ylim`/`zlim` had no query, `'auto'`, `'manual'` or
leading-axes forms; the whole titling family took no `'Name', value` text properties; contours had no
`LevelList`; bare `colormap` returned the function rather than the current map; every `Color` option
now accepts `#RRGGBB` and `#RGB`; and `LinearTickGenerator.DecimalsFor` was one decimal short for
2.5×10^k steps, which had been rounding tick labels together since M6.

**Verification.** 3,798 unit tests, `tools/run-stress.ps1` green over 26 scripts including the new
`stess_26.m`, and 0 build warnings. `stess_26.m` is where the milestone's own claims are checked as a
script rather than as a suite — the tick round trips, `get` after `set`, `findobj` counting across two
figures, per-side limits under `yyaxis`, `viewmtx` values, and `copyobj` independence — because M46's
lesson is that a unit suite feeds each function the shape it expects and only a real script does not.

**And it earned its place again: writing it found three defects the twelve new unit suites did not.**
All three are fixed here, with tests, and all three have the same shape — a form nobody had written by
hand because each suite tested the verb it was about, in the spelling that verb prefers.

- **`findobj(gcf, 'Color', [1 0 0])` matched nothing.** The filter compared with `JgsValue.AreEqual`,
  which is the identity comparison handles need — two arrays are equal only when they are the *same*
  array. Half the properties worth searching on are colours, and a colour is a 1×3 row. It now compares
  element by element, through the same `DeepEquals` that `isequal` uses.
- **`delete(h)` said a handle was not a path.** MATLAB spells two verbs with one name and only the file
  one was here. A handle is a number and a path is a string, so the dispatch is unambiguous; the
  graphics half removes a plot from its axes, an axes from its figure, and closes a figure. A number
  that names no object still falls through to the file complaint. All-or-nothing across a vector: a
  mixed list is refused rather than half-applied.
- **`contour(Z)` errored** — the shortest form of the verb, and the one every script reaches for. It
  insisted on x and y. It now indexes the grid by row and column the way `surf(Z)` has since M20b, and
  a two-argument call is `contour(Z, levels)`, which is the only thing two arguments can be.

`tempdir` and `tempname` were also missing, found the same way, and added: a script that wants to write
something it does not intend to keep had nowhere to put it but the user's own folder.

**Left to the user**, since batch cannot exercise it: `camroll` and `hidden on` in a live figure
window, `whitebg`/`colordef` against the app theme, the `yyaxis` side rulers under pan and zoom, and the
inspector rows for the new axes and surface properties.
