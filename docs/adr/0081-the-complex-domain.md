# 0081 — The complex domain

Date: 2026-08-24 · Milestone: M81 · Status: accepted

## Context

The capability report's divergence list carried a line harvested from ADR 0023: *`sqrt`/`log` stay
real-domain and error on complex input.* It was the first of four gaps this session set out to close.

**It had been false since M42.** That milestone gave `exp`, `log` and `sqrt` a real fast path that
bails into a complex answer — `MapComplexProducing`, whose `staysReal` predicate promotes the whole
array the moment one element leaves the real domain, which is MATLAB's own rule. `sqrt(-1)` has
answered `1i` and `log(-1)` has answered `pi*i` for seventeen milestones.

Nothing noticed, and the reason is worth stating plainly: **`JgsComplexTests.cs` contains no `sqrt`
or `log` case at all.** M42 wrote the behaviour and no test named it, so the sentence describing its
absence was never contradicted by anything. It was copied forward into ADR 0023's divergence list,
harvested from there into `docs/matlab-divergences.md`, and printed from there into a capability
report a reader would reasonably take as current. A recorded limitation with no test on either side
of it is a claim about the past that nothing in the build can refute.

This is the fourth expired block the arc has found by re-reading one: `warp` in M67 (M45 had already
built the texture-mapped surface it was waiting for), `MException` in M68 (three of the four things
the plan meant to build already worked), `Interactions` in M80 (every gesture was happening and only
the name was missing), and now this. The pattern is consistent enough to be a rule: **re-check a
recorded block before believing it, and prefer the check that runs the code.**

What *was* missing is everything else. Two shapes of gap, and one seam closes both:

- **Functions whose real domain is a proper subset of the reals answered `NaN`.** `log2(-1)`,
  `log10(-1)`, `log1p(-2)`, `asin(2)`, `acos(2)`, `acosh(0)`, `atanh(2)`, `asec(0.5)`, `acsc(0.5)`,
  `asech(2)`, `acoth(0.5)` and the degree spellings of all of them. MATLAB answers a complex number
  for every one.
- **Functions that never leave the reals refused a complex argument outright.** `sin(1+2i)` was the
  error "sin expects a number or numeric array, but got a complex" — which made complex numbers
  representable but not usable, since the moment a calculation went complex the ordinary functions
  stopped accepting its result.
- **And `(-8)^0.5` was `NaN`**, because `NumericBinary` short-circuits two numeric scalars straight
  into `Math.Pow` before the complex arm below it can be reached, and the packed kernel behind `.^`
  writes doubles.

## Decisions taken before any code

1. **Widen the seam that exists rather than write a second one.** `MapComplexProducing` already does
   the whole job; almost the entire milestone is registering more names through it. The registration
   helper moved out of a local function in `JgsBuiltins.cs` into a static one the other files can
   reach, because `Math1` is declared separately in four files and a fifth copy of `MathX` is how a
   family drifts apart.

2. **A predicate per function, named rather than inlined.** Each `staysReal` is the claim "here is
   where this function leaves the reals", and a reader should be able to check it against a table of
   principal values. `NaN` belongs to every domain, so `NaN` in gives `NaN` out without a detour
   through complex arithmetic.

3. **Nothing that already answered changes its answer.** Where a value sat exactly on the edge of a
   domain and gave `NaN`, the predicate admits it to the real path deliberately — `asec(0)` still
   answers `NaN` rather than meeting a complex division by zero. The milestone adds answers where
   there were none; it does not move one a script could already read.

4. **Write the complex definitions out rather than borrow them.** The generic-math interfaces .NET 7
   added arrive as explicit interface implementations, and more importantly the branch cut is the
   interesting part of each definition. `acosh` is `log(z + sqrt(z-1)·sqrt(z+1))` and not
   `log(z + sqrt(z²-1))`, which is what puts the cut on `(-inf, 1)`.

5. **`^` gets a binary `staysReal` and no other operator does.** A pair `(a, b)` leaves the reals when
   `a` is negative and `b` is not a whole number. Threading one nullable predicate through
   `NumericBinary`, `Broadcast` and the packed kernel is what holds the milestone's reach to the one
   operator that needed it: every other operator passes null and takes the path it always took.

6. **The packed kernel declines rather than promotes.** `PackedOps.TryArithmetic` scans for a
   promoting pair ahead of `Power` and returns false, which is exactly what the class contract is
   already for — "the interpreter then falls back to the classic boxed code, so semantics never depend
   on which path ran". No complex kernel was written.

7. **The `real*` family and `nthroot` do not move.** `realsqrt(-1)` erroring is the whole reason that
   name exists, and `nthroot(-8, 3)` answering `-2` is the whole reason that one does. `mod` and `rem`
   keep refusing complex, as MATLAB's do.

## Two branch cuts that had to be chosen rather than taken

A principal value is a convention, and two correct libraries can disagree about which side of a cut
to approach from. Two of these mattered enough to be written out:

