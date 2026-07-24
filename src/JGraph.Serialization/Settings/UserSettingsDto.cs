namespace JGraph.Serialization.Settings;

/// <summary>
/// The user's persisted preferences, shared by both hosts. A plain DTO — the live settings type in the
/// application stays free of serialization concerns. Every field is optional so an older or hand-edited
/// file loads without complaint, falling back to the shipped default for anything it omits.
/// </summary>
public sealed class UserSettingsDto
{
    /// <summary>The format tag (see <see cref="UserSettingsFormat.FormatTag"/>).</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>The schema version (see <see cref="UserSettingsFormat.CurrentVersion"/>).</summary>
    public int FormatVersion { get; set; }

    /// <summary>Whether a first assignment in JGS may omit <c>let</c>. Default false (the safety net stays).</summary>
    public bool JgsOptionalLet { get; set; }

    /// <summary>The index of the first element in JGS: 0 (the default, ADR 0028) or 1.</summary>
    public int JgsIndexBase { get; set; }

    /// <summary>The folder new open/save dialogs start in when no workspace is open, or null for the shell default.</summary>
    public string? DefaultScriptDirectory { get; set; }

    /// <summary>The figure theme applied to new figure windows by name, or null for the first registered theme.</summary>
    public string? DefaultFigureTheme { get; set; }

    /// <summary>The full type names of discovered plugins the user has turned off; empty means load everything.</summary>
    public List<string> DisabledPlugins { get; set; } = [];

    /// <summary>The language a blank New Script starts in ("JGS", "MATLAB", …), or null for JGS.</summary>
    public string? DefaultNewScriptLanguage { get; set; }
}
