# ADR 0313 — The guide's packages chapter, the acceptance library, and the gate

## Status

Accepted, 2026-11-06. Stage P7, the last, of the packages and class folders plan
(`docs/plans/packages-and-class-folders-plan.md`, gitignored; the App Designer window plan's W-P2,
the compiler plan's K-P1, open item 195). It closes the arc ADRs 0307–0312: packages, dotted names
and scripts in packages (P1), classes in packages under their qualified names (P2), imports of
packages in the one list with the .NET imports (P3), `@Class` folders as one class (P4), the
editors' tools over the folders (P5), and deployment, the typed interface and App Designer (P6).

## Context

Each stage measured its own feature against R2025b through a probe and a parity fixture. What the
plan left for P7 was the whole road at once - a library laid out as a user lays one out, used by a
script through its imports, built into a program and opened as a project, in both engines - the
guide chapter that tells a user what the folders do here, the coverage doc's rows, and the gate.

## Decision

### The guide

`docs/jgs-scripting-guide.html` gains **Packages and class folders** under Running MATLAB (.m)
files: the `+package` folder and its members by dotted name, a member naming another in full, a
package split over path folders, `private/` inside a package, scripts in packages, the imports and
R2025b's measured order (first explicit, earliest package wildcard, package over .NET, an explicit
import above the own functions and a wildcard below them, text and handle resolution giving
`private/` the first word, a script's imports read from its whole file, the three refusals),
classes in packages by their qualified names, class folders with their method files, signature
lines, undeclared files and `private/`, the class-folder-wins rule and its warning, the tools in
R2025b's forms, completion and the Code Analyzer, deployment and App Designer, an example over the
acceptance library, and *Where JGraph differs*. The .NET chapter's imports line now points at the
one list, and the deployment chapter's stale "bound by the same limits" line is gone.
`docs/matlab-builtin-coverage.md` gains the row "Packages and class folders" naming the builtins
that learned the folders without moving the count.

### The acceptance library

`tools/matlab-checklist/ui-probes/p7/lib`, laid out as a user would:

- `+signal/+io/readTrace.m` reads a trace file into a `signal.Trace`, naming the class in full and
  taking its numbers from `+signal/+io/private/parseNumbers.m`; `+signal/Trace.m` is a handle class
  with a dependent property, an instance method, `describe` and a static `fromFile` that calls the
  sub-package in full; `+signal/summarize.m` is a script that reads `t` from the workspace it was
  run in.
- `@Series/Series.m` declares `total` and `smooth` by signature lines and a static `ofTrace`;
  `total.m` and `smooth.m` are their files, `describe.m` is declared nowhere and is public for
  it, and `private/weight.m` is reached by the method files alone.
- `p7_main.m`, the top-level script, imports `signal.Trace` and `signal.io.*` and uses the two
  classes, the handle's mutation, the script in the package, dispatch of `describe` to each class,
  `isa` by the qualified and short names, and a handle to the sub-package's member.

`accept-p7.ps1` takes the library the whole way in either engine (`-Engine R2025b`, or
`-Engine JGraph -Runtime <runtime>` for a runtime laid out by `installer\publish-runtime.ps1`):

- `p7_accept.m` (`run`): the script, then `which`, `exist`, `methods`, `superclasses`, `what`,
  `nargin` and the refusals of a private function from outside, a package's private function from
  another folder, and a member by its bare name.
- `p7_build.m` (`build`): `mcc -m p7_tool.m` with the library on the path, the deliverables, the
  archive's entries and the manifest's folders, functions and main; then the program run through
  `run-quiet.ps1` with the trace file and a factor (`tool`).
- `p7_project.m` (`project`): a project created, the library copied in and added with
  `addFolderIncludingChildFiles`, the scripts with `addFile`, the files listed, `isFileInProject`,
  the library on the project path and the classes resolving, the project closed and opened again,
  and the library run from the reopened project.
- `compare-p7.py` holds the four recordings to each other (`p7_differences.txt`).

`PackageAcceptanceTests` runs the same library in process: the script against R2025b's recorded
lines, the project against its recording, `mcc` and the program under the deployed session against
R2025b's build and program lines, and the editor's completion through the session's completion
source - the package's members after `signal.`, the sub-package's after `signal.io.`, the statics
after `signal.Trace.` and `Series.`, an object's members in the class file and in a method file of
the folder, `private/weight.m` offered nowhere.

### The gate

The full suite ran once; the seven `verify-*.py` scripts pass (the builtin coverage unchanged at
1,276 of 2,024, since the stage adds no builtin); the divergence harvest is regenerated; the
READMEs carry the stage (`ui-probes/README.md` rows for `p5/`, `p6/` and `p7/`); open item 195 is
closed, 163's last item was closed by P6 and 244 by P1; the workbook marks every stage done.

### What the acceptance showed

- **Both engines print the same 29 lines for the library**: the trace read through the wildcard
  import, scaled through the handle, the explicit import's class as `signal.Trace`, the static
  through the sub-package, the script in the package reading the caller's `t`, the weighted total
  through `private/weight.m`, `smooth`, `describe` dispatched to each class, `isa` four ways, the
  handle's name and its result, `which` for six forms, `exist` (8 for the packaged class, 2 for the
  class folder, 0 for a package function and a package script), `methods` of the class folder with
  its undeclared file, `superclasses`, `what` of the package folder by kind, the three refusals
  in R2025b's words and identifier, and `nargin` three ways.
