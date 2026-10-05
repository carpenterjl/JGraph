using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>One node of a frame: a control, or a container with the nodes inside it.</summary>
public interface IUiNodeFrame
{
    /// <summary>MATLAB's <c>Position</c>, in <see cref="Units"/>.</summary>
    Rect2D Position { get; }

    UiUnits Units { get; }

    /// <summary>The node's own <c>Visible</c>; what shows is this and every ancestor's.</summary>
    bool Visible { get; }

    /// <summary>The node's cell, when its parent is a grid (U5).</summary>
    UiGridCell? Cell => null;

    /// <summary>The size the node asks for in a <c>'fit'</c> track of a grid (U5).</summary>
    Size2D Fit => default;
}

/// <summary>
/// One <c>uicontrol</c> as a frame snapshot holds it: every value the window needs to draw and place
/// it, copied out of the model on the script thread, so the interface thread never reads a model the
/// script is writing. <c>Source</c> is there to route a user's action back and is never read;
/// <c>Position</c> is MATLAB's, in <c>Units</c>; <c>FontSize</c> is in pixels of 1/96 inch;
/// <c>UserWriteSeq</c> is the last user edit the model had taken.
/// </summary>
public sealed record UiControlFrame(
    UiControlModel Source,
    UiControlStyle Style,
    Rect2D Position,
    bool Visible,
    UiEnable Enable,
    UiText Text,
    UiHorizontalAlignment Alignment,
    UiColor? Background,
    UiColor? Foreground,
    string FontName,
    double FontSize,
    bool Bold,
    bool Italic,
    string Tooltip,
    long UserWriteSeq,
    UiUnits Units = UiUnits.Pixels,
    UiNumbers? Value = null,
    double Min = 0,
    double Max = 1,
    double StepSmall = 0.01,
    double StepLarge = 0.1,
    double ListboxTop = 1,
    UiImage? Image = null,
    bool InGroup = false,
    int FocusRequests = 0,
    string Tag = "") : IUiNodeFrame
{
    /// <summary>Whether <c>Max - Min</c> exceeds one: a multi-line edit field, a multiple-selection list.</summary>
    public bool IsMultiple => Max - Min > 1;

    /// <summary>The first element of <c>Value</c>, or NaN when it has none.</summary>
    public double Scalar => Value is { Data.Count: > 0 } value ? value.Data[0] : double.NaN;
}

/// <summary>
/// One <c>uipanel</c> as a frame snapshot holds it, with the nodes inside it in creation order.
/// <c>Insets</c> is how far its inner area stands in from each edge, in pixels, worked out on the
/// script thread by the model so that the window and a script's <c>InnerPosition</c> agree.
/// </summary>
public sealed record UiPanelFrame(
    UiContainerModel Source,
    Rect2D Position,
    UiUnits Units,
    bool Visible,
    UiEnable Enable,
    string Title,
    UiTitlePosition TitlePosition,
    UiBorderType BorderType,
    double BorderWidth,
    UiColor Background,
    UiColor? Foreground,
    UiColor? BorderColor,
    UiColor? Highlight,
    UiColor Shadow,
    string FontName,
    double FontSize,
    bool Bold,
    bool Italic,
    Thickness Insets,
    IReadOnlyList<IUiNodeFrame> Children,
    double? Fill = null,
    UiColor? FillColor = null,
    UiGridCell? Cell = null,
    Size2D Fit = default,
    Thickness? GridInsets = null) : IUiNodeFrame;

/// <summary>
/// One axes placed in a grid, as the grid's frame holds it: where it sits and what it asks for. The
/// layout answers its cell, so the renderer can keep it there while a window is being resized.
/// </summary>
public sealed record UiAxesCellFrame(AxesModel Source, UiGridCell Cell, Size2D Fit);

/// <summary>
/// One <c>uigridlayout</c> as a frame snapshot holds it (app-building plan, U5): its tracks, its
/// padding and spacing, and the nodes and axes inside it with their cells and the sizes they ask
/// for — everything the layout needs to place them again at any size without asking the script.
/// </summary>
public sealed record UiGridFrame(
    UiGridLayoutModel Source,
    bool Visible,
    IReadOnlyList<UiGridTrack> Rows,
    IReadOnlyList<UiGridTrack> Columns,
    IReadOnlyList<double> Padding,
    double RowSpacing,
    double ColumnSpacing,
    UiColor Background,
    bool Scrollable,
    IReadOnlyList<IUiNodeFrame> Children,
    IReadOnlyList<UiAxesCellFrame> Axes,
    UiGridCell? Cell = null,
    Size2D Fit = default,
    double ScrollX = 0,
    double ScrollY = 0) : IUiNodeFrame
{
    /// <summary>A grid has no place of its own: it fills the area its parent gives it.</summary>
    public Rect2D Position => new(1, 1, 0, 0);

    public UiUnits Units => UiUnits.Pixels;
}

/// <summary>
/// An immutable picture of a figure's components at one flush (app-building plan, section A): the
/// container tree with everything geometry and appearance depend on. The script thread takes it at a
/// flush point and hands it to the window, which lays it out and applies it; a window resize lays the
/// last one out again without asking the script.
/// </summary>
public sealed class UiFrame
{
    private UiFrame(FigureModel figure, long sequence, IReadOnlyList<IUiNodeFrame> roots)
    {
        Figure = figure;
        Sequence = sequence;
        Roots = roots;
        var controls = new List<UiControlFrame>();
        var components = new List<UiComponentFrame>();
        Flatten(roots, controls, components);
        Controls = controls;
        Components = components;
    }

