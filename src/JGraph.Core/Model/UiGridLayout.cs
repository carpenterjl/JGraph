using System.ComponentModel;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>What sizes one row or column of a grid: MATLAB's <c>'fit'</c>, a number of pixels, or <c>'Nx'</c>.</summary>
public enum UiGridTrackKind
{
    /// <summary>As large as the largest thing in it asks to be.</summary>
    Fit,

    /// <summary>A fixed number of pixels.</summary>
    Fixed,

    /// <summary>A share of what the other tracks leave, by weight.</summary>
    Weight,
}

/// <summary>
/// One row or column of a grid. <c>Value</c> is the pixels of a fixed track and the weight of a
/// weighted one; <c>NumberClass</c> is the numeric class a fixed track was written in, which R2025b
/// hands back (<c>{int8(5)}</c> reads back as <c>int8</c>).
/// </summary>
public readonly record struct UiGridTrack(UiGridTrackKind Kind, double Value, string NumberClass = "double")
{
    /// <summary>MATLAB's <c>'1x'</c>, what a grid grows by.</summary>
    public static readonly UiGridTrack One = new(UiGridTrackKind.Weight, 1);

    public static readonly UiGridTrack Fit = new(UiGridTrackKind.Fit, 0);
}

/// <summary>
/// Where a child sits in a grid: MATLAB's <c>Layout.Row</c> and <c>Layout.Column</c>, each one track
/// or a run of them, counted from 1.
/// </summary>
public readonly record struct UiGridCell(int Row, int RowEnd, int Column, int ColumnEnd)
{
    public UiGridCell(int row, int column)
        : this(row, row, column, column)
    {
    }
}

/// <summary>One child of a grid, as the arithmetic needs it: its cell and the size it asks for.</summary>
public readonly record struct UiGridItem(UiGridCell Cell, Size2D Fit);

/// <summary>
/// A grid laid out at one size: each track's size, and each child's rectangle as MATLAB's pixel
/// rectangle in the grid's own area (left and bottom from 1, Y upward). <c>Content</c> is the size
/// the tracks, the spacing and the padding come to, which is larger than the grid when its fixed
/// tracks do not fit.
/// </summary>
public sealed record UiGridArrangement(
    IReadOnlyList<double> RowHeights,
    IReadOnlyList<double> ColumnWidths,
    IReadOnlyList<Rect2D> Cells,
    Size2D Content);

/// <summary>
/// The arithmetic of <c>uigridlayout</c> (app-building plan, U5), measured on R2025b once its layout
/// has settled (probe <c>u5_grid</c>):
/// <list type="bullet">
/// <item>a fixed track is its pixels; a <c>'fit'</c> track is the largest size asked for by a child
/// that sits in it alone, and a child spanning several <c>'fit'</c> tracks shares what it still needs
/// among them equally;</item>
/// <item>a <c>'fit'</c> track no child reaches takes no room and no spacing;</item>
/// <item>the weighted tracks share what is left by weight, and never go below nothing;</item>
/// <item>tracks are laid from the left and from the top, inside the padding, so a grid whose fixed
/// tracks do not fit runs off the bottom and the right;</item>
/// <item>a child has the whole of its cell, the spacing between the tracks it spans included.</item>
/// </list>
/// </summary>
/// <summary>
/// A scrollable grid laid out (<see cref="UiGridMath.ArrangeScrolling"/>): the arrangement in its
/// viewport, the viewport's size, and which scroll bars show.
/// </summary>
public sealed record UiGridScrolled(UiGridArrangement Arrangement, Size2D Viewport, bool Vertical, bool Horizontal);

public static class UiGridMath
{
    /// <summary>The thickness of the scroll bar a scrollable grid shows when its tracks do not fit.</summary>
    public const double ScrollBarSize = 17;

    /// <summary>
    /// Lays out a grid that scrolls: at its size when everything fits, and otherwise in what a
    /// scroll bar along the right, along the bottom or both leave of it. The cells are MATLAB's
    /// pixel rectangles in that viewport, which sits in the grid's top-left corner.
    /// </summary>
    public static UiGridScrolled ArrangeScrolling(
        IReadOnlyList<UiGridTrack> rows,
        IReadOnlyList<UiGridTrack> columns,
        IReadOnlyList<double> padding,
        double rowSpacing,
        double columnSpacing,
        Size2D size,
        IReadOnlyList<UiGridItem> items)
    {
        bool vertical = false;
        bool horizontal = false;
        for (int pass = 0; ; pass++)
        {
            var view = new Size2D(
                System.Math.Max(0, size.Width - (vertical ? ScrollBarSize : 0)),
                System.Math.Max(0, size.Height - (horizontal ? ScrollBarSize : 0)));
            UiGridArrangement arrangement = Arrange(rows, columns, padding, rowSpacing, columnSpacing, view, items);
            bool down = vertical || arrangement.Content.Height > view.Height + 0.5;
            bool across = horizontal || arrangement.Content.Width > view.Width + 0.5;

            // A bar takes room, which can make the other direction overflow in its turn: twice round
            // settles it.
            if ((down == vertical && across == horizontal) || pass == 2)
            {
                return new UiGridScrolled(arrangement, view, vertical, horizontal);
            }

            vertical = down;
            horizontal = across;
        }
    }

