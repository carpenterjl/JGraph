# 0092 — The kernels that were never called

Date: 2026-08-25 · Status: accepted (M92; opens the managed-engine track of ADR 0088's plan)

## Context

`PackedMath` has carried vector kernels since M22: `Unary` over `TensorPrimitives` for the whole
elementwise maths family, `Sum`, `Min`, `Max`, `Dot`, and a chunked, cancellable frame around all of
them. A survey ahead of this milestone found that almost none of them were reached from anywhere in
`src`. `Unary` was called for one operation. `Sum`, `Min`, `Max` and `Dot` were called from nowhere
at all. `PackedOps.Negate` — the wrapper written so that unary minus could reach `Unary` — was dead
code: the interpreter's own `MapNumeric` handled `-x`, and it ran a delegate per element.

So `sin` of fifty million elements ran fifty million delegate calls; so did `exp`, `sqrt`, `abs`,
`floor` and the rest, and each of the three that can leave the reals ran a *second* fifty million
delegate calls first, asking a domain predicate one element at a time whether the flat path was
still allowed. `sum` and `mean` copied the whole array through `ToDoubles` before folding it.
Comparison — the operation behind every mask — was a scalar loop with a branch per element. `nnz`
copied the array before counting it. A masked read built one `int` per match in a `List<int>` that
doubled its way to a hundred megabytes and then handed over a copy of itself.

None of that is a design; it is a seam that was built and then not wired. This milestone wires it,
and wires nothing whose answers would change.

## Decision

1. **Determinism is a property of an operation, and it is written down.**
   `PackedMath.DeterminismOf` classifies each unary operation as `Exact` — the vector kernel and the
   scalar loop agree bit for bit, because negate, abs, sqrt, floor, ceil and round are single
   correctly-rounded IEEE operations however many of them a register holds — or `Approximate`,
   meaning `TensorPrimitives`' transcendental polynomials land within a few ulps of `Math`'s rather
   than on them. `Vectorizes(op, length)` is the one place that decides, and `UnaryTiered` is the
   entry point callers use so that no caller has to know which tier an operation is in.

2. **The approximate tier lands switched off.** `ApproximateThreshold` starts at `int.MaxValue`, so
   every length answers the scalar kernel and a packed array cannot disagree with a boxed one. The
   policy that lowers it — which arrays are large enough to be worth a few ulps, what the ulp bounds
   are, and what the packed/boxed parity corpus is promised — is one decision and belongs in one
   ADR, which is M93's. This milestone builds the switch and leaves it alone.

3. **The scalar path stops going through a delegate.** `UnaryScalar` runs the same
   `System.Math` functions the boxed interpreter calls, with the operation switched *outside* the
   loop, so what is left is a direct call rather than an indirect one. This is what an `Approximate`
   operation runs below the threshold: identical to the boxed answers by construction, because it
   calls the very functions the boxed path calls.

4. **A half-line domain is checked in the same pass as the arithmetic.** `TryUnaryAtLeast` walks
   cache-sized tiles, asking of each whether anything in it is below the bound and then computing
   it, so `sqrt`, `log` and `log10` read their input once instead of twice. The test is "nothing is
   below the bound" and deliberately not "the minimum is at least the bound": a minimum propagates
   NaN, and a tile holding a NaN *and* a negative would have passed — answering NaN for the element
   that should have promoted to a complex number. NaN fails every comparison, so it is admitted on
   its own without hiding anything beside it.

5. **The registration sites name their kernels.** `MathX` and `MathC` take an optional
   `PackedMath.UnaryOp`, and `sin`, `cos`, `tan`, `exp`, `log`, `log10`, `sqrt`, `floor`, `ceil` and
   `abs` supply one. `round` supplies none on purpose: MATLAB rounds away from zero and
   `PackedMath.Round` is the banker's rule, so they are two different functions that happen to share
   a name.

6. **A reduction reads the storage it was given.** `TryPackedSpan` hands a one-argument reduction
   the buffer behind its argument, and `sum`, `mean`, `min`, `max` and `nnz` read it in place. The
   fold has to be the fold the boxed path runs, not merely one with the same answer in exact
   arithmetic — which is why `PackedMath.Sum` remains a left fold in index order, and why `min` and
   `max` may use a vector kernel at all: their answer is one of their inputs, so the order they are
   folded in cannot change it. `dot` of two real packed vectors accumulates directly rather than
   boxing both operands into `Complex[]` to multiply by a zero imaginary part.

7. **Comparison is a select, not a branch.** `Compare` and `CompareScalar` compute a register at a
   time: the comparison instruction leaves all-ones where it holds, and choosing between 1.0 and 0.0
   on that mask is the whole conversion. This is exact — a mask is one of two constants with no
   arithmetic in between — and NaN is handled by the instruction, except for `!=`, which is written
   as the complement of equality rather than "greater or less" so that NaN answers true.
   `scalar op x` is computed as `x` mirrored-`op` `scalar`, so one kernel serves both sides.

8. **What is counted is counted before it is collected.** `CountNonZero` is a vector pass; `nnz` is
   that pass and nothing else. `find` and a mask's index list size their result from it and fill it
   once, rather than growing a list and copying it. A packed array read through a packed logical
   mask of its own length skips the index list altogether: `PackedMath.Compact` copies the elements
   across in the pass that finds them.

9. **`Zip` joins the kernels.** The two-operand elementwise escape hatch (`atan2`, `hypot`, the bit
   family) moves from a loop in the builtins to `PackedMath.Zip`/`ZipScalar`, so it gains the
   chunking, the cancellation poll and the buffer-lifetime discipline every other packed operation
   has — and will gain M93's threading without being touched again.

10. **`PackedOps.Negate` is deleted.** It was the wrapper that made unary minus fast, and unary
    minus never called it. A fast path nobody takes is worse than none: it reads as work already
    done. The interpreter's `MapNumeric` now names `UnaryOp.Negate` directly, which is also where
    the shape and the numeric class are kept.

## Consequences

- No answer changes. That is the milestone's claim, and the tests are written to attack it at the
  places two implementations of the same operation are allowed to drift: signed zero, NaN, infinity,
  and lengths that do not fill a vector register.
- `TryUnaryAtLeast` writes tiles before it discovers a later one is out of domain, so its
  destination must be a buffer the caller can discard. `MapComplexProducing` allocates, tries, and
  disposes on the promoting path — one wasted allocation on a road that is about to box every
  element anyway.
- The elementwise gap to MATLAB is now almost entirely the transcendentals: `sin` and `exp` over
  fifty million elements are 0.18 s and 0.19 s of scalar `Math` calls, against 0.04 s and 0.11 s for
  the vector kernels sitting behind the threshold. M93's tier decision is worth about 0.22 s of the
  `d03_elementwise_50M` row on its own, before any threading. (`sqrt` is the same shape of gap —
  0.20 s scalar against 0.03 s vector — and does not have to wait, because it is exact.)
- Two costs were measured and left standing, because neither is this milestone's to change. A fresh
  four-hundred-megabyte buffer costs about 36 ms of first-touch page faults the first time a kernel
  writes it — more than the 24 ms the write itself takes — so a chain of elementwise operations pays
  for committing memory more than for using it. And the MATLAB dialect copies an array on
  assignment, including when the right-hand side is a temporary nobody else holds; MATLAB itself
  does not. Both are recorded here rather than acted on.

## Measured

The `d03_arrays` script of the head-to-head suite, Release, i7-11700F, this build alternated with
the M91 build in the same session (so the two share a machine state), mean of two rounds:

| row | M91 (943ffcd) | M92 | change | MATLAB |
|---|---|---|---|---|
| `elementwise_50M` | 1.534 s | **0.936 s** | 1.64× | 0.225 s |
| `mask_50M` | 0.608 s | **0.223 s** | 2.73× | 0.068 s |
| `extract` | 0.174 s | **0.121 s** | 1.44× | 0.081 s |
| `reductions` | 0.482 s | 0.456 s | 1.06× | 0.067 s |
| `cumsum_20M` | 0.344 s | 0.288 s | 1.19× | 0.040 s |
| `d03` total | 30.97 s | 29.47 s | 1.05× | 4.95 s |

Against the plan's three gates for this milestone: **`elementwise_50M` ≤ 1.0 s is met** (0.924 s in
the recorded suite run). **"mask and extract at least 3× faster" is missed on both**, at 2.73× and
1.44×, and the reasons are worth writing down rather than rounding up.

