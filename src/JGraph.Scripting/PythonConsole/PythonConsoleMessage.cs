using System.Text.Json;
using System.Text.Json.Serialization;

namespace JGraph.Scripting.PythonConsole;

/// <summary>
/// One newline-delimited JSON message on the wire between the host and the Python console child
/// process. Both directions share a shape: the fields a given message uses are set and the rest are
/// absent, which keeps the codec a single type instead of a discriminated hierarchy that
/// <c>System.Text.Json</c> would need custom converters for.
/// </summary>
/// <remarks>
/// Unknown fields are ignored on both sides, so the protocol can grow without a version handshake —
/// the same additive rule the persisted formats follow. Field names are lower-case to read naturally
/// from Python.
/// </remarks>
internal sealed record PythonConsoleMessage
{
    /// <summary>Correlates an <c>exec</c> with the output and <c>done</c> it produces.</summary>
    [JsonPropertyName("id")]
    public int Id { get; init; }

    /// <summary>Host → child verb: <c>exec</c>, <c>vars</c>, or <c>shutdown</c>.</summary>
    [JsonPropertyName("op")]
    public string? Op { get; init; }

    /// <summary>Child → host kind: <c>out</c>, <c>err</c>, <c>done</c>, <c>call</c>, <c>vars</c>, or
    /// <c>ready</c>. Also <c>return</c> for the host's reply to a <c>call</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The source to execute, for <c>op: exec</c>.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /// <summary>Streamed output, for <c>type: out</c> and <c>type: err</c>.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Whether the statement succeeded, for <c>type: done</c>.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>The error headline, for a failed <c>done</c> or a failed <c>return</c>.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>The 1-based line the error was raised on, or 0 when unknown.</summary>
    [JsonPropertyName("line")]
    public int Line { get; init; }

    /// <summary>The exit code the statement asked for with <c>exit()</c>, or null.</summary>
    [JsonPropertyName("exit")]
    public int? Exit { get; init; }

    /// <summary>Correlates a <c>call</c> with its <c>return</c>. Independent of <see cref="Id"/>.</summary>
    [JsonPropertyName("seq")]
    public int Seq { get; init; }

    /// <summary>The host function being called, for <c>type: call</c> (e.g. <c>plot</c>).</summary>
    [JsonPropertyName("fn")]
    public string? Fn { get; init; }

    /// <summary>The call's positional arguments, for <c>type: call</c>.</summary>
    [JsonPropertyName("args")]
    public JsonElement? Args { get; init; }

    /// <summary>The value the host is returning, for <c>type: return</c>.</summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; init; }

    /// <summary>The workspace snapshot, for <c>type: vars</c>.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<PythonVariablePayload>? Items { get; init; }
}

/// <summary>One variable in a <c>type: vars</c> snapshot.</summary>
/// <param name="Name">The binding's name.</param>
/// <param name="Type">The Python type name, mapped to the host's vocabulary where one exists.</param>
/// <param name="Repr">A short display string, already truncated by the child.</param>
/// <param name="Data">The numeric contents when the value is a list/tuple/ndarray small enough to send,
/// otherwise null — an oversize array reports its shape in <paramref name="Repr"/> and sends no data.</param>
internal sealed record PythonVariablePayload(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("repr")] string Repr,
    [property: JsonPropertyName("data")] double[]? Data);
