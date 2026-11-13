# ADR 0346 — warning('error', id) raises the warning as an error, %s takes a run of codes, and an empty argument fills a conversion

## Status

Accepted, 2026-11-12. Open items 189, 184 and 295. Items 184 and 295 are the same formatter
(`JgsSprintf.FormatMatlab`) and are taken together. Item 189 is the warning state table.

## Context

**Item 189.** `warning('error', id)` printed a warning whose text was `error`. In R2025b it puts the
identifier in a third state beside on and off (`k7_more`'s `matlab_error`, ADR 0263). Probes
`probe_w` to `probe_w5` measured it in R2025b 25.2:
- `warning('id', fmt, ...)` under an identifier in the error state raises an `MException` with that
  identifier and the formatted message. try/catch catches it. `ME.stack(1)` is the frame that called
  `warning` (`warning` itself is not on the stack). `getReport(ME, 'basic')` is
  `Error using warning` over the message.
- lastwarn is left as it was. The warning is never recorded.
- `warning('query', id)` answers `state 'error'`, and its statement form says
  `The state of warning 'id' is 'error'.`. The state table (`s = warning`) lists the row with `error`.
- `warning(s)` with a struct whose state is `'error'` puts the error state back.
- `warning('error', id)` answers the previous state as a struct and prints nothing as a statement.
  The verb's case does not matter (`'ERROR'`).
- `warning('error', 'all')` is refused (`MATLAB:warning:cannotUseAllWithError`). `'backtrace'` and
  `'verbose'` are refused as `MATLAB:warning:unknownSettingOrId`, a number as `MATLAB:badopt`, and a
  third argument as `MATLAB:maxrhs`.
- `warning('on', 'all')` and `warning('off', 'all')` forget an identifier's error state. An identifier
  set to error after `warning('off', 'all')` still raises.
- `warning('error')` alone is still a warning whose message is `error`. A warning without an
  identifier is never an error.
- `backtrace` off and `verbose` on change nothing about the error.
- The engine's own warnings obey it: rmpath's `MATLAB:rmpath:DirNotFound`, the colon's
  `MATLAB:colon:nonIntegerIndex` and inv's `MATLAB:singularMatrix` all become errors.

**Item 184.** `fprintf('arg%d\t%s\n', 2, [2 3])` printed `arg2`, a tab, character 2, then `arg3` and a
tab. R2025b prints both characters under the one `%s`. Probes `probe_pf` and `probe_pf2` (about 150
cases, sprintf and fprintf alike) give this rule:
- `%s` meeting a number takes the run of character codes left in that one argument, from where the
  previous conversion stopped. `'%s|'` of `[72 105]` is `Hi|`. `'%d %s|'` of `[1 65 66]` is `1 AB|`.
  `'%d %s|'` of `1, [65 66], 4` is `1 AB|4 `. `'%s,%s;'` of `65, 66, 67` is `A,B;C,`, one argument each.
- A code is a whole number from 0 to 65535. That covers logicals, integer classes, singles, a complex
  number's real part, -0 and a lone surrogate (55296). 65536 is not a code. A value that is not a code
  ends the run and is written by itself as `%e`, with the conversion's flags, width and precision.
  `'%s|'` of `[65 66 67.5 68 69]` is `AB|6.750000e+01|DE|`.
- A width pads the whole run and a precision clips it. `'%.1s|'` of `[65 66 67]` is `A|`, which takes
  all three codes.
- `%c` takes one character. Text met by `%c` is spread into its characters as a numeric conversion
  spreads it into codes: `'%c|'` of `'abc'` is `a|b|c|`. After a `%c` or `%d` has taken part of a
  text, a `%s` takes the rest of that text. `'%d %s|'` of `'abc'` is `97 bc|`.
- `%c` of a number that is not a code is `%e` too: NaN, -1, 65.5, 70000, and `'%+c|'` of 2.5 is
  `+2.500000e+00|`.
- A cell or a struct among the values is refused before anything is written, whether or not a
  conversion would reach it: `MATLAB:printf:invalidInputType`, "Function is not defined for 'cell'
  inputs."

**Item 295.** `sprintf('%.17g ', [])` is `' '` in R2025b and was empty here. An empty argument is one
value that writes nothing:
- `[]`, `zeros(0, 3)`, `''`, `strings(0)` and `false(1, 0)` each take one conversion. That conversion
  writes an empty field padded to its width. The zero flag pads with zeros. Sign flags and `#` add
  nothing.
- So `'%d,%d;'` of `[]` is `,`, since the second conversion has no value and the pass stops there.
  `'%d %d\n'` of `[], 5` is ` 5\n`, and `'<%d>'` of `[], 5, []` is `<><5><>`.
- A `*` that meets an empty argument takes it as no width. A `*` with no value left stops the pass, as
  a conversion does (`sprintf('%*d|')` is empty, no longer an error). A positional conversion meeting
  an empty argument stops the pass (`sprintf('%1$d|', [])` is empty).
- With no arguments at all, the pass still stops at the first conversion (`sprintf('a%db%dc')` is `a`),
  as before.

## Decision

### The warning's third state

`JgsWarningState` keeps each explicit setting as the word "on", "off" or "error". `SetState` takes
any of the three, and `'all'` takes on or off only and resets the table, as before. `StateOf`
answers the word. `IsError` is true only for a non-empty identifier in the error state. `IsOn` stays
true for the error state, so a caller that cannot raise (a destructor's report, a callback's report, a
timer) still shows the warning.

`warning` (in `JgsBuiltins.Matlab`) reads `'error'` as a verb when a second argument follows. It refuses
as listed above. It answers the replaced state as a struct, which may now say `error`. A query's
struct, the statement form and the table all carry the word. `warning(s)` puts `error` back. A message
whose identifier is in the error state throws `JgsRuntimeException(line, col, id, message)` before it
is recorded, so lastwarn and the debugger's warning hook do not see it.

`JgsBuiltins.Warn`, the shape most of the engine's own identified warnings take (75 call sites,
rmpath and the colon among them), throws the same error when its identifier is in the error state. It
has no location, so the error's own line is 0.

