# ADR 0180 — The native library host

## Status

Accepted. Stage I7 of the .NET and shared-library interop plan
(`docs/plans/net-and-shared-library-interop-plan.md`), after stage I6 (ADR 0179, ca0a6d9). One
commit, this ADR's. It begins the native half of the plan. `loadlibrary` (I8) and `calllib` with
pointers and structs (I9) are built on it.

## Context

The plan's user decisions put native calls **out of process**. A C library that dereferences NULL
ends MATLAB. In JGraph it must end a child process instead, and the script must get an error it can
catch.

This stage builds that child process and JGraph's end of it. It has no MATLAB surface of its own:
the C type model, `loadlibrary`, `calllib`, `lib.pointer` and `libstruct` are later stages. The
stage is therefore tested:

- through the client, by unit tests against the committed `jgtestlib.dll`;
- through a test-only script door, by two JGraph-only fixtures.

One R2025b question belonged here: where `loadlibrary` finds a library. A new probe answers it
(`probe_shrlib_search`, below).

## Decision

**`JGraph.NativeHost` is a small net8.0 executable that JGraph starts on a session's first native
call.** It is deliberately dumb. It can:

- load a library;
- resolve an export;
- allocate, free, read and write memory, and measure a C string;
- call a function pointer through a signature of primitive slots.

Every C semantic, such as types, layout, strings and conversions, stays in JGraph, where it can be
tested without a process.

`JGraph.Scripting` references the project for its protocol types. That reference also carries the
executable, its `runtimeconfig.json` and its `deps.json` into every host's output and publish. The
host project is marked `IsRidAgnostic`, because a `-r win-x64` publish otherwise builds it twice
and the two copies collide (NETSDK1152).

**The wire** is one duplex named pipe, created by the host with `CurrentUserOnly`:

- Every message is a frame: a four-byte length and its bytes, written in **one** write. Two writes
  wake the reader twice.
- A request starts with its operation. A reply starts with a status. A failure carries the Win32
  code and the system's text.
- The host serves the pipe and JGraph connects, so both ends are synchronous handles. An overlapped
  read cost a wait on every reply: 94 µs per script call before this change, 37 µs after.
- Reads and writes of 64 KB or more travel through a shared `MemoryMappedFile` arena instead of the
  pipe. JGraph creates it, sized to a power of two of at least 1 MB, and the host maps it by name.

**Calls.** Each distinct lowered signature compiles once, in the host, to a `DynamicMethod`. The
method loads 8-byte argument cells and emits an unmanaged `calli`, and the JIT places every argument
by the x64 convention. x64 has one convention, so `__stdcall` and `__cdecl` are the same call.

A struct by value follows the x64 rule without a generated type:

| Size | Argument | Return |
|---|---|---|
| 1, 2, 4 or 8 bytes | an unsigned integer of that width, in a register | in the integer register |
| any other size | a pointer to a copy that the callee may change | through a hidden first-argument buffer |

This replaces the plan's per-size blob types with the same ABI in less code. It is tested with
`jg_point_len` (16 bytes in), `jg_point_make` (16 bytes out) and `jg_union_in` (4 bytes in).

**Lifetime.**

- There is one host per `JGraphScriptGlobals`. It is started on demand and kept through `clear all`.
- It stops when the session's teardown runs: `JgsRunner.Run`'s `finally` and `JgsReplSession`'s
  `ReleaseWorkspace`, both beside `CloseAllFiles`.
- Every host joins one Windows job object made with `KILL_ON_JOB_CLOSE | DIE_ON_UNHANDLED_EXCEPTION`.
  JGraph holds the job's only handle, so when JGraph ends, crashed or killed, Windows ends every host.
  No error-reporting dialog holds a crashed host open.
- The host also watches its parent process, which covers the moment before it joins the job.

**A crash** ends the host mid-request. The pipe breaks, and JGraph reads the exit code, which is the
NTSTATUS of the fault. The request ends in `NativeHostExitedException`, which the call site turns
into `JGraph:loadlibrary:HostExited`:

> The native library host stopped (exception 0xC0000005, access violation) during
> calllib('jgtestlib', 'jg_crash'). Every library it had loaded is unloaded, and every lib.pointer
> into it is invalid.

- Known codes are named: access violation, stack overflow, the division faults, illegal
  instruction, stack buffer overrun, heap corruption, fail fast, breakpoint, and an unhandled .NET
  exception. Any other code prints as hex, and a small one prints as `exit code N`.
- The dead client refuses every later request with the same exception.
- The session starts a new host on the next call. `Generation` numbers each host in the process, for
  stage 9's pointers to know they are stale.

**A cancel** (Stop, Ctrl+C) during a call kills the host, because native code cannot be interrupted
any other way.

- The call site writes "The native library host was stopped during calllib(…). Every library it had
  loaded is unloaded, …" to the error console.
- It then throws `OperationCanceledException`, so the run ends as a stopped run.
- A stop that lands before the call starts loses nothing and says nothing.
- `Interpreter.Cancellation` exposes the statement's token to builtins for this.

**The environment and the folder** are brought up to date before each call, in two steps:

1. **The environment.** It is compared only when something that can change it has run since the
   last call: `setenv`, or any .NET member (`NetCatalog.SyncFolder` notes it). The test is a counter,
   `EnvironmentBlock.NoteChange`. Then the Win32 block is compared in place, and only the variables
   that changed are sent.
2. **The folder.** The host's folder follows `pwd`, sent only when it moves.

The host keeps `PATH` as JGraph's value plus each loaded library's folder. Each library also loads
with `LOAD_WITH_ALTERED_SEARCH_PATH`, so its own folder is searched for its dependencies.

