using JGraph.Scripting.Jgs;
using JGraph.Serialization.Settings;

namespace JGraph.Application.Services;

/// <summary>
/// The user's preferences as the rest of the application sees them — an application-layer type so
/// consumers depend on it rather than on the serialization DTO. Mutable and shared: the Options dialog
/// edits the instance held by <see cref="ISettingsService"/>, and readers (the JGS engine, the figure
/// view-model, the workspace dialogs) pick up the change on their next use.
/// </summary>
public sealed class UserSettings
{
    /// <summary>Whether a first assignment in JGS may omit <c>let</c>.</summary>
    public bool JgsOptionalLet { get; set; }

    /// <summary>The index of the first element in JGS: 0 or 1.</summary>
    public int JgsIndexBase { get; set; }

    /// <summary>The folder new open/save dialogs start in when no workspace is open, or null.</summary>
    public string? DefaultScriptDirectory { get; set; }

    /// <summary>The figure theme applied to new figure windows by name, or null for the first registered theme.</summary>
    public string? DefaultFigureTheme { get; set; }

    /// <summary>The full type names of discovered plugins the user has turned off.</summary>
    public List<string> DisabledPlugins { get; set; } = [];

    /// <summary>The language a blank New Script starts in, or null for MATLAB.</summary>
    public string? DefaultNewScriptLanguage { get; set; }

    /// <summary>The application chrome theme by id ("light", "dark"), or null for the shipped default.</summary>
    public string? AppTheme { get; set; }

    /// <summary>Whether a new figure starts on the figure theme matching the application theme.</summary>
    public bool LinkFigureThemeToAppTheme { get; set; }

    /// <summary>The reply-to email the user last typed into the bug-report dialog, or null.</summary>
    public string? BugReportReplyTo { get; set; }

    /// <summary>The JGS language options these settings imply (sanitized against a hand-edited index base).</summary>
    public JgsLanguageOptions ToJgsOptions() =>
        new JgsLanguageOptions(RequireLet: !JgsOptionalLet, IndexBase: JgsIndexBase).Sanitized();

    /// <summary>Whether the plugin with type full name <paramref name="pluginTypeName"/> is enabled.</summary>
    public bool IsPluginEnabled(string pluginTypeName) =>
        !DisabledPlugins.Contains(pluginTypeName, StringComparer.Ordinal);

    /// <summary>Builds a settings snapshot from a persisted DTO.</summary>
    public static UserSettings FromDto(UserSettingsDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new UserSettings
        {
            JgsOptionalLet = dto.JgsOptionalLet,
            JgsIndexBase = dto.JgsIndexBase,
            DefaultScriptDirectory = dto.DefaultScriptDirectory,
            DefaultFigureTheme = dto.DefaultFigureTheme,
            // A hand-edited or third-party-written settings file can carry an explicit null here,
            // which System.Text.Json assigns over the initialiser. Spreading that threw out of the
            // settings service's constructor, which runs before any window exists — the app died at
            // launch, every launch, with nothing on screen naming the file to delete.
            DisabledPlugins = dto.DisabledPlugins is { } disabled ? [.. disabled] : [],
            DefaultNewScriptLanguage = dto.DefaultNewScriptLanguage,
            AppTheme = dto.AppTheme,
            LinkFigureThemeToAppTheme = dto.LinkFigureThemeToAppTheme,
            BugReportReplyTo = dto.BugReportReplyTo,
        };
    }

    /// <summary>Projects these settings back to a DTO for persistence.</summary>
    public UserSettingsDto ToDto() => new()
    {
        JgsOptionalLet = JgsOptionalLet,
        JgsIndexBase = JgsIndexBase,
        DefaultScriptDirectory = DefaultScriptDirectory,
        DefaultFigureTheme = DefaultFigureTheme,
        DisabledPlugins = [.. DisabledPlugins],
        DefaultNewScriptLanguage = DefaultNewScriptLanguage,
        AppTheme = AppTheme,
        LinkFigureThemeToAppTheme = LinkFigureThemeToAppTheme,
        BugReportReplyTo = BugReportReplyTo,
    };

    /// <summary>A copy, so the Options dialog can edit a draft and discard it on Cancel.</summary>
    public UserSettings Clone() => FromDto(ToDto());
}
