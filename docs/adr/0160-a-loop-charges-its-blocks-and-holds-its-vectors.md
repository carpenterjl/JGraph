# ADR 0160 — A loop charges its blocks and holds its vectors

## Status

Accepted. Item 12, second stage, of the head2head_v3 gap-closure plan
(`docs/plans/gap-closure-07-12-plan.md`, rev 6, agreed with Codex): 12b, the fused branches and
the charge blocks; 12c, the unchecked register file behind an opcode-aware validator; 12d, the
guarded vector class and the walked statement. The first stage (12a, 12e.1) is ADR 0159; the
borrowed argument (12e.2) is ADR 0161.

## Context

At the stage-1 commit (81d2e3b) the hot-loop compiler of M98 (`LoopCompiler.cs`,
`Interpreter.HotLoop.cs`) turned a scalar-double `for` or `while` into a register program and
ran it through one `switch`. The d03 benchmark loop — two million passes of
`v = mod(v * 1.0000001 + 0.001, 2); if v > 1, acc = acc + v; end` — was fourteen dispatches an
iteration: a `Step` charge for every statement, a comparison that materialised a 0/1 and a
`JumpIfFalse` that read it back, and a back edge (`ForNext`) that returned to a head of three more
ops (`ForHead`, `IterTick`, `ForBind`). The Release JIT kept a bounds check on every register
read and write in the switch: the disassembly of `RunHotLoop` on that loop
(`DOTNET_JitDisasm=RunHotLoop`, `DOTNET_TieredCompilation=0`) showed ninety-six `jae` branches,
three per arithmetic op. And the whitelist refused any loop that touched an array — a read
`x(k)`, a write `y(k) = s`, a whole-vector combination — or held a single statement outside it
(a `fprintf`, a cell store), which is most loops a script writes.

The plan's review had fixed two things the compiler must not move. The step limit and
cancellation are observable — the catch spills the registers, so the partial state a loop leaves
when it is stopped is part of the contract — and unsafe register indexing needs the JIT's
disassembly inspected before any check is removed. The stage was therefore staged so that every
piece is a pure win on the benchmark loop with the same final state, measured alone.

## Decision

**12b — the fused branches and the charge blocks.** A comparison that decides a branch is one
op: `UnlessLt`/`Le`/`Gt`/`Ge`/`Eq`/`Ne` jump to the false side, exactly as `Lt` followed by
`JumpIfFalse` did (a NaN operand takes the jump; `UnlessNe` jumps on equality), and the
comparison's value is never materialised, which nothing can observe: a condition binds no
variable and a bail re-evaluates the whole expression by the walk. A `for` loop's back edge is
one op, `ForStep`: advance the index, exit if done, and otherwise the head's own three ops in the
head's own order — the cancellation poll, the iteration's charge, the bind of the loop variable —
and a jump to the body's first op; the head itself stays for the first pass.

The step charges of a *charge block* are paid at once. A charge block is a maximal run of
consecutive ops with no terminator inside it (a jump of any kind, an op that can bail — the
guarded power and call, the range count, every vector op, the walked statement — the iteration
poll, the halt) and no jump target inside it but at its first op; and the jump targets are only
the labels some jump or bail record refers to, since every assignment statement marks a resume
label that no bail uses. The first `Step` of a block with two or more becomes `StepBlock(n)`, the
others are dropped, and `StepBlock` charges `n` after a precheck: when `steps + n` would pass the
limit it jumps instead to a verbatim copy of the block from that `Step` on, appended after the
halt with a jump back to the op after the block, where the statements charge one by one and the
limit is reported at the same statement, with the same registers, as the per-statement program
reports it. With no transfer inside a block, every statement charged is a statement executed;
with no bail inside it, no bail publishes a step count from its middle. `IterTick`'s poll is
untouched, and the catch now writes the step count back as well as spilling.

**12c — the unchecked register file.** `LoopProgramValidator` proves, opcode by opcode, that
every access the runner can make is inside the array it names: which of `Dest`/`A`/`B` are
registers and which are variable slots (the `written` and `logical` arrays are slot-sized),
which are kernel indices, whether `Arg` is an op index or a bail index, that a for state block
spans `A..A+4`, that every fall-through has a next op, that every bail's resume, branch, break
and continue targets are inside the program, and that a vector op's node is of the type the
runner casts it to — with checked arithmetic. A program the validator cannot prove is a compiler
defect: the compiler refuses the loop and the walk takes over. The runner is generic over an
accessor (`CheckedAccess`, `UncheckedAccess`), so the JIT compiles one body per accessor with
the access inlined and no branch per read; the unchecked body reads through
`Unsafe.Add(MemoryMarshal.GetArrayDataReference(...))` for the register file, the op list, the
kernel tables and the slot flags — the accesses the validator covers, and only inside the switch.
The bail, reload, spill and deopt paths keep the checked arrays. `JGRAPH_LOOP_UNCHECKED=0`
selects the checked body.

