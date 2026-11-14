# ADR 0354 — A block write fits its right side by extents, a struct array is written through a grid, and a subscript that is not a number is R2025b's

## Status

Accepted, 2026-11-13. Open items 285 and 286, which ADR 0337 left open, and the engine half of open
item 208 (stage K12, ADR 0268). Chips wave 3, agent N.

## Context

- **Item 285.** A write with two subscripts or more checked only the right side's element count.
  `A = zeros(3, 4); A(2:3, 2:3) = 1:4`, `A = zeros(2, 3); A(:, :) = 1:6` and a 2-by-5 into a 5-by-2
  block all wrote here, column-major. R2025b refuses each with `MATLAB:subsassigndimmismatch`. It
  accepts a right side whose extents that are not 1 equal the block's in order: a 1-by-1-by-5 into a
  5-by-1, a 1-by-2-by-3 into a 2-by-3, a column into a row. Probe `probe_n` and its two follow-ups ran
  some 150 writes in both engines: numeric, logical, `int8`, char, string, cell and struct targets;
  two and three subscripts; masks, empty blocks, growth, deletion, and the shapeless `[]`. What it
  found:
  - a block of nothing takes any empty (`A([], []) = zeros(0, 3)`) and refuses anything else;
  - a misfit written into the 0-by-0 has its own sentence: "the indices on the left side are not
    compatible with the size of the right side" (`A = []; A(1:2, 1:3) = 1:6`, and `''`, `{}`,
    `strings(0)` and `struct('a', {})` the same). `zeros(0, 3)` and `zeros(1, 0)` have the usual one;
  - the left side is named with trailing ones dropped past the second dimension, zeros kept
    (`2-by-2-by-0`), and the right side whole (`1-by-1-by-3`);
  - `A(:, :) = v` on `[]` takes the extents of `v` that are not 1 when `v` has a third dimension (a
    1-by-1-by-3 makes a 3-by-1), and refuses three of them;
  - a one-subscript write stays by count;
  - `A(1:2, 1:2) = []` is `MATLAB:badnullassign`, and a deletion past the end
    `MATLAB:subsdeldimmismatch` ("Matrix index is out of range for deletion."). JGraph had its own
    sentence for the first and `MATLAB:badsubscript` for the second.
