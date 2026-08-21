# ADR 0060 — Figure tooling, files, and motion

## Status

Accepted (M60, 2026-08-13). The last milestone of the M52–M60 arc and the last one with graphics
names left to take. `.graph` stays at version 6: two annotation properties and a text annotation's
optional box are defaulted fields, which the format has taken additively since M45.

## Context

Thirty-one names, and almost none of them draw. They work on what the other fifty-nine milestones
drew: they hang a script's own data on it, keep two objects in step, write it to a file, read it
back, turn it into pixels, and move it about.

That makes this the one milestone whose verbs can check the rest of the build, and the design below
leans on that. It also makes it the milestone with the least new arithmetic and the most decisions
about *what an answer is* when the thing a verb wants — a window, a clipboard, a click — is not
there. Every one of those decisions is in the "Answers without a window" section, because taken
together they are what makes the whole family usable from `-batch`, which is where the stress gate
and every scripted run live.

## Decision

### The denominator was wrong about what it was counting, and that is the finding

This milestone opened with the audit the last four milestones' corrections had earned, and it found
something different from what those four found. Their errors were arithmetic — a name in a list but
not in its count. This one was a wrong description: `docs/matlab-builtin-coverage.md` said its
denominator was the graphics functions under `graph2d`, `graph3d`, `specgraph` and `graphics`, and
those four folders hold **264** documented functions, not the 267 the file claimed.

The lists were never wrong. They had always also counted `legend`, `colorbar`, `bubblelegend` and
`annotation`, which MATLAB documents under `matlab/scribe`, and the eight plot-tool verbs, which it
documents under `matlab/plottools`. **Six folders, not four: 277.** Of those, 246 are implemented
after this milestone and 31 are excluded with reasons.

**The lesson is one level up from the previous four.** Counting the names in a list catches a slip in
the count. Only re-deriving the set from the source catches a slip in what the list is *of* — and
that error is the more dangerous one, because it survives every check that trusts the list. The query
is four lines against the dump's data island and is written out in the coverage file.

The audit also found eight names this file had never recorded in either column: `exportgraphics`,
`copygraphics`, `movie`, `hgexport`, `exportapp`, `rendererinfo`, and the two toolbar-button verbs.
Seven of the eight are implemented here.

### Answers without a window

Five verbs here want something a headless run has not got. Each was decided separately and they came
out four different ways, which is the point — "do nothing" is not a policy, it is one of four
answers, and the right one only twice.

- **`comet` and `comet3` draw the finished curve, then replay it if anything can show the replay.**
  The drawing happens first and unconditionally. That order is the whole design: the curve is what a
  saved figure holds and what a batch run was asked for, and the travelling is something a window
  adds. The replay's last step rewrites the whole curve, so a cancelled or unplayed animation is
  indistinguishable from a finished one.
- **`movie` does nothing.** It is the one verb here where that is right: it shows pictures already
  taken, and there is nothing in the figure for it to change. It still *reads* its frames and refuses
  anything that is not one, so a script that hands it the wrong thing hears about it in batch.
- **`copygraphics` asks the host and accepts "no clipboard here" as an answer.** The seam returns a
  bool rather than throwing, because a headless run that copies a figure has done everything it can
  be asked to do.
- **`pan` and `datacursormode` remember the word and answer it back.** JGraph's window pans on a drag
  and shows a data tip from its own toolbar whatever a script says, so the mode is bookkeeping. A
  script that sets a mode and reads it back gets what it set; a recorded divergence from MATLAB,
  where the mode really does change the pointer.
- **`gtext` refuses, and names `text` and `annotation`.** This is the one that could have gone the
  other way, and putting a label at the middle of the axes would have been worse than refusing:
  it would put a label somewhere the script did not ask for and never say so.

The seam itself is deliberately the smallest thing that serves all of them — a list of steps and a
pace — so it knows nothing about lines or frames, which is why `streamparticles` uses it without the
seam learning about streamlines.

### `getframe` is a `uint8` array, which is what makes the milestone able to check itself

MATLAB's `cdata` is a height-by-width-by-3 array of `uint8`, and the first draft here made it this
build's image value instead. That was wrong for one reason: **a frame is something a script does
arithmetic on.** `double(f.cdata)` to difference two captures is the whole reason to have `getframe`
headless, and an image value would have needed every numeric verb to learn about pictures. It is a
plain array now, carrying the `uint8` class tag M47 built, and it still goes into `imshow` and
`imwrite`, which read an array as readily as an image.

What that buys is the check `stess_32.m` sections 6, 7 and 28 make: **a figure written to a file and
read back is proved equal to the original by comparing what the two draw, not by comparing the two
files.** Two documents can differ in whitespace or field order and be the same figure; a field the
writer forgot leaves the files looking fine and the pictures different. That check would have caught
a serialization gap in any of the six versions this format has had, and it exists now because the
capture goes through the same renderer the screen and every export use.

### `annotation` owns one conversion and nothing else

Every annotation kind already existed — M4 built them for the editing surface, M26 made them
first-class, and `AnnotationSpace.Figure` has meant normalized figure coordinates since then. So this
verb is a reader for MATLAB's argument shapes plus **one conversion: MATLAB measures y upwards from
the bottom of the figure and this model measures it downwards from the top.** That flip lives in one
function, applied on the way in by the verb and on the way out by the property table, so a script
never sees the model's origin. It is its own inverse, which is why reading and writing a position
both call it and neither needs a second spelling.

Two model additions were needed and both are small. An arrow gained a second head and a label, which
is `doublearrow` and `textarrow`; a text annotation gained an optional box, because a textbox that
ignored the size it was given would be a visible wrong. **MATLAB mints a distinct object for each of
those, and here they are one object whose properties say which it is** — the same reading as the
stairstep line M55 recorded, and `get(h, 'Type')` answers `textarrow`, `doublearrow`, `line`,
`textbox` accordingly.

