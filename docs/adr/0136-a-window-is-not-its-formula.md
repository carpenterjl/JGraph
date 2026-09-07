# ADR 0136 — A window is not its formula

## Status

Accepted (M132).

## Context

The Signal Processing Toolbox starts here. Fifty-eight names in one milestone: twenty windows and
`window` to reach them, `dpss`, eleven waveform generators, eleven transforms that are not the
Fourier transform, and twelve conversion and framing verbs — plus `chirp`, `db2mag` and `mag2db`,
which MATLAB keeps in its base folders and which the plan implements beside them.

Almost every one of these is short. A window is three lines of arithmetic, a rectangular pulse is a
comparison, a decibel conversion is a logarithm. That makes the milestone sound small and it is not,
because the arithmetic is not what has to be reproduced. What has to be reproduced is the *order*
the arithmetic is written in, the edges the formula does not decide, and the two or three places
where MATLAB does something a textbook would not.

Three examples set the tone for the whole milestone.

`hamming(65)` is symmetric in algebra. Evaluated as `0.54 - 0.46*cos(2*pi*i/64)` at every index it
is symmetric to about fifteen figures and not to sixteen, because the cosine of one angle and the
cosine of its reflection round differently. MATLAB computes the first half and mirrors it, so the two
halves are equal bit for bit. A window written from the formula passes every reasonable test and
disagrees with MATLAB in the last two figures of every coefficient.

`nuttallwin` scales its index by `i*2*pi/(L-1)` and `blackmanharris` by `2*pi*i/(L-1)`. Those are the
same number in algebra and not always the same double.

`rectpuls` is `abs(t) < w/2`, except that MATLAB writes it as `abs(t) < w/2 - eps` and then puts the
left edge back by hand, so a pulse owns its left edge and not its right and two abutting pulses do
not share a sample.

None of that is discoverable from the documentation. All of it is in the sources, which is where it
came from: every name here was read out of `toolbox/signal/signal` before it was written, and the
three that are MEX files — `chebwin`, `buffer`, `seqperiod` — were measured instead.

## Decision

### The windows are mirrored, and the periodic flag is a length rather than a formula

`SignalWindows` holds all twenty. The raised-cosine family — Hann, Hamming, Blackman, flat-top —
goes through one shared routine that computes `(L + L mod 2) / 2` coefficients and reflects them,
which is `gencoswin`. `'periodic'` is not a second formula: it lengthens the window by one, runs the
same arithmetic, and drops the repeated endpoint, so a periodic window of length 16 is the symmetric
window of length 17 without its last sample. That identity is pinned in the tests for six of the
seven windows that take the flag.

The seventh is `hanning`, which is not `hann` and is not on that list. It omits the two zero
endpoints rather than including them, so its denominator is `L+1`, its first coefficient is not
zero, and its periodic form prepends a zero to a *shorter* symmetric window. Two names one letter
apart that answer different windows is exactly the kind of thing a reimplementation gets wrong
silently, so both are written out separately.

`blackman` pins its first coefficient to zero rather than leaving it as the rounding of three terms,
and the mirror carries that to the last coefficient too. `taylorwin` does **not** treat a length of
one as trivial — it still adds its cosine terms, so `taylorwin(1)` is 1.5581 and not 1 — which is the
one place in the family where the two trivial lengths are not both trivial.

**The plan's warning about `blackmanharris` was wrong.** It said the coefficients here were "the
4-term minimum set" and differed from MATLAB's. They do not: the existing `Window.cs` already
carried `0.35875 0.48829 0.14128 0.01168`, which is what `blackmanharris.m` uses. What was actually
wrong with `Window.cs` was the thing the plan did not mention — it was symmetric-only and it did not
mirror. It now delegates to `SignalWindows`.

### `chebwin` is a MEX file, so the classical algorithm is written out and measured

`chebwin` calls `chebwinx.mexw64`, which cannot be read. The algorithm here is the one MATLAB's own
M-file used before the MEX arrived: sample the Chebyshev polynomial of degree `L-1` on the unit
circle, invert the transform, normalise. Below minus one the inverse hyperbolic cosine picks up a
half turn, and the cosine of a whole number of half turns is a sign; MATLAB carries the imaginary
part along and drops it later, and this multiplies by `(-1)^order` instead, which is the same answer
without the complex arithmetic.

The two agree to sixteen figures across the body of the window and to ten at its edges, where a
hundred decibels of dynamic range passes through one transform. The fixture pins the digests at
`rel=1e-13` and the three edge coefficients at `rel=1e-9`, and says so on the line above.

### `dpss` solves the tridiagonal problem, not the sinc kernel

The Slepian sequences are the eigenvectors of a sinc kernel whose eigenvalues crowd against one; the
matrix cannot tell them apart. There is a symmetric tridiagonal matrix that commutes with the kernel
and therefore shares its eigenvectors, and whose own eigenvalues are well separated, and that is what
both MATLAB and `DiscreteProlate` solve — by bisection on the Sturm count for the eigenvalues, then
three inverse-iteration solves per vector. The concentrations come afterwards, from an
autocorrelation against the band's sinc kernel.

The sign convention is MATLAB's and is not an accident of the solver: an odd-numbered sequence is
polarised to a positive mean, an even-numbered one to a positive second sample. Without it two runs
of `dpss` need not agree with each other, let alone with MATLAB.

The `'spline'` and `'linear'` forms interpolate from a table of sequences stored on disk. There is no
such table here and computing the sequences directly is both available and better, so those two
forms are refused in words rather than approximated.

### `demod` borrowed one name from M133

