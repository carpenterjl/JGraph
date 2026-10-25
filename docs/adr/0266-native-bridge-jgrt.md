# ADR 0266 — The native bridge, jgrt.dll

## Status

Accepted, 2026-10-25. Compiler and SDK plan, stage K10 (decisions KD1, KD7 and KO3).

## Context

A C program calls a component built by MATLAB Compiler SDK through MATLAB's C runtime API:
`mclInitializeApplication`, `mclRunMain` and `mclTerminateApplication` around its work, the mxArray
functions (`mxCreateDoubleMatrix`, `mxGetPr`, `mxGetField`, …) for the values it passes and gets back,
and the functions of a C shared library `mcc -W lib` generates (`<lib>Initialize`, `mlx<F>`, `mlf<F>`),
whose generated code calls the runtime's component functions (`mclGetEmbeddedCtfStream`,
`mclInitializeComponentInstanceEmbedded`, `mclFeval`, `mclMlfFeval`, `mclGetStackTrace`). The program
includes `mclmcrrt.h` and links `mclmcrrt.lib`; at run time `mclmcrrt25_2.dll` comes from the runtime's
`runtime\win64`, which its installer puts on the PATH. The C++ Data API (K12), the Python package (K13)
and the Excel add-in (K17) sit on the same bridge in the plan. The user decided (KO3) to keep
MathWorks' C names: they are the ABI, and no program links both runtimes.

The plan's K10 is the bridge itself: hosting .NET, the application API, and the mxArray API with
caller-owned native memory. K11 is the generator that writes and compiles a C shared library.

What R2025b does was measured first by `record-k10.ps1` (`tools/matlab-checklist/compiler-probes`),
with three C programs (`src/k10client`) compiled by MSVC against R2025b's `extern\include` and
`mclmcrrt.lib`, each in both complex models where they differ, and every case that might crash in a
process of its own:

- **`k10mx` (`k10_mx_separate`, `k10_mx_interleaved`)**: about 270 questions to the mxArray API with no
  component: creation of every class and shape, sizes and subscripts, scalars, classes, IEEE values,
  element sizes, text both ways, structs, cells, reshaping, replacing data, memory, and 25 misuses.
- **`k10app` (`k10_app`)**: the application API: initializing twice and after terminating, the
  options, `-logfile`, `mclRunMain`, mx calls before and after.
- **`k10feval` (`k10_feval_*`)**: a client of `k10lib`, the C shared library R2025b's mcc built from
  `src/k10` (its generated text kept in `recordings/k10_out`): 40 kinds of input echoed and described
  by MATLAB, 58 kinds of output made by MATLAB and passed back in, objects and their properties, output
  counts through `mlx` and `mlf` (with varargin and varargout), seven errors with their reports, stacks
  and last messages, what the print and error handlers receive, and 12 misuses.

They found:

- **The mxArray API answers misuse quietly**: an index past the end answers NULL or does nothing;
  `mxCreateNumericMatrix` of a cell, char or logical class makes one; field names are not checked
  (`1bad`, `a b`, the same name twice); `mxGetPr`, `mxGetChars` and `mxGetLogicals` answer the data
  whatever the class; a sparse array has room for one nonzero at least; `mxGetScalar` of a sparse array
  is its first stored value; `mxCreateCharMatrixFromStrings` pads rows with NUL, not blanks.
  `mxCreateString` reads UTF-8 when the bytes are UTF-8 and the ANSI code page's otherwise;
  `mxArrayToString` writes the ANSI code page, a character it cannot hold as one 0x1A.
