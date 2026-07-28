# ADR 0041 — Builtin coverage, batch 2

## Status

Accepted (M38, 2026-07-28).

## Context

M37 took the four builtin families that needed no new machinery and moved the count from 109 of 515
to 185. [matlab-builtin-coverage.md](../matlab-builtin-coverage.md) recorded the rest as ~145
implementable and ~185 deliberately excluded. This milestone takes essentially all of that
implementable remainder — **185 → 326 of 515** — and, in doing so, has to decide where "implementable
on the current value model" stops being true.

## Decision

### Two workhorses, not fifteen approximations

The special functions (`erf`, `gamma`, the incomplete integrals, the polygamma derivatives) go into
one new `JGraph.Numerics.SpecialFunctions`, and everything in it is written in terms of two pieces of
code: a Lanczos log-gamma and modified-Lentz continued fractions. Accuracy is then a property of two
routines rather than of a table of per-function approximations, and the pieces compose — `erfc(x)` is
`Q(½, x²)`, which is why `erfcx` can cancel the exponential *exactly* instead of computing
`exp(x²)·erfc(x)` and overflowing. `erfc(30)` is 2.6e-393, flat zero as a double; `erfcx(30)` is
0.0188 and correct to fifteen digits.

Every inverse (`erfinv`, `gammaincinv`, `betaincinv`) brackets and bisects rather than starting a
Newton iteration from a guess. It is slower and unconditionally correct: these functions are monotone,
so there is no starting guess for a hard case to spoil, and at the sizes a script works with the
difference is invisible.

`Gamma` special-cases whole numbers and multiplies the factorial out, because `gamma(3)` has to be 2
and `exp(lnΓ(3))` is 2.0000000000000018.

### Where accuracy runs out, say so

**The Bessel family and `airy` are not implemented.** They need an AMOS-class kernel to stay accurate
across the whole order/argument plane; the tractable approaches (power series, Hankel asymptotics)
each leave a band where they lose most of their digits. Shipping something good to eight digits under
a name engineers use for filter design and waveguide maths is worse than shipping nothing, so the
coverage document records them as planned rather than done. The same reasoning excludes `qz`,
`ordschur`, `cholupdate`, and `delaunay`.

### The interpreter declares what only it can

`eval`, `evalin`, `assignin`, `exist`, `who`, `str2func`, `narginchk`, and the error history need the
running scope, so they follow the pattern `save`/`load`/`clear` and the M37 operator forms already
use: the two workspace owners (`JgsRunner`, `JgsReplSession`) call
`JgsBuiltins.RegisterEvalBuiltins(env, interpreter, host, dialect)` right after building an
interpreter, and `JgsScriptEngine.BuiltinNames()` lists the names explicitly so the catalog-to-
registration parity test still holds.

The scope those builtins see is a new `Interpreter.CurrentFrame`, set in `ExecuteFunctionBody` and
restored in its existing `finally`. Tracking the *function frame* rather than every block keeps the
per-statement path untouched — and in the MATLAB dialect a block has no scope of its own anyway, so
the two are the same thing where it matters.

`evalc` needs the console instead of the interpreter, so `JGraphScriptGlobals` gained a capture
buffer that `print`/`WriteOut` divert into. It is a single buffer, not a stack: MATLAB's `evalc` does
not nest meaningfully, and the buffer is closed in a `finally` so a failure part way through cannot
swallow the rest of the session's output.

### cd changes where relative paths resolve

`cd` sets a directory on `JGraphScriptGlobals` that `Resolve`/`ResolveForWrite` consult *before* the
script's own folder. Without that, `cd` would report a new folder while every subsequent `fopen`
still opened files in the old one — worse than not having `cd` at all.

`rmdir` refuses a non-empty folder without MATLAB's `'s'` switch. Deleting a tree because the caller
mistyped a name is not a recoverable mistake.

### true and false gain a second reading

`true(2,3)` and `false(n)` are logical-array constructors, but both words are lexer keywords, so the
call form is recognized in `ParsePrimary`: a `True`/`False` token followed by `(` becomes a
`VariableExpr` naming the builtin, and on its own it is still the literal. Both readings are pinned
by tests, because the failure mode of getting this wrong is that `if flag` stops working.

### Two documented differences stay documented

- `A(i, j)` is still not two-subscript indexing — a JGraph matrix is an array of rows, indexed
  `A(i)(j)`. This is the largest fidelity gap left and is its own milestone.
- `true == 1` is false, because `JgsValue.AreEqual` requires matching types. MATLAB treats logicals
  as numeric. It is a one-place fix with a wide blast radius (`==`, `isequal`, `ismember`, `switch`),
  so it is recorded in the coverage document rather than changed inside a coverage milestone.

## Consequences

- Builtin coverage goes from 185/515 to **326/515**; across every callable kind, 491/2,027.
- `JGraph.Numerics` gains `SpecialFunctions` and `LinearAlgebra/Factorizations` (Cholesky, LDLᵀ,
  Hessenberg, matrix exponential) — both usable from C# and Python scripts too, not just JGS.
- The LDLᵀ factorization pivots 1×1 symmetrically. A matrix that genuinely needs a 2×2 block pivot
  (`[0 1; 1 0]`) reports that clearly rather than returning nonsense.
- `rcond` computes the exact reciprocal condition number instead of LAPACK's estimate, so its values
  differ from MATLAB's in the last digits — more accurate, not less.
- `double` and `single` now exist, which is how a script turns a logical mask or a character code
  into arithmetic.

## Testing

Seven new script-level suites — `MatlabNumericBuiltinTests`, `MatlabSpecialFunctionTests`,
`MatlabMatrixBuiltinTests`, `MatlabTextBuiltinTests`, `MatlabArrayBuiltinTests`,
`MatlabEvalBuiltinTests`, `MatlabGeometryBuiltinTests` — plus `SpecialFunctionsTests` against the
kernel directly.

The assertions are chosen so that a plausible wrong implementation fails: factorizations are checked
by reassembling the matrix they came from, the special functions against closed forms and each
other's identities, the moving statistics at the ends where the window shrinks (an even window covers
the current element and the one *before* it), and the file builtins in a temporary folder of their
own so nothing depends on where the suite was started.
