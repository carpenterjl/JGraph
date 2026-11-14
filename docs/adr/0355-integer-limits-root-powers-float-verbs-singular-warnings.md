# ADR 0355 — intmax answers its class, x.^0.5 is the root, the floating verbs refuse a non-float, and a singular matrix warns

## Status

Accepted, 2026-11-13. Open items 199, 272, 299 and 307 (chips wave 3, agent Q).

## Context

- **Item 199.** `intmax`, `intmax('int8')`, `intmin('int64')` answered the right number as a `double`,
  so `intmax('int8') + 1` was 128 and `class(intmax)` 'double'. R2025b answers in the class named
  (`int32` with none), takes `intmax('like', p)` for an integer `p`, and refuses everything else in
  three sentences (`MATLAB:intmax:invalidClassName`, `:mustBeLike`, `:invalidPrototype`; probes
  `probe_int`, `probe_misc`). The value model stores every number as a double with a class tag
  (ADR 0069), so int64 and uint64 past 2^53 cannot be held exactly.
- **Item 272.** I9 (ADR 0330) made `x.^2` the product. R2025b's `x.^0.5` is `sqrt(x)` and `x.^-1` is
  `1./x` to the bit (probe `probe_pow`: corners, 21,000 random doubles across every magnitude, scalars,
  loops, anonymous functions, single); here both were the C runtime's `pow`, an ulp off at `realmax`.
  The corners: `(-0).^0.5` is +0, `0.^-1` Inf, `(-0).^-1` -Inf, NaN keeps its payload, a negative base
  is complex with an exact zero real part (`(-4).^0.5` is `2i`, where `Complex.Pow` left 1.2e-16), and
  `(-Inf).^0.5` is `Inf·i` (here NaN + NaN·i). An integer class is raised only to a whole power that is
  not negative: `int32(4).^0.5`, `int8(4)^-1`, `4.^int8(-1)` and `int32(2).^NaN` are
  `MATLAB:integerPower` (`probe_pow2`, `probe_ipow`); here they answered.
- **Item 299.** `sqrt(true)`, `isoutlier(true)` answered; `isoutlier` of a single answered double
  fences; `integral` took `'Vectorized'`. Probe `probe_float` handed 95 functions `true`, `int32(4)`,
  `'a'`, `single(0.5)` and `uint8(3)`: the whole transcendental family, `hypot`, `atan2`, `psi`,
  `beta`, `det`, `rcond`, `airy` and `besselj` refuse all four non-floats with one sentence,
  `MATLAB:math:mustBeFloat`; the matrix verbs (`inv`, `eig`, `lu`, `qr`, `chol`, `svd`, `rank`, `pinv`,
  `null`, `orth`, `cond`, `norm`, `rat`, `expm`, `sqrtm`, `logm`) refuse them in their own words
  (`probe_la`); `deg2rad` and `rad2deg` in theirs; the rounding, sign and arithmetic verbs answer all
  five. The outlier family refuses a logical, an integer, a char and a cell
  (`MATLAB:isoutlier:invalidType`, `MATLAB:filloutliers:invalidType`).
