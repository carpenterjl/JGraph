# ADR 0051 — Handles on figure objects, and a table a script can subscript

## Status

Accepted (M51, 2026-08-03). Overturns the handle-value-type non-goal recorded in
`docs/matlab-builtin-coverage.md` since M37. Builds on [ADR 0029](0029-fully-editable-figure.md)
(the legend as a first-class object with per-entry rows), [ADR 0035](0035-interactive-console.md)
(the live session that outlives a run), and [ADR 0043](0043-shaped-arrays.md) (shaped arrays, which
is why a handle can be a number and still behave like a handle).

## Context

A user ran an ordinary MATLAB analysis script — read a burn-in log, group by serial number, draw two
linked subplots, click a legend entry to hide a part — and it failed on line 6. Working through the
whole script rather than just that line found two separate bodies of missing behaviour.

**A table could only be read one variable at a time.** `T.VAR` worked; `T{:,1}` and `T(1:5, :)` both
threw. Those are how MATLAB code pulls a column's contents out and takes a smaller table, and no
amount of `T.VAR` substitutes for them when the column is picked by number. `readtable` also could
not find the data block in the user's CSV, which opens with ninety-one lines of station banner and
two narrow summary tables before the real header — MATLAB detects that; JGraph read the whole file as
one ragged table. And `unique` refused a cell of char, which is exactly what a text variable comes
back as.

**There were no handles at all.** Every drawing verb was a command over the static `JG` facade: it
mutated the current figure and returned nothing. So `ax1 = subplot(2,1,1)` silently bound null,
`plot(ax1, x, y)` failed with a confusing type error, `p1.Color` had nowhere to look, and
`lgd.ItemHitFcn = @cb` could not even be stored. The coverage doc recorded this as a deliberate
non-goal, on the grounds that a figure is edited through the plot browser and inspector. That
reasoning holds for *editing* a figure by hand. It does not hold for a script that builds thirty-eight
series and needs to say which one it means.

## Decision

### A handle is a number

A handle is an ordinary `JgsType.Number` keyed into a static registry (`JgsHandleRegistry`), the way
MATLAB's own handles worked before its graphics objects arrived. The registry maps the number to a
kind (axes, line, legend), the model object, and the two pieces of state that belong to the script
rather than to the figure: whether the object asked to stay out of legends, and the callback a legend
was given.

The alternative was a new `JgsType.Handle`. It was rejected because of everything it would have to
re-learn. A script keeps handles in an array and grows it (`h(i) = p`), concatenates them
(`[ax1 ax2]`), compares them for identity (`h == clicked`), masks them, and gathers them out of a
struct array's field (`[rows.line]`). All of that already works for packed doubles, and
`StructArrayField` in particular returns a numeric row only when every value is a number — a handle
type would have made it return a cell and broken the comparison the user's callback depends on. As
numbers, none of that needed touching.

Handles are minted at `1_000_000.5` and step by one, so they are never confused with a figure number
or a loop counter, and dotting into a number that is not in the registry fails exactly as it did
before. They are runtime-only: the registry is cleared wherever `JG.Reset()` runs, and nothing about
handles reaches serialization, so `.graph` stays at v5.

### Verbs aim at a named axes without moving the current one

Every axes-facing verb peels a leading axes handle and runs against that axes with
`JG.MakeCurrent`, restoring the previous current axes afterwards. This matches MATLAB, where
`plot(ax, …)` draws into `ax` but does not make it `gca`. One helper does it for `title`, `xlabel`,
`ylabel`, `xlim`, `ylim`, `grid`, `hold`, `legend` and `plot` rather than each verb growing its own
overload.

### `graphics.primitive.Line.empty` is a struct, not a parser change

MATLAB's preallocation idiom is a dotted class path. Under the MATLAB dialect the globals now carry a
`graphics` variable that is a nest of structs ending in a builtin, so the existing member-access
machinery walks it. The builtin answers a 1×0 empty row rather than an n×0 array — a divergence the
loop that follows the preallocation cannot see, and one that makes growth unambiguous.

### A legend click runs the script's callback

`LegendRenderer` already computed each row's rectangle; it now publishes them beside the box, so a
click can be traced to the series it names without laying the legend out again. A press and release
on the same row is a click (the legend could already be dragged; nothing about that changed), raised
through `IInteractionSurface` to the figure window and on to `ScriptGraphicsCallbacks`, where the live
console session has registered an invoker. The invoker builds MATLAB's two arguments — the legend
handle, and an event struct whose `Peer` is the clicked line's handle — runs the callback, and shows
whatever figures it touched.

The interpreter is not re-entrant and a click arrives on the window's thread, so the session holds a
one-slot busy flag: a statement already running owns the interpreter, and the click is reported and
dropped rather than allowed to interleave. A batch run never registers an invoker at all, so
assigning `ItemHitFcn` in a `-batch` script stores it and nothing fires.

### Table subscripts reuse the array machinery

`T{rows, vars}` is the horizontal concatenation of the selected variables' contents; `T(rows, vars)`
is a new immutable sub-table. Both resolve their subscripts through the same `EvaluateIndexArgument`
and `ComputePicks` the interpreter already uses for arrays, so `:`, `end`, ranges, index vectors and
logical masks all work, in each dialect's own index base, for free. The variable subscript
additionally accepts a name or a cell of names. Mixing text with numeric variables under braces is an
error, as it is in MATLAB.

### The data block is found by width

A new `DataBlockDetector` reads the record widths: the data block is the run sharing the file's widest
field count, and anything above the first such record is preamble. It refuses to engage unless it is
confident — the block must be at least two records and a majority of what follows it, and, crucially,
it reports nothing to skip when the widest record is already the first. Every ordinary CSV takes that
last branch, which is what makes the change invisible to files that never needed it.

## Consequences

- The user's script runs to completion and its legend clicks work.
- `plot`, `subplot` and `legend` became `DefineSilent`, so they hand back handles without printing
  one as a bare statement — the contract `figure` has had since M19.
- Two bugs were fixed on the way. `'DisplayName'` was writing the plot's `Name` while the legend read
  `DisplayName`, so a named series drew an empty legend row; and `ApplyPlotOptions` silently dropped
  an unrecognized option name, so a misspelling did nothing and said nothing.
- A line's colour is now written down when it is created, resolved from the axes' colour order by
  draw position. Reading `p.Color` gives a definite answer, which is what lets a second series be
  drawn to match the first. A figure rendered under a non-default theme keeps the stamped colour;
  this is recorded rather than fixed.
- The lexer now records whether a string literal was single- or double-quoted. That distinction is
  MATLAB's char-versus-string divide, and bracket concatenation is the one place JGraph needs it.
- `'best'` places the legend at the top right rather than searching for the least-obstructed corner.
- A date variable read through braces comes back as an OLE automation date, since the dialect has no
  `datetime` value.
- Assignment *into* a table (`T{i,j} = v`) is still refused. Tables are immutable, and rebuilding one
  per element write is a separate decision.
