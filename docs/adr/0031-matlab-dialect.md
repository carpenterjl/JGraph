# ADR 0031 — Running MATLAB (`.m`) files

## Status

Accepted (M28, 2026-07-24).

## Context

Engineers arrive with MATLAB scripts. JGS is deliberately MATLAB-*flavoured* — `if`/`end`, `for k = 1:n`,
`[1 2; 3 4]`, colon ranges, `;` echo suppression, complex literals — but it is not MATLAB, and the
differences are exactly the ones that make a paste-in fail: `%` is modulo rather than a comment, `'` is
only a string quote, `let` is required, indexing is 0-based, and there are no `function` declarations,
cell arrays, structs, or `@(x)` handles. Porting a script means editing it, and an edited script has to
be maintained twice.

The goal is the opposite: **a `.m` file should run in JGraph and in MATLAB with no changes at all**, and
keep meaning the same thing in both.

## Decision

**One pipeline, two dialects.** MATLAB is not a second interpreter. `JgsDialect` is an immutable record
carrying the handful of flags where the two languages genuinely disagree — index base, whether `let` is
required, whether `%` opens a comment, whether `'` is transpose, whether assignment copies, whether a
block has a scope, whether braces build cells. It is threaded as a defaulted parameter through
`Lexer` → `Parser` → `Interpreter` → the builtins. Every difference reads a flag; none reads a global.
Two presets exist, `JgsDialect.Jgs` and `JgsDialect.Matlab`, and a JGS run can never observe MATLAB's.

**A separate engine identity.** `MatlabScriptEngine` reports `Language => "MATLAB"` and always
constructs `JgsDialect.Matlab`. It rides the existing language-string routing —
`ScriptDocumentModel.LanguageForFile` maps `.m` to it, so the editor, the workspace, and
`jgraph -batch script.m` all reach it without any new dispatch. Because the engine hard-codes the
dialect, a `.m` file is unaffected by the user's JGS language settings (ADR 0032): it behaves the same
however JGraph was started, which is the whole point.

**Debugging comes along.** `IJgsDebuggable` replaced the concrete `is JgsScriptEngine` check in the
script workspace, so breakpoints, stepping, the call stack, and live edit work in `.m` files with no
further work — the debug hook was never dialect-specific.

**Cells and structs are values, braces are syntax.** `JgsType.Cell` and `JgsType.Struct` exist in both
dialects and are reachable from JGS through `cell()`, `struct()`, and `s.field`. What is MATLAB-only is
the `{...}` literal and `c{i}` index, because JGS spells block bodies with braces and overloading them
would be ambiguous for no gain.

**Assignment copies in MATLAB.** `b = a; b(1) = 0` must leave `a` alone, or a ported script silently
computes different numbers. Containers are cloned at the three points a name is bound: assignment, the
loop variable, and a function's arguments. JGS keeps its reference semantics, which its own scripts rely
on. The copy is eager — `x = x + 1` on a large array allocates the sum and then clones it — which is
one extra memcpy per statement; a copy-on-write share bit is the obvious optimization if it ever matters.

**`*` is matrix multiplication, and refuses what it cannot represent.** In MATLAB the dotted spellings
are the elementwise ones. JGraph's arrays are one-dimensional (a matrix is an array of row arrays, and a
vector has no row/column orientation), so `*` implements matrix×matrix and matrix×vector and **errors**
on vector×vector rather than guessing: an elementwise answer where MATLAB gives an inner or outer
product is a wrong number, and a wrong number is worse than an error. `/` and `^` between two arrays
error the same way, naming the dotted form to use.

## The apostrophe

The single riskiest rule in the milestone: `'` quotes a char literal and also transposes. It is
transpose when it follows something transposable — an identifier, a number, `)`, `]`, `}`, `end`, or
another transpose — **with no whitespace between**. A space before it always starts a literal, which is
what makes command syntax (`disp 'hi'`, `hold on`) read correctly and what MATLAB itself effectively
does inside brackets. Char literals escape a quote by doubling it and have no backslash escapes, so
Windows paths can be written plainly — but the *formatting* functions (`fprintf`, `sprintf`, `error`)
decode `\n` in their format string, as MATLAB's do.

## Known limitation: shape

JGraph's arrays are 1-D. Consequences, stated rather than hidden: `size` reports `1×n` for a vector
whichever way it was built; `v'` is value-preserving (which is what makes the ubiquitous `(0:0.1:1)'`
idiom work); and `*` covers only the shapes above. Scripts that *compute* with orientation — an outer
product via `col*row` — get a clear error, not a wrong answer.

## Unsupported, by name

`classdef`, `parfor`, `spmd`, and `persistent` are rejected at parse time naming the construct. A
curated list of toolbox functions (`ode45`, `fmincon`, `syms`, `readmatrix`, …) reports
`'ode45' is not supported in JGraph (differential-equation solvers)` rather than "not recognized", so a
script that needs something JGraph does not have says so plainly. Struct arrays (`s(2).f`) error
clearly too.

## Alternatives considered

- **A translator from `.m` to JGS.** Rejected: it would edit the user's script, and every error message
  and breakpoint would then point at code they never wrote.
- **A separate MATLAB interpreter.** Rejected: ~9,000 lines of tree-walker are already 80% of MATLAB's
  semantics; a fork would drift within a milestone.
- **A per-call index-base flag** (as ADR 0028 removed). Rejected for the same reason it was removed —
  the base must be a property of the language being run, not of each call site.
- **Keeping reference semantics in `.m` mode** and documenting the difference. Rejected: the failure is
  silent and numeric.
- **Reading `*` as elementwise** to avoid the shape problem. Rejected: same reason.

## Consequences

- `Lexer` now takes a dialect. Its keyword table, comment rules, quote handling, line continuation, and
  the significance of newlines inside `[ ]` all vary by it; `Token` gained `PrecededByWhitespace`,
  without which `[1 -2]` (two elements) and `[1 - 2]` (one) cannot be told apart.
- The parser gained MATLAB statements (`function` with output lists, `switch`, `try`, `global`,
  `[a, b] = f(x)`, command syntax) and expressions (transpose, cells, field access, `@(x)`), plus the
  `AstChildren`/`AstEquals` entries they need — miss those and live-edit-while-paused breaks silently.
- `global` is implemented as a run-wide set of names rather than a per-function declaration. The two
  differ only for a script that also uses one of those names as an ordinary local.
- MATLAB builtins (`strcmp`, `num2str`, `error`, `cellfun`, `fieldnames`, …) are registered in **both**
  dialects; a second, dialect-gated name table would be one more thing to drift. The builtin catalog
  covers them, and the existing sync test still pins catalog to registration.
- `size`, `max`, `min`, and `sort` gained multiple-output forms through `IJgsMultiCallable`; the
  single-value forms are untouched.
