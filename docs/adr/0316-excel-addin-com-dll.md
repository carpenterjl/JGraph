# ADR 0316 — The Excel add-in's COM DLL: a prebuilt template stamped by the build and served by jgrt.dll

## Status

Accepted, 2026-11-07. Compiler and SDK plan, stage K17c (decisions KO9, KO10 and KO13).

## Context

R2025b's Excel add-in is a native C++ COM DLL: its build generates a class over MathWorks'
`CMCLClassImpl` template with one method per function, an IDL with a dual interface (`MWFlags` at
DISPID 1, the functions from DISPID 2 in file order with `nargout` first and the outputs `[in,out]`,
`help` last), a coclass and a type library, a module definition and a resource script (resource 1
the type library, resource 2 the archive, a version block), compiles them with MSVC and MIDL, links
against `mclxlmain` and `mclmcrrt`, and registers the DLL for the current user through
`MWRegsvr /useronly` (ADR 0314, `recordings/k17_excel.out.txt`, `k17_pe.out.txt`,
`k17_registry.out.txt`). Its object refuses every call with arguments until `MWUtil.MWInitApplication`
has been given an Excel `Application`, and from a COM client answers the rows `k17com.ps1` recorded
(`k17_com.out.txt`): every Variant shape in, every class out, the `nargout`, `varargin` and
`varargout` rules, and each error's HRESULT (E_FAIL), source (the versioned ProgID) and description
(the message with `Error in => file at line n`).

Stage K17b (ADR 0315) built everything but that DLL and warned that it was pending. Decision KO9
(2026-11-06) chose how JGraph makes it: a prebuilt native template, `jgxl.dll`, shipped with the
runtime as the launchers are, copied and stamped by the build with a type library JGraph writes
through `oleaut32`'s `CreateTypeLib2` and the archive, its generic `IDispatch` reading the type
library and calling into `jgrt.dll`, so the building machine needs no compiler. KO10 chose JGraph's
own ProgIDs and component category for the utility library and MathWorks' class and member names;
KO13 allowed the builds to register per user and the checks to drive Excel, each with the registry
compared before and after.

## Decision

