# ADR 0379 — Mixed classes outside the arithmetic operators, R2025b's logical conversions, and a uniform arrayfun's classes

## Status

Accepted, 2026-11-15. Open item 340 (all but an integer with a sparse matrix, lane 7B's) and the
`[1 0 NaN] & 1` part of open item 341 (chips wave 7, lane 7C).

## Context

Every expectation below was measured in R2025b 25.2 headless (`-noFigureWindows -batch`), with
probes `probe1` to `probe5` in lane 7C's scratch (about 6,300 lines: eighteen operands, a double
scalar, row, column and 0-by-0, a single scalar, row and empty, an int8 scalar, row and empty, an
int16 scalar, a uint8 row, a logical scalar, row and empty, a char and a char row and a complex
scalar, every ordered pair under eighteen two-operand verbs and operators), one line per case in
both engines and diffed.

- **`max` and `min` of two arrays** combined any classes and read a logical beside a number as a
  count (`max(2, true)` answered a logical 1; R2025b's is the double 2). R2025b holds the pair to
  the arithmetic operators' rule (ADR 0370): an integer goes with its own class or a scalar double
  (a scalar char is its code), anything else is `MATLAB:mixedClasses`, a complex partner of an
  integer `MATLAB:complexInts`; the two arrays expand into each other or are
  `MATLAB:sizeDimensionsMustMatch` (`max([2 3], [])` reduced the row here, `max([2 3], [2; 3])`
  was refused); the answer is the integer class, else single, else double, and two logicals stay
  logical. NaN is omitted (`max(int8(2), NaN)` is int8 2; it was 0 here, the NaN cast first), or
  kept under `'includenan'`. A text second argument is `MATLAB:max:secondInputDimAll` when it
  starts "all" (`'a'`, `"all"`, `'All'`), `secondInputFlag` for a NaN flag or `'linear'`, and
  `wrongSecondInputText` otherwise; a string, cell or struct first argument is
  `MATLAB:max:wrongInput`; two outputs are `MATLAB:max:TwoInTwoOutCaseNotSupported`.
- **`rem` and `mod`** took a scalar divisor only (`rem(5, [2 3])` was refused in JGraph's words)
  and combined any classes. R2025b's are the same class rule, expansion and answer class (a
  logical answer is a double); a complex operand, the zero-imaginary flag included, is
  `MATLAB:math:mustBeReal`, a string or cell `MATLAB:math:mustBeNumericCharOrLogical`. The real
  rules for an infinite divisor were wrong in both: `rem(7, Inf)` and `mod(7, Inf)` are 7 and
  `mod(7, -Inf)` is `-Inf` (here NaN, `x - floor(x/y)*y`); `mod(int8(7), -Inf)` is int8 -128.
- **`bitand`, `bitor`, `bitxor`** refuse, in this order: a char or string
  (`MATLAB:Bitand:operandsNotNumeric`, `MATLAB:Bitor:...`, `MATLAB:bitxor:...` — R2025b's own
  casing), a complex part (`complexInputs`), a single with no integer (`firstArgsNotInt`), two
  operands that do not expand (`sizeMismatch`, except two non-empty operands of one integer class
  or two logicals, which say `MATLAB:sizeDimensionsMustMatch`), an integer beside anything but
  its class or a scalar double (`mixedClasses`, "Inputs must be signed or unsigned integers of the
  same class or scalar doubles."); a negative or fractional double is `outOfRange`
  (`outOfRangeMixedType` beside an integer). A logical beside a double answers a double (it
  answered a logical here), two logicals a logical, and a negative integer works in two's
  complement (`bitand(int8(-1), int8(1))` is 1; refused here).
- **`idivide`** is R2025b's `idivide.m`, which checks the sizes first (`dimagree`), then that one
  side is an integer (`OneArgumentMustBeInteger`), then the classes (`mixedClasses`: only the same
  class or a *double* scalar, so a char, a logical or a single scalar is refused), then a complex
  part (`complexInts`), then partly matches the rounding option (`InvalidRoundingOption`). A zero
  divisor saturates (`idivide(int8(7), int8(0))` is 127; it was 0 here).
- **The colon** combined classes or said so in JGraph's words (`int8(1):int16(3)`). R2025b's
  non-double operands (a logical, a single, each integer class, empties included) must be of one
  class, else `MATLAB:colon:mixedNonDoubleOperands`; a single or integer array in front is
  `operandsNotRealScalar` first, and a complex operand is `operandsNotRealScalar` after the mixing.
  Beside a char last operand a single or integer one is the mixing too, and `'a':int8(1):'c'` is
  `mixedCharOperands` ("second operand must be char or real scalar double"). An empty single or
  integer operand gives the empty range its class (`int8([]):3` is an int8 1-by-0).
- **`&`, `|`, `~`, `xor`, `and`, `or`, `not`, `logical`, conditions.** A NaN anywhere in an
  operand is `MATLAB:nologicalnan` (`[1 0 NaN] & 1`, `~NaN`, `xor(NaN, 1)`, `logical([1 NaN])`,
  `if [0 NaN]`, `while`, `elseif`, an evaluated operand of `&&` or `||`), even beside an empty
  (`NaN & zeros(1, 0)`); here each read NaN as true. A complex operand, flagged included, is
  `MATLAB:math:mustBeReal` under `&`, `|` and `xor`, `MATLAB:Not:operandsNotReal` under `~`, and
  `MATLAB:nologicalcomplex` as a condition; a string, cell or struct beside `&`, `|` or `xor` is
  `MATLAB:math:mustBeNumericCharOrLogical`. `xor` did not expand, kept no shape and read a char row
  as one string. `logical('a')` is true (refused here).
- **Empties.** An empty answer of a comparison or a logical operator is a logical empty of the
  expanded size whatever the classes (`int8(2) & true(0)` is a logical 0-by-0, `2 & []`,
  `zeros(1, 0, 2) < 1` a logical 1-by-0-by-2); here `&` and `|` answered a double (1-by-0 for
  `xor`), and the boxed lane a double for `int8(2) == true(0)` too.
- **An integer compared with a complex number** by `<`, `<=`, `>` or `>=` is
  `MATLAB:relop:complexIntegers` (ADR 0370's Still open), empty or flagged included; `==` and `~=`
  answer.
- **A uniform `arrayfun` or `cellfun`** gathered every numeric, logical or text answer as doubles:
  `cellfun(@(x) x, {int8(1), int8(2)})` was a double row (R2025b int8), `{1, int8(2)}`,
  `{1, true}`, `{"a", 'b'}`, `{single(1), 1+2i}` answered (R2025b
  `MATLAB:cellfun:MismatchInOutputTypes`, at the index and output where the class changes, asked
  before that answer's own size: `{1, 'ab'}` is the mismatch, `{'ab', 1}` the non-scalar at index
  1). `arrayfun` over an int8, uint16 or single array handed its function doubles (R2025b the
  class; `arrayfun(@(x) x / 2, int8([1 3 5]))` is int8 `[1 2 3]`, it was `[0.5 1.5 2.5]`), and over
  `complex([1 2], 0)` real numbers (R2025b keeps each element complex).

## Decision

### The verbs' wrappers (`JgsBuiltins.MixedClassVerbs.cs`)

`WrapMixedClassVerbs`, registered right after `WrapComplexFlagVerbs` (so it wraps it), wraps
`max`, `min`, `rem`, `mod`, `bitand`, `bitor`, `bitxor`, `idivide` and `logical` for MATLAB code
only. Each reads its two operands as `McOperand` (values column-major, size, class, logical, char:
a number, logical, char row or real numeric array; anything else — complex, sparse, time,
categorical, char matrix, object — is handed to the verb as before), applies R2025b's ladder of
refusals above, expands the pair (`McBroadcastDims`, `McPairwise`) and answers in the combined
class (`McAnswer`). `MatlabRem` and `MatlabMod` are R2025b's real rules with the infinite cases;
JGS keeps `ScalarRem`/`ScalarMod`. `max`/`min` of a complex
operand and a non-integer keeps the verb's road (see Still open). `logical` refuses a NaN and reads
a char row's codes. `bitand`/`bitor`/`bitxor` of int64 or uint64 and a double partner outside the
integer class's range keep the verb's road (a double cannot carry every 64-bit pattern; not
measured).

### The operators (`Interpreter.LogicalOperands.cs`, two short hooks in `Interpreter.cs`)

`ApplyBinary` asks `MatlabLogicalOperands` for `&`, `|` and the six comparisons in the MATLAB
dialect, after a char operand has become its codes and after the sparse road: it raises the
refusals above and answers the logical empty (`McLogicalAnswer`) when an operand is empty and the
sizes expand; a non-expanding pair falls through to the existing size refusal. `~x` asks
`MatlabNotOperand` first. `if`, `elseif` and `while` conditions (the walk and the compiled loop's
bail records) read `ConditionTruth`, and `&&`/`||` read `ShortCircuitTruth`; JGS reads truth as it
always has. The function forms `and`, `or` and `not` are re-registered in
`RegisterOperatorFunctions` to call the operators in MATLAB code (JGS keeps CreateGlobals' verbs;
`McLogicalVerbOriginals` stops a second interpreter over the same globals from wrapping the
wrapper), and `xor` is `ApplyMatlabXor`: the refusals, then `(a ~= 0) ~= (b ~= 0)`.

### The compiled loop (`LoopCompiler`, `LoopOp.GuardNaN`)

A loop compiles in MATLAB code only, so its kernels are now MATLAB's: `Mod`, `Rem`, `Min2` and
`Max2` bind `MatlabMod`, `MatlabRem`, `MatlabMin2` and `MatlabMax2` (NaN left out, the infinite
divisors), as the walk answers, in the register machine, the emitted method and the ODE body
compiler. A value read as a logical that is not one already — a condition before its
`JumpIfFalse`, an operand of `~`, `&`, `|`, `&&`, `||` — passes a new `GuardNaN` op, which bails to
the condition's or statement's record when it holds NaN; the walk then refuses it (the M98 test
that NaN is truthy in a compiled condition now expects the refusal on both roads).

### The colon (`Interpreter.ColonClasses.cs`)

`RangeFromValues` asks `RefuseColonClassMix` before the bounds are read and takes an empty
operand's class (`ColonEmptyOperandClass`) when the bounds left the range double.

### arrayfun and cellfun

`arrayfun` walks `ClassedElementsOf` (each element stamped with the array's class, a complex
array's elements kept complex) in MATLAB code. Both verbs ask `CheckUniformClass` for every uniform
answer after the first (cells, structs, handles, times and objects stay ADR 0371's and the object
gatherer's), and the gathered result takes the answers' class (`UniformClassOf`).

## Consequences

- New parity fixture `oi_mixed_class_verbs` (5,399 rows from R2025b 25.2, every row agreeing,
  packed and boxed) with a helper `p7c_chk`; the grid rows are `g_<verb>_<left>_<right>`.
- A MATLAB script that mixed an integer with a logical, a single, another integer class or a
  double array under `max`, `min`, `rem`, `mod`, the bit verbs, `idivide` or the colon, or that
  reached `&`, `|`, `~`, `xor`, `logical` or a condition with a NaN, now stops with R2025b's error
  where it answered. `rem` and `mod` with an array divisor now answer. `arrayfun` over an integer
  or single array runs its function in that class, so integer arithmetic inside it saturates and
  rounds as R2025b's does, and a uniform result of integer or single answers keeps their class.

## Divergences

None opened.

## Still open

- **`max` and `min` of a complex operand and a non-integer** (`max([2 3], 1+2i)`, `max(true, 1i)`)
  are refused here in the verb's words or answer a logical; R2025b compares by magnitude and angle
  and answers complex (but `max('ab', 1+2i)` real). Not this item's class rule.
- **`[1 1] && 1` and `[] || 1`** answer here; R2025b's are `MATLAB:nonLogicalConditional`. Only a
  NaN operand of `&&`/`||` is refused.
- **`rem("a", 2)`** answers 1 (the string reaches the verb as its char); R2025b's is
  `MATLAB:math:mustBeNumericCharOrLogical`. A wrapper that keeps strings would hand them to the
  verbs' other roads undemoted.
- **`bitshift`** keeps its own refusals (R2025b: `MATLAB:bitshift:numericArg`,
  `firstArgNotInt`, `sizeMismatch`, `complexArgs`); `atan2` and `hypot` of an integer are
  `MATLAB:math:mustBeFloat` there and answer here.
- `arrayfun` over a sparse matrix (R2025b's `MATLAB:arrayfun:SparseInputNotSupported`) was refused
  by ADR 0374, which landed before this one.
- **`idivide` of int64/uint64 and a double scalar** (`MATLAB:idivide:scalarDoubleWith64BitInt`)
  is not measured and keeps the verb's road. The JGS `idivide` still answers 0 for a zero divisor.
