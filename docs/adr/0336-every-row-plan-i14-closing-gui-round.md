# ADR 0336 — The every-row plan's I14: the closing GUI round

## Status

Accepted, 2026-11-11. Stage I14 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 15). **Done, as the round**: it was run and judged. The plan is **not complete**:
every script wall is at or under R2025b, but 30 of 184 operation rows are not. By §6 those rows go
back to their items, and the plan stays in `docs/plans/`.

## Context

§6 of the plan closes it with one GUI round taken the same way as the 2026-11-08 baseline:
- both applications open, with JGraph's `launch.m` started through UI Automation and R2025b's Run
  pressed by the user;
- a warm-up pass and three timed passes;
- a new results folder;
- the judgement: every row and every script wall at or under R2025b's median, and every CHK line
  equal to the baseline's.

I1-I13 had landed (ADRs 0322-0335). The tree was 603d8474 and the Release build was
`JGraph.Application` (Server GC since I11).

## The round

`JGraph_demo_workspace\head2head_GUI_2026-11-11` holds the round. Its sixteen scripts and its
`launch.m` are byte for byte those of the 2026-11-08 round. The report was generated with
`generate_results.py` (`python -I`), and `head2head_v3\compare_i14.py` compares the two rounds.

| | Baseline 2026-11-08 | Closing 2026-11-11 |
|---|---|---|
| JGraph session | 100.2 s | 49.4 s |
| R2025b session | 216.4 s | 223.3 s |
| Errors | 0 / 0 | 0 / 0 |
| Sum of JGraph's row medians | 14.615 s | 7.261 s |
| Sum of R2025b's row medians | 37.254 s | 36.752 s |
| Rows at or under R2025b (D3: ties count) | 99 of 184 | 154 of 184 |
| Sum of script-wall medians, JGraph / R2025b | 24.003 / 48.414 s | 10.530 / 47.863 s |
| Script walls lost | 4 (d02, d10, d11, d16) | **none** |

Script walls, JGraph against R2025b in the closing round:

| Script | Walls (s) | Script | Walls (s) |
|---|---|---|---|
| d01 | 0.908 / 1.608 | d09 | 0.418 / 12.256 |
| d02 | 0.551 / 1.694 | d10 | 0.370 / 0.966 |
| d03 | 1.387 / 2.851 | d11 | 1.016 / 2.304 |
| d04 | 0.477 / 4.065 | d12 | 0.416 / 1.832 |
| d05 | 0.833 / 3.143 | d13 | 1.048 / 3.317 |
| d06 | 1.172 / 3.881 | d14 | 0.023 / 1.491 |
| d07 | 0.322 / 3.811 | d15 | 0.681 / 1.280 |
| d08 | 0.092 / 1.696 | d16 | 0.816 / 1.668 |

**CHK.** All 244 lines are present in both engines.

- 232 are equal to R2025b. The 12 that differ are the same 12 recorded divergences as at the
  baseline: the d01 residuals, `d02_roundtrip`, `d08_lorenz_steps` and the d10 residuals.
- Four of JGraph's lines moved from the baseline, all within those 12 and all at rounding level, as D1
  allows:
  - `d02_roundtrip` 1.79e-15 → 1.67e-15 (I11);
  - `d10_logm_roundtrip` 5.77e-15 → 7.33e-15 and `d10_sqrtm_resid` 1.71e-15 → 2.70e-15 (I4, D4);
  - `d10_pinv_resid` 1.69e-14 → 1.61e-14 (I4).
- R2025b's 244 lines are identical between its two rounds, and each engine's lines are identical across
  its three passes.

## Rows over R2025b in the closing round

Medians over the three timed passes, in seconds.

**25 rows are over R2025b in both rounds** (each goes back to its item):

| Row | JGraph | R2025b | Item |
|---|---|---|---|
| `d03_cumsum_20M` | 0.088 | 0.043 | I5, open item 264 |
| `d11_movmedian_2M_w21` | 0.062 | 0.023 | I5, 263 (passes 0.011 / 0.062 / 0.090; it was 0.013 / 0.045 / 0.086 at the baseline) |
| `d11_movstd_10M_w51` | 0.043 | 0.030 | I5, 263 |
| `d03_mask_50M` | 0.056 | 0.018 | I5, 261 |
| `d13_int32_arith_20M` | 0.025 | 0.009 | I5, 261 |
| `d13_int32_saturate_20M` | 0.012 | 0.006 | I5, 261 |
| `d13_uint8_floor_20M` | 0.013 | 0.002 | I5, 261 |
| `d13_gamma_5M` | 0.021 | 0.012 | I5, 262 |
| `d13_gammaln_5M` | 0.016 | 0.010 | I5, 262 |
| `d01_svd_800` | 0.070 | 0.049 | I3, 274 |
| `d01_qr_1200` | 0.073 | 0.052 | I3, 274/275 |
| `d01_chol_2000` | 0.024 | 0.014 | I3, 274 |
| `d10_pinv` | 0.018 | 0.016 | I3/I4, 274 |
| `d10_orth` | 0.018 | 0.017 | I3, 274 |
| `d10_norms_400` | 0.010 | 0.009 | I3 |
| `d02_fft_4M` | 0.052 | 0.028 | I12, 280 |
| `d16_cumtrapz_10M` | 0.102 | 0.085 | I5, 282 (bimodal: passes 0.051 / 0.102 / 0.108) |
| `d01_matmul_2000` | 0.089 | 0.074 | open item 283 (bimodal: passes 0.054 / 0.089 / 0.090) |
| `d16_ode15s_brussode200_full` | 0.066 | 0.059 | I1 |
| `d03_loop_2M` | 0.014 | 0.011 | I2, its dependency chain's floor |
| `d16_fzero_2000` | 0.005 | 0.004 | I2, at the timer floor (D3) |
| `d12_charmatrix` | 0.005 | 0.002 | I7, 258 |
| `d12_predicates_200k` | 0.004 | 0.002 | I7, 257 |
| `d06_threshold` | 0.015 | 0.013 | I9, 270 |
| `d09_interp1_pchip_2M` | 0.004 | 0.002 | I8, at the timer floor |

