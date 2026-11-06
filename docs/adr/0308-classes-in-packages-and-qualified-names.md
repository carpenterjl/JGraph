# ADR 0308 — Classes in packages: the qualified name is the class's name, and the words over it

## Status

Accepted, 2026-11-05. Stage P2 of the packages and class folders plan
(`docs/plans/packages-and-class-folders-plan.md`, gitignored; W-P2, K-P1, open item 195), after P1
(ADR 0307). P3 (imports of packages) to P7 follow.

## Context

P1 made a `+pkg` folder's members answer to their dotted names, and a class file among them
constructed by `pkg.C(3)` - but the class was keyed by its bare name, so `class(o)` said `C`,
`isa(o, 'C')` was true, two packages' classes of one name were one class, `pkg.C.K` and
`pkg.C.stat(1)` built an instance to ask it, `superclasses('pkg.sub.K')` and `?pkg.C` could not
load the file, a saved object came back under the wrong name, and `help`, `which`, `exist`,
`nargin`, `dbstop` and `dbstack` had no word for a class in a package. R2025b's rules were measured
by the plan's research probes and pinned by the fixture `p2_classes` (174 lines recorded from
R2025b 25.2, two div rows).

## Decision

- **The qualified name is the class's name.** `JgsClass.Name` is the declared name under the
  package the file sits in (`+pkg/+sub/K.m` is `pkg.sub.K`), read off the file's path when the
  class is built; `ShortName` is the name after the last dot, what the object header shows and the
  constructor is called. The interpreter's class table is keyed by it, so `pkg.E` and a plain `E`
  beside it are two classes; `class`, `isa` (`'C'` is false), `whos`, `metaclass`, `?pkg.C`,
  `meta.class.fromName`, `superclasses`, cells, struct fields, `func2str(@pkg.C)` and MAT-files all
  carry it. Loading by a dotted name goes through the function path's dotted road, so a
  `classdef K < pkg.C` header, `m@pkg.C(o)`, `enumeration('pkg.E')`, a saved `pkg.C` and every
  name-as-text site reach the class on its first mention.
- **`pkg.C.member` is read on the class.** After P1's package reading, a dotted chain whose prefix is
  a class in a package is asked for a Constant, a Static, `empty` or an enumeration member; a
  member the class has not got is R2025b's "The class pkg.C has no Constant property or Static
  method named 'm'." - an instance method through the class name included. A static method
  mentioned bare is a call, through the class name or an instance (`pkg.C.stat` and `o.stat` are
  "Not enough input arguments", measured) - lifted from "called only when it takes no
  parameters", on both roads.
- **A packaged class does not see itself bare.** Its methods' scope holds its other methods and
  not the constructor, so `C(5)` inside `pkg.C` is "Undefined function 'C' for input arguments of
  type 'double'." (measured), and `pkg.mk` calling `C(1)` is undefined too; `pkg.C(5)` and
  (P3) `import pkg.C` are the roads in.
- **The frames.** The stack's own name for a method is `ShortName.method` (`W.err`, `W.stkn`,
  measured); `dbstack` prints an instance method as the qualified class with slashes for its dots
  (`pkg/W/stk`, `P2Plain/stk`) and a static method with dots (`pkg.W.sstk`, `P2Plain.sstk`);
  `dbstop in pkg.C` lands on the constructor's first line, or line 1 for a class that writes none,
  and `dbstatus` names it `C>C.C` (`K>K.K`); `pkg.C>m`, `pkg.C/m` and `pkg.C.m` are refused with
  R2025b's three messages (a plain `P2Plain/m` and `P2Plain>m` too - the slash forms are class
  folders', P4).
- **The tools.** `help pkg.C` is "pkg.C is a class." with the constructor's call form under the
  qualified name (`o = pkg.C(v)`, or `obj = pkg.sub.K` for a class writing none) and the
  "Documentation for" lines; `help pkg.C.m` and `pkg.C/m` are "pkg.C/m is a function." with the
  header as written; a static is "pkg.C.stat is a function." with `r = pkg.C.stat(x)`; the
  constructor "pkg.C/C is a constructor."; a plain class file's help takes the same form. `help pkg`
  lists a class folder as "pkg.D is a class." once any class of the package has been loaded and
  as "+pkg\@D is a folder." before (recorded both ways, `p1_packages` and `p2_classes`). `which
  pkg.C.stat` is the class's file; an instance method, a Constant and an enumeration member are
  ''. `exist('pkg.C')` is 8 asked plainly and as `'class'`, 0 as `'file'`. `nargin('pkg.C')` counts
  the constructor's parameters (every class constructor answered -1 before), `nargin('pkg.C.stat')`
  the static's; `pkg.C/m` and `pkg.C.m` are "Not a valid MATLAB file."
