# ADR 0021 — JGS C-style expression semantics

## Status

Accepted (M20a, 2026-07-17).

## Context

JGS assignments were statement-only (`AssignStmt` / `IndexAssignStmt`), there were no compound
assignment or increment/decrement operators, and the grammar demanded `{` on the same line as its
`fn`/`if`/`for`/`while` header. Users coming from C, C#, JavaScript, or MATLAB expect `x += 1`,
`i++`, braces on their own line, and `let [X, Y] = meshgrid(...)`-style multiple results.

## Decision

1. **Assignment is an expression.** The statement nodes were deleted; the grammar gains a lowest-
   precedence, right-associative `ParseAssignment` level producing `AssignExpr` (`=`, `+=`, `-=`,
   `*=`, `/=`, `%=`) and `IncDecExpr` (`++`/`--`, prefix and postfix). Targets are restricted at
   parse time to a variable or an array element. An assignment evaluates to the stored value, so
   `let y = (x += 2)` and `a = b = 0` work.
2. **Semantics reuse the existing operator machinery.** Compound forms funnel through the same
   `ApplyBinary` dispatch as their plain operators, so `xs += 1` broadcasts elementwise over arrays
   and `s += "x"` concatenates, exactly like `xs = xs + 1` / `s = s + "x"`. Element targets evaluate
   the container and index expressions exactly once (`a[f(i)] += 1` calls `f` once). `++`/`--`
   require a numeric (or bool) current value; postfix yields the old value, prefix the new.
3. **`let` is still required for the first binding.** Assigning to an undeclared name remains an
   error — the deliberate JGS safety net — which also catches most `if x = 5` typos even though
   assignment-in-condition is now legal (documented footgun in the scripting guide).
4. **Newline leniency.** `SkipSeparators()` now runs before a block's `{`, between a function's name
   and its parameter list (definitions only), and after `else` — so `fn f\n(x)\n{`, `if c\n{`, and
   `else\nif` all parse. Newlines after binary operators remain significant (statement separators).
5. **Destructuring `let`.** `let [a, b] = expr` binds the elements of an array whose length must
   match the name count — the consumer for multi-result builtins such as `meshgrid` (M20b).

## Consequences

- **Breaking:** `5--3` no longer parses (the lexer now reads `--`); write `5 - -3`. This matches C.
- The live-edit structural comparer (`AstEquals`) compares assignments as expressions inside
  `ExprStmt`; the debugger suite passes unchanged.
- Editor highlighting/completion needed no changes (operators are not colored; the tolerant lexer
  shares `Tokenize`).
- New tests: `JgsExpressionSyntaxTests` (24 cases: compound/inc-dec semantics, single-eval, chained
  assignment, newline leniency incl. the two-statement negative case, destructuring errors).
