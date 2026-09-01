# Solvers and Signal Processing — the plan (M124–M137), with progress

This is the living copy of the plan approved on 2026-09-01 (original:
`~/.claude/plans/peaceful-discovering-floyd.md` on the first machine). It is committed so it
travels with the repo. **Update the "Progress" table and the "Pick-up notes" as milestones land.**

## Progress

| Milestone | Status | Commit / ADR | Notes |
|---|---|---|---|
| M124 — The instruments | **DONE** 2026-09-01 | ADR 0126 | Parity fixture suite, Signal CSV + verifier, d15/d16, d14 groups, stess_71 |
| M125 — Explicit ODE family | not started | ADR 0127 | next |
| M126 — Stiff family | not started | ADR 0128 | |
| M127 — Quadrature | not started | ADR 0129 | |
| M128 — Sparse / Krylov | not started | ADR 0130 | |
| M129 — Scattered data | not started | ADR 0131 | |
| M130 — BVP + DDE | not started | ADR 0132 | |
| M131 — pdepe + funfun leftovers | not started | ADR 0133 | |
| M132 — Signal: windows/generators/transforms | not started | ADR 0134 | |
| M133 — Signal: filtering/conversions/multirate | not started | ADR 0135 | |
| M134 — Signal: design + analysis | not started | ADR 0136 | closes the 3 divergences M124 recorded |
| M135 — designfilt / digitalFilter | not started | ADR 0137 | |
| M136 — Signal: spectral + measurements | not started | ADR 0138 | |
| M137 — Signal: time–frequency/modelling/vibration | not started | ADR 0139 | |

Stress scripts: M124 = `stess_71.m`; each later milestone takes the next number (M125 = `stess_72.m`).
ADR numbers above assume one ADR per milestone in order; renumber if an ADR is added between.

## Pick-up notes (read these first on the other machine)

**Where things are**

