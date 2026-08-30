# ADR 0109 — A breakpoint observes a run, and a frame has somewhere to go

Milestone: **M108**
Status: accepted

## Context

A user wrote an ordinary MATLAB script — a sinc surface morphing into a saddle over a hundred
frames, `getframe` in the loop, `VideoWriter` around it — and reported two things. It drew one
static surface and wrote no video. And when a breakpoint was set to find out why, *the script
began throwing errors it had not thrown before*.

The second half of that report is the more serious one, and it turned out to be true.

Three defects and one absence lay behind the report, and they are the whole of this milestone:

- `axis([xmin xmax ymin ymax zmin zmax])` was refused. This is where the script actually stopped,
  on line 17, which is why the surface was drawn and nothing after it happened.
- A surface's `ZData` could be read but not written, so `set(hSurf, 'ZData', Z)` — the whole
  mechanism of the animation — could never have worked either.
- There was no `VideoWriter`, recorded as a divergence by ADR 0072.
- And setting a breakpoint changed what the script meant.

## The breakpoint

A run with a breakpoint anywhere goes under `JgsDebugSession`, and a run without one goes through
the live console session's `ExecuteFileAsync`. Those are two different runners, which is defensible.
What is not defensible is what the first of them did on the way in:

```csharp
JG.Reset();
JgsHandleRegistry.Clear();
DisposePreviousRunBuffers();
```

Those three lines are exactly right for the one-shot run they were written for — `-batch`, which
owns the process. They are exactly wrong for a run nested inside a live workspace, because all three
pieces of state are process-wide and the session around the run is still using them. Its figures are
open, its console variables hold handles into that registry, and those variables point at those
buffers.

And `JgsHandleRegistry.Clear` does one thing more than forget: it rewinds the counter.

```csharp
_next = FirstHandle;   // 1_000_000.5
```

So the first object a debug run drew was handed the number the session's first object already had.
Every handle sitting in the console workspace silently came to mean a different object — not a dead
one, which would at least have said so, but a live one of possibly the wrong type. Measured on the
build before this ADR: a statement that read `FaceAlpha` off a surface succeeded, a debug run of an
unrelated script drawing a *line* happened in between, and the same statement then failed with
"a line has no property 'FaceAlpha'". Same handle number, different object, no warning anywhere.

**A run that is nested inside a live session does not reset what that session is using.** Whether
anyone else owns the process's graphics state is already knowable: a live session installs a
callback dispatcher when it is built and keeps it for its whole life, and the one-shot runner
already reads that dispatcher on the way out so it can put it back. So the same reading decides the
reset, and `-batch` — which finds no dispatcher — is untouched.

The second, quieter half of the same complaint: the one-shot runner's `JgsException` path never
called `ShowTouchedFigures`, where the session's does. A script that drew and then failed therefore
showed its figure on an ordinary run and nothing at all under the debugger. Both paths now keep the
prompt's rule, which is that whatever ran before the error keeps its effect.

## Decision

**A breakpoint observes a run. It does not change what the run means.**

That is the whole of it, and it is worth stating as a rule rather than as a fix, because the failure
mode is a class rather than an instance: any process-wide state the one-shot runner touches on entry
is state a debug run steals from the session around it. Two were found here. The rule is what
catches the third.

## The video

ADR 0072 recorded: *"There is no `VideoWriter`. GIF is now the way a script saves an animation; a
real video container needs a codec this build does not carry."*

That was true of a codec we would have had to ship. It was never true of the two that need nothing
shipped:

- **An AVI is a RIFF file**, and this project can write one itself. Frames are either whole JPEGs
  (which SkiaSharp already encodes, for `imwrite`) or raw DIB rows. That is four of MATLAB's seven
  profiles — Motion JPEG, Uncompressed, Grayscale and Indexed — with no new dependency at all.
- **MP4 goes out through the encoder Windows already has.** `mfreadwrite`'s sink writer is an H.264
  encoder and an MP4 muxer behind six calls, present on every Windows this application runs on.
  Nothing is vendored and no binary is added.

So the divergence is retired rather than worked around, and `getframe` — which has existed since
M72 with nowhere to send its frames — finally has a destination.

The two remaining profiles are JPEG 2000, which no encoder here writes. They are refused by name,
which is the one honest answer available.

### What a writer is

A `VideoWriter` is a struct wearing a class name, like `containers.Map` and the spatial reference
types, and it joins the short list of **handle** classes. It has to: `open(v)` must be visible to
the `v` the caller is holding, or the `writeVideo` after it would report the file was never opened.

The encoder itself cannot live in a value — it owns a file handle and, for MPEG-4, a Media
Foundation session — so the struct carries an id and the *run* owns the encoder, exactly as `fopen`
does. That is also what finishes a video a script forgot to close: the encoder dies with the run
that made it, having written its index and patched its sizes. A half-written container is not a
video, and abandoning one would be the same silent wrongness this milestone exists to remove.

The container is built at the **first frame**, not at `open`, because a container cannot commit to
its headers until it knows the frame size and the frame size arrives with the first frame. MATLAB
refuses a `FrameRate` or `Quality` change once `open` has been called; a struct field here cannot
refuse an assignment, so the settings are pinned at `open` and a later change is caught at the first
frame rather than silently obeyed.

