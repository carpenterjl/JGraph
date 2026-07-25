using System.Text.Json;
using System.Text.Json.Serialization;

namespace JGraph.Serialization.Workspace;

/// <summary>
/// Reads and writes the persisted scripting-workspace state (last root, open files, breakpoints,
/// docking layout) as versioned JSON, following the same conventions as the ".graph" document format.
/// Loading is forgiving: corrupt or newer-versioned state returns null so the app falls back to a
/// fresh workspace instead of failing at startup.
/// </summary>
public static class ScriptWorkspaceStateFormat
{
    /// <summary>The format tag stored in the state document.</summary>
    public const string FormatTag = "jgraph-workspace";

    /// <summary>
    /// The current schema version. Newer state is ignored rather than misread. Adding an optional
    /// field does not bump this: unknown members are ignored on read and absent ones take their
    /// property initializers, so old state loads with the new fields defaulted. Bumping for an
    /// additive change would only make new files unreadable by an older build — silently discarding
    /// the user's session on a downgrade for no gain.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The pane arrangement generation written into new state (see
    /// <see cref="ScriptWorkspaceStateDto.LayoutSchema"/>).
    /// </summary>
    public const int CurrentLayoutSchema = 1;

    /// <summary>
    /// The oldest arrangement generation still worth restoring. A saved layout below this is
    /// discarded and the default one used instead.
    /// </summary>
    /// <remarks>
    /// Zero, because state written before the field existed describes the same five panes under the
    /// same content ids — nothing about it is wrong, so discarding it would throw away the user's
    /// arrangement for no reason. Raise this (together with
    /// <see cref="CurrentLayoutSchema"/>) only when a release rearranges panes in a way an older
    /// layout cannot express, where half-restoring is worse than starting clean.
    /// </remarks>
    public const int MinimumCompatibleLayoutSchema = 0;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serializes <paramref name="state"/>, stamping the format tag and version.</summary>
    public static string Serialize(ScriptWorkspaceStateDto state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Format = FormatTag;
        state.FormatVersion = CurrentVersion;
        return JsonSerializer.Serialize(state, Options);
    }

    /// <summary>
    /// Parses persisted state, or returns null when the JSON is malformed, mistagged, or written by a
    /// newer version — the caller starts fresh in that case.
    /// </summary>
    public static ScriptWorkspaceStateDto? Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            ScriptWorkspaceStateDto? state = JsonSerializer.Deserialize<ScriptWorkspaceStateDto>(json, Options);
            return state is not null
                && string.Equals(state.Format, FormatTag, StringComparison.Ordinal)
                && state.FormatVersion <= CurrentVersion
                ? state
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
