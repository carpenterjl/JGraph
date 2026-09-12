# ADR 0151 — The same answer by a shorter road

## Status

Accepted. A performance unit, not a milestone: the six changes the head2head_v3 gap analysis
ranked first, each chosen because it moves no answer. The analysis itself lives outside the
repository (`JGraph_demo_workspace\head2head_v3\gap_analysis\report.html`, with the ten
investigations behind it in `per_script\`).

## Context

head2head_v3 timed 184 operations on this build and on MATLAB R2025b, five interleaved repeats,
medians. MATLAB led by three times or more on 55 of them, 12.2 s in all. Every one of the 55 was
traced from the call in the script to the loop or library call that spent the time, and the cause
isolated by measurement — an environment variable changed, a probe run on the Release binary —
rather than read off a profile. Six rows were a worse algorithm. Eleven were a redirection: the
faster code was already in the tree and the caller went past it. Forty-three had a fix that changes
no answer, and the six below were the cheapest of those, in the order the report ranked them.

The finding under all six is that the engine is not slow at arithmetic. The kernels under these
rows are correct, vectorised, threaded, and in several cases faster than MATLAB's when timed alone.
What was slow was the road to them.

- **OpenBLAS ran every call on fourteen threads.** `OpenBlasLoader` set the count once at load to
  the physical-core count, and `ProcessorTopology` counted every `RELATION_PROCESSOR_CORE` record —
  on an i7-12700H that is six performance cores and eight efficiency cores, counted as equals. The
  comment on that code was calibrated on an i7-11700F, a homogeneous part, where it was right.
  Fourteen was never the best count for any LAPACK driver at any size measured: `dsyevd` at
  n = 400 took 3.9 ms on one thread and 14.5 ms on fourteen, `dgetrf` on the same matrix 1.7 ms
  against 10.7. Two mechanisms, and they separate by routine. OpenBLAS partitions a level-3 call
  evenly across its threads and waits for the slowest, and pinned to the E-cores alone `dgeqrf` at
  n = 1200 took 0.80 s against 0.062 s on the P-cores, so eight fourteenths of every panel update
  went to the slow cluster. And this is the pthread build with a spin-then-yield barrier, which the
  thousands of short level-2 calls inside `dsytrd` and `dgebrd` cross one at a time. Re-running the
  unmodified d10 script with `JGRAPH_BLAS_THREADS=1` took `det` from 31 ms to 4 and `sqrtm` from
  545 ms to 185.
- **`median` sorted a list of ten million tuples through a delegate.** `MedianReduction` was written
  for the weighted, vecdim case and took that road for `median(vector)`: a `List<(double, double)>`
  filled through a per-element index decomposition with two integer divisions per dimension, then
  `List.Sort` with a `Comparison<T>` — an indirect call per comparison, 2.3e8 of them. 1.43 s of the
  1.65 s row. `SelectKernels.PartialSort` exists for exactly this (M120), `prctile` uses it, and
  `JgsStdlib.Median` uses it, but `MedianReduction` shadowed `JgsStdlib.Median` and never reached
  it. `isoutlier`'s default method had the same shape: `MedianOf` in the windows file — the
  21-element window summary for `movmedian` — was also the whole-series median, so a two-million
  element series was sorted twice, once for the centre and once for the MAD. 87% of that row.
- **`regexp` compiled the pattern twice on every call.** `CompileMatlab` built two `Regex` objects
  per call — the `(?:…)(?<!\G)` wrapper and the plain form for `'emptymatch'` — and an instance
  `Regex` constructor never touches .NET's static cache. A scalar-subject loop of five thousand
  calls spent 85% of its time compiling one pattern five thousand times; the vectorised form, which
  compiles once, was 6.4x faster for identical work.
- **The packed reductions refused every classed array.** `PackedReduceOps.IsReducible` gated on
  `NumericClass == Double`, so `sum` over twenty million `uint8` fell onto the boxed road: a fresh
  managed `double[]` of 160 MB, then a second one filled by a scalar gather in `SlicesAlong`, then
  the same `PackedMath.Sum` it would have reached directly. Two copies of 160 MB were 80% of the
  worst ratio in the benchmark, 37x. The gate protected nothing: the boxed road folds the same
  doubles, and the class wrapper stamps the answer afterwards on either road.
- **`interp1` bisected a uniform grid.** `Bracket` is a textbook bisection over the whole knot
  vector — fourteen dependent, mispredicted steps per query over `linspace(0, 1, 20000)` — and
  nothing on the path looked at the spacing, although `IsEvenlySpaced` was three files away and
  `SettleInterp1Method` already called it, only to decide whether `'cubic'` was legal. The search
  was 70% of every `interp1` row and the reason all five methods cost the same to a hundredth of a
  second: what was being measured was not the arithmetic.
- **The moving windows copied a vector three times around a kernel that was already right.**
  `movmean` over ten million points: `FlattenColumnMajor` (one copy), `SlicesAlong` filling a
  second array element by element although a vector cut along its length is one contiguous run,
  the kernel, then `JoinAlong` scattering into a third. 480 MB of single-threaded scalar traffic
  per call; the two-stack window walk in the middle costs the same at width 1, 51 and 501, and is
  threaded.

## Decision

### A native call is sized before it is threaded

`NativeThreads.Use(work, size)` wraps every call in `OpenBlasLinalg`. It takes one lock, sets the
OpenBLAS thread count for that call from the routine's kind and its size, and releases the lock
when the call returns. Three kinds: a level-3 product, sized by its flop count, takes one thread
below two million and the whole ceiling above; a factorization and the solves built on one take one
thread below n = 512, four below 1024, the ceiling past that; a spectral driver — eigenvalues,
Schur, singular values — takes one below 512, two below 1024, four below 2048. The count is a pure
function of routine and size, so the same call gets the same threads on every run, which is what
keeps native results identical run to run — the property the single fixed count was there to keep.

The ceiling is one thread per core of the fastest efficiency class, capped at 16.
`ProcessorTopology.PerformanceCoreCount` reads the efficiency-class byte of each core record and
counts the top class: six here, every core on a homogeneous part. `JGRAPH_BLAS_THREADS` (or
`JGRAPH_THREADS`) pins the count for the process instead, and a pinned count is handed to every
call as given, so the old behaviour is one environment variable away. `version('-blas')` now reads
`OpenBLAS 0.3.34 (native, up to 6 threads, sized per call)`.

### A median is selected, not sorted for

`MedianReduction` answers the unweighted single-group case — `median(x)` of a vector, or `'all'` —
by `SelectKernels.PartialSort` on the flattened copy it already owns, at rank n/2 or the pair
n/2−1, n/2. The even case sums two halves rather than halving a sum, as the sorted road always did,
so the bits are the bits. NaN is found first, because a selection does not put it anywhere in
particular. The weighted and multi-group cases keep their road.

`MedianOf` in the windows file selects above 64 readings and sorts below, so `movmedian`'s windows
keep the sort that suits them and `isoutlier`'s series gets the selection.

### A compiled pattern is kept

`CompileMatlab` keeps a `ConcurrentDictionary` of `MatlabRegex` by `(pattern, RegexOptions)` — the
pair that decides the object — capped at 256 entries and cleared when full. A pattern that fails to
compile is not cached, so its diagnostic still names the builtin that asked.

### The packed reductions take a classed array

`IsReducible` no longer consults the class tag. `TryColumnwise` refuses a classed subject only for
the running families — `cumsum`, `cumprod`, `cummax`, `cummin`, `diff` — whose saturating scan is
answered by the class wrapper before the reduction is reached, and whose dimension-named form the
boxed road keeps. `sum`, `prod`, `mean`, `rms`, `std`, `var`, `any`, `all`, `vecnorm`, `max` and
`min` over an integer or single array now read the storage where it lies.

### `interp1` indexes a uniform grid

`Resample` tests `IsEvenlySpaced` once per call. When it holds, `ValueAt` estimates the interval as
`(at − x0) · (n − 1) / (xn − x0)`, clamped as a double before the cast so a far extrapolation cannot
convert to nonsense, and then walks the estimate against the knots themselves — down while
`at < x[i]`, up while `at ≥ x[i+1]` — so the interval answered is the one `Bracket` would have
found, to the index. `Bracket` stays for a non-uniform grid.

### One contiguous slice is the storage

`JgsMatrix.SlicesAlongOwned` returns the caller's own flattened array as the single slice when the
array is one contiguous run along the cut dimension; both `Cut` helpers use it, because the
flattened array is theirs. `JoinAlong` returns a lone contiguous slice as the joined storage,
which every caller wraps at once and none reads again. In the general `inner == 1` case both
`SlicesAlong` and `JoinAlong` block-copy each run instead of gathering it a scalar at a time.

## Consequences

Measured on the Release binary, best of two alternating runs against the untouched HEAD built in a
worktree, the machine carrying ten idle build nodes (which is why the old BLAS figures are worse
than the benchmark's quiet medians — a spin barrier is what degrades under load):

| row | before | after | |
| --- | --- | --- | --- |
| `median` 10M | 2.99 s | 0.106 s | 28x |
| `qr` 1200 | 1.23 s | 0.094 s | 13x |
| `hess` 400 | 0.180 s | 0.015 s | 12x |
| `isoutlier` 2M | 0.758 s | 0.081 s | 9.3x |
| `regexp` tokens ×5000 | 0.324 s | 0.036 s | 9.0x |
| `sum` uint8 20M | 0.132 s | 0.015 s | 8.9x |
| `lu` 2000 | 0.458 s | 0.080 s | 5.7x |
| `det` 400 | 0.011 s | 0.002 s | 4.6x |
| `svd` 800 | 0.319 s | 0.075 s | 4.3x |
| `sqrtm` 400 | 0.676 s | 0.185 s | 3.7x |
| `movmean` 10M | 0.145 s | 0.046 s | 3.2x |
| `interp1` linear 2M (warm) | 0.055 s | 0.013 s | 4x |

A probe of 127 checks over every edge of the six — odd and even counts, NaN with and without
`'omitnan'`, weights, matrices along each dimension, every `interp1` method on knots, between
knots, outside them and a billion units outside them, uniform, jittered and non-uniform grids,
`'native'` and `'double'` output classes, saturating scans, `'emptymatch'`, `'once'`, named tokens,
every window endpoint rule — answers bit-identical to the untouched HEAD on 120 of the 127. The
seven that differ are the LAPACK rows, and they differ in the last ulp of a residual, which is the
thread count moving the order of a blocked update. Pinning `JGRAPH_BLAS_THREADS=14` reproduces the
old bits exactly.

The four test lanes — native and managed linear algebra, packed and boxed storage — are green at
8,713 tests each, the same count as before: nothing here needed a new answer to pin, and every
existing pin held.

A single pass of head2head_v3 on this build, engines interleaved, machine idle, against the
five-repeat medians of the old one: of the 55 rows in the three-times band, 23 left it, and the
55 fell from 14.3 s to 10.0 s. The rows that moved most are the ones above; `dct(4e6)` and the
special functions did not move, as expected. Six rows the one pass reported slower were re-run alone
and matched the old medians, so they were first-row noise. The 244 `CHK` values changed on the
seven LAPACK residuals only. The run lives beside the original in `head2head_v3_fix1`.

## Divergences

None from MATLAB. The thread policy moves the last ulp of results that pass through `dgetrf`,
`dpotrf` and `dgetri` relative to the previous build — `eig`, `svd`, `qr` and `gemm` checksums are
unchanged — and a fixture that pinned the full precision of an LU-based answer would have moved
with it; none did.

## Still open

The report's ranked plan continues past these six. The largest single losses are structural and
not touched here: `FftKernels` has no mixed-radix path, so every non-power-of-two length falls onto
Bluestein and `dct(4e6)` mirrors to 8e6 and pays three transforms of 2^24 on one core; the stiff
solvers spend half their time in interpreted right-hand-side calls; integer arrays are `double[]`
and read eight times the bytes MATLAB reads. `sqrtm` and `logm` want a real quasi-triangular square
root and a bound `dtrsyl`, and `sqrtm`'s residual is currently better than MATLAB's, so that trade
is flagged in the report's parity register rather than taken.

## Independent verification — 12 September 2026

The earlier measurements and causal claims above describe the initial investigation. The
[verification report](../../../JGraph_demo_workspace/head2head_v3_verify6/verification_report.html)
qualifies those claims and retains the new raw measurements, full-array comparisons and hashes.
In particular, a thread ceiling is not core affinity, native thread changes do not guarantee
bitwise equivalence, and small residuals do not establish generally better forward accuracy.

The first six recommendations are now verified, with these additions:

- The native thread scope releases its lock if selecting or setting the count throws.
- Regex caching uses bounded LRU eviction and includes matching culture in the key. A miss is
  constructed under the cache lock; matching runs outside it and failed compilations are not cached.
- Uniform interpolation rejects nonfinite spans/reciprocals and falls back to bisection after
  at most two corrections. This fixes the incoming implementation's exceptions on nearest queries
  over `[0, 1e-310, 2e-310]` and `[-1e308, 0, 1e308]`.
- Contiguous packed moving-window inputs are borrowed read-only through `NumericBuffer`, with
  lifetime protection through completion of parallel readers. Outputs own separate storage.
  General mutable slice callers keep their owned-copy contract.

Selection and packed classed reductions were already implemented correctly for their intended
fast-path scope and were retained. The weighted/multi-group median fallback and saturating scan
paths remain deliberate compatibility choices.

All four test lanes passed 8,721 tests each, including eight added regression cases. A subsequent
native-thread concurrency regression also passed. Full-array comparisons passed against unchanged
HEAD on 70 cases and matched all 68 completed incoming-build outputs exactly; the other two
incoming cases were the repaired interpolation exceptions. MATLAB matched 65 cases. Four integer
std/var calls that MATLAB rejects and the existing movmax/includenan behavior remain disclosed
compatibility differences; this optimization pass preserves those regression-tested semantics.

Eight counterbalanced blocks compared a freshly built unchanged HEAD, the incoming working tree,
the finished build and MATLAB. The 19 non-overlapping original-expression rows sum to 6.062 s,
2.020 s, 1.826 s and 2.322 s respectively (sums of medians, not a single execution). The complete
six-change set is therefore 3.32× faster than fresh HEAD in that targeted harness. Separate,
longer-warmed window measurements show additional incoming/final speedups of 1.27× for movmean,
1.20× for movstd and 1.12× for prepared movmedian. Neither benchmark replaces the full application
workload or establishes a universal optimum.

The current MATLAB regex timings did not reproduce the archive. Four runs of the byte-identical
original text script and two direct-name launch checks also remained slower than the archive;
the underlying runtime/environment cause was not isolated. Cross-engine ratios are consequently
specific to the recorded current conditions, including the machine's existing Silent power plan.
