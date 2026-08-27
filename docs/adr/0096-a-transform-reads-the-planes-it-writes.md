# 0096 — A transform reads the planes it writes

Date: 2026-08-27 · Status: accepted (M96; plan items B5 and B6 — the transform, and the filters
beside it)

## Context

The transform family was the last of the big managed gaps. `fft` boxed a `System.Numerics.Complex`
per sample on the way in, split those into two `double[]`, copied every slice into an array of its
own, built a third array to transform, copied the answer back into two more, joined those, and
boxed a Complex per sample again on the way out — nine passes over sixty-four megabytes wrapped
around the one that was the actual work. The head-to-head rows said so: `d02_fft_4M` **1.096 s**
against MATLAB's 0.068, `d02_ifft_4M` **1.073** against 0.067, `d02_fft2_2048` **0.551** against
0.037, `d02_fft_batch32x64k` **0.500** against 0.055.

Beside it in the same script sat two filters. `d02_fir64_10M` — ten million samples through a
sixty-four-tap smoother — stood at **0.846 s** against MATLAB's 0.083, walking a transposed
recurrence one sample at a time and spending half its multiplies on denominator coefficients it had
been told were zero. And in the image script `d06_blur_21tap` stood at **2.860 s** against 0.094,
because `conv2(u, v, A)` built the outer product of its two vectors and convolved with the
four-hundred-and-forty-one-element kernel it had just made, where two passes of twenty-one taps
would have done.

The plan (item B5) recorded a decision to P/Invoke PocketFFT for the transform, riding the loader
and packaging pattern OpenBLAS uses. That is **not** what this milestone did, and the deviation is
argued in *Consequences* below.

## Decision

### One transform, over planes

`src/JGraph.Numerics/FftKernels.cs` holds the transform now, over planar storage: the real parts in
one span of doubles and the imaginary parts in another. Three things follow from that shape and
between them they are the milestone.

A butterfly becomes four multiplies and four adds on plain doubles, so several signals can be
transformed side by side in one SIMD register — a lane per signal, the twiddle broadcast across
them. A slice of a packed array can be read where it lies. And a transform too large for cache can
be **factored** into two passes of shorter ones, each of which is a batch, and therefore vectorised
by the same code that vectorises a batch someone asked for.

The butterfly itself is the one the old radix-2 wrote, operand for operand: the same bit-reversal,
the same stage order, the same twiddle table built with the same `(±2.0 · π / n) · k` spelling and
read at the same stride, and the same `(br·wr − bi·wi, bi·wr + br·wi)` products that
`Complex`'s own multiply forms. Nothing is contracted into a fused multiply-add, and a
`Vector<double>` multiply is the same IEEE multiply four at a time. **So every length that takes the
direct road answers the old bits**, and `FftKernelsM96Tests` keeps the pre-M96 radix-2 verbatim as
an oracle and checks it rather than assuming it — forward and inverse, powers of two, Bluestein's
lengths, the tiny direct sums, and a batch against the same signals transformed one at a time.

### The factored road, and the one difference it makes

Above 32K points a transform is written as two passes of shorter ones. Reading the index as
`t = i1 + n1·i2` and the answer's as `k = k2 + n2·k1` turns the sum into n1 transforms of length n2,
a multiply by `exp(±2πi·i1·k2/n)`, and n2 transforms of length n1 — and laying the first pass's
output out transposed makes the second pass's answer land already in order, so there is no transpose
pass at all; the transpose is the stride the gather already walks. Both passes are batches of eight
lanes, and a tile of eight signals of 2048 points is 256 KB, which one core keeps in its own cache
for all eleven stages. The cross term is read from two small tables rather than one of length n:
`m` splits as `q·n1 + r` with a shift and a mask, because n1 is a power of two.

**That is a different arrangement of the same sum and therefore a different rounding — the one
deliberate divergence in the transform.** It is chosen by length alone, so it is the same choice on
every run and on every machine, and it never depends on how many threads are working; the tests
assert exactly that, at one thread and at sixteen, and pin the factored answer against the direct
one to within 1e-13 of its own scale and against the identity a transform and its inverse make.

### Where the slices come from

`FftKernels.TransformAlong` does the whole job from packed column-major storage: gather, transform,
scatter, threaded — and `PackedTransformOps` is the scripting layer's gate onto it. A length the
direct kernel takes is transformed eight slices at a time (which is what makes the butterflies
vector work); a factored length, or one that is not a power of two, is transformed a slice at a
time with the threads spent inside it instead. Neighbouring slices of a strided layout are
neighbouring elements, so a whole tile's j-th element is one contiguous run and the gather is a run
of vector moves; a contiguous layout — a matrix transformed down its columns — is a transpose into
the tile instead.

