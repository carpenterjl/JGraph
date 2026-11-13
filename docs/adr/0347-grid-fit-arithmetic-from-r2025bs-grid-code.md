# ADR 0347 — A grid's tracks, spans, fit size and scrolling are transcribed from R2025b's grid code

## Status

Accepted, 2026-11-12. Open item 153, which designer stage DS4 (ADR 0248) left under "Not measured". It
changes the arithmetic of `uigridlayout` from U5 (ADR 0202).

## Context

U5 wrote `UiGridMath` from what probe `u5_grid` measured of R2025b's settled layouts. DS4 later ran
R2025b's own grid layout code under node against it and found four places where the two differ:

1. **A 0-pixel track and the spacing.** R2025b lets a track of 0 pixels that is not weighted give up
   the spacing after it. That covers `0`, `'0x'` and an empty `'fit'`. A last such track gives up the
   nearest spacing before it. JGraph dropped the spacing only round an empty `'fit'` track, and kept it
   for `0` and `'0x'`.
2. **A child that spans `'fit'` tracks.** R2025b takes the spanning children largest first. Each one
   shares what it still needs among the `'fit'` tracks it crosses that no child sits in alone, and
   each empty one takes a pixel first. JGraph took the children in their order and shared among all
   the `'fit'` tracks.
3. **A grid's own fit size.** A grid that sits in a `'fit'` track of another asks for its weighted
   tracks' content times the weight. JGraph asked for the content alone.
4. **Scrolling.** A scrollable grid scrolls only as far as its last occupied row and column. JGraph
   scrolled to the end of its tracks.

The code is in R2025b's web bundle
(`toolbox\matlab\appdesigner\web\release\bundle.mwBundle.appdesigner.js`). Seven modules were read
and run:
- `gbtcomponents/controller/GridController`: `_convertFitToPixels`, the two fit passes, the fit size
  (`_computeFitLength`, `_convertSizesToPixels`) and the scroll box (`_getScrollableContentBox`,
  `_getScrollableContentMinimumSize`);
- `gbtshared/util/GridLayoutUtils.getSizesWithSpacing`;
- `gbtcomponents/view/Grid`: a weighted track's pixels and where a child's cell begins and ends;
- `gbtshared/strategies/GridLayoutStrategy`: a child's rectangle;
- `gbtshared/mixins/controller/ScrollableContentBoxControllerMixin.calculateScrollableState`;
- the design-time `uicomponents_appdesigner_plugin/controller/GridController._convertFitToPixels`.

The MATLAB side's `matlab.ui.container.internal.controller.GridLayoutController.convert0xTo0px` turns
`'0x'` into 0 pixels before the view sees it. Without it, node gives a fit size of NaN for a grid with
a `'0x'` track, because `normalizeSizesTo1x` divides by the smallest weight. R2025b gives 43 for the
probe's grid, which is what the conversion predicts.

R2025b answers a grid's child `Position` headless (`-noFigureWindows`, a `uifigure` that is never
shown, a short wait after `drawnow`), as `u5_grid` already relied on. So every one of the four was
also measured in R2025b itself. Probe `p153` printed 36 rectangles and three scroll locations, and
each agreed with node to the digit.

## Decision

### `UiGridMath` is a transcription

