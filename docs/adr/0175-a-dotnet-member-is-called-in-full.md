# ADR 0175 — A .NET member is called in full

## Status

Accepted. Stage I2 of the .NET and shared-library interop plan
(`docs/plans/net-and-shared-library-interop-plan.md`), after stage I1 (ADR 0174, 1d76b57). One
commit, this ADR's.

## Context

Stage I1 let a script construct .NET types, read their members and call methods whose parameters
are all plain inputs. `net_members` (71 lines, recorded in step 0) stopped at its first line: a
`ref` parameter, which no overload could take. The fixture also pins `out`, `params` and optional
parameters, `void` methods asked for an output, the indexer, refused writes, static writes, interface
views, nested types, operators, `IDisposable`, and a loop over an `IEnumerable`. Four `net_display`
lines were owned by this stage too: the `methods` listing and three `methods -full` listings.

Two more R2025b probes (scratch, not committed) settled what the fixtures leave open: the listing's
column rule over nine types and two user classes, and ten refusals and bare-mention cases around the
fixture's rows (`probe2`: `x = m.Bump`, the `NET.explicitCast` failures, `NET.setStaticProperty` of an
unknown type, a string property given a number).

## Decision

**A signature model, cached per type and name.** `NetSignature` reads a method or constructor once:
every parameter but an `out` one is an input, in declaration order; trailing optional parameters may
be left off; the outputs are the return value when there is one (`RetVal`), then each `ref` and `out`
parameter's value after the call, in declaration order. `params T[]` is an ordinary array parameter —
R2025b passes an array there and refuses loose arguments. A type's groups are built once per process
and shared by every session (the groups are immutable); stage 1 asked `GetMethods`, which copies its
array, on every mention. Operators (`op_Addition`) are callable by name, and a default indexer's
accessors answer to its property name (`list.Item(0)`).

**Overloads are chosen by output count as well as by argument.** A candidate must take as many
inputs as were given and hand back at least as many outputs as were asked for; among those, the
recorded rank order of stage 1 decides. `x = JGTest.Modifiers.Void()` and
`[a, b] = JGTest.Modifiers.Double(2)` therefore find no overload, which is what R2025b says. The
refusal is R2025b's per road: `MATLAB:UndefinedFunction`, "No method 'T.name' with matching
signature found.", through the type; `MATLAB:class:UndefinedMethod`, "No method 'name' with matching
signature found for class 'T'.", through an object (stage 1 used the second for both).

**A .NET call answers several outputs.** `NetCallable` and the function-syntax callable implement
`IJgsMultiCallable`, so `[w, f] = JGTest.Modifiers.Split(2.75)`, `feval` and a handle all reach the
extra outputs, and a statement asks for none, so a `void` method runs as a statement.

**A bare mention asks for one output, except as a whole statement.** `m.Bump;` and
`JGTest.Members.Reset` run a `void` method; `x = m.Bump` is refused exactly as `x = m.Bump()` is
(probe2). The statement road marks the member expression it evaluates, and only that mention asks
for none.

**`System.Reflection.Missing.Value`** in an optional place stands for the parameter's default, as a
parameter left off does.

**Writes and their refusals, as recorded.** A numeric property given a non-scalar is
`MATLAB:class:RequireScalar` and a non-numeric scalar `MATLAB:class:RequireNumeric`; a string
property given a number is `MATLAB:NET:NetConversion:StringConversion`; each message leads with
"Error setting property 'P' of class ''C'':". A property with no getter reads as
`MATLAB:class:GetProhibited`. `obj.Item(0) = v` — a write into a method's answer — is
`MATLAB:index:assignmentToTemporary`, not a call of the indexer's setter.

**`NET.setStaticProperty('T.Name', v)`** is the one road to a static write (`T.Name = v` is ordinary
MATLAB assignment and makes a struct `T`, which JGraph already did). A read-only static is
`MATLAB:NET:InvalidStaticPropertyAccess`, an unknown member `MATLAB:NET:InvalidStaticPropName`, an
unknown type `MATLAB:NET:InvalidClassName`; a value the member cannot take is refused with the reason
alone, without the instance write's lead (probe2).

**`NET.explicitCast(obj, 'Interface')`** answers an interface view: a `NetObject` whose members are the
interface's (an explicit implementation's included) and `System.Object`'s, and whose class is
`NET.view.Interface`. R2025b refuses a value that is not a .NET object
(`MATLAB:NET:interfaceView:RequireNetObject`), a class (`...:UnsupportedClassToClass`), and a type the
object does not implement or that does not exist (`...:InvalidCast`); so does JGraph, in the same
words.

