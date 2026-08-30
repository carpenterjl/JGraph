# ADR 0110 — A folder listing is a struct array, and a name means something before it is a place

Milestone: **M109**
Status: accepted

## Context

Like M108, this came from a bug report rather than a plan row, and from writing the previous
milestone's own stress script. `stess_67.m` needed a file's size, and the MATLAB idiom for it is
the first thing anyone writes:

```matlab
d = dir('out');
for k = 1:numel(d)
    if ~d(k).isdir, fprintf('%s %d\n', d(k).name, d(k).bytes); end
end
```

Here that died on `'.isdir' needs a struct, but this is a cell.` `dir` answered a cell array of
names, with a path separator glued onto the folders so a reader could tell them apart:

```matlab
{'a.m', 'b.m', 'c.txt', 'sub\'}
```

That is a shape nothing in MATLAB produces, and it is not a small difference in a corner. `dir` is
how a script finds its own inputs, and every one of the six fields MATLAB answers with is a
question the caller then asks. The workaround in `stess_67.m` was `fopen`/`fseek`/`ftell` to read a
byte count — three calls and a file handle to learn something the listing already knew.

The old shape was deliberate. The doc comment said so:

> The bare-name echo of the cell *is* the listing, and `d = dir('*.m')` captures it — builtins have
> no nargout, so MATLAB's struct array form is deliberately not attempted.

Both halves of that reasoning have since stopped being true. M65 gave struct arrays storage of
their own, so there is a value to answer with. M99 gave builtins `KnowsWhenDiscarded`, so a name
*can* tell "print the listing" from "hand me the listing". The cell was a shape chosen for a build
that could not express the right one, and it outlived that build by ten milestones.

## Decision

**A listing is a struct array; printing it is a separate question.** `dir` answers MATLAB's six
fields — `name`, `folder`, `date`, `bytes`, `isdir`, `datenum` — in MATLAB's order, as a column,
and prints the names in columns when nobody caught the answer. The two arms read the same entries
from the same function, so the printed listing and the walked one cannot disagree.

**`.` and `..` are entries, not exceptions.** MATLAB lists them, and both are held against the
pattern like any other name: `dir('f')` includes them and `dir('*.m')` does not, because neither
ends in `.m`. The directory enumerator never yields them, so they are matched by hand against the
same simple expression .NET matches everything else against — the one place where the rule is
written twice, and it is written twice because there is no third way to have it applied once.

**`date` and `datenum` are one instant, truncated to the second.** The string carries no fraction,
so a `datenum` that did would disagree with the field beside it. Measured: MATLAB truncates a
`05:06:07.777` write to `05:06:07` in both fields, and does not round.

**`ls` is the same listing, worn differently.** It did not exist here at all. It is the entries'
names as a space-padded char matrix — a shape only reachable since M105 gave this build a real char
matrix — and it comes from `DirectoryEntries` too, so `ls` cannot name a file `dir` does not.

**A name means something before it is a place.** `exist('fix')` beside a folder called `fix` is
`5`, not `7`: MATLAB asks what the name resolves to before it asks what is on the disk, and only
`exist('fix', 'dir')` reaches the folder. Naming the kind still skips the built-in arm entirely, so
`'file'` and `'dir'` are answers about the disk alone.

