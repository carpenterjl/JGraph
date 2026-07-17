# ADR 0016 — Set next statement, live edit while paused, and the Data Viewer

Date: 2026-07-17
Status: Accepted

## Context

M14 delivered the JGS debugger core; M15 adds the interactive rest of the MATLAB-style wish list:
dragging the current-execution arrow (set next statement), editing code while paused with the edit
taking effect on resume, and a tabular Data Viewer for arrays and tables. All three build on seams
M14 reserved: the jump-index return of `IJgsDebugHook.BeforeStatement` and the shared statement lists
behind `BlockExecution`.

## Decisions

### 1. Set next statement: a jump index plus an immediate re-pause

`JgsDebugSession.TrySetNextStatement(sourceId, line)` is valid only while paused and only for a
statement **of the block execution is paused in** — jumping into a nested block or another frame is
unsound (its scopes and loop variables were never established) and is rejected with a message, as is a
request from a different file (line numbers only mean something in the paused document). On success
the session stores the pending jump and wakes the gate; the blocked `BeforeStatement` returns the jump
index, the interpreter's block cursor moves, and the very next `BeforeStatement` — now at the target —
re-pauses with `PauseReason.EntryJump`. The user is genuinely paused *at* the target: variables and the
call stack refresh, F10 executes the target statement, skipped statements simply never ran, and a
backwards jump re-executes. The UI raises the request from an arrow drag or the gutter's right-click
"Set next statement here"; both funnel into the same API and a rejection lands in the status bar.

### 2. Live edit: in-place list mutation is the whole mechanism

The parser produces one `List<Stmt>` per block, and **everyone shares the reference**: the AST node
(`WhileStmt.Body`…), the executing `BlockExecution` cursor, and every closure holding the enclosing
`FnStmt`. `TryApplyEdit(sourceId, newCode)` therefore mutates those lists in place — while paused, the
interpreter thread is blocked inside the hook, which makes the mutation race-free by the same invariant
that makes variable inspection safe. Because later loop iterations re-read the same list, an edit to a
loop body applies from the next iteration; because closures hold the same `FnStmt`, a refreshed function
body applies to them too. M14's `BlockExecution.Replace` seam is superseded and was removed: swapping
only the cursor's list could never reach the AST node that re-executes on the next iteration.

**Compatibility rule** (checked fully before anything mutates; incompatible edits change nothing and
the host offers a restart):

- The new code must parse.
- Every block on the execution stack belonging to the edited file is resolved to its counterpart in
  the new program: the main/`run()` root maps to the new program; a stacked function's body maps to
  the same-named top-level `fn` (same parameters — a stacked function's signature cannot change);
  a nested block maps through its parent's current statement, which is **pinned** — same header and
  same other branches (`AstEquals.EqualExceptSlot`), only the branch being executed may differ.
- Per level: statements **before** the current index must be structurally equal (`AstEquals`, which
  ignores line/column — so whitespace and comment shuffles are always compatible and the marker just
  moves), except that a top-level `fn` above the execution point may change freely (its code does not
  run by re-executing the declaration; the refresh pass below owns it). The statement at the current
  index of a non-innermost level executing a call is pinned entirely; the paused statement itself has
  not run and swaps like the tail. A function appearing twice on the stack (recursion) is not editable.
- Whole-file function refresh: top-level `fn`s of the edited file not on the stack get their body list
  contents swapped in place when the signature is unchanged (closures and aliases included); new or
  re-signatured functions are hoisted into the globals exactly like a fresh run would (the hook's
  `RunStarting` hands the session the interpreter and globals for this).

Honest limitations, by design: the header of a loop/if you are inside cannot change; code that already
ran cannot change (it already happened); a deleted function stays bound; and `run()` reads from disk,
so an in-memory edit to a not-yet-included file is overridden when the include executes (save first —
MATLAB semantics). Breakpoints are not remapped by the session — the UI re-sends the gutter's current
line set after an applied edit, which reflects what the user sees.

**UI flow**: the workspace window keeps a per-document baseline (the text the run started with, updated
on each applied edit). Any resume gesture (F5/F10/F11) first applies pending edits; a compatible edit
is silent (console note, marker moves via `LiveEditResult.NewLocation`); an incompatible one asks —
restart with the new code, keep debugging the old code, or stay paused.

### 3. Block tracking: `EnterBlock`/`ExitBlock` on the hook

For the path resolution above, the session needs to know *which* blocks execution is inside. The hook
gained `EnterBlock`/`ExitBlock` (called only on the hooked path; plain runs still pay one null check
per statement) and `EnterFunction` now passes the `FnStmt` declaration itself. Each stack entry
classifies itself on entry: a function body (the entry follows `EnterFunction`), a nested block (its
list is reference-identical to a child slot of the parent's current statement — `AstChildren` gives
statements a uniform slot view), or a root (the main program or a `run()` include).

### 4. Data Viewer: a UI-free adapter, a paged grid

`TableGridAdapter` (JGraph.Data, fully testable) projects a `Table` or a numeric array to headers +
formatted cell text with `PageSize`-row windows, so a million-row table never materializes as UI rows.
`DataGridTableControl` (JGraph.Controls) renders one page in a virtualized, read-only `DataGrid` with
page navigation; JGraph.Controls now references JGraph.Data (UI-free, layering-legal). The workspace
window feeds it from three places: double-clicking a `.csv`/`.xlsx` in the Files tree, double-clicking
an array/table variable while paused or after a run, and it lives in its own dock pane (restored into
old layouts by the M14 `EnsureKnownPane` mechanism).

## Consequences

- Everything above the UI is exercised black-box through `JgsDebugSession`/`TableGridAdapter` tests:
  forward/backward jumps (proving skipped statements never ran), jump rejections, tail/loop-body/
  function-body edits taking effect, hoisting of functions added mid-pause, the incompatibility cases,
  and adapter paging. Verified live in the app end-to-end (drag, right-click, live edit → figure title).
- The compatibility rule is monotonically relaxable — every future loosening (e.g. allowing a loop
  header change) only turns previously rejected edits into applied ones.
- M16 (completion) is unaffected by these seams; it builds on the lexer only.