- **The two complex models** differ in names (the separate model's `mxGetPi`, `mxGetImagData`; the
  interleaved model's typed accessors and `mxMakeArrayReal`/`Complex`) and in `mxGetElementSize`, which
  counts both parts of a complex element in the interleaved model. mbuild compiles with
  `MATLAB_DEFAULT_RELEASE=R2017b`, the separate model.
- **The application**: a second `mclInitializeApplication` answers false, and so does one after
  `mclTerminateApplication`, after which `mclIsMCRInitialized` still answers true; `-startmsg`, an option
  it does not know and `-logfile,f` in one string are taken without a word; `mclGetLogFileName` answers
  `>` and the full name.
- **What crosses the call**: values arrive and leave in their own class and shape; a NULL cell element
  or field is `[]`; an object, a string, a function handle, a table and the like come back to C as a
  reference of class 48, size 1-by-1, its class name, that only `mxGetProperty`, `mxSetProperty`,
  `mxDuplicateArray` and passing it back in can use. No outputs asked for makes none. `mclMlfFeval`'s
  negative counts mean a trailing varargout pointer or varargin cell. The error handler receives the
  report (`Error using k10two\nToo many output arguments.\n\n`) without the identifier; the print handler
  receives each write; a warning goes to the print handler; `fprintf(2, …)` to the error handler as
  written. `mclGetStackTrace` gives `file F, name N, line L.` per frame; `mclGetLastErrorMessage` the
  message, the causes' and `Error in => file.m at line n`.
- **Where R2025b is wrong**: its process crashes on `mxGetPr` of a complex array in the interleaved
  model, on a NULL input to `mlx` or `mlf`, and on `mxGetProperty` of a property the object does not
  have or will not show; `mxIsClass` answers false for an object's own class; a duplicate of a value
  object shares it, so `mxSetProperty` on one changes both; a struct holding one field name twice is
  passed into MATLAB; a negative output count is a C++ "vector too long"; and the log file's name
  starts with `>`.

## Decision

- **`native/jgrt`**, a C++ DLL built with MSVC by `build.ps1` (static C runtime, `/Brepro`) and checked
  in prebuilt as `native/win-x64/jgrt/jgrt.dll` and `jgrt.lib`, as the launcher templates are:
  - **`mxarray.cpp`**, the mxArray API in native memory the caller owns, R2025b's answers for every
    case measured. Each array keeps its storage in one of the two complex layouts and is rearranged the
    first time it is asked for the other, so a program of either model reads an array made in either;
    the functions whose meaning differs by model are exported twice (`mxGetPr` and `mxGetPr_730`, and
    `mxGetData`, `mxSetData`, `mxSetPr`, `mxGetElementSize` likewise), the header naming the one its
    model wants.
  - **`runtime.cpp`**, the mcl API. The first call that needs the engine starts .NET 8 through hostfxr
    (nethost linked statically) on `jgrt.runtimeconfig.json` (the Windows desktop framework, so a
    component can show its figures), with `APP_CONTEXT_BASE_DIRECTORY` the engine's folder, loads
    `JGraph.Runtime.dll` into the default load context and asks its `NativeBridge.Start` for the entry
    points. The engine is found beside jgrt.dll (an application carrying it, the tests) or in the
    runtime's `bin\win64` for jgrt.dll in `runtime\win64`.
- **The headers**, `native/jgrt/include`, in JGraph's words with MathWorks' C names (KO3):
  `mclmcrrt.h` (with `windows.h`, as MATLAB's brings it), `matrix.h` (the model chosen by
  `MATLAB_MEXCMD_RELEASE`, `MATLAB_MEXSRC_RELEASE` or `MATLAB_DEFAULT_RELEASE` as mex and mbuild choose
  it; `MX_HAS_INTERLEAVED_COMPLEX` and its kin; `MX_COMPAT_32` refused), `mclbase.h`, `mclmcr.h` and
  `tmwtypes.h`. A separate-model name used in the interleaved model fails to build, as with MATLAB's.
- **`JGraph.Runtime.Native`**: `NativeBridge` (the resolvers for the engine's assemblies and native
  libraries, then the table), `NativeCalls` (the application, instances over `Component`, `mclFeval` on
  the engine thread with the instance's handlers as the call's writers, the last error, references'
  properties through `JgRuntime.RunIn`) and `NativeArrays` (mxArrays read into `RuntimeValue`s and made
  from them through jgrt's own functions, a value C cannot hold sent as a reference to it). Nothing
  thrown leaves an entry point: a failure is false, NULL or a message to the error handler.
- **The runtime's layout** gains `runtime\win64\jgrt.dll`, `include\*.h` and `lib\win64\jgrt.lib`;
  `bin\win64` carries jgrt.dll and its runtimeconfig beside the engine. `RuntimeLayout.LibraryFolder`
  names the library folder.
- **The launcher stays K6's.** The plan named a `jg*` launcher API for it; K6's launcher starts .NET
  itself on `jglaunch.dll`, smaller and with nothing between, so jgrt exports none.
- **Met on the way:**
  - **A user function's and a class's report names it**: "Error using k10two" before "Too many output
    arguments." and "Too many input arguments.", as R2025b's desktop and C runtime both write it.
  - **A script's own error text reaches a C error handler as written**: `IScriptOutput.WriteErrorText`,
    which every other sink takes as the line it always was.
  - `JgsDeployedMain.Report(error, handler: true)`, the report without its identifier line.
  - **Dispatch sweep 24** for stage K9's `configureDictionary`, `keyHash` and `namedargs2cell`, whose
    missing rows had failed `verify-method-classes.py` since K9, and the output counts it measured.

## Measured

- **`run-k10.ps1`** builds the probe's archive with JGraph's mcc (`build_k10_jgraph.m`, the Data API
  target until K11 builds C libraries), compiles the same three clients and R2025b's generated
  `k10lib.c` against the runtime's headers and jgrt.lib, appends the archive to `k10lib.dll`, and runs
  them through `run-quiet.ps1` with the runtime's `runtime\win64` on the PATH and R2025b's off it;
  `compare-k10.py` writes `recordings-jgraph/k10_differences.txt`, every difference put down to a
  divergence below or an open item.
  - The mxArray API: 269 of 269 cases agree in the separate model, 281 of 282 in the interleaved one
    (the one is R2025b's crash).
  - The application API: 89 of 91 agree (the log file's name, and when the file is made).
  - The calls: 609 of 674 cases agree in each model; the 65 others are recorded below.
  - Each run starts its runtime: R2025b's took 1.3 s for k10mx's questions, 1.2 to 1.4 s for each
    k10app case and 12.4 s for k10feval's calls; JGraph's, from a published runtime (ReadyToRun),
    0.3 s, 0.1 to 0.2 s and 0.6 s. The archive's build took 1.2 s.
- **Tests.** `NativeBridgeTests` (5): the mx functions in process against R2025b's answers (shapes,
  text, the two complex models, struct fields), and `jgrtclient.exe` (`Runtime/native`, built by
  `build.ps1`, run in a process of its own) on an archive JGraph's mcc builds from the probe's sources:
  calls, references, output counts, an error's report and stack, the handlers' pieces, `mclMlfFeval`,
  and the divergences. The Scripting, MatlabParity, Runtime, Startup and Deploy areas: 9,168 of 9,173
  pass; the five that fail are `MatlabM79TablePiePropertyTests`' table forms of `scatter`, which fail at
  HEAD too (open item 198). K9's Data API tests and fixtures pass with sweep 24's output counts. The
  headers compile warning-free at `/W4` as C and as C++ in both models.

## Divergences

- **jgrt.dll and jgrt.lib are JGraph's**, in the runtime's `runtime\win64` and `lib\win64`, where R2025b has `mclmcrrt25_2.dll` and `mclmcrrt.lib`; the headers are JGraph's text with MATLAB's names. A program built against R2025b is rebuilt against them.
- **The exported names are the C names themselves** (and `_730` for the separate model's), not R2025b's `_proxy` names; nothing compiled against MATLAB's headers links with jgrt.lib.
- **`mxGetPr` of a complex array in the interleaved model answers NULL**; R2025b's ends the process.
- **A NULL input to `mclFeval` is refused** with "Input argument 1 is NULL"; R2025b's crashes.
- **`mxGetProperty` of a property an object does not have, or will not show, answers NULL**; R2025b's crashes.
- **`mxIsClass` answers true for an object's own class name**; R2025b's answers false.
- **`mxDuplicateArray` of a value object is a copy**: `mxSetProperty` on it leaves the original; R2025b's changes both.
- **A struct with one field name twice is refused at the call**; R2025b passes it into MATLAB.
- **A negative output count is refused in JGraph's words**; R2025b reports a C++ "vector too long".
- **`mclGetLogFileName` answers the file's full name**; R2025b's puts `>` before it. JGraph's runtime makes the file when it starts, R2025b's at the first write.
- **What JGraph cannot hold is refused at the call, not changed**: an int64 or uint64 past 2^53 (`JGraph:interop:int64Precision`), a complex integer array, a complex sparse array. A sparse logical array comes in as sparse double, and a complex array whose imaginary parts are zero comes back real, as the engine keeps them; a sparse array comes back with room for exactly its nonzeros.
- **What the engine answers differently shows through the bridge**: `intmin`/`intmax` as doubles (open item 199), a `containers.Map`'s size (open item 200), `struct('s', "text")` keeping a char (open item 201), a field name MATLAB could not write read without complaint (open item 202), the display's layout (open item 194), no `> In` line after a warning (open item 170), and an undefined function in the desktop's words (open item 203).
- **The `jg*` launcher API is not built**: K6's launcher starts .NET on `jglaunch.dll` itself.

## Still open

- Items 199-202: the engine's answers above.
- Item 203: the bridge's leftovers (a sparse array's room, zero imaginary parts, the log file's moment, the undefined function's words, handler text beyond ASCII, the mcl functions not provided, joining a .NET process without the desktop framework, the exported `get_hostfxr_path`).
