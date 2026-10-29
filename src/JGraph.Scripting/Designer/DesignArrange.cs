namespace JGraph.Scripting.Designer;

/// <summary>How the selection is aligned, in the order of App Designer's alignment types (CANVAS > ALIGN).</summary>
public enum DesignAlign
{
    /// <summary>Left edges on the anchor's (Ctrl+Alt+1).</summary>
    Left,

    /// <summary>Centres across on the anchor's (Ctrl+Alt+2).</summary>
    Center,

    /// <summary>Right edges on the anchor's (Ctrl+Alt+3).</summary>
    Right,

    /// <summary>Top edges on the anchor's (Ctrl+Alt+4).</summary>
    Top,

    /// <summary>Middles up and down on the anchor's (Ctrl+Alt+5).</summary>
    Middle,

    /// <summary>Bottom edges on the anchor's (Ctrl+Alt+6).</summary>
    Bottom,
}

/// <summary>What Same Size makes alike (CANVAS > ARRANGE > Same Size).</summary>
public enum DesignSameSize
{
    /// <summary>Width and height.</summary>
    Size,

    /// <summary>The width only.</summary>
    Width,

    /// <summary>The height only.</summary>
    Height,
}

/// <summary>
/// A component as App Designer's arrange commands see it: its <c>Location</c> and <c>Size</c> in its
/// container's pixels (MATLAB's, the y axis upward), what its shape fixes, its least size, and its
/// place among its container's children. The commands change <see cref="X"/> to <see cref="Height"/>.
/// </summary>
public sealed class DesignArrangeItem(double x, double y, double width, double height)
{
    /// <summary>The left edge.</summary>
    public double X { get; set; } = x;

    /// <summary>The bottom edge.</summary>
    public double Y { get; set; } = y;

    /// <summary>The width.</summary>
    public double Width { get; set; } = width;

    /// <summary>The height.</summary>
    public double Height { get; set; } = height;

    /// <summary>Whether its width cannot be changed (<c>IsSizeFixed(1)</c>).</summary>
    public bool FixedWidth { get; init; }

    /// <summary>Whether its height cannot be changed (<c>IsSizeFixed(2)</c>).</summary>
    public bool FixedHeight { get; init; }

    /// <summary>Whether it keeps its proportions (<c>AspectRatioLimits</c> other than <c>[0 Inf]</c>).</summary>
    public bool KeepsRatio { get; init; }

    /// <summary>Its least width and height (its adapter's <c>MinimumSize</c>).</summary>
    public (double Width, double Height) Minimum { get; init; } = (1, 1);

    /// <summary>Its index among its container's children: the stack order the spacing's ties fall back on.</summary>
    public int Z { get; init; }

    /// <summary>Moves it by (<paramref name="dx"/>, <paramref name="dy"/>), up for a positive <paramref name="dy"/> (<c>DesignerWidgetData.move</c>).</summary>
    public void Move(double dx, double dy)
    {
        X += dx;
        Y += dy;
    }
}

/// <summary>
/// App Designer's Align, Same Size and Space (App Designer window plan, stage W4), transcribed from
/// R2025b's <c>appdesigner/command/Alignment</c>, <c>SameSize</c> and <c>Spacing</c>, in their own
/// arithmetic and order: the positions are MATLAB's, the y axis upward; a centre is rounded as
/// JavaScript's <c>Math.round</c> rounds, halves upward. <c>ui-probes/w4/oracle/arrange.js</c> runs
/// R2025b's modules over seeded scenes, which the tests replay.
/// </summary>
public static class DesignArrange
{
    /// <summary>The spacing the SPACE section's box offers besides "Evenly" (<c>MANUAL_SPACING_DEFAULT</c>).</summary>
    public const double ManualSpacingDefault = 20;

    /// <summary>The SPACE section's word for spacing the selection evenly.</summary>
    public const string Evenly = "Evenly";

    /// <summary>
    /// Aligns <paramref name="items"/> on <paramref name="anchor"/> - the last of them when null - which
    /// stays where it is (<c>alignWidgets</c>).
    /// </summary>
    /// <returns>Whether it aligned: false for fewer than two items.</returns>
    public static bool Align(DesignAlign how, IReadOnlyList<DesignArrangeItem> items, DesignArrangeItem? anchor = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count <= 1)
        {
            return false;
        }

