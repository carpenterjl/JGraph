# ADR 0310 — Class folders: a class defined by an @Class folder and its method files

## Status

Accepted, 2026-11-05. Stage P4 of the packages and class folders plan
(`docs/plans/packages-and-class-folders-plan.md`, gitignored; W-P2, K-P1, open item 195; decision
PD3), after P3 (ADR 0309). P5 (completion, the Code Analyzer and the editors) to P7 follow.

## Context

A MATLAB class may be written as an `@Class` folder: `@Thing/Thing.m` holds the classdef and the
folder's other files hold methods, each declared in a `methods` block by a signature line
(`r = extent(o)`) or not declared at all; the folder's `private/` functions are the class's private
methods. JGraph's parser refused the signature line ("A 'methods' block holds functions and nothing
else."), the function path never looked inside an `@` folder, and `@Thing/Thing.m` was reached by
nothing. R2025b's rules were measured by the plan's research probes and pinned by the fixture
`p4_classfolders` (167 rows recorded from R2025b 25.2, two divergence rows), which also settled what
the research left open: the form of `help` for such a class, the argument form of `which`, the
`dbstatus` names, a method file's frame on the stack, and `lastwarn` after the class-folder-wins
warning.

## Decision

- **A class folder is one class.** `JgsFunctionPath.Find` answers a bare name with `@Name/Name.m`
  when a search folder holds that folder with that file - a folder without its class file defines
  nothing (`@Lone/method1.m` alone leaves `Lone` unrecognized and `method1` undefined, measured) -
  and `JgsClassFolder` reads the folder once as the class loads: the method files by stem and the
  `private/` functions. `JgsClass` binds them (`BindFolder`): a declared signature takes the file of
  its name as its body with the attributes of its block (instance, `Static`, `Access = private`); a
  file the classdef never declared is a public instance method; a `private/` function is a private
  method (`Cannot access method 'hidden' in class 'Thing'.` through an instance, `The class Thing
  has no Constant property or Static method named 'hidden'.` through the class, undefined by its
  bare name from outside, all measured). The file name is the method's name
  (`@Thing/misnamed.m` holding `function r = othername(o)` answers to `misnamed`; `othername` is
  undefined, measured); a method file's local functions are its own; a method file parses with the
  `methods`-block rules, so `name@Super(...)` reaches a superclass in it.
- **Everything a method is, a file method is.** Operators (`plus.m`), a built-in's name for the
  class alone (`size.m` answers `size(Thing(3))` and `size(Thing.empty)`, `size([1 2 3])` stays
  the built-in), inheritance across folders (`@Sub/extent.m` overrides `@Thing/extent.m`, the rest
  inherited), a handle class folder (`bump.m` writing `o.N`), a class folder in the current folder,
  and `+pkg/@D` inside a package (`pkg.D(1).dm()`, `methods('pkg.D')` listing `D, dm`, `which
  pkg.D` the class file; `JgsCodeTree.PackagePrefix` steps over the `@` folder so the class is
  `pkg.D`). A class name with a dot reaches a static method of any class (`Thing.staticOne`), and
  `@Thing.staticOne` is a handle to it.
- **The class-folder-wins rule (PD3).** `@Both/Both.m` beside `Both.m` is the class, wherever the
  function file sits on the path; the first resolution of the name raises
  `MATLAB:class:AtFolderPrecedence` once per session in R2025b's words, its two link lines as plain
  text, through the warning state - `lastwarn` holds it, as R2025b's does (measured; the research
  note that it did not was wrong). `which -all Both` lists the class file and then the function
  file; `exist('Both')` is 2 and `exist('Both', 'class')` 8.
