using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>
/// A splitter between two regions of a responsive app (App Designer's <c>SplitterWidget</c>): the
/// leading region's right edge, or the trailing region's left edge in a 3-panel app; 7 pixels wide.
/// </summary>
public readonly record struct DesignSplitter(bool Leading, Rect2D Box);

/// <summary>
/// A splitter dragged: each region's left and width after it, how far each region's components move
/// across (<c>LayoutResizeEngine.resizeBySplitter</c>, <c>ChildrenPositionsCalculator.adjustByBorder</c>),
/// how much of the drag the regions took, and whether they stopped short of it - App Designer's alert.
/// </summary>
public sealed record DesignRegionResize(IReadOnlyList<(double X, double Width)> Regions, IReadOnlyList<double> ChildShift, double Used, bool Stopped)
{
    /// <summary>
    /// This step of a drag after <paramref name="before"/>, the steps before it in the same drag: the
    /// regions where this step leaves them, the components moved by every step.
    /// </summary>
    public DesignRegionResize After(DesignRegionResize? before) =>
        before is null ? this : this with { ChildShift = [.. ChildShift.Zip(before.ChildShift, static (a, b) => a + b)] };
}

/// <summary>A responsive app's regions on the canvas (designer plan DS4b).</summary>
public sealed partial class DesignDocument
{
    /// <summary>How far a region keeps its components from its edges (<c>LayoutTemplateResizeUtils.CHILDREN_INSET</c>).</summary>
    public const double RegionInset = 6;

    /// <summary>Whether <paramref name="component"/> is a region of a responsive app, which App Designer does not let be moved, resized or deleted.</summary>
    public bool IsRegion(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return IsResponsive && DesignResponsive.IsRegion(Design, component);
    }

