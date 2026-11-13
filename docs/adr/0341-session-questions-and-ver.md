# ADR 0341 — The session's questions: ver, prefdir, userpath, usejava, inmem, the locks and feature('numcores')

## Status

Accepted, 2026-11-12. Open items 169 and 245, taken together: every name they list was
`MATLAB:UndefinedFunction` in JGraph, in a session and in a deployed program.

## Context

**Item 169.** K0's `k0_env` probe asked a compiled program's session what R2025b's runtime answers.
JGraph had no builtin for `prefdir`, `userpath`, `ver('MATLAB')`, `usejava('jvm')`,
`usejava('desktop')`, `feature('numcores')`, `dbstack`, `inmem` or `mlock` (recordings `env_plain`
against `recordings-jgraph/env_plain` under `tools/matlab-checklist/compiler-probes`). Checked against
HEAD first: `dbstack` exists since the App Designer window plan's stage W11 (ADR 0303), registered in
every session, deployed too; `feature` exists since ADR 0216 and answered `getpid` alone. `getSecret`
belongs to stage K3b and is left there.

**Item 245.** `ver`, `ver('compiler')` and `v = ver(product)` were undefined, so a script that reads a
version line from `ver('matlab')` or `ver('compiler')` failed outright. Stage K17b's
`probe_k17_excel.m` guards its `ver` in a try for that reason. The guard stays.

R2025b, measured headless (`-noFigureWindows -batch`) and read from its sources (`ver.p`, whose MATLAB
Online shadow `connector2/shadowfiles/shadows/ver/ver.m` is readable; `usejava.m`, `prefdir.m`,
`inmem.m`, `mlock.m`, `munlock.m`, `mislocked.m`; `toolbox/local/userpath.m`):

| Question | R2025b |
|---|---|
| `v = ver` | 1-by-110 struct, fields Name, Version, Release, Date, sorted by name by character code |
| `ver('MATLAB')` | MATLAB, 25.2, (R2025b), 28-Jul-2025; asked as `matlab`, `"MATLAB"`, `general`, `Toolbox/Matlab`, `toolbox\matlab` alike |
| `ver('compiler')` | MATLAB Compiler, 25.2, (R2025b), 28-Jul-2025; `compiler_sdk` is MATLAB Compiler SDK |
| `ver('MATLAB Compiler')`, `ver('nosuch')`, `ver('')`, `ver('MATLAB ')` | 0-by-0 struct with the four fields; the statement form warns `MATLAB:ver:NotFound` |
| `ver(5)`, `ver({'MATLAB'})` | warns `MATLAB:ver:NotAString`, 0-by-0 struct, in both forms |
| `ver('a', 'b')`; `[a, b] = ver` | `MATLAB:TooManyInputs`; `MATLAB:TooManyOutputs` |
| `ver('-support')` | the list with a fifth field, Licenses, a cell |
| `ver` printed | a rule of dashes as long as the longest header line, four header lines (MATLAB Version, MATLAB License Number, Operating System, Java Version: Java is not enabled), the rule again, then a row per product: the name in 50 columns (cut to 47 and `...`), four blanks, `Version x` in 16, four blanks, the release in the widest release's width (7 at least), trailing blanks dropped, then every row padded to the longest (`disp(char(rows))`), MATLAB first |
| a folder on the path with `Contents.m` | its first line the name (a final full stop dropped), its second `Version v [release] date` (`^Version\s+(\S+)\s*(.*?)\s+(\S+)$`), the date cleaned (`5-mar-2025` is `05-Mar-2025`); asked by the folder's name, not the product's |
| `usejava` of `'jvm'`, `'awt'`, `'swing'`, `'desktop'` | true, true, true, false in `-batch`; a deployed program's jvm is true, false under `-R -nojvm`; an unknown feature false; a number or a cell `MATLAB:unassignedOutputs` |
| `prefdir` | `%APPDATA%\MathWorks\MATLAB\R2025b`; `prefdir(1)` makes it; text refused as `MATLAB:prefs:BadInput`; a deployed program's is `<ctfroot>\.matlab`, or under `%APPDATA%\MathWorks\MATLAB\mcr_v252\<uuid>` for an archive beside its program (`mcc -C`) |
| `userpath` | `Documents\MATLAB`, '' when the folder is not there; `userpath(folder)` must name an absolute folder that exists (`MATLAB:userpath:invalidInput`), and it, `'reset'` and `'clear'` move the folder on the path and save the setting |
| `feature('numcores')` | 14 on a 6 P-core, 8 E-core part, the key in any case; a further argument `MATLAB:maxrhs`; the statement form prints five lines about the cores first |
| `[M, X, C] = inmem` | the loaded files as an n-by-1 cell in load order, a private one as `folder\private\name`; X 0-by-1; C the classes; `'-completenames'` full paths, matched as written (`'-COMPLETENAMES'` is no option); any other text ignored, a number refused |
| `mlock` in a function | its file is held: a persistent counter counts on through `clear name`, `clear functions` and `clear all`, and `inmem` still lists it; `munlock` lets it go. A local function locks its file. An anonymous function locks the file that wrote it |
| `munlock(name)` | a name that resolves to nothing is `MATLAB:dispatcher:mlockNotLocked` ("The file 'x' is not locked."); an unlocked file or a builtin is fine |