- **A class file named for another class** (`@Mis/Mis.m` holding `classdef Wrong`) is R2025b's
  refusal, headed `Error: File: <path> Line: 1 Column: 10` and reading `Class name and filename
  must match.` (`MATLAB:m_class_filename`), the column the class name's own on the classdef line
  (`ClassdefStmt.NameColumn`); the same words for a plain class file (JGraph's own words before,
  replaced). `which('Mis')` still names the file and `exist('Mis', 'class')` is 8: a classdef file
  that does not load is still a class to the tools (measured).
- **A changed folder is a changed class.** `JgsClassFolder.Changed` compares the folder's, its
  `private/` folder's and each method file's write time once per statement; a change drops the
  class and the next mention loads the folder again, as a changed class file is (ADR 0303). R2025b
  needs `clear classes` for a new method file (measured: `late(Thing(1))` is undefined before and
  after `rehash`): the fixture's two divergence rows, in JGraph's favour.
- **The tools.** `which` answers the slash form `Thing/extent` (and `Thing/Thing`, `Sub/extent`,
  `CurThing/twice`, `Lone/method1`, nothing for `Thing/hidden` under `private/`) off the folder
  without loading the class; a bare method name with a class folder's file - the loaded classes by
  name first, then the folders in search order - and every such file under `-all`; the dotted
  static form `Thing.staticOne` with the method's own file; and the argument form `which extent(t)`
  as `<file>  % Thing method`, the arguments evaluated in the caller's workspace and the dominant
  object's class answering. As a statement `which` prints what it found and binds no `ans`, as
  R2025b's does. `exist` is 2 for `Thing/extent` (plainly and as `'file'`), 0 for a bare method
  name and for `@Thing`. `what Thing` and `what @Thing` are the folder's row (its `.m` files by
  character code, `Thing.m` first - `what` lists every folder's files so now), `what('@Thing/private')`
  empty, and `what lib` lists the `@` folders under `classes`. `help Thing` is the folder listing
  ("Thing methods:", the class file as "Thing - is a class.", each file by its H1 line or as
  "Thing/name is a function." - "Thing.name" for a static - the names right-aligned, then the
  "Documentation for" lines); `help Thing/extent`, `help Thing/scale` and `help Thing.scale` the
  file's comment block or "Thing/extent is a function." with its header. `type Thing/extent` and
  `type @Thing/extent.m` print the file; `nargin('Thing/extent')` and `nargin('Thing/scale')` count
  the file's inputs, `nargin('Thing')` the constructor's, `nargin(@Thing.staticOne)` the static's,
  and `nargin('Thing.extent')` is `MATLAB:narginout:notValidMfile`.
