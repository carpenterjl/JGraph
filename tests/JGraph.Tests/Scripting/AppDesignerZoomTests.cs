using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W6, apart from a window: R2025b's zoom steps and Fit to View
/// (<c>ZoomConstants</c>, <c>ZoomUtil.calculateZoomLevel</c> and <c>calculateFitToViewZoomLevel</c>),
/// the floating panel's percentage, and on the canvas's gestures the menu's Zoom and display items and
/// the size Resizing Hints shows.
/// </summary>
[Collection("JG facade")]
public class AppDesignerZoomTests
{
    [Fact]
    public void TheZoomStepsAreR2025bs()
    {
        Assert.Equal([0.5, 0.55, 0.6, 0.7, 0.8, 0.9, 1, 1.12, 1.25, 1.37, 1.5, 1.62, 1.75, 1.87, 2, 2.12, 2.5, 2.75, 3, 3.5, 4], DesignZoom.Steps);
        Assert.Equal(1.12, DesignZoom.ZoomIn(1));
        Assert.Equal(0.9, DesignZoom.ZoomOut(1));
        Assert.Equal(4, DesignZoom.ZoomIn(4));
        Assert.Equal(0.5, DesignZoom.ZoomOut(0.5));

        // Taken to its hundredth first: 1.125 is 1.12.
        Assert.Equal(1.25, DesignZoom.ZoomIn(1.125));

        // From below the steps or past them, the first or the last; from between two (Fit to View's), the next one.
        Assert.Equal(0.5, DesignZoom.ZoomIn(0.3));
        Assert.Equal(4, DesignZoom.ZoomOut(5));
        Assert.Equal(0.55, DesignZoom.ZoomIn(0.52));
        Assert.Equal(0.5, DesignZoom.ZoomOut(0.52));

        Assert.False(DesignZoom.CanZoomIn(4));
        Assert.True(DesignZoom.CanZoomIn(3.5));
        Assert.False(DesignZoom.CanZoomOut(0.5));
        Assert.False(DesignZoom.CanReset(1));
        Assert.True(DesignZoom.CanReset(1.12));
        Assert.Equal("112%", DesignZoom.Percent(1.12));
        Assert.Equal("55%", DesignZoom.Percent(0.55));
        Assert.Equal("137%", DesignZoom.Percent(1.37));
    }

    [Fact]
    public void FitToViewTakesTheStepBelowTheNearest()
    {
        var figure = new Size2D(640, 480);

        // 800 by 600 over 700 by 540 is 1.11; the nearest step 1.12, so the one below it, 1.
        Assert.Equal(1, DesignZoom.FitToView(new Size2D(800, 600), figure));

        // 0.556: nearest 0.55, the step below 0.5.
        Assert.Equal(0.5, DesignZoom.FitToView(new Size2D(400, 300), figure));

        // Nearest the first step, or past the steps either way: the fit itself.
        Assert.Equal(357.0 / 700, DesignZoom.FitToView(new Size2D(357, 400), figure), 12);
        Assert.Equal(300.0 / 700, DesignZoom.FitToView(new Size2D(300, 300), figure), 12);
        Assert.Equal(5000.0 / 700, DesignZoom.FitToView(new Size2D(5000, 5000), figure), 12);
    }

    private static DesignDocument App()
    {
        var figure = new DesignComponent("UIFigure", DesignCatalog.R2025b.ByType("matlab.ui.Figure")!);
        figure.Set("Position", NumericValue.Row(100, 100, 640, 480));
        var design = new AppDesign(figure);
        var button = new DesignComponent("Button", DesignCatalog.R2025b.ByType("matlab.ui.control.Button")!);
        button.Set("Position", NumericValue.Row(100, 100, 100, 23));
        figure.Add(button);
        return DesignDocument.FromText(DesignCodeWriter.Splice(AppDesignerDocument.NewAppText(), design)!, null);
    }

