# ADR 0157 — Scratch measured before it is pooled

## Status

Accepted. Item 10 of the head2head_v3 gap-closure plan (`docs/plans/gap-closure-07-12-plan.md`,
rev 6, agreed with Codex): the `d01_lu_2000` row and the allocation share of
`d16_ode15s_brussode200_full`. Three stages: 10a, each candidate consumer of a scratch pool
measured on its own allocation path; 10b, the pool itself, which no candidate earned; 10c, the
stiff solver's per-run buffers, written in place.

## Context

The independent review's first form of this item was a native scratch pool under every large
temporary, on the premise that page faults on fresh memory dominated the LU row and the stiff
row. Its own correction stood at round 4: the `d01` script retains `L`, `U` and `P` to its end,
so no pool can recycle them during the call; nothing showed page faults dominating anything; and
`NativeMemory.AllocZeroed` gives no proof that pages are released to the operating system. So the
plan reframed the item as *private scratch with bounded retention, measured*: four candidate
consumers, each measured on its own numbers before any of them joins a pool, and the LU row
measured before any pool at all.

The four candidates and what the source says of each, at the 09c commit (c7bce1c):

- **The LU marshalling copy.** `lu(A)` reads a packed `A` through `ColumnMajorOf`
  (`JgsBuiltins.LinalgMarshal.cs`), one uninitialised 32 MB array and a block copy at n = 2000,
  then factors that copy in place (`LuDecomposition.FactorAdopting`, `dgetrf` over the pinned
  array). The copy is the factorization's workspace; the three outputs are built straight into
  packed storage and retained by the script.
- **Bluestein's scratch.** `FftKernels.Bluestein` rents its two planes from
  `ArrayPool<double>.Shared`, which on .NET 8 pools arrays to 2^30 elements; since ADR 0154 the
  dispatcher sends every 5-smooth length above 32 down the mixed-radix road, so Bluestein serves
  only lengths with a larger prime factor, and no head2head_v3 row has one.
- **The window kernels' copy.** `movmean` and its family on a packed vector borrow the buffer
  (`WindowKernels.Slide(WindowStat, NumericBuffer, …)`, ADR 0151): the walk reads the packed
  storage in place and allocates only its answer. The `FlattenColumnMajor` copy the plan named is
  the boxed road's, which a packed vector never takes.
- **08b's image intermediate.** `Filters.SeparableConvolve2` rents its `ah·(aw+|v|−1)` plane from
  `ArrayPool<double>.Shared` and returns it in a `finally` (ADR 0155).

And the stiff solver: `Ode15s.Refactor` formed a fresh `double[n, n]` iteration matrix
(`OdeStiffSupport.IterationMatrix`) and `LuDecomposition.Factor(double[,])` allocated its own
column-major copy to factor — two 1.28 MB large-object allocations per refactor at n = 400,
twenty-odd refactors a run, for a matrix nothing reads once it is factored; and the numerical
Jacobian's dense branch allocated a `difference` matrix it wrote and never read.

## Decision

**10a — measure first, per consumer.** Each candidate was measured on the 09c binary, with the
rig's probes and batch statistics (`JGRAPH_BATCH_STATS=1`: the process's allocated bytes and
collection counts) and, for LU, the native trace (`JGRAPH_NATIVE_TRACE=1`: `dgetrf`'s own seconds
inside the row). The numbers are in the consequences; the decisions they settled:

- The LU marshalling copy is dropped from 10b: it is about 5 ms of an 87 ms row, under the plan's
  10% line. What the row spends beyond `dgetrf` is its three output fills, which are retained by
  the script and no pool can serve; they are the row's gap, and a follow-up of their own (filed).
- Bluestein's scratch stays on the shared pool, which already serves 93% of what a warm sweep of
  twelve prime lengths rents; what a private pool could return is a tenth of what it would hold.
- The window kernels have no copy to pool: a warm `movmean` over ten million values allocates its
  answer and nothing else.
- The image intermediate stays on the shared pool: the warm `conv2` already runs at MATLAB's time,
  and the cold call is the one a pool cannot serve.

**10b — the pool.** No consumer earned it, so none was built. The plan's design (a size-class
free list from 1 MB to 256 MB, two entries a class, 512 MB in all, trimmed on a Gen2 collection,
every user filling or clearing what it reads, four-pattern fault injection with a must-fail seam)
stands as the design to build the day a consumer's own numbers ask for it; a pool with no user is
a cache that is never right.

