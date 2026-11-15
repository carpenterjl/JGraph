# ADR 0369 — a sparse matrix keeps its class, one entry is a 1-by-1 sparse matrix, and the verbs ADR 0364 left refused answer for a complex one

## Status

Accepted, 2026-11-14. Open item 331 (chips wave 6, lane 6B), left open by ADR 0364.

## Context

All the expectations below were measured in R2025b 25.2 headless (`-noFigureWindows -batch`), with
probes `p6b_a` (148 cases), `p6b_b` (135), `p6b_d` (57), `p6b_e` (19) and `p6b_mat` (the bytes of two
`-v6` files), one line per case in both engines.

- **Wrong answers.** A subscript that picks one entry (`S(1, 1)`, `S(4)`, `S(end)`) answered a dense
  scalar; R2025b answers a 1-by-1 sparse matrix, real or complex. A comparison answered a double
  sparse mask; R2025b's is logical sparse (`class` `logical`, `full` logical, `whos` one byte a
  value). `sparse(single(x))` was double; R2025b keeps single sparse matrices (`class` `single`,
  arithmetic in single, four bytes a value). `sparse(int8([1 0]))`, `sparse(uint8(...))` and
  `sparse('ab')` answered (the last an internal error); R2025b refuses `MATLAB:sparse:invalidMatrix`,
  and an integer `v` in `sparse(i, j, v)` `MATLAB:sparse:invalidThirdInput`.
- **How R2025b uses a 1-by-1 sparse matrix.** It flows through every function: `sqrt`, `exp`, `sin`,
  `floor`, `round`, `sign`, `abs`, `isnan`, `max(s, 0)`, `mod`, `atan2`, `s^2`, `s * s`, `s / 2` and
  `s .^ 2` answer 1-by-1 sparse; `s - 1`, `2 / s`, `hypot(s, 3)`, `isfinite(s)` answer dense;
  `zeros(s, 1)`, `ones(1, s)`, `repmat(7, 1, s)`, `linspace(0, 1, s)`, `eye(s / 2)`, `cell(1, s)`,
  `x(s)`, `x(s : s + 1)`, `for k = 1:s`, `while s`, `switch s`, `if s` and `isequal(s, 4)` read the
  number; `t = 0; t = t + s` is dense; `[s 2]` is sparse; `int32(s)` is
  `MATLAB:unimplementedSparseType`; `num2str` and `int2str` write it; `mat2str` writes
  `sparse(1, 1, 4, 1, 1)`; `fprintf` and `sprintf` refuse it (`MATLAB:printf:invalidInputAttribute`).
  Here every one of the sparse-answering functions, the number readers, the colon and `switch`
  refused a sparse argument, so the change of subscript needed the whole elementwise family taught
  first; `switch` matched nothing silently.
- **The verbs ADR 0364 left refused, all measured.** `sum`, `prod`, `mean`, `cumsum`, `cumprod`, `any`,
  `all`, `max`, `min` and `diag` of a complex sparse matrix answer sparse (`any` and `all` logical),
  `trace`, `norm` and `det` dense. `S.^p` is sparse whatever `p` (`S.^0` is all ones, `S.^-1` stores
  `Inf`); `p.^S` is dense; `S.^S` sparse. `S == S`, `S < T`, `S ~= T` between sparse matrices,
  `S < dense`, `1 < S` and `S == 0` are logical sparse; `S & T` and `S & 1` logical sparse, `S | 0`
  dense; `~S` and `xor(S, T)` logical sparse; `logical(S)` refuses NaN (`MATLAB:nologicalnan`) and a
  complex value (`MATLAB:nologicalcomplex`). `[S S]`, `[S; S]`, `[S dense]`, `[dense; S]`, `horzcat`,
  `vertcat` and `cat(1|2, ...)` are sparse, complex when a piece is, logical when every piece is,
  single when one is; an integer or char piece is `MATLAB:UnableToConvert`, `cat(3, S, S)`
  `MATLAB:catenate:sparseDimensionBad`. `S(i, j) = z` into a real matrix makes it complex, into a
  complex matrix keeps it complex until nothing imaginary is left; growth and deletion as for a real
  one; a logical target stays logical (`m(2) = 5` stores a one, NaN and complex refused). `A \ b`,
  `b / A`, `inv` of a complex sparse matrix answer as dense algebra does; `A \ sparse(b)` is sparse.
  The orderings (`symrcm`, `symamd`, `dmperm`) read the pattern of a complex matrix. `S ./ S`,
  `S .* dense`, `S ./ dense` are sparse, `2 ./ S` dense, `S^2`, `S^3`, `S^0.5`, `S^-1` sparse.
  `S + 0` is dense (this build kept it sparse).
- **MAT-files.** A `-v6` file holds a logical sparse matrix as the sparse class with the logical bit
  and a `0x1000` bit, its values one byte each; a single one as the *single* class with the `0x1000`
  bit, its values four bytes each. Both load in their class.

