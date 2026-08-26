# 0094 — A reduction reads where the data lies

Date: 2026-08-26 · Status: accepted (M94; the dimension-reduction rewrite of plan item B3)

## Context

Every MATLAB dimension reduction — `sum(A, 1)`, `max(A, [], 2)`, `cumsum`, `std`, twenty names in
all — ran through one wrapper in `JgsBuiltins.MatrixShape`, and the wrapper's road was built for
correctness in the days when arrays were boxed: flatten the whole array to a `double[]`, cut it into
one freshly allocated slice per column, box each slice back into a packed vector, call the scalar
builtin on it, unbox the answer, and join the pieces. For a packed 8000×5000 matrix that is 320 MB
copied three times and five thousand allocations before a single addition that matters — the
head-to-head `d03_dimreduce` row stood at 2.24 s after M93's flatten fix (2.54 s before it) against
MATLAB's 0.020 s, the worst remaining ratio in the suite. `cumsum` over 20M elements paid the same
toll in miniature: 0.27 s for what is 40 ms of dependent additions.

M93 established the discipline that makes a fix safe: Tier E means the fast form is the boxed fold
to the bit, fixed-grain threading means a thread count is never an input to an answer, and the
DOP-invariance suite is how both claims stay true. What was missing was kernels that fold along a
dimension of column-major storage without ever materializing a slice.

## Decision

### The decomposition

A reduction along dimension `d` of column-major storage is `(inner, n, outer)`: `inner` the product
of the dimensions below `d`, `n` the length being reduced, `outer` the product above. Slice
`s = o·inner + i` keeps its `j`-th element at `o·inner·n + j·inner + i` — the same arithmetic
`JgsMatrix.SlicesAlong` has always cut by, now walked instead of copied. Two layouts fall out:

- **inner = 1** (reducing the first dimension): every slice is a contiguous run. Each is folded
  where it lies, one slice per output, whole slices grouped into blocks for threading.
