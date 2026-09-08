# ADR 0145 — A scalar raised to a matrix is an eigendecomposition

## Status

Accepted (M139).

## Context

ADR 0143 closed a family of shape bugs under `*`, `/`, `\` and `^`, and left one divergence standing
at the top of its own list: `s ^ A`, a scalar raised to a matrix power, answered elementwise.

```matlab
>> 2 ^ [1 2; 3 4]
       2       4
       8      16     % here
```

```matlab
>> 2 ^ [1 2; 3 4]
   10.4827   14.1519
   21.2278   31.7106     % R2025b
```

A wrong number, silently, with no error to notice and nothing in the shape of the answer to give it
away — it is a 2-by-2 either way. M138 had made the two spellings `2 ^ A` and `[2] ^ A` agree, which
meant they now agreed on being wrong.

MATLAB's rule is short enough to write out, and it was measured rather than recalled:

```matlab
[V, D] = eig(A);
f = V * diag(s .^ diag(D)) / V;
```

`norm(s^A - f, 'fro')` is exactly `0.000e+00` for `[1 2; 3 4]`, `[2 1; 0 2]`, `[0 -1; 1 0]`,
`[1 1; 1 1]`, `[4 1; 2 3]`, `eye(3)` and `[1 2; 3 4.0000001]`. **It is emphatically not
`expm(log(s) * A)`**, which is the closed form one reaches for first and which disagrees by nine on
the defective `[2 1; 0 2]`.

Two things about that formula are easy to get wrong, and both were settled by measurement.

**Realness is not a rule applied at the end.** `2 ^ [0 -1; 1 0]` comes back exactly real in MATLAB
even though its eigenvalues are a conjugate pair, which invites the theory that MATLAB drops an
imaginary part that is small enough. It does not:

```matlab
>> isreal(2 ^ [1 -2; 3 4])
   0                              % imaginary parts of about 1.8e-15
>> isreal(2 ^ [0 -1; 1 0])
   1                              % imaginary parts of exactly zero
```

Both matrices have a conjugate pair of eigenvalues; the second one's imaginary parts happen to
cancel to the bit and the first one's do not. MATLAB is doing the complex arithmetic and narrowing
the result when — and only when — every imaginary part is exactly zero. Anything else would have to
choose a threshold, and MATLAB has not chosen one.

**A real problem has to stay in real arithmetic**, and not because it is faster. Complex
multiplication computes `a·d + b·c` for the imaginary part, so a real `0 · Inf` that would give a
plain `NaN` in the real product gives a `NaN` *imaginary* part as well, and the answer comes back
complex where MATLAB's is real:

```matlab
>> 0 ^ [1 2; 3 4]
   NaN  -Inf
   NaN   Inf      % isreal 1
