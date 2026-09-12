# ADR 0154 — A smooth length takes its own road

## Status

Accepted. Stage 07c of item 07 of the head2head_v3 gap-closure plan
(`docs/plans/gap-closure-07-12-plan.md`, rev 6, agreed with Codex); the stages before it — the
packed road, the plan cache, Makhoul's reordering, and the threading decision — are ADR 0153.
Measured alone against the 07b numbers before anything after it lands.

## Context

After 07b the `d15_dct_idct_4M` row was one Bluestein per transform: a length of 4,000,000 is not
a power of two, so the kernel padded it to 2^23 and took three power-of-two transforms of that
length plus two chirp multiplies — roughly six times the arithmetic of a transform of the length
itself, on a plan of 198 MB that the cache cannot hold. The row stood at 1.33 s against R2025b's
0.24 s, and every other `fft` of an awkward length in the build paid the same way: `fft` of a
signal that is 44,100 samples a second long, a `filter` whose length is a round number, an
`ifft` back from either.

4,000,000 is 2^8·5^6. A length whose prime factors are 2, 3 and 5 — a *5-smooth* length, which is
most lengths a person chooses — has an exact fast transform of its own: a pass per factor, no
padding, and O(n log n) work at a constant that is the radix's own.

## Decision

`MixedRadixFft` (`src/JGraph.Numerics/MixedRadixFft.cs`): a Stockham autosort transform with
radix-2, 3, 4 and 5 passes, taken by `FftKernels.Transform` for a length above the direct limit
that is 5-smooth and not a power of two. The dispatch order is unchanged for everything else: a
power of two still walks its stages or factors (`IsFactored` keeps its exact meaning and is never
asked about a mixed length), a length at or below 32 is still summed directly, and a length with
a prime factor above 5 still goes to Bluestein — whose inner transforms are powers of two and so
never reach this kernel.

**The pass.** A pass of radix r over the length not yet reduced, n′, with stride s (the product of
the radices already applied) reads, for every p below n′/r and every q′ below s, the r points
`x[q′ + s·(p + q·n′/r)]`, takes their r-point transform, turns output q by `e^{∓2πi·pq/n′}`, and
writes it to `y[q′ + s·(r·p + q)]`. The sort is in the write addresses, so the answer lands in
natural order with no bit reversal, and each pass streams one pair of planes into the other; a
work buffer is rented from the shared pool and the result copied back only when the pass count
is odd. The radix-3 and radix-5 butterflies are the standard ones (two cosines and two sines of
the fifth of a turn, folded so a butterfly is adds, a handful of multiplies and one turn per
output); radix-4 multiplies by ∓i, which is a swap and a sign.

**The plan.** The twiddles are a table per pass, `(n′/r)·(r−1)` entries, computed once per length
and direction with the angle reduced modulo n′ before the cosine is taken. The tables sum to
about n complex numbers — 64 MB at 4,000,000, which is under the 64 MB entry limit and so cached,
where Bluestein's 198 MB plan for the same length could not be. The plan lives in the cache 07a
built, generalised: `FftKernels.FftPlan` is the base both kinds share (bytes, last use, charge,
release), `PlanFor(n, inverse)` chooses the kind by the length, and the budget, the entry limit,
the single-flight construction and the eviction are the same code.

**Threads.** Every (p, q′) pair owns its r outputs, so a pass is cut into blocks — along p when
there are enough of those, else along q′, and the cut is a function of the shape alone, never of
the thread count — and the blocks run in parallel when the caller said `inside` and the pass has
at least 2^14 work items. A threaded pass and a serial one perform the same operations on the
same numbers, so the bits are the same, and the tests assert it.

**Contract.** Bit-for-bit against the road before is not claimed: a different arithmetic rounds
differently. Forward and inverse are accepted *independently* against the 30-digit reference
`tools/transforms/fft_reference.py` writes (`tests/JGraph.Tests/Numerics/fft_reference.json`:
every bin at 96, 100, 360, 1000 and 1080, which between them use every radix and every mixture;
two bins at 4,000,000) at `rel ≤ 1e-12` and `1e-11`; against closed forms at 4,000,000, 3,981,312
and 1,000,000 (one complex exponential to one bin and back, an impulse to a flat spectrum) at
`1e-10`; and the round trip at `1e-12` as a tripwire. `MixedRadixReferenceTests` measures the
Bluestein road it replaced against the same reference (kept reachable as
`FftKernels.BluesteinTransform`) so the table below is a comparison, not a claim. The parity
fixture `m153_transforms` already holds `fft`/`ifft` at 100, 1000, 98,304 (3-smooth) and 100,000
(5-smooth) against R2025b at `rel=1e-12`, and `FftKernelsM96Tests` keeps its bit-for-bit clause
for the lengths that still take the direct road, with the smooth lengths that left it moved to a
tolerance of their own. Outside the suite, the `head2head_v3\bits` FFT script must show the
power-of-two lines unchanged and the 4,000,000 lines moved, and the DCT script the same.

