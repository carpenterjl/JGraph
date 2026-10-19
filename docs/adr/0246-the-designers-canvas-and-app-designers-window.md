# ADR 0246 — The designer's canvas, and App Designer's window

## Status

Accepted, 2026-10-18. Stage DS2 of the visual app designer plan (`docs/plans/app-designer-plan.md`,
gitignored), after DS6 (ADR 0245), with the window decision DD3 (the designer is its own top-level
window, a tab per app, as R2025b's App Designer is). App Designer's canvas rules were read from
R2025b's own canvas code (`toolbox/matlab/appdesigner/web/release/bundle.mwBundle.appdesigner.js`:
`AlignmentHintManager`, `FixedHintCalculator`, `ResizeCalculator`, the component move and resize
gestures, `RubberBandManager`, `WidgetSelectionRules`, `UndoRedoManager`, the keyboard mover and the
view state's defaults); nothing of it is kept. The recordings are the probes
`tools/matlab-checklist/ui-probes/ds2/ds2_sizes` and `ds2_appdesigner`, headless, 2026-10-18, and
the dispatch sweep `tools/matlab-checklist/dispatch/dispatch_probe17`, headless, 2026-10-18.

## Context

DS1 to DS6 gave the designer a model of an app's components, read from and written to the code
and App Designer's tree. Nothing showed it. DS2 is the canvas: the app's figure drawn as it runs,
with App Designer's design gestures - selection, move, resize, the alignment guides, the grid, the
arrow keys, undo and redo. The plan's architecture asks that the canvas be the runtime's own
renderer in design mode, since a second renderer would drift.

## Decision

### The canvas is the runtime (`DesignRuntime`)

The design's components are made as `createComponents` makes them: each component's generated
lines (`DesignCodeWriter`) run, line by line, by an interpreter the designer owns, in a figure the
designer owns. What the canvas draws is that figure, through `FigureControl` and the component
layer, exactly as a running app's window draws it.

- **No script reaches it.** The figure is `uifigure`'s model (`JgsBuiltins.NewUiFigureModel`), never
  registered, so it has no number and no search or `close all` finds it. A new console session
  cannot be used for this - it resets every figure - so the interpreter is a bare one, with the
  builtins a session registers.
- **It reaches no script.** While the designer's interpreter runs, the console's session is hidden
  from that thread (`JgsCallbackDispatcher.Isolate`): no `CreateFcn` fires into it, no warning is
  written to its console, no relative path is resolved against its folder. Its statement
  boundaries send no frames (`ScriptComponentFrames.HoldOnThisThread`): a flush would take frames of
  the figures a script on another thread is writing. The designer takes its own frames
  (`ScriptComponentFrames.TakeFrame`, which settles sliders and containers as a flush does).
- **Its handles live while the designer holds it.** The handle registry keeps a kept figure's
  objects through the sweep that closing a window runs (`JgsHandleRegistry.Keep`), and lets them go
  when the designer does (`Release`). A console `clear` forgets every handle, the designer's too, so
  each call binds `app` again from the designer's own objects.
- **Design time is not run time.** No callback is wired. A component whose `Visible` is off is made
  visible and drawn faded, as App Designer's canvas draws it (35% opaque, its stylesheet). A
  `uihtml`'s page is not run (`UiComponentLayer.DesignMode`). A tab's heading shows its tab.
- **Where a component is** is where the window draws it: the shared layout's box (`UiLayout`), which
  scrolls a scrollable container's content to its top and shows one tab of a group. A `Position`
  turns into a box through the parent's inner area as laid out (`ToSurface`, `ToPosition`).

### Edits (`DesignDocument`)

An edit changes the runtime's component through its own set and reads back what it kept into the
design: a knob resized out of square comes back square, as the runtime (probe `u9_extra`, again
`ds2_sizes`) and R2025b keep it. A gesture shows on the runtime's components as it goes and becomes
one edit when it ends; a cancelled one puts them back. A component dropped into a panel, a button
group or a tab moves into it, last among its children, its `Position` now the container's; a radio
or toggle button stays in a button group. Undo and redo hold 200 steps, as App Designer's do, and a
new edit clears what could be redone. Only components placed by their positions move: not the
figure, a tab, or a grid's child (grids are DS4's).

