# ADR 0318 — The Excel add-in's Visual Basic module and add-in workbook

## Status

Accepted, 2026-11-07. Compiler and SDK plan, stage K17e (decisions KO10 and KO12; KO11, the Function
Wizard, still open).

## Context

R2025b's `mcc -W excel ... -b` writes a Visual Basic module, `<name>.bas`, declaring one worksheet
function per exported function over the add-in's COM object, and then drives Excel through automation
to import that module into a new workbook saved as `<name>.xla`. On the recording machine the second
step failed (`COM_WARN_FAILED_CREATE_EXCEL_ADDIN`, "Details: VBProject"): Excel does not trust
programmatic access to its VBA projects unless a user sets the trust setting, which stage K17 does not
touch. Stage K17a recorded the modules of five builds (k17_excel: `bas.file.k17bas.bas` with every
argument shape, `v3456.file.k17v3456.bas`, `pfile.file.k17p.bas`, `mcc_default.file.k17m.bas`,
`mcc_twofiles.bas`) and found that Excel's cells over the add-in could not be recorded through the
`.bas` import for the same reason, so they were moved here. Decision KO12 (2026-11-06) chose to write
the add-in workbook as a file with no Excel, an `.xlam`, provided a spike proved Excel accepts such a
file; decision KO10 chose JGraph's own ProgIDs for the COM utility library the module creates.

The spike ran on 2026-11-07 (scratchpad `mkxlam.py`, since checked in as
`src/k17client/k17xlam.py`): an Office Open XML package of the add-in kind carrying a `vbaProject.bin`
written from scratch (a compound file holding a source-only VBA project) opened in an invisible Excel
16 as an add-in (`IsAddin` true, `HasVBProject` true), its worksheet function answered 6 in a cell and
42 through `Application.Run`. The format follows MS-CFB and MS-OVBA, with MathWorks' own
`FunctionWizard2007.xlam` read as a reference for the project's layout (the `dir` stream's records,
the two references, the document module's attributes, the `PROJECT` stream's lines).

Recording Excel's cells over R2025b's add-in then turned up two facts about hosting, measured by
`k17hosting.ps1` and kept as `recordings/k17_hosting.out.txt`, that shape what JGraph's module writes
and what this stage can measure (see Measured).

## Decision

- **The module is R2025b's, transcribed** (`JGraph.Deploy/ExcelVisualBasic.cs`): the three module
  variables (`MCLUtil`, `bModuleInitialized`, the class variable named for the class, with R2025b's
  trailing space), `InitModule` creating the utility library's `MWUtil` once and handing it Excel's
  `Application`, one `Function <name>(...) As Variant` per exported function in the build's order with
  every input an `Optional ... As Variant`, `varargin` as `V0`, `V1`, ... up to 29 arguments in all
  (`k17_varin` takes `V0` to `V28`, `k17_both` with one fixed input `V0` to `V27`) packed by the
  `pack` function written after the last function when any takes `varargin`, the outputs as one `Dim`
  line (`Dim s, d, p As Variant`, `varargout` among them), the component created by its versioned
  ProgID on first use, the call with `nargout` and the output variables before the inputs (no
  `nargout` for a function without outputs, which answers `Empty`), the first output answered, the
  two comment blocks for a function with several outputs showing the `Array(...)` form, and every
  error answered as `"Error in " & Err.Source & ": " & Err.Description`. A line longer than 89
  characters (the signature, the `pack` call) is wrapped at a space with two spaces and an underscore,
  the continuation indented four spaces (eight for the `pack` call), as R2025b's writer wraps; the
  rule was pinned by regenerating all five recorded modules to the character. CR LF line ends, a
  line end after the last line. Two things differ from R2025b's text, each measured (Divergences):
  the utility library's ProgID is JGraph's (`JGraphComUtil.MWUtil25.2`, KO10), and the objects are
  created with no server name.
