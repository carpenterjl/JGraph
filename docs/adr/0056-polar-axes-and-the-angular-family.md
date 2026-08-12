# ADR 0056 — Polar axes and the angular family

## Status

Accepted (M56, 2026-08-12). The second chart-type milestone of the M55–M60 arc. `.graph` stays at
version 6: the milestone adds one discriminator (`polarhistogram`), which is exactly what
[ADR 0055](0055-the-everyday-2d-charts.md) bumped the version for in advance.

## Context

The angular family was the coverage doc's most self-contained remainder: eight chart verbs
(`polarplot` `polarscatter` `polarhistogram` `polarbubblechart` `compass` `feather` `rose` `polar`),
the `polaraxes` constructor, and the nine ruler verbs M54 registered as refusals because the tick
machinery it built had no polar axes to point at. The roadmap's instruction was to promote polar to
an **axes mode, exactly the way 3-D was done** (`Is3D`, ADR 0022), and that instruction decided most
of what follows.

## Decision

### Polar is a mode an axes is in, not a kind of chart

`AxesModel.IsPolar` sits beside `Is3D` and does the same job: the axes keeps every property it had,
and the renderer draws it differently. The polar configuration is four properties on the axes —
`ThetaZeroLocation`, `ThetaDirection`, `ThetaAxisUnits`, `RAxisLocation` — and two always-constructed
rulers, `RAxis` and `ThetaAxis`, built for every axes the way `ZAxis` is. Always-constructed is what
lets a script set ticks or limits before anything is drawn and lets a saved figure keep them; it is
also why M54's tick machinery works on them unchanged, which was the whole reason the nine ruler
verbs were deferred here.

Serialization is defaulted DTO fields plus one new plot discriminator, all within v6. A pre-M56
document reads back with `IsPolar` false and null angular rulers, and the mapper builds the defaults.

### The plan's three polar plot objects became zero plus one

The roadmap proposed `PolarLinePlot`, `PolarScatterPlot` and `PolarHistogramPlot`. The first two do
not exist, and that non-existence is the milestone's central result. A new `PolarTransform` in
`JGraph.Math/Transforms` maps (θ, r) onto the pixel disc, and a polar axes hands it to its plots as
the mapper: an ordinary `LinePlot` whose x data happens to be angles in radians **is** a polar line,
without knowing it moved. `PolarAxesTests` pins the claim on a `ScatterPlot` — nothing in that class
mentions circles, so if the four compass points land right, every other plot type works on a polar
axes for the same reason.

What that buys is the argument surface for free. `polarplot` is `plot`'s own core pointed at a polar
axes, so line specs, matrix columns, repeated groups and the name/value tail all work on day one;
`polarscatter` and `polarbubblechart` are the same story over `scatter` and `bubblechart`. Three
readings are angular rather than general: `polarplot(rho)` alone spreads its samples over one full
turn, because sample numbers read as radians would wind a forty-point series round the chart six
times; `polarplot(z)` reads a complex array as angle and magnitude, where `compass(z)` reads one as
real and imaginary components — the first is given a position, the second is given a vector.

`PolarHistogramPlot` is the one new object, because a fan of filled wedges is genuinely new drawing.
Its bins are cut by `histcounts`' **own** `ChooseEdges` — called, not reimplemented — so the wedges a
script draws and the numbers it checks them against are the same arithmetic, and its counting goes
through a new shared `JGraph.Math/Binning.cs`. Angles are wrapped into one turn before counting,
since −π/2 and 3π/2 point the same way; the wrapping stops the moment a call names its own edges or
limits, because a call that says where its bins go has said which turn it means.

### compass and feather are honest arrows

Both draw through `QuiverPlot` with `AutoScale` off and `Scale = 1`, and the scaling being off is the
point: a quiver field is a sample of something and is scaled to stay readable, but these arrows *are*
the readings, and one drawn at nine tenths of its own length is a chart that lies about its numbers.
`compass` turns Cartesian components into bearing and length and anchors every arrow at the middle of
a polar chart; `feather` lays the same arrows along a sample line on square paper. `rose` draws the
pre-`polarhistogram` outline — petals, not wedges, always tiling the whole turn — and its two-output
form answers the outline itself, so `polarplot(tout, rout)` redraws it exactly.

### The θ ruler holds degrees, whatever the axes speaks

`ThetaAxisUnits` governs the numbers crossing the script boundary and nothing else: the ruler's range
and ticks are stored in degrees always, because a ruler that changed units under its own ticks could
not be compared with itself. `thetalim`, `thetaticks` and their readers convert at the verb boundary,
so a turn set as `[0 pi]` under radians reads back `[0 180]` the moment the axes switches to degrees —
same ruler, same turn, two spellings.

r needs no such care. It is an ordinary scale, so `rticks`/`rticklabels`/`rtickformat`/`rtickangle`/
`rlim` are M54's Cartesian machinery pointed at `RAxis`, inheriting `'auto'`/`'manual'`, the M51
fit-before-read rule and the label overrides with no new reading logic. The one render change r asked
for was an argument: the r tick labels never passed a rotation to `DrawText`, which is why
`rtickangle` had nowhere to show until wave E.

