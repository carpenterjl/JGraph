# ADR 0325 — The every-row plan's I2: scalar callbacks compiled, and the compiled loop emitted

## Status

Accepted, 2026-11-08. Stage I2 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 5). Measured on the working tree over af086fbc (Release, `runs\i2-final` in
`JGraph_demo_workspace\head2head_v3`, its manifest hashing the dirty files) on the i7-12700H with
nothing else of this work running and the user's word that the machine was idle.

## Context

The GUI round (ADR 0322) left four rows to I2: `d16_fzero_2000` 0.032 s against R2025b's 0.004,
`d16_integral_osc200` 0.387 against 0.198, `d03_loop_2M` 0.032 against 0.010 and `d03_funs` 0.221
against 0.215. The first three call an anonymous handle per point, panel or element, and the walk
pays a workspace, two bindings, a tree walk and a minted value on every call. `integral` asks for
one 15-point Gauss-Kronrod panel per call, so the oscillatory row is several hundred calls per
integral. The d03 loop is already ADR 0160's register program; what is left is the switch per op.

## Decision

### The scalar callbacks, compiled once per call

I1's compiler (`Interpreter.OdeBody.cs`) becomes one `BodyBuilder` with three shapes, the same
whitelist and the same arithmetic (each op binds what the walk applies to two real double scalars):

- **Ode** - `@(t, y)`, as ADR 0324 (types renamed `CompiledBody`, `BodyOp`, `BodyInstruction`).
- **Point** - `@(x)` over a solver's point of n elements, read as `x(k)` with a literal `k`, or
  bare when n is 1; one answer. `ObjectiveCaller` (`JgsBuiltins.Optimize.cs`) uses it, so
  `fzero`, `fminbnd` and `fminsearch` all take it when no extra arguments follow the options. A
  NaN under `FunValCheck`, or an op that would leave the reals, walks that point, which raises
  what the walk raises. `arrayfun` uses it for one element: one real double input, uniform output,
  no `ErrorHandler`, one or no outputs asked; the answer is built packed in the input's shape as
  the walk builds it, and an element whose answer leaves the reals sends the whole call to the
  walk from the start (the body can reach nothing with a side effect).
- **Batch** - `@(x)` over a panel of points, run element by element. `Integrate`
  (`JgsBuiltins.Solvers.cs`, so `integral` and `quadgk`) uses it for the vectorised road and for
  its midpoint probe. Only elementwise bodies compile: `x*x`, `x/x`, `x\k`, `x^2`, a varying
  `mod`/`rem` divisor and a bracket refuse, since the walk answers them as matrix operations or
  refusals, and so does a body that never reads the batch (the walk answers one value per panel,
  which `integral` refuses in R2025b's words). Below `PackedMath.ApproximateThreshold` (32768)
  the walk's vector kernels are `Math`'s own functions with no fused multiply-add, so per element
  the two roads give the same doubles; a panel past it is walked. A panel whose answer would
  leave the reals is walked whole.

`JGRAPH_FN_COMPILE=0` turns the three callback uses off (I1's `JGRAPH_ODE_COMPILE` still governs the
ODE shape), `JGRAPH_FN_TRACE=1` writes each refusal. The run's cancellation is polled every 1024
points, elements or 256 panels.

### What a handle made in a loop costs

`fzero` in a loop makes a new handle per pass, so three per-call costs outside the evaluation were
taken out:

- **A template per declaration.** A body whose names are only its parameters and captured
  variables is kept on its `AnonymousFnExpr` by shape; each new handle copies the constants and
  reads its own captured values into their slots (a capture that is no longer a real double
  scalar recompiles, and the walk takes it). A body that calls a builtin or folds a bare constant
  is compiled afresh each time, since the resolver's answer is the path's at that moment. A
  refusal about the body's shape alone is kept too, so a refused handle in a loop does not throw
  and catch a refusal per call.
- **Free names per declaration.** `AnonymousFunction.Create` walked the body for its free names
  and built a set per handle; it is now worked out once, in the same order.
- **A row of number literals.** `[0 50]` went through the twenty kinds of bracket join every time
  (1.6 µs). Once the walk has built a row of plain number literals and it came out a packed double
  row of exactly those bits, later evaluations copy it under the same dialect with packing on.
- **fzero's tail and defaults.** The exit message (two `%g` formats) and the output structure are
  made only when the fourth output is asked for or the display prints; `ReadOptimSettings` with
  no options keeps each solver's defaults per problem size (the defaults structure is some thirty
  fields).

