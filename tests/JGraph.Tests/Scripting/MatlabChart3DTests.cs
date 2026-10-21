using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M57 wave C: <c>stem3</c>, <c>bar3</c>/<c>bar3h</c> and <c>pie3</c> — the three-dimensional forms
/// of charts the flat verbs already draw, read through the same argument grammar and answering the
/// same property names.
/// </summary>
[Collection("JG facade")]
public class MatlabChart3DTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabChart3DTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task<string> RunExpectingFailure(string code)
    {
        int before = _output.Errors.Count;
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        return string.Concat(_output.Errors.Skip(before));
    }

    // --- stem3 ------------------------------------------------------------------------------------

    [Fact]
    public async Task AStemInSpaceCarriesItsThreeCoordinatesAndItsStyle()
    {
        await RunAsserting("""
            figure(1);

            % A line spec, a lone 'filled', and name-value pairs after both — which no rule that
            % counts the arguments left over could read, since there is an even number of them.
            h = stem3([1 2 3], [4 5 6], [7 8 9], 'r--*', 'filled', 'LineWidth', 3, 'BaseValue', 1);
            disp(get(h, 'Type'));
            disp(get(h, 'Color'));
            disp(get(h, 'LineStyle'));
            disp(get(h, 'Marker'));
            disp(get(h, 'LineWidth'));
            disp(get(h, 'BaseValue'));

            % 'filled' means the marker takes the stem's colour inside, whatever the spec made it.
            disp(get(h, 'MarkerFaceColor'));
            """);

        Assert.Equal(
            new[] { "stem", "[1, 0, 0]", "--", "star", "3", "1", "[1, 0, 0]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AMatrixOfHeightsStandsItsStemsOnTheGridItIsIndexedBy()
    {
        await RunAsserting("""
            figure(1);
            h = stem3([1 2; 3 4]);
            disp(get(h, 'XData'));
            disp(get(h, 'YData'));
            disp(get(h, 'ZData'));
            """);

        Assert.Equal(
            new[] { "[1, 2, 1, 2]", "[1, 1, 2, 2]", "[1, 2, 3, 4]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AMisspelledStemOptionIsRefusedRatherThanReadAsALineSpec()
    {
        string error = await RunExpectingFailure("stem3([1 2], [1 2], [1 2], 'Colour', 'r');");
        // Refused as R2025b refuses a name a stem does not have (open item 136, ADR 0255).
        Assert.Contains("Unrecognized property Colour for class Stem.", error);
    }

    [Fact]
    public async Task StemCoordinatesMustBeTheSameLength()
    {
        string error = await RunExpectingFailure("stem3([1 2], [1], [1 2]);");
        Assert.Contains("stem3", error);
        Assert.Contains("same length", error);
    }

    // --- bar3 and bar3h ---------------------------------------------------------------------------

    /// <summary>
    /// Since open item 112 (ADR 0229) a bar field is R2025b's: a surface per column, each box six
    /// rows of four vertices as makebars.m lays them out, stacked columns riding on the ones before.
    /// </summary>
    [Fact]
    public async Task ABarFieldIsASurfacePerColumnTakingRowPositionsAWidthALayoutWordAndOptions()
    {
        await RunAsserting("""
            figure(1);
            h = bar3([10 20], [1 2; 3 4], 0.5, 'stacked', 'FaceAlpha', 0.4);
            disp(size(h));
            disp(get(h(1), 'Type'));
            disp(size(get(h(1), 'YData')));

            % The second column's first box stands on the first column's height, 1 to 3.
            z = get(h(2), 'ZData');
            disp(z(2, :));
            disp(get(h(2), 'FaceAlpha'));
            disp(get(gca, 'YTick'));

            % The chart turns the axes into a 3-D one, seen from MATLAB's default corner, counting
            % its rows downwards.
            disp(get(gca, 'View'));
            disp(get(gca, 'YDir'));
            """);

        Assert.Equal(
            new[] { "[1, 2]", "surface", "[12, 4]", "[1, 3, 3, 1]", "0.4", "[10, 20]", "[-37.5, 30]", "reverse" },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheHorizontalVerbSwapsTheHeightsAndThePositionsOfEveryColumn()
    {
        await RunAsserting("""
            figure(1);
            h = bar3h([1 2; 3 4], 'r');
            disp(numel(findobj(gcf, 'Type', 'surface')));
            disp(get(h(1), 'FaceColor'));

            % Heights run along y and the row positions along z: row 1's box is 1 high, 0.6 to 1.4 deep.
            y = get(h(1), 'YData');
            z = get(h(1), 'ZData');
            disp(y(2, :));
            disp(z(2, :));
            """);

        Assert.Equal(new[] { "2", "[1, 0, 0]", "[0, 1, 1, 0]", "[0.6, 0.6, 0.6, 0.6]" }, _output.NormalLines);
    }

    [Fact]
    public async Task ARowPositionIsNeededForEveryRowOfTheMatrix()
    {
        string error = await RunExpectingFailure("bar3([1 2 3], [1 2; 3 4]);");
        Assert.Contains("bar3", error);
        Assert.Contains("3 row positions but z has 2 rows", error);
    }

    [Fact]
    public async Task AWordThatIsNeitherALayoutNorAColourIsRefused()
    {
        string error = await RunExpectingFailure("bar3([1 2], 'nope', 5);");
        Assert.Contains("bar3", error);
        Assert.Contains("nope", error);
    }

    [Fact]
    public async Task AMisspelledBarOptionNamesTheOnesItTakes()
    {
        string error = await RunExpectingFailure("bar3([1 2], 'BarThickness', 0.5);");
        Assert.Contains("bar3 has no option 'BarThickness'", error);
        Assert.Contains("BarWidth", error);
    }

    // --- pie3 -------------------------------------------------------------------------------------

    /// <summary>
    /// Since open item 112 (ADR 0229) a raised pie is R2025b's: a bottom patch, a side surface, a
    /// top patch and a text per slice, in a row; the JGraph extras still shape it.
    /// </summary>
    [Fact]
    public async Task ARaisedPieIsAPatchASurfaceAPatchAndATextPerSlice()
    {
        await RunAsserting("""
            figure(1);
            h = pie3([1 2 1], [0 1 0], {'a','b','c'}, 'Height', 0.5, 'StartAngle', 0);
            disp(size(h));
            disp(get(h(1), 'Type'));
            disp(get(h(2), 'Type'));
            disp(get(h(3), 'Type'));
            disp(get(h(4), 'Type'));
            disp(get(h(4), 'String'));
            disp(get(h(6), 'DisplayName'));
            z = unique(get(h(3), 'ZData'));
            disp(z(1));

            % An explode flag pushes the slice a tenth of a radius out, and a start angle of zero
            % puts the first arc point on the x axis.
            x = get(h(5), 'XData');
            disp(abs(x(1)) > 0.05);
            y = get(h(1), 'YData');
            disp(abs(y(2)) < 1e-9);
            disp(get(gca, 'View'));
            """);

        Assert.Equal(
            new[] { "[1, 12]", "patch", "surface", "patch", "text", "a", "b", "0.5", "true", "true", "[-37.5, 30]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AnUnlabelledRaisedPieWritesWholePercentages()
    {
        await RunAsserting("""
            figure(1);
            h = pie3([1 1 2]);
            disp(numel(h));
            disp(get(h(4), 'String'));
            disp(get(h(8), 'String'));
            disp(get(h(12), 'String'));
            """);

        Assert.Equal(new[] { "12", "25%", "25%", "50%" }, _output.NormalLines);
    }

    [Fact]
    public async Task AnExplodeFlagIsNeededForEveryWedge()
    {
        string error = await RunExpectingFailure("pie3([1 2], [1 2 3]);");
        Assert.Contains("X and EXPLODE must be the same length", error);
    }

    [Fact]
    public async Task AllNegativeSharesAreRefusedAndOneIsDroppedWithAWarning()
    {
        string error = await RunExpectingFailure("pie3([-1 -2]);");
        Assert.Contains("nonnegative", error);

        await RunAsserting("""
            figure(1);
            h = pie3([1 -2]);
            disp(numel(h));
            [~, id] = lastwarn;
            disp(id);
            """);

        Assert.Equal(new[] { "4", "MATLAB:pie:NegativeData" }, _output.NormalLines);
    }
}
