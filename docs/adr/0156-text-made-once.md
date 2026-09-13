# ADR 0156 — Text made once

## Status

Accepted. Item 09 of the head2head_v3 gap-closure plan (`docs/plans/gap-closure-07-12-plan.md`,
rev 6, agreed with Codex): the `d12_concat_200k` row and part of `d12_charmatrix`. Three stages,
each measured alone against the stage before it with the same rig: 09a, one scalar formatter;
09b, a chain of string `+` built once; 09c, `char` of a numeric array written from its buffer.

## Context

`keys = "R" + string(ids') + "-" + sv` over 200,000 ids took 0.211 s where R2025b takes 0.030 s
(`runs\09-before-08b`, the 08b binary re-run on `d12_text` the same day, because the 08b run
folder holds only `d06`). Two things paid for it.

`string(ids')` boxed every element of the packed column and sent each through
`StringElementOf`, which reached `num2str`'s whole matrix road for one number: a row list, the
negative-zero pass, `AlignedRows` choosing a precision and a column width, and a `%.Ng` format
string built and parsed by `FormatMatlab` for the one number, then the padding trimmed off again.

The three `+` then ran as three pairs. Each pair converted both sides to texts, built a fresh
string array of 200,000 new strings, and handed it to the next pair as its left side, which
converted it back to texts again; two of the three answers were thrown away the moment the next
pair had read them.

`char(65 + mod((r - 1) + (0:39), 26))` in the charmatrix loop cast each code point through
`Glyph`, which read the element through `ElementAt`, boxed it, and made a one-character string
per character before a `StringBuilder` joined them.

## Decision