        DesignArrangeItem a = anchor ?? items[^1];
        foreach (DesignArrangeItem item in items)
        {
            if (ReferenceEquals(item, a))
            {
                continue;
            }

            switch (how)
            {
                case DesignAlign.Left:
                    item.Move(a.X - item.X, 0);
                    break;
                case DesignAlign.Center:
                    item.Move(JsRound((a.X + (a.Width / 2)) - (item.X + (item.Width / 2))), 0);
                    break;
                case DesignAlign.Right:
                    item.Move((a.X + a.Width) - (item.X + item.Width), 0);
                    break;
                case DesignAlign.Top:
                    item.Move(0, (a.Y + a.Height) - item.Y - item.Height);
                    break;
                case DesignAlign.Middle:
                    item.Move(0, JsRound((a.Y + (a.Height / 2)) - (item.Y + (item.Height / 2))));
                    break;
                case DesignAlign.Bottom:
                    item.Move(0, a.Y - item.Y);
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gives <paramref name="items"/> <paramref name="anchor"/>'s size - the last of them when null -
    /// (<c>makeSameSize</c>): never below an item's least size, not along a side its shape fixes, its
    /// proportions kept where its shape keeps them, its top edge where it was.
    /// </summary>
    /// <returns>Whether it sized: false for fewer than two items.</returns>
    public static bool SameSize(DesignSameSize how, IReadOnlyList<DesignArrangeItem> items, DesignArrangeItem? anchor = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count <= 1)
        {
            return false;
        }

        DesignArrangeItem a = anchor ?? items[^1];
        foreach (DesignArrangeItem b in items)
        {
            if (ReferenceEquals(b, a))
            {
                continue;
            }

            switch (how)
            {
                case DesignSameSize.Width:
                    SameWidth(b, a.Width);
                    break;
                case DesignSameSize.Height:
                    SameHeight(b, a.Height);
                    break;
                case DesignSameSize.Size:
                    Both(b, a.Width, a.Height);
                    break;
            }
        }

        return true;
    }

    private static void SameWidth(DesignArrangeItem b, double width)
    {
        if (b.FixedWidth)
        {
            return;
        }

        double target = width >= b.Minimum.Width ? width : b.Minimum.Width;
        (double w0, double h0) = (b.Width, b.Height);
        double w = target;
        double h = h0;
        if (b.KeepsRatio)
        {
            double y = HeightFor(w0, h0, w);
            if (y >= b.Minimum.Height)
            {
                h += y - h; // as SameSize adds the change, which is not always y to the last bit
            }
            else
            {
                // Its least height, and the width the ratio limits' lower bound gives it: 1, for each
                // shape that keeps its ratio (knobs and lamps are square).
                w = JsRound(b.Minimum.Height * 1);
                h = b.Minimum.Height;
            }
        }

        if (h != h0)
        {
            b.Y -= h - h0;
        }

        b.Width = w;
        b.Height = h;
    }

    private static void SameHeight(DesignArrangeItem b, double height)
    {
        if (b.FixedHeight)
        {
            return;
        }

        double target = height >= b.Minimum.Height ? height : b.Minimum.Height;
        (double w0, double h0) = (b.Width, b.Height);
        double w = w0;
        double h = target;
        if (b.KeepsRatio)
        {
            double x = WidthFor(w0, h0, h);
            if (x >= b.Minimum.Width)
            {
                w += x - w;
            }
            else
            {
                w = b.Minimum.Width;
                h = JsRound(b.Minimum.Width / 1); // as above
            }
        }

        b.Y -= h - h0;
        b.Width = w;
        b.Height = h;
    }

    private static void Both(DesignArrangeItem b, double width, double height)
    {
        if (b.FixedWidth && b.FixedHeight)
        {
            return;
        }

        double wantW = width >= b.Minimum.Width ? width : b.Minimum.Width;
        double wantH = height >= b.Minimum.Height ? height : b.Minimum.Height;
        (double w0, double h0) = (b.Width, b.Height);
        double dw = b.FixedWidth ? 0 : wantW - w0;
        double dh = b.FixedHeight ? 0 : wantH - h0;
        double w = w0 + dw;
        double h = h0 + dh;
        if (b.KeepsRatio)
        {
            double ratio = w0 / h0;
            if (ratio == 1)
            {
                (w, h) = (wantW, wantW);
            }
            else if (ratio < 1)
            {
                w += WidthFor(w0, h0, h) - w;
            }
            else
            {
                double y = HeightFor(w0, h0, w);
                dh = y - h;
                h += dh;
            }
        }

        if (dh != 0)
        {
            b.Y -= h - h0;
        }

        b.Width = w;
        b.Height = h;
    }

    // SameSize's own s and i: the other side for one side, in the current proportions.
    private static double HeightFor(double w0, double h0, double width) => width / (w0 / h0);

    private static double WidthFor(double w0, double h0, double height) => height * (w0 / h0);

    /// <summary>
    /// Spaces <paramref name="items"/> (<c>spaceWidgets</c>): evenly between the two outermost, which
    /// stay, for three or more (<paramref name="gap"/> null) - the pixels left over going one each to
    /// the first gaps - or <paramref name="gap"/> pixels apart from the first, for two or more; left to
    /// right, or top to bottom, ties by the other edge and then by the stack order.
    /// </summary>
    /// <returns>Whether anything moved.</returns>
    public static bool Space(bool horizontal, double? gap, IReadOnlyList<DesignArrangeItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count <= 1 || (gap is { } g && g < 0) || (gap is null && items.Count == 2))
        {
            return false;
        }

        double moved = (horizontal, gap) switch
        {
            (true, null) => EvenlyAcross(SortedAcross(items)),
            (false, null) => EvenlyDown(SortedDown(items)),
            (true, { } across) => ApartAcross(SortedAcross(items), across),
            (false, { } down) => ApartDown(SortedDown(items), down),
        };
        return moved != 0;
    }

