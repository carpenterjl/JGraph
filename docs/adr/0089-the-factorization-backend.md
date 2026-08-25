# 0089 — The factorization backend

Date: 2026-08-24 · Status: accepted (M89; the surface grows through M90–M91)

## Context

ADR 0088 put the matrix product on native BLAS and left everything else where it was. The
head-to-head suite's remaining dense rows were still 59×–121× behind MATLAB at n = 2000: `A\b`
3.39 s against 0.057 s, `[L,U,P] = lu(A)` 3.54 s against 0.040 s, `inv(A)` 19.2 s against 0.159 s,
`chol(S)` 1.47 s against 0.017 s.

Two costs, not one. The kernels were unblocked and serial — a textbook right-looking LU, a
dot-product Cholesky, an inverse computed as 2·n³ of solves against an identity where LAPACK's
`dgetri` is 4/3·n³. And the road to them was worse than they were: every verb reached its kernel
through `RectOf` → `JgsMatrix.ToRows`, which calls `ElementAt` once per element and allocates a
heap object for each — four million of them for a 2000-by-2000 matrix — then transposed the jagged
rows into a row-major `double[,]` that the kernel promptly cloned. With a native kernel in place
that marshalling is the larger half of the bill.

## Decision

1. **The provider grows LAPACK's factorization surface.** `DenseLinalg` gains `Getrf`, `Getrs`,
   `Getri`, `Gecon`, `Potrf`, `Trtrs` and `Gels`, all column-major with explicit leading dimensions
   and LAPACK's own argument conventions — including its `ipiv`, which is a record of row
   interchanges rather than a permutation (`DenseLinalg.PermutationOf` converts when something wants
   the permutation). `OpenBlasLinalg` calls the LAPACKE wrappers; `ManagedLinalg` reproduces the
   arithmetic of the kernels these replaced, operation for operation and in the same order, so the
   fallback answers today exactly what it answered before the seam existed.

   `dlange` is deliberately *not* bound, against the M88 plan's list. The matrix 1-norm is a maximum
   of absolute column sums: exact, O(n²), and identical however the rest of the arithmetic is done.
   Binding it would buy nothing and cost a divergence between the two backends in the one number —
   `rcond`'s `anorm` — where the two must agree.

2. **`LuDecomposition` and `Cholesky` hold their factors flat and column-major** and answer through
   `LinalgProvider.Current`. Both gain span and array-adopting entry points
   (`Factor(ReadOnlySpan<double>, n)`, `FactorAdopting(double[], n)`) so a caller that already has
   the matrix in the script's own layout pays one copy — the copy LAPACK's overwriting semantics
   require anyway — and no transpose. The `double[,]` API the older callers use is unchanged; it
   converts at the edges.

3. **One column-major marshalling layer replaces the rectangle road**
   (`JgsBuiltins.LinalgMarshal.cs`): `ColumnMajorOf` reads a value, `FromColumnMajorRect` mints one,
   and `BuildColumnMajor` writes a result straight into the storage that becomes the value. A packed
   operand is a block copy; a boxed or nested one still travels element by element and lands in the
   same layout, so the two representations reach the same kernel with the same numbers — the
   invariant ADR 0088 named, kept by construction rather than by care. `\`, `/`, `inv`, `det`, `lu`,
   `chol`, `rcond` and `linsolve` all travel it.

   `BuildColumnMajor` earns its place on one case: a permutation matrix writes n entries into n² of
   storage, and a packed result's zeros come from the operating system's own zero pages. At n = 2000
   that is 20 ms of zero-filling that simply does not happen.

4. **`chol` reads the triangle it is asked for.** It always read the lower triangle and transposed
   the factor when asked for the upper — which is invisible for a symmetric matrix and wrong for
   anything else, since MATLAB's `chol(A)` reads A's upper triangle and `chol(A, 'lower')` its
   lower. Reading the named triangle is both more faithful and what lets the factor come back
   without a transposing pass. The two directions ask the same question of a symmetric matrix, so
   the managed kernel's factors are exact transposes of one another; a blocked native factorization
   reorders within its last ulps, and the tests say so rather than asserting a mirror that is not
   there.

5. **`rcond` adopts LAPACK's estimator on the native backend.** MATLAB's `rcond` is `dgecon`'s
   estimate — a lower bound on the true reciprocal condition number, never the exact 1/κ₁ this
   engine used to compute by inverting outright. The native path now matches MATLAB; the managed
   path keeps the exact value, and that is a **recorded divergence between the two backends**, the
   first one that is a difference in *answer* rather than in last ulps. `linsolve`'s second output
   goes through the same call, because `linsolve` documents it to be `rcond(A)` and a script that
   subtracts the two expects a zero.

6. **A permutation is applied, not multiplied.** `[L,U] = lu(A)` folded P into L with
   `Linear.Multiply(Pᵀ, L)` — 2·n³ flops, 16 GFLOP at n = 2000, to move rows. It is now a row move.
   Every term but one in each of those sums was a zero times something, so the answer is the same
   for every finite L — and better for the rest, since a native `dgemm` turns `0·Inf` and `0·NaN`
   into NaN and would have spread one bad entry across a whole row.

7. **The native thread count is one per physical core, not one per logical processor.** The M88
   default was `min(ProcessorCount, 16)`, which on a hyperthreaded machine oversubscribes the
   multiply-add units a blocked factorization spends all its time in. Measured on the 8-core /
   16-thread i7-11700F at n = 2000: `dgetrf` 0.074 s on 16 threads against 0.047 s on 8, `dgetri`
   0.182 s against 0.150 s, `A\b` 0.071 s against 0.048 s. Only `dgemm` prefers the wider count, and
   then by about a tenth. `ProcessorTopology` asks Windows for the core count;
   `JGRAPH_BLAS_THREADS` still overrides, and a platform that will not say falls back to
   `ProcessorCount`.

## Consequences

- The head-to-head d01 rows, measured in the script's own sequence (Release, i7-11700F, 8 threads):

  | row | M88 | M89 | MATLAB | ratio |
  | --- | --- | --- | --- | --- |
  | `A*A'` 2000 | 0.086 s | 0.063 s | 0.054 s | 1.17× |
  | `A\b` 2000 | 3.387 s | **0.057 s** | 0.057 s | **1.00×** |
  | `[L,U,P] = lu(A)` 2000 | 3.542 s | **0.078 s** | 0.040 s | 1.95× |
  | `inv(A)` 2000 | 19.245 s | **0.140 s** | 0.159 s | **0.88× — faster** |
  | `chol(S)` 2000 | 1.471 s | **0.038 s** | 0.017 s | 2.24× |

  The M89 gate was every one of those rows within 2× of MATLAB. Three hold; `chol` misses at 2.24×,
  and the reason is measurable rather than mysterious — see below. Residuals improved everywhere at
  the same time: `lu` 1.05e-15 → 1.26e-16, `inv` 1.11e-13 → 3.20e-14, `chol` 1.65e-15 → 1.66e-16.

