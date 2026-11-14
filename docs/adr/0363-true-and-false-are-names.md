# ADR 0363 — `true` and `false` are names in MATLAB, read as the literal until something binds them

## Status

Accepted, 2026-11-14. Open item 205 (chips wave 5, lane 5B).

## Context

MATLAB's `true` and `false` are functions, not keywords (`iskeyword('true')` is 0), so a workspace
may bind either: `true = 1`, `function y = f(true)`, `[a, true] = deal(1, 2)`, `for true = 1:3`,
`global true`, `persistent true` and `@(true) true + 1` are all legal in R2025b, and the word is then
that variable. JGraph's lexer made both words keywords in the MATLAB dialect, so such a file did not
parse ("Expected a parameter name, but found 'true'"). Stage K11's names probe (ADR 0267) met it:
`k11cpp(class, delete, template, bool, true, ...)` could not be read, so its C wrappers took no
arguments.

Probes `p205`, `p205b` and `p205c` (scratch) measured what R2025b does with the word:

- **A function decides from its own text.** Where the function's code binds the word (a parameter,
  an output, an assignment of any form, a `for`, `global` or `persistent`, an anonymous function's
  parameter), the word is that variable throughout its body and its nested functions; a read before
  the binding is *Unrecognized function or variable 'true'*, and a read after `clear true` is
  *Reference to a cleared variable true*. Where the code never binds it, the word is the built-in in
  that frame even when `eval('true = 7')` (with the warning `MATLAB:lang:ExternalShadowedInEval`),
  `load`, `assignin('caller', ...)` or a called script puts a variable `true` there; `eval('true')`
  then answers the built-in too.
- **A script and the command line read the workspace.** `true` before `true = 3` in a script is
  logical 1, after it 3, after `clear true` logical 1 again; a script reads what the base workspace
  holds; a function never reads the caller's.
- **An anonymous function captures.** `g = @() true` made while `true` is 5 answers 5 after a
  `clear true`; one made before `true = 6` answers logical 1.
- **A function named after the word answers it.** A local function `true` answers the bare word and
  `true(2)` in its file; a `true.m` on the path answers both too, as a file takes any built-in's name
  (ADR 0149), in a script statement, `evalin('base', 'x = true;')`, an anonymous body and a function
  read for the first time. Two R2025b answers break that rule: `eval('true')` (the expression form
  that hands back a value) answers the built-in, and a function that was called before the file
  appeared keeps the built-in it resolved then.
- **The constructors take a class name.** `true(2, 'logical')`, `true('logical')` and
  `true(2, "logical")` are logical; any other text last (`'Logical'`, `'double'`, `'2'`) is
  `MATLAB:True:invalidStringInput`, *Class name must be a class that supports TRUE, such as
  "logical".*, which is also what `true 2` (command syntax) answers; text anywhere else, a cell, or a
  class name after `'like'` is `MATLAB:True:inputType`, *Size inputs must be numeric.* JGraph refused
  every one of them with its own words ("true expects argument 2 to be a number").
- `exist('true')` is 5, and 1 once a variable binds it; `which true` names the built-in, and
  `variable` once one does.

## Decision

### The lexer reads the words as names; the parser reads a bare one as a marked literal

`true` and `false` leave the MATLAB keyword table (`Lexer.MatlabKeywords`), so they are identifiers
wherever a name may stand: a parameter, an output, a target, a loop variable, a field, `@true`. JGS
keeps both keywords. The MATLAB editor's highlighting reads the same table, so the words are now
coloured as built-ins.

`Parser.LiteralNames.cs` (the parser is now a partial class) makes a bare word — one not followed by
`(`, `{` or `.`, which stay a name for the call, the index and the field — a `BoolLiteral` whose new
`Shadow` flags (`LiteralShadow`) say what may answer it in place of the built-in:

