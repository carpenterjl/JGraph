# ADR 0115 — A splash with nothing behind it

## Status

Accepted (M113, 2026-08-30).

## Context

The startup splash (ADR 0033) has always drawn a rounded rectangle with a blue gradient in it, a
wordmark over that, and a caption and progress bar along the bottom. Its artwork was replaceable —
drop a `splash.png` next to the executable — but a still laid on a painted panel is all it could
ever be, and the panel was the first thing anyone saw of a graphing application.

M111a and M112 built the machinery to do better. A figure can be drawn on no page
(`figure('Color','none')`), a capture of one is four channels rather than three, and `VideoWriter`
has two profiles that carry the fourth (ADR 0114). `SurfaceLerpTest_5.m` in the demo workspace
already used all three to produce exactly the picture a splash wants: five surfaces morphing into
one another under a travelling lit band, cut out of its background, ending on the frame it began on.

Nothing could play it. The engine could *write* an animated PNG and had no way to read one back —
and neither has WPF, whose PNG decoder is WIC's and stops at the first frame, nor Skia, which was
measured rather than assumed: handed an 18 MB, 270-frame APNG, `SKCodec` reports `FrameCount = 0`
and a still image.

## Decision

The splash's background is the animation, the window has no page of its own, and the wordmark,
caption and progress bar sit over it.

Four things follow from that, and each was decided rather than defaulted:

- **The reading half of the APNG work is written here.** `AnimatedPngReader` is the sibling of
  `AnimatedPngEncoder` and is built on the same fact: an APNG is an ordinary PNG with three extra
  chunk types threaded among the ones a decoder already knows. So a frame's compressed data, wrapped
  in a header carrying that frame's own size and whatever the file said before the first frame
  (palette, transparency, colour space), *is* an ordinary PNG, and decoding it is handed straight
  back to Skia. Nothing here inflates anything. The chunk writer and the CRC the two halves shared
  by copying now live once, in `PngChunks`.
- **It reads forwards only.** A frame may be a patch of the one before it, so the canvas is the
  state and `Advance` is the step; `Rewind` and step again is what a loop does anyway. Both dispose
  ops and both blend ops are honoured, because the replaceable artwork is a public contract and an
  APNG from any other tool will use them — the one this build writes uses neither.
- **The splash loops for as long as loading takes and not one frame longer.** Startup is never held
  back to finish a pass. *(Superseded: [ADR 0117](0117-the-splash-plays-out.md) — the pass on
  screen is played out to its last frame once the shell is ready, so the loop is seen closing rather
  than being cut off. The rest of this bullet stands.)* The frames are decoded one at a time on a `DispatcherPriority.Background`
  tick, so the animation yields to the warm-up rather than competing with it, and a frame that will
  not decode ends the animation and leaves the last good one standing rather than failing a start.
- **An animation and a still are separate questions.** `SplashArtwork.FindAnimation` probes
  `splash.apng`; `SplashArtwork.Find` still probes `splash.png` and friends and now ignores an
  `.apng`, which a `BitmapImage` would otherwise decode as its first frame and stand there. The
  animation wins when both exist, because the two mean different things: an animation is a
  background and the wordmark stays over it, a still replaces the wordmark outright. With neither,
  the gradient panel is still there — it is the fallback ground, and the only rectangle left.

The text keeps a ground of its own: a tight, opaque, offsetless shadow on each run, stacked with a
softer one on the block. One soft blur alone spreads too far to hold against a pale desktop, which
is a thing a transparent window has to survive and an opaque one never did.

## Consequences

`src/JGraph.Application/Assets/splash.apng` ships and is copied next to the executable, which is
where `SplashArtwork` looks and where a deployment drops its own. It is 8.4 MB: 180 frames of
560 × 368 at 24 frames a second. `Assets/make-splash.m` is what draws it — `SurfaceLerpTest_5.m`
unchanged except for the frame size, the frame rate and the output name, all three chosen so the
asset is something a program can read at startup rather than the demo's 18 MB.

`JGraph.Application` now references `JGraph.Imaging.Codecs` directly instead of reaching it through
`JGraph.Scripting`.

`splash.apng` joins the staging anchors in `installer/build-installer.ps1` (ADR 0036). It is carried
by a `Content` item with a `TargetPath` rather than by the publish's own folder rules, so it is
exactly the kind of file a layout change drops without a word — and the product would still start,
wearing the fallback panel instead of its own face, which is a failure nothing else would catch.

No MATLAB-facing behaviour changed, so this adds no divergence.
