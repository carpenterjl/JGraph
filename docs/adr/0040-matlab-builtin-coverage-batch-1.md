# ADR 0040 — Builtin coverage, batch 1

## Status

Accepted (M37, 2026-07-28).

## Context

M36 produced a filtered checklist of the 11,063 documented R2021b commands
(`matlab-r2021b-documented.html`). Of the 515 rows the documentation classifies as **builtin**,
JGraph implemented 109. Auditing the remaining 406 against the engine showed that about 185 need
machinery JGraph deliberately does not have — .NET/Java/Python/MEX interop, the console `db*`
debugger, R2020b `pattern` objects, handle graphics, sparse matrices, OOP metaclass plumbing —
leaving ~218 that the current value model can express.

This milestone takes the first focused batch of that remainder: four families that need no new
machinery at all. [matlab-builtin-coverage.md](../matlab-builtin-coverage.md) records where the other
families stand, and why each excluded one is excluded.

## Decision

### Stay on the current value model

Nothing in this milestone introduces a new `JgsType`, a handle system, or a sparse representation.
Where MATLAB's answer depends on machinery JGraph lacks, the answer is the honest one and is
documented rather than faked: `isinteger` is always false (every number is a double), `isstring` is
always false (text is `char`, there is no string-array type), and `isrow`/`iscolumn` follow the
orientation-free vector M36 already settled on for the flip/transpose family — a vector is a row.

### MATLAB's spelling wins, once

Comparing the catalog against every documented callable row found exactly four case differences.
`inf`/`Inf` and `nan`/`NaN` are not conflicts — MATLAB genuinely has both spellings — so both stay.
The other two were renamed outright: `startswith` → `startsWith`, `endswith` → `endsWith`. One
canonical name, at the cost of breaking JGS scripts that used the old spelling. `indexof` is *not* a
misspelling of `strfind` (first index versus every index), so it stays and `strfind` is left for the
strings batch.

### Constants are functions that call themselves on sight

MATLAB implements `eps`, `realmax`, `realmin`, `flintmax`, `intmax`, and `intmin` as zero-argument
functions, so `x = eps` is a number and `eps(x)` is the spacing at `x`. JGraph had no way to be both:
a bare name evaluates to whatever it is bound to, which for a function is the function.

`BuiltinFunction` gained `AutoCallsBare`. When a plain name resolves to a builtin carrying that flag,
`Evaluate` calls it with no arguments and yields the value. Callee position is exempt through a new
`EvaluateCallee`, which resolves a plain name without the auto-call, so `eps(1)` still reaches the
builtin instead of trying to subscript the number `eps` would otherwise become. The flag is opt-in
per builtin, so ordinary names are untouched: `sin` is still a value that can be passed around, and
`f = @sin` is unaffected.

The integer limits come back as **doubles** — JGraph has no integer classes — which is exact for
every class except `int64`/`uint64`, whose extremes exceed a double's 53-bit integer range.

### Degree trigonometry reduces before it converts

`sind(180)` is exactly 0 in MATLAB, and `x * pi / 180` cannot give that. `DegreeSine`/`DegreeCosine`
fold the angle with `IEEERemainder` and return the exact value at each quadrant multiple before
falling through to the radian call. Every degree function in the family (`tand`, `secd`, `cotd`, …)
is defined in terms of those two, so the exactness propagates instead of being re-derived.

### Operator function forms are the operators

`plus`, `mtimes`, `mldivide`, `eq`, … are, by MATLAB's own definition, the function spellings of
operators the interpreter already evaluates — including every matrix shape M36 taught `\`, `/`, and
`^`. Reimplementing them in a builtin would fork the semantics the first time either side changed.

`Interpreter.ApplyBinary` is a private instance method, so the interpreter declares these itself:
its constructor calls `JgsBuiltins.RegisterOperatorFunctions(globals, this)`, closing over
`ApplyOperator` (a thin overload that carries a line/column instead of a syntax node) and
`BuildRange` (for `colon`). This is the same "declared by whoever owns the capability" pattern
`save`/`load`/`clear` use. Because the interpreter is constructed *before* a workspace owner
snapshots the pristine environment, the names land in that snapshot and never appear in `whos` or a
saved `.mat`. `JgsScriptEngine.BuiltinNames()` lists them explicitly, as it does for `run`/`clear`,
so the catalog-to-registration parity test still holds.

`uminus`/`uplus` are the binary operators against zero, which keeps array broadcasting and the
logical-to-double promotion (`uplus(true)` is 1) in one place. `xor` is the family's one true
builtin, having no operator to defer to.

### Two corrections that came out of the work

- `isequal(NaN, NaN)` returned **true**, because the number comparison went through
  `double.Equals`, which treats NaN as equal to itself. That is `isequaln`'s reading, not
  `isequal`'s. `JgsStdlib.DeepEquals` now takes a `nanEqual` flag: false for `isequal`, true for
  `isequaln`/`isequalwithequalnans`.
- `MapToBool` threw on a matrix (an array of rows) instead of recursing, so `isnan(A)` on a matrix
  failed. It now recurses the way `MapNumeric` always has, and every new mask predicate benefits.
- The parser accepts unary `+`. `uplus` existing while `+a` was a syntax error would have been the
  worst of both.

## Consequences

- Builtin coverage goes from 109/515 to **185/515**; across every callable kind, 273/2,027.
- `startswith`/`endswith` no longer exist. The guide, ADR 0019's mention, and the string tests moved
  with them.
- `AutoCallsBare` is a general mechanism, not a special case for `eps` — the later batches' constant
  families (`namelengthmax`, `cputime`, `filesep`) can use it as-is.
- Known gaps recorded rather than hidden: `true(n)`/`false(n)` as logical-array constructors are a
  lexer/parser change, since both words are keywords today.

## Testing

`MatlabConstantBuiltinTests`, `MatlabTypePredicateTests`, `MatlabTrigBuiltinTests`, and
`MatlabOperatorFunctionTests`. The operator tests compare each function against the operator it
stands for on the same operands rather than against a hand-computed answer, which is the property
that actually matters; the constant and trig tests use MATLAB's own values, including the exact-zero
quadrant cases.
