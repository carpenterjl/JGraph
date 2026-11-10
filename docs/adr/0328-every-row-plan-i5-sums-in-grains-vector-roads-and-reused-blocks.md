# ADR 0328 — The every-row plan's I5: sums in grains, one-pass vector roads and reused native blocks

## Status

Accepted, 2026-11-10. Stage I5 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 8). **Partly done**: eleven of the nineteen rows are at or under R2025b's median, eight are still
over it (open items 261 to 264), and three rows outside the stage are slower than the build before
it (open item 265). Every CHK line of the three scripts is unmoved.

## Context

The GUI round (ADR 0322) left nineteen rows to I5: the elementwise, reduction and integer kernels
of `d03_arrays`, `d11_datafun` and `d13_specint` over 1M to 50M elements, 0.54 s of excess in all.
The plan's reading was one discipline, count the passes over memory. Reading each row down to its
kernel found five separate things:

- **A whole-vector sum was one dependency chain.** `sum(x)`, `mean(y)` and both passes of `std`
  were single left folds on one thread (`ReduceKernels`, `PackedMath.Sum`), because the fold's
  contract was the boxed builtin's order to the bit. Fifty million additions at one per 0.9 ns are
  45 ms each; `d03_reductions` is two of them and a `std`.
- **The data-preparation names worked on managed copies.** `normalize` flattened the array, made a
  `List<double>` of what was not NaN (twice), averaged it through LINQ, and wrote its answer into a
  managed array; `rescale`, `rmmissing`, `isoutlier`, `gradient` and `cumtrapz` were the same
  shape (`gradient` and `cumtrapz` also built an array of the sample positions 0, 1, 2, ...), and
  `rmmissing` and `isoutlier` boxed one `JgsValue` per element for a mask.
- **Every large answer was written into untouched memory.** A packed array over 8 MB is a
  `NativeBuffer`, each one a fresh block from the operating system, so each page of each answer
  faulted on its first write; and the builtins that still answered a managed `double[]` (the
  moving windows, the names above) paid the collector's clearing and, since the app took Server GC
  (ADR 0323), a fresh commit as well. `d11_cumtrapz_5M`, `d11_movmean_10M_w51` and
  `d11_movstd_10M_w51` had been under R2025b in the GUI round and were over it after I11.
- **`isprime` divided each value by every odd number up to its root, and `primes` sieved a
  `bool` per number into a growing list.**
- **The integer classes and the logical masks are stored as doubles.** `uint8(0) - u8` over 20M
  elements reads 160 MB and writes 160 MB where R2025b reads and writes 20 MB; `y > 1.0` over 50M
  writes a 400 MB mask where R2025b writes 50 MB. The arithmetic is already one fused pass an
  operation (ADR 0173); what is left is the width of the storage.

## Decision

### Sums are folded in grains (`ReduceKernels.GrainSum`)

`sum`, `mean`, `std`, `var` and `variance` now mean this: the values are cut into grains of
`ParallelKernels.GrainElements` (65,536), each grain is a left fold from zero, and the grains'
totals are a left fold from zero in index order. Up to one grain that is the left fold it always
was, so no array of 65,536 elements or fewer changes by a bit. Past it the definition is still a
function of the values alone (never of the thread count), so one long run hands its grains to the
threads from 4M elements (`ReductionThreshold`) and gives the same number at one thread and at
sixteen. The squared deviations of `std` and `var` are folded in the same grains.

The definition is one, in every road:

- the packed kernels (`ReduceKernels.Sum`, `Mean`, `Variance`): a contiguous run in grains (across
  threads when it is the whole reduction), a strided slice or an `omitnan` slice by
  `StridedSum`/`StridedSquaredDeviations`, whose grains are counted in the values kept, so
  `sum(x, 'omitnan')` is the grain sum of `x` with its NaN removed, which is how the boxed wrapper
  computes it; the row-at-a-time panel form (a plain left fold per output) is kept only where the
  fold dimension is no longer than a grain;
- the boxed builtins (`sum`, `mean`, `JgsStdlib.Variance`, the weight-1 variance) and
  `PackedMath.Sum`;
- `normalize`'s own mean and deviation (`PrepMean`, `PrepStd`), so `normalize(x)` is
  `(x - mean(x)) / std(x)` to the bit.

