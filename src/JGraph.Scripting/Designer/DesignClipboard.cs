using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>
/// App Designer's clipboard (App Designer window plan, stage W5; <c>AppDesignerClipboard</c>): what
/// Cut or Copy took from an app's canvas, held for a paste into the same app or another one the
/// window has open. One clipboard serves every app of a designer window, as App Designer's one
/// service does; it is not the system's clipboard, which App Designer does not use for components.
/// </summary>
public sealed class DesignClipboard
{
    private readonly Dictionary<(DesignDocument Document, DesignComponent Container), int> _pasteCounts = [];

    /// <summary>Whether the clipboard holds components (<c>hasContents</c>).</summary>
    public bool HasContents => Clip is not null;

    /// <summary>Whether the contents were cut rather than copied (<c>getContentAction</c> is <c>CUT</c>).</summary>
    public bool WasCut { get; internal set; }

    /// <summary>Whether the clipboard holds components, every one at its top of <paramref name="type"/> (a tree node's menu's Paste, W5c).</summary>
    public bool HoldsOnly(string type) => Clip is { } clip && clip.Items.All(i => i.Copy.Type == type);

    /// <summary>The code names of what the clipboard holds, at the top, in its order; empty when it holds nothing.</summary>
    public IReadOnlyList<string> CodeNames => Clip?.Items.Select(static i => i.Copy.CodeName).ToArray() ?? [];

    /// <summary>Raised when the contents change, and when a paste or its undo changes the paste counts.</summary>
    public event EventHandler? Changed;

    internal DesignClip? Clip { get; private set; }

    /// <summary>Holds <paramref name="clip"/>, cut or copied, with no pastes counted yet (<c>setContents</c>).</summary>
    internal void Set(DesignClip clip, bool cut)
    {
        Clip = clip;
        WasCut = cut;
        _pasteCounts.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>One more paste into <paramref name="container"/> of <paramref name="document"/>; the count now (<c>incrementPasteCounter</c>).</summary>
    internal int CountPaste(DesignDocument document, DesignComponent container)
    {
        int count = _pasteCounts.GetValueOrDefault((document, container)) + 1;
        _pasteCounts[(document, container)] = count;
        return count;
    }

    /// <summary>A paste into <paramref name="container"/> undone (<c>decrementPasteCounter</c>, which counts 1 from nothing).</summary>
    internal void UncountPaste(DesignDocument document, DesignComponent container)
    {
        int count = _pasteCounts.GetValueOrDefault((document, container));
        _pasteCounts[(document, container)] = count == 0 ? 1 : count - 1;
    }

    /// <summary>The pastes into every container of <paramref name="document"/> (<c>getPasteCount</c> with no container).</summary>
    internal int PasteCount(DesignDocument document) =>
        _pasteCounts.Where(p => ReferenceEquals(p.Key.Document, document)).Sum(static p => p.Value);
}

/// <summary>
/// What Cut or Copy took: each component at the top of the selection, copied with everything inside
/// it and its place where it was; the groups selected whole; the app and container it came from; and,
/// for each context menu taken, the components it was assigned to.
/// </summary>
internal sealed record DesignClip(
    IReadOnlyList<DesignClipItem> Items,
    IReadOnlyList<DesignClipGroup> Groups,
    DesignDocument Source,
    DesignComponent SourceParent);

/// <summary>
/// One component of a clip: a copy that belongs to no design, with its code name and everything as
/// it was; the component it copies; its outer box in its container's pixels (null for one placed
/// by order); its cells when it was in a grid; whether it is placed by its order among its
/// siblings; and, for a context menu, the code names of the components it was assigned to.
/// </summary>
internal sealed record DesignClipItem(
    DesignComponent Copy,
    DesignComponent Source,
    Rect2D? Outer,
    GridSpan? Span,
    bool OrderBased,
    IReadOnlyList<string> AssignedTo)
{
    /// <summary>The group the component is directly in, among the clip's groups; null for none.</summary>
    public DesignClipGroup? Group { get; set; }
}

/// <summary>A group of a clip: its members - items, by their index, and groups - in order.</summary>
internal sealed class DesignClipGroup
{
    /// <summary>The members: an item's index, or a group inside this one.</summary>
    public List<(int? Item, DesignClipGroup? Group)> Members { get; } = [];

    /// <summary>The group this one is in; null at the top.</summary>
    public DesignClipGroup? Parent { get; set; }

    /// <summary>Whether the group is a labelled component: two items, one a label (<c>IsLabeledComponent</c>).</summary>
    public bool IsLabeled(IReadOnlyList<DesignClipItem> items) =>
        Members.Count == 2 && Members[0].Item is { } a && Members[1].Item is { } b
        && (items[a].Copy.Type == DesignGroup.LabelType) != (items[b].Copy.Type == DesignGroup.LabelType);

    /// <summary>Every item in the group or a group inside it.</summary>
    public IEnumerable<int> AllItems()
    {
        foreach ((int? item, DesignClipGroup? group) in Members)
        {
            if (item is { } i)
            {
                yield return i;
            }
            else
            {
                foreach (int j in group!.AllItems())
                {
                    yield return j;
                }
            }
        }
    }
}
