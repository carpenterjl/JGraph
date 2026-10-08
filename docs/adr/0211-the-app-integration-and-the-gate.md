# ADR 0211 — The app, the documentation and the gate for apps

## Status

Accepted, 2026-10-07. Stage U12, the last, of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored). It closes the arc ADRs 0198–0210 built:
`uicontrol` (U1), containers and units (U2), the classic styles (U3), waits and dialogs (U4),
`uifigure` components (U5, U9, U9b), the class language (U6), App Designer apps (U7, U7b), tables,
tabs and bars (U8), custom components (U10) and GUIDE with MATLAB's `.fig` files (U11). It builds on
ADR 0069 (measuring property coverage by asking the running build) and ADR 0051 (a graphics handle
is a number).

## Context

The plan gave U12 three parts and an exit:

- the Editor and the Workspace pane: component handles display usefully, and an `.mlapp` appears in
  the file browser and runs from the Run button;
- an "Apps and GUIs" chapter in the guide;
- `probe-properties.py`'s KINDS gain the `matlab.ui.*` classes, and property coverage is
  regenerated;
- **exit:** every `verify-*-coverage.py` passes, and all acceptance scripts run.

The `.mlapp` half of the first part had already landed in U7 (ADR 0204): `ScriptWorkspace` lists
`.mlapp` with the script extensions, the file tree and Open show its code, and Run constructs the
class. The Workspace pane did not: a component was a bare number, `1000001.5`, with class `double`.

Regenerating the coverage was the part that found work. The first run with the new rows read
**2,832 of 2,868** documented properties across 75 kinds, three kinds failing outright, and a
handful of names missing from kinds that otherwise answered everything. Each was checked against
R2025b headless before anything was changed (`ui-probes/u12/u12_props`, `u12_more`).

## Decision

### The Workspace pane names a component's class

`JgsRunner.HandleBrief` gives the pane's Value column MATLAB's wording for handles: `1×1 Button
(1000001.5)` for one, `2×1 Line` for an array of one class, `2×1 graphics array` for a mix. The
Class column still says `double`, which is what `class(h)` answers (ADR 0051). Only **minted**
handles are named: they are never whole numbers, so a figure's number is shown as the number it is,
because the pane cannot tell the 1 of `figure(1)` from any other 1. Only an array whose every
element is a live handle is named, so a number that merely matches a deleted handle, or an array
longer than a thousand elements, is shown as numbers. It is a lookup per element after each
statement, under the registry's lock.

### The guide's chapter

`docs/jgs-scripting-guide.html` gains an "Apps and GUIs" group: what the four ways of writing an
app are, running one (Run, the console's workspace, when callbacks run, Clear Workspace, `-batch`
and `-showfigures`, the dead-wait rule, the Workspace pane), classic figures, `uifigure` apps,
App Designer apps (editing and saving back, Export to `.m`, no layout designer yet), GUIDE and
MATLAB's `.fig` files, and where apps differ. Its two examples were run in R2025b and in JGraph and
print the same (`u12_examples`). The "Running MATLAB files" note, which still listed `classdef` and
struct arrays as unsupported, now lists what is: `parfor`, arrays of class objects, and toolboxes.

### The property probe

`probe-properties.py` gains 47 rows: the root, `uifigure`, `uicontrol`, every container and
component kind U1–U9b made, the three switch classes and four gauge classes each by its own
maker call, the toolbar classes, `uiprogressdlg`, `uistyle` and both axes toolbar button classes.
`ComponentContainer` has no maker (it is reached only through a class file) and `FigureAPPD` is the
documented table's App Designer view of a figure, so both stay unmeasured rather than scored as
zero. The template learned three things, each from R2025b:

- **A hidden property is counted as answered.** R2025b leaves a `uicontrol`'s `TooltipString`,
  `HitTest`, `Selected` and `SelectionHighlight`, a `uitable`'s `Extent` and
  `RearrangeableColumns` and the toolbar tools' `HitTest` out of `get(h)`, and answers each through
  `get(h, name)` and `isprop` (`u12_props`). This build does the same, so the probe asks each
  documented name `get(h)` left out and counts the ones that answer; the page gains a Hidden column.
- **An object may have no `Type`** (a `ProgressDialog`) and **no `get`** (a `Style`, whose names
  are its `properties`), in R2025b as here.
- **`contour` with one output is the contour matrix**, as in R2025b, so the object is the second
  output. The three contour rows of the handle list read `not-a-handle` until they asked for it.

The regenerated page reads **2,914 of 2,930** across 78 kinds, none unreachable, and all 75
drawing verbs hand back a handle. What is missing is the sixteen geographic names of `line`,
`scatter` and `bubblechart` (`LatitudeData` and the rest), which need geographic axes.

### What the coverage found, fixed