- **inner > 1** (any other dimension): the slices interleave. The kernel walks the fold dimension
  once carrying one accumulator per output, and each step reads a contiguous row — the same
  per-output fold order as the boxed loop, which is what lets the exact folds (`sum`, `prod`,
  `mean`'s numerator, `diff`) ride `TensorPrimitives` at the same time: an elementwise add is one
  IEEE operation whatever lane it sits in, so vectorizing across *outputs* reorders nothing.

### The kernels

`src/JGraph.Numerics/ReduceKernels.cs` implements, in JGraph.Numerics with no knowledge of
`JgsValue`: `Sum`, `Product`, `Mean`, `RootMeanSquare`, `Variance` (both weights, optionally
rooted for `std`), `Any`, `All`, `Norm` (vecnorm's fold, through `Math.Pow` both times because
`Math.Pow(x, 2)` is not `x·x` — M93), `Extreme` (value and first-win position, the boxed scan
verbatim), `CumulativeSum`, `CumulativeProduct`, `CumulativeExtreme`, and `Differences`. Each is
the boxed per-slice fold exactly — same seed, same order, same NaN treatment, and the deliberate
oddities preserved on purpose:

1. The boxed sum folds from `0.0`, so `sum([−0])` is `+0`; the kernels seed the same and lose the
   same sign. The running folds seed from the first element instead, so `cummax` keeps a leading
   `−0` — both are pinned in tests.
2. The include-NaN extreme scan starts comparing at the *second* element, so a NaN in first
   position never triggers the early stop — `max([NaN 3 7], [], 'includenan')` is 7, not NaN.
   That is the boxed `ExtremeOf`'s own shape and the kernel replicates it, quirk and all.
3. Under omit-NaN a scalar reduction *deletes* NaN (a mean's denominator shrinks) while a running
   one *substitutes the identity* (its answer must keep its length); an all-NaN slice answers the
   identity outright — the constant, not whatever `0/0` computes to, because `double.NaN` and a
   computed NaN need not share bits.

### The wiring

`src/JGraph.Scripting/Jgs/PackedReduceOps.cs` sits at three hooks: the top of `WrapColumnwise`'s
`Single` (after the words and slots are parsed — the wrapper keeps sole ownership of argument
grammar) and the top of `WrapExtreme`'s `ReduceAlong` and `ReduceAll`. The fast path takes a call
when packing is on, the subject is a non-empty packed array of numeric class double (sized integer
classes keep their boxed saturation rules), and the extras are understood: the weight slot of
`std`/`var` accepts absent/`[]`/0/1 and refuses a weight vector to the boxed path, `vecnorm`'s p
slot a number, everything else nothing. `vecdim` walks one dimension at a time exactly as the boxed
loop does, and abandons to the boxed path if a pass collapses the value to a scalar before the list
runs out, rather than mimic the `Defer` edge cases. `median`, `mode` and `sort` never match — their
per-slice work is not a fold (sort is M95's). Results are minted as the boxed assembly mints them:
a lone value is a scalar (a Bool one for `any`/`all`), an empty join an empty array, everything
else a packed array reshaped to `ShapeAlong`'s dimensions — so a fallthrough and a fast answer are
indistinguishable, error messages included. There is **no threshold**: every kernel is Tier E, so
the fast path is taken whenever it applies.

### Threading

`ParallelKernels` gains `ForBlocks(blocks, parallel, body)` — the shape for work that does not cut
into equal element grains — and `ReductionThreshold` (4M elements: a reduction reads everything and
writes almost nothing, so it is even more bandwidth-shaped than a copy). Every output is folded
whole by one thread; block boundaries are a function of the shape alone; nothing is combined across
threads — so the answer is bit-identical at one thread and sixteen, and the DOP suite checks it.
Panel bands are at least 512 rows wide even when `GrainElements / n` says less, because each fold
step then reads a 4 KB contiguous run the prefetchers can stream: the band width the grain
arithmetic alone chose (13 rows, a 64 KB stride between touches) measured at *half* the throughput
on the 8000×5000 row-max. The two folds that cannot thread stay serial: a cumulative sweep down a
single slice (one dependency chain), and the include-NaN extreme of one slice (an early stop with
an order).

## Consequences

- The reduction wrapper's boxed road still exists, unchanged, and still owns everything the kernels
  refuse: weight vectors, `'native'`, `median`/`mode`/`sort`, non-double classes, empties, the
  `'all'` form of the running reductions, and every error message.
- `JgsMatrix.ShapeAlong` went from private to internal so the ops layer shapes results by the same
  arithmetic the wrapper does, rather than a copy of it.
- The `d03_cumsum_20M` row does **not** reach its gate, and honestly cannot from inside this
  milestone: the row times `cumsum(x(1:2e7))`, and the reduction is no longer the cost —
  `x(1:2e7)` is. The range index materializes 160 MB of positions, converts them to an `int[]`
  pick list element by element, and gathers — 0.09–0.13 s before `cumsum` runs at all. That is
  indexing machinery, not reduction machinery; it is recorded here as the headline cost of the
  row and left for the indexing-side milestone.

## Measured

i7-11700F, Release, alternating A/B against the M93+race-fix baseline (`e3e746b`) in one quiet
session — d03 two runs each (spreads shown), d06 four runs each (medians; the machine drifted
mid-session and one run of each side is visibly inflated).

| row | e3e746b | M94 | change | MATLAB |
|---|---|---|---|---|
| d03_reductions | 0.430–0.438 | 0.171–0.174 | 2.5× | 0.067 |
| d03_cumsum_20M | 0.339–0.363 | 0.143–0.152 | 2.4× (gate ≤0.06 **MISSED** — see above) | 0.040 |
| d03_dimreduce | 0.527–0.573 | 0.021–0.023 | 25× (gate ≤0.040 **MET**; ~110× from M92) | 0.020 |
| d03 total | 26.1–26.8 | 24.93 | | 4.95 |
| d06_generate_2048 | 0.278 med | 0.208 med | 1.3× (≈2.1× MATLAB — M93's 2× line, now borderline) | 0.100 |
| d06 total | 7.0–7.5 | 5.4–6.3 | | 6.12 |

The dimreduce row now *ties MATLAB* (0.022 vs 0.020): `sum(A, 1)` ≈ 8 ms threaded over column
blocks, `max(A, [], 2)` ≈ 10 ms threaded over panel bands (37 ms before the 512-row band floor —
the prefetch note above). The d06 generate row's three `min`/`max` calls fell from 45 ms of
`img(:)` copies to a few ms of in-place scans; what remains of the row is the generating
expression and first-touch page faults, and against MATLAB's 0.100 s it sits at roughly 2.1× —
the gate M93 missed at 2.26× is now at the line, not under it. `cumsum` alone (the fold, once the
slice exists) is ~0.06 s; the rest of its row is the range index. Every d03 and d06 checksum is
identical to the baseline's, which matched MATLAB at `%.10g`.

## Testing

- `ReduceKernelsM94Tests` (17 xunit tests, most theory-multiplied): every kernel against an
  independent slice-and-fold reference, bit for bit, over six layouts × three NaN densities ×
  every flag combination; DOP invariance at 1 vs 16 threads over both layouts at sizes that
  certainly split; the pinned oddities (−0 seeds, the spared first-position NaN, all-NaN slices,
  first-win ties in both layouts); `ForBlocks` order and exception contracts.
- `MatlabPackedReduceM94Tests` (12 tests): every script runs twice — packing forced on and off —
  and the printed output must be byte-identical at 17 significant digits, reciprocals included so
  a flipped zero sign shows. The scripts sweep the wrapped names across dimensions, words, weight
  and p slots, `'all'`, vecdim, both extreme outputs with `'linear'`, N-D arrays, orientation,
  the calls the fast path must refuse (identical errors), and a hand-computed threaded case above
  the reduction threshold.
- The full suite runs in all four lanes (`JGRAPH_LINALG` × `JGRAPH_JGS_PACKED`), and the packed
  parity corpus (M22) covers the reductions incidentally throughout.
