# 0099 — A scalar loop runs on registers

Date: 2026-08-28 · Status: accepted (M98; plan item B8)

## Context

The interpreter walks a tree. For most scripts that is invisible — the arrays are large and the
walk's cost drowns under the kernels' — but the head-to-head suite keeps one row that is nothing
*but* walk:

```matlab
acc = 0;
v = 1.0;
for k = 1:2e6
    v = mod(v * 1.0000001 + 0.001, 2);
    if v > 1
        acc = acc + v;
    end
end
```

Two million iterations of scalar work, the anti-vectorized case. Before this milestone the row cost
**1.23 s against MATLAB's 0.014** — roughly 600 ns per iteration against 7 — and it was the last
thing keeping `d03_total` behind. Each iteration paid for: a `JgsValue.Number` minted per read of a
literal and per intermediate result, a dictionary walk per variable read and write, a delegate-built
`Func` per operator application, a boxed argument list per `mod` call, and the range `1:2e6`
materialized to sixteen megabytes before the first iteration ran. None of that is the arithmetic.
The plan sized the honest fixes: allocation relief and cached dispatch buy 1.3–1.5× combined and
are not sufficient; compiling the loop body is what moves the row.

## Decision

**A MATLAB `for` over a range, or a `while`, whose body works entirely in scalar doubles compiles
once to a linear op array over an unboxed double register file, and runs on it.** `LoopCompiler`
(two passes: vet and measure, then emit) produces a `RegisterProgram`; a partial of the interpreter
executes it in a switch loop. The compilation is cached on the loop's AST node; a snapshot of every
statement reference under the loop is re-verified at each entry, because a debug hook can edit
statement lists in place (a hooked session never takes the fast path at all, but its edits outlive
the hook).

The compiler is a whitelist, and everything outside it refuses — the loop stays on the tree walk,
which is always correct. What compiles: assignments to plain variables (suppressed only), `if` /
`elseif` / `else`, nested `for` over ranges and nested `while`, `break`/`continue`, the arithmetic
and comparison and logic operators, and calls to `sin cos tan atan exp abs floor ceil round sqrt
log log10 asin acos mod rem atan2 min max` on scalars. What refuses, deliberately: any indexing,
any other call, `return`, globals, unsuppressed statements, and every JGS-dialect loop (JGS has
block scoping; this is a MATLAB-shape fast path).

Four rules keep the two roads inseparable in output:

1. **There is no second implementation of any arithmetic.** Every op binds the same code the walk
   applies: `+ - * /` are the same IEEE operations `NumericBinary`'s lambdas perform, `mod` and
   `rem` are the same statics the builtins now map (`ScalarMod`/`ScalarRem`, hoisted from their
   lambdas), the unary kernels are the same `Math.*` methods and the same domain predicates the
   `MathX` registrations name, `min`/`max` of two scalars are the same `Math.Min`/`Math.Max` the
   reduction wrapper hands to, and the loop variable is `start + i*step` — the exact expression
   `PackedMath.Fill` materializes a range with. A comparison's answer is 1.0 or 0.0 in a register
   and spills as the `Bool` the walk mints; every op's static kind (number or logical) rides on the
   commit, and `t = u` copies the kind at run time because only run time knows it.

2. **The cases a register cannot hold bail to the walk, mid-run.** `x^y` with a negative base and a
   fractional exponent leaves the reals; so do `sqrt`, `log`, `log10`, `asin`, `acos` outside their
   domains. A guarded op that trips spills the dirty registers, hands its *whole statement* to
   `Execute`, and — if every variable is still a real scalar afterward — reloads and resumes at the
   next statement, honoring a Break or Continue the statement produced. A condition bails lighter:
   conditions assign nothing, so the walk just re-evaluates the expression and the program branches
   on its truthiness (which also answers complex comparisons by real part, NaN's truthiness, and
   everything else `IsTruthy` means). A nested range's bound does the same and deposits a number. A
   nested range the walk would refuse — zero step, over the element limit — bails its statement so
   the walk throws the identical error from the identical state. And when a bailed statement leaves
   a variable no register can represent (the answer went complex and stayed), the walk **finishes
   the entire loop** from exactly that point: remaining statements of each enclosing block, then
   each enclosing loop's remaining iterations, innermost outward, in the walk's own order.

3. **The bookkeeping is charged where the walk charges it.** One step per statement, one per
   iteration, against the same 50M limit with the same error text, so a script that dies at the
   step limit dies at the same count with the same words either way. The cancellation poll runs
   once per iteration where the walk polls once per statement — the one deliberate coarsening,
   invisible in any output since cancellation is asynchronous to begin with. Variables spill to the
   environment exactly where the walk would have left them: at completion, at every bail, at the
   step limit, on cancellation — a loop variable by declaration, everything else assigned outward
   with declaration as the fallback, which is `EvaluateAssign`'s own rule.

