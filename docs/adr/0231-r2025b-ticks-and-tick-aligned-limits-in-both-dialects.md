# ADR 0231 — R2025b's tick rule and tick-aligned limits in both dialects: open item 99

## Status

Accepted, 2026-10-12. The eighteenth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), one item wide and decided by the user: option A, both dialects take R2025b's rule, and
both expand their automatic limits to the ticks at either end. Every number was recorded from R2025b
headless (probes `probe_99` from batch 17, `probe_99b` and `probe_99c` from this batch in the
open-items scratch, then the fixture `oi_ticks_rule`).

## Context

A linear ruler here chose its ticks from a 1-2-2.5-5-10 ladder aimed at a target count of five, and an
auto-scaling ruler padded its data extent by five per cent. R2025b takes about twice the ticks
(`[0 50000]` in steps of 5000, `[0 2]` in 0.2) and its default limits sit on ticks (`[1 10]` for
`plot(1:10)`, `[0.1 1]` for data from 0.123 to 0.987). Batch 17 measured the count over 77 ranges at
six figure sizes and left the decision open; this batch measured the rest.

What R2025b does, as far as 8,800 recorded answers show it:

- The candidate steps are 1, 2 and 5 times a power of ten. No 2.5 ever appeared.
- How many ticks a ruler may carry depends on its pixel length and its font, not on the text of its
  labels: four-digit labels at `[1000.37 1013.37]` and one-digit ones at `[0.37 13.37]` cut at the
  same count, and a 6-point font on a 300-pixel ruler takes eleven where a 14-point one takes five.
  Across the page the count rises from three on a 100-pixel ruler through ten on the default
  434-pixel one to eleven from about 470 pixels; up the page it reaches eleven at 275 pixels. Both
  limits sitting on ticks buy one more over part of each band, twelve at the top.
- Among the steps that fit, the one whose first and last ticks span most of the range wins: `[0 15]`
  takes 5 over 2 because 5 reaches both ends, `[0.123 0.987]` takes 0.1 over 0.2 because 0.1 reaches
  0.2 and 0.9. Equal spans go to the finer step, unless the finer step sits on both limits with as
  many ticks as it may have: `[0 6]` on a 217-pixel ruler is `0 2 4 6`, on a 300-pixel one `0 1 … 6`.
- A limit lands on a tick in R2025b's own floating point: `[0 1.1]` in tenths is not twelve landing
  ticks but twelve free ones, and is refused in favour of fifths.
- `tickaligned` fits the data's own step, moves each limit outward to the next tick when that is at
  most a quarter of the range away (`[-3 11]` on a short ruler becomes `[-5 11]`: the 15 is too far),
  never moves a limit inward (plot(1, -1e-7) keeps its lower limit at -1.0000001), and gives the
  step up for a coarser one when the moved limits hold too many ticks (`[-1 1000]` ends in steps of
  200 at `[-200 1000]`, not 100). A single value gets a unit either side: `plot(1, 5)` is `[4 6]`.
- `padded` sits about seven per cent outside the data and snaps to a multiple of the data's own step
  when one is within three per cent of the range (`[-10 110]` for `[0 100]`, `[12340 12402.78]` for
  `[12345 12399]`).
- In 3-D each ruler is budgeted by the longer of its edge's run across the page and its rise up it,
  with the box stretched to fill the plot area: the default view gives the x ruler about 245 pixels
  across, the y ruler 188 and the z ruler 189 up; seen from above the y ruler is the full 342 up.
- A resize refits automatic limits (`[-4 12]` at 560 by 420, `[-5 11]` at 280 by 210, back again).

The rule is a score MATLAB does not publish; a Wilkinson-style density score reproduced about
ninety per cent of the sweep, the caps-and-coverage rule above 99.2 per cent of it from 217 pixels
up and every one of the 77 ranges on the default axes.

## Decision

### The rule

`TickRule` (Core, so the limits can reach it) holds the arithmetic: the cap tables, `ChooseStep`
(candidates, caps, coverage, the tie), `Align` (the quarter-range reach, the no-inward rule, the
coarser fallback), `Pad` (seven per cent and the three per cent snap), `AroundValue` (a unit either
side) and `EdgePixels3D` (the stretched box). The caps are read at the axes' built-in tick font of
11 points, R2025b's default being 10: a figure at default fonts takes R2025b's default-figure
counts, and a written `FontSize` scales the ruler by its ratio to 11.

`TickBudget` carries what a generator may draw: the classic target the logarithmic, date and
category generators aim near, and the two caps. `ITickGenerator.Generate` takes a budget; the
count-only call every caller used is an extension, and a budget without caps keeps the classic
ladder, which is what a caller with no pixels to measure still gets. `LinearTickGenerator` takes
the capped path when the caps are there.

### Where the pixels come from

