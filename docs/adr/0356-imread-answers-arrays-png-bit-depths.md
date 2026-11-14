# ADR 0356 — imread answers a plain array in MATLAB code, and imwrite and imfinfo keep R2025b's bit depths

## Status

Accepted, 2026-11-14. Open items 292 and 294 (chips wave 4, agent U). Both were left by ADR 0343,
which made a 1-bit PNG read back as a logical matrix and listed the imwrite leftovers under Still open.

## Context

- **Item 292.** In MATLAB code `imread` of a uint8, uint16 or RGB file answered JGraph's picture value
  (`JgsType.Image`), a buffer of normalized samples with a class tag. `class`, `size`, `isequal`,
  `mat2str` and `G(r, c)` had been taught to read it, but nothing else had. The fixture's first 113 rows,
  run before the change, found:
  - `G > 100` and `G >= A` refused ("Operator '>' needs two numbers, but got image and number");
  - `G == 7` and `~G` answered a scalar false;
  - `G + 200`, `G .* 2` and `G ./ 3` answered unsaturated doubles (445, 81.67);
  - `-G` refused;
  - `sum(G)` answered one number, the sum of the normalized samples (18.28);
  - `max(G(:))` was a double;
  - `G(end)` and `G(G > 240)` refused;
  - `[G G]` was a double, and `[G(1, 1:2) 300]` kept 300;
  - `G(1, 1) = 300` stopped the script ("Cannot assign by index into a image").

  R2025b's `imread` answers a uint8 or uint16 M-by-N or M-by-N-by-3 array, and its third output (the
  alpha plane) is an array of the same class. Nothing in MATLAB code needs the picture value: every
  image builtin MATLAB code reaches already takes a plain array (`ImgLike`), and `getframe` has
  always answered one.
- **Item 294.** `imwrite`'s PNG writer took only depths 1, 8 and 16, and quantised every class the
  same way, by `round(x * 255)` of the normalized sample with ties to even. R2025b's `writepng`
  (probes `oi_image_values`, `extra_probe`, `verbs_probe`):
  - takes depths 1, 2, 4, 8 and 16 for a greyscale picture, 8 and 16 for greyscale with alpha and
    for RGB. Any other depth is refused: `MATLAB:imagesci:writepng:invalidGrayscaleBitDepth`,
    `:invalidGrayscaleAlphaBitDepth` or `:invalidRgbBitDepth`. Here it was written at 8 bits;
  - keeps an integer class's top bits. A uint8 sample at depth 2 is `x >> 6` and at depth 4
    `x >> 4`. A uint16 sample at depth 8 is `x >> 8` (129 is 0, 257 is 1). A uint8 sample at depth 16
    is `x * 257`. Here uint16 129 at depth 8 was 1;
  - writes a logical's own 0 or 1 at every depth above 1. A logical at depth 8 reads back as uint8
    0 and 1, and at depth 4 as 0 and 17. Here it was 0 and 255;
  - writes a double or single sample as `round(x * (2^b - 1))`, half away from zero.
    `[0.5 1.5 2.5 3.5] / 255` reads back as `[1 2 3 4]`. Here it was `[0 2 2 4]`;
  - quantises an alpha plane by its own class (a uint16 alpha on a uint8 picture is shifted).

  `imread` of a 2- or 4-bit greyscale file already answered R2025b's uint8 (Skia scales the levels
  by 85 and 17). `imfinfo` read the depth from the decoded class, so a 2- or 4-bit file said 8.
  R2025b reads the header. A pixel's depth times its colour samples, alpha not counted: greyscale
  with alpha is 8, RGBA is 24, 16-bit RGB is 48, an indexed file is the index depth. `[X, map] =
  imread` of a file with no palette answered a 0-by-3 map; R2025b answers 0-by-0.

## Decision

### imread answers an array in MATLAB code (`ReadPicture`)

In MATLAB code (read at the call, V11, ADR 0172), the multi-output `imread` hands its buffer to
`NumbersOut`, which answers a uint8 or uint16 matrix, or an M-by-N-by-3 array, of the file's class.
The buffer goes in directly: NumbersOut owns and disposes it, so there is no copy. A 1-bit file is
still a logical matrix, and the alpha output goes the same road. JGS code keeps the picture value its
image verbs take. A page with neither one nor three samples (a CMYK TIFF) is left a picture, as
before.

The operator dispatch in `Interpreter.cs` is not touched. The picture value never reaches it from
MATLAB code now, and JGS's refusal of `img > 0` is JGS's own (its image verbs threshold).

### The verbs imread's array now reaches

