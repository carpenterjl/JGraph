using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M34: the console commands — <c>clc</c> clears the output sink's display, <c>dir</c> lists a folder
/// as a cell of names, and <c>path</c> reports the folder bare names resolve against.
/// </summary>
[Collection("JG facade")]
public class ConsoleBuiltinTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-dir-" + Guid.NewGuid().ToString("N"));

    public ConsoleBuiltinTests()
    {
        JG.Reset();
        Directory.CreateDirectory(Path.Combine(_folder, "sub"));
        File.WriteAllText(Path.Combine(_folder, "a.m"), "% a");
        File.WriteAllText(Path.Combine(_folder, "b.m"), "% b");
        File.WriteAllText(Path.Combine(_folder, "c.txt"), "c");
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }, _folder), default);

    private Task<ScriptRunResult> RunJgs(string code) =>
        new JgsScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }, _folder), default);

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    [Fact]
    public async Task Clc_ClearsTheOutputSink()
    {
        ScriptRunResult result = await RunMatlab("disp(1)\nclc\n");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(1, _output.ClearCount);
    }

    [Fact]
    public async Task Clc_BareName_AutoCallsInBothDialects()
    {
        Assert.True((await RunMatlab("clc\n")).Success);
        Assert.True((await RunJgs("clc\n")).Success);

        Assert.Equal(2, _output.ClearCount);
    }

    [Fact]
    public async Task Dir_ListsEverything_SortedWithFolderMarker()
    {
        ScriptRunResult result = await RunMatlab("""
            d = dir();
            n = numel(d);
            first = d{1};
            last = d{4};
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(4, Number(result, "n"));
        Assert.Equal("a.m", Assert.Single(result.Variables, v => v.Name == "first").RawValue);
        Assert.Equal("sub" + Path.DirectorySeparatorChar,
            Assert.Single(result.Variables, v => v.Name == "last").RawValue);
    }

    [Fact]
    public async Task Dir_Pattern_FiltersTheListing()
    {
        ScriptRunResult result = await RunMatlab("""
            d = dir('*.m');
            n = numel(d);
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(2, Number(result, "n"));
    }

    [Fact]
    public async Task Dir_SubfolderName_ListsItsContents()
    {
        File.WriteAllText(Path.Combine(_folder, "sub", "inner.jgs"), "// inner");

        ScriptRunResult result = await RunMatlab("""
            d = dir('sub');
            n = numel(d);
            only = d{1};
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(1, Number(result, "n"));
        Assert.Equal("inner.jgs", Assert.Single(result.Variables, v => v.Name == "only").RawValue);
    }

    [Fact]
    public async Task Dir_MissingFolder_YieldsAnEmptyCell()
    {
        ScriptRunResult result = await RunMatlab("""
            d = dir('nosuchfolder/*.m');
            n = numel(d);
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(0, Number(result, "n"));
    }

    [Fact]
    public async Task Path_ReportsTheWorkingDirectory()
    {
        ScriptRunResult result = await RunMatlab("p = path();");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(_folder, Assert.Single(result.Variables, v => v.Name == "p").RawValue);
    }

    /// <summary>
    /// M62 turned this one around. The old test pinned the refusal — there was no search path, and
    /// saying so beat "not recognized" — and it was right for as long as that was true. Now the
    /// folder joins the path and the refusal is reserved for a folder that is not there, which is the
    /// only thing addpath has left to complain about.
    /// </summary>
    [Fact]
    public async Task Addpath_AddsAFolder_AndRefusesOneThatIsNotThere()
    {
        ScriptRunResult result = await RunMatlab("""
            addpath(pwd());
            listed = double(contains(path(), pwd()));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(1.0, Number(result, "listed"));

        ScriptRunResult missing = await RunMatlab("addpath('no-such-folder-anywhere')");

        Assert.False(missing.Success);
        Assert.Contains("addpath", missing.Message, StringComparison.Ordinal);
        Assert.Contains("no-such-folder-anywhere", missing.Message, StringComparison.Ordinal);
    }
}
