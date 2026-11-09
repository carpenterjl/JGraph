# ADR 0326 — The every-row plan's I7: string arrays a block per thread

## Status

Accepted, 2026-11-09. Stage I7 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 6). Measured on the working tree over d951e9b4 (Release, `runs\i7-first` in
`JGraph_demo_workspace\head2head_v3`, its manifest hashing the dirty files) on the i7-12700H, with the
user away and their standing permission for this session's timing runs.

## Context

The GUI round (ADR 0322) left nine d12 rows to I7: `compose` (0.144 s against R2025b's 0.084),
`num2str` (0.017 / 0.013), the string `+` chain (0.069 / 0.021), `extractBefore`/`extractAfter`
(0.147 / 0.036), the recombination (0.021 / 0.006), `contains`/`startsWith`/`endsWith` (0.086 /
0.003, the plan's widest ratio), the edits (0.096 / 0.048), `pad`/`strtrim`/`upper`/`lower` (0.038 /
0.017) and the char matrix block (0.019 / 0.002). A split probe of each step
(`probes\instrumented\split_i7_d12.m`, both engines in one session each) showed where the time went
on the I2 binary:

- **The predicates** scored each element through a closure (`Array.Exists` captured the text) into
  a boxed true or false per element, so `sum` of the answer then walked 200,000 boxed values
  (4.3 ms against R2025b's 0.0).
- **The editing verbs** `upper`, `lower`, `erase` and `replace` reached a string array through the
  elementwise retrofit (`MapOverText`), which demotes each element and calls the whole builtin once
  per element.
- **`compose`** boxed every number, built a list per row and parsed the format again for each row;
  `string(x)` of a packed array read each slot through a boxed number; `num2str(x, fmt)` formatted
  row by row through `FormatMatlab`.
- Everything else (the `+` chain, extraction, insertion, padding, trimming) was already one loop
  over a `string[]`, on one core.
- **The char matrix block is not text.** `rowsc{r} = char(65 + mod((r - 1) + (0:39), 26))` costs
  8 µs a pass against R2025b's 0.7: every statement that touches a 40-element vector leaves the
  compiled loop for the walk, and a walked builtin call costs 1 to 4.4 µs through its wrapper layers
  (`split_i7_small.m`, `split_i7_call.m`: `numel(v)` 1.3 µs, `sum(v)` 3.5 µs, `x = 0:39` 1.3 µs;
  `v(3)` 0.016 µs and `abs(-3)` 0.038 µs inside the compiled loop).

## Decision

### A block per thread (`JgsTextKernels`)

`JgsTextKernels.For` cuts `[0, n)` into 16K-element blocks and runs them through
`ParallelKernels.ForBlocks` when there is more than one. Only maps take it: each element's answer is
a function of that element and of arguments read once before the loop, nothing is folded across
elements, and nothing in the body can throw, so the answer is the same at one thread and at sixteen
and an error is still the first element's. `JGRAPH_TEXT_FAST=0` turns every I7 road off. The block
bodies and the formatter's per-element leaves (`RenderNumber`, `RenderFloat`, `RenderInteger`,
`PadNumber`, `FormatAtPrecision`, `FormatGeneral`) are `AggressiveOptimization`, so a 200,000-element
map does not run its first call on tier-0 code (ADR 0322's lesson).

### The roads

- **Predicates** (`contains`, `startsWith`, `endsWith`, `matches`, `count`): a string array is
  scored where its elements lie, one sweep straight into a packed logical (or double, for `count`)
  buffer, with no text array read out first; the score functions loop over the patterns without a
  closure. Other containers keep the bundle road, which now also writes packed answers.
- **`strlength`, `pad`, `strtrim`/`deblank`, `extractBefore`/`extractAfter`,
  `insertAfter`/`insertBefore`**: their per-element loops go through `For`; extraction and insertion
  only when every place is a marker (a position can be out of range, and that error stays serial).
  Reading a string array's texts and rebuilding one (`TryReadText`, `RebuildText`) go through it too.
- **`upper`, `lower`, `erase`, `replace`** over a string array or a cell of char: when the body is
  one string operation with arguments that cannot fail (`DirectTextMap`: `CaseMapped`'s expression,
  `erase` and `replace` with one pattern), it is applied directly, a block per thread; a pattern list
  or any other arity still calls the body per element.
- **The string `+` chain** (ADR 0156's fused build) builds its elements a block per thread; its
  string operands are read the same way.
- **`string(x)`** of a packed real array reads the buffer directly and formats a block per thread.
- **`compose` and `num2str`**: a format with exactly one numeric conversion and nothing that reads
  the argument list (no `*`, no `n$`) is parsed once (`JgsSprintf.SingleNumeric`); `compose` over one
  packed real column and `num2str` with one number per row apply it a block of rows per thread. It
  writes `prefix + RenderNumber(...) + suffix`, the text `FormatMatlab` writes for that format and one
  value; anything else (two conversions, `%s`, `%c`, a star, a position, a repeated flag, a row
  vector, a matrix) keeps its road and its errors. `%f` reads its .NET picture from a table rather
  than concatenating `"F" + places` per element.

### Measured

`measure_i7.ps1 -Stamp i7-first` and `compare_i7.py`: d12 as written, one warm-up and three timed
passes per session, I7 on and off (`JGRAPH_TEXT_FAST=0`, the same binary) alternated three times
headless, then one JGraph.Application session per condition; medians of the timed passes, seconds.
The steady-state column is a second measurement: eight passes per session, two sessions per
condition alternated, the median of passes 4 to 8.

| row | R2025b GUI | JGraph GUI (8dbad495) | headless off | headless on | app off | app on | steady off | steady on |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `d12_compose_200k` | 0.084 | 0.144 | 0.094 | 0.043 | 0.173 | 0.013 | 0.088 | 0.009 |
| `d12_num2str_20k` | 0.013 | 0.017 | 0.007 | 0.006 | 0.011 | 0.003 | 0.006 | 0.003 |
| `d12_concat_200k` | 0.021 | 0.069 | 0.054 | 0.016 | 0.056 | 0.016 | 0.037 | 0.012 |
| `d12_extract_200k` | 0.036 | 0.147 | 0.030 | 0.016 | 0.050 | 0.015 | 0.026 | 0.013 |
| `d12_recombine_50k` | 0.006 | 0.021 | 0.006 | 0.004 | 0.005 | 0.004 | 0.005 | 0.004 |
| `d12_predicates_200k` | 0.003 | 0.086 | 0.037 | 0.004 | 0.047 | 0.005 | 0.031 | 0.004 |
| `d12_edits_200k` | 0.048 | 0.096 | 0.050 | 0.037 | 0.061 | 0.031 | 0.041 | 0.020 |
| `d12_pad_case_50k` | 0.017 | 0.038 | 0.020 | 0.022 | 0.026 | 0.015 | 0.015 | 0.009 |
| `d12_charmatrix` | 0.002 | 0.019 | 0.007 | 0.016 | 0.026 | 0.019 | 0.005 | 0.006 |
| `d12_regexprep_20k` | 0.104 | 0.063 | 0.069 | 0.139 | 0.144 | 0.187 | 0.049 | 0.062 |
| `d12_regexp_match_5k` | 0.021 | 0.012 | 0.012 | 0.027 | 0.045 | 0.045 | 0.011 | 0.012 |
| `d12_unique_text_200k` | 0.043 | 0.115 | 0.118 | 0.174 | 0.210 | 0.153 | 0.115 | 0.126 |
| `d12_total` | 1.802 | 1.235 | 0.841 | 0.924 | 1.433 | 0.931 | 0.766 | 0.579 |
| `d12_text` wall | 1.821 | 1.240 | 0.853 | 0.940 | 1.437 | 0.955 | - | - |

**Where the rows stand.** `compose`, `num2str`, the `+` chain, extraction, the recombination and
the edits are under R2025b in every session. `pad_case` is under in the app and at steady state
(0.009) but 0.022 headless over three passes (its first pass on tier-0 code). Two rows miss:

- **`d12_predicates_200k`, 0.004 against 0.003.** The three predicates cost 1.5, 0.9 and 1.1 ms a
  call at steady state (`split_i7_pred.m`; R2025b 0.77, 0.34 and 0.24). A C# micro-benchmark of the
  layout (200,000 holder objects the size of a `JgsValue`, each with its 15-character string, scanned
  by `Parallel.For`) puts the floor at 0.8 to 1.1 ms a predicate at any thread count from 6 to 20 and
  any block from 8K to 64K: each element is a wrapper and a string, about 32 MB streamed per call,
  which is this machine's memory bandwidth. A compact `string[]` is no better than 0.7 ms. The row
  is at the boxed representation's floor; open item 257 is the packed string storage that removes the
  wrapper.
- **`d12_charmatrix`**: not a text row (see Context). Open item 258.

**What I7 costs the rows after it.** With I7 on, the regexp rows lose at passes 1 and 2
(`regexprep` 0.139, `regexp_match` 0.027 over three passes, against 0.069 and 0.012 off): every
pass now reaches them sooner, before the background JIT has promoted their code. Under
`DOTNET_TC_CallCountingDelayMs=0` they match the off build (`regexp_match` 0.012-0.015 on against
0.010-0.014 off), so the three-pass loss goes to I13. What is left at steady state is the GC's: the
arrays I7 builds in a few milliseconds are promoted by the next allocating row instead of inside the
slow builders, so `regexprep` pays 10-15 ms (0.062 against 0.049) and `unique_text` about 11 ms; GC
settings (`gcConcurrent=0`, `GCHeapCount=6`, a larger gen0) did not change it. `regexprep` stays
under R2025b at steady state; `unique_text` is I6's row.

### Checks

- `TextKernelsI7Tests` (10): every case run with the roads off, on at all threads and on at one
  thread, the same bytes and outcome, and the road taken by the counter: the predicates over 40,000
  keys with missing strings, pattern lists, `IgnoreCase`, empty patterns, a transposed and an N-D
  subject, cells, char rows, empties and the refusals; `strlength`; `compose` over sixteen formats
  and NaN, the infinities, -0, 1e300, logical, `int32` and `single` columns, and the shapes it leaves
  to its road with their errors; `num2str` with formats; `string(x)`; the `+` chain with expansion,
  cells, numbers and the spelt sentinel; extraction and insertion by marker, by per-element markers
  and by position with the out-of-range error; padding, trimming, case and edits on string arrays and
  cells; and d12's own rows at 200,000 with every check printed.
- I7's class, the text and format classes (string arrays, text family, char matrices, string
  options, format escapes, scalar text, regex parity, the stress-wave and empty-literal classes and
  their neighbours) and the MATLAB parity suite in both storages (`JGRAPH_JGS_PACKED=1` and `0`):
  823 of 823 in each (in boxed storage `compose`'s column road, which reads a packed buffer, is not
  taken, and the answers are the same).
- `run-stress.ps1` over the Debug build: 88 of 89, `stess_40` (open item 254, before I7).
- Every d12 CHK line (18) equal to the GUI round's JGraph pass 1 in all eight sessions.
- The seven verify scripts green; the harvest unchanged.

## Consequences

- The plan is at rev 6 with I7 partly done: seven of its nine rows at or under R2025b (`pad_case`
  at steady state and in the app), the predicates at the boxed layout's bandwidth floor, the char
  matrix block handed on as a loop-compiler matter.
- Open items 257 (packed string storage) and 258 (small-vector statements in loops) filed.
- I13 gains the regexp rows' passes 1-2 as evidence: a stage that makes earlier rows faster moves
  the later rows' tier-up into the timed passes.
- The rig gained `measure_i7.ps1`, `compare_i7.py` and the split probes `split_i7_d12.m`,
  `split_i7_pred.m`, `split_i7_cm.m`, `split_i7_small.m` and `split_i7_call.m` (outside the
  repository). The runtime's own sampling profiler (`DOTNET_EnableEventPipe` with
  `Microsoft-DotNETCore-SampleProfiler`) read with the cached TraceEvent package works offline, but
  its exclusive column is biased to call and return sites: it put 78% of `sum(v)` in a one-element
  `ToArray` that measured at 5% when removed. Read its inclusive column, and confirm by removal.

## Divergences

None. Every road gives the bytes its serial road gave.

## Still open

- `d12_predicates_200k` at 0.004 against 0.003 (item 257).
- `d12_charmatrix` (item 258).
- The regexp rows' passes 1-2 and `pad_case`'s first pass: I13.
