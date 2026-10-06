using System.Runtime.CompilerServices;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A <c>uihtml</c> as the script sees it (app-building plan, section L, stage U9b): its <c>Data</c>,
/// what <c>sendEventToHTMLSource</c> queues, the <c>HTMLSource</c> rules, and the two directions of
/// the bridge.
/// <para>
/// MATLAB → page. A write of <c>Data</c> and a <c>sendEventToHTMLSource</c> only queue; at the next
/// drain point (<c>drawnow</c>, <c>pause</c>, a wait) <see cref="Flush"/> encodes them, Data first —
/// R2025b sends a Data written after a ping before the ping (probe <c>u9b_bridge</c>) — and posts
/// them to the model, where whatever hosts the page takes them. Data goes through <c>jsonencode</c>
/// and is not sent when its JSON is what the page holds already; an event goes through R2025b's
/// other encoder (<see cref="JgsJson.EncodeEvent"/>). A value neither can write is R2025b's warning
/// at the drain point, and nothing is sent (R2025b's bridge goes dead for the session after an
/// event it cannot write; this one does not).
/// </para>
/// <para>
/// Page → MATLAB. A host reports what the page did through
/// <see cref="ScriptGraphicsCallbacks.NotifyComponent"/> (<see cref="DataAction"/> with the JSON,
/// <see cref="EventAction"/> with a <see cref="UiHtmlEvent"/>); <see cref="Prepare"/> writes Data
/// before deciding on the callback, as for any component.
/// </para>
/// </summary>
internal static class JgsUiHtml
{
    /// <summary>A page set its <c>Data</c>; the value is the JSON it set.</summary>
    public const string DataAction = "htmldata";

    /// <summary>A page called <c>sendEventToMATLAB</c>; the value is a <see cref="UiHtmlEvent"/>.</summary>
    public const string EventAction = "htmlevent";

    private const string Prefix = "matlab.ui.eventdata.";

    private sealed class State
    {
        public JgsValue Data = JgsMatrix.FromColumnMajor([], 0, 0);
        public bool DataDirty;
        public readonly List<(string Name, JgsValue? Data)> Events = [];
    }

    private static readonly ConditionalWeakTable<UiHtmlModel, State> States = new();

    // The components with something to send or a page to open. A session's statements need not all
    // run on one thread, so the list is the process's, under a lock.
    private static readonly List<UiHtmlModel> Pending = [];

    // The sessions told that no page can be shown, each once.
    private static readonly ConditionalWeakTable<JGraphScriptGlobals, object> Told = new();

    private static State StateOf(UiHtmlModel html) => States.GetValue(html, static _ => new State());

    private static void MarkPending(UiHtmlModel html)
    {
        lock (Pending)
        {
            if (!Pending.Contains(html))
            {
                Pending.Add(html);
            }
        }
    }

    /// <summary>What <c>Data</c> reads as.</summary>
    public static JgsValue DataOf(UiHtmlModel html) => StateOf(html).Data;

    /// <summary>Writes <c>Data</c>; the page hears of it at the next drain point.</summary>
    public static void SetData(UiHtmlModel html, JgsValue value)
    {
        State state = StateOf(html);
        state.Data = JgsValue.Share(value);
        state.DataDirty = true;
        MarkPending(html);
    }

    /// <summary>Queues an event for the page, sent at the next drain point.</summary>
    public static void Send(UiHtmlModel html, string name, JgsValue? data)
    {
        StateOf(html).Events.Add((name, data is null ? null : JgsValue.Share(data)));
        MarkPending(html);
    }

    /// <summary>A copy's Data is the original's (copyobj).</summary>
    public static void CopyState(UiHtmlModel from, UiHtmlModel to)
    {
        SetData(to, DataOf(from));
    }

    // --- HTMLSource -----------------------------------------------------------------------------

