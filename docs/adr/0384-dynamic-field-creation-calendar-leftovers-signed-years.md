# ADR 0384 — An index write creates a dynamic field, getfield and setfield take a path, a table name past namelengthmax is refused, a calendarDuration takes R2025b's integers, formats, signs and empties, a datetime's components are whole, a signed year is parsed, and datestr reads its letters as cnv2icudf.m does

## Status

Accepted, 2026-11-16. Open item 347 and the `datestr(3, 'yyyy')` part of open item 346 (chips
wave 8, lane 8D): the values and time leftovers ADR 0375 listed.

## Context

Probes `p8d1` to `p8d4`, `p8d_mc` and `q1` to `q5` (about 520 rows, R2025b 25.2,
`-batch -noFigureWindows`) ran every row in both engines and were diffed; JGraph's side ran in the
MATLAB dialect exactly as a parity fixture runs.

- **A dynamic field an index write creates.** `s.(f)(3) = 1`, `s.(f){2} = 1`, `s.(f)(2, 3) = 1`,
  `s.(f)(3).g = 1`, `s(2).(f)(3) = 1` with `s` undefined or a struct without the field were
  `MATLAB:UndefinedFunction` or `MATLAB:nonExistentField` here, in a script, a function and `eval`
  alike; R2025b creates the field as it does for a static name (`s.a(3) = 1`). The roads that peek
  at an absent field (`IsAbsentField`) only saw a static name, since a dynamic one cannot be read
  without evaluating its name twice.
- **The static writes around them.** `s.a(3) = 1` and `s.a{2} = 1` on a number, a char or a logical
  read `s.a` and refused with `MATLAB:structRefFromNonStruct`, `s(2).a = 1` on a number or a cell
  with a JGraph sentence; R2025b's is `MATLAB:structAssToNonStruct` ("Unable to perform assignment
  because dot indexing is not supported for variables of this type."), and `{}` or `''` start a
  struct as `[]` does. On a struct array `s.a(3) = 1`, `s.a{3} = 1` are
  `MATLAB:scalarStrucRequired` (an empty one `MATLAB:emptyDotAssignment`); here the field was read
  and missing. `s(2).a(3) = 1` on a struct without `a` was `MATLAB:nonExistentField`; R2025b adds the
  field to every element. `s.a(end+1) = 1` with no `s` is R2025b's `MATLAB:undefinedVarOrClass`
  ("Unable to resolve the name 's.a'."), and with `s` a struct without `a` its
  `MATLAB:nonExistentField`; here both created the field. The empty an index write conjures was
  always a double here: `x(3) = 'x'` with no `x` was `[0 0 120]`, `s.a(2) = int8(3)` a double;
  R2025b's is of the right-hand side's class (char, int8, single, logical).
- **`s(k).a(i) = v` with a non-literal `k`** replaced the element's field with an empty before the
  write (`u.a = [1 2]; k = 1; u(k).a(1) = 9` answered `9`, not `[9 2]`): the get-modify-set walk
  (`ClassifyLevel`) read a position from a literal only and took any other subscript for a new
  element. A literal `u(1)` was right. Found through `setfield(s, {1}, 'a', {2}, 9)`.
- **`getfield` and `setfield` past one name** were `MATLAB:maxrhs`. R2025b's getfield.m indexes one
  level at a time (a cell a paren index, a text a field name, deblanked; anything else
  `MATLAB:getfield:InvalidType`); setfield.m makes one `subsasgn` and rethrows its refusal as
  `MATLAB:setfield` with the message kept, and checks the levels' types first
  (`MATLAB:setfield:InvalidType`). Too few arguments are `MATLAB:getfield:InsufficientInputs` and
  `MATLAB:setfield:InsufficientInputs`. `setfield(5, 'a', 1)` and `setfield` of a struct array are
  the dot write's refusals; here a JGraph sentence and every element written.
