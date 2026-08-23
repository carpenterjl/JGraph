# 0079 — The table a chart is drawn from, and the patch a pie is drawn as

Date: 2026-08-23 · Milestone: M79 · Status: accepted

## Context

M77 closed the chart primitives and M78 the furniture around them, leaving 84 documented property
names unanswered. They were not one gap but six, and only two of the six were ceilings:

- **The table-sourced family** — `SourceTable` and the `*Variable` block, 15 names on scatter and 15
  on bubblechart (the same object), 4 on line. M77 declined them because nothing here was drawn from
  a table. That was true of a *line*; it was never true of a scatter, whose table form has existed
  since it was written.
- **Pie's 31**, the largest single block left. MATLAB has no pie object: `pie` makes one patch per
  wedge, so the coverage table scores a pie against `Patch` — and a pie here answered 25 of those 56
  names because it drew its own polygons and had no patch to answer for.
- **A constant line's label font** (6) and **a box chart's two colour modes** (2), each a small
  block whose model had nothing under it.
- **The geographic family** (16) and **`Layout`/`Interactions`/`Toolbar`** (11), the two real
  ceilings.

Reconnaissance also found what M77 and M78 each found by running the forms rather than counting the
names: a box chart answered `BoxFaceColor` with `'none'` while plainly drawing a filled box. The
count was right and the property was wrong, for the third wave running.

## Decisions taken before any code

1. **The table-sourced family is implemented; the geographic one stays a ceiling.** These were one
   decision in ADR 0077 and are two here, because the reason differed: a table-backed marker chart
   exists and a geographic axes does not. This is M78's heatmap decision applied where the same fact
   holds. Scatter reaches 68 of 74 and bubblechart 67 of 73; the remainder is `Latitude*` and
   `Longitude*`, and it is now the whole of what those kinds do not answer.

2. **A pie is backed by a real patch, and the patch is what is drawn.** Not a patch kept beside the
   drawing to answer questions about it — the wedges *are* a `PatchPlot`, rebuilt from the values
   whenever they move, rendered by the patch renderer. That is the difference between 31 names
   answering and 31 names acting: the markers are drawn, the dash is drawn, the per-wedge alpha is
   drawn, and the lighting shades the wedges through the same `Shading3D` a patch uses.

3. **A pie's geometry is read and never written.** `Faces`, `Vertices`, `XData`, `YData` and `ZData`
   describe wedges the pie worked out from its values rather than deciding them, so a write is
   refused by name, and the refusal names the four properties that do move them.

4. **The patch learns one thing it did not have: a colour chosen per face.** A pie's wedge colours
   come from its colormap in a way no colour *mapping* reproduces — `Resample` cycles a discrete
   palette where `Sample` clamps it — so reproducing the picture through `CData` would have changed
   it. `PatchPlot.FaceColors` is a colour per face, outranked by both `CData` and an explicit
   `FaceColor`, and it is also the honest implementation of `FaceVertexCData` written as colours.

5. **A pie's `FaceColor` answers `'flat'`.** Its faces take a colour each unless a script names one,
   and `'flat'` is MATLAB's word for that — the reading a surface's own `FaceColor` already takes
   here. Answering the series colour, which is what the shared block would have done, would have been
   a fourth wave's worth of the same mistake.

6. **The box chart's two colours are corrected as well as moded.** `BoxFaceColorMode` is only worth
   answering if the colour beside it is the one being drawn, so both now answer the seat's colour
   when none has been chosen. This is a fix, not a divergence: the old answer described no drawing.

## What each part is built on

| Part | Built on |
|---|---|
| The scatter source record | `HeatmapSource` from M78, on the same handle entry, for the same reason |
| Reading a table form | one peel shared by six verbs, rewriting the call into the array form |
| Re-reading a changed variable | `ReplotFromSource`, the shape of M78's `ResummariseHeatmap` |
| Writing both positions at once | `XYPlot.SetData` — the pair rule M77 recorded, honoured rather than worked around |
| The pie's wedges | `PieGeometry`, unchanged: the same fan of vertices, handed to a patch instead of to a polygon |
| The pie's whole property surface | `AddPatchBlock` from M78, reached through one accessor that answers a pie's patch |
| The pie's lighting | `PatchPlot.ResolveShading`, told which chart to find the axes' lights through |
| The label's font | `AddTextStyleBlock` from M78, over a style slot filled from what is drawn |
| The box chart's modes | `AddNullableMode` from M73 |

