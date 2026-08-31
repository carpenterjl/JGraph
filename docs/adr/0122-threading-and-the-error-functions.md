# ADR 0122 — Threading, and an inverse that was being bisected

## Status

Accepted (M120, 2026-08-31).

## Context

The second head-to-head report lists sixteen defects, and the fifth is that `erfinv` over a million
arguments costs 1.428 s against MATLAB's 0.010 — a hundred and forty-three times. The same entry
notes that `erf` and `erfc` are fifteen and ten times slower over five million, so the inverse
stands out even against its own family.

Alongside it, the report's timings show ninety-two operations where JGraph is slower than MATLAB,
totalling 11.9 s of the suite. Some of that is algorithmic and some of it is that the work is being
done on one core. Both are addressed here, and they turn out to be the same paragraph twice: the
question in each case is not how to make a loop faster but whether the loop should be running at
all.

## Decision

### The error functions are approximated rather than iterated

Everything in `SpecialFunctions` is written on two workhorses — a Lanczos log-gamma and a
modified-Lentz continued fraction — so that accuracy is a property of two pieces of code instead of
fifteen. That is the right trade for the gamma family and for the incomplete integrals, and it was
the wrong one for the error functions, which are the only family in the set a script calls a few
million times in a row.

`erfc` was Q(½, x²) — thirty divisions of a continued fraction driven to 1e-15 — where the same
answer is a rational in fifteen flops. `erfinv` was worse: a bisection of `erf` over [-6, 6] to
adjacent doubles, which is sixty evaluations of that continued fraction for every element. The
comment above it says why, and the reasoning was sound: *there is no starting guess for a badly
conditioned case to spoil*. What it did not weigh is that sixty of a thing that costs thirty
divisions is eighteen hundred divisions per element.

The forward functions are now W. J. Cody's rational Chebyshev approximations, which is what most
libraries' `erf` is: three intervals, a rational in x² below 0.46875, one in x up to 4, one in 1/x²
past it. The published coefficients were checked against a C library's own `erf` over forty
thousand points *before* they were written into the source — worst relative disagreement 4.6e-16
for `erf` and 9.3e-16 for `erfc`.

The inverses are a fitted first guess finished by one Halley step. The guess is a polynomial this
repository fitted for itself rather than one lifted from a paper, which is a deliberate choice: a
coefficient recalled slightly wrong is a defect that looks like an approximation, and one derived
here can be checked against the data it was derived from. Degree 14 in p² near the middle and degree
14 in 1/√(−ln q) down the tail, worst relative error 3.6e-8 and 7.7e-8 measured on a dense grid
rather than on the nodes they were fitted at. Halley triples the digits, so one step carries either
guess past what a double holds with room to spare.

Two things follow from the refinement that are worth stating as design and not as detail:

- **`erfinv` is the inverse of this library's own `erf`.** The Halley step calls `Erf`, so the two
  cannot drift apart, and the round trip is a test with no reference in it.
- **The tail refines on the logarithm.** `−y² + ln erfcx(y) − ln q` never forms an underflowed
  `erfc` or an overflowed `exp(y²)`, which is what lets the same two lines answer q = 1e-300 and
  q = 0.1.

Cody's underflow limit was also moved. His is 26.543, where `erfc` leaves the *normal* doubles;
that was right on a machine that flushed everything below to zero and it is wrong on one with
gradual underflow, where `erfc(27)` is 5.24e-319 and MATLAB answers it. Applying the exponential in
two halves, with the rational folded between them, reaches those last few hundred arguments with a
single rounding into the subnormals instead of two.

### A median is not a sort

`median` and `prctile` each sorted the whole sample. Over ten million that is 0.70 s apiece, of
which the sort is the larger half and three copies of the data are the rest — one to read the packed
buffer, one to filter NaN through a `List<double>` an element at a time, one to hand that back as an
array, and one more to clone before sorting.

A median is one order statistic and a quartile is a pair of neighbouring ones. `SelectKernels`
places the ranks a caller names and leaves everything else in whatever order the partitioning
produced. Several ranks are served by one recursion rather than one each, so `prctile(x, [25 75])`
costs less than two medians. The recursion is budgeted the way a library sort's is; a range that
exhausts its budget is sorted outright, which satisfies every rank left in it.

The input shapes the kernel is tested on were not guessed at. The partitions each one causes were
counted first, which is how the two that exhaust the budget were found — and how several that were
assumed to and do not were ruled out. The first draft of that test asserted that an organ-pipe
input would make median-of-three walk the array one element at a time; disabling the budget
entirely left it passing in 38 ms, and the claim was wrong.

### Four hot loops now run on more than one core

