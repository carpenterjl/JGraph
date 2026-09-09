# ADR 0147 — Counting the elements that are not zero, when some of them are complex

## Status

Accepted (M143).

## Context

`nnz` refused a complex argument:

```
jgraph -batch "disp(nnz([1+1i 0 2]))"
nnz expects a number or numeric array, but got a complex.
```

R2025b answers `2`. The count is not a hard question — MATLAB asks a complex number exactly the
question it asks a real one, and answers it a plane at a time, so an element counts when
`real ~= 0 || imag ~= 0` — and every part of the machinery to ask it was already here. What was
missing was one enumerator.

`nnz` and `nonzeros` are the only two callers of a private `Flatten` in `JgsBuiltins.Numeric.cs` that
yields `double`, and a `double` has nowhere to put an imaginary part. So the walk threw on the first
complex element it reached, which is where the message came from — and it is worth naming which
element that was, because the two storages a complex array can arrive in failed differently:

* A **boxed** array — `[1+1i 0 2]`, a literal — reached `Flatten`'s recursion, met a
  `JgsType.Complex` element, and drew the sentence above. Wrong, but honest.
* A **packed** array — `fft([1 2 3 4])`, a pair of planes — did not. `Flatten` asked for
  `value.AsArray`, a packed complex payload refuses to be read as boxed `JgsValue`s, and the script
  got an internal invariant back instead of a complaint about its argument:

  ```
  nnz: A packed array was accessed as boxed elements - this call site must use
  BoxedElements/ElementAt/ArrayLength.
  ```

  That one is a bug report about JGraph handed to a person who wrote `nnz(fft(x))`.

**The neighbours were measured before anything was changed, and three of the four were already
right.** `find`, `any` and `all` all reach `IsTruthy`, which has known about both planes since packed
complex storage arrived: an element is falsy only when both of its planes are exactly zero. Every one
of these agrees with R2025b today and did before this milestone:

```matlab
>> find([1+1i 0 2])         % 1 3
>> find(fft([1 2 3 4]))     % 1 2 3 4
>> any([1i 0])              % 1        — and any([complex(0,0) 0]) is 0
>> all([1i 1])              % 1        — and all([complex(0,0) 1]) is 0
>> [r, c, v] = find([1+1i 0; 0 2])     % v is [1+1i; 2], complex and in column-major order
```

`nonzeros` is the fourth, and it shares `Flatten` with `nnz`, so it had the same hole in both
storages: `nonzeros([1+1i 0 2])` is `[1+1i; 2]` in R2025b and was a refusal here.

**What "zero" means was measured too, rather than assumed from the formula.** The rule is a
comparison against zero and not a test for finiteness or for a small magnitude:

```matlab
>> nnz(-0)                  % 0    — negative zero is zero
>> nnz(complex(0, -0))      % 0
>> nnz(NaN)                 % 1    — NaN ~= 0, so it counts
>> nnz(complex(0, NaN))     % 1    — either plane is enough
>> nnz([1+0i 0])            % 1
```

`real ~= 0 || imag ~= 0` gives all five without a special case, because `NaN != 0` is true and
`-0.0 != 0` is false in IEEE arithmetic as much as in MATLAB.

## Decision

### One enumerator, yielding complex

`Flatten` becomes `FlattenComplex` and yields `Complex`. A real element arrives with a zero imaginary
part, so neither caller has to know which storage its argument came out of — which is the whole of
the fix, and is why the packed-complex case stops producing an internal message: the walk now has a
branch for that payload and reads the two planes directly, rather than asking a packed value for
boxed elements it cannot hand over.

Reading the planes rather than boxing them is not only about the error. `nnz(fft(x))` on a long
signal would otherwise allocate a `JgsValue` per sample to throw all of them away one comparison
later, and `nnz` is a function scripts call inside loops. The real fast path above it is untouched:
a real packed mask, which is what `nnz` is nearly always handed, still goes to
`PackedMath.CountNonZero` without entering the enumerator at all (M92).

### `IsCountedNonZero` is the rule, written once

Both builtins ask `value.Real != 0 || value.Imaginary != 0` through one predicate rather than
spelling the comparison twice, so the NaN and negative-zero behaviour above cannot drift apart
between counting the elements and collecting them.

### `nonzeros` narrows back to real, because R2025b does

The kept elements are gathered as `Complex` and handed to `ShapedComplex` only when one of them
actually has an imaginary part; otherwise they go out through `Numbers`, the same packed, unshaped
road real data always took. That is not a storage optimisation dressed up as a rule — it is
measured:

```matlab
>> isreal(nonzeros(complex([1 0 2])))   % 1
```

`complex()` marks an array complex with a zero imaginary plane, and it stays complex through most of
MATLAB; `nonzeros` is one of the places it does not. JGraph now answers `1` there too.

## Consequences

Forty-six assertions were compared against R2025b — the two builtins across boxed literals, packed
transform output, matrices in column-major order, the `complex()` constructor, both signed zeros,
NaN and Inf in either plane, and the three neighbours that were already right. Forty-four of them
agree; the two that do not are the orientation recorded below, which was already there before this
milestone.

Eight tests are new in `MatlabNonZeroCountsM143Tests`, and the assertions in them were run as a
script in R2025b as well as here: every block passes in MATLAB except the one that pins the
divergence, which is what makes that block a record of a difference rather than a typo.

`find`, `any` and `all` are pinned by that file too even though none of them changed. They pass
today by way of `IsTruthy`, one shared rule, and a future change to what a complex value is worth in
a boolean context would move all four builtins at once; the tests are there so it cannot move them
quietly.

Nothing else in the suite moved: 8,503 in every one of the four lanes against 8,495 before, which
is the eight new ones and no others changed.

### Divergences

- **`nonzeros` answers a row where MATLAB answers a column.** `size(nonzeros([1 0 2]))` is `[1 2]`
  here and `[2 1]` in R2025b, and the complex form inherits it: `nonzeros([1+1i 0 2])` is a 1-by-2
  here and a 2-by-1 there. The values and their column-major order are MATLAB's in both. This one
  predates M143 — the old real-only `nonzeros` returned an unshaped row, and the argument surface of
  ADR 0052 never asked about the shape — and closing it is a change to what real scripts already get
  back rather than part of widening the argument. It bites a script that concatenates the result
  (`[nonzeros(a); nonzeros(b)]` builds the wrong thing here) or that passes it somewhere shape
  matters, and it is invisible to `numel`, `sum`, `isequal` against another row, and indexing.
- **`nzmax` of a full array answers its nonzero count, where MATLAB answers `numel`.**
  `nzmax([1 0 2])` is `2` here and `3` in R2025b; `nzmax([1 0 2; 0 0 3])` is `3` here and `6` there.
  MATLAB's `nzmax` reports allocated storage, and a full matrix has a location allocated for every
  element whether or not a zero is sitting in it — so on full input it is `numel` and never a count
  at all. The sparse form is right here, and it is what the name is for. `nzmax` also still refuses a
  complex full array, through a different and widely shared enumerator — `FlattenColumnMajor`, in
  `JgsBuiltins.MatrixShape.cs`, which two hundred call sites share — rather than the one this milestone
  widened; fixing the answer would make the complex question moot there, since `numel` never looks at
  a value.
- **`nnz` refuses a char array, where MATLAB counts its codes.** `nnz('hi')` is `2` in R2025b and a
  type error here. That is the general char-as-numeric question rather than anything about counting,
  and it is not decided by this milestone.
