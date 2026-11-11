# ADR 0330 — The every-row plan's I9: a square is the product, the gradient of a matrix goes by whole columns, and a PNG is deflated in strips

## Status

Accepted, 2026-11-10. Stage I9 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 10). **Partly done**: two of the four rows are under R2025b's median
(`d06_edges` 0.032 s against 0.097, `d06_imwrite_x3` 0.104 against 1.265); `d06_generate_2048` is
0.101 against 0.081, and `d06_threshold`, which this stage did not change, read 0.019 against
0.014 headless and 0.013 at steady state. The script's wall is 1.20 s where it was 2.71 and
R2025b's is 3.95. Every CHK line is byte-identical. Landed together with I10 (ADR 0331).

## Context

The GUI round (ADR 0322) left four rows of `d06_image` to I9, 0.17 s of excess between them:
`d06_generate_2048` at 0.112 s against R2025b's 0.081, `d06_edges` at 0.172 against 0.097,
`d06_threshold` at 0.018 against 0.014 and `d06_imwrite_x3` at 1.325 against 1.265. The user's
rule for the remaining stages is that the timing run comes once, at the end, so the stage was
built on reading each row down to its kernel. The reading found four things.

- **`x.^2` was `Math.Pow` an element, and a scan before it.** The packed kernel raised every
  element with the C runtime's `pow`, on purpose: its comment records that over 212 million
  random doubles `pow(x, 2)` differed from `x*x` on 52,298 of them by one ulp, and the boxed
  interpreter answered `pow`, so the product would have made the two storages disagree. Before
  the kernel ran, `PowerWouldGoComplex` walked the whole array on one thread asking of every
  element whether a negative base met a fractional exponent, which for an exponent of 2 it
  cannot. `d06_generate` squares two 2048-square matrices and `d06_edges` two more.
- **The gradient of a matrix was a row at a time on one thread.** `[gx, gy] = gradient(F)` copied
  each row of the column-major matrix out through a stride into a fresh array, differenced it
  into another, and copied that back through the stride; then the same for each column. I5 (ADR
  0328) had given the vector case a threaded kernel and left the matrix case as it was.
- **`imwrite` made four copies of the picture and then deflated it a byte at a time on one
  thread.** A `uint8` matrix became a list of rows (`ToRows`), a rectangle (`double[,]`), a
  picture buffer, and was then divided by 255 element by element; the PNG writer then called
  `ZLibStream.WriteByte` once per sample, 4.2 million calls an image, into a single deflate
  stream at the default level. Three images a pass.
- **`d06_threshold`** is a comparison, a 9 by 9 box through `conv2` and two `double(mask)`
  conversions, 4 ms over R2025b in the round. Nothing in it reads as wrong; it is measured below
  and not changed.

R2025b was asked what `x.^2` is (`probe_pow.m`, twelve million doubles of every magnitude and
sign, a matrix, scalars, short vectors, `int32`, `single`): it is `x.*x` to the bit in every
case. `x.^0.5` is `sqrt(x)` and `x.^-1` is `1./x` to the bit as well; `x.^3` and `x.^4` are not
the products. The same probe confirmed that `gradient` of a matrix is the plain central
difference, `(F(:, j+1) - F(:, j-1)) / 2` inside and the one-sided difference at the ends, to
the bit.

## Decision

### A square is the product, on every road to a power

`PackedMath.Power(x, y)` is the one definition of a real power: `x * x` when `y` is exactly 2,
`Math.Pow(x, y)` otherwise. Every place the `^` and `.^` operators are computed now goes through
it: the boxed interpreter (`Interpreter.Binary`), the packed array-by-array and scalar-by-array
kernels, the walked hot loop (`LoopOp.PowG`), the loop compiled to IL (which now calls
`PackedMath.Power` where it called `Math.Pow`), and the compiled anonymous-function bodies of I1
and I2 (`BodyOp.Pow`). The packed array-by-scalar kernel takes the square as one
`TensorPrimitives.Multiply` over the grain instead of a loop of calls.

That is the answer R2025b gives, it is the correctly rounded one, and because every road takes
it the storages and the compiled and walked forms still agree with each other to the bit, which
was the reason the product had been declined. `Complex.Pow` is untouched: a complex square is
still the complex power.

`PowerWouldGoComplex` answers at once for a scalar exponent that is a whole number or a NaN: its
own per-element test (`PowerStaysReal`) is true for every base then, so there is nothing for the
scan to find.

### The gradient of a matrix is whole columns at a time (`VectorKernels`)

