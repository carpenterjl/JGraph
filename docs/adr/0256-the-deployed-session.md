# ADR 0256 — The deployed session: a compiled program's session, its builtins and its main

## Status

Accepted, 2026-10-21. Compiler and SDK plan, stage K1.

## Context

A compiled program runs in a session that is not an ordinary one, and stage K0 recorded how R2025b's
MATLAB Runtime runs it (`tools/matlab-checklist/compiler-probes/recordings`, plan section 4.4):

- the main function gets its command line as char rows, or, for an `mcc -n` program, the values of
  the arguments that look numeric;
- `-mcruserdata key:value` is taken off the command line;
- an uncaught error is reported on standard error and ends the program with 249;
- warnings go to standard output;
- the search path is frozen, and the current folder holds data, not code;
- what the runtime does not ship is undefined, and the debugger refuses;
- `clc` and `home` are silent;
- `input` and `pause` read standard input;
- a `startup.m` built with the program runs before its main.

JGraph had no deployment builtins. A `-batch` file's main was called with no arguments, an
uncaught error ended the run with 1, and `version`, `matlabroot` and `license` answered for JGraph.

Everything later in the plan (the archive, the launcher, the libraries, the server) runs user code in
this session, so it comes first. The archive (K3) and the launcher (K6) do not exist yet, so the
session is reached through a hidden switch over a plain folder standing in for the extracted archive.

Decision KO2 (2026-10-21): `mcrversion` answers 25, 2, 0, the R2025b release the MATLAB dialect
follows; `version` keeps JGraph's own.

## Decision

### The deployment (`ScriptDeployment`, `ScriptContext.Deployment`)

- A host that runs a compiled component puts a `ScriptDeployment` on the context: `ctfroot`, the
  component's name, the frozen path, the main's arguments, whether they are numeric (`-n`), and the
  user data `-mcruserdata` set. `UncaughtErrorExitCode` is 249. `RuntimeVersion` is KO2's 25, 2, 0.
- **The hidden switch** `jgraph -batch main.m --deployed <folder> [--deployed-numeric] -- <args>`.
  - It builds one with `ScriptDeployment.FromFolder`. The folder is `ctfroot`, and the component is
    named after the main.
  - The frozen path is the main's folder, then every other folder under the root that holds a file,
    in name order. Dot folders, `private`, and `@` and `+` folders are left off.
  - Everything after `--` is the program's command line, and `TakeUserData` takes the
    `-mcruserdata` pairs out of it.
  - It is not in the usage text. `-showfigures` carries it to the application through the same parser.
- **What a deployed `jgraph` does not read:** the user's `settings.json` and the `plugins` folder.
  Its figures are not announced with the `-showfigures` note.

### The builtins (`JgsBuiltins.Deployment.cs`)

**In every session**, as `probe_k1_session` measured R2025b's MATLAB answering:
- `isdeployed` answers whether the session is a deployed one. `ismcc` is always false, since JGraph
  never runs user code during a build.
- `ctfroot` answers the deployment's root, or `matlabroot` outside one.
- `mcrversion` answers the major version for none or one output, then the minor and the update, and
  zeros past the third.
- `mcrcachedir` answers `%LOCALAPPDATA%\JGraph\RuntimeCache\<JGraph version>`, or
  `<MCR_CACHE_ROOT>\<JGraph version>` when that is set.
- `getmcruserdata` and `setmcruserdata` keep the session's store. A missing key answers a 1-by-0
  double, keys are case-sensitive char or string, and other keys are refused as
  `MCR:mclinstancedata:MCLMCR_WRONG_USER_DATA_KEY_TYPE`.
- Excess inputs are refused with each builtin's own identifier (`MATLAB:maxrhs`,
  `MATLAB:TooManyInputs`, `MATLAB:mcrcachedir:tooManyInputArgs`).

**In a deployed session**, `RegisterDeploymentBuiltins` runs last of the registrars and replaces
what K0 recorded differently:
- **The path:**
  - The frozen folders go on the function path.
  - `addpath`, `restoredefaultpath`, `rmpath` of a folder on the path, and `path` set to anything
    but itself are refused as `MATLAB:mpath:PathAlterationNotSupported`.
  - `rmpath` of a folder not on the path only warns `MATLAB:rmpath:DirNotFound`.
  - `path` answers the frozen folders.
- **The current folder** is data, not code:
  - `ImplicitCodeFolders` is empty, and `JgsFunctionPath.Find` and `FindFile` do not look beside the
    run.
  - `which` names a data file on the frozen path, never one in the current folder.
  - `Resolve` reads a relative name in the current folder, then on the frozen path.
  - `pwd` is the folder the program was started from, not the main's.
  - `run` finds a script on the frozen path alone, else `MATLAB:run:FileNotFound`.
- **What the runtime does not ship:**
  - `help` and `lookfor` are withdrawn from the built-in layer (`JgsBuiltinLayer.Withdraw`).
  - `keyboard` and `dbstop` refuse with `MATLAB:services:MissingRequiredCapability`.
  - `mex` refuses with `MATLAB:mex:DEPLOYMENT_MODE_NOTSUPPORTED`.
- **The console:**
  - `clc` and `home` do nothing and say nothing.
  - `input` reads a line and evaluates it; a closed input answers `[]`, or `''` for text.
  - `pause` with nothing to wait for prints `Paused: Press any key and return` and reads a line.
  - `license` with no arguments answers `'unknown'`.
  - Warnings go to standard output.
- `waitforallfiguresclosed` waits while a figure is visible and answers the invisible figures' names
  as a row, as R2025b's runtime file does. It exists only in a deployed session.

### The main and the end (`JgsDeployedMain`)

- **The startup file.** A `startup.m` on the frozen path runs in the base workspace before the main
  file runs.
