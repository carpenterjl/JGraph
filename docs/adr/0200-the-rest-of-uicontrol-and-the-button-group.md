# ADR 0200 — The rest of `uicontrol`, and the button group

## Status

Accepted, 2026-10-04. Stage U3 of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored). It builds on ADR 0198 (U1: `uicontrol`,
callbacks, frames and the component layer) and ADR 0199 (U2: containers, units, `uifigure`).

It retires two entries of ADR 0198: `Extent` is no longer an estimate from the font size, and the
seven styles U1 drew as labels are now drawn as what they are.

## Context

After U2 a script could build any `uicontrol` and read back what it set, but the window drew only
`text`, `edit` and `pushbutton`; `uibuttongroup`, `textwrap` and `listfonts` did not exist; and
`Extent` was a guess. R2025b's behaviour was measured first, headless
(`tools/matlab-checklist/ui-probes/u3`), and then, for what only a person's click shows, in one
window session with the user's leave (`u3w_clicks`).

## Decision

### Every style has a control

`UiComponentLayer` realises all ten styles. The frame a script's thread hands the window
(`UiControlFrame`) now carries `Value`, `Min`, `Max`, `SliderStep`, `ListboxTop`, the `CData`
picture, the control's `Tag`, whether a button group watches it, and how many times a script has
asked for the keyboard.

| Style | Control | What the user's action sends |
|---|---|---|
| `pushbutton` | button, with its `CData` picture under its text | nothing; the `Callback` runs |
| `togglebutton` | toggle button, the same face | 1 when it goes down, 0 when it comes up |
| `checkbox`, `radiobutton` | check box, radio button, on a face carrying `BackgroundColor` | 1 and 0 |
| `edit` | text box; with `Max - Min > 1` one of several lines | its text, or its lines |
| `text` | label | — |
| `slider` | scroll bar along the control's longer side | the value, when a move ends |
| `frame` | bordered box | — |
| `listbox` | list; with `Max - Min > 1` of several selections | the selected rows, 1-based |
| `popupmenu` | drop-down list | the row chosen |

- **A click writes 1 and 0, whatever `Min` and `Max` are.** R2025b's check box with `Min` 2 and
  `Max` 7 reads 1 after a click and 0 after the next (`u3w_clicks`). A control shows as on when its
  `Value` is its `Max` or 1.
- **A radio button outside a button group goes off when pressed again**, as R2025b's does.
- **A slider's `Callback` is for a move that has ended**: an arrow, a page, the thumb let go. The
  arrow moves by `SliderStep(1)` of the range and the trough by `SliderStep(2)` (R2025b: 0.01 and
  0.1 of a 0-to-1 slider). An upright slider has its `Max` at the top.
- **A list's `Callback` runs for every press, the selected row again included**, which is what a
  script's double-click test (`SelectionType` `'open'`) relies on. The gesture of a press on an
  enabled control is recorded as the figure's `SelectionType` without any window callback.
- **A multi-line field commits when it loses the keyboard**; Enter is a new line. Its lines come
  back in the shape its `String` had: a cell stays a cell, anything else is a character matrix.
- **The controls answer a change of state, not a click.** A check box hears `Checked` and
  `Unchecked`, a slider `ValueChanged`. So the mouse, the keyboard and an accessibility client all
  count as the user. Each control's automation id is its `Tag`.
- **The system's own look.** A check box, a radio button, a scroll bar, a list and a drop-down list
  wear the Windows templates: a keyed style with no `BasedOn` stands on the theme's style, and an
  empty style of each inner part's type in the layer's resources keeps the IDE's implicit styles
  out.
- What a user did stays on screen until a frame arrives that has taken it, for every style (U1 did
  this for the edit field).

### A control that is not `'on'`

R2025b, measured (`u3w_clicks`):

- A press on a control whose `Enable` is `'inactive'` or `'off'`, and a right press on any control,
  runs the figure's `WindowButtonDownFcn` and then the control's `ButtonDownFcn`. Its `Callback`
  does not run. The `ButtonDownFcn` is handed a `matlab.ui.eventdata.MouseData` (`ButtonDown`).
- A left press on an enabled control runs neither. An enabled `text` or `frame` answers nothing.

The layer finds the control under the press from the layout, since a disabled WPF element is not
hit, and reports both. An `'inactive'` control keeps its look and hears nothing more of the press.
`'off'` dims the control.

### `uibuttongroup`

`UiButtonGroupModel` is a `UiPanelModel` with a selected button. Everything a panel has it has;
its type is `uibuttongroup`, its class word `ButtonGroup`, and in a classic figure its line is
white. Its rules are R2025b's (`u3_bgroup`, `u3_more`):

- **Which buttons it watches is decided when a button joins**, by being made in the group or moved
  into it: a radio button or a toggle button whose `Value` is at its `Min` or its `Max`. A button
  that became a radio button afterwards, one in a panel inside the group, and one made with a
  `Min` or `Max` its `Value` is not at, are not watched.
