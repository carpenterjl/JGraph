using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;
using JGraph.Interaction.Editing;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Editing;

public class FigureElementCommandsTests
{
    private static AxesModel FixedAxes()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(0, 10);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(0, 20);
        return axes;
    }

    [Fact]
    public void ShowLegend_RevealsItUndoably()
    {
        AxesModel axes = FixedAxes();
        var undo = new UndoStack();

        FigureElementCommands.ShowLegend(axes, undo);

        Assert.True(axes.Legend.Visible);
        undo.Undo();
        Assert.False(axes.Legend.Visible);
    }

    [Fact]
    public void ShowLegend_WhenAlreadyVisible_PushesNothing()
    {
        AxesModel axes = FixedAxes();
        axes.Legend.Visible = true;
        var undo = new UndoStack();

        FigureElementCommands.ShowLegend(axes, undo);

        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void ShowColorbar_RevealsItUndoably()
    {
        AxesModel axes = FixedAxes();
        var undo = new UndoStack();

        FigureElementCommands.ShowColorbar(axes, undo);

        Assert.True(axes.Colorbar.Visible);
        undo.Undo();
        Assert.False(axes.Colorbar.Visible);
    }

    [Fact]
    public void SetAxesTitle_SeedsAnEmptyTitleThenLeavesItAlone()
    {
        AxesModel axes = FixedAxes();
        var undo = new UndoStack();

        FigureElementCommands.SetAxesTitle(axes, "Signal", undo);
        Assert.Equal("Signal", axes.Title);

        FigureElementCommands.SetAxesTitle(axes, "Other", undo);
        Assert.Equal("Signal", axes.Title); // not overwritten

        undo.Undo();
        Assert.Equal(string.Empty, axes.Title);
    }

    [Fact]
    public void AddSecondaryAxes_AppendToTheCollections()
    {
        AxesModel axes = FixedAxes();

        AxisModel x = FigureElementCommands.AddSecondaryXAxis(axes);
        AxisModel y = FigureElementCommands.AddSecondaryYAxis(axes);

        Assert.Equal(2, axes.XAxes.Count);
        Assert.Equal(2, axes.YAxes.Count);
        Assert.Equal(AxisPosition.Top, x.Position);
        Assert.Equal(AxisPosition.Right, y.Position);
    }

    [Fact]
    public void ApplySubplotGrid_ReTilesExistingAxesAndAddsOneMore()
    {
        var figure = new FigureModel();
        figure.AddAxes();

        AxesModel? added = FigureElementCommands.ApplySubplotGrid(figure, 2, 2);

        Assert.NotNull(added);
        Assert.Equal(2, figure.Axes.Count);

        // The pre-existing axes now occupies the first cell rather than the whole figure.
        Rect2D firstCell = FigureModel.SubplotBounds(2, 2, 1, 1);
        Assert.Equal(firstCell.X, figure.Axes[0].NormalizedBounds.X, 9);
        Assert.Equal(firstCell.Width, figure.Axes[0].NormalizedBounds.Width, 9);
    }

    [Fact]
    public void ApplySubplotGrid_ReturnsNullWhenTheGridIsAlreadyFull()
    {
        var figure = new FigureModel();
        figure.AddAxes();
        figure.AddAxes();

        AxesModel? added = FigureElementCommands.ApplySubplotGrid(figure, 1, 2);

        Assert.Null(added);
        Assert.Equal(2, figure.Axes.Count);
    }

    [Fact]
    public void RemoveAxes_TakesItOutOfTheFigure()
    {
        var figure = new FigureModel();
        AxesModel a = figure.AddAxes();
        AxesModel b = figure.AddAxes();

        FigureElementCommands.RemoveAxes(figure, a);

        Assert.Same(b, Assert.Single(figure.Axes));
    }

    [Fact]
    public void AddText_AnchorsAtTheViewCentreUndoably()
    {
        AxesModel axes = FixedAxes();
        var undo = new UndoStack();

        var text = FigureElementCommands.AddText(axes, undo);

        Assert.Same(text, Assert.Single(axes.Annotations));
        Assert.Equal(5, text.Position.X, 9);
        Assert.Equal(10, text.Position.Y, 9);

        undo.Undo();
        Assert.Empty(axes.Annotations);
    }

    [Fact]
    public void AddArrow_AndAddShape_AreUndoable()
    {
        AxesModel axes = FixedAxes();
        var undo = new UndoStack();

        FigureElementCommands.AddArrow(axes, undo);
        FigureElementCommands.AddShape(axes, undo);
        Assert.Equal(2, axes.Annotations.Count);

        undo.Undo();
        undo.Undo();
        Assert.Empty(axes.Annotations);
    }

    [Fact]
    public void ExcludeAndIncludeLegendEntry_FlipVisibilityUndoably()
    {
        AxesModel axes = FixedAxes();
        axes.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 }).DisplayName = "A";
        axes.Legend.SyncEntries(axes.Plots);
        LegendEntryModel entry = axes.Legend.Entries[0];
        var undo = new UndoStack();

        FigureElementCommands.ExcludeLegendEntry(entry, undo);
        Assert.False(entry.Visible);

        undo.Undo();
        Assert.True(entry.Visible);
    }

    [Fact]
    public void MoveLegendEntry_ReordersAndClampsUndoably()
    {
        AxesModel axes = FixedAxes();
        foreach (string name in new[] { "A", "B", "C" })
        {
            axes.AddLine(new double[] { 0, 1 }, new double[] { 0, 1 }).DisplayName = name;
        }

        axes.Legend.SyncEntries(axes.Plots);
        LegendEntryModel c = axes.Legend.Entries[2];
        var undo = new UndoStack();

        FigureElementCommands.MoveLegendEntry(axes.Legend, c, -1, undo);
        Assert.Equal(new[] { "A", "C", "B" }, axes.Legend.Entries.Select(e => e.Plot?.DisplayName));

        undo.Undo();
        Assert.Equal(new[] { "A", "B", "C" }, axes.Legend.Entries.Select(e => e.Plot?.DisplayName));

        // Clamped: the top row cannot move up.
        FigureElementCommands.MoveLegendEntry(axes.Legend, axes.Legend.Entries[0], -1, undo);
        Assert.Equal(new[] { "A", "B", "C" }, axes.Legend.Entries.Select(e => e.Plot?.DisplayName));
    }
}
