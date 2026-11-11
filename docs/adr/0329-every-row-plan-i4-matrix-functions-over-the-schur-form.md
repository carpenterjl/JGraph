# ADR 0329 — The every-row plan's I4: sqrtm and logm over the Schur form, pinv as one product, funm's products through the backend

## Status

Accepted, 2026-11-10. Stage I4 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 9). **Partly done**: three of the four rows are under R2025b's median
(`d10_sqrtm_400`, `d10_logm_400`, `d10_funm_120`) and `d10_pinv` is one millisecond over it, level
with it at steady state, with nothing left in it but the SVD that I3 owns. The script's wall is
0.47 s where it was 0.86 and R2025b's is 0.95. Every CHK line that agreed with R2025b still does.

## Context

The GUI round (ADR 0322) left four rows of `d10_matfun` to I4, 0.34 s of excess between them:
`d10_sqrtm_400` at 0.111 s against R2025b's 0.014, `d10_logm_400` at 0.266 against 0.091,
`d10_pinv` at 0.073 against 0.017 and `d10_funm_120` at 0.035 against 0.025. Reading each down to
its kernel found three different things:

- **`sqrtm` and `logm` were iterations over the full matrix.** `sqrtm` was Denman-Beavers on a
  `double[,]`: two LU factorizations and two explicit inverses a step, eight or so steps. `logm`
  took that root until the matrix was within 0.25 of the identity and then summed thirty-two terms
  of the Mercator series, a full matrix product a term. Both were M42's (ADR 0045), written for a
  stress script, and both were wrong outside the benchmark's shape as well as slow: a matrix with a
  negative real eigenvalue has a complex principal root and logarithm, and the iteration answered
  a real matrix that was neither or refused with "the iteration hit a singular intermediate"; a
  singular matrix was refused where R2025b warns and answers.
- **`pinv` was a triple loop.** After an economy SVD through LAPACK it accumulated
  `V·diag(1/s)·Uᵀ` one singular value at a time, each a rank-one update written with a stride:
  200 values by 300 by 600 is 36 million strided updates, four times what the SVD before it cost.
