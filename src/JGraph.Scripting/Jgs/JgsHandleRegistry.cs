using System.Diagnostics.CodeAnalysis;
using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What the script layer knows about one live figure object: the model object itself, and the two
/// pieces of state that belong to the script rather than to the figure — whether the object asked to
/// be kept out of legends, and the callback a legend was given.
/// <para>
/// There is deliberately no "kind" field. M51 carried one, and M53 had to widen it with a catch-all
/// <c>Plot</c> member the moment the statistics verbs drew something that was not a line. The object
/// already knows what it is: its CLR type is the answer, and
/// <see cref="JgsGraphicsProperties.TypeNameOf(GraphObject)"/> is where that is read. A chart type
/// added in a later milestone therefore joins the handle surface by existing.
/// </para>
/// </summary>
internal sealed class JgsHandleEntry
{
    public JgsHandleEntry(GraphObject target)
    {
        Target = target;
    }

    public GraphObject Target { get; }

    /// <summary>The MATLAB type word for this object — what <c>get(h, 'Type')</c> answers.</summary>
    public string TypeName => JgsGraphicsProperties.TypeNameOf(Target);

    /// <summary>False when the object was created with <c>'HandleVisibility', 'off'</c>.</summary>
    public bool HandleVisible { get; set; } = true;

    /// <summary>A legend's <c>ItemHitFcn</c>, if a script gave it one.</summary>
    public JgsValue? ItemHitFcn { get; set; }

    /// <summary>The object's <c>ButtonDownFcn</c>, if a script gave it one.</summary>
    public JgsValue? ButtonDownFcn { get; set; }

    /// <summary>The object's <c>CreateFcn</c>. Stored for <c>get</c> to answer; it only ever fires
    /// when it arrives as a name-value pair in the call that creates the object.</summary>
    public JgsValue? CreateFcn { get; set; }

    /// <summary>The object's <c>DeleteFcn</c>, run as the object is deleted, while it still exists.</summary>
    public JgsValue? DeleteFcn { get; set; }

    /// <summary>A figure's <c>CloseRequestFcn</c> — what runs instead of the close when set.</summary>
    public JgsValue? CloseRequestFcn { get; set; }

    /// <summary>A figure's <c>SizeChangedFcn</c>, if a script gave it one.</summary>
    public JgsValue? SizeChangedFcn { get; set; }

    /// <summary>A menu item's <c>MenuSelectedFcn</c>, if a script gave it one.</summary>
    public JgsValue? MenuSelectedFcn { get; set; }

    /// <summary>A context menu's <c>ContextMenuOpeningFcn</c>, if a script gave it one.</summary>
    public JgsValue? ContextMenuOpeningFcn { get; set; }

    /// <summary>The <c>uicontextmenu</c> shown when this object is right-clicked, if any.</summary>
    public GraphObject? ContextMenu { get; set; }

    /// <summary>Whether a callback running on this object lets queued events in at a drain point.
    /// MATLAB's default is on.</summary>
    public bool Interruptible { get; set; } = true;

    /// <summary>Whether this object's events wait for a non-interruptible callback (<c>'queue'</c>,
    /// the default, true) or are discarded when they cannot run at once (<c>'cancel'</c>, false).</summary>
    public bool BusyActionQueues { get; set; } = true;

    /// <summary>MATLAB's <c>PickableParts</c> word: <c>'visible'</c> (default), <c>'all'</c>, or
    /// <c>'none'</c> — whether clicks can land on this object at all.</summary>
    public string PickableParts { get; set; } = "visible";

