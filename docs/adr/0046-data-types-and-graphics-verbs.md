# ADR 0046 — Stress-test data types and graphics verbs: table, categorical, strings, tiles

## Status

Accepted (M43, 2026-07-29). Completes the stress-test campaign of
[ADR 0044](0044-stress-test-language-fidelity.md) (language) and
[ADR 0045](0045-sparse-integer-perf-numerics.md) (numerics); reverses the "excluded" status ADR 0040
gave `table`.

## Context

After M42, five of the sixteen user stress scripts still failed, all on data types and graphics
verbs: the `table`/`timetable` constructors, `categorical` + `summary`, string-array conversions
with `missing`, `tiledlayout`/`nexttile`, `axis image`, `colormap turbo`, and the accepted
appearance verbs. Writing the four new self-checking scripts (stess_17–20) then surfaced four
genuine engine gaps hiding behind the originals: `~` collapsed an array to one scalar truthiness
bool, sprintf could not cycle a format over an array, `num2str` rejected a format string, and
`surf` refused the full X/Y matrices `meshgrid` itself produces.

## Decisions

1. **`table(...)` builds the existing `JGraph.Data.Table`** — numeric vectors become
   `NumberColumn`s, cells and string arrays become `TextColumn`s, a trailing
   `'VariableNames', {…}` names them (default `Var1…VarN`), and `timetable(rowTimes, …)` is the
   same constructor with a leading `Time` column. Member access on a table value now reads a
   variable's column: numeric columns come back as column vectors, text columns as cells — which is
   exactly what makes `T.Code{2}` brace in. The Data Viewer, `readtable`, and plotting glue that
   already spoke `Table` needed nothing.

2. **Untyped stand-ins instead of a tagged-type system**, each recorded in the coverage doc: a
   *categorical* is its cell of category names (`summary` counts distinct values into a struct,
   first-appearance order); a *duration* (`seconds(v)`) is its number of seconds; and *missing* is
   the string sentinel `<missing>` (`ismissing` recognizes it, and NaN for numerics). `class()`
   answers cell/double/char for them — the scripts consume the shapes, not the class names.

3. **String conversions complete the family**: 1-argument `split` (whitespace) and `join` (space),
   with a shaped string array joining along its rows; `string()` and `cellstr` convert both ways;
   `compose` formats per element. `sprintf`/`fprintf` under MATLAB now **flatten array arguments
   and cycle the format** (`sprintf('%d,', 1:5)` → `1,2,3,4,5,`), stopping mid-format when the
   values run out, exactly as MATLAB does — JGS keeps its strict argument-count errors, which its
   own tests pin. `num2str(x, fmt)` honours a format string; `%o` joined the specifier set.

4. **MATLAB's `~` is element-wise over arrays.** The unary-not evaluator collapsed any operand to
   one scalar truthiness bool, so `M(~mask)` indexed with a single bool. Under the MATLAB dialect
   an array operand now maps per element; JGS keeps the scalar reading of `!arr`.

5. **`tiledlayout`/`nexttile` ride on the subplot grid** — closure state (rows, cols, cursor) in
   the builtin registration, `nexttile` advancing with wrap-around, so the figure model needed
   nothing new. `axis` gained the aspect words (`image`/`equal`/`square` set the existing
   equal-aspect flag) and the `[xmin xmax ymin ymax]` vector; `shading`, `lighting`, `camlight`,
   and `rotate3d` are accepted verbs that change nothing visible, because surfaces are always
   smoothly shaded and rotation is already interactive — each says so in its catalog line.

6. **Surface builtins accept full meshgrid matrices**: `surf(X, Y, Z)` with 200×200 X/Y collapses
   them to their generating vectors (first row / first column), which is the form `peaks(200)`
   hands out; `contourf(X, Y, Z, 40)` reads a scalar fourth argument as a level count, spacing
   that many levels across z's range. `meshgrid(x)` is `meshgrid(x, x)`, and the colormap table
   gained **turbo**.

7. **Brace assignment reaches through a dot chain**: `s.a.b{r, c} = v` evaluates the chain to the
   stored cell and writes in place (member reads hand back the stored reference). Growth by brace
   through a chain still errors with guidance — growing needs a rebindable name.

8. **Two crash-class fixes found in passing**: `readtable` on a missing file threw an unwrapped
   `ImportException` through the whole process (now a script error), and the four Fable scripts are
   the regression net that found the rest.

## Consequences

- **All twenty stress scripts** — the sixteen user-written ones plus stess_17–20 (`Created by:
  Fable`: shaped-array torture, errors + strings, numerics invariants, functions + scoping) — exit
  0 under `jgraph -batch` with zero `Fail:` lines.
- `MatlabStressM43Tests` pins the table constructor and column access, timetable, categorical
  summary counts, conversions and missing, format cycling, element-wise `~`, `num2str(x, fmt)`,
  tiledlayout, the meshgrid-matrix surface forms, and dot-chain brace assignment.
- Divergences on record: categorical/duration/missing are untyped stand-ins; `summary` always
  returns (never prints); `cell` growth through a dot chain errors; `axis tight`/`off` are
  accepted no-ops.
