# ADR 0267 — C shared libraries: mcc -W lib and compiler.build.cSharedLibrary

## Status

Accepted, 2026-10-25. Compiler and SDK plan, stage K11 (decisions KD7 and KO3).

## Context

R2025b's `mcc -W lib:<name> -T link:lib` (and `-l`, and `compiler.build.cSharedLibrary`) builds a C
shared library: it writes `<name>.h`, `<name>.c`, `<name>.def` and `<name>.exports`, compiles and links
them with mbuild into `<name>.dll` with `<name>.lib` and `<name>.exp`, and appends the deployable
archive to the DLL. The generated code gives a C program `<name>Initialize`,
`<name>InitializeWithHandlers`, `<name>Terminate` and `<name>PrintStackTrace`, and for each function
an `mlx` wrapper (MATLAB's `nlhs, plhs, nrhs, prhs`) and an `mlf` wrapper (its arguments by name). The
wrappers call only the runtime's C API, which stage K10 built as `jgrt.dll` (ADR 0266), so K11 is the
generator: K10 already ran R2025b's own generated `k10lib.c` against jgrt.dll.

What R2025b does was measured first by `record-k11.ps1` (`tools/matlab-checklist/compiler-probes`):

- **`probe_k11_build` (`k11_build`)**: a main build of 13 functions, one of each shape of signature
  (`src/k11`), its generated text kept in `recordings/k11_out`; 27 other `mcc` forms (`-l`, `-o`,
  `version=`, `-C`, `-g`, `-T codegen`, `compile:lib`, `-c`, no `-T`, `link:exe`, a library named
  as its function, bad names, `-j`, a P-file, an output named as an input, an ignored input, C's
  words, the wrappers' own parameter names, a script, a class, a file twice, no files, `-v`); five
  `compiler.build.cSharedLibrary` builds; and `CSharedLibraryOptions`' answers and 30 refusals.
- **`probe_k11_names` (`k11_names`)**: how the generator names arguments (several ignored inputs, C
  and C++ words, names of the generated code, inputs and outputs of one name) and breaks its lines
  (functions of 48 to 60 letters, a library of 61), and `-o`, `-W` and `-l` together.
- **`probe_k11_nocompiler` (`k11_nocompiler`)**: a build where mbuild finds no compiler
  (`ProgramFiles(x86)` naming an empty folder).
- **`k11_pe`**: each DLL's version block and what is appended to it (`pe-resources.py`).
- **`k11client` (`k11_client`)**: a C program compiled against the main build's own `k11lib.h` and
  `k11lib.lib`, calling every wrapper in both forms in three modes (the client's handlers, the
  library's own, terminate and start again).

They found:

- **The text** is a fixed template with the library's name and one block per function in command-line
  order, CR LF line ends, every `mlx` wrapper before every `mlf` one. An `mlf` wrapper takes `int
  nargout` only when the function gives an output, a pointer per output, an `mxArray*` per input
  (`()` for none), and passes `mclMlfFeval` the output and input counts, negative and one more for a
  trailing varargout or varargin. A script wraps as a function of nothing, a class as its constructor
  of `varargout = f(varargin)`.
- **Arguments are renamed**: an ignored input is `_U4b`, then `_U4c`; an input named as an output
  gets `_in` and the first number that names no other argument (`x_in1`, or `x_in2` beside an input
  called `x_in1`); C's keywords, `inline`, `NULL`, `bool`, `true` and `class` get a `0`. Not renamed:
  `restrict`, C++'s other words, `mxArray`, `MW_CALL_CONV`, `mclMlfFeval`, and an input called
  `nargout`, which the `mlf` wrapper then declares twice: that build fails.
- **Lines are broken** at 89 columns, a broken line keeping its blank: the `mlx` declarations and every
  definition's signature continue under the column after their parenthesis; the `mlx` wrapper's call,
  and `<name>Initialize`'s for a library name of 9 letters or more, under two columns left of their
  call's parenthesis, never past 89. The last character of a statement or definition (its semicolon or
  closing parenthesis) is not counted. The `mlf` declarations and calls are never broken.
- **`-C`** writes `<name>.ctf` beside the library, and the generated `InitializeWithHandlers` then
  calls `mclInitializeComponentInstanceNonEmbeddedStandalone(&_mcr_inst, path_to_dll, "<name>",
  LibTarget, …)` in place of the embedded stream's three calls.
- **The forms**: `-l` names the library after the first file, or `-o`; `-W lib:` and then `-o`, or
  `-o` and then `-W lib:`, is `COM_ERR_MULTIPLE_COMPONENT_NAMES`, while a second `-W lib:` names it
  again; with `-l`, `-m` or `-e` anywhere `-W lib:`'s name gives way to the file's; what follows the
  name but `version=` is ignored; `-T codegen` and `-c` write the source, the exports and the archive
  and compile nothing, without a `.def`; `compile:lib` leaves `<name>.obj` and no archive at all;
  `-g` adds `<name>.pdb`; a name that is not a C identifier is `COM_ERR_ILLEGAL_OUTPUT_NAME`.
- **The DLL** has R2025b's version block (the version as file and product, the file type 0) and the
  archive appended. With no compiler, mbuild prints "Supported compiler not detected…" and mcc fails
  with `Compiler:compiler:COM_SYSTEM_ERROR`, naming its mbuild command and leaving the `.def`.
- **`compiler.build.cSharedLibrary`** writes CLibWriter's command (`-W 'lib:<name>,version=<v>' -T
  link:lib -d <out>`, then `-v`, `-X`, `-a`, `-Z`, `-g`, `-C`, the files), answers `Results` with the
  header, the DLL and the import library and adds `GettingStarted.html`. The options derive the name
  from the first file and the folder as `.\<name>cSharedLibrary`; `LibraryName` takes any name a
  folder could hold, which mcc then refuses. inputParser's ambiguous start is
  `MATLAB:InputParser:AmbiguousParameter` "'Library' matches multiple parameter names: …".
- **A client** gets the same answers from every wrapper; the runtime keeps the process alive after
  `main` returns unless `mclTerminateApplication` was called.

## Decision

- **`JGraph.Deploy.CSharedLibrary`** writes R2025b's text below JGraph's header (" * JGraph
  Compiler: <version> (MATLAB Compiler 25.2, R2025b)", the date, mcc's words cut at 77 as R2025b cuts
  them): the template line for line, the argument names and the line breaks by the rules measured, the
  `-C` start. It compiles with the MSVC `loadlibrary`'s discovery finds (`CCompilers.SelectedMsvc`,
  the Options choice when it is MSVC; vcvars64's environment, captured once) as mbuild compiles,
  but with the C runtime linked statically (`/MT`, as jgrt.dll is): `cl /c` into a temporary folder
  against JGraph's `extern\include`, `link /DLL /def:` with `jgrt.lib` and `kernel32.lib`. It stamps
  R2025b's version block and appends the archive, or writes it beside the library for `-C`. A failed
  compile or link removes what the build wrote and reports the compiler's errors.
- **`MccCommandLine`** reads `-W lib:<name>[,version=v]` and `-l` (`MccTarget.CLibrary`), the name
  rules above, `-T link:lib`, `compile:lib` and `codegen` (`LibraryStage`), `-c` as codegen and `-g` as
  the debug build, neither warned about, and a library with `-T link:exe` as the mismatch.
- **mcc** exports every file named, in order (no `startup.m` beside the main, as for .NET), reads each
  function's arguments from its file (a P-file's by place, `i0`, `o0`, as R2025b's), refuses before
  writing anything when no MSVC is found, prints R2025b's "Building with '<compiler>'." and under `-v`
  its own command lines and the compiler's output, and writes the build logs.
- **`compiler.build.cSharedLibrary`** and **`compiler.build.CSharedLibraryOptions`** transcribed from
  R2025b's `toolbox/compiler_sdk/cli/build` (cSharedLibrary.m, CSharedLibraryOptions.m,
  SharedLibraryOptions.m, the builder and CLibWriter.m), every refusal measured.
- **jgrt.dll** exports `mclInitializeComponentInstanceNonEmbeddedStandalone`, opening
  `<component_name>.ctf` beside the file it is given, and `mclmcr.h` declares it with `mccTargetType`.
- **JGraph carries the headers and `jgrt.lib`** in `extern\include` and `extern\lib\win64\microsoft`
  beside itself, where MATLAB keeps its own, so a library builds without a runtime installed; the
  installer's staging checks for both. The runtime keeps K10's `include` and `lib\win64`.
- **Met on the way:**
  - inputParser's ambiguous start in R2025b's words for every compiler options class (the standalone and
    .NET ones answered in words R2025b does not use, never probed until now).
  - A version block's string table is named `000004B0`, R2025b's capitals, for programs too.
  - `run-quiet.ps1` writes its transcript when taskkill cannot end a timed-out child, which had ended
    the recording before.
  - The recordings' generated headers lose their argument list (`<ARGUMENTS>`): it cut the build
    folder's path across lines, past the tokens, and showed the user's folder (K10's `k10_out` too).
  - `mcc -?` names .NET assemblies and C libraries among what JGraph builds (it still said neither).
  - **Dispatch sweep 25** for the two new builtins, with their output counts.

## Measured

- **`run-k11.ps1`** runs the same probes under `jgraph -batch`, the same client against JGraph's
  `k11lib`, and stage K10's `k10feval` (both complex models) against `k10lib` built by JGraph's mcc
  from `src/k10`; `compare-k11.py` writes `recordings-jgraph/k11_differences.txt`, every difference put
  down to a divergence below or an open item, nothing unexplained:
  - The generated text: 150 of the 160 files both engines wrote are byte for byte R2025b's below the
    header; the 10 others are the renames below and item 205.
  - The builds: 167 of 191 keys agree, the 24 others JGraph's words (readme, getting-started page,
    `-v`), the display's layout (item 194), `compile:lib`'s archive and the `nargout` build.
  - The names: 70 of 78 keys agree, the others JGraph's renames and item 205.
  - The client: 193 of 193 cases agree, and its standard error (the stack trace, the default handler)
    line for line.
  - `k10feval` on JGraph's own generated library: 609 of 674 cases agree in each model, the 65 others
    ADR 0266's divergences, as with R2025b's generated code.
  - The DLLs: 320 of 527 facts agree, the others the imports, the static runtime's section and the
    file type.
  - A build of one file takes 0.2 to 0.4 s (1.5 s for the first, which reads vcvars64's environment);
    R2025b's take 4.9 to 8.4 s (13.9 s for the main build). The client's runs take 0.3 to 0.5 s against
    R2025b's 4.8 to 5.9 s.
- **Tests.** `CSharedLibraryTests` (7): the main build's text against `k11_out`, the header, the long
  names' and the long library's breaks against `k11_names`, the argument names, the `-C` start, the
  command line's forms and refusals, the breaking rule. `CSharedLibraryBuildTests` (3): the client
  compiled against JGraph's generated library and run, line for line with R2025b's run of it;
  `compiler.build.cSharedLibrary`'s results, files and version; and the refusal with no compiler while
  `-T codegen` still writes (the first two skipped where no MSVC is found). The fixture
  `k11_c_shared_library` (70 lines from R2025b, 1 recorded divergence, both representations). K7's
  probe rerun with JGraph: its library cases now agree but `compile:lib`'s archive. The Scripting,
  MatlabParity, Runtime, Startup and Deploy areas: 9,179 of 9,184 pass; the five that fail are
  `MatlabM79TablePiePropertyTests`' table forms of `scatter`, which fail at HEAD too (open item 198).

## Divergences

- **The header is JGraph's**: its compiler and version on the first line, where R2025b's names MATLAB Compiler 25.2.
- **The library is compiled with the C runtime linked statically** (`/MT`) and links `jgrt.lib`; R2025b's mbuild uses `/MD` and `mclmcrrt.lib`, so its DLL needs the Visual C++ runtime beside it. The DLL imports `jgrt.dll` and `KERNEL32.dll` only.
- **The version block names a DLL** (VFT_DLL); R2025b's writes 0, the unknown type.
- **An argument R2025b's wrapper would not compile with is renamed with a `0`**: an input called `nargout` beside the wrapper's own, `mxArray`, `MW_CALL_CONV`, `mclMlfFeval`, `restrict`, `false`, and C++'s words (`new`, `this`, `delete`, `template`, `namespace`, `operator`, `private` and the rest), which break a C++ client of the header; a renamed name that another argument already has gets another `0`. R2025b leaves them, and its build or its C++ client fails.
- **`-T compile:lib` writes the archive beside the object file**, which R2025b writes nowhere, so the object can be linked and its archive appended.
- **With no compiler the build is refused before anything is written**, with `Compiler:compiler:COM_SYSTEM_ERROR` in JGraph's words naming Microsoft Visual C++; R2025b's mbuild prints its MinGW advice and mcc names its mbuild command, leaving the `.def`. A machine with MinGW-w64 alone is refused too (item 206).
- **`-v` prints JGraph's command lines and the compiler's output**, not mbuild's search and options; the non-verbose build prints R2025b's one line.
- **readme.txt and GettingStarted.html are JGraph's words**, for the JGraph Runtime and `cl`, where R2025b's are MathWorks' for its runtime and mbuild.
- **A failed compile removes every file the build wrote**; R2025b leaves the `.def`.

## Still open

- Item 204: `mkdir` of a folder that exists warns in R2025b ("Directory already exists."), not in JGraph.
- Item 205: `true` and `false` as names in the MATLAB dialect (JGraph's lexer makes them keywords), which K11's names probe met as an argument.
- Item 206: the C library's leftovers: MinGW-w64, `-M` and `-f`, `windows.h`'s macros as argument names, `mbuild` itself, sample drivers.