## A defect found beside the road

The AVI header's `hdrl` list length was four bytes short: it counted the `strl` list's header but
not the four-character code inside it. Motion JPEG survived it — MATLAB's reader re-synchronised —
and every uncompressed variant did not, failing with "Unable to read the file" on a file whose
bytes otherwise looked entirely correct.

It is written now as a sum with one term per part rather than as one constant, and there is a test
that walks the file the way a reader walks it: every list says how long it is, and the walk must
land exactly on the end. That test fails on the arithmetic, not on a symptom.

## What this did not close

- **`Archival` and `Motion JPEG 2000`.** Both are JPEG 2000. Refused by name.
- **`axis` with an output.** `v = axis` still answers nothing; only the setting forms are read.
- **Writing `ZData` and a ruler in one `set`.** Each property is written as it is read, so a call
  that resizes the heights and the rulers together fails on whichever comes first. This is not new —
  `XData` and `YData` have behaved this way since M78 — and MATLAB is more permissive, allowing the
  object to be transiently inconsistent and complaining at draw time instead.
- **The three-dimensional tick labels overlap at the box corners.** Seen while checking this
  milestone's own output, present identically before it, and untouched here.

## Consequences

`getframe` is now half of a working pipeline instead of an orphan. The GIF path ADR 0072 built
stays exactly as it is — it is still the right answer for a short animation that has to work
everywhere, and it is the only answer off Windows for anything but AVI.

The handle-aliasing fix is a behaviour change to any host that runs a script inside a live session:
figures and handles now survive a debug run. Under `-batch` nothing changes, because there is no
session to nest inside.

## Divergences recorded

- **`VideoWriter`'s `Archival` and `Motion JPEG 2000` profiles are refused by name.** Both are
  JPEG 2000, which no encoder in this build writes; the other five profiles are complete.
- **A `FrameRate` or `Quality` change is refused at a different moment.** MATLAB's property setter
  refuses the assignment outright; a struct field here cannot, so the settings are pinned at `open`
  and the disagreement is reported at the first frame — as is a `FrameRate` of zero, which MATLAB
  rejects on assignment and this rejects at `open`. Both engines refuse; only the moment differs,
  which is why `stess_67.m` items 21 and 22 pass in both.
- **A `ZData` write that would resize a surface whose rulers were given real positions is refused.**
  MATLAB accepts the write, leaves the surface inconsistent, and warns at draw time; the model here
  keeps its grid consistent, so the refusal happens at the write.
- **A surface still answers `[]` for an unset `FaceColor`/`EdgeColor`** where MATLAB answers
  `'flat'` or `'none'`. Carried from ADR 0072, unchanged, and now asserted in this milestone's tests
  so it stays visible.

## Measured

Against MATLAB R2024a on the same machine, every file written here was read back with MATLAB's own
`VideoReader` and its pixels compared:

| Profile | Read by MATLAB | Frame 1, top band | Frame 1, bottom band |
| --- | --- | --- | --- |
| Motion JPEG AVI | yes | `[253 1 1]` | `[1 1 253]` |
| Uncompressed AVI | yes | `[255 0 0]` exact | `[0 0 255]` exact |
| Grayscale AVI | yes, 1 channel | `76` (red luma) | `29` (blue luma) |
| Indexed AVI | yes, colormap applied | `[0 176 255]` | `[0 0 243]` |
| MPEG-4 | yes | `[255 1 4]` | `[1 1 254]` |

The uncompressed profile is exact, the lossy ones are within their compression, the grayscale luma
is correct to the rounding, and the indexed profile's `jet(256)` survived the round trip. Frame
count, frame rate and duration matched on all five.

The user's own script now writes a 100-frame, 30 fps, 3.33-second MP4 in 4.7 seconds, and every
consecutive pair of frames differs — it is an animation, not a hundred copies of one picture.

On the two forms that already existed in MATLAB, the answers are identical: `axis` with six and
eight elements sets the same `xlim`/`ylim`/`zlim`/`clim`, and `set(h, 'ZData', peaks(20))` on a
`surf(peaks(12))` resizes to 20-by-20 with `XData` counted out to `1:20` — the same as MATLAB, which
was checked rather than assumed. The six-element `axis` renders byte-identically to the four-element
form followed by `zlim`.

MPEG-4 refuses a frame with an odd side, and one with any side of 32 pixels or fewer. Both are the
Windows H.264 encoder's limits rather than this build's, and MATLAB refuses the same sizes on the
same machine for the same reason.

## Testing

`MatlabVideoWriterTests` — 28 tests. The videos are written to real files and their bytes read
back, because the only interesting question about a muxer is whether what came out is the container
it claims to be. Nothing decodes: a decoder would be a second implementation of the same guesses.

`JgsDebugSessionIsolationTests` — 4 tests, written as the user meets the bug: a session runs a
script, is asked about a handle, has a debug run in the middle of its life, and is asked again.
Three of the four fail against the build before this ADR, which was checked by putting the old
behaviour back and running them.

## Live checks

`stess_67.m`.