- Repo: `E:\EE Projects\JGraph` (commits go straight to `main`; nothing is pushed by the assistant).
- Demo workspace beside it: `E:\EE Projects\JGraph_demo_workspace` — `head2head_v2\` (the timed suite),
  `Matlab Stress Test\stess_NN.m` (run by `tools\run-stress.ps1`, Release exe), `matlab-gap-report\`.
- MATLAB R2024a Update 4: `E:\Matlab\bin\matlab.exe -batch "..."` (headless). Its readable sources:
  `E:\Matlab\toolbox\matlab\funfun\{ode*.m, private\ntrp*.m, odezero.m, odenumjac.m, ...}`,
  `E:\Matlab\toolbox\signal\signal\*.m` (0 `.p` files). Read for documented behaviour and constants;
  write fresh C#; never copy text; name the files read in the ADR.
- If MATLAB is at a different path on the other machine: `record-matlab.ps1 -MatlabExe <path>`,
  `run_instrumented.ps1` has `$matlabExe` at the top, `build-signal-csv.py --root <signal folder>`.

**The instruments M124 built, and how to use them each milestone**

1. **Parity fixtures** — `tests/JGraph.Tests/MatlabParity/fixtures/<mNNN>_<topic>.m` print
   `CHK|name|value|rule` (`exact`, `shape`, `rel=1e-12`, `abs=1e-9`, `div=ADRnnnn`). Record MATLAB once:
   `powershell -File tools/parity/record-matlab.ps1 -Fixtures m125_ode_explicit` → commits
   `expected/m125_ode_explicit.txt`. The xunit theory `MatlabParityFixtureTests` runs every fixture in
   the normal test run (filter `FullyQualifiedName~MatlabParity`). A `div=` line MUST differ; a fixture
   without a recording FAILS. Ad hoc: run the fixture with
   `jgraph.exe -batch <fixture.m> > out.txt` then `python tools/parity/compare.py expected/x.txt out.txt`.
   **A fixture must run on both engines**: wrap a form one engine refuses in `try/catch` and pin the
   branch taken as a `div=` line (see `m124_signal.m`). `%.17g` for doubles; `fprintf` only; no `rand`.
2. **Signal population** — `tools/matlab-checklist/matlab-r2024a-signal.csv` (351 names; the `forms`
   column is each name's documented call syntaxes, `;`-separated — that is the capability list for
   M132–M137). `docs/matlab-signal-coverage.md` buckets every name by milestone;
   `python tools/matlab-checklist/verify-signal-coverage.py` must exit 0. **Register first, then move
   the doc line** — the verifier refuses a registered name still listed as missing.
3. **Head-to-head** — `head2head_v2/scripts/d15_signal.m`, `d16_solvers.m`; add rows per milestone
   (≥ 0.1 s on MATLAB each). `d14_capability.m` has `Solvers` and `Signal` groups (groups and forms
   lists must stay the same length; 291 today). Run two scripts on both engines:
   `powershell -Command "& './run_instrumented.ps1' -Scripts @('d15_signal','d16_solvers')"` — **this
   overwrites `out_*/walltimes.csv`**; keep a copy and merge, or rerun `run_repeats.ps1 -Repeats 5`
   for the whole suite when a milestone's numbers are the claim. Reports: `python build_report.py`.

**Per-milestone gate (in this order, each a stop if it fails)**

```
dotnet build "E:\EE Projects\JGraph\JGraph.sln" -c Release
powershell -File tools\run-lanes.ps1 -Configuration Debug          # four lanes, all green
powershell -File tools\run-stress.ps1                              # count must equal stess_*.m count
python tools\matlab-checklist\probe-toolbox-forms.py               # regenerates docs/matlab-toolbox-coverage.md
python tools\matlab-checklist\verify-toolbox-coverage.py
python tools\matlab-checklist\verify-signal-coverage.py
python tools\matlab-checklist\harvest-divergences.py               # rebuilds docs/matlab-divergences.md
dotnet test tests\JGraph.Tests --filter "FullyQualifiedName~MatlabParity"
```
Then ADR (context / decision / consequences / **## Divergences** with bolded leads — the harvester
reads that heading / still open), commit on `main`.

**Traps found in M124 (verbatim so they are not re-found)**

- Windows PowerShell 5.1 `Start-Process -Wait` never returns for `matlab.exe` (it waits for
  `MathWorksServiceHost` too). Use `$null = $proc.Handle; $proc.WaitForExit()` — the `Handle` read is
  also what makes `ExitCode` non-null. `record-matlab.ps1` already does this.
- MATLAB's single-output `butter(n,Wn)` is `b` alone; JGraph's is `[b; a]` (2 rows) and `[b,a]=butter`
  is refused. Same for `freqz` (`[h; w]`). `firpm` coefficients are ~1e-5 from MATLAB's and its
  exchange warns "did not fully converge" at order 400. All three recorded in ADR 0126 for M134.
- `db(x, 'power')` is refused today (M132's `db2pow` family).
- `ode45` on Lorenz to t=200: 11,565 rows here vs 11,593 in MATLAB (t=20 and t=60 agree exactly).
  Chaotic long runs cannot pin step counts; fixtures use t ≤ 60.
- `disp(size(x))` prints `[2, 5]` in the MATLAB dialect where MATLAB prints `     2     5` — a display
  difference outside this arc; fixtures use `fprintf`.
- The Claude Bash tool halves backslashes inside python heredocs (`\\n` → `\n`); use the Write/Edit
  tools for anything with escapes. A `cat > file` with no heredoc swallows stdin and looks like a hang.
- Timing rows to remember (single interleaved run, JGraph vs MATLAB): `dct/idct` 4M **3.8 s vs
  0.25 s** (M132 — put it on the batch FFT), IIR `filter` 10M 0.119 vs 0.065 (M133), `ode45`
  tight-tolerance orbit **0.155 vs 0.030 with the same step count** (M125 — the RHS through the
  interpreter), Lorenz t=200 0.175 vs 0.143 (M125). JGraph is ahead on `integral`, `quadgk`,
  `fminsearch`, `trapz`.

**Verification record for M124 (2026-09-01, first machine)**

- Release build clean; `MatlabParity` tests 10/10; stress 71/71 (`stess_71` after its section 6
  learned that `numel` of the two-row `butter`/`freqz` answer is 2, not 16 — see ADR 0126).
- Lanes: `native/packed` **green (7,179)**; `native/boxed` 1 failure, `managed/packed` 7,
  `managed/boxed` 8 — **all pre-existing**, reproduced on the clean tree before M124's files
  were added (`ChipLinalgShapeTests` ×3 pencil-sign tests, `MatlabMatfunM107Tests` ×3 polyeig,
  `MatlabHistogram2M122Tests.ResidueAnswersMatlabsColumnsAndRow`, and
  `MatlabNumericClassM123Tests.ARunningTotalSaturatesAtEveryStep` in the boxed lanes). None is in
  the parity suite. They are a debt to clear before M125's gate can be read as "four lanes green".
- Coverage verifiers (toolbox, IPT, Signal) all exit 0; `harvest-divergences.py` → 240 divergences.

**What M125 should do first**

1. `python tools/matlab-checklist/probe-toolbox-forms.py` is the funfun forms list; `ode23/ode113/ode78/
   ode89` forms are in `matlab-r2021b-forms.csv` (folder `funfun`).
2. Read `E:\Matlab\toolbox\matlab\funfun\ode23.m`, `ode113.m`, `ode78.m`, `ode89.m`, and
   `private\ntrp23.m ntrp113.m ntrp78.m ntrp89.m odezero.m odenonnegative.m odemass.m odeevents.m`
   for the tableaus, error norm, step growth caps, `Refine` defaults, and the events bracket.
3. Before touching `OdeSolvers.DormandPrince`, run `m124_ode45` (28 exact lines) and `d08` — they are
   the guard that `ode45` does not move.
4. Write `m125_ode_explicit.m` with `nsteps/nfailed/nfevals` **exact** per solver and problem, record
   it, and let the recording find the constants.

---

# The plan as approved (2026-09-01)

## Context

The 2026-09-01 gap report (`JGraph_demo_workspace\matlab-gap-report\index.html`) ranked two
numeric gaps a first-week MATLAB user hits early:

| Gap | Today | MATLAB R2024a on this machine |
|---|---|---|
| ODE solvers | `ode45` only (`odeset` reads 5 of 23 fields; no `Events`, `OutputFcn`, `Mass`, `NonNegative`) | 12 solvers + BVP, DDE, PDE |
| Quadrature | `integral`, `quadgk`, `trapz` | + `integral2/3`, `quad2d`, `quad*`, `dblquad`, `triplequad` |
| Sparse solvers | `sparse`, `eigs`, `ichol`, `ilu`, dense `\` | + 11 Krylov solvers, `svds`, `spdiags`, orderings, tree plots |
| Scattered data | `delaunay`, `convhull`, `voronoi`, grid readers | + `griddata`, `scatteredInterpolant`, `griddedInterpolant`, `delaunayn`, … |
| Signal Processing Toolbox | 6 of 351 names (`butter db dct firpm freqz idct`) | 351 public names, all readable `.m` |

The user's decisions (asked 2026-09-01): **full funfun including BVP/DDE/PDE, those last**;
**`designfilt` + a `digitalFilter` value are in**; **solvers first, then Signal**.

The outcome: every documented name in `funfun`, `sparfun`, `polyfun` and the Signal Processing
Toolbox is either implemented and *measured equal to MATLAB* or declined by name in an ADR — and
the measurement is a permanent test, not a one-off diff.

## What already exists and is reused (verified file:line)

| Asset | Where | Reused by |
|---|---|---|
| Dormand–Prince driver with `Refine`, `MaxStep`, `InitialStep`, step recording, solution struct + `deval` | `src/JGraph.Numerics/OdeSolvers.cs:85`, `OdeRecording.cs`, `src/JGraph.Scripting/Jgs/JgsBuiltins.OdeSolution.cs`, `JgsBuiltins.Solvers.cs:359` (`Ode45Settings`) | M125 generalises it into one explicit-RK driver |
| `odeset`/`odeget` with all 23 field names accepted (`OdesetFields`, `JgsBuiltins.Solvers.cs:36`) | same | M125/M126 act on the 18 fields currently stored and ignored |
| Gauss–Kronrod 7/15 adaptive quadrature, infinite ranges, `integral`/`quadgk` | `src/JGraph.Numerics/Quadrature.cs:105` | M127 tiles it into 2-D/3-D |
| Root finder (`fzero` machinery), bounded minimiser, Nelder–Mead, NNLS | `src/JGraph.Numerics/Optimization/*` | M125 `Events` (bracketed root on the interpolant), M126 |
| Dense LU/QR/Cholesky/Schur, LAPACK provider | `src/JGraph.Numerics/LinearAlgebra/*` | M126 (Newton/Rosenbrock solves), M129 (`v4`), M137 (`modalfit`) |
| CSC sparse matrix: mat-vec, transpose, LU, solve, Arnoldi (`LargestEigenpairs`) | `src/JGraph.Numerics/Sparse/CscMatrix.cs` | M128 |
| Delaunay 2-D/3-D, convex hull 3-D, Voronoi | `src/JGraph.Math/Geometry/*` | M129 |
| Spline/pchip/makima slopes, Hermite pieces, `pp` structures, N-D grid reader | `src/JGraph.Numerics/Interpolation.cs`, `JgsBuiltins.Interpolation*.cs` | M129 `griddedInterpolant`, Signal `interp`/`resample` |
| FFT kernels: planar storage, batch, along-dimension, factored above 32K | `src/JGraph.Numerics/FftKernels.cs:623-787` | every Signal transform and spectral estimate |
| FIR feed-forward kernel, threaded along columns; IIR `filter` with initial/final state | `src/JGraph.Numerics/FilterKernels.cs`, `JgsBuiltins.cs:214` (`FilterAnswer`) | M133 `filtfilt`, `fftfilt`, `filtic`, `sosfilt` |
| Sliding windows incl. median (`Middle`) carried by a two-stack queue | `src/JGraph.Numerics/WindowKernels.cs` | M133 `medfilt1`, `hampel`, M135 `envelope` |
| Savitzky–Golay / local-fit weights | `src/JGraph.Numerics/SmoothKernels.cs` | M133 `sgolay`, `sgolayfilt` |
| Butterworth (analogue prototype + bilinear), Remez, 6 windows, spectrum, spectrogram, `Freqz` | `src/JGraph.Signal/{IirDesign,FirDesign,Window,Spectrum,Spectrogram,DigitalFilter}.cs` | M132–M137; the Signal project stays the home of the algorithms, `JgsBuiltins.Signal*.cs` the MATLAB-facing layer |
| Elliptic functions, Bessel, change points (`ischange`), `xcorr`/`xcov`, `detrend`, `unwrap`, `cplxpair`, `residue`, `interpft` (`FourierResampling`) | `JGraph.Numerics`, catalog | M134 `ellip`, `besself`; M135 `findchangepts` |
| Coverage instruments: catalog regex, form prober, `verify-toolbox-coverage.py`, hand-transcribed IPT CSV + `verify-ipt-coverage.py` | `tools/matlab-checklist/*` | M124 added the Signal population the same way IPT was added |
| Head-to-head suite, 5-run median protocol, noise floor 1.07× median / 1.26× worst, `d14_capability.m` groups | `JGraph_demo_workspace/head2head_v2/*` | every milestone adds rows |
| Stress runner (`tools/run-stress.ps1`, Release exe, count must equal file count), four test lanes (`tools/run-lanes.ps1`) | `tools/` | every milestone |
| MATLAB R2024a headless: `E:\Matlab\bin\matlab.exe -batch`; JGraph MATLAB dialect: `jgraph.exe -batch script.m` (filename, not `run(...)`) | ADR 0100/0102 | M124 parity recorder |

**Reference sources are readable.** Every Signal `.m` (0 `.p` files in `signal/signal`), every
`funfun` solver and its `private/ntrp*.m`, `odezero.m`, `odenumjac.m`, `integral2Calc.m` are plain
text under `E:\Matlab\toolbox`. They are read for the *documented behaviour and the constants that
define it* (tableaus, error-norm rules, step-growth caps, default window lengths, tie-breaking
order) and the implementation is written fresh in C# in the repo's own shape. No text is copied.
Each ADR names which reference files were read.

## House rules that bind every milestone

- One milestone = one commit **straight to `main`** (never branch), one ADR (next is **0127**),
  one stress script (next is **`stess_72.m`**), coverage docs regenerated and verified.
- Build is warnings-as-errors; the four lanes (`native|managed` × `packed|boxed`) must be green;
  the packed/boxed lanes must never select different kernels (byte-identical parity suite).
- Threaded kernels are **bit-identical** to their serial form (M120's rule); a reduction that
  reorders is a *decided* change and gets a determinism tier, not a silent one.
- A performance claim is a **5-run median** against the interleaved MATLAB run; a row that moves
  by less than 1.26× has not moved.
- A difference from MATLAB is either fixed or written under the ADR's divergence heading (the
  harvester builds `docs/matlab-divergences.md` from it) **and** pinned by a fixture line that
  expects the difference, so a divergence that silently disappears is noticed.
- Nothing is pushed; the MSI is not run; no file is touched while a test run is in progress.

## M124 — The instruments (DONE, ADR 0126)

Three instruments the later milestones cannot be gated without.

### 1. A permanent MATLAB parity fixture suite — built

- `tests/JGraph.Tests/MatlabParity/fixtures/<mNNN>_<topic>.m` — MATLAB-dialect scripts that
  print `CHK|<name>|<value>|<rule>` lines (`%.17g` for doubles; `rule` is `exact`, `rel=1e-12`,
  `abs=1e-9`, `shape`, or `div=ADR0126` meaning *must differ*). Same line grammar the head-to-head
  suite already parses.
- `tests/JGraph.Tests/MatlabParity/expected/<same>.txt` — MATLAB's output, recorded once by
  `tools/parity/record-matlab.ps1` (runs `matlab.exe -batch`, keeps only the `CHK` lines,
  writes UTF-8). The recorder also stores `matlab_version.txt`.
- `MatlabParityFixtureTests` (`[Theory]` over the fixture folder) runs each `.m` through the
  MATLAB dialect and compares against `expected/` with `tools/parity/compare.py`'s rules
  reimplemented in C# (`MatlabParityComparer`). A fixture with no recording **fails**, a `div=`
  line whose values agree **fails** ("divergence retired — delete it from the ADR").
- Day-one self-test: `m124_ode45` (28 lines, all exact step counts agree), `m124_integral` (20
  lines, incl. the ADR 0123 `div=` line), `m124_signal` (26 lines, 4 `div=` lines for M134).

### 2. The Signal population, counted the way IPT is — built

- `tools/matlab-checklist/build-signal-csv.py` → `matlab-r2024a-signal.csv` (351 names, 290 on a
  toc page, 1,982 documented forms).
- `docs/matlab-signal-coverage.md` + `verify-signal-coverage.py`: 6 implemented, 272 planned
  (M132 55, M133 36, M134 55, M135 6, M136 65, M137 55), 73 excluded by name.
- Sixteen names the plan implements live outside the Signal folder and are counted by the
  toolbox doc instead: `chirp db2mag mag2db tf2zp zp2tf ss2tf tf2ss zp2ss ss2zp ellipap
  freqspace xcov emd hht wvd xwvd`.

### 3. Head-to-head rows and capability probes — built

- `d15_signal.m`, `d16_solvers.m`: identical `CHK` lines on both engines (one interleaved run).
- `d14_capability.m`: `Solvers` (8) and `Signal` (8) groups; 291 forms, 281 accepted here.
- `run_instrumented.ps1`, `run_alternating.ps1`, README updated.

## Milestone gate (applies to M125–M137)

1. `dotnet build` clean; four lanes green; stress count = file count; new `stess_NN.m` prints no
   `Fail:`.
2. Parity fixtures for the milestone: **0 unexplained lines**; every explained one is a `div=`
   line and an ADR entry.
3. Capability probe: every documented form of every name in the milestone accepted (the CSV's
   forms column is the list).
4. Coverage doc(s) regenerated; verifier(s) exit 0.
5. Head-to-head rows: 5-run medians; each row ahead of MATLAB or within the noise floor, except
   rows the ADR lists as "MATLAB's MKL/FFTW kernel, bounded honestly" with the ratio stated.
6. ADR written (context, decision, divergences, measured table); commit on `main`.

---

## Track A — Solvers

### M125 — The explicit family: `ode23`, `ode113`, `ode78`, `ode89`, and the options `ode45` ignores

**Names:** `ode23 ode113 ode78 ode89 odextend odeplot odeprint odephas2 odephas3`; `odeset`
fields acted on: `Events, OutputFcn, OutputSel, NonNegative, Mass` (constant, non-singular),
`Stats, Vectorized, MaxOrder` (ode113), `Refine` per-solver defaults (1 for `ode23`/`ode113`, 4
for `ode45`, 8? — read `ode78.m`/`ode89.m` for theirs).

**Design:**
- `OdeSolvers.DormandPrince` becomes `ExplicitRungeKutta.Run(tableau, interpolant, …)`; a
  `RungeKuttaScheme` record holds `C, A, B, E`, the dense-output rows and the error-norm
  convention. `ode45` must stay **bit-identical** — the M119 tests, `m124_ode45` and `d08` `CHK`
  lines are the guard. `ode113` is a separate variable-order Adams PECE driver (orders 1–13, its
  own `ntrp113`).
- One `OdeDriver` owns what every solver shares: tspan handling, `Refine`, `Events`
  (bracket on the step's interpolant, terminal/direction, `[t,y,te,ye,ie]` and
  `sol.xe/ye/ie`), `OutputFcn` (`init`/step/`done` protocol, `OutputSel`, stop on `true`),
  `NonNegative` (project + damp), `Mass` (LU once), `Stats` printing, `odextend`.
- `deval` dispatches on `sol.solver` to the solver's own interpolant; `sol.idata` carries what
  that interpolant needs, MATLAB's field names.
- **Reference read:** `ode23.m ode113.m ode78.m ode89.m`, `private/ntrp23.m ntrp113.m ntrp78.m
  ntrp89.m odezero.m odenonnegative.m odemass.m odeevents.m`.

**Parity fixtures** (`m125_ode_explicit.m`): `vdp1`, `rigidode`, `orbitode` (events, restart),
`ballode` (terminal event, `odextend`), Lorenz to t=20, a `Mass` problem, a `NonNegative` decay.
Per solver and problem the fixture pins **`sol.stats.nsteps/nfailed/nfevals` exact**, the final
state `rel=1e-10`, `te` of events `abs=1e-9`, `deval` at 5 interior points `rel=1e-9`. Exact step
counts are the honest test that the algorithm, not just the answer, matches.

**Performance:** `d08_lorenz` is 0.060 s vs MATLAB 0.132 s, but M124 measured the tight-tolerance
orbit row at **0.155 s vs 0.030 s with the same step count** — the RHS through the interpreter.
Profile before optimising; the candidate is letting M98's register compiler take a scalar-only
anonymous function body (separate ADR if pursued). Gate: every new solver still ahead on Lorenz.

**Expected divergences:** `ode` (R2023b solver object) declined; `MStateDependence`,
`MvPattern`, `JPattern`, `BDF` accepted and stored until M126.

### M126 — The stiff family: `ode15s`, `ode23s`, `ode23t`, `ode23tb`, `ode15i`, `decic`

**Design:**
- `ode15s`: NDF orders 1–5 (`BDF` switches to BDF), `MaxOrder`, quasi-constant step, Newton with
  a factorised iteration matrix reused across steps (refactor on step change or convergence
  failure), numerical Jacobian by forward differences with MATLAB's column-grouping rules when
  `JPattern` is given (dense factorisation regardless — recorded), `Jacobian` as matrix or
  function, `Mass` (constant, `MStateDependence` `none`/`weak`), `MassSingular` `maybe` (DAE
  index 1).
- `ode23s`: Rosenbrock 2(3), W = I − h d J with J refreshed each step; `ode23t`: trapezoid with
  free interpolant, `ode23tb`: TR-BDF2. `ode15i` + `decic` for fully implicit `f(t,y,y')=0`
  (consistent initial conditions via least squares with fixed components).
- All share the M125 driver (events, output functions, solution struct, `deval` interpolants
  `ntrp15s ntrp23s ntrp23t ntrp23tb ntrp15i`).
- Dense LU from `LinearAlgebra/LuDecomposition.cs` (native lane via the provider).
- **Reference read:** `ode15s.m ode23s.m ode23t.m ode23tb.m ode15i.m decic.m`,
  `private/odenumjac.m odejacobian.m odemass.m daeic12.m daeic3.m ntrp15s.m …`.

**Parity fixtures** (`m126_ode_stiff.m`): `vdp1000`, `brussode` (N=20, `JPattern`), `hb1ode`,
`hb1dae` (Mass singular), `amp1dae`, `ihb1dae` (ode15i), Robertson to 4e6. Pins: final state
`rel=1e-8`, `nsteps` **exact** where the Jacobian is analytic, `nsteps` within 2% where it is
finite-differenced (recorded as the honest gate if it does not hit exact), `deval` `rel=1e-7`.

**Performance:** `d16` rows for `vdp1000` and `brussode(200)`. The cost is Jacobian evaluation
through the interpreter (N RHS calls per refresh); `Vectorized` cuts that to one call and is the
first optimisation, `JPattern` grouping the second.

### M127 — Quadrature: `integral2`, `integral3`, `quad2d`, and the legacy six

**Names:** `integral2 integral3 quad2d quad quadl quadv dblquad triplequad`; `integral` gains
any documented option still missing (`ArrayValued`, `Waypoints`, `AbsTol`/`RelTol` — check
against the forms CSV first).

**Design:**
- `integral2 'tiled'` and `quad2d`: Shampine's TwoD — a rectangle mapped to the region between
  the `ymin(x)`/`ymax(x)` curves, a 2-D Gauss–Kronrod product rule per tile, error-driven tile
  subdivision, the integrand evaluated **once per tile over a 14×14 array** (this is why it is
  fast through an interpreter, and it is how MATLAB does it). `'iterated'`: nested
  `Quadrature.Integrate` with `Waypoints`. `integral3` = `integral2` inside an outer 1-D
  adaptive integral. `quad` adaptive Simpson, `quadl` adaptive Lobatto, `quadv` vector-valued
  Simpson, `dblquad`/`triplequad` over the legacy `quad` with their `tol`/`method` arguments.
- **Reference read:** `integral2.m private/integral2Calc.m quad2d.m integral3.m quad.m quadl.m
  quadv.m dblquad.m triplequad.m`.

**Parity fixtures** (`m127_quadrature.m`): the doc examples for each name, singular corners,
infinite outer limits, a function-handle limit, `'AbsTol'`/`'RelTol'` sweeps; pins `rel=1e-10`
on values, `errbnd` `rel=1e-2`, and the warning text for `MaxFunEvals`.

**Performance:** `d16` row: `integral2` of a smooth 2-D Gaussian over a disc; target ahead of
MATLAB (their tile loop is `.m` code).

### M128 — Sparse: the Krylov solvers and the rest of `sparfun`

**Names (30 missing, 5 declined):** implement `pcg gmres bicg bicgstab bicgstabl cgs minres
symmlq qmr tfqmr lsqr svds spdiags spfun spones spconvert spaugment sprandn sprandsym sprank
colperm treelayout treeplot etreeplot gplot unmesh`; decline `svdsketch`, `equilibrate`,
`colamd`, `symamd`, `dissect`, `spparms`, `amd` — orderings and scaling change nothing in an
engine that factors densely, and the ADR says so (`issparse` already answers "stored densely").

**Design:**
- One `KrylovSolver` frame: `A` as `CscMatrix`, dense matrix or function handle;
  `[x, flag, relres, iter, resvec] = name(A, b, tol, maxit, M1, M2, x0)`, preconditioners as
  matrices or handles, `gmres(A,b,restart,…)` with `iter` as `[outer inner]`, MATLAB's flag
  meanings and the printed message when no output is asked for. Each method is its own
  recurrence; `minres`/`symmlq` need the Lanczos form, `lsqr` the Paige–Saunders bidiagonalisation.
- `svds` on `[0 A; A' 0]` through the existing Arnoldi with Ritz refinement, `'largest'`,
  `'smallest'`, `k`, `sigma`; `spdiags` all four forms; `sprank` via Hopcroft–Karp on the bipartite
  graph; `treelayout`/`treeplot`/`gplot`/`etreeplot` as line objects through the plotting facade.
- **Reference read:** `pcg.m gmres.m bicgstab.m … lsqr.m svds.m spdiags.m treelayout.m`.

**Parity fixtures** (`m128_sparse.m`): `gallery('wathen',…)`, `delsq(numgrid('S',n))`, the doc
examples with `ichol`/`ilu` preconditioners; pins `flag` **exact**, `iter` **exact**, `relres`
`rel=1e-8`, `x` `rel=1e-6`, `svds` values `rel=1e-10`, `spdiags` `exact`.

**Performance:** `CscMatrix.MultiplyVector` threaded by row blocks through `ParallelKernels`
(bit-identical: each row's dot product is one serial sum); `d16` rows: `pcg` on a 1e6 Poisson
matrix with `ichol`, `gmres(20)` on a convection–diffusion matrix. MATLAB's mat-vec is native;
target within 1.26× and honest ratio if not.

### M129 — Scattered data: `griddata`, the interpolant objects, N-D triangulation

**Names (11 missing + 5 from the inventory):** `griddata griddatan scatteredInterpolant
griddedInterpolant delaunayn tsearchn dsearchn convhulln boundary stlread stlwrite`; decline
`alphaShape`, `polyshape`, `polybuffer`, `nsidedpoly`, `triangulation`,
`delaunayTriangulation`, `DelaunayTri`, `TriRep`, `TriScatteredInterp` — object families with
their own method tables, out of this arc's scope and said so (the `boundary` verb uses an
internal alpha shape without exposing the object).

**Design:**
- `griddata` methods: `linear` (barycentric on the Delaunay), `nearest`, `natural` (Sibson via
  Bowyer–Watson cavity areas), `cubic` (Clough–Tocher), `v4` (biharmonic spline, dense solve).
  `griddatan` for N-D linear/nearest over `Delaunay3D`/a new N-D Bowyer–Watson.
- `scatteredInterpolant` and `griddedInterpolant` as **callable values** — the `pp` precedent
  (ADR 0102) gives a value that is a curve; this is a value that is a field, with
  `Points/Values/Method/ExtrapolationMethod` (`GridVectors` for gridded), read/writable fields,
  `F(xq,yq)` and `F(P)` call forms. `griddedInterpolant` wraps M101's grid reader.
- **Divergence to record:** MATLAB's triangulation is Qhull's; on co-circular points the
  triangle list differs. Fixtures compare **interpolated values**, and the triangle-list fixtures
  use points in general position only.
- **Reference read:** `griddata.m griddatan.m boundary.m dsearchn.m tsearchn.m stlread.m`.

**Parity fixtures** (`m129_scattered.m`): the doc examples (`peaks` samples, the 4 methods on
a 40-point set), extrapolation modes, `boundary` with shrink factors 0/0.5/1, `stlwrite` →
`stlread` round trip; pins `rel=1e-10` for linear/nearest/natural/cubic, `rel=1e-6` for `v4`.

**Performance:** `d16` row: `griddata` linear, 1e5 points → 1000×1000 grid; the Delaunay is
already here, the point-location walk is the new cost — a grid-bucketed start point makes it
linear.

### M130 — Boundary-value and delay problems

**Names:** `bvp4c bvp5c bvpinit bvpset bvpget bvpxtend dde23 ddesd ddensd ddeset ddeget` and
`deval` on their solutions.

**Design:** `bvp4c` — three-stage Lobatto IIIa collocation, residual control, mesh
adaptation, unknown parameters, multipoint intervals, singular term (`SingularTerm`),
`FJacobian`/`BCJacobian`; `bvp5c` — four-stage with error control; both through the dense
solver with the banded structure exploited by a block-tridiagonal elimination (the one place a
banded solver is worth writing). `dde23` — Bogacki–Shampine with the history/solution
interpolant for delayed arguments and discontinuity tracking at delay multiples; `ddesd` (state-
dependent delays), `ddensd` (neutral). **Reference read:** `bvp4c.m bvp5c.m private/bvp*.m
ntrp3h.m ntrp4h.m dde23.m ddesd.m ddensd.m`.

**Parity fixtures** (`m130_bvp_dde.m`): `twobvp mat4bvp shockbvp emdenbvp fsbvp threebvp rcbvp`,
`ddex1..ddex5`; pins mesh size **exact** for `bvp4c`/`bvp5c` on the doc problems, solution
`rel=1e-6`, `dde23` `nsteps` exact and final `rel=1e-8`.

### M131 — `pdepe` and the funfun leftovers

**Names:** `pdepe pdeval symvar vectorize inline inlineeval fcnchk`; decline `odeexamples`
(a GUI) and the 40 example files (`vdp1`, `lorenz`, `ballode`, … — they are *examples*, and the
fixtures reproduce the ones that matter). `inline` answers an anonymous-function value carrying
`formula`/`argnames` (legacy, documented as such).

**Design:** `pdepe` — Skeel–Berzins spatial discretisation on the user mesh, method of lines
through M126's `ode15s` with the mass matrix the discretisation produces, `pdeval` for the
solution and its flux. **Reference read:** `pdepe.m pdeval.m private/pdentrp.m symvar.m`.

**Parity fixtures** (`m131_pde.m`): `pdex1..pdex5`; pins `rel=1e-6` on the solution surface at
the doc's sample points, `nsteps` within 2%.

---

## Track B — Signal Processing (345 missing names: 272 implemented, 73 declined)

The bucket lists are the CSV-checked ones in `docs/matlab-signal-coverage.md` (verified by
`verify-signal-coverage.py`); the counts there are authoritative. Every Signal algorithm lives in
`src/JGraph.Signal` (or `JGraph.Numerics` when it is a kernel another project wants); the
MATLAB-facing forms live in new `JgsBuiltins.Signal.*.cs` partials registered through the
catalog. A name is "done" when every syntax line in the CSV is accepted and the fixture line for
it passes.

### The 73 declined names, recorded in M124's ADR 0126 / the coverage doc

| Family | Names | Why |
|---|---|---|
| Internal helpers and toc pages | `ChkIfBlockReusable aboutsignaltbx bscost completefreqresp computepsd crmz_grid drawpznumbers extract_phase fastreshape filt2block filtdes filterAnalysisOptions filtgraph findfreqvector firpmmex freqz_freqvec freqzparse freqzplot genplotdata getTranslatedString getTranslatedStringcell getinterpfrequencies kratio local_max psdoptions scopext sigprivate signalpolyutils specplot timezparse vratio` + 15 `toc*` | not user-facing |
| EDF files | `edfheader edfinfo edfread edfwrite` | a medical file format; with the DICOM/medical exclusion of the IPT doc |
| Signal ROI / labelling / datastores / feature extractors | `signalDatastore signalMask binmask2sigroi extendsigroi extractsigroi mergesigroi removesigroi shortensigroi sigroi2binmask sigrangebinmask signalFrequencyFeatureExtractor signalTimeFeatureExtractor signalTimeFrequencyFeatureExtractor timeFrequencyScalarFeatureOptions scalarFeatureOptions framelbl tall` | the ML-pipeline object layer, excluded with the Stats model objects |
| `fdesign`, `cascade`, `dpssclear dpssdir dpssload dpsssave` | | `fdesign` is the DSP System Toolbox object tree (not installed); the `dpss` database is a disk cache |

### M132 — Windows, waveform generators, transforms, unit conversions (55 in the Signal folder + `chirp db2mag mag2db` from base)

- **Windows (21):** `barthannwin bartlett blackman blackmanharris bohmanwin boxcar chebwin
  flattopwin gausswin hamming hann hanning kaiser nuttallwin parzenwin rectwin taylorwin triang
  tukeywin window dpss`. `WindowType` grows to all of them with `'symmetric'|'periodic'`, the
  parameters (`kaiser(n,beta)`, `chebwin(n,r)`, `gausswin(n,alpha)`, `tukeywin(n,r)`,
  `taylorwin(n,nbar,sll)`), `dpss` by the tridiagonal eigenproblem. **Trap already visible:**
  `Window.Create` is symmetric-only and `blackmanharris`'s coefficients here are the 4-term
  minimum set — MATLAB's are `0.35875 0.48829 0.14128 0.01168` — check each against the fixture
  before trusting what exists.
- **Generators (11):** `chirp` (linear/quadratic/logarithmic, phase, `'convex'|'concave'`) `diric
  gauspuls gmonopuls pulstran rectpuls sawtooth sinc square tripuls vco`.
- **Transforms (11):** `czt goertzel hilbert fwht ifwht cceps icceps rceps dftmtx bitrevorder
  digitrevorder` — `hilbert` and `czt` (Bluestein) on `FftKernels`, `cceps` with MATLAB's phase
  unwrapping and delay removal (the `nd` output). **Also: `dct`/`idct` are 15× slower than MATLAB
  at 4M (3.8 s vs 0.25 s, d15) — put them on the batch FFT here.**
- **Conversions and framing (12):** `db2mag db2pow mag2db pow2db buffer modulate demod seqperiod
  shiftdata unshiftdata udecode uencode marcumq framesig datawrap`; `db(x,'power')` is refused today.
- **Fixtures** (`m132_windows_generators.m`): every window at n = 1, 2, 7, 8, 64 both flavours
  `rel=1e-13`; generators on the doc examples `rel=1e-12`; `hilbert`/`czt`/`cceps` on a chirp
  `rel=1e-10`; `buffer` with overlap/underlap and the `opt` forms `exact`.
- **Performance:** `d15` rows `hilbert` 4M, `czt` 1M (Bluestein through the batch FFT).

### M133 — Filtering, coefficient conversions, multirate (36 in the Signal folder + `tf2zp zp2tf ss2tf tf2ss zp2ss ss2zp` from base)

- **Filtering (17):** `filtfilt` (Gustafsson initial-state trick, `sos` form, along columns,
  threaded over columns bit-identically) `fftfilt` (overlap-add, MATLAB's block-length cost
  table) `filtic sosfilt latcfilt medfilt1` (`WindowKernels.Middle`, `'omitnan'|'includenan'`,
  `'zeropad'|'truncate'`) `hampel sgolay sgolayfilt` (`SmoothKernels`) `decimate` (Chebyshev IIR
  default and `'fir'`) `interp` (the symmetric FIR of Oetken) `resample` (polyphase Kaiser-windowed
  FIR, `p/q` rational, `[y,b]` and the non-uniform `(x,tx,fs)` forms) `upfirdn upsample downsample
  fillgaps envelope` (`'analytic'|'rms'|'peak'`).
- **Conversions:** `tf2sos sos2tf zp2sos sos2zp tf2zpk ss2sos sos2ss tf2latc latc2tf sos2cell
  cell2sos residuez eqtflength polystab polyscale sos2ctf zp2ctf scaleFilterSections filtstates` (+
  the six base-folder ones) — `zp2sos`'s pairing/ordering/scaling rules (`'up'|'down'`,
  `'inf'|2|1|'none'`) are the parity risk and get the densest fixture.
- **Fixtures** (`m133_filtering.m`): `filtfilt` vs MATLAB on `butter(8,...)` and an sos to
  `rel=1e-10`; `resample(x,3,2)`, `resample(x,5,7)`, `decimate(x,4)`, `interp(x,3)` `rel=1e-9`;
  `zp2sos` on 12 pole sets `exact` order and `rel=1e-12` coefficients.
- **Performance:** `d15` rows `filtfilt` 10M (IIR is sequential; the gain is one pass over
  memory per direction, target within 1.26×; today's IIR `filter` 10M is 0.119 s vs 0.065 s),
  `resample` 10M by 3/2, `medfilt1` 10M w=51 (the carried window makes it linear; target ahead),
  `fftfilt` 10M with a 1024-tap FIR (target ahead).

### M134 — Filter design and analysis (55)

- **FIR design (18):** `fir1` (window method, all band types, `'scale'|'noscale'`) `fir2 firls
  firpmord kaiserord fircls fircls1 cfirpm cremez remez remezord gaussdesign gaussfir firgauss
  rcosdesign firrcos maxflat intfilt yulewalk`. `FirDesign.Remez` gains the `'hilbert'`/
  `'differentiator'` types, weights, `lgrid`, and `[h,err,res]`; `cfirpm` is complex Remez.
  **M124 found the existing exchange does not converge to MATLAB's design** (centre tap 1e-5 off
  at order 20; warns at order 400) — rewrite it against `firpm.m`'s reference and retire the
  `firpm_centre`/`firpm_dc` `div=` lines in `m124_signal.m`.
- **IIR design (20):** `cheby1 cheby2 ellip besself buttord cheb1ord cheb2ord ellipord buttap
  cheb1ap cheb2ap besselap lp2lp lp2hp lp2bp lp2bs bilinear impinvar freqs` (+ `ellipap`,
  `freqspace` from base). `IirDesign.Butterworth` becomes the general prototype → transform →
  bilinear pipeline; `[z,p,k]` and `[A,B,C,D]` output forms for all; `'s'` analogue flag. **Close
  the M124 divergences: `[b,a] = butter` and single-output `butter` = `b`; single-output `freqz` =
  `h`; retire `butter_two_outputs` and `freqz_single_numel` in `m124_signal.m`.**
- **Analysis (17):** `grpdelay phasedelay phasez zerophase impz impzlength stepz filtord
  filternorm firtype isallpass islinphase ismaxphase isminphase isstable zplane zplaneplot`;
  `freqz` gains `'whole'`, `[h,w] = freqz(b,a,f,fs)`, the sos form, and the **no-output plot**
  (magnitude + phase panels), as do `impz`, `stepz`, `grpdelay`, `phasez`, `zerophase`.
- **Fixtures** (`m134_design.m`): every design on the doc examples, `rel=1e-10` on
  coefficients (`ellip`/`cheby2` at order 10 will show the last-place difference of a different
  root-finding route — if it does, the fixture rule becomes `rel=1e-8` and the ADR says why);
  `buttord`/`cheb1ord`/`ellipord` orders and `Wn` **exact**/`rel=1e-12`; `impz` length **exact**;
  `is*` predicates **exact**.
