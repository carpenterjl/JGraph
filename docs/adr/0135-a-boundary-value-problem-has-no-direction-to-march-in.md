# ADR 0135 — A boundary value problem has no direction to march in

## Status

Accepted (M130, 2026-09-07). Built by an implementation agent in its own worktree and landed by
the integrator under the arrangement ADR 0131 records; landed fifth and last of the parallel wave.

## Context

M125 and M126 gave `funfun` every initial-value solver. Two families remained that MATLAB
documents beside them and that share their machinery without sharing their shape. A boundary value
problem — `bvp4c`, `bvp5c`, and `bvpinit`, `bvpset`, `bvpget`, `bvpxtend` around them — has
conditions at both ends and nothing to march from. A delay problem — `dde23`, `ddesd`, `ddensd`
with `ddeset`, `ddeget` — marches, but its right-hand side reads the solution at earlier times,
which the solver must already have written down.

The plan's strong claim was that mesh sizes would be exact for both collocation solvers on MATLAB's
seven documentation problems, and step counts exact for `dde23` on the five `ddex` problems. A mesh
count is decided by three rules — the residual estimate, the redistribution rule, and the Newton
convergence test — and a step count by where the solver chooses to land. All of them held, with one
exception whose cause was chased to a single instruction and is recorded below.

## Decision

### The answer is a count as much as a curve

What `bvp4c` and `bvp5c` have instead of a direction is a mesh, a collocation polynomial on every
interval of it, and one large algebraic system saying the polynomial satisfies the equation at the
collocation points and the boundary conditions at the ends. Newton solves the system; the residual
of the continuous solution says which intervals are too coarse; the mesh is redistributed and the
whole thing is done again. Every constant that decides the count is R2025b's: the five-point Lobatto
quadrature with weights 32/45 and 49/90 at nodes `(1 ∓ √(3/7))/2`, the factor of a hundred that
decides whether an interval gains one point or two, the exponent 7/2 in the predicted residual that
decides whether three quiet intervals become two, and the four Newton iterations and four
line-search probes with their 0.9 and 0.1 thresholds.

The two solvers control different quantities, which is why they disagree with each other and both
agree with MATLAB. `bvp4c` measures the residual `S'(x) − f(x, S(x))` of a C¹ cubic; `bvp5c`
measures the error of a quartic against the equation, sampled at the interval midpoint while the
mesh is adapting and at the two points where a fifth-order error peaks once it is not. On the same
problem at the same tolerance `bvp5c` answers roughly half the mesh — 12 points against 22 on
`twobvp`, 25 against 37 on `mat4bvp`. `bvp4c` condenses its interior stages away and keeps mesh
values; `bvp5c` carries three unknown vectors per interval and solves the larger system, which is
why its solution structure reports every third point of its working mesh and why its `deval` needs
the midpoints (`sol.idata.ymid`) the quartic passes through.

Unknown parameters are handled two ways, both MATLAB's: `bvp4c` as extra columns of the global
Jacobian and extra rows of the boundary conditions; `bvp5c` by augmenting the state with one
trivially-differentiated component per parameter and adding a continuity condition at every
interface, which keeps the augmented system square on a multipoint problem. The singular class
`y' = S·y/x + f(x, y)` is folded into the derivative before either solver sees it, as
`bvpsingular.m` does: `pinv(I − S)` at the origin, `+S·y/x` elsewhere, and `S·y(0) = 0` imposed on
the guess and on every Newton iterate through `I − pinv(S)·S`.

### The global Jacobian is a band with a border, so it gets a general sparse LU

Every interval couples its two end points, the boundary conditions reach both ends of the mesh, and
each unknown parameter fills a column. No ordering makes that banded, because a boundary condition
may mix `y(a)` and `y(b)` however it likes — which is why MATLAB itself assembles a `sparse` matrix
and calls `decomposition` on it. `CollocationLu` is therefore a left-looking sparse LU with partial
pivoting in the natural column order — Gilbert–Peierls, with an iterative depth-first reach so a
long mesh cannot overflow a stack — plus a Hager 1-norm condition estimate for the `rcond` behind
MATLAB's `SingJac` error and `IllCondJac` warning. Natural order suits this matrix: column *k*
reaches back only through the interval ending at its mesh point, so the elimination stays inside
the band and only the boundary rows stay live across the sweep. The factors are kept, because a
Newton step and every probe of its line search solve against the same matrix.

