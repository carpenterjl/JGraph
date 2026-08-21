# 0073 — The axes property families, wave one: the actable core

Date: 2026-08-21 · Milestone: M73 · Status: accepted

## Context

The capability report's gaps page named the 94 unanswered axes properties as the largest single
block in the property table (axes 53/147, polaraxes 39/107): the camera, layout, and ruler
families JGraph had no model for. Three decisions were taken before any code:

- **Full behavior everywhere.** Every property added must visibly act — a name that answers but
  does nothing is the failure mode ADR 0070 exists to prevent — so a handful of families whose
  behavior needs new rendering machinery were split into a second wave rather than stored inertly.
- **Two waves.** This one is the actable core: **69 properties** — fonts (12), grid/box (13),
  ticks and rulers (30), series cycling (5), color mapping (5), aspect ratios (4). Wave two owns
  the camera family and a perspective projection (9), the layout family
  (`InnerPosition`/`OuterPosition`/`TightInset`/`PositionConstraint`/`Units`, 5), alpha mapping
  (`ALim`/`ALimMode`/`Alphamap`/`AlphaScale`, 4), and the misc four
  (`Clipping`, `ClippingStyle`, `CurrentPoint`, `SortMethod`).
- **`Interactions`, `Toolbar`, and `Layout` stay unanswered by decision** — a 144/147 ceiling,
  recorded below, because an empty placeholder object pretends at machinery that does not exist.

The split was verified against the probe CSV before work began: 69 + 22 + 3 covers the 94 missing
names exactly. Because `axes` and `polaraxes` share one CLR type and one property table, 36 of the
wave's names land on polaraxes at the same time. Measured after the wave: axes **53 → 122/147**,
polaraxes **39 → 75/107**, properties overall **747 → 852 of 1,394**.

## The mode idiom

No `*Mode` property existed before this wave. All twenty-four of them here are **derived words
over nullable-means-auto state**, never a second copy: `XLimMode` is `AutoScale` spoken as
auto/manual, `XTickMode` is `TickPositions is null`, `XColorMode` is `RulerColor is null`,
`TickDirMode` is `TickDirection is null`, `DataAspectRatioMode` is the stored ratio's null test.
Writing `'manual'` freezes what is showing — the same semantics `xlim('manual')` has had since
M51 — and writing `'auto'` releases it. The four grid modes and `FontSizeMode` and
`PlotBoxAspectRatioMode` are the exceptions that needed a flag, because their auto values live in
the theme or a constructor rather than in a nullable slot; the flag's one observable meaning is
that a theme pass leaves a manual grid color alone and rewrites an automatic one.

## What each family reuses

- **Fonts** are write-time fan-out over the `TextStyle` structs the model always had, the
  `Theme.Restyle` idiom: `FontSize` writes every ruler's tick and label styles and the title pair
  through `TitleFontSizeMultiplier`/`LabelFontSizeMultiplier`; reads answer off the primary X
  ruler's tick style, which is MATLAB's own equation of the ruler font with the axes font.
  `FontSmoothing` became a real `TextStyle.Antialias` bit that `SkiaRenderContext.DrawText` sets
  per call; `TickLabelInterpreter` rides M72's `Interpreter`.
- **Grid appearance** was already stored and rendered (`GridModel.MajorLineStyle`/
  `MinorLineStyle`); the wave added per-direction visibility (`ShowMajorX/Y/Z`, minor same),
  with `ShowMajor`/`ShowMinor` becoming aggregate wrappers so `grid on`, the JG API, and old
  documents keep their meaning. `GridAlpha` is the style color's alpha channel, not a second
  number. The 3-D renderer's hardcoded grid pen was replaced with the model's — so `grid off`,
  `GridColor`, and the per-direction switches finally act in 3-D — and entering 3-D turns the
  wall grid on, which is the figure every 3-D verb has always produced and what MATLAB shows.
- **`Layer`** re-orders one call: the grid draws after `DrawPlots` when `'top'`. **`LineWidth`**
  replaced five hardcoded 1-pixel pens (frame, ticks, 3-D box). **`BoxStyle` `'full'`** strokes
  the five near edges the far-face outline never reached.
- **Rulers** gained `RulerColor` (inks the axis line, ticks, and both text runs; the yyaxis
  series tint yields to it), a settable `Position` (`XAxisLocation` top, `YAxisLocation` right —
  margins, ticks, and labels all mirror), `TickDirection` in/out/both, `TickLength` as MATLAB's
  fractions, and `LimitMethod` — `padded` (the JGraph default every figure was fitted under),
  `tight`, and `tickaligned` with a local 1-2-5 ladder whose twin lives in the Maths tick
  generators (Core cannot reach them; the duplication is deliberate and commented). `axis tight`
  and `axis padded`, accepted no-ops before, now mean these policies.
