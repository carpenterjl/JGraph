using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using JGraph.Core.Model;
using JGraph.Scripting;
using Microsoft.Web.WebView2.Core;

namespace JGraph.Controls.Web;

/// <summary>
/// One uihtml's page in one WebView2 (U9b): it loads the model's source, serves the page's folder by
/// R2025b's rules (<see cref="UiHtmlFolderServer"/>), runs the bridge script, and carries messages
/// both ways. All its work runs on the WebView2's UI thread; <c>post</c> gets work there from the
/// script thread, which is where the model's events are raised.
/// <para>
/// The protocol, after the bridge script's <c>loaded</c>: the page gets <c>init</c> with the model's
/// Data and runs <c>setup</c>; on <c>setup</c> the page is ready - a DataChanged follows when the Data
/// is not <c>[]</c>, then the events posted while it loaded, then whatever is posted from then on.
/// </para>
/// </summary>
public sealed class HtmlPageBridge : IDisposable
{
    /// <summary>The made-up host a page is served from; nothing leaves the machine for it.</summary>
    public const string Host = "uihtml.jgraph.localhost";

    /// <summary>Markup longer than this goes through a file: NavigateToString takes at most 2 MB.</summary>
    private const int MarkupLimit = 1_500_000;

    private readonly UiHtmlModel _html;
    private readonly CoreWebView2 _web;
    private readonly Action<Action> _post;
    private string _token = string.Empty;
    private string? _folder;
    private string? _scratch;
    private bool _ready;
    private bool _disposed;

    public HtmlPageBridge(UiHtmlModel html, CoreWebView2 web, Action<Action> post)
    {
        _html = html;
        _web = web;
        _post = post;
    }

    /// <summary>Sets the WebView2 up and loads the page. On the UI thread.</summary>
    public async Task StartAsync(bool devTools)
    {
        CoreWebView2Settings settings = _web.Settings;
        settings.AreDevToolsEnabled = devTools;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = true;
        settings.IsStatusBarEnabled = false;
        settings.AreDefaultContextMenusEnabled = devTools;
        _web.AddWebResourceRequestedFilter($"https://{Host}/*", CoreWebView2WebResourceContext.All);
        _web.WebResourceRequested += Serve;
        _web.WebMessageReceived += Received;
        await _web.AddScriptToExecuteOnDocumentCreatedAsync(WebView2Runtime.BridgeScript);
        if (_disposed)
        {
            return;
        }

        _html.MessagesPosted += OnPosted;
        _html.SourceChanged += OnSourceChanged;
        Load();
    }

    private void OnPosted(UiHtmlModel html) => _post(Flush);

    private void OnSourceChanged(UiHtmlModel html) => _post(Load);

    /// <summary>Loads what the model shows now.</summary>
    private void Load()
    {
        if (_disposed)
        {
            return;
        }

        _ready = false;
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        string? file = _html.SourceFile;
        string markup = _html.Source;
        if (file is null && markup.Length > MarkupLimit)
        {
            _scratch ??= Path.Combine(Path.GetTempPath(), "JGraph-uihtml-" + _token);
            Directory.CreateDirectory(_scratch);
            file = Path.Combine(_scratch, "page.html");
            File.WriteAllText(file, markup);
        }

        if (file is not null)
        {
            _folder = Path.GetDirectoryName(file);
            _web.Navigate($"https://{Host}/static/{_token}/{Uri.EscapeDataString(Path.GetFileName(file))}");
        }
        else
        {
            _folder = null;
            _web.NavigateToString(markup);
        }
    }

    private void Serve(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        string prefix = $"/static/{_token}/";
        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        UiHtmlFolderServer.Answer answer = _folder is not null && path.StartsWith(prefix, StringComparison.Ordinal)
            ? UiHtmlFolderServer.Resolve(_folder, path[prefix.Length..], e.Request.Method)
            : new UiHtmlFolderServer.Answer(404, null, null);
        CoreWebView2Environment environment = _web.Environment;
        switch (answer.Status)
        {
            case 200:
                Stream? body = string.Equals(e.Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : new MemoryStream(File.ReadAllBytes(answer.File!));
                string headers = (answer.ContentType is { } type ? $"Content-Type: {type}\r\n" : string.Empty) + "Cache-Control: no-store";
                e.Response = environment.CreateWebResourceResponse(body, 200, "OK", headers);
                break;
            case 501:
                e.Response = environment.CreateWebResourceResponse(null, 501, "Not Implemented", string.Empty);
                break;
            default:
                e.Response = environment.CreateWebResourceResponse(
                    new MemoryStream(System.Text.Encoding.UTF8.GetBytes(UiHtmlFolderServer.NotFoundPage)), 404, "Not Found", "Content-Type: text/html");
                break;
        }
    }

    private void Received(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        string kind;
        JsonElement message;
        try
        {
            using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);
            message = document.RootElement.Clone();
            kind = message.TryGetProperty("k", out JsonElement k) ? k.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            return;
        }

        switch (kind)
        {
            case "loaded":
                _web.PostWebMessageAsJson(JsonSerializer.Serialize(new { k = "init", json = _html.DataJson }));
                break;

            case "setup":
                _ready = true;
                if (_html.DataJson != UiHtmlModel.EmptyJson)
                {
                    Deliver(new UiHtmlMessage(null, _html.DataJson));
                }

                foreach (UiHtmlMessage owed in _html.TakePostedAfterLoad())
                {
                    Deliver(owed);
                }

                break;

            case "data" when message.TryGetProperty("json", out JsonElement json) && json.ValueKind == JsonValueKind.String:
                ScriptGraphicsCallbacks.NotifyComponent(_html, "htmldata", json.GetString()!);
                break;

            case "event" when message.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String:
                string? data = message.TryGetProperty("json", out JsonElement d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                ScriptGraphicsCallbacks.NotifyComponent(_html, "htmlevent", new UiHtmlEvent(name.GetString()!, data));
                break;
        }
    }

    private void Flush()
    {
        if (_disposed || !_ready)
        {
            return; // what waits is delivered when the page has run setup
        }

        foreach (UiHtmlMessage message in _html.TakePosted())
        {
            Deliver(message);
        }
    }

    private void Deliver(UiHtmlMessage message)
    {
        string text = message.EventName is null
            ? JsonSerializer.Serialize(new { k = "data", json = message.Json ?? UiHtmlModel.EmptyJson })
            : JsonSerializer.Serialize(new { k = "event", name = message.EventName, json = message.Json });
        _web.PostWebMessageAsJson(text);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _html.MessagesPosted -= OnPosted;
        _html.SourceChanged -= OnSourceChanged;
        _web.WebResourceRequested -= Serve;
        _web.WebMessageReceived -= Received;
        if (_scratch is not null)
        {
            try
            {
                Directory.Delete(_scratch, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
