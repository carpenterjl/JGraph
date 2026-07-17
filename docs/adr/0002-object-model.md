# ADR 0002: Figure object model and invalidation

- Status: Accepted
- Date: 2026-07-15

## Context

Everything inside a figure must be an individually selectable, editable, serializable, extensible
object, arranged like MATLAB's handle graphics: Figure → Axes → plot objects. A surface must know
efficiently when and how much to redraw, and the model must not depend on rendering or UI.

## Decision

1. **A single `GraphObject` base** provides identity (`Id`), the common editable properties the spec
   lists (visibility, z-order, selectability, tag, user data), `INotifyPropertyChanged`, and a
   parent link. `FigureModel`, `AxesModel`, `AxisModel`, `GridModel`, `LegendModel`, and the abstract
   `PlotObject` derive from it.

2. **Invalidation bubbles and is typed.** `GraphObject.Invalidate(kind)` raises an `Invalidated`
   event and propagates it to the parent, preserving the original source. `InvalidationKind` is
   ordered `Render < Layout < Data < Structure` so a consumer can coalesce many changes into the
   most expensive one. A surface subscribes once, at the figure root, and observes the whole subtree.

3. **Containment goes through `GraphObjectCollection<T>`**, which maintains parent links and raises a
   `Structure` invalidation when membership changes.

4. **Concrete plot types live in `JGraph.Objects`, not `JGraph.Core`.** `PlotObject` is an abstract
   base in Core exposing the framework's seams — `GetXDataBounds()`/`GetYDataBounds()` for
   auto-scaling and `HitTest(...)` for selection — while `LinePlot`, `ScatterPlot`, etc. implement
   drawing (`IDrawable`) in the Objects layer. This keeps Core free of rendering and lets plugins add
   plot types by deriving from `PlotObject` and implementing `IDrawable`.

5. **Data bounds and auto-scaling are model operations.** `AxesModel.RecomputeDataBounds()` unions
   each plot's extents per axis and, for auto-scaling axes, fits the visible `Range` (with padding).
   This needs no rendering, so it is fully unit-testable.

## Consequences

- The object-oriented API adds fluent helpers (for example `axes.AddLine`) as extension methods in
  `JGraph.Objects`, since Core cannot reference concrete plot types.
- Because the model carries `INotifyPropertyChanged` and stable ids, the property inspector, plot
  browser, serialization, and undo/redo layers can bind to it directly in later milestones.
- Setting an auto-scaled `Range` to a value equal to the current one raises no invalidation (the
  setter is change-guarded), so the recompute-then-render loop converges instead of repainting
  forever.
