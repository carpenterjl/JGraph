# ADR 0143 — A 1-by-1 array is the scalar it holds

## Status

Accepted (M138).

## Context

A bug report arrived as four lines, and the comment in the middle of it is the whole argument:

```matlab
a = zeros(2, 3);
b = 1;
c = [1];        % this should be exactly same as 'b'

a * b;          % ok
a * c;          % fail
```

`size(c)` answered `1 1` and `class(c)` answered `double`, so by every question a script could ask,
`c` and `b` were the same value. Only the operator could tell them apart:

```
Matrix dimensions do not agree for '*': the left has 3 columns and the right has 1 rows.
```

The dispatch in `ApplyBinaryCore` sent `*`, `/`, `\` and `^` to the matrix forms whenever **both
operands were arrays**, and asked nothing about their shapes. A bare `1` is a `JgsType.Number` and
never took that branch, so it scaled; a `[1]` is a one-element `JgsType.Array` and always took it, so
it was read as a matrix with one row. Two spellings of one value, and only one of them worked.

Probing the family rather than the reported case found the same hole under all four operators — and
under both directions of three of them. Twelve of the twenty-eight forms measured were wrong.

**Which side has to be the 1-by-1 is not the same question for each operator, and it was measured
rather than reasoned about.** MATLAB's rules do not follow the symmetry the spellings suggest:

```matlab
>> A / [2]      % 2-by-2 — an elementwise division
>> [2] / A      % error — a right division is still a system to solve
>> [2] \ A      % 2-by-2 — the same elementwise division, read backwards
>> A \ [2]      % error — a left division is still a system to solve
>> A ^ [2]      % 2-by-2 — the matrix square
>> [2] ^ A      % 2-by-2 — and none of the above
```

So `*` is scalar-formed if *either* side is 1-by-1, `/` only if the right is, `\` only if the left
is, and `^` only if the left is. Four operators, four answers.

**A 1-by-1 array is not always a number.** It can be `single`, `logical`, complex, or a slice cut out
of any of those — `A(1, 1) * A` is how most of them arise in a real script — and the elementwise path
below the dispatch already carries all of that correctly. Nothing needed to be unwrapped or
converted; the operands only needed to stop being sent down the matrix branch.

## Decision

### The question is asked once, and answered per operator

`IsScalarOperand` says whether an operand is MATLAB's scalar for these operators: a bare number or
bool, or the 1-by-1 array holding one. Text, cells, structs and class instances are not — each has
its own meaning for these operators, settled higher up in the same method.

A local `ScalarForm` maps the operator to which side it wants, and the four branches consult it. The
operands themselves are passed on untouched, so a `single` 1-by-1 still answers `single` and a
complex one still answers complex: the elementwise machinery below has carried classes since M123
and complex since long before, and this change only lets values reach it.

### The exponent is read before the general matrix branch

`A ^ [2]` needs the matrix power, not the elementwise one, so it cannot simply fall through. The
branch that already read a bare number as an exponent now reads a 1-by-1 array the same way and is
tried *before* the general "two arrays" branch, which is what used to refuse it. A complex 1-by-1 is
excluded there deliberately: `MatrixPower` takes a `double`, and reading one off a complex value
would silently drop the imaginary part.

### The backslash's swap is a scalar's swap

`s \ x` was already rewritten as `x / s` for a bare number. A 1-by-1 array now takes the same swap —
and the two scalar flags swap with the operands, so the `/` that results asks about the right side
and finds the same value it started with.

## Consequences

Twelve operator forms that were refused now answer, and every one of them agrees with R2025b on
class, shape, real part, imaginary part and `isreal`: a forty-eight-case probe over `single`,
`logical`, complex, integer, N-D and empty operands has no disagreement left in it. The shapes MATLAB
refuses are still refused, including the three that look like the ones now allowed — `[2] / A`,
`A \ [2]` and `zeros(2, 3) ^ [2]`.

Sixty-four tests are new, in `MatlabScalarArrayOperandsM138Tests`, each expectation re-derived from
R2025b rather than written by hand.

The dotted spellings are untouched. They were remapped to their undotted tokens before these branches
and are still remapped first, so `.*`, `./`, `.\` and `.^` reach the elementwise path exactly as they
did.

### Divergences

- **`A ^ p` for a non-integer `p` is refused**, where MATLAB answers with the principal power:
  `[1 2; 3 4] ^ 0.5` is `sqrtm` of it. The refusal is honest and predates this work.
- **`r.'` does not parse**, where MATLAB reads it as the non-conjugate transpose: after a name, a dot
  begins a field access here and the apostrophe then opens a string literal. `(r).'` and
  `r(:).'` parse, which is why this had not been noticed.

Closed since: **an underdetermined `\` answered the minimum-norm solution** where MATLAB answers the
basic one — `[1 2 3] \ [2]` was `[0.143; 0.286; 0.429]` here and `[0; 0; 0.667]` there. ADR 0144
settled it, and the bullet is struck from the list above rather than left standing, because
`docs/matlab-divergences.md` is generated from these sections and says of each entry that it *is* a
difference from MATLAB.

And **`s ^ A` — a scalar raised to a matrix power — answered elementwise** where MATLAB answers
with the eigendecomposition: `2 ^ [1 2; 3 4]` was `[2 4; 8 16]` here and
`[10.48 14.15; 21.23 31.71]` there. ADR 0145 settled that one, and it is struck from the list above
for the same reason.
