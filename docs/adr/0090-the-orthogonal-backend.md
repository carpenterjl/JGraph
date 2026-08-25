# 0090 — The orthogonal backend

Date: 2026-08-24 · Status: accepted (M90; the surface grows through M91)

## Context

ADR 0089 put LU, Cholesky and the solves on LAPACK and left the orthogonal factorizations where
they were. They were the worst rows in the head-to-head suite by a wide margin, and not because of
a constant factor:

| row | JGraph | MATLAB | gap |
|---|---|---|---|
| `eig` 400 | 18.7 s | 0.006 s | 3124× |
| `svd` 800 | 40.9 s | 0.032 s | 1278× |
| `qr` 1200 | 6.1 s | 0.037 s | 165× |

`qr` was a constant factor — an unblocked Householder loop over a `double[,]`. The other two were
worse than that. `svd` was one-sided Jacobi, which costs O(n³) *per sweep* and takes as many sweeps
as it takes. `eig` of a general matrix found its eigenvalues from the real Schur form in O(n³) and
then recovered each eigenvector by inverse iteration on the original matrix — three complex
Gaussian eliminations per eigenvalue, so O(n⁴) for the matrix, and complex arithmetic on a real
problem at that. At n = 400 that is the whole of the 18.7 seconds.

Underneath both sat the same marshalling road M89 replaced for the other verbs, and above them sat
a second thing worth fixing: the front ends had drifted from MATLAB's conventions in places where
nobody had had cause to look. `[U, S, V] = svd(A)` answered the economy factors; `s = svd(A)`
answered a row where MATLAB answers a column, and where `svd`'s own complex branch answered a
column; `null` of a wide matrix answered empty, because the economy V it read has no columns for a
null space wider than min(m, n); and `qr` of a triangular matrix answered its negative.

## Decision

1. **The provider grows LAPACK's orthogonal surface.** `DenseLinalg` gains `Geqrf`, `Geqp3`,
   `Orgqr`, `Ormqr`, `Gesdd`, `Gesvd`, `Syevd` and `Geev`, column-major with LAPACK's argument
   conventions — including its packed real eigenvectors, where a conjugate pair occupies two
   consecutive real columns rather than two complex ones. `DenseLinalg.ComplexVectorsOf` unpacks
   them, and sits next to the contract member whose layout it explains, the way `PermutationOf`
   sits next to `Getrf`.

   `Gesvd` is a second SVD driver and not a redundant one: the divide-and-conquer `dgesdd` is the
   faster of the two and can report a failure to converge, and the QR iteration is the more reliable
   retry. `Svd` holds the caller's matrix at arm's length anyway — the driver overwrites what it is
   given — so the retry gets a pristine copy and the caller never learns it happened. On the managed
   backend both members are the same Jacobi sweep, and the doc comment says so.

2. **`ManagedLinalg` adopts LAPACK's storage and conventions rather than merely its results.** The
   Householder loop now writes reflectors below the diagonal with the leading 1 implied and the
   finishing scalar in `tau`, which is `dgeqrf`'s own layout — and is what makes `Orgqr` and
   `Ormqr` interchangeable between the backends instead of each backend's expansion belonging only
   to its own factorization. The arithmetic is the same arithmetic: the old kernel's
   norm-scaled storage was `dlarfg` in different clothes, and the algebra that shows the two agree
   is in the code's comments.

3. **The managed general eigensolver balances before it iterates.** LAPACK's `dgeev` calls `dgebal`
   internally; JGraph's kernel did not, and on a badly scaled matrix that is worth five digits. The
   balancing routine moves out of `JgsBuiltins.Generalized` — where it backed the `balance` verb and
   nothing else — into `Balancing.InPlace` in `JGraph.Numerics`, so there is one implementation and
   `balance(A)` shows exactly the scaling `eig(A)` applies. Eigenvectors are carried back through
   the diagonal afterwards, which is `dgebak`.

4. **A conjugate pair's second eigenvector is the first one conjugated, computed rather than
   iterated for.** The packing can only carry the pair if the second is the conjugate exactly, and
   the old code iterated for each independently. This halves the managed eigenvector work and makes
   the pair exact instead of exact to rounding.

