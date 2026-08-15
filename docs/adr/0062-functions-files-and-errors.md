# ADR 0062 — Functions, files, and errors

## Status

Accepted (M62, 2026-08-14). The second milestone of the M61–M68 language arc. It makes a project of
several files runnable, gives an error an identity a script can branch on, and lets a function state
what its inputs must look like. All three are prerequisites for M68: a class lives in a path file,
its constructor raises identified errors, and its properties reuse the validators.

## Context

M61 established the thesis of this arc — what breaks a ported MATLAB script is the language, not a
missing name — and this milestone takes the next three items off that list.

Before it, `main.m` calling `helper.m` was a hard error: an unknown name went straight to
`Undefined()`, and `addpath` was on the deliberately-unsupported list with the reason "files resolve
against the script's folder and the workspace root". That reason was true and beside the point: files
resolving is not functions resolving.

`error('id:sub', …)` was worse than absent. It *parsed* the identifier, used it to decide where the
format string began, and then threw it away. Every `catch ME` saw `identifier = ''`, so code that
branches on the identifier — the standard way to tell one failure from another — silently took the
wrong arm. A test that only checks that an error was raised cannot see this, which is why it survived
five milestones of error handling.

## Decision

### The path is consulted last, and that is a deliberate divergence

Resolution order is **variables → the script's own functions → the built-ins → files on the path**.
MATLAB's is the other way round for the last two: a `max.m` in your working folder shadows the real
`max`.

JGraph puts the ~2,500 built-ins first, because the failure modes are not symmetric. Under MATLAB's
rule, a half-finished `max.m` left in a working folder silently changes what every script in that
folder computes. Under this one, the worst case is that a user's deliberate override does not take
effect — visible, reported by `which`, and fixable. `ABuiltinBeatsAFileOfTheSameName` pins it and
section 6 of `stess_34.m` states it in the script.

### A file is loaded whole, which is what makes local functions local

`JgsFunctionPath.Load` parses the whole file and declares *every* function in it into one environment
whose parent is the globals; the name resolves to the first. That one choice gives MATLAB's
local-function rule for free: the file's other functions see each other and nothing outside the file
sees any of them. Had the loader picked out one `FnStmt`, two libraries with a `normalise` helper
apiece would have started calling each other's.

A file that is *not* a function file is a script, and runs in the caller's own frame through
`Interpreter.RunScriptFile`. That is the whole difference between a script and a function and the
reason a `setup` file is worth having.

Caching is by name, keyed on the resolved path and the file's last-write time, so editing a helper
between two runs of a console session is picked up without anything being told. `rehash` is therefore
left as the accepted no-op it already was: what it promises is already true.

### A bare name that is a file runs; `@name` is how you ask for the handle

Path resolution is consulted at three sites, and each wants a different answer. In callee position
(`helper(3)`) it must hand back the function; at `@helper` it must hand back the function; at a bare
mention (`setup`) it must *call* it, which is MATLAB's rule for any name that is not a variable.

The callee site cannot simply fall through to the bare-name site: that would find the same file, call
it with no arguments, and then subscript the answer. So `EvaluateCallee` asks the path itself.

### An identifier is data, not decoration

`JgsRuntimeException` gained an `Identifier`, and the errors the *runtime* raises deliberately leave
it empty. Inventing identifiers for JGraph's own messages would mean promising spellings that
MATLAB's do not match, and a script that switched on one would take the wrong branch on real MATLAB.
Only what a script raised itself carries one.

Telling an identifier from a message is a test that must reject far more than it accepts — a colon
alone is not enough, or every `error('Value: %d', n)` becomes an identifier. `IsErrorIdentifier`
requires a colon, no space, no format escape, and no leading or trailing colon; the negative case is
tested twice.

### The stack is built while the error unwinds

`ME.stack` cannot be assembled in the `catch`, because every frame the error passed through has
already been torn down by then. So `ExecuteFunctionBody` catches, pushes its own frame onto the
exception, and rethrows. Each frame records the line that was executing *in it*: the innermost where
it failed, every outer one at the call it was waiting on.

That costs a try/catch per user function call — nearly free when nothing throws — and it is what
makes `ME.stack(1).name` mean something. M68 needs it for `MException.stack` anyway.

