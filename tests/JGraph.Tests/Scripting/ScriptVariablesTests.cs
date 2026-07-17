using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Data;
using JGraph.Scripting;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The post-run variables snapshot on <see cref="ScriptRunResult.Variables"/> for the hosted C# and
/// Python engines, plus the Python runtime home/search-path facts behind the numpy fix. Python tests
/// self-skip when no CPython runtime is installed, like the rest of the Python suite.
/// </summary>
[Collection("JG facade")]
public class ScriptVariablesTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly List<FigureModel> _figures = new();

    public ScriptVariablesTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context() => new(_output, (_, figure) => _figures.Add(figure));

    [Fact]
    public async Task CSharp_SnapshotsScriptVariables_WithFriendlyTypes()
    {
        var engine = new CSharpScriptEngine();
        ScriptRunResult result = await engine.RunAsync("""
            double x = 5;
            string name = "hi";
            var a = new double[] { 1, 2, 3 };
            """, Context(), CancellationToken.None);

        Assert.True(result.Success, result.Message);

        ScriptVariable x = Assert.Single(result.Variables, v => v.Name == "x");
        Assert.Equal("number", x.Type);
        Assert.Equal(5.0, Assert.IsType<double>(x.RawValue));

        ScriptVariable name = Assert.Single(result.Variables, v => v.Name == "name");
        Assert.Equal("string", name.Type);
        Assert.Equal("hi", name.DisplayValue);

        ScriptVariable a = Assert.Single(result.Variables, v => v.Name == "a");
        Assert.Equal("array", a.Type);
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, Assert.IsType<double[]>(a.RawValue));
    }

    [Fact]
    public async Task CSharp_SnapshotsTables()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jgraph_vars_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "x,y\n1,10\n2,20");
        try
        {
            var engine = new CSharpScriptEngine();
            ScriptRunResult result = await engine.RunAsync(
                $"var t = readcsv(@\"{path}\");", Context(), CancellationToken.None);

            Assert.True(result.Success, result.Message);
            ScriptVariable t = Assert.Single(result.Variables, v => v.Name == "t");
            Assert.Equal("table", t.Type);
            Assert.Equal(2, Assert.IsType<Table>(t.RawValue).RowCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Python_SnapshotsScriptVariables_SkippingModulesAndFunctions()
    {
        var engine = new PythonScriptEngine();
        if (!engine.IsAvailable)
        {
            return;
        }

        ScriptRunResult result = await engine.RunAsync("""
            import math
            x = 5.0
            name = "hi"
            a = [1.0, 2.0, 3.0]
            def f(n):
                return n
            """, Context(), CancellationToken.None);

        Assert.True(result.Success, result.Message);

        ScriptVariable x = Assert.Single(result.Variables, v => v.Name == "x");
        Assert.Equal("number", x.Type);
        Assert.Equal(5.0, Assert.IsType<double>(x.RawValue));

        ScriptVariable a = Assert.Single(result.Variables, v => v.Name == "a");
        Assert.Equal("array", a.Type);
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, Assert.IsType<double[]>(a.RawValue));

        // Modules and functions stay out of the panel.
        Assert.DoesNotContain(result.Variables, v => v.Name is "math" or "f");
    }

    [Fact]
    public void PythonLocator_ReportsHomeAndSearchPaths_WithTheDll()
    {
        PythonRuntimeInfo? info = PythonLocator.Find();
        if (info is null)
        {
            return; // no CPython on this machine — the engine degrades gracefully
        }

        Assert.True(File.Exists(info.Dll), $"probed DLL missing: {info.Dll}");
        if (info.Home is not null)
        {
            Assert.True(Directory.Exists(info.Home), $"probed home missing: {info.Home}");
            Assert.NotEmpty(info.SearchPaths);
        }
    }

    [Fact]
    public async Task Python_EmbeddedRuntime_UsesTheProbedHomeAndSearchPaths()
    {
        var engine = new PythonScriptEngine();
        if (!engine.IsAvailable || engine.RuntimeInfo?.Home is null)
        {
            return;
        }

        ScriptRunResult result = await engine.RunAsync("""
            import sys, os
            print(sys.prefix)
            print(os.pathsep.join(sys.path))
            """, Context(), CancellationToken.None);

        Assert.True(result.Success, result.Message);

        // The embedded interpreter runs against the probed environment's prefix...
        Assert.Contains(engine.RuntimeInfo.Home, _output.NormalText, StringComparison.OrdinalIgnoreCase);

        // ...and every probed search path (site-packages included) is importable.
        foreach (string path in engine.RuntimeInfo.SearchPaths)
        {
            Assert.Contains(path, _output.NormalText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Python_ImportNumpy_WorksWhenInstalled()
    {
        var engine = new PythonScriptEngine();
        if (!engine.IsAvailable)
        {
            return;
        }

        ScriptRunResult result = await engine.RunAsync("""
            import numpy as np
            print("numpy-sum:", np.arange(4).sum())
            """, Context(), CancellationToken.None);

        if (!result.Success && result.Message?.Contains("No module named", StringComparison.Ordinal) == true)
        {
            return; // numpy genuinely not installed for the probed interpreter — nothing to verify
        }

        Assert.True(result.Success, result.Message);
        Assert.Contains("numpy-sum: 6", _output.NormalText);
    }
}
