using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Rendering.Layout;

/// <summary>
/// Where one control goes on the figure's surface, top-left origin, in DIPs. <c>Visible</c> is the
/// control's own and every ancestor's; <c>Clip</c> is the part of the surface its containers leave
/// it; <c>Occluders</c> are the panels stacked over it, which hide it where they cross it.
/// </summary>
public readonly record struct UiPlacement(
    UiControlFrame Control,
    Rect2D Box,
    bool Visible,
    Rect2D Clip,
    IReadOnlyList<Rect2D> Occluders,
    int Order = 0);

/// <summary>
/// Where one <c>uifigure</c> component goes (U5), as <see cref="UiPlacement"/> says of a classic
/// control. <c>Order</c> is its place in the painting order among controls and components alike.
/// </summary>
public readonly record struct UiComponentPlacement(
    UiComponentFrame Component,
    Rect2D Box,
    bool Visible,
    Rect2D Clip,
    IReadOnlyList<Rect2D> Occluders,
    int Order);

/// <summary>
/// Where one panel goes: its whole box, the inner area its children and axes are placed in, and the
/// part of the surface its ancestors leave it. <c>Depth</c> counts its container ancestors.
/// </summary>
public sealed record UiPanelPlacement(
    UiPanelFrame Panel,
    Rect2D Box,
    Rect2D Inner,
    Rect2D Clip,
    bool Visible,
    int Depth,
    IReadOnlyList<UiPanelPlacement> Children)
{
    /// <summary>
    /// For a grid: the cell of each axes placed in it, on the figure's surface — where the renderer
    /// draws that axes, so it follows a window being resized before its script has heard of it.
    /// </summary>
    public IReadOnlyDictionary<AxesModel, Rect2D>? AxesCells { get; init; }

    /// <summary>For a grid: the size its tracks, spacing and padding come to.</summary>
    public Size2D Content { get; init; }

    /// <summary>
    /// For a scrollable grid whose tracks do not fit: the bar along its right edge and the bar along
    /// its bottom, on the surface, and how far it is scrolled from the left and from the top. The
    /// part of it that shows what it holds is <see cref="Inner"/>.
    /// </summary>
    public Rect2D? VerticalBar { get; init; }

    /// <inheritdoc cref="VerticalBar" />
    public Rect2D? HorizontalBar { get; init; }

    /// <inheritdoc cref="VerticalBar" />
    public double ScrollX { get; init; }

    /// <inheritdoc cref="VerticalBar" />
    public double ScrollY { get; init; }
}

/// <summary>The layout of one frame at one figure size.</summary>
public sealed class UiLayoutResult
{
    private readonly Dictionary<UiContainerModel, UiPanelPlacement> _bySource;

    internal UiLayoutResult(
        IReadOnlyList<UiPlacement> controls,
        IReadOnlyList<UiComponentPlacement> components,
        IReadOnlyList<UiPanelPlacement> roots,
        Dictionary<UiContainerModel, UiPanelPlacement> bySource)
    {
        Controls = controls;
        Components = components;
        Panels = roots;
        _bySource = bySource;
    }

    /// <summary>Every <c>uifigure</c> component, in painting order (U5).</summary>
    public IReadOnlyList<UiComponentPlacement> Components { get; }

    /// <summary>Every control, in painting order.</summary>
    public IReadOnlyList<UiPlacement> Controls { get; }

    /// <summary>The figure's own panels, back to front; each holds the panels inside it.</summary>
    public IReadOnlyList<UiPanelPlacement> Panels { get; }

    /// <summary>The placement of a container, when the frame holds it.</summary>
    public UiPanelPlacement? Find(UiContainerModel? container) =>
        container is not null && _bySource.TryGetValue(container, out UiPanelPlacement? placement) ? placement : null;
}

/// <summary>
/// The pure layout of a frame (app-building plan, section A): from a <see cref="UiFrame"/> and the
/// size of the figure's drawable area to the rectangle each container and component occupies, what
/// clips it and whether it shows. The window's component layer and the renderer read the same
/// answer, and a window resize runs it again on the last frame without asking the script.
/// </summary>
public static class UiLayout
{
    /// <summary>
    /// MATLAB places a component by its bottom-left corner in its parent's inner area, in the
    /// component's own units (see <see cref="UiUnitConverter"/>); a pixel is a DIP and pixel 1 is the
    /// parent's edge.
    /// </summary>
    public static UiLayoutResult Compute(UiFrame frame, Size2D figure)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var surface = new Rect2D(0, 0, figure.Width, figure.Height);
        var walk = new Walk();
        IReadOnlyList<UiPanelPlacement> roots = walk.Place(frame.Roots, surface, surface, surface, visible: true, depth: 0, [], null);

        // A panel painted after a control, and not one of its ancestors, lies over it: the control is
        // a window element above the drawn surface, so it has to be cut away where the panel crosses.
        IReadOnlyList<Rect2D> Over(Rect2D box, int order, IReadOnlyList<UiPanelFrame> ancestors)
        {
            List<Rect2D>? occluders = null;
            foreach ((UiPanelPlacement panel, int panelOrder) in walk.Panels)
            {
                if (panelOrder > order && panel.Visible && !ancestors.Contains(panel.Panel)
                    && Intersect(panel.Box, panel.Clip) is { IsEmpty: false } shown
                    && Intersect(shown, box) is { IsEmpty: false })
                {
                    (occluders ??= []).Add(shown);
                }
            }

            return occluders ?? (IReadOnlyList<Rect2D>)[];
        }