**12d — the guarded vector class and the walked statement.** A variable is a vector of the loop
when the body indexes it by one scalar (`v(i)`, `x(i) = s`) or assigns it an elementwise
combination of vectors and scalars; a name that holds a vector of the class as the loop is
entered is a vector name to begin with (`v = v - 0.2` is a vector statement only if `v` already
is one), and the rest follow from the syntax to a fixed point. The class is a packed real double
row or column with no class stamp, time tag, char or string tag and no third dimension; the
entry check refuses anything else, on every entry. The ops are thin: `VLoad` reads one element
unboxed after the validation `PackedOps.ToIndex` applies; `VStore` writes one element in place,
as the walk's own element write does; `VArith` hands the evaluated operands (a scalar boxed as
the walk would hold it) to the walk's own `ApplyBinary` for the operator of the node;
`VCall1` calls the builtin the name resolved to at entry, on the vector; `VNeg` is the walk's
`ApplyUnary`; `VBind` adopts a fresh answer as the walk adopts an owned one and copies another
slot as `CopyForBinding` copies it. The bytes are therefore the walk's bytes by construction —
the plan's requirement that the same-dims elementwise ops be the walk's kernels is met by
calling the walk. Every guard bails the statement to the walk: a position outside the extent
(the walk grows the array, or throws its own words), a fractional position, a logical value in
a variable (the walk demotes the array), an answer outside the class (a row plus a column expands
to a matrix; a kernel leaves the reals; two vectors under `*` are an inner product, refused at
scan and walked), and after the walk runs the statement the reload takes the environment's word
for every slot the statement could have rebound, finishing by the walk when that word is a value
no register can hold or the name is unbound.

A statement the whitelist refuses no longer refuses the loop. The scan is transactional per
statement: whatever a failed scan declared — slots, constants, kernels, entry flags — is rolled
back, and the statement becomes a `Walk` op that spills, executes the statement by the walk,
reloads and resumes, every time; its assigned names count as assigned from there on. A walked
statement may not declare a global or persistent, define a function or call a name that reaches
into the workspace by a road the program cannot see (`clear`, `eval`, `evalin`, `assignin`,
`load`, `run`, `feval`, `builtin`, `input`, `keyboard`) — such a loop stays on the walk — and
after every walked statement each builtin the program bound is checked to still resolve to
itself. A `return` from a walked statement leaves the function with the environment as the
walk left it. `JGRAPH_LOOP_VECTORS=0` restores the whitelist's refusals.

**Found on the way.** A statement bail whose target had never been written by a compiled op and
was not loaded at entry (`b = sqrt(a - 10)` bailing in the first pass) left the register stale
while the walk had made the variable complex: the reload skipped it as "never trusted", and the
next read used the old number. Since M98. Every bail now names the slots the statement rebinds
(`LoopBail.Touched`), and the reload treats them as written. `JGRAPH_LOOP_TRACE=1` now writes
every refusal — the compiler's, with the member that refused, and the entry check's, with the
variable or builtin that failed it — to standard error.

## Consequences

