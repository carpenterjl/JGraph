# ADR 0071 — The callback seam, wired for real

## Status

Accepted, implemented in M71.

## Context

ADR 0070 deferred the callback half of the common property block with a reason: storing
`ButtonDownFcn` without wiring it would make `set(h, 'ButtonDownFcn', @f)` a silent no-op, the
exact failure mode this project's coverage documents have been corrected six times for. M71 is
that seam built for real — and the decision, taken in planning, to build the **full event loop**
rather than the smaller "queue at statement ends" subset: `drawnow`, `pause`, `waitfor` and
`getframe` are real queue-drain points, a running callback can be interrupted at them, and
MATLAB's `Interruptible`/`BusyAction` words mean what they say.

The architecture facts the design rests on, established by exploration before a line was written:
every statement runs on a fresh 16 MB script thread; the interpreter is fully re-entrant on one
thread (feval and cellfun nest calls routinely) but `BeginStatement` and `BeginRun` must never be
called nested, because they reset the outer statement's cancellation token and figure accounting;
and the one callback that existed — the legend's `ItemHitFcn` — ran the interpreter **on the WPF
UI thread with a 1 MB stack** and dropped the click whenever a statement was running.

## Decision

### One queue, one thread rule

`ScriptEventQueue` sits between the interface and the interpreter. Interface threads only ever put
events in; the script thread takes them out at its own safe points. That single rule replaces the
old busy-flag-and-drop arrangement and is what lets a callback run script code without two threads
ever sharing the interpreter. The legend's `ItemHitFcn` migrated onto the queue: same observable
behaviour, minus the dropped clicks and the UI-thread interpreter.

Delivery has two doors. A running statement reaches a drain point and `JgsCallbackDispatcher`
drains in place — nested re-entry on the same thread, under the outer statement's cancellation
token, never calling `BeginStatement` or `BeginRun`. When nothing is running, the WPF shell's idle
pump starts a drain run with full statement ceremony through the same `TryBeginRun` gate as a
typed statement, so Stop works during a callback and a user statement always wins the race (the
pump yields between events and re-arms when the statement finishes).

### The scheduling words, decided at dispatch time

Whether an event may run at a drain point depends on the *running* callback's `Interruptible`;
what happens to an event that may not run depends on its own object's `BusyAction` — `'queue'`
waits its turn, `'cancel'` is discarded at that moment. Both are read on the script thread when
the question is asked, never snapshotted on the thread that queued the event: the queue-side
thread cannot know what will be running when the event is finally considered, and an enqueue-time
decision would be both a race and a fidelity error (MATLAB discards a `'cancel'` event only when
it *attempts* to run during a non-interruptible callback — one that never reaches a drain point
discards nothing). A plain statement counts as interruptible. Close requests, resizes and
deletions run regardless of `Interruptible`, as MATLAB documents. A callback that dies takes only
itself — the error is reported like a failed statement and the queue survives; cancellation alone
unwinds everything.

### Three latent defects fixed on the way

**`pause` was uninterruptible in the app**: the REPL session passed `CancellationToken.None` into
`CreateGlobals`, and `pause` was the only builtin closing over that token. It now reads the
per-statement token through the dispatcher, wakes on Stop, and drains the queue every 25 ms slice.
**`gcbo` was cleared, not restored**: the callback-state scope set `CallbackObject` to null on
exit, so a nested callback would have erased its interrupter's identity; it now restores what it
displaced. **The script close path evicted the figure before asking the host**: harmless while
nothing could veto a close, wrong the moment `CloseRequestFcn` could — `close` now consults the
callback before anything is torn down.

### DeleteFcn: a deletion coordinator, not a collection hook

