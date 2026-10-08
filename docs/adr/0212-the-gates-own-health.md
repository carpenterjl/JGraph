# ADR 0212 — The gate's own health: open items 1, 6, 12, 37, 38 and 39

## Status

Accepted, 2026-10-07. The first batch of the open-items work that followed the app-building plan
(`docs/plans/open_task_chips_09_12_2026.md`, gitignored; its triage section dates each item). It
takes the items that kept the gate itself from being trusted: three tests that failed on every
run, three that failed under load, a verifier that had drifted, and an audit that had not been
green since Z2 (ADR 0173). It builds on ADR 0149 (the file index and the dispatch table), ADR 0164
(the script-running list), ADR 0163 (adoption at a binding), ADR 0193 (audio devices) and ADR 0184
(serial callbacks).

## Context

Every gate since U2 recorded the same five or six failures as "known": the three audio tests of
item 39, the file index's external-change test (item 1) and `serial_callbacks` under load
(item 37), with the trace-sink test (item 6) now and then. A gate that always has known failures
stops being read. `verify-method-classes.py` reported nine classes and, by U12, 152 catalog names
with no row (item 12), and `audit-ownership.py` stopped at its first check (item 38).

## Decision

### `sound`'s tests use the audio simulator (item 39)

Since ADR 0193, `sound` in a JGS or MATLAB script is `sound.m` transcribed: an `audioplayer` the
session keeps, clipped, a column per channel, padded to whole 25 ms buffers. The host's
`IScriptAudio` sink still serves a C# script's `sound()`. The three tests expected the old road
and, worse, played through the machine's speakers on every run. They now turn
`jgraph.internal.audiosim` on, and read a new `audiosim('log')` (frames, channels and rate of
every block played, from `SimulatedAudio.Log`). The test that asserted "not supported by this host"
became one asserting that a script's `sound` does not reach the host sink.

### A folder time too recent to trust is listed again (item 1)

The file index re-lists a search folder at a top-level statement only when the folder's write time
has moved. File times move in clock ticks (about 15.6 ms on NTFS, 2 s on FAT), so a file another
program drops in the tick the folder was last read in leaves the time where it was, and the file
was not seen until something else changed the folder: a real miss, which the test showed as one
failure in three under load. A folder read while its time was within two seconds of the clock is
now marked racy and listed again at the next statement, as git treats a racily clean index entry.
A test freezes the folder time and the clock to make the race certain; it fails without the rule.

### The trace sink keeps only the test's own lines (item 6)

`NativeThreads.Trace` is static and test classes run in parallel. The test now keeps only the lines
whose routine field is its own name, so another class's `Geev` cannot land in it; it still asserts
exactly one line.

### The test peer sends its chunks in order (item 37)

`term_crlf_calls` counted one terminator callback where R2025b counts two, now and then under load.
A fixed pause was the item's guess; the cause was the test peer. `PeerEngine`'s `chunks` command
put each chunk on a threadpool timer of its own, and timers due close together fire in no set
order, so on a loaded machine the stream arrived shuffled (`p CR LF LF CR CR r LF q` for
`p CR LF q CR r LF CR LF`). Both engines counted the shuffled stream faithfully: a probe of six
groupings gave 0, 1 or 2 in JGraph and 1 or 2 in R2025b, differently from run to run. Each timer
now sends the next chunk in line, and both engines count 2 under every grouping
(`ThePeerSendsChunksInOrder` fails on the old peer). The engine is shared by
`jgraph.internal.devicesim` and the `peer-sim` R2025b talks to, so both were rebuilt.

The three rows whose peer paces its bytes (`byte_trickle_*`, `term_trickle_calls`,
`term_crlf_calls`) also wait for their count with a ten-second deadline, then 0.2 s for any extra
call, instead of one fixed pause. Re-recorded in R2025b: every count is unchanged; only the line
numbers inside `cb_error_text`'s recorded R2025b stack moved.

### The dispatch table covers every catalog name again (item 12)

