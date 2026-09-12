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
| `bits` | a whole array to the bit: the SHA-256 digests of the two `num2hex` files agree (below) |

A fixture with no recording fails ("not recorded"). A line printed here but absent from the
recording fails ("re-run record-matlab.ps1"). A rule that differs between the two sides fails, because
a fixture and its recording are the same script.

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
