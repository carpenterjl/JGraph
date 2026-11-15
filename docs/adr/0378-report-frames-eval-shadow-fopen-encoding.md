# ADR 0378 — getReport writes R2025b's "Error in" blocks, eval's shadowed names read the function, and fopen's readers detect the encoding

## Status

Accepted, 2026-11-15. Open items 336 and 318 (chips wave 7, lane 7G).

## Context

- **Item 336**, ADR 0368's "Still open" list. Probes `probe1`–`probe8` (scratch, about 450 rows) measured
  R2025b 25.2:
  - `getReport(ME)` (and `'extended'`) is the header, the message, a blank line, then one block per frame
    the error left: `Error in <name> (line N)`, the line as the file holds it (indentation, tabs and
    comments kept), and a line of carets under the expression the frame ran. The frame `error()` or a throw
    verb raised in is the header's and gets no block, unless the error was raised in eval'd text, where the
    frame is listed too with the carets under the `eval(...)` call. A runtime refusal (an index past the end,
    an undefined name, a missing field) has no header and lists every frame. A built-in's refusal is headed
    `Error using zeros` (`Error using *`). Causes follow the blocks as `Caused by:` and each cause's
    `'basic'` report indented four. An anonymous body made from text has no line: its block is the header over
    a blank line. `throwAsCaller` at the top has no block. JGraph wrote the header and message alone, and
    headed a runtime refusal with `Error using`.
  - The carets stand under the call or index chain the frame ran (`P7C.staterr()`, `localerr2(4)` inside
    `3 + localerr2(4) * 2`, `s.b.c(3)` for a missing field `b`, `x(5)`, `cellfun(...)` for an anonymous body
    it called, `f(1)` for a handle in a variable, `h.V` for a set method), under the name for an undefined
    one (also inside an argument list), and under the operator alone for `*`. The line is the line of the
    call, a continuation line included. Positions count characters, a tab one.
  - The report names a class method by its class's whole name: `P7C.staterr` for a static method,
    `P7C/insterr` for an instance method (a handle class's too), `pk.PC` for a constructor, `P7H/set.V` for
    a set method, `pk.PC>pcloc` for a class file's local function; a package function `pk.pferr`, its local
    `pk.pfloc>inner`; a nested function `oi336nest/inner` (its stack name, which JGraph wrote `inner`, and
    `set.V` for `P7H.set.V`). A refused call of a local function is headed `Error using probe2>localnargs`.
  - An anonymous body whose text holds `>%` is no frame at all, in `ME.stack` and `dbstack` alike:
    `@(x) error('id', 'x>%d', 1)`, `@(x) [x(3) '>%']`, `@() dbstack(numel('>%') * 0)` and an error reached
    through it (`@(x) localerrfmt('x>%d', 1)`). `'%>'`, `'x>a%d'`, `'x> %d'` and `['>' '%']` keep theirs.
    This is the rule ADR 0368 could not find for `cellfun(@(x) error('id', 'x>%d', x > 1), {2})`.
  - `eval('name = v')` in a function whose text mentions the name and never binds it, where a function
    answers the name, warns: `MATLAB:lang:ExternalShadowedInEval` for a built-in (`pi`, `sin`, `max`,
    `linspace`) or a file on the path, `MATLAB:lang:LocalFunctionShadowedInEval` ("... does not refer to the
    local function. ...") for a local function. The function's text then reads the function, bare or called
    (`pi`, `sin(1)`, `max([1 2])`, `true(1)`, `linspace` called with nothing); eval'd text, `exist` and `who`
    see the variable, and eval'd text reading it warns `MATLAB:lang:PoofedVariableShadowsExternal`. A name
    nothing answers is an ordinary variable. The warning is given once for each function and eval'd text in
    a session (a second `eval('pi = 7;')` in the same function is silent, in the error state too; `'pi = 8;'`
    or another function warns). R2025b decides what a name is when the file is loaded: a path file made after
    the function's file was read is not a function to it.
  - `which pi` is `built-in (<matlabroot>\toolbox\matlab\elmat\pi)`; JGraph printed nothing (a constant was
    no function to `which`).
  - `feature('hotlinks')` is logical false in -batch (true inside `evalc`, whose capture carries links); a
    second argument sets it and answers the old value.
  - `error('id', msg)` with no data reads `msg` as a format: `'x%%>'` is `x%>`, `'x %d'` is `x `, `'50%'`
    is `50`; so do `warning` and `MException`. JGraph kept the percent signs.
- **Item 318**, ADR 0352's leftovers:
  - A fresh `s = warning` is seven rows: `all` on, then six identifiers off
    (`MATLAB:RandStream:ReadingInactiveLegacyGeneratorState`, `MATLAB:JavaComponentThreading`,
    `MATLAB:JavaEDTAutoDelegation`, `MATLAB:RandStream:ActivatingLegacyGenerators`,
    `MATLAB:class:DynPropDuplicatesMethod`, `MATLAB:mir_warning_unrecognized_pragma`); the printed table
    lists them sorted; `verbose` is queryable (off) and never listed; `warning('on', 'all')` forgets them.
  - `fopen(name)` (and `'r'`, `'rt'`, `'r+'`, an empty encoding) detects the encoding as `fileread` does
    (ADR 0352's ICU rule), and `fopen(fid)` names it: `UTF-8` (plain ASCII included), `windows-1252`,
    `UTF-16LE` by its mark. `fgetl`, `fgets`, `fscanf`, `textscan` and `fread` of `'char'` (`'*char'`,
    `'char=>char'`, `'char*1'`, and `'char'` answering doubles) read decoded characters; a count is of
    characters and `ftell` stands after their bytes. `'uchar'`, `'schar'`, `'uint8=>char'` and `'*uint8'` read
    bytes; `'int8=>char'` saturates a negative byte to 0; `'*uchar'` answers uint8. A named `windows-1252`
    (`cp1252`) is the real code page, `US-ASCII` reads a high byte as U+FFFD, `fopen(fid)` names
    `ISO-8859-1`, `US-ASCII`, `UTF-16BE`, `UTF-16LE`. An unknown precision is `MATLAB:badprecision_mx`.
  - `fwrite(1, ...)` and `fwrite(2, ...)` write the bytes the precision makes, rounded and saturated, as one
    character a byte with zero bytes dropped, and answer the element count; a statement shows no `ans`.
    `evalc` captures both ids (and `fprintf(2, ...)`). Id 0 and a skip are
    `MATLAB:notimplemented_mx`, a cell `MATLAB:fwrite:UnsupportedClass`.
  - `?MException`, `?double` and the other built-in classes are `matlab.metadata.Class`; `Correction`'s
    `SetAccess` is private, `message`'s protected; `ME.Correction = x` (any of its properties) is
    `MATLAB:class:SetProhibited`. `meta.class.fromName('double')` answers too.
  - `checkcode(files, 'extra')` warns `MATLAB:mlint:UnusedArgs` and the error state raises it.

## Decision

### Anchors of the frames an error leaves (`JgsException.cs`, `JgsErrorReport.cs`, `Interpreter.cs`)

`JgsRuntimeException` keeps a `ReportAnchor` per frame index: where the expression the frame was running
starts. The frame an exception filter runs in is the one at `Frames.Count`, since every frame the error
left was pushed by a catch; the innermost expression's filter runs first and keeps it. Three filters
anchor, none catches: `EvaluateCallAnchored` around `Evaluate`'s call case, `EvaluateForOutputs` (now over
`EvaluateForOutputsCore`), and the statement loop's existing `PlaceAtStatement`, which anchors a call made
as a statement (`AnchorStatement`). In the frame the error was raised in, an error placed inside the call's
argument list is anchored at its own place (a name, an operator). `eval`'s text forgets its anchor and the
`eval` call anchors (`LeavesEvalText`; the error's place then counts in the text, `PlacedOutsideFrame`).

`StackOf(error, out ReportInfo)` hands the catch, beside the stack, each frame's report name and anchor;
`JgsBuiltins.NoteReport` keeps it with the caught MException by its payload, and `getReport` uses an entry
only while the stack still names the same frame at the same line. A frame still running at the catch has
no anchor; `JgsErrorReport.Underline` finds the call of the frame inside it on the line by name (or the
call holding the line's first handle, for an anonymous frame), else an `eval` call.

`JgsErrorReport.ErrorInBlocks` reads the file's line (`JgsSourceText.Read`, cached by write time) and
underlines the chain (`ChainAt`: a name, then `.name`, `.(expr)`, `(...)`, `{...}`) or the token
(`TokenAt`). `getReport` (`JgsBuiltins.Report`) heads, lists the blocks from the frame after the header's
(or from it, `ShowsRaisingFrame`) and appends the causes; `'basic'` and the JGS dialect keep the old
forms. `'hyperlinks'` is taken and ignored (ADR 0368).

### Names

`ExecuteFunctionBody` names a method's frame for the report (`ShowLastFrameAs`: `C.stat`, `C/inst`, `C`),
a property's set or get method is a method frame (`IsAccessorName`), and a nested function's frame is
`outer/inner` (`FnStmt.NestedIn`, set when the outer call hoists it; `FnStmt.StackName`; `FrameNameOf`).
`FrameShownName` puts the package (`PackagePrefix`) before a file's names. `UserFunction.ReportName` heads a
refused call of a local function (`file>name`).

### The anonymous frames R2025b leaves out (`Interpreter.Frames.cs`)

`HiddenFrame(name)`: an anonymous body whose text holds `>%`. `UnwindOtherFrame` skips it (`SkipFrame`:
the caller's line, the body's anchor forgotten) and `StackOf` and `CurrentStack` pass over it.

### eval's shadowed names (`Interpreter.LiteralNames.cs`, `JgsNameResolver.cs`, `NameMentions.cs`)

`WarnEvalBindsLiteralWord` takes every name the text binds: a name the function mentions and does not
bind (`NameMentions.Binds`, cached in `FnStmt.BoundNames`) that a local function, a built-in or a path file
(`JgsNameResolver.OnPath`) answers goes into the frame's `JgsEnvironment.EvalShadowed`, with the warning the
first time the function evals that text (`_evalTextsWarned`). The resolver's `Invoke` passes over a variable
the frame holds in `EvalShadowed` unless eval'd text is reading it, so the function's text reaches the
function, bare or called; `true` and `false` join the set, so `true(1)` calls the built-in.

### The rest

- `which` names a built-in constant as it names a built-in (`pi is a built-in function.`).
- `feature('hotlinks')` is false, a second argument accepted.
- `FormatMessage` reads an identified message with no data as a format, a lone trailing `%` dropped.
- `JgsWarningState.FactoryOff` holds R2025b's six; the struct table and the printed one leave out `verbose`
  and `backtrace`, and the printed one is sorted.
- `FileEntry.DetectsEncoding`: MATLAB code opening to read with no encoding named detects one the first time
  a reader or `fopen(fid)` asks (`JgsSourceText.DetectedEncoding`; the whole file is read once). `JgsWindows1252Encoding`
  is the code page (`EncodingNamed` gives it for `windows-1252` and `cp1252`, and US-ASCII a U+FFFD fallback);
  `MatlabEncodingName` is `fopen(fid)`'s spelling. `ReadLineFrom` reads a UTF-16 file by two-byte units.
  `fread` of `'char'` in MATLAB code reads characters (`ReadCharacters`); `CanonicalPrecision` maps
  `uchar`, `schar`, `char*1` and the C names; `EncodeValue` rounds and saturates and writes int64 and uint64
  as integers; `CharOfCode` saturates.
- `fwrite` refuses a cell or struct, writes ids 1 and 2 to the console (`WriteToConsole`; id 2 through
  `JGraphScriptGlobals.WriteErrFromCode`, which `evalc` captures, as `fprintf(2, ...)` now does), and is
  registered with its output count so a statement writing to the console answers nothing.
- `?Name` and `meta.class.fromName` of a built-in class make the metaclass of one of its values
  (`SampleOfBuiltinClass`, `MetaClassOfPlainValue`); an MException's metaproperties carry R2025b's set
  access; a write to an MException's property is SetProhibited.
- `checkcode`'s ignored arguments go through `Warn`.

## Consequences

- New parity fixtures `oi336_report_frames` (68 rows, 1 divergence), `oi336_eval_shadow` (27 rows, 2) and
  `oi318_files_console_meta` (189 rows), from R2025b 25.2. New test class `ReportFramesAndFileEncodingTests`.
- ADR 0368's divergence "A call of the word reads an eval'd variable" is closed: `true(1)` after
  `eval('true = 8;')` calls the built-in; `oi329_true_names2.call_form_read` is an agreeing row.
- Every MATLAB-dialect `getReport(ME)` now prints "Error in" blocks; a script comparing reports to the old
  two-line text sees more. A runtime refusal's default report no longer starts with `Error using`.
- A nested function's stack name is `outer/inner` (ME.stack, dbstack), a set method's `C.set.V`.
- A file opened by MATLAB code without an encoding is read in the detected one: a Windows-1252 file's `é`
  is one character where it was U+FFFD.
- An exception filter on the call path costs nothing until an error passes; the call case now goes through
  one more small method (`EvaluateCallAnchored`), not timed.

## Divergences

- **A built-in's refusal has no `Error using <built-in>` header.** R2025b heads `zeros('a')`'s report
  `Error using zeros` and `*`'s `Error using *`; this build knows the refusing built-in only for the few
  refusals that carry it (`warning`, output and input counts), and heads the rest with nothing. Row
  `builtin_header`.
- **`which` of a built-in names no folder.** R2025b's `built-in (<matlabroot>\toolbox\matlab\elmat\pi)` is
  written here `pi is a built-in function.`, as every built-in already was. Row `which_pi_text`.
- **`feature('hotlinks')` is false inside `evalc` too**, ADR 0368's divergence (evalc carries no links).
  Row `hotlinks_in_evalc`.

## Still open

- MATLAB-dialect echoes and `disp` of a value are this build's one-line forms (`ans = true`, `true`) where
  R2025b prints its multi-line display (`ans =`, a blank line, `  logical`, a blank line, `   1`) for every
  class; item 336's `evalc('true')` is that whole display system, far bigger than an item, and was left.
- R2025b wraps a long message in a report at its console width; not done.
- `eval` of text with an undefined name is headed `Error using eval` in R2025b; not done.
- A function with nested functions refuses `eval('pi = 7;')` (`MATLAB:err_static_workspace_violation`,
  "Attempt to add "pi" to a static workspace."); this build binds it.
- `pi(1)` is `MATLAB:maxrhs` in R2025b; this build answers pi.
- `fprintf(fid, ...)` to a file as a statement shows `ans = <count>` in R2025b (to the console it does not);
  this build never shows it.
- `fopen` does not know Shift_JIS and the other code pages R2025b names; `'a+'` still opens write-only.
- R2025b's `?MException` lists ten properties (five hidden or private) and `?double` 279 methods; this build
  lists the visible ones (ADR 0358).
- Not run (windows, compiled programs): the compiler-probe oracle, App Designer, AppBase's `saveobj`
  warning and ui HTML warnings with the error state.
