# 0104 — An outlier is a reading outside fences the data itself placed

Date: 2026-08-29 · Milestone: **M103** · Status: accepted

## Context

The everyday-gaps arc (ADR 0100) reaches the data-cleaning row: fourteen counted names from
MATLAB's `datafun` folder — `isoutlier` `rmoutliers` `filloutliers` `ischange` `findgroups`
`splitapply` `standardizeMissing` `subspace` `detrend` `del2` `filter2` `histcounts2` `xcorr`
`xcov` — plus ten younger siblings that post-date the R2021b dump and so move no counted total:
`groupcounts` `grouptransform` `groupfilter` `head` `tail` `topkrows` `clip` `isuniform` `rmse`
`mape`.

Little of this family is specified anywhere. The documentation names the outlier methods but not
which round of an iterated statistical test the fences come from; it says `ischange` "finds abrupt
changes" but not what a change costs; it gives `detrend` breakpoints but not the model that joins
the segments. All of it was measured against MATLAB R2024a before anything was written, in five
probe scripts totalling 385 printed comparison lines.

## Decision

**The three outlier verbs are one scan.** A private `OutlierFences` answers, per slice, where the
lower and upper fences stand and what the center is; `isoutlier` reports the verdict,
`rmoutliers` deletes along the slice direction, and `filloutliers` writes replacements — so the
three cannot disagree about which reading is out. The fences were measured, not read:

- `'median'` centers on the median with three scaled MADs (`−1/(√2·erfcinv(3/2))`, hard-coded as
  the double `1.4826022185056018`); `'mean'` on the mean with three standard deviations.
- `'quartiles'` and `'percentiles'` center on the **midpoint of their anchors**, not the median:
  `isoutlier([1 2 100 3 4], 'quartiles')` centers on 14.875 in MATLAB, which no median of that
  data is.
- **Grubbs reports the fences of its final survivors** (center 59.077 = the mean with both
  outliers already gone), while **the generalized ESD reports the round that flagged its last
  outlier**, computed with the earlier rounds' outliers removed (center 62 = 868/14, one gone).
  Both critical values come off `ContinuousDistributions.TInv` with MATLAB's own α splits.
- The moving methods ride M66's `Slide` machinery with shrink endpoints, `omitnan` statistics,
  and `SamplePoints` spans — the same window arithmetic `movmedian` answers with.

**A change point is a segmentation priced by a penalty**, computed once in
`JGraph.Numerics.ChangePoints` for all three methods: segment costs are squared residuals about
the mean, the Gaussian log-likelihood of the MLE variance (floored at `double.Epsilon`, which
cancels across segmentations because sample counts always sum to n), or squared residuals about
the least-squares line in the sample points. The exact dynamic programme runs over prefix sums;
minimum segment length is one for the mean and two for the other two. Two tie rules were
calibrated on vectors where two segmentations cost exactly the same: **a penalty-driven search
keeps the earliest split, a change budget keeps the latest** — and a budget is not an instruction
to spend, because among the counts it allows, the smallest count whose residual no larger count
improves on wins (`MaxNumChanges', 2` on a signal one cut explains takes one cut).

**`detrend`'s segmented model is one least squares over hinge powers.** With breakpoints it fits
the polynomial of the asked degree plus, per breakpoint, `(t−b)₊¹ … (t−b)₊ⁿ` — continuity of
value and nothing else, verified to 4·10⁻¹⁵ against R2024a for degree two. Degree zero leaves the
hinges empty, and R2024a then subtracts the **mean of the readings up to and including the first
breakpoint** from everything — measured twice (bp 2 averages the first two samples, bp 3 the
first three) and reproduced deliberately. `'Continuous', false` fits each segment its own
polynomial through the shared LAPACK least-squares path, with an SVD fallback for a degenerate
design.

