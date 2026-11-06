# ADR 0307 — Packages: the code tree, dotted names, scripts in packages, and the tools over them

## Status

Accepted, 2026-11-05. Stage P1 of the packages and class folders plan
(`docs/plans/packages-and-class-folders-plan.md`, gitignored), which is the App Designer window
plan's carried-in limit W-P2, the compiler plan's K-P1 and open item 195. P0 (the recordings) landed
no code. P2 (classes in packages) to P7 follow.

## Context

JGraph's MATLAB dialect resolved bare names through the search order M145 measured (ADR 0149) and
dotted names through the .NET namespaces (ADR 0174), JGraph's own built-in packages and a loaded
class's statics, and nothing else: a `+pkg` folder on the path was a folder the index never looked
inside, `pkg.f(2)` was "Unable to resolve the name 'pkg.f'", and a package's scripts, `private/`,
nesting, handles, `which`, `what`, `help`, `type`, `dbstop`, `nargin` and `addpath`'s refusals were
all absent. The plan's research (`docs/plans/notes/packages-research-2026-11-05`, five probes run
headless in R2025b and again through JGraph) measured what a package is, and the fixture
`p1_packages` writes the rules as 149 lines recorded from R2025b 25.2.

## Decision

- **The file index lists folders too.** Each search folder's entry gains the names of its `+name`
  and `@Name` sub-folders, read in the same listing as its stems and refreshed by the same two rules
  (the host's `FileChanging` seam at once, the folder's write time once per statement), so a package
  made or a member written is seen as a file is. `AnyPackages` is one flag over the search folders,
  false in almost every session.
- **`JgsCodeTree` is a walk, not a cache.** It descends `+pkg/+sub` under every search folder in
  search order through the index's entries - one set lookup per folder per segment - and answers the
  first folder holding a member, every folder holding a package, and a package's merged contents.
  One package spread over several folders is one package, the first folder winning per member
  (measured).
- **A dotted chain of plain names is asked of the tree first.** `EvaluateMember` reads `pkg.f`,
  `pkg.sub.h` before the class and .NET readings, because a file `pkg.m` beside `+pkg` - a function,
  or a class with a Constant `f` - never takes the dotted name from the package (measured); a
  variable `pkg` wins, which the reading checks for itself. A member is loaded by the function path
  under its dotted name, through the same loader, cache, staleness re-read and `private/` rule as a
  plain file: a function file is its main function, a script the script callable, a class file its
  constructor (the qualified class name is P2's). Members never see each other bare, so
  `+pkg/g.m` calling `f(x)` is undefined, as measured.
- **Scripts live in packages.** `pkg.s` runs in the caller's workspace; `x = pkg.s`, `pkg.s(1)` and
  a plain script asked for a value refuse with R2025b's `MATLAB:scriptNotAFunction` words; `feval` of
  a script at the prompt runs it, as R2025b does.
- **Handles and names as text.** `@pkg.f`, `str2func` both spellings, `feval`, `eval`, `evalin`,
  `cellfun`, `nargin`, `which`, `dbstop in pkg.f` and `clear pkg.f` all reach the member through the
  resolver's `Dotted` mode; two handles to one member compare equal; `functions(@pkg.f).file` is ''
  as measured.
- **The tools.** `what pkg`, `what +pkg`, `what pkg/sub` answer one struct per folder holding the
  package; `help pkg` prints R2025b's "Contents of pkg namespace:" listing, and `help` of a code file
  - packaged or plain - prints its first comment block or "name is a function." with the header,
  and hands the text back when an output is asked; `type pkg.f` is "File 'pkg.f' not found." where
  `type('+pkg/f.m')` reads the file through the search folders; `addpath` of a `+`, `@` or `private`
  folder warns with R2025b's three identifiers and adds nothing; `rmpath`'s warning and `genpath`'s
  trailing separator are R2025b's; `dbstack` prints a frame qualified by its package and a local
  function as `file>name`; a `+` or `@` folder made current with `cd` gives its files no bare names.
- **Three lifts the fixture needed.** `cd` with an output answers the folder left (open item 244);
  eval'd text counts its lines from the eval's own line; two statements juxtaposed on one line
  (`pkg.f 2`) are MATLAB's parse error under `MATLAB:m_missing_operator`, carried through `eval`; a
  dot on a number is "Dot indexing is not supported for variables of this type."; a one-shot run
  keeps the breakpoints `dbstop` sets so `dbstatus` lists them as R2025b's `-batch` does.

## Consequences

- Every site that resolves a name by text has one more road, `JgsNameResolver.Dotted`, and every
  site that reads a dotted expression one more reading, `TryPackageInFront`; both cost nothing
  while no search folder holds a package.
- The function path keys package members by their dotted name in a dictionary of their own, so a
  plain-name miss evicts only its own entry.
- Classes in packages construct by their dotted name but are still keyed by their bare name;
  P2 qualifies them. Class folders are listed by `what` and `help` and define nothing until P4.
  Imports of packages are P3.

## Measured

- **The fixture** `p1_packages` (149 lines recorded from R2025b 25.2, one div row): the calls, the
  refusals and their words, handles and name-based calls, `nargin`, `mfilename`, `dbstack`'s printed
  and struct names, `dbstop`, scripts, `which`, `exist`, `what`, `help`, `type`, `addpath`, `rmpath`,
  `genpath`, `cd` into a package folder, a file `pkg.m` beside the package as a function and as a
  class, `clear`, and a member written after the package was first used.
- **Tests**: `JgsPackageTests` (6: the index's folder listing and the tree's walk, a member written
  during the run, a package folder as the current folder, handles sharing the member and `clear`
  forgetting it, a script by its dotted name, juxtaposed statements refused); the resolver, file
  index, function file, precedence, debugger, command-syntax, lexer, environment, eval, console,
  deployed-session, .NET import, code analyzer and every parity fixture suite pass on this source:
  701 of 702, the one failure being `p1_packages` itself run before its div row was stamped, green
  in both representations after; the seven verify scripts unchanged (1,275 of 2,024).

## Not measured

- **`x = pkg.sub`** (a package named as a value), **`pkg.x.y` where `x` is a function alone**, a
  package member that is an `.mlapp` or a P-file, and `help` of an indented comment block: not
  probed; the first two fall through to "Unable to resolve the name", the comment block's leading
  whitespace is dropped.
- **`exist('+pkg')` for a package held by a path folder alone** was measured with `+pkg` in the
  current folder; a folder on the path alone answers 0 here.
- **R2025b's message for `MATLAB:m_missing_operator`** is written from its documented wording; the
  fixture pins the identifier.
- **`dbstack` for a nested function** (R2025b's `outer/nested`) is unchanged; a local function's
  `file>name` form is the one measured.

## Divergences

- **A package file written after the package was first used** is seen at the next statement; R2025b
  needs `rehash` (`late_before_rehash`, stamped).
- **`what`'s `m` listing** is sorted without regard to case where R2025b sorts ordinally (`C.m`
  before `dup.m`); the fixture's names are lower-case.
- **A class folder inside a package** is listed and defines nothing (P4); **a class in a package** is
  keyed by its bare name (P2).
- **Eval'd text's lines** all count from the eval's line, so a multi-line text's second line is the
  eval's line plus one where R2025b reports the eval's line for every statement of the text.
