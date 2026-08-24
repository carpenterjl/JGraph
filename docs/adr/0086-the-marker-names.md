# 0086 — The marker names

Date: 2026-08-24 · Milestone: M86 · Status: accepted

## Context

ADR 0072 left a line in its *What is not done*: **`MarkerFaceColor`/`MarkerEdgeColor` are spelled
`MarkerFill` here**, found while probing `'none'` across the kinds and deferred as *a naming pass
rather than that milestone's subject*. The capability report has carried it since, and it is the
smallest-sounding of the three rows the user named.

Probing it first, as the M46 rule requires, turned a cosmetic item into three real ones.

**The names were not merely internal.** The property table is built from two layers, and the lower
one is *reflection over the model's own browsable properties* — so `MarkerFill` and `MarkerEdge` were
**served as properties in their own right**, beside the MATLAB names the curated layer added on top.
Every kind that draws a marker answered two names MATLAB has never had, and the misspelling helper
recommended one of them: `set(h, 'MarkerFacColor', 'r')` on a line answered *"Did you mean Color,
Marker, **MarkerFill**, MarkerSize?"* — teaching a MATLAB script a JGraph word.

**On the charts in space the names were missing outright.** `plot3` makes a `Line` and `stem3` makes
a `Stem` — the same MATLAB classes `plot` and `stem` make — and neither answered either colour:

    line3d    | Face ERR A line has no property 'MarkerFaceColor'. Did you mean … MarkerFill?
    stem3d    | Face ERR A stem has no property 'MarkerFaceColor'. Did you mean … MarkerFill?

That had never been counted, because **the property census asks the `line` kind through `plot([1 2
3])` and has never asked `plot3` anything.** `KINDS` in `probe-properties.py` names one snippet per
MATLAB class, and `plot` is the one it picked.

**And `stem3`'s own verb answered wrongly.** `stem3(…, 'MarkerEdgeColor', c)` set `plot.Color` — the
whole series — because the marker had no edge of its own to put a colour on. Asking to outline the
marker heads repainted the stalks.

## Decisions taken before any code

1. **The model takes MATLAB's names.** `MarkerFill` → `MarkerFaceColor`, `MarkerEdge` →
   `MarkerEdgeColor`, across the eight plot classes that carry them. The backing fields keep their
   short names; the `DisplayName` attributes keep *"Marker fill"* and *"Marker edge"*, which are
   property-inspector prose for a person, not an API surface for a script.

2. **The wire keeps the old keys.** Every renamed DTO property carries
   `[JsonPropertyName("markerFill")]` or `[JsonPropertyName("markerEdge")]`, and
   `GraphFormat.CurrentVersion` stays at 6. A saved figure is a file somebody already has: renaming a
   CLR property must not turn a document written yesterday into one that loads with its markers
   blank, and nothing about the *format* changed, so a version bump would be a lie about what a
   reader needs. The pin is tested rather than trusted — a later tidy-up "correcting" these keys
   would lose data with no error anywhere to notice it by.

3. **`ScatterPlot` keeps `Fill` and `Color`.** A different word doing a broader job: `Fill` is also
   the bubble face a `bubblechart` reads through `IBubbleData`, and the property table already serves
   both MATLAB names over them. Half-renaming it would have made the pair look symmetric when it is
   not.

4. **The two spatial kinds get a real edge, not a served one.** `Line3DPlot` and `Stem3DPlot` had the
   fill and no edge at all, so this is one field each and one line in each renderer — the same shape
   `LinePlot` has had all along, `?? color` fallback included.

5. **They get curated entries too, not just reflected ones.** Once the model spells them MATLAB's
   way, reflection alone would serve both names. What it would not serve is the *word*: `'none'` is
   an unfilled marker rather than a colour, and no reflected colour property knows that. A colour
   that reads back `'none'` on a flat line and refuses it on a line in space is the half-fix.

6. **`scatter3` is left alone and recorded.** It has `Filled` (a flag) and `Color` where a face and an
   edge belong; giving it the two colours means a model change that reaches `StyleFor`, the depth
   sort, `IBubbleData` and the serialized shape. That is a chart-property wave, not a naming pass,
   and pretending otherwise would have smuggled it in under this milestone's name.

## Found by probing rather than by reading

- **`Stem3DPlotDto` carried a `markerEdge` field that neither mapper arm ever read or wrote** — a
  serialized property that has never round-tripped, for as long as it has existed. It is live now
  because the model finally has something to put in it.
- **The `plot3`/`plot` gap is much wider than these two names.** Measured by set difference on
  `fieldnames(get(h))`: `plot` answers 61 names and `plot3` answers 42; `stem` and `stem3` differ by
  13; `scatter` and `scatter3` by 27. MATLAB documents each pair as one class. See *What is not done*.

## Verification

- 0 warnings in Release and Debug; **5,266 tests** (5,247 → +19); **58 of 58 stress scripts**,
  including the new `stess_58.m`, which passed all eight sections on its first run.
- **The documented property counts do not move: 1,569 of 1,585 across 31 kinds, unchanged.** What
  moved is the *extra* column, down by two on each of the seven kinds that carried both JGraph
  spellings — **fourteen names that were never MATLAB's, off the surface, with no documented name
  lost.** Neither `MarkerFill` nor `MarkerEdge` appears anywhere in `matlab-r2021b-properties.csv`,
  which is why the numerator could not have moved and the check was run anyway.
- **Three floors in `MatlabM78FurniturePropertyTests` were lowered by two** — surface 83 → 81, patch
  79 → 77, quiver 65 → 63 — and the reason is written beside them. Those floors count *every* name a
  kind answers, so they were set with the two JGraph spellings inside them. A shrinking property
  surface is exactly what that test exists to catch, so lowering it deserves an explanation rather
  than an edit.
- Syntax forms and builtin counts unmoved; all four verifiers OK. The nine stress scripts that touch
  either name all use MATLAB's spelling and were unaffected, which was checked before the rename
  rather than discovered by it.

## Divergences recorded

- **`scatter3` answers neither marker colour.** It carries a fill *flag* and one colour where MATLAB
  documents a face and an edge, so the names are absent rather than wrongly served — which is the
  better of the two failures, and is why it was left rather than faked.
- **The saved keys are `markerFill` and `markerEdge`.** The document format keeps the spellings the
  model used until M86, deliberately and permanently.

## What is not done

- **The 2-D/3-D property gap.** `plot3` answers 42 names to `plot`'s 61, `stem3` 13 fewer than
  `stem`, `scatter3` 27 fewer than `scatter` — and MATLAB documents each pair as a single class. Most
  of the difference is the `*Mode` and `*DataSource` families and the polar channels, none of which
  the spatial kinds carry. It is a clean property wave of its own, and the first thing it needs is a
  row per spatial kind in `probe-properties.py`, because **the census cannot presently see any of
  this**: it measures `Line` through `plot` and has never built a `plot3`.
- **`scatter3`'s face and edge**, per decision 6.
