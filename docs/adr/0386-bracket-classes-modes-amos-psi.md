# ADR 0386 — The bracket's class and N-D rules, num2cell, mode and a complex median, cell2mat of an N-D cell, complex unique, zero signs of complex products, and psi by Amos's algorithm

## Status

Accepted, 2026-11-16. Open item 346 but its and/or/xor, kron, sparse('ab'), datestr and v7.3
single parts (lanes 8G, 8B, 8D), and open item 349 but its complex Cholesky and near-singular
complex pivot (rule 18 of the wave: they need the four-lane run) (chips wave 8, lane 8C).

## Context

Every expectation below was measured in R2025b 25.2 headless (`-noFigureWindows -batch`), with
probes `probe_cat`, `probe_cat2`, `probe_msg`, `probe_misc`, `probe_349`, `probe_mul`,
`probe_pow`, `probe_rpow`, `probe_psi` and `probe_psi2` in lane 8C's scratch, one line per case in
both engines and diffed (about 9,000 lines and 5,184 products and 3,652 psi values compared by
their bits).

- **The bracket's class.** Over sixteen values (a char, a logical, a double, a single, int8,
  int16, uint8, int64, uint64, a complex double and single, a complex int8, a flagged double, `''`,
  `[]` and an empty int16), every ordered pair under `[a b]`, `[a; b]` and `cat(3, a, b)`: a char
  wins, but a logical beside one is `MATLAB:UnableToConvert` ("Conversion to char from logical is
  not possible."), and so is a complex value, flagged ones too ("Complex values cannot be converted
  to chars", naming the complex piece's class), `''` and `[]` included; otherwise the first integer
  class in reading order wins, empties included (`[int8(1) int16(300)]` is an int8 127,
  `[zeros(0,0,'int16') int8(3)]` an int16 3), where here two integer classes were refused; then
  single; then logical only when every piece is (`[[] true]` is a double 1, here a logical); then
  double. A number becomes a char by truncation clamped to 0 and 65535, NaN as 0 (`['a' -1]` is
  `char(0)`, here a wrapped 65535). `['a'; 2]` joined here as a double. A flagged piece keeps the
  join complex only when its class is the join's (`[complex(4, 0) true]` is complex,
  `[complex(4, 0) single(1)]` a real single, `[complex(single(4), 0) 2.5]` a complex single; here
  the first was real and the second complex).
- **The bracket's shape.** `[ones(2,2,2), ones(2,1,2)]` was 2-by-6 here (R2025b 2-by-3-by-2): every
  bracket and `cat(1|2, ...)` with an N-D piece flattened it. R2025b joins 2-D pieces along the
  first two dimensions leaving out every empty piece, but a join with an N-D piece, or along a third
  or later dimension, leaves out only a 0-by-0 piece (unless all are: `cat(3, [], [])` is
  0-by-0-by-2) and refuses any other disagreement (`[ones(2,2,2), zeros(1,0)]`,
  `[ones(1,1,2) 5]`, `cat(3, ones(2), zeros(1,0))` are `MATLAB:catenate:dimensionMismatch`).
  `cat(3, ...)` here refused char and complex pieces outright and read `cat(3, true, [])` as a
  logical.
- **cat's dimension.** `cat(0|1.5|-1|[1 2]|'a'|true|NaN|complex(3,0)|3+1i, ...)` are R2025b's
  `MATLAB:catenate:invalidDimension`, `cat(Inf|1e10, ...)` its `MATLAB:catenate:maxNDims`,
  `cat()` its `MATLAB:minrhs` and `cat(3)` a 0-by-0; here they answered or refused in JGraph's words.
