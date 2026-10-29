using System.Globalization;

namespace JGraph.Scripting.Designer;

/// <summary>A setting of App Designer's, under the name R2025b gives it.</summary>
/// <param name="Name">The full name (<c>matlab.appdesigner.designview.GridInterval</c>).</param>
/// <param name="Default">The factory value, as text (<c>true</c>, <c>10</c>, <c>icon_view</c>; a list as items joined by <c>|</c>).</param>
/// <param name="Listed">Whether <c>properties()</c> of its group lists it; the others the client reads by name.</param>
public sealed record DesignSettingDefinition(string Name, string Default, bool Listed);

/// <summary>
/// App Designer's settings (App Designer window plan, decision WO7, stage W2), kept by name as R2025b
/// keeps them in <c>settings().matlab.appdesigner</c>: the eight its groups list and the ones the
/// client reads by name, as stage W0 recorded them, with their factory values. JGraph has no MATLAB
/// <c>settings()</c> tree; the host persists <see cref="Values"/> in its own settings file and
/// shows the listed ones on an App Designer page of its Options dialog.
/// </summary>
public sealed class DesignSettings
{
    /// <summary>Save each app when App Designer loses the focus.</summary>
    public const string AutoSaveOnClickAway = "matlab.appdesigner.AutoSaveOnClickAway";

    /// <summary>The component browser lists labels with their components.</summary>
    public const string IncludeComponentLabels = "matlab.appdesigner.compbrowser.IncludeComponentLabels";

    /// <summary>The canvas grid's interval, in pixels (10 to 50).</summary>
    public const string GridInterval = "matlab.appdesigner.designview.GridInterval";

    /// <summary>Alignment hints while moving and resizing.</summary>
    public const string ShowAlignmentHints = "matlab.appdesigner.designview.ShowAlignmentHints";

    /// <summary>The canvas shows its grid.</summary>
    public const string ShowGrid = "matlab.appdesigner.designview.ShowGrid";

    /// <summary>Resizing hints (a component's size while resizing).</summary>
    public const string ShowResizingHints = "matlab.appdesigner.designview.ShowResizingHints";

    /// <summary>Moves and resizes snap to the grid.</summary>
    public const string SnapToGrid = "matlab.appdesigner.designview.SnapToGrid";

    /// <summary>How many recent files the Start Page lists (0 to 12).</summary>
    public const string MaxFileListSize = "matlab.appdesigner.history.MaxFileListSize";

    /// <summary>The recent files (<see cref="DesignRecentFiles"/>), newest first: each a JSON entry, joined by <c>|</c>.</summary>
    public const string FileList = "matlab.appdesigner.history.FileList";

    /// <summary>The window's title shows the app's folder.</summary>
    public const string ShowTitleFilePath = "matlab.appdesigner.window.ShowTitleFilePath";

    /// <summary>The window's title shows the app's file extension.</summary>
    public const string ShowTitleFileExtension = "matlab.appdesigner.window.ShowTitleFileExtension";

    /// <summary>The format new apps are written in: "m" (plain text) or "mlapp".</summary>
    public const string CreateNewAppsAs = "matlab.appdesigner.fileformat.CreateNewAppsAs";

    /// <summary>
    /// The Keyboard Shortcuts dialog's "Do not show again on session start", inverted: the client's
    /// view state <c>ShowShortcutsDialog</c>, written to the <c>confirmationdialogs</c> group.
    /// </summary>
    public const string ShowShortcutsDialog = "matlab.appdesigner.confirmationdialogs.ShowShortcutsDialog";

