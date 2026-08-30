using System.Text;
using System.Text.Json;

namespace JGraph.Reporting;

/// <summary>
/// The product's one network call (ADR 0116): POSTs a report as JSON to the relay and reads back its
/// verdict. Redirects stay enabled deliberately — Apps Script answers a POST with a 302 to
/// <c>script.googleusercontent.com</c>, and it is the redirected response that carries the JSON.
/// Success is a 2xx whose body says <c>"ok": true</c>; everything else, exceptions included, comes
/// back as a failed <see cref="BugReportSendResult"/> rather than a throw.
/// </summary>
public sealed class HttpBugReportTransport : IBugReportTransport
{
    // One client for the process, per HttpClient's own guidance; 15 s covers a cold Apps Script
    // spin-up without leaving the dialog's Send hanging for the default 100.
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _url;

    /// <summary>Creates the transport against the relay at <paramref name="url"/>.</summary>
    public HttpBugReportTransport(string url)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
    }

    /// <inheritdoc />
    public bool IsConfigured => BugReportRelay.IsConfigured(_url);

    /// <inheritdoc />
    public async Task<BugReportSendResult> SendAsync(
        BugReportPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!BugReportRelay.IsConfigured(_url))
        {
            return BugReportSendResult.Failed("Bug reporting is not configured in this build.");
        }

        try
        {
            using var content = new StringContent(payload.ToJson(), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await Client
                .PostAsync(_url, content, cancellationToken)
                .ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return BugReportSendResult.Failed(
                    $"The report server answered {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            return Interpret(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return BugReportSendResult.Failed("The report server did not answer in time.");
        }
        catch (OperationCanceledException)
        {
            return BugReportSendResult.Failed("Sending was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return BugReportSendResult.Failed("The report could not be sent: " + ex.Message);
        }
    }

    /// <summary>Reads the relay's JSON verdict; a body that is not the relay's is itself a failure.</summary>
    private static BugReportSendResult Interpret(string body)
    {
        try
        {
            using JsonDocument verdict = JsonDocument.Parse(body);
            if (verdict.RootElement.ValueKind == JsonValueKind.Object
                && verdict.RootElement.TryGetProperty("ok", out JsonElement ok)
                && ok.ValueKind == JsonValueKind.True)
            {
                return BugReportSendResult.Sent;
            }

            string reason = verdict.RootElement.ValueKind == JsonValueKind.Object
                && verdict.RootElement.TryGetProperty("error", out JsonElement error)
                && error.ValueKind == JsonValueKind.String
                ? error.GetString()!
                : "the server did not accept the report";
            return BugReportSendResult.Failed("The report was refused: " + reason + ".");
        }
        catch (JsonException)
        {
            return BugReportSendResult.Failed("The report server gave an answer this build does not understand.");
        }
    }
}
