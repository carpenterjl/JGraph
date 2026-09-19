# ADR 0166 — A persistent variable is one binding

## Status

Accepted. Stage V5 of the value-ownership plan (`docs/plans/zfit-copies-and-temporaries-plan.md`),
rule M9, after V4's dotted writes (ADR 0165).

## Context

`persistent p` copied. `ExecutePersistent` declared the function's kept value into the frame as a
local, and `SavePersistents`, run from the call's `finally`, copied the frame's local back into the
function's slot. Between the two the frame's `p` and the slot were different bindings, so

```matlab
function r = pcount(depth)
persistent p
if isempty(p), p = zeros(1, 3); end
p(1) = p(1) + 1;
if depth > 0, pcount(depth - 1); end
p(2) = p(2) + 10;
r = p;
end
```

answered `[1 10 0]` for `pcount(1)` where R2025b answers `[2 20 0]` (#17, and #18 for the
`p = p + …` form): the inner call read the slot as it was before the outer call started, and the
outer call's return overwrote what the inner one had kept. Every re-entry had the same shape — a
callback that calls the owner, a function handle to it, mutual recursion, a nested function — and
an error thrown inside a recursion lost every level's updates but the outermost's.

Clearing had the opposite fault. `clear functions` and `clear all` dropped *every* function's
persistents and unloaded *every* file, the running ones included, so `clear_active(1)` — which
clears and then calls itself — answered 1 where R2025b answers 2 (#72): MATLAB does not clear a
function that is executing.

## Decision

**The fixture first.** `value_isolation_persist` (49 lines, recorded from R2025b): recursion with
every write shape (element, rebinding, growth, `end + 1`, deletion, a cell slot, a struct field, a
nested struct's element, two names in one statement, a multiple assignment, `eval`, a value
object's property), re-entry through `cellfun`, `arrayfun`, a function handle and mutual recursion,
errors after an update and inside a recursion, two functions' persistents and a global of one
name, a nested function reading and writing its parent's persistent and keeping one of its own,
the isolation of a copy of the slot (an alias, a returned value, an argument, an anonymous
function's capture), a handle object, loops — compiled and not — over a persistent, what `who` and
`exist` say, and `clear` against idle, running, suspended and script-local functions (function
files `helpers/vp_*.m`, because a script's own functions are never cleared while it runs). On V4's
binary 29 of the 49 disagreed.

**The frame records where the name lives; it holds no value.** `JgsEnvironment.DeclarePersistent`
marks the name in the declaring scope with the owner's slot workspace (a `JgsEnvironment` per
function declaration, where a dictionary of values was). `TryGet`, `TryGetScope`, `TryAssign`,
`Declare`, `Forget`, `Contains`, `DeclaresLocally` and `IsFunctionBinding` follow the mark, so
every road that reads or writes a name — plain and indexed writes, V3b's store-back, the dotted
roads V4 moved to `LookUp`, `eval`, `exist`, multiple assignment, a loop variable, the compiled
loop's entry load and exit store, a nested function walking to its parent's frame — reaches the
slot at once with no road learning anything. `SavePersistents` and its `finally` call are gone.
The slot is an entry (M2): a copy taken from it shares and the next write detaches in the slot.

The plan proposed redirecting the way `global` is redirected, in the interpreter (`LookUp`,
`ScopeOf`, the resolver). V4 had just shown the cost of that design — four roads that read
`env.TryGet` and never learned about globals — and a persistent has no workspace of its own for
an interpreter-level rule to name: which slots a name means is a fact about the frame that
declared it. So the environment answers it. A global could move the same way later; nothing here
depends on it.

`JgsEnvironment.Variables` lists a scope's locals and its persistents' current values; `who`,
`whos`, `save`, `clearvars`, the REPL's listing and the debugger's variable view read it, so a
persistent still shows in its function's workspace.

**A clear spares what is running.** The interpreter keeps the functions with a frame on the call
stack (`_activeFunctions`, pushed on entry and popped in the call's `finally`, so re-entry and
callbacks count) and the script files that are running (`_runningScripts`). `clear functions` and
`clear all` reset the persistents of, and unload, only files that are idle — `IsFileActive` —
because a re-read makes new declarations and a function's slots are keyed by its declaration:
unloading a running file would hand its next call a fresh set. `clear name` on a function file
now clears the function (it cleared only a variable of that name before): persistents reset and
the file re-read, unless it is running. A running script's own functions are never reset, as in
R2025b (`p_clear_functions_local_function`: 3).

The first read of a persistent is `[]`, 0-by-0 — it was 1-by-0.

## What moves

- Flipped: `a017_persistent_recursive_elem`, `a018_persistent_recursive_rebind`,
  `a072_clear_functions_active_persistent` (value_isolation_calls), and 48 of
  `value_isolation_persist`'s 49 lines, 28 of which disagreed before. No boxed overlay.
- `p_clear_variable_in_owner` is V7's: `clear p` inside a function forgets the name in the base
  workspace, not the active frame (#64). `Forget` already does R2025b's thing to a persistent once
  the clear reaches the right frame — the variable and what it kept both go.
- `check-ratchet` holds no V5 line.
- Found on the way, local forms, both logged for V6: `q = 1; q(3) = 5` is refused on the one- and
  two-subscript roads (appendix row #162; the N-subscript road already promotes the scalar), and
  `mat2str(-0)` writes `-0` where R2025b writes `0` (#163). The fixture avoids both.

## Consequences

- A memoising or counting function can call itself, be called back, or fail partway, and keep
  what it wrote.
- A call pays one list push and pop for the active-function record; a name lookup pays one null
  check per scope walked.
- Tests: `PersistentBindingM166Tests`.

## Divergences

None recorded.
