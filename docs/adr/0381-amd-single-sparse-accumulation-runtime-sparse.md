# ADR 0381 — amd is SuiteSparse AMD, single sparse arithmetic accumulates in single, and the sparse leftovers of ADR 0374 answer as R2025b's

## Status

Accepted, 2026-11-16. Open item 345 and two parts of open item 346 (`sparse('ab', ...)` and the
version 7.3 reader's complex single), chips wave 8, lane 8B.

## Context

Every expectation below was measured in R2025b 25.2 headless (`-noFigureWindows -batch`), with
probes `p8b_a` (125 cases), `p8b_b` (63), `p8b_c` (25), `p8b_d` (21), `p8b_e` (37), `p8b_f` (22) and
`p8b_mat_save` (R2025b's `-v6` files read byte by byte), one line per case in both engines and
diffed.

- **`amd`'s tie order.** R2025b's `amd` is SuiteSparse AMD: approximate degrees, aggressive
  absorption, supervariables found by hashing, degree lists taken last in first out, dense rows
  (degree past `max(16, 10·√n)`, at most n) set aside and ordered last, and the assembly tree
  postordered with each node's largest child last. A path of four is `[4 3 1 2]`, a 5-by-5 grid
  `[21 22 16 17 25 ...]`; here the exact minimum-degree ordering answered `[1 2 3 4]` and every
  tie the other way. R2025b also answers `[p, Info] = amd(...)` (AMD's 20-element Info, statistics
  and memory included), takes a control struct (`dense`, `aggressive`, exact field names, the
  first element of a struct array) or a numeric Control vector `[dense aggressive]`, and refuses
  in its words: `needFloatOrLogical` for any class but double, single and logical (before the
  shape), `needMatrix` for an N-D array, `MATLAB:square` for a non-square one, `invalidInput` for a
  control that is neither, `denseNonNumeric` (a logical too) and `denseScalar`,
  `aggressiveNonNumeric` (a logical is accepted) and `aggressiveScalar`. Here `amd` refused a
  second argument and a second output and ordered a non-square matrix.
- **Single sparse arithmetic.** R2025b reduces a single sparse matrix in single, one stored entry
  after another: `sum(sparse(single([1e8; 3; 3])))` is 1e8, and so are `sum(F, 'all')` (column
  major), `cumsum`, `prod` (`4097·4097·3` is 50356224, each product rounded), `cumprod` and `mean`
  (the single sum over the count); `'double'` accumulates in double and answers double. A matrix
  product with a single factor and a sparse one forms each product in single and adds them in
  single in the order of the inner index, with no fused multiply-add: `[4097 1] * [4097; 1]` is
  16785408, `[3 3 1e8] * [1; 1; 1]` is 100000008 and `[3 1e8 3] * [1; 1; 1]` 1e8, sparse times
  dense and dense times sparse alike, complex products too. Here everything was summed in double
  and rounded once, which agrees until a long accumulation (`sum` of a thousand `single(0.1)` is
  99.999046 there, 100 here).
- **`ismember` against a one-element set.** R2025b's `ismember.m` answers a one-element `B` with
  `A == B`, so a sparse operand on either side answers a logical sparse matrix (`ismember(L, true)`,
  `ismember(S, true)`, `ismember([1 2 3], sparse(2))`) and `locb` is `double(lia)`, sparse too; an
  empty `A` is `false(size(A))`, dense; a larger set reads both full and answers dense. NaN is never
  a member, a complex value compares as one, and two classes neither of which is double are
  `MATLAB:ISMEMBER:InvalidInputsDataType`. Here every answer was dense, `ismember(S, NaN)` found
  the NaN and `ismember(S, 1i)` did not find `1i`.
- **`accumarray` with sparse values.** R2025b answers sparse when the values are sparse or the sixth
  argument asks (`accumarray(subs, v, sz, fun, fill, true)`, which JGraph refused as too many
  arguments), in the values' class, summing in arrival order (single values in single). For a
  sparse answer it refuses subscripts of more than two columns (`nonmatrixSparseOutput`), logical
  or char values (`nonfloatValues`), a fill other than zero (`nonzeroFillvalSparseOutput`), a size
  other than `[N 1]` for a column of subscripts (`badSzInputVecInd`), values that are not a vector
  one a row (`badSizeValInputMatInd`), a function answer that is not a double or single number
  (`MATLAB:sparsfcn:nondoubleFunctionOutput`, `nonScalarOutput`), sparse subscripts
  (`badIndInput`) and sparse values beside an explicit false (`badClassValInput`).
- **`sparse` with char arguments.** R2025b reads a char subscript list and a char size as character
  codes (`sparse('ab', 'ab', [1 2])` is 98-by-98) and a char or string third of three arguments as
  the class of an all-zero matrix (`sparse(2, 3, 'single')`; any other word is
  `MATLAB:sparse:invalidTypename`, read before the sizes, which must then be scalars,
  `twoNonScalarInputs`). Here every char argument was refused.
- **MAT-files of complex single arrays.** R2025b's `-v6`/`-v7` file holds a single complex sparse
  matrix with flags `0x1807` (single, complex, sparse data) and four-byte parts, and a dense complex
  single as class single with four-byte parts; its `-v7.3` file holds a dense complex single as the
  compound of two four-byte floats, class `single`. JGraph wrote all of them as double and read a
  complex single back as double, in both formats (`ReadComplex` in the version 7.3 reader).
- **The runtime and the protobuf encoder.** `RuntimeSparse` held double and logical matrices only:
  `FromEngine` of a single sparse matrix was an opaque value and of a complex one a refusal, and
  `JgsMpsProtobuf` refused to write a complex sparse array and read one as real.

## Decision

### amd (`SuiteSparseAmd.cs`, `JgsBuiltins.SparseAmd.cs`)

`SuiteSparseAmd` is a statement-for-statement C# transcription of SuiteSparse AMD's `amd_order`,
`amd_aat`, `amd_1`, `amd_2`, `amd_postorder` and `amd_post_tree` (BSD-3-Clause, the licence carried
in the file's header), with 64-bit integers as in the `SuiteSparse_long` build, the default Control
(dense 10, aggressive on) and the same workspace sizes (`slen = nzaat + nzaat/5 + 7n`), so garbage
collection and the Info statistics land where AMD's do. A transcription rather than a rule: the
ties fall out of the order the degree lists, the hash buckets and the element lists are walked,
and nothing read off a few answers reproduces them. `AmdOrdering` checks the argument in R2025b's
order, reads the control (`AmdControl`), and answers the permutation and, asked, the Info column.
MATLAB code reaches it; JGS code keeps the exact minimum-degree ordering it always had, and
`symamd` (R2025b's is COLAMD's, a different algorithm) stays on that ordering in both dialects.

### Single sparse arithmetic (`JgsBuiltins.SparseSingleArithmetic.cs`)

`SingleSparseReduction` answers `sum`, `prod`, `mean`, `cumsum` and `cumprod` of a single sparse
matrix (real or complex) in single, walking each output's inputs in column-major order, `'double'`
in double; it hands back to the general road for an empty matrix, a dimension past the second or
an argument it does not read. `SingleSparseProduct` answers `A * B` where a factor is single and an
operand sparse, forming each product and each sum in single over the inner index (over B's stored
entries when B is sparse, every index when it is dense); a scalar factor stays the elementwise
road's. The reductions hook in as a separate early block in `WrapColumnwise`'s MATLAB-dialect
body; the product at the top of `SparseOperator`'s arithmetic.

### ismember and accumarray (`JgsBuiltins.SparseAccumulate.cs`)

`WrapSparseMembership`, registered at the end of `WrapSparseLeftovers`, wraps `ismember` (a call with
a sparse argument and two arguments: the one-element set compared with `==`, the empty `A`, the
class rule) and re-registers `accumarray` so a sparse argument or a sixth one reaches
`SparseAccumArray`, which applies R2025b's checks and sums or reduces the bins itself (a function
is called once a bin, on a column of the values' class).

### sparse's char arguments (`JgsBuiltins.SparseCharSubscripts.cs`)

`SparseTextArguments`, a separate early block at the top of `sparse` in MATLAB code, reads char
subscripts and sizes as codes and answers `sparse(m, n, typename)`.

### MAT-files (`MatFileWriter`, `MatFileReader`, `MatV73Writer`, `MatV73Reader.ReadComplex`)

The version 5 writer writes a complex single dense array as single and a complex single sparse one
as `mxSINGLE | complex | sparse data` with four-byte parts; the reader stamps a dense complex single
single (the sparse road already did). The version 7.3 writer writes a dense complex single as the
four-byte compound with class `single`, and `ReadComplex` loads it single. A complex integer has no
class here to load in, so it stays double.

### The runtime and the encoders (`RuntimeSparse`, `RuntimeValueConversion`, `JgsMpsProtobuf`)

`RuntimeSparse` gains a public constructor taking a class (`double`, `single`, `logical`) and
optional imaginary parts, and `Imaginary` and `IsComplex`; a single one is held to single
precision. `FromEngine` makes a single or complex sparse matrix a `RuntimeSparse` of its class and
`ToEngine` takes it back in its class, flag and all. The consumers that have no form for a complex
sparse matrix refuse it in their own words (the native .NET API, the COM add-in, the C API's
returned arrays, the native wire); `MWNumericArray` takes and gives a complex sparse matrix
(a single one as double, the only sparse class it holds). `JgsMpsProtobuf` writes a complex sparse
array with `isComplex` and its imaginary parts in the values message's second field, as a dense
complex array carries them, and reads it back complex in its class.

## Consequences

- New parity fixture `sparse_amd_single_accum` (283 rows) from R2025b 25.2, every row agreeing in
  the packed and boxed lanes, no boxed overlay, no divergence row.
- `MpsProtobufTests` gains a complex sparse round trip (double and single); `RuntimeValueTests`
  gains single, logical, complex and complex single sparse samples and three refusals.
- The protobuf encoding of a complex sparse array follows the dense one's layout. The landing ran
  `ProductionServerTests.TheServerAnswersMathWorksClientsAsMuserveDid` in the main checkout (the
  Python, .NET and C clients against R2025b's muserve recording) with the K16 cases
  `c/in.sparse.complex`, `raw/in.sparse.logical`, `raw/in.sparse.single`, `c/in.sparse.logical`,
  `raw/out.sparselogical` and `c/out.sparselogical` off its divergence list: all six agree with
  muserve, so they left the list and `compare-k16.py`'s KNOWN, and ADR 0272's "a sparse complex
  array is refused" closed.
- A large sparse matrix under the single reductions is read full (its dense size), as ADR 0374
  accepted for the statistics.

## Divergences

None new.

## Still open

- **`symamd`** is still JGraph's exact minimum-degree ordering; R2025b's is SuiteSparse COLAMD's
  `symamd` (`symamd` of a path of four is `[1 2 3 4]` there too, a 5-by-5 grid is not), and it
  refuses a non-square matrix (`MATLAB:symamdmex:InvalidInput4`) where JGraph orders it.
- **`numgrid`** is not a JGraph function (`delsq(numgrid('L', 8))` is refused).
- **Dense single reductions and products** are still computed in double and rounded:
  `sum(single([1e8 3 3]))` is 100000008 here and 1e8 in R2025b, `single([4097 1]) * single([4097; 1])`
  16785410 against 16785408, and `norm(single(...), 1)` likewise; R2025b's dense `sum` of a long
  single vector is blocked (100.00023 for a thousand `single(0.1)`), not sequential.
- **Dense `accumarray` of single values** answers double (`accumarray([1; 1; 1], single([1e8; 3; 3]))`).
- **`ismember(..., 'legacy')`** is refused here; `ismember(L, true, 'rows')` refuses in JGraph's words.
- **Complex integer arrays** are not a JGraph class, so a version 7.3 file's complex `int16` loads as
  double.
- **A string scalar as `sparse`'s size** (`sparse(i, j, v, "c", 100)`) answers here;
  R2025b's is `MATLAB:sparsfcn:negSizeOfSparse`.
