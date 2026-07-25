# ADR 0034 — Application theming

## Status

Accepted (M32, 2026-07-25).

## Context

JGraph had no styling at all. `App.xaml` was five lines with an empty `<Application.Resources/>`, and
every window inherited whatever Aero2 painted. The application had become an IDE over M30 and M31 —
a shell, a docked workspace, a command window — and an IDE that cannot be dark is an IDE people
squint at.

There was already a thing called a theme, and it is not this one. `JGraph.Core.Drawing.ITheme` is
**plot ink**: it mutates `FigureModel`, it is what a plugin extends, and it is serialized into
`.graph` files and baked into exported images. Nothing in this ADR touches it.

## Decision

### Two dictionaries, one swappable slot

`JGraph.Controls/Themes/` holds `ThemeKeys.cs`, `Light.xaml`, `Dark.xaml` and `Shared.xaml`. The
dictionaries live in **Controls** because Controls' own XAML has to reference the keys and cannot
reference the application; the manager lives in `JGraph.Application/Theming/` because only the
application owns `Application.Resources`. Nothing enters a net8.0 library — a theme is WPF, and
`jgraph.exe` must stay headless.

`Light.xaml` and `Dark.xaml` are **brushes and typography only**. `Shared.xaml` holds the implicit
`Style`s and is merged once, never swapped. That split is what makes the swap safe: a theme change
replaces values, so no control can lose its template halfway through.

`ThemeManager.Apply(id)` replaces **exactly one** entry in
`Application.Resources.MergedDictionaries`, located by the `JG.Theme.Id` sentinel each theme carries.
The obvious alternative — clear and rebuild — is wrong twice: it flashes every window as controls
fall back to system colours mid-rebuild, and it drops `Shared.xaml` along with the theme.

Every consumer uses **`DynamicResource`**. A `StaticResource` is resolved when the XAML loads and
would bake the startup theme in for the life of the process. This is the single rule most likely to
be broken by a later edit, and its symptom — one control that does not follow the swap — is easy to
miss.

`SettingsService.Changed` already fires on every `Save`, so subscribing to it in `App.OnStartup` is
the entire live-switch mechanism. Re-applying the theme already in force is a no-op.

### `Foreground` is set on containers, never on `TextBlock`

An implicit `Style TargetType="TextBlock"` setting `Foreground` is the obvious way to colour text and
it is a bug. `TextBlock.Foreground` inherits, so a `Window` or a `ListViewItem` setting it already
reaches every label inside — but an implicit style *beats* that inheritance. The result is body text
painted over a selected row's highlight: dark grey on dark blue, unreadable, and only on the row the
user just clicked. There is no implicit `TextBlock` style in `Shared.xaml`, deliberately.

Popups are the exception that needs care: `ContextMenu`, `MenuItem`, `ComboBoxItem` and `ToolTip` all
set `Foreground` explicitly, because inheritance through a `Popup` is not something to rely on.

### Replacement templates, not just Background setters

Most of `Shared.xaml` is `ControlTemplate`s. Setting `Background` on a `Button` is not enough: Aero2's
template hard-codes `#FFBEE6FD` for hover and `#FFC4E5F6` for pressed, so a dark toolbar button
flashes light blue under the pointer. The same is true of `ScrollBar` (a bright grey track glued to a
dark pane), `ComboBox`, `CheckBox`/`RadioButton` (gradient bullet chrome), `TreeViewItem` (a
near-black expander glyph), `ListViewItem` (a gradient selection rectangle drawn in a layer that
ignores `Background` entirely), `Menu`, `ToolBar` and the grid headers.

The templates keep Aero2's geometry — paddings, the 17px scroll-bar metric, the 13px bullet — and
change only where colours come from, so nothing reflows.

`TabControl`/`TabItem` are **not** styled: nothing in JGraph uses them. AvalonDock draws the document
tabs.

### The docking frame gets its own theme

AvalonDock paints its chrome from its own dictionaries and does not read our keys, so the only way to
make the frame agree with the panes is to hand it the matching theme of its own:
`DockingManager.Theme = Vs2013DarkTheme | Vs2013LightTheme`. The package was already referenced and
never applied.

The switch is driven by a `DockThemeIsDark` dependency property on `ScriptWorkspaceWindow` bound —
via `SetResourceReference` — to the theme's own `JG.Theme.IsDark` flag, rather than pushed by
`ThemeManager`. That keeps the window free of a dependency on the manager and makes the live switch
work with no wiring. Reassigning `Theme` re-templates every live pane, which is why it happens in one
place and only one place.

The dark palette follows Visual Studio's, which is both what these users' eyes are calibrated to and
what Vs2013Dark paints — so the frame and the panes agree instead of fighting.

### Syntax highlighting: two palettes we own, a contrast rule for the rest

The JGS and MATLAB `.xshd` definitions turned out to already carry **Visual Studio dark** colours —
`#569CD6` keywords, `#57A64A` comments — which had been quietly poor on the white editor since M12.
So this is not "add a dark variant"; it is "there was one palette and it was the wrong one half the
time". `SyntaxPalette` now has `Light` and `Dark`, and each definition is registered twice, looked up
by name.

