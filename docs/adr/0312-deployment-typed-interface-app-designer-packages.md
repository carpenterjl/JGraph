# ADR 0312 — Deployment, the typed interface and App Designer over packages and class folders

## Status

Accepted, 2026-11-06. Stage P6 of the packages and class folders plan
(`docs/plans/packages-and-class-folders-plan.md`, gitignored; W-P2, K-P1, open item 195; decision
PD4 and the last item of open item 163), after P5 (ADR 0311). P7 (the guide, acceptance, the gate)
follows.

## Context

P1 to P5 taught the interpreter and the editors' tools `+package` and `@Class` folders; the compiler
and App Designer had not caught up. The dependency analysis said in its own words that packages,
class folders and imports were not followed, so `mcc` of a program calling `pkg.f` or constructing
`Thing` built an archive without them and the program failed where it ran; K9's strongly typed
package wrapper (`k9pkg_k9inner.cs`, generated rightly since K9) called a name the deployed session
could not resolve (`call.pkg`, `typed.pkg` in `k9_differences.txt`); a C shared library of a package
function named its wrappers after the file alone; a main inside a package was named by its stem; and
App Designer's Run of an app saved under `+pkg` ran the bare name from the file's own folder, where
R2025b runs the qualified name from the folder above the packages. The rules were measured by four
probes (`tools/matlab-checklist/ui-probes/p6`: `p6_depend` with its `mcc` part, `p6_libs`, `p6_app`,
`p6_which`) and pinned by the fixture `p6_deploy` (38 rows recorded from R2025b 25.2, none
divergent), the acceptance set's new program and the tests below.

## Decision