    [Fact]
    public void TheCanvasMenuHasZoomAndTheFiguresDisplayItems()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document) { ShowGrid = true, ShowResizingHints = false };
        var asked = new List<DesignZoomRequest>();
        var toggled = new List<(string, bool)>();
        gestures.ZoomRequested += (_, r) => asked.Add(r);
        gestures.DisplayOptionToggled += (_, t) => toggled.Add(t);
        document.Select([]);

        IReadOnlyList<DesignMenuItem> menu = gestures.MenuAt(new Point2D(5, 5));
        Assert.Equal(["Zoom", "Grid", "Snap to Grid", "Alignment Hints", "Resizing Hints", "Apply Grid Layout", "Apply Auto Tab Order", "Visualize Tab Order"],
            menu.Where(static i => !i.IsSeparator).Select(static i => i.Label));
        DesignMenuItem zoom = menu.Single(static i => i.Label == "Zoom");
        Assert.Equal([("Zoom In", true), ("Zoom Out", true), ("Reset Zoom", false), ("Fit to View", true)], zoom.Items!.Select(static i => (i.Label, i.Enabled)));
        zoom.Items![0].Run!();
        zoom.Items![3].Run!();
        Assert.Equal([DesignZoomRequest.In, DesignZoomRequest.Fit], asked);

        // The display items are check items; one turns its setting the other way.
        Assert.Equal([true, true, true, false], menu.Where(static i => i.Checked is not null).Select(static i => i.Checked!.Value));
        menu.Single(static i => i.Label == "Resizing Hints").Run!();
        menu.Single(static i => i.Label == "Grid").Run!();
        Assert.Equal([(DesignSettings.ShowResizingHints, true), (DesignSettings.ShowGrid, false)], toggled);

        // At four times, Zoom In is not offered; Reset is.
        gestures.ZoomLevel = 4;
        zoom = gestures.MenuAt(new Point2D(5, 5)).Single(static i => i.Label == "Zoom");
        Assert.Equal([false, true, true, true], zoom.Items!.Select(static i => i.Enabled));

        // A component's menu has Zoom too, but no display items.
        document.Select([document.Design.Find("Button")!]);
        string[] labels = gestures.MenuAt(new Point2D(150, 370)).Where(static i => !i.IsSeparator).Select(static i => i.Label).ToArray();
        Assert.Equal(["Delete", "Zoom", "Align", "Same Size", "Group", "Reorder"], labels);
    }

    [Fact]
    public void ResizingHintsShowTheSizeWhileResizing()
    {
        using DesignDocument document = App();
        var gestures = new DesignGestures(document);
        DesignComponent button = document.Design.Find("Button")!;
        document.Select([button]);
        Rect2D box = document.BoxOf(button)!.Value.Box;
        var east = new Point2D(box.X + box.Width, box.Y + (box.Height / 2));

        // Off, as R2025b's factory setting is: nothing shows.
        gestures.Press(east, DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(east.X + 40, east.Y), DesignModifiers.None);
        Assert.Null(gestures.SizeHint);
        gestures.Cancel();

        gestures.ShowResizingHints = true;
        gestures.Press(east, DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(east.X + 40, east.Y), DesignModifiers.None);
        DesignSizeHint hint = gestures.SizeHint!.Value;
        Assert.Equal(ResizeEdges.East, hint.Edges);
        Assert.False(hint.Figure);
        Assert.Equal(140, hint.Box.Width);
        gestures.Release();
        Assert.Null(gestures.SizeHint);

        // The figure's edge: its size, from the figure's corner.
        gestures.Press(new Point2D(642, 482), DesignButton.Left, DesignModifiers.None);
        gestures.Drag(new Point2D(662, 502), DesignModifiers.None);
        hint = gestures.SizeHint!.Value;
        Assert.True(hint.Figure);
        Assert.Equal(ResizeEdges.SouthEast, hint.Edges);
        Assert.Equal(new Rect2D(0, 0, 660, 500), hint.Box);
        gestures.Cancel();
    }

    [Fact]
    public void TheGridifierReadsTheBoxesAtTheCanvassZoom()
    {
        using DesignDocument document = App();
        Assert.Equal(1, document.CanvasZoom);
        document.CanvasZoom = 2;
        Assert.False(document.ApplyAutoTabOrder()); // one component takes part: nothing to order
        Assert.NotNull(document.Gridify(document.Design.Figure));
    }
}