**`del2`'s boundary is a linear extrapolation, in the coordinates, of the two nearest interior
second differences** — established on a non-uniform grid, where the uniform-grid reading "cubic
extrapolation" stops being a distinguishable description. A uniform grid takes the classic
three-point difference so the bits are MATLAB's; spacing arguments follow `gradient`'s convention
(the first names the columns), and the total divides by `2·ndims`.

**`xcorr` and `xcov` go through the same transform kernels as `fft`, at MATLAB's own length** —
two to the power that covers `2N−1` — because MATLAB's answer visibly carries FFT roundoff
(`xcorr([1 2 3])` ends in `3.0000000000000004` there) and matching it means rounding the same
way, not computing the "better" direct sum. Orientation mirrors the input, a matrix answers every
ordered pair of its columns, a scalar in a numeric slot is a maxlag, and the three scales divide
by the longer length, the per-lag overlap, and the directly-summed zero-lag energies.

**A group is a number.** `findgroups` sorts distinct values as values — numbers numerically,
text ordinally — numbers them from one, and gives missing readings a NaN group; several grouping
variables group as tuples ordered variable by variable. `splitapply` mirrors its first input's
orientation (a row hands rows and joins sideways, a column hands columns and stacks, a matrix
hands each group its rows) and refuses an answer that is wider than one column across the join —
measured: MATLAB raises `OutputNotUniform` for a row answer from a row's group even when every
group's answer would have concatenated. `groupcounts` counts missing readings as their own last
group, unlike `findgroups`, which drops them — both measured.

