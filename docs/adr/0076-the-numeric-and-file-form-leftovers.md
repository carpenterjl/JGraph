# 0076 — The numeric and file form leftovers

Date: 2026-08-22 · Milestone: M76 · Status: accepted

## Context

ADR 0070 closed its list with two deferrals. The callback seam became M71. The other was "about 55
forms across `fft`, `eig`, `lu`, `filter`, `cast`, `convhull`, `delaunay`, `speye`, `fgets`,
`fwrite`, `textscan`, `fopen` — independent of each other and of everything here", and the capability
report has carried it since as "a clean standalone wave". This is that wave.

It is form work in ADR 0069's sense — documented MATLAB syntax that runs — so the deliverable is the
form probe moving, accounted for form by form. It moved **1,109 → 1,289 accepted**, and the
accounting below separates what the code did from what the measurement did, because both changed.

## What the worklist actually contained

Reading it before planning split it three ways, and only the first two were scoped.

**Real gaps**, about fifty of them: arities that refused a documented argument, missing second
outputs, and two families with no kernel behind them at all.

**Two real crashes.** Every `qr` form and `linsolve(A, B)` did not fail — they *ended the process*.
`QrDecomposition.Factor` refuses a wide matrix with an `ArgumentException`, and nothing between a
builtin and `Main` caught it: the interpreter's `try` catches `JgsRuntimeException` and the runner
caught `JgsException`, so a script's own `try/catch` never saw it and `jgraph.exe` exited 127. The
`\` operator was the only site in the build that translated those exceptions, and it did so at one
call site rather than as a rule.

**Prober artifacts that were never gaps**, recorded here so no later milestone re-opens them:
`cast(A, newclass)` already worked (the harness substituted a logical for the placeholder word);
`delaunay(P)` already worked; `sparse(m, n)`, `sparse(i, j, v)` and `sparse(i, j, v, m, n)` already
existed; and every file verb's "2 is not an open file" was the prober never having called `fopen`.

**And four recorded crashes that never happened.** `[R,flag] = chol(___)`, `[___] = eig(___,outputForm)`,
`[___] = lu(___,outputForm)` and `[X,r] = linsolve(___)` were recorded as taking the process down.
They were a bug in `probe-forms.py`: the `___` expansion recovered the first form's arguments by
splitting its own generated statement on `"; "`, which shreds a sample matrix literal —
`chol([1 2 3; 4 5 6])` was cut inside its own brackets into `chol(])`, a parse error that failed the
whole probe *file*, which the isolation pass then blamed on the builtin. Four forms across four
commands were libelled by the measurement rather than by the build.

## Nothing a script does may end the process

The fix is one seam and one backstop, and it is the first thing in the wave because everything after
it depends on being able to run a probe.

`BuiltinFunction.Call` and `CallMultiple` now run the body inside a guard that translates the six
argument-shaped exception kinds — `ArgumentException`, `InvalidOperationException`,
`ArithmeticException`, `IndexOutOfRangeException`, `KeyNotFoundException`, `FormatException` — into
a `JgsRuntimeException` carrying the builtin's name and the exception's message with .NET's
`(Parameter 'x')` suffix stripped. The numeric layer's messages were already written as sentences
for a person to read, which is what makes this a translation rather than a paraphrase. A
`JgsException` passes through untouched; a cancellation and a script's own `exit` are control flow
and must reach the runner listening for them; anything else is a defect in this build rather than in
the script.

For that last case `JgsRunner.Run` and `JgsReplSession` gained a final `catch (Exception)` that
reports `Internal error: <type>: <message>` and fails the run. It earned itself immediately: an
`ArrayIndexOutOfRange`-shaped `NullReferenceException` in the new `fft` argument reader surfaced as
a reported internal error on the first probe of `fft(A, 2, 2)` instead of as a dead process.

**One form still reports taking the process down: `beep`.** Run by hand it works and exits 0, so the
verdict is a batching artifact — it says more about the audio device than about the build — and it
is left recorded rather than quietly skipped.

## What the wave built

### A real QZ, and the divergence it closes

`eig(A, B)` had no algorithm behind it. `qz` existed (M66) but was assembled from the ordinary Schur
form of `B⁻¹A`, which cannot be formed when `B` is singular — ADR 0066 recorded the refusal, and
frozen `stess_38` §20 asserted it.

`GeneralizedSchur` is the real iteration: a QR to make `B` triangular, Givens pairs to reduce the
pencil to Hessenberg-triangular form, and implicit double shifts whose bulge chase is *the same
rotation pair applied column by column*, so the reduction and the iteration are one loop written
once. The shift is the trailing 2-by-2 pencil's own eigenvalues, and never has to be computed as a
root: only their sum and product enter, and both are real even when the pair is not. Eigenvalues come
back as a pair (`Alpha`, `Beta`) rather than a quotient, because a zero denominator is the only
honest way to say infinity.

A singular `B` takes the reciprocal route. Factoring (`B`, `A + μB`) for a μ that makes the second
matrix nonsingular gives Q and Z that answer for the original pencil too — `Q·A·Z` is the triangular
factor less μ times the other, and a difference of a triangular and a quasi-triangular matrix is
quasi-triangular. One thing has to be put right, since this pencil's 2-by-2 blocks land in the matrix
required to be triangular, and a single rotation of rows per block moves each across into `AA` where
a real factorization is allowed to keep it.

**Two bugs in it were found by measuring, and both were invisible to reading.** The first: after the
double shift's two left rotations, B has a 3-by-3 bulge that nothing took back out, so the final
"write the structural zeros" pass was destroying real values — `Q·B·Z` and `BB` agreed exactly in the
upper triangle and differed everywhere below it, which is what named the cause. The second: the
in-sphere determinant in the new `Delaunay3D` was expanded with inverted cofactor signs, so every
point read as *outside* every circumsphere, nothing was ever inserted, and the answer came back
**empty rather than wrong** — a failure mode that a test asserting "some tetrahedra" catches and one
asserting a particular tetrahedron would also have caught, but that no amount of re-reading did.

`qz` and `ordqz` now route through the iteration, the singular-B refusal is deleted, and **frozen
`stess_38` §20 was amended with authorization** — the row left the refusals table and became a
positive assertion that a singular pencil factors and puts an exact zero on `BB`'s diagonal. That is
the second authorized frozen-asset amendment, and it is the same shape as M74's: a capability landed,
so the frozen assertion of its absence had to change.

### The decompositions' documented forms

`QrDecomposition` lost its `m ≥ n` restriction — the factorization runs over the first min(m, n)
columns, which makes a wide matrix an ordinary case rather than a refused one — and gained column
pivoting. `Cholesky` now reports the order at which it met a non-positive pivot, which is the whole
content of MATLAB's second output: failing at order q says the leading q−1 block *is* definite and
its factor is already computed, and that partial factor was always being returned with nothing to
say how much of it meant anything.

On top of those: `qr` of any shape with `[Q,R,P]`, `'vector'`/`'matrix'`, `'econ'` and the pair form
`[C,R] = qr(S,B)` that applies `Qᵀ` to a right-hand side without forming `Q`; `lu` with permutation
vectors and its four- and five-output forms; `chol` with `[R,flag]` and `[R,flag,P]`; `linsolve` with
an `opts` structure whose `LT`, `UT` and `TRANSA` genuinely change which solver runs, and a second
output that is the reciprocal condition number for a square matrix and the rank for one that is not;
`eig` with the pencil, left eigenvectors, `'chol'`/`'qz'`, `'balance'`/`'nobalance'` and
`'vector'`/`'matrix'`.

### The transforms, and the shape correction underneath them

`fft` of a matrix transformed all m·n elements as a single vector. MATLAB transforms each column, and
every other reduction in this build already walks the first non-singleton dimension, so the
correction makes `fft` agree with MATLAB and with its own neighbours at once. It is landed as a bug
fix in both dialects rather than as a dialect exception, on the user's decision, and no frozen script
transforms a matrix.

One helper does the work: slices along a dimension, over the real and imaginary planes separately
because the shape helpers are `double`-only, padding or truncating each slice, and calling the
existing arbitrary-length kernel. That gives `fft(X,n,dim)`, `ifft(Y,n,dim)`, `'symmetric'`,
`fft2(X,m,n)`, `fftn(X,sz)` over every dimension a value has — `fftn` was an alias of `fft2` and the
comment saying arrays have at most two dimensions was stale — and `fftshift`/`ifftshift` over one
dimension or all of them, which for a matrix is what swaps the quadrants rather than only the
columns. It also fixes **`ifft2(fft2(A))`, which could not be written at all**: the two-dimensional
reader refused a complex matrix, so its own output could not be fed back to it.

`filter` gained the initial and final conditions. The recurrence already carried exactly the vector
MATLAB calls the filter's state, so this seeds the delay line instead of clearing it and copies it
back out instead of discarding it — filtering a signal in two pieces now equals filtering it whole,
which is the entire point of the conditions. It also walks columns and takes a dimension.

### Two kernels in space

`convhull(x,y,z)` and `delaunay(x,y,z)` had nothing behind them: `JGraph.Math.Geometry` was planar
throughout. `ConvexHull3D` is quickhull, with faces kept outward-facing so that "can this point see
this face" is a single signed volume and the enclosed volume is a sum of signed tetrahedra with no
separate orientation pass. `Delaunay3D` is Bowyer–Watson with one dimension more. Both refuse
degenerate input by name rather than answering with a sliver, and both name the planar verb that *is*
the question with an answer for that input.

`convhull`'s `'Simplify'` was nearly a word that did nothing — the existing monotone chain always
drops points lying along an edge — so the flag was pushed down into the kernel and now genuinely
decides. The default stays this build's simplified hull rather than MATLAB's, which is recorded
below.

### The file family

The registry kept only a `FileStream`, so a script could open a file and then not ask this build what
it had opened. It now keeps the path, the permission, the byte order and the encoding, which is what
`fopen(fid)` and its four-output form are for. Modes gained `w+`, `a+`, `A` and `W`, and the text
flag is removed wherever it sits rather than trimmed off the end, so `"rb+"` and `"r+b"` are the same
request.

`fread` and `fwrite` gained the precision table's missing two thirds, the `'*class'` and
`'in=>out'` spellings, `sizeA` as a count or an `[m n]` shape, a skip, and a per-call byte order —
proved by writing big-endian and reading the same file both ways, which gives the swapped numbers
rather than the same ones. `fgets` gained its character count and both line readers gained the
terminator length. `frewind` did not exist and now does. `fprintf` answers how many bytes it wrote.

**The one change that unblocked four forms** is that the shared scan engine now reports how much text
it used. `fscanf` and `textscan` decoded the whole remainder of the file regardless of what the format
matched, so a bounded read left the position at EOF and the next read came back empty; both now seek
the stream to exactly where the scan stopped.

`textscan` was a single flat value wrapped in one cell. `JgsTextScanner` is a new column-aware engine:
the format is compiled once, one column accumulates per conversion, `%d` and `%u` land in integer
classes as MATLAB's do, and `%q` and `%[...]` are read. `sscanf` and `fscanf` keep the flat engine,
because their answer *is* one array and which conversion produced which element is not a question they
are asked. The option subset is `Delimiter`, `HeaderLines`, `Whitespace`, `EmptyValue` and
`CollectOutput`; every other pair refuses by name and lists the legal set.

## The measurement, and how it was corrected

The prober was fixed in four ways before it was trusted, and its own movement is reported separately
from the code's because both are real.

- **The `___` expansion** no longer re-parses the statement it just generated; `build_call` returns
  the argument list it already computed. That is what the four false crashes were.
- **An enumerated argument's types column is its list of legal words**, so a sample can be read
  straight off it. Reading it as prose was actively wrong: `outputForm` is documented
  `'matrix', 'vector'`, the keyword table saw the word "matrix" and handed `chol` a 2-by-3 matrix
  where it wanted the word.
- **The file verbs get a real open file**, written, closed and reopened for update by a prelude, in
  place of the id `2` they were being handed.
- **The decomposition and geometry families get samples of the right kind.** They document `A` as a
  "matrix" and then need a square one; probing `eig` with a 2-by-3 measured the prober's sample.

| Measurement | Before | After |
|---|---|---|
| Forms accepted (of 2,429) | 1,109 | **1,289** |
| Forms with an `error` verdict | 492 | **480** |
| Forms `unprobed` | 781 | **621** |
| Forms recorded as taking the process down | 25 | **1** (`beep`, a batching artifact) |
| Commands accepting a target axes | 92 of 92 | **97 of 97** |
| Builtins across every callable kind | 913 | **915** (`spalloc`, `frewind`) |
| Recorded divergences (index rows) | 96 | **107** |
| Tests | 4,922 | **5,029** |
| Stress scripts | 47 | **48** |

The divergence index moved by exactly the eleven this ADR records. ADR 0066's singular-`B` refusal
was retired from that ADR in the same commit, and it does not show in the index arithmetic because it
was written under a heading `harvest-divergences.py` does not read — which is worth saying rather than
leaving as an apparent slip.

**The accepted movement is accounted for by name, and the two causes are kept apart.** Of the forms
that became accepted, **85 are on names this wave touched and none were lost**: `qr` 9, `eig` 8,
`fread` 7, `lu` 7, `chol` 5, `fopen` 5, `fwrite` 5, `textscan` 5, `balance` 3, `convhull` 3, `ferror`
3, `fgets` 3, `fscanf` 3, `cast` 2, `delaunay` 2, `hess` 2, `ifft` 2, `ifftn` 2, `linsolve` 2, and one
each for `feof`, `fft`, `fftn`, `filter`, `ftell`, `qz` and `speye`. A further **133 gained and 38
lost on names the wave never touched**, entirely from the prober's own corrections — a form whose
sample changed from a number to the documented word it should always have been is a different
measurement of the same build, not a regression, and every one of the 38 was checked to be that.

`unprobed` is reported as it moved and folded into neither side, as it has been since M69.

## Recorded divergences

- **`fscanf` and `sscanf` answer a row where MATLAB answers a column.** Frozen scripts compare what
  they read against a row literal, so the shape is left as it stands. `eig` answered a row for the
  same reason until the orientation was corrected to MATLAB's column, which is what implicit
  expansion makes it: a row where a column belongs is an outer product rather than an error.
- **`[V, D] = eig(A, B)` needs a nonsingular B.** The eigenvalues of a singular pencil are answered
  in full, infinities included, but an eigenvector for an infinite eigenvalue is not computed and the
  form refuses by name rather than returning something for it.
- **`eig` accepts `'balance'` and `'nobalance'` and computes the same answer for both.** Balancing is
  a conditioning step rather than a change of result, and this build does not perform it.
- **`lu`'s four- and five-output forms work for a dense matrix**, where MATLAB restricts them to
  sparse. The extra outputs are identities, which is a true answer to `P·A·Q = L·U·D` rather than a
  refusal. The sparse LU folds its row permutation into `L`, so its reported permutations are
  identities too, and `lu(S, thresh)` reads and checks a threshold it has no dial to turn.
- **`chol`'s three-output form answers an identity permutation.** Nothing here reorders a sparse
  matrix to keep its factor sparse, so `Pᵀ·A·P = Rᵀ·R` holds as it stands.
- **A sparse matrix handed to `qr`, `chol`, `svd` or `eig` is filled in and factored densely.** The
  factors come back dense, which is a difference in storage rather than in the answer.
- **`convhull` simplifies by default**, dropping points that lie along one of its own edges, where
  MATLAB keeps them. `'Simplify', false` gets MATLAB's default behaviour, and both words genuinely
  act.
- **The bit precisions `bitN` and `ubitN` are refused by name.** They need a cursor that counts bits
  rather than bytes, and rounding one up to a byte would read the wrong thing quietly.
- **`fopen` reports a native byte order as `'ieee-le'` rather than `'n'`**, because that is the order
  it actually used, and the answer to what a file was opened with should be the order rather than the
  word that stood for it.
- **`textscan` reads five of MATLAB's option pairs** — `Delimiter`, `HeaderLines`, `Whitespace`,
  `EmptyValue`, `CollectOutput` — and refuses the rest by name with the legal set listed.
- **`eig` of a complex pencil is refused**, as `[V, D] = eig(A)` of a complex matrix already was:
  there is no complex eigenvector solver here.

## What is not done

- **`ordqz` reorders one eigenvalue at a time and refuses a 2-by-2 block.** Splitting a conjugate
  pair would take the factorization out of the reals, and moving the pair as a unit is not what a
  per-eigenvalue selection asks for.
- **`qr`'s sparse forms densify.** MATLAB's sparse QR is a different algorithm with different fill;
  what is tested here is that `R \ C` and `S \ B` agree.
- The `error` bucket stands at 480. The arity-refusal filter that scoped M70 can be re-applied to it,
  and the four false crashes this wave found are a reminder that a verdict is a lead rather than a
  finding.
- **`beep`'s crash verdict**, above.
