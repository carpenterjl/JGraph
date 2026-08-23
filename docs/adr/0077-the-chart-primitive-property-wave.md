# 0077 — The chart primitives' property surface

Date: 2026-08-23 · Milestone: M77 · Status: accepted

## Context

M73–M75 closed the axes and the figure. What was left as the largest block in the property table was
the thing those two contain: the charts themselves. A scatter answered to 33 of MATLAB's 74 names, a
histogram to 21 of 46, a line to 26 of 52, a bar to 29 of 44, a stem to 26 of 43 — and beside them,
sharing every seam, errorbar 26/52, stair 26/38, area 28/39 and bubblechart 32/73.

Reconnaissance against the probe CSV found the 188 missing names had four shapes, and the wave is
organized around them rather than around the kinds:

- **A block every plot object shares** — its seat in the series cycle, whether it takes a legend
  row, what a data tip on it says, whether it is clipped, and where its numbers came from. About
  seventy names served once, from one place.
- **Two kinds with no alias block at all.** `HistogramPlot` kept its bins behind `[Browsable(false)]`
  and its own names (`BinCount`, `FillColor`, `EdgeWidth`), so 25 of its 46 names were unreachable;
  `ErrorBarPlot` held its two reaches in `readonly` fields set once by a constructor.
- **A per-kind block that needed model and renderer work**: binning that can be re-done, a bar
  layout that can be changed, a baseline that is drawn rather than merely stored, sideways error
  bars, a transparency per scatter point.
- **A block with nothing under it**: the geographic (`Latitude*`, `Longitude*`) and table-sourced
  (`*Variable`, `SourceTable`) families, 15 names on scatter and bubblechart and 4 on line.

Three divergences were also found that the probe *cannot* see, because in each the name answers:
`Marker` read back `"circle"` rather than `'o'` on stem and errorbar (no alias, so the raw enum word
went out), `MarkerFill` was served under the JGraph spelling while MATLAB's `MarkerFaceColor` was
missing, and `XData`/`YData` refuse a length change on their own. A coverage table counts served
names; it cannot count a name served wrongly, which is the standing argument for the stress scripts.

## Decisions taken before any code

1. **`*DataSource` and `refreshdata` are implemented**, reversing the exclusion
   `docs/matlab-builtin-coverage.md` had recorded since M60. That entry was honest when it was
   written — the family it needed did not exist — and stopped being a decision the moment the family
   was built. It is the first name to leave that list rather than the unimplemented one.
2. **Polar names are real; geographic and table-sourced names are a ceiling.** `ThetaData`/`RData`
   and their modes and sources answer on a chart whose axes is polar, because there θ and r *are*
   x and y — one pair of numbers read the other way, which is why `polarplot` and `plot` share one
   model. `Latitude*`, `Longitude*`, every `*Variable` and `SourceTable` are not answered at all:
   there is no geographic axes and no table-backed chart to hang them on, and a name that answers
   `[]` for ever is the failure mode ADR 0070 exists to prevent.
3. **`histogram`, `stem` and `errorbar` gained option tails**, `errorbar`'s direction refusal was
   lifted once the sideways whiskers were drawn, and a **categorical histogram** was implemented so
   that `Categories`, `DisplayOrder`, `NumDisplayBins` and `ShowOthers` are real rather than a
   second ceiling.
4. **`Annotation` and `DataTipTemplate` are both real nested objects**, with handles of their own.

## What each wave reuses

- **The common block** (`JgsGraphicsProperties.PlotWave.cs`, one new partial file, as each of the
  last three waves had) is where the M73 mode idiom is applied unchanged: a derived reading of state
  the model already carries, never a second copy. `XDataMode` reads whether the positions were given
  or counted out; `CDataMode`, `SizeDataMode` and `AlphaDataMode` are null tests on the scatter's
  three per-point channels; `ColorMode` and `FaceColorMode` are null tests on the colour slots that
  have meant "auto" since M73 gave every plot a seat instead of a baked colour.
- **Two modes needed a flag, and the reason is worth stating**: `LineStyleMode` and `MarkerMode`
  cannot be derived from the value, because solid and none are both the automatic value *and* a
  value a script may choose. Each carries a bool the setter raises and the series cycler lowers
  immediately after writing its own — which is exactly ADR 0073's "the exceptions that needed a
  flag, because their auto values live in a theme or a constructor". It also closes that ADR's
  recorded divergence that a line "cannot tell an explicit solid linespec from an unstyled line".
- **`Annotation`** is a two-step, as MATLAB spells it: `h.Annotation.LegendInformation.IconDisplayStyle`.
  `'off'` is read by one predicate, `PlotObject.ShowsInLegend`, at the two places a legend reconciles
  its rows. Reading it never builds the object, so a renderer asking about every plot on an axes does
  not give them all one.
