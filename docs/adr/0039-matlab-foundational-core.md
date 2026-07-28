# ADR 0039 — The MATLAB foundational core

## Status

Accepted (M36, 2026-07-27).

## Context

The user supplied a minimum-viable command set for a MATLAB stand-in (~100 items across matrix
generation, shape manipulation, linear algebra, reductions, control flow, environment, file I/O, and
graphics) with the standing requirement that every feature JGraph ships behaves identically — as the
user observes it — to real MATLAB. An audit against the M35 engine found seven gaps: builtins could
not produce multiple outputs, most matrix generators and shape verbs were missing, `\ / ^` between
arrays threw, reductions flattened matrices instead of reducing columns, `whos`/`help`/`format` did
not exist, there was no `save`/`load` or byte-level file access, and `image` was absent.
[matlab-foundational-coverage.md](../matlab-foundational-coverage.md) tracks the list item by item.

## Decision

### Builtins produce multiple outputs

`BuiltinFunction` implements `IJgsMultiCallable` through an optional `MultiOutput` delegate
(arguments, wanted, line, column → outputs). The interpreter's `EvaluateForOutputs` already
dispatched through `IJgsMultiCallable` for user functions, so no interpreter change was needed; the
M31-era `MultiOutputBuiltin` wrapper class dissolved into the same mechanism. `[X, Y] = meshgrid(x,
y)` — the gap ADR 0038 recorded — works now, along with `[r, c] = size(A)` (with MATLAB's fold/pad
rules), `[m, i] = max/min`, `[s, i] = sort`, `[r, c, v] = find` (column-major subscripts), and
`ind2sub`. meshgrid's one-output form is `X` alone in the MATLAB dialect while JGS keeps the pair
its `let [X, Y] =` destructuring documents.

### MATLAB semantics are dialect-conditional; JGS behavior is pinned

Column-wise reductions (`sum(A)` over a matrix → per-column row vector; `sum(A, 2)` → per-row
column; `sum(A, 'all')`; the same for prod/mean/median/std/variance/mode/any/all/cumsum/cumprod/
diff/sort and the max/min family incl. `max(A, [], dim)` and the elementwise two-argument form) and
square constructors (`zeros(n)`, `ones(n)`, `rand(n)`, `randn(n)` as n-by-n) apply in the MATLAB
dialect only, by re-registering wrappers after the base builtins — the established
`dialect.IndexBase`/`OnOff` pattern. JGS keeps its documented flat reductions and count-shaped
constructors, pinned by the existing suites.

### Dense linear algebra lives in JGraph.Numerics.LinearAlgebra

