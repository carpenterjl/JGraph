# ADR 0351 — A containers.Map is a column of its keys, a field keeps its string, and a dynamic field name is checked as R2025b checks it

## Status

Accepted, 2026-11-13. Open items 200, 201, 202 and 301, and the dynamic-field rows of open item 208
(chips wave 3, agent P). Items 200 to 202 are the engine answers ADR 0266 saw through the native
bridge; item 301 was found by wave 1 (ADR 0341).

## Context

- **Item 200.** `size(containers.Map({'a','b'}, {1, 2}))` is `[2 1]` in R2025b and was `[1 1]` here.
  The class's own `size`, `length` and `isempty` (`@Map\size.m` and the rest, built in) make a map a
  column of its keys: `size(m)` is `[Count 1]`, `size(m, 1)` the Count and `size(m, d)` 1 past it,
  `[r, c, d] = size(m)` is `[Count 1 1]`, and `isempty` is a Count of zero. `numel` is not overloaded
  and stays 1. Probe `p200` (52 lines) found more that follows from that `size`: `isscalar` is false
  for any Count but one (an empty map included), `isrow` likewise, `iscolumn` and `isvector` are true,
  `whos` reports `2x1` and 8 bytes, a function handle's `size` inside `cellfun` sees the column, and
  cellfun's named questions (`'isempty'`, `'length'`, `'prodofsize'`, `'ndims'`, `'size'`), which call
  no class's own, see one object. The method takes one dimension, a scalar: `size(m, [1 2])` is
  `MATLAB:class:InvalidInputArgumentTypeSizeOrValue` and `size(m, 1, 2)` is `MATLAB:maxrhs`.
- **Item 201.** `struct` and `setfield` were builtins without `KeepsStringArguments`, so a string
  value arrived as the char row it stands for: `class(getfield(struct('s', "text"), 's'))` was `char`
  and its `length` 4. A cell value kept its strings already.