- **The add-in workbook is written as a file** (`ExcelAddInWorkbook.cs`, decision KO12): `<name>.xlam`,
  an Office Open XML package whose workbook part has the add-in content type
  (`application/vnd.ms-excel.addin.macroEnabled.main+xml`), one empty sheet, the workbook window very
  hidden as an add-in's is, a minimal style sheet, and `xl/vbaProject.bin`. The project (`VbaProject.cs`)
  is source-only: `_VBA_PROJECT` with version 0xFFFF and no performance cache, so Excel compiles the
  modules when it opens the file; the `dir` stream's records (SysKind Win64, LCID 1033, code page
  1252, the project name `VBAProject`, the `stdole` and `Office` references every workbook project
  carries, since VBA's and Excel's are implicit, then each module's name, stream name, offset 0 and
  type); one compressed stream per module holding its source alone; the `PROJECT` stream with the
  protection state, password (none) and visibility fields encrypted as MS-OVBA 2.4.3 asks (checked
  against MathWorks' file: its `CMG` decrypts to a four-byte protection state, its `GC` to one byte);
  `PROJECTwm`. The modules are `ThisWorkbook` (the document module's attribute lines, no code) and
  `Module1` holding the `.bas` under an `Attribute VB_Name` line, the name Excel's own import gives a
  module file that has none. The compound file (`CompoundFile.cs`) is version 3 (512-byte sectors,
  the mini stream for streams under 4,096 bytes, a balanced directory tree in the file's order, no
  times) and is read back by the same class; the MS-OVBA compression writes copy tokens over each
  4,096-byte chunk and a raw chunk where the tokens would not fit. Every entry's time is fixed, so
  one add-in's workbook is the same bytes build after build.
- **The build writes both with `-b`** (`ExcelAddIn.Write`): `<name>.bas` and `<name>.xlam` beside the
  DLL, listed by `compiler.build.excelAddIn` where R2025b's `postmcc` lists them (the DLL,
  `_install.bat`, the workbook where the `.xla` would stand, the module, the separate archive, the
  page), named in `readme.txt`, and the page gains "Use the Add-In in Excel". The build no longer
  warns that the module is pending; the DLL-pending warning of a build without the template stays.
  The readme and the page say that Excel installed by Click-to-Run sees the machine's registrations
  alone, so for Excel `_install.bat` is run as an administrator (Measured).
- **No Function Wizard** (KO11 open; recommended none in K17): MathWorks' `FunctionWizard.xla` and
  `FunctionWizard2007.xlam` cannot ship and need `mwcommgr.dll`; the add-in works through its module's
  worksheet functions. A JGraph wizard is a later item (open item 248).
- **The oracle:** `k17cells.ps1` takes `-Xlam` (the workbook opened as an add-in, the formulas written
  into a second workbook, calculated and read back) beside K17a's `-Bas`; `k17hosting.bas.in` and
  `k17hosting.ps1` measure how an add-in reaches Excel (the activations from VBA with and without a
  server name and from the script's own process, the registry as Excel's process sees it, the runtime's
  DLLs and the add-in's loaded into Excel one Excel each); `k17xlam.py` wraps a module into a
  workbook with no Excel (the spike, kept standalone); `record-k17e.ps1` builds R2025b's add-in with
  its module, wraps that module, records the cells and the hosting rows and unregisters (`-Skip build`
  registers the add-in built before through `mwregsvr /useronly`); `run-k17.ps1` gains the `cells`
  step over the probe's `k17bas.xlam` and the hosting rows, with the runtime found through the registry
  key the launchers read (Excel is started by COM and inherits none of the script's PATH, so the key
  points for the run at a stand-in layout whose `bin\win64` is a junction to `jgraph.exe`'s folder,
  removed in the cleanup); `compare-k17.py` compares `k17_cells` and `k17_hosting`.

## Measured

- **The module.** `ExcelAddInTests`: `mcc -W excel:k17bas,K17Bas,version=1.0 -b` over the eighteen
  functions writes `bas.file.k17bas.bas` to the character but for the two differences; the
  `compiler.build.excelAddIn` with `AddInVersion` 3.4.5.6 writes `v3456.file.k17v3456.bas`; two files
  write `mcc_twofiles.bas`; one file `mcc_default.file.k17m.bas`; `Wrap` keeps 89 characters and
  breaks the 90th word.
- **The workbook.** `ExcelAddInWorkbookTests` (4): the compression round-trips the recorded module
  (12,095 bytes to under half), a run, noise (a raw chunk) and nothing, and the encryption decrypts
  MathWorks' `CMG` and `GC`; the compound file round-trips streams under, at and over the cutoff, an
  empty one and a nested storage, in the file's order; MathWorks' `FunctionWizard2007.xlam` reads back
  through the same reader (`MLFProject2007`, its five modules, its two references, its `Module1`
  source); JGraph's workbook carries `ThisWorkbook` and `Module1` with the module's text, its project
  stream's lines and encrypted fields, its `_VBA_PROJECT` and `PROJECTwm`, and is the same bytes twice.
  Excel's acceptance: the spike above, and `run-k17.ps1`'s cells step (`xlam.open`: IsAddin true,
  HasVBProject true).
- **R2025b's cells** (`recordings/k17_cells.out.txt`, R2025b's own module as written, its add-in
  registered for the current user by its build): every one of the 73 formulas answers "Error in
  VBAProject: Class not registered on local machine", and `=k17_count()+0` `#VALUE!`.
  `recordings/k17_hosting.out.txt` says why, in two parts:
  - `CreateObject(progid, "localhost")` (and `"."`) is a DCOM activation, which reads the machine's
    registrations alone: the add-in registered under `HKCU` answers VBA error 463 "Class not registered
    on local machine" and the script's own process `0x80040154 ... from machine localhost`, while the
    utility library, registered machine-wide by MATLAB's installer, is created either way. MathWorks'
    design is the machine-wide one: its AppID `{64571771-615C-406E-82F2-1B02209353EE}` is registered
    under `HKLM` with `DllSurrogate`, so an add-in registered as an administrator runs in `dllhost.exe`
    through that activation, outside Excel's process.
  - Excel 16 installed by Click-to-Run (build 20527, x64, `Office16\EXCEL.EXE`, not a Store package)
    sees none of the keys written under the user's hive by another process before or while it runs
    (a class name, a CLSID and a plain `Software` key written by .NET and by `reg.exe`, the add-in's own
    ProgID), while it reads the user's older keys (`.3g2`, `Shell Folders`, `Volatile Environment`) and
    the machine's (`MWComUtil.MWUtil25.2`); the same keys read fine from the script and from `reg.exe`
    at the same moment. So a local `CreateObject` of the per-user add-in answers 429 "ActiveX component
    can't create object" from VBA while the script's own process creates it. And R2025b's add-in DLL,
    loaded by its path into Excel, ends Excel's process (`0x800706BE`), where MATLAB Runtime's
    `mclmcrrt25_2.dll`, `mwcomutil.dll` and `mclxlmain25_2.dll` load; through `dllhost.exe` it would
    not be loaded into Excel at all.
  So Excel's cells over R2025b's add-in cannot be recorded on this machine without a machine-wide
  registration, which takes administrator rights that no script of this plan asks for; R2025b's
  conversions are K17a's and K17d's COM recordings, which the cells would have shown through Excel's
  own Variants.
- **JGraph's run** (`run-k17.ps1`, `recordings-jgraph/k17_*.out.txt`, `k17_differences.txt`): excel 1,004 keys, 258 agree, 746 recorded divergences, 0 unexplained; com_preinit 26, 21, 5, 0; com 559 keys, 508 agree, 51 recorded divergences, 0 unexplained; com_extra 1,076 keys, 1,021 agree, 55 recorded divergences, 0 unexplained; cells 82 keys, 11 agree, 71 recorded divergences, 0 unexplained; hosting 55 keys, 37 agree, 18 recorded divergences, 0 unexplained; pe 2,452 keys, 726 agree, 1,726 recorded divergences, 0 unexplained; registry 15, 10, 5, 0. The registry after the run is the one before plus JGraph's component category.
  JGraph's `k17bas.xlam` opens as an add-in with its VBA project (`xlam.open`: IsAddin true,
  HasVBProject true, one sheet), and every cell answers "Error in VBAProject: ActiveX component can't
  create object": JGraph's add-in and its utility library are registered for the current user too, by
  the probe's build, and this Excel sees neither (`k17_hosting`: the add-in's ProgID and the utility's
  unreadable from VBA while the script's own process creates both; `jgrt.dll`, `jgcomutil.dll` and the
  add-in's DLL load into Excel, and the add-in's loads again).
