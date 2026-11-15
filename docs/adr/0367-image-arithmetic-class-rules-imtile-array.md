# ADR 0367 — imadd and its siblings follow R2025b's class rules on arrays, imtile answers an array, and imwrite refuses the integer classes R2025b's writers refuse

## Status

Accepted, 2026-11-14. Open item 322 (chips wave 6, lane 6F). The item is ADR 0356's "Still open"
list, less imread's timing (open item 273 (a)).

## Context

- **The arithmetic verbs.** In MATLAB code `imadd`, `imsubtract`, `immultiply` and `imdivide` took
  only a uint8 or uint16 array (ADR 0356's `IntegerArrayArithmetic`). They read it as normalized
  samples, combined it as a picture is combined, and clamped the result to [0, 1]. A double, single,
  int16 or logical array was refused ("imadd expects argument 1 to be an image, but got a array").
  `imlincomb` refused every array, because it read each array as a weight. `imabsdiff` and
  `imcomplement` ran the picture road on every array. The probe `oi322_image_arith` (230 rows) and
  three side probes (`rawmsg`, `mixprobe`, `wprobe`, about 130 more lines) measured R2025b 25.2, and
  R2025b's sources in toolbox/images/images say why:
  - `imadd(X, Y)` is `X + Y` when Y is a scalar double, or the same size and class as X; anything
    else is `images:imadd:invalidInput`. A logical X, or a third argument naming another class, goes
    through `imlincomb(1, X, 1, Y, class)`, so `imadd(L, M)` is a double;
  - `imsubtract` is the same, with a logical X going through `imlincomb` to double;
  - `immultiply` masks: a logical with a numeric array keeps the numeric one's class and zeroes it
    where the mask is false, and two logicals are `X & Y`. Otherwise it is `X .* Y` with a scalar
    double Y, or two arrays of one class and size (`checkForSameSizeAndClass`);
  - `imdivide` is `X ./ Y` with a scalar double Y, whatever X is (`imdivide('ab', 1)` is
    `[97 98]`). Two logicals divide as doubles (`[1 0; Inf 1]`). Anything else must be numeric and
    of one class and size;
  - `imabsdiff` is `|X - Y|` in the class, saturated (`int8 127` against `-128` is 127), and of two
    logicals is logical;
  - `imcomplement` is `~im`, `intmax - im` for an unsigned class, `bitcmp(im)` (that is, `-im - 1`)
    for a signed one, and `1 - im` otherwise (`imcomplement([2 -1])` is `[-1 2]`, unclamped);
  - `imlincomb` has four fast paths through the operators (`1, A, 1, B` is `A + B`, and likewise
    `A - B`, `B - A` and `A + c`). They expand (`imlincomb(1, A, 1, [1 2])` is a 2-by-2) and refuse
    as the operators do (`MATLAB:mixedClasses`, `MATLAB:sizeDimensionsMustMatch`). Every other form is
    imlincombc: the weighted sum in double, then the output class, rounded half away from zero and
    saturated (`imlincomb(0.5, uint8([1 3 5 255]))` is `[1 2 3 128]`). The arrays must agree in
    class (`images:imlincomb:mismatchedArrayClass`) and size (`:mismatchedArraySize`), and the output
    class is validatestring'd (`'doub'` is double, `'uint'` is ambiguous);
  - integer arithmetic is the class's own: half away from zero, saturated, NaN to 0, so
    `imadd(u, 50.5)` of 10 is 61 (here it was 179.5 rounded to 179 by the picture road's ties) and
    `imdivide(u, 0)` is 255;
  - every refusal is validateattributes' own words, with `MATLAB:<fn>:invalidType`,
    `:expectedReal` and the input's number and name. `immultiply` calls Y input number 1 too, and a
    char's message leaves off its "Instead its type was char." line.
- **int16 at a depth.** ADR 0356 left an int16 array at a depth below 16 unprobed. R2025b's PNG
  writer refuses int16 at every depth (`MATLAB:invalidType`, "Expected input to be one of these
  types: double, single, logical, uint8, uint16 ..."), and the TIFF, JPEG, BMP and GIF writers refuse
  it in their own words. Here it was written from its normalized samples (int16 0 as 128). The other
  integer classes JGraph already refused, in JGraph's words.
- **immultiply of an int16 picture** read the normalized sample, where int16's offset makes a
  product wrong. On the new road it is `X .* Y` in int16's own units. The probe also found that
  `im2int16(0.5)` was -1 here and 0 in R2025b, whose im2int16 rounds onto uint16's 65535 steps and
  then moves down by 32768.
- **imtile** answered a picture value in MATLAB code, always RGB and always transposed in its
  sizes (a 2-by-2 pair gave `<uint8[4 2 3]>`). R2025b (`imtile.m`, `images.internal.createMontage`)
  answers an array of the frames' class: grey stays grey, frames of different classes go to double,
  a logical first frame in a cell becomes uint8, the grid is `ceil(sqrt(rows * frames / cols))`
  columns, and a background colour makes the montage uint8 RGB. A frame of another size is resized to
  the first one's thumbnail by `imresize(I, [r NaN])`, which scales both directions by `r / rows`.
- **A logical at BitDepth 16** was named by ADR 0356 as a divergence with no row, because R2025b's
  whole answer is not stable. Its first half is: R2025b writes the logical's bytes as 16-bit samples,
  so `logical([0 1 1 0 1 0 0 1])` reads back as `[256 1 1 256 ...]`, the first four samples being the
  array's own eight bytes taken two at a time.

## Decision

### A MATLAB road for the arithmetic verbs (`JgsBuiltins.ImageArithmeticMatlab.cs`)

In MATLAB code, when no argument is a picture value (`TakesMatlabImageRoad`), the seven verbs run
`MatlabImadd`, `MatlabImsubtract`, `MatlabImmultiply`, `MatlabImdivide`, `MatlabImabsdiff`,
`MatlabImcomplement` and `MatlabImlincomb`. Each is a transcription of its R2025b source: the same
checks in the same order, the same identifiers and words, the same fast paths. `imadd`'s third
argument is taken, and imadd hands imlincomb the very argument list imadd.m builds, so
`imadd(u, u, 5)` is `u + u + 5` there and here.

The element arithmetic is the file's own `ImBinary`, not the interpreter's operators, so a change to
general operator dispatch (wave 6's lane 6C works on `mixedClasses`) cannot move these answers. It
carries exactly what the sources reach: an integer class with its own class or a scalar (anything
else is `MATLAB:mixedClasses`), single over double (the operands' float values, the double result
rounded to float), logical and char as double, and implicit expansion. An integer result is computed
in double and finished by `JgsNumericClasses.Convert`.

A picture value keeps the picture road. In MATLAB code only a JGraph extra such as `mat2im` makes one
now, and `MatlabImageBuiltinTests.Imlincomb_And_Imabsdiff_CombineImages` leans on its clamped
answers. JGS keeps the picture road for every call. ADR 0356's `IntegerArrayArithmetic` is gone; the
road replaces it.

### imtile answers an array in MATLAB code (`MatlabImtile`)

In MATLAB code `imtile` takes a cell of arrays, an M-by-N-by-P stack or an M-by-N-by-1-or-3-by-P
array, with `GridSize`, `ThumbnailSize`, `ThumbnailInterpolation`, `BorderSize`, `BackgroundColor`
and `Frames`, and answers by `createMontage`'s rules. JGS keeps `TileImages` and its picture value.
A thumbnail resize goes through the new `Geometry.ResizeByScale`, which takes each direction's scale
rather than reading it from the sizes; `Geometry.Resize` now calls it with the scales it always
computed.

### imwrite refuses an integer class by the writer's words (`RefuseMatlabImwriteClass`)

In MATLAB code an int8, int16, int32, int64, uint32 or uint64 array written to a `.png`, `.tif`,
`.jpg` (signed classes), `.bmp` or `.gif` path is refused with that writer's identifier and words,
before anything is written. JGS keeps writing int16 from its normalized samples.

### im2int16 rounds onto uint16's steps in MATLAB code

The class-verb road (ADR 0356) snaps each sample onto `round(x * 65535) / 65535` before the int16
offset when the target is int16 and the code is MATLAB, so `im2int16(0.5)` is 0.

## Consequences

- New parity fixture `oi322_image_arith` (261 rows from R2025b 25.2, one of them a divergence),
  packed and boxed.
- A MATLAB script that added, subtracted, multiplied or divided double, single, int16 or logical
  arrays with these verbs now runs, with R2025b's classes. One that leaned on the clamp (`imadd(A, B)`
  of doubles staying at most 1) gets R2025b's unclamped sum.
- `imtile` in MATLAB code is an array, so `size`, indexing and arithmetic read it as R2025b does.
- An int16 PNG, TIFF, JPEG, BMP or GIF written from MATLAB code is now refused.

## Divergences

- **A logical written at BitDepth 16 reads back as its own 0 and 1.** R2025b writes the logical's
  bytes as if they were 16-bit samples, so the first half of the file is the array's bytes taken two
  at a time (`[256 1 1 256]` for `logical([0 1 1 0 1 0 0 1])`) and the second half is whatever lies
  past the array in memory. R2025b writes a logical's own 0 or 1 at every other depth, and that is
  what is written here (correctness outranks parity). Fixture row `oi322_image_arith/lg16_head`
  compares the deterministic first half.

## Still open

- `imcomplement` of a complex double is refused here; R2025b answers `imcomplement(1+2i)` as `0+2i`,
  by a rule not worked out.
- `imtile` of file names, of an indexed image with a map, and `ThumbnailSize` "auto" are not taken;
  a logical frame resized to a thumbnail is thresholded at one half (unmeasured).
- `imresize(A, [r NaN])` here rounds the other size where R2025b takes the ceiling, and reads each
  direction's scale from its own sizes where R2025b applies `r / size(A, 1)` to both (seen in
  R2025b's imresize source while matching imtile's thumbnails; imresize itself was not changed).
- The writers' other class refusals R2025b makes were seen and not taken: single into TIFF or GIF,
  uint16 into JPEG (without a BitDepth of 12 or 16), BMP or GIF. R2025b writes uint32 and uint64 into
  JPEG, where JGraph refuses them. JGraph's BMP writer fails on every class (`failed to encode
  image`), and a logical or greyscale JPEG reads back as three planes here and one in R2025b.
