# ADR 0319 — The Excel client for the Production Server

## Status

Accepted, 2026-11-07. Compiler and SDK plan, stage K17f (decisions KO4 and KO9; KO11, the Function
Wizard, still open).

## Context

R2025b's `compiler.build.excelClientForProductionServer` (and `mcc -W mpsxl:<name>,<class>`) builds an
Excel add-in whose worksheet functions do not run MATLAB code themselves but call a deployed archive on
a MATLAB Production Server over HTTP: it generates a C# source (`<Class>.cs`, 180 KB for the probe's
eighteen functions) over MathWorks' .NET client assembly, compiles it with the .NET Framework's
`csc.exe` with that assembly embedded as a resource and Excel's interop assembly from the GAC, and
registers the result for the current user through `regasm /regfile` and `reg import`; a `.bas` module
declares the worksheet functions, and `compiler.package.excelClientForProductionServer` wraps the DLL,
the module and MathWorks' `ServerConfig.dll` (a COM add-in with a ribbon button that writes the server's
address under `Software\MathWorks\MATLAB Builder EX\2.5`) into a prebuilt installer with `_config.txt`.
Stage K17a recorded all of it (`recordings/k17_excel.out.txt`: the options' refusals, seventeen client
builds, ten `mcc -W mpsxl` forms, thirteen packaging calls and every generated text; `k17_mps.out.txt`:
the client reflected and every function called against `jgraph-mps` behind a logging proxy, so the
wire format is on record request by request; `k17_payload.out.txt`: the installers' resources;
`k17_strings.out.txt`: the .NET string heaps). Decision KO4 rules out the .NET Framework and KO9
(2026-11-06) makes JGraph's client native: a second prebuilt template beside `jgxl.dll`, stamped by
the build, its worksheet functions calling the server from native code - so a client machine needs
neither a JGraph Runtime nor any .NET.

The recording also shows three slips in R2025b's `ExcelClientForProductionServerWriter`: with
`ReplaceExcelBlankWithNaN` on alone the switch is dropped from the `mcc` line (the method assigns
`dataMarshallOption` and reads `dataMarshallOptions`), with `ConvertExcelDateToString` on alone the
build fails on that undefined variable (`date`), and with any switch over two or more files the
comma that should separate the files goes in front of the whole `class{}` group, which `csc` then
refuses (`nan`, `numdate`, `blankdate`, `allswitches`: `COM_SYSTEM_ERROR`). Only `onefileall` (one
file) and the hand-written `mpsxl_group` form build with switches.

## Decision

- **The options and the build are transcribed** (`JgsBuiltins.CompilerExcelClient.cs`,
  `JgsBuiltinClasses.Compiler.cs`): `compiler.build.ExcelClientForProductionServerOptions` with
  R2025b's thirteen properties in its order (`GenerateVisualBasicFile` on by default, the four
  marshalling switches off), from a Production Server archive's results (its `FunctionFiles` and the
  one `.ctf` among its `Files`; another build's results refused as `notAProductionServerResult`) or
  from files and an archive (`ServerArchive`: text, nonempty, a file that exists as given, `.ctf`
  supplied when the text has no extension and any other refused as `notACTFFile`), the names set in
  sorted order; `compiler.build.excelClientForProductionServer` writing the mcc line as the writer
  means it - `-W mpsxl:<name>,<class>,version=<v> -T link:lib -d`, `-v`, `-b`, `-g`, the files, then
  `class{<Class>:<file> -switch ...,<file> -switch ...}` with every switch that is on for every file
  (the three slips not copied: `docs/MATLAB_Behavioral_Fixes.md`) - and its postmcc: `Results.Files`
  the DLL, then under `GenerateVisualBasicFile` the module and the workbook; `BuildType`
  `excelClientForProductionServer`.
- **`mcc -W mpsxl:<name>[,<class>[,version=]]`** reads as `-W excel` does (K7's parser, `MccTarget.ExcelClient`;
  `-B cmpsxl` gives a bare version; an empty name `COM_ERR_ILLEGAL_COMPONENT_NAME`), `-g` its debug build,
  `-T`, `-C` and `-c` changing nothing (there is no archive), and each `class{}` entry naming a file to
  export with its switches, the file among the files (one given twice is warned about, as R2025b warns:
  `mpsxl_group`), a switch not among the four passed over (`mpsxl_badswitch`). A name that is not an
  identifier is refused before anything is written.
