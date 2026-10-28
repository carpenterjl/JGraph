# ADR 0277 — The guide's "Deploying code" chapter, the acceptance set, and the gate

## Status

Accepted, 2026-10-27. Stage K22, the last, of the compiler and SDK plan
(`docs/plans/compiler-and-sdk-plan.md`, gitignored). It closes the arc ADRs 0256–0276 built: the
deployed session (K1), the dependency analysis (K2), the archive (K3) and its protection (K3b),
the runtime facade (K4) and the window host (K5), standalone programs (K6) and the rest of `mcc`
(K7), the .NET assembly for the MWArray API (K8) and for the MATLAB Data API (K9), the native
bridge (K10), the C (K11) and C++ (K12) shared libraries, the Python package (K13), the Production
Server (K14–K16), the installers (K18), the compiler apps (K19), MATLAB Projects and the Compiler
Task Manager (K20) and the legacy deployment projects (K21). Stage K17, the Excel add-in and the
Excel client for the server, waits on its research and stays open.

## Context

The plan gave K22 three parts: a "Deploying code" chapter in the scripting guide, an acceptance set
of one small program per target built from the same sources by R2025b and by JGraph and exercised
by the same client program, and the gate. Every stage before it had measured its own target against
R2025b with a probe of its own; what was still unmeasured was the whole road at once, from one source
tree through every build to a client in each language, in both engines, so that the lines a user of
the SDK sees are the same.

## Decision

### The guide

`docs/jgs-scripting-guide.html` gains "Deploying code", eight sections under their own group in the
navigation: what a build makes and how a deployed session behaves (the archive, the runtime,
`isdeployed`, `ctfroot`, the frozen path, the arguments, the dependency analysis and its pragmas);
standalone programs (`mcc -m`/`-e`, the two `compiler.build` functions, what a build writes, every
`mcc` switch, the protection levels, the extraction cache); the JGraph Runtime (what it holds, the
MSI, how a program finds it, `runtime=bundled`, the runtime functions); the libraries for .NET (both
APIs), C, C++ and Python, each with the build command, what it writes and how a client written
against MathWorks' SDK ports; the Production Server (`jgraph-mps new`/`run`, the RESTful API as it
answers it, discovery, health, metrics, the asynchronous protocol, the routes file, the protobuf
clients, `mps.cache` and `mps.sync.mutex`); installers (`compiler.package.installer`, the three
runtime deliveries, the zip, the runtime-only installer, WiX in the box); the apps, projects and
`deploytool`; and where deployment differs from MATLAB (no interchange of archives, the WiX bundle,
what is not there yet and what is out of scope, the limits carried over from the session). The
chapter ends with what the acceptance set showed.

### The acceptance set

`tools/matlab-checklist/compiler-probes/src/k22` holds one source tree: seven library functions with
`arguments` blocks (`acc_add`, `acc_stats` answering a struct, `acc_greet` answering text,
`acc_scale` a matrix, `acc_count` a persistent counter, `acc_fail` an error with an identifier,
`acc_quiet`), the console tool `acc_tool.m` over the data file `acc_data.txt` built in beside it,
the windowed app `acc_app.m` in App Designer's shape (a label and a Compute button, which puts the
library's answers in the label, logs them through `acc_log` to the file `K6_LOG` names and closes
the app), and the discovery file `acc_sig.json`.

`build_k22.m` builds the nine targets from them, one file run unchanged under R2025b's
`matlab -batch -noFigureWindows` and under JGraph's `jgraph -batch`: the console program with its
data file (`compiler.build.standaloneApplication`), the windowed program
(`standaloneWindowsApplication`), the .NET assembly for the MWArray API (`dotNETAssembly`, a class
`AccClass` in `AccNet`), the archive and strongly typed interface for the MATLAB Data API
(`Interface='matlab-data'`, `AccData`), the C shared library (`cSharedLibrary`, `acclib`), the C++
shared library (`cppSharedLibrary`, `acccpp`), the Python package (`pythonPackage`, `accpkg`), the
Production Server archive with the discovery file (`productionServerArchive`, `accarch`) and the
Excel add-in (`excelAddIn`, which R2025b builds and JGraph does not have until stage K17). It prints
one line per build and the deliverables each left.

