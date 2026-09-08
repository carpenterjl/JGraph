# ADR 0141 — A frame is the unit, and a quote mark is a conjugate

## Status

Accepted (M137).

## Context

Fifty-five names: the short-time Fourier transform and everything the toolbox builds on it, the
synchrosqueezed transform and the ridges read off it, the six spectral descriptors and the fast
kurtogram, the linear-prediction recursions and the thirteen conversions between a prediction
polynomial, its reflection coefficients and its autocorrelation, the four autoregressive fits, the
four fits that start from a response rather than a signal, the modal trio, and the vibration family
that turns a tachometer, a run-up and a load history into something a machine's owner can read.

The plan said to write `spectrogram` first because everything else leans on it, and that was right.
What it did not say is how little of the rest is a variation on it.

**Four names compute the same transform and agree about nothing else.** `spectrogram` reads its
arguments positionally through the same parser as `pwelch`, defaults to eight Hamming segments at
half overlap, keeps one side of a real signal's spectrum, and returns a power spectrogram beside
the transform. `stft` reads name–value pairs, defaults to a hundred and twenty-eight periodic Hann
samples at three-quarters overlap, keeps both sides centred, and returns the transform alone.
`fsst` fixes the overlap at one sample less than the window so that the transform is invertible,
pads the signal so that every sample gets a frame, and squeezes along frequency only. `xspectrogram`
frames both signals and then hands the frames to `cpsd`, inheriting *that* name's scaling, folding
and centring rather than the spectrogram's. One shared framing function underneath, four different
names above it, and the differences are not options — they are the names.

**The finding is a punctuation mark.** `fsst` multiplies its transform by a linear phase ramp before
it squeezes:

```matlab
inds = 0:nfft-1;
ez = exp(-1i*2*pi*m*inds/nfft)';
```

That trailing `'` is a conjugate transpose, not a transpose. `inds` is a row and `ez` must be a
column, so the quote is there to stand the vector up — and it silently reverses the ramp's
direction. The ramp therefore runs the other way from the one the expression appears to describe.

Nothing shows this for an even-length window, because `m` is then exactly half the transform length
and the ramp is plus or minus one whichever way it turns. An odd-length window is a different
matter: the estimates land in the same bins and carry different phases, and a bin that sums five of
them comes out with the right magnitude to within a percent and the wrong phase entirely. Reading
the ramp as written gave a synchrosqueezed transform that agreed with MATLAB to eleven digits for
every even window and was visibly wrong for every odd one — the sort of difference a fixture built
only from round numbers would never have found.

**`ordertrack` needs the Vold–Kalman filter only when the shaft does not.** MATLAB's help puts the
filter at the front of the order-tracking family, and the family reads as though it rests on it. It
does not. `ordertrack` uses the resampling route whenever the speed is a vector, and refuses the
filter's options in that case; the filter is reached only through a matrix of speeds, one column per
shaft, or through `orderwaveform`. That made the family separable: everything but `orderwaveform`
was written and measured before the filter existed.

**Three names rest on `pspectrum`, which M136 left unwritten.** `pentropy` and `pkurtosis` take
their time–frequency map from it; `rpmtrack`'s default method does too. Its other method is `fsst`,
which is written — but a name whose documented default fails is not an implemented name.

## Decision

### One framing, four names above it

`ShortTimeTransforms` holds MATLAB's `getSTFTColumns` and the arithmetic every name shares: the
frames and their centre times, the window's two derivatives, the two-dimensional reassignment, the
constant-overlap-add test, the overlap-add synthesis, and the two rotations that put zero frequency
in the middle. `Synchrosqueezing` holds `fsst`, `ifsst` and the Viterbi ridge search. The
MATLAB-facing layer keeps the four argument dances apart, because they are apart.