C# and Python come from AvalonEdit and are tuned for white — their keyword blue is `#0000FF`, which
on `#1E1E1E` is a colour you can see but cannot read. `SyntaxThemes.EnsureReadable` fixes those **by
rule rather than by a hand-maintained colour table**: any named colour failing WCAG AA (4.5:1)
against the editor background is blended toward the background's opposite until it passes, keeping
its hue. Originals are cached, so switching back restores exactly what AvalonEdit shipped, and the
adjustment cannot accumulate across swaps.

### Static brush fields had to go

`BreakpointMargin` held five `static readonly Brush`/`Pen` fields, read once and never again. It is a
`FrameworkElement`, so they became dependency properties with `AffectsRender` and a
`SetResourceReference` in the constructor — a live `DynamicResource` binding for free, repainting the
gutter with nobody having to remember to invalidate it.

`CurrentLineRenderer` is an `IBackgroundRenderer`, **not** a `FrameworkElement`, so it has no resource
lookup at all. `ScriptEditorControl` owns a themed `CurrentLineBrush` property and pushes the value
in. The same pattern carries `SyntaxIsDark`.

### App chrome and figure ink stay separate

`LinkFigureThemeToAppTheme` defaults **off**. Silently darkening an exported plot because the IDE is
dark would be a genuine surprise, and the colours end up inside `.graph` files and PNGs that leave
the machine. When it is on it affects **new** figures only — never one already open, never one loaded
from a file, which carries its own theme.

### Settings are additive, with no version bump

`UserSettingsDto` gains `AppTheme` (string?) and `LinkFigureThemeToAppTheme` (bool).
`UserSettingsFormat.CurrentVersion` stays at 1. `Deserialize` gates on `FormatVersion <=
CurrentVersion` and absent members take their initializers, so an old file loads correctly. Bumping
would make new files unreadable by an older build and silently reset *every* preference on a
downgrade. This is the kind of thing that gets "corrected" later, so it is written down and tested.

### Themes are a fixed built-in set

`IAppThemeCatalog` exists so the door stays open, but there are two themes and they ship with the
app. A plugin-supplied theme means loading third-party BAML straight into `Application.Resources`
with no validation story; the key set is still moving; and `JGraph.Plugins` targets net8.0, so
extending `IPlugin` with a theme would force `net8.0-windows` onto the one contract that keeps
`jgraph.exe` headless.

## Consequences

- **A new key must be added to both dictionaries.** The parity test enforces it. Without it a missing
  key is invisible until someone switches themes on that exact screen.
- **The dark palette clears WCAG AA on every text-on-surface pair.** `#9D9D9D` on `#2D2D30`, the worst
  pair, is 5.5:1. The semantic colours were lightened for it: the light theme's `#C0392B` error red on
  a dark surface is 3.1:1, which fails AA for the one colour that must never be missed.
- **OS-rendered dialogs stay light.** `OpenFileDialog`, `MessageBox` and the folder picker are drawn by
  Windows and follow the OS theme, not ours. Accepted.
- **Some colours are deliberately not themed** — say so before someone "fixes" them: `OverlayRenderer`
  (selection handles inside the figure canvas), `SampleFigureFactory` and `PropertyRowViewModel.Palette`
  (`JGraph.Core.Drawing.Colors` — plot data and the colour picker's swatches), `HexBrushConverter`,
  and the swatch fills in `PropertyInspectorControl`. Those render the user's chosen figure colour, i.e.
  data. The swatch *outlines* stay a hard-coded mid grey for a related reason: it is the one value that
  keeps a white swatch visible on light and a black one visible on dark.
- **The splash window is theme-independent.** It is brand artwork with its own gradient, and it is on
  screen before a theme could sensibly be applied anyway.

## Alternatives considered

- **Clear and rebuild `MergedDictionaries` on every swap.** Flickers, and drops `Shared.xaml`.
- **Remembering the theme's index instead of a sentinel key.** Any later edit to `App.xaml` silently
  invalidates it.
- **An implicit `TextBlock` style carrying `Foreground`.** See above — it breaks selected rows.
- **A hand-maintained colour table for AvalonEdit's C#/Python definitions.** Two dozen named colours
  per language, drifting with every package upgrade. The contrast rule is shorter and self-maintaining.
- **Cloning AvalonEdit's definitions per theme** instead of adjusting the shared ones in place. Its
  definitions are not cloneable and the built-in XSHD resources are not public. Adjusting in place is
  also *correct*: the theme is application-wide, so every editor should follow.
- **Deriving the figure theme from the app theme by default.** Rejected; see above.

## Testing

The theme-key parity test is the highest-value one and it runs in the standard gate: `JGraph.Controls`
is net8.0-windows and unreachable from the test project, but neither half of the contract needs WPF —
`ThemeKeys` is nothing but string constants (linked into the test assembly) and a theme dictionary is
plain XML to an `XDocument` (copied as content). It asserts that the two themes define identical key
sets, that both match `ThemeKeys.All` exactly in both directions, that each carries the `JG.Theme.Id`
sentinel, that `Shared.xaml` does *not*, and that no theme dictionary contains a `Style`.

Everything visual is manual, per the standing limitation — but it was checked, in both themes, by
capturing the live windows: the shell, the editor with syntax highlighting, the docked panes, and a
figure window (which correctly kept its light plot ink under a dark IDE).