## Decision

### Where they live

`JgsBuiltins.SessionQuestions.cs` declares `ver`, `prefdir`, `userpath`, `usejava`, `inmem`, `mlock`,
`munlock` and `mislocked` from `RegisterEvalBuiltins` (they ask the interpreter and the host), and
`EvalBuiltinNames` lists them. `feature('numcores')` is a branch of `feature`
(`JgsBuiltins.Matlab.cs`), answering `ProcessorTopology.PhysicalCoreCount()`, the physical cores of
every efficiency class (the existing `PerformanceCoreCount` counts the fastest class alone).

### What ver lists

`ver` follows R2025b's `ver.m` line by line: the forms, the warnings, the struct, the row layout, the
match by folder name, `Contents.m` on the search path. What it lists is JGraph's:

- **JGraph** itself. Its Version and Release are the two parts of `version`: `'0.2.3 (JGraph)'` gives
  0.2.3 and (JGraph), as R2025b's MATLAB row splits `'25.2.0.2998904 (R2025b)'`. Its Date is the build's,
  the file time `version('-date')` reads, written `dd-MMM-yyyy`.
- **MATLAB**, the dialect JGraph follows, as R2025b's row reads: 25.2, (R2025b), 28-Jul-2025. This is
  decision KO2's release, the one `mcrversion` answers, so a script's `ver('matlab')` version test
  branches as it would under R2025b.
- **MATLAB Compiler** and **MATLAB Compiler SDK**, in a session that compiles: JGraph's `mcc` and
  `compiler.build` make R2025b's builds (the compiler and SDK plan). A deployed session has neither, as
  R2025b's runtime ships neither, so it lists neither.
- **Every folder on the search path whose `Contents.m` names a version**, read as R2025b reads it.

No other toolbox is listed, although JGraph implements much of several (and `license('test', ...)`
answers 1 for every feature).

The header names JGraph: `JGraph Version: <version>`, `JGraph License Number: <license>` (JGraph, or
`unknown` in a deployed program, as `license` answers), the operating system, and `Java Version: Java is
not enabled`.

### usejava

JGraph runs on .NET and has no Java, so `jvm`, `awt` and `swing` answer false. That is what a script
guarding its `java.*` calls must see (`usejava.m`'s own examples guard `java.awt.Frame` and a "requires
Java" error with these). `desktop` is true under the Script Workspace's window (the one host that gives
a session a table viewer) and false in `-batch`, a console or a deployed program, as R2025b's is false
in `-batch`.

### prefdir and userpath

`prefdir` is the folder JGraph keeps its own settings in, `%APPDATA%\JGraph`, or the folder
`JGRAPH_PREFDIR` names (the device preferences read the same variable). A deployed program's is
`ScriptDeployment.PreferencesFolder`: `<ctfroot>\.matlab`, which the archive's extraction already
makes, or `%APPDATA%\JGraph\mcr_v252\<uuid>` for an archive extracted beside its program
(`ScriptDeployment.ArchiveBeside`, new).

`userpath` is R2025b's default, `Documents\MATLAB`, the folder `createProject`'s default root already
sits under, and '' when it is not there. The setters move the folder on the search path for the
session; a deployed session refuses them as it refuses every path change.

### The locks