    /// <summary>Every setting, with its factory value: the listed eight, then the ones read by name.</summary>
    public static IReadOnlyList<DesignSettingDefinition> Definitions { get; } =
    [
        new(AutoSaveOnClickAway, "false", true),
        new(IncludeComponentLabels, "false", true),
        new(GridInterval, "10", true),
        new(ShowAlignmentHints, "true", true),
        new(ShowGrid, "false", true),
        new(ShowResizingHints, "false", true),
        new(SnapToGrid, "true", true),
        new(MaxFileListSize, "8", true),
        new("matlab.appdesigner.codeview.ShowAppMigrationTips", "true", false),
        new("matlab.appdesigner.codeview.ThemedReadonlyBackgroundColor", "", false),
        new("matlab.appdesigner.compatibility.ShowBackwardCompatibilityWarningTip", "true", false),
        new("matlab.appdesigner.componentlibrary.Favorites", "", false),
        new("matlab.appdesigner.componentlibrary.CategoryOrder", "", false),
        new("matlab.appdesigner.componentlibrary.ViewMode", "icon_view", false),
        new(CreateNewAppsAs, "m", false),
        new("matlab.appdesigner.fileformat.ShowNewPlainTextInfoMessage", "true", false),
        new(ShowTitleFilePath, "true", false),
        new(ShowTitleFileExtension, "true", false),
        new(ShowShortcutsDialog, "true", false),
        new(FileList, "", false),
    ];

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>Settings at their factory values, with any of <paramref name="saved"/> laid over them (unknown names are kept as they are).</summary>
    public DesignSettings(IReadOnlyDictionary<string, string>? saved = null)
    {
        foreach (DesignSettingDefinition definition in Definitions)
        {
            _values[definition.Name] = definition.Default;
        }

        if (saved is not null)
        {
            foreach ((string name, string value) in saved)
            {
                _values[name] = value;
            }
        }
    }

    /// <summary>Raised after a setting changed, with its name.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>Every setting's value by name, for the host to persist.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>A setting's value as text; an unknown name answers the empty text.</summary>
    public string Get(string name) => _values.TryGetValue(name, out string? value) ? value : string.Empty;

    /// <summary>A logical setting: <c>true</c> when its text is "true".</summary>
    public bool GetBool(string name) => string.Equals(Get(name), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>A whole-number setting, or <paramref name="fallback"/> when its text is not one.</summary>
    public int GetInt(string name, int fallback) =>
        int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    /// <summary>Sets a setting, raising <see cref="Changed"/> when its value changed.</summary>
    public void Set(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        if (_values.TryGetValue(name, out string? old) && old == value)
        {
            return;
        }

        _values[name] = value;
        Changed?.Invoke(this, name);
    }

    /// <summary>Sets a logical setting.</summary>
    public void Set(string name, bool value) => Set(name, value ? "true" : "false");

    /// <summary>Sets a whole-number setting.</summary>
    public void Set(string name, int value) => Set(name, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>The grid interval, kept within App Designer's limits (10 to 50).</summary>
    public double GridIntervalValue => DesignGeometry.GridIntervalWithin(GetInt(GridInterval, (int)DesignGeometry.GridInterval));

    /// <summary>The recent files as the setting keeps them, held to <see cref="MaxFileListSize"/>.</summary>
    public DesignRecentFiles RecentFiles() =>
        new(Get(FileList).Split('|', StringSplitOptions.RemoveEmptyEntries), Math.Clamp(GetInt(MaxFileListSize, 8), 0, 12));

    /// <summary>Keeps <paramref name="files"/> in the setting.</summary>
    public void SetRecentFiles(DesignRecentFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Set(FileList, string.Join('|', files.Entries()));
    }

    /// <summary>
    /// The window's title for the active app, as R2025b's <c>_getAppTitle</c> makes it: "App Designer -"
    /// and the app's full file name, or its name alone when <see cref="ShowTitleFilePath"/> is off,
    /// without its extension when <see cref="ShowTitleFileExtension"/> is off, and "*" when it has
    /// unsaved changes; just "App Designer" with no app. A new app (<paramref name="isNew"/>) shows
    /// its name and the extension it will be saved with (<c>app1.mlapp</c>), whatever the path setting.
    /// </summary>
    public string Title(string? fullFileName, bool dirty, bool isNew = false)
    {
        if (fullFileName is null)
        {
            return "App Designer";
        }

        string shown = fullFileName;
        if (!isNew && !GetBool(ShowTitleFilePath))
        {
            shown = shown[(shown.LastIndexOf(Path.DirectorySeparatorChar) + 1)..];
        }

        if (!GetBool(ShowTitleFileExtension) && shown.LastIndexOf('.') is int dot and >= 0)
        {
            shown = shown[..dot];
        }

        return "App Designer - " + shown + (dirty ? "*" : string.Empty);
    }
}
