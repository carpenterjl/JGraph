# ADR 0161 — A copy measured before it is borrowed

## Status

Accepted. Item 12, third and last stage, of the head2head_v3 gap-closure plan
(`docs/plans/gap-closure-07-12-plan.md`, rev 6, agreed with Codex): 12e.2, the borrowed read-only
argument. The first stage (12a, 12e.1) is ADR 0159; the second (12b–12d) is ADR 0160. This stage
lands no code: the design was measured at its ceiling first and declined.

## Context

Every call of an ODE right-hand side copies the state twice before the body sees it. The bridge
(`RunOdeSolver`'s `Derivative` in `JgsBuiltins.OdeFamily.cs`) clones the solver's `y` into a
fresh column, and the anonymous function binds each argument through
`Interpreter.CopyForBinding` (`AnonymousFunction.CallMultiple` in `JgsCallable.cs`), which copies
a packed array again. The second copy is what stops `y(2) = 0` inside a body from writing into
the caller's buffer through `JgsValue.SetPackedNumber`, which writes in place.

The plan's 12e.2 would remove both for parameters a static analysis proves read-only: a
conservative walk of the handle's body, once per handle, and a `Borrowed` wrapper over the
caller's buffer that every mutator copies before writing, as a second line of defence. It was
scheduled last in item 12, after 12d's numbers, with a kill switch `JGRAPH_BORROW_ARGS=0` and a
fixture of handles that must keep copying (a global that retains `y`, `q = y` then a write to
`q`, `end` in an index, a second handle, a user function taking `y`).

The rows it names are `d16_ode23_lorenz200` and `d16_ode45_orbit_tight`, whose probes stood at
0.054 s and 0.134 s warm against R2025b's 0.0175 s and 0.0123 s after ADR 0160
(`runs\12bcd-loops-b`). Their states hold three and four numbers, so each copy is a few
allocations of a few dozen bytes, set against an interpreted body of a dozen scalar operations
and a concatenation.

## Decision

**Measure the ceiling before building the design.** No borrowed argument can save more than the
two copies it removes, so both were removed outright on inputs that are safe without them. A
throwaway switch, `JGRAPH_BORROW_PROBE=1`, made the bridge hand the solver's own array to the
column and made the anonymous function bind its arguments as given. It was built from the
2a3a66b tree through `manifest.ps1` into `runs\12e2-copy-probe\bin` (labelled dirty), and
reverted from the source as soon as the binary existed; it was never committed. Both probes'
right-hand sides only read `y`, so the switched binary computes the same trajectories: the call
counts below are identical with the switch off and on.

The two probes ran on that one binary in six paired rounds, one fresh process per probe, the
switch order alternating each round (`runs\12e2-copy-probe\ab`, JGraph only):

| probe, warm | copies (off) | no copies (on) | off / on, median | per round |
| --- | --- | --- | --- | --- |
| `probe_d16_ode23_lorenz200` | 0.0545–0.2057 s, median 0.0576 | 0.0542–0.0580 s, median 0.0566 | 1.018× | 1.00–1.02× (two off samples, 0.088 and 0.206 s, were machine stalls) |
| `probe_d16_ode45_orbit_tight` | 0.1302–0.1409 s, median 0.1336 | 0.1267–0.1321 s, median 0.1298 | 1.029× | 1.00–1.07× |

The runs make 26,800 derivative calls over 8,232 steps (Lorenz, `ode23` to t = 200) and 14,353
over 2,388 steps (the orbit, `ode45` at RelTol 1e-9), counted from the solution structure's
`stats` on the same binary.

**The borrowed argument is declined.** Its whole reward on the rows it was scheduled for is 2–3%,
and with no copies at all the rows still stand at 3.2× and 10.6× R2025b. Its cost is not local:

- a read-only-parameter analysis per handle, whose whitelist of builtins and syntactic forms must
  track every road by which a value can escape or be written;
- a `Borrowed` mark that every in-place writer must honour, which today means
  `SetPackedNumber`, `TryGrowInPlace`, the compiled loop's `VStore` (ADR 0160) and the ownership
  assumed by M109's elision of `CopyForBinding` for fresh operator answers, and would mean every
  in-place road written after them;
- and, if any of them forgets, a write into the solver's own state in the middle of a step,
  which no test of the answer's shape would notice.

No code lands. `JGRAPH_BORROW_ARGS` is not introduced, the bridge keeps its clone, and
`CopyForBinding` keeps copying every packed argument.

**12d does not reach the right-hand side either.** The plan let a compiled RHS supersede 12e.2 if
12d's vector class made one reachable. It does not: the vector class compiles `for` and `while`
loops (ADR 0160), and a right-hand side is an anonymous function body with no loop in it, which
is walked. A compiled anonymous body stays out of the plan's scope, as the plan states.

## Consequences

- Item 12 is complete, and with it the plan: items 07–12 are ADRs 0153–0161.
- The two d16 explicit rows keep their ADR 0160 numbers. What they spend is the walk of the body
  per call and the solver's own work per step; this stage did not separate those two, and no row
  of the plan schedules either.
- The ceiling method is recorded for the next design of this kind. When a proposed runtime
  feature only removes a known cost, remove that cost outright on an input where it is safe, on
  one binary behind a switch, and measure before designing the guard. ADR 0157 did the same for
  the scratch pools, with traced probes instead of a switch.
- `CopyForBinding`'s rule is unchanged: an anonymous function's parameter is always its own
  value, so a body may write to it freely.

## Divergences

None. No answer moves: the bridge and the binding are the code of 2a3a66b.
