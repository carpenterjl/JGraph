# ADR 0032 — User settings and the Options dialog

## Status

Accepted (M29, 2026-07-24).

## Context

JGraph had no user preferences. The figure theme reset to Light every launch, every plugin found on
disk loaded unconditionally, new scripts always opened as JGS, and the JGS language was fixed — `let`
required, 0-based indexing — with no way to change it short of writing a `.m` file. The user asked for
a settings/profile system: optional `let`, a global index base, a default script directory, a UI theme,
loaded plugins, and a default new-script language.

The only persistence that existed was `WorkspaceStateService` — versioned JSON in
`%AppData%\JGraph\workspace.json`, forgiving on load, silent on IO failure. It is the pattern to copy.

## Decision

**A versioned `settings.json`, cloned from the workspace-state format.**
`UserSettingsDto`/`UserSettingsFormat` in `JGraph.Serialization` mirror the workspace format exactly:
tag `jgraph-settings`, `CurrentVersion 1`, every field optional, and a malformed, foreign, or
newer-versioned file loads as null so the app falls back to defaults rather than failing at startup.
`SettingsService` (in `JGraph.Application`, over `%AppData%\JGraph\settings.json`) holds the live
`UserSettings` and persists changes; all IO failures degrade to defaults, exactly as workspace state
does.

**The JGS options flow through a provider, not a restart.** `JgsScriptEngine` takes a
`Func<JgsLanguageOptions>` and reads it on each run, so a change in the Options dialog applies to the
next run with no restart. `JgsLanguageOptions.Sanitized()` clamps a hand-edited index base back to 0,
keeping the interpreter out of a state no rule covers. Only these two options are adjustable — the rest
of the language is fixed — and **MATLAB (`.m`) ignores them entirely** (ADR 0031): a `.m` file behaves
the same however JGraph is configured.

**Plugins are filtered at load, by type name.** `PluginLoader.LoadDefault` gained an `include`
predicate applied before `registry.Apply`; a plugin's identity is its type full name, which the
settings list disables. The built-in standard library is applied inside `CreateDefault`, so its themes
and colormaps can never be turned off. Because a loaded assembly cannot be unloaded, a plugin change
takes effect on the next launch — the dialog says so.

**Both hosts read the file.** `App.ConfigureServices` loads settings first (the plugin filter and the
JGS engine both need them), and the headless CLI reads the same `settings.json` directly via
`UserSettingsFormat` — it has no DI container, but it already references `JGraph.Serialization`. A batch
run therefore honours the user's plugin and JGS-option choices just as the interactive app does.

**The Options dialog is a thin view over a UI-free draft.** `OptionsViewModel` holds the editable
copy — language options, default directory/theme/language, and a plugin checklist discovered from the
plugins folder — and `Apply()` commits it through the settings service. Nothing is saved until OK, so
Cancel discards every edit. `OptionsWindow` reflects the draft into controls and back. It is reachable
from an **Options…** button on the FigureWindow toolbar and an **Options…** item in the script
workspace's View menu, both routed through `IOptionsService` so no view-model news up a window.

## Deferred: application chrome theming

The user's list included "UI theme settings". Deferred, and here is why: `App.xaml` has no resource
dictionaries and no app-level styling of any kind — every window and control uses default WPF chrome.
A light/dark *application* theme is a milestone in its own right (restyling every surface), not a
setting to bolt on. The **figure** theme is persisted now (that infrastructure exists via the plugin
registry); the window chrome is left for later. The settings file has no field for it yet, so adding
one later is a forward-compatible, no-version-bump change.

## Alternatives considered

- **Named profiles** (`-profile matlab-like`). Out of scope for this milestone — a single settings
  file covers the ask; profiles can layer on later without changing the file format.
- **A `startup.jgs`** that runs on every launch (MATLAB's `startup.m`). Out of scope; the `-r` flag and
  a future startup-script setting can cover it.
- **Applying JGS options as a process-wide static** (like `JgsPacking.Enabled`). Rejected: the options
  must be data the engine reads, mirroring ADR 0031's dialect decision, so a future per-document
  override stays possible.
- **Live plugin reload.** Not possible — `AssemblyLoadContext.Default` cannot unload — so the change is
  deferred to the next launch and the dialog says so.

## Consequences

- `SettingsService`, `UserSettings`, `OptionsService`, and `OptionsViewModel` live in
  `JGraph.Application` (net8.0-windows) and so are not reachable from the net8.0 test project — the same
  boundary that keeps `WorkspaceStateService` and M26's `ElementMenuBuilder` untested there. The logic
  underneath **is** tested: the DTO round-trip and forgiving load (`UserSettingsFormatTests`), the JGS
  options behaviour end to end (`JgsLanguageOptionsTests`), and the plugin filter
  (`PluginLoaderTests`).
- `FigureViewModel` and `ScriptingService` gained an `ISettingsService`/`IOptionsService` dependency;
  both are DI singletons, so the graph stays complete.
- The default figure theme, default new-script language, and dialog directories now read from settings,
  falling back to their previous hard-coded defaults when unset.
