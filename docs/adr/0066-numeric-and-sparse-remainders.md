# ADR 0066 — Numeric and sparse remainders

## Status

Accepted (M66).

## Context

M61 through M65 were about the language: comma-separated lists, function files, string arrays, time
types, struct arrays. Each of them changed what a script *could express*, and each of them was one
idea implemented once. M66 is not that. It is the list of names and argument shapes that earlier
milestones deliberately deferred — nine preprocessing verbs, five numeric leftovers, eleven sparse
names, three dense ones — and it is a breadth milestone by construction: twenty-eight names, four
waves, no new value model, no new dispatch.

The plan called it a stabilization buffer after M65's flip, and that is what it was. It is also where
the M52 deferred table finally empties: every entry in it is now either closed or narrowed to a
sentence saying what is left.

Breadth milestones have a characteristic failure, and it is worth naming before the decisions: a
long list of small items invites each one to be implemented to the level that makes its own test
pass. The decisions below are mostly about resisting that — where the honest thing was one seam
instead of nine special cases, where an existing factorization already answered the question, and
where the thing MATLAB does could not be reproduced and had to be written down rather than
approximated in silence.

## Decision

### One seam gives the whole preprocessing family its time-awareness

Every name in wave A works on numbers, and every one of them is also asked about times: a gappy
temperature log is a datetime column beside a double column, and `fillmissing` has to answer for
both. Nine functions each learning what a datetime is would be nine places to get it wrong.

Instead there is one pair of helpers — `PrepStrip` and `PrepDress` — that takes a value apart into
the numbers the family works on, the shape it had, and the tag that says what those numbers meant.
A datetime arrives as milliseconds since the epoch and leaves as a datetime again. The family never
learns the type exists.

This is M64's reduction trick applied to a new family, and the point of recording it is the ratio:
the time-awareness the plan promised for `fillmissing` and `rmmissing` cost one helper, not nine
rewrites. Any later family that operates elementwise on numbers gets it the same way.

### `discretize` and `histcounts` share one edge chooser

`discretize(x, 5)` and `histcounts(x, 5)` must agree about where the five bins are. Two
implementations of one rule is how they would stop agreeing — not immediately, but the first time
one of them was tuned. So `discretize` calls `ChooseEdges` and `BinOf`, the same two functions
`histcounts` has called since M52, and the stress script asserts the agreement rather than assuming
it.

The one thing `discretize` adds is `'IncludedEdge', 'right'`, which is a genuinely different bin
rule and gets its own search rather than a flag threaded through the shared one.

### The relational operators order complex numbers by their real parts

MATLAB compares complex numbers with `<` by discarding the imaginary parts. This is a strange rule —
`1+9i` and `1-9i` compare equal under it, in both directions — but it is the rule, and refusing
instead meant a ported script stopped at a line MATLAB runs without comment.

What makes it worth an ADR entry is that `sort`, `max` and `min` do *not* follow it: they order by
magnitude, then by phase. So the language contains two orderings of the same values, and they
disagree. Both are implemented, both are tested together in one place, and the test says out loud
that the disagreement is correct — because the natural instinct on finding it would be to "fix" one
of them.

The implementation is a `byRealPart` flag on the broadcast helper rather than a pre-pass that strips
imaginary parts from every operand. A pre-pass would walk every array on every comparison; the flag
costs a branch on the element path that only complex elements ever take.

### `size` stayed lenient at both ends, on purpose

`size(A, dim)` answers 1 for a dimension past the value's rank, which is what MATLAB does, and it
also answers 1 for dimension zero, which MATLAB refuses. The wave that added `size(A, [1 2])` briefly
made the zero case an error — the more faithful behaviour — and that broke a JGS script in the test
suite.

The leniency stays. **JGS is frozen**, and a call that worked has to keep working; a refusal that is
more MATLAB-like is still a refusal of something that used to answer. The recorded limit is the whole
of it: `size(A, 0)` is 1 here and an error in MATLAB.

The multi-output side needed a matching change. `[r, c] = size(V)` folds every trailing dimension into
the last output, which is right when the call did not say which dimensions it wanted. When it did —
`[a, b] = size(V, [1 2])` — folding answers a different question, so the wrapper takes the dimension
list as one-to-one and pads with ones.

### The generalized Schur form is assembled, not iterated

`qz` is a QZ iteration in MATLAB — Hessenberg-triangular reduction followed by an implicit
double-shift sweep, a few hundred lines of delicate code. It was not written here, because for a
nonsingular `B` the answer already existed in two routines the repository had:

If `Z` is the real Schur basis of `B⁻¹A`, then `A·Z = B·Z·T`. Factor `B·Z = Qᵀ·R`. Then `Q·B·Z` is
`R`, upper triangular by construction, and `Q·A·Z` is `R·T`, which is quasi-upper-triangular because
an upper triangular matrix times a quasi-upper-triangular one is. Two existing factorizations, no new
iteration, and every relation holds exactly rather than to within a convergence tolerance.

`ordqz` is the same construction with a reordered Schur basis, which is why it can reuse
`Schur.Reorder` unchanged: the pencil's invariant subspaces are the matrix's, and moving them is a
question about `T`.

What the construction cannot do is a singular `B`, where the pencil has infinite eigenvalues and only
a real QZ iteration will reduce it. That is refused by name. **A factorization of a nearby pencil is
worse than no factorization**, because it is a wrong answer that looks like a right one, and the
script has no way to tell.

### Where an algorithm is not MATLAB's, the name says which one it is

Three of the sparse orderings and one dense routine are honest approximations of what MATLAB ships,
and each is written down at its definition rather than implied:

- **`amd`** here is an *exact* minimum-degree ordering, not the approximate one the name is short
  for. The approximation exists to make degree updates cheap on very large matrices; at the sizes a
  script builds interactively the exact version is both affordable and better.
- **`dissect`** bisects on breadth-first level sets — the cheapest thing that is genuinely a
  separator — rather than through a multilevel partitioner.
- **`dmperm`** answers the single-output form, a maximum matching by augmenting paths. The six-output
  Dulmage–Mendelsohn decomposition is not implemented.
- **`balance`** does the scaling half and not the permutation half, so `balance(A)` here is what
  MATLAB spells `balance(A, 'noperm')`.

The common shape: each still satisfies the contract a script can check for itself. An ordering is a
permutation; a balancing is a similarity. What a script *cannot* check is how good the ordering is,
and that is exactly what is recorded. The stress script asserts the checkable half — that every
ordering is a permutation of 1..n — and the ADR carries the rest.

`qz`'s real-versus-complex form belongs in the same list: MATLAB's default output is the complex
form, this produces the real one, and `'complex'` is refused by name. For a pencil with real
eigenvalues the two coincide; for a conjugate pair they do not, and the difference is a 2×2 block.

### The incomplete factorizations are checked where they can be exact

`ichol` and `ilu` drop everything outside the matrix's own pattern, which makes them preconditioners
rather than factorizations: `L·Lᵀ` is *near* `A`, not equal to it. A test that asserts a residual
below some tolerance is asserting a tolerance, not a factorization.

There is one case where they can be checked against something: a tridiagonal matrix's Cholesky factor
is bidiagonal, already inside the pattern, so dropping the fill drops nothing and both factorizations
reproduce the matrix exactly. That is the case the tests use, and it caught a real bug — the first
right-looking `ichol` never fired its update loop at all, because it looked for the upper-triangle
entries in a row store that had already discarded them, and every residual test with a tolerance in
it would have passed.

### A sparse matrix answers what it stores

Four verbs were unblocked, and all four had the same complaint: they silently discarded the sparsity
or refused outright, so choosing sparse storage bought a matrix that could be built and multiplied
and almost nothing else.

- A **scalar subscript** reads straight out of the compressed columns. Nothing is expanded.
- **`find`** walks the stored entries. This is the answer a sparse matrix is shaped to give.
- **Transpose** stays sparse. It used to come back dense, so a single quote quietly undid the storage
  decision.
- **Backslash** goes through the sparse factorization. The `LowerUpper` routine was refactored into a
  reusable pass so `Solve` could keep the pivot permutation the substitutions need — going through
  `[L, U]` and substituting afterwards would have meant recovering that permutation from L's pattern.

A subscript wider than one element is the one place that materializes the dense value, and above four
million elements it is refused by name pointing at `find`. A sparse matrix is exactly the shape that
has no business becoming dense: a 10⁶-by-10⁶ pattern is a few megabytes sparse and a petabyte dense.

**Sparse element assignment (`S(i,j) = v`) is not implemented** — a recorded limit. `sparse(i, j, v)`
is the idiomatic way to build one and it has worked since M42.

## Consequences

### Numbers

- **4,598 tests**, up from 4,514 at M65 — 84 new, across four new test files.
- **38 stress scripts**, all passing, with `stess_38.m` new: 21 self-checking sections.
- Coverage: base builtin table unmoved at 386/514, because all twenty-eight new names are documented
  as kind *function*. Across every callable kind, **905 of 2,027**, up from 880.
