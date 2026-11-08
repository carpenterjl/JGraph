# ADR 0322 — The every-row plan's stage P0: where the GUI round's seconds go

## Status

Accepted, 2026-11-08. Stage P0 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 2); a measurement, no engine code changed. Measured on 8dbad495 (Release) against
R2025b (25.2.0.2998904) on the i7-12700H, the ASUS "Silent" power plan, with nothing else of this
work running.

## Context

- **The plan** (rev 1, written from the second head2head GUI round of 2026-11-08) asks for every timed
  row and every script wall of that round at or under R2025b's median. R2025b led 85 operations and
  four walls: `d02_fft_signal` 3.65 s against 1.52 s, `d10_matfun` 1.19 against 0.95, `d11_datafun`
  4.02 against 2.29 and `d16_solvers` 2.28 against 1.77. d02 and d11 lose *between* their TIME rows
  (3.4 s and 2.6 s untimed), and d16 is open item 251 (every ODE row 1.6-2x slower than on 2026-09-13).
- **P0 as written** began with a five-repeat pass of all 16 scripts in the head2head_v3 rig. That pass
  was started and stopped by the user after one repeat: the user was using the machine, and the pass
  measures something else (below). Its partial folder was discarded; its clean Release build of
  8dbad495 was kept as the stamp `runs\p0-8dbad495`.

## Decision

### The baseline is the GUI round; every measurement runs in one session per engine (plan rev 2)

The rig launches a fresh process per script per engine per repeat. Measured in the stopped pass, a
MATLAB `-batch` script process took 25-50 s of wall time against 1-4 s of script, so the pass spent
about 50 of its 60 minutes starting MATLAB; and its rows are cold first calls where the GUI round's
are warm passes in one open session (`d16_ode23_lorenz200` was 0.106 s in the rig and 0.045 s in the
GUI round on 2026-09-13). The GUI round's medians stay the baseline. New runners in the rig
(`JGraph_demo_workspace\head2head_v3`, outside the repository):

- `session\session_launch.m` — the GUI round's `launch.m` method driven by a list file: one warm-up
  and N timed passes in one process, a diary log per script and pass, `passes.csv` walls. It keeps its
  list in a plain variable so engines back to 401d34b run it.
