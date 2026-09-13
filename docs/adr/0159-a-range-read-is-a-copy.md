# ADR 0159 — A range read is a copy

## Status

Accepted. Item 12, first stage, of the head2head_v3 gap-closure plan
(`docs/plans/gap-closure-07-12-plan.md`, rev 6, agreed with Codex): 12a, the affine range read
in the interpreter, and 12e.1, the explicit solvers' interpolant and output sink — the two parts
of the item the plan put ahead of the compiler work because neither touches a compiled loop. The
compiler stages (12b–12d) and the borrowed argument (12e.2) are ADR 0160 and ADR 0161.

## Context

At the item 11 commit (8d6abee) a paren read through a colon range took the longest road the
interpreter has. `x(a:b)` evaluated the range to a packed row of doubles (`EvaluateRange`,
`PackedOps.CreateRange`), turned every element into a validated integer position
(`PackedOps.PicksFromPacked` → `ToIndex`), and gathered through the pick list
(`PackedOps.Gather`). Twenty million elements meant a 160 MB range, an 80 MB pick list and a
160 MB answer for a read whose answer is a contiguous slice of the source. Two subscripts were
worse: `M(k, :)` and `M(:, a:b)` went through `JgsMatrix.BuildValues`, one boxed `JgsValue` per
element through `JgsMatrix.At`, and `TryPackElements` re-packed the boxes afterwards — a row of
two thousand doubles was two thousand allocations. This is what `cumsum(x(1:2e7))`, the
`sig(k, :)` slices of the d02 factorial and the ODE rows' `y(end, 3)` reads paid; the plan lists
it under item 12 because it is interpreter work, not kernel work.

The explicit Runge–Kutta interpolant (`RungeKuttaScheme.Interpolate`) formed each stage's weight
polynomial inside the component loop, once per component per stage, and formed the derivative's
weights too whether or not a slope was asked for; every accepted step's report
(`OdeOutput.AfterStep`) allocated two lists it discarded on return. Neither is the ODE rows' cost
— that is the callback bridge and the interpreter evaluating the right-hand side, 12e.2's subject
— but both were free to fix without touching an answer.

The fixture written first, `m159_slices.m` (77 lines from R2025b, run on the 11b binary before
any code moved: 77 agree), also showed two things about the colon that the item had not set out
to touch, listed under the divergences: R2025b rounds a colon with fractional operands when it
is used directly as an index, where JGraph refuses it, and refuses a classed range whose stop is
outside the class, where JGraph saturates it.

## Decision

**12a — the affine range read.** `Interpreter.AffineIndex.cs`. A subscript slot is evaluated
through `EvaluateSlot`, which recognises three shapes: a lone `:`, a `RangeExpr`, and a scalar.
For a range the three bounds are evaluated here, in the order and under the `end` context
`EvaluateRange` evaluates them, exactly once; `TryAffineRange` then proves the run or hands the
same three values to `RangeFromValues` (the body of `EvaluateRange` after its evaluations, split
out for this) so the general path reads what it always read. The proof is:

- every bound is an unclassed double scalar (`JgsType.Number`, class `Double`, no time tag) — a
  classed bound is stamped and saturates through the range's own class rules, a time bound
  scales to milliseconds, and neither is worth reproducing here;
- the start and the step are finite whole numbers, so every element `start + i·step` is a whole
  number, and the forward and backward halves of `PackedMath.RangeElement` agree on it exactly;
- the count is `RangeCountOf(start, step, stop)`, the colon's own count (the `(1 + 4ε)` rule of
  M132, now one function both roads call), and is at least one;
- the first and the last element are both positions inside the extent under the dialect's base,
  which with a whole step puts every element between them inside it too.

A proved run is a copy: one `Span.CopyTo` when the step is one, a strided loop otherwise (a
negative step walks backwards), into fresh packed storage of the target's packed kind. One
subscript takes `OrientGather`'s rule from the index's shape (a 1-by-count row) without an index
value — the target's orientation wins for a vector, a matrix answers a row; two subscripts copy
column by column into a `rows`-by-`cols` result, and one element by one element is the element,
as `BuildValues` answered it. Anything the proof does not cover — an empty range, a fractional
start or step, a classed or timed bound, a scalar that is not a whole in-range number, a position
outside the extent, a boxed or complex or string target — takes the general path with the values
already evaluated, so it throws the general path's own words (`x(4990:5010)` names 5001, the
first element past the end, because the pick list stops there). The fast path applies only when
`JgsPacking` holds the target; the managed/unpacked lane never reaches it.

