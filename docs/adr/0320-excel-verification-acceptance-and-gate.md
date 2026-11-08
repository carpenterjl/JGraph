# ADR 0320 — The Excel targets verified: the acceptance row, the Excel checks, the gate

## Status

Accepted, 2026-11-07. Compiler and SDK plan, stage K17g, the last of K17 and of the plan (decision
KO13; KO11, the Function Wizard, stays open as a recorded divergence). It closes the arc ADRs
0314–0319 built: the recordings (K17a), the options and `mcc -W excel` (K17b), the add-in's COM DLL
(K17c), the conversion layer and the COM utility library (K17d), the module and the add-in workbook
(K17e), and the Excel client for the Production Server with its installer (K17f).

## Context

Stages K17b to K17f each measured their own piece against K17a's recordings through `run-k17.ps1`,
which builds about forty add-ins and clients and drives them from PowerShell. What K22 (ADR 0277) had
left open for the Excel targets was the whole road: the acceptance set built from one source tree by
both engines with the Excel add-in and the Excel client among its targets and the same client script
calling both engines' builds; the checks the plan asked for (the add-in in Excel's cells, a function
that plots raising a JGraph window, the client installed by its own installer against a server, the
Excel Add-in Compiler app's Build and Package); the guide's Excel section; and the gate.