`src/k22client` holds the clients, one per target, each written against MathWorks' SDK and run
unchanged against both engines' builds: `accnetclient` (C#, the MWArray API: construction,
each function in its one-output and `numArgsOut` forms, a struct's fields, a char array as text,
the implicit conversion from a string, `ToArray`, the error, disposal), `accdataclient` (C#, the
MATLAB Data API: `MATLABRuntime.StartMATLAB`, dynamic calls with and without `RunOptions`, a
`MATLABStruct`'s fields, .NET arrays in and out, then the generated `AccData.MATLABFunctions` with
the parameter types R2025b's generator writes - `Complex` for a double without `mustBeReal`, `char[]`
for a char - and `TerminateApplication`), `acc_c.c` (the generated `acclib.h`: `mclInitializeApplication`,
`acclibInitialize`, each `mlf` wrapper, the struct's fields through `mxGetField`, the error through
`mclGetLastErrorMessage`, `acclibTerminate`), `acc_cpp.cpp` (the Data API through
`MatlabCppSharedLib.hpp`: `initMATLABApplication`, `initMATLABLibrary`, `feval` every way, a
`StructArray`'s fields, then the generated `acccppv2.hpp` with `double`, `std::complex<double>`,
`std::vector<double>`, `std::u16string` and `matlab::data::Array` parameters as R2025b's generator
writes them), `acc_py.py` (`accpkg.initialize()`, each function, `matlab.double` in and out, the
struct as a dict, the error, `terminate`) and `acc_server.py` (the health and discovery API, each
function in both notations, the error reply, a missing argument, a missing function, a GET, and the
asynchronous protocol: the POST, the state, the result, the collection, delete, an error's result).
A .NET client's `using` lines are turned from `MathWorks.MATLAB.` to `JGraph.MATLAB.` for JGraph's
build and nothing else in any client changes; a C or C++ client is compiled against the runtime's
`include` and linked with `jgrt.lib` where R2025b's links `mclmcrrt.lib` and the Data API libraries.

`accept-k22.ps1 -Engine R2025b|JGraph` runs it all: the build, the console tool through
`run-quiet.ps1` with arguments, with none and on its error, the windowed program watched by
`k6-windows.ps1` with its Compute button pressed through UI Automation's InvokePattern (JGraph's
window as a JGraph window; R2025b's is a MATLAB window and runs only with `-AllowMatlabWindows`,
with the user's leave), the .NET clients built with `dotnet`, the C and C++ clients with MSVC, the
Python client under CPython 3.12, and the server client against R2025b's `muserve` or `jgraph-mps`
serving the archive; the recordings in `recordings/` and `recordings-jgraph/` (`k22_build.out.txt`,
`k22_accept.out.txt`), the machine's paths as tokens. `compare-k22.py` keys every line by its
section and label and names each differing key by a KNOWN pattern, or reports it unexplained.

### What the acceptance showed

Of the 245 keys the two recordings share, 218 agree line for line and the 27 that differ are the
three recorded groups below; nothing is unexplained. What agrees:

- **The console tool**: its arguments, `isdeployed`, the data file read through `fileread` from the
  frozen path, the library's answers over it, the persistent counter, and on `-fail` R2025b's error
  report on standard error (`Error using acc_fail (line 6)`, `Error in acc_tool (line 21)`, the
  identifier) and exit code 249.
- **The MWArray API**: every result's class, dimensions and `ToString` (`2.8000`, the matrix's two
  rows), `numArgsOut`, the struct's fields, a char array as text and a string's implicit conversion,
  `ToArray`, the counter across calls, the error's text with the MWMCR stack trace in R2025b's
  form, and a char `'x'` given for a double converted to 120 by the `arguments` block (123 after
  the sum) in both.
- **The MATLAB Data API**: the dynamic calls, a no-output function refused with
  `MATLAB:TooManyOutputs` unless `RunOptions(nargout: 0)` is given (as stage K9 recorded), a
  struct's fields as `MATLABArray`, .NET arrays in and out, a string `"x"` for a double converted to
  NaN, the generated `MATLABFunctions` with `Complex` and `char[]` parameters, the error's text,
  disposal and `TerminateApplication`.
- **The C library**: every `mlf` wrapper, the struct's fields through `mxGetField`, the error text
  `mclGetLastErrorMessage` answers (with its `Error in => acc_fail.m at line 6` line), the
  `arguments` block's refusal of a 1-by-3 char for a scalar, and termination.
- **The C++ library**: `feval` every way, a `StructArray`'s fields, the generated header's `double`,
  complex, vector, `u16string` and `Array` overloads, the counter continuing across the typed calls
  (4), the exception's `what()`.
- **The Python package**: the struct as a dict, `matlab.double` in and out, the counter, and
  `MatlabRuntimeError`'s text naming the cache path.