- **The first watched button to join is selected**, and so is one that joins with its `Value` at
  its `Max`. A later one that joins at its `Min` is not, even when nothing is selected.
- **Writing a `Value` that starts with 1 to a watched button selects it**; writing 0 to the selected
  one leaves nothing selected. Any other value is kept and changes no selection.
- **Selecting writes 1 into the button and 0 into the one that was selected**, whatever their `Min`
  and `Max`. `SelectedObject` takes a radio button or a toggle button of the group, watched or not,
  the first of several handles, or `[]`.
- A button that leaves the group while selected is deselected and reads 0. A watched button that
  has become a check box refuses a `Value` of 1 after the value is in.
- **A script's writes run no callback.** A user's press on another button runs
  `SelectionChangedFcn` (a `SelectionChangedData` with `OldValue` and `NewValue`) and then the
  button's own `Callback`. A press on the selected radio button runs nothing. A press that lets the
  selected toggle button up writes its 0 and runs its `Callback`; the group hears nothing and still
  counts it selected (`u3w_clicks`).
- `SelectionChangeFcn` is the same slot under its older name. `Buttons` answers empty, as R2025b's
  does.

The group is a kind of its own in the `.graph` document, with which child is selected and which
buttons it watches; `copyobj` of a group selects the copy's own button.

### The property surface

- **`Value`** is a row: a column is turned, an empty is 0-by-0, a matrix is refused
  (`UIControlValueDimensions_M`), and a cell, a string and a complex number each have R2025b's own
  refusal. A control that becomes a list while its `Value` is untouched takes 1 and keeps it.
- **`String`**: a newline starts a new line (a row becomes a character matrix, a cell element
  several elements). A `listbox` or a `popupmenu` reads `'a|b|c'` as three items, whenever it
  became a list.
- **`SliderStep`** has R2025b's three refusals and its warning when the second step is the smaller.
- **`CData`** is validated as R2025b validates it, kept as given so it reads back in its class, and
  handed to the window as pixels; a NaN pixel shows the face beneath. It is in the document.
- **Hidden names.** A property can be left out of what `get(h)` and `set(h)` list and still answer
  (`GraphicsProperty.Listed`). `TooltipString`, `TooltipStr` (one line of text only),
  `UIContextMenu`, `Selected`, `SelectionHighlight` and `HitTest` are such names on a `uicontrol`,
  and `SelectionChangeFcn` on a group. `get(c)` lists R2025b's forty names.
- **`ContextMenu`** has R2025b's refusals, and reads empty once its menu is deleted.
- **`set(h)` and `set(h, name)`** answer for a component as R2025b's do: a struct of the writable
  names, each with the words it takes, and one name's words (`u3_set`).
- **`uicontrol(h)`** gives an existing control the keyboard and answers it.

### `Extent`, `textwrap`, `listfonts`

- **`Extent` has R2025b's rules and this build's own measure of the text.** R2025b's numbers are
  whole points: the widest line plus 4 points, the lines' height plus 6, each rounded
  (`u3_metrics`). A `text` control, and any style whose `Max - Min` exceeds one, measures every
  line; the rest measure the first. No lines at all is 4 by 6 points, and an empty line measures as
  a space. All of that is reproduced. The width of the text itself is not R2025b's (see
  "Divergences"): it is measured in the font the window draws.
- **`UiFonts`** (Core) is the one font table and the one measurement: `Family` maps a `FontName` to
  the family drawn (MS Sans Serif to Microsoft Sans Serif, Helvetica to Arial, a name the machine
  has no outline font for to Arial, which is what R2025b measures such a name as), `Measure` asks
  GDI for the text's width at a 2048-pixel em and scales it, which gives the font's design
  advances with no window, and `Families` lists the machine's fonts. The component layer and
  `Extent` read the same table.
- **`textwrap`** wraps to a count of characters exactly as R2025b does (`u3_wrap`): words are
  gathered with the spaces after them while they fit, the last word counting one space; a word
  longer than the count is cut into pieces, a piece of spaces alone dropped; the paragraph's last
  line loses its trailing spaces; a paragraph with no words is one space. To a control's width it
  gathers words while the line's `Extent` is no wider than the control. Its second output is where
  the control would sit, in the control's units.
- **`listfonts`** is the machine's families, sorted without regard to case; `listfonts(h)` adds the
  font `h` names.

### Two general fixes this stage needed

- **A one-element array of handles is that handle to the dot.** `h = findobj(...); h.String = 'x'`
  failed, because `findobj` answers an array. A 1-by-1 is a scalar in MATLAB.
- **`cellstr` of an empty character array is `{''}`**, whatever its shape, as MATLAB's is.

## Measured

