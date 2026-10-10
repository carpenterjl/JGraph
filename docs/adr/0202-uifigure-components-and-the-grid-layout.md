# ADR 0202 — The `uifigure` components, batch 1, and `uigridlayout`

## Status

Accepted, 2026-10-04. Stage U5 of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored). It builds on ADR 0198 (U1: `uicontrol`,
callbacks, frames and the component layer), ADR 0199 (U2: containers, units, `uifigure`),
ADR 0200 (U3: the rest of `uicontrol`) and ADR 0201 (U4: waiting and the classic dialogs).

## Context

After U4 a script could build and run any classic interface. A `uifigure` existed and held only
what a classic figure holds: none of the components an app is written with existed, there was no
`uigridlayout`, `uiaxes` made an ordinary axes in a figure of its own, and the dialogs an app lays
over its own window (`uialert`, `uiconfirm`, `uiprogressdlg`) were missing. The plan's third
example, a filter designer written with a grid, a `uiaxes`, a slider's `ValueChangingFcn` and a
`uialert`, could not run past its second line.

R2025b's behaviour was measured first, headless (`tools/matlab-checklist/ui-probes/u5`:
`u5_matrix`, `u5_grid`, `u5_forms`, `u5_dialogs`, `u5_dialogs2`). The event structures of the
dialogs' `CloseFcn` were read from R2025b's installed source.

## Decision

### The components

- Thirteen functions make sixteen kinds of component: `uilabel`, `uibutton` (push and `'state'`),
  `uieditfield` (text and `'numeric'`), `uitextarea`, `uidropdown`, `uilistbox`, `uicheckbox`,
  `uiradiobutton`, `uitogglebutton`, `uislider` (and `'range'`), `uispinner`, `uiimage` and
  `uihyperlink`. Each is a model in `JGraph.Core` (`UiComponents.cs`), a frame in the snapshot,
  a placement in the shared layout and a WPF element in the component layer, the road U1 laid for
  `uicontrol`.
- Every component has R2025b's property names, defaults and refusals. A refusal is
  `MATLAB:ui:<Class>:<id>` with R2025b's sentence; a refusal inside the call that makes a
  component is `MATLAB:ui:<Class>:unknownInput`, as there. In a making call `Value` and
  `ValueIndex` are applied after everything else, whatever order the pairs came in.
- Values follow R2025b's coercions. A string scalar is a char row where a text is wanted and is
  **not** one where R2025b keeps the difference: `Items`, `ItemsData`, `ValueIndex`,
  `MajorTickLabels`, a numeric `Value`, `RowHeight` and `ColumnWidth`. To carry that, `set` now
  passes its arguments through as they were written, and each property says whether it takes a
  string as text.
- An enumerated word is matched whole, in any case; only `InputType` takes an abbreviation. This
  is what R2025b does and is unlike `uicontrol`, which abbreviates everywhere.
- A `uidropdown` and a `uilistbox` hold `ItemsData` of any class beside their `Items`; `Value` is
  the datum when there is data and the text when there is none, and `ValueIndex` is the position
  either way.
- `uiradiobutton` and `uitogglebutton` live only in a `uibuttongroup` of a `uifigure`. The first
  one made is selected; taking the selection from the selected one gives it to the first; a group
  of one cannot be emptied (`noButtonSelected`); radio and toggle buttons do not mix in one group.
- A slider's ticks are R2025b's when a script sets them. Left automatic they are computed here
  (see Divergences).
- `focus(c)` asks the window to give a component the keyboard; on a figure that is not shown it
  warns, in R2025b's words, and returns.

### What a person does

- The window reports through one door, `ScriptGraphicsCallbacks.NotifyComponent(component,
  action, value)`: `"value"` for a value that has settled, `"changing"` for one on its way,
  `"pushed"`, `"clicked"`, `"doubleclicked"`, `"opening"`, `"image"` and `"link"`. As in U1 the
  value is written on the script thread before anything decides whether a callback runs, so a
  busy or stopped script does not lose what was typed.
