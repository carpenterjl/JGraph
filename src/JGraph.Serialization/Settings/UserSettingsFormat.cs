using System.Text.Json;
using System.Text.Json.Serialization;

namespace JGraph.Serialization.Settings;

/// <summary>
/// Reads and writes the user's preferences as versioned JSON, following the same conventions as the
/// scripting-workspace state and the ".graph" document format. Loading is forgiving: corrupt or
/// newer-versioned settings return null so the app falls back to its shipped defaults rather than
/// failing at startup.
/// </summary>
public static class UserSettingsFormat
{
    /// <summary>The format tag stored in the settings document.</summary>
    public const string FormatTag = "jgraph-settings";

    /// <summary>The current schema version. Newer settings are ignored rather than misread.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serializes <paramref name="settings"/>, stamping the format tag and version.</summary>
    public static string Serialize(UserSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Format = FormatTag;
        settings.FormatVersion = CurrentVersion;
        return JsonSerializer.Serialize(settings, Options);
    }

    /// <summary>
    /// Parses persisted settings, or returns null when the JSON is malformed, mistagged, or written by
    /// a newer version — the caller uses defaults in that case.
    /// </summary>
    public static UserSettingsDto? Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            UserSettingsDto? settings = JsonSerializer.Deserialize<UserSettingsDto>(json, Options);
            return settings is not null
                && string.Equals(settings.Format, FormatTag, StringComparison.Ordinal)
                && settings.FormatVersion <= CurrentVersion
                ? settings
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
