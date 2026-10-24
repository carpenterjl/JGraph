# ADR 0260 — The JGraph Runtime's facade, its runtime options, and its folder

## Status

Accepted, 2026-10-24. Compiler and SDK plan, stage K4.

## Context

Stages K1–K3b gave JGraph a deployed session, the archive, and a way to run a standalone program
from it (`jgraph --deployed app.ctf`). Every SDK target still to come (.NET, C, C++, Python, the
server, Excel) instead calls a component's functions from a host process. K4 builds the one public
face of the engine those targets sit on: a runtime a process starts once, components opened from
archives, calls on an engine thread, and a value model that crosses. It also handles the runtime
options (`mcc -R`), audits the engine's process-wide state, and lays out the runtime's folder.

What R2025b does was measured first, by `record-k4.ps1` (`tools/matlab-checklist/compiler-probes`):

- **`k4_ropts`.** `probe_k4_build` built `k4r_main` once per `-R` option, and each program ran from a
  current folder that was not its own.
  - `-startmsg,X` prints `Initializing MATLAB Runtime version 25.2`, then `X`. A bare `-startmsg`
    prints only the first line, and `-completemsg,X` prints only `X`. All of them print before main,
    in that order, and one `-R` can carry both (`-startmsg,X,-completemsg,Y`).
  - `-logfile,f` is read against the current folder, not the program's, and is replaced on every
    run. It takes standard output and standard error in the order written, warnings included, and
    the uncaught error's report without its identifier line.
  - `-singleCompThread` makes `maxNumCompThreads` 1, and `maxNumCompThreads(4)` refuses as
    `MATLAB:Multicore:MultithreadingDisabled`.
  - `-nojvm` changes nothing visible, since `usejava('jvm')` is already false in a compiled program.
  - An unknown option prints `Warning: Unrecognized command line option: bogus.` first, and the
    program runs.
  - `maxNumCompThreads` starts at 6 on this machine, its performance-core count (it has 20 logical
    processors). JGraph answered 20.