MATLAB fires `DeleteFcn` *before* destruction — the object still parented, its children still
reachable — and exactly once, parent first. Hooking `CollectionChanged` can do none of that:
`ObservableCollection` throws on re-entrant mutation while more than one handler is attached (a
`DeleteFcn` that deletes a sibling mid-`clf` would crash the clear), and `Clear()` raises a Reset
that does not say what died. So deletion is announced instead: `GraphObjectLifecycle.NotifyDeleting`
marks the object's new `BeingDeleted` flag — the fired-once guard — and raises an event the
scripting layer answers by running the object's `DeleteFcn` and then walking its descendants,
each marked and fired once, before the removal happens. The container collections announce their
own removals; `JG.CloseFigure` announces the figure (outside the registry lock, because the
callback may re-enter it); the legend/colorbar hide-as-delete paths announce explicitly, so a
plain `set(h, 'Visible', 'off')` — which MATLAB does not treat as deletion — fires nothing.
`Reparent` wraps its removal in a suppression scope: a move is not a deletion. A `DeleteFcn` that
errors is reported and the deletion continues, as MATLAB's does. Deletions caused on other threads
(a window's close box, the plot browser) queue instead of firing in place.

`CreateFcn` has exactly one moment: given as a name-value pair in the creating call, it fires
after the options are applied, with the new object as `gcbo`; `set(h, 'CreateFcn', @f)` afterwards
only stores. The plot family and the menu verbs honour the pair; both route it through the
property table so the option spelling and `set` cannot drift apart.

### ButtonDownFcn: one hit test, announced in every mode

The pixel-to-object resolution that lived privately inside the edit mode is now
`FigureHitTesting.Resolve`, shared verbatim between selection and the click seam — what the user
can select and what a script hears about are the same object by construction. The controller
announces every press, before and regardless of the active mode, because MATLAB fires
`ButtonDownFcn` whatever tool is selected. The scripting layer then applies MATLAB's rules: the
hit object takes the click unless its `PickableParts` is `'none'` (the click falls to the axes),
bare canvas is the figure's click, whatever is decided becomes `gco` even when nothing has a
callback, and the callback receives the documented Hit event — `Source`, `EventName`, `Button`
(1/2/3), `IntersectionPoint` in data coordinates with NaNs where that means nothing.

### The figure callbacks

`close(fig)` runs the figure's `CloseRequestFcn` instead of closing; the callback closes with
`closereq` (new, and `AutoCallsBare` so its natural spelling `@(s,e) closereq` is a call) or
`delete`, vetoes by returning without either, and vetoes by erroring. `close(fig, 'force')` and
`close all force` skip the question; `delete(fig)` never asks. The title bar's close box cancels
the WPF close and queues the request — the interpreter never runs on the window's thread — and a
second click while the first request is still undelivered closes outright, the documented escape
when a wedged script never drains. Stop converts abandoned close requests into closes rather than
discarding them: the person asked for the window to go away. `SizeChangedFcn` rides the queue
coalesced (a drag queues one callback, in the position of the first, reading the settled size),
and the same wiring finally makes `get(fig, 'Position')` truthful after a user resize — the
viewport writes the model on every real size change.

### uicontextmenu and uimenu

Two new model classes — `ContextMenuModel` holding `MenuItemModel` items, which nest — parented to
the figure in a collection of their own, so menus are ordinary objects: typed (`uicontextmenu`,
`uimenu`), findable, deletable, reachable as `Children`, reparentable between figures and menus.
Every object gained a `ContextMenu` property (with the pre-R2020a spelling `UIContextMenu` naming
the same slot) holding a menu handle; a right-click on an object that has one shows that menu and
nothing else, MATLAB's substitution rule, with nested items as submenus and `Checked`, `Enable`
and `Separator` honoured. Picks queue the item's `MenuSelectedFcn` (old spelling `Callback`, same
slot). Menu *structure* serializes into `.graph` documents — a new `ContextMenus` list on the
figure DTO, absent-key-safe, no format bump; callbacks are script-side state and do not. The
menu-bar forms of `uimenu` (no parent, or a figure parent) are refused by name: this build has no
figure menu bar, and half-honouring the form would store a menu nothing can show.

### What batch means now

