# ADR 0129 — The Jacobian is the cost of a stiff step

## Status

Accepted (M126, 2026-09-06).

## Context

M125 put five explicit solvers on one driver. None of them can be pointed at a stiff problem. Van
der Pol at μ = 1000 is the standard demonstration: `ode45` needs hundreds of thousands of steps to
cross three thousand time units because its step is limited by stability rather than by accuracy,
while `ode15s` crosses the same interval in 591. The difference is not a tuning constant. An
explicit method's step is bounded by the fastest mode in the problem whether or not that mode is
still doing anything; an implicit method's is bounded by the accuracy the answer needs.

MATLAB's answer to that is six names, and they are the whole of this milestone: `ode15s`,
`ode23s`, `ode23t` and `ode23tb` for `M(t, y)·y' = f(t, y)`, and `ode15i` with `decic` for the fully
implicit `f(t, y, y') = 0`. With them come the nine `odeset` fields the explicit family stored and
left alone — `Jacobian`, `JPattern`, `JConstant`, `Vectorized`, `BDF`, `MaxOrder`, `MassSingular`,
`InitialSlope`, `MStateDependence`/`MvPattern` — and three statistics the explicit family has no use
for: how many Jacobians were formed, how many iteration matrices were factored, how many linear
systems were solved.

Those three are the point of the title. An explicit solver's cost is its function evaluations, and
`nfevals` describes it completely. An implicit solver's is not: on the Brusselator with four hundred
equations a single numerically differenced Jacobian costs four hundred evaluations of the
right-hand side, and the whole run costs two of them. What the solver is actually managing — when to
re-linearize, when to refactor, when to admit the step is too long — is invisible in `nfevals` and
visible in `npds`, `ndecomps` and `nsolves`. The fixture pins all six.

## Decision

### The mass matrix stays a matrix

M125's `OdeSetup` folds a mass matrix into the derivative: `M·y' = f` becomes `y' = M⁻¹·f`, factored
once and solved against every call. That is right for an explicit solver and wrong for every solver
here, which needs `M` on the left of its own iteration matrix `M − h·γ·J` and could not invert it
anyway when it is singular. Rather than give `OdeSetup` a mode, each stiff solver hands it a copy of
the options with `Mass` removed, and keeps the matrix itself. The same copy drops `NonNegative` when
there is a mass matrix, with MATLAB's warning, because the constraint is written for `y' = f` and
there is no `f` here to clip.

### The Jacobian has four routes and one of them is the milestone

`Jacobian` may be a matrix, a function, or absent; `JConstant` says the first of those is true of
the other two. `OdeJacobianSource` reads whichever the options describe and answers how many
derivative evaluations it cost, which is nought for the first two.

The fourth route is `OdeNumericalJacobian`, which is MATLAB's `numjac`, and it is the piece of this
milestone most worth having got exactly right. A forward difference is trivial; choosing the
increment is not. The increment is not `√eps·y` but `(y + fac·yscale) − y` — formed and then
subtracted, so that it is exactly representable and the quotient divides by the step actually taken
— where `yscale` falls back to the component's own absolute tolerance when the component is smaller
than it, and `fac` is Salane's adaptive storage carried from call to call. A column whose difference
came out at the level of rounding is taken again with a larger increment and kept only if the second
reading says more; whether it did adjusts `fac` for next time. Two implementations that difference
the same function with different increments produce Jacobians that agree to six figures and step
counts that do not agree at all, which is why `OdeNumericalJacobianTests` pins the increments
themselves across a sequence of six calls rather than the Jacobian they produce.

`JPattern` is what makes a large problem affordable: columns that share no row of the pattern can be
perturbed together, so one evaluation differences them all. `ColumnGroups` is `colgroup`'s first-fit
packing, and on the Brusselator it finds the same three groups MATLAB does — 84 steps, 177
evaluations and 2 Jacobians on both engines at N = 20, and 83, 178 and 2 at N = 200.

### Four solvers, four loops, one set of shared parts

The explicit family collapsed into one loop because its members differ only in a tableau. These do
not. `ode15s` runs numerical differentiation formulas of orders one to five on backward differences
at quasi-constant step size, and chooses its order each step by asking the differences themselves
what the error would be one order either side. `ode23s` is a Rosenbrock pair with no iteration at
all — three solves against one factorization — and therefore needs a correct Jacobian every single
step, which is why its `npds` equals its `nsteps`. `ode23t` is the trapezoidal rule, which damps
nothing, started with one backward Euler step and reverting to backward Euler when its iteration
fails three times in a step. `ode23tb` is TR-BDF2, whose trapezoidal stage lands at `t + (2 − √2)h`
precisely so that its iteration matrix is the second stage's too.

What they share is written once: `OdeSetup`, `OdeOutput` and `OdeEvents` from M125 unchanged, the
Jacobian source, the mass-matrix arithmetic, the row scaling a differential-algebraic iteration
matrix needs, and the five free interpolants. "Free" is the point of that last: none of `ntrp15s`,
`ntrp23s`, `ntrp23t`, `ntrp23tb` or `ntrp15i` costs a derivative evaluation, so `Refine`, a named
output grid, an event search and `deval` are all arithmetic on what the step has already paid for.

### A singular mass matrix is a different problem, and it is solved before the first step

`MassSingular` defaults to `'maybe'`, which means the solver decides, and the decision is
`eps·nnz(M)·cond(M) > 1`. When it is a differential-algebraic equation the caller's `y0` almost
never satisfies the algebraic part of it, and no step can be taken from a state that does not.
`DaeInitialConditions.Type12` is `daeic12`: the singular value decomposition of the mass matrix
separates the directions in which `y'` is determined from the directions in which `y` itself must
satisfy `f = 0`, and a damped Newton iteration with a weak line search runs in the second set alone.
`Type3` is `daeic3`, for a state-dependent mass matrix, where no one decomposition serves and a short
implicit Euler step stands in for it.

