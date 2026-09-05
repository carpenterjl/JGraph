# ADR 0128 — The prompt talks to the paused script

## Status

Accepted (2026-09-05). A user-experience arc between M125 and M126, outside the solvers plan's
numbering: five commits, each a thing a MATLAB hand reaches for in the window and did not find.

## Context

The interpreter had been measured against MATLAB for a hundred milestones; the window around it
had not. Five gaps were named at once, all of them things one does with the hands rather than in
a script:

1. A script paused at a breakpoint could not be talked to. The prompt said "Busy — stop the
   current run first", and the run had its own workspace anyway — ADR 0109 gave a debug run a
   fresh environment so a breakpoint could not change what a script meant, and the console said
   so at every breakpointed F5. In MATLAB a paused script is in the base workspace and the prompt
   reads `K>>`.
2. The Data Viewer was a read-only snapshot of the moment a variable was opened.
3. Nothing in the editor went to where a name was defined.
4. The console opened in JGS, the first engine registered, and forgot any other choice on close.
5. Hovering a name showed nothing.

## Decision

### A breakpointed run joins the console workspace

The interpreter's debug hook is attachable between statements (`Interpreter.DebugHook`), so the
live `JgsReplSession` runs a document under it and takes it off again for the next prompt.
`JgsDebugSession.RunAsync(IScriptSession, …)` is the road in; the one-shot overload stays for a
host without a session. ADR 0109's reason for the separate environment — the handle registry
rewinding, figures being reset — was already answered by that ADR's "nested run" rule, and running
inside the session is the same rule taken to its end: nothing is reset because nothing is
started. What the paused script sees is what the prompt defined; what it leaves is there
afterwards; a function defined at the prompt is stepped into like any other.

### `K>>` borrows the blocked interpreter

While paused, the interpreter thread is blocked at a gate, which is what has always made the
Workspace pane's reads race-free. `EvaluateAsync` extends the same fact to a write: a typed
statement runs on another thread inside the same interpreter, in the environment of any frame of
the call stack, with its own token and step budget and with that frame as `CurrentFrame` (so
`eval` and `who` answer about it). The hook stands aside for the duration — a breakpoint inside
anything the statement calls does not fire, and the block and frame stacks are not touched — and
the session refuses Continue, a jump or a live edit until the statement is done; Stop waits for
it before unwinding, so two threads are never tearing down one interpreter.

`dbcont`, `dbstep`, `dbstep in`, `dbstep out`, `dbstep N`, `dbquit`, `dbstack`, `dbup` and
`dbdown` are recognised by the host at the paused prompt. They are host commands, not builtins:
each drives the debugger the paused script is under, which the interpreter alone cannot reach, so
`-batch` does not know them and the builtin count does not move. Clicking a frame in the Call
Stack pane is `dbup`/`dbdown` with the mouse — it chooses the frame the Workspace pane shows, the
frame `K>>` evaluates in, and where the execution marker sits.

### The Data Viewer writes back through a statement

A cell edit is not applied by the grid. The workspace that owns the value composes the statement
that performs it — `IWorkspaceCellEditor` on a session, its twin on the paused debugger — and the
statement runs exactly as a typed one does: at the prompt when idle, in the selected frame at
`K>>` while paused. It therefore waits its turn, can be interrupted, reports an error the same
way, and a failed write reverts the cell because the viewer re-reads the value. A vector writes
`v(i)`, a matrix `m(r, c)`, a cell `c{r, c}`, a struct `s.field`, a table `t.Var(i)`, and what
was typed is an expression, as in MATLAB's own editor. The viewer follows the variable: every
statement, step and edit goes through one road into the Workspace pane (`ShowVariables`), and the
viewer re-reads the value there.

Table writes turned out not to exist. `T.Var` read out a copy, so `T.Var(i) = v` changed nothing
and said nothing. `T.Var = column` now replaces a variable or adds one, and `T.Var(i) = v` reads
the column out, writes it like any array under a scratch name, and puts it back — both rebuild
and rebind, since a `Table` holds its columns by value. The column converter is shared with
`table()`, so the two cannot drift.

### A name's definition is found by reading lines

