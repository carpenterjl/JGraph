# ADR 0380 — GIF indices, a 1-bit logical TIFF, BMP files through every compression readbmp takes, imfinfo's Format, and imwrite's signed-class warning

## Status

Accepted, 2026-11-16. Open item 344 (chips wave 8, lane 8A): ADR 0373's still-open list, all but
the 12-bit, 16-bit and lossless JPEG writes, which stay ADR 0373's divergences.

## Context

Every expectation was measured in R2025b 25.2 headless (`-noFigureWindows -batch`): probes `p8a1`
to `p8a6` in lane 8A's scratch (about 150 cases: every class into GIF and TIFF with and without a
map, appended GIF frames with and without their own table, imfinfo's Format of each format and
spelling, signed classes into each writer, and some sixty BMP files built byte by byte: RLE8 and
RLE4 with every code, cut short and run past their rows, 16- and 32-bit files with and without
bitfields, 12-, 40-, 108- and 124-byte headers, top-down rows, palettes of every size), one line per
case in both engines and diffed. The rules are R2025b's own sources: writegif.m, writetif.m,
imwrite.m, readgif.m, imgifinfo.m, imbmpinfo.m and readbmpdata.m (bmpdrle, the RLE decoder, is
compiled; its rules were measured).

- **A logical TIFF.** writetif writes a logical picture as a 1-bit file, CCITT 1D, white is zero,
  and ignores a map given with it; imread answers it logical and imfinfo says 1 bit, grayscale. A
  logical picture of more than one plane is `MATLAB:imagesci:writetif:unsupportedData`. JGraph
  wrote uint8 0 and 255 and read them back as such.
- **GIF writes.** writegif takes the samples as indices: a logical's or uint8's as they are, a
  double's as `x - 1` (it is one-based). Without a map a logical picture gets a black-and-white
  table and anything else a 256-level grey ramp; an appended frame without a map uses the file's
  table, one with a map carries it as the frame's own. An index past the table or below zero is
  `MATLAB:imagesci:writegif:dataOutOfRange` and clamped, so `[0 0.5 1]` is all zeros. A table is
  written at the power of two that holds it (two entries is one bit). A 3-D array (RGB) is
  `MATLAB:imagesci:writegif:badSize`. JGraph wrote any GIF as the colours its samples make, chose
  its own table, and wrote RGB.
- **GIF reads.** readgif answers the indices and the first frame's table (padded to its power of
  two), never colours. A later frame is composed over the earlier ones by its disposal method and
  transparent index, and when not every frame uses the global table (nor every one the default)
  each frame is painted through its own table and matched back onto the first frame's (rgb2ind):
  a black-and-white frame appended to a red-green-blue file reads `[0 3; 3 0]`. imgifinfo's Format
  is the signature's `'GIF'`, in capitals, and BitDepth the bits of the table each frame uses.
  JGraph read a GIF through Skia as colours.
- **imfinfo's Format.** It is the format the bytes say, lower case (`'png'`, `'jpg'` for `.jpeg`,
  `'tif'` for `.tiff`, `'png'` for a PNG named `.jpg`), except GIF's `'GIF'`. JGraph gave the
  extension in capitals.
- **signedPixelData.** imwrite warns `MATLAB:imagesci:imwrite:signedPixelData` before the PNG, BMP
  and GIF writers refuse a signed class (TIFF refuses without it, JPEG refuses in imwrite).
