# 0091 — The complex and pencil backend

Date: 2026-08-25 · Status: accepted (M91; completes the dense LAPACK arc of ADR 0088)

## Context

ADR 0090 put the orthogonal factorizations on LAPACK and left three families where they were: the
complex operations, the generalized (pencil) eigensolvers, and the Schur machinery.

Their state was uneven in a way the head-to-head suite never showed, because none of them has a
d01 row. The complex product was a boxed triple loop over `JgsValue` elements. Complex `det` and
`inv` ran a complex LU that lived in the *scripting* layer, below no seam at all. Complex `A\B`
and `A/B` did not exist — they fell through to the real reader and were refused with an error
message that named `'*'`. `[V, D] = eig(A)` and `[U, S, V] = svd(A)` for a complex A were refused
outright, and the complex singular values that *were* answered came from the eigenvalues of the
Gram matrix AᴴA through a real 2n-embedding — squaring the condition number, so a singular value
of 1e-8 came back with half its digits gone. `eig(A, B)`, `qz`, `schur` and `ordschur` were all
correct and all hand-rolled, single-threaded, on `double[,]` rectangles.

MATLAB answers every one of these through the same LAPACK drivers the bundled OpenBLAS already
exports. ADR 0026 rejected a native math dependency on scope grounds ("the workloads are
elementwise and FFT, not LAPACK") that ADR 0088 found no longer held and reversed; this milestone
spends the tail of that reversal — the symbols were in the DLL all along.

## Decision

1. **Eleven new bindings, one new provider surface.** `DenseLinalg` grows `Ggev`, `Sygvd`, `Gees`,
   `Gges`, `Trsen`, and the z-family `Zgemm`, `Zgetrf`, `Zgetrs`, `Zgetri`, `Zgeev`, `Zgesdd`
   (with `zgesvd` bound as the convergence fallback). Complex crosses the boundary as
   `Span<Complex>` — `System.Numerics.Complex` is two sequential doubles, which *is* LAPACK's
   interleaved `complex*16`, so a pinned span goes straight through with no packing layer.

2. **The managed backend answers everything the native one does.** The Francis iteration, the QZ
   iteration and the block-exchange reorder stay where they were and become the managed lane's
   kernels (`Schur.FactorManaged`, `GeneralizedSchur.FactorManaged`, `Schur.ReorderManaged`),
   reached by `ManagedLinalg` wrappers that marshal rectangle ↔ flat. The genuinely new managed
   code is complex: an LU trio in LAPACK's conventions (pivoting on cabs1, |re| + |im|, so the two
   backends pick the same rows), a complex balancing pass and inverse iteration behind `Zgeev`,
   and a complex one-sided Jacobi behind `Zgesdd` — which never forms the Gram matrix, so the
   managed lane's complex singular values *gained* accuracy in the same milestone that made the
   native lane fast.

3. **Fronts route, kernels answer.** `Schur.Factor`/`Reorder`, `GeneralizedSchur.Factor`,
   `ComplexEigen.Values`/`Factor`/`SingularValues`/`Svd`, the new `ComplexLinear`
   (product/solve/det/inverse), and `Eigen.PencilSpectrum`/`PencilFactor`/`SymmetricPencil` all
   call `LinalgProvider.Current`. The scripting layer's own complex LU was deleted, not seamed —
   a kernel below no seam was the thing this arc exists to remove.