    /// <summary>
    /// Whatever a script has hung on this object with <c>setappdata</c>. It lives on the entry rather
    /// than on the model because it is the script's own bookkeeping and has no business being drawn,
    /// inspected or saved — and because the entry is already what a closed figure lets go of, so
    /// application data cannot outlive the object it was attached to.
    /// </summary>
    public Dictionary<string, JgsValue> AppData { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// The script's handles on figure objects. A handle is an ordinary number, the way MATLAB's were before
/// its own graphics objects arrived, and this is the book that says which object each number names.
/// <para>
/// Making a handle a number rather than a value type of its own is what lets a script keep handles in an
/// array and do the obvious things with them: <c>h(i) = p</c> grows the array, <c>[ax1 ax2]</c>
/// concatenates, <c>h == p</c> compares by identity, and <c>[s.line]</c> gathers a field across a struct
/// array into a row. All of that already worked for numbers.
/// </para>
/// The numbers are minted with a half so they cannot be confused with a figure number, a loop counter,
/// or anything a script is likely to compute; dotting into a number that is not in the book still fails
/// the way it always did. A <em>figure</em> is the exception and is never minted at all: its handle is
/// its number, which is what MATLAB has always done and what makes <c>close(1)</c>, <c>figure(1)</c> and
/// <c>get(1, 'Color')</c> name the same thing without any bookkeeping to keep in step.
/// </summary>
internal static class JgsHandleRegistry
{
    private const double FirstHandle = 1_000_000.5;

    private static readonly object Gate = new();
    private static readonly Dictionary<double, JgsHandleEntry> Entries = new();
    private static readonly Dictionary<GraphObject, double> Handles = new(ReferenceEquality.Instance);

    private static double _next = FirstHandle;

    /// <summary>The handle for a figure object, minting one the first time it is asked for.</summary>
    public static JgsValue For(GraphObject target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // A figure is its number. Minting one would give the same figure two names.
        if (target is FigureModel figure)
        {
            int number = JG.GetFigureNumber(figure);
            if (number > 0)
            {
                return JgsValue.Number(number);
            }
        }

        lock (Gate)
        {
            if (Handles.TryGetValue(target, out double existing))
            {
                return JgsValue.Number(existing);
            }

            double handle = _next;
            _next += 1;
            Entries[handle] = new JgsHandleEntry(target);
            Handles[target] = handle;
            return JgsValue.Number(handle);
        }
    }

    /// <summary>
    /// The entry for an object, minting its handle if it has none. A search needs this: it walks
    /// objects rather than numbers, and still has to ask each one whether it wants to be found.
    /// </summary>
    public static JgsHandleEntry EntryFor(GraphObject target)
    {
        JgsValue handle = For(target);
        return TryGet(handle, out JgsHandleEntry? entry)
            ? entry
            : new JgsHandleEntry(target);
    }

    /// <summary>
    /// The entry an object already has, without minting one. This is the dispatch-time question —
    /// "does this object still have script-side state?" — and minting would answer yes for an object
    /// that was deleted between the event and its delivery.
    /// </summary>
    public static bool TryGetEntry(GraphObject target, [NotNullWhen(true)] out JgsHandleEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(target);
        entry = null;

        // A live figure gets its entry on demand — being open is what being alive means for it.
        if (target is FigureModel figure)
        {
            if (JG.GetFigureNumber(figure) > 0)
            {
                entry = FigureEntry(figure);
                return true;
            }

            return false;
        }

        lock (Gate)
        {
            return Handles.TryGetValue(target, out double handle) && Entries.TryGetValue(handle, out entry);
        }
    }

    /// <summary>The entry a value names, when the value is a number this registry knows.</summary>
    public static bool TryGet(JgsValue value, [NotNullWhen(true)] out JgsHandleEntry? entry)
    {
        entry = null;
        if (value.Type != JgsType.Number)
        {
            return false;
        }

        double handle = value.AsNumber;
        lock (Gate)
        {
            if (Entries.TryGetValue(handle, out entry))
            {
                return true;
            }
        }

        // A whole number may be a live figure. Resolving it here rather than minting on figure
        // creation is what keeps a closed-and-reopened number pointing at the current figure.
        if (handle > 0 && handle == System.Math.Floor(handle) && handle < int.MaxValue
            && JG.TryGetFigure((int)handle, out FigureModel figure))
        {
            entry = FigureEntry(figure);
            return true;
        }

        return false;
    }

    /// <summary>The entry for a handle, or an error naming the handle as dead.</summary>
    public static JgsHandleEntry Require(JgsValue value, int line, int col)
    {
        if (TryGet(value, out JgsHandleEntry? entry))
        {
            return entry;
        }

        throw new JgsRuntimeException(line, col,
            "This is not a handle to a figure object; it may belong to a figure that has since been cleared.");
    }

    /// <summary>Forgets every handle — what a fresh run or a cleared figure registry means.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
            Handles.Clear();
            FigureEntries.Clear();
            _next = FirstHandle;
            JgsGraphicsCallbackState.Clear();
            JgsGraphicsProperties.ForgetGroups();
        }
    }

