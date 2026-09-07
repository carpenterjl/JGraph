# ADR 0130 — A red stress section is a question, not a verdict

## Status

Accepted (2026-09-07).

## Context

The MATLAB stress suite stood at 61 of 73 when M126 was committed. M125 had left it at 72 of 72, and
the twelve failures were proven to pre-date M126 by building `0acff3d` in a detached worktree and
running them against it, section for section. They arrived with the seven MATLAB-compatibility
commits between the two milestones — `8a777fe`, `1ad513e`, `9aac1ef`, `5b11d2f`, `6b5d45a`,
`68f186b`, `0acff3d` — none of which ran `tools\run-stress.ps1`, added an ADR, or touched the
divergence index.

A red section is not evidence of a bug. The suite is a record of what JGraph did on the day each
section was written, and a section that freezes a divergence goes red when later work correctly
lifts it. Before deciding, the whole suite was run through R2025b.

**That run says the suite is not a MATLAB program.** All 73 scripts were driven through one
`matlab -noFigureWindows -batch` process, a fresh function workspace each, the diary flushed per
script. **Twenty-three ran clean; nineteen finished with `Fail:` lines; thirty-one threw part-way.**
They are written in JGraph's dialect — `a(1).b` chained subscripts, `savefigure`,
`graphics.primitive.Line.empty`, handles that are numbers — so MATLAB is an oracle *per expression*
and not per script. (`stess_43` also hangs there on `waitfor(gcf)`, which its own §11 asserts returns
in batch. It does not.)

Where MATLAB did reach a disputed section it agreed with JGraph every time: it fails `stess_23 §6`,
`stess_36 §8`, `stess_42 §8` and `stess_54 §10`, and it dies inside `stess_50 §20` at the same line
with the same meaning. Every remaining question was settled by running the expression itself in
R2025b.

## Decision

### Eleven sections were wrong, and are corrected to MATLAB's answer

Five causes, each of them a parity improvement one of those seven commits made and no one wrote
down. In every row below, MATLAB R2025b was asked directly.

| What the section froze | R2025b | Sections |
| --- | --- | --- |
| `ax.Title` / `XLabel` / `YLabel` is the string | a `matlab.graphics.primitive.Text`; `strcmp(ax.Title, 'Top')` is **false** there too, and `.String` reads the same on both engines | 23 §6, 26 §13, 27 §18, 29 §12 |
| a marker reads back as the letter it was written with | `plot(…,'rs').Marker` is **`square`**; so is a patch set to `'s'` and a surface set to `'d'` (`diamond`) | 27 §16, 49 §14, 49 §17, 50 §27, 50 §31, 51 §5 |
| `c = contour(Z)` is the chart | the **contour matrix**, a 2-by-N double; the chart is the second output | 50 §20–§22 |
| `mesh` starts with no faces | `FaceColor` is **`[1 1 1]`**, the axes background | 26 §20 |
| `datetime + 1` is refused | **`02-Jan-2024`** — a bare number is a count of days | 36 §8 |
| `char(seconds(1.000001))` shows the fraction | **`1 sec`**; the default format carries no sub-second digits | 54 §10 |
| `imagesc` leaves the grid under the image | `Layer` is already **`top`** after `imagesc` (and `bottom` after `plot`), so setting `'top'` moves no pixels on either engine | 45 §4 |

`stess_45 §4` was measuring `'top'` over `'top'`. It now asserts the default, then measures the move
down to `'bottom'` and back, which is a change worth 4,534 pixels in MATLAB and more here.

`stess_50` had been *dying* at §20 since the contour change, so §21 to §34 had not run since. Two of
them were also frozen against the old marker spelling; both are corrected, and the script now runs to
the end.

### A group lives as long as the axes it was made in

`hggroup` and `hgtransform` sit beside the render tree on purpose (ADR for the group wave). The
handle sweep that reaps objects no longer reachable from a live figure walks that tree — so it never
saw a group, read every group as unreachable, and dropped its handle at **the first `clf` of the
session, whichever figure that `clf` named**. A line or an axes survived; only a group died, and it
died even when its own figure was untouched.

