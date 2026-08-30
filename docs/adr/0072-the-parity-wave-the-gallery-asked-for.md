# 0072 — The parity wave the gallery asked for

**Status:** accepted · **Milestone:** M72 · **Supersedes in part:** [0023](0023-matlab-compatible-jgs-surface.md)

## Context

M71 closed with the callback seam built and a capability report written over the build. That report
was made the honest way — by writing real MATLAB against this console and drawing the figures it
produced — and the afternoon of gallery work turned up **eleven defects**, which the report listed
under "Quirks found while making this report". They were not exotic. Every one was a line a MATLAB
user would type without thinking:

```matlab
set(p, 'EdgeColor', 'none')
surf(X, Y, Z, 'FaceAlpha', 0.4)
title('x\cdot e^{-r^2}')
[X, Y, Z] = meshgrid(v)
slice(X, Y, Z, V, 0, 0, 0)
streamline(X, Y, U, V, sx, sy)
1 - mat2gray(I)
imwrite(frame, 'anim.gif', 'WriteMode', 'append')
figure('Position', [100 100 400 300])
grid minor
```

The report's own "next milestones" table named three of them — transparency, patch lighting, TeX
text — as unscheduled. This milestone takes the whole list, plus one stale entry in the divergence
index that had quietly stopped being true.

## What the probes established

Every item was reproduced through `jgraph.exe -batch` before a line was written, which is the
standing M46 rule, and three of the eleven turned out not to be what the report said:

1. **Whitespace array literals already parsed.** `[3 2 1 0; 4 5 6 7]` indexed, sized and `disp`ed
   correctly; ADR 0023's "array literals require commas" had been overtaken years earlier by the
   parser's `StartsANewElement`. The real defect was the **echo**: `Interpreter.EchoDisplay` walked
   an array element by element in column-major order, so the matrix came back as
   `[3, 4, 2, 5, 1, 6, 0, 7]` — the right numbers, in an order nobody typed. `disp` was right the
   whole time, because it went through `FormatMatrix` and the echo did not.
2. **`streamslice` no longer wiped held content.** The contour survived; what remained was the
   colours, one series colour per streamline, which turned a twenty-line slice into a plaid.
3. **`view(3)` was not a no-op on the property.** `get(ax, 'View')` already answered `[-37.5 30]`.
   The axes was never in three dimensions to begin with, because only the surface verbs set
   `AxesModel.Is3D` and a patch-only axes therefore rendered flat.

That third finding is why patch lighting could not have been tested at all before it was fixed: a
patch in a 2-D axes goes through `Render`, not `Render3D`, and lighting lives in the latter. The
first version of the lighting work was measured against an exported PNG and moved **zero pixels**,
which is how the 3-D flag was found.

## Decisions

### `'none'` is written, not read, on a surface

MATLAB's `'none'` is not a colour; it is the absence of one. Where an object already had somewhere
to put that — a nullable `Color`, a `FaceVisible` flag — the word now writes there, and reflection
learned the nullable case once so every reflected `Color?` property on every kind gained it in a
single edit.

A **surface is the exception, and deliberately so.** `SurfacePlot.FaceColor` being null does not
mean "no faces"; it means "take the colour from the colormap", which MATLAB spells `'flat'`. Writing
`'none'` there drops the faces (the style becomes `Wireframe`) and writing `'flat'`/`'interp'`/a
colour brings them back, so the useful half — the half that was reported — works. **Reading still
answers `[]` rather than `'none'`/`'flat'`**, because `stess_26.m` is a frozen asset and asserts
`isempty(get(mesh, 'FaceColor'))`. That is a divergence, recorded below, and it is the one place in
this milestone where an older spelling was kept rather than replaced. Both readings were built and
measured; the frozen contract decided it.

### Alpha multiplies, it does not replace

`FaceAlpha` and `EdgeAlpha` are new model properties on `PatchPlot` and `SurfacePlot`, each
multiplying the object's existing `PlotObject.Opacity` rather than standing in for it — so
`alpha(0.5)`, which works the whole object and predates this, composes with a per-part setting
instead of fighting it. Two latent bugs fell out of the wiring: a surface's explicit `EdgeColor`
took no opacity at all, and a solid `FaceColor` bypassed it entirely.

### A patch is lit the same way a surface is

`ILitObject` is the new seam: `FaceLighting` and `Material`, implemented by both classes, so
`lighting` and `material` reach every lit object in an axes rather than every surface. Normals are
built in the projection's normalized cube space — the same reason a surface's are, and the reason
they mean anything when X spans ones and Z spans millions. Flat lighting takes Newell's normal per
face; gouraud sums the incident face normals per vertex and shades the triangle fan's corners.

### `slice` keeps both of its meanings

JGS has had `slice(array, start, stop)` since long before there were volumes to cut, and the JGS
surface is frozen. MATLAB's volume `slice` is therefore declared **only for the MATLAB dialect**,
where it shadows the array reading. Each plane is drawn as a `PatchPlot` with one interpolated
reading per vertex, not as a surface: a surface here is a height over the x-y plane, and two of the
three slice orientations stand vertically, where no such height exists.

### The form of a `streamline` call is decided by counting

MATLAB gives the plane form and the space form the same name. Four or six arguments is a plane,
nine is space, and six is the one place both readings fit — `streamline(X, Y, U, V, sx, sy)` against
`streamline(U, V, W, sx, sy, sz)` — where the field settles it, because a volume has pages and a
plane does not. Every one of the five documented forms errored before this: the reader was fixed at
three components, so a plane call handed its arrays to a volume reader that measured them against
each other and refused.

### TeX is translated to characters, not laid out

