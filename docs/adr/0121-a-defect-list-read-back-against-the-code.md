# ADR 0121 — A defect list read back against the code

## Status

Accepted (M119, 2026-08-31).

## Context

The second head-to-head report closes with sixteen defects found in JGraph, none of them looked
for: each is a call the suite had to be written around, a shape that came back wrong, or a timing
that stood far enough out of its own neighbourhood to be a mistake rather than a cost. Three were
asked for by name — the first three, all marked high — together with three things visible in the
figures the suite draws.

The first thing to do with a defect list is to re-measure it, and two of the three were already
gone.

**02, `smoothdata` grows superlinearly with the sample count.** The report has 10k at 0.114 s, 30k
at 0.409 s, 100k at 3.52 s and 300k at **102.8 s**, and says the suite had to be resized to let the
script finish. Measured now: 0.0004, 0.0005, 0.0022 and **0.0033 s**. The transform of ADR 0120
took it, and took the shape with it — thirty times the data now costs eight times the work rather
than nine hundred.

**03, the moving-window family is quadratic in the window.** The report has `movmean` over ten
million with a window of 51 at 1.76 s, `movstd` at 2.29, and `movmax` and `movmin` together at
6.88. Measured now: **0.112, 0.344, and 0.265**. And the claim the defect actually makes — that the
cost tracks the window width — is answerable in one line: a window of 501 costs **0.115 s** against
the window of 51's 0.112. The window is not in the cost at all. ADR 0118 carried it out of there.

Nothing was wrong with the report. It was written on the 30th against a build that predates both
milestones, and read three milestones later as though it were current. **A defect list is a
measurement, and a measurement has a date on it.** Two of the three highest-severity items in it
had been closed by work that never knew they were open, because the work was aimed at a benchmark
row rather than at a defect number.

That leaves one named defect and three figures — and the figures turned out to hold the deeper of
the four things fixed here.

## Decision

### `char` of a matrix of code points keeps the matrix's shape

`char(M)` read its argument in storage order from end to end and answered one long row, so a 2-by-3
of code points came back 1-by-6. The shape was lost at construction, which is why every 2-D
subscript on the answer was then refused — and refused by an error that named row-and-column
indexing as a thing it supported, because the char matrix machinery of ADR 0106 was working
perfectly on a value that had never been given a second row.

MATLAB reads each *row* of code points as a row of characters. So does this now, through the same
`JgsValue.CharMatrix` mint every other char matrix goes through, which is what makes the padding,
the shape and the tag agree by construction. A row vector still answers a plain char row, which is
MATLAB's answer and what the rest of the text machinery expects; a column vector now answers a
column.

### The point marker is drawn in the colour it has

`'.'` is the marker MATLAB scripts reach for most often, and it is the one glyph in the set with no
outline: it is not a shape with a fill inside it, it *is* its fill. Every other case in the
renderer's marker switch strokes an outline and fills only when a face colour was asked for. The
point alone painted with the fill paint unconditionally — and the paint is shared, so a series with
no face colour, which is what `plot(x, y, '.')` is, painted in whatever the previous caller had
left on it.

That was usually nothing. Three exports at marker sizes 2, 6 and the default came out **byte for
byte identical**, all three of them an empty axes with correctly computed limits and no data. The
markers were mapped, batched and handed to the canvas the whole time; only the pixels were missing,
which is why nothing that counts draw calls had ever noticed.

A marker's fill is now always given a real colour, the edge colour when no face colour was asked
for. Nothing else in the switch reads it without asking first, so nothing else moves.

### A polar curve is not reduced by a rule that only a Cartesian one obeys

A long line is drawn through a min/max reduction: the samples are bucketed by data x into device
columns and each bucket keeps its extremes, which is how a series of ten million draws in the time
a screenful of pixels deserves. It switches on above two samples per column of the plot area.

Both halves of that assume a device column stands for a range of data x and nothing else. On a
polar axes the same code was being handed the angular mapper, where x is θ — and a column of a disc
is a chord, not a slice of θ. Worse, the range of x the reduction must cover was read back through
the plot area's two bottom corners, which on a disc names the wedge those corners subtend and not
the turn. The rose in the gallery came back as the petals inside that wedge: the bottom half of the
chart, with the top half missing entirely and the grid, the rim and every label drawn correctly
around the hole. In a tall plot area the wedge narrowed to a single petal.

