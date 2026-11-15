using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Chips wave 6, lane 6A (ADR 0372): open items 326 and 327. The MATLAB-dialect expectations are
/// R2025b's (probes p6a, p6b); the fixture oi_graphics_defaults holds the full recording. These tests
/// hold what the fixture cannot: what the JGS dialect keeps, and that the default menus and toolbar
/// are handles alone, never in the model a window draws or a document saves.
/// </summary>
[Collection("JG facade")]
public sealed class GraphicsDefaultsLeftoverTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "jgraph-6a-" + Guid.NewGuid().ToString("N"));

    public GraphicsDefaultsLeftoverTests()
    {
        JG.Reset();
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
            // A leftover temporary directory is not worth failing a test over.
        }
    }

    private async Task Run(IScriptRepl engine, string code)
    {
        await using IScriptSession session = engine.CreateSession(new ScriptContext(
            _output, (_, _) => { }, _directory, resolvePath: null, new TestFigureFiles()));
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private Task Matlab(string code) => Run(new MatlabScriptEngine(), code);

    private Task Jgs(string code) => Run(new JgsScriptEngine(), code);

    [Fact]
    public async Task TheDefaultMenusAndToolbarAreHandlesAloneNeverTheFiguresOwnBars()
    {
        await Matlab("""
            f = figure('Visible', 'off');
            assert(numel(findall(f, 'Type', 'uimenu')) == 89);
            assert(numel(findall(f, 'Type', 'uitoolbar')) == 1);
            assert(isempty(f.Children));
            mine = uimenu(f, 'Text', 'Mine');
            assert(numel(f.Children) == 1);
            """);

        FigureModel figure = JG.CurrentFigure;
        Assert.Single(figure.Menus); // the script's own menu; the defaults are drawn by nobody
        Assert.Empty(figure.Toolbars);
    }

    [Fact]
    public async Task ADeletedDefaultMenuTakesItsEntriesAndTheRestStay()
    {
        await Matlab("""
            f = figure('Visible', 'off');
            mf = findall(f, 'Tag', 'figMenuFile');
            delete(mf);
            assert(~isvalid(mf));
            assert(numel(findall(f, 'Type', 'uimenu')) == 79);
            tb = findall(f, 'Type', 'uitoolbar');
            delete(tb);
            assert(isempty(findall(f, 'Type', 'uitoolbar')));
            clf(f);
            assert(numel(findall(f, 'Type', 'uimenu')) == 79);
            """);
    }

    [Fact]
    public async Task AJgsPatchKeepsItsOwnBlackEdge()
    {
        await Jgs("""
            patch([0, 1, 1], [0, 0, 1], 'r');
            """);
        PatchPlot jgs = JG.Gca().Plots.OfType<PatchPlot>().Single();
        Assert.Equal(JGraph.Core.Drawing.Colors.Black, jgs.EdgeColor);
        Assert.Equal(0.75, jgs.EdgeWidth);

        JG.Reset();
        await Matlab("""
            p = patch([0 1 1], [0 0 1], 'r');
            assert(isequal(p.EdgeColor, [33 33 33] / 255) && p.LineWidth == 0.5);
            """);
    }

    [Fact]
    public async Task AJgsHistogramOfNothingKeepsItsUnitSpan()
    {
        await Jgs("""
            histogram([], 4);
            """);
        HistogramPlot jgs = JG.Gca().Plots.OfType<HistogramPlot>().Single();
        Assert.Equal([0, 0.25, 0.5, 0.75, 1], jgs.BinEdges);

        JG.Reset();
        await Matlab("""
            [n, e] = histcounts([], 4);
            assert(isequal(e, 0:4));
            """);
    }

    [Fact]
    public async Task ALegendOfAGroupComesBackFromTheDocumentStandingForTheGroup()
    {
        string path = Path.Combine(_directory, "g.fig").Replace(@"\", @"\\");
        await Matlab($$"""
            f = figure('Visible', 'off');
            ax = axes(f);
            g = hggroup(ax);
            line(g, [1 2], [1 2]);
            line(ax, [1 2], [3 3]);
            legend(g, 'grp');
            savefig(f, '{{path}}');
            close(f);
            f = openfig('{{path}}', 'invisible');
            lg = findall(f, 'Type', 'legend');
            assert(isequal(lg.String, {'grp'}));
            assert(strcmp(get(lg.PlotChildren, 'Type'), 'hggroup'));
            """);
    }
}
