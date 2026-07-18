using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Serialization;

/// <summary>M21a: the persistent data-tip annotation — anchors, undo, and .graph round-trip.</summary>
public class DataTipAnnotationTests
{
    [Fact]
    public void RoundTrip_KeepsPinLabelAndAppearance()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddLine([1, 2, 3], [4, 5, 6]);
        axes.Annotations.Add(new DataTipAnnotation(new Point2D(2, 5), new Point2D(2.4, 5.6))
        {
            Text = "peak",
            SourceSeries = "volts",
            PointIndex = 1,
            FontSize = 13,
        });

        FigureModel restored = GraphFormat.Deserialize(GraphFormat.Serialize(figure));

        var tip = Assert.IsType<DataTipAnnotation>(Assert.Single(restored.Axes[0].Annotations));
        Assert.Equal(new Point2D(2, 5), tip.PinnedPoint);
        Assert.Equal(new Point2D(2.4, 5.6), tip.LabelPosition);
        Assert.Equal("peak", tip.Text);
        Assert.Equal("volts", tip.SourceSeries);
        Assert.Equal(1, tip.PointIndex);
        Assert.Equal(13, tip.FontSize);
    }

    [Fact]
    public void Anchor_IsTheLabel_NotThePin()
    {
        var tip = new DataTipAnnotation(new Point2D(1, 1), new Point2D(2, 2));

        Point2D anchor = Assert.Single(tip.GetAnchorPoints());
        Assert.Equal(new Point2D(2, 2), anchor);

        // Dragging (SetAnchorPoints) moves the label; the pin never leaves the data point.
        tip.SetAnchorPoints(new[] { new Point2D(3, 4) });
        Assert.Equal(new Point2D(3, 4), tip.LabelPosition);
        Assert.Equal(new Point2D(1, 1), tip.PinnedPoint);
    }

    [Fact]
    public void EffectiveLabel_DefaultsToCoordinates_AndHonorsCustomText()
    {
        var tip = new DataTipAnnotation(new Point2D(1.25, -3), new Point2D(0, 0));
        Assert.Equal("(1.25, -3)", tip.EffectiveLabel);

        tip.Text = "resonance";
        Assert.Equal("resonance", tip.EffectiveLabel);
    }

    [Fact]
    public void AddAnnotationAction_UndoRemoves_RedoReAdds()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        var tip = new DataTipAnnotation(new Point2D(1, 2), new Point2D(1.5, 2.5));
        axes.Annotations.Add(tip);
        var action = new AddAnnotationAction(axes.Annotations, tip, "Add data tip");

        action.Undo();
        Assert.Empty(axes.Annotations);

        action.Redo();
        Assert.Same(tip, Assert.Single(axes.Annotations));
        Assert.Equal("Add data tip", action.Description);
    }

    [Fact]
    public void PinnedXY_InspectorProperties_MoveThePin()
    {
        var tip = new DataTipAnnotation(new Point2D(1, 2), new Point2D(0, 0));
        tip.PinnedX = 7;
        tip.PinnedY = 9;

        Assert.Equal(new Point2D(7, 9), tip.PinnedPoint);
        Assert.Equal("(7, 9)", tip.EffectiveLabel);
    }
}
