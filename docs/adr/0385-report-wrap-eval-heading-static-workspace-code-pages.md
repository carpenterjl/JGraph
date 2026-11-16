# ADR 0385 — getReport wraps at the console's width, eval heads an unanswered name, nested functions' static workspaces, constants of no input, fprintf's ans, fopen's code pages and 'a+', ?MException's ten properties

## Status

Accepted, 2026-11-16. Open item 350, ADR 0378's leftovers (chips wave 8, lane 8F).

## Context

Every expectation below was measured in R2025b 25.2 headless (`-noFigureWindows -batch`), with probes
`p1width` to `p8tables` in lane 8F's scratch (about 600 lines), one line per case in both engines and diffed.

- **The console width.** Under `-batch` R2025b reports a command window of 180 by 45
  (`get(0, 'CommandWindowSize')`, `matlab.desktop.commandwindow.size`), redirected or not, and `getReport`
  (every form, `'basic'` included, and inside `evalc`) wraps a long message to it; `ME.message` itself is not
  wrapped, nor are the "Error in" blocks' lines of code. A line holds at most 179 characters and breaks at the
  last space or tab that keeps it so (`'a' * 176 + ' bbb tail'` breaks before `bbb`; a 179-character head
  stays); the run of blanks at the break goes; one word longer than the line stays whole and breaks at the
  first blank after it; a continuation line begins with the leading spaces of the line it continues, so a
  cause's message, indented four under "Caused by:", wraps with the indent counted and repeated; a hyphen, a
  comma and a no-break space are no break; a carriage return starts the count again; a character counts one
  (an `é` too). The uncaught error R2025b prints in `-batch` wraps the same way, and so does the warning
  display (`[Warning: ...]`, the prefix counted).
- **eval's heading.** A name nothing answers in eval'd text — `eval('zzq')`, `eval('zzq + 1')`, `eval('zzq(3)')`,
  `x = eval('zzq')`, a second statement, nested eval, eval's catch text — is headed `Error using eval`
  (`evalc`, `evalin` theirs); the identifier, message and stack are unchanged. A name refused in a function
  the text called keeps that function's report (no header); `feval('zzq')` and `cellfun(@zzq, {1})` in the
  text are headed by those built-ins; an index past the end, a missing field and `zzq.b.c`
  (`MATLAB:undefinedVarOrClass`) have no header. JGraph headed none of them.
- **Static workspaces.** A function with nested functions, and each nested function, refuses
  `eval('pi = 7;')`, `eval('newv = 7;')`, `[q, newv] = deal(...)`, `evalc`, `evalin('caller', ...)` from a
  function it called, and `for newv = ...` in eval'd text: `MATLAB:err_static_workspace_violation`,
  "Attempt to add "newv" to a static workspace." (no header; `assignin('caller', ...)` is headed
  `Error using assignin`). A variable the frame holds, one the function's text binds anywhere (later
  included, a `global`, a cleared one), one an enclosing function's text binds, `ans`, and an indexed write
  of an existing variable are allowed; a local function of the same file that is not nested is not static.
  An anonymous function's workspace is static wherever it is made (`f = @() eval('anonv = 3;')`). JGraph
  bound them all.
- **Constants of no input.** `pi`, `i` and `j` take no input in R2025b: `pi(1)`, `pi(2)`, `pi('single')`,
  `pi([])`, `i(1)`, `j(2)` are `MATLAB:maxrhs` "Too many input arguments." headed `Error using pi`; `pi()`
  answers pi. JGraph indexed the value (`pi(1)` was pi, `pi(2)` an index error). Measured beside them:
  `eps('single')` is a single (JGraph answered a double), `eps('like', p)` the eps of `p`'s class,
  `eps(1, 2)` `MATLAB:eps:mustBeLike`, `realmax(1)`, `realmin('int8')`, `flintmax(2)`
  `MATLAB:<name>:invalidClassName`, `realmax('single')` a single (a double here). `Inf(n)` and `NaN(n)`
  already agreed; `e` is no function in R2025b and already refused.
- **fprintf's ans.** `fprintf(fid, ...)` to a file as a statement binds `ans` to the byte count and, without a
  semicolon, shows it (`fprintf(fid, '')` shows 0); to the console (id 1 or 2, a variable holding 1, or no id)
  it binds nothing. `sprintf` and `fwrite` were already right. JGraph never bound it.