- **The client DLL is a native template** (`native/jgxlmps`, `jgxlmps.dll`, 332 KB, static C runtime,
  `/Brepro`, checked in under `native/win-x64/jgxlmps`): `mcc` copies it to `<name>.dll` (no version in
  the name, as R2025b's) and stamps it with the type library `ExcelTypeLib.WriteClient` writes (a dual
  interface `I<Class>` with one method per function, `HRESULT f([in] long nargout, [in, out] VARIANT*
  out..., [in] VARIANT in...)`, no `nargout` for a function without outputs, no `MWFlags` and no `help`,
  `varargin` as `V0`.. up to 29 inputs in all, each input optional, since the module passes them straight
  as R2025b's `params object[]` takes them; its ids derived apart from an add-in's), the configuration
  `JGRAPH_EXCEL_CLIENT` (`AddIn=`, `Class=`, `Version=`, `Function=<name>:<switches>`) and the version
  block. The object is served as jgrt's COM objects are (ADR 0316): a vtable of 520 slots with a thunk
  per member and `ITypeInfo::Invoke` behind `IDispatch::Invoke`. Each call: the `[in]` VARIANTs up to the
  first left-out one (VBA's missing optional, .NET's `Type.Missing`) converted as R2025b's client
  converts them - a blank 0 (NaN under `ReplaceExcelBlankWithNaN`), a DATE its datenum (its text under
  `ConvertExcelDateToString`), a Range read through its `Value`, an array of Variants a double matrix
  when every element is a number, a logical one when every element is a boolean, else a cell, column-major
  - written as MATLAB Production Server's `MATLAB_Params` (type, dimensions, the payload; no memory
  layout and no `isComplex`, as the .NET client omits them), posted over WinHTTP to
  `<ServerURL>/<function>` with `Content-Type: application/x-google-protobuf` and `nargout` the number of
  declared outputs, and the `MATLAB_Result` read back into the `[in, out]` slots: a scalar its VARIANT
  type, an array a zero-based two-dimensional SAFEARRAY, a logical `VARIANT_BOOL`, a char row a BSTR, a
  cell an array of Variants; a NaN 0 under `ReplaceNaNToZeroInExcel`, a double a DATE under
  `ConvertNumericOutToDateInExcel` when the value less the bias is one OLE holds; a failure as text in
  the first output (`MATLABException:<identifier>`, `WebException:<status reason>`,
  `WebException:ConnectFailure`, `Exception:<message>`, `MATLAB error thrown : MPS server url is not
  provided for <name>`), as R2025b's client answers them.
- **Where the server is:** `HKEY_CURRENT_USER\Software\JGraph\Excel Client\<name>` (then the machine's
  key): `ServerURL` (`http://host:port/<archive>`), `timeoutSeconds` (120), `responseSizeLimit` (64 MB)
  and `certificateFile` (a PEM an HTTPS server's certificate must equal; the chain's authority is not
  asked about, and the body is not sent until the certificate matched); the `Logging` subkey (`Enabled`,
  `LoggingLevel` 0/1/2, `LogFileFolder`) switches on a log in `<folder>\<name>-<date>.log`, one line per
  event in the shape of R2025b's trace listener's.
- **The module and the workbook:** `ExcelVisualBasic.ClientModule`, R2025b's client module to the
  character (no `InitModule`, the versionless ProgID `<name>.<Class>`, `varargin` declared and `V0`..
  passed straight, the signature's continuation under its first parameter, the call's one column before
  its first argument, the commented call marked `  '`), and the add-in workbook `<name>.xlam` written as a
  file (decision KO12); both written for every build and listed under `-b` alone, as R2025b lists its
  `.bas`.
