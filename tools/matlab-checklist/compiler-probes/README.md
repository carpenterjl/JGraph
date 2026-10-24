# Compiler and SDK probes (stages K0-K3b)

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
| `probe_k1_numeric.m` | 108 more strings through `mclisnumerictype`, the answer only (stage K1). |
| `probe_k1_session.m` | The deployment builtins in an ordinary R2025b session, their refusals, and the text of the messages a deployed session raises (stage K1). |
| `run-k1.ps1` | Runs K0's cases in JGraph's deployed session (`jgraph -batch main.m --deployed <folder> -- args`), each program laid out as R2025b extracted it, and writes `recordings-jgraph/<case>.out.txt` and `recordings-jgraph/differences.txt` (stage K1, ADR 0256). |
| `k2_make_tree.m` | Writes stage K2's tree: a main and one file per dependency question, a library folder, a second file of one name, a folder to add whole. |
| `probe_k2_depend.m` | Over that tree, `matlab.codetools.requiredFilesAndProducts` (`rfp`) and ten `mcc -m -C` builds (`mcc`): each archive's user files, the five log files and the warnings. Nothing built is run (stage K2, ADR 0257). |
| `probe_k2_rules.m` | 65 small mains, one per rule: which literals the data rule takes, the pragmas' forms, variables, dots, scripts, classes. |
| `probe_k2_names.m` | Over the dispatch table's names: the products each needs, and the non-deployable files mcc warns about. Its recording is embedded in `JGraph.Deploy` as R2025b's tables. |
| `record-k2.ps1` | Runs `probe_k2_depend` and writes `recordings/k2_rfp.out.txt` and `k2_mcc.out.txt`, the tree as `<T>`, the work folder as `<OUT>` (and `<OUT-NODRIVE>` where an archive keeps a path without its drive). |
| `record-k3-cache.ps1` | Runs K0's `k0_env` and `k0_path` under a fresh `MCR_CACHE_ROOT` per case and writes `recordings/k3_cache.out.txt`: what the runtime cache holds after a first and second run, a deleted or changed file, a deleted marker, entry or index, `MCR_CACHE_SIZE`'s forms, and eviction (stage K3, ADR 0258). R2025b's `extractCTF.exe` was measured by hand through `run-quiet.ps1`; the ADR records its messages. |
| `probe_k3b_pcode.m` | `pcode`'s outputs, flags, refusals and warnings, and how a P-file runs beside and without its source: precedence, `exist`, line 0, the stale warning, damaged files (stage K3b, ADR 0259). |
| `probe_k3b_secrets.m` | The secret functions under `-batch`, where the vault is unavailable: their argument checks and refusals, and the messages' texts. |
| `probe_k3b_build.m` | `mcc -m -C` plain and with `-s`, `-j`, `-s -j`, `-k` and `-J`, each archive's entries and manifest, and `compiler.build`'s `ObfuscateArchive`, `SecretsManifest` and `ExternalEncryptionKey` validation. `-J` fails under `-batch` (no vault). |
| `probe_k3b_secrets_build.m` | Builds a program that calls every secret function; `record-k3b.ps1` runs it with and without `K3B_SECRET` set. |
| `record-k3b.ps1` | Runs the four K3b probes and the built programs through `run-quiet.ps1`, and writes `recordings/k3b_pcode.out.txt`, `k3b_secrets.out.txt`, `k3b_build.out.txt`, `k3b_run.out.txt`, `k3b_secrets_build.out.txt` and `k3b_secrets_run.out.txt` (`-Skip` leaves steps out). |
| `recordings-jgraph/` | JGraph's recordings of K0's cases, with the same tokens, and the lines that differ from R2025b's. |
| `src/` | The probe programs. `k0_try`/`k0_do` print `key<TAB>value` or `ERR id \| message`, then any warning. |
| `recordings/` | The recordings. Machine paths are tokens: `<CACHE>`, `<BUILD>`, `<RUN>`, `<TEMP>`, `<MATLABROOT>`, `<HOME>`, `<REPO>`, and each of them without its drive as `<...-NODRIVE>`. The `[1 N]` sizes printed for path values still depend on the machine. |
| `research-2026-10-21/` | Sources from the plan's research session: the SDK client programs (C, C++, .NET MWArray, .NET Data API, Python) for stages K8-K13, the `mps.json` probes and their output for K14, and the options probes. |

## Order

```
powershell -File record-build-side.ps1 -BuildRoot <scratch>
powershell -File run-k0.ps1 -BuildRoot <scratch>
powershell -File run-k1.ps1 -WorkRoot <another scratch>
powershell -File record-k2.ps1 -WorkRoot <a third scratch>
matlab -batch "probe_k2_rules('<scratch>')" -noFigureWindows > recordings/k2_rules.out.txt
matlab -batch "probe_k2_names('<repo>/tools/matlab-checklist/matlab-r2025b-dispatch.csv')" -noFigureWindows > recordings/k2_names.out.txt
powershell -File record-k3-cache.ps1 -BuildRoot <the K0 build scratch> -WorkRoot <a fourth scratch>
powershell -File record-k3b.ps1 -WorkRoot <a fifth scratch>
```

`run-quiet.ps1` gives a program an empty stdin. Until stage K1 it held a UTF-8 byte order mark,
which Windows PowerShell's `Process.Start` writes into the pipe; `stubs` was re-recorded without it.

Builds take about 10 s each (20 s for the first in a session). A run takes about 2-3 s, and the
invisible-figure case about 25 s.