### The compiled loop, emitted

`Interpreter.HotLoopNative.cs` emits a register program as a `DynamicMethod` (`NativeLoop`): every
register and slot flag an IL local, an IL `switch` to enter at any op, and the register machine's
own arithmetic per op (`ScalarMod`, `Math.Pow` behind `PowerStaysReal`, the kernels called
directly when they are static methods, each comparison and fused branch with NaN on the switch's
side). Anything the register machine hands to its C# - a bail, a walked statement, the step limit,
cancellation - leaves the method with a code after writing the locals back, and
`RunHotLoopNative` answers it with the same `HotLoopBail`, spill and throw as before, then
re-enters at the op the bail names. A program with a vector op is not emitted. A loop starts on
the register machine and moves, registers and all, at its 4096th back edge
(`JGRAPH_LOOP_NATIVE_AFTER`), so a short loop never pays for an emission; `JGRAPH_LOOP_NATIVE=0`
turns it off.

### Measured

`measure_i2.ps1 -Stamp i2-final` and `compare_i2.py`: d03 and d16 as written, one warm-up and three
timed passes per session, I2 on and off (`JGRAPH_FN_COMPILE=0,JGRAPH_LOOP_NATIVE=0`, the same
binary; the template, literal and fzero-tail changes have no switch and are in both) alternated
three times headless, then one JGraph.Application session per condition; medians of the timed
passes, seconds. The two GUI columns are the GUI round's.

| row | R2025b GUI | JGraph GUI (8dbad495) | headless off | headless on | app off | app on |
| --- | --- | --- | --- | --- | --- | --- |
| `d16_fzero_2000` | 0.004 | 0.032 | 0.019 | 0.004 | 0.019 | 0.006 |
| `d16_integral_osc200` | 0.198 | 0.387 | 0.339 | 0.144 | 0.423 | 0.142 |
| `d03_loop_2M` | 0.010 | 0.032 | 0.031 | 0.013 | 0.031 | 0.013 |
| `d03_funs` | 0.215 | 0.221 | 0.147 | 0.009 | 0.160 | 0.008 |
| `d16_integral_2000` | 0.075 | 0.060 | 0.047 | 0.017 | 0.075 | 0.017 |
| `d16_quadgk_inf500` | 0.034 | 0.024 | 0.019 | 0.010 | 0.020 | 0.011 |
| `d16_fminsearch_200` | 0.047 | 0.038 | 0.035 | 0.006 | 0.037 | 0.006 |
| `d16_total` | 1.761 | 2.282 | 1.157 | 0.865 | 1.220 | 0.884 |
| `d03_arrays` wall | 2.887 | 1.982 | 1.710 | 1.531 | 1.702 | 1.581 |
| `d16_solvers` wall | 1.770 | 2.284 | 1.170 | 0.876 | 1.222 | 0.886 |

The first measurement (`runs\i2-first`, before the templates, the literal row and the emitter)
had fzero at 0.006-0.008 and the loop at 0.030.