A save writes `createComponents` again from the design into the app's text, and the component
properties block when it no longer declares the design's components (`DesignCodeWriter.Splice`);
every other line is kept, line endings and all, and App Designer's tree follows (DS6). On code App
Designer laid out, an unchanged design gives the text back. A design read with lines the reader
cannot place is shown but not saved: a save would lose them.

### App Designer's gestures (`DesignGestures`, `DesignGeometry`)

UI-free, so that each rule is tested without a window; the canvas turns its mouse and keys into
surface points and draws what the gestures leave.

- **Guides.** A moving box's left edge, centre and right edge each snap to the nearest left or right
  edge or centre of every other shown box, and of the figure, within 7 pixels; top, middle and
  bottom likewise. Edges are counted a pixel inside the box and centres as App Designer rounds them.
  A box 10 pixels or thinner aligns across that thinness by its centre only; two boxes both 28 or
  smaller whose sizes differ by 1 to 5 align by centres only. The guide runs 20 pixels past both
  boxes. A resize aligns the edges it moves, and marks an edge it keeps that is aligned already.
- **The grid.** Off by default; when shown, with Snap to Grid (on by default), a move puts the
  dragged component's corner on the grid of its container and a resize puts its moving edges there.
  The interval is 10, from 10 to 50 in steps of 5.
- **Resizing.** Eight handles; Ctrl grows about the centre, Shift keeps the proportions. The least
  size is 10 by 10, a tab group's and a button group's 100 by 30. A shape the runtime keeps (knobs,
  lamps, round gauges, switches) keeps its ratio with its far corner where it was; a slider has
  handles along its length only.
- **Selection.** A press selects; Ctrl or Shift adds a component of the same container, takes a
  selected one out, and selects one of another container alone. A press on one of several selected
  keeps them for a drag and selects it alone if no drag follows. A drag from the figure, a tab, a
  grid, or a panel's or button group's body away from its 6-pixel edge draws a rubber band, which
  selects the components it holds whole, in the container of the one nearest where it began; a
  container held is selected rather than what is in it. Ctrl+A selects the selection's container's
  components; an empty selection is the figure.
- **Keys.** The arrows move by a pixel, by ten with Shift; a run of them is one edit, ended when no
  arrow is held. Escape cancels a gesture.

### The window (`AppDesignerWindow`) and `appdesigner`

The designer is its own top-level window with a tab per app, a toolbar (Open, Save, Undo, Redo, Show
Grid, Snap to Grid, the interval, Alignment Hints) and a status line that names the selection and
its `Position`. It is reached three ways:

- **File > Open in App Designer** on an `.mlapp` open in the editor. The editor still opens an app as
  its code (the designer has no code view until DS5). A save in the designer reloads an editor tab
  showing the app when that tab has no unsaved changes, and says so when it has.
