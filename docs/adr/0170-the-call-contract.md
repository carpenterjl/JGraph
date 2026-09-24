# ADR 0170 — The call contract: outputs asked for, call site handed on

## Status

Accepted. Stage V9 of the value-ownership plan (`docs/plans/zfit-copies-and-temporaries-plan.md`),
after V8's loop bounds (ADR 0169). One commit, this ADR's, with three fixtures recorded from R2025b
before the interpreter was touched.

## Context

A call had one shape here whatever the syntax asked for. `IJgsCallable.Call` meant "one output", and
every road took it: a statement `f();` asked for one, so a callee branching on `nargout` saw 1 on all
four forms of #77 (`f();`, `h();`, `feval(h);`, `feval('f');`), a `varargout` function asked for
nothing produced one and `ans` was bound (#81), and `load` — whose whole distinction is whether
anyone wanted the struct — could not tell `S = load(fn)` from `load(fn)` through a handle, `feval`
or an anonymous body, so it bound the loaded names into the workspace on every road (#109, #144)
and into an anonymous function's static workspace without the refusal R2025b makes (#145). The
call site reached the frame on the named road alone: `[a, b] = h(x)` and `feval(@f, x)` left
`inputname` refused or empty (#78, #79). And a call asked for more outputs than the callee has ran
the callee anyway — `y = disp(1)` bound nothing in silence, `y = error(...)` raised the error it
was never allowed to reach (#55). The unassigned-output refusal (#84) had its own wording.

## Decision

**The fixtures first, and R2025b before the design.** Four probe rounds, then three fixtures:
`nargout_propagation` (76 lines: the count on every road and the `ans` a statement binds),
`inputname_roads` (34 lines) and `output_count_checks` (50 lines). Measured on the way, and
built to:

- A statement call asks for zero outputs on every road, and `ans` takes the first output the
  callee made anyway — a named output it assigned, `varargout{1}` it set, a builtin's value — on
  every road too: `two();`, `h = @two; h();`, `feval(@two);`, `g = @() two(); g();`,
  `cellfun(@np, {1});` all bind `ans`; a function that assigned no output binds nothing and is not
  refused. (The probe that said a handle binds nothing was checking `ans` in the wrong workspace;
  the fixture, which checks the caller's, is the record.)
- `cellfun`, `arrayfun` and `structfun` hand their own count on: as a statement each element's
  function sees `nargout` 0 and what it hands back anyway is collected for `ans` under the uniform
  rule as ever (`cellfun(@np, {1});` binds `ans`), while an `ErrorHandler`'s answer is not collected
  when nothing was asked (measured: a handler answering a struct raises nothing);
  `r = cellfun(...)` asks for one; `[a, b] = cellfun(...)` for two.
  `eval('np();')` and `evalin('caller', 'np();')` run the text's last line as the statement it is;
  `x = eval('np()')` asks the expression for one. `builtin('feval', @np);` asks for none.
- `inputname` reads the site of the call that opened the frame: a named call, a handle (`h(x)`,
  `[a, b] = h(x)`), `feval` by handle and by name (the written `feval(f, x)` minus `f`), a handle
  from `str2func`, one held in a cell or a struct, `eval`'s text, a nested function and a nested
  function's handle all answer `x`; an anonymous body's own call is the site (`@(q) in1(q)` answers
  `q`, `@(q) in1(x)` answers the captured `x`, `@(q) in1(q + 1)` answers ''); a dotted method call's
  receiver is its first argument (`b.inp(x)` answers `b` then `x`, as `inp(b, x)` does); a callback
  a builtin makes with no syntax of its own — `cellfun`, `arrayfun`, `structfun`, an
  `ErrorHandler` — answers ''; an index past the arguments answers ''; index zero is refused
  (`MATLAB:inputname:argNumberNotValid`); a script is refused
  (`MATLAB:inputname:notSupportedInScript`), a script run from a function included.
- A call asked for more outputs than the callee has is refused before the callee runs: a user
  function — local, nested, a method, through a handle, `feval`, an anonymous body, `cellfun`,
  `eval`, a `varargout` relay, `hold` and the other names MATLAB keeps as files — as
  `MATLAB:TooManyOutputs`; a built-in (`disp`, `error`, `clc`, `save`, `clear`, `assignin`,
  `rethrow`, `drawnow`, `max` asked for three, `builtin('disp', 1)`) as `MATLAB:maxlhs`, both
  "Too many output arguments." When: a direct call of a user function refuses before its arguments
  are evaluated (`x = none_out_arg(bump())` never runs `bump`); a builtin, a handle, `feval` and an
  anonymous forward refuse after them (`bump` ran), while the anonymous body's own direct call
  refuses before its own. `[a, b] = f(1)` for `f = @(x) x` is the shortfall
  (`MATLAB:needMoreRhsOutputs`), not this. `nargout` reports no output for `pause`, and R2025b
  still binds `x = pause(0)`; `x = beep` too, and `nargout('beep')` is 1.
- The unassigned-output refusal is `MATLAB:unassignedOutputs`: `Output argument "a" (and possibly
  others) not assigned a value in the execution with "<name>" function.`, where the name is
  `file>function` for a local function, the bare name for a file's main function,
  `file>outer/nested` for a nested one, `Class/method` for a method, and `varargout{k}` names the
  missing element of a short `varargout`; a `varargout` function that never made the cell is `One
  or more output arguments not assigned during call to "varargout".`
- `load` binds the loaded names only when asked for none: a statement, `h = @load; h(fn);`,
  `feval(@load, fn);`, `builtin('load', fn);`, `eval('load(fn)')`; `S = load(fn)`, and the same
  through a handle, `feval`, `builtin`, an anonymous body or `cellfun`, bind nothing and answer the
  struct; `g = @(f) load(f); g(fn);` as a statement is refused with "Attempt to add "v" to a static
  workspace." (`MATLAB:err_static_workspace_violation`). A numeric text file is bound under its
  sanitised name the same way.

**V9.1 — the count reaches every callee.** `IJgsMultiCallable.CallMultiple` takes zero: the callee
sees `nargout` 0 and hands back the first output it made anyway, or none. `UserFunction` produces
`wanted` outputs (one, if assigned, for a statement), and refuses a missing one in R2025b's words
(`QualifiedName`: the file's stem, the closure chain of a nested function, `OwnerClass` for a
method). `BuiltinFunction.CallMultiple` sends zero to the multi-output body only for a builtin that
cares (`KnowsWhenDiscarded`, and the new `TakesOutputCount`: `feval`, `load`, `cellfun`,
`arrayfun`, `structfun`, `eval`, `evalin`); every other builtin answers its value, which a
statement binds to `ans` as R2025b binds `size(1);` and `h = @sin; h(1);`. The statement road
(`ExecuteExpressionStatement`) asks for zero on every callee shape — a plain name, a bare name, a
variable holding a handle, `h(x)`, `c{1}(x)`, `s.f(x)`, `obj.m(x)` (`TryExecuteCallStatement`,
`InvokeFunctionValue`) — and binds `ans` to the first output handed back; a bare name from any
layer, a `clear.m` run as `clear;` included, is called there asked for nothing. The forwarder's
statement form asks a count-taking target for none. The runtime's own callbacks — a timer's, a
listener's, a graphics callback, a function file run as the program — are invoked asked for
nothing (`JgsCallbacks.Invoke`), as MATLAB invokes one; a function argument a builtin evaluates
for its value is asked for one as before. `for k = np()` over a scalar answer runs one pass
(#174, found by the fixture: the walk refused a lone number).

**V9.2 — the call site is handed on.** The roads set the pending call before invoking
(`SetPendingCall`), and the frame takes it with the receiver of a dotted method call beside it
(`CurrentReceiver`). A builtin that runs script code with no argument syntax for the callee it
calls (`RunsScript` and not `ForwardsCallSite`) has the site cleared before its body, so a
callback reads nothing; `feval` forwards the written call minus its first argument
(`WithPendingCall`), and `builtin(...)` forwards as before. `inputname` refuses in a script
(`InScript`: set for the main program and a script file run by name, cleared for a function's
body), answers '' with no site or past the arguments, and refuses index zero.

**V9.3 — a call asked for more than the callee has.** `JgsOutputDemand.Refuse(callee, wanted)`:
a user function's maximum is its output list (unbounded with `varargout`; a JGS `fn` has none), a
builtin's is R2025b's `nargout` for the name, a bound method's its method's. The table
(`JgsBuiltinOutputCounts`, generated by `tools/matlab-checklist/gen-builtin-outputs.py` from
`nargout-r2025b.tsv`, which `tools/matlab-checklist/audit-nargout.m` recorded for every name the
catalog registers: 1,520 names with a fixed count, 108 with none; a name with a variable count or
not MATLAB's has no maximum here) also says whether MATLAB holds the name as a file, which picks
the identifier; `pause` is the one measured exception the generator leaves out. A row's count
applies where it cannot be wrong about this build's own builtin — when it is zero, or when the
builtin has only a single-output body: the stress suite showed `nargout` under-reporting a C
built-in's second output (`fgets` reports 1 and gives `[tline, ltout]`), seeing the `tf`
constructor where the digitalFilter overload answers `[b, a]`, and unable to see what this build
gives beyond MATLAB's own (`triplot`'s coordinates). A builtin with a multi-output body answers
for itself, and handing back fewer than a multiple assignment asked is refused there as
`MATLAB:maxlhs` (`[a, b, c] = max(x)`), after the body ran rather than before. Where the refusal
happens otherwise follows the measured order: `TryResolveCall` refuses a direct call of a user
function before evaluating its arguments; every other road refuses after them — the expression
call and the multiple assignment after their arguments, `InvokeHandle` after dispatch has chosen
the target, `feval`, the forwarder and `CallForOutputs` (cellfun's family) before invoking. A
validator (`mustBePositive` in an `arguments` block or on a property) is asked for nothing. A
built-in's refusal carries its name (`UsingName`) so an `ErrorHandler`'s record heads it "Error
using error", as before; the old special case for `@(x) error(...)` is gone. `nargout('f')` inside
a function body is the function form (the variable of that name no longer shadows it), and
`nargout` of a builtin answers the recorded count.

## What moves

- Flipped, stamped in both lanes: `value_isolation_calls` a055 ×3, a077, a078, a079, a081, a084 ×2;
  `value_isolation_lifetime` a109 ×2, a111 (`a111_save_handle_load_is_new`), a144 ×3, a145 — the
  16 lines V9 owned. Their `.owners` rows are gone (the V5 rows of `calls` and the V6/V10 rows of
  `lifetime` remain).
- The three new fixtures agree on every line in both lanes: no divergence.
- `check-ratchet` holds no V9 line.

## Consequences

- A callee sees the count the syntax asked for, on every road; a statement binds `ans` to what the
  callee made anyway; `load` binds by demand; `inputname` works through handles and `feval`; a
  call asked for too much is refused before the callee runs, with MATLAB's identifier and order.
- Cost: one field write more per call (the receiver beside the pending call), one table lookup
  per builtin minted (at registration), and one comparison per call against the callee's
  maximum. A statement call of a user function now returns through the multi-output road (an
  array of one). The allocation regression against the V2 baseline is in the plan's "As built
  (V9)".
- Not modelled: R2025b's `inputname` inside a function called by its own M-file implementations
  (`fzero`, `integral`, `bsxfun`, `arrayfun` with two inputs) answers those files' local variable
  names; `builtin('feval', @f, x)` answers '' where this build answers `x`. Neither is in a fixture.
- Tests: `CallContractM170Tests`.

## Divergences

None recorded by this stage.
