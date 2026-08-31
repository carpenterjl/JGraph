# ADR 0119 — A window with a shape

## Status

Accepted (M117, 2026-08-31).

## Context

ADR 0118 gave the moving-window verbs a window that is carried rather than rebuilt, and it carried
`smoothdata`'s two moving-average arms along with them. It did not touch the other six, and it did
not touch `movmad`. Measured, that left the family's largest costs standing:

| | |
| --- | ---: |
| `smoothdata(x, 'sgolay')`, 100k readings, the default window | 25.5 s |
| `smoothdata(x, 'lowess')`, same | 19.7 s |
| `smoothdata(x, 'rlowess', 201)`, 10k readings | 14.6 s |
| `smoothdata(x, 'gaussian')`, same as the first | 10.2 s |
| `movmad(x, 51)`, 10M readings | 2.2 s |

The engine analysis of 2026-08-30 costed the sliding-window family at 14.77 s, and every one of
those rows is larger than that on its own. The analysis measured `smoothdata` with its default
method, which is a moving average and is now four milliseconds; it never measured the arms that
fit. Three separate things were paying for that:

- **A window rebuilt as a pair of lists per output sample**, and then copied twice more into arrays
  to be handed to the fit. Four allocations per answer, and with the default window a tenth of the
  length of the data (ADR 0066), sixteen gigabytes of them for a hundred thousand readings.
- **A normal system built by an outer product per reading**, with a fresh `double[degree + 1]` for
  the powers of that reading — an allocation per reading per window per sample.
- **A robust fit that solved its system again for every residual it measured.** Each of the three
  passes of `rlowess` and `rloess` re-centred and re-solved the whole system once per reading in
  the window to ask what the fit said there — as many systems as the window is wide, where one
  does.

## Decision

**Where the readings are evenly spaced, the window has a fixed shape, and a fixed shape is one row
of numbers rather than a fit rebuilt per point.**

### The shape is worked out once

`JGraph.Numerics.SmoothKernels` reads the window's shape once and applies it. Two facts make that
possible. A Gaussian weight depends only on how far a reading sits from the centre of its window,
so every interior window weighs its neighbours by the same numbers. And a least-squares fit read
off at one point is a *linear functional* of the values it was fitted through — so a local
polynomial smoother is also one row of weights, obtained by solving the normal matrix against the
first unit vector rather than by inverting it. Each answer is then one pass of fused
multiply-adds, applied a tile at a time so that the tile and the readings it draws on both stay in
cache while every tap is applied to them.

The kernel is a different route to the same number rather than the same arithmetic, so the last
place moves. It moves by about what a reordered sum moves by: over an exhaustive sweep of methods,
widths, endpoint rules and data shapes, the median relative gap is 2.6e-16 and the widest 1.9e-14.

### The ends are fitted, not kernelled

A kernel pays for itself when a second window is the same shape, and at the ends no two windows
are: each is cut short by a different amount. Those are fitted directly, and the normal system is
built from sums of powers rather than from an outer product per reading — every entry of the
matrix depends on its row plus its column and nothing else, so a window of width W needs 2d+1
running sums rather than (d+1)² of them.

### One fit answers everywhere

The robust passes solve once per set of weights and then read that polynomial wherever they need
it. This is the same quantity the old code computed — a fit re-centred at a reading and read at its
constant term is that fit evaluated there — for the price of one system instead of W.

### A gap is repaired, not surrendered to

`smoothdata` steps over missing readings unless told otherwise, and a window that drops one is not
the same shape as one that keeps it. Rather than hand the whole series back to the walk for a
single gap, the kernel answers the series and the windows a gap actually reached are walked
afterwards. That trade is taken while there are fewer windows to walk than the series has points,
and abandoned past it — so a series that is half missing still walks from end to end, and one with
a single gap pays for one window's worth of walking rather than all of them.

### The mean absolute deviation stops building what it measures

