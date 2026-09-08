# ADR 0140 — A spectrum is one argument dance and four ways to fill it

## Status

Accepted (M136).

## Context

Sixty-five names: every way the Signal Processing Toolbox has of turning a record into a spectrum,
every measurement taken off one, the peak finder and the level measurements, the twelve bilevel
waveform names, the five ways of lining two signals up, four correlation forms, and two change
detectors.

The plan expected the spectral half to be one function under many names, and it is — but not in the
way the plan said, and the exception is the finding.

**`psdoptions` really is written once, and the four names that look like it are not.** Every modern
spectral name — `periodogram`, `pwelch`, `cpsd`, `mscohere`, `tfestimate`, the four autoregressive
estimates, the two subspace ones, `pmtm` — reads a transform length, a sample rate and a bag of
words in any order, and MATLAB writes that reading once. The four names of 1993 look like the same
thing: `csd`, `cohere`, `tfe` and `specgram` estimate the same quantities and take the same kinds of
argument. They are their own code from top to bottom. They read `(nfft, fs, window, noverlap, p,
dflag)` positionally rather than `(window, noverlap, nfft, fs)`; they default to a Hann window over
the whole transform with no overlap and a sample rate of two; they divide by the window's energy
times the segment count rather than by the sample rate; they keep a sum rather than a mean; and they
accept a detrending flag the modern names dropped. Routing them to their modern spellings would have
given a plausible number for every call and the wrong one for all of them.

**Two of the six names that look deprecated are gone rather than deprecated.** `psd` and `spectrum`
are two lines each in R2025b, and both lines are an error. Implementing them as estimators would
have been implementing something MATLAB does not have.

## Decision

### The shared reading is written once, and the estimate under it is one function

`SpectralEstimation` carries the arithmetic every non-parametric estimate is made of: the frequency
grid with its Nyquist point written exactly rather than accumulated, the windowed transform (wrapped
rather than truncated when the record is longer than the transform, and reached by the chirp-z
transform or by Goertzel when a list of frequencies is asked for instead of a length), the
periodogram, the one-sided fold, the rotation that centres zero frequency, and the chi-squared
interval. `SpectralEstimators` is the periodogram and Welch's average over that, and the cross,
coherence and transfer estimates are the same average with a second signal in it.

One rule in the interval took measuring: the degrees of freedom are truncated once, on the way in,
and never again. A real signal's zero and Nyquist bins get half the degrees the rest do, and half of
seven is three and a half, not three — which MATLAB's chi-squared inverse takes in its stride and a
second truncation turns into a five per cent error, or at one segment into a division by zero.

### The four names of 1993 are transcribed, not routed

`JgsBuiltins.SpectralLegacy` writes `csd`, `cohere`, `tfe` and `specgram` out in full, from
`psdchk`'s positional reading to the normalisation each uses. `psd` and `spectrum` are refused with
the message MATLAB refuses them with. Their one shared subtlety is that they square a *magnitude*
where the modern names add two squares — and a magnitude is a hypotenuse, so the two differ in the
last bit and the difference shows wherever a cross spectrum nearly cancels.

### The parametric estimates are four fits and one report

`AutoRegressiveModels` holds Burg's lattice, the Levinson recursion, the Yule–Walker fit over the
biased autocorrelation, and the two least-squares fits over `corrmtx`'s data matrix. The names on
top of them are M137's (`arburg`, `aryule`, `arcov`, `armcov`); the arithmetic is here because that
is where it is first needed, which is the rule M133 set. `SubspaceSpectra` is MUSIC and the
eigenvector method, split by a singular value decomposition rather than by an eigendecomposition —
which matters, because the pseudospectrum is the projection onto the noise subspace and a projection
does not care which basis a decomposition chose for it.

### A spectrum is pinned against its own peak

The fixture's rule, and the reason it has forty thousand lines rather than four hundred. A power
spectral density falls twenty decades between its peak and its floor. A bin at `1e-20` of the peak
is the difference of two nearly equal transforms and has no relative accuracy in any engine — this
one's transform is not FFTW and never will be. So every spectrum is pinned by its own scale, as
M134's coefficients are, and only counts, indices and sample positions are pinned exactly.

### `findpeaks` is transcribed, including the stack

The plan named this one, and it was right to. A peak's prominence is not its height above the
nearest minimum: it is its height above the higher of the two lowest points reachable without
crossing a taller peak, and MATLAB finds that with a single left-to-right pass over a stack of peaks
and the lowest valley under each, run twice — once forwards and once over the reversed signal.
Reimplementing it would have agreed on the easy cases and disagreed on every plateau. Two smaller
rules are in the same class: a plateau collapses to one sample before the sign of the difference is
taken, and a width measured from half a peak's own height raises the height floor to zero, because
half of a negative peak is above it.