`JGraph.Signal.Fft` is now the boxed door onto the same kernels rather than a second transform, so
the packed road and the boxed road cannot disagree. That cost `JGraph.Signal` its BCL-only standing:
it takes a project reference on `JGraph.Numerics`, the way `JGraph.Imaging` already did.

### A filter with no feedback is not a recurrence

`src/JGraph.Numerics/FilterKernels.cs` handles `filter(b, a, x)` when `a` has nothing in it past
`a(1)`. Writing out the transposed recurrence for that case gives

```
y[i] = b0·x[i] + (b1·x[i−1] + (b2·x[i−2] + (… + (b_{L−1}·x[i−L+1] + s))))
```

— one right-nested chain that starts at the oldest tap and works forwards, with `s` the delay the
caller carried in while the window still reaches back past the first sample and zero once it does
not. Summing the taps in that order reproduces the recurrence's own rounding, so **the answers are
the same bits**; summing them in any other order would not, which is why the vectorising goes across
outputs and never across taps — a lane per output, each running the same chain in the same order.
Threads go the same way, in fixed grains. `zf` is worked out from the tail of the signal by the same
right-nested rule.

One thing does change, and only where the data is not finite. The recurrence multiplied the output
by `a(j+1)` and subtracted it even when that coefficient was zero, and zero times an infinity is a
NaN — so a single NaN in the input poisoned the delay line and every later sample with it. A filter
with no feedback cannot carry a value further than its own length, and now does not. **MATLAB was
asked**: `filter([0.5 0.25 0.25], 1, [1 2 NaN 4 5 6 7 8])` gives NaN at three positions and 5.25 at
the sixth, which is what this build now gives and is not what it gave before.

An IIR filter keeps the recurrence exactly as it was — it is one long dependency chain, and there is
nothing to divide.

### A separable convolution never builds its kernel

`Filters.SeparableConvolve2` runs `conv2(u, v, A)` as one pass along the rows and one down the
columns. That is `|u| + |v|` multiplies per pixel where the built kernel cost `|u|·|v|` — for the
twenty-one-tap blur of a 2048-square image, a hundred and seventy-six million instead of one and a
half billion. Threads take bands of sixty-four output rows, so a band's inputs stay in one core's
cache across every tap of `u`. The multiply and the add stay separate operations: a fused one would
round differently on the machines that have it and not on the ones that do not.

Two passes of sums are not the same rounding as one pass over a materialised kernel, so this is the
milestone's second deliberate divergence. The tests pin every shape, anchor and crop against the
built-kernel convolution to within 1e-13 of its own scale, hold the delta kernel to exactness, and
check that the answer does not move with the thread count.

The **general** `Convolve2` — the one behind `conv2(A, B)` and the imaging filters — is untouched
and still exact. Splitting its interior from its border strip, which plan item B6 also asks for, is
not in this milestone; nothing measured needs it, and the honest note is that it was left undone
rather than done quietly.

### Four MATLAB differences closed on the way

Writing the tests against real MATLAB turned up four things this family had been getting wrong, all
of them older than this milestone:

- **`fft(5)` was an error.** A value that is not an array carries no rows and columns of its own, and
  asking one for them answered 0-by-0 — which made the length along the first non-singleton
  dimension zero and raised "a transform length is a positive whole number, but got 0" for every
  scalar. A scalar is one element in one row, and its transform is itself. MATLAB agrees.
- **A zero length was refused.** It is a legal answer, not a mistake. MATLAB: `fft([])` is empty,
  `fft(zeros(1, 0), 4)` is a 1-by-4 **of zeros** because the padding is real padding, and
  `fft(zeros(0, 3), 2)` is a 2-by-3 of them. All of that falls out of the existing join once the
  refusal is out of the way. Only a negative length is refused now, and the wording says so.
- **An array with no slices lost its shape.** The join reads the slice length off the first slice,
  and `fft(zeros(2, 0))` has no first slice to read, so it came back 0-by-0 where MATLAB says 2-by-0.
  The shape is the transform's own now, not the join's.
- **`isreal` threw on a planar complex array.** It knew about packed real arrays and boxed ones and
  fell through to boxed-element access for the third kind — so `isreal(fft(x))` had been an error
  for as long as packing has been on. It answers from the imaginary plane now.

