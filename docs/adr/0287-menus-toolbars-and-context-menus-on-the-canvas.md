# ADR 0287 — Menus, toolbars and context menus built on the canvas

## Status

Accepted, 2026-10-30. Stage W5d of the App Designer window plan: the fourth part of W5 (clipboard and
structure), after the clipboard (W5a, ADR 0284), in-place text (W5b, ADR 0285) and tabs, button groups
and trees (W5c, ADR 0286). W5e (Replace With) follows.

## Context

After W5c a figure's menus, toolbars and context menus could be added from the library, but the canvas
showed none of them: they could be reached only in the component browser and the inspector. R2025b's App
Designer draws the menu bar and toolbars above the figure's content, opens a menu's list under it when it
is selected, and keeps the context menus in a tray under the canvas, from which each is opened to be
edited and dragged to the components it goes to.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`):
`appdesigner/action/MenuAction`, `MenuBarAction`, `ToolAction`, `ToolbarAction` and `ReorderAction`,
`appdesigner/util/MenuUtil` and `ToolbarUtil`, the plugins under `appdesigner/view/designarea/plugins`
(`menubar`, `menuhierarchy`, `tool`, `toolbar`, `toolbarhierarchy`, `uicontextmenu`), the strategies beside
them, the drag services under `appdesigner/view/canvas/mousegestureservices` (`menuhierarchydrag`,
`toolbarhierarchydrag`, `dragassign`), `appdesigner/view/canvastray` with `CanvasTrayViewController` and
`CanvasTrayViewModel`, `appdesigner/view/menubar`, `appdesigner/view/contextmenu`
(`ToolMenuItemProvider`, `ToolbarMenuItemProvider`, `ContextMenuMenuItemProvider`,
`StandardMenuItemsProvider`), `DesignAreaContextMenuController`, `appdesigner/command`
(`AssignContextMenuCommand`, `UnassignContextMenuCommand`, `AddAndAssignContextMenuCommand`,
`PasteAtIndexCommand`), `appdesigner_interface/model/_ComponentContextMenuInfo`,
`appdesigner_interface/controller/ContextMenuAssignable`, `PlacementUtil.getCandidateAssignTargets`,
`AssignmentValidation`, `ComponentDnDManager`, `SingleDropEntryModel`, and the menu, tool, toolbar and
context menu models, controllers and library entries. The style rules are in
`toolbox/matlab/appdesigner/startscreen/release/index-css.css`, not in the bundle.

## Decision

- **The bars stand above the figure's content** (`gbtFigureToolsContainer`), which stays where the
  surface's origin is: the menu bar 22 pixels high, each toolbar 26, the canvas growing by them; the
  bars are at negative y on the surface. They are laid out from the design (`DesignBarLayout`), so an
  order or a text shows at once, and drawn by the canvas.
- **A selected menu shows its list** (`ad-child-menu-selected`): its items in a column under it, or
  beside an item with items under it, with the + under the list that adds at its end, unless something
  further down is selected (`ad-descendant-selected`), and the + beside a selected item with none under
  it that adds its first (`ad-menu-add-right`). The menu bar ends in a + that adds a top-level menu
  (`ad-menu-add-toplevel`). Several top-level menus selected show no list (`ad-multiselected-menu`).
- **Add Menu** (`MenuAction._performAddMenu`): Add Menu to Right (Shift+Enter) beside a top-level menu,
  and first under any other; Add Menu Below (Enter) first under a top-level menu and after any other.
  Shift+Enter on an item with items adds last under it; both do nothing on a context menu. Named among
  its siblings' texts, selected, one edit. A dropped menu bar selects its first menu alone
  (`MenuBarEntryDefinition`), and drops once (`SingleDropEntryModel`).
- **Add Push Tool and Add Toggle Tool**: after a tool and first in a toolbar by a tool's menu and keys
  (`ToolAction`, Shift+P, Shift+T - any P or T without Ctrl, as `ToolbarHierarchyKeyboardPlugin` takes
  them); last by a toolbar's menu and keys and its + (`ToolbarAction`, `ad-toolbar-split-button`), whose
  arrow opens a menu of both. A dropped toolbar goes among the toolbars by where it was dropped.
- **The keys** (`MenuHierarchyNavigateReorderPlugin`, `ToolKeyboardPlugin`, `ToolbarKeyboardPlugin`): the
  arrows, Home and End go from one to another; Escape goes up to the parent; each is taken even when it
  does nothing, so a menu's arrows never reach the canvas's nudge.
- **Move** (`ReorderAction`'s internal order) orders menus among menus, toolbars among toolbars and tools
  in their toolbar, by the Ctrl+Shift keys and the Move submenu - a top-level menu's and a tool's Left and
  Right, a list's and a toolbar's Up and Down. A tool takes no Ctrl+Shift+Up or Down, a toolbar no
  Ctrl+Shift+Left or Right. Stacking stays for the components (W5c).
- **A drag** (`handleDoingMenuHierarchyDrag`, `handleDoingToolbarHierarchyDrag`, the drag models): a menu
  along its row, while it still overlaps it; a toolbar over the toolbars; a tool along its toolbar or into
  another, `MoveCommand`'s one edit. Going on, the last of the selected lands at the drop
  (`DESIGN_REORDER_OFFSET` with a `Direction`).
- **In place**: a menu's text by F2, a typed character and a double click, its editor's Up and Down saving
  and going on (`ArrowKeyExit`, `MenuBarInPlaceEditPlugin`; a context menu's items stay); a tool's icon
  by R2025b's file dialog, typed P and T excepted.
- **Each one's menu** (`DesignAreaContextMenuController`): a menu's Add Menu Item, Add Menu, Move, the
  clipboard's with **Paste to Right** (`_handlePasteRight`: after a top-level menu, last under any
  other) and Delete; the menu bar's Add Top-Level Menu, Select All Menus, Copy and Paste; a tool's and a
  toolbar's Add Push Tool, Add Toggle Tool, Move, the clipboard's and Delete; a context menu's clipboard's
  and Delete, Go to Component and Edit; the tray's own Paste of a context menu. The menu key brings the
  selection's.
- **A component's Context Menu submenu** (`buildContextMenuAssignment`), after the figure's display items:
  Add New Context Menu and Assign without one; Go to, Replace With and Unassign Context Menu with one. The
  figure takes one; a menu, a toolbar, a tool and a context menu do not.
- **The Context Menus tray** (`CanvasTrayView`), at the bottom of the view, not zoomed: gone with no context
  menu, a tab opening and closing it, dragged to resize, closing under 150 pixels, opening by itself for a
  new context menu or one selected, not for an app opened. Each context menu's entry shows `app.Name`, how
  many it is assigned to with a tick or a warning, what they are on hovering, and the first of its items.
- **Editing in the tray** (`ContextMenuRichEditPlugin`): a double click or Edit opens one, its first item
  selected, under the line that goes back; selecting an item opens its context menu, selecting a context
  menu shuts it. Its items take the menus' keys, +s, drags and editor, in the tray's own scale.
- **A context menu's drag** (`DragAssignContextMenuService`, past 4 pixels; `AssignmentValidation`): over
  the frontmost, innermost component that takes one, 2 pixels larger each way, the figure last; a tooltip
  in R2025b's words beside the pointer, the component hinted, red when it has that one already, and those
  that have it ticked; dropped, assigned, one edit that leaves the selection. **From the library**
  (`CanvasTrayTarget`): made and assigned on a component, made and assigned to nothing over the tray,
  refused anywhere else.
- **A delete** selects what R2025b's `_getClosestComponent` gives for a menu, a tool and a context menu too.

## Consequences

- W5e's Replace With joins a tool's menu after its Delete; W8's Callbacks join every one of these menus; W7
  can offer the same document calls in the component browser.
- `DesignBarLayout` is what a later scrolling tray or a hover hint would build on.

## Measured

- **R2025b's own code**: `ui-probes/w5/oracle/bars.js` runs over 500 seeded figures, as they stand and given
  their modules' own names for their imports, `MenuAction`'s `_performAddMenu`, `_performAddMenuAtEnd` and
  `_updateAction`, both tool actions' `_performAddTool`, the keyboard plugins (with their MenuBar and
  ContextMenu subclasses, `ToolKeyboardPlugin` and `ToolbarKeyboardPlugin`), `MenuBarInPlaceEditPlugin`'s
  arrows, `ReorderAction` over `ReorderCommand` loaded whole, and the three drag models' drops, over
  `MenuUtil` and `ToolbarUtil` loaded whole (DesignBarsOracle.json). `DesignBarsOracleTests` replays them:
  what is enabled, where each add goes, what each key selects, every move and drop.
- **Tests**: `AppDesignerBarsTests` (15): a menu bar above the figure, dropped once; Enter and Shift+Enter; a
  list's +s; the keys; Move and a drag; tools by keys, menu and +; a tool dragged along and across, a
  toolbar's drag; the in-place edits; Paste to Right and the menu bar's menu; a component's Context Menu
  submenu; a drag from the tray and drops from the library; the tray's editing; what a delete leaves
  selected; the menu key; the live actions. With the designer, App Designer, toolstrip and settings suites:
  860 pass.
- **The window check** (`run-w5ddesigner.ps1`, JGraph.Application in Release over `w4/W4App.m` with a menu bar
  and two toolbars added): components selected in the component browser and the canvas given the keyboard
  through UI Automation patterns, the menu key, Enter, F2, Escape, P and T posted as window messages to the
  designer's own window once it is checked to be in front, the menu's items expanded and invoked through
  their patterns; each result read back from the file. All 69 checks pass. A picture shows the bars, a list
  open with its +s, the tray with its entry and the tray open on a context menu. `run-w2designer.ps1` (39),
  `run-w4designer.ps1` (23), `run-w5designer.ps1` (34), `run-w5bdesigner.ps1` (55), `run-w5cdesigner.ps1` (41)
  and `run-w6designer.ps1` (34) still pass.

## Not measured

- **A press on the bars, their +s, a drag of a menu or tool, the tray's own presses, a context menu's drag
  and the Ctrl+Shift keys in the window**: a mouse and held modifiers are not carried by posted window
  messages; the tests drive the same gestures.
- **What R2025b's tray does with the selection moved off a context menu being edited** was read around, not
  run (`ContextMenuRichEditPlugin` listens only while a context menu's strategy is active): here it ends the edit.

## Divergences

- **The bars and the tray are drawn by the canvas in JGraph's measure**: widths from JGraph's text measure,
  6 pixels each side of a top-level menu, a list's rows 26 pixels high; a tool with no icon shows a faint
  square, where R2025b shows nothing.
- **No hover marks** on menus, tools and toolbars (`ad-menu-hovered`, `ad-widget-hovered`), and no delay
  before a tray entry's tooltip beyond WPF's own.
- **A menu's, tool's and toolbar's drag starts at the system's drag distance**, not R2025b's 3 pixels.
- **The library's Menu Bar entry stays enabled with a menu bar there**, and its drop is refused with a word
  in the status bar, where R2025b disables the entry.
- **No drop hints for a menu bar or a toolbar dragged from the library** (`ad-highlight-droplocation-menubar`,
  `ad-toolbar-highlight`, the figure moved down to make room).
- **The tray does not scroll** past what its height shows (`overflow:auto`), and is narrower than 600 pixels in
  a narrower view; its entries' torn lower edge, the badge's bounce, the recommended-target box and a context
  menu raised in the tray while an Assign item is pointed at (`focusWidget`) are not drawn.
- **A menu's own Paste pastes as Paste does**, not at the point the menu opened at (`PasteAtCommand`), for
  components pasted with a menu selected.
- **A menu added to the right of a top-level menu goes beside it among the figure's children**, for an app
  whose menus are not first among them (written by hand); R2025b's place among menus alone assumes they are.
- **The selection moved off a context menu being edited ends the edit.**
