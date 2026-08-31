# ADR 0120 — A smoother measured against MATLAB

## Status

Accepted (M118, 2026-08-31).

## Context

ADR 0119 closed with one named follow-up and one named measurement. The follow-up was to apply a
smoothing kernel through the frequency domain rather than one tap at a time, which it declined to
do without an ADR of its own because `Convolution` is deliberately direct. The measurement was that
MATLAB's `smoothdata` appeared to cost the same for a window of ten thousand as for one of a
thousand, and so had to be transforming.

Both were worth revisiting, and revisiting the second is what made this milestone what it is.

**The measurement was against the wrong thing.** ADR 0119's MATLAB column was taken with the
*default* window on both engines. JGraph's automatic window is a tenth of the data (ADR 0066);
MATLAB's is a heuristic read off the data itself, and for the hundred thousand readings in that
table it chose **265**. Every "default window" row compared JGraph doing thirty-eight times the
work. The speedups in that table are real — they are JGraph against JGraph — but the MATLAB column
beside them was never a like-for-like comparison, and the conclusion drawn from it, that MATLAB
must be transforming, does not follow from it. Measured at equal windows, MATLAB is convolving
directly at close to what this machine's vector units can do.

So the first thing this milestone did was to compare the two engines on the same window, and then,
before comparing how *fast*, to compare what they **answer**. That is where it stopped being a
performance milestone.

## What comparing answers found

Nine hundred and forty-seven checksummed cases in ADR 0119 established that M117 had not moved
JGraph's answers. None of them asked whether those answers were right. Run against MATLAB across
every method, nine widths, both endpoint rules, missing readings kept and stepped over, evenly and
unevenly spread sample points, and matrices along both dimensions — **301 of 1002 cases agreed**.

Nine rules were wrong, every one of them measurable in a few lines:

1. **A Gaussian window's standard deviation is a fifth of its window, not a quarter.** Recovered
   exactly by smoothing an impulse and reading the ratio of the peak tap to its neighbour: MATLAB
   8.2 for a window of 41, JGraph 10.25.
2. **A window given as a `[before after]` pair is as wide as it *reaches*** — one less than the
   readings it covers, because the reading it is centred on is counted by neither half. A window of
   7 has a standard deviation of 1.4 and a `[3 3]` one has 1.2.
3. **A fit does not let its window shrink at the ends.** It reads the width nearest the point,
   which for every point before the first whole window is the same readings. JGraph cut the window
   short, which is what an average does. The gap this opened was not small: up to 0.3 on a scale of
   1.5, at every point in the first and last half-window.
4. **A window given as a pair was silently thrown away.** The test that picked the window out of
   the arguments admitted only scalars, so `smoothdata(x, 'movmean', [2 7])` quietly fell back to
   the automatic width — a window of two for twenty readings. Not one of the 144 two-element cases
   agreed. The second output now comes back as a pair, as MATLAB's does.
5. **A window measured as a distance is half open.** A reading exactly half a window ahead belongs
   to the next window rather than this one, which is what lets a window of even width hold as many
   readings as it is wide — and is what makes places of its own answer the same as a plain count
   when the places happen to be evenly spread. MATLAB's two forms agree exactly; JGraph's did not.
6. **A median of readings with a missing one among them is missing.** A sort over doubles puts
   every NaN in front, so reading the middle of the sorted whole answered with a real reading
   whenever fewer than half the window was missing. `median([10 20 NaN])` was 10.
7. **`movmad` is a *median* absolute deviation about the median**, not a mean one about the mean.
   The two agree on a window of one or two readings and part company on every larger one, which is
   what made it easy to miss: `movmad([1 2 3 4 100], 3)` is `[0.5 1 1 1 48]` and JGraph answered
   `[0.5 2/3 2/3 42.9 48]`. The point of the statistic is that one wild reading barely moves it,
   which a mean cannot do.
