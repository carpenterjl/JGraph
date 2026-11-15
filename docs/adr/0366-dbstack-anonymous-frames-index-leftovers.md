# ADR 0366 — dbstack lists an anonymous body and a script as frames, inmem lists the scripts run, and the indexing leftovers after ADR 0354 are R2025b's

## Status

Accepted, 2026-11-14. Open items 302 and 320 (chips wave 5, lane 5A).

## Context

- **Item 302.** `dbstack` inside an anonymous function answered the functions alone: the compiled
  K0 probe's `call.dbstack` row (`k0_try('call.dbstack', @() numel(dbstack))`) was 1 here and 3 in
  R2025b. Probes `p302`, `p302x`, `p302y`, `p302z`, `p302v` and `p302i` (about 90 lines) measured
  R2025b 25.2:
  - an anonymous function's body is a frame of its own, named by its text as `func2str` writes it
    (`@()dbstack`, `@(v)frame2name(v,'x')`), its file the file that wrote it, its line the line
    running in its body; a handle made by `eval` or `str2func` has line 0. `callit(@() dbstack)` is
    three frames: the handle, `callit` at the line that called `fn()`, the script at the call;
    `cellfun` and `arrayfun` are no frames;
  - a script run by name is a frame (`p302s@9 ; p302@37`), as the -batch file was already;
  - the struct's `file` is the file's name alone (`p302.m`), its whole path under `-completenames`;
    JGraph gave the path always;
  - printed, only the running frame is marked (`> In`); the others begin `In`, and `dbstack(1)`
    leaves the mark out; an anonymous frame prints `file>@()body` with no line; a script's local
    function prints `script>loc` (JGraph printed `loc` for the -batch file, whose functions are
    stored under no source id);
  - `dbstack(n)` takes a nonnegative integer of any numeric class and `'-completenames'` as written,
    one of each. The count as text (`dbstack 1`), other text, a logical or a cell is
    `MATLAB:err_illegal_dbstack_argument`, an array `MATLAB:ScalIntReq`, a fraction `MATLAB:IntVal`.
    JGraph took `'1'` and refused a number;
  - `inmem` lists the -batch file and every script `run` read, in load order with the functions
    (`p302i, p302s, p302f, p302r`), until `clear name` or `clear functions` lets them go; a running
    one stays. JGraph listed scripts named on the path only.
