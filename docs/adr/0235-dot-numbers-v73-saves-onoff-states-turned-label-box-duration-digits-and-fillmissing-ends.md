# ADR 0235 — T.(n), saving bare strings and times and version 7.3 files, graphics on/off as OnOffSwitchState, the plot box under turned labels, a duration's digits, and fillmissing over strings and at the ends: open items 130 to 135

## Status

Accepted, 2026-10-15. The twenty-second batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), six items wide, all filed by batch 21 (ADR 0234) from the divergences its fixtures
recorded and from `stess_38` checked against R2025b. Every answer was recorded from R2025b headless:
probes `probe_b22a` to `probe_b22e` (T.(n), a duration's text, fillmissing, the on/off properties of
forty kinds of graphics object and thirty app components, saving), `probe_h5` and `probe_h5b` (what
R2025b writes into a version 7.3 file), `probe_box` (the plot box and tight inset under turned
labels) and the cross-check `w73`/`r73` (R2025b loading a version 7.3 file this build wrote, and
appending to it) in the open-items scratch, then the fixtures `oi_dot_number_duration_fill`,
`oi_onoff_states_save_v73` and `oi_turned_label_box`. Everything lands in the MATLAB dialect; the JGS
dialect is unchanged.

## Context

- **130.** `T.(2)` and `T.(2) = v` were this build's dynamic-field refusal; R2025b reads and writes
  the table's second variable.
- **131.** `save` refused a bare string, datetime or duration ("a version 5 MAT-file cannot hold")
  where R2025b saves them, a calendarDuration came back a struct, and `-v7.3` was refused: ADR 0065
  kept this build to version 5 for fear of a hand-rolled HDF5 writer producing files MATLAB
  silently mis-reads.
- **132.** `ax.Visible` and every other graphics on/off property read a char `'on'`/`'off'`;
  R2025b answers a `matlab.lang.OnOffSwitchState`, which exists here since ADR 0234.
- **133.** The plot box a script reads before the first frame kept its default under turned tick
  labels, with a guessed tight inset; R2025b raises the box's bottom to the labels' extent.
- **134.** `string(seconds(1.4142135624))` was `"1.4142135624 sec"`; R2025b writes `"1.4142 sec"`.
  `cellstr` of a duration answered an array of texts, `sprintf('%s', d)` and `compose` printed its
  milliseconds, `string(calmonths(2))` failed inside, and a row or scalar duration echoed as its
  milliseconds.
- **135.** Two silent wrong answers `stess_38` had frozen: over a string array fillmissing filled
  `""` and left a real `<missing>`; `'linear'` held the end value where R2025b extrapolates.

## Decision

### 130: T.(n)

A dynamic field that is not text is settled where the dot meets what it is on. On a table a whole
positive number names that variable, to read or to write; a write one past the last makes `Var`n;
the refusals are R2025b's: `IllegalVarIndex` for zero, a fraction, a negative, NaN, a vector or an
empty, `VarIndexOutOfRange` for a read past the end, `DiscontiguousVars` for a write further on,
`IllegalVarSubscript` for a logical or a cell. A string scalar names a field as a char row does. On
anything else a number is `MATLAB:mustBeFieldName`. Reads, whole writes, writes through
(`T.(1)(2) = 50`), deletion (`T.(2) = []`) and timetables all take it.

### 131: bare strings and times, and version 7.3

A string array, a datetime and a duration save anywhere — bare, in a cell, in a struct — as the
object elements a table's have been since ADR 0234, and load back as themselves; a calendarDuration
is an object element of its own class rather than the struct it is kept in.

