# ADR 0233 — A categorical ruler, object arrays past two dimensions, the table verbs a categorical needs, R2025b's refusals, horzcat and vertcat as the bracket, and isenum and istabular: open items 120 to 125

## Status

Accepted, 2026-10-13. The twentieth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), six items wide, all filed by batch 19 (ADR 0232) from the divergences its fixtures
recorded. Every answer was recorded from R2025b headless: probes `probe_120` (the categorical ruler,
with two exported frames to measure the padding), `probe_121` (object arrays), `probe_122` (the table
verbs) and `probe_123` (refusals, `horzcat`/`vertcat`, `isenum`/`istabular`) in the open-items scratch,
then the fixtures `oi_categorical_ruler`, `oi_object_arrays_nd`, `oi_table_verbs` and
`oi_refusals_bracket_verbs`. All six land in the MATLAB dialect only; JGS keeps what it had.

## Context

- **120.** A categorical reached a chart as its codes: `bar`, `plot`, `scatter` and their kind drew
  codes on a numeric ruler, and `XData`, `XTick`, `XLim` and `XTickLabel` answered numbers. R2025b
  puts a `CategoricalRuler` under them: category `k` stands at position `k`; the chart's data, the
  limits and the ticks read back as categoricals (`XLim` the first and last category in view,
  `XTick` every category, `XTickLabel` a column of names); limits and ticks are written as
  categories (`MATLAB:graphics:CategoricalRuler:Limits`/`:Ticks` for a number or an unknown name);
  the automatic limits take in every category and the data, half a category beyond each end (three
  bars of width 0.8 stand in `[0.1 3.9]`, two points in `[0.5 2.5]`, measured on exported frames);
  a held axes adds new categories at the end; a categorical onto a numeric ruler that holds data is
  `MATLAB:graphics:configureAxes:Mixing`, while numbers onto a categorical ruler are positions;
  `ax.XAxis.Categories` reorders the ruler and every chart moves with its names; `ruler2num` and
  `num2ruler` convert; `histogram` of a categorical counts its categories (unused ones included,
  the undefined left out, `BarWidth` 0.9, `Data` the categorical, no `BinEdges`); `pie` of one
  labels each slice `name (pct%)`; `bar(["a" "b"], y)` makes a categorical of the names; a heatmap's
  labels and data are columns; `bar` over a matrix answers a row of bars.
- **121.** Object arrays were 2-D only: `cat(3, a, b)` folded to 1-by-6, `t(1, 2, 2)` was refused;
  `o.list(2).p = 9` was refused with `o.list` a value-object array; `a.total` on an array answered a
  bound function; `v(a(1:2).p)` indexed with the first of the list; `[s1 struct('p', 1)]` and
  `[s1 string.empty]` were refused; `cell2mat({a; a})` was refused.
- **122.** `vartype`, `convertvars`, `table2cell`, `join`, `innerjoin` and `stack` were missing;
  `array2table` and `table2struct` turned a categorical into a double or a char, `groupsummary`'s
  key into a cell, `sortrows(T, {'kind', 'n'})` was refused, `arrayfun` walked a categorical's codes,
  and `save`/`load` kept only the codes.
- **123.** Refusals kept this build's words: `double(obj)`, `sum(a)`, `num2str(obj)`,
  `sprintf('%d', obj)` and their kind over a value object, `a.p(end)`, `class(a.p)`, a constructor
  reading an argument it was not given, and a categorical in `.*`, `cumsum`, `round`, `regexp`,
  `strjoin`, `strsplit`, `strtrim`, `cellfun`, `str2num`, `datestr`, `fieldnames` or a dot.
- **124.** `horzcat({1}, {2})` and `vertcat('x', 'x')` were refused where `[{1} {2}]` and
  `['x'; 'x']` work.
- **125.** `isenum` and `istabular` were missing.

## Decision

### 120: the categorical ruler