This changes the last bits of a sum over more than 65,536 elements against every earlier build.
It is not a divergence from MATLAB in either direction: R2025b's own `sum` is blocked and threaded
and its last bits were never ours. `cumsum` stays one chain on one thread, as R2025b's is
(bit-identical there at 1 and 6 threads), and now runs in a loop of its own
(`ReduceKernels.RunningSum`) into an uninitialised destination. `rms`, `prod` and the other
reductions are unchanged.

### One-pass roads over a packed vector (`JgsVectorRoads`, `VectorKernels`, `PrimeKernels`)

For the plainest call of each name (the array alone, the default method) over a packed array of
plain doubles, the answer is computed where the array lies and written into packed storage:

- **`normalize(x)`** (a vector): the grain sum; if it is finite, the grain-folded deviation, then
  `(x - centre) / scale` in one pass across threads. A sum that is not finite (a NaN, an infinity)
  goes to the general road.
- **`rescale(A)`**: one scan for the least and greatest across threads, one pass of
  `0 + (x - from) / span`. A NaN or a constant array goes to the general road.
- **`gradient(v)`** and **`cumtrapz(v)`** (a vector, unit spacing): the neighbour difference over
  two across threads; the running half-sum on one thread (it is one chain).
- **`rmmissing(v)`**: the NaN are counted grain by grain, so each grain knows where its survivors
  go and the grains are copied on any thread; the mask is made only when the second output is
  asked for.
- **`isoutlier(v)`** (the default rule): one scratch copy, the two selections the general road
  already made (`SelectKernels.PartialSort`), and one pass for the mask.
- **`isprime(A)`** where the greatest value is under 2^31 and no more than 64 times the element
  count (plus a million): one sieve of the odd numbers up to the greatest, cut into 32 KB pieces,
  and one lookup an element. **`primes(n)`** reads the same sieve off in order.

Each road performs the general road's arithmetic on each element in the general road's order, so
the answers agree to the bit, and whatever the general road has a rule for (a NaN to step over, an
infinity, an answer of fewer than two elements, a matrix along a dimension, an option) sends the
call back to it. `JGRAPH_VECTOR_FAST=0` turns the roads off.

The moving windows (`movmean`, `movstd` and the rest) over a packed vector write their answer
through `WindowKernels.SlideInto` into packed storage; the walk is the same walk.

### Freed native blocks are reused (`NativeBlockCache`)

`NativeBuffer` offers the block it frees to a short list and takes its next block from the list
when one fits with no more than an eighth to spare. The list holds at most eight blocks and a
sixteenth of physical memory (between 256 MB and 1 GB), evicts the oldest first, returns a block
nobody has asked for within ten seconds to the operating system, and is emptied before the
allocator decides a request does not fit in memory. A reused block holds what its last owner left,
so only a destination that is written in full takes one, which was `AllocateForOverwrite`'s
contract already; a zeroed request goes to the operating system, whose fresh pages are zero for
nothing. The first form of this cleared a reused block for a zeroed request, and the first
measurement (`runs\i5-first`) showed what that costs: a pass over the whole block, on every row
that copies into a zeroed buffer (`d03_cumsum_20M` 0.081 s against 0.070 before the stage,
`d03_arrays` 1.65 s against 1.59). So the destinations that are written whole now say so: the
comparison masks, the mask and index gathers, the copy of a packed array, and the range copy
`x(a:b)`, which over 2M elements or more is also copied in grains across threads. This is the pool
ADR 0157 designed and did not build, because no consumer's numbers asked for it; the windows, the
vector roads, the masks and the cumulative reductions are now those consumers.

What the list holds is added to the process's peak: `d03_arrays` peaks at 1.7 GB without it and
2.6 GB with it (eight reuses; a 2 GB cap gave the same eight at 3.7 GB, which is why the cap is
1 GB). `JGRAPH_BLOCK_CACHE=0` turns the list off, `JGRAPH_BLOCK_CACHE_MB` sets the cap, and
`JGRAPH_BATCH_STATS=1` now reports `blocks_offered` and `blocks_reused`.

`JGRAPH_BLOCK_POISON=1` is the must-fail seam ADR 0157 asked for: every native block handed out
for overwriting, fresh or reused, is first filled with NaN, so a kernel that promised to write
every element and did not shows it in its answer.

### A long mask is written past the cache

`x > scalar` (and the other five comparisons against a scalar) over 2M elements or more writes its
mask with non-temporal stores (`PackedMath.CompareScalarStream`): an ordinary store reads each
line of the destination before overwriting it, a third of the comparison's memory traffic. The
mask is the same mask. `JGRAPH_STREAM_MASKS=0` puts the ordinary stores back.