- **`meta.package.fromName` is a struct** (decision PD5): R2025b's property names - Name,
  Description (the name), DetailedDescription, ClassList (the package's class files and class
  folders, qualified), FunctionList (the code files that are not classes, scripts among them,
  measured), InnerNamespaces, OuterNamespace - with the legacy PackageList and ContainingPackage
  beside them, each list a struct column of Names, the parent the parent's struct or a 0-by-0, and a
  0-by-0 for a name no folder holds as a package. Nothing loads to answer it.
- **MAT-files.** The writer already wrote the class's name, now qualified; an enumeration member is
  saved by its name under a field of its own (`__enumerationMember`) and loaded as the class's
  member - before this a saved member of any enumeration came back as a default instance with no
  name. `-v7.3` of objects is still open item 131, so the fixture saves version 7 alone.

## Consequences

- Every site that keyed a class by its name reads the declared name where it meant the
  constructor (`_own`'s key, the implicit-constructor test, the inherited-constructor skip) and
  `Name` where it meant the class; a class this build supplies under a dotted name of its own
  (`matlab.ui.componentcontainer.ComponentContainer`) is not packaged, keeps its constructor under
  that whole name and its whole name in a stack frame - only a class file under a `+` folder takes
  the short stack name and loses its bare constructor.
- `TryClassInFront`'s new branch and `PackageClass` cost nothing while no search folder holds a
  package (`AnyPackages`).
- The parity harness echoes a scalar object, a cell and a struct in the console's shape, not
  R2025b's, and `disp` of a function handle likewise: the fixture pins the names through `disp`
  with the whitespace squashed, and leaves the echo forms to the console tests.

## Measured

- **The fixture** `p2_classes` (174 lines recorded from R2025b 25.2, two div rows): construction,
  constants, statics and their bare mentions, instance methods both ways and through the class
  name, `pkg.C.empty`, object arrays, enumerations and their listing, a handle class in a package,
  a plain `E` beside `pkg.E`, the names (`class`, `isa`, `superclasses`, `metaclass`, `?`,
  `fromName`, `whos`, `func2str`, `str2func`, `feval`, `nargin`), `methods` (list and printed),
  `properties`, `fieldnames`, `ismethod`, the displays by `disp`, `meta.package.fromName`, save and
  load in version 7, `help` of the class, its methods and its constructor, `which`, `exist`,
  `dbstop`/`dbstatus`, `dbstack` printed and as a struct, `MException.stack`, `clear pkg.C`, a
  class file written after first use and rewritten.
- **Tests**: `JgsPackageTests` (9: three new - a plain enumeration's member through save and load,
  `nargin` of a class constructor with and without one, the `meta.package` struct's lists and
  parent chain); the class, inheritance, enumeration, debugger, MAT-file, resolver, precedence,
  eval, package and every parity fixture suite pass on this source; the seven verify scripts
  unchanged; the harvest regenerated.

## Not measured

- **The echo of a packaged object** (`o = pkg.C(3)` at the prompt) is the console's, as for a
  plain object; the fixture pins the header through `disp`.
- **`nargin` of a class that writes no constructor** answers 0 here; R2025b was not probed.
- **`help pkg.C/stat`** (a static asked with a slash) prints the dotted form; only the dotted ask
  was recorded.
- **`Plain.nosuch`** for a class in no package keeps its earlier road (an instance built and asked);
  the packaged road refuses with the measured words.
- **`dbstack` for a nested function of a method** is unchanged.

## Divergences

- **`meta.package.fromName` answers a struct** where R2025b answers a `matlab.metadata.Namespace`
  whose `FunctionList` is `matlab.metadata.Method` objects (two rows stamped; open item 196).
- **A class folder inside a package** (`+pkg/@D`) is listed by `help` and `meta.package` and defines
  nothing until P4.
- **The harness's echo** of a scalar object, a cell, a struct and a function handle is the console's
  shape (not pinned by the fixture; open item 194 holds the MATLAB dialect's `disp` and echo).
