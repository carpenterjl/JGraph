# ADR 0284 — The clipboard on the canvas: Cut, Copy, Paste, Duplicate, a Ctrl-drag's copy and Select All

## Status

Accepted, 2026-10-29. Stage W5a of the App Designer window plan: the first part of W5 (clipboard and
structure), after zoom and view (W6, ADR 0283). W5 is split in five, each its own commit and ADR:
W5a the clipboard, W5b in-place text, W5c structure (tabs, button groups, trees), W5d menus,
toolbars and context menus on the canvas, W5e Replace With.

## Context

After W6 the quick access bar's Cut, Copy and Paste reached only the Code View's editor, and on the
canvas there was no Duplicate, no Ctrl-drag copy, and Ctrl+A selected the selection's siblings.
R2025b's App Designer copies components into a clipboard of its own - not the system's - that every
app of the session shares, pastes them into the selection's container with names, places and
callbacks by fixed rules, duplicates them in place, copies them with a Ctrl-drag, and selects the
figure's components with Select All.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`):
`appdesigner/model/AppDesignerClipboard`; the actions `CopyAction`, `CutAction`, `PasteAction` and
`SelectAllAction`; the commands `CopyCommand`, `CutCommand`, `PasteCommand`, `PasteAtCommand`,
`PasteAtIndexCommand`, `DuplicateCommand`, `DuplicateAtCommand`, `DuplicateInGridCommand`,
`CopyComponentCommand`, `CopyCallbackCommand`, `DeleteCommand` and `SelectAllComponentsCommand`;
`appdesigner/util/CopyPasteUtil`, `GridUtil`, `MutualExclusiveButtonUtil` and `GroupUtil`;
`appdesigner/model/codenamegenerator/CopyCodeNameGenerator` and `AbstractCodeNameGenerator`;
`appdesigner/view/canvas/DragDuplicateController`, `WidgetSelectionRules`, `GroupSelectionRules`
and `ComponentMovePlugin`; `StandardMenuItemsProvider`, `DesignAreaContextMenuController` and the
`ContextMenus` catalog; `ViewConstants`; the models' `DesignGestures`, `PositionIsOrderBased`,
`UseExactPositionForPasteIfSameType` and `Deletion`.

## Decision

- **The clipboard** (`DesignClipboard`, one per designer window, so components copied in one app
  paste into another): what Cut or Copy took - each component at the top of the selection, copied
  whole with its place, its cells in a grid and, for a context menu, the components it was assigned
  to; the groups selected whole; the app and container it came from - and a count of the pastes
  into each container, cleared by every Cut or Copy (`AppDesignerClipboard`).
- **Cut, Copy and Duplicate apply** when something other than the figure is selected, all in one
  container, and a group or a component that can be copied is among it - a responsive app's region
  cannot be (`areSelectionsValidForCutCopyOrDuplicateAction`, `RegionPanelModel`). Cut is Copy and a
  delete, the delete one edit; after it nothing is selected (`DeleteCommand`), which a paste reads.
- **Where a paste goes** (`getValidParent`): with nothing selected, the container its components
  came from in the same app, else the figure; with several selected, the tree nodes' parent, the
  group's container or the first one's container; with one, the figure for the figure and a context
  menu, a container itself - a tab group's shown tab, a tab's group for a tab, a menu's parent for a
  menu, a tree's or tree node's parent for its own kind, a toolbar for a tool, a panel's only grid -
  else the component's container. Paste applies when every component can go there: a tab into a
  tab group, a node into a tree, a tool into a toolbar, a radio or toggle button into a button group
  (`canMoveToParent`), no grid beside a grid (`checkIfCanPaste`).
- **Where the copies land** (`_positionInTargetContainer`): into the container they came from, at
  their places, moved 20 pixels along and down for each paste there after the first - after a cut,
  the first lands where they were; into another container, their box's top-left corner 8 pixels in
  from its top-left (`PASTE_UPPER_LEFT_INSET`), moved as often; a tab's components into a sibling
  tab, and another app's into a figure, keep their places. An undone paste is uncounted. Into a
  grid, the first free cells (`GridUtil.findLocation`, transcribed: the corner cells and the second
  column of the rows between them looked at, as its inner loop breaks after one; a group's
  components placed as their group's box; spans kept for components from a grid; only labelled
  components grouped again). Components placed by order go after the selection's last one placed
  by order; a menu after the menu bar's menus.
