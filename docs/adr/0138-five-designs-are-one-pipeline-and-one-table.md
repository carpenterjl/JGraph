# ADR 0138 — Five designs are one pipeline, and one of them is a table

## Status

Accepted (M134).

## Context

Fifty-five names: five analogue prototypes, five classical IIR designs, four minimum-order rules,
four band transforms, two maps from the s-plane to the z-plane, the analogue frequency response,
twenty FIR designs, and the seventeen names that answer what a filter *is* rather than designing
one.

The milestone has a second job as well. M124 measured six Signal names against MATLAB and recorded
three divergences under ADR 0126: `butter`'s two-output form was refused and its single output
answered two rows; `freqz`'s single output answered two rows; and `firpm`'s coefficients were about
`1e-5` from MATLAB's, with the exchange warning that it had not converged at order four hundred.
All three are closed here, and the fixture lines that pinned them are ordinary pinned values again.

Three observations shaped the work.

**The five IIR designs are one pipeline.** `butter`, `cheby1`, `cheby2`, `ellip` and `besself`
differ in exactly one line — which prototype they ask for. Everything after that is shared:
pre-warp the cutoffs, realise the prototype in state space, apply the band transform *there*, map
to the unit circle, and read the answer off in whichever of four forms was asked for. The
state-space route is not a stylistic choice. A substitution like `s → (s² + ω₀²)/(s·B)` performed on
a sixteenth-order polynomial is an exercise in cancelling large numbers; the same transform on a
state matrix is a solve. The difference shows in the sixth figure of an eighth-order design and in
every figure of a twentieth-order one.

**One prototype is a table rather than a formula.** MATLAB's `besselap` carries the poles of the
Bessel polynomial to twenty-five significant figures for every order up to twenty-five, because
those roots cannot be found from the polynomial's coefficients at that accuracy — a
twenty-fifth-order Bessel polynomial's coefficients span twenty-five orders of magnitude, and a root
finder run on them loses most of its figures. The table is transcribed rather than computed.

**One prototype MATLAB does not publish.** `ellipap` is compiled. What is written here is the
classical construction through the Landen descending transformation, and it agrees with MATLAB's to
about the twelfth figure — close enough that the fixture pins it, and far enough from an accident
that it is worth saying how it was arrived at.

Every other name was read out of `toolbox/signal/signal` before it was written.

## Decision

### The IIR pipeline is written once, and the zeros come from two different places

`IirDesign.Classical` is the whole of it, and `AnalogPrototypes` and `FrequencyTransforms` are the
pieces it calls. What the five names add is a prototype and a parser.

The one place they genuinely differ is where their zeros come from. Butterworth and Chebyshev type I
put every zero at the band edge — `z = −1` for a lowpass, `z = +1` for a highpass, both for a
bandpass — so MATLAB writes them down in closed form and normalises the gain at one frequency.
Chebyshev type II, elliptic and Bessel have zeros that depend on the transform, so theirs are read
off the transformed system as transmission zeros. The two routes are algebraically the same and
disagree in the last bit or two, and each name uses the one its own source uses.

The output form is chosen by the number of outputs alone: four is state space, three is roots, two
is coefficients, and **one is the numerator by itself**. That last is not symmetric and not
guessable, and it is what M124's first divergence was about.

### The elliptic prototype is the Landen construction, written out

The two ripples fix a selectivity `k₁`; the degree equation turns that and the order into a modulus
`k`, which says where the transition band ends; the zeros are the reciprocals of the Jacobi `cd`
function at the odd half-points of the quarter period; and the poles are those same points
displaced along the imaginary axis by the amount that puts the passband ripple where it was asked
for. Every step is a Landen transformation, which converges quadratically, so seven steps are past
enough.

The one place a textbook transcription goes wrong is the inverse. The descending step inverts
`w = (1+v)·w₁/(1 + v·w₁²)`, whose root has to be written with its conjugate multiplied through:
the subtracting form loses every figure as `v` falls to nothing, which after seven steps is most of
them. Written the wrong way the answer comes back a factor of `2⁷` too small, which is a distinctive
enough number to have found the bug quickly.

### `firpm` is a transcription, and that is the whole of why it agrees now

