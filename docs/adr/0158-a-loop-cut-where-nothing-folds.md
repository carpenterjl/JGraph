# ADR 0158 — A loop cut where nothing folds

## Status

Accepted. Item 11 of the head2head_v3 gap-closure plan (`docs/plans/gap-closure-07-12-plan.md`,
rev 6, agreed with Codex): the six rows whose loop is independent per output element —
`d11_diff_10M`, `d11_discretize_10M`, `d11_histcounts_10M_256`, `d11_sortrows_500k`,
`d13_besseli_200k` and `d13_besselk_200k` — threaded, each under a contract that the thread count
and the grain are not inputs. Five stages: 11a the expensive scalar maps, 11b `diff`, 11c
`discretize`, 11d `histcounts`, 11e `sortrows`.

## Context

At the 10c commit (4ddcc3b) each of the six rows ran on one thread, and the independent review
read the six as one item because the same thing licenses every one of them: the answer at output
`i` is a function of the input at `i` (or, for a sort, of the whole input and a tie rule), so
however the outputs are divided among threads the array that comes back is the same array. What
differed was why each was still serial:

- The Bessel rows sat at 200,000 elements, under `ParallelKernels.ComputeBoundThreshold`
  (262,144), and the maps took 64K grains — a threshold and a grain sized for a `Math.Pow` at tens
  of cycles an element, where a modified Bessel continued fraction is a few hundred nanoseconds.
- `diff` of a row vector was one contiguous slice, and `ReduceKernels.Differences` threaded across
  slices only, so ten million subtractions ran in one scalar loop.
- `discretize` stripped its input to a fresh `double[]` (`PrepStrip`), decided which of four loop
  bodies applied at every element, and wrote a `double[]` that was then adopted.
- `histcounts` copied its input (`FlattenColumnMajor`), materialised the finite values a second
  time to find their range (`Binning.EdgesFor`), tallied in one loop, and always wrote the
  per-value bin array whether the call asked for it or not.
- `sortrows` copied its input, sorted its ranks with one `Array.Sort` per key column, and gathered
  the rows through the permutation into a `double[]`; the positions were always built.

The fixture written first, `m158_loops.m` (260 lines from R2025b, run on the 10c binary before
any code moved), also showed four things the item had not set out to change, listed under the
consequences: `diff` past its length and of a scalar answered the wrong empty; `sortrows` ranked
`-0` ahead of `+0` where MATLAB ties them, and refused a direction word as its second argument;
and `histcounts` with a requested bin count wrote its edges an ulp off MATLAB's on most of them,
and lost the sign of a `-0` minimum on the first.

## Decision

**11a — an expensive cost class for the delegate maps.** `ParallelKernels.ForCostly` cuts a length
into grains of `CostlyGrain` (4,096 by default; `JGRAPH_COSTLY_GRAIN` and a settable property for
the sweep and the tests) and threads from `ExpensiveThreshold` (16,384). `PackedMath.Map`, `Zip`
and `ZipScalar` take a `CostClass`; `Expensive` routes them there, `Compute` (the default) leaves
them exactly as they were. A builtin may name the class only because it already reaches one of
the three maps with a per-element delegate, which is what makes a finer partition a different
cut of the same answers. `besselj`, `bessely`, `besseli` and `besselk` name it. `gamma`,
`gammaln`, `erf` and `erfc`, which the plan had listed, do not: the sweep (below) measured them at
a tenth of a Bessel function's cost per element, no faster on 4K grains than on 64K at a million
elements and slower than the serial loop at two hundred thousand, so the class is named by what
was measured and not by the count of exponentials in a formula. `expint` stays out, as the plan
said: its convergence test is array-wide. A refusal from inside a threaded map — a negative
argument to `besselk` — comes back out of `ParallelKernels` as the one exception it was, so the
builtin's `Guarded` wrapper sees what it always saw.

**11b — `diff`'s contiguous branch.** One contiguous slice is cut by output index over
`ParallelKernels.For` at `MemoryBoundThreshold`, each grain a `TensorPrimitives.Subtract` of the
slice against itself shifted by one, reading one element past its end; many contiguous slices take
the same subtraction per slice inside the existing block walk. The operand order is the scalar
loop's (`x[j+1] - x[j]`), so a NaN keeps the payload the scalar loop kept. Higher-order `diff`
stays staged, one pass per order, and the interleaved layout is untouched.

