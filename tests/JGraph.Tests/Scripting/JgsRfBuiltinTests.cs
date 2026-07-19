using JGraph.Api;
using JGraph.Core.Data;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Engineering;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M23: the RF builtins from JGS — Touchstone reading, S-parameter access and conversions,
/// reflection helpers (db, vswr, gammain), the rfplot/smithplot verbs, and the microstrip/stripline
/// calculators.
/// </summary>
[Collection("JG facade")]
public class JgsRfBuiltinTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory;

    public JgsRfBuiltinTests()
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

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), _directory), default);

    private async Task<string> Eval(string expression)
    {
        ScriptRunResult result = await Run($"print({expression})");
        Assert.True(result.Success, result.Message);
        return _output.NormalText.Trim();
    }

    /// <summary>Writes a 2-port .s2p fixture (two points, S11 = S22 = 0.5/0.4, feed-through zero) and returns its filename.</summary>
    private string WriteS2p()
    {
        string path = Path.Combine(_directory, "dut.s2p");
        File.WriteAllText(path,
            "# GHz S RI R 50\n" +
            "1.0  0.5 0.0  0.0 0.0  0.0 0.0  0.5 0.0\n" +
            "2.0  0.4 0.0  0.0 0.0  0.0 0.0  0.4 0.0\n");
        return "dut.s2p";
    }

    [Fact]
    public async Task Sparameters_ReadsAsATableWithTheExpectedColumns()
    {
        string file = WriteS2p();
        ScriptRunResult result = await Run($$"""
            let net = sparameters('{{file}}');
            print(rowcount(net))
            print(colnames(net))
            print(rffreq(net))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Contains("2", _output.NormalText); // two frequency points
        Assert.Contains("s11_re", _output.NormalText);
        Assert.Contains("z0", _output.NormalText);
        Assert.Contains("1000000000", _output.NormalText); // 1 GHz in Hz
    }

    [Fact]
    public async Task Rfparam_ReturnsTheParameterAcrossFrequency()
    {
        string file = WriteS2p();
        ScriptRunResult result = await Run($$"""
            let net = sparameters('{{file}}');
            print(real(rfparam(net, 1, 1)))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("[0.5, 0.4]", _output.NormalText.Trim());
    }

    [Fact]
    public async Task Db_OfHalf_IsMinusSixDecibels() =>
        Assert.StartsWith("-6.02", await Eval("db(0.5)"));

    [Fact]
    public async Task Vswr_OfHalf_IsThree() =>
        Assert.Equal("3", await Eval("vswr(0.5)"));

    [Fact]
    public async Task S2z_ConvertsAndRfparamStillReads()
    {
        string file = WriteS2p();
        // S11 = 0.5 in a 50 Ω system → Z11 = 50·(1+0.5)/(1−0.5) = 150 Ω (feed-through is zero).
        ScriptRunResult result = await Run($$"""
            let z = s2z(sparameters('{{file}}'));
            print(real(rfparam(z, 1, 1)))
            """);

        Assert.True(result.Success, result.Message);
        Assert.StartsWith("[150", _output.NormalText.Trim());
    }

    [Fact]
    public async Task Rfplot_ProducesAFigureWithLineTraces()
    {
        string file = WriteS2p();
        ScriptRunResult result = await Run($$"""
            rfplot(sparameters('{{file}}'), 1, 1);
            show()
            """);

        Assert.True(result.Success, result.Message);
        AxesModel axes = Assert.Single(_figures)!.Axes[^1];
        Assert.Contains(axes.Plots, p => p is LinePlot);
        Assert.Equal("Frequency (Hz)", axes.PrimaryXAxis.Label);
    }

    [Fact]
    public async Task Smithplot_DrawsAGridAndAReflectionLocusInsideTheUnitDisk()
    {
        string file = WriteS2p();
        ScriptRunResult result = await Run($$"""
            smithplot(sparameters('{{file}}'), 1, 1);
            show()
            """);

        Assert.True(result.Success, result.Message);
        AxesModel axes = Assert.Single(_figures)!.Axes[^1];
        Assert.True(axes.EqualAspect);
        Assert.Contains(axes.Plots, p => p is SmithGrid);

        var locus = Assert.IsType<LinePlot>(axes.Plots.First(p => p is LinePlot));
        for (int i = 0; i < locus.Data.Count; i++)
        {
            double magnitude = System.Math.Sqrt(
                (locus.Data.GetX(i) * locus.Data.GetX(i)) + (locus.Data.GetY(i) * locus.Data.GetY(i)));
            Assert.True(magnitude <= 1.0 + 1e-9, $"|Γ| = {magnitude} escaped the unit disk");
        }
    }

    [Fact]
    public async Task Microstrip_DestructuresIntoImpedanceAndEffectivePermittivity()
    {
        ScriptRunResult result = await Run("""
            let [z0, eeff] = microstrip(3.081, 1, 2.2);
            print(abs(z0 - 50) < 1)
            print(eeff > 1)
            print(eeff < 2.2)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("true\ntrue\ntrue", _output.NormalText.Trim().ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Microstripw_SynthesizesAWidthNearThePozarAnchor() =>
        Assert.Equal("true", await Eval("abs(microstripw(50, 1, 2.2) - 3.081) < 0.1"));

    [Fact]
    public async Task ShippedExample_RunsEndToEnd_AndProducesTwoFigures()
    {
        // Runs examples/matlab-rf-match.jgs against examples/sample.s2p exactly as shipped, so the
        // documented walkthrough can never silently rot.
        string examples = LocateExamplesDirectory();
        string script = await File.ReadAllTextAsync(Path.Combine(examples, "matlab-rf-match.jgs"));

        ScriptRunResult result = await _engine.RunAsync(
            script, new ScriptContext(_output, (_, figure) => _figures.Add(figure), examples), default);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, _figures.Count); // the rectangular plot and the Smith chart
        Assert.Contains("microstrip Z0", _output.NormalText);
    }

    /// <summary>Walks up from the test assembly to the repository's <c>examples</c> directory.</summary>
    private static string LocateExamplesDirectory()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "examples");
            if (File.Exists(Path.Combine(candidate, "matlab-rf-match.jgs")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository's examples directory.");
    }

    [Fact]
    public async Task Gammain_OnAOnePort_ReturnsS11()
    {
        // A 1-port network: gammain ignores the load and returns S11 (0.5 here).
        string path = Path.Combine(_directory, "load.s1p");
        File.WriteAllText(path, "# GHz S RI R 50\n1.0  0.5 0.0\n");
        ScriptRunResult result = await Run("""
            print(real(gammain(sparameters('load.s1p'))))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("[0.5]", _output.NormalText.Trim());
    }
}
