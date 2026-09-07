# ADR 0132 — One call per tile is the whole difference

## Status

Accepted (M127, 2026-09-07). Built by an implementation agent in its own worktree and landed by
the integrator under the arrangement ADR 0131 records; landed second, after M131, which is why its
number is not the plan's.

## Context

`integral` and `quadgk` have answered one-dimensional integrals since M43, and M123 finished their
options. Nothing here integrated over a plane or a solid, and the eight names that do — `integral2`,
`integral3`, `quad2d`, and the five legacy verbs `quad`, `quadl`, `quadv`, `dblquad`, `triplequad`
that MATLAB documents as superseded and still ships — were the last of `funfun`'s numerics.

The plan named the reason a two-dimensional integral is hard through an interpreter, and it is not
the mathematics. An adaptive rule that asks for one point at a time makes a hundred and ninety-six
trips through the interpreter per tile; MATLAB's makes one, because it hands the integrand a whole
14×14 array and reads the answer back as one matrix. Matching MATLAB's numbers to ten figures
required matching its *tiling* — which tile is split, in what order, under what error — and
matching its speed required matching its calling pattern. The two turned out to be the same
decision.

## Decision

### The tiled engine is Shampine's TwoD, tile bookkeeping included

`integral2`'s `'tiled'` method, `quad2d`, and the inner level of `integral3` are one engine,
`PlaneQuadrature`. The region between `ymin(x)` and `ymax(x)` becomes a rectangle in `(θ, φ)`; a
product Gauss–Kronrod (3, 7) rule is laid over each tile as two seven-point rules over two halves of
each side, so a tile carries fourteen abscissae in each direction and its whole 196-point array is
handed to the integrand in **one call**. That is what makes the method usable here, and it is also
how MATLAB counts: `MaxFunEvals` is a budget of calls, not points, which is why `'MaxFunEvals', 3`
stops after two tiles.

Both variables are read through a cosine — `x = mid + half·cos θ`, `y = bottom + (1 + cos φ)/2 ·
height` — so the map's rate vanishes at every edge and the abscissae crowd towards a boundary
without landing on it; `1/(√(x+y)(1+x+y)²)` over the unit triangle comes out as `π/4 − 1/2` without a
`1/0` ever being formed. `quad2d`'s `'Singular', false` turns the transform off, and on a polynomial
over a rectangle the two readings agree to the last bit, which is what says it costs nothing where
it is not needed.

Tiles are kept the way MATLAB keeps them: in arrival order, their *adjusted* errors in a second list
ascending, with a cross-reference between the two, so the worst tile is the last entry and costs no
search — and once there are more than 2,000 tiles the *smallest* is taken instead, which arrests the
list's growth on a problem the rule cannot resolve. The adjustment factor is measured, not assumed:
the four quarters together are a far better answer than their parent was, so their difference from
it says by how much the estimator overstated itself, and that factor is carried onto the quarters'
own estimates.

### A second one-dimensional engine, because node placement is the answer

`Quadrature.Integrate` splits one panel at a time under a QUADPACK-scaled error estimate, and it is
the right engine for `integral`. It is the wrong one under `integral3`'s outer level and under
`'iterated'`, and the reason is not accuracy but where the nodes land: every abscissa of the outer
integral costs a whole inner integration whose answer carries the inner tolerance's own noise, so an
outer mesh half an ulp away from MATLAB's samples a different function and the two answers part in
the sixth figure rather than the sixteenth. `Quadrature.IntegrateOverAMesh` is therefore a faithful
port of the mesh adaptation `integralCalc` uses — every panel short of its share `2·tol·|h|/L` of the
tolerance is halved in one sweep and the rest retired — over the existing (7, 15) table.

Two details are what put the outermost nodes on MATLAB's abscissae to the last bit. The endpoint
cubic is written `(b−a)(3t − t³)/4` away from the ends and the algebraically identical
`(b−a)(|t|−1)²(|t|+2)/4` within a quarter of them, which is how `integralCalc` writes it and avoids
the cancellation the first form carries. And `checkSpacing` runs *before* the integrand is
evaluated on a sweep, so when it fires the reported value is the previous sweep's — a port that
reports the running totals answers a different number on a problem that hits the minimum step.

`integral` and `quadgk` were **not** moved onto the new engine. `m124_integral` pins them, and one
of its lines is ADR 0123's recorded divergence, which must keep diverging: a `div=` line that agrees
is an unexplained line. If the project wants that divergence closed, the fixture is re-recorded in
the same commit.

### The legacy five are themselves, not wrappers

`quad` is Gander's `adaptsim` — three unequal starting subintervals off centre by 0.13579, Simpson
refined by double Simpson and one Romberg step; `quadl` is `adaptlob` — four-point Lobatto refined
by seven-point Kronrod, recursing six subintervals at a time, with the tolerance loosened up front
by a factor read off the integrand itself; `quadv` is `adaptsim` over an array-valued integrand with
the one tolerance held against the largest component. Each holds every subinterval to an
*absolute* tolerance, a local test rather than a global one, and each answers how many times it
called the integrand. Reimplementing them over `integral` would answer a different `fcnt` and a
slightly different `q`; the counts here are exact against R2025b on every fixture case. `dblquad`
and `triplequad` nest by *calling* the quadrature handle they were given, as MATLAB does, so a
caller's own `myquadf` works.