## Verification

**Properties answered — the kinds this wave aimed at:**

| kind | before | after | what is left |
|---|---:|---:|---|
| pie | 25/56 | **56/56** | — |
| constantline | 29/35 | **35/35** | — |
| boxchart | 27/29 | **29/29** | — |
| scatter | 59/74 | 68/74 | the geographic six |
| bubblechart | 58/73 | 67/73 | the geographic six |

**Totals: 1,310 → 1,367 of 1,394.** Fourteen kinds answer every documented name. Every property
still unanswered is one of two things and nothing else: geographic (16), or the
`Layout`/`Interactions`/`Toolbar` block (11).

**Syntax forms: 1,300 → 1,323 accepted**, and the two halves of that are worth separating because
only one of them is a gain:

| | forms | |
|---|---:|---|
| the table forms this wave implemented | 16 | scatter, scatter3, swarmchart, swarmchart3, bubblechart, bubblechart3, polarscatter, polarbubblechart — two each |
| forms that always worked and were mis-probed | 7 | heatmap ×3, scatterhistogram ×3, stackedplot ×1 |

Target forms 97 of 97 → **100 of 100**, the denominator having grown by the three commands whose
table form now probes rather than errors.

Gate: 0 warnings in Release and Debug · 5,136 tests · 51 of 51 stress scripts, the new `stess_51.m`
proving each family by exported pixel counts.

**The one proof worth naming**: seven pie exports — plain, exploded, part-circle, single-wedge,
zero-valued, styled and colormapped — are byte-identical before and after the drawing was handed to
the patch. A rewrite of how a chart is drawn is worth only as much as the evidence that it draws the
same thing.

## Divergences recorded

- **The geographic family is not answered at all** — a 68/74 ceiling on scatter, 67/73 on
  bubblechart and 48/52 on line, restated from ADR 0077 with its other half removed. There is no
  geographic axes, and a chart answering `LatitudeData` with `[]` for ever would be pretending at
  machinery that does not exist.

- **A pie's mesh is read and never written.** MATLAB's pie is patches a script may reshape; here the
  shape comes from the values, so `Faces`, `Vertices` and the three coordinate arrays refuse a write
  and say which properties move them.

- **A pie's lighting acts when the axes is viewed in three dimensions**, which is where a patch's
  own does — a flat drawing here is not lit by anything. `stess_51` §2 pins both halves: a light
  moves the wedges under `view(3)`, and `FaceLighting 'none'` gives back exactly the unlit picture.

- **A colour variable is read as one when the word is not an option name.** The bubble verbs take an
  optional colour variable after their required ones, so a table whose variable is called
  `'LineWidth'` has to be reached through the property rather than through the call.

- **`AlphaVariable` is refused on a marker chart in space, and `ZVariable` on a flat one.** Each
  answers empty and refuses a name, saying which verb draws a chart that has the channel — the shape
  `ZData` already takes on a flat chart.

- **`bubblechart3` counted its arguments before its table was read.** The verb checked for four
  arrays before handing over to the shared body, so the table form — which names four channels
  rather than passing them — was refused for missing what it had named. Found by the form prober
  after the sample was corrected, which is the prober earning its keep in the same run that fixed it.

- **The form prober was passing a table where a variable name belongs.** The dump types `xvar` as
  "one or more table variable indices", whose first matching keyword is `table`, so every documented
  table form was probed with the table itself in the name's place and every one was recorded as a
  refusal the build had earned. It had not. This is the M77 LineSpec finding exactly, one wave later
  and in the same file: a sample chosen by keyword can measure the prober rather than the build, and
  the forms it moves are a correction rather than a gain.

## What is not done

- `Layout`, `Interactions` and `Toolbar` — 11 names across six kinds, the machinery ceiling M73
  recorded and M80 is aimed at.
- The geographic family, per the divergence above. It needs a basemap tile service, which is a
  product rather than a milestone.
