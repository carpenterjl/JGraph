# ADR 0275 — MATLAB Projects and compiler tasks

## Status

Accepted, 2026-10-26. Compiler and SDK plan, stage K20 (decision KD6).

## Context

Since R2025a a compiler task lives in a MATLAB project: the apps of stage K19 (ADR 0274) edit a
task the project stores, the Compiler Task Manager lists and makes them, and
`compiler.project.getStandaloneDesktopAppConfiguration(proj, name)` and its siblings hand a script
the same task as a handle. Stage K19 held a task in its window alone; stage K20 gives it the project
to live in, and the minimum of projects that takes: `matlab.project.createProject`, `openProject`,
`currentProject` and their siblings, a project's files, labels, path, startup and shutdown files
and shortcuts, and the definition files on disk.

**What R2025b does**, measured by `record-k20.ps1` (`probe_k20_projects` under `matlab -batch
-noFigureWindows`; `recordings/k20_projects.out.txt`, 1,844 keys), plus an exploratory run and a
hash search:

- **The definition files.** A project is a `.prj` stub (`<MATLABProject xmlns=".../MATLABProjectFile"/>`)
  beside `resources\project\Project.xml` (`<Info MetadataType="fixedPathV2"/>`), `rootp.xml`, and one
  `<id>p.xml`/`<id>d.xml` pair per node: the `p` file its location and type, the `d` file an `<Info>`
  element with its data; the root's nodes under `resources\project\root`, every other node's
  children under a folder named by its id. The id is the base64url SHA-1 of the node's location and
  type concatenated after its ancestors' (`RootFiles` is `qaw0eS1zuuY1ar9TdPn1GMfrjbQ`,
  `RootFileshello.mFile` is `NlS6DTIym__tSzNJvG8uNdsbVgE`), found by trying the concatenations
  against the recorded names; a root container (`Root/Files`, `Root/Categories`, `Root/EntryPoints`,
  `Root/ProjectPath`, `Extensions/Root`) has a `p` file alone. The nodes: `ProjectData/Info` with
  the name; `Information/Extension` under `Extensions/Root` with the description; `Root/Categories`
  with a `Category` per category (`DataType`, `Name`, `ReadOnly`, `SingleValued`; the default
  `FileClassCategory` Classification with its seven read-only labels `artifact`...`test`) and a
  `Label` under each; `Root/Files` with a `File` node per file or folder by name, nested, a folder
  carrying a `1/DIR_SIGNIFIER` child, a file's labels as `<Category UUID><Label UUID/></Category>`
  in its `d` file and a label's data in a `Data/LabelData` child; `Root/ProjectPath` with a
  `Reference` per folder (`Ref`, `Type="Relative"`); `Root/EntryPoints` with an `EntryPoint` per
  startup file, shutdown file or shortcut (`File`, `GroupUUID`, `Icon`, `Name`, `Type` `Basic`,
  `StartUp` or `Shutdown`, `Visible`, and a `StartUpPrev` extension chaining the order); and the
  compiler tasks: a `MathWorksDeploymentConfigs/Files` node whose `configMapData` maps each task's
  name to a `MathWorksDeploymentConfig{uuid}` node, which carries `displayName`, `type`
  (`StandaloneDesktopApp`, `ProductionServerArchive`, `DotNETAssembly`, `PythonPackage`,
  `CppSharedLib`, `CSharedLib`, `ExcelAddIn`, `JavaPackage`, `COMComponent`, `WebAppArchive`,
  `Toolbox`), `uniqueId` and the model as JSON in `<Type>ConfigModel` (an mf0 document: `entries`
  of a build configuration and, where the target packages, the installer options, each with its
  scalar settings and a `ProjectLabel` reference per file role). The task's files are the project's
  files carrying the task's labels: a category `Deployment_Category_<uuid>` named
  `<CONFIGTYPE> - <task>` with a label per role (`Deployment_MainFile_<uuid>` "Main Function",
  `Deployment_AdditionalFiles_<uuid>` "Additional File", the exported functions, the images, the
  installer's files, the signatures file, the .NET strong key file), attached to the file nodes.
- **The API.** `createProject(path)` makes `blank_project.prj` in the folder and opens it;
  `Name=`, `Folder=` name it; an existing project refuses (`ProjectAlreadyExists`), a file refuses
  (`RootFolderIsFile`); the open project closes first (its shutdown files run); a nested project is
  allowed. `openProject` takes a root folder or a `.prj`: a missing one `FileDoesNotExist` with the
  open project kept; a folder or file that is not one `LoadFail` naming the folder for a `.prj` and
  the file otherwise, with the open project closed on the way. Opening makes the root the current
  folder, puts the project path on the search path, and runs the startup files; closing runs the
  shutdown files, takes the path off, and leaves the root. `currentProject` refuses with
  `NoProjectCurrentlyLoaded`; `matlab.project.currentProject` and `rootProject` answer an empty
  `matlab.project.Project`; two handles of one project compare unequal. A closed project's handle
  refuses everything but `isLoaded` with `CurrentProjectMismatch`. `addFile` takes a file or folder
  under the root (`FileNotInProjectRoot`, `FileDoesNotExist`, `FileIsMetadataError` for the
  definition files, `RootFolderNotAddable`), labels a MATLAB file Design, and answers `ProjectFile`
  handles; `findFiles` by Category and Label as strings or ProjectFile objects; `addPath` a folder
  under the root (`PathNotInProject`, `IsNotDirectory`); `addStartupFile`, `addShutdownFile`,
  `addShortcut` a file under the root (`FileDoesNotExistUnderRoot`); `createCategory` with its
  data type (`none`, `char`, `double`, `integer`, `logical`; `unrecognizedStringChoice`) and
  `single-valued`; `createLabel`, `findLabel`, `addLabel` with data checked against the type
  (`AddFileLabelInvalidData`); a default category or label refuses change
  (`CanNotModifyReadOnlyCategory`, `CanNotModifyReadOnlyLabel`); the `remove*` methods answer
  nothing and refuse nothing. `deleteProject` removes the definition files and the stub, keeps the
  rest, and refuses a loaded project (`DeleteFailLoaded`) or a folder that is not one.
- **The tasks.** Each getter takes a project and a name (`IsNotScalarProject`, `minrhs`,
  `TooManyInputs`; the name letters, digits, underscores and spaces, not ending in a space,
  `STRING_MUST_MATCH_PATTERN`, an empty or non-text one `STRING_CANNOT_BE_EMPTY`; a name of another
  type's task `GET_CONFIGURATION_FAILURE_UNKNOWN`), makes the task when missing with its category and
  labels, and answers a `compiler.project.<Kind>ConfigurationOptions` handle (a fresh one each call)
  whose properties, in a recorded order, are char rows, logicals, cell columns of full paths, the
  label structs (`Category`, `Name`), the output folders (`<task>\output\build`,
  `<task>\output\package`), `RuntimeInstallType` (`web`, `installer`, `NONE`) and `Interface`
  (`matlabdata`, `mwarray`), `SupportPackages` (a 0-by-1 struct) and a hidden `AnalysisIssues`.
  A set writes to the project at once, a closed project's included: a text property must be a char
  row or string scalar (`MATLAB:class:RequireString`; a name must not be empty), a logical a scalar
  (`RequireScalar`), a file list a cell or string array (`must_be_cell_array`) of files in the
  project (`FILE_NOT_IN_PROJECT`), an output folder non-empty and free of `< > : " * | ? &`, an
  enumeration its member in exact case (`mf0:messages:InvalidValue`), the read-only
  `ProfileDisplayName` and labels `SetProhibited`.
- **The script.** `compiler.internal.deployScriptGenerator.DeployScriptBuilder(cfg, root)` with
  `buildTarget` and `packageTarget` writes the deployment script of a task: `projectRoot = "..."`,
  the comment, `buildOpts = compiler.build.<Options>(fullfile(projectRoot, "main.m"))` (several
  files bracketed; a .NET task through `classmap = containers.Map; classmap("Class1") = ...`),
  `AdditionalFiles` when any, `AutoDetectDataFiles`, `OutputDir`, `ObfuscateArchive` and
  `Verbose = true` always, the target's own lines (a standalone's help text as a temporary file,
  `EmbedArchive`, `ExecutableIcon`, `ExecutableName`, `ExecutableSplashScreen`, `ExecutableVersion`,
  `TreatInputsAsNumeric`; an archive's `ArchiveName` and `FunctionSignatures`; a package's
  `PackageName`; a .NET assembly's name, version, `DebugBuild`, `EmbedArchive = "on"`, `Interface`,
  `EnableRemoting`, `FrameworkVersion`, `StrongNameKeyFile`; a C++ library's `DebugBuild`,
  `Interface`, `LibraryName`, `LibraryVersion`; a C library's without the version; an add-in's name,
  version, `ClassName`, `DebugBuild`, `EmbedArchive`, `GenerateVisualBasicFile`), the build, two
  blank lines, and the package: `ApplicationName`, the author lines, `DefaultInstallationDir` as the
  subfolder, `Description` with its lines joined by `newline`, the installer's images and name,
  `InstallationNotes`, `OutputDir`, `RuntimeDelivery = "none"` for `NONE`, `Summary`, `Verbose =
  true`, `Version` when not `1.0.0`, and `compiler.package.installer`. A task without its main file
  or exported functions refuses (`missingCriticalFile`); an archive refuses an installer
  (`invalidAdapterOption`).
- **Three slips in the generator**, each contradicting the option it writes: a task that keeps the
  Windows command prompt (`SuppressWindowsCmdPrompt` false) is built by
  `compiler.build.standaloneWindowsApplication`, and one that suppresses it by
  `standaloneApplication`; `RuntimeInstallType` `installer` writes no `RuntimeDelivery` line, so the
  installer fetches the runtime from the web; the task's `InstallerAdditionalFileList` is written
  nowhere. And the .NET class's `StrongNameKeyFileLabel` names the Sample File label.

## Decision

- **The project model, headless, in `JGraph.Deploy.Projects`.** `ProjectDefinitionFiles` reads and
  writes R2025b's `fixedPathV2` files with the recorded hash for every node's name (so a project
  JGraph makes opens in MATLAB and the other way round: the recording's fixed names are the test's
  oracle), attributes in ordinal order, four-space indentation, no `d` file for a root container,
  stale files deleted. `MatlabProject` is the node tree with the operations stage K20 needs (name,
  description, files and folders with the Design label, categories and labels with data, the path,
  the entry points, the tasks), saved after every change as MATLAB saves; its refusals are
  R2025b's identifiers and words (`ProjectException`). `CompilerTaskConfiguration` is a task over
  its node: the kind table (`CompilerTaskKinds`: the type names, classes, model attributes, category
  titles, labels and properties of every kind in the recorded order), the model's JSON read and
  written by key, the file-role properties as labels on the project's files, and the checks above.
  `ToTaskModel` and `ApplyTaskModel` carry a task to and from stage K19's `CompilerTaskModel`, which
  grows the fields a task has (`EmbedArchive`, `DebugBuild`, the help and example text, the .NET and
  add-in settings, the installer version, the project root and the task's name for the output
  folders).
- **The script is R2025b's, measured.** `DeploymentScript` writes the shape above line for line,
  and departs from the generator where the generator departs from the option (correctness outranks
  parity, ADR 0146's rule): a suppressed prompt builds a Windows application, `installer` writes
  `RuntimeDelivery = "installer"`, the installer's additional files are written. Stage K19's
  "only what differs from the defaults" shape is retired.
- **The session's projects, in `JgsBuiltins.Projects`.** One open project per session
  (`ProjectSession` on the host): `matlab.project.createProject`, `openProject`,
  `matlab.project.loadProject`, `currentProject`, `matlab.project.currentProject`, `rootProject`,
  `deleteProject`, `isFileInProject`, `isUnderProjectRoot` and `matlab.project.LabelDefinition` as
  recorded; opening changes the folder, adds the path folders and runs the startup files through
  `run`; closing runs the shutdown files, takes every project path folder off and goes back to the
  folder before. The handle objects ride the device-object machinery (`ProjectObject`,
  `ProjectFileObject`, `ProjectLabelObject`, `LabelDefinitionObject`, `CategoryObject`,
  `PathFolderObject`, `ShortcutObject`, `TaskConfigurationObject`, `ProjectEnumValue`) with R2025b's
  displays, property order, methods and refusals; they are views the session keeps alive
  (`DeletesWhenUnheld` false, a new hook on `DeviceObject`), a loaded project known by its root so
  `reload` keeps every handle good, and a fresh handle per `currentProject` call. The getters make
  JGraph's seven kinds; the Java, COM, web app and toolbox getters refuse as out of scope
  (decisions KD1 and KD2).
- **Found on the way:** `{obj.Prop.Name}` over a device array reached through an object's member
  or a call (`{proj.Categories.Name}`, `{findCategory(proj, 'x').LabelDefinitions.Name}`) now spreads
  one value per element, and an empty array gives none; `metaclass` of a device object answers its
  property list with each property's access and its superclass list; `num2cell` takes a struct
  array; `nargout` of a package function is read from the audit's rows with the file kind, as stage
  K15 set them.
- **The windows.** `CompilerTaskManagerWindow` is the Compiler Task Manager over the open project in
  its catalogs' words: the tasks by name and kind, Open (the K19 app window over the task, its title
  carrying the task's name, every edit written back through `ApplyTaskModel` at once), New Task with
  a name and a target type, Rename and Remove. An app command with a project open, or given a
  project's `.prj`, opens the project and the manager (`IScriptCompilerApps.OpenTaskManager`); with
  no project it opens stage K19's window as before. A legacy `<deployment-project>` file is refused
  until stage K21.

## Measured

- `run-k20.ps1`: 1,129 of 1,844 keys agree, 715 recorded divergences, 0 unexplained. The divergences
  are the recorded kinds: the definition files' uuids and the names they give (`tree.*`, `id.*`, 
  canonicalized), the catalog texts, R2025b's internals (`compiler.internal.*`, the enumeration
  classes, `exist`, `which`, `nargin` and the first help line of p-coded functions), the displays'
  details, the Java, COM, web app and toolbox tasks, the script's measured slips, the output folder
  R2025b calls not normalized, the folder after close, and the method listings.
