# ADR 0216 — What a class says about itself, and a file read as text: open items 14, 27, 29 and 34

## Status

Accepted, 2026-10-08. The fifth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored). Every listing, identifier and sentence was recorded from R2025b (probes `probe_b6` to
`probe_b6f` in the open-items scratch, then the fixture `oi_class_answers`).

## Context

When triaged, half of items 27 and 29 already worked. `superclasses` of a user class, and `struct`
of one object with its hidden and private properties, had arrived with app-building stage U6. These
gaps were left:

- `methods(x)` as a statement printed `ans = {...}` for every class but a .NET type.
- `superclasses` as a statement did the same.
- `feature('getpid')`, which the device fixtures need, did not exist.
- `struct(obj)` gave no warning, refused an array of objects, and raised when a getter refused.
- `fread(fid, '*char')` answered doubles.
- `fileread` did not exist.

## Decision

### `methods` (item 14)

Asked for nothing, `methods` prints R2025b's listing for a user class (`ClassMethodsListing`).

- **Header:** a blank line, "Methods for class X:", and a blank line.
- **Names:** the instance methods by character code, the constructor among them, an implicit one
  included. Methods a class inherits from a base other than `handle` count as its own; `copy` of
  `matlab.mixin.Copyable` is one.
- **Statics:** a "Static methods:" block.
- **`handle`:** a handle class ends with "Methods of X inherited from handle.".
- **Columns:** each name is padded to the longest plus two, filled as the .NET listing fills them
  (ADR 0175).

`-full` writes each method's signature from its declaration. The outputs come first, one bare or
several in brackets. The inputs follow in parentheses only when there are any: `obj OiKid`,
`[a, b] two_out(obj, x, varargin)`, `skip_arg(~, y)`, `Static r make(n, m)`. An inherited line ends
with "  % Inherited from X". `handle`'s lines are untyped, unlike a .NET type's, and all lines are
ordered by method name. Asked for a cell, `-full` gives the same lines without the inheritance
notes, as a library's are (ADR 0183).

A plain struct and a function handle list R2025b's 11 and 7 names, as a cell and as a listing.
Before, they answered an empty cell. In the MATLAB dialect a name that is nothing (no class, no
.NET type, no library, no builtin) prints "No class 'X'." and answers `[]`. A name that is a
builtin, `double` for one, keeps its refusal rather than be called no class.

### `superclasses` and `feature` (item 27)

Asked for nothing, `superclasses` prints "Superclasses for class X:" with each name indented four,
or "No superclasses for class X.", between blank lines. `feature('getpid')` is this process's id as
a double. The key is matched in any case, and extra arguments are ignored.

### `struct` of an object (item 29)

The MATLAB dialect warns `MATLAB:structOnObject` with R2025b's sentence. As in R2025b, `lastwarn`
is set even while the warning is off. A property whose getter refuses is left out of the struct. An
array of objects answers a 1-by-1 struct from its first element, as R2025b does.

### `fread` and `fileread` (item 34)

A precision whose class side is `char` (`'*char'`, `'uint8=>char'`, `'char=>char'`) answers characters
in the shape the size asked for. `fileread(file)` reads the file whole as a char row, as UTF-8, keeping
line ends and a byte-order mark (R2025b keeps it as U+FEFF). `'Encoding'` names another encoding, in
any case. The refusals carry R2025b's identifiers:

| Case | Identifier |
| --- | --- |
| A missing file | `MATLAB:fileread:cannotOpenFile` |
| A number | `MATLAB:validators:mustBeNonzeroLengthText` |
| A cell | `MATLAB:validators:mustBeTextScalar` |
| An empty name | `MATLAB:validators:mustBeNonzeroLengthText` |
| An unknown option or an odd pair | `MATLAB:TooManyInputs` |
| An unknown encoding | `MATLAB:iofun:InvalidEncoding` |

The empty-name refusal has its own sentence.

## Measured

- `oi_class_answers`: 77 lines, 75 exact and 2 stamped divergences (below).

## Divergences

- **The inherited-from-`handle` line has no hyperlinks.** R2025b writes it with two hyperlinks,
  `<a href="matlab: methods handle">Methods</a>` and a `helpPopup` link on `handle`, even under
  `-batch` and `evalc`. JGraph writes the text, as its .NET listing does (`oi_class_answers`,
  `m_shape`, `m_copykid`, `div=0216`).
- **Built-in classes keep their methods and bases unlisted.** `methods` of a number, char, logical or
  cell still refuses, where R2025b lists them: 270 names for `double`, toolbox overloads among them.
  `superclasses` of `table`, `datetime` or `inputParser` answers no bases, where R2025b names its
  implementation classes (`tabular`, `matlab.mixin.internal.datatypes.TimeArrayDisplay`,
  `matlab.mixin.Copyable`).
- **`feature` answers only `getpid`.** Any other key is `JGraph:feature:unsupported`. R2025b has
  hundreds of undocumented keys and calls an unknown one `MATLAB:feature:unknownFeature`, "Feature X
  not found"; JGraph cannot tell an unknown key from one it lacks.
- **`struct(obj, 1)` keeps JGraph's sentence.** R2025b says `MATLAB:Cell2Struct:NonStringFieldNames`.
- **`struct` of an empty object array is not reached.** `Class.empty` does not exist yet (open
  item 65). R2025b answers a 1-by-0 struct with the fields.

## Consequences

A script that prints `methods(obj)` or `superclasses(obj)` to learn a class sees R2025b's layout. A
script that reads a text file with `fileread` or `fread(fid, '*char')` gets char, so `strsplit`,
`regexp` and `strfind` take the result as they do in MATLAB.