`AxisModel.RulerCategories` (saved with the document) marks a ruler categorical: its ticks are the
categories at their positions (`CategoricalRulerTickGenerator`, every one shown; a tick set by
position takes its category's name), and `AxesModel.FitRange` gives it the union of `[1, n]` and the
data, padded by half a category. `JgsCategoricalCharts` wraps the chart verbs (`bar`, `barh`,
`plot`, `line`, `scatter`, `stem`, `area`, `stairs`, `errorbar`, `swarmchart`, `boxchart`, `plot3`,
`semilogy`, `text`, `xline`, `xregion`, `yline`, `yregion`): a categorical argument becomes positions
on the ruler it stands along (the ruler's categories when held, the argument's otherwise; `Mixing`
when a held numeric ruler holds data), the ruler takes the categories after the call, and the chart
remembers each categorical property's categories. `JgsGraphicsProperties.Get`/`Set` ask it first:
a chart's `XData`/`YData`/`Value` read back through the ruler's names; an axes' `XLim`, `XTick` and
`XTickLabel` and a ruler's `Categories`, `Limits` and `TickValues` read and write as categories;
`xlim`, `xticks`, `xticklabels`, `ruler2num` and `num2ruler` take the same road. `histogram` of a
categorical (or `'Categories'` with `'BinCounts'`) counts by name and stands on a categorical ruler
of the categories it shows (`HistogramPlot.ShowCategories` keeps the ruler in step when the order
or the count shown changes), with `OthersValue`, `Categories` without `Others`, and R2025b's refusal
of the numeric histogram's properties through a dot. `pie` and `pie3` of a categorical, `histcounts`
of named categories, a heatmap's column labels, `bar(text, y)`, and a row of bars come with it; a
plot that replaces another, and `cla reset`, make the ruler numeric again.

### 121: object arrays past two dimensions

An object array carries an N-D shape the way a numeric array does (`ReshapeDims` over its
elements). Every verb that rearranges objects does it over a numeric stand-in of their positions
(`JgsObjectArray.PositionsLike`/`ValueLike`), so `cat(3, …)`, `reshape`, `permute`, `squeeze`,
`flip`, `t(:, :, 2)`, `t(1, 2, 2)`, `t(5)`, three-subscript writes, growth and page deletion
(`AssignByStandIn`), `t(1, 1, 2).p = 7`, `[t3 t3]` and `[t3; t3]` (`CatAlong`) are exactly the
numeric road's; `size`, `ndims`, `num2cell`, `isvalid`, `==` and the display keep the shape. A
property holding a value-object array is a computed level, so `o.list(2).p = 9`, `o.list(end).p`
and `o.list(4).p = 4` write through get-modify-set; a method a dot names on an array is called over
it (`a.total`, `ha.bump;`); a list in a subscript spreads (`v(a(1:2).p)`, `c{a(1:2).p}`); a struct
beside an object goes through the constructor (objects are asked before structs in a bracket),
`struct([])` joins as nothing and `string.empty` is converted; `string.empty`, `cell.empty` and a
bare `double.empty` are what R2025b answers; `cell2mat` of objects joins their grid by the bracket.

### 122: the table verbs a categorical needs

`JgsBuiltins.TableVerbs`: `vartype` is a subscript (a struct of class `vartype`) that a table's
`()`, `{}` and writes resolve to the variables of its type (`numeric`, `float`, `integer`,
`cellstr`, or a class name); `convertvars` converts named, numbered, masked, `vartype`-picked or
predicate-picked variables by a class's name or a function (`'char'` becomes a cell of char rows,
R2025b's `InvalidTextType` and `VariableTypesConversionFailed`); `table2cell`; `join` (left order,
each key matched once, `LeftKeyValueNotFound`, `DuplicateRightKeyVarValues`, `CantInferKey`,
`KeepOneCopy`, `_left`/`_right` for a clash, the right rows as a second output) and `innerjoin`
(every pair, sorted by key — a categorical by its categories — then left row, then right row;
`LeftVariables`, `RightVariables`, `ileft` and `iright`); `stack` (the other variables repeated, a
categorical indicator, the values interleaved through the bracket, `NewDataVariableName`,
`IndexVariableName`, `ConstantVariables`, `InterleavingDataVarsFailed` for unlike kinds).
`array2table` and `table2struct` (with `ToScalar`) keep a categorical one; `groupsummary` groups by
a categorical in category order with the undefined last and answers it a categorical, in both its
table and vector forms; `sortrows` of a table takes several keys, a direction each, numbered
variables (negative descending) and a categorical in category order. `arrayfun` walks a categorical
element by element, and a uniform `arrayfun` or `cellfun` collects characters into a char, string
scalars into a string array, one-element arrays into numbers and categorical elements into a
categorical. A categorical is saved as an object element of class `categorical` (codes, category
names, the two flags) and loads back as one.

### 123: R2025b's refusals

`JgsBuiltins.R2025bRefusals` wraps, over every other wrapper, the verbs whose refusal R2025b words
its own way when the first argument is an object of a script's class (the conversions,
`num2str`, `mat2str`, `int2str`, the reductions, `abs`, `round`, `sqrt`, `norm`, `find`,
`median`, `mode`, `rmfield`, `orderfields`) or a categorical handed to a text verb; `sprintf` and
`fprintf` name the type their first conversion wanted; `setfield` writes an object's property;
`isgraphics` of an object array is false each; `cellfun` over a categorical, `sort` of a cell that
holds more than names, `class` with a list, `max(a, b, dim)`. A parameter the caller did not pass
is `MATLAB:minrhs` when read; `a.p(end)` is `expected_one_output_from_reference`; a write past an
object array's elements is `singleSubscriptNumelMismatch`; `metaclass` answers
`matlab.metadata.Class`; a categorical's `.*`, `cumsum`, `round`, dot read and dot write, a string
value set holding a missing, and a table's braces over mixed variables are R2025b's; `strrep` over
a categorical warns and hands it back.

