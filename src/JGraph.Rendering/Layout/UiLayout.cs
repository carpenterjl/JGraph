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
    IReadOnlyList<Rect2D> Occluders);

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
    IReadOnlyList<UiPanelPlacement> Children);

/// <summary>The layout of one frame at one figure size.</summary>
public sealed class UiLayoutResult
{
    private readonly Dictionary<UiContainerModel, UiPanelPlacement> _bySource;

    internal UiLayoutResult(
        IReadOnlyList<UiPlacement> controls,
        IReadOnlyList<UiPanelPlacement> roots,
        Dictionary<UiContainerModel, UiPanelPlacement> bySource)
    {
        Controls = controls;
        Panels = roots;
        _bySource = bySource;
    }

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
        IReadOnlyList<UiPanelPlacement> roots = walk.Place(frame.Roots, surface, surface, visible: true, depth: 0, []);

        // A panel painted after a control, and not one of its ancestors, lies over it: the control is
        // a window element above the drawn surface, so it has to be cut away where the panel crosses.
        var controls = new List<UiPlacement>(walk.Controls.Count);
        foreach ((UiPlacement placement, int order, IReadOnlyList<UiPanelFrame> ancestors) in walk.Controls)
        {
            List<Rect2D>? occluders = null;
            foreach ((UiPanelPlacement panel, int panelOrder) in walk.Panels)
            {
                if (panelOrder > order && panel.Visible && !ancestors.Contains(panel.Panel)
                    && Intersect(panel.Box, panel.Clip) is { IsEmpty: false } shown
                    && Intersect(shown, placement.Box) is { IsEmpty: false })
                {
                    (occluders ??= []).Add(shown);
                }
            }

            controls.Add(placement with { Occluders = occluders ?? (IReadOnlyList<Rect2D>)[] });
        }

        return new UiLayoutResult(controls, roots, walk.BySource);
    }

    /// <summary>The controls of <see cref="Compute"/>, for a caller that wants nothing else.</summary>
    public static IReadOnlyList<UiPlacement> Place(UiFrame frame, Size2D figure) => Compute(frame, figure).Controls;

    private sealed class Walk
    {
        private int _order;

        public List<(UiPlacement Placement, int Order, IReadOnlyList<UiPanelFrame> Ancestors)> Controls { get; } = [];

        public List<(UiPanelPlacement Panel, int Order)> Panels { get; } = [];

        public Dictionary<UiContainerModel, UiPanelPlacement> BySource { get; } = new(ReferenceEqualityComparer.Instance);

        public IReadOnlyList<UiPanelPlacement> Place(
            IReadOnlyList<IUiNodeFrame> nodes, Rect2D area, Rect2D clip, bool visible, int depth, IReadOnlyList<UiPanelFrame> ancestors)
        {
            var panels = new List<UiPanelPlacement>();
            var size = new Size2D(area.Width, area.Height);
            foreach (IUiNodeFrame node in nodes)
            {
                Rect2D pixels = UiUnitConverter.ToPixels(node.Position, node.Units, size);
                var box = new Rect2D(
                    area.X + pixels.X - 1,
                    area.Y + area.Height - (pixels.Y - 1) - pixels.Height,
                    pixels.Width,
                    pixels.Height);
                bool shown = visible && node.Visible;
                int order = _order++;
                switch (node)
                {
                    case UiControlFrame control:
                        Controls.Add((new UiPlacement(control, box, shown, clip, []), order, ancestors));
                        break;

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
                        IReadOnlyList<UiPanelPlacement> children =
                            Place(panel.Children, inner, innerClip, shown, depth + 1, inside);
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