- **The server**: health, discovery (the archive, its four described functions, the release, each
  signature's `help`, `mwsize` and `mwtype`), each function in both notations, the error reply with
  its stack, a missing argument's `MATLAB:minrhs`, 404 `FunctionNotFound` and `URL not found`.

**Found with it and put right:** the discovery API wrote `"mwsize":[]` for a signature's
`size=1,:`, where R2025b writes `[1,":"]` (a number per numeric dimension, the colon as a string);
`Discovery.Parameters` now writes it so. The gate's full suite found a second thing, older:
since ADR 0255's maker wrapper, `scatter(tbl, 'Var1', 'Var2')` and `scatter3` of a table refused
their own table form, because the wrapper reads any capitalised word as a property name in the
MATLAB dialect and peeled the variable names as an option pair. `MadeShape` gains `TableNames`
(2 for `scatter`, 3 for `scatter3`): with a table first, the words after it are data however they
are spelled, and the trailing pairs are found after them. The clients themselves taught four things the SDK's user
meets: a C client must call `mclTerminateApplication` after the library's terminate, or R2025b's
process never ends (the K11 client did; the first acceptance client did not and timed out); a
dynamic .NET call of a function without outputs needs `RunOptions(nargout: 0)`; the strongly typed
interfaces type a double without `mustBeReal` as complex and a char as a char array, in C# and
C++ alike; and a client assembly must not carry the component's name in another case
(`accnet.exe` beside `AccNet.dll`), since Windows loads one for the other.

## Consequences

- The compiler and SDK plan is complete bar K17: a builder makes a program, a library or an archive
  from one source tree with MATLAB's own commands, and a client written for MATLAB Compiler SDK
  runs against the build after changing its `using` or `#include` lines and the runtime it links.
- The acceptance set is the plan's whole-road measure; `accept-k22.ps1` runs it again in either
  engine when a stage changes what a build makes.
- Two scripts behave differently, both put right: the discovery API's size of a `size=1,:`
  parameter, and `scatter` or `scatter3` of a table with its variable names, which works again
  with trailing options after the names.

## Measured

- **The acceptance set** (`accept-k22.ps1 -Engine R2025b`, then `-Engine JGraph -Runtime <a published
  runtime>`, `compare-k22.py`): 245 keys, 218 agree, 27 recorded divergences (the Excel add-in's 4
  rows, R2025b's windowed program's 13, muserve's asynchronous 10), 0 unexplained. R2025b's builds
  took 198 s, JGraph's 7 s. The .NET, C and C++ clients were compiled for each engine from the same
  source with only the `using` lines (C#) or the headers and `jgrt.lib` (C, C++) changed.
- **JGraph's windowed program** ran as a JGraph window: `acc app` shown, Compute pressed through
  InvokePattern, the log lines `started`, `isdeployed 1`, `computed add=5 n=5 mean=2.8 max=5`, the
  label text and `closing`, exit 0.
- **The gate**: the full suite once, 12,527 tests, 5 failing - all `MatlabM79TablePiePropertyTests`,
  `scatter` and `scatter3` of a table refusing their own table form (below); after the fix the
  table-chart, maker-option, scatter and Production Server tests pass (163); the seven `verify-*.py`
  scripts, the divergence harvest (1086 rows from 174 ADRs) and the parity ratchet pass.

## Not measured

- **R2025b's windowed program** (`windowed\acc_app.exe`): a MATLAB window, not run without the
  user's leave; the row records that. JGraph's ran, its button pressed and its log lines read.
- **The Excel add-in**: built by R2025b, not built by JGraph (stage K17), and no Excel is on this
  machine to drive either.
- **The asynchronous protocol against R2025b**: `muserve` answers 400 to `?mode=async`, so the
  rows are R2025b's refusal against jgraph-mps's protocol, which stage K15 built from the RESTful
  API's documentation.

## Divergences

- **The Excel add-in is not built.** `compiler.build.excelAddIn` is unresolved in JGraph until stage
  K17; R2025b builds `accxl_1_0.dll` with its `.hpp`, `.lib` and `.h` and warns that the component
  is registered for the current user.
- **R2025b's windowed program was not run**: a MATLAB window needs the user's leave
  (`-AllowMatlabWindows`); the row says so, and JGraph's run stands alone.
- **The asynchronous protocol has no oracle**: muserve answers 400 to `?mode=async`, so the ten
  rows are R2025b's refusal against jgraph-mps's protocol from the RESTful API's documentation
  (ADR 0271); the request collection asked without `since` is 400 `MissingParamSince`.
- **The runtime cache's version folder** in an error's file path is `R2025b` there and JGraph's own
  version here (ADR 0258); `compare-k22.py` reads both as `<VERSION>`.
- **A build's own chatter** (mbuild's "Building with", midl's file names, dotnet's summary for the
  .NET build, the add-in's registration warning) is R2025b's and not keyed; JGraph's builds print
  their lines alone.
