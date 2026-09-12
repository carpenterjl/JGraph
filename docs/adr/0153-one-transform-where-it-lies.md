# ADR 0153 — One transform, where it lies

## Status

Accepted, in stages. Item 07 of the head2head_v3 gap-closure plan (`docs/plans/gap-closure-07-12-plan.md`,
rev 6, agreed with Codex): the DCT/FFT pipeline behind the `d15_dct_idct_4M` and
`d02_fft_batch32x64k` rows. This ADR carries stages 07a, 07b and 07d; the mixed-radix kernel (07c)
is ADR 0154. Each stage is measured alone against the P0 baseline (`head2head_v3\runs\baseline-4605ef8`,
five counterbalanced repeats of the suite and of the prepared-kernel probes on the committed tree
at 4605ef8) before the next lands.

## Context

`dct(x)` over four million samples took 3.8 s here against 0.24 s in R2025b — the largest single
row of the fifty-five the independent review examined, and three quarters of item 07's excess.
The transform underneath was right; the road to it was not:

- `CosineLine` unpacked the packed vector to a `double[]` (`ToDoubles`), cut it with `SlicesAlong`
  (a second copy), copied each slice into a fresh buffer, and called `CosineTransforms.Forward`,
  which built the even extension as a `Complex[2n]` — 128 MB at n = 4M — and handed it to
  `Fft.Transform(Complex[])`, which split it into two planes (a fourth copy) before the kernel
  saw a number. On the way out every bin was turned by `Complex.FromPolarCoordinates` — a `cos`
  and a `sin` per element — and the result was joined back through `JoinAlong`.
- `Fft.Transform(Complex[])` reaches the span overload of `FftKernels.Transform`, which cannot ask
  for threads inside one transform. 8,000,000 = 2^9·5^6 is not a power of two, so it went to
  Bluestein: one chirp-z transform built from three power-of-two FFTs of m = 2^24, each through
  `PowerOfTwo` → `Factored(…, inside: false)`, serial. The chirp and its transformed mirror — pure
  functions of the length and direction — were recomputed on every call, six 128 MB arrays were
  rented and cleared per call, and the whole thing ran on one core while thirteen idled.

The review's correction stands: it is *one* Bluestein of three FFTs per transform, `n ≤ 32` is
summed directly, and the cited MATLAB Makhoul source is the codegen branch, not evidence of the
runtime algorithm.

## Decision

### 07a — the packed road and threading inside one transform (no arithmetic change)

`CosineTransforms.Forward` and `Inverse` gain an overload that writes into a caller's span and
takes an `inside` flag. The even extension is built over two rented planes of doubles instead of a
`Complex[]`; the FFT is the same kernel; the half-sample turn is spelled as the `Complex` multiply
spelled it, `(re·cos − im·sin)`, and `FromPolarCoordinates(1, θ)` is `(cos θ, sin θ)` exactly,
so the bits are the bits. `CosineLine` takes that road when the input is a packed double array
that is one contiguous line along the transform dimension — a vector, or any array cut along its
only non-singleton dimension — with no padding, cropping or other type asked for: it reads the
buffer where it lies, writes into a `JgsPacking.Allocate`d buffer, and hands the shape across.
Every other shape, class and argument keeps the road it had.

`FftKernels.Transform(Span, Span, n, inverse, inside)` is the new span entry. `inside` reaches
`PowerOfTwo`, and through it `Factored`, whose tiles own disjoint output ranges, so threading
them changes no rounding; and it reaches `Bluestein`, whose two remaining transforms of length m
are power-of-two and factored.

**The plan cache.** A `BluesteinPlan` — the chirp and the transform of its conjugate mirror for
one `(n, inverse)` — is built once, single-flight through a `Lazy`, under a byte budget of
256 MB in all; a plan over 64 MB is built, used and dropped; eviction is by last use under one
lock. A plan is built serially so the bits it holds cannot depend on who built it, and a caller
holds its reference for the call, so eviction never frees memory under a running transform. The
cache does nothing for the 4M row (its plan is 396 MB); it serves repeated mid-size awkward
lengths, which is where `fft` in a loop and the `filter`-family lengths live.

