# ADR 0113 — A page with nothing on it

Milestone: **M111a**
Status: accepted

## Context

M111 made `axis off` clear an axes' furniture and leave its children standing, which is most of what a
script wants when it is drawing a figure to lay over something else. It is not all of it: the page
itself was still painted. Every export cleared the canvas with the figure's own colour, so the best a
cut-out could do was match a background it already knew — the same "only works against a known
colour" bargain the workaround before M111 struck.

MATLAB's answer is one word: `exportgraphics(fig, file, 'BackgroundColor', 'none')`. The option was
already read here for its spelling, and `'none'` was the one value it refused.

## Decision

`'none'` is a fully transparent colour, and it travels the ordinary path: the renderer clears with it,
Skia's canvas clears to nothing, and the PNG encoder writes the alpha channel it finds. Nothing new
was needed under the scripting layer.

Three details decided rather than defaulted:

- **The formats that cannot carry it say so.** PNG, PDF and SVG can; this build's TIFF writer emits
  three samples per pixel and its BMP writer's fourth byte is `BI_RGB` padding, and JPEG has no
  alpha at all. Those four refuse `'none'` by name and say which formats do, rather than writing the
  black rectangle a transparent clear leaves behind in them.
- **`InvertHardcopy` leaves a transparent page alone.** It exists so a dark figure is not printed
  black, and its test was "the background is not white" — which a transparent background passes. It
  would have answered a request for no page by painting the page white, which is the one thing the
  caller said not to.
- **`'current'` and `'figure'` are accepted** as MATLAB's two spellings of "the colour it already
  wears", so the option can be written unconditionally in a loop.

The figure is not changed by any of this: the colour is swapped in for the one export and restored in
a `finally`, which is what the option already did for an opaque colour, so an animation loop that
exports a cut-out every frame still draws on screen in its own colour.

## Consequences

`SurfaceLerpTest_5.m` in the demo workspace is the first script written against this and against
M111 together: no painted background, no hand-cleared decorations, `axis off` and
`BackgroundColor 'none'`, writing a numbered PNG sequence because MP4 carries no alpha.

`copygraphics` is unchanged — it never applied `BackgroundColor` to the figure at all, and a
transparent clipboard image is a question about the clipboard rather than about the renderer.

## Recorded divergences

- **`copygraphics` does not read `BackgroundColor`.** It parses the option and copies the figure as
  it stands.

## Testing

`TransparentBackgroundTests` takes the exporter at its word: a transparent figure's PNG corners carry
zero alpha, `InvertHardcopy` no longer whitens it while it still whitens an opaque dark page, and an
axes told `axis off` lets the transparency through the plot box as well as the margins — better than
nine tenths of the picture left empty, which is what a cut-out is.

`MatlabTransparentExportTests` covers the script's end: `'none'` writes an empty-cornered PNG, leaves
the figure wearing its own colour afterwards, is refused by name for `.jpg`, and `'current'` writes
the red page the figure was given.
