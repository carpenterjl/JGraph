# ADR 0358 — A containers.Map joins by its own verbs, cell2struct takes a dimension, a metaproperty's missing default raises, and an object inside a struct or a cell shows its size and class

## Status

Accepted, 2026-11-14. Open items 317 and 319 (chips wave 4, agent X): the leftovers ADR 0351 and ADR 0353
listed.

## Context

- **Item 317, maps in a bracket.** `[m m]` and `[m1; m2]` of two maps made a struct array here (a map is
  a classed struct). R2025b's Map class has its own `horzcat`, `vertcat` and `cat`. Probes `p317`,
  `p317b`, `p317c` and `p317e` (R2025b 25.2, `-batch -noFigureWindows`) found:
  - side by side is `MATLAB:Containers:Map:HorzcatNotSupported`, a grid too; `horzcat(m)` and
    `cat(2, m)` of one map are that map;
  - stacked is a new map (`[m1; m2] == m1` is false, a write to it leaves both alone): a copy of the
    first, then every entry of each later map written in, in key order, so a later key wins. Each key
    and value is converted to the first map's `KeyType` and `ValueType` as `m(k) = v` converts it (5
    into a logical map is true, an int32 into a double map a double, 1.5 into an int32-keyed map the
    key 2), or refused as `MATLAB:Containers:TypeMismatch` ("Specified key type …" or "Specified value
    type does not match the type expected for this container."). The result keeps the first map's
    types. `vertcat(m)` and `cat(1, m)` of one map are a copy;
  - a piece that is not a map after the first is the method's argument validation:
    `MATLAB:class:RequireClass`, "Invalid input for argument N (varargin): Value must be
    'containers.Map scalar'.", N its place (an empty `[]` included: `[m; []]` is refused). A first piece
    that is not a map is R2025b's conversion through the constructor: `MATLAB:UnableToConvert`, "The
    following error occurred converting from double to containers.Map: Incorrect number of inputs
    specified.";
  - leading empties leave the bracket first: `[[]; m]`, `['', m, []]`, `[{}; m]`, `[struct([]); m]`
    are `m` itself;
  - `cat(3, m1, m2)` is `MATLAB:Containers:Map:NDCatNotSupported`; `cat` validates every piece as a
    map before it (`cat(3, [], m)` refuses argument 2, `cat(1, 5, m)` argument 2).
- **Item 317, a for loop over a map** ran once here. R2025b refuses it, whatever the Count:
  `MATLAB:Containers:Map:IncorrectIndexing`, "Only one-dimensional indexing is supported.".
- **Item 317, a map inside a struct or a cell** printed its storage (`struct(keys: {…}, values: …)`).
  R2025b writes `m: [2×1 containers.Map]` in a struct and `{2×1 containers.Map}` in a cell (`[0×1 …]`
  for an empty map).
- **Item 317, cell2struct** took one row or column of values. Probes `p317d` and `p317e` measured R2025b's
  `cell2struct(c, fields, dim)`: `dim` is 1 when not given; the struct array is `c`'s size with `dim`
  taken out, a column when one dimension is left (`cell2struct(cell(2,3), f2, 1)` is 3-by-1, of a
  2-by-3-by-4 cell along 2 a 2-by-4), a dimension past `c`'s own a slice of one. The names are a cell of
  char rows, a char matrix's rows as they stand (`['a '; 'bc']` refuses "a "), or a string array read as
  `char` reads it (`""` names nothing). The refusals, in R2025b's order: `minrhs`/`maxrhs`;
  `FirstArgNotACell`; the dimension (`badopt` for anything but a double, logical and integer classes
  included; `NonScalarInput`; `DimensionNumber` for an empty, zero or negative one; `nonaninf`;
  `NonIntegerInput`); the names (`badopt` for a number, `MissingNotSupported`); `NumberOfNamesMismatch`;
  then every name (`NonStringVector` for a char matrix in the cell, `NonStringFieldNames` for any other
  non-char or empty, `InvalidFieldName`) before any duplicate (`MATLAB:DuplicateFieldName`, 'Duplicate
  field name "a"', no period).
- **Item 317, the ownership audit** (`tools/ownership/audit-ownership.py`) raised ValueError in
  `registrations()`. Its comment-and-string stripper ended an interpolated string at the first quote
  inside a hole (`$"…{folder.Replace("'", "''")}…"` in `JgsBuiltinClasses.App.cs`), so the rest of the file
  was read inside out and a registration's opening parenthesis was never found.
- **Item 317, `jgraph.exe -batch "<inline code>"`** runs the text as JGS. ADR 0030 decided it ("The
  statement is JGS unless it names a file"), so it is left.
- **Item 319, DefaultValue.** A metaproperty with no default read a 0-by-0. Probe `p319`: R2025b raises
  `MATLAB:class:NoDefaultDefined` ("No default value has been defined for property B."), for a typed
  property, a dependent one and a handle class's too, and `MATLAB:class:NoDefaultPermitted` ("Property
  Shape is abstract and therefore cannot have a default value.") for an abstract one. `getfield(p,
  'DefaultValue')` and `{mc.PropertyList.DefaultValue}` raise for the first property without one.
- **Item 319, deep member lists.** A metaclass is a struct made to `MetaDepth` 2, so
  `mc.MethodList(1).DefiningClass.PropertyList(1).DefiningClass.MethodList` had no field. R2025b's
  metadata are handles and reach any depth.
- **Item 319, an object inside a struct or a cell** printed its whole property display. R2025b writes
  `[1×1 Mc196Val]` in a struct field and `{1×1 Mc196Val}` in a cell: the size and full class name, a
  handle's and a deleted handle's too, `[1×2 …]`, `[0×0 …]` for arrays, `[1×1 matlab.metadata.Class]` and
  `[11×1 matlab.metadata.Property]` for metadata. A member of an enumeration is its name (`o: Low`), and
  in a cell a row of members is bracketed (`{[Low]}`, `{[Low    Low]}`; `[Low    Low]` in a field).
- **Item 319, `mustBeNonzeroLengthText`** was not a builtin. R2025b's `.m` is `istext` then
  `all(strlength(text) > 0, 'all')`, both refused under `MATLAB:validators:mustBeNonzeroLengthText`
  ("Value must be a character vector, string array, or cell array of character vectors." / "Value must
  be text with one or more characters."). Probe `p319b`: a missing string, `""`, `blanks(0)` and a cell
  holding `''` are empty text; a char column, a char matrix, a 0-by-1 char, a cell holding a string or a
  char matrix, a number and `[]` are not text; `{}` and `strings(0)` pass. `nargin` 1, `nargout` 0.

## Decision

### A bracket with a map (`JgsBuiltins.JoinMaps`, `JgsBuiltins.MapConcat.cs`)

`AssembleMatrix` and `BuildArrayLiteral` ask `AnyMatlabMap` before the struct join, in the MATLAB
dialect, with the pieces before empties are dropped. `JoinMaps` joins each row side by side, then
stacks the rows, by `JoinMapsAlong`: leading empties leave, a map alone after them is itself, a first
piece that is not a map is `UnableToConvert`, a later one `RequireClass`, side by side
`HorzcatNotSupported`, stacked `MergeMaps`. `MergeMaps` makes a new map of the first's types holding its
entries, then writes every later entry through `MapKeyAs` and `MapValueAs`, which convert or refuse as
measured. The bracket verbs (`Interpreter.BracketVerbs.cs`) give `vertcat(m)` a copy (`JoinOneMap`) and
`cat(dim, …)` with a map among its pieces to `CatMaps`. A JGS script keeps its struct join.

`KeysAsked` now reads a 1-by-1 numeric array as the number it holds: `containers.Map([1.5], 2)` and a
computed key were refused by `Put` as an array.

### A for loop over a map (`Interpreter.ExecuteForOverArray`)

`IsMatlabMapShape` is asked before the struct walk and raises `MapIncorrectIndexing`.

### cell2struct (`JgsBuiltins.CellToStructAlong`)

In the MATLAB dialect `cell2struct` is R2025b's, with `CellToStructDimension` and `CellToStructNames`
checking in R2025b's order. Each slot shares its value with the cell (M2). `cell2struct` joins
`StringAwareBuiltins`, so a string array of names reaches it as one. The JGS dialect keeps its one-row
form.

### A metadata member a field cannot answer (`JgsBuiltins.MetadataMember`)

`MemberOf` asks it for a metadata value in the MATLAB dialect, `StructArrayFieldValues` for a list's
`DefaultValue`, and `getfield` for `DefaultValue`:
- `DefaultValue` of a metaproperty whose `HasDefault` is false raises `NoDefaultDefined`, or
  `NoDefaultPermitted` when it is `Abstract`;
- a member list (`PropertyList`, `MethodList`, `EventList`, `EnumerationMemberList`, `SuperclassList`,
  `InferiorClasses`) of a `matlab.metadata.Class` rendered without one is rendered then from the class's
  name to `MetaDepth`, so a chain goes as deep as it is read. `MetaDepth` itself stays 2: the struct is a
  tree, and a deeper one would make every walk of it (isequal, a display) grow by the member count at each
  level.

### An object or a map inside a struct or a cell (`JgsValue.MatlabNestedSummary`)

`NestedDisplay` asks it after secretID's compact display, in the MATLAB dialect: a map is
`[Count×1 containers.Map]`, a metadata value `[size class]`, an object `[size class]` (an array's own
size, a deleted handle as a live one), a row of enumeration members their names. In a cell the brackets
are left off, since this build's cell display writes its own braces around each element, as for
secretID (ADR 0353). The struct and cell layout around the fragment stays this build's (ADR 0232).

### mustBeNonzeroLengthText (`JgsBuiltins.Validators.cs`)

A builtin, a `StringAwareBuiltin` so a missing string reaches it as one, with a catalog entry, a nargout
row (0, file) and dispatch sweep 45x.

### The ownership audit (`skip_literal`, `skip_hole`)

`strip_code` blanks a literal through `skip_literal`, which skips an interpolated string's holes as code
(`skip_hole`, nested literals included) and ends a raw string at as many quotes as opened it. The audit
now runs to its report; it names two minting problems (`cell`, `repmat`) that its crash had hidden.

## Consequences

- New parity fixtures `oi_map_concat_cell2struct` (231 rows from R2025b 25.2, one a divergence) and
  `oi_metadata_nested_display` (112 rows, none a divergence), green in the packed and boxed lanes.
- New tests: `MapStructMetadataLeftoversTests` (6 cases).
- `MatlabArrayBuiltinTests.StructHelpers_MoveBetweenStructsAndCells` called `cell2struct({7, 8}, {'x',
  'y'})`, which R2025b refuses (`NumberOfNamesMismatch`: the fields run down dimension 1); it now passes
  a column and tests `dim` 2.
- `oi_object_arrays`'s `DISPLAY_OBJ_PROP` divergence row prints the nested array as `[1×3 Val116]` now.
- A map stacked on another is a map; a script that made a struct array of maps by `[m1; m2]` gets one map.
- A for loop over a map is refused in the MATLAB dialect.

## Measured

- Probes `p317` to `p317e`, `p319`, `p319b`, `p319c` in both engines agree on every row of these items
  after the change, but for the rows below and those this ADR leaves open.

## Divergences

- **A map after a leading empty and before another map**: `[[]; m1; m2]` is
  `MATLAB:class:RequireScalar` in R2025b ("Invalid input for argument 1 (mapObj): Value must be a
  scalar.") and `[[]; m1; []; m2]` a refusal of `size`'s argument; here the empties leave and the maps
  merge, as they do without the leading empty. Fixture row `vcat_empty_then_two_maps`.

## Still open

- `m(k) = v` itself converts nothing and refuses nothing: a char value into a double-valued map is
  stored. Only the merge converts.
- An empty `GetMethod` or `SetMethod` is a 0-by-0 double; R2025b's is an empty function handle, which
  this build's value model does not have (a value-model change).
- The metaclass of a built-in type lists the names `methods` gives; R2025b lists 279 for a double, 95 for
  a char, 47 for a cell (probe `p319`), each with its signature. On the recording machine the list
  includes every installed toolbox's overloads (`diag` comes first), so it is not one list to record.
- `exist('secretID')` stays 5 (ADR 0353's divergence): secretID is a `.m` in R2025b
  (`toolbox\matlab\authnz\secretapis\publicBuiltins\secretID.m`), and the standing rule (ADR 0149) answers
  5 for every built-in here that is a file there, `table` and `mustBeNonzeroLengthText` among them.
- `struct(p)` of a metaproperty is refused here; R2025b's struct has no `DefaultValue` field.
- A metaclass rendered past `MetaDepth` makes its lists when read, but `fieldnames` and `isfield` of it
  do not list them until then.
- `s(:)` of a 1-by-N struct array is 1-by-N here (R2025b N-by-1), so `arrayfun(f, s(:))` is a row:
  that, and not arrayfun, is the 1-by-N item 319 saw (every N-by-1 struct array probed answers a column).
- A uniform `arrayfun` refuses a 1-by-1 cell or struct answer; R2025b collects them.
