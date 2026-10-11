# ADR 0224 — findprop and a deleted handle's class, the defaults each maker takes, Parent = [] everywhere, and the names whose values are worked out: open items 91, 93, 94 and 95

## Status

Accepted, 2026-10-11. The eleventh batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), the items batch 10 filed (ADRs 0222 and 0223). Every answer was recorded from R2025b
(probes `probe_91`, `probe_91b`, `probe_93`–`probe_93e`, `probe_94`, `probe_95`–`probe_95c` and
`probe_q` in the open-items scratch, then the fixture `oi_findprop_detach_computed`). Item 90, the
timing of `lu`, waits for a quiet machine.

## Context

- **Item 91.** There was no `findprop`, so `findprop(ln, 'Color')` and `ln.findprop('LineWidth')`
  were refused; R2025b answers a `matlab.graphics.internal.GraphicsMetaProperty` (a component's a
  `matlab.metadata.Property`), matching the name with its case, and an empty `matlab.metadata.Property`
  for a name it does not find. After `delete(ln)` a handle here was a number again, so `methods(ln)`
  and a dot on it were refused as for a number, where R2025b still lists a `Line`'s 22 methods, 50
  properties and 3 events and refuses the dot in its own words.
- **Item 93.** Batch 10 gave every default to every new object of its class. R2025b's high-level
  makers set some properties from the axes' orders over the defaults: `plot` and `line(x, y)` a line's
  `Color`, `LineStyle` and `Marker`, `stem` its colour, `bar` its face colour — so this build had
  `plot` taking `DefaultLineMarker` and `DefaultLineLineStyle`, a silent wrong answer — while the
  low-level `line('XData', …)` takes them all. `line` and `patch` refused the name-value-only forms, a
  title took no `DefaultTextColor`, `Stem`, `Scatter` and `Bar` defaults were refused as invalid
  class names, and a figure that `plot`, `axes`, `gcf`, `gca` or `uifigure` made took no
  `DefaultFigure*`. On the way: `patch(x, y, [0 1 0])` with three vertices read the colour as
  per-vertex data (R2025b reads a row as the colour, a column as data), and a stem left to the colour
  order read its `Color` as `'none'`.
- **Item 94.** `b.Parent = []` on a placed component was refused (ADR 0199), and `uimenu`,
  `uicontextmenu`, `uitoolbar`, `uipushtool`, `uitoggletool` and `uicontrol` refused `'Parent', []`
  (ADR 0206); R2025b detaches the one and makes the others belonging to nothing.
- **Item 95.** Fourteen of item 88's names were still missing: a figure's `Theme`, an axes', a polar
  axes' and a uiaxes' `InteractionOptions`, a group's and a transform's `Annotation`, `SourceTable` on
  lines, stems, stairs and box charts, a ruler's `Exponent` and `MinorTickValues`, a title's
  `Position` and `Extent`, and a quiver's `ScaleFactor`. Measuring the last found this build's quiver
  auto-scale was not MATLAB's, `quiver(…, s)` scaled by `0.9 s` where R2025b's `AutoScaleFactor`
  reads `s`, and `quiver(U, V)` put every arrow on y = 0, counted from 0 — a silent wrong answer.

## Decision

### findprop, and a deleted handle's class (item 91)

