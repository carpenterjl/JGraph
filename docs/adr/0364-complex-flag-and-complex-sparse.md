# ADR 0364 — complex(1, 0) keeps its complex flag, and a complex sparse matrix can be built

## Status

Accepted, 2026-11-14. Open item 298 (chips wave 5, lane 5C), left open by ADR 0339.

## Context

All the expectations below were measured in R2025b 25.2 headless (`-noFigureWindows -batch`), with
probes `p5c` (153 cases), `p5e` (138) and `p5i` (38), one line per case in both engines.

- **The flag.** R2025b keeps `complex(1, 0)` complex: `isreal` is false, and so for
  `complex([1 2], 0)`, `complex(single(1), 0)`, `complex(1)`, `complex(true, 0)`, `complex([], 0)` and
  an N-D matrix. Here every one was real: `complex` built its answer through `JgsValue.ComplexNum`,
  which turns a zero imaginary part into a plain number, and boxed an array of such numbers.
  Planar storage already had the flag (`JgsPackedComplex.PreserveComplex`, which `isreal` reads), set
  only by `arrayfun`'s uniform collection and the `'like'` constructors.
- **Which operations keep it.** R2025b keeps it through a copy (a binding, a struct field, a cell, a
  function's return), concatenation with a real value (`[complex(1, 0) 2]`), `'` and `.'`,
  `reshape`, `permute`, `squeeze`, `full`, `sort`, `unique`, `cell2mat`, a MAT-file round trip (v7
  and v7.3), and `sparse` of a dense value. It drops it on any indexed read (`x(1)`, `x(:)`, `x(2:3)`),
  any indexed assignment (`x(1) = 5`, a grown `x(2) = 3`, a deletion), unary minus, any arithmetic
  (`x + 0`, `x * 2`, `x - x`, `sqrt`, `^`, `sum`, `cumsum`, `max`), `double`, `single`, an integer
  class, `repmat`, the flips and shifts, `real`, `imag`, `conj` and `abs`. `num2str` and `mat2str`
  write `complex(1, 0)` as `1`; `whos` says complex.
- **Here, before.** Beyond the flag itself: unary minus of any planar array was an internal error
  ("A packed array was accessed as boxed elements"); an indexed write into a flagged array kept the
  flag; `repmat` kept it; `int8` of a flagged array was the same internal error; and `complex` read a
  char part, a sparse part, a complex part and two arrays of different shapes in its own words.
- **Complex sparse.** `sparse([1+2i 0; 0 3])` was refused (`sparse needs numbers`). Sparse storage is
  `CscMatrix` in `JGraph.Numerics`, a real matrix that the solvers, the orderings and the Krylov
  methods read through `Values`. R2025b builds a complex sparse matrix from a complex dense matrix
  (keeping the flag: `sparse(complex([1 0; 0 2], 0))` is complex) and from complex triplets (real
  again when nothing imaginary is left: `sparse(i, j, complex([1 3], 0))` and a pair of duplicates
  that cancel, `sparse([1 1], [1 1], [1i -1i])`). It answers `full`, `nnz`, `nzmax`, `nonzeros`,
  three-output `find` (the values complex), `issparse`, `isreal` false, `class` double, `size`,
  `whos` (24 bytes per stored entry and 8 per column plus one), `+`, `-`, `*` and `.*` between sparse
  matrices, a scalar's `*` and `/` (complex too), a dense sum (dense) and product (dense), comparisons
  with a scalar (a sparse mask), unary minus, `'` (conjugating) and `.'`, `real`, `imag` and `abs`
  (real sparse), `conj`, a subscript read, `isequal` and a MAT-file. Arithmetic, subscripts and
  unary minus answer real when nothing imaginary is left; `'`, `.'`, `full` and a MAT-file keep the
  flag. A real sparse matrix times a complex scalar is complex sparse (`speye(2) * 1i`).

## Decision

### The flag lives in planar storage only (`JgsBuiltins.ComplexFlag.cs`)

`complex` builds a planar array with `PreserveComplex` set whenever every imaginary part is zero, a
scalar included: `complex(1, 0)` is the one-element planar array (the representation ADR 0339's
`IsComplexScalar` already took). A non-zero imaginary part builds what it built before. No scalar
`JgsType.Complex` value carries a flag, so the boxed arrays keep their invariant (an element with a
zero imaginary part is a number) and every element read through `ElementAt` stays real, which is
R2025b's indexed read.

- **Writes clear it.** `JgsValue.WritablePlanes` and `SetPackedComplex`, the two in-place write
  gates of planar storage, clear the flag; `PreserveComplex` gained an internal setter for it. A
  write that demotes the array to boxed elements loses it on the way.
- **Unary minus maps the planes** (`MapPlanesDroppingFlag`, a new arm of the interpreter's
  `MapNumeric`): the answer has no flag and is a real packed array when nothing imaginary is left.
- **Concatenation carries it.** `StampLiteral`, the one exit of the numeric bracket and of
  `cat`/`horzcat`/`vertcat`, hands its answer to `JoinKeepsComplexFlag`, which turns a real double or
  single answer into a flagged planar one when a piece was flagged.
- **`repmat` drops it** (`RealPlaneOfZeroImag` on its planar road), and an **integer class** converts
  a planar value with no imaginary part through its real plane. `double` and `single` already
  dropped it.
- **`num2str` and `mat2str`** read a flagged first argument as its real plane (`FirstWithoutZeroImag`).
- **The display** writes every element of a flagged array with no imaginary part as `1+0i`, and the
  one-element array as the scalar it is (`x = 1+0i`), in `Display` and in the statement echo.
- **MAT-files.** The v5 and v7.3 writers write a flagged array complex; the readers load a complex
  double or single array whose imaginary part is all zero as a flagged one (`ZeroImagFlaggedOf`).
- **`complex`'s checks** are R2025b's: `MATLAB:complex:realPartInputNonNumericOrNonReal`,
  `...imagPartInputNonNumericOrNonReal` for a complex, cell or other non-numeric part, and
  `MATLAB:complex:inputSizeMismatch` for two arrays of different shapes. A char part is its codes; a
  sparse part beside a dense one is read full; two sparse parts make a complex sparse answer.
- **`fft` of a flagged planar array** transforms by the real road (`PackedTransformOps`), the boxed
  road's answer to the bit; the complex kernel gave `-1.9999999999999998` for an exact `-2`.

### A complex sparse matrix is its own storage beside the real one (`JgsComplexSparse`)

`JgsComplexSparse` (Scripting, not Numerics) keeps compressed sparse columns with a `Complex` value
per stored entry; an entry is stored when either part is non-zero. `JgsValue.ComplexSparse` wraps it
under `JgsType.Sparse`, so `issparse`, `class` and every dispatch on the type see a sparse matrix.
`JgsValue.AsSparse` no longer casts: a complex payload is refused there in words that say so ("This
operation does not take a complex sparse matrix here; use full() first."), so a verb not taught the
complex kind — the solvers, the orderings, `eigs`, `sum`, sparse assignment, the v7.3 writer, the
Production Server encoders, the runtime conversions — refuses one rather than reading half of it.
`JgsValue.SparseExtent` gives the size of either kind, and `size`, `numel`, `isempty`, the indexing
extents and the variable summary read it.

`JgsBuiltins.SparseComplex.cs` teaches the measured verbs: `sparse` of a complex dense matrix and of
complex triplets, `full` (`SparseDense`), `nnz`, `find` (`ComplexSparseFind`), the operators
(`ComplexSparseBinary`, asked first by `SparseBinary`), a subscript read (`ComplexSparseSubscript`,
over the two parts as real sparse matrices, so every subscript form and refusal is the real road's),
`'` and `.'` (`ComplexSparseTransposed`), unary minus (`SparseNegated`), `isreal` (through
`HasComplexPart`), `whos`, `isequal`, a sparse right-hand side, and the v5 MAT-file writer and reader.
`real`, `imag`, `abs`, `conj`, `nonzeros` and `nzmax` are wrapped (`WrapSparseVerbs`) round the verbs
as they stand. `SparseOfComplex` makes an answer real when nothing imaginary is left, unless the
verb keeps the flag.

Some of those verbs now answer for a real sparse matrix too, which they refused before: unary minus,
`real`, `imag`, `abs`, `conj` and `nonzeros` of one, and `.*` between two (R2025b's answers, measured).

## Consequences

- New parity fixture `complex_flag_sparse` (171 rows) from R2025b 25.2, every row agreeing, green in
  the packed and boxed lanes, no boxed overlay.
- `oi_table_missing_save_summary`'s `wh_file_13` and `wh_mem_13` (`whos` of a complex sparse matrix)
  agree; they were ADR 0234's divergence, struck there with a "Closed since" paragraph.
- `arrayfun(@(a) complex(a, 0), [1 2])` is complex, as R2025b's is (ADR 0339's Still open).
- Display changes, both dialects: a flagged array writes `1+0i`; a complex sparse matrix shows as
  `2x2 sparse complex double, 2 nonzeros` with its entries complex.

## Divergences

None opened.

## Still open

- **Complex integers.** `complex(int8(1), 0)` is a complex double here; R2025b's is a complex int8
  (with its refusals: `MATLAB:complexInts` for arithmetic, `MATLAB:transpose:intType` for `'`,
  `MATLAB:complexInteger` for `abs` and `conj`, `MATLAB:complex:mixedInputs` for two integer
  classes). `int8(1+2i)` is refused here. No numeric class on a complex value is an integer one, which
  is a new element kind, not this item's flag.
- **Unary plus** keeps the flag: the parser drops `+x` (R2025b's `+complex(1, 0)` is real, and `+'a'`
  is 97). `uplus(x)` drops it, as R2025b's does.
- **`sort`, `unique` and `cell2mat`** drop the flag here and keep it in R2025b; so does `x.^1` of an
  array (R2025b keeps it there and drops it for a scalar). `logical(complex(1, 0))` answers here and
  is refused in R2025b (`MATLAB:nologicalcomplex`), as `logical(1+2i)` is in other words.
- **A string part** (`complex("a", 0)`) is read as its char codes here and refused in R2025b: `complex`
  does not keep string arguments.
- **Complex sparse, not taught:** `sum` and the other reductions, `S.^p`, `[S S]` (sparse
  concatenation is refused for a real matrix too), `S == S` between two sparse matrices, indexed
  assignment into one or of a complex value into a real one (ADR 0167's divergence stands), `\`, the
  orderings and the Krylov solvers, `eigs`, a v7.3 MAT-file and the runtime conversions.
  Each refuses by name. (`mps.json.encode` and `decode` were taught at landing: each value's real and
  imaginary parts in turn, as R2025b writes them; k14_mps_json's complex sparse rows agree.)
- **A subscript read that picks one entry** (`S(1, 1)`, `S(3)`) is a dense scalar here, for a complex
  matrix as for a real one; R2025b answers a 1-by-1 sparse matrix.
- **A comparison with a sparse matrix** answers a double sparse mask here, real or complex; R2025b's is
  logical sparse (ADR 0234's logical sparse divergence).
- **A single sparse matrix** (`sparse(single([1+2i 0]))`) is double here; R2025b's is single.
  `sparse(int8([1 0]))` answers here and is refused in R2025b (`MATLAB:sparse:invalidMatrix`).
- **The display formats** of a flagged value and of a complex sparse matrix are this build's
  (`1+0i`, `(1,1)  1+2i`), not R2025b's (`1.0000 + 0.0000i`), as every display here is.