### `linkprop` listens to the tree, not to the object

The obvious implementation subscribes to each linked object's `PropertyChanged`. It silently never
fires, and finding out why is worth recording: **the properties worth linking mostly do not live on
the object they are named on.** An axes' `XLim` is a range on its x ruler, so setting it raises a
change on the ruler, which an axes-level `PropertyChanged` never hears.

Invalidation bubbles up the tree, so the axes does hear it — but invalidation says *something*
changed and not *what*. So the link remembers what it last saw each object holding and treats
whichever no longer matches as the origin. That doubles as the re-entry guard: after a copy every
object holds the value the link now remembers, so the invalidations the copy itself raises find
nothing to do.

### `rotate` is arithmetic, and finding that out cost a property

`rotate` reads as an interaction and is not one: it turns a plot's own data about an axis through a
point, which is Rodrigues' formula and nothing else. Writing it found that **`XData` and `YData`
could be read and not written** — the sixth time this file's family has carried half of a property,
after `Faces` in M58 and `Vertices` in M59.

This one costs the most of the six, because moving a series by writing its data is the ordinary way
to redraw one without drawing it again, and it is what MATLAB scripts do constantly. Writing either
coordinate keeps the other, since a series is held as a pair; writing one of the wrong length says so
rather than silently truncating.

## Consequences

`docs/matlab-builtin-coverage.md` moves from **215 to 246 of 277 documented graphics functions**, on
the corrected denominator described above. **All 31 that remain are excluded with stated reasons**:
nine plot-tool and toolbar verbs that JGraph's browser and inspector already are, eight geographic
names that need a basemap service, six print and app-building dialogs, four chart containers whose
content is a layout algorithm, three legacy machine verbs, and `rbbox` and `refreshdata`.

**Nothing in the graphics surface is waiting on a milestone, and nothing left in it draws.** The four
names ADR 0059 and M54 recorded as waiting on this one are all done: `gtext` refuses by name,
`comet` and `comet3` draw, and `streamparticles` and `interpstreamspeed` came with the animation seam
exactly as ADR 0059 said they would.

**Five verbs are accepted no-ops and recorded as such**: `disableDefaultInteractivity`,
`enableDefaultInteractivity`, `enableLegacyExplorationModes`, `addToolbarExplorationButtons` and
`removeToolbarExplorationButtons`. A script that begins by turning an exploration mode off should
still run, which is why these answer rather than refuse.

**The IPT ride-alongs did not land, and this is the one thing M60 was scoped for and did not do.**
`warp` needs a texture-mapped surface, and `SurfacePlot` still has no texture — the same reason
ADR 0049 recorded, unchanged, so it is re-recorded rather than pretended away. The fan-beam trio
(`fanbeam`, `ifanbeam`, `fan2para`) is rebinning over the implemented `radon`/`iradon`: real
numerical work with its own geometry to get right, and it is separable from everything else here.
Both are a short follow-on rather than a milestone, and neither is in the graphics count above.

`stess_32.m` is the live check, and its twenty-eight sections found four things — `f = getframe`
answering the verb instead of a frame, `linkprop` never firing, `XData` unwritable, and `double()`
refusing an image, which is what turned `cdata` into an array. Three of the four were real defects in
this milestone's own code and the fourth was a gap it exposed.

### Recorded divergences

- **A `.fig` holds this build's own `.graph` document.** The extension a script writes is `.fig` and
  `openfig` and `hgload` read it back, but what is in it is JSON rather than a MAT-file of handle
  objects, which is a format for a different program's object model.
- **`openfig`'s `'reuse'` behaves as `'new'`.** Every load registers a new numbered figure. Pretending
  a window exists in a run that has none would be worse than the divergence.
- **`exportgraphics(ax, …)` exports the whole figure** rather than cropping to the axes. The option
  words are all read and checked for spelling; only `Resolution` changes anything, and only for a
  raster format.
- **Application data does not survive a save.** It is the script's own bookkeeping and has no business
  being drawn or serialized, which is also what lets it hold a handle.
- **`linkprop` answers a number rather than a listener object.** The link is alive because it exists.
  Storing the answer in appdata is still the right habit for a script meant to work in both.

**Retired since:** the `alim`/`alphamap` divergence this ADR recorded — that transparency is a number on an object and the mapping does not exist — was closed by M74 (ADR 0074), which built the mapping and drew surfaces and images through it.

## Live checks for the user

Batch cannot see any of these, so they are listed rather than claimed:

- **The animation seam has no host player yet.** `ScriptAnimation.SetPlayer` is the seam and nothing
  installs one, so `comet` draws its curve and `movie` does nothing in the app exactly as in batch.
  Wiring a dispatcher timer in the figure window is the follow-on that makes them move; until then
  the verbs are correct and still.
- An `annotation('textarrow', …)` over a real plot, to see whether the label sits where a reader
  expects relative to the arrow — the placement rule here is "on the far side of the tail from the
  tip", which is a judgement the eye settles.
- `annotation('textbox', …)` with a background and a border, at a size the text does not fill and at
  one it overflows. The box is the size given rather than the size of the text, which is MATLAB's
  behaviour with `FitBoxToText` off and is the opposite of what every other text in this build does.
- `linkprop` over two axes in two open windows, panning one with the mouse. The mirror is driven by
  invalidation, so it should follow a drag and not only a scripted `xlim`.
- `copygraphics(gcf)` in the app, then paste into another program. This is the one verb whose whole
  behaviour is the host's, and batch can only prove it does not crash.