## Decision

### The class is a tag on the sparse wrapper (`JgsValue`, `JgsBuiltins.SparseClasses.cs`)

A logical sparse matrix is a `CscMatrix` of ones wrapped by `JgsValue.LogicalSparse`, whose kind rides
`PackedKind` (`IsLogicalSparse`); a single one is a `CscMatrix` whose stored values are rounded to
single, its wrapper carrying `JgsNumericClass.Single` (`IsSingleSparse`), real or complex. No new
storage: every solver and kernel that reads `CscMatrix` reads either. `class`, `islogical`,
`isnumeric`, `isfloat`, `full` (`SparseDense`), `whos` (one, four or eight bytes a value, sixteen or
eight for complex), the display (`sparse logical`, `sparse single`), `nonzeros` and `find`'s values
answer in the class. A verb that answers a fresh `JgsValue.Sparse` answers double, which is R2025b's
answer for arithmetic on a logical matrix; the verbs that keep a class put it back through
`SparseInClassOf` (a subscript, `'` and `.'`, unary minus for a single, an assignment, the reductions'
class carry in `Finish`). `sparse` answers a logical or single input in its class and refuses the
others in R2025b's words (MATLAB dialect). `logical`, `double` and `single` of a sparse matrix keep
it sparse; the integer classes are `MATLAB:unimplementedSparseType`.

### One road for every operator with a sparse operand (`SparseOperator`)

`Interpreter.ApplyBinary` hands any sparse operand to `JgsBuiltins.SparseOperator` before the class
machinery (which has no sparse storage to stamp), with the dense operator as a callback. The sparse
kernels (`SparseBinary`, `ComplexSparseBinary`) answer what they cover (`SparseKernelCovers`): two
sparse matrices of one shape under `+ - * .*`, a dense scalar scaling, a sum with a dense matrix, a
product with one, a real system solved. Everything else answers through the dense engine over the
operands read full, and is stored sparse again where R2025b's answer is (`SparseByDense`,
`SparseAnswerWanted`), so every class, expansion, error and power rule is the dense road's. A
comparison with a scalar still walks only the stored pattern when zero compares false
(`SparseCompareScalar`, moved from `SparseBinary`), and answers a logical mask. A single operand makes
the answer single; an integer operand is `MATLAB:sparseInteger`. `~S` is `SparseNot` in the MATLAB
dialect. `S + 0` leaves the kernel and is dense.

### One entry is a 1-by-1 sparse matrix, and reads as its number (MATLAB dialect)

`SparseSubscript` answers a one-entry read as a 1-by-1 sparse matrix in the MATLAB dialect; the JGS
dialect keeps its number. A third subscript is `MATLAB:ndFullOnly`. So that such a value goes where
R2025b's goes, the readers of a number take one: `Num` (every `zeros(s, 1)`-style size reader),
`ToDoubles` (real sparse reads as its column-major values), the colon's bounds, an index argument
(read full: a position, or a logical mask), `switch`, truthiness (`IsTruthy`: non-empty and every
entry non-zero, for any sparse matrix), and `sprintf`/`fprintf` (see Divergences).
`WrapSparseFunctions`, registered after every other wrapper, teaches the functions:

- the elementwise family (`sqrt`, `exp`, `log`, `log2`, `log10`, `log1p`, `expm1`, the trigonometric
  and hyperbolic functions and their inverses, `floor`, `ceil`, `round`, `fix`, `sign`, `isnan`,
  `isinf`) runs the dense function over the stored values and over zero, and keeps the pattern when
  zero maps to zero (`SparseMapped`); otherwise the matrix reads full; the answer is sparse;
- `max`, `min`, `mod`, `rem`, `atan2`, `tril`, `triu`, `reshape`, `kron`, `inv`, and `repmat` and
  `diag` of a complex matrix, read their arguments full and answer sparse when a data argument was
  (`repmat(7, 1, s)` is dense); the second output of `max` and `min` stays dense;
- `isfinite`, `hypot`, `trace`, `norm`, `det`, `int2str` and `num2str` read full and answer dense;
- `mat2str` writes `sparse(I, J, V, m, n)`; `spfun` and `spones` of a complex matrix use its entries;
  the orderings read a complex matrix's pattern.

The reductions in `RegisterMatlabReductions` read a complex, logical or single matrix full in its
class and answer `sum`, `prod`, `mean`, `any`, `all`, `cumsum` and `cumprod` sparse (`DenseAsSparse`);
`any` and `all` of a complex matrix read its pattern.

### Concatenation (`JgsBuiltins.SparseJoin.cs`)

