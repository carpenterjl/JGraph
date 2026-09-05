using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// <c>histogram2</c>, <c>residue</c> and the two forms of <c>nargin</c> (M122) — the three names the
/// capability probe was missing outright, as against the ones it was refusing a container.
/// </summary>
/// <remarks>
/// Every number here is R2024a's. The normalizations in particular are worth pinning by value: five
/// of the six are one division away from each other, so a formula that is wrong in the divisor still
/// produces a plausible grid.
/// </remarks>
[Collection("JG facade")]
public class MatlabHistogram2M122Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabHistogram2M122Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    private const string Readings = "xv = (0:0.1:1); yv = sin(xv);\n";

    /// <summary>
    /// The default grid is the one <c>histcounts2</c> answers for the same readings, which is the
    /// whole point of the chart doing no counting of its own.
    /// </summary>
    [Fact]
    public Task TheDefaultGridIsTheOneHistcounts2Answers() => Task.Run(() =>
    {
        string grid = Run(Readings + """
            h = histogram2(xv, yv);
            [n, xe, ye] = histcounts2(xv, yv);
            fprintf('%s %s %s | %s %s %s', mat2str(get(h, 'Values')), mat2str(get(h, 'XBinEdges')), ...
              mat2str(get(h, 'YBinEdges')), mat2str(n), mat2str(xe), mat2str(ye));
            """);

        string[] halves = grid.Split(" | ");
        Assert.Equal(halves[1], halves[0]);
        Assert.Equal("[5 0;1 5] [0 0.5 1] [0 0.5 1]", halves[0]);
    });

    [Theory]
    [InlineData("count", "[5 0;1 5]")]
    [InlineData("probability", "[0.454545 0;0.0909091 0.454545]")]
    [InlineData("countdensity", "[20 0;4 20]")]
    [InlineData("pdf", "[1.81818 0;0.363636 1.81818]")]
    [InlineData("cumcount", "[5 5;6 11]")]
    [InlineData("cdf", "[0.454545 0.454545;0.545455 1]")]
    public Task EveryNormalizationMatchesMatlab(string normalization, string expected) => Task.Run(() =>
        Assert.Equal(expected, Run(
            Readings
            + $"h = histogram2(xv, yv, 'Normalization', '{normalization}');\n"
            + "disp(mat2str(get(h, 'Values'), 6));")));

    /// <summary>
    /// The bin count can be one number for both directions or a pair, positionally or by name, and a
    /// pair of edge vectors can stand where the count does.
    /// </summary>
    [Fact]
    public Task TheBinsCanBeAskedForFourWays() => Task.Run(() =>
    {
        string bins = Run(Readings + """
            a = histogram2(xv, yv, 4);
            b = histogram2(xv, yv, [3 4]);
            c = histogram2(xv, yv, 'NumBins', [2 5]);
            d = histogram2(xv, yv, [0 0.5 1], [0 0.5 1]);
            e = histogram2(xv, yv, 'BinWidth', [0.25 0.25]);
            fprintf('%s %s %s %s %s', mat2str(get(a, 'NumBins')), mat2str(get(b, 'NumBins')), ...
              mat2str(get(c, 'NumBins')), mat2str(get(d, 'NumBins')), mat2str(get(e, 'NumBins')));
            """);

        Assert.Equal("[4 4] [3 4] [2 5] [2 2] [4 4]", bins);
    });

    /// <summary>Counts worked out elsewhere, with no readings to count.</summary>
    [Fact]
    public Task AGridCanBeHandedOverAlreadyCounted() => Task.Run(() =>
    {
        string given = Run("""
            a = histogram2('XBinEdges', [0 1], 'YBinEdges', [0 1], 'BinCounts', 5);
            b = histogram2('XBinEdges', [0 1], 'YBinEdges', [0 1 2 3], 'BinCounts', [1 2 3]);
            fprintf('%s %s | %s %s', mat2str(get(a, 'BinCounts')), mat2str(size(get(a, 'BinCounts'))), ...
              mat2str(get(b, 'BinCounts')), mat2str(size(get(b, 'BinCounts'))));
            """);

        Assert.Equal("5 [1 1] | [1 2 3] [1 3]", given);
    });

    /// <summary>
    /// <c>FaceColor</c> takes three words as well as a colour, and reads back the word that was
    /// written — <c>auto</c> and <c>flat</c> draw the same picture but are not the same answer.
    /// </summary>
    [Fact]
    public Task FaceColorRemembersWhichWordItWasGiven() => Task.Run(() =>
    {
        string words = Run(Readings + """
            a = histogram2(xv, yv);
            b = histogram2(xv, yv, 'FaceColor', 'flat');
            c = histogram2(xv, yv, 'FaceColor', 'none');
            d = histogram2(xv, yv, 'FaceColor', [1 0 0]);
            fprintf('%s %s %s %s', get(a, 'FaceColor'), get(b, 'FaceColor'), get(c, 'FaceColor'), ...
              mat2str(get(d, 'FaceColor')));
            """);

        Assert.Equal("auto flat none [1 0 0]", words);
    });

    /// <summary><c>Data</c> is one row per reading, x then y.</summary>
    [Fact]
    public Task TheReadingsReadBackAsPairs() => Task.Run(() =>
        Assert.Equal("[11 2]", Run(
            Readings + "h = histogram2(xv, yv);\ndisp(mat2str(size(get(h, 'Data'))));")));

    /// <summary>
    /// The display style decides how many dimensions the axes has, which is this chart's one
    /// peculiarity — and it has to hold whether the style arrived as an option or through <c>set</c>.
    /// </summary>
    [Fact]
    public void TheDisplayStyleDecidesWhetherTheAxesIsThreeDimensional()
    {
        JG.Reset();
        var plot = new Histogram2Plot([0, 0.4, 0.9], [0, 0.5, 1.0]);
        AxesModel axes = JG.Gca();
        axes.Plots.Add(plot);

        axes.Is3D = true;
        Assert.Equal(Histogram2DisplayStyle.Bar3, plot.DisplayStyle);

        plot.DisplayStyle = Histogram2DisplayStyle.Tile;
        Assert.Equal(Histogram2DisplayStyle.Tile, plot.DisplayStyle);
    }

    /// <summary>
    /// An empty bin contributes no box unless it is asked for, which is what keeps a mostly-empty
    /// grid from reading as a solid floor.
    /// </summary>
    [Fact]
    public void AnEmptyBinIsNotABoxUnlessItIsAskedFor()
    {
        var plot = new Histogram2Plot([0, 0.4, 0.9], [0, 0.5, 1.0]) { NumBins = (4, 4) };

        int drawn = plot.Boxes().Count;
        plot.ShowEmptyBins = true;
        int all = plot.Boxes().Count;

        Assert.True(drawn < all, "an empty bin was drawn without being asked for");
        Assert.Equal(16, all);
    }

    /// <summary>Edges the caller named survive a change that would otherwise re-choose them.</summary>
    [Fact]
    public void NamedEdgesAreNotQuietlyMovedByANormalization()
    {
        var plot = new Histogram2Plot([0.1, 0.4, 0.9], [0.2, 0.5, 1.0]);
        plot.SetBinEdges([0, 0.5, 1], [0, 0.5, 1]);

        plot.Normalization = "probability";

        Assert.Equal([0, 0.5, 1], plot.XBinEdges);
        Assert.Equal([0, 0.5, 1], plot.YBinEdges);
    }

    // --- residue ---------------------------------------------------------------------------------

    [Fact]
    public Task ResidueAnswersMatlabsColumnsAndRow() => Task.Run(() =>
    {
        string answer = Run("""
            [r, p, k] = residue([1 1], [1 3 2]);
            [r2, p2, k2] = residue([2 5 3 6], [1 6 11 6]);
            fprintf('%s %s %s %d %d | %s %s', mat2str(r), mat2str(p), mat2str(k), ...
              size(r, 2), size(p, 2), mat2str(round(r2)), mat2str(k2));
            """);

        // mat2str writes an empty as the call that makes it, as MATLAB does.
        Assert.Equal("[1;0] [-2;-1] zeros(0,0) 1 1 | [-6;-4;3] 2", answer);
    });

    [Fact]
    public Task ResidueReadBackwardsRebuildsThePolynomials() => Task.Run(() =>
    {
        string back = Run("""
            [r, p, k] = residue([1 1], [1 3 2]);
            [b, a] = residue(r, p, k);
            fprintf('%s %s', mat2str(round(b, 10)), mat2str(round(a, 10)));
            """);

        Assert.Equal("[1 1] [1 3 2]", back);
    });

    /// <summary>The residues of a repeated pole run in ascending power, which MATLAB documents.</summary>
    [Fact]
    public Task ARepeatedPoleReadsInAscendingPower() => Task.Run(() =>
        Assert.Equal(
            "[2.25;-1.25;0.5]",
            Run("[r, ~, ~] = residue([1 0 0], conv([1 3], [1 2 1]));\ndisp(mat2str(round(r, 6)));")));

    // --- nargin and nargout ----------------------------------------------------------------------

    /// <summary>
    /// The two questions the one name asks. Inside a function body it is the count the call passed;
    /// given a function it is the count that function declares, and the local binding shadows the
    /// builtin exactly as MATLAB's does.
    /// </summary>
    [Fact]
    public Task NarginIsBothTheCallsCountAndTheDeclarationsCount() => Task.Run(() =>
    {
        string counts = Run("""
            f = @(a, b) a + b;
            fprintf('%d %d %d %d %d %d', nargin(f), nargout(f), nargin(@sin), ...
              nargin('two'), nargout('two'), inner(1, 2));
            function [a, b] = two(x, y, varargin)
              a = x; b = y;
            end
            function r = inner(a, b)
              r = nargin;
            end
            """);

        // -3 is MATLAB's sign convention: two fixed inputs and then varargin.
        Assert.Equal("2 -1 1 -3 2 2", counts);
    });

    [Fact]
    public void ANameThatIsNoFunctionIsRefusedByMatlabsOwnIdentifier()
    {
        var context = new ScriptContext(new RecordingScriptOutput(), (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            "n = nargin('nosuchfunctionxyz');", context, default, sourceId: "", hook: null,
            JgsDialect.Matlab);

        Assert.False(result.Success);
        Assert.Contains("Not a valid MATLAB file", result.Message, StringComparison.Ordinal);
    }
}