**`NET.convertArray`** arrives in the forms `net_members` passes (a vector to a rank-1 array of the
class's element type, or of a named one); stage I4 completes it with the rest of the array surface.

**A nested type keeps .NET's `+`** (`JGTest.Outer+Inner`, measured); stage 1 wrote a dot. A nested
type is reached through a factory, not by name: `JGTest.Outer.Inner()` is
`MATLAB:subscripting:classHasNoPropertyOrMethod`, as recorded.

**`delete` of a .NET object does nothing**: R2025b neither calls `Dispose` nor clears the variable.
**A `for` loop over a .NET object is refused** (`MATLAB:class:parenReferenceScalar`), an
`IEnumerable` included.

**`methods` prints a .NET type's listing when its answer is not asked for** (`NetMethodsListing`).
The names are padded to the longest name listed plus two and filled top to bottom in as many columns
as fit 180 characters, the command window width R2025b reports under `-batch`: the instance names (the
type's methods, its indexer, its constructor, its operators' MATLAB names, `matlab.mixin.Scalar`'s),
then `Static methods:` with the static names no instance method shares, then the line naming
`handle`'s for a reference type. A static class lists no instance methods of its own; an interface
view lists `eq`. With `-full`, one line per signature in R2025b's notation (`Static [double scalar
RetVal, double scalar a, System.String label] RefOutReturn(double scalar a)`, `optional<int32
scalar> b`, `JGTest.Point lhs1 Point` for a struct's default constructor, `lhs1 plus(A, B)` for an
operator), the type's own lines by name and then the inherited ones in R2025b's order; asked for an
answer, `methods(x, '-full')` gives those lines as a cell. Any other value keeps its answer as before
(the cell, bound to `ans` as a statement).

**The fixture resets what it changes.** A .NET static lives as long as the process, and the test
process runs every fixture: `net_members` left `JGTest.Members.StaticField` at 8, which `net_basics`
then read. `net_members` now ends with `JGTest.Members.Reset()` and `JGTest.Resource.ResetCount()`,
which print nothing; it was re-recorded from R2025b and its recording did not change.

## Measured

Release CLI, warm, 20,000 calls per row, the best of three passes: `System.Math.Max(3, 4)` 9.5 µs
(17–20 µs in stage 1, before the groups were cached), an instance call `m.Add(1)` 3.2 µs (15 µs),
`[w, f] = JGTest.Modifiers.Split(2.75)` 2.3 µs, an optional left off 2.0 µs, a static property read
0.8 µs (3 µs) and an instance property write 1.3 µs. R2025b is not yet measured beside them (stage
10's timing row).

## What moves

- `net_members` agrees on all 71 lines (it stopped at its first line); `net_display` on all 22 with
  its 13 display divergences (`methods_list` and the three `methods_full_*` lines flipped).
  `net_basics`, `net_conversions` and `net_exceptions` still agree on every line. No line is pending
  on I2.
- Coverage: 424 of 514 builtins and 1,134 of 2,024 callables (`NET.setStaticProperty`,
  `NET.convertArray`; `NET.explicitCast` is not on the R2021b list); the interop section of the
  builtin coverage document is 27.
- Tests: `NetInteropM175Tests` (17).

## Consequences

- A script can call any public .NET method a MATLAB script can: `ref`, `out`, `params` and optional
  parameters, indexers, operators by name, interface members through a view, and statics written
  through `NET.setStaticProperty`. Stage 3 adds assemblies in full and `import`.
- `methods` of a user class, a struct or a number still answers its cell where R2025b prints a
  listing (`Methods for class HCirc:` and the names; `obj HCirc(r)` notation with `-full`), measured
  in the stage's probe and left for its own change: it is not .NET's.

## Divergences

- **`methods` fills its columns to a fixed 180-character width.** JGraph has no command window width;
  180 is what R2025b reports under `-batch`, where the fixtures are recorded. In the desktop, R2025b
  fills the window it has.
- **`methods('System.Math')` lists its seven instance names in one row.** R2025b puts them in two where
  the width allows one; no other type probed does so, and the rule every other listing follows is
  kept.
- **`[a, b] = methods(x)` is refused** (`MATLAB:maxlhs`); R2025b answers a second output it does not
  document.
- **Messages name a class without a hyperlink** — `class ''Members''` where R2025b's desktop message
  links the name. JGraph writes no hyperlinks anywhere; the fixtures reduce R2025b's links to text
  and agree.