- **`funm`'s block products were a triple loop over `Complex[,]`.** `G(1:120,1:120)` has every
  eigenvalue within 0.1 of the others, so Schur-Parlett takes it as one block of 120 and sums its
  Taylor series: a 120-cube complex product a term (`NormEstimators.Product`, written for the norm
  estimator's thin operands), and two more to take the answer out of the Schur basis.

D4 of the plan asked whether `sqrtm` and `logm` may change algorithm; the answer recorded there is
yes, with the two residuals the script prints (already divergent from R2025b's in their last
digits) kept at rounding level.

## Decision

### `sqrtm` and `logm` are functions of the Schur form (`MatrixRoots`)

A new numerics class, `JGraph.Numerics.LinearAlgebra.MatrixRoots`, answers both for a real square
matrix held flat and column-major. It takes one of three roads by the shape of the input, which
are the three R2025b's own `sqrtm` and `logm` take:

- **Already upper triangular:** no factorization and no way back. A diagonal matrix is answered
  entry by entry, so the root of a diagonal of exact squares is exact and `logm(zeros(2))` is
  `[-Inf 0; 0 -Inf]`.
- **Symmetric:** the symmetric eigensolver (`dsyevd`), the function of the eigenvalues, and the
  eigenvectors to carry it back. For a root with no negative eigenvalue that is `B·Bᵀ` with
  `B = V·diag(w^¼)`, one `dsyrk`, so the answer is exactly symmetric and costs half a product;
  otherwise it is `V·diag(f)·Vᵀ` by `dgemm`, real and imaginary parts apart, averaged across the
  diagonal so that it too is exactly symmetric.
- **Anything else:** the real Schur form (`dgees`), each two-by-two block turned into a complex
  triangle by one rotation (`rsf2csf`'s rotation, the one `SchurConversion.RealToComplex`
  applies), the function of the complex triangle, the rotations undone on the answer, and
  `Q·F·Qᵀ` by two real `dgemm`. Undoing the rotations on the answer rather than applying them to
  the Schur vectors is what keeps the vectors real: the way back is two real products where the
  complex Schur basis would have needed two complex ones, four times the arithmetic.

The triangular kernels are:

- **The root** is the Björck-Hammarling recurrence, `R(i,j) = (T(i,j) − Σ R(i,k)·R(k,j)) /
  (R(i,i) + R(j,j))`, taken a superdiagonal at a time: every entry of a superdiagonal reads only
  superdiagonals nearer the diagonal, so its entries are handed out across threads. The root is
  kept twice, by columns and by rows, so both halves of each dot product are contiguous, and the
  dot product is a `Vector256` kernel over the interleaved complex storage.
- **The logarithm** is inverse scaling and squaring with the thresholds and degree choice
  `funm(A, @log)` has used since M107 (`MatrixFunction.TriangularLogarithm`): roots until the
  triangle's one-norm distance from the identity is under 0.264, then the Padé approximant in its
  Gauss-Legendre partial-fraction form, `Σ w·A·(I + s·A)⁻¹`. Each term is a triangular solve from
  the right, which is a forward substitution along each row; no row reads another, so the rows are
  handed out across threads and each takes all the nodes in turn. The diagonal of the answer is
  then set to the logarithm of the triangle's diagonal outright, so `logm(eye(3))` is exactly
  zero and a negative eigenvalue's imaginary part is exactly π.

Every entry of either kernel is one dot product computed whole on one thread, so the answer is the
same at one thread and at sixteen (`OneThreadAndManyAgreeToTheLastBit`).

A real matrix with no eigenvalue on the closed negative real axis has a real principal root and
logarithm, so the imaginary parts the rotations leave are dropped rather than carried back; with a
negative real eigenvalue the answer is complex, as R2025b's is. The builtins moved beside `funm`
(`JgsBuiltins.MatfunSpectral.cs`), where the script's error stream is in reach, and now say what
R2025b says: "Matrix is singular and may not have a square root." for a zero eigenvalue under
`sqrtm`, "Principal matrix logarithm is not defined for A with nonpositive real eigenvalues. A
non-principal matrix logarithm is returned." under `logm`, `MATLAB:sqrtm:inputMustBeFinite` for a
NaN or an infinity under `sqrtm`, and a matrix of NaN for one under `logm`.

### `pinv` is one product

The singular values descend, so the ones over the tolerance are a leading run of length `r`.
`pinv` scales the first `r` columns of `V` by `1/s` and answers `Vs·U(:,1:r)ᵀ` by one `dgemm`,
written straight into the packed result.

### `funm`'s products go to the backend from order 24

`MatrixFunction.Multiply` and `Conjugated` (the block's Taylor series and `U·F·Uᴴ`) call
`ComplexLinear.Multiply` (`zgemm`) when the operand's order is 24 or more. Under that the triple
loop is kept, so the small matrices the parity fixtures and the M107 and M142 tests hold keep
their last bits. `s ^ A` shares the code and the threshold.

## Measured

`head2head_v3\measure_i4.ps1` and `compare_i4.py`, `d10_matfun.m` unchanged, one session per
condition (`runs\i4-first`): three headless sessions of this build and three of the build before
it (3b93a455) alternated, one app session, and one eight-pass headless session of each build read
over passes 4 to 8 (steady). Medians of the timed passes, in seconds.

| Row | R2025b | before | I4 | I4 app | I4 steady | before, steady |
|---|---|---|---|---|---|---|
| `d10_sqrtm_400` | 0.014 | 0.128 | 0.012 | 0.012 | 0.011 | 0.107 |
| `d10_logm_400` | 0.091 | 0.272 | 0.055 | 0.051 | 0.045 | 0.276 |
| `d10_funm_120` | 0.025 | 0.035 | 0.014 | 0.012 | 0.013 | 0.034 |
| `d10_pinv` | 0.017 | 0.072 | **0.018** | 0.018 | 0.017 | 0.072 |
| `d10_matfun` wall | 0.949 | 0.861 | 0.472 | 0.446 | 0.430 | 0.857 |

`d10_pinv` read 0.017, 0.018 or 0.019 in every one of the twelve timed passes; R2025b's median is
0.017.

The split probe (`probes\instrumented\split_i4.m`, mean milliseconds a call over five calls, one
session per engine) says where what is left lies:

| Piece | I4 | before | R2025b |
|---|---|---|---|
| `[U,T] = schur(E)`, 400 | 23.5 | 24.3 | 16.4 |
| `logm(E)` | 62.2 | 345.3 | 54.1 |
| `logm(triu(E))` (no Schur form, no way back) | 54.6 | 355.1 | 22.9 |
| `sqrtm(triu(E))` | 15.2 | 87.7 | 3.9 |
| `sqrtm(E)` (general) | 42.1 | 88.9 | 23.2 |
| `[V,D] = eig(S)`, symmetric 400 | 35.5 | 29.4 | 5.5 |
| `sqrtm(S)` | 10.8 | 139.5 | 7.0 |
| `logm(S)` | 14.3 | 590.5 | 7.4 |
| `funm(A, @exp)`, 120 | 21.2 | 37.5 | 11.2 |
| `[U,S,V] = svd(B,'econ')`, 600 by 300 | 16.6 | 17.9 | 9.3 |
| `pinv(B)` | 18.8 | 71.0 | 9.9 |
| `orth(B)` | 18.0 | 20.9 | 8.8 |

Three things in it are worth their own line. The rows the script times are under R2025b while the
probe's repeated calls are not: the script's R2025b numbers are a first call in each pass and the
probe's are a warmed loop, and R2025b gains more from the loop than this build does. The
triangular kernels are the slow part of what is left: `logm` of a 400 triangle is 55 ms against
R2025b's 23 and `sqrtm` of one is 15 ms against 4, so the complex recurrence and the seven Padé
solves are two to four times what R2025b's blocked real-arithmetic kernels cost (open item 266).
And `[V,D] = eig(S)` through the script's `eig` is 35 ms where `sqrtm(S)`, which calls the same
`dsyevd` and then a `dsyrk`, is 11 ms: the `eig` builtin's two-output road is losing 25 ms
somewhere outside LAPACK (open item 267; not a row of the round).

`d10_expm_400`, which this stage did not touch, read 0.031 against the old build's 0.028 headless
and 0.034 against 0.028 at steady state. Its passes scatter from 0.025 to 0.054 on this build and
from 0.026 to 0.044 on the old one, so the medians are within the scatter, but the direction is
the same in both conditions and it is recorded (open item 268). The row is I3's and was over
R2025b's 0.020 before.

### Checks

- Of the 28 CHK lines of `d10_matfun`, 25 are byte-identical to the GUI round's in all five
  sessions of this build, `d10_funm` among them. The three that moved are the three residuals this
  stage's kernels produce, all of which already differed from R2025b's: `d10_sqrtm_resid`
  1.705e-15 to 2.700e-15 (R2025b 3.268e-15), `d10_logm_roundtrip` 5.773e-15 to 7.327e-15 (R2025b
  4.552e-15) and `d10_pinv_resid` 1.688e-14 to 1.610e-14 (R2025b 7.661e-15).
- `MatrixRootsI4Tests` (12 tests): R2025b's own answers to seventeen digits for the small cases of
  `sqrtm`, `logm` and `pinv` (triangular, symmetric, a rotation, a negative eigenvalue, a singular
  matrix, a scalar, an empty), the two warnings and the refusal, a 60 by 60 matrix with twenty
  negative eigenvalues and a conjugate pair against R2025b's sums, residuals at order 150,
  `funm(A, 'log')` against `logm`, `funm` over one block of 60 and over several, the four
  Moore-Penrose conditions on a rank-deficient 60 by 30, and one thread against many to the last
  bit.
- The 320 tests of the areas the diff touches (the matrix functions of M42, M90, M91, M107 and
  M142, the operator functions, the matrix builtins and the `m134_design` fixture) pass in all
  four lanes: native and managed linear algebra, packed and boxed storage.
- The stress suite passes 88 of 89 scripts; the one failure is `stess_40`'s stale assertion (open
  item 254, older than this stage). The seven `verify-*.py` scripts pass and the divergence
  harvest gains this ADR's three rows.

## Consequences

- `sqrtm` and `logm` differ in their last bits from every earlier build's, are the same at any
  thread count, and answer three kinds of input they used to get wrong or refuse: a matrix with a
  negative real eigenvalue (a complex answer), a singular matrix (a warning and an answer), and a
  triangular or diagonal one (exactly, without a factorization).
- `sqrtm` of a symmetric matrix is exactly symmetric, and so is `logm` of one.
- `pinv` differs in its last bits from every earlier build's at every size (it is a `dgemm` where
  it was a sum of rank-one updates). No parity fixture or stress script held it to the last bit.
