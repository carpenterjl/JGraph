# ADR 0335 — The every-row plan's I13: compilation mode measured over the whole round, and left as it is

## Status

Accepted, 2026-11-11. Stage I13 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 14). **Done, as a measurement**: nothing changes in the build or the runtime
configuration.

Over the whole round, ReadyToRun is slower than the tiered JIT. So is the no-tiering ceiling. Starting
call counting at once is inside the noise. Every CHK line is identical in every condition.

## Context

The plan's I13 targeted the tiered JIT's first-call cost: a benchmark row is one call, so its loops can
run tier-0 code (ADR 0158, and I8's first measurement, ADR 0333). Earlier stages handed it these cases:

- d16's brussode rows over R2025b in passes 1-2 (I1, ADR 0324);
- fzero's and the emitted loop's first pass (I2, ADR 0325);
- regexp/regexprep reaching their code before tier-up once I7 made earlier rows faster (ADR 0326).

The plan asked for four measurements:

- (a) `DOTNET_TieredCompilation=0` as the ceiling;
- (b) ReadyToRun compilation in the Release build, which D2 allows, its size recorded;
- (c) `AggressiveOptimization` on new hot loops, by rule;
- (d) `DOTNET_TC_CallCountingDelayMs=0`, measured beside (b).

## Decision

Keep the shipping build as it is: framework-dependent, IL only, tiered compilation with its defaults.
Rule (c) stays: every per-element loop a row calls once carries `AggressiveOptimization`, together with
its leaves. Since I8, the stages that wrote such loops marked them from the start, and that rule is what
the measurement credits.

No ReadyToRun option is added to `installer\build-installer.ps1`, and no runtime knob is added to either
`runtimeconfig.template.json`.

## Measured

`head2head_v3\measure_i13.ps1` and `compare_i13.py`, `runs\i13-second`. The tree was 4e0c2e2a (I12 landed),
the sixteen scripts as written, headless. Five conditions:

| Condition | What |
|---|---|
| `plain` | `dotnet publish -c Release -r win-x64 --self-contained false`, as the installer publishes |
| `r2r` | the same with `PublishReadyToRun=true` and crossgen2 `--instruction-set:avx2,bmi,bmi2,lzcnt,movbe,popcnt,fma` |
| `tc0` | `plain` under `DOTNET_TieredCompilation=0` (every method optimised at its first call) |
| `delay0` | `plain` under `DOTNET_TC_CallCountingDelayMs=0` |
| `r2rd0` | `r2r` under `DOTNET_TC_CallCountingDelayMs=0` |

There were five repeats, each running every condition once in rotated order: one session of warm-up and
three timed passes over all sixteen scripts, 25 sessions in all. Per condition, each operation row's median
is taken over all 15 timed passes; there are 184 rows, the `*_total` rows left out.

| Condition | Sum of row medians (s) | Rows at or under R2025b | Sum of script walls (s) |
|---|---|---|---|
| plain | 7.029 | 157 | 10.409 |
| r2r | 7.323 | 160 | 10.994 |
| tc0 | 7.260 | 159 | 10.786 |
| delay0 | 7.006 | 158 | 10.372 |
| r2rd0 | 7.226 | 156 | 11.116 |

R2025b's GUI sum over the same rows is 37.254 s. The GUI round's JGraph sum was 14.615 s.

- **ReadyToRun costs time.** Precompiled code runs until call counting promotes it, and it is weaker code
  than tier 1: no dynamic PGO, no cross-assembly inlining outside the version bubble, and no
  `Vector<T>`-width specialisation beyond the named instruction set. The largest losses are in rendering:
  `d06_panel_export` 0.610 → 0.744 and `d06` wall 1.227 → 1.386. The three extra rows at or under R2025b
  are noise-level rows that swing either way between conditions.
- **The ceiling does not win either.** With tiering off, every method is compiled optimised at its first
  call, but without PGO, and the compile time is paid inside the timed passes: `d13_bitops` 0.181 → 0.215,
  `d12_str2double` 0.108 → 0.134, `d16_ode15i` 0.210 → 0.253. The tier-0 cost the plan was written against
  is no longer a whole-round cost.
- **`DOTNET_TC_CallCountingDelayMs=0` is noise.** It is 0.023 s and one row better over the round, and
  some rows move each way (`d12_str2double` 0.090, `d16_ode23tb` 0.040). There is also no documented
  runtimeconfig setting for it; only the environment variable was measured.
- **The rows handed to I13 are met.** In the plain condition, the medians are:

  | Row | plain | R2025b |
  |---|---|---|
  | `d16_ode15s_brussode200_full` | 0.051 | 0.056 |
  | `d16_ode15s_brussode200_pattern` | 0.022 | 0.027 |
  | `d16_fzero_2000` | 0.005 | 0.004 |
  | `d12_regexprep_20k` | 0.056 | 0.104 |
  | `d12_regexp_match_5k` | 0.013 | 0.021 |

  `d16_fzero_2000` is 0.004 in the other four conditions, within D3's timer floor. `d03_loop_2M` (0.015
  against 0.010) is I2's chain floor in every condition.

**Builds.** The publish folder is 139.1 MB plain and 183.0 MB ReadyToRun, in 401 files either way:
- `JGraph.Scripting.dll`: 11.6 → 28.2 MB;
- `JGraph.Numerics.dll`: 0.7 → 2.5 MB.

The rig's `dotnet build` folder is 364.5 MB, because it also carries every runtime's native assets.

**CHK lines.** All 244 CHK lines of the sixteen scripts are identical across all 25 sessions. A compilation
mode does not change arithmetic, and the fixture confirms it.

## Consequences

- A row over R2025b after this is an algorithm or a memory-pass problem, never a compilation-mode problem.
  The 27 rows still over in the plain condition belong to their items and open items:
  - I3/I4's LAPACK-bound rows: chol, qr, svd, null, orth and pinv (item 274), plus hess and norms
    one millisecond over;
  - I5's 8-byte storage rows and windows: mask, int32 arith and saturate, uint8_floor (item 261); cumsum
    (264); movstd, movmedian and movmean (263); gamma and gammaln (262);
  - I9's `d06_generate` and `d06_threshold` (items 269, 270);
  - I7's `d12_charmatrix` and `d12_predicates` (items 257, 258);
  - I2's `d03_loop_2M` (its chain floor);
  - `d16_fzero_2000` (0.005 against 0.004; 0.004 in the other four conditions), and the two d09 interp1
    rows at 0.004-0.005 (I8 met them in the app);
  - `d16_cumtrapz_10M` (0.102 against 0.080; bimodal, and under in the GUI round's app: open item 282).

  `d02_fft_4M` is 0.037 here against 0.040.

  I14's closing round judges them by D3's rule.
- `measure_i13.ps1` stays in the rig, so a later stage can repeat the comparison if the runtime or the
  build changes (a .NET upgrade, Native AOT).

## Divergences

None.

## Still open

- **`d16_cumtrapz_10M`** (open item 282): 0.102 against R2025b's 0.080 in every condition. It is not a
  regression. Headless it was already 0.108 at the baseline commit, 8dbad495 (`runs\p0-8dbad495`). Its
  passes are bimodal, near 0.05 and near 0.10 (this run had both). The GUI round's 0.059, in the app,
  was the low mode.

Every other row over R2025b is tracked by its item and the open items listed above.
