# ADR 0315 — Excel add-ins: the options, `mcc -W excel`, and the output folder without its DLL

## Status

Accepted, 2026-11-07. Stage K17b of the compiler and SDK plan (`docs/plans/compiler-and-sdk-plan.md`,
gitignored; revision 28). The first engine code of K17: `compiler.build.ExcelAddInOptions` and
`compiler.build.excelAddIn`, `mcc -W excel` in the command line's parser, and the add-in's output
folder as R2025b's mcc lays it out, less the COM DLL (stage K17c, decision KO9) and the `.bas`
module (stage K17e). The fronts that refused the Excel add-in since K19-K21 build it now.

## Context

K17a (ADR 0314) recorded every R2025b Excel build in `recordings/k17_excel.out.txt`: the options'
answers and refusals, 17 `compiler.build.excelAddIn` builds, 23 `mcc -W excel` forms, each build's
files and texts. K17b is the stage that reads that recording back into code, as K11 read K11a's for
the C library: the options class transcribed from R2025b's `ExcelAddInOptions.m`, the builder from
its `ExcelAddIn` builder and `ExcelAddinWriter`, the batch file from `writeInstallBat.m`, the
command line's `excel:` target from what the recording says it takes and refuses.

By KO9 the add-in's DLL is a prebuilt native template (`jgxl.dll`) stamped by the build, which K17c
brings with the type library and the registration. A stage that writes the options and the folder
but no DLL has to say so plainly, so a user who builds an add-in today is not left with a folder
that looks finished: the build warns once, names what is pending, and leaves the archive beside the
add-in where the DLL would have carried it.

## Decision

### The options and the build function

- **`compiler.build.ExcelAddInOptions`** (`JgsBuiltins.CompilerExcel.cs`, `JgsBuiltinClasses.Compiler.cs`)
  declares R2025b's fifteen properties in its order (`opts.properties`): `AddInName`, `ClassName`,
  `AddInVersion`, `DebugBuild`, `EmbedArchive`, `FunctionFiles`, `GenerateVisualBasicFile`, then
  the dependency options and `OutputDir`. `AddInName` and `ClassName` are dependent: the name given,
  else the first file's stem as it is (R2025b's `fileparts`, not `defaultOutputFilename`) and the
  stem with `Class` after it; `OutputDir` defaults to `.\<name>excelAddIn`. The checks are R2025b's:
  `cliName` for the add-in name (text, one scalar, no white space but a blank, no separator; `a b`,
  `1abc`, `a.b` and `class` pass), `propertyIsScalarText` then `mustBeNonzeroLengthText` for the
  class and the version (so `''` is the nonzero-length refusal and `5` the type refusal), the three
  switches as `matlab.lang.OnOffSwitchState` under the class's own name, and `FunctionFiles` as
  K11's file list with the P-file warning R2025b's `throwPFileWarning` raises
  (`Compiler:build:shared:cannotAnalyzePFiles`, the P-files one per line). The constructor takes
  the files first and the names in `setdiff`'s sorted order; `narginchk(1, 29)`.
- **`compiler.build.excelAddIn`** builds the mcc line R2025b's writer builds: `-W excel:<name>,<class>,version=<v>`,
  `-T link:lib`, `-d`, `-v`, the dependency options, then `-b`, `-g` and `-C` for the three switches,
  then the files. After mcc it does R2025b's postmcc: `_install.bat` beside the DLL, and
  `GettingStarted.html`; `Results.Files` lists the DLL, `_install.bat`, the `.bas`, the separate
  archive and the page in that order, each where written, and `BuildType` is `excelAddIn`. An
  installer takes the add-in's `Results` by its `AddInName`, as it takes every other build's.

### `mcc -W excel`

