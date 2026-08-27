# 0095 — A sort keeps the order it was given

Date: 2026-08-26 · Status: accepted (M95; the sort rewrite of plan item B4)

## Context

`sort` was the slowest thing left in the engine. The reduction wrapper's boxed road — flatten the
array, copy each slice out, box one `JgsValue` per element, call the builtin, join the pieces —
cost a sort every bit as much as it cost a fold, and M94 had just taken the folds off it. The
head-to-head `d03_sort_20M` row stood at **14.4 s** against MATLAB's 0.283 s, and `d03_intops_10M`,
which is a 10M-element sort with a cumulative sum after it, at **5.9 s** against 0.150 s.

The second output was worse than slow, it was quadratic. `[B, I] = sort(x)` recovered the positions
*after* sorting, by walking the input once for every sorted value looking for the first unused
element that matched it — `O(n²)` comparisons and an `O(n)` scan of a `used` flag array per value.
It had never been measured, because nothing in the suite asks for the second output at size; measured
now, it quadruples on every doubling exactly as that shape predicts — 0.16 s at 20K, 0.62 s at 40K,
4.57 s at 100K, 18.85 s at 200K. A million elements would be some seven minutes and the suite's own
twenty million about two days, which is not a wait, it is a script that never returns.

## Decision

### The rule to match

A fold's answer depends on the order its threads combine things in, which is what ADR 0093 and
ADR 0094 spend their determinism argument on. A sort's does not: its answer is settled by its input
and its tie rule alone, so no schedule can move it. What had to be established instead was exactly
what the tie rule *is*, and MATLAB was asked rather than assumed:

- Values are compared with `<`, so **`-0` and `+0` tie**. They are the one pair of distinct doubles
  a comparison cannot separate, and the sort must therefore leave them in the order they arrived.
- The sort is **stable in both directions**. `sort([5 1 5 1 5], 'descend')` answers positions
  `[1 3 5 2 4]`, not the reverse of the ascending answer.
- NaN is lifted out and put back at the end `MissingPlacement` names — last when ascending, first
  when descending, under `'auto'` — **in the order it arrived**, keeping its own sign and payload.

JGraph's boxed path already did all three (.NET's `double.CompareTo` compares with `<`, `>`, `==`,
so unlike Java's `Double.compare` it does not order `-0` before `+0`), and the probe confirmed the
boxed answers matched MATLAB's on every one of forty-three forms. So the kernels had a rule to
replicate, not a rule to invent.

### The kernel

`src/JGraph.Numerics/SortKernels.cs` sorts along one dimension of column-major storage, over the
same `(inner, n, outer)` decomposition M94 introduced. Slices with `inner > 1` are gathered into a
contiguous run first, sorted, and written back with their stride — sorting through the stride would
put a multiply in every comparison and every swap of an `n log n` loop, to save two passes.

One slice long enough to be worth threading is **split on values, not on positions**: a strided
sample of the slice picks the splitters, one pass counts what falls in each bucket, one pass
scatters the values into them, and then every bucket is sorted on its own thread with **nothing to
merge afterwards** — bucket `k` holds only values that belong before bucket `k + 1`, so the buckets
laid end to end are the answer. The alternative, sorting fixed chunks and merging them pairwise,
measured the same on this machine while reading the whole array five more times; a partition reads
it twice. The scatter walks blocks in index order and gives each block its own cursor into each
bucket, which is what keeps equal values arriving in order — the stability the whole thing rests on.

Three details carry their own weight:

1. **The zeros are repaired, not sorted.** `Span.Sort` is free to swap two values it finds equal,
   and `-0` against `+0` is exactly that case. Both signs answer false to every `<`, so they always
   share a bucket whatever the splitters are; that one bucket has its zero signs read off while it
   is still in arrival order, and written back over the zero run afterwards. The cost is nothing at
   all unless a slice really does hold both signs.
2. **An already-ordered run is recognised rather than sorted again.** A bucket is checked for
   ascending order before it is sorted, which costs one pass and saves the whole sort on input that
   was already in order — and, because the scatter kept arrival order, on input that is all one
   value, which is the case that would otherwise be a degenerate partition.
3. **The positions path re-reads its values.** With `[B, I]` wanted, the kernel scatters each
   value's index alongside it, settles ties back into ascending order after the sort, and then
   builds `B` by reading each value back out of the source through its position. That is what makes
   `A(I)` equal `B` by construction rather than by argument, and it hands NaN payloads and zero
   signs back exactly as they arrived without a second special case.

`'descend'` flips the bucket map and turns each bucket round inside itself, so the descending
answer is built rather than reversed — a reversal would undo the tie order that stability requires.

The bucket cap is written `byte.MaxValue + 1` rather than `256`, because that is what fixes it: the
counting pass casts each bucket index to a `byte`, and raising the cap past a byte does not crash,
it quietly answers a sorted-looking array that is not sorted. This was found by measurement, not by
reading — a tuning sweep at 512 and 1024 buckets changed the `d03_sort` checksum.

### The wiring

`src/JGraph.Scripting/Jgs/PackedSortOps.cs` hooks the top of `WrapColumnwise`'s `Single` and its
`Multi`, after the wrapper has parsed the words and slots, so argument grammar keeps its one home.
The fast path takes a call when packing is on, the subject is a non-empty packed array of numeric
class double, and the options are understood: a direction word, `MissingPlacement` of
auto/first/last, `ComparisonMethod` of auto/real. `'abs'`, a logical or sized-integer array, a
complex array, an empty one, `'all'`, and anything unrecognised all fall through to the boxed road
with their answers and their error messages untouched.

### One MATLAB difference closed on the way