    /// <summary>The region <paramref name="component"/> is inside, or null.</summary>
    public DesignComponent? RegionOf(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        for (DesignComponent? up = component.Parent; up is not null; up = up.Parent)
        {
            if (IsRegion(up))
            {
                return up;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the app is a responsive one, as its code says (<see cref="AppDesign.IsResponsive"/>):
    /// its figure's grid is locked and takes nothing, its figure takes nothing beside it.
    /// </summary>
    public bool IsResponsive => Design.IsResponsive;

    /// <summary>Whether the responsive app is a template whose regions can be split (<see cref="DesignResponsive.IsTemplate"/>).</summary>
    private bool HasRegions => DesignResponsive.IsTemplate(Design);

    /// <summary>
    /// Whether moving boxes covering <paramref name="union"/> are over a region's edge
    /// (<c>TemplateComponentMove.isOverEdgeOfRegion</c>): inside no region whole, and touching one -
    /// which App Designer refuses, so that nothing straddles two regions.
    /// </summary>
    public bool IsOverRegionEdge(Rect2D union)
    {
        if (!HasRegions)
        {
            return false;
        }

        bool inside = false;
        bool touches = false;
        foreach (DesignComponent? region in DesignResponsive.Regions(Design))
        {
            if (region is null || BoxOf(region) is not { } box)
            {
                continue;
            }

            Rect2D r = box.Box;
            inside |= union.Left >= r.Left && union.Right <= r.Right && union.Top >= r.Top && union.Bottom <= r.Bottom;
            touches |= union.Left <= r.Right && r.Left <= union.Right && union.Top <= r.Bottom && r.Top <= union.Bottom;
        }

        return !inside && touches;
    }

    /// <summary>
    /// The splitters (<c>LayoutTemplateManager</c>): on the leading region's right edge, and in a
    /// 3-panel app on the trailing region's left edge - 7 pixels wide, as tall as the region.
    /// </summary>
    public IReadOnlyList<DesignSplitter> Splitters()
    {
        if (!HasRegions)
        {
            return [];
        }

        IReadOnlyList<DesignComponent?> regions = DesignResponsive.Regions(Design);
        var splitters = new List<DesignSplitter>(2);
        if (BoxOf(regions[0]!) is { } leading)
        {
            Rect2D b = leading.Box;
            splitters.Add(new DesignSplitter(true, new Rect2D(b.Right - 4, b.Y, 7, b.Height)));
        }

        if (regions.Count == 3 && BoxOf(regions[2]!) is { } trailing)
        {
            Rect2D b = trailing.Box;
            splitters.Add(new DesignSplitter(false, new Rect2D(b.X - 3, b.Y, 7, b.Height)));
        }

        return splitters;
    }

    /// <summary>
    /// A splitter dragged across by <paramref name="d"/> pixels from where the regions stand now - on
    /// the canvas, with any drag shown there (<c>LayoutResizeEngine.resizeBySplitter</c> with its
    /// internal edge calculators): the region on the splitter's side grows by what the region it takes
    /// from has to spare - its width less what its components need, 6 pixels from each edge
    /// (<c>LayoutTemplateResizeUtils.calculateAvailableSize</c>) - in a 3-panel app the leading splitter
    /// then taking from the trailing region, the centre moving along; the splitter stops where nothing
    /// is left. Each region's components keep their place on the canvas while its edge passes them, and
    /// are pushed along within 6 pixels of it; a region whose only component is a grid has none to move.
    /// A drag is App Designer's steps, one for each move of the pointer, each from where the last left
    /// the regions and by what the pointer moved past the part of the drag the regions have taken
    /// (<c>_HandleDoingLayoutTemplateResize</c>), so that the splitter stays at its limit while the
    /// pointer goes on.
    /// </summary>
    public DesignRegionResize? ResizeRegions(DesignSplitter splitter, double d)
    {
        if (!HasRegions)
        {
            return null;
        }

        IReadOnlyList<DesignComponent?> regions = DesignResponsive.Regions(Design);
        int n = regions.Count;
        var x = new double[n];
        var width = new double[n];
        var minX = new double?[n];
        var maxX = new double?[n];
        for (int i = 0; i < n; i++)
        {
            Rect2D box = BoxOf(regions[i]!)!.Value.Box;
            x[i] = box.X;
            width[i] = box.Width;
            (minX[i], maxX[i]) = Extent(regions[i]!);
        }

        double Space(int i) =>
            minX[i] is not { } low || maxX[i] is not { } high ? System.Math.Max(0, width[i] - (2 * RegionInset))
            : low < RegionInset && high <= width[i] - RegionInset ? System.Math.Max(0, width[i] - RegionInset - high)
            : low >= RegionInset && high > width[i] - RegionInset ? System.Math.Max(0, low - RegionInset)
            : low < RegionInset && high > width[i] - RegionInset ? 0
            : System.Math.Max(0, width[i] - (2 * RegionInset) - (high - low));

        var left = new double[n];  // how far each region's left edge moves
        var right = new double[n]; // and its right edge
        double used;
        if (splitter.Leading)
        {
            int centre = 1;
            if (d > 0)
            {
                double r1 = System.Math.Min(d, Space(centre));
                right[0] += r1;
                left[centre] += r1;
                used = r1;
                if (n == 3 && d - r1 > 0)
                {
                    double r2 = System.Math.Min(d - r1, Space(2));
                    right[0] += r2;
                    left[centre] += r2;
                    right[centre] += r2;
                    left[2] += r2;
                    used += r2;
                }
            }
            else
            {
                double r = -System.Math.Min(-d, Space(0));
                right[0] += r;
                left[centre] += r;
                used = r;
            }
        }
        else
        {
            if (d < 0)
            {
                double r1 = -System.Math.Min(-d, Space(1));
                right[1] += r1;
                left[2] += r1;
                used = r1;
                if (d - r1 < 0)
                {
                    double r2 = -System.Math.Min(r1 - d, Space(0));
                    right[0] += r2;
                    left[1] += r2;
                    right[1] += r2;
                    left[2] += r2;
                    used += r2;
                }
            }
            else
            {
                double r = System.Math.Min(d, Space(2));
                left[2] += r;
                right[1] += r;
                used = r;
            }
        }

        var after = new (double X, double Width)[n];
        var shift = new double[n];
        for (int i = 0; i < n; i++)
        {
            after[i] = (x[i] + left[i], width[i] + right[i] - left[i]);

            // A region moved whole carries its components; an edge that moves besides passes them by.
            double carried = left[i] * right[i] > 0 ? (System.Math.Abs(left[i]) < System.Math.Abs(right[i]) ? left[i] : right[i]) : 0;
            double leftEdge = left[i] - carried;
            double rightEdge = right[i] - carried;
            if (minX[i] is not { } low || maxX[i] is not { } high)
            {
                continue;
            }

            if (leftEdge < 0)
            {
                shift[i] = -leftEdge;
            }
            else if (leftEdge > 0)
            {
                shift[i] = -System.Math.Max(0, System.Math.Min(low - RegionInset, leftEdge));
            }
            else if (rightEdge < 0)
            {
                shift[i] = System.Math.Min(System.Math.Max(width[i] - high - RegionInset, 0) + rightEdge, 0);
            }
        }

        return new DesignRegionResize(after, shift, used, System.Math.Abs(used - d) > 1e-9);
    }

    /// <summary>
    /// The left and right of what a region holds, from its inner area's left
    /// (<c>LayoutTemplateResizeUtils.getChildrenForRegion</c>); none for a region whose only component
    /// is a grid, which fills it.
    /// </summary>
    private (double? Min, double? Max) Extent(DesignComponent region)
    {
        if (region.Children is [{ Type: GridType }])
        {
            return (null, null);
        }

        Rect2D inner = Runtime.InnerOnSurface(region);
        double? min = null;
        double? max = null;
        foreach (DesignComponent child in region.Children)
        {
            if (BoxOf(child) is { } box)
            {
                min = System.Math.Min(min ?? double.MaxValue, box.Box.Left - inner.X);
                max = System.Math.Max(max ?? double.MinValue, box.Box.Right - inner.X);
            }
        }

        return (min, max);
    }

    /// <summary>
    /// Shows <paramref name="resize"/> on the canvas, on the runtime's components only: the side
    /// regions' columns and the regions' components moved across. Null puts them back.
    /// </summary>
    public void PreviewRegions(DesignRegionResize? resize)
    {
        // Nothing is undone or redone while a drag is shown: the runtime holds the drag, not the design.
        _regionPreview = resize is not null;
        if (!HasRegions || DesignResponsive.Grid(Design) is not { } grid)
        {
            return;
        }

        IReadOnlyList<DesignComponent?> regions = DesignResponsive.Regions(Design);
        Runtime.Set(grid, "ColumnWidth", resize is null ? DesignTimeColumns(grid) : Columns(grid, resize));
        for (int i = 0; i < regions.Count; i++)
        {
            foreach (DesignComponent child in regions[i]!.Children)
            {
                if (child.Get("Position") is NumericValue { Data: [var cx, var cy, var cw, var ch] })
                {
                    Runtime.Set(child, "Position", NumericValue.Row(cx + (resize?.ChildShift[i] ?? 0), cy, cw, ch));
                }
            }
        }

        _boxes = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ends a splitter's drag at <paramref name="resize"/> as one edit (App Designer's region resize
    /// command with its children's adjusting): the side regions' columns, and each region's components
    /// moved across; the generated <c>updateAppLayout</c> follows the widths on save.
    /// </summary>
    public void CommitRegions(DesignRegionResize resize)
    {
        ArgumentNullException.ThrowIfNull(resize);
        if (!HasRegions || DesignResponsive.Grid(Design) is not { } grid)
        {
            return;
        }

        PreviewRegions(null);
        IReadOnlyList<DesignComponent?> regions = DesignResponsive.Regions(Design);
        var edits = new List<Edit>();

        // The columns, when a side region's width is not what they hold (LayoutTemplateManager._handleRegionPropertySet).
        CellValue columns = Columns(grid, resize);
        IReadOnlyList<DesignValue> held = DesignResponsive.Tracks(grid.Get("ColumnWidth"));
        if (Enumerable.Range(0, regions.Count).Where(static i => i != 1)
            .Any(i => held.ElementAtOrDefault(i) is not NumericValue { Data: [var pixels] } || pixels != ((NumericValue)columns.Items[i]).Data[0]))
        {
            edits.Add(new PropertyEdit(grid, "ColumnWidth", grid.Get("ColumnWidth"), columns, DesignTimeColumns(grid), columns) { Selects = false });
        }

        // Each region the drag left elsewhere has its components moved as the drag moved them; one back
        // where it began has nothing (cacheRegionsResizeCommand's isSame), whatever the drag did on the way.
        for (int i = 0; i < regions.Count; i++)
        {
            Rect2D before = BoxOf(regions[i]!)!.Value.Box;
            if (resize.ChildShift[i] == 0 || (before.X == resize.Regions[i].X && before.Width == resize.Regions[i].Width))
            {
                continue;
            }

            foreach (DesignComponent child in regions[i]!.Children)
            {
                if (child.Get("Position") is NumericValue { Data: [var cx, var cy, var cw, var ch] } position)
                {
                    NumericValue moved = NumericValue.Row(cx + resize.ChildShift[i], cy, cw, ch);
                    edits.Add(new PositionEdit(child, position, moved, position) { Selects = false });
                }
            }
        }

        if (edits.Count > 0)
        {
            // The selection is the user's still: App Designer's splitter selects nothing.
            Do([.. edits, .. Selection.Select(static c => new SelectEdit(c))]);
        }
    }

    private bool _regionPreview;

    /// <summary>
    /// The responsive grid's columns with the side regions' widths of <paramref name="resize"/>, whole
    /// pixels, and the centre <c>'1x'</c>, which takes the rest of the figure as App Designer's centre
    /// region does (<c>ResponsiveAppFactory._adjustRegionPanelData</c>).
    /// </summary>
    private static CellValue Columns(DesignComponent grid, DesignRegionResize resize)
    {
        var items = DesignResponsive.Tracks(grid.Get("ColumnWidth")).ToArray();
        items[0] = NumericValue.Scalar(System.Math.Round(resize.Regions[0].Width, MidpointRounding.AwayFromZero));
        items[1] = new CharValue("1x");
        if (items.Length == 3)
        {
            items[2] = NumericValue.Scalar(System.Math.Round(resize.Regions[2].Width, MidpointRounding.AwayFromZero));
        }

        return CellValue.Row(items);
    }

    /// <summary>
    /// Lays a responsive app's regions out on the canvas as App Designer places them at design time
    /// (<c>ResponsiveAppFactory._adjustRegionPanelData</c>): the side columns as the code gives them,
    /// the centre taking the rest of the figure whatever the code gives it.
    /// </summary>
    private void LayOutRegions()
    {
        if (HasRegions && DesignResponsive.Grid(Design) is { } grid)
        {
            Runtime.Set(grid, "ColumnWidth", DesignTimeColumns(grid));
        }
    }

    /// <summary>The responsive grid's columns as the canvas lays them out (<see cref="LayOutRegions"/>).</summary>
    private DesignValue DesignTimeColumns(DesignComponent grid)
    {
        if (!HasRegions)
        {
            return TracksValue(grid);
        }

        DesignValue[] items = [.. DesignResponsive.Tracks(grid.Get("ColumnWidth"))];
        items[1] = new CharValue("1x");
        return CellValue.Row(items);
    }

    /// <summary>A grid's tracks as the design holds them, or the class's default.</summary>
    private DesignValue TracksValue(DesignComponent grid) =>
        grid.Get("ColumnWidth") ?? grid.Class?.Property("ColumnWidth")?.DefaultFor(Theme) ?? CellValue.Row();
}
