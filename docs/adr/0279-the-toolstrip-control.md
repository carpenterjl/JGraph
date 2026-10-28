# ADR 0279 — The toolstrip control, laid out and resized as R2025b's toolstrip framework does it

## Status

Accepted, 2026-10-28. Stage W1 of the App Designer window plan (decision WD2: a reusable toolstrip
control laid out from R2025b's own layout configuration), after stage W0's recordings (3abe7a1, no
ADR).

## Context

The designer window (designer plan DS2–DS8) has a flat WPF `ToolBar`. R2025b's App Designer has a
toolstrip: tabs of titled sections of columns, large and small buttons, split and drop-down
buttons with popups, toggles and button groups, a combo box and a spinner, contextual tabs shown
by the document's view, a quick access bar, sections that drop their labels and then collapse to
drop-downs as the window narrows, the toolstrip minimized to its tabs, and Alt key tips. Stage W0
recorded App Designer's toolstrip as data (`w0_toolstrip.json`, all six tabs, their popups and the
quick access bar, from App Designer's tab modules run unmodified under node) and read the
framework's collapse rules from `MW/toolstrip/widgets/Tab`, `Section` and `Column`. Stage W1 builds
the control that renders such a layout; stage W2 puts it in the window.

## Decision

- **The layout model is UI-free, in `JGraph.Scripting/Toolstrip`** - a revision of the plan's
  section 7.1, which put `ToolstripLayout.cs` beside the control in `JGraph.Controls`. The test
  project is plain `net8.0` and cannot reach a WPF assembly, and stage W2's `DesignToolstrip`
  (in `JGraph.Scripting/Designer`, also UI-free) has to produce the layout the control takes;
  `JGraph.Controls` references `JGraph.Scripting`, not the other way. So the records, the reader,
  the rules, the resize and the key tips' state machine live there and the WPF control takes them:
  - `ToolstripLayout` (tabs with their tab group and whether contextual, sections with a collapse
    priority and the icon they collapse to, columns with a fixed width and an alignment, items of
    ten kinds - push, split, drop-down and toggle buttons, combo box, spinner, text field, label,
    empty control, panel - and popups of items, check items, headers, separators and dynamic
    panels; the quick access bar). `MapItems` replaces controls through panels and the quick
    access bar, as a host fills in limits, icons or words.
  - `ToolstripConfigReader` reads the framework's configuration tree (`type`, `tag`, `children`,
    `PopupList`s) - the shape `_generateTabLayoutConfig` and `_generateStaticPopupsConfig` return -
    and `ReadRecording` reads a whole W0 recording from its resolved trees, adding the RUN section
    to each tab whose common options name it (`hasRunSection`) as the framework shares it. The
    static popups come as `{popups: [...]}`, and the reader takes that form.
  - `ToolstripRules`: a column of one button draws it large (icon over label), a column of
    several draws them small (`Column._updateChildrenOrientations`), a panel's columns likewise
    when the panel is alone in its column; a column may drop its labels when it has no fixed width
    and its controls, empty controls aside, are all small labelled buttons
    (`Section._isColumnEligibleToHideDescendantLabels`; ALIGN's buttons labelled `" "` qualify, as
    in R2025b); a panel's consecutive columns share a row and any other control takes one of its
    own (the grid interval and Snap to Grid); a large label breaks at its line breaks, a small one
    runs them together; the quick access bar's tips number 1-9, 01-09, 0A-0Z (`MnemonicsManager`).
  - `ToolstripResizer` transcribes `Tab.resize`: sections ordered by collapse priority, highest
    first, ties in tab order; too narrow, labels drop from the last section towards the first and
    then sections collapse from the last, each collapsed one showing its labels again in its popup;
    wider, sections expand from the first without labels while they fit, and only if every section
    expanded do labels come back from the first while they fit (R2025b measures nothing more after
    re-collapsing the section that did not fit, so its label pass does not run: while any section
    is collapsed the expanded ones have no labels). A priority of -1 never collapses. The collapse
    button's 20 pixels are reserved. Galleries, which shrink first in the framework, are left out:
    App Designer has none.
  - `ToolstripKeyTips`: Alt pressed and released alone shows the tabs' tips and the quick access
    bar's numbers; a tab's tip selects it and shows its controls' tips; a control's tip runs it;
    a key that begins longer tips narrows the shown ones (Step is `DD`); Escape forgets a partial
    tip, then goes back to the tabs, then hides them. `Assign` keeps the layout's tips (repeats
    included: Run, Pause and Continue share `R`) and gives every other tab and control a letter of
    its label, the first letters of its words first, no tip ever beginning another; with more
    entries than free letters, a letter is kept for two-letter tips.
