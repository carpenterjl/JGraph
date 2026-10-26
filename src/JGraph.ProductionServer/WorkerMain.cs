using System.Collections.Concurrent;
using JGraph.Runtime;

namespace JGraph.ProductionServer;

/// <summary>
/// The worker process, <c>jgraph-mps worker</c> (compiler and SDK plan, stage K14): a JGraph Runtime host
/// that opens the archives the server deploys and calls their functions for the requests it is
/// handed, one at a time, on the engine thread. Its standard output carries the protocol and its
/// console the engine's output, which goes to standard error for the server's log and its metrics
/// (stage K15).
/// </summary>
internal static class WorkerMain
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // The protocol takes standard output as a stream before the console is pointed at standard
        // error, so nothing a function prints lands between frames.
        using Stream input = Console.OpenStandardInput();
        using Stream output = Console.OpenStandardOutput();
        Console.SetOut(Console.Error);
        // A fault on any thread names itself in the server's log before the process ends.
        AppDomain.CurrentDomain.UnhandledException += static (_, e) => Console.Error.WriteLine("[unhandled] " + e.ExceptionObject);
        JgRuntime.Initialize();

        var components = new Dictionary<string, Component>(StringComparer.Ordinal);
        var running = new ConcurrentDictionary<long, CancellationTokenSource>();
        var writeLock = new SemaphoreSlim(1, 1);
        async Task Send(WorkerProtocol.Reply reply)
        {
            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WorkerProtocol.WriteAsync(output, reply, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeLock.Release();
            }
        }

        while (true)
        {
            WorkerProtocol.Request? request;
            try
            {
                request = await WorkerProtocol.ReadAsync<WorkerProtocol.Request>(input, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                Console.Error.WriteLine("[request stream] " + ex.GetType().Name + ": " + ex.Message);
                break;
            }

            if (request is null || request.Op == "shutdown")
            {
                break;
            }

            switch (request.Op)
            {
                case "deploy":
                    try
                    {
                        if (components.Remove(request.Name!, out Component? old))
                        {
                            old.Dispose();
                        }

                        // A fresh runtime, its beep on, as muserve's worker starts (k15_server: quiet answers on).
                        Component component = Component.Open(request.Path!, ownSession: true, cacheFolderName: request.Folder, cacheDirectory: request.CacheRoot);
                        components[request.Name!] = component;
                        await Send(new WorkerProtocol.Reply(request.Id, 200, "OK", Functions: [.. component.Functions])).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        await Send(new WorkerProtocol.Reply(request.Id, 500, ex.Message)).ConfigureAwait(false);
                    }

                    break;
                case "undeploy":
                    if (components.Remove(request.Name!, out Component? retired))
                    {
                        retired.Dispose();
                    }

                    await Send(new WorkerProtocol.Reply(request.Id, 200, "OK")).ConfigureAwait(false);
                    break;
                case "cancel":
                    if (running.TryGetValue(request.Target, out CancellationTokenSource? cancel))
                    {
                        cancel.Cancel();
                    }

                    break;
                case "call":
                case "web":
                {
                    if (!components.TryGetValue(request.Name!, out Component? component))
                    {
                        await Send(new WorkerProtocol.Reply(request.Id, 404, "ComponentNotFound Component not found.")).ConfigureAwait(false);
                        break;
                    }

                    var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    running[request.Id] = source;
                    // The call runs on the engine thread; this loop goes on reading, so a cancel can arrive.
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (request.Op == "web")
                            {
                                WorkerProtocol.WebRequest web = request.Web!;
                                MpsWebReply reply = await MpsCalls.WebAsync(
                                    component, request.Function!, web.Method, web.Path, web.HttpVersion,
                                    [.. web.Headers.Select(static h => (h[0], h[1]))], Convert.FromBase64String(web.BodyBase64), source.Token).ConfigureAwait(false);
                                await Send(new WorkerProtocol.Reply(request.Id, reply.Status, reply.Reason,
                                    WebHeaders: [.. reply.Headers.Select(static h => new[] { h.Name, h.Value })],
                                    WebBodyBase64: Convert.ToBase64String(reply.Body), WebRefused: reply.Refused)).ConfigureAwait(false);
                            }
                            else
                            {
                                MpsReply reply = await MpsCalls.CallAsync(component, request.Function!, request.Body ?? "", request.HideStack, source.Token).ConfigureAwait(false);
                                await Send(new WorkerProtocol.Reply(request.Id, reply.Status, reply.Reason, reply.Body)).ConfigureAwait(false);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            await Send(new WorkerProtocol.Reply(request.Id, 500, "RequestCancelled Request was cancelled.")).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            await Send(new WorkerProtocol.Reply(request.Id, 500, "Internal Server Error " + ex.Message.ReplaceLineEndings(" "))).ConfigureAwait(false);
                        }
                        finally
                        {
                            running.TryRemove(request.Id, out _);
                            source.Dispose();
                        }
                    }, CancellationToken.None);
                    break;
                }
            }
        }

        foreach (Component component in components.Values)
        {
            component.Dispose();
        }

        JgRuntime.Terminate();
        return 0;
    }
}
