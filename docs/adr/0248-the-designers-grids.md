# ADR 0248 — The designer's grids

## Status

Accepted, 2026-10-19. Stage DS4 of the visual app designer plan (`docs/plans/app-designer-plan.md`,
gitignored), after DS3 (ADR 0247); its responsive apps are DS4b (ADR 0249). App Designer's grid
editing was read from R2025b's own code - its canvas bundle
(`toolbox/matlab/appdesigner/web/release/bundle.mwBundle.appdesigner.js`: the placement validators
and command creators under `appdesigner_interface/behavior/canvas/placement`, `GridUtil`,
`MoveInGridCommand`, the row and column commands, `GridMenuItemProvider`, `GridifyCommand` and
`UngridifyCommand`, the `gridify` modules, the `gridchildresize` gesture, the design-time grid widget
under `uicomponents_appdesigner_plugin/widget/designtimegrid`, its `GridController`, and the runtime
grid's `gbtcomponents/controller/GridController` and `gbtshared` strategies) and its MATLAB side
(`GridLayout.m`, `GridLayoutOptions.m`, the grid's adapter, design-time controller and property
view, `ComponentCodeGenerator`). Nothing of it is kept. Where a rule is arithmetic, App Designer's
own modules were run unmodified under node on the same inputs as the C#, and their answers kept as
test data: the gridifier on 308 scenes, and on 1,136 more of some 258,000 fuzzed ones a second check
ran (`gridify-golden.json`, `gridify-verify*.json*`); the cell, span, menu-point and track rules on
266 cases and 11,340 more (`GridRulesOracle.json`, `GridRulesVerifyOracle.json`). R2025b's App
Designer read two apps saved here (`ds6_verify`, headless, 2026-10-19).

## Context

DS2 drew a grid's children where the runtime's layout put them and left them alone: they could not
be moved, resized or added to, a drop over a grid went into the container behind it, and the Grid
Layout entry of the library was disabled. App Designer edits a grid by its cells: a component goes
into the cells it covers, a handle spans it over more, rows and columns are added, deleted and sized
from a context menu and a configuring mode, and "Apply Grid Layout" lays a container's components
out in a grid it works out from where they stand.

## Decision

### Cells at design time (`UiGridMath`, `UiLayout`, `DesignRuntime.CellsOf`)

The canvas draws a grid's cells from the shared layout: `UiGridArrangement` now keeps where each
row and column begins, and a grid's placement keeps its arrangement, the rectangle its tracks are
laid in and its padding (`GridCells`, built by `DesignRuntime.CellsOf`). The designer's figure is
marked design time (`FigureModel.IsDesignTime`), and its grids lay out as App Designer's design-time
grid does: an empty `'fit'` row or column is 10 pixels, so that it can be seen and dropped into
(`GridController._convertFitToPixels`), and no grid scrolls. A script's figure is unchanged.

A grid's child is App Designer's design-time wrapper of it - its cells' box - for a click, its
selection outline, its handles and a drag; a slider or a gauge is still drawn smaller inside. A
grid's tracks that overflow it are drawn past it, as App Designer draws them at design time.

### Placing in cells (`DesignDocument.PlacementFor`, `PlacementForNew`, `DrawPlacement`, `DesignGridRules`)

App Designer's placement, read from its validators:

- **The containers are tried** deepest first, the front one first, a selected container's own before
  the rest (`PlacementUtil.getCandidateContainers`). A container holding a grid is reached through
  its grid only, unless the drop comes from it. A panel, tab or button group holds a dragged box
  wholly inside it - or, for the container it comes from, touching it (`BoundsPositionValidator`) -
  and a library drop by the pointer (`MousePositionValidator`); one that holds a drop it cannot take
  refuses it there (`RestrictedParentValidator`): a Grid Layout on a button group is refused, not
  passed on to lay out the figure. DS2 and DS3 went by the box's centre and walked up from what was
  under the pointer.
- **A grid takes the cells a box covers** (`CellBoundsValidator`): each edge of the box belongs to the
  cell whose box, widened by half the spacing either side, holds it; a cell counts when the box
  covers its centre, or the two cells either side of a gap when the box straddles it. Occupied cells
  are taken like empty ones: App Designer never refuses, swaps or shifts on a drop.
- **A grid holds the pointer over it** (`GridController._postValidation`): a drop it refuses - several
  components from a container that places by position (`SelectionValidator`), a radio button, a box
  that covers no cell - stops there and changes nothing, rather than falling through to the panel
  behind; and it refuses, wherever the pointer is, what covers its cells but cannot go in them.
- **A labelled entry** (`LabeledComponentEntryDefinition`): its label and component take cells beside
  each other by where they stand; the component's cells are the drop's, the label's are beside them
  (left for a west label, below for a south one); with no room for the label it is refused.
- **Click and draw** (`ClickAndDrawAlignmentHintsGestureManager`, `CellMousePositionValidator`): a press
  picks the cell under the pointer - none in a gap or the padding - and a drag spans over cells by
  the span resize's snapping; a labelled entry's label and component as `DrawnLabeledSpans` gives them.
- **What is written** is `Layout.Row` and `Layout.Column`, a number or `[a b]`, and no position. A
  component dragged into a grid has its position unset; dragged out of one into a container that
  places by position, it is placed where it was dropped at the size it had and its cells unset
  (`AbsoluteLayoutCreator`); grid to grid, it takes the new cells. A drag that would leave any of its
  components in the cells they are in does nothing (`GridLayoutCommandCreator`). Each is one edit.

### Spans (`GridSpanResize`, `DesignGestures`)

A grid's child has the handles it would have elsewhere, on its cells' edges, and a grid in a grid
has eight; a grid in any other container fills it and has none. A component whose shape keeps its
ratio - a knob, a lamp, a round gauge, a switch - has its four corners only, in a grid or out of one
(`AbstractDesignTimeController._attachResizeHandles`); DS2 gave it eight. A handle spans the child over cells
(`gridchildresize`: `SnappingDataBuilder`, `SnapIndexCalculator`, `GridChildResizeHelpers`): the
dragged edge follows the pointer, kept within the grid and to at least one track, and snaps to the
next cell's edge of the same kind once past the midpoint between the two; release sets the cells as
one edit. Only the component whose handle was taken spans. The arrow keys move no grid child, but
take the key (`ArrowKeyMoveReorderPlugin`).

### Rows and columns (`InsertTrack`, `DeleteTrack`, `SetTrack`)

- **Inserted** (`AddRowCommand`, `AddColumnCommand`): always `'1x'`; the children at it and after it move
  along, and a child spanning across it grows.
- **Deleted** (`DeleteRowCommand`, `DeleteColumnCommand`): the children in it alone are deleted, a child
  spanning it shrinks, and the rest move back; the grid stays selected.
- **Set** (the configuring popup's `row-change`, `column-change`): one track replaced, the rest as they
  are. A grid's tracks are read from its component, so a row a child reaches past the ones set -
  MATLAB's implicit `'1x'` - counts.
- **In the inspector** a grid's `RowHeight` and `ColumnWidth` also take App Designer's comma form
  (`fit, 2x, 40`), and fewer tracks than its children reach are refused (`GridUtil.isNotEnoughSpaceOnGrid`).

### The context menu and configuring (`DesignGestures.MenuAt`, `DesignGridConfigure`)

The canvas has a context menu. For one grid selected it has App Designer's grid items
(`GridMenuItemProvider`): Configure Grid Layout; Add Row above or below and Add Column left or right
of the cell clicked - into the gap clicked, or before the first or after the last in the padding;
Delete Row and Delete Column for the cell clicked; Remove Grid Layout. Each item that adds or deletes
shows the row or column it would, while pointed at. Then Delete, and Apply Grid Layout for a figure,
panel or tab.

A grid is configured from the menu, by a double click with it selected, or by the button at its
top-left corner (App Designer's grid edit mode): its rows and columns become bands, rows over
columns until the pointer picks columns out by their tabs above or below the grid; a click picks a
band and opens the popup, which shows its kind - Fit, Weighted with its weight, Fixed with its pixels
- and inserts a row or column either side or deletes it. Switched to fixed, a track takes what it is
drawn at, rounded; switched to weighted, the weight last shown (100 before any). A weight is from 0 to
100. Deleting a grid's last row or column deletes the grid. Escape, the button, a press outside the
grid and its bands, or any change of the selection ends it. Only the primary button picks a band.

Which grids show their cells follows the selection (`SelectableController._visualize`): a selected
grid and the nearest grid around a selected component show their cells and the configure button,
grids further out and grids inside one showing its button their cells; with the figure selected,
every grid not in a grid shows its button. During a drag every grid shows its cells, and the cells a
drop would take are filled.

### Applying and removing a grid layout (`Gridify`, `Ungridify`, `DesignGridify`)

- **Apply Grid Layout** (`GridifyCommand`), on a figure, panel or tab, and a Grid Layout dropped from the
  library on one: a new grid fills it, and the components in view - each whose centre is inside it,
  or that spans it, across and down - go into the cells App Designer's gridifier
  (`AppDesignerGridifier`, transcribed into `DesignGridify` with every quirk that changes its output)
  works out from where they stand, with its rows, columns, padding and spacing. A component that does
  not grow - a button, a label - holds its row or column fixed in pixels; the rest are weighted. With
  nothing in view, the grid has as many `'1x'` rows and columns as 50-by-200-pixel cells fill it.
- **A Grid Layout dropped on a grid** makes a grid in the cells its avatar (204 by 141) covers, sized
  by the same empty rule for them. A Grid Layout is not drawn (its model's `CanDraw` is false); a
  click places it.
- **Remove Grid Layout** (`UngridifyCommand`): every component in the grid, and in the grids inside it,
  moves to the grid's container where it is drawn, at its size - or, into a grid, takes the cells the
  grid had - and the grids are deleted; the grid's container is selected when it is a grid, else the
  figure, as App Designer's delete of the grid leaves it.

Each is one edit.

### Undo

A step that moves or deletes several children of one container puts each back where it was when it
moved: a child's place is recorded as the edit runs, since undo runs a step's edits last to first.
A component taken into a grid has its position put back last, so that an axes the grid pinned to its
cell is where it was. A grid's tracks undo to what the design held, not to the implicit `'1x'` rows
its children reached; a child deleted with its row is made again in the cells it was in.

### The inspector

A grid's child shows its `Position` and does not edit it (`DisabledPropertiesInGrid`).

## Measured

- `AppDesignerGridTests` (25 tests): an empty figure's grid and its cells; a 10-pixel empty fit row;
  entries dropped on cells, across a gap, a labelled one, and one refused; drawing a span and a press in
  a gap; a child dragged to other cells, back over its own, and the arrows; components into and out of
  grids, and undo; several refused; a span by a handle; rows inserted and deleted with children
  moving, spanning and deleted, and undo; Apply and Remove Grid Layout on three buttons (App
  Designer's gridify sample A, line for line); a grid nested in cells; the context menu by where the
  click is; configuring, from the figure's button, ended by the selection, with no columns, and after
  an undo; which grids show; the order undo puts moved children back in; an axes laid out and undone;
  a button group refusing a Grid Layout; a panel in a cell taking only what is wholly inside it; a
  knob dragged out as it is drawn, with corner handles; the comma form; the inspector; every shipped
  grid drawn with its children in their cells (121 grids, 396 children); a shipped app's grids edited
  and saved; an app with grids built on the canvas, saved and opened again.
- The gridifier and the grid rules against App Designer's own code (`AppDesignerGridifyTests`,
  `AppDesignerGridifyVerifyTests`, `AppDesignerGridRulesTests`, `AppDesignerGridRulesVerifyTests`):
  every case exact. With the canvas tests, 349 cases.
- A review of the integration by readers of App Designer's specs, each finding checked by another,
  confirmed 30 defects - among them an axes left at its cell by an undo, children put back out of
  order, configuring that would not start from the figure's button and came back unasked, a crash
  configuring a grid with no columns, the popup going stale after an undo - all fixed and tested.
- The full suite: 11,865 passed. The seven verify scripts pass.
- **R2025b's App Designer reading apps saved here** (`ds6_verify`, `DS6_PART=new`, headless): an app with
  grids built on the canvas - a panel's components laid out in a grid, a grid nested in a cell, a
  span, a row inserted, a column fixed - opens the same as the tree its code makes (`new GridApp: same`,
  15 components). A shipped app (`GUIDEAppMaintenanceOptions`) with a row inserted in each of its four
  grids, children spanned and a row deleted, saved over App Designer's own tree, differs from the tree
  its code makes in the same 68 places, and only those, as the file R2025b ships does - App Designer's
  own saved form of components the edit did not touch (modes saved manual, a text kept as a cell):
  nothing the edit wrote differs.
- **The window** (the Release app started with `-r "appdesigner(...)"` on a copy of the app built with
  grids, UI Automation patterns only, captured with PrintWindow, ended by Stop-Process; the app's file
  and `workspace.json` unchanged): the library's Grid Layout enabled; a grid selected in the browser
  showing App Designer's Grid Layout group, its cells dashed and its configure button; its columns set
  in the comma form; too few rows refused with the reason; a grid child's Position shown and not
  edited; the toolbar's Delete and Undo.

## Not measured

- The mouse in the real window: placing, dragging, spanning, the context menu and configuring are
  tested through `DesignGestures` and `DesignGridConfigure`, which the canvas only feeds.
- A grid's pixels against App Designer's canvas: the cells are the shared layout's, which differs from
  R2025b's own grid for some `'fit'` tracks (open item 153).

## Consequences

- `JGraph.Scripting.Designer` has `DesignGridRules` (`GridSpan`, `GridCells`, `GridSpanResize`),
  `DesignGridify`, `DesignGridConfigure` and `DesignDocument`'s grid edits; `DesignGestures` has the
  context menu, configuring and the grid marks; `JGraph.Controls.Designer` has the configuring popup.
- `UiGridArrangement` keeps its track starts and `UiPanelPlacement` a grid's arrangement; a figure can be
  marked design time. `DesignDocument.ShowTab` shows a tab and works the boxes out again (DS2 left them
  stale).
- The Grid Layout entry of the library is enabled.
- No running script behaves differently.

## Divergences

- **An edge on a gap's midline belongs to one cell**: App Designer leaves an edge exactly half a spacing
  past a cell in neither, which gives a span one cell too large, or one reversed (`Column [2 1]`) that
  its own grid then refuses.
- **The bottom and right padding add a row or column after the last**: App Designer tests a click there
  against the last row's top and the last column's left, so Add Row and Add Column are disabled in it.
- **Add Row on a grid with no rows and no columns adds one**: App Designer's click data has no row there,
  and its handler throws.
- **A track the gridifier works out below nothing is nothing**: App Designer's gridifier gives a negative
  pixel track for components hanging far over their container's edge, which MATLAB refuses.
- **A component dragged in or out of a grid moves as itself only where it is placed by position**: a
  grid's child stays in its cells while a dashed box shows where it is dragged, and a span resize
  shows the dragged box; App Designer moves the component itself, half transparent for a span.
- **A span resize that ends in the cells it began in is no edit**: App Designer records an empty step.
- **A labelled component spans and moves in a grid without its label** (open item 150): App Designer
  keeps the two as a group.
- **Remove Grid Layout places each component where it is drawn**: App Designer adds the grid's whole
  `Position` to each one's, which, if a grid child's position counts from 1, puts it a pixel right and
  up for each grid.
- **A labelled entry drawn in a grid with no room for its label is refused**: App Designer moves both
  cells back one and puts the label in column 0 or row 0, which MATLAB refuses.
- **A grid being configured is veiled**, where App Designer greys its background and draws its
  components three-quarters opaque; the bands and the add and delete hints are flat, not striped;
  the popup's buttons are words.

## Still open

- Responsive apps (DS4b, ADR 0249).
- The runtime's `'fit'` arithmetic against R2025b's grid (open item 153).
- Paste, duplicate and Ctrl-drag copy into a grid, and Add Label in a grid (open item 151).
- A labelled pair spanned and moved together in a grid (open item 150).