**10c — the stiff solver's per-run buffers.** `Ode15s` allocates one iteration matrix and one
factor buffer at the start of a run and writes both in place at every refactor:
`OdeStiffSupport.IterationMatrixInto` writes `m − scale·j` element for element in the same order
and with the same expression as `IterationMatrix`, so a signed zero settles the same way;
`LuDecomposition.FactorReusing(double[,], double[])` transposes into the caller's buffer and
factors it in place, and the decomposition then owns that buffer — the previous decomposition
made from it is stale from that moment, which is how `Refactor` used it anyway (`factored` is
replaced, never kept). The mass-matrix addend and the DAE row scaling apply to the reused matrix
exactly as they applied to the fresh one. The numerical Jacobian's dense branch no longer
allocates the difference matrix; the pattern branch, which reads it back to take a column's
largest entry as the sparse matrix holds it, keeps it.

Contract: `bits=` on every solution the solver answers and `exact` on every counter, checked two
ways — `StiffScratchM157Tests` holds the in-place forms to the allocating forms' bits over six
orders, zeros and negative scales included, and proves a reused buffer answers the second matrix;
and `bits\bits_d16_stiff.m` (the Brusselator at N = 200 full and with `JPattern`, at N = 20 under
`BDF`, `MaxOrder`, `NormControl` and `Vectorized`, van der Pol under `JConstant`, a constant mass
matrix and `NonNegative`, and the hb1dae index-1 DAE with and without `InitialSlope`: every mesh
and every solution as bits, every counter exact) run on the 09c binary and on this one.

## Consequences

**10a, measured** on the 09c binary (`runs\10-before-09c` for the rows and their probes, five
counterbalanced repeats against R2025b; `runs\10a-09c` for the traced and once-per-length runs,
three repeats each).

*The LU row.* `probe_10_lu_split.m` times the row's pieces in one process under the native trace,
at n = 2000:

| piece, warm | JGraph | of which `dgetrf` | MATLAB |
| --- | --- | --- | --- |
| `det(A)`: the marshalling copy and the factorization | 0.038–0.040 s | 0.031–0.036 s | — |
| `lu(A)`, one output: the same plus one n-by-n fill | 0.057–0.061 s | " | — |
| `[L, U, P] = lu(A)`: the same plus three fills | 0.095–0.099 s | " | 0.040 s |

The copy is the difference between `det` and its `dgetrf`: about 5 ms, 6% of the row's 0.087 s
(0.083 s warm; the traced numbers carry the trace's own cost). Each output fill is about 19 ms —
`FillLower` and `FillUpper` visit n² elements with a branch or two apiece into freshly zeroed
packed storage, and the permutation matrix is n writes — so the three-output form spends more on
its outputs than on the factorization, and `dgetrf` alone (0.031–0.036 s on six threads) is already
under MATLAB's whole row. The outputs are what the script keeps, so no pool touches them; writing
them by column is filed as its own change. The probe's process allocates 291 MB over four calls:
the four copies (128 MB), the rest the script's.

*Bluestein's scratch.* `probe_10_bluestein.m` sweeps twelve prime lengths from 100,003 to 8,000,009
— every one takes Bluestein's road on this build, since ADR 0154 sends only 5-smooth lengths
elsewhere — once cold and three times warm in one process, and `probe_10_bluestein_once.m` the same
sweep once. A warm sweep rents 1,669 MB of scratch (the two planes of `Singly`, the two of
`Bluestein`, and above 1.5 M the four plan arrays the 64 MB cache limit keeps out of the cache);
the process allocated 115 MB per warm sweep beyond the cold one (2,343 MB for four sweeps against
1,997 MB for one), so the shared pool served 93% of it. Where the plan is cached the warm call runs
2.6–5.4× faster than the cold one; where it is not, warm and cold are the same, which is the plan
cache's limit (ADR 0153), not the scratch's:

| n | plan | cold | warm | warm / cold |
| --- | --- | --- | --- | --- |
| 100,003 | 6 MB, cached | 0.047–0.048 s | 0.008–0.009 s | 5.4× |
| 200,003 | 11 MB, cached | 0.034–0.035 s | 0.011–0.012 s | 3.0× |
| 400,009 | 22 MB, cached | 0.052–0.084 s | 0.020–0.022 s | 2.6–3.8× |
| 1,000,003 | 47 MB, cached | 0.100–0.103 s | 0.037–0.039 s | 2.7× |
| 1,500,007 | 87 MB, not cached | 0.233–0.252 s | 0.218–0.222 s | 1.1× |
| 3,000,017 | 174 MB, not cached | 0.494–0.510 s | 0.446–0.477 s | 1.1× |
| 8,000,009 | 378 MB, not cached | 1.06–1.34 s | 0.96–1.02 s | 1.1× |

A private pool that retained two entries a size class could return at most the 115 MB residue —
under 10 MB a call — while holding up to 512 MB between calls; and no head2head_v3 row reaches
Bluestein at all. It stays where it is.

*The window kernels.* `probe_d11_movmean_10M_w51.m` (new): the probe's process allocates 323 MB
over four calls of `movmean` on ten million values, which is the four 80 MB answers and the series;
a warm call is 0.025 s (cold 0.034 s) against MATLAB's 0.012 s, and the walk reads the packed
series in place. There is no copy for a pool to serve; the remaining gap is the walk itself, which
no item of this plan schedules.

*The image intermediate.* `probe_d06_blur_21tap`: the warm `conv2` is 0.029 s against MATLAB's
0.027 s (J/M 1.05), the cold one 0.042 s against 0.028 s. The intermediate is one 34 MB plane,
which the shared pool hands back from a 64 MB bucket on every warm call; the 137 MB the process
allocates over four calls is that bucket once plus the script's own managed pieces (the answers are
packed and uncounted). A pool has nothing to give the warm call and nothing yet to give the cold
one.

