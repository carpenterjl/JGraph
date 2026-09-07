# ADR 0137 — A filter is four spellings of one recurrence

## Status

Accepted (M133).

## Context

Forty-two names: thirty-six from the Signal Processing Toolbox's filtering, conversion and multirate
sections, plus the six coefficient conversions MATLAB keeps in a shared control folder — `tf2zp`,
`zp2tf`, `tf2ss`, `ss2tf`, `zp2ss`, `ss2zp` — which the plan implements beside them because half the
toolbox calls them.

A digital filter is one recurrence and four ways of writing it down: a ratio of polynomials, the
roots of those polynomials, a first-order recurrence in a state vector, and a chain of second-order
ratios. Twenty-four of this milestone's names walk one edge of that shape and the rest run the
recurrence over a signal or change its sample rate.

The arithmetic in all of it is a page of textbook. What is not in the textbook, and what this
milestone is actually about, is the bookkeeping — and there is a great deal more of it than the
name count suggests.

Three examples set the tone.

A second-order section with no zeros has the numerator `[0 0 1]`. Not `[1 0 0]`: MATLAB builds each
section through `zp2tf`, which right-aligns the numerator against the denominator, so a section with
fewer zeros than poles is a delay rather than a pass-through. Get it wrong and the cascade is the
same filter shifted by a sample, which every magnitude check in the world will pass.

`zp2sos` orders its poles by distance from the unit circle and then pairs each zero with the pole
nearest it. Neither of those changes the filter and both change the matrix, so a cascade built from
the formula rather than from the rule agrees with MATLAB's in its multiset of coefficients and in
nothing else.

`filtfilt` has two arithmetic routes through one algorithm, and MATLAB chooses between them by
signal length: below ten thousand samples in one column it builds the reflected signal whole, and
otherwise it carries a state through the pieces. The two agree to about the last figure and not
beyond it.

None of that is in the documentation; all of it is in the sources, which is where it came from.
Every name here was read out of `toolbox/signal/signal` or `toolbox/shared/controllib/general`
before it was written, and the four that are MEX files — `upfirdn`, `sosfilt`, `latcfilt` and the
zero-finder inside `ss2zp` — were measured against R2025b instead.

## Decision

### The section builder is one routine, and the numerator is right-aligned

`SecondOrderSections` holds the whole of `zp2sos` and its relatives. The roots are paired by
`cplxpair`, split into complex and real, the poles sorted by their distance from the point on the
unit circle at their own angle, and the zeros pulled one at a time to whichever pole is nearest.
Then the sections are built from the bottom up, so that the last pair written is the first section,
which is what makes `'up'` and `'down'` mean what they say.

One routine builds every section: the denominator is the poles' polynomial, the numerator is the
zeros' polynomial **right-aligned against it**, and both are padded on the right to the section's
width. That single rule covers the all-pole section, the odd zero against a pair of poles, the odd
pole at the end of an odd-order cascade, and the fourth-order sections `zp2ctf` asks for. Written
per case, as the sources are, it is four places to get the alignment wrong.

The scaling forms need a filter's two-norm and its peak response, which is `filternorm`, an M134
name. It is written out here as `SecondOrderSections.FilterNorm` along with the impulse-response
length rule it needs; **the `filternorm` and `impzlength` names stay M134's.**

### The multirate names are their filters, so the designs come forward from M134

`interp`, `decimate` and `resample` each design a filter before they touch a sample, and the answer
is that filter convolved with the signal. Two implementations that agree on polyphase resampling
still disagree on every output sample unless they agree on the coefficients to the last bit.

So `PrototypeDesigns` carries exactly the paths those three take: `firls` for a linear-phase design
of even symmetry, the window method built on it for a lowpass, and the Chebyshev type I lowpass
through an analogue prototype, a state-space realisation, a frequency shift and the bilinear
transform. The route through state space rather than through the coefficients is not decoration —
it is what keeps an eighth-order design's poles where they belong, and it is what MATLAB does.
**The names `firls`, `fir1`, `cheby1`, `cheb1ap`, `lp2lp`, `bilinear` and `grpdelay`, their other
band types, their analogue forms and their `[z, p, k]` outputs remain M134's**; what is here is the
arithmetic three multirate names call.

`upfirdn` is a MEX file. It is written here as one polyphase loop: each output sample reaches into
the input at a stride of the interpolation factor and picks up only the taps that land on a real
sample. That is the same additions in the same order as upsample-convolve-downsample, so the answers
are bit-identical to the naive version and only the cost differs.

### `filtfilt` keeps both of MATLAB's routes, because the length chooses between them

The zero-phase pass itself was written for M132's `demod`, as `ZeroPhaseFilter`. What M133 adds is
the name, the cascade form, the walk down several columns, the scale-value and `'ctf'` readings —
and the second arithmetic route.

`FilterPasses.ZeroPhase` implements both. One column shorter than ten thousand samples goes through
`ConcatenatedPass`, which builds the reflected signal as one array; anything else goes through
`SplitPass`, which filters the leading reflection to pick up a state and carries it through the
signal and the trailing reflection without ever holding them together. The algebra is identical and
the summation order is not, so the two part company in the last figure. Implementing one and using
it for both would fail a fixture pinned on the other, which is a strange-looking bug to hunt.

### Three more milestones lent this one their machinery

`tf2latc` and `latc2tf` are M133 names and they are `poly2rc`, `rc2poly` and `rlevinson`, which are
M137's. The Levinson step up and step down are written out in `LatticeFilters` and the three names
stay M137's.

`fillgaps` predicts across a gap with Burg's method at an order Akaike's criterion picks, which is
`arburg` — also M137's. `SmoothingFilters.Burg` and its order-selecting form are here; the name is
not.

