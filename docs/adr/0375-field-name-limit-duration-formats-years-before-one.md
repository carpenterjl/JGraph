# ADR 0375 — A field name past namelengthmax is cut, findobj searches a member list, mat2str writes control characters as calls, a duration takes R2025b's formats only, a calendarDuration keeps its type and writes weeks, quarters and time as R2025b does, and a year before 1 prints and counts

## Status

Accepted, 2026-11-15. Open item 342 (chips wave 7, lane 7E): the values leftovers ADR 0371 listed.

## Context

Probes `p7e1` to `p7e6` (about 580 rows, R2025b 25.2, `-batch -noFigureWindows`) ran every row in both
engines.

- **Field names past `namelengthmax`** (2048, ADR 0371) were kept whole. R2025b cuts one to 2048
  characters with the warning `MATLAB:namelengthmaxexceeded` ("'…' exceeds MATLAB's maximum name length
  of 2048 characters and has been truncated to '…'.") in `s.(f) = v`, `s(2).(f) = v`, `s.(f).b = v`,
  `s.(string(f)) = v`, `struct(f, v)` (cell values too), `setfield`, `cell2struct` (either dimension),
  and the reads `s.(f)`, `s.(f)(1)` and `getfield`, so a read of the long name finds the cut field and a
  missing one is `MATLAB:nonExistentField` naming the cut name. Two names that cut to one are
  `MATLAB:DuplicateFieldName` ("Duplicate field name "…"", no full stop), which `struct('a', 1, 'a', 2)`
  is too; here it kept the last value. `isfield` does not cut (false for the long name), and `rmfield`
  refuses an absent name past 2048 characters as `MATLAB:rmfield:FieldnameTooLong` ("The string '…' is
  longer than the maximum MATLAB name length.  It is not a valid fieldname.") before
  `InvalidFieldname`, as rmfield.m does.
- **`findobj(mc.PropertyList, 'Name', 'A')`** was `MATLAB:class:InvalidHandle`. R2025b searches the
  member list: a name and value match a property of that exact name holding an equal value (`'name'`
  and the value `'a'` match nothing), `-not`, `-or`, `-regexp` and `-property` work, an unknown
  property matches nothing, and the answer is a column of the list's class or a 0-by-0.
- **`mat2str` of text.** mat2str.m writes characters 0 and 10 to 13 (`'[\0\n-\r]'`) as calls between
  two pieces, `['a' char(10) 'b']`, a tab raw; here only character 0 was. A string array holding one of
  them is `compose(…)` of its escapes (`\0 \n \v \f \r`, the backslash doubled), and a double quote is
  doubled in any string (`"a""b"`); here strings were written as they stood.
- **`mustBeValidVariableName`** said "The following are not valid variable names"; R2025b says "These
  are not valid variable names", `mustBeFile` and `mustBeFolder` "These files do not exist" and "These
  folders do not exist". An empty cell or string array passes; a char matrix and a missing string are
  `mustBeNonzeroLengthText`; a cell holding a char matrix is `MATLAB:string:MustBeCharCellArrayOrString`
  ("First argument must be text.").
- **`containers.Map('KeyType', k, 'ValueType', v)`** stored any text. R2025b takes the key types
  `char double single int32 uint32 int64 uint64` and the value types `any char logical double single
  int8 uint8 int16 uint16 int32 uint32 int64 uint64`, in any case and kept in lower case; anything else
  is `MATLAB:Containers:Map:KeyUnsupportedType` or `ValueUnsupportedType` ("Unsupported KeyType 'uint8'
  specified.  See documentation for valid key types."), the key's first, and a non-text type is
  `MATLAB:string:MustBeStringScalarOrCharacterVector` (here a crash). The pair may come the other way
  round. Made from numeric keys, a map keeps their class when all are one of `single int32 uint32 int64
  uint64` and is double otherwise (a uint8, int8, int16 or logical key too), warning
  `Convert64BitIntToDouble` when a 64-bit key is converted; here every numeric map was double and kept
  its keys' own classes.
- **A duration's format.** R2025b takes the units `y d h m s`, the clocks `dd:hh:mm:ss`, `hh:mm:ss`,
  `mm:ss`, `hh:mm`, and on the first three a dot and up to nine `S`; every other text (each other
  letter, `HH:mm:ss`, `hh`, the empty text, `hh:mm.SSS`, ten `S`) is `MATLAB:duration:UnrecognizedFormat`
  ("The format 'E' is not valid.") when set, as the constructor's `'Format'` and in `char`, `string`
  and `cellstr`; a non-text format is `MATLAB:duration:InvalidFormat`. An `InputFormat` is a clock
  (`MATLAB:duration:UnrecognizedInputFormat`, "Unsupported format 's'. See the documentation of
  'InputFormat' for valid formats.") and `'hh:mm'` reads two fields as hours and minutes. Here every
  format was accepted and an unknown one printed as `hh:mm:ss`. `dd:hh:mm:ss` leaves the day out when
  there is none (`01:02:03`, `01:01:02:03` past a day).
- **A calendarDuration array** lost its type to any index, element write, deletion or transpose
  (`x(2:3)`, `x(2) = caldays(4)`, `x'` were plain structs, so `string(x)` printed "1x2 struct array
  with fields: months, days, millis"). R2025b writes a time of day as `4h 5m 6s` (all three when there
  is time, seconds to five significant digits), `calweeks` in weeks (`2w`, format `ymwdt`) and
  `calquarters` in quarters (`2q`, `qmdt`); a sum or a concatenation writes every unit either writes
  (`caldays(10) + calweeks(0)` is `1w 3d`, `calmonths(15) + calquarters(0)` `1y 1q`). A gap a growth
  leaves is `0d`. Here time was `04:05:06` and weeks and quarters were days and months.
- **A year before 1.** The datetime display wrote `CE` and a year four hundred on (`G y` of year -5 was
  `CE 395`), and replaced only a four-digit year in the default formats. R2025b's `u` is the signed
  year (`-5`, `uu` `-05`, `uuuu` `-0005`), `y` the year of the era (year 0 is 1 BCE: `yyyy` `0001`), `G`
  in any count `BCE`, and a year past 9999 is written whole. `datetime(-5, 3, 1, 'TimeZone', 'UTC')` was
  refused as un-representable. `datenum` refused a year before 100 ("Not a legal OleAut date") and
  clamped one past 9999; `datenum(0, 1, 1)` is 1, `datenum(-1, 1, 1)` -364. `datestr` refused every
  serial before year 100; a serial in [0, 1) is R2025b's day 0 of January in year 0 (`datestr(0)` is
  `00-Jan-0000`, `datevec(0)` `[0 0 0 0 0 0]`) and times of day alone are format 16 (`datestr(0.5)` is
  `12:00 PM`). formatdate.m refuses a format whose `y` letters number other than two or four
  (`MATLAB:formatdate:yearFormat`, "Unrecognized year format. Format: dd-yyy.").

## Decision

### Field names (`JgsBuiltins.FieldNameWithinLimit`)

One helper cuts a name past `NameLengthMax` with R2025b's warning, in the MATLAB dialect only. The
interpreter's dynamic field name (`Interpreter.FieldName`) asks it of a text name, so every dot write
and read of a dynamic name is cut; `struct`'s names (`StructNameArgument`), `getfield`, `setfield` and
`CellToStructAlong` ask it before the name's validity is checked. `BuildStruct` refuses a repeated name
in the MATLAB dialect. `RemovableFieldNames` refuses an absent name past the limit as too long. JGS
keeps a name whole.

### findobj over a member list (`JgsBuiltins.FindAmongMetadata`)

`findobj` in the MATLAB dialect hands a metadata array other than a metaclass to `FindAmongMetadata`,
which keeps the elements `MetadataSearchKeeps` answers, reading the terms left to right, and answers a
column of the list's class (`MetaColumn`) or a 0-by-0 (`EmptyMetaArray`).

### mat2str of text (`MatStrCharPiece`, `MatStrStringPiece`)

In the MATLAB dialect a char row's characters 0 and 10 to 13 are written as calls and the literal takes
its brackets; a string array with one of them is written with compose's escapes inside `compose(…)`,
and a double quote is doubled. JGS writes character 0 as a call, as before, and strings as they stand.

### The name validators (`NamesMustHold`)

The MATLAB dialect's sentences begin "These"; an empty cell or string array passes; a char matrix and
a missing string fail the nonzero-length check; a cell holding a char matrix is string's refusal. JGS
keeps "The following".

### containers.Map's types (`MapDeclaredTypes`, `MapKeyTypeOfKeys`)

In the MATLAB dialect the declared pair, in either order, is checked against R2025b's lists and kept
in lower case; a non-text type is refused (`DeclaredTypeText`, in both dialects, where it crashed).
Numeric keys give the key type `MapKeyTypeOfKeys` names and are converted to it by `MapKeyAs`.

### A duration's formats (`JgsBuiltins.DurationFormats.cs`)

`IsDurationDisplayFormat` and `IsDurationClockFormat` hold R2025b's lists. `DurationDisplayFormat`
reads a format the `Format` setter or the constructor is given, `RefuseDurationDisplayFormat` the one
`char`, `string` and `cellstr` are given (`TimeFormatArguments`), and `RefuseDurationInputFormat` an
`InputFormat`. `FormatDurationMatlab` writes `dd:hh:mm:ss` without a day under 24 hours.

### A calendarDuration (`KeptCalendar`, `FormatCalendarMatlab`, `CalendarFormatUnion`)

`Interpreter.KeptCalendar` puts a calendarDuration's tag back on the struct array an index
(`IndexStruct`), an element write or deletion (`AssignIntoStruct`, `AssignIntoStructGrid`, the N-D
write) or a transpose (`TransposedStructs`) made. `ReadCalendar` reads a gap's `[]` as 0. `calweeks`
and `calquarters` make the formats `ymwdt` and `qmdt`; `CalendarValue` takes a calendar model's format;
a sum (`CalendarZip`) and a concatenation (`JgsTime.ConcatTag`) take the union. In the MATLAB dialect
`FormatCalendar` writes through `FormatCalendarMatlab`; JGS keeps `hh:mm:ss` and days.

### A year before 1 (`JgsTime.YearToken`, `WithinNetRange`, datestr and datenum)

`FormatDatetime` hands the moment's own year to `WithNameTokens`, which writes every `y` and `u` run
through `YearToken` and, in the MATLAB dialect, `G` as `BCE` before year 1; the four-digit replacement
is gone. `OffsetAt` and `ToUtc` read a zone's offset at a year .NET cannot hold four hundred years on
(`WithinNetRange`). In the MATLAB dialect `datenum` counts a year before 100 or past 9999 through
`JgsTime.FromComponents`, `datestr` reads such a serial four hundred years on with its own year
(`FormatDatestr`'s `year` and `dayZero`), applies R2025b's day 0 and format 16, and refuses a `y` count
other than two or four; `datevec` answers `[0 0 0 h m s]` for a serial in [0, 1). JGS keeps its
out-of-range refusal.

## Consequences

- New parity fixture `oi_field_names_formats_years` (526 rows from R2025b 25.2, two of them
  divergences), green in the packed and boxed lanes.
- `MatlabStringsM104Tests.MustBeValidVariableNameNamesEveryNameThatFailed` reads "These are";
  `MatlabM82TimeTests` reads `2w` for `calweeks(2)` and `1mo 5h 0m 0s` for `calmonths(1) + hours(5)`.
- A MATLAB script that declared a map type R2025b refuses, set a duration format it refuses, or used
  `datestr` with a `yyy` year now stops where R2025b does. Every MATLAB-dialect calendarDuration with a
  time of day prints differently.

## Divergences

- **`datestr` writes a year's whole signed number**: R2025b's formatdate.m writes `mod(year, 10000)`, so
  `datestr(-1)` is `30-Dec-9999` and `datestr(datenum(12345, 6, 7))` `07-Jun-2345`, dates that are not
  the moment's. R2025b's own `datevec(-1)` is `[-1 12 30 0 0 0]` and `char(datetime(-1, 12, 30))`
  `30-Dec--0001`; JGraph writes `30-Dec--0001` and `07-Jun-12345`. `yy` stays the last two digits of
  the year's magnitude, as formatdate.m writes it. Fixture rows `div_datestr_negative_year`,
  `div_datestr_five_digit_year`.

## Still open

- Left as ADR 0371 left them: App Designer's code-name check (`DesignNames.NameLengthMax`, 63) needs an
  App Designer window to measure; a locale outside the 66-locale table keeps .NET's names; an empty
  `GetMethod`/`SetMethod` (a value-model change) and `struct(metaproperty)`'s hidden fields stay ADR
  0371's divergences; the ownership audit's `cell` and `repmat` were judged harmless there.
- Seen while probing, not this item's: `s.(f)(3) = 1` creating a field (`s` undefined or the field
  absent) is refused here (probe `p7e3` g3 to g5); `getfield(s, 'a', 'b')` and `setfield(s, 'a', 'b',
  v)` (nested names) are `MATLAB:maxrhs` here; `table(…, 'VariableNames', {f})` with a name past 2048
  characters is R2025b's `MATLAB:table:VariableNameLengthMax`, kept here; `findobj(mc, …)` of a metaclass
  answers two `matlab.metadata.MetaData` in R2025b, unmodelled; `containers.Map('KeyType', …,
  'ValueType', …, 'UniformValues', true)` is R2025b's `IncorrectNumberInputs`.
- calendarDuration: `repmat` crashes on one (an invalid cast) and `reshape` drops its type (and the
  other shape verbs are unmeasured); its `disp` is `[1mo, 2mo, 3mo]` here (R2025b `   1mo   2mo   3mo`);
  unary minus is refused; a `NaN` element, the constructor's `'Format'` option and R2025b's integer
  refusals (`calweeks(1.5)`, fractional hours) are not modelled; `string` of an empty one is 0-by-0.
- `datetime(0.5, 1, 1)` is R2025b's `MATLAB:datetime:MustBeInteger`; a negative year is not parsed with
  an `InputFormat` here (`'-0005-03-01'` with `uuuu-MM-dd`, and `'0000-03-01'`); `datestr`'s upper-case
  `YYYY` is R2025b's year and literal text here.