- **A table name past `namelengthmax`.** `table(…, 'VariableNames', {f})`, `t.Properties.VariableNames`,
  `array2table`, `timetable` and `addvars` kept a 2049-character name; R2025b refuses it as
  `MATLAB:table:VariableNameLengthMax` ("'…' is 2049 characters long, which is longer than the
  maximum allowed length for a table variable name, 2048 characters."), a dimension name as
  `MATLAB:table:DimNameLengthMax`. A row name is not limited. A text scalar as the one name of a
  one-variable table was refused here ("takes a cell of names").
- **`findobj(mc, …)` of a metaclass** answered the metaclass whatever was asked. R2025b walks the
  class and below it its properties, methods and events (`-depth` 0 and 1 are the class alone):
  `findobj(mc, 'Name', 'A')` is the 1-by-1 `matlab.metadata.Property`, a search that keeps nothing a
  0-by-0 `matlab.metadata.Class`. A search that keeps members of several kinds answers a
  heterogeneous `matlab.metadata.MetaData` array (`findobj(mc, 'Name', 'Lv324Cls')` is the class and
  its constructor), and the walk finds members no list holds: a value class's hidden `transpose`,
  `ctranspose`, `permute`, `reshape`, `display`, `disp`, `details` and `_colonobj`, and members
  without a `Name` (a bare `findobj(mc)` is `MATLAB:class:AmbiguousSuperclass`).
- **`containers.Map`'s `'UniformValues'`.** After the declared pair it is R2025b's
  `MATLAB:Containers:Map:IncorrectNumberInputs`, and a declared form whose third argument is not
  the other type's name its `Param` refusal ("Missing parameter name 'KeyType' or 'ValueType'.");
  after keys and values `true` keeps the inferred value type or refuses a mix
  (`UniformMismatch`) or a non-scalar (`UniformNonScalar`), `false` makes it `any`, and a value
  that is no logical or numeric scalar is `ExpectScalarLogical`; alone it makes an empty map. Here
  the pair was a key or ignored. Another name after the keys and values is ignored with R2025b's
  `MATLAB:Containers:Map:InvalidParameterName` warning.
- **calendarDuration.** `repmat` of one crashed (an invalid cast), `reshape`, `permute`, `circshift`,
  `flip`, `fliplr`, `flipud`, `rot90` and `squeeze` answered a plain struct array; `-x`, `+x`,
  `uminus` and `uplus` were refused; `calweeks(1.5)` and its kind answered fractions where R2025b
  refuses them per unit (`MATLAB:calendarDuration:NonintegerCalWeeksData` and the four others, in
  their own words), as `calendarDuration(Y, M, D, h, mi, s)` refuses a fractional year, month,
  day, hour or minute (`MustBeInteger`; seconds may be fractional) and a complex or text argument
  (`InputMustBeReal`, `InvalidCalDurationData`); the `'Format'` option was refused as a count of
  arguments, and the `Format` property took any text. R2025b takes the letters `yqmwdt` once each
  in that order (`UnrecognizedFormat`, `DuplicateFormatCharacters`,
  `OutOfOrderFormatCharacters` naming the order to use, `InvalidFormat` for a non-text), and a
  format without `m`, `d` and `t` warns `InvalidCalendarDurationFormatWarn` and takes them
  (`'md'` is `'mdt'`). `string` of a `NaN` element is the missing string (`char` and `cellstr`
  write `NaN`); an infinite one writes `Inf` or `-Inf` (here `Infinityy NaNmo`); `isnan`,
  `ismissing`, `isinf` and `isfinite` refused one. A zero element writes the smallest unit the
  array's other elements write (`[calmonths(1) calmonths(0)]` is `1mo`, `0mo`; seconds when one
  writes a time of day; `0d` alone). R2025b divides no calendarDuration
  (`MATLAB:math:mustBeNumericCharOrLogical`) and multiplies one by whole numbers only
  (`NonIntegerMultiplier`); here both scaled it. `string` and `cellstr` of an empty one were 0-by-0
  whatever its shape, and `calmonths(zeros(1, 0))` itself 0-by-0.
- **`datetime(0.5, 1, 1)`** answered; R2025b's components but the seconds are whole
  (`MATLAB:datetime:MustBeInteger`, "Year, month, day, hour, and minute components must be integer
  values."), as a date vector's first five columns are.
- **A signed year through `InputFormat`.** .NET's parser holds the years 1 to 9999, so
  `datetime('-0005-03-01', 'InputFormat', 'uuuu-MM-dd')`, `'0000-03-01'`, `'12345-03-01'` and
  `'0005-03-01 BCE'` with `yyyy-MM-dd G` were refused. R2025b's `u` takes a sign (`-5` under `u`,
  `-05` under `uu`), `y` none, `G` the eras `BCE` and `CE` (5 BCE is the year -4), and `'-0005-03-01'`
  with no InputFormat reads as `uuuu-MM-dd`; a plus sign, `BC` and `AD` are
  `MATLAB:datetime:ParseErr` ("Unable to convert '…' to datetime using the format '…'.").
