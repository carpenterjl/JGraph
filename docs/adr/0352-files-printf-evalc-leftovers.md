# ADR 0352 — dir and delete in the current folder, fileread's detected encoding, a string under printf, the warning leftovers, evalc's NUL, and MException's Correction

## Status

Accepted, 2026-11-13. Open items 300, 303, 306 and 308, the leftovers of ADRs 0338, 0340 and 0346.
They share the console, the formatter and the warning machinery, and are taken together.

## Context

**Item 300.** `dir(fullfile('rel', '**'))` with a relative folder listed nothing.
- The cause was wider than `**`. `dir` and `ls` anchored every relative pattern to the run's starting
  folder (`host.WorkingDirectory`), not to the folder `cd` had moved to. So after a `cd` every
  relative `dir` listed the wrong folder, and usually nothing.
- `delete` took no patterns: `delete('*.tmp')` asked .NET to delete a file named `*.tmp` and ended the
  run with an IOException no `try` could catch.

Probes `probe300` and `probe2` measured R2025b's `delete`:
- A name holding `*` or `?` anywhere is a pattern over its last part. It deletes every file it
  matches in the current folder, or in the folder it names, read-only files included, and matches
  case-insensitively.
- A folder a pattern matches warns `MATLAB:DELETE:DirectoryDeletion`, "'<full path>' is a directory.
  Use rmdir to delete directories." (two spaces), and the files beside it are still deleted.
- A pattern that matches nothing is silent. So is one with a wildcard in a folder part
  (`delete('s*/b.txt')` deletes nothing).
- A plain name that is a folder warns `DirectoryDeletion` with the name as given (`'sub'`, `'.'`).
- A plain name that is not there warns `MATLAB:DELETE:FileNotFound`, "File '<name>' not found.", with
  the name as given. `delete('')` is "File '' not found.", and `'[ab].tmp'` is not a pattern.
- A cell or a string array is `MATLAB:string`, "Name must be a text scalar.". `delete()` is
  `MATLAB:minrhs`.
- With several names, each is taken in turn: a missing one warns and the next is still deleted.