- **Headless probes** (`ui-probes/u3/`): `u3_styles`, `u3_extent`, `u3_metrics`, `u3_bgroup`,
  `u3_more`, `u3_wrap`, `u3_set`. Findings are in the probes' README.
- **Parity fixtures**, recorded with `-noFigureWindows`: `u3_styles` (582 lines), `u3_bgroup` (213)
  and `u3_text` (133) pass in both value representations. `u3_text` pins `Extent` by its rules and
  never by a width.
- **Unit tests**: `UiControlU3Tests` (each style's user value written before its callback, a
  multi-line field's shapes, the group's press, callback order and Stop, a press on an inactive
  control, what the frame carries, `Extent`'s formula against the font's own measure, the document
  and `copyobj`).
- **The MATLAB window session, with the user's leave** (`u3w_clicks`): R2025b opened one figure and
  `java.awt.Robot` clicked in it 26 times, after checking before each click that the figure was the
  window in front. It gave what a click writes, the button group's callback order and event data,
  and the `ButtonDownFcn` rules above. It corrected three things the documentation had suggested:
  a click writes 1 and 0 rather than `Max` and `Min`; the selected toggle button of a group can be
  let up; a press on an inactive control runs `WindowButtonDownFcn` first.
- **The JGraph window check, with the user's leave**: the Release app ran a figure of all ten
  styles and a button group under `-batch -showfigures`, driven through UI Automation patterns
  only (Invoke, Toggle, SelectionItem, RangeValue, Value, ExpandCollapse, SetFocus). Every control
  sat where the layout puts it; each pattern ran the control's `Callback` with the right `Value`;
  the group's `SelectionChangedFcn` ran before the button's; a script's writes moved every control
  in the window; a label sized to its `Extent` and a label sized by `textwrap` held their text.
  The process was stopped, not closed, and `workspace.json` was unchanged. The check found two
  faults, both fixed: a list showed a character matrix's padding, and a disabled button did not
  look disabled.

## Not measured

- No keystroke or mouse click was synthesized into JGraph. So a press on an inactive control, a
  right press, the lone radio button going off, a slider's arrows and a double click in a list are
  covered by the code and the unit tests, not by input in a window.
- R2025b's `popupmenu` was not driven: the probe's second click missed the open list. That its
  `Value` is the row chosen is MathWorks' documentation.
- R2025b's `WindowButtonUpFcn` after a press on an inactive control was not recorded; JGraph runs
  it, as the pair of the press it reported.
- What R2025b shows for a button whose `Value` is neither its `Max` nor 1 was not looked at.
- The look of each control was not compared with R2025b's pixel for pixel.

## Divergences

- **`Extent`'s numbers are this build's own measurement of the text.** R2025b measures through GDI
  at the display's scaling, in a font of its own choosing: at 125 % an 8-point MS Sans Serif `'a'`
  is 4.2 points and a line 11.25, widths scale with the size asked for while line heights step
  between bitmap strikes, and `'Arial'` measures as the default font (`u3_metrics`). JGraph
  measures the design advances of the font its window draws, so that text sized by `Extent` or
  `textwrap` fits what is drawn. The margins, the rounding to whole points and which lines count
  are R2025b's.
- **`textwrap` to a control's width breaks lines by this build's `Extent`**, so its lines can
  differ from R2025b's where the fonts' widths do. The wrap to a count of characters is R2025b's
  exactly.
- **`SliderStep` of a `single` or an integer class is the numbers given.** R2025b reads the bytes
  as doubles: `single([0.1 0.3])` reads back `[3.8e-07 0]` and `int8([0 1])` `[1.3e-321 0]`. This
  is MATLAB's fault, and JGraph does not copy it (`docs/MATLAB_Behavioral_Fixes.md`).
- **`set(h, 'Value', "1")` is refused in the words for text**, not R2025b's words for a string:
  `set` hands a string scalar on as text. Through the dot the refusal is R2025b's.
- **`SelectedObject` set to a deleted handle is refused as "not a handle"**, where R2025b has a
  refusal of its own (`InvalidSelectedObjectComponent`); a deleted handle is a number that names
  nothing (ADR 0198).
- **A `popupmenu`'s `BackgroundColor` is not drawn.** The system's drop-down list paints its own
  face.
- **`listfonts` lists what Windows enumerates.** R2025b's list also has Java's logical names
  (`Monospaced`, `SansSerif`, `Serif`) and the registry's substitutes.
- **`set(h)` of anything but a component is still a cell of names**, where R2025b answers a struct
  (open item 41).

## Still open

- `true(2, 2, 3)` and `false` with more than two sizes are refused (open item 42); the fixture
  uses `logical(ones(2, 2, 3))`.
- `reset(h)` on a component, and R2025b's `TooltipStr`/`Tooltips` beyond the two older spellings.
- A classic figure's `Color` is still painted over by the theme (open item 40), and the three
  audio tests still fail (open item 39).
