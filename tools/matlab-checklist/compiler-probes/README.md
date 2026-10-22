# Compiler and SDK probes (stage K0)

Recordings of what R2025b's MATLAB Compiler builds and what the programs it builds do when run,
for the compiler and SDK plan (`docs/plans/compiler-and-sdk-plan.md`, stage K0). Later stages
compare JGraph's builds against these.

## Running a compiled program safely

Never start an R2025b-built program or SDK client directly. Use `run-quiet.ps1`, which:

- scans the program's imports first and refuses to start it if a DLL would not be found;
- puts R2025b's `runtime\win64` first on its PATH;
- sets `SEM_FAILCRITICALERRORS`, which the child inherits;
- puts the child in a job object, so a crash ends it without a dialog and a timeout kills its
  whole tree;
- refuses a windowed (`mcc -e`) program unless `-AllowWindowed` is given, which needs the user's
  leave.

None of that stops the **beep**. The MATLAB Runtime calls `MessageBeep` (through
`bin\win64\libmwnativedisplay.dll`) every time the program raises an error, caught or not, and it
plays on the user's speakers. Every probe main therefore calls `k0_quiet` (`beep('off')`) as its
first statement. A probe that fails before main runs still beeps: for example, `mcc -n` refusing
an argument in `mainFuncForNumericStandaloneApplication`. `run-k0.ps1` marks such cases `Beeps`
and runs them only when they are named; tell the user before running one.

## Files

| File | What |
|---|---|
| `run-quiet.ps1` | The launcher described above. |
| `build_k0.m` | Builds every probe in `src/` with `mcc` into a scratch folder (outside the repo). |
| `record_k0_build.m` | Records the build side: `mcc` refusals, `StandaloneApplicationOptions` display and validation, `compiler.build.Results`, the files a build writes. |
| `record-build-side.ps1` | Runs both `.m` files under `matlab -batch -noFigureWindows` and writes `recordings/build_k0.out.txt` and `recordings/build_side.out.txt`. |
| `run-k0.ps1` | Runs each case (program, arguments, environment) through `run-quiet.ps1` in a fresh run folder and writes `recordings/<case>.out.txt`. |
| `probe_k0_numeric.m` | Which strings `mcc -n` accepts (the `mclisnumerictype` rule), with `matlab -batch`. |
| `src/` | The probe programs. `k0_try`/`k0_do` print `key<TAB>value` or `ERR id \| message`, then any warning. |
| `recordings/` | The recordings. Machine paths are tokens: `<CACHE>`, `<BUILD>`, `<RUN>`, `<TEMP>`, `<MATLABROOT>`, `<HOME>`, `<REPO>`, and each of them without its drive as `<...-NODRIVE>`. The `[1 N]` sizes printed for path values still depend on the machine. |
| `research-2026-10-21/` | Sources from the plan's research session: the SDK client programs (C, C++, .NET MWArray, .NET Data API, Python) for stages K8-K13, the `mps.json` probes and their output for K14, and the options probes. |

## Order

```
powershell -File record-build-side.ps1 -BuildRoot <scratch>
powershell -File run-k0.ps1 -BuildRoot <scratch>
```

Builds take about 10 s each (20 s for the first in a session). A run takes about 2-3 s, and the
invisible-figure case about 25 s.
