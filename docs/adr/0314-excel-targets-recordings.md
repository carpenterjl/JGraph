# ADR 0314 — Recordings of R2025b's Excel targets: the builds, the conversion rules, the client's wire

## Status

Accepted, 2026-11-06. Stage K17a of the compiler and SDK plan (`docs/plans/compiler-and-sdk-plan.md`,
gitignored; revision 27). No engine code changes: the stage is the measurement the rest of K17
(K17b to K17g) is built against, under decision KO13 (per-user COM registration and an invisible
Excel, each with cleanup).

## Context

K17 gives JGraph MATLAB Compiler's Excel add-in and MATLAB Compiler SDK's Excel client for the
Production Server. The research of 2026-10-27 (plan section 4.8) read R2025b's sources and the
add-ins it had built, and found that the one thing the sources do not say is the conversion
between Excel's Variants and MATLAB's values: it lives in compiled code (`mwcomutil.dll`,
`mclcom`), and can only be learned by calling an R2025b-built add-in. The `.bas` for `varargin`,
`varargout` and a function with no outputs, the `mpsxl` client's files and its wire format were
unrecorded too. Every rule JGraph implements is measured in R2025b, never written from memory, so
the stage records all of it first.

## Decision

### What is recorded, and how

`tools/matlab-checklist/compiler-probes/record-k17.ps1` runs five steps over one scratch folder,
with the user's registry (the names under `HKCU\Software\Classes` and its CLSID, TypeLib,
Interface, Record, AppID and Component Categories keys, `Software\MathWorks` and Excel's own key)
listed before the first step and compared after the last:

- **`probe_k17_excel.m`** under `matlab -batch -noFigureWindows`, over `src/k17`: eighteen
  functions, one per argument shape (none, one, several, two outputs, optional arguments,
  `varargin`, `varargout`, both, an `arguments` block, no outputs, errors of each kind, a plot
  invisible unless asked, an H1 line with quotes and non-ASCII text, an echo of what arrived, a pass
  through, a persistent counter, a maker of a value of every class). It records
  `compiler.build.ExcelAddInOptions` (every property's values and refusals, the derived names, save
  and load), the builds (plain, and the same again for the GUIDs; with the `.bas`; the archive
  separate; a debug build; versions `2`, `0.5`, `3.4.5.6`, `abc`, `1.2.3.4.5`, `70000` and `0`;
  the names derived from one file; an additional file; a `.p` file; a class named like the add-in;
  a build left to its own output folder), `mcc -W excel` in twenty forms, and for each what it
  printed, its `Results`, its files, every generated text file (`.bas`, `_install.bat`,
  `readme.txt`, `GettingStarted.html`, `buildresult.json`, the IDL, the C++ class and module, the
  `.def` and `.rc`, the logs) and what it added to the user's registry, read through `reg.exe` root
  by root. Then the same for the Excel client: `compiler.build.productionServerArchive` over the
  functions, `ExcelClientForProductionServerOptions` and `excelClientForProductionServer` from the
  `Results` and from files, each marshalling switch alone and together, `mcc -W mpsxl` with its
  `class{...}` group in ten forms, and `compiler.package.excelClientForProductionServer` and its
  options with each argument and refusal.
- **`src/k17client/k17com.ps1`**, the conversion oracle, in a PowerShell of its own: the add-in
  R2025b built (`k17xl.K17Xl.1_0`) created by its ProgID and every function called with every
  Variant shape, under every `MWFlags` setting on a fresh object each, every class `k17_make`
  answers read back, outputs written into worksheet ranges, the utility library's `MWPack`,
  `MWUnpack`, `MWDate2VariantDate`, `CreateRemoteObject`, `MWInitApplication` and
  `MWTerminateApplication`, each other build's ProgID, and every error's HRESULT, source and
  description. The rows before `MWInitApplication` run in a process of their own
  (`k17_com_preinit.out.txt`).
- **`k17cells.ps1`**: an invisible Excel, a scratch workbook, the `.bas` imported through the VBA
  project object model where Excel trusts it, formulas written, calculated and read back, the
  workbook closed unsaved.
- **`k17mps.ps1`** with **`k17proxy.py`**: JGraph's own build of the functions as a server archive,
  served by `jgraph-mps` behind a logging proxy that writes every request and reply as it went over
  the wire; R2025b's Excel client DLL reflected (its types, members, attributes, references and
  resources), its string heap read (`k17strings.py`), its server configuration written where
  `ServerConfig.dll` keeps it, and its worksheet functions called.
