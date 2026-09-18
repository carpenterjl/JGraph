# ADR 0164 — A scope holds a share, and a write reads nothing it writes

## Status

Accepted. Stage V3 of the value-ownership plan (`docs/plans/zfit-copies-and-temporaries-plan.md`),
after V2's entries (ADR 0163). It landed in two commits: V3a (7a741d6 — M5's scopes, the
script-running audit, M8) and V3b (this ADR's commit — the write roads: M16's order, M15, M10, M14,
M11, and the cleared global #158).

## Context

V1 and V2 gave every *entry* a counted share of its payload, so a write through one name detaches
from every other. Two kinds of reader were still outside the count.

The first is C# itself. The interpreter evaluates a left operand, an argument, an index target, a
loop's source, and then runs more script code before it is done with what it read. That code can
write the variable the reader came from, and with one holder counted the gate lets the write land
in place: `r = gv + bump()`, where `bump` sets `gv(1) = 7`, added the new `gv` where R2025b adds the
one that was read (#5–#11, #13–#15, #19–#23, #57, #58, #155, #159 in appendix A). A builtin that
mutates its argument — `insert(d, k, v)` changed `d` — is the same defect written by hand (#20, #21).

The second is a write reading its own target. `a([2 3 1]) = a` scattered `a` into `a` in index
order, so a later pick read an element an earlier pick had already overwritten (#31–#34, #42–#46);
`a(4:6) = a` grew the target first and then read the grown target as the right-hand side (#40, #41,
#73–#75). A refused write left its growth behind (#47–#49, #125), a write mask longer or shorter
than the array was refused (#62, #68–#71), and a struct element carrying other fields was grafted
onto every element rather than refused (#76, #86, #87). `[v(1), b] = deal(7, v)` wrote `v(1)` into
the payload `b` was about to receive (#35). And the order of a write's parts was one order for
every target shape — right-hand side first — where R2025b runs the subscripts first for every
shape but a paren index directly on a variable (#151), reads the target through its entry after
both (#56), and treats a global cleared by one of them as cleared (#158); the brace fast path
evaluated a subscript it then declined and evaluated again (#50, #51).

## Decision — V3.1, the script-running audit

`tools/ownership/audit-ownership.py` builds a call graph over every method and file-scoped local
function in `JGraph.Scripting` and walks it from the script entry points (a callable's `Call` and
`CallMultiple`, the source and tree evaluators, `RunWhilePaused`, a callback dispatcher's `Drain`).
A call is followed when it is unqualified or on the engine's own receivers; library-looking
receivers are not, because following `.Add(` and `.Build(` by name joined every builtin to every
other. The operator-overload dispatchers are cut (an object argument is the dynamic rule's, below)
and so are the helpers that forward to another builtin looked up by name. It flags 83 builtins.
`JgsBuiltins.ScriptRunningBuiltins` (`JgsBuiltins.Scopes.cs`) lists 69 of them, and the file
asserts the other 14 script-free with one reason each (`// audit: runs no script: …`); the gate
fails on a flagged builtin neither listed nor asserted, a listed one no longer flagged, and a stale
assertion. `BuiltinFunction.RunsScript` reads the list at mint time.

## Decision — V3.2, the scopes (M5, M8)

**A scope holds a counted share** (`Interpreter.Scopes.cs`). Wherever the interpreter keeps an
evaluated wrapper while more script code runs, it keeps `JgsValue.Share(w)` instead — a holder in
M2's sense — and gives the count back in a `finally`, however the scope ends (an error, a `return`,
a `break`, a `continue`). A share a callee hands back is kept (`ScopeHolds.Keep`): the answer is a
holder now. A share whose wrapper has since swapped its storage (a compaction, a growth, a
demotion) is not released twice. The roads: a binary operand and the `+` chain; argument
evaluation (lazy — a hold is taken only when a later argument is not inert and an earlier value is
holdable); the whole call, for a listed builtin and for any call handed a callable or an object
(the dynamic half of the rule, which also covers a callable-taker registered without a name the
audit can read); an index target and a method's receiver (`BoundMethod.WithReceiverValue`); a brace
read and the brace spread; the rows of a bracket or cell literal; a loop's source. A user function,
an anonymous function, a method and a constructor bind their parameters through `CopyForBinding`
and need no scope.

**Inert expressions open none.** `y = x + k * z` holds nothing, because nothing in `k * z` can run
script code; `y = x + f(y)` holds `x`. The syntactic half is cached on the node
(`Expr.ScopeShape`, `Expr.ScopeNames`) and the names are checked against their bindings when the
scope would open: data is inert, a built-in that is not `RunsScript` is inert, an object (whose
operators and subscripts are methods), a function handle and a name nothing holds are not. Scopes
are binding shares in M17's sense, so the JGS dialect opens none.

**A builtin never mutates an argument** (M8). `insert` and `remove` on a `dictionary` start from an
internal share (`JgsBuiltins.Private`) and answer a new dictionary; a `containers.Map` is a handle
and is still changed in place.

## Decision — V3.3, the write roads (M16, M15, M10, M14, M11)

**The target's shape decides the order** (M16, `Interpreter.Writes.cs`). A paren index directly on
a variable — `x(i) = rhs`, `x(i, j) = rhs`, a dictionary's `d(k) = rhs` — runs its right-hand side
first, then its subscripts, then reads the target. Every other shape runs its subscripts and
dynamic field names first, left to right down the path, then the right-hand side, then reads the
target; `end` is taken against the container as it is at that moment, and on a container that does
not exist yet every extent is zero. Only a part that can run script code can tell the orders apart,
so the parts are pre-evaluated into `PreEvaluated` nodes (`PrepareTarget`) only when one of them is
not inert; the roads then evaluate nothing twice — **each subscript exactly once, M15** — and
resolve the target through its entry after every part has run, never from a wrapper captured
before them. The conjuring of a target that does not exist yet moved to the same point: a global
one of the parts cleared is created afresh, and a right-hand side that reads the name it is written
into still finds nothing there.

**A cleared global** (#158). `clear global g` takes `g` out of the global workspace and nothing
else: a frame that declared it keeps a name the workspace no longer holds. Reading that name — for
`end`, or anywhere — is R2025b's "Reference to a cleared variable g."; writing it creates it in the
global workspace again. Measured, not assumed: the order matrix shows a global another function
re-created between the clear and the write being written (`w_paren_sub_grow_rhs_clear` is
`[50 9]`), so the link is by name and there is no local. `clear global g` had been parsing as a bare
`clear` followed by a `global g` declaration, because `global` is a keyword token; command syntax
now takes it as a word.

**A write-back for every shape.** Growth, deletion and every other write that rebuilds its container
end on `StoreBack`: a variable is rebound, a field is written through `AssignToMember`, a cell
element through `AssignToBraceIndex`. That is what makes `s.f(end + 1) = v`, `c{1}(2) = []`,
`s.a.b(4) = 9` and `c{3}(2) = 9` land where they were aimed, where they used to be refused for want
of a plain variable. A road that goes on writing after the store-back (growth, then the write)
continues with the wrapper the entry now holds (`Stored`), because a value object's property keeps
the checked copy its setter made. A field the write creates on its way (`s.f(3) = 9` with no `f`,
`s = []; s.f(2) = 9`) starts as the empty of the right-hand side's kind, as a variable always did.

**A write reads nothing it writes** (M10). When the right-hand side, or a subscript, is the target's
own payload (`SharesStorageWith`, or the same wrapper), the write holds a counted share of it for its
duration under the same `ScopeHolds` a scope uses; the target's first write then detaches (M3) and
the right-hand side goes on reading what it read. Growth from itself follows: `TryGrowInPlace`
refuses a shared buffer, so `a(4:6) = a` rebuilds, and the share still reads the old payload. Both
dialects, because reading what one is writing is wrong in either.

**A refused write changes nothing** (M14). Every road validates the subscripts, the selection's
count against the right-hand side and the conversion before it grows, promotes or demotes the
target: the count check moved ahead of `GrowVector`, `Grow` and the cell growth, and the picks a
write names are computed once and handed to the packed, complex and boxed writes rather than
recomputed after growth. A write mask names the positions of its true entries and may be any
length; the extent it needs is its highest true position (`MaskPicks`), so a longer mask grows the
array and a shorter one writes what it names; read masks keep the read rule. A subscript below one
on the write side is refused in R2025b's words ("Array indices must be positive integers or logical
values.", or "Index in position k is invalid. …" with several subscripts) before anything is rebuilt.
`st(k) = t` requires `t` to carry exactly the array's fields, in any order, and refuses otherwise as
`MATLAB:heterogeneousStrucAssignment` ("Subscripted assignment between dissimilar structures.")
before an element is replaced; growth past the end fills the gap with the array's fields, so
`struct('a', {})` accepts `st(1) = struct('a', 7)` and refuses `struct('b', 7)`.

**A multiple assignment holds every output** (M11). `ExecuteMultiAssign` takes a counted share of
every output before the first target is written; each binding takes its own share; what is left —
an output an error stopped short of, a `~` — is given back in the `finally`. `[v(1), b] = deal(7, v)`
binds `b` to the `v` the call answered.

**Three builtins the matrix caught.** A bracket of structs whose field sets differ is refused as
R2025b refuses it ("Names of fields in structure arrays being concatenated do not match. …") and
refused before anything is built, where the union JGraph used to make grafted the missing field onto
the pieces themselves (`b_horzcat_failing`; the JGS dialect keeps the union, over shared copies);
`rmfield` rebuilds from the kept fields rather than copying and pruning, because a `Dictionary`
refills a removed slot with the next field added, which put a later `s.g` ahead of the fields that
were there, and a field that is not there is simply not removed (R2025b, the order matrix's
`w_dynfield_sub_shrink_rhs_shrink` row); `mat2str` prints a real element of a complex array as the
real it is (#61, withdrawing ADR 0142's recorded divergence).

## What moves

Stamped in both lanes. V3a flipped 178 lines: `value_isolation_scope` (16: #5–#11, #14, #15,
#19–#23, #57, #58), `gen_scope_5` (88), `gen_scope_5000` (64), `gen_builtins` (6: `insert`,
`remove`), and `scope.boxed.txt` (#159). V3b flipped the remaining 316 V3 lines: `gen_order` (206,
#151, #56, #158, and the ten `w_paren{,2d}_sub_grow_rhs_*` lines of its boxed overlay, #160),
`gen_overlap` (51: `o_permute_rhs_same`, `o_grow_rhs_same`, `o_grow_end_rhs_same`, `o_both_same`,
`o_both_same_grow`), `gen_refusal` (12: the count refusals on a range, a 2-D range, a cell paren and
a string element), `gen_multi` (8: `m_deal_*`, `m_three_*`), `_writes` (36: #31–#35, #40–#51, #56,
#62, #68–#71, #73–#76, #86, #87, `a151_*`), `_forms` (#124, #125), `gen_builtins`
(`b_horzcat_failing`). Nothing V3 owns is pending after this commit; `check-ratchet` is what holds
that.

**Lines owned by V6 that flipped on the way** (246), named here because a flip must be recorded by
the commit that makes it: growth and deletion through a field, a cell slot, a cell assignment and a
value object's property — the `e_*_{field,cellslot,cellassign,objprop}_{grow,delete}_entry`,
`e_*_{field,cellslot,cellassign,objprop}_colon_entry`, `e_*_*_elem_entry`, `e_*_objprop_bracechar_*`
and `e_*_*_bracechar_*` families of `gen_entry_5` and `gen_entry_5000` (91 each), and the
`o_*_{char,structarr}_{field,cellelem}` and `o_grow*_{field,cellelem}` lines of `gen_overlap` (54),
all of them V6's "container path writes" and refused-road rules, which the write-back settled; in
`_forms`: `a060_sort_cell_alias`, `a061_complex_promotion_alias` (#61), `a063_struct_field_delete_alias`,
`a090_nested_end_cell_growth`, `a091_string_element_char_write`, `a092_strrep_cellstr_alias`,
`a148_empty_local_field_write` and `a148_empty_global_field_write` (`s = []; s.f = 1`); in `_writes`:
`a153_order_cell_elem_end_vs_rhs` and `a153_order_struct_field_end_vs_rhs`. The boxed overlays of
`gen_overlap` (15 lines) and `_writes` (4) were lines that agreed under boxed storage only, and are
deleted because the packed recording agrees now.

**Baselines that changed without flipping**, all V6's and re-stamped under it: the order half of
`a153_order_table_varnames_sub_vs_rhs` and `a153_order_datetime_day_sub_vs_rhs` now logs `idx;rhs;`
while the accessor write still does not land (#147); `a119_tbl_cell_var_nested_write` writes into
the char a table's cell column hands back where it used to be refused; `a133_graphics_prop_growth_write`
and `_delete_write` now reach the property setter, which refuses a `YData` of another length than
the series (V6's accessor paths).

**Re-owned by V3a** through the generator's owner rules (`.m` byte-identical): `s_loop_source_char`
and `s_loop_source_string` → V6, because the held value is isolated now and what remains is a
missing form (a char row refused as a loop source, a string array's elements bound as char rows).

## Consequences

- A scope costs one share wrapper per hold: `probe_z_scope_cost` (100K `y = y + f(i)` iterations)
  allocates 1.024× V2's bytes, the designed cost — a scope cannot reuse the entry's wrapper, since a
  detach swaps that wrapper's payload, which is exactly what the scope must not see. Inert loop
  bodies (`y = x + k * z`, `y = [x, z; z, x]`, `y = max(x, z(end))`) open no scope at all
  (`ValueIsolationM164Tests` counts zero).
- A write with an inert right-hand side and inert subscripts takes the road it always took; the
  pre-evaluation and the re-resolution only happen when a part can run script code. `x(end + 1) = v`
  pays one lookup for the `end` read of its container.
- Growth and deletion through any container path are supported, so a script that built a struct
  field or a cell element one append at a time no longer has to copy out and back. A test that had
  pinned the old refusal (`GuiRoundGapsTests`, a cell reached through a field) now pins the growth.
- `ConcatenationUnionsTheFields` became `ConcatenationRefusesDifferentFieldSetsAndLeavesThePieces`:
  a MATLAB-dialect script that relied on the union is refused in R2025b's words.
- The ownership unit tests run in the boxed lane; V1/V2's lane runs had been parity-filtered
  (`OwnershipM162Tests`, `ValueIsolationM163Tests`, `ExposedReclamationM162Tests` read `.AsBuffer` of
  boxed arrays and now read `ScopePayload`, or stand down where there is no buffer to measure).
- Tests: `ValueIsolationM164Tests` (every scope kind gives its count back however it ends, the shares
  a builtin hands back are kept, inert bodies open none, M8, M10, M11, M14, M16's order and the
  cleared global), `SubscriptOnceM164Tests` (every write road, every subscript shape, and one that
  throws, each counting its subscript's calls), and `OwnershipGateTests` over the new conjuring line.

## Divergences

None recorded. Three are withdrawn, each marked in its own ADR: ADR 0142's "`mat2str` prints `+0i`
for a real element of a complex array" (#61) — it prints the real part alone now, as R2025b does;
ADR 0152's "a grown cell's new slots are 1-by-0" (`cell_grow_fill_shape` now agrees, its `div=` line
retired) and "a cell reached through a field will not grow". `rmfield` of a field the struct
does not have answers the struct unchanged, which is what R2025b did in the order matrix's
`w_dynfield_sub_shrink_rhs_shrink` row; it is recorded here as a measurement, not a choice.