Autoscale is asymmetric on purpose. The fitted r range always reaches the middle —
`min(0, fitted.Min)` with no padding below — because a circle whose centre is not zero is a chart
that lies about proportion. θ is never fitted at all: the circle is the chart, so `thetalim('auto')`
restores the whole turn rather than refitting, `'manual'` is an explicit no-op, and a turn asked for
beyond 360° is trimmed to one, since the frame cannot wind over itself.

### The spokes live in one place, and the wedge folds

The renderer's spoke arithmetic (a spoke every 30°, kept to the visible turn, the duplicate dropped
where a full circle's two ends meet) began as private helpers in `FigureRenderer`. Wave E moved it
into a shared `JGraph.Math/Ticks/PolarSpokes.cs` that both the renderer and `thetaticks` read
through — the same one-place rule `JgsRulerTicks` gives the Cartesian verbs, and what makes
`thetaticks` structurally unable to report a spoke the chart is not drawing.

`thetalim([0 180])` cuts the chart to a wedge, and the clipping folds before it gates: `DataToPixel`
folds an angle by whole turns from the visible turn's start, so a bearing of 405° lands on the 45°
spoke and −90° is the 270° it names — gone on a half-circle chart, the way a radius past the rim is
gone. `PixelToData` folds into a turn starting at the wedge's own beginning, so a chart cut to
[−90°, 90°] reads −45° at that spoke rather than the 315° of another atan2 branch.

### Everything else came free, and one test says so per claim

The angular verbs peel a leading axes handle through `PeelAxes`, the ruler verbs through M55's
`PeelRuler`, so `rlim(pax, …)` and `thetaticks(pax.ThetaAxis, …)` aim without moving `gca`. The verbs
and the property spellings (`RLim`, `ThetaTick`, `ThetaDir`, …) share stores, so `get(pax, 'RTick')`
and `rticks` cannot come to disagree. `polaraxes('ThetaDirection', 'clockwise')` applies its tail
through the property table `set` uses, so every axes property is settable there the day it is added.
A polar verb aimed at a Cartesian axes, and a polar ruler verb on one, refuse by naming the verb and
saying what to make first.

## Consequences

**383 of 514 builtins** (up 1: `polaraxes` is the one name here MATLAB documents as kind *builtin*)
and **163 of 266 documented graphics functions** (up 17: the eight chart verbs and the nine rulers).
The chart-types family drops from 23 to 15; the properties-and-rulers family M54 emptied to 23 is
now 14, all waiting on stated reasons. Across every callable kind the checklist reports **728 of
2,027**, and for the first time since M54 the tool's number and the coverage file's agree — the
nine-name gap the registered-but-refusing rulers opened is closed, because they draw now.

**Divergences**, recorded in the coverage doc's graphics section:

- **`get(h, 'Type')` on a `compass` or `feather` answers `'quiver'`**, the M55 rule again: a chart
  built out of another chart answers as what it is made of.
- **`polar` draws on a polar axes.** MATLAB's legacy `polar` draws its own grid on a hidden Cartesian
  axes, which is why its ticks cannot be spoken to; here it is `polarplot` under the old name, so the
  ruler verbs work on its chart — strictly an improvement, but an observable one.
- **`rtickformat`/`thetatickformat` queries answer the .NET format spelling**, the same divergence
  `xtickformat` has carried since M54, for the same reason.
- **Wheel zoom and pan do not speak polar yet.** The interaction layer moves the Cartesian ranges,
  which a polar mapping ignores, so they are inert on a circle rather than wrong; `rlim` is the
  scripted zoom. Teaching the wheel to scale `rlim` is recorded follow-up work, not silently absent.

**Found on the way and left where it was:** `LineSpec`'s `'g'` maps to `Colors.Green`
(`[0, 0.502, 0]`) where MATLAB's `'g'` is `[0 1 0]`. It has been so since M2 and is project-wide —
every dialect, every verb — so an angular milestone quietly changing what green means everywhere
would have been the wrong place; recorded here, to be decided once, deliberately.

**Follow-ups recorded, not done:** the legacy Smith chart still draws its own `PolarGrid` rather
than sitting on the new polar axes — migrating it was excluded from M56 scope by the roadmap and
stays excluded until it is its own decision; and `histcounts` still owns private copies of the
binning helpers (`BinOf`, `Uniform`, `Spanning`, `Normalized`) while only `PolarHistogramPlot`
routes through the shared `Binning` — the consolidation is mechanical and belongs to the next
milestone that touches `histcounts`.

**Verification.** 0 build warnings; **4,039 unit tests** green (up 80 across the milestone), among
them the transform pinned in isolation — compass orientation, wedge folding both directions,
round-trips — and the verb suites with every documented argument form. `tools/run-stress.ps1` green
over 28 scripts including the new `stess_28.m`: eighteen sections, each argument form exercised at
least twice in different shapes, `polarhistogram` counts checked against `histcounts` on the same
data, a polar figure with every angular plot type sent through save/load, and the negative section
requiring misspelt options and impossible calls to be refused by name.

**Left to the user**, since batch structurally cannot exercise it: each angular chart in a live
figure window and its inspector and plot-browser rows; a wedge under `thetalim` on screen; the
compass arrowheads at window sizes batch never renders; and the inertness of wheel zoom and pan on a
polar chart, pending the follow-up above.