- **The payloads**: `k18-payload.py` over each installer the packager wrote (its `_config.txt` and
  `payload.zip` read from its resources), `pe-resources.py` over each built DLL (the type library,
  the archive and the version block as resources), `k17strings.py` over the .NET DLLs.
- **Cleanup**: everything added to the registry dumped whole, `regsvr32 /s /u` over every add-in
  built, the MathWorks configuration key removed, any windowless Excel of this run's ended, and the
  registry names compared with the first snapshot (`k17_registry.out.txt`).

`compare-k17.py <folder>` holds JGraph's recordings of the same names to R2025b's, recording by
recording, with KNOWN lists per recording that K17b to K17g fill; run against `recordings/` itself
every key agrees.

### What the oracle needed to work at all

- **`MWInitApplication` wants Excel.** A call with any argument fails with `E_FAIL` and no text until
  `MWUtil.MWInitApplication` has been given an Excel `Application`; a `Scripting.Dictionary` in its
  place is refused with `E_FAIL`; a method with no parameters (`k17_quiet`, `k17_none`) runs before
  it, and `help` and `MWFlags` answer. So the oracle runs an invisible Excel for its duration, which
  also lends the worksheet the Range rows need, and quits it with the workbook closed unsaved (KO13).
- **The direct syntax alone.** Of PowerShell's three ways to call a COM method, only the direct
  syntax (`$o.name(nargout, [ref]slot, input...)`, built as text and run with `Invoke-Expression`)
  keeps a two-dimensional array two-dimensional and surfaces the add-in's error text:
  `Type.InvokeMember` flattens a `double[2,3]` to a row of six, `PSMethod.Invoke` buries the text in
  a middle exception. A value that passed through a function's `$args` is a `PSObject`, which goes
  as an `IDispatch` of its own and is refused; a two-dimensional array written from a function
  without `-NoEnumerate` leaves as its elements. `[out] IMWFlags**` (Clone) takes a
  `DispatchWrapper` slot through `InvokeMember`; `[ref]` fails on it.
- **Three calls end the host.** A negative `nargout` (`CallComFcn` allocates `nargout` output
  slots with `_alloca`; a stack overflow), a call after `MWTerminateApplication` (an access violation
  in `mclFeval`, with the runtime's crash report), and `CoerceNumericToType` 1, a value `mwDataType`
  does not define (an access violation in `mclmxArray2Variant`; 2 and 5 are refused with `E_FAIL`).
  The oracle writes every line through as it goes, runs the first two last with Excel already quit,
  and leaves the third out. An `ErrorWrapper` inside an array, through PowerShell's own marshaller,
  ended the host once too and is left out (a Range holding an error cell is the row Excel produces).
- **`CallComFcn`'s rules** (R2025b's `mclcomclass.h`, read in place, and measured): a Variant that is
  `VT_EMPTY`, `VT_ERROR` with `DISP_E_PARAMNOTFOUND` or a null reference is a defaulted argument,
  allowed at the end of the list alone (`k17_opt(1, missing, true)` is `E_FAIL`; `k17_opt(1, 'b',
  missing)` is `nargin=2`); a `varargin` parameter arriving as an array of Variants is spread
  (`k17_varin([1,'a',true])` is `nargin=3`) and an empty one is dropped (`nargin=0`); `nargout` 0
  destroys the first output (`k17_two` with 0 gives two nulls); `varargout` is one cell from the
  slot's position on (`k17_varout` with `nargout` 3 is a 1-by-3 array of Variants); a refusal of
  `CallComFcn`'s own carries no text of its own but the last MATLAB error's (`stale.*`: right after
  `k17_err`, a defaulted middle argument reports "Custom text with an identifier"; after a successful
  call, nothing).
- **A helper's quote.** `k17_show`, the function the oracle's echo rows rely on, first listed a
  complex array's elements with `v(:)'` and printed every imaginary part with its sign flipped: a
  trailing quote is a conjugate transpose. `v(:).'` now; the first probe run was stopped and rerun
  with the fixed helper so the add-ins carry it.

### What was found and where K17 goes with it

- **The GUIDs are deterministic.** Two builds of one project agree on the interface, library and
  class ids; the library id depends on the add-in name alone, the interface and class ids on the
  names and the version (`k17m,K17M` at 1.0 and 2.0 share the LIBID and nothing else); the file
  list and its order change nothing. The derivation is not a plain hash of the names (MD5 and SHA-1
  of fifty spellings, raw and under the standard UUID namespaces, matched nothing), so K17c derives
  JGraph's own GUIDs deterministically from the same inputs, a recorded divergence.