- `mask_50M` is now a comparison that writes four hundred megabytes and a count that reads them,
  and little else: `nnz` fell from roughly a third of a second to 15 ms, and the comparison from
  about 0.25 s to 0.14 s. What is left is a logical array stored one double per element where
  MATLAB stores one byte, which is a storage decision and not a kernel one.
- `extract` was only 2.1× off MATLAB before this milestone and is now 1.5×. Its remaining cost is
  not bandwidth — it is `Compact`'s data-dependent branch per element, which no part of this
  milestone vectorizes. A vector compress is the next step there, not more wiring.

`reductions` and `cumsum_20M` move only a little because their large costs are the boxed round trip
through `WrapColumnwise`, which slices a matrix into one fresh value per column before the builtin
underneath ever sees it. That is M94's, and this milestone deliberately only removed the copy
`ToDoubles` made *inside* each slice.

## Testing

- `PackedMathM92Tests` compares the vector and scalar kernels of every `Exact` operation bit for bit
  over an input built from the awkward doubles (both zeros, NaN, both infinities, the midpoints, the
  extremes) and asserts through `BitConverter` so that NaN counts as agreement and the two zeros do
  not. It pins each `Approximate` operation to the scalar kernel at every length, checks the
  comparison kernels against the operators they name at a length that leaves a scalar tail, checks
  `CountNonZero` against the loop, and checks that `TryUnaryAtLeast` declines in the first tile, a
  later one and the last element, admits negative zero, and is not fooled by a NaN sitting beside a
  negative.
- `MatlabPackedKernelsM92Tests` covers the script surface: the promotion to complex that a
  whole-array domain check must still trigger and the NaN that must not hide it, `round`'s rule, the
  sign of zero through unary minus, negation inside a numeric class, the reductions against a
  hand-written fold over a hundred thousand elements, `min`/`max` stepping over NaN where `sum` does
  not, `nnz`, `find`'s shapes and its limited form, and a masked read's values, orientation, logical
  class, empty case and length-mismatch error.
- The full suite runs in all four `JGRAPH_LINALG` × `JGRAPH_JGS_PACKED` lanes, plus the 59-script
  stress sweep and the four coverage verifiers.