    /// <summary>
    /// Lays a grid out. <paramref name="padding"/> is MATLAB's <c>[left bottom right top]</c>. With
    /// <paramref name="size"/> null the weighted tracks take nothing, which gives the size the grid
    /// itself asks for when it sits in a <c>'fit'</c> track of another.
    /// </summary>
    public static UiGridArrangement Arrange(
        IReadOnlyList<UiGridTrack> rows,
        IReadOnlyList<UiGridTrack> columns,
        IReadOnlyList<double> padding,
        double rowSpacing,
        double columnSpacing,
        Size2D? size,
        IReadOnlyList<UiGridItem> items)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(padding);
        ArgumentNullException.ThrowIfNull(items);
        double left = padding.Count > 0 ? padding[0] : 0;
        double bottom = padding.Count > 1 ? padding[1] : 0;
        double right = padding.Count > 2 ? padding[2] : 0;
        double top = padding.Count > 3 ? padding[3] : 0;

        (double[] widths, bool[] columnGone) = Sizes(
            columns, items, static cell => (cell.Column, cell.ColumnEnd), static fit => fit.Width,
            columnSpacing, size is { } s ? s.Width - left - right : null);
        (double[] heights, bool[] rowGone) = Sizes(
            rows, items, static cell => (cell.Row, cell.RowEnd), static fit => fit.Height,
            rowSpacing, size is { } z ? z.Height - top - bottom : null);

        double[] columnStart = Starts(widths, columnGone, columnSpacing, out double contentWidth);
        double[] rowStart = Starts(heights, rowGone, rowSpacing, out double contentHeight);
        var content = new Size2D(contentWidth + left + right, contentHeight + top + bottom);
        double height = size?.Height ?? content.Height;

        var cells = new Rect2D[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            UiGridCell cell = items[i].Cell;
            (double x, double w) = Span(columnStart, widths, cell.Column, cell.ColumnEnd);
            (double y, double h) = Span(rowStart, heights, cell.Row, cell.RowEnd);

            // Rows count down from the top; MATLAB's rectangle counts up from the bottom, from 1.
            cells[i] = new Rect2D(left + x + 1, height - top - y - h + 1, w, h);
        }