The previous implementation was a clean reimplementation of the Parks–McClellan exchange from the
same textbook MATLAB's is from. It answered coefficients about `1e-5` from MATLAB's and warned that
it had not converged at order four hundred. Those are one fact rather than two: **the exchange's
fixed point is a property of the grid rather than of the mathematics.** Two implementations that
agree on the algorithm and disagree on where the candidate frequencies sit converge to two
different filters, both equiripple, neither wrong.

So `FirDesign` is now a transcription of `firpm.m`, one-based indices and all, down to three
details that a reimplementation has no reason to reproduce:

- the grid is laid out band by band with a spacing that depends on the order, each band's upper edge
  forced onto it exactly, and a band with fewer than eleven points re-cut into ten;
- the barycentric weights are a product that steps through the other extremes `⌊(r−1)/15⌋ + 1` at a
  time, which is a different rounding from the plain product and keeps the product away from
  overflow at high orders;
- the point list is generated as `start + k·step` rather than by a running sum, which is one point
  longer at the end for some bands — and one point is the difference between two exchanges.

With those, the design at order four hundred agrees with MATLAB to the tenth figure and converges.

### The analysis names share one parser, and the phase ones share one grid

Every response name takes the same three optional trailing arguments — a point count or a frequency
vector, the word `'whole'`, and a sample rate — in whatever combination the caller felt like. That
parser is written once.

Two things underneath are worth naming. MATLAB computes a response over a uniform grid **with a
transform** rather than by evaluating the polynomials, and folds a filter longer than the transform
round rather than truncating it; every name that takes a point count inherits that. And the phase
functions do not evaluate on the grid they were asked for at all: they evaluate on a grid of at
least eight thousand points, unwrap there, and keep every `k`-th value. Unwrapping is a comparison
against π between neighbours, so it is wrong whenever two neighbours are far apart, and making them
close is the only defence.

The five predicates take no roots when they can avoid it. Stability is decided by the reflection
coefficients rather than by the poles, because Schur's rule answers the question exactly and a root
finder only answers it to within its own error — a pole at `1 + 1e-14` is a root finder's opinion,
not a filter's.

### The machinery M133 borrowed has moved to where its names live

M133 wrote `firls`, `fir1` and `cheby1` early, because `interp`, `decimate` and `resample` are their
filters and a filter that agrees to six figures is a rate change that agrees to none. Those bodies
have moved into `FirWindowDesign` and `IirDesign` and `PrototypeDesigns` is gone; `Multirate` calls
the real names. The check that the move changed nothing is `m133_filtering.m`, which pins 2,780
lines and still passes unaltered.

`SecondOrderSections.FilterNorm` and `ImpulseLength` have moved to `FilterAnalysis` for the same
reason, and `filternorm` and `impzlength` are the names on them now.

### `let [b, a] = butter(…)` now means what it says in both dialects

Closing the `butter` divergence broke three scripts, and the break was informative. The JGS dialect's
destructuring `let` evaluated its right-hand side for **one** answer and took that answer apart,
which worked only while every multi-output builtin happened to pack its outputs into an array. It
now asks a multi-output builtin for as many answers as the statement names, which is what the
syntax has always looked like it meant. A `fn` still answers one array, because that is what a `fn`
returns.

### The fixture pins coefficients against the vector, not against themselves

`m134_design.m` pins **6,072 lines**. One rule in it is new and is the reason the count is
believable.

A design's coefficients are pinned to a fraction **of the vector they live in** rather than of
themselves. An analogue bandpass numerator alternates between coefficients of order `1e11` and
coefficients that algebra says are zero; the second kind arrive as a few units in the last place of
the first kind, which is dust, and a relative rule on dust is a rule about which order two engines
summed a cancelling sum in. Pinning `abs = scale · tol` says what is meant — this coefficient is
right to a part in `10^10` of the filter — and has the second virtue that the *rule* does not depend
on the data, so two engines cannot disagree about which rule applies.

Root lists are pinned by their count and by order-free digests, for the reason ADR 0137 gave.

## Consequences

Fifty-three names, in five new files under `JGraph.Signal` and three under
`JGraph.Scripting/Jgs`, with `FirDesign` and `IirDesign` rewritten and `PrototypeDesigns` deleted.
The Signal Processing Toolbox coverage document goes from **97 of 351 documented names to 150**.