MATLAB orders a **complex** array by magnitude, and settles ties by phase angle, unless
`ComparisonMethod` says otherwise: for a complex array the default `'auto'` *is* `'abs'`. JGraph's
`'auto'` meant real-then-imaginary, so `sort([3+1i, 1-2i, 2])` answered `[-4 1-2i 1+1i 2 3+1i]`
where MATLAB answers `[1+1i 2 1-2i 3+1i -4]`. The magnitude comparison JGraph already had under
`'abs'` reproduced MATLAB's answer exactly, so the fix was to say that `'auto'` means it the moment
there is a complex element to order. This is nothing to do with the packed path — which refuses
complex arrays — but it is the same builtin, and it was found by asking MATLAB what the fast path
had to match.

## Consequences

- The boxed road still owns everything the kernels refuse, and its quadratic second output is still
  there for the forms that reach it: complex, logical and sized-integer arrays asked for `[B, I]`
  still pay `O(n²)`. Those are small in practice and none is in the suite; the fix, if one is ever
  wanted, is to give the boxed path the same index-carrying sort rather than to widen the fast one.
- `d03_intops_10M` was M97's row. Its cost was the sort, not the integer arithmetic, and with the
  sort fixed it now **beats** MATLAB — so what remains for M97 is the rounding-and-clamp fusion it
  was really about, measured against a row that is already ahead rather than 39× behind.
- The `sort` of an empty array still answers `1×0` where MATLAB answers `0×3` for `zeros(0,3)`.
  That is the wrapper's empty-shape rule, not the sort's, and it is untouched here.
- The partition costs one byte per element of scratch on top of the answer, and it is allocated per
  call rather than pooled.

## Measured

Release build, the head-to-head `d03_arrays` script, against the M94 baseline (`19fb495`) built in a
second worktree and run alternately with it so a drift in machine load lands on both sides.

| row | M94 | M95 | MATLAB |
| --- | --- | --- | --- |
| `d03_sort_20M` | 13.83 s | **0.443 s** | 0.283 s |
| `d03_intops_10M` | 5.92 s | **0.130 s** | 0.150 s |
| `d03_total` | 24.70 s | **4.942 s** | 4.952 s |

Every checksum in the script is unchanged from the baseline, and the three the sort decides —
`d03_sort`, `d03_int_exact`, `d03_int_median` — read `2.01609051`, `107374632864886` and
`1073741670` on both engines, which is the `%.17g` byte-identical half of the gate.

**The gate's other half is met on the recorded run and not reliably.** `sort_20M ≤ 0.5 s` was
measured six more times on a quiet box with the baseline not running beside it: 0.401, 0.472, 0.525,
0.473, 0.501, 0.503 — a median of 0.501 against a gate of 0.500. The recorded run is 0.443 and three
of those six are over. The row lands *on* the gate rather than under it, and it is worth being plain
about that rather than quoting the minimum.

What holds it there is not the sort. Timed apart inside one process, the row's two halves are the
range index `y(1:2e7)` at 0.10 s and the sort of the vector it builds at 0.17 s once warm — but
0.29 s the first time, which is the only time the suite ever calls it. So the row is an index, a JIT
and a sort, and only the last of those was M95's to fix. This is the same range-indexing cost ADR
0094 recorded against `cumsum_20M`, still unaddressed and still capping two rows.

The second output, at a size the boxed road cannot reach at all: 20M values-only 0.33 s, values and
positions together 0.51 s, with `isequal(x(i), b)` true on every run.

A row M95 does not touch moved too: `d03_loop_2M` reads 1.71 s on the baseline and 1.23 s here,
across runs whose ranges do not overlap. The likely reason is that a boxed sort of twenty million
elements leaves a large heap behind and the interpreter loop after it pays for the collection — but
that was not tested, and it is recorded here as an observation rather than a claim.

Two rows read as regressions and are not. `d03_elementwise_50M` measured 0.9× — baseline
[0.433 0.364 0.438] against [0.431 0.449 0.406], ranges that overlap, so the difference is the box.
Under sustained load the whole machine drifts: over eight alternating rounds the baseline's own sort
row went from 13.8 s to 22.8 s and its total from 25.5 s to 41.2 s while nothing about it changed.
That is what the alternation is for, and it is why the absolute number above was taken separately
from the ratio.

## Testing

- `SortKernelsM95Tests` (29 tests with the theories multiplied out): every kernel result compared
  against a reference built from a stable library sort over a `<`-only comparison — values bit for
  bit, positions exactly — across both layouts, every direction and missing-placement, and eight
  data shapes chosen to make each part of the rule matter (clean, NaN-speckled, wholly NaN, both
  zeros, all equal, already ordered, ordered backwards, and infinities with heavy duplicates).
  Then the same at one thread and at sixteen over slices past the threading threshold; then the
  rule pinned claim by claim — the two zeros keeping arrival order both ways round, equal values
  reporting ascending positions both ways round, a NaN keeping its own bits, a wholly-missing slice
  left alone, the already-ordered shortcut leaving positions untouched, and a slice of one repeated
  value (the degenerate partition) coming back with its positions in order.
- `MatlabPackedSortM95Tests`: each script runs twice — packing forced on and off — and the printed
  output must be byte-identical at seventeen significant digits with reciprocals alongside, so a
  flipped zero sign cannot hide. The scripts sweep both directions, every dimension including one
  past the end, N-D arrays, the option words, both outputs, the permutation property, the forms the
  fast path must refuse, and the errors it must not answer differently — plus a slice past
  `SliceThreshold`, which is the only way to reach the partition from a script.
- The full suite runs in all four lanes (`JGRAPH_LINALG` × `JGRAPH_JGS_PACKED`).
