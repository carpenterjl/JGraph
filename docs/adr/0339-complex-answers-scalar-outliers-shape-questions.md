# ADR 0339 — A complex scalar is a uniform answer, a complex panel restarts the integral, one reading is a slice, and the shape questions read any float matrix

## Status

Accepted, 2026-11-12. Five small open items from the every-row plan, fixed together: 255, 256, 259 and
276 are refusals of things R2025b answers, and 253 is a test that went stale under ADR 0316.

## Context

All the expectations below were measured in R2025b 25.2 headless (`-noFigureWindows -batch`).

**255: uniform `arrayfun` and complex answers.** `arrayfun(@(a) sqrt(a), [4 -1 9])` was refused with
"Non-scalar in Uniform output, at index 2". `IsUniformScalar` took numbers, logicals, one object, one
character and one categorical element, but not a `JgsType.Complex` answer. R2025b answers the complex
row `[2 0+1i 3]`, with `isreal` false. It does the same for a column, a matrix and an N-D input (each
in the input's shape), for `single`, and for `cellfun` over a cell of any shape. `cellfun`'s uniform
collection (`CollectResults`) refused a complex scalar in the same way.

**253: a misspelt option.** `MatlabCommaSeparatedListTests.ArrayFunRefusesAMisspeltOptionAndNamesTheRealOnes`
expected `arrayfun(@(k) k, [1 2], 'UnifromOutput', false)` to name the real option names. Since ADR
0316, `arrayfun` walks a char argument as data, as R2025b does, so the misspelling is a third input of
13 characters. R2025b refuses it with `MATLAB:arrayfun:UnequalSize`: "... Previous inputs had size 2 in
dimension 2. Input #3 has size 13". JGraph gave the same refusal but said "size 0", because it read
the char row's dimensions with `JgsMatrix.DimsOf`, which gives a bare string value no length.

**256: `integral` with a real midpoint.** `Integrate` picks the real or the complex road from one
probe, at the interval's midpoint. For `integral(@(x) sqrt(x - 1), 0, 2)` the midpoint value is 0, so
the real road was taken. The first panel below 1 then answered complex, and `ToDoubles` refused it.
R2025b answers `0.666666678883704 + 0.666666678883704i`. The same happens with `quadgk`, with
`sqrt(1 - x)` over the same interval, and with `sqrt(x)` over [-1, 3].

**259: one reading.** `isoutlier(7)` was an internal error ("Index was outside the bounds of the
array") in both storages. `Cut` reads the subject's dimensions with `JgsMatrix.DimsOf`, which gives a
bare scalar no dimensions, so there were no slices, and the fence and mask arrays were read past their
end. The other methods answered a 1-by-0 mask with NaN fences, and `rmoutliers` and `filloutliers`
failed the same way. R2025b answers false: one reading is never an outlier of itself. Its fences are
the reading (7, 7, 7) for median, mean, quartiles, percentiles and both moving methods, and NaN, NaN,
7 for grubbs and gesd. Separately, the median fences of an all-NaN slice (`isoutlier(NaN)`,
`isoutlier([NaN NaN])`) took a median of nothing and failed the same way. R2025b answers false with
NaN fences.

**276: the shape questions.** `istriu`, `istril`, `isdiag`, `isbanded`, `bandwidth`, `issymmetric`
and `ishermitian` read their argument with the real `RectOf`. So:
- a complex matrix was refused at its first complex entry;
- an empty matrix was read past its end;
- a sparse matrix was refused;
- `issymmetric` and `ishermitian` were one predicate that skipped the diagonal.

R2025b answers for any double, single or logical matrix, complex, sparse or empty:
- `ishermitian` compares with the conjugate, so `[1i 0; 0 1]` is symmetric but not Hermitian;
- both compare the diagonal exactly, so a NaN anywhere makes them false, the diagonal included;
- both take `'skew'` and `'nonskew'`, and refuse any other word with `MATLAB:issymmetric:inputFlag` or
  `MATLAB:isHermitian:inputFlag`;
- every predicate answers false for an N-D array, and `bandwidth` refuses one with
  `MATLAB:bandwidth:inputMustBe2D`;
- `isbanded` refuses a negative bandwidth with `MATLAB:isbanded:nonNegIntScalar`;
- an integer or char matrix is refused by all seven (`MATLAB:math:mustBeFloatOrLogical` and each
  verb's own `inputType`).

## Decision

### Uniform collection takes a complex scalar

`IsUniformScalar` takes a complex scalar: a `JgsType.Complex` value, or the one-element planar array
`complex(a, 0)` builds (`IsComplexScalar`). `UniformComplex(answers, dims)` gathers the answers into
one planar complex array (`ComplexStorage`, `PreserveComplex` set) when at least one is complex and
every other is a real non-integer number. It answers null otherwise, and the existing roads go on as
before:
- the array is `single` when any answer is;
- one complex answer for a 1-by-1 input is that complex scalar itself.

`arrayfun` shapes the answer like its first input (`JgsMatrix.Like`, as for numbers). `cellfun`'s
`CollectResults` builds it in the cell's rows and columns before its per-element refusal. The compiled
road of ADR 0325 is unchanged: it still hands any call whose answer leaves the reals to the walk, and
the walk now collects it.

### A misspelt option is data, and its size is size's

The `UnequalSize` message reads both inputs' dimensions with `SizeDims` (what `size` answers), so a
char row has its length. The stale test is rewritten to R2025b's refusal, word for word, and to the
three-scalar call `arrayfun(@(k, a, b) k + a + b, 1, 'U', false)`, which R2025b walks to 86.

### A complex panel restarts the integral on the complex road

When the real road's integrand answers a complex value for a panel, `Integrate` stops that pass and
starts again on `IntegrateComponents`, which asks the integrand from the beginning. This covers the
vectorised call and the one-point-at-a-time call. The signal is a private exception,
`IntegrandWentComplex`, raised from the sampler and caught around `Quadrature.Integrate`, so nothing
partial is kept. A compiled panel (ADR 0325) that leaves the reals is walked as before. The walk's
complex answer then restarts the integral, so both roads give the same bytes. An integrand with side
effects runs again for the panels before the complex one; R2025b's single pass does not.

### One reading is a 1-by-1 slice

`isoutlier`, `rmoutliers` and `filloutliers` write a scalar subject as the 1-by-1 array it is
(`WithScalarAsSlice`) before anything cuts it, keeping its numeric class. The general road then
answers it the way it answers any slice of length one, so every method and the `OutlierLocations`
form come out as R2025b's. The median fences of a slice with nothing present are NaN, as the mean's
already were. `Cut`, which other verbs share, is not changed.

### The shape questions read one subject

The seven verbs read their argument once into a `ShapeQuestionSubject`:
- real dense entries, through `RectOf`, the fast road as before;
- complex dense entries, through `ComplexRectOf`, which also takes an empty matrix;
- a sparse matrix's stored entries, never made dense;
- or nothing for an N-D array, which every predicate answers false and `bandwidth` refuses.

`ShapeBandwidth` reads how far the non-zeros reach. `ShapeIsSymmetric(subject, conjugate, skew)`
compares every entry on and above the diagonal with its mirror, exactly: with the conjugate for
`ishermitian`, negated for `'skew'`. `issymmetric` and `ishermitian` take the optional `'skew'` or
`'nonskew'`, with R2025b's refusal of any other word. `isbanded` refuses a negative bandwidth in
R2025b's words. The old real `IsSymmetric(double[,])`, which `eig` and `linsolve` use, is not changed.

## Consequences

- Every row of the five items' probes matches R2025b, except those listed under Divergences and Still
  open.
- The parity fixture `complex_answers_scalar_outliers` was recorded from R2025b 25.2 (75 rows, one
  divergent; green in the packed and boxed lanes, no boxed overlay). The `integral` rows are held to the integral's own promise (`rel=1e-6`, or `abs=4e-6`
  where R2025b's own imaginary part is that far from the true value). JGraph's adaptation is not
  R2025b's, so the two answers agree to the tolerance, not to the bit: JGraph gives
  `0.666666666857557` for the parts of `integral(@(x) sqrt(x - 1), 0, 2)`, and R2025b gives
  `0.666666678883704`.
- `CallbackBodyI2Tests.ArrayfunLeavesEverythingElseToTheWalk` and
  `AnIntegrandThatGoesComplexOnAPanelWalksThatPanel` now record the answers on both roads, with the
  same compiled and walked counts. `MatlabCommaSeparatedListTests` replaces the stale test with
  `ArrayFunReadsAMisspeltOptionAsDataOfTheWrongSize`.
- The touched test classes and the nine related parity fixtures pass, 526 of 526 in the packed lane
  and 526 of 526 in the boxed lane.

## Divergences

- **JGraph's shape questions answer an integer matrix; R2025b refuses one.** `istriu(int8([1 0; 2 3]))`
  is false in JGraph, while R2025b raises `MATLAB:math:mustBeFloatOrLogical`. The other six verbs
  refuse with their own `inputType` identifiers. JGraph answered integers before this ADR, and the
  answer is the true one, so it is kept (fixture row `sh_integer`).

## Still open

- `complex(1, 0)` is a real scalar in JGraph (`isreal` true). R2025b keeps it complex, so
  `arrayfun(@(a) complex(a, 0), [1 2])` is real in JGraph and complex in R2025b. This is the scalar
  representation, not the collection.
- `isoutlier` of a `single` input answers `double` fences, for a vector as for a scalar. R2025b
  answers `single`. Closed by ADR 0355.
- `sqrt(true)` answers 1 in JGraph. R2025b refuses it (`MATLAB:math:mustBeFloat`). Closed by ADR 0355.
- `isoutlier(true)` answers in JGraph. R2025b refuses a logical (`MATLAB:isoutlier:invalidType`). Closed by ADR 0355.
- `integral(..., 'Vectorized', false)` is a JGraph extra. R2025b's `integral` refuses the name. Kept as an extra and recorded as a divergence by ADR 0355.
- A sparse complex matrix cannot be built in JGraph (`sparse needs numbers`), so the shape questions
  over one were not tried.
- A char matrix is refused by the shape questions in JGraph's words, not R2025b's identifiers.
