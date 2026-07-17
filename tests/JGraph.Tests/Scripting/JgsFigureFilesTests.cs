using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Export;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>A real <see cref="IScriptFigureFiles"/> over GraphFormat + FigureExporter, as the app wires it.</summary>
internal sealed class TestFigureFiles : IScriptFigureFiles
{
    public void Save(FigureModel figure, string path) => GraphFormat.Save(figure, path);

    public FigureModel Load(string path) => GraphFormat.Load(path);

    public void Export(FigureModel figure, string path) => FigureExporter.Export(figure, path, new ExportOptions());
}

/// <summary>M19: savefigure / loadfigure / exportfigure builtins end to end on disk.</summary>
[Collection("JG facade")]
public class JgsFigureFilesTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<(int Number, FigureModel Figure)> _shown = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public JgsFigureFilesTests()
    {
        JG.Reset();
        _directory = Path.Combine(Path.GetTempPath(), "jgraph-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private Task<ScriptRunResult> Run(string code, IScriptFigureFiles? files = null) =>
        _engine.RunAsync(code, new ScriptContext(
            _output, (n, f) => _shown.Add((n, f)), _directory, resolvePath: null,
            files ?? new TestFigureFiles()), default);

    [Fact]
    public async Task SaveThenLoad_RoundTripsTheFigure_AndLoadBecomesCurrent()
    {
        ScriptRunResult result = await Run("""
            plot([1, 2, 3], [4, 5, 6])
            title("saved from script")
            savefigure("run.graph")

            let restored = loadfigure("run.graph")
            print("handle:", restored)
            hold(true)
            plot([1, 2], [1, 2])
            show()
            """);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(_directory, "run.graph")));
        Assert.Contains("handle: 2", _output.NormalText); // Figure 1 was the implicit one.

        // show() displayed the loaded figure — with the round-tripped series plus the new one.
        (int number, FigureModel figure) = Assert.Single(_shown);
        Assert.Equal(2, number);
        Assert.Equal(2, figure.Axes[0].Plots.Count);
        Assert.Equal("saved from script", figure.Axes[0].Title);
    }

    [Fact]
    public async Task Savefigure_WithHandle_TargetsThatFigure()
    {
        ScriptRunResult result = await Run("""
            let a = figure()
            plot([1], [1])
            let b = figure()
            plot([1, 2, 3], [1, 2, 3])
            savefigure("first.graph", a)
            """);

        Assert.True(result.Success, result.Message);
        FigureModel saved = GraphFormat.Load(Path.Combine(_directory, "first.graph"));
        Assert.Single(saved.Axes[0].Plots);
    }

    [Fact]
    public async Task Exportfigure_WritesDecodablePngAndSvg()
    {
        ScriptRunResult result = await Run("""
            plot([1, 2, 3], [2, 4, 8])
            exportfigure("run.png")
            exportfigure("run.svg")
            """);

        Assert.True(result.Success, result.Message);

        byte[] png = File.ReadAllBytes(Path.Combine(_directory, "run.png"));
        Assert.True(png.Length > 100);
        Assert.Equal(0x89, png[0]); // PNG signature.
        Assert.Equal((byte)'P', png[1]);

        string svg = File.ReadAllText(Path.Combine(_directory, "run.svg"));
        Assert.Contains("<svg", svg);
    }

    [Fact]
    public async Task Savefigure_NewRelativePath_LandsInTheWorkingDirectory_NotTheProcessDirectory()
    {
        // Regression (found live): the workspace's READ resolver returns the bare name for a file
        // that doesn't exist yet, which would drop a new save into the process directory. Writes
        // must resolve against the working directory instead.
        Func<string, string> workspaceStyleReadResolver = path =>
        {
            string probe = Path.Combine(_directory, path);
            return File.Exists(probe) ? probe : path; // Bare name for not-yet-existing files.
        };

        ScriptRunResult result = await _engine.RunAsync(
            "plot([1], [1])\nsavefigure(\"fresh.graph\")",
            new ScriptContext(_output, (_, _) => { }, _directory, workspaceStyleReadResolver,
                new TestFigureFiles()), default);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(Path.Combine(_directory, "fresh.graph")));
        Assert.False(File.Exists(Path.Combine(Environment.CurrentDirectory, "fresh.graph")));
    }

    [Fact]
    public async Task Savefigure_UnknownHandle_IsRuntimeError()
    {
        ScriptRunResult result = await Run("savefigure(\"x.graph\", 9)");

        Assert.False(result.Success);
        Assert.Contains("no figure 9", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task Loadfigure_MissingFile_IsScriptDiagnostic_NotACrash()
    {
        ScriptRunResult result = await Run("loadfigure(\"nope.graph\")");

        Assert.False(result.Success);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public async Task WithoutHostFigureFiles_TheBuiltinsFailClearly()
    {
        ScriptRunResult result = await _engine.RunAsync(
            "savefigure(\"x.graph\")",
            new ScriptContext(_output, (_, _) => { }, _directory), default);

        Assert.False(result.Success);
        Assert.Contains("not supported by this host", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task FigureWorkflow_EndToEnd_TwoWindowsSaveExportReload()
    {
        // The M19 E2E: data -> two figures -> show both -> save/export -> reload and annotate.
        File.WriteAllText(Path.Combine(_directory, "data.csv"), "X,Y\n1,10\n2,20\n3,15\n4,30\n");

        ScriptRunResult result = await Run("""
            let t = readcsv("data.csv")
            let y = column(t, "Y")

            figure(1)
            histogram(y, 4)
            title("distribution")

            figure(2)
            subplot(2, 1, 1)
            plot(column(t, "X"), y)
            subplot(2, 1, 2)
            bar(column(t, "X"), y)

            show(1)
            show(2)
            savefigure("run.graph", 1)
            exportfigure("run.png", 2)

            let again = loadfigure("run.graph")
            show(again)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, _shown.Count);
        Assert.Equal(1, _shown[0].Number);
        Assert.Equal(2, _shown[1].Number);
        Assert.Equal(3, _shown[2].Number);           // The reloaded copy got its own handle.
        Assert.Equal(2, _shown[1].Figure.Axes.Count); // The two subplots.
        Assert.Equal("distribution", _shown[2].Figure.Axes[0].Title);
        Assert.True(File.Exists(Path.Combine(_directory, "run.png")));
    }
}