### Measured

`head2head_v3\measure_i5.ps1` and `compare_i5.py`, the three scripts unchanged, one session per
condition (`runs\i5-second`): three headless sessions of this build and three of the build before
it (441ee36f) alternated, one app session with the stage on and one with its switches off, and one
eight-pass headless session of each build read over passes 4 to 8 (steady). Medians of the timed
passes, in seconds.

| Row | R2025b | before | I5 | I5 app | app, switches off | I5 steady | before, steady |
|---|---|---|---|---|---|---|---|
| `d03_elementwise_50M` | 0.330 | 0.277 | 0.288 | 0.312 | 0.287 | 0.215 | 0.212 |
| `d03_reductions` | 0.057 | 0.085 | 0.039 | 0.037 | 0.048 | 0.035 | 0.095 |
| `d03_dimreduce` | 0.021 | 0.023 | 0.020 | 0.021 | 0.026 | 0.021 | 0.021 |
| `d11_normalize_10M` | 0.038 | 0.134 | 0.019 | 0.017 | 0.132 | 0.018 | 0.134 |
| `d11_rescale_10M` | 0.026 | 0.090 | 0.022 | 0.028 | 0.092 | 0.022 | 0.097 |
| `d11_rmmissing_5M` | 0.039 | 0.046 | 0.008 | 0.006 | 0.047 | 0.010 | 0.050 |
| `d11_isoutlier_2M` | 0.041 | 0.061 | 0.040 | 0.037 | 0.068 | 0.038 | 0.063 |
| `d11_movmedian_2M_w21` | 0.023 | 0.015 | 0.022 | 0.018 | 0.019 | 0.079 | 0.017 |
| `d13_primes_5M` | 0.014 | 0.018 | 0.009 | 0.009 | 0.020 | 0.009 | 0.018 |
| `d13_isprime_1M` | 0.084 | 0.085 | 0.005 | 0.005 | 0.087 | 0.005 | 0.084 |
| `d13_uint8_sum_20M` | 0.005 | 0.012 | 0.005 | 0.005 | 0.006 | 0.005 | 0.015 |
| `d03_cumsum_20M` | 0.045 | 0.070 | **0.056** | 0.052 | 0.049 | 0.057 | 0.074 |
| `d03_mask_50M` | 0.020 | 0.060 | **0.042** | 0.043 | 0.058 | 0.040 | 0.059 |
| `d11_movstd_10M_w51` | 0.027 | 0.039 | **0.057** | 0.082 | 0.078 | 0.037 | 0.036 |
| `d13_gamma_5M` | 0.012 | 0.039 | **0.021** | 0.019 | 0.088 | 0.021 | 0.018 |
| `d13_gammaln_5M` | 0.012 | 0.014 | **0.014** | 0.014 | 0.015 | 0.015 | 0.015 |
| `d13_int32_arith_20M` | 0.009 | 0.032 | **0.033** | 0.024 | 0.035 | 0.039 | 0.035 |
| `d13_int32_saturate_20M` | 0.007 | 0.013 | **0.012** | 0.012 | 0.013 | 0.013 | 0.015 |
| `d13_uint8_floor_20M` | 0.002 | 0.014 | **0.012** | 0.014 | 0.021 | 0.015 | 0.016 |

The first eleven rows are at or under R2025b in the headless median; the last eight are over it.
Two of the eleven are marginal: `d11_rescale_10M` is 0.028 in the app session, and
`d11_movmedian_2M_w21` read 0.079 in the steady session (the same slow mode appeared in the first
measurement; the window road is the same walk as before, so the mode is not understood).

Rows the stage was not asked for and moved all the same: `d11_cumtrapz_5M` 0.055 to 0.012 (R2025b
0.043), `d11_gradient_5M` 0.058 to 0.009 (0.065), `d11_diff_10M` 0.011 to 0.006 (0.007). Three
went the other way: `d11_trapz_5M` 0.029 to 0.044 (steady 0.031 to 0.041; R2025b 0.044),
`d13_erfc_5M` 0.016 to 0.024 (R2025b 0.028) and `d13_int_round_divide_5M` 0.051 to 0.059 (R2025b
0.161). All three are still at or under R2025b; the app session with the switches off reads
0.023, 0.019 and 0.045 for them, so the cause is one of the switched features (the block list, the
vector roads or the streamed masks) and not the grain sum. It was not diagnosed (open item 265).

