# ADR 0263 — The rest of mcc: every switch, bundles, -w, and mcc from the command line

## Status

Accepted, 2026-10-24. Compiler and SDK plan, stage K7.

## Context

K6 built `mcc -m` and `-e` with the switches a standalone program needs and refused every other
switch R2025b knows as one JGraph did not build yet (`JGraph:mcc:optionNotBuilt`). K7 reads the
rest of mcc's command line as R2025b reads it: the obsolete switches, the switches a standalone
build takes and ignores, `-T`'s forms, every `-W` target's checks, `-B` bundles, `-w`, `-K`, the
trace switches, and mcc from the operating system's command line.

What R2025b does was measured first by `record-k7.ps1` (`tools/matlab-checklist/compiler-probes`),
with nothing built run and no window shown:

- **`probe_k7_switches` (`k7_switches`)**, 140 mcc calls each into a folder of its own, and
  **`probe_k7_more` (`k7_more`)**, 48 more for what the first left open. They found:
  - **Several letters are as many switches.** `-mv`, `-vm` and `-mC` build. `-help` is read as
    `-h -e -l -p`, and `-h` takes `-e` as its file. `-ok7ns` is `-o -k -7 -n -s`, and `-7` is
    unrecognized. A valued switch takes the next argument even when it is a switch (`-M -d` takes
    `-d`). One at the very end, with nothing after it, is taken and does nothing. An empty value is
    `COM_ERR_EXPECTED_ARGUMENT`, worded with R2025b's example of one.
  - **The obsolete switches.** `-O`, `-i`, `-z`, `-L <x>` and `-t` print a note that they are no
    longer supported, and the build goes on (K0's `-L` and `-t` without `-m` only failed for lack of
    a wrapper). `-V`, `-win32`, `-win64`, `-y` and `-x` are refused. `-F <file>` alone is
    `COM_ERR_DASH_F_DEPRECATED`; `-F` with anything else is K0's `OPTION_F_MUST_BE_ALONE`.
  - **What a standalone build ignores.** `-g`, `-G`, `-M <x>`, `-f <x>` and `-c` warn
    `COM_WARN_SWITCH_IGNORED` (`-G` as `-g`). `-b`, `-S`, `-u`, `-H`, `-Y <file>`, `-A all` and
    `-P` are taken without a word. `-P` takes the next argument unless it is a switch. `-A` takes
    `all` and nothing else. `-c` with no wrapper is `MISSING_WRAPPER_FOR_CODEGEN_ONLY`; `-D` is
    unrecognized.
  - **`-T`.**
    - Its value is words from link, compile, codegen, exe and lib joined by colons, any of them
      empty: `exe` and `link:` build, `link:nothing` is refused.
    - `link:exe` is the program with its archive inside. `compile:exe` writes the program and the
      archive beside it. `codegen` writes the archive, the readme and the logs, and no program.
    - `-U` beside `-m` or `-e` writes the archive beside the program, as `-C` does.
    - A standalone wrapper with a `lib` target is `WRAPPER_TARGET_MISMATCH`. `-T` with no wrapper
      is `MISSING_WRAPPER`.
  - **`-W`.**
    - **The standalone wrappers.** `main` and `WinMain`, in any case and with or without a colon,
      ignore anything after the name they do not know, and the last `version=` wins. Two `-W`s
      build; two `-o`s are `COM_ERR_MULTIPLE_COMPONENT_NAMES`.
    - **The library targets** each have their checks:
      - `lib` needs a name (`ILLEGAL_OUTPUT_NAME ''`).
      - `cpplib` needs a name, refuses `named`, and refuses any option but its own
        (`api=bogus` is `BAD_ARG_FOR_CPPLIB`).
      - `dotnet` takes `api=` only right after the name and only `matlab-data`, `mwarray` or
        nothing, and a .NET version of 4.0 or 5.0.
      - `python`, `com`, `excel` and `mpsxl` need a name (`ILLEGAL_COMPONENT_NAME`).
      - `CTF` needs `-U`, and `spark` needs `-C`.
  - **`-B file[:a,b]`** is replaced by the file's switches, split at white space and across lines,
    `%1%`, `%2%` filled in. A missing argument, or none given, is `ILLEGAL_BUNDLE_REPLACEMENT`;
    extra arguments are left unused; `file:` gives one empty argument. A bundle may name another. A
    missing file is `COM_ERR_UNABLE_TO_OPEN_FILE`. R2025b's own bundles (`csharedlib`, `cpplib`,
    `dotnet`, `cjava`, `ccom`, `cexcel`, `cmpsxl`, `macro_option_*`) live in
    `toolbox/compiler/bundles`, and `-/t` shows that `-m` is read through `macro_option_m`.
  - **`-w`.**
    - `list` prints a table of six entries (`all_warnings`, `specified_file_mismatch`,
      `repeated_file`, `switch_ignored`, `missing_lib_sentinel`, `demo_license`) and builds
      nothing. `enable`, `disable` and `error` set every warning, or with `:id` one.
    - The id is the table's short name or any message identifier with a colon, known or not;
      `disable:COM_WARN_STARTUP_FILE_INCLUDED` is refused. The words are taken in any case; `on`
      and `off` are taken and do nothing.
    - `error` shows the warning, then fails with `COM_ERR_WARNING_TREATED_AS_ERROR` and leaves no
      files. A warning disabled by name stays unseen even while every warning is an error, and the
      build still fails.
    - MATLAB's own `warning('off', id)` silences mcc's warnings too, and `-w` leaves MATLAB's state
      as it was.
  - **`-K`** keeps what a failed build wrote: a bad `-r` icon leaves the archive, the readme and
    the logs, where without `-K` nothing is left.
  - **Files.** A file named twice warns `COM_WARN_REPEATED_FILE` with its full path, and is taken
    once. A file that is not code is `COM_ERROR_INCLUDE_FILE_WITHOUT_DASH_A`. A missing file is
    named without its folder. `-h`'s file is looked for before anything else. A missing `-I` folder
    is `DEPFUN_ERR_INVALID_DIR_NAME`. A `startup.m` beside the main is compiled in and warned about
    (K0 saw it run). `-R -nojvm` warns `COM_WARN_OPTION_NOJVM`. A `-Z` package that is not installed
    is `REQ_UNEXPECTED_ERROR` with the support-package validator's text.
  - **`-?`, and mcc with no arguments,** print the help. `-/?` prints the trace switches; `-/t`
    prints the processed command line before the build.