    private static double EvenlyAcross(List<DesignArrangeItem> a)
    {
        int n = a.Count;
        double widths = 0;
        for (int i = 0; i < n; i++)
        {
            widths += a[i].Width;
        }

        double free = a[n - 1].X + a[n - 1].Width - a[0].X - widths;
        int gaps = n - 1;
        double each = free >= 0 ? System.Math.Floor((free / gaps) + 1e-4) : System.Math.Ceiling((free / gaps) - 1e-4);
        double left = free - (each * gaps);
        double total = 0;
        for (int i = 1; i < n - 1; i++)
        {
            DesignArrangeItem before = a[i - 1];
            DesignArrangeItem item = a[i];
            double x = before.X + before.Width + each;
            if (left != 0 && i <= System.Math.Abs(left))
            {
                x += left > 0 ? 1 : -1;
            }

            double dx = x - item.X;
            item.Move(dx, 0);
            total += dx;
        }

        return total;
    }

    private static double EvenlyDown(List<DesignArrangeItem> a)
    {
        int n = a.Count;
        double heights = 0;
        for (int i = 0; i < n; i++)
        {
            heights += a[i].Height;
        }

        double free = a[0].Y + a[0].Height - a[n - 1].Y - heights;
        int gaps = n - 1;
        double each = free >= 0 ? System.Math.Floor((free / gaps) + 1e-4) : System.Math.Ceiling((free / gaps) - 1e-4);
        double left = free - (each * gaps);
        double total = 0;
        for (int i = 1; i < n - 1; i++)
        {
            DesignArrangeItem above = a[i - 1];
            DesignArrangeItem item = a[i];
            double y = above.Y - each - item.Height;
            if (left != 0 && i <= System.Math.Abs(left))
            {
                y += left > 0 ? -1 : 1;
            }

            double dy = y - item.Y;
            item.Move(0, dy);
            total += dy;
        }

        return total;
    }

    private static double ApartAcross(List<DesignArrangeItem> a, double gap)
    {
        double total = 0;
        for (int i = 1; i < a.Count; i++)
        {
            double dx = a[i - 1].X + a[i - 1].Width + gap - a[i].X;
            a[i].Move(dx, 0);
            total += dx;
        }

        return total;
    }

    private static double ApartDown(List<DesignArrangeItem> a, double gap)
    {
        double total = 0;
        for (int i = 1; i < a.Count; i++)
        {
            double dy = a[i - 1].Y - a[i].Height - gap - a[i].Y;
            a[i].Move(0, dy);
            total += dy;
        }

        return total;
    }

    // Spacing's s: left to right, then bottom to top, then back to front. JavaScript's sort is
    // stable, as List.Sort is not: the index breaks what the comparer leaves equal.
    private static List<DesignArrangeItem> SortedAcross(IReadOnlyList<DesignArrangeItem> items) =>
        items.Select(static (item, i) => (item, i))
            .OrderBy(static p => p, Comparer<(DesignArrangeItem Item, int I)>.Create(static (l, u) => Across(l.Item, u.Item) is var c and not 0 ? c : l.I.CompareTo(u.I)))
            .Select(static p => p.item).ToList();

    // Spacing's i: top to bottom (the y axis is upward), then left to right, then back to front.
    private static List<DesignArrangeItem> SortedDown(IReadOnlyList<DesignArrangeItem> items) =>
        items.Select(static (item, i) => (item, i))
            .OrderBy(static p => p, Comparer<(DesignArrangeItem Item, int I)>.Create(static (l, u) => Down(l.Item, u.Item) is var c and not 0 ? c : l.I.CompareTo(u.I)))
            .Select(static p => p.item).ToList();

    private static int Across(DesignArrangeItem l, DesignArrangeItem u)
    {
        if (l.X < u.X)
        {
            return -1;
        }

        if (l.X == u.X)
        {
            if (l.Y < u.Y)
            {
                return -1;
            }

            if (l.Y == u.Y)
            {
                return l.Z < u.Z ? -1 : 1;
            }
        }

        return 1;
    }

    private static int Down(DesignArrangeItem l, DesignArrangeItem u)
    {
        if (l.Y > u.Y)
        {
            return -1;
        }

        if (l.Y == u.Y)
        {
            if (l.X < u.X)
            {
                return -1;
            }

            if (l.X == u.X)
            {
                return l.Z < u.Z ? -1 : 1;
            }
        }

        return 1;
    }

    /// <summary>JavaScript's <c>Math.round</c>: halves upward.</summary>
    internal static double JsRound(double x) => System.Math.Floor(x + 0.5);
}
