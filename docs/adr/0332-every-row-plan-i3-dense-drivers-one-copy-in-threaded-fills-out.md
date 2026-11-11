# ADR 0332 — The every-row plan's I3: the dense drivers take one copy in and write their outputs in one threaded pass

## Status

Accepted, 2026-11-10. Stage I3 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 11). **Partly done**: six of the twelve rows are at or under R2025b's median headless
(`d01_lu_2000`, `d09_polyfit_2M_deg8`, `d10_expm_400`, `d10_schur_400`, `d10_balance`, and
`d10_norms_400` level), `d10_hess_400` is level at steady state, and five are not: `d01_chol_2000`,
`d01_svd_800`, `d01_qr_1200`, `d10_null` and `d10_orth`. What is left in those five is the LAPACK
driver itself — OpenBLAS's against R2025b's MKL — and a thread sweep says the thread count is not
it. Every CHK line is byte-identical.

## Context

The GUI round (ADR 0322) left twelve rows to I3, 0.12 s of excess between them, each 1.1 to 1.9
times R2025b: `d01_lu_2000`, `d01_chol_2000`, `d01_svd_800`, `d01_qr_1200`, `d09_polyfit_2M_deg8`,
`d10_expm_400`, `d10_null`, `d10_orth`, `d10_hess_400`, `d10_schur_400`, `d10_balance` and
`d10_norms_400`. The factorizations themselves already ran in OpenBLAS; ADR 0321 had measured where
lu's time went (37 ms in the copy and `dgetrf`, 19 ms in the three output fills, against R2025b's
37 ms for the whole). Reading each verb down to its LAPACK call found the same thing in every one:
work around the driver that the driver did not need.

- **Fills on one thread onto fresh pages.** lu's L, U and P, qr's R, chol's cleared triangle and
  every packed input's private copy were serial loops over 12-32 MB of newly allocated memory, so
  each page faulted in one at a time on the one thread writing it.
- **chol copied the whole matrix and then cleared half of it.** `dpotrf` reads one triangle; the
  other was copied in, carried through the factorization untouched, and zeroed after it — two
  passes over 16 MB at n = 2000 that changed nothing.
- **qr copied the reflectors to expand Q.** `[Q, R] = qr(A)` copied the 1200-square factored array
  into a zeroed one before `dorgqr`, then built R element by element with index arithmetic.
- **The singular values were taken in a second copy.** `svd(A)` and `norm(A)` copied the matrix to
  marshal it and `Svd` copied it again for the driver to destroy.
- **The norms copied to read.** `norm(M, 'fro')`, `norm(M, 1)` and `norm(M, Inf)` copied the matrix
  before summing it, and the Inf-norm then walked it across its rows, a cache line per element.
- **polyfit built its design matrix twice.** A 2,000,000-by-9 Vandermonde was written row-major
  (144 MB) and `QrDecomposition.Factor` transposed it into a second 144 MB array; the right-hand
  side went through a `double[n, 1]` and a flat copy of that. And the long thin `dgeqrf` ran on one
  OpenBLAS thread, because the thread policy sizes a factorization by its short side, here 9.
- **expm, hess, schur and balance worked on rectangles.** Each of `expm`'s eight products at 400
  square transposed its two operands into column-major and its answer back (`Linear.Multiply`), and
  the solve transposed four more; `hess` and `schur` transposed in and out around one driver call;
  `balance` four times around a column-major kernel. `hess(M)` with one output also formed Q
  (`dorghr`), as much work again as the reduction, and threw it away.
- **null and orth went through three rectangles** — the factor's, the kept columns', the answer's —
  for columns that lie contiguous in the factor as LAPACK returns it.

## Decision

Every dense driver gets exactly one copy of its input, made where it is needed and threaded when
large, and writes each output straight into the storage that becomes the script value, in one pass,
threaded when large. No answer moves: every road computes the same numbers in the same order, and
the tests hold each new road to the old one bit for bit.

