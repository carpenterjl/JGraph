# ADR 0276 — Legacy deployment projects, deploytool and createDeploymentScript

## Status

Accepted, 2026-10-27. Compiler and SDK plan, stage K21 (decision KD6).

## Context

Before R2025a the compiler apps saved a `.prj` of their own: a `<deployment-project
plugin="plugin.ezdeploy">` file (`plugin.webdeploy` for a web app) with one `<configuration>`
carrying `param.*` elements and `fileset.*` elements. R2025b still takes those files: `deploytool
-build prj` and `-package prj` build and package one, `createDeploymentScript(prj, out)` writes
the `compiler.build` script it stands for, and `deploytool prj`, the legacy apps, the target apps
and `open` upgrade it into a MATLAB project with a compiler task (stage K20's) and show the task.
Stage K19 refused every legacy file as not a project until this stage read them.

**What R2025b does**, measured by `record-k21.ps1` (`probe_k21_legacy` under `matlab -batch
-noFigureWindows`; `recordings/k21_legacy.out.txt`, 885 keys) over a sample of every sub-target
the probe writes itself, since R2025b writes the form no more and no genuine file but a .NET
example exists on this machine; the parameter sets came from the legacy plugin's own
`plugin.xml` inside `compiler.jar`, `compilersdk.jar` and `compiler_mdwas.jar`, and two genuine
files on public repositories confirmed the element order:

- **The form.** Targets `target.ezdeploy.standalone` (Application Compiler, `subtarget.standalone`),
  `target.ezdeploy.library` (Library Compiler: `subtarget.library.c`, `.library.cpp`, `.net.component`,
  `.java.package`, `.python.module`, `.com.component`, `.ex.addin`), `target.ezdeploy.mps.archive`
  (Production Server Compiler: `subtarget.mads`, `.mps.excel`), `target.webdeploy` (Web App Compiler,
  `subtarget.web.app`) and `target.ezdeploy.map.reduce` (Hadoop). The parameters are the
  application's name, icon and `param.icons` list, version, author, email, company, summary,
  description, splash (`param.screenshot`), the install path (`param.installpath.combo` and
  `.string`), logo, notes, the four folders (`intermediate`, `files.only`, `output`, `logdir`), the
  mcc options, the sub-target, the runtime deliveries (`web.mcr`, `package.mcr`, `no.mcr` and their
  names), the Windows prompt, the log, the native inputs, the help text and example, the library's
  namespace and class organization, the .NET version, remoting, shared assembly and key file, the
  type safe API, the C++ API choice, and the archive's and client's settings; the file sets are
  `main`, `resources`, `package`, `depfun` (standalone), `exports`, `classes` (packages of classes
  of files), `examples`, `documentation` (library), `package.server`, `package.client` (archive),
  `web.main` (web app). `${PROJECT_ROOT}` is the file's own folder, whatever the `location` says.
- **`compiler.internal.readPRJStruct`** is `readstruct(file, 'FileType', 'xml', 'DetectTypes',
  0).configuration` with the macros resolved and every slash made the file separator: attributes
  as `<name>Attribute`, dots and dashes as underscores, a repeated `file` as a string array, an empty
  element "", a class organization as nested structs. A name without an extension takes `.prj`.
- **`createDeploymentScript`** checks in this order: the project file exists (`projectDoesNotExist`;
  a name found on the path alone reads as `notCompatibilityPRJ`), the output's name is a variable
  name (`invalidOutputFileName`), its extension is `.m` or none (`incompatibleScriptExtension`), the
  output does not exist (`scriptFileCannotExist`), the project reads with a known sub-target
  (`notCompatibilityPRJ`, Hadoop `hadoopNotSupported`), a main file or exported functions exist
  (`noMainFileSelected`, `noExportedFunctionsSelected`), a class-based kind has a class
  (`invalidClassDefinition`); then it writes the script (`failedToWriteScript` on a folder that is
  not there) and warns about mcc options other than `-C` (`mccFlagsNotSupported`). The script: the
  build options object over the main file, the exported functions or a class map
  (`classmap("Class1") = ...` for .NET, Java and COM), `AdditionalFiles` as the resources set sorted,
  `AutoDetectDataFiles`, `OutputDir` (the intermediate folder), `Verbose`, the kind's own lines
  (a standalone's help text file block when `param.checkbox` is set, `EmbedArchive` false for `-C`,
  `ExecutableIcon` from the first of `param.icons` (`param.icon` is passed by), `ExecutableName`,
  the splash, `ExecutableVersion`, `TreatInputsAsNumeric`; a C library's `DebugBuild`,
  `EmbedArchive`, `LibraryName`; a C++ library's `Interface` (`mwarray` for the legacy API, both lines
  for `option.cpp.all`), `LibraryVersion`, `SampleGenerationFiles`; a .NET assembly's `AssemblyName`
  (the namespace), `AssemblyVersion`, samples, `EnableRemoting`, `FrameworkVersion` 4.0 or 5.0,
  `StrongNameKeyFile` for a shared assembly; a Python package's `PackageName` (`namespace.appname`);
  an Excel add-in's `AddInName`, `AddInVersion`, `ClassName`, `GenerateVisualBasicFile = "on"`; an
  archive's `ArchiveName`, with a second build and `compiler.package.excelClientForProductionServer`
  for the Excel client; a web app's `ArchiveName`), the build by `standaloneWindowsApplication` when
  the prompt is suppressed, then for the kinds that package one the runtime download line when the
  runtime is packaged and the installer options: the package set sorted as `AdditionalFiles`,
  `AddRemoveProgramsIcon`, `ApplicationName`, the author lines, `DefaultInstallationDir` (the combo's
  folder and the install path string), `Description`, `InstallerIcon`, `InstallerLogo`,
  `InstallerName` (the web name when the runtime is downloaded, else the packaged runtime's name),
  `InstallerSplash`, `InstallationNotes`, `OutputDir`, `RuntimeDelivery = "installer"` when packaged,
  `Summary`, `Verbose`, `Version` (the application's). A .NET project that asked for the type safe
  API warns `typeSafeNotSupported` when built.
- **`deploytool -build` and `-package`** (`compiler.internal.build.legacyProject`,
  `compiler.internal.package.legacyProject`) run that script: the warning that the forms are going
  away, the mcc line and its output, "Build Complete", and for `-package` the installer and
  "Packaging Complete" (an archive is copied to its redistribution folder). `-package` of a library
  or package whose `fileset.depfun` is empty fails inside R2025b's packager (`noSuchMethodOrField`
  on a string), and of a no-runtime project wants the MATLAB Runtime installer.
- **The upgrade** (`matlab.internal.deployment.createProjectModelFromPRJ`, the road `deploytool prj`,
  the legacy apps, the target apps and `open` take; under `-batch` it is all they do): the file is
  renamed `.bak`, its folder becomes a MATLAB project named after the file (an existing project
  there takes the task), every file under the folder joins the project (Design for `.m`, `.mlapp`,
  `.mat`, `.c`, `.h`, `.cpp`, `.hpp`; Derived for `.dll`, `.exe`, `.lib`, `.xml`; Artifact for
  `.html`), and a task named "<project> Standalone", "C Shared Library", "CPP Shared Library",
  "NET Assembly", "Python Package", "Excel AddIn", "Deployable Archive", "Web App", "Java Package" or
  "COM Component" carries the settings: the files as the task's labels (the main file, the
  additional files, the exported functions, the samples of a C++, .NET, Python or Java library, the
  installer's files, the icons, the splash, the logo), the texts and flags as its properties (the
  install subfolder with forward slashes, the installer's version the application's). The function
  answers the task's configuration handle, the files that could not be added ("<file> could not be
  added to the task because it does not exist.") and the unsupported settings ("The following
  additional MCC options are not supported: -v", the type safe API, "Building an Excel client for
  Production Server is no longer supported using a project.  The project has been updated to build a
  deployable archive for Production Server.", "Multiple classes are no longer supported.  The first
  class name will be used."); an empty double for a file that is not a legacy project, a Hadoop
  project, or a MATLAB project's `.prj`; a Library Compiler project with an unknown sub-target
  becomes a project without a task; a stub without the mcc options element fails on that field.
  The project stays open. R2025b stores `SuppressWindowsCmdPrompt` as the inverse of the legacy
  prompt flag, drops the example text, a C++ library's version and the documentation set, labels a
  C library's examples not at all, and refuses the whole upgrade when a strong name key file is
  missing.

## Decision

- **The reader, in `JGraph.Deploy.Projects`.** `LegacyDeploymentProject` reads the file as
  `readPRJStruct` reads it (`LegacyXmlStruct`: readstruct's shape, resolved and raw), knows the
  targets and sub-targets, the parameters, file sets, icons and class organization, and refuses as
  the builders refuse (`LegacyProjectException` in R2025b's identifiers and words). Nothing writes
  the legacy form back.
- **The settings and the script.** `LegacyTaskSettings` is what the parameters mean, kind by kind;
  `LegacyDeploymentScript` writes `createDeploymentScript`'s text for every kind, measured line for
  line, and departs from R2025b where R2025b departs from the project (correctness outranks parity,
  ADR 0146's rule): a project asking for no runtime gets `InstallerName` from `param.no.mcr.name`
  and `RuntimeDelivery = "none"` where R2025b names the installer by the packaged runtime's name and
  downloads the runtime (recorded in MATLAB_Behavioral_Fixes).
- **The upgrade.** `LegacyProjectUpgrade` makes the project and the task as R2025b does, over
  stage K20's `MatlabProject` and `CompilerTaskConfiguration`, carrying the flag as the legacy
  project means it, the example text and the C++ version (R2025b's inversions and drops recorded);
  a missing strong name key file is warned about like any other missing file. `MatlabProject` learns
  the Derived and Artifact classifications and the Design extensions the upgrade showed.
- **The session, in `JgsBuiltins.Legacy`.** `createDeploymentScript` with R2025b's order of checks,
  `compiler.internal.readPRJStruct` and `matlab.internal.deployment.readPRJStruct`,
  `compiler.internal.build.legacyProject` and `package.legacyProject` (the script run in a workspace
  of its own through `EvaluateSource`, the help text file's `onCleanup` objects cleared when it
  ends, "Build Complete" and "Packaging Complete" printed, an archive copied to its redistribution
  folder, a kind JGraph does not build refused before anything runs),
  `matlab.internal.deployment.createProjectModelFromPRJ` (the upgrade, the project opened, the
  handle and the two string arrays), and the ten `compiler.internal.ui.get*DeploymentScript`
  wrappers over a project's task (stage K20's script; the web app, Java and COM ones refuse).
  `deploytool -build`/`-package` and the legacy apps' two-argument forms go through the same road;
  their one-argument forms, the target apps given a legacy `.prj`, and `open` of a `.prj` (a MATLAB
  project opens, a legacy one upgrades) upgrade and bring up the task's app window through the new
  `IScriptCompilerApps.OpenTask`; a session without windows stops at the upgrade, as `-batch` does.
- **Found on the way.** `fprintf` to a file now flushes after each write, so a reader sees the text
  while the file stays open (R2025b's deployment script builds from its help text file before
  closing it, and JGraph's build read nothing and then refused to write an empty resource); the
  build reads the help file with sharing. Stage K20's `DeploymentScript` writes the help text with
  `\n` escapes, the sample files as `SampleGenerationFiles`, and R2025b's comment in place of
  `LibraryVersion` for a C++ library on the MATLAB Data API.

## Measured

- `run-k21.ps1`: 661 of 885 keys agree, 224 recorded divergences, 0 unexplained. The divergences
  are the recorded kinds: the catalog texts, `exist`, `nargin` and `nargout` of p-coded functions,
  what a build prints and leaves behind (JGraph's compiler's lines and files, stages K6-K18), the
  Java, COM, web app and Excel client kinds, the Excel add-in's build (stage K17), the no-runtime
  and packaged-runtime packages, R2025b's packager crash on an empty dependency set, the upgraded
  project's definition-file count, the K20 script's installer files, the root's spelling under
  `jgraph -batch`, and the upgrade values above.
- `tools/runtime-checks/run-k21window.ps1`: all checks pass (a legacy standalone project given to
  `deploytool` in JGraph.Application: the file renamed `.bak`, the folder a project, the task's
  window up with the legacy main file and author, the code view over the legacy folders, the build
  ending in "Process completed successfully" with the program under the legacy `for_testing`
  folder); the picture reviewed.
- `LegacyProjectTests` (4: the reader and its refusals, the scripts of eight kinds against the
  recorded text over samples written as the probe writes them, the upgrade's project, task, labels,
  warnings and repeat, and the session's commands with a real build), `ProjectTests` (4) and
  `CompilerAppsTests` (7) pass; gate: the Deploy, ownership, compiler, catalog, output-count,
  completion and Production Server tests, 367.

## Divergences

- **The Java, COM, web app and Excel client kinds** are read and their scripts written, but
  `-build`, `-package` and the window refuse them, and the upgrade makes a bare task of their kind
  with no labels or properties (decisions KD1 and KD2).
- **An Excel add-in project** is read, scripted and upgraded but not built until stage K17.
- **A no-runtime project** packages an installer without a runtime under `param.no.mcr.name`
  (R2025b names it by the packaged runtime's name, downloads the runtime, and its `-package` wants
  the runtime installer); a packaged-runtime project carries the JGraph Runtime from the checkout's
  build where R2025b wants the MATLAB Runtime installer it cannot find.
- **`-package` of a library or package with an empty dependency set** packages (R2025b's packager
  fails on its own string).
- **The upgrade keeps** the legacy prompt flag's meaning, the example text and a C++ library's
  version, leaves a strong name key file that does not exist as a warning, makes a .NET task with
  `Class1` for a project without classes (R2025b fails on its own string), takes an `.mlapp` main
  file as it is (R2025b reads the app's name out of the file), and writes JGraph's definition
  files (their count and uuids differ while the nodes agree).
- **An Excel add-in project without a class** is refused as `invalidClassDefinition` by
  `createDeploymentScript` (R2025b fails on its own string).
- **`${MATLAB_ROOT}`** is JGraph's root, so a sorted list places such a file elsewhere.
- **A warning** is printed on one line where R2025b wraps it; what a build prints and leaves is
  JGraph's compiler's; the catalogs read through `message()` are not answered (ADR 0274); `exist`,
  `nargin` and `nargout` of R2025b's p-coded functions are JGraph's built-ins' (ADR 0271).
- **Closing a project** goes back to the folder before it (ADR 0275).

## Still open

Open item 218.

## Consequences

A legacy `.prj` is read, scripted, built, packaged and upgraded as R2025b does it, so a user's old
project goes into stage K20's projects and stage K19's windows without a step by hand, and
`createDeploymentScript` is the measured script for every kind. Stage K22 (the guide, the
acceptance set and the gate) is ready; stage K17 (the Excel add-in's build) still needs its
research.
