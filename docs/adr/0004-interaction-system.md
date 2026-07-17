# ADR 0004: Interaction system and navigation undo/redo

- Status: Accepted
- Date: 2026-07-15

## Context

The figure window needs Matlab-like interaction — mouse-wheel zoom, drag pan, rubber-band zoom, a
data cursor, and undo/redo of navigation — and the requirements state the interaction system must be
modular, extensible, and kept independent of rendering. It must also not couple to WPF, so the same
logic can back other hosts later.

## Decision

1. **Input is abstracted.** `JGraph.Interaction` defines its own `PointerEventArgs`, `WheelEventArgs`,
   and `KeyEventArgs` (with a `ModifierKeys` flag) — no WPF types. The WPF `FigureControl` translates
   real mouse/keyboard events into these and forwards them.

2. **Modes are pluggable.** `IInteractionMode` (with `PanMode`, `RectangleZoomMode`, `DataCursorMode`)
   encapsulates one behavior. `InteractionController` holds the active mode, forwards events, and owns
   behavior common to all modes (wheel zoom about the cursor). New modes (crosshair, box select,
   3D orbit) are added without touching existing ones.

3. **The host is abstracted behind `IInteractionSurface`.** It exposes the per-axes geometry from the
   last paint (`TryGetAxesAt` → mapper + plot rectangle), the shared `UndoStack`, and a repaint
   request. The interaction layer therefore depends only on `Core` and `Math`, never on rendering or
   WPF. The renderer records this geometry in a `FigureRenderResult` that the control keeps.

4. **Navigation math is pure and scale-correct.** `Navigation` computes zoom/pan/zoom-to-rect in the
   axis' forward (scale) space, so it is correct for linear, logarithmic, and inverted axes and is
   fully unit-testable without a UI.

5. **Undo is snapshot-based.** `UndoStack` (in `Core`) stores `IUndoableAction`s. Navigation gestures
   capture an `AxesViewState` (all axis ranges + auto-scale flags) before and after and push a single
   `AxesViewChangeAction`, so a whole gesture undoes atomically. This same stack will carry property
   edits and object moves in later milestones.

## Consequences

- Rendering and interaction communicate only through the geometry snapshot and the model; neither
  reads the other's internals, satisfying the "rendering independent of interaction" requirement.
- The MVVM shell binds toolbar commands to the control through a small `IFigureNavigator` seam, so the
  view model never touches interaction internals.
- Because the surface abstraction and modes are UI-free, a future non-WPF host (or headless
  automation) reuses the entire interaction layer unchanged.
