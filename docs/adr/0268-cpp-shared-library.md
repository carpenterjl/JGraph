# ADR 0268 — C++ shared libraries for the MATLAB Data API: mcc -W cpplib and compiler.build.cppSharedLibrary

## Status

Accepted, 2026-10-25. Compiler and SDK plan, stage K12 (decisions KD7 and KO3).

## Context

R2025b's `mcc -W cpplib:<name>,generic` (and `compiler.build.cppSharedLibrary`, whose Interface is
`matlab-data` unless asked otherwise) builds a C++ shared library for the MATLAB Data API. There is no
compiled code in it: the build writes the deployable archive `<name>.ctf` and a strongly typed header
`<name>v2.hpp` into `v2\generic_interface`, with a readme there and another above it. A C++ program
includes MATLAB's `MatlabCppSharedLib.hpp` (the Data API in `matlab::data`, calls in
`matlab::execution`, the application and libraries in `matlab::cpplib`) and the generated header,
links `libMatlabDataArray.lib`, `libMatlabCppSharedLib.lib` and `mclmcrrt.lib`, starts the runtime
with `initMATLABApplication`, opens the archive with `initMATLABLibrary` and calls its functions
with `feval`, `fevalAsync` or the header's typed wrappers. The plain `-W cpplib:<name>` (and `,all`)
builds the legacy mwArray library beside the generic interface's archive, with no header; `,legacy`
the mwArray library alone. Decision KD7 left the mwArray interface out; decision KO3 named JGraph's
C++ API `jgraph::data`, `jgraph::execution` and `jgraph::cpplib` with MathWorks' class and member names.

What R2025b does was measured first by `record-k12.ps1` (`tools/matlab-checklist/compiler-probes`):

- **`probe_k12_build` (`k12_build`)**: over a copy of stage K9's functions (`src/k9`), a main build of
  26 files (functions of every argument shape, a value class, a handle class, an enumeration, a
  class with an optional method, a package function), a variadic build, 18 other
  `compiler.build.cppSharedLibrary` builds (one file, `varargin`, `varargout`, the three together,
  each class alone, a package function, untyped arguments, the class with an optional method,
  objects, a version, `-g`, an explicit Interface, a folder, and three with Interface `mwarray`, the
  last of a folder), a build with no output folder, 20 `mcc -W cpplib` forms, and the options'
  answers and refusals; the generated text kept in `recordings/k12_out`.
- **`k12client` (`k12_client`)**: one C++ program compiled against R2025b's headers and the main
  build's header, run in six modes: `local` (the Data API in the program's own memory, 218 cases:
  every factory, every index form, references, iterators, copy on write, every exception),
  `calls` (`feval` and `fevalAsync` of every function, errors and their stacks and causes, the
  output and error buffers, cancel and interrupt, objects and enumerations back and forth, two
  libraries), `typed` (the generated header: functions, classes' properties and methods, comparisons,
  enumerations), `twice` (a second start and a start after the end), `outproc` and `runmain`.
- **`k12_paths`**: where `initMATLABLibrary` finds an archive named without a folder, in four
  setups of `CPPSHARED_BASE_CTF_PATH`, the working folder and the program's folder.
- **`k12_edge`** (`record-k12-edge.ps1`): the Data API read, referenced and compared past the end of
  an array by two small programs, one case per process, built from one source against each engine's
  headers.

They found:

- **The header** is R2025b's codegen (`toolbox/matlab/external/engines/codegen/cpp` over the common
  templates): its comment, `#include "MatlabTypesInterface.hpp"` and `<map>`, the classes first in
  the order MATLAB's digraph toposort gives their dependencies, then the functions in the order
  given; every function and method a template on its output count with a `return_type_<name>`
  struct and one specialization per count, a method's specializations after its class; an input
  that may be complex also taken real in an overload of its own; `varargin` one overload taking a
  vector of arrays; static methods left out, and handle's methods but its comparisons; no copy
  constructor where a constructor of one untyped input would have the same signature; line feeds and
  no last one.
- **Two defects in it**: a name-value argument is passed by position, which the function then
  refuses (`k12_client: t.nv`, "Invalid argument at position 2. A name is expected."); and an output
  typed as one of MATLAB's own classes (a `(1,1) cell` or `struct`) falls through every category of
  `getOutputTypeCategory`, so its specialization returns an uninitialized array, where the
  generator's own comments say it passes the array as it came.
