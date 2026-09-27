# ADR 0181 — loadlibrary and the prototype model

## Status

Accepted. Stage I8 of the .NET and shared-library interop plan
(`docs/plans/net-and-shared-library-interop-plan.md`), after stage I7 (ADR 0180, 81f20f4). One
commit, this ADR's. It gives the native host of ADR 0180 its MATLAB surface: `loadlibrary`,
`unloadlibrary`, `libisloaded`, `libfunctions`, `mexext`, `mex.getCompilerConfigurations`, the
Options choice of C compiler, and a first `calllib`. Pointers, `libpointer` and `libstruct` are
stage I9.

## Context

`loadlibrary(lib, header)` in R2025b preprocesses the header with the selected MEX compiler, parses
the declarations with its own (Perl) parser, and builds a *thunk* library, one small C function per
signature, through which every later `calllib` goes. `loadlibrary(lib, @protofile)` skips the header
and reads the same description from an M-file R2025b wrote earlier with `'mfilename'`.

The plan's user decisions fix three things:

- a header needs a compiler, as it does in MATLAB, and without one the load fails with MATLAB's
  sentence;
- the compiler is an Options choice (automatic by default);
- both dialects get the names.

Step 0 recorded the `shrlib_load` fixture against R2025b, including a prototype file R2025b wrote
for `jgtestlib.h`. Stage I7 measured the library search and the `LoadFailed` sentence. What remained
open was:

- how R2025b names C types it was not shown (typedef'd tags, anonymous structs, `wchar_t`, enum
  pointers, `long double`, `__stdcall`, arrays, externs);
- which structs and enums reach the prototype file;
- the identifier of every warning `loadlibrary` raises;
- the refusal sentences of the whole family;
- `mex.getCompilerConfigurations`'s shape;
- whether R2025b can read back a prototype file JGraph writes.

Three new probes answer those (below).

One more decision was the user's, taken at the start of this stage: R2025b cannot call a function
that returns a struct by value (`jg_point_make` goes to `notfound` with
`MATLAB:loadlibrary:InvalidFunctionReturnType`), and JGraph's host can. JGraph calls it. The user's
words were that this adds to MATLAB's behaviour rather than departing from it. It is recorded below as
an extension, and the fixture rows it changes are `div=ADR0181`.

## Decision

**One model for both routes.** `LibraryModel` is R2025b's prototype file as data:

- `LibFunction`: `name`, `calltype`, `LHS`, `RHS`, `alias` and `thunkname`;
- `LibStruct`: members and packing;
- `LibEnum`.

Types are MATLAB's shared-library names (`int32`, `doublePtr`, `cstring`, `stringPtrPtr`, a struct's
or enum's name, `FcnPtr`, `error`). A header and a prototype file both produce one, and
`libfunctions`, `calllib` and the `mfilename` writer read one. It also lays structs out as MSVC does
for x64, packing included. A unit test holds the layout to the offsets the library itself exports
(`jg_layout`).

**The header route.**

1. **Find the header.** It is found as given, then in the current folder, the library's folder and
   the path. `loadlibrary(lib)` looks for `<lib>.h`. A missing header is R2025b's
   `MATLAB:loadlibrary:FileNotFound`, checked before the library or any compiler.
