# ADR 0144 — A rectangular division answers the basic solution

## Status

Accepted (M140).

## Context

`[1 2 3] \ [2]` asks for the x with `x(1) + 2*x(2) + 3*x(3) = 2`. There is a whole plane of them,
and picking one is a convention rather than a calculation. JGraph picked the shortest vector in that
plane and MATLAB picks another:

```matlab
>> [1 2 3] \ [2]
   0
   0
   0.6667          % JGraph answered [0.1429; 0.2857; 0.4286]
```

Neither is wrong as mathematics. The minimum-norm solution spreads the answer over every column;
the *basic* solution carries it on the `rank(A)` columns a pivoted factorization ranked first and
leaves a nought everywhere else. Only one of them is what a MATLAB script will have been written
against, and a script cannot tell which it got by asking whether the answer solves the system —
both do. That is how this went unnoticed long enough for a test to be written asserting it.

Probing the family rather than the reported line found the shortest solution was the smallest part
of it. A rectangular system with complex entries was refused outright — *"the complex least-squares
solve is not supported"*, honest for as long as it was true. A rank-deficient one came back with
whatever a near-singular triangle produced: `[1 2 3; 2 4 6] \ [1; 2]` answered
`[-0.2626; 0.2519; 0.2529]`, which does solve the system, but is neither the shortest solution nor
MATLAB's, and no two of the numbers in it mean anything. And a right-hand side written without
brackets was refused at the door: `[1 2 3] \ 2` never reached the solver at all, because the
dispatch asked whether the operand was an array rather than what shape it was.

**The rank rule is measurable, and it was measured.** MATLAB prints the tolerance it used, so the
formula can be read straight off the warning:

```matlab
>> [1 2; 2 4; 3 6] \ [1; 2; 3]
Warning: Rank deficient, rank = 1, tol =  4.984889e-15.
```

That is `max(m, n) * eps * |R(1,1)|` to the last digit — three times the spacing of one times
`sqrt(56)`, the longest column, which is what pivoting puts in the leading diagonal entry. The same
formula reproduces every tolerance R2025b printed across the probe. **For a complex system the
tolerance is ten times looser**, which is measured rather than reasoned about: the same shape warns
at `4.468561e-14` in complex where it warns at `4.468561e-15` in reals, and the factor of ten holds
across every complex shape tried.

## Decision

### One road for every rectangular shape

`Linear.BasicSolution` factors `A·P = Q·R` with column pivoting, counts the diagonal entries that
clear the tolerance, solves that leading triangle, and scatters the result back through the pivot
into a solution that is noughts everywhere the pivoting did not reach. A square system still goes
to the LU factorization it always went to.

Every rectangular shape takes this road, not only the under-determined ones. An over-determined
full-rank system has one least-squares answer and so cannot be changed by the choice — its numbers
move by a few ulps and no further — but routing it here is what gives the rank decision a single
place to live, and what lets a deficient system of any shape answer at all.

None of this is new machinery. `Geqp3`, `Ormqr` and `Trtrs` were already on the backend contract
with an implementation on each side of it, so the solve is written once, in terms of the three of
them, and both lanes reach it through their own kernels. The unpivoted `Gels` it replaces is now
called only by the tests that hold the contract to its shape.

### The rank decision is MATLAB's, and it is said out loud

A system that ran out of independent columns still has an answer, and MATLAB hands it back with a
warning rather than refusing. So does JGraph now, in MATLAB's own words — down to the width the
tolerance is printed at, because a script may well be reading it — and raised by calling the
script's own `warning`, so that `lastwarn` reports it and a script that has redefined `warning`
sees it.

### A bare number is a one-by-one on either side of a division

M138 settled that `[1]` is the same value as `1` to a matrix operator. The claim was only half
applied: `[1 2 3] \ 2` was refused for having a right-hand side that was not an array, and
`2 / [1 2 3]` fell past the matrix branch entirely and answered elementwise. Both now read a bare
number as the one-by-one it would be with brackets round it. `[1 2 3] \ 2` is one equation in three
unknowns and is solved; `2 / [1; 2]` is a system with exactly one answer and gives the 1-by-2
`[0 1]` where the elementwise reading gave a 2-by-1 of something else; and `2 / [1 2 3]` asks for an
X that does not exist and is refused, as `[2] / [1 2 3]` already was.

## Consequences