**Where the rows stand.** `d03_funs` and `d16_integral_osc200` are under R2025b in every session.
`d16_fzero_2000` is 0.003-0.004 from pass 2 on in every session, 0.007 at pass 1 (tier-up, I13's),
which puts its three-pass median at R2025b's 0.004 headless (a tie, beaten under D3) and at 0.006
in the one app session (0.007, 0.006, 0.004). `d03_loop_2M` is 0.013 from pass 2 on (0.020 at pass
1, where it runs on the register machine until the move and the emitted method's first JIT) and
does not reach 0.010: 6.5 ns per pass is the loop's dependency chain - a multiply, an add, `mod`'s
divide (which the JIT makes an exact multiply by 0.5), floor, a multiply and a subtract - with no
dispatch left in it. R2025b's 5 ns is below that chain unless it fuses the multiply and add, which
would round differently and move D1's byte-identical CHK; the row is recorded as missed at the
chain's floor.

### Checks

- `CallbackBodyI2Tests` (36): every case on both roads, the same bytes and outcome, and the road
  it must take by the counters - fzero in a loop, from a guess with all four outputs and the
  displays, fminbnd and fminsearch over points, an answer leaving the reals, `FunValCheck`, the
  refusal lists for solvers and integrands, `arrayfun` in its shapes and everything it leaves to
  the walk, `integral`/`quadgk` with waypoints, infinite ranges and backwards limits, a template
  following a capture that changes class, and a literal row that stays fresh when a copy is
  written.
- `LoopNativeI2Tests` (9): the walk, the register machine and the emitted road (moved after one
  back edge) agree on the workspace's bits, the text and the outcome: the head2head loop, every
  scalar op and builtin, NaN on every comparison and fused branch, nested ranges, while loops with
  break and continue and a zero-trip loop, an answer leaving the reals (bail and deopt), walked
  statements leaving and re-entering, logical variables, the step limit at every budget from 3 to
  70, and a vector loop staying on the register machine.
- I2's and I1's classes, the compiled-loop classes (M98, 12b blocks, 12d vectors, V8 bounds), the
  precedence tests, the optimisation, quadrature and arrayfun tests and the MATLAB parity suite,
  with `JGRAPH_LOOP_NATIVE_AFTER=1` so every compiled loop with a second pass ran emitted: 598 of
  599 in each storage; the one is open item 253's stale test, which fails the same way before I2.
  The broad filter (10,769 tests) ran in the native-linear-algebra lanes of both storages before
  the emitter landed, with the same single failure.
- `run-stress.ps1` over the Debug build: 88 of 89, `stess_40` (open item 254, before I2).
- Every d03 and d16 CHK line (58) equal to the GUI round's JGraph pass 1 in all eight sessions; a
  hex dump of every callback road's answers equal with `JGRAPH_FN_COMPILE` on and off.
- The seven verify scripts green; the harvest unchanged (1343 rows from 217 ADRs).

## Consequences

- The plan is at rev 5 with I2 partly done: three of its four rows at or under R2025b at steady
  state, the loop row recorded at its chain's floor, pass-1 tier-up of fzero and the loop handed
  to I13.
- Open items 255 (a uniform `arrayfun` refuses a complex scalar answer; R2025b collects it) and
  256 (`integral` refuses a complex integrand whose midpoint is real; R2025b integrates it) were
  found writing the parity cases; both are the walk's, on both roads.
- The rig gained `measure_i2.ps1`, `compare_i2.py` and `probes\instrumented\split_i2_fzero.m`
  (outside the repository). A fresh-process probe of a row like fzero reads 3x the session's time
  on tier-0 code; `DOTNET_TC_CallCountingDelayMs=0` brings it near, so split probes are read under
  it and the session decides.

## Divergences

None. Every compiled road gives the walk's doubles and the walk's refusals; the emitted loop gives
the register machine's.

## Still open

- `d03_loop_2M` at 0.013 against 0.010 (the chain above).
- The pass-1 cost of fzero (0.007) and the emitted loop (0.020): I13.
- A loop with a vector op stays on the register machine; emitting `VLoad` and `VStore` is the next
  step if a row asks for it.
