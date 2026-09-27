# ADR 0176 — .NET names are imported, and an assembly is added once

## Status

Accepted. Stage I3 of the .NET and shared-library interop plan
(`docs/plans/net-and-shared-library-interop-plan.md`), after stage I2 (ADR 0175, 36c1fb9). One
commit, this ADR's.

## Context

Stage I2 left two fixtures to this stage. `net_import` (35 lines, recorded in step 0) stopped at its
first helper: JGraph's parser read `import System.*` as an expression and refused the `.*` at the end
of the line. `net_assembly` ran to the end with 12 lines pending on I3: the runtime's status before
.NET loads, the assembly's class count, the identity of a second `NET.addAssembly`, `which` and
`exist` of the `NET` package's names, `NET.disableAutoRelease`, and whether .NET sees the folder `cd`
moved to.

ADR 0149 (M145) refused `import` by name, and the M145 plan's precedence table already held its two
layers: an explicit import after the variables, a wildcard after the local functions. A probe of
R2025b (`probe3`, scratch, not committed) settled what the fixtures leave open: 30 import forms
written as generated function files, `NET.addAssembly` by `System.Reflection.AssemblyName`, its empty
and wrong-arity refusals, the full `Classes` list, `which` and `exist` of every `NET.*` name, and the
`dotnetenv` fields before .NET loads.

## Decision

**`import` is a statement of its own.** In the MATLAB dialect, `import` followed by a spaced name is
an `ImportStmt` holding one or more dotted names, each ending in a name or in `.*` (the lexer's
elementwise-multiply token, glued to the last dot). `import` alone, `L = import` and
`import('System.Math')` are the `import` builtin.

**A function's imports are its whole body's.** R2025b resolves a function's imports when it parses
the file, so an import holds before its own line and inside a branch never taken (net_import `late`,
`branch`). JGraph collects a function's `import` statements when the function is first entered (its
blocks included, its nested functions not; cached on the declaration) and gives them to the call's
frame before the first line runs; the statement itself then does nothing. A nested function, an
anonymous function and `eval` run in scopes under that frame and see its imports; a function it
calls does not, because the lookup stops at the frame's call boundary (`callee`). The base workspace
and a script import as their statements run.

**Where an imported name sits.** The M145 table's two rows are now in the resolver
(`ResolutionLayer.ExplicitImport`, `WildcardImport`):

| Layer | Name | What answers |
|---|---|---|
| 1 | variable | the workspace walk |
| **2** | **explicit import** | `import System.Math.Max` → `Max`; `import System.Math` → `Math` |
| 3–4 | nested, local function | |
| **5** | **wildcard import** | `import System.*` → `String`, `Collections`; `import System.Math.*` → `Max` |
| 6… | private, user method, folders, built-in | as M145 |

Both are lexical: the answer is settled before the arguments are evaluated. A wildcard over a
namespace reaches its types, and its namespaces only in front of a dot: under `import JGTest.*`,
`plot` constructs `JGTest.plot` and `max.Thing()` reaches the namespace `JGTest.max`, while
`max([1 3])` is still MATLAB's `max`. A wildcard over a type reaches its static methods alone —
`PI` (a constant) and an enum's members (`import System.DayOfWeek.*`, then `Monday`) are not
reached, nor a generic type (`List`), which is what R2025b does (probe3). An explicit import anywhere
in reach beats every wildcard; between two wildcards the later wins (`conflict`); a variable beats a
wildcard (`Math = 3; import System.*` reads 3).

**The refusals R2025b makes at parse time are made on entry.** An explicit import that names no type
and no static method is `MATLAB:mir_illegal_import_argument` (`import System.NoSuchType`,
`import System.IO` — a namespace needs `.*` —, `import System.Math.PI`); a parameter, an output or an
assigned variable named like an explicit import is `MATLAB:lang:ImportedFunctionAndVariableHaveSameName`.
A wildcard is never refused: `import No.Such.*` is accepted and listed.

