# ADR 0334 — The every-row plan's I12: the IIR recurrence in place, the cosine pair at half length, a real matrix's fft2

## Status

Accepted, 2026-11-10. Stage I12 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 13). **Partly done.**

- I12's own two rows, `d15_dct_idct_4M` and `d15_filter_iir_10M`, are well under R2025b.
- Of the three d02 transform rows rev 3 added, `d02_fft2_2048` and `d02_ifft_4M` are under.
- `d02_iir_10M` (not an I12 row) is under too, because it runs the same recurrence.
- `d02_fft_4M` is the one miss: 0.044 against 0.040 in the single app session, though 0.036 headless
  and 0.028 at steady state. Its road is unchanged; see Decision and open item 280.

Every CHK line is byte-identical to the build before.

## Context

The GUI round (ADR 0322) gave I12 two rows, and rev 3 of the plan (from I11, ADR 0323) added the cost of
making a real input's spectrum exactly conjugate-symmetric to three d02 rows:

| Row | JGraph | R2025b |
|---|---|---|
| `d15_dct_idct_4M` | 0.210 | 0.136 |
| `d15_filter_iir_10M` | 0.085 | 0.068 |
| `d02_fft_4M` | 0.038 (I11 probe) | 0.040 |
| `d02_ifft_4M` | 0.049-0.051 | 0.045 |
| `d02_fft2_2048` | 0.035-0.042 | 0.031 |

Reading the code found five things:

- **The cosine pair already used Makhoul's form.** ADR 0153 had made it one length-n FFT, not the
  mirror-and-2n FFT the plan assumed. What was left:
  - the transform itself: 4,000,000 = 2^8·5^6 on the mixed-radix road, about 55 ms;
  - serial passes around it: a cosine and a sine per coefficient, two reorders, and, in the inverse, a
    serial division by n over both planes.
- **filter with feedback never reached packed storage.** `PackedFilterOps` only took the
  feed-forward case. A real denominator went to the boxed road:
  - it flattened the signal, cut it into slices, joined the answer and repacked it, four passes over
    80 MB;
  - in between, `DigitalFilter.Filter` ran the transposed recurrence through a general inner loop over
    a pooled delay array.
- **Every inverse FFT ended with a serial divide.** `FftKernels.Transform`'s 1/n scale was a scalar loop
  over both planes; for a 4M line it was most of the gap between `ifft` and `fft`.
- **fft2 of a real matrix did twice the work it needed.** It made three passes:
  - every column transformed as a complex sequence with a zero imaginary half;
  - every row of the result;
  - then `ConjugateSymmetry.Force` to average each element with its mirror.

## Decision

### The recurrence in registers, on packed storage (`FilterKernels.Recurrence`, `RecurrenceAlong`)

`Recurrence(b, a, x, y, delays)` is the transposed direct-form II loop for normalized, padded
coefficients. Delay lines of one to four live in locals, with a general loop for longer ones. Each step
keeps the old loop's expression to the operation, so the answer is bit for bit the old one:

- `output = b0·x + d0`, then `d_j = ((b_{j+1}·x) + d_{j+1}) − (a_{j+1}·output)` in ascending `j`;
- the last delay adds a literal `0.0`, where the loop added its missing neighbour; that turns a −0 into
  +0 just as the loop did;
- zero coefficients are still multiplied, so an infinity or NaN travels as it did.

The method is `AggressiveOptimization`.

- `RecurrenceAlong` runs it once per slice of packed column-major storage. A contiguous slice runs where
  it lies, a strided one is gathered and scattered, slices are spread over threads (each chain belongs to
  one thread), and the final conditions are laid out as `FeedForwardAlong`'s.
- `PackedFilterOps` takes a feedback denominator onto this road (`JGRAPH_IIR_FAST=0` turns it off).
- `DigitalFilter.Filter`'s own loop now calls `Recurrence`, so there is one implementation.

A recurrence is one dependency chain, so the gain is everything around it: about 3 ns a sample against
R2025b's 7.

### The inverse scale vectorised and threaded (`FftKernels.ScaleDown`)

Both planes are divided by n with `TensorPrimitives.Divide`, threaded from 2^18 elements when the
transform may use the machine (`inside`). Division is correctly rounded lane by lane, so the bits are the
scalar loop's.

### The cosine pair: threaded passes, and half length for long even lines (`CosineTransforms`)

