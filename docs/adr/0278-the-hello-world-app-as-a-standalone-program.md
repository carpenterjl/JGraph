# ADR 0278 — The Hello World app as a standalone Windows application, and the delete a window's close owes

## Status

Accepted, 2026-10-27. A follow-up to the compiler and SDK plan (ADRs 0256–0277) and the
app-building plan (ADRs 0198–0211): the first app with a `uiaxes` built and run as a JGraph
standalone program, and the runtime fault it found.

## Context

The compiler plan's acceptance set (ADR 0277) built a windowed program from an app with a label
and a button; stage K6 (ADR 0262) plotted in a classic figure under the runtime. No deployed
program had yet carried a `uiaxes`, a grid layout, a text field, a drop-down or a slider, and no
deployed app had been closed by its user: the acceptance apps closed themselves from a callback.
The user asked whether the app-building plan was complete enough for a Hello World standalone
Windows application that shows the app-building features with functions and graphics in it, and
then asked for one.

## Decision

- **The example, `examples\hello-app`.** `HelloApp.m` is an App Designer-shaped class: a
  `uifigure` titled `Hello World` with a `uigridlayout` of four columns and six rows; a title
  label; a name field and a Greet button that writes `Hello, <name>!` (or `Hello, World!`) into a
  label; a shape drop-down (`sine`, `square`, `damped`), a numeric frequency field with limits
  and an amplitude slider, each of whose `ValueChangedFcn` redraws; a Plot button; a `uiaxes`
  with a title, labels and grid, drawn by `plot` from `hello_wave.m`, a function in a file of its
  own with an `arguments` block, so a build shows a dependency found by analysis; and a status
  label that names what the app last did. With `HELLO_LOG` set, each status line also goes to
  that file with the time, since a windowed program's output goes nowhere. The `delete` method
  logs `closing`. `build_hello_app.m` builds it with
  `compiler.build.standaloneWindowsApplication` into `output\HelloApp.exe` (gitignored), and the
  README says how to run it at the prompt, build it, and run the built program with the runtime.
- **The check, `tools\runtime-checks\run-hello-app.ps1`.** Builds the app under a published
  runtime's `jgraph -batch`, starts `HelloApp.exe` with `JGRAPH_RUNTIME_ROOT`, and drives the
  window through UI Automation patterns alone: the title label read, the first plot's status
  line waited for, the name set through `ValuePattern` and Greet invoked, the greeting read, the
  frequency set and Plot invoked, the status line read, a picture of the window saved with
  `PrintWindow`, the window closed through `WindowPattern`, the exit code and the log checked.
  A field commits its text on Return or when the keyboard focus leaves it, so the check moves the
  focus to the button with `AutomationElement.SetFocus` before invoking it; the field's own
  `ValueChangedFcn` then draws once before the button draws again, as R2025b's field would.