### 124 and 125

`horzcat`, `vertcat` and `cat(1|2, …)` in MATLAB code build what the bracket builds
(`Interpreter.JoinAsBracket`), and the bracket learnt two of R2025b's rules on the way: a non-cell
beside a cell is one element of it (`[{1} 2]` is `{1, 2}`), and a number stacked with char rows is
the character it codes (`['a'; 66]`); concatenation along any dimension checks every dimension but
the joined one. `istabular` (a table or a timetable; a file function's `TooManyInputs`) and `isenum`
(a member of an enumeration: the device classes' members) are registered, and `table()` is the 0-by-0
table.

## Divergences

- **A graphics handle's class is `double`, not R2025b's class name** (as before): `class(ax.XAxis)`
  is not `matlab.graphics.axis.decorator.CategoricalRuler`, nor a histogram's
  `…categorical.Histogram`; `get` lists a categorical ruler's and a categorical histogram's
  properties as the numeric ones'.
- **A crowded categorical ruler keeps its labels level**: R2025b turns thirty categories' labels to
  90 degrees on a default axes; this build leaves `XTickLabelRotation` at 0.
- **`axis` over a categorical and a numeric ruler answers numbers**: R2025b refuses with
  `MATLAB:axis:Mixed2D`; `loglog` over a categorical draws where R2025b refuses.
- **`repmat` of an object with three counts is refused**, as `repmat` of numbers with three counts
  is; R2025b builds a 1-by-2-by-2 array.
- **A value-object array reached through a struct field, a cell or an element of another array is
  not written through** (`st.list(2).p = 9`, `c{1}(2).p = 9`, `a(2).name(1).p = 0`); one held in
  an object's property is.
- **A `vartype` displays as a struct**, a table indexed by a `vartype` that picks nothing has no
  rows left, `T.(vartype(…))` is this build's dynamic-field refusal, `convertvars` to `'char'` does
  not warn, a table cannot be saved to a MAT-file, and `whos('-file', …)` is not here.
- **`set`, `get` and `addlistener` over an array of handles** keep this build's
  `ObjectArrayNotSupported`; R2025b answers `SetMethodUnknown` and `nonSetObservableProp`.
- **The product of two categoricals, `struct` of one and `isspace` of one** are this build's refusal
  and answers; **a row of function handles** is this build's refusal in its own words; **an
  enumeration's member** (`matlab.lang.OnOffSwitchState.on`) is not a name here; `exist('istabular')`
  is 5, a builtin, where R2025b's is a file.

## Consequences

- A MATLAB-dialect chart over a categorical changes throughout: its ruler, limits and ticks, the
  data a script reads back, and the saved document (which keeps `RulerCategories`).
- `bar` and `barh` over a matrix answer a row of bars in the MATLAB dialect.
- `horzcat` and `vertcat` in MATLAB code are the bracket: their numeric class follows the bracket's
  rule (`horzcat(true, 2)` is a double), and a mismatch is `MATLAB:catenate:dimensionMismatch`.
- A heatmap's `XDisplayLabels`, `YDisplayLabels`, `XDisplayData`, `YDisplayData`, `XData` and
  `YData` read as columns in the MATLAB dialect.
- The fixtures `oi_object_arrays` and `oi_categorical` of ADR 0232 regenerate with the rows this
  batch fixed made exact: 13 divergences where there were 57, and 32 where there were 100. `h_lt`
  (two handles compared by `<`) is dropped, as batch 19 dropped its siblings: R2025b orders handles
  by an address it does not publish, and answered it the other way this time.
- New fixtures `oi_categorical_ruler` (211 lines, 15 divergences), `oi_object_arrays_nd` (156, 4),
  `oi_table_verbs` (201, 5) and `oi_refusals_bracket_verbs` (152, 7). The catalog lists `vartype`,
  `convertvars`, `table2cell`, `innerjoin`, `stack`, `isenum` and `istabular`; dispatch sweep 16
  measured them (`isenum` a built-in, the rest M-files with `tabular` methods where they have any),
  `dispatch-class-map.csv` maps `vartype`, and the coverage counts are 1,230 of 2,024 and 347 of 377.
- Two tests and a stress script asserted what R2025b refuses: `max(4, 9, 2)` (a forwarding wrapper's
  three arguments; R2025b's `MATLAB:max:caseNotSupported`) now forwards `max([4 9], [], 2)`, and the
  table-braces test reads R2025b's identifier. The ownership gate lists the stand-in slot an object
  array's write is made in.
- Full suite 11,034 of 11,034; stress 89 of 89.

## Still open

- The leftovers above, filed as items 126 to 129.
- Item 90 (the `lu` timing) still waits for a quiet machine.