`save(..., '-v7.3')` writes HDF5 (`MatV73Writer`) in the layout R2025b writes, measured from its own
files: a 512-byte userblock with the MAT-file description and the version word 0x0200, then a
version 0 superblock whose base address is the userblock's end (every address counts from there but
the end-of-file address, which is absolute), version 1 object headers, and old-style groups (a
symbol-table B-tree over nodes of eight names, the names in a local heap). Every variable is a
root-level dataset or group carrying `MATLAB_class`: numbers of every class with their dimensions
reversed; logicals as uint8 and chars as uint16 with `MATLAB_int_decode`; complex as a compound of
`real` and `imag`; an empty value as the uint64 list of its dimensions marked `MATLAB_empty`; a cell
as a dataset of object references into `#refs#`; a struct as a group of its fields with
`MATLAB_fields` (variable-length sequences of one-byte strings in a global heap of at least 4096
bytes, which R2025b's HDF5 asserts); a struct array as a group of reference datasets with no class
of their own; a sparse matrix as a group of `jc`, `ir` and `data` under `MATLAB_sparse`. `-append`
to a version 7.3 file keeps it version 7.3, as R2025b does. R2025b loaded every one of 36 cross-check
variables equal (`h5info` and `h5disp` accept the file, `whos('-file')` lists it) and appended to it;
this build loads R2025b's own files of the same kinds. The reader learnt what R2025b writes and it
had missed: a complex variable is known by its compound type (R2025b writes no `MATLAB_complex`), a
char matrix is one, an empty keeps its class and size, a struct array's fields are the reference
datasets without a class (which settles the ambiguity ADR 0065 recorded), and a sparse group reads.
The classes R2025b keeps in its object subsystem are refused in a version 7.3 file by name.

### 132: graphics on/off properties as OnOffSwitchState

A property read in the MATLAB dialect answers `matlab.lang.OnOffSwitchState` where it reads `'on'` or
`'off'`, for every such property R2025b answers so (measured over forty kinds of graphics object and
thirty app components: all of them but `HandleVisibility`, which can be `'callback'`, and a
uicontrol's or uitable's `Enable`, which can be `'inactive'`; a toolbar state button's `Value`).
The conversion is at the read (`JgsGraphicsProperties.AsSwitchState`), so `get`, the dot, `get(h)`,
`[h.Visible]` and a categorical ruler's readings all answer it, and writes take it as before. A
member made where no interpreter is in hand marks its thread, so a call with one among its
arguments finds the class's methods (`Interpreter.AnyExternalMethods`, kept apart from `AnyNet`,
which also says whether .NET is loaded); `waitfor` compares one by its name. A struct or a cell shows
a single member as its name, as R2025b's `v: on` and `{[on]}` do.

### 133: the plot box under turned labels

Before the first frame, a MATLAB-dialect axes in its default box answers R2025b's tight inset and
plot box (`R2025bEstimate`): below the box, the tick labels' extent at their angle — the widest
rounded up to a pixel times |sin|, a label's height times |cos| — and 5.33 pixels, then an xlabel's
height and 1.33; left of it, the widest y label rounded up and 5.33, then a ylabel's height and 4, or
how far the first x label hangs past the box if more (a level label is centred on its tick, a turned
one hangs its rounded width times |cos| to the left); above it, half a y label's height, or a
title's height and 3.67; right of it, how far the last x label hangs past (a turned one its height
times |sin|, an upright one half its height). Axis labels and titles are measured at R2025b's sizes
(the axes font times the multiplier, the title bold). The box keeps its default margins until the
inset outgrows one; that edge then moves to the inset and the default margin's fraction of a pixel.
`TickLabelTurn.Measure` is the measurement the turning rule and the estimate share.

### 134: a duration's digits

The MATLAB dialect's duration text is R2025b's: a single-unit format writes the number at five
significant digits (fifteen under `format long`), fixed while the unrounded magnitude is in
[1e-4, 1e5) and scientific outside it (99999.5 seconds is `100000 sec`, 1e5 is `1e+05 sec`), `day`
and `yr` taking a plural except at exactly one; a clock format shows as many fraction digits as it
has `S`, cut rather than rounded, and Inf and NaN alone. `string`, `char`, `cellstr` (a cell, in the
shape), `sprintf`, `compose`, the echo and a struct's or cell's display all write it;
`string(calmonths(2))` is `"2mo"`; `num2str` of a time is R2025b's `nonNumericInput`.

### 135: fillmissing over strings and at the ends

Over a string array only `<missing>` is missing; over a cellstr the empty char; a char array has no
missing characters and comes back as it went in (as `rmmissing` and its kind). Text fills run along
a dimension as numbers do, take `EndValues`, a string array's constant is a string (one, or one per
slice) and a cellstr's char, each refused in R2025b's words (`ConstantString`, `ConstantCellstr`,
`InterpolationInvalidArray`, `FirstInputInvalid`, no method `minrhs`); fillmissing takes its string
arguments as written for that. At the ends every method follows `EndValues`, and under the default
`'extrap'` its own reach: `'linear'` continues the line through the two present values nearest the
end (a lone value fills nothing), `'nearest'` copies, `'previous'` and `'next'` fill only the end
they reach, `'constant'` and the moving methods fill an end as they fill a gap. The second output
marks only what was filled.