`JGRAPH_AFFINE_INDEX=1|0` forces the mode (`JgsAffineIndex.Enabled`), and `JgsAffineIndex.Reads`
counts the copies, so a test can assert which road a script took. Assignment (`x(a:b) = …`), the
three-or-more-subscript path, and the `(k-1)*n + (1:n)` spelling of a slice (a binary expression,
not a `RangeExpr`) are untouched.

**12e.1 — the interpolant and the output sink.** `RungeKuttaScheme.Interpolate` forms the
weights once per call into a stack buffer — the same products in the same order as before — and
the component loop adds `weight·stage` in stage order as it always did, so the value is
bit-identical; the gradient's weights are formed only when a slope is asked for, and the
gradient sum only then. `OdeOutput` keeps its two lists and clears them per step: nothing below
retained either (the result clones each state, the output function gets a fresh array of times
and selected copies of the states).

## Consequences

**Contract.** `AffineIndexM159Tests` runs every script with the copy path forced on and forced
off and asserts byte-identical output, or the same refusal in the same words: whole, stepped,
descending and `end`-relative ranges, one element, a column target, a matrix read linearly, the
two-subscript blocks (row, column, block, stepped, scalar-and-range in both orders, all-all, the
1-by-1 forms), the empties, fractional and classed bounds, positions outside the extent (low,
high, zero, past the end, a fractional row), logical, `uint8`, `int16`, char, char-row, datetime
and complex targets, bounds with side effects (each evaluated once, in order, on both roads),
and a row read inside a loop; the count of copies is asserted where the copy path must fire
(when `JgsPacking` is on — the unpacked lane packs nothing, so both roads are the general gather
there) and where it must refuse. `RungeKuttaInterpolantM159Tests` holds the interpolant as it stood as an
oracle and checks bit equality over the four schemes, with and without a slope, with and without
a component held non-negative, at the ends of the step and inside it; and runs the orbit twice
through the named-times and the refined roads, asserting the same numbers and that no two
stored states share an array.