The accuracy measured, worst |got − reference| over both planes relative to the largest
|reference|, the mixed-radix road against Bluestein's on the same input (`JGRAPH_FFT_REPORT`):

| length | forward, mixed radix | forward, Bluestein | inverse, mixed radix | inverse, Bluestein |
| --- | --- | --- | --- | --- |
| 96 = 2^5·3 | 3.4e-16 | 6.7e-16 | 3.6e-16 | 8.4e-16 |
| 100 = 2^2·5^2 | 2.5e-16 | 1.1e-15 | 2.5e-16 | 8.3e-16 |
| 360 = 2^3·3^2·5 | 4.5e-16 | 1.0e-15 | 4.5e-16 | 8.5e-16 |
| 1000 = 2^3·5^3 | 4.4e-16 | 1.2e-15 | 4.4e-16 | 1.1e-15 |
| 1080 = 2^3·3^3·5 | 5.4e-16 | 1.0e-15 | 5.4e-16 | 8.1e-16 |
| 4,000,000 (two bins) | 5.8e-17 | 1.2e-16 | 5.8e-17 | 2.6e-16 |

The round trip is 4e-16 at 1080 and about 1e-15 at 1,000,000, 3,981,312 and 4,000,000. The new
road is nearer the reference than the one it replaces at every length, both directions, by two
to four times — a transform of the length itself carries fewer roundings than three padded
transforms and two chirp multiplies — so there is no length class at which Bluestein should be
kept for accuracy's sake.

Considered and rejected for now: a native FFT provider. FFTW is GPL, MKL's licence and size are out
of proportion to one transform, and the OpenBLAS DLL has no FFT. To be reconsidered only if this
kernel, measured, leaves the row above twice R2025b.

## Consequences

Measured alone against the 07b numbers with the same rig: five counterbalanced repeats of
`d15_signal` and `d02_fft_signal` and of the three probes (`runs\07c-mixed-radix`).

| row | scope | 07b | 07c | speedup | J/M before → after |
| --- | --- | --- | --- | --- | --- |
| `d15_dct_idct_4M` | benchmark row | 1.327 s | 0.368 s | 3.61× | 5.7 → 1.6 |
| `dct(4M)` | probe, cold | 0.725 s | 0.188 s | 3.9× | 4.2 → 1.1 |
| `dct(4M)` | probe, warm | 0.562 s | 0.115 s | 4.9× | 10.5 → 2.3 |
| `idct(4M)` | probe, cold | 0.571 s | 0.188 s | 3.0× | 9.8 → 3.1 |
| `idct(4M)` | probe, warm | 0.578 s | 0.131 s | 4.4× | 10.5 → 2.4 |
| `d15_total` | script | 2.363 s | 1.478 s | 1.60× | 1.48 → 0.92 |
| `d02_fft_batch32x64k` | benchmark row | 0.130 s | 0.129 s | — | 2.8 → 2.7 |
| `d02_fft_4M` / `d02_ifft_4M` | benchmark rows | 0.076 / 0.058 s | 0.076 / 0.056 s | — | unchanged |

Against the P0 baseline the row stands at 10.3× (3.787 s → 0.368 s) and its ratio to R2025b at
1.6 from 15.6; `d15_signal` as a script now finishes ahead of R2025b. As a process it allocates
0.88 GB, from 1.15 GB after 07b and 1.93 GB at baseline, and its peak working set is 1.93 GB from
2.42. The d02 rows are powers of two and did not move, as the contract requires. What remains of
the row's gap is a warm transform of 0.115 s against 0.052 s: the passes are scalar loops, and
the reorder, the post-twiddle and the copy back are each a pass over the planes; vectorising the
butterflies is the next step if the row is asked for again, and the plan does not schedule it.

Bits: the `head2head_v3\bits` scripts against the 07b build. Exactly the 5-smooth lines moved
and nothing else: in the DCT script the 4,000,000-point `dct` and `idct`, `d(2)`, and the 1000 and
100000 lines, while the 4096, 65536 and 262144 lines and the round-trip flag are equal; in the
FFT script the 4,000,000, 1000 and 100000 lines, while the 2^22-point pair, the 65536-point
batch, and 4096 and 65536 alone are equal. That pins that the power-of-two road is untouched and
that no length outside the new road's own changed.

## Divergences

None new: the fixture rows this touches keep the rules they had.
