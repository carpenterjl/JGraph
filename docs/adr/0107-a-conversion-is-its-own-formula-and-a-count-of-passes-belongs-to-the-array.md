# 0107 — A conversion is its own formula, and a count of passes belongs to the array

Date: 2026-08-29 · Milestone: **M106** · Status: accepted

## Context

The everyday-gaps arc (ADR 0100) reaches the last row of `specfun` that was still empty: the four
coordinate conversions `cart2pol` `pol2cart` `cart2sph` `sph2cart`, the elliptic family `ellipke`
`ellipj`, the exponential integral `expint`, the associated Legendre functions `legendre`, the two
rational approximations `rat` `rats`, and the assignment problem `matchpairs`. Eleven names, 22
documented forms, and the whole of what the folder had left.

The plan's table numbered this row M105. That number was taken in the meantime by the char-matrix
milestone (ADR 0106), so this is M106 and the matrix-function leftovers become M107. Nothing else
about the row changed.

Four of the eleven are one line of arithmetic each and were still worth measuring, because what the
formula does not say is what happens to the argument that is not a coordinate. The other seven are
not specified anywhere a reader would look: the documentation gives `ellipke` a tolerance without
saying what it is measured against, gives `expint` no hint that it answers in complex, gives
`legendre` three normalization words without their signs, and gives `matchpairs` no rule for a tie.
Every answer below was measured against MATLAB R2024a before anything was written — six probe
scripts, then a 228-line side-by-side run.

## Decision

**A conversion is its own formula and nothing else.** `cart2pol` is an `atan2` and a `hypot`;
`cart2sph` measures the planar distance once and uses it twice; `sph2cart` is three products of a
sine and a cosine. None of the four holds any arithmetic of its own, because holding some would be a
second place where the shape rule and the rounding could drift from the operators'. That was only
possible after the repair below.

**The height a cylindrical call carries is a passenger, not a coordinate.** `cart2pol(x, y, z)`
hands `z` straight back, so it keeps its own size even when the two coordinates beside it were
expanded into something larger: `cart2pol([1 2], [3 4], 5)` answers a 1-by-2 angle, a 1-by-2 radius,
and the scalar 5. Asking for that third output without handing one over is `MATLAB:unassignedOutputs`
naming the variable, which is what the interpreted original raises when it reaches the end of its
file with `z` never written.

**How many times an iterative routine goes round is a property of the array, not of the element.**
`ellipke`, `ellipj` and `expint` all stop when the largest remaining correction *anywhere in the
array* falls under the tolerance, and until then every element is carried along — a settled one
keeps receiving corrections that are below the tolerance but are not zero. So these three take the
whole array into the engine rather than looping over it here, and `ellipke(m)` of one parameter is
not obliged to answer, to the last bit, what `ellipke` of a vector containing it answers in that
place. The stopping test skips NaN, because MATLAB's `max` does: a NaN parameter neither stops the
recurrence early for the parameters beside it nor keeps it running for ever.

**`ellipj` climbs a ladder and must come back down the same number of rungs it went up.** Recording
one rung too few very nearly works, because the descending step it then skips is a halving and the
amplitude it then starts from is half as large — the two errors cancel exactly while the last rung's
correction is negligible. At the default tolerance it *is* negligible, and every one of the eleven
default-tolerance rows in the parity run agreed to the last bit with the count off by one. At
`ellipj(2, 0.5, 1e-3)` the cancellation stops and the answer drifts in the seventh digit. Only a
loose tolerance shows it, which is the reason the parity script carries one.

**Which expansion `expint` uses is decided by a curve, not by a magnitude.** An eighth-degree
polynomial in the real part is compared against the size of the imaginary part: under the curve the
power series, above it the continued fraction. The curve crosses zero near 2.6 on the real axis,
which is what sends every real argument past that point to the fraction while the whole negative
real axis stays with the series — and the series takes a logarithm, so **`expint` of a negative real
number is complex**, with exactly a half turn of imaginary part. Two corners fall out of that
reading and both are reproduced because both are MATLAB's answer: a NaN argument satisfies neither
comparison, falls between the two branches, and comes back as the **nought** the answer was
initialised to; and the curve must be evaluated seeded with its leading coefficient rather than with
nought, or the extra multiply that a nought seed adds is `0 · ∞` at an infinite argument, which
would put infinity in neither branch too and answer nought where MATLAB answers NaN.