- **The WPF control, `JGraph.Controls/Toolstrip`**, built in code like the designer's other views
  and themed by `ThemeKeys` (no new key):
  - `Toolstrip` (a `Border`): the tab headers (a `TabControl` whose content the toolstrip shows
    itself, so UI Automation sees tabs and tab items), the quick access bar and the
    minimize button (R2025b's "Minimize Toolstrip" and "Restore Toolstrip") in the header row, the
    selected tab's sections below. `SetContext(tabGroups)` shows contextual tabs by tab group (the
    selected tab, if it goes, gives way to the first contextual tab newly shown); `SetTabVisible`
    hides a tab whatever the context (SIMULINK for an app without a model); `SetEnabled`,
    `SetVisible`, `SetSelected`, `SetValue` by tag or action id; `Invoked` for every control.
  - **Commands by action id.** `CommandResolver` gives the command for an action id, and every
    button and popup item with one is bound to it with `CommandTarget` set to what the host names
    (its window). The toolstrip is a focus scope, as WPF's `ToolBar` is, so a click leaves the
    document's logical focus where it was - and a command without a target would go to that
    focused element: with a text box focused, Undo from the toolstrip reached the window only with
    `CommandTarget` set and the text box's own Undo without (both checked). After a mouse click,
    keyboard focus goes back to the document, as R2025b's toolstrip restores it.
  - **A column follows what it shows.** R2025b hides Pause and Continue, and Step In and Step Out
    while no app runs, by taking them out of their columns
    (`rtcintegration/plugins/toolstrip/ExecutionControlsToolstrip`), so Run, alone in its column,
    draws large. `SetVisible` hides a control and lays its column out again over what it shows.
  - Buttons: push, toggle (a button group keeps one pressed), drop-down and split (a large split
    runs from its icon and opens from its label and arrow; a small one opens from the arrow at its
    right). A drop-down, a split button's arrow and a collapsed section stay pressed while their
    popup is open and offer UI Automation's ExpandCollapse; push buttons Invoke, toggles Toggle,
    the spinner's and the text field's boxes Value, tabs SelectionItem. Automation ids are the
    layout's tags (a split arrow's is the tag with `.arrow`, a collapsed section's drop-down the
    section's tag with `.dropDown`); names are the labels on one line; help text the
    descriptions.
  - Popups are context menus built when they open, so a dynamic panel (recent files, argument sets,
    functions) is filled by `PopupPanelProvider` each time; check items keep their state; R2025b's
    `closePopupOnClick` is obeyed. A collapsed section's popup holds its columns.
  - The spinner refuses a value outside its limits, or not a number, with the layout's error text
    ("Value must be between 10 and 50") as its tooltip and help text, and steps within them.
  - The fit runs on a layout pass (`UpdateLayout`) before each measure: a section that collapses or
    drops its labels has only its descendants' measures made invalid, so measuring the section
    alone read its old width and every fit flipped all sections (found by the sample's first
    self-test).
  - Key tips are drawn by an adorner, centred under a large button or a tab and 12 pixels in under
    a small one, 5 pixels above its bottom, as R2025b places them; the minimized toolstrip's
    overlay has an adorner layer of its own.
  - `ToolstripIcons`: JGraph's own drawings (decision WO1), 73 of them under R2025b's icon names -
    every name the recorded layout uses, the execution controls', the editor's common ones, the
    section icon and the arrows and chevrons the control draws - drawn at 16 or 24 pixels in the text colour, fixed colours where they mean
    something (run green, stop red).
- **The sample and its checks.** `tools/runtime-checks/toolstrip-sample` shows the recorded App
  Designer toolstrip over a text box with Design View, Code View and Debugging buttons, writing
  every command and invocation to a log; `-selftest` runs the checks that need WPF's real layout.
  `run-w1toolstrip.ps1` runs the self-test in both themes and then drives the window through UI
  Automation patterns only and pictures it. `ui-probes/w0/oracle/resize.js` runs R2025b's own
  `Tab.resize` over 400 seeded tabs of model sections and writes `ToolstripResizeOracle.json`,
  which `ToolstripTests` replays through `ToolstripResizer`.

## Consequences

- Stage W2 builds `DesignToolstrip` from `w0_toolstrip.json` with `ToolstripConfigReader`, binds
  R2025b's action ids in `DesignActions`, and replaces the window's flat toolbar with the control.
  It fills the words the recording leaves to the editor's own actions (Step, Stop, Comment, Indent
  and the code-folding buttons have no icon or text from the rich-text editor's catalog, which W0
  stubbed) and swaps the execution controls with `SetVisible`.
