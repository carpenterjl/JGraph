# 0102 — A curve is data, and a grid is read the same way in any number of directions

Date: 2026-08-29 · Status: accepted (M101; toolbox-function arc, item 3)

## Context

M100 took `polyfun`'s polynomial half and left its interpolation half named but unbuilt. That half
is nine names — `spline`, `pchip`, `makima`, `ppval`, `mkpp`, `unmkpp`, `interp1q`, `interpft`,
`interpn` — and the ten documented forms of `interp2` and `interp3` that JGraph had never answered:
`interp2(V)`, `interp2(V,k)`, both `___,method` forms, and the same four again for `interp3`, which
until now took only its seven-argument form and only `'linear'`.

Two things were already here and both were smaller than they looked. `Interpolation.cs` had held
`SplineSlopes` and `PchipSlopes` since M39, but only `interp1` could reach them and only to read a
value; there was no way to *hold* a curve. And `interp2` had a grid reader of its own, `interp3` a
different one over `ScalarField`, and `interpn` none at all — three answers to one question.

### Something found on the way in

**`interp1`'s `'cubic'` was answering `'pchip'`, and R2024a's does not.** The mapping was written
in M39 with the comment "MATLAB's own alias since R2020b". That is what the release notes say about
`interp1`'s documentation; it is not what the running MATLAB does. `interp1(1:5, [1 3 2 5 4], 2.3,
'cubic')` answers `2.7945` there and `2.784` for `'pchip'`, and `2.7945` is exactly what `'v5cubic'`
answers and exactly what Keys' cubic convolution at a = −½ computes. The mapping is removed here,
`'cubic'` and `'v5cubic'` both name the convolution, and `'makima'` — refused by name since M39 —
is built.

## Decision

**A piecewise polynomial is a value, and everything else follows from it.** MATLAB's `pp` structure
— `form`, `breaks`, `coefs`, `pieces`, `order`, `dim` — is built once by `PiecewiseStruct` and read
once by `ReadPiecewise`, and every name here is one or the other end of it. `spline(x,y)` returns
one and `spline(x,y,xq)` reads one; `mkpp` and `unmkpp` are the structure's two doors;
`interp1(x,v,method,'pp')` became reachable the moment the structure existed rather than needing
anything of its own. Coefficients are held row-major internally and transposed at the boundary,
because a `pp`'s coefficients are read a row at a time and MATLAB's matrix is column-major: doing
the transposition once, at the edge, is what keeps the evaluation loop from doing it per piece.

**One grid reader, in any number of directions.** `GridSampler` in `JGraph.Numerics` reads a plaid
grid at a point, and `interp2`, `interp3` and `interpn` differ only in two ways: how many
directions they have, and that the first two number theirs the way `meshgrid` does — x across the
columns, y down the rows — where `interpn` numbers them the way the array is indexed. The second
difference is a permutation of the first two arguments, applied before anything else happens. This
is why `interp2` and `interp3` gained four documented forms each without gaining an implementation,
and why `interpn` cost almost nothing on top of them.

**The three local methods are weights and the spline is derivatives.** `'nearest'`, `'linear'` and
`'cubic'` each read a bounded stencil along every direction, so a query costs the product of those
stencils and nothing else. A spline cannot be done that way — its slope at one knot depends on
every sample along that direction — so its slopes are taken once, along each direction and along
each *combination* of directions, and a query is then a tensor Hermite over the cell it lands in.
That costs 2^n arrays of the grid's own size, which is the price of not walking the whole grid once
per query point. It is well defined because a not-a-knot spline's slopes are a linear function of
the samples, so differentiating along one direction and then another gives the same array whichever
order it is done in — and it is exactly why the same trick cannot be played with `pchip` or
`makima`, whose slopes are not.

**`'cubic'` is Keys' cubic convolution at a = −½, everywhere, and its edges are quadratic.** The
kernel reads one sample beyond each end of the data; that sample is invented as `3y₁ − 3y₂ + y₃`,
so that the parabola through the three nearest carries on unbroken. Both were established by
measurement rather than by reading: `interp1(0:4, (0:4).^3, 2.5, 'cubic')` is exactly `15.625` and
`interp1(1:5, [1 3 2 5 4], 1.3, 'cubic')` is exactly `1.915`, and only the quadratic extension gives
the second. Where the samples are unevenly spaced or fewer than three, MATLAB changes the method and
says so; that is replicated, warning and all.

**Cubic convolution has no extrapolation and says so.** Its kernel is written over a cell and there
is no cell outside the samples, so `interp1(x, v, 5, 'cubic', 'extrap')` warns and answers NaN
rather than continuing the end piece. The other three cubics carry theirs on. This is the one place
where writing the convolution as piecewise coefficients would have been *too* capable — it can
extrapolate, and MATLAB deliberately does not let it.

**`interp1q` refuses anything that is not a column, because MATLAB's own does.** `interp1q` is
documented as the quick one that checks nothing, and what falls out of its stacking the sites and
the query points on top of each other to sort them is that a row will not concatenate with a column.
That is replicated rather than repaired — `MATLAB:catenate:dimensionMismatch`, the identifier MATLAB
raises — so a script that works on the real thing works here.

**ADR 0100's identifier rule is applied again.** Fifteen more documented identifiers are raised with
MathWorks' spelling, from `MATLAB:chckxy:NotEnoughPts` to
`MATLAB:griddedInterpolant:InputMixSizeErrId`; 23 of the 24 refusals measured against R2024a match
it exactly. One place departs from the rule's letter deliberately:
`MATLAB:griddedInterpolant:BadInterpTypeErrId` is raised with this repository's message rather than
MathWorks', because MathWorks' names four methods a grid reader here does not take and pointing a
caller at one of those would be worse than saying nothing. The identifier is what a script branches
on, and that is theirs.

### Divergences recorded here

- **A spline's coefficients, and every value read from one, agree with MATLAB to a few units in the
  last place rather than to the bit.** The system for the slopes is the one MATLAB's own `spline`
  writes, but it is closed by the Thomas algorithm where MATLAB hands a sparse tridiagonal matrix to
  `\`, and the two eliminate in a different order. Measured over a script of 184 printed lines
  covering every documented form of the eleven names: 40 lines differ, every one of them within
  1.5 × 10⁻¹⁵ of its own row's largest value, and the median difference is 1.3 × 10⁻¹⁶ — one ulp.
  The same class of difference reaches `pchip` and `makima` through `PieceCoefficients`, `interpft`
  through the transform, and `interp2`/`interpn` through the order the tensor product is summed in.
  Nothing here differs in any digit a script would print.
- **`'makima'` over more than one direction is refused by name rather than answered with a
  different surface.** MATLAB's N-D modified Akima is a tensor Hermite — that much is measured, and
  so are its per-direction slopes: solving for the four cross-derivatives of one cell from samples
  of `interp2(...,'makima')` reproduces the whole cell to 4 × 10⁻¹⁴ using this repository's own
  makima slopes, so only the cross-derivative rule is unknown. It is neither the modified Akima
  slope of the x-slopes taken along y nor the other way round, and neither order of applying the
  1-D construction to the coefficient arrays gives it either; all four were computed and none
  matches. Refusing is M100's rule applied again: answering with a different curve would be wrong
  quietly. `'linear'`, `'nearest'`, `'cubic'` and `'spline'` are all built for two, three and n
  directions.
- **`interp1q` raises the concatenation identifier for every argument that is not a column, where
  MATLAB's choice depends on which of its unchecked steps trips first.** `interp1q([1 2 3], [1 2
  3], [1.5 2.5])` raises `MATLAB:catenate:dimensionMismatch` on both engines; `interp1q([1 2 3], [1
  2 3], 2)` raises it here and `MATLAB:badsubscript` there. Both refuse, and a function documented
  as doing no checking has no defined failure to replicate — only an accidental one.

## What this did not close

- **`polyfun`'s geometry group.** `griddata`, `griddatan`, `delaunayn`, `convhulln`, `dsearchn`,
  `tsearchn` and `boundary` need a triangulation rather than a grid, and are deferred with the
  plan's other multi-milestone arcs. Eleven of `polyfun`'s 34 names remain, and every one of them
  is in that group or beside it.
- **The four methods MATLAB's grid reader accepts and does not document for these names.**
  `griddedInterpolant`'s error message lists `'next'`, `'previous'` and `'pchip'` as well;
  `interp2`, `interp3` and `interpn` document five methods and those are the five here. `'pchip'` in
  particular MATLAB itself warns is one-dimensional only and reverts to `'linear'`.
- **A warning is a plain line here where MATLAB writes a `[Warning: …]` block with a stack.** This
  is not M101's — every warning JGraph raises has read that way since M62 — but three of this
  milestone's behaviours are warnings, so it shows up in a parity diff for the first time.
- **`deconv`'s and `mkpp`'s post-R2021b arguments**, on the same grounds M100 gave: the forms CSV
  this repository measures against is R2021b's, so a form it does not list is neither counted nor
  built.

## Consequences

`interp3` is no longer implemented over `ScalarField`, which means it no longer inherits that type's
one-method-only limit, and it means a bug in the grid reader is one bug rather than three. It also
means `interp3`'s refusals moved: a call matching no form now says so with
`MATLAB:interp3:nargin` where it used to complain about an argument count.

The `pp` structure is the first value in JGraph that is a *curve* rather than a number, an array or
a handle. `ppval` will take one from anywhere — `spline`, `pchip`, `makima`, `mkpp` or
`interp1(…,'pp')` — and M102's `polyeig` and the deferred `griddata` arc will both want the same
shape of thing.

## Measured

Parity against MATLAB R2024a on this machine, one script through both engines and diffed. Note that
`jgraph.exe -batch "run('x.m')"` runs the **JGS** dialect; passing the filename itself as the
statement is what selects MATLAB by extension.

| Script | Lines compared | Lines differing | What differs |
|---|---:|---:|---|
| 11 names, every documented form, 145 cases | 184 | 40 | last-bits rounding only; worst 1.5 × 10⁻¹⁵ of scale, median 1.3 × 10⁻¹⁶ |
| 24 refusals | 24 | 1 | `interp1q` on a non-column, above |

Coverage, each number re-derived by its verifier rather than edited:

| Document | Before | After |
|---|---|---|
| `matlab-toolbox-coverage.md`, names | 186 of 377 | **195 of 377** |
| `matlab-toolbox-coverage.md`, forms | 260 of 1,036 | **260 of 1,036** |
| `matlab-builtin-coverage.md`, all kinds | 947 of 2,024 | **956 of 2,024** |
| `polyfun` folder, names | 14 of 34 | **23 of 34** |
| `polyfun` folder, forms accepted | 19 of 94 | **38 of 94** |

Of M101's 38 documented forms the prober accepts 25, leaves 8 unprobed for want of a sample, and
records 5 as errors. The eight unprobed all name an argument whose documented type is "structure" or
a numbered list — `ppval(pp,xq)`, `unmkpp(pp)`, and the `Xq1,…,Xqn` forms of `interp2`, `interp3`
and `interpn` — and no sample is invented for them, on the grounds M99 set and M100 kept. All five
errors are the prober's own sample: it hands `interp3` a 3-by-3 plane where a volume belongs and
`mkpp` a dimension its coefficient matrix cannot be cut into, and **real MATLAB refuses the same
five calls**, `mkpp` with the identical identifier and message. That was checked rather than
assumed.

`matlab-form-coverage.md` did not move, and is not expected to: none of these eleven names is in the
population it measures.

## Testing

`tests/JGraph.Tests/Scripting/MatlabInterpolationM101Tests.cs`, 41 tests, assertions inside the
scripts so what is pinned is MATLAB's answer and not JGraph's display. Two exist for properties that
tell a right kernel from a plausible wrong one:
`Interp1_CubicIsCubicConvolutionAndNotTheShapePreservingOne` pins that cubic convolution reproduces
a cubic exactly on an even grid, which no shape-preserving rule does, and
`Makima_DoesNotFlattenWhereThreeSamplesHappenToLineUp` pins the one case that separates modified
Akima from plain Akima — the whole of what the modification is for.

Two existing tests changed meaning rather than failing: `interp2`'s and `interp3`'s
"refused by name" tests now assert that only `makima` is refused, because M101 built the rest.

Full suite 6,067 tests, 0 build warnings, five coverage verifiers exit 0.

## Live checks for the user

```matlab
pp = spline([0 1 2 3 4], [0 1 8 27 64])   % form 'pp', 4 pieces, order 4
ppval(pp, 2.5)                            % 15.625 — exact, it is a cubic
pchip([0 1 2 3 4], [0 1 8 27 64], 2.5)    % 15.6405 — shape-preserving, deliberately not
makima([0 1 2 3 4], [0 1 8 27 64], 2.5)   % 15.6358 — a third answer again
interpft([1 2 3 4], 8)                    % 1  1.0858  2  2.5  3  3.9142  4  2.5
interp2([1 2; 3 4])                       % 3-by-3, the midpoints filled in
interp2(magic(5), 2.3, 3.7, 'spline')     % 5.5703
interp3(reshape(1:27,3,3,3), 1.5, 1.5, 1.5)          % 7.5
interpn([1 2], [1 2 3], [1 2 3; 4 5 6], 1.5, 2.5)    % 4
[b, c, L, k, d] = unmkpp(mkpp([0 1 2], [1 2; 3 4]))  % takes a curve apart
```