Script walls (R2025b's GUI wall / before / I5 / I5 app): `d03_arrays` 2.887 / 1.555 / 1.440 /
1.493, `d11_datafun` 2.294 / 1.705 / 1.370 / 1.316, `d13_specint` 3.451 / 1.133 / 0.996 / 0.978.

### Checks

- All 76 CHK lines of `d03_arrays`, `d11_datafun` and `d13_specint` are byte-identical to the GUI
  round's in all ten sessions of the measurement, and again with `JGRAPH_BLOCK_POISON=1`.
- `VectorRoadsI5Tests` (20 tests): each road against the general road on the same data (the
  switch off) and on the inputs that must fall back to it, the grain sum against its serial
  definition at any thread count, `sum`, `mean` and `std` the same at one thread and at all and
  the same in both storages, a long streamed mask against the ordinary one, a reused block holding
  what a fresh one would, and the list serving overwrite destinations and never a zeroed request.
  They pass in packed storage with the poison seam on and in boxed storage.
- The 387 tests of the areas the diff touches (reductions, windows, preprocessing, cleaning,
  comparison and indexing, the allocator) pass in packed storage with the poison seam on and in
  boxed storage.
- The stress suite passes 88 of 89 scripts; the one failure is `stess_40`'s stale assertion (open
  item 254, older than this stage). The seven `verify-*.py` scripts pass and the divergence
  harvest is unchanged (1343 divergences from 217 ADRs).
- The whole suite was run once in packed storage: 13,406 of 13,408, the two failures both older
  than the stage (open items 253 and 260).

## Consequences

- A sum, mean, deviation or variance over more than 65,536 elements differs in its last bits from
  every earlier build's, and is the same number at any thread count. A recorded expectation that
  held such a sum to the last bit would have moved; none in the parity suite or the three scripts'
  CHK lines did.
- A process that works on large arrays holds up to eight freed native blocks (1 GB at most) for
  ten seconds after their last use. `d03_arrays` peaks at 2.6 GB where it peaked at 1.7 GB.
- A kernel that takes its destination from `AllocateForOverwrite` and does not write all of it now
  answers with another array's numbers instead of zeros. `JGRAPH_BLOCK_POISON=1` is how that is
  caught, and a new consumer of `AllocateForOverwrite` should be run under it once.
- The plainest calls of `normalize`, `rescale`, `gradient`, `cumtrapz`, `rmmissing`, `isoutlier`,
  `isprime` and `primes` have two roads. A change to the general road's arithmetic has to be made
  in the vector road too, and `VectorRoadsI5Tests` compares the two.
- Four switches are added for measurement and diagnosis: `JGRAPH_VECTOR_FAST`,
  `JGRAPH_BLOCK_CACHE` (with `JGRAPH_BLOCK_CACHE_MB`), `JGRAPH_STREAM_MASKS` and
  `JGRAPH_BLOCK_POISON`.

## Divergences

None. No road answers differently from the general road it stands in for, and the grain sum is
neither nearer to nor further from R2025b's `sum` than the left fold was (both differ from it in
the last bits of a long sum, as any two summation orders do).

## Still open

- **The integer classes and the logical masks are 8-byte doubles** (open item 261):
  `d03_mask_50M` 0.042 against 0.020, `d13_int32_arith_20M` 0.033 against 0.009,
  `d13_int32_saturate_20M` 0.012 against 0.007, `d13_uint8_floor_20M` 0.012 against 0.002. The
  arithmetic is one pass already; what is left is four to eight times R2025b's memory traffic.
- **`gamma` and `gammaln`** (open item 262): 0.021 and 0.014 against 0.012. The stage did not
  change their kernels.
- **The moving windows** (open item 263): `d11_movstd_10M_w51` is 0.057 headless against 0.039
  before the stage and 0.027 in R2025b, level with the old build at steady state (0.037 against
  0.036); `d11_movmedian_2M_w21` has an unexplained slow mode.
- **`d03_cumsum_20M`** (open item 264): 0.056 against 0.045, one chain on one thread.
- **Three rows slower than before the stage** (open item 265): `d11_trapz_5M`, `d13_erfc_5M`,
  `d13_int_round_divide_5M`, cause not diagnosed.
- `isoutlier` of a scalar is an internal error on the general road (open item 259, older than
  this stage), and a cancellation test in the runtime suite is stale (open item 260).