- `funm` and `s ^ A` differ in their last bits from order 24 up and not below.
- A symmetric matrix whose smallest eigenvalue is a rounding error below zero has a root with
  imaginary parts of the size of that error's square root. R2025b's symmetric branch has the same
  property; it is the eigenvalue's sign that decides, in both.
- No switch was added: the old iterations are gone rather than kept behind one, because they
  answered wrongly for the inputs listed above and a switch would have kept that reachable.

## Divergences

- **`sqrtm` and `logm` take a real matrix and answer one output.** R2025b also takes a complex
  matrix and answers `[X, resnorm]` and `[X, alpha, condx]` for `sqrtm` and `[L, exitflag]` for
  `logm`; here a complex input and a second output are refused, as they were before this stage.
- **The residuals `d10_matfun` prints for `sqrtm`, `logm` and `pinv` differ from R2025b's in
  their leading digits.** `d10_sqrtm_resid` is 2.70e-15 against R2025b's 3.27e-15,
  `d10_logm_roundtrip` 7.33e-15 against 4.55e-15 and `d10_pinv_resid` 1.61e-14 against 7.66e-15:
  all three are rounding error of the same size from different arithmetic, and all three differed
  before this stage too (1.71e-15, 5.77e-15 and 1.69e-14).
- **The singular warning of `sqrtm` is raised for an eigenvalue that is exactly zero.** A singular
  symmetric matrix whose computed eigenvalue is a rounding error away from zero (`[1 1; 1 1]`) is
  warned about in R2025b when its eigensolver returns the zero exactly, and may not be here.

## Still open

- **`d10_pinv`** is 0.018 against 0.017 (0.017 at steady state). The product this stage replaced
  the loop with is under a millisecond; the rest is `dgesdd`, 16.6 ms against R2025b's 9.3 for the
  same economy SVD, which is I3's (`d10_null` and `d10_orth` wait on the same thing).
- **The triangular kernels** (open item 266): `logm` of a 400 triangle is 55 ms against R2025b's
  23 and `sqrtm` of one 15 ms against 4. Both rows are under R2025b as the script times them, so
  this is headroom and not a lost row.
- **`[V,D] = eig(S)` of a symmetric 400** is 35 ms against R2025b's 5.5 and against the 11 ms the
  same LAPACK call costs inside `sqrtm` (open item 267).
- **`d10_expm_400`** read three to six milliseconds slower than on the build before, inside its
  scatter (open item 268).
- `sqrtm` and `logm` of a complex matrix, and their second and third outputs, are still refused.
