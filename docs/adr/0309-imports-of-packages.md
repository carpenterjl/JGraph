# ADR 0309 — Imports of packages: one import list over .NET and +package names

## Status

Accepted, 2026-11-05. Stage P3 of the packages and class folders plan
(`docs/plans/packages-and-class-folders-plan.md`, gitignored; W-P2, K-P1, open item 195; decision
PD1), after P2 (ADR 0308). P4 (class folders) to P7 follow.

## Context

`import` reached .NET names alone (ADR 0176): `import pkg.*` was accepted and reached nothing, and
`import pkg.f` was refused as naming no type, so every R2025b file that imports a package failed
here. R2025b's rules for package imports were measured by the plan's research probes and pinned
by the fixture `p3_imports` (120 rows recorded from R2025b 25.2, no divergence row), which also
re-measured what the research left open: the order of two wildcards, a package wildcard against a
.NET one, a script's import list, and handles, `str2func`, `feval` and `eval` against a file of
the imported name in reach.

## Decision

- **One list per scope, both worlds.** `NetImports` became `JgsImports`; an entry is a dotted name
  and which world it names is decided when a name is looked up: a package's member (a function
  file or a script), a class in a package, a class's static method, a sub-package, or a .NET
  type, static method or namespace (`JgsImported`). An explicit entry is checked when its function
  is entered, as before, against both worlds.
- **The precedence as measured.** An explicit import answers after the variables and before
  nested and local functions, a wildcard after local functions and before everything else — a
  written call's `private/` folder, the current folder, the path and the built-ins included
  (`import pkg.*; max([1 5 2])` is `pkg.max`). Among explicit imports of one name the first written
  wins (`import pkg.f; import other.f` is `pkg.f`); among package wildcards the earlier written
  wins (`import pkg.*; import other.*; f(2)` is `pkg.f`, swapped it is `other.f`); any package
  wildcard beats any .NET wildcard whichever was written first (`import System.Math.*; import
  pkg.*; Pow(2, 3)` is `pkg.Pow` in both orders); among .NET wildcards the later wins (ADR 0176,
  kept). A variable beats a wildcard. A wildcard over a package reaches its functions, scripts and
  classes, and its sub-packages only in front of a dot (`sub.h(4)` under `import pkg.*`); a
  wildcard over a class (`import pkg.C.*`) reaches its static methods alone (`K` is undefined,
  measured). A class an import gives the short name answers `C(4)`, `C.K`, `C.stat(1)`, `E.A` and
  `class(C)` as `pkg.C` and `pkg.E`.
- **By text and by handle the private folder comes first.** A name resolved from text — `feval`'s,
  `str2func`'s, a handle's, eval'd code's — gives the running file's `private/` folder the first
  word over a wildcard import where a written call gives it to the import: beside `private/f.m`,
  `f(2)` under `import pkg.*` is `pkg.f` and `@f`, `str2func('f')`, `feval('f', 2)` and
  `eval('f(2)')` are the private file; without it all five are `pkg.f`; a plain `f.m` on the path
  loses to the import in every form; an explicit import beats the private file in every form. The
  resolver's Invoke mode takes a `dynamic` flag for `feval`, eval'd text is marked while it runs
  (`Interpreter.InEvalText`), and Handle mode asks the private folder before the wildcard layer.
- **A handle to an imported name is the member's own.** `@f` under `import pkg.*` is the handle
  `@pkg.f` — `func2str` answers `pkg.f`, `@max` there is `pkg.max`, `functions(h).file` is empty —
  a `NamedHandle` named in full over the member, as P1's `@pkg.f` is.
- **Scope.** A function's imports are its own, seen by its nested and anonymous functions and not
  by a local function or a callee; `clear all` leaves them; `clear import` is refused in a function
  and in a script. A script's imports are a list of its own over its caller's (`JgsImports.Outer`),
  read from the whole file before its first statement — R2025b lists an import written on a
  script's last line from its first — and dropped after the run: `scriptval` lands in the caller's
  workspace and the caller's `import` lists nothing (measured). The base workspace's list persists.
