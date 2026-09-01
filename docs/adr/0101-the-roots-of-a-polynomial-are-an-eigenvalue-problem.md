# 0101 — The roots of a polynomial are an eigenvalue problem

Date: 2026-08-28 · Status: accepted (M100; toolbox-function arc, item 2)

## Context

M99 opened the third population — the names MATLAB writes in MATLAB and ships on its own path — and
built the machinery to count it: `docs/matlab-toolbox-coverage.md`, a prober, and a fifth verifier.
It closed one folder of six names.

This milestone takes the fourteen everyday names the plan put first: `roots` `poly` `polyder`
`polyint` `polyvalm` from `polyfun`, `conv` `deconv` `convn` from `datafun`, and `nextpow2`
`unwrap` `cplxpair` from `elfun`, plus `polyarea` `rectint` `inpolygon`. Twenty-eight documented
forms.

They are not exotic. `roots` and `conv` are among the first things anybody reaches for, and JGraph
has never had either. `polyval` and `polyfit` have been here since M42, so a script could fit a
polynomial and evaluate it but could not multiply two, divide one by another, differentiate one, or
find where one crossed zero.

### Two things found on the way in

**`polyval` was registered twice.** `JgsBuiltins.MatrixFunctions.cs:30` declared a two-argument
Horner loop; `JgsBuiltins.DataAnalysis.cs:45` declared the real one, which takes the centring and
scaling from `polyfit` and has a second output. `Define` re-declares rather than wraps, and
`RegisterDataAnalysisBuiltins` runs after `RegisterMatrixFunctionBuiltins`, so the second won and
the first had been dead since M42. It was found by asking the index for every call passing the
string `"polyval"` — two registration sites for one key is what a shadowed entry looks like, and it
is not a question grep can answer, because a registration is an argument and not a declaration. The
dead one is deleted here.

**The form prober turned a literal `[]` into `[1]`.** A documented syntax like `max(A,[],dim)` or
`unwrap(P,[],dim)` writes the empty out because passing an empty is what *selects* that form. The
prober's bracket rule reads a bracket as a placeholder pair — `alim([amin amax])` — and its
emptiness test fell through the `all(...)` over an empty sequence, which is vacuously true, so `[]`
became a one-element array. Twenty-four forms across eight names had been measured that way. Fixed
here, because a wrong measurement is a different thing from a missing sample: M99 declined to *add*
samples it did not have, and that judgement stands, but a tool that asks the wrong question and
records the answer as a failure has to be corrected.

## Decision

**The arithmetic goes in `JGraph.Numerics`, and the shape rules go in the scripting layer.** Four
new files in the leaf project — `Polynomials.cs`, `Convolution.cs`, `PhaseSequences.cs`,
`PlanarGeometry.cs` — and one new partial, `JgsBuiltins.Polynomials.cs`, holding the part that is
about MATLAB rather than about mathematics. That split is not tidiness: what is genuinely difficult
about these names is almost entirely on the MATLAB side.

**`roots` is `eig` on the companion matrix, and nothing else.** The roots of a monic polynomial are
exactly the eigenvalues of its companion matrix, so the question is handed to the same LAPACK path
`eig` uses. The alternative — Newton, or deflation — would need its own convergence story and would
disagree with `eig` on the hard cases, which are the ones anybody checks. The cost of the decision
is stated as a divergence below: `roots` inherits `eig`'s answer including where it differs from
MATLAB's.

**Degenerate leading coefficients are stripped before the matrix is built.** A polynomial whose
leading coefficient is tiny beside the next has roots out near infinity; dividing through by it
overflows and the companion matrix arrives full of infinities the eigensolver can only answer with
NaN. Those terms are discarded and the roots they carry are simply absent, which is what MATLAB
documents.

**Convolution is direct, never transformed.** An FFT is far cheaper for long operands but the two do
not agree to the bit: the transform route leaves a floor of dust where the direct sum leaves exact
zeros. MATLAB's own `conv` is direct, so a transform here would show up as a divergence on every
test that convolves a short filter with anything.

**`polyint` refuses a column, because MATLAB's own concatenation does.** MATLAB divides `p` by the
row `length(p):-1:1` and concatenates the constant on the right; handed a column the division
broadcasts to a square and the concatenation cannot happen. This is replicated rather than repaired
— `MATLAB:catenate:dimensionMismatch`, the identifier MATLAB raises. A script that works on the real
thing works here, and one that would fail there fails here for the same reason.

**ADR 0100's identifier rule is applied again, not extended.** Fourteen more documented identifiers
are raised with MathWorks' spelling: `MATLAB:roots:NonVectorInput`, `MATLAB:poly:InputSize`,
`MATLAB:conv:AorBNotVector`, `MATLAB:deconv:ZeroCoef1`, `MATLAB:cplxpair:ComplexValuesPaired` and
the rest. All were read from the running MATLAB and match.

### Divergences recorded here

