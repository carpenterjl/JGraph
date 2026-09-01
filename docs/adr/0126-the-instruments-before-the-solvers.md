# ADR 0126 — The instruments, before any solver is written

## Status

Accepted (M124, 2026-09-01).

## Context

The 2026-09-01 gap report ranked two numeric gaps a first-week MATLAB user meets early: the ODE
solvers (`ode45` alone, of twelve, with `odeset` reading five of its twenty-three fields) and the
Signal Processing Toolbox (six names of 351). Beside them sit the rest of `funfun` — `integral2`,
the BVP, DDE and PDE solvers — `sparfun`'s eleven Krylov solvers, and `polyfun`'s scattered-data
interpolants. The plan that closes them is fourteen milestones long (M124–M137), and every one of
them makes the same claim: *this answers what MATLAB answers*.

Every earlier milestone made that claim too, and made it the same way — a diff run once, by hand,
against R2024a, and then a test that pinned the number the diff had shown. That is a recording of
one afternoon. It does not notice when a later change moves the number; it does not notice when a
divergence written into an ADR quietly stops being one; and it cannot be run by anyone who does not
have the afternoon again. M118 found nine wrong rules in a smoother that every previous check had
passed, because the check was *the fast road answers what the walk answered* — and the walk was
wrong. M123 found that `smoothdata`'s window was ten thousand where MATLAB's was four, invisible
in the sum the check compared.

So the first milestone of the arc builds the measuring instruments and writes nothing else.

## Decision

### A parity fixture is a permanent test, not a diff

`tests/JGraph.Tests/MatlabParity/fixtures/<mNNN>_<topic>.m` is a MATLAB-dialect script that
prints lines of one grammar:

```
CHK|<name>|<value>|<rule>
```

`tools/parity/record-matlab.ps1` runs the script through `matlab.exe -batch`, keeps only those
lines, and writes them beside it under `expected/`. That recording is committed, and it is the
only step in the suite that ever touches MATLAB. `MatlabParityFixtureTests` runs every fixture in
the ordinary test run, in the MATLAB dialect, and compares its lines against the recording **by
the rule each line carries**: `exact`, `shape`, `rel=`, `abs=`, or `div=ADRnnnn`.

The last rule is the one that changes what a divergence is. A `div=` line passes only when the two
engines **differ**. A divergence recorded in an ADR is now also a test that fails the day the
divergence disappears — which is what happened to the `sqrt`/`log` divergence for forty
milestones before M84 noticed, and to `interp1`'s `'cubic'` between M39 and M101.

Two more rules of the grammar follow from what the suite is for. A fixture with no recording
**fails**, so that a fixture nobody recorded cannot pass vacuously. A line printed here but absent
from the recording fails too: the fixture and its recording are the same script, and a script
edited without being re-recorded is a recording of something else.

The comparator lives in two hosts on purpose — `MatlabParityComparer` in C# for the test run,
`tools/parity/compare.py` for a diff of two logs by hand — and the two carry the same rules,
tested against the same five cases (an agreeing set, a wrong value, a retired divergence, missing
and unrecorded lines, a rule that changed).

A fixture pins what the algorithm *does* as well as what it answers. `ode45`'s fixture holds
`nsteps`, `nfailed` and `nfevals` **exact** on a decay and the row counts **exact** at every
`Refine`, `MaxStep` and tolerance it accepts — and all twenty-eight lines agree with R2024a. That
is a stronger statement than any endpoint: an error norm, a step-growth cap or a first-step
heuristic that differs from MATLAB's by one constant shows as a count off by one on every
problem, and no tolerance hides it.

### The Signal population is harvested, not transcribed

The IPT list was typed by hand because the machine the base dump came from had no IPT. This
machine has R2024a with Signal installed and every public name is a readable `.m`, so
`tools/matlab-checklist/build-signal-csv.py` walks `toolbox/signal/signal` and writes
`matlab-r2024a-signal.csv`: the name, the toc page that lists it, its kind, and every call form
its help block documents (1,982 forms over 351 names). The counting rule is the gap report's own,
so the two agree on the total.

`docs/matlab-signal-coverage.md` sorts the 351 into buckets — six implemented, 272 assigned to
M132–M137 by name, 73 excluded by name with the reason — and `verify-signal-coverage.py` refuses a
name in no bucket, in two, called implemented without a catalog entry, **or registered without
being called implemented**. That last check is new here: it is the trap the toolbox verifier
already springs (register first, then move the doc line), written down so it springs on Signal too.

### The head-to-head suite grows two scripts and two probe groups

`d15_signal.m` and `d16_solvers.m` time what exists today — `filter` over ten million, `firpm`
at order 400, `dct` over four million, `ode45` on Lorenz to t=200 and on a tight-tolerance orbit,
two thousand `integral`s, `trapz` over ten million, `fzero` and `fminsearch` loops — every row
sized so MATLAB takes at least a tenth of a second on it, because the five-run noise floor is
1.07× at the median and 1.26× at the worst and a row below it measures nothing. Their `CHK` lines
are identical on both engines. `d14_capability.m` gains a Solvers group and a Signal group of
eight forms each, five of the sixteen accepted today and eleven the coming milestones will turn on.

