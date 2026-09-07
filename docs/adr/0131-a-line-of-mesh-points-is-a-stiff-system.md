# ADR 0131 — A line of mesh points is a stiff system

## Status

Accepted (M131, 2026-09-07). The first milestone of the solvers arc built by an implementation
agent in its own worktree and landed by the integrator; the working arrangement is recorded under
Consequences, because it changes what "the gate" means for the rest of the arc.

## Context

M126 left `funfun` with every initial-value solver MATLAB documents and none of the things that
are built on them. The largest of those is `pdepe`, the one-dimensional parabolic–elliptic solver:
a system `c·∂u/∂t = x⁻ᵐ ∂(xᵐ f)/∂x + s` on a mesh the caller supplies, in slab, cylindrical or
spherical symmetry, with boundary conditions `p + q·f = 0` at each end. MATLAB solves it by the
method of lines — discretise in space, integrate the resulting system in time — and the time
integrator it uses is `ode15s` with a mass matrix, which is exactly what M126 built. What was
missing was the discretisation, and `pdeval`, which reads a solution back between mesh points.

Beside `pdepe` sit the last of the folder's documented names: `symvar`, `vectorize`, `inline`,
`inlineeval` and `fcnchk`. They look unrelated and are not. `fcnchk` is what every legacy `funfun`
called on its first argument so that a string could stand for a function; `inline` is what it built
when the string was an expression; `symvar` is how `inline` decided what the arguments were. MATLAB
documents all of them as superseded by anonymous functions, and still ships them.

The plan named this milestone as the one whose numerics were already paid for, and it was. The
substance is the discretisation and the legacy helpers' text rules, and the fixture's strong claim
is not a step count — `pdepe` reports none — but the number of times each problem's coefficient
function is called, which is one call per subinterval per right-hand-side evaluation plus the
grouped columns of the numerical Jacobian, and moves the moment a step is taken differently.

## Decision

### The flux is taken at the one point the symmetry chooses

Skeel and Berzins integrate the equation over a half-cell either side of each mesh point and
approximate the flux at one interior point ξ of each subinterval. The whole content of the scheme
is which point: the midpoint for a slab, the logarithmic mean `(xR − xL)/log(xR/xL)` for a cylinder,
`xL·xR·log(xR/xL)/(xR − xL)` for a sphere; and the cell divider ζ between two mesh points is the
midpoint, the geometric mean `√(ξ·xM)` and `∛(xL·xR·xM)` respectively. Where the mesh starts on the
axis and `m > 0` those formulas divide by zero, so a second family — `(2/3)(xL² + xL·xR + xR²)/(xL +
xR)`, with `ξᵐ` replaced by `ζᵐ⁺¹/ξ` — is used on *every* subinterval rather than just the first,
which is what keeps the scheme's order uniform. Each family has its own interpolant (linear in `x`,
in `log x`, in `1/x`, and in `x²` on the singular branch), and `Pdepe.Interpolate` is the one place
it is written: forming the flux inside a cell and reading the solution between mesh points are the
same operation, and `pdeval` uses it rather than agreeing with it by construction.