- `tools/runtime-checks/run-k20window.ps1`: all checks pass (the manager's heading and list, the
  app window over the task, an author written to the definition file at once, the code view, the
  build ending in "Process completed successfully" with the program under the task's folder, a
  second task made in the manager); pictures of both windows reviewed.
- `ProjectTests` (4: the definition files' names and bytes against the recording, a project round
  trip with a task through its files, the task's script against R2025b's recorded text bar the slip,
  and the session's commands), `CompilerAppsTests` (7, the script expectations moved to the measured
  shape). Gate: the Deploy, ownership, compiler, catalog, output-count, completion and Production
  Server tests.

## Divergences

- **The definition files carry JGraph's uuids**, and the hashed names those give, so a step's file
  list differs from R2025b's in names and count while the nodes are the same; the fixed-name nodes
  are byte for byte R2025b's.
- **The Java, COM, web app and toolbox tasks are refused** by their getters, read from a project
  but not opened, where R2025b makes them (decisions KD1 and KD2); the categories a project lists
  count R2025b's tasks of those kinds.
- **The script writes the options' meaning where R2025b's generator slips**: a task that suppresses
  the Windows command prompt builds a `standaloneWindowsApplication` and one that keeps it a
  `standaloneApplication` (R2025b has them the other way round); `RuntimeInstallType` `installer` is
  written as `RuntimeDelivery = "installer"` (R2025b writes nothing and the installer fetches from
  the web); the installer's additional files are written as `packageOpts.AdditionalFiles` (R2025b
  drops them).