### MException is a struct with a class name

An MException is `JgsValue.Struct` with `identifier`, `message`, `stack`, carrying a new
`ClassName` tag that only `class` and `isa` read. Every field access, `isfield`, and display already
work on a struct, so the milestone spends nothing on them.

The tag is a property of the value rather than a field inside it, which is the difference from the
older `Type`-field convention the transforms use: a field can be spelled by accident, and would show
up in `fieldnames`. `CopyForBinding` carries it, so an MException passed to a function is still one.
M68 turns these three fields into a real object without moving any of them.

`throw`, `rethrow`, and `throwAsCaller` differ in MATLAB only by which frame the report points at,
and JGraph reports the line the script is on either way — so the three are one behaviour under three
names, recorded here rather than pretended otherwise.

### `arguments` is recognised only where a block may appear

The word is not a keyword. `TryParseArgumentsBlock` recognises it only as the first thing in a MATLAB
function body and only when a separator follows, so `arguments = 5` and `arguments(2)` still mean
what they always did. The syntax takes no spelling away from anyone, which is what makes it purely
additive — section 21 of `stess_34.m` is what fails if that stops being true.

The block runs as an ordinary statement rather than as part of the call. That is what lets a default
expression mention an earlier argument, and MATLAB defines it the same way for the same reason.

A validator written as a bare name is called with the value; a validator written as a call is
evaluated exactly as written, in the function's own frame — which is what makes
`mustBeMember(name, {'red','green'})` able to name its own argument.

## Consequences

Tests move from 4,387 to **4,404**, all green, with 0 build warnings, and all **34** stress scripts
pass. `stess_34.m` is the first stress script that is not one file: `M62_beside.m` sits next to it
and `m62_lib` holds three more, because a project spread across files is precisely what is being
tested.

**One defect the probes could not have found.** `addpath('m62_lib')` resolved its relative folder
against the *process* working directory, so it worked from every probe (all run from inside the
folder) and failed the moment the stress runner started elsewhere. Relative folders now resolve
against the script's own directory, like every other path a script names. The lesson is narrower than
"probe first": a probe run from the convenient directory cannot see a directory bug.

### Deliberate test flips

- `ConsoleBuiltinTests.Addpath_ExplainsThereIsNoSearchPath` →
  `Addpath_AddsAFolder_AndRefusesOneThatIsNotThere`. The old test pinned the refusal and was right
  while there was no search path to add to. The refusal now belongs to a folder that is not there,
  which is all addpath has left to complain about.
- `Path_ReportsTheWorkingDirectory` did **not** flip. `path()` now answers a separator-joined list,
  but with nothing added the list is one entry and the assertion still holds — worth recording,
  because it is the kind of test that passes for a new reason.

### Recorded divergences

- **A built-in wins a name a path file also claims**, where MATLAB gives the file priority. Reasons
  above.
- **`throwAsCaller` reports the same line `throw` does.** JGraph has no notion of blaming a frame.
- **A name-value argument (`options.Width`) is refused**, by name and with its own reason, rather
  than mis-parsed. Take the pairs through `varargin`. `(Repeating)` is likewise not implemented.
- **The declared class is reached by conversion through that class's own constructor**, so
  `x (1,1) double` accepts a logical and converts it. The container classes (`cell`, `struct`,
  `table`) are checked and never converted, because their constructors mean something else entirely —
  `cell(3)` builds a 3-by-3 cell rather than converting anything.
- **An error raised at the top level has an empty stack**, where MATLAB names the script frame.
- **`error` with no identifier still records nothing**, including for every error the interpreter
  raises itself. This is the decision above, not an omission.

## Live checks for the user

Batch cannot see these, so they are listed rather than claimed:

- Editing a helper `.m` while a console session is open, then calling it again: the timestamp check
  should pick the edit up with no `clear` and no restart.
- `addpath` in the console, then completion on a name that only that folder provides — the completion
  engine reads the catalog and the workspace, and has not been taught about the search path, so the
  worst case is a missing suggestion rather than a wrong one.
- A path function reached by F5 from a subfolder, to confirm the script's own directory is what
  relative `addpath` resolves against in the app as well as in batch.