- **fopen's code pages.** R2025b's fopen knows ICU's names (case, `-`, `_` and spaces do not count):
  ISO-8859-1 to -11, -13 to -15 (`ISO-8859-11` is named `iso-8859_11-2001`), windows-874 and -1250 to -1258,
  KOI8-R and -U, IBM866, Macintosh (`macintosh`), Shift_JIS (`sjis`, `cp932`, `ms932`, `windows-31j`,
  `windows-932`), GBK (`cp936`, `windows-936`), GB2312 (`EUC-CN`), GB18030, Big5 (`windows-950`),
  Big5-HKSCS, EUC-KR, `CP949` (named `ibm-949_P110-1999`), `windows-949` (named `KSC_5601`), EUC-JP, EUC-TW
  (`euc-tw-2014`), UTF-16LE/BE and UTF-32LE/BE. `UTF-16`, `UTF-32`, `cp950` (`ibm-950_P110-1999`), IBM437,
  IBM850, ISO-2022-JP and UTF-7 open with `MATLAB:iofun:UnsupportedEncoding` ("The encoding 'IBM437' is not
  supported." and "See the documentation for FOPEN." on a second line); `ISO-8859-16`, `system`, `native` and
  an unknown name are `MATLAB:iofun:InvalidEncoding` "The encoding 'x' is not valid.". A character a page does
  not hold is written as the page's substitution bytes: 0x1A for the ISO, windows-125x, KOI8 and US-ASCII
  pages, `?` for windows-874, Macintosh, Big5, GBK and windows-949, 0x7F for IBM866, IBM437 and IBM850,
  FC FC for Shift_JIS, A1 A1 for GB2312, AF FE for EUC-KR and CP949 (whose `€` is not mapped; 0x1A and 0x7F for
  one up to U+00FF), F4 FE for EUC-JP. JGraph knew six names and wrote `?`.
- **`'a+'`.** R2025b's `'a+'` reads anywhere and writes at the end (C's `"a+"`): `ftell` starts at the file's
  length, a write after `fseek(fid, 0, 'bof')` lands at the end. JGraph opened it write-only. `fopen(fid)`
  names a permission with neither `b` nor `t` binary (`'r'` is `'rb'`, `'a+'` `'ab+'`, `'w+'` `'wb+'`);
  a write-only file (`'w'`, `'a'`) reads `[]` and a count of 0.
- **`?MException`** lists ten properties: `type` (hidden, protected), `hasBeenCaught`, `identifier`,
  `message`, `cause`, `stack` (dependent), `defaultstack`, `enhancedstack`, `arguments` (hidden) and
  `Correction`, with their get and set access and defaults; `properties('MException')` lists the public five.
  JGraph listed five and `properties('MException')` none.

## Decision

### The console width (`ScriptContext.ConsoleWidth`, `JgsErrorReport.WrapToConsole`)

A host says how wide its console is with `ScriptContext.ConsoleWidth`; null (the default) is a host with no
width, and nothing is wrapped. The `-batch` runner (`BatchRunner`, `BatchConsoleWidth` = 180) reports R2025b's
`-batch` width, and so does the parity fixture runner, whose recordings are `-batch`'s; a deployed program's
console was not measured and reports none. `getReport` in the MATLAB dialect wraps the message (and each
cause's indented text) with `WrapToConsole`; the JGS dialect's report is unchanged.

### eval's heading (`JgsRuntimeException.NamesNothing`, `HeadsEvalText`)

The interpreter's own undefined-name errors (`UndefinedError`, `UndefinedForArguments`) carry `NamesNothing`.
`eval`, `evalc` and `evalin` run their text under an exception filter, `HeadsEvalText(verb)`, which names the
verb as the error's `UsingName` when the error was raised in the text itself (no frame unwound) and nothing
named one before; nested eval names the inner one. `UsingName` became settable for it. A built-in's own
refusal of a name (`feval`, `cellfun`) does not carry the flag, so it keeps ADR 0378's missing built-in header
rather than gaining a wrong `eval` one.

### Static workspaces (`Interpreter.StaticWorkspace.cs`)

`EvalTextBinds` collects the names eval'd text binds at its top level (the collection `WarnEvalBindsLiteralWord`
had, now shared). `RefuseStaticWorkspaceAdds` runs before the text in `EvaluateSource`: for each name,
`AddsToStaticWorkspace(env, name)` walks the frame out to its call boundary — a variable any scope holds, or a
function whose text `Binds` the name, allows it; a function that is nested (`FnStmt.NestedIn`) or holds a nested
`FnStmt` makes the frame static — and an anonymous function's workspace is static as before. `assignin` asks
the same question and heads its refusal `Error using assignin`. The whole text is refused before any of it
runs (R2025b runs the statements before the offending one).

### Constants of no input

`TryResolveCall` refuses `pi`, `i` and `j` called with arguments when the name resolves to the built-in layer's
constant (a variable of the name is indexed as ever), in the MATLAB dialect. `eps` takes `'like'` and answers
a single for `'single'`; `realmax`, `realmin` and `flintmax` refuse a non-class argument with R2025b's words and
answer a single for `'single'` (MATLAB dialect).

### fprintf's ans (`JgsBuiltins.PrintsToFile`)

The statement path of a call (`Interpreter.cs`, the one line that decides `BindAns`) also binds `ans` when the
callee is the built-in `fprintf` and its first argument is a numeric id other than 1 or 2. `fprintf` keeps
`BindsAnsAsStatement = false` and its wrappers are untouched.

### fopen's code pages (`JgsCodePageEncoding.cs`, `JgsCodePageTables.cs`)

