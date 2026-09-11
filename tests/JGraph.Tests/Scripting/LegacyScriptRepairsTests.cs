using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// ADR 0150: what a folder of legacy MATLAB scripts needed to run here. <c>upper</c> and
/// <c>lower</c> hand non-text back; <c>warning</c> keeps a state per identifier and
/// <c>lastwarn</c> names the identifier; <c>hist</c> exists; a mesh colours its wires through the
/// colormap; and <c>run</c> of a function file calls its main function. The MATLAB-side numbers
/// are pinned by <c>adr0150_legacy_script.m</c>; these are the behaviours a fixture cannot see —
/// what reaches the console, what the model holds, and what a saved figure keeps.
/// </summary>
[Collection("JG facade")]
public class LegacyScriptRepairsTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public LegacyScriptRepairsTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult RunMatlab(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    private string Printed() => _output.NormalText.Replace("\r", string.Empty).Trim();

    private string Warnings() => _output.ErrorText.Replace("\r", string.Empty).Trim();

    // --- upper and lower ----------------------------------------------------------------------

    [Fact]
    public void UpperOfANumber_IsTheNumber_SoALegacyNanCheckCanAskIt()
    {
        ScriptRunResult result = RunMatlab("""
            x = upper(600);
            fprintf('%s %g %d\n', class(x), x, strcmp(upper(600), 'NAN'));
            c = upper({'ab', 'cd'});
            fprintf('%s %s %s\n', class(c), c{1}, c{2});
            m = upper(['ab'; 'cd']);
            fprintf('%s|%s\n', m(1, :), m(2, :));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("double 600 0\ncell AB CD\nAB|CD", Printed());
    }

    [Fact]
    public void UpperOfACellHoldingANumber_IsRefusedByMatlabsIdentifier()
    {
        ScriptRunResult result = RunMatlab("""
            try
                upper({'ab', 65});
            catch e
                fprintf('%s|%s', e.identifier, e.message);
            end
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("MATLAB:upper:CellsMustContainChars|Cell elements must be character arrays.", Printed());
    }

    // --- warning state --------------------------------------------------------------------------

    [Fact]
    public void WarningQuery_AnswersAStruct_AndPrintsTheStateAsAStatement()
    {
        ScriptRunResult result = RunMatlab("""
            s = warning('query', 'a:b');
            fprintf('%s %s %s\n', class(s), s.identifier, s.state);
            warning('query', 'a:b')
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("struct a:b on\nThe state of warning 'a:b' is 'on'.", Printed());
        Assert.Equal(string.Empty, Warnings());
    }

    [Fact]
    public void WarningOff_SilencesTheIdentifier_ButLastwarnStillRecordsIt()
    {
        ScriptRunResult result = RunMatlab("""
            p = warning('off', 'a:b');
            warning('a:b', 'hidden %d', 4);
            [m, id] = lastwarn;
            fprintf('%s|%s|%s\n', p.state, m, id);
            warning('on', 'a:b');
            warning('a:b', 'shown %d', 5);
            warning('plain %d', 6);
            [m, id] = lastwarn;
            fprintf('%s|[%s]\n', m, id);
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("on|hidden 4|a:b\nplain 6|[]", Printed());
        Assert.Equal(["Warning: shown 5", "Warning: plain 6"], _output.Errors);
    }

    [Fact]
    public void WarningOffAlone_SilencesEverything_UntilWarningOn()
    {
        ScriptRunResult result = RunMatlab("""
            warning off
            warning('quiet');
            warning('a:b', 'also quiet');
            warning on
            warning('loud');
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("Warning: loud", Warnings());
    }

    [Fact]
    public void WarningRestores_TheStateStructItHandedOut()
    {
        ScriptRunResult result = RunMatlab("""
            p = warning('off', 'a:b');
            q = warning('query', 'a:b');
            warning(p);
            r = warning('query', 'a:b');
            fprintf('%s %s %s', p.state, q.state, r.state);
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("on off on", Printed());
    }

    [Fact]
    public void WarningStateChange_AsAStatement_PrintsNothing()
    {
        ScriptRunResult result = RunMatlab("""
            warning('off', 'MATLAB:divideByZero')
            warning('on', 'MATLAB:divideByZero')
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(string.Empty, Printed());
        Assert.Equal(string.Empty, Warnings());
    }

    // --- hist -----------------------------------------------------------------------------------

    [Fact]
    public void Hist_AnswersCountsAndCentres_WhenAsked()
    {
        ScriptRunResult result = RunMatlab("""
            [n, x] = hist([1 2 2 3 5 8 8.5 9 10], 4);
            fprintf('%s %s\n', mat2str(n), mat2str(x));
            [n, x] = hist([1 2 3; 4 5 6; 7 8 9]', 3);
            fprintf('%s %s', mat2str(n), mat2str(x));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("[4 1 0 4] [2.125 4.375 6.625 8.875]\n[3 0 0;0 3 0;0 0 3] [2.33333333333333;5;7.66666666666667]", Printed());
        Assert.Empty(JG.Gca().Plots);
    }

    [Fact]
    public void Hist_AsAStatement_DrawsTheBars_AndPrintsNothing()
    {
        ScriptRunResult result = RunMatlab("hist([1 2 2 3 5 8 8.5 9 10], 4)");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(string.Empty, Printed());
        var bars = Assert.IsType<HistogramPlot>(Assert.Single(JG.Gca().Plots));
        Assert.Equal(new double[] { 4, 1, 0, 4 }, bars.BinCounts);
        Assert.Equal(1, bars.BinEdges[0]);
        Assert.Equal(10, bars.BinEdges[^1]);
        Assert.NotNull(bars.FaceColor);
    }

    // --- mesh -----------------------------------------------------------------------------------

    [Fact]
    public void Mesh_ColoursItsWiresThroughTheColormap_AndSurfDoesNot()
    {
        ScriptRunResult result = RunMatlab("""
            figure(1); mesh(peaks(6));
            figure(2); surf(peaks(6));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        var mesh = Assert.IsType<SurfacePlot>(Assert.Single(JG.Figure(1).Axes[^1].Plots));
        Assert.True(mesh.ColormapEdges);
        Assert.Null(mesh.EdgeColor);
        Assert.Equal(SurfaceStyle.FilledWithWireframe, mesh.Style);
        Assert.NotNull(mesh.FaceColor);

        var surf = Assert.IsType<SurfacePlot>(Assert.Single(JG.Figure(2).Axes[^1].Plots));
        Assert.False(surf.ColormapEdges);
    }

    [Fact]
    public void EdgeColorFlat_AsksForColormapWires_AndANamedColourTakesThemBack()
    {
        ScriptRunResult result = RunMatlab("""
            figure(1); h = surf(peaks(6));
            set(h, 'EdgeColor', 'flat');
            fprintf('%s', get(h, 'EdgeColor'));
            figure(2); k = surf(peaks(6));
            set(k, 'EdgeColor', 'interp');
            set(k, 'EdgeColor', 'r');
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("flat", Printed());
        var asked = Assert.IsType<SurfacePlot>(Assert.Single(JG.Figure(1).Axes[^1].Plots));
        Assert.True(asked.ColormapEdges);
        Assert.Null(asked.EdgeColor);

        var named = Assert.IsType<SurfacePlot>(Assert.Single(JG.Figure(2).Axes[^1].Plots));
        Assert.False(named.ColormapEdges);
        Assert.NotNull(named.EdgeColor);
    }

    [Fact]
    public void ColormapEdges_SurviveTheGraphFormat()
    {
        Assert.True(RunMatlab("mesh(peaks(6));").Success, _output.ErrorText);
        var figure = (FigureModel)JG.Gca().Parent!;

        FigureModel loaded = GraphFormat.Deserialize(GraphFormat.Serialize(figure));

        var surface = Assert.IsType<SurfacePlot>(Assert.Single(loaded.Axes[^1].Plots));
        Assert.True(surface.ColormapEdges);
    }

    // --- a file run from another folder -----------------------------------------------------------

    /// <summary>
    /// The launcher's <c>-batch file.m</c> from another working directory: the file's folder is
    /// the running script's folder, so a helper beside it that shares a built-in's name — the
    /// legacy <c>extract.m</c> that opened ADR 0150 — takes the call exactly as it does when the
    /// folder is the working directory.
    /// </summary>
    [Fact]
    public void AFileRunFromElsewhere_FindsAHelperBesideIt_ThatSharesABuiltinsName()
    {
        string folder = Path.Combine(Path.GetTempPath(), "jgraph-adr0150-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "extract.m"), "function y = extract(w, n)\ny = w(1:n, 1:n);\n");
            string script = Path.Combine(folder, "main.m");
            File.WriteAllText(script, "y = extract(magic(4), 2);\nfprintf('%d %d', size(y));\n");

            var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), Path.GetTempPath())
            {
                ScriptPath = script,
            };
            ScriptRunResult result = JgsRunner.Run(
                File.ReadAllText(script), context, default, sourceId: "", hook: null, JgsDialect.Matlab);

            Assert.True(result.Success, result.Message + _output.ErrorText);
            Assert.Equal("2 2", Printed());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // --- run of a function file -----------------------------------------------------------------

    [Fact]
    public void Run_OfAFunctionFile_CallsItsMainFunction()
    {
        string folder = Path.Combine(Path.GetTempPath(), "jgraph-adr0150-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "mainfile.m");
            File.WriteAllText(file, "function mainfile\ndisp('main ran');\nhelper();\nfunction helper\ndisp('helper ran');\n");

            ScriptRunResult result = RunMatlab($"run('{file.Replace('\\', '/')}');");

            Assert.True(result.Success, result.Message + _output.ErrorText);
            Assert.Equal("main ran\nhelper ran", Printed());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