- **Both build the same program**: the archive holds the library's eight files under the same
  paths (the sub-package's `private/` and the class folder's `private/` among them), the manifest
  lists `p7_tool` and its main; the program prints the same seven lines, `isdeployed` 1 and `which`
  naming the class file and the method file from the frozen path.
- **Both open the same project**: `blank_project`, the folder added as one `ProjectFile`, 17 files
  in the same order (the `+`, `@` and `private` folders as entries), `isFileInProject` three ways,
  one path folder, the classes resolving while the project is open, 17 files and one path folder
  after reopening, and the library running from the reopened project.
- **Two things measured on the way**, both engines agreeing: a `private/` folder inside a package
  serves the files beside it alone, so `+signal/private/` is not seen from `+signal/+io/` (the
  library was laid out wrongly first, and both engines refused it the same way); and
  `matlab.project.isFileInProject` takes the file alone, no project.
- **One difference**: after `close(proj)` takes the library off the path, R2025b still answers
  `exist('Series')` 2 for the class folder it had seen, after `rehash` too, while
  `exist('signal.Trace')` drops to 0; JGraph forgets the class folder with the path entry. The
  mirror of P4's "a new method file needs `clear classes`": R2025b's class-folder cache. Recorded.

## Consequences

- The packages and class folders plan is complete: `+package` and `@Class` folders stand in JGraph
  as R2025b reads them, from a dotted name at the prompt through the imports, the class system,
  the editors' tools, the compiler and App Designer, every rule measured by running R2025b and
  pinned by the fixtures `p1_packages` to `p6_deploy`; W-P2 and K-P1 are closed with item 195.
- The acceptance library is the plan's whole-road measure: `accept-p7.ps1` runs it again in either
  engine when a stage changes name resolution, the archive or the projects, and
  `PackageAcceptanceTests` runs it in every test run against R2025b's recordings.
- The guide tells a user what the folders do and where JGraph differs.

## Measured

- **R2025b headless** (`p7_accept.out`, `p7_build.out`, `p7_tool.out`, `p7_project.out`) against
  JGraph's (`.jgraph.out` beside them, from the runtime published at c815b5b8): 93 keys, 69 agree,
  24 recorded divergences (the archive's own entries and the manifest's folders, the runner's exit
  line, the class-folder cache rows), 0 unexplained.
- **Tests**: `PackageAcceptanceTests` (4: the script's 29 lines, the project, the build and the
  program, completion). The full suite once: 13,257 tests, 13,256 passing; the one failure was
  `AudioTests.TheSimulatedOutputPlaysInRealTimeAndKeepsWhatItPlayed`, whose five-second wait for
  the simulated output's completion ran out under the loaded suite - older than the stage, which
  touches no engine code - and its class passes alone (4 of 4).
- **The verify scripts**: builtin 450 of 514 and 1,276 of 2,024, IPT 270 of 409, method classes
  2,490 measured, signal 269 of 351, stats 385 of 589, target forms 101 of 101, toolbox 348 of 377.

## Not measured

- The Script Workspace's own opening of a project (its window); the project was opened through
  the session's `openProject`, which the window calls.
- The editor's completion in R2025b, which has no headless form; the completion test pins JGraph's
  reading alone, as P5's did.
- `clear classes` after `close(proj)` in R2025b (whether it drops the cached class folder); a
  function that keeps results cannot call it.

## Divergences

- **A class folder of a folder taken off the path** stays resolvable in R2025b (`exist` 2 after
  `close(proj)` and after `rehash`); JGraph forgets it with the path entry. Recorded.
- **The archive's own entries**: R2025b's `.META` files and `toolbox/compiler` and `toolbox/local`
  patches with their manifest folders are the MATLAB Runtime's; JGraph's archive holds the
  program's files (ADR 0258). The library's files are the same in both.
