# ADR 0311 — Completion, the Code Analyzer and the editors over packages and class folders

## Status

Accepted, 2026-11-06. Stage P5 of the packages and class folders plan
(`docs/plans/packages-and-class-folders-plan.md`, gitignored; W-P2, K-P1, open item 195), after P4
(ADR 0310). P6 (deployment, the typed interface, App Designer) and P7 (the guide, acceptance, the
gate) follow.

## Context

P1 to P4 taught the interpreter `+package` and `@Class` folders; the tools around the code had not
caught up. The editor's completion offered nothing after `pkg.` or `pkg.C.`, and `obj.` in a class
file knew the buffer's own members alone, never a class folder's method files. The Code Analyzer
had none of R2025b's checks of a name against its file: a function named otherwise than its file
(`FNDEF`), a dotted function name (`FNDOT`), a class named otherwise than its file (`MCFIL`) or its
`@` folder (`MCDIR`), and a method with no input in a block that is not `Static` (`NOIN`), the last
one raised over the signature lines P4's parser began to read; its name forms stopped at a plain
name, where R2025b's `checkcode` reads `Thing/extent`, `+pkg/g`, `@Thing/extent` and a bare method
name. `edit` was not a builtin at all, so a headless script calling it failed where R2025b's says
nothing, and the Script Workspace's `edit name` knew no dotted, slash or relative form. The rules
were measured by three probes (`tools/matlab-checklist/ui-probes/p5`: `p5_analyzer`, `p5_more`,
`p5_display`) and pinned by the fixture `p5_tools` (104 rows recorded from R2025b 25.2, none
divergent) and by thirty-nine new cases of the analyzer oracle.

## Decision

- **Five checks of a name against its file, as measured.** `FNDEF` (warning, a fix) on a file's
  main function whose name is not the file's stem, case and all (`Casename` in `casename.m` is
  flagged), in a plain, `+package`, `@Class` or `private` folder alike, the local and nested
  functions never checked; its text `Function name 'x' is known to MATLAB by its file name: 'y'.`
  and its fix the file's name written over the function's. `FNDOT` (error, no fix) beside it on a
  dotted name, which the parser now reads in a plain file rather than refusing (`function r =
  pkg.dotted(x)` parses as R2025b parses it). `MCFIL` (error, a fix) on a classdef whose name is not
  the file's, its fix renaming the class and its constructor. `MCDIR` (error, no fix) before it when
  the file sits in an `@` folder named otherwise, its text naming the file's full path. `NOIN`
  (warning, no fix) on a method with no input in a block that is neither `Static` nor `Abstract`,
  bodied or a signature line, any access, the constructor excepted; the span is the method's name
  (`SourceSpans.SignatureName` reads a signature line's name as it reads a `function` header's).
  A signature line raises nothing else: it is not walked as a body (it has none), so no `INUSD` or
  `STOUT` over it. A constructor's output is never `STOUT` either (R2025b raised none; measured).
  `%#ok<FNDEF>` suppresses as any other, `-notok` shows it, `-cyc`'s complexity line names the
  function by its written name and sorts first, and `codeIssues` lists `FNDEF` as `warning`/`auto`.
