# ADR 0173 — Reused arithmetic temporaries

## Status

Accepted. Stage Z2 of the value-ownership plan (`docs/plans/zfit-copies-and-temporaries-plan.md`),
the last stage, after V11's dialect carrying (ADR 0172). One commit, this ADR's, with one fixture
recorded from R2025b before the interpreter was touched and the kernel alias contract landed first.

## Context

Warm `zfit` — 231 Zernike terms over a 700-by-700 surface, run again with the Zernike cache warm —
took 1.30–1.38 s on 90f9f87 against R2025b's 0.20–0.22 s (the plan's Origin). V1's shared reads took
the two copies per cache hit out of it (1.74 s → 1.00 s on this session's rig, quiet regime); what was
left was the arithmetic: `zz = zz - Z * k` allocated a 3.9 MB temporary for `Z * k` and a second
3.9 MB answer for the subtraction, every one of 693 steps, on the large-object heap, zeroed by the
runtime and dropped at once. `PackedOps.TryArithmetic` allocated a destination for every packed
operator; the kernels could not be handed an operand as the destination, because the scalar-left
`Subtract` and `Divide` chunks filled the destination with the scalar and then read the operand;
and the ownership model that says when a variable's storage may be written over (V1–V10: a holder
count on every payload, an exposed mark for what the count cannot see, a share for every scope) was
built but never asked for that.

