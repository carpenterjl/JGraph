using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M57 wave E: <c>swarmchart</c>, <c>swarmchart3</c> and <c>bubblechart3</c> through the script
/// surface — what each verb switches on, what the options change, and what a misspelled spread says.
/// </summary>
[Collection("JG facade")]
public class MatlabSwarmChartTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSwarmChartTests() => JG.Reset();

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

    /// <summary>Two columns of readings, four in one and six in the other.</summary>
    private const string Readings = """
        x = [1 1 1 1 2 2 2 2 2 2];
        y = [1 2 3 4 1 1 2 2 3 4];
        """;

    [Fact]
    public async Task ASwarmChartIsAScatterWithTheSidewaysSpreadTurnedOn()
    {
        await RunAsserting($$"""
            figure(1);
            {{Readings}}
            h = swarmchart(x, y);
            disp(get(h, 'Type'));
            disp(get(h, 'XJitter'));
            disp(get(h, 'YJitter'));
            disp(get(h, 'XJitterWidth'));

            % The readings are untouched: the spread moved the markers, not the data.
            disp(get(h, 'XData'));

            % And it stayed inside the width, which is what the width is for.
            disp(max(abs(get(h, 'XJitterOffsets'))) <= 0.5 * get(h, 'XJitterWidth') + 1e-12);
            """);

        Assert.Equal(
            new[]
            {
                "scatter", "density", "none", "0.9",
                "[1, 1, 1, 1, 2, 2, 2, 2, 2, 2]", "true",
            },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheSpreadAndItsWidthAreBothSaidByName()
    {
        await RunAsserting($$"""
            figure(1);
            {{Readings}}
            h = swarmchart(x, y, 36, 'XJitter', 'rand', 'XJitterWidth', 0.4);
            disp(get(h, 'XJitter'));
            disp(get(h, 'XJitterWidth'));
            disp(max(abs(get(h, 'XJitterOffsets'))) <= 0.2 + 1e-12);

            % Zero hands the width back to the data, which is the only way to undo one.
            set(h, 'XJitterWidth', 0);
            disp(get(h, 'XJitterWidth'));

            % And a spread of none makes it the scatter it always was.
            h2 = swarmchart(x, y, 'XJitter', 'none');
            disp(get(h2, 'XJitter'));
            disp(max(abs(get(h2, 'XJitterOffsets'))));
            """);

        Assert.Equal(
            new[] { "rand", "0.4", "true", "0.9", "none", "0" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AnOrdinaryScatterCanSpreadItsPointsToo()
    {
        // The properties are the marker chart's, not the verb's — which is the evidence that this is
        // one chart with a setting rather than two charts.
        await RunAsserting("""
            figure(1);
            s = scatter([1 2], [1 2], 'XJitter', 'density');
            disp(get(s, 'XJitter'));
            b = bubblechart([1 2], [1 2], [3 4], 'YJitter', 'rand');
            disp(get(b, 'YJitter'));
            """);

        Assert.Equal(new[] { "density", "rand" }, _output.NormalLines);
    }

    [Fact]
    public async Task ASwarmInSpaceSpreadsBothOfTheFlatCoordinates()
    {
        await RunAsserting("""
            figure(1);
            h = swarmchart3([1 1 1 2 2 2], [1 1 1 2 2 2], [1 1 2 2 3 3]);
            disp(get(h, 'Type'));
            disp(get(h, 'XJitter'));
            disp(get(h, 'YJitter'));
            disp(get(h, 'ZJitter'));
            disp(get(h, 'ZData'));
            disp(max(abs(get(h, 'ZJitterOffsets'))));
            """);

        Assert.Equal(
            new[] { "scatter", "density", "density", "none", "[1, 1, 2, 2, 3, 3]", "0" },
            _output.NormalLines);
    }

    [Fact]
    public async Task BubblesInSpaceAreSizedByValueAndNotSpread()
    {
        await RunAsserting("""
            figure(1);
            h = bubblechart3([1 2 3], [1 2 3], [1 2 3], [10 20 30]);
            disp(get(h, 'SizeData'));
            disp(numel(get(h, 'BubbleDiameters')));
            disp(get(h, 'XJitter'));

            % The diameters run from the smallest bubble to the largest, as bubblesize says.
            d = get(h, 'BubbleDiameters');
            disp(d(1) < d(3));
            """);

        Assert.Equal(new[] { "[10, 20, 30]", "3", "none", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task AMisspelledSpreadNamesTheFourItTakes()
    {
        string error = await RunExpectingFailure("swarmchart([1 2], [1 2], 'XJitter', 'wiggle');");
        Assert.Contains("XJitter is one of none, density, rand, randn", error);
        Assert.Contains("wiggle", error);
    }

    [Fact]
    public async Task AWidthIsANumberOfZeroOrMore()
    {
        string error = await RunExpectingFailure("swarmchart([1 2], [1 2], 'XJitterWidth', -1);");
        Assert.Contains("width of zero or more", error);
    }

    [Fact]
    public async Task ABubbleChartInSpaceNeedsItsSizes()
    {
        string error = await RunExpectingFailure("bubblechart3([1 2], [1 2], [1 2]);");
        Assert.Contains("bubblechart3 needs the sizes as well as the positions", error);
    }

    [Fact]
    public async Task EveryReadingStillNeedsAllOfItsCoordinates()
    {
        string error = await RunExpectingFailure("swarmchart([1 2 3], [1 2]);");
        Assert.Contains("the first has 3 values but the second has 2", error);
    }
}