**The function form lists, and resolves only at the base workspace.** `L = import('System.Math')`
answers the list, the added names first (probe3), and in a function `Math.Max` after it is still
`MATLAB:undefinedVarOrClass` (`fncall`): R2025b resolved the function's names when it parsed them.
A non-text argument is `MATLAB:import:InvalidInputDataType`, a name neither wildcard nor fully
qualified `MATLAB:import:NonFullyQualifiedImportArgument`. `clear import` is allowed only at the
prompt (the app's console), and is R2025b's `MATLAB:cannotClear` in a function, a script and
`evalin('base', …)`.

**`which` names an imported method** (`Max is a built-in method`) and `exist` does not (0), as R2025b
answers.

**An assembly is added once per session.** `NET.addAssembly` answers the same `NET.Assembly` for an
assembly however it is named again — the path, the path as a string, the short name, a
`System.Reflection.AssemblyName` — so `==` holds (`reload_same`). The `AssemblyName` form loads through
the default context and fails with the loader's exception (`MATLAB:NET:CLRException:AddAssembly`);
an empty name is `MATLAB:NET:AddAssembly:EmptyAssemblyName`; any other argument count is R2025b's
"No method 'NET.addAssembly' with matching signature found." — `Unloadable=true` included, which
R2025b's `-batch` answers the same way, so there is no unloadable form to build. `Classes` lists the
top-level classes in the assembly's order and then the nested ones (`JGTest.Outer+Inner` last: 24).

**The runtime loads when the script reaches .NET.** `dotnetenv` answers `notloaded`, with an empty
version and location, until the session first reaches .NET (a .NET name, `NET.addAssembly`,
`NET.isNETSupported`, which R2025b loads to answer), and `loaded` after. A request for another runtime
is R2025b's `MATLAB:netenv:NETLoaded` once loaded; before, R2025b would switch at load and JGraph
cannot, so it refuses under its own `JGraph:netenv:UnsupportedRuntime`.

**The `NET` package answers `which` and `exist`.** `which('NET.addAssembly')` and every `NET.*`
function JGraph has is "`name` is a built-in method" (R2025b), a static method named in full is
"`Max` is a built-in method", and
`exist('NET.NetException')` and `exist('NET.Assembly')` are 8.

**`NET.disableAutoRelease` and `NET.enableAutoRelease`** lock a COM object's wrapper in R2025b. JGraph
holds every .NET object it hands out until the last holder goes, so a COM object needs nothing;
anything else is R2025b's refusal: `MATLAB:badargs` for a .NET or other object, `MATLAB:class:RequireClass`
for a value that is no handle.

**A .NET call sees the folder `cd` moved to.** R2025b's `cd` is the process's. JGraph's `cd` moves the
session's folder, so before a .NET member runs (a constructor, a member read, a call), the process's
working folder is moved to the session's when the session has one of its own and it differs from
where the process was last moved; it is left there, as MATLAB leaves it. An unmoved call costs one
string compare. `cd` now drops a trailing separator as R2025b's does (`cd(tempdir)`, then `pwd`, has
none); only a drive's root keeps one.

**`feval` reaches a built-in package's function by name** — `feval('NET.addAssembly', …)`,
`feval('containers.Map')` — which is the JGS dialect's only road to one (net_jgs_smoke).

## Measured

Release CLI, warm, 20,000 calls per row, the best of three runs: `System.Math.Max(3, 4)` 8.1 µs,
`Max(3, 4)` under `import System.Math.*` 9.4 µs, an instance call 1.8 µs, a static property read
0.8 µs — stage 2's figures within noise, so the folder check and the import lookup cost nothing
visible.

## What moves

- `net_import` agrees on all 35 lines (it stopped at its first); `net_assembly` on all 48, one of them
  a divergence (`env_before_runtime`). No line is pending on I3; the ratchet's pending lines are
  I4 8, I5 2, I8 1, I9 3.
- `net_jgs_smoke` gains two rows: a second `NET.addAssembly` through `feval` is the same handle, and
  `import()` lists nothing in JGS, which has no import statement.
- Coverage: 427 of 514 builtins and 1,137 of 2,024 callables (`import`, `NET.disableAutoRelease`,
  `NET.enableAutoRelease`); the interop section of the builtin coverage document is 24.
- Tests: `NetInteropM176Tests` (16).

## Consequences

- A MATLAB script can `import` .NET namespaces, types and static methods with MATLAB's scope and
  precedence, and names an assembly as often as it likes. Stage I4 adds arrays, generics, enums and
  dictionaries.
- The resolver has two layers more; both are asked only after the session has imported something
  (`Interpreter.AnyImports`), so a script that never imports pays one flag read per name that nothing
  else answers.
- The process's working folder now moves with a script that calls .NET after a `cd`. Code in the app
  that reads the process folder (`Environment.CurrentDirectory`) sees it.

## Divergences

- **`dotnetenv` answers `core` before a runtime is chosen.** R2025b on Windows defaults to .NET
  Framework (`framework`); JGraph runs on .NET 8 and cannot host Framework (net_assembly
  `env_before_runtime`, `div=ADR0176`).
- **A request for another runtime before .NET loads is refused** (`JGraph:netenv:UnsupportedRuntime`);
  R2025b accepts it and loads that runtime later.
- **A refusal R2025b makes when it parses a file comes when the function is entered.** Another function
  of the same file still runs, and the message does not lead with R2025b's `Error: File: … Line: …
  Column: …`.
- **A script's imports stay in the workspace it ran in.** R2025b scopes them to the script's code, so
  after a script ran `import System.Math.*`, `import` in its caller lists nothing (probe3); in JGraph
  the caller's list holds it.
- **A MATLAB package import reaches nothing.** `import pkg.*` is accepted, as R2025b accepts a wildcard
  it cannot find, and `import pkg.fn` is refused; JGraph has no packages (ADR 0149).
- **`which('NET.isNETSupported')` says it is a built-in method.** R2025b answers the path of its
  `isNETSupported.m`; JGraph has no file to name.
- **The process follows `cd` at the next .NET call, not at the `cd`.** Code .NET runs on another
  thread in between sees the folder the process was in.
