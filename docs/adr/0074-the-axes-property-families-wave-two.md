# 0074 — The axes property families, wave two: the camera, the alpha map, and the odd four

Date: 2026-08-21 · Milestone: M74 · Status: accepted

## Context

ADR 0073 closed sixty-nine axes properties and deferred twenty-two, naming them precisely because
they were the ones that needed machinery the renderer did not have: a camera that could be placed
rather than merely aimed, a perspective divide, a lookup from data to transparency, and switches for
clipping and face order. This wave built that machinery. Measured after it: axes **122 → 139/147**,
polaraxes **75 → 82/107**, properties overall **852 → 878 of 1,394**.

The eight axes names still unanswered are the five of the layout family
(`InnerPosition`, `OuterPosition`, `TightInset`, `PositionConstraint`, `Units`), which wait for M75,
and the three ADR 0073 left unanswered by decision (`Interactions`, `Toolbar`, `Layout`).

## The camera is a set of nullable slots over the angles

`Azimuth`, `Elevation` and `Roll` remain the primary state: interaction drag writes them, undo
captures them, documents have always carried them, and `view` speaks them. The four camera
quantities became **manual overrides where null means auto** — `CameraPosition`, `CameraTarget`,
`CameraUpVector`, `CameraViewAngle` — with the four `*Mode` properties derived from the null test,
the same idiom as all of M73's modes. `Projection` is a plain enum defaulting to orthographic.

The consequence that mattered most: **an axes whose every slot is null runs the constructor it always
ran.** `Projection3D` gained a second constructor rather than a rewritten one, and `FigureRenderer`
picks between them on `axes.HasAutomaticCamera`. That is what makes the wave's default rendering
provably unchanged rather than argued to be.

The derived camera is not invented, either: the position the angles imply is `target + direction ×
distance` where the distance is the one the view angle asks for, so reading `campos`, `camva` and
`camproj` back describes a camera that would draw the picture on screen. A test asserts the two
constructors agree to six decimals at four different view angles.

`view` releases all four slots — MATLAB's own behavior, and the reason `SetViewAngles` exists as a
single method that every angle writer routes through: `JG.View`, the `View` property, `camorbit`, and
the rotate drag. Undo captures the slots beside the angles, so undoing a rotate that released a
hand-placed camera puts it back.

## What the second constructor does

Everything happens in the normalized cube the projection already worked in, so a camera placed in
data units looks the same whatever magnitudes the three axes carry. The position and target are
mapped through `Normalize`; the up vector takes only the scale part, because it is a direction. A
look-at basis follows — `d` from target to camera, `u = up × d`, `v = d × u` — and roll composes onto
`u`/`v` afterwards, exactly as it did. The target projects to the center of the plot area, which is
what makes moving the target pan the picture.

The scale is either the fit (when the view angle is automatic — MATLAB's automatic view angle *is*
the fit) or `min(W, H) / (2 · distance · tan(va/2))`. Halving the angle doubles the picture, and that
identity is the whole of `camzoom`.

Perspective is the `viewmtx` divide re-expressed about the target plane: something at the target's own
depth is drawn life size, nearer things grow by `distance / (distance − toward)`, farther things
shrink. `CameraMatrices.ViewMatrix` had carried this math since the `viewmtx` builtin was written and
the renderer had never used it.

`Unproject` is the inverse the axes `CurrentPoint` needed: a pixel names a line of sight, and the
slab method clips it to the plot box. The rotation is orthonormal, so the orthographic inverse is the
transpose; the perspective case un-divides at the target plane and casts from the camera. A test
asserts `Unproject ∘ Project` returns the point it started from, in both projections.

## The other three families

- **`SortMethod`** rides a `RenderState` flag, since every 3D object already sorted its own faces on
  the projected depth. `'childorder'` skips that sort in each of the five objects that do one; the
  surface's wavefront walk is asked not to reverse either direction, which is the same thing said
  without a sort. Cross-object order stays `ZOrder` in both modes, because that is what childorder
  means between objects.
- **`Clipping`** guards four of the renderer's five `PushClip`/`PopClip` pairs — the 2D plots, the 3D
  content, the polar content, and the data-space annotations. The grid's clip stays unconditional:
  grid geometry never exceeds the plot area, so unclipping it buys nothing and risks pixel drift.
  **`ClippingStyle`** answers `'rectangle'`, which is the truth, and refuses `'3dbox'`.
