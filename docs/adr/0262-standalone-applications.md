# ADR 0262 — Standalone applications: the launcher, mcc -m and -e, compiler.build

## Status

Accepted, 2026-10-24. Compiler and SDK plan, stage K6.

## Context

K3 gave JGraph an archive and `jgraph --deployed app.ctf`, K4 a runtime folder, K5 a window host
outside the IDE. K6 makes what R2025b's `mcc -m`, `mcc -e`, `compiler.build.standaloneApplication`
and `standaloneWindowsApplication` make: a program a user starts by name, with no JGraph installed
beyond the runtime.

What R2025b does was measured first by `record-k6.ps1` (`tools/matlab-checklist/compiler-probes`).
`probe_k6_build` and `probe_k6_roots` built 30 programs windowless. `pe-resources.py` read every
program's resources. The console programs ran through `run-quiet.ps1`. The windowed ones ran with
the user's leave (given 2026-10-24), each watched by a window timeline and closed through UI
Automation patterns.

- **The build (`k6_build`).**
  - `mcc -m` and `-e` write the program, `readme.txt` and K2's five logs. `mcc -e` writes no
    `splash.png`; only `compiler.build.standaloneWindowsApplication` does, resized to 400 by 400.
  - `-W main:<name>` names the component, not the program: the program is named for the main unless
    `-o` names it. The last of `-m`, `-e` and `-W` wins. A missing `-d` folder is made.
  - `-o` takes a letter or `_` and then letters, digits and `_` (`COM_ERR_ILLEGAL_OUTPUT_NAME`).
    `-r` takes an `.ico` file only (`EMBED_ICON_FAILED` for a PNG). `-h` names a help text file.
  - The readme is a title line, `<name> Executable`, and three numbered sections, with CR LF line
    ends. With `-C` it lists the `.ctf` first.
  - compiler.build writes the mcc command its writers build (`-W`, `-T link:exe`, `-d`, `-v`,
    `-Z autodetect`, `-o`, the target's flags, `-r <temp>\icon.ico`, the file) and, with `Verbose`,
    prints it first. Its icon is made from `ExecutableIcon` at 256, 128, 48, 32, 24 and 16 pixels.
- **Where files go (`k6_roots`).** mcc searches its main's own folder, where
  `requiredFilesAndProducts` does not. A file under the folder mcc runs in goes under
  `fsroot/<component>/` by its path from there; any other goes under its full path. K3 had placed
  files by the main's folder, which the probes then could not tell apart.
- **The program (`k6_pe`).**
  - A PE launcher of 1.1–1.2 MB, console or windowed, the archive appended after its last section.
    The windowed one imports GDI+ for the splash.
  - The version block holds the version as the file's and the product's, with `2.3` read as
    `2.3.0.0`, in one language-neutral Unicode table of two strings.
  - An icon is added as a second, language-neutral icon group beside the launcher's own. Every entry
    of that group carries five bytes of its image's PNG header where the size belongs (13 by 0
    pixels, 100 colours), with `-r` and with compiler.build alike.
- **The console programs (`k6_console`).** A first argument of `-?` or `/?` prints the help
  without starting the runtime (0.3–0.6 s, against 1.9–3.0 s for a run). With no `-h` the help is
  `Usage:<program path> <optional arguments>`; with one, the file's text. `-h`, `-help` and `--help`
  reach the main as arguments.
- **The windowed programs (`k6_windows`).**
  - They write nothing, not even to the standard handles they inherit.
  - With a `splash.png` beside the program, a 400 by 400 window shows 0.8 s after the start. It stays
    until the first figure or app window is made, or the program ends; with none, there is no splash.
  - An uncaught error shows a dialog titled `Error` holding the message and `Error in => k6_err.m at
    line 12`, and the program exits with 249 when it is dismissed.
  - A figure or an app keeps the program alive until it is closed. R2025b then takes another 12–20 s
    to end.

## Decision

### The launcher (`native/jglaunch`)

- **One C++ source built twice** by `build.ps1` (MSVC through vswhere, `nethost` linked statically,
  the C runtime static, `/Brepro`): `jglaunch-console.exe` and `jglaunch-gui.exe`, about 300 KB
  each. They are checked in under `native/win-x64/launchers`, as `libopenblas.dll` is, so neither
  building JGraph nor building a program needs a C++ compiler. `JGraph.Deploy` carries them to every
  output folder's `launchers\win64`, and `publish-runtime.ps1` to the runtime's.
