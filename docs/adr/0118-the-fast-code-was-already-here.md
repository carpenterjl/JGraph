# ADR 0118 — The fast code was already here

## Status

Accepted (M116, 2026-08-31).

## Context

The engine-gap analysis of 2026-08-30 measured 153 operations against MATLAB R2024a and found that
22.8 of JGraph's in-script seconds were recoverable at parity. It also found something more useful
than the number: in four of the six causes, **the fast implementation was already in this
repository and the verb did not call it**.

- `Filters.BoxMean` carries running sums and says so in its own doc comment, while the nine `mov*`
  statistics rebuilt each window from scratch — a `List<double>` and a `ToArray()` per output
  sample, then a LINQ summary over it. `smoothdata`'s default window is a tenth of the series
  (ADR 0066), so its default call was quadratic in the length of the data.
- `MeshOperations.Bucket` is the arithmetic bin index, while `histcounts` and `discretize` walked a
  binary search over edges they had just built evenly spread themselves, and `histc` asked every
  bin in turn from the top down.
- `SortKernels` was written in M95 to take `sort` off the boxed road, and one clause in
  `PackedSortOps` — `string.Equals(name, "sort")` — kept `unique`, `sortrows` and the set
  operations on it.
- `cellstr` maps a whole container at once and beats MATLAB, while the text predicates re-entered
  the builtin once per element.

None of this was a wrong decision at the time. Each verb was written to be correct and each was
correct; what none of them had was a reason to look at what the verb next door already had.

## Decision

**Every one of those four verbs now reaches the code that was already here, and none of them
answers anything different.**

### The window is carried, not rebuilt

`JGraph.Numerics.WindowKernels` slides a window whose summary is carried from one point to the
next. The carrying is done by a two-stack queue rather than by a running total, and that choice is
the whole of the correctness argument:

- **Nothing is ever taken back off.** A running total that adds the arriving value and subtracts
  the departing one cannot un-add a NaN or an infinity, its error grows without bound over a long
  series, and a rolling sum of positive numbers can end up negative — the failure pandas and xarray
  both have open issues about. Values are pushed onto a back stack carrying a running fold; when
  the front runs out the back stack is flipped into it carrying *suffix* folds. The window's answer
  is then one combine of two folds each built by adding alone. Every element is folded exactly
  twice however wide the window is.
- **NaN needs no special case.** `'includenan'` pushes it and lets the fold carry it, which works
  precisely because nothing is subtracted; `'omitnan'` pushes the fold's identity in its place and
  leaves it out of the count.
- **The largest and the smallest are monoids too, and their identities are the odd part.** The walk
  this replaced summarised with `Enumerable.Max` and `Enumerable.Min`, which do not treat NaN the
  same way: `Max` skips it and `Min` is swallowed by it. That makes NaN the *identity* of the
  maximum and an *absorbing* element of the minimum, whose identity is therefore positive infinity.
  Both are associative, so both answer what the walk answered rather than what MATLAB would.
  (That `movmax` ignores a NaN its own documentation says it should propagate is a divergence this
  change preserves rather than introduces, and is recorded as one below.)
- **The variance is the one that is not a monoid.** It is carried as a count, a mean and a sum of
  squared deviations and merged by Chan's formula. That merge answers an infinity where a two-pass
  walk answers NaN, so a count of the non-finite values in the window settles that case directly: a
  window of two or more holding one is NaN, which is what the walk gave.
- **The median is not a fold at all.** It is a sorted array the arriving value is slid into and the
  departing one slid out of, which costs a move of the block between the two places rather than a
  sort of the whole window.
- **The mean absolute deviation is measured from a centre that moves**, so it cannot be carried and
  is still walked. It is the only one.

`smoothdata`'s two moving-average methods and `isoutlier`'s moving fences go through the same
kernels, and the sample-points form — which read every reading for every reading, whatever the
window — now uses two pointers when the places rise.

### The bin is found by arithmetic and checked

`Binning.BinFinder` reads the edges once instead of once per value. Where they are evenly spread it
finds the bin by one subtraction and one multiply; because arithmetic on doubles does not land
exactly, the guess is then checked against the edges it claims to sit between and stepped until it
does. The edges having been measured evenly spread to within a quarter of a bin, that step happens
at most twice, and a set that somehow needs more is handed to the binary search — which is also
what an unevenly spread set gets from the start. **The answer is the search's answer whatever the
edges look like; the arithmetic is only ever a shortcut to it.** `histcounts`, `discretize`,
`histcounts2`, `histc`, the histogram charts and `binscatter` all go through it, and the two
private copies of the rule that had grown up in `JgsBuiltins` are gone.

### The order is settled by the bits

