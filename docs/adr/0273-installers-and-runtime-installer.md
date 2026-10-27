# ADR 0273 — Installers: the JGraph Runtime MSI, `compiler.package.installer` and the runtime's functions

## Status

Accepted, 2026-10-26. Compiler and SDK plan, stage K18 (decisions KD6, KO7 and KO8).

## Context

A program built by `mcc -m`, a library built for .NET, C, C++ or Python, or an archive served by
`jgraph-mps` runs on the JGraph Runtime, which until now was a folder `publish-runtime.ps1` laid out
and `JGRAPH_RUNTIME_ROOT` named: nothing installed it, and the registry key the launchers read
(ADR 0260, 0262) was written by nobody. R2025b's end users install the MATLAB Runtime from its
installer, and its builders package an application with `compiler.package.installer`, whose
installer brings the runtime along (downloaded while installing, carried inside, or left to the
user), and `compiler.runtime.customInstaller`, a runtime-only installer. Stage K18 gives JGraph
both: the runtime's own MSI, and the packager that makes an application's installer around it.

**What R2025b does**, measured by `record-k18.ps1` (`probe_k18_package` under `matlab -batch` over
a standalone build, a windowed one and a Python package; `k18-payload.py` reading what each
installer R2025b wrote carries; `recordings/k18_package.out.txt`, `k18_payload.out.txt`):

- **`compiler.package.InstallerOptions`** is a value class of twenty-one public properties
  (RuntimeDelivery, InstallerSplash, InstallerIcon, InstallerLogo, PackageType, AdditionalFiles,
  AddRemoveProgramsIcon, AuthorName, AuthorEmail, AuthorCompany, Summary, Description,
  InstallationNotes, OptionalDependencies, Shortcut, Verbose, Version, ApplicationName,
  InstallerName, OutputDir, DefaultInstallationDir), ApplicationName required
  (`'ApplicationName' has no default and must be set.`), InstallerName `MyAppInstaller`, OutputDir
  `.\<name>installer` and DefaultInstallationDir `%ProgramFiles%\<name>` derived from the name with
  its bad folder characters and blanks made `_`; made from a build's results object with the
  build's name, a standalone build's ExecutableIcon as AddRemoveProgramsIcon and its program as
  Shortcut put before the names given; each set checked in its own words (set.*: cliName for the
  names, str2double part by part for Version so `1,2` and `007.08` pass and `1.0-beta` does not,
  mustBeMember in exact case for RuntimeDelivery, PackageType and OptionalDependencies, the images
  looked for as given, their bytes an image and no animated GIF, Shortcut a file or folder, the
  rest scalar text).