- **Item 320.** ADR 0354's "Still open" list. Probes `p320` (110 rows), `p320b` to `p320e` (60 more)
  ran every row in both engines:
  - a plain struct written by index into a variable that holds data is
    `MATLAB:invalidConversion` naming its class (`x = []; x(2, 2) = struct('a', 1)` is "Conversion
    to double from struct is not possible.", on every subscript road, `{}` "to cell", `''` "to char",
    `int8([])` "to int8", `5` too), and so is a field (`s.f = []; s.f(2) = t`) or a cell's slot
    (`c = {[]}; c{1}(2) = t`); a variable the write creates starts a struct array; JGraph started one
    from any `[]` and mixed a struct into `x = 5`. A string target is `MATLAB:UnableToConvert`;
  - `c{i, j, k} = v` naming several cells is `MATLAB:index:expected_one_output_for_assignment`
    ("Assigning to 2 elements ...", 0 for `c{[], 1, 1}`); JGraph wrote them all;
  - a field write one level down carries the variable level's identifiers:
    `MATLAB:scalarStrucRequired` or `MATLAB:emptyDotAssignment` over a struct array (also reached
    through a cell, `c{1}.a = 5`), `MATLAB:structAssToNonStruct` over text, a number, a cell or a
    function handle (JGraph wrote a handle's field). A string refuses the field written as no
    property (`MATLAB:noPublicFieldForClass`, "Unrecognized property 'f' for class 'string'.") and a
    field read on the way to a deeper write as `MATLAB:noSuchMethodOrField`. An empty cell or char
    turns into a struct as `[]` does (`x = {}; x.f = 1`, `st.t = ''; st.t.f = 1`); JGraph refused;
  - `cell(2, 2, 2)` is a 2-by-2-by-2 cell, and `cell(n)` is n-by-n (JGraph made a 1-by-n row);
    sizes as a vector, an empty size 0, a negative 0, `MATLAB:nonaninf` and `MATLAB:NonIntegerInput`;
  - `'a':'c'` is `'abc'` and `'a':2:'e'` is `'ace'`, in a loop and as a subscript too; both ends must
    be char (`MATLAB:colon:mixedCharOperands`), one character each, a double step whole, and a string
    is `MATLAB:colon:inputsMustBeNumericCharLogical`;
  - reshape's refusals are `MATLAB:getReshapeDims:*` and `MATLAB:checkDimCommon:nonnegativeSize` in
    R2025b's words;
  - a cell's read with two subscripts or more past the end, or not a position, names the position
    (`Index in position 1 exceeds array bounds. Index must not exceed 2.`);
  - `T(2, "p")` reads the variable a string names, and `T(2, ["p" "q"])`; an unknown name is
    `MATLAB:table:UnrecognizedVarName`, an unknown row `MATLAB:table:UnrecognizedRowName`, and the
    name's case must match (`T(2, "P")` is refused);
  - `x(:)()`, `x(1)()`, `f()(2)`, `magic(3)(1)` and `x(1){1}` are `MATLAB:mir_paren_index_must_be_last`
    when R2025b reads the file. JGraph reads the chain they spell.

## Decision

### An anonymous body and a script run by name are frames (`Interpreter.Frames.cs`)

`Interpreter` keeps `_otherFrames`, the frames that are no function call, each with its place among
`_activeFunctions` (the count running when it was entered) and the line it was entered from.
`AnonymousFunction.CallMultiple` enters one around its body in the MATLAB dialect
(`EnterAnonymousFrame` / `LeaveOtherFrame`); `RunScriptFile` takes the line that named the script
(`callLine`, passed by the path's script callable) and enters one too (`EnterScriptFrame`).
`CurrentStack` interleaves them with the functions, innermost first, each frame at the line running
in it and the next at the line it was entered from; a handle's frame is its `MatlabText` and its
file, at line 0 when `AnonymousFnExpr.InText` (the parser stamps a handle written in `eval`'s text,
which `str2func` reads too). The error stack (`StackOf`) is unchanged.

`dbstack`: the struct's `file` is `Path.GetFileName` unless `-completenames`; the printed form marks
the first frame only when nothing was left out, prints an anonymous frame as `file>@()body` with no
line (`AnonymousFrameName`); the MATLAB dialect's arguments are R2025b's (`DbstackArguments`), JGS
keeps its words. `IsLocalFunctionOf` reads the -batch file's storage, kept under no source id.

### inmem lists the scripts run as programs (`ScriptsRun`, `NextLoadOrder`)

`Run` notes a MATLAB `.m` or P-file it starts (`NoteScriptRun`: the -batch file by `MainScriptPath`,
a file `run` read by its path). Every loaded file takes a place in one load order
(`JgsFunctionPath.Loaded.Order`), and `inmem` lists the path's files and the scripts run sorted by
it. `clear functions` / `clear all` (`ForgetScriptsRun(null)`) and `clear name` let a script run go
unless it is running.

### The indexing leftovers (`Interpreter.IndexLeftovers.cs`, `JgsBuiltins.IndexLeftovers.cs`)

- `RefuseStructIntoData`, at the index assignment before anything is conjured: a plain struct on the
  right of `x(…) = s` where the container already holds data is `MATLAB:invalidConversion`. The
  container is peeked without evaluating anything (`PeekWriteContainer`): a variable, a literal field
  of a scalar struct, a cell's slot named by one literal number. A container any other expression
  names is left to the roads as before. `NotConvertible` carries `MATLAB:UnableToConvert` and a
  numeric array's cell refusal `MATLAB:invalidConversion`.
- `RequireOneBracePlace`: `c{i, j, k} = v` counts the places its subscripts name (a place past an
  edge is the one it grows to) and refuses anything but one, as the two-subscript road does.
- `ResolveStructForWrite` takes the field it is resolving for (`into`, `last`): the nested level
  refuses with `FieldWriteRefusal` and `ScalarStructRequired`, a string with
  `StringFieldWriteRefusal`, and the default road (a cell's slot) with the same.
  `RefusesDots` counts a function handle and passes an empty cell or char (`IsEmptyDotHolder`), which
  the variable level and a composite path's cell slot (`ClassifyLevel`) turn into a struct.
- `MatlabCell`: `cell` with numeric sizes in the MATLAB dialect.
- `CharRange`: `RangeFromValues` hands a colon with a text operand to it in the MATLAB dialect; a `for`
  over one walks the built row, and a subscript reads its codes (`PositionalIndex`).
- `Reshape` refuses in R2025b's words in the MATLAB dialect.
- `BracePicks` names its position (`PackedOps.SubscriptPosition`) when a cell is read with several
  subscripts.
- `TableColumnPositions` takes a string array; `TableColumnByName` and `TableRowPositions` refuse in
  R2025b's words and match case in the MATLAB dialect.

## Consequences

- New parity fixtures `oi302_dbstack_frames` (53 rows) and `oi320_index_leftovers` (160 rows, two of
  them divergences), from R2025b 25.2.
- `MatlabBuiltinTests.CellAndStructBuilders` counted `cell(3)` as 3; it is 9.
  `MatlabShapeBuiltinTests.Reshape_RejectsAChangedElementCount` and
  `MatlabTableIndexingTests.VariablesAnswerToTheirNames` read R2025b's words now.
- A MATLAB script that grew a numeric `[]` into a struct array by index now stops where R2025b stops;
  one that relied on `cell(n)` being a row gets R2025b's square.
- K0's compiled `call.dbstack` row should read 3 when the K1 oracle is next run (its JGraph
  recording, `recordings-jgraph/env_plain`, is still K0's stale one).

## Divergences

- **`x(:)()`, `x(1)()`, `f(x)(2)`, `magic(3)(1)` and `x(1){1}` are read as the chain they spell**,
  where R2025b refuses the file (`MATLAB:mir_paren_index_must_be_last`, "Invalid array indexing.").
  The chain is a JGraph extra that misleads no one (it answers what it spells), the tests lean on it
  (`cov(D)(2, 2)`), and no MATLAB program can depend on the refusal, since R2025b never runs one.

## Still open

- **The error stack (`ME.stack`) has no anonymous frames**; R2025b's has one per handle
  (`@()error('p302:x','boom')@27`, measured in `p302`). Changing it reaches every error printed from
  inside a `cellfun` handle.
- **A script started by `run`** is not a frame here; R2025b's stack then holds `run.m`'s own frames
  (unmeasured).
- `dbstack`'s struct `file` for a package function or a class method is the file's name here
  (`info.m`); unmeasured in R2025b.
- Printed `dbstack` lines carry R2025b's hyperlinks under `-batch`'s `evalc`; JGraph prints none
  (the fixtures strip them).
