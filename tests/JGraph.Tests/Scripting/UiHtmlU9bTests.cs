using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// U9b of the app-building plan (ADR 0208): <c>uihtml</c>. What a script sees is held by the parity
/// fixtures (<c>u9b_*</c>, the bridge through a stand-in page); these hold the parts around them —
/// what a page may load, how numbers are written, which page a component keeps, what is sent and
/// when, the warning with no browser — and one real WebView2 page, when the runtime is installed.
/// </summary>
[Collection("JG facade")]
public class UiHtmlU9bTests : IAsyncLifetime
{
    private readonly RecordingScriptOutput _output = new();
    private JgsReplSession _session = null!;
    private IUiHtmlPageHost? _hiddenBefore;

    public Task InitializeAsync()
    {
        JG.Reset();
        _hiddenBefore = UiHtmlPages.Hidden;
        _session = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(
            new ScriptContext(_output, (_, _) => { })));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        ScriptEventQueue.Flush();
        await _session.DisposeAsync();
        UiHtmlPages.Hidden = _hiddenBefore;
        JG.Reset();
    }

    private async Task Exec(string code)
    {
        ScriptRunResult result = await _session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private static UiHtmlModel TheHtml() =>
        JG.FigureNumbers.Select(n => JG.TryGetFigure(n, out FigureModel f) ? f : null)
            .OfType<FigureModel>().SelectMany(f => f.Components).OfType<UiHtmlModel>().Single();

    // --- what a page may load -------------------------------------------------------------------

    [Fact]
    public void APageLoadsWhatR2025bServes_FromItsFolderAndBelow()
    {
        string folder = Path.Combine(Path.GetTempPath(), "jgraph-u9b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        try
        {
            foreach (string name in new[] { "a.js", "a.txt", "a.mjs", "a.png", "a.webp", "noext", ".hidden", "a.wav", "page.html" })
            {
                File.WriteAllText(Path.Combine(folder, name), "x");
            }

            File.WriteAllText(Path.Combine(folder, "sub", "index.html"), "<p>i</p>");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "jgraph-u9b-outside.js"), "x");

            UiHtmlFolderServer.Answer Get(string path, string method = "GET") => UiHtmlFolderServer.Resolve(folder, path, method);
            Assert.Equal((200, "text/javascript"), (Get("a.js").Status, Get("a.js").ContentType));
            Assert.Equal(404, Get("a.txt").Status); // R2025b does not serve text files (probe u9b_types)
            Assert.Equal(404, Get("a.mjs").Status);
            Assert.Equal(404, Get("a.webp").Status);
            Assert.Equal((200, "image/png"), (Get("a.png").Status, Get("a.png").ContentType));
            Assert.Equal((200, (string?)null), (Get("noext").Status, Get("noext").ContentType));
            Assert.Equal((200, (string?)null), (Get("a.wav").Status, Get("a.wav").ContentType));
            Assert.Equal(404, Get(".hidden").Status);
            Assert.Equal(200, Get("A.JS").Status);
            Assert.Equal(200, Get("a.js/").Status);
            Assert.Equal(Path.Combine(folder, "sub", "index.html"), Get("sub/").File);
            Assert.Equal(Path.Combine(folder, "sub", "index.html"), Get("sub").File);
            Assert.Equal(200, Get("sub/../a.js").Status);
            Assert.Equal(404, Get("../jgraph-u9b-outside.js").Status); // nothing above the page's folder
            Assert.Equal(404, Get("missing.js").Status);
            Assert.Equal(501, Get("a.js", "POST").Status);
            Assert.Equal(200, Get("a.js", "HEAD").Status);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            File.Delete(Path.Combine(Path.GetTempPath(), "jgraph-u9b-outside.js"));
        }
    }

    // --- numbers ------------------------------------------------------------------------------

    [Theory]
    [InlineData(1.0, "1")]
    [InlineData(-0.0, "-0")]
    [InlineData(0.1, "0.1")]
    [InlineData(1.0 / 3, "0.33333333333333331")]
    [InlineData(999999.0, "999999")]
    [InlineData(1e6, "1.0E+6")]
    [InlineData(1234567.5, "1.2345675E+6")]
    [InlineData(1e15, "1.0E+15")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(1e-5, "1E-5")]
    [InlineData(1.5e-5, "1.5E-5")]
    [InlineData(5e-324, "4.94065645841247E-324")]
    [InlineData(100000.0 + 1.0 / 3, "100000.33333333333")]
    public void ADoubleIsWrittenAsR2025bsJsonencodeWritesIt(double x, string text) =>
        Assert.Equal(text, JgsJson.FormatDouble(x));

    [Theory]
    [InlineData(0.1f, "0.1")]
    [InlineData(1f / 3, "0.333333343")]
    [InlineData(3.14159265f, "3.14159274")]
    [InlineData(1e10f, "1.0E+10")]
    [InlineData(1e-38f, "1E-38")]
    public void ASingleIsWrittenWithItsOwnDigits(float x, string text) =>
        Assert.Equal(text, JgsJson.FormatSingle(x));

    // --- which page a component keeps ------------------------------------------------------------

    private sealed class FakePage(bool inWindow) : IUiHtmlPage
    {
        public bool InWindow { get; } = inWindow;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void AWindowsPageReplacesAHiddenOne_AndAHiddenOneNeverReplacesAWindows()
    {
        var html = new UiHtmlModel();
        var hidden = new FakePage(inWindow: false);
        var window = new FakePage(inWindow: true);
        Assert.Null(html.Attach(hidden));
        Assert.Same(hidden, html.Attach(window));
        Assert.Same(window, html.Page);
        var late = new FakePage(inWindow: false);
        Assert.Same(late, html.Attach(late)); // refused: the caller closes it
        Assert.Same(window, html.Page);
        html.Detach(window);
        Assert.Null(html.Page);
    }

    // --- what is sent, and when ------------------------------------------------------------------

    /// <summary>A host whose pages only listen: what the model posts, in order.</summary>
    private sealed class ListeningHost : IUiHtmlPageHost
    {
        public List<UiHtmlMessage> Heard { get; } = [];

        public int Opened { get; private set; }

        public IUiHtmlPage? Open(UiHtmlModel html)
        {
            // A page is opened after the drain point posts, so what waits is taken at once, as a
            // real page takes it once it has run setup.
            Opened++;
            Heard.AddRange(html.TakePosted());
            html.MessagesPosted += m => Heard.AddRange(m.TakePosted());
            return new FakePage(inWindow: false);
        }
    }

    [Fact]
    public async Task WritesAndEventsWaitForADrainPoint_DataGoesFirst_AndUnchangedJsonIsNotSent()
    {
        var host = new ListeningHost();
        UiHtmlPages.Hidden = host;
        await Exec("""
            f = uifigure('Visible', 'off');
            h = uihtml(f, 'HTMLSource', '<p>page</p>');
            sendEventToHTMLSource(h, 'ping', [1;2;3]);
            h.Data = struct('a', {1, 2});
            """);
        Assert.Empty(host.Heard); // nothing leaves before a drain point
        await Exec("drawnow;");
        Assert.Equal(1, host.Opened);
        Assert.Equal(
            [new UiHtmlMessage(null, """[{"a":1},{"a":2}]"""), new UiHtmlMessage("ping", "[1,2,3]")],
            host.Heard);
        host.Heard.Clear();
        await Exec("h.Data = struct('a', {1, 2}); h.Data = 7; h.Data = 7; drawnow;");
        Assert.Equal([new UiHtmlMessage(null, "7")], host.Heard);
        Assert.Equal("7", TheHtml().DataJson);
    }

    [Fact]
    public async Task DataNoEncoderCanWriteWarnsAtTheDrainPoint_AndIsNotSent()
    {
        var host = new ListeningHost();
        UiHtmlPages.Hidden = host;
        await Exec("""
            f = uifigure('Visible', 'off');
            h = uihtml(f, 'HTMLSource', '<p>page</p>');
            lastwarn('');
            h.Data = 1 + 2i;
            [~, before] = lastwarn;
            drawnow;
            [~, after] = lastwarn;
            disp(['before=' before]);
            disp(['after=' after]);
            sendEventToHTMLSource(h, 'x', sparse(1));
            drawnow;
            [~, sent] = lastwarn;
            disp(['sent=' sent]);
            """);
        Assert.Contains("before=", _output.NormalLines);
        Assert.Contains("after=MATLAB:json:UnsupportedComplexDataType", _output.NormalLines);
        Assert.Contains("sent=MATLAB:json:UnsupportedSparseDataType", _output.NormalLines);
        Assert.Empty(host.Heard);
    }

    /// <summary>A host that cannot open anything, as with no WebView2 runtime.</summary>
    private sealed class NoBrowser : IUiHtmlPageHost
    {
        public IUiHtmlPage? Open(UiHtmlModel html) => null;
    }

    [Fact]
    public async Task WithNoBrowserAComponentKeepsItsStateAndTheSessionIsToldOnce()
    {
        UiHtmlPages.Hidden = new NoBrowser();
        await Exec("""
            f = uifigure('Visible', 'off');
            h1 = uihtml(f, 'HTMLSource', '<p>one</p>', 'Data', 3);
            h2 = uihtml(f, 'HTMLSource', '<p>two</p>');
            drawnow;
            sendEventToHTMLSource(h1, 'e', 1);
            drawnow;
            disp(h1.Data);
            """);
        Assert.Contains("3", _output.NormalLines.Select(static l => l.Trim()));
        string all = _output.NormalText + _output.ErrorText;
        Assert.Equal(1, all.Split("An HTML component shows nothing here").Length - 1);
    }

    [Fact]
    public async Task APagesDataIsWrittenBeforeItsCallbackIsDecided_AndAnEventsVectorsAreRows()
    {
        UiHtmlPages.Hidden = new ListeningHost();
        await Exec("""
            f = uifigure('Visible', 'off');
            h = uihtml(f, 'HTMLSource', '<p>page</p>');
            drawnow;
            """);
        UiHtmlModel html = TheHtml();
        ScriptGraphicsCallbacks.NotifyComponent(html, JgsUiHtml.DataAction, "[1,2,3]");
        ScriptGraphicsCallbacks.NotifyComponent(html, JgsUiHtml.EventAction, new UiHtmlEvent("\"go\"", "[1,2,3]"));
        await Exec("""
            h.HTMLEventReceivedFcn = @(s, e) disp(sprintf('%s %s', e.HTMLEventName, mat2str(e.HTMLEventData)));
            drawnow;
            disp(mat2str(h.Data));
            """);
        Assert.Contains("go [1 2 3]", _output.NormalLines);
        Assert.Contains("[1;2;3]", _output.NormalLines);
        Assert.Equal("[1,2,3]", html.DataJson);
    }

    // --- a real page ----------------------------------------------------------------------------

    /// <summary>
    /// The echo page in WebView2, in a hidden window: it starts, hears MATLAB's Data and events and
    /// answers with its own. Runs when the WebView2 runtime is installed (Windows 11 ships it); passes
    /// without asserting anything where it is not, as the U7b MATLAB cross-check does without MATLAB.
    /// </summary>
    [Fact]
    public async Task TheEchoPageRunsInARealWebView2_WhenTheRuntimeIsThere()
    {
        UiHtmlPages.Hidden = null;
        UiHtmlPages.TryLoadHidden();
        if (UiHtmlPages.Hidden is null)
        {
            return;
        }

        string echo = Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers", EchoHtmlPageHost.EchoFile);
        Assert.True(File.Exists(echo), echo);
        await Exec($$"""
            global SEEN
            SEEN = {};
            f = uifigure('Visible', 'off');
            h = uihtml(f, 'Data', 5, 'HTMLSource', '{{echo}}');
            h.HTMLEventReceivedFcn = @(s, e) eval('global SEEN; SEEN{end+1} = [e.HTMLEventName '' '' char(e.HTMLEventData)];');
            h.DataChangedFcn = @(s, e) eval('global SEEN; SEEN{end+1} = [''data '' mat2str(s.Data)];');
            t = tic;
            while toc(t) < 30 && ~any(startsWith(SEEN, 'echo'))
                pause(0.05);
            end
            sendEventToHTMLSource(h, 'ping', [1;2;3]);
            sendEventToHTMLSource(h, 'set', '[4,5]');
            t = tic;
            while toc(t) < 15 && ~any(startsWith(SEEN, 'setdone'))
                pause(0.05);
            end
            for k = 1:numel(SEEN)
                disp(['seen: ' SEEN{k}]);
            end
            delete(f);
            """);
        string[] seen = [.. _output.NormalLines.Where(static l => l.StartsWith("seen: ", StringComparison.Ordinal))];
        Assert.Equal(
            ["seen: ready 5", "seen: echo 5", "seen: pong [1,2,3]", "seen: data [4;5]", "seen: setdone [4,5]"],
            seen);
    }
}