A group now records the axes it was made in, exactly as MATLAB parents it to `gca`, and the sweep
keeps a group whose axes is still live and forgets one whose axes has gone. R2025b was asked for the
whole lifecycle and JGraph now answers it line for line: valid through a `clf` aimed elsewhere, dead
after a `clf` on its own figure, and the same for a transform and for a group that never took a
member.

### A mesh arrives already hiding what is behind it

MATLAB's `mesh` calls `matchBackgroundColor`, which paints `FaceColor` the axes background — so a
mesh is *hidden-on by default*, `hidden off` is the call that takes the faces away, and `hidden on`
puts them back. JGraph had it inverted: a mesh was a wireframe with no faces, and `hidden on` was the
call that added them. `mesh`, `meshc` and `meshz` now paint their faces the axes background.

### `hidden` and `shading` sort surfaces by face colour, the way MATLAB does

Both verbs in MATLAB classify an axes' surfaces before acting, and the test is neither the class nor
the verb that drew them: a surface counts as a mesh when its `FaceColor` is `'none'` or equal to the
axes background. `hidden` moves only those; `shading` gives a mesh a new `EdgeColor` only and leaves
its faces alone.

JGraph's two verbs each had their own test, and both broke once a mesh had faces: `hidden off` turned
every surface in the axes — a `surf` included — into a wireframe, and `shading interp` filled the
mesh and took the mesh away. Both now share `IsMeshLike`, which is MATLAB's test, with one wrinkle
that belongs to this model: a null face colour reads back as `'none'` on a wireframe and as `'flat'`
anywhere else, so the style has to be consulted to tell the two apart.

### `subplot` names the word it could not use

`subplot(2, 2, 3, 'sideways')` was refused with *"options come in 'Name', value pairs"* — true, since
a lone fourth word is half a pair, and no help when the mistake is a word from the wrong vocabulary.
A lone trailing word is now refused by name, and the message says which two words the position takes.
MATLAB quotes the word back too.

## Consequences

- The suite is **73 of 73** again, and `stess_50` reaches its last section for the first time since
  the contour change.
- Four lanes green at **7,672**, up from 7,669: `HiddenPaintsAMeshOpaqueAndBackAgain` became
  `HiddenTakesAMeshesFacesAwayAndPutsThemBack` and gained `HiddenLeavesASurfAlone`, and the group
  lifecycle gained one test per side of the `clf` question. Three tests that pinned the old mesh
  style now pin the new one.
- All 22 `AstraScripts` still pass, including `a14_peaks_surfaces` and `a16_handles_lifecycle`, the
  two paired with the commits this work reaches into.
- A mesh now draws opaque. That is a visible change to every mesh picture, and the right one: it is
  what hidden-line removal means and what MATLAB has always drawn.

## Divergences

- **`waterfall` fills its ribbons with a series colour.** MATLAB gives the patch `FaceColor`
  `[1 1 1]`, the axes background, for the same reason `mesh` gets it — the fill is there to hide the
  rows behind, not to carry a colour. Here it takes the first colour-order entry,
  `[0 0.4471 0.7412]`. The chart hides what is behind it either way; the ink is different, and
  `get(h, 'FaceColor')` answers differently.
- **A `surf`'s default `EdgeColor` reads `'flat'`.** MATLAB answers the RGB triple
  `[0.1294 0.1294 0.1294]` — a fixed dark grey, not a colormap lookup — so a script that reads the
  property back and expects a colour gets a word here. The faces agree: both answer `'flat'`.

## Still open

Neither of these is a difference in what JGraph answers, so neither belongs in the list above.

- **The seven MATLAB-compatibility commits carry no record.** They lifted at least six observable
  divergences — the marker spelling, the text handles, `contour`'s first output, `imagesc`'s `Layer`,
  `datetime + numeric`, `mesh`'s faces — with one-line commit messages and no ADR, which is why the
  suite went red without anyone learning what had changed. This ADR is the record for the six; there
  may be more, and the way to find them is another suite-wide run against R2025b rather than a
  reading of the diffs.
- **`stess_43 §11` asserts that `waitfor(gcf)` returns in a batch run.** It does here; in R2025b it
  blocks until the figure is deleted, which is what hung the reference run. The section is not wrong
  about JGraph, and JGraph's behaviour is the useful one for a script with nobody in it, but the
  comment claiming MATLAB agrees should not be trusted.