4. **`eig(A, B)` keeps its routing policy and changes its engine.** The symmetric-definite pair
   still goes through Cholesky — now LAPACK's `dsygvd`, whose vectors arrive already scaled so
   Zᵀ·B·Z = I — and everything else through `dggev`. Eigenvector normalization is `dggev`'s
   (largest component's |re| + |im| = 1), which is what MATLAB hands back for this form; the
   managed lane renormalizes its B⁻¹·A-route vectors to the same convention so the two backends
   agree on more than direction.

5. **An eigenvalue at infinity stays exactly infinite.** The managed QZ promises that a singular
   B answers β = 0 exactly (ADR 0076); a blocked native iteration leaves β at rounding scale
   instead, which would make `eig(A, B)` answer 1e16 where it has always answered Inf. The fronts
   apply the managed kernel's own snap rule — |β| ≤ 1e-12·(1 + ‖B‖) collapses to zero — so the
   promise survives the backend swap.

6. **Complex `\` and `/` exist now, square only.** `A\b` factors through `zgetrf`/`zgetrs`; `A/B`
   arrives as the plainly-transposed problem (no conjugation — MATLAB's identity is
   (Bᵀ\Aᵀ)ᵀ). A rectangular complex system is refused with a message that names the missing
   complex least-squares solver rather than a wrong operator.

7. **`expm`, `logm` and `sqrtm` ride the provider they already touched.** `expm` was on
   `Linear.Multiply`/`Solve` (gemm-gated since M88) all along; `logm`'s Mercator-series power
   multiply ran a private naive `MatMul`, which is deleted in favor of `Linear.Multiply`;
   `sqrtm`'s Denman–Beavers iteration was already on the provider's LU and inverse.

8. **`ordqz` stays managed on both lanes.** `dtgsen` is not bound; the one-eigenvalue-at-a-time
   generalized reorder operates on whatever valid generalized Schur form `dgges` or the managed
   QZ produced. It is the one Schur-family operation still answered by hand, and it is recorded
   here rather than left to be discovered.

## Consequences

- Complex `A\b`, `A/B`, `[V, D] = eig(A)`, `[V, D, W] = eig(A)`, `[U, S, V] = svd(A)` (with
  `'econ'` and `0`) now exist for complex arguments — four refusals closed, each answering in
  MATLAB's shapes.
- Complex singular values no longer square the condition number on either backend: σ = 1e-8 in a
  2×2 comes back to twelve digits where the Gram route kept about eight.
- `schur`, `ordschur`, `qz`, `eig(A, B)`, `det`/`inv` of complex, and the complex product all
  answer through the same provider dispatch as the rest of the dense surface; `JGRAPH_LINALG`
  selects the engine for every one of them.
- The Schur-family conventions are LAPACK's where a backend computes them: T's block order and
  eigenvector phase may differ between lanes, which is why every new test asserts residuals and
  structure rather than element values.

## Testing

- `PencilAndComplexProviderTests` runs the new provider surface against both backends by name:
  Zgemm against the hand-rolled product, LU solve/inverse residuals, singular complex matrices
  reported through info codes, complex eigenpair residuals and trace reproduction, the complex
  SVD reassembling tall/wide/square with a unitary U, the small-σ accuracy case the Gram route
  failed, pencil eigenvalues checked through det(A − λB) on a real embedding, pencil eigenvector
  residuals, `Sygvd`'s ordering and B-normalization and its indefinite-B info code, Schur and QZ
  reassembly with structure asserts, and `Trsen` bringing a chosen eigenvalue to the top with the
  similarity preserved.
- `MatlabComplexAndPencilM91Tests` covers the script surface: complex `\`/`/` (square, vector
  leniency, singular and rectangular refusals by message), complex `eig` in one, two and three
  outputs, complex `svd` full and economy shapes, `det`/`inv` agreement, `schur`/`ordschur`/`qz`
  residual and convention checks, both `eig(A, B)` routes, the singular-B Inf-values-but-no-
  vectors contract, and the `logm(expm(X))`/`sqrtm` round-trips.
- The full suite runs in all four `JGRAPH_LINALG` × `JGRAPH_JGS_PACKED` lanes, plus the 59-script
  stress sweep and the four coverage verifiers.

## Divergences

- **Complex `A\B` is square-only**: the rectangular case needs a complex least-squares solve
  (`zgels`) that is not bound, and the refusal names it. MATLAB answers the least-squares
  solution.

ADR 0076's two divergences in this territory — `eig` of a complex pencil refused, `ordqz`
refusing a 2-by-2 block — stand unchanged and stay recorded there; `zggev` and `dtgsen` are the
bindings that would close them.