- **Tests.** `ExcelAddInTests` (its `-b` builds writing the module and the workbook with nothing pending, the
  Results order with the workbook, the module against the three recordings and the wrap rule),
  `ExcelAddInWorkbookTests` (4); the Deploy and Runtime areas: 411 of 411.

## Divergences

- **The add-in workbook is `<name>.xlam`, written as a file** (KO12); R2025b writes `<name>.xla`
  through Excel and warns `COM_WARN_FAILED_CREATE_EXCEL_ADDIN` where Excel does not trust its VBA
  project. The module is `Module1` in a project named `VBAProject`, with a `ThisWorkbook` document
  module and the `stdole` and `Office` references; the workbook has one empty sheet and no document
  properties.
- **The module creates its objects with no server name** where R2025b's writes
  `CreateObject(progid, "localhost")`: that activation cannot reach an add-in registered for the
  current user, which is what `mcc`, `_install.bat` and `regsvr32` give without administrator rights
  (`docs/MATLAB_Behavioral_Fixes.md`, section B). JGraph's component therefore runs in Excel's process
  (as it runs in any client's since stage K17c) where R2025b's, registered machine-wide, runs in
  `dllhost.exe` through its AppID's `DllSurrogate`; an out-of-process option is open item 248.
- **The utility library's ProgID is JGraph's** (`JGraphComUtil.MWUtil25.2`, KO10).
- **No Function Wizard** (KO11 open): nothing of MathWorks' ships, and the `IMWEnumVararg` and
  `IMWMethodArgInfo` interfaces it would use stay undeclared (open item 247).
- **The module is written for a `-T codegen` build too** (R2025b's `-b` with `-T codegen` is
  unmeasured), and the readme and page are JGraph's words, with the Click-to-Run note.
- **Excel's cells** over JGraph's add-in: the same refusal as R2025b's in the local activation's words (429 "ActiveX component
  can't create object" where R2025b's module gets 463 "Class not registered on local machine"), since
  neither per-user add-in is visible to this Excel; the 71 cell rows are one recorded divergence, and the
  cells themselves wait for a machine-wide registration (open item 248).

## Not measured

- R2025b's cells with a machine-wide registration (administrator rights), and its add-in hosted in
  `dllhost.exe`; whether the Click-to-Run registry view takes up new per-user keys after a logon.
- A module imported into a workbook's own VBA project (the trust setting, which K17 leaves alone);
  the name Excel gives an imported module without an `Attribute VB_Name` line is assumed `Module1`.
- The wrap of a `Call` line longer than 89 characters (no recorded function has one); the module of a
  function with more than 29 inputs, or whose name is the class variable's.
- `-b` with `-T codegen` in R2025b.

## Still open

KO11 (the Function Wizard). Open item 248: what this stage leaves.
