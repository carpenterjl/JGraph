# ADR 0274 — The compiler apps

## Status

Accepted, 2026-10-26. Compiler and SDK plan, stage K19 (decision KD6).

## Context

A builder on R2025b packages code through the compiler apps as well as through `mcc` and the
`compiler.build` functions: since R2025a one app per target, opened by `standaloneApplicationCompiler`,
`productionServerArchiveCompiler`, `dotNetAssemblyCompiler`, `pythonPackageCompiler`,
`cppSharedLibraryCompiler`, `cSharedLibraryCompiler` and `excelAddinCompiler` (and the Java and COM
ones JGraph has no target for), each a web page in MATLAB's desktop over a compiler task in a MATLAB
project; and the legacy `applicationCompiler`, `libraryCompiler`, `productionServerCompiler`,
`deploytool` and `webAppCompiler`, scheduled for removal. Stages K6 to K18 gave JGraph every build
and package the apps run; stage K19 gives it the apps.

**What R2025b does**, measured by `record-k19.ps1` (`probe_k19_apps` under `matlab -batch
-noFigureWindows`, where no app can open; `recordings/k19_apps.out.txt`, 511 keys):

- **The target-specific apps** are M-files of one optional input (the two that take a project:
  `standaloneApplicationCompiler`, `productionServerArchiveCompiler`) or none, no output. With no
  input they publish to the desktop's app gallery (`message.publish('/matlab_compiler/app_gallery',
  'StandaloneDesktopApp')`), which in `-batch` warns once that the connector is not running and does
  nothing. With a file they call `open(prjFile)`: a missing file is `MATLAB:open:fileNotFound`
  (`File 'x' not found.`), a wrong class `MATLAB:validators:mustBeTextScalar`, an empty one
  `mustBeNonzeroLengthText`, and any existing file opens silently in `-batch` (a project in the app,
  anything else in the editor). A second input is `MATLAB:TooManyInputs`.
- **The legacy apps** take two inputs at most (`MATLAB:narginchk:tooManyInputs`), warn first
  (`Compiler:deploytool:JavaAPIToBeRemoved` naming the successor for `applicationCompiler`,
  `productionServerCompiler` and `libraryCompiler` with no input or one; with `-build` or `-package`,
  `DeploytoolBuildPackageToBeRemoved` for `deploytool` and `webAppCompiler`,
  `DeploytoolFunctionalityToBeRemoved` for the rest), then `launchui` checks each input as scalar text
  (`MATLAB:<app>:invalidType` with "Expected input number N to be one of these types: char, string
  Instead its type was double."; `MATLAB:<app>:expectedScalartext` for a string array, a char matrix
  or a cell), opens the app with none, prints the help block for `-?` or `/?`, refuses one input that
  is not a readable project (`Compiler:deploytool:invalidFirstInputOneInput`, a stub project and a
  text file alike), builds or packages a legacy project for `-build`/`/build` and `-package`/`/package`
  (`Compiler:build:compatibility:projectDoesNotExist`, `notCompatibilityPRJ`), and refuses any other
  first word (`invalidFirstInputTwoInputs`); `-BUILD` is the latter. `hadoopCompiler` is
  `Compiler:deploytool:HadoopCompilerRemoved`.
- **The windows' words** come from the `deployment:desktop`, `deployment:model`,
  `compiler_ui_common`, `compiler_ui_standalone`, `compilersdk_ui_mps`, `compilersdk_ui_dotnet`,
  `compilersdk_ui_python`, `compilersdk_ui_cppSharedLibrary`, `compiler_ui_excel` and
  `deployment_common` catalogs (the app titles, the configuration types and descriptions, Main File
  and Exported Functions, Application Info, Files Required for Standalone to Run, Executable Details,
  Package Type and its three runtime deliveries, Installer Settings, Output Locations, Show Code, Create
  Build Script, Build and Package, Test Client, the packaging dialog's steps and results). The
  share window's script builder and the `compiler_ui_common:deploymentscript` catalog give the
  generated script's shape: the project root, the build options object set property by property,
  the build, the package options and the installer, under the catalog's comments.

## Decision

- **One window per target, JGraph's own.** `JGraph.Application`'s `CompilerAppWindow`, built in
  code like App Designer's window, titled as the gallery titles the app, with the toolbar (Build,
  Build and Package where the target packages, Create Build Script, Show Code, Reanalyze, and Test
  Client for an archive) and the sections in R2025b's words: the main file or the exported functions,
  the info (name, version, author, company, e-mail, summary, description), the target's own settings
  (the executable's name, type and input type and its images; the .NET API; the C++ API; the function
  signature file with Create File writing R2025b's template from the function headers; the add-in's
  class), the files required (the dependency analysis of the session, `Autodetect Data Files`,
  files and folders added by hand), code obfuscation, the installer's settings (the three runtime
  deliveries, the name, notes, root and subfolder, images, files packaged), and the output folders
  (`<root>\<name>\output\build` and `output\package`, the task layout stage K20 keeps). The status
  line is "Add a main file to make the task deployable" until it has one, then "The compiler task
  is ready". `CompilerAppGalleryWindow` is the dialog `deploytool` and `libraryCompiler` open: the
  seven targets with their descriptions. Every control carries an automation id for the window check.
- **The task is a model, the script its meaning.** `JGraph.Deploy.CompilerTaskModel` holds what the
  window shows, `CompilerTargets` the catalogs' words, and `DeploymentScript.Generate` writes the
  `compiler.build` script: only what differs from the options' defaults is set, paths under the
  project root as `fullfile(projectRoot, ...)`, the installer's part after a `compiler.runtime.download`
  line when the runtime is carried, the Data API's version note for .NET and C++. The Code View
  shows it as the task changes, Create Build Script saves it, and Build runs it in the MATLAB
  session as the prompt would (`ScriptWorkspaceWindow.RunCompilerScriptAsync`, the console's lines
  captured for the dialog); Build and Package runs the build, then the package part alone over the
  session's `buildResults`, so each is its own step. `CompilerPackagingDialog` is R2025b's packaging
  dialog in shape: the name and version, a line per step scheduled, in progress, done or failed,
  Show Log, "Process completed successfully" or "An error occurred while deploying", Open Output
  Folder and Close.
- **The commands.** `JgsBuiltins.CompilerApps` declares the fifteen names and hands the host's
  `IScriptCompilerApps` (a door like App Designer's, on `ScriptContext`) the target and the file,
  with the session's dependency analysis and current folder as `CompilerAppServices`. The checks are
  R2025b's: the target-specific apps' counts and validators, the legacy apps' two inputs, warnings,
  validators, `-?`, the project refusals and the `-build`/`-package` forms word for word;
  `hadoopCompiler` its removal. A session without windows (the CLI, a batch run, tests) refuses
  with `JGraph:compilerApps:NoWindow`. The Deploy menu opens the apps by the same commands at the
  prompt.
- **A file given to a target-specific app** is taken as the app's main file (or first exported
  function) when it is a MATLAB file, since JGraph has no projects yet; a `.prj` is refused
  (`JGraph:compilerApps:NoProject`) until stage K20, any other file as not a MATLAB file. R2025b opens
  a project in the app and any other file in the editor.
- **The Test Client** (`TestClientWindow`, the Production Server Archive Compiler's) runs a JGraph
  Production Server (`jgraph-mps`, stage K14) as a process over an instance made for the built
  archive (`JGraph.Deploy.LocalTestServer`: the program beside JGraph, a checkout's build, or
  `JGRAPH_MPS`), on the port given or any free one, shows the server's lines, and sends MATLAB
  Production Server's JSON request (`{"nargout": n, "rhs": [...]}`) for a function with its inputs,
  showing the reply; Stop ends the server, as does closing the window.
- **Java, COM and web apps** are out of scope (decisions KD1, KD2): their commands refuse with
  `JGraph:compilerApps:targetOutOfScope`. The Excel Add-in Compiler opens and shows its script, but
  its build waits for stage K17.
- **Dispatch sweep 31** for the fifteen names; the nargout audit's rows for them (no output, a file
  in R2025b), so `x = deploytool` refuses as R2025b's does.

## Measured

`run-k19.ps1`, the same probe under `jgraph -batch`, compared by `compare-k19.py`: 244 of 511 keys
agree, 267 are the divergences named below, none is unexplained. `run-k19window.ps1` (JGraph's own
windows, through UI Automation patterns): the Standalone Application Compiler opened by
`-r "standaloneApplicationCompiler('<file>')"` with the file as its main file and the status line ready,
Show Code with the build options over the main file and the installer, Build with its packaging dialog's
"Building Standalone App" ending in "Process completed successfully" and the program in the task's
output folder; the Production Server Archive Compiler opened on the same file, its Build and the
archive, then the Test Client started on a free port, a request `[41]` to the function answered
`HTTP 200` with `42`, and Stop leaving no server. `CompilerAppsTests` (7): the catalogs' words, the
task's defaults and status, the script against its expected text (the standalone with an installer,
the archive, .NET's version note, the package part alone), the commands opening their apps with the
target and the full path and the legacy warnings, twenty-two refusals in R2025b's words, the refusal
without a window, and the test client's server over an archive built in the session (health, a call).

## Divergences

- **An app opens a window where R2025b publishes to its gallery**: under `-batch` R2025b does
  nothing (one warning that the connector is not running), JGraph's session without windows refuses
  (`JGraph:compilerApps:NoWindow`); the legacy apps the same after their warning.
- **The windows are JGraph's own layout** over R2025b's words, not R2025a's web pages; a task lives
  in its window and is not stored (projects and tasks: stage K20).
- **A file given to a target-specific app**: a project refused (none until K20), a MATLAB file taken
  as the main file, another file refused, where R2025b opens the project or the editor.
- **Java, COM and web app compilers refuse** where R2025b opens them (KD1, KD2).
- **`-?` prints the catalog's entry**, not R2025b's help block; `exist` answers 5 and `which`
  names no file for built-ins that are files in R2025b (ADR 0271); the first help line is the
  catalog's signature (open item 194); `nargin` of a legacy app is 2 for its two named inputs where
  R2025b's varargin is -1.
- **A warning prints its text alone**, without MATLAB's `> In` stack lines (open item 170).
- **Every legacy project is refused** as a file that is not one, since JGraph reads none until
  stage K21 (`createDeploymentScript`, `readPRJStruct`, `deploytool -build` over a real project);
  R2025b's internal helpers (`compiler.internal.*`, `message.publish`, `license`, `feature('webui')`,
  the capability) and its catalogs read through `message()` are not JGraph's to answer.
- **`open` of a project or MATLAB file** is JGraph's `open` of a VideoWriter or a context menu
  (ADR 0183, U9), where R2025b's opens nothing in `-batch`.
- **The generated script's shape** is the share window's and the catalog's comments, measured
  against `createDeploymentScript` only in stage K21; the Test Client is a server process with one
  request form, not R2025b's dev-and-test front end.

## Still open

- Item 216: the windows' leftovers (Show All Options, Settings, Samples, support packages, the
  onboarding dialogs), the stored task (K20), the legacy projects and `createDeploymentScript` (K21),
  the Excel add-in's build (K17), the signature template's purposes, the Test Client's variable
  editors and breakpoints, the worker's beep, App Designer's Share menu.

## Consequences

- A builder sets up a build and its installer in a window as on R2025b, sees the script it stands
  for, and runs or keeps it; the script is what stages K6 to K18 already run, so the window adds no
  second path to the compiler.
- The window check drives JGraph's own windows only, by automation ids, and ends the app without
  saving; no MATLAB window was opened.
- Stage K20 gives the apps a project to keep a task in, and K21 the legacy projects and
  `createDeploymentScript`; the window's model and script generator are theirs to reuse.
