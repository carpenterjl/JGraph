# ADR 0139 — `designfilt` is a table, and a cascade is a closed form

## Status

Accepted (M135).

## Context

Six names: `designfilt`, the `digitalFilter` value it returns, and the four one-line filters
`lowpass`, `highpass`, `bandpass` and `bandstop` — plus the two dozen methods a designed filter
answers to, every one of which is a name M134 already wrote with a value in front of it.

The milestone reads as a parsing job and mostly is one. `designfilt` designs nothing itself: a
response name picks a list of parameter sets, the names the caller used pick one set out of that
list, the set and the chosen method pick a design, and the design is one of M134's. There are
seventy such combinations in the Signal Processing Toolbox alone, and the whole of the milestone's
FIR half is the table that maps them.

The IIR half is not a parsing job, and that is the finding.

**A `digitalFilter`'s coefficients are second-order sections, and those sections are not a
factorisation of a transfer function.** M134 designs all five classical filters, and designs them
correctly — as zeros, poles and a gain. Turning that into biquads leaves two things free: which pole
pair goes in which section, and how the overall gain is spread across them. `zp2sos` has four
answers to the first and three to the second, and none of the twelve combinations is MATLAB's. The
reason is that MATLAB does not factorise anything. Each of the sixteen band-and-method pairs has a
closed form that writes each biquad's four free coefficients and its scale value directly from the
prototype's pole angles, with the bilinear transform already inside the algebra rather than applied
after it. The section order, the exact `2` in a lowpass numerator, and the gain each section carries
are consequences of that formula, not choices made afterwards — all but one of them, and that one
is below.

**A modulus close to one needs sixty Landen steps, not seven.** M134's elliptic machinery took a
fixed seven descending steps, which is past convergence from a modulus of a half and nowhere near it
from a modulus of `1 − 1e-16`. A seventy-decibel stopband puts the complementary modulus exactly
there: the first steps only double the distance from one before the convergence becomes quadratic.
Seven steps left the designed poles wrong in the eleventh figure, which the numerator's middle
coefficient — a difference of nearly equal quantities — amplified to the tenth.

## Decision

### The cascade designs are transcribed, one closed form per band per method

`CascadeDesign` carries the sixteen forms. A digital specification becomes an analogue lowpass one —
the bilinear pre-warp for a lowpass or highpass, the band's own `c` parameter for a bandpass or
bandstop — and a minimum-order request first becomes an order and a re-stated edge, by one of four
rules that differ only in which band `MatchExactly` holds to. Then one formula turns the analogue
specification into biquads that are already digital.

A bandpass or bandstop section comes from a fourth-order block whose denominator is split by finding
its roots, and that is the one place the reference's answer depends on a root finder's output order
rather than on arithmetic. It is not followed there. This repository has two linear-algebra backends
whose eigensolvers list a quartic's roots in different orders, so following the reference literally
would make a bandpass cascade's section order depend on which backend was loaded — which is not a
difference a filter is allowed to have. The roots are found instead by a fixed-point iteration that
is the same arithmetic in every lane, the sections are ordered gentlest first, and each pole pair is
given the zero pair nearest it in angle. Measured against R2025b over sixteen order-given designs and
twenty minimum-order ones covering every `MatchExactly`, every coefficient and scale value agrees to
the last bit once the sections are put in a common order.

### The Landen descent runs until it converges

`Landen` now iterates while the modulus exceeds the machine epsilon, as the reference does, rather
than taking a fixed count. The complementary square roots in the degree equation are also written
`√(1 − x²)` rather than the better-conditioned `√((1−x)(1+x))`, because for an odd order the two part
in the eleventh figure and a modulus is what every pole of the design is a function of. M134's
`ellipap` moved in its last figures as a result, and its fixture still passes.

### The table is data, and the seventy combinations are measured

`FilterSpecification` holds the ten responses, their parameter sets, and the methods each set
admits. Matching is exact on the set of names given: a caller who names a set that is not in the
table is told which sets are. The FIR half routes to M134's `fir1`, `firls`, `firpm`, `fircls` and
`maxflat`; both minimum-order searches — the Kaiser one and the equiripple one — estimate an order,
design, measure the response on a fixed grid, and grow the order until the specification is actually
met, because the estimates are empirical fits and can be a tap or two short. All 1,769 recorded FIR
coefficients agree with R2025b bit for bit.

Two rules in that half are not guessable from the specification and were found only by reading the
reference. A highpass or bandstop Kaiser design forces its order even whatever `MinOrder` says,
because an odd-order symmetric filter has a zero exactly where the response is meant to be one. A
bandpass or bandstop Kaiser design equalises its two transition widths first — the narrower wins,
and the stopband edge moves — because one window has one shape and cannot hold two transitions.

### A `digitalFilter` is a class-tagged struct

The value is a struct carrying the class name `digitalFilter`, which is M62's exception and M129's
decomposition again. `class(d)` answers `digitalFilter`, `d.PassbandFrequency` reaches the
specification the filter was designed from, and every analysis name takes one without learning a new
kind of argument: each asks for coefficients first and carries on with what it gets back. That is
the whole of how the two dozen methods work.

An IIR filter's predicates are the exception: they are asked of the sections rather than of the
polynomial the sections multiply out to. Eight zeros at `−1` leave the expanded numerator with roots
a hundredth off the unit circle, so the expanded filter is not minimum phase when the cascade
plainly is.

### The four verbs decide FIR against IIR by the signal's length