```

The eigenvalues here are `-0.372` and `5.372`, so the diagonal is `Inf` and `0`, and the zeros off
the diagonal of `diag(p)` are multiplied by that `Inf` in the product. The `NaN`s in the answer are
that multiplication, which is also why `V * diag(p)` has to be written as the full product it is in
MATLAB rather than as the column scaling it mathematically is.

## Decision

### The formula is transcribed, not reimplemented

`MatrixFunction.ScalarPower` is the whole algorithm: factor, raise the diagonal, multiply, divide.
It sits beside Schur–Parlett in `MatrixFunction.cs` because it is a function of a matrix, and
deliberately does not use it — MATLAB does not reach for Schur–Parlett here, and the point of this
milestone is to answer what MATLAB answers, roundoff included. There are two overloads, over a real
matrix and over a complex one, because a real matrix has a real eigensolver and MATLAB uses it.

The division is `X / V`, and it is written the way the `/` operator itself is written here: the
transposed system `(Vᵀ \ Xᵀ)ᵀ`, with the plain transpose and not the conjugate one.

### The domain rule for the diagonal is the one `.^` already has

`s .^ diag(D)` is spelt by a delegate the scripting layer supplies, so that a negative base with a
whole exponent stays real exactly as it does elsewhere: `(-2) ^ [2 0; 0 4]` is `[4 0; 0 16]` and
real, `(-2) ^ [1 2; 3 4]` is complex. The delegate stays on `Math.Pow` where the answer is real,
which is what makes `2 ^ [1 0; 0 3]` exactly `[2 0; 0 8]` — `Complex.Pow` would route the same
question through `exp(λ·log(s))` and land a few ulps away.

### The real branch is a correctness branch

When the base is real, every eigenvalue is real and every raised eigenvalue is real, the product and
the solve are done in real arithmetic. That is the case MATLAB carries out in real arithmetic too,
and it is what gives `0 ^ [1 2; 3 4]` and `2 ^ [Inf 0; 0 1]` the real answers they have there.
Everything else goes through the complex path, and comes back through the same narrowing every other
complex verb here uses: an element with an exactly-zero imaginary part is a plain number, so an
array of them is `isreal`.

### Two error messages become MATLAB's own

The new branch refuses a non-square exponent in MATLAB's words, and so — now — does `^` between two
matrices, which is the same complaint about the same operator and used to have a sentence of its own.
`^` also refuses an integer class outright unless both operands are scalar, which is MATLAB's rule
and covers `A ^ p` as much as `s ^ A`; it is checked in `ApplyBinary` ahead of the class-combining
rule, because MATLAB puts it ahead too and the combining rule would otherwise name the wrong fix.

## Consequences

A silently wrong number is gone. Eighty-four cases were run through R2025b and through JGraph and
compared on class, shape, `isreal`, real part and imaginary part; **the only two disagreements were
error wordings, and both were then made to match.** Thirty-seven of the seventy numeric cases agree
bit for bit. The worst disagreement among the rest is **6.9e-15 relative**, on `2 ^ magic(4)` — a
singular matrix whose eigenvector matrix is the worst conditioned in the set. Every other
double-precision case is inside 2.6e-15, and most are inside one ulp.

The defective matrices behave better than they have any right to. `2 ^ [2 1; 0 2]`, `2 ^ [1 1; 0 1]`,
`2 ^ [3 1 0; 0 3 1; 0 0 3]` and `2 ^ [1 1 0; 0 1 1; 0 0 2]` all agree with MATLAB *exactly*, because
both eigensolvers return the same duplicated eigenvector column and both then divide by the same
near-singular matrix. That agreement is a property of the two LAPACK builds and not of the formula,
and it should not be relied on: a defective matrix with distinct eigenvalues would have no reason to
agree so well, and `2 ^ magic(4)`, the nearest thing in the set to that, is where the worst error is.
The tolerance that can honestly be held is a relative 1e-14, which is what the tests assert.

Eighty-five tests are new, in `MatlabScalarMatrixPowerM139Tests`. Every expectation was printed by
R2025b and pasted, and a generator script parses the `InlineData` rows back out of the test file,
runs them through `matlab -batch` and diffs: **zero differences over all eighty-five.**

`A ^ p`, `s ^ s` and the dotted spellings are untouched. The new branch is reached only when the left
operand is 1-by-1 and the right is an array of some other size, which is a shape none of them has.

### Divergences

- **`A ^ p` for a non-integer `p` is still refused**, where MATLAB gives the principal power:
  `[1 2; 3 4] ^ 0.5` is `sqrtm` of it. That is the matrix-base half of `mpower` and a different
  problem from this one; the refusal is honest and still stands.
- **`[1 2 3] ^ 2` answers elementwise**, where MATLAB refuses a non-square base. A non-square
  *exponent* is now refused in MATLAB's words, but a non-square *base* never reached the matrix
  branch at all — it is a vector, not a matrix, so it falls through to `.^` and answers `[1 4 9]`.
  Fixing it belongs with `A ^ p`.
- **`2 ^ 'ab'` is refused in JGraph's words, not MATLAB's.** Both refuse — a 1-by-2 is not a square
  exponent — but a char row is a string here rather than an array, so it never reaches the new
  branch and draws the elementwise operator's own sentence instead. A char *matrix* does reach it:
  `2 ^ ['ab'; 'cd']` is the same four numbers in both.
- **A `single` answer agrees only to about six figures.** `single(2) ^ [1 2; 3 4]` is
  `10.482742` in R2025b and `10.482739` here, a relative 2.4e-7 — three single ulps. MATLAB carries
  the whole eigendecomposition in single precision; JGraph has no single-precision eigensolver and
  rounds a double answer at the end. Closing this means a single-precision `dgeev`, which is a much
  larger piece of work than the class tag it would fix.
- **The sign of a zero can differ.** `(-2) ^ [1 0; 0 2]` is `[-2 -0; 0 4]` there and `[-2 0; 0 4]`
  here, and the zero block of a block-diagonal exponent picks up minus signs here that it does not
  there. The values are equal and `isequal` says so; only `1/x` could tell them apart.
- **Roundoff dust against an exact zero differs in the last bits.** Where MATLAB's answer has an
  imaginary part of `1.11e-16` against a real part of order one, JGraph's may have `1.35e-16` or
  zero. The two agree to a relative 1e-14 on the matrix as a whole, which is all a conjugate pair
  that nearly cancels can promise.
- **`2 ^ sparse(A)` is refused**, where MATLAB answers with the same full matrix the dense exponent
  gives. A sparse operand takes the sparse kernels before the matrix branch is reached and is told
  to use `full()` first. The refusal predates this work and is honest; routing sparse here would
  mean deciding what a sparse `eig` is, which is a milestone of its own.
