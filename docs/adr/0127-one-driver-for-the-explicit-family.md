# ADR 0127 — One driver for the explicit family

## Status

Accepted (M125, 2026-09-04).

## Context

`ode45` had been here since M43 and was, until this milestone, the whole of the ODE library: one
pair, one driver, and an `odeset` that stored twenty-two fields and acted on four. MATLAB's
`funfun` folder holds twelve solvers, and the four explicit ones beside `ode45` — `ode23`,
`ode78`, `ode89` and `ode113` — share everything with it except the arithmetic of a step: the
handling of `tspan`, `Refine`, the events, the output function, the non-negativity constraint,
the mass matrix, the statistics, the solution structure and `deval`. The plan's answer was to
write that shared part once and let a tableau be a value.

The instruments M124 built are what made the milestone measurable rather than plausible. The
fixture `m125_ode_explicit.m` pins, for every one of the five solvers, the step count, the
failed-attempt count and the derivative-evaluation count on van der Pol, the rigid body, Lorenz,
a mass-matrix problem, a `NonNegative` decay and a `NormControl` run — exact, all of them — and
beside those the event times of the restricted three-body orbit and the bouncing ball, the
`odextend` joins, and `deval` inside the steps. It was recorded once from R2025b and runs in the
ordinary test suite: 196 lines, all agreeing.

This is also the milestone where the second machine took over. It has R2025b where the first
had R2024a Update 4; the three M124 recordings were re-recorded from it and the version file
moved with them, so that the suite has one reference and not two. Nine of their seventy-four
lines changed in the last digits — `butter`'s coefficients and `filter`'s output moved at 1e-16,
one quadrature answer at 1e-17 — and every one still agrees under its rule.

## Decision

### A tableau is a value, and the driver reads what it says

`RungeKuttaScheme` holds the nodes, the tableau, the solution and error weights, the dense-output
rows, the continuation stages the Verner pairs need for their interpolants, and — the part that
turned out to matter — five conventions the four reference files do not share:

- whether the last stage of a step is the first of the next (`ode23` and `ode45` reuse it; the
  Verner pairs take a fresh slope each step and pay for it);
- whether a stage is `y + Σ f·(h·a)` or `y + h·Σ a·f` (`ode23` scales its weights by the step
  first; the others sum first);
- whether the step length is purified to `(t + h) − t` before or after the solution is formed
  with it (`ode45` before; the other three after);
- what a retried step measures its error against (the Verner pairs use the state the step began
  from alone; the others the larger of that and the new state);
- how far a first refusal may shrink the step (`ode23` no lower than half; the others a tenth).

None of these changes the method. Every one changes the last bit of an error estimate, and a
step is accepted or refused on that bit. The fixture pins `nsteps` exact, which is why they are
written down rather than averaged away: a driver that took the reasonable choice everywhere
would be off by a step somewhere on every problem.

`ExplicitRungeKutta.Run` is MATLAB's loop over that record — the first-step heuristic, the
step control, the `MinStep`/`MaxStep` clamps, the growth rule, the failure rule — and
`AdamsPece.Run` is `ode113`, the variable-order predictor–corrector on modified divided
differences, ported with its arrays indexed from one because that is how the recurrence is
written and read. Its four-figure error constants are the reference's own: a constant good to
six figures takes different steps than one good to four, and the fixture would have said so.

### What every solver shares is written once

`OdeSetup` is `odearguments`, `odemass` and `odenonnegative` together: the span and its checks
(with MATLAB's identifiers, so a script that catches `MATLAB:odearguments:TspanNotMonotonic`
catches it here), the threshold each component is measured against, the step limits, and the
derivative with the mass matrix and the non-negativity constraint folded into it. A constant mass
matrix is factored once and solved against every call; a mass function is factored per call. Each
fold costs one more evaluation of the slope at the start, exactly as the reference counts it.

`OdeOutput` is the three output modes — the step's end, `Refine` points through it, or only the
times the caller named — and the `'init'`/step/`'done'` protocol of the output function, which
may stop the run. `OdeEvents` is `odezero`, the bracketing search over one step's interpolant,
with its terminal-and-direction bookkeeping and its rule that a terminal event found at the very
start does not stop anything. When a terminal event cuts a step, the stages are re-read off the
step's own polynomial at the shortened step's nodes before the step is stored, so that what the
solution structure carries describes the step that was taken.

