# ADR 0374 — A sparse argument reaches every everyday function as R2025b's does, version 7.3 MAT-files and the encoders keep a sparse matrix's class, and an integer beside a sparse matrix is refused

## Status

Accepted, 2026-11-15. Open item 337 and item 340's integer beside a sparse matrix (chips wave 7,
lane 7B), left open by ADR 0369.

## Context

All the expectations below were measured in R2025b 25.2 headless (`-noFigureWindows -batch`), with
probes `p7b_a` (200 cases), `p7b_b` (141), `p7b_c` (78), `p7b_mat` (the HDF5 layout of five `-v7.3`
variables read back with `h5info` and `h5read`) and `p7b_mps` (`mps.json` of logical and single sparse
arrays), one line per case in both engines.

- **Wrong answers, not refusals.** ADR 0369 made `S(i, j)` a 1-by-1 sparse matrix and taught the
  elementwise family and the number readers. Many other functions read a sparse argument as its
  stored values or as one element and answered wrongly, silently: `fliplr(S)` and `rot90(S)` a row,
  `flipud(S)`, `flip(S)`, `permute(S, [2 1])`, `transpose(S)` and `ctranspose(S)` the matrix
  unchanged, `isscalar(S)`, `isrow(S)`, `iscolumn(S)` and `isvector(S)` of a 2-by-3 matrix true,
  `issorted(S)` true, `ismember(s, [1 2 3])` and `ismember(3, S)` false, `trapz(S(1, :))` 0,
  `cumtrapz` empty, `not(S)`, `and(S, 1)` and `or(S, 0)` one value. Others refused in JGraph's words
  (`length`, `angle`, `cast`, `num2cell`, `polyval`, `nthroot`, `unique`, `mode`, `dot`, `factorial`,
  `gcd`, `lcm`, `rad2deg`, `sind`, `realpow`, `nextpow2`, `pow2`, `isprime`), and the statistics and
  rearrangements (`median`, `std`, `var`, `sort`, `diff`, `circshift`, `rescale`, `normalize`,
  `cummax`, `cummin`, `uminus`, `uplus`) answered dense where R2025b answers sparse.
- **What R2025b answers.** The statistics, rearrangements and one-argument functions above answer
  sparse in the class of the matrix (`median(F)` single, `sort(L)` logical), their second outputs
  (`[m, i] = sort(S)`, `[u, ia, ic] = unique(S)`, `[m, f] = mode(S)`) dense; `dot` is sparse when both
  vectors are and dense beside a dense one; `length`, `issorted`, `ismember`, `polyval` and `isprime`
  answer dense; `not(S)` and `and(S, x)` are logical sparse, `or(S, x)` and `xor(S, x)` sparse only
  when both sides are; `uplus(L)` and `uminus(L)` are double sparse, `transpose(L)` logical;
  `cast(S, 'single')` is single sparse, `cast(S, 'int8')` `MATLAB:unimplementedSparseType`,
  `cast(1, 'like', L)` logical sparse, and `cast(S, 'like', single(1))` dense single; `num2cell(S)`
  is a cell of 1-by-1 sparse matrices in the matrix's class. A uniform `cellfun` whose first answer
  is sparse is `MATLAB:cellfun:SparseNotSupported` ("Sparse Arrays are not supported. See SPFUN."),
  and a later 1-by-1 sparse answer is collected as its number; `arrayfun` refuses a sparse input
  (`MATLAB:arrayfun:SparseInputNotSupported`) and a first sparse answer
  (`MATLAB:arrayfun:SparseNotSupported`). R2025b refuses `string(S)` (`MATLAB:invalidConversion`,
  "Conversion to string from sparse double is not possible.", `logical` and `single` named for
  theirs, `double` for a complex one), `char(S)` (`MATLAB:unimplementedSparseType`; a logical one
  `MATLAB:invalidConversion`, a complex one `MATLAB:noConversionComplexToChar`), `interp1` (sample
  values first, `MATLAB:griddedInterpolant:SparseInterpValuesErrId`, then coordinates and query points,
  `SparseDataPtErrId`), `histc` (`InvalidFirstInput`, then `InvalidSecondInput`), `bitand`, `bitor`,
  `bitxor`, `bitshift` (`MATLAB:Bitand:sparseArgs` and kin, "Inputs must be full."), `gamma` and
  `gammaln` (`notFullReal`), `rand(n, 'like', S)` and `randn` (`sparsePrototype`).
