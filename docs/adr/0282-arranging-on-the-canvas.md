# ADR 0282 — Arranging on the canvas: align, size, space, reorder, tab order, labels and fonts

## Status

Accepted, 2026-10-28. Stage W4 of the App Designer window plan, after the Start Page (W3, ADR
0281); it was the designer leftovers plan's C1 (open item 151, part 1).

## Context

After W3 the CANVAS tab's ALIGN, ARRANGE (but Grouping) and SPACE sections were shown disabled with
their stage, the canvas had no keys for them, the figure could be resized only by typing its
`Position`, a label could not be put beside a component that had none, and the library always made
a labelled component with its label. R2025b's App Designer does all of these from the toolstrip, the
canvas's keys and its context menu. W4 builds them.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`, the main bundle):
`appdesigner/command/Alignment`, `SameSize`, `Spacing` and their commands; `AlignAction`,
`SizeAction`, `SpaceAction`, `ReorderAction`, `ReorderCommand`, `TabOrderAction`,
`AutoTabOrderCommand`, `VisualizeTabOrderCommand`, `FontAction`, `FontCommand`, `AddLabelAction`,
`AddLabelToComponentCommand`, `LabeledComponentEntryDefinition`, `LabeledComponentPositionConfig`
and its calculator; `SelectionModelManager`'s anchor; the models' `DesignGestures` and
`ComponentTabBehavior`; `DesignAreaKeyboardController` and `ArrowKeyMoveReorderPlugin`;
`DesignAreaContextMenuController`, `LayoutMenuItemsProvider`, `TabOrderMenuItemsProvider` and
`AddLabelMenuItemProvider`; the figure resize service (`FigureResizeCalculator`,
`FigureResizeModel`, `FigureResizeCommand`) and the `ad-figure-edge` rules of its style sheet;
`ComponentEntryModel`'s optional entry.

## Decision

- **The anchor** (`DesignDocument.Anchor`): `SelectionModelManager`'s - the last selected of two or
  more, none for one; a right click on one of the selection makes it the anchor. The commands set it
  back after they run and are undone, as theirs do. The canvas outlines it 2 pixels wide
  (`ad-layout-anchor`).
- **Align, Same Size and Space** (`DesignArrange`, UI-free) transcribe `Alignment`, `SameSize` and
  `Spacing` in their own arithmetic and order: MATLAB's positions with the y axis upward, centres
  rounded as `Math.round` rounds, Same Size never under a component's least size, not along a side
  its shape fixes (a slider's), in its own proportions where its shape keeps them, its top edge kept;
  Space evenly between the outermost two for three or more (the pixels left over one each to the
  first gaps) or by the box's pixels from the first, sorted across or down with ties by the other
  edge and then the stack order. A group is one box round its components' outer positions, edges
  rounded (`calculateGroupSizeProperties`), moved whole. They apply as the actions' `_updateAction`
  says: two or more, every one supporting the gesture by its model (not tabs, menus, context menus,
  toolbars and their tools, tree nodes or grids; Same Size not a group), the first not in a grid.
  Each is one edit; the selection and anchor stay. SPACE's box reads as `parseFloat` reads it.
- **Reorder** (`DesignDocument.Stack`): `ReorderAction._performReorder` with `ReorderCommand`'s moves,
  among the siblings of the selection's kind - menus, axes, toolbars, or the rest but context menus -
  a run of them keeping its gaps to the front or the back. The order is the order the code makes the
  components in (the later in front), so it is the code's order that changes; the runtime restacks
  with `uistack`. Not the figure, a tab, a menu, a toolbar or its tools, a responsive app's region,
  or a group (their models refuse `CanStack`).
- **Tab Order** (`ApplyAutoTabOrder`, `TabOrder`): `AutoTabOrderCommand` - in each container, the
  components that take part (`doesParticipateInAutoTabOrder`: not menus, context menus, toolbars and
  their tools, axes, tree nodes or the figure) laid out by App Designer's gridifier (DS4's
  `DesignGridify`, with `createGridifyParametersByDefaultValues([])`: no padding, spacing 10), read
  row by row and left to right and put after the children that do not take part, before the context
  menus; CANVAS > Tab Order does every container and keeps the selection, the canvas menu's one
  container and selects it. A container the gridifier cannot lay out (one in a tab not shown, of no
  size) is left as it is. Visualize Tab Order outlines each component that takes focus as the app
  runs (`isTabFocusableAtRuntime`), 500 ms each - after Apply from the canvas menu the first six
  (`MAX_TIME_TO_ANIMATE`) - a group's tabs but the one shown and the menus but the top ones left out;
  Escape stops it.
