namespace JGraph.Scripting.Designer;

/// <summary>Where Move sends the selection among its siblings: a tab's, a tree node's order (the canvas menu's Move).</summary>
public enum DesignMove
{
    /// <summary>To the first place (Move First, Ctrl+Shift+Home).</summary>
    First,

    /// <summary>One place back (Left or Up, Ctrl+Shift+Left or Up).</summary>
    Previous,

    /// <summary>One place on (Right or Down, Ctrl+Shift+Right or Down).</summary>
    Next,

    /// <summary>To the last place (Move to End, Ctrl+Shift+End).</summary>
    Last,
}

/// <summary>
/// App Designer's structure rules (App Designer window plan, stage W5c), apart from any app: which
/// ways a selection moves among its siblings and where each one goes (<c>ReorderAction</c>'s
/// <c>_checkAbilityToReorder</c> and <c>_performReorder</c>, <c>ReorderCommand._moveComponents</c>),
/// where a tab heading dragged along its strip is dropped (<c>TabDragController</c>), and where a radio
/// or toggle button added to a button group goes (<c>MutualExclusiveButtonUtil</c>), transcribed from
/// R2025b's client and checked against it by the node oracle <c>ui-probes/w5/oracle/structure.js</c>.
/// </summary>
public static class DesignStructure
{
    /// <summary>The size a radio button added to a group is made at, before its text sizes it (<c>createRadioButtonComponentData</c>).</summary>
    public static readonly (double Width, double Height) RadioButtonSize = (52, 15);

    /// <summary>The size a toggle button added to a group is made at (<c>createToggleButtonComponentData</c>).</summary>
    public static readonly (double Width, double Height) ToggleButtonSize = (100, 22);

    /// <summary>
    /// Which ways a selection can move among <paramref name="count"/> siblings
    /// (<c>_checkAbilityToReorder</c>): <paramref name="selected"/> are the places of the selected ones
    /// that move. The stack order (<paramref name="stack"/>) moves one alone; the order of tabs and
    /// tree nodes any number. None among fewer than two.
    /// </summary>
    /// <returns>Whether each of first, back one, on one and last can move it, and whether a drag can (<c>canOffset</c>, the internal order only).</returns>
    public static (bool First, bool Previous, bool Next, bool Last, bool Offset) Ability(int count, IReadOnlyList<int> selected, bool stack)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if ((stack && selected.Count != 1) || (!stack && selected.Count == 0) || count - 1 <= 0)
        {
            return default;
        }

