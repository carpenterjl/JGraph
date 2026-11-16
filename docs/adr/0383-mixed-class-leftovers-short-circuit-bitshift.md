# ADR 0383 — Complex max and min, R2025b's short-circuit and condition conversions, bitshift, kron's class rule and the other mixed-class leftovers

## Status

Accepted, 2026-11-16. Open item 351 and two parts of open item 346 (`kron`'s class rule and the
`and`/`or`/`xor` function forms' shape) (chips wave 8, lane 8G). Follows ADR 0379.

## Context

Every expectation below was measured in R2025b 25.2 headless (`-noFigureWindows -batch`), with
lane 8G's probes 1 to 4 (about 430 cases, one line per case in both engines, diffed).

- **`max` and `min` of a complex operand and a non-integer** (`max([2 3], 1+2i)`, `max(true, 1i)`)
  were refused in JGraph's words ("max expects argument 1 to be a number") or answered a logical.
  R2025b compares a complex pair, the zero-imaginary flag included, by magnitude and then by angle
  in (-pi, pi] (`max(2, 2i)` is `2i`, `max(-2, 2i)` is `-2`, `max(complex(-2, 0), 2)` is `-2`), keeps
  the first on a full tie, and answers complex only when a picked value has an imaginary part
  (`max(complex([1 5], 0), 3)` is the real `[3 5]`, `min(true, 1i)` the real 1): single beside a
  single, else double (a logical or a char beside a complex number is a double; `max('ab', 1+2i)`
  is the real `[97 98]`). A value with a NaN part is a NaN: omitted, or kept under `'includenan'`,
  the first of two kept. The pair expands or is `MATLAB:sizeDimensionsMustMatch`.
  `'ComparisonMethod'` (a case-blind leading part of the name, `'Comp'`; values `'auto'`, `'abs'`,
  `'real'` in any case; the last one named wins) took two scalars only here: `'real'` compares the
  real part, then the imaginary one; `'abs'` of real numbers compares the magnitude, then the angle
  (`max(-2, 2, 'ComparisonMethod', 'abs')` is `-2`). A NaN flag after it is
  `MATLAB:max:InvalidFlagPosition`, a missing value `keyWithoutValue`, an unknown one
  `InvalidComparisonMethod` (`min`'s say `MATLAB:min:...`).
- **A complex reduction's NaN** came back as a real NaN (`max([NaN+1i, 2, 1i], [], 'includenan')`
  is `NaN+1i` in R2025b), and `max([NaN 2 1], [], 'includenan')` answered 2 in both storages: the
  scan let the next number past a NaN in front.
- **`&&` and `||`** answered for any operand. R2025b converts each evaluated operand in this order:
  a cell, string, struct, function handle, datetime, duration or categorical is
  `MATLAB:invalidConversion` ("Conversion to logical from cell is not possible."); a complex value,
  flagged or empty included, `MATLAB:nologicalcomplex`; a NaN `MATLAB:nologicalnan`; anything but
  one element (`[1 1]`, `[]`, `'ab'`, `true(1, 0)`, `ones(1, 1, 2)`, `sparse([1 1])`)
  `MATLAB:nonLogicalConditional`. A char is its code (`char(0) && 1` is false; it was true). An
  operand short-circuited away is not converted (`1 || []` is true). The same holds in a plain
  expression, an `if` or `while` condition, and inside a loop the loop compiler takes. **An `if`,
  `elseif` or `while` condition** of a cell, string, struct, function handle or datetime is
  `MATLAB:invalidConversion` as well (it was read as true here).
- **`rem("a", 2)`** answered 1: the string reached the verb as its char. R2025b's `rem` and `mod`
  refuse a string, a struct, a function handle and a datetime with
  `MATLAB:math:mustBeNumericCharOrLogical`, the string before a complex partner (`rem("a", 1i)`).
- **`bitshift`** kept JGraph's own refusals and arithmetic (a 53-bit double, no negative integers,
  an integer class saturating instead of dropping bits). R2025b refuses, in order: a char, string,
  logical, cell, struct or function handle `MATLAB:bitshift:numericArg`; a single with no integer in
  front (`bitshift(single(1), 1)`, `bitshift(1, single(1))`) `firstArgNotInt`; a complex part
  `complexArgs`; two non-scalars of different sizes `sizeMismatch` (no implicit expansion,
  `[]` beside a row included); a type that is not an integer class name (`8`, `'double'`, a cell)
  `unknownTypeString`, an integer's other than its own `invalidTypeString`; a double outside the
  assumed type or not whole `outOfRange`; a shift that is not whole (NaN, Inf) `inputsMustBeIntegers`.
  A double's assumed type is uint64 (`bitshift(1, 60)` is `2^60`, `bitshift(2^63, 1)` is 0); the
  shift is in the type's two's complement, dropping what passes the top (`bitshift(uint8(255), 1)`
  is 254, `bitshift(int8(64), 1)` is -128), a signed right shift arithmetic (`bitshift(int8(-8), -1)`
  is -4, `bitshift(int8(-128), -8)` is -1); the answer is in the first argument's class
  (`bitshift(1, int8(1))` is a double).
