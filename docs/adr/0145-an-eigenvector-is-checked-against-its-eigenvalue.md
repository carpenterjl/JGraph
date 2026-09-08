# ADR 0145 — An eigenvector is checked against its eigenvalue

## Status

Accepted (M141).

## Context

The managed backend's `eig` returned, for a matrix with two well-separated eigenvalues, the same
eigenvector twice:

```matlab
>> [V, D] = eig([4 1; 2 3])          % eigenvalues 5 and 2
V =
    0.7071    0.7071
    0.7071    0.7071                 % both columns are the eigenvector for 5
>> norm(A*V - V*D)
    3.0000                           % LAPACK and R2025b give 2.2e-16
```

`[2 1; 0 3]` failed the same way. `det(V)` came back exactly nought for every defective matrix
probed, where LAPACK returns a V that is singular only to rounding.

**The cause is the start vector.** The managed backend finds an eigenvector by inverse iteration:
solve against `A − λ̃I` a few times, and whatever component of the start vector lies along the
wanted eigenvector is amplified until it is all that is left. A start with *none* of that component
has nothing to amplify — the iterate does not move, and the agreement test that ends the loop reads
not moving as convergence. The start was the flat vector, and the flat vector is not neutral: it
*is* an eigenvector of some matrices. `[1; 1]` is `[4 1; 2 3]`'s, for the eigenvalue 5. Asking that
matrix for its *other* eigenvector started exactly on the wrong one and never left.

The comment above the loop already recorded a near miss of this — an earlier fixed count of three
solves had been raised to twelve because "three is one short whenever the starting vector is
orthogonal to the left eigenvector of the value wanted". More iterations help when rounding seeds a
little of the missing component and it grows; they do not help when the early-exit test fires
first, which is what happens here.

**Nothing caught it for as long as nothing divided by V.** A sweep of forty general matrices passes
on both backends, no test asserted an eigen*vector* rather than an eigenvalue — deliberately, since
which vectors an eigensolver returns and in what order is its own business — and every consumer in
the project used eigen*values*. What found it was `s ^ A`, MATLAB's `V * diag(s .^ diag(D)) / V`,
which is the first thing here to invert V: it came back as `22.67` on the native lane, matching
R2025b, and `-592770333` on the managed one. That milestone was reverted for it (commit 27f8a79)
and re-lands on top of this.

## Decision

### The answer is measured against the property it claims

An eigenvector's whole claim is that `A·v − λ·v` is nought, and that is now checked. If the flat
start's answer fails, the iteration is run again from each axis in turn and the best residual wins.
No single vector can be orthogonal to every eigenvector at once, so the axes are enough to fall back
on, and a degenerate start cannot survive all of them.

The flat start is still tried first and still answers almost every matrix on the first attempt, so
nothing that worked before now costs more than one residual — an O(n²) measurement against the
O(n³) solve it is checking.

The acceptance threshold is deliberately loose, at `1e-7` relative to the largest entry. It is not
there to police accuracy: the iteration perturbs its shift by about `1e-10` to keep the system out
of exact singularity, which caps what a defective matrix can reach. It is there to catch an answer
that is not an eigenvector at all, and a residual of 3 against a matrix of scale 4 is that.

## Consequences

`[4 1; 2 3]` goes from a residual of 3.0 to 2.2e-16 and `[2 1; 0 3]` from 1.0 to exactly nought.
The forty-matrix sweep that passed before still passes, on both backends, and no eigenvalue moves —
only the vectors that were wrong change.

A provider test now holds both backends to the property for six matrices, including the two
regressions, a defective one, and a conjugate pair. It asserts the residual and not the vectors,
because the vectors and their order legitimately differ between backends and a test that pinned
them would be wrong.

### Divergences

- **A defective matrix's eigenvectors are accurate only to about 1e-10 on the managed backend**,
  where LAPACK reaches working precision. The shift is perturbed by `(scale + 1) * 1e-10` to keep
  `A − λ̃I` out of exact singularity, and that perturbation is the floor on the answer:
  `[2 1; 0 2]` has a residual of 4.4e-16 natively and 2.1e-10 here. Tightening it means an
  eigenvector routine that solves the exactly-singular system properly — LAPACK's `dtrevc` on the
  Schur form, which perturbs a zero pivot rather than the whole shift — and that is a rewrite of
  this routine rather than a constant to change.
