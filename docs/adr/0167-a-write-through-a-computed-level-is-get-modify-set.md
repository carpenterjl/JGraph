# ADR 0167 — A write through a computed level is get, modify, set

## Status

Accepted. Stage V6 of the value-ownership plan (`docs/plans/zfit-copies-and-temporaries-plan.md`),
after V5's persistent binding (ADR 0166). It landed in eighteen commits, one a sub-stage, each with
its own fixture recorded from R2025b: bed7cd2 (V6.1, table rebuilds keep their metadata), bf1fceb
(V6.2, table brace assignment), a271441 (V6.3, sparse indexed assignment), 4618e66 (V6.4, every
rebuild keeps what the value is), 4199ae6 (V6.5, writes through a container path), 52c48e7 (V6.6,
exception fidelity), 4dee7df (V6.7, ErrorHandler records and `mat2str` of a negative zero), f53ec3b
(V6.8, comma-list assignment to a struct array's field), 3d5ce34 (V6.9, composite writes store
back), dfb8f3c (V6.10, `guidata`, `axes(parent)`, `isvalid`), dbed03e (V6.11, timers), bba83d4
(V6.12, events and listeners), ae6a89a (V6.13, save and load), 41547f0 (V6.14, table forms), c7bdd51
(V6.15, datetime and duration forms), 73048a1 (V6.16, property accessors and `Dependent`), c33c26b
(V6.17, a `for` over text, `d(key) = []`, a Map's Count), and this ADR's commit (V6.18: the six
remaining lines stamped as the accepted divergences below, all four lanes whole, the allocation
regression).

## Context

V0's probes found, beside the isolation defects V1–V5 fixed, places where R2025b works and this
build refused or — worse — did nothing and said nothing (appendix A #25–#30, and the rows the probe
sweep and the review rounds added: #38, #39, #52–#54, #59–#67, #82–#85, #90–#92, #95–#149, #157,
#161–#170). They were of three kinds.

**A write whose path passes through a computed read was dropped.** A graphics property, a table's
variable or its `Properties`, a `datetime` component, a dictionary's cell value, a `matfile`'s
variable, an object's accessor-backed or observable property — each hands a *temporary* to a read,
and an indexed write into that temporary went into the temporary. `p.YData(2) = 9` changed nothing
(#95); so did `T.Properties.VariableNames{1} = 'a'` (#114), `e.Day(2) = 15` (#126), `d{1}.Var1(1) = 9`
on a table in a cell (#116), and `t.h.data(2) = 9` replaced a handle object held in a struct with an
empty struct in both the copy and the original (#137). The one road that did store back — an image
or surface's data — evaluated its target twice, which M15 forbids.

**A rebuild lost what the value was.** A table rebuilt by a write came back without its row names
or row times (#38, #39); an N-D growth dropped the string tag (#52); a char row refused to grow into
a matrix (#53); a number written into a logical array made it double on every road (#157); a
scalar target refused a subscript past its end on two of the three roads (#162); `a(2).v(1) = 9` on
a struct array wrote into a selection and was lost (#166); `st.t = 'ab'; st.t.f = 1` replaced the
text with a struct without a word (#167).

**Forms were missing outright**: sparse indexed assignment (#26); `[st.f] = deal(…)` (#30) and
`[h.LineWidth] = deal(3)` (#134); a `for` over a char row or a string array; `d(key) = []`; the
levels of a write path that do not exist yet (#82, #83); `struct([])` (#85); `MException` causes
and stacks carried through `throw`, `rethrow` and `catch` (#59, #66, #67); the failure inside an
`ErrorHandler`'s record (#54); `guidata`, `axes(parent)`, `isvalid` (#102–#104); timers (#105);
`events`, `notify`, `addlistener`, `SetObservable` (#106, #108); `save -struct`, objects and
function handles in a MAT-file, `matfile` (#110–#113); `T{r, c} = v` (#25), `T(rows, vars) = …`,
`[T; U]`, `rowfun`, `varfun`, `addvars`, `table2array` (#115–#122); text and components written
into a `datetime` or `duration` (#123, #127–#129); `get.p` / `set.p` and `Dependent` (#27, #28).

Each had to land on the general write road, so that M2's share, M3's detach, M10's read of nothing
it writes, M14's refusal that changes nothing, M15's one evaluation a subscript and M16's order
apply to it as they apply to `x(i) = v`. A form built on a road of its own would be a new place for
every old defect.

## Decision — one road for every computed level

`Interpreter.Composite.cs`, built at V6.1 for tables and extended by the sub-stages that followed.
`TryWriteThroughComputedLevel` walks the target's chain from its root by peeks that run nothing — a
bound name, a literal field of a scalar struct, a cell slot named by a number in hand, an object's
property, one handle out of a handle array — and at the first level whose holder is computed it
reads the level into a scratch slot, rewrites the target with the slot in the level's place
(`ReplaceNode`), runs the **ordinary** assignment on that target, and puts the slot back through the
level's setter, then `StoreBack`s the rebuilt holder where it was read: a variable, a field, a cell
element. `IsComputedHolder` names the levels: a table's dot, brace or paren when it is the whole
target; a graphics handle's property when the write goes on past it (`p.YData = y` keeps its road);
a time value's component or property, whole or in part, and a paren on a time value when a
component follows; a dictionary's brace; a `matfile` and its `Properties`; an object's property when
it has an accessor or is observed and listened to.

**Get once, set once a statement, in R2025b's order.** The order was recorded with logging
accessors, not assumed: the subscripts left to right, each `end` calling the getter afresh; then
the right-hand side; then the getter once more for the value to modify; then M14's validation; then
the setter once — `o.p(end) = rhs` logs `get;rhs;get;set`, `o.p(idx) = rhs` logs `idx;rhs;get;set`,
`o.p = o.p + rhs` logs `get;rhs;set`, a read `x = o.p(end)` logs `get;get`. This is not M16's order
for a variable target, where a paren index on a variable runs its right-hand side first; the order
depends on the target's kind, and V0's order axis records both. A handle is a reference and is not
stored back; a property that answered a handle is set nowhere; a refused write leaves the holder
as it was. A scalar property read as a one-by-one goes back as the scalar.

**Levels that do not exist yet** are classified as the walk goes (`ClassifyLevel`): a level that
exists is walked; one the ordinary roads create (`s.a.b(3)`, `c{3}(2)`, `s(3).f`) is theirs; any
other absent level is `WriteThroughAbsentLevel`'s, the same get, modify, set — the rest of the path
assigned into a slot that starts from nothing, or from a `NewElement()` of the struct array the
level belongs to, then the slot assigned to the level itself, one step shorter, so the recursion
ends on a road that exists. `x.y(3).z = 1` makes a struct whose `y` is a 1-by-3 struct array with
empty `z` in the first two, `c{3}.f = 1` grows the cell with `[]` and makes a struct in the third
slot, and a path through an existing value of the wrong kind is refused in R2025b's words.

**A new holder kind is one arm of `IsComputedHolder` and one get/set pair.** That is how tables
(V6.1, V6.2, V6.14: `WriteTableBrace`, `WriteTableParen`, `WriteTableVariables` shared between
them), graphics properties and datetime components (V6.9, `WriteThroughPropertyLevel`), a
dictionary's brace (V6.9, `WriteDictionaryBrace`), a `matfile` (V6.13, `WriteThroughMatFile`),
an observed property (V6.12) and an accessor (V6.16, `WriteThroughObjectProperty`) each joined; the
image and surface special road in `EvaluateAssign` is gone. Hot path: a target whose inner node is
a variable that is not computed leaves after one `LookUp`, and an `end` reads its container lazily
(`IndexContext`), so `o.q(end) = 8` runs its parts first only when an `end` would read through an
object (`EndReadsThroughObject`).

## Decision — every rebuild keeps what the value is

`GrowthFill` is the target's own kind's: `false` for a logical, `NaT` for a datetime, `0s` for a
duration (recorded: R2025b grows a duration with zeros, not `NaN`), `char(0)` for a char, `""` for
a string. `CarryValueTags` and `KeepTextKind` carry the string, char-matrix and time tags through
every rebuild road — 1-D, 2-D and N-D growth, deletion, concatenation growth, the boxed road that
had lost both; the N-D road carries every tag (`CarryNdTags`), deletes whole slices and takes a char
row, which grows into a char matrix or an N-D char array. `IntoNumericArray` converts a number into
a logical target (`NaN` refused in R2025b's words); an emptied or empty-constructed logical is an
empty **logical**, which is what lets `m = true(1, 0); m(end + 1) = v` stay a mask. A scalar target
is promoted on every road — in place for a variable, through a scratch slot and one `StoreBack` for
any other entry. `['ab' 67]` is the char `'abC'`, so a char row against numbers compares its codes.

A table carries `RowNames`, `RowTimes`, `DimensionNames`, `VariableUnits`, `VariableDescriptions`,
`Description` and `UserData` through every rebuild (`Table.WithColumns`, `WithRowLabels`, `Select`);
a row-growing write extends the row names (`RowN`) and the row times (missing). `JgsTimeColumn` keeps
a duration variable and a timetable's row times as milliseconds and a tag, so `TT.Time` is the class,
format and exact values that went in. `JgsValueColumn` holds a variable as the script value it was
given — an integer, single or logical array, a string array, a cell that is not all text, a struct
array — shared out to readers (M2) and cut by rows with every tag carried. `reshape`, `permute`,
`squeeze` and `repmat` keep the time tag.

`EvaluateForWrite` takes `s(k)` writable inside the array the entry holds (#166, a write that was
lost without a word), and `ResolveStructForWrite` refuses a field that holds text, a number, a
logical, a cell or a non-empty array in R2025b's words rather than conjuring a struct in its place
(#167) — and, on the same line of code, lands `o.s.f = v` in an object's property (#137).

## Decision — the forms

Each on the general road, each recorded from R2025b before it was built.

- **Sparse** (V6.3, `JgsBuiltins.SparseAssign.cs`): the three index-write roads hand a sparse target
  to `AssignIntoSparse`, which evaluates the subscripts once against the matrix's extents and stores
  a CSC rebuild back through the target's entry — entries put into a position-keyed map, a written
  zero removing one, the matrix never expanded to its dense size. A sparse right-hand side goes into
  any other target as its dense values; `S op scalar` answers a sparse mask.
- **Exceptions** (V6.6): `JgsRuntimeException` carries the thrown value; `throw`, `rethrow` and
  `throwAsCaller` set it and `ExecuteHandler` binds the thrown exception's fields as shares, with the
  throw-site stack unless `rethrow` keeps the one it has, where a new exception used to be rebuilt
  from identifier and message. The stack goes on past the catch: the unwound frames, then the
  catching function at the line of the call it was waiting on, then its callers. `cause`,
  `addCause` (a column of shares), `getReport`. `error(ME)` is refused, because R2025b refuses it
  (`MATLAB:error:invalidMessageType`) — measured against the plan's own text.
- **ErrorHandler records** (V6.7): `FailureRecord` carries the failure's identifier and writes its
  message under "Error using `file>name` (line N)" when the failure unwound through a named
  function. `@(x) error(…)` under `cellfun` is refused as "Too many output arguments.", as R2025b
  refuses it. `mat2str(-0)` writes `0`.
- **Comma lists** (V6.8): `ExpandAssignmentTargets` expands `[st.f] = …` and `[st(2:3).f] = …` over a
  struct array or a handle array into one ordinary `st(k).f = v` a element, the subscript evaluated
  once (M15), the count what the call is asked for, so `deal(v)` and `varargout` see `nargout` as
  the element count.
- **Graphics verbs** (V6.10): `guidata` stores a counted share on the figure entry; `axes(f)` and
  `axes('Parent', f)`; `isvalid` over handles, handle arrays and handle objects; `JgsObject.Deleted`,
  a class's own `delete` wrapped so the mark follows the body and a second `delete` runs nothing;
  `RequireLive` refuses a dot on a deleted object.
- **Timers** (V6.11, `JgsBuiltins.Timers.cs`): a timer is a struct wearing the class name and a
  handle, its private half a `JgsTimerState`; `JgsTimerScheduler` is one a run — never a static, so
  two lanes on two threads never fire each other's timers — and drains what is due at statement
  boundaries (`Interpreter.Tick`), inside `pause`, `drawnow`, `start` and `wait`, oldest first,
  skipping a timer whose callback is on the stack; a compiled hot loop drains at its end. Every
  property write goes through `SetTimerField` (`WritableStruct()`, M7's gate).
- **Events and listeners** (V6.12, `JgsBuiltins.Events.cs`): `events` blocks, `SetObservable`,
  `< event.EventData`; a listener is a struct wearing `event.listener` and a handle, held by the
  source's `JgsObject.Listeners`; `notify` runs its listeners newest first inside the call; `PreSet`
  and `PostSet` fire once a statement from `TryAssignToObject`, an indexed write into an observed
  property being a computed level; `ObjectBeingDestroyed` after the mark. A path file that does not
  parse is a catchable runtime error at its first use, not a failure of the run.
- **Save and load** (V6.13): the MAT-file writer takes instances of user classes (a handle's element
  id the same at every mention, a deleted handle marked) and function handles (with the captured
  workspace); the reader decodes them through `IMatObjectBinder` — an instance with its defaults and
  no constructor, a class that is gone R2025b's warning and a `uint32`. `save -struct`; `matfile` as
  a handle struct whose `m.v` decodes the one variable now and whose every write is the
  computed-level road, `Append` a read, a merge and a rewrite.
- **Tables** (V6.1, V6.2, V6.14): `T.Properties.X = …` for every property; `T{r, c} = v` through the
  ordinary `IndexWrite` on each selected variable; `T(rows, vars) = table|cell`, a cell read as a
  table first (`VariableFromCells`, the `cell2table` rule); `[]` deleting rows or variables;
  `[T; U]` and `[T U]` in `Interpreter.TableConcat.cs` (names matched in the first's order, each
  variable joined by the bracket, a cell band taken as a table); `rowfun`, `varfun`, `addvars`,
  `table2array`, `istable` (which did not exist). Growth past the height extends every variable.
- **Datetime and duration** (V6.9, V6.15): `e.Day(2) = 15` and `e.Year = 2021` through
  `WithTimeComponent` (every moment taken apart on the wall clock, months and days rolling over);
  `IntoTimeArray` turns a one-moment right-hand side into one bare number so the ordinary roads fill
  a mask with it, reads text through the target's own `Format` and then `datetime(text)`'s shapes,
  and refuses a number into a datetime, a datetime into a double and a zone on one side only in
  R2025b's words; the two default formats follow what the array holds.
- **Accessors** (V6.16): `get.p` / `set.p` and `Dependent`; the bypass is the accessor's own body's,
  carried on its frame (`JgsEnvironment.AccessorOf`), as measured — inside `get.p` a read of `obj.p`
  is the storage but a write of it calls `set.p`; the declaration is checked before the set method
  runs; a value class's setter returns the object, stored back where it was read; `disp` and
  `struct(o)` run every getter, `properties` and `isprop` run none.
- **Loops, dictionaries, Maps** (V6.17): a `for` over a char row binds a 1-by-1 char a pass, over a
  char matrix an n-by-1 column, over a string array a 1-by-1 string, and a 0-by-n source runs n
  passes; `d(key) = []` removes an entry — the bare bracket alone, an empty in a variable refused
  "Dimensions of the key and value must be the same, or the value must be scalar."; `keys` and
  `values` answer columns of their kind, `d.keys` calls the verb and `d.Count` is refused; a
  `containers.Map`'s `Count` is a `uint64`.

## What moves

- **The ratchet holds no V6 line.** 236 lines were `pending V6` when V6.1 began (379 at V0; 246 of
  V6's had flipped under V3's write-back before V6 started, and V2 and V3 re-owned lines to V6 on
  their way). Every one flipped to agreement in the sub-stage that owned it, or is one of the nine
  `div=ADR0167` lines named under Divergences, stamped with this build's output in both lanes so
  any other output fails. Two were re-owned: `a107_listener_lifetime` to V10 (a `listener` object's
  exact lifetime is a destructor's), `a111_save_handle_load_is_new` to V9 (the loaded handle is a new
  instance now; what remains is #109's binding of `h` by `S = load(fn)`). `check-ratchet` prints V7 4,
  V8 1, V9 16, V10 14.
- **Seventeen fixtures, 908 lines, recorded from R2025b**: `table_rebuild_metadata` (35),
  `table_brace_assign` (40), `sparse_indexed_assign` (40), `growth_keeps_type` (55),
  `container_path_writes` (56), `mexception_values` (30), `errorhandler_records` (24),
  `struct_field_cslist_assign` (35), `composite_write_back` (63), `handle_lifetime` (34),
  `timer_callbacks` (53), `events_listeners` (77), `save_load_roundtrip` (70), `table_forms` (121),
  `datetime_forms` (77), `property_accessors` (54), `loop_text_dict_remove` (44). The sub-stages that
  probed before they recorded (`probe_s11*`, `probe_s12*`, `probe_s13`–`probe_s16`) found the plan's
  text wrong in places — `error(ME)` refused, `@(x) error(…)` under `cellfun` refused, a duration
  growing with zeros, a table's unknown variable added rather than refused, R2025b's `ErrorFcn`
  running after all — and the fixture holds what R2025b did, not what the plan expected.
- **Sidecars retired**: the `.owners` of `growth_keeps_type`, `value_isolation_accessors`,
  `value_isolation_objscope` (every line had flipped under V4 and V6.9), and, at this commit, of
  `composite_write_back`, `errorhandler_records`, `handle_lifetime`, `sparse_indexed_assign` and
  `value_isolation_graphics`, whose only remaining claims were the divergences below.
- **Found on the way and logged** (appendix A): #166, #167 (fixed in V6.5), #168 (fixed in V6.9),
  #169 (fixed in V6.17), #165 (the sparse mask's class, a divergence below); not built and owned by no
  fixture line: #164 (`fprintf('%d', seconds(2))` prints the milliseconds where R2025b refuses) and #170
  (R2025b infers a Map's `ValueType` from its values and refuses `[]` on a numeric one).
- **Not built**, recorded here for whoever takes it: the panel form of `axes(parent)`; `timerfind`,
  `BusyMode` 'queue'/'error', a timer firing while a session is idle; a `listener`'s lifetime with its
  last handle (V10), `AbortSet`, listeners on graphics handles; `whos('-file', fn)`,
  `functions(f).workspace`, `rmpath` forgetting a loaded class; `'GroupingVariables'` and
  `'ErrorHandler'` for `rowfun`/`varfun`; `datetime` text spellings beyond the target's `Format` and
  the recognized shapes; a validator's refusal on a property write in R2025b's words; `Static` on an
  accessor; `d.isKey(k)` and the other argument-taking verbs at a dictionary's dot.

## Consequences

- **The cost, against the V2 baseline** (99b7205, Release CLI, allocated bytes, median of three,
  this commit's binaries): `probe_z_scope_cost` 1.033 (1.024 at V3 — the scopes and the drain
  checks V6 added around a statement), `probe_z_cow_writes` 1.000, `probe_d03_loop_2M` 1.009,
  `probe_d12_concat_200k` 1.000, `probe_d12_charmatrix` 1.010. A write with an inert right-hand
  side and inert subscripts on a variable that is not computed takes the road it always took.
- A script that built a graphics series, a table, a datetime array or an object's property one
  element at a time now changes what it aimed at, where it used to change a temporary and say
  nothing. The tripwire that failed on a dropped store-back is the reason the silent forms were
  found before a user found them.
- `OwnershipGateTests`' allow-list names each scratch slot the road uses, so a new computed level
  cannot write past the model unseen; `audit-ownership.py` records 468 sites (437 at V5).
- The builtin coverage document moves from 1,118 to 1,124 (`guidata`, `isvalid`, `events`,
  `table2array`, `varfun`, `rowfun`, `istable`, `isprop`).
- Tests: `TableRebuildM167Tests`, `TableBraceAssignM167Tests`, `SparseIndexedAssignM167Tests`,
  `GrowthKeepsTypeM167Tests`, `ContainerPathWritesM167Tests`, `ExceptionFidelityM167Tests`,
  `ErrorHandlerRecordsM167Tests`, `StructFieldCslistAssignM167Tests`, `CompositeWriteBackM167Tests`,
  `GraphicsVerbsM167Tests`, `TimerCallbacksM167Tests`, `EventsListenersM167Tests`, `SaveLoadM167Tests`,
  `TableFormsM167Tests`, `DatetimeFormsM167Tests`, `PropertyAccessorsM167Tests`,
  `LoopTextDictRemoveM167Tests`. Three stress scripts that had frozen a JGraph leniency R2025b
  refuses (`stess_34`'s `error(ME)`, `stess_37`'s `S(2,3).a = 5`) or a divergence a sub-stage lifted
  now assert R2025b's answer.

## Divergences

Each is stamped `div=ADR0167` with this build's output, so an output that moves — or comes to agree,
which retires the line and this entry — fails the fixture.

- **A YData longer than a chosen XData is refused** (`g_line_manual_x_growth`, `composite_write_back`).
  After `p.XData = [10 20 30]`, R2025b accepts `p.YData(end + 1) = 4`, keeps the four values and
  draws nothing. A series here is one pair of equal length, so the write is refused: "YData has 4
  values where the series has 3. Both coordinates are written together — set them in one call, or
  draw the series again." (#133's growth through a property is built; the mismatch alone is refused.)
- **An error the runtime raises itself has no identifier and no "Error using" header**
  (`h_message_builtin_failure`, `errorhandler_records`; ADR 0062). Under an `ErrorHandler`, R2025b's
  record of an inner-dimension failure carries `MATLAB:innerdim` and the header; this build's
  carries an empty identifier and the bare message, as every runtime-raised error does.
- **`isvalid` of a plain number answers false** (`h_isvalid_number`, `handle_lifetime`). R2025b refuses
  "Undefined function 'isvalid' for input arguments of type 'double'."; a graphics handle here is a
  number, so a number that names nothing is a dead handle and `isvalid` says so.
- **A sparse matrix holds real values** (`s_rhs_complex`, `sparse_indexed_assign`; #26, #165).
  `S(2) = 2i` is refused — "A sparse matrix here holds real values; a complex value cannot be
  assigned into one." — where R2025b makes the matrix complex; and a sparse comparison mask
  (`sparse([1 0 2]) > 1`) answers class `double`, not `logical`, because `CscMatrix` has one value
  class. It indexes and counts as a mask.
- **The default `LineWidth` is 1.5** (`a141_line_width_default`, `a141_handle_array_element_prop_write`,
  `value_isolation_graphics`; #141). R2025b's is 0.5 points. 1.5 is the model's, the renderer's and
  the figure file's default by design, and moving it would move every golden image and every saved
  figure; it is not this plan's business.
- **`save` writes version 5 MAT-files only** (`a146_matfile_indexed_write`, `value_isolation_lifetime`;
  #146). `-v7.3` is refused: "save writes version 5 MAT-files only; version 7.3 is an HDF5 format that
  can be read but not written." A v7.3 file is read, through `load` and through `matfile`, and never
  written.
- **A `matfile` variable takes a dot or a brace past its paren** (`r_matfile_struct_field_read`,
  `r_matfile_cell_brace_read`, `save_load_roundtrip`). `m.st.a` and `m.c{2}` answer the field and the
  content here — the variable is read whole and indexed — where R2025b refuses "Cannot index into
  'st' because MatFile objects only support '()' indexing.": an accepted superset.
