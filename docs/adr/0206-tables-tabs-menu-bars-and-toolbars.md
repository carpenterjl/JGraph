# ADR 0206 — Tables, tabs, a figure's menu bar and its toolbars

## Status

Accepted, 2026-10-05. Stage U8 of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored). It builds on ADR 0198 (U1: callbacks,
frames and the component layer), ADR 0199 (U2: containers, units), ADR 0202 (U5: the `uifigure`
components, `uigridlayout`) and ADR 0204 (U7: graphics classes).

## Context

After U7b a script could build an interface of controls, panels and grids, and an App Designer
app of those could be run and edited. Four things every larger interface is made of were still
missing: a table of data, pages behind tabs, menus along the top of the window, and a toolbar.
`uimenu` existed only inside a `uicontextmenu`; given a figure it refused, saying a figure's menu
bar was not supported. `uitable`, `uitabgroup`, `uitab`, `uitoolbar`, `uipushtool` and
`uitoggletool` did not exist.

R2025b's behaviour was measured first, headless (`tools/matlab-checklist/ui-probes/u8`:
`u8_matrix`, `u8_forms`, `u8_behave`, `u8_more`, `u8_parents`). Two things it showed shaped the
stage:

- **Each of these is one class in R2025b, in a classic figure and in a `uifigure` alike.** A
  table, a tab group, a menu and a tool answer to the same names, take the same words and refuse
  in the same sentences in both; only a table's font and a tab group's place start differently.
- **They refuse as the newer components do, not as a `uicontrol` does** — the on/off, word,
  colour and text coercions of U5 — while a table keeps a classic control's `Units` and
  `FontUnits`.

## Decision

### `uitable`

- One model, `UiTableModel`, a component with `Units`. Its `Data` stays on the script's side, as
  it was given, because it reads back as it was given: a numeric array of any class, a logical
  array, a string array, a cell of scalars and text, or a table. Text, a time, a struct, anything
  of three dimensions, and a cell holding a nested cell, a string or a vector are refused with
  R2025b's identifiers (`MATLAB:hg:uitable:BadDataType`, `BadDataDimension`,
  `BadDataCellArrayType`, `BadDataCellArraySize`).
- `ColumnName` and `RowName` read `'numbered'` until written, and a table's variable names and
  row names in their place while the data is a table. Written, they keep the shape they were
  given in: a character row, a character matrix, a column cell; numbers become the text
  `num2str` writes.
- `ColumnWidth` (`'auto'`, `'fit'`, `'Nx'`, or a row cell of those and pixel widths),
  `ColumnEditable` and `ColumnSortable` (a logical or a logical row), `ColumnFormat` (a row cell
  of format words and lists of choices), `BackgroundColor` (the row stripes, an n-by-3 matrix or
  colour names), `Selection` against `SelectionType` and `Multiselect` and the size of the data:
  each takes and refuses what R2025b's does. `Selection` reads back as written.
- **The model holds a picture, not the data**: `UiTableContent`, the cells as text or as check
  boxes, the columns with their headings, widths and choices, and the row headings. It is worked
  out again on the script thread whenever the data or a column property is written, so the
  window never reads a value a script is writing.
- **A number in a cell** is written by MATLAB's `format` of the column's word, `short` when it
  has none: a whole number in full, four decimals between a thousandth and a thousand, and four
  decimals with an exponent outside that.
- **An edit a person makes** goes back as the text typed, the box ticked or the choice picked,
  and is written into the data on the script thread before anything decides whether a callback
  runs. A cell that held a number takes the number the text reads as, and NaN when it reads as
  none; a logical cell takes the tick; anything else takes the text. A numeric array keeps its
  class, a string array stays one, and a table's variable is rebuilt with the cell changed.
  `CellEditCallback` is told by a `CellEditData`: `Indices`, `DisplayIndices`, `PreviousData`,
  `EditData`, `NewData`, `Error`.
- **A selection a person makes** is written the same way; `SelectionChangedFcn` is told by a
  `TableSelectionChangedData` and then `CellSelectionCallback` by a `CellSelectionChangeData`.
- In the window a table is a WPF `DataGrid` built from the frame: text, check-box and
  drop-down columns, stripes, row and column headings, and a selection of cells, rows or columns.

### `uitabgroup` and `uitab`