**This is the one deviation from the plan**, which asked for a block-tridiagonal elimination. The
border rules that out, and matching MATLAB's own choice of a general sparse factorization is also
what keeps the rounding close enough for the exact mesh counts to hold.

### A delay problem reads a polynomial the solver has already written

`dde23` is `ode23` — M125's Bogacki–Shampine (2, 3) pair — with the delayed arguments read off the
same Hermite cubic that pair carries for its dense output, and, when a lag is shorter than the
step, off the tentative solution at the end of the step being taken, iterated up to five times
until it stops moving. What a delay problem has that an initial value problem does not is a
discontinuity structure it can *predict*: history and solution generally disagree in the first
derivative at `t₀`, that disagreement propagates to `t₀ + τ` in the second and to `t₀ + 2τ` in the
third, smoothing as it goes. `dde23` works those points out before it starts and steps exactly onto
each of them, four levels deep — five when `Jumps` or `InitialY` has been declared, because then a
level of smoothing has been given away. `ddesd` cannot predict them, since a state-dependent delay
moves with the solution: it uses the classical four-stage fourth-order formula and controls the
residual of its natural interpolant, sampled at `1/2 ∓ √3/6` and scaled by 2.1342. `ddensd`
replaces every delayed derivative with a difference quotient over a `√eps` interval behind the
delayed argument and hands the retarded problem to `ddesd`, exactly as R2025b does.

One subtlety decides `dde23`'s step counts. The stage weights `h·B` and the interior nodes `t + h/2`,
`t + 3h/4` are formed from the step the control *asked for*, and only then is the step purified
onto the discontinuity it must land on. A step stretched onto a discontinuity therefore takes its
stages at the nodes of the step it meant to take. Getting this backwards changed nothing visible in
the solution, moved `ddex1` in the twelfth figure, and was the difference between matching R2025b's
step counts and not.

### `deval` reads five more kinds of solution

`bvp4c`, `dde23`, `ddesd` and `ddensd` solutions are read off the Hermite cubic and `bvp5c`'s off
the quartic; the dispatch in `JgsBuiltins.OdeSolution.cs` gained two fields and one branch that
returns before the `idata` requirement, because none of these structures carries `idata`. Nothing
else in that file moved, and M131's `pdeval` never touched it.

### What is not built, and why

The statement form of the delay solvers — `dde23(...)` with no output, which draws through
`odeplot` — opens a window and MATLAB's `dde23` returns only `sol`; `OutputFcn` and `OutputSel` are
honoured through `ddeset`, so a caller who wants the callback gets it. `options.OutputTY`, an
undocumented internal field, is not read. `FJacobian` as a numeric matrix on a multipoint `bvp5c`
problem takes the whole matrix rather than the region's block, which is R2025b's own behaviour
(`bvp5c`'s `odeJac_region` does not slice by region where `bvp4c`'s does) and is reproduced rather
than corrected.

### Reference sources

Read for the documented behaviour and the constants that define it, nothing copied: R2025b's
`toolbox/matlab/funfun/bvp4c.m`, `bvp5c.m`, `bvpinit.m`, `bvpset.m`, `bvpget.m`, `bvpxtend.m`,
`private/bvparguments.m`, `bvpfunctions.m`, `bvpsingular.m`, `ntrp3h.m`, `ntrp4h.m`, `dde23.m`,
`ddesd.m`, `ddensd.m`, `ddeset.m`, `ddeget.m`, `deval.m`, `private/odezero.m`, `odefinalize.m`,
`odenumjac.m`, the `+matlab/+ode/+internal/+dde/` package (`lagvals.m`, `Ndde.m`, `Ndelays.m`,
`Nypdel.m`), and the example files `twobvp.m`, `twoode.m`, `twobc.m`, `mat4bvp.m`, `emdenbvp.m`,
`fsbvp.m`, `rcbvp.m`, `shockbvp.m`, `threebvp.m`, `ddex1.m` (with `ddex1de.m`, `ddex1hist.m`,
`ddex1delays.m`), `ddex2.m` … `ddex5.m`. Reused rather than rewritten: M126's `numjac` for every
differenced Jacobian in both families, M125's `odezero` for the delay solvers' event location,
wrapped so the event function sees the delayed arguments at the point being tested.