The precondition belongs to the mapper, so it is now stated there: `ICoordinateMapper` answers
whether one device column is a range of data x, `true` by default because a pair of rulers at right
angles is the ordinary case, and `PolarTransform` says no. This is the whole of the fix, and it is
the whole of the claim.

The threshold is why it was hard to see: 720 samples against a plot area 220 units wide reduces,
and against one 400 units wide does not. A polar chart drawn on its own looked right and the same
chart in a tile did not, so the bug read as being about tiling, which it is not.

### `ode45` answers four points for every step it takes

MATLAB's `ode45` reported 3309 points on the suite's Lorenz run where this one reported 772. The
gap is not the integration: both are Dormand–Prince 5(4) and both accept about eight hundred steps.
It is that MATLAB reports four points per accepted step and this reported one.

That is `Refine`, and it defaults to 4. The three extra points are read off the pair's own
continuous extension — a quartic per step, built from stages the step has already paid for, that
agrees with the method at both ends — so they cost no derivative evaluations at all. Its
coefficients are MATLAB's `BI`, and each of its seven rows sums to that stage's fifth-order weight,
which is what makes the polynomial meet the step's own endpoint rather than merely pass near it.

An accepted step is a coarse thing. Drawing eight hundred of them corner to corner is what made the
suite's phase-plane spirals read as polygons, which is the third of the three figure observations
and turned out to have the same cause as the step count.

Two more things came with it, because they are the same paragraph of MATLAB's method:

- **The step control is MATLAB's.** The first step is chosen from the slope at the start rather than
  from a tenth of the span; the error is measured in MATLAB's norm, against a floor of
  `atol/rtol` rather than a sum of the two tolerances; a step is grown by `0.8·(rtol/err)^(1/5)`
  capped at five, refused once by the size its own error asks for and thereafter by halving, and
  never grown at all in a step that had to be retried.
- **Named times no longer cut the step.** A caller who passes a vector of times is not asking the
  method to land on them, and clipping each step to the next request makes the integration follow
  the sampling rather than the equation. Those times are now read off the same interpolant, so
  asking for a thousand points costs the tens of steps the equation needs and not a thousand.

## Consequences

**Four of the suite's checksums move, and every one of them moves onto MATLAB.**

| | before | after | MATLAB |
| --- | ---: | ---: | ---: |
| `d08_lorenz_steps` | 772 | **3301** | 3309 |
| `d08_lorenz_zmax` | 46.37 | **47.88** | 47.88 |
| `d08_osc_err` | 0.000439 | **0.000248** | 0.000248 |
| `d08_chem_final` | 0.833308 | **0.833333** | 0.833333 |

`d08_osc_err` was one of the report's three genuine disagreements and the other three were listed
as loose agreements; all four now match to every figure the suite prints. The step count is the one
that does not close completely — 3301 against 3309 is 825 accepted steps against 827, which is the
two implementations' own arithmetic in the error norm rather than a difference of rule, and is left
as it is.

**The figures.** The gallery's rose has its eight petals. The phase planes are spirals rather than
polygons. `d03`'s phase portrait, which was an empty axes with correct limits, is the curve MATLAB
draws.

**Tests.** 6,839, none of them removed or loosened, and 69 of 69 stress scripts. The new ones were
checked against the code they were written for by putting the defect back: the polar test reports
that 181 samples arrived where 720 were sent, which is the reduction doing exactly what it was
asked to.

**The suite.** Eight of the 188 checks moved and every one moved onto MATLAB; the other 180 are
byte for byte what they were. Two of the report's nine deliberate divergence markers — the char
matrix's rows and its columns — now agree, so they are markers for a divergence that is closed. The
older eight-script suite goes from 48 of 49 to **49 of 49**.

## Divergences

- **`reshape` still refuses a char array.** Defect 12 in the same report, untouched here and still
  open. `char` of a cell and `strvcat` both build char matrices, so it is one construction route
  closed rather than a hole in the representation.
- **`ode45` takes no options.** There is no `odeset`, so `Refine`, `MaxStep`, `Events` and the rest
  are the defaults and cannot be asked for. `Refine` is now the default MATLAB uses; the others are
  absent as they were.
- **The accepted-step count differs from MATLAB's by about a quarter of a percent** on the Lorenz
  run, and the reported point count with it. Both steppers implement the same control on the same
  method; what separates them is where the last bits fall in the error norm and in the user's own
  derivative, and neither is worth pinning.