5. **The front ends keep their shapes and hand out flat storage besides.** `QrDecomposition`, `Svd`
   and `Eigen` keep every member the ~40 existing call sites use, and grow column-major factories
   and accessors beside them. `Svd.U` and `Svd.V` are now built rather than stored, so they are
   materialized once and kept: `pinv` indexes them inside a triple loop, and rebuilding the
   rectangle per read would have turned an O(n³) pseudo-inverse into an O(n⁵) one.

6. **Three verbs stop paying for answers they do not use.** `e = eig(A)` asks for no eigenvectors,
   which is most of a general eigensolver's work; `rank`, `cond(A, 2)`, `norm(A, 2)` and
   `linsolve`'s quality report ask for singular values without either factor; and `[C, R] = qr(A, B)`
   applies Qᵀ through the reflectors rather than forming Q to multiply by it.

7. **Four MATLAB divergences close, because LAPACK's conventions are MATLAB's.** `[U, S, V] =
   svd(A)` is full-sized with `svd(A, 'econ')` and `svd(A, 0)` for the economy forms; `s = svd(A)`
   is a column; `null` of a wide matrix reads the full V and so can report a null space wider than
   min(m, n) — a tall one must not be given the full decomposition, whose unread m-by-m U is the
   whole of the memory for a long thin matrix; and `qr` leaves a column that is already zero below
   the diagonal alone, which is `dlarfg`'s identity reflection and the reason `qr(eye(3))` is
   `eye(3)`.

8. **The managed SVD stops normalizing directions it does not have.** A column the sweeps
   annihilated has a norm of about 1e-16 and a direction made entirely of rounding; dividing one by
   the other produced a unit vector pointing nowhere in particular, and two of them were not
   orthogonal to each other. Below a `rows·eps·σ₁` cutoff the direction is discarded and a real one
   put in its place. The completion that does it also changed: it takes the standard basis vector
   that sticks furthest out of the span rather than the first one to clear a fixed threshold, which
   on a nearly-full span could be none of them — leaving a column of zeros in a factor that had
   promised orthonormal ones.

## Consequences

The d01 rows, in the suite's own sequence on an i7-11700F, best of eight runs — the machine was
not idle, and contention only ever adds time, so the minimum is the honest estimator:

| row | before | M90 | MATLAB | ratio |
|---|---|---|---|---|
| `eig` 400 | 18.7 s | **0.019 s** | 0.006 s | 3.17× |
| `svd` 800 | 40.9 s | **0.076 s** | 0.032 s | 2.38× |
| `qr` 1200 | 6.1 s | **0.087 s** | 0.037 s | 2.35× |
| `A*A'` 2000 | 0.063 s | 0.068 s | 0.054 s | 1.26× |
| `A` 2000 | 0.057 s | 0.061 s | 0.057 s | 1.07× |
| `[L,U,P] = lu(A)` 2000 | 0.078 s | 0.081 s | 0.040 s | 2.03× |
| `inv(A)` 2000 | 0.140 s | 0.134 s | 0.159 s | **0.84× — faster** |
| `chol(S)` 2000 | 0.038 s | 0.040 s | 0.017 s | 2.35× |

The three rows this milestone is about improve by 984×, 538× and 70×. Both of the milestone's
absolute targets are met — `eig` 400 at or under 0.02 s and `svd` 800 at or under 0.1 s — and the
one stated as a ratio is not: "every d01 row within 2× of MATLAB" holds for the product, the solve
and the inverse, and fails on `lu` at 2.03×, on `qr` and `chol` at 2.35×, on `svd` at 2.38× and on
`eig` at 3.17×.

That gap is the library, and it is the same gap ADR 0088 recorded as the price of choosing OpenBLAS
over MKL — arriving now on five rows rather than the one it arrived on in M89. Marshalling cannot
account for it, on the arithmetic of how much memory there is to move: the input copy is 1.3 MB for
`eig` 400 and 5.1 MB for `svd` 800, which at any plausible memory bandwidth is a millisecond or so
against nineteen and seventy-six, and even `qr` 1200's three passes over 11.5 MB come to well under
a tenth of its 87 ms. That is a bound rather than a measurement — `DenseFactorizationBenchmarks` and
`OrthogonalFactorizationBenchmarks` time the kernels alone when a quiet machine is available for
them — but it is a bound tight enough to place the remaining time in the kernels, where closing it
would mean a different library rather than different code above one.