- The compiler apps (open item 216) can adopt the control: nothing in it knows App Designer.
- The layout model sits where the designer's other UI-free classes do, so the window, the
  context menus, the keyboard shortcuts and the tests read one description.

## Measured

- **R2025b's resize** (`ToolstripResizeOracle.json`): 400 seeded tabs of one to seven sections,
  twelve widths each, 4,800 resizes; `ToolstripResizer` gives R2025b's state for every section after
  every one. The first transcription differed wherever the widening pass re-collapses a section:
  it measured again there and R2025b does not; fixed.
- **Tests**: `ToolstripTests` (16): the six tabs, their sections and the shared RUN section; the
  controls with their actions' words, mnemonics, the combo box's items, the spinner's error text,
  the SPLIT DOCUMENT button group; every popup a split or drop-down button opens, the RUN popup's
  headers and check items, the save menu's separator, the go-to dynamic panel; the quick access
  bar; `MapItems`; orientation and label shedding on the recorded columns; labels and tooltips; the
  quick access tips; the resize against the oracle, a priority of -1, priorities in order; tip
  assignment and prefix freedom; the tip machine's narrowing, completion and Escape. 16 pass.
- **The self-test** in both themes, at 1280 by 420: Design View's tabs DESIGNER, CANVAS, VIEW and
  Code View's DESIGNER, EDITOR, VIEW, CANVAS giving way to EDITOR and DESIGNER kept across views;
  the CANVAS tab (FILE, ALIGN, ARRANGE, SPACE, RUN; 724 pixels wide) fitted at 1500, 1100, 900,
  760 (all expanded and labelled), 640 (RUN collapsed, the rest without labels), 520 and 400
  (ARRANGE, SPACE and RUN collapsed), 300 (all five collapsed, 284 of 284 pixels) and 1500 again
  (all expanded and labelled); typed text kept when Undo from the quick access bar went to the
  window, and undone when the target was taken away; Save's command and `Invoked`; the tab tips
  `D`, `C`, `V` and the quick access bar's 1-8, DESIGNER's controls' tips with R2025b's `R` and
  `DD` and prefix-free, `R` running Run; the group keeping one button pressed; the spinner
  refusing 5 and taking 20. Exit 0.
- **The window check** (`run-w1toolstrip.ps1`, every step a UI Automation pattern): tabs selected;
  App Details invoked with its name and R2025b's description; SHARE expanded, Standalone Desktop App
  invoked from its popup and SHARE collapsed after; Save's arrow expanded and Save As... invoked;
  Grid toggled on; the interval set to 30 through Value and committed when the focus left; Code
  View selecting EDITOR; Debugging swapping Run for Continue and showing Step In and Step Out; the
  window narrowed to 560 with RUN collapsed to its drop-down, its popup opened and Run invoked
  from it; widened again with RUN expanded; minimized, DESIGNER opened over the document and New
  invoked from there; restored; pictures of each and of the dark theme. All 54 checks pass, the two self-tests among them.

## Not measured

- **The Alt key from the keyboard**: the window-level Alt handling is not driven, since the checks
  synthesize no input; the tip machine and the control's `ShowKeyTips`/`TypeKeyTip`, which the
  handlers call, are checked.
- **A context menu's picture**: `PrintWindow` draws the layered popup window black behind its text.
- **R2025b's own toolstrip in a picture** beside JGraph's: no MATLAB window was opened (W0 read the
  rules from the code instead).
- **A theme swap at run time**: the sample starts in one theme; every brush is a dynamic resource.

## Divergences

- **Every tab and control has a key tip.** R2025b's App Designer gives tips only to Run, Pause and
  Continue (`R`), Step (`DD`) and Stop (`Q`), the search field and the quick access bar; its tabs
  have none, so its control tips cannot be reached. JGraph keeps R2025b's and gives the rest a
  letter of their label.
- **A section the widening pass has to collapse again shows its labels in its popup.** R2025b's
  leaves them hidden there until the next narrowing; the tab's width is the same either way.
- **The toolstrip has no context menu**: R2025b's Show Label, Hide Label, Add to and Remove from
  Quick Access Toolbar and Restore Defaults are not built, and the quick access bar is the
  layout's.
- **No galleries and no compact mode**: the framework has both; App Designer uses neither.
- **The icons are JGraph's own** (decision WO1), under R2025b's names.