The transform of a real frame is written with its upper half as the exact conjugate of its lower
rather than computed twice. MATLAB's transform of a real vector is conjugate-symmetric to the last
bit and `istft` leans on that — an exactly symmetric column has an exactly real inverse, so a real
signal comes back real. A transform that is symmetric only to rounding leaves an imaginary residue
in every reconstruction, which is a difference a user sees.

### The recursions, and the triangle they close

`LinearPrediction` holds the two Levinson steps and everything MATLAB builds out of them: the
reverse recursion, the Schur recursion, the line spectral frequencies, and the two time-domain fits
`prony` and `stmcb`. `FrequencyResponseFit` holds `invfreqz` and `invfreqs`, which share an
equation-error solve and a Gauss–Newton refinement and differ in one thing — whether the basis is
`z⁻ᵏ` on the unit circle or `sᵏ` on the imaginary axis, written highest power first.

The four autoregressive fits were already written for M136's parametric spectra. What is new is the
argument reading, the reflection coefficients only these names return, and one arithmetic change
described below.

### A backslash is a basic solution

MATLAB's backslash on a rank-deficient least-squares problem returns a *basic* solution — at most
`rank(A)` non-zero entries, with a nought in every column the pivoting left out — and warns. It does
not return the shortest solution. `prony(h, 4, 3)` on the impulse response of a second-order system
is exactly that case: the system is rank two, and the minimum-norm answer and the basic one are
different polynomials that fit equally well. `HouseholderQr.BasicSolution` was added for it, and
`arcov` and `armcov` were moved onto it from the normal equations at the same time — squaring the
condition number had been putting their coefficients out in the ninth digit.

### `tfestimate` and `mscohere` grow the forms M136 left off

M137 needed `tfestimate(x, y, …, 'mimo')` and `'Estimator', 'H2'` for `modalfrf`, and neither was
accepted: M136 had written the single-input forms only. Both are now written, along with `cpsd`'s
MIMO form. With several inputs a transfer function is a linear system rather than a ratio, because
each output is driven by all of them at once and the inputs are generally correlated; the division
is a matrix one at every frequency.

### What is refused rather than approximated

Four options rest on code that is not in the Signal Processing Toolbox at all, and they refuse with
a message that says so: `modalfrf`'s `'subspace'` estimator and `modalfit`'s `'lsrf'` fit both call
into the Control System Toolbox, and `stftmag2sig`'s `'gd'` method needs the Deep Learning Toolbox —
MATLAB itself refuses that one without a licence.

## Consequences

Fifty-two of the milestone's fifty-five names are implemented and measured against R2025b. The
fixture pins **81,208 lines**; every family was measured head to head against MATLAB before the
fixture was written rather than after, and eleven separate probe scripts stand behind it.

The four lanes run **8,121 tests**, seventy-three of them new. The stress suite runs **84 scripts**,
and `stess_84` passes on R2025b as well as through the Release CLI — four of its twenty-six sections
were rewritten after MATLAB failed them, because the assertions were wrong rather than the engine.

Two changes reach outside the milestone. `HouseholderQr.BasicSolution` is new and is what MATLAB's
backslash does; `AutoRegressiveModels`' least squares moved onto it, which changes `pcov` and
`pmcov` in their ninth digit and is closer to MATLAB than what was there. `tfestimate`, `mscohere`
and `cpsd` gained the MIMO and estimator forms M136 documented as implemented and did not accept.

### Divergences

- **Three names rest on `pspectrum`, which is still not written.** `pentropy` and `pkurtosis` take
  their time–frequency map from it, and `rpmtrack`'s default `'stft'` method does too. ADR 0140
  records why `pspectrum` was left: it is a streaming zoom estimator of about nineteen hundred lines
  with no shared machinery, and approximating it would give a spectrum that looks right and is not
  MATLAB's. The three names are not defined, so a script that reaches for one is told the name is
  unknown. `rpmtrack`'s other method, `'fsst'`, is written and reachable through `fsst` directly, and
  its ridge tracker is the only part of the name this build lacks the map for.
