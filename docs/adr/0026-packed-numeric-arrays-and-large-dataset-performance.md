# ADR 0026 — Packed numeric arrays and large-dataset performance

## Status

Accepted (M22, 2026-07-18).

## Context

The M21 MATLAB-compatibility work made million-sample DSP scripts *possible*; M22 makes them cheap,
targeting ~100M-sample arrays on both a 16 GB laptop and a 64 GB workstation without
`OutOfMemoryException` or GC degradation. Profiling identified the real costs:

1. **JGS boxed every number.** `JgsValue` is a class (~48 bytes per script double, 6x raw); arrays
   were `JgsValue[]`; every elementwise operator allocated a fresh `JgsValue[]` plus one object per
   element through delegates; every builtin round-tripped `JgsValue[]` ↔ `double[]`; the Variables
   pane enumerated and copied whole arrays per refresh; a giant elementwise op was uninterruptible.
2. **Pointer-mode hover scanned every point** of every plot per mouse move (`LinePlot.HitTest` was
   a full O(n) loop — 1.25M distance computations per hover move after M21).
3. **`.graph` saves printed every sample as indented JSON digits** through one giant string.

The rendering path was already flat and decimated (`ArrayDataSeries`, `MinMaxDecimator`) and was
deliberately not redesigned.

## Decision

1. **A new `JGraph.Numerics` project** (the only assembly compiling unsafe code) provides flat
   contiguous storage behind an abstract `NumericBuffer`: `ManagedBuffer` (plain `double[]`, can
   adopt caller-built arrays without copying), `NativeBuffer` (`NativeMemory.AllocZeroed`, invisible
   to the GC, `GC.AddMemoryPressure` + finalizer so abandoned script variables free promptly), and
   `MappedBuffer` (an SSD-backed delete-on-close temp file in `%TEMP%/JGraph/buffers`, paged by the
   OS). `BufferAllocator` picks the backend: small requests (≤1M elements) stay managed; large
   requests go native while `bytes ≤ ½ × (physical − load − 1 GB reserve − outstanding native)`
   (via `GC.GetGCMemoryInfo`, no P/Invoke) and degrade to mapped beyond that — big arrays get
   slower instead of OOM. Overrides: `JGRAPH_BUFFER_MODE=managed|native|mapped`,
   `JGRAPH_BUFFER_MANAGED_MAX`, `JGRAPH_BUFFER_NATIVE_FRACTION`. Orphaned `.jgbuf` files (power
   loss only — crashes are covered by delete-on-close) are swept at app startup.
2. **`PackedMath`: chunked, cancellable SIMD kernels** over buffers via `TensorPrimitives`
   (System.Numerics.Tensors 9.x, the one new package — MathNet/MKL were rejected; the workloads are
   elementwise and FFT, not LAPACK). Operations run in 4M-element chunks with a callback between
   chunks; the interpreter polls its cancellation token there, so Stop interrupts a 100M-element
   statement mid-flight. Kernels whose vectorized form would change results stay scalar on purpose:
   `Pow` (a log/exp kernel returns NaN for negative bases), remainder, and `Round` (midpoint
   semantics); Add/Sub/Mul/Div/Sqrt/Abs are IEEE-exact either way, and transcendentals may differ
   from `Math.*` in the last ulp (accepted, tolerance-tested).
3. **JGS packed arrays with no new `JgsType`.** A homogeneous numeric array keeps
   `Type == JgsType.Array` but stores a `NumericBuffer` (kind Number or Bool — MATLAB logicals) or
   a planar `JgsPackedComplex` (re/im buffers, for spectra) in the reference slot. Exactly one
   `JgsValue` wrapper ever exists per buffer, which preserves the documented reference semantics
   (`X2 = X1` aliases) and lets a heterogeneous write (`x(2) = "hi"`) demote the representation in
   place for every alias at once. `AsArray` **throws** for packed values, so unmigrated call sites
   fail loudly; migrated sites use `ElementAt`/`ArrayLength`/`BoxedElements` (read-only materialize)
   — an unported builtin is never worse than the old all-boxed world. Producers (ranges, numeric
   literals, `Numbers()` builtin outputs, `zeros`/`ones`/`linspace`, `audioread`, fft results,
   slice reads, comparison masks) create packed values; the interpreter's operators, logical
   indexing, slice reads/writes (including the `X(1:k) = 0` spectral-zeroing idiom on packed
   complex), truthiness, and the hot builtins take flat fast paths. A kill switch
   (`JGRAPH_JGS_PACKED=0`) forces the boxed representation; the parity suite runs a script corpus
   in both modes and asserts byte-identical output, and the whole test suite passes either way.
   The range guard rose from 50M to 250M elements; arrays over 1000 elements display as a bounded
   prefix plus count; the data viewer declines arrays over 2M elements; the previous completed
   run's buffers are disposed deterministically when the next run starts (finalizers backstop).
   The plot boundary deliberately keeps one bulk copy: scripts run off the UI thread, and figures
   must never share memory a script can mutate or dispose.
