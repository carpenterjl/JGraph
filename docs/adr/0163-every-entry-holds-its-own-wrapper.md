# ADR 0163 — Every entry holds its own wrapper

## Status

Accepted. Stage V2 of the value-ownership plan (`docs/plans/zfit-copies-and-temporaries-plan.md`),
after V1's holder count and gated writes (ADR 0162).

## Context

V1 put a holder count on every payload and a gate in front of every in-place write, and made a
*binding* — `b = a` — share the payload under the count. That left the model's second half open:
M2 says every **entry** holds a wrapper of its own, and a workspace variable is only one kind of
entry. An anonymous function's snapshot, a `containers.Map` value, a figure's appdata, `ans`, a
workspace named by `assignin`, the struct `load` answers, and every cell or struct a builtin builds
out of another's children — each is a place a later statement can read, and each was storing
whatever wrapper it was handed. A wrapper in two entries defeats the gate from either side: the
first write through one of them detaches that wrapper's payload, and the other entry, holding the
same wrapper, moves with it. Appendix A #1–#4, #24, #80, #100 and #101 are that defect spelled
through the roads the probes found; the generated matrix held 134 lines of it.

There is a second, quieter question V1 left to this stage. A binding shared *every* call's answer,
because nothing could say whether a builtin had minted its answer or handed back a wrapper it was
given or had stored (`deal`, `getappdata`, `squeeze` on a vector). The safe reading costs one copy
at the name's first write — `x = zeros(1, n); x(1) = 1` copied `n` doubles for a holder that did
not exist.

## Decision — V2.1, the audit

`tools/ownership/audit-ownership.py` learned wrappers, where V1.1 had taught it payloads. It follows
a `JgsValue` from where it was borrowed — an argument, an element read out of someone's cell or
struct, a local assigned one of those — and reports two new categories:

- **store** (130 sites): a borrowed wrapper placed into a container the method is building — an
  array slot, a dictionary entry, a collection expression, an initializer, a `List.Add`, a copy
  constructor over a borrowed dictionary, or a gated slot store whose *value* is borrowed. Only the
  top-level shape of an expression counts: `Foo(args[0])` is a call, whose answer is `Foo`'s
  business and is settled per helper in passes, as V1.1's mint contracts were.
- **handback** (84 sites): a builtin whose return is a borrowed wrapper, or a call of a helper that
  returns one. The per-builtin verdict (`fresh`, `borrowed`, `unknown`, or a mix) is what V2.2's
  adoption list is checked against.