- **Add Label** (Ctrl+L and the canvas menu, `AddLabelToComponentCommand`): for a component whose
  library entry has a label, not one already labelled: the label a new labelled component of its
  kind would have - named by the entry's naming function (so an Edit Field named `EditField` gets
  `EditField_2Label`, text "Edit Field"), aligned and sized as the entry's - put where the entry's
  calculator puts it beside the component's outer box, the component not moved (`westmiddle` and
  `westtop` left of it, `southcenter` under it, with `CalculateLabeledCompDefaultsFromUnlabeled`'s
  shift); inserted just before the sibling made before the component; grouped with it as a labelled
  component, the group it was in holding the new one in its place. In a grid, the cell left of it or
  under it, refused with R2025b's "Unable to Add Label" alert when the component is in the first
  column or the last row.
- **The font keys** (Ctrl+B, Ctrl+I, Ctrl+], Ctrl+[, `FontCommand`): the font is read from the anchor
  (a group's last component with a font) or else the first selected with one; bold or italic turned
  the other way, the size stepped through R2025b's sizes (8 to 72); set on every selected component,
  every group's, and everything inside them that has a font (`CanChangeFont`: not images, lamps, tab
  groups, tabs, menus, grids and the like). One edit; the selection stays.
- **The figure's edges** (`DesignGestures.FigureEdgeAt`, `DesignDocument.ResizeFigure`): the
  `ad-figure-edge` zones, 12 pixels deep and 8 outside - right, bottom, left, the bottom-left corner
  in two pieces and a 23-pixel bottom-right corner on top - resize the figure by
  `FigureResizeCalculator` (R2025b never runs it with a kept aspect ratio): no smaller than 10 by 10,
  the left edge no further left than the room the canvas shows less 2 pixels. One edit of the
  figure's `Position` size; what it holds keeps its positions, from the bottom-left corner, as
  `FigureResizeCommand` sets the size alone. While the left edge is dragged the canvas shifts the
  figure with it.
- **The optional entry**: a labelled library entry dragged or drawn with Ctrl held makes its
  component alone, named and placed as an entry without a label (`ComponentEntryModel`; in a grid,
  in the cell the pointer is in).
- **The canvas's keys** are `DesignAreaKeyboardController`'s, bound on the canvas: Ctrl+Alt+1 to 6
  (and the keypad's) align; Ctrl+Shift+Home, Up, Down and End bring to front, forward, backward and
  send to back; Ctrl+L, Ctrl+B, Ctrl+I, Ctrl+] and Ctrl+[. The actions give the canvas the keyboard
  back after they run (`_requestCanvasFocus`).
- **The canvas's menu** follows `DesignAreaContextMenuController`: Add Label first; then, for
  anything but the figure, `LayoutMenuItemsProvider`'s Align, Same Size, Group (the grouping
  actions, which were flat items) and Reorder submenus, each enabled when any of its items is; Apply
  Auto Tab Order and Visualize Tab Order with Apply Grid Layout, for one container.
- **The window**: each arrange action is a command of its own, which the toolstrip's controls and
  the canvas's keys reach; the Reorder and Tab Order drop-downs and SPACE's box are enabled by the
  selection. `DesignActions` runs 74 of the 202 actions.

## Consequences

- W5's internal reorder (tabs, menus, tree nodes: `APPDES_DESIGN_REORDER_*`, the same keys through
  `ArrowKeyMoveReorderPlugin`) goes beside `Stack`.
- W6's zoom scales the figure's edges and passes its zoom to the auto tab order's gridifier, which
  reads zoomed boxes in R2025b; W6's Resizing Hints shows the figure's size while an edge is dragged.
