# ADR 0045 — Stress-test numerics: sparse matrices, eigs, integer classes, the parallel product

## Status

Accepted (M42, 2026-07-29). Companion to [ADR 0044](0044-stress-test-language-fidelity.md) (the
language half of the same stress-test campaign); extends the linear-algebra kernels of
[ADR 0039](0039-matlab-foundational-core.md) and ADR 0040's coverage bookkeeping.

## Context

After M41 the stress scripts still failed on numerics: no `hilb`/`cond`/`sqrtm`/`logm`/`polyval`/
`peaks`, no `ode45`, `det`/`inv`/`exp`/`log`/`sqrt` refusing complex input, no sparse family at all
(one script builds 5000×5000 `sprand` matrices — dense-backed storage is not an option), no integer
classes (`uint8.empty(0, 5)`), and a dense matrix product so slow that 100 iterations of `A*A'` at
n = 1000 would have run for hours. `plot` also rejected MATLAB's name-value pairs and matrix-column
form.

## Decisions

1. **Sparse is a real storage class, not a flag.** `JgsType.Sparse` wraps an immutable
   `JGraph.Numerics/Sparse/CscMatrix` (compressed sparse column — column-major like everything else
   in JGraph). Immutability is what lets MATLAB's copy-on-assign share instances with no copy
   bookkeeping: `CopyForBinding` deliberately leaves the type alone. `class()` reports `double`
   (sparsity is an attribute in MATLAB too); `issparse` tells them apart.

2. **Sparse operators dispatch before any dense machinery.** `ApplyBinary` routes any expression
   with a sparse operand to `JgsBuiltins.SparseBinary`. Sparse±sparse and sparse×sparse stay sparse
   (union merge; column-at-a-time product with a dense accumulator); sparse×dense and dense×sparse
   produce dense results; scalar scaling keeps the pattern. Everything else — including
   scalar-plus-sparse, which MATLAB densifies — errors by name and points at `full()`, rather than
   silently materializing a 25-million-element matrix.

3. **`lu` on sparse is Gilbert–Peierls** (left-looking, partial pivoting, dense working column,
   delayed permutation), two-output only, with the row permutation folded into L exactly like the
   dense `[L, U]` contract. There is no fill-reducing ordering in v1 — random `sprand` patterns at
   stress-script densities factor in seconds; the limitation is documented rather than hidden. A
   structurally empty pivot column (sprand leaves the odd one) factors on with a zero pivot, as
   MATLAB does, instead of erroring: L·U still reassembles A exactly.

4. **`eigs` is one Arnoldi expansion, values and Ritz vectors.** Subspace `max(2k+4, 20)`, the small
   projected problem through the existing `Eigen.Factor`, projected eigenvectors by two rounds of
   shifted inverse iteration (the shift nudged off the Ritz value), Ritz vectors mapped back through
   the basis. No implicit restarts: for the extremal eigenvalues the scripts ask about, one
   generous expansion is accurate, and the code stays a page long. Dense input is converted and
   accepted by the same path.

5. **Integer classes are conversions, not types.** `int8`…`uint64` round half away from zero,
   saturate at the class limits, and map NaN to 0 — MATLAB's conversion semantics — onto JGraph's
   double storage. `uint8.empty(0, 5)` works because member access on a builtin-function value now
   consults a statics table (`JgsBuiltins.TryGetBuiltinStatic`) before insisting the dot was a
   struct access. `class(uint8(5))` still says `double`; the coverage doc records the divergence.

6. **The dense product is a parallel saxpy kernel** (`JGraph.Numerics/LinearAlgebra/DenseProduct`):
   flat column-major buffers, `R[:,c] = Σₖ B[k,c]·A[:,k]` streaming contiguous columns,
   `Parallel.For` over result columns once the work passes a flop threshold. It replaced a
   per-element delegate loop over jagged rows at the interpreter's `*`. The 100×(`A*A'` + four
   elementwise passes) loop at n = 1000 now completes in ~80 s; the stress target was one to two
   minutes.

7. **The complex gaps closed additively.** `HasComplexElements` dispatches `det`/`inv`/`trace` to a
   complex LU, `eig`/`svd` to a new complex QR kernel (`ComplexEigen`: complex Householder
   Hessenberg + Wilkinson-shifted single-shift QR; singular values via a Hermitian real embedding),
   and `exp`/`log`/`sqrt` promote element-wise to complex exactly when the input demands it
   (`MapComplexProducing`). The real fast paths are untouched. `sqrtm` is Denman–Beavers; `logm` is
   inverse scaling-and-squaring over it with a Mercator tail; `cond` reads the existing SVD.

8. **`ode45` is Dormand–Prince 5(4) with FSAL** and proportional step control in
   `JGraph.Numerics/OdeSolvers`. A two-element tspan reports every accepted step; a longer tspan is
   hit exactly by step clipping. `plot` gained MATLAB's trailing name-value pairs (LineWidth, Color,
   LineStyle, Marker, MarkerSize, DisplayName) and matrix-column expansion, which is how `[t, y]`
   results are actually looked at.

## Consequences

- stess_1, 6, 8, 9, 13, and 14 run clean end-to-end (with 2, 4, 7, 11, 16 from M41 — eleven of the
  sixteen scripts); the remainder wait on M43's data types and graphics verbs.
- Sparse indexing (`S(i,j)`), `find` over sparse, sparse `\`, and sparse transpose do not exist yet;
  each errors by name with `full()` as the escape hatch. They can land incrementally behind the same
  dispatch seam.
- `whos` reports a sparse variable as `double (sparse)`; the Data Viewer shows the triplet display
  rather than a grid.
- `MatlabStressM42Tests` pins the kernel invariants: sparse round-trip and operator parity with
  dense, LU reassembly on `magic(6)`, eigs against a known spectrum and the defining residual,
  integer rounding/saturation and `.empty`, `sqrtm`/`logm` round-trips, complex eigenvalue
  trace/determinant, ode45 against the cosine, and the plot forms.
