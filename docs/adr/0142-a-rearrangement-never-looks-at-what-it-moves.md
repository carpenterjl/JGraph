# ADR 0142 — A rearrangement never looks at what it moves

## Status

Accepted (M137).

## Context

A probe script written while measuring the short-time transform family did an ordinary thing to an
ordinary value — `circshift` on a column of an STFT, then `flipud` on the result — and stopped:

```
flipud needs numbers, but element (0, 0) was a complex.
circshift needs numbers, but element (0, 0) was a complex.
```

`flipud` does not need numbers. It needs to know where things are. Reversing the order of a column
is the same operation whether the column holds doubles, complex numbers, characters or cells, and
MATLAB accepts every one of them for the same reason: a rearrangement never looks at what it moves.

The refusal came from the doubles-only reader every one of these verbs shared, and probing the
family rather than the two reported names found it in nine of them: `flipud`, `circshift`, `rot90`,
`transpose`, `reshape`, `permute`, `ipermute`, `squeeze` and `shiftdim`. Three others — `flip`,
`fliplr` and `ctranspose` — had grown a complex path of their own at some point, each written into
its own `Define` block, each unaware of the others. `flipud` and `fliplr` are the same operation
along different directions, and one of them worked.

**This is the third time this family has been taught what it is holding.** M105 taught it the char
matrix's tag, M122 taught it char rows, string arrays and cells, and both left a mechanism behind:
the verbs that only permute positions are run on the *positions* — 1 to N in the argument's shape —
and the answer is read as where each element went. One gather then puts the real elements there.
The verb never learns what a cell is. So the third container needed no new machinery at all; it
needed a name added to a list. That is the whole of the fix, and it is the reason to write it down.

**`ctranspose` is the one name that does more than move things.** For text, an apostrophe is a plain
transpose, which is why M122 put it on the positions list. For numbers it conjugates, and a
permutation of positions cannot conjugate anything.

**MATLAB draws one line here that no symmetry predicts, and it had to be measured.** Whether the
answer stays complex when every imaginary part is zero splits the family in two:

```matlab
>> isreal(flipud(complex(1,0)))     % 1  — and fliplr, flip, flipdim, circshift, rot90
>> isreal(reshape(complex(1,0)))    % 0  — and permute, ipermute, squeeze, shiftdim, transpose
```

Nothing about the numbers differs between those two lists. What differs is what MATLAB's
implementation does: the first group copies its elements into a new array, and a new array is real
if what lands in it is; the second hands back the same data with a new shape stamped on it, and the
complexity flag goes along with the data. Reading the family as one rule would have got half of it
wrong whichever rule was chosen.

**Two neighbours turned up while probing, both older than the complex work and both about a value's
shape rather than its contents.** `circshift(7, 0)` came back *empty* — for a real scalar as much as
a complex one — because the verb asked an array for its dimensions and a bare number is not one.
And `transpose(zeros(0, 3))` answered 0-by-0 where `zeros(0, 3)'` answered the 3-by-0 MATLAB
answers: one operation carrying two readings of an empty, with the named function on the wrong side
of the disagreement.

## Decision

### The complex lane is the positions lane

`JgsBuiltins.RearrangedText.cs` becomes `JgsBuiltins.Rearranged.cs`, because it is no longer only
about text. Its wrapper gains a third lane beside the char-row promotion and the boxes gather: an
argument with complex elements in it has the verb run on its positions, and the complex elements are
gathered through the answer. `ComplexRearrangingBuiltins` is the list — the M122 positions list
entire — and a name added to it gets the behaviour with no code of its own, exactly as a name added
to the boxes list does.

`ctranspose` is on the list, with its conjugation applied to each element after the gather has put
it where the transpose sent it. That keeps the one thing it does beyond moving elements in one
place, rather than in a complex path of its own alongside the text path it already has.

### The narrowing list is MATLAB's seam, not a rule

`ComplexNarrowingBuiltins` names the six verbs whose answer is a fresh array — `circshift`, `rot90`,
`fliplr`, `flipud`, `flip`, `flipdim` — and the gather drops the complexity flag for them and keeps
it for everyone else. It is a list because MATLAB's behaviour is a list; the remarks in the file say
so and the tests pin all twelve names either way.

### The empty is the verb's own business

A gather over no elements hands back the verb's own answer untouched. The empty rules belong to the
verbs — flipping a 0-by-3 leaves it 0-by-3, and `reshape` may have been told a new shape — and
rebuilding one from a dimension list in the gather flattened both to 1-by-0.

### The two neighbours

`circshift` reads its argument's shape with `SizeDims`, which answers 1-by-1 for a bare number,
rather than with `JgsMatrix.DimsOf`, which answers about an array. `TransposeValue` settles an empty
by turning its two dimensions over before the vector path can flatten it, which is what the `'`
operator was already doing.

## Consequences

Twelve names carry complex elements that nine of them refused: `flip`, `flipud`, `fliplr`,
`flipdim`, `circshift`, `rot90`, `reshape`, `permute`, `ipermute`, `squeeze`, `shiftdim` and both
transposes. Every value, every shape and every `isreal` in a seventy-line head-to-head probe now
agrees with R2025b, N-D arrays and complex scalars included. Logical arrays, char rows, char
matrices, string arrays, cells and the integer and single classes were checked at the same time and
were already right — M105, M122 and M123 had each done their half — and they still are.

Seventy-four tests are new, in `MatlabShapeOfComplexM137Tests`. The four lanes run 8,196 tests.

The three ad-hoc complex paths inside `fliplr`, `flip` and `ctranspose` are left where they are. The
wrapper reaches those names first, so the paths are no longer what answers a script, but they are
what answers an internal caller that holds the `BuiltinFunction` rather than the environment binding
— and removing them would trade a dead branch for a live refusal.

### Divergences

- **`cat`, `horzcat` and `vertcat` still refuse complex arrays**, while `[a; b]` and `[a b]` build
  them. These are not rearrangements of one argument — they assemble several, and the positions
  trick needs a single numbering across all of them and a rule for which arguments are data (for
  `cat` the first is a dimension). The bracket is the form scripts overwhelmingly write and it is
  correct, so this is recorded rather than hurried.
- **`kron` refuses complex arrays.** It is on this family's edge and not in it: a Kronecker product
  multiplies the elements it moves, so it needs complex arithmetic rather than a complex gather.
- **`complex(x, 0)` answers a real value**, where MATLAB's answers a complex one with a zero
  imaginary part. Every remaining `isreal` disagreement in the probe traces to the constructor and
  not to a verb — the flag it fails to set is one the rearranging family already carries correctly
  when `zeros(n, 'like', 1i)` sets it, which is what the narrowing tests use. `complex(zeros(0, 3),
  zeros(0, 3))` answers a 1-by-0 for the same reason, losing the shape it was given.
- **`mat2str` prints `+0i` for a real element of a complex array**, where MATLAB prints the real part
  alone: `mat2str([1+1i 2])` is `[1+1i 2+0i]` here and `[1+1i 2]` there. It is a difference about
  printing rather than about values, and it predates this work — but it is why the tests in this
  milestone compare `real(v)` and `imag(v)` separately rather than reading one string back.