- **The debugger's words.** `dbstop in Thing/extent` lands on the file's first line and `dbstatus`
  names it `Thing.extent`; `dbstop in Thing` and `Thing/Thing` land on the constructor's first line
  as `Thing.Thing>Thing.Thing`; `Thing/withlocal>helper` is `Thing.withlocal>helper`
  (`JgsSourceMap` prefixes a file in an `@` folder with its class); `Thing.extent` is `Cannot find
  function "Thing.extent".`, `Thing>extent` is `Cannot find function "extent" within "Thing".` and
  `Thing/withlocal>withlocal.helper` is refused `within "Thing/withlocal"`, the slash word as
  written; `pkg.D/dm` is refused (the slash forms stop at a package). `dbstack` prints a method
  file's frame as `In Thing/stk (line 2)` where the stack's own name for it, and the error stack's,
  is the bare `stk` (a classdef method's stays `Class.method`, P2); `mfilename` in `@Thing/info.m`
  is `info` with the folder's full path.
- **The listings.** `methods('Thing')` lists the public methods, files and classdef alike, with
  the statics; `methods -full` shows a declared file's signature as declared and an undeclared
  file's as `varargout name(varargin)`, as R2025b shows a signature it has not read; `methods` of
  a folder without its class file is `[]`; `ismethod`, `metaclass`, `?Thing` and `superclasses`
  see the class as any other.
- **Command words with parentheses.** `which scale(t, 1)` reaches `which` as one word: the
  parser's command syntax runs a word on over spaces and commas inside its parentheses (measured
  through R2025b's answer for the form).

## Consequences

- `JgsFileIndex.AnyClassFolders` is the one field a bare name's lookup reads before the class
  folders are asked; a session with no `@` folder anywhere pays nothing. `JgsCodeTree` answers
  `ClassFolder`, `ClassFolders`, `AllClassFolders` and `ClassFolderOf(file)`.
- A class with a folder costs the folder's and its files' write times once per statement in
  which it is mentioned (`JgsClassFolder.Changed`), the price of seeing a method file's edit at
  the class's next mention.
- A method file that does not parse, or is not a function file, is a method whose call raises the
  load error (R2025b's "Error: File:" form), so one bad file does not take the class down; a
  declared signature with no file is a method whose call is refused in JGraph's words. Neither
  case is measured.
- `ClassMethod` carries `Signature`, `File` and `Undeclared`; `JgsClass.DeclaresMethod` is false
  for a file method, which is what keeps its frame's name bare.
- `TryClassInFront` refuses an instance method reached through a plain class's name as it refused
  one through a packaged class's (P2): `Circle.area(c)` is `The class Circle has no Constant
  property or Static method named 'area'.` in the MATLAB dialect, where it used to construct an
  instance and read the method off it.
- `what` lists a folder's files by character code in every folder (it was case-blind), which is
  R2025b's order (`C.m, E.m, dup.m` in P1's probe; `Thing.m, extent.m` here).

## Measured

- **The fixture** `p4_classfolders` (167 rows from R2025b 25.2, two divergent): declared, undeclared,
  static and private method files, the folder's `private/`, a local function of a method file, a
  misnamed file, `size.m` and `plus.m`, `mfilename` and the stack from a method file, the refusals
  for a private method by every road, inheritance across folders, a handle class folder, the
  current folder's class folder, `+pkg/@D`, the class-folder-wins warning and `lastwarn`, `@Mis`
  and `@Lone`, `cd` into the folder, and `which` (bare, slash, dotted, `-all`, the argument form),
  `exist`, `what`, `help`, `type`, `nargin`, `dbstop`, `dbstatus`, `dbstack`, the error stack,
  `methods`, `methods -full`, `ismethod`, `metaclass`, `?Thing`, `superclasses`, and a method
  file written after the class was used (the two divergence rows).
- **Tests**: `ClassFolderTests` (4: an edited method file read at the class's next mention, a
  signature with no file refusing only when called, a method file that does not parse failing at
  its own call, a command word keeping its parentheses together); `MatlabClassdefTests` updated to
  R2025b's words for a misnamed class file; the parity fixture suite and the package, import,
  classdef, resolver, debugger and ownership suites pass on this source; the seven verify scripts
  unchanged; the harvest regenerated.

## Not measured

- **A method file edited after use**: JGraph reads it at the class's next mention; R2025b was
  probed for a new file only (it needs `clear classes`), and an edited one likely follows the same
  rule.
- **A signature with no file, a method file that does not parse or holds a script**: JGraph's
  refusals, at the call.
- **`which extent` with no class loaded** walks the folders in search order and by name within one;
  R2025b was measured with a class loaded (the loaded classes answer by name: `@Sub` with `Sub` and
  `Thing` loaded, `@Thing` with `Thing` alone - a rule "the class loaded last" would fit too).
- **`which -all extent`'s order** is pinned sorted; R2025b's research listing had `@Thing` before
  `@Sub` for folders neither loaded.
- **`help` of a class folder whose class file has an H1 comment** prints that line for the class
  entry; R2025b was measured without one.
- **`dbstatus` for a breakpoint under a class folder's `private/`** keeps the file's bare stem.

## Divergences

- **A method file added to a class folder after the class was used is seen at the class's next
  mention** (`late_before_rehash`, `late_after_rehash`): R2025b needs `clear classes`, before and
  after `rehash`.