- **The Data API** keeps a dimension list as given, refuses a single index into a 2-D array
  (`NotEnoughIndicesProvided`), allows trailing extra zero indices, converts between numeric types on a
  read and refuses a mismatched write (`TypeMismatch`), and answers two missing strings as unequal.
  Its comparisons of an element are declared `noexcept`: one of a number past the end ends the process,
  one of a string past the end answers false. A struct element taken past the end is refused only
  when a field is read, and iterating it ends the process. Reading a cell's element as an `Array` past
  the end throws the library's internal `foundation::matlabdata::EArrayIndexOutOfRange`, a bare
  `std::exception` with "Array index is out of range.", though the header documents
  `InvalidArrayIndexException`, which every other read throws.
- **Calls** reach only the component's functions, its classes' methods on their objects and the few
  functions the header calls on an object (`subsref`, `subsasgn`, `isvalid`, the comparisons); any
  other name is a plain `std::exception`, "Function X not found in the component.". An error carries
  its identifier, message, stack and causes; its report goes to the call's error buffer, or standard
  error; warnings and text go to the output buffer, or standard output, as they are written. A queued
  call cancelled never runs ("The MATLAB command was cancelled."); a running one is interrupted only
  when asked ("The execution of the MATLAB command was interrupted."). A second `initMATLABApplication`
  is refused while one runs and after one ended. Two libraries of one archive run in sessions of their
  own. An archive named without a folder is looked for in `CPPSHARED_BASE_CTF_PATH`'s folder, the
  working folder, then the program's.