8. **A robust fit weighs every reading against the smooth of the whole series.** JGraph measured
   each window against itself, took its scale from that window's own median, ran three passes, and
   *multiplied* each pass's weights onto the last. Cleveland's rule — and MATLAB's — takes the
   residuals and their scale over the entire series, hands each reading one weight that follows it
   into every window it is read in, reads that weight afresh from the tricube each pass rather than
   piling it on, and does five reweightings. Weights multiplied together over and over only ever
   shrink, so JGraph's robust fit walked away from its readings the longer it ran; swept over the
   pass count, the accumulating form *diverges* from MATLAB where the fresh form converges to it.
9. **A window that cannot pin a polynomial of one degree will often pin a lower one.** JGraph
   retreated straight to a weighted mean. A least-squares solve of the rank-deficient system still
   passes through the readings the window can see, and dropping a degree is what that amounts to.
   Tricube weights make this ordinary rather than exotic: the outermost reading of a window carries
   a weight of exactly zero, so a window of three readings is a window of two as far as the fit is
   concerned, and a loess fit there answers the reading itself.

**Every one of these was invisible to the test suite, and for one reason.** M116 and M117 both
wrote their tests as *the fast road answers what the walk answered*. That is a real property and
worth having — it is what let M117 move six verbs without moving an answer. But a walk is only a
reference for as long as it is right, and a test that mirrors the implementation cannot tell you
that it is not. Both milestones faithfully preserved defects that had been there since the code was
written, and reported them as evidence of correctness.

## Decision

### The transform

`SmoothKernels` applies a wide kernel through the frequency domain, by **overlap-save**: the
readings are cut into blocks, each block is multiplied by the kernel's own transform, and the part
of each block that no wraparound touched is kept. Overlap-save rather than overlap-add because what
is wanted here is exactly the part of the convolution that no zero padding contributed to, so the
answers can be kept as they come out with nothing to add together afterwards.

**Two blocks ride in every transform.** A circular convolution with a real kernel is a real-linear
operation, so it leaves the two halves of a complex signal alone: put one block in the real part and
the next in the imaginary part and both come back convolved, for one forward and one inverse
transform rather than two of each. Nothing is separated afterwards and nothing is approximated by
it — the halves never mix in the first place.

The block is four times the kernel, which is close to the cheapest that shape gets: a shorter block
spends most of its transform on the overlap it has to throw away, and a longer one pays a bigger
logarithm for the room it gains.

**The crossover was measured, not guessed.** Over two million readings, the cost of the kernel
alone: at 65 taps the direct pass takes 0.016 s against the transform's 0.044; at 129 they meet at
0.031; at 2049 the direct pass takes 0.485 against 0.051. The direct curve is straight in the width
and the transformed one is a logarithm, so they cross once and never again. The threshold is 128.

**The transform is refused outright when the readings hold anything that is not finite.** A direct
pass lets a missing reading reach exactly the windows that read it, which is what `'includenan'`
asks for; a transform spreads it across the whole block it lands in, and an infinity spreads a NaN
over the same ground. That is a different answer rather than a differently rounded one.

This does not carry to `conv`. `Convolution` stays direct, for the reason its own doc comment gives:
`conv` has to agree with MATLAB's `conv` to the bit, and a transform leaves a floor of dust where a
direct sum leaves exact zeros. A smoother is a different case — its last place has already moved,
because a fit reached by solving a normal system and one reached by applying that system's answer as
a row of weights do not round identically either.

### The ends stop being the cost

Once the middle is transformed, the cut-short ends are the whole of the bill, and two changes take
them away:

- **A Gaussian's answer is one convolution from end to end.** A window that runs off the end of the
  readings is that same sum with the absent readings counted as nothing, which is exactly what a
  convolution does at its own ends. Only the divisor differs, and that is read off a running total
  of the kernel rather than summed afresh per point. A cut-short end stops growing with the square
  of the window and stops growing with it at all.
- **An unweighted fit answers a whole end with one polynomial.** The readings are the same for
  every point there and the weights are all one, so the fit does not change from point to point —
  only the place it is read at does. That is the same rule as (3) above: correcting the end window
  is what made the end cheap.

## Consequences

**Answers.** Over the same 1002 cases, agreement with MATLAB goes from **301 to 822**, and **no
case moved away from MATLAB** — every one that agreed before still agrees. The remainder is
concentrated in the robust arms at narrow windows and in unevenly spread sample points.

