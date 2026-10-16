# ADR 0236 — the tick rule a scale settles on, the event data a component's callback receives, a table's format words, and the knob's label ring: open items 49, 51, 59, 63 and 64

## Status

Accepted, 2026-10-15. The twenty-third batch of the open-items work
(`docs/plans/open_task_chips_09_12_2026.md`, gitignored): the five items the 2026-10-07 triage
held back for a MATLAB window session, run with the user's leave. The recordings are committed
beside their probes: headless, `u5_ticks2` (855 slider rows over 68 ranges and 17 track lengths —
run twice, byte-identical, so the odd answers are R2025b's own and stable) and `u9_settle` (tick
counts over sizes, outer rectangles at a fixed Position, the aspect coercion); in a window,
`u5w_events`, `u8w_events`, `u9w_events` and `u9w_tree` (the App Testing Framework's gestures
where they reach — they refuse trees, the colour picker, a table's headers and its check-box
cells — and `java.awt.Robot` where they do not), with the pictures `u8w_formats.png`,
`u8w_tab.png`, `u9w_looks.png` and `u9w_palette.png`. Everything lands in the MATLAB dialect.

## Context

- **49.** A `uislider`'s automatic `MajorTicks` agreed with R2025b in 31 of the 34 ranges then
  recorded; the chip asked for more ranges at more widths before the rule is changed.
- **51.** The event data a `uifigure` component's callback receives had been written from
  metaclass dumps and documentation: no component can be driven headless.
- **59.** A `uitable`'s `CellEditData` after bad text, the order round a selection, a
  `MenuSelectedFcn`'s event, a toggle tool's order, the text each `ColumnFormat` word writes, and
  a classic figure's tab rectangle were likewise unmeasured.
- **63.** None of the U9 callbacks (knob, switch, pickers, trees, `ContextMenuOpeningFcn`) had
  run in R2025b.
- **64.** A fresh knob answered `[0 20 40 60 80 100]` and re-ticked once drawn; this build kept
  the unsettled ticks and its own outer rectangle (ADR 0207).

## Decision

### 49 and 64: one tick engine, each scale's own track

The slider's rule is unchanged in shape — the most equal intervals, each at least 18 pixels,
every interior tick a four-digit number, adjacent labels' half-widths and a 2-pixel gap fitting
the interval — and now measured against 855 recorded rows, of which it reproduces 631. What it
misses are walks R2025b's view takes with a rounded step and a clamped end (`[1 12]` at the
default width is `1, 2.5, …, 10, 12`) and a handful of steps nothing reproduces (`0.1425` for
`[0 1]` at width 200; `[0 0.5]` has fewer ticks at width 400 than at 300). Four hypothesis
families were fitted against the corpus — pairwise-fit ladders, dividing-ladder walks with a
rounded-step fallback, hybrids — and every one scored below the standing rule, so it stands, and
the corpus is pinned by `ScaleTickCorpusTests` so a change that loses ground is seen.

A knob and a gauge run the same engine over their own track: the freak values match to the digit
(a knob's `[0 1]` settles on the same `0.1425` as a slider 200 pixels wide), so `RefreshTicks`
now feeds `AutoMajorTicks` an effective track — a knob's three-quarter label ring
(`1.5π(d/2 + 12)`), a ninety-degree gauge's `0.88s`, a semicircular's `0.9s`, a linear's
`1.15s`, a circular's `1.475s − 30.5`, fitted against the size sweeps — and only the minor
density stays per kind. A fresh scale is re-ticked as it is made: the stale defaults R2025b
keeps until its view answers are a transient a synchronous engine does not have, and the settled
values are what its window shows and what a script reads after a `drawnow`. The corpus counts:
sliders 631 of 855, knob 41 of 67, circular 46, linear 47, ninety 54, semi 48 of 60 each
(the rest are recorded divergences; fixture `oi_scale_settled_ticks`).

### 64: the ring, and the silent aspect

`OuterPosition` of a knob is the hull of its dial and its labels, each label just outside an
eleven-pixel stand-off along its own angle (225° round to −45°); a discrete knob's items ring
the top half (180° to 0°), so it never reaches below its dial, as R2025b's measured boxes have
it. The sides agree with `u9_settle`'s recordings to within a pixel; the vertical reach sits
about five pixels shy, a recorded divergence. A `Position` write with the wrong aspect was
already coerced to the largest true-shaped box inside it; both probes show R2025b does it with
no warning headless or in a window, so the one this build never raised is no longer a divergence.

### 51, 59 and 63: the event data as the window sessions recorded it

- `ValueChangedData` carries, after `EventName`, the places of the value among the items: a
  list's and a drop-down's `PreviousValueIndex` then `ValueIndex` (a drop-down's `Edited`
  between them, true for typed text, whose index is empty when it names no item); a discrete
  knob's and a switch's `ValueIndex` then `PreviousValueIndex`.
- A click's `InteractionInformation` is typed — `ListBoxInteraction`, `DropDownInteraction`,
  `TableInteraction`, `TreeInteraction` — with the item hit and the click's place in the
  figure's pixels and on the screen (the figure's corner plus the place). The window layers now
  pass the pointer's place with every clicked raise.