Threading in this repository has one rule, from ADR 0093: a threaded kernel answers what the serial
one answers, to the bit. Each of these was taken only as far as that rule allows.

- **`interp1`.** All five of its methods cost the same to a hundredth of a second over two million
  points, which is the tell: what was being measured was the `switch` over the method's *spelling*,
  once per query point, and not the arithmetic. The rule is settled once per set, and the query loop
  is split across cores. Nothing about the answer depends on the split — each point is independent.
- **`interp2`, `interp3` and `interpn`.** One grid reader serves all three, so all three
  move together. Same shape as `interp1`, with one wrinkle: the sampler keeps its working indices
  and weights in fields, so a grain needs a sampler of its own. What it does not need is a second
  copy of the grid or of the spline slopes, so `ForAnotherThread` shares those and copies only the
  scratch.
- **The moving-window family.** This one is not independent, and it is the interesting case. The
  window is carried rather than rebuilt (ADR 0118), as two folds in a queue that never subtracts —
  and *where* that queue splits the window depends on how many values have left since it last
  turned itself over. Start a block anywhere and it splits the same window in a different place,
  which for a sum is the same additions in a different order and a different last bit. So a block
  resumes only where the queue would have turned over anyway: it walks one output earlier than such
  a point and reports from the point itself, beginning with the same window in the same two stacks
  split in the same place. The one output it walks and does not report is the price.
- **`hess`.** Not threading at all, in the end. The reflector was applied a column at a time, and a
  `double[,]` is stored a row at a time — so walking a column is a stride of n doubles per step: at
  400 square, a cache line fetched and one number used out of it, four hundred times per column,
  four hundred columns, four hundred reflectors. Accumulating a whole row of dot products at once
  reads each of those lines once and uses all of it.

## Consequences

**Nothing moved.** All 188 of the suite's checksums are what they were, and so are all 49 of the
older suite's. That is the measure this milestone is held to: every change here is to how long an
answer takes and not to what it is.

**The named defect.** `erfinv` over a million arguments: **1.508 s to 0.020 s**, against MATLAB's
0.011. `erf` over five million 0.374 to 0.070 and `erfc` 0.308 to 0.062, against 0.022 and 0.020.
Measured against MATLAB over fourteen thousand arguments spanning every interval boundary of the
rational and both arms of the inverse, the worst disagreements are 1.5 ulp for `erf`, 2.1 for
`erfinv`, 2.1 for `erfcinv` and 3.3 for `erfcx`; of twenty-one subnormal answers, none differs by
more than one unit in the last place.

**The rest of the gap.**

| | before | after | MATLAB |
| --- | ---: | ---: | ---: |
| `d11_stats_10M` | 2.224 | **0.281** | 0.469 |
| `d13_erfinv_1M` | 1.508 | **0.020** | 0.011 |
| `d09_interp1` (five methods) | 1.029 | **0.201** | 0.043 |
| `d09_interp2` (two methods) | 0.687 | **0.314** | 0.179 |
| `d11_movstd_10M_w51` | 0.436 | **0.145** | 0.022 |
| `d10_hess_400` | 0.350 | **0.196** | 0.005 |

Summed over every operation the suite times, the work JGraph does that MATLAB does faster falls
from **11.9 s to 5.9 s**, and the suite's own in-script total from 23.6 s to 17.7 s against MATLAB's
49.3 s. `d11_datafun` was the one script of fourteen that JGraph lost — 6.489 s against 5.536 — and
it now takes 4.064 s. **JGraph is ahead on all fourteen.**

**Tests.** 6,917, up from 6,839, and 69 of 69 stress scripts. The threading tests are written as bit
equality rather than as a tolerance, because a tolerance would pass the bug they exist to catch —
and each was checked by putting that bug back: shifting the moving-window resumption by one output
fails them with a last-bit disagreement, which is exactly the symptom predicted.

## Divergences

None. Every change here is to how long an answer takes, and the 188 checksums say so. One
divergence is *closed* rather than opened: `erfc` now answers the few hundred arguments between
26.5 and 27.3 where the result is a subnormal, which MATLAB answers and this did not.

## Still open

Neither of these is a difference in what JGraph answers, so neither belongs in the list above — but
both are places this milestone reached and stopped.

- **`hess` is still not on the LAPACK path.** Defect 10 in the same report, and the factor of
  seventy it names is now a factor of thirty-nine. What remains is the difference between a managed
  rank-one update and a blocked reduction calling BLAS-3, and closing it means `dgehrd` and
  `dorghr` through the native seam rather than anything else to do here.
- **`interp1` is 4.7 times MATLAB rather than 24.** The remainder is a binary search per query
  point against MATLAB's own gridded interpolant, which exploits a uniform grid. That is a
  different algorithm, not a threading question.