Two readings of the sources that decided counts: `eps(superiorfloat(a, b))` is `eps('double')`,
because `superiorfloat` returns a class name, and reading it as `eps(x)` moves the nudged endpoint
and changes `fcnt`; and the six re-evaluated points of the vectorization check are *different*
tables in `quad2d.m` and `integral2Calc.m` (`[16 74 132; 27 81 124]` against
`reshape(1 + (1:6)·⌊49/2⌋, 2, 3)`), and each costs a call, so the wrong table shifts the
`MaxFunEvals` warning by one tile. Both tables are carried.

### What the forms are, and one thing the plan had wrong

Every documented form of the eight is accepted; `integral` needed nothing, its `ArrayValued`,
`Waypoints`, `AbsTol` and `RelTol` having been there since M123. `quad2d` takes `AbsTol`, `RelTol`,
`Singular`, `MaxFunEvals` and `FailurePlot`; the last is accepted and draws nothing, because what it
draws is a picture of the tiles left unrefined and a numeric verb does not open a window here.
**MATLAB's `integral2` declares one output**: `[q, e] = integral2(...)` is `MATLAB:TooManyOutputs` in
R2025b, contrary to the plan's brief, so `errbnd` is `quad2d`'s alone and only `quad2d` has a
multiple-output form. The five legacy verbs emit no warning when called, checked with `lastwarn` in
R2025b, and none is emitted here.

### Reference sources

Read for the documented behaviour and the constants that define it, nothing copied: R2025b's
`toolbox/matlab/funfun/integral.m`, `integral2.m`, `integral3.m`, `quad.m`, `quad2d.m`, `quadl.m`,
`quadv.m`, `dblquad.m`, `triplequad.m`, and under `private/`: `integral2Calc.m`,
`integral2ParseArgs.m`, `integralCalc.m`, `integralParseArgs.m`, `Gauss3Kronrod7.m`,
`Gauss7Kronrod15.m`. Taken from them: the two node-and-weight tables; the default tolerances
(`integral2`/`integral3` `AbsTol` 1e-10 and `RelTol` 1e-6; `quad2d` `AbsTol` 1e-5 with `RelTol`
floored at `100·eps`, warning only when the caller asked for finer); the budgets (`quad2d` 2,000
calls, tiled `integral2` 10,000, the legacy three 10,000, the 1-D mesh 16,384 panels); the local
tolerance `TOL·Δθ·Δφ/(4·AREA)` floored at `100·eps·|ΣQ|`; the `/8` on both tolerances once the loop
is running; the 2,000-tile switch; the initial interval counts (ten for `integral`, three for every
nested integral); the 0.13579 split and the `eps(b−a)/1024` minimum step of the recursive rules;
and every warning and error text.

## Consequences

**The fixture.** `m127_quadrature.m`: 90 lines, 0 unexplained against R2025b, no `div=` line. The
documented examples of all eight names, singular corners, infinite outer limits, function-handle
limits, tolerance sweeps, every `fcnt` of `quad`/`quadl`/`quadv` **exact**, and the `MaxFunEvals`,
`minRectSize` and vectorization warning texts pinned through `lastwarn`.

**One quantity is pinned loosely, and it is not a divergence.** `quad2d`'s `errbnd` is a sum of
several hundred per-tile estimates accumulated in retirement order, so its last figures belong to
the summation and not to the method: on the documented example MATLAB answers
`4.2215438837031242e-07` and JGraph `4.2215438843280487e-07`, 1.5e-10 apart, while the values beside
them agree to 4e-16 — which is what says the tiles landed in the same places. The plan's `rel=1e-2`
stands on those four lines.

**Tests.** `MatlabQuadratureM127Tests` 17; the assembly at 7,703 on the default lane; four lanes
green. `stess_75.m` is ten sections and passes on `jgraph.exe` and on R2025b alike. The 28 `d14`
forms (with four local functions for the two-output forms) were probed and accepted on both
engines; the `d16` row — a smooth Gaussian over a disc, two hundred times, then once at a tight
tolerance — is written and, as ADR 0131 records, waits for the arc's one timing session.

**Coverage.** `funfun` goes from 21 to 29 of 40 names; the toolbox total from 288 to 296 of 377.

## Divergences

None. M127 adds no difference from MATLAB that a script can observe.

## Still open

- **`quadv` flattens a matrix-valued integrand's answer to a row** where MATLAB keeps the shape.
  No documented form or example exercises it and the fixture does not pin it; a shape question for
  whoever next touches `quadv`.
- **Complex-valued integrands and `single` inputs** are not handled by the plane and solid verbs;
  `integral` handles complex already. No documented form the prober runs asks for either.
- **`'FailurePlot'`** is accepted and draws nothing; if the picture is ever wanted it belongs with
  the graphics verbs.
- **The `integralCalc` mesh engine could very likely close ADR 0123's `strong_sing` divergence** for
  `integral` itself. It was deliberately not applied there; see the Decision.
