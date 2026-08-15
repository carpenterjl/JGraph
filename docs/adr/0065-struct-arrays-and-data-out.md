# ADR 0065 — Struct arrays and data out

## Status

Accepted (M65, 2026-08-15). The fifth milestone of the M61–M68 language arc, and the one where the
value-model rule the last two established meets the case it does not cover.

## Context

`class(S)` said `'cell'` for a struct array, because that is what one was: a `JgsType.Cell` whose
elements happened all to be structs, recognised by scanning it. M41 built that in an afternoon and it
carried four milestones' worth of `regionprops` and `dir` results, so it earned its keep — but it
could represent states a struct array cannot be in (elements with different field sets), and it could
not tell itself apart from a cell a script had built by hand.

The other half of the milestone was a hole with no cleverness in it at all. Reading data has been
possible since M10; writing it was not possible in any form. A script could load a measurement file,
work on it, plot it, and then had nowhere to put the answer but the console. `writematrix`,
`writetable` and `writecell` did not exist, `readmatrix` refused by name, and the MAT-file pair could
not carry a logical, a sparse matrix, or a file MATLAB had written on a big-endian machine — and
could not read version 7.3 at all, which is the version MATLAB writes whenever a variable is large
or holds a string.

## Decision

### A struct array gets storage of its own, and this is not a reversal of M63 and M64

M63 gave string arrays no new `JgsType` and M64 gave time none either, both settling on a tag over
storage that already knew how to be an array. The rule those two ADRs wrote down — **a type here is
a meaning attached to storage that already knows how to be an array** — is not repealed here. It is
applied, and it points the other way.

The question the rule asks is whether the borrowed storage can hold exactly the states the type
allows. For a string array a `JgsValue[]` can, and for a duration a `double[]` can. For a struct
array a cell **cannot**: MATLAB's type has an invariant — every element has every field — that a cell
of independent structs has no way to state, let alone keep. Everything that went wrong before this
milestone went wrong through that gap. `S(2).b = 1` left element one without a `b`; `[S1 S2]` unioned
nothing; `fieldnames` asked whichever element it happened to reach.

So `JgsStructArray` is real storage: a flat column-major array of field dictionaries, plus the field
names an *empty* struct array has to remember because it has no element to read them from. The
enum did not grow — `JgsType.Struct` is the same member it always was — but its payload changed from
one dictionary to this.

### A scalar struct is the one-element case, which is what keeps the blast radius small

`JgsValue.AsStruct` still hands back a `Dictionary<string, JgsValue>`: element zero's. That one
decision left roughly sixty call sites untouched. Every one of them is asking whether an options bag
or a tagged struct carries a field, and every element of a struct array carries the same fields, so
element zero is the right answer for all of them. The places where one struct and many genuinely
differ — the field read, the field write, `class`, `numel`, `for`, concatenation, display — ask
`IsStructArray` first and never reach `AsStruct`.

This is the same shape as M63's demotion at `BuiltinFunction.Call` and M64's `TimeAwareReductions`
wrapper, arrived at from the opposite direction: **make the new case the general one and the old case
its degenerate form, then let the old code go on reading the degenerate form.**

### The payload is shared, not copied, so `S(k).f = v` can land

`ResolveStructElementForWrite` hands back the element's dictionary *by reference*, and the write goes
into it. That is the only reason the accumulation idiom every real script uses — `S(end+1).name = x`
inside a loop — costs one dictionary rather than a rebuild of the whole array per iteration. `end`
inside that subscript counts the elements already there, which is what used to be missing: nothing
told `end` what it was inside of, so the idiom was refused outright.

Growth fills the gap with elements carrying the array's fields, each holding `[]`, at the moment the
gap appears. The invariant is maintained where it is broken, not checked afterwards.

### A struct array displays its shape, not its contents

`disp` on a 1,400-element `regionprops` result used to print 1,400 structs. It now prints
`1400x1 struct array with fields: Area, Centroid, BoundingBox`, which is MATLAB's own behaviour and
for MATLAB's own reason: a wall of text is not a display.

### Writing refuses by name rather than flattening

This is the milestone's second thesis and it applies to all three output waves.

`writematrix(A, 'out.xlsx')` does not write a comma-separated file under a spreadsheet name; it says
it writes delimited text and asks for a `.csv` or `.txt`. `save` does not write a string array as the
numbers behind it; it names the type and refuses. `load` on a version 7.3 file holding a class it
cannot represent says `holds a myclass, which cannot be loaded` rather than producing a plausible
double. An HDF5 filter this cannot undo is named by its id.

