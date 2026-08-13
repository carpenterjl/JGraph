using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M57 wave D: <c>binscatter</c> read through the script surface — the bin count said positionally
/// or by name, the limits, the read-only answers, and what a misspelled option does.
/// </summary>
[Collection("JG facade")]
public class MatlabBinScatterTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabBinScatterTests() => JG.Reset();

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

    /// <summary>Ten readings on a lattice, which every counting case below is checked against.</summary>
    private const string Readings = """
        x = [1 2 3 4 1 2 3 4 1 2];
        y = [1 1 1 1 2 2 2 2 3 3];
        """;

    [Fact]
    public async Task TheReadingsAreCountedIntoTheGridTheyWereAskedFor()
    {
        await RunAsserting($$"""
            figure(1);
            {{Readings}}
            h = binscatter(x, y, [2 2]);
            disp(get(h, 'Type'));
            disp(get(h, 'NumBins'));
            disp(get(h, 'XBinEdges'));
            disp(get(h, 'YBinEdges'));

            % Values is as many rows as there are bins across, which is MATLAB's way round.
            disp(get(h, 'Values'));

            % Every reading is counted once and no more, which is what the edge rule buys.
            disp(sum(sum(get(h, 'Values'))));
            """);

        Assert.Equal(
            new[]
            {
                "binscatter", "[2, 2]", "[1, 2.5, 4]", "[1, 2, 3]", "[2, 4; 2, 2]", "10",
            },
            _output.NormalLines);
    }

    [Fact]
    public async Task TheBinCountIsOneNumberForBothDirectionsOrTwoForEach()
    {
        await RunAsserting($$"""
            figure(1);
            {{Readings}}
            h = binscatter(x, y, 4, 'ShowEmptyBins', 'on', 'XLimits', [0 5]);
            disp(get(h, 'NumBins'));
            disp(get(h, 'XLimits'));
            disp(get(h, 'ShowEmptyBins'));

            % The same count said again by name, after it was already said positionally.
            set(h, 'NumBins', 3);
            disp(get(h, 'NumBins'));
            disp(size(get(h, 'Values')));
            """);

        Assert.Equal(
            new[] { "[4, 4]", "[0, 5]", "on", "[3, 3]", "[3, 3]" },
            _output.NormalLines);
    }

    [Fact]
    public async Task AnUnaskedForBinCountIsTheSquareRootChoiceAndTheChartSpansIt()
    {
        await RunAsserting($$"""
            figure(1);
            {{Readings}}
            h = binscatter(x, y);
            disp(get(h, 'NumBins'));
            disp(get(h, 'YLimits'));
            """);

        Assert.Equal(new[] { "[4, 4]", "[1, 3]" }, _output.NormalLines);
    }

    [Fact]
    public async Task AMisspelledOptionNamesTheOnesItTakes()
    {
        string error = await RunExpectingFailure("binscatter([1 2], [1 2], 'NumBinz', 4);");
        Assert.Contains("binscatter has no option 'NumBinz'", error);
        Assert.Contains("NumBins", error);
        Assert.Contains("ShowEmptyBins", error);
    }

    [Fact]
    public async Task EveryReadingNeedsBothOfItsCoordinates()
    {
        string error = await RunExpectingFailure("binscatter([1 2 3], [1 2]);");
        Assert.Contains("x has 3 readings but y has 2", error);
    }

    [Fact]
    public async Task ABinCountIsAWholeNumberAndThereIsALimitToHowManyOfThem()
    {
        string fractional = await RunExpectingFailure("binscatter([1 2], [1 2], 2.5);");
        Assert.Contains("whole number of bins", fractional);

        string toomany = await RunExpectingFailure("binscatter([1 2], [1 2], 900);");
        Assert.Contains("at most 250 in each direction", toomany);
    }

    [Fact]
    public async Task TheCountsCanBeReadButNotWritten()
    {
        string error = await RunExpectingFailure("""
            h = binscatter([1 2], [1 2]);
            set(h, 'Values', 1);
            """);

        Assert.Contains("'Values' can be read but not written", error);
    }
}