- **At start the launcher:**
  - prints the help for a first argument of `-?` or `/?`, from the resource `JGRAPH_HELP` or R2025b's
    usage line, without starting .NET;
  - finds the JGraph Runtime of the version in its resource `JGRAPH_RUNTIME`, as `RuntimeLayout.Find`
    does: `JGRAPH_RUNTIME_ROOT`, the registry key, then its own folder;
  - shows `splash.png` from beside itself in a layered window, the windowed build only;
  - starts .NET through `hostfxr` on the runtime's `bin\win64\jglaunch.dll` with
    `--program <itself> [--splash <window>] -- <its arguments>`, and exits with the run's code.
- **Where it cannot start,** it says why on standard error, or in a message box when a windowed
  program has none, and exits with 1: no runtime of its version, a damaged runtime, or no .NET 8
  Desktop Runtime.

### The program's run (`jglaunch.dll`)

- **`src/JGraph.Runtime.Launch`** is that library, on the Windows desktop framework so a program can
  open windows. `StandaloneProgramRun` (in the window host) does the work.
  - It runs the main as `jgraph --deployed` runs one (K1, K3), on `WindowedBatchRun` and the window
    host's own application. A figure or app left on screen therefore keeps the program answering its
    user until the last window closes.
  - A windowed program, as its manifest says, writes nothing. Its uncaught error is R2025b's dialog
    (`IDeployedErrorDialog`; `JgsDeployedMain.DialogText`), and it exits with 249 once the dialog is
    dismissed. The splash goes when the first window shows or the run ends.
- **A program built with `-C`** carries no archive. `ScriptDeployment` finds `<name>.ctf` beside it
  and extracts it to `<name>_mcr` there, as K0 measured, again when it is incomplete, changed or
  rebuilt (`RuntimeCache.OpenBeside`).

### The program's build (`JGraph.Deploy`)