- **`compiler.package.installer`** takes a results object or a list of files with
  `requiredMCRProducts.txt` (warned about as going away; `buildresults.json` instead), the
  options' names or an `Options` object alone; refuses a missing file, two items of one name, an
  empty product list, a shortcut not among the packaged items, a deleted image, an output folder
  that is a file; warns when a program is packaged without a Shortcut; prints `Packaging files` and
  `Installer created at <path>` under Verbose; writes `<InstallerName>.exe`, R2025b's `setup.exe`
  with the payload as resources (the install bundle: `ApplicationInstallerManifest.xml` with the
  default installation folder, the application's name, `application\<shortcut>`, a GUID, the icon
  and logo, the version, the notes, the organization, the summary and the welcome panel text
  `<name> <version>` / summary / blank / description / author / email, the runtime products; the
  files under `application\`; the 48-pixel icon and the 290-by-112 logo), the install agent's URL,
  the 400-by-400 splash, the 256-to-16-pixel icon group; `PackageType` `zip` puts the program, its
  payload and a one-line readme in `<InstallerName>.zip`. With no MATLAB Runtime installer on the
  machine, `installer` and `none` are refused (couldNotFindRuntimeInstaller, needInstallAgent).
- **`compiler.runtime.customInstaller(name, results | files, ...)`** builds the runtime alone
  (`<name>.exe`, a manifest of nothing but the products), `none` refused, zip refused with web,
  three name-value pairs at most, the name as cliName checks it.
- **`mcrinstaller`** answers `'' [] [] [] ''` and prints how to download when none is on the
  machine, else the path, the version's three numbers and `WIN64`; **`compiler.runtime.version`**
  is 25; **`compiler.runtime.download`** fetches the installer (not run: gigabytes).

## Decision

- **The runtime MSI.** `installer/JGraph.Runtime.Installer` (WiX 6, like JGraph's own installer of
  ADR 0036), built by `installer/build-runtime-installer.ps1` over `publish-runtime.ps1`:
  `JGraph-Runtime-<version>.msi`, per machine, everything of the runtime layout under
  `Program Files\JGraph Runtime\<version>`, versions side by side as MATLAB Runtime's releases
  are: each version is a product of its own, its upgrade code the name-based (version 5) UUID of
  `JGraph Runtime <version>` in a namespace of JGraph's own, which the script and
  `JGraph.Deploy.RuntimeInstaller.UpgradeCode` derive alike and which is how a file is told to be
  the runtime's installer. It writes `HKLM\SOFTWARE\JGraph\JGraph Runtime\<version>\Location` (and
  `Release`), the key the launcher, `RuntimeLayout.Find` and a Python package's `__init__.py` read,
  and appends `runtime\win64` to the system PATH as the MATLAB Runtime installer appends its own.
  51 MB. Inspected through `MsiDatabase`, never run.
- **`compiler.package.installer` over `JGraph.Deploy.ApplicationInstaller`**, a WiX bundle:
  `<InstallerName>.exe` with the standard bootstrapper application over JGraph's own theme (the
  logo and the application's name above; a welcome page with R2025b's welcome panel text and, when
  there are notes, a box of them; the options page with the installation folder, which
  `DefaultInstallationDir` seeds: `%ProgramFiles%` and the other Windows folders as Burn's own, any
  other environment variable as Burn reads one, an absolute path as it is, a relative one under
  Program Files; then WiX's progress, success and failure pages). Its chain is the runtime MSI,
  `Permanent` and visible in Apps & Features, downloaded from the GitHub release of the matching
  JGraph version (KO8) for `web`, carried inside for `installer`, left out for `none`, then the
  application's own MSI: the files under `<folder>\application` as R2025b installs them, a Start
  menu shortcut to the Shortcut item, the Apps & Features icon, summary and contact, the product
  version the given version's first three numbers within what Windows Installer allows, an
  upgrade code derived from the application's name so a rebuild upgrades the install before it.
  `zip` wraps the program and a readme. The images are resized as R2025b's packager resizes them
  (the icon at the six sizes, the logo to 290 by 112, the splash to 400 by 400, as a bitmap for
  Burn's splash). `InstallerOptions` is R2025b's class, every set as recorded;
  `compiler.package.installer`'s checks run in R2025b's order and words (the exe-without-shortcut
  warning, the deprecation warning for `requiredMCRProducts.txt`, `buildresults.json` taken too).
- **`compiler.runtime.customInstaller`** is the same bundle with the runtime alone and no options
  page.
- **WiX comes with JGraph (KO7).** `installer/WixTools/WixTools.csproj` restores the three WiX
  packages (MS-RL) into the NuGet cache; `build-installer.ps1` ships the command line, the two
  extensions and the license under `toolbox\compiler\wix`, where `WixTool.Find` looks first, then
  `JGRAPH_WIX`, then the package cache (a source checkout). It runs on the .NET host this process
  runs on.
- **The runtime's functions.** `mcrinstaller` finds the runtime MSI of this JGraph version: the
  file `JGRAPH_RUNTIME_INSTALLER` names, the download folder (`%LOCALAPPDATA%\JGraph\runtime\win64`),
  JGraph's own `toolbox\compiler\deploy\win64`, a checkout's build; each counts only when its
  package properties are the runtime's. `compiler.runtime.download` fetches it from the release
  URL (`JGRAPH_RUNTIME_RELEASE_URL` overrides the base) into the download folder, checks it and
  names it, or names one already there. `compiler.runtime.version` is 25.
- **Dispatch sweep 30** for the five names the sweep can call; `compiler.runtime.download`'s rows
  are written by hand, since calling it in R2025b starts a download.

## Measured

`run-k18.ps1`, the same probe under `jgraph -batch` with JGraph's installers built by WiX from
the package cache and the checkout's runtime MSI, compared by `compare-k18.py`: 453 of 621 keys
agree, 168 are the divergences named below, none is unexplained. `InstallerTests` (6): the
upgrade codes against the script's, RFC 4122's own example, the runtime MSI's tables (its
registry key, PATH entry and marker file), the versions, folders and words as R2025b treats them,
the sources, and, with WiX, installers built in every delivery and read back: the bundle's
manifest (`wix burn extract`: the runtime's payload external with its download URL and size for
web, embedded for installer, absent for none; the application's embedded) and the application
MSI's tables (`msi.dll`: the product, version, publisher, comment and contact, the files, the
`application` folder, the shortcut, the icon), the zip's two entries, the runtime-only bundle.

## Divergences

- **The installer is a WiX bundle of JGraph's own**, not R2025b's `setup.exe` with its resources
  and install agent: its size, pages and inner files are JGraph's (the welcome text, the notes,
  the folder and the chain are R2025b's in substance); the zip holds the program and the readme,
  not R2025b's `data\` folder.
- **A `web` installer needs the runtime's MSI on this machine** for the hash Burn verifies the
  download with, so a build without one is refused (couldNotFindRuntimeInstaller) where R2025b
  builds; a `none` installer needs nothing, where R2025b needs the install agent from its runtime
  installer (needInstallAgent).
- **The messages name the JGraph Runtime** where R2025b's name the MATLAB Runtime (the refusals,
  `Packaging the JGraph Runtime`, mcrinstaller's line).
- **An additional file of a packaged file's name is refused** (noRepeats) where R2025b packages
  both (results.exe.additional).
- **R2025b's fileattrib warning is not raised** (`File user characters ignored on Windows.`, after
  every packaging call).
- **A warning prints its text alone**, without MATLAB's `> In` stack lines (open item 170).
- **The version in the bundle is the given version's first four numbers and in the MSI its first
  three**, within what Windows Installer allows, where R2025b writes the text as given.
- **An options object cannot be saved to a MAT-file** (its Verbose is a .NET value; R2025b's
  loadobj restores it; open item 215).
- **`exist` answers 5 for the built-ins** that are files in R2025b (ADR 0271, seen again).
- **Displays are JGraph's own layout** (open item 194): the options object, a properties list, a
  cell (an empty one as `{}`).
- **The MSI's product and upgrade codes are derived from the names**, where R2025b's manifest
  carries a fresh GUID per build.

## Still open

- Item 215: the download itself unmeasured (nothing is published), `compiler.runtime.installer`
  (an undocumented p-file) not built, the installer's own pages unseen (never run), the runtime MSI
  not in JGraph's developer install (a 51 MB download instead), OptionalDependencies and the product
  list carried but not read (one runtime), the Excel client's refusal (K17), an options object's
  save.

## Consequences

- An end user installs the JGraph Runtime once per version from its MSI, and every program and
  library built by JGraph's compiler finds it through the registry as R2025b's find the MATLAB
  Runtime; a builder packages an application with `compiler.package.installer` and hands out one
  installer program that brings the runtime.
- No installer is run here; the house rule stands. The bundle's behaviour while installing (the
  download, the chain, the pages) is Burn's, inspected through its manifest.
- The developer install grows by WiX's 13 MB; the runtime-only install does not carry it.
