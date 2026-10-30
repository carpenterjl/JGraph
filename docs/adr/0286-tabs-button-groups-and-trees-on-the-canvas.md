# ADR 0286 — Tabs, button groups and trees built on the canvas

## Status

Accepted, 2026-10-29. Stage W5c of the App Designer window plan: the third part of W5 (clipboard and
structure), after the clipboard (W5a, ADR 0284) and in-place text (W5b, ADR 0285). W5d (menus,
toolbars and context menus on the canvas) and W5e (Replace With) follow.

## Context

After W5b a tab group's tabs, a button group's buttons and a tree's nodes could be added only from
the library as the group's entry makes them, and put in another order only by deleting and pasting.
R2025b's App Designer builds them on the canvas: Add Tab and the tab strip's +, a tab moved by its
menu, its keys or its heading's drag, Add Radio Button and Add Toggle Button, and a tree's nodes
selected in the tree, added with Enter, indented with Ctrl+] and Ctrl+[ and moved with their keys.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`):
`appdesigner/view/contextmenu/AddTabMenuItemProvider`, `ButtonGroupMenuItemProvider` and
`TreeNodeMenuItemsProvider`, `appdesigner/view/designarea/DesignAreaContextMenuController` (the
menus' order), `appdesigner/view/canvas/TabStripMouseController` and `TabDragController`,
`appdesigner/action/ReorderAction`, `appdesigner/command/ReorderCommand`, `AddComponentCommand`,
`MoveCommand`, `DeleteCommand` and `TreeDragReorderCommand`, `appdesigner_interface/command/AddComponentCommand`
and `PropertyCommandFactory`, `appdesigner/action/TreeAction`, `appdesigner/util/TreeUtil`,
`TabsUtil`, `MutualExclusiveButtonUtil` and `PositionUtil`, `appdesigner/view/designarea/plugins/uitree/TreeSelectPlugin`,
`ArrowKeyMoveReorderPlugin`, `appdesigner/view/TreeNodeSelectionHandler`, the code name generators
(`CreateRestrictedParentCodeNameGenerator`), the tab, tab group, tree, tree node and button group
controllers and models (`DesignGestures`, `Deletion`, `Parenting.getSiblingTextValues`,
`isSingleSelection`, `MutualExclusiveChildrenType`) and `AbstractDesignTimeController.setComponentToPreferredSize`.

## Decision

- **Add Tab** (`AddTabMenuItemProvider`, one tab group or one tab selected): a tab after the selected
  tab, or last in the selected group; the tab strip's + adds one last (`_handleTabPlusMouseDown`). It
  is titled among its siblings' titles and code-named in the app (`TabsUtil.createTabComponentData`,
  `CreateRestrictedParentCodeNameGenerator`), selected, and shown - a tab made, put back by an undo or
  pasted is its group's shown tab, as its controller's `createView` makes it `SelectedTab`.
- **Move** (the internal order, `ReorderAction` with `_IS_STACK_ORDER` false): a tab's and a tree
  node's menu's Move submenu - First, Left or Up, Right or Down, Last (Up and Down for tabs down a
  side), with R2025b's Ctrl+Shift keys shown - and the keys themselves; any number selected, each by
  `_performReorder`'s arithmetic and `ReorderCommand`'s moves, one edit, the selection kept. A tab
  heading's drag (`TabDragController`) drops before the first heading whose centre is past the
  pointer, or after the last, one place less when it goes past where it started, while the dragged
  heading still overlaps the strip (`DESIGN_REORDER_OFFSET`).
- **The stack order moves one component alone** - a correction of W4: R2025b's
  `_checkAbilityToReorder` enables Bring to Front, Bring Forward, Send Backward and Send to Back only
  with exactly one selected; W4 moved several together.
- **The shared keys**: Ctrl+Shift+Home, End, Up and Down move a selected tab or node and restack any
  other component; Ctrl+Shift+Left and Right move a tab or node; Ctrl+] and Ctrl+[ indent a selected
  node and change any other's font size (the tree's own keys stop the event at the node; the tab's
  reach `ArrowKeyMoveReorderPlugin`, where only the internal order applies to it).
- **Add Radio Button and Add Toggle Button** (`ButtonGroupMenuItemProvider`, one button group
  selected, by the group's kind - its first radio or toggle button's, a radio button for a group with
  neither, `MutualExclusiveChildrenType`): a button named among the group's buttons, 11 pixels in, 10
  below the group's top or under its lowest button - 1 pixel apart for radio buttons, 1 pixel over
  for toggle buttons (`_calculateLocation`) - made 52 by 15 or 100 by 22 and then sized to its text
  keeping its top (`setComponentToPreferredSize`): a radio button its text in 12-pixel Helvetica, its
  circle and the larger of 5 and 3% more (the 58 and 65 pixels R2025b's own Radio Button Group gives
  `Button` and `Button2`), a toggle button no narrower than 100; selected only when it is the first.
  The group stays selected. A button group holds only its own kind of button (`canMoveToParent`): a
  drag, a library drop and a paste of a radio button into a toggle button group are refused.
- **A tree's nodes**: shown open at the tree's first level, and every node above a selected or added
  node opened (`_expandFirstLevelNodesOnLoad`, `expandTo`); what stands open is the canvas's, not the
  design's. A press on a node's row (`TreeSelectPlugin`) selects it alone, Ctrl adds it to or takes it
  from its tree's selected nodes, Shift selects the rows from the last one pressed - one at a time in
  a check box tree (`isSingleSelection`); with Ctrl or Shift while something else is selected, the
  tree is pressed instead. A press on a twisty opens or closes the node. Add Sibling (Enter) adds a
  node after the selected node, Add Child (Shift+Enter) first inside it, either last in a selected
  tree (`_performAddNode`); Increase Indent moves the node last into the node before it, Decrease
  Indent out of its node after it (`_getParentNodeAndIndexForIndentAction`, `MoveCommand`). Each is one
  edit, and the node is selected. The + beside the one selected node at the tree's right edge, or at
  the top of a selected tree with no nodes, adds a sibling (`TreeToolbar`).
- **A node's menu** (`TreeNodeMenuItemsProvider` and the delete item): Add Sibling, Add Child,
  Increase Indent, Decrease Indent, Move, Cut, Copy, Paste - enabled when the clipboard holds nodes
  only - Duplicate and Delete. A node's text is edited by F2 and a double click on its row with W5b's
  single-line editor; typing does not open it.
- **The canvas menu** gains Add Radio Button or Add Toggle Button after Add Label, and Add Tab with a
  tab's Move after the grid items (`DesignAreaContextMenuController`'s order).
- **A delete** (`DeleteCommand`): a tab group's only tab takes its group with it
  (`DeleteParentIfLastChild`); a deleted tab or node leaves the sibling before the first of them
  selected, else the first left, else its container (`_getClosestComponent`), and a tab so selected is
  shown.
- **The runtime keeps the design's order**: a tab, a node or a node moved in is put in its place in
  the runtime too (tabs sent to the end in turn with `uistack`, nodes with `move`), which also puts a
  tab whose delete is undone back in its place (it came back last before).
- **The menu key** (Apps, Shift+F10) brings the selection's menu, a node's own for a selected node, at
  its first component's centre - the browser's `contextmenu` event at the focused canvas or node.

## Consequences

- W5d's menus, toolbars and their tools move through the same Move (`CanReorder`) with their own
  sibling sets (menus among menus), and their menus join the canvas's the same way.
- W7's component browser can offer Move and the tree's actions through the same document calls.

## Measured

- **R2025b's own code**: `ui-probes/w5/oracle/structure.js` cuts `ReorderAction`'s
  `_checkAbilityToReorder`, `_getReorderableComponents`, `_filterAndOrderByIndex` and
  `_performReorder` out of the bundle as they are and runs them over `ReorderCommand` loaded whole -
  900 moves: the internal order's four and a drag, the stack order's four, one or several selected -
  and loads `MutualExclusiveButtonUtil` whole for 300 buttons added to radio and toggle button groups
  (DesignStructureOracle.json). `DesignStructureOracleTests` replays them: every move allowed, every
  order made and every button's place, size and selection are R2025b's. The oracle shows the stack
  order disabled for every scene with two or more selected.
- **Tests**: `AppDesignerStructureTests` (20): Add Tab after a tab and last, titled and shown, one
  edit; the strip's +; Move and its refusals, the runtime's order; a heading's drag, off the strip and
  with Ctrl; a tab's menu across and down a side; a deleted tab's selection and an only tab's group;
  Add Radio Button under the lowest and into an empty group, one edit; Add Toggle Button and the
  groups' kinds; a tree shown open and a selected node shown; Add Sibling and Add Child; Enter and
  Shift+Enter; the indents and their refusals; Move of nodes and the code's order; a row's press with
  Ctrl and Shift, the tree's + and a twisty; a check box tree's single selection; a node's menu, its
  Paste and the menu key; F2 and a double click on a node, typing not; a deleted node's selection; the
  stack order of one; the live actions. `DesignStructureOracleTests` (4). With the designer, App
  Designer, toolstrip and settings suites: 839 pass.
- **The window check** (`run-w5cdesigner.ps1`, JGraph.Application in Release over `w4/W4App.m` with
  a tab group, a radio button group and a tree added): components selected in the component browser
  and the canvas given the keyboard through UI Automation patterns, the menu key, Enter and F2 posted
  as window messages to the designer's own window once it is checked to be in front, the menu's items
  expanded and invoked through their patterns. Add Tab after T1 (`T1,Tab,T2`, titled `Tab`) and
  Move > First (`Tab,T1,T2`); Add Radio Button under R2 at `[11 25 58 22]`, not selected; Enter on N2
  adding `Node4` after it; F2 on it and Enter saving `Leaf`; the node's menu's Increase Indent moving
  it into N2, and Undo; each read back from the file. All 41 checks pass; `run-w2designer.ps1` (39),
  `run-w4designer.ps1` (23), `run-w5designer.ps1` (34), `run-w5bdesigner.ps1` (54) and
  `run-w6designer.ps1` (34) still pass.

## Not measured

- **The keys with modifiers, a heading's drag, the strip's +, the tree's + and a twisty in the
  window**: held modifiers and a mouse are not carried by posted window messages; the tests drive the
  same gestures.
- **R2025b's tree widget's own selection and editor** (mw-tree's multiple selection and design-time
  editor) and **whether R2025b shows the tab it selects after a delete** were read around, not run.

## Divergences

- **The tab strip's + and the tree's + are drawn by JGraph**: R2025b's style sheet for them is not in
  the bundle read; the + sits after the last heading (under it for tabs down a side) and at the tree's
  right edge level with the node.
- **A tree's nodes are not dragged to another place or node** (`TreeDragReorderCommand`,
  `DESIGN_TREE_DRAG_REORDER`): Move and the indents do it.
- **The tab strip does not scroll** (R2025b's strip has scroll buttons when its headings outrun it).
- **A button group emptied of its buttons takes radio buttons at once**: R2025b keeps the kind it had
  until the app is opened again (its `MutualExclusiveChildrenType` is set when the first button arrives).
- **A node's text is edited with W5b's single-line editor over its row**, not the tree widget's own
  editor; a node's selected rows are filled and outlined by the canvas rather than drawn selected by
  the tree.
- **A toggle button added with a text wider than its 100 pixels is sized by JGraph's text measure**
  and a padding of its own; R2025b measures its widget in the browser.
- **The menu key's menu opens at the selection's first component's centre**, where the browser opens
  it at the focused element.
