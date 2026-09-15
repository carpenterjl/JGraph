# The MATLAB parity fixture suite

A permanent, MATLAB-free test that JGraph answers what MATLAB answers (R2025b since M125; R2024a before), line by line, by the rule
each line asks for. Introduced by M124 (ADR 0126) as the gate for the solver and Signal milestones.

## The pieces

| Path | What it is |
|---|---|
| `tests/JGraph.Tests/MatlabParity/fixtures/<mNNN>_<topic>.m` | a MATLAB-dialect script that prints `CHK\|name\|value\|rule` lines |
| `tests/JGraph.Tests/MatlabParity/fixtures/helpers/*.m` | class and function files fixtures share; on the path, never fixtures |
| `tests/JGraph.Tests/MatlabParity/expected/<same>.txt` | what MATLAB printed, recorded once, committed |
| `tests/JGraph.Tests/MatlabParity/expected/<same>.boxed.txt` | the boxed overlay: only the lines whose state differs under `JGRAPH_JGS_PACKED=0` |
| `tests/JGraph.Tests/MatlabParity/expected/matlab_version.txt` | which MATLAB the recordings are of |
| `tools/parity/record-matlab.ps1` | runs a fixture through `matlab.exe -batch`, keeps the `CHK` lines, writes `expected/` |
| `tools/parity/compare.py` | the comparison rules, for an ad-hoc diff of two logs |
| `tools/parity/check-ratchet.py` | holds the recordings' pending states against the ADRs that have landed |
| `tools/parity/gen-value-isolation.py` | writes the `value_isolation_gen_*` matrix fixtures and their `.owners` (never edit those by hand) |
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
| `div=ADR0123` | a recorded divergence: the recording is stamped `\|diverges\|<output>` and the line passes only on exactly that output, which must **differ** from MATLAB's — if the two engines ever agree the line fails, saying the divergence is retired and must be deleted from the ADR |
| `bits` | a whole array to the bit: the SHA-256 digests of the two `num2hex` files agree (below) |

A fixture with no recording fails ("not recorded"). A line printed here but absent from the
recording fails ("re-run record-matlab.ps1"). A rule that differs between the two sides fails, because
a fixture and its recording are the same script.

### The ratchet: states on a recorded line

A recording's line may carry two more fields after the rule. They are what lets a fixture hold
lines JGraph does not yet match without letting anything else move:

```
CHK|name|<MATLAB's value>|<rule>|pending V3|<JGraph's exact output today>
CHK|name|<MATLAB's value>|div=ADR0160|diverges|<JGraph's exact output>
RUN|pending V6|<the message the run fails with>
```

- `pending Vn` — JGraph is known to fail this line until stage Vn of the value-ownership plan lands.
  The line passes only when JGraph prints the recorded baseline, as text. A **different wrong answer
  is a regression** and fails; an answer that now agrees with MATLAB fails too, until the owning
  stage's commit removes the marker (the flip is recorded, never silent); a baseline that changes
  for a reason is re-stamped in a commit that names the line and why.
- `diverges` — an accepted divergence (the rule is `div=ADRnnnn`, the ADR's `## Divergences` names
  the line). It passes on exactly the stamped output and fails on any other, and on agreement.
  An unstamped `div=` line fails: accepting any output on a divergent line hid regressions.
- `RUN|pending Vn|message` — the whole run is known to fail with that message (a class file the
  parser refuses, say). The run must fail with exactly it; the lines it printed are checked; the
  lines it never reached are excused. A run that succeeds, or fails otherwise, fails the fixture.

Printed lines never carry a state. States are written by the **stamp mode**: set
`JGRAPH_PARITY_STAMP` to the expected folder to write and run the parity tests; each recording is
re-stamped from what JGraph printed, and the theory fails with a summary of what it did, so a
stamping run never reads as a green one. The owner of a failing line comes from the fixture's
`.owners` sidecar (`name<tab>Vn`, `*<tab>Vn` for the rest, `RUN<tab>Vn` for the run); a line no
owner claims is reported, not stamped, and a divergence that has come to agree is reported as
retired. `tools/parity/check-ratchet.py` reads the recordings without an engine: it fails a pending
line on an unknown stage, or on a stage whose ADR has landed under `docs/adr`, or with a baseline
that already agrees, and prints the pending count per stage — the ratchet's progress.

```powershell
$env:JGRAPH_PARITY_STAMP = "$PWD\tests\JGraph.Tests\MatlabParity\expected"
dotnet test tests/JGraph.Tests --no-build --filter "FullyQualifiedName~MatlabParityFixtureTests"
Remove-Item Env:JGRAPH_PARITY_STAMP
python tools/parity/check-ratchet.py
python -m unittest tools/parity/test_compare.py
```

### The boxed overlay: one recording, two representations

