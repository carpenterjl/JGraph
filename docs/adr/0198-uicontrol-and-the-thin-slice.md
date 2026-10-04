# ADR 0198 — `uicontrol`, and the thin slice of app building

## Status

Accepted. Stage U1 of the app-building plan (`docs/plans/uifigure-app-building-plan.md`), after
U0 (0afdec5, edb41d8). One commit, this ADR's.

The stage runs the user's `D:\Temp_Broken_Scripts\test1\test_data.m` end to end: a figure with a
pixel `Position`, two `edit` fields, three `text` labels and a `pushbutton` whose nested callback
reads the fields and writes the result. It touches every risky mechanism of the plan once:
- a component model in Core, and `uicontrol` with R2025b's whole property surface;
- every graphics callback slot taking all three of MATLAB's forms, pinned while it is held;
- event data with MATLAB's class names;
- a user's value written through the script queue, before its callback is considered;
- a frame snapshot, a pure layout, a flush with one frame in flight, and a WPF layer fed only frames;
- the keyboard routed to the component that has it;
- `figure('Visible','off')` honoured;
- `-batch -showfigures` keeping its session for the windows it leaves.

## Context

Before U1 `uicontrol` was a refusal ("app building"), graphics callback slots took a function handle
alone, a text `CloseRequestFcn` other than `closereq` closed the figure it was meant to keep open,
`figure('Visible','off')` opened a window, and `-batch -showfigures` showed windows whose callbacks
never ran. U0 recorded R2025b's behaviour headless; three things needed a window and were recorded
here, with the user's leave.

## Decision

### The model