- A list's `ClickedFcn` runs on the press, before `ValueChangedFcn`; a drop-down's second click,
  on an item of its open list, is told after the value settles.
- A spinner says the value is changing, then that it changed, for an arrow press as for typing;
  the typed text is told as the number it reads as, and text that is no number never reaches the
  callback. A numeric edit field has no `ValueChangingFcn`: the dot write refuses with
  `MATLAB:noPublicFieldForClass` and `set` with `MATLAB:hg:InvalidProperty`, R2025b's own split.
- A table edit stores `str2double` of bad text — `NaN`, with an empty `Error` — and is followed
  by `DisplayDataChangedFcn` naming the interaction `'edit'` and the column; `DisplayRowName` is
  the word `'numbered'` itself while the rows are numbered by default. The selection order is
  `SelectionChangedFcn`, `CellSelectionCallback`, `ClickedFcn`; a row-header click reports
  `RowHeader` true with an empty column.
- A menu bar entry's event is an `ActionData` named `MenuSelected` (the parent's own
  `MenuSelectedFcn` runs as its menu opens, which this build already did); a context menu
  entry's is a `MenuSelectedData` carrying the right-clicked object and a
  `ComponentInteraction`, and `ContextMenuOpeningData` carries the same pair — the context menu
  model keeps its last opening for the entries' sake, whether or not anyone hears the opening.
- A toolbar tool's events are `ActionData` named `ToolClicked`, `On` and `Off`.
- A tree's check-box click selects the node too, and the tick is told last (`SelectionChanged`,
  `Clicked`, `CheckedNodesChanged`); ticking a branch checks it with everything under it, which
  this build already did.

### 59: the format words, and the classic tab

R2025b's `uifigure` table writes only `short`, `long`, `bank`, `+`, `rat` and `char`
distinctly: the `e`, `g` and `eng` words all render as `short` does (`u8w_formats.png`), and
`NumberText` now follows — `short` as the scalar display (fixed only strictly between 0.001 and
1000), `long` to fifteen decimals (fixed strictly between 0.001 and 100), `bank` two decimals
but a bare `0` for zero, `rat` a continued fraction within an absolute millionth or a `*` past
ten characters, `char` as `%.14g`. A number under the `logical` word is a clear check box
whatever its value. The classic figure's tab rectangle measured from its frame — a 25-pixel top
chrome and one-pixel borders — is what this build already draws, so item 59's last question
closes with no change.

## Consequences

- `JgsGraphicsProperties.AutoMajorTicks` is the one tick engine; `RefreshTicks` maps each scale
  through `EffectiveTrack`, and a fresh scale is re-ticked in `AddUiComponent`.
- `UiRing.Hull` places a label ring for both knobs; `UiKnobModel.OuterOf` and
  `UiDiscreteKnobModel.OuterOf` use it, so grid `'fit'` cells take the new boxes too.
- `JgsUiEventData.Make` takes trailing fields; `JgsUiComponentEvents` builds the indices and the
  typed interactions; `NotifyComponent` carries a click's place; the WPF layers raise clicks
  with it, the list on the press, the spinner twice.
- Tests: `ScaleTickCorpusTests` (the corpus floors), `OpenItemsBatch23Tests` (46 tests),
  `UiTablesTabsU8Tests` and `MatlabUiMenuTests` updated to the recorded event names and shapes.
- Fixture `oi_scale_settled_ticks` (28 rows, 11 divergences stamped).

## Divergences

- The rounded-step walks R2025b's view takes where no equal division fits, and its handful of
  irreproducible steps, stay: 224 of 855 slider rows, and the scales' shares above
  (`ScaleTickCorpusTests` prints the counts; `sl_1_12`, `sl_1e6`, `sl_099`, `kn_1000`, `kn_big`,
  `kn_01` in the fixture).
- R2025b's minor-tick density varies with the major step (three, five or eight per interval);
  this build keeps five per interval for a slider, two for a knob, ten for a circular gauge
  (`sl_255`, `sl_1000`).
- The knob's outer rectangle agrees with R2025b's sides to a pixel and sits about five pixels
  shy vertically; a switch's differs by up to three pixels (`kn_outer`, `dk_outer`, `sw_outer`).
- A script that reads a fresh scale's ticks before R2025b's view has settled sees its stale
  defaults (`[0 20 40 60 80 100]` whatever the limits); this build answers the settled values at
  once. The fixture reads after a four-second settle, where both engines agree.
- The uitest gestures refuse a range slider's drag, a date picker's `choose` and the colour
  picker entirely, and the colour picker's `ValueChangedFcn` never fired in the Robot session
  (the palette picked the colour without it), so a colour pick's event data stays unmeasured;
  this build keeps `ValueChangedData` with the colour before and after.
- `class` of a component handle in an event's `ContextObject` answers `double`, as handles are
  numbers here (ADR 0051).