    private const string NotFound =
        "You have specified a file that cannot be found.\nSpecify HTML markup, a file name that is on the MATLAB path, or use a full or relative path.";

    private const string IsUrl =
        "You have specified a URL, which is not supported.\nSpecify HTML markup, a file name that is on the MATLAB path, or use a full or relative path.";

    /// <summary>
    /// Makes <paramref name="text"/> the component's page, R2025b's way (probe <c>u9b_forms</c>): a
    /// URL is refused; text naming a file — beside the current folder, on the path, or in full,
    /// <c>file:</c> URLs included — is that file, whatever its type; other text ending in
    /// <c>.html</c> or <c>.htm</c>, in lower case, is a file that cannot be found; anything else is
    /// markup.
    /// </summary>
    public static void SetSource(UiHtmlModel html, string text, Func<string, string, Exception> refuse)
    {
        string? file = null;
        if (text.Length > 0)
        {
            if (LooksLikeUrl(text))
            {
                throw refuse("MATLAB:ui:HTML:invalidHTMLSource", IsUrl);
            }

            file = FileNamed(text);
            // The ending is matched as written: 'missing.HTM' is markup (probe u9b_forms).
            if (file is null && (text.EndsWith(".html", StringComparison.Ordinal) || text.EndsWith(".htm", StringComparison.Ordinal)))
            {
                throw refuse("MATLAB:ui:HTML:invalidHTMLSource", NotFound);
            }
        }

        html.SetSource(text, file);
        MarkPending(html);
    }

    private static bool LooksLikeUrl(string text) =>
        text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    private static string? FileNamed(string text)
    {
        string path = text;
        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || !uri.IsFile)
            {
                return null;
            }