**`histcounts2` is the one-dimensional chooser with the fourth root.** The automatic rules defer
to `Binning.EdgesFor` with a new sample-root parameter (Scott's denominator becomes n^¼, because
the same readings spread over bins in two directions at once — verified on data where the cube
and fourth roots choose different widths). An asked bin count picks the **nice width the
automatic table would** (1, 2, 3, 5 or 10 times a power of ten: three bins over [0.1, 0.9] are
3×0.1 wide starting at zero, which is why MATLAB's edges end in `…013`), snaps the left edge down
onto that width's grid, and stretches the width to the next tenth of the power of ten only when
the asked count fails to reach the data (four bins are 0.23 wide, not 0.225). A pair with either
coordinate outside its edges is outside the grid, and both of its bin numbers say zero.

Error identifiers follow ADR 0100's amendment: two dozen documented identifiers are raised —
`MATLAB:splitapply:MissingGroupNums`, `MATLAB:isoutlier:MissingWindowLength`,
`MATLAB:detrend:KeyWithoutValue`, `MATLAB:ischange:MethodInvalid`, `MATLAB:clip:InvalidLowerBound`
and their siblings — with MATLAB's own messages where a probe captured them.

### Two defects found beside the road

- **`conv2(A, B, 'same')` cropped the wrong centre for an even-sized kernel** — the crop started
  at `floor((k−1)/2)` where MATLAB starts at `floor(k/2)`, so every even kernel's answer was
  shifted one row and column since the shape was written. Found because `filter2` is conv2 with
  the kernel turned half a turn, and no existing test used an even kernel.
- **`Filters.Filter(…, convolve: true)` anchored the flipped kernel at `k/2`**, tuned to agree
  with the shifted conv2 above; MATLAB's `imfilter(…, 'conv')` agrees with the *correct* conv2.
  Both modes now anchor at `(k−1)/2` — the flip itself is what moves an even kernel's centre —
  and the test that pinned their mutual agreement now pins agreement with MATLAB too.

### Divergences recorded here

- **`xcorr` and `xcov` carry this build's FFT bits, not MKL's** — `xcorr([1 2 3])` is exactly
  `[3 8 14 8 3]` here where MATLAB's own transform leaves `3.0000000000000004` at the ends, and
  the residue at impossible lags lands on different elements; every difference is within
  `5·10⁻¹⁵` of the row's scale. The same class as ADR 0101's `eig` rows.
- **A least-squares trend's last bits belong to the solver** — `detrend`'s fits differ from
  MATLAB's in the final bits, and `ischange('linear')` answers the slope of a perfect line
  exactly (2 where MATLAB's own fit says `1.9999999999999996`); `'Continuous', false` leaves
  exact zeros where MATLAB leaves `10⁻¹⁵` dust.
- **`conv2` accumulates in a different order** — `filter2(ones(3)/9, magic(4))` differs from
  MATLAB in the last bits of sums that are associativity-sensitive, and `del2` with two distinct
  scalar spacings sits one ulp away on two entries.
- **`groupcounts` here takes a column of values, or a table** — MATLAB groups whole rows for any
  other shape, so a row vector answers one group of count one with its values spread across a
  cell; that degenerate answer is refused by name rather than reproduced.
- **A row vector handed to `grouptransform` groups like a column** — MATLAB treats each of its
  columns as a one-row variable, so `'meancenter'` answers all zeros and `'zscore'` all NaN;
  here the row transforms the way its transpose would and keeps its shape.
- **`findgroups` over a char matrix errors without MATLAB's identifier**
  (`MATLAB:findgroups:InputSizeMismatch` there; a plain refusal here), and four error messages
  differ in wording while their identifiers match — `splitapply`'s failure wrapper names the
  group generically, and the outlier verbs' `invalidType` texts are one sentence, not three.

## What this did not close

- **`hist`**, the last `datafun` name — the legacy histogram, a graphics wrapper the plan never
  commissioned for this milestone; `datafun` stands at 40 of 41.
- **Tables inside the cleaning verbs**: `isoutlier`/`ischange`/`detrend` refuse `DataVariables`
  by name; the grouping family answers tables in full.
- **Vector dimension lists** in `rmse`/`mape` (`vecdim`); a scalar dimension and `'all'` answer.
- **Anonymous `varargin`** surfaced as a gap while building the prober's `splitapply` sample —
  `@(varargin) …` parses as one parameter named `varargin` — and is spawned as its own task
  rather than patched here.

## Consequences

- `JGraph.Numerics` gains `ChangePoints`; the scripting layer gains three partials (`Cleaning`,
  `Grouping`, `DataTrends`) and 24 catalogued names. `Binning.EdgesFor` learns a sample-root
  parameter; nothing else about the 1-D chooser moved.
- The `datafun` folder is one name from closed; the toolbox count moves 224 of 377 names and 349
  of 1,036 accepted forms (59 of M103's 65 accepted, the other six being `Name,Value` rows the
  dump carries no pair names for — the same category every earlier milestone's `Name,Value` rows
  sit in).

## Measured

Five probe scripts, 385 printed comparison lines against MATLAB R2024a on this machine:
**297 identical, 38 within 5·10⁻¹⁵ of the row's scale, 4 same-identifier message differences,
41 table-display lines** (JGraph's compact table rendering predates this milestone),
**2 random-stream lines** (`randn` draws differ by stream, not rule), and **3 lines that are the
deliberate divergences above**. All ten documented error identifiers probed came back exact.

## Testing

- `tests/JGraph.Tests/Scripting/MatlabDataCleaningM103Tests.cs` — 67 tests, every number read
  off R2024a; defining properties asserted where one exists (the three verbs agreeing on one
  scan, a budget not spent, exactness on quadratics).
- Full suite: 6,172 tests, 0 warnings; the five coverage verifiers exit 0.
- `stess_63.m` (25 checks) passes 25/25 here and 21/25 in MATLAB — the four failures there are
  items 20–23, which assert this build's side of the recorded divergences.

## Live checks for the user

```matlab
x = [57 59 60 100 59 58 57 58 300 61 62 60 62 58 57];
[tf, L, U, C] = isoutlier(x, 'gesd');    % flags 100 and 300; C is 62, the second round's mean
plot(x); yline(L); yline(U);
[tf, S1] = ischange([1 1 1 5 5 5 9 9 9], 'MaxNumChanges', 5);  % takes 2 cuts, not 5
G = findgroups({'b','a','b','c'});
splitapply(@mean, [10 20 30 40], G)      % [25 10 40] — sorted group order
```
