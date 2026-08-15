# ADR 0061 — Comma-separated lists and variadic outputs

## Status

Accepted (M61, 2026-08-14). The first milestone of the M61–M68 language arc and the load-bearing one:
`c{:}` reaches into argument evaluation, bracket and cell construction, and multiple assignment, so it
lands first and almost alone. `.graph` is untouched — nothing here is a value, and nothing here draws.

## Context

The M52–M60 arc finished the *name* surface: 385 base builtins, 246 of 277 graphics functions, and
both toolboxes with nothing pending. An audit of what still breaks a real ported script found that
the answer had stopped being missing names and become the language itself, and that the largest item
was this one.

`f(args{:})`, `[c{:}]`, `varargin{:}` forwarding — the single most pervasive idiom in real MATLAB —
was a hard error. Worse, it had been *made* an error deliberately: M52 found the colon reaching the
index conversion as a null and coming back out as a `NullReferenceException`, and fixed the crash by
refusing the form. There was a test asserting the refusal. That was the right fix for a crash and the
wrong long-term answer, and this milestone replaces it.

## Decision

### A comma-separated list is not a value, and that is the whole design

MATLAB's rule is that a list exists only in an argument position, a bracket, or a multiple
assignment. JGraph now keeps that rule literally: `EvaluateSpread` answers a `JgsValue[]` and there is
no `JgsType` for a list. It cannot be stored in a variable, cannot be a field, cannot be returned from
a builtin, and lives only as long as it takes the caller to spread it.

**That is what stops the blast radius.** The alternative — a list-shaped `JgsValue` — would have put a
new kind of value into a switch surface that roughly 2,500 builtins dispatch on, and every one of them
would have needed to decide what to do with a value none of them is ever handed. Instead exactly four
call sites learned to spread, and nothing downstream of them changed at all:

- the argument list of a call, and of a call asked for several outputs
- a bracket literal's row, and the single-row form beside it
- a cell literal's row, whose width is therefore not known until the row is evaluated
- a multiple assignment's right-hand side

The refusals are load-bearing and are tested as carefully as the spreads. `x = c{:}` says *"This brace
index names 3 elements where one value is wanted"* and names the three places a list does fit. If a
list ever quietly becomes a storable value, sections 9 to 12 of `stess_33.m` and
`AListIsRefusedWhereOneValueIsWanted` are what fail.

### The deliberate test flip

`MatlabSetOperationTests.ACellsColonIsRefusedRatherThanCrashing` asserted the old error in *every*
position. It is now `ACellsColonSpreadsWhereAListFitsAndIsRefusedWhereOneDoesNot`, which asserts the
spread in the three positions that take one and the refusal in the position that does not. This is
recorded rather than quietly rewritten because the test was right when it was written: it pinned a
crash fix, and only the arrival of the real feature makes it wrong.

### A struct array's field reads two ways, and both are right

`s.a` over a struct array has meant "the collected row" since M41, because a comma-separated list had
nowhere to go. Now it spreads in an argument list and still collects everywhere else, which is
MATLAB's own behaviour and means `[s.a]` and `x = s.a` both keep working while `counts(s.a)` starts
reporting three arguments instead of one.

The subtlety worth recording: **`iscell(t.name)` over a two-element struct array now errors**, because
the field spreads and `iscell` hears two arguments. That is MATLAB's answer too. Reading a list where
one value belongs is the caller's mistake, and the milestone's job is to say so rather than to guess.

This is also where M65 was made cheap. `StructArrayFieldValues` is the only code that knows a struct
array is stored as a cell of structs; the spread and the collected read both go through it. When M65
makes struct arrays a real type, that one method changes and no call site does.

### Reading the target list before the call

`[varargout{1:nargout}] = f(varargin{:})` is the relay, and it needs something the other spreads do
not: **one target standing for several outputs**. The output count a call is asked for is therefore
not the number of targets written — it is the number of slots they name, and that has to be resolved
*before* the call, because it is the call's `nargout`.

`ExpandAssignmentTargets` does exactly that and nothing more: it turns one brace target naming N slots
into N single-slot targets, each of which then goes through the assignment path that already existed,
growth included. The cell need not exist yet — `varargout` almost never does when the relay line runs
— so the subscript is measured against what is there and writing past the end grows it, which is the
same rule `c{end+1} = x` has followed since M41.

### `varargout` is `varargin` pointing the other way

A trailing `varargout` output is a cell whose length the function chooses, so the number of outputs a
function can answer stops being knowable from its header. `CallMultiple` now takes the named outputs
first and draws the rest from the cell. An unassigned `varargout` is an empty list rather than a
fault: a function asked only for its named outputs never had reason to fill one.

### The arrayfun finding, and the silent wrapper behind it

M52 recorded that `arrayfun` ignored its option tail and said the fix was to share `cellfun`'s loop.
Doing that closed three things at once: multi-output, a working `'ErrorHandler'`, and — the real
defect — **a misspelt option word being accepted in silence**. `arrayfun(@(k) k, x, 'UnifromOutput',
false)` used to run and return a numeric array, having quietly not done what it was told.

Finding out why it had gone missing is worth recording on its own. The `Wrap` helper that adds a
multiple-output form to an already-registered builtin **returns silently when the name is not
registered yet**, and `RegisterArrayBuiltins` runs *after* the MATLAB registrar. So a
`Wrap("arrayfun", …)` written in the obvious place does nothing whatsoever, and says nothing. The
several-output form is now declared where the name is, with a comment saying why. Any future `Wrap`
of a name registered later will fail the same silent way — the helper is a trap in the shape of a
convenience, and this is the second time this file family has lost a feature to a helper that
declines to complain.

## Consequences

The tests move from 4,364 to **4,387**, all green, with 0 build warnings, and all **33** stress
scripts pass. `stess_33.m` is the live check and passed all 25 sections on its first run — the only
milestone in the last ten where the script found nothing, which is the expected shape when the CLI
probes were run first: every defect this milestone had was found by probing before the code was
written, not after.

Four defects were found that way and none reached the test suite: `f = getframe`-style bare-name
binding was not among them, but the arrayfun silent-option acceptance, the silent `Wrap`, the
double-evaluation risk in a member spread, and the relay's missing target expansion all were.

**A member spread reads its target only when the target is a plain name.** `s.field` is the form
scripts write; restricting the spread to it means asking "would this spread?" can never run a call
twice. `f().field` in an argument list stays a single value, which is a recorded divergence and a
cheap one.

**JGS is untouched.** The brace spread is additive in both dialects (it was an error before and works
now), and the struct-field spread — the one change that alters an existing answer — is gated on the
MATLAB dialect, because JGS has answered with the collected row since M41 and that surface is frozen.

### Recorded divergences

- **`f().field` and `s.(name)` on a call result do not spread**, for the double-evaluation reason
  above. Both still read as one value.
- **`x = c{[1 2]}` errors where MATLAB errors, with a different message.** MATLAB says "Expected one
  output from a curly brace or dot indexing expression"; this build names the three places a list
  fits, which is more use to someone who has just written one in the fourth place.
- **A list is not a value even in an `ans` position.** `c{:}` alone on a line does not print each
  element in turn the way MATLAB does; it refuses, because the display path takes a value.

## Live checks for the user

Batch cannot see these, so they are listed rather than claimed:

- A relay function in the app's console across several calls, to confirm `nargout` is what the console
  asks for rather than what the last call asked for.
- Completion and signature help on a line containing `c{:}` — the completion engine lexes tolerantly
  and has not been taught that a brace can name several, so the worst case is a missing suggestion
  rather than a wrong one.
