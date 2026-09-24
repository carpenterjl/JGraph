# ADR 0168 — A nested workspace is bound lexically

## Status

Accepted. Stage V7 of the value-ownership plan (`docs/plans/zfit-copies-and-temporaries-plan.md`),
rule M12, after V6's computed levels (ADR 0167). One commit, this ADR's, with two fixtures recorded
from R2025b before the interpreter was touched.

## Context

`UserFunction.CallMultiple` decided a call frame's boundary from its closure's flag:

```csharp
IsCallBoundary = _interpreter.Dialect.MatlabFunctions && !_closure.IsCallBoundary
```

so the flag alternated with depth. A file function's frame was a boundary; its nested function's
frame, closing over that boundary, was not; a function nested one level deeper, closing over a
frame that was not a boundary, was a boundary again — and its `v = v + [10 0]` stopped there and
declared a local. The three-level form left the outer `v` at `[1 2]` where R2025b answers `[11 2]`
(#36).

`clear` had a different fault. `clearvars` started at the current frame, but the named and plain
forms called `environment.Forget` on the workspace the builtin was registered in, so inside a
callback `clear victim` deleted the base workspace's `victim` and left the callback's (#64), a
plain `clear` emptied the base workspace (#65), and `clear p` inside the function that owns the
persistent `p` never reached the frame that declared it — the slot kept its value and the next call
read it (`p_clear_variable_in_owner`, V5's line owned by V7). `clear -regexp` was not a form at all:
the word went through the named road and forgot nothing.

Two smaller things came with them. `exist('g', 'var')` answered 0 for a global the frame had linked,
because the frame holds no value for a global and `exist` read the frame alone. And a nested
function's write of a name nothing bound yet — after a `clear v`, say — declared it in the nested
frame, where MATLAB gives it to the parent.

## Decision

**The fixtures first, and R2025b before the design.** `nested_workspaces` (39 lines): rebinding,
element, growth, deletion, field and cell writes from two, three and four levels of nesting; a
grandparent written from a nested function whose middle level never mentions the name; middle and
inner writing in turn; sibling nested functions, one calling the other; a name only a nested
function uses (its own, fresh each call, unseen by a sibling); a parent that assigns a name after a
nested function wrote it; a nested function's parameter and output of the parent's variable's name;
`global` declared in the parent and written below, and declared in the nested function alone;
`persistent` in the parent written below, and one of the nested function's own across two parent
calls; a nested function returned as a handle and called after its parent returned — a counter,
two instances of it, a writer and a reader over one workspace, an inner-of-inner; `nargin` and
`nargout` at each level; a local function called from a nested one, which is a boundary and sees
nothing; and every `clear` form inside a nested function. `clear_active_frame` (25 lines): `clear`,
`clear name`, two names, a missing name, `clear -regexp` in command and in function syntax, `clear
variables`, `clear all`, `clear global name`, and a plain `clear` of a global link — each inside a
`cellfun` callback with a same-named base variable that must survive, through
`evalin('caller', …)`, and in a script run from a function (`helpers/nw_clear_*.m`); a persistent
under each form inside its owner; and the base workspace's own `clear`.

Measured on the way, and shaping what follows: a parent that reads a name only a nested function
assigns is refused at parse time ("Identifier 't' is not a function or a shared variable. To share
't' with a nested function, initialize it in the current scope."), as is a `global` declared at
two levels ("Global or persistent variables must be declared in the same scope where they are
first used."), so neither is in the fixture. A nested function's `clear v` clears the parent's `v`
whether or not the nested function mentions `v` otherwise; its plain `clear` and `clear all` clear
the parent's variables. A plain `clear` inside a function unbinds its persistent and keeps the
value for the next call; `clear variables`, `clear -regexp` and `clear all` take the value. `clear
all` inside a callback takes the globals. A `clear g` of a global link unbinds the name in that
frame and the global keeps its value everywhere else. A nested function's parameters and outputs
are its own whatever the parent holds under the name.

**A frame is a boundary exactly when its function is not nested.** `UserFunction.IsNested` is set
where nested functions are hoisted (`Interpreter.ExecuteFunctionBody`, the one place a `FnStmt`
inside a function body becomes a callable) and nowhere else — file functions, script functions,
methods and constructors are not nested — and the frame's flag is
`Dialect.MatlabFunctions && !IsNested`. The parser's nesting decides, never the closure's flag.

Every consumer of the flag was re-read against the lexical rule (the plan's V7.1), and each held:
`JgsEnvironment.TryAssign` stops its walk at a boundary and now, from a nested frame, asks the
sharing rule below; `IsGlobal` stops a declaration's reach at a boundary, so a nested function
shares its parent's `global` and a local function does not; `IsInsideCall` walks to the nearest
boundary and tells the resolver a function definition is `Nested` rather than `Local`; the
`clearvars` walk stops at the boundary and every other `clear` form now walks the same frames;
`JgsClass`'s constructor frame and its default-value workspace are boundaries by construction; reads
walk parents (a nested frame, its parent's frame, the file scope, the built-in root) and never see
the base workspace, so they needed no flag; `assignin` and `evalin('caller')` use the dynamic
`CallerFrame`, which the lexical rule does not touch; `who`, `whos` and `exist` read the current
frame (a nested function's `who` lists its own frame's names, not the parent's — not measured, not
changed). The JGS dialect has no boundary and no nested functions, as before.

**A nested function's write of an unbound name lands where MATLAB's sharing rule puts it.** MATLAB
shares a variable between a nested function and its parents when both mention it, and the variable
belongs to the outermost function that does. `NameMentions` collects the names a function's own
body mentions — reads, assignment targets, loop variables, `global` and `persistent` names, its
parameters and outputs, what its anonymous functions capture — leaving the functions nested inside
it to their own sets, and caches them on the `FnStmt`. A call frame carries its `Function`;
`TryAssign`, reaching the boundary from a nested frame with the name unbound, declares it in the
outermost enclosing frame whose function mentions it (`TryAssignShared`), and otherwise answers
false so the write's own frame declares it, as before. Since a parent that only reads such a name is
refused at parse time, the rule decides one thing here: `clear v` in a nested function, then
`v = [7 8]` there, leaves the parent's `v` at `[7 8]` (measured). A nested function's outputs are
its own: the first write of one declares it in the nested frame, as its parameters were already
declared there.

**Every form of `clear` resolves in the active workspace** — the current frame and, for a nested
function, its parents' frames up to the boundary, which are the frames an assignment can reach
(`JgsRunner.DefineWorkspaceBuiltins`, `ActiveFrames`). Only the base workspace has pristine
bindings to revert to; a call frame's cleared name is gone. The forms:

- `clear name …`: the nearest active frame that binds the name — a local, a persistent it declared,
  a global it linked — forgets it, and the persistent's slot or the global link goes with it
  (`Forget` now drops the link; the global keeps its value). A name nothing binds is a no-op, and
  the name of an idle function file still unloads it (V5).
- `clear`: every variable of the active frames goes, with its packed buffers released under M6's
  rule; a persistent is unbound and its slot kept (`Unlink`), and the frames' global links drop.
- `clear variables`, `clear -regexp p …` (the patterns in command or function syntax): the same walk,
  taking a persistent's value; the regexp form keeps what no pattern matches and releases nothing.
- `clear all`: what `clear functions` did (unload idle files, forget idle persistents), then the
  globals (`ClearGlobals`), then the frames as `clear variables`.
- `clear global name …`: unchanged (V3b).

**`exist(name, 'var')` sees a global link** (`Interpreter.VariableExists`): a global the frame linked
is its variable while the global workspace holds one, and reads as cleared after `clear global`.

## What moves

- Flipped: `a036_nested_three_levels`, `a064_clear_named_in_callback`, `a065_clear_all_in_callback`
  (`value_isolation_calls`) and `p_clear_variable_in_owner` (`value_isolation_persist`), stamped in
  both lanes; the `persist` sidecar is removed (that line was its only claim) and the `calls`
  sidecar keeps its V8 and V9 claims.
- The two new fixtures agree on every line in both lanes; no sidecar.
- `check-ratchet` holds no V7 line.
- Found on the way, logged for later (appendix #171): MATLAB's command syntax takes any word, so
  `clear -regexp ^p$` is a call with two strings; this parser's command syntax ends at an operator
  character, and the fixture writes that pattern in function syntax. Not built here.

## Consequences

- A helper's `clear` no longer reaches out of the frame that ran it; a nested function's writes
  land in the workspace it was written in, at any depth; a persistent is cleared by the frame that
  owns it.
- Cost: a call frame carries one more bool and one reference; a nested function's write of an
  unbound name walks its parents once, against a name set computed once per declaration; `exist`
  pays one `IsGlobal` check. The allocation regression against the V2 baseline is in the plan's
  "As built (V7)".
- Tests: `NestedWorkspacesM168Tests`.

## Divergences

None recorded.
