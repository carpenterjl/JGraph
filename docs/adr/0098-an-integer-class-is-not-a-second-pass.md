# 0098 — An integer class is not a second pass

Date: 2026-08-28 · Status: accepted (M97; plan item B7)

*Numbered 0098 rather than 0097 because the `[]` literal took 0097 while this was being written.*

## Context

The plan's item B7 is headed "Integer-class ops — kills the 44×", and the 44× is the
`d03_intops_10M` row: 6.6 s against MATLAB's 0.15 when the plan was written. **That row was already
won before this milestone began, and not by anything this milestone does.** Read what it runs:

```matlab
iv = mod((1:1e7) * 2654435761, 2^31);
t = tic; isrt = sort(iv); ic = cumsum(isrt(1:1e6)); dt = toc(t);
```

There is no integer class anywhere in it. `iv` is an ordinary double array whose values happen to be
whole numbers — the script calls it "the exact integer pipeline" because the arithmetic has to match
to the last digit, not because a class is involved. The row is a sort and a cumulative sum, and
ADR 0094 and ADR 0095 closed it. It stands at **0.129 s against MATLAB's 0.150** — JGraph ahead by
1.16× — and the gate the plan wrote for M97, `intops 10M ≤ 0.3 s`, was met two milestones before
M97 started.

So the row this milestone was aimed at is not a row about this milestone's subject. The subject
itself — the eight integer classes and `single` — the head-to-head suite barely measures: the only
place a class appears in eight scripts is `uint8(255*img)`, three times, inside `d06_imwrite_x3`.
Measured directly against MATLAB on 4,194,304 elements, which is the size the image script works at,
the gap was real and had never been looked at:

| operation | JGraph | MATLAB | behind by |
| --- | ---: | ---: | ---: |
| `uint8(255*img)` | 0.0184 | 0.0072 | 2.6× |
| `single(img)` | 0.0093 | 0.0063 | 1.5× |
| `int32(img * 1e6)` | 0.0170 | 0.0105 | 1.6× |
| `a + b`, `c - a`, `d * 2` in int32 | 0.0574 | 0.0228 | 2.5× |
| `a * 100000` in int32 (saturating) | 0.0175 | 0.0109 | 1.6× |
| `p .* q + p` in single | 0.0230 | 0.0057 | 4.0× |
| `w + uint8(40)` | 0.0194 | 0.0023 | 8.4× |

That gap is what this milestone is about. The plan's stated gate is not, because it was already met.

## Decision

### What a class costs, and where it was being paid

Storage is always double. A class is a tag, and the tag records that the samples were rounded half
away from zero, saturated into the class's range, and had their NaNs read as zero — and that they
must stay that way. So every write into a classed array owes the same treatment to what it wrote,
and MATLAB's rule that `uint8(200) + uint8(100)` is 255 rather than 300 is that debt being paid.

It was being paid twice over, on two separate roads, and both of them one element at a time through
a delegate:

- `JgsNumericClasses.Stamp` → `Map` → `PackedMath.Map(buffer, dest, x => Convert(x, class))`. This is
  the road every arithmetic result takes, and concatenation and indexed reads with it.
- `JgsBuiltins.ToNumericClass` → `MapNumeric(..., x => Convert(x, class))`. This is the road the
  class constructors take — `uint8`, `int32` and their six siblings, and `single` and `double` — and
  with them `cast`, `idivide`, `fread`, and `zeros(m, n, 'int32')`.

Neither road knew the other existed. Both allocated a whole fresh array and swept it, and on the
arithmetic road that sweep came *after* a sweep that had just written every one of those elements —
sixty-four megabytes of traffic to read back what was still warm and write it out again, plus four
million virtual calls to decide what to do with each element, plus a `switch` on the class inside
every one of them.

### The rule as a value

`PackedMath.Rounding` is a class's whole arithmetic carried as a struct: nothing, a rounding to
`float` precision and back, or a round-half-away-from-zero into `[min, max]` with NaN read as zero.
Carrying it as a value is what lets the kernel that computes an element also finish it.