4. **Entry is a revalidation, not a promise.** Each time the loop statement executes: every
   variable some read may see before a compiled write must be bound to a plain real scalar double
   or bool with no numeric class (an `int32` scalar refuses — classed arithmetic saturates, and
   that is the walk's business); every builtin the program bound must still resolve to the builtin
   of that name (a shadowed or rebound `mod` refuses, and the walk then indexes into the variable
   like MATLAB does); a bare `pi` or `eps` must still be the auto-calling constant, folded once at
   entry because its answer never changes; and no name may be `global`. The root range's bounds are
   then evaluated once with the full evaluator — they may be arbitrary expressions, side effects
   included — and the count computed by `EvaluateRange`'s exact rule, its errors thrown verbatim,
   without sixteen megabytes of range ever existing.

`JGRAPH_LOOP_JIT=0|1` forces the mode (default on), and `JgsLoopJit.Enabled` is the parity-test
lever, the same shape as `JGRAPH_JGS_PACKED`.

## What this did not close, and what it cannot

- **The row is 3.7× behind MATLAB, not at parity.** ~26 ns per iteration is a switch-dispatched
  interpreter against a JIT; the plan predicted 2–6× and this lands inside it. The plan's option
  (iv) — IL emission over this same program, reaching MATLAB's 4–8 ns — remains the decision point
  it was, to be commissioned only if this row is declared must-win. The IR was built so that would
  be an alternate backend, not a rewrite.
- **A tiny loop entered many times pays entry each time.** Validation, register loading and spill
  cost on the order of a microsecond per entry; a three-iteration inner loop whose enclosing loop
  refused to compile pays it per entry and wins little back. Measured, that worst case is a wash
  against the walk (0.370 vs 0.395 median, ranges overlapping) — the compiled iterations pay for
  the entry — but it is overhead the enclosing loop would have amortized had it compiled.
- **`arrayfun`, `cellfun`, indexing, user function calls** stay walked. The whitelist grows by
  need, not by ambition; every addition must bind the walk's own kernel and prove byte-parity.

## Consequences

- Two new files (`LoopCompiler.cs`, `RegisterProgram.cs`), one new interpreter partial
  (`Interpreter.HotLoop.cs`), and a kernel-binding partial on the builtins
  (`JgsBuiltins.HotLoop.cs`). The interpreter's tree walk is untouched except for the two
  three-line hooks at the top of `ExecuteFor`/`ExecuteWhile`.
- `mod` and `rem`'s scalar cores are named statics now (`ScalarMod`, `ScalarRem`); their builtins
  map the same statics, so the two roads cannot drift.
- A compiled `for` never materializes its range. The walk still does; `x(1:2e7)` still does — the
  range-indexing cost ADR 0094 first recorded is untouched by this and remains open.
- The register file is allocated per entry (a few hundred bytes), so recursion and re-entry need no
  pooling discipline.

## Measured

Release, i7-11700F, rested box (nothing else running, zero stray testhosts), six alternating
runs per row with the lever flipped between them, medians quoted with ranges in parentheses.
The walk column is also the pre-M98 cost, since with the lever off the new code never runs.
Checksums identical between the two roads on every run.

| script | walk | compiled | ×
| --- | ---: | ---: | ---: |
| the d03 2M-iteration loop, via CLI | 1.201 s (1.167–1.274) | 0.052 s (0.050–0.055) | 23× |
| nested pair, 1M inner iterations | 0.435 s (0.430–0.447) | 0.025 s (0.024–0.025) | 17× |
| worst case: 3-iteration inner loop entered 200K times, outer refused | 0.395 s (0.382–0.451) | 0.370 s (0.306–0.399) | a wash |

Against MATLAB, through the head-to-head harness: `d03_loop_2M` **0.052 s (0.047–0.052) vs
0.014 — 3.7× behind, from 86× behind**, `CHK|d03_loop` equal to MATLAB's digit for digit. The gate
`loop_2M ≤ 0.08 s` is **met**. That row was ~1.2 s of `d03_total`'s ~4.9; the total drops to
~3.9 s, ahead of MATLAB's — the full rerun's exact totals are in the implementation log. About
26 ns per compiled iteration against the walk's ~600 and MATLAB's 7.

## Testing

- `MatlabLoopJitM98Tests` (33): every script runs compiled and walked and the output must be
  byte-identical at seventeen significant digits — the benchmark loop, break/continue and nested
  loops, while loops over logical flags, every whitelisted kernel over awkward values, signed
  zeros and NaN and infinities through the registers, NaN truthy in a condition, `pi`/`eps` folded,
  the logical class of a copied comparison, loop variables after empty ranges and after break,
  answers that leave the reals (per-statement bail, full deopt, bail inside an if, bail inside a
  condition, an escape three frames deep, a break landed from a walked continuation), compound
  assignment, NaN equality, the range errors (zero step, over-limit, nested
  both), undefined names, a shadowed `mod`, a global, a classed integer, an indexed write, root
  bounds evaluated exactly once, function frames and persistents, and an unsuppressed assignment
  echoing from the walk. Where it matters the test also asserts the fast path really ran (or
  really refused) via a compiled-runs counter, because a fast path that silently never fires
  passes every parity test while buying nothing.
- The four-lane suite and the 59-script stress corpus run with the compiler on by default, so every
  loop in every corpus script now exercises it against recorded output.
- `LoopJitBenchmarks` records the 2M-loop and nested-pair costs both ways.
