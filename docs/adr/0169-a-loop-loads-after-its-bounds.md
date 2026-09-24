# ADR 0169 — A loop loads after its bounds

## Status

Accepted. Stage V8 of the value-ownership plan (`docs/plans/zfit-copies-and-temporaries-plan.md`),
rule M13, after V7's lexical workspaces (ADR 0168). One commit, this ADR's, with one fixture
recorded from R2025b before the interpreter was touched.

## Context

`Interpreter.ExecuteFor` tried the register compiler first. `TryExecuteHotLoop` compiled the loop
against the environment as it stood, then `TryLoadHotLoopEntry` loaded the vector slots' wrappers,
the scalar slots' values and the resolved builtins, and only then evaluated the range's three
bounds. A bound is an expression, and an expression may call a nested function that shares the
loop's workspace (ADR 0168) — so `for k = 1:resetx()` with `resetx` rebinding `x = 7 * ones(1, 5)`
ran the compiled body against the wrapper `x` had *before* the bound, wrote `x(1) = 1` into it, and
spilled nothing, because the slot was never dirty: the workspace's `x` was the new one. R2025b
answers `[1 7 7 7 7]`; this build answered `[7 7 7 7 7]` (#37). The boxed lane agreed with MATLAB
all along, because an unpacked vector never qualifies for a slot and the walk ran; the defect was
the packed lane's, and it was invisible to every test whose bounds had no effect.

Two smaller faults sat beside it. A loop whose bounds carried a class (`int16(1):int16(4)`) was
refused after the compiled road had evaluated them, and the walk evaluated them again — a bound
with an effect ran twice. And a loop that ran no pass left its variable as it was, where MATLAB
binds it to `[]`.

## Decision

**The fixture first, and R2025b before the design.** `loop_bound_effects` (21 lines): a bound that
rebinds, grows, clears or demotes a vector the body writes; a step that rebinds one; a stop that
grows the vector the start already measured; a bound that rebinds, clears or promotes a scalar the
body reads, and one that demotes a vector the body reads whole; start, step and stop each with an
effect, in the order written, once, whether the loop then runs or not; a bound that throws after
an earlier bound ran; a stop that reads the loop variable's prior binding; a bound that rebinds the
loop variable; the zero-trip loop's variable over a range and over every empty head; a 0-by-3
source; an inner loop's bound with an effect on every pass of the outer; and a bound that assigns a
builtin's name into the loop's workspace, in a script and in a function.

Measured on the way: MATLAB evaluates start, then step, then stop, once each, and a stop that
throws leaves the start's effect done and the body unrun. A zero-trip loop binds its variable to
the 0-by-0 double whatever the variable held and whatever the head was — an empty range, a
descending one, an integer range, `[]`, `zeros(1, 0)`, `{}`, `''`, `strings(1, 0)` — while
`zeros(0, 3)` is not a zero-trip loop but three passes over 0-by-1 columns. Inside a function,
`abs(k)` is bound to the builtin when the function is parsed, and an `assignin('caller', 'abs', …)`
from a bound does not change what the body calls (`[1 2 3]`); in a script the same bound shadows
the builtin and the body indexes the variable (`[4 5 6]`).

**The bounds run first, once, and the loop reads what they left.** `ExecuteForOverRange` evaluates
the three bounds in the order written, converts them (`RangeBoundValue`, carrying the class) and
counts the range (`HotLoopRangeCount`, the walk's own errors), and holds the result as one
`RangeSteps`. Only then does it offer the loop to the compiler, and `TryExecuteHotLoop` takes the
steps as an argument: the compiler's seeding of vector and variable names, the entry check's
loading of every slot, its global check on each name, its resolution of every builtin the program
bound — all of it now reads the environment as the bounds left it. A refused entry hands the same
`RangeSteps` to `ExecuteForOverSteps`, so the walk never evaluates a bound the compiled road already
ran; a class-carrying range walks the same way, without the second evaluation; and a bound that
throws throws before either road is chosen, once. The `while` road is unchanged — it has no bounds
— and passes no steps.

**V8.1, every other read the compiled entry makes before the first iteration, audited for the same
order:** the step (one of the three bounds; evaluated second, as MATLAB does); the loop variable's
prior binding (read by nothing on the compiled road — the compiler marks the variable assigned
before it scans the body, so the slot is never `EntryRequired` — and read by the bounds themselves
on both roads, before the loop binds it: `k = 3; for k = 1:k + 1` runs to 4); the `JGRAPH_LOOP_JIT`
gate, the debug hook and the dialect checks (process and session state a bound cannot move, read
before the bounds and rightly so); the cached program (`_loopPrograms`, keyed by the statement, so
a program compiled on an earlier call is re-validated by the entry check against this call's
post-bound environment — which is how a second call whose bound demotes a vector or shadows a
builtin is refused); and the range count itself, including the over-limit and wrapped-count
errors, which now come from the one evaluation. Nothing the entry reads precedes the bounds any
more.

**Each case names its road** (`LoopBoundM169Tests`), through the compiler's counters: `CompiledRuns`
for a bound whose effects leave the body's vectors and builtins valid (the rebind, the growth, a
rebound scalar); the new `JgsLoopJit.EntryRefusals` for a bound that leaves a slot's value the
wrong shape or unbound, or a builtin's name bound to a variable — counted on the second of two
calls, where the cached program's entry check runs after the spoiling bound; and neither counter
for a bound that throws. Every case runs with the compiler forced on and off and prints the same
bytes.

**A zero-trip loop binds its variable to `[]`** (`BindZeroTripVariable`): the range road, when the
count is zero, before any road runs; and the array, cell, char and struct-array walkers when they
have no pass to make. The binding is `EmptyBracket()`, the `[]` the dialect writes. A `for` nested
inside a compiled program has no walker to do it, and a register cannot hold an empty: its
`ForHead`, finding no pass at index zero, marks the variable's slot (`HotLoopVectors.Emptied`), the
spill declares `[]` for a marked slot, the reload keeps a marked slot whose binding is still the
empty, and every bind of the slot (`ForBind`, `ForStep`, `Bind`, `BindVar`) clears the mark. That is
sound while no op reads the register under the mark, so the compiler refuses a statement that
reads a nested loop's variable anywhere but inside that loop's own body — before it, in its
bounds, or after it — which makes it a walked statement (ADR 0160, 12d): the spill declares the
empty, the walk reads it, and the loop stays compiled. A sparse-row loop whose inner range is often
empty pays one store per head, not a bail.

## What moves

- Flipped: `a037_loop_bound_rebinds` (`value_isolation_calls`), stamped in both lanes; its row is
  gone from the `calls` sidecar (the V9 rows remain) and from the boxed overlay, where it was the
  overlay's only line.
- The new fixture agrees on 20 lines in both lanes; the 21st is the divergence below.
- Retired: ADR 0160's "the loop variable after an empty range" (`m160_loops`, `empty_range_new_var`),
  which the zero-trip binding closed — the line is `exact` now, the fixture re-recorded for its rule
  field and re-stamped for its two remaining `div=ADR0160` lines, the bullet deleted from ADR 0160
  with a retirement note, and the M98 test renamed `LoopVariableIsEmptyAfterAnEmptyRange`.
- `check-ratchet` holds no V8 line.

## Consequences

- A loop's body sees the workspace its bounds left, on both roads; a bound runs exactly once; a
  loop that runs no pass leaves `[]` in its variable.
- Cost: one bool store per bind op on the compiled road (the mark cleared), one bool array per
  program entry, and one compile-time set walk. One struct of five fields per loop entry instead of
  three doubles and a class on the stack; the class-carrying range no longer pays a second
  evaluation of its bounds. A statement that reads a nested loop's variable outside its loop is
  walked where it compiled before. The allocation regression against the V2 baseline is in the plan's
  "As built (V8)".
- Tests: `LoopBoundM169Tests`.

## Divergences

Stamped `div=ADR0169` with this build's output, so an output that moves — or comes to agree, which
retires the line and this entry — fails the fixture.

- **A builtin's name assigned into a function's workspace by a bound is a variable to the body**
  (`lb_shadow_ignored_in_function`, `loop_bound_effects`). After `assignin('caller', 'abs', [4 5 6])`
  from the stop, `y(k) = abs(k)` indexes the variable here and answers `[4 5 6]`; R2025b bound
  `abs` to the builtin when it parsed the function and answers `[1 2 3]`. A name is resolved when
  it is read (ADR 0149's layers, variables first), on every road, and the compiled road's entry
  check refuses a program whose builtin the workspace now shadows rather than call what the body
  would not. In a script the two agree.
