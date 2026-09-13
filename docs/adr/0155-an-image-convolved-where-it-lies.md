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

## Divergences

None new.
