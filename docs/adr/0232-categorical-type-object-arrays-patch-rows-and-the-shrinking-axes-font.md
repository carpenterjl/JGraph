# ADR 0232 — A categorical type, object arrays past the first cut, a patch's data rows, the axes font that shrinks with its box, and the tick caps refitted: open items 114, 115, 116, 118 and 119

## Status

Accepted, 2026-10-13. The nineteenth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), five items wide, with three decisions by the user before it started: item 114 and
item 118 land in both dialects, item 115 is a first-class type rather than a tagged cell, and item 90
(the `lu` timing on a quiet machine) waits until the batch is done. Every answer was recorded from
R2025b headless: probes `probe_114`/`114b`/`114c`, `probe_115`/`115b`/`115s`, `probe_116`/`116b`,
`probe_118`/`118b`/`118c` and `probe_119`/`119j` in the open-items scratch, then the fixtures
`oi_patch_rows_axes_font_short_rulers`, `oi_object_arrays` and `oi_categorical`.

## Context

- **114.** A patch here answered `XData`, `YData` and `ZData` as rows and `Vertices` as n-by-3;
  R2025b answers a column per face (`[20 1]` for a pie slice, m-by-n for several faces), an empty
  `ZData` and two-column `Vertices` for a flat patch, `CData` as the ink 1-by-1-by-3 when no colour
  data was given, a column per face or per vertex otherwise (`FaceColor` `'interp'` for per-vertex
  data, `'flat'` for per-face), and a text's default `FontSize` is 10, not 12. A vector `XData`
  write is one face over the shortest coordinate, a matrix one face per column, and the vertex list
  grows with NaN; `FaceVertexCData` written as RGB rows is colours, not readings.
- **115.** `categorical` answered a cell of names. R2025b's is a class: a value set and category
  names, `Ordinal` and `Protected`, comparison by name and by order (`InvalidOrdinalComparison`
  when the orderings differ, `NotOrdinal` for `<` on an unordered one), concatenation that merges
  categories (refused across ordinal and non-ordinal, or across two ordinal sets in different
  orders), indexed writes of names (`InvalidRHS` for a number, `ProtectedForAssign` for a new name
  on a protected set), `<undefined>` for a missing, `summary` as text or as a struct of `Size`,
  `Type`, `Categories`, `Counts` and `NumMissing` (`Min`, `Median`, `Max` added for an ordinal),
  the display with five spaces before the first column, six between and one after, `sort` with
  undefined last ascending and first descending, `unique` with each undefined its own, the set verbs
  over names, `fillmissing`/`rmmissing`/`standardizeMissing`, `findgroups`, `discretize` to
  categories, and `StyleConfigurations.Target` as a protected categorical whose categories are the
  component's (`{table, column, row, cell}`, `{tree, node, level, subtree}`, `{listbox, item}`,
  `{dropdown, item}`).
- **116.** Batch 17 gave value classes arrays. R2025b also spreads `a.p` as a comma-separated list
  (`[a.p]`, `{a.name}`, `[a.p] = deal(…)`, `x = a.p` takes the first), reads, writes and deletes
  `a(r, c)`, fills a gap `a(5) = s` opens by the class's no-argument constructor (one call shared
  for a value class, one per slot for a handle class), converts a foreign piece of a bracket through
  the constructor (`[s1 5]` is `Val116(5)`, `[s1 Sub116(2)]` keeps the subclass object), refuses
  `a.p = v` on an array, holds handle objects in arrays (writes through, `==` by identity,
  `delete(ha)` and `isvalid(ha)` over all), answers `Class.empty`, `isequal` elementwise, `arrayfun`,
  `cellfun`, `num2cell` and `cell2mat` over objects, the shape verbs, `properties`, `methods` and
  `isprop` of an array, and `if obj`, `switch obj` or an operator on a value object with its own
  identifiers.