- **`StrongNameKeyFileLabel` names the Strong Key File label** the task's category carries, where
  R2025b's class names the Sample File label.
- **An output folder R2025b refuses as "not normalized"** (a relative folder of its own making
  aside) is taken when it is relative and free of the refused characters.
- **Closing a project goes back to the folder before it**; R2025b's folder after close is its own.
- **A project created with no folder** lands in JGraph's default folder (Documents\MATLAB\Projects,
  a numbered name when taken), not R2025b's.
- **`Dependencies` is a struct of Nodes and Edges**, not a digraph (open item 217).
- **The enumeration values are objects** (`deployment.compiler.model.MCRInstallType`,
  `MatlabAPIOptionType`, `matlab.project.DefinitionFiles`) whose `string` answers the member, where
  R2025b's derive from a numeric class; `enumeration` of them answers nothing.
- **The configuration classes list the handle's methods**, not R2025b's longer listing; a project's
  `methods` is R2025b's list, of which references, export, source control and `runChecks` refuse as
  not in JGraph (open item 217).
- **A file's labels come in the order they were attached**; R2025b's come in its storage order.
- **A label without data answers `[]`** as R2025b's does, but `{label.Data}` over several labels
  spreads; R2025b's internals (`compiler.internal.project.rename`, `remove`, `openEditor`, the
  deploy script builder, `getStandaloneDeploymentScript`) are not built: the manager renames and
  removes, `DeploymentScript` writes the script.
- **A task name given as a number or a cell** is refused with R2025b's words for an empty name,
  which is how R2025b reads it.
- **`exist`, `which`, `nargin` and the first help line** of R2025b's p-coded project functions are
  JGraph's built-ins' (ADR 0271, open item 194); the catalogs read through `message()` are not
  answered (ADR 0274).
- **A task's SupportPackages stays empty**: a struct with the right fields is refused as well as one
  with the wrong ones, since JGraph has no support packages (open item 217).

## Still open

Open item 217.

## Consequences

A compiler task has a home: a project MATLAB reads, written by the apps, the manager or the prompt,
with the same script either way. Stage K21 can read legacy `.prj` files into a project's task and
make `createDeploymentScript` from `DeploymentScript`. Projects beyond what tasks need (references,
export, source control, the dependency graph) wait for a plan of their own.