`envelope`'s peak reading needs local maxima at a minimum separation, which is `findpeaks` — M136's.
The separation rule alone is written here; the eight name-value pairs, the widths and the prominence
stack stay M136's.

This is the third milestone in a row to borrow forward, and the pattern is now deliberate: a name
belongs to the milestone that owns its options and its error messages, and the arithmetic underneath
it goes wherever it is first needed.

### The parity fixture pins arrangements elementwise and signals by digest

`m133_filtering.m` pins **2,780 lines**. The two halves of it are pinned differently on purpose.

A conversion's answer is small and every element of it is a decision: which pole went in which
section, which zero was paired with it, where the gain landed. Those are pinned elementwise with the
row count exact, because a digest over a section matrix passes while the sections are in the wrong
order, and the wrong order is the one mistake worth catching.

A filter pass's answer is two hundred samples of nothing very interesting, and is pinned by the same
four digests M132 introduced — mass, total variation, and two bounded ratios carrying sign and
position. A rate change adds its length, exact, because the length is the statement about how the
filter's delay was trimmed.

One rule is new. A coefficient that is zero in algebra and a few units in the last place in
arithmetic is pinned `abs=1e-9` rather than relatively: a relative rule on 1e-16 is a rule about
which order two engines summed a cancelling sum in, which is not what any of this is testing.

## Consequences

Forty-two names, in seven new files under `JGraph.Signal` and three under
`JGraph.Scripting/Jgs`. The Signal Processing Toolbox coverage document goes from **61 of 351
documented names to 97**, and the builtin document from 1,113 to **1,114** — a milestone of
forty-two names moving a total by one, because `ss2tf` is the only one of them MATLAB keeps in a
base folder that the inventory covers.

`stess_80` adds 29 sections to the stress suite, which now runs 80 scripts. All four lanes are green
at **7,889 tests**, up 61.

Three things are cheaper for the milestones that follow. `PrototypeDesigns` is the first half of
M134's design family and `SecondOrderSections` is the value M135's `digitalFilter` will carry;
`FilterPasses.BlockConvolve` is the overlap-add M136's spectral estimators will want; and
`SmoothingFilters.Burg` is M137's `arburg` with a name missing.

### Divergences

- **A pair of real roots can come back in the other order, so `sos2zp` and `tf2zp` may list the same
  zeros the other way round.** `roots` computes eigenvalues without asking for eigenvectors, and
  LAPACK's eigenvalue-only path deflates a real pair in a different order from the path MATLAB's
  `eig` takes — `roots([1 0 -0.25])` is `[0.5; -0.5]` in MATLAB and `[-0.5; 0.5]` here, while
  `eig([0 0.25; 1 0])` agrees in both. This is `roots`'s behaviour since M100 rather than anything
  M133 changed, and matching it would mean computing eigenvectors nothing wants; the fixture pins
  root lists by order-free digests and their count, which is what a root list means.
- **`fillgaps`'s order-choosing form agrees to seven figures rather than to twelve.** Akaike's
  criterion picks a forty-seventh-order autoregressive model for the fixture's segment, and that
  model has a pole at 1.0009 — just outside the unit circle — so running it eleven samples into the
  gap grows the difference between two fits rather than damping it. The fits themselves agree to the
  last few bits; the fixed-order forms either side of it are pinned at 1e-9 and pass.
- **`filtfilt` declines the `digitalFilter` argument.** `filtfilt(D, x)` and `fftfilt(D, x)` take a
  designed filter value, which is M135's; `filtfilt`'s two-argument form is the one that would
  otherwise be read as a numerator and a denominator, so it says in words what is missing rather
  than guessing.
- **`resample`'s non-uniform form is declined.** `resample(x, tx)` and `resample(x, tx, fs)` take a
  time vector, interpolate onto a uniform grid and resample that; the interpolation half is a second
  algorithm with its own four methods and its own edge rules, and it is not in this milestone's
  scope. The rational form and its filter, length, Kaiser-shape and given-filter readings are all
  implemented.
- **`ss2zp` and `ss2tf` read a system with one output.** MATLAB's take a matrix `C` and answer one
  column of zeros per output row. The transmission zeros here come from the system pencil's finite
  generalized eigenvalues, which is one problem per output; the loop over outputs is not written, and
  a system with several is refused in words rather than answered for its first.
- **`latcfilt` does not take a dimension.** Its sixth argument processes an array along a chosen
  dimension, and it is refused rather than ignored. Every other form — the feed-forward lattice, the
  all-pole one, the ladder, the initial and final conditions, and the walk down the columns of a
  matrix — is implemented.
- **`filtstates` is a function that answers a struct of constructors, not a package.** MATLAB spells
  these `filtstates.dfiir(num, den)` and `filtstates.cic(int, comb)`, which is a namespace this
  interpreter does not have. The call site reads the same and `filtstates.dfiir` on its own is a
  function handle rather than a default-constructed object.

## Still open

- The head-to-head timing rows for M127–M132 are still unmeasured and M133's join them: `filtfilt`
  at 10M, `resample` at 10M by 3/2, `medfilt1` at 10M with a width of 51, and `fftfilt` at 10M with
  a 1024-tap filter. The plan calls for one quiet session at the end of the arc.
- `dct`/`idct` at 4M are still fifteen times slower than MATLAB's, deferred from M132 to that same
  session.
- `SecondOrderSections.FilterNorm` recomputes the impulse response for every section boundary of a
  scaled cascade, which is quadratic in the section count. Cascades are short and it has not
  mattered; M134's `filternorm` should carry the running product rather than rebuild it.