`VectorKernels.CentralDifferencesAcross` and `CentralDifferencesDown` take a column-major matrix
at unit spacing. Across the columns, an output column is one neighbouring column minus the other,
halved: two `TensorPrimitives` calls over contiguous memory. Down the rows, each column is the
vector kernel of I5 applied to itself. Both cut the columns into at most 64 strips and run the
strips in parallel from `ParallelKernels.MemoryBoundThreshold` elements. Every element is one
subtraction and one division by two, exactly the general road's `(a - b) / (x[i+1] - x[i-1])`
with the positions `0, 1, 2, …`, so the answer is the general road's to the bit at any thread
count. `JgsVectorRoads.TryGradientField` takes the road for `gradient(F)` with one or two outputs
of a packed matrix of plain doubles with at least two rows and two columns; the second field is
made only when it is asked for. A spacing argument, an N-D array, another class and boxed
storage keep the general road. `JGRAPH_VECTOR_FAST=0` turns it off with the rest of I5's roads.

### A packed matrix reaches the picture verbs in one pass

`ImgLike`, through which every imaging builtin reads a numeric argument, lays a packed real
matrix out row-major with `MatrixLayout.Transpose` (the tiled, threaded transpose of ADR 0155)
straight into the picture buffer, where it made a row list and a rectangle first. The division
that brings an integer class into [0, 1] runs grain by grain across threads. The samples are the
ones the general road produced: the same numbers moved, and the same one division.

### An 8-bit PNG is deflated in strips

`ImageCodec.Write8BitPng` cuts the rows into strips of about 128 KB of raw bytes. Each strip is
quantised (the same `ToByte` per sample) and deflated on its own thread by its own
`DeflateStream`, at the level the single stream used; every strip but the last ends with a sync
flush, which is an empty stored block on a byte boundary, so the strips laid end to end are one
valid deflate stream (the construction pigz uses). The zlib header rides on the first strip and
the Adler-32 of all the raw bytes, combined from the strips' own checks by zlib's
`adler32_combine` formula, on the last. Each strip is written as its own IDAT chunk, its CRC
computed on the strip's thread.

The pixels a reader gets are the single-stream writer's; the file's bytes are not (several IDAT
chunks, and a few per cent larger because each strip starts with an empty window). The
plan allowed a faster compression level; none was needed, and the level is unchanged.

## Measured

`head2head_v3\measure_i9.ps1` and `compare_i9.py` (one run for I9 and I10, which share two
scripts), `d04_plot2d.m` and `d06_image.m` unchanged, one session per condition (`runs\i9-first`):
three headless sessions of this build and three of the build before it (f937a6fb) alternated, one
app session, and one eight-pass headless session of each build read over passes 4 to 8 (steady).
Medians of the timed passes, in seconds.

| Row | R2025b | before | I9 | I9 app | I9 steady | before, steady |
|---|---|---|---|---|---|---|
| `d06_generate_2048` | 0.081 | 0.120 | **0.101** | 0.110 | 0.104 | 0.122 |
| `d06_edges` | 0.097 | 0.173 | 0.032 | 0.066 | 0.029 | 0.178 |
| `d06_threshold` | 0.014 | 0.014 | **0.019** | 0.017 | 0.013 | 0.014 |
| `d06_imwrite_x3` | 1.265 | 1.259 | 0.104 | 0.116 | 0.094 | 1.267 |
| `d06_image` wall | 3.948 | 2.707 | 1.196 | 1.220 | 1.180 | 2.756 |

The split probe (`probes\instrumented\split_i9.m`, mean milliseconds a call over five calls
after a warming one, one session per engine, 2048 by 2048):

| Piece | I9 | before | R2025b |
|---|---|---|---|
| `X.^2` | 4.4 | 16.2 | 1.7 |
| `sin(3*X)` | 11.5 | 11.6 | 13.0 |
| `sqrt(X.^2 + Y.^2)` | 9.9 | 38.2 | 8.2 |
| the `d06_generate` expression | 89.5 | 124.0 | 61.2 |
| `(raw - min(raw(:))) / (max(raw(:)) - min(raw(:)))` | 21.1 | 25.1 | 3.3 |
| `[gx, gy] = gradient(blurred)` | 6.2 | 141.7 | 56.3 |
| `sqrt(gx.^2 + gy.^2)` | 10.3 | 31.8 | 8.2 |
| `mag / max(mag(:))` | 8.3 | 10.8 | 2.5 |
| `double(edges > 0.25)` | 5.9 | 6.3 | 2.1 |
| `conv2(bw, ones(9)/81, 'same')` | 5.9 | 8.2 | 3.8 |
| `uint8(255*img)` | 5.5 | 5.1 | 1.3 |
| `imwrite(u, file)` | 26.2 | 414.3 | 435.8 |
| `imread(file)` | 81.1 | 63.6 | 35.2 |

