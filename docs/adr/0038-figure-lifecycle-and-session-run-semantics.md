# ADR 0038 — Figure lifecycle and per-run session semantics

## Status

Accepted (M35). Supersedes the `close all` deferral in ADR 0035.

## Context

A MATLAB function file that plots into `figure(1)` and `figure(2)` showed both figures the first
time it was run with F5 and *nothing* the second time — the console reported `ans = 1`, `ans = 2`,
`0 figure(s) displayed`. Four separate defects, all of them consequences of a console session
outliving the run:

1. `JGraphScriptGlobals` remembered which figure numbers it had already displayed **for the life of
   the session**, so a re-run's figures were "already shown" and `ScriptContext.ShowFigure` was
   never called. The figure count was the same bug seen from the other end: a delta against a total
   that had stopped growing.
2. Closing a figure window told the host's window map and nobody else. The engine kept handing
   scripts a `FigureModel` that no window was bound to, so a re-run plotted into an orphan.
3. `figure(1)` as a statement bound and echoed `ans` (MATLAB prints nothing), and `ans` was declared
   into the *base* workspace even from inside a function body.
4. `hold off` and `grid off` turned those features **on**: command syntax passes the bare word as
   the string `"off"`, and every non-empty string is truthy.

Two more fidelity gaps surfaced alongside: plotting into existing axes kept the previous plot's
title, labels, and frozen limits, and F5 resolved a script's relative paths against the workspace
root rather than the script's own folder.

## Decision

### Figure display is per run, driven by touch stamps

`JG` keeps a monotonic counter and stamps a figure whenever it is selected, created, or drawn into
(`Figure`, `RegisterFigure`, `Gca`, `Subplot`, `Clf`, `Bode` — every drawing verb funnels through
`Gca`). `JGraphScriptGlobals.BeginRun()` records the counter and clears the per-run "already shown"
set; `ShowTouchedFigures()` then displays exactly the figures the run touched. Every session — JGS,
MATLAB, C#, Python — calls `BeginRun` at the top of each statement or file run.

Consequences: a re-run redisplays its figures (recreating windows the user closed, refreshing ones
still open); "N figure(s) displayed" counts *this run's* figures, so a figure opened from the Data
Viewer or a `.graph` file no longer inflates a later script's count; and a prompt statement that
touches a figure refreshes that window, which is closer to MATLAB's live update than the previous
silent skip. `JgsReplSession`'s delta bookkeeping is gone — the count is per run by construction.

### Closing a window retires the figure

`JG.CloseFigure(number)` drops the figure from the registry and promotes the most recently touched
survivor to current (so `gcf` after a `close` still answers sensibly; with none left, the next
figure verb starts again at 1). Two things call it:

- `FigureWindowService`'s `Closed` handler — the user clicking the X now retires the figure, so the
  next `figure(n)` builds a fresh one rather than drawing into an invisible model.
- The `close` builtin, via `ScriptContext.CloseFigure` — an optional host callback (null for batch
  runs and tests) that closes the real window.

Because the UI thread now writes to the registry while a script may be reading it, `JG`'s registry
operations take a narrow lock. Everything else in `JG` remains single-threaded by design.

`ShowScriptFigure` restores a minimized window but deliberately does **not** call `Activate()`:
MATLAB's `figure(n)` steals focus, and in an IDE-shaped app that would yank the caret out of the
editor mid-run.

### `close`, `clf`, `gcf`, `gca`

Registered in both dialects. `close` with no argument closes the current figure, `close(n)` a named
one (erroring when it does not exist, as MATLAB does), `close all` every one. `clf` empties a figure
but keeps its window. `gcf` returns the current number. `gca` has no value to return — JGS has no
axes-handle type — but still creates the figure and axes MATLAB would, so `gca; xlabel('t')` works.

### `ans` follows MATLAB's rules

`BuiltinFunction.BindsAnsAsStatement` (default true, false only for `figure`) lets a bare call
statement skip `ans` while `h = figure(1)` still yields the handle — the smallest mechanism that
works without nargout plumbing. The interpreter checks the *resolved callable*, not the name, so a
shadowed name behaves correctly. `BindAns` now declares into the statement's own scope: at the
prompt that is still the base workspace, but inside a function body it is the call frame, which
dies with the call — so running a function file leaves the base workspace clean.

### `hold` belongs to the axes; switches read the word

`AxesModel.Hold` replaces the process-wide static. Hold now ends when its axes do — a new figure or
`clf` starts unheld, exactly as in MATLAB. It is transient: not serialized to `.graph`, not shown in
the inspector. A shared `OnOff` helper reads `'on'`/`'off'` (case-insensitive), booleans, and
numbers, and errors on anything else, for `hold`, `grid`, and `colorbar`. A bare `hold`/`grid`
toggles in the MATLAB dialect (real MATLAB behaviour) and stays "on" in JGS, where examples and the
guide have always relied on that.

### Plotting replaces the axes state, not just its plots

`ResetAxesForReplace` implements MATLAB's `NextPlot = 'replace'`: it clears plots, annotations, the
title, axis labels, secondary axes, grid, legend/colorbar visibility, equal-aspect, and the 3D view,
and returns autoscale and linear scaling. It keeps the subplot cell (`NormalizedBounds`), the
background, the title's font, and hold — those describe the axes, not the plot that was in it.

### A file run resolves paths beside its file

`ExecuteFileAsync` already receives the document's path as its source id, so `BeginRun` takes the
script's folder and `Resolve`/`ResolveForWrite` try it first. Prompt input passes none and keeps
resolving against the workspace root. This restores the behaviour F5 had before runs moved into the
session, without changing the session seam.

## Consequences

- The reported repro works: F5 twice shows two figures both times, with no `ans` noise; closing the
  windows and pressing F5 brings them back.
- One existing test changed meaning on purpose: a prompt statement touching a figure now reports one
  figure displayed rather than zero (`JgsReplSessionTests.AStatementThatTouchesAFigureRefreshesTheSameWindow`).
- Four new suites pin the edge cases this class of bug lives in: `MatlabSessionIdempotenceTests`
  (run twice), `MatlabFigureLifecycleTests` (close/clf/window close), `MatlabConsoleEchoTests`
  (what prints and what `ans` holds), `MatlabCommandSyntaxTests` (`hold off` really is off).
- Still missing, and now visible: builtins cannot return multiple outputs, so `[X, Y] =
  meshgrid(x, y)` fails in the MATLAB dialect, and `rand`/`randn` have no `rng(seed)`. Both are
  separate work. *(Superseded: ADR 0039 gives builtins a `MultiOutput` seam — meshgrid, size,
  max/min, sort, find, and ind2sub produce real multiple outputs now. `rng(seed)` is still open.)*
