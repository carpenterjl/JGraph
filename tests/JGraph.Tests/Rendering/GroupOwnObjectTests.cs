using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Export;
using JGraph.Objects;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>
/// Open item 296 (ADR 0360): a group is its axes' own object. Its <c>Visible</c> is read when its
/// members are drawn and fitted, a transform's <c>Matrix</c> moves their drawing and the limits and
/// never their data, and the <c>.graph</c> document keeps the group. Checked through the offscreen
/// renderer only, never a window.
/// </summary>
public class GroupOwnObjectTests
{
    private static readonly Size2D Size = new(240, 200);
    private static readonly Color Red = Color.FromRgb(255, 0, 0);

    /// <summary>A figure with one fixed-limits axes and one thick red vertical line at x = 2.</summary>
    private static (FigureModel Figure, AxesModel Axes, LinePlot Line) Scene()
    {
        var figure = new FigureModel { Size = Size };
        AxesModel axes = figure.AddAxes();
        LinePlot line = axes.AddLine([2, 2], [0, 10]);
        line.Color = Red;
        line.LineWidth = 8;
        foreach (AxisModel ruler in (AxisModel[])[axes.PrimaryXAxis, axes.PrimaryYAxis])
        {
            ruler.AutoScale = false;
            ruler.Range = new DataRange(0, 10);
        }

        return (figure, axes, line);
    }

    /// <summary>How many strongly red pixels there are left of the middle and right of it.</summary>
    private static (int Left, int Right) RedPixels(FigureModel figure)
    {
        (int width, int height, byte[] rgba) = FigureExporter.RenderRgba(figure, new ExportOptions { Size = Size });
        int left = 0, right = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int at = ((y * width) + x) * 4;
                if (rgba[at] > 200 && rgba[at + 1] < 60 && rgba[at + 2] < 60)
                {
                    if (x < width / 2)
                    {
                        left++;
                    }
                    else
                    {
                        right++;
                    }
                }
            }
        }

        return (left, right);
    }

    private static GraphicsGroupModel Group(AxesModel axes, bool transforms, GraphicsGroupModel? outer = null)
    {
        var group = new GraphicsGroupModel(transforms, axes, outer);
        axes.AddGroup(group);
        return group;
    }

    [Fact]
    public void AHiddenGroupHidesItsMemberWhoseOwnVisibleStaysOn()
    {
        (FigureModel figure, AxesModel axes, LinePlot line) = Scene();
        GraphicsGroupModel group = Group(axes, transforms: false);
        group.Admit(line);
        Assert.True(RedPixels(figure).Left > 0);

        group.Visible = false;
        Assert.True(line.Visible);
        Assert.Equal((0, 0), RedPixels(figure));
    }

    [Fact]
    public void AnOuterGroupHiddenHidesTheInnerGroupsMembers()
    {
        (FigureModel figure, AxesModel axes, LinePlot line) = Scene();
        GraphicsGroupModel outer = Group(axes, transforms: false);
        GraphicsGroupModel inner = Group(axes, transforms: false, outer);
        inner.Admit(line);
        outer.Visible = false;
        Assert.True(inner.Visible);
        Assert.Equal((0, 0), RedPixels(figure));
    }

    [Fact]
    public void ATransformDrawsItsMemberWhereTheMatrixPutsItAndLeavesTheDataAlone()
    {
        (FigureModel figure, AxesModel axes, LinePlot line) = Scene();
        GraphicsGroupModel transform = Group(axes, transforms: true);
        transform.Admit(line);
        (int left, int right) = RedPixels(figure);
        Assert.True(left > 0 && right == 0, $"before: {left} {right}");

        double[,] shift = GraphicsGroupModel.Identity();
        shift[0, 3] = 6; // x = 2 drawn at x = 8
        transform.SetMatrix(shift);
        (left, right) = RedPixels(figure);
        Assert.True(left == 0 && right > 0, $"after: {left} {right}");
        Assert.Equal(2, line.Data.GetX(0));
    }

    [Fact]
    public void TheLimitsLeaveAHiddenGroupOutAndFitATransformWhereItMoves()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        LinePlot line = axes.AddLine([0, 1], [0, 0]);
        GraphicsGroupModel transform = Group(axes, transforms: true);
        transform.Admit(line);
        double[,] up = GraphicsGroupModel.Identity();
        up[1, 3] = 5;
        transform.SetMatrix(up);
        axes.RecomputeDataBounds();
        Assert.Equal(new DataRange(5, 5), axes.PrimaryYAxis.DataBounds);
        Assert.Equal(new DataRange(0, 1), axes.PrimaryXAxis.DataBounds);

        transform.Visible = false;
        axes.RecomputeDataBounds();
        Assert.True(axes.PrimaryYAxis.DataBounds.IsEmpty);
    }

    [Fact]
    public void TheDocumentKeepsTheGroupsTheirMembersMatrixVisibilityAndPlace()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        LinePlot first = axes.AddLine([0, 1], [0, 1]);
        first.Tag = "first";
        GraphicsGroupModel group = Group(axes, transforms: false);
        group.Tag = "grp";
        group.Visible = false;
        group.DisplayName = "named";
        LinePlot member = axes.AddLine([0, 1], [1, 0]);
        group.Admit(member);
        GraphicsGroupModel inner = Group(axes, transforms: true, group);
        inner.Tag = "inner";
        double[,] shift = GraphicsGroupModel.Identity();
        shift[1, 3] = 5;
        inner.SetMatrix(shift);
        LinePlot deep = axes.AddLine([0, 1], [0, 0]);
        inner.Admit(deep);
        LinePlot last = axes.AddLine([0, 1], [2, 2]);
        last.Tag = "last";

        FigureModel back = GraphFormat.Deserialize(GraphFormat.Serialize(figure));
        AxesModel axesBack = back.Axes[0];
        Assert.Equal(2, axesBack.Groups.Count);
        GraphicsGroupModel groupBack = axesBack.Groups[0];
        GraphicsGroupModel innerBack = axesBack.Groups[1];
        Assert.Equal("grp", groupBack.Tag);
        Assert.False(groupBack.Visible);
        Assert.Equal("named", groupBack.DisplayName);
        Assert.False(groupBack.Transforms);
        Assert.Same(groupBack, innerBack.Outer);
        Assert.True(innerBack.Transforms);
        Assert.Equal(5, innerBack.Matrix[1, 3]);
        Assert.Equal(2, groupBack.Members.Count);
        Assert.Same(axesBack.Plots[1], groupBack.Members[0]);
        Assert.Same(innerBack, groupBack.Members[1]);
        Assert.Same(axesBack.Plots[2], Assert.Single(innerBack.Members));
        Assert.Equal(0, ((LinePlot)innerBack.Members[0]).Data.GetY(0)); // its own data, not the moved

        // The group stands where it was among the drawn objects: after "first", before "last".
        Assert.True(groupBack.Seat > axesBack.Plots[0].CreationOrder);
        Assert.True(groupBack.Seat <= axesBack.Plots[3].CreationOrder);
    }

    [Fact]
    public void ADocumentWrittenBeforeGroupsReadsBackWithNone()
    {
        var figure = new FigureModel();
        figure.AddAxes().AddLine([0, 1], [0, 1]);
        string json = GraphFormat.Serialize(figure);
        Assert.DoesNotContain("\"groups\"", json, StringComparison.OrdinalIgnoreCase);

        FigureModel back = GraphFormat.Deserialize(json);
        Assert.Empty(back.Axes[0].Groups);
        Assert.Single(back.Axes[0].Plots);
    }
}