Two consequences are visible from a script. The first column of the answer is the state the solver
worked out and not the guess it was handed — `hb1dae` starts from `y0 = [1; 0; 1e-3]`, which
violates its own conservation law by a thousandth, and `sol.y(:,1)` is on the constraint. And the
problem is refused outright rather than solved badly when the algebraic block of the Jacobian is
itself singular, because that is an equation of index greater than one and moving the guess cannot
help.

### The fully implicit form carries its formulas in Lagrange rather than difference form

`ode15i` solves `f(t, y, y') = 0`, where the state and the slope enter however the problem writes
them, so its iteration matrix is `df/dy + (γ/h)·df/dy'` and both partial derivatives have to exist.
Each of `Jacobian`, `JPattern` and `Vectorized` is therefore a two-element cell — one half per
derivative — and either half may be given and the other differenced.

Its mesh is not quasi-constant, so the backward differentiation formulas cannot be carried as
rescaled differences; they are recomputed each step from the mesh actually there, by Fornberg's
triangular recurrence. `decic` is what makes a starting point consistent: the linearized residual
`0 = f + (df/dy)·Δy + (df/dy')·Δy'` has 2n unknowns and n equations, so it is solved with as many
components of the change as possible set to zero, over the components the caller did not pin, inside
a trust region that refuses to move the guess by more than a factor of two in norm.

### The solution structure and everything that reads it

`sol.idata` is MATLAB's, field for field and page for page: `kvec` and `dif3d` for `ode15s`, `k1` and
`k2` for `ode23s`, `z` and `znew` for `ode23t`, `t2` and `y2` for `ode23tb`, `kvec` alone for
`ode15i` — which needs no interpolation data of its own, because its polynomial is through mesh
points the structure already carries. `ode15s`'s differences are trimmed to the highest order the run
reached, as `odefinalize` trims them, which is why a first-order run carries three columns and a
fifth-order one carries seven. The two solvers that cannot honour a non-negativity constraint do not
record one. `sol.stats` gains `npds`, `ndecomps` and `nsolves` for these solvers and keeps three
fields for the explicit family, and `ode15i` alone carries `extdata.ypfinal`, because a continuation
of a fully implicit solution has to start from a consistent pair.

`deval` dispatches to each solver's own interpolant, rebuilt from the structure's fields alone.
`odextend` continues any of the nine solvers, and continues `ode15i` from the slope the run it
extends ended at unless handed an `[y0 yp0]` pair of its own.

### Reference sources

