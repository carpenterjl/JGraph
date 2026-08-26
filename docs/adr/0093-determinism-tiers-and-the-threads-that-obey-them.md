# 0093 — Determinism tiers, and the threads that obey them

Date: 2026-08-25 · Status: accepted (M93; the tier policy ADR that M92 deferred, plus the threading it licenses)

## Context

M92 wired the packed kernels up and wired nothing whose answers would change. It left two things
standing on purpose. The first was a switch: `ApproximateThreshold`, built and set to `int.MaxValue`,
so the vector transcendentals it had just plumbed in never actually ran. The second was a thread
count of one — every kernel in `PackedMath` swept its buffer on the calling thread, on a machine with
sixteen of them.

Both are the same question asked twice. A vector `sin` and a scalar `sin` are different polynomials;
a threaded fold and a serial fold are different orders of addition. In each case something that is
supposed to be an implementation detail can reach a script's output. JGraph's packed/boxed parity
suite says that must not happen by accident — every corpus script prints byte-identical output with
packed storage on and off — so the only responsible way to take either speedup is to say in advance,
in writing, which operations are allowed to move and by how much.

That statement is this ADR. It is the policy the plan called B0 and deferred to here.

## Decision

### The tiers

**Tier E — exact.** The vector or threaded form is provably the same arithmetic as the scalar serial
one, for every double there is. Negate, abs, sqrt, floor, ceil and round are single correctly-rounded
IEEE operations and stay so however many of them a register holds. A comparison produces one of two
constants with no arithmetic in between. A gather, a scatter, a copy, a fill and a compaction move
values without touching them. Every per-element operation is exact under threading too, because
which thread wrote an element is not part of the element. **Wired unconditionally, no threshold, no
environment variable.** Parity is by construction, and the tests assert bits rather than values so
that a lost NaN or a flipped sign of zero cannot pass.

