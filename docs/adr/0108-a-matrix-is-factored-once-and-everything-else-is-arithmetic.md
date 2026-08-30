# ADR 0108 — A matrix is factored once, and everything else is arithmetic

Milestone: **M107**
Status: accepted

## Context

Fifteen names were all that stood between this build and MATLAB's `matfun` folder: the elimination
`rref`, the plane rotation `planerot` and the two factorization updates over it (`qrinsert`,
`qrdelete`), the two conversions between a real factorization and a complex one (`cdf2rdf`,
`rsf2csf`), the eigenvalue conditioning `condeig`, the three estimators (`normest`, `condest`,
and `normest1` beneath the last of them), the Sylvester equation, the two least-squares solvers
`lsqminnorm` and `lscov`, the polynomial eigenproblem `polyeig`, a general function of a matrix
`funm`, and the generalized singular value decomposition `gsvd`. With them comes `decomposition`,
the object that keeps a factorization so that solving with it repeatedly costs one.

The plan's table numbered this row M106. That number was taken in the meantime by the coordinate
and special-function milestone, so this is M107 and ADR 0108.

Almost none of this is new arithmetic. `matfun` already held `lu`, `qr`, `svd`, `eig`, `schur`,
`qz`, `chol` and `ordschur`, every one of them on LAPACK since M89–M91. What was missing was the
layer above: the things a caller does *with* a factorization once it exists. So the decision that
shapes the whole milestone is that no name here computes a factorization it could have asked for.

## Decision

**A matrix is factored once, and everything above that is arithmetic over the factors.**

`condeig` is `eig` and a ratio of two lengths — the same `eig`, so its eigenvectors are the ones
`eig` would have handed over and its numbers cannot drift away from what `eig` says about the same
matrix. `condest` is one LU and one norm estimate. `rsf2csf` is one rotation per conjugate pair over
a Schur form that already exists. `polyeig` lays the polynomial out as a single large pencil and
hands it to `eig`, which is why `polyeig(A, B)` and `eig(A, -B)` agree to the last bit: they are the
same call. `funm` triangularizes once and then does nothing but multiply, divide and add.
`decomposition` is the idea stated outright.

The one place this rule is not merely tidy but load-bearing is `funm`. A function of a matrix with
repeated eigenvalues cannot be had from the eigenvectors, because there are not enough of them;
Schur–Parlett recovers each off-diagonal entry by dividing by the difference of two eigenvalues,
which is exact when they are far apart and worthless when they are close. So eigenvalues within a
tolerance of one another are gathered into a block, the division is never performed inside a block,
and the block's own value comes from a Taylor series about its mean — where being clustered is a
virtue, because it makes the nilpotent part small. The price is that the caller must supply not just
the function but its derivatives, which is why MATLAB's `funm` takes a two-argument handle and why
this one does too.

### The complex Schur form, which three names needed and none had

`funm` works over a strictly triangular form, and `sylvester` needs one whenever any of its three
matrices is complex. Neither existed here: `schur(A, 'complex')` had refused by name since it was
written, on the stated grounds that nothing else in the linear-algebra stack worked in complex
arithmetic.

For a real matrix it did not need to exist. The real Schur form is cheaper and better conditioned,
and `rsf2csf` — which this milestone was implementing anyway — turns one into the other. So
`schur(A, 'complex')` of a real matrix is `rsf2csf(schur(A))`, which is both what MATLAB's own
`funm` does and one fewer iteration to write. Only a matrix that is genuinely complex has no real
form to convert, and for that one the managed complex QR iteration that already computed eigenvalues
was taught to accumulate the unitary it was applying, and `zgees` was bound so the native lane uses
LAPACK's. The change to the iteration is two accumulation loops and one widened index range; the
eigenvalue path is untouched and still passes nothing.

### An eleven-named object with four factorizations behind it

`decomposition` documents eleven types. Three of them — banded, Hessenberg, permuted triangular —
name a sparsity pattern that a general LU exploits automatically and answers identically for, so
they share its code and differ only in what they refuse. Two more, triangular and diagonal, are
substitutions rather than factorizations. What is left is LU, Cholesky, LDL, QR and the complete
orthogonal decomposition, and the last of those was already needed by `lsqminnorm`.