- **checkcode's name forms.** `ResolveCodeFile(…, partialPaths: true)` reads, after a path and a
  name on the path, the slash form (`Thing/extent` through `ClassMethodFile`), a relative path
  under each search folder with or without its extension (`+pkg/g`, `@Thing/extent`, `@Mis/Mis`
  through `FindRelative`) and a bare method name (the first class folder's file, as `which` names
  it); `Thing` is the class folder's class file, `misnamed` the current folder's file first.
  `pkg.f`, `pkg.C`, `Thing.extent` and `pkg/g` are `MATLAB:mlint:FileNotFound`; `codeIssues` takes
  none of the partial forms (`Thing/extent` and `@Thing/extent.m` are `FileNotFound`, `pkg.f` is
  `InvalidFile`), as measured. `codeIssues(folder)` walks `+`, `@`, `private` and plain sub-folders
  alike (R2025b's `IncludeSubfolders` default is true), the files in case-blind path order and the
  issues in ordinal path order - the W10 rule, holding over the new folders.
- **`edit` is a builtin that says nothing.** Headless R2025b's `edit` opens nothing and prints
  nothing for every form (`edit('pkg.f')`, `edit pkg.f`, `edit('Thing/extent')`, `edit('Thing')`,
  `edit('Thing.extent')`, `edit('@Thing/extent')`, `edit('good.m')`, `edit('misnamed')`, measured)
  and refuses a non-text argument (`MATLAB:validators:mustBeTextScalar`, `Invalid argument at
  position 1. Value must be a character vector or string scalar.`); JGraph's does the same. The
  Script Workspace reads `edit` at its prompt before the interpreter sees it, and asks its MATLAB
  session, an `IScriptCodeLocator`, for the file the name stands for: `JgsReplSession.LocateCodeFile`
  answers a package member by its dotted name, `Thing.extent` and `pkg.C.stat` as the class's method
  file or else its class file, and every form `checkcode` takes. A name with no file is left to the
  old road (the open documents, the workspace's scripts, the builtin catalog).
- **Completion from the session's code tree.** `JgsReplSession.Members(qualifier)` asks
  `PackageCompletion` before the .NET names: after `pkg.` the package's members off `JgsCodeTree`
  - a function, a script (`script pkg.s`), a class, a class folder, a sub-package, each by its kind
  read from its file's first word - and after a class's name (`pkg.C.`, `Thing.`) its statics,
  constants and enumeration members. A loaded class answers from its definition; one not loaded
  from its file, parsed as text (a saved file parses; nothing runs, no property default is
  evaluated). `IScriptCompletionSource.ClassMembers(className)` is the new question for an
  instance's members: `obj.` in a class file joins the buffer's own members (U7's reading) with the
  session's view of the class, so an `@` folder's method files appear; `o.` in a method file of an
  `@Thing` folder - the first parameter of the buffer's first `function`, the folder read off the
  buffer's path (`ScriptEditorControl.CompletionFilePath`) - offers `Thing`'s properties, public
  methods and method files; a private method or `private/` function is offered nowhere.
- **The Files pane needs nothing.** It lists a `+` or `@` folder as any folder, opens it on a
  double-click as the Current Folder does, and opens its files by extension; the plan's note that
  no special icon is needed stands.

## Consequences

- `CodeChecks` gains `FNDEF`, `FNDOT`, `MCFIL`, `MCDIR` and `NOIN`; `ScopeAnalysis` takes the
  file's name or path (`_fileName`) for `MCDIR`'s text and the `@` folder it reads, so the analyzer
  stays text-only otherwise. The editors' underlines, message bar, hover and Fix follow from the
  messages as W10 wired them; `checkcode`, `mlint`, `codeIssues` and `fix` print and apply them.
- `Scope.IsConstructor` keeps `STOUT` off a constructor's output. A signature line is skipped by
  `AnalyzeFunction` (as an abstract one was), so `ClassInheritanceU6Tests`' classes with signature
  lines raise nothing new.
- The parser's `ParseMatlabFunction` reads a dotted function name in the MATLAB dialect outside a
  methods block; what such a file does when run is not measured (JGraph hoists the function under
  its written name).
- `IScriptCodeLocator` is a capability beside `IScriptCompletionSource`; both are called on the UI
  thread while the session may run and read the path's index, never the script's state.
- `PackageCompletion` parses a class file with the real parser; a buffer mid-keystroke is never
  given to it (that is `ClassCompletion`'s text reading), only a file on disk.

## Measured

- **The fixture** `p5_tools` (104 rows from R2025b 25.2, none divergent): the five checks over
  thirty-seven files in a plain folder, `@Thing` and its `private/`, `@Mis`, `@Lone`, `+pkg`,
  `+pkg/@D` and `+pkg/private` (ids, lines, columns, fix flags, texts), their severities, the
  `-string`, `-id` and `-cyc` forms, `checkcode`'s name forms from the current folder and from the
  library with their refusals, `codeIssues`' refusals and its walk of the library with the rows'
  order, `Severity`/`Fixability`/`CheckID`/columns of an `FNDEF` row, `fix` of `FNDEF` (status row,
  the renamed line, no issue left) and of `MCFIL` (class and constructor renamed), `help` and
  `type` of a class folder's method, and `edit` headless in eight forms plus its refusal.
- **The oracle** `CodeAnalyzerOracle.json` gains 39 cases (`p5/…`, `p5more/…`) from the probes'
  files, replayed by `CodeAnalyzerOracleTests` (every message's id, line, columns and fixability).
- **Tests**: `CodeAnalyzerTests.NameChecks_FixTheNameAndReadTheClassFolder` (FNDEF's fix, MCFIL's
  fix over class and constructor, MCDIR's text and absence, NOIN over signature lines);
  `PackageCompletionTests` (4: a package's members by kind, a class's statics before and after it
  loads, an instance's members in the class file and in a method file, the session's file for every
  `edit` form); the analyzer builtin, completion, interop-views, class-folder, package, classdef,
  parser and call-contract suites and the parity fixture suite pass in both representations; the
  seven verify scripts unchanged; the harvest regenerated.

## Not measured

- **A dotted-name function file when run** (`dotted(1)` with `function r = pkg.dotted(x)` inside):
  JGraph defines the function under its written name.
- **`r = edit('good')`** (an output): the probe's reading went through `evalc` inside an anonymous
  function and answered a static-workspace error, which is the probe's, not `edit`'s.
- **`edit` of a name with no file**: R2025b asks whether to create it, even in `-batch`, which no
  script can answer; JGraph says nothing headless and, in the window, reports the name not found.
- **Completion itself**: R2025b has no headless completion to record; what JGraph offers is its own
  reading of the same folders.
- **`MCDIR` for a class file given by a relative path**: R2025b printed the absolute path when the
  file was named absolutely; JGraph prints the path the analysis was given, which `checkcode`
  resolves to the full path first.

## Divergences

None recorded by the fixture.