A bracket row (`BuildArrayLiteral`) or matrix (`AssembleMatrix`) with a sparse piece goes to
`ConcatenateSparse`, which drops 0-by-0 pieces, checks heights along a row and widths down the rows
(`MATLAB:catenate:dimensionMismatch`), and answers in the combined class (complex, logical when every
piece is, single when one is). `horzcat`, `vertcat` and `cat(1|2)` reach it through the bracket
verbs; `cat(3, ...)` with a sparse piece is refused.

### Assignment of any kind (`SparseAssignedAnyKind`)

A complex target, or a complex value written into a real one, is written part by part through the
real road (`SparseAssigned`) over the real and imaginary parts, so every subscript form, growth,
deletion and refusal is that road's, and the answer is real when nothing imaginary is left. A logical
target stays logical and refuses NaN and complex values; a single one stays single. A sparse right-hand
side of any kind written into a dense target reads full (`SparseDense`).

### MAT-files (version 5)

The writer writes a logical sparse matrix as the sparse class with `FlagLogical | FlagSparseData` and
`miUINT8` values, a single one as the single class with `FlagSparseData` and `miSINGLE` values; the
reader loads both in their class (`FlagSparseData` is `0x1000`, the value `FlagDeletedHandle` has,
which only an object element reads).

### Also

`find` of a sparse row vector answers rows, as R2025b's does. `xor` of sparse matrices is logical
sparse.

## Consequences

- New parity fixture `complex_sparse_verbs` (338 rows) from R2025b 25.2, every row agreeing but the two
  divergences, green in the packed, boxed and managed-linear-algebra lanes, no boxed overlay. Solver
  and power rows are written to twelve digits: the complex dense power (`(1+2i).^2` is
  `-3+4.0000000000000018i` here) and the dense solvers differ from R2025b's sparse ones in the last bits.
- `MatlabStressM42Tests.SparseUnsupportedOperatorPointsAtFull` asserted `S ./ S` was refused; it is now
  `SparseElementwiseQuotientIsSparseLikeMatlabs`, R2025b's answer.
- Comparisons answer logical sparse in both dialects; the JGS dialect's one-entry subscript is still
  a number.
- A large sparse matrix under an operator or function with no sparse kernel is read full, where it was
  refused: `exp(S)` of a large `S` costs its dense size, as R2025b's does.

## Divergences

- **`sprintf` and `fprintf` print a sparse argument** (`w_sub_sprintf`, `o_fprintf`). R2025b refuses one
  (`MATLAB:printf:invalidInputAttribute`, "Function is not defined for sparse inputs."); here it prints
  the values it stands for: an accepted superset, kept because a one-entry subscript is now sparse and
  the house's own tests print `S(i, j)`.

Closed (struck in their ADRs): ADR 0167's **a sparse matrix holds real values** (`s_rhs_complex`) and
ADR 0234's **a logical sparse array is a double one** (`wh_file_12`, `wh_mem_12`).

## Still open

- **Complex sparse, still refused by name:** `lu`, `chol`, `ichol`, `ilu`, `eigs`, `svds` and the Krylov
  solvers (`pcg`, `gmres`, `bicgstab` and the rest) read `CscMatrix.Values` as reals and refuse a
  complex matrix ("does not take a complex sparse matrix here"); teaching them is a complex
  factorization in `JGraph.Numerics`, far bigger than this item. `colamd` does not exist here.
- **`kron` of a complex matrix** is refused, dense or sparse (the dense `kron` reads reals only).
- **Version 7.3 MAT-files:** a complex sparse matrix is refused by the writer, and a logical or single
  one is written double; the v7.3 reader loads a logical or single one as double. **The runtime conversions** and the
  Production Server encoders read a logical or single sparse matrix as a double one (`k14_mps_json`'s
  `e_sp_logical_*` rows stay ADR 0270's divergence).
- **Functions outside the families above** that read their arguments as dense arrays still refuse a
  sparse one, the 1-by-1 sparse matrix a subscript now answers included (R2025b takes it everywhere).
- **Single sparse arithmetic** is computed in double and rounded to single at the end, so a matrix
  product can differ from R2025b's single accumulation in the last bit.
- **`sparse(int8([1 2]), [1 2], v)`** (subscripts of two classes) answers here and is
  `MATLAB:sparse:constructorMixedType` in R2025b.
- **The display** of a sparse matrix is this build's (`1x3 sparse logical, 2 nonzeros`), as every
  display here is.
- **`true(n, 'like', p)`** with a sparse prototype is still dense and takes any sparse prototype (ADR
  0213's divergence); a logical sparse prototype now exists to check.
- **Dense findings on the way** (not this item's): `0.^1i` is 0 here and `NaN+NaNi` in R2025b;
  `[1 0 NaN] & 1` answers here and is `MATLAB:nologicalnan`; `any` of a complex dense matrix answers
  one value, not one per column; `amd` breaks ties otherwise than R2025b (`amd` of a 3-by-3 pattern
  `[3 1 2]` against `[1 2 3]`).
