# ADR 0264 — .NET assemblies for the MWArray API

## Status

Accepted, 2026-10-24. Compiler and SDK plan, stage K8 (decisions KD3 and KO4).

## Context

R2025b's `mcc -W dotnet:` and `compiler.build.dotNETAssembly` build a .NET assembly a C# program
calls MATLAB code through: `<name>.dll` for the MWArray API and `<name>Native.dll` for the native
API, each a class per class map entry, the archive inside. Its clients reference MathWorks'
`MWArray.dll`. K7 checked `-W dotnet:` as R2025b checks it and then refused it as a target a later
stage builds. K8 builds it, for the MWArray API; the MATLAB Data API is K9's.

The user's decisions: MathWorks' class and member names under JGraph's namespaces, so a client ports
by changing its `using` lines (KD3); and .NET 8 clients only, since a .NET Framework process cannot
load .NET 8 assemblies (KO4, decided 2026-10-24 with KO3, KO6, KO7 and KO8).

What R2025b does was measured first by `record-k8.ps1` (`tools/matlab-checklist/compiler-probes`):

- **`probe_k8_build` (`k8_build`)**: the options class and 60 of its checks, 8 compiler.build builds and 14
  mcc forms, each into a folder of its own; the main build's generated text is kept in `recordings/k8_out`.
- **`k8client` (`k8_client`)**, a C# program of about 470 cases built against R2025b's `MWArray.dll`
  and the main build (`src/k8`: `k8echo`, `k8make`, `k8err`, `k8vout`, `k8args`, `k8show`, `k8quiet`,
  and a second class's `k8other`), run through `run-quiet.ps1` with the beep off.
- **`probe_k8_disp` (`k8_disp`)**: what R2025b's `disp` prints for 120 values under `format short`,
  the display an MWArray's `ToString` answers.

They found:

- **The generated classes.**
  - Each class has, per function, a single-output method for each count of named inputs, one more
    taking `params MWArray[] varargin` when the function takes varargin, the same again with
    `numArgsOut` first answering an array, and `f(int numArgsOut, ref MWArray[] argsOut, MWArray[] argsIn)`.
  - A function without named outputs (none, or varargout alone) has `void` single-output methods
    and, without varargout, no array form. The native class always has a protected
    `[MATLABSignature(name, named inputs, outputs, varargin)]` method.
  - The first class carries the `#if SHARED` key block. `using System.IO;` and the embedded-stream
    lookup appear only when the archive is embedded; `-C` opens `MWMCR(name, folder, true)`.
  - The namespace is the assembly name (dotted names kept) and its native one adds `Native`; the dll
    is named after the name's last part. Doc comments wrap within 89 columns and a path is cut at 90.
- **The files.** `-W dotnet:<name>` alone makes a class of that name and targets 4.0 (csc, a
  `<name>Version.cs`); 5.0 builds through the .NET SDK with two `.csproj` files. `-g` builds Debug
  with symbols, `remote` adds `I`-interfaces, `api=matlab-data` writes the archive and
  `strongly_typed_interface`. Only compiler.build writes `GettingStarted.html`. A `-W` class with no
  file of its own is still made. `class{Name:files}` names a class and its files.
- **The options** (`DotNETAssemblyOptions.m`, `parseClassMapInputs.m`, `cliName.m`,
  `InterfaceManager.m`, transcribed): the class map from a `containers.Map` or from the files given
  (`<first file>Class`), `AssemblyName` derived as `example.<first file>`, `OutputDir` as
  `.\<name>dotNETAssembly`, version 1.0.0.0 when the interface is still mwarray once every option is
  set, the interface warnings, and every refusal in R2025b's words.
- **The MWArray API.**
  - Flat data is read by rows unless `rowMajorData` is false; a logical's `columnMajorOrder` is the
    other way about. A scalar or a .NET array makes a double unless `makeDouble` is false; `int` and
    `int[]` convert to double, `byte`, `short`, `long` and `float` to their own classes. `int[]` is
    the dimensions constructor. A .NET array's last two dimensions are MATLAB's rows and columns.
  - Elements are read and written at MATLAB's 1-based subscripts, a linear one or one per dimension;
    anything else is `Invalid index`, and nothing grows. `GetField(name, index)` counts from 0.
  - `ToScalarDouble` and its kin read the first element of an array of their class only;
    `ToScalarInteger` also truncates a double. `IsNaN` and `IsInfinity` refuse a non-scalar.
  - `ToString` is MATLAB's `format short` display for numbers, logicals and text (fields of 6 or 12
    for whole numbers, of 10 to 4 decimals under a common `1.0e+03 *` scale, `1.2346e+05` for a
    scalar out of range, the window 80 columns wide), and MATLAB's older display for cells and
    structs (`[5]    'two'    [2x2 double]`, `name: 'probe'`, a string as `[1x1 opaque]`).
  - A MATLAB error is a plain `Exception`: `... MWMCR::EvaluateFunction error ...`, the message and
    its causes, `Error in => file.m at line n.` and the M-code stack, after the error's report on
    standard error. Too many outputs is MATLAB's own error at the call; a negative count is refused first.
  - The native API answers every number, logical and char as an array of at least two dimensions, a
    string array as a vector, a cell or struct as its own classes; an `object[]` argument is spread
    into its elements, and int8, uint16, uint32, uint64 and complex values have no native form.
