namespace JGraph.Controls.Themes;

/// <summary>
/// The resource keys every application theme must define — the contract between
/// <c>Themes/Light.xaml</c>, <c>Themes/Dark.xaml</c> and everything that styles itself.
/// </summary>
/// <remarks>
/// <para>
/// These are <em>application chrome</em>: window, panel, text, editor and grid colours. They are
/// deliberately separate from <c>JGraph.Core.Drawing.ITheme</c>, which is plot ink — it mutates the
/// figure model and is serialized into <c>.graph</c> files. Darkening the IDE must not darken an
/// exported plot.
/// </para>
/// <para>
/// Every consumer must reference these with <c>DynamicResource</c>. A <c>StaticResource</c> is
/// resolved once when the XAML loads and will not follow a theme swap.
/// </para>
/// <para>
/// A key added here must be added to <em>both</em> dictionaries; the theme-key parity test enforces
/// it, because a missing key is invisible until someone switches themes on that exact screen.
/// </para>
/// </remarks>
public static class ThemeKeys
{
    /// <summary>The sentinel every theme dictionary carries, holding its own theme id.</summary>
    /// <remarks>
    /// This is how <c>ThemeManager</c> finds the one merged dictionary to replace. Without it the
    /// only options are clearing and rebuilding the merged set — which flickers and drops
    /// <c>Shared.xaml</c> — or remembering an index, which any later edit to App.xaml invalidates.
    /// </remarks>
    public const string ThemeId = "JG.Theme.Id";

    /// <summary>The human-readable name of the theme, for diagnostics.</summary>
    public const string ThemeName = "JG.Theme.Name";

    /// <summary>Whether the theme is dark. Consumers that must branch (AvalonDock, syntax colours) read this.</summary>
    public const string ThemeIsDark = "JG.Theme.IsDark";

    // ---- Surfaces -------------------------------------------------------------------------

    /// <summary>A top-level window's background.</summary>
    public const string Window = "JG.Brush.Window";

    /// <summary>A content surface that holds data: list, tree, grid and text-input backgrounds.</summary>
    public const string Surface = "JG.Brush.Surface";

    /// <summary>A chrome panel behind controls — headers, tool areas, dialog backgrounds.</summary>
    public const string Panel = "JG.Brush.Panel";

    /// <summary>A toolbar's background.</summary>
    public const string ToolBar = "JG.Brush.ToolBar";

    /// <summary>A status bar's background.</summary>
    public const string StatusBar = "JG.Brush.StatusBar";

    /// <summary>A menu bar's and drop-down's background.</summary>
    public const string Menu = "JG.Brush.Menu";

    /// <summary>A push-button's or combo box's face.</summary>
    public const string Control = "JG.Brush.Control";

    /// <summary>The outline of a button, combo box or text input.</summary>
    public const string ControlBorder = "JG.Brush.ControlBorder";

    // ---- Lines ----------------------------------------------------------------------------

    /// <summary>An ordinary container outline.</summary>
    public const string Border = "JG.Brush.Border";

    /// <summary>An outline that must read as a hard edge — a group box, a focused container.</summary>
    public const string BorderStrong = "JG.Brush.BorderStrong";

    /// <summary>A menu or toolbar separator line.</summary>
    public const string Separator = "JG.Brush.Separator";

    // ---- Text -----------------------------------------------------------------------------

    /// <summary>Body text.</summary>
    public const string Text = "JG.Brush.Text";

    /// <summary>Supporting text: hints, captions, counts.</summary>
    public const string TextSecondary = "JG.Brush.TextSecondary";

    /// <summary>Text on a disabled control.</summary>
    public const string TextDisabled = "JG.Brush.TextDisabled";

    /// <summary>Text drawn on top of <see cref="Accent"/>.</summary>
    public const string TextOnAccent = "JG.Brush.TextOnAccent";

    // ---- Interaction ----------------------------------------------------------------------

    /// <summary>The accent colour: primary buttons, progress, active indicators.</summary>
    public const string Accent = "JG.Brush.Accent";

    /// <summary>The background of a control under the pointer.</summary>
    public const string Hover = "JG.Brush.Hover";

    /// <summary>The background of a control being pressed.</summary>
    public const string Pressed = "JG.Brush.Pressed";

