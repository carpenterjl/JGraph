# ADR 0234 — Turned labels on a crowded ruler, the categorical classes' surface, repmat past two counts, writes through any value-object array, the table leftovers, handle arrays under set, get and addlistener, and OnOffSwitchState: open items 126 to 129

## Status

Accepted, 2026-10-14. The twenty-first batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), four items wide, all filed by batch 20 (ADR 0233) from the divergences its fixtures
recorded. Every answer was recorded from R2025b headless: probes `probe_126`, `probe_126b` and
`probe_126c` (the categorical classes and the label angle), `m126_rot` to `m126_rot5` (the turning
rule, bisected), `probe_127` (`repmat` and the writes), `probe_128` to `probe_128d` (the table
leftovers) and `probe_129`, `probe_129b` (handle arrays, categorical products, OnOffSwitchState) in
the open-items scratch, then the fixtures `oi_crowded_ruler_mixed_axis`, `oi_repmat_object_writes`,
`oi_table_missing_save_summary` and `oi_setget_arrays_onoff`. Everything lands in the MATLAB dialect;
three changes to how a table holds its data reach JGS too (Consequences).

## Context

- **126.** A crowded categorical ruler kept its labels level where R2025b turns them (thirty
  categories on a default axes read `XTickLabelRotation` 90); `axis` over a categorical and a numeric
  ruler answered numbers where R2025b refuses (`MATLAB:axis:Mixed2D`); `loglog` over a categorical
  drew where R2025b refuses (`MATLAB:PolarAxes:LogScale`); `get` of a `CategoricalRuler` and of a
  categorical `Histogram` listed the numeric classes' properties, and `histogram(c, 'BinMethod', …)`
  was taken where R2025b refuses the name.
- **127.** `repmat` took two counts at most; a value-object array reached through a struct field
  (`st.list(2).p = 9`), a cell (`c{1}(2).p = 9`) or an element of another array
  (`a(2).name(1).p = 0`) was not written through.
- **128.** A `vartype` displayed as a struct; a table indexed by a `vartype` that picks nothing lost
  its rows; `T.(vartype(…))` was this build's dynamic-field refusal; `convertvars` to `'char'` did not
  warn; a table could not be saved, and `whos('-file', …)` was not here; `ismissing`, `anymissing`,
  `fillmissing`, `rmmissing` and `standardizeMissing` refused a table; `summary(T)`'s struct lacked
  most of R2025b's fields.
- **129.** `set`, `get` and `addlistener` over an array of handles answered
  `ObjectArrayNotSupported`; the product of two categoricals, `struct(c)` and `isspace(c)` were this
  build's; a row of function handles was refused in this build's words; `matlab.lang.OnOffSwitchState`
  was not a name.

## Decision

### 126: the turned labels and the categorical classes' surface

**The turning rule**, as measured: a horizontal ruler's labels turn by themselves (`TickLabelRotationMode`
`'auto'`, a MATLAB-dialect axes' default) to 0, 30, 45 or 90 degrees by the room each label has —
the ruler's whole pixels (each end snapped to its pixel: `round(0.905 W) - round(0.13 W)` for a
default axes) shared among the ticks it shows, not the distance between them. The labels stay level
while the room holds the widest label, rounded up to a pixel, and a gap of nine sixteenths of the
font's pixel size, rounded up; past that they turn to 30 degrees while the room is at least two
label heights, to 45 while it is at least one, and to 90 beyond. A label's width is R2025b's text
extent (Helvetica, which is Arial here, with pair kerning); its height is the face's ascent and descent
at the size, each rounded to a pixel (9, 12, 15, 17, 21, 30 at 6, 8, 10, 12, 14, 20 points). Room exactly
at a threshold keeps the smaller angle, except along a categorical ruler of 8 to 31 categories, where
R2025b takes the larger one (measured for every count from 2 to 40 and at 48, independent of the
figure; not understood). A vertical ruler never turns. `TickLabelTurn` holds the rule; `JgsRulerTicks` answers a
script with it over the plot box a script sees, and the renderer draws by it over the plot box it
lays out (`AxisModel.TickLabelAngleAuto`, `ChosenTickLabelAngle`, `DrawnTickLabelAngle`; the mode is
saved with the document). Writing `XTickLabelRotation` makes the mode manual; writing the mode
`'manual'` keeps the angle showing.

**The classes' surface.** `axis` over mixed rulers is `MATLAB:axis:Mixed2D` or `Mixed3D` (three outputs
too, a wrapper that takes the output count); `loglog`, `semilogx` and their kind over a categorical
refuse a log scale after the chart is made, as R2025b does; `fill` and `patch` take categorical x;
`text` and its kind refuse a categorical where R2025b does. The `CategoricalRuler` and the categorical
`Histogram` are classes of their own in the generated R2025b tables (`graphics_class_names.m`,
`graphics_class_members.m` recorded them; only their rows were merged in), so `get`, `set`, a dot
and the listings answer their own properties, and a name that belongs to the other class of the
pair is refused with R2025b's identifier and words (`MATLAB:class:setgetPropertyNotFound`,
`MATLAB:class:InvalidProperty`; a numeric histogram's `BarWidth` is its own). A categorical
histogram refuses the numeric one's name-value pairs; its counts form keeps `BinCounts` manual; a
ruler's `HandleVisibility` is `'off'`.

