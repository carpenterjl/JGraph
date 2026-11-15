# ADR 0373 — imcomplement of a complex double, imresize's one scale, imtile of files and colour maps, the writers' class rules, and a BMP writer

## Status

Accepted, 2026-11-15. Open item 335 (chips wave 7, lane 7A): ADR 0367's still-open list.

## Context

The probes `p1` (imcomplement, imresize, imtile; 50 rows), `p2` (every class into PNG, TIFF, JPEG,
BMP and GIF and read back, JPEG options, the BMP layout; 120 rows) and `badbd` (JPEG options and
indexed BMP writes) measured R2025b 25.2, and its sources say why:

- **imcomplement.** imcomplement.m refuses a complex array unless it is double, and a complex
  double falls through to `1 - im`. The item said `imcomplement(1+2i)` is `0+2i`; R2025b answers
  `0-2i`, which is `1 - (1+2i)`. A result with no imaginary part left is real (`isreal` of
  `imcomplement(complex(1, 0))` is true), as the operator narrows it. JGraph refused every complex
  double, and read a boxed complex array (`[1+2i 3-1i; 0.5 2i]`) as NaNs.
- **imresize.** resizeParseInputs fills a NaN size from the aspect ratio and then takes `ceil` of
  both sizes (`[3 NaN]` of a 5-by-7 is 3-by-5; `[2.5 NaN]` is 3-by-4; `[0.5 0.7]` is 1-by-1), and
  deriveScaleFromSize applies `r / size(A, 1)` to both directions (the other way round for
  `[NaN c]`). A size with no NaN scales each direction by its own ratio. A scale is used as given,
  the size being `ceil(scale .* size(A))` (deriveSizeFromScale), so `imresize(A, 0.3)` of a 5-by-7
  weighs its samples by 0.3, not by 2/5 and 3/7. A logical array is resized as `uint8(255 * A)` and
  answered as `B > 128` (preprocessImage, postprocessImage), a logical. JGraph rounded the NaN size,
  read each direction's scale back from the sizes, refused a size below one pixel, and answered a
  logical array as a double of fractions.
- **imtile.** imtile.m takes file names (a char row, a string array, a cell of names; a cell mixing
  names with arrays is `MATLAB:images:montage:mixedInput`) and reads each by getImageFromFile, which
  refuses a missing file as `images:getImageFromFile:fileDoesNotExist` with the name as given and
  expands a one-plane file through its own colour map (`ind2rgb`), so an indexed PNG or a grey BMP
  tiles as a double RGB image. A second argument before the first char argument is a colour map:
  each one-plane frame goes through `ind2rgb` (an integer or logical index is zero-based, a float
  one-based, both clamped to the map). The array road refuses an int16 image with a map
  (`invalidIndexedImage`) and a map that is not double (`MATLAB:createMontage:invalidType`); a third
  argument that is not char is `nonCharParam`. `ThumbnailSize` and `GridSize` are validated as
  numeric, so `"auto"` is `MATLAB:imtile:invalidType` (createMontage's "auto" is montage's, not
  reachable from imtile). A cell frame of more than three planes is
  `MATLAB:images:imageDisplayValidateParams:unsupportedDimension`.
- **The writers.** writetif takes logical, uint8, uint16 and double (single is refused); writebmp
  logical, uint8, single and double (uint16 is refused); writegif logical, uint8 and double;
  writejpg refuses uint16 without a BitDepth of 12 or 16 (`bitdepthNotSpecifiedAt16`), a BitDepth
  other than 8, 12 or 16 (`badBitDepth`), 16 bits for colour (`tooManyBitsForColor`) or lossy
  (`tooManyBitsForLossy`), and writes any other class as `uint8(255 * double(x))`, so a uint32 or
  uint64 array is written (every sample of 1 or more is 255). JGraph refused uint32 and uint64 into
  JPEG, wrote single into TIFF and GIF, uint16 into GIF (as zeros) and uint16 into JPEG (as zeros),
  and did not know the `Mode` option.
- **JPEG planes.** R2025b writes a greyscale or logical picture as a one-component JPEG, which reads
  back as one plane (imfinfo: 8 bits, grayscale). Skia's encoder was handed RGBA, so JGraph wrote
  three components and read three planes back.
- **BMP.** Skia decodes a BMP but has no BMP encoder, so `SKImage.Encode` answered null and every
  BMP write failed ("failed to encode image"). writebmp writes a logical picture as a 1-bit file
  with a black-and-white palette, any other greyscale picture as an 8-bit file with a 256-entry grey
  ramp (or the colour map an indexed write gives, padded with black; a double index is one-based),
  and RGB as 24 bits, rows bottom-up padded to four bytes, with no resolution and the palette size
  in biClrUsed. readbmp answers an indexed file's indices (logical for 1 bit, else uint8) and its
  palette as the map, so `[X, map] = imread` of a grey BMP is the levels and a 256-by-3 ramp.

## Decision

### imcomplement of a complex double (`MatlabImcomplement`)