- **`atan2` and `hypot` of an integer** were already refused (`MATLAB:math:mustBeFloat`, ADR 0355),
  in a compiled loop, an anonymous function and `arrayfun` too. What was left: a cell is
  `mustBeFloat`, a complex operand of `atan2` (flagged included) `MATLAB:math:mustBeReal` before
  the size check, sizes that do not expand `MATLAB:sizeDimensionsMustMatch` (JGraph's words here),
  and `hypot` of a complex number is of its magnitude (`hypot(3i, 4)` is 5; it was refused).
- **`idivide` of int64 or uint64 and a double scalar** (complex or zero included) is
  `MATLAB:idivide:scalarDoubleWith64BitInt`; it answered. Two 64-bit integers divide, a zero
  divisor saturating.
- **The JGS `idivide`** answered 0 for every zero divisor, its comment saying "as MATLAB's does";
  integer division by zero saturates (`idivide(int8(7), int8(0))` is 127, `int8(-7)` -128, 0/0 is
  0), in MATLAB and as the integer classes define it. No ADR made the 0 deliberate.
- **`kron`** combined any classes. R2025b's `kron.m` is `A .* B` over reshaped operands, so it is
  the arithmetic operators' rule (ADR 0370): an N-D operand is `MATLAB:kron:TwoDInput` first; a
  cell, string, struct, function handle or datetime `MATLAB:math:mustBeNumericCharOrLogical`; an
  integer beside a complex value `MATLAB:complexInts`; beside a single, a logical, another integer
  class or a double or char that is not a scalar `MATLAB:mixedClasses` (`kron(int8([1 2]), [1 2])`,
  `kron(int8(1), [])`). A char is its codes (`kron('a', 'b')` is 9506; refused here).
- **The JGS `and`, `or` and `xor`** answered a flat row for a matrix (`and([1, 0; 1, 1], ...)` was
  1-by-4). Lane 7C made the MATLAB forms the operators; the JGS verbs still flattened.

## Decision

### max and min (`JgsBuiltins.MixedClassLeftovers.cs`, hooks in `JgsBuiltins.MixedClassVerbs.cs`)

`McIsTwoArrayForm` takes a NaN flag or a `'ComparisonMethod'` name third, and `McTwoArrayExtreme`
reads the options with `McExtremeOptions` (R2025b's three refusals). A complex pair with no integer
goes to `McComplexExtreme` (operands read by `McComplexOperandOf`, ordered by `McComplexOrder`,
expanded through `McPairwise` over element positions); `'abs'` of a real pair orders by
`McRealMagnitudeOrder`. A sparse operand keeps the verb's road. The reductions keep
`RankedExtreme`, which now reads a NaN answer back through the position the reduction reports
(`RestoreMissingExtremes`; a vector of dimensions keeps the real NaN), and the boxed scan
(`ExtremeOf`) and the packed one (`ReduceKernels.ScanExtreme`) stop at a NaN in front under
`'includenan'`.

### && and || (`Interpreter.LogicalOperands.cs`)

`ShortCircuitTruth` asks `MatlabShortCircuitTruth` in MATLAB code: `RefuseLogicalConversion`, the
complex and NaN refusals, then the one-element rule, a char read by its code; an object keeps its
own truth. `ConditionTruth` asks `RefuseLogicalConversion` first. The compiled loop holds only
scalar doubles, so a non-scalar, text or complex operand never compiles; such a statement walks and
the walk refuses it (a test asserts the loop road).

### The other verbs

`WrapMixedClassVerbs` wraps `bitshift` (`McBitShift`, `McShiftBits`, `McIntegerWidth`), `kron`
(`McKron`, keeping strings, a char row handed to the verb as codes) and `atan2`/`hypot`
(`McAtan2Hypot`), in MATLAB code only. `rem` and `mod` keep string arguments (the wrapper's new
`keepsStrings`) and refuse a string, struct, function handle or datetime. `McIntegerDivide` refuses
`scalarDoubleWith64BitInt`. The JGS `idivide` divides by zero as a double and lets the integer
class saturate; the JGS `Logical2` (`and`, `or`, `xor`) answers in its array operand's shape (the
left one's when both are arrays).

## Consequences

- New parity fixture `oi_mixed_class_leftovers` (532 rows from R2025b 25.2, every row agreeing,
  packed and boxed) with helpers `p8g_cond` (a value as a condition, beside `&&`/`||`, in a loop)
  and `p8g_two_out`; new test class `MixedClassLeftovers8gTests` (JGS shapes and `idivide`, the
  compiled-loop road, a char's code).
- A MATLAB script that handed `&&` or `||` an array, an empty, a cell or a string, or an `if` a cell
  or a string, now stops with R2025b's error where it answered; `bitshift` answers R2025b's numbers
  for negative integers, wide doubles and assumed types, and refuses where it did; `kron` refuses
  the class mixes R2025b refuses. JGS code keeps its answers except `idivide`'s zero divisor and the
  logical verbs' shape.

## Divergences

None opened.

## Still open

- **`max` and `min` of a sparse matrix and a complex number** (`max(sparse([1 2]), 1i)`) are refused
  here; R2025b answers a complex sparse matrix.
- **`rem` and `mod` of a duration** answer numbers here; R2025b's are
  `MATLAB:duration:InvalidNumericConversion` (even `rem(hours(5), hours(2))`).
- `bitshift`, `kron` and `atan2`/`hypot` of a categorical, a table or a char matrix keep the verb's
  road (not measured).
