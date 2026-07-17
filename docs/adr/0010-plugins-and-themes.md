# ADR 0010: Plugin discovery and the theme registry

- Status: Accepted
- Date: 2026-07-16

## Context

Milestone 9 makes JGraph extensible from the outside and ships the last of the built-in look-and-feel.
Two related needs: (1) a way for third parties — and the framework itself — to contribute named
resources (themes, colormaps) that the application surfaces without hard-coding them, and (2) two
additional presets, **Presentation** and **IEEE**, that differ from Light/Dark not just in color but in
typography (font family and sizes).

## Decision

1. **A new leaf project, `JGraph.Plugins`, owns discovery and registration.** It references
   `JGraph.Core` only — no objects, rendering, WPF, or serialization — so plugins stay portable and
   unit-testable. An `IPlugin` has a `Name`, a `Version`, and a single `Configure(IPluginRegistry)`
   method; it contributes data, never behavior tied to a backend. (When a future milestone lets plugins
   contribute plot types, the project takes on `JGraph.Objects`; today themes and colormaps live in
   `JGraph.Core.Drawing`, so Core suffices.)

2. **The registry is both the write side and the read side.** `PluginRegistry` implements the
   `IPluginRegistry` surface handed to plugins (`AddTheme`, `AddColormap`) and also exposes the
   catalog the app queries (`Themes`, `Colormaps`, `TryGetTheme`, `TryGetColormap`). Names are unique
   (case-insensitive) and registration order is preserved so menus are stable; a duplicate is a
   configuration error. `PluginRegistry.CreateDefault()` seeds the registry with the built-in
   `StandardLibraryPlugin` (Light/Dark/Presentation/IEEE themes and the standard colormaps), which
   doubles as the canonical example of a plugin.

3. **Discovery is reflection over assemblies, with an optional plugins directory.** `PluginLoader`
   finds concrete `IPlugin` types with a public parameterless constructor in a set of assemblies, and
   can load `*.dll` files dropped into a directory (via `AssemblyLoadContext.Default`) before scanning
   them. Discovery is deterministic (assemblies and types processed in a stable order); a missing
   directory means "no plugins"; a non-managed DLL is skipped; a failure to load or configure surfaces
   as a `PluginException` naming the offender. `PluginLoader.LoadDefault(pluginDirectory)` is the app's
   startup entry point: standard library first, then whatever the directory contributes.

4. **Themes carry typography, not just color.** `ITheme` gains a font family, per-role font sizes
   (figure title, axes title, axis label, tick label), and a bold-titles flag; `Theme.Apply` now sets
   these alongside colors. The Light and Dark presets specify typography identical to the model
   defaults, so applying them is unchanged. **Presentation** uses a large, bold, saturated look for
   slides; **IEEE** uses a compact Times New Roman face with faint gridlines and a conservative palette
   for two-column papers.

5. **The application resolves themes through the registry.** The DI container registers the
   `PluginRegistry` built by `PluginLoader.LoadDefault`. The figure view model exposes `AvailableThemes`
   and a settable `CurrentTheme`, and the toolbar's theme selector is a combo box bound to them — so a
   plugin that adds a theme appears in the menu with no application change. The demo gallery lists the
   registry's themes the same way.

## Consequences

- JGraph is now extensible without recompiling the app: drop a DLL exposing an `IPlugin` into the
  `plugins` folder and its themes/colormaps appear in the UI. The registration surface is intentionally
  small (themes and colormaps); new contribution kinds — plot types, export formats — are future
  `IPluginRegistry` methods plus a mapper arm, the same local-and-mechanical extension shape the
  serialization DTOs use.
- Loading plugin assemblies into the default `AssemblyLoadContext` means plugins share the host's
  dependency versions and cannot be unloaded; this is the right trade for a desktop app and keeps type
  identity simple (an `IPlugin` from a plugin DLL is the same `IPlugin` the host knows). Isolation or
  hot-unload would need a collectible load context and is deferred until a real need appears.
- Typography moved into the theme, so applying a theme now restyles fonts as well as colors. This is a
  deliberate, observable model mutation (consistent with how color theming already worked) and it
  round-trips through the `.graph` format because text styles already serialize their family, size, and
  weight.
