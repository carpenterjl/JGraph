# ADR 0327 — The every-row plan's I6: sets, grouping and text order without a box per element

## Status

Accepted, 2026-11-09. Stage I6 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 7). Measured on the working tree over 1465cf65 (Release, `runs\i6-first` in
`JGraph_demo_workspace\head2head_v3`, its manifest hashing the dirty files) on the i7-12700H, on the
Silent power plan every earlier stage was measured on, with the user away from the machine for the
run.

## Context

The GUI round (ADR 0322) left six rows to I6: `unique` with three outputs over 2M whole-number keys
(0.426 s against R2025b's 0.088), `accumarray` over the same keys (0.023 / 0.009), `setdiff`,
`intersect` and `ismember` on 1e5 keys in a 5000-wide range (0.041 / 0.006), `sortrows` of a
500k-by-3 matrix by one column (0.022 / 0.016), and `unique` and `sort` over 200,000 strings
(0.115 / 0.043 and 0.078 / 0.038). Reading the code and a split probe
(`probes\instrumented\split_i6.m`) showed where each went:

- **`unique`** boxed one `JgsValue` per element on the way in (`Arr`), sorted two million ranks with
  the library sort on one thread, and answered with boxed elements.
- **The set operations and the two-output `ismember`** made an array of one boxed element per key
  and settled every question with a comparison delegate over those; the one-output `ismember`
  boxed each candidate for a binary search.
- **`accumarray(keys(:), vals(:))`** copied both arguments twice (`x(:)` clones the buffer, then
  `ToDoubles` copies it again) and made three passes; the two `x(:)` copies alone were a third of
  the row.
- **`sortrows`** was already a rank sort across threads (ADR 0158); what was left was a serial rank
  fill, a remap that one key does not need, and a gather that waited for two million elements
  before threading.
- **Text `sort` and `unique`** sorted positions through a comparison delegate calling
  `string.CompareOrdinal`; `unique` also made an array of one string per element first.

## Decision

### Whole-number keys read a table (`JgsSetKernels`)

A packed array of plain doubles (no logical kind, integer class, char, time or categorical tag, no
higher dimensions, no growth slack) is read where it lies. `TryWholeRange` scans it in blocks for
its least and greatest value and for anything that is not a whole number an `int` holds; NaN, the
infinities and a negative zero all fail the scan, and the call goes to the road it always took,
which is where their rules live (a NaN its own group; the two zeros told apart by `unique` and not
by the set operations). When the range is no wider than 64K keys or than the input is long, one
`int` table over the range answers the call:

- **`unique`**: one pass marks each key's first (or last) position; an ascending walk of the table
  is the sorted answer and `ia` (the groups sorted by first position under `'stable'`); a second
  pass, in blocks across threads, reads every element's place for `ic`. Other packed doubles skip
  the boxing and take the ranked grouping directly (`GroupNumbers` over the buffer), whose sort is
  now `SortKernels.SortRanks`, threaded above its threshold.
- **`union`, `intersect`, `setdiff`, `setxor`** (sorted, not `'rows'`): one table per side marks
  first positions; one ascending walk picks the values and the positions each side contributes,
  exactly as the general road names them. The answer is shaped by the same rule (a row when the
  left set is a row and, except for `setdiff`, the right one too).
- **`ismember`**, one output or two: a table over the set; each element of the subject, which may
  hold anything, is a member when it is a whole number in range whose slot is marked. The answer is
  a packed logical in the subject's shape.
- **`accumarray(subs, vals)`** with a packed column of subscripts, one packed value a row and no
  other argument: the scan proves the subscripts, then one pass sums into the bins in arrival
  order, storing a bin's first value rather than adding it to zero, which is the general road's
  arithmetic to the last bit. A subscript that is not a whole number from 1 up falls through to the
  road that refuses it.

`JGRAPH_SET_FAST=0` turns every I6 road off.

### `x(:)` is a counted share

In the MATLAB dialect, with the ownership model on (ADR 0162), `x(:)` of a packed array is now a
second wrapper over the same buffer with the buffer's holder count raised, shaped as a column,
where it was a clone. Whichever of the two is written first takes its own copy then (M3), and a
temporary's buffer is never reused or disposed while the count says someone else holds it (M6,
Z2a's `Reusable`). The wrapper is the one the clone had: the buffer's kind and no other tag. An
array with growth slack is still cloned, since its storage is laid out by its own wrapper. A dead
wrapper never decrements, so after `f(x(:))` the first later write to `x` copies once; that is the
copy the clone made every time, now made only when something is written.
`JGRAPH_COLON_SHARE=0` puts the clone back.

### `sortrows` by one key

One key is one pass and its payload is the order itself: the ranks are filled in blocks across
threads and `SortRanks` sorts them with the row numbers beside them. The gather of the sorted rows
goes across threads from a quarter-million elements (it is bound by cache misses, not by
bandwidth).

### Text is ordered by packed prefixes (`JgsTextKernels.OrderTexts`)

Sorting positions by the texts they name: every text is read past the prefix they all share, and
the next eight characters (four, when any needs more than a byte) are packed into a `ulong` whose
unsigned order is the characters' own, zero-padded where a text ends, complemented for a descending
sort. A stable sort of those keys (a byte-wise radix sort; `SortRanks` across threads from 32K
keys) settles every pair whose windows differ. Each run of equal keys is then settled by its texts
whole: a pair by one comparison, a run of one repeated text by nothing, anything else by a
comparison sort of the run with arrival order breaking ties. The result is the order the stable
comparison sort gave. `sort` over text uses it for the present strings of each slice and gathers
the sorted texts in blocks; `unique` over text (not `'rows'`) orders the texts themselves, with no
array of one string per element, puts the missing ones behind in arrival order and marks group
starts by string equality. Both are under `JGRAPH_SET_FAST`.

`SortKernels.SortRanks` gained an overload naming the count from which it threads; the answer is
the same at any threshold.

### Measured

`measure_i6.ps1 -Stamp i6-first` and `compare_i6.py`: d11 and d12 as written, one warm-up and three
timed passes per session, I6 on and off (`JGRAPH_SET_FAST=0` and `JGRAPH_COLON_SHARE=0`, the same
binary) alternated three times headless, one JGraph.Application session per condition, then one
eight-pass session per condition whose passes 4 to 8 are the steady-state columns. Medians, seconds.

| row | R2025b GUI | JGraph GUI (8dbad495) | headless off | headless on | app off | app on | steady off | steady on |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `d11_accumarray_2M` | 0.009 | 0.023 | 0.025 | 0.004 | 0.031 | 0.003 | 0.024 | 0.004 |
| `d11_unique_2M` | 0.088 | 0.426 | 0.132 | 0.004 | 0.213 | 0.004 | 0.134 | 0.004 |
| `d11_sortrows_500k` | 0.016 | 0.022 | 0.021 | 0.014 | 0.024 | 0.016 | 0.020 | 0.009 |
| `d11_setops` | 0.006 | 0.041 | 0.027 | 0.001 | 0.046 | 0.001 | 0.021 | 0.001 |
| `d12_unique_text_200k` | 0.043 | 0.115 | 0.110 | 0.021 | 0.138 | 0.022 | 0.105 | 0.020 |
| `d12_sort_text_200k` | 0.038 | 0.078 | 0.066 | 0.014 | 0.061 | 0.019 | 0.065 | 0.013 |
| `d11_total` | 2.271 | 4.012 | 2.097 | 1.932 | 2.211 | 1.924 | 1.858 | 1.663 |
| `d12_total` | 1.802 | 1.235 | 0.543 | 0.460 | 0.602 | 0.611 | 0.533 | 0.374 |

All six rows are at or under R2025b in every condition (`sortrows` in the app is equal at 0.016,
which D3's tie rule counts). The off columns agree with what I7 measured for the same rows
(`unique_text` 0.110 against 0.118), so the session is comparable with the earlier stages'.

Rows that are not I6's and read over R2025b in these sessions with the roads on and off alike
(`movstd` 0.15 headless against 0.027, `normalize`, `rescale`, `gradient`, `cumtrapz`, `isoutlier`)
belong to I5 and its neighbours; I6 did not move them.

### Checks

- `SetKernelsI6Tests` (15): every case run with the roads off, on at all threads and on at one
  thread, the same bytes and outcome, and (in packed storage) the road taken by the counter:
  `unique` over 300,000 keys with every option, rows and columns, a matrix, all-equal input, other
  doubles (NaN, the two zeros, the infinities, a range too wide for a table) and the kinds that keep
  the old road; the four set operations with every output and shape combination, empty answers,
  `'stable'`, NaN, a negative zero, cells and integer classes; `ismember` with one and two outputs,
  a subject of anything, a matrix subject, scalars, empties and `'rows'`; `accumarray` over 300,000
  rows, negative zeros, the forms outside the road and its four refusals; `sortrows` by one key
  ascending and descending with NaN, zeros and ties; text `sort` and `unique` over 40,000 keys with
  repeats and missing strings, both directions, `MissingPlacement`, matrices along both dimensions,
  cells, and awkward text (empty strings, embedded and trailing `char(0)`, texts ending inside the
  window, a long shared prefix, characters past a byte, one text repeated); and `x(:)` followed by
  writes through either name, growth, in-place arithmetic, a function argument, logical and `int8`
  arrays.
- Packed storage: every test under `JGraph.Tests.Scripting` and the MATLAB parity suite, 9519 of
  9520; the one failure is `ArrayFunRefusesAMisspeltOptionAndNamesTheRealOnes`, open item 253,
  failing on every build since before I1.
- Boxed storage (`JGRAPH_JGS_PACKED=0`): I6's and I7's classes, the set, sort, array, table, text
  and ownership classes and the parity suite, 710 of 710 (the numeric roads read packed buffers and
  are not taken there; the answers are the same).
- `run-stress.ps1` over the Debug build: 88 of 89, `stess_40` (open item 254).
- Every d11 and d12 CHK line (42) equal to the GUI round's JGraph pass 1 in all ten sessions.
- The seven verify scripts green; the harvest unchanged.
- `tools/ownership/audit-ownership.py` crashes on HEAD before and after this change (an open item
  already), so the new share in `x(:)` was not checked by it; `OwnershipGateTests` and the value
  isolation classes pass.

## Consequences

- The plan is at rev 7 with I6 done: six rows at or under R2025b.
- `unique`, the set operations and `ismember` now answer packed arrays where they answered boxed
  ones on these roads; the two storages agree by the parity suite.
- `x(:)` no longer copies. A script that reads `x(:)` in a loop and never writes saves a copy per
  pass; one that writes `x` after reading `x(:)` pays the copy at the write instead of at the read.
- Timing while the laptop is in use reads two to three times slow whatever the power plan says: the
  first sessions of this stage did, on the same Silent plan as every earlier manifest. The off
  condition of a stage is the control that shows it.
- The rig gained `measure_i6.ps1` (with `-BuildOnly` and `-SkipBuild`, so only the sessions need an
  idle machine), `compare_i6.py` and `split_i6.m` (outside the repository).

## Divergences

None. Every road gives the bytes the general road gave.

## Still open

- `[~, p] = sort(x)` over one 500k column costs three times what `sortrows` of the whole matrix
  does (`split_i6.m`); not a row of the round.