**5 rows are over only because R2025b was faster today than at its baseline.** Against the baseline's
R2025b they are met:

| Row | JGraph | R2025b today | R2025b baseline | Item |
|---|---|---|---|---|
| `d09_ppval_1M` | 0.089 | 0.072 | 0.089 | open item 284 |
| `d16_ode15s_brussode200_pattern` | 0.024 | 0.021 | 0.027 | I1 |
| `d03_dimreduce` | 0.019 | 0.017 | 0.021 | I5 |
| `d09_interp1_spline_2M` | 0.004 | 0.003 | 0.004 | I8, at the timer floor |
| `d13_uint8_sum_20M` | 0.005 | 0.004 | 0.005 | I5 |

**Rows that were ahead at the baseline and are over now:** `d01_matmul_2000`, `d02_fft_4M`,
`d16_cumtrapz_10M` and `d09_ppval_1M`.
- Two of them did not change in code under the plan (I3 did not touch the matmul road), and none of the
  four is a code regression.
- I13's headless runs of the same tree (`runs\i13-second`, plain) already had matmul bimodal
  (0.049-0.091 by pass), fft_4M at 0.029-0.055 and cumtrapz at both modes.
- `d09_ppval_1M` reads 0.087-0.097 in every I13 pass and 0.086 at the baseline; R2025b's own median
  moved from 0.089 to 0.072.

**Slower than at the baseline, but still ahead:**
- `d04_hist_export` 0.025 → 0.035 (R2025b 0.470);
- `d06_blur_21tap` 0.013 → 0.018 (R2025b 0.031);
- `d12_regexp_match_5k` 0.012 → 0.016 (R2025b 0.022) and `d12_regexp_tokens_5k` 0.011 → 0.018
  (R2025b 0.038). I7 and I13 recorded these as a timed pass's tier-up and collector position, not code;
- `d15_filter_fir101_10M` 0.029 → 0.037 (R2025b 0.040; headless at I13 0.026-0.035).

`d06_generate_2048` (open item 269) is 0.087 against R2025b's 0.088 in this round: met, by a millisecond.

## What went wrong in the round, and what was recovered

R2025b's editor still had the **2026-11-08 round's** `matlab\launch.m` open from that day, and Run
was pressed on that tab. The launcher writes next to itself, so it wrote into
`head2head_GUI_2026-11-08\matlab\results`:

- **Logs: recovered.** `diary` **appends**, so each pass log held the baseline's block followed by
  the new run's. `head2head_v3\split_i14_matlab_logs.py` cut each of the 64 logs at its second
  `=== <script>: begin ===`. The first block, byte for byte, went back to the 2026-11-08 folder; the
  second stayed with this round.
- **Lost:** that folder's `passes.csv`, `session.txt` (rewritten with `fopen 'w'`) and the exported
  figures (same names).
  - The baseline's R2025b walls survive in that round's untouched `results\report.html`: with three
    timed passes, its minimum, median and maximum are the three values, though not their order. They
    also survive in the plan's workbook.
  - `head2head_GUI_2026-11-08\matlab\RESULTS_OVERWRITTEN.txt` says all of this, including the
    original session times.

The R2025b run is a valid session of the closing round: the same `launch.m` and scripts byte for byte,
in a folder whose path differs. Its whole results folder was moved into
`head2head_GUI_2026-11-11\matlab\results`. JGraph's side of the baseline was not touched.

For the next round, close every editor tab from an older round in R2025b before handing over, or run
the round in a folder whose `launch.m` is the only one R2025b has seen. Give the user the full path of
the `launch.m` to run, and check on the first `passes.csv` line that the new folder is the one being
written.

## Decision

- **I14 is done as the round.** The plan's goal (every row and every wall at or under) is not met.
  It stays in `docs/plans/` with its workbook, at rev 15, and each row above returns to the item
  named beside it.
  - Two open items are filed: **283** (`d01_matmul_2000` bimodal) and **284** (`d09_ppval_1M`
    against R2025b's faster day).
  - Items 261-264, 274, 275, 280 and 282 carry the closing round's numbers.
- **The four lost walls are won.** d02 is 0.55 s against 1.69 s, d10 0.37 against 0.97, d11 1.02
  against 2.30 and d16 0.82 against 1.67.
- **No code changes in this stage.**

## Divergences

None new. The four moved CHK lines are within the 12 recorded rows (D1, D4).