- **118.** R2025b's axes font is automatic: 8 points for a plot box under 132 pixels wide or 106
  high, 8.5 from there, 9 from 180 or 144, 9.5 from 228 or 182, 10 from 276 or 222, the smaller of
  the two bands winning; the title and labels at 1.1 of it, a legend and colorbar at 0.9,
  `FontSizeMode` staying `'auto'` until a `FontSize` is written and shrinking again when it is set
  back; a text keeps 10 and a `uiaxes` 12. The box is the axes' own inner box, so a subplot and a
  tiled axes shrink too.
- **119.** The count caps of ADR 0231 had been fitted to axes whose font was shrinking with them,
  so a cap read at the wrong font. Refitted at a fixed font over 24 ruler lengths and 150 ranges
  (probe_119, 11,000 answers): horizontal caps 4/3 at 105 pixels, 5/5 at 165, 6/5 at 205, 7/6 at
  290, 8/7 at 330, 8/8 at 380, 10/9 at 420, 10/10 at 470 and 12/11 beyond; vertical 4/3 at 65, 5/5
  at 85, 6/5 at 105, 7/6 at 125, 8/7 at 155, 8/8 at 205, 10/9 at 230, 10/10 at 252 and 12/11 beyond
  (landing cap first, free cap second, in R2025b's pixels at 10 points, which are this build's at
  11). A step is counted twice: tolerantly (a limit a hair past a tick still counts it) under the
  landing or free cap, else exactly in double arithmetic (`3 × 0.1` overshoots 0.3) under the free
  cap; when nothing fits, the coarsest step with two ticks inside. The polar r ruler counts the
  same way whatever its size, capped at 7 landing and 5 free; a colorbar is budgeted at its own
  length with the axes font at 0.9; the theta ruler ticks every 30 degrees over 90 or more, every
  15 over 45 or more, every 5 under that. R2025b's theta ticks and the colorbar's were 98 to 100
  per cent reproduced, the polar r ruler 83 per cent, rulers under about 200 pixels 86 to 95.

## Decision

### 114: a patch's data in R2025b's shape

`PatchPlot.IsFlat` (saved) records a patch made with no z: `patch(x, y, c)`, `fill`, a pie wedge,
`'Faces'`/`'Vertices'` with two columns, `XData`/`YData` by name; a z at construction, a `ZData`
write or three-column `Vertices` make it a patch in space. `XData`, `YData` and `ZData` read a
column per face (`PatchCoordinates`, `UniformFaces`), `Vertices` two or three columns, `CData` and
`FaceVertexCData` the ink, a scalar, a column or an m-by-n (`PatchColorData`); writes rebuild the
faces (`WritePatchCoordinates`, `WritePatchVertices`, `WritePatchFaces`) and `SetPatchColorData`
sets `'interp'` for per-vertex readings. RGB rows written as `FaceVertexCData` are kept as written
(`PatchPlot.ColorTable`, read back exactly) and carried to the renderer as `FaceColors` or
`VertexColors`. A patch made by `XData`/`YData` alone is filled with the ink. `TextAnnotation`'s
default `FontSize` is 10 (the DTO default too) and it answers `FontSizeMode`. The text ink stays
0.251 grey, as item 105 decided.

### 115: categorical as a type

A categorical is a numeric array of codes (1..n, NaN for `<undefined>`) wearing a
`JgsCategoricalTag` (`Categories`, `Ordinal`, `Protected`), the way a datetime wears its time tag:
reads go down the array roads, and `JgsCategorical` (names, codes, merge, remap, absorb, format,
header, compare) and `Interpreter.Categorical` (brackets, indexed writes, growth, deletion) do what
a name needs. `JgsBuiltins.Categorical` registers the constructor and the verbs, and wraps the
verbs that already existed (`sort`, `unique`, `ismember`, `min`, `max`, `median`, `mode`, the set
verbs, `string`, `cellstr`, `char`, `double`, the integer casts, `num2cell`, `jsonencode`,
`sprintf`, `fprintf`, `isobject`, `ismissing`, `fillmissing`, `rmmissing`, `standardizeMissing`,
`findgroups`, `discretize`, `histcounts`, `summary`, and the refusals of `strcat`, `logical`,
`cell`, `num2str`, `mat2str`, `sum`, `mean` and the arithmetic verbs); a wrapper keeps the inner
builtin's flags, so a wrapped `mat2str` still keeps its string arguments and a wrapped `sortrows`
its output counts. `if c`, `-c`, `~c`, `c:d` and `c{1}` are R2025b's refusals; `switch c` matches
by name. A categorical reaches a chart as its codes, a `uitable` as a table variable (edited by
name), `StyleConfigurations.Target` as a protected categorical with the component's categories,
and a `uidropdown`'s `Items` as its names.