Headless `-batch` installs no pump, and every drain point knows it: `waitfor` keeps its
return-at-once contract (a batch script that waits on a figure must end with the run, not hang),
`drawnow` still flushes nothing into a window that does not exist, and interface events never
arrive. What *does* fire under `-batch` is everything the script causes itself: `CreateFcn`,
`DeleteFcn`, `CloseRequestFcn` — a one-shot run installs its own dispatcher for exactly this,
which also carries the real cancellation token into `pause`. That split is what makes the seam
stress-testable: `stess_43.m` asserts the script-caused half, and the queue-driven half is
asserted by tests that enqueue synthetic events exactly as the windows do.

### drawnow grew its words, and shows figures mid-statement

`drawnow` now does three things: shows the figures the statement has touched so far — which is
what makes an animation loop animate rather than finish — drains the queue, and blocks on a
render barrier the windowed host installs (an empty dispatcher call at render priority; the idiom
the animation player already used). `'limitrate'` caps only the barrier at MATLAB's ~20 per
second — callbacks are still delivered; `'nocallbacks'` skips only the drain; `'update'` and
`'expose'` are the old spellings of those. The blocking rule is worth stating as the invariant it
is: the script thread may block on the UI thread, the UI thread never blocks on the script thread.

## Consequences

### What moved

| Measure | before M71 | after |
|---|---|---|
| Property slots answered (probe) | 436 of 1,361 across 26 kinds | **736 of 1,394 across 28 kinds** |
| `uicontextmenu` / `uimenu` | absent | 12/12 and 21/21 — complete |
| Forms accepted (probe) | 1,011 of 2,422 | **1,101 of 2,429** |
| Forms unprobed | 917 | 796 |
| Documented builtins | 413 of 514 | 415 of 514 |
| Drain points | none (drawnow a no-op, waitfor returned at once everywhere) | drawnow, pause, waitfor, getframe |
| Callbacks that fire | legend `ItemHitFcn`, UI thread, dropped when busy | nine kinds, queued, script thread |