MATLAB's amplitude and quadrature demodulation mix the signal against the carrier again and remove
the image with a zero-phase fifth-order Butterworth — `filtfilt(butter(5, ...))`. `filtfilt` belongs
to M133. Rather than approximate it or defer `demod`, the zero-phase filter itself is written here as
`ZeroPhaseFilter`, with Gustafsson's endpoint treatment exactly as `filtfilt.m` does it: reflect the
signal through each endpoint for three filter orders, start each pass from the steady state the
filter would settle into at that endpoint's value, and trim. **The `filtfilt` name, its
second-order-section form and its threading remain M133's**; what is here is the one pass `demod`
needs.

### The colon operator was computing its ranges the obvious way, and MATLAB does not

This is the finding of the milestone and it is not about signal processing at all.

`pulstran` compares a shifted time against a pulse's edges, and its edges are open on one side. Two
of its 101 samples landed in the wrong frame. The cause was not `pulstran` and not `tripuls`: it was
`0:1/1000:0.1`, whose 91st element this interpreter put one unit in the last place above where
MATLAB puts it.

MATLAB does not compute a range as `start + i*step`. It computes the first half forwards from the
start and the second half backwards from the last element, so the drift of a step that is not a
binary fraction lands in the middle of the range, where the values are largest and it matters least,
rather than accumulating to the end. `PackedMath.RangeElement` now does the same, and both the boxed
range and the packed one go through it.

**And a `for` loop over a range written in its own head does not do that.** MATLAB steps there:
`for x = 0:0.1:1` reaches 0.6 by adding the step six times, while `v = 0:0.1:1; v(7)` reaches it by
counting back four steps from one, and the two differ by one unit in the last place. This was
measured in MATLAB rather than assumed — the first attempt at the fix made both spellings agree,
which is wrong. `ExecuteForOverSteps` now walks a range in a loop head by stepping, which is what the
compiled hot loop was already doing; a loop over a *variable* holding a range still walks the array.
Both halves are pinned in the fixture, because getting either one alone would look right.

Every whole-number range is unaffected: both roads are exact there.

## Consequences

Fifty-eight names, in eight new files under `JGraph.Signal` and four under
`JGraph.Scripting/Jgs`. The Signal Processing Toolbox coverage document goes from **6 of 351
documented names to 61**, and `funfun`-style folder counts elsewhere are untouched. The builtin
coverage document's total, which had not been updated through the five parallel milestones, is
brought back in step at **1,113 of 2,024**.

The parity fixture `m132_windows_generators.m` pins **1,234 lines** against R2025b, of which five are
recorded divergences. Its vector digests are deliberately not plain sums: most of these signals
oscillate about zero, so their sum is a cancellation of a hundred numbers of order one down to a
number of order 1e-16, and a relative tolerance on that says nothing. Each vector is pinned instead
by its mass, its total variation, and two bounded ratios that carry the sign and position
information a magnitude alone would lose.

`stess_79` adds 19 sections to the stress suite, which now runs 79 scripts. All four lanes are green
at **7,828 tests**, up 61.

Two things are cheaper for the milestones that follow. `FftKernels` now has four more callers that
are not the Fourier transform, which is the shape M136's spectral estimators will want; and
`ZeroPhaseFilter` is the first half of M133's `filtfilt`.

### Divergences

- **The far tail of `marcumq` has no significant figures, and three faithful implementations of
  MATLAB's own algorithm disagree there by per cent.** `marcumq(30, 40, m)` is about 1e-23, and the
  series that computes it sums terms of the form `expA(Y, m) * (1 - innerSum)` where `innerSum` is a
  Poisson tail that has already accumulated to `1 + 2e-14`. The factor `1 - innerSum` is therefore
  rounding noise, and the answer is a sum of noise multiplied by large numbers. This was checked by
  porting `marcumq.m` line for line into a third language: it agrees with neither MATLAB nor this
  engine, and the three answers differ by up to fourteen per cent. Five lines are pinned `div=` at
  `marcumq(30, 40, 1..5)`. The mid-range values agree to eleven figures and are pinned tightly.
- **`chebwin`'s edge coefficients agree with MATLAB's MEX to ten figures rather than sixteen.** The
  algorithm is reconstructed rather than read, and it inverts a transform whose values span a
  hundred decibels; the error at the window's edge is the transform's own rounding at that dynamic
  range. Pinned at `rel=1e-9` in the fixture with the reason on the line above.
- **`cceps`'s third output is not implemented.** `[xhat, nd, xhat1] = cceps(x)` rebuilds the cepstrum
  from the roots of the signal's z-transform, which is a root-finding problem rather than a
  transform and which MATLAB itself refuses when any root lies on the unit circle. Asking for it is
  refused in words.
- **`dpss`'s `'spline'` and `'linear'` forms are declined.** They interpolate from a disk cache of
  precomputed sequences; the cache is excluded by name in the coverage document and the direct
  computation is available for every length.
- **`framesig` accepts numeric arrays and not timetables.** MATLAB's returns a timetable when given
  one, with row times at each frame's mean; the timetable layer is not in this milestone's scope.
  Every numeric form, every one of its seven options and all three outputs are implemented.

## Still open

- The head-to-head timing rows for M127–M131 are still unmeasured, and M132's own `d15` rows
  (`hilbert` at 4M, `czt` at 1M) join them. The plan calls for one quiet session at the end of the
  arc.
- `dct`/`idct` at 4M are still fifteen times slower than MATLAB's. The plan put that in this
  milestone; it is a batch-transform change to `JgsBuiltins.CosineTransform.cs` rather than anything
  in the names added here, and it is deferred to the timing session, where it can be measured rather
  than asserted.
- `pulstran`'s sampled-prototype form interpolates with `linear`, `nearest`, `spline` and `pchip`.
  MATLAB's `interp1` offers more methods than those four; none of the others appear in `pulstran`'s
  own examples.