- **Where R2025b answers wrongly.** An int8, uint16, uint32 or uint64 result throws
  `ArgumentOutOfRangeException` naming `numArgsOut` (the research had read this as a check of the output
  count before the call); an element of a complex array, and its imaginary part, read 0; a double written
  into an int32 array, and a complex value into a real one, are ignored; a field named `1bad`, or a name
  twice, is accepted; the native API's sparse result has the wrong shape; a character outside the
  Windows code page is lost from `ToString`.

## Decision

- **`src/JGraph.MATLAB.NET.Arrays`**, the MWArray API under `JGraph.MATLAB.NET.Arrays`, `.native` and
  `JGraph.MATLAB.NET.Utility`: every class, constructor, member and conversion R2025b's `MWArray.dll`
  has (`sdk_probe/mwarray_v40.txt`), as `k8_client` measured them.
  - `MatlabDisplay`: MATLAB's `format short` display, from the `k8_disp` corpus. `LegacyDisplay`:
    the older cell and struct display.
  - `NativeConversion`: .NET arrays to MATLAB sizes and back, and the native API's values.
  - `MWMCR` over a `JGraph.Runtime` component, R2025b's error messages, the report on standard error.
  - `RuntimeLocator`: a module initializer that loads the engine's assemblies and native libraries from
    the JGraph Runtime of the assembly's version (`JGRAPH_RUNTIME_ROOT`, then the installer's key) when
    the client's folder does not hold them, as R2025b's MWArray.dll finds the MATLAB Runtime.
- **A client references `toolbox\dotnetbuilder\bin\win64\net8.0\JGraph.MATLAB.NET.Arrays.dll`** in the
  runtime, the assembly alone in its folder as R2025b keeps MWArray.dll, so a client's build copies it
  and no engine assembly (`publish-runtime.ps1` writes it; at first the client referenced the copy in
  `bin\win64`, and its build copied the whole engine beside it).
- **`JGraph.Deploy.DotNetAssembly`**: R2025b's generated text with JGraph's header and namespaces,
  compiled in process with Roslyn against .NET 8's reference assemblies (carried beside JGraph in
  `toolbox\dotnetbuilder\ref\net8.0`, or an installed SDK's pack), the archive as a manifest resource
  or beside the assemblies, the XML documentation, symbols for `-g`, a strong-name key when given; the
  4.0 build's `Version.cs` or the 5.0 build's two `.csproj` files; the overview, getting-started page and
  readme in JGraph's words.
- **`MccCommandLine`** reads `-W dotnet:` in full (`DotNetWrapper`), `class{...}` arguments and `-g`
  as a debug build. **mcc** builds the classes from the `-W` class and every `class{}`, each
  function's inputs, outputs and help line read from its file.
- **`compiler.build.dotNETAssembly`** and **`compiler.build.DotNETAssemblyOptions`**, transcribed, with
  the class map shown under `Class Map Information`.
- **Met on the way:**
  - **`isreal`** is false in the MATLAB dialect for anything but a number, char or logical, as R2025b's.
  - **A `containers.Map`'s keys are kept sorted**, so `keys` and `values` answer in R2025b's order.
  - **`dir` searches a `**` folder**, the folder and every one below it (open item 188).
  - An object display shows an on/off property as `on` or `off`, and a map as `[n×1 containers.Map]`.
  - `DeployedException.Causes`, and `Component`'s signatures for the native API.

