# ADR 0114 — A video with nothing behind it

Milestone: **M112**
Status: accepted

## Context

ADR 0113 let a still be exported onto no page: `exportgraphics(fig, file, 'BackgroundColor', 'none')`
writes a PNG whose background is nothing at all, and `SurfaceLerpTest_5.m` was written against it.
What that script could not do was write a video. It wrote 270 numbered PNGs and left assembling them
to a tool that is not on this machine, and the note in its header said so.

The obvious next step — hand the frames to `VideoWriter` — does not work, and the reason is not a gap
in this build. **Not one of MATLAB's seven profiles carries an alpha channel.** Motion JPEG and
MPEG-4 have none to carry; the uncompressed AVI profile stores three bytes a pixel; the two JPEG 2000
profiles expose one channel or three. A cut-out written to any of them is composited onto something
on the way in, and what is lost is not a property of the picture but its shape.

So there were two ways to give a script a transparent clip, and one of them is not a way at all:
composite the frames onto a colour the script picks, or write a container that keeps the coverage.
The first is the same as not having the feature.

## Decision

### The page a figure does not have

`figure('Color', 'none')` is MATLAB, verified against R2024a rather than recalled — the property
takes the word, and `get` reads back the word rather than a triplet, because no triplet means it. It
was refused here, which is why ADR 0113 had to put transparency on the *export* rather than on the
figure.

It maps to a fully transparent background, which `FigureRenderer` already clears to correctly: the
Skia clear uses Src blend, so a zero-alpha colour clears to nothing rather than to black. Nothing
under the renderer needed changing, which is the same thing ADR 0113 found.

### A capture with no page is four channels

This is where transparency stops being a property and becomes data. `getframe` on an ordinary figure
answers `cdata` as height-by-width-by-3 and still does. On a figure with no page it answers
**height-by-width-by-4**, because with nothing behind the drawing the coverage is the only thing that
says where the drawing is — dropping it does not lose one of the picture's properties, it loses the
picture's shape.

`ImageBuffer` now allows a fourth channel for the same reason and for nothing else: a four-channel
buffer is a capture, not a picture. The image verbs read `Channels` as "one or three" throughout and
are never handed one.

### Two profiles that keep it, listed after MATLAB's seven

MATLAB's seven are unchanged and still first, so a script that only ever names one of them sees
exactly what it saw before. Two are added:

- **`'Animated PNG'`** (`.apng`, and `.png`, which is the name every viewer already knows how to
  open). Lossless, eight bits of coverage, and it plays in any browser. This is the one to reach for.
- **`'Uncompressed AVI with Alpha'`** (`.avi`). The same picture at 32 bits a pixel for an editor.

The APNG muxer is worth a paragraph because it looks harder than it is. APNG is not a new image
format — it is an ordinary PNG with three extra chunk types threaded between the ones a decoder
already knows. So each frame goes to Skia's own PNG encoder and what comes back is taken apart: the
first frame's `IHDR` settles the file, and each frame's compressed `IDAT` payload is re-labelled as
that frame's data. Nothing here compresses anything itself, so a frame matches a still exported by
the same renderer byte for byte, and a decoder that has never heard of APNG sees a plain PNG of the
first frame. Like the AVI muxer it writes forwards and patches backwards: the frame count in `acTL`
is a placeholder seeked back to at `close`, so nothing is buffered to learn it.

The 32-bit AVI is four lines of the muxer it already had. A 32-bit DIB row is on a four-byte boundary
already, so the padding the writer used to insert becomes the coverage byte and the stride does not
change.

### What a profile without alpha is told

A four-page frame handed to a profile that cannot store it is **refused by name**, and the message
says which two profiles can. Dropping the page would write a video of a rectangle where the script
asked for a cut-out — quietly, and only visible once the clip is laid over something. That is the
same trap ADR 0113 refused for the still formats, and it is refused here for the same reason.

The other direction has an answer rather than a refusal: an ordinary three-page frame written to an
alpha profile is **opaque**. It is the only thing a picture that never mentioned coverage can have
meant, and it is what lets one loop write both kinds to one file.

## Consequences

`SurfaceLerpTest_5.m` no longer writes a PNG sequence and no longer carries an ffmpeg line in its
header. It writes `SurfaceLerpTest_5.apng` directly — 270 frames, 700 by 460, 76.4% of the pixels
fully transparent, the union of the ink over the whole tour at [110, 14, 590, 429], and the same
framing the sequence had. The script is three lines shorter than the version that could not do it.

The one thing a reader should not conclude from the profile list is that MATLAB has these. It does
not, and a script written against them will not run there. That is recorded below rather than
softened.

## Recorded divergences

- **`VideoWriter.getProfiles` answers nine profiles where MATLAB answers seven.** The two added,
  `'Animated PNG'` and `'Uncompressed AVI with Alpha'`, are the only ones here that keep a frame's
  transparency, and MATLAB has no profile that does. MATLAB's seven are listed first and unchanged.
- **`getframe` answers a four-page `cdata` for a figure whose `Color` is `'none'`.** MATLAB's `cdata`
  is always three pages, and its capture of a transparent figure is whatever was behind the window.
- **`VideoWriter` reads `.png` and `.apng` as video extensions.** MATLAB reads neither and refuses
  the name.

## Testing

`MatlabTransparentVideoTests` covers the script's end of the wire in eight tests: the word `'none'`
set and read back on a figure; `getframe` answering three pages for an ordinary figure and four for
one with no page, with a corner whose coverage is nought; the profile table's shape; the AVI's
declared bit count read out of its `BITMAPINFOHEADER`; and the APNG walked chunk by chunk the way a
decoder walks it — signature, colour type 6, `acTL` before the first data chunk with the frame count
patched to the real one, one `fcTL` per frame with the sequence numbers running unbroken across
`fcTL` and `fdAT` together, every frame the whole canvas with dispose-none and blend-source, and
every check word recomputed against an implementation written out in the test rather than borrowed
from the encoder. A profile without alpha refusing a four-page frame, and an ordinary frame written
to an alpha profile coming out opaque, are the two halves of the last decision.