### 116: object arrays past the first cut

`Interpreter.ObjectArrays` (rewritten) holds the roads: a bracket of objects and foreign pieces
(`AsMemberOf` converts through the constructor with R2025b's `UnableToConvert` words), handle
elements held as the objects themselves, `ObjectArrayMember` (a method over the array, `delete`
and `isvalid`, else the property list), `ObjectArrayFieldValues` for the spread, `AssignLinear`
and `AssignGrid` with `GapFiller`, `HandleArrayEquality`, the `empty` class member. The spread
reaches `[a.p]` over a variable, over what a call answered and over `Class.empty.p`; a 2-subscript
write goes to the grid road; `''` deletes as `[]` does. `JgsBuiltins.ObjectArrays` wraps the shape
verbs over objects (and, by the same wrapper, over categoricals) and orders handles by creation for
`<`, `>`, `sort`, `unique`, `ismember`, `setdiff`, `intersect` and `union` (`JgsObject.Serial`);
`findobj` filters a handle array by property, `getfield` reads a property, `num2cell` keeps a 2-D
shape, `structfun` answers a column in the MATLAB dialect, `arrayfun` lets a verb answer nothing
and refuses a non-scalar in R2025b's words. A write through a handle reached by any path
(`v.list(2).p = x`, `m(2, 2).p = x`) lands on the object. An array of objects is displayed as
`1×3 Val116 array with properties:` and shown inside a struct or property as `[1×3 Val116]`; the
lifetime tracker and the run result's snapshot read an array's class without asking it for one
object. An operator on a value object, `if obj`, `switch obj` and `obj:1` are R2025b's refusals in
the MATLAB dialect.

### 118: the axes font follows its box

`AxesFontRule` (Core) holds the bands; `AxesModel.ApplyAutoFont(width, height)` scales every
ruler's tick and label styles and the title pair by the ratio of the new target to the last
(`AutoFontScale`), skipped for a manual `FontSize`, a `uiaxes` or an axes in a `uifigure`; the
renderer calls it with each laid-out plot box and lays out again when it changed, and a property
read (`FontSize`, a title or label's, a legend's or colorbar's) applies it to the last or estimated
box first. `FontSizeMode = 'auto'` restores the built-in sizes and shrinks again. The legend and
the colorbar follow the axes font times `FurnitureFontFactor` (1 for this library's look, 0.9 for
a MATLAB-dialect axes) until their own `FontSize` is written (`FontSizeManual`); `SyncFurnitureFonts`
keeps them in step after a write too. A document saves the built-in sizes (`UndoAutoFont` before
the axes is mapped), so a loaded figure is not shrunk twice. The base sizes stay this build's (11
for the ruler, 13 for a label, 15 for a title) where R2025b's are 10, 11 and 11: the bands are
ratios, and the fixture records them as ratios.

### 119: the caps refitted

`TickRule`'s cap tables are the refitted ones, `CountWithin` (tolerant) and `CountExactly` are two
readings of a step, `ChooseStep` tries the exact reading when the tolerant one exceeds the cap and
falls back to the widest pair, `PolarRadius` caps the r ruler at 7 and 5, `ColorbarRenderer`
budgets a colorbar by its own length and 0.9 of the font (`ColorbarBudget` for the property read),
and `JgsRulerTicks` answers the theta ruler's ticks by the 30/15/5-degree rule.

## Divergences

- **A patch's default ink is 0.251 grey where R2025b's is `[0.1294 0.1294 0.1294]`**: `CData`,
  `FaceVertexCData` and the face colour of a patch given no colour read this build's ink (item 105's
  decision; a patch whose axes came as `'Parent'` is filled from the colour order, as R2025b's is),
  and a text's default `Color` reads `'none'`.
- **A column write of `XData` onto a two-face patch keeps the vertices it uses**: R2025b keeps all
  eight of the old vertex list after `p.XData = [5; 6; 7; 8]`; this build keeps the four the one
  face names.
- **Colours written per vertex as RGB rows draw in the first vertex's colour**; R2025b interpolates
  them across the face. They read back as written.
- **The axes font base is 11 points, R2025b's 10** (ADR 0231): the automatic bands are the same
  ratios of it, so an auto-sized axes here is 1.1 times R2025b's at every size, and a `uiaxes`
  keeps 11 where R2025b keeps 12. A title is 15/11 of the axes font and a label 13/11, R2025b's
  are 1.1 of it; both shrink with it.
- **A subplot's plot box is a few pixels narrower than R2025b's**, so `subplot(3, 3, 5)` of a
  560-by-420 figure reads 8 points here (a 118-pixel box) and 8.5 there (134 pixels).
- **Rulers under about 200 pixels still take a tick more or fewer than R2025b** (item 119, still
  open): `[0 5]` on a 180-pixel x ruler is `0 1 … 5` there and `0 5` here, `[0 6]` on 60 pixels is
  `0 5` there and `0 2 4 6` here, `[0 10]` on a 186-pixel ruler at 9 points is `0 5 10` there and
  `0 2 … 10` here. R2025b scores density where this build caps a count; the refit reaches 86 to 95
  per cent of the short rulers.
- **The polar r ruler takes R2025b's count on 83 per cent of ranges**: `[0 12]` is `0 5 10` there
  and `0 2 … 12` here, `[-5 5]` is `-4 -2 0 2 4` there and `-5 0 5` here.
- **A categorical goes to a chart as its codes**: `bar`, `plot`, `scatter`, `histogram`, `pie`,
  `boxchart`, `xline` and `xregion` over a categorical draw its codes on a numeric ruler, and
  `XData`, `XTick` and `XLim` read numbers where R2025b's `CategoricalRuler` answers categoricals
  and `histogram` counts categories.
- **`sort`, `unique`, `setdiff` and `<` order handle objects by creation**; R2025b orders them by
  an address it does not publish, so `sort([Hnd116(3) Hnd116(1) Hnd116(2)])` lists them in a
  different order there (`[3 2 1]` by their `p`) and here (`[3 1 2]`).
- **A value object's refusals keep this build's words where R2025b's name an internal**:
  `double(obj)`, `char(obj)`, `cell(obj)`, `num2str(obj)`, `mat2str(obj)`, `sprintf('%d', obj)`,
  `sum`, `max`, `any`, `cumsum`, `mean`, `nnz` over an object array, `setfield`, `rmfield` and
  `orderfields` of an object, `arrayfun`'s uniform refusal, `a.p(end)` over a list, `deal`'s count
  mismatch and `cat(3, …)`; `class(a.p)` is `maxrhs` here and `invalidClassContext` there; a
  constructor that reads an argument it was not given says `Unrecognized function or variable`
  here and `Not enough input arguments` there; `metaclass(a)` is a `meta.class`, R2025b's a
  `matlab.metadata.Class`.
- **An N-D array of objects is not built**: `cat(3, a, b)` and `a(:, :, 2)` are refused, R2025b
  answers a 1-by-3-by-2 array.
- **A value object array held in a property is not written through**: `o.list(2).p = 9` is
  refused where R2025b writes it (a handle array in a property is written through).
- **`a.total` on an array answers the method, not its call**, and `obj_cs(a(1:2).p)` indexes with
  the first of the list; R2025b calls the method and spreads the list into the subscripts.
- **A struct made of an object array, `[s1 struct('p', 1)]` and `[s1 string.empty]`**: R2025b
  converts the struct through the constructor and makes an element of the empty string; this build
  refuses the first and the second.
- **The constructor call count differs**: a gap filled by the no-argument constructor costs this
  build one or two calls more or fewer than R2025b over a long script (106 there, 108 here).
- **A categorical's refusals keep this build's words**: `times`, `regexp`, `regexprep`, `strjoin`,
  `strsplit`, `strtrim`, `cellfun`, `str2num`, `datestr`, `fieldnames`, `properties`, `methods`,
  `c.a`, `strrep` (R2025b warns), `categorical(["a" missing], …)` (`EmptyCategoryName` there,
  `CharData` here), and a table's braces over mixed variables.
- **`arrayfun` over a categorical is refused**; R2025b applies the function to each element and
  answers a double or a categorical.
- **`save` and `load` of a categorical round-trip its codes as a double**; R2025b keeps the type.
- **The display of a categorical is this build's**: a long row is not wrapped into `Columns 1
  through N`, a 3-D one is shown flat, `summary` prints the category names in plain text where
  R2025b bolds them, and a categorical inside a cell, a struct or a table is shown by this build's
  cell, struct and table display.
- **A table has no `vartype`, `convertvars`, `table2cell`, `join`, `innerjoin` or `stack`**, and
  `array2table` of a categorical, `table2struct`, `groupsummary` and `grp2idx` answer a double, a
  char, a cell and a group number where R2025b keeps the categorical.
- **`isenum` and `istabular` are not registered.**
- **A categorical answers `isequal(c, string(c))` false, as R2025b does, but `isequal(c, cellstr(c))` true**: both as measured.

## Consequences

- Patch property shapes change in both dialects: a script reading `p.XData` gets a column, and
  `[h(1).XData; h(3).XData]` of two pie slices stacks as R2025b's does. Two tests expected the old
  rows and a flat wedge's three-column vertices; stress 51's slice now takes a `Faces` write, as
  R2025b's does, and stress 50 reads its `FaceVertexCData` as a column.
- Every MATLAB-dialect figure with a small axes changes: the ruler font shrinks with the box, the
  legend and colorbar follow at nine tenths, and the tick count follows the font. The ticks the
  batch-18 fixture recorded for a subplot are now a recorded divergence (R2025b's density score).
- `categorical` answers a categorical, not a cell: `class(categorical(…))` is `'categorical'`, `summary`
  answers R2025b's struct (one test read `s.high` and now reads `s.Counts(1)`), and
  `StyleConfigurations.Target`'s three recorded divergences of ADR 0230 agree now and are exact.
- An interpolated patch face carries its vertex transparency (it did not, because per-vertex
  readings never made a face `'interp'` before).
- A saved document holds an axes' built-in font sizes; one saved before this batch holds them too.
- Fixtures `oi_patch_rows_axes_font_short_rulers` (578 lines, 62 divergences), `oi_object_arrays`
  (614 lines, 57 divergences) and `oi_categorical` (746 lines, 100 divergences); the helper classes
  `Val116`, `Hnd116`, `Sub116`, `NoArg116` and `Zero116` live in `fixtures/helpers`. The builtin
  catalog lists the thirteen categorical verbs and dispatch sweep 15 measured them (every one an
  M-file with a `categorical` class method); `dispatch-class-map.csv` maps the categorical class.
- Full suite 11,030 of 11,030; stress 89 of 89.

## Still open

- Item 119 keeps its residual: R2025b's density score under about 200 pixels and on the polar r
  ruler, filed again with the measurements.
- A `CategoricalRuler` for charts over a categorical, N-D object arrays, writes through a value
  object array held in a property, `a.total` as a call, the table verbs a categorical needs
  (`vartype`, `convertvars`, `table2cell`, `join`, `stack`), `save`/`load` of a categorical,
  `arrayfun` over a categorical, R2025b's identifiers for the value-object refusals, `horzcat`
  and `vertcat` as verbs over cells and char rows, and `isenum`/`istabular`: filed as items 120
  to 126.
- Item 90 (the `lu` timing) runs after this batch, on a quiet machine.