Sweep 10 shadowed the 152 names added since sweep 9 under the 37 argument patterns. R2025b
crashed inside `vrjoystick`'s built-in (an access violation in a MEX file) on its third pattern, so
sweep 11 took the five names after it and the `which -all` classification of all 152, and sweep 12
measured `vrjoystick` one pattern per MATLAB process (`run-probe12.ps1`), writing `builtin-err` for
a pattern whose process died, since the built-in was reached. Eleven of the 152 keep the built-in
under some pattern: `tabular`'s `addvars`, `isprop`, `rowfun` and `varfun` with a table; a handle's
`addlistener`, `listener`, `notify` and `isvalid` with a `containers.Map`; and the class constructors
`timer`, `onCleanup` and `vrjoystick` under every pattern. The table is embedded and read at run
time, so a user file named like one of these now resolves as R2025b resolves it. The class map
gains rows for `timetable` (the table column) and the eight event, listener, `matfile`, metadata
and `timer` classes, each `struct` and marked never swept, as the map's other unswept classes are.

### The ownership audit is green again (item 38)

- **Minting.** `values` could hand back a .NET value's own wrapper (`JgsValue.Share` of an external
  is the value itself); it now wraps the same object anew, so every return is minted.
  `fieldnames` was restructured so the scan sees the one cell it mints, and
  `NetBuiltinConversions.Cell` and `DictionaryValues` carry `// audit: mints`.
- **The audit's blind spots.** A registration whose body is a method group
  (`DefineSilent("waitfor", WaitFor)`) is read as a call of that member; a table of makers
  registered in a loop is read through `// audit: registered through MakeComponent`; and a
  statement led by a keyword is never an expression-bodied declaration: `return Foo(a, () => b)`
  had been filed as a local function `Foo`, so every call of the real `Foo` in that file led
  nowhere. That misreading hid the table makers' `CreateFcn` road and gave `legend` a road it does
  not have.
- **The list.** Added, each reaching script during the call: `get` and `set` (get and set methods,
  `PostSet` listeners), the plot makers that fire a `CreateFcn` named among their options (`plot`,
  `stairs`, `polar`, `polarplot`, `subplot`), the function plotters (`fplot` and the `ez*`/`f*`
  family), `groupfilter`, `grouptransform`, `nlinfit`, `nlpredci`, `kstest`, `midiid` (it pumps the
  callback queue), `loadlibrary` (a prototype function) and `executeCallback`. Removed, each
  running none during the call: `legend`, `uifigure`, `uiaxes`, `dialog` (none fires a
  `CreateFcn`) and `uiprogressdlg` (a replaced dialog's `CloseFcn` runs later, from the queue).
  Asserted script-free with their reasons: 28 builtins the graph joins to a script road only by a
  shared member name (`Read`, `Write`, `Build`, `MemberOf`, `Apply`, `Copy`). Asserted to run
  script by a road the graph cannot follow: `openfig` and `hgload` (`JgsFigFile.Build`), through a
  new `// audit: runs script:` form the audit checks both ways.
- **The record.** 189 sites written since Z2 are classified, each reviewed by category: the 20
  in-place writes are gated (`WritableBuffer`, `WritableFields`) or write a copy minted in the same
  method; the stores write private buffers or entries that hold their own wrapper. One store did
  not: a write to an app figure's added property (`RunningAppInstance`) kept the script's wrapper;
  it now stores a share.

## Measured

- File index class: 15 of 15, alone and in the managed/unpacked lane; the racy test fails with the
  rule switched off.
- `serial_*` fixtures: 8 of 8. Re-recording `serial_callbacks` in R2025b changed one line's text.
- The CR/LF grouping probe (`send`, and `chunks 10 N` for N = 1–5): before the peer fix, JGraph
  0–2 and R2025b 1–2 depending on the run; after it, 2 in both for every grouping, bytes in order.
- Full Release suite before the peer fix: 10,831 of 10,835; the four failures (`serial_callbacks`,
  `net_tcpclient` losing its echo peer, the MIDI scheduler's timing, `RoundAfterRound`) pass alone
  with other projects loading the machine.
- Sweeps 10–12: 2,312 names, all measured; `verify-method-classes.py` clean.
- `audit-ownership.py`: minting OK (19), script-running OK (156 listed, 42 asserted), 647 sites.

## Divergences

None new. The racy rule closes a miss: a file dropped by another program in the same clock tick as
the folder's last change is now seen at the next statement, which is what ADR 0149 records for
every external change.

## Consequences

The gate's known failures are gone; a failure now means something. `uifigure`, `uiaxes` and
`dialog` do not run a `CreateFcn` given at creation, which R2025b does for a figure; that is a
parity gap, filed as open item 80.

## Still open

- Item 80: `CreateFcn` for `uifigure`, `uiaxes` and `dialog` (probe R2025b first).
