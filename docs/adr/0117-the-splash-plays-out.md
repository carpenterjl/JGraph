# ADR 0117 — The splash plays out

## Status

Accepted (M115, 2026-08-30).

## Context

ADR 0115 decided that the splash "loops for as long as loading takes and not one frame longer" and
that "startup is never held back to finish a pass". The reasoning was that a splash is a report on
loading, so it should end when the loading does.

Watching it start says otherwise. The warm-up takes a second or two; the animation is seven and a
half. So the first thing anyone sees of the product is five surfaces part-way through a morph,
cut off mid-motion by a window appearing over them. The animation was made to end on the frame it
began on — a closed loop — and nobody had ever seen it get there.

## Decision

**The splash outlives the warm-up by whatever is left of the pass on screen.** When the shell is
built and its session restored, `SplashWindow.PlayToEndAsync` plays the animation to its last frame
and the shell waits for it.

- **The wait is named on screen.** The caption becomes *Finishing splash animation…* — the one thing
  a progress report must never do is claim to be loading something when nothing is loading.
- **The bar restarts and fills across the finish.** It is charged in the artwork's own time
  (`AnimatedPngReader.Elapsed` against `Duration`), not in frames, so an artwork whose frames run at
  different lengths still reads as time remaining. The bar going back to zero is deliberate: the
  phase it measures changed at the same instant the caption did, and a bar creeping from 0.8 to 1.0
  over seven seconds tells the user nothing about the wait they are actually in.
- **It waits for the pass, not for a pass.** The animation has been looping throughout the warm-up,
  so finishing the pass on screen is what shows every frame, and it is the shortest wait that does.
- **Thirty seconds is the backstop.** The artwork is replaceable (ADR 0115), and a file someone
  drops in `%AppData%\JGraph` must not be able to turn a start into a hang. The backstop is a plain
  `Task.Delay` rather than another dispatcher tick, so a starved timer cannot hold the shell back
  either — and a splash with no animation at all, or one that stopped on a frame it could not
  decode, returns from the wait immediately rather than waiting for something that will never come.

## Consequences

A cold start is now the warm-up plus the remainder of the animation. Measured here: the warm-up
finishes about one and a half seconds in, and the pass takes about seven and a half seconds more —
the animation makes little headway while the loading is competing with it, because its tick is at
`DispatcherPriority.Background` by ADR 0115's own decision and drifts rather than dropping frames.
Shortening the start therefore means a shorter `splash.apng`, which is one number in
`Assets/make-splash.m`, and not a change here.

This supersedes ADR 0115's third bullet. The rest of that decision stands: the animation is still
the ground, still read forwards only, still yields to the warm-up, and a frame that will not decode
still ends it with the last good one standing.

No MATLAB-facing behaviour changed, so this adds no divergence.
