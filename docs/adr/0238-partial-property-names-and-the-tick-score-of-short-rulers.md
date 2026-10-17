# ADR 0238 — the start of a property name, and the tick score where the count caps run out: open items 86 and 119

## Status

Accepted, 2026-10-16. The twenty-fifth batch of the open-items work
(`docs/plans/open_task_chips_09_12_2026.md`, gitignored): item 86, found in batch 9 (ADR 0220) when
`'Pos'`, `'XLi'` and `'Mark'` disproved the shortest-start rule, and item 119, found in batch 18
(ADR 0231) where the count caps stopped reproducing R2025b under about 217 pixels. The recordings
are `probe_86` and `probe_86b` (every start of every name of 23 kinds of graphics object, 33,000
lines; the attribute lists; the forms get, set, cell, struct, findobj and the creating functions
take), `probe_119b` (15,000 rows: a square axes at every five pixels from 40 to 260 at a written 10
points, at 8 and at 13, the polar r ruler at 21 radii), `probe_119c` (ranges slid and stretched
under 60 and 140 pixels) and `probe_119d` (18,000 rows: the capped region to 500 pixels at a written
10, and the automatic font against the written one), all headless, 2026-10-16.

## Context

**86.** R2025b's `get` and `set` take a start of a property name, in any case, where the dot takes
whole names only; which starts resolve is neither "unique" nor "the shortest when it starts all the
others" (`'Pos'` resolves although `PositionConstraint` begins the same; `'Tit'` is refused although
only `Title` is listed). JGraph took whole names only.

**119.** ADR 0231's rule — a cap on the tick count by the ruler's pixels over its font, coverage
deciding among the steps under it — reproduces R2025b from about 217 pixels up and on the default
axes exactly. Under that, R2025b weighs density: `[0 5]` on a 155-pixel x ruler is `0 2 4` there and
`0 5` under a cap. The polar r ruler and the colorbar kept the classic target count. ADR 0232 had
refitted the caps at a fixed font and found the batch-18 edges had absorbed the automatic font
shrink of a small axes (open item 118) without saying how.

## Decision

### 86: a start means the one best-ranked name it begins

Read off probe_86 (`JgsGraphicsProperties.PartialNames.cs`): the names of the object's R2025b class
that begin with the text (case-insensitively) are ranked — a listed name that can be set, above a
listed one that cannot be set or a `…Mode` twin of another name, above the hidden names — and the
start means the one name in the best rank that has any; a rank holding two is refused. So `'Pos'` is
`Position` (`PositionConstraint` is one of six names R2025b never abbreviates, each standing beside
a hidden older spelling: the Position and Limit methods), `'XLi'` is `XLim` over `XLimMode`, `'YA'`
is `YAxisLocation` over the read-only `YAxis`, `'Ta'` is `Tag` over the hidden `TargetManager`, and
`'Tit'` (Title, TitleHorizontalAlignment, TitleFontWeight, …), an axes' `'Col'` and a line's
`'Mark'` are refused with `MATLAB:hg:InvalidProperty` — `Unrecognized property Mark for class
matlab.graphics.chart.primitive.Line.` from `get`, the class's last word from `set` and the creating
functions. A whole name always wins over the starts it is.