What is not shared is the refusing. Asking for `'chol'` of a matrix that is not positive definite
has to fail rather than quietly give the LU answer, because the entire reason a caller names a type
is to assert something about the matrix; a type that silently degraded would turn an assertion into
a comment.

The object carries a scalar multiplier and a transposition flag, and neither refactors: multiplying
by three divides the answer by three, and transposing solves with the same factors the other way
round. That is what makes `3*dA'` free.

## A defect found beside the road

**`lsqminnorm` of a complex matrix answered the wrong vector, and the real case was perfect.**
A Householder reflector is applied with its scalar conjugated during the factorization and when
multiplying by Qᴴ, and unconjugated only when Q itself is formed — that is what LAPACK's own
`zgeqr2` and `zung2r` do, one line apart. Applying it unconjugated throughout is invisible in real
arithmetic, where the conjugate of the scalar is the scalar, and it makes the complex answer wrong
in a way that still looks like a vector of the right shape: `lsqminnorm([1i 0; 0 2], [1; 2])` came
back as `[1i; 1]` where the answer is `[-1i; 1]`. The factorization was written this milestone, so
nothing shipped with it, but it is worth recording because it is exactly the shape of bug that a
real-only test suite cannot see.

**`condest`'s witness was the estimator's second answer and should have been its third.** What
`condest` reports is the column of the inverse that attained the norm, not the unit vector that
produced it; MATLAB's own source discards the second output and keeps the third, and reading the
wrong one gives a vector that is unit, plausible and wrong. Caught by the parity run, which is the
only thing that could have caught it.

**`lscov`'s orthogonal path projected onto the wrong subspace.** Its covariance needs the part of
the correction the data did *not* determine, so the basis it subtracts is the row space of the
constrained problem — and the first reading built the complement of that instead, which is the same
projector with the sign of its complement. The degrees of freedom happened to come out right on the
case that exposed it, so the mean squared error agreed and only the standard errors did not.

### Divergences recorded here

- **`qrinsert` and `qrdelete` follow the algorithm MATLAB publishes rather than the one it runs.**
  MATLAB's own `.m` source performs each update as a sequence of `planerot` rotations, whose radius
  is always positive; its compiled path uses a sign-preserving rotation instead, and the two
  disagree in the sign of some entries of R and of the matching columns of Q. Both are valid
  factorizations — `Q*R` reproduces the matrix either way, to about 1e-15 — and MATLAB itself gives
  the published answer whenever the compiled path declines the inputs, which a sparse `Q` is enough
  to arrange. This build gives the published answer always.
- **`polyeig` refuses a complex coefficient matrix.** The linearized pencil goes to `eig`, whose
  pair form is real here, so a polynomial with complex coefficients is refused by name rather than
  answered. MATLAB accepts one.
- **`polyeig` with no arguments raises `MATLAB:minrhs`.** MATLAB raises `MATLAB:badsubscript`,
  which is what indexing the first of no arguments happens to produce there rather than a check it
  wrote; the message here says what is actually wrong.
- **A `decomposition` shows all of its properties, and they can be written.** MathWorks' object
  displays two — the size and the type — with the rest behind a link, and every property is
  read-only. Here it is a struct wearing a class name, which is how every object in this build is
  carried and is what gives it value semantics for nothing, so a script can see and set fields
  MATLAB would not let it. The object stays consistent if it does, because every solve reads the
  factors and not the fields.

## What this did not close

`decomposition(A, 'ldl')` of a hermitian matrix with a nought where a pivot must go falls back to a
general LU. There is no symmetric pivoting here, so `[0 1; 1 0]` has no LDL factorization to take.
That is not recorded as a divergence because it is not one a script can see: the object still
reports its type as `'ldl'` and answers what MATLAB answers, and only the factors behind it differ.


`decomposition` keeps its factors in a store beside the objects that name them rather than inside
them, and that store is cleared rather than pruned once it grows past what any script plausibly
holds at once. An object whose factors have been cleared takes them again from the matrix it still
carries, so the answers never change — only the promise of speed lapses, and only under a script
holding hundreds of live decompositions.

Four gaps the parity harness walked into are none of them this milestone's, and each is left to its
own task: `norm` refuses any complex argument; `tril` and `triu` refuse one too; `diag` of a matrix
answers a row where MATLAB answers a column, and so does `eig`; and an infinite eigenvalue of a
pencil is always `+Inf` here where MATLAB carries the sign — which is why `polyeig` of a singular
leading coefficient shows one `-Inf` there and four `+Inf` here.

