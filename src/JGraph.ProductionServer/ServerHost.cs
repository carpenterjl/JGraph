using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
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
/// A running JGraph Production Server (compiler and SDK plan, stages K14 and K15): Kestrel on the
/// instance's <c>--http</c> address (decision KO6), the worker pool, the archive watcher, the route
/// table, the metrics and the asynchronous requests. One request is <c>POST /&lt;archive&gt;/&lt;function&gt;</c>
/// with a JSON body; the status codes, reason phrases and headers are those MATLAB Production
/// Server's in-MATLAB test server and R2025b's muserve answered (<c>k14_server</c>, <c>k15_server</c>),
/// which agree case for case: a 404 <c>ResourceNotFound URI-path not of form '/APPLICATION/FUNCTION'</c>
/// for any other path shape, 404 <c>ComponentNotFound</c> and <c>FunctionNotFound</c>, 405
/// <c>MethodNotAllowed Unsupported method</c> for methods but POST, 400 <c>MissingContentType</c> and 415
/// <c>UnsupportedContentType</c>, 400 <c>InvalidJson</c> and <c>InvalidInput</c> for bodies that are not
/// requests, 200 with the outputs or the error, 500 <c>ReturnTypeNotSupported</c> for an output JSON
/// cannot hold, 413 past <c>--request-size-limit</c>; CORS preflights and the <c>Access-Control-Allow-Origin</c>
/// and <c>Vary</c> headers with <c>--cors-allowed-origins</c>; <c>GET /api/discovery</c>, <c>/api/health</c>
/// and <c>/api/metrics</c>; the paths a routes file maps to web handlers; and <c>?mode=async</c>, the
/// asynchronous request protocol of the RESTful API's documentation. Each refusal's phrase ends with
/// the request's id, <c>(request id=0:n:0)</c>, as R2025b's do.
/// </summary>
public sealed class ServerHost : IAsyncDisposable
{
    private static readonly string[] KnownMethods = ["GET", "POST", "PUT", "DELETE", "HEAD", "OPTIONS"];

    private readonly WebApplication _app;
    private readonly WorkerPool _pool;
    private readonly ArchiveWatcher _watcher;
    private readonly RouteTable _routes;
    private readonly MetricsRegistry _metrics;
    private readonly AsyncRequestRegistry _async = new();
    private readonly ConcurrentDictionary<string, CtfManifest> _manifests = new(StringComparer.Ordinal);
    private long _requests;