    /// <summary>A frame with nothing in it.</summary>
    public static UiFrame Empty(FigureModel figure) => new(figure, 0, []);

    public FigureModel Figure { get; }

    /// <summary>Increases with every frame taken, process-wide; a later frame supersedes an earlier.</summary>
    public long Sequence { get; }

    /// <summary>The figure's own components, in creation order, which is also back-to-front.</summary>
    public IReadOnlyList<IUiNodeFrame> Roots { get; }

    /// <summary>Every control in the tree, depth first — the order they are painted in.</summary>
    public IReadOnlyList<UiControlFrame> Controls { get; }

    /// <summary>Every <c>uifigure</c> component in the tree, depth first (U5).</summary>
    public IReadOnlyList<UiComponentFrame> Components { get; }

    /// <summary>The dialogs laid over the figure — <c>uialert</c>, <c>uiconfirm</c>, <c>uiprogressdlg</c> — oldest first (U5).</summary>
    public IReadOnlyList<UiOverlayFrame> Overlays { get; private init; } = [];

    private static long _sequence;

    /// <summary>Copies the figure's components. Call on the thread that writes the model.</summary>
    public static UiFrame Take(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        using (UiGridLayoutModel.Remembering())
        {
            return new UiFrame(figure, Interlocked.Increment(ref _sequence), TakeNodes(figure.Components))
            {
                Overlays = [.. figure.Overlays.Select(static overlay => overlay.Snapshot())],
            };
        }
    }

    private static List<IUiNodeFrame> TakeNodes(IReadOnlyList<UiObject> components)
    {
        var nodes = new List<IUiNodeFrame>(components.Count);
        foreach (UiObject component in components)
        {
            bool inGrid = component.Parent is UiGridLayoutModel;
            UiGridCell? cell = inGrid ? component.GridCell ?? new UiGridCell(1, 1) : null;
            Size2D fit = inGrid ? UiFit.Of(component) : default;
            switch (component)
            {
                case UiComponentModel leaf:
                    nodes.Add(leaf.Snapshot() with { Cell = cell, Fit = fit });
                    break;

                case UiGridLayoutModel grid:
                    grid.PinAxes();
                    nodes.Add(new UiGridFrame(
                        grid,
                        grid.Visible,
                        grid.Rows,
                        grid.Columns,
                        grid.Padding,
                        grid.RowSpacing,
                        grid.ColumnSpacing,
                        grid.BackgroundColor,
                        grid.Scrollable,
                        TakeNodes(grid.Components),
                        [.. grid.ContainedAxes().Select(static axes =>
                            new UiAxesCellFrame(axes, axes.GridCell ?? new UiGridCell(1, 1), UiFit.Of(axes)))],
                        cell,
                        fit,
                        grid.ScrollX,
                        grid.ScrollY));
                    break;

                case UiControlModel control:
                    nodes.Add(new UiControlFrame(
                        control,
                        control.Style,
                        control.Position,
                        control.Visible,
                        control.Enable,
                        control.Text,
                        control.HorizontalAlignment,
                        control.BackgroundColor,
                        control.ForegroundColor,
                        control.FontName,
                        control.FontSizeInPixels(control.PixelPosition().Height),
                        control.FontWeight is "bold" or "demi",
                        control.FontAngle is "italic" or "oblique",
                        control.Tooltip.Joined,
                        control.UserWriteSeq,
                        control.Units,
                        control.Value,
                        control.Min,
                        control.Max,
                        control.SliderStepSmall,
                        control.SliderStepLarge,
                        control.ListboxTop,
                        control.Image,
                        control.GroupManaged && control.Parent is UiButtonGroupModel,
                        control.FocusRequests,
                        control.Tag ?? string.Empty));
                    break;

                case UiPanelModel panel:
                    nodes.Add(new UiPanelFrame(
                        panel,
                        panel.Position,
                        panel.Units,
                        panel.Visible,
                        panel.Enable,
                        panel.HasTitle ? panel.Title.Lines[0] : string.Empty,
                        panel.TitlePosition,
                        panel.BorderType,
                        panel.BorderWidth,
                        panel.BackgroundColor,
                        panel.ForegroundColor,
                        panel.BorderColor,
                        panel.HighlightColor,
                        panel.ShadowColor,
                        panel.FontName,
                        panel.FontSizeInPixels(),
                        panel.FontWeight is "bold" or "demi",
                        panel.FontAngle is "italic" or "oblique",
                        panel.Insets(),
                        TakeNodes(panel.Components),
                        panel is UiProgressIndicatorModel bar ? (bar.Indeterminate ? 1 : bar.Value) : null,
                        (panel as UiProgressIndicatorModel)?.ProgressColor,
                        cell,
                        fit,
                        panel.GridInsets()));
                    break;
            }
        }

        return nodes;
    }

    private static void Flatten(IReadOnlyList<IUiNodeFrame> nodes, List<UiControlFrame> into, List<UiComponentFrame> components)
    {
        foreach (IUiNodeFrame node in nodes)
        {
            switch (node)
            {
                case UiControlFrame control:
                    into.Add(control);
                    break;
                case UiComponentFrame component:
                    components.Add(component);
                    break;
                case UiPanelFrame panel:
                    Flatten(panel.Children, into, components);
                    break;
                case UiGridFrame grid:
                    Flatten(grid.Children, into, components);
                    break;
            }
        }
    }
}