- **The root lacked four names R2025b lists** (`u12_more`): `CallbackObject` (what `gcbo`
  answers, read-only, empty outside a callback), `FixedWidthFontName` (`'Courier New'`, text only,
  refused otherwise with `MATLAB:class:RequireString`), `PointerLocation` (the pointer in the
  root's `Units`, a 1×2 pair; a write moves the pointer, as R2025b's does; a wrong shape is
  `MATLAB:datatypes:Point2dDataType:ArrayShape`) and `ScreenDepth` (32, stored when written, as
  R2025b stores it). `UiScreen.Pointer` reads the pointer through `GetCursorPos` scaled by the
  monitor it is on, and a test can stand a point of its own in for it (`SetTestPointer`) so no
  test moves the user's mouse.
- **An axes toolbar button had no callback of its own.** R2025b's `ToolbarPushButton` has
  `ButtonPushedFcn` and its `ToolbarStateButton` `ValueChangedFcn`, each absent from the other
  class (`MATLAB:noPublicFieldForClass`). One model stands for both here, so `GraphicsProperty`
  gains `OnlyWhen`, a per-object condition `NamesOf` and `TryFind` honour: the push style lists and
  answers only `ButtonPushedFcn`, the state style only `ValueChangedFcn`. A press in the window
  (`FigureControl.ToolbarButtonPressed`) now queues the button's own callback through
  `ScriptGraphicsCallbacks.NotifyToolbarButton`, with R2025b's
  `matlab.graphics.controls.eventdata.ButtonPushedEventData` (`Source`, `Axes`, `EventName`) or
  `ValueChangedEventData` (and `Value`, `PreviousValue`), in the order R2025b's classes list them.
  The class word in their errors is R2025b's (`ToolbarPushButton`).
- **`h(1)` on one object was refused** as "Cannot call a matlab.ui.style.Style; it is not a
  function", for any class object, a user's `classdef` included. A paren subscript of one object is
  now applied to a stand-in one-element array, so `s(1)`, `s(1, 1)`, `s(end)`, `s(:)`, `s(true)`
  and `s()` are the object, `s(1).FontWeight` reads through, and a subscript out of range is
  refused exactly as a one-element array's is. A pick of none or of several would make an array of
  objects, which open item 65 covers, and is refused in those words.

## Measured

- Fixture `u12_props` (39 lines, 2 recorded divergences): the root's four names, defaults, writes
  and refusals, `CallbackObject` inside a `DeleteFcn`; both toolbar button classes' callbacks,
  lists, `isprop`, the creation pair, refusals; one object subscripted seven ways.
- `AppIntegrationU12Tests` (5): the Workspace pane's text for a component, an array, a mix, a
  figure number, a deleted handle and a near number; a toolbar push and a state flip reaching their
  callbacks with R2025b's event data through the window's seam; a press with no callback queuing
  nothing; `PointerLocation` in pixels and normalized, and a write moving the stand-in pointer.
- **All nine acceptance scripts run** (research C's `apps/examples`, `ex1`–`ex9`): seven to the end
  headless; `ex4` (a timer while `waitfor` holds the window) and `ex5` (a loop until a toggle is
  pressed) wait for a person in R2025b too, and ran under the JGraph window check in the Release app
  by UI Automation patterns only (Toggle, Window.Close): `ex4`'s rate text advanced, stood still
  on Pause and the script ended on Close with its message; `ex5` enabled Save after Stop and wrote
  its result file.
- Property coverage regenerated (above), the five `verify-*-coverage.py` scripts, the divergence
  harvest and the parity ratchet at the gate.

## Not measured

- **A toolbar press in a MATLAB window**: which of the button's callback and the toolbar's
  `SelectionChangedFcn` R2025b runs first (here the button's), and the toolbar's own event data
  (open item 78). The press is tested through the window's seam, not by a click, because the
  toolbar is drawn over the canvas and has no automation element.
- **`PointerLocation` across monitors of different scale**: each point is scaled by the monitor it
  is on, which matches R2025b on one monitor; a pointer on a second monitor at another scale was not
  compared.

## Divergences

- **A subscript of one object out of range is refused in this build's words**, as any array's is:
  "Index 2 is out of range for length 1 (indexing is 1-based)." with no identifier, where R2025b
  says `MATLAB:badsubscript`, "Index exceeds the number of array elements. Index must not exceed
  1." (`u12_props`: `obj_two`, `obj_zero`; open item 79).
- **The Workspace pane shows a figure's number as a number**, where MATLAB's pane says `1×1
  Figure`: a handle is a number here (ADR 0051), and a figure's cannot be told from any other.
- **An axes toolbar button's callback runs before the toolbar's `SelectionChangedFcn`**, an order
  not yet measured in R2025b (open item 78).

## Consequences

- The app-building plan is complete: U0–U12, ADRs 0198–0211.
- A property of one model that only some of its objects have is now a table entry with `OnlyWhen`
  rather than a second model type.
- A component handle in the Workspace pane says what it is; a script that wants the class still
  asks `isa`, since `class` answers `double`.

## Still open

- The toolbar's press order and event data in a MATLAB window (open item 78).
- Subscript refusals in R2025b's words (open item 79).
- Arrays of objects, and of styles in `StyleConfigurations` (open item 65).
- The IDE theme painting over a script figure's colours, seen again in U11's GUIDE windows (open
  item 40).