4. **Hover hit-testing is a windowed binary search.** `SeriesHitTester` (shared by line, scatter,
   and stem plots) binary-searches ascending span-backed data for the x-window that could contain a
   hit and scans only those candidates — provably identical results to the legacy full scan (any
   in-tolerance point lies inside the window; out-of-window points cannot tie). Non-ascending data
   keeps the exact legacy scan. The Skia polyline path object is now reused across draws.
5. **`.graph` format version 4.** Series above 10k points persist as base64 blocks of raw
   little-endian doubles (`xsPacked`/`ysPacked` + `count` — System.Text.Json handles `byte[]` ↔
   base64 natively, NaN/±Inf included); small series stay readable JSON arrays; v1–v3 documents load
   unchanged. `Save`/`Load` stream JSON to/from the file, never materializing a giant string.
6. **Signal internals** (public API and zero-dependency status unchanged): the radix-2 FFT uses a
   pooled direct-sincos twiddle table (killing the accumulating `w *= wLen` rounding drift — locked
   in by new large-n accuracy tests), Bluestein and `filter` run on pooled scratch, and WAV decode
   is one format dispatch with bulk span loops instead of a per-sample switch. The DF2T filter
   recurrence stays scalar — it is a feedback loop and honestly not vectorizable.

## Consequences

- The two M21 MATLAB lab scripts run unchanged with drastically lower allocation; a 1.25M-sample
  array costs 10 MB instead of ~60 MB of objects, and spectra 16 bytes/bin instead of a boxed
  `Complex` object each.
- User-visible changes are limited to: bounded echo for >1000-element arrays, the 250M range limit,
  the 2M data-viewer cap, last-ulp SIMD differences, `.graph` v4 (older builds refuse new files;
  old files load fine), and Stop now interrupting giant operations — a pure improvement.
- Benchmarks live in `tests/JGraph.Benchmarks` (packed-vs-boxed elementwise chains, end-to-end
  interpreter runs in both modes, FFT at 2^20 and 1M, 10M-point hover hit-tests, and 1M-point
  save/load); representative short-job numbers are recorded below.
- Risks accepted: the `GC.KeepAlive` contract on buffer spans is centralized in `PackedMath` and a
  review-checklist item; native-memory lifetime rests on finalizers plus deterministic prior-run
  disposal; mapped-file orphans are limited to power loss and swept at startup.

## Measurements (short-job BenchmarkDotNet, AVX-512 machine, recorded at M22)

See `tests/JGraph.Benchmarks` to reproduce; indicative, not contractual:

| Scenario | Boxed / before | Packed / after |
|---|---|---|
| `y = a*2.5 + 1; sin(y)`, 1M elements | 57.7 ms, 32 MB allocated | **1.7 ms, 0 B** (34x) |
| Same chain, 10M elements | 516 ms, 320 MB allocated | **50 ms, 0 B** (10x) |
| `sum` reduction, 10M elements | — | 10.1 ms, 0 B |
| JGS script end-to-end (1M-sample range → sin chain → sum) | 450 ms, 298 MB allocated | **20 ms, 69 MB** (22x) |
| Hover hit-test, 10M-point line (per mouse move) | 54.7 ms full scan | **0.93 ms** windowed (59x) |
| Save / Load 1M-point figure (.graph v4, 20.4 MB packed file) | — | 15.3 ms / 18.1 ms |
| FFT: 2^20 radix-2 / 1M-sample Bluestein | — | 266 ms / 1.53 s |
