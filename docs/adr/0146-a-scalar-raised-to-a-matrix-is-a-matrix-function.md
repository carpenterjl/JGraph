# ADR 0146 — A scalar raised to a matrix is a matrix function

## Status

Accepted (M142).

## Context

ADR 0143 left this at the top of its own divergence list. `2 ^ [1 2; 3 4]` answered `[2 4; 8 16]`
here and `[10.48 14.15; 21.23 31.71]` in R2025b: a wrong number, silently, with nothing in the shape
of the answer to give it away — it is a 2-by-2 either way. The dispatch had a branch for `A ^ p` and
none for `s ^ A`, so the pair fell past the matrix operators and reached `.^`.

**MATLAB's rule was measured, and it is the eigendecomposition.**
`[V, D] = eig(A); V * diag(s .^ diag(D)) / V` reproduces `s ^ A` with `norm(s^A - f, 'fro')` exactly
zero on every matrix with a full set of eigenvectors that was tried.

**And on a matrix without one, that rule is wrong.** `[2 1; 0 2]` has the single eigenvalue 2 and a
single eigenvector; `eig` hands the formula the same column twice, `V` is singular, and
`V * diag(4, 4) / V` collapses to `4·I` whatever the division does with it. R2025b answers
`[4 0; 0 4]`. The Jordan form says the answer is `[4 4·ln2; 0 4]`, and every other road agrees:

```matlab
>> expm(log(2) * [2 1; 0 2])          % 4.0000   2.7726
                                      %      0   4.0000
>> funm([2 1; 0 2], @(x, k) log(2).^k .* 2.^x)      % the same
>> 2 ^ [2 1; 0 2]                     % 4 0; 0 4 — and this one is MATLAB's
```

That is not roundoff. `4·ln2` is 2.77, and the entry MATLAB drops is the largest off-diagonal in the
answer. The same hole opens wherever an eigenvalue repeats without bringing an eigenvector with it:
`2 ^ [5 -1 0; 1 3 0; 0 0 3]` is `[27.09 -11.09 0; 11.09 4.91 0; 0 0 8]` and R2025b answers
`[16 0 0; 0 16 0; 0 0 8]`, which is not close to it in any norm.

An earlier attempt at this milestone transcribed MATLAB's formula, on the principle that matching is
the point. It could not be landed, and the reason is worth recording, because it is the argument for
what replaced it. The transcription reproduced `[4 0; 0 4]` on the native backend and threw on the
managed one — and the managed backend was the one behaving correctly. LAPACK's two eigenvector
columns for a defective matrix differ by about 4.4e-16, so `det(V)` is not quite zero and the LU
squeaks through to MATLAB's answer; the managed eigensolver returns them bit-identical, which is the
truthful thing to return when there is only one eigenvector, so `det(V)` is exactly zero and there is
nothing to divide by. MATLAB's answer on a defective matrix is a rounding artefact of its own
eigensolver. An implementation cannot be built on one.

## Decision

### The operator answers the matrix function, and it is right where MATLAB is not

`s ^ A` is f(A) for f(x) = s^x, evaluated by Schur-Parlett — `MatrixFunction.Evaluate`, which has
been here since M107 under `funm`. The divergence from MATLAB on a defective exponent is deliberate
and is recorded below.

The reason this can be written at all is that f knows its own derivatives to every order:
f⁽ᵏ⁾(x) = (ln s)ᵏ·s^x. Schur-Parlett gathers eigenvalues that are close to one another into a block
and evaluates the block from its Taylor series precisely so that it never has to divide by the
difference of two nearly equal numbers — and a repeated eigenvalue is the limiting case of that.
So `2 ^ [2 1; 0 2]` is one block of order two, the series is `f(2)·I + f'(2)·N`, and the answer is
`[4 4·ln2; 0 4]` because that is what the arithmetic says, not because a special case was written
for it.

**It is not spelt `expm(log(s) * A)`, though that is its value.** Two reasons. A diagonal exponent
must stay exact — `2 ^ [1 0; 0 3]` is `[2 0; 0 8]` and not two numbers a few ulps away from them —
which needs the base raised through the same domain rule `.^` uses, on `Math.Pow` where the answer
is real. And a negative or complex base needs the whole evaluation in complex arithmetic, which
`expm` here is not: it takes a real matrix.

**The logarithm is taken once, on the principal branch**, which is the branch that makes `s ^ A`
agree with `expm(log(s)·A)` wherever both are defined, and in real arithmetic when the base is
positive so that a real problem carries no imaginary dust into the recurrence.

### Realness is a property of the base

f(x) = s^x carries the reals to the reals exactly when s is not negative. So a real exponent under a
non-negative base has a real answer, and every imaginary part left in it is the complex Schur form's
own roundoff, which is discarded: `2 ^ [0 -1; 1 0]` has a conjugate pair of eigenvalues and comes
back exactly real, and `0 ^ [1 2; 3 4]` is a real matrix of NaNs rather than a complex one.

A negative base has no such guarantee and keeps whatever the arithmetic gave it. That is not a
threshold or a tidy-up — it is what makes `(-2) ^ [2 0; 0 4]` real, because both powers are whole
and `Math.Pow` returns them exactly, while `(-2) ^ [1 2; 3 4]` is not, and
`(-2) ^ [2 1; 0 2]` is `[4 4·(ln2 + iπ); 0 4]`, complex precisely in the entry MATLAB drops.

### The triangularization moved down beside Schur-Parlett

`funm`'s `FunmSchur` — the matrix itself when it is already upper triangular, the real Schur form
through `rsf2csf` when it is real, the complex one otherwise — is now `MatrixFunction.Triangularize`
in the numerics assembly, because `^` is a second caller that needs the same triangle for the same
reason. `funm`'s own helper is what is left of it: a one-line delegation that keeps the name and the
note about why a real matrix does not take the complex road.

