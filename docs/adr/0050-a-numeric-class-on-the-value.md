# ADR 0050 — A numeric class on the value, a cell a loop can walk, and a table that knows its size

## Status

Accepted (M47, 2026-08-02). Closes the three base-language gaps
[ADR 0049](0049-the-image-processing-toolbox-surface.md) recorded and left. Builds on
[ADR 0043](0043-shaped-arrays.md) (shape on the value wrapper) and
[ADR 0045](0045-sparse-integer-perf-numerics.md) (integer conversion as round-and-saturate on double
storage).

## Context

M46 wave L wrote a script the way somebody would actually write one, and three things it wanted did
not exist. None of them was about images, so all three were written down rather than folded into a
toolbox milestone:

**A converted number forgot what it was converted to.** M42 gave the language the eight integer
class constructors, implemented exactly as MATLAB defines the conversion — round half away from zero,
saturate at the class limits, NaN to zero — over the double storage JGraph has always had. What it
did not do was record which class had been asked for, so `class(uint8(7))` answered `'double'` and
`isinteger` answered false for everything. M46 then put a class tag on `ImageBuffer`, which is why
`class(imread(...))` answers `'uint8'` correctly while the plain array beside it does not. Two
adjacent things gave two different answers to the same question.

**A `for` loop could not walk a cell array.** M41.F taught the loop MATLAB's column-at-a-time
semantics for numeric matrices. The cell case was never added, so `for name = {'line', 'diamond'}` —
the ordinary way to loop over a list of words — was an error, and a script had to count and index by
hand instead.

**A table could not say how big it was.** `height` and `width` are how a MATLAB script asks a table
for its row and variable counts. Neither existed, and `numel(T.SomeColumn)` was the workaround in
use.

## Decisions

### 1. The class is a tag on the value, not a second representation

`JgsValue` gains one field, `NumericClass`, defaulting to `Double`. Storage does not change: a
`uint8` array holds the same doubles it always held, already rounded and saturated into 0…255. The
tag records that they were, and that they must stay there.

This is the same shape ADR 0043 chose for shape itself, for the same reason. The alternative — eight
real integer buffer kinds — would have given every numeric kernel, every packed fast path and every
builtin nine cases instead of one, to buy an in-memory footprint no script has asked for. A tag buys
the observable behaviour (`class`, `isinteger`, `isa`, `whos`, saturating arithmetic) at the cost of
one byte per wrapper.

The tag is mint-time only, set through `SetNumericClass` on a wrapper the caller has just built, and
never on a wrapper a name is already bound to. That is what keeps it compatible with the
single-wrapper invariant packed arrays rely on.

### 2. Arithmetic keeps the result inside the narrower class, and refuses what MATLAB refuses

`ApplyBinary` became a wrapper around the old body: it works out the result class from the two
operands, runs the operation, and puts the answer back into that class. Only arithmetic takes a
class — a comparison answers a logical whatever it compared.

The rule is MATLAB's, stated in one place (`JgsNumericClasses.Combine`):

- An integer class wins over a floating one. `uint8(200) + uint8(100)` is `uint8(255)`, not 300.
- Two **different** integer classes are an error, and so is an integer array combined with a
  non-scalar double. An integer array combines only with its own class or with a scalar, so
  `uint8([1 2]) + 1` works where `uint8([1 2]) + [1 2]` is refused, by name, with both classes in the
  message.
- `single` beats `double`. Everything else is `double`.

Concatenation follows the same precedence, which is why `[int8(1) 300]` is an `int8` row whose second
element saturates to 127 — the double next to it is converted, not the other way round.

### 3. The tag rides along everywhere a wrapper is minted

The M40 lesson applies unchanged: metadata on the wrapper is lost by every path that builds a new
one. Three paths carry it — `KeepShape`, which every copy-on-assign binding already went through;
`IndexInto`, so a selection out of a `uint8` array is still `uint8`; and the two literal builders.
Everything else — reductions, the elementary functions, the linear algebra — answers `double`, which
is recorded as a divergence rather than claimed as fidelity.

### 4. A cell iterates a column at a time, binding a cell

`for x = C` over a cell walks columns exactly as it does over a matrix, and the bound value stays a
cell: a one-row cell binds a 1×1 cell each pass, so the body reads it with `x{1}`. That is MATLAB's
rule, and it is the one that makes the idiom read the way scripts write it.

### 5. `height` and `width` read the first two dimensions, and `size` learned tables

`height(X)` is `size(X, 1)` and `width(X)` is `size(X, 2)` for everything, which is what MATLAB has
done since R2020b. A table's two dimensions are its row count and its variable count.

Teaching them tables meant teaching `size` tables at the same time: it had been answering `[1 1]` for
one, because a table fell through to the catch-all. Both now read a single `SizeDims` helper, so
there is one answer to the question and three names for it.

## Consequences

**Two further defects surfaced while closing the three, and were fixed.**

A **cell literal with rows was flattened row-major into a single 1-by-n cell**. `{1, 'two'; 3,
'four'}` reported a size of 1-by-4, `C{2}` was `'two'` rather than 3, and `C{2,1}` was out of range
outright. It is fixed to build column-major with the real shape, which is also what the new
for-over-cell needs to iterate a two-row cell correctly. This is the same class of finding as wave L's
four: nothing was wrong with cells until something asked them for their shape.

The **assignment copy dropped the class**, found the first time a probe script assigned an arithmetic
result to a name and asked its class. The fix is one line in `KeepShape` — the same helper M40 added
when the identical thing happened to shape, which is the strongest evidence available that this is
where wrapper metadata belongs.

**Three recorded divergences.** A reduction over an integer array (`sum`, `prod`, `max`) answers
`double`, where MATLAB's `sum` defaults to `'native'` for integer inputs. The elementary functions
answer `double` rather than erroring on an integer argument as MATLAB does. And integer arithmetic is
computed at double precision and converted once at the end, so a long expression rounds where MATLAB
would round at each step — a favourable difference, and stated rather than hidden.

**The base tracker moved by two, to 607 of 2,027 callable** — `height` and `width`, both documented
as functions rather than builtins. Neither table in `matlab-builtin-coverage.md` moved.

**2,557 tests pass** (2,545 before), the twenty-two stress scripts still exit 0 with no `Fail:` line,
and the build carries 0 warnings.

## Alternatives considered

- **Real integer storage.** Eight buffer kinds, or a discriminated numeric buffer. It buys exact
  intermediate rounding and a smaller footprint, and costs a case in every kernel, every packed fast
  path and every builtin boundary in the codebase. The observable behaviour a script can see is
  identical for everything except a multi-step integer expression, which is recorded above.
- **Letting an integer array combine with a double array.** More forgiving, and wrong: MATLAB refuses
  it, and a script that relies on the permissive reading here fails there. The error names both
  classes and says what is allowed, which is more useful than a silent answer.
- **Carrying the class through every builtin.** The honest full mirror, and a milestone of its own:
  each of several hundred builtins has its own MATLAB rule for what class it returns. Carrying it
  through assignment, indexing, concatenation and arithmetic covers what a script does with a class
  tag; the rest is recorded.
- **Leaving the cell literal alone.** Strictly, its shape is not one of the three gaps. But the
  for-over-cell loop this ADR adds reads a cell's shape, and a two-row cell literal did not have one —
  so half the new feature would have been quietly wrong. Same reasoning as wave L's: a fix that the
  thing being added depends on is not scope creep.
