# ADR 0377 — ++ is two signs, complex products, quotients and powers with infinite parts, and the numeric leftovers after ADR 0370

## Status

Accepted, 2026-11-15. Open item 341 but its `[1 0 NaN] & 1` row (lane 7C's) and the stress script
outside git (chips wave 7, lane 7D).

## Context

Every expectation below was measured in R2025b 25.2 headless (`-noFigureWindows -batch`), with
probes `probe1` to `probe5` and a product and quotient grid in lane 7D's scratch, one line per case
in both engines and diffed.

- **`++x`** lexed as the increment operator in the MATLAB dialect. R2025b has none: `++a` is
  `+(+a)`, `--a` is `-(-a)`, `a++b` is `a + (+b)`, `a--b` is 5 for 3 and 2, `[a ++b]` and `[a --b]`
  are two elements, `+++a` and `+-+-a` stack, `a^++b` and `a*--b` take the signs, and `a++` alone is
  `MATLAB:m_incomplete_statement`. `a^+b` was a parse error here (the power operand took only `-`
  and `~`).
- **`uplus("a")`** answered 97 (the operator function added 0 to the char codes). R2025b refuses a
  string, a string array and a cell as `MATLAB:math:mustBeNumericCharOrLogical`, and so do
  `uminus("a")`, `-"a"`, `uminus({1})`, `-{1}` and `-struct(...)`, which here refused without an
  identifier.
- **`x .^ 1` of a flagged array** keeps the flag in R2025b only when the 1 is written as a constant:
  `x .^ 1`, `x .^ [1]`, `x .^ (1)`, `x .^ +1`, `x .^ 1.0`, `x .^ -(-1)`, `x .^ (2 - 1)`,
  `x .^ (2 / 2)`, `x .^ (1 ^ 5)` and `power(x, 1)` keep it; `x .^ e` with `e = 1`, `x .^ (e + 0)`,
  `x .^ ones(1, 1)`, `x .^ eye(1)`, `x .^ double(1)`, `power(x, e)` and `f(x, 1)` through an
  anonymous `@(z, p) z .^ p` drop it (R2025b folds the constant when it reads the file). Here any
  scalar double 1 kept it and a 1-by-1 array dropped it.
- **`cell2mat`** of a cell of numbers flattened it into a row in the MATLAB dialect
  (`cell2mat({[1 2]; [3 4]})` was `[1 2 3 4]`); a char, a struct or a string piece answered a JGS
  list; `cell2mat({{1}, {2}})` and `cell2mat([1 2])` refused in JGraph's words. R2025b's cell2mat.m
  joins column by column (row by row when there are fewer rows than columns), refuses a cell
  holding a cell (`MATLAB:cell2mat:UnsupportedCellContent`) and a non-cell
  (`MATLAB:validation:UnableToConvert`), and when the pieces' classes differ refuses a logical
  beside a char (`MATLAB:cell2mat:IncompatibleTypes`) and words a failed join
  `MATLAB:cell2mat:CatFailed`; one class's failed join is the bracket's own refusal.
- **`sortrows` and `unique(..., 'rows')` of a flagged array** ordered rows by the real order (and
  `sortrows` with `'ComparisonMethod'` or a complex matrix refused). R2025b orders them column by
  column in the complex order (magnitude, then angle; NaN last ascending and first descending;
  `-0` is `0`), answers `sortrows` of a flagged array real and `unique` complex, and `sortrows` of a
  complex matrix the same way, complex.
- **Complex products** with an infinite or NaN part: `(Inf+1i)*1i` was `NaN+Infi` (R2025b
  `-1+Infi`). The grid of every pair of complex numbers with parts in {0, -0, 1, Inf, -Inf, NaN, 2}
  (2,205 products, one side possibly flagged) follows one rule, below, but for the sign of a zero
  part in 44 of them. Two finite factors whose product overflows land where R2025b's do
  (`complex(1e200, 1e200)^2` is `0+Infi`).
- **Complex quotients**: `Inf/1i` was `NaN-Infi` (R2025b `0-Infi`); the same grid (2,205 quotients,
  and 1,323 with one real side under `/` and `\`) follows the rule below but for zero signs.
- **The matrix product** `[Inf 1] * [1i; 1]` was `NaN+NaNi` (the BLAS's scaling by alpha); R2025b
  multiplies a real side into each plane of a complex one (`NaN+Infi`; `[1 1] * [complex(0, Inf); 1]`
  is `1+Infi`) and two complex sides as plain complex sums.
- **`0 .^ z`** for a complex z was 0 throughout; R2025b's is 0 when z's real part is positive and its
  imaginary part finite (`0 .^ complex(Inf, 1)` too), else `NaN+NaNi` (`0.^1i`, `0.^(-1+1i)`,
  `0.^complex(1, Inf)`, a NaN part).
- **Whole complex powers** went through `Complex.Pow`'s logarithm: `(1+2i).^2` was
  `-3.0000000000000004+4.000000000000001i`. R2025b's `z.^n` for a whole n (2 to 2^53 measured) is
  square-and-multiply, and `z.^-n` its reciprocal: it agrees to the bit with that over 187 powers of
  eleven bases (overflowing and infinite ones included) and 68 large powers up to 2^53.
- **`any` and `all` of a complex dense matrix** answered one value. R2025b reduces along the
  dimension, and an entry with a NaN in either part is skipped by `any` and counted by `all`
  (`any(complex(NaN, 1))` and `any(complex(1, NaN))` are false).
- **`psi(k, x)` with a vector, matrix or empty `k`** answered; R2025b's is `MATLAB:psi:kScalar`
  "K must be a scalar.". `psi(1)` was `-0.57721566490155274` (R2025b `-0.57721566490153231`, the
  truth `-0.5772156649015329`): the asymptotic series stopped at five terms.
- **`psi(k, Inf)` for k ≥ 1** is `Inf`, `-Inf`, `Inf` in R2025b for k = 1, 2, 3 (single too). The
  polygamma functions ψ⁽ᵏ⁾(x) = (-1)^(k+1) k! Σ 1/(x+n)^(k+1) tend to 0 as x grows, as R2025b's own
  `psi(1, 1e100)` (1e-100) and `psi(2, 1e300)` (0) say. R2025b's answer at Inf is wrong.
- **`b / D` of a singular diagonal D**: `[1 2] / zeros(2)` was `[Inf NaN]`, R2025b `[NaN Inf]` (real
  and flagged, `[1 2; 3 4] / zeros(2)` and `[1 2] / diag([1 0])` alike): R2025b substitutes from the
  last column back, which is the transposed system's back substitution, where `\` takes a diagonal
  as lower (ADR 0355).
- **`inv` of a singular complex diagonal or triangular matrix** was Inf throughout; R2025b's is the
  real road's (ADR 0355): `inv(complex(zeros(2)))` is `[Inf 0; 0 Inf]`, `inv([2i 0; 0 0])` is
  `[-0.5i 0; 0 Inf]`, `inv(complex([1 2; 0 0], 0))` is Inf over its triangle and 0 below.

## Decision

### ++ and -- are two signs in the MATLAB dialect (`Lexer`, `Parser.ParsePowerOperand`)

The MATLAB lexer never makes `PlusPlus` or `MinusMinus`; the parser's unary rules already stack the
signs and the bracket's element break already reads `[a ++b]`. `x++` is then an incomplete
statement as in R2025b. The power operand takes a unary plus in MATLAB (a `UnaryExpr` node, as
ADR 0370's `+x`). JGS keeps `++` and `--`.

### uplus and uminus refuse what +x refuses (`Interpreter.ApplyUplus`, `RefusesUnarySign`)

The MATLAB `uplus` function is the operator's `UnaryPlus` (a char read as its codes first); the
MATLAB `-x` and `uminus` refuse a string, cell, struct or function handle in the same words. JGS keeps
`0 + x` and `0 - x`.

### A written constant keeps the flag (`Interpreter.IsWrittenConstant`)

`FlaggedToFirstPower` is taken for a 1 (a scalar or a 1-by-1 double array) only when the exponent's
syntax is number literals joined by signs, arithmetic operators and one-element brackets. The
operator function `power` has no syntax to read and keeps ADR 0370's reading (see Divergences).

### cell2mat is cell2mat.m for every plain cell (`CellContentsChecked`, `JoinsAsCellContents`, `CellToMatRefusal`)

In MATLAB code `cell2mat` first refuses a non-cell numeric, char or logical argument and a cell
holding a cell in R2025b's words, then hands any 2-D cell whose pieces are numbers, logicals, text,
numeric, string, time or categorical arrays or plain structs to `CellContentsJoined` (ADR 0370),
with cell2mat.m's class checks around it. `CellContentsJoined` hands one piece back as itself at
either level, as `cat` of one array does (a char row went through the bracket's one-element
matrix road and came back a JGS list). Objects, function handles and N-D cells keep their roads.

### sortrows and unique rows in the complex order (`SortRowsOfComplex`, `UniqueOfFlagged`)

`sortrows` of a flagged or complex first argument hands the verb a key matrix: each column as two,
its magnitude and its angle (`atan2` with `-0` read as `0`, NaN as a NaN magnitude), with the columns
named and a cell of directions doubled to match; the verb's stable order is the complex order and
its index is the answer's. `'ComparisonMethod', 'real'` of a flagged array is the verb's real order.
A flagged array's rows come back real, a complex array's complex. `unique(x, 'rows')` of a flagged
array orders the rows so before ADR 0370's first-appearance road, `'stable'` keeping the verb's.

### Complex products (`JgsBuiltins.MultiplyMatlab`)

For two complex factors, in order: a factor with no imaginary part is ADR 0370's real factor (a
`-0` imaginary part joins the sign rule for a zero times a doubly infinite factor); a factor with no
real part, yi, makes the product y·(-b + ai) by the same rule, and two such factors the real
-b·y (NaN for a NaN part); otherwise a NaN part makes both parts NaN; otherwise the naive product,
and where it has a NaN, C99's recovery over the direction vectors of an infinite factor with a part
that comes out zero kept zero, or, for two finite factors that overflowed, the product of the two
scaled to unit size, scaled back with a zero part kept zero. Square-and-multiply over this rule is
what lands `z.^n` on R2025b's overflows.

### Complex quotients (`JgsBuiltins.DivideMatlab`)

A divisor with no imaginary part divides each part, a part that is zero staying zero when the other
is not (`1i/0` is `0+Infi`); a divisor with no real part is that rule over the numerator turned by
-i; anything else is Smith's quotient (.NET's, which every finite answer already agreed with), and
where both parts are NaN with no NaN anywhere, C99's recovery for an infinite numerator over a finite
divisor or a finite numerator over an infinite one. `ApplyBinaryCore`'s `/` hands complex pairs to
it in the MATLAB dialect; JGS keeps .NET's quotient.

### The matrix product with an Inf or NaN (`NonFiniteComplexProduct`)

In MATLAB code, a complex matrix product whose operands hold an Inf or a NaN is summed in managed
code, term by term in order: a real side times each plane of the other, two complex sides as plain
complex products. A finite product keeps `Zgemm`.

### Complex powers (`JgsBuiltins.ComplexPower`)

In MATLAB code: a zero base with a complex exponent is 0 or `NaN+NaNi` by the rule above; a complex
base with a whole exponent 2 ≤ |n| ≤ 2^53 is square-and-multiply over `MultiplyMatlab`, and
`DivideMatlab(1, ·)` of it for a negative n; `z.^-1` is `DivideMatlab(1, z)`. JGS keeps
`Complex.Pow`.

### any and all of a complex matrix (`ComplexTruthPlane`)

The MATLAB `any` and `all` wrappers read a dense complex array as a real one: 1 where an entry is
non-zero, 0 where it is zero, and for an entry with a NaN part 0 for `any` and 1 for `all`; then
reduce as for a real array, dimensions, vectors of dimensions and `'all'` included.

### psi (`ComplexFlagVerbs` psi wrapper, `SpecialFunctions.DigammaSevenTerms`)

`psi(k, x)` refuses an order that is not one number as `MATLAB:psi:kScalar` before its other
checks. MATLAB code's digamma is `DigammaSevenTerms`: the walk to x ≥ 10, its steps summed in order,
then seven terms of the asymptotic series summed smallest power first. It agrees with R2025b to the
bit at 1, 2, 3, 10, 100, 1e-3 and 1e5 and is within a few ulps of the truth elsewhere. JGS and the
gamma fit keep `Digamma`. `psi(k, Inf)` stays 0 for k ≥ 1 (see Divergences).

### b / D runs backwards (`SquareSystems.Solve`, `ComplexSquareSystems.Solve`, `diagonalAsUpper`)

Both square solvers take `diagonalAsUpper`; the MATLAB `/` (real, complex and a real matrix with a
complex right-hand side) passes it, so a diagonal coefficient is back-substituted. `\` is unchanged.

### A singular complex triangle inverts as the real one (`SingularComplexInverse`)

In MATLAB code a singular complex `inv` of a diagonal matrix inverts element by element through
`DivideMatlab` and of a triangular one is Inf over its triangle and 0 elsewhere, under the same
`MATLAB:singularMatrix` warning; any other singular matrix stays Inf throughout.

## Consequences

- New parity fixture `oi341_complex_leftovers` (987 rows from R2025b 25.2, six of them divergences),
  packed and boxed, with a helper `p7d_chk` (p6c_chk with a rule column and a digit count). 700 of
  its rows are the product and quotient grid over parts {0, 1, Inf, -Inf, NaN}, a zero's sign read
  as 0.
- A MATLAB script that wrote `x++` or `x--` as a statement now stops at parse time, as in R2025b;
  one that wrote `a--b` or `++a` gets R2025b's value.
- A MATLAB script that took `cell2mat` of a cell of numbers gets the joined matrix, not a row, and
  one that took `any`/`all` of a complex matrix gets one answer per column.
- A complex matrix product holding an Inf or NaN is a managed triple loop (finite ones keep the
  BLAS).

## Divergences

- **`psi(k, Inf)` is 0 for k ≥ 1** (`psi_1_inf`, `psi_2_inf`, `psi_3_inf`, `psi_1_vec_inf`,
  `psi_s_inf`): the polygamma functions tend to 0 at +Inf, and R2025b's `Inf`, `-Inf` contradict its
  own answers at 1e100 and 1e300. Correctness outranks parity.
- **`power(x, e)` with a flagged array and a 1 held in a variable keeps the flag**
  (`pow_power_fn_var`): the function form sees only values, so it keeps ADR 0370's reading, which is
  R2025b's for the written `power(x, 1)`; the operator `x .^ e` drops it as R2025b does.

## Still open

- **A Hermitian or symmetric positive definite system** is solved by LU here and by Cholesky in
  R2025b (`complex([2 1; 1 3], 0) \ b` is `[0.19999999999999996; 0.60000000000000009]` here and
  `[0.2; 0.6]` there; `hilb(6) \ b` differs in the 9th digit, real or complex). The providers have
  no complex Cholesky (`Zpotrf`, `Zpocon`), and the real symmetric road is ADR 0355's open item; not
  a dispatch change.
- **`[1 2 3; 4 5 6; 7 8 9] * 1i \ b`** (and the real and flagged matrix) is singular here and nearly
  singular in R2025b (RCOND 1.541976e-18): MKL's elimination rounds the last pivot to a tiny number
  where OpenBLAS and the managed kernels find an exact zero (ADR 0355's still-open row).
- **`psi(k, x)` for k ≥ 1** differs from R2025b in the last digits (`psi(1, 0.5)`, the Hurwitz zeta
  sum), and `psi(0.5)`, `psi(1.5)` by a few ulps: R2025b's psi is Amos's algorithm; the fixture
  reads those rows to 12 digits.
- **A signed zero part** of a complex product or quotient with a flagged or pure imaginary factor
  differs in sign in some pairs (44 of the 2,205 products); `mat2str` and every comparison read the
  two zeros alike.
- **`cell2mat` of an N-D cell** keeps the old flattening road; R2025b's cell2mat.m joins along each
  dimension.
- **`unique(z, 'rows')` of a truly complex matrix** fails here ("Failed to compare two elements");
  R2025b orders it as `sortrows` does.
- Found while measuring, outside the item: the bracket joins `['a'; 2]` as a double (R2025b a char,
  so `cell2mat({'a', 1; 2, 3})` is double here), and refuses `[int8(1) int16(2)]` (R2025b's is
  `int8`, the leftmost integer class), so `cell2mat({int8(1), int16(2)})` is `CatFailed` here.
- `power(x, 1)` in an anonymous function, `f(x, 1)` with `f = @(z, p) z .^ p`, drops the flag in
  both engines; a flagged base with a whole exponent keeps the real road (`complex(1.1, 0) .^ 3`
  was not measured against square-and-multiply).