- **The H1 line is mangled on its way to `help`.** The C++ help map holds the H1 line byte by byte
  (its UTF-8 widened as if each byte were a character: `€` is three characters) and `% o` in it is
  eaten as a format (`100% of` becomes `100<newline>`). JGraph writes the H1 line as it is: a MATLAB
  bug not copied (correctness outranks parity); K17c, which writes the help map, adds the row to
  `MATLAB_Behavioral_Fixes.md`.
- **Excel refuses the `.bas` import.** Without the trust setting (`AccessVBOM`, which K17 does not
  change) `Workbook.VBProject` is `Nothing` and `VBComponents.Import` cannot run; `VBE.VBProjects`
  counts 0. The cells of Excel itself move to K17e, where the `.xlam` JGraph writes is opened by an
  invisible Excel with `AutomationSecurity` low and the same formulas (`k17cells.ps1`) are calculated.
- **The Excel client is a .NET Framework assembly compiled at build time** from a generated C# source
  (`<Class>.cs`, left in the output folder) by the Framework's `csc.exe`, signed with
  `MathWorksKey.snk`, with MathWorks' .NET client assembly embedded as a resource and resolved by an
  `AssemblyResolve` handler, `Microsoft.Office.Interop.Excel` 15 from the GAC and
  `Microsoft.VisualBasic`; a `.reg` beside it registers it per user through `mscoree.dll` with the
  `Programmable` category. Its worksheet functions are `void f(int nargout, ref object out..., params
  object[] inputs)` (no outputs: `void f(params object[] inputs)`), a `Range` input read through
  `Value`, `Type.Missing` ending the inputs. Each call makes or reuses an `MWHttpClient` over the
  configured URL and posts protobuf (K16's wire format, `MATLAB_Params`) to `<url>/<function>` with
  `User-Agent: MathWorks.MATLAB.ProductionServer.Client.MWFevalHandler`, `Content-Type:
  application/x-google-protobuf` and `Expect: 100-continue`; a failure of any kind is a `MessageBox`
  (`MATLABException:<identifier>`, `WebException:<text>`, `Exception:<text>`) and the cell gets the
  box's text. The server URL is a value *name* with DWORD data 1 under `HKCU\Software\MathWorks\MATLAB
  Builder EX\2.5\MPS Server Config\<add-in name>`, a `.ctf` suffix dropped and `http://` prefixed when
  absent, with `timeoutSeconds` and `responseSizeLimit` beside it (machine keys consulted after the
  user's); `Logging\{Enabled, LoggingLevel, LogFileFolder}` switches on a `FileLogTraceListener`
  whose text the recording holds. The three writer slips of the plan are measured: a switch alone is
  dropped from the `mcc` line, `ConvertExcelDateToString` alone is "Unrecognized function or variable
  'dataMarshallOption'", and two or more files with any switch put a comma in front of the group and
  the build fails in `csc`; one file with switches builds. The `.bas` is written with or without `-b`
  (`-b` lists it in `Files`). K17f keeps the shape and the wire and makes the client native (KO9).
- **R2025b registers per user on this machine** (its build warns so), through its own
  `runtime\win64\mwregsvr.exe /useronly`; plain `regsvr32` of the same DLL fails with exit 5 (access
  denied: `DllRegisterServer` wants the machine hive), `regsvr32 /u` works. Under
  `HKCU\Software\Classes`: `<addin>.<Class>` with `CLSID` and `CurVer`, `<addin>.<Class>.<maj>_<min>`
  with `CLSID`, the CLSID with `AppID`, `InprocServer32` (`ThreadingModel=Both`), `ProgID`,
  `VersionIndependentProgID`, `TypeLib` (with `Version`) and `Implemented Categories\{5D003098-...}`,
  the `TypeLib\{libid}\1.0` key (`0\win32` and `0\win64` both the DLL, `FLAGS` 0, `HELPDIR` the
  folder) and the `Interface\{iid}` key (`ProxyStubClsid32`, `TypeLib`). After the builds of a whole
  run, `regsvr32 /u` over every DLL left 151 keys (CLSID, TypeLib and Interface keys of add-ins
  registered more than once, and all of the .NET clients', which have no unregister of their own);
  the cleanup sweeps every key added since the first snapshot whose contents name a `k17` component,
  and the final comparison is +0 −0. K17c registers the same keys and unregisters them whole.
- **The version rule** is `mcc`'s (`COM_ERR_INVALID_VERSION`: "at most four integers (0 to 65534),
  separated by '.'"), not the options class's (any non-empty text passes `AddInVersion`); the DLL
  name takes the first two parts with `0` for a missing second and `1_0` for a leading `0`
  (`0` and `0.5` are `_1_0`, `2` is `_2_0`, `3.4.5.6` is `_3_4`).

## Consequences

- K17b to K17g have their oracle: every rule they implement (the files a build writes, the DLL's
  name and resources, the interface, the registration, the conversion both ways, the error text,
  the `.bas`, the client's files and wire format, the installer's payload) is a key in these
  recordings, and `compare-k17.py` is the gate that holds JGraph's to them.
- The oracle runs again whenever a question comes up: `record-k17.ps1 -Skip probe,cells,mps,payload,cleanup`
  needs only the registered add-in and about three minutes.
- Nothing registered stays: the cleanup step unregisters every add-in the probe built, removes the
  configuration key the client step wrote and compares the registry with its state before.

## Measured

- **`k17_excel.out.txt`**: 17 add-in builds (14 built, `abc`, `1.2.3.4.5` and `70000` refused by
  `mcc`), 23 `mcc -W excel` forms (three of them the GUID question), the server archive, 16 Excel
  client builds (11 built; `abc` refused, `date` the undefined variable, `nan`, `numdate`,
  `blankdate` and `allswitches` the comma slip), 10 `mcc -W mpsxl` forms, 13 packaging calls, and
  every options refusal, 119 refusals in all, in about 35,900 lines, with the registry's additions
  after each build (an add-in build adds its CLSID, TypeLib and Interface keys; a client build an
  entry under `Wow6432Node\CLSID`; the ProgIDs stood from the first run and appear in the cleanup's
  dump).
- **`k17_com.out.txt`**: 560 rows (every Variant shape in, every class out, the Range rows, the
  flags, the utility library, the errors, the other ProgIDs), and `k17_com_preinit.out.txt` the 27
  rows before `MWInitApplication`. Among them: a `DATE` arrives as a MATLAB datenum (`DateBias`
  693960 added), `CY` and `DECIMAL` as doubles, a `char` as `uint16`, a one-based 2-by-2 Variant
  array of numbers as a 1-by-4 double, a jagged array as nested cells, a blank cell as 0 and an
  error cell as its `int32` code, `MWStruct`, `MWComplex` and `MWSparse` both ways, `int64` refused,
  `string`, `datetime`, `duration`, `categorical`, `table`, a handle and an object answered as
  `VT_ERROR DISP_E_TYPEMISMATCH`, NaN as `VT_EMPTY` (or 0, or `#N/A`, by `ReplaceMatlabNaN`), an
  output into a Range resized to the value and written cell by cell, `TransposeOutput` honoured for
  ranges, `OutputAsDate` reading a datenum, `InputDateFormat` 1 giving `3/15/2024 10:30:00 AM`.
- **`k17_cells.out.txt`**: Excel 16.0 build 20527, `AccessVBOM` absent, `VBProject` `Nothing`, the
  import refused.
- **`k17_mps.out.txt`**: the client's types and members, its string heap, 52 requests and replies on
  the wire, its own log, and every worksheet function answering through `jgraph-mps` (a struct,
  `datetime`, `string` and complex answers are the client library's refusals, as K16 measured).
- **`k17_payload.out.txt`**, **`k17_pe.out.txt`**, **`k17_strings.out.txt`**, **`k17_registry.out.txt`**.

## Not measured

- Excel's own cells over the add-in (the `.bas` import is refused; K17e).
- A figure shown by the add-in (`k17_plot('on')` would raise a MATLAB Runtime window; the oracle
  asks for an invisible one).
- The GUID derivation itself (deterministic, source unknown).
- `MWInitApplicationWithMCROptions` with a string array (PowerShell's `object[]` is refused as
  "MATLAB Runtime option list must be a string array"), `MWEnumVararg` (not registered by R2025b's
  installer), `CreateRemoteObject` ("Class not registered" for the add-in's own ProgID).

## Divergences

None: the stage changes no engine code. The divergences K17 will carry are listed in the plan and
recorded by the stages that make them.
