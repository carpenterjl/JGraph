# ADR 0001: Rendering backend abstraction and initial engine

- Status: Accepted
- Date: 2026-07-15

## Context

JGraph must render publication-quality output, handle millions of points, support vector export
(SVG/PDF), and leave room for future GPU acceleration. The requirements also state that rendering
must never be tightly coupled to WPF drawing APIs, and that different rendering engines should be
implementable later.

## Decision

1. **Abstract all drawing behind `IRenderContext`** (in `JGraph.Rendering`). It exposes primitive
   operations — clear, clip, line, polyline, rectangle, polygon, markers, text, and text
   measurement — in device space. The object model and the figure renderer depend only on this
   interface. A "rendering engine" in JGraph *is* an `IRenderContext` implementation; we deliberately
   do not add a separate empty `IRenderEngine` abstraction, because `IRenderContext` already provides
   the substitution seam and an extra interface would be dead weight.

2. **Use SkiaSharp as the first concrete engine** (`JGraph.Rendering.Skia`). Skia is a mature,
   cross-platform 2D engine (used by Chrome and Flutter) that handles very large geometry, has GPU
   backends (OpenGL/Vulkan/Metal) for later, and offers native SVG and PDF output plus crisp
   anti-aliased raster rendering. It is the single project permitted to reference SkiaSharp.

## Consequences

- A GPU surface, an SVG exporter, or a PDF exporter is a new `IRenderContext` implementation; no
  model or renderer code changes.
- Text measurement is part of `IRenderContext`, so layout that depends on label sizes happens inside
  the paint pass using the same backend that will draw the text — measurement and rendering never
  disagree.
- Backend-specific resources (Skia paints/fonts) are cached inside the Skia context and never leak
  into the abstraction.
- The abstraction is intentionally small; backends that lack a primitive (for example, a plotter
  format without filled polygons) emulate it rather than the interface growing backend-specific
  members.