- A tab group is a container that holds tabs and nothing else; a tab is a container that fills
  what its group leaves. **The sizes are R2025b's once it has settled**: a strip of 24 pixels
  along the top or the bottom and a pixel of border on the other three sides; along a side, a
  strip as wide as its widest heading and 24 pixels, never under 66 or over 101. A tab's
  `Position` is read, in its own `Units`, and cannot be written.
- The first tab made shows. A tab that is deleted or moved away while it shows hands the page to
  the tab after it, or to the one before when it was the last. `SelectedTab` takes a tab of the
  group; anything else is refused in R2025b's two ways. A script's choice of tab runs no
  callback; a person's runs `SelectionChangedFcn` with a `SelectionChangedData`.
- A tab group's `Children` are its tabs in the order their headings stand, first first — the one
  list that is not newest first — and `uistack` and a written `Children` move them in that order.
- In the layout only the tab that shows is laid out as showing, so what is in the others is
  neither drawn nor clickable, an axes included. The group's box and the page are drawn with the
  panels; the headings are a WPF `TabControl` laid along the strip.

### A figure's menu bar

- `uimenu` made in a figure is an entry of that figure's menu bar (`FigureModel.Menus`); made in a
  menu or a context menu it is an entry of that. With no parent named it goes on the current
  figure's bar. The window shows the bar above everything else, in a classic figure and a
  `uifigure` alike, and takes its room from the window, not from the figure.
- A menu answers to R2025b's 24 names. `Label` and `Callback` are still answered and no longer
  listed. `Position` is the entry's place among its siblings: writing a place moves it there, and
  writing any other number — a fraction, zero, one past the end — moves it to where the number
  falls and reads back as written, the others counting round it, which is what R2025b does.
- A menu with entries of its own runs its `MenuSelectedFcn` as it opens. `Accelerator` is Ctrl
  and the letter. `&` marks an access key.

### Toolbars

- `uitoolbar` belongs to a figure; `uipushtool` and `uitoggletool` belong to a toolbar and, with
  none named, to the current figure's, which is made when it has none.
- A tool's picture is its `Icon` when it has one and its `CData` otherwise; both read back as
  written. A file that is not there is kept and warned of, as in R2025b.
- A push tool runs its `ClickedCallback`. A toggle tool goes down or comes up, runs its
  `OnCallback` or `OffCallback`, and then its `ClickedCallback`. A `State` a script writes runs
  the callback of that state when the queue is next drained, and never the `ClickedCallback` —
  measured.

### What the stage changed elsewhere

- **A figure lists its children as R2025b does**: menus, context menus, toolbars and components
  as one list, newest first, with the axes after them. `GraphObject` gained a creation order to
  merge the four lists by.
- **The makers say what is wrong with a call before what is wrong with its parent**:
  `uitab(fig, 'Bogus', 1)` is refused for the name, not for the figure.
- `uicontextmenu` and `uimenu` take R2025b's forms and refuse in its words; the older makers
  refuse a tab group, a menu and a toolbar as a parent, each in its own.
- `set(h)` and `set(h, name)` answer the words of a menu, a toolbar and a tool.
- Three or four logicals are a colour to the properties that take an opacity.

### The document format and `copyobj`

- A table is written as its picture, a tab group as its tabs and the one that shows, a figure's
  menu bar and toolbars as lists of their own. `copyobj` copies a tab, a menu, a toolbar and a
  tool, and gives a copied table the data of the table it was copied from.
- A table read from a document has no script behind it: its `Data` is then what it shows —
  numbers to the digits shown, text, and ticks.

## Found on the way

- A string scalar given to `set(h, name, value)` on a menu, a toolbar or a tool was turned into
  text before the property saw it; these now tell `""` from `''` where R2025b does.
- R2025b's recordings of a tab's rectangle are racy: the first layout of a `uifigure` takes it
  more than half a second, and a group goes on answering its old inner rectangle after a heading
  widens its strip. The fixture waits, and asks only for what settles.

## Consequences

- The acceptance script — a figure with a menu bar, a toolbar, and a tab group holding an axes
  and a table — runs headless and in a window.
- The callable count rises by six to 1,212 of 2,024, and the builtins by three to 437 of 514.
- A script that caught "a figure's menu bar is not supported" no longer has anything to catch.
- A figure's `Children` now lists its menus and toolbars among its components.

## Measured