### What the instruments found on their first day

The fixtures were written for the names that already exist, as a self-test. Three of the
twenty-six Signal lines did not agree, and each is a divergence recorded below rather than a
tolerance loosened:

- **`butter`'s two-output form is refused**, and its single output answers `[b; a]` as a
  two-row matrix where MATLAB's single output is `b` alone.
- **`freqz`'s single output answers `[h; w]`** as two rows (sixteen numbers for eight points)
  where MATLAB answers `h` alone.
- **`firpm`'s coefficients are 1e-5 from MATLAB's** on a twenty-tap lowpass, and at order 400
  the exchange warns that it has not converged. The shape and the symmetry agree; the design does
  not.

All three belong to M134, which rewrites the design layer, and each fixture line will fail the
day M134 lands — which is the point.

### Reference sources

The reference for a rule is MATLAB's own readable `.m` on this machine, read for the documented
behaviour and the constants that define it. Nothing is copied. Files read for this milestone:
`toolbox/signal/signal/Contents.m`, the fifteen `toc*.m` pages, and the help blocks of every
public Signal `.m` (by the harvester, for their syntax lines only).

## Consequences

- The gate for every milestone from M125 on is mechanical: its fixture has **0 unexplained
  lines**, every explained line is a `div=` line and an ADR entry, and the recording is committed.
- `record-matlab.ps1` is the only place MATLAB is run, and it is never a timing.
- Two Windows PowerShell 5.1 traps, found and written into the recorder: `Start-Process -Wait`
  never returns for MATLAB because it waits for every descendant and `MathWorksServiceHost` stays
  alive — wait on the launcher's own handle instead; and a `-PassThru` process reports a null
  `ExitCode` unless its `Handle` was read before the wait.
- A fixture must run on **both** engines, so a form one engine refuses is wrapped in `try`/`catch`
  and the branch taken is the recorded value. That is how `butter`'s divergence is pinned without
  the fixture itself failing on either side.
- `stess_71.m` carries a handful of the recorded numbers so the Release exe is checked against
  them as well, and holds the recorded divergences to their divergence.
- Nothing numeric changed in this milestone. Zero of the 188 head-to-head checksums moved, and
  no name was added to the catalog.
- The lanes are not all green, and were not before this milestone: `native/packed` passes all
  7,179 tests, but the `managed` lanes fail seven (three pencil-sign tests, three `polyeig`
  tests, `residue`'s shape) and the `boxed` lanes fail `ARunningTotalSaturatesAtEveryStep` —
  every one reproduced on the tree with this milestone's files stashed. They are named here so
  M125 clears them before its gate reads "four lanes green" as a fact.

## Divergences

Three are added, all in Signal, all assigned to M134.

- **`[b, a] = butter(n, Wn)` is refused, and `butter(n, Wn)` answers `[b; a]` as a 2-by-(n+1)
  matrix where MATLAB answers `b` alone.** Pinned by `m124_signal`'s `butter_two_outputs` line.
- **`h = freqz(b, a, n)` answers `[h; w]` as a 2-by-n matrix where MATLAB answers `h` alone**
  (and `[h, w] = freqz(...)` is refused). Pinned by `freqz_single_numel`. Both this answer and
  `butter`'s report `size` 2-by-n but **`numel` 2**: the value is a list of two rows wearing a
  matrix's size, not a matrix, and `numel` counts the rows. M134 replaces both with real
  multi-output forms, which retires the shape question with the divergence.
- **`firpm`'s equiripple exchange does not converge to MATLAB's design**: the centre tap of
  `firpm(20, [0 0.3 0.5 1], [1 1 0 0])` is 0.400135 here and 0.400143 in R2024a, and at order 400
  the exchange stops early with a warning. Pinned by `firpm_centre` and `firpm_dc`.

## Still open

The two new scripts were run once, interleaved, for their `CHK` parity; the timings below are that
single run and become claims only under the five-run protocol, when a milestone changes them. Four
rows are worth writing down now so they cannot be forgotten:

| Row | JGraph | MATLAB | Owner |
|---|---:|---:|---|
| `d15_dct_idct_4M` | 3.81 s | 0.25 s | M132 — the transform is not on the batch FFT |
| `d15_filter_iir_10M` | 0.119 s | 0.065 s | M133 — a sequential recurrence; one pass over memory |
| `d16_ode45_orbit_tight` (RelTol 1e-9, same step count) | 0.155 s | 0.030 s | M125 — the RHS through the interpreter, five times per step |
| `d16_ode45_lorenz200` | 0.175 s | 0.143 s | M125 |

Elsewhere JGraph is ahead: `integral` ×2000 (0.17 s vs 0.26 s), `quadgk` on an infinite range
×500 (0.022 s vs 0.091 s), `fminsearch` ×200 (0.056 s vs 0.097 s), `firpm(400)` (0.036 s vs
0.132 s — but see the divergence above: faster to a different answer).

- `ode45` on Lorenz to t = 200 takes 11,565 steps here and 11,593 in R2024a, where at t = 20 and
  t = 60 the counts agree exactly. Chaos amplifies the last bit of a different summation order
  into a different step sequence; the row pins the scale of the run rather than the count. M125's
  fixtures use t = 20.
