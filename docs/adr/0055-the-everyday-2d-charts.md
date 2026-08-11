# ADR 0055 — The everyday 2-D charts

## Status

Accepted (M55, 2026-08-11). The first of the chart-type milestones, and the one that spends
[ADR 0054](0054-the-axes-property-surface.md)'s property table rather than adding to it. `.graph`
goes to **version 6** here, once for the whole M55–M60 arc.

## Context

`matlab-builtin-coverage.md` has called the chart types "the largest honest remainder" since M45:
thirty-three names, each of which reads as a plot object, a renderer branch, a serialization arm and
an inspector row. That is the shape M45 paid for `plot3` and `patch`, and paying it thirty-three
more times was the reason the graphics arc kept being deferred.

M54 was sequenced first to make that price smaller, and it made a specific promise: because the
property surface is reflection over the model's own browsable properties, a chart type added later
would join `get`, `set`, `findobj` and the inspector **by being written**. M55 is the first
milestone that could test the promise, on eleven verbs at once.

## Decision

### Only five of the eleven are new plot objects

The milestone's cheapest decision was to keep asking whether a verb is a chart or a *setting*.

- **`barh` is `bar` with `Horizontal` on.** A horizontal bar chart has every property a vertical one
  has and draws by the same rules with two coordinates exchanged. Two object types would have been
  two copies of one thing, differing in a name.
- **`stairs` is a `LinePlot` with `Steps = StepMode.Post`.** It goes through `plot`'s own argument
  path, which is what gives it line specs, matrix columns and the whole name/value surface for free.
- **`pareto`, `plotmatrix` and `plotyy` draw nothing of their own.** They arrange plots and axes
  that already exist, so they added no model, no DTO, no rendering and no serialization at all.

That leaves `AreaPlot`, `PiePlot`, `HeatmapPlot`, `BoxChartPlot` and the bubble path on
`ScatterPlot` — and the last of those is a property too, not an object.

The **recorded cost** is that a chart made of another chart answers as what it is made of:
`get(h, 'Type')` reads `'bar'` for a `barh` and `'scatter'` for a `bubblechart`, where MATLAB mints a
distinct type for each. A script that wants to know can read `Horizontal` or `SizeData`, which is the
thing it actually cares about.

### `.graph` goes to v6 once, for the whole arc

Every milestone from here to M60 adds plot kinds. A reader that does not know a discriminator cannot
draw it whichever version the document claims, so the honest signal is "this document may contain
charts your build has never heard of" — and that is one signal, not six. Adding a *defaulted field*
to an existing DTO still needs no bump (the ADR 0048 practice, which M54 relied on); adding a new
**discriminator** is what v6 marks. A v5 document loads under v6 unchanged, and a v6 document handed
to an older build hits the existing newer-version rejection rather than loading wrong.

### M54's promise held, and it is the milestone's main result

Ten chart types were written across waves A–G. **None of them wrote a line of property code, a line
of inspector code, or a line of `findobj` code**, and `stess_27.m` checks that by asking a figure
holding all of them to find each by name and change each by name. The guardrail test M54 left — every
public browsable model property is reachable through `get` — is what turns "we did not have to" into
"we could not have forgotten to".

The one thing a new chart does have to do by hand is say what it is called, because a MATLAB spelling
is not always the CLR type name (`'stair'`, not `'stairs'`). The sync test that pins those names to
the DTO discriminators catches a chart that forgets.

### The charts that needed a decision of their own

**`pie` sits on round, frameless axes.** Nothing in a pie is measured along an axis, so the axes it
draws on has equal aspect, no box and no rulers — the M7 equal-aspect machinery, reused.

**`heatmap` is an axes with a plot on it**, not MATLAB's standalone chart container. The recorded
divergence buys a chart that sits in a `subplot` and answers every ordinary axes verb. Its title and
axis labels are properties on the plot, which is where MATLAB's chart keeps them too.
`MeasureText` on `IRenderContext` was flagged in the roadmap as a possible new render-context member
for the cell text; it turned out to have existed since M44, so no seam changed.

**`boxchart`'s quartiles live in `JGraph.Math/Quartiles.cs`**, not in the plot object, and
`stess_27.m` checks the drawn median against `median()` — a box chart that computes its own quartiles
slightly differently from the statistics verbs in the same build would be a figure that disagrees
with the numbers beside it.

**`bubblelim` and `bubblesize` are figure-level state**, as they are in MATLAB: the mapping from a
data range onto a range of drawn diameters belongs to the picture, not to one series.

### `pareto` pins both rulers, and ruler handles became first-class

A pareto chart is bars in their own units and a cumulative curve in percent, on M54's second y ruler.
Both rulers are **pinned** — left to the total, right to 100 — rather than fitted, because the top of
one has to mean the same place on the page as the top of the other, or the chart lies about how much
of the problem the first few bars account for.

