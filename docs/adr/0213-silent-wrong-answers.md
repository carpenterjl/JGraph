# ADR 0213 — Silent wrong answers: open items 2, 3, 7, 8, 9, 10, 22/54 and 42

## Status

Accepted, 2026-10-08. The second batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), taking the items that changed a number, a class or a shape without any error — the
divergence remediation plan's Tier 1 (items 1 and 2 there, and four rows of its item 3). It
retires divergences recorded by ADRs 0156, 0159, 0160 and 0208. Every expectation was recorded
from R2025b (probes `probe_b2`, `probe_b3` and the edge probes in the open-items scratch, then the
fixture `oi_silent_answers`).

## Context

Each of these gave a plausible answer that was not MATLAB's: `'a' + 1` was `'a1'`; `f(k) = x(k) > 0`
in a loop turned a double vector logical; `accumarray` answered rows; `string([9016.99437 12345.5])`
lost a digit MATLAB keeps; `x(2:0.5:3)` was refused and `uint8(254):258` saturated into a run of
255s. A script ported from MATLAB ran, and was wrong.

## Decision

### A MATLAB char is its codes (open item 2, decision D2)

In the MATLAB dialect a char row or char matrix meeting a number, a logical, a complex value, a
numeric array or another char under any arithmetic, comparison or logical operator is the double
array of its codes, in its shape (`ApplyBinary`, before every other arm): `'a' + 1` is 98,
`'123' - '0'` is `[1 2 3]`, `'abc' == 'abd'` is `[1 1 0]`, `'a' + int8(1)` is an int8 98 by the
ordinary class rules, `'ab' + 'cde'` is a size error. `+` joins text only with a string array,
which claims the pair first, so `'x' + 1 + "y"` is `"121y"` as in R2025b. `-'a'` and `~'ab'` read the
codes too. `'a' + {1}` is R2025b's `MATLAB:math:mustBeNumericCharOrLogical`. The JGS dialect keeps
`+` joining text (decision D2, 2026-10-03), the decision ADR 0063 deferred.

Found on the way: `v(:)` of a char row answered the row, so `v(:)'` was a column and `strrep(v(:)', …)`
failed. A char row flattens to a char column now.

### A logical stored into a numeric array takes the array's class (item 9)

`f = zeros(1, n); f(k) = true` demoted the packed buffer to boxed elements, and an array whose every
element was boolean read as logical. In the MATLAB dialect a logical scalar or array stored into a
non-empty numeric array is 1 and 0 of that array, through both the one-subscript write
(`LogicalAsNumbers`) and the element write (`WriteElement`), in both representations. An empty `[]`
takes the class of what is stored in it, as R2025b's does.

### A zero step is an empty range, and an empty range keeps its class (item 9)

`1:0:5` is 1-by-0 and a loop over it runs no times; `int8(1):int8(0):5` and `int8(5):1` are empty
int8 rows. The JGS dialect keeps refusing a zero step.

### A colon written as a subscript (item 8)

A colon with a fractional operand used directly as a subscript warns `MATLAB:colon:nonIntegerIndex`
once and rounds each element half away from zero, even when every element comes out whole
(`x(1:2.5)`), in both subscript paths (`RoundedIndexRange`). An integer-classed colon whose start or
stop lies outside the class is `MATLAB:colon:OutOfRange` (`RequireColonInClass`, for a range and a
`for` loop alike); the step is not checked, as `uint8(1):300:2` shows in R2025b.

### accumarray's shape follows its subscripts (item 7)

Each row of `subs` is one subscript: a column gives a column (and a size must be `[N 1]`, else
`MATLAB:accumarray:badSzInputVecInd`), a k-column matrix a k-dimensional array, a row one subscript
of that many dimensions. A function sees each bin's values as a column. The JGS dialect keeps its
row of bins.

### The reductions take complex input (item 10)

`sum`, `prod`, `mean`, `cumsum`, `cumprod` and `diff` fold complex arrays serially, along any
dimension, with `'all'`, a vector of dimensions, `'omitnan'` and `'reverse'`; the answer is real when
every imaginary part is zero, and a single stays single. A fractional dimension is
`MATLAB:getdimarg:invalidDim`.

### string() writes an array at one precision (item 3)

R2025b writes every element of a real array at `ceil(log10(m)) + 4` significant digits for the largest
finite magnitude `m`, five at least and sixteen at most (`JgsSprintf.ArrayPrecision`), through
`string`, a string `+` an array, and both representations. A scalar keeps its own precision, which is
the same formula. The item's claim that `string(single(pi))` is `3.1415927410126` came from an array
of singles whose largest element is `1e10`; a lone single is `3.1416`, as here.

### func2str (items 22 and 54)

A named handle is its bare name in the MATLAB dialect (`func2str(@sin)` is `'sin'`). An anonymous
function is the text the parser saw (`AnonymousFnExpr.MatlabText`): no whitespace outside quotes, a
space between elements of a bracket or brace literal written as a comma, every literal as written
(`1e-3`, `"s t"`). The JGS dialect keeps its own spelling.

### true and false take any number of sizes (item 42)

`true(2, 2, 3)`, `false([2 3 4])`, trailing singletons dropped, a negative size as 0, a fractional size
`MATLAB:NonIntegerInput`, and `'like'` with a logical prototype; any other prototype is
`MATLAB:True:invalidInputClass` (or `False`).

### Not valid, closed

Open item 61 (`mat2str([])`): R2025b answers `'[]'`, as JGraph does; the item misremembered. Its one
residue, `mat2str(char(zeros(0, 3)))`, belongs to item 31 (a real 1-by-0 and 0-by-n char).

## Measured

- `oi_silent_answers`: 129 lines, all exact, in both representations.
- Retired to exact lines: 59 in `m156_text`, two each in `m159_slices` and `m160_loops`, one in
  `u9b_bridge`.

## Divergences

- `true(n, 'like', p)` with a sparse prototype answers a dense logical, and takes any sparse
  prototype, because this build's sparse storage keeps no logical flag (`oi_silent_answers`,
  `true_like_sparse` checks the class only).
- A subscript refusal (`x(2.5)`, `M(1.5, :)`) and a size mismatch (`'ab' + 'cde'`) keep JGraph's words
  and no identifier; open item 79 and the remediation plan's item 7 give them R2025b's.

## Consequences

Ported scripts that do arithmetic on characters, accumulate flags in a preallocated double vector,
index with a computed colon or sum complex data now get MATLAB's answers. A MATLAB-dialect script that
compared two char rows with `==` and relied on one logical answer now gets R2025b's elementwise answer
or size error; `strcmp` is the MATLAB idiom and is unchanged.
