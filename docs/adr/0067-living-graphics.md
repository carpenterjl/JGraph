# ADR 0067 — Living graphics

## Status

Accepted (M67).

## Context

Every milestone from M61 to M66 was language or numerics: comma-separated lists, function files,
string arrays, time, struct arrays, sparse orderings. The graphics surface had not moved since M60,
and what M60 left behind was a specific and unusual kind of debt — **things that were finished except
for the part a person can see**.

The animation seam is the clearest case. M60 wrote `comet`, `comet3`, `movie` and `streamparticles`
against a `ScriptAnimation.SetPlayer` seam that nothing ever filled, so every one of them drew its
finished picture and stopped. The tests passed; the stress script passed; a comet did not move. Two
IPT names had been recorded as blocked since M46 for a related reason: `warp` needed a surface that
could carry a picture, and the fan-beam trio needed nothing at all except the work. And the
handle-graphics section of the coverage document listed thirteen names — `animatedline`, `rectangle`,
`groot`, `hggroup`, `hgtransform` and the rest — that are not verbs at all but the *objects* a figure
is made of.

The characteristic failure of a milestone like this is building the visible half and leaving the
model half implied: a player that special-cases each verb, a rectangle that is really an annotation,
a group that is a name with nothing behind it. Each of those looks right in a screenshot and is wrong
the moment a script asks a question about it.

## Decision

### The player is a seam the host fills, and its absence is an answer

`FigureAnimationPlayer` opens the figure, applies each step on the UI thread, waits at render
priority so the frame is actually painted, and sleeps in slices between steps. It ends early on two
conditions and both are answers rather than failures: **closing the figure window** retires the
figure so its number stops resolving, and **pressing Stop** cancels the run, which the player checks
between steps rather than making the user wait out the motion.

Called on the UI thread it answers false instead of blocking, because there would be nothing left to
draw with — the same answer a batch run gets. That is what keeps the two worlds honest: a verb's
correctness never depends on whether a player exists, and every animated verb still leaves its
finished picture behind.

The tests install a **recording player** rather than a window. That is what makes the step machinery
— the one part a headless run never exercised — checkable at all: the steps run, the counts and pace
are asserted, and what a real window adds on top is timing, which is the live check.

### A movie draws its frames

`movie` used to build a list of empty steps: with a player it waited, and without one it did nothing
whatever. It now decodes its frames, draws the last of them into the figure, and replays the lot when
something can show them. This is the order every other animated verb here already used — draw the
finished picture, then replay how it got there — and applying it to `movie` means a batch run gets a
real answer from it for the first time. Two consequences are deliberate: `movie` now clears the
figure it plays in, and frames of different sizes are refused rather than stretched.

Decoding the frames also surfaced a defect older than this milestone. `[f g]` is **one struct array
of two frames**, not two values, and the frame counter written before struct arrays were real read it
as a single frame and quietly played the first of them. Nothing failed; a two-frame movie was a
one-frame movie.

### `MaximumNumPoints` does not earn a model class

An animated line is an ordinary line plus one integer. A model class for it would mean a rendering
case, a serialized form and a `.graph` version bump to carry that integer, so the cap lives in a
`ConditionalWeakTable` in the script layer instead. The consequences are recorded rather than hidden:
`get(h, 'Type')` on an animated line answers `'line'`, and a figure saved and reloaded keeps the line
and forgets the cap.

`rectangle` went the other way and is a **patch**, because a rectangle with a curvature is a rounded
polygon and a patch is exactly a polygon with a fill and an edge. It draws in the data's own
coordinates, which is what tells it apart from the `annotation('rectangle', …)` this build already
had: that one is placed on the figure and stays put when the axes are zoomed.

### A group is beside the render tree, not in it

`hggroup` and `hgtransform` could have been container plot objects. That would have meant getting
five surfaces right — rendering, hit-testing, autoscale, the plot browser, and the saved figure — for
a container that draws nothing. What a script actually does with a group is show and hide its members
together and move them together, and the members already know how to do both.

So a group is a book of who belongs to it, and a transform moves its members' own coordinates. To
stop that compounding, the group **remembers what each member looked like when it joined** and
re-derives from that every time the matrix is set, which is what makes `set(t, 'Matrix', …)` in a
loop an animation rather than a drift. Recorded limits: a group does not clip or z-order its members
as a unit, a member reads back its transformed data rather than its original, and a saved figure
keeps the members and forgets the grouping.

