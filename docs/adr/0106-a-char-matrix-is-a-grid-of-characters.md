# 0106 — A char matrix is a grid of characters

Date: 2026-08-29 · Milestone: **M105** · Status: accepted

## Context

ADR 0105 recorded a divergence it could not close inside its own milestone: **a char matrix here
reported `double` for its class and rows-by-one for its size.** `char('a', 'bcd')` answered
`class=double size=[2 1]` where MATLAB R2024a answers `class=char size=[2 3]`.

The cause was the representation. A char matrix was a plain `JgsValue.Array` of equal-length
`JgsType.String` rows reshaped to N-by-1, with nothing tagging it as text — so `ClassOf` fell
through to `double` and `SizeDims` reported the array's own N-by-1. It predated M104 by roughly
forty milestones; M104 only made it visible on four more names, because `strvcat`, `str2mat`,
`strjust` and `num2hex` all build one.

The class and the size were the reported symptom, but they were not the extent of it. Measured
first, against MATLAB R2024a: `A(:, 2)` raised an index error rather than answering a column,
`double(A)` and `upper(A)` were refused outright, `char(A)` came back 1-by-6, `[A; 'xy ']` came
back 3-by-1, `fprintf('%s', A)` printed the rows instead of the column-major run, and
`num2str([1; 22])` answered a **cell**. Almost nothing about a char matrix was right, which is also
why almost nothing could depend on it.

## Decision

**A char matrix is an ordinary numeric array of code points wearing a tag** — `_isCharMatrix` on
`JgsValue`, minted beside `MarkStringArray` and `SetNumericClass` — and not a column of char rows.
This is the same answer M47 gave the integer classes and M63 gave the string array: the storage is
what it always was, and a tag carries the meaning.

The alternative was to keep the column of char rows and tag *that*. It was rejected because of
what each choice costs. A char matrix genuinely **is** a 2-D grid of characters, so making the
storage say so means `size`, `numel`, `length`, `ndims`, indexing in every subscript form, `A(:)`,
`end`, the transpose, `double`, `reshape` and the comparison operators are the array machinery that
was already there, reading a real shape. Tagging the column of rows would have left every one of
those to be taught separately, and indexing — the deepest and most fragile of them — would have had
to synthesise a character-level view over row-level storage. The measured evidence is that the
whole of indexing needed **one line**.

**A char row is `JgsType.String` and stays so; there is exactly one representation of one.**
The tag is only ever for the stack. This is what keeps the rest of the text surface working
unchanged, and it makes one rule load-bearing: **a char value with a single row collapses back to a
char row.** `A(2, :)` is the same `'bcd'` a literal would have been, so every builtin that has
always taken a char row goes on taking it. `WrapCharMatrix` is that rule, and it is applied at
indexing, at `reshape`, and in `CarryValueTags` — a transpose is the only other path that can reach
a single row, via `A(:, 2)'`.

**A bracket stacks char rows; it does not pad them.** `['ab'; 'cd']` is 2-by-2 char, `[A, A]` joins
side by side, and `['a'; 'bcd']` is refused with MATLAB's own
`MATLAB:catenate:dimensionMismatch`. Padding is `char` and `strvcat`'s job and no one else's. The
path is gated tightly — every piece char, and either more than one bracket row or a char matrix
among them — so a bracket mixing char with numbers is left exactly as it was.

### The tag has to be carried, and by name

Nineteen shape verbs — `sortrows` `fliplr` `flipud` `flip` `rot90` `circshift` `repmat` `horzcat`
`vertcat` `cat` `sort` `unique` `permute` `triu` and the rest — all answered the **right shape**
immediately, because the storage is a real 2-D array, and all **dropped the tag**, because each
mints a fresh wrapper from the code points. They live in five different files.

They are retrofitted by name in one place, `KeepCharMatrixKind`, which is the move M63 already made
for the text-kind-preserving verbs. Fifteen call sites would each have had to remember; the
sixteenth would not have. Two details are load-bearing: the wrapper tests **every** argument, not
just the first, because `cat` takes the dimension in front of the values; and only the **first**
output is re-tagged, because `sort` and `unique` answer positions in their second and third.