- **Alpha mapping** is `Colormap` said again for transparency. `AlphaSampler` mirrors
  `Colormap.Sample` including the log overload; `AlphaResolver` mirrors `ColorScaleResolver`. The
  alphamap is a plain `IReadOnlyList<double>` rather than a type of its own, because MATLAB's is a
  vector the script hands over whole. `ALim`/`ALimMode` are the `CLim`/`CLimMode` block again.
- The mapping had to be **consumed** to be real: `AlphaData` landed on `SurfacePlot` (with the flat
  face mode MATLAB spells as `FaceAlpha` holding a word instead of a number) and on `ImagePlot`. Both
  caches learned the alpha stamp — the data, the limits, the map and the scale — because a cache that
  ignores what it was built from keeps drawing the old transparencies. That is the M73 lesson about
  `_builtLogColor`, applied before it could bite rather than after.
- **`CurrentPoint`** is transient interaction state, deliberately not serialized. The hit test records
  it, and on a 3D axes it records the real sight line through `Unproject` rather than a flat point the
  camera does not agree with. The click callback's `IntersectionPoint`, which had carried a hardcoded
  zero for its third component since M71, now reports that line's near end.

## What the wave found on the way

- `camva` was a zoom on the axis limits against a constant named `DefaultViewAngle`, and `campos`
  synthesized a position at a hardcoded distance of two box spans; `camtarget` and `camup` were
  stubs that refused any value but the one they returned. All four are now the state they claim to
  describe, and the three helpers that propped them up were deleted rather than left orphaned.
- `camproj` discarded the axes it was handed — a latent bug in the named-axes form, fixed here.
- The automatic up vector cannot be `+z` for a top-down view, because the camera is looking along it.
  MATLAB answers `[0 1 0]` for `view(2)`, and now so does JGraph; the projection's degenerate-up
  fallback exists for a script that asks for one anyway.
- A grid of alpha data of the wrong shape threw an `ArgumentException` out of the engine instead of
  failing the script. It is a script mistake and is now reported as one.

## Verification

4,870 tests (20 new in `MatlabM74AxesPropertyTests`, 13 in `Projection3DCameraTests`, 3 in the
serialization suite), 46/46 stress scripts, all four verifiers OK, and the property probe re-measured:
878 of 1,394 across 28 kinds. Every visual property was proved by pixels before it was written down —
perspective 105k changed pixels, the camera target 204k, the up vector 213k, the view angle 353k,
childorder 34k, clipping 1,061, alpha data 123k, `ALim` 110k, the alphamap 119k, `AlphaScale` 123k.
No property in this wave answers without acting.

## Divergences recorded

- **`camdolly`, `campan` and `camlookat` still move the axis limits** rather than the camera, which
  is what they did before the camera was real. They are consistent with each other and pinned by
  `stess_26`, and moving them onto the camera is a separate decision.
- **`ClippingStyle` answers `'rectangle'` and refuses `'3dbox'`**, where MATLAB defaults to `'3dbox'`.
- **`AlphaData` reaches surfaces and images only.** Patches and scatters carry no alpha data, so the
  axes-level alpha mapping has nothing to act on there.
- **`FaceAlpha 'interp'` is refused.** Only `'flat'` — one transparency per face — is implemented.
- **A 2D axes reports `CurrentPoint` with a zero third component**, where MATLAB reports the two ends
  of the sight line at the camera's own distance.
- **`CurrentPoint` follows the pointer on a 2D axes and only a click on a 3D one**, because the flat
  mapper the hover path holds is not the camera a 3D pixel must be read through.
- **Lighting resolves one view direction per frame from the orthographic depth row**, which under a
  perspective camera is an approximation that grows with the view angle.
- **Nothing is clipped at the near plane.** A point behind a perspective camera is flung far outside
  the plot area, where the plot-area clip deals with it.
- **`Projection` is stored but consulted only in 3D**, so setting perspective on a flat axes is inert
  until it becomes three-dimensional.

## What is not done

The layout family — `InnerPosition`, `OuterPosition`, `TightInset`, `PositionConstraint`, `Units` —
which needs the renderer to report the rectangle it measured, and which M75 takes together with the
figure property closure. `Interactions`, `Toolbar` and `Layout` remain unanswered by the decision
ADR 0073 recorded.
