# ADR 0333 — The every-row plan's I8: convolution in blocks, interpolation read and written in place

## Status

Accepted, 2026-11-10. Stage I8 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 12). **Done**: all eight rows are at or under R2025b's median in the app and at steady
state. `d09_conv_200k_x64` (0.002 app, 0.001 steady, against 0.001) and `d09_interpft` (0.004 app, 0.003
steady, against 0.003) are within the timer's millisecond resolution, which decision D3 allows. Every CHK
line is byte-identical.

## Context

The GUI round (ADR 0322) left eight `d09_polyinterp` rows to I8, 0.055 s of excess between them:

| Row | JGraph | R2025b |
|---|---|---|
| `d09_conv_200k_x64` | 0.010 | 0.001 |
| `d09_interp1_spline_2M` | 0.008 | 0.004 |
| `d09_interp1_pchip_2M` | 0.007 | 0.003 |
| `d09_interp1_makima_2M` | 0.007 | 0.005 |
| `d09_interp1_nearest_2M` | 0.007 | 0.004 |
| `d09_interp2_linear_1M` | 0.033 | 0.020 |
| `d09_interp2_spline_1M` | 0.165 | 0.146 |
| `d09_interpft` | 0.004 | 0.003 |

Reading each verb down to its arithmetic found the following:

- **conv walked the whole answer once per tap.** For each of 64 taps it added one scaled copy of the
  signal into the full 1.6 MB answer. That is 64 passes over memory no cache keeps, all on one thread, and
  the three shapes computed the full convolution and then copied out a window.
- **interp1 copied in and out.** The query points were flattened into a managed array, the answers built
  in another, and the whole result copied into the script value. The cubic and nearest rules each
  evaluated a point in a few nanoseconds; the copies were most of each row.
- **interp2 searched every axis by bisection and solved the spline one line at a time.** Every query
  point's cell was found by an 11-step bisection over 1200 knots, even though the grid is a linspace. The
  sample was a general N-dimensional loop over 2^d corners with per-corner index arithmetic. For a
  spline, the slopes along each axis re-ran the whole not-a-knot tridiagonal elimination on every one of
  the grid's 2400 lines, although the elimination depends only on the axis.
- **interp1 sorted knots that were already sorted.** `SortedOrder` ran a full sort even when the samples
  were already strictly increasing.
- **interpft built a complex answer for a real record.** It allocated a `Complex[]` and then converted it
  to a real value.

## Decision

Every road computes the same numbers in the same order as the road it replaces. Each is held to the old
algorithm bit for bit in the tests, with the old code kept verbatim in the test where it no longer exists.

### Direct convolution in blocks (`Convolution.ConvolveInto`)