## Measured

- **`run-k8.ps1`** runs `probe_k8_build` unchanged under `jgraph -batch` and `k8client` with its `using`
  lines changed against JGraph's build (`recordings-jgraph/k8_differences.txt`).
  - The client: 456 of 472 cases agree. The 16 that differ are the divergences below.
  - The builds: 114 of 133 keys agree. The rest are JGraph's readme and verbose lines, remoting, the
    MATLAB Data API and `-T link:exe`.
  - The generated classes are R2025b's text but for the header, the paths and the namespaces.
  - Against a published runtime, the client's folder holds `JGraph.MATLAB.NET.Arrays.dll`, the two
    assemblies and itself; the engine is loaded from the runtime's `bin\win64`.
  - R2025b's main build took 15.3 s, JGraph's 1.3 s; the client's run took 7.6 s on R2025b's runtime
    and 1.2 s on JGraph's.
- **The fixture `k8_dotnet_assembly`** has 88 lines from R2025b: the options' defaults and 37
  refusals, 7 builds' files, 5 mcc refusals, `isreal` and `dir`. 85 are exact; 3 are divergences.
- **Tests.** `MWArrayDisplayTests` (108: 107 corpus values), `MWArrayApiTests` (5),
  `DotNetAssemblyTests` (2: the generated text against `k8_out`, and 80 calls against `k8_client`),
  `MccSwitchesTests` updated.

## Divergences

- **A .NET assembly targets .NET 8, for .NET 8 clients**, whatever `FrameworkVersion` says (decision KO4); R2025b's 4.0 assemblies load in .NET Framework processes.
- **The MWArray API is `JGraph.MATLAB.NET.Arrays.dll` under JGraph's namespaces** (decision KD3), and a disposed array's refusal names `JGraph.MATLAB.Arrays.MWArray`.
- **The generated files' header names JGraph's compiler**, and the `.csproj` files target net8.0 and reference JGraph's arrays assembly.
- **The classes are compiled in process with Roslyn**, so `-v` prints no `csc` or `dotnet build` lines, and no .NET SDK is needed.
- **readme.txt, the overview and GettingStarted.html are in JGraph's words.**
- **buildresult.json lists the base runtime only**; R2025b adds its .NET and Excel add-on (35051).
- **Remotable assemblies are refused** (`JGraph:mcc:targetOutOfScope`, `JGraph:compiler:targetOutOfScope`): .NET 8 has no remoting.
- **The MATLAB Data API is refused until stage K9** (`JGraph:mcc:targetNotBuilt`, `JGraph:compiler:targetNotBuilt`).
- **`-T link:exe` with `-W dotnet:` builds the library**; R2025b writes an archive and one assembly.
- **An int8, uint16, uint32 or uint64 result comes back as an MWNumericArray of its class**; R2025b throws `ArgumentOutOfRangeException` for `numArgsOut`.
- **The native API refuses an int8, uint16, uint32 or uint64 result as a type it has no form for**; R2025b throws `ArgumentOutOfRangeException`.
- **An element of a complex array, and its imaginary part, read their values**; R2025b reads 0.
- **A value written into an integer array is rounded to its class, and a complex one makes a real array complex**; R2025b ignores both.
- **A struct's field names must be valid and distinct**; R2025b takes `1bad` and a name twice.
- **The native API's sparse result is its whole matrix**; R2025b's has the wrong shape.
- **A sparse logical result is a sparse double** (JGraph has no sparse logical, ADR 0234).
- **ToString keeps characters outside the Windows code page**; R2025b's loses them.
- **The report on standard error names an undefined function in the older words** (`Undefined function ... for input arguments of type 'double'`) where R2025b's says `Unrecognized function or variable`.
- **What a component's `disp` prints is JGraph's own display**, as in every session.
- **An MWObjectArray cannot be passed into a component yet**, and `Images.renderArrayData` and the arrays' binary serialization are not provided.

## Still open

- Item 188 (`dir` with `**`) is done here.
- Item 191: `SampleGenerationFiles` is taken and checked, and no sample drivers are written.
- Item 192: signing with a strong-name key file is built and not yet tried with a real key.
- Item 193: an MWObjectArray, a .NET object, passed into a component.
- Item 194: `MatlabDisplay` draws R2025b's `format short` display; the MATLAB dialect's own `disp`
  still draws JGraph's.
