# ADR 0370 — The complex flag's and the numeric leftovers after ADRs 0364 and 0365, and a real number times a complex one

## Status

Accepted, 2026-11-14. Open items 330 (all but complex integers) and 333, and the `Inf*1i` row of
325 (chips wave 6, lane 6C).

## Context

Every expectation below was measured in R2025b 25.2 headless (`-noFigureWindows -batch`), with
probes `probe1` to `probe8` in lane 6C's scratch (about 1,400 lines), one line per case in both
engines and diffed.

- **Unary plus** was dropped by the parser, so `+complex(1, 0)` kept the flag, `+'a'` stayed a char
  and `+true` a logical. R2025b's `+x` is `uplus`: a double for a char or a logical, the flag
  dropped, the class of a number kept (`+int8(3)`, `+single(3)`), a signed zero kept
  (`1 ./ (+(-0))` is `-Inf`), and a string, cell, struct or function handle refused as
  `MATLAB:math:mustBeNumericCharOrLogical`.
- **`sort`, `unique` and `cell2mat`** dropped the flag; R2025b keeps it, and orders a flagged array
  as the complex array it is: by magnitude, then by angle, NaN last
  (`sort(complex([-3 1 -1 2], 0))` is `[1 -1 2 -3]`, `unique` the same; `'stable'` keeps the order
  of first appearance; `-0` and `0` are one value). Beyond the flag, `sort` of a flagged matrix
  answered a row, and `cell2mat` of any cell holding a flagged piece lost its shape and class and
  joined pieces R2025b refuses to join (`{x1, [2 3]; [4 5], 6}` is `MATLAB:catenate:dimensionMismatch`).
- **`x .^ 1`** of a flagged array keeps the flag in R2025b; a flagged scalar, any other exponent, and
  an exponent that is a single, a logical, a flagged complex or a vector drop it. `[1+2i 3] .^ 1`
  answered `1.0000000000000002+2i` here.
- **`logical`** of any complex value, flagged or not, is `MATLAB:nologicalcomplex`.
- **A string part of `complex`** was read as its char codes; R2025b refuses it
  (`MATLAB:complex:realPartInputNonNumericOrNonReal`, `...imagPart...`, and
  `MATLAB:complex:expectsNumeric` for `complex("a")`). A char part is still its codes.
- **`complex(zeros(2)) \ b`**, rechecked now that the flag exists: it answers R2025b's complex
  `[Inf; NaN] + [NaN; NaN]i` under `MATLAB:singularMatrix`, and so do `Z \ eye(2)`, a flagged upper
  triangle and a flagged identity. Nothing needed changing.
- **A vector to a scalar power** (`[1 2 3] ^ 2`, `[1 2 3] ^ 0.5`, a column, a logical, a char, a
  complex vector, a 1-by-0 or 0-by-1 empty) answered elementwise; R2025b's is
  `MATLAB:mpower:notScalarAndSquareMatrix`. The other `^` shape refusals (`2 ^ [1 2 3]`,
  `[1 2 3] ^ [1 2 3]`, two matrices) had R2025b's words and no identifier.