- Three of the twenty-eight — `normalize`, `discretize`, `groupsummary` — have no bare row in this
  install's index (they appear only as `double.normalize`, `datetime.discretize`,
  `table.groupsummary`), so they are implemented and uncountable, the same shape `writelines` had in
  M65.
- 0 build warnings.

### Two deliberate test flips

Both were assertions that a thing was refused, and both are now assertions of what it does:

1. `JgsComplexTests.Equality_Works_ButOrderingErrors` asserted that `1i < 2i` fails. It is now
   `Equality_Works_AndOrderingReadsTheRealPart`, and asserts that it answers false — with a second
   case showing that a sign flip in the imaginary part changes nothing.
2. `MatlabMovingWindowTests.SamplePointsIsRefusedByNameRatherThanIgnored` asserted the refusal. It is
   now `SamplePointsMakeTheWindowADistance`, and asserts the answer — plus the one thing sample points
   *do* refuse, which is padding, because padding needs places outside the data and sample points say
   there are none.

### What the wave found that was not its own

Two defects surfaced that predate the milestone, and both were surfaced by the same mechanism: adding
a name changed what an existing lookup found.

**A parameter named after a builtin took the builtin's value.** The `arguments` block asked
`env.TryGet(name)` to decide whether a caller had supplied a parameter. That walks outward to the
global scope, where every builtin lives — so a parameter named `factor`, `size` or `mode` looked
bound even when the caller left it out, and its declared default was skipped in favour of a function
handle. It had been latent since M62 and only became visible when M66 registered a builtin called
`factor` that a test's parameter was already named after. The fix is a `DeclaresLocally` lookup that
asks the frame about its own bindings, which is the question a frame was always asking.

The general lesson: **a lookup that walks outward is answering a different question from the one a
frame asks about its own parameters**, and the two agree until a name collides. Adding names is
exactly what makes them collide.

**The sparse solve was silently wrong before it was ever called.** `Solve` was written to do back
substitution row by row against a factor stored by column, which reads column `k`'s entries as though
they were row `k`'s. The probe caught it on a 3×3 system whose answer was `[1; 1; 1]` and which came
back `[1.5; 1.4; 1]`. Nothing in the repository had solved with a sparse matrix before, so there was
no regression to notice — the routine was new and wrong at once. Comparing against the dense
`full(A) \ b` in the same assertion is what makes it stay caught.

### Recorded limits

- `size(A, 0)` is 1 here, an error in MATLAB. JGS relies on the leniency.
- `balance` scales but does not permute.
- `qz` produces the real form; MATLAB's default is complex.
- `amd` is exact minimum degree, `dissect` bisects on level sets, `dmperm` is the single-output form.
- `ichol` and `ilu` are the zero-fill variants only; no drop tolerance, no `'nofill'`/`'crout'`
  options.
- `interp2` does `'linear'` and `'nearest'`; `'cubic'`, `'spline'` and `'makima'` are refused by name.
- `fillmissing` does not do `'spline'`, `'pchip'` or `'makima'`, for the same reason.
- `smoothdata`'s automatic window is a tenth of the data length, and `'SmoothingFactor'` replaces the
  tenth with the caller's fraction. MATLAB derives its default differently.
- `perms` refuses more than 10 values, naming the size the answer would have been.
- Sparse element assignment is not implemented.
- `normalize` and `fillmissing` refuse `'DataVariables'`: it picks variables out of a table, and these
  take arrays.

### Live checks for the user

Nothing in this milestone draws, which is unusual and worth saying — the whole of it is numbers. Two
things are still worth looking at in the running application:

1. A sparse matrix in the Workspace pane and the Data Viewer, after `S = sparse(...)` and `S'` — the
   transpose should still report itself as sparse, where before M66 it came back dense.
2. `stess_38.m` section 21 draws its cleaned trace. Run it from the Script Workspace with F5 and look
   at the figure: the raw series has a spike at sample 5 and gaps at 3 and 8, and the smoothed series
   drawn over it should have neither.

### Closed by a later milestone

Recorded above when this ADR was written and no longer true. It sits under a heading
`harvest-divergences.py` does not match, because an index that still names a closed divergence is
the stale claim that machinery exists to catch.

- **A singular `B` was refused by `qz`.** The construction here took the ordinary Schur form of
  `B⁻¹A`, which cannot be formed at all when `B` is singular, so the pencils with an eigenvalue at
  infinity — the ones a QZ iteration exists for — were refused by name. **Closed in M76**, which
  wrote the real iteration and routed `qz`, `ordqz` and the new `eig(A, B)` through it. Frozen
  `stess_38` section 20 turned from an assertion that this refuses into an assertion that it
  factors, which is the second authorized amendment to a frozen script. ADR 0076 has the reasoning.