`TexMarkup.Render` maps MATLAB's documented TeX subset to Unicode, and it is applied in
`SkiaRenderContext.DrawText`/`MeasureText` — the one funnel every text in a figure passes through.
That placement is the whole reason the feature reaches a title, a tick label, a legend entry and a
text object at once, and it is what the choice of Unicode buys: a run of characters is exactly what
one `DrawText` call can draw. `TextStyle` gained an `Interpreter` field so a text object can opt
out, which is how `'none'` works and how `get(t, 'String')` keeps answering what was written.

The cost is stated plainly: superscripts and subscripts work for the characters Unicode has them
for, `\bf` and `\color` are read and dropped because one run has one style, and `\frac` is not
stacked. Everything they contain is still shown, and an unknown command is shown as written rather
than swallowed.

### A picture is a matrix of readings, at the operator

MATLAB has no image type. Reading an image value as numbers at `Interpreter.NumericBinary` — rather
than teaching every verb to accept one — is what makes the whole family compose: `1 - I`, `I .* 2`,
`mat2gray(mat2gray(X))`. A picture too large to box (four million samples, the line `im2mat` already
draws) declines, and the operator refuses by type as it did before, which is the honest answer for
an expression that would otherwise allocate a hundred million boxes.

### GIF is written here because Skia will not

Skia decodes GIF and does not encode it, so `GifEncoder` is written out: palette selection (exact
when a figure has fewer than 256 colours, a 6×6×6 cube plus a grey ramp when it does not),
variable-width LZW, and the length-prefixed sub-blocks the format wants. **Appending writes a whole
frame over the file's trailing terminator and puts the terminator back** — a GIF is a stream of
self-describing blocks, so a frame added that way is indistinguishable from one written in the first
pass, and a script building a hundred-frame animation never holds more than one frame in memory.

## Divergences recorded

- **A surface answers `[]` for an unset `FaceColor`/`EdgeColor`, where MATLAB answers `'flat'` (or
  `'none'` for a mesh's faces).** Writing takes MATLAB's words; only reading keeps the older
  spelling, because `stess_26.m` is frozen and asserts it.
- **TeX is rendered to characters rather than laid out.** No stacked fractions or integral limits;
  `\bf`, `\it`, `\rm`, `\color`, `\fontname` and `\fontsize` are read and dropped, since a run drawn
  in one call has one style. A superscript or subscript falls back to the plain character where
  Unicode has no raised form for it.
- **`'latex'` is read as the TeX subset with the maths delimiters dropped.** It covers the symbol and
  script markup the two languages share, which is what almost every axis label uses it for.
- **`slice`'s trailing interpolation-method word is checked and then read linearly** whichever of the
  three it names.
- **An appended GIF frame must match the first frame's size.** The size a viewer shows is fixed by
  the logical screen descriptor, so a differently-sized frame is refused rather than silently
  cropped.
- **Outside a GIF, an indexed `imwrite(X, map, path)` paints through the map rather than storing it.**
  None of the other formats written here is an indexed one, so the picture is saved as the colours it
  then has — the same picture.

**Retired by M108, and deleted from the list above rather than struck through**: *"There is no
`VideoWriter`"*. The reasoning held for a codec this project would have had to ship, and not for the
two it does not: an AVI is a RIFF file written here, and MP4 goes out through the H.264 encoder
Windows already provides. Five of MATLAB's seven profiles are written; the two JPEG 2000 ones are
refused by name. See ADR 0109. GIF remains the right answer for a short animation that has to work
everywhere, and is the only answer off Windows for anything but AVI.

**Retired by M85, and deleted from the list above rather than struck through** (the harvest lifts a
struck-through bullet whole): *"`slice` draws axis-aligned planes only"*. ADR 0085 built the slicing
surface, and found on the way that the form did not refuse — handed three matrices it read them as
seventy-two scalar plane positions and drew a hundred and eight patches, under a probe verdict of
`accepted`.

## What is not done

- **3-D picking.** A click on a `surf` still resolves to the axes; carried from ADR 0071 and still
  the natural core of an interaction wave. *(Done in M87; see ADR 0087.)*
- **Window-level mouse and key callbacks.** Also carried from 0071. *(M75 built the whole family —
  this line was already false when it was written, and ADR 0087 records how long it stood.)*
- **The axes property families** — camera, rulers, layout — remain the largest single block in the
  property table, 94 names on the axes alone.
- **`MarkerFaceColor`/`MarkerEdgeColor`** are spelled `MarkerFill` here; found while probing `'none'`
  across the kinds, and left because it is a naming pass rather than this milestone's subject.
  *(Done in M86 — which found that the two JGraph spellings were being served as properties in their
  own right, and that the charts in space answered neither MATLAB name at all; see ADR 0086.)*
- **`streamslice`'s three spatial forms** error on the verb's own arity — nine arguments handed to
  something that accepts eight. *(Done in M85, which took the verb to 10 of its 10 documented forms;
  see ADR 0085.)*
- **The ~55 numeric/file form leftovers** (`fft(X, n, dim)`, `eig(A, B)`, `lu` output forms,
  `textscan`), carried from ADR 0070 and still a clean standalone wave.

## Verification

0-warning Release build; `dotnet test JGraph.sln --filter "FullyQualifiedName!~Python"` green;
`tools/run-stress.ps1` exit 0 with zero `Fail:` lines over 44 scripts; all four coverage verifiers
OK. Every behaviour was probed through `jgraph.exe -batch` before its test was written. Three of the
changes — patch lighting, `EdgeColor 'none'` on a surface, and TeX — are asserted against **rendered
pixels**, by exporting the figure twice and counting what moved, because each of them is a change
nothing else in the gate can see.
