namespace JGraph.Application.Theming;

/// <summary>
/// The built-in themes: Light (the baseline, identical to how JGraph looked before M32) and Dark.
/// </summary>
public sealed class AppThemeCatalog : IAppThemeCatalog
{
    private const string DictionaryBase = "pack://application:,,,/JGraph.Controls;component/Themes/";

    /// <summary>The id of the light theme — the default, and the fallback for an unknown id.</summary>
    public const string LightId = "light";

    /// <summary>The id of the dark theme.</summary>
    public const string DarkId = "dark";

    /// <inheritdoc />
    public IReadOnlyList<AppThemeDescriptor> Themes { get; } =
    [
        new AppThemeDescriptor(LightId, "Light", IsDark: false, new Uri(DictionaryBase + "Light.xaml")),
        new AppThemeDescriptor(DarkId, "Dark", IsDark: true, new Uri(DictionaryBase + "Dark.xaml")),
    ];

    /// <inheritdoc />
    public AppThemeDescriptor Default => Themes[0];

    /// <inheritdoc />
    public AppThemeDescriptor Resolve(string? id) =>
        Themes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
