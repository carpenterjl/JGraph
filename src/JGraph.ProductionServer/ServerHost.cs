using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using JGraph.Deploy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JGraph.ProductionServer;

/// <summary>
/// A running JGraph Production Server (compiler and SDK plan, stage K14): Kestrel on the instance's
/// <c>--http</c> address (decision KO6), the worker pool, and the archive watcher. One request is
/// <c>POST /&lt;archive&gt;/&lt;function&gt;</c> with a JSON body; the status codes, reason phrases and headers
/// are those MATLAB Production Server's in-MATLAB test server answered (<c>k14_server</c>): a 404
/// <c>ResourceNotFound URI-path not of form '/APPLICATION/FUNCTION'</c> for any other path shape, 404
/// <c>ComponentNotFound</c> and <c>FunctionNotFound</c>, 405 <c>MethodNotAllowed Unsupported method</c>
/// for methods but POST, 400 <c>MissingContentType</c> and 415 <c>UnsupportedContentType</c>, 400
/// <c>InvalidJson</c> and <c>InvalidInput</c> for bodies that are not requests, 200 with the outputs or
/// the error, and 500 <c>ReturnTypeNotSupported</c> for an output JSON cannot hold; CORS preflights
/// and the <c>Access-Control-Allow-Origin</c> and <c>Vary</c> headers with <c>--cors-allowed-origins</c>;
/// <c>GET /api/discovery</c> with <c>--enable-discovery</c>. Each refusal's phrase ends with the request's
/// id, <c>(request id=0:n:0)</c>, as R2025b's do.
/// </summary>
public sealed class ServerHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly WorkerPool _pool;
    private readonly ArchiveWatcher _watcher;
    private readonly ConcurrentDictionary<string, CtfManifest> _manifests = new(StringComparer.Ordinal);
    private long _requests;

    private ServerHost(WebApplication app, WorkerPool pool, ArchiveWatcher watcher, ServerInstance instance, Action<string> log)
    {
        _app = app;
        _pool = pool;
        _watcher = watcher;
        Instance = instance;
        Log = log;
    }

    /// <summary>The instance served.</summary>
    public ServerInstance Instance { get; }

    /// <summary>Where the log lines go.</summary>
    public Action<string> Log { get; }

    /// <summary>The address the server listens on, once started.</summary>
    public string Url { get; private set; } = "";

    /// <summary>The port bound, once started.</summary>
    public int Port { get; private set; }

    /// <summary>
    /// Starts the workers, deploys the archives and listens. <paramref name="port"/> overrides the
    /// config's (0 for any free port, as the tests take).
    /// </summary>
    public static async Task<ServerHost> StartAsync(ServerInstance instance, int? port, Action<string> log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(log);
        MainConfig config = instance.Config;
        foreach (string unread in config.Unread)
        {
            log($"main_config: {unread} is not read by jgraph-mps");
        }

        var pool = new WorkerPool(config.Workers, log);
        await pool.StartAsync().ConfigureAwait(false);
        var watcher = new ArchiveWatcher(instance.AutoDeployFolder, pool, log);

        (string host, int configured) = config.Http;
        int listenOn = port ?? configured;
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = config.RequestSizeLimit;
            IPAddress address = host is "localhost" or "127.0.0.1" ? IPAddress.Loopback
                : host == "0.0.0.0" || host == "*" ? IPAddress.Any
                : IPAddress.TryParse(host, out IPAddress? parsed) ? parsed : IPAddress.Loopback;
            options.Listen(address, listenOn);
        });
        WebApplication app = builder.Build();
        var server = new ServerHost(app, pool, watcher, instance, log);
        app.Run(server.HandleAsync);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        string bound = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault() ?? $"http://{host}:{listenOn}";
        server.Url = bound.Replace("[::]", "localhost", StringComparison.Ordinal).Replace("0.0.0.0", "localhost", StringComparison.Ordinal);
        server.Port = new Uri(server.Url).Port;
        await watcher.StartAsync().ConfigureAwait(false);
        return server;
    }

    private string RequestId() => $"(request id=0:{Interlocked.Increment(ref _requests).ToString(CultureInfo.InvariantCulture)}:0)";

    private async Task HandleAsync(HttpContext context)
    {
        HttpRequest request = context.Request;
        HttpResponse response = context.Response;
        string rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? request.Path.Value ?? "/";
        int question = rawTarget.IndexOf('?', StringComparison.Ordinal);
        string path = question < 0 ? rawTarget : rawTarget[..question];
        bool hasQuery = question >= 0;
        string method = request.Method;
        string? origin = request.Headers.Origin.FirstOrDefault();
        bool corsOrigin = origin is not null && OriginAllowed(origin);

        void Refuse(int status, string phrase)
        {
            response.StatusCode = status;
            response.ContentLength = 0;
            SetReason(context, phrase + " " + RequestId());
        }

        void CorsHeaders()
        {
            if (corsOrigin)
            {
                response.Headers.AccessControlAllowOrigin = origin;
                response.Headers.Vary = "Origin";
            }
        }

        if (method == HttpMethods.Options)
        {
            // A preflight, when CORS is on and the request names its method (k14_server: cors preflight*).
            string? asked = request.Headers.AccessControlRequestMethod.FirstOrDefault();
            if (corsOrigin && asked is not null)
            {
                if (asked is not ("POST" or "GET" or "DELETE"))
                {
                    Refuse(405, "Access-Control-Request-Method not allowed");
                    return;
                }

                response.StatusCode = 200;
                response.Headers.AccessControlAllowOrigin = origin;
                response.Headers.AccessControlAllowMethods = "POST, GET, DELETE";
                if (request.Headers.AccessControlRequestHeaders.FirstOrDefault() is { Length: > 0 } headers)
                {
                    response.Headers.AccessControlAllowHeaders = headers;
                }

                return;
            }

            CorsHeaders();
            Refuse(405, path.StartsWith("/api/", StringComparison.Ordinal) ? "Bad Method" : "MethodNotAllowed Unsupported method");
            return;
        }

        CorsHeaders();

        // The API's paths, exact and without a query (get.discovery.query is not one). A GET of any other path is
        // "URL not found" (get.root, get.api, get.nosuch); any other method's goes to the archives (path.api.like:
        // POST /api/k14add asks for a component named api).
        bool apiPath = !hasQuery && path is "/api/discovery" or "/api/health" or "/api/metrics" or "/api/statistics" or "/api/archives" or "/api/archives/";
        if (apiPath)
        {
            await ApiAsync(context, path, Refuse).ConfigureAwait(false);
            return;
        }

        if (method == HttpMethods.Get)
        {
            Refuse(404, "URL not found " + path);
            return;
        }

        // /APPLICATION/FUNCTION and nothing else (path.*); the function may be missing, the shape may not.
        string[] parts = path.Split('/');
        if (parts.Length != 3 || parts[0].Length != 0 || parts[1].Length == 0)
        {
            Refuse(404, "ResourceNotFound URI-path not of form '/APPLICATION/FUNCTION'");
            return;
        }

        if (method is "PUT" or "DELETE" or "HEAD")
        {
            Refuse(405, "MethodNotAllowed Unsupported method");
            return;
        }

        if (method != HttpMethods.Post)
        {
            Refuse(400, "Invalid HTTP");
            return;
        }

        string component = parts[1];
        string function = parts[2];
        if (!_pool.Functions.TryGetValue(component, out string[]? functions))
        {
            Refuse(404, "ComponentNotFound Component not found.");
            return;
        }

        if (!functions.Contains(function, StringComparer.Ordinal))
        {
            Refuse(404, "FunctionNotFound Function not found.");
            return;
        }

        // The body's type: JSON in any case with any charset; protobuf is read as nothing (stage K16 brings it).
        string? contentType = request.Headers.ContentType.FirstOrDefault();
        if (contentType is null)
        {
            Refuse(400, "MissingContentType Content-Type header value missing.");
            return;
        }

        string mediaType = contentType.Split(';')[0].Trim();
        if (mediaType.Length == 0)
        {
            Refuse(415, "InvalidContentType \"\" is not syntactically correct.");
            return;
        }

        bool protobuf = mediaType.Equals("application/x-google-protobuf", StringComparison.OrdinalIgnoreCase);
        if (!protobuf && !mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            Refuse(415, "UnsupportedContentType Content-Type header value is unsupported.");
            return;
        }

        byte[] body;
        try
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
            body = buffer.ToArray();
        }
        catch (BadHttpRequestException)
        {
            Refuse(413, "Request Entity Too Large");
            return;
        }

        if (protobuf)
        {
            Refuse(400, "InvalidInputArgs Invalid rhs.");
            return;
        }

        WorkerProtocol.Reply reply;
        try
        {
            reply = await _pool.CallAsync(component, function, WorkerProtocol.Utf8(body), Instance.Config.HideErrorStack, Instance.Config.RequestTimeout, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // the client went away
        }

        if (reply.Status != 200 || reply.Body is null)
        {
            Refuse(reply.Status, reply.Reason);
            return;
        }

        await WriteJsonAsync(context, reply.Body).ConfigureAwait(false);
    }

    /// <summary>A 200 with a JSON body and its length (R2025b's responses carry Content-Length).</summary>
    private static async Task WriteJsonAsync(HttpContext context, string json)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task ApiAsync(HttpContext context, string path, Action<int, string> refuse)
    {
        string method = context.Request.Method;
        switch (path)
        {
            case "/api/discovery":
                // A HEAD is refused first, then discovery being off (post.discovery is Discovery Disabled when it is),
                // then any method but GET.
                if (method is "HEAD")
                {
                    refuse(405, "Bad Method");
                }
                else if (!Instance.Config.Discovery)
                {
                    refuse(403, "Discovery Disabled");
                }
                else if (method != HttpMethods.Get)
                {
                    refuse(405, "Method Not Allowed");
                }
                else
                {
                    await WriteJsonAsync(context, Discovery.Json(Manifests())).ConfigureAwait(false);
                }

                return;
            case "/api/health":
                // Stage K15 gives the health check its answer; the server answers while it listens.
                if (method != HttpMethods.Get)
                {
                    refuse(405, "Method Not Allowed");
                    return;
                }

                context.Response.StatusCode = 200;
                context.Response.ContentLength = 0;
                return;
            case "/api/metrics":
                refuse(403, "Metrics Disabled");
                return;
            case "/api/statistics":
                refuse(403, "Statistics Disabled");
                return;
            case "/api/archives" or "/api/archives/":
                refuse(403, "Archive Management Disabled");
                return;
            default:
                refuse(404, "URL not found " + path);
                return;
        }
    }

    /// <summary>The deployed archives' manifests, read once each from the archive file.</summary>
    private IEnumerable<(string Name, CtfManifest Manifest)> Manifests()
    {
        foreach ((string name, string path) in _watcher.Deployed.ToArray())
        {
            if (!_manifests.TryGetValue(name, out CtfManifest? manifest))
            {
                try
                {
                    using CtfArchiveReader archive = CtfArchiveReader.Open(path);
                    manifest = archive.Manifest;
                    _manifests[name] = manifest;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException)
                {
                    continue;
                }
            }

            yield return (name, manifest);
        }
    }

    private bool OriginAllowed(string origin)
    {
        IReadOnlyList<string> allowed = Instance.Config.CorsOrigins;
        return allowed.Count > 0 && (allowed.Contains("*") || allowed.Contains(origin.ToLowerInvariant()));
    }

    private static void SetReason(HttpContext context, string phrase)
    {
        if (context.Features.Get<IHttpResponseFeature>() is { } feature)
        {
            feature.ReasonPhrase = phrase;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.Dispose();
        await _app.StopAsync().ConfigureAwait(false);
        await _pool.DisposeAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