One difference was found and deliberately **not** closed: this build's `[]` literal is 1-by-0 where
MATLAB's is 0-by-0, so the first non-singleton dimension of a bare `[]` is the second here and the
first there, and `fft([])` and `fft([], 4)` differ for that reason alone. Handed the same shapes by
name the two engines agree exactly, which is how the literal was isolated. It is the empty literal's
problem, not the transform's, and it reaches far enough that it wants its own change.

### And a rectangle that was built one boxed element at a time

`conv2` takes its image as a `double[,]` and hands one back, and both conversions went through the
boxed door: `JgsMatrix.ToRows` asked a packed array for a `JgsValue` per element to check that it was
a number — four million objects for a 2048-square image — `Matrix` then copied the jagged result into
a rectangle, and `MatrixToRows` built the answer through a delegate per cell. A one-tap `conv2` of
that image, which does no arithmetic worth the name, cost **0.46–0.89 s**: the whole row was the
conversion and none of it was the convolution. Packed storage is already doubles and every one of
them reads back as a number, so the check has nothing to find; the packed arm reads the buffer
straight into the rectangle and writes the answer straight back into a flat array. The same one-tap
call now costs 0.13 s, and every imaging builtin that takes a matrix got the same road for free.

### One thing beside the transform that the row was made of

`abs(F)` of a spectrum was minting a `JgsValue` per element to discover that a magnitude is a number,
and reading each plane through a virtual `AsSpan()` twice per element while it did. Four functions —
`abs`, `real`, `imag`, `angle` — can only answer a real number from a complex one, and now say so
with a second delegate that returns a `double`; the planar arm uses it and boxes nothing. Nothing
about the answers changes: the delegate is the boxed one with the box taken off, written at the same
call site.

## Divergence found here, and left standing

- **An empty matrix literal is 1-by-0, where MATLAB's is 0-by-0.** `zeros(0, 0)` is right; the bare
  `[]` is not. It matters beyond display, because every builtin that walks "the first non-singleton
  dimension" picks the second dimension for a 1-by-0 and the first for a 0-by-0 — so `fft([])`
  answers a 1-by-0 where MATLAB answers 0-by-0, and `fft([], 4)` answers a 1-by-4 of zeros where
  MATLAB answers a 4-by-0 empty. Handed the same shapes by name — `fft(zeros(1, 0), 4)`,
  `fft(zeros(0, 3), 2)` — the two engines agree exactly, which is how the literal was isolated as
  the one carrying the difference. Found in M96a while checking the transform against real MATLAB,
  and left where it was found: the literal reaches into concatenation, deletion, `isempty` and every
  reduction's default dimension, and changing it is its own piece of work.

## Consequences

**PocketFFT was not used, and this is a deviation from the approved plan.** The plan's item B5 says
"USER DECIDED: PocketFFT native", riding "the SAME loader/resolver/packaging pattern as OpenBLAS".
The pattern does not in fact transfer. OpenBLAS ships an official prebuilt Windows DLL with a
published SHA256, and `native/win-x64/SOURCE.md` records exactly that provenance. PocketFFT ships no
binary at all: taking it would mean fetching C sources and compiling them here with MinGW, and
committing a self-built DLL whose provenance is "this machine, that day" rather than a release asset
anyone can re-check. The plan also expected the managed fallback to land "~4–6× behind instead of
16×" — that estimate was for the old radix-2 with better plumbing around it, not for a kernel
written for planes, batches and cache. The measured rows below are what the managed kernel actually
does, and two of the three transform gates are met with it. **The native question is therefore still
open rather than settled**, and it should be answered against these numbers rather than against the
1.1-second row that prompted it.

Determinism: the direct transform is Tier E by construction (the old bits). The factored transform,
the separable convolution and the feed-forward filter's non-finite behaviour are the three
value-changing pieces, all of them chosen by shape rather than by schedule, all of them
thread-count-invariant, and all of them tested as such.

`JGraph.Signal` is no longer a BCL-only leaf. It is a leaf still — nothing depends on it that did
not — but it takes `JGraph.Numerics` with it now, and `docs/architecture.md` says so.

## Measured

Release, six runs through the head-to-head harness on a rested machine. "Before" is the
head-to-head log as M95 left it; "MATLAB" is that suite's own recorded run.

