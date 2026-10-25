# Compiler and SDK probes (stages K0-K7)

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
| `src/k4/k4r_main.m`, `src/k4/k4.m` | Stage K4's programs: a main built once per `-R` option, and the function two .NET components run. |
| `probe_k4_build.m` | Builds `k4r_main` with no option, `-startmsg`, `-completemsg`, both in one `-R`, `-logfile`, `-singleCompThread`, `-nojvm`, an unknown option and a bare `-startmsg`, and `k4.m` as the .NET assemblies K4A and K4B (`ClassMap` cannot be passed by name, so the class is `k4Class`) (stage K4, ADR 0260). |
| `src/k4client/` | A C# client of K4A and K4B: two instances of K4A and one of K4B in one process, what each sees of the others' globals, persistents, base workspace, current folder, random stream, format, warning state, user data and figures, two calls from two threads, an error, and disposal. |
| `record-k4.ps1` | Runs `probe_k4_build`, each `k4r_main` from a current folder not its own (the `-logfile` build twice, the second ending on an error), and the client built with `dotnet`, all through `run-quiet.ps1`; writes `recordings/k4_build.out.txt`, `k4_ropts.out.txt` and `k4_client.out.txt`. |
| `src/k5/k5.m`, `probe_k5_build.m` | Stage K5's function and its build as the .NET assembly K5: a timer one call starts and the ticks it counts and prints, a pause, a busy loop, invisible figures (stage K5, ADR 0261). No window is shown. |
| `src/k5client/` | A C# client of K5: whether the timer ticks while the client sleeps between calls, inside a call that pauses and inside one that computes; `WaitForFiguresToDie` with nothing and with an invisible figure open; and one invisible figure left for the runtime's warning when the process ends. |
| `record-k5.ps1` | Runs `probe_k5_build` and the client built with `dotnet`, through `run-quiet.ps1`; writes `recordings/k5_build.out.txt` and `k5_client.out.txt`. |
| `src/k6/`, `probe_k6_build.m` | Stage K6's mains (`k6_hello`, `k6_fig`, `k6_err`, `k6_wait`, the app class `k6_app` and the help text) and their builds: `mcc -m` and `-e` with `-o`, `-W main:`/`WinMain:` and versions, `-r` (an `.ico` and a `.png`), `-h`, `-C`, the `.mlapp` R2025b's serializer saves from `k6_app.m`, refused output names, and `compiler.build.standaloneApplication`/`standaloneWindowsApplication` with an icon, a splash, verbose, a separate archive and `-n`; each build's files, readme, splash and results, and the options' refusals (stage K6, ADR 0262). |
| `probe_k6_roots.m` | Whether mcc searches its main's own folder when the main is built from elsewhere (it does, where `requiredFilesAndProducts` does not), and where an archive places the files by the folder mcc runs in. |
| `pe-resources.py` | What a Windows program carries: subsystem, sections, imports, icon groups and images, the version block, the manifest, the archive appended. Reads R2025b's builds and JGraph's alike. |
| `record-k6.ps1`, `k6-windows.ps1` | Runs the two probes, the PE reports and the console programs through `run-quiet.ps1`, and - with the user's leave, given 2026-10-24 - the windowed ones, each watched by `k6-windows.ps1`'s window timeline and closed through UI Automation patterns; writes `recordings/k6_build.out.txt`, `k6_roots.out.txt`, `k6_pe.out.txt`, `k6_console.out.txt` and `k6_windows.out.txt`. |
| `run-k6.ps1`, `build_k6_jgraph.m` | The same programs built by JGraph's `mcc` and `compiler.build` under a published JGraph Runtime's `jgraph -batch`, then the same reports, runs and window timelines; writes `recordings-jgraph/k6_*.out.txt` and `k6_differences.txt`, the console lines that differ from R2025b's. JGraph windows only. |
| `src/k7/`, `probe_k7_switches.m`, `probe_k7_more.m` | Stage K7's mains (`k7_hello`, `k7_excl` naming an excluded function, `startup/k7_start` with a `startup.m` beside it) and 188 mcc calls, each into a folder of its own: the obsolete switches, the ones a standalone build ignores, letters read as switches, the syntax's refusals, `-T`'s forms, every `-W` target's checks, `-B` bundles, `-w` over the startup, repeated-file and ignored-switch warnings and its table, `-K`, `-I`, `-R`, `-Z`, and MATLAB's own warning state (stage K7, ADR 0263). |
| `record-k7.ps1` | Runs the two probes under `matlab -batch -noFigureWindows`, then R2025b's `bin\mcc.bat` from cmd.exe in the probe's source folder (standard output, standard error, exit code, time); writes `recordings/k7_switches.out.txt`, `k7_more.out.txt` and `k7_cmd.out.txt`. Nothing built is run and no window is shown. |
| `run-k7.ps1`, `compare-k7.py` | The same probes under JGraph's `jgraph -batch` and the same command-line cases through `jgmcc.cmd`; writes `recordings-jgraph/k7_*.out.txt` and `k7_differences.txt`, each case whose outcome, warning or files differ (JGraph's files listed from the disk, as its `dir` does not search `**`). |
| `recordings-jgraph/` | JGraph's recordings of K0's cases, K6's and K7's, with the same tokens, and the lines that differ from R2025b's. |
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
powershell -File record-k4.ps1 -WorkRoot <a sixth scratch>
powershell -File record-k5.ps1 -WorkRoot <a seventh scratch>
powershell -File record-k6.ps1 -WorkRoot <an eighth scratch>            # -Skip windows without the user's leave
powershell -File ..\..\..\installer\publish-runtime.ps1 -Output <a runtime scratch>
powershell -File run-k6.ps1 -WorkRoot <a ninth scratch> -Runtime <the runtime scratch> -Mlapp <eighth>\b\src\mlapp\k6_app.mlapp
powershell -File record-k7.ps1 -WorkRoot <a tenth scratch>              # about 30 min: 188 builds and refusals
powershell -File run-k7.ps1 -WorkRoot <an eleventh scratch>             # JGraph's CLI Debug build; about 15 s
```

`run-quiet.ps1` gives a program an empty stdin. Until stage K1 it held a UTF-8 byte order mark,
which Windows PowerShell's `Process.Start` writes into the pipe; `stubs` was re-recorded without it.

Builds take about 10 s each (20 s for the first in a session). A run takes about 2-3 s, and the
invisible-figure case about 25 s.