- **mcc**: a version given with `generic` is `COM_WARN_VERSIONING_NOT_SUPPORTED`; `-T link:exe`,
  `codegen` and `compile:lib` all write the generic interface; `-o` after the name is
  `COM_ERR_MULTIPLE_COMPONENT_NAMES` while a second `-W cpplib:` names it again; a name that is not
  an identifier is `COM_ERR_ILLEGAL_OUTPUT_NAME`; `-c` writes a C library's source instead, as `-W lib`
  with `-T codegen` would. **compiler.build** writes CppLibWriter's command (`-W 'cpplib:<name>,generic'
  -T link:lib -d <out>`, `-v`, `-Z autodetect`, the files), answers `Results` with `<out>\v2` and adds
  `GettingStarted.html`; `CppSharedLibraryOptions` derives the name from the first file and the
  folder as `.\<name>cppSharedLibrary`, warns when a version is set for `matlab-data`, and refuses a
  folder among the files for `mwarray`.
- **`subsref` and `subsasgn`**, which the header calls on an object, check a subscript structure
  whole before they index anything, in an order and words three more probes measured (below), and
  `subsasgn` called with no output is refused, "SUBSASGN must be called with an output.".

## Decision

- **JGraph's C++ Data API is header-only, in JGraph's own words** (`native/jgrt/include/jgraph`):
  `data_array.hpp` and `data_array_impl.hpp` (every array class, factory, reference, iterator,
  visitor and exception of the API, arrays held in the program's memory with copy on write),
  `execution.hpp` (`feval` and `fevalAsync` in every form, `FutureResult`, the stream buffers, the
  execution exceptions, typed calls), `cpplib.hpp` (`initMATLABApplication`, `initMATLABLibrary`,
  `runMain`, the archive's search) and `types_interface.hpp` (what a generated header is built on).
  MathWorks' names are the entry points: `MatlabCppSharedLib.hpp`, `MatlabDataArray.hpp` and its
  38 `MatlabDataArray/` headers, `MatlabExecutionInterface.hpp` and `MatlabTypesInterface.hpp` each
  include their `jgraph` header and `jgraph/matlab_names.hpp`, which names `jgraph` `matlab` too, so
  R2025b's generated header and a program written for MATLAB's runtime compile unchanged (KO3).
- **jgrt.dll carries the calls** (`cpplib.cpp`, `jgraph/jgrt_cpp.h`): `jgrtCppStart`, `Stop`, `Open`,
  `Close`, `WaitForFigures`, `Call`, `Cancel`, `Forget`, `Release` and `Free`. Arrays cross in a binary
  form of JGraph's own (`NativeWire`); a call is queued on the engine thread
  (`NativeCppCalls`, JGraph.Runtime) and ends through a completion function with its outputs, its error
  record or a refusal; its text goes to the call's buffers as it is written, or to the process's
  standard output and error. The process keeps whether the application has started or ended, so a
  second start and a start after the end are refused in R2025b's words.
- **`JGraph.Deploy.TypedInterfaceCpp`** is R2025b's C++ generator transcribed: the same token-replaced
  text built in the same order (CppClass, CppConstructor, CppProperty, CppCallable,
  CPPTypeConverter, writeReturnSection, writeTypeStruct, writeTemplateDefault,
  getOutputTypeCategory, generateNamespace, KeywordsCPP, resolveClassDependencies), over the
  functions and classes stage K9's reader takes from the files; JGraph's version line; the two
  defects corrected (a name-value argument passed with its name before it; an output of MATLAB's own
  class passed as the array it came as).
- **`JGraph.Deploy.CppSharedLibrary`** writes the archive, the header and the two readmes;
  **`MccCommandLine`** reads `-W cpplib:<name>[,generic|legacy|all][,version=v]`
  (`MccTarget.CppLibrary`, `CppInterfaceKind`), `-g` without a word and `-c` as R2025b reads it; **mcc**
  exports every file named (a folder's files too), generates the header first (its count line under
  `-v`), and writes the build logs; **`compiler.build.cppSharedLibrary`** and
  **`compiler.build.CppSharedLibraryOptions`** are transcribed from R2025b's
  `toolbox/compiler_sdk/cli/build` (cppSharedLibrary.m, CppSharedLibraryOptions.m, the builder,
  CppLibWriter.m) over the K11 options and the interface manager, every refusal measured.
- **The legacy mwArray interface is not built** (KD7): `,legacy` and Interface `mwarray` are refused;
  `-W cpplib:<name>` and `,all` write the generic interface's archive, as R2025b's do, and warn that
  the legacy library is not built.
- **JGraph and its runtime carry the headers**: `extern\include` beside JGraph (with `jgraph\` and
  `MatlabDataArray\`) and the runtime's `include`; the installer's staging and the runtime's publish
  check for them.
- **Met on the way:**
  - **`subsref`, `subsasgn` and `substruct`**, which the generated header reaches a class's properties
    by: the indexing a subscript structure spells, done as that indexing written out would be, in a
    scope of its own with the values bound as an assignment binds them, so a write leaves the
    caller's value alone (the ownership gate flagged the first version's plain bindings, through which
    `subsasgn` changed the array it was handed). Every level is checked before any is indexed, in the order and words three
    probes measured in R2025b: the structure's two fields, then its elements, then each level's type,
    that only a `.` follows a `()`, and its cell of indices, an assignment's `()` needing one. A `.`
    level's name goes to the dynamic field reference as given, a `{}` with no index is every element,
    and subsref gives as many outputs as its comma-separated list has. `substruct` is R2025b's
    M-file, checking only its argument count.
  - **`properties` of MATLAB's own value classes**, as R2025b lists them: a datetime's eight, a
    duration's and calendarDuration's `Format`, a containers.Map's `Count`, `KeyType` and `ValueType`,
    a table's variables then `Properties` and its two dimension names, none (0-by-1) for a
    categorical, a string or a cell (the ADR 0232 divergence on a categorical's `properties` is
    retired: `oi_categorical`'s row is exact). A containers.Map's ValueType now comes from its values as
    R2025b's does (real scalars of one numeric or logical class are that class, char rows `char`,
    anything else `any`).
  - **No process ended past the end**: a typed reference made past the end throws, as R2025b's
    does (JGraph's had ended the process from a noexcept constructor); an element past the end
    compares unequal and a struct element past the end iterates as none, where R2025b's library
    ends the process. `record-k12-edge.ps1` compiles two small probes from one source against both
    engines' headers and runs each case in a process of its own (`k12_edge`).
  - **The coverage verifier** read a struck name in the middle of a list as two names; it takes one
    whole now.
  - **Dispatch sweep 26** for the five new builtins, with their output counts.

## Measured

- **`run-k12.ps1`** runs the same probe under `jgraph -batch` and the same client, compiled against
  the runtime's headers and the header JGraph's mcc generated and linked with `jgrt.lib`, against
  JGraph's archives on a stand-in runtime with R2025b's off the PATH; `compare-k12.py` writes
  `recordings-jgraph/k12_differences.txt`, every difference put down to a divergence below or an open
  item, nothing unexplained:
  - The generated headers: the main build's 1,891 lines are R2025b's but its version line, with 8
    lines more that pass `k9nv`'s name-value arguments their names; every other build's header is
    R2025b's but its version line.
  - The builds: 147 of 211 keys agree; the 64 others are JGraph's words (readmes, the
    getting-started page), the display's layout (item 194), the version line, file sizes, and the
    legacy interface not built.
  - The client: 556 of 602 cases and error reports agree. The others: the cell read above; JGraph's
    storage limits (64-bit integers past 2^53, complex integers, complex and logical sparse, a zero
    imaginary part); an empty array's shape kept; the name-value correction; the package function
    (item 195); one session for two libraries of one archive; a char result taken as text in a
    typed tuple; R2025b's own frame and noise; a value's display (item 194); a deployed warning's
    location line and an operator's name heading its error (item 170); an undefined function's
    words in a deployed report (item 203).
  - The paths: 12 of 12 cases agree.
  - The local mode: 217 of 218 cases agree, the other the cell read.
  - Builds take 0.0 to 0.3 s (the main build 0.3 s); R2025b's take 2.8 to 17.9 s (the main build
    14.9 s). The client's modes that start the runtime take 0.4 to 6.8 s (the calls mode waits on
    purpose, for `k9slow`'s cancel and interrupt cases); R2025b's take 5.0 to 13.2 s. The local mode
    takes 0.2 s against 0.1 s, and the paths runs 0.4 to 0.5 s against 7.3 to 8.6 s.
- **Tests.** `CppSharedLibraryTests` (6): the main, variadic and one-function headers against
  `k12_out`, the comment, the name-value names, the command line's forms and refusals, the files the
  build writes. `CppSharedLibraryBuildTests` (1, skipped where no MSVC is found): the client compiled
  against JGraph's build and run in its `local` and `typed` modes, case for case with R2025b's run.
  The fixture `k12_cpp_shared_library` (184 lines from R2025b, none diverging, both representations):
  the options, mcc's forms, the header read back, `substruct`, `subsref` and `subsasgn` (their checks
  in order, their lists, what they leave alone), `properties` of MATLAB's classes and containers.Map's
  value types.

## Divergences

- **The header's version line is JGraph's**: "JGraph Strongly Typed Interface Version: <version> (R2025b)", where R2025b's names MATLAB's.
- **A name-value argument is passed with its name before it**; R2025b's header passes it by position, which the function refuses.
- **An output typed as one of MATLAB's own classes is returned as the array it came as**; R2025b's header returns an uninitialized array.
- **The C++ API is JGraph's own code in the jgraph namespace, named matlab too**, and a program links `jgrt.lib`, where R2025b's links three MathWorks libraries.
- **The legacy mwArray interface is not built**: `-W cpplib:<name>,legacy` and Interface `mwarray` are refused, and `-W cpplib:<name>` and `,all` write the generic interface alone with a warning.
- **A cell's element read past the end throws InvalidArrayIndexException**, as R2025b's header documents; R2025b's library throws its internal `EArrayIndexOutOfRange`, which no documented exception catches.
- **Comparing or iterating past the end does not end the process**: an element past the end compares unequal (`==` false, `!=` true) and a struct element taken past the end has no fields to iterate; R2025b's library ends the process (0xC0000409) for a number compared and for the iteration (`k12_edge`).
- **An empty array keeps its shape**: a 0-by-3 array reaches MATLAB as 0-by-3, where R2025b's Data API hands MATLAB 0-by-0.
- **Two libraries of one archive share one session**, so a handle object of one is good in the other; R2025b runs each in a session of its own and refuses it ("Invalid HandleObjectRef").
- **An object goes back only to the component that returned it**: another archive's library refuses a value object, which R2025b's takes.
- **A native call's error stack ends at the function that raised it**: R2025b's has a frame of its own below it, `matlab.engine.internal.fevalWithCustomTypeConversion`.
- **A char result is taken as text in a typed tuple**, as it is alone; R2025b's tuple refuses it.
- **An application started out of process runs in this one**: JGraph's runtime has no separate process.
- **The readmes and the getting-started page are JGraph's words**, for the JGraph Runtime.

## Still open

- Item 207: the C++ library's leftovers: sample drivers (`SampleGenerationFiles` is taken and checked, and nothing is written from it); an enumeration-vector property, a property only read or only written, and the generator's refusal of a file it cannot generate (`SpecifiedItemNotGenerated`), transcribed and never compiled or reached by a probe in either engine; and a package function by its package name (item 195).
- Item 208: the engine's own indexing, met through `subsref` and `subsasgn` and measured beside them: `x()` and `s()` the value itself, a char index read as its character codes, a string, cell or string-array subscript refused in R2025b's words, a char matrix as a dynamic field name, dot indexing of a number, `c{:} = v` over several elements, `reshape` of a struct array, two refusals R2025b gives when it parses, and two identifiers.
- Item 170, seen again: a deployed report does not head an operator's error with the operator ("Error using *"). Item 203, seen again: an undefined function's words in a deployed report.