    /// <summary>
    /// Drops the handles of objects no longer reachable from a live figure. Closing a window retires
    /// its figure, and without this the entries — and through them the whole model subtree — would be
    /// held alive by the registry for as long as the session lasted.
    /// </summary>
    public static void DropUnreachable()
    {
        var live = new HashSet<GraphObject>(ReferenceEquality.Instance);
        foreach (int number in JG.FigureNumbers)
        {
            if (JG.TryGetFigure(number, out FigureModel figure))
            {
                Collect(figure, live);
            }
        }

        lock (Gate)
        {
            var dead = new List<double>();
            foreach ((double handle, JgsHandleEntry entry) in Entries)
            {
                if (!live.Contains(entry.Target))
                {
                    dead.Add(handle);
                    Handles.Remove(entry.Target);
                }
            }

            foreach (double handle in dead)
            {
                Entries.Remove(handle);
            }

            foreach (FigureModel figure in FigureEntries.Keys.ToList())
            {
                if (!live.Contains(figure))
                {
                    FigureEntries.Remove(figure);
                }
            }
        }
    }

    /// <summary>
    /// Every object a figure owns, itself included, in no particular order. This walks the
    /// <em>descendant</em> relation, not the visible-children one: a hidden legend or a ruler is
    /// still owned by its axes, and reaping its entry — with whatever callbacks and application
    /// data a script hung on it — just because it is not currently showing would lose state the
    /// object never let go of. <c>findall</c> makes the same choice for the same reason.
    /// </summary>
    internal static void Collect(GraphObject root, HashSet<GraphObject> into)
    {
        if (!into.Add(root))
        {
            return;
        }

        foreach (GraphObject descendant in JgsGraphicsProperties.DescendantsOf(root))
        {
            Collect(descendant, into);
        }
    }

    /// <summary>
    /// The entry for a figure. Figures are not in <see cref="Entries"/> — their handle is their number —
    /// but they still need somewhere to keep the script-side state an entry carries.
    /// </summary>
    private static readonly Dictionary<FigureModel, JgsHandleEntry> FigureEntries =
        new(FigureEquality.Instance);

    private static JgsHandleEntry FigureEntry(FigureModel figure)
    {
        lock (Gate)
        {
            if (!FigureEntries.TryGetValue(figure, out JgsHandleEntry? entry))
            {
                entry = new JgsHandleEntry(figure);
                FigureEntries[figure] = entry;
            }

            return entry;
        }
    }

    /// <summary>Two model objects are the same object only when they are the same reference.</summary>
    private sealed class ReferenceEquality : IEqualityComparer<GraphObject>
    {
        public static readonly ReferenceEquality Instance = new();

        public bool Equals(GraphObject? x, GraphObject? y) => ReferenceEquals(x, y);

        public int GetHashCode(GraphObject obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private sealed class FigureEquality : IEqualityComparer<FigureModel>
    {
        public static readonly FigureEquality Instance = new();

        public bool Equals(FigureModel? x, FigureModel? y) => ReferenceEquals(x, y);

        public int GetHashCode(FigureModel obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