- **The fault, and its fix.** Closing the window by its title bar never ran the app's `delete`
  method - not in the deployed program, and not in JGraph's application either. Three things stood
  in the way, each found with the Hello World app and the UI trace:
  1. *The program ended first.* The window's `Closed` handler retires the figure
     (`NotifyWindowClosed`), which announces the figure's deletion; the dispatcher queues the
     `ObjectDeleted` event for the script thread, since the figure has destroy listeners (the one
     `registerApp` added, which runs the app's destructor). But `WindowedBatchRun`, the run under
     every windowed program and under `-batch -showfigures`, had set WPF's
     `ShutdownMode.OnLastWindowClose`: the application shut down as the window closed, the session
     was disposed, and the queued event was never drained. The run now keeps `OnExplicitShutdown`
     once the script has returned with windows up, listens to the new
     `ScriptGraphicsCallbacks.WindowClosed` event raised after each figure window's close, and when
     no window is left shuts down only once no drain is under way and nothing is queued - waiting
     up to ten seconds after the last window went (R2025b's programs take 12–20 s after theirs,
     ADR 0262). The pump's end checks the same condition, so a drain that ran the delete ends the
     program at once.
  2. *The handle book could not find the figure.* `JgsHandleRegistry.TryGetEntry` read a figure's
     entry only while JG numbered the figure, and the close had already taken it out of the
     numbering, so the queued event's delivery found no entry, no `DeleteFcn` and no listener. A
     figure marked as being deleted is now read too, until the sweep lets its entry go.
  3. *The sweep ran too soon.* `NotifyWindowClosed` released the retired figure's handles at once,
     dropping the entry that held the listener. When the close queued work, the release is now
     owed instead (`TakeOwedRelease`), and the dispatcher's drain releases after it has delivered
     the queue, on the script thread.
  The application shares the second and third fixes: a user closing an app's window from the
  prompt's session gets the app's `delete` too, where before only `close(fig)` and `delete(app)`
  from a script did.

## Consequences

- A deployed app's `delete` method runs when its user closes the window, as it does in MATLAB,
  so what it cleans up (ports, files, timers) is cleaned up. `-batch -showfigures` in the
  application gains the same.
- The example is the shortest road for a reader: run `HelloApp`, run `build_hello_app`, start the
  program. The guide's standalone section points at it.
- The `uiaxes` has no UI Automation element: a check reads the plot from the picture, not from
  the tree (open item 220).

## Measured

- **The build** (`run-hello-app.ps1` with the runtime published from this commit): `jgraph -batch`
  exited 0 in 1 s; `HelloApp.exe` with `readme.txt`, `splash.png`, `buildresult.json`,
  `requiredMCRProducts.txt`, `includedSupportPackages.txt`, `mccExcludedFiles.log` and
  `unresolvedSymbols.txt` beside it.
- **The run**: the window `Hello World` up in under a second; the title label `Hello, World!`;
  the status `plot sine f=1 a=1 n=400` at start; `Hello, JGraph!` after Greet; `plot sine f=3 a=1
  n=400` after the frequency and Plot; the picture showing the grid, the controls and the 3 Hz
  sine in the `uiaxes` with its title, labels and grid; the window closed through WindowPattern;
  the program ended, exit code 0.
- **The log**: `started`, `isdeployed 1`, `plot sine f=1 a=1 n=400`, `greeting Hello, JGraph!`,
  `plot sine f=3 a=1 n=400` twice (the field's commit, then the button), `closing` - the last
  line absent before the fix, in two runs, and present after it.
- **Tests**: `AppDesignerU7Tests` gains
  `ClosingTheWindowOfAnAppWithoutACloseRequestFcn_RunsItsDeleteFromTheDrain` (the title bar's
  close on an app with the default `closereq`: `RequestClose` lets the window go, the close queues
  the deletion and keeps the listener, the drain runs the delete, and the figure's entry is let
  go after); it failed before the handle-book fix as the program did. The U7, U10, U12, desktop
  routing, event dispatch, figure callback, wait, menu, button-down, .fig and plain-text app
  tests and the app-building and object-item parity fixtures pass (83 and 138); the seven
  verify scripts, the harvest and the ratchet pass.

## Not measured

- **The same app built by R2025b**: `build_hello_app.m` is written to build under MATLAB Compiler
  too, but a MATLAB window needs the user's leave and the build was not run there.
- **The ten-second grace** was not exercised: the delete runs in milliseconds. A callback queued
  by the close that waits longer is cut off at the grace, as R2025b cuts its own shutdown.

## Divergences

- **The window's close runs the app's `delete` before the program ends** after a wait of at most
  ten seconds; R2025b's runtime takes 12–20 s after its last window and runs the delete within
  it. A delete that takes longer than ten seconds is cut off here and not there.
- **A `uiaxes` has no UI Automation element** in JGraph's window; R2025b's web-based figure
  exposes one. A check reads the plot from a picture.