- `"changing"` events coalesce: a drag that reports a hundred positions queues one
  `ValueChangingFcn`, with the latest. The settled value is never dropped.
- Each callback is told by R2025b's event class: `ValueChangedData` (`Value`, `PreviousValue`),
  `ValueChangingData`, `ButtonPushedData`, `SelectionChangedData` for a group, and the others
  named in `JgsUiComponentEvents`.
- A numeric field that is given text it cannot read, or a number outside its `Limits`, keeps its
  value and shows it again; no callback runs.

### `uigridlayout`

- A grid fills the area its parent gives its children. Its tracks are pixels, `'fit'` or a
  weight (`'2x'`), with `Padding`, `RowSpacing` and `ColumnSpacing`. The arithmetic is R2025b's
  as `u5_grid` measured it once the layout had settled, and lives in one pure function,
  `UiGridMath.Arrange`, which the model, the renderer and the window all call:
  - tracks are laid from the left and from the top, inside the padding;
  - a `'fit'` track is the largest size asked for by a child that sits in it alone, and a child
    spanning several shares what it still needs among the `'fit'` tracks it spans;
  - a `'fit'` track no child reaches takes no room and no spacing;
  - weighted tracks share what is left and never go below nothing;
  - asked what size it would itself like to be (a grid in a `'fit'` track of another), a grid
    sizes its weighted tracks to what is in them.
- A child goes where its `Layout.Row` and `Layout.Column` say (a number or a two-element span). A
  new child takes the cell after the furthest one in use, row by row. `RowHeight` and
  `ColumnWidth` read back with `'1x'` tracks added for as far as a child reaches. A child moved
  from one grid to another keeps its cell.
- A grid owns its children's `Position`: writing one warns
  (`MATLAB:ui:components:noPositionSetWhenInLayoutContainer`) and changes nothing, and reading
  one gives the cell. A grid's own `Position` cannot be written. `uicontrol` is refused in a grid.
- **The size a component asks for** (`UiFit`) is R2025b's rule round this build's text measure:
  a line is 1.23 font sizes high, a label is its text and two pixels, a button its text and ten
  by a line and eight, an edit field ten and 9.44 font sizes wide, and so on for every kind
  (the table is in the probes' README). Text widths now include pair kerning, which R2025b's
  include.
- **`Scrollable` scrolls.** A scrollable grid whose tracks do not fit shows a bar along the edge
  it overflows and lays out in what the bars leave. The window's bars and the wheel tell the
  script side where the grid is scrolled to (`ScrollX`, `ScrollY` on the model), and the next
  frame moves it; the renderer moves an axes in the grid with it.

### `uiaxes`

- `uiaxes(parent)` makes an axes that is a child of a `uifigure`, a panel or a grid: `Units` of
  pixels, `Position` its outer rectangle (`[10 10 400 300]` at first), `NextPlot`
  `'replacechildren'`, `BackgroundColor` `'none'`, never the current axes. A plot into it replaces
  its children and keeps its title, labels and grid, which is what `'replacechildren'` means and
  what an app relies on. In a grid its cell is its place.

### The dialogs over a figure

- `uialert`, `uiconfirm` and `uiprogressdlg` are overlays of the figure (`UiOverlayModel`, in
  `FigureModel.Overlays`), drawn by the window over everything else in it, with the figure dimmed
  behind a modal one. They are not figures, as R2025b's are not.
- `uialert` returns at once and runs its `CloseFcn` when dismissed. `uiconfirm` waits in U4's
  loop and answers the option pressed, or its `CancelOption` when dismissed. `uiprogressdlg`
  returns an object with R2025b's ten properties; `close(d)` and `delete(d)` take it down, and a
  press on its Cancel button sets `CancelRequested`.
- The `CloseFcn` structures are R2025b's: `Source`, `EventName` (`'AlertDialogClosed'` or
  `'ConfirmDialogClosed'`), `DialogTitle`, and for a confirmation `SelectedOptionIndex` and
  `SelectedOption`.