- **Performance:** design is small; the row is `fir1(2000,…)` + `freqz(…, 2^20)`.

### M135 — `designfilt`, the `digitalFilter` value, and the four one-line filters

- **Names:** `designfilt digitalFilter lowpass highpass bandpass bandstop` (+ the `digitalFilter`
  methods MATLAB documents: `filter filtfilt freqz impz stepz grpdelay phasez zerophase zplane
  isstable islinphase isminphase isallpass firtype filtord info double single isdouble issingle
  isfir tf sos zpk ss`).
- **Design:** `digitalFilter` is a **value** (the `pp`/interpolant precedent): `Coefficients`
  (tf or sos + scale), `FrequencyResponse`, `ImpulseResponse`, `Specifications` (the
  name–value set), the design method. `designfilt`'s response types (`lowpassfir/iir`,
  `highpass*`, `bandpass*`, `bandstop*`, `differentiatorfir`, `hilbertfir`, `arbmagfir`) and
  design methods (`butter cheby1 cheby2 ellip window equiripple ls maxflat kaiserwin cls`) route to
  M134's designs; the interactive fallback (a design assistant when the spec is under-determined)
  is declined — an under-determined spec errors with MATLAB's message text. `lowpass`/`highpass`/
  `bandpass`/`bandstop`: `'ImpulseResponse'` `'auto'|'fir'|'iir'`, `'Steepness'`,
  `'StopbandAttenuation'`, the `[y,d]` output, the no-output plot.