- **Construction.** `sparse(i, j, v)` refuses subscripts of two classes
  (`MATLAB:sparse:constructorMixedType`, "Subscripts must be the same data type (single, double, or
  integer)."); a logical or char list reads as double, and the third input's class is checked first.
  `true(n, 'like', p)` and `false` answer logical sparse for a logical sparse prototype and refuse any
  other sparse one (`MATLAB:True:invalidInputClass`); `zeros`, `ones` and `eye` with `'like'` answer in
  the prototype's class, complex (flag and all) when it is. `kron` of a complex matrix answers, dense
  or sparse, real again when nothing imaginary is left (`kron(complex([1 2], 0), [1 1])` is real).
- **An integer beside a sparse matrix** is `MATLAB:sparseInteger` under every relational and
  elementwise logical operator too (`sparse(1) == int8(1)`, `int8(2) < S`, `int8(2) & S`,
  `S | int8(0)`, `S ~= uint8(1)`); here the comparisons and `&`, `|` answered.
- **Version 7.3 MAT-files.** R2025b writes a logical sparse matrix as class `logical` with
  `MATLAB_int_decode` 1 and uint8 values, a single one as class `single` with four-byte floats, and a
  complex one's values as the compound of `real` and `imag` in its class's width (an empty matrix
  has `jc` alone); every one loads in its class. Here a complex one was refused and a logical or
  single one written and read as double.
- **`mps.json` and the protobuf encoders.** `mps.json.encode` writes a logical sparse array as
  `mwtype` `logical` with the rows and columns alone (a value list there is refused by R2025b's
  decoder) and a single one as `mwtype` `single`, real or complex; both decode in their class. The
  protobuf schema's sparse message carries `type` DOUBLE (0), LOGICAL (1) or SINGLE (2) and the
  values in `mwsparsedouble`, `logical` or `mwsparsesingle` (muserve's `out.sparselogical`,
  recorded by K16). Here every one was double.

## Decision

### The functions that read a sparse argument as a dense array (`JgsBuiltins.SparseLeftovers.cs`)

`WrapSparseFunctions` (ADR 0369) gains names in its three lists: the elementwise family takes the
degree forms, `sec`, `csc`, `cot`, `rad2deg`, `deg2rad`, `nextpow2`, `pow2` and `factorial`; the
functions that read full and answer sparse take the statistics, the rearrangements, `angle`,
`nthroot`, `realpow`, `gcd` and `lcm` (both data arguments for the last two); the functions that read
full and answer dense take `issorted`, `ismember`, `polyval` and `isprime`. Its local wrapper became
`WrapOverSparse`, shared with `WrapSparseLeftovers`, registered at its end, which answers:

- the shape questions (`length`, `isrow`, `iscolumn`, `isvector`, `isscalar`) from the extent, so a
  large matrix is never read full for them;
- `transpose` and `ctranspose` as the operators answer them, in the class;
- `not` as `~S`; `and` sparse when either side is, `or` when both are, each read full;
- `dot`, sparse when both vectors are; `cast` by name and with `'like'`; `num2cell` into a cell of
  sparse matrices in the class;
- R2025b's refusals for `string`, `char`, the bit functions, `gamma`, `gammaln`, `histc` and
  `interp1`, in their order.

`uminus`, `uplus` and `xor` are the interpreter's operator forms (`RegisterOperatorFunctions`,
registered after the builtins), so their sparse readings sit there: `uminus(S)` is the unary
operator's sparse answer (`SparseNegated`), `uplus(L)` a double sparse matrix, and `xor` sparse only
when both sides are. The dense `and`, `or` and `xor` function forms answer a flat row for a matrix
(below); a sparse answer takes the operand's shape (`LogicalVerbShaped`).

`full` of a real 1-by-1 sparse matrix answers the scalar it holds (`SparseDenseScalar`), so
`full(S(i, j))` goes wherever a dense scalar goes. A uniform `cellfun` or `arrayfun` answer that is
sparse goes through `UniformSparseAnswer` (MATLAB dialect): the first refused, a later 1-by-1 one
read as its number; `arrayfun` refuses a sparse input before walking.

### Construction

`sparse(i, j, v)` checks the two subscript lists' classes after the third input's (MATLAB dialect;
`SubscriptClassWord`). `ConstructorTraits` hands a sparse `'like'` prototype to `SparseLikePrototype`,
which stores the answer in the prototype's class and complexity and refuses it for `rand` and
`randn`; `true`/`false` with `'like'` take a logical sparse prototype only. `kron` multiplies complex
factors itself (`KroneckerProduct`), and the sparse wrapper stores the answer sparse as before.

### An integer beside a sparse matrix (`SparseOperator`)

The `MATLAB:sparseInteger` check at the top of `SparseOperator` covers the relational operators and
`&`, `|` as well as the arithmetic ones. It is inside the sparse operator path, not in
`ApplyBinary`, so no dense operand reaches it.

### Version 7.3 MAT-files (`MatV73Writer.Sparse`, `MatV73Reader.ReadSparse`)

