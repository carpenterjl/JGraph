# The MATLAB parity fixture suite

A permanent, MATLAB-free test that JGraph answers what MATLAB answers (R2025b since M125; R2024a before), line by line, by the rule
each line asks for. Introduced by M124 (ADR 0126) as the gate for the solver and Signal milestones.

## The pieces

| Path | What it is |
|---|---|
| `tests/JGraph.Tests/MatlabParity/fixtures/<mNNN>_<topic>.m` | a MATLAB-dialect script that prints `CHK\|name\|value\|rule` lines |
| `tests/JGraph.Tests/MatlabParity/expected/<same>.txt` | what MATLAB printed, recorded once, committed |
| `tests/JGraph.Tests/MatlabParity/expected/matlab_version.txt` | which MATLAB the recordings are of |
| `tools/parity/record-matlab.ps1` | runs a fixture through `matlab.exe -batch`, keeps the `CHK` lines, writes `expected/` |
| `tools/parity/compare.py` | the comparison rules, for an ad-hoc diff of two logs |
| `tests/JGraph.Tests/MatlabParity/MatlabParityFixtureTests.cs` | the xunit theory: one case per fixture, the same rules in C# |

## The line grammar

```
CHK|<name>|<value>|<rule>
```

Doubles are printed with `%.17g`, so a value that round-trips is compared as the number it is.

| Rule | Passes when |
|---|---|
| `exact` | the same number, or the same text |
| `shape` | the same text once whitespace is normalised, e.g. `[19 2]` from `mat2str(size(y))` |
| `rel=1e-12` | `\|actual - expected\| <= 1e-12 * \|expected\|` (`<= 1e-12` when expected is 0) |
| `abs=1e-9` | `\|actual - expected\| <= 1e-9` |
| `div=ADR0123` | the values **differ**. A recorded divergence: if the two engines ever agree the line fails, saying the divergence is retired and must be deleted from the ADR |

A fixture with no recording fails ("not recorded"). A line printed here but absent from the
recording fails ("re-run record-matlab.ps1"). A rule that differs between the two sides fails, because
a fixture and its recording are the same script.

## Writing a fixture

- MATLAB dialect only, and only forms both engines accept. A form JGraph refuses is not a fixture
  line; it is a capability-probe row in `head2head_v2/scripts/d14_capability.m`.
- Print with `fprintf`, never `disp` — display formats differ and are not what is being measured.
- No `rand`. Deterministic data only: `mod((1:n)*0.618033988749895, 1)` is the house noise.
- Pin what the algorithm does, not only what it answers: an ODE solver's `nsteps` is `exact`; a
  final state is `rel=`; an event time is `abs=`.
- Choose the tolerance the operation promises. An integrator asked for `RelTol` 1e-6 is pinned
  at `rel=1e-6`, not 1e-12.
- One fixture per milestone and topic; keep each under a few hundred lines so a failure is readable.

## Recording and running

```powershell
powershell -File tools/parity/record-matlab.ps1 -Fixtures m124_ode45
dotnet test tests/JGraph.Tests --filter "FullyQualifiedName~MatlabParity"
```

To see JGraph's side by hand:

```powershell
src\JGraph.Cli\bin\Release\net8.0\jgraph.exe -batch tests\JGraph.Tests\MatlabParity\fixtures\m124_ode45.m > actual.txt
python tools/parity/compare.py tests/JGraph.Tests/MatlabParity/expected/m124_ode45.txt actual.txt
```