It is deliberately spelled twice — once as `Apply(double)`, which is `Math.Clamp(Math.Round(x,
MidpointRounding.AwayFromZero), min, max)` written out, and once in vector registers. The two
spellings live in different projects out of different vocabularies, so a test compares them element
for element over the whole edge grid rather than trusting them to have been written from the same
understanding, and a second test compares both against `JgsNumericClasses.Convert`, which is the
spelling the interpreter has always used.

### Round half away from zero, in a register

Four choices in the vector kernel are each there to stop a particular wrong answer.

**The fraction is compared against a half, not added to the element.** The obvious rounding is
`floor(x + 0.5)`, and it is wrong for `0.49999999999999994`: adding a half to it gives exactly 1.0 in
doubles, so the obvious form rounds up a value that is below the midpoint. Truncating and comparing
`|x − trunc(x)| ≥ 0.5` never forms that sum.

**The step away from zero is selected, not added.** Writing `r = whole + bump` with `bump` zero where
no rounding is due destroys a signed zero: `-0.0 + 0.0` is `+0.0` in IEEE, so `int8(-0.3)` would come
back `+0` where `Math.Round` gives `-0`. Selecting between `whole` and `whole ± 1` leaves the
untouched case untouched. A step is only ever taken when `|x| ≥ 0.5`, so the sign test that picks its
direction never sees a zero.

**The clamp is a compare-and-select, not `Vector.Max`/`Vector.Min`.** `vmaxpd` does not resolve `-0`
against `+0` the way `Math.Clamp` does, and `Math.Clamp(-0.0, 0, 255)` returns `-0.0` because
`-0.0 < 0.0` is false. Comparing and selecting reproduces that exactly; taking a maximum would not.

**NaN is blended to zero before any of it.** After that the infinities need no special case: `Inf`
truncates to itself, `Inf − Inf` is NaN, the `≥ 0.5` comparison against NaN is false so no step is
taken, and the clamp saturates it at the range end. Above 2⁵² a double has no fraction left, so it
subtracts from its own truncation to exactly zero and is already its own answer.

The float conversion narrows two double registers into one float register and widens them back,
which is the same pair of conversion instructions a scalar cast issues, under the same rounding mode.

### Fused, and still the same bits

The elementwise kernels take a `Rounding` as an argument. Given one, they walk their grain in 8,192-
element tiles: the arithmetic kernel writes a tile, then the rounding kernel finishes that tile
before the next one is started. The tile is sized so the second read comes out of cache instead of
out of memory — the same tiling `TryUnaryAtLeast` already used for its domain check.

What makes the fused answer the unfused answer bit for bit is that neither half changed. The
arithmetic is the kernel it was on its own and the rounding is the kernel it was on its own; the only
new thing is the tile they share. That is why they stay two loops rather than becoming one
hand-written expression per operator — six operators times three arities times two kinds of rounding
would have been thirty-six expressions to get right, and each one a fresh chance to disagree with the
scalar path. The tests assert the identity directly, over every operator and arity.

### Where the interpreter hands the class down

`ApplyBinary` already asked `JgsNumericClasses.Combine` which class the answer is owed. It now passes
that class into `ApplyBinaryCore`, which hands it to the packed fast path, which rounds into it as it
computes.

The delicate part is knowing whether that happened, because converting a second time would cost
exactly the sweep the fusion saves. `ApplyBinaryCore` answers with an `out bool`, set true on the one
road where a kernel actually applied the class and false everywhere else — a boxed fallback, a matrix
operation, an implicit expansion, a promotion to complex, a user-class overload, time arithmetic. The
compiler's definite-assignment rule does the enforcing: every other `return` in that method has to
have set it, and they all set it false. Where it comes back false the answer is converted afterwards
exactly as it always was, so the fusion can only ever be an optimisation that did not fire.

The class-constructor road was left where it is and given the same rule: `MapNumeric` now takes an
optional `Rounding`, and `ToNumericClass` passes the one its class asks for. That one change covers
all seven of its callers.

## What this did not close, and cannot

`w + uint8(40)` on four million elements finished at 0.0105 s against MATLAB's 0.0023. That is still
4.6× and it is not a kernel problem: MATLAB stores a `uint8` array in one byte per element and we store
it in eight. MATLAB moves 8 MB to do that addition and we move 64 MB. At those sizes we are in fact
moving *bytes* slightly faster than MATLAB is — about 5.3 GB/s against its 3.5 — and losing anyway,
because there are eight times as many of them.

