# ADR 0009: The versioned ".graph" document format

- Status: Accepted
- Date: 2026-07-16

## Context

Milestone 8 makes figures persistent: a figure can be saved to a file, reopened, and copied/pasted as
an editable object graph (not just as an image, which Milestone 5 already covered). This needs a
serialization format for the whole `FigureModel` tree — figures, axes, scales, every plot type, the
grid, the legend, and annotations — that is stable across releases and does not entangle the model with
serialization concerns.

## Decision

1. **A new leaf project, `JGraph.Serialization`, owns persistence.** It references `JGraph.Objects`
   (so it can see every concrete plot and annotation) and uses `System.Text.Json` from the BCL — no new
   package dependency. `JGraph.Controls` and `JGraph.Application` reference it for clipboard and file
   I/O. The model layers (`Core`, `Objects`) gain nothing.

2. **The format is versioned JSON with an explicit DTO layer.** A document is
   `{ "format": "jgraph", "formatVersion": 1, "figure": { … } }`. Rather than annotate the model types
   or serialize them directly, an explicit set of DTO records mirrors the model, and a mapper converts
   between them. This is the same discipline the framework uses elsewhere (the renderer is a seam, the
   exporter never mutates the model): the on-disk format is a deliberate contract, decoupled from
   internal property names, so refactoring the model does not silently break saved files. `GraphFormat`
   is the single entry point (`Serialize`/`Deserialize`/`Save`/`Load`); it rejects a wrong format tag, a
   newer `formatVersion`, and malformed or inconsistent content with a `GraphFormatException`.

3. **Polymorphism is handled by a type discriminator on the DTOs.** Plots and annotations are
   heterogeneous, so their DTOs form a small hierarchy under `PlotDto`/`AnnotationDto` with a
   `System.Text.Json` polymorphic `type` discriminator (`"line"`, `"bar"`, `"image"`, `"polarGrid"`,
   `"text"`, `"arrow"`, …). Adding a plot or annotation type is a new `[JsonDerivedType]` line plus one
   arm in the mapper — the extension point the plugin milestone will build on.

4. **Values are written in their natural human-readable form.** Colors serialize as hex strings
   (`"#FF0000"`), enums as their names, geometry as small `{x, y}` / `{min, max}` objects, and 2-D image
   fields as nested arrays. Non-finite data (`NaN`/`Infinity`, used for line gaps) is preserved via
   named-literal number handling, and non-ASCII text (axis units such as "²") is left unescaped so the
   file reads cleanly. The result is a document a human can inspect and hand-edit.

5. **Copy/paste reuses the same format.** `FigureClipboard` (in `JGraph.Controls`) places a figure on
   the clipboard both as a PNG image (for other applications) and as ".graph" JSON under a private
   clipboard format, and reads that private format back. The figure window gains Open/Save and Copy
   Figure/Paste Figure commands over an `IFigureDocumentService`, keeping the view model free of WPF.

## Consequences

- Figures are now durable and interchangeable, and the format is a stable, versioned contract: older
  builds refuse newer documents rather than mis-parsing them, and future schema changes bump
  `formatVersion` with a documented migration.
- The DTO layer is boilerplate that must be extended alongside the model — the cost of keeping the model
  clean. The `type`-discriminator structure makes each addition local and mechanical, and the round-trip
  tests catch any omission.
- A few model types gained read-only accessors purely for serialization (`HistogramPlot.Values`,
  `ErrorBarPlot.ErrorNeg`/`ErrorPos`); these expose already-owned data and change no behavior.
- Only array-backed data series are persisted (rebuilt as `ArrayDataSeries` on load); a live or computed
  series is materialized to its samples, which is the correct behavior for a saved snapshot.