- **`k7_cmd`**, R2025b's `bin\mcc.bat` from cmd.exe in the probe's source folder:
  - Output and timing.
    - mcc prints to standard output, its warnings there between `[`+backspace and `]`+backspace.
      A refusal goes to standard error between `{`+backspace and `}`+backspace, its lines without
      trailing spaces, then `ERROR: MATLAB error Exit Status: 0x00000001`, exit code 1.
    - The help exits with 0. A run takes 3.8–10.4 s.
  - Arguments.
    - Single quotes around an argument are taken off.
    - `-Y <file>` is a licence check outside MATLAB, and fails with MathWorks Licensing Error 2.

## Decision

### The command line (`MccCommandLine`, `MccWarnings`)

- **Letters split in place.** An option of several letters (not `-win32`/`-win64`, not `-/x`) is
  replaced in the argument list by its letters, each a switch. So every valued letter takes the
  next argument in turn, as R2025b's does.
- **Bundles in place.** `-B` reads its file against the folder mcc runs in, then from
  `toolbox\compiler\bundles` beside JGraph (`MccCommandLine.BundleFolder`). JGraph ships
  R2025b's bundle names with their switches, from `src/JGraph.Deploy/Bundles`. The file's switches
  replace `-B` and its value in the argument list. A bundle naming bundles more than 32 deep is
  refused (`JGraph:mcc:bundleDepth`) rather than read forever.
- **Each switch is read as k7_switches measured it.** `Notes` holds the obsolete switches' notes,
  printed before the build. `IgnoredSwitches` holds the warned ones, and the silent ones are taken.
  `Link` holds `-T`'s form, and `SeparateArchive` covers `-C`, `-U` and `compile:exe`.
  `KeepPartialOutput` is `-K`, and `Trace` and `ShowTraceHelp` are the trace switches. `Processed`
  is the command line as read, for `-/t`.
- **`-W`'s targets** are checked where they are given, with R2025b's refusals, then refused once
  the command line is read:
  - **Not built yet** (`JGraph:mcc:targetNotBuilt`): `lib` and `-l` (stage K11), `cpplib` (K12),
    `dotnet` (K8), `python` (K13), `CTF` (K14), `excel` and `mpsxl` (K17).
  - **Out of scope** (`JGraph:mcc:targetOutOfScope`, section 10 of the plan): `java`, `com`,
    `hadoop`, `spark`, `webapp`.
- **`-w`** is `MccWarnings`. It holds a state for every warning and one per identifier (a short
  name stands for its message identifier) and decides `Shows` and `Fails` as k7_more measured.
  `ListText` prints R2025b's table, line for line, with the states set.
- **The encryption switches** `-s`, `-k` and `-J` stay refused (`JGraph:mcc:optionNotBuilt`), by the
  decision of 2026-10-24. `-E <file>` is refused the same way.

### The build (`mcc`)

- **The order is R2025b's:**
  1. `-h`'s file is checked, inside `Parse`.
  2. The notes and, under `-/t`, the processed line are printed.
  3. The files are found, the repeated file is warned of, and what is not code is refused.
  4. The ignored switches are warned of, and `-I` is checked.
  5. The analysis runs, with its excluded-file, `startup.m` and `getSecret` warnings.
  6. The icon is read last, as R2025b embeds it last.
- **Every mcc warning goes through `-w`:** shown if `-w` and MATLAB's own warning state allow, then
  failing the build if `-w` makes it an error.
- **A `startup.m` beside the main** is added to the build's roots, so it is analysed and placed in
  the archive with the main; K6's warning for it now fires. K2's analysis never added one.