The honest statement is that **the double-backed integer representation is now the whole of the
remaining gap on the narrow classes**, and closing it means a second storage representation reaching
into every kernel in `JGraph.Numerics`, which is a far larger thing than this milestone and was never
what B7 proposed. The measurement that says so is in *Measured* below: an integer-class operation now
costs what the identical double-class operation costs, to within the noise. The class itself is free.

## Two defects found here, and left standing

These are not divergences from MATLAB — the packed lane matches MATLAB in both cases — so they are
deliberately not written under a heading the divergence harvester reads. They are JGraph's two
interpreter lanes disagreeing with each other, which is its own kind of wrong.

Both were found by the parity tests, both were reproduced unchanged at `5a8bcb1` (two milestones
back) as well as at `47bce4e`, and neither is this milestone's doing. Both are the boxed lane losing
metadata the packed lane keeps, and both are filed as work of their own.

1. **The boxed lane drops N-D shape through elementwise arithmetic.** `A = reshape(1:24, 2, 3, 4);
   B = A * 1000` gives a 2×3×4 packed and a 2×12 boxed. This is the trap ADR 0094 recorded and it is
   still standing.
2. **The boxed lane drops the class when an integer array is grown past its end.** `x =
   uint8([10 20 30]); x(4) = -7` gives a `uint8` holding 0 packed, and a `double` holding −7 boxed.
   An in-range indexed write — `x(2) = 300` — is correct in both lanes; only the grow path loses the
   tag.

The packed lane is right in both cases and matches MATLAB. Rather than assert a parity that does not
hold, the two tests that found these pin the packed lane's answer and say in their own comments why
they do not compare the roads.

## Consequences

- `JGraph.Numerics` gains a public `PackedMath.Rounding` and a `PackedMath.Round`, and three fused
  overloads of the binary kernels. Nothing else in the project changed.
- The class constructors, `cast`, `idivide`, `fread` and the shaped-with-a-class builtins all take
  the vector road, because they all pass through `ToNumericClass`.
- Arithmetic inside a class is now free relative to the same arithmetic outside one. That is a
  stronger claim than "faster" and it is the one the controls in *Measured* test.
- Nothing in the head-to-head suite moves except `d06_imwrite_x3`, and only a little of that row is
  the three casts; the rest is PNG encoding. This milestone's subject is not what that suite
  measures, which is worth knowing before the next plan item is sized from it.
- The plan's stated gate for M97 (`intops 10M ≤ 0.3 s`) is met, and was met before M97 began. It is
  recorded as met, not as achieved here.

## Measured

Release, on a rested machine with no `testhost.exe` alive — the orphaned-process hazard ADR 0096
records is the reason every number here is a median of five runs rather than one reading.

Every figure is a median over six interleaved runs of the before and after binaries — `47bce4e`
built in one worktree, this milestone in another, alternating so that any drift in the machine falls
on both — with each binary's cold first run dropped. All four thousand elements of the checksums are
identical between the two.

| row (4,194,304 elements) | before | after | after's range | gain | MATLAB | behind |
| --- | ---: | ---: | --- | ---: | ---: | ---: |
| `uint8(255*img)` | 0.0184 | **0.0154** | 0.0144–0.0168 | 1.19× | 0.0072 | 2.14× |
| `single(img)` | 0.0093 | 0.0098 | 0.0086–0.0109 | 1.00× | 0.0063 | 1.6× |
| `int32(img * 1e6)` | 0.0170 | **0.0135** | 0.0113–0.0141 | 1.26× | 0.0105 | 1.29× |
| three int32 ops | 0.0574 | **0.0366** | 0.0335–0.0518 | 1.57× | 0.0228 | 1.61× |
| `a * 100000` in int32 | 0.0175 | **0.0103** | 0.0100–0.0125 | 1.70× | 0.0109 | **0.94× — beats** |
| `p .* q + p` in single | 0.0230 | **0.0179** | 0.0166–0.0191 | 1.28× | 0.0057 | 3.14× |
| `w + uint8(40)` | 0.0194 | **0.0105** | 0.0101–0.0117 | 1.85× | 0.0023 | 4.57× |

