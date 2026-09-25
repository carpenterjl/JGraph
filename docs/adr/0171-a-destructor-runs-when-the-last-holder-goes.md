# ADR 0171 — A destructor runs when the last holder goes

## Status

Accepted. Stage V10 of the value-ownership plan (`docs/plans/zfit-copies-and-temporaries-plan.md`),
after V9's call contract (ADR 0170). One commit, this ADR's, with one fixture recorded from R2025b
before the interpreter was touched.

## Context

Nothing here ever ran a destructor. `onCleanup` did not exist (#29). A `handle` class's `delete`
method ran on an explicit `delete(obj)` and never otherwise: not when the name that held the
object was cleared (#88), not at the frame's exit (#89), not when a cell, a struct or an anonymous
snapshot that held it was released (#154), not when an escaped nested workspace went (#93), and
`clear c` inside a function cleared the name and kept the object (#94). A listener made by
`listener` lived with its source, as `addlistener`'s does, instead of ending with its last handle
(#107). M1's holder counts (ADR 0162) could not say when a last reference went: they over-state by
design — a dead wrapper never decrements — which is what makes them safe for copy-on-write and
useless for a destructor.

## Decision

**The fixture first, and R2025b before the design.** Two probe rounds (60 cases), then
`destructor_lifetime` (53 lines, `helpers/DeleteHolder.m`, `DeleteThrower.m`, `PropOrder.m`). Measured,
against the plan's own assumptions, and built to:

- A frame's exit releases its variables in **first-declaration order** — `z; a; m` destroy as
  `Z;A;M`, and a rebound name keeps its first slot (`m = M1; a = A; m = M2` destroys `M1` at the
  rebinding, then `M2;A` at the exit) — not the reverse order the plan had assumed. `clear` and
  `clear variables` release in the same order; `clear c a` in the named order. R2025b's `-batch`
  exit destroys the base workspace by name (`x`, `y`, `c` as `c;x;y`).
- A container releases its direct handles first, in index or field order, then its nested
  containers: `{A, {B, C}, D}` destroys as `A;D;B;C`, `st.c = {A, B}; st.d = D` as `D;A;B`; a struct
  array's elements in index order; a handle's `delete` runs before its properties are released
  (`H;I`), and an explicit `delete(h)` releases them at once (`H;I;J` before the next statement).
- A temporary dies at the end of its statement: `use_tag(DeleteLogger('T')); vlog('after')` logs
  `usedT;T;after;`; a statement's bare call binds `ans`, which holds the object until `ans` is
  rebound; `[~] = make_h()` destroys the output with the statement; an output the caller keeps
  survives the frame's exit (`Z;A;kept;H`).
- An error unwinds the frame before the `catch` runs (`done;caught`). A task or `delete` that fails
  is the warning `MATLAB:class:DestructorError`, "The following error was caught while executing
  '<Class>' class destructor:" and the error's report, and the object is deleted all the same.
- An alias outlives `clear` of the original; an anonymous snapshot holds what it captured until the
  handle is cleared; a nested function's handle keeps its workspace alive until the last such handle
  goes (two readers of one `c`: `1;one;1;done`), a handle held only by the workspace itself is no
  escape (`done;after`), and `clear c` in a nested function releases the parent's `c` at once.
- `delete(obj)` then `clear obj` runs nothing twice; `isvalid` answers false through every alias
  after `delete`; a read through a deleted handle is `MATLAB:class:InvalidHandle`.
- A Map's entries go with the Map; a global goes with `clear global`, not with `clear` of the link;
  a persistent's value goes when the persistent is reset; a listener made by `listener` ends with its
  last handle (`L;`), one made by `addlistener` lives with its source (`L;L;`).

**The exact count** (`JgsLifetime`). Beside M1's count, every handle object carries an exact holder
count on its instance from birth; a cell or boxed array, a struct array, a value object, an
anonymous function's snapshot and a nested function's workspace carry one once they are *scanned*
— built by a factory holding a handle, or holding anything tracked while something with a destructor
is alive (`AnyLive`), or written a tracked value through a store gate, which scans the rest of the
container then. The count moves only where a holder starts or stops holding: `JgsEnvironment`'s
stores (`Store`/`Drop` behind `Declare`, `DeclareFunction`, `TryAssign`, `Forget`, `Unlink`,
`RetainOnly`, `DeclarePersistent`), the container gates (`SetSlot`, the field and property stores,
a Map's rebuilt value cell), the factories (`Minted`), M3's shallow detach (`Detached`: the copy
holds every child once more, the old payload loses this holder), a frame's exit (`FrameExited`) and
a handle to a nested function escaping its workspace (`Escapes`; one bound inside the workspace or
its nested frames is no escape, a snapshot's is). An unscanned container's children were never
counted for it, so its death releases nothing: a leak in the safe direction, never a destructor
under a live name. Stores outside the model — appdata, `guidata`, a timer's and an addlistener
listener's properties — pin what they take. A numeric array is never walked: a boxed array is
scanned for handles at construction (a type test a slot), for anything else only while a destructor
is alive.

**The zero is a check, not an act.** A count that reaches zero queues a check keyed by the block
depth of the running statement; `Interpreter.Tick` drains the checks made at its depth or deeper
before the next statement. What is still held by then (a callee's output the caller has bound, a
loop's cell literal while the loop runs — a loop's own pass boundary counts as the body's depth) is
left alone; what nobody holds is destroyed: a handle's `delete` (asked for nothing, through the same
`DestructorCall` an explicit `delete` uses; a handle without one, and with listeners, is marked and
raises `ObjectBeingDestroyed`; one with neither is unobservable and only releases what it held), then
its properties, direct handles first. A frame's exit queues the frame itself, so its variables go at
the caller's next boundary; a workspace with escaped handles waits for the last of them. The run's
end drains everything and then destroys the base workspace by name. A check that finds its payload
already released does nothing (`Dead`, `Released`, `FieldsReleased`).

**`onCleanup`** is the class MATLAB's own `onCleanup.m` is — `classdef onCleanup < handle` with a
`task` property and a `delete` that calls it — defined from source on first use
(`JgsBuiltins.Cleanup.cs`), so everything above holds for it with no special road.

## What moves

- Flipped, stamped in both lanes: `value_isolation_lifetime` a029 ×2, a088, a089, a093, a094,
  a154 ×7; `value_isolation_events` a107 — the 14 lines V10 owned. Their `.owners` rows are gone.
- The new fixture agrees on every line in both lanes: no divergence.
- `check-ratchet` holds no V10 line.

## Consequences

- A destructor runs where R2025b runs it, in R2025b's order, for a handle class's `delete`, for
  `onCleanup`, and for a `listener`.
- Cost: one type test per binding and per slot of a container a factory builds (the handle test),
  a count and a queued check per holder of a handle or a scanned container, and a walk of a frame's
  variables at its exit while a destructor-bearing object is alive or the frame bound a handle or a
  function. The allocation regression against the V2 baseline is in the plan's "As built (V10)".
- Not modelled, and not in the fixture: a value still in flight in an enclosing statement when a
  callee clears it through `evalin('caller', 'clear x')` or a nested function's `clear x` is destroyed
  at the callee's next statement (R2025b keeps the argument alive until the statement completes: `r =
  uses_tag(c, clear_c_in_caller())` logs `h;C;C1;`, this build would read a deleted object); a tracked
  value stored by a builtin through a road the audit lists as a raw fresh fill is held by that
  container without a count (it leaks, it is never destroyed early); a cycle through a container
  holding a nested handle to the workspace that holds the container is never destroyed.
- Tests: `DestructorLifetimeM171Tests`.

## Divergences

None recorded by this stage.