Read for the documented behaviour and the constants that define it, and nothing copied:
`toolbox/matlab/funfun/ode15s.m`, `ode23s.m`, `ode23t.m`, `ode23tb.m`, `ode15i.m`, `decic.m`,
`odextend.m`, `deval.m`, `numjac.m`, and under `private/`: `ntrp15s.m`, `ntrp23s.m`, `ntrp23t.m`,
`ntrp23tb.m`, `ntrp15i.m`, `odenumjac.m`, `odejacobian.m`, `colgroup.m`, `daeic12.m`, `daeic3.m`,
`odemass.m`, `odemxv.m`, `odefactorize.m`, `odefinalize.m`, `odearguments.m`, `ode15ipdinit.m`,
`ode15ipdupdate.m` — all from R2025b on this machine. The NDF coefficients, the Rosenbrock and
TR-BDF2 constants, Salane's four `eps` powers and the difference-rescaling matrix are the reference's
own literals, checked against them twice.

## Consequences

**The fixture.** `m126_ode_stiff.m`: 235 lines, 0 unexplained. Every `nsteps`, `nfailed`, `nfevals`,
`npds`, `ndecomps` and `nsolves` is exact against R2025b on van der Pol at μ = 1000, on Robertson
with an analytic Jacobian, on the Brusselator with and without a sparsity pattern, on `hb1dae` and on
`amp1dae` — including the 449 steps, 114 failures, 1,573 evaluations, 62 Jacobians and 1,200 solves
of the transistor amplifier, whose mass matrix is singular and not diagonal.

**The two lines pinned within two per cent, and why.** `ode23s` on Robertson and `ode15i` on the
implicit Robertson are the two runs whose Jacobian is differenced afresh at nearly every step. Both
track R2025b exactly for hundreds of steps — `ode15i`'s mesh is identical for its first 238 — and
then part in the last bits. Tracing it found the same mechanism ADR 0127 recorded for the Verner
pairs: the step is steered through `(err/rtol)^(1/3)`, and `Math.Pow` and MATLAB's `power` do not
agree to the last unit in the last place. One ulp there becomes one step in four hundred, and then a
few more. Both problems agree *exactly* on every count when the Jacobian is given in closed form,
which is what says the difference is arithmetic and not algorithm; the fixture says so beside the
lines.

**What the fixture taught.** The first recording disagreed on six lines. Two were tolerances the
fixture had set too tight for what the run was asked for. One was the interpolation data's shape,
which depends on a step count that is allowed to differ. One was a `NormControl` run that landed on
the same rounding boundary and was moved to a problem that does not. The other two were the real
one: `sol.y(:,1)` was the caller's guess rather than the consistent state the DAE search produced —
a bug visible only in the solution-structure form, because the two-array form reports the corrected
state through the ordinary output path. `OdeResult` now carries the state the first step began from
and the structure reports it.

**Tests.** 7,669, up from 7,649, four lanes green. `stess_73.m` carries the recorded counts, the
sparsity pattern's saving, the DAE start, `decic` and `ode15i`, the per-solver interpolants and the
refusals, and passes on jgraph.exe and on R2025b alike.

**A debt this milestone inherited and did not create.** Twelve stress scripts fail — 23, 26, 27,
29, 36, 39, 42, 45, 49, 50, 51 and 54 — and every one of them is a graphics, layout or display
section with nothing to do with a solver. They fail identically on commit `0acff3d`, which is where
this milestone started: they were introduced by the twenty-two commits between M125 and here, none of
which re-ran the stress suite. The check was done by building that commit in a detached worktree and
running the twelve against it, section for section. M125 left the stress suite at 72 of 72 and this
milestone leaves it at 61 of 73, which is not a number to leave alone; it is the same shape of debt
ADR 0126 recorded when M125 inherited eight lane failures, and it should be cleared the same way —
in its own commit, before the next milestone's gate can be read as green.

**Coverage.** `funfun` goes from 11 to 17 of 40 names and from 32 to 55 of its documented forms;
the toolbox total goes from 278 to 284 names and from 478 to 508 forms. Twenty-three of those thirty
new forms are the six solvers'; the other seven are `strfun` and `datafun` forms that the
twenty-two commits between M125 and this one made work without regenerating the document. The
callable-name count is 1,054, and eleven of the fifteen names it has gained since M123 are
M125's and M126's.

