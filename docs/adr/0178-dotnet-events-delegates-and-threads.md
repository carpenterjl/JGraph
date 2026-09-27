# ADR 0178 — .NET events, delegates and the threads they arrive on

## Status

Accepted. Stage I5 of the .NET and shared-library interop plan
(`docs/plans/net-and-shared-library-interop-plan.md`), after stage I4 (ADR 0177, 5655a79). One
commit, this ADR's.

## Context

Stage I4 left two lines pending on this stage: `net_events_delegates` stopped at its first
`addlistener` ("First argument provided is not valid for addlistener"), and `net_display`'s
`events_disp` echoed a cell where R2025b prints a listing. The fixture's forty recorded lines pin the
shape: listeners on a .NET event, newest first, `delete`, `Enabled`, the `addlistener`/`listener`
lifetimes, R2025b's refusals, a failing callback as a warning, events raised on a thread-pool thread
delivered at `pause` and `drawnow` and never during a busy loop, and function handles as `Func`,
`Action` and custom delegates.

Eight R2025b probes (`probe5a`–`probe5h`, scratch, not committed) settled the rest:

- **Events.** The refusal identifiers. One .NET handler per event, taken off when the last listener
  goes. The listener's properties, and `Recursive`. `ObjectBeingDestroyed` on a .NET object, and what
  a deleted .NET handle refuses. `drawnow nocallbacks` and `limitrate`. `pause` in a callee, inside a
  callback and inside `evalc`; statements and figure creation are not drain points.
- **Delegates.** Constructor refusals. How a handle's answer converts. Output counts for `void`,
  `ref` and `out`. Errors in the handle. `Combine`, `Remove`, `DynamicInvoke`, `EndInvoke`.
  `createGeneric` of `Func` and `Action`. A delegate a pool thread invokes (`ApplyLater`, a method
  added to the test assembly for this).

R2025b crashed twice (access violation) when a queued pool event reached a listener that had been
disabled (`probe5d`) or deleted (`probe5e`) since it was queued. It delivered one of two queued events
at `pause(0)` in one run and none in another. It hangs for good in the deadlock case, as step 0
found. What the probes settled became 127 new fixture lines, recorded from R2025b with the fixture.

## Decision

**A listener on a .NET event is the `event.listener` a classdef source gets.** It is the same struct
and the same `JgsListener`, with the same `Enabled`, `Recursive`, `Callback`, `delete` and
`isvalid`. An `addlistener` listener survives `clear lh`; a `listener` one ends with its last handle.
A `NetEventSubscription` per object and event holds the listeners in place of a classdef object's own
list. It adds one handler to the .NET event when the first listener arrives and removes it when the
last one goes (R2025b: `FiredHandlerCount` is 1 with two listeners and 0 after both are deleted).