Three readings of the sources that decided counts: `bvp4c`'s `colloc_RHS` *assigns* rather than
accumulates its evaluation count at the start of each region, so on a multipoint problem
`nODEevals` is the last region's, not the total; `bvp4c`'s analytic-Jacobian branch always
recomputes the midpoint Jacobian while its numerical branch averages the two ends whenever they
agree to a quarter of their size, and unifying the two moves the mesh; and `ddesd`'s "Hermite
extrapolation from the previous step" is `ntrp3h` evaluated at its own right endpoint, which is the
identity — implement it as an extrapolation and the step counts move.

## Consequences

**The fixture.** `m130_bvp_dde.m`: 240 lines, 0 unexplained against R2025b, two `div=` lines.
Every mesh count is **exact** for both `bvp4c` and `bvp5c` on `twobvp` (both branches), `mat4bvp`,
`fsbvp` with its `bvpxtend` continuation, `shockbvp`, `emdenbvp`, `threebvp` and `rcbvp`, and so is
every `nODEevals` and `nBCevals`. `ddex1`, `ddex3`, `ddex4` and `ddex5` match step for step,
failure for failure and evaluation for evaluation. One rule was loosened and the reason is
recorded: `ddex4_deval` from `rel=1e-6` to `abs=1e-8`, on a value of `−3.468e-4` sitting on a zero
of `cos`, four orders below the solution's size and reached through a `√eps` difference quotient;
the observed difference is 5.8e-10.

**Tests.** `MatlabBvpDdeM130Tests` 24; the assembly at 7,767 on the default lane; four lanes
green. `stess_78.m` is fourteen sections and passes on `jgraph.exe` and on R2025b alike, adding
three checks the fixture does not carry: every discontinuity `dde23` tracked is a mesh point of the
answer, the derivative `deval` reports at a mesh point equals `sol.yp` there, and `emdenbvp`,
`ddex3` and `ddex5` match their closed forms to the accuracy the default tolerance asked for. The 57
`d14` forms were run on both engines and none was refused; the six `d16` rows — the shock layer by
continuation with and without Jacobians, `twobvp` at 1e-8 on both solvers, the baroreflex problem,
and the D1 problem of Enright and Hayashi — wait for the arc's timing session.

**Coverage.** `funfun` reaches 40 of 40 documented names implemented or declined; the toolbox
total goes from 331 to 342 of 377.

## Divergences

- **`ddex2`, the baroreflex problem, takes 600 steps where R2025b takes 598, and its final heart
  rate differs in the third figure.** MATLAB: `nsteps = 597`, `sol.y(3, end) = 1.4377142366`;
  JGraph: `599` and `1.4380450974`. The two engines agree **bit for bit** on `x`, `y` and `yp` for
  the first nine mesh points and then part by four ulps in one step size. The right-hand side
  evaluates `(Pa(t − 4)/93)^7` and `(93/Pa(t))^7` at every stage, and R2025b's `pow` and .NET's
  `Math.Pow` round differently in the last bit for a substantial fraction of arguments — a
  10,000-point sweep of `x^7` over `[0.5, 1)` disagrees on hundreds of them. A one-ulp change in a
  stage slope is invisible in `y` but not in the cancelling combination the step size is computed
  from, and over a thousand time units with 128 rejected steps the two integrations separate. This
  is the mechanism ADRs 0127 and 0129 recorded for the Verner pairs and the stiff family, and it is
  not a delay-solver change. `ddex2`'s discontinuity list (ten entries, last at 616), `tfinal` and
  history are pinned exact and agree. Fixture lines `ddex2_nsteps` and `ddex2_heartrate`,
  `div=ADR0135`.

## Still open

- **`bvp4c`'s `dF/dp` is always differenced one parameter at a time**, even under `'Vectorized'`,
  where R2025b's `odenumjac` calls the derivative once with a matrix of perturbed parameters. This
  changes only `nODEevals`, only for a problem with both unknown parameters and a vectorized
  derivative, which none of the documentation problems is; no fixture line pins it because there is
  no reference problem that exercises it.
- **The condition estimate is Hager's, not `dgecon`'s**, and can differ from MATLAB's `rcond` by a
  small factor. It drives only the `IllCondJac` threshold; the `SingJac` error is a missing pivot,
  detected exactly. None of the documentation problems triggers either.
- **`Math.Pow` against MATLAB's `pow`** is now recorded three times (ADRs 0127, 0129, 0135) as the
  mechanism behind every step-count divergence in the solvers arc. A correctly-rounded `pow` for the
  integer exponents the problems actually use would close all of them, and belongs to a numerics
  milestone rather than any one solver's.