Three builtins read only a picture or misread an integer array. They take imread's array now:
- **`im2double`, `im2single`, `im2uint8`, `im2uint16` and `im2int16`** now read every array in
  MATLAB code through `ImgLike` and answer through `NumbersOut`. Under JGS only an integer, logical
  or M-by-N-by-3 array takes this road. Each array is read in its class's units, an RGB array plane
  by plane. As in R2025b:
  - a float array keeps its values for a float target: `im2double(2)` is 2, and `im2single` rounds
    to single;
  - a float array is clamped to [0, 1] for an integer target;
  - the answer carries the target's class.

  Before, in MATLAB code:
  - `im2double(uint8(128))` was 1, because the array was clamped to [0, 1] as if it were a double;
  - an RGB array and a vector were refused;
  - `im2uint8` of a double matrix answered a double.

  JGS's road for a 2-D double matrix is unchanged.
- **`imadd`, `imsubtract`, `immultiply`, `imdivide`** take a uint8 or uint16 array in MATLAB code
  (`IntegerArrayArithmetic`). It is read in its class's units, combined as a picture is, saturated
  to the class, and answered as an array of the class.
- **`im2mat`** (a JGraph extra) takes an array through `ImgLike`.

### The PNG writers quantise by class (`ImageCodec.PngLevel`)

`ImageCodec.PngLevel(sample, class, bits)` is the level a sample of a class stores at a depth, by the
rules above. The strip-parallel 8-bit writer (ADR 0330), the 16-bit writer and the new packed writer
all go through it, for the picture and for its alpha plane. Depth 1 stays "on when nonzero" (ADR
0343). `ToByte`, which the Skia road (JPEG, BMP, WebP) still uses, rounds half away from zero.

### Depths 2 and 4 are written (`WritePackedGrayPng`)

The 1-bit writer became `WritePackedGrayPng(path, image, bits)`, which packs 8 / bits samples a byte
with the first pixel in the high bits and pads each row to a byte. A greyscale picture with no alpha
and BitDepth 1, 2 or 4 is written by it.

### A depth the picture cannot have is refused (`RefusePngBitDepth`)

The refusal is in `imwrite`, for a `.png` path with BitDepth given, before any byte is written. It
uses writepng's identifiers and words.

### imfinfo reads a PNG's depth from its header (`ImageCodec.PngBitDepth`)

For a PNG, `BitDepth` is the IHDR depth times 3 for colour types 2 and 6, and times 1 otherwise.
Other formats keep the class-based answer. JGraph's extra `NumberOfSamples` field is kept (R2025b's
PNG `imfinfo` has no such field).

### A plain file's map is 0-by-0

In both dialects.

## Consequences

- New parity fixture `oi_image_values` (135 rows from R2025b 25.2), packed and boxed, with no
  divergences. It covers:
  - operators, indexing, assignment, reductions, concatenation, round trips and `image`/`imshow`
    on uint8, uint16, RGB, indexed and alpha reads;
  - every depth's levels for uint8, uint16, double, single and logical, and imfinfo's BitDepth and
    ColorType for each kind of file;
  - the three refusals;
  - the class verbs and `imadd` and its siblings on imread's arrays.
- New tests in `MatlabImageClassTests`: `ImreadAnswersAPlainArrayThatOperatorsAndAssignmentTake` and
  `ImwriteBitDepthsTwoFourEightAndSixteenStoreR2025bsLevels`.
- In the boxed lane (`JGRAPH_JGS_PACKED=0`), an imread result is now a boxed array: one JgsValue
  per sample, where the picture value kept a hundred megapixels out of the element model. The
  packed default stores a double per sample either way.
- A MATLAB script that leaned on the picture value's quirks changes answer. `sum(G)` is the column
  sums, `G + 200` saturates, `[G 300]` is uint8.

## Divergences

- **A logical written at BitDepth 16 is 0 and 1 here.** R2025b writes the logical's bytes as if they
  were 16-bit samples, so it reads back what lies past the array in memory (`[256 1 27949 29793]`
  for `logical([0 1 1 0])`). Its answer at every other depth is the logical's own 0 or 1, and that is
  what is written here. There is no fixture row, because R2025b's answer is not stable.

## Still open

- **`imadd` and its siblings on a double or single array** are refused in MATLAB code, as before.
  The picture arithmetic clamps to [0, 1], and R2025b's imadd of doubles does not.
- **An int16 array at a depth below 16**, and `immultiply` of an int16 picture, read the normalized
  sample (int16's offset makes the shift rule and a product wrong). Neither was probed.
- **`imtile`** answers a picture value in MATLAB code. R2025b answers an array.
- **imread of a 2048-square PNG** (open item 273 (a)) now also pays the conversion to an array in
  MATLAB code. That is one pass over the samples on top of the decode, and was not timed.
