# ADR 0153 — One transform, where it lies

## Status

Accepted, in stages. Item 07 of the head2head_v3 gap-closure plan (`docs/plans/gap-closure-07-12-plan.md`,
rev 6, agreed with Codex): the DCT/FFT pipeline behind the `d15_dct_idct_4M` and
`d02_fft_batch32x64k` rows. This ADR carries stages 07a and 07b; the mixed-radix kernel (07c) is
ADR 0154. Each stage is measured alone against the P0 baseline (`head2head_v3\runs\baseline-4605ef8`,
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

### 07b — pending

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

## Divergences

- `dct(7)`: R2025b takes a scalar through its FFT and answers `7.0000000000000009`; a scalar is
  its own orthonormal transform and answers `7` here (`m153_transforms`, `dct_scalar`,
  `div=ADR0153`).
- `dct(zeros(0, 1))`: R2025b answers a 0-by-0; the empty column stays a 0-by-1 here
  (`dct_empty_shape`, `div=ADR0153`).