- **Where starts are taken:** `get`, `set` in the pair, cell and struct forms, `findobj`'s
  name-value filters (`findobj(f, 'Typ', 'axes')`), and the creating functions' pairs (`plot(…,
  'LineW', 2)`, `text(…, 'FontSi', 14)`, `figure('Nam', …)`, `axes('XLi', …)`, `uicontrol('Str',
  …)`). `plot`'s own option parser (`SplitPlotOptions`) reads a word as a start of a plot option
  when it is at least two characters and not a line-spec word.
- **Where whole names only:** the dot, `isprop`, `findobj`'s `-property` — as R2025b's.
- **The recorded names grew a third column.** `graphics_class_names.m` now prints whether each name
  can be set; `gen_graphics_class_names.ps1` marks a listed-but-read-only name `^` beside the
  hidden `~`, and `R2025bNames.ReadOnly` answers it. A uicontrol, uipanel or uimenu, whose R2025b
  names were not recorded, takes the one listed name the text begins, as the app components do (U1).
- **The JGS dialect is unchanged:** whole names only.

Fixture `oi_partial_names` (135 rows, one divergence) pins the forms, the refusals and the ranks;
`PartialPropertyNameTests` (11 tests) runs the same through the engine and `TryPartialName` directly.

### 119: a score per band, measured edges, the caps beyond

`TickRule` now chooses a linear step by R2025b's score on the shorter rulers and keeps the batch 18
caps, re-measured, on the longer ones (`TickScore`, `TickBudget`):

- **The score.** Each candidate step (1, 2 and 5 times a power of ten) scores the coverage of the
  range by its ticks, less a penalty per tick under a target and a larger one per tick over it, plus
  an allowance when both limits land on ticks; the best wins, a tie going to the finer step. A ruler
  across the page runs through four bands as it lengthens (targets 3, 3.5, 4.5, 5.5), one up the page
  through seven (3 to 9), the vertical bands coming sooner because labels stack by their height.
  The parameters were fitted per band over the 181 ranges and chosen from the jointly feasible set
  (`eval119.py`): every band but the first reproduces 99 to 100 per cent of the sweep.
- **The band edges by font.** At a written 10 points the x bands end at 110, 170, 210 and 290
  pixels, the y bands at 65, 90, 110, 130, 155, 205 and 230. The edges do not scale with the font:
  at a written 8 the x edges sit at 105, 145, 175 and 215 and the y edges hardly move; at 13 the x
  edges are 175 and 235 (the sweep ended at 260), the y edges 105, 145, 175, 215, 255. The tables
  hold the three measured fonts, interpolate between them, hold under 8 and scale past 13.
- **The caps beyond.** From the last scored edge the batch 18 caps apply, re-measured at every five
  pixels: across the page (8, 7) to 337, (8, 8) to 382, (10, 9) to 432, (11, 10) to 477, (12, 11)
  past it; up the page (10, 9) to 242, (11, 10) to 267, (12, 11) past it. Each band reproduces every
  range but the two or three where R2025b's own double arithmetic reads a limit as off its tick.
- **The automatic font.** probe_119d answered the same at every square size with the automatic font
  as at a written 10 — within the scored bands. The capped region does follow the shrunk font: a
  255-pixel square axes at its 9.5 points takes eleven ticks on `[0 1]` where a 255-pixel ruler at a
  written 10 takes six. So `AxesModel.BudgetFor` hands the budget the nominal font (the drawn one
  divided by `AutoFontScale`) for the scored edges and the drawn font for the caps; a written font
  is both. The colorbar keeps reading its length at nine tenths of the axes font (ADR 0232).
- **Exact arithmetic past the target.** A candidate is counted tolerantly (a limit a hair short of
  a tick still counts it); one that exceeds its cap, or on a scored band its whole target, is counted
  again in R2025b's double arithmetic, where 3 × 0.1 overshoots 0.3, and is then drawn by that
  count: `[0 0.3]` on a 60-pixel ruler is drawn `0 0.1 0.2`, on a 120-pixel one to 0.3, and
  `[0 0.7]` on a 140-pixel y ruler in seven tenths without the 0.7. `ChooseStep` reports the exact
  reading and `LinearTickGenerator` draws by it.
- **Aligned limits** (`tickaligned`) fit under the free cap with the landing allowance withdrawn,
  each limit moved out to its tick when within a quarter of the range, the step given up for a
  coarser one when the moved limits would hold more than the band's fit cap, the coarser step moving
  the data's own limits afresh: `[-1 1000]` ends at `[-200 1000]` in steps of 200 on the default x
  ruler; `[-3 11]` on a 60-pixel ruler stays `[-3 11]` with ticks `0 5 10`, on a 120-pixel one
  becomes `[-5 11]`.
- **The polar r ruler** is size-independent (21 radii from 100 to 600 pixels answered alike) and
  takes the score (5, 0.40, 0.68, 0.15), which reproduces 81 per cent of its 120 ranges.

Fixture `oi_short_rulers` (926 rows, 28 divergences) covers the square axes through the bands, a
tall axes through the upper y bands, slid ranges, written fonts, automatic limits and the polar
ruler at three radii; `TickRuleBandTests` (77 cases) pins the band lookup, the per-font edges, the
caps against the drawn font, each band's answers, the exact recount, the polar ruler and the aligned
limits. The refit lifted 37 divergence rows of `oi_patch_rows_axes_font_short_rulers` and the
`subplot` row of `oi_ticks_rule` (batch 19's short-ruler rows now agree).

## Consequences

- `JgsGraphicsProperties.PartialNames.cs` (new): `TryPartialName`, `WholeName`, `NeverAbbreviated`;
  `Get`, `Set` and `OptionsOf` resolve a name through `WholeName` unless reached through the dot;
  `isprop` reads with `dot: true`; `findobj`'s `Matches` and `set`'s struct form take starts;
  `SplitPlotOptions` / `ApplyPlotOptions` take a start of a plot option in the MATLAB dialect and
  route the rest to `JgsGraphicsProperties.Set` for R2025b's refusal.
- `R2025bNames.ByClass` keys strip `^` as well as `~`; `R2025bNames.ReadOnlyByClass` / `ReadOnly`.
  `graphics_class_names.txt` re-recorded with the settable column (UTF-8 with BOM: the generator's
  redirection writes UTF-16 when left to itself).
- `TickRule`: `TickScore` (target, under, over, landing, caps, fit cap), `TickBudget.Score`,
  `HorizontalScores`/`VerticalScores` with `HorizontalEdges`/`VerticalEdges` by font and
  `HorizontalCaps`/`VerticalCaps`; `ScoreFor(pixels, font, vertical, drawnFont)`; `ChooseStep` with
  the `exactly` overload; `Align` by the fit cap; `PolarRadius` as a score. `TickBudget.ForRuler`
  takes the drawn font; `AxesModel.BudgetFor` passes the nominal and the drawn one.
- `LinearTickGenerator` draws the majors by `CountWithin` or `CountExactly` as the rule read them.

## Divergences

- A start that only hidden names begin is refused here (`MATLAB:hg:InvalidProperty`) where R2025b
  answers one of them: `get(ax, 'UIContextM')` is an empty `GraphicsPlaceholder` there (row
  hidden_only). The hidden `…_I` and `…_IS` internals are not recorded at all.
- On a class written in MATLAB code (bar, histogram) R2025b lets a hidden name block a start
  (`'CDataM'` on a bar, `'BinCountsM'` on a histogram are refused there); JGraph resolves them to the
  listed Mode name, since its recorded lists do not carry those hidden names.
- The chart verbs that parse their own options through `OptionSpec` (stem, scatter, bar, …) keep
  whole option names; only `plot`'s parser and the generic property road take starts.
- On the shortest rulers (under 110 pixels across, 65 up) R2025b also reads the sign of the limits,
  taking more ticks when zero lies strictly inside a range than when it does not (`[-2 7]` is
  `-2 0 2 4 6` there and `0 5` here), which no score of counts and coverage expresses; the first band
  reproduces 96 per cent of the ranges that start at zero and 72 to 87 per cent of the slid ones
  (rows sq60_*_m2_7, m0p3_0p9, 0p3_2p7, m15_85, 12345_12399, slide60_m3p875_1p125,
  slide60_m1p875_3p125).
- A shrunk 9-point font moves a band's edge where the model holds it: a 165-pixel y ruler at 9
  points scores as the 7 band (rows tall165_y_0_0p7, tall165_y_0p123_0p987) and a 217-pixel one
  reads its caps as a 241-pixel ruler would (sq217_y_0_0p15, marked under ADR 0232's wrapper in the
  batch 19 fixture).
- In R2025b's own double arithmetic `[0 0.55]` in twentieths and `[0 1.1]` in tenths do not land on
  their upper limit although the quotient is a whole number; it refuses the first on a 270-pixel y
  ruler (row tall270_y_0_0p55) and draws the second with eleven ticks. JGraph's `Lands` and
  `CountExactly` divide, so two or three ranges in 181 differ in the top band.
- A written 13 points at the edge of a band: `[0 0.7]` on a 180-pixel x ruler is `0 0.5` there
  (row fs13_x_0_0p7). The 13-point sweep ended at 260 pixels, so the later 13-point edges are the
  10-point ones scaled; the written-8 caps beyond 215 pixels are scaled likewise, unmeasured.
- The polar r ruler prefers or refuses a step by its leading digit in a way no score of counts
  expresses: `[0 21]` is `0 10 20` there and `0 5 10 15 20` here, `[0 55]` is `0 20 40`,
  `[12345 12399]` is `12360 12380` (rows polar*_0_21, 0_25, 0_55, 12345_12399 at every radius). The
  score reproduces 97 of its 120 ranges; a "nearest count" rule reaches 102 but loses `[0 1]`,
  `[0 5]`, `[0 10]` and `[0 50]`, the common ones.
- At a written 10 points two single widths answer out of band (x 230 and 235 pixels score as the
  third band inside the fourth; y 175 as the seventh inside the sixth); no fixture row sits there.
- A line's `'Col'` resolves to `Color` in both engines, an axes' is refused in both; the display
  rows of `oi_partial_names` compare the read against the dot rather than print R2025b's default
  values, since the defaults differ (ADR 0230, ADR 0231).
