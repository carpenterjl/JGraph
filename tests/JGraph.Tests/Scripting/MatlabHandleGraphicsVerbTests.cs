using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M54 wave B: the verbs that treat a figure object as something to interrogate, search for and
/// copy. Everything here reads the same property table the dot does, so what is checked is that the
/// verbs agree with the dot and with each other — and that a search, a copy and a clear each mean
/// what MATLAB means by them.
/// </summary>
[Collection("JG facade")]
public class MatlabHandleGraphicsVerbTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabHandleGraphicsVerbTests() => JG.Reset();

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

    /// <summary>Runs code that is meant to fail, and answers only the complaint this run made.</summary>
    private async Task<string> RunExpectingFailure(string code)
    {
        int before = _output.Errors.Count;
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success);
        return string.Concat(_output.Errors.Skip(before));
    }

    [Fact]
    public async Task GetAndTheDotGiveTheSameAnswer()
    {
        await RunAsserting("""
            figure(1);
            p = plot(1:3, [1 2 3]);
            disp(isequal(get(p, 'Color'), p.Color));
            disp(isequal(get(gca, 'XLim'), gca.XLim));
            disp(get(p, 'Type'));
            """);

        Assert.Equal(new[] { "true", "true", "line" }, _output.NormalLines);
    }

    [Fact]
    public async Task GetWithNoNameListsEveryProperty()
    {
        await RunAsserting("""
            figure(1);
            p = plot(1:3, [1 2 3]);
            all = get(p);
            disp(class(all));
            disp(isfield(all, 'LineWidth'));
            disp(isfield(all, 'Type'));
            disp(all.Type);
            """);

        Assert.Equal(new[] { "struct", "true", "true", "line" }, _output.NormalLines);
    }

    [Fact]
    public async Task AskingSeveralHandlesOrSeveralNamesAnswersACell()
    {
        await RunAsserting("""
            figure(1); hold on;
            p = plot(1:3, [1 2 3]);
            s = scatter(1:3, [3 2 1]);
            kinds = get([p s], 'Type');
            disp(class(kinds));
            disp(kinds{1});
            disp(kinds{2});
            pair = get(p, {'Type', 'LineWidth'});
            disp(numel(pair));
            disp(pair{1});
            """);

        Assert.Equal(new[] { "cell", "line", "scatter", "2", "line" }, _output.NormalLines);
    }

    [Fact]
    public async Task SetWritesEveryPairToEveryHandleGiven()
    {
        // This is the point of the verb: one call over the result of a search.
        await RunAsserting("""
            figure(1); hold on;
            plot(1:3, [1 2 3]);
            plot(1:3, [3 2 1]);
            set(findobj('Type', 'line'), 'LineWidth', 2.5, 'LineStyle', '--');
            lines = findobj('Type', 'line');
            disp(get(lines(1), 'LineWidth'));
            disp(get(lines(2), 'LineStyle'));
            """);

        Assert.Equal(new[] { "2.5", "--" }, _output.NormalLines);
    }

    [Fact]
    public async Task SetTakesACellOfNamesAndACellOfValues()
    {
        await RunAsserting("""
            figure(1);
            p = plot(1:3, [1 2 3]);
            set(p, {'LineWidth', 'Tag'}, {4, 'first'});
            disp(p.LineWidth);
            disp(p.Tag);
            names = set(p);
            disp(class(names));
            disp(numel(names) > 5);
            """);

        Assert.Equal(new[] { "4", "first", "cell", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task SetUsedAsAStatementPrintsNothing()
    {
        await RunAsserting("""
            figure(1);
            p = plot(1:3, [1 2 3]);
            set(p, 'LineWidth', 2)
            """);

        Assert.DoesNotContain("ans", _output.NormalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindobjSearchesEveryFigureUnlessGivenOne()
    {
        await RunAsserting("""
            figure(1); hold on;
            plot(1:3, [1 2 3]);
            scatter(1:3, [3 2 1]);
            ax = gca;
            figure(2);
            plot(1:4, [4 3 2 1]);
            disp(numel(findobj('Type', 'line')));
            disp(numel(findobj(ax, 'Type', 'line')));
            disp(numel(findobj(ax)));
            disp(numel(findobj(ax, 'flat')));
            """);

        // Two lines across two figures; one under the first figure's axes; that axes and its two
        // series; and the axes alone once the search is told not to descend.
        Assert.Equal(new[] { "2", "1", "3", "1" }, _output.NormalLines);
    }

    [Fact]
    public async Task FindallReachesTheFurnitureFindobjLeavesAlone()
    {
        // An axes' rulers are not its children, so a plain search never sees them — which is the
        // distinction MATLAB draws between the two verbs.
        await RunAsserting("""
            figure(1);
            plot(1:3, [1 2 3]);
            ax = gca;
            disp(numel(findobj(ax, 'Type', 'numericruler')));
            disp(numel(findall(ax, 'Type', 'numericruler')) > 0);
            """);

        Assert.Equal(new[] { "0", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task ABareSearchNameIsTheSearchRatherThanTheFunction()
    {
        await RunAsserting("""
            figure(1);
            plot(1:3, [1 2 3]);
            disp(numel(findobj));
            """);

        // The figure, its axes, and the one line in it.
        Assert.Equal(new[] { "3" }, _output.NormalLines);
    }

    [Fact]
    public async Task ThePredicatesTellALiveHandleFromAnyOtherNumber()
    {
        await RunAsserting("""
            figure(1);
            p = plot(1:3, [1 2 3]);
            disp(ishandle(p));
            disp(ishghandle(p));
            disp(ishandle(42));
            disp(isgraphics(p, 'line'));
            disp(isgraphics(p, 'axes'));
            disp(all(ishandle([p gca])));
            """);

        Assert.Equal(new[] { "true", "true", "false", "true", "false", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task AncestorClimbsToTheKindItWasAskedFor()
    {
        await RunAsserting("""
            figure(1);
            p = plot(1:3, [1 2 3]);
            disp(ancestor(p, 'axes') == gca);
            disp(ancestor(p, 'figure') == 1);
            disp(ancestor(p, 'axes', 'toplevel') == gca);
            disp(isempty(ancestor(p, 'colorbar')));
            """);

        Assert.Equal(new[] { "true", "true", "true", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task ACopyCarriesTheDataAndThenGoesItsOwnWay()
    {
        await RunAsserting("""
            figure(1);
            p = plot(1:3, [1 2 3]);
            p.LineWidth = 3;
            figure(2);
            c = copyobj(p, gca);
            disp(c.Type);
            disp(isequal(c.YData, p.YData));
            disp(c.LineWidth);
            c.LineWidth = 9;
            disp(p.LineWidth);
            disp(numel(gca.Children));
            """);

        Assert.Equal(new[] { "line", "true", "3", "3", "1" }, _output.NormalLines);
    }

    [Fact]
    public async Task CopyingAnAxesBringsWhatWasDrawnInIt()
    {
        await RunAsserting("""
            figure(1); hold on;
            plot(1:3, [1 2 3]);
            scatter(1:3, [3 2 1]);
            ax = gca;
            figure(2);
            copied = copyobj(ax, 2);
            disp(copied.Type);
            disp(numel(copied.Children));
            """);

        Assert.Equal(new[] { "axes", "2" }, _output.NormalLines);
    }

    [Fact]
    public async Task GobjectsIsABlockOfBlanksToFillIn()
    {
        await RunAsserting("""
            b = gobjects(1, 3);
            disp(size(b));
            disp(any(ishandle(b)));
            figure(1);
            b(2) = plot(1:3, [1 2 3]);
            disp(ishandle(b(2)));
            """);

        Assert.Equal(new[] { "[1, 3]", "false", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task ClaEmptiesTheAxesAndResetPutsItsSettingsBack()
    {
        await RunAsserting("""
            figure(1);
            plot(1:3, [1 2 3]);
            title('kept');
            hold on;
            cla;
            disp(numel(gca.Children));
            disp(gca.Title);
            disp(ishold);
            cla reset;
            disp(isempty(gca.Title));
            disp(ishold);
            """);

        Assert.Equal(new[] { "0", "kept", "true", "true", "false" }, _output.NormalLines);
    }

    [Fact]
    public async Task NewplotEmptiesTheAxesOnlyWhenHoldIsOff()
    {
        await RunAsserting("""
            figure(1);
            plot(1:3, [1 2 3]);
            hold on;
            ax = newplot;
            disp(ax.Type);
            disp(numel(ax.Children));
            hold off;
            newplot;
            disp(numel(gca.Children));
            """);

        Assert.Equal(new[] { "axes", "1", "0" }, _output.NormalLines);
    }

    [Fact]
    public async Task TheCallbackQuestionsAnswerNothingOutsideACallback()
    {
        await RunAsserting("""
            figure(1);
            plot(1:3, [1 2 3]);
            disp(isempty(gco));
            disp(isempty(gcbo));
            disp(isempty(gcbf));
            """);

        Assert.Equal(new[] { "true", "true", "true" }, _output.NormalLines);
    }

    [Fact]
    public async Task AMisspelledPropertyNamesTheOneThatWasMeant()
    {
        string errors = await RunExpectingFailure("""
            figure(1);
            p = plot(1:3, [1 2 3]);
            get(p, 'Colour');
            """);

        Assert.Contains("Colour", errors, StringComparison.Ordinal);
        Assert.Contains("Color", errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnpairedPropertyAndAnUnknownFlagBothSayWhatIsWrong()
    {
        Assert.Contains("has none", await RunExpectingFailure("""
            figure(1);
            set(plot(1:3, [1 2 3]), 'LineWidth');
            """), StringComparison.Ordinal);

        Assert.Contains("'-depth'", await RunExpectingFailure("""
            figure(1);
            plot(1:3, [1 2 3]);
            findobj(gca, '-flat');
            """), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritingAReadOnlyPropertySaysSoRatherThanDoingNothing()
    {
        Assert.Contains("read but not written", await RunExpectingFailure("""
            figure(1);
            set(plot(1:3, [1 2 3]), 'Type', 'bar');
            """), StringComparison.Ordinal);
    }

    /// <summary>
    /// M54 wave G, found by stess_26: a colour filter matched nothing, because the comparison behind
    /// it was the identity one handles use, where two arrays are equal only when they are the same
    /// array. Half the properties worth searching on are colours.
    /// </summary>
    [Fact]
    public async Task FindobjMatchesAPropertyWhoseValueIsARowNotJustAWordOrANumber()
    {
        await RunAsserting("""
            figure(1);
            a = plot(1:5, 1:5);
            hold on;
            b = plot(1:5, 2:6);
            set(a, 'Color', [1 0 0]);
            set(b, 'Color', [0 0 1]);
            disp(numel(findobj(gcf, 'Color', [1 0 0])));
            disp(isequal(findobj(gcf, 'Color', [0 0 1]), b));
            disp(numel(findobj(gcf, 'Color', [0 1 0])));

            % A number and a word still match the way they did.
            set(a, 'LineWidth', 4);
            disp(numel(findobj(gcf, 'LineWidth', 4)));
            disp(numel(findobj(gcf, 'Type', 'line')));
            """);

        Assert.Equal(new[] { "1", "true", "0", "1", "2" }, _output.NormalLines);
    }

    /// <summary>
    /// M54 wave G, also from stess_26: MATLAB spells two verbs with one name, and only the file one
    /// was here — <c>delete(h)</c> complained that a handle was not a path.
    /// </summary>
    [Fact]
    public async Task DeleteTakesAFigureObjectAsWellAsAFileName()
    {
        await RunAsserting("""
            figure(1);
            a = plot(1:5, 1:5);
            hold on;
            b = plot(1:5, 2:6);
            c = plot(1:5, 3:7);
            delete(b);
            disp(numel(findobj(gca, 'Type', 'line')));

            % A vector deletes every one of them, and the survivors are untouched.
            delete([a c]);
            disp(isempty(findobj(gca, 'Type', 'line')));
            disp(strcmp(get(gca, 'Type'), 'axes'));

            % A deleted handle names nothing afterwards.
            failed = false;
            try
                get(b, 'Color');
            catch
                failed = true;
            end
            disp(failed);
            """);

        Assert.Equal(new[] { "2", "true", "true", "true" }, _output.NormalLines);

        // And a number that is not a handle is still a bad path, not a silent success.
        Assert.NotEmpty(await RunExpectingFailure("delete(42);"));
    }
}