**Tier D — deterministic, but value-changing.** `TensorPrimitives`' `Sin`, `Cos`, `Tan`, `Exp`, `Log`
and `Log10` land within a few ulps of `Math`'s rather than on them. These are gated on
`ApproximateThreshold`, **32,768 elements**, and `JGRAPH_FAST_MATH=0` turns them off entirely.
Below the line the scalar kernel runs — the very `System.Math` functions the boxed interpreter
calls, so the answer is not close to the boxed one, it *is* the boxed one. The measured worst case
above the line is **2 ulps** (`ParallelKernelsM93Tests` asserts a bound of 4, over 60,000 points per
function across each function's working range).

Why 32K in particular: everything a person reads is below it. Every script in the packed/boxed
parity corpus, every hand-checked expected value in five thousand tests, every array small enough to
print. Above it lives the case the tier exists for — an array whose transcendentals are the cost of
the statement — where `sin` over fifty million elements is 242 ms of `Math` calls against 38 ms of
vector kernel.

**The threading rule.** Data-parallel over fixed grains only. A grain boundary is a function of
length and of `ParallelKernels.GrainElements`, never of how many threads are running or how fast
they happen to be going, so the decomposition is the same decomposition on every machine and every
run. Where grains must be recombined — `CountNonZero`'s tally, `Compact`'s offsets — the combine is
serial, in index order, on the calling thread. **Never completion-order combining.** The reductions
that fold values rather than counts (`Sum` above all, which is deliberately a left fold in index
order because the boxed interpreter is) are not threaded at all in this milestone.

### What that licenses, and what it costs

1. **`ParallelKernels` is the one place a managed kernel becomes several threads.** `For(length,
   threshold, betweenGrains, body)` cuts the work into fixed grains and runs them on at most
   `MaxDegree` threads when there are enough elements to be worth it. `PackedMath`'s Binary,
   BinaryScalar (both sides), Unary, UnaryScalar, Map, Zip, ZipScalar, Compare, CompareScalar, Fill,
   FillConstant, Copy, CountNonZero, Compact and TryUnaryAtLeast all sweep through it.

2. **The grain is 64K elements, and the first answer was wrong.** A million was the obvious size —
   a sub-multiple of the 4M chunk the cancellation contract was written around — and it turned out
   to be a ceiling on parallelism rather than a unit of work: `x .^ 2` over 262,144 elements is three
   and a half milliseconds of `Math.Pow` and got exactly one thread, because it was exactly one
   grain. At 64K (512 KB, a stretch that fits a core's own cache) the same array is four grains, and
   the compute-bound kernels went from 1.0× to 3.7×.

3. **The default thread count is logical processors, not physical cores — the opposite of the native
   side's.** ADR 0089 counted physical cores because a blocked factorization saturates the multiply-add
   units two hyperthread siblings share. These kernels do not saturate them: `Math.Pow` over fifty
   million elements is a chain of dependent latencies and the sibling fills the stalls (90.9 ms at
   eight threads, 83.2 ms at sixteen; `sin`'s scalar loop 42.6 ms against 34.7 ms). The memory-bound
   kernels are within a few percent either way. One environment variable, `JGRAPH_THREADS`, sets
   both; `JGRAPH_BLAS_THREADS` still overrides the native side alone.

4. **Two thresholds, because there are two costs.** A kernel that streams memory is bounded by how
   fast the machine moves eight bytes, and threading it buys 1.1× to 1.7× and only above about two
   million elements (`MemoryBoundThreshold`). A kernel that computes has tens of cycles per element
   to divide and pays back from 256K (`ComputeBoundThreshold`), reaching 6–8×. One threshold for
   both would have left one of them wrong.

5. **Cancellation keeps its contract and loses its wrapper.** The caller's poll runs between grains,
   from whichever thread finished one, and `ParallelKernels` unwraps what `Parallel.For` bundles:
   an `OperationCanceledException` if any grain raised one, otherwise the first inner exception.
   Several grains failing is one failure observed several times — a caller that used to get the one
   exception its loop raised must not start getting an `AggregateException` because the array
   happened to be big enough to split.

6. **`Compact` counts before it collects, in two passes.** Each grain counts its own matches, the
   counts are added up in index order on the calling thread, and each grain then fills its own
   stretch of the destination. The result is the order a single loop would have written, which is the
   only order the answer is allowed to be in — and it is what closes the `extract` row M92 missed.

7. **A packed array's flat storage is its column-major order, so a reduction stops rebuilding it.**
   `FlattenColumnMajor` took the jagged-rows road for anything shaped, reading a boxed value per
   element and — for a column, the commonest shape a reduction ever sees — allocating one row array
   per element on the way. `min(A(:))` over four million numbers spent **1.54 seconds** there and now
   spends **13 milliseconds**. This is scope taken early from M94: the copy is gone, the boxed round
   trip through `WrapColumnwise`'s slicing and reassembly is not, and that is still M94's.

8. **Server GC for the headless runner, and it is a trade rather than a win.** `JGraph.Cli` gets
   `System.GC.Server`. The arithmetic rows do not move at all — a fifty-million-element array is a
   native buffer outside the GC heap entirely, and `elementwise_50M` measures 0.381 s workstation
   against 0.390 s server — but the rows that allocate do. `sort_20M`, which sorts boxed values, goes
   from 14.3 s to 12.1 s; `cumsum_20M` goes the other way, 0.237 s to 0.295 s; the `d03` total is
   25.8 s against 23.3 s. Taken for the total, with the `cumsum` cost recorded rather than hidden.
   `JGraph.Application` is deliberately **not** changed: it is an interactive process that stays
   resident, server GC gives the collector a heap per core, and the arithmetic it runs is now
   threaded regardless of GC mode.

9. **`x .^ 2` is still `Math.Pow`, and the reason is worth recording.** Special-casing an exponent of
   two to a multiplication is the obvious win and it is not available: over 212 million random
   doubles, `Math.Pow(x, 2)` disagreed with `x * x` on **52,298** of them by one ulp. The product is
   the better answer — it is the correctly-rounded one — and the boxed interpreter does not give it,
   so taking it in the packed kernel would buy speed with parity. Threading the scalar loop bought
   7.4× instead, and cost nothing.

## Consequences

- A packed array of 32,768 elements or more, run through `sin`, `cos`, `tan`, `exp`, `log` or
  `log10`, can now differ from the boxed array in the last one or two ulps. Nothing in the parity
  corpus is that large; `JGRAPH_FAST_MATH=0` is the switch for a caller who needs the guarantee
  anyway. On the head-to-head suite the effect was zero: all eleven `d03` checksums, printed at ten
  significant figures — including the mask fraction, which counts how many elements of
  `sin(2πx)·e^(−x)+√x` exceed 1 — are **identical** to M92's and to MATLAB's.
- The elementwise gap to MATLAB is now bandwidth, not arithmetic. `multiply` over fifty million
  elements is 32.4 ms on one thread and 28.6 ms on four, and stays there: the machine cannot read and
  write eight hundred megabytes any faster. What is left of `d03_elementwise_50M` is seven such
  passes and the page faults that commit the memory for them.
- `PackedMath.Map` and `PackedMath.Zip` now call the caller's delegate from several threads at once.
  Every delegate that reaches them today is one of `Math`'s or a numeric-class conversion; the XML
  docs say so, and a caller with state to keep wants a loop of its own.
- `PackedMath.ChunkElements` is no longer the cancellation cadence for the elementwise kernels — the
  poll runs per 64K grain now, which is oftener, not rarer. It remains the unit the serial
  reductions walk.

## Measured

The `d03_arrays` and `d06_image` scripts of the head-to-head suite, Release, i7-11700F, this build
alternated with the M92 build in the same session (so the two share a machine state); `d03` is the
mean of two rounds each, `d06` of four.

| row | M92 (0d259b8) | M93 | change | MATLAB |
|---|---|---|---|---|
| `d03_generate_50M` | 0.296 s | **0.165 s** | 1.79× | 0.578 s |
| `d03_elementwise_50M` | 0.860 s | **0.383 s** | 2.25× | 0.225 s |
| `d03_reductions` | 0.463 s | 0.407 s | 1.14× | 0.067 s |
| `d03_mask_50M` | 0.201 s | **0.104 s** | 1.93× | 0.068 s |
| `d03_extract` | 0.114 s | **0.065 s** | 1.75× | 0.081 s |
| `d03_cumsum_20M` | 0.274 s | 0.301 s | 0.91× | 0.040 s |
| `d03_dimreduce` | 2.239 s | **0.489 s** | 4.58× | 0.020 s |
| `d03_sort_20M` | 13.02 s | 12.47 s | 1.04× | 0.283 s |
| `d03_intops_10M` | 5.70 s | 5.15 s | 1.11× | 0.150 s |
| `d03_loop_2M` | 1.122 s | 1.427 s | 0.79× | 0.014 s |
| `d03` total | 26.78 s | 23.18 s | 1.16× | 4.952 s |
| `d06_generate_2048` | 1.414 s | **0.226 s** | 6.26× | 0.100 s |
| `d06_edges` | 0.688 s | **0.183 s** | 3.76× | 0.099 s |
| `d06` total | 8.52 s | **5.39 s** | 1.58× | 6.120 s |

`d03_extract` now beats MATLAB (0.065 s against 0.081 s), `d03_generate_50M` beats it more than
three times over, and `d06`'s total crosses: 5.39 s against MATLAB's 6.12 s.

Two rows moved the wrong way and both are noise or GC, not threading. `d03_cumsum_20M` is about 25%
slower with server GC than without (0.295 s against 0.237 s, consistently across rounds), which is
the price of the trade in decision 8 — `d03_sort_20M` pays it back four times over, 12.1 s against
14.3 s, and the `d03` total is 23.3 s against 25.8 s. `d03_loop_2M` is run-to-run variation on this
machine: the same build measured 1.131 s and 1.493 s in consecutive rounds, straddling M92's 1.122 s.

Against the plan's three gates for this milestone: **`elementwise_50M` ≤ 0.45 s is met** at 0.383 s,
and **DOP-invariance is green** — every grained kernel answers the same bits at one thread and at
sixteen, asserted through `BitConverter`. **`d06 generate` within 2× of MATLAB is missed**, at 2.26×
(0.226 s against a 0.200 s target), and the breakdown is worth writing down rather than rounding.

Warm, the statement splits into 0.10 s of expression, 0.06 s of normalization and the rest in
first-touch page faults on the fifteen temporaries the expression allocates. Of the normalization,
0.045 s is three `min`/`max` calls, each of which materializes `img(:)` as a fresh four-million-element
array and then copies it again inside the reduction. Both copies are what M94's `PackedReduceOps`
removes, and neither is something threading can reach. The row went from 14.1× MATLAB to 2.3× in
this milestone; the last 0.3× is the next one's.

Per-kernel, at fifty million elements, one thread against sixteen:

| kernel | 1 thread | 16 threads | |
|---|---|---|---|
| `Math.Pow` (`x .^ 2`) | 618 ms | **83 ms** | 7.4× |
| `Zip` (atan2, hypot, mod) | 487 ms | **63 ms** | 7.7× |
| `sin`, scalar kernel | 242 ms | **35 ms** | 7.0× |
| `exp`, scalar kernel | 188 ms | **32 ms** | 5.9× |
| `exp`, vector kernel | 61 ms | **22 ms** | 2.8× |
| `sin`, vector kernel | 38 ms | **20 ms** | 1.9× |
| `sqrt` | 34 ms | **20 ms** | 1.7× |
| `CountNonZero` | 15 ms | **9 ms** | 1.7× |
| `compare` | 53 ms | **39 ms** | 1.4× |
| `multiply` | 32 ms | **29 ms** | 1.1× |

## Testing

- `ParallelKernelsM93Tests` runs twenty-two grained kernels at one thread and at sixteen over an
  array of 2,109,497 elements — over the threading threshold, and not a multiple of the grain, so
  the last grain is a partial one — and compares raw bits. It checks that counting and compacting
  agree at every thread count and that a compaction comes back in the order a single loop would have
  written it; that a domain check still finds a negative sitting in the last grain, and in the first
  while other grains are already running; that the poll runs once per grain; that a cancelled grain
  arrives as an `OperationCanceledException` and not as a bundle, and that a grain failing some other
  way arrives as itself; and that each approximate kernel is within four ulps of the one it replaces
  over sixty thousand points of its working range.
- `MatlabThreadedKernelsM93Tests` says the same things from a script: a split elementwise chain
  checked element by element against the arithmetic that made it, a split comparison whose match
  count is computed independently, a split extract whose order and stride are asserted, a split
  domain check that must still promote for a negative at either end, and `mod` and `.^` still
  answering what the scalar loops answer. Then the reduction side of the `FlattenColumnMajor`
  change: a matrix read in storage order, a column and a row reducing to the same number, a logical
  matrix still reducing as zeros and ones, and NaN still doing what it did. Finally the tier itself
  — bit-identical to the boxed path at 32,767 elements, and within a few ulps at 32,768.
- `PackedMathTests`' cancellation test now pins the serial cadence explicitly (one grain plus a
  partial second, below the threading threshold) rather than assuming there is only one.
- `ParallelThresholdBenchmarks` is the recorded harness for the numbers above: every kernel at four
  lengths and two thread counts, memory-bound and compute-bound cases kept separate.
- The full suite runs in all four `JGRAPH_LINALG` × `JGRAPH_JGS_PACKED` lanes, plus the 59-script
  stress sweep and the four coverage verifiers.