- **`StandaloneProgram.Write`** copies the template and replaces its resources through the Windows
  resource update API (decision KO5):
  - one icon group describing its images (a 256-pixel image's size written as 0);
  - R2025b's version block;
  - the RCDATA `JGRAPH_RUNTIME` and `JGRAPH_HELP`.

  It then appends the archive, or writes it beside the program for `-C`, and writes `readme.txt`.
- **`RuntimeDelivery`**, JGraph's own (KD4):
  - `installed`, the default: R2025b's model.
  - `bundled`: the runtime's marker and `bin` copied beside the program, which then runs wherever
    its folder is copied. It is `-W main:<name>,runtime=bundled` on the mcc line and the hidden option
    property `RuntimeDelivery`. It still needs the .NET 8 Desktop Runtime.
  - `singlefile` is refused for now (open item 186).
- **`RuntimeLayout`** moved here from `JGraph.Runtime`: a build looks with it for the runtime it
  bundles and for the templates.
- **`IconFile`** reads and writes `.ico` files. **`MccCommandLine`** reads mcc's command line: K6's
  switches, the dependency switches compiler.build writes, `-R` (K4) and `-j` (K3b). It refuses in
  R2025b's words, and refuses every other switch R2025b knows as one JGraph does not build yet.
- **`CtfLayout.FromReport`** places files by the folder mcc runs in when told it, with the main's
  own folder first on the path. Without it, the main's folder places them, as before.

### mcc and compiler.build (`JGraph.Scripting`)

- **`mcc`** resolves its files as written or on the path, and refuses `.mlx` (JGraph has no live
  scripts) and anything that is not `.m`, `.p` or `.mlapp`. It runs K2's analysis with the main's
  folder searched, warns as R2025b warns (excluded functions, `startup.m`; `getSecret` in JGraph's
  words), builds the archive (P-files with `-j`), writes the program and the logs, and with `-v`
  prints R2025b's lines with JGraph named.
- **compiler.build's classes**, `compiler.build.StandaloneApplicationOptions`, `Results` and
  `compiler.runtime.Dependencies`, are value classes declared in C# as uistyle's Style is. Their
  properties are in R2025b's order, and every setter is transcribed from
  `toolbox/compiler/cli/build` and `toolbox/compiler/utils`, with the messages from
  `resources/Compiler/en`.
  - A name-value constructor sets in the order R2025b's `setdiff` sorts.
  - `ExecutableName` and `OutputDir` are derived as R2025b derives them.
  - `ExecutableVersion` is not checked until mcc.
- **The two build functions** make the icon (imresize's bicubic) and the splash. They write
  R2025b's mcc command, run JGraph's mcc and answer `compiler.build.Results`. Neither function nor
  mcc exists in a deployed session.
- **compiler.build's default icon and splash** are JGraph's own artwork, kept where R2025b keeps
  its own relative to `matlabroot`: `toolbox\compiler\packagingResources`. They were made from
  `jgraph.ico` by `PackagingResources/make_art.m`.

### Along the way

- **An object's display** heads with the class's name without its package, and sums up a cell, a
  struct, a table or an object property as R2025b does (`{}`, `{'autodetect'}`, `{2×1 cell}`,
  `[0×0 struct]`, `[1×1 Class]`).
- **The runtime folder** publishes `jglaunch.dll` and the templates, at 169.8 MB (K5's was 168.3 MB).

## Measured

- **`MccCommandLineTests` (15)** cover K0's refusals and K6's output names, the target switch, the
  wrapper and the versions.
- **`StandaloneProgramTests` (7)** read the resources back through Windows itself: the subsystem,
  the version strings, the help and runtime resources, the icon group's entries and images, the
  archive, `-C` and the readme.
- **`StandaloneBuildTests` (8)** run mcc and compiler.build from a session and run the programs in
  process:
  - the arguments;
  - the main's own folder searched and the files placed by the current folder;
  - the refusals and the options;
  - the results;
  - the verbose command;
  - the windowed build's splash;
  - `-C` extracted to `_mcr`;
  - no compiler in a deployed session.
- **The fixture `k6_compiler_build`** has 139 lines from R2025b, three of them recorded
  divergences, in both representations:
  - the options, their derived names and refusals;
  - mcc's refusals;
  - the files of three mcc builds;
  - `Results` and its display;
  - the verbose command;
  - a windowed build.
- **`run-k6.ps1`** built the same programs with JGraph's mcc and compiler.build under a published
  runtime, read them and ran them, and recorded them by the same names in `recordings-jgraph`:
  - **The console runs** match R2025b's line for line but one. `fprintf('%s', [2 3])` is R2025b's
    whole argument as characters, where JGraph repeats its format (open item 184; not K6's).
  - **The run times** are 0.3–0.4 s against R2025b's 1.9–3.0 s. The help prints in under 0.1 s.
  - **The windowed runs:**
    - they write nothing;
    - the splash shows with a `splash.png` and goes when the first window shows;
    - figures and apps keep the program alive and their buttons answer UI Automation, which R2025b's
      Chrome-hosted windows did not;
    - the error dialog has R2025b's title and text and the exit is 249;
    - each run ends within 0.1 s of its last window.

## Divergences

- **JGraph's launcher is its own program.** A JGraph standalone program is about 300 KB, where R2025b's launcher is 1.1–1.2 MB; it reads its runtime version and help from resources of its own, `JGRAPH_RUNTIME` and `JGRAPH_HELP`.
- **A program's icon replaces the launcher's** in one icon group that describes its images; R2025b adds a second group whose entries read 13 by 0 pixels (`-r` and compiler.build alike).
- **readme.txt is JGraph's own text** under R2025b's title line and three numbered sections, naming the JGraph Runtime and its installer where R2025b names the MATLAB Runtime and MCRInstaller.exe.
- **`mcc -v` names JGraph's compiler**: `Compiler version: JGraph 0.2.3 (R2025b)` where R2025b prints `Compiler version: 25.2 (R2025b)`.
- **compiler.build's default icon and splash are JGraph's artwork**, in the same place relative to `matlabroot`.
- **A windowed program ends when its last window closes**; R2025b's takes another 12–20 s.
- **A program whose runtime cannot be found says so in JGraph's words** and exits with 1.
- **`mcc` refuses a live script** (`JGraph:mcc:liveScript`); R2025b builds one.
- **mcc's `getSecret` warning is JGraph's** (secrets come from the machine's environment variables; `-J` is not built), and no `<name>_secrets_manifest.json` is written.
- **An `.ico` `ExecutableIcon` is refused at the build** (`invalidInputExt`); R2025b's `imfinfo` cannot read the icon its own `makeico` writes and refuses it on set.
- **JGraph's `RuntimeDelivery`** ('installed' or 'bundled'), hidden on the options and `runtime=bundled` on `-W`, has no counterpart in R2025b, whose standalone builds always need an installed runtime.
- **The splash goes when the first window is shown**; R2025b's goes when the window is made, a moment before it shows.

## Still open

- Item 184: `fprintf` and `sprintf` with `%s` and a numeric array argument.
- Item 185: `mcc -?`'s text is JGraph's own; the rest of mcc's switches are K7's.
- Item 186: `RuntimeDelivery='singlefile'`, and a bundled program that carries .NET too.
- Item 187: a JGraph program's code signature. R2025b's programs are not signed either, but a
  bundled program is likely to be copied about.
- Item 180 stays K9's: a standalone program loads the window host from its runtime's `bin`, but
  through `jglaunch.dll`'s own references, not through `JgRuntime`'s search.