- **BMP reads.** imbmpinfo reads the colour map from the bytes between the header and the pixels
  (biClrUsed is not read), so a 24-bit file with a palette is indexed and an 8-bit file without one
  grayscale with a 0-by-0 map; a 40-byte header's bitfields masks follow it and leave no map; a
  108-byte header on a 16- or 32-bit file that is not bitfields is `invalidBMPmetadata`, a
  compression past 4 `unrecognizedCompression`, 24-bit RLE `unsupportedCompressionScheme`; BitDepth
  is the header's bits a pixel. readbmpdata fills data cut short with zeros after
  `truncatedImageData`, reads RLE8 and RLE4 into rows padded to four and eight pixels (a run past a
  row's end carries into the next row; an end of line starts the next row; a pixel no code reaches
  is 0; RLE8 is used for compression 1 whatever the header's depth), with bmpdrle's warnings: a
  write past the rows `indexExceedsOutputBufferLength` (one a pixel), a code past the data
  `indexExceedsInputBufferLength` then `missingEndCode`, data run out `missingEndCode`, bytes after
  the end code `prematureEndCode`. A 16-bit file without compression is five bits a colour widened
  by bit replication; with bitfields each mask's bits are scaled to eight. JGraph read an RLE or
  bitfields BMP through Skia as colours, an 8-bit file's map from biClrUsed, a truncated file as an
  error, and gave imfinfo the depth of what it decoded (24 for a 16- or 32-bit file).

## Decision

### Readers in the codecs (`BmpFile`, `GifFile`)

`BmpFile.ReadHeader` transcribes imbmpinfo (12-, 40-, 56-, 108- and 124-byte headers; null for any
other, which Skia still reads) and `BmpFile.Read` readbmpdata, with the warnings collected as
`ImageFileWarning` records and the refusals thrown as `ImageFileRefusal` (an `IOException` carrying
R2025b's identifier). `GifFile.Parse` reads a GIF's frames (global, local and default tables, the
graphic control block's disposal and transparency, interlacing, its own LZW decoder) and
`GifFile.Read` composes a frame as readgif does. In MATLAB code `ReadImageFileSamples` takes every
BMP `BmpFile` reads and every GIF before Skia (the imread wrapper and imtile's file road), and gives
the BMP warnings through `Warn`; the JGS dialect keeps its old road (uncompressed indexed BMPs through
`IndexedBmp`, everything else Skia).

### Writers (`GifEncoder.WriteIndexed`, `TiffCodec.WriteOneBit`)

In MATLAB code a 2-D GIF write goes through the new `MatlabGifWrite` (writegif's indices, table,
range check, delay of 50 hundredths and loop count, no loop block for writegif's -1) and
`GifEncoder.WriteIndexed`; a 4-D array and the JGS dialect keep the old writer. `TiffCodec.Write`
writes a one-plane logical picture as a 1-bit CCITT 1D white-is-zero file without a palette, in
both dialects (R2025b reads JGraph's file back as it reads its own, measured).

### imfinfo and imwrite in MATLAB code

`MatlabImageFormat` is the Format (`ImageFormatOf`, the bytes' format, with GIF in capitals);
`MatlabImfinfoFromHeader` describes a BMP or GIF from its header (BitDepth, ColorType, the frame's
size, one entry a GIF frame). The JGS dialect keeps `'PNG'`. `WarnMatlabSignedPixels` gives the
warning before `RefuseMatlabImwriteClass`.

## Consequences

- New parity fixture `oi344_image_leftovers` (131 rows from R2025b 25.2, twelve of them
  divergences), packed and boxed alike; new unit tests `BmpGifFileTests`.
- A MATLAB script reading a GIF gets indices and a map where it got colours (`ind2rgb` gives the
  colours); one writing a double GIF in [0, 1] gets R2025b's zeros and warning;
  `MatlabM72ParityTests.AGifIsWrittenAndAppendedToFrameByFrame` now writes uint8 frames, and
  `MatlabImageBuiltinTests.Imfinfo_ReportsTheFileWithoutTheScriptOpeningIt` expects `'png'`.
- A logical picture written to TIFF is a 1-bit file in JGS too, and reads back logical there.

## Divergences

- **A bitfields BMP's samples are rounded once.** readbmpdata scales a mask's bits as
  `uint8(v * 255 / (2^n - 1) + 0.5)`, and uint8 rounds already, so a zero reads 1 and every level
  is a half up (a black 5-6-5 pixel is `[1 1 1]`). JGraph answers `round(v * 255 / (2^n - 1))`.
  Fixture rows `oi344_image_leftovers/bf565`, `bf555`, `bf444`, `bf16_black`, `v4_bf565`.
- **A 32-bit bitfields BMP reads each colour through its mask.** readbmpdata sorts the masks to
  find each colour's byte, and a 108- or 124-byte header's zero alpha mask sorts first, moving
  every colour one byte (red reads the pad byte); a mask that is not a whole byte is read as the
  byte its rank names. JGraph shifts and scales each mask. Rows `v4_bf32_a0`, `v5_bf32_a0`,
  `bf32_10bit`.
- **An RLE8 delta moves from the cursor.** The BMP format, RLE4 in R2025b and Skia move a delta
  from where the last code left off; R2025b's RLE8 decoder moves from the start of the row after
  an encoded run and one short after an absolute run. Rows `rle8_delta_after_run`,
  `rle8_delta_after_abs`, `rle8_delta_twice`, `rle8_delta_up`.

## Still open

- writetif's options (`Compression`, `RowsPerStrip`, `Description`, `Resolution`, `ColorSpace`) are
  unknown options here; R2025b's `'Compression', 'jpeg'` refusal of a logical picture and its
  `notBinaryAndCCITT` refusal of a CCITT uint8 one wait on them.
- A 4-D GIF write (frames on the fourth dimension) keeps the old colour writer; R2025b writes the
  frames as indices. writegif's `TransparentColor`, `BackgroundColor`, `DisposalMethod`, `Location`
  and `ScreenSize` options are not taken. imread's `'Frames', 'all'` and readgif's frame refusal
  (`MATLAB:imagesci:readgif:frameCount`) are not modelled.
- A recast GIF frame (tables that differ) takes the nearest colour of the first table; R2025b
  calls rgb2ind, which dithers a colour the table does not hold (not measured).
- imread of a BMP with a frame index answers here; R2025b's readbmp is `MATLAB:TooManyInputs`. A
  2-bit BMP is R2025b's `MATLAB:unassignedOutputs` and read through Skia here. imfinfo's Height of
  a top-down BMP is positive here (imbmpinfo keeps the header's sign; not measured), and imfinfo
  gives none of the BMP- and GIF-specific fields (Colormap, CompressionType, DelayTime, ...).
- bmpdrle warns once for each pixel written past the rows; JGraph stops warning after a hundred.