Z0's measurement was never run; V1 measured its own count representation instead. Z2 therefore
carries its own rig (scratch `z2\`): the plan's `zfit_parts`, `probe_z_arith` (nine shapes at 490K
and 4.9M elements) and `probe_z_timed_test` (test1 without its figures), each in a fresh process,
interleaved across the binaries, two rounds. The machine turned out to have two regimes: the first
round of every measurement ran managed-heap workloads about twice as slowly as the second while
native-buffer variants were unchanged, and another session's test host ran during the last round
(`testhost.exe`, 33 minutes of CPU, left alone as the house rules say). The numbers below are the
quiet round's, and the noisy rounds are recorded in the plan's "As built (Z2)".

## Decision

**The kernel alias contract.** Every elementwise binary kernel may be handed one of its operands as
the destination and answers the fresh destination's bits: the scalar-left `Subtract` and `Divide`
compute `scalar op y[i]` in one pass (the same IEEE operation the two-pass form rounded once), and
`PackedMathAliasTests` holds all six operations, three arrangements, every aliasing, one grain and
many, the Rounding forms, the fused sweep and the threaded aliased sweeps to it.

**Z2a — an operator writes into a fresh temporary operand.** `EvaluateOperand` says whether an
operand's value is storage this evaluation alone holds: a nested operator's owned answer (M109's
`OwnsFreshResult`, or a Z2a reuse one level down). `TryArithmetic` writes the answer into such an
operand when the answer is a plain double of the operands' length, from 64K elements up
(`JgsReuse.MinElements`), and the temporary has one holder, no exposed mark and no growth slack.
Ownership transfers: a reused answer shares its storage with the operand by construction, so its
`owned` is decided from the other operand alone. A share taken by M5's hold is a second holder and
is never written into. Both dialects, in the walk (`EvaluateBinary`, the `+` chain) and in the
compiled loop's vector ops (`HotLoopVectors.Fresh`).

**Z2b — `v = v op E` in place.** A plain MATLAB-dialect assignment whose right-hand side is `v op E`
or `E op v` (`+ - .* ./`; `* /` when the other operand turns out a scalar) runs its operands as the
operator would — the left held while the right may run script — and then writes the answer over
`v`'s own buffer when: `v` is a plain packed double with no class, tag or slack; the other operand
is a plain double scalar or a plain double array of `v`'s shape not sharing `v`'s storage; the length
is between 64K and 16M (`InPlaceNoPollElements`: the in-place sweep takes no cancellation poll, so
above it the allocating road, which polls and leaves `v` untouched when cancelled); no debug hook
is attached; the wrapper is not exposed; the workspace is one no JGS code can hold entries of
uncounted (`IsMatlabWorkspace`: every workspace of a MATLAB session, and a MATLAB function's own
frame whoever called it — a JGS binding keeps the wrapper and takes no share, and `run` from JGS
writes a `.m` script's variables into the JGS caller's workspace); the wrapper is still the binding
the assignment would replace (`JgsEnvironment.WouldReplace`, the walk `TryAssign` takes); and,
with the road's own hold released, the payload has one holder. Every refusal takes the allocating
road with the operands already evaluated and binds as before. The compiled loop's vector slots take
the same road through `LoopOp.VUpdate`, where the slot register is the binding.

**Z2d — the fused `v ∓ X * s`.** `PackedMath.ScaleAccumulate` computes `a ∓ b·s` in one sweep,
the product rounded and then the sum, per cache-resident tile, no fused multiply-add — the two
operators' bits. `v = v ∓ X * Y` (or `.*`) takes it under Z2b's conditions when one product operand
is a plain double scalar and the other a plain double array of `v`'s shape; the product's operands
are evaluated as the product would evaluate them, and a matrix product, a classed scalar or a
shared `v` falls back to the operator's answer and the update road.

**Z2e — uninitialized destinations.** An arithmetic destination the kernel writes in full is
allocated without zeroing (`BufferAllocator.AllocateForOverwrite`: `GC.AllocateUninitializedArray`,
`NativeMemory.Alloc`), behind `JgsReuse.UninitializedDestinations` (`JGRAPH_REUSE_UNINIT`, on).

**Z2c — threads for the reuse roads only.** Lowering `ParallelKernels.MemoryBoundThreshold` (now a
settable property, `JGRAPH_MEMORY_THRESHOLD`, default still 2M) to 256K for every kernel made warm
`zfit` slower (0.59 s against 0.36 s: `sum`, `dot` and the small copies lost more than the sweeps
gained), while the in-place sweeps at 490K gained (a single thread moves 14 GB/s over three resident
arrays where four move 26). The in-place and fused sweeps are threaded from
`JgsReuse.InPlaceThreadElements` (`JGRAPH_REUSE_THREADS`, 256K); everything else keeps 2M.

**Z2f — the managed/native boundary: measured and declined.** With `JGRAPH_BUFFER_MANAGED_MAX=65536`
warm `zfit` was 0.35 s in both regimes (native buffers are insensitive to the page-zeroing state that
halves managed LOH throughput), but the d-suite (20 probes, three interleaved rounds) ran slower on
15 of 20 — the ODE rows +26% to +61%, `diff` and `discretize` over 10M +18% to +22%, the windows
and sorts +7% to +10% — and faster only on `fft_batch` (−23%), the Bessel pair (−5%) and `lu` (−2%):
native buffers register GC pressure, and a workload that grows and drops many mid-size arrays pays a
full collection for every few (gen2 43 → 356 on `zfit_parts`). The boundary stays at 1M.

**A gap closed on the way (M6).** A JGS `let b = a` bound through `Declare` and never took the
exposed mark `CopyForBinding`'s JGS road sets, so a payload at `Holders == 1` could still have a
JGS alias and Z2b's exposed gate would have written through it (the JGS alias test found it; c15's
fixture arrays were below the 64K floor). Every JGS binding marks now — `let` and the JGS variable
road of assignment, an owned answer's included — and the workspace gate above stands whatever the
marks say.

**Kill switch.** `JGRAPH_REUSE=0` restores the allocating roads and nothing else, so a difference
between the two roads is a defect of the reuse roads by definition; `ArithmeticReuseM173Tests`
compares them bit for bit over nine shapes.

## Measured

Quiet regime (round 2 of `measure_round.ps1`, the median of three timed repeats, seconds; the two
baselines the plan asks for — 90f9f87 with the rig, and 9e77c90, every correctness stage and no
Z2 — against the Z2 binary; R2025b from the plan's Scope table):

| part | 90f9f87 | post-V11 (9e77c90) | Z2 | R2025b |
|---|---|---|---|---|
| `zfit_warm` | 1.739 | 1.001 | 0.359 | 0.20–0.22 |
| `sub_scaled_462` (`zz = zz - Z * k`) | 0.861 | 0.757 | 0.103 | 0.022–0.024 |
| `scale_only_462` (`W = Z * k`) | 0.322 | 0.281 | 0.297 | 0.022–0.024 |
| `sum_dot_231` | 0.154 | 0.091 | 0.087 | 0.137–0.146 |
| `zernike_warm_694` | 1.330 | 0.060 | 0.060 | 0.001–0.002 |
| `timed_test` warm (test1, no figures) | 1.911 | 1.460 | 1.180 | — |
| `zz=zz-Z*s` 490K × 462 | 1.134 | 1.082 | 0.107 | — |
| `zz=zz-W` 490K × 462 | 0.601 | 0.568 | 0.160 | — |
| `zz=s-zz` 490K × 462 | 0.719 | 0.723 | 0.139 | — |
| `W=(Z-Y)*s+Y` 490K × 462 | 1.679 | 1.793 | 0.841 | — |
| allocated, `zfit_parts` (GB) | 94.6 | 56.4 | 20.0 | — |
| gen2 collections, `zfit_parts` | 140 | 102 | 44 | — |

The plan's decision lines (10% of `zfit_warm` or 30% of `sub_scaled_462`) are passed by Z2a+Z2b+Z2d
together (−64% and −86% against post-V11 in the quiet regime; −46% and −94% in the slow one, where
post-V11 measures 2.1 s). Z2c and Z2e are inside the noise on `zfit_warm` and were kept on the
isolated shapes (the in-place sweeps −25% at 490K with threads; `scale_only` 4–15% faster without
zeroing in every round of a three-round re-measure). `JGRAPH_REUSE=0` on the Z2 binary measures as
post-V11. Every checkpoint (`k_sum`, `r_sum`, `zz_sum`, `hs_std`, the nine shape sums) prints the
same digits on every binary and setting.

Where a warm `zfit` still spends its time (`zfit_prof.m`, accumulators around each statement kind,
slow regime, post-V11 in brackets): the 231 cache reads 0.11 s [0.09]; the 231 `sum(dot(zz, Zi))`
0.20 s [0.19]; the 231 updates with the Zernike in a variable 0.12–0.19 s [0.62–0.68]; the 231
residual updates with the call inside the product 0.34–0.39 s [0.84–0.86], of which the fused sweep
is a third and the call, its hold and the path resolution of `zernike` at each statement the rest.

## What moves

- `temporary_reuse` (new, recorded from R2025b, 62 lines): every in-place operator both ways, the
  fused shapes on rows and 700-by-100 matrices, Z2a's temporaries, the alias, capture, cell and
  field guards, the held global (`gv + tr_bump()`, `plus`, a bracket, the self-update both ways),
  `ans` while a cell holds the payload, the 462-step loops, and NaN/±Inf/−0/subnormal through the
  sweeps — each line a digest of the bits (`helpers/tr_bits.m`). Its first recording disagreed on
  31 lines because `sin(1:70000)` sits above `ApproximateThreshold` (ADR 0093); the operands are
  exact integer arithmetic with one rounding each. Agrees in both lanes.
- The ratchet holds no Z2 line; the harvest is unchanged (this ADR records no divergence).

## Consequences

- `zz = zz - Z * k` over a 3.9 MB matrix is one sweep over `zz` and `Z` and no allocation; a
  nested operator chain allocates one buffer per leaf that has no fresh operand; `v = v op E`
  allocates nothing when nothing else can see `v`. Nothing a script prints changes, and the two
  roads answer the same bits.
- The reuse roads run only from 64K elements, so every printed array, parity fixture and
  hand-checked value below it takes the roads it always took.
- Not taken: a JGS session's base and function workspaces (an entry there may be held uncounted);
  arrays above 16M elements (the poll); classed, logical, tagged or strided variables; a `v` any
  scope, capture, slot or field still holds; the compiled vector road's fused update (one temporary
  per step there); a buffer pool for dropped temporaries and the managed/native boundary (declined
  above). What remains between 0.36 s and R2025b's 0.21 s is the 231 `sum(dot(zz, Z))` reductions,
  the cache reads and the per-statement walk.
- Tests: `PackedMathAliasTests`, `ArithmeticReuseM173Tests`.

## Divergences

None recorded by this stage.
