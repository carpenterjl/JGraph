# 0103 — A named matrix is a rule about two indices

**Milestone M102.** Status: accepted.

## Context

MATLAB's `elmat` folder is the one that names matrices. Eighteen of its thirty-three names were
here already — `eye`, `magic`, `hilb`, `permute`, `flip` and the rest — and the fifteen that were
not are the ones that name a *particular* matrix: Toeplitz and Hankel, Vandermonde and companion,
Pascal, Hadamard, Rosser, Wilkinson, the inverse Hilbert, and the catalogue `gallery` reaches. With
them come four shape verbs that had no home either: `repelem`, `shiftdim`, `ipermute` and the older
spelling `flipdim`.

None of these is hard arithmetic. What makes the folder worth one milestone rather than fifteen
small ones is that every matrix in it is the same shape of thing: **an entry decided by its own two
indices and a handful of parameters**. Written that way there is nothing to assemble, nothing to
transpose, and no intermediate — a Toeplitz matrix is a rule about `i − j` and a Hankel matrix a
rule about `i + j`, and both are one pass over the storage the engine already reads.

The shape verbs turn out to be one thing too. `ipermute` is `permute` with the order read the other
way round, `shiftdim` is `permute` by a rotation, and `flipdim` is a reversal along one direction.
Only `permute` was implemented, and its body sat inside a lambda where nothing else could reach it.

## Decision

**Two engine files and one dispatcher.** `src/JGraph.Numerics/TestMatrices.cs` holds the ten
classical builders and `src/JGraph.Numerics/GalleryMatrices.cs` the Higham families; both write
`double[]` in column-major order, which is the storage the scripting layer adopts without a copy.
`src/JGraph.Scripting/Jgs/JgsBuiltins.MatrixBuilders.cs` parses arguments, and
`JgsBuiltins.MatrixBuilders.Gallery.cs` dispatches `gallery` by name.

**`permute`'s body became `PermuteDimensions` and all four verbs share it.** The extraction was
forced rather than chosen: `ipermute` and `shiftdim` are that operation, and a second copy would be
a second thing to get wrong.

**`flip` was repointed at the same reversal `flipdim` uses, and this fixed it.** `flip(A, 3)` on a
three-dimensional array had been answering with the array's *columns* since the verb was written,
because the only reversal available took a matrix and read `dim` as one of two. `flipdim` is
documented as identical to `flip`; shipping the two with different answers would have been a defect
this milestone created, so both now end in one `FlipAlong` that reverses along any direction at any
rank. No test had covered it.

**Forty-two `gallery` families are answered and sixteen are refused by name.** The line is drawn at
what the arguments decide. A family whose entries come out of a random stream — `rando`,
`randsvd`, `qmult`, `randcorr`, `randcolu`, `randhess`, `randjorth`, `cycol`, `wathen`, and the
three `*data` families — cannot be reproduced, because a matrix drawn from a stream other than
MATLAB's is a different matrix under the same name. Five more answer with a sparse matrix, which
this builder does not construct. `condex`'s fourth kind is refused for a reason of its own, below.
Each refusal names which of the three applies to it. This is the rule M101 set for N-dimensional
`makima` and M100 set before that: refuse rather than substitute.

**Where a family's default would need the random stream but explicit arguments would not, the
explicit form works.** `gallery('toeppd', n, m, w, theta)` builds the matrix; `gallery('toeppd', n)`
refuses and says why. `gallery('krylov', A, x)` likewise.

**The trigonometric families take their angles in degrees.** This is not decoration. MATLAB's
`chebspec` produces the exact rationals — 19/6, −4, 4/3 — which is only possible if its Chebyshev
grid is exactly `[1, ½, −½, −1]`, and `cos(π/3)` is not ½ in binary floating point. Reducing in
degrees with a two-part `π/180` and carrying the product's own rounding error makes the quarter
turns exact and cuts the disagreement with the correctly rounded cosine from **55 % of grid points
to 17 %**, measured over every `k·180/N` for N from 2 to 30. `orthog`, `smoke`, `prolate` and
`toeppd` all went the same way, and `gallery('smoke', 4)` now has an exact `i` on its diagonal
rather than `6.1 × 10⁻¹⁷ + i`.

