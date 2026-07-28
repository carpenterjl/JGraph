# ADR 0043 — Shaped arrays, and logicals that equal numbers

## Status

Accepted (M40, 2026-07-28). Supersedes the "a matrix is an array of row arrays" half of
[ADR 0028](0028-uniform-zero-based-indexing.md); the 0-based/1-based decision that ADR made is
untouched.

## Context

`docs/matlab-builtin-coverage.md` recorded two differences a MATLAB script can notice that were not
builtin coverage at all:

- **`A(i, j)` was not two-subscript indexing.** A matrix was an array of row arrays, so `A(i)(j)` was
  the spelling, `A(3)` on a 3×3 returned a whole row, `A(:)` returned the rows, and an index produced
  by `find` did not index back — it read a row where MATLAB reads an element. That last one is the
  serious case: not an error, a wrong number.
- **`true == 1` was false.** Ordering comparisons already treated a logical as the number it stands
  for, so `true > 0` worked while `true == 1` did not, and a mask could not be checked against
  `[1 0]`.

## Decision

### An array carries a shape, stored column-major

`JgsValue` gains `Rows` and `Cols` over its existing flat storage. A value built by the old factories
is 1-by-n, so nothing that does not ask for a shape sees one; `JgsValue.Shaped` and
`JgsMatrix.Build`/`FromRows`/`FromColumnMajor` are how a matrix is made.

Storage is **column-major**, because that is what MATLAB means by linear order. It makes `A(:)` a
buffer clone rather than a gather, it is the order `reshape` already assumed, and it is the order a
MAT-file holds on disk — so `save`/`load` became simpler rather than harder.

Shape lives on the *wrapper*, next to the reference, under the same single-wrapper invariant that
makes packed arrays alias correctly (ADR 0026). That is what lets `Reshape` be seen by every name
bound to the value, and it is also the milestone's sharpest edge: **every path that mints a new
wrapper has to carry the shape across**. A copy, an elementwise map, a comparison, a gather and a
transpose each do it explicitly, and the MATLAB dialect's copy-on-binding was the first place that
silently flattened a matrix when it did not.

### One place knows the layout

`JgsMatrix` is the only code that knows how a matrix is laid out: `IsMatrix`, `RowCount`, `ColCount`,
`At`, `ToRows`, `Build`, `FromElements`, `Like`. The four pre-existing helpers — `RowsOfMatrix`,
`IsMatrixValue`, `Interpreter.IsMatrix`, `AsRows` — became one-line forwarders to it, and that is why
**fifty call sites across the linear algebra, reductions, geometry and Schur builtins needed no edit
at all**. `JgsMatrix` also still reads the old nested form, which a MAT-file load, a workspace
restore or a JGS script can hand it.

### Two subscripts, and `end` per dimension

`A(i, j)` selects an element; a range, vector, mask or `:` in either slot selects the submatrix it
names, with the shape that implies. The `end` stack changed from a single length to *(extents, slot)*
so that `A(end, end)` means the last row and the last column rather than the last element twice.

A single subscript on a matrix is now column-major linear, so `A(find(A > 5))` round-trips. The
result's orientation follows MATLAB: it takes the index's shape, except that a logical mask always
gathers into a column, and except that between two vectors the source's own orientation wins so
`v(1:2)` still looks like `v`.

Writes gained the same two-subscript form, plus growth (`A(3, 4) = 1` reallocates and zero-fills) and
deletion (`A(i, :) = []`). Both replace the value rather than mutating it, so both need a plain
variable on the left — the restriction cell growth has had since `c{end + 1} = x`. Deleting a lone
element of a matrix is refused rather than guessed at, because there is no rectangle left.

### `[a, b]` concatenates in MATLAB and lists in JGS

A bracket literal is now proper MATLAB concatenation: blocks join left to right and must agree on
height, rows stack and must agree on width, an empty contributes nothing, and a mismatch names both
shapes. That is a new dialect flag rather than a shared change, because in JGS `[[1, 2], [3, 4]]` is
how a matrix has always been written and its own scripts and guide rely on it.

One deliberate leniency, recorded because it is a deviation: **in a stacked literal whose blocks are
all vectors, if any of them is a column then the rows are read as columns too.** A JGS vector's
orientation is often incidental — it came from a reader or a range that had none to give — and this
is what keeps `[audio; zeros(k, 1)]` meaning "pad this signal". Two genuine row vectors still stack
into a 2-by-n matrix, because neither of them is a column.

The matrix product has the same shape: the operands' orientations are tried as written, and only if
they do not meet is a *vector* turned the other way. A matrix is never reinterpreted. Two bare row
vectors are still refused, since neither says which product was meant.

### A logical equals the number it stands for

`JgsValue.AreEqual` compares `Number` and `Bool` by value. `JgsStdlib.DeepEquals` had done this since
M37 — `isequal(true, 1)` was already true — so this closes a gap between `==` and `isequal` rather
than opening one, and `ismember` was already lenient too.

`PackedOps.TryEquality` carried its own copy of the strict rule in two branches. It had to change
with `AreEqual` or a packed array would have kept the old answer; `JgsPackedParityTests` is the guard
rail that would have caught it.

Folded in while here: **`NaN == NaN` was true**, because `AreEqual` used `double.Equals` rather than
`==`. MATLAB says false, `isequal(NaN, NaN)` already said false, and the two disagreed.

`isequal` now compares *sizes* as well as elements, which is MATLAB's actual definition and which the
old element-by-element walk could not express — `isequal([1 2 3], [1; 2; 3])` is false.

## Consequences

- `A(i, j)`, `A(i, :)`, `A(:, j)`, `A(end, end)`, submatrix reads and writes, growth and deletion all
  work, and `A(:)` is MATLAB's flatten.
- **`A(k)` on a matrix changes meaning**, from "row k" to "element k counting down the columns". This
  is the point of the change, and it breaks a script written against the old
  `m[row][col]` form — which `docs/jgs-scripting-guide.html` taught.
- **A transposed vector is a real column.** `(0:0.1:1)'` used to hand the same row back; it now
  reports `[11 1]` from `size`, and can be concatenated into a matrix.
- **`true == 1`, and `NaN == NaN` is false.** Both are silent behaviour changes for a script that
  relied on the old answers.
- `find` on a matrix returns a column, as MATLAB does.
- `JgsDialect` gains `ConcatenatesBrackets`, false for JGS and true for MATLAB.
- A matrix is one packed buffer instead of one heap object per row, so building and copying one got
  cheaper as a side effect.

## Testing

`ShapedArrayTests` is the new suite, twenty facts covering two-subscript reads, per-dimension `end`,
column-major linear indexing with the `find` round-trip, gather orientation, writes, growth,
deletion, transpose, concatenation and its two error messages, the shape predicates, shape-preserving
elementwise work, the matrix product's orientation rule, logical/numeric equality, NaN, `switch`, and
`isequal` comparing sizes.

The assertions run *inside* the script, so they pin MATLAB's answers rather than JGraph's display
formatting. Where a case used to be pinned the other way — `[1, "1", true] == 1`, the transpose of a
vector, the nested display of a matrix — the old assertion was rewritten rather than deleted, so the
change of answer is visible in the diff.