**11c — `discretize` in place.** A packed input is read where it lies; a boxed or datetime input is
stripped once and wrapped. The four bodies — which edge a bin owns, numbered or labelled — are
four loops chosen once, and the loop runs in grains over `ParallelKernels.For` at
`MemoryBoundThreshold` into packed storage that becomes the answer. `BinFinder` is unchanged: its
arithmetic guess and its search repair are read-only, so every thread asks it the same question
and gets the same bin.

**11d — `histcounts` in three parts.** The input is read in place. For a requested bin count with
no width and no limits, the range comes from `PackedMath.FiniteRange`, one streaming pass whose
grains are merged in index order with the same `<` rule as the serial fold, so a `-0` and a `+0`
tie in favour of whichever came first, exactly as `Enumerable.Min` over the finite copy did; the
Scott, Freedman–Diaconis, Sturges, square-root, width and limits rules keep `EdgesFor` as it was.
The per-value bin array is made only when a third output is wanted. The tally is `long[]`, and
from `MemoryBoundThreshold` values with up to 65,536 bins it is one `long[]` per block — the
blocks a function of the length and the bin count alone — added in bin order: whole numbers add
exactly, so the threaded count is the serial count.

**11e — `sortrows`.** The input is read in place and the positions are built only when wanted. The
gather walks column by column in blocks of output rows, sequential writes with the reads following
the permutation, threaded from `MemoryBoundThreshold` elements. The rank pass is
`SortKernels.SortRanks(ulong[] keys, int[] payload, int n)`: below `RankThreshold` (262,144) it is
the library sort followed by the tie repair the serial pass always made — each run of equal keys
gets its payload put ascending, which is what makes an unstable sort stable; above it the keys are
cut by value into buckets off a strided sample, scattered block by block so each bucket keeps
arrival order, and sorted and repaired one bucket per thread; laid end to end the buckets are the
answer, as `SortKernels.SplitByValue` reasons for doubles. Equal keys always share a bucket, so a
run's repair sees the whole run. The plan's signature carried a `descending` flag; the caller
already complements a descending key's ranks, so the kernel sorts ascending and takes no flag. The
pass is not routed through the double sort, as the plan said, and the ranks stay `RankOf`'s — with
one change the fixture forced (below): the sign of a zero is folded away before ranking.

**The first call runs optimised code.** The first rig run put the `discretize` row at 0.045–0.18 s
across its five samples while the row's probe, four calls in a fresh process, stood at 0.040 s
warm — and the row was slow on one thread too. `DOTNET_TieredCompilation=0` took it to 0.023 s. A
benchmark row is one call, and that one call ran the tiered JIT's tier-0 code: the fill loop, and
above all `BinFinder.Of`, called ten million times, with the tiering delay resetting while the
script's earlier builtins kept jitting new methods (the same effect ADR 0153's 07d note met from
the other side, where threads divided tier-0 code). The fill loops of `discretize`, the histogram
tally, the row gather and the finite-range fold, and the finder's `Of`, `OfRightClosed` and its
two searches, now carry `MethodImplOptions.AggressiveOptimization`; the attribute on the loop
alone did not do it, because the per-element callee was still tier-0. The row then measures
0.022–0.024 s on every run.

**The four parity repairs the fixture forced**, each older than the item and pinned in
`m158_loops` as exact lines:

- `diff` past its length answers the empty with that dimension zero and the others kept
  (`diff` of a 3-by-4 five times is 0-by-4; of a column three times, 0-by-1); `diff` of a scalar is
  the 0-by-0 empty. The packed road had answered a 1-by-0 row for all of them and the boxed road
  for the scalar.
- `sortrows` compares the two zeros equal, as MATLAB does: rows that differ only in the sign of a
  zero keep their arrival order or fall to the next key. `RankOf` orders `-0` before `+0` — right
  for `unique` and the set operations, where the two are distinct members — so `RowOrder` folds the
  sign before ranking.
- `sortrows(A, 'descend')` and `sortrows(A, {'ascend', …})`: a direction alone, over every column,
  which MATLAB reads as the two-argument form and JGraph refused as "not an array of numbers".
