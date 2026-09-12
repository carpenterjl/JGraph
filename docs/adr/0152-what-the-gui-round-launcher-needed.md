# ADR 0152 — What the GUI-round launcher needed

## Status

Accepted. A follow-up in the shape of ADR 0150, not a milestone: seven repairs found while writing
`JGraph_demo_workspace\head2head_GUI\jgraph\launch.m` and `generate_results.m`, each measured
against R2025b before it was made, and one gap that would not reproduce.

## Context

The head2head round that keeps both applications open needs a launcher that lists its scripts,
times them, and draws the comparison. Writing it turned up eight places where R2025b answers and
this build does not. Seven are plain defects; the eighth is described under *What did not
reproduce*.

- `h.list = dir('*.m'); c = {h.list.name}` yielded a one-element cell. `{s.name}` had worked since
  M61, and so had `s(2:3).name` — the two forms the comma-separated list knew were written as a
  bare name and as a subscript of one. A field reached through another field was neither, so the
  list never spread and `sort({h.list.name})` then refused a cell where it wanted an array.
- `datestr(now, 'yyyy-mm-dd HH:MM:SS')` printed `2026-22-12 09:09:71`: the minutes where the month
  should be, and the seconds wrong with them. `datestr` predates `datetime` and reads a different
  language from it — lower-case `mm` is the month there and upper-case `MM` the minute, where
  `datetime`'s tokens (Unicode's, which .NET shares) have it the other way round. M64 had routed
  both names through one translation into .NET's tokens, which is a translation that cannot exist:
  `QQ` has no counterpart there, and `HH` means two different hours depending on whether an AM/PM
  marker appears elsewhere in the same format. The numbered formats — `datestr(now, 31)` — were
  refused outright.
- `close all` without a semicolon printed `ans = 1`. R2025b's `close` answers whether the figures
  went only when an output is asked for.
- `C = {}; C{2,1} = 'a'` was refused. Growth was the linear form's alone, and even there a column
  cell grew into a row.
- Grouped `bar(1:3, [1 2; 3 4; 5 6], 0.8)` put the two series at ±0.2 where R2025b puts them at
  ±0.1429 and draws narrower bars.
- `bar(1, 0.5, 0.6, 'FaceColor', [0 0 1], 'EdgeColor', 'none')` was refused: the trailing scalar was
  read as a bar width only when the value before it was not a scalar, which is the right question
  for two leading numbers and the wrong one for three. `'none'` was not a colour `bar` knew either.
- `categorical([1 1 2]', [1 2], {'A', 'B'})` was refused: `categorical` took one argument.

## Decision

### A comma-separated list follows the whole path, not its last step

`EvaluateSpread` asked whether the target was a bare name holding a struct array, and separately
whether it was a subscript of one. Both were spellings of one restriction — *asking the question
must not run a call* — said in terms of the last step of the path. It is now said in terms of the
whole path: the list spreads when the root of the path is a **variable already holding a struct or
a cell**. A path rooted in a variable names stored values and contains no call, so evaluating it to
find out what it is cannot run anything. `s.field`, `s(2:3).field`, `h.list.field`, `q.inner.list.field`
and `k{1}.field` are all the same question, and the two old branches and their `SubscriptedName`
helper are gone. A root that is a class instance is deliberately left out: a property read there is
a call in all but spelling.

The path is read once. `EvaluateMember`'s body past the target read is now `MemberOf`, so when the
path turns out to name a single struct after all — `h.list(2).b` — the dot finishes on the value in
hand rather than sending the path round again, which would run a subscript's own call twice.

### `datestr` reads `datestr`'s language

`JgsTime.FormatDatestr` walks MATLAB's legacy tokens directly: `yyyy`/`yy` year, `QQ` quarter,
`mmmm`/`mmm`/`mm`/`m` month, `dddd`/`ddd`/`dd`/`d` weekday and day, `HH` hour, `MM` minute, `SS`
second, `FFF` fraction, `AM`/`PM` the marker, and anything else written out. `HH` is the 24-hour
clock on its own and the 12-hour clock right-aligned in two columns when the format carries a
marker, so `'HH:MM PM'` is `" 2:35 PM"` and `'HH:MM'` is `"14:35"`; the marker prints upper-case
however it was spelled. The single-letter `m` and `d` are the first letters of the month and
weekday names. `JgsTime.DatestrPattern` holds the thirty-two numbered formats, recorded from R2025b
for an afternoon moment in the third quarter, which tells every one of them apart; `-1` is the
default and a number outside the table is refused by name.

One builtin now serves both kinds of input: a `datetime` and a serial date number reach the same
formatter as serial date numbers, because R2025b reads one language for both — `datestr(t, 'MM')`
on a datetime is the minute. Several serial numbers answer as a char matrix, as several datetimes
already did. The bare call answers `dd-mmm-yyyy` when every moment is midnight and
`dd-mmm-yyyy HH:MM:SS` otherwise.