| row | before | after | range | MATLAB | gate |
| --- | ---: | ---: | --- | ---: | --- |
| `d02_fft_4M` | 1.096 | **0.060** | 0.058–0.068 | 0.068 | ≤0.14 — **met**, and beats MATLAB |
| `d02_ifft_4M` | 1.073 | **0.047** | 0.045–0.056 | 0.067 | — **beats** MATLAB |
| `d02_fft2_2048` | 0.551 | **0.044** | 0.041–0.061 | 0.037 | ≤0.075 — **met** |
| `d02_fft_batch32x64k` | 0.500 | **0.139** | 0.104–0.171 | 0.055 | ≤0.11 — **missed** |
| `d02_fir64_10M` | 0.846 | **0.030** | 0.030–0.033 | 0.083 | ≤0.17 — **met**, beats MATLAB 2.8× |
| `d06_blur_21tap` | 2.860 | **0.094** | 0.089–0.096 | 0.094 | ≤0.19 — **met**, ties MATLAB |
| `d02_total` | 6.591 | **2.419** | 2.348–2.602 | 4.445 | — d02 now **beats** MATLAB |
| `d06_total` | 6.007 | **2.429** | 2.335–2.566 | 6.120 | — d06 now **beats** MATLAB |

That is 18× on the 4M transform, 23× on its inverse, 12× on the 2-D one, 28× on the FIR and 30× on
the blur. Head-to-head parity is unmoved at 48 of 49 checks, and every one of d02's and d06's
checksums reads exactly what it read before.

**Five of six gates met. `fft_batch32x64k` missed, and here is what it is made of.** Timed in
pieces on the same rested box: the range index `sig((k-1)*65536 + (1:65536))`, thirty-two times,
costs **0.042 s on its own** — 38% of the whole gate before a transform has run. The thirty-two
transforms cost **0.047 s**. `max(abs(Fk))` costs most of the rest, and each iteration allocates two
fresh half-megabyte buffers on top. One run in six came in at 0.104 and the other five did not; the
median is 0.139 and the gate is 0.11, so it is missed rather than borderline. **The transform is now
the smaller half of its own row.** The range index is the same cost ADR 0094 recorded against
`cumsum_20M` and ADR 0095 against `sort_20M`: `x(1:n)` materialises the range, turns it into a
per-element `int[]` of positions, and gathers. This is the third milestone it has capped a row in,
and it is still unscoped.

**Calibration.** Three rows M96 does not touch were read on the same six runs, and all three land on
their recorded values: `d06_generate_2048` 0.195–0.230 against 0.195, `d06_edges` 0.168–0.206 against
0.187, and — the closest control there is — `d02_iir_10M` 0.090–0.098 against 0.097, since it goes
through the very builtin M96b changed and takes the recurrence it left alone. The machine measuring
the "after" column is the machine that measured the "before" one.

**Getting there took finding out why the machine lies.** Measurements over several hours read two to
four times slow, including on rows nothing had touched. A leftover `testhost.exe` was alive each
time — one had burned 4,272 seconds of CPU, another 3,318 — left behind by `dotnet test` runs that
had already reported and exited. Every number above was taken after killing them and waiting for the
machine to fall under 25% busy. The lesson generalises past this milestone: on this box a timing is
worth nothing unless something checked what else was running.

## Testing

`FftKernelsM96Tests` (44) carries the pre-M96 radix-2 as an oracle and asserts bit-identity for every
direct length forward and inverse, batch against single, the factored road against the direct one and
against one thread versus sixteen, the round trip, every slice geometry a dimension can have, and the
symmetry word. `MatlabPackedFftM96Tests` (10) runs each script twice, packed and boxed, and demands
byte-identical output at seventeen significant digits with both planes printed — dimensions, padding,
cutting, complex subjects, `fft2`/`fftn`, `fftshift`, the classes the fast path refuses, the empty
shapes read off MATLAB, and the refusals' wording. `FilterKernelsM96Tests` (13) checks the
feed-forward kernel against the recurrence bit for bit across tap counts, sample counts, carried
conditions and four denominators that only look trivial, that any split of the output range gives the
same answer, that the thread count cannot move it, and that a NaN reaches exactly as far as the
filter is long. `MatlabPackedFilterM96Tests` (8) is the same parity discipline over `filter` and
`conv2`. `SeparableConvolutionM96Tests` (11) pins the separable pass against the kernel it no longer
builds across eight sizes and three shapes, plus the delta kernel, the thread count, and the empty
fallbacks.