        var controls = new List<UiPlacement>(walk.Controls.Count);
        foreach ((UiPlacement placement, int order, IReadOnlyList<UiPanelFrame> ancestors) in walk.Controls)
        {
            controls.Add(placement with { Occluders = Over(placement.Box, order, ancestors), Order = order });
        }

        var components = new List<UiComponentPlacement>(walk.Components.Count);
        foreach ((UiComponentPlacement placement, IReadOnlyList<UiPanelFrame> ancestors) in walk.Components)
        {
            components.Add(placement with { Occluders = Over(placement.Box, placement.Order, ancestors) });
        }

        return new UiLayoutResult(controls, components, roots, walk.BySource);
    }

    /// <summary>The controls of <see cref="Compute"/>, for a caller that wants nothing else.</summary>
    public static IReadOnlyList<UiPlacement> Place(UiFrame frame, Size2D figure) => Compute(frame, figure).Controls;

    private sealed class Walk
    {
        private int _order;

        public List<(UiPlacement Placement, int Order, IReadOnlyList<UiPanelFrame> Ancestors)> Controls { get; } = [];

        public List<(UiComponentPlacement Placement, IReadOnlyList<UiPanelFrame> Ancestors)> Components { get; } = [];

        public List<(UiPanelPlacement Panel, int Order)> Panels { get; } = [];

        public Dictionary<UiContainerModel, UiPanelPlacement> BySource { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Places the nodes of one container. <paramref name="area"/> is where its children are
        /// placed; <paramref name="gridArea"/> is what a grid among them fills, which in a titled
        /// panel is a little less; <paramref name="cells"/>, when the container is itself a grid,
        /// is the cell it gave each node, as MATLAB's pixel rectangle in <paramref name="area"/>.
        /// </summary>
        public IReadOnlyList<UiPanelPlacement> Place(
            IReadOnlyList<IUiNodeFrame> nodes, Rect2D area, Rect2D gridArea, Rect2D clip, bool visible, int depth,
            IReadOnlyList<UiPanelFrame> ancestors, IReadOnlyList<Rect2D>? cells)
        {
            var panels = new List<UiPanelPlacement>();
            var size = new Size2D(area.Width, area.Height);
            for (int index = 0; index < nodes.Count; index++)
            {
                IUiNodeFrame node = nodes[index];
                Rect2D box;
                if (cells is not null)
                {
                    Rect2D cell = cells[index];
                    if (node is UiComponentFrame { Kind: UiComponentKind.Slider or UiComponentKind.RangeSlider } slider)
                    {
                        cell = UiSliderModel.TrackInCell(cell, slider.Upright);
                    }

                    box = OnSurface(area, cell);
                }
                else if (node is UiGridFrame)
                {
                    box = gridArea;
                }
                else
                {
                    box = OnSurface(area, UiUnitConverter.ToPixels(node.Position, node.Units, size));
                }

                bool shown = visible && node.Visible;
                int order = _order++;
                switch (node)
                {
                    case UiControlFrame control:
                        Controls.Add((new UiPlacement(control, box, shown, clip, []), order, ancestors));
                        break;

                    case UiComponentFrame component:
                        Components.Add((new UiComponentPlacement(component, box, shown, clip, [], order), ancestors));
                        break;

                    case UiGridFrame grid:
                    {
                        var items = new List<UiGridItem>(grid.Children.Count + grid.Axes.Count);
                        foreach (IUiNodeFrame child in grid.Children)
                        {
                            items.Add(new UiGridItem(child.Cell ?? new UiGridCell(1, 1), child.Fit));
                        }

                        foreach (UiAxesCellFrame axes in grid.Axes)
                        {
                            items.Add(new UiGridItem(axes.Cell, axes.Fit));
                        }

                        // A grid that scrolls shows what it holds through a viewport in its top-left
                        // corner, with a bar along each edge it overflows, and is moved under it.
                        UiGridScrolled scrolled = grid.Scrollable
                            ? UiGridMath.ArrangeScrolling(
                                grid.Rows, grid.Columns, grid.Padding, grid.RowSpacing, grid.ColumnSpacing,
                                new Size2D(box.Width, box.Height), items)
                            : new UiGridScrolled(
                                UiGridMath.Arrange(
                                    grid.Rows, grid.Columns, grid.Padding, grid.RowSpacing, grid.ColumnSpacing,
                                    new Size2D(box.Width, box.Height), items),
                                new Size2D(box.Width, box.Height), false, false);
                        UiGridArrangement arrangement = scrolled.Arrangement;
                        var viewport = new Rect2D(box.X, box.Y, scrolled.Viewport.Width, scrolled.Viewport.Height);
                        double scrollX = scrolled.Horizontal
                            ? System.Math.Clamp(grid.ScrollX, 0, System.Math.Max(0, arrangement.Content.Width - viewport.Width))
                            : 0;
                        double scrollY = scrolled.Vertical
                            ? System.Math.Clamp(grid.ScrollY, 0, System.Math.Max(0, arrangement.Content.Height - viewport.Height))
                            : 0;
                        var moved = new Rect2D(viewport.X - scrollX, viewport.Y - scrollY, viewport.Width, viewport.Height);

                        // A grid is drawn as a panel with no border and no title: its background.
                        var drawn = new UiPanelFrame(
                            grid.Source, new Rect2D(1, 1, box.Width, box.Height), UiUnits.Pixels, grid.Visible, UiEnable.On,
                            string.Empty, UiTitlePosition.LeftTop, UiBorderType.None, 0, grid.Background, null, null, null,
                            grid.Background, string.Empty, 12, false, false, new Thickness(0), grid.Children);
                        Rect2D gridClip = Intersect(clip, viewport);
                        var within = new List<UiPanelFrame>(ancestors) { drawn };
                        int gridSlot = Panels.Count;
                        Panels.Add((null!, order));
                        IReadOnlyList<UiPanelPlacement> gridChildren = Place(
                            grid.Children, moved, moved, gridClip, shown, depth + 1, within,
                            [.. arrangement.Cells.Take(grid.Children.Count)]);
                        var axesCells = new Dictionary<AxesModel, Rect2D>(ReferenceEqualityComparer.Instance);
                        for (int a = 0; a < grid.Axes.Count; a++)
                        {
                            axesCells[grid.Axes[a].Source] = OnSurface(moved, arrangement.Cells[grid.Children.Count + a]);
                        }

                        var gridPlacement = new UiPanelPlacement(drawn, box, viewport, clip, shown, depth, gridChildren)
                        {
                            AxesCells = axesCells,
                            Content = arrangement.Content,
                            VerticalBar = scrolled.Vertical
                                ? new Rect2D(box.X + viewport.Width, box.Y, box.Width - viewport.Width, viewport.Height)
                                : null,
                            HorizontalBar = scrolled.Horizontal
                                ? new Rect2D(box.X, box.Y + viewport.Height, viewport.Width, box.Height - viewport.Height)
                                : null,
                            ScrollX = scrollX,
                            ScrollY = scrollY,
                        };
                        Panels[gridSlot] = (gridPlacement, order);
                        BySource[grid.Source] = gridPlacement;
                        panels.Add(gridPlacement);
                        break;
                    }

                    case UiPanelFrame panel:
                        Thickness inset = panel.Insets;
                        var inner = new Rect2D(
                            box.X + inset.Left,
                            box.Y + inset.Top,
                            System.Math.Max(0, box.Width - inset.Left - inset.Right),
                            System.Math.Max(0, box.Height - inset.Top - inset.Bottom));
                        Rect2D innerClip = Intersect(clip, inner);
                        var inside = new List<UiPanelFrame>(ancestors) { panel };
                        int slot = Panels.Count;
                        Panels.Add((null!, order));
                        Thickness forGrid = panel.GridInsets ?? inset;
                        var gridInner = new Rect2D(
                            box.X + forGrid.Left,
                            box.Y + forGrid.Top,
                            System.Math.Max(0, box.Width - forGrid.Left - forGrid.Right),
                            System.Math.Max(0, box.Height - forGrid.Top - forGrid.Bottom));
                        IReadOnlyList<UiPanelPlacement> children =
                            Place(panel.Children, inner, gridInner, innerClip, shown, depth + 1, inside, null);
                        var placement = new UiPanelPlacement(panel, box, inner, clip, shown, depth, children);
                        Panels[slot] = (placement, order);
                        BySource[panel.Source] = placement;
                        panels.Add(placement);
                        break;
                }
            }

            return panels;
        }
    }

    /// <summary>MATLAB's pixel rectangle in an area as a rectangle on the surface, Y downward.</summary>
    private static Rect2D OnSurface(Rect2D area, Rect2D pixels) => new(
        area.X + pixels.X - 1,
        area.Y + area.Height - (pixels.Y - 1) - pixels.Height,
        pixels.Width,
        pixels.Height);

    /// <summary>The overlap of two rectangles, empty when they do not meet.</summary>
    public static Rect2D Intersect(Rect2D a, Rect2D b)
    {
        double left = System.Math.Max(a.X, b.X);
        double top = System.Math.Max(a.Y, b.Y);
        double right = System.Math.Min(a.X + a.Width, b.X + b.Width);
        double bottom = System.Math.Min(a.Y + a.Height, b.Y + b.Height);
        return right > left && bottom > top ? new Rect2D(left, top, right - left, bottom - top) : new Rect2D(left, top, 0, 0);
    }

    /// <summary>
    /// The font a component names, as the machine has it: MATLAB's classic default MS Sans Serif is a
    /// bitmap font Windows maps to Microsoft Sans Serif, and Helvetica, the uifigure default, is Arial
    /// here. One table, so text is measured and drawn in the same face.
    /// </summary>
    public static string FontFamily(string name) => UiFonts.Family(name);
}
