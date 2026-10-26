using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace JGraph.ProductionServer;

/// <summary>The states of an asynchronous request, in the words the RESTful API gives them.</summary>
internal enum AsyncState
{
    Reading,
    InQueue,
    Processing,
    Ready,
    Error,
    Cancelled,
}

/// <summary>
/// One asynchronous request (compiler and SDK plan, stage K15): <c>POST /archive/function?mode=async&amp;client=…</c>
/// as MATLAB Production Server's RESTful API documents it, which R2025b's muserve does not answer
/// (k15_server: async, 400 "mode=async is not supported."), so the protocol is the documentation's:
/// the request is represented at <c>/~&lt;instance&gt;/requests/&lt;id&gt;</c> with its state, its
/// <c>lastModifiedSeq</c> (the server's sequence number at its last change) and its client; its
/// result at <c>…/result</c>, its state alone at <c>…/info</c>; <c>POST …/cancel</c> cancels one that
/// has not finished, <c>DELETE</c> removes one that has.
/// </summary>
internal sealed class AsyncRequest
{
    private readonly AsyncRequestRegistry _registry;

    internal AsyncRequest(AsyncRequestRegistry registry, string client)
    {
        _registry = registry;
        Client = client;
        Id = Guid.NewGuid().ToString("D");
        LastModifiedSeq = registry.Next();
        Cancellation = new CancellationTokenSource();
    }

    public string Id { get; }

    public string Client { get; }

    public AsyncState State { get; private set; } = AsyncState.Reading;

    public long LastModifiedSeq { get; private set; }

    /// <summary>Whether <c>DELETE</c> removed it; it is then only remembered for the 410 its paths answer.</summary>
    public bool Deleted { get; private set; }

    /// <summary>Cancels the function's run.</summary>
    public CancellationTokenSource Cancellation { get; }

    /// <summary>The reply, once the request is ready or failed: a 200 with its body, or the refusal's status and reason.</summary>
    public WorkerProtocol.Reply? Result { get; private set; }

    public string Self => _registry.Up + "/" + Id;

    public bool Terminal => State is AsyncState.Ready or AsyncState.Error or AsyncState.Cancelled;

    /// <summary>Moves the request on, stamping the server's next sequence number.</summary>
    public void Advance(AsyncState state, WorkerProtocol.Reply? result = null)
    {
        lock (_registry.Sync)
        {
            if (Terminal && state != State)
            {
                return;
            }

            State = state;
            Result = result ?? Result;
            LastModifiedSeq = _registry.Next();
        }

        _registry.Changed();
    }

    public void MarkDeleted()
    {
        lock (_registry.Sync)
        {
            Deleted = true;
        }
    }

    /// <summary>The representation: <c>{"id","self","up","lastModifiedSeq","state","client"}</c>.</summary>
    public void WriteRepresentation(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("id", Id);
        writer.WriteString("self", Self);
        writer.WriteString("up", _registry.Up);
        writer.WriteNumber("lastModifiedSeq", LastModifiedSeq);
        writer.WriteString("state", StateName(State));
        writer.WriteString("client", Client);
        writer.WriteEndObject();
    }

    /// <summary>The state information: <c>{"request","lastModifiedSeq","state"}</c>.</summary>
    public string InfoJson()
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("request", Self);
            writer.WriteNumber("lastModifiedSeq", LastModifiedSeq);
            writer.WriteString("state", StateName(State));
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public string RepresentationJson()
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteRepresentation(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static string StateName(AsyncState state) => state switch
    {
        AsyncState.Reading => "READING",
        AsyncState.InQueue => "IN_QUEUE",
        AsyncState.Processing => "PROCESSING",
        AsyncState.Ready => "READY",
        AsyncState.Error => "ERROR",
        _ => "CANCELLED",
    };
}

/// <summary>
/// The server's asynchronous requests: the instance's collection at <c>/~&lt;instance&gt;/requests</c>,
/// the sequence number every change stamps, and the long poll <c>GET …/requests?since=N</c> waits on.
/// A deleted request stays in memory, so its paths answer 410; the oldest go once ten thousand are held.
/// </summary>
internal sealed class AsyncRequestRegistry
{
    private const int Kept = 10_000;
    private readonly ConcurrentDictionary<string, AsyncRequest> _requests = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();
    private readonly HashSet<string> _clients = new(StringComparer.Ordinal);
    private long _seq;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The instance's id, the first segment of every request path.</summary>
    public string Instance { get; } = Guid.NewGuid().ToString("D");

    public string Up => "/~" + Instance + "/requests";

    internal object Sync { get; } = new();

    public long Next() => Interlocked.Increment(ref _seq);

    /// <summary>The server's state number now (<c>createdSeq</c>).</summary>
    public long Current => Interlocked.Read(ref _seq);

    public AsyncRequest Create(string client)
    {
        var request = new AsyncRequest(this, client);
        _requests[request.Id] = request;
        _order.Enqueue(request.Id);
        lock (Sync)
        {
            _clients.Add(client);
        }

        while (_order.Count > Kept && _order.TryDequeue(out string? oldest))
        {
            _requests.TryRemove(oldest, out _);
        }

        Changed();
        return request;
    }

    public AsyncRequest? Find(string id) => _requests.TryGetValue(id, out AsyncRequest? found) ? found : null;

    /// <summary>Whether a request of <paramref name="client"/> was ever made.</summary>
    public bool KnowsClient(string client)
    {
        lock (Sync)
        {
            return _clients.Contains(client);
        }
    }

    /// <summary>The live requests of the clients or ids named, changed after <paramref name="since"/>, latest change first.</summary>
    public List<AsyncRequest> Select(IReadOnlyCollection<string> clients, IReadOnlyCollection<string> ids, long since) =>
        [.. _requests.Values
            .Where(r => !r.Deleted && r.LastModifiedSeq > since && (clients.Contains(r.Client) || ids.Contains(r.Id)))
            .OrderByDescending(static r => r.LastModifiedSeq)];

    /// <summary>Whether any id named is a request at all.</summary>
    public bool AnyKnown(IReadOnlyCollection<string> ids) => ids.Any(id => _requests.ContainsKey(id));

    /// <summary>Completes when the next change is stamped.</summary>
    public Task WaitForChangeAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource waiter = Volatile.Read(ref _changed);
        return waiter.Task.WaitAsync(cancellationToken);
    }

    internal void Changed()
    {
        TaskCompletionSource previous = Interlocked.Exchange(ref _changed, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        previous.TrySetResult();
    }

    /// <summary>The collection's body: <c>{"createdSeq":N,"data":[…]}</c>.</summary>
    public string CollectionJson(IEnumerable<AsyncRequest> requests)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("createdSeq", Current);
            writer.WriteStartArray("data");
            foreach (AsyncRequest request in requests)
            {
                request.WriteRepresentation(writer);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The <c>X-MPS-Start-Time</c> value: sixteen hex digits of the server clock's 100 ns ticks.</summary>
    public static string StartTime() => (DateTime.UtcNow.Ticks & 0x00FFFFFFFFFFFFFF).ToString("x16", CultureInfo.InvariantCulture);
}
