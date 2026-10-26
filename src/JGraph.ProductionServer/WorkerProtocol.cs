using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JGraph.ProductionServer;

/// <summary>
/// What passes between the server and a worker process (compiler and SDK plan, stage K14): frames of a
/// four-byte little-endian length and UTF-8 JSON, requests down the worker's standard input and
/// replies up its standard output. A worker's own console output goes to its standard error, which
/// the server logs and reads metric events off (stage K15), so a function's <c>disp</c> cannot corrupt
/// the stream.
/// </summary>
internal static class WorkerProtocol
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A web request handed to a web handler (stage K15): what MATLAB Production Server's request struct holds.</summary>
    /// <param name="Method">The HTTP method.</param>
    /// <param name="Path">The request target as sent, query included.</param>
    /// <param name="HttpVersion">The protocol version, <c>1.1</c>.</param>
    /// <param name="Headers">The request headers in order, each a name and a value.</param>
    /// <param name="BodyBase64">The body, base64.</param>
    public sealed record WebRequest(string Method, string Path, string HttpVersion, string[][] Headers, string BodyBase64);

    /// <summary>A request to a worker.</summary>
    /// <param name="Id">The request's number, echoed by the reply.</param>
    /// <param name="Op"><c>deploy</c>, <c>undeploy</c>, <c>call</c>, <c>web</c>, <c>cancel</c> or <c>shutdown</c>.</param>
    /// <param name="Name">The archive's name (the component the server routes by).</param>
    /// <param name="Path">The archive's file, for <c>deploy</c>.</param>
    /// <param name="Function">The function called, for <c>call</c> and <c>web</c>.</param>
    /// <param name="Body">The request body, for <c>call</c>.</param>
    /// <param name="HideStack">Whether the error reply leaves the frames out.</param>
    /// <param name="Target">The id of the call to cancel, for <c>cancel</c>.</param>
    /// <param name="Folder">The runtime cache folder's name, for <c>deploy</c> (<c>&lt;archive&gt;_&lt;n&gt;</c>).</param>
    /// <param name="Web">The web request, for <c>web</c>.</param>
    /// <param name="CacheRoot">The instance's own runtime cache the archive is extracted into, for <c>deploy</c>.</param>
    public sealed record Request(long Id, string Op, string? Name = null, string? Path = null, string? Function = null, string? Body = null,
        bool HideStack = false, long Target = 0, string? Folder = null, WebRequest? Web = null, string? CacheRoot = null);

    /// <summary>A worker's reply.</summary>
    /// <param name="Id">The request answered.</param>
    /// <param name="Status">The HTTP status for a call; 200 for a deploy that worked; a web handler's code.</param>
    /// <param name="Reason">The reason phrase, or a deploy's fault, or a web handler's message.</param>
    /// <param name="Body">A call's JSON reply, or null.</param>
    /// <param name="Functions">The component's functions, for a deploy.</param>
    /// <param name="WebHeaders">A web handler's response headers, each a name and a value.</param>
    /// <param name="WebBodyBase64">A web handler's response body, base64.</param>
    /// <param name="WebRefused">Whether a web reply is the server's refusal (its status and reason) rather than the handler's answer.</param>
    public sealed record Reply(long Id, int Status, string Reason, string? Body = null, string[]? Functions = null,
        string[][]? WebHeaders = null, string? WebBodyBase64 = null, bool WebRefused = false);

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken cancellationToken)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, json.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The next frame, or null at the end of the stream.</summary>
    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
        where T : class
    {
        byte[] length = new byte[4];
        if (!await FillAsync(stream, length, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count < 0 || count > 1 << 30)
        {
            throw new InvalidDataException($"A worker frame of {count} bytes is not one.");
        }

        byte[] json = new byte[count];
        if (!await FillAsync(stream, json, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(json, Options);
    }

    private static async Task<bool> FillAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int at = 0;
        while (at < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(at), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            at += read;
        }

        return true;
    }

    /// <summary>The text of a UTF-8 body, invalid bytes as U+FFFD, as R2025b's server reads one (k14_server: body.latin1).</summary>
    public static string Utf8(byte[] bytes) => new UTF8Encoding(false, false).GetString(bytes);
}
