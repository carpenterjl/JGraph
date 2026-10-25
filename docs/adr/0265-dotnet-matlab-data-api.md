# ADR 0265 — .NET assemblies for the MATLAB Data API

## Status

Accepted, 2026-10-24. Compiler and SDK plan, stage K9 (decisions KD3 and KO4).

## Context

R2025b's `mcc -W dotnet:<name>,api=matlab-data` and `compiler.build.dotNETAssembly(..., 'Interface',
'matlab-data')` build an archive a C# program opens with `MathWorks.MATLAB.Runtime.MATLABRuntime` and
calls through a `dynamic` instance, and a strongly typed interface (C# files and a small assembly) that
wraps each function and class with typed overloads. The client references MathWorks'
`MathWorks.MATLAB.Runtime.dll` and `MathWorks.MATLAB.Types.dll`. K8 refused `api=matlab-data` as a
target K9 builds. The interface is generated from each function's `arguments` blocks, which JGraph
parsed as positional lines only, and from enumeration classes, which JGraph did not have.

The user's decisions carry over from K8: MathWorks' names under JGraph's namespaces (KD3), .NET 8
clients only (KO4).

What R2025b does was measured first by `record-k9.ps1` (`tools/matlab-checklist/compiler-probes`):

- **`probe_k9_build` (`k9_build`)**: the main build of 25 files (functions of every argument kind, a
  value class, a handle class, an enumeration, a package function), a variadic build, 14 variant builds
  (defaults, varargin, varargout, mixed, a dotted name, a class or an enumeration alone, a package,
  version, embedding, framework, debug, untyped, an optional method), 8 mcc forms, `meta.class`'s member
  order, and the options' Data API checks. The main build's text is kept in `recordings/k9_out`.
- **`k9client` (`k9_client`)**, a C# program of about 4,400 cases against R2025b's runtime and the
  main build: the API reflected, every inbound .NET kind, about 50 cast targets over 75 MATLAB kinds,
  implicit conversions and `IConvertible`, structs, objects, handles, enumerations, calls with named
  arguments, errors and their reports, the output writers, async calls and cancellation, a second
  archive, the typed interface (`TypedCases.cs`), and the failed starts.
- **Three parity fixtures** recorded in R2025b for the language the interface reads:
  `k9_arguments_blocks`, `k9_argument_messages` and `k9_enumerations`.

They found:

- **The interface** is R2025b's codegen (`toolbox/matlab/external/engines/codegen/csharp`, readable):
  per function a static partial `MATLABFunctions` class with an overload for each count of optional
  inputs dropped and each count of outputs; per class a `struct` (value class) or `class` (handle class)
  over the MATLAB object with its properties through `matlab.internal.engine.getProperty`/`setProperty`,
  its methods in `meta.class` order (each block's last first), `lt`/`le`/`gt`/`ge` as operators,
  handle's `eq ne lt gt le ge delete isvalid`, `IEquatable`, and casts to and from `MATLABObject`; per
  enumeration a C# `enum`. Types come from a table of class and size (`double (1,1) {mustBeReal}` is
  `double`, without `mustBeReal` `System.Numerics.Complex`, `(1,:)` a rank-1 array, no size `dynamic`);
  an output named as an input of its type is `ref`, of another type renamed `_x`.
- **No interface** when any function takes varargin or gives varargout: the archive alone and a
  note. `STRONGLY_TYPED_DISABLED=1` skips it silently. The archive is beside the assembly whatever
  `EmbedArchive` says, and mcc writes no `-C`.
- **The runtime's rules**: the conversions of every MATLAB kind to each .NET target (own type and C#
  widenings for scalars, the exact element type for arrays, ranks padded or trimmed, R2025b's
  messages for each refusal), cellstr to `string[]`, dictionaries to `Dictionary`/`IDictionary`,
  enumerations by name; inbound .NET values by kind; only the component's own functions callable;
  named arguments passed as name-value pairs; errors as `MATLABExecutionException` with the stack
  and the report on the call's error writer; tuples from several outputs; cancellation; disposal.
- **The language**: name-value arguments (`opts.Name`, case-insensitive, an unambiguous prefix, the
  last duplicate winning, the structure's fields in declared order, `nargin` the positional count),
  `(Repeating)` and `(Output)` blocks, R2025b's validation messages (the size words, scalar expansion,
  conversion by assignment rules, `Invalid default value ...`), and enumerations (members, text,
  comparisons, `enumeration`, the display, `switch`, conversion from text in an arguments line).
- **Where R2025b is wrong**: its generator passes name-value arguments by position, which every such
  function refuses, writes `bump(new RunOptions(nargout:0),)` for a method whose one input is optional
  (the assembly then does not compile and is not made), calls static methods and keyHash its runtime
  refuses, and skips a static method's first input; its runtime crashes on a disposed array, on null
  writers and when a failed start is finalized, cannot pass an array of C# enum members, accepts the
  field name `1x`, refuses an empty `MATLABStruct[]`, splits a line written to the output writer at each
  format argument, and prints an enumeration error from `clear` when the runtime ends.

## Decision

- **`src/JGraph.MATLAB.Runtime`**, the MATLAB Data API under `JGraph.MATLAB.Runtime`,
  `JGraph.MATLAB.Types` and `JGraph.MATLAB.Exceptions`: `MATLABRuntime` (`StartMATLAB`,
  `StartMATLABAsync`, `TerminateApplication`, `WaitForFiguresToClose`), `MATLABProvider` as a
  `DynamicObject`, `RunOptions`, `MATLABArray`, `MATLABStruct`, `MATLABObject`, the four exceptions, the
  attributes, `MATLABWorkspace`, and `Conversion` with every rule `k9_client` measured. It runs on a
  `JGraph.Runtime` component, finds the engine as K8's arrays assembly does, and is published alone in
  the runtime's `extern\dotnet\net8.0` as R2025b keeps its two in `extern\dotnet\netstandard2.0`.
- **`JGraph.Deploy.TypedInterface`**: R2025b's generator transcribed, with JGraph's header and
  namespaces and its two defects corrected (name-value arguments passed by name; no dangling comma).
  `TypedInterfaceReader` (in the scripting assembly) reads the model from the files: inputs and outputs
  from headers and `arguments` blocks, properties, the constructor and the methods in `meta.class` order,
  enumeration members, package names from `+folders`.
- **`JGraph.Deploy.DotNetDataAssembly`** writes the archive, the interface's files, the `.csproj` and
  `.deps.json` in their .NET 8 form, the assembly compiled with Roslyn (K8's compiler, now shared), and
  the readme; the note when no interface or no assembly can be made.
- **mcc** generates the interface before the analysis (so `-v`'s count line comes first, as R2025b's),
  makes every file's function or class public by its package-qualified name, and builds with
  `DotNetDataAssembly`. **`compiler.build.dotNETAssembly`** builds it with `Interface='matlab-data'`,
  writes no `-C`, and adds the Data API's getting-started page.
- **The language**: `arguments` blocks with name-value lines, `(Repeating)`, `(Input)` and `(Output)`
  (`ArgumentsSignature`, `ArgumentBinder`, `DeclaredArgument`), bound inside the function's frame so a
  refusal is the function's own at its declaration line; `namedargs2cell`; `nargin('f')` counting
  declared inputs; enumeration classes (`JgsBuiltins.Enumerations`).
- **Met on the way:**
  - **`switch` on a string scalar** matches a char case, as `strcmp` does (it never matched).
  - **`isobject`** is true for a string, table, timetable, datetime, duration and calendarDuration.
  - **`isscalar`** of an object array or a table is asked by its size.
  - **A deleted handle's property** is refused on `get`, `set` and a client's access, not only by a dot.
  - **`keyHash`** and **`configureDictionary`** are builtins.
  - **A method's frame is shown as `Class/method`** in a deployed report, as R2025b's.
  - `mustBeA`'s message, `char` of a missing string refused, `mat2str(char(0))`.

## Measured

- **`run-k9.ps1`** runs `probe_k9_build` unchanged under `jgraph -batch` (the meta loop now guarded,
  which leaves R2025b's output as it was) and `k9client` with its `using` lines changed against JGraph's
  build; `compare-k9.py` writes `recordings-jgraph/k9_differences.txt`.
  - The interface: 24 of 25 files are R2025b's text but for the header and namespaces; `k9nv.cs`
    passes its name-value arguments by name.
  - The client: 4,144 of 4,379 cases agree; every other one is a divergence below or an open item.
  - The builds: 152 of 174 keys agree; the rest are JGraph's readme, project files and `-v` line,
    `meta.class` (open item 196) and the optional method's build that R2025b's could not compile.
  - What the client prints and writes to its error writer differs only as the divergences below say.
  - R2025b's main build took 13.4 s, JGraph's 1.2 s; the client's run took 11.6 s on R2025b's runtime
    and 4.0 s on JGraph's.
- **The fixture `k9_dotnet_data_api`** has 33 lines from R2025b: compiler.build and mcc builds, the
  files and the interface's text, the notes, `STRONGLY_TYPED_DISABLED`, `-v`'s count, and the language
  fixes. 30 are exact; 3 are divergences. **`k9_arguments_blocks`** (98 lines), **`k9_argument_messages`**
  (460, 2 divergences) and **`k9_enumerations`** (64, 3 divergences) gate the language.
  `k8_dotnet_assembly`'s `mcc_mda` row, a divergence until now, is exact.
- **Tests.** `DotNetDataApiTests` (4: the interface against `k9_out`, the optional method, no
  interface for varargin, and the client's landmarks in process). `MccSwitchesTests`,
  `MatlabClassdefTests`, `MatlabFunctionsFilesAndErrorsTests` (a name-value argument now taken) and
  `MatlabStringsM104Tests` updated; `OwnershipGateTests`' allow-list takes K9's bindings and the
  compiler options' writes K6 and K8 had left out of it. The Scripting, MatlabParity, Deploy and
  Runtime areas: 8,971 of 8,982 before those updates, every failure but five of them; the five,
  `MatlabM79TablePiePropertyTests`' table forms of `scatter`, fail at HEAD too (open item 198).

## Divergences

- **The MATLAB Data API is `JGraph.MATLAB.Runtime.dll` under JGraph's namespaces** (decision KD3), for .NET 8 clients (decision KO4), in the runtime's `extern\dotnet\net8.0`.
- **The interface's header names JGraph's version**, its `using` lines JGraph's namespaces, and the `.csproj` and `.deps.json` target net8.0 and reference `JGraph.MATLAB.Runtime.dll`.
- **The interface is compiled in process with Roslyn**, so a build prints no msbuild summary and needs no .NET SDK.
- **readme.txt and GettingStarted.html are in JGraph's words.**
- **The interface passes name-value arguments by name** (`"Scale",Scale`); R2025b's passes them by position, which the function refuses.
- **A method whose only input besides the object is optional compiles**; R2025b's interface does not, and its build makes no assembly.
- **A static method and an enumeration member are callable as `Class.member`**, which R2025b's interface calls and its runtime refuses.
- **`keyHash` is callable, so the interface's `GetHashCode` works**; R2025b's runtime refuses the name. JGraph's hash is its own, in 53 bits.
- **An array of C# enum members passes as an enumeration array**; R2025b answers `Class K9.k9color not found.`
- **An empty `MATLABStruct[]` passes as a 1-by-0 struct**; R2025b refuses it as though its fields differed.
- **`MATLABStruct` refuses an invalid field name**; R2025b accepts `1x`. Its `GetFieldNames` answers a `string[]`, where R2025b's answers a compiler-made iterator (whose name the client's type label cannot read).
- **A complex single converts to `System.Numerics.Complex`**, a widening R2025b refuses.
- **A datetime, duration or categorical converts to `object[]` and `MATLABObject[]`**; R2025b fails with `ObjectNotAccessible`.
- **An object cannot pass to another runtime instance**; R2025b serializes it across with warnings. Plain data passes in both.
- **A negative `nargout` is `ArgumentOutOfRangeException`**; R2025b fails in its C++ runtime with `vector too long`.
- **The output and error writers receive whole lines**; R2025b's splits a line at each format argument. A disposed array, null writers and a failed start do not crash JGraph's runtime, and no error is printed when it ends.
- **A failed property access names no `getProperty.m` frame** (R2025b's is MATLAB code at line 25).
- **Handles compare with `<` by JGraph's own order**, which neither product specifies.
- **A package function (`+pkg`) is generated but cannot be called**: JGraph has no packages (open item 195).
- **An int64 or uint64 beyond 2^53, a complex integer and a sparse complex or logical value** keep JGraph's storage limits (ADRs 0234 and 0260).
- **`char` of an array of enumeration members answers their names as a char matrix**; R2025b answers the array itself.
- **An arguments line declaring `int8` or `uint16` refuses a complex value**: JGraph has no complex integers.
- **The report on standard error** keeps K1's and K8's differences: no `Error using *` for an operator (open item 170), the older words for an undefined function, no warning backtrace, and JGraph's own `disp`.
- **The attributes `Nojvm`, `OutOfProcess` and `RuntimeOption` are inert**: the archive always runs in the client's process.

## Still open

- Item 195: packages (`+pkg` folders) in the language.
- Item 196: `meta.class` lists its members as names, not `meta.method` and `meta.property` arrays.
- Item 197: the strongly typed interface's leftovers (inherited members, classes known only from the path, the attributes).
