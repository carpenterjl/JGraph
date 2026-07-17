# ADR 0019 — JGS data analysis: element-wise comparisons, logical indexing, and a stdlib

Date: 2026-07-17
Status: Accepted

## Context

JGS could plot but not analyze: comparisons were scalar-only, `==` compared arrays by reference,
indexing was single-element, and there were no statistics, string, or table-inspection builtins.
The target workflow — a messy engineering CSV (junk preamble, serial-number column, measurement
columns) parsed, filtered by criteria like `data(parameter > threshold)`, summarized per unique ID,
and index-mapped back to the original rows — required both language-core changes and a stdlib.

## Decisions

### 1. Comparisons and equality are element-wise over arrays; truthiness is MATLAB's

`< <= > >=` broadcast a scalar across an array (or pair equal-length arrays) and return an array of
bools — a mask. `==`/`!=` do the same using shallow value equality per element, so `ids == "SN-1"`
masks a string column; mismatched element types compare unequal rather than throwing. **This
replaces reference equality for array `==` — a breaking change** with `isequal(a, b)` (deep,
single bool) as the whole-value form. Array truthiness changed with it: an array is true iff
non-empty and **all** elements are truthy, so `if a == b { }` still reads correctly; the emptiness
test is `length(a) > 0`. `&& || !` stay scalar/short-circuit over that truthiness; element-wise
logic is the `and`/`or`/`not` builtins (no new `&`/`|` tokens — lexer, highlighter, and debugger
stay untouched). Bools read as 0/1 in arithmetic, so `sum(mask)` counts matches.

### 2. Indexing an array with an array gathers; parens work MATLAB-style

One interpreter helper backs both syntaxes: `a[sel]` and — because "calling" a non-function
array/string cannot mean anything else — `a(sel)`, so the user's `data(parameter > threshold)`
works verbatim. A scalar selects an element (0-based as always); an all-bool array is a
length-checked mask; an all-number array gathers by index (order kept, repeats allowed); strings
gather to strings. Gathers preserve row correspondence: `volt(hot)` and `ids[hot]` stay aligned
with `hot = find(temp > 85)`'s original row numbers. Masked *assignment* (`a(mask) = v`) remains
unsupported — l-values are still single-index only.

### 3. A deliberately small stdlib, registered in both registries

33 new builtins in `JgsBuiltins` (bodies in `JgsStdlib`/`JgsSprintf` where they are real
algorithms) and mirrored in `JgsBuiltinCatalog` — the existing pinning test keeps the two lists
identical. Statistics (`std`/`variance` are sample, n−1; `median`, `mode` smallest-on-ties,
`percentile` linear-interpolated, `cumsum`, `cumprod`, `diff`) propagate NaN rather than skipping —
cleaning is explicit (`x(not(isnan(x)))`). Array ops: `numel`, `sort`, `unique`, `find` (the
index-mapping primitive), `any`, `all`, `concat`, `slice` (0-based, stop-exclusive), `indexof`,
`reverse`, `isnan`, `isequal`, `and`/`or`/`not`. Strings: `sprintf` (fixed C-subset
`%d %i %f %e %g %s %x %%` with width/precision, invariant culture, loud errors on anything else),
`str`/`num` (NaN on parse failure, MATLAB `str2double`), `upper`/`lower`/`trim`,
`split`/`join`, `startswith`/`endswith`/`replace`, and polymorphic `contains` (substring or array
membership).

### 4. Tables open up: names, row count, text columns, and junk-preamble skipping

`colnames(t)`, `rowcount(t)`, and `textcolumn(t, name)` (missing cells → `""`) expose what scripts
need for the unique-ID workflow without making `Table` mutable or dynamic. The readers gain an
optional `skiprows` argument — `readcsv(path, 6)` — mapped to the already-existing
`ImportOptions.SkipRows`. `HeaderDetector` is deliberately untouched: auto-detecting arbitrary
preambles risks regressing the import wizard for no necessary gain, and the script author knows
their file format.

## Consequences

- 74 new tests: comparison/truthiness semantics, both gather syntaxes and their errors, every
  builtin's values and edge cases, skiprows against CSV and xlsx fixtures, and an end-to-end test
  running the full messy-log workflow (per-device stats, over-temp index mapping, aligned slices,
  two-subplot figure). `analysis-demo.jgs` + `test_log.csv` in the demo workspace mirror it live.
- Breaking changes (array `==`, array truthiness) surfaced no failures in the existing suite; both
  are documented here as the intended new semantics.
- Arrays remain boxed `JgsValue[]`; element-wise ops allocate per element. Fine at the tens of
  thousands of rows this workflow targets; a packed numeric array is future work if profiling ever
  demands it.
