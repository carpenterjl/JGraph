using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M52: the nine <c>mov*</c> statistics once they can be told what window to use and what an
/// incomplete one at either end means. Expected values are MATLAB's own.
/// </summary>
/// <remarks>
/// NaN inside a window is included by default, the same reading <c>sum</c> and <c>mean</c> take of it
/// and the one these functions already had before there was a flag to ask. Whether MATLAB's own
/// default differs for some of the nine is recorded in ADR 0052 rather than guessed at here —
/// <c>'omitnan'</c> is one word away either way.
/// </remarks>
[Collection("JG facade")]
public class MatlabMovingWindowTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabMovingWindowTests() => JG.Reset();

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

    /// <summary>
    /// The centred window every one of the nine shares: an odd width sits on the point, an even one
    /// puts its extra element behind, and both shrink rather than pad at the ends.
    /// </summary>
    [Fact]
    public Task TheDefaultWindowIsCentredAndShrinksAtTheEnds() => RunAsserting("""
        assert(isequal(movsum([1 2 3 4 5], 3), [3 6 9 12 9]));
        assert(isequal(movmean([1 2 3 4 5], 3), [1.5 2 3 4 4.5]));
        assert(isequal(movsum([1 2 3 4], 2), [1 3 5 7]));
        """);

    /// <summary>
    /// <c>[nb nf]</c> says how far back and forward the window reaches, which is the only way to ask
    /// for a trailing window — the one a running average of past readings actually wants.
    /// </summary>
    [Fact]
    public Task ABeforeAndAfterPairAsksForAnOffCentreWindow() => RunAsserting("""
        assert(isequal(movmean([1 2 3 4 5], [1 0]), [1 1.5 2.5 3.5 4.5]));
        assert(isequal(movmean([1 2 3 4 5], [0 1]), [1.5 2.5 3.5 4.5 5]));
        assert(isequal(movsum([1 2 3], [2 0]), [1 3 6]));
        assert(isequal(movmean([1 2 3], [0 0]), [1 2 3]));
        """);

    [Fact]
    public Task DiscardKeepsOnlyThePointsWhoseWindowFits() => RunAsserting("""
        assert(isequal(movmean([1 2 3 4 5], 3, 'Endpoints', 'discard'), [2 3 4]));
        assert(isequal(size(movmean([1 2 3 4 5], 3, 'Endpoints', 'discard')), [1 3]));
        assert(isempty(movmean([1 2], 5, 'Endpoints', 'discard')));
        """);

    [Fact]
    public Task FillMarksTheIncompleteWindowsRatherThanShrinkingThem() => RunAsserting("""
        f = movmean([1 2 3 4 5], 3, 'Endpoints', 'fill');
        assert(isnan(f(1)));
        assert(isnan(f(5)));
        assert(isequal(f(2:4), [2 3 4]));
        """);

    /// <summary>
    /// A number in the <c>'Endpoints'</c> slot pads the places past the ends with it, so every window
    /// is the full width — which is the reading that keeps a moving sum comparable across the array.
    /// </summary>
    [Fact]
    public Task ANumberPadsThePlacesPastTheEnds() => RunAsserting("""
        assert(isequal(movsum([1 2 3], 3, 'Endpoints', 0), [3 6 5]));
        assert(isequal(movsum([1 2 3], 3, 'Endpoints', 10), [13 6 15]));
        assert(isequal(movmean([2 2 2], 3, 'Endpoints', 2), [2 2 2]));
        """);

    [Fact]
    public Task OmitNanDropsMissingReadingsFromTheWindow() => RunAsserting("""
        assert(isequal(movmean([1 NaN 3], 3, 'omitnan'), [1 2 3]));
        assert(isequal(movsum([1 NaN 3], 3, 'omitnan'), [1 4 3]));
        assert(isequal(movmax([1 NaN 3], 3, 'omitnan'), [1 3 3]));
        % Including it spreads the NaN to every window that touches it, which here is all of them.
        m = movmean([1 NaN 3], 3, 'includenan');
        assert(isequal(isnan(m), [true true true]));
        """);

    /// <summary>
    /// A window with nothing left in it is the statistic of nothing — the same rule the reductions
    /// settled on: 0 for a sum, 1 for a product, NaN for anything that has to divide.
    /// </summary>
    [Fact]
    public Task AWindowThatOmitsEverythingIsTheStatisticOfNothing() => RunAsserting("""
        assert(movsum([NaN NaN], 1, 'omitnan')(1) == 0);
        assert(movprod([NaN NaN], 1, 'omitnan')(1) == 1);
        assert(isnan(movmean([NaN NaN], 1, 'omitnan')(1)));
        """);

    [Fact]
    public Task TheWindowFollowsTheDimensionItIsGiven() => RunAsserting("""
        m = [1 2; 3 4];
        assert(isequal(movmean(m, 3, 2), [1.5 1.5; 3.5 3.5]));
        assert(isequal(movmean(m, 3, 1), [2 3; 2 3]));
        assert(isequal(movsum(m, 3), [4 6; 4 6]));
        """);

    /// <summary>Every one of the nine takes the same options; the summary at the end is all that differs.</summary>
    [Fact]
    public Task AllNineTakeTheSameWindowAndTheSameEndpoints() => RunAsserting("""
        names = {@movmean, @movmedian, @movsum, @movprod, @movmax, @movmin, @movstd, @movvar, @movmad};
        x = [1 2 3 4 5];
        for k = 1:numel(names)
            f = names{k};
            assert(numel(f(x, 3)) == 5);
            assert(numel(f(x, 3, 'Endpoints', 'discard')) == 3);
            assert(numel(f(x, [1 0])) == 5);
        end
        """);

    [Fact]
    public Task TheWindowOptionsSayWhatTheyWantedWhenTheyAreWrong() => RunAsserting("""
        ok = 0;
        try
            movmean([1 2 3], 3, 'Endpoints', 'stretch');
        catch err
            ok = ok + ~isempty(strfind(err.message, 'shrink'));
        end
        try
            movmean([1 2 3], 0);
        catch err
            ok = ok + ~isempty(strfind(err.message, 'at least 1'));
        end
        try
            movmean([1 2 3], [1 2 3]);
        catch err
            ok = ok + ~isempty(strfind(err.message, 'before after'));
        end
        assert(ok == 3);
        """);

    /// <summary>
    /// <c>'SamplePoints'</c> places the readings where they were taken, so the window is a distance
    /// along those places rather than a count of elements. M66 built it; before that it was refused
    /// by name, because quietly counting elements would have answered a different question.
    /// </summary>
    [Fact]
    public Task SamplePointsMakeTheWindowADistance() => RunAsserting("""
        % The last reading sits far from the rest, so nothing else falls inside its window.
        spread = movmean([1 2 3], 3, 'SamplePoints', [1 2 20]);
        assert(abs(spread(3) - 3) < 1e-12);
        assert(abs(spread(1) - 1.5) < 1e-12);

        % Padding needs places outside the data, and sample points say there are none.
        ok = 0;
        try
            movmean([1 2 3], 3, 'SamplePoints', [1 2 4], 'Endpoints', 0);
        catch err
            ok = ~isempty(strfind(err.message, 'nowhere to pad'));
        end
        assert(ok == 1);
        """);
}
