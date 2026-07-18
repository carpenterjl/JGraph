# ADR 0025 — Pointer mode, persistent data tips, and the plot context menu

## Status

Accepted (M21, 2026-07-18).

## Context

The figure window's default tool was a modal Pan; the data cursor was a transient single-instance
overlay cleared on every mode switch; there was no right-click menu on the plot surface and no way
to constrain a rectangle zoom to one axis. The goal was MATLAB-grade quality of life: a default
pointer that pans, shows a crosshair over data, and pins persistent point labels; a roving data-tip
tool; and a tool-aware context menu.

## Decision

1. **`DataTipAnnotation` is a model object, not an overlay.** It lives in `axes.Annotations`
   (drawn by the normal annotation pipeline), stores the **pinned data coordinates** — not a plot
   reference, since plots have no stable identity in the `.graph` format — plus an informational
   source-series name and point index. Its single anchor point is the **label position**, so the
   whole existing drag/undo machinery (`MoveAnnotationAction`, `SetAnchorPoints`) moves the label
   while the pin never leaves the data point. It renders a marker at the pin, a leader line, and a
   label box (custom `Text` or the coordinates), serializes as the `"datatip"` discriminator
   (format version 3), and edits in the inspector (`Pinned X`/`Pinned Y`, text, appearance). If the
   underlying data changes, the tip stays at the captured coordinates — predictable, documented.
2. **`PointerMode` is the default.** Down-then-move beyond 4 px starts the shared
   `PanDragGesture` (extracted from `PanMode`, which remains for API compatibility): pan on 2D,
   camera rotate on 3D. A sub-threshold click on a data point (14 px tolerance) places a persistent
   tip via the new `AddAnnotationAction`; clicking a tip's label selects it and dragging moves it.
   The mode's `Cursor` is dynamic — arrow, crosshair near a pickable point or tip, hand while
   dragging — surfaced through a new `InteractionController.NotifyCursorChanged()` on the existing
   `StateChanged`/`UpdateCursor` path.
3. **`DataTipsMode` replaces the transient data cursor.** Each click places the same annotation
   but replaces the tip *this tool* placed last (a roving readout), as a single composite undo
   action; pointer-placed tips are never touched. The old `DataCursorInfo` overlay,
   `controller.DataCursor`, and the OverlayRenderer branch were deleted. The toolbar's radios are
   now Pointer / Zoom / Data Tips / Edit (the Pan radio was dropped; wheel zoom and the pointer
   cover it).
4. **The context menu is a UI-free model.** `InteractionController.BuildContextMenu(pixel)`
   assembles `ContextMenuItem` records: the active mode's contributions via `IContextMenuSource`
   (the zoom mode's three checkable constraint choices), the data-tip entries available in every
   mode ("Delete This Data Tip" when the pixel hits one, "Delete All Data Tips" when any exist —
   both undoable), and always "Restore View", which resets the axes *under the pointer*.
   `FigureControl` opens a WPF `ContextMenu` from the model on a right click that moved less than
   4 px. Menu contents are unit-tested headlessly.
5. **Constrained zoom.** `RectangleZoomMode.Constraint` (Unconstrained/Horizontal/Vertical, set
   from the menu) makes the rubber band span the plot's full height/width — what you see is exactly
   what zooms — and restores the free axis' range bit-for-bit after `ZoomToRect`, so a constrained
   zoom can never drift the other axis. The minimum-drag check applies only to the constrained
   dimension.

## Consequences

- Data tips persist across mode switches, undo/redo, and `.graph` save/load ­— and appear in the
  plot browser and inspector like any annotation.
- "Delete All Data Tips" pushes one undo action per tip (back-to-front, so indices re-insert
  correctly); a composite action was not worth a new undo primitive.
- Headless tests cover click-vs-drag thresholds, hover cursor transitions, placement/undo,
  replace-last semantics, constrained zoom exactness, and menu contents; the label-drag path
  (which needs rendered bounds) is exercised in the live check.
