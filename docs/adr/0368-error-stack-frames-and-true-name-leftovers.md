# ADR 0368 — The error stack holds anonymous bodies, scripts and run, and the leftovers of `true` and `false` as names are R2025b's

## Status

Accepted, 2026-11-14. Open items 329 and 334 (chips wave 6, lane 6E).

## Context

- **Item 334**, ADR 0366's "Still open" list, and one case the main session added. Probes `p334` and
  `p334b` (scratch, about 50 rows) measured R2025b 25.2:
  - `ME.stack` holds a frame for every anonymous body the error left, as `dbstack` lists them (ADR
    0366): named by its text, at the line running in it, line 0 for a handle made by `eval` or
    `str2func`. `cellfun(@(x) error('p334:a', 'boom'), {1})` written in a script is two frames,
    `@(x)error('p334:a','boom')@3` and the script; `arrayfun(@(x) failing(x), 1)` is three, `failing`,
    the handle and the script; a catch inside a function the handle called sees the handle's frame
    still running below it. `getReport` heads such an error `Error using p334>@(x)error(...) (line 3)`,
    and with no line for a handle made from text. JGraph's stack listed the functions alone.
  - A script named in a statement is a frame of `ME.stack` (`p334s@12 ; p334@79`); JGraph listed
    none, and named the main script at the script's line instead. The main session found the same in
    a JGS session: `jgraph -batch "q2" -sd dir`, with `q2.m` catching its own error, printed `0 frames`
    where R2025b prints `1 frames` (`q2`, line 3). The script's frame was `dbstack`'s alone (ADR
    0366), and only the -batch file had a frame in the error stack (ADR 0214), so a script a JGS
    statement runs had none; the stress script `stess_41` section 9 asserts that frame.
  - `run('s.m')` holds R2025b's own `run.m` as a frame between the script and its caller, at line
    112 (`evalin('caller', strcat(scriptStem, ';'))`), in `dbstack` and `ME.stack` alike:
    `p334r@1 ; run@112(run.m) ; p334@84`. A function file run so is its main function's frame over
    `run`'s, with no frame for the file (`p334fn@2 ; run@112 ; p334b@3`); runs nest
    (`ri ; run ; rr ; run ; main`). JGraph listed the -batch file at the run script's line.
  - `dbstack`'s `file` for a package function is `pf.m`, for a class method `C.m`, the stack names
    `pf` and `C.stat`; JGraph already answered all four.
  - Printed `dbstack` lines carry hyperlinks under `evalc` in -batch (`<a href="matlab:
    matlab.lang.internal.introspective.errorDocCallback(...)">`), and none when printed straight to
    the -batch output, where `feature('hotlinks')` is 0. JGraph prints none in either.
