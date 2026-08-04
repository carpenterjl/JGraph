using System.Diagnostics.CodeAnalysis;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>What kind of figure object a handle names.</summary>
internal enum JgsHandleKind
{
    Axes,
    Line,
    Legend,
}

/// <summary>
/// What the script layer knows about one live figure object: which kind it is, the model object itself,
/// and the two pieces of state that belong to the script rather than to the figure — whether the object
/// asked to be kept out of legends, and the callback a legend was given.
/// </summary>
internal sealed class JgsHandleEntry
{
    public JgsHandleEntry(JgsHandleKind kind, GraphObject target)
    {
        Kind = kind;
        Target = target;
    }

    public JgsHandleKind Kind { get; }

    public GraphObject Target { get; }

    /// <summary>False when the object was created with <c>'HandleVisibility', 'off'</c>.</summary>
    public bool HandleVisible { get; set; } = true;

    /// <summary>A legend's <c>ItemHitFcn</c>, if a script gave it one.</summary>
    public JgsValue? ItemHitFcn { get; set; }
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
/// the way it always did.
/// </summary>
internal static class JgsHandleRegistry
{
    private const double FirstHandle = 1_000_000.5;

    private static readonly object Gate = new();
    private static readonly Dictionary<double, JgsHandleEntry> Entries = new();
    private static readonly Dictionary<GraphObject, double> Handles = new(ReferenceEquality.Instance);

    private static double _next = FirstHandle;

    /// <summary>The handle for a figure object, minting one the first time it is asked for.</summary>
    public static JgsValue For(JgsHandleKind kind, GraphObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (Gate)
        {
            if (Handles.TryGetValue(target, out double existing))
            {
                return JgsValue.Number(existing);
            }

            double handle = _next;
            _next += 1;
            Entries[handle] = new JgsHandleEntry(kind, target);
            Handles[target] = handle;
            return JgsValue.Number(handle);
        }
    }

    /// <summary>The entry a value names, when the value is a number this registry knows.</summary>
    public static bool TryGet(JgsValue value, [NotNullWhen(true)] out JgsHandleEntry? entry)
    {
        if (value.Type != JgsType.Number)
        {
            entry = null;
            return false;
        }

        lock (Gate)
        {
            return Entries.TryGetValue(value.AsNumber, out entry);
        }
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
            _next = FirstHandle;
        }
    }

    /// <summary>Two model objects are the same object only when they are the same reference.</summary>
    private sealed class ReferenceEquality : IEqualityComparer<GraphObject>
    {
        public static readonly ReferenceEquality Instance = new();

        public bool Equals(GraphObject? x, GraphObject? y) => ReferenceEquals(x, y);

        public int GetHashCode(GraphObject obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
