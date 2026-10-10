# ADR 0221 — lu's one output is LAPACK's matrix, and its lower factor is written by column: open item 5

## Status

Accepted, 2026-10-10. The tenth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), with ADRs 0222 and 0223. Recorded from R2025b (probe `probe_5` in the open-items scratch,
then the fixture `oi_lu_forms`).

## Context

- **Item 5** asked for `lu`'s three output fills to be written column by column, after item 10a's
  measurement (ADR 0157) put them at about 19 ms each at n = 2000, more than the factorization. Two
  of the three had already been rewritten that way (`FillUpper`, and `FillLower` without a
  permutation copy their columns); what was left was the two-output L, which walked every element
  of every column through the inverse permutation with two branches, and the one-output form.
- **The one-output form was wrong.** It built PᵀL + U − I element by element. R2025b's one output is
  LAPACK's own matrix, L − I + U with the permutation dropped (`isequal(Y, L + U - eye(n))` for the
  three-output L, and `isequal(diag(Y), diag(U))`, both true in R2025b and false here). It was also
  not U's bits: the diagonal was `(1 + u) − 1`, which loses U's low bits (−0.49999999999999967 for
  R2025b's −0.5 on `[1 2 3; 4 5 6; 7 8 10]`).

## Decision

### The one output is the factors

`lu(A)` with one output answers the factored matrix as it stands. It is LAPACK's answer, and a copy
of it, so its triangles are the three-output L and U bit for bit.

### The two-output L scatters each column

A column of the two-output L is the factored column's one and the entries below it, each written
to the row of A that factored row came from (`order[s]`); the destination arrives zeroed, so the
n − c entries of column c are all that is written. The answer is the same bits as before
(`isequal(L2, P' * L)` for n = 1, 2, 3, 7, 64 and 257, rank-deficient from 7 on).

### Measured, as far as the machine allowed

The 2026-10-10 runs were made while other work loaded the machine: `det(A)` alone at n = 2000 ranged
from 0.12 to 0.20 s across seven repeats, where ADR 0157's quiet run had it at 0.039 s. Against that
noise the fills cannot be separated (Release, `time_5.m` in the open-items scratch, before 0.140 /
0.165 / 0.164 s minimum for `Y = lu(A)`, `[L, U]`, `[L, U, P]`, after 0.141 / 0.155 / 0.179 s). The
one-output form now does one copy where it did n² branches, and the two-output form half the
writes; the rig's `run_repeats.ps1 -Scripts d01_linalg` on a quiet machine is still owed.

## Divergences

None. The one-output divergence this ADR found and closed was a silent wrong answer, not a recorded one.

## Consequences

A script that reads `lu(A)`'s single output as LAPACK's packed factors — to pass it to code written
for `dgetrf`'s layout, or to read U's diagonal from it — now gets them. Tests:
`MatlabLinalgProviderM89Tests.LuWithOneOutputHoldsBothFactorsAtOnce` (rewritten to R2025b's form) and
`LuTwoOutputLowerIsThePermutedLowerBitForBit`; fixture `oi_lu_forms` (27 lines).

## Still open

- The d01_lu_2000 timing on a quiet machine with the rig (`run_repeats.ps1`, `run_probes.ps1
  -Probes probe_d01_lu_2000`).