**10c, measured alone** against the 09c binary the same way (`runs\10c-stiff-scratch`), the claim
being the collection counters, not a second:

| row | scope | 09c | 10c | J/M before → after |
| --- | --- | --- | --- | --- |
| `brussode200_full` | probe, cold / warm | 1.000 / 0.550 s | 0.982 / 0.530 s | 4.73 / 9.01 → 4.79 / 8.19 |
| `d16_ode15s_brussode200_full` | benchmark row | 0.604 s | 0.578 s | 7.19 → 7.14 |
| `d16_ode15s_brussode200_pattern` | benchmark row | 0.230 s | 0.208 s | 1.75 → 1.56 |
| `d16_total` | script | 3.866 s | 3.849 s | 1.26 → 1.25 |

| counter, the probe's process (four runs) | 09c | 10c |
| --- | --- | --- |
| allocated | 5,562 MB | 5,337 MB |
| Gen0 / Gen1 / Gen2 collections | 22–23 / 9–11 / 5–6 | 18 / 4 / 1 |
| peak working set | 455–474 MB | 432–442 MB |

The 225 MB the four runs no longer allocate is 56 MB a run — twenty-two refactors of two 1.28 MB
matrices, and two Jacobians' 1.28 MB difference matrices — and it was all of the run's large-object
traffic: the Gen2 count fell from five or six to one, the Gen1 count from ten to four. The 5.3 GB
that remain are the interpreter's, four hundred script evaluations of the right-hand side per
Jacobian, which item 12's compiled bridge is for; that is why the warm probe still takes eight
times MATLAB's. The timings moved with the counters and inside their ranges (probe warm 0.535–0.561
s before, 0.514–0.550 s after; the row 0.588–0.638 s and 0.566–0.603 s), which is what a 4%
allocation change should do. The script's allocation fell from 5,267 MB to 5,155 MB and its Gen2
count from 4–6 to 1–4; every other d16 row's range overlaps its own on the two sides.

Bits: `bits_d16_stiff.m` on the 09c binary and this one, all 42 lines agree — fourteen solutions'
meshes and values as digests, and their fourteen counters exact — and the Brusselator's counters
are R2025b's own (`m157_bruss_stats.m`: N = 20 full `84 6 249 2 23 166`, N = 200 full `83 6 970 2
22 167`, the patterned runs alike).

The item overall: no pool, because no consumer's numbers asked for one; the stiff solver's
large-object churn gone; and two measurements the plan did not have — that the LU row's gap is its
output fills, not its copy, and that the stiff row's allocation is the interpreter's, not the
solver's. The other stiff solvers (`Ode23s`, `Ode23t`, `Ode23tb`, `Ode15i`) still form a fresh
iteration matrix per refactor and can take the same two calls when a row of theirs asks; none of
this plan's rows does.

## Divergences

None. Item 10 moves no answer: every solution and counter the stiff solver reports is bit for bit
what the 09c binary reported, and no builtin's output changed.