- **`stftmag2sig` reconstructs by `'gla'` and `'fgla'` only.** MATLAB's third method, `'legla'`, is a
  local weighted phase update over a truncated kernel of window products — its own two hundred lines
  with a truncation order estimated from the window, and no part of it shared with the two that are
  written. Its fourth, `'gd'`, is gradient descent through the Deep Learning Toolbox, which MATLAB
  itself refuses without that licence. Both refuse here with a message that names them.
- **`stftmag2sig` run to its own stopping rule agrees to about a part in a thousand.** Griffin and
  Lim's iteration stops at a relative inconsistency of 1e-4 by default, so the answer is only defined
  to that tolerance; two engines that take slightly different step sequences over a hundred
  alternations land a few parts in a thousand apart. Through five iterations the two agree exactly.
  The fixture pins the bounded-iteration answers tightly and pins the converged one by the property
  the name promises — that the reconstruction's short-time magnitudes are the ones it was given.
- **`orderwaveform` agrees to about a part in a hundred thousand.** The Vold–Kalman filter solves a
  system with one unknown per sample per order by preconditioned conjugate gradients, stopped at a
  relative residual of 1e-3. That is the accuracy the algorithm promises, and the measured
  disagreement — worst case 1.6e-4 relative, typically 1e-5 — sits inside it. The fixture pins the
  waveforms at a thousandth of the signal's own scale, which is what the method is defined to.
- **`tachorpm`'s smooth fit differs by a part in a hundred thousand on a constant speed.** The
  least-squares cubic spline through ten knots is ill conditioned when the speed barely varies, and
  the two engines' factorizations land about 1e-5 apart in the interior of the record. Its
  `'linear'` fit agrees to the last bit, the pulse times agree exactly, and a ramping speed — which
  is what a run-up is — agrees to 1e-9. The fixture pins the ramping case.
- **`modalfit`'s complex-exponential fit agrees to about a part in a billion.** The poles come from
  a Hankel least-squares problem built out of an impulse response, and the derived quantities — the
  damping ratios, the mode shapes, the reconstructed response — inherit and amplify it. The measured
  worst case is 2e-9 relative on the reconstruction, 3e-10 on the mode shapes. The fixture pins them
  at those sizes rather than at the 1e-10 the rest of the milestone uses.
- **The no-output plots draw one axes rather than MATLAB's panels.** `spectrogram`, `fsst` and
  `strips` draw when nothing asks for their numbers, on this build's own axes: an image rather than a
  surface, and no colour bar. `rainflow`, `tsa`, `envspectrum`, `modalfrf`, `modalsd`, `tachorpm`,
  the two rpm maps and the two order names accept the call with no outputs and draw nothing, because
  their plots are stacked panels with linked axes and a two-dimensional histogram. The numbers behind
  them are the pinned ones.
- **`ordertrack` refuses the Vold–Kalman options when the speed is a vector, as MATLAB does, and
  refuses them when it is a matrix, which MATLAB does not.** The multi-shaft form needs the filter
  with one carrier per shaft and a reference index per order; `orderwaveform` has that machinery and
  `ordertrack` does not yet route to it. A single shaft — the documented common case — takes the
  resampling route in both engines and agrees exactly.

## Still open

- `pspectrum`, and with it `instfreq`, `instbw`, `pentropy`, `pkurtosis` and `rpmtrack`. Five names
  now wait on one, which is the strongest argument yet for writing it; the plan's next arc should
  budget a milestone for the estimator rather than a name for each of the five.
- `stftmag2sig`'s `'legla'`, and `ordertrack`'s multi-shaft route through the filter that
  `orderwaveform` already has.
- The head-to-head timing rows for M127–M137 are still unmeasured. `spectrogram` at ten million
  samples is the row the plan named for this milestone, and it is one batched transform per frame
  with nothing clever in it; the arc's one timing session is still owed.