- **Item 307.** `inv` of a singular matrix, and `\` and `/` of a singular square system, raised an
  error with no identifier. R2025b warns and answers (`probe_sing`, `probe_sing2`, `probe_tri`):
  `MATLAB:singularMatrix` ("Matrix is singular to working precision.") for a zero pivot or an RCOND
  of exactly 0, `MATLAB:nearlySingularMatrix` ("... RCOND =  1.306145e-17.") below eps (single's eps
  for a single), `MATLAB:illConditionedMatrix` for a NaN RCOND; `inv` of a singular matrix is Inf, a
  singular solve whatever its substitutions give in IEEE arithmetic, a negative power of a singular
  matrix Inf with no warning. The rank-deficient warning of a rectangular solve had no identifier
  (R2025b: `MATLAB:rankDeficientMatrix`).

## Decision

### intmax and intmin answer in their class (`ClassedIntegerLimit`)

In MATLAB code, read at the call (V11, ADR 0172), the limit is stamped with its class; JGS code keeps
the plain double. Text naming a class is a char row or a string scalar, case exact; `'like'` is case
blind; a prototype must carry an integer class (a complex integer does). **`intmax('int64')` is the
double 2^63 tagged int64, and `intmax('uint64')` the double 2^64 tagged uint64**: the class's
saturation point as `JgsNumericClasses.Range` already held it, so `intmax('int64') == int64(2^63)`
and `intmax('int64') + 1 == intmax('int64')` are true as in R2025b, and every cast saturates to it.
The alternative, the largest double below 2^63, would make `int64(2^63)` and `intmax('int64')`
differ, which R2025b never does. `intmin('int64')` (-2^63) is exact. `mps.json.decode` saturated
through BigInteger's truncating conversion to 2^63 - 1024; it converts through `long`/`ulong` now,
so `intmax('uint64')` survives an encode and decode and compares equal.

### A power of 0.5 is the root and a power of -1 the reciprocal (`PackedMath.Power`)

`PackedMath.Power(x, 0.5)` is `Math.Sqrt(x + 0.0)` (the `+ 0.0` turns -0 into +0 and leaves every
other value alone) and `PackedMath.Power(x, -1)` is `1.0 / x`; every road ADR 0330 listed goes
through it. The packed array-by-scalar kernel takes 0.5 as `TensorPrimitives.Add` of 0 and
`TensorPrimitives.Sqrt`, and -1 as `TensorPrimitives.Divide(1, x)`. The complex road
(`ComplexPower`) takes `Complex.Sqrt` and `1 / z` for the two exponents. In MATLAB code an integer
class on either side of `^` or `.^` with an exponent that is negative, fractional or NaN is
`MATLAB:integerPower` (`IntegerPowerAllowed`); an empty base answers.

### The floating-only verbs refuse a non-float (`JgsBuiltins.FloatSubjects`)

`RefuseNonFloatSubjects` wraps, in MATLAB code only and outside the class carry, the names R2025b
refuses: `MustBeFloatBuiltins` (every argument), `MatrixInputTypeRefusals` (the first argument, each
in its own words), `deg2rad`/`rad2deg`, and the outlier family (the first argument; a char's message
has no "Instead" line, as R2025b's has not; `filloutliers` counts its arguments first). ADR 0125 kept
the integer half of this as a divergence rather than stop a running script; the logical half was
reported, and R2025b's rule is one rule over logical, char and the integers, so it is implemented as
one.

### A single's outlier fences are single

`isoutlier`, `rmoutliers` and `filloutliers` of a single answer their fences, centres and cleaned
data as single (`InDataClass`). The median fences, the default, are computed in single arithmetic
(each operation in double rounded to float, which is that operation's correctly rounded float) and
agree with R2025b to the bit; the other methods' are their double fences rounded.

### integral keeps 'Vectorized'

It is a JGraph extra that R2025b refuses (`MATLAB:TooManyInputs`). It misleads no one, so by the
rule for extras (items 82 and 84) it stays and is recorded as a divergence.

### A singular square system warns and answers (`SquareSystems`)

`SquareSystems.Solve` and `SquareSystems.Invert` (JGraph.Numerics) are MATLAB's `A\B` and `inv(A)`
for a square dense real `A`:

- **A triangular matrix is substituted**, lower before upper (a diagonal one is both, and R2025b's
  `diag([1 0 2]) \ ones(3, 1)` is forward substitution's `[1; Inf; NaN]`), with IEEE division and no
  skipped zeros (`[1 2; 0 0] \ [1; 0]` is NaN in R2025b). Its RCOND is the diagonal's smallest
  magnitude over its largest, which is what R2025b quotes (`[1 1; 0 1e-17]` says 1e-17, the true
  1-norm figure being 5e-18, and `[1 1e20; 0 1]` says nothing).
- **Anything else takes the partial-pivot LU.** A zero pivot is `Singular`, and the solve runs the
  factors' own substitutions in managed code so the Inf and NaN fall where R2025b's do on both
  backends. Otherwise RCOND is the provider's `Gecon` and the solve its `Getrs`.
- **`inv`**: a diagonal matrix inverts element by element; a triangle with a zero on its diagonal is
  Inf over its triangle; a nonsingular triangle inverts by `Trtrs` against the identity (a pivoted LU
  finds an exact zero pivot in `[1 0 0; 2 1e-16 0; 3 4 7]`, which R2025b inverts); any other matrix
  with a zero pivot is Inf throughout; RCOND for the warning is the LU estimate.
- **A NaN anywhere goes to the managed kernels**: the native LAPACKE entry points check their inputs
  for NaN and return untouched, which left `inv([1 NaN; 2 3])` answering its own argument and
  `[1 NaN; 2 3] \ b` answering `b`.
- `Classify` names the warning: zero pivot or RCOND 0 → singular, NaN → ill-conditioned, below eps
  (single's for a single operand) → nearly singular.

The interpreter's square solves (`MatrixSolve`) and `inv` raise the warning through
`JgsBuiltins.Warn`, so `warning('off', id)` silences it, `warning('error', id)` refuses with it, and
`lastwarn` reads it. `inv` of a complex singular matrix is Inf under the same warning. A negative
power of a singular matrix is Inf with no warning (`MatrixPower`). The rank-deficient warning of a
rectangular solve carries `MATLAB:rankDeficientMatrix`.

## Consequences

- New parity fixture `oi_numeric_classes_warnings` (383 rows from R2025b 25.2, four of them
  divergences), packed and boxed. Its operator rows use variables: R2025b folds an operator over
  literal arrays when it reads the file, warns then, and leaves `lastwarn` empty at the call.
- `k14_mps_json`: rows `i_08` and `e_f_uint64_max_round` agree now; `i_09`, `i_10`,
  `e_f_uint64_max_small`/`_large`, `y_uint64_max` and `y_uint64_over` stay divergences, restamped.
- New tests `SquareSystemsAndRootPowersTests` and `MatlabNumericClassesWarningsTests`. Three tests
  that asked for the old refusal (`MatlabLinalgProviderM89Tests` two, `MatlabLinearAlgebraBuiltinTests`
  one) ask for the warning, or refuse under `warning('error', id)`.
- A script that called `sqrt`, `exp`, `norm`, `eig` and the rest on a logical, a char or an integer,
  or raised an integer to a fractional or negative power, now stops with R2025b's error.
- Every square `\` and `/` now computes RCOND (an O(n²) estimate on the native backend). **On the
  managed backend `Gecon` is the exact reciprocal (ADR 0089), an inverse of its own, so a managed
  square solve costs about twice what it did.**

## Measured

- The probes in both engines after the change: `probe_pow` and `probe_pow2` agree but for `sqrt(-0)`
  and `[4 0; 0 9] ^ 0.5` (below); `probe_int` agrees but for the display of an int64 past 2^53;
  `probe_float` agrees on every refusal; `probe_tri` agrees on all 26 warnings.
- The fixture under `JGRAPH_LINALG=managed` prints what the native backend prints.

## Divergences

- **`intmax('int64')` is 2^63 and `intmax('uint64')` 2^64**, one past R2025b's, because a double
  holds neither; `intmax('int64') < 2^63` is false here (`int64_max_below_2p63`).
- **An int64 or uint64 past 2^53 prints as a double**: `sprintf('%d', intmax('int64'))` is
  `9.223372e+18` and `num2str(intmax('uint64'))` `1.844674407370955e+19` (`int64_max_text`,
  `uint64_max_text`; ADRs 0069 and 0156).
- **`integral(..., 'Vectorized', v)` is taken**; R2025b refuses the name (`integral_vectorized`).

## Still open

- **`sqrt(-0)` is -0 here and +0 in R2025b**; `(-0).^0.5` is +0 in both.
- **A singular complex system** (`[1i 2i; 2i 4i] \ b`) is still refused; R2025b answers NaN under
  `MATLAB:singularMatrix`.
- **R2025b's symmetric and Hessenberg roads** (Cholesky, LDLᵀ) are not taken: a nonsingular system
  agrees to rounding, a singular symmetric one may put its Inf and NaN elsewhere, and the RCOND quoted
  for a symmetric positive definite matrix (`hilb(12)`) is the LU estimate, not R2025b's Cholesky one.
- **A matrix whose LU pivot is exactly zero here and tiny in R2025b** (`[1 2 3; 4 5 6; 7 8 9]`) is
  singular here and nearly singular there: OpenBLAS and MKL round the elimination differently.
- **The sparse `inv` and `\`** keep their own roads (`inv(sparse(A))` is refused here).
- **The single class is dropped** by `asec`, `acsc`, `acot`, the hyperbolic reciprocals and their
  inverses, every degree function, `gammaln`, `erfinv`, `erfcinv`, `erfcx`, `reallog`, `realsqrt`,
  `pow2` and `psi` (`probe_float`); R2025b answers single.
- **`int32(4) .^ single(0.5)` and `int8(2) .^ true`** are `MATLAB:mixedClasses` in R2025b; here the
  first is `MATLAB:integerPower` and the second answers. Mixing an integer with a single or a logical
  is the class combine's question, not this item's.
- **`expint`, `gammainc`, `cplxpair`, `legendre`, `sinpi`, `cospi`** were probed and not changed:
  the first four refuse a non-float in other words, and the last two do not exist here.
- **`[4 0; 0 9] ^ 0.5`** (a matrix to a fractional power) is refused here; R2025b answers `sqrtm`.
- **`isfloat(true)`** answers true here (seen in passing); R2025b answers false.
