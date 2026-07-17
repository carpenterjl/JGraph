# ADR 0006: Export pipeline

- Status: Accepted
- Date: 2026-07-15

## Context

Milestone 5 adds figure export: raster (PNG/JPEG/BMP/TIFF), vector (SVG/PDF), and clipboard copy,
plus an export dialog in the figure window. The requirements demand publication-quality output and
consistency with the screen, and ADR 0001 promised that new output targets would be new canvases
behind the same rendering seam — not parallel drawing code.

## Decision

1. **One renderer, many canvases.** `JGraph.Export` (net8.0, no WPF) runs the same
   `FigureRenderer` used on screen against different Skia canvases: an `SKBitmap` canvas for raster
   formats, `SKSvgCanvas` for SVG, and an `SKDocument` PDF page for PDF. No new `IRenderContext`
   implementation was needed — `SkiaRenderContext` already wraps any `SKCanvas` — which validates
   the ADR 0001 seam. Vector output therefore contains real paths and text, never embedded bitmaps.
   The project sits beside `JGraph.Rendering.Skia` in the layer diagram as part of the Skia backend
   family (it is allowed to reference SkiaSharp; the model/renderer layers still are not).

2. **Sizes are device-independent units end to end.** `ExportOptions.Size` uses the same 1/96-inch
   units as on-screen layout, so exporting at the viewport size reproduces exactly what the user
   sees. Raster `Scale` supersamples pixels (2× ≈ 192 DPI) without touching layout, fonts, or line
   widths. PDF pages are created in points with a 72/96 canvas scale, so the figure's physical
   print size is exact.

3. **Themes style chrome, not the model — for exports too.** `ExportOptions.Theme` supplies chrome
   defaults exactly like `FigureRenderer`'s theme parameter; it never restyles the model. Callers
   re-theming an export apply the theme to the figure first, the same contract as on screen.

4. **BMP and TIFF writers are hand-rolled.** Skia ships no BMP or TIFF encoder (its `Encode`
   returns null for them). Rather than pulling in an imaging dependency or making the export
   library Windows-only (WPF's encoders), JGraph writes them directly: 32-bit uncompressed BMP and
   baseline (frozen-spec, universally readable) little-endian RGB TIFF. Both are trivial fixed
   layouts, unit-tested by decoding (BMP round-trips through Skia's decoder) and structural
   parsing (TIFF IFD).

5. **Dashed strokes are flattened for SVG.** Skia's SVG backend silently drops dash path effects.
   `SkiaRenderContext` gained an opt-in `flattenDashes` mode that chops dashed strokes into
   explicit segments via `SKPathMeasure`; only the SVG exporter enables it, so raster and PDF keep
   the faster path effect. This was caught by rendering exports during verification, and the
   behavior is pinned by a test.

6. **Clipboard and the dialog live in the WPF layers.** `FigureClipboard` (in `JGraph.Controls`)
   exports PNG bytes through `FigureExporter` and hands them to the Windows clipboard, so clipboard
   images are pixel-identical to file exports; `FigureControl` binds Ctrl+C. The save dialog is an
   `IFigureExportService` behind DI in `JGraph.Application`, keeping the view model WPF-free.
   Copying the figure as an editable object (not an image) arrives with the serialization
   milestone.

## Consequences

- A future GPU/alternative backend changes nothing here as long as it renders through
  `IRenderContext`; a non-Skia vector writer (e.g., hand-written SVG) would slot in as another
  branch of `FigureExporter` without touching the model or renderer.
- Headless/server-side export works on any .NET 8 platform — `JGraph.Export` has no UI dependency.
- Exports have no side effects on the figure model (asserted by test), so exporting never perturbs
  an interactive session's undo history or view state.