Members join through **`set(h, 'Parent', g)`**, which meant making `Parent` writable in the property
table. That is one mechanism for every drawn object at once, where MATLAB's `plot(…, 'Parent', g)`
would have had to be taught to each drawing verb separately. The divergence is one line of script,
and `Parent` now also moves a plot between axes, which is worth having on its own.

### A texture is a different answer to a question the renderer was already asking

`SurfacePlot.Render3D` asks `Palette` for one colour per grid vertex or per cell. Texture mapping is
that method returning the picture's colours instead of the colormap's — about fifteen lines, and no
new drawing path. `warp` samples the picture at the grid's own resolution, nearest neighbour, so a
small picture on a fine grid shows its own pixels rather than a blur of them.

Four years of "waiting on the renderer" turned out to be waiting on one method. That is worth
recording precisely because the estimate was so far out: the block was real when it was written and
stopped being real when M45 gave the surface a per-vertex palette, and nobody went back to check.

### The fan-beam family is one rebinning used twice

A fan ray leaving a vertex a distance `D` from the centre, at fan angle `γ` when the fan has turned by
`β`, is the same line through the object as the parallel ray at angle `θ = β + γ` and distance
`s = D·sin γ`. That single relation is the whole implementation: `para2fan` reads a parallel sinogram
where the fan rays fall, `fan2para` reads a fan sinogram where the parallel rays fall, `fanbeam` is
`radon` then the first, and `ifanbeam` is the second then `iradon`.

Integrating along fan rays directly would be a little more accurate. The rebinning was chosen because
it is **exactly** the identity above, so a script can check it: `fan2para` of `para2fan` is the
sinogram it started with, up to interpolation. That is a stronger claim than a tolerance on a
reconstruction, and the test makes it on a smooth object deliberately — on the head phantom the same
test would be measuring how sharp the skull is rather than whether the rebinning is an identity.

`'minimal'` coverage is refused by name: it sweeps a different set of angles, not a subset of a full
cycle, and half of it would be worse than none.

### One-argument log plots

`semilogx(y)`, `semilogy(y)` and `loglog(y)` count along the whole numbers exactly as `plot(y)` does.
The one ambiguity the shorter form introduces — is the second argument the y data or a line spec? —
is settled by its type rather than by counting, so `semilogy(y, 'r--')` still works.

## Consequences

**Eighteen new names.** Thirteen are documented MATLAB builtins (`animatedline` `addpoints`
`getpoints` `clearpoints` `rectangle` `axes` `groot` `reset` `waitfor` `hggroup` `hgtransform`
`frame2im` `im2frame`) and five are Image Processing Toolbox names (`fanbeam` `ifanbeam` `fan2para`
`para2fan` `warp`). The builtin table moves to **412 of 514**, the IPT table to **270 of 409** — which
closes both entries M46 recorded as blocked — and the across-every-kind total to **919 of 2,027**.

**A coverage correction rides with it.** Re-deriving the builtin count instead of adding to it showed
that the figure had read 386 since M63 and should have read 399: M66 implemented all ten sparse
orderings and all three of `qz`/`ordqz`/`balance` without moving any of them out of the missing
tables. Both sections are corrected here. The remaining gap between the computed total and the prose
groupings is recorded rather than guessed at.

**What the milestone found that was not its own.** Three things, and all three were invisible:

- **A two-frame movie played one frame**, because `[f g]` is a struct array and the frame counter
  predated struct arrays being real.
- **`makehgtform` already existed** (M54, with `axisrotate`, which the one written here did not have),
  and the duplicate registration was dead code the moment it was written. The CLI probe caught it by
  printing a refusal message that named an option the new code had never heard of.
- **The `warp` block had expired.** M46 recorded it as needing something the renderer could not do;
  M45 had already made that possible.

**Recorded limits.** A group is not in the render tree (no clipping, no z-order, not saved). An
animated line's type is `'line'` and its point cap is not saved. `waitfor` returns at once, because a
script run has nobody in it to wait for. `'minimal'` fan coverage is refused. `warp` lays its picture
on a rectangle, not on a parametric grid.

**Live checks for the user**, which batch cannot see:

- Run `stess_39.m` from the Script Workspace with F5 and watch section 3: the comet should *travel*
  along its curve rather than appear finished, and the movie should show its frames in turn.
- Press Stop during the comet and confirm the run ends promptly rather than after the animation.
- Close the figure window mid-animation and confirm the script finishes rather than erroring.
- Look at the warped surface in section 8 and confirm the picture is on the surface rather than a
  colormap of its heights.