### 127: repmat and the writes through any value-object array

`repmat` in MATLAB code takes any number of counts, as a row or as scalars, and tiles by one gather
over positions, so every kind of array (numbers, characters, cells, strings, structs; objects and
categoricals through their stand-ins) gets a third dimension and more; R2025b's refusals come with
it (`invalidReplications`, `nonNumericReplications`, `NdSparseOutput`, a table's `NDSubscript`, a
function handle's `err_non_scalar_function_handles`), and a struct tiles into a struct array.
A value-object array reached through a struct field, a cell or an element of another array is
written through as one in a property is: the element is read out (or made by the constructor past
the end), the rest of the path is written into it, and it goes back by the paren road, which grows
the array as R2025b does; several subscripts go back as the one position they named. A struct array
takes three subscripts or more to read and write through a stand-in of its positions; one that
reaches past the array still grows it.

### 128: the table leftovers

`JgsBuiltins.TableLeftovers` teaches the verbs a table. `vartype` displays as R2025b's subscript
text, answers `properties` (none), `methods`, `isa` and `ismethod` as its class does, and through a
dot is `MATLAB:table:IllegalVarSubscript`. A table keeps its rows when it has no variables
(`Table(columns, emptyRowCount)`): `T(:, vartype('datetime'))` is 3-by-0, a variable added to it takes
its height, and a table of no variables displays and joins as R2025b's. `T.Variables` reads and
writes the variables as one array; `table('Size', …, 'VariableTypes', …)` preallocates with R2025b's
defaults, refusals and char warning; `convertvars` to `'char'` warns. `ismissing` (with indicators),
`anymissing`, `fillmissing` (methods, constants checked per variable, `DataVariables`,
`ReplaceValues` naming `name_filled`, the second output), `rmmissing` (`MinNumMissing`,
`DataVariables`, the second output) and `standardizeMissing` work over a table, variable by variable
(a categorical's undefined is missing; a `'nearest'` tie fills from after, as R2025b's does over a
vector too).
`summary(T)` answers R2025b's struct, one field a variable, and prints its display when nothing is
asked. A table, a timetable, and the strings, datetimes and durations inside them save to a
MAT-file as object elements and load back equal (`isequal` of two tables compares names, order,
values, row names and row times). `whos` in the MATLAB dialect answers R2025b's struct (`nesting`
names the function listing its workspace) and prints its listing, of the workspace or of a file
(`-file`, wildcards, `-regexp`), with its byte rules: a cell element costs 120 bytes beside its
own, a struct 16 with 8 and its name's length a field and 120 beside each value, and an object's
size is `-`.

### 129: handle arrays, categorical products, function-handle rows and OnOffSwitchState

`set` and `get` take an array of `matlab.mixin.SetGet` objects (each name matched by any unambiguous
beginning, the cell form one row an element, a column of answers back); over an array of a plain
handle class they are R2025b's `SetMethodUnknown`/`GetMethodUnknown`. `addlistener`, `listener` and
`notify` take an array of handles: a listener an element, answered as a column whose `delete`,
`isvalid` and dots reach each one; a property that is not `SetObservable` is
`MATLAB:class:nonSetObservableProp`. The product of two categoricals is R2025b's (`times.m`: the
combined names), `struct(c)` its storage with the `structOnObject` warning, `isspace`, `isletter` and
`isstrprop` of a non-text value one false; a row of function handles is
`MATLAB:err_non_scalar_function_handles`. `matlab.lang.OnOffSwitchState` is a value class here
(`OnOffSwitchStateValue`): its members by name, `enumeration`, its conversions, comparisons,
`sort`, `~` and the logical verbs it inherits, its displays (the class name a bold link to its
help) and arrays; `isenum` of a member is true. `size` with a dimension that is not a number is
R2025b's `MATLAB:size:invalidDim` for one answer or several (a MAT-file's `size(m, 'v')` names a
variable).

## Divergences

- **A graphics handle's class is `double`** (as before): `class` and `superclasses` of a
  `CategoricalRuler` or a categorical `Histogram` answer `double` and nothing, here as in ADR 0233.
- **An axes' and a ruler's colour is 0.251 grey** where R2025b's is `[0.1294 0.1294 0.1294]`, the
  ink kept in both dialects by decision (ADR 0230), as a patch's is (ADR 0232).
- **A property listing prints a graphics `on`/`off` as text**: `get` of a ruler or a histogram, and
  `ax.Visible` and its kind, answer a char `'on'`/`'off'` where R2025b answers an `OnOffSwitchState`
  (so `isenum(ax.Visible)` is false here), and the listing's values print in this build's form.