**`what` answers MATLAB's field set rather than four names of its own.** It had `path m jgs mat
fig`; it now has MATLAB's twelve in MATLAB's order, as column cells, with `@Cls` reported as the
class `Cls` and `+pkg` as the package `pkg`. A missing folder is an empty struct array rather than
a throw, which is MATLAB's answer and is what lets a caller ask about a folder that may not exist.

## The discarded arm had never demoted its strings

`dir fix` — command syntax — raised `dir expects argument 1 to be a string, but got a string array`
while `dir('fix')` listed the folder. The cause is not in `dir`.

`BuiltinFunction.Call` and `CallMultiple` both run their arguments through `DemoteStringScalars`
first, which is what makes M63's string scalars invisible to the ~2,500 builtins that predate them.
The interpreter's two discarded-call sites reached `MultiOutput` *directly*:

```csharp
none(given, 0, discarded.Line, discarded.Column);
```

so the discarded arm was the one path into a builtin with no demotion and no exception translation.
`optimset` — the only user of the flag that takes arguments — is never written in command syntax,
so nothing had found it. Both sites now go through `BuiltinFunction.CallDiscarded`, which is the
same wrapper the other two arms use. That is a fix to every `KnowsWhenDiscarded` builtin, not to
`dir`.

## Divergences recorded

- **A printed listing wraps at 80 columns.** MATLAB lays its columns out to the width of the
  command window — 160 in `-batch` on this machine, 80 in a default desktop session. JGraph's
  console has no width to ask for, so the classic terminal's 80 is used. The names, their order and
  their padding are identical; only where the line breaks differs.
- **`what` carries a thirteenth field, `jgs`.** JGraph's own dialect has script files MATLAB has no
  kind for, so they are reported in a field of their own after MATLAB's twelve rather than folded
  into `m`. `stess_68.m` item 19 is this divergence and fails in MATLAB by design.

## Measured

A 34-line parity script over a fixed fixture — a listing, a pattern, a single file, a missing
folder, a sub-folder, `ls`, `what` and eight `exist` forms — run under this build and under MATLAB
R2024a on the same machine, comparing 88 lines of output field by field including `datenum` to
eight decimal places.

**87 of 88 lines are identical.** The one that differs is `what`'s extra `jgs` field, above.

Everything the fields carry was measured rather than assumed, and three of the answers were not
what the documentation would have suggested:

| Question | MATLAB's answer |
| --- | --- |
| Sort order | ordinal by code point — `+pkg`, `.`, `..`, `@Cls`, `Banana`, `_under`, `apple` |
| `dir('f/*')` | includes `.` and `..`; `dir('f/*.txt')` does not |
| Sub-second `date`/`datenum` | truncated, not rounded |
| `dir` of a missing folder | `0`-by-`1` struct array, not an error |
| `ls` of a name matching nothing | `0`-by-`0` char and a console notice — no error either |
| `exist('fix')` beside a folder `fix` | `5`, the built-in |

The `datenum` values agree with MATLAB's to within one unit in the last place — MATLAB's own differ
from `floor`-to-second by up to 5e-11 days, which is under the 1.16e-10 ulp at that magnitude, so
the two arrive at the same double by different arithmetic.

## Found on the way, not fixed here

Two gaps in the date surface next door to this one, both older than this milestone and both left
alone rather than folded in:

- `datenum` of a date *string* is refused; MATLAB reads `'04-Mar-2026 05:06:07'` and answers
  `740045.2126`. This is why `stess_68.m` item 8 reads the clock out of the string by hand instead
  of round-tripping it.
- `datestr(n, 'dd-mmm-yyyy HH:MM:SS')` answers `04-06-2026 05:03:00` — MATLAB's `mm` is a month and
  its `MM` is a minute, and the format letters are being handed to .NET, whose meanings are the
  other way round.

## Testing

`ConsoleBuiltinTests` — 17 tests, 12 of them new or rewritten: the six fields and their classes,
the column shape, the dot entries, the reported bug's own loop, `date`/`datenum` against a file
whose write time is set by the test, the empty listing that still knows its fields, the printed
listing, command syntax, `ls` as a char matrix and as an empty one, `what`'s field set and
orientation, and `exist`'s new precedence.

## Live checks

`stess_68.m` — 20 items, all 20 passing here and 19 of 20 in MATLAB R2024a, the twentieth being
the `jgs` divergence above. `stess_67.m`'s `filebytes` helper now reads `dir(path).bytes`, which is
what it wanted to say in the first place.