- `File`, on every MATLAB literal: a `true.m` or `false.m` on the path.
- `Variable`: outside every function (a script, the command line, eval'd text, a class's defaults),
  and in a function whose text binds the word. Each function body is a `LiteralScope`; the binding
  sites (`AsTarget` for assignments and multiple assignments, the parameter and output lists, `for`,
  `global`, `persistent`, an anonymous function's parameters) mark the scope, and when the body ends
  (`LiteralScope.Adopt`) every literal read in it is settled: a word the body bound reads the
  variable, and the rest wait on the enclosing body, whose variables a nested function shares, so a
  parent's binding written after its nested function's text still reaches the nested reads.
  `FnStmt.LiteralBindings` keeps what each function binds, its enclosing functions' included.
- `Function`: the file defines a function under the word (`_literalFunctions`, settled at the end
  of `ParseProgram`).

An assignment whose target read as a literal (`true = 1`) gets a `VariableExpr` target. A class
attribute written with the word (`(Constant = true)`) reads either form.

### A literal costs the literal until something can answer it

`Interpreter.Evaluate` returns the literal for `LiteralShadow.None` (JGS) exactly as before. A MATLAB
literal goes to `ShadowableLiteral`, which returns the literal unless one of three things holds:

- the literal may read a variable and some workspace over the layer has bound either word —
  `JgsBuiltinLayer.LiteralNameBound`, set by `JgsEnvironment.Store`, `DeclareGlobal` and
  `DeclarePersistent` the first time either word is bound and never reset, so a session that never
  binds them pays one field read;
- the file defines a function of the name;
- the file index says a file shadows the word (`JgsNameResolver.IsShadowed`, now internal), the
  check every built-in call already makes, with its set refreshed at most once per statement.

When one holds, `LiteralShadowing` asks: a variable (through `JgsNameResolver.Lookup`) counts only
when the frame's function binds the word by its text, and, in an anonymous function's body, only
when the binding lies inside its own workspaces (its call frame and its snapshot) and not in the
defining workspace behind them; a nested or local function of the file counts; and a file answers
through phase two of a no-argument call from the built-in, the dispatch table included. Nothing
answering is the literal.

`MintsItsValue` counts only a `None` literal as minted: a MATLAB literal may be another holder's
value. An anonymous function's free names include a literal that may read a variable, so the
snapshot captures `true` when the defining workspace holds it.

### The compiled loop keeps the constant and checks at entry

The loop compiler pools a literal as 0 or 1 as before and records `RegisterProgram.ReadsShadowableLiteral`;
a literal a function of the file answers refuses compilation. `TryLoadHotLoopEntry` refuses the
fast path when `LiteralsStillLiteral` finds something answering either word, and
`HotLoopBuiltinsStillResolve` does the same after a walked statement, so `eval('true = 10;')` inside
a compiled loop leaves it for the walk from that statement on (R2025b's 22 in the fixture). A loop
over the built-ins compiles and runs as it did.

### The constructors take a trailing class name

`LogicalConstructor` takes a last `'logical'` (as spelled, char or string) and drops it, refuses any
other trailing text with `MATLAB:True:invalidStringInput` / `MATLAB:False:invalidStringInput` and
R2025b's sentence, and refuses text, a string array or a cell among the sizes with
`MATLAB:True:inputType`, *Size inputs must be numeric.* A class name after `'like'` is among the sizes.

## Consequences

- K11's names probe reads `k11cpp`: its wrappers take their ten arguments
  (`recordings-jgraph/k11_names.out.txt`); the key still differs from R2025b's, now only by the C++
  words JGraph renames with a `0` (ADR 0267's divergence), and `compare-k11.py` says so. The names
  compare stays 70 of 78 with nothing unexplained.
- New parity fixture `oi205_true_false_names` (81 rows from R2025b 25.2), four of them `div=ADR0363`.
- New tests in `MatlabTrueFalseNamesTests`.
- A script or function that names a variable `true` or `false` now runs; nothing that parsed before
  reads differently, except where a `true.m` or `false.m` is on the path, which now answers the bare
  word as R2025b's does.

## Divergences

- **A read R2025b refuses reads the built-in.** In a function whose text binds `true`, R2025b
  refuses a read before the binding (*Unrecognized function or variable 'true'*) and after
  `clear true` (*Reference to a cleared variable true*), an anonymous body's cleared capture
  included; JGraph reads the built-in there, as it reads the built-in for every other built-in's
  name (`a = pi; pi = 5;` is the same gap).
- **A function called before a `true.m` appeared reads the file.** R2025b keeps the built-in such a
  function resolved earlier; JGraph re-resolves, the stale-cache behaviour ADR 0149 declined.

## Still open

- `eval('true')` and `evalin(ws, 'true')` with an output answer the built-in in R2025b even with a
  `true.m` on the path; JGraph answers the file (not in the fixture).
- `which true` names the built-in in JGraph's words and does not report a variable (ADR 0149's
  `which` divergences); `iskeyword` is not a JGraph built-in.
- `true(2, 'like', sparse(true))` is dense, and a sparse double prototype is taken (ADR 0213).
- The `MATLAB:lang:ExternalShadowedInEval` warning R2025b gives when `eval` binds a word the
  function's text reads as a function is not given.
- A unit-level rule decides a function named after the word: every bare mention in its file reads
  the function, which is R2025b's answer for a local function; a nested function named `true`
  inside one function of the file reaches the mentions in its siblings as well.