        return new UiGridArrangement(heights, widths, cells, content);
    }

    /// <summary>The sizes of one direction's tracks, and which of them take no room at all.</summary>
    private static (double[] Sizes, bool[] Gone) Sizes(
        IReadOnlyList<UiGridTrack> tracks,
        IReadOnlyList<UiGridItem> items,
        Func<UiGridCell, (int From, int To)> span,
        Func<Size2D, double> along,
        double spacing,
        double? available)
    {
        // Asked what it would like to be — no size given — a weighted track is as large as what
        // is in it, as a 'fit' one is: that is how a panel holding a grid finds its own size.
        bool Fits(UiGridTrack track) =>
            track.Kind == UiGridTrackKind.Fit || (track.Kind == UiGridTrackKind.Weight && available is null);

        int count = tracks.Count;
        var sizes = new double[count];
        for (int i = 0; i < count; i++)
        {
            if (tracks[i].Kind == UiGridTrackKind.Fixed)
            {
                sizes[i] = System.Math.Max(0, tracks[i].Value);
            }
        }

        // A child alone in a 'fit' track first; then the ones that span, which share what they
        // still need among the 'fit' tracks they cross.
        foreach (UiGridItem item in items)
        {
            (int from, int to) = Clamp(span(item.Cell), count);
            if (from == to && Fits(tracks[from]))
            {
                sizes[from] = System.Math.Max(sizes[from], along(item.Fit));
            }
        }

        foreach (UiGridItem item in items)
        {
            (int from, int to) = Clamp(span(item.Cell), count);
            if (from == to)
            {
                continue;
            }

            int fits = 0;
            double have = spacing * (to - from);
            bool weighted = false;
            for (int t = from; t <= to; t++)
            {
                have += sizes[t];
                fits += Fits(tracks[t]) ? 1 : 0;
                weighted |= tracks[t].Kind == UiGridTrackKind.Weight && available is not null;
            }

            double missing = along(item.Fit) - have;
            if (fits == 0 || weighted || missing <= 0)
            {
                continue;
            }

            for (int t = from; t <= to; t++)
            {
                if (Fits(tracks[t]))
                {
                    sizes[t] += missing / fits;
                }
            }
        }

        var gone = new bool[count];
        int shown = 0;
        double used = 0;
        double weights = 0;
        for (int i = 0; i < count; i++)
        {
            gone[i] = tracks[i].Kind == UiGridTrackKind.Fit && sizes[i] <= 0;
            if (gone[i])
            {
                continue;
            }

            shown++;
            used += sizes[i];
            weights += tracks[i].Kind == UiGridTrackKind.Weight ? System.Math.Max(0, tracks[i].Value) : 0;
        }

        if (available is { } room && weights > 0)
        {
            double spare = System.Math.Max(0, room - used - (spacing * System.Math.Max(0, shown - 1)));
            for (int i = 0; i < count; i++)
            {
                if (tracks[i].Kind == UiGridTrackKind.Weight)
                {
                    sizes[i] = spare * System.Math.Max(0, tracks[i].Value) / weights;
                }
            }
        }

        return (sizes, gone);
    }

    private static (int From, int To) Clamp((int From, int To) span, int count)
    {
        int from = System.Math.Clamp(span.From, 1, System.Math.Max(1, count)) - 1;
        int to = System.Math.Clamp(span.To, from + 1, System.Math.Max(1, count)) - 1;
        return (from, to);
    }

    /// <summary>Where each track begins, from the first one's edge, and what they all come to.</summary>
    private static double[] Starts(double[] sizes, bool[] gone, double spacing, out double total)
    {
        var starts = new double[sizes.Length];
        double at = 0;
        bool any = false;
        for (int i = 0; i < sizes.Length; i++)
        {
            if (!gone[i] && any)
            {
                at += spacing;
            }

            starts[i] = at;
            if (!gone[i])
            {
                at += sizes[i];
                any = true;
            }
        }

        total = at;
        return starts;
    }

    private static (double Start, double Length) Span(double[] starts, double[] sizes, int from, int to)
    {
        if (sizes.Length == 0)
        {
            return (0, 0);
        }

        (int first, int last) = Clamp((from, to), sizes.Length);
        return (starts[first], System.Math.Max(0, starts[last] + sizes[last] - starts[first]));
    }
}

/// <summary>
/// MATLAB's <c>uigridlayout</c> (<c>matlab.ui.container.GridLayout</c>): an invisible container that
/// fills its parent and places its children in rows and columns (app-building plan, U5). It draws
/// only its background. Children are placed as they arrive, row by row after the last one placed,
/// and the grid grows by <c>'1x'</c> tracks to hold wherever a child is put.
/// </summary>
public sealed class UiGridLayoutModel : UiContainerModel
{
    private static readonly IReadOnlyList<UiGridTrack> TwoOnes = [UiGridTrack.One, UiGridTrack.One];
    private static readonly IReadOnlyList<double> TenAllRound = [10, 10, 10, 10];

    private IReadOnlyList<UiGridTrack> _rows = TwoOnes;
    private IReadOnlyList<UiGridTrack> _columns = TwoOnes;
    private IReadOnlyList<double> _padding = TenAllRound;
    private double _rowSpacing = 10;
    private double _columnSpacing = 10;
    private string _paddingClass = "double";
    private string _rowSpacingClass = "double";
    private string _columnSpacingClass = "double";
    private UiColor _background = UiPanelModel.DefaultBackground;
    private double _scrollX;
    private double _scrollY;

    public UiGridLayoutModel()
    {
        Name = "GridLayout";
        Units = UiUnits.Pixels;
        Position = new Rect2D(1, 1, 100, 100);
    }

