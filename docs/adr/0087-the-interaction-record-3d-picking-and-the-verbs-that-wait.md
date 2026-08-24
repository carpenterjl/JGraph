# 0087 — The interaction record, 3-D picking, and the verbs that wait

Date: 2026-08-24 · Milestone: M87 · Status: accepted

## Context

The capability report carried one row here, quoting ADR 0071: *"Window-level mouse & key callbacks
(`WindowButtonDownFcn`, `KeyPressFcn`, …) and 3-D picking — named in ADR 0071 as the natural core of
an interaction wave. M72 went to parity instead, so this is still the next one."*

Probed before a line was written, **most of the row was already false**:

    WindowButtonDownFcn      writable      CurrentPoint       read-only
    WindowButtonUpFcn        writable      CurrentCharacter   read-only
    WindowButtonMotionFcn    writable      SelectionType      read-only
    WindowScrollWheelFcn     writable      CurrentObject      read-only
    KeyPressFcn              writable
    KeyReleaseFcn            writable
    WindowKeyPressFcn        writable
    WindowKeyReleaseFcn      writable

M75 built the whole family — all eight callbacks, both `CurrentPoint`s, the coalescing queue — with
`WindowEventCallbackTests.cs` testing every one. ADR 0071's bullet *"The window-level callbacks are
absent"* stood for **twelve milestones** after it stopped being true, and the gaps page quoted it as
current for all of them.

**That is the fifth expired block this arc has found by re-reading one**, after `warp` in M67,
`MException` in M68, `Interactions` in M80 and `sqrt`/`log` in M81. The pattern is stable enough to
state as a rule: *a recorded limitation with no test on either side of it is the shape to look for.*
Nothing contradicted this one because nothing had to — the sentence lived in a divergence list, the
list is harvested into an index, and the index is read as current.

Two things in the row were genuinely missing.

**3-D picking.** No plot type in space implemented a hit test, so a click on a `surf` resolved to the
axes and a `ButtonDownFcn` on the surface never fired. The camera needed to do it has been built on
every click since M75 — `FigureHitTesting` already constructs a `Projection3D` for the axes'
`CurrentPoint`. The picking simply never asked it.

**The verbs that wait.** `waitforbuttonpress` and `ginput` did not exist; bare `pause` was refused.
ADR 0071 gave its reason as *this build has no key routing to the interpreter* — which M75 also made
false. And `waitforbuttonpress` sat in the coverage document's not-implemented list under the same
ground, *waits for a person*. Two documents, one expired premise, three verbs.

## Decisions taken before any code

1. **Picking through a camera goes one way only.** `ICoordinateMapper` answers both directions
   because a flat mapper can; a camera cannot, since a pixel names a whole line of sight — which is
   exactly why the axes' `CurrentPoint` is *two* points. So `ISpatialMapper` projects and does not
   unproject, and picking works **forward**: draw each candidate where the renderer drew it and
   measure on screen. That is also the only way to get it right, because what a click lands on is
   decided by the picture and not by the data.

2. **`HitTest3D` is its own seam beside `HitTest`, not an overload of it.** Two methods taking two
   different mapper interfaces would resolve by argument type, which is a silent way for a call site
   to pick the wrong one. A separate name makes the branch in `Resolve` visible.

3. **A filled shape is picked by its inside.** A surface and a patch are tested for containment first
   and nearness to the outline second. A `slice` plane that could only be clicked on its edges would
   be the wrong answer for the commonest thing anybody clicks on.

4. **The hit carries how near the camera it was, and `Resolve` reads it.** Distance alone cannot
   order two overlapping faces — a click inside both is dead centre of both. `PlotHitResult` gained
   `CameraDepth`, and the nearer wins within a one-pixel tie band. **On a flat axes the depth is NaN
   and every comparison against NaN is false**, so a flat figure keeps the first-found rule it has
   always had; two flat objects under one pixel have no "in front", and inventing an order for them
   would change behaviour this milestone has no business changing.

5. **The waiting verbs read their own record, not the callback queue.** That queue only ever holds an
   event some object has a callback for — which is what makes an unscripted window cost nothing. A
   verb waiting for a key must hear the key with no `KeyPressFcn` anywhere, so `ScriptInputWatch`
   sits beside the queue and records every press unconditionally. It is a **counter**, not a flag,
   because the question is "has anything happened *since I started*"; a flag would let a press from
   before the call release the wait, and clearing one first would race the interface thread.