    /// <summary>The background of a selected list, tree or grid row.</summary>
    public const string Selection = "JG.Brush.Selection";

    /// <summary>Text on a selected row.</summary>
    public const string SelectionText = "JG.Brush.SelectionText";

    /// <summary>The keyboard-focus indicator.</summary>
    public const string FocusRing = "JG.Brush.FocusRing";

    // ---- Semantics ------------------------------------------------------------------------

    /// <summary>An error message or invalid-input marker.</summary>
    public const string Error = "JG.Brush.Error";

    /// <summary>A warning message.</summary>
    public const string Warning = "JG.Brush.Warning";

    /// <summary>A success message.</summary>
    public const string Success = "JG.Brush.Success";

    // ---- Script editor --------------------------------------------------------------------

    /// <summary>The code editor's background.</summary>
    public const string EditorBackground = "JG.Brush.EditorBackground";

    /// <summary>The code editor's default text colour.</summary>
    public const string EditorForeground = "JG.Brush.EditorForeground";

    /// <summary>The line-number gutter's text colour.</summary>
    public const string LineNumber = "JG.Brush.LineNumber";

    /// <summary>The band drawn behind the line the debugger is paused at. Semi-transparent by design.</summary>
    public const string CurrentLineHighlight = "JG.Brush.CurrentLineHighlight";

    /// <summary>A breakpoint dot.</summary>
    public const string BreakpointFill = "JG.Brush.BreakpointFill";

    /// <summary>The debug gutter's background.</summary>
    public const string BreakpointMargin = "JG.Brush.BreakpointMargin";

    /// <summary>The current-statement arrow's fill.</summary>
    public const string ExecutionArrow = "JG.Brush.ExecutionArrow";

    /// <summary>The current-statement arrow's outline.</summary>
    public const string ExecutionArrowBorder = "JG.Brush.ExecutionArrowBorder";

    /// <summary>The ghost arrow shown while dragging to set the next statement. Semi-transparent by design.</summary>
    public const string ExecutionArrowGhost = "JG.Brush.ExecutionArrowGhost";

    // ---- Data grid ------------------------------------------------------------------------

    /// <summary>A grid or list column header's background.</summary>
    public const string GridHeader = "JG.Brush.GridHeader";

    /// <summary>The background of an alternating grid row.</summary>
    public const string GridRowAlt = "JG.Brush.GridRowAlt";

    /// <summary>A grid's rules.</summary>
    public const string GridLine = "JG.Brush.GridLine";

    // ---- Typography -----------------------------------------------------------------------

    /// <summary>The UI font family.</summary>
    public const string FontUI = "JG.Font.UI";

    /// <summary>The fixed-pitch font family used by the editor and the console.</summary>
    public const string FontMono = "JG.Font.Mono";

    /// <summary>The size of supporting text.</summary>
    public const string FontSizeSmall = "JG.FontSize.Small";

    /// <summary>The size of body text.</summary>
    public const string FontSizeNormal = "JG.FontSize.Normal";

    /// <summary>The size of a heading.</summary>
    public const string FontSizeLarge = "JG.FontSize.Large";

    /// <summary>The size of code.</summary>
    public const string FontSizeCode = "JG.FontSize.Code";

    /// <summary>Every key a theme dictionary must define, in declaration order.</summary>
    /// <remarks>
    /// The parity test walks this rather than reflecting over the constants so that a key which is
    /// declared but forgotten here is itself a visible omission.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } =
    [
        ThemeId, ThemeName, ThemeIsDark,
        Window, Surface, Panel, ToolBar, StatusBar, Menu, Control, ControlBorder,
        Border, BorderStrong, Separator,
        Text, TextSecondary, TextDisabled, TextOnAccent,
        Accent, Hover, Pressed, Selection, SelectionText, FocusRing,
        Error, Warning, Success,
        EditorBackground, EditorForeground, LineNumber, CurrentLineHighlight,
        BreakpointFill, BreakpointMargin, ExecutionArrow, ExecutionArrowBorder, ExecutionArrowGhost,
        GridHeader, GridRowAlt, GridLine,
        FontUI, FontMono, FontSizeSmall, FontSizeNormal, FontSizeLarge, FontSizeCode,
    ];
}
