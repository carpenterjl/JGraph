# ADR 0365 — A singular complex system warns and answers, a matrix to a fractional power is mpower.m's, and the floating leftovers of ADR 0355 are closed

## Status

Accepted, 2026-11-14. Open item 321, its correctness rows (chips wave 5, lane 5E). The item's RCOND
cost on the managed backend is not part of this ADR (see Still open).

## Context

ADR 0355 left nine numeric rows open, each measured again in R2025b 25.2 (probes `probe1` to
`probe5`, lane 5E's scratch):

- **A singular complex `\`** was refused ("The matrix is singular to working precision.", no
  identifier). R2025b warns and answers as for a real matrix: `[1i 2i; 2i 4i] \ [1; 2]` is
  `NaN + NaN·i` under `MATLAB:singularMatrix`, `[1i 0; 0 0] \ [1; 1]` is `[0 - 1i; Inf + NaN·i]`,
  `[1 0; 0 1e-17i] \ [1; 1]` warns `MATLAB:nearlySingularMatrix` with `RCOND =  1.000000e-17.`, a NaN
  is `MATLAB:illConditionedMatrix`. A **real** matrix with a complex right-hand side is two real
  systems there: `[1 2; 0 0] \ [1i; 2]` is `[-Inf + NaN·i; Inf + NaN·i]`, where one complex
  substitution would give `NaN + NaN·i` in its first row. Its nearly singular case also went
  unwarned here.
- **`isfloat(true)`** was true; R2025b answers false (for an empty logical too).
- **`sqrt(-0)`** was -0 (IEEE's root); R2025b's is +0, for a single, in a loop, inside a complex
  array and through `realsqrt` alike.
- **The single class was dropped** by `asec`, `acsc`, `acot`, the hyperbolic reciprocals and their
  inverses, every degree function, `deg2rad`, `rad2deg`, `atan2d`, `gammaln`, `erfinv`, `erfcinv`,
  `erfcx`, `reallog`, `realsqrt`, `pow2` and `psi`. R2025b answers single, takes it from either
  operand of `pow2(f, e)` and `atan2d(y, x)`, and from `psi(k, x)`'s x alone
  (`psi(single(1), 2)` is a double).
- **`trace`** answered `true` for `trace(true)` and a double for a single. R2025b's trace.m is
  `sum(diag(A))` behind one refusal: a logical, a char or an integer sums to a double, a single to a
  single, and a non-square or N-D argument is `MATLAB:trace:square` ("Matrix must be square.").
- **`filloutliers(true)`** answered JGraph's sentence; R2025b's is `MATLAB:minrhs` for any data.
- **`[4 0; 0 9] ^ 0.5`** was refused ("'^' on a matrix supports integer exponents only."), and a
  complex exponent fell through to the elementwise power (`[2 1; 0 2] ^ (0.5+1i)` answered `1` off
  the diagonal), a complex matrix's whole power was refused, `A ^ Inf` raised an internal overflow,
  and the integer refusal of `^` had no identifier. R2025b's `mpower` is a MATLAB file
  (`toolbox/matlab/matfun/mpower.m`) and was read, and its output compared bit for bit against a
  copy run outside the toolbox over 65 cases.
- **`int32(4) .^ single(0.5)`** was `MATLAB:integerPower` and **`int8(2) .^ true`** answered.
  R2025b refuses both, `true ^ int8(2)`, `single(2) .^ int8(2)`, `int8(2) .^ int16(2)` and an empty
  base alike as `MATLAB:mixedClasses` ("Integers can only be combined with integers of the same
  class, or scalar doubles."); a char and a double still combine.

## Decision

### A square complex system answers where it is singular (`ComplexSquareSystems`)

`JGraph.Numerics.LinearAlgebra.ComplexSquareSystems.Solve` is the complex half of ADR 0355's
`SquareSystems`, in managed code above the backends: a triangular matrix is substituted (lower
before upper) with RCOND its diagonal's smallest magnitude over its largest; anything else takes the
provider's `Zgetrf` (the managed one for a NaN, as the real road does), a zero pivot is substituted
through the factors in managed code, and a nonsingular one is the provider's `Zgetrs` with RCOND
from a managed transcription of LAPACK's `zgecon`/`zlacn2` estimate over the factors (a few solves,
no inverse). One division rule places the Inf and NaN where R2025b's land: a divisor with a zero
imaginary part divides each part on its own (`ComplexSquareSystems.Divide`), anything else is
Smith's quotient. `JgsBuiltins.ComplexMatrixSolve` sends a square system there, or, when the matrix
is real, solves the right-hand side's real and imaginary parts as one real system of twice the
columns through `SquareSystems.Solve` (`SolveRealByParts`). It reports the condition and RCOND, and
the interpreter's `\` and `/` raise R2025b's warning through `WarnIfSingular`, so
`warning('off'|'error', id)` and `lastwarn` reach it. No backend provider changed.

### isfloat, sqrt(-0), trace and filloutliers

- `isfloat` excludes a logical (`IsLogicalValue`), in both dialects.
- `sqrt` and `realsqrt` are `Math.Sqrt(x + 0.0)` (`SqrtPlusZero`), the complex `sqrt` reads a -0 real
  part as +0, the loop compiler's `sqrt` is the same function, and `PackedMath`'s `UnaryOp.Sqrt`
  adds +0 to each tile before the vector root (and in its scalar loop), so the packed and boxed lanes
  agree to the bit.
- `trace` in the MATLAB dialect refuses as trace.m does and answers a double, or a single summed in
  single arithmetic (`SingleTrace`); JGS keeps its own words and the old answers.
- `filloutliers` with fewer than two arguments is `MATLAB:minrhs`, before the type check.

### The floating family keeps single (`FloatCarryingBuiltins`)

The names above join ADR 0125's floating list; `pow2` and `atan2d` join the multi-subject list,
and `psi(k, x)` reads its class from x (`FromOf`). The answer is computed in double and rounded to
single, as the rest of the list is; R2025b computes in single, so the last bit can differ (the
degree functions' exact values — `sind(single(30))` is 0.5, `rad2deg(single(pi))` is 180 — agree).

### A matrix to a scalar power is mpower.m's (`FractionalMatrixPower`)

`Interpreter.MatrixPower` reads the exponent whole (a 1-by-1 array, a complex number) and refuses as
mpower.m orders it: an N-D base `MATLAB:mpower:inputsMustBe2D`, a non-square one
`MATLAB:mpower:notScalarAndSquareMatrix`; the integer refusal now carries
`MATLAB:mpower:integerNotSupported`. A finite real whole power of a real matrix is the repeated
product it always was (the 1e-12 tolerance on "whole" is gone: mpower.m asks `round(Y) == Y`).
Everything else goes to `JgsBuiltins.MatrixScalarPower` and
`JGraph.Numerics.LinearAlgebra.FractionalMatrixPower`, a transcription of mpower.m:

- `IntegerPower` is `integerMpower` (repeated squaring, the inverse first and unwarned for a negative
  power, Inf for a singular one, NaN for a zeroth power of a matrix holding one) — a complex
  matrix's whole power;
- `Evaluate` is `generalMpower`: a Hermitian or skew-Hermitian matrix is `V·diag(d.^p)·Vᴴ` over an
  orthonormal basis (the symmetric eigensolver for a real symmetric matrix, the Schur form otherwise),
  anything else `V·diag(d.^p)/V` over its eigenvectors, solved in real arithmetic when both are
  real, unless RCOND of V is below 1e-2 and the power real and finite, when it is `mpower_schur`:
  Higham and Lin's Schur–Padé algorithm with `powerm2by2`, `min_sqrts`, `sqrt_power_1`, the
  continued-fraction coefficients, the squaring phase and `make_real`, transcribed;
- a matrix holding an Inf or a NaN is a real NaN throughout (measured), and `d.^p` is raised as
  MATLAB's `.^` raises a real or a complex vector (0.5 the root, with R2025b's complex `sqrt` to the
  bit, measured over fourteen values; -1 the reciprocal).

The managed linear algebra lane, run at landing, answered three rows otherwise: its eigensolver left
roundoff off the diagonal of `[4i 0; 0 9]`, and its product kernels skip a zero entry, so an infinity
never met a zero (`zeros(2) ^ -0.5`, `[0 0; 0 4] ^ -0.5`). The answers do not depend on the backend: a
diagonal matrix takes the identity as its eigenbasis, as LAPACK's `zgeev` returns it, and a product
with an infinity or a NaN in either factor sums every term by definition.

### An integer power with a single or a logical is mixedClasses (`PowerMixesClasses`)

In the MATLAB dialect `^` and `.^` refuse an integer class paired with a single, a logical or a
different integer class, empty or not, after the mpower integer refusal and before the class
combine. `+`, `.*` and the rest are not changed (see Still open).

## Consequences

- New parity fixture `oi_numeric_leftovers` (649 rows from R2025b 25.2, every row agreeing, packed
  and boxed), with two helpers: `p5e_chk` prints a complex answer's real and imaginary parts as two
  `mat2str` texts (JGraph's `mat2str` spells a complex infinity `Inf+NaNi` where R2025b writes
  `Inf+1i*NaN`), and `p5e_close` prints each element under a relative tolerance for the fractional
  powers, which reach R2025b's answers to rounding (1e-12; 1e-6 to 1e-7 for three defective
  matrices whose answers both engines get only to about 1e-9).
- New tests `MatlabNumericLeftovers321Tests`. Changed: `MatlabLinearAlgebraBuiltinTests` (the
  fractional power answers), `MatlabComplexAndPencilM91Tests` (the singular complex system refuses
  only under `warning('error', ...)`), `PackedMathM92Tests` (two tests pinned IEEE's -0 root).
- A script that solved a singular complex system, or raised a matrix to a fractional power, now
  gets an answer and a warning where it stopped; one that raised an integer with a single or a
  logical, or called `trace` on a vector, now stops with R2025b's error.
- `sqrt` of a packed array runs one more in-cache pass per tile (adding +0); not timed (timing runs
  were not allowed in this wave).

## Divergences

None opened. The fractional power's last bits are not a divergence: mpower.m's answer depends on
the eigensolver's vectors, and every row agrees under its stated tolerance.

## Still open

- **The managed backend's RCOND cost** (item 321's first part): every real square solve computes
  RCOND, and the managed `Gecon` is an exact inverse (ADR 0089). Measuring it and replacing it with
  an estimator needs a timing run on an idle machine and was not done here. The complex road added
  here already uses an estimator.
- **`[1 2 3; 4 5 6; 7 8 9] * 1i \ b`** is singular here and nearly singular in R2025b, the real
  case's OpenBLAS-against-MKL pivot (ADR 0355's open row).
- **`complex(zeros(2)) \ b`** answers a real `[Inf; NaN]` here: the zero-imaginary complex flag is
  lane 5C's.
- **A vector to a scalar power** (`[1 2 3] ^ 2`, `[1 2 3] ^ 0.5`) answers elementwise here and is
  `MATLAB:mpower:notScalarAndSquareMatrix` in R2025b; the vector base never reaches the matrix road.
- **`int8(2) + true`, `int8(2) + single(2.5)`, `int8(2) .* true`** are `MATLAB:mixedClasses` in
  R2025b and answer here: the class combine of every other operator.
- **Negative arguments of `gammaln` and `psi`** are `MATLAB:gammaln:negativeVal` and
  `MATLAB:psi:negativeX` in R2025b (double and single); here they answer. `gammaln(Inf)` is NaN here
  (R2025b Inf) and `psi(0)` Inf (R2025b -Inf). `reallog(-1)` and `realsqrt(-1)` say JGraph's sentence
  with no identifier (R2025b `MATLAB:reallog:complexResult`, "Reallog produced complex result.").
- **mat2str of a complex infinity** is `Inf+NaNi` here and `Inf+1i*NaN` in R2025b.
- **A complex `inv`** of a nearly singular matrix does not warn (only the singular case does).
- **`normAm`** above order 50 is an estimate in MATLAB and exact here, which can choose a different
  Padé degree for a large defective matrix (an answer that agrees to rounding either way).
