# 0097 — There is more than one empty

Date: 2026-08-27 · Status: accepted (M96b; found while checking M96's transform against MATLAB)

## Context

`size([])` answered `[1 0]` here. MATLAB answers `[0 0]`.

It was found sideways. M96 put the transform family on planar kernels and checked every shape it
could reach against `matlab.exe -batch`; two rows would not line up. `fft([])` came back 1-by-0
where MATLAB said 0-by-0, and `fft([], 4)` came back a **1-by-4 of zeros** where MATLAB said a
**4-by-0 empty** — not a rounding difference but a different answer of a different shape. Handed the
same shapes by name — `fft(zeros(1, 0), 4)`, `fft(zeros(0, 3), 2)` — the two agreed exactly. So it
was not the transform. `zeros(0, 0)` was already 0-by-0 here. It was the literal, and the literal
alone.

MATLAB has more than one empty, and they are not interchangeable:

* `[]` is **0-by-0** — the shapeless empty, which carries no orientation at all;
* `zeros(1, 0)` is a **row** that happens to hold nothing;
* `zeros(0, 1)` is such a **column**.

Every reader that walks "the first non-singleton dimension" walks **down** a 0-by-0 (dimension 1 is
0, which is not 1) and **across** a 1-by-0 (dimension 1 is 1, so it moves on to the second). That one
difference decides which way `fft`, `sum`, `cumsum`, `sort`, `diff`, `max`, `cat` and everything else
in that family runs. A literal minted as the wrong empty is therefore not a cosmetic error: it is the
wrong answer, quietly, in a value a great many scripts start from.

The mint was three lines of `Interpreter.cs`. An array built with no shape asked for is a row — 1-by-n
— and for no elements that is 1-by-0, which is exactly the *other* empty.

## Decision

**`[]` is 0-by-0 in the MATLAB dialect**, along with `''` and `{}`. JGS keeps its 1-by-0 row: there a
bracket is a list rather than a concatenation, an empty list has been 1-by-0 since the language
shipped, and that surface is frozen.

`src/JGraph.Scripting/Jgs/JgsEmpty.cs` is new and holds what an empty is worth and what happens when
you join one to something. Everything below is downstream of the literal's shape, and each answer in
it was **read out of MATLAB rather than reasoned about** — the empty-array corners are thinly
documented, and several are only knowable by asking.

### Concatenation omits an empty, and the ones left decide the shape

MATLAB omits an empty from a join. That is what makes `[[], 1]` a 1-by-1 rather than a shape error,
and it is what a script growing a result with `out = [out; row]` from `out = []` depends on. It is
also applied **before the kind of the join is decided**, not only its size: `['SN:', []]` is the char
row `'SN:'`, where the numeric block machinery had been taking the text for a block and answering a
1-by-1 double.

When every piece is empty there is nothing to omit *into*, and the shape has to be settled from the
empties themselves. A 0-by-0 is dropped outright — it is the empty that carries no shape — and if
that leaves nothing, 0-by-0 is the answer. So `[zeros(0, 0), zeros(1, 0)]` is 1-by-0 and
`[zeros(0, 3), zeros(0, 2)]` is 0-by-5. What remains can still disagree, and MATLAB does not refuse
it: for blocks that are all zero columns wide it answers the tallest by zero, and where the
disagreement is not that clean it gives up and answers 0-by-0. Those last two are measured behaviour
that no ordinary script reaches; they are in `JgsEmpty` with their provenance written on them.

`horzcat`, `vertcat` and `cat` follow the same rule as the bracket now. They did not before —
`vertcat([], [1 2])` was a shape error and `cat(1, [], [1 2])` walked off the end of an array.

### An empty grows, and a shapeless one takes its extent from what is written

`out = []; out(3) = 7` is how a great many MATLAB scripts fill a result they did not size in advance,
and it depends on the empty growing. Linear growth past the end is refused only where the answer
would genuinely be ambiguous — a matrix with more than one row *and* more than one column, which is
the case MATLAB itself refuses. Everything else grows: a row, a column, and every empty. `zeros(0, 1)`
is a column and grows downwards; the shapeless empty grows across. (This also closes `q = [1; 2];
q(4) = 7`, which had been refused: it is the same predicate, and writing a narrower one would have
been keeping a known-wrong refusal on purpose.)

`A(:, j) = v` on the 0-by-0 takes the `:` extent from the **right-hand side** — `out = [];
out(:, 1) = [1; 2]` is a 2-by-1. Only 0-by-0 does this. `zeros(0, 3)` has a shape already, and
`A(:, 1) = 5` on it writes into no rows and stays 0-by-3.

`A(:) = []` deletes every element there is and leaves the shapeless empty, whatever `A` was. It had
never reached the deletion path at all — `:` arrives as a null index — and refused to "assign 0
values into 3 selected elements".

### Every empty result carries a shape

The rest of the work is one bug repeated: a function that answers an empty minted a bare row rather
than the shape its own rule gives. `A(idx)` with an empty index now obeys the ordinary shape rule, so
`v([])` is 0-by-0 (the index's shape) where `v(zeros(1, 0))` is 1-by-0 (the vector's orientation).
`A(:)` flattens an empty to a column, 0-by-1, as it does everything else — and so does `c(:)` on a
cell, which had never followed it. `find`, `unique`, `fliplr`, `flipud`, `flip`, `diag`, `num2str`,
`cellfun`, `inv` and `\` all answer with a shape now.

The reductions were the largest piece. An empty subject used to skip the column-wise wrapper
entirely and fall through to the flat builtin underneath, which sees a list with nothing in it and
cannot know which way it was meant to run. They go through the wrapper now, and the ordinary slicing
machinery gives the right shape for free — `sum(zeros(0, 3))` is a 1-by-3 of zeros, `sum(zeros(3, 0))`
is 1-by-0, `sum([], 1)` is 1-by-0 and `sum([], 2)` is 0-by-1. Two pieces had to be added to it: when
there is **no slice at all** the join cannot measure how long a slice's answer would have been, so it
asks for one (which is why `sort(zeros(3, 0))` is 3-by-0 and `diff(zeros(3, 0))` is 2-by-0); and the
0-by-0 with no dimension named is MATLAB's own documented exception, reducing the whole of itself to
a scalar, so `sum([])` is `0`, `prod([])` is `1` and `mean([])` is `NaN`.

`mean`, `median` and `mode` answered an empty with an **error** before this. They answer NaN now,
which is MATLAB's answer and what a script that filtered its data down to nothing depends on.

`max` and `min` are not folds and do not follow that rule. An extreme of nothing is nothing, and
MATLAB gives that nothing a shape: a slice with no elements answers no value, leaving the reduced
dimension zero long, while no slice at all collapses it to one. `max(zeros(0, 3))` is 0-by-3 and
`max(zeros(3, 0))` is 1-by-0.

### An `arguments` block fits an empty to the size it declared

`function y = f(x)` with `x (1,:) double` had been passing `f([])` because `[]` *was* a 1-by-0 row.
Made 0-by-0, it started failing the declaration — and `''` with it, which is what `stess_34`'s
integration section caught. MATLAB does not refuse an empty here; it **reshapes** it, so `f([])`
sees a 1-by-0 against `(1,:)` and a 0-by-1 against `(:,1)`, and `f(zeros(0, 1))` against `(1,:)`
sees a 1-by-0 too. Only an empty with a shape it can give up does this — the shapeless 0-by-0, or a
vector — so `zeros(0, 3)` against `(1,:)` is still the refusal MATLAB makes of it, and a size that
cannot hold nothing still refuses every empty, which is what keeps `(1,1)` meaning "a scalar, and
not `[]`".

### Two crashes on the way

`[] * []` walked off the end of an array — the jagged reader indexes `a[0]` before it counts the
rows. A product over an empty has a shape, and it is not always empty: `zeros(2, 0) * zeros(0, 3)` is
a 2-by-3 of zeros, each element a sum over no terms. `FlattenColumnMajor` had the same first-row
read, and through it so did `filter` and `vecnorm` and everything else that calls it.

## Consequences

**The literal is now correct and so is nearly everything downstream of it.** Roughly 900 forms were
put through `matlab.exe -batch` and this build side by side across fourteen sweeps, each one diffed
line for line. The reduction sweep alone — nineteen names over five empty shapes over three
dimensions — went from **88 of 285** agreeing to **285 of 285 on shape**, with fourteen rows still
answering `double` where MATLAB answers `logical` (recorded below, and older than this milestone).
The general sweeps end exact but for the divergences listed below.

**`''` is the one deliberate trade.** MATLAB distinguishes an empty char by provenance: `''`,
`strcat('', '')`, `['' '']`, `strrep('aa', 'aa', '')` and `upper('')` are 0-by-0, while `blanks(0)`,
`char(zeros(1, 0))` and `sprintf('')` are 1-by-0. A char row here is a .NET string with no shape of
its own, so nothing can tell those apart. `''` is overwhelmingly the one scripts write, so a
zero-length char row reports 0-by-0 and those three named forms now read 0-by-0 where MATLAB reads
1-by-0. That is three forms traded for five, and the common one is on the right side of it.

**Two tests asserted the old behaviour and were rewritten rather than deleted.**
`MatlabPackedFftM96Tests.AnEmptySubjectAnswersTheEmptyArrayMatlabAnswers` carried the divergence in
its own remarks — it is the test that found this — and now carries MATLAB's answers, keeping the
named-shape controls that proved the literal was the cause. `MatlabPackedKernelsM92Tests` asserted
that `mean([])` *refuses*; it asserts NaN now, and is renamed to say so.

**The packed and boxed lanes had disagreed about an empty's shape and now do not.** `ColumnMajorOf`
read a boxed operand's shape off its list of rows, and a list of rows cannot say how wide a matrix
with no rows in it is: `zeros(0, 3) \ b` was 0-by-0 boxed and 0-by-3 packed. Every probe in this
milestone was run under both storage models and diffed against the other before being diffed against
MATLAB.

**This is a semantic change to a value that is everywhere**, which is why it was taken through the
whole four-lane gate rather than a filtered run.

## Divergences recorded here, and left standing

All but the last of these are **older than the literal's shape**: every one reads the same for
`zeros(1, 0)` as for `[]`, so M96b neither caused it nor closes it. They are written down here
because this is the milestone that looked, and nothing else lists them. The last one is a trade
M96b makes, and says so.

- **An empty logical array loses its class.** `logical([])`, `~[]`, `[] == []` and
  `any(zeros(3, 0))` all answer `double` where MATLAB answers `logical`. Storage packs a
  zero-element array as numbers because there is no element to read a kind from, and nothing carries
  the kind alongside. Closing it means threading a packed kind through every empty mint, which is
  its own change.
- **An empty double joined to a logical answers logical.** `[[], true]` is a `logical` here and a
  `double` in MATLAB, where the empty still has its say about the class of the join even though it
  contributes no element to it. It reads the same for `[zeros(1, 0), true]`, so it is the join's
  class rule rather than the literal's shape. The integer rule beside it is already MATLAB's:
  `class([[], int8(3)])` is `int8` in both.
- **Deleting a lone element from a matrix is refused.** `A(3) = []` on a 3-by-3 answers "deleting
  from a matrix takes a whole row or column"; MATLAB flattens the matrix and answers a 1-by-8 row.
  `A(:, :) = []`, which MATLAB reads as deleting every row, is refused for the same reason. So is
  `A(:, j) = <an empty that is not 0-by-0>` — `q = zeros(0, 0); q(:, 1) = zeros(0, 1)` is read here
  as a deletion where MATLAB reads it as a write and answers 0-by-1.
- **The refusals are worded in JGraph's voice, not MATLAB's.** As they are everywhere else: MATLAB's
  "Attempt to grow array along ambiguous dimension" is "Index 9 is past the end of a 2x3 matrix;
  grow it with two subscripts, like A(9, 1)" here, and `end` on an empty complains about the index
  rather than about what an index must be.
- **A scalar does not grow by index.** `q = 5; q(3) = 7` is "cannot assign by index into a number"
  where MATLAB answers `[5 0 7]`. It is a different code path from growth from an array — the
  callee is not an array at all — and M96b did not touch it. A logical subscript in a write
  (`q(true) = 7`) is refused for a related reason.
- **`mat2str` renders every empty as `'[]'`.** MATLAB writes the shape out for the ones that have
  one: `mat2str(zeros(1, 0))` is `'zeros(1,0)'` there and `'[]'` here.
- **`disp([])` prints `[]`.** MATLAB prints nothing at all.
- **A zero-length char row reports 0-by-0 whatever made it.** MATLAB distinguishes them by
  provenance — `''` and `strcat('', '')` are 0-by-0, `blanks(0)` and `char(zeros(1, 0))` and
  `sprintf('')` are 1-by-0 — and a char row here is a .NET string with no shape of its own to tell
  them apart. This one is a trade made *by* M96b rather than inherited: `''` is overwhelmingly the
  form scripts write, so the three named forms give up their 1-by-0 for it.

## Testing

Four lanes, all four green on everything this milestone touches: `linalg=native/managed` ×
`packed=1/0`, 5,840 tests each. The two boxed lanes carry **57 failures that are byte-for-byte the
same set as at HEAD** — volume and N-D graphics tests failing on a boxed-storage defect older than
this work, verified by building HEAD in a separate worktree and diffing the failing test names
rather than by comparing counts. The two packed lanes are clean. All 59 MATLAB stress scripts pass,
including `stess_34`, whose integration section is what found the `arguments`-block regression
above.

`MatlabEmptyLiteralM96Tests` (15) is the regression net, and every expectation in it was read out of
MATLAB rather than derived: the literal's shape and the two empties it is not, the default dimension
pulling them apart, concatenation omitting an empty and settling an all-empty shape, growth by linear
index and by two subscripts and by `:`, deletion whole and partial, an empty subscript's shape, every
reduction's shape and its identity, the extremes, the readers that answer an empty, the product over
one, an `arguments` block's refitting, and the ordinary questions — `isempty`, `numel`, `length`, `ndims`, `size`, `isrow` — which must
read the same as they always did. Each test runs its script twice, packed and boxed, because that is
where the two lanes had disagreed.