- `DenseFactorizationBenchmarks` (ShortRun, provider to provider, no marshalling):

  | n | operation | managed | native | speedup |
  | --- | --- | --- | --- | --- |
  | 1000 | getrf | 197.8 ms | 13.3 ms | 14.8× |
  | 1000 | getrf+getri | 1,156.8 ms | 27.9 ms | 41.4× |
  | 1000 | getrf+getrs | 188.3 ms | 13.4 ms | 14.0× |
  | 1000 | potrf | 161.4 ms | 9.1 ms | 17.8× |
  | 2000 | getrf | 2,520.2 ms | 41.6 ms | 60.6× |
  | 2000 | getrf+getri | 7,569.5 ms | 149.1 ms | 50.8× |
  | 2000 | getrf+getrs | 2,059.0 ms | 49.0 ms | 42.0× |
  | 2000 | potrf | 1,496.4 ms | 36.5 ms | 41.0× |

- **The honest miss.** `dpotrf` at n = 2000 is 36.5 ms for 2.67 GFLOP — 73 GFLOPS, against the
  157 GFLOPS MATLAB's whole `chol` implies. `dgetrf` is 41.6 ms for 5.33 GFLOP, or 128 GFLOPS, where
  MATLAB's whole `lu` at 0.040 s implies something over 200. The marshalling above the kernels is
  measured at about 10 ms in both cases and cannot account for the difference: OpenBLAS's Cholesky
  and LU simply trail MKL's, by about 2× and 1.6×. This is exactly the risk ADR 0088 recorded when
  it chose OpenBLAS over MKL, arriving one milestone earlier than expected — it was written down
  against the SVD. Nothing in the marshalling layer will close it; only a different library, or a
  blocked factorization written over the BLAS-3 kernels ourselves, would.

- `Cholesky.Factor(double[,])` keeps its lower-triangle-reading meaning for its non-script callers
  (the multivariate distributions, the definite pencil in `eig(A, B)`); the triangle is now an
  argument rather than an assumption.

- The `Gels` path means `\` on an over- or under-determined system is one blocked QR natively; the
  managed backend routes the same call to the Householder factorization the operator always used,
  through `Linear.LeastSquaresManaged`, so the fallback's answers are unchanged.

- M90 binds `dgeqrf`/`dorgqr`/`dgeqp3`, `dgesdd` and `dsyevd`/`dgeev`, retiring the O(n⁴)
  eigenvector recovery behind the 3124× row, and runs the test-convention audit that eigenvector
  signs and orderings need. M91 adds the complex z-routines, the generalized pencils, and `schur`/
  `qz`, then reruns the head-to-head suite end to end.

## Divergences

- **`rcond` estimates the reciprocal condition number rather than computing it, on the native
  backend.** That is what MATLAB answers — `dgecon` returns a lower bound, never the exact 1/κ₁ —
  and what `linsolve`'s second output now reports alongside it. The managed fallback keeps the exact
  value it always computed, so this is also the first difference between the two backends that is a
  difference in *answer* rather than in last ulps: for `[4 1; 1 3]` the exact reciprocal is 0.42642
  and the estimate 0.42813.
- **`chol` reads the triangle it is asked for**, upper by default and lower on request, where it
  used to read the lower triangle whatever it was asked and transpose the factor. This is MATLAB's
  documented behaviour and only visible on a matrix whose two triangles disagree — which is not a
  matrix Cholesky is defined on.
- **A square system whose factorization meets an exact zero pivot is refused**, where MATLAB warns
  and answers with infinities. JGraph has always refused a singular square solve; what changed in
  M89 is that a native factorization finds the zero. `[1 2 3; 4 5 6; 7 8 9] \ [1;1;1]` left
  1.1e-16 of rounding noise on U's diagonal under the old kernel and answered as though the system
  were solvable; it is exactly singular, and is now said to be.