- **`Complex.Asin(2)` answers `1.5708 + 1.3170i` where MATLAB answers `1.5708 - 1.3170i`.** Both are
  principal values of the same multivalued function; a script ported from MATLAB reads the sign. So
  `asin` is written as `-i·log(iz + sqrt(1 - z²))`, which lands on MATLAB's side, and `acos` is
  written as `π/2 - asin(z)` over it so the pair cannot drift apart from each other. `asec`, `acsc`,
  `asind`, `acosd`, `asecd`, `acscd` and `asinh` all inherit the choice by construction.

- **A negative zero flipped `atanh`.** `atanh(z)` is `½·log((1+z)/(1-z))`; for a real `z` above one
  the quotient is a negative real, and complex division writes its imaginary part as *negative* zero.
  `Atan2(-0, -3)` is `-π` where `Atan2(+0, -3)` is `+π`, which is the entire difference between
  `atanh(2)` answering MATLAB's `0.5493 + 1.5708i` and answering its conjugate. A real argument on a
  branch cut is approached from above, so a zero that arrived by arithmetic rather than from the
  caller is a positive one. `acoth` inherits the fix.

Neither was visible from reading the code. Both were found by printing values and comparing them with
MATLAB's documented answers — which is the CLI probe rule doing the thing it exists for.

## A defect found by probing rather than by counting

**`mat2str` could not write a complex number.** `mat2str(1i)` answered the bare text `[]` and
`mat2str([1i 2])` threw, because the function reached `JgsMatrix.ToRows`, which reads only reals.

The scalar case is the worse of the two: it was not an error but an *answer*, and a wrong one, from
the single function in the library whose entire contract is that its text reads back as the same
value. It has been wrong since `mat2str` landed in M52, and no count would ever have shown it —
`mat2str` was implemented, its forms were accepted, and the coverage table has no column for
"answers correctly".

Every element is now written `re+imi` once any element is complex, including the ones that happen to
be real, because `[1+0i 0+2i]` reads back as a complex array where `[1 0+2i]` would too but only by
accident of its second element. MATLAB writes it the same way and for the same reason.

This is the third wave running in which the productive half of the work came from running the forms
rather than counting the names: M79 found a box chart reporting a colour it was not drawing, M80
found the form prober passing a table where a variable name belongs, and M81 found this.

## Verification

- 0 warnings in Release and Debug; **5,192 tests** (5,178 + 14); **53 of 53 stress scripts**, including
  the new `stess_53.m`, which passed all fifteen sections on its first run because every value in it
  had been printed at the CLI first.
- The identities are the check that needs no table of principal values, and the stress script leans on
  them: `exp(log(z)) == z`, `sin(asin(2)) == 2`, `tanh(atanh(2)) == 2`, `((-8)^(1/3))^3 == -8`,
  `2^log2(-8) == -8`, `asin(2) + acos(2) == π/2`.
- The real path is pinned in both directions: `sqrt([1 4 9])` is still a plain packed `[1 2 3]`,
  `isreal` still answers true for it, and `sqrt([1 -4 9])` promotes the whole array — which is the
  behaviour that says the fast path is still being taken when it should be.

## Divergences recorded

- **`realsqrt`, `reallog` and `realpow` refuse rather than promote**, and `nthroot(-8, 3)` answers the
  real signed root `-2` rather than the principal complex one. Both are MATLAB's own behaviour and are
  recorded here so a later widening of the seam does not run over the top of them.
- **`mod` and `rem` have no complex form** and say so by name, as MATLAB's do.
- **An array power promotes element by element, not as a whole.** `[-8 4].^0.5` holds a complex first
  element and a plain real second one, where MATLAB's result is uniformly complex. Every observable
  answers correctly — `isreal` is false, `mat2str` writes `[0+1i 2+0i]`, arithmetic composes — but the
  console shows the real element without an imaginary part. The unary family promotes the whole array
  because every element takes the same function; a binary operator pairs each element with its own
  second argument, so each pair is its own question.
- **A vanished imaginary part normalises back to a plain number**, which is M21b's rule and is why
  `1i * 1i` is `-1` rather than `-1+0i`. It is also why `cos(1i)` reads as a real number.

## What is not done

- **The display of a complex number is .NET's, not MATLAB's.** `exp(1i*pi)` shows as
  `-1+1.22464679914735e-16i` where MATLAB shows `-1.0000 + 0.0000i`. The value is right and the
  formatting is the console's; a MATLAB-faithful complex column format is a formatting wave, not this
  one.
- **`svd` and `eig` still refuse their multi-output forms for a complex matrix**, and a complex pencil
  is still refused by `qz` — ADR 0076's divergences, untouched here, because they are about
  factorizations rather than about the elementwise family. (The multi-output refusals were
  closed in M91, ADR 0091; the complex pencil stands.)
- **`atan2`, `hypot` and the special functions stay real.** MATLAB refuses `atan2` of a complex too;
  `gamma` and the error functions of a complex argument are a numerics milestone, not a seam widening.