The Verner pairs' interpolants need stages beyond the attempt's — four for `ode78`, five for
`ode89` — and those are evaluated the first time anything reads inside a step: the solution
structure, a refined output, a named time, an event. Each costs a derivative call that the count
reports, and the fixture's `nfevals` lines are exact only because the laziness is the reference's.

### The solution structure is MATLAB's, page for page

`sol.idata.f3d` is n-by-stages-by-**mesh**, not by steps: its first page belongs to the initial
point and is zero, because `deval` reaches for the page of the step that ends at a mesh point.
M123's structure had one page fewer, and its own test pinned that; the test moves to sixteen
pages with the recording that says so. `ode113` carries `klastvec`, `phi3d` and `psi2d`, trimmed
to the highest order the run reached, as `odefinalize` trims them.

`deval` dispatches on `sol.solver` to that solver's interpolant, rebuilding every step from the
structure's own fields, and refuses a time outside the interval with MATLAB's identifier where
M123's extrapolated. `odextend` runs the solver that made the solution from where it stopped,
with the options it stopped with unless told otherwise, and joins the two: a continuation from
the solution's own last state drops its duplicate first point, one from a new state keeps both so
the mesh shows the jump.

### The statement form draws

A solver called with no output at all draws its answer through `odeplot`, which is MATLAB's rule
and the reason `odeplot`, `odephas2`, `odephas3` and `odeprint` are registered beside the solvers.
They keep the points the driver hands them and draw once at `'done'` — the picture MATLAB's
animated lines end on, without the animation.

### Reference sources

Read for the documented behaviour and the constants that define it, and nothing copied:
`toolbox/matlab/funfun/ode23.m`, `ode45.m`, `ode78.m`, `ode89.m`, `ode113.m`, `odextend.m`,
`deval.m`, `odeplot.m`, `odeprint.m`, `odephas2.m`, `odephas3.m`, and under `private/`:
`ntrp23.m`, `ntrp45.m`, `ntrp78.m`, `ntrp89.m`, `ntrp113.m`, `odezero.m`, `odearguments.m`,
`odeevents.m`, `odemass.m`, `odemassexplicit.m`, `odenonnegative.m`, `odefinalize.m`,
`packageAsFuncHandle.m` — all from R2025b on this machine. The Verner tableaus are kept as the
decimal literals those files carry rather than as fractions, and were checked against them twice:
every literal in the C# is in the reference and every literal in the reference is in the C#, and
row by row each stage's weights sit on the stages the reference puts them on.

## Consequences

**The fixture.** 196 lines, 0 unexplained. Every `nsteps`, `nfailed` and `nfevals` for every
solver on every problem is exact against R2025b, `ode113`'s included; every event time agrees to
1e-8 or better; `deval` agrees to the interpolant's own accuracy.

**What the fixture taught, in the order it taught it.** The first recording disagreed on seven
lines. Three were the fixture's own (an argument order, a two-number value under a one-number
rule). The other four were the Verner pairs' trajectories: identical step counts, endpoints 1e-8
apart. Tracing both engines attempt by attempt found the cause at step 16 of van der Pol — an
error estimate of 3.7e-7, formed from weights of ±23 cancelling eight orders of magnitude, whose
last bits are rounding, and whose eighth root sets the next step. A one-ulp difference in a
`pow` became 2.7e-7 in the estimate, 3.4e-8 in the step, and 1e-8 at the end. That is not a
defect of either implementation; it is what a high-order pair does when the step is far more
accurate than it needs to be, and MATLAB's own arithmetic is not reproducible here to the bit.
The Verner endpoints are pinned at `rel=1e-6` with that written beside them, and the older pairs
and the Adams method at `rel=1e-9`, which they meet with room to spare.

**The `ode78` event.** MATLAB's `odezero` reported the same maximum-distance event twice at one
time, 3.09580822595, on the orbit; `ode78` here reported it once. The search re-finds a root it
has just passed when rounding leaves the value on the wrong side of zero, and which engine does so
on a given step is decided by the trajectory's last bit. The fixture pins the distinct event times
and indices, which are the events.

