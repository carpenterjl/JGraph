# ADR 0063 — String arrays

## Status

Accepted (M63, 2026-08-15). The third milestone of the M61–M68 language arc, and the first to
introduce a type. It is also the first to change what an existing, correct script means: a
double-quoted literal used to be a char row and is now a string.

## Context

`isstring` was hardwired to `false` and `class("abc")` answered `'char'`, so every script that
branched on the two being different took the wrong arm. That much was known.

What the corpus sweep found was more interesting: **the representation was already there.**
`["a", "b"]` had built an array of `JgsType.String` elements since the MATLAB dialect landed —
`numel` was 2, indexing worked, `cellstr` worked, `join` worked. What was missing was not storage but
identity. `class` answered `'double'` for it, because an array of strings is an array and the
numeric-class tag is what `class` reads off an array.

That finding is what set the shape of the milestone.

## Decision

### A string array is a tagged array of strings, and there is no new `JgsType`

The plan called for a `JgsType.StringArray`. The sweep argued against it. Shape, indexing, growth,
deletion, reshape, transpose, `end`, logical masks and N-D folding all already worked on an array of
strings; a new enum member would have had to reimplement every one of them, and would have added a
case to a switch surface some 2,500 builtins wide.

So a string array is `JgsType.Array` carrying `IsStringArray`, in the same manner as M47's numeric
class and M62's struct class name. The blast radius of the type itself is one boolean.

### A string scalar is a 1-by-1 string array

Not a convenience — MATLAB's own model. `numel("abc")` is 1 because the string is one element, where
`numel('abc')` is 3 because the char row is three of them. Storing the scalar as a 1-by-1 array makes
that fall out rather than needing to be arranged, and is the only point on which the two
representations genuinely disagree.

### The demotion boundary is what let 2,500 builtins go unchanged

`BuiltinFunction.Call` replaces every string **scalar** argument with the char row it stands for
before the body runs. `title("Speed")` and `plot(x, y, "LineWidth", 2)` went on working the day
`"..."` stopped being a char, with nothing edited. A string array of any other size is not a char row
and is passed through untouched.

`KeepsStringArguments` opts a name out. The list is deliberately short — the type questions
(`class`, `isa`, `isstring`, `ischar`, `isstr`) and the size questions (`numel`, `length`, `size`,
`ndims`, `isempty`, `isscalar`, `isvector`, `isrow`, `iscolumn`, `height`, `width`), plus
`ismissing`. Most of them need no code of their own: they answer correctly the moment they can see
the 1-by-1 array, because that is genuinely what a string scalar is.

**The flag is decided from the name in the constructor, not stamped onto the environment
afterwards.** Several builtins are declared twice — an inner one that does the work and an outer
wrapper adding a second output — and only the wrapper is reachable from the environment. The first
version marked the environment, which reached the wrapper alone: `numel("abc")` correctly answered 1
while `size("abc")` answered 1-by-3, because `size`'s wrapper called an unmarked inner.

### Elementwise text is one rule, applied in one place

Fifteen text builtins live in five files and all now obey the same rule: *a text function applied to
several pieces of text answers once per piece, and the container comes back.* That is applied as a
wrapper pass over the finished environment rather than as fifteen edits, because it is one rule.

The test for belonging on the list is whether the function consumes the whole array. `join` does, so
it is not on it; `upper` does not, so it is. `join`, `split`, `strsplit`, `strjoin` and `compose` get
a different, smaller wrapper that keeps the *kind* of text rather than mapping: string in, string
out.

### String concatenation resolves before implicit expansion

`"p" + ["1" "2"]` must not reach `JgsBroadcast.Map`. A string array is an array underneath, so
expansion would pair the elements up, join each pair as char, and reassemble a plain array — the
right text with the wrong type. `ConcatenateStrings` does its own spreading, which is the same rule
applied once instead of twice.

`'a' + 'b'` is left alone. JGraph has concatenated char rows with `+` since long before this
milestone, where MATLAB adds their code points; changing that is a separate decision about char, not
about string, and is recorded below rather than made here.

## Consequences

Tests move from 4,404 to **4,422**, all green, 0 build warnings, and all **35** stress scripts pass.

### The corpus survived the flip, and one script had been waiting for it

Every one of `stess_1`–`stess_34` passed the literal flip unchanged. `stess_23` is the striking one:
its section 4 is titled *"Char rows join; strings stay apart"* and asserts `numel(["a", "b"]) == 2`.
That passed before because `["a","b"]` was the char row `'ab'`, whose `numel` is also 2. It passes
now for the reason it was written for. A test that passes for a new reason is worth recording, and
this one had the intended semantics written into its title two milestones early.

### Two defects the type surfaced rather than caused

Both were found by `stess_35`, and neither is M63's doing:

- **`legend({'a', 'b'})` was read as a list of handles.** MATLAB's most common spelling reached
  `PlotsOf` and failed complaining about a figure handle — a message about entirely the wrong thing.
  A cell of char, and now a string array, is a list of names.
- **`[a b]` never joined two char rows held in variables.** The char-join test required a
  single-quoted *literal* among the elements, which was the only way to tell a char row from a
  double-quoted one before strings had a type of their own. Now the values can be asked directly, so
  the test is on the values and `[first last]` joins as it always should have.

The pattern is the one M62 recorded from the other side: a type that makes a distinction explicit
finds the places that were guessing at it.

### Deliberate test flips

- `MatlabHandleGraphicsTests.SingleQuotedPiecesJoinIntoOneWordAndDoubleQuotedOnesDoNot` did **not**
  need editing, which is worth stating: it already asserted `numel(["a","b"]) == 2` and
  `strcmp(pair(1), 'a')`, and both hold for the new reason.
- `JgsBuiltinCatalog`'s `isstring` entry — "Always false — JGraph has char text, not MATLAB string
  arrays" — is now a description of a name that answers. The old wording was accurate while it was
  true.

### Recorded divergences

- **`'a' + 'b'` concatenates, where MATLAB adds code points.** Predates this milestone; strings now
  make the intended spelling (`"a" + "b"`) available and correct.
- **`char` of several strings stacks padded rows rather than building a char matrix.** JGraph has no
  char-matrix type: `['ab'; 'cd']` has always been an array of char rows, and `char(["a" "bbb"])`
  answers the same way.
- **A tag that a builtin does not know about is lost.** `unique`, `sort`, `join`, `split` and the
  elementwise family were taught; anything else handed a string array and returning a rebuilt array
  hands back a plain one. The failure is visible (`class` says `double`) rather than silent.
- **`fliplr` still refuses a char row.** It is an array function here and `reverse` is the text one;
  MATLAB allows both.
- **`extractAfter` and friends answer empty text when the marker is absent**, where MATLAB answers
  `<missing>`. Empty text is still one string, and an array of none breaks every caller that goes on
  to index it.

### The JGS surface is untouched

`HasStringArrays` is `IsMatlab`. JGS never had a string type for double quotes to mean, its surface
is frozen, and `JgsKeepsItsOwnMeaningForDoubleQuotes` is the test that fails if the gate ever stops
being a gate.

## Live checks for the user

Batch cannot see these:

- A string array in the Workspace pane and the Data Viewer — the panel names JGS types, and a string
  array is an array of strings underneath, so the worst case is that it reads as an array.
- Completion and signature help on a line containing a `"..."` literal, now that the lexer's
  distinction between the two quotes carries a meaning downstream.
- `disp` of a multi-element string array in the console, where the quoted form is the only thing on
  the page that says string rather than cell.