`m159_slices` (77 lines from R2025b): 27 bits lines over the reads (a read is a copy, so the
bytes are MATLAB's bytes), 23 shape lines, the fractional and classed bounds, nine refusals, and
the explicit solvers' step counts on short runs where the two engines still take the same steps
(584, 957, 977, 21 and 433 rows) with the orbit's energy at `rel=1e-6`. All 77 agree on the 11b
binary, on this build, and on this build with `JGRAPH_AFFINE_INDEX=0`. Two workspace bits
scripts compare the 11b bin against this one: `bits_d03_slices` (23 lines, every read shape
including `-0` and a NaN payload inside a copied run) and `bits_d16_explicit` (22 lines: ode23
and ode45 on Lorenz and the orbit, a named tspan, Refine 1 and 8, a terminal event, a
non-negative component) — all equal.

**Measured** alone against the 11b bin re-run on d03, d12 and d16 the same hour
(`runs\12-before-11b` → `runs\12a-affine-index`, medians of five):

| row | before (s) | after (s) | speed-up | J/M before → after |
| --- | --- | --- | --- | --- |
| `d03_cumsum_20M` (`cumsum(x(1:2e7))`) | 0.165 | 0.092 | 1.79× | 3.77 → 2.07 |
| `d03_reductions` (`std(y(1:1e7))` among three) | 0.144 | 0.099 | 1.45× | 1.95 → 1.36 |
| `d03_sort_20M` (`sort(y(1:2e7))`) | 0.542 | 0.464 | 1.17× | 1.45 → 1.20 |
| `d03_total` | 3.116 | 2.766 | 1.13× | 0.33 → 0.29 |
| `d03_loop_2M` | 0.052 | 0.054 | — | 3.71 → 4.00 |
| `d12_charmatrix` | 0.022 | 0.022 | — | 5.25 → 5.50 |
| `d16_ode23_lorenz200` | 0.115 | 0.106 | — | 2.95 → 2.84 |
| `d16_ode45_orbit_tight` | 0.144 | 0.146 | — | 4.12 → 3.95 |
| `d16_total` | 3.913 | 3.836 | — | 1.30 → 1.26 |

| `probe_12_slices`, cold / warm (s) | before | after | speed-up | J/M after |
| --- | --- | --- | --- | --- |
| `slice_full_20M` — `x(1:N)` | 0.127 / 0.121 | 0.054 / 0.060 | 2.4× / 2.0× | 2.06 / 1.81 |
| `slice_step2_10M` — `x(1:2:end)` | 0.071 / 0.073 | 0.038 / 0.037 | 1.9× / 2.0× | 1.57 / 1.75 |
| `slice_desc_20M` — `x(end:-1:1)` | 0.126 / 0.127 | 0.063 / 0.067 | 2.0× / 1.9× | 1.79 / 1.70 |
| `slice_rows_2000` — 2,000 × `M(k, :)` | 0.211 / 0.079 | 0.153 / 0.059 | 1.4× / 1.3× | 9.76 / 4.14 |
| `slice_colblock_2000x1000` — `M(:, a:b)` | 0.098 / 0.084 | 0.0074 / 0.0074 | 13.2× / 11.3× | 2.17 / 2.00 |
| `slice_subblock_1000x1000` — `M(a:b, c:d)` | 0.045 / 0.044 | 0.0036 / 0.0035 | 12.6× / 12.7× | 1.68 / 1.94 |

The probe's process allocated 3,690 MB before and 361 MB after over its twenty-four reads, and its
peak working set fell from 1.79 to 1.08 GB. The vector reads are now a copy and a second copy
(the answer is fresh storage, as in MATLAB) at about twice MATLAB's time — the remaining factor is
the interpreter around the read, not the read. The 2,000 row reads are the strided loop plus the
statement overhead of a `for` iteration each, which 12b addresses. The three d03 rows that moved
are the ones whose expression holds a range read; every other d03 and d12 row is within its
range of the before-run, and so are the two d16 rows this stage's solver changes reach
(`d16_ode23_lorenz200` 0.074–0.118 → 0.060–0.119, `d16_ode45_orbit_tight` 0.140–0.189 →
0.132–0.189; their probes 0.239 / 0.053 → 0.243 / 0.054 and 0.162 / 0.139 → 0.162 / 0.128) —
12e.1 was never the rows' cost, and the plan said so. The stiff rows `d16_ode23t_vdp1000x20` and
`d16_ode23tb_vdp1000x20` read 0.105 → 0.144 and 0.114 → 0.170 at the median with overlapping
ranges (0.096–0.167 → 0.119–0.164, 0.109–0.195 → 0.130–0.198) on code this stage does not
touch; the counterbalanced spread of a run. Rows and probes are in `runs\12a-affine-index`.

**What did not change.** The general gather, `OrientGather`'s rule and `PicksFromPacked` are as
they were; assignment through a range still materialises it; the `(k-1)*n + (1:n)` spelling
still materialises its range; the implicit solvers' interpolants are untouched. The ODE rows'
cost is the bridge and the right-hand side, which 12e.2 and 12d address.

## Divergences

Both were found by `m159_slices` on the binary before this item and are kept by it; a chip is
filed to close them together.

- A colon with fractional operands used directly as an index: R2025b rounds each element with the
  warning `Integer operands are required for colon operator when used as index`, so
  `x(2:0.5:3)` reads elements 2, 3 and 3, where JGraph refuses the fractional position
  (`m159_slices`, `refused_fractional_step`, `div=ADR0159`). The copy path is not involved: a
  fractional step fails the proof and the general path refuses as it always did.
- A classed range whose stop is outside the class: R2025b refuses `uint8(254):258` with
  `MATLAB:colon:OutOfRange`, where JGraph saturates it to `[254 255 255 255 255]` and reads five
  elements (`m159_slices`, `refused_uint8_saturating`, `div=ADR0159`). The copy path refuses a
  classed bound and leaves the range to the general path, which is where the saturation lives.
