# ADR 0027 — Image value type and codec layering

## Status

Accepted (M24, 2026-07-19).

## Context

M24 adds a curated core of MATLAB's Image Processing Toolbox to the JGS scripting language
(`imread`/`imwrite`/`imshow`, colour conversion, intensity/histogram ops, geometry, 2-D filtering,
edge detection, morphology, connected-component labeling). Two design pressures shaped it:

1. **How to represent an image value.** JGS had `JgsType` = Null/Number/Complex/Bool/String/Array/
   Table/Function and no 2-D matrix type — a "matrix" is nested boxed row-arrays. A megapixel image
   as nested `JgsValue` arrays is one heap object per pixel (unusable), and a `Table` is the wrong
   shape (thousands of columns). The M22 `NumericBuffer` tiering (managed → native → SSD-mapped) is
   exactly the right backing store for 12-MP-scale data, but a bare packed array carries no
   height/width/channel metadata.
2. **Where image decoding may live.** No image decoder existed anywhere. SkiaSharp was referenced
   only by `JGraph.Rendering.Skia`, `JGraph.Export`, and `JGraph.Controls`; the layering invariant
   keeps `Core`/`Objects`/`Signal`/`Api` Skia-free. `JGraph.Scripting` (an application-tier project)
   referenced none of them.

## Decision

1. **A new `JgsType.Image` carries an `ImageBuffer`**, mirroring how `Table` is carried (an opaque
   reference in the value's reference slot). `ImageBuffer` (in the new **`JGraph.Imaging`** project)
   holds one `NumericBuffer` of interleaved, row-major, `[0, 1]` samples plus `Height`/`Width`/
   `Channels` (1 or 3), so it inherits the M22 native/mapped tiers for large images for free. It
   defines a constant-size `Display()` (`image[HxWx3]`) so the console and Variables panel never dump
   pixels, and the run-end buffer sweep in `JgsRunner` disposes image values exactly once — every
   builtin returns a freshly allocated buffer (identity-ish ops `Clone()`), so no buffer is ever
   aliased into two values. Script-level access is read-only 1-based subscripting
   (`img(r, c[, ch])`); mutation happens only through builtins.

2. **Two new projects, both referenced directly by `JGraph.Scripting`.** `JGraph.Imaging` (depends
   only on `JGraph.Numerics`) holds `ImageBuffer` and every algorithm as codec-free static classes,
   keeping them headlessly unit-testable. `JGraph.Imaging.Codecs` (adds SkiaSharp) holds the sole
   `ImageCodec.Read`/`Write` bridge. The layering invariant names `Core`/`Objects`/`Signal`/`Api` —
   `Scripting` is not in that list, and every existing consumer of `Scripting` already sits above
   SkiaSharp, so nothing acquires Skia transitively that did not already have it. A `ScriptContext`
   codec seam was rejected: codecs are stateless pure functions with no host-owned resource, so a
   seam would force every host and test fixture to wire one for no benefit.

3. **`imshow` reuses `ImagePlot` for grayscale and adds `RgbImagePlot` for colour.** Grayscale draws
   through the existing colormapped `ImagePlot` with a fixed `[0, 1]` gray range; true colour uses a
   new Skia-free `RgbImagePlot` that holds pre-computed `0xAARRGGBB` pixels and calls the existing
   `IRenderContext.DrawImage` seam — no renderer registry or axes-model change. The one
   `ImageBuffer → uint[]` copy at display time is also what makes the run-end buffer disposal safe,
   since figures outlive the run. `RgbImagePlot` serializes as a base64 pixel DTO, bumping the
   `.graph` format to version 5.

## Consequences

- Images get the M22 memory tiering, a clean type identity for builtins and error messages, and
  bounded console/inspector output, without rippling a matrix type through the interpreter.
- `JGraph.Scripting` now depends on SkiaSharp (via `JGraph.Imaging.Codecs`). This is acceptable for
  the application tier; the pure algorithms in `JGraph.Imaging` remain codec-free and could later be
  referenced by `Objects`/`Api` without breaking the invariant.
- Algorithms target MATLAB-compatible signatures, not bit-exact output (Canny/imresize/edge
  auto-thresholds differ); tests anchor on hand-computed fixtures and structural properties.
- Old builds reject version-5 `.graph` files, consistent with the v2/v3/v4 policy.