`funm` of a block bigger than two under `@exp` takes the general Taylor path rather than a matrix
exponential of its own, because there is no complex `expm` here to call. A block is a cluster of
eigenvalues within a tenth of each other, so the series is well behaved by construction; the
measured difference against MATLAB on such a block is 1e-14 relative.

## Consequences

`matfun` goes from 10 of 25 names to **25 of 25**, and its accepted forms from 9 to **51 of 62** —
every one of M107's own 42 forms, with nothing unprobed and nothing refused. The toolbox count moves
to **268 of 377 names and 442 of 1,036 forms**, and the across-all-kinds builtin count to
**1,029 of 2,024**.

Two changes to the form prober came with it, and neither touches a name outside this milestone.
A form whose whole argument list is written as an ellipsis — `[F,exitflag] = funm(...)`,
`[x,stdx] = lscov(...)` — is the older dumps' spelling of `___`, and is now read that way; and a
trailing ellipsis is repetition when the run of placeholders immediately before it is a numbered
series, rather than only when the whole list is. Both were measured against the previous run: 42
forms moved from `unprobed` to `accepted` and every one of them is M107's.

## Measured

Against MATLAB R2024a on this machine, before anything was written and again afterwards. Ten probe
scripts established the behaviour, then a 239-line side-by-side run: **158 lines identical**, 19
more in which both answers are a residual norm below 1e-14 and therefore nought to working
precision, 58 within **1.14e-14** of their row's own scale, and **4 material differences** — the two
`qrinsert`/`qrdelete` sign rows, the sign of an infinite eigenvalue (which is `eig`'s and predates
this), and the identifier for `polyeig` with no arguments.

The five things that had to be measured rather than reasoned out:

- `lsqminnorm`'s default rank tolerance is `max(m,n) · eps · |R(1,1)|` from the pivoted
  factorization, and a tolerance the caller gives is an absolute threshold on that same diagonal —
  `lsqminnorm(A, b, 20)` reports rank nought.
- `decomposition`'s automatic type is `'lu'` for any square matrix that is not triangular, including
  a symmetric positive definite one; a diagonal matrix is `'triangular'` and not `'diagonal'`; and
  anything rectangular is `'qr'`.
- `isIllConditioned` is `rcond < eps` for every type but the two orthogonal ones, where it is
  instead a deficient rank.
- MATLAB's `hypot` of a complex argument is the hypotenuse of its magnitude, and MATLAB's `norm` of
  a two-vector is correctly rounded where `hypot` is not — and where .NET's `double.Hypot` is not
  either, differing from a correctly rounded length in 48 of 400 measured pairs. `planerot`'s radius
  follows `norm`, because that is what its source calls, so it is computed with an exact residual
  and one Newton step rather than by the obvious call.
- A `matfun` name that takes two matrices almost always constrains their shapes against each other,
  which no generic argument sample can know; the prober's samples for these fifteen are written as
  agreeing sets, and `qrinsert`'s pair is at order one on purpose, because its inserted piece is a
  column for `'col'` and a row for `'row'` and only a one-by-one factorization lets one sample be
  both.

## Testing

`tests/JGraph.Tests/Scripting/MatlabMatfunM107Tests.cs`, 63 tests. Where a defining property exists
it is asserted instead of a value — a factorization reproducing its matrix, a Sylvester solution
satisfying its equation, a generalized decomposition rebuilding both of its inputs, a polynomial
eigenvector making its own polynomial singular — because those are the promises a caller relies on
and they do not move when a last bit does. The whole suite is 6,353 tests with no failures and no
build warnings, five coverage verifiers green, and `stess_66` in the stress corpus asserting this
ADR's divergences against the running interpreter.

## Live checks

`funm`'s block table is the only thing this milestone writes rather than returns, so it is the only
thing an assertion cannot check. Run in both engines with `options.Display = 'on'` and again with
`'verbose'`, its four lines and the "Evaluating function of block" line above them come out
byte-identical — including the count of nought that MATLAB reports for an exponential block, which
is the zero its table was initialised to and never overwritten, because no series was taken.
