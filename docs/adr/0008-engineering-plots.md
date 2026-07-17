# ADR 0008: Engineering plots and the signal-processing library

- Status: Accepted
- Date: 2026-07-16

## Context

Milestone 7 adds the engineering/scientific plot types that distinguish a lab tool from a general
charting library: Bode and Nyquist diagrams (control systems), polar and Smith charts (RF/microwave),
spectrograms and amplitude spectra (DSP), and eye diagrams (digital communications). These need two
things the framework did not yet have: a place for the numeric machinery (FFT, windowing, transfer
functions), and a way to draw circular coordinate systems. The constraint, as in every milestone, was
to add all of this without disturbing the load-bearing seams — the single `IRenderContext`, the
object model, and the one-way layering.

## Decision

1. **A new leaf project, `JGraph.Signal`, owns the DSP math.** It depends only on the BCL
   (`System.Numerics.Complex`) and is referenced by `JGraph.Objects`, sitting alongside `JGraph.Math`
   as a pure-computation leaf. It provides an `Fft` (iterative radix-2 for power-of-two lengths, a
   direct O(n²) DFT fallback otherwise), tapering `Window`s (Hann, Hamming, Blackman, Blackman–Harris,
   flat-top, rectangular), a single-sided amplitude `Spectrum`, a short-time-Fourier `Spectrogram`,
   and a continuous-time `TransferFunction` (H(jω) evaluation, magnitude in dB, and unwrapped phase).
   Keeping it free of any model or rendering type means the numerics are independently testable and
   reusable.

2. **The genuinely new coordinate system — polar — is expressed as a square Cartesian axes plus a
   grid decoration, not a new mapper or renderer branch.** A polar or Smith plot is drawn by
   converting its data to Cartesian *before* plotting (polar: (θ, r) → (r·cosθ, r·sinθ); Smith:
   impedance z → reflection coefficient Γ = (z − 1)/(z + 1)), so the existing `AxisTransform`,
   decimation, interaction, hit-testing, and export pipelines all apply unchanged. The circular grid
   (`PolarGrid`, `SmithGrid`) is an ordinary `IDrawable` that samples its circles through the same
   coordinate mapper. This deliberately avoids a polar-specific rendering path, which would have been
   a large new obligation on every `IRenderContext` backend.

3. **Roundness needs equal aspect, so that is the one small, general Core/renderer addition.** For a
   data circle to map to a pixel circle, one data unit must span equal pixels on both axes.
   `AxesModel.EqualAspect` (MATLAB `axis equal`) makes the renderer shrink the plot area to a centered
   square-per-unit rectangle; `AxesModel.FrameVisible` lets polar/Smith charts suppress the
   rectangular frame in favor of their own circular boundary. Both are broadly useful and required no
   new seam. No new `IRenderContext` member was added in this milestone.

4. **The remaining "plots" are compositions of existing pieces.** A Bode plot is two stacked
   subplots (magnitude and phase) on a shared logarithmic frequency axis; a Nyquist plot is a line of
   the H(jω) locus (both frequency branches) with the critical (−1, 0) point marked, on an
   equal-aspect axes; a spectrogram is an `ImagePlot` of the STFT magnitude with time/frequency
   extents. These are built by fluent helpers (`figure.AddBode`, `axes.AddNyquist`,
   `axes.AddSpectrogram`, `axes.AddPolar`, `axes.AddSmith`, `axes.AddEyeDiagram`) and mirrored on the
   `JG` facade, which takes numerator/denominator arrays so the facade never names a `JGraph.Signal`
   type. Only the eye diagram is a bespoke `PlotObject` (`EyeDiagramPlot`), because overlaying many
   signal slices is not expressible as an existing plot.

5. **Auto-scale padding is applied in scale space.** Fitting a logarithmic axis to data and then
   padding it linearly can drive the minimum to or below zero, collapsing the visible range (a latent
   bug this milestone's Bode/semilog plots exposed). Padding now expands a logarithmic axis by a
   fraction of its *decade* span, so decade ticks land correctly across the swept band.

## Consequences

- The plot roster now covers control, RF, DSP, and comms visualizations, and every one of them reuses
  the M1 seams: the only additions outside the additive `JGraph.Signal`/`JGraph.Objects` code were two
  axes properties and the equal-aspect plot-area adjustment. The rendering seam gained nothing.
- Polar and Smith charts are display-oriented: because their data is pre-converted to Cartesian, the
  interaction layer pans/zooms them as Cartesian axes rather than in native (r, θ). The linear
  engineering plots (Bode, Nyquist, spectrogram, eye) support the full interaction/export pipeline.
- The FFT's non-power-of-two path is O(n²); the spectrum and spectrogram helpers keep their frame
  sizes powers of two to stay on the fast radix-2 path. A future Bluestein/mixed-radix path can
  replace the fallback behind the same `Fft` API without affecting callers.
- The log-space padding fix improves every logarithmic plot (semilog X/Y, log-log), not just Bode.
