# ADR 0345 — filter's initial and final conditions are laid out delays first, as R2025b lays them out

## Status

Accepted, 2026-11-12. Open item 279, found while ADR 0334's tests were being written.

## Context

`[y, zf] = filter(b, a, x, zi, dim)` returned `zf` with the delays along `dim`. A row signal gave a
1-by-L row, and a 4-by-5 matrix filtered along its rows gave 4-by-L. R2025b always puts the delays
first and the signal's other dimensions after them, with `dim` taken out. Both roads did it the old
way: the packed one (`PackedFilterOps`, `FilterKernels.FeedForwardAlong` and `RecurrenceAlong`) and
the boxed one (`JoinAlong` in `FilterAnswer`). `zi` was read as a flat list of exactly L numbers that
every slice started from, so a matrix of initial conditions, one line per slice, was refused.

R2025b was probed in 140 cases (probes `probe279`, `probe279b` and `probe279c`, one line per case
with 17 digits a value). L was swept at 0, 1, 2 and 3, FIR and IIR, with `a(1)` equal to 1 and 2:
- **zf** is `[L, other dimensions]`, with trailing singletons dropped and at least two dimensions:
  - `[L 1]` for a row, a column and a scalar signal;
  - `[L 4]` for a 4-by-5 along dimension 2 and `[L 5]` along dimension 1;
  - `[L 4 5]` along dimension 3;
  - `[2 2 3]` for a 2-by-3-by-4 along dimension 3, `[2 2 4]` along 2 and `[3 3 4]` along 1 (L = 3);
  - `[L 8]` for a row of 8 along dimension 1 and `[2 1 3]` for `[1 2 3]` along dimension 7;
  - `[0 1]` and `[0 4]` when L is 0;
  - `[2 0]` for `[]` and a 3-by-0 (no slices), and `[2 1]` or `[2 3]` of zeros for a 1-by-0 or a 0-by-3
    (slices of no samples).
- **zi** is accepted in three forms:
  - `[]`, 0-by-0 only, is rest;
  - a two-dimensional vector of length L, row or column, is the line every slice starts from (on a
    matrix and an N-D signal too, and a scalar when L is 1);
  - otherwise zi must have L rows. As a matrix, it then needs as many columns as there are slices (so
    a 2-by-6 serves a 2-by-3-by-4 along dimension 3). With more dimensions than two, its other
    dimensions must be exactly the signal's (2-by-2-by-3 serves that signal, 2-by-3-by-2 does not).
- **Refusals.** Every other shape is `MATLAB:filter:invalidInitialConditions`, "Initial conditions
  must be a vector of length max(length(a),length(b))-1, or an array with the leading dimension of
  size max(length(a),length(b))-1 and with remaining dimensions matching those of x." The refused
  shapes include:
  - a scalar for L = 2, a 0-by-2, and a 1-by-1-by-2 (not a vector);
  - an L-by-1-by-4 for four slices;
  - a 4-by-1 column when L is 1 and there are four slices;
  - a 0 or a 0-by-3 when L is 0.

  A char or string zi, `''` included, is `MATLAB:filter:inputType`, "Invalid data type. Input arrays
  must be numeric or logical." A logical or integer zi is read as its values. A dimension that is not
  a positive whole number is `MATLAB:getdimarg:dimensionMustBePositiveInteger`.
- **Classes.** A single anywhere among b, a, x and zi makes `zf` single as well as `y`.

The same probe showed two older faults in the shape around the filter. A scalar signal answered a
1-by-0 `y` and a 1-by-2 of zeros, because a scalar's own `Dims` read 0-by-0. A signal with no slices
(a 3-by-0 down its columns) answered a 0-by-0 `y`, because `JoinAlong` takes its length from the first
slice. A dimension of 0 reached the slicing and failed with the runtime's own index message.

## Decision

### One layout, in the kernels

`zf`'s layout makes slice `s`'s delay line the contiguous run at `s · L`, the slices in storage order.
Both kernels now write it there for every slice. A strided slice (inner > 1) used to scatter its line
by the inner stride, and writing it in one piece is no slower. `FeedForwardAlong` and
`RecurrenceAlong` take an optional `initialStride`. At 0, `initial` is one line for every slice, as
before. At L, it is one line per slice in that same layout. The filtering itself (`FeedForward`,
`FinalState` and `Recurrence`) is untouched: the same operations in the same order, the same bits.

### The shape, and reading zi

`PackedFilterOps.OtherDimensions` gives the signal's dimensions with `dim` taken out and trailing ones
dropped. `PackedFilterOps.FinalConditionsShape` puts L before them, and both roads shape `zf` with it.
`FilterInitialConditions` in `FilterAnswer` reads zi by the three forms above and returns its values
with a flag for one line per slice. The flag goes to `PackedFilterOps.TryFilter` (new parameter
`initialPerSlice`) and to the boxed loop, which copies each slice's line from `s · L`. The boxed loop
collects the final lines straight into that layout, in place of a second `JoinAlong`.

### The fixes beside it

- A scalar signal is read as 1-by-1.
- `y` is shaped as the signal itself (`ShapeAlong(dims, dim, n)`), so a road with no slices keeps a
  3-by-0 a 3-by-0.
- `FilterDimension` refuses a dimension in R2025b's words.
- The class wrapper (`CarryNumericClass`) now also stamps `filter`'s second output.

## Consequences

- Every zf shape, every accepted and refused zi shape and the error identifiers of the probe agree with
  R2025b, in the packed and the boxed lane alike. Every double value of a single call agrees to the
  last digit. One resumption differs: with `a(1) = 2`, R2025b's second half differs from its own
  unbroken run by 2.8e-17, and here by nothing. The fixture holds resumptions to 1e-15.
  The single cases differ by an ulp (see Divergences).
- A script that indexed `zf(r, :)` after filtering along dimension 2 now has to index `zf(:, r)`. A
  script that passes `zf` back as `zi` resumes as before, for a vector and now for any matrix or N-D
  signal along any dimension.
- `filtfilt`, `filtic`, `sosfilt` and `fftfilt` call `DigitalFilter` directly and never see `zf`. No
  library `.m` in the repository unpacks `zf`.
- New parity fixture `filter_conditions` (212 rows from R2025b 25.2). It covers zf's size and class
  and an order-weighted digest of its values in 48 cases, 17 refusals, and five resumptions along
  every kind of dimension.
- Kernel tests `FilterKernelsM96Tests` and `SignalTransformsI12Tests` read the final lines at `s · L`.
  `TheTransformsRefuseByName` matches R2025b's sentence.

## Divergences

- **A single filter is worked in double and rounded once.** R2025b works it in single, so `y` and `zf`
  differ in the last bit or two of a single (`zf(2)` of the probe's IIR is -0.7265625 here and
  -0.72656256 there). The class and the layout agree.
- **A complex signal, coefficient or zi is refused.** R2025b filters it, and its `zf` is complex in
  the same layout.

## Still open

- Seen while probing, and not this item's:
  - R2025b refuses a matrix `b` (`MATLAB:filter:notVectorInput`), and this build filters with its
    elements;
  - a sparse argument is `MATLAB:filter:sparseInput` there;
  - a char signal and an empty `b` or `a` are worded differently here;
  - `a(1) = 0` has its own identifier there (`MATLAB:filter:firstElementOfDenominatorFilterZero`).
- **Not measured.** `filter` is a timed row of the every-row plan (`d15_filter_iir_10M`). The kernels'
  arithmetic did not change, and the final lines are now written in one piece instead of by stride,
  but nothing was timed.