Contract, tested: `TransformRoadTests` keeps the boxed road as it stood as the oracle and asserts
the new road bit for bit at ten lengths across the direct, factored and Bluestein roads, forward
and inverse, serial and threaded; the plan cache's budget and entry limit; the packed `dct`
against the sliced road of before through the script layer. Outside the test suite, the
`head2head_v3\bits` scripts write the full outputs of the 4M `dct`/`idct`, the 4M `fft`/`ifft`,
the 65536-point batch and five smaller lengths as `bits` lines from the baseline build and from
this one, and `compare.py` finds all 28 digests equal.

### 07d — no length threshold for threading inside one transform (decided by measurement)

The factorial the plan asked for — the `d02_fft_batch32x64k` row as written (thirty-two calls of
65,536) against its batched reshape form, and one transform alone at each power of two from 2^15
to 2^22, at the default thread count and at `JGRAPH_THREADS=1`, five repeats each on the 07a
build (`runs\07d-factorial`, `runs\07d-factorial-t1`) — found the row's cost is not the
transform: the batched form is 4.1× cheaper than the loop (0.033 s against 0.137 s), and what the
loop pays is the slice and the per-call fan-out, which is item 12's affine range and is measured
after 12a lands. One transform alone, warm, threaded against one thread: 2^15 and 2^16 equal
(1.12 against 1.12 ms, 1.00 against 1.06 ms), 2^17 1.2× faster threaded, 2^18 1.8×, 2^19 to 2^22
2.2–2.8×. Only the *cold* first call favoured one thread at 2^15 to 2^17 (by 1.15–1.55×), and a
threshold of 2^18 was cut on that reading. It was wrong: with the threshold in, the row's cold
pass went from 0.127 s to 0.195 s in five of five repeats and the warm pass did not move, and
with `DOTNET_TieredCompilation=0` the cold gap closed (0.133 s against 0.126 s). The cold cost is
tier-0 code, which threads divide across cores and one thread does not; there is no length at
which a lone transform is better off serial. `Factored` threads its tiles whenever it is asked
(`inside`), as 07a left it, and `probe_d02_fft_forms` stays in the rig for item 12a's turn.

### 07b — Makhoul's reordering: one length-n transform per DCT

The even extension is a length-2n sequence whose spectrum carries the DCT-II twice. Makhoul's
reordering carries it once: `v[j] = x[2j]`, `v[n−1−j] = x[2j+1]` — the even-indexed samples in
order, then the odd-indexed ones reversed — is a length-n sequence whose DFT `V`, turned by a
quarter-sample phase, has the unnormalized DCT-II as its real part:

    X[k] = w(k) · Re(e^{−iπk/2n} · V[k])

The inverse is the mirror. With `C = X / w` the unnormalized coefficients,
`V[k] = e^{iπk/2n} · (C[k] − i·C[n−k])` for `k ≥ 1` and `V[0] = C[0]` — the real part of
`e^{−iπk/2n}·V[k]` is `C[k]` by construction, and its imaginary part is `−C[n−k]` because `V` is
the spectrum of a real sequence — then one inverse transform of length n, then the reordering
undone. `Forward` and `Inverse` keep their signatures and their `inside` flag; the two rented
planes are now length n; `Matrix(n)` and the orthonormal weights are untouched, so every
`dctmtx`-based identity still holds.

For the 4M row this halves the Bluestein: n = 4,000,000 pads to m = 2^23 where the 8M-point
extension padded to 2^24, so the three transforms inside are each half the length and the plan
is 198 MB instead of 396. For a power of two the one transform of length n replaces one of 2n.

