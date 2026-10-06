using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>What a <c>uihtml</c> shows: a file, served from its folder, or markup; and which load it is.</summary>
/// <param name="File">The full path of the page, or null for markup.</param>
/// <param name="Markup">The markup to show when there is no file.</param>
/// <param name="Epoch">Counts the loads asked for: a page loads again when it changes.</param>
public sealed record UiHtmlFrame(string? File, string Markup, long Epoch);

/// <summary>
/// One message for a page: the JSON its <c>Data</c> now holds (<see cref="EventName"/> null), or an
/// event from <c>sendEventToHTMLSource</c> with its data's JSON (null when it was sent with none).
/// </summary>
public sealed record UiHtmlMessage(string? EventName, string? Json);

/// <summary>
/// What a page sent with <c>sendEventToMATLAB</c>: its name and its data, each as the JSON the
/// page's <c>JSON.stringify</c> wrote; a null <see cref="DataJson"/> is an event with no data.
/// </summary>
public sealed record UiHtmlEvent(string NameJson, string? DataJson);

/// <summary>A page that shows a <see cref="UiHtmlModel"/>: in a window, or in a hidden host.</summary>
public interface IUiHtmlPage : IDisposable
{
    /// <summary>Whether this page belongs to a figure's window (a window's page replaces a hidden one).</summary>
    bool InWindow { get; }
}

/// <summary>
/// MATLAB's <c>uihtml</c> (app-building plan, section L, stage U9b): a web page in a component. The
/// script side writes what the page is (<see cref="SetSource"/>) and posts what it sends
/// (<see cref="Post"/>); whatever hosts the page — a WebView2 in the figure's window, a hidden one
/// under <c>-batch</c>, a test's stand-in — loads it, takes the posted messages and hands what the
/// page sends back to the script's queue. The page side's rules are R2025b's (probes
/// <c>u0_uihtml</c>, <c>u9b_bridge</c>): on a load the page's <c>setup</c> sees <see cref="DataJson"/>,
/// and a <c>DataChanged</c> follows when that is not <c>[]</c>; data posted before the page was ready
/// is superseded by that, events posted before it are delivered after it.
/// </summary>
public sealed class UiHtmlModel : UiComponentModel
{
    /// <summary>The JSON of <c>Data</c>'s default, <c>[]</c>.</summary>
    public const string EmptyJson = "[]";

    private readonly object _gate = new();
    private readonly List<UiHtmlMessage> _posted = [];
    private string _source = string.Empty;
    private string? _file;
    private long _epoch;
    private string _dataJson = EmptyJson;
    private IUiHtmlPage? _page;

    public UiHtmlModel()
        : base("HTML", new Rect2D(100, 100, 100, 100))
    {
    }

    /// <inheritdoc />
    public override UiComponentKind Kind => UiComponentKind.Html;

    /// <summary>The <c>HTMLSource</c> as written: markup, or the name of a file.</summary>
    [Browsable(false)]
    public string Source
    {
        get
        {
            lock (_gate)
            {
                return _source;
            }
        }
    }

    /// <summary>The full path of the file <see cref="Source"/> names, or null when it is markup.</summary>
    [Browsable(false)]
    public string? SourceFile
    {
        get
        {
            lock (_gate)
            {
                return _file;
            }
        }
    }

    /// <summary>Counts the loads asked for (<see cref="UiHtmlFrame.Epoch"/>).</summary>
    [Browsable(false)]
    public long SourceEpoch
    {
        get
        {
            lock (_gate)
            {
                return _epoch;
            }
        }
    }

    /// <summary>The JSON the page's <c>Data</c> holds: what MATLAB last sent, or the page last set.</summary>
    [Browsable(false)]
    public string DataJson
    {
        get
        {
            lock (_gate)
            {
                return _dataJson;
            }
        }
    }

    /// <summary>The page showing this component, if one has been opened.</summary>
    [Browsable(false)]
    public IUiHtmlPage? Page
    {
        get
        {
            lock (_gate)
            {
                return _page;
            }
        }
    }

    /// <summary>Raised, on the thread that posted, when a message waits in <see cref="TakePosted"/>.</summary>
    public event Action<UiHtmlModel>? MessagesPosted;

    /// <summary>Raised, on the script thread, when the page must load again (a new source).</summary>
    public event Action<UiHtmlModel>? SourceChanged;

    /// <summary>
    /// Makes <paramref name="text"/> the page: the file <paramref name="file"/> when it names one,
    /// else the markup itself. Data posted for the old page is dropped; events wait for the new one.
    /// </summary>
    public void SetSource(string text, string? file)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            _source = text;
            _file = file;
            _epoch++;
            _posted.RemoveAll(static m => m.EventName is null);
        }

        Invalidate(InvalidationKind.Ui);
        SourceChanged?.Invoke(this);
    }

    /// <summary>
    /// Posts <paramref name="message"/> for the page. Data's JSON becomes <see cref="DataJson"/> at
    /// once, so a page that loads before the message is taken starts from it.
    /// </summary>
    public void Post(UiHtmlMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
        {
            if (message.EventName is null)
            {
                _dataJson = message.Json ?? EmptyJson;
                _posted.RemoveAll(static m => m.EventName is null);
            }

            _posted.Add(message);
        }

        MessagesPosted?.Invoke(this);
    }

    /// <summary>Records the JSON the page set its <c>Data</c> to: it is the page's already, so nothing is posted.</summary>
    public void NoteDataFromPage(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        lock (_gate)
        {
            _dataJson = json;
        }
    }

    /// <summary>Takes every message posted since the last call, in order.</summary>
    public IReadOnlyList<UiHtmlMessage> TakePosted()
    {
        lock (_gate)
        {
            UiHtmlMessage[] taken = [.. _posted];
            _posted.Clear();
            return taken;
        }
    }

    /// <summary>Takes the posted events only, dropping posted data — what a page that has just run <c>setup</c> is owed.</summary>
    public IReadOnlyList<UiHtmlMessage> TakePostedAfterLoad()
    {
        lock (_gate)
        {
            UiHtmlMessage[] taken = [.. _posted.Where(static m => m.EventName is not null)];
            _posted.Clear();
            return taken;
        }
    }

    /// <summary>
    /// Makes <paramref name="page"/> the one showing this component and answers the page it replaced
    /// (the caller disposes it), or answers <paramref name="page"/> itself when it is refused: a
    /// hidden page never replaces a window's.
    /// </summary>
    public IUiHtmlPage? Attach(IUiHtmlPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        lock (_gate)
        {
            if (_page is { InWindow: true } && !page.InWindow)
            {
                return page;
            }

            IUiHtmlPage? replaced = _page;
            _page = page;
            return ReferenceEquals(replaced, page) ? null : replaced;
        }
    }

    /// <summary>Lets go of <paramref name="page"/> if it is the one showing this component.</summary>
    public void Detach(IUiHtmlPage page)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_page, page))
            {
                _page = null;
            }
        }
    }

    /// <summary>Whether this component is gone: deleted, or in no figure any more.</summary>
    [Browsable(false)]
    public bool IsGone => BeingDeleted || OwningFigure() is not { BeingDeleted: false };

    private FigureModel? OwningFigure()
    {
        GraphObject? at = Parent;
        while (at is not null and not FigureModel)
        {
            at = at.Parent;
        }

        return at as FigureModel;
    }

    /// <inheritdoc />
    public override UiComponentFrame Snapshot()
    {
        lock (_gate)
        {
            return Common() with { Html = new UiHtmlFrame(_file, _file is null ? _source : string.Empty, _epoch) };
        }
    }
}