**A real argument's answer is said to be real rather than left to the arithmetic.** MATLAB does the
whole of `expint` in real arithmetic when its argument is real; this build carries complex
throughout, which is exact for a real argument everywhere except at an infinity, where a complex
recurrence produces NaN in the part a real one leaves at nought. So the imaginary part of a real
argument's answer is set to nought before the half turn on the cut is subtracted. That single line
is the difference between `expint(Inf)` being NaN and being NaN + NaN i.

**`legendre` is computed fully normalized whatever normalization was asked for, and scaled at the
end.** The classical functions of a high degree overflow a double long before the normalized ones
lose a digit, so the downward recurrence runs where it is well behaved and the scaling is applied
afterwards — as a running product through each element when the factor alone would overflow, which
is the only reason degree 150 comes back with a finite number in all 151 rows. Near the ends of the
interval the recurrence's seed, (−sin θ)ⁿ, underflows to nothing and there is no scale to start
from; those columns are seeded instead at an estimated order where the function is still
representable, carried down from an arbitrary tiny value, and normalized afterwards by the sum of
squares the recurrence itself produced.

**A continued-fraction term is the nearest whole number, not the floor.** That is why a term can be
negative in the middle of a positive expansion — `rat(2.5)` is `3 + 1/(-2)` and not `2 + 1/(2)` —
and why the sign written in front of a term is the sign of the *remainder it came from* rather than
of the term itself, so an element that rounded to nothing from below is spelled `-0`. The expansion
stops when the convergent it has built is within the tolerance of the number, which is a stronger
test than the remainder looking small.

**`rat` and `rats` answer in text, and could not have before M105.** One output from `rat` is the
fraction written out, one row of characters per element in storage order; `rats` is a whole matrix
written as a column-aligned table, one row of characters per row of the matrix. Both are a stack of
char rows padded to a common width — a value this build only learned to hold one milestone ago.

**`matchpairs` squares its problem off rather than handling "unmatched" apart.** The cost matrix goes
into the top-left of an (m+n)-by-(m+n) block with its transpose in the bottom-right and the price of
leaving a row or a column out where the two blocks meet, so a perfect matching of the square block
*is* a partial matching of the original and there is no second case to write. One rule survives that
reduction and has to be applied afterwards: **a pair that costs exactly what leaving both of its
ends out would cost is left out**, because the two readings are worth the same and this is the one
MATLAB reports.

### A defect found beside the road

- **Every two-argument numeric builtin flattened its matrix into a row and refused to expand.**
  `atan2(ones(2,3), ones(2,3))` came back 1-by-6, `hypot(A, 1)` of a matrix came back a row, and
  `atan2([1;2;3], [10 20])` — which MATLAB answers with a 3-by-2 table — was refused as "arrays of
  different lengths". The pairwise helper behind these compared *lengths* rather than shapes and
  returned packed storage with no shape on it, so the shape was simply dropped. It has been that way
  since the packed fast paths arrived in M92, with no test over it, and it reaches `atan2` `atan2d`
  `hypot` `nthroot` `realpow` `pow2` `gcd` `lcm` `idivide` `beta` `betaln` `gammainc` `gammaincinv`,
  the four Bessel functions and the six bit operations. `idivide` was nearly missed: the index this
  milestone read its callers out of had stopped parsing that file at a nested pattern-matching
  conditional twenty lines above the call, so the call was not in the list. The fix routes it through `JgsBroadcast`, which is
  the engine the elementwise operators and `bsxfun` already share, so the shape rule cannot fork —
  and it is what makes the four coordinate conversions expressible as their own formulas at all,
  because `cart2pol` *is* `atan2` and `hypot`.

### Divergences recorded here

- **A single-precision argument comes back double.** MATLAB's `superiorfloat` carries the class
  through all eleven of these names, so `cart2pol(single(3), single(4))` answers `single` there and
  `double` here. The numbers agree; only the class does not. This is the pairwise engine's rule
  rather than these names', and closing it belongs with the class-carrying work of M97 rather than
  inside this milestone.
- **`pol2cart` and `sph2cart` refuse a complex angle.** MATLAB accepts one, because the cosine and
  sine of a complex number are defined and its `pol2cart(1+2i, 3)` answers a complex point. The
  pairwise engine here is real-valued, so the two conversions that build coordinates from an angle
  refuse rather than substitute. The two that measure an angle from coordinates refuse a complex
  argument in MATLAB as well, and do so here with MATLAB's own identifier.