- **The arguments.**
  - The main, a function file's or a class file's constructor, is called with the arguments as
    char rows.
  - An `-n` program's arguments go through `LooksNumeric`, R2025b's `mclisnumerictype` as two probes
    measured it over 145 strings (rule below). The ones it accepts are evaluated; any other is
    refused before the main runs.
  - That refusal is R2025b's, raised as `mainFuncForNumericStandaloneApplication` raises it, with its
    two frames.
- **The report.** In both the one-shot runner and the live session, an uncaught error writes
  R2025b's report to standard error and the run ends with 249. The report is:
  - an `Error using <frame> (line n)` line, for an error a script raised;
  - the message and a blank line;
  - `Caused by:` and the indented causes;
  - an `Error in` line and a blank line for every outer frame;
  - the identifier.

  An internal error ends with 249 too.

**The `-n` rule.** A letter refuses the argument when it starts a word: at the start, or after
anything but a digit, a letter, `_`, `.` or `@`. A quote of either kind accepts whatever follows.

### Fixed on the way

- `s = beep` answers the switch, `'on'` or `'off'`, without sounding. K0's probes start with it.
- `quit(5, 'force')` and `exit('force')` are taken, where `quit` refused a second argument.
- `f(varargin{:})` of a name nothing answers, with no arguments after the expansion, was an
  `IndexOutOfRangeException`; it is now `Unrecognized function or variable 'f'.`
- `feval` of a name nothing answers uses R2025b's words and `MATLAB:UndefinedFunction`, as a written
  call does.
- **The quiet launcher.** `run-quiet.ps1` gave every program a stdin holding a UTF-8 byte order mark:
  Windows PowerShell's `Process.Start` writes the stdin writer's preamble into the pipe. It now opens
  the writer with an encoding that has none. K0's `stubs` case was re-recorded: R2025b prompts once
  at a closed input, not twice. The second prompt was its `input` refusing the three bytes.

## Consequences

- A host that runs compiled code (K4's facade, K6's launcher, the server) sets
  `ScriptContext.Deployment`; nothing else changes what a session is.
- K3's archive replaces `FromFolder`'s layout rule with its manifest's path list. The `ScriptDeployment`
  constructor already takes one.
- `JgsBuiltinLayer.Withdraw` exists for a registrar that must leave a name undefined; it refuses once
  the layer is sealed.
- `ScriptRunningBuiltins` gains `input` (a deployed `input` evaluates what it reads) and
  `waitforallfiguresclosed`. The M164 audit allows three unreachable names, not two.
- `StartupOptions` has `DeployedRoot`, `NumericArguments` and `ProgramArguments`.

## Measured

- **The probes.** `probe_k1_numeric` (108 more strings through `mclisnumerictype`) and
  `probe_k1_session` (the builtins in R2025b's MATLAB, and the messages' text), in
  `tools/matlab-checklist/compiler-probes`. Dispatch sweep 18 (`dispatch/dispatch_probe18.m`) found a
  file shadowing each of the seven names under every pattern, as R2025b's resolution does. Its nargout
  rows are in `nargout-r2025b.tsv`, with five output-count rows generated from them.
- **K0's programs run here.** `run-k1.ps1` runs K0's 26 cases (the two `-n` refusals included, since
  JGraph makes no beep) through `jgraph -batch --deployed` under the quiet launcher. It lays each
  program out as R2025b extracted it and writes `recordings-jgraph/` and a `differences.txt`.
  - Every exit code is R2025b's.
  - Fifteen cases print the same lines: the arguments, `-n` and its refusals, the error reports,
    `exit`/`quit`, both `feval` cases, the stubs and the invisible figure.
  - What differs in the rest is listed below, or is outside this stage: JGraph's display format,
    warnings with no `> In` line, the functions of open item 169, and `eval` assigning inside an
    anonymous function, which R2025b refuses.
- **The fixture** `k1_deployment_builtins` (55 lines, recorded in R2025b) passes in both
  representations.
- **The tests** (`DeployedSessionTests`, 89 cases) run programs through `BatchRunner` with the
  switch. They cover:
  - arguments, `-n` against 65 strings of the probes, the refusal's report, exit codes, the three
    reports;
  - warnings, the frozen path and the current folder, what is withdrawn and refused;
  - `input` and `pause` on a closed stdin, user data, `startup.m`, the command line and the
    deployment's layout rule;
  - the ordinary session's answers and refusals.

## Divergences

- **`ctfroot` and `mcrcachedir` name JGraph's cache**, `%LOCALAPPDATA%\JGraph\RuntimeCache\<JGraph
  version>`, where R2025b's are under `MathWorks\MatlabRuntimeCache\R2025b`. Until K3 extracts
  archives, `ctfroot` is the folder `--deployed` names.
- **A deployed `version` and `matlabroot` answer for JGraph**: its version, and the folder it runs
  from (KO2 kept `version` JGraph's; `mcrversion` is R2025b's).
- **A deployed path holds only the program's folders**, two here against R2025b's 226 with the
  runtime's toolbox folders.
- **An operator's error has no `Error using` line**: R2025b heads `ones(2) * ones(3)` with
  `Error using *`, and JGraph's report starts at the message.
- **`exist('help')` and `exist('guide')` in a deployed session are 0 and 5**, where R2025b answers 7
  for folders of those names on its runtime path.

## Still open

- Open item 169: the session questions a deployed program asked in K0 that JGraph has no answer for.
- Open item 170: the deployed report's and warnings' leftovers.
- K0-w, the windowed program (its splash, visible figures keeping it alive), still waits for the
  user's leave. `waitforallfiguresclosed` and the `-showfigures` route are untested against R2025b
  with a window.