- **`DataTipTemplate`** carries rows of (`Label`, `Value`, `Format`), seeded per kind — X/Y for most,
  bin edges and counts for a histogram — and `DataTipsMode` consults them when it places a tip.
- **`Clipping`** became a per-plot bool that the three plot loops honour by pushing the clip per
  object rather than around the loop, so a series told not to clip draws past the box while its
  neighbours stay inside it and the draw order between them is unchanged.
- **The interpreter learned to walk a chain of handle-valued properties.** `ax.XAxis.Color = c` and
  `h.Annotation.LegendInformation.IconDisplayStyle = 'off'` are the ordinary MATLAB spellings and
  both failed before this wave: the dotted-write path accepted a bound variable or a subscript of
  one and nothing further. Every step of the walk is a property read, so it cannot have a side
  effect, and a name the owner does not answer to falls through to the struct path and its ordinary
  error.

## The histogram, which is most of the wave

`HistogramPlot` was rebuilt on `PolarHistogramPlot`'s shape — the model this project already had for
the same object drawn round a circle. Everything about the binning is now state a script may write:
the edges outright, or a count, a width, limits, or the rule by which they are chosen, and every one
of them re-cuts the bins and counts the readings again. The counting lives in the model rather than
in the verb for the reason the polar one already demonstrated: the verb sees the numbers once, and
the object goes on being asked questions.

Two names had to be untangled first. The model called the raw readings `Values`, which is MATLAB's
name for the *bin heights*; they are now `Data` and `Values` respectively, which is what
`get(h,'Values')` had been answering wrongly.

**The binning rule moved to `JGraph.Math`.** `histcounts` had carried a private `ChooseEdges`
implementing MATLAB's `auto`/`scott`/`fd`/`integers`/`sturges`/`sqrt`; it is now `Binning.EdgesFor`,
called by `histcounts`, `polarhistogram`, `discretize` and the histogram model alike. `Binning`'s own
doc comment had claimed for three milestones that it existed so those charts "cannot disagree about
which side of an edge a reading sits on" — the edge rule was shared and the *choice* of edges was
not, and now both are.

**The default binning changed.** `histogram(x)` cut ten equal bins; it now uses MATLAB's `auto` rule,
which is why `histogram([1 2 2 3 3 3 4 4 4 4])` answers four integer bins rather than ten. Every
existing histogram's picture moves. No frozen script pinned it (they assert `Type` alone), and the
gallery images in the capability report are regenerated rather than stored.

## The rest, in one line each

- **Bar**: `BarLayout` is settable after creation, which needed the grouped/stacked arithmetic to
  become a function of a whole set of series (`AxesExtensions.LayOutBars`) rather than a decision
  taken once inside the creating verb. `CData` gives every bar its own colour; `XEndPoints` and
  `YEndPoints` are projections of geometry the model already computed for drawing.
- **The baseline became an object.** `BaseLineModel` is owned by bar, stem and area; `BaseValue` and
  `ShowBaseLine` on the chart are two spellings of the line's own number and visibility, so they
  cannot drift apart. Bar and stem now *draw* it, which MATLAB does by default and this build did
  not — the one pixel change in the wave that is not opt-in.
- **Line**: `MarkerEdgeColor`, `MarkerIndices` and `LineJoin` were all modelled and rendered
  already and simply had no name a script could reach them by — `MarkerStyle.Edge` and
  `LineStyle.Join` have existed since M6. `AlignVertexCenters` is new and snaps vertices to pixel
  centres. `plot` itself learned all five, `MarkerFaceColor` most importantly: it is the commonest
  spelling in MATLAB code and this verb refused it.
- **Errorbar**: the two vertical reaches became writable, two horizontal ones were added and drawn,
  and the direction words `'horizontal'` and `'both'` mean what they say.
- **Scatter**: `AlphaData` is the third per-point channel beside size and colour, read through the
  axes' alpha map by the same `AlphaResolver` a surface and an image have used since M74.
- **The legend key gained an outline**, so a bar's, an area's and a histogram's edge colour, width,
  dash and alpha reach the swatch. They could not before, because `LegendKey` had nowhere to put
  them.

## Verification

0-warning Release **and** Debug builds; **5,072 tests** (33 new in `MatlabM77PlotPropertyTests`);
**49/49 stress scripts**, the new `stess_49.m` proving every visual family by exported pixel counts
and round-tripping the new properties through a save and a load; all four verifiers OK.

**Properties: 930 → 1,132 of 1,394 across 28 kinds.** Six of the nine kinds this wave was aimed at
now answer every documented name:

| kind | before | after | left |
|---|---:|---:|---|
| `histogram` | 21/46 | **46/46** | — |
| `bar` | 29/44 | **44/44** | — |
| `errorbar` | 26/52 | **52/52** | — |
| `stem` | 26/43 | **43/43** | — |
| `stair` | 26/38 | **38/38** | — |
| `area` | 28/39 | **39/39** | — |
| `line` | 26/52 | 48/52 | 4 geographic |
| `scatter` | 33/74 | 59/74 | 15 geographic and table-sourced |
| `bubblechart` | 32/73 | 58/73 | the same 15 |

**Reported separately**: the common block also lands on kinds this wave was not aimed at, because it
is served once for every `PlotObject` — quiver +4, boxchart +3, contour +3, patch +3, pie +3,
surface +3, image +2, constantline +1. Twenty-two names, gained by not writing them.

**Forms: 1,289 → 1,300, none lost**, and that number is two things which are reported apart:

- **5 forms the code moved**: `errorbar(x,y,yneg,ypos,xneg,xpos)`, `stem(___,'filled')`, and the
  three `refreshdata` forms.
- **6 forms the prober's own correction moved**: `stem(___,LineSpec)`, `stem3(___,LineSpec)`,
  `fplot(___,LineSpec)`, `fplot3(___,LineSpec)`, `xline(x,LineSpec,labels)`,
  `yline(y,LineSpec,labels)`. The prober sampled a `LineSpec` placeholder as `'a'`, which is a
  character vector and not a line spec — MATLAB refuses it too. The forms had been measuring whether
  a verb would swallow nonsense, which is the opposite of what they document. It was found by making
  two verbs strict enough to say no, and the sample is now `'r--o'`. This is the second wave running
  in which the measurement needed correcting before its verdict could be trusted.

Builtins **915 → 916** (`refreshdata`).

## Divergences recorded

- **The geographic and table-sourced families are not answered at all** — a deliberate 59/74 ceiling
  on scatter, 58/73 on bubblechart and 48/52 on line. There is no geographic axes and no
  table-backed chart, and an object that answers `LatitudeData` with `[]` for ever would be
  pretending at machinery that does not exist.
- **`ZData` on a flat chart reads `[]` and refuses a non-zero write.** MATLAB promotes the object to
  a spatial one; this build has no such promotion, and says so. A write of zeros is accepted,
  because that is where the chart already sits — and because a right angle's cosine is 6e-17 rather
  than 0, so `rotate` about the z axis writes back a "zero" that is not one.
- **`ZJitter` and `ZJitterWidth` on a flat scatter** answer `'none'` and `0` and refuse anything
  else, for the same reason.
- **`XData` and `YData` still refuse a length change on their own.** They are written as a pair;
  `set(h,'XData',x,'YData',y)` and `refreshdata` both do. MATLAB allows a transiently inconsistent
  series and this does not.
- **`CDataMode`, `SizeDataMode` and `AlphaDataMode` refuse `'manual'` while their channel is empty**,
  because the mode is derived from the channel and there would be nothing to freeze.
- **`IconDisplayStyle 'children'` behaves as `'on'`.** Nothing here has children that draw.
- **`AlignVertexCenters` on an error bar answers `'off'` and refuses `'on'`** — its whiskers are laid
  out in pixels already.
- **A bar chart and a stem chart now draw their baseline by default**, which MATLAB does and this
  build did not. Every existing bar and stem figure gains a line at its base value.
- **`histogram(x)` bins by MATLAB's `auto` rule** where it cut ten equal bins before.
- **A histogram's `FaceColor 'none'` is a fully transparent fill**, since the model has no separate
  fill switch; reading it back answers `'none'`, and so does a script that set `FaceAlpha` to 0.
- **`Normalization` reads back the model's own words** (`density`, `cumulative`, `cumulativeprobability`)
  where MATLAB answers `pdf`, `cumcount` and `cdf`. The verb takes MATLAB's words; the handle answers
  the model's. This predates M77 and is recorded here because the wave made the property reachable
  on a second kind.

## The frozen assets

`stess_42.m` §7 asserted the `errorbar` horizontal refusal this wave lifts, and was amended to
assert the capability instead — the same reading, and the same kind of amendment, as `stess_26` §17
in M74 and `stess_38` §20 in M76. It sits in the editable-with-cause band; the cause is that the
statement it made became false by design. No script in `stess_1`–`stess_40` was touched.

## What is not done

The geographic and table-sourced families, and with them `geoaxes`, `geoplot`, `geoscatter` and the
table-backed chart forms — all still excluded with reasons in `docs/matlab-builtin-coverage.md`.
`DataTipTemplate` rows are consulted when a tip is placed but there is no `dataTipTextRow` verb to
build one from a script, so rows are edited rather than created. `pie` remains measured against
`Patch` and answers 25 of its 56 names, which is a join being approximate rather than a gap.
