# ADR 0207 — Knobs, switches, gauges, the lamp, the pickers, trees and styles

## Status

Accepted, 2026-10-05. Stage U9 of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored). It builds on ADR 0198 (U1: callbacks,
frames and the component layer), ADR 0202 (U5: the first `uifigure` components and
`uigridlayout`), ADR 0204 (U7: graphics classes) and ADR 0206 (U8: tables, tabs, menus and
toolbars; `StyleConfigurations` was left empty there for this stage).

## Context

After U8 a script could build an interface of fields, buttons, tables and tabs. The components a
dashboard is made of were still missing: `uiknob` (continuous and discrete), `uiswitch` (slider,
rocker, toggle), `uigauge` (circular, linear, ninety-degree, semicircular), `uilamp`,
`uidatepicker`, `uicolorpicker`, `uitree` and `uitreenode` (plain and with check boxes), and with
them `uistyle`/`addStyle`/`removeStyle`, a component's own `ContextMenu`, `focus` and `scroll`,
`expand`, `collapse` and `move`.

R2025b's behaviour was measured first, headless (`tools/matlab-checklist/ui-probes/u9`:
`u9_makers`, `u9_matrix` — 47,015 lines, every property of the sixteen kinds in both figure kinds
against one battery of values — `u9_forms`, `u9_behave`, `u9_grid`, and `u9_extra` after the
first recording). Three things it showed shaped the stage:

- **Each kind is one class in R2025b, in a classic figure and in a `uifigure` alike**, refusing
  with the U5 coercions under `MATLAB:ui:<Class>:<id>`; each gauge shape and each tree kind is a
  class of its own (`Gauge`, `LinearGauge`, `NinetyDegreeGauge`, `SemicircularGauge`; `Tree`,
  `CheckBoxTree`), so each has a property table of its own here.
- **R2025b writes a component's property through `isequal` first.** A value equal to the one
  held is not written at all: `false` lands on a knob at 0 where `true` is refused, `[]` lands on
  a picker with no disabled dates where it is refused once there are some, and `{}` lands on an
  empty `DisabledDates` because `datetime({})` is the empty datetime. The comparison converts a
  datetime's partner, which is where the `MATLAB:datetime:AutoConvertStrings` warning comes from
  when a datetime is written to a word or text to a date. The property write here does the same,
  kind for kind (a number against a number or logical, text against text), and warns in the same
  places.
- **A knob, a discrete knob, a lamp, a round gauge and a switch keep their proportions.** A
  rectangle written to them is shrunk to the largest of their shape inside it (probe `u9_extra`:
  `[10 10 80 40]` on a knob is `[10 10 40 40]`, on a semicircular gauge `[10 10 73.85 40]`, on a
  switch `[10 10 80 35.56]`); in a grid's cell they take the largest of their shape whose labels
  still fit.

## Decision

### Knobs, switches, gauges and the lamp