- **The tools.** `which('f')` answers the member's file, over the private folder and the path;
  `nargin('f')` the member's count; `isa(o, 'C')` is true for a `pkg.C` instance under either
  import form; `methods('C')` lists the class; `exist('C')` is 8 plainly and as `'class'`, where
  `exist('f')` is 0 — an imported function is no file to `exist`, which answers 2 only for a file of
  the name in reach (the private folder's, or a plain `f.m` on the path).
- **The refusals in R2025b's form.** `import pkg` (a package alone), `import pkg.nosuch` and
  `import pkg.sub` (a sub-package alone) are `MATLAB:mir_illegal_import_argument`; a nested
  function named like an explicit import is
  `MATLAB:lang:ImportedFunctionAndNestedFunctionHaveSameName`; a variable named like one is
  `MATLAB:lang:ImportedFunctionAndVariableHaveSameName`. Each message is headed `Error: File: <the
  file's path> Line: <n> Column: <c>` pointing at the refused name (the second name on a line at
  its own column, 14 for `import pkg.f pkg.nosuch`) or at the assignment (`f = 5;` at line 3,
  column 1), measured; `ImportStmt` keeps each name's column. `import nosuchpkg.*` is accepted.
- **Found on the way.** `nargin('f')` asked the registration environment rather than the running
  frame for an import; `cellfun`'s refusal of a non-scalar uniform answer had no identifier and
  JGraph's words — it is `MATLAB:cellfun:NotAScalarOutput` in R2025b's words now, as `arrayfun`'s was.

## Consequences

- `JgsEnvironment.Imports` is a `JgsImports`; `Interpreter.ImportFor` answers both worlds and marks
  the session as using .NET only for a .NET answer; `ImportedClass` is the one question `exist`,
  `isa`, `methods` and a class in front of a dot ask. Nothing costs a session with no import.
- Two unmeasured orders moved with the measured one: a .NET wildcard sits below the private
  folder in Handle mode as a package wildcard does, and the first of two explicit .NET imports of
  one name wins as the first of two package ones does (it was the later; net_import has no row for
  either).
- The "Error: File:" heading carries the file's whole path, as R2025b's does; the fixture strips
  its root. A file that fails to parse keeps V6's heading with the file's name alone: the two are
  different errors, and R2025b was measured for this one.

## Measured

- **The fixture** `p3_imports` (120 rows from R2025b 25.2, none divergent): explicit and wildcard
  imports of functions, scripts, classes, enumerations, statics and sub-packages; the precedence
  between two wildcards, two explicit imports, an explicit and a wildcard, a package and a .NET
  wildcard, a variable, a local function, a nested function, the private folder, the current
  folder, a path file and a built-in; the refusals with their messages; the listing and its empty
  shape; nested, local, anonymous and callee scope; `clear all` and `clear import`; a script's
  value and list; `which`, `exist`, `isa`, `nargin`, `methods`; handles, `str2func`, `feval`,
  `eval` and `cellfun` with and without a private file and with a path file; and the fixture's own
  level as a script.
- **Tests**: `JgsImportTests` (3: the refusal's column for a second name and the assignment's
  position, a script's own list read from its whole file and dropped after, a handle made under an
  import called from outside); `NetInteropM176Tests` and `net_import` unchanged; `JgsPackageTests`
  (9); the parity fixture suite and the resolver, precedence, handle, eval, script, class, debugger
  and REPL suites pass on this source; the seven verify scripts unchanged; the harvest regenerated.

## Not measured

- **A script's view of its caller's imports**: a script's list sits over the workspace's it runs in
  (`Outer`), so a function's `import pkg.*` is seen by a script it calls; R2025b was not probed.
- **An anonymous function made inside eval'd text** resolves as a written call does (the wildcard
  before the private folder); only the text's own statements take the dynamic order.
- **A parameter named like an explicit import** is refused at the function's declaration line and
  column; R2025b's position for that case was not probed (net_import pins the identifier).
- **`which -all` under an import** lists the member's file first and the other layers after.
- **The prompt**: an unknown explicit import typed at the prompt is refused without the file
  heading; a script run with `run` takes the script road.

## Divergences

- **A function's import refusals are raised when the function is first entered**, not when its
  file is parsed (ADR 0176's rule, kept): a file with a bad import loads, and its first call is the
  error with the parse-time heading.