The alternative in each case is a file that opens successfully and is wrong, which is the worst
failure a data path has.

### Version 5 is the only format written, and always will be

Reading version 7.3 is a hand-rolled HDF5 parser — the same call the project made for xlsx in M10, and
for the same reason: a NuGet dependency for one file format is a poor trade. Writing it would be a
different proposition. A writer has to produce bytes a real MATLAB will read, and the failure mode of
getting a superblock or a chunk index subtly wrong is not an error, it is a file MATLAB opens and
mis-reads. Version 5 is fully specified, already written here, and MATLAB reads it unconditionally.

`save -v7.3` therefore refuses with that reasoning rather than silently writing a version 5 file
under the flag.

### Byte order travels with the buffer

The version 5 reader became an instance rather than a set of static methods, holding one `_swap` flag
for the length of one file. The reason is specific: a compressed element inflates into a *second*
buffer that inherits the file's byte order, so a swap decision re-derived at each read would be right
for the outer bytes and wrong for the inflated ones. Byte order is a property of the buffer, not of a
call.

### `-append` is honestly a read, a merge and a rewrite

A version 5 MAT-file is a flat run of elements with no directory, so there is nowhere to append to.
`Append` reads what is there, replaces any variable of the same name, and rewrites the file. Saying
so in the code is the point: a reader who thinks it seeks to the end will be surprised by the
`InvalidDataException` a corrupt existing file throws out of a *save*.

### The dimensions reverse, and nothing transposes

HDF5 counts rows the opposite way round from MATLAB, so an m-by-n matrix is stored with shape
(n, m). The consequence is a happy one and worth writing down because it looks like a bug: reversed
dimensions over row-major storage *is* column-major storage over the original shape, so the stored
run of elements is already in MATLAB's order and the correct reader transposes nothing. The one real
transpose is a char matrix, for the same reason it was one in version 5.

### The version 7.3 fixtures are written by something that never heard of this reader

There is no MATLAB on this machine. Hand-building HDF5 fixtures the way the version 5 tests
hand-build their big-endian files was the obvious path and was rejected: **a hand-rolled writer
agreeing with a hand-rolled reader proves only that the two share an opinion.** The fixtures are
written by a real HDF5 library through `tools/make-v73-fixtures.py`, and checked in deflated and
base64-encoded in `MatV73Fixture.cs`, so the repository carries no binary assets and the tests need
no Python. That matches the `XlsxFixture` precedent from M10.

### A named load reads only what it was named

Found by the CLI probe, and not an HDF5 matter at all: `load('f.mat', 'ok')` failed when a *different*
variable in the file was unreadable, because both readers decoded everything and the caller filtered
by name afterwards. The wanted set now goes down into both readers, and the version 5 one learned to
read a variable's *name* without decoding what follows it. An object sitting beside your matrix no
longer spoils a load that was never about it.

## Consequences

Tests move from 4,447 to **4,514**, all green, 0 build warnings, and all **37** stress scripts pass.

### Three deliberate changes to existing tests and one to a frozen asset

Enumerated here because the arc's rule is that a flipped assertion is never silent.

- **`JgsIndexingTests`** asserted that `s(0)` on a JGS struct errors saying it is a struct. A struct
  is a 1-by-1 struct array now, so `s(1)` is its one element. The test pins the remaining refusal —
  reaching past the end — instead. This is a JGS change the freeze allows, because it turns an error
  into an answer.
- **`MatlabStressM43Tests`** wrote `struct('cells', cell(2, 2))`. A bare cell argument to `struct`
  spreads across the elements of a struct array now, which is MATLAB's documented rule, so that
  spelling builds a 2-by-2 struct array with an empty field rather than one struct holding a cell.
  The braces MATLAB requires — `{cell(2, 2)}` — are now in the test.
- **`stess_3.m` line 25**, the same spelling in a frozen asset, edited with the user's explicit
  one-line permission. It is the only edit any of `stess_1.m`–`stess_36.m` has ever received.

### What the struct-array flip surfaced

`class(S)` answering `'cell'` was the visible symptom, but the flip found the invariant breaks listed
above and one more worth naming: **a genuine cell of structs and a struct array were the same value**,
so `iscell(regionprops(...))` was true. Any script branching on that took the wrong branch silently.