The handler is compiled over the event's own delegate type, which must have R2025b's standard shape,
`void (object sender, TArgs e)` with `TArgs` an `EventArgs`. Any other shape is
`MATLAB:NET:UnsupportedDelegateType` for both `addlistener` and `listener`. A callback receives the
sender (the listener's own source value when it is the object listened to, so `s == p` holds) and the
arguments as the declared type answers them (`e.Value` of a `CustomArgs`), newest listener first. A
failing callback is the `MATLAB:callback:error` warning, as for a classdef source.

R2025b's refusals, each with its identifier, now also on the classdef path where the sentence was
already R2025b's:

| Case | Identifier |
|---|---|
| A value type or a name as the source | `addlistener:invalidinput` |
| A deleted handle | `class:InvalidHandle` |
| Two or five arguments | `minrhs` / `maxrhs` |
| A callback or event name of the wrong kind | `class:InvalidInputArgumentTypeSizeOrValue` |
| An event the type does not declare (a static one included) | `class:invalidEvent` |

`events` takes a .NET type by name. As a statement on a .NET object or type it prints R2025b's
listing ("Events for class T:" and the names), where it used to echo the cell.

**Work .NET asks for from another thread waits for the script thread.** Each script thread has a
`NetCallbackQueue`, found through a thread-static. An event raised on another thread posts its
listeners there and returns. A delegate invoked on another thread posts its call and blocks that
thread until the call has run. The queue drains at R2025b's drain points:

- `pause` with a positive time, `drawnow` (and `limitrate`) and `getframe`;
- the app's idle prompt: `ScriptEventQueue.HasWork` now counts .NET's work, and the REPL's pump run
  drains it;
- never between statements, never at `pause(0)`, never under `drawnow nocallbacks`, never at figure
  creation.

A callback's own `pause` drains too, re-entrantly (probe5f: `Pin ; Pool 1 ; Pool 2 ; Pout`). An
event or delegate raised on the script thread itself, inside a .NET call the script made, runs at
once. A queued event reaching a listener deleted or disabled since is dropped.

**A function handle becomes any delegate.** A handle reaches a delegate-typed parameter first.
`JGTest.Unary(@f)`, `System.Action(@f)`, `System.EventHandler(@f)` and
`NET.createGeneric('System.Func', {…}, @f)` make one. Anything but one handle is
`dispatcher:noMatchingConstructor`, or `NET:GenericObjectCreationError` through `createGeneric`
(naming ``System.Func`2``).

The delegate is compiled over the type's own `Invoke`, so .NET can combine, remove and invoke it like
any other. It asks the handle for the return value, when there is one, and then for one output per
`ref` or `out` parameter (`@(a) a + 1` for `RefOut` is `maxlhs`). `out` parameters are not inputs.
An `Action` asks for nothing; `showout` sees `nargout` 0.

The answer converts as a property write converts a value:

| Answer | Identifier |
|---|---|
| A vector or a char row | `class:RequireScalar` |
| One char, a cell or a 1x1 struct | `class:RequireNumeric` |
| A complex number | `class:RequireReal` |
| `[]` | `NET:NetConversion:UndefinedConversion` |
| A number where a string is wanted | `NET:NetConversion:StringConversion` |

A script error in the handle (or in that conversion) crosses the .NET frames wrapped and comes out of
the script's .NET call as itself, as it does in R2025b (`my:named`, not a `NET.NetException`).
`d(x)` on a delegate is its `Invoke`, and `a();` as a statement asks for nothing.

**Deadlock is detected.** The script thread counts its depth in .NET calls, which is zero again while
a callback runs inline. A delegate waiting on another thread that sees the script thread blocked in
.NET for `NetCallbackQueue.DeadlockTimeout` (3 s) withdraws its request and throws. The .NET call the
script is blocked in then fails with `JGraph:NET:DelegateDeadlock`. This covers a method that waits
on the thread (`ApplyOnThread`) and a property that does (`task.Result`). Nothing is left waiting:
the next call from another thread runs at the next `pause`.

**`delete` of a .NET handle.** Its `ObjectBeingDestroyed` listeners run with an `event.EventData`
while the object is still valid (probe5a: the callback reads `s.ToString()`). Then that handle, which
every name for it shares, is invalid: `isvalid` is false; a member, a conversion, passing it to .NET
and `addlistener` are `class:InvalidHandle`; `class`, `isa`, `events`, `==` and `isequal` still
answer; the display reads "handle to deleted Publisher". The .NET object is untouched, with no
`Dispose` (ADR 0175), and a new wrapper of it that .NET hands back is valid.

**Three general fixes the fixture needed:**

- R2025b's `evalc` answers the warnings its code raised (probe5w). Warnings now go through
  `JGraphScriptGlobals.WriteWarning`, which writes into an open capture.
- A MATLAB anonymous function takes fewer arguments than it names. A missing one the body reads is
  `MATLAB:minrhs` "Not enough input arguments.", and more than it names is `MATLAB:TooManyInputs`
  "Too many input arguments."; JGraph refused both in words of its own. JGS keeps its strict arity.
- `[]` reaches any reference-type parameter as `null` after everything the recorded row lists
  (`u.EndInvoke([])` calls and fails in .NET, as in R2025b).

## Measured

Release CLI, warm, 20,000 calls per row, the quietest of three runs on a loaded machine:

| Call | Cost |
|---|---|
| `p.RaiseSync()`, a synchronous event with one listener | 3.0 µs |
| `JGTest.Invoker.Apply(@(x) 2 * x, 3)`, a new delegate each call | 10.4 µs |
| `f.Invoke(3)` on a delegate made from a handle | 5.6 µs |
| `d(21)` on a delegate .NET made | 1.6 µs |
| `pub.Name`, for comparison | 1.4 µs |

The delegate row was 355 µs on the first build, which compiled an expression per delegate. Each
delegate type, and each event-handler type, now compiles one factory, once.

A script that makes no delegate and no listener pays one thread-static read and an interlocked
increment and decrement per .NET call (the depth count), and one null test per drain point.

## What moves

- `net_events_delegates` agrees on all 167 lines. The 40 lines recorded in step 0 came back
  unchanged; the other 127 are this stage's probes. `net_display` agrees on all 22 (`events_disp`
  was pending on I5). No line is pending on I5; the ratchet's pending lines are I8 1 and I9 3.
- New JGraph-only fixture `interop_deadlock_detected` (3 lines): its cases are MATLAB code in
  `helpers/ix_deadlock.m`, run from JGS.
- The test assembly gains `Invoker.ApplyLater`, a delegate another thread invokes without the
  caller waiting. `ApplyLater`, `ApplyOnThread` and `Publisher.RaiseOnThreadPool` use threads of
  their own rather than `Task.Run`. Under a busy pool (the parallel test lanes) a waiting
  `Task.Result` ran a task that had not started inline on the script thread, so the deadlock lines
  passed without any thread crossing, and a pool slow to start could hold events back past a
  script's `pause`. The fixture was re-recorded after the change and answered the same.
- Tests: `NetInteropM178Tests` (17). `MatlabCellStructTests` now expects R2025b's "Not enough input
  arguments." for `n = @(a, varargin) a; n()`, and `MatlabSparseKrylovM128Tests` its "Too many input
  arguments." for `bicg` calling a one-argument handle with the direction flag.

## Consequences

- A MATLAB script can listen to .NET events, pass function handles wherever .NET takes a delegate,
  and receive .NET's callbacks from other threads at the points R2025b delivers them. Stage I6 adds
  the inline C# helper.
- Every drain point now also drains .NET's queue. A host that pumps events idly must ask
  `ScriptEventQueue.HasWork`, not `Count`.
- Any warning raised under `evalc` lands in its answer, for classdef, timer, destructor and file
  warnings too.

## Divergences

- **A cross-thread delegate deadlock is an error after a timeout.** R2025b hangs for good.
  `JGraph:NET:DelegateDeadlock`, pre-registered in the plan.
- **A queued event reaching a listener disabled or deleted since is dropped.** R2025b crashes with an
  access violation (probe5d, probe5e).
- **A delegate a pool thread invoked that fails in the script faults its task with JGraph's own
  exception.** R2025b faults it with `MathWorks.MATLAB.Interface.UnmanagedDelegateException` and
  prints the error; JGraph prints nothing. The fixture checks `IsFaulted` only.
- **`events` as a statement prints its listing only for .NET.** A classdef object's `events` still
  echoes the cell; R2025b's classdef listing was not probed.
- **A missing anonymous-function argument the body mentions is refused when the call starts.**
  R2025b refuses it only when the read happens (`@(a, b) a || b` with one true argument answers
  there).