`plotyy` then forced the milestone's one real design question. MATLAB returns `AX` as two overlaid
axes; this build has one axes with two rulers. Answering with the same axes handle twice would have
made `ylabel(AX(2), 'right')` label the *left* side — quietly wrong. So **`AX` holds the two ruler
handles**, and a ruler handle now works wherever an axes handle works:

- `PeelAxes` resolves a ruler handle to its owning axes, so all twenty-nine of its call sites accept
  one with no change;
- a new `PeelRuler` additionally hands back the ruler, and only the verbs that speak about a single
  ruler consume it — `ylim`, `yticks`, `ylabel` aim at the side they were handed instead of whichever
  side `yyaxis` last made active;
- the ruler property table answers to the axes-shaped spellings `YLim`, `YColor`, `YLabel` and
  `YScale`, each **guarded by orientation**: `get(AX(2), 'XLim')` names the mistake rather than
  honouring it.

**This reverses a decision M54 recorded deliberately.** M54 refused `xticks(gca.XAxis)` with "aims at
an axes, but got a handle to a numericruler", because there was no way to honour it. There is now, so
it reads that ruler's ticks. The guard is still there and still fires for a handle that is neither an
axes nor a ruler — `xticks(lineHandle)` would otherwise put a tick at a million and a half, since a
handle is an ordinary number.

### `plotmatrix` answers `BigAx` with handle 0

MATLAB's `BigAx` is an invisible axes that exists only so a title can hang on it. An invisible axes
here draws nothing at all, **its title included**, so minting one would have silently swallowed every
`title(BigAx, …)`. The slot answers with 0 and `sgtitle` is what writes over the grid: a refusal a
script can see beats a title that vanishes. The verb also lays out its own subplots, so it clears the
figure and refuses a leading axes handle by saying so. Its diagonal carries each column's own
distribution, because a column scattered against itself draws a straight line and says nothing.

## Consequences

**146 of 266 documented graphics functions**, up 13, and the builtin table is unmoved at 382 because
MATLAB documents none of these as kind *builtin*. The remaining graphics surface is **120 names in
five families**. The denominator moved for the second time: `bubblesize` and `bubblelim` were
documented graphics functions this file had in neither list, found because implementing them moved
the checklist tool by two names the doc could not account for — the third time that same gap shape
has surfaced, and always the same way, a name absent from a list rather than marked missing in it.

**`bubblecloud` is excluded**, settling a pledge this file made to M55. It is not a bubble chart with
a different legend: it packs labelled circles against each other with no axes, no scales and no
coordinates, which puts it with `wordcloud` and `parallelplot` — chart containers whose whole content
is a layout algorithm. `bubblechart` against a categorical x carries the same comparison on axes a
reader can measure.

**Divergences**, all in the coverage doc's graphics section: a chart built out of another answers as
what it is made of; `plotyy`'s `AX` holds rulers; `plotmatrix`'s `BigAx` is 0, and the verb clears
the figure and cannot be aimed at an axes; a `heatmap` is an axes plus a plot rather than a chart
container; `pie`'s `Explode` reads back as the distance a slice is pulled out rather than the flag
that asked for it; `plotyy` leaves the right-hand side active, as MATLAB leaves its second axes
current.

**And the script wave earned its place again — this time on a verb nobody in this milestone touched.**
Writing `stess_27.m` found that **`plot(y)`, `stem(y)` and `stairs(y)` numbered their samples from 0
while `bar(y)` and `area(y)` numbered them from 1**, in the same axes, in the same figure. The cause
is the shape M52's `find(X, k)` had: `bar` and `area` build their own coordinates, while `plot` lets
the `JG` facade choose — and the facade cannot know which script asked, so its implicit x has been
0-based since M12, which is right for JGS and one place to the left for a `.m` file. A new
`ImplicitX(dialect, n)` puts the choice with the caller that knows the dialect. The JGS surface is
untouched and has a test saying so; the frozen scripts and the example workspace were audited for
reliance on the old numbering before the change, exactly as M52 audited `find`, and none had any.

**Recorded, not fixed:** `semilogx(y)`, `semilogy(y)` and `loglog(y)` still refuse the
one-argument form that `plot` accepts. Found by the same probe; it is an argument-surface gap in
three verbs rather than anything about charts, and it belongs with the next milestone that touches
them.

**Verification.** 0 build warnings, **3,959 unit tests**, and `tools/run-stress.ps1` green over 27
scripts including the new `stess_27.m` — twenty-three sections, each argument form exercised at least
twice in different shapes, ending in the negative section that requires a misspelt option word to be
reported by name. Section 20 saves a figure holding every new chart type and loads it back; section
21 does the same for a pareto, because which y ruler a series is measured against is the one fact
about a two-scale arrangement a file could lose, and a grep while writing that test found **no
serialization coverage of `YAxisIndex` at all**. It has some now.

**Left to the user**, since batch structurally cannot exercise it: every new chart in a live figure
window and in the inspector and plot browser rows; the pie's round frameless axes and the heatmap's
cell text under resize; the pareto's two rulers and the plotyy tint under pan and zoom; and the
bubble legend's placement against a figure it does not own.