- **`native/jgxl`, the template** (`jgxl.cpp`, `jgxl.rc`, `jgxl.def`, `build.ps1`; checked in as
  `native/win-x64/jgxl/jgxl.dll` with `SOURCE.md`, 200 KB, static C runtime, `/Brepro`). It holds no
  code for the component: `DllGetClassObject` finds the JGraph Runtime of the version stamped into
  its `JGRAPH_RUNTIME` resource as the launcher finds it (`JGRAPH_RUNTIME_ROOT`, the registry key,
  the add-in's own folder for a runtime carried along) and then, as a Python package's `__init__.py`
  does, in a PATH folder holding `jgrt.dll`; loads that `jgrt.dll` and asks it for the class factory
  (`jgrtXlGetClassObject`); `DllCanUnloadNow` asks it whether any object of the add-in is alive.
  `DllRegisterServer` writes the registration itself from the type library alone, so a machine
  registers an add-in before the runtime is installed: machine-wide (`HKLM\Software\Classes`) when
  the caller may, else for the current user, which is what R2025b's `MWRegsvr /useronly` gives and
  what its recording shows (the two ProgIDs with `CLSID` and `CurVer`, the `CLSID` key with `AppID`,
  `Implemented Categories`, `InprocServer32` `ThreadingModel=Both`, `ProgID`, `TypeLib` and
  `VersionIndependentProgID`, the type library through `RegisterTypeLib[ForUser]`, JGraph's category
  `{C4E7A6D2-…}` "JGraph Excel Add-in Components"); `DllUnregisterServer` removes it from both hives.
- **`native/jgrt/src/com.cpp`, the generic COM object** (jgrt.dll rebuilt, 374 KB). One `Module`
  per add-in DLL loads its type library from the DLL's own resource, finds the coclass and its
  default interface, and reads the names as given from the documentation strings
  (`<addin> <M>.<m> Type Library`, `<Class> Class`): a type library's names are case-insensitive and
  the first spelling wins, so a coclass `K17Xl` in a library `k17xl` reads back as `k17xl`, in
  R2025b's libraries as in JGraph's (`accxl_1_0.dll`: library accxl, coclass accxl). Each object is
  a vtable of 520 slots: IUnknown's three, IDispatch's four and a thunk per further slot, each thunk
  reading its arguments as pointer-sized slots (the registers, then the caller's stack past the
  return address and the shadow space), which on x64 every argument of these interfaces is (`long`,
  `VARIANT_BOOL`, `BSTR`, an interface pointer, `VARIANT*`, and a `VARIANT` by value, passed as a
  pointer to a copy); the type library's FUNCDESC says what each slot's parameters are. So the dual
  interface is served whole: an early-bound client calls the vtable, a late-bound one's
  `IDispatch::Invoke` is `ITypeInfo::Invoke` driving that same vtable, as ATL's `IDispatchImpl` does,
  which gives R2025b's DISP_E_BADPARAMCOUNT and DISP_E_TYPEMISMATCH for free and turns a failed call's
  `IErrorInfo` into the EXCEPINFO a .NET client reads as a COMException with the add-in's source and
  message. Every member call goes to the engine by name (`ComInvoke`) with its arguments as VARIANTs,
  an `[in, out]` slot as `VT_BYREF | VT_VARIANT` onto the caller's own; the result is written to the
  `[out, retval]` parameter by its type. Objects the engine hands out (the flags) are wrapped the same
  way under their interface (`jgrtComWrap`) and recognized when they come back (`jgrtComUnwrap`,
  through a private IID). `ISupportErrorInfo` says yes for the interface. The template's
  `DllCanUnloadNow` is S_OK while no object of its module is alive and no factory is locked;
  jgrt.dll itself stays. An add-in may export 510 functions (the slots left).
- **`JGraph.Deploy`:**
  - **`ExcelTypeLib`** writes the type library with `ICreateTypeLib2`/`ICreateTypeInfo` in the shape
    R2025b's IDL declares (`plain.file.k17xl_idl.idl`): the library named for the add-in and versioned
    by the DLL's rule (a major of 0 is 1.0, as `k17v05 1.0 Type Library` and `k17vz 1.0 Type Library`
    are); `I<Class>` dual with `MWFlags` at DISPID 1 (get and put), the functions from DISPID 2 in file
    order (`HRESULT f([in] long nargout, [in, out] VARIANT* out..., [in] VARIANT in...)`, no `nargout`
    for a function without outputs) and `help([in] BSTR, [out, retval] BSTR*)` last; the coclass with
    its default interface. R2025b's library imports MathWorks' `mwcomutil.tlb` for `IMWFlags`; JGraph's
    declares `IMWFlags`, `IMWArrayFormatFlags` and `IMWDataConversionFlags` in the add-in's own library
    under JGraph's ids with MathWorks' member names (KO10; `Clone(IMWFlags**)` an `[in, out]` slot as
    MathWorks' IDL has it without attributes). Each function's documentation string is its H1 line,
    which `help` answers and VBA's object browser shows, where R2025b writes `Method f`. The reader
    (`ExcelTypeLib.Read`) answers the library, class, interface and ids, and each function's inputs,
    outputs and help from the vtable view of the dual.
  - **`ExcelComGuids`**: the ids are deterministic as R2025b's are (K17a: the library id from the
    add-in's name alone, the interface and class ids from the names and the version, the file list
    changing nothing), by JGraph's own derivation (a name-based UUID, SHA-1 under a JGraph namespace)
    since MathWorks' is not a plain hash of the names; JGraph's category, AppID and flags interface ids
    are constants.
  - **`ExcelAddIn.Write`** copies the template to `<addin>_<major>_<minor>.dll` and stamps it:
    `TYPELIB` 1, `RT_RCDATA` 2 the archive when embedded (else `<addin>.ctf` beside), the version block
    by the DLL's rule (`v05` and `vzero` 1.0.0.0, `v3456` 3.4.5.6, the file type VFT_DLL as stage K11's
    libraries), and `JGRAPH_RUNTIME`; `PeResources.Replace` and `PeResources.Read` handle any resource
    type and name; `CtfArchiveReader.Open` opens an add-in DLL through its resource 2. `Register` and
    `Unregister` call the DLL's own exports; `IsElevated` says whether the registration will be
    machine-wide; `RegistrationEnabled` is off in the unit tests (KO13: they register nothing).
    `DefaultTemplatePath` finds `jgxl.dll` beside JGraph's launchers or in the runtime's
    (`launchers\win64`, where `publish-runtime.ps1` now puts it).