Two facts from K17e and K17f shape what this stage can measure here. Excel 16 installed by
Click-to-Run reads the machine's COM registrations alone: every per-user registration (which is what
`mcc`, `_install.bat` without administrator rights and the client's installer "for the current user"
write) is invisible to its VBA, so a worksheet function's `CreateObject` answers 429 and the cell
holds the module's error text, in R2025b's add-in and JGraph's alike (`recordings/k17_cells.out.txt`,
open item 248). And the plan's house rule keeps every built installer unrun, with one exception the
plan wrote into this stage: the Excel client's own installer, run for the current user and removed
with `/uninstall` (KO13's cleanup).

## Decision

### The acceptance row

`build_k22.m` builds twelve targets: the Excel add-in now with its module (`GenerateVisualBasicFile`
on; JGraph writes the add-in workbook beside it, R2025b's `.xla` needs an Excel that trusts its VBA
project and is not written), the Excel client for the Production Server from the server archive's
results (`xlclient`: `accxlc.dll`, `accxlc.bas`, and JGraph's `accxlc.xlam`), and the client's
installer pointed at the server the acceptance runs (`xlinstaller`: `accxlcExcelClientInstaller.exe`
in both engines). Both engines register the add-in and the client for the current user as they build;
R2025b's `mcc` warns so on standard output, JGraph's on standard error.

`accept-k22.ps1` gains three steps. `excel` runs `src\k22client\acc_xl.ps1`: an invisible Excel,
the engine's utility library (`MWComUtil.MWUtil25.2` or `JGraphComUtil.MWUtil25.2`) given its
`Application` through `MWInitApplication` (R2025b's add-in refuses every call until then, K17a; JGraph's
takes it the same way), the add-in created by its ProgID `accxl.AccXl.1_0` and each library function
called in PowerShell's direct COM syntax: a sum, a char for a double (the `arguments` block converts it
to 120, so 123), a missing argument, a struct answer (an `MWStruct` with three fields), a text answer,
a matrix scaled from a two-dimensional array and from a worksheet Range, the persistent counter, the
error with the add-in's source and R2025b's description, and the flags' defaults. The `server` step,
after `acc_server.py`, runs `src\k22client\acc_xlclient.ps1` against the same server: the client
configured where it reads its server (R2025b's under `MATLAB Builder EX\2.5\MPS Server Config`, the
URL a value name; JGraph's `ServerURL` under `Software\JGraph\Excel Client\accxlc`), R2025b's .NET
add-in reflected with MathWorks' client assembly loaded first and JGraph's native one created by its
ProgID, the same samples called, the configuration removed. `cleanup` unregisters the add-in and the
client with `regsvr32 /u`, sweeps every key added since the snapshot whose contents name `accxl`
(R2025b's .NET client was registered by a `.reg` file and has no unregister; its CLSID lands under
`Wow6432Node`), unregisters the runtime's `jgcomutil.dll` when this run's build registered it, and
compares the registry's names with the snapshot. `Tokens` turns `localhost:<port>` into
`localhost:<PORT>`, since muserve's port is whatever was free. `compare-k22.py` names each Excel row
that differs.

### The Excel check

`tools\runtime-checks\run-k17excel.ps1` does what this machine lets it do, and says what it cannot.

- **addin:** `jgraph -batch` builds the K17 functions into an add-in with its module and workbook.
  The add-in is created by its ProgID in the check's own process: `k17_one(3)` answers 4, `k17_count`
  counts, `k17_err('id')` fails with the add-in's source and R2025b's description, and `k17_plot('on')`
  raises a JGraph figure window in that process - `Figure 1`, found by enumerating the process's
  windows, pictured with `PrintWindow` and closed through `WindowPattern.Close`; a second, invisible
  figure then counts one. An invisible Excel (`DisplayAlerts` off) opens the workbook as an add-in,
  reads its module's procedures and calculates `=k17_one(3)` in a scratch workbook: the cell holds
  the module's refusal, `Error in VBAProject: ActiveX component can't create object`, exactly as
  `run-k17.ps1`'s cells step recorded it, and the check passes on that text; a number there would
  mean the registry view had changed and is reported as news. The workbooks close unsaved, Excel
  quits, the add-in is unregistered.
- **client:** `jgraph -batch` builds the acceptance library into a server archive, the Excel client
  for it and the client's installer with `ServerURL` this machine; `jgraph-mps` serves the archive.
  The installer runs for the current user with `/quiet /dir <folder> /url http://localhost:<port>/accarch`
  and the check reads what it did: the five files unpacked (the client, its module and workbook,
  `jgxlconfig.dll`, the installer's own copy), the client's `ProgID` and `JGraph.ServerConfig`
  registered, the settings `ServerURL`, `timeoutSeconds` 60 and `responseSizeLimit` 750 as packaged,
  Excel's COM add-in entry with `LoadBehavior` 3, the `OPEN` entry under Excel's Options key naming
  the workbook, the Apps & Features entry `k17gxc Excel client (JGraph)`. The installed client is
  created by its ProgID and calls the server: 5, `Hello, Ada!`, the counter, a missing argument as
  `MATLABException:MATLAB:minrhs` text, a scaled two-dimensional array. Excel opens the installed
  workbook (the same refusal in the cell; its `COMAddIns` are noted). Then `/uninstall /quiet`: the
  settings, the registrations, Excel's entries, the Apps & Features entry and the folder are gone.
- **The registry after is the one before**, by the names under the user's classes, JGraph's keys,
  Excel's add-ins and the Uninstall key, and Excel's `OPEN` entries are as before.

### The window check

`tools\runtime-checks\run-k17window.ps1`, as K19's: `JGraph.Application -r "excelAddinCompiler('<file>')"`,
the Excel Add-in Compiler window with its exported functions list and status line, Show Code with
`compiler.build.ExcelAddInOptions`, `ClassName = "Class1"`, `GenerateVisualBasicFile = "on"` and the
installer, Build with the dialog `Excel Add-in Packaging Progress` (step `Building Excel Add-In`)
ending in `Process completed successfully` and the DLL, module and workbook in the task's output
folder, the add-in registered for the current user; Build and Package ending the same way with one
installer in the package folder (named, never run); pictures; the add-in unregistered and its keys
swept, the registry compared.

### The guide

`docs/jgs-scripting-guide.html`'s "Deploying code" chapter gains "Excel add-ins and the Excel client":
the add-in build and what it writes, how Excel and COM clients reach it, the conversion rules and the
utility library, the Click-to-Run note (for Excel's cells, register once as an administrator), the
Excel client and its switches, settings and failure texts, the installer and the configuration
add-in, and the app. The chapter's opening names the Excel targets, the differences section records
that they are JGraph's own pieces under MATLAB's names (no Function Wizard; R2025b's three writer
slips not copied), and the acceptance paragraph names the add-in and the client.

## Measured

- **The acceptance set** (`accept-k22.ps1 -Engine R2025b`, then `-Engine JGraph -Runtime <a published
  runtime>`, `compare-k22.py`): 330 keys, 284 agree, 46 recorded divergences, 0 unexplained (at K22: 245,
  218, 27, 0). R2025b's twelve builds took 212 s, JGraph's 5 s. Of the Excel rows, every build line agrees
  (`build.excel ok excelAddIn`, `build.xlclient ok excelClientForProductionServer`, `build.xlinstaller`),
  the add-in's 22 keys agree in 20 (the two that differ are R2025b's standard-error report of the error
  `acc_fail` raised), and the client's 21 keys agree in 15 (the six that differ are the four .NET rows that
  have no native counterpart and the struct message, below).
- **The add-in from COM, in both engines alike:** `acc_add(2, 3)` 5, `'x'` for a double 123, a missing
  argument refused by the type information with `DISP_E_BADPARAMCOUNT` from PowerShell, `acc_stats` an
  `MWStruct` of three fields (`n=4; mean=2.5; max=4`), `Hello, Ada!`, a 2-by-2 scaled from a two-dimensional
  array and from a Range (one-based both ways), the counter 1, 2, 3 across a failure, `acc_fail` an
  `E_FAIL` with source `accxl.AccXl.1_0` and `acc_fail: from COM\nError in => acc_fail.m at line 6`, the flags'
  defaults `InputArrayFormat=1 OutputArrayFormat=0 DateBias=693960`.
- **The client against the server, in both engines alike:** 5, 123, `MATLABException:MATLAB:minrhs` as the
  cell's text for a missing argument, `Hello, Ada!`, a zero-based 2-by-2, 5, the counter continuing from the
  server client's calls (4, 5), no MessageBox; the configuration written and removed.
- **The Excel check** (`run-k17excel.ps1 -Runtime <a published runtime>`): 50 checks passed, none failed.
  The add-in built and registered, created by its ProgID in the check's process: `k17_one(3)` 6 (its
  double), `k17_count` 1 and 2, `k17_err('id')` `E_FAIL` with source `k17gxl.K17gXl.1_0` and `Custom text
  with an identifier\nError in => k17_err.m at line 6`, `k17_plot('on')` 1 and the window `Figure 1` up
  in the check's process (pictured in `addin-figure.png`), closed through `WindowPattern.Close`, an
  invisible figure after it counting one; Excel 16 (build 20527) opened `k17gxl.xlam` as an add-in and
  `=k17_one(3)` held the recorded refusal; the add-in unregistered. The client: the archive, the client
  and `k17gxcExcelClientInstaller.exe` built, `jgraph-mps` serving on 9975, the installer's `/quiet /dir
  /url` run exiting 0 with the five files, the two registrations, the settings (`ServerURL`, 60, 750), the
  `LoadBehavior 3` entry, the `OPEN` entry and the Apps & Features entry as described; the installed
  client answering 5, `Hello, Ada!`, 1 and 2, `MATLABException:MATLAB:minrhs` and a 2-by-2; Excel
  opening the installed workbook with the refusal in the cell and `JGraph.ServerConfig` not among its
  five `COMAddIns`; `/uninstall /quiet` exiting 0 with every key, entry and the folder gone; the
  registry after the one before and no `OPEN` entry left. Three things the first run taught: the CLI's
  Debug folder has no `JGraph.Runtime.Desktop.dll`, so a figure raised there has no window (the check
  takes `-Runtime`); the module's procedures cannot be read through `VBProject` on this machine
  (`AccessVBOM` unset, never changed; K17a found it), so that row is a note; and the client's DLL loaded
  into the check's process kept the uninstall from deleting it until the check released its objects and
  called `CoFreeUnusedLibrariesEx`, as a user closes Excel before uninstalling.
- **The window check** (`run-k17window.ps1`): every check passed - the task manager over the project, the
  app over `myXl`, the status line, the code view's `ExcelAddInOptions`, `ClassName = "Class1"`,
  `GenerateVisualBasicFile = true` and `compiler.package.installer`, Build ending in `Process completed
  successfully` with `k17win_hello_1_0.dll`, `k17win_hello.bas` and `k17win_hello.xlam` under
  `myXl\output\build` and `k17win_hello.Class1.1_0` registered for the current user, Build and Package
  ending the same way with `MyAppInstaller.exe` under `output\package`, two pictures; the add-in
  unregistered, the utility library's registration the build made undone, the registry after the one
  before. The first run found two things: `excelAddinCompiler` refuses a file in both engines (the check
  now opens the app over a project's task), and a build registers the utility library per user when
  nothing has (the check undoes what its run made).
- **The gate**: the Deploy and Runtime areas with the ownership gate, 422 of 422 (no engine code changed in
  this stage: the acceptance, the checks, the guide and the documents); the seven `verify-*.py` scripts
  green, the divergence harvest (1340 rows from 215 ADRs).

## Not measured

- **Excel's cells over either engine's add-in or client** with a registration Excel can see: a
  machine-wide one takes administrator rights no script of the plan asks for (open items 248 and 249);
  the cells here hold the recorded refusal in both engines, and the checks pass on that text.
- **The installer's dialog, `/allusers` and an elevated run**: the check runs it with `/quiet` for
  the current user alone (the house rule, and KO13's cleanup).
- **R2025b's client installer run**: inspected by `k18-payload.py` (K17f), never run, as the house
  rule keeps every R2025b-built installer.
- **The ribbon button of the server configuration add-in inside Excel** (Excel's `COMAddIns` view of
  a per-user entry is behind the same registry view), and `JgxlConfigDialog` driven by a check.
- **A figure raised from inside Excel's process**: the figure window check runs in the check's own
  process, since Excel's cells cannot reach the add-in here.

## Divergences

- **A struct answer in the Excel client's cell** is `Exception:No .NET type available for a MATLAB structure
  with fields [n,mean,max]` in R2025b and `Exception:A MATLAB structure with fields [n,mean,max] cannot be
  shown in a cell` in JGraph (ADR 0319's wording).
- **The client's reflection rows** (`deps.*`, `asm.load`) are R2025b's .NET assembly's and have no native
  counterpart; JGraph's client is created by its ProgID.
- **The files a build leaves**: R2025b's add-in build writes `AccXl_com.hpp`, `accxl_1_0.lib`, `accxl_idl.h`
  and `mwcomtypes.h` beside the DLL and the module, JGraph's the add-in workbook `accxl.xlam`; the client's
  build writes `accxlc.xlam` in JGraph alone (R2025b's `.xla` needs an Excel that trusts its VBA project).
- **The cleanup rows**: `regsvr32 /u` exits 4 over R2025b's .NET client (no `DllUnregisterServer`), its
  `Record` and `Wow6432Node` keys are swept, and the first run after K22 also removed the stale registration
  that K22's R2025b build had left (3 removed); JGraph's add-in and client unregister themselves, and the
  runtime's `jgcomutil.dll` is unregistered when the run registered it.
- **The registrations' shape**: R2025b's .NET client lands under `Wow6432Node\CLSID` through
  `regasm /regfile` and `reg import` and has no unregister of its own (the cleanup sweeps its keys);
  JGraph's client and add-in register and unregister through their own `DllRegisterServer` and
  `DllUnregisterServer`.
- **The per-user registration warning** is on standard output in R2025b (`mcc` prints it as a warning
  with its stack) and on standard error in JGraph, under the `STDERR` key.
- **Excel's cells** hold `Error in VBAProject: Class not registered on local machine` over R2025b's
  add-in (its module's `CreateObject(progid, "localhost")` is a DCOM activation) and `Error in
  VBAProject: ActiveX component can't create object` over JGraph's (a local activation the Click-to-Run
  registry view does not serve), as K17e recorded; the check passes on JGraph's text.

## Still open

- **KO11**, the Function Wizard: none in K17, a recorded divergence (the add-in works through its
  worksheet functions).
- **Open item 250** holds what the stage leaves.
