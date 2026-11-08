# ADR 0321 — lu's output fills timed on a quiet machine: open item 90

## Status

Accepted, 2026-11-08. The thirty-first batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored); a measurement, no code changed. Closes what ADR 0221 left open.

## Context

- **Item 5** (ADR 0221, commit 68a8f3df, 2026-10-10) rewrote the two-output `lu`'s lower factor to
  scatter each column and made the one-output form a copy of LAPACK's factors, after ADR 0157 had
  measured the three output fills at about 19 ms each at n = 2000, more than `dgetrf` itself.
- **It could not be timed.** Other work loaded the machine that day: `det(A)` at n = 2000 ranged
  0.12 to 0.20 s across seven repeats where ADR 0157's quiet run had it at 0.039 s. ADR 0221 recorded
  its three forms as 0.140 / 0.165 / 0.164 s before and 0.141 / 0.155 / 0.179 s after, and said the
  rig's run on a quiet machine was owed. Item 90 asked for it: `run_repeats.ps1 -Scripts d01_linalg`
  and `run_probes.ps1 -Probes probe_d01_lu_2000` against the 10c binary, then `summarize_runs.py`.

## Decision

### What was run

The head2head_v3 rig (`JGraph_demo_workspace\head2head_v3`, outside the repository) on 2026-11-08
between 00:07 and 00:24, with no build, test run or MATLAB session of any other work on the machine
(processor load under 15% before the start; the power plan was the ASUS "Silent" scheme, recorded
in every manifest). Three binaries, each measured in a stamp of its own, five repeats each,
counterbalanced, the median reported:

| stamp | binary | provenance |
| --- | --- | --- |
| `90-10c` | `runs\10c-stiff-scratch\bin\jgraph.exe`, the binary item 5 named | commit c7bce1ca (2026-09-12) built dirty on 2026-09-13; hashed in place |
| `90-before-0221` | the commit before ADR 0221, 6bb588e3 | a detached worktree built Release by `manifest.ps1 -Repo` (its manifest kept as `manifest.build.json`) |
| `90-head` | HEAD, 732e8b25 | built Release by `manifest.ps1` |

