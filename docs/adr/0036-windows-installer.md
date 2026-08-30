# ADR 0036 — Windows installer (MSI via WiX)

## Status

Accepted (M33).

## Context

An external application needs to invoke JGraph's command line, which means `jgraph.exe` has to
exist somewhere stable and be reachable from PATH. Until now JGraph only existed as build output.
The requirements: install the product, *ask* the user whether to put `jgraph` on PATH, update an
existing installation when the installer is re-run, and uninstall cleanly from Apps & Features.

The codebase was already installer-shaped. `GuiLauncher.Locate()` looks for
`JGraph.Application.exe` beside `jgraph.exe` first — the "deployed layout" — and every runtime
asset (examples, `python/jgraph_console.py`, the scripting guide) travels as a csproj content
item. So `dotnet publish` of the two executable projects into one folder *is* the product; the
installer's only real job is to put that folder somewhere and register it.

## Decision

**MSI, authored with WiX 6, built by `installer/build-installer.ps1`.**

- **MSI over MSIX.** MSIX cannot modify PATH (its closest feature is an app-execution alias),
  requires a code-signing certificate just to install, and requires signed packages to update.
  MSI supports all three requirements natively: an `Environment` table row for PATH, `MajorUpgrade`
  for re-run-to-update, and standard Add/Remove registration — with no certificate.
- **Per-machine scope.** `Program Files\JGraph`, the *system* PATH, one UAC prompt. A per-user
  install would hide `jgraph` from other accounts and from elevated processes — the external
  application invoking it is the whole point.
- **Framework-dependent publish.** The MSI stays ~10 MB and relies on the .NET 8 Desktop Runtime.
  There is deliberately **no launch condition** checking for it: MSI cannot wildcard-search
  `dotnet\shared\Microsoft.WindowsDesktop.App\8.*`, and the .NET apphost already shows its own
  "runtime missing" dialog with the correct download link on first launch. A Burn bundle that
  chains the runtime installer is the follow-on if this ever ships to machines we don't control.

### The pieces

`installer/` (top level, **not** in `JGraph.sln` — the WiX project consumes `installer/staging`,
which only exists after a publish, so it must never run on an ordinary solution build):

- `build-installer.ps1` — publishes `JGraph.Application` then `JGraph.Cli` into one staging
  folder (overlapping DLLs are the same net8.0 assemblies from the same tree), sanity-checks
  anchor files (`jgraph.exe`, `JGraph.Application.exe`, the Python console script, the guide, an
  example), then builds the wixproj with the version read from `Directory.Build.props`.
- `JGraph.Installer/Package.wxs` — the package. Files are harvested with a wildcard
  (`<Files Include="staging\**">`), not authored per file, so content added by future milestones
  ships automatically.
- `JGraph.Installer/PathDialog.wxs` — the PATH checkbox page.

### The decisions that will look wrong later

**The UpgradeCode is immutable.** `266EE39B-347D-4ED2-94A8-484A03A6A127` is the product's
permanent identity; it is how a re-run finds the existing installation to replace. Changing it
orphans every existing install as a second "JGraph" in Apps & Features.

**`AllowSameVersionUpgrades="yes"`, and ICE61 is suppressed.** The product version does not move
every milestone — it went 0.1.0 to 0.2.0 at M113 after sixty-one milestones of standing still — so a
re-built MSI of a given version must still replace an installed one of the same version. ICE61 is MSI
validation complaining about exactly that policy; it is the documented cost of the policy, not a
defect. ICE57 is also suppressed: an all-users Start Menu shortcut in a per-machine package trips
it regardless of authoring — it is a known false positive.

**The PATH choice is remembered with two registry components, not one value.** MSI checkboxes are
checked whenever their property has *any* value, so reading a stored `0` back into `ADDTOPATH`
would render as a checked box. Instead: the previous choice is searched into
`JGRAPH_PREVADDTOPATH`, and a `SetProperty` clears `ADDTOPATH` when the stored value is `"0"`.
Two transitive components (`PathEnvironment` writes `AddToPath=1` plus the `Environment` row;
`PathDeclined` writes `AddToPath=0`) persist the choice under `HKLM\SOFTWARE\JGraph`.
`Sequence="first"` on the `SetProperty` makes silent re-runs honour the remembered choice without
clobbering a fresh UI selection. `Permanent="no"` + `Part="last"` on the `Environment` row means
uninstall removes exactly the JGraph entry and MSI broadcasts `WM_SETTINGCHANGE` itself — new
consoles see the change, already-open ones do not.

**The custom dialog is inserted by publish-order, and its InstallDirDlg override carries no
condition.** For one control MSI runs every event whose condition holds, in `Order`, and the last
`NewDialog` wins — so publishing `PathOptionsDlg` at `Order="10"` reroutes the stock flow without
copying any stock dialog. There is no path-validity condition on that publish because WiX 6's
`InstallDirDlg` validates via the `CheckTargetPath` control event at Order 1, which aborts the
whole chain on a bad path; the old `WIXUI_INSTALLDIR_VALID` property is never set in v6, and
conditioning on it silently skips the page.

**The icon lives in `src/JGraph.Application/Assets/jgraph.ico`**, not in the installer: the
application owns its own branding (`<ApplicationIcon>` on both executables) and the installer
consumes it for the ARP entry and the Start Menu shortcut.

## Consequences

- Install, then `jgraph -batch "..."` works from any *new* command prompt when the PATH box was
  ticked. Re-running any equal-or-newer MSI updates in place; Apps & Features uninstalls, removing
  the PATH entry and the install folder.
- User data survives uninstall by design: `%AppData%\JGraph` (settings, workspace state) and the
  seeded `Documents\JGraph\Examples` are the user's files, not the product's.
- The MSI is unsigned; other machines will see a SmartScreen warning. Code signing, runtime
  bundling (Burn), and `.graph`/`.jgs` file associations are all explicitly out of scope and are
  natural later slices.
- First-run example seeding copies *from* `Program Files` (read-only) *to* Documents — the seeder
  was built for a read-only source (ADR 0033), so installing changes nothing there.