The managed fallback is still asymptotically behind on two of the three, and honestly so: one-sided
Jacobi is O(n³) per sweep and inverse iteration is O(n³) per eigenvalue. Both are correct
everywhere and fast enough for the matrices a script actually hands them, and neither is the path
taken when the native library loaded. Retiring them for a managed Hessenberg-QR eigenvector
back-substitution and a Golub–Kahan bidiagonalization would be a milestone of its own, buying
nothing on any machine that can load a DLL.

`Svd.U` and `Svd.V` are cached, which costs a second copy of each factor for the lifetime of the
decomposition. That is the price of keeping a rectangular accessor on a class whose storage is flat,
and it is the right way round: the accessors are read in loops and the storage is read once.

M91 adds the complex z-routines, the generalized pencils (`dggev`/`dsygvd`), `schur` and `qz`, the
`expm`/`logm`/`sqrtm` products, and reruns the head-to-head suite end to end. `Schur.Factor` does
not balance and is left alone here on purpose: it is M91's to move, and moving it now would change
`schur`, `qz` and `expm` in a milestone that is not about them.

## Testing

`OrthogonalFactorizationProviderTests` (property-based, run against every live backend: ‖A − Q·R‖,
orthonormality, ‖A − U·Σ·Vᵀ‖, ‖A·v − λ·v‖, the pivot record as a permutation, the packed-eigenvector
unpacking, empty matrices, and a padded leading dimension — the last because every one of these
takes an `lda` that may exceed the row count, and a kernel that read the buffer as though it were
compact would silently read the padding as data). `MatlabSpectralProviderM90Tests` covers the
script-visible shapes, options and output counts.

The gate ran the suite under all four combinations of `JGRAPH_LINALG` and `JGRAPH_JGS_PACKED`
(`tools/run-lanes.ps1`): 5,467 tests, both packed lanes clean, both boxed lanes carrying exactly the
57 failures HEAD already had — compared by name and theory argument, not by count. 59 of 59 stress
scripts pass; `stess_38.m`'s balance section asserted that `eig` of a badly scaled matrix was five
digits worse than `eig` of its balanced twin, which stopped being true when `eig` started balancing,
and now asserts that both are exact.

The documented-form counts do not move. `svd` has no rows in the R2021b form catalog, so its new
`'econ'` and `0` options are not counted there; `cond`'s single `cond(A,p)` row was already
accepted, since only the *numeric* spelling of infinity was refused.

## Divergences

- **`cond(A)` of an exactly singular matrix can answer an enormous finite number rather than
  `Inf`.** MATLAB's `cond` divides the largest singular value by the smallest and answers `Inf` only
  when the smallest is exactly zero, so its answer is LAPACK's to give: `dgesdd` returns 1.04e-16
  for the second singular value of `[1 2; 2 4]` where the managed Jacobi returns a clean nought, and
  the two backends therefore report 4.8e16 and `Inf`. The native answer is the MATLAB-faithful one,
  and `cond(A, 1)`, `cond(A, inf)` and `cond(A, 'fro')` detect the singularity exactly on both.
- **`eig` accepts `'nobalance'` and computes the balanced answer anyway.** This was already
  recorded, but it changes character here: before M90 JGraph balanced for neither word, so the
  default was wrong and `'nobalance'` was accidentally right. Now the default matches MATLAB and
  only `'nobalance'` does not.
- **A symmetric matrix's eigenvectors, and any singular or eigen vector, may differ from MATLAB's
  by the sign of a whole column** — and between the two backends likewise. A unit eigenvector is
  determined up to sign, LAPACK fixes the sign by whatever its rotations produce, and no
  normalization layer is imposed. The general eigensolver's vectors are normalized to unit 2-norm
  with the largest component real, which is `dgeev`'s documented convention; the managed fallback
  additionally makes that component positive, which `dgeev` does not promise.
