# ADR 0133 — Stop where MATLAB stops

## Status

Accepted (M128, 2026-09-07). Built by an implementation agent in its own worktree and landed by
the integrator under the arrangement ADR 0131 records; landed third, after M131 and M127.

## Context

`sparfun` had five of its thirty-five documented names. The thirty that were missing fall into
three kinds: the eleven iterative solvers — `pcg`, `bicg`, `bicgstab`, `bicgstabl`, `cgs`, `gmres`,
`lsqr`, `minres`, `qmr`, `symmlq`, `tfqmr` — with `svds` beside them; the sparse constructors and
readers `spdiags`, `spfun`, `spones`, `spconvert`, `spaugment`, `sprank`, `colperm`, `sprandn`,
`sprandsym`; and the four drawing verbs `treelayout`, `treeplot`, `etreeplot`, `gplot` with `unmesh`.

The plan's strong claim for this milestone was that `flag` and `iter` would be **exact** against
R2025b for every solver on every fixture problem. An iterative solver's recurrence is textbook and
was never the risk. What decides `iter` is everything around it: when a convergence test is
believed, what happens when it is re-measured and fails, how many further sweeps that buys, what
counts as stagnation, and whether the answer handed back is the last iterate or the best one. Those
rules are MATLAB's, they are in the `.m` files, and they are the reason the title is what it is.

## Decision

### One frame, eleven recurrences

`KrylovSolver` carries the bookkeeping once, in four partials: the tolerance clamp and its warning,
the two early answers (an all-zero right-hand side; an initial guess already inside the tolerance),
the residual history, the re-test of a passed convergence test against `b − A·x`, the extra sweeps a
failed re-test buys, the three-consecutive-stagnant-iterates rule, and the closing choice between
the last iterate and the best. Each solver contributes its recurrence and nothing else.

Three rules were taken verbatim from the sources, because each decides `iter`. A convergence test
that passes on the recurrence's residual is never believed until it is re-measured against
`b − A·x`, since the recurrence drifts. When the re-measurement disagrees the solver does not stop:
it grants itself `min(⌊n/50⌋, 5, n − maxit)` further iterations — ten for the `bicgstab` pair,
`4·ℓ` for `bicgstabl` — and calls the run stagnant only after those. And an update below
`eps·‖x‖` three times running is stagnation whatever the residual says. `pcg` and `lsqr` reject a
tolerance *equal* to `eps` where the other nine accept it — one character of difference in their
sources, and it decides whether a warning is raised.

### What each solver keeps to itself

`bicgstab` counts in halves and `bicgstabl` in quarters, because each produces more than one
iterate per pass and either can be the one that converges. `tfqmr` runs to `2·maxit` and halves the
count on the way out. `gmres` measures its relative residual against `‖M⁻¹b‖` rather than `‖b‖` —
alone in the family — reports `[outer inner]`, and prints one number when it was not restarted and
the pair when it was. `lsqr` stops on `‖Aᵀr‖/(‖A‖‖r‖) ≤ tol` *before* taking its step, so the
iteration it reports is one below the loop index. `minres` and `symmlq` read their residual norm off
the recurrence when there is no preconditioner and off a carried residual vector when there is,
which is why the same problem stops at different iterations with and without one; both can answer
flag 5, a preconditioner that is not positive definite, which no other solver here can.

### The operator boundary, and why a triangular preconditioner is substituted

`KrylovOperator` is the whole of what a solver knows about its matrix: `A·x`, and `Aᵀ·x` for the
three that need it. A matrix answers both from its storage; a function handle answers whichever it
was written for, with MATLAB's trailing `'notransp'`/`'transp'` flag — and only for `bicg`, `qmr`
and `lsqr`, which is why a `pcg` preconditioner handle takes one argument and a `bicg` one takes
two. A preconditioner given as a matrix goes through `CscSolver`, which recognises a triangular
matrix and substitutes rather than factoring. That is the faithful path as well as the cheap one:
MATLAB's `mldivide` tests triangularity first, so `L\x` for an `ichol` factor is a substitution
sweep there too, and a sweep and an LU do not round alike — an iteration count that turns on the
last bit of a preconditioned residual would part company. A non-triangular preconditioner is
factored once and the factors reused, which needed `CscMatrix.Factorize` split into a factor step
and a solve step.