- **`JGraph.Runtime`:**
  - **`ComServed` and `ComObjects`**: the engine's side of jgrt's handles. A wrapper lives as long as
    its COM references, which a client's garbage collector releases on a thread of its own, so a cached
    wrapper could die between a check and a use (the oracle's first run under JGraph did: a property
    put on a flags object whose wrapper the finalizer had just released). An object with an owner (the
    component's flags, the flags' two parts) therefore keeps a reference of its own on its wrapper,
    released when the owner is released; one without (a clone a client asked for) caches none.
  - **`ExcelComponent`**: what `CreateObject("<addin>.<Class>")` gets. It opens the add-in's archive
    (resource 2, or `<addin>.ctf` beside) as a `Component`, so every object of one add-in shares its
    session as R2025b's singleton module does (`count.secondobject` 3, `count.firstagain` 4). Members:
    `MWFlags` (a put makes the given flags object the component's, so putting the original back reads
    its own bias: `flags.put` 7 then 693960), each function with R2025b's measured rules (the first
    `nargout` slots filled and the rest left; `varargout` a row of Variants of the outputs past the
    fixed ones; a one-dimensional array of Variants spread into `varargin` and anything else one
    argument; trailing arguments not given left out, one missing in the middle refused), and
    `help(name)` the H1 line of the function named exactly, else `''`. An error is E_FAIL with the
    versioned ProgID as source and R2025b's description (`Custom text with an identifier\nError in =>
    k17_err.m at line 6`, through the same text as the C runtime's `mclGetLastErrorMessage`). The object
    works before `MWInitApplication`: JGraph's add-in needs no Excel to run.
  - **`ExcelFlags`, `ExcelArrayFormatFlags`, `ExcelDataConversionFlags`**: MathWorks' members and
    defaults (`InputArrayFormat` 1, `AutoResizeOutput` true, `DateBias` 693960, the rest 0 or false),
    `Clone`; the flags are held, their effect on the conversions is stage K17d's.
  - **`ExcelVariants`**: the measured conversions. In: every scalar type to its MATLAB class, a `BSTR` a
    char row (`''` 0-by-0), a `DATE` a datenum (the OLE date plus 693960), `CY` and `DECIMAL` doubles, a
    `DBNull` `[]`, a `VT_ERROR` cell its int32 code, a `SAFEARRAY` a matrix of its element class (a
    one-dimensional one a row), and an array of Variants at the top level a matrix when its elements
    are numbers of one class (an empty element 0), a logical array when all are booleans, a char
    matrix when each is one character, else a cell, a nested one always a cell. Out: a value's class
    to its VARIANT type, a matrix to a SAFEARRAY with every lower bound 1 as VBA's are
    (`out.doublerow` `[1..1,1..3]`), a cell to one of Variants (an empty cell an empty array), a char
    matrix to one string per character, an empty value and NaN (under the default flags) to nothing,
    -0 to 0. Range objects, the flags' effect and MathWorks' value classes are stage K17d's and are
    refused with a message naming the stage.
  - **`NativeComCalls`** and the three new entries of the bridge's table (`ComCreate`, `ComInvoke`,
    `ComRelease`).
- **`JGraph.Scripting`**: `mcc -W excel` fills the template path, stamps, and registers the add-in as
  R2025b's mbuild does, with R2025b's `COM_WARN_PER_USER_REGISTRATION` when the process is not elevated
  (naming JGraph where R2025b names MATLAB); the pending warning is now for the `.bas` alone (stage
  K17e); more than 510 functions is refused (`JGraph:mcc:excelAddInFunctions`).
- **Met on the way, measured in R2025b (2026-11-07) and fixed:** `arrayfun` took a char array as the
  start of its option tail, where R2025b walks it character by character (`arrayfun(@(c) double(c), 'ab')`
  is `[97 98]`, `arrayfun(@(c) c, 'ab')` is `'ab'`, a char matrix keeps its shape, `''` gives a 0-by-0,
  a trailing `'uniformoutput'` with no value is one more array and refused as `MATLAB:arrayfun:UnequalSize`
  in R2025b's words, `'UniformOutput'` alone is walked), and `num2cell` refused a char array where
  R2025b answers a cell of single characters in the array's shape (`''` a 0-by-0 cell). The probe's
  `k17_show` tripped over both.
- **The probe and the oracle under JGraph.** `probe_k17_excel.m` reads the registry through .NET's
  `Microsoft.Win32.Registry` where the engine runs no shell command, so JGraph's run records each build's
  registry delta as R2025b's did. `k17com.ps1` takes `-Dll` and `-Clsid` (the object through the DLL's
  own `DllGetClassObject`, no registry: the unit test's way), `-NoExcel` and `-UtilProgId ''`.
  `run-k17.ps1` runs the probe (whose builds register per user), the oracle in its two phases against
  the registered ProgID with an invisible Excel, `pe-resources.py` over every DLL, and the cleanup
  (`regsvr32 /u` over every add-in built, the registry compared with its state before), then
  `compare-k17.py` over the five recordings this stage makes.

## Measured

- **`run-k17.ps1`** (`recordings-jgraph/k17_*.out.txt`, `k17_differences.txt`): excel 1,004 keys, 259 agree, 745 recorded divergences, 0 unexplained (K17b: 202 agree); com_preinit 26 keys,
  15 agree, 11 recorded divergences, 0 unexplained; com 559 keys, 342 agree, 217 recorded divergences, 0
  unexplained; pe 2,452 keys, 720 agree, 1,732 recorded divergences, 0 unexplained (its keys collide per file
  stem, so it is compared by pattern); registry 15 keys, 10 agree, 5 recorded divergences, 0 unexplained. The probe's 28 add-ins register and unregister cleanly:
  the registry after the run is the one before plus JGraph's component category.
- **Tests.** `ExcelAddInServerTests` (4): the type library's interface parameter for parameter with
  R2025b's IDL and its ids' determinism; the DLL's resources and its archive read back; the served
  object's members in this process (nargout, varargin, varargout, 30 input and 26 output conversions,
  the errors, the flags, the shared session); and the K17a oracle itself run from PowerShell against
  the unregistered DLL through its class factory, 237 rows equal to R2025b's. `ExcelAddInTests` (5)
  for the DLL now written. `MatlabArrayBuiltinTests` for `arrayfun` and `num2cell` over char arrays.
  The Deploy, Runtime and touched Scripting areas: 439 of 439.
- **Found and fixed on the way:** a `switch` expression's arms take their best common type before
  boxing, so every scalar answered as a double until each arm was boxed itself; `Clone` declared with
  a retval was refused by the client (DISP_E_BADPARAMCOUNT) where MathWorks' IDL declares the slot;
  the wrapper lifetime race above.

## Divergences

- **The DLL is JGraph's template**, 200 KB importing kernel32, ole32, oleaut32 and advapi32, where R2025b's is compiled per build and imports mclxlmain and mclmcrrt; no C++ source, IDL, MIDL output, module definition, resource script, import library or exports file is written (KO9).
- **The type library is JGraph's**: its ids its own derivation; the flags interfaces declared in it under JGraph's ids rather than imported from MathWorks'; each function's documentation string its H1 line; the resources language-neutral; the version block's file OS and type set.
- **Registration**: the component category and AppID are JGraph's (KO10); the type library is registered for win64 alone; four Interface keys where R2025b's registration writes one; `regsvr32` without elevation registers for the current user where R2025b's exits 5 and needs `MWRegsvr /useronly`.
- **The object runs before `MWInitApplication`**; R2025b's refuses every call until it has an Excel Application.
- **A refusal of the call's own carries JGraph's text** (which argument was left out, what cannot be converted); R2025b's carries none, or the last MATLAB error's text (ADR 0314).
- **int64 and uint64 cross** as VT_I8 and VT_UI8 where JGraph holds them exactly, a string answers its text, an empty array crosses as an empty double of its shape, a value no VARIANT holds (datetime, duration, categorical, a handle, a table) is refused with a message; R2025b refuses an int64 in with no text and answers a type mismatch value for the rest.
- **A complex value with a zero imaginary part crosses as a double**; R2025b answers an MWComplex (the engine keeps it real, ADR 0266).
- **The `.bas` is still pending** (stage K17e), and said so by `JGraph:mcc:excelAddInPending`; Range objects, the flags' effect and MathWorks' value classes are stage K17d's.

## Not measured

A figure shown from inside Excel's process (the window host's thread), Excel's own unload, a re-entrant recalculation (stage K17g); an early-bound VBA client (the vtable path is exercised through `ITypeInfo::Invoke`); a build with a hosted runtime installed through the registry key.

## Still open

Open item 246 (what this stage leaves), stages K17d to K17g.