`lowpass` and its three siblings turn a steepness into a transition width as a fraction of the
distance to the band edge, the width into a stopband edge, and the edge into a Kaiser order
estimate. If that order is more than half the length of the signal in hand, the answer is an
elliptic IIR filter instead — and if the signal cannot even carry three times *that* order, which is
what `filtfilt` needs, the order is capped at a third of the length. Both caps are why a short signal
gets an answer rather than an error.

## Consequences

Ten of `designfilt`'s eleven Signal-only responses design exactly what MATLAB designs, across all
four bands, both impulse responses, every parameter set and every method — 3,514 coefficients
measured bit for bit before the fixture was written. The fixture pins 4,154 lines.

M134's fifty-three names gained a second caller each. Nothing in `JGraph.Signal` was rewritten for
this milestone except the Landen descent; `CascadeDesign` and `FilterSpecification` are new files
above the existing designs rather than through them.

The `digitalFilter` value is the first one in the repo that a dozen unrelated names accept. The hook
is a single line in each family's argument reader, which is what made teaching fifteen analysis
names and three filtering ones a small change rather than a large one.

### Divergences

- **`arbmagfir` is not designed.** Its `equiripple` method needs `cfirpm`'s complex exchange, which
  ADR 0138's first divergence records as absent, and its other two methods are not routings of names
  that exist: the least-squares one is its own weighted solve against a response interpolated onto
  the exchange's grid, and the frequency-sampling one is its own inverse transform with a linear
  phase term, neither of which is `firls` or `fir2` with different arguments. The response is left
  out of the table rather than approximated, so `designfilt('arbmagfir', ...)` is refused with the
  list of responses that are there.
- **A generalised Butterworth is not designed.** `lowpassiir` and `highpassiir` accept a
  `NumeratorOrder` and `DenominatorOrder` pair in MATLAB, which designs the maximally flat IIR filter
  whose two orders differ. `maxflat` designs it (M134), but the cascade MATLAB stores it in is
  reached by a route this milestone did not measure, so the parameter set is absent and the call is
  refused with the sets that are there.
- **The DSP System Toolbox responses are absent.** `arbmagiir`, `arbmagnphasefir`,
  `arbmagnphaseiir`, `arbgrpdelayiir`, `isinclowpassfir`, `isinchighpassfir`, `notchiir`, `peakiir`
  and `fracdelayfir` belong to a product this repo does not stand in for, as do the extra parameter
  sets those responses add to the ten that are here.
- **The list of valid parameter sets is the Signal-only list.** When a specification does not match,
  MATLAB names the sets from every installed toolbox and orders them by how close each is to what was
  typed; this one names the sets the response actually has, in the order the table holds them. The
  refusal is the same refusal; the advice is shorter.
- **An IIR filter's responses go through the expanded transfer function.** `freqz`, `impz`, `stepz`,
  `grpdelay`, `phasez`, `zerophase` and `phasedelay` multiply the sections out first, where MATLAB
  evaluates them section by section. The two agree to the last few figures at the orders a design
  reaches here and part company at high order, which is what a cascade exists to avoid. The
  predicates and the filtering pass go through the sections; the responses do not.
- **A `digitalFilter` displays as a struct.** `disp(d)` prints the fields in one list rather than
  MATLAB's two labelled groups, and the value has no `filterAnalyzer`. `designfilt(d)` — the form
  that opens the design assistant to edit an existing filter — is refused rather than opening
  anything, as are the no-argument and response-only forms.
- **Five `digitalFilter` methods are absent.** `filterAnalyzer`, `filt2block`, `freqrespest`,
  `noisepsd` and `toMultirate` reach products this repo does not stand in for. `filternorm` is absent
  for the opposite reason: MATLAB's own list of the value's methods does not carry it, so neither
  does this one, and `filternorm(d)` is refused in both.
- **A bandpass or bandstop cascade lists its sections in another order.** Within each fourth-order
  block, which of the two sections comes first and which zero pair each is given follow from a root
  finder's output order in MATLAB and from a stated rule here — gentlest section first, each pole
  pair with the zero pair nearest it in angle. The filter is the same filter: `tf`, `zpk`, `freqz`
  and `filter` all agree. What differs is `Coefficients` row by row, and anything that runs the
  sections one at a time — a `filtfilt` transient above all, which is why the four verbs' first and
  last hundred samples are not pinned for an IIR design while the middle is pinned to seven figures.
  The rule chosen here is `zp2sos`'s, and it is the better-conditioned of the two: pairing by index
  instead can put a pole pair beside the unit circle in the same section as a zero pair on the far
  side of the band, and that section has a gain of thousands.
- **An odd-order bandstop window design disagrees in the first figure.** `designfilt('bandstopfir',
  'FilterOrder', 41, ...)` designs an antisymmetric filter and then scales it by the sum of its own
  taps, which is algebraically zero. Both engines answer coefficients of order `1e13` and they
  disagree by eighteen per cent, because the divisor is rounding dust in both. The design is
  meaningless in either engine and the fixture does not pin it.

### Still open

- `arbmagfir`, and with it the last of `designfilt`'s Signal-only responses. It needs either
  `cfirpm` (ADR 0138) or two transcriptions of its own.
- The generalised Butterworth parameter set, which is a measurement rather than a design: `maxflat`
  already answers it as a transfer function.
- An IIR `digitalFilter`'s responses section by section rather than through the expanded
  polynomial, which matters at orders past about twelve.
- The single head-to-head timing session, now covering M127 through M135. A design is small; the
  rows are `fir1(2000, …)`, `freqz(…, 2^20)` and M132's owed `dct`/`idct` batch-FFT speed-up.