        int first = selected.Min();
        int last = selected.Max();
        int end = count - 1;
        return (first > 0, first > 0, last < end, last < end, !stack);
    }

    /// <summary>
    /// The order <paramref name="count"/> siblings take when the ones at <paramref name="selected"/>
    /// move <paramref name="how"/> (<c>_performReorder</c>): each to its place less the first's for
    /// First, plus the room after the last for Last, one back or on; or, for a drag
    /// (<paramref name="drop"/>), one after another from the place dropped at - then put there one at a
    /// time as <c>ReorderCommand</c> does.
    /// </summary>
    /// <returns>The siblings' former places in their new order.</returns>
    public static int[] Order(int count, IReadOnlyList<int> selected, DesignMove? how, int? drop = null)
    {
        ArgumentNullException.ThrowIfNull(selected);
        int[] sorted = [.. selected.Order()];
        int t = sorted[0];
        int i = sorted[^1];
        int s = count - 1;
        int b = drop ?? 0;
        var moves = new List<(int Item, int Old, int New)>();
        foreach (int n in sorted)
        {
            int f = how switch
            {
                DesignMove.First => n - t,
                DesignMove.Last => n + s - i,
                DesignMove.Previous => n - 1,
                DesignMove.Next => n + 1,
                _ => b++,
            };
            moves.Add((n, n, f));
        }

        return [.. Apply(Enumerable.Range(0, count).ToList(), moves)];
    }

    /// <summary>
    /// <paramref name="order"/> after <c>ReorderCommand._moveComponents</c>: the moves taken from the
    /// greatest new place, or from the least when all of them go back, each one out of the list and in
    /// again at its new place.
    /// </summary>
    internal static List<T> Apply<T>(List<T> order, IReadOnlyList<(T Item, int Old, int New)> moves)
    {
        var list = new List<T>(order);
        var sorted = moves.OrderByDescending(static m => m.New).ToList();
        if (sorted.All(static m => m.New < m.Old))
        {
            sorted.Reverse();
        }

        foreach ((T item, _, int to) in sorted)
        {
            list.Remove(item);
            list.Insert(System.Math.Clamp(to, 0, list.Count), item);
        }

        return list;
    }

    /// <summary>
    /// Where a tab heading dragged to <paramref name="pointer"/> along its strip goes
    /// (<c>_showVerticalInsertionIndicators</c> and <c>_completeGesture</c>): before the first heading
    /// whose centre is past the pointer, or after the last; one place less when it goes after where it
    /// started. <paramref name="centres"/> are the headings' centres along the strip, in order.
    /// </summary>
    /// <returns>The heading the drop marker is at and whether on its far side; and the tab's new place, null when it stays.</returns>
    public static (int Marker, bool After, int? To) TabDrop(IReadOnlyList<double> centres, double pointer, int start)
    {
        ArgumentNullException.ThrowIfNull(centres);
        int hovered = -1;
        for (int k = 0; k < centres.Count; k++)
        {
            if (pointer < centres[k])
            {
                hovered = k;
                break;
            }
        }

        bool atEnd = hovered < 0;
        if (atEnd)
        {
            hovered = centres.Count - 1;
        }

        int to = start < hovered && !atEnd ? hovered - 1 : hovered;
        return (hovered, atEnd, to == start ? null : to);
    }

    /// <summary>
    /// The size a radio or toggle button added to a group is given for <paramref name="text"/>
    /// (<c>setComponentToPreferredSize</c>): its widget's width - the text in 12-pixel Helvetica, up to
    /// a whole pixel, and a radio button's circle beside it - and the larger of 5 and 3% of it more
    /// (<c>_getWidthBuffer</c>); a toggle button no narrower than it was made
    /// (<c>isPreserveMinimumDefaultWidth</c>); 22 high. App Designer measures its widget in its
    /// browser; the shared text measure gives the 58 and 65 pixels its Radio Button Group's
    /// <c>Button</c> and <c>Button2</c> are made at.
    /// </summary>
    public static (double Width, double Height) ButtonPreferredSize(bool toggle, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        double measured = System.Math.Ceiling(JGraph.Core.Model.UiFit.TextWidth([text], "Helvetica", 12, bold: false, italic: false) - 1e-9);
        double widget = measured + (toggle ? ToggleButtonPadding : RadioButtonCircle);
        double width = widget + System.Math.Round(System.Math.Max(5, 0.03 * widget), MidpointRounding.AwayFromZero);
        return (toggle ? System.Math.Max(width, ToggleButtonSize.Width) : width, 22);
    }

    // A radio button's circle and the gap after it, and a toggle button's padding, in its widget's preferred width.
    private const double RadioButtonCircle = 18;
    private const double ToggleButtonPadding = 16;

    /// <summary>
    /// Where a radio or toggle button added to a button group goes (<c>_calculateLocation</c>), as
    /// MATLAB counts from the group's bottom-left: 11 pixels in, 10 below the top of a group whose
    /// content is <paramref name="contentHeight"/> high, or under the lowest of its buttons - 1 pixel
    /// apart for a radio button, 1 over for a toggle button; and whether it is selected
    /// (<c>getSelection</c>): when it is the first. <paramref name="bottoms"/> are the bottoms of the
    /// group's radio and toggle buttons.
    /// </summary>
    public static (double X, double Y, bool Value) ButtonPlace(bool toggle, double contentHeight, IReadOnlyList<double> bottoms)
    {
        ArgumentNullException.ThrowIfNull(bottoms);
        double height = toggle ? ToggleButtonSize.Height : RadioButtonSize.Height;
        double spacing = toggle ? -1 : 1;
        double y = bottoms.Count > 0 ? bottoms.Min() - height - spacing : contentHeight - height - 10;
        return (11, y, bottoms.Count == 0);
    }
}
