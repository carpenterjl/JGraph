using JGraph.Api;
using JGraph.Data;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Values that have a table shape but no simpler host representation — matrices, cell arrays and
/// structs — reaching the Data Viewer as formatted grids, so MATLAB values are actually inspectable.
/// </summary>
[Collection("JG facade")]
public class ScriptValueGridTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public ScriptValueGridTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private async Task<IReadOnlyList<ScriptVariable>> Run(IScriptEngine engine, string code)
    {
        ScriptRunResult result = await engine.RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), CancellationToken.None);
        Assert.True(result.Success, result.Message);
        return result.Variables;
    }

    private static ScriptValueGrid GridOf(IReadOnlyList<ScriptVariable> variables, string name) =>
        Assert.IsType<ScriptValueGrid>(Assert.Single(variables, v => v.Name == name).RawValue);

    [Fact]
    public async Task AMatrix_BecomesARowByColumnGrid()
    {
        ScriptValueGrid grid = GridOf(await Run(new MatlabScriptEngine(), "m = [1 2 3; 4 5 6];"), "m");

        Assert.Equal("matrix", grid.Kind);
        Assert.Equal(3, grid.ColumnNames.Count);
        Assert.Equal(2, grid.Rows.Count);
        Assert.Equal("6", grid.Rows[1][2]);
    }

    [Fact]
    public async Task ACellArray_KeepsItsMixedContents()
    {
        ScriptValueGrid grid = GridOf(await Run(new MatlabScriptEngine(), "c = {1, 'two', 3};"), "c");

        Assert.Equal("cell", grid.Kind);
        string[] row = Assert.Single(grid.Rows);
        Assert.Equal(3, row.Length);
        Assert.Contains("two", row[1]);
    }

    [Fact]
    public async Task AStruct_ListsOneFieldPerRow_WithItsType()
    {
        ScriptValueGrid grid = GridOf(
            await Run(new MatlabScriptEngine(), "s.name = 'probe'; s.gain = 2.5;"), "s");

        Assert.Equal("struct", grid.Kind);
        Assert.Equal(new[] { "Field", "Type", "Value" }, grid.ColumnNames);
        Assert.Equal(2, grid.Rows.Count);
        Assert.Equal("gain", grid.Rows[0][0]); // fields sort by name
        Assert.Equal("number", grid.Rows[0][1]);
    }

    [Fact]
    public async Task APlainNumericVector_StaysAnArray_NotAGrid()
    {
        // The array path is cheaper and the viewer already handles it; a regression here would send
        // every vector through string formatting.
        IReadOnlyList<ScriptVariable> variables = await Run(new MatlabScriptEngine(), "v = [1 2 3];");

        Assert.IsType<double[]>(Assert.Single(variables, v => v.Name == "v").RawValue);
    }

    [Fact]
    public async Task ARaggedMatrix_PadsShortRows_RatherThanFailing()
    {
        ScriptValueGrid grid = GridOf(
            await Run(new JgsScriptEngine(), "let m = [[1, 2, 3], [4]]"), "m");

        Assert.Equal(3, grid.ColumnNames.Count);
        Assert.Equal("4", grid.Rows[1][0]);
        Assert.Equal(string.Empty, grid.Rows[1][2]);
    }

    [Fact]
    public void TheGridAdapter_ViewsAProjectedGrid()
    {
        var adapter = TableGridAdapter.ForGrid(
            "matrix 2×2", new[] { "0", "1" }, new[] { new[] { "1", "2" }, new[] { "3", "4" } });

        Assert.Equal(2, adapter.RowCount);
        Assert.Equal("4", adapter.GetText(1, 1));
        Assert.Single(adapter.GetPage(0, out int firstRow), static row => row[0] == "3");
        Assert.Equal(0, firstRow);
    }

    [Fact]
    public void TheGridAdapter_PadsAShortRow_RatherThanThrowing()
    {
        var adapter = TableGridAdapter.ForGrid("cell 1×3", new[] { "0", "1", "2" }, new[] { new[] { "a" } });

        Assert.Equal(string.Empty, adapter.GetText(0, 2));
    }
}
