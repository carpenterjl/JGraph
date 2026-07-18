# ADR 0024 — DSP builtins, Bluestein FFT, and the audio host seam

## Status

Accepted (M21, 2026-07-18).

## Context

The acceptance scripts call the MATLAB signal-processing surface: `fft`/`ifft`/`fftshift`/
`ifftshift`, `filter`, `firpm`, `freqz`, plus `audioread`/`sound`/`pause` on a real 26-second
guitar recording (~1.25M samples, not a power of two). `JGraph.Signal` had a radix-2 FFT with an
O(n²) fallback — hours, not seconds, at that length — and no filtering, filter design, or WAV
support. The project stays dependency-free by convention.

## Decision

1. **Bluestein chirp-z in `Fft`.** Arbitrary-length transforms are expressed as a circular
   convolution padded to a power of two and evaluated with the existing radix-2 kernel —
   O(n log n) at any length. The chirp exponent k²/2n is reduced modulo 2n in exact integer
   arithmetic so phases stay accurate for large n. The direct DFT survives only for n ≤ 32.
2. **`DigitalFilter`** — `Filter(b, a, x)` is MATLAB's Direct Form II transposed with zero initial
   state and a[0] normalization; `Freqz(b, a, count, fs)` evaluates B/A on the one-sided grid
   ω = πk/count with a matching frequency axis f = k·fs/(2·count).
3. **`IirDesign.Butterworth`** — the textbook pipeline: analog prototype poles, `tan(πW/2)`
   pre-warp, LP/HP/BP/BS band transform in s, bilinear map, complex polynomial expansion, gain
   normalization at the band's reference frequency. Band-pass/stop double the order, as in MATLAB.
4. **`FirDesign.Remez`** — Parks–McClellan on a ~16·(r+1)-point band grid with barycentric
   Chebyshev interpolation and extremal exchange. Type II (even length) designs on the transformed
   problem D/cos(ω/2) with weight W·cos(ω/2). The current extremals stay in the candidate set each
   exchange (their errors alternate ±δ by construction), which is what lets the iteration climb out
   of the near-zero-δ first iterations from a uniform initial guess. Non-convergence returns the
   best iterate and prints a console warning. Coefficients are recovered by frequency-sampling the
   designed amplitude with the linear phase attached and inverse-transforming; even symmetry is
   then enforced exactly.
5. **`WaveFile`** — a hand-rolled RIFF codec: chunk-walking reader for PCM 8/16/24/32,
   IEEE float 32/64, and the extensible format tag, mono or multi-channel averaged to mono,
   normalized to [-1, 1]; plus a 16-bit PCM writer used for playback streams and test fixtures.
6. **The audio seam mirrors figure files.** `IScriptAudio { Play(samples, rate) }` rides
   `ScriptContext` as an optional service; the app's `AppScriptAudio` renders an in-memory WAV and
   plays it with `System.Media.SoundPlayer` (non-blocking, a new call replaces the current
   playback — MATLAB `sound` semantics); tests substitute a recording fake, and a host without the
   seam fails `sound` with a clear message. `pause(seconds)` waits on the run's `CancellationToken`
   handle, so Stop interrupts it instantly (`JgsBuiltins.CreateGlobals` now receives the token).
7. **JGS surface.** Dual-registered builtins: `fft ifft fftshift ifftshift filter butter firpm
   freqz audioread sound pause mod size disp`; `freqz`/`butter`/`audioread` return destructurable
   `[a, b]` results; `zeros`/`ones` accept a size vector (`zeros(size(t))`); `xlim`/`ylim` accept
   `[min, max]`; `plot` accepts repeated `(x, y[, spec])` groups with the caller's hold state
   restored; `atan2` became elementwise. `audioread` joined the path-completion map (`.wav`).

## Consequences

- Algorithm correctness is pinned by property tests (Parseval, closed-form filter responses,
  Butterworth |H(Wn)| = 1/√2, firpm symmetry/ripple/alternation) rather than golden vectors.
- The two lab scripts run end to end headless in the test suite (a generated 0.2 s pluck stands in
  for the real recording so `pause` stays short).
- `firpm` achieves the physically attainable ripple (~26 dB for the demo's 128 taps over a 2%%
  transition), matching MATLAB — tests assert the equiripple property, not wishful attenuation.