`pdepe.m` writes its square and cube roots as `.^(1/2)` and `.^(1/3)`, and after ADR 0129's finding
that `Math.Pow` and MATLAB's `power` part in the last unit, `Geometry.For` writes `Math.Pow(v, 1.0 /
3.0)` literally rather than `Math.Cbrt`. The spherical problem exercises it on every subinterval and
agrees to 2e-13.

### Why it is a differential-algebraic system and not an ODE

An equation whose `c` is identically zero is elliptic and contributes no time derivative, and a
boundary condition with `q = 0` prescribes the solution rather than the flux. Both are expressed
the way `pdepe.m` expresses them: a constant diagonal mass matrix with a zero on those rows, and a
right-hand side that carries the residual there. The divisor `c` stays in the right-hand side
rather than moving into the mass matrix, because `c` may depend on the solution and a mass matrix
that did would cost a refactorization every step; where the divisor is zero the row is algebraic
anyway, and the divisor is replaced by one so the residual is left as it stands.

That is precisely the singular-mass path `ode15s` has had since M126, so `Pdepe.Solve` calls
`Ode15s.Run` directly — not through the interpreter — with `MassSingular = Yes`, `Refine = 1`, and a
`JPattern` that is block tridiagonal with `npde × npde` blocks, the coupling of a three-point
stencil. The pattern is what makes the numerical Jacobian cost `3·npde` derivative evaluations
instead of `npde·nx`, and it is why the coefficient-function counts below come out exact: the
grouped columns are the same three groups `colgroup` finds.

`pdepe` reads `RelTol`, `AbsTol`, `NormControl`, `InitialStep`, `MaxStep` and `Events` from the
options it is handed and overwrites everything about the mass matrix, the Jacobian and
vectorization, which is what the documentation says it does. The event function is the one place
the signature differs from the ODE family's — `[value, isterminal, direction] = events(m, t, xmesh,
umesh)` sees the whole mesh solution — so it has its own adapter in the builtin partial rather than
reusing the family's reader.

### `pdeval` is MATLAB's sweep, including its last block

The interpolant walks the mesh once, taking the leading run of query points that fall below the
next mesh point. Whatever is left is given the last subinterval's derivative but the *mesh's own
value* — that is what `pdeval.m`'s final block does, it is why `pdeval(m, x, ui, x(end))` answers
`ui(end)` exactly, and it is why the query points must be sorted. MATLAB's loop has the same
requirement and does not check it; neither does this one, and the fixture pins the trailing point.

### An `inline` is an anonymous function that remembers what it was written as

`inline` builds its value by handing `str2func` the text `@(args) formula`, and wraps the handle in
an `InlineFunction` carrying the formula and the argument names. It has to carry them: printing the
parsed expression back gives `(x ^ 2) + y` where the caller wrote `x^2+y`, and `formula`, `char` and
`argnames` are documented to answer what the caller wrote. `inline(expr)` derives its arguments
from `symvar`, falling back to `x`; `inline(expr, n)` names them `x, P1 … Pn`, the form the legacy
funfuns used to pass parameters through. `vectorize` of an inline is a new inline over the dotted
formula. `fcnchk` accepts a handle, a name or an expression, with `'vectorized'` as its option, and
throws for anything else — its two-output form, which answers a struct only ever fed back to
`error`, is not built, and nothing documents it.

`char` of an inline lives at the top of `char` itself in `JgsBuiltins.StringEditing.cs`, not in a
wrapper: `RegisterStringEditingBuiltins` runs after the solver block and re-declares `char`
outright, so a wrapper registered beside `pdepe` would be silently replaced.

### `symvar`'s rules are pieces of MATLAB syntax, not a principle

A run of word characters counts only when it starts with a letter; a run followed by `(` is a
function call, and blanks before the parenthesis are stripped first so `foo (x)` is one too; a run
preceded by `.` is a field name or the tail of `1.e10`; text inside quotes is skipped, and a quote
that follows an identifier, a closing bracket, a dot or another quote is a transpose rather than
the start of a string; and the constants left out — `i j pi inf Inf nan NaN eps` — are matched
case-sensitively, so `I` and `J` survive. The answer is a sorted cell column; MATLAB sorts
space-padded char rows, which agrees with an ordinal sort because the pad is below every word
character.

### What is declined, by name

`odeexamples` is a GUI. The forty-seven files under R2025b's `funfun\examples\` — `amp1dae ballode
batonode brussode burgersode ddex1 ddex1de ddex1delays ddex1hist ddex2 ddex3 ddex4 ddex5 emdenbvp
fem1ode fem2ode fsbvp hb1dae hb1ode iburgersode ihb1dae kneeode lorenz lotka mat4bvp orbitode pde
pdex1 pdex1bc pdex1ic pdex1pde pdex2 pdex3 pdex4 pdex5 rcbvp rigidode shockbvp threebvp twobc twobvp
twoode vanderpoldemo vdp1 vdp1000 vdpode weissinger` — are examples, not API. The R2021b checklist
counts them at the top of `funfun`, which is why the folder's documented total reads 40; the ones
that matter are rewritten as local functions inside the parity fixtures of M125, M126 and this
milestone, and M130's will take the BVP and DDE ones.

### Reference sources

Read for the documented behaviour and the constants that define it, nothing copied: R2025b's
`toolbox/matlab/funfun/pdepe.m`, `pdeval.m`, `private/pdentrp.m`, `symvar.m`, `vectorize.m`,
`fcnchk.m`, `inlineeval.m`, the `@inline` class folder (`inline.m`, `argnames.m`, `formula.m`,
`char.m`, `subsref.m`, `vectorize.m`, `display.m`, `disp.m`, `feval.m`, `nargin.m`, `symvar.m`), and
`funfun/examples/pdex1.m` … `pdex5.m`. Taken from them: the ξ/ζ formulas per `m` and per
singularity, the half-cell volumes `(ζᵐ⁺¹ − xLᵐ⁺¹)/(m+1)` and `(xRᵐ⁺¹ − ζᵐ⁺¹)/(m+1)`, the `xᵐ`
scaling of the two boundary rows, the mass-matrix classification (`c == 0` everywhere, `q == 0` at
each end, the left end exempted when singular), the `odeset` fields `pdepe` overwrites, the
`MATLAB:pdepe:*` and `MATLAB:pdeval:*` identifiers, `vectorize`'s replacement order and `symvar`'s
constant list.

## Consequences

**The fixture.** `m131_pde.m`: 156 lines, 0 unexplained against R2025b, one `div=` line. All five
documented problems, the events form, and — sharper than the plan's "within 2%" — the **exact**
coefficient-function call count of every one of them: `pdex1` 1,122, `pdex2` 2,942, `pdex3` 4,921,
`pdex4` 2,365, `pdex5` 3,641. A `persistent` counter inside the problem's own local function is the
portable stand-in for `nsteps` on a solver that reports no statistics, and it works on both
engines. The worst observed relative error per problem was 2.8e-12, 2.0e-13, 2.8e-15, 3.9e-12 and
1.6e-10 (`pdex5`), so the value rules are `rel=1e-9` and `rel=1e-8` with two to three orders of
margin rather than the plan's `1e-6`.

**Tests.** `MatlabPdepeM131Tests` 12; the assembly at 7,685 on the default lane (up from 7,672);
four lanes green. `stess_74.m` carries a handful of the recorded numbers, `pdeval`'s trailing
point, the DAE boundary case, the events form, and the legacy helpers' text answers; it passes on
`jgraph.exe` and on R2025b alike.

**How this milestone was built, and what that changes.** M127–M131 were developed at once, each by
an implementation agent in its own git worktree off `d8eea40`, under one written brief: the agent
owns the numerics, the builtin partial, the fixture and its recording, the unit tests and a
handoff; it never runs the lane sweep (which kills every `testhost` on the machine), the stress
suite or the timing rows, never touches the demo workspace, and never touches `main`. The
integrator reads the handoff, squash-merges the branch, assigns the ADR and stress numbers in
landing order, regenerates the coverage and divergence documents, runs the four lanes and the
stress suite, and writes this document. **Head-to-head timing is deferred to one quiet session at
the end of the arc** — a deliberate deviation from gate step 5, decided because a five-run median
interleaved with MATLAB means nothing while four other builds are running. The proposed `d14` forms
and the `d16` row for `pdepe` are recorded in the handoff and will be measured then; none was timed
here.

**Coverage.** `funfun` goes from 17 to 21 of 40 names present; the toolbox total from 284 to 288
of 377. The names `inline`, `inlineeval`, `fcnchk`, `formula` and `argnames` are not in the R2021b
checklist's documented set and count as callable names only.

## Divergences

- **An `inline` is a `function_handle`, not an object of class `inline`.** `class(inline('x^2+y',
  'x', 'y'))` is `inline` in MATLAB, and `disp` of it prints `Inline function:` over `f(x,y) =
  x^2+y`; here `class` answers `function_handle` and `disp` prints a handle. Everything else about
  the value agrees — `formula`, `argnames`, `char`, `vectorize`, the argument heuristics and
  calling it. `inline` is documented for removal and `fcnchk` and `inline` are its only producers,
  so an `@inline` value class was not built. Pinned by `CHK|inline_class|inline|div=ADR0131` in
  `m131_pde.m`.

## Still open

- **`nargin(f)` on a function handle is a parse refusal in the JGS grammar**, reported as `Not a
  valid MATLAB file.` — it is the function form of `nargin`, not the keyword, and no catalog grep
  finds it because it never reaches a builtin. MATLAB's `@inline/nargin` answers the argument
  count; the fixture pins `numel(argnames(f))` instead. It belongs to a language milestone, not
  this one.
- **`pdepe(..., options, p1, p2, ...)`**, the undocumented trailing-parameter form `pdepe.m`
  forwards to its three problem functions, is not read; anonymous functions supersede it.
- **`[f, msg] = fcnchk(...)`** is not accepted; see the Decision.
- **The example files are declined, not absent by accident.** The gap-report inventory should
  count the forty-seven by name as decided, which is the reason the list above is spelled out.