**Contract.** `LoopChargeBlocksM160Tests` runs every script under a sweep of step budgets that
places the limit on every statement of the loop in turn — each block boundary at −1, 0 and +1 —
on the walk and on the compiled road with the fusion and the blocks on and off and the accessor
checked and unchecked, in a session whose workspace survives the failure, asserting the same
outcome, the same text and every variable's bits at every budget: a straight-line block,
`break` and `continue` under a tight budget, `if` arms of unequal length, a guarded call that
leaves the reals mid-loop, a condition that bails and resumes, nested loops and a `while`, a
nested zero step; cancellation between blocks (the walk polls at every statement and the
compiled loop at every iteration, the M98 rule, so a cancellation during the last statement of
an iteration is seen by both at the next boundary with the same variables left); and the six
comparisons over every pair of `0, -0, 1, -1, NaN, Inf, -Inf`. `LoopProgramValidatorM160Tests`
feeds the validator hand-built programs with one bad operand of each kind — a jump past the end,
a register at the file's length, a slot past the slots, a for state block that runs past the
file, a kernel index past the table, a guarded call on an unguarded kernel, a bail index or a
bail target outside the program, a vector operand past the vector file, an opcode it does not
know — and the smallest good ones. `LoopVectorsM160Tests` runs every vector script with the
compiler on and off and asserts the same bytes, with the counters saying which road ran
(`JgsLoopJit.CompiledRuns`, `JgsLoopJit.Bails`): element reads and writes, the elementwise
combinations, orientation, the guarded kernel, the expansion, the classed, matrix, scalar and
complex refusals, the logical store, growth and the refused positions, the inner product, walked
statements of every kind, a walked statement that changes a variable's kind or shadows a builtin
through `assignin`, a `return` from one, the forbidden constructs, and the switch off. The two
M98 tests that pinned refusals now compile (`IndexedWritesCompileSinceTheVectorClass`, the
echoing assignment) and still print the walk's bytes. All of them are lane-aware: the unpacked
lanes hold no packed vector, so the entry check refuses there and both roads are the walk.

`m160_loops` (39 lines from R2025b): the benchmark loop at two hundred thousand passes, the
flow, nested and comparison loops, the kernels, and bits lines over every vector a loop built;
the guarded kernel, the expansion, the inner product, growth, the logical store, four refused
positions, the walked statements, the early return, the char-matrix loop of the d12 row. All 39
agree on the 12a binary, on this build and on this build with `JGRAPH_LOOP_JIT=0` (three lines
by their `div=` rule, below). `stess_87` (twelve sections) holds the invariants no road may
move: closed-form sums, the vectorized truth table of the six comparisons, element and
elementwise loops against the same computation vectorized bit for bit, the bails against a
direct write or call, the walked statements against the walk, the refusals, the char-matrix
loop; it passes with the compiler on and off (21 loops compiled under the switch). The
two-process contract: every stress script run on the Release build under `JGRAPH_LOOP_JIT=0`
and `=1`, stdout compared byte for byte, and the batch stats line (`compiled_loops`,
`loop_bails`, new) read from the second: 87 scripts, every one exiting the same way on both
roads, 84 byte-identical and the other three (stess_5, stess_10, stess_12) differing only in
`rand` values and an elapsed time; 1,326 loops compiled across 12 scripts with 396 bails, zero
compiled under the switch off. Before the rule that a loop of only walked statements refuses,
the same run compiled 1,411 loops across 41 scripts — the 29 scripts that dropped out held
loops of nothing but `fprintf`, cell and struct stores, which the walk runs as it always has.

**Measured** on the same bin under each switch, so each piece is seen alone (`runs\12bcd-loops`,
the working tree built once; `runs\12-no-fusion`, `12-no-blocks`, `12-no-unchecked`,
`12-no-vectors`, `12-no-jit` are that bin with one switch off; medians of five, cold / warm):

| probe | switch off (s) | every switch on (s) | what the switch buys |
| --- | --- | --- | --- |
| `probe_d03_loop_2M`, fusion | 0.0424 / 0.0356 | 0.0346 / 0.0274 | 1.23× / 1.30× |
| `probe_d03_loop_2M`, charge blocks | 0.0344 / 0.0274 | 0.0346 / 0.0274 | 0.99× / 1.00× |
| `probe_d03_loop_2M`, unchecked file | 0.0347 / 0.0274 | 0.0346 / 0.0274 | 1.00× / 1.00× |
| `probe_d03_loop_2M`, the compiler (M98) | 1.3553 / 2.1787 | 0.0346 / 0.0274 | 39× / 80× |
| `vecloop_elements_200k`, vectors | 0.4454 / 0.1297 | 0.0182 / 0.0104 | 24.5× / 12.5× |
| `vecloop_elementwise_20k`, vectors | 0.1373 / 0.0518 | 0.1600 / 0.0474 | 0.86× / 1.09× |
| `vecloop_cellstore_2000`, vectors | 0.0079 / 0.0051 | 0.0121 / 0.0090 | 0.65× / 0.57× |