- **Series cycling became stateful.** An auto-styled plot takes a *seat* (`PlotObject.SeriesIndex`)
  from the axes' counter instead of having its color baked at creation; the renderer resolves the
  palette from the seat at draw time through one shared `SeriesPalette.Resolve` (replacing five
  positional walks plus the legend renderers' two). That is what makes `ColorOrderIndex` rewind
  real, lets `colororder` retint a live figure, stops a deletion from recoloring the survivors,
  and finally lets script-drawn auto lines follow a theme switch. `LineStyleOrder` steps once per
  lap of the palette, the MATLAB div/mod law, applied at creation. An emptied axes resets the
  counter in the same `CollectionChanged` handler yyaxis already used, so `cla`, hold-off, and the
  replacing verbs never learned a counter exists. Raw-API plots never take seats and are colored
  positionally exactly as before.
- **Color mapping is axes-level state with per-plot working copies.** `AxesModel.Colormap` and
  `ColorLimits` fan out on write through a public `PlotObject.AdoptAxesDefaults` hook — the same
  hook the axes raises when a plot joins it, which is what fixed the order-dependence bug:
  `colormap jet; surf(...)` used to do nothing. `ColorScale` `'log'` runs through one new
  `Colormap.Sample` overload consumed at every mapped plot's sampling site and by the colorbar's
  gradient and decade ticks; the image tile's cache key learned the flag, because a cache that
  ignores it silently keeps the old spread.
- **`AmbientLightColor`** multiplies the ambient term per channel in `LightingModel.Shade`, read
  once per pass beside `SceneLights`; white — the default — multiplies nothing away, and like
  MATLAB it only shows while a light exists.
- **Aspect ratios**: `DataAspectRatio` is stored and consulted every frame — 2-D generalizes the
  equal-aspect shrink to arbitrary unit ratios, 3-D shapes the projection box from the spans — and
  `PlotBoxAspect` writes clear it (last writer in charge), so `daspect([1 1 1])` now keeps its
  equal-units promise when the limits later change instead of freezing the box it happened to
  make.

## What the wave found on the way

- The `??=` color bake at every creation site existed so `get(h,'Color')` answered definitely;
  the seat-based peek answers the same values without freezing them.
- The frame was one `DrawRectangle`; per-edge drawing was required for `XColor`/`YColor` to reach
  their own lines, and it is what let `box off` keep the ruler pair (user-approved, MATLAB's
  look). Charts that put their rulers away entirely — pie, Smith, `imshow` — still draw nothing,
  because the pair is gated on the ruler showing ticks or labels.
- The 3-D grid had never consulted `GridModel` at all — `grid off` was a silent no-op there, and
  the default-off grid state was invisible only because the renderer ignored it.
- The image tile cache and the surface palette cache both needed the color scale in their
  validity keys; the pixel-diff discipline (export twice, count) caught the first within minutes.

## Verification

4,834 tests (28 new in `MatlabM73AxesPropertyTests`), 45/45 stress scripts — the new
`stess_45.m` proves every visual family by exported pixel counts — all four verifiers OK, and the
property probe re-measured: 852 of 1,394 across 28 kinds. The frozen pins
(`XLim` after autofit, lowercase words, `View` pairs) were audited before the mode work and hold:
`RecomputeDataBounds()` runs at exactly the times it did.

## Divergences recorded

- **`Interactions`, `Toolbar`, and `Layout` are not answered at all** — a deliberate 144/147
  ceiling: an interaction object, an axtoolbar, and a tiled-layout options object would be
  placeholders for machinery that does not exist yet.
- **`LineWidth` defaults to 1 where MATLAB defaults to 0.5**, because 1 is what every existing
  figure was drawn with.
- **The automatic `TickDir` is `'out'` where MATLAB's 2-D automatic is `'in'`**, and the
  automatic `TickLength` answers MATLAB's `[0.01 0.025]` while drawing the fixed five pixels
  every figure has always had; a chosen length is honored as the fraction it is.
- **The automatic limit method is `padded` where MATLAB's is `tickaligned`** — the frozen stress
  scripts pin fitted limits under the padded policy.
- **`FontUnits` other than `'points'` are refused** rather than converted.
- **A later axes-font write re-derives the title size over a manual `title(...,'FontSize',...)`**
  — there is no per-text-object `FontSizeMode` yet.
- **Writing `ColorOrderIndex` restarts the line-style lap too**; MATLAB tracks
  `LineStyleOrderIndex` as an independently writable counter, and here it is read-only and
  derived.
- **A non-default `LineStyleOrder` styles a line at creation and does not restyle it
  retroactively**, and it cannot tell an explicit solid linespec from an unstyled line.
- **The axes `Colormap` read answers the map's stop table**, not MATLAB's 256-row resample.
- **`heatmap` and `bar3` keep their own colormaps** — the axes-level fan-out reaches the
  color-mapped families (`image`, `surface`, `contour`, `patch`, `scatter`, `scatter3`,
  `binscatter`) and leaves chart-styled kinds alone.
- **The minor grid is not drawn in 3-D**, though its switches answer; and `Layer` re-orders the
  grid in two dimensions only.
- **`YAxisLocation` refuses a yyaxis axes** (MATLAB refuses it too, with a different message),
  and `'origin'` is refused for both locations.
- **`FontSmoothing`, `TickDir`, `TickLength`, and the axis locations do not reach the yyaxis
  side rulers'** drawing path; the primary pair honors all of them.

## What is not done

Wave two, scoped and deferred: the camera family with a real perspective projection
(`CameraPosition`/`Target`/`UpVector`/`ViewAngle` + modes, `Projection`), the layout family
(`InnerPosition`, `OuterPosition`, `TightInset`, `PositionConstraint`, `Units`), alpha mapping
(`ALim`, `ALimMode`, `Alphamap`, `AlphaScale`), and `Clipping`/`ClippingStyle`/`CurrentPoint`/
`SortMethod`. Also left where they were: per-text-object font modes, the polaraxes R/Theta mode
spellings (`RLimMode`, `ThetaTickMode`, …), and window-level interaction machinery.
