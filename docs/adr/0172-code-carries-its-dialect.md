# ADR 0172 — Code carries its dialect

## Status

Accepted. Stage V11 of the value-ownership plan (`docs/plans/zfit-copies-and-temporaries-plan.md`),
after V10's exact lifetimes (ADR 0171). One commit, this ADR's, with one JGraph-only fixture whose
MATLAB half was recorded from R2025b before the interpreter was touched.

## Context

`Interpreter.Dialect` was one field, swapped by `run()` for the duration of an included file and
by nothing else. A function an included `.m` file defined ran its body under whatever dialect was
active when it was called, so `first = @(x) x(1)` made by a `.m` script and called from JGS
answered `first([1 2 3])` = 2 and `@(a) [a, a]` built a two-element list (#130). A `.m` *function
file* could not be called from JGS at all: the search path answered the MATLAB dialect only (#131).
Every built-in captured the *session's* dialect at registration and read it for the rest of the
run, so `run("m_find.m")` from JGS printed `k=[1 2] k1=[2 3]` where R2025b prints `k=[2 3] k1=2`
(#142), and the same script's `fprintf('…\n')` printed a literal `\n` (#143), because `find`'s
origin and limit and `sprintf`'s escapes and cycling were chosen once for the session. The
debugger kept a session dialect of its own for the prompt, the cell edits and the live edits, so a
pause inside MATLAB code entered from JGS composed 0-based cell edits and ran them 1-based, and
selecting an outer frame parsed in one dialect and ran in another.

The audit (V11.1) found 102 reads of the interpreter's dialect and 154 of a captured one in 42
built-in files. Every one but three is *lexical*: a property of the code being run — its index
base, whether a binding shares (M2) or a scope holds (M5), whether a function's frame is a call
boundary, what a bracket or a `%` means, what `find(m, 1)` or a format escape means, where a
declaration hoists to, and which words its refusals use (an identifier a `.m` file catches must be
MATLAB's, whoever ran the file). The three that are the *host's*: the prompt's and the completion
engine's parser, which read the language the user is typing, and the shadowing warning, which is
about the folders a session scans and which a JGS session never raises.

## Decision

**Code carries its dialect.** The parser stamps every statement and every anonymous-function
expression with the dialect it parsed in (`Stmt.Dialect`, `AnonymousFnExpr.Dialect`), beside the
source id. Every entry into a body of code enters that dialect and leaves the caller's on exit, an
error included (`Interpreter.EnterDialect`, a `using` token like `EnterFile`'s): a function body
(`ExecuteFunctionBody`) and its parameter binding (`UserFunction.CallMultiple` binds under the
callee's dialect, so a MATLAB function's parameter is M2's share whoever handed the argument over
and a JGS `fn`'s stays the reference it was), an anonymous body and its binding, a script run as the
program or by name (`Run`, `RunScriptFile`: the program's own dialect, which is what `run()` and the
path's script road now rely on instead of swapping the field), a class constructor's binding, a
property default, a validator, and the paused debugger's prompt. `RunInDialect` is gone. Where a
declaration hoists, whether a hoisted `FnStmt` is a no-op when reached, and whether a handle
captures a built-in are the *declaration's* dialect, not the running one, so the debugger's live
edit of a `.m` file hoists into the file while the run is paused in JGS.

**The built-ins read the running dialect at the call.** `CreateGlobals` makes one
`JgsRunningDialect` slot — the session's dialect and the current one, with `JgsDialect`'s members
forwarded and an implicit conversion to it — installs it on the built-in layer, and hands it to
every registrar in place of the dialect they captured; the interpreter built over that workspace
takes the same slot (a workspace built for one dialect refuses an interpreter of the other) and
swaps it at every body entry. A read that had been taken once at registration and kept (a keyed
collection's and a timer's store rule, the char-row promotion of the rearranging verbs, `sort`'s
index base, `meshgrid`'s one-output form) reads the slot inside the call now. Every registration
choice that installed a wrapper for one dialect is a runtime choice: the MATLAB reductions'
dimension forms and `max`/`min`'s, the formatters' escape decoding, the imaging multi-output forms
and the constructors' shapes are registered for every session and hand a JGS call to the form
underneath; the names the two dialects give different meanings — `print`, `range`, `slice`,
`seconds`, `datetime`, `zeros`/`ones`/`rand`/`randn` — dispatch on the calling code's dialect
(`RegisterMatlabFormOver`, `RegisterJgsFormOver`), each road keeping its own form's flags: whether
a bare mention calls is JGS's question, whether a statement binds `ans` and the several-output
body are MATLAB's. Two MATLAB-only names cannot dispatch and stay the MATLAB session's alone: the
`graphics` namespace constant and the spreadable `Inf`/`NaN` forms, which would replace JGS's own
value bindings.

**JGS may call a `.m` function file** (#131, decided here). The JGS resolver's order is its walk
and then the folders: a name nothing in the walk holds is looked for as a `.m` file beside the
script and on the added folders, and what the file defines runs as MATLAB. The folders come
*after* the built-in layer, so no JGS built-in is ever shadowed by a file (MATLAB's order asks the
folders before the layer, with the shadowing warning), and a name the walk answers never touches
the index. A dotted mention of a class file's name loads it from JGS too. The alternative — a
refusal naming the file — would have left the crossing reachable only through handles a `run`
script leaves behind, and the fixture's first five cases unwritable.

**The debugger follows the code.** Each frame record carries the dialect its call was made under,
read before the callee enters its own; the pause records the running dialect. The prompt parses
and runs in the selected frame's code dialect (`RunWhilePaused` enters it), a cell edit composes in
it (`ComposeCellAssignment` takes the frame), and a live edit parses in the edited file's own —
MATLAB for a `.m` file, the run's otherwise, the rule `run()` parses by.

**Binding shares are dialect-gated; ownership inside the model is not** (M17, unchanged). A JGS
binding never shares, so a MATLAB script's `a(1) = 7` reaches a JGS alias of `a` (c10, kept) and a
JGS write into a payload a MATLAB script shared detaches and leaves the alias whole (c7, kept); M3's
and M7's gates run whatever the dialect, so a MATLAB function's nested write into a struct a JGS
caller handed it leaves the caller's struct and its alias whole.

## What moves

- `dialect_crossing` (new, JGraph-only, 44 lines): the probe sweep's c1–c11 through a `.m` function
  called from JGS and through `run` of a `.m` script, c7, c10, the nested crossing, the lexical
  built-ins (`find`'s origin and limit, `unique`, `sprintf`'s cycling and escapes, `fprintf`'s
  newline) reached both ways and JGS's own meaning after them, a JGS function called from a `.m`
  script, an error in each direction restoring the caller's dialect, a compiled MATLAB loop inside a
  JGS-called function, V10's lifetimes across the crossing (a frame's exit, a JGS rebinding of an
  escaped handle), and c15/c16 (Z2b's guards). Its recording is written from the rule; every line
  agrees in both lanes. `dialect_crossing_matlab` (new, recorded from R2025b, 33 lines) calls the
  same helpers from MATLAB and agrees in both lanes, so what each helper means in MATLAB is measured.
- Flipped: #130, #131 (as decided above), #142, #143. `check-ratchet` holds no V11 line.

## Consequences

- A `.m` file means what it means in MATLAB however it is reached, and JGS code keeps JGS's meaning
  however it is called: a MATLAB function called from JGS indexes 1-based, binds by value,
  concatenates its brackets, hoists into its file and refuses in MATLAB's words; a JGS closure called
  from a `.m` script indexes 0-based.
- Cost: one field write per body entry and exit, one field read where a dialect flag used to be
  read, and one extra type test on the JGS refusal road (an undefined name asks the file index before
  it refuses). The allocation regression against the V2 baseline is in the plan's "As built (V11)".
- Not modelled: a char callback (`'disp(1)'` in a timer's or a graphics object's property) is parsed
  in the dialect running when it fires, not when it was stored — a MATLAB-made char callback fired
  while JGS code runs at a drain point parses as JGS; a `.jgs` file named by `run()` from MATLAB code
  is parsed as MATLAB, as it always was (only a `.m` file carries a dialect of its own by name).
- Tests: `DialectCrossingM172Tests`, `JgsDebugDialectM172Tests`.

## Divergences

None recorded by this stage.