- **datestr's letters.** R2025b's cnv2icudf.m reads `Y`, `D`, `f`, `h`, `q` and `s` as `y`, `d`, `F`,
  `H`, `Q` and `S`, so `datestr(738000, 'YYYY')` is `2020`, `'QQ-YYYY'` `Q3-2020`, `'Dd'` `28`,
  `'hh:mm'` `00:07` and `'HH:MM:ss'` `12:00:00`; they were literal text here. It refuses an hour,
  minute or second field of other than two letters, a millisecond field of other than three, a
  month field past four letters or broken, and a quarter beside anything but a year, in
  formatdate's words (`'Mmm'` is `MATLAB:formatdate:minuteFormat`). `datestr(3, 'yyyy')` (item 346)
  already answered `0000` since ADR 0375.

## Decision

### Dynamic names resolved before the roads (`Interpreter.WithInertNamesResolved`)

In the MATLAB dialect a write target that is not a variable or a paren on one has each dynamic
field name that reads only data replaced by the static name it holds, once, before the right-hand
side (R2025b's order for those shapes); a name that may run script code was already pre-evaluated
(`PrepareTarget`). Every road then treats `s.(f)` as `s.a`.

### The field writes (`Interpreter.Writes.cs`, two hooks in `EvaluateAssign`)

`RefuseFieldIndexWrite` refuses `s.a(…) = v`, `s.a{…} = v` and `s(k).a(…) = v` on a value with no
dots and on a struct array, in R2025b's identifiers. `RefuseEndOfUnboundField` refuses an `end`
reading a field of an unbound name or an absent field. `IsAbsentField` counts `{}` and `''` as a
struct's start and reads `s(k).a` on a struct (array) without `a`, or no `s`, as absent.
`ResolveStructElementForWrite` refuses `s(2).a = v` on a value with no dots and starts a struct
from `{}` or `''`. `ConjuredEmptyOf` is the empty an index write conjures for a variable or a field
in the MATLAB dialect: of the right-hand side's char, logical or integer or single class.
`ClassifyLevel` reads an inert subscript's position (`InertPosition`) where it read only a literal,
and counts `{}` and `''` as a field's start.

### getfield and setfield (`JgsBuiltins.Subscripts.cs`, `WrapFieldVerb`)

In the MATLAB dialect the two are re-registered over themselves after `subsref` and `subsasgn`: a
form past one name, a cell level, a target that is no scalar struct (setfield) or too few
arguments takes R2025b's file's road through the subscript machinery; the plain forms keep the
builtins (an object's property, the cut long name, the string value).

### Table names (`JgsBuiltins.RefuseLongTableNames`)

The constructor's `VariableNames` (`BuildTable`, also `timetable` and `array2table`), the
`Properties` write (`WithTableProperties`, variable and dimension names) and `addvars` refuse a
name past `NameLengthMax` in the MATLAB dialect. `BuildTable` takes a text scalar as one name.

### findobj of a metaclass (`JgsBuiltins.FindInMetaclass`)

`findobj` hands a scalar metaclass to `FindInMetaclass`, which reads the class, then its
`PropertyList`, `MethodList` and `EventList` (the last three from `-depth` 2), each element through
`MetadataSearchKeeps`. One kind kept is its column (the class itself as it is), none the 0-by-0
class; several kinds are refused as not modelled (`JGraph:findobj:HeterogeneousMetadata`).

### containers.Map (`MapUniformValues`, `UniformMapValueType`)

Taken off the arguments before the forms are read, in the MATLAB dialect, as above.

### calendarDuration (`JgsBuiltins.CalendarLeftovers.cs`)

`RefuseCalendarUnitData` (the single-unit constructors), `CalendarConstructorOptions` and
`RefuseFractionalCalendarParts` (`calendarDuration`), `CalendarDisplayFormat` (the option and the
`Format` setter in `SetTimeProperty`), `CalendarSign` (the unary operators in `ApplyUnary`,
`uminus`, `uplus`), `CalendarZeroUnit` (`TimeText` of a zero element in an array),
`InfiniteCalendarText` (`FormatCalendarMatlab`), `IsNaNCalendarElement` (`string`), and
`WrapCalendarVerbs` re-registering `repmat`, `isnan`, `ismissing`, `isinf` and `isfinite` over
whatever they were, after `WrapSparseFunctions` and before `RefuseGroupsAtAxesVerbs`.
`GatheredStructs` (the shape verbs' struct road) puts a calendarDuration's tag back.
`CalendarBinary` refuses a division and a fractional multiplier and keeps a zero component zero
under `Inf`. `CalendarValue` keeps an empty model's shape in the MATLAB dialect; `string` and
`cellstr` read a calendarDuration's own shape.

### datetime (`RefuseFractionalDatetimeParts`, `SignedYearMoment`)

`ComponentwiseMoments` and the date-vector form refuse a fractional component but the seconds.
`ParsedMoment` tries `SignedYearMoment` when .NET's parser fails, in the MATLAB dialect: the format
is made a pattern with named groups, the year read signed under `u`, an era applied, and the moment
built by `JgsTime.FromComponents` (which holds any year); a year .NET holds is left to .NET's
refusal, now worded as R2025b's `ParseErr` when an InputFormat was given.

### datestr (`JgsBuiltins.CanonicalDatestrFormat`)

In the MATLAB dialect the format is read into the formatter's letters as cnv2icudf.m reads it, with
an AM or PM marker set aside, and its fields' counts checked, replacing ADR 0375's year count.

## Consequences

- New parity fixture `oi_dynamic_fields_calendar_years` (450 rows from R2025b 25.2, every row
  agreeing), green in the packed and boxed lanes.
- `OwnershipGateTests`' allowed raw write for a conjured variable names `ConjuredEmptyOf`.
- In MATLAB code datestr reads the letters `Y`, `D`, `f`, `h`, `q` and `s` and refuses a field of
  the wrong count, a `containers.Map` call with an unknown name after its keys and values warns,
  and `getfield`/`setfield` of a struct array or a non-struct follow R2025b's files.
- A MATLAB script that created a field through a dynamic name, indexed into a variable's struct
  element through a variable, or conjured a char, logical or integer array by an index write now
  gets R2025b's answer; one that wrote a field onto a number, a struct array or past an absent
  field's end, gave a calendarDuration a fractional count, divided one, gave a datetime a
  fractional component, or named a table variable past 2048 characters now stops where R2025b does.

## Divergences

None opened.

## Still open

- **The display of a calendarDuration** is JGraph's own (`[1mo, 2mo, 3mo]`, `x = 2mo`,
  `s = struct(a: [1mo, 2mo, 3mo])`, `t = table[3x1]`), as every class's is here (a double row is
  `x = [1, 2, 3]`, a duration `[1 hr, 2 hr, 3 hr]`). R2025b's block display, measured in every
  context (probes p8d2 c1 to c30, p8d4 k18 to k35): `disp` right-aligns each element to the widest in
  fields three spaces apart (`   10mo   11mo     1y`) and splits columns past the window width; a
  statement adds `x = ` and the `1×3 calendarDuration array` header (`0×0 empty calendarDuration
  array`); a cell writes `{[1mo    2mo    3mo]}` (four spaces, unpadded), `{[2mo]}`, `{[NaN]}`,
  `{2×2 calendarDuration}`; a struct field `a: [1mo    2mo    3mo]`, `a: 2mo`, `a: [2×2
  calendarDuration]`; a table column right-aligns under its header. Matching it is a decision about
  the whole display, not this type's.
- **A heterogeneous `findobj(mc, …)`** (above) and R2025b's hidden members of a class.
- **Field writes not settled here:** `s.a(:) = 1` and `x(:) = 1` with nothing there are 1 in
  R2025b and empty here; `s.a(3) = []` and `x(3) = []` with nothing there are R2025b's
  `MATLAB:badnullindex` ("Deletion requires an existing variable.") and empty here;
  `s(2).a{2} = 1` on a struct without `a` is `MATLAB:cellAssToNonCell` here; `s.a{end+1} = 1` with no
  `s` creates the field here (R2025b's unresolved name); `x(end+1) = 1` with no `x` is R2025b's
  `MATLAB:lang:EndUsedOutsideIndexingExpression` and 1 here.
- **A long dynamic name on a table**: `t.(f) = 2` with a name past 2048 characters is cut with the
  struct warning before the table sees it (ADR 0375's `FieldName`); R2025b refuses it as
  `VariableNameLengthMax`. `table(…, 'DimensionNames', …)` is not taken by the constructor here.
- **calendarDuration operators R2025b refuses or reads otherwise**: `==` of two is
  `MATLAB:math:mustBeNumericCharOrLogical` there and compared here; `2 \ calmonths(4)` reaches the
  numeric division here; `calmonths(3) + 0` is `3mo` there and refused here.
- `datetime(Inf, 1, 1)` writes `Inf` in R2025b and a year four million here.