`JgsMatlabEncoding` is an encoding with R2025b's name for `fopen(fid)` (`MatlabEncodingName` reads it) and
whether it is supported; `Named(name)` matches ICU's alias rule against the measured names. The single-byte
pages are `JgsSingleByteEncoding`, table-driven from R2025b's own decoding of every byte
(`native2unicode(uint8(0:255), name)`, generated into `JgsCodePageTables.cs`) with each page's substitution
byte, so they read and write exactly as R2025b's; in the MATLAB dialect ISO-8859-1, windows-1252 and US-ASCII
take this road too (their reading is unchanged, an unheld character is 0x1A where it was `?`). The multi-byte
pages are `JgsCodePageEncoding` over .NET's `CodePagesEncodingProvider` (asked directly, never registered
process-wide), writing R2025b's substitution bytes run by run. `EncodingNamed` falls back to `Named`; fopen in
the MATLAB dialect refuses an unknown name with `MATLAB:iofun:InvalidEncoding` and warns
`MATLAB:iofun:UnsupportedEncoding` for the seven.

### 'a+' and the permission

`'a+'` opens an `AppendingFileStream` (read and write, positioned at the end, every write sent to the end).
`fopen(fid)` inserts R2025b's `b` in the MATLAB dialect, and `fread` of a write-only file answers `[]` and 0.

### ?MException (`ExceptionMetaProperties`)

The metaclass of MException lists R2025b's ten properties with their hidden flag, get and set access, dependent
flag and default; `MetaPropertyModel` gained `Get`, `DependentFlag` and `HasDefaultFlag` for a property
described by name alone. `properties('MException')` lists the public five.

## Consequences

- New parity fixtures `oi350_report_eval_static` (93 rows, 1 divergence) and `oi350_files_meta` (131 rows, 11
  divergences), from R2025b 25.2, green in the packed and boxed lanes.
- Every MATLAB-dialect `getReport` run by `jgraph -batch` or the parity runner wraps a message longer than 179
  characters; a script comparing reports to unwrapped text sees line breaks. Hosts that report no width
  (the application's command window, the embedding API, the test sessions) are unchanged.
- eval'd text that binds a new name inside a function with nested functions, or inside an anonymous function,
  is now refused; such code ran here before.
- `pi(1)`, `i(2)` and the like are refused in MATLAB code; a variable named `pi` or `i` indexes as before.
- `fprintf(fid, ...)` to a file without a semicolon shows `ans = <count>` (in this build's one-line form).
- `fopen(fid)`'s permission reads `'rb'` for `'r'`; `MatlabM76FileFormTests.FopenAnswersWhatItOpened` expected `'r'`
  (written from memory) and now expects `'rb'`.
- `fileread(f, 'Encoding', name)` and `fopen` read and write the new code pages.
- ADR 0232's row `oi_categorical.cat_protected_display` (an `evalc` binding a name inside an anonymous function) is
  R2025b's static-workspace refusal and now agrees; ADR 0232 says so under "Closed since".

## Divergences

- **A function handle to a missing name called in eval'd text has no header.** `eval('h = @zzq; h(1)')` is
  headed `Error using eval` in R2025b; the handle's refusal is raised where `cellfun(@zzq, ...)` raises its own,
  which R2025b heads `cellfun`, so it carries no flag. Row `undef_handle`.
- **Code pages .NET does not hold as R2025b's ICU does.** Big5-HKSCS is read and written as Big5 (its `é` and
  `Ж` are R2025b's but not here); EUC-JP has no JIS X 0212 (`é`); EUC-TW is not available here (InvalidEncoding);
  CP949 reads a 0x7F byte as U+007F (R2025b U+001A); `cp950` maps `Ж` and `€` differently; EUC-KR's substitution
  bytes read as two private-use characters (R2025b one U+FFFD); `UTF-16` reads little-endian (R2025b writes
  little-endian and reads big-endian); `UTF-16BE` writes big-endian (R2025b writes little-endian under the native
  machine format, which is its bug); `UTF-32` writes no byte-order mark; `UTF-7` is refused (R2025b warns and
  opens). Rows `enc_Big5_HKSCS`, `enc_EUC_JP`, `enc_EUC_TW`, `enc_CP949`, `enc_cp950`, `enc_EUC_KR`,
  `enc_euc_kr`, `enc_UTF_16`, `enc_UTF_16BE`, `enc_UTF_32`, `enc_UTF_7`.

## Still open

- `?double` lists 279 methods in R2025b; ADR 0358's reason stands: the list holds every installed toolbox's
  overloads, so it is no one list to record, and this build lists what `methods` gives. `?MException`'s
  method list (24 entries with repeats) was not taken either.
- The uncaught error `jgraph -batch` prints is its own `(line,col): message` form, not R2025b's wrapped report;
  the warning display (`[Warning: ...]` under `-batch`) is not wrapped.
- `get(0, 'CommandWindowSize')` and `matlab.desktop.commandwindow.size` are not answered (the graphics root's
  property layer, lane 8E's); the application's command window reports no width.
- `fread` of nothing answers `zeros(0,1)` or `zeros(1,0)` where R2025b answers 0-by-0 (`fread(fid, 2)` at the end of
  a file).
- The refusal of eval'd text in a static workspace comes before any of the text runs.
- Item 194's multi-line display (`ans =` over a blank line) applies to fprintf's ans as to every other.
