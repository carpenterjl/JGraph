# ADR 0155 — An image convolved where it lies

## Status

Accepted. Item 08 of the head2head_v3 gap-closure plan (`docs/plans/gap-closure-07-12-plan.md`,
rev 6, agreed with Codex): the two `d06` convolution rows. Two stages, each measured alone against
the stage before it with the same rig: 08a, the marshalling copies; 08b, the packed kernels.

## Context

`conv2` from a script paid for its answer three times. The interpreter holds a matrix
column-major in one packed buffer; `Filters.Convolve2` and `Filters.SeparableConvolve2` take a
row-major `double[,]`; so the builtin unpacked the operand through a strided loop that read one
element per cache line, ran the kernel, and transposed the result back through another. On the
2048-square image of `d06_image.m` each copy is 32 MB read at one useful double per line, and the
two of them were most of the row: the threshold row's kernel is a 9-by-9 box over a mask that is
0.8 % ones, so its arithmetic is 2.7 million multiply-adds inside a row that took 158 ms. The blur row, a
21-tap separable pass each way, is 176 million multiply-adds that already threaded, and the rest
of its time was the copies, the full-size intermediate and the crop.

## Decision

**08a — the two marshalling copies become one tiled transpose.** `MatrixLayout.Transpose`
(`src/JGraph.Numerics/MatrixLayout.cs`) turns a column-major `rows × cols` buffer into a
column-major `cols × rows` one — the same bytes as the source read row-major — by tiles of 64 by
64: a tile's lines in and out are 32 KB each way and stay in one core's cache until the tile is
done. The work is cut into strips of 64 source columns, each of which owns 64 rows of the target
and nothing else, and the strips run in parallel from `ParallelKernels.MemoryBoundThreshold`
upward. `JgsBuiltins.Matrix` and `MatrixToRows` — the entry and exit of every builtin that still
takes a `double[,]`, `conv2` and `filter2` among them — go through it. A transpose is a copy: the
bits cannot move, and the tests and the bits scripts say they do not.

**08b — the packed kernels, column-major in and out.** `Filters` gains a second entry for each
convolution, taking the image as a `ReadOnlySpan<double>` with its `(rows, cols)` and writing
column-major into a span the caller sized with `Filters.Convolve2Size`; the `conv2` builtin hands
two packed real matrices (or a packed image and two tap vectors) to them and adopts the answer
as the value it would have built, so neither marshalling copy, the full-size intermediate nor
the crop is made any more. The boxed entries stay as they were: they are the oracle the tests
hold the new ones to, and `filter2`, `FilterDesign` and every other `double[,]` caller
keep them.

*The separable kernel* keeps the boxed road's pass order exactly, as the plan required after its
first draft had the axes backwards: every input row is convolved with `v` across its columns into
an `ah × (aw+|v|−1)` intermediate, taps ascending, and each answer row is then the sum, taps
ascending, of `u[m]` times the intermediate row `m` above it, both through the same `AddScaled`
that keeps the multiply and the add separate. What changes is which way the loops walk memory:
in column-major storage a row is the strided direction, so the first pass takes a band of 64 rows
at a time and gathers each intermediate column of the band from the input columns behind it,
contiguous stretches both; the second pass walks answer columns, each a contiguous stretch of the
intermediate shifted by a tap. Only the columns the shape asks for are computed in the first
pass, and the second writes straight into the cropped answer. Each intermediate and answer
element therefore receives the same products in the same order as before and rounds to the same
bits; bands and column blocks are cut by the shape alone and own their outputs, so the thread
count changes nothing.

*The general kernel* is output-owned: every 64-by-64 tile of the answer is one block that visits,
in ascending source row and then column, exactly the source pixels that can reach it, skips every
source value equal to zero before any multiply, and adds each product to an accumulator that
started at +0 — the scatter's own order, skip and arithmetic, so each output element sees the
same additions in the same sequence. The plan's sparse branch was not built: the zero test is one
compare per source pixel per tile that can see it, at most a few per pixel for any kernel narrower
than a tile, and a nonzero list would cost a strided pass over the whole source to build, more than
the compares it saves; a branch that cannot pay for itself is a second road to keep equal for
nothing.

**The skip rule is a divergence, now recorded.** R2025b forms every product, so a zero source
under an Inf or a NaN tap is a NaN in its answer; the JGraph scatter has skipped zero sources since
M96, which is what makes the threshold row's arithmetic proportional to the mask's ones, and the
plan's contract for 08b was the scatter's bits. The fixture `m155_image` puts Inf and NaN taps
over a two-thirds-dense mask and over a one-percent one; the NaN and Inf counts, the finite sums
and the zero count differ from MATLAB on both and are recorded below. Everything else in the
fixture agrees with R2025b at `rel=1e-13`, and the six lines that are exact in any order — the
integer counts under `ones(9)`, `ones(4, 6)` and an asymmetric integer kernel, and the density
that is the same `1/81` added a count of times — agree with MATLAB's own bytes (`bits`).