- **`appdesigner(filename)` and `appdesigner`**, as MATLAB's: the file found and checked as R2025b's
  `getValidatedInputAppFileName` does it, in its words (`ds2_appdesigner`): a char row or string
  scalar, no keyword, a name that starts with a letter (R2025b's `namelengthmax`, 2048), `.mlapp`
  added when there is no extension and exactly that extension otherwise (case sensitive), found in
  the current folder or on the path, and spelled as the file is on disk. It takes one argument and
  answers nothing (`nargout` 0, held as a file: `nargout-r2025b.tsv`, regenerated). It reaches the
  app through `IScriptAppDesigner` on the script context, as `serialExplorer` reaches its pane, and
  waits for the window, so that a design the canvas cannot read is the call's error.
- **The command line**: `JGraph.Application.exe -r "appdesigner('C:/apps/MyApp.mlapp')"` opens the
  app at start, as `matlab -r` does. `-r` runs in the console's language, so a JGS console reads a
  backslash in quotes as an escape; forward slashes work in both.

The window asks about unsaved apps when it closes, and JGraph's own close asks it first.

## Measured

- **The canvas's model** (`AppDesignerCanvasTests`, 29). The fixture app's every class is made with
  no refusal but its missing image, in a figure no number names, and every handle goes on dispose;
  each component placed by its position, on both of its tabs, lands where it is drawn and back; the
  guides, the grid, resizing, the rubber band and selection by hand-worked boxes; moves, a drop into
  a panel and out by undo, a knob's shape, a cancelled gesture, splicing, a save and reopen; and the
  gestures end to end - a drag, a snap, the grid, a handle, a band, a press on one of several, a drop,
  a run of arrows, Escape, a tab's heading.
- **Every app R2025b ships** (`EveryShippedAppOpensOnTheCanvas`, local). All 44 open; 471 components
  placed by their positions land where they are drawn; a move and its undo give each app's first
  one back. The runtime refused 17 lines, listed and not failed: ten images and a page whose
  files are not beside the app, and the three Simulink components in each of two apps (open item
  148), which have no box.
- **`appdesigner`** (`AppDesignerCommandTests`, 14): each refusal in R2025b's words, the file found
  and spelled, the window's refusal, a session with no window.
- **The window** (the Release app started with `-r "appdesigner(...)"` on copies of two shipped
  apps, captured with PrintWindow, the grid shown through UI Automation; no synthesized input,
  ended by Stop-Process, `workspace.json` unchanged). Both apps drawn as their code makes them, the
  figure selected, hidden components faded, the grid over the figure.
- The full suite: 11,489 passed. The seven verify scripts pass; the dispatch sweep has `appdesigner`
  going to the file under all 37 patterns.

## Not measured

- **The mouse in the real window.** UI Automation cannot drag, and synthesized input is not used; the
  gestures are tested through `DesignGestures`, which the canvas only feeds.
- R2025b's App Designer window beside JGraph's, gesture for gesture; a dark theme; a high-DPI screen.

## Consequences

- `JGraph.Scripting.Designer` has `DesignRuntime`, `DesignDocument`, `DesignGestures` and
  `DesignGeometry`; `JGraph.Controls.Designer` has `DesignCanvas`; the app has `AppDesignerWindow`.
- `JgsCallbackDispatcher.Current` is null on an isolated thread; `ScriptComponentFrames` holds per
  thread; `JgsHandleRegistry` keeps figures; `UiComponentLayer` has `DesignMode` and `TabHeadingAt`;
  `JgsGraphicsProperties.AspectRatioOf` is internal.
- The builtin count is 1,231 of 2,024 (`appdesigner`). `nargout-r2025b.tsv` gained `appdesigner`,
  and the `xregion` and `yregion` rows its generated table had been given by hand.
- No running script behaves differently.

## Divergences

- **`appdesigner` in a session with no window refuses** (`JGraph:appdesigner:NoWindow`): the CLI and
  a batch run have no window to show. R2025b under `-batch` passes its web-window check
  (`ds2_appdesigner`) and opens App Designer.
- **`appdesigner` with no file brings up the designer's window as it is**, with Open, not App
  Designer's start page.
- **A design the canvas cannot read is refused** (`JGraph:appdesigner:CannotOpen`), where App
  Designer opens any app from its tree. The designer reads the code (DS1), and an app whose
  `createComponents` it cannot read at all has no design.
- **The canvas's marks are its own**: the selection outline, the handles, the guides' colours, the
  faded overlay for a hidden component. During a resize the guides align the moving edges and not
  the centre; a container moves when pressed within 6 pixels of its inner area's edge or on its
  title, where App Designer marks a hotspot.

## Still open

- **A label and its field move separately.** App Designer keeps a labelled component as a component
  group, which the designer does not model (open item 150).
- **The canvas's other tools** (open item 151): resizing the figure, Align, Distribute and Same Size,
  reordering, Ctrl-drag to copy, zoom, the menu bar and toolbar on the canvas, a tree's nodes, and
  remembering the window's place.
- Adding, deleting and renaming components (DS3); grids (DS4); the code view (DS5).