- **Headless probes** (`ui-probes/u8/`): `u8_matrix` (every property of the nine kinds, in both
  figure kinds, against one battery of values), `u8_forms` and `u8_parents` (the makers and what
  each takes as a parent), `u8_behave` and `u8_more` (data, names, selection, tabs, menus,
  tools, the order of children, the event classes).
- **Parity fixtures**, recorded with `-noFigureWindows`, every line agreeing with R2025b:
  `u8_defaults` (636 lines), `u8_props` (7,760), `u8_forms` (677), `u8_table` (404), `u8_tabs`
  (147) and `u8_bars` (186). A line that could not agree was taken out and is listed under
  Divergences.
- **Unit tests**: `UiTablesTabsU8Tests` works a table's cells, its selection, a tab group, a
  menu and both tools through the seams the window uses, checks the layout of a tab group and
  the frame's bars, and copies each kind. `UiComponentSerializationTests` round-trips a table in
  a tab, a menu bar and a toolbar.
- **The JGraph window check**, in the Release app under `-batch -showfigures`, by UI Automation
  patterns only (Invoke, ExpandCollapse, Toggle, SelectionItem, Grid, Value, Window.Close); the
  window was captured alone with PrintWindow and `workspace.json` was unchanged afterwards.
  A classic figure with a menu bar, a toolbar and a tab group holding an axes and a table: a menu
  entry, a checked entry and a top-level menu each ran their callback; the push tool ran its
  own and the toggle tool its on, its shared and its off callback; the second tab was picked and
  the group told of both; a text cell, a number cell (a number, and text that is none, which
  became NaN), and a check-box cell were edited and a cell selected, each reaching its callback
  with the data already changed; the window then showed the number as the script had stored it.
- The check found one fault, now fixed: the grid kept an edited cell's value back until the
  person left its row, so a callback heard of an edit one row late.

## Not measured

- **None of the callbacks was run in R2025b.** A table's, a tab group's, a menu's and a tool's
  callbacks need a window there. The event classes and their properties are R2025b's own
  (`meta.class`); what a `CellEditData` holds after an edit, what a number typed over a number
  becomes when it is no number, and the order of a toggle tool's callbacks follow MathWorks'
  documentation.
- **How R2025b writes a number in a cell** was not compared: it draws a table only in a window.
- **A classic figure's tab** was not measured in a window; its sizes here are a `uifigure`'s.
- No keystroke or mouse click was synthesized into JGraph: typing into a cell and dragging a
  column are covered by the unit tests' seam or not at all.
- The look of a table, a tab strip, a menu and a tool was not compared with R2025b's.

## Divergences

- **A tab is the size it will be as soon as it is made.** R2025b measures a tab some tenths of a
  second after it is made, and in a classic figure only once a window shows it; until then it
  answers the group's whole rectangle for the tab's.
- **A classic figure's own menus and toolbar are not objects.** R2025b's six menus and its
  toolbar are `uimenu`s and a `uitoolbar` with hidden handles, so `findall` finds them and a
  script's first menu has `Position` 7. Here the window's own furniture is not in the tree, and
  a script's first menu has `Position` 1.
- **Sorting a column sorts what is shown and nothing else.** `DisplayData` is always `Data`,
  and `DisplayDataChangedFcn` never runs.
- **`DisplaySelection` is `Selection`.** R2025b answers an empty for it until its view has
  drawn.
- **`StyleConfigurations` is an empty table of three text variables.** R2025b's holds a
  categorical, a cell and a style object.
- **A table cannot be the parent of a table.** R2025b's `uitable(t)` makes one.
- **An object of U8 cannot be made without a parent.** `uimenu('Parent', [])` is refused;
  R2025b makes one that belongs to nothing.
- **`SelectedTab` of a group with no tabs is an empty double.** R2025b's is an empty
  `GraphicsPlaceholder`; a handle is a number here.
- **A table's `Extent` is its own size and 40 pixels.** That is what R2025b answered for every
  table asked; its rule was not found.
- **A context menu's unlisted `Position` reads `[0 0]`.** What R2025b's reads was not recorded.

## Still open

- The event data and the look of the U8 objects, recorded in a MATLAB window session (open item
  59).
- A table's view: `DisplayData` under a sort, `DisplayDataChangedFcn`, rearranged columns,
  `scroll`, key and button-down callbacks, `uistyle` (open item 60; `uistyle` is U9's).
- `mat2str([])` answers `[]`, where R2025b's answers `zeros(0,0)` (open item 61).
- A classic figure's own menus and toolbar as objects (open item 62).