### `FactorFills`: threaded copies and triangle fills (Numerics)

`FactorFills.Copy`, `Upper`, `Lower` and `ClearOtherTriangle` cut a fill by columns into 64 blocks
(1 MB pieces for a plain copy) across `ParallelKernels` from a million elements, 8 MB; below that
they run on the calling thread. They move elements and compute nothing, so the cut cannot change a
bit. `ColumnMajorOf`'s packed branch — every factorization's private copy — uses `Copy`; lu's L and U
(including the two-output form's row scatter) use `Lower` and `Upper`; qr's R uses `Upper`;
Cholesky's clearing uses `ClearOtherTriangle`.

### chol copies the triangle it reads

A packed matrix is copied a triangle at a time with zeros written over the other triangle in the
same threaded pass, and `Cholesky.FactorAdoptingTriangle` skips the clearing afterwards. LAPACK and
the managed kernel read only the triangle asked for, so the factor is the same to the bit; a test
puts NaN in the other triangle to prove nothing reads it.

### qr writes R first, then expands Q where the reflectors lie

`QrDecomposition.WriteR` writes R into the result's own (zeroed) storage, and `TakeQColumnMajor`
runs `dorgqr` over the factored array itself whenever that array is exactly Q's size — a square
matrix's Q, or a tall matrix's economy Q — and otherwise copies the reflectors as one contiguous
block. The expansion reads only the reflectors and overwrites everything it reads afterwards (both
the LAPACK and the managed `Orgqr`), so Q is the same to the bit; after it the decomposition refuses
R and the solves.

### The singular values are taken in the caller's copy

`Svd.TrySingularValuesAdopting` runs `dgesdd` in the array the verb already copied; when the
divide-and-conquer driver fails to converge (it has destroyed the array by then) the verb copies
again and takes `Svd.SingularValues`' road, which keeps a pristine matrix for `dgesvd`. `svd(A)`,
`norm(A)` and `norm(A, 2)` use it.

### The norms read the matrix where it lies

`norm(M, 'fro')`, `norm(M, 1)` and `norm(M, Inf)` of a packed matrix read its storage in place. The
sums are the copying road's, in its order: the elements in storage order for Frobenius, each column
top to bottom for the 1-norm, and each row left to right for the Inf-norm — now kept as one running
sum per row while the matrix is read once down its columns.

### polyfit builds the design column-major and factors it where it lies

`VandermondeColumnMajor` writes the m-by-(n+1) design in one pass threaded by rows, each row's
powers the same products in the same order; `QrDecomposition.FactorAdopting` factors it in place and
`SolveColumnMajor` solves y's own copy. The residual for `S.normr` remakes each row's powers rather
than keeping a 144 MB copy of the design. The factorization and the solve are the same LAPACK calls
on the same numbers.

### A long thin factorization is sized by its footprint

`NativeThreads.FactorSize(m, n)` gives `dgeqrf` and `dorgqr` their short side, as before, except for
a matrix of at least 4M elements (32 MB), which is sized √(m·n), the side of the square with the
same footprint: polyfit's 2,000,000-by-9 design is 4,242 and gets the machine where it got one
thread. `dormqr` keeps C's short side unless its reflectors are such a block. A square matrix is
sized exactly as before. `JGRAPH_TALL_FACTOR_THREADS=0` turns it off; a test solves a
500,000-by-9 least-squares problem both ways and gets the same bits.

### expm, hess, schur and balance run column-major

`MatrixFunctions.Exponential(ReadOnlySpan<double>, n)` is the same Padé(6,6) scaling and squaring —
the same row sums, the same products through the same provider call at the same sizes (with
`Linear.Multiply`'s own native-or-loop rule), the same solve — over column-major arrays; the
rectangle overload now converts and calls it, so there is one implementation. `hess` of a packed
matrix runs `Hessenberg.TryReduceColumnMajor`, which forms Q only when Q is an output; `schur` runs
`Schur.FactorColumnMajor` (the same `dgees` call); `balance` balances its copy in place and hands it
back. A test holds the column-major exponential to the old rectangle algorithm, kept verbatim in the
test, at the naive-loop and the `dgemm` sizes.

### null and orth select columns where they lie

`SelectColumnsColumnMajor` copies the kept singular vectors as whole columns of the column-major
factor.

## Measured

`head2head_v3\measure_i3.ps1` and `compare_i3.py`, the three scripts unchanged, one session per
condition (`runs\i3-first`): three headless sessions of this build and three of the build before it
(a7b317cf) alternated, one app session, one eight-pass headless session of each build read over
passes 4 to 8 (steady), and the split probe on both builds, in R2025b, with OpenBLAS pinned at 1, 2,
4 and 6 threads, and with the long-thin sizing off. Medians of the timed passes, in seconds.

| Row | R2025b | before | I3 | I3 app | I3 steady | before, steady | |
|---|---|---|---|---|---|---|---|
| `d01_lu_2000` | 0.047 | 0.062 | 0.044 | 0.063 | 0.042 | 0.050 | met headless and steady |
| `d01_chol_2000` | 0.014 | 0.026 | **0.023** | 0.026 | 0.025 | 0.029 | over |
| `d01_svd_800` | 0.046 | 0.073 | **0.072** | 0.070 | 0.087 | 0.074 | over |
| `d01_qr_1200` | 0.055 | 0.094 | **0.118** | 0.142 | 0.090 | 0.093 | over; bimodal on both builds |
| `d09_polyfit_2M_deg8` | 0.229 | 0.254 | 0.140 | 0.142 | 0.142 | 0.253 | met |
| `d10_expm_400` | 0.020 | 0.030 | 0.014 | 0.019 | 0.015 | 0.027 | met |
| `d10_null` | 0.015 | 0.018 | **0.016** | 0.019 | 0.018 | 0.019 | over |
| `d10_orth` | 0.017 | 0.019 | **0.018** | 0.018 | 0.018 | 0.017 | over |
| `d10_hess_400` | 0.007 | 0.012 | **0.008** | 0.008 | 0.007 | 0.012 | level at steady state |
| `d10_schur_400` | 0.029 | 0.029 | 0.025 | 0.029 | 0.025 | 0.028 | met |
| `d10_balance` | 0.002 | 0.003 | 0.001 | 0.001 | 0.000 | 0.002 | met |
| `d10_norms_400` | 0.009 | 0.011 | 0.009 | 0.011 | 0.009 | 0.011 | level |
| `d01_linalg` wall | 1.671 | 1.011 | 0.973 | 1.160 | 0.938 | 0.959 | |
| `d09_polyinterp` wall | 12.670 | 0.750 | 0.662 | 0.987 | 0.643 | 0.728 | |
| `d10_matfun` wall | 0.949 | 0.405 | 0.390 | 0.429 | 0.381 | 0.383 | |

`d01_qr_1200`'s passes fall in two groups on both builds, near 0.085 and near 0.15 (this build
0.072-0.157, the one before 0.083-0.160), so its median says which group three passes landed in more
than what the code costs; the warm split below is 79 ms against 83.

The split probe (`probes\instrumented\split_i3.m`, mean milliseconds a call over five calls after a
warming one, one session per condition; the pinned columns are this build with
`JGRAPH_BLAS_THREADS`):

| Piece | I3 | before | R2025b | 1 thread | 2 | 4 | 6 | tall sizing off |
|---|---|---|---|---|---|---|---|---|
| `A + 0`, 2000 square | 2.6 | 3.5 | 1.5 | 3.9 | 2.8 | 3.9 | 3.5 | 3.5 |
| `lu(A)`, one output | 45.2 | 49.2 | 28.4 | 110.3 | 68.8 | 47.0 | 50.7 | 46.1 |
| `[L, U, P] = lu(A)` | 51.9 | 60.1 | 37.4 | 117.6 | 69.7 | 50.6 | 50.8 | 53.8 |
| `chol(S)` | 24.3 | 30.4 | 15.1 | 53.3 | 38.2 | 26.5 | 22.1 | 22.6 |
| `svd(A(1:800, 1:800))` | 77.4 | 74.3 | 25.6 | 66.8 | 69.2 | 66.5 | 71.9 | 74.4 |
| `qr(Aq)`, R only | 38.5 | 46.2 | 18.8 | 54.1 | 45.5 | 40.6 | 40.8 | 39.7 |
| `[Q, R] = qr(Aq)` | 79.0 | 83.1 | 36.0 | 110.8 | 95.7 | 85.0 | 82.7 | 82.9 |
| `polyfit(x, y, 8)`, 2M | 137.2 | 266.3 | 166.0 | 150.1 | 141.6 | 144.0 | 139.8 | 146.1 |
| `expm(G)`, 400 | 21.4 | 35.7 | 13.9 | 29.0 | 20.9 | 17.8 | 17.5 | 20.2 |
| `[Ub, Sb, Vb] = svd(B, 'econ')` | 17.8 | 17.9 | 8.9 | 16.0 | 22.7 | 23.3 | 26.1 | 18.4 |
| `null(B)` | 18.5 | 16.7 | 8.7 | 15.8 | 22.4 | 23.1 | 25.3 | 18.5 |
| `hess(M)` | 8.1 | 11.8 | 4.4 | 7.3 | 8.5 | 9.4 | 12.3 | 7.0 |
| `schur(M)` | 26.8 | 31.0 | 21.3 | 25.4 | 32.5 | 34.4 | 47.0 | 26.3 |
| `balance(M)` | 0.5 | 3.3 | 1.3 | 0.5 | 0.5 | 0.5 | 0.5 | 0.5 |
| `norm(M, 2)` | 9.5 | 9.6 | 5.8 | 8.8 | 9.3 | 10.2 | 10.7 | 9.1 |
| the 'fro', 1 and Inf norms | 0.7 | 3.2 | 0.3 | 0.6 | 0.5 | 0.6 | 0.6 | 0.5 |

What the probe says. The copies and fills this stage took out are visible where they were most of
the call — `balance` 3.3 to 0.5 ms, the three cheap norms 3.2 to 0.7, `hess` 11.8 to 8.1, `expm` 35.7
to 21.4, `polyfit` 266 to 137 (the long-thin sizing is 9 ms of it) — and small where the driver is
most of the call. In the five rows still over, the driver is R2025b's to win: the 800-square
singular values are 67-77 ms on every thread count against MKL's 26, the 600-by-300 economy SVD
16-26 against 9, `chol` 22 at its best against 15, `[Q, R]` 79 against 36. The thread policy already
picks the best count for each at its size (one thread for the 300- and 400-order spectral drivers,
all six for the 1200- and 2000-order factorizations), so a different policy buys nothing; what would
is a faster LAPACK under the same calls (open item 274). The probe's checksum is the same in R2025b
and in all eight JGraph conditions.

### Checks

- All 58 CHK lines of `d01_linalg`, `d09_polyinterp` and `d10_matfun` are byte-identical in all
  nine sessions to the build before's (the three that differ from the GUI round, `d10_logm_roundtrip`,
  `d10_pinv_resid` and `d10_sqrtm_resid`, are I4's, ADR 0329), and all 244 CHK lines of the sixteen
  benchmark scripts are identical between this build and the one before it (a run of every script
  on both, for the bits and not for time). The polyfit coefficients are the same with the long-thin
  sizing on and off.
- `DenseDriverFillsI3Tests` (Numerics, 42 cases): the copy, the upper trapezoid, the lower triangle
  with and without a unit diagonal, clearing and a row scatter, and the other triangle cleared,
  against plain loops at one thread and at many, from one element to 1100 square; Cholesky from a
  copied triangle against the factor of the whole copy, definite and failing, both directions; Q
  expanded in place against Q from a copy and R written against the old element-by-element R, for
  square, tall, wide and 1200-square matrices, full and economy, and the spent decomposition
  refusing R; the singular values in the caller's copy against `SingularValues`; the column-major
  exponential against the pre-I3 rectangle algorithm, kept verbatim in the test, at the loop and the
  `dgemm` sizes; the column-major Hessenberg and Schur forms against the rectangle roads; the
  footprint sizing's values and switch; a 500,000-by-9 least-squares solve with the sizing on and
  off, the same bits.
- `DenseDriversI3Tests` (12): lu's one, two and three outputs and `'vector'` held to each other
  exactly, at 7 and at 1100 square; chol's factors exactly triangular, NaN in the other triangle
  changing nothing, the failure order and partial factor; qr's R the same from one and two outputs,
  Q·R and QᵀQ, economy, pivoted and pair forms, every shape; `norm(A)` equal to `svd(A)`'s first value
  and the 1-, Inf- and Frobenius norms equal to their loops; hess and schur the same whichever
  outputs are asked, the complex Schur form, the refusal's words; balance's scaling exact powers of
  two; null and orth equal to the singular factors' own columns; polyfit's coefficients and `normr`
  equal to the pre-I3 algorithm, run beside it in the test, on 50 points and on 500,000 (long-thin).
- Gate: the CLI's Debug build; the 2,747 tests of the linear-algebra areas (all of
  `JGraph.Tests.Numerics` and the linear-algebra, decomposition, polynomial, matrix-function,
  statistics and solver classes) in all four lanes, native and managed backend by packed and boxed
  storage; the 285 parity fixtures in packed and in boxed storage; the stress suite 88 of 89 (only
  `stess_40`, open item 254); the seven `verify-*.py`; the divergence harvest.

