# ADR 0053 — The Statistics and Machine Learning Toolbox surface

## Status

Accepted (M53, 2026-08-08). Builds on [ADR 0052](0052-the-documented-argument-surface.md), whose
option parser, reduction descriptors and seeded random stream this milestone was waiting for, and
follows the shape of [ADR 0049](0049-the-image-processing-toolbox-surface.md), which is where the
"transcribe the documented list first, then implement against it" method comes from.

## Context

The user asked for the Statistics Toolbox as a full mirror, machine learning excluded. That phrase
hides the milestone's real problem, which is not how many functions there are but **how a mirror
knows what it is missing**. The R2021b dump in the demo workspace came from an install with neither
toolbox present, so `build-checklist.py` can see zero statistics names; nothing in the repo could
have reported a gap. Every other question — which distributions, which tests, how faithful the
p-values — is downstream of having a list that cannot quietly shrink.

The second problem is that a statistics answer is *plausible when wrong*. A distance matrix, a
p-value, a fitted parameter and a set of clusters all look like answers whatever the arithmetic did.
A test suite written by the same reasoning that wrote the implementation will agree with it. That
shaped how this milestone is validated far more than the size of the surface did.

## Decision

### The list is transcribed once, from a release-pinned page, and machine-checked afterwards

`tools/matlab-checklist/matlab-r2021b-stats.csv` is built from MathWorks' archived R2021b
alphabetical function list, read as markup rather than retyped — so an invented or misspelt name is
not a failure mode — and pinned to that release so nothing introduced later leaks in.
`verify-stats-coverage.py` then checks, on demand, that every listed name sits in exactly one bucket
of `docs/matlab-stats-coverage.md`, that no bucket names a function that does not exist, that
everything called implemented is really in `JgsBuiltinCatalog.cs`, and that the headline count
matches the tables. The counting rule is the IPT one: a name counts when MathWorks gives it a
reference page of its own, and a name the base language also documents is counted here and marked
implemented.

The arithmetic closed at **385 implemented, 204 excluded, 0 pending, of 589**. The pending bucket
being empty is the milestone's real claim: every name is either implemented or refused in writing,
with a reason, and nothing is left in the state of "not looked at yet".

### A new project, registered in one place, before the reductions are wrapped

`src/JGraph.Statistics` is a peer domain to `JGraph.Imaging`: pure numerics, referencing only
`JGraph.Numerics`, with the script layer in `JgsBuiltins.Statistics.*.cs` partials.
`RegisterStatisticsBuiltins` is called **before** `RegisterMatlabReductions`, so `var`, `skewness`,
`prctile` and their neighbours are wrapped for a dimension by the same machinery `mean` uses rather
than each carrying a private copy of it. Where a statistic puts something other than a dimension in
the slot after the array — `trimmean(X, 10, 'floor', 2)` — it handles its own tail, through the same
`JgsMatrix.SlicesAlong`/`JoinAlong` pair, so there is still only one description of what a dimension
means.

`range` is the one name the two dialects answer differently on purpose. JGS has meant
`range(start, stop, step)` since M12 and that surface is frozen, so the statistic replaces the
sequence builder in the MATLAB dialect only.

### What "full mirror, machine learning excluded" turned out to mean

The cut is not by subject but by **what the answer is**. A name that answers arrays is in; a name
that answers a trained model object is out, because the object is a runtime — fit, predict, loss,
cross-validate, tune — and not a function. So `regress`, `robustfit`, `glmfit`, `nlinfit`, `lasso`,
`ridge` and `stepwisefit` are all here, and `fitlm`/`fitglm`/`LinearModel` are excluded as one
family with that reason written down. The exclusions are grouped into ten families plus individual
entries, each with a one-line reason, and the excluded bucket is as machine-checked as the
implemented one.

### A distribution object is a tagged struct, not a new value type

`makedist`/`fitdist` answer a struct carrying a `Type` field — the same `TransformTag` convention
`affine2d` established — and every name that asks a distribution a question (`pdf`, `cdf`, `icdf`,
`random`, `mean`, `median`, `std`, `var`, `iqr`) has a **guard** put in front of its existing
definition. The guard recognizes the object or hands the call straight on to the definition it is
standing in front of, so no name's previous behaviour is copied anywhere and none of it can drift.
`class(pd)` answers `'prob.NormalDistribution'`, and the class names construct as well, which is
purely additive: a spelling that errored now works.

The same mechanism took a second shape in wave K without changing: `paretotails` is a different
tagged struct, and one more guard in front of the same three names is all it took for a piecewise
distribution to be a distribution.

### A call written as a statement is told that nobody wanted its answer

MATLAB's `ecdf(x)` draws and `[f, x] = ecdf(x)` computes. The interpreter had no way to express
that: `CallMultiple` routed to `MultiOutput` only when more than one output was wanted, so nargout
zero was inexpressible. `BuiltinFunction.KnowsWhenDiscarded` is an opt-in flag that lets an
expression statement reach the multi-output body with `wanted == 0`. Twelve lines in the
interpreter, off for every builtin that does not ask for it.

### The plot verbs added no plot object

Twenty-nine of them — box plots, dendrograms, probability plots, performance curves, glyph plots,
scatter-histograms — are built entirely out of lines, patches, scatters, bars, text and subplots
that the figure model already had. What each one contributes is the arithmetic in front of the
drawing: which quartiles a box is built from, where a dendrogram's links go, what a probability plot
puts on its axis. No new plot object, no serialization change, and no `.graph` version bump in the
whole milestone. Where a picture genuinely needed a primitive that does not exist — three-dimensional
bars for `hist3`, a probability-scaled ruler — the numbers are answered exactly and the drawing
substitutes, in writing.

### Two optimizers, in the repo, tested before they were used