- `run_session.ps1` — one `jgraph -batch` and one `matlab -batch` process per measurement (`-EnvVar`
  for JGraph's environment); `run_session_app.ps1` — the same inside `JGraph.Application.exe -r`,
  stopped with `Stop-Process` so no layout is saved; `summarize_session.py` — medians, excess, CHK
  agreement.
- `run_probes.ps1` gained `-ProbeDir`, `-OutName`, `-Prefix`; `bisect_251.ps1` and
  `bisect_session.ps1` build one commit from a detached worktree through `manifest.ps1 -Repo` (the
  manifest names that commit) and time it; `sweep_p05.ps1` runs a thread-policy sweep.

### P0.2 — the untimed lines (instrumented copies)

`probes\instrumented\inst_d02_fft_signal.m`, `inst_d10_matfun.m` and `inst_d11_datafun.m` are the
scripts with a TIME line round every statement the originals leave untimed. Warm-up plus three passes
in one session, medians (`runs\p0-8dbad495\inst`, `inst-app`, `inst-app-gcserver`):

| script wall | JGraph headless | JGraph app | JGraph app, Server GC | R2025b GUI round | R2025b `-batch` |
| --- | --- | --- | --- | --- | --- |
| d02 | 1.54 | 3.34 | 1.57 | 1.52 | 3.52 |
| d10 | 1.30 | 1.29 | 1.21 | 0.95 | 2.90 |
| d11 | 2.49 | 3.74 | 2.39 | 2.29 | 4.71 |

The app reproduces the GUI round; headless JGraph does not lose (R2025b's `-batch` pays 2.2-2.3 s per
`exportgraphics`, so the fair column is the GUI round's). The two binaries differ in their collector:
`jgraph.exe` has run Server GC since M93 (`src/JGraph.Cli/runtimeconfig.template.json`), the windowed
app has always had the default workstation GC, unchanged between 401d34b and 8dbad495. Run with the
runtime's own `DOTNET_gcServer=1`, no code changed, the app's d02 falls from 3.34 to 1.57 s and d11
from 3.74 to 2.39 s. The lines that carry the loss (app / headless / R2025b `-batch`, seconds):

| line | app | headless | R2025b |
| --- | --- | --- | --- |
| d02 the `d02_roundtrip` CHK, `max(abs(back - sig))` | 2.156 | 0.621 | 0.007 |
| d11 the `d11_discretize` CHK, `sum(bins(~isnan(bins)))` | 1.100 | 0.401 | 0.057 |
| d11 `nmiss = sum(isnan(g))` | 0.614 | 0.205 | 0.004 |
| d02 `Fs([K/2+1:K 1:K/2], [K/2+1:K 1:K/2])` | 0.373 | 0.110 | 0.010 |
| d11 row `d11_unique_2M` | 0.326 | 0.127 | 0.131 |
| d10 the builders' nine CHK lines | 0.363 | 0.353 | 0.001 |

Split into operations (`probes\instrumented\split_i11_lines.m`, warm medians, JGraph headless against
R2025b): R2025b's `ifft(F)` of a conjugate-symmetric `F` answers a **real** array (`isreal` 1) where
JGraph's answers a complex one, and `back - sig` with a complex left side then takes 0.911 s against
0.0033 (`abs` of the complex result 0.197 against 0.0037, `max` over 4M reals 0.052 against 0.0008);
`isnan` over 5M 0.08-0.30 s against 0.002, `sum` and `nnz` of a 5M logical 0.085 and 0.166 s against
0.0005 and 0.0003, `~isnan` over 10M 0.272 against 0.0046; one index vector into a 2048-square matrix
(rows only, columns only or both) about 0.10 s against 0.007 and `circshift` 0.146 against 0.0036;
`trace` of a 3000-square matrix 0.234 s against 0.0001 (`trace(hankel(...))` of 2000-square 0.086, the
`magic(1200)` line 0.036). These become I11's rows in the plan.

### P0.4 — one probe per losing row

78 new probes in `probes\` (`probe_<row>.m`, written by a generator from the scripts' own statements:
the input outside `tic`, the row once cold and three times warm, the row's CHK lines plus a few that
read more of the answer), giving 99 `probe_d*` probes with the ten that existed and extras for the
untimed `eig(M)` and `histogram(y, 120)` and the four vdp solvers. All 99 ran in one session per engine
(`runs\p0-8dbad495\probes-first`, JGraph 51 s, R2025b 83 s, every process exit 0, no ERROR line). Every
CHK line that differs between the engines is one of the scripts' recorded divergence rows or an exact
step count the scripts deliberately leave unpinned, except the histogram probes: JGraph's `histogram`
ignores its bin count (open item 252).

### P0.3 — open item 251 is diffuse

The probes' warm numbers in a fresh process do not reproduce the GUI round's direction (ode45 Lorenz
warm was 0.13 s at 401d34b and 0.06 s at HEAD; the GUI round has 0.042 then 0.070), so the bisection
used `scripts\d16_solvers.m` itself in one JGraph session (warm-up and three passes) under
`DOTNET_gcServer=0`, the app's collector, on Release builds of eleven commits from detached worktrees
(`runs\251-<sha>\d16-session-ws`; the probe timings of the first five are kept under `bisect`):

| commit (date) | ode45 Lorenz | ode23 Lorenz | ode89 Lorenz | ode15s vdp | ode23s vdp | ode23t vdp |
| --- | --- | --- | --- | --- | --- | --- |
| 401d34b (09-13, first GUI round) | 0.038 | 0.044 | 0.070 | 0.066 | 0.101 | 0.061 |
| afc4833f (09-25, value ownership done) | 0.041 | 0.049 | 0.082 | 0.066 | 0.117 | 0.064 |
| a74f809b (09-27, interop) | 0.048 | 0.053 | 0.084 | 0.076 | 0.121 | 0.075 |
| f73b20a1 (09-29) | 0.056 | 0.054 | 0.088 | 0.076 | 0.125 | 0.074 |
| edb41d8e (10-03) | 0.049 | 0.057 | 0.091 | 0.079 | 0.126 | 0.074 |
| 782c2c51 (10-05) | 0.057 | 0.057 | 0.099 | 0.078 | 0.132 | 0.080 |
| 6bb588e3 (10-09) | 0.055 | 0.058 | 0.091 | 0.079 | 0.133 | 0.077 |
| 6bf49513 (10-13) | 0.057 | 0.063 | 0.102 | 0.087 | 0.144 | 0.082 |
| bd07f4fa (10-18) | 0.055 | 0.064 | 0.110 | 0.091 | 0.152 | 0.087 |
| 8a0d1deb (10-19) | 0.058 | 0.070 | 0.111 | 0.098 | 0.158 | 0.090 |
| d90e662c (10-29) | 0.057 | 0.068 | 0.114 | 0.095 | 0.154 | 0.091 |
| 8dbad495 (11-08) | 0.057 | 0.069 | 0.108 | 0.094 | 0.152 | 0.091 |

No commit carries the regression: the rows climb a few percent at a time across some 150 commits from
the value ownership stages to 10-19, and are flat since. Under the plan's rule a diffuse regression is
recorded, not undone; I1's compiled right-hand side, which removes the per-stage handle call these
rows pay for, is the fix. The same table shows `d16_ode15s_brussode200_full` at 0.099 s already at
401d34b, so its "gain" between the GUI rounds (0.516 to 0.109 s) was an app-side effect as well.

### P0.5 — the thread sweep (decision D5)

`probes\sweep\sweep_p05_kernels.m`, one process per policy, policies alternated inside each of three
repeats, warm medians (`runs\p0-8dbad495\sweep_p05`). A policy is `JGRAPH_THREADS` (read by
`ParallelKernels`) and a process affinity mask: the P-cores are logical processors 0-11 here, the
E-cores 12-19 (`GetLogicalProcessorInformationEx`). The plan's proposed `JGRAPH_KERNEL_THREADS` was not
added; P0 changes no engine code.

| row | default (16) | 1 | 6 | 6, P-cores | 12 | 12, P-cores | 20, all |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `elementwise_50M` | 0.334 | 0.567 | 0.367 | 0.286 | 0.340 | 0.366 | 0.337 |
| `mask_50M` | 0.058 | 0.120 | 0.059 | 0.060 | 0.062 | 0.060 | 0.058 |
| `reductions` | 0.088 | 0.090 | 0.090 | 0.090 | 0.091 | 0.088 | 0.087 |
| `normalize_10M` | 0.151 | 0.140 | 0.135 | 0.138 | 0.140 | 0.144 | 0.152 |
| `rescale_10M` | 0.103 | 0.104 | 0.099 | 0.093 | 0.102 | 0.096 | 0.106 |
| `int32_arith_20M` | 0.043 | 0.099 | 0.044 | 0.045 | 0.041 | 0.045 | 0.040 |
| `int32_saturate_20M` | 0.019 | 0.050 | 0.024 | 0.021 | 0.019 | 0.024 | 0.020 |
| `uint8_floor_20M` | 0.019 | 0.059 | 0.025 | 0.029 | 0.020 | 0.027 | 0.022 |
| `uint8_sum_20M` | 0.012 | 0.013 | 0.012 | 0.014 | 0.013 | 0.013 | 0.012 |
| `gamma_5M` | 0.061 | 0.113 | 0.059 | 0.046 | 0.068 | 0.080 | 0.057 |

Every policy printed the same CHK lines. `normalize`, `rescale`, the reductions and the `uint8` sum take
the same time on one thread as on twenty (they are not threaded), and the threaded rows stop gaining at
about six threads; P-cores alone against all cores is inside the noise. The proposed answer to D5:
keep the present default and look to the passes over memory (I5), not to the thread policy.

## Consequences

- The plan is at rev 2: P0.1, P0.3, P0.4 and P0.5 rewritten as above, P0 done, and I11's first moves
  named — the app's collector (a `runtimeconfig.template.json` for `JGraph.Application` measured on the
  whole round), `ifft`'s conjugate-symmetry test, complex-real arithmetic, logical `sum`/`nnz`/`isnan`,
  the one-vector gather, `trace`. I1 starts on the compiled right-hand side with no regression to undo.
- Open item 251 stays open, absorbed by I1; open item 252 filed (`histogram`'s bin count).
- Nothing in the repository changed but this document and the harvest. The rig's new runners,
  probes, instrumented copies and run folders live in `JGraph_demo_workspace\head2head_v3`.

## Divergences

- **`ifft` of a conjugate-symmetric input answers complex.** R2025b tests whether the input is
  conjugate-symmetric along the transform dimension and, when it is, answers a real array; JGraph
  answers real only when `'symmetric'` is given, so `isreal(ifft(fft(x)))` is 0 in JGraph and 1 in
  R2025b and every later operation takes the complex road (`d02`'s round-trip line, 0.62 s against
  0.007). The every-row plan's I11 takes it.
- **`histogram(y, nbins)` and `'NumBins'` give the automatic binning.** `histogram(y, 120)` draws 40 bins
  with `BinMethod` 'auto' where R2025b draws 120, 'manual'; `histcounts(y, 120)` agrees. Open item 252.

## Still open

- I11's fixes, each measured in the app as well as headless; whether the app takes Server GC (its
  memory and pause behaviour in an interactive session were not measured).
- The 12 recorded divergence CHK rows are unchanged; the probes' extra exact step counts are not
  parity claims.