**This moves the last bits.** It is a different operation sequence, so the 07a contract (bits
equal to the boxed road) ends here, and forward and inverse are accepted *independently*
against a reference outside the transform — a permutation or a sign error can cancel in a round
trip and never be seen. `tools/transforms/dct_reference.py` sums the definitions directly in
`mpmath` at 30 digits (cosines by the three-term recurrence, which loses log₁₀ n of them and
leaves more than twenty) for the fixture's own LCG input, and writes
`tests/JGraph.Tests/Numerics/dct_reference.json`: every coefficient of the DCT-II and DCT-III at
n = 8, 33, 100, 1000 and 4096, and four coefficients of each (`k = 1, 7, n/2, n−1`) at 2^20 and
4,000,000, where a full direct sum is hours. `CosineReferenceTests` checks the new road and the
even-extension road it replaced (kept there as `EvenExtensionForward`/`Inverse`) against that
file at `rel ≤ 1e-12` (small lengths, every coefficient) and `rel ≤ 1e-11` (production lengths,
selected coefficients); against closed forms at 2^20, 4,000,000 and the prime 4,000,037 — a
constant (only DC, `√n·c`), one cosine at `k0 ∈ {1, 7, n/2}` (one coefficient, `√(n/2)`) both
directions, a unit impulse (coefficient `k` is `w(k)·cos(πk(2j0+1)/2n)`), and a DC-only spectrum
back to a constant — at `1e-10`; and the round trip at 2^20, 3,981,312 (3-smooth), 4,000,000,
4,000,037 and 2^22 at `1e-9` as a tripwire. The closed-form cosine reduces its integer numerator
modulo 4n before the multiply: unreduced, `π(2j+1)k0/2n` reaches `π·n/2` at `k0 = n/2`, and a
double that size carries 1e-9 of rounding into `cos` — an error of the expectation, which both
roads reproduced to three digits before the reduction.

The accuracy measured, worst |got − reference| over the vector relative to max |reference|,
new road against the even extension (`JGRAPH_DCT_REPORT`):

| length | forward, new | forward, old | inverse, new | inverse, old |
| --- | --- | --- | --- | --- |
| 8 | 7.1e-17 | 1.4e-16 | 1.3e-16 | 1.3e-16 |
| 33 | 6.0e-16 | 5.4e-16 | 2.9e-16 | 1.1e-15 |
| 100 | 6.7e-16 | 3.4e-16 | 3.8e-16 | 5.5e-16 |
| 1000 | 1.1e-15 | 5.1e-16 | 4.4e-16 | 9.0e-16 |
| 4096 | 5.7e-16 | 3.8e-16 | 4.5e-16 | 6.8e-16 |
| 2^20 (selected) | 2.8e-16 | 1.0e-16 | 1.1e-16 | 1.5e-16 |
| 4,000,000 (selected) | 3.0e-16 | 7.4e-17 | 2.7e-16 | 2.5e-16 |
| closed forms, 2^20 to 4,000,037 | — | — | ≤ 2.8e-15 | ≤ 4.6e-15 |

The round trip is 1.2e-15 to 2.4e-15 at every production length. The plan's clause — a length
threshold if the new road is less accurate on any length class — is read against this table:
the forward road is a few ulps further from the reference at six of the seven lengths, the
inverse a few ulps nearer at six of the seven, every entry is within 5·ε of the scale and none
grows with n. That is the rounding of one operation order against another, not a loss of
accuracy, and both roads sit four orders inside the 1e-12 contract; the new road ships at every
length, and this table is the record the clause asked for.

## Consequences

### 07a, measured alone

Five counterbalanced repeats of `d15_signal` and `d02_fft_signal` and of the three probes, the
working tree built by the rig into `runs\07a-packed-road\bin` (labelled dirty: the seven paths of
this stage), against the committed baseline. Medians of five; the paired ratio is JGraph over
MATLAB in the same repeat.

| row | scope | baseline | 07a | speedup | J/M before → after |
| --- | --- | --- | --- | --- | --- |
| `d15_dct_idct_4M` | benchmark row | 3.787 s | 2.250 s | 1.68× | 15.6 → 10.0 |
| `dct(4M)` | probe, cold | 1.961 s | 1.208 s | 1.62× | 11.6 → 7.3 |
| `dct(4M)` | probe, warm | 1.779 s | 1.022 s | 1.74× | 29.2 → 20.5 |
| `idct(4M)` | probe, cold | 1.745 s | 1.059 s | 1.65× | 28.5 → 17.3 |
| `idct(4M)` | probe, warm | 1.799 s | 1.045 s | 1.72× | 31.0 → 19.4 |
| `d02_fft_batch32x64k` | benchmark row | 0.133 s | 0.126 s | 1.06× | 2.4 → 2.8 |
| `d02_fft_4M` | benchmark row | 0.072 s | 0.074 s | 0.97× | 1.0 → 1.2 |