A recording's states are one representation's answers — the packed one, the default. The suite
also runs with `JGRAPH_JGS_PACKED=0` (the boxed lanes of `tools\run-lanes.ps1`), and the boxed
path answers some pending lines differently: a line that agrees with MATLAB only there, a
different wrong answer, a defect only boxed storage has. Those lines live in
`expected/<fixture>.boxed.txt`, the **overlay**: the same grammar, holding only the lines whose
state differs from the recording's, and a `RUN|pending` line when the run's fate differs. In the
boxed lane both comparators merge the overlay over the recording before comparing
(`MatlabParityComparer.ApplyOverlay`, `compare.py`'s third argument); in the packed lane the
recording is read as it is. An overlay line changes only a line's state and baseline — one naming
no line of the recording, changing its value or rule, or equal to the recording's line is a
problem, so an overlay cannot drift from its recording unnoticed.

The overlay is written by the stamp mode run in the boxed lane, which stamps the merged recording
and writes back only the lines that differ (deleting the overlay when none does); the recording
itself is written only by a stamp in the packed lane. A stage's commit therefore runs the stamp
twice, once per representation:

```powershell
$env:JGRAPH_PARITY_STAMP = "$PWD\tests\JGraph.Tests\MatlabParity\expected"
dotnet test tests/JGraph.Tests --no-build --filter "FullyQualifiedName~MatlabParityFixtureTests"
$env:JGRAPH_JGS_PACKED = "0"
dotnet test tests/JGraph.Tests --no-build --filter "FullyQualifiedName~MatlabParityFixtureTests"
Remove-Item Env:JGRAPH_PARITY_STAMP, Env:JGRAPH_JGS_PACKED
```

`check-ratchet.py` reads every overlay too: it fails one that does not fit its recording and holds
its states to the same rules, and prints the overlay's pending count per stage after the recording's.
A run that fails in the recording and succeeds under boxed storage has no spelling in the overlay
and is refused; re-stamp the recording first.

### The `bits` rule

A `bits` line pins every element of an array, in order, to the bit — the check a sum, a
checksum or a `%.17g` of one element cannot make. Nothing computable in exact doubles on both
engines is a digest worth trusting for acceptance (a sum is permutation-blind; a hash that both
engines can evaluate exactly has constructible collisions), so the digest is taken on the host:

1. The fixture makes its own unique folder — `bitsdir = tempname; mkdir(bitsdir);` — and writes
   the array through the `writebits` helper copied from `p0_bits_rule.m`: one header line
   `class rows cols [pages…]`, then one `num2hex` row per element in column-major order (sixteen
   digits for a double, eight for a single), the real plane then the imaginary plane for complex,
   logical, char and integer arrays written as the doubles of their values. The helper writes the
   transposed char matrix in chunks of 65536 rows because `fprintf('%s', charMatrix)` consumes a
   char matrix column-major on both engines.
2. The fixture closes the file and prints `CHK|<name>|file:<absolute path>|bits`.
3. Whoever captures the output resolves the line before anything else sees it — the recorder
   before writing `expected\`, the xunit harness (`MatlabParityComparer.ResolveBits`) before
   `Compare`, `compare.py` on both logs — replacing the path with the SHA-256 of the file's bytes
   and deleting the file and its emptied `tempname` folder. A path that is missing, relative, or
   outside the temp folder fails that line ("bits file missing"); nothing is substituted.

`p0_bits_rule.m` pins `num2hex`'s spellings of `-0`, NaN, `-NaN`, the infinities, a subnormal
and the singles as `exact` lines, and its recording proves MATLAB and JGraph write byte-identical
files for the same array. `ParityComparatorTests` proves the rule rejects a one-ulp change, a
swap, `[1 2 2 1]` against `[2 1 1 2]`, a reversal, a rotation, a sign flip of zero, a changed NaN
payload and a changed shape, for double and for single.

## Writing a fixture

- MATLAB dialect only. A form JGraph refuses may be a fixture line when a stage of the
  value-ownership plan owns it: the fixture prints the refusal (`ERR <message>`) inside a `try`,
  and the line is stamped `pending Vn`. A refusal nobody owns is a capability-probe row in
  `head2head_v2/scripts/d14_capability.m`, not a fixture line.
- Print with `fprintf`, never `disp` — display formats differ and are not what is being measured.
- No `rand`. Deterministic data only: `mod((1:n)*0.618033988749895, 1)` is the house noise.
- Pin what the algorithm does, not only what it answers: an ODE solver's `nsteps` is `exact`; a
  final state is `rel=`; an event time is `abs=`.
- Choose the tolerance the operation promises. An integrator asked for `RelTol` 1e-6 is pinned
  at `rel=1e-6`, not 1e-12.
- One fixture per milestone and topic; keep each under a few hundred lines so a failure is readable.

### How a fixture runs, and where its helpers live

Both engines run a fixture the same way: the fixtures folder is the current folder, the fixture
runs by name from its real path (so `mfilename` is its own name and its folder is the implicit
one), and `fixtures/helpers/` is on the function path — the recorder does `cd(fixtures);
addpath(helpers); name`, the harness gives `JgsRunner.Run` the fixture's path and the helpers folder
as a search folder. Only the top-level `.m` files are fixtures. A class or function file that
fixtures share goes in `helpers/`: beside the fixtures, both enumerations would take it for a
fixture needing a recording of its own. `p1_helpers.m` proves the arrangement on both engines.

Whether a fixture is a script or a function file is its own first token, and both engines run it by
that form; a fixture that needs base-workspace semantics (a `clear` inside a callback, a `load`
into the workspace) is written as a script, as every fixture so far is.

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