**Item 303.** `fileread` decoded every file as UTF-8, a byte that was not UTF-8 becoming U+FFFD.
R2025b's `fileread.m` is `fopen(filename, "r", "n", "")` and `fread(fid, "*char")'`. An empty encoding
makes `fopen` detect one. Probes `probe303` and `probe2` read 49 files. Every answer fits ICU's UTF-8
recogniser (`CharsetRecog_UTF8::match`) run over the whole file, with UTF-8 chosen at a confidence of
80 or more and Windows-1252 otherwise:
- The recogniser counts complete multi-byte sequences by their lead and trail bits alone (an overlong
  `C0 AF` and a surrogate `ED A0 80` count as valid). It counts broken ones: a byte that cannot lead,
  or a lead whose trail stops early. The byte that broke a sequence is skipped, not read as a lead.
- A sequence cut short by the end of the file is neither. So `C3 A9 E9` is UTF-8 (U+00E9 U+FFFD), and
  `C3 A9 E9 41` is Windows-1252.
- A file starting with the UTF-8 byte-order mark counts the mark as one valid sequence.
- UTF-8 when nothing is broken and something is valid, or the mark is there with nothing broken, or
  the mark is there and the valid outnumber the broken more than ten to one (the mark, ten `C3 A9` and
  a lone `80` are UTF-8; the mark, nine and a lone `80` are not).
- Eleven `C3 A9` and one lone `80` without a mark (ICU's confidence 25) are Windows-1252, and so is
  plain ASCII (15).
- Shift-JIS, GBK, KOI8-R and Greek bytes all come back as Windows-1252: no other charset is chosen.
- A valid sequence 9000 bytes in counts, so the whole file is read, not ICU's first 8000 bytes.
- Read as UTF-8, a broken sequence is U+FFFD as .NET decodes it, the byte-order mark is kept as
  U+FEFF, and a character past the basic plane becomes U+001A. A UTF-16 byte-order mark reads the file
  in that order, the mark kept.
- Windows-1252 maps its five undefined bytes to the C1 controls.

With an encoding named, `'windows-1252'` is the real code page (this build read it as ISO-8859-1), and
`'US-ASCII'` reads a byte past 127 as U+FFFD (this build read `?`).

ADR 0338 found the parser and App Designer's reader taking Windows-1252 for a file that is not all
UTF-8, and gave `JgsSourceText.Decode` that rule. `fileread`'s rule is not that one: it differs on a
sequence cut short at the end, on a mark with broken bytes, and in keeping the mark.

**Item 306.** Five leftovers of ADR 0346, measured in probes `probe306` and `probe2`:
- A string scalar under printf: R2025b refuses one under `%d`/`%i` ("Conversion to int64 from string
  is not possible.") and under `%u`/`%o`/`%x`/`%X` (uint64), both `MATLAB:invalidConversion`.
  - Under `%c`, one of other than one character is `MATLAB:printf:UnableToConvertToOneElement`, and a
    missing one is "Conversion to char from string is not possible.".
  - Under `%s`, a missing one is `MATLAB:string:MissingNotSupported`.
  - Under `%f`, `%e` and `%g` it is the number it spells: `"ab"` is NaN, `" 2 "` is 2, `"0x10"` NaN,
    `"1+2i"` 1, `""` NaN.
  - A `*` reads it as a number too, and one spelling none ends the pass (`sprintf('%*d;', "x", 5)` is
    empty).
  - Here sprintf and fprintf demoted a string scalar to its char row on the way in, so `"5"` under
    `%d` printed 53.
- `warning('pkg:id')` alone is `MATLAB:warning:missingMessageArgument`, and `error('pkg:id')`
  `MATLAB:error:missingMessageArgument`.
  - The identifier here is two or more parts between colons, each an ASCII letter then letters,
    digits or underscores. `'pkg:'`, `':pkg'`, `'a::b'`, `'a: b'`, `'a:1b'`, `'_a:b'`, `'a-b:c'` and
    `'a.b:c'` are messages.
  - `warning('a:b', 5)` is `MATLAB:warning:badMessageArgument`, and
    `warning(struct('message', 'm', 'identifier', 'a:b'))` `MATLAB:warning:missingStructureField`.
- `warning('query', 'all')` and the bare statement print:
  - their heading without a colon, the column heads unindented, each row indented two with its state
    right-aligned in five, and a blank line after the rows;
  - `backtrace` and `verbose` are not listed;
  - with nothing to list, the one line "All warnings have the state 'on'." (or 'off'). An explicit
    setting equal to the default is not listed.
- `getReport(ME)` of a warning raised as an error is headed "Error using warning", in `'basic'` too
  (ADR 0346's divergence).
- `JgsBuiltins.Warn`'s warning raised as an error had line 0 in `ME.stack`. R2025b's colon warning
  has the line of the statement it failed in.
- The reports that cannot raise their error in the caller's place, with their identifier in the error
  state (timer and listener probed headless):
  - A listener callback's failure (`MATLAB:callback:error`) and a destructor's failure
    (`MATLAB:class:DestructorError`) print nothing and leave lastwarn alone. Here they printed and
    recorded.
  - `delete` of a running timer under `MATLAB:timer:deleterunning` in the error state reports a
    destructor error ("The following error was caught while executing 'timer' class destructor:"), and
    the timer is deleted.
  - A timer callback that warns under an identifier in the error state prints "Error while evaluating
    TimerFcn for timer 'timer-1'" and the message, as for any callback error.

**Item 308.**
- R2025b's evalc and console show NUL as a blank: `fprintf`, `disp`, a display, an error message and
  a warning alike. A file written by fprintf keeps it, and `sprintf` keeps it.
- R2025b's evalc drops the U+FFFF characters that lead its capture, and only those:
  - `fprintf('%s', char(65535))` is empty, and `'%sa'` is `a`;
  - `disp(char(65535))` is the newline alone;
  - `fprintf('a'); fprintf('%s', char(65535))` keeps it, and so does `'a%sb'`.
- `ME.Correction` is R2025b's fifth MException property. It is the 0-by-0 double for every error a
  -batch session raises (error(), MException(), an undefined function, an undefined variable, a bad
  index, a syntax error), and is listed by `properties` and `isprop`. Here there was no such field.

## Decision

### dir and ls anchor to the current folder; delete takes R2025b's patterns

`ResolveFolder` anchors a relative pattern to `host.CurrentDirectory`, the folder `pwd` reports.
That is where `cd` moved to, else the run's folder, else the working directory.

The MATLAB dialect's `delete` of a name goes through `DeleteNamedFiles`, which follows the measured
rules above:
- Matches go through ADR 0340's `FileVerbMatching` (simple, case-insensitive on Windows), and are
  deleted in name order.
- A read-only file loses the flag first (`RemoveFile`).
- Both warnings go through `JgsBuiltins.Warn`, so `warning('error', id)` raises them.
- A name is resolved in the current folder first, then through the read resolver, as `copyfile`'s
  source is.

The JGS dialect's `delete` is unchanged.

### fileread detects the encoding as R2025b's fopen does

`JgsSourceText.DecodeDetected` is ICU's recogniser at the confidence of 80, over the whole file, then
UTF-8 with .NET's replacement or Windows-1252. The MATLAB dialect's `fileread` uses it when no
encoding is named; JGS keeps reading UTF-8. A named `windows-1252` (or `cp1252`) decodes through the
same Windows-1252 table, and `US-ASCII` with a U+FFFD fallback. `fopen`'s own reading (fgetl,
fscanf, fread of `*char`) is not changed.

A character past the basic plane is kept as its surrogate pair (below).

### sprintf and fprintf keep their strings

The two are on `StringAwareBuiltins`, so every wrapper around them hands a string scalar on as it
came. The core:
- `PrintfArguments` demotes the format to its char row on MATLAB's road, and every string on JGS's
  road. `WrapFormatters` reads a string-scalar format, so its escapes are still decoded.
- `JgsSprintf.FormatMatlab(format, args, stringRules: true)` marks each string's group.
- Where a conversion meets a marked group, `StringUnder` refuses as R2025b does, or hands the text on
  (`%s`, `%c`), or the number it spells (`NumberSpelledBy`).
- `TakeStarValue` reads a string's number, and stops the pass when it spells none.
- A refusal is a `JgsSprintf.PrintfRefusal`, a `FormatException` with an identifier, which sprintf and
  fprintf raise under it.

Other callers of `FormatMatlab` (compose, the designer's value text) keep the two-argument form and
the old reading.

### The warning leftovers

`warning` and `error` in the MATLAB dialect refuse a lone identifier (`RefuseLoneIdentifier`,
`IsMessageIdentifier`). `warning` also refuses a non-text message after an identifier, and a struct
without both `state` and `identifier`.

`WarningQuery` prints R2025b's layout, as described above.

A warning raised as an error carries `UsingName = "warning"`. A `catch` marks the caught MException
with any `UsingName` (`JgsBuiltins.MarkUsing`), and `getReport` heads a marked exception "Error using
<name>" in every form. JGraph writes no "Error in" lines for any report, so the default form is that
header and the message.

`JgsBuiltins.Warn`'s error carries `TakesStatementLine`. `ExecuteBlock`'s statement loop (plain and
hooked) has an exception filter, `JgsRuntimeException.PlaceAtStatement`, that gives such an error the
running statement's line before any frame unwinds. The filter never catches, so the plain path pays
one local store per statement. `JgsException.Line` and `Column` gain a private setter for it
(`PlaceAt`).

`JgsWarningState.ReportUnraisable` is the destructor's and the listener's report: silent in the error
state, else recorded and shown when on. The timer's delete reports the destructor error in the error
state, as measured.

### evalc and the console

`JGraphScriptGlobals.Blanked` turns NUL into a blank in everything MATLAB-dialect code prints, writes
or warns, captured or not. evalc trims leading U+FFFF from what it answers.

### MException's Correction

`MakeException`, `CaughtException` and the .NET exception's value carry a `Correction` field holding
the 0-by-0 double (`NoCorrection`), so it reads, lists and answers `isprop` as R2025b's.

## Consequences

- New parity fixture `oi_files_printf_evalc` (236 rows from R2025b 25.2, one a divergence): `dir`
  after `cd` (11 patterns), `delete`'s patterns and warnings, 30 files and 11 named encodings through
  `fileread`, 46 string cases under sprintf and fprintf, 19 lone identifiers through `warning` and
  `error`, seven tables, the reports, the engine warning's line, the unraisable reports, 20 evalc
  captures, and `Correction`.
- `oi_warning_error_state`'s row `caught_report` is an agreeing line (re-recorded).
- New test class `FilesPrintfEvalcLeftoversTests`.
- A script that printed a string under `%d` expecting its codes now gets R2025b's refusal. A script
  that called `error('pkg:id')` or `warning('pkg:id')` with no message now gets R2025b's refusal.
- A relative `dir` or `ls` in a file run lists the run's folder, which `pwd` reports, rather than the
  host's working directory.

## Measured

- Probes `probe300`, `probe303`, `probe306`, `probe308` and `probe2` in R2025b 25.2 (about 330 cases),
  headless. The fixture was recorded from them.
- Tests: the new fixture and `oi_warning_error_state` agree in both lanes, as do 12 fixtures that touch
  the file verbs, printf, MException, destructors, listeners and timers (14 in all, packed and boxed).
  `FilesPrintfEvalcLeftoversTests` (66) and 36 neighbouring classes (1170 tests) pass, one timing test
  aside (`TimerCallbacksM167Tests.TwoTimersFireInDueOrder`, which fails and passes by turns on the
  loaded machine).

## Divergences

- **`fileread` keeps a character past the basic plane as its surrogate pair**; R2025b writes U+001A
  (its substitute character) for each, losing it. ADR 0338 saw the same. Fixture row `fr_astral`.

## Still open

- **R2025b's fresh warning table lists six identifiers off by default**
  (`MATLAB:RandStream:ReadingInactiveLegacyGeneratorState`, `MATLAB:JavaComponentThreading`,
  `MATLAB:JavaEDTAutoDelegation`, `MATLAB:RandStream:ActivatingLegacyGenerators`,
  `MATLAB:class:DynPropDuplicatesMethod`, `MATLAB:mir_warning_unrecognized_pragma`). This build's
  lists `verbose` instead. `s = warning` therefore differs in a fresh session; the printed table no
  longer lists `verbose`.
- **`fopen`'s own reading** (fgetl, fgets, fscanf, `fread(fid, '*char')`) with no encoding named was
  not changed to the detected encoding. Only `fileread` was.
- **Whether R2025b's parser and App Designer's reader follow ICU's rule too** was not measured.
  `JgsSourceText.Decode` keeps ADR 0338's stricter rule.
- **The listener's, destructor's and timer's reports** in R2025b carry "Error using ..." and "Error
  in ..." lines; here they carry the message alone, as before.
- **`checkcode`'s `MATLAB:mlint:UnusedArgs`** is still recorded without being shown and does not obey
  the error state. AppBase's `saveobj` warning and ui HTML's warnings need a window to probe, and were
  not changed.
- **A warning printed with no identifier** cannot be put in the error state, in R2025b either.
- **`fwrite(1, ...)`** is refused here ("1 is not an open file"); R2025b writes to the console and
  drops NUL there (not a blank).
- **`?MException`** does not resolve here, so its metaclass (with `Correction`'s `SetAccess`
  private) cannot be read. `ME.Correction = x` is refused in R2025b (`MATLAB:class:SetProhibited`).
  Here an MException is a struct and the field can be written.
- **A string format with `%s` of a missing string under `compose`** and the other callers of the
  two-argument `FormatMatlab` were not measured against strings.