The `d02` rows are powers of two and take no new road; their movement is the noise floor, and
their bits are unchanged (below). `d15_signal` as a process allocated 1.93 GB before and 1.42 GB
after (`GC.GetTotalAllocatedBytes`, five of five repeats each), its peak working set fell from
3.06 GB to 2.72 GB, and its processor-time ratio rose from 1.9 to 3.6 cores kept busy — the three
2^24-point transforms now thread. A single-call plan takes its four arrays from the shared pool
and returns them, as the transform before it did; the first cut of this stage allocated them
fresh and cost the 4M probe 170 MB per call, which the batch counters caught.

Bits: the `head2head_v3\bits` scripts, run on the baseline build and on this one, agree on all 28
`bits` lines — the full 4M `dct` and `idct`, the full 4M `fft` and `ifft`, the 32-by-65536
batch of magnitudes, and `dct`/`idct`/`fft`/`ifft` at 1000, 4096, 65536, 100000 and 262144 (the
FFT set also at 4,000,000). Four lanes green at 8,780 tests each; `m153_transforms` (176 lines)
agrees with R2025b within the rules it carries.

What is left of the row is the algorithm: two Bluesteins of three 2^24-point transforms each for
an 8M-point even extension, which 07b halves and 07c removes.

### 07b, measured alone

The same rig against the 07a numbers: five counterbalanced repeats of the two scripts
(`runs\07b-makhoul`) and of the three probes on the tree as it lands (`runs\07b-makhoul-final`,
built after the 07d threshold came out; the script run carried the threshold, which touches no
d15 row and no batched d02 row).

| row | scope | 07a | 07b | speedup | J/M before → after |
| --- | --- | --- | --- | --- | --- |
| `d15_dct_idct_4M` | benchmark row | 2.250 s | 1.327 s | 1.70× | 10.0 → 5.7 |
| `dct(4M)` | probe, cold | 1.208 s | 0.725 s | 1.67× | 7.3 → 4.2 |
| `dct(4M)` | probe, warm | 1.022 s | 0.562 s | 1.82× | 20.5 → 10.5 |
| `idct(4M)` | probe, cold | 1.059 s | 0.571 s | 1.85× | 17.3 → 9.8 |
| `idct(4M)` | probe, warm | 1.045 s | 0.578 s | 1.81× | 19.4 → 10.5 |
| `d02_fft_batch32x64k` | benchmark row | 0.126 s | 0.130 s | 0.97× | 2.8 → 2.8 |
| `d02_fft_batch32x64k` | probe, cold / warm | 0.127 / 0.107 s | 0.131 / 0.106 s | — | 2.7 → 2.7 |
| `d02_fft_4M` | benchmark row | 0.074 s | 0.076 s | 0.97× | 1.2 → 1.2 |

Against the P0 baseline the row stands at 2.85× (3.787 s → 1.327 s) and its ratio to R2025b at
5.7 from 15.6. `d15_signal` as a process allocates 1.15 GB, from 1.42 GB after 07a and 1.93 GB at
baseline, and its peak working set is 2.42 GB from 2.72 GB. The d02 rows are the noise floor
again, as they must be: the power-of-two road did not change.

Bits: the `head2head_v3\bits` scripts on the baseline build and on this one. Every one of the
thirteen DCT lines moved — `d(2)` from `3f97e1d77b9aed98` to `3f97e1d77b9aedd7`, and the digests
of the full 4M `dct` and `idct` and of both at 1000, 4096, 65536, 100000 and 262144 — while the
round-trip flag held; all fourteen FFT lines (the 4M `fft`/`ifft`, the 65536-point batch, and
`fft`/`ifft` at 1000, 4096, 65536, 100000, 262144 and 4,000,000) are equal. That is the shape
07b predicted: the DCT's last bits belong to the new operation order, whose distance from the
30-digit reference the Decision's table records, and nothing outside the DCT moved.

What is left of the row is Bluestein itself: one chirp-z of three 2^23-point transforms for a
length that is 2^8·5^6, which 07c's mixed-radix kernel takes directly.

## Divergences

- `dct(7)`: R2025b takes a scalar through its FFT and answers `7.0000000000000009`; a scalar is
  its own orthonormal transform and answers `7` here (`m153_transforms`, `dct_scalar`,
  `div=ADR0153`).
- `dct(zeros(0, 1))`: R2025b answers a 0-by-0; the empty column stays a 0-by-1 here
  (`dct_empty_shape`, `div=ADR0153`).