    private ServerHost(WebApplication app, WorkerPool pool, ArchiveWatcher watcher, RouteTable routes, MetricsRegistry metrics, ServerInstance instance, Action<string> log)
    {
        _app = app;
        _pool = pool;
        _watcher = watcher;
        _routes = routes;
        _metrics = metrics;
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
    /// <exception cref="InvalidOperationException">The instance's routes file is not as it should be; the server does not start, as MPS does not.</exception>
    public static async Task<ServerHost> StartAsync(ServerInstance instance, int? port, Action<string> log, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(log);
        MainConfig config = instance.Config;
        foreach (string unread in config.Unread)
        {
            log($"main_config: {unread} is not read by jgraph-mps");
        }

        var routes = new RouteTable();
        if (instance.RoutesFilePath is { } routesFile)
        {
            try
            {
                routes.LoadInstanceFile(routesFile);
            }
            catch (Exception ex) when (ex is RoutesException or IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"The routes file '{routesFile}' cannot be used: {ex.Message}", ex);
            }
        }

        var metrics = new MetricsRegistry();
        var pool = new WorkerPool(config.Workers, log, line => metrics.ReadLine(line));
        await pool.StartAsync().ConfigureAwait(false);
        var watcher = new ArchiveWatcher(instance.AutoDeployFolder, Path.Combine(instance.Root, ".jgraph_deployed"), pool, routes, log);

        (string host, int configured) = config.Http;
        int listenOn = port ?? configured;
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = config.RequestSizeLimit;
            // localhost is both loopbacks (a client resolving localhost may try ::1 first, and a SYN to
            // an unbound ::1 was seen to hang on Windows); a port alone binds every interface, as MPS does.
            if (host is "localhost" or "127.0.0.1")
            {
                if (listenOn == 0)
                {
                    options.Listen(IPAddress.Loopback, 0);
                }
                else
                {
                    options.ListenLocalhost(listenOn);
                }
            }
            else if (host is "0.0.0.0" or "*" or "::")
            {
                options.ListenAnyIP(listenOn);
            }
            else
            {
                options.Listen(IPAddress.TryParse(host, out IPAddress? parsed) ? parsed : IPAddress.Loopback, listenOn);
            }
        });
        WebApplication app = builder.Build();
        var server = new ServerHost(app, pool, watcher, routes, metrics, instance, log);
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

        void Refuse(int status, string phrase, bool withId = true)
        {
            response.StatusCode = status;
            response.ContentLength = 0;
            SetReason(context, withId ? phrase + " " + RequestId() : phrase);
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

        // A body past --request-size-limit is refused by its Content-Length before anything else (k15_server: size.*).
        long limit = Instance.Config.RequestSizeLimit;
        if (request.ContentLength is { } declared && declared > limit)
        {
            Refuse(413, $"Content-Length exceeds size limit of {limit.ToString(CultureInfo.InvariantCulture)} bytes");
            return;
        }

        // A method HTTP does not have here is refused before any routing (route.patch).
        if (!KnownMethods.Contains(method, StringComparer.Ordinal))
        {
            Refuse(400, "Invalid HTTP");
            return;
        }

        // The asynchronous requests' paths, /~<instance>/requests… (stage K15).
        if (path.StartsWith("/~", StringComparison.Ordinal))
        {
            await AsyncApiAsync(context, path, Refuse).ConfigureAwait(false);
            return;
        }

        // A path a routes file maps goes to its web handler, whatever the method (route.*).
        if (_routes.Match(rawTarget) is { } target)
        {
            await WebAsync(context, target, rawTarget, Refuse).ConfigureAwait(false);
            return;
        }

        if (method == HttpMethods.Options)
        {
            Refuse(405, path.StartsWith("/api/", StringComparison.Ordinal) ? "Bad Method" : "MethodNotAllowed Unsupported method");
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

        byte[]? body = await ReadBodyAsync(context, Refuse).ConfigureAwait(false);
        if (body is null)
        {
            return;
        }

        // The protobuf format (stage K16) carries MATLAB_Params and answers MATLAB_Result; JSON is stage K14's.
        string? text = protobuf ? null : WorkerProtocol.Utf8(body);
        if (request.Query.TryGetValue("mode", out Microsoft.Extensions.Primitives.StringValues mode) && mode.ToString() == "async")
        {
            await StartAsyncRequestAsync(context, component, function, text, protobuf ? body : null).ConfigureAwait(false);
            return;
        }

        _metrics.Accepted();
        var queued = System.Diagnostics.Stopwatch.StartNew();
        WorkerProtocol.Reply reply;
        try
        {
            reply = await CallPoolAsync(component, function, text, protobuf ? body : null, context.RequestAborted, () => _metrics.Started(queued.Elapsed)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _metrics.Canceled(wasProcessing: false);
            return; // the client went away
        }

        _metrics.Finished(reply.Status == 200);
        if (reply.Status != 200 || (reply.Body is null && reply.BodyBase64 is null))
        {
            Refuse(reply.Status, reply.Reason);
            return;
        }

        if (reply.BodyBase64 is not null)
        {
            await WriteProtobufAsync(context, Convert.FromBase64String(reply.BodyBase64)).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(context, reply.Body!).ConfigureAwait(false);
    }

    /// <summary>One call on the pool, in the JSON format with <paramref name="text"/> or the protobuf one with <paramref name="protobufBody"/>.</summary>
    private Task<WorkerProtocol.Reply> CallPoolAsync(string component, string function, string? text, byte[]? protobufBody, CancellationToken cancellationToken, Action started) =>
        protobufBody is not null
            ? _pool.CallProtobufAsync(component, function, protobufBody, Instance.Config.HideErrorStack, Instance.Config.RequestTimeout, cancellationToken, started)
            : _pool.CallAsync(component, function, text ?? "", Instance.Config.HideErrorStack, Instance.Config.RequestTimeout, cancellationToken, started);

    /// <summary>A 200 with a MATLAB_Result body; muserve sends no outputs as an empty body with its length and no type (k16_server: nargout.0).</summary>
    private static async Task WriteProtobufAsync(HttpContext context, byte[] bytes)
    {
        context.Response.StatusCode = 200;
        if (bytes.Length > 0)
        {
            context.Response.ContentType = "application/x-google-protobuf";
        }

        context.Response.ContentLength = bytes.Length;
        if (bytes.Length > 0)
        {
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
        }
    }

    /// <summary>The whole body, or null after a 413 for one past the limit as it is read.</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpContext context, Action<int, string, bool> refuse)
    {
        try
        {
            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
            return buffer.ToArray();
        }
        catch (BadHttpRequestException)
        {
            long limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize ?? MainConfig.DefaultRequestSizeLimit;
            refuse(413, $"Content-Length exceeds size limit of {limit.ToString(CultureInfo.InvariantCulture)} bytes", true);
            return null;
        }
    }

    /// <summary>A 200 with a JSON body and its length (R2025b's responses carry Content-Length).</summary>
    private static async Task WriteJsonAsync(HttpContext context, string json, int status = 200)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteTextAsync(HttpContext context, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/plain";
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task ApiAsync(HttpContext context, string path, Action<int, string, bool> refuse)
    {
        string method = context.Request.Method;
        void Refuse(int status, string phrase) => refuse(status, phrase, true);
        switch (path)
        {
            case "/api/discovery":
                // A HEAD is refused first, then discovery being off (post.discovery is Discovery Disabled when it is),
                // then any method but GET.
                if (method is "HEAD" or "OPTIONS")
                {
                    Refuse(405, "Bad Method");
                }
                else if (!Instance.Config.Discovery)
                {
                    Refuse(403, "Discovery Disabled");
                }
                else if (method != HttpMethods.Get)
                {
                    Refuse(405, "Method Not Allowed");
                }
                else
                {
                    await WriteJsonAsync(context, Discovery.Json(Manifests())).ConfigureAwait(false);
                }

                return;
            case "/api/health":
                // {"status":"ok"} while the server listens (k15_server: health); MPS answers 503 "Health check failed"
                // only when it has lost its license server, which JGraph has none of.
                if (method is "HEAD" or "OPTIONS")
                {
                    Refuse(405, "Bad Method");
                }
                else if (method != HttpMethods.Get)
                {
                    Refuse(405, "Method Not Allowed");
                }
                else
                {
                    await WriteJsonAsync(context, "{\"status\":\"ok\"}").ConfigureAwait(false);
                }

                return;
            case "/api/metrics":
                if (method is "HEAD" or "OPTIONS")
                {
                    Refuse(405, "Bad Method");
                }
                else if (!Instance.Config.Metrics)
                {
                    Refuse(403, "Metrics Disabled");
                }
                else if (method != HttpMethods.Get)
                {
                    Refuse(405, "Method Not Allowed");
                }
                else
                {
                    await WriteTextAsync(context, _metrics.Text()).ConfigureAwait(false);
                }

                return;
            case "/api/statistics":
                Refuse(method is "HEAD" or "OPTIONS" ? 405 : 403, method is "HEAD" or "OPTIONS" ? "Bad Method" : "Statistics Disabled");
                return;
            case "/api/archives" or "/api/archives/":
                Refuse(method is "HEAD" or "OPTIONS" ? 405 : 403, method is "HEAD" or "OPTIONS" ? "Bad Method" : "Archive Management Disabled");
                return;
            default:
                Refuse(404, "URL not found " + path);
                return;
        }
    }

    // --- web handlers (stage K15) ---------------------------------------------------------------------

    private async Task WebAsync(HttpContext context, RouteTable.Target target, string rawTarget, Action<int, string, bool> refuse)
    {
        HttpRequest request = context.Request;
        HttpResponse response = context.Response;
        if (!_pool.Functions.TryGetValue(target.Component, out string[]? functions))
        {
            // muserve: "component not found" followed by a C++ function's name it leaked (route.othercomp); the name alone here.
            refuse(404, "component not found", true);
            return;
        }

        if (!functions.Contains(target.Function, StringComparer.Ordinal))
        {
            refuse(500, "invalid work type", true);
            return;
        }

        byte[]? body = await ReadBodyAsync(context, refuse).ConfigureAwait(false);
        if (body is null)
        {
            return;
        }

        // The headers as the handler sees them: Host first, as the wire carries it, then the rest as Kestrel keeps them.
        var headers = new List<string[]>();
        if (request.Headers.Host.FirstOrDefault() is { } host)
        {
            headers.Add(["Host", host]);
        }

        foreach ((string name, Microsoft.Extensions.Primitives.StringValues values) in request.Headers)
        {
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string? value in values)
            {
                headers.Add([name, value ?? ""]);
            }
        }

        var web = new WorkerProtocol.WebRequest(request.Method, rawTarget, request.Protocol.StartsWith("HTTP/", StringComparison.Ordinal) ? request.Protocol[5..] : "1.1",
            [.. headers], Convert.ToBase64String(body));
        _metrics.Accepted();
        var queued = System.Diagnostics.Stopwatch.StartNew();
        WorkerProtocol.Reply reply;
        try
        {
            reply = await _pool.CallWebAsync(target.Component, target.Function, web, Instance.Config.RequestTimeout, context.RequestAborted,
                started: () => _metrics.Started(queued.Elapsed)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _metrics.Canceled(wasProcessing: false);
            return;
        }

        if (reply.WebRefused || reply.WebBodyBase64 is null)
        {
            _metrics.Finished(false);
            // The server's own refusals carry no request id (bad.error, bad.number); the pool's (a worker ended) do.
            refuse(reply.Status, reply.Reason, !reply.WebRefused);
            return;
        }

        _metrics.Finished(true);
        byte[] bytes = Convert.FromBase64String(reply.WebBodyBase64);
        response.StatusCode = reply.Status;
        SetReason(context, reply.Reason);
        foreach (string[] header in reply.WebHeaders ?? [])
        {
            // A Content-Length the handler wrote is the body's own (muserve sends both; JGraph sends the body's).
            if (header.Length != 2 || header[0].Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                response.Headers.Append(header[0], header[1]);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // A name or value HTTP cannot carry is left out.
            }
        }

        response.ContentLength = bytes.Length;
        if (request.Method != "HEAD")
        {
            await response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
        }
    }

    // --- asynchronous requests (stage K15) --------------------------------------------------------------

    private async Task StartAsyncRequestAsync(HttpContext context, string component, string function, string? body, byte[]? protobufBody)
    {
        string client = context.Request.Query.TryGetValue("client", out Microsoft.Extensions.Primitives.StringValues given) ? given.ToString() : "";
        AsyncRequest request = _async.Create(client);
        _metrics.Accepted();
        context.Response.Headers.Location = request.Self;
        context.Response.Headers["X-MPS-Start-Time"] = AsyncRequestRegistry.StartTime();
        await WriteJsonAsync(context, request.RepresentationJson(), 201).ConfigureAwait(false);
        request.Advance(AsyncState.InQueue);
        _ = RunAsyncRequestAsync(request, component, function, body, protobufBody);
    }

    private async Task RunAsyncRequestAsync(AsyncRequest request, string component, string function, string? body, byte[]? protobufBody)
    {
        var queued = System.Diagnostics.Stopwatch.StartNew();
        bool processing = false;
        WorkerProtocol.Reply reply;
        try
        {
            reply = await CallPoolAsync(component, function, body, protobufBody, request.Cancellation.Token, () =>
            {
                processing = true;
                _metrics.Started(queued.Elapsed);
                request.Advance(AsyncState.Processing);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _metrics.Canceled(processing);
            request.Advance(AsyncState.Cancelled);
            return;
        }

        if (request.Cancellation.IsCancellationRequested)
        {
            _metrics.Canceled(processing);
            request.Advance(AsyncState.Cancelled);
            return;
        }

        bool ok = reply.Status == 200 && (reply.Body is not null || reply.BodyBase64 is not null);
        _metrics.Finished(ok);
        request.Advance(ok ? AsyncState.Ready : AsyncState.Error, reply);
    }

    private async Task AsyncApiAsync(HttpContext context, string path, Action<int, string, bool> refuse)
    {
        HttpRequest request = context.Request;
        string method = request.Method;
        void Refuse(int status, string phrase) => refuse(status, phrase, true);
        string[] parts = path.Split('/');
        // /~<instance>/requests[/<id>[/info|/result|/cancel]]
        if (parts.Length is < 3 or > 5 || parts[1] != "~" + _async.Instance || parts[2] != "requests")
        {
            Refuse(404, "URL not found " + path);
            return;
        }

        if (parts.Length == 3)
        {
            if (method != HttpMethods.Get)
            {
                Refuse(405, "Method Not Allowed");
                return;
            }

            await CollectionAsync(context, Refuse).ConfigureAwait(false);
            return;
        }

        AsyncRequest? found = _async.Find(parts[3]);
        string action = parts.Length == 5 ? parts[4] : "";
        if (action is not ("" or "info" or "result" or "cancel"))
        {
            Refuse(404, "URL not found " + path);
            return;
        }

        if (found is null)
        {
            Refuse(404, action is "" or "info" ? "ResourceNotFound" : "RequestNotFound");
            return;
        }

        switch (action)
        {
            case "":
                if (method == HttpMethods.Get)
                {
                    if (found.Deleted)
                    {
                        Refuse(410, "RequestAlreadyDeleted");
                        return;
                    }

                    await WriteJsonAsync(context, found.RepresentationJson()).ConfigureAwait(false);
                }
                else if (method == HttpMethods.Delete)
                {
                    if (found.Deleted)
                    {
                        Refuse(410, "RequestAlreadyDeleted");
                    }
                    else if (!found.Terminal)
                    {
                        Refuse(409, "RequestNotCompleted");
                    }
                    else
                    {
                        found.MarkDeleted();
                        context.Response.StatusCode = 204;
                        context.Response.ContentLength = 0;
                    }
                }
                else
                {
                    Refuse(405, "Method Not Allowed");
                }

                return;
            case "info":
                if (method != HttpMethods.Get)
                {
                    Refuse(405, "Method Not Allowed");
                }
                else if (found.Deleted)
                {
                    Refuse(410, "RequestAlreadyDeleted");
                }
                else
                {
                    await WriteJsonAsync(context, found.InfoJson()).ConfigureAwait(false);
                }

                return;
            case "result":
                if (method != HttpMethods.Get)
                {
                    Refuse(405, "Method Not Allowed");
                }
                else if (found.Deleted)
                {
                    Refuse(410, "RequestAlreadyDeleted");
                }
                else if (found.State == AsyncState.Cancelled)
                {
                    Refuse(410, "RequestAlreadyCancelled");
                }
                else if (!found.Terminal || found.Result is null)
                {
                    Refuse(409, "RequestNotCompleted");
                }
                else if (found.State == AsyncState.Ready && found.Result.BodyBase64 is not null)
                {
                    await WriteProtobufAsync(context, Convert.FromBase64String(found.Result.BodyBase64)).ConfigureAwait(false);
                }
                else if (found.State == AsyncState.Ready && found.Result.Body is not null)
                {
                    await WriteJsonAsync(context, found.Result.Body).ConfigureAwait(false);
                }
                else
                {
                    Refuse(found.Result.Status, found.Result.Reason);
                }

                return;
            case "cancel":
                if (method != HttpMethods.Post)
                {
                    Refuse(405, "Method Not Allowed");
                }
                else if (found.Deleted)
                {
                    Refuse(410, "RequestAlreadyDeleted");
                }
                else if (found.State == AsyncState.Cancelled)
                {
                    Refuse(410, "RequestAlreadyCancelled");
                }
                else if (found.Terminal)
                {
                    Refuse(410, "RequestAlreadyCompleted");
                }
                else
                {
                    found.Cancellation.Cancel();
                    context.Response.StatusCode = 204;
                    context.Response.ContentLength = 0;
                }

                return;
        }
    }

    private async Task CollectionAsync(HttpContext context, Action<int, string> refuse)
    {
        IQueryCollection query = context.Request.Query;
        if (!query.TryGetValue("since", out Microsoft.Extensions.Primitives.StringValues sinceText))
        {
            refuse(400, "MissingParamSince");
            return;
        }

        if (!long.TryParse(sinceText.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out long since))
        {
            refuse(400, "InvalidParamSince");
            return;
        }

        string[] clients = query.TryGetValue("clients", out Microsoft.Extensions.Primitives.StringValues clientText)
            ? clientText.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries) : [];
        string[] ids = query.TryGetValue("ids", out Microsoft.Extensions.Primitives.StringValues idText)
            ? idText.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries) : [];
        if (clients.Length == 0 && ids.Length == 0)
        {
            refuse(400, "MissingQueryParams");
            return;
        }

        if (ids.Length > 0 && !_async.AnyKnown(ids))
        {
            refuse(400, "NoMatchForQueryParams");
            return;
        }

        if (ids.Length == 0 && !clients.Any(_async.KnowsClient))
        {
            refuse(404, "ClientNotFound");
            return;
        }

        // The collection waits until one of the requests changes after since, as MPS's does.
        while (true)
        {
            Task changed = _async.WaitForChangeAsync(context.RequestAborted);
            List<AsyncRequest> matched = _async.Select(clients, ids, since);
            if (matched.Count > 0)
            {
                await WriteJsonAsync(context, _async.CollectionJson(matched)).ConfigureAwait(false);
                return;
            }

            try
            {
                await changed.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
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