Each stamp ran the `d01_linalg` script (the row `d01_lu_2000` is `[L, U, P] = lu(A)` inside the
script's process) and the probe `probe_d01_lu_2000` (the same call alone, once cold and three times
warm). A second pass, stamps `90-10c-forms`, `90-before-0221-forms` and `90-head-forms`, ran a new
probe `probe_d01_lu_forms.m` (added to the rig's `probes` folder): `det(A)`, which is the marshalling
copy and `dgetrf` with no output fill, then `Y = lu(A)`, `[L2, U2] = lu(A)` and `[L, U, P] = lu(A)`,
each once cold and three times warm in one process. Every probe process exited 0 in both engines,
and every `CHK` line is the same across the three binaries and matches R2025b's (the residuals at
1.3e-16 against R2025b's 1.41e-16, the diagonal sums equal to ten digits).

### What was measured

Warm medians of five repeats, seconds, n = 2000. R2025b's column is the same probe in
`matlab -batch` on the same machine, in the same pass.

| piece, warm | 10c | before 0221 | HEAD | R2025b |
| --- | --- | --- | --- | --- |
| `det(A)`: the copy and `dgetrf`, no fill | 0.0382 | 0.0371 | 0.0368 | 0.0334 / 0.0303 / 0.0304 |
| `Y = lu(A)`, one output | 0.0557 | 0.0487 | 0.0444 | 0.0332 / 0.0308 / 0.0310 |
| `[L, U] = lu(A)`, two outputs | 0.0688 | 0.0551 | 0.0534 | 0.0362 / 0.0347 / 0.0347 |
| `[L, U, P] = lu(A)`, three outputs | 0.0840 | 0.0578 | 0.0562 | 0.0389 / 0.0367 / 0.0366 |
| probe `d01_lu_2000`, warm | 0.0857 | 0.0576 | 0.0593 | 0.0416 / 0.0406 / 0.0413 |
| script row `d01_lu_2000` | 0.082 | 0.056 | 0.056 | 0.042 / 0.040 / 0.041 |

The ranges (min to max of five) that decide what is separable:

- `Y = lu(A)` warm: before 0221 0.0455–0.0612, HEAD 0.0433–0.0495. The medians differ by 4.3 ms and
  HEAD's whole range lies under the other's median: **ADR 0221's one-output change is measurable**,
  about 4 ms, or 1.10x on the form (the n² branched fill of PᵀL + U − I became one copy of the
  factors).
- `[L, U] = lu(A)` warm: before 0221 0.0537–0.0559, HEAD 0.0501–0.0570. The medians differ by 1.7 ms
  inside overlapping ranges: **the two-output scatter is not separable** from run-to-run noise on
  this machine. It halved the writes of one fill (about 6 ms of work by ADR 0157's figure); the
  saving, if any, is under the 3 ms the ranges span.
- `[L, U, P] = lu(A)` warm: 0.0578 against 0.0562, and the script row 0.056 against 0.056. ADR 0221
  did not touch the three-output form (`FillUpper` and `FillLower` without a permutation were already
  column copies), so this is the noise floor: about 1.5 ms, 3%.
- `det(A)` warm: 0.0382, 0.0371, 0.0368. The factorization and the marshalling copy are the same
  across all three binaries, so every difference above is in the outputs.

### Where the 10c gain came from

Item 5 asked for the measurement against the 10c binary, and that comparison shows a 1.46x on the
row (0.082 to 0.056 s) and 1.45x on the warm probe. It is not ADR 0221's: the three-output form's
fills did not change between the two commits (`git log c7bce1ca..6bb588e3 --
src/JGraph.Scripting/Jgs/JgsBuiltins.LinalgForms.cs` is empty, and the ownership audit of 2026-09-15,
commit 04f1d96a, already lists `FillUpper` and `FillLower` as `factors.Slice(...).CopyTo(...)` column
copies), and `det` is unchanged. What fell is the cost per output returned: about 5 ms for one
output (0.0557 − 0.0382 against 0.0487 − 0.0371), 13 ms for two, 25 ms for three, which is 6 to 8 ms
per n-by-n double matrix (32 MB) handed back to the script. That is the shape of a whole-array copy
per bound output, and the value ownership plan (ADRs 0162 to 0173, 2026-09-14 to 2026-09-27) landed
between the two commits with exactly that purpose. The ADR records the attribution as consistent
with the evidence rather than isolated to a commit: no bisection of the ownership stages was run,
since none of them is this item's.

### Where the row stands

JGraph's three-output `lu` at n = 2000 is 0.056 s against R2025b's 0.037 s warm (1.5x, from 2.26x at
10c and 1.95x on the script row). Of JGraph's 0.056 s, 0.037 s is the copy and `dgetrf` (R2025b's
`det`, the same factorization without a fill, is 0.030 s: 1.2x) and about 19 ms is the three
fills. R2025b spends about 6 ms over its `det` for its three outputs. The one-output form is 0.044 s
against 0.031 s (1.4x), the two-output 0.053 s against 0.035 s (1.5x). The remaining fill cost is
the three n-by-n writes themselves; no further change is filed by this item, since a fill cheaper
than a column copy means returning views of the packed factors, which ADR 0157 ruled out for outputs
the script keeps.

## Divergences

None. A timing is not a behaviour; the `CHK` lines agree with R2025b's to the digits each prints.

## Consequences

ADR 0221's "Still open" is closed by this record. The rig's `probes` folder gains
`probe_d01_lu_forms.m`, which any later change to `lu`'s outputs can be measured with by stamp. The
run folders `runs\90-*` hold every sample, log, manifest and the three-way `summarize_runs.py`
output; the manifests of the `90-before-0221*` stamps name HEAD (the rig hashes a given binary in
place and reads the repository's tree), so each carries a `manifest.build.json` naming the commit
the binary was built from. Nothing in the repository changed but this document and the harvest.

## Still open

- Nothing from item 90. The row's remaining 1.5x is `dgetrf` at 1.2x of MATLAB's and three fills
  that are already copies; neither has an item.