**A family ignores arguments past the ones it reads, which is what MATLAB does.** Not a divergence
and not an oversight: `gallery('chow', 4, 4, 4, 4)` answers in both engines, and that was checked
rather than assumed before the leniency was left in.

**Errors carry the identifier MathWorks documents.** Ten do — `MATLAB:compan:NeedVectorInput`,
`MATLAB:hadamard:InvalidInput`, `MATLAB:pascal:InvalidArg2`, `MATLAB:invhilb:notSupportedClass`,
`MATLAB:repelem:twoInputNonVector`, `MATLAB:gallery:invalidN`, `MATLAB:gallery:invalidMatName`,
`MATLAB:hanowa:OddN` and the two diagonal-conflict warnings — and all ten match. This is ADR 0100's
amendment applied unchanged; the refusals JGraph invents still carry none.

### Divergences recorded here

- **`toeplitz([])` answers a 0-by-0 empty where MATLAB raises `MATLAB:badsubscript`.** MATLAB's own
  `toeplitz.m` reads the first element of its argument before checking there is one, so the empty case
  dies inside the implementation rather than being refused by it. `hankel([])` answers 0-by-0 in both.
  Reproducing an upstream indexing accident is not parity worth having, so JGraph answers the shape
  the construction implies.
- **`wilkinson(n, 'uint8')` builds the matrix where MATLAB raises
  `MATLAB:sizeDimensionsMustMatch`.** MATLAB forms the diagonal by counting from `−m` to `m` in the
  requested class, and an unsigned count from a negative start begins at zero instead, so the
  diagonal comes out shorter than the matrix and the assembly fails. Every entry of
  `wilkinson(5, 'uint8')` — `[2 1 0 1 2]` down the diagonal — is representable in `uint8`, so JGraph
  answers it. The same applies to `uint16` and `uint32`.
- **Sixteen `gallery` families are refused by name, and one of them is a default.** The twelve drawn
  families and the five sparse ones are listed above. The sixteenth is `gallery('condex', n, 4)`,
  which is built from an orthonormal basis of a three-dimensional span whose third direction this
  milestone did not identify; the projector is recoverable to fifteen digits from the answer, but a
  different basis is a different counter-example, and kind 4 is `condex`'s **default**, so
  `gallery('condex', n)` refuses too. Kinds 1, 2 and 3 are answered. What was established, for
  whoever takes it up: the matrix is `I + θ(I − QQ')` where `QQ'` is a rank-three projector whose
  range contains `e₁` and the vector of ones, and whose third direction for n = 7 is
  `[0, −a, a, −b, b, −c, c]` with `a : b : c = 15 : 19 : 23`.
- **`gallery('kms', n, ρ)` with a purely imaginary ρ differs from MATLAB in the sign of two zeros.**
  `ρ³` comes out with a negative zero for its real part here and a positive one there; the two
  compare equal under `==` and `isequal` and differ only in `disp`. Two entries of sixteen at n = 4.
## What this did not close

`elmat` is complete at 33 of 33 names, so nothing remains in the folder. Outside it, the prober
change below made one real gap visible for the first time: `gradient(F, hx, hy, …, hN)`, the form
that takes one spacing per dimension, which JGraph's `gradient` refuses past three arguments. That
is not `elmat`'s and is left for its own milestone.

## Consequences

`probe-forms.py` learned what a repetition ellipsis is. `blkdiag(A1,...,AN)`,
`repelem(A,r1,...,rN)`, `gallery(matrixname,P1,P2,...,Pn)` and `gradient(F,hx,hy,...,hN)` were all
`unprobed` — no sample could be built — because the token `...` had no reading. It has two: between
two placeholders it means "any number of these", and the pair around it already stands for two, so
dropping it samples the form at the smallest count that exercises it. At either end of the list —
`bar3(...,style)`, `coneplot(axes_handle,...)` — it is the older spelling of `___`, and what belongs
there is not recoverable from the form alone.

The first reading was implemented and the second was left alone deliberately. An earlier attempt
dropped every ellipsis, which unlocked 56 forms in the builtin population and scored 33 of them as
failures the engine never had — the prober inventing a call and then marking the build down for
refusing it. Restricting the rule to the repetition case leaves `matlab-form-coverage.md` at exactly
the numbers M101 measured, which is the check that it changed nothing it should not have.

## Measured