- **Paste at a point** (the canvas menu's Paste, `PasteAtCommand`): into the container a paste with
  the component there selected goes into, the copies' box's top-left corner at the point; in a grid,
  from the cell under it. It counts no paste.
- **The copies** (`CopyComponentCommand`, `CopyPasteUtil`): renamed as `CopyCodeNameGenerator` names
  copies - each name with an `_N` taken off, made unique among the app's names and those given
  before it, the class's name when too long; a labelled component's field so, its label the field's
  new name with `Label` after it, or its own when the field was named by hand; the components, then
  what is inside each, level by level. References among them follow; others stay in the same app
  when what they name is there; a check box tree's checked nodes are cleared; a radio or toggle
  button pasted alone is selected only into a group with none. **Callbacks** are taken off, except
  on a cut's first paste into the same app (`CopyCallbackCommand`: a paste count of one), which binds
  them again and makes again, with App Designer's code for it, a callback the cut took; another
  app's copies lose their context menus. A cut context menu goes back on the components it was
  assigned to that have none. The groups are made again; the copies are selected, and an undo puts
  the selection back.
- **Duplicate** (Ctrl+D on the canvas, `DuplicateCommand`): copies in the selection's container, 20
  pixels along and down, those placed by order after the last of them, in a grid in the first free
  cells; no callbacks.
- **A Ctrl-drag copies** (`DragDuplicateController`, `DuplicateAtCommand`, `DuplicateInGridCommand`):
  Ctrl alone pressed on what is selected keeps it selected for the drag, its toggle waiting for the
  release; on nothing selected, it is selected; the drag past 4 pixels copies the selection where it
  is dropped - into the container under it, or a grid's cells - and the selection stays. It snaps
  and aligns as a move does, to the selection too.
- **Select All** (Ctrl+A, `SelectAllComponentsCommand`): the figure's components but its menus,
  toolbars and context menus - each group whole - or the figure when it has none; with a menu bar's
  menu selected, its menus; with tools, their toolbar's tools; with toolbars, the toolbars.
- **The canvas menu** (`StandardMenuItemsProvider`): Cut, Copy, Paste and Duplicate before Delete,
  with Ctrl+X, Ctrl+C, Ctrl+V and Ctrl+D beside them (and Ctrl+L beside Add Label); Duplicate not
  for the figure or nothing selected. The window's Cut, Copy and Paste reach the canvas in the
  Design View and the editor in the Code View; `DesignActions` runs 81 of the 202 actions.

## Consequences

- W5c and W5d paste tabs, tree nodes, menus and tools by order through the same rules, and add
  Paste to Right for menus.
- W7's component browser cuts, copies and pastes through the same commands.

## Measured

- **R2025b's own modules**: `ui-probes/w5/oracle/copy.js` runs `CopyCodeNameGenerator` and
  `GridUtil.findLocation` unmodified over 1,600 seeded scenes (DesignClipboardOracle.json);
  `DesignClipboardOracleTests` replays them: every copy's name and every cell found are R2025b's.
- **Tests**: `AppDesignerClipboardTests` (11): a paste into the same container stepping along and
  down and an undo uncounting it; a cut's first paste in place under its name with its callback made
  again, the second without; a paste into another container 8 pixels in; Paste at a point; a
  labelled component copied whole; Duplicate, refused across containers and for the figure;
  Duplicate in a grid's first free cell; a Ctrl-drag's copy, into a panel too; a paste into another
  app; Select All with a group; the canvas menu's items and keys. The arrange, grid, zoom and
  toolstrip tests follow the menu's new items and the live actions. With the designer, App Designer,
  toolstrip and settings suites: 800 pass.
- **The window check** (`run-w5designer.ps1`, UI Automation patterns, JGraph.Application in Release
  opening two apps): Cut, Copy and Paste disabled with the figure selected and nothing copied; one
  button selected in the component browser enabling Cut and Copy; Copy enabling Paste; Paste making
  `A_2` 20 pixels along and down, saved; Cut and its paste putting it back under its name, saved;
  Undo; a copy pasted into the other app at its place, saved. All 34 checks pass; W2's
  `run-w2designer.ps1` (39) still passes.

## Not measured

- **The keys (Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+D, Ctrl+A), a Ctrl-drag and the canvas's menu in the
  window**: they take a mouse or keys, which the checks do not synthesize; the tests drive the same
  gestures and commands.
- **R2025b's whole paste in a window**: only its naming and grid placement were run; the places,
  callbacks and parents follow its commands as read.

## Divergences

- **A Ctrl-drag shows the copies as dashed boxes**, not as pictures of the components, which R2025b
  clones from its page.
- **A right-button drag does not copy**: on Windows R2025b's right press on a component can begin a
  copy too.
- **Groups inside a copied container are not made again**; the copied groups at the top are.
- **Paste is disabled where the container cannot hold what is copied** - a button with a toolbar or
  a tree selected - where R2025b's toolstrip Paste would try.
- **Select All selects a nested group's outermost group**; R2025b's selects such a member and its
  outer group both.
- **The canvas menu's Paste in a grid's gap or padding** places the copies from the first free cell;
  R2025b's leaves their cells unset.
