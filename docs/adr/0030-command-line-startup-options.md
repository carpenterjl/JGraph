# ADR 0030 — Command-line startup options and a headless launcher

## Status

Accepted (M27, 2026-07-24).

## Context

JGraph could only be started by launching a window. `App.OnStartup` ignored `e.Args` outright, so
there was no way to run a script from a shell, capture a session's output to a file, or ask the
executable what it supports. Every batch use was therefore closed off: regenerating figures as a
build step, processing a folder of measurements on a schedule, or checking in CI that a script still
runs.

The obstacle is that `JGraph.Application` is `<OutputType>WinExe</OutputType>`. A WinExe has no
console: it cannot write to standard output, and a shell gets its prompt back before the program has
done anything, so it cannot see an exit code either.

## Decision

**A second executable owns the command line.** `src/JGraph.Cli` builds `jgraph.exe`: a console
program targeting plain `net8.0` that never references WPF. It parses the arguments, owns stdout and
stderr, and returns the exit code. This is the same split MATLAB uses, and it buys something the
alternative cannot:

**`-batch` runs headlessly, in the launcher's own process.** Everything a script needs below the UI —
`JGraph.Scripting`, `.Serialization`, `.Export`, `.Rendering.Skia`, `.Plugins`, `.Api` — already
targets `net8.0`, so a batch run needs no WPF, no message loop and **no display at all**. It works
over a remote session and in a container. `exportfigure`/`savefigure` render offscreen exactly as
they do interactively.

**`-showfigures` opts back into a window.** Figures are suppressed by default (with a line saying
so, because a script that silently did nothing would look broken). With `-showfigures` the launcher
hands the run to `JGraph.Application.exe` with stdout piped back, and the application runs it with
**the main window never shown** — `IFigureWindowService` already mints a standalone `FigureWindow`
per figure number, independently of the main window, so skipping `window.Show()` is the whole change.
The process then exits as soon as the script does, unless it left windows open, in which case it
waits for the user to close the last one: exiting immediately would make the figures it was asked to
display flash past unseen.

**The parser is shared, not duplicated.** `StartupCommandLine`, `StartupOptions`, `StartupStatement`,
`BatchRunner` and the output sinks live in `JGraph.Scripting/Startup/` — the lowest layer both
executables already reference. The launcher forwards arguments verbatim to the application, so any
disagreement between two parsers would be a bug invisible until runtime.

**The statement is JGS unless it names a file.** If the argument resolves to an existing file, it is
read and its extension picks the engine (reusing `ScriptDocumentModel.LanguageForFile`); otherwise it
is JGS source. A file that exists but has no engine (`.txt`) is an error rather than a second guess:
the argument unambiguously named a file.

**Relative paths resolve against the shell's directory**, overridable with `-sd`. Writes land there.
Reads probe there first and then beside the script, so a script run by path still finds its own data
files — otherwise no script would be portable.

**`exit`/`quit` end the script anywhere.** New builtins throw `ScriptExitException`, which every
engine catches and turns into `ScriptRunResult.Exited(code)`. The code rides out on the *result*, so
the launcher needs no extra host interface. In the interactive editor `exit()` closes the
application: surprising for a Run button, but a script that says "stop" should mean the same thing
however it was started.

## Exit codes

`0` the script finished · `1` the script failed · `2` the command line was invalid · `n` whatever the
script passed to `exit(n)`.

## Alternatives considered

- **`AttachConsole(ATTACH_PARENT_PROCESS)` in the WPF application** — one executable and far less
  work, but the shell's prompt returns before the output does, exit codes need `start /wait`, and
  every batch run would still spin up WPF and require a desktop session. Rejected.
- **Always showing figure windows under `-batch`** — a simpler mental model, but it makes every batch
  run require a display, which defeats the point.
- **Reading the statement as source only, with a `-language` flag** — rejected as more to type for
  the common case; `run('file.jgs')` remains available for anyone who wants to be explicit.
- **A host interface for `exit`** (mirroring `IScriptFigureFiles`) — unnecessary once the code is on
  `ScriptRunResult`, which every host already inspects.

## Consequences

- `JGraph.Cli` references `JGraph.Application` with `ReferenceOutputAssembly="false"` and
  `SkipGetTargetFrameworkProperties="true"`: build ordering only. The two meet as processes.
- The launcher locates the application beside itself, then in the sibling project's build output, so
  it works from a development tree as well as a deployed one.
- `docs/jgs-scripting-guide.html` is now copied into both executables' output — `-h` opens it.
- `-batch` writes exactly one closing line of its own on failure (`jgraph: script failed — …`); the
  engines already write their own diagnostics as they go, and duplicating them would be noise.
- Three-line `IScriptFigureFiles` implementations now exist in both hosts. Sharing them would mean
  `JGraph.Scripting` referencing the serialization and export projects, which is a worse trade.
