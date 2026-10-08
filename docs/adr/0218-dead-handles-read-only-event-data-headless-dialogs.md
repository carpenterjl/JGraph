# ADR 0218 — Dead handles, read-only event data and the headless print dialogs: open items 69, 83 and 84

## Status

Accepted, 2026-10-08. The seventh batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored). Items 83 and 84 were filed by batch 6 (ADR 0217). Everything was recorded from R2025b
headless (probe `probe_b8` in the open-items scratch, then the fixture `oi_handle_odds`; item 69's
lines were recorded in `u9b_bridge` by U9b).

## Decision

### Event data are read-only (item 69)

In the MATLAB dialect, a write to a property of a component's or a figure's event data is
R2025b's `MATLAB:class:SetProhibited`, "Unable to set the 'EventName' property of class
''DataChangedData'' because it is read-only.". This covers every built-in class
`JgsUiEventData.IsBuiltinEventData` names. A name the event data does not have is
`MATLAB:noPublicFieldForClass`.

The data stay classed structs underneath (ADR 0202), so the refusal is made where a dotted write
lands (`AssignToMember`). `isstruct` was already false. `u9b_bridge`'s three `evt_*_readonly` lines
are exact, and ADR 0208's divergence is retired.

### A handle that names nothing (item 83)

A handle is a number here (ADR 0051), so `delete` of a deleted figure's handle took the file form
and said it wanted a string. The registry now remembers the handles it has handed out
(`JgsHandleRegistry.WasHandle`): a minted handle is a half above an integer from 1,000,000.5 on,
and a figure's is its number. In the MATLAB dialect, `delete` treats a value by what it names:

- **A handle that named an object and names nothing now** is passed over quietly, as R2025b deletes
  a deleted handle.
- **A number that never was a handle** is `MATLAB:hg:udd_interface:CannotDelete`, "Invalid or deleted
  object.".

`close` of either is `MATLAB:close:InvalidFigureHandle`, "Invalid figure handle.".

### The headless print dialogs (item 84)

With no window, `printdlg`, `printpreview` and `exportsetupdlg` refuse in the MATLAB dialect with
R2025b's `MATLAB:hg:NonInteractiveFunctionSupport` and its sentence, which the blocking dialogs
already used (ADR 0201). JGS keeps the sentence naming the verb that needs no window.

`pagesetupdlg` is removed in R2025b (`MATLAB:pagesetupdlg:FunctionRemovedWeb`). JGraph has a page
setup window behind it, so whether to remove it is the user's decision, and item 84 stays open
for that half.

## Measured

- `oi_handle_odds`: 11 lines, 10 exact and 1 stamped divergence (below).
- `u9b_bridge`: 3 lines retired to exact.
- `MatlabM84DialogTests` and `MatlabClassdefTests` assert the new sentences.

## Divergences

- **The number of a closed figure is a deleted handle.** `close(g); delete(7)` for figure 7 is quiet
  here. R2025b refuses the bare number 7 because its figure handle is an object and not the number
  (`oi_handle_odds`, `delete_closed_number`, `div=0218`). A script deleting through the handle it
  holds behaves as in R2025b.
- **`pagesetupdlg` still exists** (item 84, pending the user's decision).

## Consequences

Clean-up code that deletes handles it may already have deleted (`delete(h)` in a `CloseRequestFcn`
after `close all`) runs as it does in R2025b. A callback that writes to its event data meets
R2025b's refusal.