- **Item 329**, ADR 0363's "Still open" list. Probes `p329` and `p329b` (about 140 rows):
  - `x = eval('true')` and `evalin(ws, 'true')` asked for a value answer the built-in with a `true.m`
    on the path when the text is the bare word (`'true'`, `' true '`, `'true;'`, `"true"`); `'true,'`,
    `'(true)'`, `'~true'`, `'true()'` and `'[true]'` answer the file, so do `eval('true')` as a
    statement, `evalc('true')` and `eval('y = true;')`; a variable or a local function of the name
    still answers the bare text, and a `pi.m` answers `eval('pi')`. JGraph answered the file.
  - `eval('true = 7;')` (or `evalc`'s) in a function whose text mentions `true` and never binds it
    warns `MATLAB:lang:ExternalShadowedInEval`, *Identifier 'true' in the evaluated statement does not
    refer to the external function. In a future release, using an identifier different to how it is
    used in the file will not be supported.*, every time, and the function still reads the built-in.
    No warning in a script, in a function that binds the word, in one that never names it (where
    `eval('false')` then reads the 8 the eval bound), through `assignin`, or for text a called
    function runs here with `evalin('caller', ...)`. R2025b gives it for `pi` and `sin` too.
  - `which true` is `true is a variable.` printed and `'variable'` as a value when a variable holds the
    name, in a script and in a function that binds it, and for any other variable (`zz329`, `max`
    bound over the built-in, a variable an `eval` made in a function). JGraph named the built-in.
  - `iskeyword` is `lang/iskeyword.m`: no argument is the twenty keywords as a 20-by-1 cell, one is
    `any(strcmp(s, list))` with any refusal caught as false; `MATLAB:TooManyInputs` for two arguments,
    `MATLAB:TooManyOutputs` for two outputs. JGraph had no `iskeyword`.
  - A nested function named `true` answers the mentions in the function it is nested in and its other
    nested functions, and not its sibling local functions' or the main function's; JGraph's
    file-wide rule (ADR 0363) reached them all.
  - `true(2, 'like', sparse(true))` is a 2-by-2 sparse logical, `true(2, 'like', sparse(1))`
    `MATLAB:True:invalidInputClass`.

## Decision

### An anonymous body, a script and run are frames of the error stack (`Interpreter.Frames.cs`)

`UnwindOtherFrame` records the innermost frame ADR 0366's `_otherFrames` holds on the error as it
leaves it, named and placed as `OtherFrame` names it for `dbstack`: `AnonymousFunction.CallMultiple`
catches in the MATLAB dialect, and `RunScriptFile` for a framed script. `JgsRuntimeException.PushFrame`
takes the frame's own line (`PushFrame(name, file, callLine, line)`), so a handle made from text is
at line 0. `StackOf` interleaves the frames still running at the catch with the functions, as
`CurrentStack` does. A script a JGS statement names is framed already (its dialect is MATLAB once
`RunScriptFile` enters it), so its error has its frame.

`run` of a MATLAB file enters `EnterRunFrames`: a frame for `run.m` at its line 112 (an `_otherFrames`
entry with a fixed line, the tuple's new `Line`), and, for a script, the script's frame over it;
`UnwindRunFrames` records both on an error and `LeaveRunFrames` ends them. `run.m`'s path is
`<installation root>\toolbox\matlab\lang\run.m` (`RunFramePath`), where R2025b keeps it; no file is
there in this installation.

`getReport` heads an error whose top frame is a handle made from text with no line, and
`FrameShownName` prefixes an anonymous frame's name with its file even when its text holds a `>`.
`JgsDeployedMain.Report` writes the same "Error in" line for one. `DesignRun.RaisedByStartup` passes
over anonymous frames: it reads the functions alone, as before.

### eval's bare word, the warning, which, the nested function (`Interpreter.LiteralNames.cs`, `Parser.LiteralNames.cs`)

- `EvaluateSource` and `EvaluateSourceIn` take `bareWordIsBuiltin`, which `eval` asked for a value
  (both its forms) and `evalin` pass; `BareWordSkipsFile` clears the literal's `File` shadow when the
  whole text is the bare word (`TextEndings` aside). `ShadowableLiteral` now honours that flag.
- `WarnEvalBindsLiteralWord`, before eval'd text runs: in the running function's own frame, outside a
  script, an assignment, a multiple assignment or a `for` of the text binding a word the function's
  text mentions (`MentionsLiteralWord`: bare, from the parser's new `FnStmt.LiteralMentions`, or as a
  name, `NameMentions`) and does not bind warns with R2025b's identifier and words.
  `VariableAnswersLiteral` now refuses the variable only in a function whose text mentions the word.
- `which` in the MATLAB dialect answers `'variable'` (`WhichVariable`) first when
  `Interpreter.IsWorkspaceVariable` says the name is bound here (for `true` and `false` in a function,
  by the rule the literal reads), and the statement form prints `name is a variable.`.
- `iskeyword` (`JgsBuiltins.Keywords.cs`) over the existing `MatlabKeywordList`; catalog entry,
  `nargout-r2025b.tsv` row (1, file) and dispatch sweep `6e` (`file` under all 37 patterns).
- The parser's `LiteralScope` keeps the literals spelled in a body (`Spell`), the words it mentions
  (`Mentions`) and the words a function nested in it is named after (`NestedFunctions`); a nested
  function named after a word marks the body's literals when the body ends, and only a file-level
  function goes into `_literalFunctions`.

## Consequences

- New parity fixtures `oi329_true_names2` (92 rows) and `oi334_stack_frames` (33 rows), from R2025b
  25.2, one divergence each; new tests in `StackFramesAndTrueNamesTests`, the main session's JGS case
  among them.
- An error raised inside a `cellfun`, `arrayfun` or callback handle now has the handle's frame first in
  `ME.stack`, as R2025b's has; `getReport` names it. No fixture or recording of R2025b's printed an
  anonymous frame JGraph lacked except these two fixtures.
- `which x` for a variable answers `variable`; `x = eval('true')` is logical beside a `true.m`.

## Divergences

- **`evalc` carries no hyperlinks.** R2025b's `evalc` of `dbstack` (and of a displayed class name)
  holds `<a href="matlab:...">` links even in -batch, where the printed output has none; JGraph's
  `evalc` holds what it prints. A link to the MATLAB desktop's help and editor means nothing here, and
  the fixtures strip them.

Closed since: a call of the word no longer reads an eval'd variable. After `eval('true = 8;')` in a
function that names `true` and never binds it, `true(1)` calls the built-in, as R2025b's does, and so
for every other name a function reads as a function (`sin(1)` after `eval('sin = [5 6];')`); the frame
remembers the names such an eval bound and the resolver passes over them for the function's own text
(ADR 0378, open item 336). Fixture row `oi329_true_names2.call_form_read` agrees.

## Still open

- `true(2, 'like', sparse(true))` is dense: this build has no sparse logical (`sparse(true)` is a
  sparse double, `sparse(true(2)) & sparse(true(2))` is refused), so the prototype cannot be told from
  `sparse(1)`, which R2025b refuses. ADR 0213's divergence stands.
- The `MATLAB:lang:ExternalShadowedInEval` warning for any other name (`pi`, `sin`), with the read of
  the built-in that goes with it.
- `getReport`'s header for a class method and a package function: R2025b writes `P334C.staterr` for a
  static method, `P334D/insterr` for an instance method and `p334pk.pferr` for a package function;
  JGraph writes `P334C>P334C.staterr`, `P334D>P334D.insterr` and `pferr`.
- `getReport` without `'basic'` writes R2025b's header alone, not its "Error in" lines, and heads a
  runtime refusal (`Index exceeds ...`) with "Error using", where R2025b writes the message alone.
- `cellfun(@(x) error('id', 'x>%d', x > 1), {2})` has no anonymous frame in R2025b (measured, one
  frame); every other handle measured had one. The rule is unknown and the row was left out.
- ADR 0363's two divergences stand (a read before the binding or after `clear`; a function resolved
  before a `true.m` appeared).
