using JGraph.Scripting.Editing;

namespace JGraph.Controls.Scripting;

/// <summary>
/// What every editor shows alike (App Designer window plan W9c, R2025b's <c>matlab.editor.displaysettings</c>): line numbers, the
/// current line's highlight, datatips while editing, and the zoom - one factor for every file (<c>EnableSingleFileZoom</c> off). A host
/// loads them from its settings and keeps them when they change; each <see cref="ScriptEditorControl"/> follows <see cref="Changed"/>.
/// </summary>
public static class EditorDisplay
{
    /// <summary>The settings' names (R2025b's).</summary>
    public const string ShowLineNumbersSetting = "matlab.editor.displaysettings.ShowLineNumbers";
    public const string HighlightCurrentLineSetting = "matlab.editor.displaysettings.HighlightCurrentLine";
    public const string DatatipsSetting = "matlab.editor.displaysettings.DataTipsInEditMode";
    public const string ZoomFactorSetting = "matlab.editor.displaysettings.CurrentZoomFactor";

    private static bool _showLineNumbers = true;
    private static bool _highlightCurrentLine;
    private static bool _datatips;
    private static double _zoom = ZoomRules.Default;

    /// <summary>Raised after any of the four changed.</summary>
    public static event EventHandler? Changed;

    /// <summary>Line numbers in the margin (<c>ShowLineNumbers</c>, on by default).</summary>
    public static bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set => Set(ref _showLineNumbers, value);
    }

    /// <summary>The caret's line shaded (<c>HighlightCurrentLine</c>, off by default).</summary>
    public static bool HighlightCurrentLine
    {
        get => _highlightCurrentLine;
        set => Set(ref _highlightCurrentLine, value);
    }

    /// <summary>Datatips while editing, not only while paused in the debugger (<c>DataTipsInEditMode</c>, off by default).</summary>
    public static bool Datatips
    {
        get => _datatips;
        set => Set(ref _datatips, value);
    }

    /// <summary>The zoom factor, one of <see cref="ZoomRules.Factors"/>.</summary>
    public static double ZoomFactor
    {
        get => _zoom;
        set => Set(ref _zoom, ZoomRules.Factors.Contains(value) ? value : ZoomRules.Default);
    }

    /// <summary>Takes the four from a settings map by their names, those present; the rest stay as they are.</summary>
    public static void Load(IReadOnlyDictionary<string, string>? settings)
    {
        if (settings is null)
        {
            return;
        }

        if (settings.TryGetValue(ShowLineNumbersSetting, out string? numbers))
        {
            ShowLineNumbers = numbers == "true";
        }

        if (settings.TryGetValue(HighlightCurrentLineSetting, out string? highlight))
        {
            HighlightCurrentLine = highlight == "true";
        }

        if (settings.TryGetValue(DatatipsSetting, out string? tips))
        {
            Datatips = tips == "true";
        }

        if (settings.TryGetValue(ZoomFactorSetting, out string? zoom) && double.TryParse(zoom, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double factor))
        {
            ZoomFactor = factor;
        }
    }

    private static void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