- `UiScaleModel` (the slider's limits, value and ticks, lifted out of `UiSliderModel`) carries
  `UiKnobModel` and `UiGaugeModel`; `UiDiscreteKnobModel` and `UiSwitchModel` are items models
  like a drop-down. A knob's `Value` is a double within its `Limits`, which clamp it; a gauge's is
  any finite number and is not clamped. A gauge's `ScaleColors` are rows or names and its
  `ScaleColorLimits` increasing pairs, equal bands over the limits until written; the round
  gauges have a `ScaleDirection`, the others an `Orientation` with R2025b's words per shape, and a
  linear gauge swaps its rectangle when it stands up, as a switch does.
- A switch has exactly two `Items` and at most two `ItemsData`; a discrete knob at least two of
  each. Items rewritten under a value keep the value when the new items still have it and take
  the first otherwise; data first given keeps the index, data replaced looks the datum up (probe
  `u9_extra`; a drop-down behaves the same, and now does).
- The automatic ticks are R2025b's unsettled defaults — five intervals for a knob and two for a
  ninety-degree gauge, a round-step rule for the rest — and `MajorTicks`, `MinorTicks` and the
  labels take a row vector, refusing a matrix or a datetime as R2025b does.
- The window draws each as a `UiDrawnFace` (`UiScaleFaces.cs`): a dial with a ring of ticks, a
  discrete dial with a position per item, a gauge of each shape with its bands and needle, a
  switch of each style, a lamp. Each answers UI Automation — `RangeValue` for a knob, `Value` for
  a discrete knob, `Toggle` for a switch, a read-only `RangeValue` for a gauge — which is how the
  window check drove them.

### The date picker and the colour picker

- `UiDatePickerModel` keeps a date as whole days from 1899-12-30 (the script side's epoch), so
  that year 0 — R2025b's lower limit — has a place; `NaT` is null. `Value` is a datetime scalar
  within `Limits` or `NaT` (the time of day dropped, the year 0 to 9999, a disabled weekday
  refused); `Limits` any two increasing datetimes; `DisabledDates` a vector of datetimes, sorted
  and deduplicated, an empty one keeping its shape; `DisabledDaysOfWeek` numbers 1 to 7 or day
  names, keeping the class written. A limit or a disabled weekday that excludes the value takes
  it away, and the `NaT` left behind reads in datetime's own default format until the value or
  `DisplayFormat` is written again (measured); a disabled date that excludes it leaves one in the
  picker's format. `DisplayFormat` takes the date letters, drops a time of day, and refuses a time
  alone, an empty, or an unknown letter in R2025b's three sentences.
- `UiColorPickerModel` holds a solid colour (three logicals accepted, which no other colour
  property does) and an `Icon` that is a file, an m-by-n-by-3 array, or one of its two words,
  `'default'` and `'text'`, by any unambiguous beginning.
- The window shows a date picker as a field with a calendar drop-down and a colour picker as a
  swatch button with a palette and a hex box; a date typed, a date picked, a swatch or a hex code
  reach the script as `'value'`, text that is no date puts the date back.

### Trees

- `UiTreeNodeModel` is a `GraphObject` with its own `Nodes`; `UiTreeModel` (and the sealed
  `UiCheckBoxTreeModel`) holds the top nodes, `SelectedNodes`, `CheckedNodes`, `Multiselect` and
  `Editable`. A node has R2025b's fifteen properties and no others — no `Visible`, no `Position` —
  and speaks as a component, so `set(node)` is a struct and an unknown name refuses in R2025b's
  words. `uitreenode` takes a tree or a node as its parent and nothing else; without one it makes
  a tree in a new `uifigure`.
- `Children` list nodes first-first; `SelectedNodes` are kept in the order written and lose a
  node that is deleted or moved out; `CheckedNodes` follow R2025b's rule — the nodes written,
  then their descendants, then every parent whose children are all in, in one pass — and a child
  added under a checked parent is checked. `move` puts a node after or before a sibling, `expand`
  and `collapse` open and close a node, a tree or an array of them, with `'all'` for the branch.
- Node events (`JgsUiComponentEvents.Trees.cs`): `SelectionChangedFcn` with
  `SelectedNodesChangedData`, `NodeExpandedFcn`/`NodeCollapsedFcn`, `NodeTextChangedFcn` with
  the text and the one before, `CheckedNodesChangedFcn` with all eight lists, and
  `ClickedFcn`/`DoubleClickedFcn` with a `TreeInteraction` naming the node and its level. The
  window's tree is a WPF `TreeView`: Ctrl-click selects several, F2 or a double click edits a
  node, a check box ticks one.

### Styles

- `uistyle` is a value object of the built-in class `matlab.ui.style.Style` (U7's class table),
  with ten properties and R2025b's refusals; `set` and `get` on one refuse as on any value
  object. `addStyle(h, s, target, index)` on a table (`'table'`, `'row'`, `'column'`, `'cell'`),
  a tree (`'tree'`, `'node'`, `'level'`, `'subtree'`), a list box or a drop-down (`'item'`)
  checks the target word, then the index, with R2025b's identifiers; the style is copied by
  value; an index past the data is kept; a column of indices reads back as a row.
  `removeStyle(h, k)` takes rows away by number, `removeStyle(h)` all of them.
  `StyleConfigurations` is a table of `Target`, `TargetIndex` and `Style`. The rules reach the
  window as `UiStyleRule`s on the component, where a table's rows and cells, a tree's nodes,
  levels and subtrees, and a list's items are painted with them.

### Context menus, focus and scroll

- A component's `ContextMenu` must belong to its own figure (`MATLAB:hg:InvalidObject`); a
  deleted one reads as empty. The window attaches it to the control and tells
  `ContextMenuOpeningFcn` first; `open(cm, x, y)` asks the window to show it at a point.
- `focus` refuses a gauge, a lamp, a tree node, a tab and a menu; `scroll` takes a tree to a node
  or to `'top'`/`'bottom'`, a list box to an item or an end, a table to a row, a column or a cell,
  a text area, a scrollable grid, panel or figure to a place, refusing the rest in R2025b's words
  and warning `MATLAB:uicontainer:ScrollableOff` for a container that does not scroll. The
  request is kept on the model (`ScrollRequests`, `ScrollTarget`) for the window to honour.

### What the stage changed elsewhere

- `cellstr` takes a datetime or a duration; `datetime.empty` and `duration.empty` exist; a
  semicolon-rowed literal of datetimes is a datetime, and a concatenation whose pieces all show a
  default format works its format out again from all the moments, as R2025b's does (`[d1 d2]`
  shows the time of day only `d2` has); a datetime of year 0 or 10000 can be built and printed
  (four-hundred-year blocks, which the Gregorian calendar repeats); text in quotes in a format is
  copied through.
- An on/off word and an enumeration word are read with blanks around them trimmed, as R2025b
  reads `'off '`; a component's `Position` refuses a matrix and a character matrix as R2025b does.
- A `-batch -showfigures` run's `-logfile` stays open while its windows are up: the callbacks
  print to it. (Every callback of the window check died on a closed writer before.)
- The grid layout gives a shaped kind the largest of its shape whose labels still fit the cell,
  through `UiShapedCells`, which the layout and the window share.

## Found on the way

- A script's local functions cannot see the script's variables: the first draft of the
  dashboard example wrote `pressure.Value = x` in one and made a struct called `pressure`. The
  example is a function with nested functions, as it would have to be in R2025b.
- A tick on a tree's check box was told on its click; UI Automation's `Toggle` and the keyboard
  change the state without one. It is told on the state now.
- The guard that drops a string scalar to a character row before a property sees it had to learn
  `NodeData`, `DisplayFormat`, `ScaleColors` and `ScaleColorLimits`, where R2025b tells `""` from
  `''`.

## Consequences

- The dashboard — a tree of units with check boxes, three gauges, a lamp, a knob, a switch, a
  discrete knob, a date picker, a colour picker, a context menu on the knob and the gauge — runs
  headless and in a window (`ui-probes/research/apps/examples/ex6_dashboard.m`).
- The callable count rises by eight to 1,220 of 2,024; the builtin count stays at 437 of 514.
- A drop-down whose data arrive while its second item is picked keeps that item (it took the
  first before).

## Measured

- **Headless probes** (`ui-probes/u9/`): `u9_makers`, `u9_matrix`, `u9_forms`, `u9_behave`,
  `u9_grid`, `u9_extra`; `u7_classes` extended with the sixteen kinds and the graphics class table
  regenerated.
- **Parity fixtures**, recorded with `-noFigureWindows`, every line agreeing with R2025b:
  `u9_defaults` (1,144 lines), `u9_props` (11,970), `u9_forms` (696), `u9_tree` (191),
  `u9_values` (439) and `u9_styles` (226). A line that could not agree was taken out and is
  listed under Divergences.
- **Unit tests**: `UiComponentsU9Tests` turns a knob and drags it, flips a switch, sets a
  discrete knob, types and picks a date, chooses a colour, selects, expands, renames, ticks and
  clicks nodes, checks a style rule, a component's context menu, a scroll request and the
  proportions a switch and a knob keep; `UiComponentSerializationTests` round-trips the new
  kinds and a check-box tree with its nodes.
- **The JGraph window check**, in the Release app under `-batch -showfigures`, by UI Automation
  patterns only (RangeValue, Toggle, Value, SelectionItem, Invoke, SetFocus); the window was
  captured alone with PrintWindow and `workspace.json` was unchanged afterwards. The dashboard:
  the knob set to 90 moved the three gauges and the status; the switch armed the alarm (the lamp
  red, "ALARM pressure 90"); the discrete knob said "Mode Run"; the Turbine node selected drove
  the knob to 91; the Boiler 1 check box said "1 units checked"; the date typed said "Shift
  06/10/2026"; the hex code `#FF0000` left in the palette's box recoloured the trend line. The
  check found three faults, now fixed: the closed log writer, the click-told check box, and the
  shaped kinds stretched to their grid cells.

## Not measured

- **None of the callbacks was run in R2025b.** The event classes and their properties are
  R2025b's own (`meta.class`, probe `u9_behave`); what each event holds follows MathWorks'
  documentation.
- **How R2025b draws a knob, a gauge, a switch, a tree or a palette** was not compared: it draws
  them only in a window. Its automatic ticks and `OuterPosition` once a window has settled them
  were measured only through the fit sizes of `u9_grid`.
- **The identifier of R2025b's aspect-ratio warning**, which its view issues off the script's
  thread, was not captured.
- **A `Value` on a disabled date** (as opposed to a disabled weekday) was not probed.
- No keystroke or mouse click was synthesized into JGraph: the palette's swatches, a drag on a
  knob and a context menu opened by the mouse are covered by the unit tests' seam or not at all.

## Divergences

- **A knob's and a gauge's automatic ticks are R2025b's unsettled defaults.** R2025b's view
  re-ticks a settled knob to ten intervals and a ninety-degree gauge to four; this build keeps
  five (`[0 20 40 60 80 100]`) and two (`[0 50 100]`) and never re-ticks.
- **`OuterPosition` of a knob, a discrete knob and a switch is this build's own.** R2025b's comes
  from its view's text measurement; here it is the dial grown by the widest label and a line of
  text, or the switch grown by its captions; the fit sizes of the two differ by a few pixels.
- **A shaped component shrinks silently to its proportions.** R2025b's view warns later, off
  the script's thread ("Cannot apply the requested height due to aspect ratio constraints");
  this build applies the rule at the write and says nothing.
- **`StyleConfigurations` holds text and a cell of style objects.** R2025b's `Target` is
  categorical and its `Style` an object array; `T.Style(1).FontWeight` there is
  `T.Style{1}.FontWeight` here.
- **Displaying a `uistyle` lists all ten properties.** R2025b lists only the ones that were set.
- **Two styles do not make an array.** `[s s]` is refused; R2025b makes a 1-by-2 `Style` array.
- **A datetime of year 0 or 10000 is printed but not taken apart.** `datetime(0,1,1)` and a
  picker's lower limit print as `01-Jan-0000`; `.Year` and `year` of such a moment fail here.
- **A colour picker's `'text'` icon shows the swatch.** The word is kept and read back; the
  window shows the colour and its code as it does for any picker.

## Still open

- The event data and the look of the U9 objects, recorded in a MATLAB window session (open item
  63).
- A knob's and a gauge's ticks and `OuterPosition` as R2025b's view settles them (open item 64).
- `StyleConfigurations` as a categorical and an object array, and style arrays (open item 65).
- A datetime's parts outside .NET's years, and a default-format flag on datetimes (open item 66).
- `uihtml` (stage U9b).