**What native code prints goes nowhere.** Before any library loads, the host points its standard
handles at `NUL`. A library's C runtime therefore starts on `NUL`, and a chatty library can never
fill an unread pipe and hang. This matches the MATLAB desktop. R2025b `-batch` prints to its own
stdout (step 0, finding 12), so that is a divergence.

**The library search** is R2025b's, measured by `probe_shrlib_search`, which loads renamed copies of
the test library through the prototype file and asks the process which file it mapped:

- a path as given; otherwise the current folder, then each folder of the path;
- any extension given is kept (`s12.lib` loads);
- left off, **`.mexw64` is tried before `.dll`** (a folder holding both loads the first);
- a relative path with folders is taken from the current folder;
- nothing found leaves the bare name to the system search (`kernel32`).

The failures are R2025b's `MATLAB:loadlibrary:LoadFailed`: "There was an error loading the library
"<name>"" and then the loader's own text, with `%1` filled in as Windows fills it. The name is the
path when a file was found, and as given when not.

**The test door.** `jgraph.internal.nativehost` is test-only and undocumented:

| Call | Answers |
|---|---|
| `nativehost('call', lib, 'int32 jg_int32(int32)', 5)` | the return, as `calllib` will: numbers as double, a `cstring` as char |
| `nativehost('pid')` | the host's process id, or 0 |
| `nativehost('find', lib)` | the file the search finds, or the bare name |
| `nativehost('stop')` | ends the host |

A signature is C-style, with MATLAB's type names. The door caches modules and exports per host. It
lives under the `jgraph` root (ADR 0179) and is how the fixtures reach the host. Stage I9's `calllib`
supersedes it for scripts.

## Measured

Release, this machine:

| What | Cost |
|---|---|
| A host start and first request (unit tests, warm client) | about 80 ms |
| The first native call of a fresh CLI run (host start and client JIT) | 250 ms |
| A round trip calling `void jg_void(void)`, client to host and back | 15 µs |
| The same with an `int32` argument and return | 16 µs |
| A script call through the test door, library and export cached | 37 µs |
| An unchanged environment check | a counter compare; 7.7 µs when the block is read |
| A write of 1e6 doubles, a `jg_sum`, a `jg_scale_double` and a read back, through the arena | 97 ms for the whole test, the host's start included |

Before the work in this stage, a script call cost 194 µs. The fixes:

- the synchronous pipe;
- one write per frame;
- the export cache;
- the environment counter.

The `calllib` cost against R2025b is stage I10's measurement.

## What moves

- **New project `src/JGraph.NativeHost`:**
  - `Program` (the pipe, `NUL` standard handles, `SetErrorMode`, the parent watch);
  - `HostServer` (the request loop, `PATH`, the arena);
  - `CallCompiler` (lowering and the `calli` cache);
  - `HostProtocol` (frames, operations, slots);
  - `NativeMethods`.
- **New `Jgs/Native/`:**
  - `NativeHostProcess` (start, requests, crash and cancel, the arena, sync);
  - `NativeJob`;
  - `EnvironmentBlock`;
  - `NativeSession` (the per-session host, modules, exports, the library search).
- **The door:** `JgsBuiltins.NativeHost.cs`, and the `internal` field of the `jgraph` root.
- **Hooks:**
  - `JGraphScriptGlobals.Native` and `StopNativeHost`, called by the run's and the console session's
    teardown;
  - `setenv` and `NetCatalog.SyncFolder` note environment changes;
  - `Interpreter.Cancellation`.
- **Projects:** `JGraph.Scripting` references the host and allows unsafe code, for the in-place
  block compare and the arena. `JGraph.sln` gains the project.
- **Installer:** `build-installer.ps1` anchors `JGraph.NativeHost.exe` and its
  `runtimeconfig.json`. The WiX harvest ships them with no change to the package.
- **Probe:** `tools/matlab-checklist/interop-probes/probe_shrlib_search.m` (+ `.out.txt`).
- **Tests:** `NativeHostM180Tests` (27): every integer width with its sign, `uint64` max, floats
  among integers, structs by value, memory, the arena, strings, loader and `GetProcAddress` codes,
  crash, cancel, `printf`, environment and folder sync, the job object, the session's restart, the
  search order, a run ending its host, and Stop during a native call.
- **New JGraph-only fixtures:**
  - `interop_host_crash.jgs` (11 lines);
  - `interop_env_sync.jgs` (3 lines);
  - their cases in `helpers/ix_native.m`.

  The plan's `interop_host_cancel` fixture is a unit test instead, because a fixture cannot press
  Stop on itself.

## Consequences

- A crash in a C library costs the session its loaded libraries, and never JGraph itself.
- A native call can be cancelled, which R2025b cannot do.
- Every native call is a cross-process round trip, about 15 µs before conversion. This suits
  MATLAB-style `calllib` use: calls into a library that each do real work. A tight loop over a
  trivial C function is several times slower than in process.
- Pointer values a script will see (stage I9) are addresses in the host. Nothing in MATLAB lets a
  script do arithmetic on an address except `plus`, so this is invisible.
- A session that never calls native code starts no process and pays nothing.

## Divergences

- **Native code runs in a child process, so a crash in it is an error, not a dead JGraph.**
  Pre-registered in the plan. R2025b loads the library into MATLAB, where a NULL dereference ends
  the session. JGraph raises `JGraph:loadlibrary:HostExited`, naming the exception, and the next call
  starts a new host. Pointer addresses are the host's.
- **Stop during a native call works.** R2025b cannot interrupt a native call. JGraph kills the host
  and ends the run as stopped, after saying that the host's libraries were unloaded.
- **What native code prints is discarded.** The MATLAB desktop shows nothing either, but R2025b
  `-batch` prints it to its standard output (step 0, finding 12). JGraph's host points its standard
  handles at `NUL`, in the app and the CLI alike.