- **An integer class** combined here with a logical or a single, scalar or not; R2025b combines an
  integer only with its own class or a scalar double (a scalar char is its code), under every
  arithmetic operator: `int8(2) + true`, `int8(2) + single(2.5)`, `int8(2) .* true`, `true * int8(2)`
  and `int8([]) + []` are `MATLAB:mixedClasses`, and so were the double arrays and other integer
  classes this engine already refused in its own words. A complex partner, flagged included, is
  `MATLAB:complexInts` first. The matrix operators ask about shapes before either, for any integer
  operand, two of one class included: `*` of two non-scalars is `MATLAB:mtimes:integerNotSupported`,
  `/` by a non-scalar `MATLAB:mrdivide:integerLinearAlgebra`, `\` with a non-scalar on the left
  `MATLAB:mldivide:integerLinearAlgebra` (`int8([1 2; 3 4]) * int8([1 2; 3 4])` and `int8([]) \ 2`
  answered here). The comparisons combine anything. The powers' own refusals are ADR 0365's; a
  complex partner (`int8(2) .^ single(1+2i)`) and a double array (`int8([1 2]) .^ [1 2]`) were not.
- **`gammaln` and `psi`** answered a negative argument (the reflection formulas); R2025b refuses it,
  double and single, `-Inf` included: `MATLAB:gammaln:negativeVal` "Input must be nonnegative.",
  `MATLAB:psi:negativeX` "X must be nonnegative." (`psi(k, x)` by its `x`). A complex argument is
  `MATLAB:gammaln:notFullReal` (a sparse one too) and `MATLAB:psi:notReal`; a negative order
  `MATLAB:psi:validOrder`. `gammaln(Inf)` was NaN (the Lanczos form's `∞ - ∞`), R2025b's is Inf;
  `psi(0)` and `psi(-0)` were the pole's `+Inf`, R2025b's `-Inf` (the limit from the right), and so
  `psi(0, 0)`; `psi(1, 0)` is Inf in both.
- **`reallog(-1)`, `realsqrt(-1)` and `realpow(-1, 0.5)`** said JGraph's sentence with no identifier;
  R2025b's are `MATLAB:reallog:complexResult` "Reallog produced complex result." and its siblings
  for `realsqrt` and `realpow`, and a complex argument is `MATLAB:validators:mustBeReal` "Invalid
  argument at position N. Value must be real."
- **`mat2str` of a complex infinity** wrote `Inf+NaNi`; R2025b writes a non-finite imaginary part as
  a product, `Inf+1i*NaN`, `1-1i*Inf`, which reads back.
- **A complex `inv`** of a nearly singular matrix did not warn, and one holding a NaN answered a real
  Inf under `MATLAB:singularMatrix`. R2025b warns `MATLAB:nearlySingularMatrix` with RCOND
  (`inv([1 0; 0 1e-17i])`, single too), and answers NaN throughout, complex, under
  `MATLAB:illConditionedMatrix` for a NaN.
- **A real number times a complex one** (item 325's `Inf*1i`, NaN+Infi here, 0+Infi in R2025b) was
  the promoted complex product with C99's infinity recovery. Every real factor in
  {Inf, -Inf, NaN, 0, -0, 2} was multiplied with every complex one whose parts are in
  {0, -0, 1, Inf, -Inf, NaN}, in five spellings (`x*z`, `z*x`, `[x x].*z`, `x.*[z z]`,
  `[x x].*[z z]`), 216 cases that agreed across the spellings; 71 differed from the componentwise
  product and 78 from C99's. The rule they follow is below.

## Decision

### Unary plus is a node in the MATLAB dialect (`Parser.ParseUnary`, `Interpreter.UnaryPlus`)

The MATLAB parser keeps `+x` as a `UnaryExpr`; JGS still drops it. `UnaryPlus` refuses a string,
cell, struct or function handle in R2025b's words and answers a number, logical, complex or numeric
array as `-0 + x` through the binary operator, so the class, the signed zero and the flag's loss are
the operator's. A sparse matrix, a time and anything else it does not judge come back as they
went. A class's `uplus` overload is asked first, as for `-x`. The loop compiler does not compile the
node; a loop that uses it runs in the interpreter.

### The flag's verbs (`JgsBuiltins.ComplexFlagVerbs.cs`, `Interpreter.CellContentsJoined`)

`WrapComplexFlagVerbs`, registered after every other wrapper, wraps for MATLAB code only:

- `sort` of a flagged first argument sorts its real plane with `'ComparisonMethod', 'abs'` (which
  for numbers with no imaginary part is the complex order; a script's own comparison is kept, and an
  empty is sorted plainly) and flags the sorted values again (`Reflagged`); the index is the verb's;
- `unique` of a flagged array, sorted, puts the values in the complex order first (`-0` read as `0`)
  and asks the verb for them as they first appear, then reads `ia` (first or `'last'`) and `ic` back
  through the order; `'stable'`, `'rows'` and the rest are the verb's over the real plane, flagged
  again;
- `logical` of a dense value with a complex part, flagged or not, is `MATLAB:nologicalcomplex`;
- `complex` sees its arguments before strings are demoted, and refuses a string part;
- `gammaln` and `psi` refuse a negative, complex or (for `gammaln`) sparse argument and a bad order;
  a non-float argument is handed through untouched, so `MATLAB:math:mustBeFloat` still comes first.

`cell2mat` of a 2-D cell holding a flagged piece (and no cell) is `CellContentsJoined`, a
transcription of R2025b's `cell2mat.m`: one piece is itself, otherwise each column of pieces is
joined by the bracket top to bottom and then the columns side by side (rows first when there are
fewer rows than columns), so the bracket keeps the flag (ADR 0364's `JoinKeepsComplexFlag`) and
refuses what R2025b refuses. It sits in `RegisterBracketVerbs`, which holds the interpreter.

`x .^ 1` of a flagged array with a plain double `1` is `FlaggedToFirstPower` (in `ApplyBinaryCore`'s
MATLAB block). `ComplexPower` answers `z ^ 1` as `z`, in both dialects.

### A vector to a scalar power is refused (`ApplyBinaryCore`)

A non-square array (not 0-by-0) raised by `^` to a scalar goes to `MatrixPower`, which refuses it as
`MATLAB:mpower:notScalarAndSquareMatrix` (or `MATLAB:mpower:inputsMustBe2D`); the `s ^ A` and
two-matrix refusals now carry their identifiers.

### An integer pair is refused as R2025b refuses it (`JgsBuiltins.IntegerCombine.cs`)

`IntegerPairRefusal`, asked by `ApplyBinary` in the MATLAB dialect for every arithmetic operator,
before the size check (R2025b names the classes before the shapes): for `*`, `/` and `\` the shape
refusals of any integer operand, then `MATLAB:complexInts` for a complex partner, then
`MATLAB:mixedClasses` for a logical, a single, another integer class or a non-scalar double. For
`^` and `.^` it is asked after the mpower integer refusal and before ADR 0365's `PowerMixesClasses`,
and answers only a complex partner and a double array. Sparse, text, time and categorical operands
are not judged. `JgsNumericClasses.Combine` and its JGS sentences are unchanged.

### gammaln(Inf), psi(0), the real family and mat2str

- `SpecialFunctions.LogGamma(+Inf)` is `+Inf` (both dialects: ln Γ grows without bound).
- `psi`'s digamma at `±0` is `-Inf` in MATLAB code; JGS keeps `+Inf`.
- `realsqrt`, `reallog` and `realpow` refuse a complex argument and a complex answer under R2025b's
  identifiers and words in MATLAB code (`RefuseComplexArguments`, `ComplexResult`); JGS keeps its
  sentences.
- `mat2str` writes a non-finite imaginary part as `+1i*Inf`, `-1i*Inf`, `+1i*NaN`, in both dialects
  (the old text read back in neither).

### A complex inv warns (`RegisterLinearAlgebraBuiltins`)

A complex matrix holding a NaN inverts to complex NaN under `MATLAB:illConditionedMatrix`; an
invertible one warns `MATLAB:nearlySingularMatrix` when `ComplexSquareSystems.ReciprocalCondition`
(ADR 0365's `zgecon` estimate) is below eps, or single's eps for a single. The inverse itself is
computed as before.

### A real number times a complex one (`JgsBuiltins.MultiplyMatlab`)

In the MATLAB dialect the elementwise `*` hands complex pairs to `MultiplyMatlab`: a factor with no
imaginary part is a real number x, and for a complex a+bi with b ≠ 0:

- a = ±0: the imaginary part is x·b and the real part x·a, or +0 when x·b is not finite
  (`Inf*1i` is `0+Infi`, `NaN*1i` is `0+NaNi`, `2*complex(-0, Inf)` is `0+Infi`);
- otherwise each part is multiplied, and a NaN in either makes both NaN (`2*complex(1, NaN)`), except
  that a signed zero times a number with both parts infinite leaves one part zero by the sign of the
  three factors' product (`0*complex(Inf, Inf)` is `0+NaNi`, `-0*complex(Inf, Inf)` a real NaN).

Two real factors multiply as reals; two complex ones are `MultiplyC99`, as before. The rule
reproduces all 216 measured cases. JGS keeps `MultiplyC99` throughout. The matrix product of
non-scalars is not this path (see Still open).

## Consequences

- New parity fixture `oi_numeric_leftovers2` (885 rows from R2025b 25.2, every row agreeing, packed
  and boxed), with a helper `p6c_chk` that prints whether a value is real, so the flag shows.
- `MatlabM81ComplexDomainTests.TheRealOnlyFamilyStillRefuses` reads R2025b's words.
- A MATLAB script that raised a vector with `^`, mixed an integer with a logical or a single, or
  multiplied two integer matrices with `*`, `/` or `\` now stops with R2025b's error where it
  answered; one that took `gammaln` or `psi` of a negative number stops too.
- `+x` in MATLAB code is evaluated: a char becomes its codes and a logical a double, as in R2025b.

## Divergences

None opened.

## Still open

- **Complex integers** (item 330's first part): `complex(int8(1), 0)` is a complex double here, a
  new element kind (ADR 0364's Still open).
- **`++x`** is lexed as the increment operator in the MATLAB dialect too (R2025b's is `+(+x)`).
  `uplus("a")` (the function) still reads the string's char code: its argument is demoted first.
- **`x .^ ones(1, 1)`** keeps the flag here (a 1-by-1 array reads as the scalar 1); R2025b drops it.
- **`cell2mat` of a numeric cell with no flagged piece** is still the old flattening road in the
  MATLAB dialect: `cell2mat({[1 2]; [3 4]})` answers `[1 2 3 4]`, R2025b `[1 2; 3 4]`.
  `CellContentsJoined` is R2025b's algorithm and could take every numeric cell; not done here
  (found while measuring, outside the item).
- **`unique(x, 'rows')` and `sortrows`** of a flagged array with negative entries order rows by the
  real order.
- **A flagged Hermitian system** (`complex([2 1; 1 3]) \ b`) is solved by LU here and rounds in the
  last bit differently from R2025b (which takes Cholesky).
- **`b.' / zeros(2)`** answers `[Inf NaN]` here and `[NaN Inf]` in R2025b, real or flagged (the
  singular `mrdivide` order of ADR 0355's road); `inv(complex(zeros(2)))` is `[Inf 0; 0 Inf]` in
  R2025b and Inf throughout here.