- On a figure that is not shown `uialert` and `uiprogressdlg` raise
  `MATLAB:uitools:uidialogs:InvisibleFigure`; given what is not a figure,
  `InvalidFigureHandle`; `uiconfirm` with nobody to answer raises
  `MATLAB:hg:NonInteractiveFunctionSupport`.

### What the stage changed elsewhere

- **A dead handle is refused in R2025b's words.** `JgsHandleRegistry.Require` said "This is not
  a handle to a figure object…" for every dead handle; it now raises `MATLAB:class:InvalidHandle`,
  "Invalid or deleted object.", which is what `set(d, …)` on a closed progress dialog and on any
  other deleted object gives in R2025b.
- `c{k}.Prop = v` writes a property of a handle held in a cell.
- `polyval` takes complex coefficients and points (ex3 evaluates a filter on the unit circle).
- `close(h)` takes a progress dialog.
- The stale entry that named `uifigure` as unsupported is gone.

### The document format and `copyobj`

- A component is written as its kind and a bag of its model's properties by name, and a grid as
  its tracks, padding, spacing and children, each child with its cell. An axes keeps its cell and
  whether it is a `uiaxes`. There are some eighty properties across the sixteen kinds; naming
  each in a DTO would be the same list twice. A name a later build does not know, or a value
  that does not read, leaves the component's default. `copyobj` goes the same road.

## Consequences

- ex3 runs: headless, and in a window where its slider, drop-down, spinner, check box, button
  and alert were each worked.
- The callable count rises by seventeen to 1,205 of 2,024 (`focus` is newer than the R2021b list
  the count is taken against).
- `set` no longer converts a string argument to a char row before a property sees it. Every
  property that existed before U5 still does that conversion itself; only the U5 names listed
  above keep the difference.
- A script that caught the old dead-handle message by its text no longer matches it. It had no
  identifier to catch by.

## Measured

- **Headless probes** (`ui-probes/u5/`): `u5_matrix` (every component's defaults, classes and
  refusals), `u5_grid` (tracks, fit sizes, placement, nesting, the settling time), `u5_forms`
  (the making calls), `u5_dialogs` and `u5_dialogs2` (the three dialogs, `focus`, `uiaxes`).
  Findings are in the probes' README.
