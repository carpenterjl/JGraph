# ADR 0044 — Stress-test language fidelity: N-D arrays, implicit expansion, nested functions

## Status

Accepted (M41, 2026-07-29). Extends [ADR 0043](0043-shaped-arrays.md) (shape on the value wrapper)
and partly supersedes the "2-D only" reading of it.

## Context

Sixteen user-written MATLAB stress-test scripts were run through `jgraph -batch` and reduced to
per-feature probes. They exposed two process-killers (a 400-deep recursive script died of a real
.NET stack overflow; a loop growing a matrix one row and column per step was O(n³)), a family of
parser gaps (`if cond, stmt; end`, one-line `function` bodies, nested functions, `persistent`), and
the largest value-model gaps left after ADR 0043: no third dimension, no implicit expansion, no cell
shape, no struct arrays. This ADR records the decisions; the M42/M43 ADRs cover the numeric and
data-type builtins the same scripts need.

## Decisions

1. **Scripts run on dedicated 16 MB-stack threads** (`ScriptThread`, replacing `Task.Run` at every
   engine/session/debugger entry point). The interpreter costs a few kilobytes of native stack per
   script-level call, so on a 1 MB pool thread the uncatchable stack overflow always fired before
   the interpreter's own depth guard could. With the big stack, the guard — raised to **512**, just
   over MATLAB's default 500 — trips first and is an ordinary catchable script error.

2. **Growth is amortized in place under MATLAB's copy-on-assign.** A packed matrix grows through
   `JgsValue.TryGrowInPlace`: the buffer over-allocates half-again per dimension and carries a
   private column stride; `AsBuffer` compacts on sight, so no raw-buffer consumer can ever observe
   the slack, and the indexed write paths use capacity-aware element accessors so the growth loop
   never triggers compaction. JGS keeps the rebuild-and-rebind behavior — its reference semantics
   make in-place mutation observable through aliases. The 5000-step `A(i,i)=i` loop went from hours
   to 0.25 s.

3. **A comma is a MATLAB statement separator** (display, where `;` suppresses), which is what makes
   `if cond, stmt; end` and one-line `function f(), body; end` parse. `SkipSeparators` and
   `IsStatementEnd` gain the comma under the MATLAB dialect only.

4. **Nested functions exist in end-closed files.** A token pre-scan (`DetectFunctionEnds`) settles
   the file's style — MATLAB's own rule is all-or-nothing per file — because the answer is needed
   while the first function is still being parsed. In the end-closed style a `function` inside a
   body is an ordinary statement; `ExecuteFunctionBody` hoists nested declarations (a handle taken
   before the declaration line must resolve) and each closes over the parent's live call frame, so
   assignment write-through gives MATLAB's shared workspace.

5. **`persistent` is per function declaration**, stored on the interpreter keyed by `FnStmt`,
   initialized `[]`, written back from the call's `finally` (a value assigned before a later error
   still persists). Cleared with the session, not by `clear`.

6. **Arrays are N-D.** Shape beyond 2-D is an `int[]` on the wrapper (trailing singletons trimmed);
   `_rows`/`_cols` hold MATLAB's own 2-D fold — `dims[0]` by `prod(dims[1..])` — so every
   two-subscript reader sees exactly the fold MATLAB defines and only `size`/`ndims`/N-subscript
   indexing consult the truth. N-subscript reads and in-range writes fold trailing dimensions into
   the last subscript when there are fewer subscripts than dimensions, and index singleton
   dimensions beyond the rank. N-D growth, deletion, and per-page display are deliberately absent
   (display prints the fold).

7. **Implicit expansion is one engine** (`JgsBroadcast`): dimension by dimension, sizes match or one
   is 1. The elementwise operators, `bsxfun`, and the comparisons all route through it, so a column
   plus a row is their outer sum and a 1×1 array behaves as a scalar. Two plain vectors of clashing
   lengths keep the historical "different lengths" message. The ADR 0043 same-length row/column zip
   leniency is **withdrawn** — MATLAB's outer expansion wins, which is what the stress tests assert.
   Exposed and fixed in passing: `linsolve` returned a row where `\` returns a column, and
   `real`/`imag`/`angle`/`abs`/`conj` dropped the shape of complex matrices.

8. **Vector reductions return scalars** (`sum`/`max`/… of a row or column), and the shape-keeping
   reductions (`cumsum`, `sort`, `diff`) carry a column's orientation through.

9. **Cells carry the same wrapper shape arrays do**: `cell(r, c)` is a grid, `C{r, c}` reads and
   writes column-major over it. `cell(n)` stays 1-by-n (documented divergence; MATLAB makes n-by-n).

10. **A struct array is a cell of structs.** `S(n).f = v` creates or grows it (MATLAB's own
    preallocation idiom), `S(k).f` reads and writes the element through a one-element sub-cell
    unwrap, and `S(1).f = v` on a scalar struct writes the struct itself. Divergences, accepted and
    documented: `class(S(k))` says cell, and field sets are per-element rather than uniform.

11. **`run('file.m')` executes under the file's dialect**, not just parses in it — the interpreter's
    dialect is swapped for the include (`RunInDialect`). Known limit: a function the include defines
    runs its body under whatever dialect is active when it is later called.

12. **`clear`/`clearvars`/`whos` are workspace builtins everywhere** (shared
    `JgsRunner.DefineWorkspaceBuiltins`), not session-only: batch runs and breakpointed runs have
    them too. `clear` takes names; plain `clear` spares the functions a script defined
    (`Interpreter.ScriptFunctionNames`) the way MATLAB's does — `clear all` drops those as well.

## Consequences

- The stress scripts stess_2, stess_7, stess_11 and stess_16 pass end to end; stess_1/3/4/5 pass
  every section that does not need an M42/M43 builtin (complex predicates, `polyval`, `hilb`,
  `table`, integer classes).
- `[1] == [1, 2]` is now `[true, false]` (scalar expansion), and a column minus a same-length row is
  a matrix — both MATLAB's answers; four tests that had encoded the old zip were updated.
- `length` of a shaped matrix is now MATLAB's largest dimension; the nested (pre-shape) form keeps
  its JGS item-count reading.
- `tic`/`toc` auto-call on their bare names (`t = toc` stores seconds, not a function).
- `MatlabStressM41Tests` (26 facts) pins all of the above.