The writer takes the sparse value whole and writes R2025b's layout for each class: `logical` with
`MATLAB_int_decode` and one byte a value, `single` with four-byte floats, a compound `data` for a
complex one in its class's width. The reader reads the class attribute and a compound `data`, and
loads a logical, single or complex matrix in its class (`CompoundParts`), a complex one flag and all.

### The encoders and the runtime conversions

`JgsMpsJson` writes a logical sparse array as `mwtype` `logical` with two lists (`LogicalSparse`), a
single one as `mwtype` `single` with single's number notation (real and complex), and its decoder
takes `single` and loads `logical` and `single` in their class; a logical one with a value list is
refused in the words R2025b printed before it ended its process (ADR 0270's rule). `JgsMpsProtobuf`
writes `type` 1 or 2 with the `logical` or `mwsparsesingle` payload and reads `type` back into the
class. `RuntimeValueConversion.ToEngine` makes a logical `RuntimeSparse` a logical sparse matrix.

## Consequences

- New parity fixture `sparse_leftovers` (410 rows) from R2025b 25.2, every row agreeing in the
  packed and boxed lanes, no boxed overlay, no divergence row. A JGraph-written version 7.3 file of
  each class loads in R2025b in its class, and R2025b's loads here (probe `p7b_x`).
- `k14_mps_json` rows `e_sp_logical_small`, `e_sp_logical_large`, `e_sp_logical_round` and
  `y_sparse_logical` were rewritten to `exact` by hand and dropped from the fixture's diverging list;
  rule 0 kept this lane from running that fixture (it is the main session's to run).
- A large sparse matrix under `sort`, `median`, `circshift` and the other read-full functions costs
  its dense size, as ADR 0369 accepted for the elementwise family; the shape questions do not.
- `arrayfun` over a sparse matrix is refused in the MATLAB dialect, as R2025b refuses it; the JGS
  dialect still walks one.

## Divergences

- **`sprintf` and `fprintf` print a sparse argument** (ADR 0369's divergence, reconsidered and kept).
  R2025b refuses every sparse argument (`MATLAB:printf:invalidInputAttribute`), the 1-by-1 one
  included (`f_sprintf_*` in probe `p7b_a`); the accepted superset stays because it misleads no one
  and the house's tests and scripts print `S(i, j)` (the keep-JGraph-extras rule).

Closed (struck in their ADRs): ADR 0213's **`true(n, 'like', p)` with a sparse prototype is dense**
(`oi_silent_answers`' `true_like_sparse`, now also `k_true_like*` and `c_true_like*`), ADR 0270's
**a sparse logical array is a sparse double** (`k14_mps_json`), and the logical and single part of
ADR 0272's **a sparse logical, single or complex array is double** and of ADR 0266's sparse
logical sentence.

## Still open

- **`amd`'s tie order.** R2025b's `amd` is SuiteSparse AMD (approximate degrees, aggressive
  absorption, supervariables found by hashing, degree lists taken last in, first out); a path of four
  is `[4 3 1 2]` there and `[1 2 3 4]` here, an arrow `[5 4 3 2 1]` against `[2 3 4 1 5]`, the 3-by-3
  tridiagonal `[3 1 2]` against `[1 2 3]`. Matching it is a transcription of AMD's `amd_2`, not a
  rule that can be read off these cases.
- **Single sparse arithmetic** is still computed in double and rounded to single; the measured small
  products and sums agree, a long accumulation may not in the last bit.
- **`ismember(L, true)`** of a logical sparse matrix is logical sparse in R2025b and dense here (a
  double sparse matrix's answer is dense in both).
- **`accumarray(subs, v)` with a sparse `v`** answers dense here, sparse in R2025b.
- **Version 5 MAT-files** still write a single complex sparse matrix as a double one.
- **The runtime** has no single or complex `RuntimeSparse`: `FromEngine` of a single sparse matrix is
  an opaque value, of a complex one refused; the protobuf encoder refuses a complex sparse array.
- **Dense leftovers found on the way** (not this item's): the `and`, `or` and `xor` function forms
  answer a flat row for a matrix (`and([1 0 0; 1 1 1], ...)` is 1-by-6); `num2cell(single([1 2]))`
  answers doubles; `[m, f] = mode(x)` is `MATLAB:maxlhs`; `num2cell(A, dim)` is `MATLAB:maxrhs`;
  `median` of a complex dense matrix is refused; `kron(int8(...), double)` answers where R2025b refuses
  `MATLAB:mixedClasses`; `sparse('ab', ...)` refuses a char subscript list R2025b reads as codes;
  `datestr(3, 'yyyy')` is refused (R2025b `0000`); `ReadComplex` in the v7.3 reader loads a complex
  single array as double.