The fusion is the whole of 12b's gain; the charge blocks buy nothing measurable, and neither does
the unchecked register file — the bounds-check branches the disassembly shows are predicted
branches, and removing a hundred and nineteen of them from the loop's path did not move the
median by a millisecond. Both are kept: the blocks are the design the plan's review asked for
(one charge per straight-line block with the limit reported at the same statement), the
unchecked file is proved sound and costs nothing, and each has its switch. The vector class is
the whole of 12d's gain and it is large where it applies — an element read and write per pass —
and a wash where a pass is a handful of whole-vector operators through the walk's own kernels
(the fresh answer per operator is the walk's cost too; what the class removes is the tree). The
cell-store row is the plan's open question answered: a loop whose only statement walks was a
loss (a spill and a reload per pass for nothing), so such a loop — and such a nested loop —
now refuses and the walk runs it as before. After that rule the same loop, alternating the
switch in one process (`cellstore.m`, three pairs), is 0.0384 / 0.0132 with the compiler on and
0.0380 / 0.0132 with it off; the probe's own row still reads the heap the earlier sections
shaped (`runs\12d-bound-names`, 0.0120 / 0.0090 against the no-jit 0.0084 / 0.0052), which is the
rig's known limit and why the standalone pair is quoted.

Against the stage-1 run (`runs\12a-affine-index` → `runs\12bcd-loops` for the probes,
`runs\12bcd-loops-b` for the script rows; JGraph column, medians of five):

| row | before (s) | after (s) | speed-up |
| --- | --- | --- | --- |
| `probe_d03_loop_2M` cold / warm | 0.0452 / 0.0385 | 0.0346 / 0.0274 | 1.31× / 1.41× |
| `probe_d12_charmatrix` cold / warm | 0.0423 / 0.0140 | 0.0426 / 0.0141 | — |
| `d03_loop_2M` | 0.054 | 0.049 | 1.10× |
| `d03_total` | 2.766 | 2.710 | 1.02× |
| `d12_charmatrix` | 0.022 | 0.021 | — |
| `d12_total` | 1.565 | 1.544 | — |

The loop probe's ratio to MATLAB fell from 2.63 / 3.93 to 2.36 / 3.11. The script rows' MATLAB
column in both stage-2 passes came out two to six times slower than in every earlier run of the
same scripts (d03_total 9.4 → 17.3 s) while MATLAB's probe timings matched the earlier runs; the
cause was not chased, so the script rows are quoted on JGraph's times and the ratios on the
probes. `d03_funs` (arrayfun over a handle, code this stage does not touch) read 0.312 → 0.344
with ranges 0.300–0.338 and 0.339–0.354; the other d03 and d12 rows are within their ranges.
The loop row is still four times MATLAB: the remaining cost is the dispatch itself, one
`switch` per op at about two nanoseconds, and the plan's own position stands — closing that is
a compiler to native code, not another superinstruction.

**Disassembly.** The Release JIT's code for the checked runner on the benchmark loop holds 139
`jae` bounds-check branches (the runner grew from M98's 96 with the new ops); the unchecked
runner holds 20, every one of them on a vector op's checked path or a cold branch, none in the
scalar loop's ops. 12c is not the one-line note the plan allowed for; the checks were there.

**What did not change.** The walk's own loop, its per-statement poll and charge, the M98 bail,
spill and deopt roads, `EvaluateRange`'s count, the affine read of ADR 0159. The ODE rows' cost
is still the bridge and the right-hand side, which 12e.2 addresses; a compiled right-hand side
(the vector class applied to an anonymous function body) is what the plan scheduled after
12d's numbers exist.

## Divergences

All three were found by `m160_loops` on the 12a binary and are kept by this stage; chip
task_56e0bb57 is filed to close them together, and the compiled road agrees with the walk on
each.

- A logical stored into every element of a double vector: JGraph's `class(f)` answers `logical`
  where R2025b keeps `double` (`m160_loops`, `logical_store_class`, `div=ADR0160`). The walk's
  element write demotes a packed buffer to boxed on a Bool value; the compiled `VStore` bails on
  a logical for that reason, so both roads agree.
- A zero-step colon in a loop head: JGraph throws `A range step must not be zero.` where R2025b
  runs the loop zero times over a 1-by-0 range (`refused_nested_zero_step`, `div=ADR0160`). The
  compiled `RangeCount` bails to the walk, which throws.

**Retired by V8 (ADR 0169), and deleted from the list above rather than struck through**: *"The
loop variable after an empty range: R2025b binds it to a 0-by-0 double, so `exist('qq', 'var')` is
1 after `for qq = 5:1, end`; JGraph leaves a new variable unbound"* (`empty_range_new_var`). A loop
that runs no pass now binds its variable to `[]` on both roads, the line is `exact`, and the M98
test is `LoopVariableIsEmptyAfterAnEmptyRange`.