A fifty-case probe over wide, tall, square, rank-deficient, complex, empty and multi-right-hand-side
operands has two disagreements left with R2025b, both recorded below; the other forty-eight agree to
a worst relative error of 2.3e-15 on class, shape, real part and imaginary part. The shapes MATLAB
refuses are still refused. The dotted spellings are untouched.

Sixty-one tests are new in `MatlabBasicSolutionM140Tests`, and one more in the provider suite; every
script expectation was generated from R2025b rather than typed. They are asserted at twelve decimal
places on purpose: a pivoted factorization
leaves a few ulps of residue in the entries the pivoting did not zero, and the two lanes reach it
through different kernels, so exact digits would assert roundoff rather than the answer.

Routing every rectangular shape through `Ormqr` found a hole in the managed kernel that nothing had
reached before. LAPACK returns at once when a product has no rows, no columns or no reflectors, and
the managed `Ormqr` did not — it sliced the right-hand side at each reflector's offset and walked
off the end of an empty one. So `[1 2 3; 4 5 6] \ zeros(2, 0)` answered the 3-by-0 MATLAB answers
with on the native lane and threw on the managed one, and the throw arrived at the operator as an
`ArgumentException`, which it dutifully reported as a dimension mismatch. The quick return is now
where LAPACK has it, with a provider test holding both backends to it — `Gesdd`, immediately below
it in the same file, had the same guard already.

Two existing tests asserted the behaviour this milestone changes and were rewritten rather than
deleted — one had `WideSolveIsTheMinimumNormAnswer` in its name, and now asserts that the answer is
the basic one *and* that it is not the shortest, which is the assertion that would have caught this.

### Divergences

- **A singular square system is refused where MATLAB answers with infinities.** `[1 2; 2 4] \ [1; 2]`
  raises "The matrix is singular to working precision." here; R2025b warns
  `Matrix is singular to working precision` and returns `[NaN; NaN]`. This is the LU path, which
  M140 does not touch, and the divergence predates it. A near-singular square system diverges the
  other way: `A(:, [1 1 2 3 4]) \ b` for a 5-by-5 with a repeated column answers finite numbers here
  and infinities there, because the two factorizations disagree about whether an exactly dependent
  column produces an exactly zero pivot.
- **A rank-deficient system may put its answer on a different set of columns from MATLAB's, and the
  two lanes may disagree with each other about which.** Which columns a basic solution rests on is
  whatever the pivoting ranked first, and on a near-tie two correct implementations of column
  pivoting rank differently. A 5-by-7 complex matrix of rank 4 answers on columns 1, 3, 5, 6 here
  and on 3, 4, 5, 6 in R2025b. On the **managed** backend the same happens to *real* systems — a
  5-by-7 real matrix of rank 4, and a rank-deficient 3-by-8, both land elsewhere than R2025b — where
  on the native backend they agree, because there the real path is LAPACK's own `dgeqp3`. Complex
  systems diverge on both lanes, there being no `zgeqp3` on the contract to reach.

  Every one of these is a basic solution and every one achieves the same minimum residual —
  1.147079 for that complex system, in both lanes and in MATLAB — so none is more correct than
  another. But they are different numbers, and a script that solves a rank-deficient system can see
  which lane it is on, which is the part worth minding. Closing it means transcribing LAPACK's norm
  downdating, including the restart heuristic that decides when a downdated norm is no longer
  trustworthy; the full-rank case, which is almost every case, is unaffected either way.
- **The rank-deficiency warning cannot be turned off.** `warning('off', 'MATLAB:rankDeficientMatrix')`
  is the first thing a script that means to solve deficient systems in a loop will reach for, and
  JGraph's `warning` accepts `'off'` and ignores it — it keeps no such state — so the warning is
  raised on every pass where MATLAB would have been told to stop. The gap belongs to the `warning`
  builtin rather than to the division, but M140 is what makes it visible: before it, a deficient
  system had no warning to silence because it had no answer either.
- **A `single` rectangular system is solved in double precision and rounded.**
  `single([1 2 3; 4 5 6]) \ single([1; 2])` answers `[0; 0; 0.33333334]` here and
  `[8.2667e-08; 0; 0.33333325]` there, because MATLAB carries the whole factorization in single.
  The class and the shape are right and the answer agrees to six figures; there is no
  single-precision `dgeqp3` behind this to carry it further.