- **`roots` and `poly` answer whatever `eig` answers, including in the last bits where `eig` and
  MATLAB's own already differ.** This is the OpenBLAS-versus-MKL difference ADR 0089 accepted,
  arriving through a new door rather than a new one of its own: `poly(magic(4))` differs in the
  twelfth digit and the sixth roots of −1 in the sixteenth, and both were confirmed to come from
  `eig` by running `eig` alone on the same matrices in both engines. A signed zero can land on the
  other sign for the same reason — the real part of `roots([1 0 1])` is `-0` here and `+0` in
  MATLAB, which `1/real(r)` can tell apart and nothing else can. Every other answer these fourteen
  names produce was diffed against MATLAB R2024a and matches to all seventeen digits.

## What this did not close

- **`polyfun`'s interpolation half.** `spline`, `pchip`, `makima`, `ppval`, `mkpp`, `unmkpp`,
  `interpn`, `interp1q` and `interpft` are M101, and the geometry group — `griddata`, `delaunayn`,
  `convhulln` and the rest — is deferred with the plan's other multi-milestone arcs.
- **`deconv`'s post-R2021b arguments.** R2024a's `deconv` takes a shape word, a `Method` of
  `"least-squares"` and a `RegularizationFactor`. None is in the R2021b form dump this repository
  measures against, so none is counted and none is built; the one documented form, `[q,r] =
  deconv(u,v)`, is.
- **Four defects found in passing, none of them M100's, each recorded as its own task rather than
  widened into this one**: `sprintf` and `fprintf` reject printf flags such as `%+g`; a complex
  *scalar* cannot be indexed at all, so `y(1)` and `y(:)` both fail where a real scalar and a
  complex array both work; an infinity displays as `Infinity` rather than `Inf`; and the shared
  `Num` argument reader refuses a one-element array where MATLAB draws no distinction, so
  `round(2.567, [1])` fails although `isscalar([1])` is true.

## Consequences

`roots` inheriting `eig` means it also inherits every improvement to `eig`, and every backend
question about it is asked once. It also means a caller who wants roots more accurate than `eig`
will not get them here, which is the honest price.

The prober fix is the one change here that reaches outside the milestone. Both probers were re-run
after it: `matlab-toolbox-coverage.md` records one form it had been scoring as a failure, and
`matlab-form-coverage.md` did not move, because the `max` and `min` forms it also touches fail for a
second reason as well — the one-element-array refusal above. When that task lands, that document
should be re-measured and is expected to rise.

## Measured

Parity against MATLAB R2024a on this machine, one script through both engines and diffed. Note that
`jgraph.exe -batch "run('x.m')"` runs the **JGS** dialect; passing the filename itself as the
statement is what selects MATLAB by extension. Both dialects were run and gave identical answers for
these names, which are registered in both.

| Script | Lines compared | Lines differing | What differs |
|---|---:|---:|---|
| 14 names, 150 cases | 765 | 22 | `poly(magic(4))`, the sixth roots of −1, one signed zero — all from `eig` |
| the remaining forms, 55 cases | 282 | 2 | one signed zero, from `eig` |

Coverage, each number re-derived by its verifier rather than edited:

| Document | Before | After |
|---|---|---|
| `matlab-toolbox-coverage.md`, names | 172 of 377 | **186 of 377** |
| `matlab-toolbox-coverage.md`, forms | 225 of 1,036 | **241 of 1,036** |
| `matlab-builtin-coverage.md`, all kinds | 933 of 2,024 | **947 of 2,024** |
| `elfun` folder | 16 of 19 | **19 of 19 — complete** |
| `polyfun` folder | 6 of 34 | **14 of 34** |

Of M100's 28 documented forms the prober accepts 16, leaves 10 unprobed for want of a sample, and
records 2 as errors. Both errors are the prober's generic `matrix` sample, `[1 2 3; 4 5 6]`, being
non-square where `poly(A)` and `polyvalm(p,X)` need square; real MATLAB refuses the same two calls
with the same two identifiers, which was checked rather than assumed.

## Testing

`tests/JGraph.Tests/Scripting/MatlabPolynomialM100Tests.cs`, 71 tests, assertions inside the scripts
so what is pinned is MATLAB's answer and not JGraph's display. The shape assertions carry as much
weight as the value ones — `conv` alone has three orientation rules and one of them turns on whether
a one-element operand counts as a row.

Two tests exist for bugs this milestone had and fixed:
`Unwrap_MeasuresEveryStepAgainstTheOriginalRecord` catches measuring a step against an
already-corrected sample, which compounds the corrections and turns a steady ramp into a runaway;
`Cplxpair_OrdersOneGroupOutermostPairFirst` catches emitting a group of four sharing a real part
from the middle outwards instead of the outside in, which a group of two cannot distinguish.

Full suite 6,026 tests, 0 build warnings, five coverage verifiers exit 0.

## Live checks for the user

```matlab
p = poly([1 2 3])           % 1 -6 11 -6
r = roots(p)                % 3 2 1, as a column
conv([1 2 3], [1 1])        % 1 3 5 3
[q, rem] = deconv([1 3 3 1], [1 1])   % q = 1 2 1, rem = 0 0 0 0
polyder([1 0 -1])           % 2 0
unwrap([0 4 8 12])          % 0 -2.2832 -4.5664 -6.8496
cplxpair([2, 1-1i, 1+1i])   % 1-1i, 1+1i, 2
inpolygon(0.5, 0.5, [0 1 1 0], [0 0 1 1])   % 1
polyarea([0 4 4 0], [0 0 3 3])              % 12
```