`JGraph.Core` gains `UiObject` (a component's `Position`, `Enable`, `Tooltip`) and `UiControlModel`
(the ten styles, `String`, `Value`, `Min`, `Max`, `ListboxTop`, `SliderStep`, alignment, colours,
font). `FigureModel.Components` holds them in creation order. Nothing in the renderer draws them.

- Colours are three doubles (`UiColor`), so `[0 0.45 0.74]` reads back as written. The model's
  `Color` is bytes, which would read back `0.4510`.
- `BackgroundColor` and `Value` follow the style until a script sets them: white for `edit`,
  `listbox` and `popupmenu`, grey otherwise; `Value` 1 for the list styles, 0 otherwise (measured:
  restyling an unset control changes its background, a set one keeps it).
- Text keeps the shape it was given (`UiText`): a char row, a char matrix, or a column cell.

### The property surface

`JgsGraphicsProperties.UiControl.cs` curates the forty names R2025b's `get` lists, plus the hidden
`TooltipString` and `UIContextMenu`. A component's table is curated whole: reflection is skipped, and
the drawn objects' `Selected`, `SelectionHighlight`, `HitTest` and `PickableParts` are removed,
because a `uicontrol` has none of them. Every coercion and refusal is R2025b's, measured headless
(`tools/matlab-checklist/ui-probes/u1/u1_uicontrol.out.txt`):
- words match in any case or by a unique prefix (`'push'`, `'te'`, `'ri'`); `'p'` is ambiguous;
- `String` from a number is its `num2str`, from a numeric array one line per element, from a string
  array or a cell a column cell; a logical is refused;
- colours take triplets of any numeric class, short and long names, `#rgb`/`#rrggbb` and `'none'`;
- the identifiers and sentences are R2025b's. Through `set` or the dot they start "Error setting
  property 'X' of class 'UIControl':" on a line of its own; inside the creating call they do not.
  `FontSize` 0 is refused bare in both, as R2025b refuses it.

`uicontrol` takes R2025b's forms: no argument, a parent, name-value pairs, `'Parent'` among them, or
a struct of properties, with R2025b's refusals for the rest. With no parent it uses the current
figure or makes one. A refused option leaves no half-made control behind.

### Callbacks: three forms, pinned, and R2025b's event data

`JgsGraphicsCallbackValues` says, once, what every graphics callback property takes: a function
handle, text, or a cell led by either. `''`, `[]` and `{}` clear; a string is stored as text; a cell
holding only a handle is that handle; any other cell is kept as a column (measured). `5`, `{5}` and a
struct are refused with `MATLAB:datatypes:callback:CreateCallback`. Unset reads `''`.

**A `CloseRequestFcn` is never "no callback"** (`u1_closereq`): R2025b stores every empty form as
`''`, which runs, does nothing and keeps the figure open. `'closereq'` is stored as the default, so
a window's close does not wait on the script thread for the same answer. Unset reads `'closereq'`.

Text runs in the base workspace; `{f, a, b}` runs `f(src, evt, a, b)`. The dispatcher, `close`,
`CreateFcn` and `CloseRequestFcn` all run through one helper, which is what fixes the text
`CloseRequestFcn` bug. A stored callback is pinned (`JgsLifetime.Pin`) for as long as its slot holds
it: `test_data.m`'s button callback is a nested function whose workspace outlives the function only
through the slot.

Event data are classed structs with R2025b's classes and field order, so `class(evt)` and
`isa(evt, 'event.EventData')` answer as MATLAB does:

| Callback | Class | `EventName` |
|---|---|---|
| a `uicontrol`'s `Callback` | `matlab.ui.eventdata.ActionData` | `Action` |
| `DeleteFcn` | `event.EventData` | `ObjectBeingDestroyed` |
| `CloseRequestFcn` | `matlab.ui.eventdata.WindowCloseRequestData` | `Close` |
| a figure's `KeyPressFcn`/`KeyReleaseFcn` | `matlab.ui.eventdata.KeyData` | `KeyPress`/`KeyRelease` |
| `WindowKeyPressFcn`/`WindowKeyReleaseFcn` | `matlab.ui.eventdata.KeyData` | `WindowKeyPress`/`WindowKeyRelease` |
| a component's `KeyPressFcn`/`KeyReleaseFcn` | `matlab.ui.eventdata.UIClientComponentKeyEvent` | `KeyPress`/`KeyRelease` |
| `WindowButtonDownFcn`/`WindowButtonUpFcn` | `matlab.ui.eventdata.WindowMouseData` | `WindowMousePress`/`WindowMouseRelease` |
| a figure's own `ButtonDownFcn` | `matlab.ui.eventdata.MouseData` | `ButtonDown` |
| anything drawn's `ButtonDownFcn` | `matlab.graphics.eventdata.Hit` | `Hit` |
| `WindowScrollWheelFcn` | `matlab.ui.eventdata.ScrollWheelData` | `WindowScrollWheel` |

The first three were measured headless (`u1_eventdata`), the rest in a window (`u1w_keys`). A
figure's `MouseData` has no `IntersectionPoint`; the old struct gave it one.

### A user's value goes through the queue

The window never writes the model. `ScriptGraphicsCallbacks.NotifyUserValue(component, value)`
queues a `ControlAction` carrying the new value and a sequence number. When the script thread
dequeues it, the value is written **first**, before the `Interruptible`/`BusyAction` gate decides
whether the callback runs. Stop discards callbacks and keeps the writes (`ApplyUserValue`). A script
that never yields keeps reading the old text, as in MATLAB.

The window keeps showing what the user typed until a frame arrives whose `UserWriteSeq` has taken it.
After that the model's text is the truth, so a callback that clears the field is obeyed. An edit
field commits on Enter and on losing the keyboard, and any press in the window commits a focused
field first, so its value reaches the script before the click does.

### Keys

R2025b's order, recorded in a window (`u1w_keys`):
- a press reaches `WindowKeyPressFcn` first, then the `KeyPressFcn` of whoever holds the keyboard;
- a release reaches the holder's `KeyReleaseFcn` first, then `WindowKeyReleaseFcn`;
- while a component holds the keyboard, the figure's own `KeyPressFcn` does not run.

`Character` comes from WPF's text input, not from a table of keys: a press that may type something
is held until its text arrives, and reported with its guessed character only if none does.

### Frames, the layout, and the layer

A component change marks its figure dirty (`InvalidationKind.Ui`, which the canvas ignores) and bumps
one process-wide epoch. At a flush point the script thread takes a `UiFrame` and hands it to the host:
- every statement boundary, unforced: one cheap read when nothing changed, and skipped while the
  figure's last frame is still in flight;
- the end of a run or a pump run, and `drawnow`: forced.

So 10,000 `set(h,'String',…)` calls cost 10,000 writes and a handful of frames (a test asserts at
most three). `UiLayout.Place` in `JGraph.Rendering` turns a frame and the figure's size into
top-left rectangles in DIPs: MATLAB's 1-based bottom-left pixels, which U0 measured to be DIPs. The
window's `UiComponentLayer` lies over the canvas, applies only frames (a newer one supersedes an
older), and lays the last one out again on resize. Its controls wear keyed styles from
`Themes/UiComponents.xaml`, so the IDE theme never reaches them. A window that opens later starts
from the figure's last frame.