- **Fixtures** (`m135_designfilt.m`): 24 specs through `designfilt`, compared by `d.Coefficients`
  `rel=1e-10` and `isstable`/`filtord` `exact`; the four verbs on a 3-tone signal `rel=1e-9`.
- **Divergences expected:** `'auto'` chooses FIR vs IIR by MATLAB's own rule (read `lowpass.m`);
  minimum-order designs must land on MATLAB's order **exactly** or the fixture says `div=`.

### M136 — Spectral estimation and measurements (65 + `xcov` from base)

- **Spectral (24):** `periodogram pwelch cpsd mscohere tfestimate pspectrum pmtm plomb pburg pcov
  pmcov pyulear peig pmusic rooteig rootmusic poctave` + legacy `psd csd cohere tfe pmem spectrum
  specgram`. One `Welch` frame builder (`FftKernels.TransformBatch` over all segments in one call,
  threaded, one-sided/two-sided/centered, `'power'|'psd'`, `ConfidenceLevel`, `'reassigned'`, the
  `fs`/`f`-vector/`'onesided'` argument dance shared by all of them — `psdoptions.m` is the
  reference for the parsing rules). `pspectrum`'s `'power'|'spectrogram'|'persistence'` and its
  Kaiser leakage → resolution rules.
- **Measurements (13):** `bandpower enbw meanfreq medfreq obw powerbw sfdr sinad snr thd toi
  instfreq instbw`.