### `close` is a silent builtin

`DefineSilent`, which `figure` and the rest of the statement verbs already use: the value is still
there for `went = close('all')`, and a bare `close all` binds and echoes nothing.

### A cell grows by either brace form

`C{r, c} = v` past an edge grows to the rectangle enclosing what was there and what was named, each
element staying where its own subscripts put it. `C{k} = v` grows along the dimension the cell
already runs in — a column stays a column, a row or an empty becomes a row — and a cell that is
neither refuses with MATLAB's own words, *Attempt to grow array along ambiguous dimension.* New
slots hold `[]`. Growth still needs a rebindable name, so a cell reached through a field keeps the
in-range write it has always had and now says why it will not grow rather than reporting a bad
index.

The two-subscript write no longer goes through `BraceSlots` when both subscripts name one slot,
which is also what keeps a subscript with a side effect from running twice.

### Grouped bars stand where R2025b puts them

Measured across one to ten series and three position spacings: with `n` series a group spans
`min(0.8, n / (n + 1.5))` of the gap between positions, the series centres are spaced evenly across
that span, and each bar is `BarWidth` of its share of it. A single series takes the whole gap, so
its bar is `BarWidth` of it, which is what this build already drew. `BarPlot` gains `GroupSpan` and
`SeriesPitch`, and what used to be one `SlotHalfWidth` serving both the spacing and the width is
now the two numbers R2025b keeps apart. The chart's reach along the category axis is the group's,
so the axes enclose the outermost bar whichever series is asked.

### `bar` takes a width and then pairs

Three leading numbers can only be `bar(x, y, width)`, whatever follows; two alone stay `bar(x, y)`,
which is the single bar `bar(1, 0.5)` draws. `FaceColor` and `EdgeColor` accept `'none'`, which a
bar draws by having nothing to draw with — no paint inside, no stroke around.

### `categorical` takes a value set

`categorical(values, valueset)` and `categorical(values, valueset, names)`: a value standing in the
set takes the name at its place there, and one that does not becomes `<undefined>`, which is what
R2025b's display and its `cellstr` call it. Without the third argument the set names itself. A
categorical here is still its cell of names (there is no categorical type), so matching is on the
text each value is written as — one conversion rather than one kind of comparison per kind of value
set. The cell now carries the values' shape, so `categorical([1 1 2]')` is 3-by-1 as it is there.

## Consequences

`adr0152_gui_round_gaps.m` pins **76 lines** recorded from R2025b, every one agreeing by its rule
but the one divergence below. `GuiRoundGapsTests` covers what a fixture cannot see: that `close all`
reaches the console silently while `went = close('all')` still answers, that a subscript in a
spread path runs once, what each new refusal says, and the bar geometry in data units — the numbers
there are R2025b's own drawn bar edges, read off the patch with `h.Face.VertexData`.

Three tests changed because the behaviour they pinned was the defect: `datestr(datenum(2020,1,1))`
is `01-Jan-2020`, `datestr(737791, 'yyyy-MM-dd')` is `2020-00-01`, and `datestr(t, 'uuuu/MM/dd')` is
`uuuu/30/05` — all three measured in R2025b. The grouped-bar layout test now carries R2025b's
offsets. The catalogue entries for `bar`, `categorical` and `datestr` say what the new forms are;
no builtin name was added, so the four coverage documents are unchanged.

### Divergences

- **A grown cell's new slots are 1-by-0 where MATLAB's are 0-by-0.** `cell(2, 2)`'s own fill has
  read 1-by-0 since long before this, so growth fills the way the constructor does; both are `[]`
  and both are empty. Recorded as `cell_grow_fill_shape`.
- **A cell reached through a field will not grow**, by either brace form, where MATLAB grows it.
  The refusal names the reason. Growth rebinds a name, and a field read hands back the stored cell
  rather than somewhere to rebind.
- **A field of a struct array returned by a call does not spread** — `{makeStruct().name}` is one
  element — because the path must be rooted in a variable for reading it to be free.
- **`categorical` has no `'Ordinal'` or `'Protected'` option**, and there is no `categories`,
  `isordinal` or `iscategorical`: a categorical here is a cell of names and always has been.
- **`datestr` no longer reads .NET or `datetime` tokens.** `'uuuu-MM-dd'` is four literal letters
  and a minute, which is what R2025b answers; a script here that wrote a .NET format for `datestr`
  must write `datestr`'s own. `datetime`'s own display format is untouched.

### What did not reproduce

`boxchart(ones(3,1), y1, 'BoxFaceColor', blue); hold on; boxchart(2*ones(3,1), y2, 'BoxFaceColor',
orange)` was reported to draw the first box black. Run under `-batch` at this tree — alone, with
`'MarkerColor'` beside it, inside a `subplot`, and across a grid of eight subplots — both boxes are
drawn in the colours given, and `b1.BoxFaceColor` reads back what was set. It is left open rather
than closed: the report came from the windowed application, which this unit did not run.
