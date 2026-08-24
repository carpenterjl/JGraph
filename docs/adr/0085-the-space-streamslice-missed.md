# 0085 — The space streamslice missed, and the surface slice was never cut along

Date: 2026-08-24 · Milestone: M85 · Status: accepted

## Context

Two rows stood on the capability report's gaps page since M72, under one heading: *"`streamslice`'s
three spatial forms; `slice` over a slicing surface"*. They are the forms that milestone did not
reach, and they had sat unexamined for thirteen milestones.

Probed before a line was written, they turned out to be two different kinds of gap:

- **`streamslice` refused.** `ArityRange("streamslice", args, 2, 8)` against nine arguments, so every
  spatial call said *"streamslice expects between 2 and 8 argument(s), but got 9."* Eight of the
  verb's ten documented forms read `error` in `form-probe-results.csv`; only the two plane forms
  worked. A refusal is the honest failure — the script learns immediately.
- **`slice` did not refuse.** Handed `slice(X, Y, Z, V, XI, YI, ZI)` with three 6×6 matrices, it read
  them as three *plane lists* — thirty-six positions each — and drew **108 axis-aligned patches**.
  The form probe records that signature as `accepted`, and has since M72. This is the third time this
  arc has met the class ADR 0077 named: a coverage table counts served names, and cannot count a name
  served wrongly.

## The defect underneath, which was neither of them

The first test written against the new arrows failed with *"only 1 lines were drawn."* The probe had
said eight handles; the axes had one child.

**Every verb in this family drew one piece.** `DrawLines` sent each traced line through `JG.Plot3`,
and a plotting verb with `hold` off calls `PrepareAxes`, which clears the axes first. So a slice
cleared itself once per line and kept the last. The same shape was in `streamline` with several
seeds, in `contourslice`, in `streamribbon` and in `streamtube` — five verbs, since M59.

Nothing noticed because **every handle came back, and every one was live**. `numel(h)` was right;
only the picture was wrong. `stess_44.m` §8 asserts `numel(hs) >= 2` and that the first and last
handles share a colour — both true of an axes with one line on it and seven orphans beside it. Even
M72's own finding here (*"each streamline took the next series colour, so a twenty-line slice was a
plaid"*) was measured through the handles: the colours were being fixed on lines that were not on
the chart.

The rule this leaves is short enough to keep: **`numel(h)` is not a picture.** `stess_57.m` §4
therefore counts `get(gca, 'Children')`, which is the thing a person would see.

## Decisions taken before any code

1. **The volume form's trailing triple names planes, not starting points.** It is the same three
   lists `slice` takes, any of which may be `[]`. That is what makes this verb a slicer rather than
   another spelling of `streamline`, and it settles the whole implementation: the tracing happens
   **inside** the plane, on a flat field built from the two in-plane components, because a line traced
   through the volume would leave the plane immediately and the picture would stop being a slice.

2. **The form is settled by counting.** Every argument here can be a matrix and none can be told
   apart by looking. Two or four is a plane, six or nine is a volume, and the odd counts between are
   those same forms with a density after them; eight is nothing and says so. Trailing *words* are
   taken off first and are not part of the count.

3. **Arrows are drawn by default, in both dialects.** This is MATLAB's own default and the only
   change in this milestone that alters a picture JGS already drew. **The JGS output freeze was
   lifted for it by the user's explicit authorization**, recorded here the way the frozen-script
   amendments of M74, M76 and M82 are. The alternative — a dialect gate — would have left the two
   dialects drawing different slices forever for the sake of a default nobody had chosen on purpose.

4. **One arrowhead per streamline, lying in the slice's own plane.** The barbs are found by turning
   the line's direction a quarter turn about the plane's normal, so a head is flat against its slice
   however the slice faces. A head is drawn as a line object in the slice's shared colour, which is
   what keeps a slice one drawing rather than a tangle of lines with differently-coloured tips.