`NelderMead` and `LevenbergMarquardt` in `JGraph.Statistics/Optimize` are what the maximum-likelihood
fitters and `nlinfit` stand on. Both were validated standalone against problems with known answers
before any name was registered against them, because a fitter that converges to the wrong parameters
answers a number rather than an error.

## What the validation actually caught

The unit suites are table-driven pins against published R2021b values plus identities a plausible
wrong implementation fails: `icdf(cdf(x)) == x` over grids, masses summing to one, `var(x, 1)` and
`var(x, 0)` differing by the right factor, Welch not equal to pooled, ward not equal to single
linkage, a seeded draw reproducing twice.

That is the layer that caught the errors worth naming — and every one of them produced a
*plausible* answer, not a crash:

| Found | Was |
|---|---|
| Frank's conditional inversion | every draw came back not-a-number; the derived denominator was negated |
| Frank's density | negative, because the numerator carried `e^-a - 1` where the sign is not squared away |
| the Archimedean fit | answered a parameter of 74, then 55, where the truth was 2 — first because a flat penalty region defeated golden section, then because an *overflowed* density gave an infinite log-likelihood that beat every finite one |
| Pearson type 6 | chose its support from the signs of the roots rather than from the exponents, and refused a distribution it should have built |
| the stochastic embedding | diverged to a spread of 10²⁴ at the published learning rate, which is tuned for thousands of points |
| `eig` on a general matrix (M39, found the same way) | the precedent: validating a new kernel against an old one is how a wrong answer that nothing complained about gets found |

## stess_25.m, and what only a script could find

Thirty-seven sections, every argument form written at least twice in different shapes, the last two
being negative tests: a misspelt option word has to error *and name the alternatives*, and the
deliberate refusals have to still refuse. Its assertions are mostly identities rather than digits,
because an identity cannot be satisfied by accident — thirteen continuous families undoing
themselves, nine metrics surviving a `squareform` round trip, seven linkages each cutting into the
two clusters that are actually there, `pca` reconstructing its input to eight decimal places.

Three defects came out of it that no unit suite had reason to look for, and all three are the same
shape: **a form a person would naturally write, that nothing had written before.**

- **`x = rand` and `t + randn` did not work.** Both names evaluate to the function itself without
  brackets, so the addition failed with a type error. It surfaced writing `mhsample`'s proposal
  distribution — `@(t) t + randn` is how that is spelt in every textbook. Both names now auto-call
  bare in the MATLAB dialect; `zeros` and `ones` deliberately do not, because nobody writes those
  without a size.
- **`ksdensity` had no third output.** MathWorks documents `[f, xi, bw]`, and the bandwidth is
  interesting exactly when the caller did not name one: it is how a script reports what the automatic
  rule chose and makes a second estimate comparable to the first.
- **`cdf(pt, x)` refused a piecewise distribution.** Wave J built `paretotails` and left the reader
  that recognizes one unused — the object could be built and its fields read, but the three names
  that ask a distribution a question did not know it. One guard, in the shape the wave-I objects
  already used.

The script also found something about the *test*, not the code, and it is worth recording because it
will recur: an early version fed `pdist` a set of points containing a zero row, which makes the two
angle metrics and the two rank metrics undefined. The comparison did not fail — it *passed*, because
`min` and `max` omit missing values by default since M52, so the NaN never reached the assertion. A
missing value in a comparison is invisible unless something looks for it.

## Consequences

- **385 of 589, zero pending.** The count is machine-checked and the buckets must partition the list,
  so a later milestone cannot move a name without the arithmetic noticing.
- **No `.graph` change, no new plot object, no new value type.** The whole toolbox is builtins over
  `JGraph.Statistics` plus tagged structs; the figure format is untouched at v5. The graphics arc
  (M54 onward) is what bumps it.
- **`JgsHandleKind.Plot` is a stopgap.** Wave J needed handles to patches, scatters and bars that
  answer a few properties; M54 widens the handle surface properly and should build on it rather than
  around it.
- **`knnsearch` changed behaviour deliberately.** Wave H refused `'NSMethod'`; wave J accepts it,
  because the searchers now exist and both spellings answer the same neighbours. The wave-H test that
  asserted the refusal was replaced by one asserting the equality — a test flip that is the point of
  the change, not a regression.
- **The divergences are listed by name** in `docs/matlab-stats-coverage.md`, in two sections: what
  this mirror answers differently, and what it will state rather than match. The recurring ones are
  the percentile midpoint convention, the sign convention on component loadings, `mvncdf` beyond
  three dimensions, the optimizer tolerances, and every seeded draw — deterministic under a seed,
  never stream-compatible with MATLAB's Mersenne Twister.
- **Three gaps recorded, not fixed**, carried out of the milestone: a scalar struct field cannot be
  subscripted (`stats.df0(1)`); `gpstat` with a shape at or above one answers NaN where MATLAB
  answers Inf; `isstruct(pd)` answers true where MATLAB answers false, which is the visible cost of
  a distribution object being a tagged struct.
- **`dummyvar` of a row vector reads it as one observation of several grouping variables**, which is
  what a straightforward `[n, p] = size(group)` implementation does and may well be what MATLAB does
  too. It is not asserted either way here; `stess_25` passes it a column, which is how a grouping
  variable is written and is unambiguous under both readings.
- Gate at close: Release build 0 warnings, the full suite green, `run-stress.ps1` all 25 scripts,
  `verify-stats-coverage.py` and `verify-ipt-coverage.py` both OK.
- **In-app checks left to the user**, being what batch structurally cannot exercise: that the plot
  verbs look right in a figure window (a box plot's whiskers, a dendrogram's leaf order, a
  scatter-histogram's marginal axes), and that the new plots behave in the inspector and the plot
  browser.