### `figure('Visible','off')`, and `-batch -showfigures`

A figure with `Visible` off gets no window. Setting it on later touches the figure, so it is shown
when that statement ends; a shown figure set off is hidden.

`-batch -showfigures` now runs the script in a session (`BatchRunner.RunInSessionAsync`) that
outlives it. While its windows are open, the app delivers their callbacks there, and the session
ends, with the run's files and native host, when the last window closes. A callback's `exit` ends
the process with its code. Plain `-batch` keeps MATLAB's rule: the process ends with the script.

**The pump's busy flag is set before the pump run starts, never taken from the run's task.** The
window check found the bug the first version had: a drain that finishes before its first `await`
completes its task synchronously, and the finished task was assigned after its own `finally` had
cleared the field. The pump then looked busy for ever and delivered nothing more.

### Tooling

- `record-matlab.ps1` gains `-NoFigureWindows`. A fixture can also ask for it with a line reading
  `% record: -noFigureWindows`, as `u1_uicontrol` does, because plain `-batch` displays figure
  windows.
- `ScriptUiTrace` writes the road of a user's action (raised, queued, pumped, dispatched, frame
  applied) to the file named by `JGRAPH_UI_TRACE`, and is off otherwise. It is how the pump bug was
  found.
- `nargout-r2025b.tsv` gains `uicontrol` (1, built-in), recorded by `audit-nargout.m`, and
  `JgsBuiltinOutputCounts` is regenerated.

## Measured

- **Headless probes** (`tools/matlab-checklist/ui-probes/u1/`):
  - `u1_uicontrol`: defaults per style, every coercion and refusal;
  - `u1_eventdata`: the event data of the callbacks a script can fire;
  - `u1_closereq`: what an empty `CloseRequestFcn` holds and does.
- **Window probes, with the user's leave** (`u1w_*`):
  - `u1w_dialogs`: the layouts of `questdlg` (default, two buttons, long text, struct options),
    `inputdlg` (one field, two with defaults, multi-line) and `listdlg` (multiple, single with a
    prompt). They are transcribed in U4. `-batch` refuses blocking dialogs even with a display
    (`MATLAB:hg:NonInteractiveFunctionSupport`), so these ran under `-nodesktop -r`;
  - `u1w_keys`: `java.awt.Robot` typing and clicking into a figure. It recorded the key order, the
    event data, when an edit field's `Callback` fires (on Enter, and on a click elsewhere after
    typing, before that click's own callback), and that **a click on a `uicontrol` in a classic figure
    does not run `WindowButtonDownFcn`**;
  - `u1w_uitest`: `matlab.uitest` gestures on a uifigure, the oracle for U5. In a uifigure a click on
    a component does run `WindowButtonDownFcn`; `ValueChangingFcn` runs with the old `Value` still
    in place; `ButtonPushedData` and `ValueChangedData` (with `PreviousValue`) are the event data.
- **The parity fixture** `u1_uicontrol` (352 lines, recorded with `-noFigureWindows`) passes, with
  three stamped divergences below.
