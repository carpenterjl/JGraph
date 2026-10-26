using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JGraph.ProductionServer;

/// <summary>
/// What passes between the server and a worker process (compiler and SDK plan, stage K14): frames of a
/// four-byte little-endian length and UTF-8 JSON, requests down the worker's standard input and
/// replies up its standard output. A worker's own console output goes to its standard error, which
/// the server logs, so a function's <c>disp</c> cannot corrupt the stream.
/// </summary>
internal static class WorkerProtocol
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>A request to a worker.</summary>
    /// <param name="Id">The request's number, echoed by the reply.</param>
    /// <param name="Op"><c>deploy</c>, <c>undeploy</c>, <c>call</c>, <c>cancel</c> or <c>shutdown</c>.</param>
    /// <param name="Name">The archive's name (the component the server routes by).</param>
    /// <param name="Path">The archive's file, for <c>deploy</c>.</param>
    /// <param name="Function">The function called, for <c>call</c>.</param>
    /// <param name="Body">The request body, for <c>call</c>.</param>
    /// <param name="HideStack">Whether the error reply leaves the frames out.</param>
    /// <param name="Target">The id of the call to cancel, for <c>cancel</c>.</param>
    public sealed record Request(long Id, string Op, string? Name = null, string? Path = null, string? Function = null, string? Body = null,
        bool HideStack = false, long Target = 0);

    /// <summary>A worker's reply.</summary>
    /// <param name="Id">The request answered.</param>
    /// <param name="Status">The HTTP status for a call; 200 for a deploy that worked.</param>
    /// <param name="Reason">The reason phrase, or a deploy's fault.</param>
    /// <param name="Body">A call's JSON reply, or null.</param>
    /// <param name="Functions">The component's functions, for a deploy.</param>
    public sealed record Reply(long Id, int Status, string Reason, string? Body = null, string[]? Functions = null);

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
