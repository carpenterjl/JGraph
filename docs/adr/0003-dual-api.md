# ADR 0003: Supporting both a MATLAB-like and an object-oriented API

- Status: Accepted
- Date: 2026-07-15

## Context

The framework must support two API styles equally: a MATLAB-like functional API
(`Plot(x, y, "r--")`, `Title(...)`, `Grid(true)`) and an object-oriented API
(`figure.AddAxes()`, `axes.AddLine(x, y)`, `line.Color = ...`). Both must manipulate the same object
model so a figure built one way is indistinguishable from one built the other way.

## Decision

1. **The object model is the single source of truth.** Both APIs create and mutate
   `FigureModel`/`AxesModel`/`PlotObject` instances directly.

2. **Object-oriented API = fluent extension methods.** `AddLine`, `AddScatter`, and `AddBar` are
   extension methods on `AxesModel` in `JGraph.Objects` (Core cannot depend on the concrete plot
   types). Property editing is just setting properties on the returned plot object.

3. **MATLAB-like API = a stateful static facade.** `JG` (in `JGraph.Api`) keeps an implicit "current
   figure" and "current axes" and a hold flag, mirroring `gcf`/`gca`/`hold`. Each call routes to the
   same object model and the same extension methods. A `LineSpec.Parse` turns strings like `"r--o"`
   into color/dash/marker, applying MATLAB semantics (a marker with no line style draws markers
   only).

4. **The facade stays UI-free.** `JG` builds models but never opens a window; it raises a
   `FigureShown` event so a WPF host can display `JG.CurrentFigure`. This keeps `JGraph.Api` free of
   any WPF dependency and testable headlessly.

## Consequences

- The two styles interoperate: code can start a figure with `JG.Plot` and then reach into
  `JG.CurrentFigure` to fine-tune objects, or vice versa.
- `JG`'s static current-figure state is single-threaded by design (matching MATLAB and the WPF UI
  thread); a `Reset()` method supports deterministic tests.
- Because both APIs funnel through the model, later features (serialization, undo, property
  inspector) work identically regardless of which API created the figure.