`findprop(obj, name)` answers, for a graphics handle, R2025b's metaproperty: its 26 fields in R2025b's
order (a component's 22), the name matched with its case, `SetAccess` `'protected'` for a read-only
property (`'private'` on the root), `Hidden` for a name the recorded `properties(h)` leaves out,
`SetObservable` from the recorded observable list, and a `DefiningClass` naming the object's class.
For an object of a user handle class it answers the property's own declaration. A name not found, or
a deleted handle, is the empty `matlab.metadata.Property`, with the fields, so that `.Name` on it is
empty. The registry keeps the R2025b class of each handle it drops (`JgsHandleRegistry.RetiredClassOf`,
forgotten with the run); `methods`, `properties`, `events`, `ismethod` and the printed listings answer
that class's recorded members, and a dot is refused as R2025b refuses it: a name the class has is
`MATLAB:class:InvalidHandle`, any other the dot's own sentence naming the class. `findprop` is in the
catalog, and dispatch sweep 13 measured it with the three other names added since sweep 12
(`fileread`, `feature`, `matlab.ui.layout.GridLayoutOptions`, shadowed through package folders), so
`verify-method-classes.py` passes again.

### Which maker takes which default (item 93)

A default is still given as an object joins its parent, but a line's `Color`, `LineStyle` and
`Marker`, a stem's `Color` and a bar's `FaceColor` are passed over unless the object comes from a
low-level form (`MakingLowLevel`): `line()` and `line('XData', x, 'YData', y, …)`, whose coordinates
are properties like any other and are R2025b's `[0 1]` when left out. `patch()` and `patch('XData',
…)` make the low-level patch (R2025b's triangle when left out). `Stem`, `Scatter` and `Bar` hold
defaults. `JG.FigureMade` tells the script layer of each figure made under a number, however it was
made, and a `uifigure` asks for the same, so every figure takes the root's `DefaultFigure*`. An axes'
title and a ruler's label take the text defaults when a script first reaches them, as R2025b makes
them: the title its colour, each its interpreter. With three vertices a 1-by-3 colour row is the
colour; a stem left to the order reads the colour it is drawn in.

### Parent = [] (item 94)

Writing `Parent = []` moves an object to where batch 10's parentless objects wait
(`JgsDetachedComponents`): a component, panel, grid or uicontrol to the hidden panel, an axes to the
hidden figure, a menu, context menu or toolbar to that figure's, and a tool to a hidden toolbar in
it; what it holds goes with it, its window loses it, and a `Parent` write places it again. The U8
makers take `'Parent', []` and make the object in the holder their parent kind allows; a tab, whose
holder would be a tab group, is still refused. A tree node keeps its refusal (not recorded).

### The values worked out (item 95)

`JgsGraphicsProperties.ComputedNames.cs`. A figure's `Theme` is a classed struct with R2025b's `Name`
and `BaseColorStyle`, writable with `'light'`, `'dark'` or a theme, which turns `ThemeMode` manual.
`InteractionOptions` is the classed struct of R2025b's fields and values (`LimitsDimensions` a string),
and a struct written back is kept, each field checked. A group's `Annotation` is the handle a line's
is, its `IconDisplayStyle` starting `'off'` as R2025b's does. `SourceTable` is R2025b's 0-by-0 table.
A ruler's `Exponent` is an `int32`; its `MinorTickValues` split each tick step in five, or four when
the step's leading digit is 2, inside the limits and leaving out the ticks, and on a decade log
ruler are 1 to 9 times each power of ten — R2025b's values to the bit. A written `Exponent` or
`MinorTickValues` is kept and turns its mode manual. A title is placed a quarter of its font size in
pixels above the plot box (below it under a reversed y), a ruler label beyond its tick labels, in data
or normalized units, and measured for `Extent`; a written `Position` is kept and turns `PositionMode`
manual. A flat quiver auto-scales as MATLAB's: the cell is the x span over the grid's columns by the
y span over its rows (a vector's a square of √n), and the longest arrow, measured in cells, is fitted
to `AutoScaleFactor`, which `quiver(…, s)` sets to `s`; `ScaleFactor` reads the result. `quiver(U, V)`
places each arrow at its column and row number.

## Divergences

- **A metaproperty's flags beyond its name, access, hiddenness and observability are this build's
  reading** (`findprop_line`): `Dependent` is whether it can be written, `GetObservable` follows
  `SetObservable`, `AbortSet` is true, `DefiningClass` names the object's class rather than the
  superclass that declares the property, and `DefaultValue`, `GetMethod`, `SetMethod` and
  `Validation` are `[]`.
- **A deleted handle's `class` is `'double'`**, as a live one's is (ADR 0051); its members and its
  dot are R2025b's.
- ~~**A dot on an empty struct array is `zeros(1,0)`, not a list of nothing**, so
  `x = findprop(h, 'Bogus').Name` assigns an empty where R2025b refuses for want of an output.~~ Lifted by ADR 0225.
- **A title's and a label's `Position` and `Extent` are worked out from this build's layout**, whose
  figure, plot box and font sizes are its own (`title_position`); a written `Position` does not move
  the drawn text.
- **A ruler's automatic `Exponent` is a rule fitted to R2025b's answers at twenty limits**, not its
  algorithm (~~and the tick labels do not factor it out~~, lifted by ADR 0225); `MinorTickValues` follow this build's ticks,
  which differ from R2025b's at some limits, and nothing draws a written one.
- **`Theme`, `InteractionOptions`, `SourceTable` and a group's `Annotation` are kept and read back but
  change nothing drawn or done**: every figure is drawn in the application's theme, the window's
  interactions do not read the options, and the legend lists a group's members, not the group. The
  options' on/off fields are `'on'`/`'off'` text, as every on/off property here is, where R2025b's
  are `OnOffSwitchState`.
- **A quiver's `XData` and `YData` from `quiver(U, V)` are the whole grid** where R2025b keeps the
  column and row numbers as vectors (`quiver_components_xdata`).
- **A title made before a default is set keeps its colour across a plot that resets the axes**;
  R2025b remakes it there, so it takes the default then.
- **A three-vertex patch given a 1-by-3 colour outside [0, 1] takes it as per-vertex data**, where
  R2025b refuses it as a bad RGB triplet.

## Consequences

`findprop(h, name)` checks such as `p.SetObservable` before an `addlistener` run;
a script that deletes a handle and asks it about itself gets R2025b's answers; `set(groot,
'DefaultLineMarker', 'o')` no longer marks every `plot`, while `line('XData', …)` takes it;
`set(groot, 'DefaultFigureColor', …)` reaches the figure a bare `plot` makes; App Designer's and
GUIDE's code that parks a component, menu or tool with `Parent = []` runs; `quiver` arrows have
MATLAB's lengths, and `quiver(U, V)` draws where MATLAB does. Tests: `OpenItemsBatch11Tests` (the
quiver scale in the model, the figure hook, a detached menu and tool, a deleted handle across runs);
fixture `oi_findprop_detach_computed` (80 lines, 2 stamped). The divergences and open items ADRs 0222
and 0223 recorded for these are lifted from them. Found on the way and filed: this build's `'g'` is
`[0 0.5 0]` where MATLAB's is `[0 1 0]`, and R2025b's tick labels factor out the exponent.

## Still open

- Item 90, the timing of `lu`'s fills, on a quiet machine.
- Item 92, the order of a component callback and its event's listeners, in a MATLAB window session.