- **`excel:<name>[,<class>[,<version>]]`** in `MccCommandLine`: the class defaults to the name
  (mcc_noclass: `k17mnc`'s class is `k17mnc`); with three parts a `version=` prefix is dropped,
  which is how `-B cexcel:<name>,<class>,<v>` (R2025b's own bundle, `-W excel:%1%,%2%,%3% -T
  link:lib -b -S`) hands a bare version; with a fourth part the third is read whole, so R2025b
  refuses `version=1.0` itself as a version when `other=1` follows (mcc_extra), and JGraph does the
  same. The version is K7's rule (`COM_ERR_INVALID_VERSION`, at most four integers of 0 to 65534).
  An empty name, with or without the colon, is `COM_ERR_ILLEGAL_COMPONENT_NAME`; a dotted name
  `COM_COM_EXCEL_NAMESPACE_ERROR` with the message's own quotes; `-o` after it
  `COM_ERR_MULTIPLE_COMPONENT_NAMES`, as a library's; `-T link:exe` the wrapper mismatch.
- **The switches:** `-g` is the debug build and `-c` the codegen, as a C library's; `-b` asks for
  the Visual Basic module (`VisualBasicFile`); `-C` writes the archive beside; `-T codegen` writes
  the archive and the texts and no DLL (mcc_codegen has no DLL in R2025b either); `-T compile:lib`
  is taken as `-T codegen` (R2025b's mbuild fails on it and leaves nothing).
- **Names JGraph refuses first:** an add-in or class name that is not an identifier (`1bad`, `a-b`,
  `a b`) cannot be a type library's, a coclass's or a ProgID's; `JGraph:mcc:excelAddInName` and
  `excelClassName` refuse it before anything is written, where R2025b's options accept it and its
  mbuild fails on the generated source (mcc_badname, mcc_badclass).

### The output folder

- **`ExcelAddIn.Write`** (`JGraph.Deploy/ExcelAddIn.cs`) writes the archive (target type 5, read
  from K17a's separate `.ctf`) and `readme.txt`, and takes a `TemplatePath`: the prebuilt DLL to
  copy and stamp. With none, as this stage leaves it, no DLL is written and the archive goes beside
  the add-in as `<name>.ctf` whatever `EmbedArchive` says; `mcc` then warns
  `JGraph:mcc:excelAddInPending` once per build, naming the DLL and, under `-b`, the `.bas`. K17c
  fills the template from the runtime's `jgxl.dll`; K17e writes the `.bas`.
- **The DLL's name** is R2025b's postmcc rule: `<name>_<major>_<minor>.dll`, with a major of 0 made
  `1_0` (`0` and `0.5` give `_1_0`, `2` `_2_0`, `3.4.5.6` `_3_4`), and the versioned ProgID takes
  the same suffix.
- **`_install.bat`** is `writeInstallBat.m` line for line (880 bytes for `k17xl_1_0.dll`, line
  feeds, no line end after `ECHO.`): MWRegsvr where it is on the path, else `regsvr32`, with the
  per-user question; JGraph's add-in registers through its own `DllRegisterServer`, which either
  tool calls. `readme.txt` is JGraph's words under R2025b's title line and three numbered sections,
  CR LF, naming the archive when it is beside, the DLL, the `.bas` under `-b`, and the runtime
  installer; `GettingStarted.html` is JGraph's words over R2025b's sections. The five logs are
  K3's (`BuildLogs`), with the one JGraph Runtime product.
- **No generated source:** no `.cpp`, `.hpp`, `.idl`, `.def`, `.rc` or `dlldata.c`; the add-in
  needs no compiler on the building machine (KO9).

### The fronts

`CompilerTask.IsBuilt` is true for every target, so the Excel Add-in Compiler app's Build and
Package, a project's Excel task (`deploytool -build`, the K20 deployment script), a legacy
`subtarget.ex.addin` project and `compiler.internal.ui.getExcelDeploymentScript` all build through
`compiler.build.excelAddIn`; `mcc -?` lists the add-in and the Production Server archive among what
JGraph builds; `compiler.package.installer` takes an add-in's results.

### The probe under JGraph

`run-k17.ps1 -UpTo addin` runs K17a's `probe_k17_excel.m`, unchanged but for a second argument that
stops it after the add-in sections (the options, every build, `mcc -W excel`, the server archive
and the add-in beside it), until K17f gives JGraph the Excel client the later sections build. Two
guards were needed in the probe: JGraph has no `ver` (open item 245), and by design no `system`,
so `reg.exe` cannot be asked for the registry per build; under JGraph every registry delta is
empty, and K17c's runner reads the registry from PowerShell around the whole run instead.
`compare-k17.py` no longer compares a warning's printed text (R2025b prints it on standard output
under whatever key ran last, JGraph on standard error, which the runner appends at the end): each
key's warning is compared through its `.warn` row, the `lastwarn` both engines share.

### Found on the way

- **A char beside a cell of chars** was refused by `setdiff`, `intersect`, `union`, `setxor` and
  the indexed `ismember` ("expects argument 1 to be an array, but got a string") where R2025b takes
  it as a cell of one (`setdiff({}, 'CLSID')` a 0-by-1 cell, `setdiff({'x', 'CLSID'}, 'CLSID')`
  `{'x'}`, `ismember({'x', 'CLSID'}, 'CLSID')` `[false true]`; measured 2026-11-07). Fixed
  (`CharBesideCell`); the probe's registry helper tripped over it. The answer's shape was JGraph's
  own too (a cell always a column, a 0-by-0 a row): measured the same day, `setdiff`'s answer is a
  row when A is a row vector (1-by-n, 1-by-0 included, 0-by-0 not) and `union`, `intersect` and
  `setxor`'s when both sets are, cells and arrays alike (`union({'b', 'a'}, {'c'})` 1-by-3,
  `union([], [1 2])` 2-by-1, `setdiff([1 2], [])` 1-by-2, `setdiff({}, 'CLSID')` 0-by-1); fixed,
  and `MatlabSetOperationTests` pins the measured shapes where it pinned the old column.
- **`ver` is undefined** (`ver`, `ver('compiler')`, `v = ver(product)`): open item 245.
- **`Tee-Object` writes UTF-16** in Windows PowerShell; `run-k17.ps1` writes its differences file
  through `[IO.File]::WriteAllText` with UTF-8.

## Consequences

- An Excel add-in's build is scripted, parsed and laid out as R2025b's; K17c adds the DLL by filling
  `ExcelAddInOptions.TemplatePath` and stamping, with nothing else to move, and K17e adds the `.bas`
  to the same file list. The pending warning goes with the DLL.
- The apps, projects and legacy builds that refused the target build it; the acceptance set's Excel
  row (K17g) still waits for the DLL.
- The comparer's KNOWN list names every row this stage leaves divergent, each with the stage that
  closes it, so K17c and K17e tighten it rather than widen it.

## Measured

- `run-k17.ps1 -UpTo addin`, `compare-k17.py --only excel`: 1,004 keys, 202 agree,
  802 recorded divergences, 0 unexplained (175 of them the Excel client's rows, K17f's).
- `ExcelAddInTests` (5): the command line's forms and refusals against the recording's words, the
  DLL's name over seven versions, `_install.bat` equal to the recording's for two builds, the
  options' derived names and ten checks, and a build's files, warning, results and the installer's
  name. `MatlabSetOperationTests` gains the char-beside-cell cases.

## Not measured

- A build with a template (no `jgxl.dll` exists yet): `TemplatePath` is a parameter K17c fills and
  tests.
- The `.bas` (K17e), Excel's own cells (K17e), the registry (K17c).
- The app window's Build of an Excel task (the window check is K17g's).

## Divergences

- `readme.txt` and `GettingStarted.html` are in JGraph's words, for the JGraph Runtime.
- No `.cpp`, `.hpp`, `.idl`, `.def`, `.rc` or `dlldata.c` is generated (KO9).
- The DLL, its registration, the per-user registration warning and the GUIDs are pending K17c; the
  `.bas` and the `.xla` failure warning are pending K17e; until then the archive is beside the add-in
  and `mcc` warns `JGraph:mcc:excelAddInPending`.
- A name that is not an identifier is refused before the build (`JGraph:mcc:excelAddInName`,
  `excelClassName`), where R2025b's mbuild fails after generating; `-T compile:lib` is `-T codegen`.
- `buildresult.json`, `requiredMCRProducts.txt` and `RuntimeDependencies` list the one JGraph
  Runtime product (ADR 0267); the options class declares no superclasses; the displays are JGraph's
  layout (open item 194); an options object is not saved to a MAT-file (open item 215).
- `AdditionalFiles` set to `''` is kept as `{}` where R2025b keeps the `''`; a list of support
  packages is refused in JGraph's words (JGraph has none).