### The formatter's runs and empties

`FormatMatlab` now keeps, beside its value stream, the argument each value came from (`groups`).
Numeric array elements share their argument's group. Each string of a string array is its own group.
An empty argument is one value in `EmptyGroup`. `Emit`, on MATLAB's road only:
- writes `PadText("", spec, zero ? '0' : ' ')` for an empty value;
- spreads text met by a numeric conversion into codes, and text longer than one character met by `%c`;
  the codes keep the text's group;
- for `%s` over a code, takes every following code in the same group, then pads and clips once.

`IsCharacterCode` is now 0 to 65535, MATLAB's char. `Render` writes a non-code under `%c` as `%e`, as
it already did under `%s`. `TakeStarValue` answers null on MATLAB's road when no value is left, and 0
for an empty argument. The JGS dialect's `Format` passes no groups and is unchanged.

The MATLAB dialect's `sprintf` and `fprintf` refuse a cell or struct among the values
(`RefusePrintfContainers` in `JgsBuiltins`). Datetimes, categoricals and objects are left to their
own roads.

## Consequences

- New parity fixture `oi_warning_error_state` (73 rows from R2025b 25.2). It covers the three states,
  the query and table, the struct round trip, the 'all' interplay, the refusals, backtrace and verbose,
  rmpath's and the colon's warnings, and a caught error's fields. One row is a divergence.
- New parity fixture `oi_printf_text_and_empties` (118 rows): %s runs, %c, codes at the 65535 edge,
  text after %c and %d, the cell and struct refusals, and empty arguments across formats, flags, `*`
  and positions.
- New test class `PrintfRunsAndWarningErrorTests`.
- Any script that printed a numeric vector under one `%s` expecting the format to repeat per element
  now gets R2025b's single run. Any script that relied on `[]` writing nothing now gets the
  conversion's literal text around an empty field.

## Divergences

- **`getReport(ME, 'basic')` of a warning raised as an error is the message alone here**; R2025b heads
  it with `Error using warning`. Fixture row `caught_report`.

## Still open

- **An engine warning raised as an error has line 0** (`JgsBuiltins.Warn` has no location). ME.stack's
  top frame is right, with line 0. R2025b's top frame for rmpath is rmpath.m itself.
- **Warnings that do not go through `JgsBuiltins.Warn` or `warning` ignore the error state.** That is
  the ones written with `host.Warnings.Record` directly (destructor, listener and timer reports,
  AppBase, ui HTML), and plain "Warning:" prints with no identifier. They still show as warnings.
  inv's singular matrix is an error here already, with no identifier, so `MATLAB:singularMatrix`
  cannot be switched.
- **String scalars under printf**: R2025b refuses a string under a numeric conversion
  (`MATLAB:invalidConversion`, "Conversion to int64 from string is not possible.") and a string of
  other than one character under `%c` (`MATLAB:printf:UnableToConvertToOneElement`). Here a string
  spreads into its codes, or its characters, as a char vector does. Probe `probe_w4`.
- **evalc and the console turn NUL into a blank in R2025b** (`evalc('fprintf(''a%sb'', char(0))')` is
  `a b`, and the same for `disp`). A file written by fprintf keeps the NUL in both. evalc of
  `char(65535)` is empty in R2025b. These are evalc's capture, not the formatter. The fixture
  compares sprintf's codes instead.

## Not measured

Nothing here needs a window. The error state inside a callback, timer or destructor was not probed.