2. **Find a compiler.** `CCompilers` looks in this order, first found wins:
   - the Options choice;
   - `MW_MINGW64_LOC` (MATLAB's own MinGW variable);
   - MSVC through `vswhere`, newest first, any Visual Studio from 2019 on (R2025b's list stops before
     2026; JGraph's does not);
   - `cl.exe` on `PATH`;
   - `gcc.exe` on `PATH`.

   MSVC runs in `vcvars64.bat`'s environment, captured once per process. With none found, the load
   is R2025b's `MATLAB:mex:NoCompilerFound_link_Win64`, its hyperlinks reduced to their text.
3. **Preprocess.** The header is preprocessed as C, keeping its line markers:
   - MSVC runs `cl /nologo /E /TC /Zp8` (R2025b's `/Zp8`);
   - GCC runs `gcc -E -x c`;
   - both search the header's folder, the current folder and each `includepath`.

   A failure is R2025b's `MATLAB:loadlibrary:cppfailure`: "Failed to preprocess the input file."
   and "Output from preprocessor is:" followed by the compiler's own text.
4. **Parse.** `CHeaderParser` is a C declaration parser, not R2025b's Perl:
   - it parses the whole translation unit, since `windows.h` has to go through;
   - it skips `__declspec`, `__attribute__`, `__pragma`, calling conventions, pointer qualifiers,
     `_Static_assert` and function bodies;
   - it follows `#pragma pack` and `__pragma(pack(…))`;
   - it evaluates enum and array-size constant expressions (`E_ONE << 3`, `'a'`, `0x10 | 0x01`,
     `sizeof`);
   - a declaration it cannot read is skipped to its end, and the rest is kept.

   It records only what the named headers declare, known by the line markers: the header, and each
   `addheader` by its file name with or without `.h`. That covers functions, exported variables
   (`calltype` `data`, `LHS` the type plus `Ptr`), and every struct and enum they *define*, used or
   not. Types are resolved wherever they were declared.

**The names are R2025b's, measured.** The parser is held by unit tests to the two prototype files
R2025b wrote:

- `jgtestlib_proto.m` (step 0), for `jgtestlib.h`;
- `probe_shrlib_parse_proto.m` (this stage), for a header built to ask every open question.

Both are compared function by function, struct by struct and enum by enum, from committed
preprocessor output, so the parser tests need no compiler. The rules they pinned:

| C | MATLAB type |
|---|---|
| `char*`, `const char*`, `signed char*` written so | `cstring` |
| `int8_t*` (the same type through a typedef), `char s[16]` as a parameter | `int8Ptr` |
| `char**` / `char***` | `stringPtrPtr` / `int8PtrPtrPtr` |
| `unsigned char*`, `wchar_t*` | `uint8Ptr`, `uint16Ptr` |
| `long` / `unsigned long` | `long` / `ulong` |
| `size_t`, `ptrdiff_t`, `intptr_t`, `__int64` | `uint64`, `int64`, `int64`, `int64` |
| `long double` | `error`, with "Type 'longdouble' was not found" |
| a struct | its tag, or its typedef when it has no tag (`tagA`, `B`, `ANON_T`) |
| `struct T*`, `enum E*` | `TPtr`, `EPtr` |
| a union, a bitfield struct | the typedef stays unknown: `error` by value, `voidPtr` by pointer, each warned |
| `int m[2][3]` as a parameter / a member | `int32Ptr` / `int32#6` |
| a struct member `T arr[2]` | `T#2` |
| a typedef'd function pointer | `FcnPtr` |
| a parameter written as a function pointer | the function is dropped, as R2025b drops it |
| varargs | `RHS` ends in `error`, `calltype` `cdecl`, no thunk |
| `__stdcall` | nothing: x64 has one call |
| `f()` / `f(void)` | no parameters (the thunk names differ, as R2025b's do) |

Thunk names are computed too, as R2025b spells them (`int16longuint16ulongThunk`,
`voidstructBThunk`), so a written prototype file is R2025b's own.

**The prototype-file route.** `loadlibrary(lib, @protofile)` calls the function for its four
outputs and reads `methodinfo`, `structs` and `enuminfo`. A text second argument that names a
function and no header is a prototype file too, as R2025b takes it. `ThunkLibName` is ignored: the
host calls every function through `calli`. A prototype function that fails keeps its identifier, with
R2025b's "There was an error loading the library" sentence first.

**Writing one.** `'mfilename'` writes the model in R2025b's layout, thunk names included, and it
names the thunk library after the library, as R2025b does. `probe_shrlib_readback` loaded a file JGraph
wrote into R2025b:

- beside R2025b's own thunk for the same header, it loaded and called exactly as R2025b's own file
  does;
- with `ThunkLibName` emptied, R2025b drops every `Thunk` function to `notfound`;
- with every call type set to `cdecl`, R2025b calls without a thunk, and passes a `double` wrongly
  (`jg_double(2.5)` answered 1).

So the file keeps R2025b's form. The folder `mfilename` names is kept: R2025b writes into the
current folder whatever folder it is given (step 0).

**Loading.**

- The library is found by stage I7's search and loaded in the session's host.
- Each function and variable the model names is resolved once. What the module lacks is `notfound`:
  a row cell, or 0×0 when nothing is missing.
- The load's warnings are R2025b's, in its order, with its identifiers (`probe_shrlib_warnids`):
  1. `parsewarnings` — the header route, asked for fewer than two outputs, when the parse warned;
  2. `TypeNotFound` — for each function using `error`;
  3. `TypeNotFoundForStructure` — for a struct member of type `error`, `FcnPtr`, or an array of
     structs;
  4. `EnumExists` and `StructTypeExists` — when another loaded library defines the same name;
  5. `FunctionNotFound` — for each missing export;
  6. `nofunctions` — on the header route, when nothing was found.
- A name already loaded is `MATLAB:loadlibrary:ClassIsLoaded` and a no-op.
- The options are case-sensitive, and a bad one is refused in R2025b's words. `thunkfilename` is
  accepted and names nothing.

**The rest of the family.**

- `unloadlibrary` frees the module; its refusals are R2025b's `NameMustBeSpecified` and
  `ClassNotFound`.
- `libisloaded` refuses anything but text with `LibraryNameRequired`. A library whose host has died
  is not loaded.
- `libfunctions` transcribes `libfunctions.m`: the 180-column `methods` layout headed "Functions in
  library", `-full` for signatures, a cell column when asked. It answers `[]` for a library not
  loaded, and prints "No class 'lib.x'." when not asked. Anything but text goes to `methods`.
- The signature line is R2025b's:
  - the return first;
  - a returned pointer as `lib.pointer`;
  - an enum as `lib.<name>`;
  - then every pointer argument's value after the call, except `FcnPtr` and three-level pointers;
  - one output bare, several bracketed.
- `mexext` answers `mexw64`, and `mexext('all')` R2025b's four platforms.

**The compiler setting.**

- It is `CCompiler` in the shared settings file: a discovery id, or none for automatic.
- The Options dialog lists "Automatic (…)" and every compiler found, and the app applies the choice
  at start and on every save. The CLI reads the same file.
- A choice naming a compiler no longer found falls back to automatic.
- `mex.getCompilerConfigurations(lang, list)` answers `mex.CompilerConfiguration`-shaped structs with
  R2025b's eleven properties, and `Details` with its eleven. `C`, `C++`, `Any`, `Selected`,
  `Installed` and `Supported` are case-insensitive, and anything else is `MATLAB:mex:UnknownSwitch`.

**`calllib`, stage I8's part.**

- **Refusals:** R2025b's `NameAndFunctionNeeded`, `NotFound`, `MethodNotFound`, and
  `NoMatchingSignatureFound` (a wrong count, or outputs a function does not have).
- **Values that cross the call:**
  - numbers, rounded and saturated into the parameter type;
  - logicals;
  - C strings (the argument's value after the call is an output);
  - enums, by value or member name;
  - a struct returned by value, as a MATLAB struct of its members.
- **Anything else** is `JGraph:calllib:UnsupportedType` until stage I9.
- **The host:** a crash is stage I7's `HostExited`, and it unloads every library, and a cancel kills
  the host.

## Probes

Each runs through `run-probes.ps1` and writes its answers beside it:

- `probe_shrlib_parse` covers:
  - the type names and recorded structs and enums of `probe_shrlib_parse.h` and
    `probe_shrlib_parse_inc.h` (whose prototype file and MSVC preprocessor output are committed under
    `tests/Interop/native/parse/`);
  - `addheader`;
  - every refusal of `loadlibrary`, `libisloaded`, `libfunctions`, `unloadlibrary` and `calllib`;
  - `mexext`;
  - `mex.getCompilerConfigurations` and the missing-compiler sentence.
- `probe_shrlib_warnids`: the identifier of each load warning, from hand-written prototype files
  that each provoke one.
- `probe_shrlib_readback`: R2025b loading JGraph's prototype file, and the `parsewarnings` identifier.

## Measured

Release, this machine, R2025b beside it:

| What | JGraph | R2025b |
|---|---|---|
| `loadlibrary(lib, 'jgtestlib.h')`, first in the process (vcvars captured) | 1.25 s | 1.0 s |
| The same header with `#include <windows.h>` | 0.28 s | 10.1 s |
| `loadlibrary(lib, @jgtestlib_proto)` | 0.034 s | ~0 s |

R2025b's header route builds a thunk DLL, which is most of its cost. JGraph's is preprocessing
(about 0.2 s for `windows.h`), then a parse of the whole unit.

## What moves

- **New `Jgs/Native/`:**
  - `LibraryModel` (the model, `LibTypes`, layout, signatures);
  - `CHeaderParser`;
  - `CCompilers` (discovery, `vcvars` capture, preprocessing);
  - `PrototypeFile` (read and write);
  - `SharedLibrary`.
- **`NativeSession`** keeps the loaded libraries and forgets them with their host.
- **New `JgsBuiltins.SharedLibraries.cs`:**
  - `loadlibrary`, `unloadlibrary`, `libisloaded`, `libfunctions`, `calllib`;
  - `mexext`, and the `mex` namespace for `getCompilerConfigurations`.

  They are registered from `RegisterEvalBuiltins`, and named in `EvalBuiltinNames` and the catalog.
- **New public `LoadLibraryCompilers`** for the hosts; `CCompiler` in `UserSettingsDto` and
  `UserSettings`; the Options dialog's "C shared libraries" group; `App` and the CLI apply the choice.
  The Options dialog's OK also stops dropping the bug-report reply-to address, which it had been
  losing on every save.
- **The checklist tools** learn `NAMESPACE_ONLY`: `mex` is registered as the head of
  `mex.getCompilerConfigurations`, and MATLAB's `mex` command stays not implemented.
  `matlab-builtin-coverage.md` moves `calllib`, `libisloaded` and `unloadlibrary`: 431 of 514
  builtins, 1,146 of 2,024 callables.
- **Fixtures:**
  - `shrlib_load` is re-recorded with 26 more rows (`mexext`, the refusals, a prototype named by
    text, the `calllib` refusals, the command form of `unloadlibrary`), and its six function-count
    rows are `div=ADR0181`; every line agrees or is stamped, and none is pending on I8;
  - `shrlib_types` is re-recorded with its struct-return row `div=ADR0181`, and now runs to its
    end, with its conversion rows pending I9;
  - `shrlib_structs` and `shrlib_pointers` are re-stamped pending I9 at stage I8's refusal;
  - `net_jgs_smoke` gains five JGS rows.
- **Tests:** `SharedLibraryM181Tests` (19):
  - the parser against both R2025b prototype files;
  - `addheader`, the warnings text, a skipped declaration;
  - the layout against the library's own offsets, signatures, the prototype-file round trip;
  - struct returns, scalars and strings, a crash, unload;
  - no compiler, the Options choice, `getCompilerConfigurations`;
  - four compiler-gated tests (header route with `mfilename`, `windows.h` and `addheader`,
    `cppfailure`, the header beside the library), skipped with the reason where no compiler is
    found.

  `UserSettingsFormatTests` round-trips `CCompiler`.

## Consequences

- A C library loads from its header on any machine with Visual Studio's C++ tools or MinGW-w64, and
  from a prototype file with neither. The lanes need no compiler: the parser is tested on committed
  preprocessor output.
- `windows.h` costs a quarter of a second, not ten.
- `calllib` is usable for numeric, string and enum interfaces now. Pointer arguments wait one stage,
  and say so by name.
- A prototype file written by either engine loads in JGraph. One JGraph writes loads in R2025b once
  R2025b has built its thunk for that header.

## Divergences

- **A function returning a struct by value is loaded and called.** This is an extension; the user
  counts it as adding to MATLAB rather than departing from it. R2025b lists it in `notfound` and
  warns `MATLAB:loadlibrary:InvalidFunctionReturnType`. JGraph calls it through the x64 hidden return
  buffer and answers a MATLAB struct, so `jgtestlib` loads 86 functions where R2025b loads 85.
- **The warnings text holds the named headers' warnings only.** It uses R2025b's sentences and
  layout. R2025b's also holds its parser's complaints about system headers (`corecrt.h`,
  `setjmp.h`, the intrinsics headers) and the thunk compiler's output lines, neither of which JGraph
  has. Line numbers are JGraph's preprocessor's.
- **No thunk library is built.** `thunkfilename` is accepted and names nothing. A written prototype
  file names the thunk R2025b would build, so R2025b can use the file once it has built that thunk
  itself.
- **`mfilename` with a folder writes into that folder.** R2025b writes into the current folder
  whatever folder it is given (plan step 0, finding 9).
- **`mex.getCompilerConfigurations` answers structs with the class name `mex.CompilerConfiguration`.**
  They are not R2025b's objects, and their `Details` describe the preprocessing `loadlibrary` runs,
  not MEX builds. `'Supported'` answers what is installed. The MinGW and C++ names (`MinGW64 Compiler
  (C)`, `mingw64`, `MSVCPP170`) are not measured here: this machine has no MinGW and the probe asked
  for C only.
- **Visual Studio 2026 is found.** R2025b's compiler list asks `vswhere` for 17.x only.