`UiGridMath.Arrange` and `UiGridMath.ArrangeScrolling` keep their signatures and their callers (the
model, the renderer and the designer's canvas). Inside, each direction is laid out the way R2025b lays
it:
- `'0x'` becomes a fixed track of 0 pixels.
- `FitToPixels` is `_convertFitToPixels`. First, each child that sits alone in a `'fit'` track sets
  that track to its largest size. Then the children that span are taken by descending fit size, in a
  stable sort. A span that crosses a weighted track asks nothing. Otherwise the child's need is its
  size, less the tracks it crosses, less the spacing between the ones that are not 0. That need goes
  to the `'fit'` tracks no child sits in alone, or to all its `'fit'` tracks when there are none.
  Each empty one takes a pixel and the spacing it opens, and the rest is shared equally.
- `WithSpacing` is `getSizesWithSpacing`. It builds the view's list of tracks and spacings and zeroes
  the spacing round 0-pixel unweighted tracks.
- The weighted tracks share what the view's other entries leave inside the padding, by weight, and
  never go below nothing (`_getColumnWidthInPixels`).
- A child's rectangle runs from its first track to its last that is not a 0-pixel unweighted track
  (`_getEndIndexForView`). It covers the view's entries in between, spacing included.
- Asked for its own size (`size` null), a grid treats its weighted tracks as `'fit'` and multiplies
  each one's result by its weight over the smallest weight (`normalizeSizesTo1x`). The spacing round a
  weighted track is kept even when it comes to 0.
- At design time, an empty `'fit'` track becomes 10 pixels after the passes. This is the design-time
  controller's override, unchanged from DS4.

`UiGridArrangement` keeps its shape. `RowStarts` and `ColumnStarts` are now the view's sums, which
put a 0-pixel track where the next one starts, as before.

### Scrolling

`ArrangeScrolling` is `calculateScrollableState`, repeated with the bars until they settle:
- The content reaches to the last occupied row and column (1 and 1 for an empty grid), with the
  padding round it.
- A direction scrolls when its content is wider or taller than the grid.
- When only one direction scrolls, the other takes a bar only if its minimum size and a bar do not
  fit. The minimum size counts weighted tracks as nothing, the tracks up to the last occupied one, and
  every spacing in the grid.
- The arrangement's `Content` is R2025b's content box. This is what `scroll`,
  `ScrollableViewportLocation` and the renderer's clamp measure against. When the other direction's
  bar was not needed, the content box is that bar narrower.

## Consequences

- New `GridFitOracle.json` beside the tests: 1,524 grids, with node's answers to each. Of these, 24
  are worked cases for the four differences and the design-time track. The other 1,500 are
  seeded-random grids of 1 to 5 tracks each way, mixing fixed, `0`, `'fit'`, weighted and `'0x'`
  tracks, with up to 5 children (spanning or not). Each random grid is checked laid out at a size,
  asked for its own size, at design time, or scrolling. The harness, the module extractor and the case
  generator stayed in the session's scratch folder.
- New `UiGridFitOracleTests` checks every case in the oracle against `UiGridMath`, to 1e-6 a pixel:
  each track's size and start, every child's rectangle, the fit size, and the scroll bars and content.
  There are worked tests for each difference too.
- New parity fixture `u5_grid_fit`, 99 rows recorded from R2025b 25.2:
  - the 0-pixel tracks in columns and rows, with `'0x'`, a last track and a span ending on one;
  - an empty `'fit'` track between two fixed ones;
  - one child, and then two children, spanning `'fit'` rows;
  - a weighted grid's fit size, and a `'0x'` grid's fit size;
  - the scrollable grid's rectangles and scroll locations.
- `u5_grid` and the scroll rows of `oi_axes_box_styles_tables_scroll` and `u9_values` do not move.
  Their grids have no 0-pixel fixed track, no span that the new sharing changes, no weighted nested
  grid with weights above 1, and every track occupied.
- A grid nested in a `'fit'` track with weights above 1 is now larger, so an app built that way lays
  out differently, as it does in R2025b.

## Divergences

None new. The four differences of open item 153 are closed, and their lines in `u5_grid_fit` agree.
ADR 0202's divergence about a scrollable grid laying out in what its bars leave is struck. ADR 0248's
"Not measured" bullet about the grid's pixels is struck too. Each has a "Closed since" paragraph.

## Not measured

- The window: the grids were drawn neither in the JGraph window nor in R2025b's. Every number here
  is a `Position` read back headless or node's answer.
- A grid in a tab: R2025b's `calculateScrollableState` takes the tab group's border off the content
  height before the minimum-size test (`_contentBoxHackFcn`). It is not transcribed.
- A scrollable grid's own fit size: R2025b adds its current scroll bars to it (`_computeLength`'s
  insets). It is not transcribed.
- A child whose fit size changes when a bar appears: R2025b recomputes the fit height of children
  in weighted columns (`recomputeFitHeight`). `UiFit` has no width-dependent heights for this to act
  on.

## Still open

Nothing of open item 153.
