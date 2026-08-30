using System.Net;
using System.Text;
using System.Text.Json;
using JGraph.Reporting;
using Xunit;

namespace JGraph.Tests.Reporting;

/// <summary>
/// The product's one network call, exercised against a loopback <see cref="HttpListener"/> playing
/// the relay: the request must be a JSON POST carrying the payload, and every outcome — accepted,
/// refused, server error — must come back as a result, never as a throw.
/// </summary>
public class HttpBugReportTransportTests
{
    private static BugReportPayload Sample() => BugReportBuilder.Build(
        "t", "It broke.", "a@b.c", "0.2.0", "Test OS 1.0",
        new DateTimeOffset(2026, 8, 30, 14, 0, 0, TimeSpan.Zero));

    /// <summary>Serves exactly one request with the given status and body, capturing what arrived.</summary>
    private static async Task<(BugReportSendResult Result, string Method, string? ContentType, string Body)> RoundTrip(
        int status, string responseBody)
    {
        // Port 0 is not a thing HttpListener does; probe for a free port instead.
        using var listener = new HttpListener();
        int port = FreePort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/relay/");
        listener.Start();

        Task<HttpListenerContext> serving = listener.GetContextAsync();
        var transport = new HttpBugReportTransport($"http://127.0.0.1:{port}/relay/");
        Task<BugReportSendResult> sending = transport.SendAsync(Sample());

        HttpListenerContext context = await serving;
        string method = context.Request.HttpMethod;
        string? contentType = context.Request.ContentType;
        string body;
        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync();
        }

        context.Response.StatusCode = status;
        byte[] answer = Encoding.UTF8.GetBytes(responseBody);
        await context.Response.OutputStream.WriteAsync(answer);
        context.Response.Close();

        return (await sending, method, contentType, body);
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    [Fact]
    public async Task PostsThePayloadAsJsonAndReadsOkTrue()
    {
        (BugReportSendResult result, string method, string? contentType, string body) =
            await RoundTrip(200, "{\"ok\":true}");

        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Equal("POST", method);
        Assert.StartsWith("application/json", contentType, StringComparison.OrdinalIgnoreCase);

        using JsonDocument sent = JsonDocument.Parse(body);
        Assert.Equal("It broke.", sent.RootElement.GetProperty("description").GetString());
        Assert.Equal("JGraph Bug Report: t - 30082026 - 0.2.0", sent.RootElement.GetProperty("subject").GetString());
    }

    [Fact]
    public async Task ARefusalComesBackWithItsReason()
    {
        (BugReportSendResult result, _, _, _) = await RoundTrip(200, "{\"ok\":false,\"error\":\"missing fields\"}");

        Assert.False(result.Ok);
        Assert.Contains("missing fields", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AServerErrorIsAFailureNotAThrow()
    {
        (BugReportSendResult result, _, _, _) = await RoundTrip(500, "oops");

        Assert.False(result.Ok);
        Assert.Contains("500", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnswerThatIsNotTheRelaysIsAFailure()
    {
        // A captive portal or a proxy answering 200 with HTML must not count as "sent".
        (BugReportSendResult result, _, _, _) = await RoundTrip(200, "<html>signed out</html>");

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task NothingListeningIsAFailureNotAThrow()
    {
        var transport = new HttpBugReportTransport($"http://127.0.0.1:{FreePort()}/relay/");

        BugReportSendResult result = await transport.SendAsync(Sample());

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task AnUnconfiguredTransportRefusesWithoutTouchingTheNetwork()
    {
        var transport = new HttpBugReportTransport("https://REPLACE-AFTER-DEPLOY.invalid");

        Assert.False(transport.IsConfigured);
        BugReportSendResult result = await transport.SendAsync(Sample());
        Assert.False(result.Ok);
        Assert.Contains("not configured", result.Error, StringComparison.Ordinal);
    }
}