### Three error messages become MATLAB's own

A non-square exponent, and `^` between two matrices, are the same complaint about the same operator
and now draw the same sentence MATLAB draws, which names `.^` as the fix. `^` also refuses an
integer class unless both operands are scalar, ahead of the class-combining rule, because MATLAB
puts it ahead too — otherwise `2 ^ int8([1 2; 3 4])` reports the combination as the problem and
names the wrong fix.

An operand with more than two dimensions draws a **different** sentence, and that is measured
rather than assumed: MATLAB refuses an N-D array for having pages, not for the shape of any one of
them. `2 ^ ones(2, 2, 2)` used to answer a 2-by-2 here, having dropped the third dimension on its
way to the elementwise power — a wrong shape as well as a wrong number. Both sides of the operator
draw it, because `ones(2, 2, 2) ^ 2` is the same complaint and was answering `'^' needs a square
matrix`, which is true of the page it happened to flatten and not of the problem.

## Consequences

Eighty-two expressions were compared against R2025b's `expm(log(s) * A)` — Jordan blocks of orders
two to six, defective matrices that are not already triangular, eigenvalues 1e-10 apart, a nilpotent
part of 1e6, bases from 1e-10 to 1e10, conjugate pairs, negative and complex bases, complex
exponents, `magic`, `hilb` and `vander`. **All eighty-two agree**, seventy-six of them printing
identically to twelve significant digits; the worst disagreement anywhere in the set is 4.9e-16
relative, and it is on `(-2) ^ [2 0; 0 4]`, where JGraph's answer is the exact one and the matrix
exponential's is not. Four of the remaining six are of that kind: a negative base, where JGraph
is exactly real and `expm(log(s) * A)` carries about 1e-15 of imaginary dust through from
`log(-2)`. An earlier pass over harder shapes — an 8-by-8 `rosser`, `magic(6)` — agreed the same
way, worst 4.6e-16.

A further forty-four expressions were compared against R2025b's own `^` for the things that are not
the number — class, shape, realness, and thirteen refusals. All of them agree except the nine
recorded below.

One hundred and fifty-one tests are new in `MatlabScalarMatrixPowerM142Tests` and sixteen in
`ScalarMatrixPowerM142Tests`, which holds the kernel to the Jordan form without an interpreter above
it. Two groups assert nothing that was pasted at all: one writes the Jordan block's value out from
the derivatives it must carry, and one holds `s ^ A` against `expm(log(s) * A)` computed by JGraph's
own exponential, which reaches the answer by scaling and squaring and never asks what the
eigenvalues are. Those are the assertions that survive a change of backend, and they are the ones
that run in all four lanes.

Nothing else in the suite moved: 8,495 in the default lane against 8,328 before, which is the 167
new ones and no others changed.

### Divergences

- **`s ^ A` for a defective `A` answers the Jordan form's value where MATLAB drops it.**
  `2 ^ [2 1; 0 2]` is `[4 4·ln2; 0 4]` here and `[4 0; 0 4]` in R2025b; `2 ^ [5 -1 0; 1 3 0; 0 0 3]`
  is `[27.09 -11.09 0; 11.09 4.91 0; 0 0 8]` here and `[16 0 0; 0 16 0; 0 0 8]` there. This one is
  deliberate and is the whole of the milestone: MATLAB evaluates `V * diag(s .^ diag(D)) / V`, a
  defective matrix has fewer eigenvectors than columns, and the formula silently answers `s^λ` times
  the identity for the block it cannot separate. `expm(log(s) * A)` and `funm` — both MATLAB's own —
  agree with JGraph and not with MATLAB's `^`. A script that was written against the eigen-based
  answer on a defective matrix will see a different number here, which is why it is recorded rather
  than only fixed.
- **A `single` answer is computed in double precision and rounded.** `2 ^ single([1 2; 3 4])` is
  `[10.482739 14.151878; 21.227818 31.710558]` here and `[10.482742 14.151882; 21.227823 31.710566]`
  there, because MATLAB carries the whole evaluation in single. The class and the shape are right
  and the answer agrees to six figures; there is no single-precision Schur factorization here to
  carry it further, and the same gap is already recorded for the division in ADR 0144.
- **`0 ^ A` and `Inf ^ A` are matrices of NaNs where MATLAB has infinities in them.**
  `0 ^ [1 2; 3 4]` is all NaN here and `[NaN -Inf; NaN Inf]` in R2025b; `Inf ^ [1 2; 3 4]` is all NaN
  here and all Inf there. Neither is defensible as a limit — `s ^ A` has no value as s approaches
  zero or grows without bound, and both answers are whatever the arithmetic made of an infinity
  meeting a zero on the way through a different algorithm. The class and shape agree, and so do the
  cases where the infinity is in the exponent rather than the base: `2 ^ [Inf 0; 0 1]` is all NaN in
  both.
- **`1 ^ A` has a `+0` where MATLAB has a `-0`.** `1 ^ [1 2; 3 4]` is the identity in both, but
  MATLAB's lower-left entry is negative zero. `expm(log(1) * A)` is `expm(0)`, whose entry is `+0`,
  so this is MATLAB's sign and not the mathematics'. It matters only to `1/x` and to a script that
  prints with `%+g`.
- **`A ^ p` for a non-integer `p` is still refused**, where MATLAB answers with the principal power:
  `[1 2; 3 4] ^ 0.5` is `sqrtm` of it. This is the other half of `mpower` and M142 does not touch
  it; the refusal is honest and predates this work. The machinery to close it is now in place —
  f(x) = x^p has derivatives of every order too — but the branch rules for a negative eigenvalue are
  a milestone of their own.
