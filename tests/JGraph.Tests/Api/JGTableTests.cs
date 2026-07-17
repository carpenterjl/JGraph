using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Data;
using JGraph.Objects;
using Xunit;

namespace JGraph.Tests.Api;

[Collection("JG facade")]
public class JGTableTests : IDisposable
{
    public JGTableTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    [Fact]
    public void ReadCsv_FromTempFile_ReadsTable()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jgraph_jg_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path, "x,y\n1,10\n2,20\n3,30");
            Table table = JG.ReadCsv(path);
            Assert.Equal(3, table.RowCount);
            Assert.Equal(new[] { "x", "y" }, table.ColumnNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Plot_Table_BuildsStyledLineOnCurrentAxes()
    {
        Table table = Table.Parse("x,y\n1,10\n2,20");
        JG.Figure();

        LinePlot plot = JG.Plot(table, "x", "y", "r--");

        Assert.Single(JG.Gca().Plots);
        Assert.Same(plot, JG.Gca().Plots[0]);
        Assert.Equal("y", plot.DisplayName);
        Assert.Equal(DashStyle.Dash, plot.DashStyle);
    }

    [Fact]
    public void Histogram_Table_AddsHistogram()
    {
        Table table = Table.Parse("v\n1\n2\n3\n4\n5");
        JG.Figure();
        HistogramPlot plot = JG.Histogram(table, "v", 4);
        Assert.Same(plot, JG.Gca().Plots[0]);
        Assert.Equal(4, plot.BinCount);
    }
}