**Head-to-head.** `d14_capability` accepts 300 of 308 forms, up from 290 of 299: the nine forms M126
adds are all accepted. Of the eight still refused, seven belong to M127 and later — `integral2`,
`pcg`, `griddata`, a two-output `butter`, `hann`, `pwelch`, `findpeaks` — and the eighth,
`filtfilt` over a two-row `butter` answer, is ADR 0126's recorded divergence, which R2025b refuses
too. `d16_solvers` gains ten rows and every `CHK` line on them agrees on both engines, including
the step, failure, evaluation, Jacobian, factorization and solve counts of the four-hundred-equation
Brusselator.

The timings are five-run medians, engines interleaved, on this machine against R2025b (the whole
suite's script spread over those five runs: median 1.07×, worst 1.34×). The van der Pol and
Robertson rows are twenty solves each, because one is under the noise floor.

| Row | JGraph | MATLAB | |
|---|---:|---:|---|
| `d16_ode15i_robertsonx20` | 0.174 s | 0.468 s | ahead, 2.7× |
| `d16_ode23tb_vdp1000x20` | 0.107 s | 0.188 s | ahead, 1.8× |
| `d16_ode23t_vdp1000x20` | 0.099 s | 0.157 s | ahead, 1.6× |
| `d16_ode15s_vdp1000x20` | 0.161 s | 0.246 s | ahead, 1.5× |
| `d16_ode23s_vdp1000x20` | 0.194 s | 0.248 s | ahead, 1.3× |
| `d16_ode15s_brussode200_pattern` | 0.744 s | 0.136 s | behind, 5.5× |
| `d16_ode15s_brussode200_full` | 1.172 s | 0.083 s | behind, 14.1× |

Every stiff solver is ahead of MATLAB on the problem the family exists for, which was the gate. The
two rows behind are the four-hundred-equation Brusselator, and neither is the solver's doing — both
engines take 83 steps, 6 failed attempts, 2 Jacobians, 22 factorizations and 167 solves, and the
same 970 or 178 evaluations. What differs is what those cost. The pattern saves 792 evaluations and
0.43 s of the JGraph run, which puts one evaluation of a four-hundred-equation right-hand side
written as a loop at 0.54 ms through the interpreter; MATLAB's whole run, evaluations and algebra
together, is 0.083 s. And a bare loop of twenty-two solves of a 400-by-400 costs 0.35 s here against
0.066 s there, which is the rest of the gap. Both halves are the two costs M124 named and this
milestone did not touch: the right-hand side through the interpreter, and a dense factorization
against MKL's. Sparse storage for the iteration matrix — which is what MATLAB switches to when
`JPattern` is given, and which at this size makes its own pattern row *slower* than its dense one —
is M128's decision, not this one's.

One thing this milestone did fix on the way: the Newton iteration multiplied its correction by a
materialized identity matrix on every pass when there was no mass matrix at all — a hundred and
sixty thousand multiplications per iteration on this problem for a result equal to its input.
`MassTimes` reads the mass type and skips it, which is exact by construction and left the fixture's
235 lines byte-identical.

## Divergences

None. M126 adds no difference from MATLAB that a script can observe.

## Still open

None of these is a difference in what JGraph answers, so none belongs in the list above.

- **`JPattern` is grouped by first fit alone.** MATLAB's `colgroup` also tries a reverse-COLAMD
  ordering and keeps whichever packing needs fewer groups. Every pattern tried here — banded,
  tridiagonal, the Brusselator's — gives first fit the same count, so nothing observable differs; a
  pattern where the second ordering wins would cost more evaluations here than there.
- **The test for a singular mass matrix is deterministic.** MATLAB's `condest` is a block one-norm
  estimator started from a random matrix; the estimate here comes from the factorization the solver
  is about to form anyway. What the test asks is whether the matrix is singular to working
  precision, which is a question about orders of magnitude, and the two answers agree on every
  problem tried.
- **A sparse `JPattern` still gets a dense factorization.** The pattern cuts the Jacobian's cost and
  not the iteration matrix's, which is the plan's own decision and the reason the Brusselator row
  above is where it is. Sparse linear algebra is M128.
- **`deval` at an interface point answers the average silently**, and the other three notes ADR 0127
  left open, unchanged.
