# ADR 0219 — The 1-by-0 char is not '': open item 31

## Status

Accepted, 2026-10-08. The eighth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored). Every shape was recorded from R2025b (probe `probe_b9` in the open-items scratch, then the
fixture `oi_empty_char`).

## Context

The item asked for a 1-by-0 char kept apart from `''`. That representation already existed:
`blanks(0)` answered the 1-by-0 char, a char matrix of one row and no columns
(`JgsValue.CharMatrix([""])`). What was missing were the operations that produce it in R2025b, and
the readers that refuse it.

- **Producers that collapsed it to `''`:**
  - a char row indexed by an empty range, mask or vector (`x(1:0)`, `x(x == 'z')`);
  - `char(zeros(1, 0))`, `repmat('a', 1, 0)` and `sprintf` of nothing;
  - `strcat` of two 1-by-0 chars;
  - a bracket of empties holding one (`[blanks(0), '']`).
- **Readers:**
  - `isequal(blanks(0), '')` answered true;
  - `mat2str` and `strjoin` refused the value outright.

## Decision

`JgsBuiltins.EmptyChar(rows, cols)` mints the empty char of a shape: `''` for 0-by-0, the 1-by-0
char, or an n-by-0 or 0-by-n char matrix.

- **Indexing.** Nothing picked from a char row keeps the row's orientation, so the answer is
  1-by-0, as for any vector. Only a 0-by-0 index (`x([])`) answers `''`. A one-row empty read out of
  a char matrix (`x(:, 1:0)`, `x(1, 2:1)`) is 1-by-0 too (`WrapCharMatrix`).
- **`char`** of an empty code array keeps its shape: `char(zeros(1, 0))` is 1-by-0 and
  `char(zeros(0, 3))` is 0-by-3.
- **`repmat`** of a char row asked for no columns is 1-by-0. The branch existed, but the one-row
  case above it answered first.
- **`sprintf`** answers a row always, so nothing written is 1-by-0.
- **`strcat`** of 1-by-0 chars is 1-by-0; `strcat('', '')` stays `''`.
- **Brackets.** An empty char other than `''` counts as char and contributes its rows of nothing
  when nothing else does. `[blanks(0); '']` and `[blanks(0), '']` are 1-by-0, and
  `[blanks(0); blanks(0)]` is 2-by-0.
- **Readers:**
  - `isequal` compares `''` as the 0-by-0 it is, so it is unequal to the 1-by-0 char and still
    equal to `[]`;
  - `IsTextScalar` and `TextOf` read the 1-by-0 char as empty text, so text builtins take it;
  - `strjoin` takes it in a cell;
  - `mat2str` writes it as R2025b does, `zeros(1,0)`.
- **`mididevice`'s `Input` and `Output`** of a one-sided device are 1-by-0, as their `(1,:) char`
  declarations make them in R2025b. ADR 0194's divergence is retired.

## Measured

- `oi_empty_char`: 57 lines, all exact.
- `midi_devices` `dev_input_size`: retired to exact.

## Divergences

- **A text builtin given the 1-by-0 char may answer `''`.** `TextOf` reads it as empty text, and a
  builtin that builds its answer from text gives the 0-by-0. The fixture's `upper`, `fliplr`,
  `strtok` and `deblank` lines agree with R2025b; others were not measured one by one.

## Consequences

`s = x(1:0); s = [s 'a']` and the other ways scripts grow a row from nothing behave as in R2025b. A
`switch` on a 1-by-0 char does not match `case ''`, as in R2025b (`oi_empty_char`, `switch_1x0`).
