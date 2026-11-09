# ADR 0324 — The every-row plan's I1: the ODE right-hand side, compiled

## Status

Accepted, 2026-11-08. Stage I1 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 4); closes open item 251. Measured on the working tree over 5362a74b (Release,
`runs\i1-final` in `JGraph_demo_workspace\head2head_v3`, its manifest hashing the dirty files) on the
i7-12700H, nothing else of this work running, the user's word that the machine was idle.

## Context

The GUI round (ADR 0322) has 14 ODE rows in d08 and d16 at 1.0-7.3x R2025b, 0.59 s of excess. P0
found open item 251 diffuse: the rows climbed a few percent at a time over some 150 commits, no one
commit to undo, and the plan's answer to it is this stage: the non-stiff rows are one handle call per
stage over a state of three or four elements, so the cost is the call (a workspace, two bindings, a
walk of the body, a value minted and read back), not the arithmetic. The stiff rows pay the same
call from the Jacobian's column differences and the Newton iterations, and the two brussode rows
turned out to pay for something else entirely (below).

## Decision

### The right-hand side compiled once per solve

`Interpreter.TryCompileOdeBody` (`src/JGraph.Scripting/Jgs/Interpreter.OdeBody.cs`) compiles an
anonymous `@(t, y) body` when the solver is about to run it and the body is, element by element,
scalar arithmetic over:

- the first parameter, and `y(k)` with a literal `k` inside the state (the whole state bare only
  when it has one element);
- real double scalars the handle captured (read at compile time: the snapshot cannot change during
  the solve, since nothing the whitelist admits reaches it);
- the constants the compiled loop folds (`pi`, `eps`, `Inf`, ...; the layer holds most as the number
  itself);
