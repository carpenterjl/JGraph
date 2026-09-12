using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// ADR 0152: the gaps found writing the head2head_GUI launcher. A comma-separated list reached
/// through a field, <c>datestr</c>'s own format language, growing a cell by brace assignment, the
/// grouped bar layout, and <c>bar</c>'s name/value form after a width. The answers themselves are
/// pinned against R2025b by <c>adr0152_gui_round_gaps.m</c>; these are the parts a fixture cannot
/// see — what reaches the console, what a refusal says, and how wide a bar is drawn.
/// </summary>
[Collection("JG facade")]
public class GuiRoundGapsTests : IDisposable
{
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public GuiRoundGapsTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult RunMatlab(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    private string Printed() => _output.NormalText.Replace("\r", string.Empty).Trim();

    // --- close says nothing as a statement ------------------------------------------------------

    [Fact]
    public void CloseAll_AsACommand_PrintsNothing_AndStillAnswersWhenAnOutputIsAsked()
    {
        ScriptRunResult result = RunMatlab("""
            figure
            close all
            figure
            close('all')
            figure
            went = close('all');
            fprintf('went=%d\n', went);
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("went=1", Printed());
    }

    // --- a comma-separated list through a field --------------------------------------------------

    [Fact]
    public void AFieldOfAFieldSpreadsItsStructArray_WhereverTheListHasRoomToGo()
    {
        ScriptRunResult result = RunMatlab("""
            s = struct('a', {1, 2, 3}, 'b', {'x', 'y', 'z'});
            h.list = s;
            k = {s};
            fprintf('%d %d %d\n', numel({h.list.b}), numel({k{1}.b}), numel({h.list(2:3).b}));
            fprintf('%s%s%s\n', h.list.b);
            fprintf('%s\n', strjoin(sort({h.list.b}), ','));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("3 3 2\nxyz\nx,y,z", Printed());
    }

    [Fact]
    public void APathThroughAFieldIsReadOnce_SoAnIndexWithASideEffectRunsOnce()
    {
        // The list is asked for by evaluating the path, and the path is evaluated once. A subscript
        // that counts its own calls is the only way to see that from a script.
        ScriptRunResult result = RunMatlab("""
            global calls
            calls = 0;
            s = struct('b', {'x', 'y', 'z'});
            h.list = s;
            names = {h.list(counted()).b};
            fprintf('%d %s\n', calls, names{1});

            function i = counted()
                global calls
                calls = calls + 1;
                i = 2;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("1 y", Printed());
    }

    // --- growing a cell by brace assignment ------------------------------------------------------

    [Fact]
    public void ACellThatIsNeitherRowNorColumn_HasNoOneDimensionToGrowAlong()
    {
        ScriptRunResult result = RunMatlab("""
            M = {1 2; 3 4};
            M{5} = 'i';
            """);

        Assert.False(result.Success);
        Assert.Contains("Attempt to grow array along ambiguous dimension.", result.Message);
    }

    [Fact]
    public void ACellReachedThroughAField_StillWritesInRangeAndRefusesToGrow()
    {
        ScriptRunResult inRange = RunMatlab("""
            s.c = {'p', 'q'};
            s.c{1, 2} = 'r';
            fprintf('%s %s\n', s.c{1}, s.c{2});
            """);

        Assert.True(inRange.Success, inRange.Message + _output.ErrorText);
        Assert.Equal("p r", Printed());

        ScriptRunResult beyond = RunMatlab("""
            s.c = {'p', 'q'};
            s.c{3, 1} = 'r';
            """);

        Assert.False(beyond.Success);
        Assert.Contains("cannot grow by brace assignment", beyond.Message);
    }

    [Fact]
    public void GrowingByBothSubscriptsKeepsEveryElementWhereItsOwnSubscriptsPutIt()
    {
        ScriptRunResult result = RunMatlab("""
            C = {'a', 'b'; 'c', 'd'};
            C{3, 3} = 'z';
            fprintf('%s %s %s %s %s %d\n', C{1, 1}, C{2, 1}, C{1, 2}, C{2, 2}, C{3, 3}, numel(C{3, 1}));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("a c b d z 0", Printed());
    }

    // --- datestr ---------------------------------------------------------------------------------

    [Fact]
    public void ANumberThatNamesNoDatestrFormat_IsRefusedByName()
    {
        ScriptRunResult result = RunMatlab("d = datestr(now, 99);");

        Assert.False(result.Success);
        Assert.Contains("is not one of datestr's numbered formats", result.Message);
    }

    [Fact]
    public void DatestrReadsItsOwnLanguageForADatetimeToo_NotTheDatetimeOnesTokens()
    {
        ScriptRunResult result = RunMatlab("""
            t = datetime(2026, 9, 12, 14, 35, 7);
            fprintf('%s|%s|%s\n', datestr(t, 'yyyy-mm-dd HH:MM:SS'), datestr(t, 31), datestr(t, 'MM'));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("2026-09-12 14:35:07|2026-09-12 14:35:07|35", Printed());
    }

    // --- categorical -----------------------------------------------------------------------------

    [Fact]
    public void AValueSetAndItsNamesMustBeTheSameLength()
    {
        ScriptRunResult result = RunMatlab("c = categorical([1 2], [1 2], {'A'});");

        Assert.False(result.Success);
        Assert.Contains("value set has 2 values but there are 1 category names", result.Message);
    }

    [Fact]
    public void AValueThatStandsTwiceInTheValueSetIsRefused()
    {
        ScriptRunResult result = RunMatlab("c = categorical([1 2], [1 1], {'A', 'B'});");

        Assert.False(result.Success);
        Assert.Contains("appears more than once in the value set", result.Message);
    }

    // --- the grouped bar layout ------------------------------------------------------------------

    [Fact]
    public void TwoSeriesStandAtR2025bsOffsets_AndReachExactlyAsFarAsItsBarsDo()
    {
        // R2025b: XEndPoints 0.857142… and 1.142857…, and the leftmost bar's own edges measured
        // from the drawn patch are 0.742857… to 0.971428… — so the chart's reach is the first of
        // those and the last bar's right edge the mirror of it.
        IReadOnlyList<BarPlot> bars = JG.Bar([1, 2, 3], [[1.0, 3, 5], [2.0, 4, 6]], stacked: false);

        Assert.Equal(2, bars.Count);
        Assert.Equal(0.857142857142857, bars[0].CenterAt(0), 12);
        Assert.Equal(1.142857142857143, bars[1].CenterAt(0), 12);
        Assert.Equal(0.742857142857143, bars[0].GetXDataBounds().Min, 12);
        Assert.Equal(3.257142857142857, bars[0].GetXDataBounds().Max, 12);
    }

    [Fact]
    public void OneSeriesKeepsTheWholeGapBetweenPositions_SoItsBarIsBarWidthOfIt()
    {
        // R2025b draws the single bar of bar(1:3, y) from 0.6 to 1.4, which is BarWidth of the gap.
        IReadOnlyList<BarPlot> bars = JG.Bar([1, 2, 3], [[1.0, 3, 5]], stacked: false);

        Assert.Single(bars);
        Assert.Equal(0.6, bars[0].GetXDataBounds().Min, 12);
        Assert.Equal(3.4, bars[0].GetXDataBounds().Max, 12);
    }

    [Fact]
    public void ASixthSeriesIsWhereTheGroupStopsWideningWithIt()
    {
        // n/(n + 1.5) reaches 0.8 at six series and is capped there, which R2025b's XEndPoints show.
        IReadOnlyList<BarPlot> six = JG.Bar([1, 2], [.. Enumerable.Range(0, 6).Select(_ => new[] { 1.0, 1.0 })], stacked: false);
        IReadOnlyList<BarPlot> ten = JG.Bar([1, 2], [.. Enumerable.Range(0, 10).Select(_ => new[] { 1.0, 1.0 })], stacked: false);

        Assert.Equal(0.666666666666667, six[0].CenterAt(0), 12);
        Assert.Equal(0.64, ten[0].CenterAt(0), 12);
    }

    // --- bar's name/value form -------------------------------------------------------------------

    [Fact]
    public void AWidthFollowedByPairs_IsAWidthAndThePairs()
    {
        ScriptRunResult result = RunMatlab("""
            h = bar(1, 0.5, 0.6, 'FaceColor', [0 0 1], 'EdgeColor', 'none');
            fprintf('%g %g %g %g %g\n', h.BarWidth, h.XEndPoints(1), h.FaceColor);
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("0.6 1 0 0 1", Printed());

        BarPlot drawn = Assert.IsType<BarPlot>(JG.Gca().Plots[0]);
        Assert.Equal(0, drawn.EdgeWidth);
    }

    [Fact]
    public void AFaceColorOfNoneLeavesTheBarUnpainted()
    {
        ScriptRunResult result = RunMatlab("h = bar([1 2 3], 'FaceColor', 'none');");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(0, Assert.IsType<BarPlot>(JG.Gca().Plots[0]).FaceAlpha);
    }

    [Fact]
    public void TwoScalarsAloneAreStillXAndY_NotAValueAndAWidth()
    {
        ScriptRunResult result = RunMatlab("""
            h = bar(1, 0.5);
            fprintf('%g %g\n', h.BarWidth, h.XEndPoints(1));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("0.8 1", Printed());
    }
}