- **The dependency analysis follows a dotted name to its member and takes a class folder whole.**
  `JgsDependencyAnalyzer` reads a chain of literal names on a bare root (`pkg.f(2)`, `pkg.sub.h`,
  `pkg.C.K`, `pkg.s`, `@pkg.f`, `?pkg.C`, a dotted superclass, a dotted `arguments` class,
  `%#function pkg.f`) as the member the longest chain reaches in a `+package` folder of the folders
  names are looked for in - the current folder, `-I`, the path - and takes that file alone: a package
  is never taken whole, and `pkg.C.K` is `+pkg/C.m` once `+pkg/+C/K.m` and `+pkg/+C/@K` are not there.
  A root that is a variable or one of the file's own functions names nothing (a struct `pkg`), and a
  name only in a string (`feval('pkg.f')`) is not seen, as before. A class defined by an `@Class`
  folder is taken whole - its `private/` and its unused method files too - whether reached by its
  name, by a static through its name (`Thing.staticOne`), by a handle (`@Thing.staticOne`), by
  `?Thing`, by a subclass (`@Sub` brings `@Thing`), by `%#function Thing`, by a dotted chain
  (`pkg.D` for `+pkg/@D`) or as a root (the class file or a method file of the folder); its method
  files are analysed as any taken code is, so `@Thing/usesfar.m` brings `lib/farhelper.m`. A bare
  method name reaches nothing on its own (`p6_which`: `extent(t)` with four folders holding
  `extent.m` took none; the handle case `@extent` took `@Thing` for the `Thing(2)` beside it).
  `import pkg.f` gives the file's bare `f` that member, above the file's own functions (P3's
  measured order); `import pkg.*` and `import pkg.sub.*` give a bare name the package's member of
  that name, above the private folder and the path; the imports are read with the file's functions,
  wherever in the file they stand. A package member's own `private/` is reached by the rule that
  was there (`+pkg/q.m` brings `+pkg/private/p.m`). `-N`/`toponly` takes the folder's files with
  the root and analyses none of them, as measured. `requiredFilesAndProducts` takes `pkg.f` and
  `pkg.C` as roots (the member's file) and `Thing/extent` (the method file, and with it the folder).
- **The archive keeps the folder layout and the path never holds them.** `CtfLayout` places
  `+pkg/f.m` and `@Thing/private/hidden.m` under their folder's path as any file; the path entry a
  file gives is its folder up to the first `private`, `@`, `+` or dot folder (`lib` for
  `lib/+pkg/f.m`, measured in the manifest's `directory` elements; `papp` for a main under
  `work/+pkg`), so a build of `+pkg/g2.m` alone still puts `lib` on the path. A main inside a
  package is named by its qualified name (`pkg.g2`, `pkg.papp`; the manifest's `FUNCTION_pkg_g2`,
  measured) while the program and component keep the stem; the deployed session runs the main by
  its file, so the name is the manifest's alone. The deployed session's frozen path is read through
  the same index as a live one's, so `pkg.f` and `Thing` resolve in a program built from them
  (`p6_depend`: `run.usepkg` printed `f 20`, `extent 9`, `scale 7`; `StandaloneBuildTests` runs the
  same program under JGraph).
- **The typed interface's package wrapper calls through.** Nothing in the generator changed: K9
  wrote `k9pkg_k9inner.cs` as R2025b writes it. The .NET provider's name check already took a
  dotted public name; the call reaches the session's `feval` with `k9pkg.k9inner`, which P1 taught
  to resolve a dotted name, and the archive's `+k9pkg` folder is listed by the frozen path's index.
  `DotNetDataApiTests` now calls `matlab.k9pkg.k9inner(1.0)` dynamically and through
  `K9.k9pkg.MATLABFunctions.k9inner` (101, as `call.pkg.double` and `typed.pkg` recorded) and reads
  R2025b's refusal of `k9pkg.nosuch`. The C API and the Python package go through the same name
  check (`Component.Functions` holds `pkg.f`) and the same call; a C shared library of `+pkg/f.m`
  names its wrappers `mlxPkg_f` and `mlfPkg_f` and calls `mclFeval(_mcr_inst, "pkg.f", …)`, as
  R2025b's generated text does (`p6_libs`); the exports list follows. The Production Server archive
  and the .NET archive list `pkg.f` and `pkg.g2` as their functions and `pkg_f.cs` as the typed file,
  as measured.
- **An app in a `+package` or `@Class` folder runs by its qualified name from the folder above.**
  `DesignRun.CommandOf` is R2025b's `PathUtil.getAppRunCommandFromFileName` and `getPathToApp`,
  measured over every path their comment lists and over `.mlapp` paths: a file whose own folder is
  not a `+` or `@` folder runs by its name from that folder (`+foo\sub\bar.mlapp` is `bar` from
  `+foo\sub`, `+foo\private\bar.mlapp` is `bar`); otherwise the path is split at its first `\+`
  (or, with none, its first `\@`), the rest is read with dots, `@foo.foo` becomes `foo`, every
  other `@` and every `+` is dropped, and the part before the split is the folder: `+foo\@bar\bar.m`
  is `foo.bar`, `@bar\method.m` is `bar.method`, `+foo\+bar\@baz\baz.mlapp` is `foo.bar.baz`,
  `@foo\other.mlapp` is `foo.other`, and the degenerate `+\bar.mlapp` and `+a+b\c.mlapp` are
  `.bar` and `ab.c` as R2025b reads them. The shadow check resolves a dotted command through the
  function path's dotted road; the same-name and startup-argument rules read the packaged class by
  its qualified and stack names (P2's forms). An `.mlapp` is a package member: the file index lists
  `.mlapp` files beside `.m` and `.p` in every folder it reads, so `which('pkg.papp')` names the
  file and `exist` answers 8, as measured. Save As into such a folder needs nothing: the class's
  name in the file is the stem, as R2025b writes it.

## Consequences

- A program, a library or an archive built by `mcc` or `compiler.build` from code that uses
  packages and class folders carries them and runs them; the acceptance set gained a console
  program over `+acc/fold.m` and `@AccSeries` (its declared method file and its `private/weight.m`)
  built and run by both engines (`k22_accept.out.txt`: `pkg.factor`, `pkg.none`).
- `requiredFilesAndProducts`, `mcc`'s logs and the compiler apps' analysis list package members
  and class folders by their files.
- App Designer runs an app saved under `+pkg` as `pkg.App` with the folder above on the path, and
  refuses as before when something outranks it; the window check `run-p6designer.ps1` opens one.
- The file index listing `.mlapp` stems means an `.mlapp` beside a `.m` of one name is seen by the
  index's shadowing rule as the plain road already ordered them (an `.mlapp` before a `.p` before
  the `.m`).

## Measured

- `tools/matlab-checklist/ui-probes/p6/p6_depend.m` (with `p6_layout.m`, `p6_depend_mcc.m`):
  `requiredFilesAndProducts` over twenty-nine roots, the member files and folders as roots, the
  dotted and slash root forms, `toponly` and no library; `mcc -m usepkg.m` (the archive's entries,
  the manifest's folders and functions, the program run) and `mcc -m +pkg/g2.m` (`pkg.g2`).
- `p6_libs.m`: `mcc -W lib`, `-W python`, `-W CTF` and `-W dotnet,api=matlab-data` over
  `+pkg/f.m` and `+pkg/g2.m`, and `-W lib` over `@Thing/staticOne.m`.
- `p6_app.m`: `PathUtil` over eighteen paths; an `.mlapp` written through App Designer's serializer
  into `+pkg` run as `pkg.papp` headless, `which`, `exist`, `?pkg.papp`,
  `requiredFilesAndProducts` of its caller, and `mcc -m` of it.
- `p6_which.m`: a bare method name with four folders holding it.
- The fixture `p6_deploy` (38 rows, recorded headless), `PackageDependencyTests` (29 roots and 5
  facts), `CtfArchiveTests` (+1), `CSharedLibraryTests` (+1), `AppDesignerRunArgumentsTests`
  (+17 rows), `AppDesignerRunTests` (+1), `DotNetDataApiTests` (the package calls),
  `StandaloneBuildTests` (+1), the acceptance set's `build.pkg`, `pkg.factor` and `pkg.none` in
  both engines.

## Not measured

- A C shared library's or a Python package's package function called end to end by a client (the
  name check and the call are the .NET Data API's, which is measured).
- An `.mlapp` saved in an `@Class` folder run by `Class.method` (the command is R2025b's; no such
  app was made).
- `mcc` of a program whose main is a method file of a class folder.
- The window check itself was run (its checks pass); App Designer's Save As into a package folder
  through the dialog was not driven.

## Divergences

- A method file of an `@Class` folder given as a root: R2025b took the folder's files and left out
  what a method file calls (`rfp.methodfile`: no `farhelper.m`); JGraph analyses the folder's files
  as any taken code, so the helper comes too. Recorded; a build that leaves a called file out fails
  to run.
- `requiredFilesAndProducts('Thing/extent')`: R2025b answers the class file alone; JGraph the
  method file and, with it, the folder. Recorded.
