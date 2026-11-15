# ADR 0371 — m(k) = v converts as a Map's merge does, s(:) is a column, a uniform arrayfun collects cells, structs and times, datetime refuses R2025b's unsupported letters, num2str reads several conversions as sprintfc does, and namelengthmax is 2048

## Status

Accepted, 2026-11-14. Open items 324 and 325 (chips wave 6, lane 6D): the leftovers ADR 0358 and
ADR 0359 listed, but for `Inf*1i` (lane 6C).

## Context

Probes `p6d`, `p6d2` and `p6d3` (about 450 rows, R2025b 25.2, `-batch -noFigureWindows`) ran every
row in both engines.

- **Item 324, `m(k) = v`** stored what it was given. R2025b converts the key and the value to the
  map's `KeyType` and `ValueType` exactly as the merge of ADR 0358 does, or refuses them
  (`MATLAB:Containers:TypeMismatch`, "Specified key type …" / "Specified value type does not match the
  type expected for this container."): a char into a double map, `[1 2]`, `[]`, `{1}`, a struct, a
  complex number and a string into a numeric map are refused; an int32 into a double map is a double,
  1.5 into an int32 map 2, -3 into a uint8 map 0, NaN into an int8 map 0, 2 into a single map a single;
  a logical map takes a whole number only (-1 and `int8(2)` are true; NaN, Inf and 0.5 are
  `MATLAB:IntVal`, "Scalar integer value required, but value is not integral"); a char map takes a
  string scalar as its char row and a char matrix as the row of its characters in storage order
  (`['ab'; 'cd']` is `'acbd'`), a string array of two is refused. A key is read first: a char key into
  a double map, `[1 2]`, `[]` and a complex key are the key's refusal, `int8(3)` and `true` into a
  double map are doubles, 1.5 into an int32 map 2, a string `"a"` into a char map `'a'`, and NaN is
  `MATLAB:Containers:NaNNotAllowed` ("NaN-valued key is not allowed for this container.").
  `containers.Map({'a'}, {1})` is a double map, as here already.
- **Item 324, `s(:)`** of a 1-by-N struct array was 1-by-N here; R2025b answers N-by-1 for every shape
  (a 2-by-2 is 4-by-1, a 1-by-3-by-2 6-by-1, `struct('a', {})` 0-by-1), through a cell's slot and a
  field too, so `s(:)'` is a row and `arrayfun(f, s(:))` a column. `s(1:end)` and `s([1 2 3]')` keep the
  source's orientation, as here already.
- **Item 324, a uniform `arrayfun` or `cellfun`** refused a 1-by-1 cell or struct answer. R2025b collects
  a 1-by-1 cell apiece into a cell of what they hold, a 1-by-1 struct apiece into a struct array, a
  datetime, a duration or a calendarDuration apiece into one array (here a datetime or duration apiece
  came back as its milliseconds, and a calendarDuration apiece was refused). It refuses a function
  handle (`UnimplementedOutputArrayType`, "function_handle output type is not supported. Set
  'UniformOutput' to false."), a containers.Map or a table (`SubsasgnProblem`, "Unable to assign into
  output array 1 at index 1. …"), a struct whose fields differ from the first's
  (`structFieldNumberMismatch`, `structFieldNameMismatch`, the name in the message), and any answer of
  one of these kinds beside one of another class (`MismatchInOutputTypes`, "(cell versus double)",
  the first answer's class first). An empty cell or a struct array of other than one element is
  `NotAScalarOutput`. An `ErrorHandler` is not asked about these refusals. R2025b's `NotAScalarOutput`
  and `MismatchInOutputTypes` messages break the line before "Set 'UniformOutput' to false.".
- **Item 324, metadata.** `struct(metaproperty)` was refused ("struct takes name/value pairs."); R2025b
  answers a struct of its properties with `MATLAB:structOnObject`, 1-by-1 for an array, no
  `DefaultValue` where the property has no default. `isfield` of any metadata value is false (it is an
  object); `fieldnames`, `properties` and `isprop` of a metaclass rendered past `MetaDepth` left out the
  six member lists that a read makes (ADR 0358).
- **Item 324, the ownership audit** named `cell`, `repmat` and `cell2struct` among the adopted builtins
  whose returns it cannot call minted.
- **Item 325, datetime formats.** R2025b refuses every letter outside `G y u Q M d D e a h H m s S W x X
  z Z` (`MATLAB:datetime:UnsupportedSymbol`, "The format 'E' contains an unsupported symbol: 'E'. See
  the <a href=…>datetime.Format property</a> …") and six or more `e`
  (`MATLAB:datetime:UnsupportedIdentifier`, named `'eeeeee'` however long the run), when the format is
  set (`t.Format`, the `'Format'` option), when it formats (`char`, `string`, `cellstr`) and as an
  `InputFormat`; the identifiers are read across the whole format before any symbol (`'E zzzz'` names
  `zzzz`, `'O eeeeee'` names `eeeeee`), the symbols in order (`'E O'` names `E`). Here `E`, `L`, `c`,
  `w`, `F` and the rest printed .NET's names or the letters. `G` (any count `CE`), `Q`/`QQ` (the quarter
  padded), `QQQ` (`Q4`, in German too), `QQQQ` (`4th quarter`), `QQQQQ` and longer (the number), `D`
  (the day of the year, padded to the run) and `W` (the week of the month) printed their letters here.
- **Item 325, `num2str` under a format with two or more conversions** printed a row at a time. num2str.m
  hands the array to sprintfc, which cuts each row into cells of as many elements as the format has
  conversions, the last cell short (`sprintf('%d,%d', 3)` is `3,`), and num2str.m right-justifies each
  column of cells as `strjust` does, pads the columns to the widest, joins and trims them:
  `num2str([1 2 3], '%d,%d')` is `'1,2 3,'`, `num2str(magic(4), '%d,%d')` row three is `' 9,76,12'`. A row
  shorter than the format is warned `MATLAB:num2str:badConversion` ("Not enough arguments for
  format."). sprintfc reads the format's escapes: `num2str([1 2; 3 4], '%d\t%d')` holds tabs, and
  `num2str(pi, '%8.3f\n')` is `'3.142'`.
- **Item 325, `namelengthmax`** was 63; R2025b's is 2048 and `isvarname` is true to 2048 characters
  (`mustBeValidVariableName` with it). Struct field names past 2048 are truncated with
  `MATLAB:namelengthmaxexceeded` in R2025b (not done here, see Still open). `genvarname`,
  `matlab.lang.makeValidName` and `matlab.lang.makeUniqueStrings` are no builtins here.
- **Item 325, an empty part.** `datetime(2026, 1, 1:0)` was refused ("the parts must all be scalars or
  all the same length"); R2025b answers a 1-by-0 datetime: every part that is not a scalar must agree,
  and the first of them gives the size (`datetime(2026, [], 1)` is 0-by-0, `datetime(2026, 1,
  zeros(0, 3))` 0-by-3); parts of different lengths are `MATLAB:datetime:InputSizeMismatch` ("Data
  inputs must be the same size, or any of them can be a scalar."). `duration(1, 1:0, 0)` crashed here
  (an index past the end), `calendarDuration(1, 1:0, 1)` too; both are 1-by-0 in R2025b.
  `datetime(zeros(0, 3))` is 0-by-1 (a column, one moment a row), `datetime(zeros(2, 0))`
  `MATLAB:datetime:InvalidNumericData`, and seven parts are year to milliseconds.

## Decision

### m(k) = v on a Map (`JgsBuiltins.MapEntryAs`, `Interpreter`'s keyed write)

In the MATLAB dialect the keyed write hands a `containers.Map`'s key and value to `MapEntryAs`, which
reads them through `MapKeyAs` and `MapValueAs`, the merge's own converters, before `Put`. `MapKeyAs`
refuses a NaN key; `MapValueAs` turns a string scalar or a char matrix into a char row for a char map
and refuses a fraction or a non-finite value for a logical map. A dictionary and the JGS dialect are
as before.

### s(:) (`Interpreter.IndexStructElements`)

A lone colon over a struct array is an N-by-1 in the MATLAB dialect.

### The uniform collection (`JgsBuiltins.UniformGather.cs`)

`IsGatheredKind` names a cell, a plain struct, a function handle, a table, a containers.Map, a
datetime, a duration and a calendarDuration. `CheckGatheredAnswer` holds each answer of a uniform
`arrayfun` or `cellfun`, in the loop, against the first answer of its output when either is of these
kinds: one element, the same class, no handle, no map or table, the same field names.
`GatheredAnswers` builds the row the caller shapes. Both run in the MATLAB dialect only; numbers,
text, categoricals and objects keep their roads. The `NotAScalarOutput` and `MismatchInOutputTypes`
messages carry R2025b's line break.

### Metadata (`JgsBuiltins.MetadataAsStruct`, `MetadataPropertyNames`)

`struct(m)` of a metadata value warns `MATLAB:structOnObject` and answers its first element's fields
as a plain struct, without `DefaultValue` where `HasDefault` is false. `MetadataPropertyNames` gives
`fieldnames`, `properties` and `isprop` R2025b's full metaclass list when the value was rendered past
`MetaDepth`. `isfield` of a metadata value is false in the MATLAB dialect.

### datetime's letters (`RefuseUnsupportedZoneLetters`, `JgsTime.EraQuarterDayWeek`)

The zone-letter refusal reads two passes: the identifiers (`zzzz`, and in the MATLAB dialect six or
more `e`), then the symbols (`O`, and in the MATLAB dialect every letter outside
`DatetimeFormatLetters`). The datetime constructor asks it of an `InputFormat` too. `WithNameTokens`
writes `G`, `Q`, `D` and `W` through `EraQuarterDayWeek` in the MATLAB dialect; `QQQQ` is written in
English only and keeps .NET's reading in another language.

### num2str's several conversions (`Num2StrSeveralConversions`)

In the MATLAB dialect a format with two or more conversions is sprintfc's cells, right-justified by
column (`Num2StrJustify`), padded and trimmed (`Num2StrPadded`); a row shorter than the format warns
`MATLAB:num2str:badConversion`. The format of a real array's formatted road is decoded by
`JgsFormatEscapes.Decode` first.

### namelengthmax (`JgsBuiltins.NameLengthMax`)

One constant, 2048, answers `namelengthmax` and bounds `IsValidVariableName` (so `isvarname`,
`mustBeValidVariableName`, `str2func`'s and `feval`'s unanswered names, `setappdata`'s names, the
legacy compiler's output name, the MPS cache names). `JgsMpsProtobuf` takes a field name up to 2049
characters, mps.json's measured limit (ADR 0359); it was 63.

### An empty part (`ComponentLength`)

`ComponentwiseMoments`, `duration`'s three or four parts and `calendarDuration`'s parts take their
count and size from the first part that is not a scalar, in the MATLAB dialect. A single numeric
matrix of moments or of durations is a column. Seven datetime parts are accepted in the MATLAB dialect,
the last milliseconds.

### The ownership audit

`CellToStructAlong` carries `// audit: mints` (both returns build the struct from new element
dictionaries); the audit's `cell2struct` problem is gone. The other two are left (see Still open).

## Consequences

- New parity fixture `oi_values_leftovers` (371 rows from R2025b 25.2, two of them divergences), green
  in the packed and boxed lanes, with the helper class `fixtures/helpers/Lv324Cls.m`.
- `MatlabStringsM104Tests.ANameMayBeNamelengthmaxCharactersAndNoMore` (was
  `ANameMayBeSixtyThreeCharactersAndNoMore`) and `MatlabEvalBuiltinTests` read 2048.
- A MATLAB script that stored a value of another type in a typed map, used a format letter R2025b
  refuses, or relied on `s(:)` being a row, now stops or answers where R2025b does. A uniform
  `arrayfun` or `cellfun` that answered a datetime apiece now answers a datetime, not milliseconds.

## Divergences

- **`struct` of a metadata value holds only its visible properties**: R2025b's also holds the hidden
  ones (`Access`, `Type`, `NeverAmbiguous`, `Sealed` of a metaproperty, seventeen more of a metaclass,
  four of a metamethod), which this build does not model. Fixture row `div_struct_metaproperty`.
- **An empty `GetMethod` or `SetMethod` is a 0-by-0 double**: R2025b's is a 0-by-0 function handle.
  This build's function handle is one callable, 1-by-1 wherever its size is read (`size`, `numel`,
  `isempty`, display, concatenation, `func2str`, saving), so an empty one is a value-model change the
  item said to stop on. Fixture row `div_empty_get_method`.

## Still open

- A uniform `arrayfun` or `cellfun` mixing numeric classes, logical, char or string answers
  (`{1, int8(2)}`, `{1, true}`, `{single(1), 2}`, `{"a", 'b'}`, `{1, 'a'}`) is R2025b's
  `MismatchInOutputTypes`; here they join as before (probe `p6d2` rows x7, x8, x19, x23 to x25).
- A struct field name longer than 2048 characters is truncated by R2025b with
  `MATLAB:namelengthmaxexceeded` (`s.(f) = v`, `struct(f, v)`, `setfield`, `cell2struct`, and a read
  `s.(f)` or `getfield` of the long name finds the truncated field; `isfield` does not truncate and
  `rmfield` refuses `MATLAB:rmfield:FieldnameTooLong`; probe `p6d3` rows w1 to w13). Here the name is
  kept whole. Each road is a field write or read of its own.
- App Designer's code-name check (`DesignNames.NameLengthMax`) still says 63; R2025b's App Designer
  could only be measured in a window.
- A locale outside the 66-locale table keeps .NET's names (ADR 0359); `QQQQ` in another language too.
- A metaclass of a built-in type lists `methods`' names (R2025b 279 for a double with signatures,
  install-dependent), and `exist('secretID')` stays 5 (ADR 0149's rule), as ADR 0358 left them.
- The ownership audit still names `cell` and `repmat`. `cell`'s .NET road returns
  `NetBuiltinConversions.Cell`, which is annotated as minting, but the audit resolves calls by member
  name and another `Cell` member is on its borrowing list. `repmat`'s verdict comes from
  `BuildValues(down, across, (_, _) => source)`, whose 1-by-1 case hands back the source wrapper for a
  non-array source (a number, an object, a table, a handle): adopting it was harmless in every probe
  (a value object written through either name detaches, tables are immutable), so nothing was changed.
- Seen while probing, not these items': `findobj(mc.PropertyList, 'Name', 'A')` is
  `MATLAB:class:InvalidHandle` here; `arrayfun` over an int8 or single array hands the function
  doubles; `mat2str` of a char matrix holding a newline writes it raw (R2025b `char(10)`);
  `mustBeValidVariableName`'s message reads "The following are not valid variable names" (R2025b
  "These are not valid variable names: '…'."); `containers.Map('KeyType', 'uint8', …)` is accepted
  (R2025b `MATLAB:Containers:Map:KeyUnsupportedType`); a duration's `Format = 'E'` is accepted (R2025b
  `MATLAB:duration:UnrecognizedFormat`); `string` of a calendarDuration array prints its storage; a
  year before 1 prints `CE` and a wrong year (.NET's `DateTime` starts at year 1).