A bare cast of an array that is already in memory, measured on its own over sixteen samples because
the six-sample medians were not separating it from its own noise: `uint8` **0.0149 → 0.0103**
(1.44×), `double` 0.0105 → 0.0092 (1.14×), `single` 0.0099 → 0.0098 (1.00×). The float cast gains
nothing and that is not a disappointment but an explanation: it moves 64 MB and does one conversion
instruction per element, so at about 6.5 GB/s it was already bandwidth-bound and the delegate was
never what it was waiting for. The casts that gained are the ones with arithmetic in them.

**The controls are the real result.** Each classed operation timed beside the *identical* operation
with no class on it, same size, same run:

| operation | with no class | in an integer class | ratio | before |
| --- | ---: | ---: | ---: | ---: |
| `x + scalar` | 0.0111 | 0.0094 | 0.85× | 0.0188 |
| `x * scalar` | 0.0084 | 0.0101 | 1.20× | 0.0180 |
| `x + array` | 0.0110 | 0.0099 | 0.90× | 0.0170 |

The ratios straddle 1.00 in both directions, which is what "the same" looks like when it is measured
rather than asserted. **An `int32` addition now costs what a `double` addition costs.** It used to
cost 1.7–2.0× as much, and the difference was the second sweep.

**Nothing in the head-to-head suite moves, and that was expected.** `d06_imwrite_x3` — the only row
in eight scripts with a class in it — reads 0.747 against MATLAB's 1.319, where M96 left it at 0.724;
the three casts save about 11 ms out of 720, which is under that row's noise. Paired four-run
measurements of the three scripts that could plausibly have been touched give ratios scattering
0.82×–1.12× in both directions, `d03_total` at 1.00× and `d06_total` at 0.98× — no systematic cost on
double-only code from threading the class down through `ApplyBinary`, and no systematic gain either.
Parity is unmoved at 48 of 49 checks and every checksum reads what it read before.

One thing that cost time and is worth recording. The first version applied the rounding by copying
the source to the destination and rounding in place, which is two passes where the delegate made one,
and the float cast — the cheapest rule, and the one with no arithmetic to hide a second pass behind —
came out *slower than before*. It was caught by measuring against the previous build rather than
against nothing. The kernels now read the source and write the destination in one pass. A second
version tiled every fused binary, including the ones carrying no class at all, which cut each grain
into eight and made a `TensorPrimitives` call pay its per-call overhead eight times; a rule that moves
nothing now takes an untiled arm.

## Testing

- `RoundClampM97Tests` (33 cases): the vector kernel against the scalar spelling, bit for bit, over
  an edge grid built out of the values that break the obvious implementations — both ties, the value
  one ulp under a tie, ±2⁵², ±2⁵³, both infinities, NaN, both zeros, the denormals, and each of the
  eight classes' two range ends with a whole step, a half step and one ulp either side of them.
  Every offset from 0 to 8 so the value under test lands in the vector body and in the scalar tail
  alike; every length from 0 to 40; a hundred thousand randomized values per class; the float
  conversion against `(float)x` over five thousand exponents; thread-count invariance over three
  million elements at DOP 1 against DOP 16; and the fused kernels against the unfused pair for all
  six operators, all three arities and five roundings.
- `MatlabPackedClassM97Tests` (10 cases): the same scripts run with packing forced on and forced off,
  asserting byte-identical output at seventeen significant digits with reciprocals printed alongside
  so a saturated `-0` cannot hide behind a zero that prints the same either way. Every class over the
  edge grid; saturating arithmetic in int8, uint8, int32 and int64; `single` through six operations
  including the 16777216/16777217 pair; shapes, compound assignment and indexed reads; chars through
  their codes and logicals through their zeros and ones; the refusals and their exact wording; and
  three hundred thousand elements to cross both the threading grain and the fusion tile.
- The cross-layer test that `PackedMath.Rounding` and `JgsNumericClasses.Convert` are two spellings
  of one rule, over every class and forty edge values, as scalars and through a buffer.
