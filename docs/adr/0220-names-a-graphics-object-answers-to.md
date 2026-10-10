# ADR 0220 — The names and words a graphics object answers to, far datetimes, and calls named before their class: open items 28, 41, 66, 82, 84, 85 and 89

## Status

Accepted, 2026-10-09. The ninth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored). Every answer was recorded from R2025b (probes `probe_82` to `probe_82i`, `probe_41` to
`probe_41c`, `probe_66`, `probe_66b` and `probe_28` to `probe_28f` in the open-items scratch, then the
fixtures `oi_property_names`, `oi_set_words`, `oi_datetime_reach` and `oi_call_forms`).

## Context

- **Item 82.** Reflection puts every browsable model property in an object's property table (ADR
  0051's bridge), so a MATLAB script saw this build's own names: the plot browser's `Name`, `ZOrder`,
  `Opacity`, `Selectable`, the colour-mapping knobs, a text's `Bold` and `Padding`. Some hand-written
  spellings were given to every object of a model when R2025b gives them to one kind: every axes
  answered the polar `RLim`, `ThetaDir` and the rest, and every line `RData` and `ThetaData`. A sweep
  of `get(h)` over 19 kinds of object found 336 names R2025b does not list. Of those, R2025b answers
  `isprop` 1 for a few it keeps hidden (`UIContextMenu` on everything, a figure's `HitTest`,
  `Selected` and `SelectionHighlight`, a text's `Text`, a panel's `ShadowColor` and `ResizeFcn`,
  a uiaxes' `BackgroundColor`) and refuses the rest with `MATLAB:hg:InvalidProperty`. A new
  figure's `Name` read `'Figure'`, the model's name for the plot browser; R2025b's is `''`.
- **Item 85.** The BatchRunner test that loses its figure was not losing it to a class that names
  `JG`: every one of those is in the `JG facade` collection. `JgsRunner.Run` resets the facade and
  clears the handle registry at the start of every top-level run, and six device test classes ran
  scripts through it outside the collection; a seventh built a JGS session there, which installs
  the process's callback dispatcher.
- **Item 28.** The item said R2025b asks an anonymous function's body for one output when it is
  called for none. It does not: `@() declares_unset()` called for nothing runs quietly, which it
  could not if an output were asked for. The case that found the item, `audiodevinfo(1,2,3,4,5,6)`,
  is refused as a plain statement too, because `audiodevinfo.m` asks its helper for `devInfo`
  however it was called. Probing the item's edge cases found two other faults: a nested `evalc`
  was refused ("it does not nest"), and `areaOf(OiArea())` failed when it was the first line to name
  `OiArea`.
- **Item 84.** R2025b has removed `pagesetupdlg`; the user decided (2026-10-09) that JGraph keeps it,
  with its page setup window, as an extension.
- **Item 89.** `hggroup(ax)` was refused; R2025b takes a parent first.
- **Item 41.** `set(h)` of anything but a component answered a cell of names, and `set(h, name)` was
  refused as a missing value. R2025b answers a struct of the writable names, each holding a column
  cell of the words the property takes: `{'linear'; 'log'}` for `XScale`, the two states for an
  on/off property, a 0-by-1 cell for a colour and a 0-by-0 one for anything else.
- **Item 66.** A datetime outside .NET's years (0, before 0, 10000 and on) could be built and
  printed since U9, but every part read through a .NET `DateTime` and failed with an internal error:
  `.Year`, `year`, `ymd`, `datevec`, `quarter`, `dateshift`, writing `d.Year`. And a concatenation
  told a format a script set from a default one by comparing the text, so `d.Format = 'dd-MMM-uuuu'`
  written by hand was worked out again from the moments. R2025b keeps a set format through a
  concatenation (when it is the first piece's) and through a write, `'default'` returns to the
  worked-out default, and `''` is refused.

## Decision

### R2025b's names, and only those, in the MATLAB dialect's lists (item 82)

`tools/matlab-checklist/graphics-probes/graphics_class_names.m` records, for 44 graphics classes,
every property with public get access and whether it is hidden, read from each class's `metaclass`,
and what `get` lists for a line and a scatter in polar axes (their polar names are dynamic, so
`metaclass` does not see them). `gen_graphics_class_names.ps1` writes them to
`JgsGraphicsProperties.R2025bNames.cs`, generated and not edited by hand.

`JgsGraphicsProperties.R2025bClassesOf` reads an object as R2025b's classes, as `TypeNameOf` reads
it, and answers every class a model stands for: a `LinePlot` is a `Line` and also what
`animatedline`, `fplot` and `fimplicit` make, a `PatchPlot` is also a `Rectangle`, and a line or
scatter in polar axes adds the polar names. In the MATLAB dialect, through `TryFind` and `NamesOf`,
a property is:
- **shown** when one of those classes defines it and does not hide it;
- **hidden** when they define it only as hidden, or do not define it at all: it answers when named
  and `get(h)`/`set(h)` leave it out;
- **absent** only for the plot browser's `Name` on anything but a figure: `isprop` answers 0 and
  `get`/`set` refuse it.

The first build refused every name R2025b lacks. Eleven stress scripts and about sixty tests failed:
they use JGraph's extras in MATLAB scripts on purpose (`ThetaDirection` on polar axes, a plain axes'
`BackgroundColor`, `Opacity`, a histogram's `BarWidth`, `YAxisIndex`, a box chart's `MedianValues`).
The user decided (2026-10-09) that the extras stay and drop out of the lists, as for `pagesetupdlg`
(item 84), and that `Name` is refused, because it is no extra but the model's label and GUI code
tells a figure from anything else by `isprop(h, 'Name')`.

An object whose classes were not recorded (a component, whose surface was curated name by name since
U1; a 3-D bar; a rectangle annotation; a tiled layout) keeps every name. A JGS script keeps every
name everywhere.

A name nothing answers is refused in R2025b's words for every recorded class:
`MATLAB:hg:InvalidProperty`, "Unrecognized property Bogus for class matlab.graphics.chart.primitive.Line."
when reading and "... for class Line." when writing, as components already were (U1, U2).

A panel's `ResizeFcn` and `ShadowColor` are unlisted in the curated panel table. A figure's `Name`
reads the model's "Figure" as `''` in the MATLAB dialect, the reading the figure window already
gives it.

### The JG facade's own collection (item 85)

The seven device test classes join the `JG facade` collection, and `FacadeCollectionGuardTests`
reads the test sources: a class that calls `JgsRunner.Run`, `JG.Reset`, `JgsHandleRegistry.Clear`,
`BatchRunner.Run…` or builds a JGS session must be in the collection.

### Calls (item 28)

- `audiodevinfo` with six arguments is `MATLAB:unassignedOutputs` whatever it was asked for.
- `evalc` nests. Each one captures into a buffer of its own, and an outer one sees only what the inner
  did not claim (`JGraphScriptGlobals.BeginCapture` keeps the outer buffers on a stack).
- A name nothing holds, called while no class is loaded, evaluates its arguments to name the first
  one's class in the refusal (ADR 0214). If evaluating them loaded the session's first class, the
  name is now looked for among that class's methods before it is refused.

An anonymous function called for nothing still calls its body for nothing, as before; the fixture
pins the forms the item named (a function that declares an output it never sets, `varargout` with
nothing and with one value, a nested anonymous function).

### Groups (item 89)

`hggroup` and `hgtransform` take an axes or a group first, or as `'Parent'`, and their `Parent` reads
back as that axes or group (`JgsGraphicsGroup.Outer`, `ParentOf`); a group is still beside the render
tree. `polaraxes` takes a figure or a panel first and makes a new polar axes there, as `axes(parent)`
does, which `oi_property_names` needed for `polaraxes(figure(…))`.

### `set(h)`'s words (item 41)

`graphics_class_names.m` also records `set(h)` of each class: the words of every property that takes
some, and which take a colour's 0-by-1 nothing. In the MATLAB dialect an object whose classes were
recorded answers `set(h)` and `set(h, name)` as a component does since U3 (`OptionsOf`), with its
property's own words where the table gives some, R2025b's recorded ones otherwise. A read-only name is
`MATLAB:class:SetProhibited`, an unknown one `MATLAB:hg:InvalidProperty`.

### Far datetimes and a format a script set (item 66)

`JgsTime.WallClock(ms, tag, out cycles)` reads a moment whole four-hundred-year cycles on or back
into .NET's years. The Gregorian calendar repeats exactly every 146,097 days, a whole number of weeks,
so the month, day, weekday, week, day of the year and time of day are the moment's own, and its year
is `Year + 400 * cycles` (`JgsTime.YearOf`). Every part reader, `datevec`, `yyyymmdd`, `timeofday`,
`dateshift` and the part writers read through it, and `dateshift` moves its answer home by the same
cycles.

`JgsTime.ToDateTime` turns the whole milliseconds and the fraction into ticks apart: their product
was past where a double holds every tick for a moment near year 9999, and a far moment's whole
second read as 7.0000128.

Writing the fixture found two more readers wrong for every date, not only far ones. `week(t)` answered
ISO's week number; R2025b's is the week of the year, Sunday first, with week 1 holding the first of
January (31 December is often week 53). And `day` and `week` took no second word. Both now take
R2025b's kinds: `dayofmonth`, `dayofweek`, `iso-dayofweek`, `dayofyear`, `name`, `shortname`;
`weekofyear`, `weekofmonth`, `iso-weekofyear` and `iso-weekofmonth` (the week, Monday first, of the
month its Thursday falls in), refusing others in R2025b's words. `dateshift` works a default format out
again for the moments it shifted to.

`JgsTimeTag.FormatSet` says a script gave the format: `d.Format = …` and the constructor's `'Format'`.
A concatenation keeps the first piece's set format and otherwise works the default out again from
every moment, whatever the later pieces show; a write leaves a set format alone. `d.Format = 'default'`
returns to the worked-out default, and `d.Format = ''` is `MATLAB:datetime:UnrecognizedFormat`.

## Measured

- `oi_property_names`: 30 lines, 4 stamped (JGraph's extras answering). `oi_set_words`: 148
  lines. `oi_datetime_reach`: 36 lines. `oi_call_forms`: 9 lines. The rest exact.
- The listing-count tests (M73, M75, M77, M78, M79, M84) and six stress scripts (26, 45, 47, 49, 51,
  56) count the names each kind shares with R2025b now; all 89 stress scripts pass.
- The `get(h)` listing of the 19 kinds in `probe_82` matches R2025b's name for name in the JGraph-only
  direction; `isprop` and `get` agree for all 336 names that were JGraph's own.

## Divergences

- **JGraph's own graphics names answer in a MATLAB script** (`isprop(ax, 'ZOrder')` is 1,
  `get(ln, 'Opacity')` reads), where R2025b refuses them; they are left out of `get(h)` and
  `set(h)`. Decided by the user.
- **A misspelt name on a recorded class is refused in R2025b's sentence**, without the "Did you
  mean …?" JGraph gave before, as for components since U1.
- **R2025b's partial names are not taken.** `get(ax, 'Pos')` and `set(ln, 'LineW', 3)` work in R2025b
  and are refused here (open item 86, which records the cases a shortest-start rule gets wrong).
- **A figure named 'Figure' by hand reads '' in the MATLAB dialect**, and `findobj` for an empty
  `Name` finds it, because the model's default name and that one are the same text.
- **An axes' `BubbleSizeLimits` and `BubbleSizeRange`** are `MATLAB:class:GetProhibited` in R2025b
  (they exist, without public get access) and `MATLAB:hg:InvalidProperty` here.
- **A dot on a graphics handle with a name nothing answers** is `MATLAB:hg:InvalidProperty` in the
  `get` sentence; R2025b's dot is `MATLAB:noSuchMethodOrField` (open item 68).
- **`pagesetupdlg`** is kept, with its page setup window, where R2025b has removed it
  (`MATLAB:pagesetupdlg:FunctionRemovedWeb`); decided by the user for item 84.
- **R2025b names JGraph does not answer** (61 in the sweep: a figure's `Theme`, an axes'
  `GridLineWidth`, a bar's labels) are open item 88, and `set(h)` leaves them out with them.
- **`set(h)`'s on/off words are text**, `{'on'; 'off'}`, where R2025b's are `OnOffSwitchState`
  values, as for components since U3.
- **A datetime picked out of a default-format array keeps the array's format** (`e = d(2)` shows the
  time of day the array shows); R2025b works the default out again for what was picked.
- **A zoned datetime outside .NET's years** reads its zone's offset as of the moment four hundred years
  on or back.

## Consequences

A MATLAB script that tests `isprop(h, 'Name')` before writing a figure-only property, or lists
`fieldnames(get(h))` to copy one object's settings to another, now sees what it would in R2025b,
and one that uses a JGraph extra by name (`set(ln, 'Opacity', 0.5)`) keeps working. Automatic limits and the default axes `Position`, found while probing, are
open item 87.