- W7's component browser takes the tab order view (`TabOrderComponentTreeDisplayStrategy`, the plan
  had it in W4) and the browser's Reorder submenu (`ComponentReorderMenuItemProvider`, which R2025b
  puts there, not on the canvas).

## Measured

- **R2025b's own modules** (`ui-probes/w4/oracle/arrange.js`, node): `Alignment`, `SameSize` and
  `Spacing`, unmodified from R2025b's bundle, over 3,600 seeded scenes (shared edges and fractional
  sizes, fixed sides, kept ratios, least sizes, shuffled stack orders, unknown kinds, negative and
  zero gaps); `DesignArrangeOracleTests` (3) replays every scene to the bit. Same Size's ratio
  branches add the change to a side rather than assigning it, which differs in the last bit; that
  was found by the oracle.
- **Tests**: `AppDesignerArrangeTests` (16): the anchor and a right click; align on the anchor as
  one undoable edit with the selection and anchor kept, `Math.round`'s halves, another anchor; what
  align refuses (one, the figure, a grid's children); a group aligned as one box; Same Size keeping
  the top edge and a slider's height; Space evenly and by "20 px", parseFloat's refusals; Reorder's
  moves, the code's order, a run keeping its gaps; Apply Auto Tab Order row by row (the toolstrip's
  keeping the selection, the menu's selecting its container) and the tab order past a label;
  Visualize from the menu, all of them and the six after Apply; Add Label left of an edit field and
  under a knob, grouped, undone; in a grid; the font keys on a selection and what it holds, bold
  back, the sizes stepped, undone; the figure's edge zones, cursor, a resize, the left edge's room,
  the least size, cancel; a labelled entry dropped with Ctrl; the canvas menu's items.
  `AppDesignerGridTests`, `AppDesignerGroupCanvasTests` and `AppDesignerCanvasTests` follow the
  menu's submenus and the figure's left edge. With the designer, App Designer, toolstrip and
  settings suites: 782 pass.
- **The window check** (`run-w4designer.ps1`, UI Automation patterns, JGraph.Application in Release
  opening `w4/W4App.m`, a plain-text app JGraph's writer made): with the figure selected Align,
  Same Size, Reorder and SPACE's box disabled and Tab Order enabled, the box saying "Evenly"; one
  button selected in the component browser enabling Reorder but not Align; Reorder > Bring to Front
  and Save writing the button last; Tab Order > Apply Auto Tab Order and Save writing the components
  row by row, left to right; Visualize Tab Order. All 23 checks pass. W2's `run-w2designer.ps1` (39)
  and W3's `run-w3designer.ps1` (43) still pass.

## Not measured

- **Two or more selected in the window, the canvas's keys and menu, the figure's edges and a Ctrl
  drag**: they take a mouse or keys, which the checks do not synthesize; `AppDesignerArrangeTests`
  drives the same gestures and commands without a window.
- **Add Label's names and places, the auto tab order and the figure's resize in R2025b's own
  window**: read from its modules and, for the arithmetic, checked against them; no R2025b window
  session was run.

## Divergences

- **The font keys step through R2025b's sizes from any size**: R2025b looks the size up in its list,
  so from a size not in it (13) Ctrl+] gives 8 and Ctrl+[ is disabled.
- **Undoing a font key puts back each component's own font**: R2025b's undo sets every one to the
  font the key was read from.
- **A component keeps its size after a font key**: R2025b resizes a label, a button, a check box and
  the other text components to their preferred size (`PreferredSizeProperties`), which JGraph does
  not compute (after a Text edit either).
- **A responsive app's figure is not resized by its edges** (R2025b reflows its regions,
  `LayoutTemplatedFigureResize`; open item 224).
- **Visualize Tab Order draws no arrow under a menu with submenus**
  (`ad-tab-order-animation-menu-with-submenus`).
- **A right click on a member of a selected group makes the group the anchor**: R2025b makes the
  member the anchor, which its alignment then moves with the group.
- **Ctrl+Shift+Home, End, Up and Down do not yet reorder tabs, menus and tree nodes** on the canvas,
  which R2025b's `ArrowKeyMoveReorderPlugin` does with the same keys (W5).