- **The comparisons and logical operators with a complex integer**: `<` and its kin are
  `MATLAB:relop:complexIntegers`, `&`, `|` and `xor` `MATLAB:math:mustBeReal`, `~complex(...)`
  `MATLAB:Not:operandsNotReal`; `int8(2) & true(0)` is a logical 0-by-0 in R2025b and a double
  1-by-0 here.
- **`max`, `min`, `rem`, `mod`, `bitand`, `idivide` and `:`** with mixed classes have their own
  identifiers in R2025b (`MATLAB:mixedClasses` for the first four; `MATLAB:Bitand:mixedClasses`,
  `MATLAB:idivide:mixedClasses`, `MATLAB:colon:mixedNonDoubleOperands`); here they answer or refuse
  in their own words. An integer with a sparse matrix is `MATLAB:sparseInteger`.
- **Complex times complex and division with infinite parts**: `(Inf+1i)*1i` is `-1+Infi` in R2025b
  (`NaN+Infi` here), `Inf/1i` is `0-Infi` (`NaN-Infi`), and `[Inf 1] * [1i; 1]` (a BLAS product) is
  `NaN+Infi` there and `NaN+NaNi` here.
- `psi(1, Inf)` is Inf in R2025b and 0 here (trigamma tends to 0; R2025b's answer looks wrong);
  `psi(k, x)` with a vector `k` is `MATLAB:psi:kScalar`; `psi(1)` differs from R2025b's in the 14th
  digit.

Closed since: ADR 0377 (open item 341) makes `++x` two signs in the MATLAB dialect and refuses
`uplus("a")`, keeps the flag of `x .^ 1` only for a written constant (so `x .^ ones(1, 1)` drops it),
joins every plain numeric cell by `CellContentsJoined`, orders `unique(x, 'rows')` and `sortrows` of a
flagged array in the complex order, answers `b.' / zeros(2)` and `inv(complex(zeros(2)))` as R2025b
does, forms complex products and quotients with infinite parts by R2025b's rule (and the matrix
product with an Inf), makes `0.^1i` NaN and whole complex powers square-and-multiply, reduces `any` of
a complex matrix per column, refuses a vector `psi` order, and fixes `psi(1)`'s digits. `psi(1, Inf)`
stays 0 as a divergence there; the Hermitian Cholesky, the `[1 2 3; 4 5 6; 7 8 9]` pivot, complex
integers, the complex-integer comparisons and the mixed-class verbs stay open.