`stess_81` adds 22 sections to the stress suite, which now runs 81 scripts; `stess_71`'s section 6,
which froze M124's three divergences, now checks that they are closed.

Three cheaper things for what follows. `designfilt` is M135's and it is a parser in front of these
designs, all of which now exist. `filternorm`, `impzlength`, `firtype` and the five predicates are
the `digitalFilter` methods M135 has to answer. And `FirDesign`'s exchange is the machinery M136's
`arbmagfir` designs would want.

### Divergences

- **`cfirpm` and `cremez` are not there.** The complex equiripple exchange is two more algorithms —
  a Lawson-weighted ascent and a descent stage — behind an interface that takes a caller-supplied
  response function, in about two thousand lines of MATLAB across four files. Approximating it would
  answer a design that passes no fixture, so the names are left unregistered rather than registered
  and always failing: `exist('cfirpm')` answering true for a name that cannot be called is worse
  than a missing name. `firpm` covers the linear-phase designs and `firls` the arbitrary ones.
- **A design's zeros can come back in another order.** `cheby2`, `ellip` and `besself` read their
  zeros off the transformed system as transmission zeros, and the generalized-eigenvalue pencil this
  build reads them from lists the same multiset in a different order from MATLAB's deflation. It is
  the same freedom ADR 0137 recorded for `roots`, and the fixture pins root lists by their count and
  by order-free digests for the same reason. The coefficients built from them agree.
- **A Bessel highpass or bandstop numerator is noise past its leading coefficient.** Those two
  transforms stack four zeros on one point, and a fourfold repeated root is found to about
  `eps^(1/4)`, which is four figures. Both engines lose them and lose them differently; the fixture
  pins those two numerators by digest rather than elementwise, and the leading coefficient — the one
  that means anything — agrees to twelve figures.
- **`grpdelay` answers different garbage exactly on a zero.** A lowpass design has all its zeros at
  `z = −1` and a bandpass has some at `z = 1`, and the frequency grids put a point exactly on each,
  where the delay is nought over nought. What comes out there depends on whether the arithmetic
  underneath put the zero exactly on the circle or a bit off it: MATLAB answers one finite number,
  this build's native linear algebra answers an infinity, and its managed fallback answers a third
  thing. The fixture skips those two points of the grid; every other point agrees to eight figures.
- **The band transforms read real coefficients.** MATLAB's `lp2lp`, `lp2hp`, `lp2bp` and `lp2bs`
  accept a complex numerator and denominator in their transfer-function form. This build reads real
  ones, which is every documented example and every call the design pipeline makes; a complex pair
  is refused rather than silently having its imaginary part dropped.
- **The display arguments of `maxflat`, `fircls` and `fircls1` are accepted and do nothing.**
  MATLAB's `'trace'`, `'plots'` and `'both'` print a table of admissible designs or draw the
  constraint violation as the search runs. The words are taken so that a call written for MATLAB
  runs, and the design is the same one; what is missing is the commentary.
- **The no-output plots draw one axes rather than MATLAB's panels.** `freqz`, `impz`, `stepz`,
  `grpdelay`, `phasez`, `phasedelay`, `zerophase` and `zplane` draw when nothing asks for their
  numbers, on this build's own axes: magnitude and phase go on one frame rather than two stacked
  ones, and `zplane` marks its roots without MATLAB's ×/○ glyphs or its multiplicity numerals. The
  numbers behind them are the pinned ones.

## Still open

- `cfirpm` and `cremez`, above. They are the only two names of the fifty-five left, and nothing
  later in this arc needs them: M135's `designfilt` routes `arbmagfir` through `firls` rather than
  through the complex exchange.
- The head-to-head timing rows for M127–M133 are still unmeasured, and M134's join them:
  `fir1(2000, …)` and `freqz(…, 2^20)`. Design is small work and neither row is expected to be
  interesting; the plan calls for one quiet session at the end of the arc, which is also where
  `dct`/`idct` at 4M is owed a batch-FFT speed-up from M132.
- `FilterAnalysis.Norm` at `p = 2` builds the whole impulse response to sum its squares, which for a
  design with a pole at 0.9999 is a hundred thousand samples for one number. The energy is a
  Lyapunov equation and could be solved in the order cubed instead. Nothing yet asks for it on a
  filter that slow.
