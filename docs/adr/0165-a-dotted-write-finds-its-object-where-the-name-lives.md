# ADR 0165 — A dotted write finds its object where the name lives

## Status

Accepted. Stage V4 of the value-ownership plan (`docs/plans/zfit-copies-and-temporaries-plan.md`),
after V3's scopes and write roads (ADR 0164).

## Context

A dotted write, `name.prop = v`, asks two questions before it takes the struct road: is `name` a
graphics handle (M51), and is it an instance of a user class (M68)? Both questions read the name
with `env.TryGet` — the frame's own entries and its parents' — where every other read and write in
the interpreter reads it with `LookUp`, which sends a name the frame declared `global` to the
global workspace. So inside a function

```matlab
global gV;  gV = ValueBox();  gV.p = 7;     % #16
global gp;  gp = plot([1 2 3]);  gp.YData = [4 5 6];   % #150
```

neither question found anything, the write fell through to the struct road, and the struct road —
which does use `LookUp` — found the object and refused it: "Cannot set a field on 'gV': it is a
ValueBox, not a struct.", "…it is a number, not a struct." R2025b assigns. The same miss reached
`h(i).Color = c` on a handle array held in a global ("Cannot index into … with a subscript and a
field"), and `ClassNamed`, which would have taken a global holding data for the class of the same
name.

V0 had two lines for it (`a016_value_object_global_prop`, `a150_global_handle_prop_write`) and
the entry matrix one per size (`e_object_global_prop_entry`). The plan asked for the matrix around
them before the fix: the global, nested-function and persistent forms, for value and handle
classes.

## Decision

**The fixture first.** `value_isolation_objscope` (43 lines, recorded from R2025b): a value
object, a handle object and a graphics handle, each reached through a global (from the declaring
frame, from another frame, through `eval`), a nested function's parent workspace (one and two
levels) and a persistent; whole-property writes, a dynamic property name, element writes, growth
and deletion through the property, an object held by the object, a cell and a struct held by it,
an alias taken before the write (isolated for the value class, shared for the handle classes), an
anonymous function's capture, a refused write, a handle held by a value object and a value object
held by a handle. On V3's binary 29 of the 43 disagreed; the nested-function and persistent forms
already agreed (a nested function's frame reaches its parent's entries through `TryGet`, and a
persistent is a local for the length of the call) and stay in as controls.

**The fix.** `ResolveObjectTarget`, `TryResolveHandleTarget`, `IsHandleArray` and `ClassNamed`
read the name with `LookUp`. Nothing else is needed for M3: under M2 the global workspace's entry
holds the very wrapper `LookUp` hands back, so `WritableFields()` on it detaches *in the entry* —
a copy taken earlier (`w = gV`) keeps its payload, and the write lands in the global. Element
writes, growth and deletion through the property (`gV.p(5) = 9`, `gV.p(2) = []`) ride V3b's
store-back, which re-enters `AssignToMember` and now finds the object.

**A handle is its own share.** The matrix found a second defect, not a global one:

```matlab
o = ValueBox();  o.p = HandleHolder();  w = o;  o.p.data = 7;   % w.p.data was [], R2025b: 7
```

`CopyForBinding` knows a handle object is a reference and hands the wrapper back uncounted, but
`JgsValue.Share` — what a *container's* private copy calls on each thing it holds — counted a
`JgsObject` whatever its class. `w = o` shares `o`'s payload; the nested write makes `o` writable,
which copies the object and `Share`s its fields, the handle among them, now counted twice; and the
write through the handle's `WritableFields()` then read "shared" and cloned the handle. `Share`
now answers a handle object, and a handle-class struct (`containers.Map`, `VideoWriter`), with the
value itself and takes no count — the rule `CopyForBinding`, `Hold` and `Private` each already
applied by hand, moved to the one place every road goes through. The scope roads compare the share
with what they passed in before recording a release, so nothing gives back a count it never took.

## What moves

- Flipped: `a016_value_object_global_prop` (scope), `e_object_global_prop_entry` (gen_entry_5,
  gen_entry_5000), and 41 of `value_isolation_objscope`'s 43 lines, 27 of which disagreed before
  the fix. No boxed overlay: both representations agree on every line.
- `a150_global_handle_prop_write` moves to V6. V4 does what row #150 asked — the handle is found
  in the global, and `x_global_gfx_prop_write` and four more lines of the new fixture hold that
  flip — but the row's own write changes `YData`'s length, which the property setter refuses
  (#133, V6). Its baseline changes from the struct road's refusal to the setter's, and
  `a153_order_graphics_ydata_end_vs_rhs` (V6) changes the same way for the same reason.
- Two lines of the new fixture are owned by V6 because the local form fails identically:
  `x_global_gfx_prop_elem_write` (`h.YData(2) = 7` is dropped, #95) and
  `v_global_prop_struct_field_write` (`o.p.f = 7`, a field write into a struct an object's property
  holds, is refused — new appendix row #161).
- `check-ratchet` holds no V4 line.

## Consequences

- A MATLAB script that keeps its application object, or its plot handles, in a global — the usual
  shape of a GUI script written before nested functions — can set properties on it from any
  function.
- `JgsValue.Share(handle)` is reference-equal to its argument. Code that shares and later releases
  must test for that, as `Hold` and `ShareFor` do. The ownership audit now sees `Share` as able to
  hand back its argument, and asked for the two stores inside `PrivateCopy` (a cell's slots, an
  object's fields) to be classified: both are the rule itself (437 sites).
- Tests: `ValueIsolationM165Tests` (the global roads for value, handle and graphics objects, every
  write shape through a property, the refused write's atomicity, and the handle held by a value
  object and by a cell).

## Divergences

None recorded.
