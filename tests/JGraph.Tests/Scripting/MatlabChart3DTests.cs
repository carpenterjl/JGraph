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
        Assert.Contains("stem3 has no option 'Colour'", error);
        Assert.Contains("MarkerFaceColor", error);
    }

    [Fact]
    public async Task StemCoordinatesMustBeTheSameLength()
    {
        string error = await RunExpectingFailure("stem3([1 2], [1], [1 2]);");
        Assert.Contains("stem3", error);
        Assert.Contains("same length", error);
    }

    // --- bar3 and bar3h ---------------------------------------------------------------------------

    [Fact]
    public async Task ABarFieldTakesRowPositionsAWidthALayoutWordAndOptions()
    {
        await RunAsserting("""
            figure(1);
            h = bar3([10 20], [1 2; 3 4], 0.5, 'stacked', 'FaceAlpha', 0.4);
            disp(get(h, 'Type'));
            disp(get(h, 'YData'));
            disp(get(h, 'ZData'));
            disp(get(h, 'BarWidth'));
            disp(get(h, 'Style'));
            disp(get(h, 'FaceAlpha'));

            % The chart turns the axes into a 3-D one, seen from MATLAB's default corner.
            disp(get(gca, 'View'));
            """);

        Assert.Equal(
            new[] { "surface", "[10, 20]", "[1, 2; 3, 4]", "0.5", "stacked", "0.4", "[-37.5, 30]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheWholeMatrixIsOneObjectAndTheHorizontalVerbOnlySetsAProperty()
    {
        await RunAsserting("""
            figure(1);
            h = bar3h([1 2; 3 4], 'r');
            disp(get(h, 'Horizontal'));
            disp(get(h, 'FaceColor'));

            % MATLAB answers with a surface per column; this is one object for the whole chart,
            % because the boxes are painted back to front and the sort has to see all of them.
            disp(numel(findobj(gcf, 'Type', 'surface')));
            """);

        Assert.Equal(new[] { "on", "[1, 0, 0]", "1" }, _output.NormalLines);
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

    [Fact]
    public async Task ARaisedPieTakesTheSameArgumentsAsAFlatOnePlusAThickness()
    {
        await RunAsserting("""
            figure(1);
            h = pie3([1 2 1], [0 1 0], {'a','b','c'}, 'Height', 0.5, 'StartAngle', 0);
            disp(get(h, 'Type'));
            disp(get(h, 'Labels'));

            % An explode flag becomes the tenth of a radius MATLAB pushes a wedge out by.
            disp(get(h, 'Explode'));
            disp(get(h, 'Height'));
            disp(get(h, 'StartAngle'));
            """);

        Assert.Equal(
            new[] { "surface", "{'a', 'b', 'c'}", "[0, 0.1, 0]", "0.5", "0" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AnUnlabelledRaisedPieAnswersWithThePercentagesItWrote()
    {
        await RunAsserting("""
            figure(1);
            h = pie3([1 1 2]);
            disp(get(h, 'Labels'));
            disp(get(h, 'Values'));
            """);

        Assert.Equal(new[] { "{'25%', '25%', '50%'}", "[1, 1, 2]" }, _output.NormalLines);
    }

    [Fact]
    public async Task AnExplodeFlagIsNeededForEveryWedge()
    {
        string error = await RunExpectingFailure("pie3([1 2], [1 2 3]);");
        Assert.Contains("pie3", error);
        Assert.Contains("3 entries but there are 2 values", error);
    }

    [Fact]
    public async Task ANegativeShareIsRefusedBecauseAWedgeCannotHaveOne()
    {
        string error = await RunExpectingFailure("pie3([1 -2]);");
        Assert.Contains("pie3", error);
        Assert.Contains("negative share", error);
    }
}