            path = uri.LocalPath;
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.Contains('<') || path.Contains('>'))
        {
            return null;
        }

        try
        {
            JgsCallbackDispatcher? dispatcher = JgsCallbackDispatcher.Current;
            string beside = dispatcher?.Interpreter?.Host is { } host ? host.Resolve(path) : Path.GetFullPath(path);
            if (File.Exists(beside))
            {
                return Path.GetFullPath(beside);
            }

            if (!Path.IsPathRooted(path) && path == Path.GetFileName(path) && dispatcher?.Interpreter?.FunctionPath is { } search)
            {
                foreach (string folder in search.Folders)
                {
                    string candidate = Path.Combine(folder, path);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return null;
    }

    // --- MATLAB to the page -----------------------------------------------------------------------

    /// <summary>
    /// Sends what this thread's components queued, and opens a page for any that needs one. Called
    /// at every drain point, before the queue is read, so a page's answer to what is sent here can
    /// be heard in the same drain.
    /// </summary>
    public static void Flush(JGraphScriptGlobals? host)
    {
        UiHtmlModel[] now;
        lock (Pending)
        {
            if (Pending.Count == 0)
            {
                return;
            }

            now = [.. Pending];
            Pending.Clear();
        }

        foreach (UiHtmlModel html in now)
        {
            if (html.IsGone)
            {
                continue;
            }

            State state = StateOf(html);
            if (state.DataDirty)
            {
                state.DataDirty = false;
                try
                {
                    string json = JgsJson.Encode(state.Data, JgsJson.Options.Default);
                    if (json != html.DataJson)
                    {
                        html.Post(new UiHtmlMessage(null, json));
                    }
                }
                catch (JgsJson.Unwritable refused)
                {
                    Warn(host, refused.Identifier, refused.Message);
                }
            }

            (string Name, JgsValue? Data)[] events = [.. state.Events];
            state.Events.Clear();
            foreach ((string name, JgsValue? data) in events)
            {
                try
                {
                    html.Post(new UiHtmlMessage(name, data is null ? null : JgsJson.EncodeEvent(data)));
                }
                catch (JgsJson.Unwritable refused)
                {
                    Warn(host, refused.Identifier, refused.Message);
                }
            }

            EnsurePage(html, host);
        }
    }

    private static void Warn(JGraphScriptGlobals? host, string identifier, string text)
    {
        host ??= JgsCallbackDispatcher.Current?.Interpreter?.Host;
        if (host is not null)
        {
            JgsBuiltins.Warn(host, identifier, text);
        }
    }

    /// <summary>
    /// Opens a page for a component that shows something and has none: a hidden one, unless its
    /// figure is about to be shown in a window, whose layer opens its own. With no host at all — no
    /// WebView2 runtime — the component keeps its state, draws nothing, and the session is told once.
    /// </summary>
    private static void EnsurePage(UiHtmlModel html, JGraphScriptGlobals? host)
    {
        if (html.Page is not null || html.Source.Length == 0 || WindowWillShow(html))
        {
            return;
        }

        IUiHtmlPage? page = null;
        string? why = null;
        if (UiHtmlPages.Hidden is null)
        {
            UiHtmlPages.TryLoadHidden();
        }

        if (UiHtmlPages.Hidden is { } hidden)
        {
            try
            {
                page = hidden.Open(html);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                why = ex.Message;
            }
        }
        else
        {
            why = UiHtmlPages.Unavailable;
        }

        if (page is null)
        {
            host ??= JgsCallbackDispatcher.Current?.Interpreter?.Host;
            if (host is not null && !Told.TryGetValue(host, out _))
            {
                Told.Add(host, new object());
                Warn(host, "JGraph:uihtml:noBrowser",
                    $"An HTML component shows nothing here: {why ?? "no page host is available"}");
            }

            return;
        }

        if (html.Attach(page) is { } replaced)
        {
            replaced.Dispose();
        }
    }

    private static bool WindowWillShow(UiHtmlModel html)
    {
        if (!ScriptComponentFrames.HasSink || !UiHtmlPages.WindowsHostPages)
        {
            return false;
        }

        GraphObject? at = html.Parent;
        while (at is not null and not FigureModel)
        {
            at = at.Parent;
        }

        return at is FigureModel { Visible: true };
    }

    // --- the page to MATLAB -------------------------------------------------------------------------

    /// <summary>
    /// What a page did, applied to the component and turned into the callback owed: its Data
    /// written (always, even with no callback), or its event's name and data decoded.
    /// </summary>
    public static GraphicsEvent? Prepare(GraphicsEvent raised, UiHtmlModel html, JgsValue source)
    {
        switch (raised.Action)
        {
            case DataAction when raised.Interim is string json:
            {
                JgsValue decoded;
                try
                {
                    decoded = JgsJson.Decode(json);
                }
                catch (JgsJson.Malformed)
                {
                    return null;
                }

                State state = StateOf(html);
                JgsValue previous = state.Data;
                state.Data = decoded;
                html.NoteDataFromPage(json);
                return raised with
                {
                    Action = "DataChangedFcn",
                    Interim = JgsUiEventData.Make(Prefix + "DataChangedData", source, "DataChanged", new()
                    {
                        ["Data"] = decoded,
                        ["PreviousData"] = previous,
                    }),
                };
            }

            case EventAction when raised.Interim is UiHtmlEvent sent:
            {
                JgsValue name;
                JgsValue data;
                try
                {
                    name = JgsJson.Decode(sent.NameJson);
                    data = sent.DataJson is null ? JgsMatrix.FromColumnMajor([], 0, 0) : JgsJson.DecodeEventData(sent.DataJson);
                }
                catch (JgsJson.Malformed)
                {
                    return null;
                }

                return raised with
                {
                    Action = "HTMLEventReceivedFcn",
                    Interim = JgsUiEventData.Make(Prefix + "HTMLEventReceivedData", source, "HTMLEventReceived", new()
                    {
                        ["HTMLEventName"] = name,
                        ["HTMLEventData"] = data,
                    }),
                };
            }

            default:
                return null;
        }
    }
}