`AxesModel.BudgetFor(ruler)` reads the plot box the renderer last measured, or the estimate an
unmeasured axes gets, and gives the ruler its budget by orientation; a 3-D axes budgets by edge.
The limits are fitted against that budget (`FitRange`), so `XLim` read before any frame is the
answer the frame will draw; the estimate for a MATLAB-dialect axes is R2025b's plot box alone, as
the renderer keeps that box until the real text needs more.

The renderer's 2-D pass now loops: ticks from the last frame's box (or the estimate), decorations,
layout, then `RefitRulers` against the laid-out box and a second pass when a budget or a limit
moved, a third for the rare flip-flop. The 3-D path budgets its three rulers by their edges. A
yyaxis side ruler is budgeted by the plot height.

### Defaults and documents

`LimitMethod` defaults to `Tickaligned` in both dialects and `AutoScalePadding` to 0.07, which
`padded` uses as its fraction. A document stores the method only when it is not the default, so an
older figure opens with tick-aligned limits; `TargetMajorTickCount` stays as the classic target.

## Divergences

- **Equal coverage on a ruler under about 250 pixels can go to the other step**: `[0 5]` on a
  155-pixel x ruler is `0 2 4` in R2025b and `0 5` here, `[0 21]` is `0 10 20` there and
  `0 5 10 15 20` here; a padded `[0 0.0004]` is drawn in fifths of the range there and tenths here.
  R2025b scores density where this build caps a count.
- **A padded limit snaps at exactly three per cent on both sides** (`[-0.1 1.1]` for `[0 1]`,
  `[2.9 4.1]` for `[3 4]`, `[-0.3 1]` for `[-0.2 0.9]`); R2025b's floating point snaps one side
  (`[-0.1 1.07]`, `[2.93 4.1]`, `[-0.3 0.977]`).
- **A limit a hair short of a tick still counts the tick, and the ticks are clean numbers**:
  `[0 0.7]` in tenths is eight ticks here where R2025b's x ruler drops the 0.7 it computes as
  0.7000000000000001 and takes fifths on a 310-pixel ruler, and the zero of `-0.2:0.2:1` reads
  `2.775557562e-17` there and 0 here.
- **The axes font does not shrink with a small figure**: R2025b draws a 280-by-210 figure's axes
  at 9 points and a 200-by-800 one at 8.5, so its 171-pixel y ruler gives `[0 0.7]` the limits
  `[0 0.8]` in fifths where this build keeps `[0 0.7]` in tenths (item 118).
- **A written `FontSize` scales the count by its ratio to 11, R2025b's by its ratio to 10**: the
  default axes at 14 points answers `[0 10]` in twos as R2025b does, but a 300-pixel ruler at 14
  points may take one tick more than R2025b's.
- **A `uiaxes` counts as a plain axes of its inner box does** (`[0 10]` in twos for `plot(1:10)`);
  R2025b's 400-by-300 uiaxes answers `[1 10]` with ten ticks.
- **The polar r ruler and a colorbar keep the classic count** aimed at a target; neither was measured.
- **The classic figure is 560 by 420 pixels when no size is given**; R2025b on this machine opens
  1194 by 719, so an unsized figure's counts differ with it.

## Consequences

- Every figure changes: automatic limits sit on ticks and rulers carry about twice the ticks they
  did, in both dialects. A saved document with no limit method opens tick-aligned; one saved with
  `padded` keeps it and pads R2025b's seven per cent (its `AutoScalePadding` is kept as written).
- Six tests expected the padded limits or the old count and now expect R2025b's (`plotyy` sides
  `[1 3]` and `[100 300]`, `stackedplot` x `[1 6]`, `YLimitMethod` reads `'tickaligned'`,
  `AutoScalePadding` 0.07, a contour test pins its ruler ticks away from its level labels, the
  padding test names its method). Tests wanting exact bounds set `LimitMethod` on the ruler;
  `AutoScalePadding = 0` no longer does that.
- Fixture `oi_ticks_rule` (275 lines, 8 recorded divergences, numbers rounded to nine decimals
  because R2025b's tick values carry its floating-point noise). The three limit lines of
  `oi_axes_box_styles_tables_scroll` that ADR 0230 recorded as divergences agree now and were
  re-recorded as exact.
- `xlim`, `ylim`, `zlim`, `rlim` and `thetalim` with a pair now write through the property path when
  the axes has listeners, so a `PostSet` on `XLim` hears the verb whether or not the pair changes
  anything; with aligned limits it often does not, which is how `oi_handle_members` caught it.
- Full suite 11,027 of 11,027; stress 89 of 89.

## Still open

- Item 118 (the axes font shrinks with a small figure in R2025b) and item 119 (R2025b's density
  score for rulers under about 250 pixels, the polar r ruler and the colorbar), filed.