`Interpreter.Locks.cs` (a new part of the interpreter) keeps the locked files:
- `RunningCodeFile` is `CurrentFile`, which is already the closure's file for an anonymous body;
- `SetFileLocked` and `IsFileLocked` hold and test the set;
- `ForgetUnlockedPersistents` is `ForgetPersistents` sparing a locked file's functions.

`clear all` and `clear functions` call `ForgetUnlockedPersistents` (`JgsRunner`). `JgsFunctionPath`'s
`Unload()` and `Unload(name)` keep a locked file loaded, so its declarations, and with them its
persistents, survive. `mlock` at the prompt, with no file running, does nothing, as in R2025b.

### inmem

`JgsFunctionPath.LoadedFiles()` lists the path's loaded files: plain names, then dotted package
members, then private files as `folder\private\name`. The classes defined from files follow when
their file is not already listed, and they are C.

## Consequences

- `k0_env`'s `prefdir`, `userpath`, `ver.matlab`, `usejava.*`, `feature.numcores`, `exist.inmem`,
  `call.inmem` and `call.mlock` rows have an answer in JGraph; `env_plain`'s JGraph recording is now
  stale (it is regenerated by the K1 oracle, which needs a compiled program and was not rerun).
- Dispatch sweep 42 (`classify-42.csv`, `dispatch-probe42.csv`, `nargout-42.tsv`): all eight names go
  to a shadowing file under every pattern. `ver` is a P-file (exist 6), `userpath` and `usejava`
  M-files, the other five built-ins; `prefdir`'s nargout is `MATLAB:narginout:doesNotApply`. The
  dispatch table and `JgsBuiltinOutputCounts` are regenerated.
- The coverage document moves to 1,284 of 2,024 across every callable kind and 454 of 514 builtins.
- The parity fixture `oi169_session_questions` was recorded from R2025b 25.2 (163 rows, four of them
  divergent: the header's labels and the three Java features), green in the packed and boxed lanes.
  Paths, core counts and JGraph's dates are asked as classes, shapes and relations; ver's rows and a
  toolbox's `Contents.m` are compared as laid out, blanks shown as dots.
- `DeployedSessionTests.Runtime_AnswersTheSessionQuestionsK0Asked` runs the k0_env questions in a
  deployed session: prefdir under ctfroot, the dialect's row and no compiler, no Java, the locks, inmem,
  and `userpath('reset')` refused.

## Divergences

- **`ver` lists JGraph, the MATLAB dialect, and MATLAB Compiler and its SDK, where R2025b lists its
  installed products (110 here).** The header's first two lines name JGraph, and its operating-system
  line leaves out the Windows edition (`Microsoft Windows 11 Version 10.0 (Build 26200)`).
- **`usejava('jvm')`, `usejava('awt')` and `usejava('swing')` are false; R2025b's are true** (in a
  deployed program too, unless built with `-R -nojvm`). JGraph has no Java, and a script guarding a
  `java.*` call must see that.
- **`feature('numcores')` as a statement prints only `ans`**, where R2025b first prints five lines on
  what it detected.
- **`prefdir` is `%APPDATA%\JGraph`**, where R2025b's is `%APPDATA%\MathWorks\MATLAB\R2025b`; a
  deployed program's beside its archive is under `%APPDATA%\JGraph\mcr_v252`.
- **`userpath`'s default folder is not put on the search path when a session starts**, as R2025b's is,
  and a setter's change lasts for the session, where R2025b saves it.
- **`inmem` lists the program's files alone**, where R2025b's list starts with its own toolbox files;
  JGraph's builtins are not files. A script that is running is not listed either (R2025b lists it):
  the path's loaded files hold functions and classes, not a script run by name.

## Still open

- `ver('fixpoint')`, `ver('powersys')` and `ver('xpc')`: R2025b warns `MATLAB:ver:ProductNameDeprecated`
  and answers the renamed product; JGraph lists none of them and answers empty.
- `ver -support` printed: the license column's words are unmeasured (JGraph writes `License <name>`).
- `munlock`/`mislocked` given a package member's dotted name, and an anonymous function written in one
  file and called in another, are unmeasured.
- The console's own reset (`JgsReplSession.Clear`, the host's Clear Workspace) rebuilds the interpreter
  and drops every lock with the persistents, as it always has.
- A compiled R2025b program built with `-R -nojvm` was not run for `usejava` (the recording is K0's
  default build); JGraph answers false either way.