- **Parity fixtures**, recorded with `-noFigureWindows`, every line agreeing with R2025b:
  `u5_props` (5,325 lines), `u5_defaults` (581), `u5_grid` (562, four of them fit widths within
  the fixture's tolerance of a pixel), `u5_forms` (680), `u5_values` (675) and `u5_dialogs`
  (196). A line that could not agree was taken out and is listed under Divergences.
- **Unit tests**: `UiComponentsU5Tests` works each kind through `NotifyComponent` (value and
  previous value, a drag's coalescing, a refused number, a group's selection, a value surviving
  Stop), answers `uiconfirm` through `ScriptGraphicsCallbacks.OverlayShown`, dismisses an alert,
  cancels and closes a progress dialog, and checks the grid's arithmetic, its auto-placement and
  its scrolling in the layout. `UiComponentSerializationTests` round-trips components, a grid
  with a nested panel and a `uiaxes`, and a group of radio buttons.
- **The JGraph window checks**, in the Release app under `-batch -showfigures`, by UI Automation
  patterns only (Value, RangeValue, Invoke, Toggle, SelectionItem, ExpandCollapse, SetFocus,
  Window.Close); each window was captured alone with PrintWindow and `workspace.json` was
  unchanged afterwards.
  - ex3: the slider, the drop-down, the spinner's arrow, the check box and the button each
    reached their callbacks; the alert appeared and left on OK; the axes kept its title, labels
    and grid through every redraw; closing the window ended the process.
  - A figure holding every kind in one grid: each of a text field, a numeric field (and a number
    outside its limits, which was refused and shown back), a spinner, a state button, a
    drop-down, a check box, a hyperlink, a slider, a list, a radio button, a toggle button, an
    alert, a confirmation (three options, the second pressed) and a progress dialog (seen at a
    third, then gone) gave its callback the right values.
  - **The `fit` check**: in that figure each label's drawn text lay inside the `'fit'` cell the
    grid gave it, and each component's size in the window was the size the script read, to a
    pixel.
  - A scrollable grid of twelve rows in a figure too short for them: the bar's range was the
    overflow (390), moving it 120 moved the rows 120, and the last row was in view at the end.
- The checks found and fixed four faults: a `uiaxes` lost its title on a second plot; a slider's
  ticks were stale after its grid stretched it; a toggle button heard a click and not a Toggle;
  and a label, a link and a picture could not be found through UI Automation (a link now also
  offers Invoke).

## Not measured

- **R2025b's event data was not recorded in a window.** A component's callback cannot be made to
  run headless there. The class names and properties are from R2025b's metaclasses and
  MathWorks' documentation; the dialogs' are from its source.
- No keystroke or mouse click was synthesized into JGraph: typing into a field, dragging a
  slider's thumb or a scroll bar, the wheel, and a click on a picture are covered by the unit
  tests' seam or not at all.
- The look of each component was not compared with R2025b's pixel for pixel.
- A range slider's thumbs were not moved in the window.

## Divergences

- **The layout is synchronous.** R2025b lays a `uifigure` out some tenths of a second after
  `drawnow`; a `Position` or a slider's ticks read before then are the old ones. Here they are
  right as soon as they are asked for.
- **A slider's automatic ticks are computed here.** R2025b's come from its view. This build's
  rule gives R2025b's ticks in 31 of the 34 ranges recorded; `[0.01 0.99]`, `[1 12]` and
  `[0 1e6]` differ. `MinorTicks` left automatic reads the computed ticks, where R2025b's reads
  empty until its view has settled.
- **Fit sizes are R2025b's rules round this build's own text measure.** Widths that come from
  text differ from R2025b's browser metrics by fractions of a pixel.
- **`uiconfirm` always waits.** R2025b's waits only when its answer is asked for; a builtin here
  cannot tell. A script that calls it for its `CloseFcn` alone is held until it is answered.
- **`uiconfirm` runs without a display when a test answers it**, which R2025b never does.
- **A component's handle is a number.** `class(b)` is `double`, as for every graphics object
  here, where R2025b's is `matlab.ui.control.Button`.
- **A property a component lacks is refused in the words `get` uses** when it is read with a
  dot, where R2025b uses another identifier for dot access.
- **Values are never categorical.** R2025b takes a categorical for some `Items` and `Value`.
- **A group's `SelectedObject` is right after a button is moved out.** R2025b's goes on naming
  the button that left.
- **A `uiaxes` in a grid keeps its cell when a `Position` is written to it**, silently. R2025b
  reads the written rectangle back until its layout runs again.
- **`Children` is a row**, as everywhere here; R2025b's is a column.
- **A scrollable grid lays out again in what its bars leave**, and its bars are this build's.
  R2025b's scrolling was not measured.

## Still open

- `scroll` and `ScrollableViewportLocation`, a scrollable panel and a scrollable figure, and the
  wheel over a grid's bare background (open item 47).
- A component with no parent, and `GridLayoutOptions` as a constructor (open item 48).
- A slider's automatic ticks in the three ranges above (open item 49).
- `uiconfirm` called for no output (open item 50).
- The event data of the U5 components, recorded in a MATLAB window session (open item 51).
- `true(3, 3, 3)` (open item 42) kept one fixture line from being written the short way.