- **num2cell** (R2025b's num2cell.m): `num2cell(single([1 2]))` held doubles here; `num2cell(A, dims)`
  was `MATLAB:maxrhs`. R2025b keeps each element's class, wraps a cell's elements in cells, and with
  `dims` permutes them to the front and cuts blocks of their size (`num2cell(A, [2 1])` holds `A.'`),
  refusing a bad list as `MATLAB:badsubscript`, `MATLAB:permute:repeatedIndex`,
  `MATLAB:permute:tooFewIndices` or `MATLAB:permute:badIndexType`.
- **mode** (R2025b's mode.m): `[m, f] = mode(x)` was `MATLAB:maxlhs` here. R2025b answers the most
  frequent value (smallest on a tie, a NaN equal to no other), its count as a double, and a cell of
  every value with that count, a sorted column each; an empty answers NaN (0 for an integer or
  logical), 0 and empties (`mode([])`'s cell holds a 0-by-1, `mode(zeros(0,3))`'s 1-by-0s); a char,
  a logical, an integer and a complex array keep their class; a fourth output is
  `MATLAB:TooManyOutputs`.
- **median of a complex array** was refused. R2025b's median.m sorts it by magnitude and then
  angle and averages the middle two by its `meanof`; a NaN answers NaN (real on the vector road),
  `'omitnan'` skips it, an empty answers a flagged NaN.
- **cell2mat of an N-D cell** flattened it into a row; R2025b's cell2mat.m joins from the last
  dimension back to the second by `cat` along each, then stacks the column left.
- **unique of a complex array** failed here ("Failed to compare two elements in the array"), with
  `'rows'` or without. R2025b orders the values (the rows column by column) by magnitude and then
  angle, a NaN equal to nothing.
- **The sign of a zero part of a complex product.** R2025b's 5,184 products over parts
  {0, -0, 1, -1, Inf, -Inf, NaN, 2} (one side possibly real) all follow: two factors with no
  imaginary part are the real product of the real parts; two finite factors are the plain
  (ac - bd) + (ad + bc)i, a real factor read as x + 0i (`1 * complex(-0, -1)` is `0 - 1i`, here
  `-0 - 1i`); anything with an infinite or NaN part, or an overflow, is ADR 0377's rule. Also
  `complex(1, -0)` and `complex([1 2], [-0 -0])` lost the -0 here, and `imag` of a flagged array
  answered +0.
- **`power(x, 1)` through an anonymous function**: `@(z) power(z, 1)` keeps the flag in both
  engines and `@(z, p) power(z, p)` with 1 drops it in R2025b and keeps it here — ADR 0377's
  `pow_power_fn_var` divergence, unchanged. The item's "drops the flag in both engines" was the
  operator form `@(z, p) z .^ p`, where both drop it, as they should.
- **A flagged base to a whole power.** `complex(1.1, 0) .^ n` is R2025b's real power of 1.1, not
  square-and-multiply: over 45 bases and 30 exponents (`probe_rpow`), the flagged and the real road
  agree bit for bit in R2025b, and R2025b's real power differs from the correctly rounded value by
  one ulp in 93 of 1,134 whole powers (`1.1 .^ 4` is `1.4641000000000006`, the truth
  `1.46410000000000047...` rounds to `1.4641000000000004`, which .NET's `Math.Pow` and this engine
  answer).
- **psi.** R2025b's psi is Amos's DPSIFN (ACM TOMS 610). This engine's digamma (ADR 0377's seven
  terms) and its Hurwitz-zeta polygamma agreed with R2025b to the bit on 516 of 2,430 values.

## Decision

### One engine for numbers, logicals and text (`JgsBuiltins.MatlabConcat.cs`)

`TryMatlabBracketJoin` reads each piece (`TryMatlabCatSignature`, `ReadMatlabCatPiece`: kind,
class, dims, real and imaginary planes, whether the imaginary parts are all zero), decides the
class by the rule above (`TryMatlabCatClass`), converts every piece into it
(`ConvertMatlabCatPieces`), joins each bracket row along 2 and the rows along 1
(`JoinMatlabCatPieces`, 2-D or strict N-D rules), and builds the value (`MatlabCatValue`: a char row
or matrix, a logical, a classed number, boxed complex elements as the literal road builds them, or a
flagged planar array). The bracket asks it (`Interpreter.BuildArrayLiteral` and
`Interpreter.AssembleMatrix`, one early `if` each, MATLAB code only) only when the join has an N-D
piece, pieces of more than one kind (class, logical, char, complex or flagged), or stacked char rows
with an empty among them; a homogeneous join keeps the old road and its speed. A complex piece
joining an integer class is left to the old road (there are no complex integers here).
`cat(dim > 2, ...)` in MATLAB code goes to `TryMatlabCatAlong` (strict rules) before the numeric
verb, and the `cat` wrapper checks the dimension first (`MatlabCatDimensionRefusal`).

### num2cell, mode and a complex median (`JgsBuiltins.SlicesAndModes.cs`)

`RegisterSlicesAndModes` re-registers `num2cell`, `mode`, `median`, `unique` and `imag` through
`WrapMatlabVerb` (MATLAB code asks the new answer first; null hands the call on), after
`RegisterBracketVerbs` and before `RegisterR2025bRefusals`. `MatlabNum2Cell` is num2cell.m over a
number, logical, char, cell, string or (with dims) struct array; `MatlabMode` is mode.m with all
three outputs over `ReductionGroups`; `MatlabComplexMedian` is median.m's sorted road and `meanof`
for a complex array (weights keep the old road).

### cell2mat of an N-D cell (`Interpreter.NdCellContentsJoined`, `JoinAlongDimension`)

`CellContentsJoined` hands an N-D cell to cell2mat.m's loop; the cell2mat wrapper admits an N-D
cell whose pieces the engine joins (`IsMatlabCatPlain`). A struct or object N-D cell keeps its road.

### unique of a complex array (`MatlabUniqueComplexRows`)

With `'rows'` the rows of a complex matrix, without it every element as a row of one, are ordered
by magnitude then angle (-0 read as 0, NaN last), equal ones kept once; `'stable'`, `'last'`, the
index outputs, and a row vector's answer a row.

### Complex products (`JgsBuiltins.MultiplyMatlab`)

Two factors with no imaginary part: the real product; two finite factors: the plain formula, unless
it overflows to a NaN; everything else ADR 0377's rule. `complex(x, -0)` keeps the -0
(`ZipComplex`), and MATLAB code's `imag` of a flagged array with a -0 reads its plane.

### psi (`JgsBuiltins.AmosPsi.cs`)

MATLAB code's `psi(k, x)` for x &gt; 0 is `AmosPolygamma`, a reconstruction of DPSIFN (KODE 1, M 1):
IEEE double's machine constants, XMIN and the term count from the precision, the series over
(x+k)^-(n+1) for a small x and an order past 0, the asymptotic expansion over B(1..22) at x + XINC
and the backward recurrence. Its overflow and underflow exits and an order past 100 keep ADR 0377's
road; JGS keeps `Digamma` and `Polygamma`. The routine's text could not be fetched in the lane (a
download needs the user's leave), so the structure was rebuilt and checked by bits: 2,661 of 3,652
values agree with R2025b to the bit (324 of 332 for k = 0, 92% for k = 1..10, 31%, 24% and 11% for
k = 20, 40, 100); the rest are an ulp or two away, toward the truth about as often as away from it.

## Consequences

- New parity fixture `oi346_bracket_modes_psi` (1,297 rows from R2025b 25.2, one of them a
  divergence), packed and boxed, with helpers `p8c_chk` (p7d_chk printing char codes, integer
  classes through mat2str and N-D values as a row), `p8c_nth` (the k-th of n outputs) and `p8c_zs`
  (a number with its zero signs written out).
- A MATLAB script that joined two integer classes, `''` with a number vertically, or N-D arrays gets
  R2025b's answer instead of a refusal or a flattened matrix; one that joined a char with a logical
  or a complex value is refused as R2025b refuses it; `x = []; x = [x true]` is now a double, as in
  R2025b.
- `MatlabPackedClassM97Tests.TheRefusalsAndTheCombiningRuleKeepTheirWording` pinned the old refusal
  of `[int8(1) int16(2)]` and now pins R2025b's int8; `ValueIsolationM163Tests`' `num2cell(C)` row now
  unwraps the cells num2cell builds (`[D{:}, D{:}]`).
- A mixed-kind bracket is a managed copy through double planes (homogeneous joins keep their road);
  `mode`, `num2cell` and complex `median`/`unique` in MATLAB code are managed loops (not timed).
- The wave gate (full suite) found three things the lane's filters did not reach, fixed at the
  landing: `imag` read the stored plane of any packed complex array, so an N-D FFT's zero imaginary
  parts printed -0 in the packed lane and 0 in the boxed one (`MatlabPackedFftM96Tests`); it now
  reads the plane only of a flagged array (every imaginary part zero). `cat`'s dimension check
  refused the boxed lane's `[1]`, a one-element array holding a number (`ChipScalarArgumentTests`).
  And `disp([n held m stillHeld])` now joins numbers and logicals as a double, R2025b's
  `[2, 0, 5, 1]` (`OpenItemsBatch16Tests` updated).

## Divergences

- **`complex(1.1, 0) .^ 4` is the correctly rounded `1.4641000000000004`** (`flagpow_4`): R2025b's
  power, real and flagged alike, is one ulp off the correctly rounded value in about 8% of whole
  powers; this engine's is correctly rounded. Correctness outranks parity.

## Still open

- **psi(k, x)** differs from R2025b by an ulp or two on about a quarter of the values (most for
  k ≥ 20): DPSIFN was rebuilt from its structure, not transcribed, and its exact text (the
  constants' digits, the large-order branch) would close the rest. Its overflow and underflow exits
  (`psi(1, 1e300)`, `psi(0, 1e300)`) keep the old road.
- **A flagged factor with a -0 imaginary part** is read as +0 by the elementwise operator road
  (`NumericBinary` reads elements, which normalize), so 64 of the 5,184 products differ in a zero's
  sign; the kernel is right, and the fixture leaves those factors out. The quotient's zero signs
  (ADR 0377) were not measured.
- **Complex integers** (`complex(int8(1), int8(2))`, `[int8(1) 1i]`, `median` of one, R2025b's
  `MATLAB:sort:complexIntegers`): no storage here; those pairs are left out of the fixture.
- `[zeros(0,3); zeros(1,0)]` is 0-by-0 here and 0-by-3 in R2025b (`JgsEmpty.JoinDown`), and
  `int64`/`uint64` values past 2^53 lose digits in a join (stored as doubles) and print in `%g` through
  `mat2str`.
- `cell2mat` of an N-D cell of structs, and `num2cell` of a struct array with one argument, keep
  their old roads.