- **Item 286.** `s = struct('a', {}); s(:, 1) = struct('a', {1, 2})` was refused ("Cannot assign by
  index into a struct"). The two-subscript road took only a one-element struct, and that through
  `AssignIntoStructGrid`, which named one row and one column and nothing else. The N-subscript road
  refused every struct. R2025b writes a struct array through a grid as it writes any array: colons,
  ranges, masks, growth (gaps get the array's fields, each `[]`), deletion, and three subscripts. It
  refuses a misfit in the numeric words, a value that is not a struct with
  `MATLAB:heterogeneousAssignment` ("Assignment between unlike types is not allowed."), on the
  one-subscript road too, and other fields with `MATLAB:heterogeneousStrucAssignment`.
- **Item 208.** The probe ran each row of the chip in both engines:
  - `x()` and `s()` are the value itself in R2025b, so `s().a` is `s.a`. They were refused here.
  - A char subscript is its character codes (`x('a')` is `x(97)`, `x(['ab'; 'cd'])` a 2-by-2 of codes,
    `MATLAB:badsubscript` past the end), on reads and writes, for every positional container. It was
    refused here.
  - A string, cell, struct, function handle or datetime subscript is
    `MATLAB:matrix:unableToUseTypeAsIndex` ("Unable to use a value of type string as an index."). The
    refusals here had JGraph's own words and no identifier.
  - `c{:} = 1` over three elements, and `c{1:2} = 1`, `c{:, 1} = 1`, `s(:).a = 5` and `s(1:2).a = 5`,
    are `MATLAB:index:expected_one_output_for_assignment` ("Assigning to 3 elements using a simple
    assignment statement is not supported. ..."). Naming exactly one place (`c = {1}; c{:} = 4`)
    writes it.
  - `reshape` of a struct array was refused. So were `permute`, `transpose` and the other shape verbs,
    and `s'` came back unturned.
  - `y() = 5` and `c{}` are refused when R2025b reads the file: `MATLAB:m_empty_indices` and
    `MATLAB:m_improper_grouping`.
  - `c(1) = 5` on a cell gave R2025b's words without `MATLAB:invalidConversion`. `s2.a = 5` on a
    struct array gave them without `MATLAB:scalarStrucRequired`, and on an empty one R2025b says
    something else (`MATLAB:emptyDotAssignment`). `x.a = 1` on a number lacked
    `MATLAB:structAssToNonStruct`, and a string's dot is `MATLAB:noSuchMethodOrField`.
  - With two subscripts a mask past the end is "The logical indices in position 1 contain ...".

## Decision

### One fit rule, five roads (`Interpreter.RefuseMisfit`)

In the MATLAB dialect every write with two subscripts or more holds a right side that is not one
element to the block. The count must match, and unless the block is empty the extents that are not 1
must match in order. A misfit is `MATLAB:subsassigndimmismatch` naming both sizes
(`LeftSideDims`, `SizeWords`, `WrittenDims`), or `IndicesNotCompatible` when the target was the
shapeless 0-by-0 (`IsShapelessTarget`). The roads:
- `AssignTwoSubscripts`;
- `AssignNSubscripts`, which also gives R2025b's words in place of "Cannot assign 4 values into a
  selection of 3 element(s)";
- `AssignCellTwoSubscripts` and `AssignCellNd`;
- `AssignIntoCharRow` when a row and a column are written (`x(1, 1:3) = 'uv'`).

The check runs where the count check ran, before the target grows (M14). A one-element array on the
two-subscript road now fills the block as a scalar does. The JGS dialect keeps its count rule and
its words. `ShapelessColonExtents` gives `A(:, :) = v` on `[]` the extents of a `v` with a third
dimension. `AssignIntoCharRowAsMatrix` reads `''` as the 0-by-0 it is.

Deletion: `DeleteSlice` and `DeleteNdSlices` say `MATLAB:badnullassign` in R2025b's words
(`NullAssignmentOneNonColon`) and refuse a subscript past the end first (`RefuseDeletionPastEnd`).

### A struct array rides the numeric road (`AssignStructByStandIn`)

`IsStructGridWrite` sends a write with two subscripts or more to `AssignStructByStandIn`. That covers
a plain struct target with any right side, and a plain struct written into the empty `[]`. The
method is ADR 0241's (open item 121) stand-in for object arrays, applied to structs:
1. The fields are held to the array's (M14).
2. The write is made on a numeric stand-in of the positions (1 onward; the right side's after them),
   so the fit, the growth, the deletion and every refusal are the numeric road's.
3. The elements are gathered back. A 0 the growth left becomes an element with the array's fields,
   each `[]`.

Elements carried over take a share as on the one-subscript road (M2). An empty struct on the right
is written as a 1-by-0, so that it is not the deleting `[]`, and its misfit is reported with its own
size. A JGS script keeps `AssignIntoStructGrid`.

### A subscript that is not a number (`PositionalIndex`)

`EvaluateIndexArgument` hands a MATLAB subscript to `PositionalIndex`:
- a char row or char matrix becomes its codes, in its shape;
- a string, cell, plain struct, function handle or time value is
  `MATLAB:matrix:unableToUseTypeAsIndex`, naming its class;
- the char `':'` stays the colon.

A table's subscripts are names as well as positions, so `Interpreter.Tables` evaluates them with
`EvaluateNamedIndexArgument`, which skips this. Keyed collections never came here.

### The rest of item 208

- `CallOrIndex` answers `x()` with the value for data (`IsEmptyCallTheValue`): arrays, text, numbers,
  cells, sparse and plain structs. A callable, a keyed collection and an interpolant keep their own
  meaning of an empty call.
- The brace write and `ResolveStructElementForWrite` count what a colon, a range or a mask names, and
  refuse anything but one place in R2025b's words (`AssigningToSeveral`).
- The shape verbs gather a plain struct array's elements through their positions, as they gather a
  cell's (`JgsBuiltins.IsPlainStructArray`, `StructPositionsLike`, `GatheredStructs`). `s'` turns a
  two-dimensional struct array (`TransposedStructs`).
- The parser refuses `y() = 5` (any assignment to an empty paren index) and `c{}` with R2025b's
  identifiers. `eval` puts R2025b's "Error: " before the first, as R2025b does.
- The identifiers: `MATLAB:invalidConversion` for a cell's paren write, `ScalarStructRequired`
  (`MATLAB:scalarStrucRequired` or `MATLAB:emptyDotAssignment`) and `FieldWriteRefusal`
  (`MATLAB:structAssToNonStruct`) at a variable's field write, `MATLAB:noSuchMethodOrField` for a
  string's dot, and the logical mask's place (`PackedOps.MatlabMaskSpan`).

## Consequences

- New parity fixture `oi_index_assign_shapes`, 246 rows recorded from R2025b 25.2, every one agreeing
  and none a divergence. It covers the numeric, empty, cell, char, string and struct rows of 285; the
  struct grid rows of 286; and the item 208 rows.
- A MATLAB script that wrote a block with the right count and the wrong extents now stops where
  R2025b stops. Such a script filled the block in an order its author did not write.
- `reshape`, `permute`, `transpose` and the other position verbs take struct arrays.
- `x('a')` reads character codes. `x("a")` and `x({1})` are refused with R2025b's identifier.

## Divergences

None new.

## Still open

- **`[]` takes a struct here; R2025b refuses it.** `x = []; x(2, 2) = struct('a', 1)`, `x(1:2) = ...`
  and `x(:, 1) = ...` are `MATLAB:invalidConversion` ("Conversion to double from struct is not
  possible.") in R2025b. JGraph starts a struct array, as V6 decided. The road is shared with a write
  that creates its variable (`EmptyOfKind` conjures `[]`), so the fix is to conjure an empty struct
  there first. That is a chip of its own.
- **`c{i, j, k} = v` over several cells** still writes them all, through the paren write of a one-cell
  value. R2025b's count refusal is not applied there.
- **Not indexing, met by the probe:**
  - `cell(2, 2, 2)` is refused (`MATLAB:maxrhs`);
  - `'a':'c'` is refused ("The start of a range must be a number");
  - `reshape`'s count refusal has JGraph's words, not `MATLAB:getReshapeDims:notSameNumel`;
  - a cell's two-subscript read past the end does not name the position;
  - `x(:)()` and `x(1)()` are not refused at parse (`MATLAB:mir_paren_index_must_be_last`);
  - the field writes one level down (`st.t.f = v` on a struct array, `st.t = 'ab'; st.t.f = 1`) still
    lack the identifiers the variable level now has.