## Consequences

Sixty-one of the milestone's sixty-five names are implemented and measured against R2025b: the
fixture pins **40,242 lines**, and every family was measured head to head before the fixture was
written rather than after.

Nothing that already existed was rewritten. The eight new files in `JGraph.Signal` sit above the
transform and the windows rather than through them, and the ten new builtin files share one argument
reader between them.

Four names are not written, and the reason is the same for all four: see the divergences below.

### Divergences

- **`pspectrum` is not written.** Its estimate is not a periodogram with different arguments: it is a
  streaming zoom estimator built on overlapping chirp-z transforms, with a Kaiser window whose length
  comes from a leakage figure, a set of sub-estimators that stride past each other, per-estimator
  flushing, and a gain that depends on how many samples each one saw. That is about nineteen hundred
  lines of stateful class in MATLAB, none of it shared with anything else in the toolbox, and
  transcribing it faithfully is a milestone's work rather than a name's. Approximating it would have
  produced a spectrum that looks right and is not MATLAB's, which is the one outcome this repository
  refuses. The name is not defined, so a script that reaches for it is told the name is unknown
  rather than handed an approximation.
- **`instfreq` and `instbw` are not written.** Their default method is the first moment of
  `pspectrum`'s spectrogram, so they rest on the name above. Their `'hilbert'` method is one line and
  would have been easy — but a name whose documented default form fails is not an implemented name,
  so both are left out whole rather than half.
- **`poctave` is not written.** Its smoothing mode is a short interpolation and would have been
  cheap; its power and spectrogram modes are not, because they design an ANSI S1.11 filter bank,
  which is its own chain of a bandpass prototype, a first-order allpass frequency transform and a
  section-by-section conversion that exists nowhere else in the toolbox. Half of a name is not a
  name, so it is left out whole and stays undefined.
- **`findsignal` searches with a fixed alignment only.** Its `'dtw'` and `'edr'` time alignments and
  its four normalisations are options this build refuses rather than approximates: the two alignments
  are compiled subsequence searches with no readable source, and a normalisation that is applied
  differently is a different search. The fixed alignment — which is the default — is exact, including
  the rule that a candidate is a local minimum found by peak-finding on the negated distance rather
  than by testing each sample against its neighbours, and the rule that naming neither cap means one
  segment.
- **`zerocrossrate` reports no crossing indices.** The third output is a three-dimensional logical
  array of which samples crossed, one plane per channel. It is built here, but MATLAB's is permuted
  from a frame layout this build lays out the other way round for a multi-channel signal, so only the
  single-channel case is known to agree and the output is not pinned.
- **The least-squares autoregressive fits do not warn when they are rank deficient.** `pcov` and
  `pmcov` on a signal made of exact sinusoids ask for more poles than the data can support, and
  MATLAB says `Rank deficient` out loud before answering. This build answers the same numbers
  quietly. The answer is the same; the warning is not there.
- **The change-point search is exact rather than pruned.** MATLAB's `findchangepts` uses PELT, which
  prunes the dynamic program it is a special case of. This build runs the dynamic program itself: the
  same objective, the same segment costs, the same minimum, and the same penalty search around it,
  but quadratic in the record's length rather than nearly linear. It agrees on every case measured,
  including all four statistics and both ways of naming how many changes to find; it will be slower
  on a long record.
- **A reassigned periodogram's centre frequencies are not centred.** `periodogram(..., 'reassigned')`
  reports the reassigned estimate and the original one exactly; its fourth output, each bin's centre
  of gravity, is de-aliased but not rotated when `'centered'` is also asked for. The two options are
  rarely used together and the quantity is meaningless in the bins where they differ — a bin with no
  energy has no centre of gravity, and both engines answer noise there.
- **`pmusic`'s eigenvector output is not pinned.** The third output is a basis for the noise
  subspace, and a basis is not unique: two decompositions that agree about the subspace need not agree
  about which vectors span it, or about their phase. The pseudospectrum, which is the projection onto
  that subspace, is pinned exactly; the vectors are not.

### Still open

- `pspectrum`, and with it `instfreq` and `instbw` — one transcription that unlocks three names.
- `poctave`, which needs the ANSI filter bank and the weighting filters.
- `findsignal`'s two warping alignments and its four normalisations.
- The single head-to-head timing session, now covering M127 through M136. A spectrum is a transform
  and an average, so the rows are `pwelch` over ten million samples with a 4096-point Hann window at
  half overlap, and `findpeaks` over ten million with a prominence — the second of which MATLAB
  writes in `.m` and this build writes in C#.