### Four things the stress script found that four waves of unit tests did not

The M46 pattern for the fifth time, and the sharpest instance of it yet: every one of these is a form
a real script writes and no suite had reason to try, because a suite tests the verb it is about in
the spelling that verb prefers.

- **`[S; S]` flattened.** Two 1-by-3 struct arrays stacked came back as a column of six. The
  concatenation appended elements where column-major storage needs them interleaved, so the answer
  was the right elements in the wrong shape — which is exactly the failure that survives a unit test
  checking `numel`.
- **`isscalar` said yes to a struct array.** The shape predicates read `Array` and `Cell` and treated
  everything else as a single value, which was true of a struct until this milestone. `isscalar`,
  `isvector`, `isrow` and `iscolumn` now ask a struct for its shape like anything else.
- **`[a, b] = S(1:2).f` did not distribute.** M61 made a struct field a comma-separated list, but the
  spread path recognised only a plain name, so the same idiom over a slice reported a shortfall of
  outputs. It now accepts a subscript over a name — in both AST shapes, because MATLAB spells
  indexing and calling alike and the parser cannot tell which one it is looking at.
- **`writelines` had no `readlines`.** The asymmetry was invisible from inside wave B, where the
  question was what to write. It is obvious the moment a script tries to check what it wrote.

### Recorded limits

- **An element write names one linear subscript.** `S(3).f = v` grows; `S(2,3).f = v` is refused by
  name. Two-subscript *reads* work, so `S(2,3).f` on an array that exists is fine. Growing into a
  second dimension is the rarer half of an idiom that is nearly always a list.
- **A field cannot be set across a whole array.** `[S.f] = deal(1)` is not here; the refusal names the
  element form.
- **`.xlsx` output is refused by name.** The xlsx machinery in this repository reads; a workbook
  writer is a milestone of its own.
- **Version 5 cannot carry a string array, a datetime, a duration, or a map**, and says so by name
  rather than writing the numbers behind them. MATLAB is version 7.3-only for strings for the same
  reason.
- **Complex sparse matrices are not read** from version 7.3, and HDF5 layout version 4 chunk indexes
  and fractal-heap groups are refused by name. None of the three is what MATLAB writes for ordinary
  variables.
- **A rank-1 HDF5 dataspace reads as a row.** The format does not distinguish; MATLAB writes vectors
  either way and a row is the commoner intent.
- **A struct array and a scalar struct whose every field is an equally shaped cell are stored
  identically** in version 7.3. The heuristic reads that shape as a struct array. This is a genuine
  ambiguity in the format, not a shortcut.
- **Four pre-existing gaps this milestone surfaced and did not fix**, each older than the work and
  each deserving its own change rather than a ride-along here: a char matrix is a column of char rows
  rather than a matrix of characters, which is also why `class` of one answers `'double'`;
  `fieldnames` answers a row where MATLAB answers a column; and `size([])` answers `[1 0]` where
  MATLAB says `[0 0]`. `stess_37.m` says so at the two places it would otherwise assert a MATLAB
  shape, so the divergence is visible in the corpus rather than only here.

### The coverage table does not move, and the total does

None of the fourteen names this milestone adds is documented by MATLAB as kind **builtin** — every one
of `writematrix` `writecell` `writetable` `writelines` `readmatrix` `readcell` `readlines` `csvwrite`
`dlmwrite` `struct2table` `table2struct` `orderfields` `getfield` `setfield` is kind *function*, and
`struct` itself is a *class*. The 514-name table stands where M60 left it, and the across-every-kind
total moves by thirteen of the fourteen: `writelines` is not in this install's documented set at all,
so no arithmetic over these tables can see it. That total is the number this milestone is measured
by, which is the third milestone running where that is true — and it is the arc's
thesis stated as arithmetic. What was broken here was not a missing name but a type that lied about
what it was.

## Live checks for the user

Batch cannot see these:

- A struct array in the Workspace pane and the Data Viewer. It is a new payload behind an old type,
  so the interesting case is a `regionprops` result: the pane should say what it is rather than
  unrolling it.
- A version 7.3 MAT-file saved by a real MATLAB, opened with `load`. Every fixture here was written by
  an HDF5 library following MATLAB's conventions, which is one remove from the article itself.
- `writetable` output opened in Excel, to confirm the header row and quoting survive the trip.
