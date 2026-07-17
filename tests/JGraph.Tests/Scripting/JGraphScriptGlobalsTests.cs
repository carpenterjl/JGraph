using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Data;
using JGraph.Scripting;
using Xunit;

namespace JGraph.Tests.Scripting;

[Collection("JG facade")]
public class JGraphScriptGlobalsTests : IDisposable
{
    public JGraphScriptGlobalsTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    [Fact]
    public void Show_DisplaysCurrentFigure_AndCountsIt()
    {
        var figures = new List<FigureModel>();
        var globals = new JGraphScriptGlobals(new ScriptContext(new RecordingScriptOutput(), (_, figure) => figures.Add(figure)));

        JG.Figure();
        JG.Plot(new double[] { 1, 2 }, new double[] { 3, 4 });
        globals.show();

        FigureModel shown = Assert.Single(figures);
        Assert.Same(JG.CurrentFigure, shown);
        Assert.Equal(1, globals.FiguresShown);
    }

    [Fact]
    public void Show_WithExplicitFigure_DisplaysThatFigure()
    {
        var figures = new List<FigureModel>();
        var globals = new JGraphScriptGlobals(new ScriptContext(new RecordingScriptOutput(), (_, figure) => figures.Add(figure)));
        var figure = new FigureModel();

        globals.show(figure);

        Assert.Same(figure, Assert.Single(figures));
    }

    [Fact]
    public void Print_WritesALine()
    {
        var output = new RecordingScriptOutput();
        var globals = new JGraphScriptGlobals(new ScriptContext(output, (_, _) => { }));

        globals.print("hi");

        Assert.Contains("hi", output.NormalText);
    }

    [Fact]
    public void Readcsv_ResolvesRelativePathAgainstWorkingDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"jgraph_scr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "data.csv"), "x,y\n1,10\n2,20\n3,30");
            var globals = new JGraphScriptGlobals(new ScriptContext(new RecordingScriptOutput(), (_, _) => { }, dir));

            Table table = globals.readcsv("data.csv");

            Assert.Equal(3, table.RowCount);
            Assert.Equal(new[] { "x", "y" }, table.ColumnNames);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