What the probe says about the two rows that are not met. `d06_generate` is twenty elementwise
passes and three reductions: the expression is 89 ms against R2025b's 61 and the normalisation
after it 21 ms against 3.3. The transcendental kernels are level with R2025b's (`sin` 11.5 ms
against 13.0); what is left is the plain passes, each of which moves 32 MB in and 32 MB out
where a fused evaluation would move it once, and the `min` and `max` of four million elements,
which are six times R2025b's (open item 269). `d06_threshold` is three such plain pieces, each
1.5 to 3 times R2025b's in the warmed loop (open item 270); as the script times it the row read
0.019 headless, 0.017 in the app and 0.013 at steady state against 0.014 on the build before in
every condition, so the stage did not move it and its headless reading is inside the scatter of
a row this small. `imread` of the written file is 81 ms against 64 before and R2025b's 35: the
file is the same pixels in thirty-two chunks, and the reader is not a row of the round (open
item 273).

### Checks

- All 8 CHK lines of `d04_plot2d` and `d06_image` (compared by `compare_i9.py` against the
  GUI round's) are byte-identical in all nine sessions, and all 244 CHK lines of the sixteen
  benchmark scripts are identical between this build and the one before it (a run of every
  script on both, for the bits and not for time).
- The three PNGs `d06_image` writes decode to the same pixels as the ones the build before
  wrote (2048 by 2048, compared sample by sample), and are 2 to 6 per cent larger.
- `ImagePipelineI9Tests` (9 tests): the square against the product on every road (a packed
  array, a boxed one, a matrix, a scalar, an anonymous function, a loop long enough to be
  compiled), the other exponents unchanged, a negative base with a whole, a fractional and a NaN
  power; the gradient of a matrix against the general road with the roads off, on and on one
  thread, to seventeen digits, with NaN and infinities in it, at 2 by 2, 2 by n, n by 2 and
  1500 by 1500, and against the central-difference formula R2025b confirmed; `imwrite` and
  `imread` round trips for `uint8`, `double`, colour planes, one pixel, a row, a column and an
  alpha plane; a five-strip PNG whose IDAT chunks, laid end to end, inflate through
  `ZLibStream` (which checks the Adler-32) to exactly the rows written, on one thread and on
  many; and a packed matrix through the picture verbs.
- Gate: the CLI's Debug build; the 3,478 tests of the areas the two stages touch (the new
  classes, histograms, scatters, imaging, codecs, gradients, powers, the packed kernels, the loop
  and body compilers, the ODE solvers, export and rendering) and the 285 parity fixtures, each in
  packed and in boxed storage; the stress suite 88 of 89 (only `stess_40`, open item 254); the
  seven `verify-*.py`; the divergence harvest. The linear-algebra lanes were not run: nothing
  here reaches LAPACK.

## Consequences

- `x.^2`, `x^2` for a scalar, and a square inside a compiled loop or a compiled anonymous
  function are the product. On about one input in four thousand that is one ulp away from what
  every earlier build answered, in the direction of the correctly rounded square and of R2025b.
  No CHK line of the sixteen benchmark scripts moved (244 lines compared between this build and
  the one before it), and no parity fixture.
- `gradient` of a matrix is bit for bit what it was, one or two outputs, at any thread count.
- A PNG written by `imwrite` (and by anything else that reaches `ImageCodec.Write` for an 8-bit
  PNG) decodes to the pixels it did and is not the same file: more IDAT chunks, a few per cent
  larger. A reader that insists on a single IDAT chunk would refuse it; libpng, Skia and R2025b's
  `imread` do not.
- Every imaging builtin that takes a numeric matrix reads it in one threaded pass. The samples
  are unchanged.
- No new switch. The gradient road is under `JGRAPH_VECTOR_FAST` with I5's; the square and the
  strips have no off position, because each is the same answer by a shorter way (the strips) or
  the better answer on every road at once (the square), and a switch would let the roads disagree.

## Divergences

- **`x.^0.5` and `x.^-1` are the C runtime's `pow`.** R2025b's are `sqrt(x)` and `1./x` to the bit
  (probe `probe_pow.m`); here they may differ from those in the last bit. Only the square was
  moved to its exact form in this stage.
- **A PNG written by `imwrite` is not R2025b's file.** The pixels are the same; R2025b writes
  libpng's adaptive row filters in one stream, this build writes unfiltered rows in strips, so
  the bytes and the size differ. This was already so before the strips.

## Still open

- **`d06_generate_2048`** is 0.101 against 0.081 (open item 269): the plain elementwise passes
  and the `min`/`max` reductions over four million elements. The square alone took 12 ms a
  matrix out of the row.
- **`d06_threshold`** is 0.013 to 0.019 against 0.014 (open item 270): `double(mask)`, the 9 by
  9 box and the comparison are each 1.5 to 3 times R2025b's. Not changed by this stage.
- **`x.^0.5` and `x.^-1`** are R2025b's `sqrt(x)` and `1./x` to the bit and are still `pow`
  here (open item 272).
- **`imread`** of a 2048-square PNG is 81 ms against R2025b's 35, and of a logical PNG answers a
  picture value that `>` refuses (open item 273). Found in passing; not a row.