- **Two arrays that cannot be expanded against each other are refused in this build's words, not
  MATLAB's.** `cart2pol([1 2 3], [1 2])` says "Cannot apply 'cart2pol' to arrays of different
  lengths (2 and 3)" where MATLAB raises `MATLAB:sizeDimensionsMustMatch` and says "Arrays have
  incompatible sizes for this operation." This is the expansion engine's message wherever it is
  reached — the operators say it too — and it predates this milestone; it is recorded here because
  M106 is the first milestone whose own names raise it.

## What this did not close

- **`specfun` is complete at 23 of 23 names, so nothing in the folder is left.** The next rows of
  the arc are the matrix-function leftovers, and after them the three arcs the plan defers: `funfun`
  (quadrature and the ODE suite), `sparfun` (the iterative solvers) and the `polyfun` geometry.
- **The complex readings of `rat`** — an imaginary part dropped when it is small beside the real
  one, the two halves put over a common denominator when it is not, and the stacked text form with
  its marker row — are implemented but are not a documented syntax and are not measured against
  MATLAB beyond the real path.
- **`matchpairs` on a sparse cost matrix** takes the dense road. MATLAB has a second path for a
  sparse cost with a non-positive unmatched price; the answer is the same matching, and only the
  work is different.
- **Four gaps the parity harness walked into**, none of them this milestone's and each spawned as
  its own task: `double` refuses a complex argument, `sprintf` does not accept the `%+` flag,
  a complex *scalar* cannot be indexed at all (`z(1)` and `z(:)` both refuse), and `Inf` and `NaN`
  do not take size arguments (`Inf(2,2)` indexes the scalar instead of building a matrix).

## Consequences

- `JGraph.Numerics` gains five engines — `EllipticFunctions`, `ExponentialIntegral`,
  `LegendreFunctions`, `ContinuedFractions` and `Assignment`. The scripting layer gains two partials
  (`Coordinates`, `SpecfunParts`) and 11 catalogued names, and `JgsBuiltins.Zip` is rewritten onto
  `JgsBroadcast`.
- `specfun` moves from **12 of 23 names to 23 of 23** and its accepted forms from 8 to 30. The
  toolbox count is **253 of 377 names and 400 of 1,036 accepted forms**, and the across-all-kinds
  builtin count is **1,014 of 2,024**.
- **Every one of M106's 22 documented forms is accepted**, with nothing unprobed and nothing
  refused.

## Measured

Six probe scripts and a 228-line side-by-side run against MATLAB R2024a on this machine:
**217 lines identical**, and **11 that differ**, every one of them within **3.4 × 10⁻¹⁴** of its
row's own scale. Nine of the eleven are a single ulp of a library function — .NET's `cos`, `atan2`
and `exp` are not MATLAB's to the last bit. The largest, `expint(3)`, is an absolute difference of
4.4 × 10⁻¹⁶: two ulps at the magnitude of the series' intermediate sum, multiplied by the roughly
130-fold cancellation between that sum and the answer.

Every `matchpairs` row is identical, ties included — including `matchpairs(ones(3,3), 5)`, where six
matchings cost the same, and `matchpairs([-1 -2; -3 -4], 0)`, where two do and MATLAB does not pick
the diagonal. The shortest-augmenting-path search here happens to break those ties the way MATLAB's
does; that is measured agreement on twenty-odd cases and not a promise, since neither engine
documents a tie rule.

## Testing

- `tests/JGraph.Tests/Scripting/MatlabSpecfunM106Tests.cs` — 44 tests, every number read off R2024a;
  the defining property asserted wherever one exists (a conversion undone by its inverse,
  `sn² + cn² = 1`, a matching's total cost checked against an exhaustive search over all 4! × 2⁴
  alternatives, a convergent's own ratio).
- Full suite: 6,290 tests, 0 warnings; the five coverage verifiers exit 0.
- `stess_65.m` (25 checks) passes 25/25 here and 22/25 in MATLAB — the three failures there are the
  items that assert this build's side of the recorded divergences.

## Live checks for the user

```matlab
[th, r] = cart2pol([1;2;3], [10 20])       % a 3-by-2 table — the engine expands now
[th, r, z] = cart2pol([1 2], [3 4], 5)     % z is the scalar 5: a passenger, not a coordinate
expint(-1)                                 % -1.8951 - 3.1416i — the series takes a logarithm
expint(NaN)                                % 0 — NaN satisfies neither branch's test
rat(2.5)                                   % 3 + 1/(-2) — the nearest whole number, not the floor
rats([1 2; 3 4.5])                         % a table of fractions, two rows of characters
matchpairs([2 5; 5 2], 1)                  % empty: each pair costs exactly what leaving out costs
size(hypot(ones(2,3), 2))                  % 2 3 — this was 1 6 before M106
```