- **`k4_client`.** `k4.m` was built as two .NET assemblies, K4A and K4B. A C# client (`src/k4client`)
  made two instances of K4A and one of K4B in one process and called `k4` on each.
  - The two K4A instances share one session: globals, persistent variables, the base workspace,
    the current folder, the random stream, `format`, the warning state, `setmcruserdata`, the
    beep's switch, and figures.
  - K4B has its own of every one of these. A figure K4A made was not among K4B's.
  - `cd` in K4A also moved the .NET process's current folder, yet K4B's `pwd` stayed where it was.
  - Two one-second calls from two threads took two seconds, whether they went to two instances or
    to two components. Calls are serialized across the process.
  - An uncaught error reached .NET as a `System.Exception` carrying the message and an M-code
    stack (`deeper` at line 88, `k4` at line 37), and the report went to standard error.
  - A disposed instance still answered, and a new instance saw the old one's globals. The session
    lives as long as the process.
  - The first instance took 48.6 s to start (the runtime's cold start), the second 0.0 s, and the
    second component 1.0 s.
  - `ClassMap` cannot be passed to `compiler.build.dotNETAssembly` as a name-value pair
    (`Compiler:clishared:doNotSetRequiredPropByNVPair`).

## Decision

### `JGraph.Runtime` (new project, headless, net8.0)

- **`JgRuntime`.** One runtime per process.
  - `Initialize(RuntimeOptions)` prints the start lines, applies `-singleCompThread` and opens
    the log. Opening a component starts the runtime with that component's options if it is not
    running yet, as R2025b's first component sets them.
  - `Terminate()` ends every session and puts back the process-wide state the host had before the
    runtime started. The runtime can then be started again.
- **The engine thread.** A dedicated 16 MB-stack thread runs every component's work, one item at a
  time, in the order queued. This is R2025b's serialization: calls from any thread wait their turn.
- **`Component`.**
  - `Open(path)` or `Open(stream)` extracts the archive into the runtime cache (K3) and starts a
    deployed session over it: `isdeployed` is true, the path is frozen, and `ctfroot` is the
    extracted copy.
  - Every `Component` opened on one archive (one build's uuid) shares its session, as instances of
    one R2025b component class do.
  - `Call(name, nargout, args)` and `CallAsync(..., CancellationToken)` resolve the name as
    `feval` does from the base workspace. Only the manifest's public functions can be called,
    and too many outputs are refused as `MATLAB:TooManyOutputs` before the call.
  - Cancelling the token stops the function at its next statement, or before it starts.
  - `Dispose()` retires the handle; the session lasts until `Terminate`.
- **`DeployedException`** carries the MException's identifier, message and stack (each frame's
  name as the report writes it, its file and line), plus R2025b's report text.
- **The value model, `RuntimeValue`.**
  - `RuntimeNumeric` covers `double`, `single` and the eight integer classes, real or complex.
    `RuntimeLogical`, `RuntimeChar`, `RuntimeString` (null is `<missing>`), `RuntimeCell`,
    `RuntimeStruct` and `RuntimeSparse` cover the rest. Each holds its class, its size (N-D
    included) and its elements in column-major order.
  - A value with no data form here (an object, a function handle, a table, a datetime, a
    categorical) is a `RuntimeOpaque`. Only the component that returned it can take it back.
  - Inbound values are minted fresh (ADRs 0162–0164), and outbound ones are copied out, so the host
    never aliases a session's storage.
  - The model refuses what a class cannot hold: an `int8` of 128, a fractional integer, a complex
    integer array, and an `int64` or `uint64` beyond 2^53. That last refusal is the interop plan's
    precision limit.
  - Collection literals stay unambiguous: `RuntimeNumeric.Of` takes doubles, and the typed
    factories are named for their classes (`Int8`, `Single`, `Complex`).

### The process-wide state (the audit)

The engine keeps some session state in statics (plan section 5). Following `k4_client`:

- **Already per session in JGraph**, as R2025b has them per component: the workspace, globals,
  persistent variables, the path, the current folder, the random stream, the warning state,
  `lastwarn`, user data and the beep's switch.
- **Per component, moved in and out of the statics (`JgsProcessState`).** Before a call for a
  component other than the last one, the engine thread takes the last one's state out and puts
  this one's in. This covers:
  - the figure registry (`JG.TakeState`/`PutState`);
  - the graphics handles and what clearing them clears: groups, root defaults, detached
    components, the callback and click history, and owed container updates;
  - `format` (`JgsNumberFormat`);
  - the installed callback dispatcher.
- **Process-wide, as R2025b's are or by nature.**
  - The OpenBLAS thread ceiling and JGraph's parallel kernels, which `-singleCompThread` holds
    at one for the whole process.
  - The window host's hooks (`ScriptEventQueue`, `ScriptComponentFrames`, `ScriptRenderPump`),
    which are stage K5's.
  - The designer's kept figures.
  - `groot`'s own properties.
  - The performance switches and counters.
  - The engine thread itself.

### Runtime options (`JGraph.Deploy.RuntimeOptions`)

- **Parsing.** `RuntimeOptions.Parse` reads `-R` arguments as R2025b's regular expressions split
  them: a part that starts with `-` names an option, and the parts up to the next one are its value.
- **Where they apply.**
  - In a standalone program, `jgraph --deployed app.ctf` applies the manifest's options before
    main (`BatchRunner.StartRuntime`).
  - In a host process, `JgRuntime.Initialize` applies them.
- **The log.** `DeployedLogOutput` replaces the file, copies both streams in order, and leaves out
  the report's identifier line.
- **`-singleCompThread`.**
  - `maxNumCompThreads` answers 1 and refuses another count with R2025b's identifier and text.
  - The process's arithmetic runs on one thread: `OpenBlasLoader.LimitToOneThread`, and
    `ParallelKernels.MaxDegree` set to 1.
- **`maxNumCompThreads` everywhere** now starts at the performance-core count
  (`ProcessorTopology`), as R2025b's does: 6 here, where JGraph answered 20.

### The runtime's folder (`RuntimeLayout`, `installer/publish-runtime.ps1`)

- **Layout.**
  - The marker `jgraph-runtime.json` holds the JGraph version and release 25.2.
  - `bin\win64` holds the engine, `jgraph.exe` and `JGraph.Runtime.dll`, published ReadyToRun.
  - `runtime\win64`, `include`, `python` and `launchers\win64` are left for K10, K11/K12, K13 and
    K6 to fill.
- **Finding it.** `RuntimeLayout.Find` looks in three places, in order, and a place counts only
  when its marker names the version asked for:
  1. `JGRAPH_RUNTIME_ROOT`;
  2. the registry key `SOFTWARE\JGraph\JGraph Runtime\<version>` (`Location`), under
     HKEY_LOCAL_MACHINE and then HKEY_CURRENT_USER, which K18's installer will write;
  3. the application's own folder, for a self-contained application.
- **Publishing.** `publish-runtime.ps1` publishes the headless launcher (which now references
  `JGraph.Runtime`) into `bin\win64`.
  - ReadyToRun is the default, and `-NoReadyToRun` turns it off.
  - The script checks that the anchors are present and that no WPF framework assembly reached
    the folder (decision KO5).
  - It installs nothing.

## Consequences

- **K5** adds windows to a component's figures. A component's figures are already its own here.
- **K6's launcher** finds the runtime through `RuntimeLayout`'s order and runs an archive the way
  `jgraph --deployed` does, `-R` options included.
- **K8–K13** build MathWorks' API shapes over `Component`, `RuntimeValue` and `DeployedException`.
  K8's MWArray `numArgsOut` check before the call, and its exception text, are theirs.
- **K14's server** runs components in worker processes, so the engine thread's serialization is
  per worker.
- **The CLI references `JGraph.Runtime`**, so the runtime's bin carries it. `jgraph.exe` still
  carries the `uihtml` WebView2 host it always has (U9b).

## Measured

- **`ComponentTests`** (7) run R2025b's own `k4.m` in two components the way `k4client` did and
  check every answer against `k4_client`. They also cover the error's identifier, message, stack
  and report, refused names and output counts, two threads' calls taking their turn, cancellation,
  the host's figures put back by `Terminate`, and the start lines and log under runtime options.
- **`RuntimeValueTests`** (30) send 26 values through a component: every numeric class, complex,
  N-D, empty, logical, char rows and matrices, strings with `<missing>`, nested cells, struct arrays
  and sparse. Each reaches the engine as its class and size and comes back equal. A function handle
  goes back to its own component and is refused by another. The tests also cover the model's
  refusals, and the three values JGraph's storage does not keep.
- **`RuntimeOptionsTests`** (10) cover the options' parsing, R2025b's start lines, and
  `RuntimeLayout.Find`'s order and version check.
- **`DeployedSessionTests`** gains three tests:
  - the start lines, the log's content, and its replacement, under `jgraph --deployed`;
  - `-singleCompThread`'s `maxNumCompThreads`;
  - the performance-core default.
- **Start time.** Two runtime layouts were published and run warm, five times each after two
  warm-up runs:

  | Warm, to first output | Without ReadyToRun | ReadyToRun |
  |---|---|---|
  | Standalone hello (`jgraph.exe --deployed hello.ctf`), to exit | 0.32–0.33 s | 0.20–0.21 s |
  | Library (`Component.Open` and one call) | 0.26–0.28 s | 0.15 s |

  The plan's target was 1.5 s. R2025b's programs took 1.8–3.1 s warm here (K0, `k4_ropts`), and
  its .NET Data API 2.8 s. The ReadyToRun layout is 166 MB, against 126 MB without.

## Divergences

- **The start line names JGraph's runtime.** `-startmsg` prints `Initializing JGraph Runtime version
  25.2`, where R2025b prints `Initializing MATLAB Runtime version 25.2`.
- **`cd` in a component does not move the host process's current folder.** R2025b's did, though its
  other components kept their own folders. In JGraph the folder is only the session's.
- **A disposed `Component` refuses calls** with `ObjectDisposedException`. R2025b's disposed instance
  still answered, because its session belongs to the class. The session itself lasts the same in
  both.
- **The runtime can be terminated and started again** in one process. R2025b's cannot.
- **`-nojvm` and `-nodisplay` change nothing.** JGraph has no Java, and the display option is Linux's.
- **`groot`'s own properties are shared** by every component in a process. Figures, handles, root
  defaults and `format` are each component's own.
- **An empty logical passed in comes back as the empty double.** An empty array carries no class in JGraph.
- **A 1-by-0 char passed in comes back as `''`, 0-by-0.**
- **A complex value whose imaginary parts are all zero comes back real.**

## Still open

- Item 176: does R2025b run a library component's `startup.m`? It runs one before a standalone
  program's main. A library session here runs none.
- Item 177: a component's figures are counted and drawn, but not shown in a window (stage K5).
- Item 178: `groot`'s own properties (`Units`, `ScreenPixelsPerInch` and the rest) could join the
  per-component state.
