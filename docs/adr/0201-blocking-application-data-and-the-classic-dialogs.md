# ADR 0201 — Blocking, application data and the classic dialogs

## Status

Accepted, 2026-10-04. Stage U4 of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored). It builds on ADR 0198 (U1: `uicontrol`,
callbacks, frames and the component layer), ADR 0199 (U2: containers, units, `uifigure`) and
ADR 0200 (U3: the rest of `uicontrol`).

## Context

After U3 a script could build any classic interface but could not stop and wait for it. `uiwait`
and `uiresume` did not exist, `waitfor` delivered graphics callbacks but no timers, and none of
MATLAB's classic dialogs existed: `msgbox`, `errordlg`, `warndlg`, `helpdlg`, `questdlg`,
`inputdlg`, `listdlg`, `waitbar`, `dialog`, nor the system dialogs `uigetfile`, `uiputfile`,
`uigetdir`, `uisetcolor`, `uisetfont`, `uiopen`, `uisave` and `uiload`. `guihandles` was missing,
`guidata` kept its value beside the application data instead of among it, and `exportapp`
photographed whichever window was active, from the script thread.

R2025b's behaviour was measured first. The probes are in `tools/matlab-checklist/ui-probes/u4`
(headless: `u4_wait`, `u4_wait2`, `u4_guidata`, `u4_dialogs`, `u4_messages`, `u4_nargout`), and the
layouts of the three blocking dialogs come from U1's window session (`u1w_dialogs`).

## Decision

### Waiting

- `uiwait`, `uiresume` and `waitfor` wait in one loop on the script thread (`BlockUntil`). Between
  slices it delivers what `pause` delivers: queued callbacks, due timers, .NET's events and the
  devices'. `waitfor` therefore drains everything `pause` drains, which closes the gap U0 found.
- A figure has R2025b's hidden `WaitStatus`: `[]` until something waits, then `'waiting'` or
  `'inactive'`, written with abbreviated words too. `uiwait` blocks until it is no longer
  `'waiting'`, the figure is deleted, or the timeout passes; `uiresume` writes `'inactive'`.