Each recorded site carries the rule it satisfies and the note that settles it, as before. Of the
130 stores, the ones that mattered were the cell roads — `c(idx)`, `c(:)`, a cell's transpose,
`[C, C]` and `[C; C]`, `repmat`, `repelem`, `num2cell` of a cell, `values`/`entries`, `sortrows`
of a cell, `c(1:2) = d` — and the struct rebuilders `setfield`, `rmfield`, `orderfields`,
`convertContainedStringsToChars`, the option structs (`odeset`, `bvpset`, `ddeset`, `bvpinit`,
`bvpxtend`, `dde23`'s history), and `cellfun`/`arrayfun`/`structfun`'s collected answers. The rest
are argument lists and option tables built for one call, immutable elements (a number, a string),
containers the entry's own write road had already detached, and the gates themselves.

The census also corrected V1.1: its parameter seeding joined a signature's parameters into one
string before splitting them again, so only the last parameter of a member ever read as borrowed.
With every parameter seeded, 11 more writes, 6 disposes and 31 re-categorised sites appeared — all
destination buffers and element arrays a caller allocates for a mint helper — and are recorded with
that reading.

Two things the audit could not settle by reading alone got a spelled-out assertion: a helper whose
returns are a `switch` expression or a recursion carries `// audit: mints` above its declaration
(`Filled`, behind `zeros` and `ones`), which the audit believes and this ADR lists.

## Decision — V2.2, the change

**Every road stores a share.** `AnonymousFunction.Create` captures `CopyForBinding(value)` (#1,
#24, #101); `setappdata` keeps a counted share in the MATLAB dialect and the caller's own wrapper
marked exposed in JGS (#100, M17); `BindAns` binds `CopyForBinding(value)` unless the statement
minted it (#3, #4); `assignin` declares a share; `load` gives the returned struct a share of what
the workspace adopts (#109's storage half); a keyed collection's value takes a share through the
interpreter's `m(k) = v`, the constructor and `insert` (#2, with the dialect deciding as for
appdata); `UserData` holds a share through `set` as it already did through the dotted write; and
each cell and struct road above shares per child. `JgsStructArray.SharedCopy` is what a struct
rebuilder starts from.

**`str2func` captures nothing** (#80): the text is evaluated in an empty static workspace over the
built-in layer, so a free name in the body is a function resolved at the call and never the
caller's variable; the handle still carries the caller's file for its local functions.

**A binding adopts a minted answer.** `JgsBuiltins.MintingBuiltins` names the builtins whose answer
a binding keeps rather than shares: `zeros`, `ones`, `rand`, `colon`, `magic`, `cell`, `struct`,
`repmat`, `sort`, `num2cell`, `fieldnames`, `struct2cell`, `cell2struct`, `setfield`, `rmfield`,
`values`, `keys`, `sprintf`, `isempty`. The list is opt-in and verified: the audit fails the gate
when a name on it can return anything but a wrapper it minted. Three candidates it refused while
this was written — `logical` hands a logical argument back as it is, `eye` and `unique` have a
return the scan cannot prove — are the reason the list is verified rather than trusted. A user
function's answer is adopted too, because its outputs are its frame's own entries and the frame
dies with the call; an anonymous function's is not, because `@() v` hands back its capture. The
call road records the answer it may adopt as it returns, and the binding adopts only that object,
so an inner call's answer, or a slot a bound cell handed back when the name turned out to index
rather than call, is still shared.

**What moves.** The `value_isolation` fixtures record the change under the ratchet, stamped in both
lanes: `a001`–`a004`, `a024`, `a080` (binding), `a100`, `a101` (graphics), and the 86 `anon`/`map`
lines of each `gen_entry` fixture flip to agreement. Twelve generated lines change baseline without
flipping and are re-stamped under their owners: the logical `numify` lines through an anonymous
capture and a map value and the dictionary `remove` lines through the same two (V6), and the eight
`w_paren_field_sub_*` order lines (V3's, M16's stale target), where the element write now lands and
only the `rhs;idx;` order still differs from R2025b.

**Re-owned lines.** V2's audit moved the generated lines whose cause is not storage to the stage
that owns the cause, through the generator's owner rules: a numeric written into a logical array
(`e_logical_*_numify_*`, appendix #157's family, every entry kind) is V6's "every rebuild keeps
what the value is"; a char row written through a container path and a string element written
through braces in a callee (#90, #91) are V6's refused roads; an indexed write into a scalar
reached through a container (`c{2}(1) = 9`, `s(2).f(1) = 9`) is V6's `container_path_writes`; a
value object's property written through a global is V4 (#16), and through a cell slot or a struct
field is V6's write-back rule. The `.m` fixtures are byte-identical; only the sidecars changed.

## Decision — V2.3, the tripwire

`OwnershipGateTests` gains a rule over `Declare`, `DeclareFunction` and `TryAssign`: a binding
whose value is not visibly minted (a `JgsValue` factory, an exception, an empty), copied
(`CopyForBinding`) or shared (`Share`) must be allowed by name with the reason — a compiled loop's
register, a persistent slot (V5's), a JGS `let` (M17), `Rebind`'s rebuilt values, a hoisted
function. Every allowed line is checked to still exist, as before.

## Consequences

- `x = zeros(1, n); x(1) = 1` no longer copies: `ValueIsolationM163Tests` asserts zero allocations
  for the write and one when a second name shares. `OwnershipM162Tests` expectations that counted
  the call's temporary as a third holder now count two.
- A builtin that starts handing back an argument fails the gate the day it does, if it is on the
  minting list; if it is not, its answer is shared and the cost is a copy, never a leak.
- `containers.Map` values and appdata follow the dialect: a share in MATLAB, the caller's own
  wrapper (exposed for M6) in JGS. `UserData` shares in both, because the property table has no
  dialect; a JGS script that wrote through a source variable after setting `UserData` would have
  seen the property follow, and now sees it keep what was set.
- `str2func('@() name')` refuses a caller variable, as R2025b does.

## Divergences

None. Every line V2 owns flips to agreement; the four generated lines whose baseline changed are
re-stamped under their owners (two V6, two re-owned to V6 by this stage's audit), and nothing is on
a `div=` line.