- **A ruler answers `LimitMethod`**, a JGraph extra R2025b refuses (kept, as decided 2026-10-09).
- **`axis padded` chooses this build's padded limits** (ADRs 0230, 0231), over categorical rulers too.
- **The plot box a script reads before the first frame ignores turned labels**: R2025b raises the
  inner bottom to the turned labels' extent (`w|sin| + h|cos| + 5.33` pixels below the ruler) when
  the default margin is too small; this build's estimate keeps the default box.
- **The turning rule needs a pixel less room per tick in three measured cases**: four-digit labels
  at 6 and 9 points, and twelve-digit labels at 10 points (78 of 81 measured thresholds agree to the
  pixel; the rest of this build's label widths are linear where R2025b's may round per glyph).
- **A table with variables displays as `table[RxC]`, and `summary`'s statistics as this build's
  lines** (as before).
- **`string` and `char` of a duration keep every digit**: `1.4142135624 sec` where R2025b writes
  `1.4142 sec` (a summary's `Std` shows it).
- **`whos` counts an object's bytes as this build stores it**: a table, a categorical or a string in
  a workspace or a file is not R2025b's internal size (393 against 1988 bytes for the fixture's
  table).
- **`save` refuses `-v7.3`, and a bare `datetime`, `string` or `duration`**; inside a table they save.
- **A logical sparse array is a double one, and a complex one is refused** (ADRs 0167, 0213), so
  `whos` of them differs.
- **`T.(n)` with a number is refused**; R2025b reads and writes the n-th variable.
- **`get` and `set` with no output display a struct as this build does**, as do a struct and a cell
  holding an `OnOffSwitchState`.
- **`methods` of an `OnOffSwitchState` lists its own** where R2025b lists all 153 it inherits.
- **`exist` answers 5 for the table verbs** (ADR 0149), where R2025b answers 2 for its M-files and 0
  for `convertvars`, `innerjoin` and `stack`, which it keeps only as table methods.

## Consequences

- A MATLAB-dialect figure with a crowded horizontal ruler draws its labels turned, and the saved
  document keeps whether a ruler turns its own (`TickLabelAngleAuto`); a JGS axes is unchanged (its
  labels turn only when written).
- Three changes reach JGS, because they are how a table holds its data: a table of no variables
  keeps its row count; a char matrix and a nested table are held as they are in a variable; two
  tables are `isequal` when their variable names, values, row names and row times are.
- `size` refuses a char, string or cell dimension in MATLAB code for one answer or several.
- The batch 20 fixtures regenerate with the rows this batch fixed made exact: `oi_categorical_ruler`
  15 divergences to 6, `oi_object_arrays_nd` 4 to none, `oi_object_arrays` 13 to 11, `oi_table_verbs`
  5 to none, `oi_categorical` 32 to 29, `oi_refusals_bracket_verbs` 7 to 1.
- New fixtures `oi_crowded_ruler_mixed_axis` (593 lines, 18 divergences), `oi_repmat_object_writes`
  (178, none), `oi_table_missing_save_summary` (840, 19) and `oi_setget_arrays_onoff` (427, 23); the
  probe classes `SG129` and `Obs129`, and `Live129` (a handle with a destructor and nothing else),
  join the fixture helpers. `OpenItemsBatch21Tests` holds the turning rule's arithmetic, the drawn
  angle and the saved mode.
- The gates caught five regressions of the batch before it landed: the table verbs' wrapper asked
  `methods` and `properties` for one answer when none was wanted, so their listings came back as
  `ans`; a struct's growth by two subscripts met the new N-D read first; `size(m, 'v')` of a MAT-file
  met the new dimension check; a histogram of a cellstr's names (a JGraph extra) was taken for a
  numeric one and refused `DisplayOrder`; and a column of listeners made by `listener` was ended at
  its statement's end whenever an object with a destructor was alive anywhere, because each
  element's own struct, which nothing holds, was counted as a handle (`JgsListener.Grouped`: only
  the column ends them; fixture row `listener_arr_live`).
- `categorical` of many distinct values was quadratic: seventy thousand took twenty seconds, and
  take six hundredths now (each value's place is looked up, not searched for).
- A date picker test printed `char(string(e.Value))`, which R2025b refuses for `NaT` now that
  `string(NaT)` is a missing string; it prints `char(e.Value)`.
- Full suite 11,042 of 11,042; stress 89 of 89. `stess_38` asserted the `'nearest'` tie the other
  way, and now holds R2025b's; it also holds two older fillmissing answers R2025b gives otherwise (a
  string array's `""` filled as missing, `'linear'` holding the end value), filed as item 135.

## Still open

- The leftovers above that are not decided, filed as items 130 to 134: `T.(n)`; saving a bare
  string, datetime or duration and `-v7.3`; a graphics `on`/`off` as `OnOffSwitchState`; the plot
  box under turned labels before the first frame; a duration's digits. Item 135: fillmissing over a
  string array and at the ends of `'linear'`.
- Item 90 (the `lu` timing) still waits for a quiet machine.