**Speed**, best of three on both engines, every window named explicitly, both engines warmed
before timing. 100 000 readings unless the row says otherwise.

| | M117 | M118 | | MATLAB |
| --- | ---: | ---: | ---: | ---: |
| `gaussian`, window 1001 | 0.037 s | 0.004 s | **9×** | 0.002 s |
| `gaussian`, window 10001 | 0.127 s | 0.009 s | **14×** | 0.012 s |
| `gaussian`, window 40001 | 0.564 s | 0.032 s | **18×** | 0.087 s |
| `sgolay`, window 10001 | 0.449 s | 0.009 s | **51×** | 0.006 s |
| `sgolay`, window 40001 | 5.718 s | 0.017 s | **334×** | 0.021 s |
| `lowess`, window 10001 | 0.460 s | 0.472 s | — | 0.958 s |
| `loess`, window 1001 | | 0.010 s | | 0.011 s |
| `rlowess`, 10k, window 201 | 0.231 s | 0.145 s | 1.6× | 0.076 s |
| `movmad`, 10M, window 51 | | 1.801 s | | 0.178 s |
| `movmedian`, window 21 | | 0.007 s | | 0.001 s |

JGraph is ahead of MATLAB on five of these ten rows and behind on five. Two of the changes here are
worth reading twice.

`lowess` did not get faster, and could not: its ends are a separate weighted fit for every point
even when the window is the same, because a tricube weight is measured from the point the fit is
read at. Those ends are now *wider* than they were, since they no longer shrink — that is the price
of rule (3), paid in the right direction, and it very nearly cancels what the transform won in the
middle. JGraph is still twice MATLAB's speed there.

`rlowess` got faster while doing **more** work — six fitting sweeps where it used to do four —
because rule (8) took the residual loop out of the window. The old code solved a fresh normal
system for every residual it measured, which ADR 0119 had already halved and this milestone
removes: a residual asks the same polynomial about a different place, and the robustness weights
are read off the whole series rather than the window.

`movmad` is the one row that had to be earned twice. Measured as a median about a median it is a
different and more expensive statistic than the mean about a mean it used to compute — sorting each
window twice took it to **16.6 s**. It is 1.80 s because the window is now carried in order and the
median of the distances is found by a *merge* rather than by sorting them: distances measured from
the middle of an ordered window are two ordered runs, one walking down from the middle and one
walking up, so their own middle is where those two runs meet.

**What is still open.** Threading, unchanged from ADR 0118 and 0119: the window walk and `movmad`
are one thread and MATLAB's are not. The end fits of `lowess`, `loess` and the robust arms are the
one part of this family that is still quadratic in the window, and they are embarrassingly parallel
— each point's fit reads only the readings and the trust weights, and writes only its own answer.

## Divergences

- **`smoothdata`'s automatic window is a tenth of the data length, where MATLAB derives its own
  from the data.** Unchanged from ADR 0066 and restated here because it is what made ADR 0119's
  comparison against MATLAB unlike-for-unlike: for a hundred thousand readings JGraph chooses 10000
  and MATLAB chooses 265. Any timing comparison of this family must name its window explicitly.
- **At the ends of *unevenly* spread sample points, a fit's window is cut short rather than slid
  back inside the readings.** MATLAB slides it for some window widths and not others, and the rule
  separating the two was not established; sliding unconditionally moved two measured cases away
  from MATLAB, so the slide is applied only where it is verified — to evenly spread places, where
  MATLAB answers a plain count exactly. Measured on one uneven series, the remaining gap is about
  1.6e-2 on a scale of 1, against 1.3e-1 before this milestone.
- **The robust arms still differ from MATLAB at narrow windows.** With the global scale, the fresh
  tricube base and five passes in place, `rlowess` and `rloess` agree with MATLAB to 1e-15 on the
  cases exercised at moderate windows, and disagree on windows of three to eleven readings where
  the tricube zeroes most of the window. 103 of the 180 remaining disagreements are theirs.
