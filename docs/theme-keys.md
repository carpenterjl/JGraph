# Theme keys

The contract between `JGraph.Controls/Themes/Light.xaml`, `Themes/Dark.xaml`, and everything that
colours itself. The authoritative list is `JGraph.Controls/Themes/ThemeKeys.cs`; this page says what
each key is *for*, which the constant names alone cannot.

Three rules, in order of how much damage breaking them does:

1. **Reference every key with `DynamicResource`.** A `StaticResource` is resolved once when the XAML
   loads and will not follow a theme swap. The symptom is one control that stays the old colour.
2. **A new key goes in both dictionaries.** The parity test
   (`tests/JGraph.Tests/Theming/ThemeKeyParityTests.cs`) enforces it, because a key present in Light
   and missing from Dark is invisible until someone switches themes on that exact screen.
3. **Set `Foreground` on containers, not on `TextBlock`.** `TextBlock.Foreground` inherits, so a
   `Window` or a `ListViewItem` already reaches every label inside it — and an implicit `TextBlock`
   style *beats* that inheritance, painting body text over a selected row's highlight.

Themes carry **values only**. Styles live in `Themes/Shared.xaml`, which is merged once and never
swapped, so a theme change can never take a control's template with it.

## Surfaces

| Key | Use |
| --- | --- |
| `JG.Brush.Window` | A top-level window's background. |
| `JG.Brush.Surface` | A surface holding data: list, tree, grid and text-input backgrounds. |
| `JG.Brush.Panel` | Chrome behind controls — headers, tool areas, dialog backgrounds. |
| `JG.Brush.ToolBar` | A toolbar's background. |
| `JG.Brush.StatusBar` | A status bar's background. |
| `JG.Brush.Menu` | A menu bar's and drop-down's background. |
| `JG.Brush.Control` | A push-button's or combo box's face. |
| `JG.Brush.ControlBorder` | The outline of a button, combo box or text input. |

## Lines

| Key | Use |
| --- | --- |
| `JG.Brush.Border` | An ordinary container outline. |
| `JG.Brush.BorderStrong` | An outline that must read as a hard edge. |
| `JG.Brush.Separator` | A menu or toolbar separator. |

## Text

| Key | Use |
| --- | --- |
| `JG.Brush.Text` | Body text. |
| `JG.Brush.TextSecondary` | Hints, captions, counts. |
| `JG.Brush.TextDisabled` | Text on a disabled control. |
| `JG.Brush.TextOnAccent` | Text drawn on `JG.Brush.Accent`. |

## Interaction

| Key | Use |
| --- | --- |
| `JG.Brush.Accent` | Primary buttons, progress, active indicators. |
| `JG.Brush.Hover` | A control under the pointer. |
| `JG.Brush.Pressed` | A control being pressed. |
| `JG.Brush.Selection` | A selected list, tree or grid row. |
| `JG.Brush.SelectionText` | Text on a selected row. |
| `JG.Brush.FocusRing` | The keyboard-focus indicator. |

## Semantics

| Key | Use |
| --- | --- |
| `JG.Brush.Error` | An error message or invalid-input marker. |
| `JG.Brush.Warning` | A warning message. |
| `JG.Brush.Success` | A success message. |

These are the keys most likely to be copied between themes unchanged, and the ones where that is
most wrong: the light theme's error red on a dark surface is 3.1:1, which fails WCAG AA for the one
colour that must never be missed.

## Script editor

| Key | Use |
| --- | --- |
| `JG.Brush.EditorBackground` | The code editor's background. |
| `JG.Brush.EditorForeground` | The editor's default text colour. |
| `JG.Brush.LineNumber` | The line-number gutter's text. |
| `JG.Brush.CurrentLineHighlight` | The band behind the paused line. **Semi-transparent by design** — the code underneath has to stay readable. |
| `JG.Brush.BreakpointFill` | A breakpoint dot. |
| `JG.Brush.BreakpointMargin` | The debug gutter's background. |
| `JG.Brush.ExecutionArrow` | The current-statement arrow's fill. |
| `JG.Brush.ExecutionArrowBorder` | Its outline. |
| `JG.Brush.ExecutionArrowGhost` | The ghost arrow shown while dragging to set the next statement. Semi-transparent by design. |

Token colours are **not** here: they come from `SyntaxPalette`, because an `.xshd` document needs
hex strings, not brushes.

## Data grid

| Key | Use |
| --- | --- |
| `JG.Brush.GridHeader` | A grid or list column header. |
| `JG.Brush.GridRowAlt` | An alternating row. |
| `JG.Brush.GridLine` | A grid's rules. |

## Typography

| Key | Type | Use |
| --- | --- | --- |
| `JG.Font.UI` | `FontFamily` | UI text. |
| `JG.Font.Mono` | `FontFamily` | The editor and the console. |
| `JG.FontSize.Small` | `Double` | Supporting text. |
| `JG.FontSize.Normal` | `Double` | Body text. |
| `JG.FontSize.Large` | `Double` | A heading. |
| `JG.FontSize.Code` | `Double` | Code. |

## Theme identity

| Key | Type | Use |
| --- | --- | --- |
| `JG.Theme.Id` | `String` | The sentinel `ThemeManager` finds the swappable dictionary by. **`Shared.xaml` must never define it.** |
| `JG.Theme.Name` | `String` | The display name. |
| `JG.Theme.IsDark` | `Boolean` | The one bit consumers that cannot read our brushes need — AvalonDock's chrome, the syntax palette. Both bind to it with `SetResourceReference`, which is why the live switch needs no wiring. |

## What is deliberately not themed

Say so before "fixing" one of these: they render the user's chosen **figure** colours, i.e. data,
not chrome.

- `OverlayRenderer` — selection handles drawn inside the figure canvas.
- `SampleFigureFactory` and `PropertyRowViewModel.Palette` — `JGraph.Core.Drawing.Colors`.
- `HexBrushConverter` and the swatch fills in `PropertyInspectorControl`.
- The swatch **outlines** stay a hard-coded mid grey: it is the one value that keeps a white swatch
  visible on light and a black one visible on dark.
- `SplashWindow` — brand artwork with its own gradient, on screen before a theme could be applied.
- OS-rendered dialogs (`OpenFileDialog`, `MessageBox`) follow the Windows theme, not ours.

See [ADR 0034](adr/0034-application-theming.md) for why the split exists.