Parity against MATLAB R2024a on this machine: one script of 184 cases through both engines and
diffed. Note that `jgraph.exe -batch "run('x.m')"` runs the **JGS** dialect; passing the filename
itself as the statement selects MATLAB. MATLAB's own `-batch` wants the name without the extension.

| What | Lines compared | Lines differing | What differs |
|---|---:|---:|---|
| 15 names, every documented form, plus 42 gallery families | 203 | 15 | see below |
| of those, deliberate refusals | — | 3 | `rando`, `poisson`, `condex(n,4)` — the divergences above |
| of those, numeric | — | 12 | last-bits rounding only; worst 8.3 × 10⁻¹⁶ of the row's scale |
| refusal identifiers | 10 | 0 | every documented identifier matches exactly |

The twelve numeric differences are `chebspec` at one grid angle, six `orthog` kinds, `prolate` with
a non-default bandwidth, `toeppd`, `house`'s reflector, `gallery('kms')`'s signed zero, and two
`ipjfact` determinants. Nine of the twelve are under 1.2 × 10⁻¹⁶ — one unit in the last place.

Coverage, each number re-derived by its verifier rather than edited:

| Document | Before | After |
|---|---|---|
| `matlab-toolbox-coverage.md`, names | 195 of 377 | **210 of 377** |
| `matlab-toolbox-coverage.md`, forms | 260 of 1,036 | **290 of 1,036** |
| `matlab-builtin-coverage.md`, all kinds | 956 of 2,024 | **971 of 2,024** |
| `elmat` folder, names | 18 of 33 | **33 of 33** |
| `elmat` folder, forms accepted | 29 of 68 | **59 of 68** |

All thirty of M102's documented forms are accepted, with nothing left unprobed and nothing in
error. `matlab-form-coverage.md` did not move and is not expected to: none of these fifteen names
is in the population it measures.

## Testing

`tests/JGraph.Tests/Scripting/MatlabMatrixBuilderM102Tests.cs`, 38 tests, assertions inside the
scripts so what is pinned is MATLAB's answer and not JGraph's display.

Where a matrix has a defining property the property is asserted and not only its entries, because a
table of numbers can be copied wrongly and still look right. `H'H = nI` at all eight Hadamard
orders reached; `L·L' = pascal(n)` and `pascal(n,2)³ = I` at both parities of n; `A² = 2ⁿ⁻¹I` for
the binomial matrix; `A² = I` for the involutory one; Clement's and the sampling matrix's
eigenvalues are the integers they advertise; and `chebspec` annihilates the vector of ones.

That discipline caught the milestone's own worst bug. `pascal(n,1)` was built by the addition rule
with the subtraction the wrong way round — `L(r−1,c−1) − L(r−1,c)` where it is
`L(r−1,c) − L(r−1,c−1)` — which produces a lower-triangular matrix of plausible small integers that
is not a factor of anything. It was the parity diff that found it, and `L·L' = pascal(n)` that now
keeps it found.

Full suite 6,105 tests, 0 build warnings, five coverage verifiers exit 0.

## Live checks for the user

```matlab
toeplitz([1 2 3], [1 4 5 6])       % 3-by-4, constant down each diagonal
hankel([1 2 3], [3 4 5 6])         % 3-by-4, constant along each anti-diagonal
blkdiag([1 2; 3 4], 5)             % the blocks corner to corner
compan([1 0 -7 6]), eig(ans)       % -3, 1, 2 — the polynomial's roots
hadamard(12)' * hadamard(12)       % 12*eye(12), from a bordered circulant
pascal(5,1) * pascal(5,1)'         % pascal(5) — it is the Cholesky factor
pascal(5,2)^3                      % the identity, to fifteen digits
rosser, eig(ans)                   % a double eigenvalue at 1000 and two more above it
invhilb(6) * hilb(6)               % the identity, in exact integers
gallery('smoke', 4)                % i on the diagonal exactly, not 6.1e-17 + i
gallery('chebspec', 4)             % 19/6 exactly in the corner
[v, beta, s] = gallery('house', [3;1;2])   % a reflector onto the first axis
gallery('rando', 4)                % refused, and the message says the stream is why
repelem([1 2; 3 4], [1 2], 2)      % each row repeated its own number of times
shiftdim(reshape(1:6,1,2,3))       % 2-by-3, and the count of what was stripped
flip(reshape(1:8,2,2,2), 3)        % the planes swapped — this used to flip the columns
```