6. **The test for a window is `ScriptEventQueue.PumpInstalled`.** The same question `waitfor` has
   always asked, rather than a second host seam saying the same thing. Where no pump is installed
   each verb refuses by name and says which verb does the job without waiting — M60's answer, M84's,
   and what keeps `jgraph.exe -batch` and the 59-script gate free of a verb that would wait forever
   for somebody who is not there. It is also why the one-hour cap inside `WaitForPress` is
   unreachable in a batch run: the refusal comes first.

7. **`pause`'s switch is not a wait, so it works everywhere.** `pause('on'|'off'|'query')` needs no
   window, and each of the three answers the state *as it was before the call* — which is what makes
   `old = pause('off'); … pause(old)` put back whatever was there rather than guessing `'on'`. With
   pauses off, the bare form returns at once rather than refusing: a script that turned pauses off
   should not then be stopped by one.

8. **`pause` does not gain `AutoCallsBare`.** Its documented bare form is a *statement*, and a
   statement already calls. Opting in would change what `x = pause` means in **JGS**, whose surface
   is frozen and where this would be neither additive nor dialect-gated. `waitforbuttonpress` and
   `ginput` do opt in, because `w = waitforbuttonpress` is the documented spelling and both are new
   names with no JGS history to break.

## Found by probing rather than by reading

- **The expired block above**, which is the largest finding in the milestone and reduced its planned
  scope by most of a wave.
- **`@() pause` inside a function handle binds the verb rather than calling it**, found when
  `stess_59.m` §4 failed. That is ADR 0071's *oldest* recorded divergence working exactly as
  recorded, not a new fault — and the sixth time this arc has met the bare-name rule. The script says
  `pause()` and says why.

## Verification

- 0 warnings in Release and Debug; **5,291 tests** (5,266 → +25); **59 of 59 stress scripts**,
  including the new `stess_59.m`, which passed eight of nine sections on its first run and the ninth
  once its own `@() pause` was corrected.
- **All callables 925 → 927**; **builtins 415 → 416** with the handle-graphics not-implemented
  section going 8 → 7 and the missing total 99 → 98. `verify-builtin-coverage.py` caught every one of
  those four numbers before the prose was told, which is now **four waves running**.
- **Syntax forms: accepted unchanged at 1,344; the denominator grew by one to 2,454**, and the extra
  form is `waitforbuttonpress`'s, recorded `unprobed` for cause. `ginput` is documented as a
  `function` with no graphics flag, so it is outside the form population entirely and moves nothing.
  **Neither is folded into success or failure.**
- **`pause`'s two headless forms stay `unprobed`**, because the prober's skip list is by *name* and
  `pause` is on it for its bare form. That understates what the build does rather than overstating
  it, which is the right direction for a skip list to err, and it is recorded here rather than fixed
  by loosening a guard that exists to stop a probe hanging.
- Property counts unmoved at 1,569 of 1,585; the other three verifiers OK.

## Divergences recorded

- **Picking in space measures on screen, not in data space.** Two objects the same distance from the
  pointer are separated by which is nearer the camera; below a one-pixel difference the camera
  decides outright. MATLAB's rule is the topmost drawn object, which for a painter's-algorithm
  renderer is the same answer by a different route — but the tie band is this build's own number.
- **A flat axes keeps first-found on a tie.** The depth ordering reaches only objects drawn through a
  camera; two flat objects under one pixel resolve to whichever was added first, as they always have.
- **`ginput` ends on any key, where MATLAB ends on Enter.** MATLAB's bare form collects until Return;
  here any key ends it, and the key is not one of the points. The counted form ends early on a key
  too, which is MATLAB's behaviour.
- **A wait gives up after an hour.** MATLAB's waits are unbounded. A wait that outlived the session
  would be indistinguishable from a hang, so this one refuses by name instead — unreachable in a
  batch run, where the absence of a window refuses first.

## What is not done

- **No `WindowState`, `Pointer` or `PointerShapeCData`.** The window-event family is complete; the
  window *appearance* properties it sits beside are not, and they are a figure-property wave rather
  than an interaction one.
- **`selectmoveresize` and the `ui*` widget family** stay on the not-implemented list, and their
  ground has not expired: they are app building, and this build has no widgets to build with.
- **Picking ignores `PickableParts 'all'`**, exactly as the flat picking does — the shared hit test
  never sees invisible objects, which is ADR 0071's own recorded divergence and unchanged.