`movmad` is the one summary that cannot be carried, because the centre it is measured from moves
with its window. What it can stop doing is allocating that window: the statistic contract is now a
span over one buffer the walk refills rather than a `Func<double[], double>` over a fresh array per
answer. Every summary in the family went with it, so the sample-points walk — the one road the
carried window cannot take — got the same relief. **Those answers do not move at all**: the span
summaries reproduce their LINQ predecessors including the asymmetry that a maximum over doubles
steps over a missing reading and a minimum is swallowed by one.

## Consequences

**Measured, best of three runs each, on the same machine.** MATLAB is measured the same way, which
matters: its first call to `smoothdata` is thirty to seventy times its warm one, and a
single-shot comparison flatters it in one direction and JGraph in the other.

| | before | after | | MATLAB, 8 threads | MATLAB, 1 thread |
| --- | ---: | ---: | ---: | ---: | ---: |
| `gaussian` 100k, default window | 10.236 s | 0.134 s | **76×** | 0.006 s | |
| `rlowess` 10k, w201 | 14.641 s | 0.231 s | **63×** | 0.091 s | 0.537 s |
| `sgolay` 100k, default window | 25.532 s | 0.526 s | **49×** | 0.003 s | |
| `lowess` 100k, default window | 19.661 s | 0.484 s | **41×** | 0.012 s | |
| `sgolay` 100k, one missing reading | 25.182 s | 2.150 s | 12× | 0.009 s | |
| `movmad` 10M, w51 | 2.157 s | 1.436 s | 1.5× | 0.203 s | 1.284 s |

Those six rows together fall from 97.4 s to 4.96 s. **All 188 checksums the fourteen-script
head-to-head suite prints are identical to a run of the same suite built at the previous commit**,
and every one of the six rows above prints the same checksum to ten significant figures before and
after. 6,817 unit tests pass, 69 of 69 stress scripts pass, five coverage verifiers agree, no
warnings.

**What moved, and what it was measured against.** Over 947 checksummed cases spanning every method,
nine widths, four endpoint rules, missing readings kept and stepped over, sample points evenly and
unevenly spread, matrices along both dimensions and series of one, two and three readings: 832 are
unchanged and 115 moved. Every one of the moved cases is a fit or a Gaussian average; **not one
`mov*` answer moved, and neither did `isoutlier` or `filloutliers`**. Of the 115, a hundred are
last-place drift.

The other fifteen are windows holding an infinity, where the two routes disagree about which of
NaN and ±Inf comes out — a kernel with a rounding-error tap turns an infinity into a NaN where a
solved system propagates it. This was worth measuring rather than assuming, because it is the one
change here that is visible rather than merely small. Against MATLAB on the same 72 cases, the
infinity pattern **agreed 35 times before and agrees 47 times now**; of the fifteen that moved,
twelve now match MATLAB and none stopped matching. So the change is toward MATLAB, and no
divergence is added or retired by it.

**What is still on the table, and it is no longer the algorithm.** JGraph now convolves; MATLAB
does not. Measured at a million readings, MATLAB's `smoothdata` costs the same for a window of ten
thousand as for a window of one thousand — 0.089 s against 0.106 s — and the rate that implies for
a direct convolution is above what this machine can reach. It is transforming. Closing the
remaining twenty to a hundred and seventy fold means an overlap-add convolution through the FFT
that M96 already built, and that is a decision of its own rather than a consequence of this one:
`Convolution` is deliberately direct, and says so in its own doc comment, because `conv` has to
agree with MATLAB's `conv` to the bit and a transform leaves a floor of dust where a direct sum
leaves exact zeros. That reasoning is about `conv` as a verb and does not obviously carry to a
smoother whose last place has already moved — but it is an argument to have, in an ADR of its own.
The second open item is unchanged from ADR 0118: `movmad` and the window walk are one thread and
MATLAB's are not, which is the whole of `movmad`'s remaining gap (1.436 s against 1.284 s on one
thread, 0.203 s on eight).

No MATLAB-facing form changed, so this adds no divergence.