- **`-T codegen`** writes the archive as `<name>.ctf` with the readme and the logs
  (`StandaloneProgramOptions.ArchiveOnly`). Under `-K`, a failed icon leaves the same.
- **`mcc -?` and `mcc` alone** print JGraph's own help (`MccCommandLine.HelpText`), in R2025b's
  order: the switches every target reads, the targets JGraph builds, the ones it does not build yet.
  `-w list` prints the table. Neither builds anything.

### mcc from the command line (`jgraph mcc`, `jgmcc.cmd`)

- `jgraph mcc …` (`StartupMode.Mcc`, the word `mcc` as the first argument) runs
  `MccCommand.Run` over the rest: a fresh MATLAB-dialect session over the current folder, and
  JGraph's mcc called directly with the words, single quotes taken off.
- Its output is mcc.bat's:
  - mcc's lines go to standard output, its warnings between `[`/`]` and a backspace.
  - A refusal goes to standard error between `{`/`}` and a backspace, its lines without trailing
    spaces, then the closing line, exit code 1.
- `jgmcc.cmd` beside `jgraph.exe` runs `jgraph.exe mcc %*` and passes the exit code on, under the
  name R2025b's `mcc.bat` suggests. The installer checks that it and the bundle folder are staged.

### Along the way

- **`copyfile`** copies a folder (its contents into the destination, which is made) and a name with
  wildcards in its last part (every match into the destination folder, which is made), as MATLAB's
  does. K7's own probe needed both, and JGraph took neither. Not probed against R2025b (open item
  190).
- **`compare-k7.py` and `run-k7.ps1`** run both probes and the command-line cases with JGraph and
  compare them with the recordings case by case.

## Measured

- **`run-k7.ps1`** (`recordings-jgraph/k7_differences.txt`).
  - `k7_switches`: 125 of 140 cases agree. Every case that differs is listed under Divergences:
    eleven builds of targets JGraph does not build, `-E`, `-R -nojvm`'s warning, and the excluded file's name
    (K6's divergence, twice).
  - `k7_more`: 47 of 48 agree. The one that differs is `warning('error', id)` before mcc, a form
    JGraph does not have (open item 189).
  - `k7_cmd`: 5 of 13 cases agree. The rest are the help, `-v`'s version line, the closing line's
    "JGraph" (four refusals) and `-Y`.
- **Speed.** JGraph's command-line cases take 0.3–0.4 s, against R2025b's 3.8–10.4 s. Both probes
  run in JGraph in 10 s, where R2025b's took about half an hour.
- **The fixture `k7_mcc_switches`** has 58 lines from R2025b: refusals, the warning table three
  ways, 21 builds' outcomes, warnings and files, and two notes. 57 are exact; the help's first line
  is a recorded divergence.
- **Tests.**
  - `MccSwitchesTests` (10): the parser against both recordings.
  - `MccSwitchBuildTests` (6): codegen and compile:exe, `-K`, `-w` over the repeated-file,
    startup and ignored-switch warnings, the refusals and notes, `MccCommand` as mcc.bat, and
    `copyfile`.
  - `MccCommandLineTests` (15): updated for `-Z`.

## Divergences

- **`mcc -?` and mcc with no arguments print JGraph's own help**, laid out as R2025b's, naming the targets JGraph builds and those it does not build yet; `-/?` prints JGraph's words for the trace switches, and `-/t`'s processed line is JGraph's reading (no mccpath or mccstartup files, no `-B macro_default`).
- **The library, server and add-in targets are refused** (`JGraph:mcc:targetNotBuilt`) until their stages build them, and Java, COM, Hadoop, Spark and web app targets for good (`JGraph:mcc:targetOutOfScope`), each after R2025b's own checks of its `-W` value.
- **`-R -nojvm` gives no warning**: JGraph's `-nojvm` is inert (K4), so R2025b's `COM_WARN_OPTION_NOJVM`, that graphics may be restricted, would be untrue.
- **`-E <file>` is refused** (`JGraph:mcc:optionNotBuilt`); R2025b reads the file as one of its own project files.
- **`jgraph mcc` and `jgmcc` close a refusal with `ERROR: JGraph error Exit Status: 0x00000001`** where R2025b's mcc.bat says `MATLAB error`.
- **`-Y` is taken and ignored from the command line too**; R2025b's mcc.bat checks the licence file it names and fails without one.
- **`-T compile:exe`'s readme lists the archive beside the program**, as `-C`'s does; R2025b writes the readme of a program with its archive inside.

## Still open

- Item 185 is done here.
- Item 188: JGraph's `dir` does not search `**`. K7's probe lists its builds with
  `dir(fullfile(d, '**', '*'))`, so `compare-k7.py` lists JGraph's from the disk instead.
- Item 189: `warning('error', id)`, which JGraph prints as a warning whose text is `error`.
- Item 190: `copyfile`'s folders and wildcards, built from MATLAB's documented behaviour and not
  probed; `movefile` still takes neither.