**Tests.** 7,195, up from 7,179, and 72 of 72 stress scripts: `stess_72.m` carries the recorded
step counts, the bounce, the `odextend` join, `NonNegative`'s extra evaluations, and the mass
matrix checked against the derivative divided by it. Four lanes green.

**Coverage.** `funfun` goes from 6 to 11 of 40 names (`ode23`, `ode78`, `ode89`, `ode113`,
`odextend`), 273 to 278 of 377 toolbox names. The form prober's generic samples had never fitted a
solver (`odefun` sampled as `sin`, `options` as a struct `odeset` did not make), which is why
`ode45`'s forms sat at nought accepted since the folder was first counted; solver-shaped samples
take the folder from 4 to 32 accepted forms and the toolbox total from 442 to 478.

**Head-to-head.** `d14_capability` accepts 290 of 299 forms, up from 281 of 291; the nine refused
belong to M126 and later. `d16_solvers` gains eight rows, and every `CHK` line agrees on both
engines. The timings are five-run medians, engines interleaved, on this machine against R2025b
(script spread median 1.09×, worst 1.56×):

| Row | JGraph | MATLAB | |
|---|---:|---:|---|
| `d16_ode113_lorenz200` | 0.048 s | 0.091 s | ahead |
| `d16_ode78_lorenz200` | 0.039 s | 0.063 s | ahead |
| `d16_ode89_lorenz200` | 0.065 s | 0.074 s | ahead, within the floor |
| `d16_ode113_orbit_tight` | 0.003 s | 0.026 s | ahead |
| `d16_ode78_orbit_tight` | 0.012 s | 0.016 s | ahead |
| `d16_ode89_orbit_tight` | 0.013 s | 0.017 s | ahead |
| `d16_ode23_ballode_200` (200 events and `odextend`s) | 0.022 s | 0.119 s | ahead, 5× |
| `d16_ode23_lorenz200` | 0.051 s | 0.038 s | behind, 1.34× |
| `d16_ode45_lorenz200` | 0.201 s | 0.122 s | behind, 1.65× (M124: 1.2×) |
| `d16_ode45_orbit_tight` | 0.169 s | 0.040 s | behind, 4.2× (M124: 5.2×) |

Every new solver is ahead of MATLAB on Lorenz, which was the gate. The two rows behind are the
two lowest-order pairs — the ones that take the most steps for a given accuracy — and the
derivative is a script function called through the interpreter on every stage. That is the cost
M124 named and this milestone did not touch it: it added no interpreter work per call, and the
candidate M124 named is unchanged, letting M98's register compiler take a scalar-only
anonymous-function body, which is a separate decision with its own ADR.

Two other `CHK` lines in the suite moved with the machine rather than with the milestone.
`d08_lorenz_steps` at t = 60 is 3,337 here and 3,309 in R2025b, where R2024a on the first
machine answered 3,337: the two MATLAB versions part on the same chaotic run that ADR 0126 said
the fixtures avoid, and the t = 20 count, which the fixture pins, is exact against both. The
residual-level lines in `d01`, `d02` and `d10` (1e-15 against 2e-15) are rounding, and the report
files them as information rather than as differences: 218 checks agree, 4 are information, 1 is
the marker above, 0 differ, 2 are the `deconv` skips of ADR 0125.

## Divergences

One is added.

- **The `ode` solver object (R2023b) is declined by name.** `F = ode` is refused here; the
  function forms are the whole of the API. Pinned by `m125_ode_explicit`'s `ode_object` line.

## Still open

None of these is a difference in what JGraph answers, so none belongs in the list above.

- **`deval` at an interface point answers the average silently** where MATLAB warns that the
  solution is not unique there. The value is the same; the warning is not raised.
- **`[t, y, te, ye, ie]` without an `Events` function answers empty event arrays** where MATLAB
  answers its statistics vector in the third output — a quirk of `odefinalize` rather than a
  documented form.
- **`MaxOrder`, `BDF`, `Jacobian`, `JPattern`, `JConstant`, `MvPattern`, `InitialSlope` and
  `Vectorized`** are stored and read back and nothing acts on them — as in MATLAB's explicit
  solvers, until M126.
- The interpreter-bound rows above.