**09a — one scalar formatter.** `JgsSprintf.FormatScalarGeneral(double)` is what `num2str` wrote
for a lone number, written once as a function of the double: `%g` at the precision
`AlignedRows` picks for a one-element row (as many digits as a whole number has; four past a
fraction's leading digit, five at least, sixteen at most), a negative zero as `0`, the
infinities spelt, and NaN as `NaN`. A whole number below 10^15 whose digit count fits the
precision is written from its integer, which is what `%g` writes for an exact integer it has room
for; anything else, and any whole number where the logarithm's floor undercounts its digits, goes
through `FormatGeneral` with the same precision, which now takes its `"G<n>"` picture from a
table instead of concatenating one per call. `StringElementOf` reaches it for every number, NaN
staying the missing string in front of it, so `string`, string `+`, and every other road through
that rule share it. `string(x)` of a packed real array iterates the buffer directly: each double
through the formatter, NaN as the missing string, a packed logical as `true` or `false` — the
elements the boxed arm built from `BoxedElements`, shaped by the same `ShapedLike`. The class tag
selects nothing: the boxed road read an `int32` or a `single` element as the double it holds, and
so does this one.

The contract was byte equality with the road it replaces. `ScalarTextM156Tests` holds the
formatter to `num2str`'s one-number road itself over 819,502 values: every integer decade to
10^22 and power of two to 2^64 with their neighbours, every power of ten a double holds with its
neighbours, the rounding boundary into the next decade at every precision from 1 to 16, 0.1-step
decimals, negative zero, the infinities, and pseudo-random doubles by bit pattern, by log-uniform
magnitude and as whole numbers, with both signs. The packed `string` is held to the boxed road's
elements and shape over rows, columns, matrices and logicals.

**09b — a chain of string `+` built once.** `JgsBuiltins.StringConcatChain` holds the texts and
shape of every operand of a chain and builds each answer string once; `ConcatenateStrings` is its
two-operand case, with the pair's own loop kept for it. The interpreter reaches it from
`EvaluateBinary`: a `+` whose left operand is another `+` folds its whole left spine
(`EvaluatePlusChain`, `FoldPlus`), evaluating each operand exactly where the pairs evaluated it and
applying each pair the moment its right operand exists. A pair joins the chain only when it would
have gone straight to `ConcatenateStrings` — one side a string array and neither side an operand
that `ApplyBinaryCore` claims first (a struct, an object, a sparse matrix, a time; `JoinsAsText`
lists the rest, and a comment at the concatenation check says a new claim must take its operand off
that list). Every other pair is `ApplyBinary` as before, a pending chain built first to be its left
side. `StringConcatChain.Append` converts the new operand and checks its shape against the answer
so far on the spot, which is every check that pair made, in its order, at its node: converting the
answer so far could not throw, because it was a string array.

Nothing anything can see has moved. No operand is evaluated or read earlier or later than it was,
and no error changes its text, its node or its place in the order; what is gone is the intermediate
string arrays, which were temporaries of one expression. Two details of the pairs are kept on
purpose. An intermediate answer whose text spelled the missing sentinel was read back by the next
pair as the missing string, so `"<miss" + "ing>" + "x"` stays missing. And the outermost pair of a
chain that is not text still reports whether its answer is fresh, so `y = a + b + c` over numbers
keeps the copy elision `EvaluateForBinding` gave it.

The plan allowed fusion only when every operand was pure — a variable, a literal, or `string`,
`num2str` or `char` of one — so that flattening the chain could not reorder side effects. That
restriction answers a design that evaluates the operands first and checks them afterwards; this one
never evaluates an operand before the pairs did, so it is not needed, and it would have excluded
the benchmark's own operand, `string(ids')`, whose argument is a transpose. `ScalarTextM156Tests`
holds the chain to the pairs on the same text with the fold switched off and on
(`StringConcatChain.Enabled`): every chain of three over seventeen kinds of operand (4,913 chains),
every chain of four over eight of them, parenthesised and mixed chains, an incompatible pair ahead
of an operand that would set a variable (it is never evaluated) and ahead of an undefined function,
operands that draw from the random generator (the draws land where they did), and the benchmark
expression, which is built exactly once.

**09c — `char` of a numeric array written from its buffer.** `PackedGlyphs` reads a packed array's
codes where they lie and makes each row's text once: a matrix of more than one row stacks its rows,
read row-major as before, and anything else is one row in storage order. The cast is `Glyph`'s own,
`(char)(int)` on the double, with no range check, because `Glyph` never made one: `char([65536
65537])` is codes 0 and 1 on both roads, and R2025b's saturation stays a recorded divergence rather
than a change made inside a performance stage. A logical slot reads as 1 or 0, which is what
`ElementAt` boxed it as, and the shape rules, 0-by-3 included, are untouched. `ScalarTextM156Tests`
holds the packed road to the boxed element loop, and both to the cast itself, over fractional,
Unicode, out-of-range, negative, NaN and infinite codes, a matrix holding out-of-range codes, a
column, logicals and three empty shapes.

**The fixture came first.** `m156_text` was recorded from R2025b before any code moved, and run on
the untouched 08b binary: the `string` sweep of single numbers, decades, powers of two, tenths,
shapes, every integer class, `single`, logical and missing; five sweeps of 20,000 to 40,001
elements as bits lines against MATLAB's own bytes; every chain of three string `+` operands over
eleven kinds of operand in every position (819 lines) and seven longer or nested chains; an
incompatible pair ahead of a state-changing operand and ahead of an undefined one; and `char` of
fractional, Unicode, out-of-range, negative, NaN, Inf, matrix, column, empty, integer-class,
`single` and logical codes. Everything on which JGraph already disagreed with MATLAB is a
`div=ADR0156` line, listed below; none of them is new, and the 08b binary and the 09a, 09b
and 09c builds answer all 1,250 lines the same way.

## Consequences

**09a, measured alone** with the rig, five counterbalanced repeats of `d12_text` and of the two
probes, against the 08b binary run the same day (`runs\09-before-08b`; `runs\09a-scalar-formatter`):

| row | scope | before | 09a | speedup | J/M before → after |
| --- | --- | --- | --- | --- | --- |
| `d12_concat_200k` | benchmark row | 0.211 s | 0.049 s | 4.31× | 7.54 → 1.76 |
| `concat` | probe, cold / warm | 0.281 / 0.224 s | 0.081 / 0.056 s | 3.5× / 4.0× | 9.95 / 6.57 → 2.92 / 1.69 |
| `charmatrix` | probe, cold / warm | 0.047 / 0.019 s | 0.047 / 0.019 s | 1.0× | 8.16 / 8.26 → 8.88 / 8.00 |
| `d12_total` | script | 1.912 s | 1.656 s | 1.15× | 0.22 → 0.16 |

The script's allocation fell from 1,374 MB to 1,190 MB and its peak working set from 868 MB to 730
MB; the concat probe's process allocation fell from 1,426 MB to 690 MB and its peak working set
from 882 MB to 578 MB. Two rows whose code this stage does not touch also moved,
`d12_predicates_200k` (0.116 → 0.058 s) and `d12_extract_200k` (0.057 → 0.038 s); both lie inside
or at the edge of their before-run's own range (0.063–0.125 s and 0.037–0.066 s), and no claim is
made for them. The `d12_charmatrix` row's median moved from 0.037 to 0.028 s inside the same kind
of range while its probe did not move at all.

Bits: `head2head_v3\bits\bits_d12_text.m` on the 08b binary and this one — the 200,000 composed
keys, the string of their ids, `string` over fractions, scaled and whole sweeps, tenths, powers,
every class and a matrix, every chain of three and four operands over thirteen kinds of operand
(4,394 chains), and the codes of the charmatrix row's 2,000 rows and of every edge input of
`char`: all 15 lines equal.

**09b, measured alone** against the 09a binary the same way (`runs\09b-concat-chain`):

| row | scope | 09a | 09b | speedup | J/M before → after |
| --- | --- | --- | --- | --- | --- |
| `d12_concat_200k` | benchmark row | 0.049 s | 0.033 s | 1.48× | 1.76 → 1.19 |
| `concat` | probe, cold / warm | 0.081 / 0.056 s | 0.056 / 0.050 s | 1.45× / 1.12× | 2.92 / 1.69 → 2.07 / 1.62 |
| `d12_recombine_50k` | benchmark row | 0.009 s | 0.006 s | 1.50× | 1.12 → 0.86 |
| `d12_total` | script | 1.656 s | 1.602 s | 1.03× | 0.16 → 0.19 |

`d12_recombine_50k` is `head(1:50000) + "|" + tail(1:50000)`, a chain of three, and the fold
reaches it with no change of its own. The concat probe's process allocation fell from 690 MB to 473
MB and its peak working set from 578 MB to 470 MB; the script's allocation from 1,190 MB to 1,129
MB. `d12_predicates_200k` read 0.058 s on 09a and 0.072 s here, inside two overlapping ranges
(0.054–0.073 s and 0.051–0.089 s) of a row whose code neither stage touches.

Over the two stages the row is 6.4× faster than before item 09 (0.211 → 0.033 s, J/M 7.54 → 1.19),
and the probe's allocation is a third of what it was (1,426 → 473 MB).

Five alternating rounds of `bits\chain_numeric_timing.m` on the two binaries
(`runs\09b-concat-chain\chain_timing.log`) say what the fold costs a chain that is not text: a
million interpreted `a + b + c + d` over scalars take 0.0222 s on 09a and 0.0220 s here, twenty
thousand `A + B + C` over 1,000-element rows 0.162 and 0.156 s, and two hundred thousand `s + "a" +
s` over one-character strings 0.297 s and 0.263 s.

Bits: `bits_d12_text.m` on the 08b binary and this one, all 15 lines equal — among them the 4,394
chains of three and four operands, which the 08b binary built pair by pair with the original loop.

**09c, measured alone** against the 09b binary the same way (`runs\09c-packed-char`):

| row | scope | 09b | 09c | speedup | J/M before → after |
| --- | --- | --- | --- | --- | --- |
| `charmatrix` | probe, cold / warm | 0.048 / 0.019 s | 0.041 / 0.014 s | 1.16× / 1.36× | 9.02 / 8.30 → 8.18 / 6.00 |
| `d12_charmatrix` | benchmark row | 0.026 s | 0.023 s | 1.13× | 6.00 → 5.75 |
| `d12_total` | script | 1.602 s | 1.618 s | 0.99× | 0.19 → 0.20 |

The probe's ranges do not overlap (cold 0.046–0.063 s before and 0.040–0.043 s after, warm
0.019–0.026 s and 0.014–0.015 s); the row's do (0.021–0.028 s on both), so the row claims nothing
its probe does not. The probe's process allocation fell from 129 MB to 94 MB and its peak working
set from 189 MB to 153 MB; the script's allocation from 1,129 MB to 1,120 MB. Of the row's kernel
only the 2,000 calls of `char` on a row of 40 codes changed: `char(rowsc)` stacking the cell, the
transpose and `double` are untouched, and the loop that builds the rows is item 12's, which is why
the warm probe still takes six times MATLAB's.

The same run read `d12_concat_200k` at 0.037 s against 0.033 s, and most rows of the script a few
percent slower, MATLAB's total among them (7.89 to 8.22 s), though no `char` of a number lies on
the concat row's path. Five alternating rounds of `d12_text` on the two binaries
(`runs\09c-packed-char\ab_09b_09c.log`) settle it: `d12_concat_200k` 0.034 s on both,
`d12_compose_200k` 0.238 and 0.237 s, `d12_edits_200k` 0.078 and 0.079 s, `d12_total` 1.519 and
1.508 s, and `d12_charmatrix` 0.029 and 0.021 s. The move was the machine between runs, not the
stage.

Over the three stages `d12_concat_200k` is 6.4 times faster than before item 09 (0.211 to 0.033 s,
J/M 7.54 to 1.19), the concat probe's allocation is a third of what it was (1,426 to 473 MB), the
charmatrix probe is 1.16 times faster cold and 1.36 times warm, and the script allocates 1,120 MB
where it allocated 1,374 MB.

Bits: `bits_d12_text.m` on the 08b binary and this one, all 15 lines equal — among them the codes
of the charmatrix row's 2,000 rows and of every edge input of `char` as a row, a matrix, a column,
`single` and logical, which the 08b binary wrote element by element through `Glyph`.

## Divergences

Every one of these was found by `m156_text` on the binary before item 09 and is kept by it.

- `string` of a numeric array: R2025b takes one precision for the whole array from its largest
  magnitude, as `num2str` does for a matrix, and writes `string([9016.9943749474514 12345.5])` as
  `9016.99437` where JGraph writes each element at its own precision, `9016.9944` (`m156_text`,
  `array_precision`, `array_precision_small`, `div=ADR0156`). 09a kept each element's own
  precision byte for byte, which was its contract.
- `string` of a `single`: R2025b writes the single's double value to fourteen significant digits,
  `3.1415927410126`, where JGraph writes it as `num2str` writes the double, `3.1416` (`m156_text`,
  `cls_single`, `div=ADR0156`).
- `string` of an `int64` or `uint64` beyond 2^53: JGraph holds the value as a double and writes
  `string(intmax('int64'))` as `9.223372036854776e+18` where R2025b writes every digit
  (`m156_text`, `cls_int64_max`, `cls_uint64_max`, `div=ADR0156`). Narrow integer storage is
  outside the plan.
- A char row meeting a number, NaN, a logical, a char matrix, a cell or another char row under `+`:
  JGraph joins their text, so `'x' + 1 + "y"` is `"x1y"`, where R2025b adds code points first,
  `"121y"`, and refuses a cell (`m156_text`, the 55 `chain_chr_*` and `chain_*_chr_*` lines over
  those operands, `chain4_char_head`, `div=ADR0156`).
- A string whose text spells the missing sentinel: `"<miss" + "ing>" + "x"` is the missing string
  in JGraph, whose missing string is that text, and `"<missing>x"` in R2025b (`m156_text`,
  `chain_sentinel_text`, `div=ADR0156`).
- `char` of a code outside 0 to 65535: R2025b saturates (Inf and 1e10 to 65535, a negative to 0)
  where JGraph casts to an integer and keeps its low sixteen bits (65536 to 0, −1 to 65535, Inf and
  1e10 to 0) (`m156_text`, `char_wide`, `char_negative`, `char_inf`, `char_huge`, `char_int8`,
  `div=ADR0156`). The plan keeps the cast exactly, and a range check is a compatibility decision of
  its own.
- `char` of a logical: R2025b refuses it and JGraph answers codes 1 and 0 (`m156_text`,
  `char_logical`, `div=ADR0156`).
- `char(zeros(0, 3))`: 0-by-3 in R2025b, 0-by-0 in JGraph (`m156_text`, `char_empty_0x3`,
  `div=ADR0156`).