The form movement, accounted: 77 forms moved `unprobed → accepted` (mostly command-word forms the
prober could not read before), 12 moved `error → accepted` (the axes-sample fix below), 4 arrived
with the two new verbs, and exactly **one moved `accepted → error`: bare `camlookat`**. Its old
verdict was batch-context luck — an earlier form in the same probe batch had drawn into the axes —
and in isolation this build refuses an empty axes where MATLAB tolerates one. The honest verdict is
the error, and the small real gap it names is recorded rather than reconciled away. The 35 forms
that moved `unprobed → error` are command-word forms the build genuinely refuses (`close all
hidden`, `copyfile`'s flags, `grid minor`, …) — leads for later milestones, measured for the first
time.

The ~300-slot jump is honest but bounded in meaning: the property probe measures presence in
`get(h)`, not behaviour. The behaviour — a `ButtonDownFcn` that fires on a click, a `DeleteFcn`
that fires parent-first exactly once — is proven by the M71 test suites and `stess_43.m`, and the
generated coverage document says so rather than implying the table proved it.

### The prober learned command syntax

`drawnow limitrate` and `close all force` were structurally unprobeable: the form prober's parser
only read call syntax, so every command-word form sat in `unprobed` as prose. It now probes
`name word word` as the call MATLAB's command-function duality makes it — `drawnow('limitrate')` —
which moves command-word forms across the whole corpus out of `unprobed`, in both directions,
honestly. `waitfor` also left the prober's skip list: with no pump installed it provably cannot
hang a batch.

Teaching it that exposed a sample-table bug of exactly M70's shape — a plausible mapping measuring
the wrong thing. The dump writes `hold`'s target as *"axes, array of axes"*; the sample table knew
the phrase "axes object" but not the bare word, so "array" matched first and the prober handed
`hold(ax, ___)` a vector where the documentation says a handle. Those forms had read `accepted`
before only because the command forms didn't parse and `___` filled with nothing — the old
verdicts were under-probed, not right. One `("axes", "gca")` row fixed twelve forms at once, and
the first re-run's four apparent regressions were what surfaced it: an accepted count that moves
the wrong way is a question, never a reconciliation.

### An expression-position bare name is not a call, and it bit three times

`f = figure` binds the function, not a figure; an `eval` called for its value evaluates its last
statement as an expression, so a trailing bare `drawnow` or `closereq` silently does nothing.
This is a pre-existing, dialect-wide divergence from MATLAB's zero-argument
call-on-name-reference rule, and M71 hit it three separate times before recognizing the shape.
The verbs whose *natural spelling is the bare word* — `closereq`, `uicontextmenu`, `uimenu` — are
registered `AutoCallsBare`, as `gco` and `gcf` already were. The general rule stands and is now
recorded below rather than rediscovered.

## Recorded divergences

- **A bare function name in expression position is the function, not a call.** MATLAB calls a
  zero-argument function wherever its name is referenced; this build calls it only in statement
  position or when the builtin opts in (`gco`, `gcf`, `gca`, `closereq`, `uicontextmenu`,
  `uimenu`). `f = figure` binds the function; write `f = gcf` after a bare `figure`, or
  parenthesize.
- **Callbacks do not serialize.** A `.graph` document saves a context menu's structure but not
  `MenuSelectedFcn` or any other callback; MATLAB's `.fig` saves them. Callbacks live on the
  script-side handle entry and die with the session.
- **`ContextMenuOpeningFcn` runs after the menu shows, not before.** The opening callback rides
  the queue like every other event, because running it first would mean the window's thread
  waiting on the interpreter; a callback that adjusts entries is in time for the next open.
- **`uimenu` cannot reach a menu bar.** The no-parent and figure-parent forms are refused by name:
  figures here have no menu bar. A `uimenu` parents to a `uicontextmenu` or another `uimenu`.
- **`PickableParts 'all'` behaves as `'visible'`.** The shared hit test never sees invisible
  objects, so the one word that would make them clickable cannot be honoured; `'visible'` and
  `'none'` are exact.
- **`figure` creation is not an interruption point.** MATLAB drains callbacks when a figure is
  created; this build drains at `drawnow`, `pause`, `waitfor` and `getframe` only.
- **`camlookat` refuses an empty axes.** MATLAB aims the camera at nothing without complaint; this
  build answers "there is nothing in the axes to look at". Found when probe isolation stopped an
  earlier batch-mate from drawing into the axes first.

**Three bullets have been deleted from the list above rather than struck through**, because the
harvest lifts a struck-through bullet whole and a retired divergence must leave the index:

- *"The window-level callbacks are absent"* — **false since M75**, which built all six named there
  plus `WindowKeyPressFcn` and `WindowKeyReleaseFcn`, and tested every one of them in
  `WindowEventCallbackTests.cs`. The sentence stood for twelve milestones, and the capability
  report's gaps page quoted it as current for all of them. That is the **fifth** expired block this
  arc has found by re-reading one, after `warp` in M67, `MException` in M68, `Interactions` in M80
  and `sqrt`/`log` in M81 — the pattern is not a coincidence, and a recorded limitation with no test
  on either side of it is the shape to look for.
- *"3D plots are unpickable"* — retired by M87, which is the half of that sentence that was still
  true.
- *"Bare `pause` is unsupported"* — its stated reason, *this build has no key routing to the
  interpreter*, stopped being true in M75 as well; M87 wrote the verb the reason had been blocking.

## What is not done
- The ~55 numeric and file leftovers from ADR 0070 (`fft(X,n,dim)`, `eig(A,B)`, `lu` output forms,
  `textscan`, …), unchanged and still a clean standalone wave.
- The IPT/statistics form pass: 2,940 documented forms across 869 callables, of which 2,422 were
  probed — the graphics families are now substantially closed, and the remaining `error` and
  `unprobed` mass is numeric.
- `uicontextmenu` on figures shows only when the click lands on bare canvas of a figure that was
  given one; the built-in menu (zoom constraints, data tips) is otherwise kept. MATLAB has no
  built-in plot context menu, so the substitution rule only ever fires where a script asked for it.
- Queued events resolve their callback at dispatch time. An object deleted-and-reaped between a
  UI-thread deletion notice and its delivery quietly drops its `DeleteFcn` — visible only through
  a close-then-`close all` race, recorded here rather than papered over with a snapshot.