- `histcounts` with a requested count writes its width as the decimal it is. MATLAB's `binpicker`
  takes `p10 * ceil(ll / p10)` with `p10 = 10^k`; for negative `k` its `10^k` is an ulp below the
  true value (R2025b's `10^-5` is `3ee4f8b588e368f0`, the correctly rounded `1e-5` is `…f1`) and
  the product lands, by cancellation, on the correctly rounded decimal — `0.01002`, say — where
  JGraph's exact `1e-5` times 1002 lands an ulp above it. The width is now `ceil(ll / p10)`
  divided by the exact power of ten, the correctly rounded decimal by construction, and the two
  engines' edges agree to the bit on every count the fixture asks for. The first edge is written as
  the left edge itself, as `binpicker` writes it, so a `-0` minimum keeps its sign (JGraph's
  `left + 0 * width` had answered `+0`); `Spanning` and `Uniform` do the same.

**Thresholds and grains from the sweep**, not the projection: `probe_11_grain_sweep.m` and
`probe_11_rank_sweep.m` under `runs\11-sweep-new`, three runs each, every number the median of
five warm calls.

| expensive map, warm ms | serial | grain 1024 | grain 4096 | grain 16384 | grain 65536 |
| --- | --- | --- | --- | --- | --- |
| `besseli` 16,384 | 7.34 | 2.25 | 2.53 | 7.17 | 7.46 |
| `besseli` 65,536 | 18.46 | 5.31 | 6.05 | 7.77 | 17.65 |
| `besseli` 200,000 | 53.43 | 13.98 | 9.32 | 9.69 | 18.45 |
| `besseli` 1,000,000 | 264.26 | 34.23 | 35.35 | 37.13 | 35.54 |
| `besselk` 200,000 | 48.28 | 8.15 | 8.68 | 9.63 | 16.80 |
| `gamma` 200,000 | 4.96 | 7.95 | 7.92 | 10.46 | 2.23 |
| `gamma` 1,000,000 | 24.41 | 7.24 | 6.60 | 6.72 | 6.72 |
| `erf` 200,000 | 3.80 | 10.69 | 11.01 | 10.99 | 4.91 |
| `erf` 1,000,000 | 18.36 | 8.22 | 7.99 | 7.74 | 6.51 |

The 4K grain is the Bessel functions' best or within noise of it at every length; 16K is already
three times faster than serial, so the threshold stays there. Gamma and erf are the other kind of
map, and stay on the compute-bound grain.

| `sortrows(T, 2)`, warm ms | serial | threaded rank sort |
| --- | --- | --- |
| 250,000 rows | 37.2 | 37.6 (under the threshold: serial) |
| 500,000 rows | 47.2 | 22.5 |
| 524,288 rows | 52.4 | 25.1 |
| 1,000,000 rows | 110.4 | 24.8 |

The threshold is 2^18 rows: a quarter-million-row key sorts in the time the partition's two extra
passes cost, and from half a million up the buckets win by two to four times.

## Consequences

**Contract, checked four ways.** `LoopThreadingM158Tests`: an expensive map answers the same bits
at one thread and sixteen over 16K, 200K, 262,144 and 1M elements and over grains of 1000, 4096,
16384 and 65536; `ForCostly` cuts at its grain and stays on the calling thread under its
threshold; one contiguous slice differenced by output index answers the scalar loop's bits with
signed zeros, NaN payloads and infinities on and off the grain boundaries; `SortRanks` is the
stable reference sort at 300,000 keys with the threshold as shipped and dropped to a thousand,
over five distinct keys, many keys with ties, one key everywhere, the extremes and a sorted input,
at one thread and sixteen; `FiniteRange` is the serial fold with whichever zero came first.
`LoopBuiltinsM158Tests`: the histogram tally, the discretize fill and the row gather answer the
same on one thread and sixteen over 2.15M values at 2, 256, 65,536 and 70,000 bins, and the four
parity repairs. `m158_loops` (260 lines): the small cases spelled to the character, `num2hex`
where a sign or a payload matters, and 43 bits lines over three-million-element inputs built from
products, sums and `mod` alone — identical on both engines where a sine would not be (ADR 0093) —
for `diff` in every layout and order, `discretize` under both edge rules and with labels,
`histcounts` at 256, 65,536 and 70,000 bins with its bin output, and `sortrows` over 600,000 and
1,100,000 rows ascending, descending, mixed and tied: all 260 agree with R2025b on the new build
at the default thread count and at `JGRAPH_THREADS=1`; on the 10c binary 45 lines failed, every
one a repair above or the script stopping at `sortrows(A, 'descend')`. `bits_d13_maps.m`
(`runs\…\bits\out`): the four Bessel names plus gamma, gammaln, erf and erfc over 16K, 200K,
262,144 and 1M elements, 40 bits lines equal between the default thread count and one thread.
`stess_86.m` carries the invariants of items 07 to 11 on the Release exe.

**Measured** on the working tree against the 10c binary re-run on `d11_datafun` and
`d13_specint` the same day (`runs\11-before-10c`, five counterbalanced repeats against R2025b;
`runs\11-loops`):

| row (median of 5, s) | before | after | speed-up | J/M before → after |
| --- | --- | --- | --- | --- |
| `d11_diff_10M` | 0.028 | 0.026 | 1.08× | 3.75 → 3.71 |
| `d11_discretize_10M` | 0.072 | 0.023 | 3.13× | 5.83 → 1.92 |
| `d11_histcounts_10M_256` | 0.153 | 0.016 | 9.56× | 7.05 → 0.80 |
| `d11_sortrows_500k` | 0.088 | 0.035 | 2.51× | 4.89 → 1.94 |
| `d13_besseli_200k` | 0.100 | 0.026 | 3.85× | 12.50 → 3.25 |
| `d13_besselk_200k` | 0.085 | 0.015 | 5.67× | 9.11 → 1.60 |
| `d11_total` | 3.952 | 3.724 | 1.06× | 0.33 → 0.35 |

| probe, cold / warm (s) | before | after | speed-up |
| --- | --- | --- | --- |
| `d11_diff_10M` | 0.035 / 0.030 | 0.027 / 0.023 | 1.27× / 1.32× |
| `d11_discretize_10M` | 0.081 / 0.072 | 0.029 / 0.025 | 2.81× / 2.87× |
| `d11_histcounts_10M_256` | 0.150 / 0.138 | 0.013 / 0.008 | 11.2× / 16.4× |
| `d11_sortrows_500k` | 0.091 / 0.081 | 0.044 / 0.026 | 2.07× / 3.13× |
| `d13_besseli_200k` | 0.092 / 0.084 | 0.023 / 0.014 | 3.94× / 5.92× |
| `d13_besselk_200k` | 0.084 / 0.079 | 0.023 / 0.015 | 3.70× / 5.29× |

The histogram row now runs under MATLAB's (0.016 against 0.022 s; the warm probe 0.008 against
0.010). The other five keep a gap: `diff` is a memory-bound pass over 160 MB whose 80 MB answer is
fresh storage each time (MATLAB 0.007 s), and the item's contract forbade any road but the
subtraction; `discretize`, `sortrows` and the Bessel rows are at 1.6–3.3 times MATLAB from 5–12.
The untouched rows of both scripts are within noise of the before-run (`d13_gamma_5M` 0.077 →
0.080, `d13_erf_5M` 0.092 → 0.092, `d11_unique_2M` 0.269 → 0.272); `d13_total` moved 2.605 →
2.788 s on rows this item did not touch (`cart2pol`, `hypot`, the integer rows), the
counterbalanced spread of a run and not a change. Rows and probes are in `runs\11-loops-b`;
`runs\11-loops` is the first pass, before the first-call repair, and is kept for its Bessel
numbers, which are the same.

**What did not change.** Every map that was on the compute-bound grain is still on it; the
interleaved `diff`, the higher-order chain and the `expint` road are as they were; `BinFinder`,
`EdgesFor`'s automatic rules and the double sort's tie rule are untouched. `accumarray` answers a
row where MATLAB answers a column (found writing the stress script; filed).

## Divergences

None. Item 11 moves four answers, and each of them toward R2025b: the empty shapes of `diff`, the
zero tie and the direction word of `sortrows`, and the requested-count edges of `histcounts` — all
pinned as exact lines in `m158_loops`. Every other line of the fixture, every bits line included,
was already what R2025b answers, and the threaded roads answer it still.
