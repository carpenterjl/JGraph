# User note — continuing the solver / Signal arc on a new machine

This file is for you, not the agent. It holds a prompt you can paste into a fresh Claude Code
session in `E:\EE Projects\JGraph` (or wherever the repo lives on the new machine) so the agent
reads the notes M124 left behind before touching anything.

## Before you paste it

1. The new machine needs the repo and the demo workspace **side by side**:
   `<root>\JGraph` and `<root>\JGraph_demo_workspace` (the stress runner and head-to-head suite
   find each other by that relative layout).
2. MATLAB R2024a is assumed at `E:\Matlab\bin\matlab.exe`. If it lives elsewhere, tell the agent
   in the prompt (the third bullet below); every script that runs MATLAB takes a path override.
3. Build once so the Release exe exists: `dotnet build JGraph.sln -c Release`.
4. Nothing from M124 has been pushed. If the new machine cloned from `origin`, it does NOT have
   commits `3b33ff8`, `91bc7b7`, `f2cb0e8` — copy the working tree instead, or push from the old
   machine first.

## The prompt

Copy everything between the lines.

---

I am continuing a fourteen-milestone plan (M124–M137) that adds MATLAB's solvers and the Signal
Processing Toolbox to JGraph. M124 is done and committed; you are starting M125. Before doing
anything else, get familiar with the workspace by reading, in this order:

1. `docs/plans/solvers-and-signal-plan.md` — the living plan. Read the **Progress** table, the
   **Pick-up notes** (paths, instruments, the per-milestone gate, traps found, and "What M125
   should do first"), then the M125 section of the plan itself.
2. `docs/adr/0126-the-instruments-before-the-solvers.md` — what M124 built, the three
   divergences it recorded, and the pre-existing lane failures M125 must clear first.
3. `tools/parity/README.md` — how a parity fixture is written, recorded and run.
4. `docs/adr/0125-the-class-a-verb-answers-in.md` and `docs/adr/0123-text-containers-quadrature-and-a-blank-panel.md`
   — the two milestones before this arc, for the house style of an ADR and of a divergence entry.
5. `tests/JGraph.Tests/MatlabParity/fixtures/m124_ode45.m` and `src/JGraph.Numerics/OdeSolvers.cs`
   — the solver M125 generalises and the fixture that must not move.

Environment on this machine: the repo is at <REPO PATH>, the demo workspace is beside it at
<DEMO WORKSPACE PATH>, MATLAB R2024a is at <MATLAB PATH> (say "same as the plan" if it is
`E:\Matlab\bin\matlab.exe`).

House rules, unchanged: commit each milestone straight to `main` (never branch), never push, never
run the MSI, warn me before any harness that opens a window, do not read or write files while a
test run is in progress, use CodeAnalyzer (`reindex` first) for structural questions and grep for
text, and do not spawn agents or workflows unless I ask.

Then: (a) run `codeanalyzer reindex`; (b) confirm the instruments work here — `dotnet test
tests/JGraph.Tests --filter "FullyQualifiedName~MatlabParity"` must pass 10 tests, and
`python tools/matlab-checklist/verify-signal-coverage.py` must exit 0; (c) reproduce the eight
pre-existing lane failures ADR 0126 names and clear them, since M125's gate needs four green
lanes; (d) begin M125 exactly as the plan states, starting with the reference reads and the
fixture, and update the Progress table in `docs/plans/solvers-and-signal-plan.md` when M125 lands.
Report what you found in each of (b) and (c) before starting (d).

---

## If something in the prompt does not fit

- **No MATLAB on the new machine.** The parity suite still runs (the recordings are committed);
  only *new* fixtures cannot be recorded. Tell the agent to write M125's fixture and leave the
  recording step for a machine with MATLAB, and to mark the milestone "fixture unrecorded" in
  the Progress table rather than skipping the fixture.
- **Different drive letters.** `run-stress.ps1` and `run-lanes.ps1` take the repo from their own
  location; `head2head_v2\run_instrumented.ps1` has `$jgraphExe` and `$matlabExe` at the top and
  needs editing by hand.
