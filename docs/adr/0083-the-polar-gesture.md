# 0083 — The polar gesture

Date: 2026-08-24 · Milestone: M83 · Status: accepted

## Context

ADR 0056 built the polar axes and recorded one limit: *wheel zoom and pan do not speak polar yet.*
Every wave since has restated it. What the milestone found on looking is that the reason was specific
and nameable, and that closing it needed one thing nobody had noticed was missing.

**The renderer handed the interaction layer the Cartesian mapper, even for a polar axes.**
`AxesRenderInfo.Transform` is an `AxisTransform` over the primary X and Y ranges, and a polar axes
stores θ as its plots' X data and r as their Y. So a wheel over a polar chart ran
`Navigation.ZoomAboutPixel` on ranges holding radians and radii — perfectly well-formed arithmetic,
on ranges the drawing does not read from. Nothing on screen changed, and nothing errored either.

**And `AxesViewState` read only the X, Y and Z axes and the camera.** `Capture`, `ApplyTo` and
`DiffersFrom` all did. So a polar gesture, once it worked, would have compared before and after as
equal, `CommitViewChange` would have pushed nothing, and the whole thing would have been
un-undoable — silently. That is the half of this milestone a reader would not think to look for, and
it is why the test file ends with two fixtures about the undo stack.

**The rotation had nowhere to live.** The user asked for a drag that turns the chart as well as one
that slides the radii. Two candidates existed and both were wrong:

- `ThetaZeroLocation` is a four-word enum — Right, Top, Left, Bottom — and a drag turns the chart by
  whatever angle the pointer moved.
- Shifting `ThetaLim` **rotates nothing**. `PolarTransform` uses the visible turn only to decide which
  angles are *drawn*; where a drawn one lands comes from the zero angle. `[0 360]` to `[30 390]`
  leaves every point exactly where it was. This was the plan's mechanism and it was tested and
  discarded — the last fixture in `PolarNavigationTests` pins both halves of that finding so it
  cannot be re-proposed.

## Decisions taken before any code

1. **The polar mapper travels beside the Cartesian one, not instead of it.** `AxesRenderInfo` gains
   `Polar` and `Mapper`; `Transform` is unchanged and still what hit-testing, data tips and
   annotations read. Widening `Transform` to `ICoordinateMapper` would have moved four consumers that
   have no reason to move.

2. **`Navigation` branches on the mapper it was given, not on a flag.** `axes.IsPolar && mapper is
   PolarTransform` — so a caller that somehow holds a Cartesian mapper for a polar axes takes the old
   path rather than writing to rulers its mapper cannot read.

3. **A drag is decomposed once, against the centre, and both components apply at once.** The radial
   part slides the visible radii; the tangential part turns the chart. No modes and no modifier: the
   chart follows the pointer. This is more than MATLAB's polar pan does and is recorded below.

4. **The turn is a new continuous property, `AxesModel.ThetaZeroOffset`, in degrees.** It rides on the
   zero angle inside `PolarTransform`, so every ring, spoke, label and data point moves together by
   construction. It is serialized, because a view a saved figure cannot keep is one a gesture should
   not have been allowed to make — unlike the interaction list beside it, which is deliberately not
   serialized because it says how a window behaves rather than what a figure is.

5. **And it is a scriptable property**, `ThetaZeroOffset` on a polar axes. A rotation a script can
   neither read nor set would be a view the rest of the build cannot describe. MATLAB does not
   document the name; that is the divergence, and it is smaller than the alternative.

6. **`Dimensions` maps onto the two rulers a polar axes has**: `X` is θ and `Y` is r. `XY` — the
   default — scales r alone rather than both, because zooming a polar chart means changing how much of
   the radius is shown, and a default wheel that also narrowed the wedge would be a surprise nobody
   asked for.

7. **`ResetView` restores what a gesture moved.** The radial ruler, the visible turn and the rotation,
   as well as the Cartesian pair. A reset that put back two of five would be worse than none.

M80's gates apply unchanged and for free: `PanDragGesture.Begin` already requires
`InteractionOf<PanInteractionModel>()` and `Wheel` requires `ZoomInteractionModel`, so
`disableDefaultInteractivity` silences a polar chart without a line being written for it. There is a
test for that, because "for free" is a claim.

## Verification

- 0 warnings in Release and Debug; **5,219 tests** (5,208 + 11); **55 of 55 stress scripts**, including
  the new `stess_55.m`.
- The gestures are window chrome and cannot be driven from a batch script, so they are driven through
  `InteractionController` directly in `PolarNavigationTests`, as the existing interaction tests do.
  `stess_55.m` checks the state a gesture moves and the pixels each part of it changes — a rotation
  that moved a number and no pixels would pass every test written against the gesture.
- The Cartesian side is pinned in both places: a flat axes still zooms and pans exactly as it did, and
  a polar wheel leaves the primary X and Y ranges untouched, which is what says the gesture found the
  rulers the chart is drawn through.

## Divergences recorded

- **A drag turns a polar chart, which MATLAB's does not.** MATLAB's polar pan is radial. The
  tangential half of the gesture is this build's own, and it is what the user asked for.
- **`ThetaZeroOffset` is a property MATLAB does not document.** It exists because a continuous
  rotation needs somewhere to live and `ThetaZeroLocation` holds four compass points. It reads and
  writes on a polar axes and is saved with the figure.
- **`Dimensions` on a polar axes is this build's reading.** MATLAB does not define it for one. `X` is
  θ, `Y` is r, and `XY` is r alone rather than both.
- **A polar `ResetView` puts `ThetaLim` back to the whole circle**, rather than to whatever a script
  last set. A reset restores the view a fresh chart has, and a wedge is a view.

## What is not done

- **The data tip on a polar chart now reads through the polar mapper**, which is a change and an
  improvement — a click used to be reported in radians-as-X. It is not a divergence and it is not
  tested here beyond the mapper's own round trip; a data-tip wave would pin the text.
- **Rotating does not move the θ tick labels' own rotation**, only their positions. They stay upright,
  which is what MATLAB's are.
- **Housekeeping**: `docs/matlab-divergences.md` still carried ADR 0078's "`Interactions` stays
  unanswered on polar axes and on text, and `Toolbar` on polar axes", which M80 answered three
  milestones ago and which the harvest kept lifting because the bullet was still in the list. Struck
  in ADR 0078 as part of this wave, since this is the wave that read the polar divergences.
