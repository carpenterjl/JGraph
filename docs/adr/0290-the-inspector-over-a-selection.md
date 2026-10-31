# ADR 0290 — The inspector over a selection

## Status

Accepted, 2026-10-30. Stage W7b of the App Designer window plan: several components' shared properties, the property
search, and what the plan called More/Fewer. W7c has the inspector's editors, W7d callbacks for several components.

## Context

JGraph's inspector showed one component and said "N components selected" for more. R2025b's combines several, searches,
has two views, keeps sub-groups behind a bar, and remembers what was open for each class.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`): `appdesigner/view/propertyview/PropertiesPanelController`,
`base/PropertyViewController`, `base/_PropertyViewFactory`, `inspector/PropertyInspectorViewFactory`, `inspector/PropertyInspectorView`, the
`NonLabeledComponentStrategy`, `MenuStrategy`, `UserComponentStrategy` and `LabeledComponentInDesignViewStrategy`,
`appdesigner_interface/command/PropertyCommandFactory`, `CompositePropertySetCommand`, `CompositablePropertySetCommand`,
`appdesigner/model/codenamegenerator/ModifyCodeNameGenerator`, `inspector_client/widget/InspectorPropertySheet`, `InspectorClientManager`,
`InspectorPropertySet`, `InspectorPropertySubGroup`, `inspector_client/search/InspectorToolbar` and `SearchTextField`, with
`startscreen/release/index-css.css` for the sizes. The sheets themselves come from R2025b's own `InspectorFactory`:
`ui-probes/w7/w7b_inspector.m` writes the JSON it publishes to the client, which has each property's class name, display
name and help, none of which the DS3 recording had.

## Decision

- **What a selection comes to** (`_decorateSelection`, `getView`): one component; a labelled component selected whole, its field's
  rows; or several, a group counting as its every member and the last as the anchor. A custom component among several leaves
  the anchor alone, rows and edits (`ParticipatesInMassEditing`), though the tab still says several.
- **The rows** (`_createInspectorObject`, `_intersectObjectProperties`, `_renderPropertyItem`): the anchor's groups in its order, keeping a
  property only where every component has it **and types it as the first does**. A button's `BackgroundColor` is an `RGBColor`, a
  label's an `RGBAColor`, so they do not share it; an axes' `AD_AliasedVisible` is not a button's `Visible`. A group or sub-group left
  empty goes. The value shown is the last's (`MULTIPLE_VALUES_COMBINATION_MODE.LAST`), never a blank or a mixed mark.
- **A menu's rows** (`_adjustInspectorInfo`): one on the menu bar has no Accelerator, Separator or Checked; one holding menus under a menu
  or context menu no Accelerator or Checked. This was missing for one menu too.
- **An edit** (`_getComponentsAndPropertiesForEdit`, `PropertyCommandFactory`, `CompositePropertySetCommand`): one step on them all, undone
  together, the selection staying. One already holding the value is left alone, and none needing it is no step, which now holds
  for one component as well. When one refuses, those done are put back and nothing has changed. A text edit renames each by
  `ModifyCodeNameGenerator`, each seeing the names before it (`RunButton`, `RunButton_2`). `AutoResizeChildren` reaches every one
  inside them that has it. A labelled component's font, colours, Enable and Visible go to its label in that one step, where they
  were two.
- **A refused value says so in App Designer's alert** (`_showErrorDialog`), the row going back, in place of the status line.
- **The tab** (`_updateContentPaneTitle`): the first tab says `Multiple Components`, or the class (`Button`, `Radio Button Group`, a menu's
  `Menu`, `UI Figure`), or `Component`, with the panel hidden, for nothing selected. **R2025b never shows the words "Multiple
  Components Properties"**: `_updateContentPaneTitle` sets them as a `ContentPane`'s title, and `ContentPane` has `_setTitleAttr: null`.
- **The toolbar** (`InspectorToolbar`, `.ToolbarContainer`): a 22 high search, then Group View and Alphabetical View, one always on.
- **The alphabetical view** (`_createPropertySheetView`): every row flat, by the name it shows, as JavaScript's `<` orders it.
- **The search** (`_handleSearch`, `_handleSearch` of the toolbar, `handleClear`): over the alphabetical view with both buttons off; rows
  saying the text first, in any case, with it marked; then, under `Help documentation results` in bold, rows whose help holds it; the
  rest hidden; `No matches found` for none. Escape and the field's button clear it, as does any new selection, and the view chosen comes back.
- **A row's help** (`_createTooltip`): its heading in bold over its body, from the sheet's `||`, after a second, on the name and the
  editor; a name of 24 characters or more says itself first. Rows say the sheet's display name: `XLabel.String`, a menu's `Text`.
- **"More/Fewer Properties" in R2025b's inspector is a 12 pixel bar with an arrow** (`InspectorPropertySubGroup`, `.property-more`): the
  sub-group's rows hidden above it, opening upward. The words `subsectionMore` and `subsectionLess` belong to the old property sheet
  (`components/SubSection`). They stand here as the bar's automation name only.
- **What is kept** (`InspectorClientManager.objectCache`, `_getIDForCache`, `adjustPropertyInspector`): per app, for one class (a grid's child
  apart) or a set of classes, the open groups and sub-groups and the view chosen. A sheet with another number of properties starts afresh.
- `DesignInspector` gains the sheet's words and sub-groups; `DesignInspectorSheet` holds the rules, free of the screen, with
  `DesignInspectorState`; `DesignDocument.Inspector` the edit; `DesignInspectorView` is rewritten over them.

## Consequences

- JGraph's own code name row stays for one component (the rule on keeping JGraph's extras), and goes for several.
- The label's text row shows only in the group view, as R2025b prepends it to the grouped widget.
- A window check selecting a component it may have selected already must select another first: a row among several selected
  takes no `Select` through UI Automation.
- `setComponentToPreferredSize` after a text or font edit (`PreferredSizeProperties`) is still not done by the inspector, for one
  component or several. It waits for W7c, in open item 232.

## Measured

- **R2025b's own code**: `ui-probes/w7/oracle/inspector.js` runs `_createInspectorObject`, the sheet's combining, rendering, sorting and
  searching, `_updateContentPaneTitle` and `_getIDForCache` cut out of the bundle, and the strategies, `_decorateSelection` and the
  name generators loaded whole, over stand-ins for the peer nodes and the page, on the sheets R2025b's `InspectorFactory` published:
  `DesignInspectorOracle.json` has 605 scenes (every class alone, menus by where they stand, button groups by what they hold, a grid's
  child, 500 mixed selections with groups, labelled components and custom components, 30 groups whole, 20 of `AutoResizeChildren`), about
  3,500 searches and 200 renames. `DesignInspectorOracleTests` (10) replays them, with no difference.
- **Tests**: `AppDesignerInspectorTests` (18). With the designer, App Designer, toolstrip and settings suites: 926 pass.
- **The window check** (`run-w7bdesigner.ps1`, JGraph.Application in Release over `w4/W4App.m` with a spinner and a lamp added): rows
  selected and text set through UI Automation patterns, keys posted to the designer's own window once it is checked to be in
  front; Ctrl+A with Ctrl marked down in the key state of the designer's own input thread only (`AttachThreadInput`, `SetKeyboardState`),
  and put back. All 90 checks pass, each edit read back from the file: the tab's words, the search and its two kinds of match, a
  bracket in it, the view kept for a class and not another, a sub-group kept open, a refused value's alert with the row back,
  several selected with their shared rows, a `Tag` set on seven components, saved, and taken off by one Undo. Pictures show them.
  `run-w7adesigner.ps1` (103), `run-w2designer.ps1` (39), `run-w4designer.ps1` (23), `run-w5designer.ps1` (34), `run-w5bdesigner.ps1` (55),
  `run-w5cdesigner.ps1` (41), `run-w5ddesigner.ps1` (69), `run-w5edesigner.ps1` (53) and `run-w6designer.ps1` (34) still pass.

## Not measured

- **R2025b's App Designer itself in a window**: its code was run under node over stand-ins, not in its own client.
- **Which sheet properties a real peer node lacks**: R2025b shows a sheet's property only if the design-time peer node has it, and the
  stand-ins hold all of them. So a row R2025b leaves out for that reason would show here; the DS3 recording already had this gap.
- **A mouse in the window**: a rubber band, or a Ctrl or Shift click, are not driven, as posted mouse messages do not reach the canvas.

## Divergences

- **A search takes what is typed as it is.** R2025b builds its highlight's pattern from the text unescaped, so `(` throws part way through
  and `.` marks every letter.
- **A value refused on one of several puts the others back straight away**, where R2025b waits for each promise; the end is the same.
- **A grid's child keeps its Position among several**, by each one's own `IsEditable`. R2025b asks only the anchor, whose siblings are in
  the same grid in any selection the canvas allows, so nothing differs but the check.
- **The view buttons have JGraph's glyphs**, the search's clear button a cross, and a scroll position is not kept per class.
- **No refresh button or timer warning** in the toolbar: both are for the Property Inspector over running figures (`showTimerWarning`).
- **The app's row shows only its name until W8**, under the tab `App`, with no toolbar.