## Consequences

- One copy in and one threaded pass out is now the shape of every dense verb `FactorFills` serves;
  a new verb that writes a triangle or a copy should take the same helpers.
- `QrDecomposition` can be spent: after `TakeQColumnMajor` has expanded in place it refuses R, Q and
  the solves. Only `qr`'s two- and three-output forms take it, R first.
- `[H] = hess(A)` called through the multiple-output door answers H; it answered Q before, on a road
  no script reached with one output.
- The thread policy has a second input for the QR family, the footprint, from 32 MB. `Work.Spectral`
  and the square factorizations are unchanged; the split probe's pinned-thread sessions record where
  each driver's best count is at the benchmark's sizes.

## Divergences

None new. Every changed road answers what the old one answered, bit for bit.

## Still open

- **The LAPACK drivers** (open item 274): `dgesdd` at 800 square (67-77 ms against MKL's 26), the
  600-by-300 economy SVD (16-26 against 9), `dpotrf` at 2000 (22 against 15), `dgeqrf`+`dorgqr` at 1200
  (79 against 36), `dgehrd` at 400 (7 against 4.4). No thread count closes them. What would: a newer
  or differently built OpenBLAS (its `DYNAMIC_ARCH` kernel for this CPU, ReLAPACK's recursive
  factorizations), or a LAPACK under the same provider calls; to be measured routine by routine with
  the split probe before anything is swapped.
- **`d01_qr_1200` is bimodal** on both builds (passes near 0.085 or near 0.15; open item 275): find
  what the slow passes pay for — the collector after the 32 MB matrices `d01` makes before it, or the
  OpenBLAS pool waking — with `DOTNET_gcServer=0` and a pinned-thread session.
- **`istriu` refuses a complex matrix** (`istriu needs numbers, but element (0, 1) was a complex`);
  R2025b answers. Found in passing by a test; open item 276.
- Item 268 (`d10_expm_400` slower after I4) is overtaken: the row is 0.014 against R2025b's 0.020.