A complex double is `1 - im`, real when nothing imaginary is left, complex storage otherwise; other
complex classes keep `images:imcomplement:complexInput`. `ReadImArray` now reads a boxed array with
a complex element as complex (it saw only packed complex), so the other arithmetic verbs refuse such
an array as `expectedReal`, as they refuse a packed one.

### imresize in MATLAB code (`MatlabResize`)

For an array argument in MATLAB code, imresize sizes and scales as resizeParseInputs does and
resizes through `Geometry.ResizeByScale` with those scales; a logical array is answered as the
samples whose `round(255 * v)` is above 128. JGS, and a picture value in MATLAB code, keep the old
road (`SizeTarget`, `ScaleTarget`, `Geometry.Resize`). imtile's thumbnail threshold for a logical
frame is the same `> 128` rule (it was `>= 0.5`).

### imtile reads files and colour maps (`MatlabImtile`)

`MatlabImtile` now takes the host's path resolver and the dialect. File names are read by the new
`ImtileReadFile` through `ReadImageFileSamples` and `ImreadPicture`, the same reading `[X, map] =
imread(f)` does (both moved out of the imread wrapper so the two share them). The colour map goes
through the new `ImIndexToRgb` (matlab.images.internal.ind2rgb), with `ImMapRows` reading the map.
`ImtileNumericParameter` is the numeric class check for GridSize and ThumbnailSize.

### imwrite's class rules and JPEG options (`RefuseMatlabImwriteClass`, `MatlabJpegData`)

`RefuseMatlabImwriteClass` now carries each writer's whole class list, so single into TIFF or GIF
and uint16 into BMP or GIF are refused in R2025b's words. `MatlabJpegData` runs writejpg's checks in
its order, converts uint32 and uint64 to `uint8(255 * x)`, and refuses a 12- or 16-bit or lossless
JPEG in JGraph's words: only eight-bit lossy JPEG is written here. `Mode` joins imwrite's option
names. A logical picture written to BMP with a palette of other than two entries, or a logical RGB
picture, is `wrongNumberOfColormapEntriesFor1D`.

### A BMP codec (`IndexedBmp`) and one-component JPEG (`ImageCodec.WriteGrayJpeg`)

`IndexedBmp.Write` writes writebmp's file byte for byte (the fixture compares the bytes of a grey,
an RGB and a logical file), and `ImageCodec.Write` sends `.bmp` to it in both dialects. In MATLAB
code an indexed write (`imwrite(X, map, 'f.bmp')`) keeps the indices and palette. `IndexedBmp.Read`
reads an uncompressed 1-, 4- or 8-bit BMP's indices and palette; imread's new
`ReadImageFileSamples` takes it before Skia, and imfinfo reports such a file as indexed.
`ImageMetadata.EncodedChannels` reads a 16-bit-or-more BMP as colour whatever its values, so a grey
24-bit BMP is three planes, as R2025b reads it. A one-plane JPEG is encoded from a Gray8 bitmap,
which Skia writes as a one-component file.

## Consequences

- New parity fixture `oi335_image_leftovers` (200 rows from R2025b 25.2, four of them divergences),
  packed and boxed.
- BMP writing works in both dialects, and a greyscale JPEG is one plane in both; JGS's `imread` of a
  grey JPEG is one plane now.
- A MATLAB script resizing with `[r NaN]` or a fractional scale gets R2025b's sizes and samples; one
  resizing a logical array gets a logical.
- A MATLAB script writing single to TIFF or GIF, uint16 to BMP, GIF or JPEG (without BitDepth 12 or
  16), or naming 12 or 16 bits or lossless for JPEG, is refused where it used to write.

## Divergences

- **A 12- or 16-bit or lossless JPEG is refused.** R2025b writes uint16 at BitDepth 12 or 16 as a
  lossless JPEG, any other class at BitDepth 12 as a 12-bit lossy one, and `'Mode', 'lossless'` at
  eight bits, and reads each back (uint16 for 12 and 16 bits). JGraph's encoder and decoder (Skia)
  write and read eight-bit lossy JPEG only, so after writejpg's own checks such a write is refused
  in JGraph's words rather than written as an eight-bit picture. Fixture rows
  `oi335_image_leftovers/jpg_u16_bd12`, `jpg_u16_bd16`, `jpg_u8_bd12`, `jpg_lossless8`.

## Still open

- A logical TIFF is written as uint8 0 and 255 (R2025b writes a 1-bit file and reads it back
  logical), and a logical or double GIF without a map is written as grey levels (R2025b writes a
  logical's 0 and 1 as indices, and a double as `x - 1`, so `[0 0.5 1]` is all zeros with
  `MATLAB:imagesci:writegif:dataOutOfRange`). Seen in the class sweep, not taken.
- imfinfo's `Format` is the extension in capitals (`'JPG'`, `'BMP'`); R2025b's is lower case
  (`'jpg'`, `'bmp'`).
- The `MATLAB:imagesci:imwrite:signedPixelData` warning R2025b gives a signed class into PNG, BMP or
  GIF before the writer refuses it is not given here.
- An RLE-compressed or bitfields BMP is read through Skia as colours, not as indices and a map.
