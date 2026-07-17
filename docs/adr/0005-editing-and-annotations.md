# ADR 0005: Editing, selection, and annotations

- Status: Accepted
- Date: 2026-07-15

## Context

Milestone 4 adds the "figure window you can edit": annotations (text, arrows, shapes), click
selection, drag-moving, a property inspector, and a plot browser. The requirements constrain the
design in three ways: undo must cover property edits and object moves (but not plot creation or
removal), the editing logic must stay UI-independent like the rest of the interaction layer, and
rendering must remain a pure model → `IRenderContext` pipeline with no knowledge of editing state.

## Decision

1. **Annotations are anchor-point objects in two coordinate spaces.** The abstract
   `AnnotationObject` (Core) exposes its geometry as an ordered list of anchor points via
   `GetAnchorPoints`/`SetAnchorPoints`, in either data coordinates (`AxesModel.Annotations`, clipped
   to the plot, following zoom/pan) or normalized [0, 1] figure coordinates
   (`FigureModel.Annotations`, drawn last, pinned to the window; (0, 0) = top-left, matching
   `NormalizedBounds`). The uniform anchor list makes moving, undo snapshots, and scale-correct
   translation (`ShiftByPixels` maps anchor → pixel → shifted pixel → anchor, exact for log and
   inverted axes) identical across annotation types. Concrete types (`TextAnnotation`,
   `ArrowAnnotation`, `RectangleAnnotation`, `EllipseAnnotation`) live in `JGraph.Objects` and
   implement `IDrawable`, exactly like plots. Annotations never influence auto-scaling.

2. **Hit geometry comes from the last paint.** Each annotation records its device-space
   `RenderedBounds` while rendering; selection and dragging read those (arrows refine with a
   distance-to-segment test). This extends the existing pattern — interaction always consumes the
   previous paint's geometry (`FigureRenderResult`, now also carrying the figure-space
   `NormalizedCoordinateMapper` via `IInteractionSurface.FigureMapper`) — and avoids duplicating
   text-measurement in a UI-free layer.

3. **Selection is a shared, single-object `SelectionManager`.** Owned by the
   `InteractionController`; the new `EditMode` (click → annotations, then plots, then the axes),
   the plot browser tree, and the property inspector all read and write the same instance, and it
   keeps `GraphObject.IsSelected` flags in sync. The selection highlight (dashed rectangle + corner
   handles) is drawn by the control's overlay renderer, not by the objects, so rendering stays
   editing-agnostic. Multi-select is deferred.

4. **Edits are undoable actions on the existing stack.** `PropertyChangeAction` (reflection-based
   old/new value swap, with `IMergeableAction`/`UndoStack.PushOrMerge` for coalescing continuous
   gestures), `MoveAnnotationAction` (before/after anchor snapshot, one per drag), and
   `RemoveAnnotationAction` (undo re-inserts at the original index). Per the original requirements,
   plot deletion is a plain confirmed action and plot creation is never undoable.

5. **The property inspector is reflection over ComponentModel attributes.** A UI-free descriptor
   layer (`EditablePropertyFactory` in `JGraph.Interaction.Editing`) reflects public read/write
   properties, honoring `[Browsable(false)]`, `[Category]`, and `[DisplayName]` (BCL attributes —
   no new dependencies), maps property types to editor kinds (number, toggle, enum, color,
   optional color, range), and owns culture-aware parsing/formatting — all unit-testable without
   WPF. The WPF `PropertyInspectorControl` is a thin template-selector view over those
   descriptors. Unsupported property types are simply not shown rather than shown read-only.

## Consequences

- New annotation types follow the same recipe as plots: derive `AnnotationObject`, implement
  `IDrawable`, add fluent/`JG` helpers — and they get selection, dragging, undo, browser and
  inspector support for free through the anchor-point and reflection contracts.
- Any object made editable in the inspector needs only ComponentModel attributes on its properties;
  the inspector, undo, and refresh behavior require no per-type code.
- Because hit geometry is paint-derived, hidden or never-painted annotations are not selectable, and
  hit-testing is exact for whatever the user actually sees.
- The dual-space design fixes the MATLAB annotation dichotomy explicitly: data annotations zoom
  with the plot and clip at its edge; figure annotations never move.
