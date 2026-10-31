# ADR 0289 — The component browser

## Status

Accepted, 2026-10-30. Stage W7a of the App Designer window plan, the first part of W7 (inspector and browser), which
is split into W7a the browser, W7b the inspector's multiple selection, search and More/Fewer, W7c its editors, and W7d
callbacks for several components with the Callbacks submenus.

## Context

JGraph's browser was a plain tree of code names with a Rename dialog. R2025b's has a toolbar, three views, a search,
rows picked with Ctrl and Shift, names edited in place, drags among siblings and to other parents, a long menu, a
collapsed form and a height of its own.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`): `appdesigner/view/browser/ComponentTreeView`,
`ComponentTreeController`, `ComponentTreeEditor`, `ComponentBrowserLayoutManager`, the three
`appdesigner/view/browser/strategy` classes, `appdesigner/model/viewmodel/ComponentTreeViewModel` and `AppViewModel`,
`appdesigner/widget/search/ComponentTreeToolbar`, `appdesigner/command/TreeDragReorderCommand`, `ReorderCommand`, `MoveCommand`,
`DeleteAndCopyCommand`, `DesignAreaContextMenuController._createComponentTreeContextMenu` with the providers it lists
(`IncludeLabelComponentsMenuItemProvider`, `ComponentTreeSortMenuItemProvider`, `AddLabelMenuItemProvider`,
`ComponentRenameMenuItemProvider`, `StandardMenuItemsProvider`, `ComponentDeleteMenuItemProvider`,
`ComponentReorderMenuItemProvider`, `InsertCodeNameMenuItemProvider`, `TabOrderMenuItemsProvider`), each model's
`ComponentTreeBehavior`, `ReversedComponentViewOrder`, `IsSameTypeSelection`, `isSingleSelection` and `canMoveToParent`,
`appdesigner/model/SelectionModelManager`, `inspector_client/objectbrowser/ObjectBrowser`, `inspector_client/breadcrumbs/Breadcrumbs`,
and `mw-tree` and `mw-data-model`'s selection, keys, drag and search.

## Decision

- **The rows** (`ComponentTreeViewModel.getTreeData`): the app's own row, named for its class; under it the figure; each
  component saying `app.CodeName`, in the stack order. Three views: without the labels of labelled components, the
  factory view; with every component; and the tab order, which lists what Tab goes to, in the order it is made, leaving
  out labels, lamps, gauges, knobs, switches, axes and context menus (`isShownInTabOrderTree`). The setting
  `IncludeComponentLabels` opens each app's browser, and its menu changes that app's alone.
- **The toolbar** (`ComponentTreeToolbar`): the search, then `View:` and a drop-down of Sort by Stack Order and Sort &
  Filter by Tab Order. Leaving the tab order goes back to what was listed before (`_previousStrategy`).
- **The search** (`_filter`, `_search`): rows whose whole label holds the text in any case, with the rows above them, all
  open, what was found marked; `No matches found` for none; Escape and the field's button clear it. The label is searched,
  so `app.` finds every component.
- **Selection** (`_setSelection`, `MultipleNodeSelection`): Ctrl adds or takes off a row, Shift runs from the row last
  clicked, Shift with an arrow extends; several rows go together while each has the first's parent and, for menus, context
  menus and toolbars, its class. A figure, a tab or a check box tree's node among several gives the figure, or the last
  asked when the figure was selected. A selected row waits for its release, so that several can be dragged. A member of a
  group picked in the tree is selected by itself, as `setComponentSelection` takes it, and the tree shows a group as its
  members. Closing a row keeps the selection as it is.
- **A name in place** (`ComponentTreeEditor`, `DesignTimeEventsConfig`): F2, a double click, a letter or digit typed, or the
  menu's Rename open `app.` and a box. Enter or leaving saves; Escape keeps the name; an empty name changes nothing; a
  name `isAppAttributeNameValid` refuses is said in App Designer's alert. The app's row is not renamed. This takes the
  place of JGraph's Rename dialog for a component; the code browser's stays.
- **The menu** (`_createComponentTreeContextMenu`), in its order: Include component labels, shown but for the tab order;
  the two sorts; Add Label; Rename; Cut, Copy, Paste and Duplicate, in the Design View only; Delete and Reorder, for
  anything but the figure and the app; Insert at Cursor, in the Code View only, disabled on a generated line; and the tab
  order of one container or the whole app.
- **Drags** (`_validateDrag`, `_validateDrop`, `ReorderUtility`, `TreeDragReorderCommand`): a row's top and bottom 5 pixels
  drop beside it, with a line, and its middle on it, first among its rows; a closed row opens after a second's hover.
  Menus, context menus, toolbars and axes stay among their own class. Among siblings the sources go beside their
  neighbours in the rows by the view's rule, reversed for the stack order but not for the tab order; menus, tools, tabs,
  tree nodes and slider switches go to other parents, into what `canMoveToParent` lets them. One edit, which undoes.
- **The collapsed form** (`ObjectBrowser`, `_generateBreadcrumbDataFromSelection`): closing the app's row, or the splitter
  dragged small, shows breadcrumbs from the app to the selection, `Multiple Components(n)` or `Group(n)` at the end, cut at
  20 characters; a crumb selects, and the expander brings the tree back. The app's row picked selects the app.
- **The height** (`_layoutChangeHandler`): 21 a row and 33 for the toolbar, at most three quarters of the side, until the
  splitter is dragged.
- `DesignBrowser` holds the rules, free of the screen; `DesignDocument.Browser` the edits and `DesignGestures.Browser` the menu.

## Consequences

- The Callbacks items of R2025b's browser menu (`ComponentMenuItemsProvider`) come with W7d.
- The app's row shows its name in the inspector until W8 gives it App Details' rows.
- A check selecting a label through the browser must include labels first.

## Measured

- **R2025b's own code**: `ui-probes/w7/oracle/browser.js` loads `ComponentTreeViewModel`, the three strategies,
  `TreeDragReorderCommand` and `ReorderCommand` whole and cuts `_validateDrag`, `_validateDrop`, `_setSelection`, what it calls,
  `AppViewModel`'s view order and `_layoutChangeHandler` out of the bundle, over stand-ins for the peer nodes, into
  `DesignBrowserOracle.json`: 200 trees in three views, 300 drags, 1,200 drops, 200 reparents, 800 selections and 400 heights,
  with each class's flags read from its model's own text. `DesignBrowserOracleTests` (7) replays them.
- **Tests**: `AppDesignerBrowserTests` (11). With the designer, App Designer, toolstrip and settings suites: 882 pass.
- **The window check** (`run-w7adesigner.ps1`, JGraph.Application in Release over `w4/W4App.m` with a slider, a tree and a
  toolbar added): rows selected and text set through UI Automation patterns, the menu key, F2, Enter and Escape posted
  to the designer's own window once it is checked to be in front, each edit read back from the file. All 103 checks pass;
  pictures show the rows, the search with its mark, the tab order, the editor, the breadcrumbs and the Code View.
  `run-w2designer.ps1` (39), `run-w4designer.ps1` (23), `run-w5designer.ps1` (34), `run-w5bdesigner.ps1` (55),
  `run-w5cdesigner.ps1` (41), `run-w5ddesigner.ps1` (69), `run-w5edesigner.ps1` (53) and `run-w6designer.ps1` (34) still pass.

## Not measured

- **R2025b's App Designer itself in a window**: its code was run under node over stand-ins, not in its own client.
- **The setting changed while apps are open**: `DesignBrowserState.SettingChanged` is tested and the window's wiring read;
  no check changes the setting with an app open.
- **Ctrl and Shift clicks and a row's drag in the window**: they need a held key or button, which a posted message
  cannot give, so the view's mouse handling for them, the drop line and the hover's opening are read, not driven. Their
  rules are replayed by the tests.

## Divergences

- **A slider switch does not go where it cannot stand.** R2025b's `canMoveToParent` lets one go to anything, a button or the
  app's row among them; here it goes to the figure, a panel, a tab or a button group.
- **A context menu does not go into a menu.** R2025b's validation copies the menu's and lets it, which MATLAB then refuses.
- **A component in a group does not go to another parent.** R2025b leaves a labelled slider switch's label behind.
- **Several rows dropped under another parent all go, in their order.** R2025b's `DeleteAndCopyCommand` takes only the first,
  and `MoveCommand` puts each at one index, reversing them.
- **The one object goes over**, where R2025b deletes a menu or tab and pastes a copy; nothing differs in the file.
- **A context menu's items are not listed in the tab order.** R2025b hands the tree rows whose parent it does not list.
- **A custom component is always listed in the tab order**, where R2025b asks whether anything in it takes focus.
- **The toolbar's look is JGraph's**, with its own glyphs for the two views, and a drag has no avatar beside the pointer.
- **No Ctrl+Space, and no typed character opening the editor with several selected.**
- **A component's Rename shows no preview of what changes.** DS5's dialog did, and R2025b has none; the code browser's keeps it.
