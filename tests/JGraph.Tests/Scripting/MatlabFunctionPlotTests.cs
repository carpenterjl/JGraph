using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M58: the function plotters. Each verb is handed a function rather than data, so what these tests
/// pin is where it chose to read the function and what it did with the readings — the drawing itself
/// is an ordinary plot, and the verbs it draws with have their own suites.
/// </summary>
[Collection("JG facade")]
public class MatlabFunctionPlotTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabFunctionPlotTests() => JG.Reset();

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

    // --- fplot ----------------------------------------------------------------------------------

    [Fact]
    public Task FplotDrawsALineOverTheDefaultDomain() => RunAsserting("""
        figure(1);
        h = fplot(@(x) sin(x));
        assert(strcmp(get(h, 'Type'), 'line'));

        x = get(h, 'XData');
        y = get(h, 'YData');
        assert(abs(x(1) + 5) < 1e-9);
        assert(abs(x(end) - 5) < 1e-9);
        assert(numel(x) == numel(y));

        % Every drawn point is the function's own value, and the readings only ever move forward.
        assert(max(abs(y - sin(x))) < 1e-12);
        assert(all(diff(x) > 0));
        """);

    [Fact]
    public Task FplotTakesItsOwnInterval() => RunAsserting("""
        figure(1);
        h = fplot(@(x) x.^2, [0 3]);
        x = get(h, 'XData');
        assert(abs(x(1)) < 1e-12);
        assert(abs(x(end) - 3) < 1e-12);
        """);

    /// <summary>
    /// The claim the whole milestone rests on: readings are spent on the part of the curve that
    /// bends. A uniform grid would put a quarter of them on each quarter of the domain.
    /// </summary>
    [Fact]
    public Task FplotSpendsItsReadingsWhereTheCurveBends() => RunAsserting("""
        figure(1);
        h = fplot(@(x) atan(50 * x), [-5 5]);
        x = get(h, 'XData');
        assert(numel(x) > 23);
        assert(sum(abs(x) <= 0.25) > numel(x) / 2);
        """);

    [Fact]
    public Task APoleIsABreakAndNotAWall() => RunAsserting("""
        figure(1);
        h = fplot(@(x) 1 ./ x, [-5 5]);
        y = get(h, 'YData');

        % One gap, at the pole, and nothing left standing at a height that would draw a wall.
        assert(sum(isnan(y)) == 1);
        assert(max(abs(y(~isnan(y)))) < 100);
        """);

    [Fact]
    public Task ShowPolesOffKeepsTheRunawayReadings() => RunAsserting("""
        figure(1);
        h = fplot(@(x) 1 ./ x, [-5 5], 'ShowPoles', 'off');
        y = get(h, 'YData');
        assert(max(abs(y(~isnan(y)))) > 100);
        """);

    [Fact]
    public Task MeshDensityIsWhereTheSamplerStarts() => RunAsserting("""
        figure(1);
        straight = fplot(@(x) 2 * x + 1, [0 1]);
        assert(numel(get(straight, 'XData')) == 23);

        figure(2);
        coarse = fplot(@(x) 2 * x + 1, [0 1], 'MeshDensity', 7);
        assert(numel(get(coarse, 'XData')) == 7);
        """);

    [Fact]
    public Task FplotTakesASpecAndTheLineProperties() => RunAsserting("""
        figure(1);
        h = fplot(@(x) sin(x), [0 pi], 'r--', 'LineWidth', 2.5, 'DisplayName', 'wave');
        assert(get(h, 'LineWidth') == 2.5);
        assert(strcmp(get(h, 'DisplayName'), 'wave'));
        assert(isequal(get(h, 'Color'), [1 0 0]));
        """);

    [Fact]
    public Task TwoFunctionsAreACurveInThePlane() => RunAsserting("""
        figure(1);
        h = fplot(@(t) cos(t), @(t) sin(t), [0 2*pi]);
        x = get(h, 'XData');
        y = get(h, 'YData');
        radius = sqrt(x.^2 + y.^2);
        assert(max(abs(radius - 1)) < 1e-9);
        """);

    /// <summary>
    /// A handle that cannot take a whole array is asked one parameter at a time instead, and answers
    /// the same curve.
    /// </summary>
    [Fact]
    public Task AHandleThatCannotTakeAnArrayIsAskedOneAtATime() => RunAsserting("""
        figure(1);
        h = fplot(@(x) max(x, 0), [-2 2]);
        x = get(h, 'XData');
        y = get(h, 'YData');
        assert(numel(x) > 3);
        assert(max(abs(y - max(x, 0))) < 1e-12);
        """);

    [Fact]
    public Task TheLegacyTwoOutputFormAnswersWithTheReadings() => RunAsserting("""
        [X, Y] = fplot(@(x) x.^2, [0 1]);
        assert(numel(X) == numel(Y));
        assert(abs(Y(1)) < 1e-12);
        assert(abs(Y(end) - 1) < 1e-12);
        assert(max(abs(Y - X.^2)) < 1e-12);
        """);

    // --- fplot3 ---------------------------------------------------------------------------------

    [Fact]
    public Task Fplot3DrawsACurveInSpace() => RunAsserting("""
        figure(1);
        h = fplot3(@(t) sin(t), @(t) cos(t), @(t) t, [0 4*pi]);
        assert(strcmp(get(h, 'Type'), 'line'));

        x = get(h, 'XData');
        y = get(h, 'YData');
        z = get(h, 'ZData');
        assert(numel(x) == numel(z));
        assert(max(abs(x.^2 + y.^2 - 1)) < 1e-9);
        assert(abs(z(end) - 4*pi) < 1e-9);
        """);

    /// <summary>
    /// M58 found this: a line in space kept its coordinates unreachable, so <c>get(h, 'ZData')</c> on
    /// a <c>plot3</c> handle named a property the object did not answer to. The verb that found it was
    /// <c>fplot3</c>, but the gap was M54's and belongs to <c>plot3</c> too.
    /// </summary>
    [Fact]
    public Task ALineInSpaceAnswersForItsCoordinates() => RunAsserting("""
        figure(1);
        h = plot3([1 2 3], [4 5 6], [7 8 9]);
        assert(isequal(get(h, 'XData'), [1 2 3]));
        assert(isequal(get(h, 'YData'), [4 5 6]));
        assert(isequal(get(h, 'ZData'), [7 8 9]));

        set(h, 'ZData', [0 0 0]);
        assert(isequal(get(h, 'ZData'), [0 0 0]));
        assert(isequal(get(h, 'XData'), [1 2 3]));
        """);

    // --- fsurf, fmesh, fcontour -----------------------------------------------------------------

    [Fact]
    public Task FsurfReadsAGridAndDrawsASurface() => RunAsserting("""
        figure(1);
        h = fsurf(@(x, y) x.^2 + y.^2, [-2 2]);
        assert(strcmp(get(h, 'Type'), 'surface'));

        z = get(h, 'ZData');
        assert(isequal(size(z), [35 35]));
        assert(abs(z(1, 1) - 8) < 1e-12);
        assert(abs(z(18, 18)) < 1e-12);
        """);

    [Fact]
    public Task ADomainCanNameEachDirection() => RunAsserting("""
        figure(1);
        h = fmesh(@(x, y) x + y, [0 1 0 10], 'MeshDensity', 11);
        x = get(h, 'XData');
        y = get(h, 'YData');
        assert(numel(x) == 11);
        assert(abs(x(end) - 1) < 1e-12);
        assert(abs(y(end) - 10) < 1e-12);
        """);

    [Fact]
    public Task ThreeFunctionsAreAParametricSurface() => RunAsserting("""
        figure(1);
        h = fsurf(@(u, v) cos(u) .* sin(v), @(u, v) sin(u) .* sin(v), @(u, v) cos(v), ...
            [0 2*pi 0 pi], 'MeshDensity', 25);

        X = get(h, 'XData');
        Y = get(h, 'YData');
        Z = get(h, 'ZData');
        assert(isequal(size(X), [25 25]));
        assert(max(max(abs(sqrt(X.^2 + Y.^2 + Z.^2) - 1))) < 1e-9);
        """);

    /// <summary>
    /// A surface that runs away is broken rather than drawn: a spike to infinity flattens everything
    /// else in the picture into its floor.
    /// </summary>
    [Fact]
    public Task ASurfaceThatRunsAwayIsBroken() => RunAsserting("""
        figure(1);
        h = fsurf(@(x, y) 1 ./ (x.^2 + y.^2), [-2 2]);
        z = get(h, 'ZData');
        assert(sum(sum(isnan(z))) > 0);
        assert(max(max(z(~isnan(z)))) < 200);
        """);

    [Fact]
    public Task FcontourTakesItsLevelsOrWorksThemOut() => RunAsserting("""
        figure(1);
        listed = fcontour(@(x, y) x.^2 - y.^2, [-2 2], 'LevelList', [-2 0 2]);
        assert(strcmp(get(listed, 'Type'), 'contour'));
        assert(isequal(get(listed, 'LevelList'), [-2 0 2]));

        figure(2);
        stepped = fcontour(@(x, y) x + y, [-2 2], 'LevelStep', 1, 'Fill', 'on', 'LineWidth', 2);
        levels = get(stepped, 'LevelList');
        assert(numel(levels) == 9);
        assert(all(abs(diff(levels) - 1) < 1e-12));
        assert(strcmp(get(stepped, 'Filled'), 'on'));
        assert(get(stepped, 'LineWidth') == 2);
        """);

    [Fact]
    public Task ASurfaceTakesTheDrawingOptionsUnderMatlabsNames() => RunAsserting("""
        figure(1);
        h = fsurf(@(x, y) x + y, [-1 1], 'ShowContours', 'on', 'FaceAlpha', 0.5, 'LineStyle', 'none');
        assert(strcmp(get(h, 'ShowContourBelow'), 'on'));
        assert(abs(get(h, 'Opacity') - 0.5) < 1e-12);
        """);

    // --- fimplicit and fimplicit3 ---------------------------------------------------------------

    [Fact]
    public Task FimplicitDrawsTheCurveWhereTheFunctionIsZero() => RunAsserting("""
        figure(1);
        h = fimplicit(@(x, y) x.^2 + y.^2 - 1, [-2 2]);
        assert(strcmp(get(h, 'Type'), 'line'));

        x = get(h, 'XData');
        y = get(h, 'YData');
        radius = sqrt(x.^2 + y.^2);
        assert(max(abs(radius - 1)) < 1e-3);
        assert(sum(isnan(x)) == 0);
        """);

    /// <summary>A curve in two pieces is one object with a gap in it, not two objects.</summary>
    [Fact]
    public Task ACurveInPiecesIsOneObjectWithAGap() => RunAsserting("""
        figure(1);
        h = fimplicit(@(x, y) x.^2 - y.^2 - 1, [-3 3], 'MeshDensity', 81);
        x = get(h, 'XData');
        assert(sum(isnan(x)) >= 1);
        assert(numel(findobj(gcf, 'Type', 'line')) == 1);
        """);

    [Fact]
    public Task Fimplicit3DrawsTheSurfaceWhereTheFunctionIsZero() => RunAsserting("""
        figure(1);
        h = fimplicit3(@(x, y, z) x.^2 + y.^2 + z.^2 - 1, [-2 2], 'MeshDensity', 25);
        assert(strcmp(get(h, 'Type'), 'patch'));

        X = get(h, 'XData');
        Y = get(h, 'YData');
        Z = get(h, 'ZData');
        assert(numel(X) > 100);
        assert(max(abs(sqrt(X.^2 + Y.^2 + Z.^2) - 1)) < 0.05);

        % Every face names three vertices, counting from one.
        faces = get(h, 'Faces');
        assert(size(faces, 2) == 3);
        assert(min(min(faces)) >= 1);
        assert(max(max(faces)) <= numel(X));
        """);

    [Fact]
    public async Task AFunctionThatIsNeverZeroHasNoSurface()
    {
        string message = await RunExpectingFailure(
            "figure(1); fimplicit3(@(x, y, z) x.^2 + y.^2 + z.^2 + 10, [-1 1]);");
        Assert.Contains("never zero", message);
    }

    // --- the legacy ez family -------------------------------------------------------------------

    [Fact]
    public Task TheLegacyVerbsLookOverATurnOfTheCircle() => RunAsserting("""
        figure(1);
        h = ezplot(@(x) sin(x));
        x = get(h, 'XData');
        assert(abs(x(1) + 2*pi) < 1e-9);
        assert(abs(x(end) - 2*pi) < 1e-9);

        figure(2);
        h3 = ezplot3(@(t) sin(t), @(t) cos(t), @(t) t);
        z = get(h3, 'ZData');
        assert(abs(z(1)) < 1e-12);
        assert(abs(z(end) - 2*pi) < 1e-9);
        """);

    [Fact]
    public Task AFunctionCanBeWrittenAsText() => RunAsserting("""
        figure(1);
        h = ezplot('x .* sin(x)');
        x = get(h, 'XData');
        y = get(h, 'YData');
        assert(max(abs(y - x .* sin(x))) < 1e-12);
        """);

    /// <summary>
    /// The one place these verbs decide from what they were handed: text naming two variables is an
    /// equation, and the curve drawn is where it holds.
    /// </summary>
    [Fact]
    public Task TextNamingTwoVariablesIsAnImplicitCurve() => RunAsserting("""
        figure(1);
        h = ezplot('x.^2 + y.^2 - 1');
        x = get(h, 'XData');
        y = get(h, 'YData');
        assert(max(abs(sqrt(x.^2 + y.^2) - 1)) < 1e-2);
        """);

    /// <summary>
    /// A name the workspace already answers to is still a variable of the expression when it is one
    /// of the six letters these verbs are documented in terms of.
    /// </summary>
    [Fact]
    public Task TheStandardLettersStayVariablesEvenWhenTheWorkspaceHasThem() => RunAsserting("""
        x = 1:10;
        figure(1);
        h = ezplot('x.^2');
        assert(numel(get(h, 'XData')) > 10);
        assert(abs(min(get(h, 'YData'))) < 1e-9);
        """);

    [Fact]
    public Task AnExpressionOfOneVariableStillMakesASurface() => RunAsserting("""
        figure(1);
        h = ezsurf('x.^2');
        assert(strcmp(get(h, 'Type'), 'surface'));
        assert(isequal(size(get(h, 'ZData')), [35 35]));
        """);

    [Fact]
    public Task TheContouredPairDrawContoursAndTheOthersDoNot() => RunAsserting("""
        figure(1);
        plain = ezmesh(@(x, y) x + y, [-1 1]);
        assert(strcmp(get(plain, 'ShowContourBelow'), 'off'));

        figure(2);
        both = ezmeshc(@(x, y) x + y, [-1 1]);
        assert(strcmp(get(both, 'ShowContourBelow'), 'on'));

        figure(3);
        filled = ezcontourf('sin(x) + cos(y)');
        assert(strcmp(get(filled, 'Type'), 'contour'));
        assert(strcmp(get(filled, 'Filled'), 'on'));

        figure(4);
        lines = ezcontour('sin(x) + cos(y)');
        assert(strcmp(get(lines, 'Filled'), 'off'));
        """);

    /// <summary>
    /// A circle drawn in polar has a constant radius, so a sampler watching only the radius would call
    /// it flat. The angles are chosen from the drawn curve instead.
    /// </summary>
    [Fact]
    public Task EzpolarFollowsTheDrawnCurveRatherThanTheRadius() => RunAsserting("""
        figure(1);
        h = ezpolar(@(t) 1 + 0*t);
        assert(numel(get(h, 'XData')) > 40);

        figure(2);
        rose = ezpolar('cos(2*t)');
        assert(numel(get(rose, 'XData')) > 40);
        """);

    [Fact]
    public Task EzplotThreeTakesTheAnimateWordAndDrawsTheWholeCurve() => RunAsserting("""
        figure(1);
        h = ezplot3(@(t) sin(t), @(t) cos(t), @(t) t, [0 4*pi], 'animate');
        z = get(h, 'ZData');
        assert(abs(z(end) - 4*pi) < 1e-9);
        """);

    [Fact]
    public async Task ALegacyVerbSaysWhereThePropertiesWent()
    {
        string message = await RunExpectingFailure("figure(1); ezplot(@(x) x, 'MeshDensity', 11);");
        Assert.Contains("legacy spelling", message);
        Assert.Contains("fplot", message);
    }

    // --- what is refused ------------------------------------------------------------------------

    [Fact]
    public async Task AMisspeltOptionIsRefusedByName()
    {
        string message = await RunExpectingFailure("figure(1); fplot(@(x) x, 'MeshDenisty', 11);");
        Assert.Contains("MeshDenisty", message);
        Assert.Contains("MeshDensity", message);
    }

    [Fact]
    public async Task ABackwardsIntervalIsRefused()
    {
        string message = await RunExpectingFailure("figure(1); fplot(@(x) x, [5 2]);");
        Assert.Contains("larger", message);
    }

    [Fact]
    public async Task SomethingThatIsNotAFunctionIsRefused()
    {
        string message = await RunExpectingFailure("figure(1); fplot([1 2 3]);");
        Assert.Contains("function", message);
    }

    [Fact]
    public async Task Fplot3NeedsAllThreeFunctions()
    {
        string message = await RunExpectingFailure("figure(1); fplot3(@(t) t, @(t) t);");
        Assert.Contains("three functions", message);
    }
}