### `svds` takes two routes, and multiplicity is why

A Krylov space grown from one starting vector contains one vector per *distinct* singular value, so
a value of multiplicity two is found once and the third answer handed back is the fourth singular
value. The five-point Laplacian on a square grid — the first thing anyone tries `svds` on — is full
of repeats, and the first implementation answered `[7.464, 6.732, 6.000]` where MATLAB answers
`[7.464, 6.732, 6.732]`. Up to a smaller dimension of 512 the whole decomposition is therefore taken
through the engine's own `Svd` and the wanted end picked off it: exact, never fails to converge, and
at that size cheaper than being careful. Above it, Golub–Kahan–Lanczos bidiagonalization with full
reorthogonalization, the Ritz residual as the test. Bidiagonalizing rather than running Arnoldi on
`[0 A; Aᵀ 0]` is itself about multiplicity: the augmented spectrum comes in `±σ` pairs, and a
symmetric solver asked to separate a pair degenerate to working precision hands back vectors mixed
between the two. Every selection answers largest-first, `'smallest'` included — that is what MATLAB
does, and it is not obvious.

### The constructors, the drawing verbs, and the one line people get wrong

Which end of a column of `spdiags`'s compact form a short diagonal sits at depends on whether the
matrix is taller than it is wide, not on the diagonal: for `m ≥ n` the compact row is `i + d`, for
`m < n` it is `i`. The fixture pins it from both sides. `sprank` is Hopcroft–Karp on the bipartite
graph of the pattern, the same number MATLAB reaches as `sum(dmperm(A) > 0)`. `treelayout` needed
`fixparent` as well as the layout sweep: MATLAB's own `treeplot` example, `[2 4 2 0 6 4 6]`, is not
in elimination order, and without the renumbering the picture is wrong in one node — which the
fixture caught, one line out of 747. The drawing verbs go through the `JG.Plot` facade like any
chart. `spconvert` refuses the four-column complex list by name, because sparse storage here holds
no complex values.

### The matrix–vector product is threaded by rows, and is the same to the bit

`CscMatrix.MultiplyVector` is now written by rows and cut into row blocks through
`ParallelKernels.ForBlocks`. Rows rather than columns is what makes threading possible at all: a
column-wise accumulation scatters into `y` and two threads would need a lock or a private copy
each, where a row's answer is one dot product owned by one thread. The answer is identical at any
thread count — the split is between rows and never inside one — and identical to the
column-scattered form it replaces, because within a row the stored entries are in increasing column
order either way. A unit test compares the two bit for bit, as M120's rule requires.

### What is declined, and what turned out to be present already

`colamd`, `equilibrate`, `spparms` and `svdsketch` are declined: orderings and scaling change
nothing in an engine that factors densely, and `issparse` already says "stored densely". The plan
also listed `amd`, `symamd` and `dissect` as declines; all three have been registered since M66
(`JgsBuiltins.SparseOrderings.cs`), each with the divergence its file records, and nothing was done
to them. `sparfun` therefore has exactly four missing names, the four above.

### Reference sources