- `+ - * / \ ^` and their dotted forms, unary minus and plus;
- the compiled loop's scalar builtins (`sin cos tan atan exp abs floor ceil round sqrt log log10 asin
  acos`) and `mod`, `rem`, `atan2`, each one only when the name resolves to the builtin from the
  handle's file (`JgsNameResolver.CompiledBuiltin`).

A column `[e1; e2; ...]`, a row `[e1, e2, ...]` or one scalar expression, as many elements as the
state. The result is a postfix program (`CompiledOdeBody`) that the solver's stages run over the
solver's own state array, writing a fresh slope array; no value is minted, no workspace made and no
tree walked per stage. Every op binds the arithmetic the walk applies to two real double scalars,
the same table the compiled loop binds (ADR 0160), so the two roads give the same doubles: a power
is `Math.Pow` behind `PowerStaysReal`, a domain-limited builtin runs behind its guard. When a guard
fails (a power or a builtin that would leave the reals) that one evaluation walks, which raises
whatever the walk raises. Anything else refuses: a call to a script function, an index into a
captured array, a comparison, a captured single or integer, a debug hook attached, three
parameters; the handle is then called as before. The compiled road polls the run's cancellation
every 1024 evaluations. `JGRAPH_ODE_COMPILE=0` turns it off and `JGRAPH_ODE_TRACE=1` writes every
refusal and its reason to standard error. Events, output, mass and Jacobian handles are not
compiled.

### The stiff solvers' banded iteration matrix

`d16_ode15s_brussode200_pattern` and `_full` call a script function (`brussrhs`, a loop the loop
compiler already runs), which the compiled body refuses, and a split of the row
(`probes\instrumented\split_i1_bruss.m`, and a C# harness calling `Ode15s.Run` with a C# right-hand
side) showed where their time went: 22 dense LU factorizations of the 400-by-400 iteration matrix at
2.5-3 ms each, and 30 ms of `colgroup`.

- **`BandedLu`** (`src/JGraph.Numerics/LinearAlgebra/BandedLu.cs`): LU with partial pivoting in
  LAPACK's band layout (`dgbtf2`/`dgbtrs`, unblocked). `BandedLu.FactorIteration` is what ode15s,
  ode23s, ode23t and ode23tb factor their iteration matrix with: the band of its actual nonzeros is
  measured, and when 2·kl + ku + 1 is at most an eighth of n (n ≥ 32) it is factored in band form,
  else dense through `LuDecomposition` as before; a zero pivot also goes to the dense one, so a
  singular matrix is refused in its words. Brussode's matrix has kl = ku = 2 with or without
  `JPattern` (a differenced Jacobian's columns are exactly zero where the function does not read the
  state), so both rows take it; R2025b factors the patterned case sparse. vdp's 2-by-2 never does,
  and its four rows cannot move.
- **`colgroup`'s overlap table** (`OdeNumericalJacobian.ColumnGroups`) was formed by asking every
  pair of columns about every row: 32 million reads for 400 columns, once per solve. It is now
  counted row by row over each row's own columns, the same counts (only zero against nonzero is
  read), the same groups.

### Measured

`measure_i1.ps1 -Stamp i1-final` and `compare_i1.py`: d08 and d16 as written, one warm-up and three
timed passes per session, the compiler on and off (`JGRAPH_ODE_COMPILE=0`, the same binary)
alternated three times headless and three times in JGraph.Application; medians of all nine timed
passes per condition, seconds. The two R2025b and JGraph GUI columns are the GUI round's.

| row | R2025b GUI | JGraph GUI (8dbad495) | app, compiler off | app, on | headless, on |
| --- | --- | --- | --- | --- | --- |
| `d08_lorenz` | 0.004 | 0.029 | 0.015 | 0.001 | 0.001 |
| `d16_ode45_lorenz200` | 0.012 | 0.070 | 0.054 | 0.003 | 0.003 |
| `d16_ode45_orbit_tight` | 0.010 | 0.053 | 0.043 | 0.003 | 0.004 |
| `d16_ode23_lorenz200` | 0.012 | 0.086 | 0.073 | 0.003 | 0.003 |
| `d16_ode113_lorenz200` | 0.049 | 0.055 | 0.042 | 0.003 | 0.004 |
| `d16_ode78_lorenz200` | 0.025 | 0.083 | 0.070 | 0.003 | 0.004 |
| `d16_ode89_lorenz200` | 0.029 | 0.123 | 0.103 | 0.005 | 0.006 |
| `d16_ode78_orbit_tight` | 0.007 | 0.026 | 0.022 | 0.002 | 0.003 |
| `d16_ode89_orbit_tight` | 0.008 | 0.028 | 0.023 | 0.002 | 0.003 |
| `d16_ode23s_vdp1000x20` | 0.197 | 0.201 | 0.155 | 0.030 | 0.025 |
| `d16_ode23t_vdp1000x20` | 0.082 | 0.130 | 0.091 | 0.032 | 0.033 |
| `d16_ode23tb_vdp1000x20` | 0.103 | 0.144 | 0.123 | 0.027 | 0.020 |
| `d16_ode15s_brussode200_pattern` | 0.027 | 0.087 | 0.021 | 0.031 | 0.029 |
| `d16_ode15s_brussode200_full` | 0.056 | 0.101 | 0.050 | 0.054 | 0.066 |
| `d16_total` | 1.761 | 2.282 | 2.157 | 1.323 | 1.300 |
| `d16_solvers` wall | 1.770 | 2.284 | 2.159 | 1.325 | 1.313 |
| `d08_dynamics` wall | 1.788 | 0.116 | 0.110 | 0.087 | 0.092 |

The "compiler off" column already has the banded LU and the grouping; the brussode rows before them
were 0.093 and 0.098 in the app (`runs\i1-wip`). Two rows outside I1 moved with it:
`d16_ode15s_vdp1000x20` 0.099 → 0.028 and `d16_ode45_400calls` 0.024 → 0.004.

**The brussode rows and the tiered JIT.** Twelve rows are 3-20x under R2025b in every session. The
two brussode rows are under R2025b in every compiler-off session and at pass 3 of every compiler-on
session (0.020-0.022 and 0.049-0.056), but over it in passes 1 and 2 of the compiler-on sessions,
which lifts their three-pass medians to 0.029-0.031 (pattern) and 0.054-0.066 (full). The difference
is the order of the script, not the brussode code: with the explicit rows compiled, the interpreter's
call and loop paths are hardly run before brussode, so they are still being promoted by the tiered
JIT during its first passes; with them walked, the earlier rows warm those paths. Measured on the
same binary: `DOTNET_TieredCompilation=0` makes it worse (0.024-0.026 and 0.065, without the
profile-guided code), and `DOTNET_TC_CallCountingDelayMs=0` puts the full row under R2025b from pass 1
(0.052) and `d16_total` at 1.09-1.21 s, with the pattern row still 0.032 at pass 1
(`runs\i1-final\diag-*`). A process-wide JIT setting is I13's to decide over all sixteen scripts, so
the rows go there as its first case; the acceptance's "all 14 rows" is met at steady state and not
yet in the three-pass median for these two.

### Checks

- A 1.87 MB `num2hex` dump of every solver on Lorenz, the orbit, captured scalars, the scalar
  builtins, a row answer, events, the four stiff solvers' outputs and counts, a power leaving the
  reals and a wrong-length body is byte-identical with the compiler on and off.
- Every d08 and d16 CHK line (54) equal to the GUI round's JGraph pass 1 in all twelve sessions of
  the measurement, on and off.
- `OdeBodyI1Tests` (20): each case both roads, the same bytes and outcome, and the road it must take
  by the compiler's counters (compiled, walked for one evaluation, refused).
- `BandedLuI1Tests` (8): solves against the dense factorization's to rounding with row interchanges
  on every step, residuals of the dense order, the matrices declined; `ColumnGroupsI1Tests` (5): the
  row-by-row count gives the pair-by-pair groups on banded, random and degenerate patterns.
- The ODE, solver, loop-compiler and LU test classes and the parity suite in both storages: 1050
  of 1050 in each.
- `run-stress.ps1` over the Debug build: 88 of 89; `stess_40` section 14 fails the same way on the
  I11 build (a stale expectation of the class-file name refusal's wording, filed as open item 254).
- The seven verify scripts green; the harvest unchanged (1343 rows from 217 ADRs).

## Consequences

- The plan is at rev 4: I1 done, its rows measured, the brussode warm-up handed to I13 with the
  `TC_CallCountingDelayMs` lever and its first numbers.
- Open item 251 closed: the diffuse climb is irrelevant once the per-stage call is gone.
- The rig gained `measure_i1.ps1`, `compare_i1.py` and `probes\instrumented\split_i1_bruss.m`
  (outside the repository). `compare.txt` is written UTF-16 by the PowerShell redirection, as I11's
  was.

## Divergences

None. The compiled body gives the walk's doubles; the banded factorization rounds differently from
the dense one where it is taken, as R2025b's own sparse and dense factorizations differ from each
other, and no CHK line or fixture moved.

## Still open

- The brussode rows' three-pass medians in a compiler-on session (I13).
- A script-function right-hand side (brussode's) still costs a walked call per evaluation, about
  40 µs warm; the compiled loop's dispatch is I2's.