`FunctionLocator` reads each language's definition shape line by line — `fn`, `function` in
every MATLAB spelling, `def` and `class`, a C# method or type — rather than parsing, which is what
makes it cheap enough to run over a whole workspace on a keystroke and lets it read a file with a
syntax error further down. The search order is MATLAB's: the file the name was used in, the other
open tabs, the workspace (a file named for the function first — MATLAB's rule — then any script
that defines it), and the built-ins, which have no file but can say what they are. The editor's
context menu offers *Open name*; Ctrl+D is MATLAB's key and shadows AvalonEdit's delete-line on
purpose; F12 is the other habit. `edit name` and `open name` at the prompt reach the same code,
and `open x` for a workspace variable is MATLAB's `openvar`. The workspace's list of script
extensions gains `.m`, which it had never included.

### MATLAB is the default, and the console language is session state

The prompt opens in MATLAB and a blank New Script is `.m`, unless the user chose otherwise in
Options. The language picked in the console's dropdown is saved with the window layout,
breakpoints and open files — it is picked in the window, beside the layout, so it belongs with
the layout rather than in the settings — and a restored language this machine cannot run falls
back rather than selecting a console that is not there.

### Datatips

The editor asks a provider the host supplies and shows the answer beside the pointer until it
moves. The host answers from the paused frame while stopped, and from the console workspace of
the document's language when idle, so a name the workspace does not hold shows nothing.

## Consequences

- A debug run is no longer isolated from the prompt. The tests of ADR 0109 that a breakpoint must
  not change what a script does still pass; what changed is that the script and the prompt are
  now the same workspace, which was the behaviour the whole session model existed to provide.
- `JgsDebugSession` gained a second way to run and a way to evaluate; the interpreter gained an
  attachable hook and `RunWhilePaused`. The hooked block executor is unchanged.
- `IWorkspaceCellEditor` is a third session capability beside `IScriptRepl` and
  `IGraphicsEventSession`, feature-detected the same way. C# and Python sessions do not have it
  and show their values read-only.
- The state file carries one more optional field; old state loads with it null, and the format
  version does not move (its own rule for additive fields).
- Engines register MATLAB first, which orders every picker. Nothing else read that order.
- Tests: 9 for the shared workspace and `K>>`, 20 for the debugger words, 8 for table writes,
  6 for cell edits, 21 for the locator, 17 for `edit`/`open`, 2 for the state field. The
  Scripting, Serialization and Startup namespaces are green at 4,691.

## Divergences

- **A breakpoint inside a function called from `K>>` does not pause.** MATLAB opens a nested
  `K>>` there; the debugger here stands aside while a typed statement runs. The statement is
  interruptible, the run is not disturbed, and a nested prompt over a borrowed interpreter is a
  second design.
- **A text table variable reads out as a cell of char, so `T.Var(i) = "s"` takes a string by
  wrapping it, and a string column does not round-trip as a string array.** A `Table` column has
  no memory of whether it was built from strings or a cellstr; `readtable`'s default is the
  cellstr, and that is what every text column is here.
- **`edit foo` for a file that does not exist does not offer to create it.** MATLAB asks; here the
  name is looked up as a function instead, and the status bar says when nothing is found.
- **Datatips also appear outside debugging**, from the console workspace of the file's language.
  MATLAB shows them only while stopped; the idle answer is a convenience with no wrong reading, since
  a name the workspace does not hold shows nothing.
- **`dbstop`, `dbclear`, `dbstatus`, `dbtype` and `keyboard` stay unanswered.** Breakpoints are the
  margin's, `dbtype` prints a file the editor shows, and `keyboard` would pause a run that is not
  under the debugger.

## Still open

- `T.Properties.VariableNames` is not answered — found while testing table writes, not caused by
  them. `T{i, 'Var'}` and `size(T, 2)` are the reads the tests use instead.
- The Workspace pane itself does not edit in place; the Data Viewer does. A scalar is still one
  double-click away from a grid of one cell.
- A datatip over a name shows the variable's display text; MATLAB's shows the size beside the
  class (`1×3 double`). `ScriptVariable` carries no size, so the class stands alone.
- Command history and Tab completion at the prompt, `dbstop if error`, Run to Cursor and
  conditional breakpoints were named as a second batch and are not here.