**08c — deferred, as the plan says.** MATLAB documents its separable pass order as columns then
rows; this one is rows then columns, so the last bits of a separable `conv2` differ from MATLAB's
(the fixture holds them at `rel=1e-13`, not `bits`). Changing it would move bits for a
compatibility gain no benchmark row measures, and is not done here.

## Consequences

Measured alone with the rig, five counterbalanced repeats of `d06_image` and of the two probes,
against the 07c binary on the same day (`runs\08-before-07c`, which reproduces the P0 baseline
to the millisecond: threshold 0.158 s, blur 0.146 s).

| row | scope | before | 08a | speedup | J/M before → after |
| --- | --- | --- | --- | --- | --- |
| `d06_threshold` | benchmark row | 0.158 s | 0.071 s | 2.23× | 6.9 → 3.3 |
| `d06_blur_21tap` | benchmark row | 0.146 s | 0.066 s | 2.21× | 4.4 → 2.1 |
| `threshold` | probe, cold / warm | 0.154 / 0.145 s | 0.075 / 0.067 s | 2.1× | 7.1 / 12.1 → 3.7 / 5.3 |
| `blur` | probe, cold / warm | 0.142 / 0.126 s | 0.068 / 0.056 s | 2.1× / 2.2× | 4.4 / 4.5 → 2.2 / 1.9 |
| `d06_total` | script | 3.756 s | 3.575 s | 1.05× | 0.27 → 0.27 |

The plan expected the copies to be a large fraction of the threshold row and a smaller one of the
blur row; they were half of each. Every other `d06` row is unchanged, as is the process's
allocation (1.01 GB; the copies were replaced, not removed — that is 08b).

Bits: `head2head_v3\bits\bits_d06_image.m` on the 07c binary and this one — the full 2048-square
blur and density outputs, the mask and clean fractions, and twenty-one smaller lines over every
shape, an even kernel, asymmetric taps, a kernel larger than the image, a dense kernel and a mask
under Inf and NaN taps: all 25 lines equal.

**08b, measured alone** against the 08a binary the same way (`runs\08b-packed-kernels`):

| row | scope | 08a | 08b | speedup | J/M before → after |
| --- | --- | --- | --- | --- | --- |
| `d06_threshold` | benchmark row | 0.071 s | 0.050 s | 1.42× | 3.3 → 2.4 |
| `d06_blur_21tap` | benchmark row | 0.066 s | 0.045 s | 1.47× | 2.1 → 1.2 |
| `threshold` | probe, cold / warm | 0.075 / 0.067 s | 0.046 / 0.046 s | 1.6× / 1.5× | 3.7 / 5.3 → 2.0 / 3.4 |
| `blur` | probe, cold / warm | 0.068 / 0.056 s | 0.046 / 0.034 s | 1.5× / 1.7× | 2.2 / 1.9 → 1.4 / 1.05 |
| `d06_total` | script | 3.575 s | 3.499 s | 1.02× | 0.27 → 0.27 |

Over the two stages the threshold row is 3.2× faster than the baseline (0.158 → 0.050 s) and the
blur row 3.2× (0.146 → 0.045 s); the warm blur probe is level with R2025b. The probes' process
allocation says where the rest went: the blur probe from 745 MB to 137 MB, the threshold probe
from 1.01 GB to 373 MB, with the peak working set of the blur probe down from 1.08 GB to 300 MB.
What remains of the threshold row is not the convolution: `edges > 0.25`, `double(...)` twice and
`dens > 0.5` are four passes over 32 MB, and the mask's own convolution is a few milliseconds.
Every other `d06` row is unchanged.

Bits: `bits_d06_image.m` on the 08a binary and this one, all 25 lines equal — the two full-size
answers and every smaller shape, kernel and special-value line — as `FilterKernelsM96Tests`
already said over the same cases at one thread and at sixteen.

## Divergences

- `conv2` with an Inf or NaN tap over a source that has zeros: R2025b forms every product, so a
  zero under such a tap answers NaN; the scatter skips zero sources before any multiply and
  answers the sum of the other products (`m155_image`, `odd_nan_count`, `odd_inf_count`,
  `odd_finite_sum`, `sparse_odd_nan_count`, `sparse_odd_inf_count`, `sparse_odd_finite_sum`,
  `sparse_odd_zero_count`, `div=ADR0155`). A rule the imaging layer has had since M96, surfaced
  by this fixture; kept because it is what item 08's bit-for-bit contract was written against.
