# ADR 0112 — A view angle is a zoom, and Visible is furniture

Milestone: **M111**
Status: accepted

## Context

Two defects from one animation script, reported together because the workarounds for them were
written in the same file. Both are old, both were recorded somewhere as already working, and both
turn out to be a single sentence said wrong in two places.

**`camva(angle)` ignored the angle.** Every value between 4 and 18 drew the identical picture. Only
the difference between "never called" and "called at all" moved anything, and that step was large.
`camva()` read the value back correctly, so the model stored the angle and the renderer did not use
it — which is the shape of a bug that survives every test written about the property and none about
the picture.

**`axis off` did nothing at all.** The word sat in the same switch arm as `auto` and `manual`, which
really are accepted no-ops here, and a comment beside it said visible frames were already the
default. The only property that could have cleared the frame, `Visible`, hid the axes' children
along with it, which MATLAB does not do. So the workaround in the field was to paint every
decoration in the background colour by hand — which only works against a background you know.

## Decision

### A chosen view angle is a magnification, not a placement

The cause is arithmetic, and it is worth writing down because it is invisible in every line
individually. M74 gave `Projection3D` a second constructor that takes a placed camera, and
`FigureRenderer` chose between the two on `HasAutomaticCamera` — which a chosen view angle makes
false. So `camva` moved the picture onto the placed-camera path, and that path needed a camera
position, which nothing had supplied, so it used the derived one. And the derived stand-off was
`diagonal / 2 / tan(va/2)`.

The scale the placed-camera path then computes is `min(W, H) / (2 · distance · tan(va/2))`. Substitute
the stand-off and the tangents cancel: the visible width is the box diagonal for every angle. The
cone narrowed and the camera stepped back by exactly enough to undo it. Both halves were reasonable
and each was tested against the other, which is why the pair survived.

Two changes, either of which alone would have been a partial fix:

- **The automatic stand-off is the one the *default* angle implies**, never the chosen one. MATLAB
  leaves `CameraPosition` where it is when `camva` narrows the cone — `CameraPositionMode` stays
  `auto` and the position does not move — so `campos` no longer reports a camera that walks backwards
  as the picture is zoomed in.
- **An angle alone is not a placement.** `HasAutomaticCameraPlacement` is the old test with the view
  angle taken out of it, and it is what the renderer and the hit-tester now branch on. An axes that
  has been given nothing but an angle stays on the fitting constructor, and the angle arrives there
  as `CameraZoomFactor` — `tan(default/2) / tan(chosen/2)` — which multiplies the fit.

Multiplying the fit is what makes the framing continuous. At the default angle the factor is exactly
one, so an untouched figure is drawn to the same pixel it always was; and `camva(6.6086)` is the
automatic framing rather than a jump away from it. `camzoom` divides the effective angle and so rides
on the same path for free.

### `Visible` governs an axes' own furniture and never its children

This is MATLAB's model, and it was measured against R2024a rather than recalled: with `axis off`, the
rulers, ticks, tick labels, box, grid, the two axis labels and the axes background all go; the
**title stays**, and so do the plots, the legend and the colorbar. `axis off` now sets `Visible`
false and `axis on` sets it true, and `Visible` means that and only that.

So `FigureRenderer` no longer skips an invisible axes wholesale. It draws it and leaves out the
furniture: the background rectangle, the 2-D frame edges, ticks and side rulers, the grid on either
layer, the 3-D box and its grid and edge labels, and the x/y/z labels — while `DrawTitleBlock`, the
plots, and every floating decoration are drawn either way.

The furniture that is not drawn is also **not measured**. `MeasureDecorations` skips the tick and
label bands for an invisible axes, so the plot box takes back the margin they were holding. That is
most of what a script says `axis off` for, and it is what makes an exported still fill its frame.

## Consequences

`camva` and `camzoom` do what their catalog entries have always claimed. ADR 0048 recorded "`camva`
is applied as a zoom about the default framing" and struck it out as retired by M74; M74 is where it
stopped being true, and this restores it — the coverage doc's copy of that sentence has been
corrected rather than left to age.

A polar axes is not covered: `axis off` sets its `Visible` false like any other, and the rings and
spokes are drawn by `RenderPolarContent`, which does not consult it. Recorded below rather than
half-done.

An axes told `set(ax, 'Visible', 'off')` now behaves differently than before — its children stay on
the page. That is the reported defect, not a regression, and it is the behaviour MATLAB has.

## Recorded divergences

- **`axis off` leaves a polar axes' rings and spokes drawn.** The Cartesian and 3-D frames obey
  `Visible`; the angular one does not yet.
- **The layout does not otherwise change when an axes is hidden.** MATLAB re-lays the axes out
  against its `OuterPosition`, so the plot box grows by a little more there than it does here; here
  it grows by exactly the bands the ticks and labels had reserved.

## Testing

`AxisOffAndViewAngleTests` covers both halves at the level each was broken at: `CameraZoomFactor` is
one at the default angle and doubles when the cone is halved; the automatic camera does not move when
an angle is chosen; the fitting projection magnifies by the zoom it is handed; and — end to end
through the renderer — 18°, 8° and 4° draw three different sizes in MATLAB's order, with the
automatic framing landing between 8 and 4 where the default angle says it should. On the 2-D and 3-D
sides an invisible axes draws the same plots and none of the lines, keeps its title, loses its axis
label, and hands its margins back to the plot box.

`MatlabAxisOffTests` covers the script's end of the wire: `axis off` then `axis on`, a 3-D surface
that survives both, and `camva` read back beside a placement that is still automatic.

Four of the ten fail against the renderer this commit replaces. The three angles of the report's own
repro, which drew byte-identical images before, now draw 132,695, 40,151 and 9,994 inked pixels; real
MATLAB R2024a draws 652,201, 179,959 and 44,033 for the same three, in the same order.