- **Unit tests**:
  - `UiControlU1Tests` (12): `test_data.m` end to end, both its answers, after the function returned
    and the workspace was cleared; `ActionData`; text and cell callbacks; the write surviving a
    failing callback and Stop; the old text until the script yields; key routing, key order and
    event data; the window's button events; the text `CloseRequestFcn` veto; pinning across
    `clear all`; 10,000 writes;
  - `BatchRunnerTests.ShowFiguresSession_KeepsAnsweringItsWindowsCallbacks_RoundAfterRound`.
- **The suites near the change**: callbacks, figures, handles, menus, graphics properties,
  completion, interaction and every parity fixture, 1,042 tests before the last fixes, then the
  failing ones again. Four tests encoded old non-MATLAB behaviour and now follow R2025b:
  - an unset `CloseRequestFcn` reads `'closereq'`;
  - `[]` keeps the figure open where it used to close it;
  - a figure's own click has no `IntersectionPoint`;
  - the window's key events are named `WindowKey…`.

  The lanes were not run: nothing here is maths.
- **The window check, with the user's leave**: the Release app ran `test_data.m` under
  `-batch -showfigures`, driven through UI Automation patterns only (set a field's text, move the
  keyboard, invoke the button). The window matched MATLAB's layout, and three rounds gave
  `Total Resistance: 75.00 Ω`, `Total Resistance: 1.000 kΩ` and the script's error text in its
  colour. The process was stopped, not closed, and `workspace.json` was unchanged.

## Not measured

- The window check never typed real keystrokes or clicked the screen. A first attempt that did sent
  its keystrokes into the user's own foreground window, because Windows would not bring JGraph's
  forward. So the text-input path for `Character` and the press-commits-first path are covered by the
  code and the unit tests, not by a person's input.
- The IDE path (Run in the editor, callbacks through the console's idle pump) was not driven in a
  window; it shares the session, the queue and the layer with the batch path that was.
- `Extent` is an estimate from the font size (half an em a character, 1.25 em a line) until U3
  measures text.

## Divergences

- **`class` of a `uicontrol` handle is `'double'`**, where R2025b answers
  `'matlab.ui.control.UIControl'`. Handles are numbers (ADR 0051); type-aware handles (decision Q2)
  arrive with App Designer's typed properties (`u1_uicontrol`, `class`).
- ~~**A component's `Units` is `'pixels'` only until U2's units engine.** R2025b takes
  `'normalized'`, `'characters'`, `'points'`, `'inches'` and `'centimeters'`; JGraph refuses them by
  name (`units_normalized`).~~
  — **retired 2026-10-04 by U2 (ADR 0199)**, which brought the units engine.
- **A deleted handle is refused without R2025b's identifier.** R2025b raises
  `MATLAB:class:InvalidHandle` ("Invalid or deleted object."); the registry cannot tell a deleted
  handle from a number that never was one (`set_on_deleted`).
- ~~**`Children` puts a figure's components before its axes**, whichever was made first. R2025b lists
  them newest first across kinds; U2 gives the figure one tree of children.~~
  — **retired 2026-10-04, never a divergence.** R2025b lists every component before every axes too,
  newest first within each (U2's probe `u2_tree`, ADR 0199).
- **`Extent` is an estimate** until U3 measures the drawn text.
- **The pointer's motion still hands its callback `[]`.** R2025b's event data for it is not
  recorded yet. (A resize was on this line until 2026-10-04: U2, ADR 0199, recorded R2025b's
  `SizeChangedData` and hands it over.)

## Still open

- The rest of the styles are drawn as labels until U3; their properties are already R2025b's.
- `HandleVisibility 'callback'`, `Interruptible` and `BusyAction` keep the generic refusals'
  wording rather than R2025b's (U2).
- The ownership audit is red on HEAD for reasons that predate this stage (open item 38);
  `uicontrol` is on `ScriptRunningBuiltins`, because its `CreateFcn` runs script code.
