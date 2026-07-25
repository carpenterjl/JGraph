using System.Text.Json;
using System.Text.Json.Serialization;

namespace JGraph.Scripting.PythonConsole;

/// <summary>
/// Reads and writes the newline-delimited JSON frames the Python console speaks. Framing is one
/// message per line: JSON never contains a raw newline, so a line is a complete message and a partial
/// read can never be mistaken for one.
/// </summary>
/// <remarks>
/// This type has no dependency on a running interpreter, which is the point — the protocol is testable
/// on a machine with no Python installed, and only the live child-process round trip is manual.
/// </remarks>
internal static class PythonConsoleCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // The child writes plain ASCII JSON; nothing here needs relaxed escaping, and the strict
        // default is the safer choice for text that came from user code.
        WriteIndented = false,
    };

    /// <summary>Serialises <paramref name="message"/> to a single line, without its terminator.</summary>
    public static string Encode(PythonConsoleMessage message) =>
        JsonSerializer.Serialize(message, Options);

    /// <summary>
    /// Parses one line. Returns false for anything that is not a JSON object — a child that prints to
    /// the real stdout despite the redirection (a C extension writing to fd 1, say) must not take the
    /// session down, so unparseable lines are the caller's to report, not an exception.
    /// </summary>
    public static bool TryDecode(string line, out PythonConsoleMessage message)
    {
        message = null!;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            PythonConsoleMessage? parsed = JsonSerializer.Deserialize<PythonConsoleMessage>(line, Options);
            if (parsed is null)
            {
                return false;
            }

            message = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>A request to execute <paramref name="code"/>, tagged with <paramref name="id"/>.</summary>
    public static PythonConsoleMessage Exec(int id, string code) =>
        new() { Id = id, Op = "exec", Code = code };

    /// <summary>A request for the child's workspace snapshot.</summary>
    public static PythonConsoleMessage Vars() => new() { Op = "vars" };

    /// <summary>A request for the child to exit cleanly.</summary>
    public static PythonConsoleMessage Shutdown() => new() { Op = "shutdown" };

    /// <summary>The host's reply to a <c>call</c>, carrying the value the proxy should return.</summary>
    public static PythonConsoleMessage Return(int seq, JsonElement? value = null) =>
        new() { Type = "return", Seq = seq, Value = value };

    /// <summary>The host's reply to a <c>call</c> the host could not satisfy.</summary>
    public static PythonConsoleMessage ReturnError(int seq, string message) =>
        new() { Type = "return", Seq = seq, Message = message };
}