- **Threaded passes.** The reorder, the quarter-sample turn and the inverse's spectrum build run in
  grains through `ParallelKernels.For` with unchanged per-element arithmetic. Odd lines and lines below
  4096 keep the old bits exactly.
- **Half-length road.** An even line from `HalfLengthFrom` = 4096 up takes it (`JGRAPH_DCT_HALF=0`
  turns it off):
  - Makhoul's reordered sequence `v` is real. It is packed as `z[j] = v[2j] + i·v[2j+1]`, transformed at
    length n/2, and split: `E = (Z[k] + conj Z[m−k])/2`, `O = (Z[k] − conj Z[m−k])/2i`,
    `V[k] = E + e^{−2πik/n}·O`.
  - Each pass over `k ≤ m` writes the coefficients at `k` and `n − k` together; the turn at `n − k` is the
    same angle's sine and cosine exchanged.
  - The inverse forms the full road's spectrum at `k` and `k + m`, folds it to `Z = E + i·O`, transforms
    back at n/2, and reads the even and odd samples from the two planes.

  This road changes the rounding, recorded below.

### A real matrix's fft2 (`FftKernels.RealForward2`)

A real packed double matrix takes this road when both sides are powers of two the tile kernel walks
directly (16 to 32,768), with at least 65,536 elements. Called with no resize, `fft2`/`fftn` use it
(`JGRAPH_FFT_REAL=0` turns it off):

1. Sixteen columns per tile, two per complex lane (`a + i·b`). One transform then gives both columns'
   spectra, as `A = (Z[k] + conj Z[−k])/2` and `B = (Z[k] − conj Z[−k])/2i`, written for rows 0 … m/2
   only.
2. Those m/2 + 1 rows are transformed along the second dimension. Rows 0 and m/2, whose inputs are real,
   are made exactly symmetric the way a real line's transform is.
3. The upper rows are written as `F[k1, k2] = conj F[m − k1, −k2]`.

The answer is therefore exactly Hermitian through the origin by construction, and the forcing pass is
skipped. It differs from the general road's answer in the last bits.

### What was tried and taken out: a half-length road for a 1-D real fft and its inverse