Pivoted LU (det, inv, the square `\`), Householder QR (least squares, economy Q), one-sided Jacobi
SVD (singular values to full precision; rank with MATLAB's default tolerance; the matrix 2-norm),
and eigen decomposition — cyclic Jacobi for symmetric input (ascending, MATLAB's order), Hessenberg
reduction plus complex single-shift QR for general real matrices, with eigenvectors recovered by
inverse iteration and conjugate pairs symmetrized. `Linear.Solve` dispatches `\`'s three shapes:
LU when square, least squares when tall, minimum-norm via QR of Aᵀ when wide (MATLAB returns a
*basic* solution there — a deliberate, documented difference). Real input only: complex matrices
error clearly rather than losing their imaginary parts.

The MATLAB lexer gained `\` and `.\`; the parser folds both into the multiplicative level.
`MatrixOperation` grew `\` (solve), `/` (as `(Bᵀ\Aᵀ)ᵀ`), and matrix `^` for integer exponents,
replacing the old "not implemented" errors. Failures surface MATLAB's own vocabulary ("singular to
working precision") but as errors, not warnings-plus-Inf.

Builtins: `inv`, `det`, `rank`, `trace`, `norm` (vector p/inf norms; matrix 1, 2, inf, 'fro'),
`eig`, `lu` (`[L,U]` folds P into L; `[L,U,P]` is the strict form), `qr`, `svd` — the
decompositions' multiple outputs ride the new seam. `dot` conjugates its first operand.

### Shape and generation verbs

`eye`, `diag` (both directions, offset k), `magic` (all three constructions match MATLAB —
verified against magic(3/4/6)), `logspace` (with the `(a, pi)` special case), `ndims`, `reshape`
(column-major, `[]` wildcard), `cat`/`horzcat`/`vertcat`, `flip`/`fliplr`/`flipud`, `squeeze`
(a 2-D no-op), `permute` (2-D orders), `prod`, `ismember`, `transpose`/`ctranspose` function forms.
Vectors here have no row/column orientation, so the flip/transpose family treats them as rows —
consistent with the interpreter's own `'` operator.

`isequal` now compares logicals and doubles by value (`isequal(mask, [1 0])` is true), as MATLAB
does — the one JGS-visible semantic change in this milestone, and a strict correction.

### Environment

`whos` is session-owned like `clear` (only the session knows which names the user created) and
prints an aligned name/size/class table. `help` reads `JgsBuiltinCatalog` — signature plus summary,
zero new metadata to maintain. `format` switches numeric display through one process-wide
`JgsNumberFormat` (the same one-console reasoning as the figure registry), reset whenever a scope
is created: the default stays JGraph's full round-trip precision (= `format long`), `short` trims
to 5 significant digits with integers exact, `shortE`/`longE` force exponent notation, and
`compact`/`loose` are accepted unchanged because the console already writes MATLAB-compact output
(the user's M35 decision).

### MAT-file v5, for real interop

`MatFile/MatFileWriter` + `MatFileReader` speak the level-5 binary format: doubles (scalar, vector,
matrix), complex, logical, char, cell, and scalar struct. The writer emits uncompressed
little-endian elements (MATLAB reads those unconditionally); the reader additionally handles
zlib-compressed elements (MATLAB's default since R2006b), the small-element tag form, and every
integer/float encoding widened to double. Objects, sparse matrices, and struct arrays report what
they are instead of mis-reading. Matrices cross the row-major/column-major boundary in the writer
and reader, not in scripts.

`save`/`load` are declared beside `clear` by the workspace owners (the session and the one-shot
runner — `JgsWorkspaceIo.DefineSaveLoad`), because only the owner knows which names the user
created. Both support command syntax (`save state.mat`, `load state.mat x`), which required the
command-syntax parser to glue dotted file names (`results.mat`) and `-option` words (`-ascii`) into
single word arguments. `-ascii` writes G8 text rows and `load file.txt` reads a numeric matrix named
after the file. One knowing deviation: `load` always declares into the workspace, so `S = load(f)`
both returns the struct and defines the variables — distinguishing the two forms needs nargout
plumbing the interpreter does not have.

### File handles

A per-run id table lives on `JGraphScriptGlobals` (ids from 3; 1 and 2 are the consoles), closed by
`fclose('all')`, session teardown, and the one-shot runner's `finally`. `fopen` returns −1 on
failure, MATLAB's testable convention. `fread`/`fwrite` move binary values in uint8…double
precisions (uint8 default, as MATLAB); `fgetl` returns lines without newlines and the number −1 at
EOF; `fprintf(fid, …)` writes formatted text to 1 (console), 2 (error console), or an open file.

### The documented-command checklist

`tools/matlab-checklist/build-checklist.py` filters the raw R2021b dump (56,824 rows) to its
documented subset (11,063 rows, 2,027 of them callable commands) while keeping the original app
shell and its click-to-check localStorage workflow. It pre-seeds the done-state with every command
JGraph implements — read straight out of `JgsBuiltinCatalog.cs` plus the session builtins, keywords,
and operators — applied only when the browser has no saved progress, so the user's own ticks always
win. Output: `matlab-r2021b-documented.html` in the demo workspace, 7.6 MB instead of 25.

## Consequences

- The multi-output seam ends the "builtins return one value" era; new decompositions register both
  forms in one `Define` call.
- MATLAB-dialect scripts moving matrices through reductions now get MATLAB's answers; scripts that
  (necessarily) never did this see no change, because the old behavior was an error.
- MAT interop is real but bounded: the supported-type list above, verified by a live round-trip
  against the user's MATLAB machine (pending).
- Known gaps, tracked and deliberate: `rng(seed)`; complex input to the decompositions; N-D arrays;
  struct arrays and objects in MAT-files; `qr`/`svd` return economy-size factors.

## Testing

`MatlabMultiOutputTests`, `MatlabShapeBuiltinTests`, `MatlabColumnwiseReductionTests`,
`LinearAlgebraTests` (kernel-level, MATLAB-verified fixtures, identity-based decomposition checks),
`MatlabLinearAlgebraBuiltinTests`, `MatlabEnvironmentBuiltinTests`, `MatFileRoundTripTests`
(including hand-crafted small-element/int16 and zlib-compressed fixtures the writer never emits),
and `MatlabFileIoTests`. Expected values are MATLAB's own; decomposition tests verify defining
identities (P·A = L·U, A·v = λ·v) rather than pinning sign conventions.