Read for the documented behaviour and the constants that define it, nothing copied: R2025b's
`toolbox/matlab/sparfun/pcg.m`, `gmres.m`, `bicg.m`, `bicgstab.m`, `bicgstabl.m`, `cgs.m`,
`minres.m`, `symmlq.m`, `qmr.m`, `tfqmr.m`, `lsqr.m`, `svds.m`, `spdiags.m`, `spfun.m`, `spones.m`,
`spconvert.m`, `spaugment.m`, `sprandn.m`, `sprandsym.m`, `sprank.m`, `colperm.m`, `treelayout.m`,
`unmesh.m`, `numgrid.m`, `delsq.m`, `rjr.m`, `private/iterchk.m`, `iterapp.m`, `itermsg.m`; and
`toolbox/matlab/graphics/math/treeplot.m`, `etreeplot.m`, `gplot.m`. Taken from them: the default
tolerance 1e-6; the default `maxit` `min(n, 20)` (`gmres` `min(⌈n/restart⌉, 10)` restarted and
`min(n, 10)` not; `minres` floored at 1 where the others floor at 0); `maxstagsteps` 3; the
`maxmsteps` rule above; `ℓ = 2` fixed in `bicgstabl.m`; `svds`'s tolerance 1e-10 and subspace
`max(3k, 15)`; `unmesh`'s quantization grid `round(eps^(−1/3))`; `spaugment`'s default
`c = max(|A|)/1000`; and the five message templates with the four iteration-count spellings of
`itermsg.m`, whose English was read out of R2025b's message catalogue by a probe — the text is not
in the `.m` file, and "Tolerance may not be achievable. Use a larger tolerance." is nothing like
what its identifier suggests.

## Consequences

**The fixture.** `m128_sparse.m`: 747 lines, 0 unexplained against R2025b, no `div=` line. Every
`flag` and every `iter` is **exact** — `bicgstab`'s halves, `bicgstabl`'s quarters, `gmres`'s
`[outer inner]`, `tfqmr`'s halved count, and the `ichol`, `ilu` and Jacobi-preconditioned runs.
Two things in it are deliberate and are not looseness. `relres` for a *converged* run is pinned as
`relres ≤ tol`, not as a number: at convergence it is a quantity of size `eps`, and MATLAB's
8.7e-16 against JGraph's 1.1e-15 on the same iterate is a 26 % difference between two zeros. Every
non-converged `relres` is pinned numerically at `rel=1e-8` and agrees. `resvec` is pinned by length
(exact) and first entry (`rel=1e-12`) for the same reason.

**Tests.** `MatlabSparseKrylovM128Tests` 20 plus the bit-for-bit product test; the assembly at
7,724 on the default lane; four lanes green. `stess_76.m` is thirteen sections and passes on
`jgraph.exe` and on R2025b alike. The `d14` list is a new `Sparse` group of 77 forms, all accepted
today; the two `d16` rows — `pcg` with `ichol` on a million-unknown Poisson matrix, `gmres(20)` on
a convection–diffusion matrix — are written and wait for the arc's timing session.

**Coverage.** `sparfun` goes from 5 to 31 of 35 names; the toolbox total from 296 to 322 of 377.

## Divergences

None. M128 adds no difference from MATLAB that a script can observe.

## Still open

- **`svds`'s option forms are refused:** `svds(A, k, sigma, Name, Value)`, `svds(A, k, sigma,
  opts)` and `svds(Afun, n, …)`. The inner options are plumbed as parameters of
  `SparseSingularValues.Compute` and not surfaced; wiring them moves no numerics.
- **`svds` above a smaller dimension of 512 runs the iteration** and can therefore miss a repeated
  singular value there. Documented in the file; no fixture line reaches that size.
- **`sprandsym(S, [], rc, 3)`**, the rank-one-per-off-diagonal kind, is not built; its output is
  random and nothing about it is pinnable.
- **`sprandn`/`sprandsym` values cannot match MATLAB's for a seed**: their `randn` is dsfmt19937
  and the positions are drawn with replacement. The fixture pins `nnz`, size, pattern and symmetry.
- **`numgrid` and `delsq` are still missing**, and `gallery('poisson', n)` is refused because the
  family builder does not construct sparse matrices. The fixture, the tests and the stress script
  build the five-point Laplacian from `sparse(i, j, s)` instead, verified in R2025b to be the same
  matrix. A short milestone would make several MATLAB examples run verbatim.
- **`abs` on a sparse value is refused**, so `max(max(abs(A)))` — a phrase `spaugment.m` itself
  uses — must be written over `full(A)` in anything that runs on both engines. A sparse-arithmetic
  question for whoever next touches the sparse value.