The same split for one long real line made `fft` of 4M about 4 ms faster, and its mirror made `ifft`
faster still. But `d02_roundtrip` (`%.3g` of the round trip's largest error) then printed 2.89e-15, or
2.66e-15 with both directions on the new road, instead of today's 1.67e-15; R2025b prints 1.78e-15. A
printed CHK line moving further from R2025b is not what D1 allows, and the row was already under, so the
code was removed. `d02_fft_4M`'s road is the one I11 left.

## Measured

`head2head_v3\measure_i12.ps1` and `compare_i12.py` (`runs\i12-first`), the scripts unchanged:

- three headless sessions of `d15_signal` + `d02_fft_signal` per build, this one and 684a4eb7 ("before"),
  alternated;
- one app session;
- one eight-pass headless session per build, read over passes 4 to 8 ("steady");
- the split probe on both builds and in R2025b.

Medians of the timed passes, in seconds:

| Row | R2025b | before | I12 | I12 app | I12 steady | before, steady |
|---|---|---|---|---|---|---|
| `d15_dct_idct_4M` | 0.136 | 0.231 | 0.082 | 0.081 | 0.080 | 0.225 |
| `d15_filter_iir_10M` | 0.068 | 0.091 | 0.033 | 0.032 | 0.034 | 0.086 |
| `d02_iir_10M` (not I12's) | 0.090 | 0.069 | 0.033 | 0.031 | 0.031 | 0.068 |
| `d02_fft2_2048` | 0.031 | 0.025 | 0.016 | 0.016 | 0.015 | 0.021 |
| `d02_ifft_4M` | 0.045 | 0.039 | 0.031 | 0.036 | 0.033 | 0.040 |
| `d02_fft_4M` | 0.040 | 0.034 | 0.036 | 0.044 | 0.028 | 0.033 |
| `d15_total` | 1.342 | 0.865 | 0.652 | 0.676 | 0.568 | 0.775 |
| `d02_total` | 1.501 | 0.695 | 0.635 | 0.746 | 0.594 | 0.666 |

`d02_fft_4M`'s app passes were 0.044, 0.042 and 0.046. Its road did not change, and the build before
reads the same headless.

The split probe (`probes\instrumented\split_i12.m`, mean ms a call over five calls after a warming one):

| Piece | I12 | before | R2025b |
|---|---|---|---|
| `dct`, 4M | 36.7 | 102.0 | 56.6 |
| `idct`, 4M | 54.1 | 119.9 | 57.1 |
| `filter`, order 4, 10M | 29.3 | 87.0 | 68.1 |
| `filter`, order 2, 10M | 23.1 | 65.4 | 67.7 |
| `fft`, real 4M (2^22) | 36.1 | 42.4 | 43.8 |
| `ifft`, 4M | 34.6 | 40.8 | 34.0 |
| `fft2`, real 2048² | 17.5 | 29.8 | 28.1 |
| `fft`, real 4,000,000 | 63.2 | 64.4 | 16.4 |

The last line is the mixed-radix road at a length that is not a power of two: R2025b is 4x faster there
(open item 281).

### Checks

- **CHK lines.** All 17 CHK lines of `d15_signal` and `d02_fft_signal` are identical in every session of
  both builds. Against the GUI round only `d02_roundtrip` differs, 1.79e-15 → 1.67e-15, which is I11's move
  (ADR 0323), present in the build before as well.
- **The probe at 17 digits.**
  - Both IIR outputs, `fft_peak`, `fft_energy`, the round trip and the `fft2` sum are identical between
    the builds.
  - The half-length cosine pair moves `dct`'s second coefficient from 0.023322455329191274 to
    0.023322455329259525 (R2025b 0.023322455329191371). That is 7e-14 absolute, against coefficients up to
    about 1e3, so rounding.
  - The `idct` round trip's largest error moves from 4.2e-15 to 7.1e-15 (R2025b 2.9e-15).

  Both print the same at the script's `%.8g` and under its 1e-9 test. They are recorded here as D1 asks.
- **`SignalTransformsI12Tests` (Numerics, 32 cases).**
  - The recurrence against `DigitalFilter`'s old loop, kept verbatim, at orders 1-7, seeded and from rest,
    with negative zeros, an infinity, a NaN and a zero feedback coefficient, final delays included.
  - `RecurrenceAlong` against the per-slice loop down columns and along rows.
  - The threaded inverse scale against the unscaled mixed-radix transform divided in a scalar loop.
  - The cosine full road against the pre-I12 code, kept verbatim, to the bit, threaded and not.
  - The half road against the full road to rounding, its round trip, and independence from threading.
  - `RealForward2` against column-then-row complex transforms to 1e-14 of scale, exactly Hermitian, with
    the four self-mirrored elements real.
  - The shapes the road takes.
- **`SignalTransformsI12Tests` (Scripting, 3 cases).**
  - `filter` with feedback against the recurrence written out in the script, to the bit: four filters,
    row and column signals, `zi` and `zf`, a matrix along each dimension, and non-finite samples.
  - `dct`/`idct` against the definition at selected coefficients and in round trips, both roads, the
    slice road against the vector road.
  - `fft2` of a real matrix: exactly Hermitian and the general transform to rounding at five shapes, and
    `ifft2` back to real.
- **Gate.**
  - 975 tests of the transform, filter, signal, cosine and FFT classes, and 257 parity fixtures, each in
    packed and in boxed storage.
  - The stress suite: 88 of 89 pass; the failure is `stess_40`, open item 254.
  - The seven `verify-*.py` and the divergence harvest.
  - The four backend lanes do not apply, since no BLAS or LAPACK road changed.

## Consequences

- A feedback filter over packed storage no longer leaves it. `filter` is now under R2025b in both of its
  forms.
- `ScaleDown` is the inverse scale for every `FftKernels.Transform`, so every long inverse FFT is faster
  by a pass, with the same bits.
- The real-matrix road is the place to add `fftn` over more dimensions, or rectangular sides the tile
  kernel factors, if a row ever asks.

## Divergences

None new against R2025b. The cosine pair and `fft2` of a large real matrix round differently from the
build before in their last bits; the values are recorded above, and no printed line moved.

## Still open

- **`d02_fft_4M`** (open item 280): under R2025b headless and at steady state, but 0.044 against 0.040 in
  the one app session. A half-length road would cost the `d02_roundtrip` line.
- **The mixed-radix transform** at 4,000,000 points (open item 281): 63 ms against R2025b's 16.

Closed since: `filter`'s final conditions (open item 279) are laid out delays first, as R2025b lays them
out, and its initial conditions are read the same way (ADR 0345).
