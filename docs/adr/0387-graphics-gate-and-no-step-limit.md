# ADR 0387 — The display waits for a script's drawnow, and a Run has no statement limit

## Status

Accepted, 2026-11-17. Open items 361 and 359, found by the head2head show/hide round
(`JGraph_demo_workspace/head2head_GUI_2026-11-17_showhide`).

## Context

- **The crash.** The show/hide round forces every figure on screen as it is made: a stand-in `figure.m`
  calls the built-in, then `drawnow`. In the Release app that opened each window mid-Run, and the script
  thread went on adding lines, surfaces and axes to the figure the window was painting. Three launches of
  the same Run gave three outcomes: normal (65 s); every row 3-10x slower from the first timed pass; and
  the crash dialog in the warm-up pass. `%APPDATA%\JGraph\crash.log` held the reason each time:
  `InvalidOperationException: Collection was modified` on the UI thread, in `FigureRenderer.Render`,
  `FigureModel.RecomputeDataBounds` (called from the render) and `PlotBrowserControl.BuildFigureNode`.
  Nothing ordered the UI thread's reads of a figure against the script thread's writes. It had not shown
  before because a Run's figures normally open only when the Run ends, after the script has stopped
  changing them; an animation's `drawnow` loop only narrowed the window.
- **R2025b's rule.** Graphics are updated when the script reaches `drawnow`, `pause`, `figure`,
  `uiwait`/`waitfor`, a blocking dialog, or its end; between those points nothing is drawn, and input to a
  figure waits.
- **The limit.** `Interpreter.MaxSteps` ended any Run after 50,000,000 statements with "Step limit
  exceeded (the script ran too long - check for an infinite loop)". The show/hide launcher (two
  conditions of a warm-up and three passes over the sixteen head2head scripts) died in its sixth pass;
  R2025b ran the same launcher to the end. The Stop button, Ctrl+C and a caller's token already end a
  runaway loop through the cancellation check beside the step count, in the walk, the register machine
  and the emitted loop alike.

## Decision

1. **A graphics gate.** `JGraph.Core.Model.GraphicsGate` (rules in `GraphicsGateState`) orders scripts
   against the display:
   - `ScriptThread.Run`, the one road every script entry point takes, holds the gate around the work.
   - The script opens it (`Park`) where it waits for the display: `ScriptRenderPump.Flush` (`drawnow`,
     `exportapp`), showing a figure (`JGraphScriptGlobals.Display`), and the waits in `PumpWait` (`pause`,
     `pause` for a key) and `UiWait` (`uiwait`, `waitfor`, the blocking dialogs).
   - The display enters with `TryEnterDisplay` and never waits. Refused, it skips the read and asks to be
     called back (`WhenFree`) when the gate next opens. `FigureControl` does this for the paint (the
     surface keeps its last picture), the viewport size it writes into the figure, and the pointer and
     keyboard handlers (the input is dropped, as R2025b holds it). `PlotBrowserControl` does it for its
     rebuild.
   - Script threads do not exclude each other. A script blocked on the UI thread while holding the gate
     cannot deadlock it, because the display never waits; a script taking the gate back waits only for a
     paint already under way. The script reads what it is changing on its own thread freely.
2. **No statement limit by default.** `Interpreter.MaxSteps` is `long.MaxValue`, as R2025b has none. It
   stays settable for the tests that place a limit inside a compiled loop (ADR 0160).

## Consequences

- A figure shown mid-run is painted at the script's next `drawnow`, pause, wait or end, as in R2025b, and
  not in between. A window resized or uncovered while a script runs shows its last picture until then.
- The JGraph app's own figure documents are behind the same gate: during a Run they repaint at the Run's
  park points or its end.
- A script with a true infinite loop runs until stopped, in the app (Stop) and in `-batch` (Ctrl+C), as in
  R2025b. Item 260's test already stops its loop by the token.
- Tests: `GraphicsGateTests` (11): the rules on a gate of each test's own (the display refused while a
  script holds it, a park lets it in and runs what waited, taking the gate back waits for a paint, scripts
  do not wait for each other, nesting, a park off a script thread), `ScriptThread.Run` holding the
  process's gate, and a 60-million-iteration loop finishing. The scripting, deployment, timer, dialog, wait
  and loop classes pass (410 of 411; the one failure, the WebView2 echo page, fails the same way without
  this change).