5. **`[verts, averts] = streamslice(___)` draws nothing.** The arrangement `stream2` and `stream3`
   already have beside `streamline`, one verb further along: tracing and drawing are separate, and
   asking for the numbers is asking not to draw. The width of a vertex table says which world it came
   from — two columns over a plane, three through a volume.

6. **Both of the verb's doors peel a leading axes handle.** `OnNamedAxesOutputs` is new beside
   `OnNamedAxes` for exactly this: a verb needs both wrappers or neither, or `streamslice(ax, …)` and
   `[v, a] = streamslice(ax, …)` would mean different things.

7. **A slicing surface is told apart by shape.** A plane list is a scalar, a vector or an empty; a
   surface is three matrices with more than one row *and* more than one column each, which no plane
   list ever is. Mismatched sizes are refused by name — three coordinates of one lattice is what the
   form means, so a ragged triple is a mistake rather than a shape needing interpretation.

8. **The surface and the plane share one patch builder.** `SampledPatch` takes a lattice of points
   and colours them by what the volume reads at each. That is what makes *a surface lying in a plane
   draws what the plane form draws* true by construction rather than by coincidence, and `stess_57.m`
   §9 pins it as a byte-identical export.

## Found by probing rather than by reading

- **The orphaning above**, which is the largest thing in this milestone and was not in its plan.
- **`slice`'s 108 patches**, which the plan predicted as "would be read as a pile of scalar planes"
  and which turned out to be exactly that, drawn.

## Verification

- 0 warnings in Release and Debug; **5,247 tests** (5,226 → +21); **57 of 57 stress scripts**,
  including the new `stess_57.m`, which passed all twelve sections on its first run.
- **Syntax forms 1,336 of 2,453 → 1,344.** All eight are `streamslice`, which goes from 2 of its 10
  documented forms to **10 of 10**. The only other movement in `form-probe-results.csv` is the
  prober's own temporary-directory name inside message text — prober-caused, not code-caused, and
  split out here because the two are not the same measurement.
- **No count moves in `matlab-builtin-coverage.md`**, and that is the honest report: both names were
  already implemented and already counted. What changed is what they do, which only a test can see.
  All four verifiers OK.
- `stess_44.m` §7 and §8 were run in full **before** any code was written and again after, and both
  pass unchanged.

## Divergences recorded

- **`streamslice` draws one arrowhead per streamline, halfway along it.** MATLAB spaces its arrows by
  the same density that spaces the seeds, so a long line there may carry several; here a line carries
  one, at its midpoint. The direction each says is the same.
- **The interpolation-method word is checked and then read linearly.** `linear`, `nearest` and
  `cubic` are all accepted and all trilinear — the stance `slice` beside it already takes, so a
  script asking for `'cubic'` learns the word was understood rather than learning nothing.
- **A slicing surface is drawn as a patch, as the plane form is.** MATLAB returns surface objects
  from `slice`; this returns patches, which is M72's decision and unchanged — two of the three plane
  orientations stand vertically, where a surface's height over the x-y plane does not exist.

**Not a divergence, and kept outside the list so the harvest does not lift it as one:** arrows now
appear in JGS-drawn slices as well as MATLAB-drawn ones. That is a deliberate lift of the JGS output
freeze, authorized by the user for this change alone; every other change in this milestone is
purely additive.

## What is not done

- **`streamribbon` and `streamtube` still error on their ungridded forms** — `streamribbon(U, V, W,
  startx, starty, startz)` reads its arguments as a grid it does not have. It is the same
  count-versus-look ambiguity M85 settled for `streamslice`, in two verbs it did not touch, and it is
  a clean small wave of its own.
- **`contourslice` errors on all seven of its documented forms** under the prober, for the same
  reason: `LooksLikeGrid` accepts four numeric arguments as a grid plus a field when the first three
  are plane lists. Its *drawing* is now correct — §4 of `stess_57.m` proves every contour it makes is
  on the axes — but its argument reading is not.
- **No rotated planes through `surf` geometry.** A slicing surface covers the shapes a script can
  build itself, which is the general case; MATLAB's convenience of rotating a `surf` into position is
  not offered.
