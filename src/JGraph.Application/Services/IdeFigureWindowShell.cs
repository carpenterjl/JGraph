using JGraph.Runtime.Desktop.Services;

namespace JGraph.Application.Services;

/// <summary>
/// The application's half of a figure window (stage K5): its toolbar opens the script editor and
/// the Options dialog, and a new figure starts in the theme the user's settings prefer. With the
/// app-theme link on, the application's own theme id ("light"/"dark") names the figure theme, so a
/// new figure matches the IDE.
/// </summary>
public sealed class IdeFigureWindowShell : IFigureWindowShell
{
    private readonly ISettingsService _settings;
    private readonly IScriptingService _scripting;
    private readonly IOptionsService _options;

    /// <summary>Creates the shell over the application's services.</summary>
    public IdeFigureWindowShell(ISettingsService settings, IScriptingService scripting, IOptionsService options)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _scripting = scripting ?? throw new ArgumentNullException(nameof(scripting));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string? PreferredFigureTheme
    {
        get
        {
            UserSettings current = _settings.Current;
            return current.LinkFigureThemeToAppTheme ? current.AppTheme ?? "Light" : current.DefaultFigureTheme;
        }
    }

    /// <inheritdoc />
    public Action? OpenScript => _scripting.OpenEditor;

    /// <inheritdoc />
    public Action? ShowOptions => _options.ShowOptions;
}