- **Peaks, alignment, distances (11):** `findpeaks` (all 8 name–values, `[pks,locs,w,p]`,
  prominence by the stack algorithm, no-output plot) `peak2peak peak2rms rssq zerocrossrate cusum
  findchangepts` (over `ChangePoints.cs`) `alignsignals finddelay findsignal dtw edr`.
- **Bilevel waveforms (12):** `statelevels` (histogram mode method) `midcross dutycycle
  pulseperiod pulsesep pulsewidth falltime overshoot risetime settlingtime slewrate undershoot`.
- **Correlation (5):** `xcorr2 xcov cconv convmtx corrmtx`.
- **Fixtures** (`m136_spectral.m`): the doc examples per name; `pwelch` on a 3-tone + noise
  (deterministic `mod`-noise, never `rand`) `rel=1e-10`; `findpeaks` outputs **exact**
  locations, `rel=1e-12` widths; `snr`/`thd`/`sinad` `rel=1e-8`; `statelevels` `exact`.
- **Performance:** `d15` rows `pwelch` 10M (`hann(4096)`, 50 %), `spectrogram`-sized batch,
  `findpeaks` 10M with `MinPeakProminence` (target ahead — MATLAB's is `.m` code).

### M137 — Time–frequency, signal modelling, vibration (55 + `emd hht wvd xwvd` from base)

- **Time–frequency (17):** `spectrogram` (all forms, `'yaxis'`, `'reassigned'`, `'MinThreshold'`,
  the no-output image) `stft istft iscola xspectrogram stftmag2sig fsst ifsst tfridge kurtogram
  pentropy pkurtosis spectralKurtosis spectralSkewness spectralFlatness spectralCrest
  spectralEntropy`. `stft`/`istft` COLA-consistent with MATLAB's `'FrequencyRange'`,
  `'OverlapLength'`, `'FFTLength'`, centered frames.
- **Modelling (25):** `lpc levinson rlevinson arburg arcov armcov aryule invfreqs invfreqz prony
  stmcb` + the 14 reflection/LSF conversions `ac2poly ac2rc is2rc lar2rc lsf2poly poly2ac poly2lsf
  poly2rc rc2ac rc2is rc2lar rc2poly schurrc`.
- **Vibration (13):** `rainflow tachorpm rpmfreqmap rpmordermap rpmtrack orderspectrum ordertrack
  orderwaveform tsa modalfit modalfrf modalsd envspectrum strips`.
- **Fixtures** (`m137_timefreq.m`): `stft`→`istft` round trip `rel=1e-10`; `spectrogram`
  magnitudes on a chirp `rel=1e-9` and `t`/`f` vectors **exact**; `lpc`/`levinson`/`aryule`
  `rel=1e-10`; `rainflow` cycle counts **exact**; `modalfit` frequencies `rel=1e-6`.
- **Performance:** `d15` row `spectrogram` 10M / 1024 / 50 % (one batch FFT; target ahead).

---

## Verification, end to end

Per milestone, in this order, each one a stop if it fails:

```bash
dotnet build "E:\EE Projects\JGraph\JGraph.sln" -c Release
```
```bash
powershell -File "E:\EE Projects\JGraph\tools\run-lanes.ps1" -Configuration Debug
```
```bash
powershell -File "E:\EE Projects\JGraph\tools\run-stress.ps1"
```
```bash
python "E:\EE Projects\JGraph\tools\matlab-checklist\probe-toolbox-forms.py"
```
```bash
python "E:\EE Projects\JGraph\tools\matlab-checklist\verify-toolbox-coverage.py"
```
```bash
python "E:\EE Projects\JGraph\tools\matlab-checklist\verify-signal-coverage.py"
```
```bash
python "E:\EE Projects\JGraph\tools\matlab-checklist\harvest-divergences.py"
```
```bash
powershell -File "E:\EE Projects\JGraph_demo_workspace\head2head_v2\run_repeats.ps1" -Repeats 5
```

The parity fixtures run inside the lanes (they are xunit tests). Recording MATLAB expectations
(`tools/parity/record-matlab.ps1`) is done once per new fixture and its output is committed; it is
the only step that needs MATLAB, and it is never run inside the test suite. MATLAB's first call
is 30–70× its warm one, so the recorder is never used for timing.

At the end of the arc: rerun the gap-report inventory (`matlab-gap-report/inventory.py`) — the
Signal row must read 278/351 with 73 declined by name, `funfun` 40/40 documented names
implemented or declined, `sparfun`/`polyfun` likewise — and rebuild the report's toolbox
sections from it.

## Risks and traps, named now

- **Exact step-count parity is the strong claim.** It holds only if the error norm, the
  step-growth cap, the first-step heuristic and the `Refine` rule are MATLAB's exactly; one
  differing constant shows as `nsteps` off by one on every problem. That is the point of pinning
  it: the fixture finds the constant. (M124: `ode45`'s 28 lines already agree exactly.)
- **`ode45` must not move.** `m124_ode45`, M119's tests and `d08`'s `CHK` lines guard the
  refactor into the shared driver; run them before anything else in M125.
- **The catalog verifier refuses a name documented as missing that is registered** — regenerate
  the docs *after* registering, never before. (Both the toolbox and the Signal verifier.)
- **Existing `Window.Create` coefficients may be wrong** (Blackman–Harris set differs from
  MATLAB's); fixture first, then fix, never assume the existing kernel is a reference.
- **`zp2sos` ordering and `designfilt` minimum orders** are rule-heavy; both get `div=` lines
  rather than silent approximations if a rule cannot be matched.
- **Threading:** each new kernel threaded through `ParallelKernels` is compared bit-for-bit with
  its serial form in a unit test (M120's pattern); a Welch average is a summed reduction and gets
  an ordered sum.
- **Test hygiene:** a leftover `testhost.exe` makes the next build skip the test assembly;
  `run-lanes.ps1` clears them, and so must any manual run. `Pause_InterruptsATightLoop` can kill
  `ReRun_ResetsNumbering` four seconds later — not a load flake.
- **Scope creep:** `ode` (object), `polyshape`, `alphaShape`, `triangulation`, EDF, signal ROI
  and datastores are declined *by name* in the first ADR that meets them, so a later inventory
  counts them as decided rather than missing.