The answer is cut into 2048-element (16 KB) blocks. Each block is cleared, then every tap's scaled stretch
of the signal is added in, taps in ascending order, using `TensorPrimitives.MultiplyAdd` (`Add` for a unit
tap; a zero tap is skipped, as the old loop's product added nothing). Each output therefore receives the
same products in the same order as when the whole answer was walked tap by tap, so the result is the same
to the bit; a block only changes which outputs are worked on together. The blocks run through
`ParallelKernels.ForBlocks` from 1M multiply-adds. `ConvolveInto(u, v, start, destination)` computes only
the window asked for: `'same'` and `'valid'` no longer compute the full answer and copy out of it, and
`conv` of packed real operands writes straight into the packed result. `Window` is public so the builtin
can name the window.

### `KnotSearch`: a divide on even axes, bisection otherwise (Numerics)

`KnotSearch.For(axis)` decides once whether an axis is evenly spaced. The test uses a tolerance of
|step|·1e-10 and fails on NaN knots. On an even axis, `Cell(at)` estimates the cell by a divide and
corrects it against the knots at most once in each direction. If the estimate is still wrong it falls back
to `Bisect`. The answer is always the cell bisection gives: the largest i in [0, n−2] with axis[i] ≤ at,
clamped. The tests check this on even, uneven, nearly even (+5e-14) and stepped (+1e-6) axes, and on axes
holding NaN.

### `GridSampler` searches with `KnotSearch` and samples two dimensions directly

Each axis gets a `KnotSearch`, shared with the samplers made for other threads (`ForAnotherThread`), and
the private `Bracket` is removed. A rank-2 linear sample is the bilinear sum, written out in the
general loop's order: the total starts at 0, then adds the products (a0·a1)·v00, (t0·a1)·v10, (a0·t1)·v01
and (t0·t1)·v11. A rank-2 spline sample is the 16-term Hermite sum in the general loop's order, skipping
zero weights as it did. Other ranks and methods keep the general loop.

### The spline's elimination is computed once per axis (`Interpolation.SplineLinePlan`)

The not-a-knot system's end rows and its forward-elimination factors depend only on the knots, so
`SplineLinePlan.For(x)` computes them once (null for an axis shorter than 4, where the old per-line code
still runs). `Solve(source, answer, start, stride, count)` then solves `count` lines in place in the
answer. Each line builds its right-hand side, sweeps down and back-substitutes using exactly
`SplineSlopes`' operations. `GridSampler.SlopesAlong` solves blocks of 64 interleaved lines, or single
contiguous lines on the first axis, through `ForBlocks` from 65,536 elements.

### Packed roads for interp1, interp2 and conv

- **interp1:** with one data set and packed query points, interp1 reads the points where they lie and
  writes each answer, `ValueAt`'s as before, straight into the packed result, threaded.
- **interp2:** with two packed query arrays of the same shape, each grain gets its own sampler and
  writes into the packed result. `GridValuesOf` copies a packed grid with a threaded
  `FactorFills.Copy`, and `ReadAxes` reads a packed meshgrid's axis straight from its storage.
- **Sorted knots:** `SortedOrder` returns the identity order for strictly increasing samples, which is
  the order a stable sort gives.
- **interpft:** a real record's answer is written into a `double[]`.

### Optimised at the first call

A benchmark row is a single call, so the first passes of a session run the per-point loops as tier-0 JIT
code. The first measurement (`runs\i8-first`) showed interp1 at 10-12 ms in its cold passes against
3 ms warm, slower than the build before I8 in those passes, and conv at 30 ms against 1 ms. The interp1
loop is therefore a static `ValuesAt` marked `AggressiveOptimization`. `ValueAt`, `CubicAt` and
`Convolution`'s block loop carry the same attribute. These attributes change only when the code is
compiled, not what it computes.

## Measured

`head2head_v3\measure_i8.ps1` and `compare_i8.py`, `d09_polyinterp` unchanged, one session per condition
(`runs\i8-second`, after the tier-0 change; `runs\i8-first` was before it):

- three headless sessions of this build and three of the build before it (31c9c649), alternated;
- one app session;
- one eight-pass headless session of each build, read over passes 4 to 8 ("steady");
- the split probe on both builds, in R2025b, and on this build at one thread.

Medians of the timed passes, in seconds.

| Row | R2025b | before | I8 | I8 app | I8 steady | before, steady |
|---|---|---|---|---|---|---|
| `d09_conv_200k_x64` | 0.001 | 0.010 | 0.005 | 0.002 | 0.001 | 0.009 |
| `d09_interp1_spline_2M` | 0.004 | 0.012 | 0.004 | 0.003 | 0.004 | 0.008 |
| `d09_interp1_pchip_2M` | 0.003 | 0.012 | 0.004 | 0.003 | 0.003 | 0.008 |
| `d09_interp1_makima_2M` | 0.005 | 0.012 | 0.004 | 0.003 | 0.004 | 0.008 |
| `d09_interp1_nearest_2M` | 0.004 | 0.011 | 0.003 | 0.003 | 0.003 | 0.007 |
| `d09_interp2_linear_1M` | 0.020 | 0.042 | 0.016 | 0.008 | 0.004 | 0.024 |
| `d09_interp2_spline_1M` | 0.146 | 0.146 | 0.030 | 0.024 | 0.017 | 0.150 |
| `d09_interpft` | 0.003 | 0.004 | 0.004 | 0.004 | 0.003 | 0.004 |
| `d09_interp1_linear_2M` (not I8's) | 0.007 | 0.012 | 0.003 | 0.003 | 0.002 | 0.007 |
| `d09_total` | 12.639 | 0.754 | 0.594 | 0.488 | 0.444 | 0.638 |
| `d09_polyinterp` wall | 12.670 | 0.756 | 0.596 | 0.491 | 0.453 | 0.651 |

Headless conv is 0.005: one headless pass in nine still took 0.030, and the others were 0.003-0.007. The app
and steady columns, the conditions the GUI round compares, are level with R2025b. `d09_polyval_2M` (not an I8
row and not touched by I8) varies between sessions on both builds, 0.004-0.025.

The split probe (`probes\instrumented\split_i8.m`, mean milliseconds per call over five calls after a
warming one, `runs\i8-second`):

| Piece | I8 | before | R2025b | I8, 1 thread |
|---|---|---|---|---|
| `conv`, 200k by 64 | 4.8 | 10.1 | 1.5 | 10.6 |
| `conv(..., 'same')`, 200k by 64 | 2.5 | 10.6 | 0.9 | 9.0 |
| `conv`, 200k by 2000 | 39.4 | 260.9 | 3.8 | 30.5 |
| `interp1` linear, 2M | 4.1 | 43.0 | 6.0 | 13.7 |
| `interp1` spline, 2M | 4.9 | 67.2 | 3.5 | 18.1 |
| `interp1` pchip, 2M | 4.5 | 20.6 | 3.3 | 17.4 |
| `interp1` makima, 2M | 4.5 | 13.0 | 6.0 | 17.9 |
| `interp1` nearest, 2M | 4.2 | 5.9 | 4.3 | 15.0 |
| `spline` build, 20k | 2.4 | 8.7 | 4.9 | 2.1 |
| `interp2` linear, 1M, meshgrid matrices | 39.0 | 50.7 | 17.5 | 36.0 |
| `interp2` linear, 1M, grid vectors | 7.7 | 26.5 | 8.7 | 15.6 |
| `interp2` spline, 1M | 32.0 | 151.6 | 141.6 | 102.3 |
| `interp2` spline, 10 points (the setup) | 19.7 | 116.2 | 123.0 | 46.8 |
| `interpft`, 20k to 80k | 3.1 | 3.1 | 2.0 | 10.0 |

What the probe says:

- **Spline setup.** The spline grid's setup, once per axis instead of once per line, went from 116 ms to
  20 (R2025b 123), which accounts for most of `interp2_spline`'s gain.
- **Threads.** The interp1 roads gain about 3x from threading on top of the removed copies. The
  one-thread column is still at or under the build before for every piece but nearest and interpft,
  whose cost is the arithmetic.
- **Long kernels.** At 2000 taps, direct convolution is 400M multiply-adds and cannot reach R2025b's
  3.8 ms, which is not direct (open item 277).
- **Meshgrid matrices.** The matrix-grid interp2 call costs 30 ms more than the vector form in this
  probe, though the row itself is 4-16 ms (open item 278).

The probe's checksum is the same in R2025b and in all four JGraph conditions.

### Checks

- All 19 CHK lines of `d09_polyinterp` are byte-identical in every session of both measurements (nine per
  run, both builds) to the GUI round's JGraph pass, and identical between this build and 31c9c649 in a
  run for the bits, not for time.
- `InterpolationConvolutionI8Tests` (Numerics, 33 cases) holds each new road to the old algorithm:
  - convolution in blocks against the tap-by-tap loop, kept verbatim, over sizes from one element to
    long by long, with zero, unit, negative-zero and subnormal-scale taps over a signal holding
    infinities, NaN and negative zero, the windows of `'same'` and `'valid'`,
    and the refusals;
  - `KnotSearch` against bisection on even, uneven, nearly even, stepped and NaN axes;
  - `SplineLinePlan` against `SplineSlopes`, contiguous and strided;
  - `GridSampler` against the pre-I8 sampler, kept verbatim in the test (`GridSamplerAsItWas`), for
    linear, spline, nearest and cubic in two dimensions and spline in three, inside and outside the
    grid, the same bits.
- `InterpolationConvolutionI8Tests` (Scripting, 10 cases):
  - `conv` against a tap loop in every shape and orientation, mixed storage and scalars;
  - interp1's packed road against its general road for every method, with extrapolation and fill
    values, unsorted knots and one point;
  - interp2's packed pairs against the grid-vector form for linear, nearest, cubic and spline, an
    uneven grid and interp3, and a million points;
  - interpft's real answers.
- Gate: the CLI's Debug build; 2,609 tests of the interpolation, convolution, polynomial, signal, filter,
  data-analysis, spectral and numeric areas (all of `JGraph.Tests.Numerics`) in packed and in boxed
  storage; the 285 parity fixtures in packed and in boxed storage; the stress suite 88 of 89 (only
  `stess_40`, open item 254); the seven `verify-*.py`; the divergence harvest (1350 from 220 ADRs). The
  four backend lanes do not apply: no BLAS or LAPACK road changed. After the tier-0 change, the 79 tests of
  the I8, interp1 and convolution classes passed in both storages.

## Consequences

- `KnotSearch` is the cell search for evenly spaced axes in Numerics. A new sampler should take it rather
  than write another bisection; `interp1`'s `UniformKnots` is the same rule in the scripting layer, and
  the two can merge.
- `SplineLinePlan` makes the spline's per-axis work linear in the lines, so `interp2`/`interpn` spline
  setup is now cheaper in JGraph than in R2025b.
- A per-point loop that one benchmark call runs should be optimised at its first call. In a cold pass
  tier-0 code cost interp1 more than the copies this stage took out (the first measurement).

## Divergences

None new. Every changed road answers what the old one answered, bit for bit.

## Still open

- **Long-kernel convolution** (open item 277): 200k by 2000 taps is 39 ms against R2025b's 3.8, which
  is beyond what direct convolution can reach; it needs R2025b's crossover probed, and an FFT road
  moves bits.
- **interp2 over meshgrid matrices** (open item 278): a 50 ms first headless pass and 31-39 ms repeated
  calls in the probe, against 4-16 ms in the row.