- **Item 202.** R2025b refuses a dynamic field write whose name no field may have:
  `s.('1bad') = 1` is `MATLAB:AddField:InvalidFieldName` ("Invalid field name: '1bad'."), and so are
  a space, an empty name, a leading underscore, a leading or trailing blank and a non-ASCII letter.
  A keyword (`'if'`, `'end'`) is a field name, and so is a name of 64 letters, past `namelengthmax`.
  Probe `p202`/`p208t` found the same refusal through every road a write takes (an element, a nested
  dot, an indexed write, `deal`, a struct in a cell, a variable not yet made, which stays unmade), and
  in `setfield` (R2025b's is `s.(deblank(name)) = v`). `struct('1bad', 1)` and `cell2struct` are
  `MATLAB:Cell2Struct:InvalidFieldName` ('Invalid field name "1bad".'), an empty name or a string
  array of names `MATLAB:Cell2Struct:NonStringFieldNames`. Reading an absent name is
  `MATLAB:nonExistentField`, whatever its spelling; reading a present one no field may have, which only
  a struct made outside the language can hold, is `InvalidFieldName` (`k10_feval` risky 11 and K16's
  `in.struct.badname`). `getfield` of an absent name is `nonExistentField` (it is `s.(deblank(name))`),
  `rmfield` refuses an absent one (`MATLAB:rmfield:InvalidFieldname`, "A field named '1bad' doesn't
  exist."), and `orderfields` a list that is not a rearrangement
  (`MATLAB:orderfields:CellStrMismatchFieldnames`). JGraph wrote and read every such name, answered
  `rmfield` unchanged and refused the rest in its own words. A table's variable may be any text and is
  not checked.
- **Item 301.** `struct2cell(s)` of a scalar struct with n fields was 1-by-n here and is n-by-1 in
  R2025b. Of a struct array it is fields by the array's own size: 2-by-1-by-2 for a 1-by-2 array of two
  fields, 2-by-2-by-3 for a 2-by-3, 1-by-0-by-0 for an empty one with one field, 0-by-1 for `struct()`,
  0-by-0-by-0 for `struct([])`. Here a struct array answered its first element's row.
- **Item 208's dynamic-field rows.** `s.(["a" "b"])`, read or written, is
  `MATLAB:index:dotParenArgsMustBeStringScalarOrCharacterVector` in R2025b ("Dynamic field or property
  name must be a string scalar or character vector."), and was `MATLAB:mustBeFieldName` here. A char
  matrix names the field its characters spell in column order: `s.(['a'; 'b'])` is `s.ab`,
  `s.(['ab'; 'cd'])` is `s.acbd`, a 1-by-1-by-2 char is its two characters and a 0-by-1 char the empty
  name. A missing string is `MATLAB:string:MissingNotSupported`, on a table too. A table refuses a
  string array and a char matrix as it refuses a cell (`MATLAB:table:IllegalVarSubscript`), as here.

## Decision

### A map's shape in the MATLAB dialect (`JgsBuiltins.IsMatlabMapShape`)

`IsMatlabMapShape(value, out count)` is true for a `containers.Map` read in the MATLAB dialect. The
builtins that answer for a map's shape ask it first: `size` (its one- and two-argument forms, and its
refusals), `isempty`, `isscalar`, `isrow`, `iscolumn` (`isvector` already answered true), `whos`
(size and 8 bytes) and cellfun's named questions (`AskOfCell`), which answer for the one object.
`SizeDims` is not changed: its 208 callers are numeric routines, argument validation and display,
and a property validated `(1,1)` was not measured. `numel` and `length` were already right. A JGS
script keeps its 1-by-1 map.

### struct and setfield keep strings

Both names join `StringAwareBuiltins`. `struct` reads a name as a char row or a string scalar
(`StructNameArgument`), and in the MATLAB dialect refuses anything else, and an empty name, as
`NonStringFieldNames`, and a name no field may have as `InvalidFieldName`. `setfield` takes a string
name too.

### Field names (`JgsBuiltins.IsValidFieldName`, `Interpreter.WritableFieldName`)

`IsValidFieldName` is an ASCII letter followed by ASCII letters, digits and underscores, any length,
keywords allowed. In the MATLAB dialect:
- every struct write by a dynamic name passes `WritableFieldName`: `AssignToMember`, the nested dot of
  `ResolveStructForWrite` (before it makes the child), and `EvaluateForWrite` (an indexed write's
  field). A write to a variable not yet made checks the name before making it. A variable that is
  there but is no struct keeps its own refusal first, as in R2025b.
- a read of a present field no field may have is refused (`InvalidFieldNameRead`) in `MemberOf` and
  `StructArrayFieldValues`; an absent one is `nonExistentField` as before, a struct array's now too.
- `setfield` deblanks and checks the name; `getfield` deblanks it, answers `nonExistentField` for an
  absent one and refuses a present invalid one; `rmfield` follows rmfield.m (`RemovableFieldNames`):
  it deblanks, refuses the first absent name, and lets one name go unremoved from a struct with no
  fields; `orderfields` refuses a list as long as the fields that is not a rearrangement of them;
  `struct` and `cell2struct` refuse in `Cell2Struct`'s words.

The value model is not checked: MAT-file reading, `jsondecode`, tables, interop and every other
builder that makes a struct internally make the names they make.

### struct2cell answers fields by size (`StructFieldsByElement`)

In the MATLAB dialect `struct2cell` answers a cell of one row a field, in the struct's field order,
reshaped to `[fields, size(s)]`, each slot a share of the field's value. Anything but a struct is
`MATLAB:UndefinedFunction` in R2025b's words. A JGS script keeps its row of the first element.

### A dynamic name that is a string array or a char matrix (`Interpreter.FieldName`)

`FieldName` answers a sentinel for each: `\u0001A` for a string array of other than one element and
`\u0001C` and the characters in column order for a char matrix, beside open item 130's numbered ones.
A missing string scalar is refused there, for every target. `RefuseNumberedField` turns the char
matrix's sentinel into its name and refuses the string array's in R2025b's words; `TableVariableAt`
refuses both as `IllegalVarSubscript`. `StructArrayFieldValues` settles the sentinel too, so the
comma-separated list (`{t.(['a'; 'b'])}`) reads `ab`.

## Consequences

- New parity fixture `oi_struct_map_shapes` (203 rows from R2025b 25.2, every one agreeing, none a
  divergence), green in the packed and boxed lanes.
- `rmfield` of an absent name, `getfield` of an absent name and a struct array's absent field now
  refuse in R2025b's words in the MATLAB dialect. A script that relied on `rmfield` passing over a
  missing name is refused, as it is in R2025b.
- `struct2cell` of a scalar struct is a column; code that took its row and transposed it now gets a
  row, as in R2025b.
- ADR 0266's divergence for items 200 to 202 is closed.

## Measured

- The probes `p200`, `p201`, `p202`, `p208`, `p208t` and `p301` in both engines agree on every row of
  these items after the change; the rows that still differ are listed below.

## Divergences

None opened.

## Still open

- **Concatenating maps**: `[m m]` is `MATLAB:Containers:Map:HorzcatNotSupported` in R2025b and makes
  a 1-by-2 struct array here; `[m1; m2]` merges the maps in R2025b and is a 2-by-1 struct array here.
- **A `for` loop over a map** is `MATLAB:Containers:Map:IncorrectIndexing` ("Only one-dimensional
  indexing is supported.") in R2025b and runs once here.
- **A map shown inside a struct or a cell**: `disp(struct('m', m))` shows `m: [2×1 containers.Map]` and
  `disp({m})` `{2×1 containers.Map}` in R2025b; here the map's storage is printed.
- **A struct array's transpose**: `size(s')` of a 1-by-2 struct array is `[1 2]` here and `[2 1]` in
  R2025b, so `struct2cell(s')` is 2-by-1-by-2 here.
- **`cell2struct` of more than a column of values** (`cell2struct(struct2cell(sa), f, 1)` of a struct
  array, and its `dim` argument) is refused here.
- **A write to a non-struct** (`x = 5; x.a = 1`) gives R2025b's words without its identifier
  `MATLAB:structAssToNonStruct` (open item 208, agent N's rows).
- **`rmfield` of a name longer than `namelengthmax`** is `MATLAB:rmfield:FieldnameTooLong` in R2025b;
  here it is the absent-name refusal.