- **The server configuration add-in is JGraph's** (`native/jgxlconfig`, `jgxlconfig.dll`): a COM add-in
  for Excel (`IDTExtensibility2`, `IRibbonExtensibility`; ProgID `JGraph.ServerConfig`, registered with
  Excel's add-in entry `Software\Microsoft\Office\Excel\Addins\JGraph.ServerConfig`, LoadBehavior 3) whose
  "Configure Production Server" button on the Add-ins tab opens a dialog that writes the settings above
  for the client chosen (the clients configured so far listed) and the logging; the same dialog opens
  without Excel through `rundll32 jgxlconfig.dll,JgxlConfigDialog`.
- **The installer is JGraph's template** (`native/jgxlsetup`, `jgxlsetup.exe`): `compiler.package.
  excelClientForProductionServer` and `compiler.package.ExcelClientForProductionServerOptions`
  transcribed (the results or files forms, `'Options'`, `ClientLibrary` and `ServerArchive` existing
  files of their extensions, `InstallerName` `<library>ExcelClientInstaller`, `InstallerIcon`,
  `MaxResponseSize` 750, `ServerTimeOut` 60 an integer above zero, `ServerURL`, `SSLCertificate`,
  `Version` `1.0` of numeric parts, `OutputDir` `.\<library>ExcelClientInstaller`; the warning when the
  files leave the client library out), the payload a stored zip (`JGRAPH_INSTALL_BUNDLE`; the files given,
  a folder under its own name, `jgxlconfig.dll` among them as R2025b's `ServerConfig.dll` is), R2025b's
  `_config.txt` text key for key (`JGRAPH_EXCEL_CLIENT_CONFIG`: `CTFFile`, `CTFArchive`,
  `AutomationAddinDll`, `responseSizeLimit`, `timeoutSeconds`, `AutomationAddin`, `ServerInfo`,
  `certificateFile`), the icon at R2025b's six sizes and the version block. Run, the installer asks where
  to install and the server's address (`ServerInfo/<archive>`), unpacks the files, registers the client
  and the configuration add-in through their own `DllRegisterServer` (for the current user, or machine-wide
  when elevated and asked), writes the settings, tells Excel to open the `.xlam` when there is one (the
  `OPEN` entry Excel's own Add-Ins dialog writes) and leaves an entry in Apps & Features; `/quiet`
  installs with the defaults, `/uninstall` undoes it. By the plan's house rule the installer is inspected
  and never run here.
- **Fronts:** `mcc -?` lists the client; the catalog's four rows; dispatch sweep 41 (the four names go
  to a shadowing file under every pattern; the table and the output counts regenerated).

## Measured

- **The wire format** (`ExcelClientTests`): the client built from the template and driven through its own
  class factory in this process against a server of the test's own sends R2025b's bytes for `k17_one(2)`
  and the recorded shapes (`k17_mps` requests 3, 7, 11, 19, 20, 21, 22, 23, 24, 25, 27, 52: a double, two
  arguments, no outputs, a 2-by-2 double matrix column-major, a blank, a left-out argument, a date as a
  datenum, a boolean, an int32, a row with a blank, a text, a blank in the middle), and lands the recorded
  replies in the cell as R2025b's client does (3.5, `[1 2; 3 4]` as a zero-based double[2,2], a row,
  `'hello'`, an int8 `-5`, logicals, NaN, a datenum, a MATLAB error as `MATLABException:<id>`, a 404 as
  `WebException:Not Found`, no server as `WebException:ConnectFailure`, no address as R2025b's text) and
  as JGraph decides where R2025b's cannot (Divergences); the switches: a blank sent as NaN, a date as its
  text, a NaN back as 0, a datenum as a date, a row of both as Variants.
- **The module** to the character (`fromresults.file.k17mps.bas`, eighteen functions;
  `mpsxl_default.file.k17x.bas`; `mpsxl_group.file.k17xg.bas` through a build).
- **The builtins** (`ExcelClientBuiltinTests`, 4): the options' derived names and every recorded refusal
  (`copts.*`, `cset.*`, `cbuild.*`, `popts.*`, `pset.*`), a build from a server archive's results and from
  files (`Results.Files` the DLL, the module and the workbook; the DLL alone without `-b`, the module
  written all the same), the verbose line with the `class{}` group as the switches mean, the switches
  in the DLL's configuration, `mcc -W mpsxl` in every recorded form and the installer's configuration,
  payload entries and version. `MccCommandLineTests` expects the client built.
- **JGraph's run** (`run-k17.ps1`, now over the whole probe; `recordings-jgraph/k17_*.out.txt`,
  `k17_differences.txt`): excel 1,004 keys, 426 agree, 578 recorded divergences, 0 unexplained; com_preinit 26, 21, 5, 0; com 559 keys, 508 agree, 51 recorded divergences, 0 unexplained; com_extra 1,076 keys, 1,021 agree, 55 recorded divergences, 0 unexplained; cells 82 keys, 11 agree, 71 recorded divergences, 0 unexplained; hosting 55 keys, 37 agree, 18 recorded divergences, 0 unexplained; mps 355 keys, 153 agree, 202 recorded divergences, 0 unexplained; payload 230 keys, 59 agree, 171 recorded divergences, 0 unexplained; pe 2,452 keys, 1,123 agree, 1,329 recorded divergences, 0 unexplained; registry 15, 10, 5, 0. The registry after the run is the one before.
  The mps recording: every request of the eighteen functions, the echoes, the makers, the two-output, varargout and
  optional calls goes out as R2025b's client sent it, byte for byte (the wire keys agree but for the error reply that
  names the work root and the oracle's own `two.array` artifact), and the cells' values agree for every row but the
  divergences named (the .NET rows, the settings' key, the log, and the five rows where JGraph decides differently).
  The payload recording: the installers' file names, subsystem, overlay and the default version block agree; the
  resources are JGraph's by name (the configuration text identical under its own name).
- **Tests:** `ExcelClientTests` (5), `ExcelClientBuiltinTests` (4), `MccCommandLineTests` expecting the client built, `OwnershipGateTests` over
  the new setters; the Deploy and Runtime areas with the ownership gate: 422 of 422

## Divergences

- **The client is native** (KO9): `jgxlmps.dll` stamped, no C# source, no `regasm` script, no .NET
  assembly to reflect (the `asm.*`, `type.*`, `deps.*` and string-heap rows of `k17_mps` and the whole of
  `k17_strings` have no counterpart); its registration is a ProgID, a class with its type library and
  JGraph's component category where regasm's is the assembly's codebase under `Wow6432Node`.
- **The three writer slips are not copied** and the five client builds R2025b fails or builds wrong
  build with their switches (`docs/MATLAB_Behavioral_Fixes.md`, section B).
- **Where the settings live:** `Software\JGraph\Excel Client\<name>` with `ServerURL` a text value, where
  R2025b's `ServerConfig.dll` keeps the URL as a value *name* with data 1 under `Software\MathWorks\MATLAB
  Builder EX\2.5\MPS Server Config\<name>`; the log file and its format JGraph's own.
- **What lands in a cell:** an empty output lands as Empty where R2025b's client fails on a null
  reference; a char matrix as one string per row where R2025b's runs the characters column-major into
  one text (`'acbd'`); a string output as its text where R2025b's refuses the type; an Excel error value
  and a struct output refused in JGraph's words; a double row under `ConvertNumericOutToDateInExcel` as
  Variants holding dates and, for a value no OLE date holds, the number.
- **The add-in workbook** is `<name>.xlam` written as a file (KO12) where R2025b writes `<name>.xla`
  through Excel and warns `COM_WARN_FAILED_CREATE_EXCEL_ADDIN`; `readme.txt` and `buildresult.json` are
  JGraph's.
- **The installer** carries `JGRAPH_INSTALL_BUNDLE` (a stored zip it unpacks itself) and
  `JGRAPH_EXCEL_CLIENT_CONFIG` where R2025b's carries `MW_COMPILER_EZDEPLOY_*` with `bsdtar.exe` and
  `zlib1.dll` to run; it is stamped with the `Version` given (R2025b's keeps 1.0.0.0) and JGraph's icon
  writer's icons; it installs per user or, elevated, for every user, where R2025b's needs administrator
  rights. The server configuration add-in is JGraph's, with its own ProgID and dialog.
- **The oracle's own artifact:** `two.K17Mps.array` differs because PowerShell's reflection unrolled a
  `double[]` into two arguments for R2025b's client where the direct COM call passes one SAFEARRAY.

## Not measured

- Excel's cells over the client, the ribbon button and the `OPEN` entry: this Excel 16 (Click-to-Run)
  sees no per-user COM registration (open item 248); the installer is never run here (stage K17g's client
  check installs it per user). `JgxlConfigDialog` is driven by nothing yet.
- In R2025b: `ConvertExcelDateToString`'s text, a Range holding dates among numbers, the response size
  limit's refusal, an HTTPS server with the packaged certificate, the installer's handling of
  `certificateFile`, a client with more than 29 inputs to one function.

## Still open

KO11 (the Function Wizard). Open items 248 and 249.