    /// <summary>
    /// MATLAB's <c>RowHeight</c>: the rows a script declared, with <c>'1x'</c> rows after them for
    /// as far down as a child reaches. The extra rows are there only while a child needs them
    /// (R2025b, probe <c>u5_grid</c>).
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<UiGridTrack> Rows
    {
        get => Grown(_rows, NeededRows());
        set => SetProperty(ref _rows, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>ColumnWidth</c>, grown as <see cref="Rows"/> is for as far across as a child reaches.</summary>
    [Browsable(false)]
    public IReadOnlyList<UiGridTrack> Columns
    {
        get => Grown(_columns, NeededColumns());
        set => SetProperty(ref _columns, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>Padding</c>: left, bottom, right, top.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> Padding
    {
        get => _padding;
        set => SetProperty(ref _padding, value ?? TenAllRound, InvalidationKind.Ui);
    }

    /// <summary>The numeric class <see cref="Padding"/> was written in, which reads back.</summary>
    [Browsable(false)]
    public string PaddingClass
    {
        get => _paddingClass;
        set => _paddingClass = value ?? "double";
    }

    [Browsable(false)]
    public double RowSpacing
    {
        get => _rowSpacing;
        set => SetProperty(ref _rowSpacing, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string RowSpacingClass
    {
        get => _rowSpacingClass;
        set => _rowSpacingClass = value ?? "double";
    }

    [Browsable(false)]
    public double ColumnSpacing
    {
        get => _columnSpacing;
        set => SetProperty(ref _columnSpacing, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string ColumnSpacingClass
    {
        get => _columnSpacingClass;
        set => _columnSpacingClass = value ?? "double";
    }

    [Browsable(false)]
    public UiColor BackgroundColor
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Ui);
    }

    /// <summary>A grid has no border and no title: its children are placed in the whole of it.</summary>
    public override Thickness Insets() => new(0);

    /// <summary>
    /// How far a scrollable grid is scrolled, in pixels from the left of what it holds. The layout
    /// keeps it within what there is to scroll.
    /// </summary>
    [Browsable(false)]
    public double ScrollX
    {
        get => _scrollX;
        set => SetProperty(ref _scrollX, value, InvalidationKind.Ui);
    }

    /// <summary>How far a scrollable grid is scrolled, in pixels from the top of what it holds.</summary>
    [Browsable(false)]
    public double ScrollY
    {
        get => _scrollY;
        set => SetProperty(ref _scrollY, value, InvalidationKind.Ui);
    }

    /// <summary>Everything placed in the grid, components first and then axes, each in its own order.</summary>
    public IReadOnlyList<GraphObject> Placed()
    {
        var placed = new List<GraphObject>(Components.Count);
        placed.AddRange(Components);
        placed.AddRange(ContainedAxes());
        return placed;
    }

    /// <summary>The cell a child of this grid sits in; (1, 1) for one that was never placed.</summary>
    public static UiGridCell CellOf(GraphObject child) => child switch
    {
        UiObject component => component.GridCell ?? new UiGridCell(1, 1),
        AxesModel axes => axes.GridCell ?? new UiGridCell(1, 1),
        _ => new UiGridCell(1, 1),
    };

    /// <summary>
    /// Gives a newcomer its cell, R2025b's way (probe <c>u5_grid</c>): the cell after the furthest
    /// one any child reaches, reading row by row; the first cell when the grid is empty. A new row is
    /// added when the last one is full.
    /// </summary>
    public UiGridCell NextCell(GraphObject newcomer)
    {
        int row = 0;
        int column = 0;
        foreach (GraphObject child in Placed())
        {
            if (ReferenceEquals(child, newcomer))
            {
                continue;
            }

            UiGridCell cell = CellOf(child);
            if (cell.RowEnd > row || (cell.RowEnd == row && cell.ColumnEnd > column))
            {
                row = cell.RowEnd;
                column = cell.ColumnEnd;
            }
        }

        if (row == 0)
        {
            return new UiGridCell(1, 1);
        }

        return column < System.Math.Max(1, Columns.Count) ? new UiGridCell(row, column + 1) : new UiGridCell(row + 1, 1);
    }

    /// <summary>A child has been given a cell: the grid's tracks may have changed with it.</summary>
    public void Hold(UiGridCell cell)
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Columns));
        Invalidate(InvalidationKind.Ui);
    }

    private int NeededRows()
    {
        int needed = 0;
        foreach (GraphObject child in Placed())
        {
            needed = System.Math.Max(needed, CellOf(child).RowEnd);
        }

        return needed;
    }

    private int NeededColumns()
    {
        int needed = 0;
        foreach (GraphObject child in Placed())
        {
            needed = System.Math.Max(needed, CellOf(child).ColumnEnd);
        }

        return needed;
    }

    private static IReadOnlyList<UiGridTrack> Grown(IReadOnlyList<UiGridTrack> tracks, int count)
    {
        if (tracks.Count >= count)
        {
            return tracks;
        }

        var grown = new List<UiGridTrack>(tracks);
        while (grown.Count < count)
        {
            grown.Add(UiGridTrack.One);
        }

        return grown;
    }

    /// <summary>
    /// The grid's own rectangle: the whole of the area its parent gives its children, or its cell
    /// when it sits in another grid.
    /// </summary>
    public Rect2D OwnPixelRect()
    {
        if (Parent is UiGridLayoutModel outer)
        {
            return outer.RectOf(this);
        }

        Size2D area = Parent switch
        {
            UiPanelModel panel => panel.GridArea(),
            IUiContainer holder => holder.InnerPixelSize,
            _ => new Size2D(100, 100),
        };
        return new Rect2D(1, 1, area.Width, area.Height);
    }

    [ThreadStatic]
    private static Dictionary<UiGridLayoutModel, UiGridArrangement>? _remembered;

    /// <summary>
    /// For as long as the answer is held, a grid on this thread is laid out once however many of its
    /// children ask where they are — what taking a frame of a figure full of grids needs. Nothing may
    /// change the grids meanwhile.
    /// </summary>
    public static IDisposable Remembering()
    {
        if (_remembered is not null)
        {
            return new Forgetting(restore: false);
        }

        _remembered = new Dictionary<UiGridLayoutModel, UiGridArrangement>(ReferenceEqualityComparer.Instance);
        return new Forgetting(restore: true);
    }

    private sealed class Forgetting(bool restore) : IDisposable
    {
        public void Dispose()
        {
            if (restore)
            {
                _remembered = null;
            }
        }
    }

    /// <summary>This grid laid out at the size it has now, with its children in <see cref="Placed"/> order.</summary>
    public UiGridArrangement Arrange()
    {
        if (_remembered is { } remembered && remembered.TryGetValue(this, out UiGridArrangement? known))
        {
            return known;
        }

        Rect2D own = OwnPixelRect();
        UiGridArrangement arrangement = Arrange(new Size2D(own.Width, own.Height));
        _remembered?.TryAdd(this, arrangement);
        return arrangement;
    }

    /// <summary>
    /// Pins each axes placed in this grid to its cell, in pixels, so that everything which places an
    /// axes — the renderer, a script's <c>Position</c>, a click — finds it where the grid put it.
    /// Script thread only.
    /// </summary>
    public void PinAxes()
    {
        IReadOnlyList<GraphObject> placed = Placed();
        UiGridArrangement? arrangement = null;
        for (int i = 0; i < placed.Count; i++)
        {
            if (placed[i] is not AxesModel axes)
            {
                continue;
            }

            arrangement ??= Arrange();
            Rect2D cell = arrangement.Cells[i];
            if (axes.Units != UiUnits.Pixels)
            {
                axes.Units = UiUnits.Pixels;
            }

            if (axes.InnerTarget is not null)
            {
                axes.InnerTarget = null;
            }

            if (axes.PixelBounds != cell)
            {
                axes.PixelBounds = cell;
            }
        }
    }

    /// <summary>This grid laid out at a size, or — with none — at the size it asks for.</summary>
    public UiGridArrangement Arrange(Size2D? size)
    {
        IReadOnlyList<GraphObject> placed = Placed();
        var items = new UiGridItem[placed.Count];
        for (int i = 0; i < placed.Count; i++)
        {
            items[i] = new UiGridItem(CellOf(placed[i]), UiFit.Of(placed[i]));
        }

        if (size is not { } given || !Scrollable)
        {
            return UiGridMath.Arrange(Rows, Columns, _padding, _rowSpacing, _columnSpacing, size, items);
        }

        // A grid that scrolls lays out in its viewport, which is the top-left of its rectangle: a
        // bar along the bottom lifts every cell by its height.
        UiGridScrolled scrolled = UiGridMath.ArrangeScrolling(Rows, Columns, _padding, _rowSpacing, _columnSpacing, given, items);
        if (!scrolled.Horizontal)
        {
            return scrolled.Arrangement;
        }

        return scrolled.Arrangement with
        {
            Cells = [.. scrolled.Arrangement.Cells.Select(static cell => new Rect2D(cell.X, cell.Y + UiGridMath.ScrollBarSize, cell.Width, cell.Height))],
        };
    }

    /// <summary>A child's cell as MATLAB's pixel rectangle in the grid's area.</summary>
    public Rect2D RectOf(GraphObject child)
    {
        IReadOnlyList<GraphObject> placed = Placed();
        for (int i = 0; i < placed.Count; i++)
        {
            if (ReferenceEquals(placed[i], child))
            {
                return Arrange().Cells[i];
            }
        }

        return new Rect2D(1, 1, 0, 0);
    }

    /// <inheritdoc cref="UiContainerModel.InnerPixelSize" />
    public Size2D OwnSize()
    {
        Rect2D own = OwnPixelRect();
        return new Size2D(own.Width, own.Height);
    }
}
