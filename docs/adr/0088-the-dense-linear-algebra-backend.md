# 0088 — The dense linear-algebra backend

Date: 2026-08-24 · Status: accepted (M88; the surface grows through M89–M91)

## Context

The head-to-head suite (`JGraph_demo_workspace\head2head`) put the same eight MATLAB-syntax
scripts through both engines on the same machine. JGraph won every figure build, every export,
and startup — and lost dense linear algebra by 28×–3124×: `eig` at n = 400 took 18.7 s against
MATLAB's 0.006 s, `svd` at 800 took 40.9 s against 0.032 s, the n = 2000 matmul 1.5 s against
0.054 s. The gap is structural, not a tuning miss. MATLAB's math is Intel MKL — native,
multithreaded, AVX-tuned BLAS/LAPACK — while our kernels are textbook unblocked scalar loops
over row-major `double[,]`, with exactly one `Parallel.For` in the whole engine
(`DenseProduct`'s column loop) and an O(n⁴) eigenvector recovery behind the worst row.

ADR 0026 rejected MathNet/MKL because "the workloads are elementwise and FFT, not LAPACK". That
premise was true in M22 and died over the following sixty milestones: ADR 0039/0045 grew a full
dense surface — `eig`, `svd`, `qr`, `lu`, `chol`, `schur`, `qz`, `expm` — that scripts now lean
on at sizes where kernel quality is the whole story. The dependency policy this repo actually
practices is *isolate, don't forbid*: SkiaSharp lives in exactly three projects, pythonnet in
one, and `JGraph.Numerics` was already the designated unsafe-code island.

## Decision

1. **Bundle OpenBLAS (BSD-3) and route dense linear algebra through a provider seam.**
   `native\win-x64\libopenblas.dll` is the official 0.3.34 LP64 release, committed with its
   license, source URL and SHA256s (`native\win-x64\SOURCE.md`). Its import table names only
   `KERNEL32.dll` and `msvcrt.dll`, so the one file is the whole dependency. MKL was considered
   again and passed over: OpenBLAS is redistributable without a license wrinkle, a quarter the
   size, and within a small factor of MKL everywhere that matters here (the recorded risk: its
   SVD trails MKL more than its gemm does — M90 will measure and report honestly).

2. **The seam is `DenseLinalg` in `JGraph.Numerics.LinearAlgebra`** — span-based, flat
   column-major, explicit leading dimensions, LAPACK argument conventions. `ManagedLinalg`
   wraps the hand-rolled kernels (always available); `OpenBlasLinalg` calls CBLAS/LAPACKE
   through `[LibraryImport]` bindings (`Native\OpenBlasNative.cs`) that only ever load through
   `OpenBlasLoader`'s resolver. `LinalgProvider.Current` is the process-wide choice: native
   when the library loads, managed otherwise, `JGRAPH_LINALG=managed|native` forcing either —
   the same switch-plus-env pattern as `JgsPacking`. Forcing `native` on a machine where the
   load fails selects a backend that *throws* on first use, so a parity lane can never silently
   run managed twice. `version('-blas')`/`version('-lapack')` report the live status
   (`"OpenBLAS 0.3.34 (native, 16 threads)"` or the fallback reason), replacing the M-era
   "computes its own linear algebra" text.

   The contract deliberately says nothing about *where* work happens; a future GPU backend is
   another subclass, not a new seam.

3. **The provider replaces kernel internals, not call sites.** `DenseProduct.ColumnMajor` and
   `Linear.Multiply` now answer through `Current`, so every existing caller — boxed or packed,
   builtin or operator — gets the native kernels without changing. The packed/boxed
   representation axis and the provider axis stay orthogonal: both representations funnel into
   the same `Current`, which is what keeps the M22 parity suite's byte-identical guarantee
   intact by construction.

4. **`JgsLinalg` is the zero-copy bridge** (`JGraph.Scripting\Jgs\JgsLinalg.cs`) — the one
   place that combines `JgsValue` with the provider, owning the `GC.KeepAlive` lifetime
   discipline the same way `PackedMath` does for elementwise ops. Packed storage is already
   flat column-major — precisely BLAS's layout — so `*` on two packed real matrices reads both
   buffers in place and writes one fresh result buffer: zero copies against the boxed path's
   four-plus-boxing. A reoriented vector costs nothing (a contiguous vector is the same bytes
   either way up), and the bridge mirrors the boxed path's shape rules and error texts exactly.
   The same file gave the transpose operator a blocked span fast path: `A'` at 2000² was 0.43 s
   of per-element wrapper allocation — six times the multiply it feeds — and is now ~15 ms.

5. **Thread count is fixed at load** — `JGRAPH_BLAS_THREADS`, defaulting to ProcessorCount
   capped at 16 — so native results are identical run to run. Oversubscription is structural
   non-issue: the one managed `Parallel.For` this replaces *was* the parallelism.

6. **`X'*X` and `X*X'` are recognized syntactically and computed as symmetric rank-k products**
   (`cblas_dsyrk`, one triangle computed and mirrored) — the way MATLAB recognizes its own syrk
   patterns. This is not an optimization first; it is a correctness requirement the stress suite
   caught on day one: the old saxpy kernel made `A'*A` exactly symmetric *by accident* (the two
   mirrored elements sum the same products in the same order), a blocked native gemm does not,
   and `ldl(A'*A)` refused its own input (`stess_6.m`). Mirroring a computed triangle makes the
   result exactly symmetric *by construction* — and halves the flops. The recognition is
   identifier-only (`A'*A` where both sides name the same variable; reading an identifier twice
   is pure), which is the same syntactic scope MATLAB documents.

## Consequences

- `A*A'` at n = 2000: 1.50 s → 0.086 s (gemm alone 0.057 s warm; MATLAB 0.054 s). The
  M88 gate (≤ 0.1 s) holds. `A*v` at 2000: 0.098 s → 0.004 s.
- `DenseLinalgBenchmarks` (ShortRun, i7-11700F, provider-to-provider, no marshalling):

  | n | managed gemm | native gemm | speedup |
  | --- | --- | --- | --- |
  | 100 | 160 µs | 29.7 µs | 5.4× |
  | 400 | 7.87 ms | 0.66 ms | 11.9× |
  | 1000 | 99.8 ms | 10.8 ms | 9.2× |
  | 2000 | 811 ms | 64.5 ms | 12.6× |
- A machine without the DLL (or a non-x64 process) degrades to the managed kernels silently,
  with the reason one `version('-blas')` away. The DLL rides `CopyToOutputDirectory` items that
  flow transitively to every host, the WiX staging included.
- Uninterruptible native calls are no regression: the managed linalg kernels never polled
  cancellation either, and the native calls are 30–3000× shorter than what they replace.
- LAPACKE's LP64 `int` sizes are safe: `BufferAllocator` already caps element counts at
  `int.MaxValue`.
- M89 binds LU/chol/solve/inv/rcond; M90 qr/svd/eig (retiring the O(n⁴) eigenvector path);
  M91 the complex z-routines, the generalized pencils, and schur/qz. Each extends
  `DenseLinalg`, delegates the corresponding `Factor` internals, rewires its hot builtins
  through `JgsLinalg`, and records its measured row in `DenseLinalgBenchmarks`.

## Divergences

Both are *toward* MATLAB, and both are between the two backends rather than between JGraph and
MATLAB — the native one is the default wherever the library loads.

- **BLAS multiplies every element, where the managed saxpy kernel skips zero factors.** `0·Inf` and
  `0·NaN` therefore contribute NaN on the native path exactly as they do in MATLAB, and are silently
  dropped on the managed fallback.
- **A blocked native kernel reorders accumulation within the last ulps.** Small integer-valued
  products are still exact, and the provider parity tests hold the rest to 1e-12 relative.