## Divergences

- **`save -v7.3` refuses strings, datetimes, durations, categoricals, tables, objects and function
  handles**: R2025b keeps them in its object subsystem (`#subsystem#`), which this writer does not
  write; version 7 holds them.
- **fillmissing without a method fills `'linear'`**, a JGraph extra R2025b refuses (`MATLAB:minrhs`;
  kept, as decided 2026-10-09); a complex vector and `SamplePoints` are refused here.
- **A turned or wide label's width can be a pixel off R2025b's**: twelve digits at 10 points, a
  run of letters, 14-point labels, so the plot box under them can sit a pixel apart (as the turning
  rule's three misses, ADR 0234).
- **`methods` of an `OnOffSwitchState` lists its own**, where R2025b lists the 153 it inherits,
  some from the toolboxes installed (as ADR 0234).
- **`save` and `load` with `-v4`** are refused here (`save does not support the option '-v4'`).
- **findobj counts one handle more** under an axes than R2025b (as before).

## Consequences

- The host sees a member as its name: a variable's raw copy (the Variables pane, the tests'
  `RawValue`) is `"on"` or `"off"`, its type `matlab.lang.OnOffSwitchState`. Twelve older tests
  that `disp` an on/off reading now expect R2025b's member display (`    on`); four that asserted
  this build's text fill or its version 7.3 refusals assert R2025b's behaviour, and a version 7.3
  char matrix reads back a char matrix, as a version 5 one does.
- MATLAB code reading an on/off property gets a member: `strcmp`, `==`, `if`, `switch`, `ismember`,
  `isequal` and `set` take it as R2025b's do, and a script that passed it on to its own code sees
  `isenum` true and `ischar` false. JGS reads a char, as before.
- A MATLAB-dialect script can save version 7.3 files MATLAB reads, and a bare string, datetime or
  duration in any version; ADR 0065's decision stands for the JGS dialect, which still refuses
  `-v7.3`.
- The batch 21 fixtures' rows this batch fixed are exact now: `oi_setget_arrays_onoff` 23
  divergences to 19 (an axes' `Visible` as a member, `==`, `isenum`, `set`), `oi_table_missing_save_summary`
  19 to 15 (`T.(2)` read and written, a bare datetime saved, a summary's duration `Std`), and
  `value_isolation_lifetime`'s `a146_matfile_indexed_write` (a `matfile` write to a version 7.3 file,
  which the MATLAB dialect now makes through the same writer); `oi_crowded_ruler_mixed_axis` keeps its
  six, re-stamped (its listings print members now, its plot boxes miss by a twelve-digit label's pixel).
- New fixtures `oi_dot_number_duration_fill` (288 lines, 187 rows, 3 divergences),
  `oi_onoff_states_save_v73` (352, 141, 5) and `oi_turned_label_box` (120, 38, 9).
  `OpenItemsBatch22Tests` holds a version 7.3 file's userblock, superblock and global heap, its read
  back, the on/off properties R2025b keeps char, a member under `sprintf` beside a number, and
  `waitfor` over a member.
- The gates caught two regressions before landing: a member passed to `sprintf` was printed by the
  format's first conversion, so a GUIDE file's `sprintf('... %d ... %s', n, get(h, 'Visible'))` logged
  nothing for the `%s` (fixture `u11_guide`; each member is now printed by the conversion it meets,
  and `fprintf` with it); and `matfile`'s write refusal for a version 7.3 file met the file `save
  -v7.3` can now write.
- The R2025b cross-check aborted R2025b twice before the writer was right, both times inside its
  HDF5: the superblock's end-of-file address is absolute where every other address counts from the
  superblock, and a global heap collection must be 4096 bytes at least.
- Full suite 11,051 of 11,051 (the MIDI scheduler's timing test passes alone, as in batch 21); stress
  89 of 89. `stess_38` lines 118 and 120 take R2025b's answers (only `<missing>`
  is missing in a string array; `'linear'` extrapolates).

## Still open

- Item 90 (the `lu` timing) still waits for a quiet machine.