`GroupDistinct` — which is `unique`, `uniquetol` and all four set operations — sorts by a
whole-order `ulong` key made from each value's own bits when every key is one plain number. That
key agrees with the comparison it replaces everywhere: −0 before +0, every NaN behind everything.
The library sort it leans on is not stable and does not need to be, because which member of a group
the sort leaves in front changes nothing: membership is decided by comparing neighbouring keys, and
a group is named by its smallest or largest index, taken with `Math.Min` and `Math.Max` rather than
read off the front of the run. `unique` no longer wraps each of its elements in an array of one
first, which for two million elements was two million allocations before anything had been
compared.

`sortrows` says the same lexicographic order as one stable pass per key, taken from the last key to
the first — the same order for the same reason a radix sort is — instead of a comparison delegate
walking the key list afresh at every one of the N log N comparisons a sort makes.

`ismember` read the set once instead of once per candidate. This was the largest single defect the
change uncovered and it was not in the report: `Ismember`'s `Contains` walked every member for
every value, a product where it could be a sum, so asking whether five thousand readings appeared
among a hundred thousand was five hundred million comparisons. It is now a sorted key array and a
binary search, with the keys built so that two are equal exactly when `SameScalar` calls the values
the same — every NaN one key, the two zeros sharing one, which are the two rules
`double.Equals` has and a comparison does not.

### The container is mapped once

The elementwise text wrapper does the string demotion and the exception translation once for the
whole container rather than once per element. Both depend on the arguments beside the text and
those do not change as the map walks; done per element they were a closure, a delegate and an
array each time round, which was most of what a call cost. What reaches the builtin is what reached
it before, element for element.

## Consequences

**Measured, best of two full runs of the fourteen-script suite against the recorded baseline, on
the same machine:**

| | before | after | | vs MATLAB |
| --- | ---: | ---: | ---: | ---: |
| `smoothdata` 100k | 3.334 s | 0.004 s | 834× | 33× ahead |
| `movmax`+`movmin` 10M | 6.876 s | 0.237 s | 29× | 7.2× |
| `movmean` 10M w51 | 1.756 s | 0.117 s | 15× | 9.0× |
| `ismember` in `d11_setops` | 0.335 s | 0.037 s | 9.1× | 1.4× |
| `movstd` 10M w51 | 2.288 s | 0.318 s | 7.2× | 16.7× |
| `movmedian` 2M w21 | 0.730 s | 0.104 s | 7.0× | 5.2× |
| `discretize` 10M | 0.399 s | 0.060 s | 6.7× | 4.6× |
| `sortrows` 500k | 0.248 s | 0.045 s | 5.5× | 2.8× |
| `predicates` 200k | 0.181 s | 0.039 s | 4.6× | 9.8× |
| `histcounts` 10M/256 | 0.544 s | 0.130 s | 4.2× | 5.9× |
| `unique` 2M | 0.976 s | 0.258 s | 3.8× | 3.2× |
| histogram chart build | 0.950 s | 0.273 s | 3.5× | 1.1× |

In-script total over the 153 timed operations: **39.6 s to 22.0 s**, against MATLAB's 49.6 s.
17.6 of the 22.8 seconds the analysis called recoverable are recovered. The histogram chart row was
not a target — it fell out of `Binning.Counts` reading its edges once.

**All 188 checksums the suite prints are identical to the recorded baseline**, across every script,
not only the ones this touched. 6,772 unit tests pass, 69 of 69 stress scripts pass, no warnings.

**What is entitled to move, and did not measurably:** a two-stack fold combines in a different
order than a left-to-right walk, so `movsum`, `movmean`, `movprod`, `movvar` and `movstd` may
differ in the last place — for the two sums in the direction of being more accurate rather than
less, a partial pairwise summation against a straight left fold. `movmax`, `movmin` and `movmedian`
answer the same bits; only the sign of a zero can move, and only when a window holds zeros of both
signs. `WindowKernelsM116Tests` measures every summary against the walk it replaced, over every
endpoint rule, every width, and data holding NaN inside the window and at its edges, both
infinities, both zeros and long runs of equal values — bit for bit where the fold is exact, and to
a relative tolerance where it is not.

**What is still on the table.** The window walk is one thread. MATLAB's is not — `-singleCompThread`
costs it 3.4× on `movmean` — and that is most of what remains of `movstd`'s 16.7×. Threading it
means cutting a slice into chunks, which moves where the queue's flips fall and therefore moves the
last place of a sum; under ADR 0093's determinism discipline that needs grain boundaries pinned to
the shape and an ADR of its own. It is not in this one.

**Two things found while doing this and deliberately left alone.** `ismember` answers a single
logical when its first argument is a cell, because a cell subject matches neither branch of
`Ismember` and falls through to the scalar case — a correctness bug, filed separately rather than
folded into a performance change. And `movmax` ignores a NaN that MATLAB's `'includenan'` default
propagates, which is what `Enumerable.Max` over doubles does and what this repository has always
answered; it is preserved here on purpose, since changing it is a divergence decision and not a
consequence of carrying a window.

No MATLAB-facing behaviour changed, so this adds no divergence.