- The argument forms and refusals are R2025b's, including the timeout's (a value under one second
  becomes one second with a warning; a vector, an empty and a NaN are refused in a timer's words,
  because R2025b's timeout is a timer).
- A callback that is waiting can be interrupted whatever its `Interruptible` says, as MathWorks
  documents. Without that a dialog opened from a callback that asked not to be interrupted could
  never be answered.
- A wait started 64 callbacks deep is refused (`JGraph:waiting:NestedTooDeep`): callbacks stop
  being delivered at that depth, so nothing could end it.
- Without a window `uiwait` gives R2025b's warning and waits all the same. A timeout, a timer or a
  deletion ends it. **A wait that nothing can end returns**: no window, no armed timer and nothing
  queued. R2025b hangs there.
- In the IDE the status bar says which verb is waiting, because statements typed meanwhile queue
  behind it.

### Application data

- `guidata` is one of the figure's application data, under R2025b's name `UsedByGUIData_m`;
  storing an empty value takes it out. `guihandles` is new: one field for each `Tag` that can be a
  field name, in `findall`'s order, objects sharing a tag sharing a field as a row, and `[]` for a
  figure with nothing tagged.
- `getappdata`, `setappdata`, `isappdata` and `rmappdata` refuse in R2025b's words and take the
  root (handle 0).

### The dialogs are figures

- `dialog`, `msgbox`, `errordlg`, `warndlg`, `helpdlg`, `waitbar`, `questdlg`, `inputdlg` and
  `listdlg` build figures out of the same objects a script uses: controls, an axes that fills the
  figure and holds the message as a `text`, and an `IconAxes` holding a 32 by 32 image. The trees,
  the tags, the units and the layout arithmetic are R2025b's. A script that finds a dialog's text
  by its tag and changes its font, or waits on the figure with `uiwait(msgbox(...))`, finds what
  R2025b gives it.
- The measures inside that arithmetic — how wide and how tall a line of text is — are this
  build's own, as a control's `Extent` is (ADR 0200). A dialog's size therefore differs from
  R2025b's by what the two fonts differ by. The fixture holds those numbers to a few points and
  everything else exactly.
- The four icons are drawn here from circles, a triangle and strokes. R2025b's are MathWorks'
  artwork; a script sees only their size.
- `waitbar`'s bar is R2025b's `uiprogressindicator`: a new component, drawn as a panel with a
  filled part, so it appears wherever panels do. Its message is the axes' title, as in R2025b.
- The three blocking dialogs wait in `uiwait`. With nobody to answer they refuse before they read
  their arguments, with R2025b's `MATLAB:hg:NonInteractiveFunctionSupport`.
- A test answers them through `ScriptGraphicsCallbacks.BlockingDialogShown`: it is told of the
  dialog's figure once it is built and presses its controls through the same door the window uses.

### What the dialogs needed from the rest

- **Text in device units.** An axes `text` takes `Units` of pixels, points, inches, centimeters
  and characters: its `Position` is then an offset from the lower left of the plot box, its
  `FontSize` is in points, its `Extent` is measured without a frame having been drawn, and it is
  not clipped to the plot box. Its `Position` is `[x y z]` (it read back as an annotation's box
  before), and a cell of lines reads back as that cell.
- **`MenuBar` is real.** `'figure'` is the default and `'none'` leaves the window plain: no
  toolbar (with `ToolBar` at `'auto'`), status bar, plot browser or inspector. This is what makes a
  dialog's window a dialog. It read `'none'` always before.
- **`WindowStyle` is real.** `'modal'` keeps the other figure windows from being used and stays in
  front; `'alwaysontop'` stays in front; `'docked'` is taken and remembered.
- `axes(...)` takes any axes property as a name-value pair; `line` and `text` take any property
  after their data; `image(..., 'Parent', ax)` no longer makes a figure; `delete([])` does nothing.
- A figure has the hidden `CurrentKey`.

### The system's dialogs

- `uigetfile`, `uiputfile`, `uigetdir`, `uisetcolor`, `uisetfont`, `uiopen`, `uisave` and `uiload`
  go through `IScriptNativeDialogs`, which the application implements with the Windows file,
  folder, colour and font dialogs on the UI thread. Without a host they refuse as R2025b does.

### `exportapp`

- It photographs the figure's own window, on the UI thread, after the figure is shown and what the
  script changed has reached the window. It writes the figure's area without the window's
  furniture, as `.png`, `.jpg`, `.tif` or `.bmp`, and refuses in R2025b's words without a display.

### What the window checks changed

- `-batch -showfigures` now tells the application to close the window of a figure the script
  deleted or closed (`BatchRunner.RunInSessionAsync` takes the host's close). The window check
  found it missing: a dialog's OK button deleted its figure and left the window up.
- That session announces itself (the event pump) before the script starts rather than after it
  ends, so a wait inside the script is a real one. Before, `waitfor(btn, 'Value', 1)` in a script
  with nothing else pending took the dead-wait way out with its window on screen.
- `set(h, 'XData', x, 'YData', y)` writes a series' two coordinates together, so one call can
  change how many points it has. It was refused with a message that advised exactly that call.
- While a wait goes on, what its callbacks changed is sent to the windows after each slice.

## Consequences

- ex1 (nested callbacks with `errordlg(..., 'modal')` and `questdlg`), ex2 (`guidata` and a modal
  `uiwait`), ex4 (a timer and `waitfor(fig)`) and ex5 (`waitfor(btn, 'Value', 1)`) run.
- The document format gains a figure's `MenuBar` and `WindowStyle`, a text's device units and the
  progress indicator. Older documents read as before.
- A figure whose script sets `MenuBar` to `'none'` loses JGraph's toolbar and panes, which is what
  the script asked for.

## Measured

- **Headless probes** (`ui-probes/u4/`): `u4_wait`, `u4_wait2`, `u4_guidata`, `u4_dialogs`,
  `u4_messages`, `u4_nargout`. Findings are in the probes' README. `u4_wait` ends in the runner's
  timeout by design: it is the probe that showed a wait started inside a timer's callback is never
  ended by another timer.
- **Parity fixtures**, recorded with `-noFigureWindows`: `u4_wait`, `u4_appdata` and `u4_dialogs`.
  `u4_dialogs` holds every tree, tag, unit and position exactly and the sizes that come from a
  text measure to a few points a line.
- **Unit tests**: `UiDialogsU4Tests` answers every blocking dialog through
  `ScriptGraphicsCallbacks.BlockingDialogShown` (each button of a question, Return and Escape,
  closing, the fields and dimensions of an input dialog, a list's picks and Select all), and
  covers their refusal with nobody to answer, `uiwait` held until a control's callback resumes,
  `waitfor` ended by a user's click, the dead wait, a message box's button and keys, the waitbar's
  filled part in the frame, what `MenuBar` does to a window's furniture, and the system dialogs
  through a stand-in `IScriptNativeDialogs`. The timeout, a timer's `uiresume`, a deletion and the
  refusals of every verb are in the fixtures.
- **The JGraph window check**: the Release app ran one figure of nine buttons under
  `-batch -showfigures`, driven through UI Automation patterns only (Invoke, Value, SelectionItem,
  SetFocus). `msgbox`, a modal `errordlg`, `questdlg`, `inputdlg`, `listdlg`, `waitbar`, a
  `uiwait` on a dialog the script built, and `exportapp` each opened, were answered and handed the
  script the right value; the main window was disabled while a modal dialog was up and enabled
  after; `exportapp` wrote the figure's 520 by 300 area. The process was stopped, not closed, and
  `workspace.json` was unchanged. The check found one fault, fixed: the `-batch -showfigures`
  session never told the application to close the window of a figure the script deleted, so a
  dialog's OK button deleted the figure and left its window (and, for a modal one, left every
  other window disabled).
- **ex4 and ex5 in a window**, by the same means (Toggle and the window's Close pattern): ex4's
  timer drew at its rate while the script sat in `waitfor(fig)`, Pause held it, and closing the
  window ran `CloseRequestFcn`, ended the wait and the process. ex5's Stop toggle ended its
  polling loop, `waitfor(goBtn, 'Value', 1)` held until the second toggle, and the result file
  was written. This check found the two faults under "What the window checks changed". ex1 and
  ex2 ran headless, answered through the test seam.

## Not measured

- No keystroke or mouse click was synthesized into JGraph, so Return, Space and Escape in a
  dialog's window, a double click in `listdlg` and a click on the dialog's empty area are covered
  by the unit tests, not by input in a window.
- The system's file, folder, colour and font dialogs were not opened: they are Windows' own
  windows and cannot be answered without input. Their builtins are tested against a stand-in.
- The depth cap (`JGraph:waiting:NestedTooDeep`) has no test: it takes 64 nested callbacks.
- The look of each dialog was not compared with R2025b's pixel for pixel.
- R2025b's bare `uiwait` resumed by a timer after 0.5 s took about 4 s in the probe; why is not
  known, and no fixture line depends on it.

## Divergences

- **A wait that nothing can end returns.** With no window, no armed timer and nothing queued,
  `uiwait` and `waitfor` come back; R2025b holds a batch run for ever. A script run headless ends
  instead of hanging.
- **A dialog's size is R2025b's arithmetic round this build's own text measure.** Widths and
  heights that come from the message's `Extent` differ from R2025b's by what the two fonts differ
  by (ADR 0200); the trees, tags, units, margins and button sizes are R2025b's.
- **The dialogs' icons are drawn here.** R2025b's are MathWorks' artwork; the `CData` a script
  reads from the `IconAxes` image is 32 by 32 as there, with other pixels.
- **The dialogs' callbacks are built-in functions, not nested functions of an M-file**, so
  `func2str` of a dialog button's `Callback` or of its `KeyPressFcn` gives another name. The OK
  button of a message box holds R2025b's text, `delete(gcbf)`.
- **`waitbar`'s title reads its `FontSize` in pixels** (13.33), where R2025b's reads 10 points:
  an axes title here has no `FontUnits`.
- **`uiprogressindicator` keeps a panel's property names** beside its own `Value`,
  `Indeterminate` and `ProgressColor`; R2025b's internal class has fewer.
- **A modal figure disables the other figure windows and stays in front, and leaves the IDE's own
  window usable.** R2025b's modal figure blocks the whole desktop. The IDE is where Stop is.
- **`WindowStyle` `'docked'` is taken and remembered and docks nothing**: there is no figure dock.
- **`exportapp` writes `.png`, `.jpg`, `.tif` and `.bmp` and refuses `.pdf`** (open item 45).
- **The blocking dialogs and the system's refuse, with R2025b's identifier, only when nobody can
  answer**; under a test's stand-in they run without a display, which R2025b never does.

## Still open

- A timer array (`delete([t1 t2])`) is refused (open item 43); the fixture deletes one at a time.
- The root has no `Default*` and `Factory*` properties (open item 44); the dialogs hold R2025b's
  font as constants.
- The theme redraws a title a script styled: `waitbar`'s message is drawn bold in a window (open
  item 40, extended).
- `uisetfont`'s and `uisetcolor`'s less common forms (a handle whose property is written, a title
  argument) are taken as R2025b's documentation gives them and were not probed, since R2025b
  refuses them headless.