### A defect found beside the road

`num2str` of a matrix answered a **cell** of char rows. It had already computed MATLAB's own column
alignment — the rows were equal-width and correct — and then put them in the one container none of
the char rules apply to. `num2str([1 20; 300 4])` is 2-by-8 char in MATLAB and was a 1-by-2 cell
here. `int2str` routes through it and was wrong the same way.

### Divergences recorded here

- **Char arithmetic and comparison do not read code points** — `'abc' == 'abd'` is a scalar `false`
  here where MATLAB answers `[true true false]`, and `'a' + 1` is `"a1"` where MATLAB says 98. A
  char row is a `JgsType.String` rather than a vector of code points, and the whole language treats
  it that way. This is pre-existing, is a milestone of its own, and is untouched here: it is why
  `A == ' '` still answers all-false, though it now at least answers in the right shape.
- **`upper` `lower` `strtrim` `deblank` refuse a char matrix** — they refused one before M105 too, so
  nothing regressed, but MATLAB accepts all four. Each needs its own measured rule and they do not
  share one: `deblank` row-wise then re-padded is exactly MATLAB's "drop trailing all-blank
  columns", `strtrim` is a *column* operation and cannot be done row-wise at all, and `strlength` of
  a char matrix is a scalar. Deliberately not half-done.
- **`char` of a row string array is 2-D here and N-D in MATLAB** — `char(["a", "bbb"])` is
  1-by-3-by-2 in R2024a and 2-by-3 here. N-D char arrays are their own arc; the column form
  `char(["a"; "bbb"])` agrees exactly.

## Consequences

- ADR 0105's first recorded divergence is **removed**, not struck through — the harvest lifts
  struck-through bullets whole. That takes `docs/matlab-divergences.md` from **204 to 203**; the
  three narrower divergences this ADR records in its place bring it to **206 across 38 ADRs**.
- `stess_64.m` item 22 stops asserting the divergence and starts asserting agreement. It now passes
  in **both** engines; the file is 25/25 here and 22/25 in MATLAB, where the three failures left are
  the divergences it names by design.
- The MAT-file reader and writer stop sniffing for "a column of equal-length strings" and ask the
  tag, which is both cheaper and exact — the sniff could not tell a char matrix from an ordinary
  array that happened to hold text.

## Measured

Everything above was measured against MATLAB R2024a before it was written, and again afterwards. A
79-line assertion script covering class, size, `numel`, `length`, `ndims`, every subscript form,
the transpose, `double`, concatenation, the refusal identifier, `cellstr`, `strjust`, `strmatch`
and `%s` runs green **unmodified in both engines**. A 19-verb shape-verb probe is byte-identical
between them.

Three diffs remain on the full probe, all named as divergences above or not char-matrix questions
at all: char comparison, `strtrim`, and the multi-line display block — JGraph's display is its own
compact house style for every type, including numeric matrices.

## Testing

`MatlabCharMatrixTests` — nine tests over what a char matrix is, how it is read, how it is built,
and that the tag survives the wrapper. Three existing tests asserted the old behaviour and now
assert MATLAB's: the `char`/`cellstr` conversion test, `num2str`'s several-rows test (renamed off
"…ComeBackAsACell"), and the MAT-file round trip.

**6,246 tests green, 0 warnings, five coverage verifiers green.** The whole stress suite is
unchanged from baseline — the two failures in it (`stess_24` item 21, `stess_31` item 25) reproduce
on a stashed checkout of HEAD, are stale expectations from earlier milestones, and are spawned as
their own tasks rather than patched here.

## Live checks for the user

```matlab
A = char('a', 'bcd');
class(A)        % char   — was double
size(A)         % [2 3]  — was [2 1]
A(:,2)'         % ' c'   — was an index error
double(A)       % [97 32 32; 98 99 100]
['ab'; 'cd']    % 2-by-2 char
num2str([1;22]) % 2-by-2 char — was a 1-by-2 cell
```
