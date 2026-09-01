using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M52: the data-analysis names the base language was missing — <c>gradient</c>, <c>trapz</c>,
/// <c>cumtrapz</c>, <c>interp1</c>, <c>polyfit</c>, <c>polyval</c>'s error estimate, <c>sortrows</c>,
/// <c>histcounts</c>, <c>corrcoef</c>, <c>cov</c>, <c>rms</c>, <c>bounds</c> — plus the two
/// signatures that were short: <c>linspace</c> without a count and <c>round</c> with one.
/// </summary>
/// <remarks>
/// Expected values are MATLAB's own, computed by hand where the closed form is short enough to
/// state (the trapezoid rule, a least-squares line, a leverage-based prediction interval) and read
/// off the definition otherwise. Two answers here change rather than add: <c>linspace(a, b, 1)</c>
/// is now b rather than a, and its last value is b exactly rather than whatever the arithmetic
/// left — both pinned so the change stays a decision.
/// </remarks>
[Collection("JG facade")]
public class MatlabDataAnalysisTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabDataAnalysisTests() => JG.Reset();

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

    // --- linspace and round -----------------------------------------------------------------------

    [Fact]
    public Task LinspaceCountsToAHundredWhenNotToldOtherwise() => RunAsserting("""
        v = linspace(0, 1);
        assert(numel(v) == 100);
        assert(v(1) == 0 && v(end) == 1);
        assert(isequal(linspace(1, 10, 4), [1 4 7 10]));
        assert(isequal(linspace(5, -5, 3), [5 0 -5]));
        """);

    [Fact]
    public Task OneSampleOfARangeIsWhereItFinishes() => RunAsserting("""
        % MATLAB's reading, and the opposite of what the old three-argument form answered.
        assert(linspace(1, 10, 1) == 10);
        assert(isempty(linspace(1, 10, 0)));
        % The last value is the endpoint itself, not start + (stop - start) as the arithmetic left it.
        w = linspace(0, 0.3, 7);
        assert(w(end) == 0.3);
        """);

    [Fact]
    public Task RoundTakesAPlaceCountAndAKindOfPlace() => RunAsserting("""
        assert(round(2.5) == 3 && round(-2.5) == -3);
        assert(round(3.14159, 2) == 3.14);
        assert(round(3.14159, 3, 'decimals') == 3.142);
        assert(round(12345, -2) == 12300);
        assert(abs(round(3.14159, 3, 'significant') - 3.14) < 1e-12);
        assert(round(123456, 2, 'significant') == 120000);
        assert(abs(round(0.0012345, 2, 'significant') - 0.0012) < 1e-15);
        assert(round(0, 3, 'significant') == 0);
        assert(isequal(round([1.4 1.5 -1.5]), [1 2 -2]));
        """);

    [Fact]
    public Task RoundNamesTheKindsOfPlaceItKnows() => RunAsserting("""
        caught = '';
        try
            round(1.5, 2, 'sig');
        catch err
            caught = err.message;
        end
        assert(contains(caught, 'significant'));
        assert(contains(caught, 'decimals'));
        """);

    // --- gradient ---------------------------------------------------------------------------------

    [Fact]
    public Task GradientTakesCentralDifferencesInsideAndOneSidedAtTheEnds() => RunAsserting("""
        assert(isequal(gradient([1 2 4 7 11]), [1 1.5 2.5 3.5 4]));
        % A spacing scales every difference; coordinates place them individually.
        assert(isequal(gradient([1 2 4], 0.5), [2 3 4]));
        g = gradient([1 4 9], [0 1 3]);
        assert(abs(g(1) - 3) < 1e-12);
        assert(abs(g(2) - 8/3) < 1e-12);
        assert(abs(g(3) - 2.5) < 1e-12);
        % A column vector's gradient runs along the column, not across its single row.
        assert(isequal(gradient([1; 4; 9]), [3; 4; 5]));
        """);

    [Fact]
    public Task GradientOfAMatrixIsOneAcrossAndOneDown() => RunAsserting("""
        A = [1 2 3; 4 5 6];
        [fx, fy] = gradient(A);
        assert(isequal(fx, [1 1 1; 1 1 1]));
        assert(isequal(fy, [3 3 3; 3 3 3]));
        [gx, gy] = gradient(A, 2, 0.5);
        assert(gx(1, 1) == 0.5);
        assert(gy(1, 1) == 6);
        % A vector has nothing to differ across, so the second gradient is zeros rather than an error.
        [~, vy] = gradient([1 2 4]);
        assert(all(vy == 0));
        """);

    // --- trapz and cumtrapz -----------------------------------------------------------------------

    [Fact]
    public Task TrapzIntegratesSampledData() => RunAsserting("""
        assert(trapz([1 2 3]) == 4);
        assert(trapz([0 1 2], [1 2 3]) == 4);
        % Half a period of a sine has area 2, which a thousand trapezoids get to five figures.
        t = linspace(0, pi, 1001);
        assert(abs(trapz(t, sin(t)) - 2) < 1e-5);
        """);

    [Fact]
    public Task TrapzWalksAMatrixColumnByColumnUnlessToldADimension() => RunAsserting("""
        B = [1 2; 3 4; 5 6];
        assert(isequal(trapz(B), [6 8]));
        assert(isequal(trapz(B, 2), [1.5; 3.5; 5.5]));
        assert(isequal(trapz([0 1 2], B), [6 8]));
        % A single number in the second slot is a dimension, which is how MATLAB tells the two
        % two-argument forms apart.
        assert(isequal(trapz(B, 1), trapz(B)));
        """);

    [Fact]
    public Task CumtrapzStartsAtZeroAndKeepsItsLength() => RunAsserting("""
        assert(isequal(cumtrapz([1 2 3]), [0 1.5 4]));
        assert(isequal(cumtrapz([0 2 4], [1 2 3]), [0 3 8]));
        c = cumtrapz([1 2; 3 4; 5 6]);
        assert(isequal(size(c), [3 2]));
        assert(isequal(c(:, 1), [0; 2; 6]));
        % The last running total is the whole area.
        assert(abs(c(end, 2) - trapz([2; 4; 6])) < 1e-12);
        """);

    // --- interp1 ----------------------------------------------------------------------------------

    [Fact]
    public Task Interp1TakesEachDocumentedMethod() => RunAsserting("""
        x = [1 2 3 4];
        v = [10 20 30 40];
        assert(isequal(interp1(x, v, [1.5 2.5 3.5]), [15 25 35]));
        % Exactly halfway rounds to the later sample, the way MATLAB's nearest does.
        assert(isequal(interp1(x, v, [1.4 1.6 2.5], 'nearest'), [10 20 30]));
        assert(isequal(interp1(x, v, [1.9 3.0], 'previous'), [10 30]));
        assert(isequal(interp1(x, v, [1.1 3.0], 'next'), [20 30]));
        % Left out, the sample positions are 1..n.
        assert(isequal(interp1(v, [1.5 2.5]), [15 25]));
        """);

    [Fact]
    public Task Interp1CubicsReproduceACubic() => RunAsserting("""
        x = [0 1 2 3 4];
        y = x .^ 3;
        % Not-a-knot is exact on the polynomial it is made of.
        assert(abs(interp1(x, y, 2.5, 'spline') - 15.625) < 1e-10);
        % pchip gives up that exactness to stop the curve overshooting, so it answers differently.
        assert(abs(interp1(x, y, 2.5, 'pchip') - 15.625) > 1e-6);
        % 'cubic' is cubic convolution, which is what 'v5cubic' has always meant and what MATLAB
        % answers today; it is exact on a cubic where pchip is not.
        assert(interp1(x, y, 2.5, 'cubic') == interp1(x, y, 2.5, 'v5cubic'));
        assert(abs(interp1(x, y, 2.5, 'cubic') - 15.625) < 1e-10);
        assert(interp1(x, y, 2.5, 'cubic') ~= interp1(x, y, 2.5, 'pchip'));
        % makima is a third cubic again, and neither of the other two.
        assert(abs(interp1(x, y, 2.5, 'makima') - 15.635834670947) < 1e-10);
        """);

    [Fact]
    public Task Interp1FillsOutsideTheSamplesTheWayItWasTold() => RunAsserting("""
        x = [1 2 3 4];
        v = [10 20 30 40];
        assert(isnan(interp1(x, v, 9)));
        assert(interp1(x, v, 5, 'linear', 'extrap') == 50);
        assert(interp1(x, v, 9, 'linear', -1) == -1);
        % The cubics extrapolate by default, which is the one place the method changes more than shape.
        assert(~isnan(interp1(x, v, 5, 'spline')));
        """);

    [Fact]
    public Task Interp1TakesUnsortedSamplesAndAMatrixOfThem() => RunAsserting("""
        assert(interp1([3 1 2], [30 10 20], 1.5) == 15);
        M = [10 100; 20 200; 30 300];
        r = interp1([1 2 3], M, [1.5 2.5]);
        assert(isequal(size(r), [2 2]));
        assert(isequal(r(:, 1), [15; 25]));
        assert(isequal(r(:, 2), [150; 250]));
        % The answer takes the shape of the query, so a column of questions gives a column of answers.
        assert(isequal(size(interp1([1 2 3], [1 2 3], [1.5; 2.5])), [2 1]));
        """);

    [Fact]
    public Task Interp1RefusesAMethodItWouldHaveToGuessAt() => RunAsserting("""
        unknown = '';
        try
            interp1([1 2 3], [1 2 3], 1.5, 'quadratic');
        catch err
            unknown = err.message;
        end
        assert(contains(unknown, 'linear'));
        assert(contains(unknown, 'previous'));

        repeated = '';
        try
            interp1([1 1 2], [1 2 3], 1.5);
        catch err
            repeated = err.message;
        end
        assert(contains(repeated, 'different'));
        """);

    // --- polyfit and polyval ----------------------------------------------------------------------

    [Fact]
    public Task PolyfitRecoversThePolynomialItsPointsCameFrom() => RunAsserting("""
        p = polyfit([1 2 3 4 5], [2 4 6 8 10], 1);
        assert(abs(p(1) - 2) < 1e-10 && abs(p(2)) < 1e-10);
        q = polyfit([1 2 3 4], [1 4 9 16], 2);
        assert(abs(q(1) - 1) < 1e-10 && abs(q(2)) < 1e-10 && abs(q(3)) < 1e-10);
        """);

    [Fact]
    public Task PolyfitReportsTheFactorizationAndResidualBehindTheFit() => RunAsserting("""
        [p, S] = polyfit([1 2 3 4 5], [2.1 3.9 6.2 7.8 10.1], 1);
        % Least squares by hand: slope = Sxy/Sxx = 19.9/10, intercept = mean(y) - slope*mean(x).
        assert(abs(p(1) - 1.99) < 1e-10);
        assert(abs(p(2) - 0.05) < 1e-10);
        assert(S.df == 3);
        assert(abs(S.normr - sqrt(0.107)) < 1e-10);
        assert(isequal(size(S.R), [2 2]));
        """);

    [Fact]
    public Task PolyvalSizesItsErrorBarFromTheFitsOwnRecord() => RunAsserting("""
        [p, S] = polyfit([1 2 3 4 5], [2.1 3.9 6.2 7.8 10.1], 1);
        [y, delta] = polyval(p, 3, S);
        assert(abs(y - 6.02) < 1e-10);
        % s * sqrt(1 + h), where s is normr/sqrt(df) and h is the leverage 1/n + (x - xbar)^2/Sxx.
        expected = (S.normr / sqrt(S.df)) * sqrt(1 + 0.2);
        assert(abs(delta - expected) < 1e-10);
        % The plain call still answers one value, and reads a polynomial highest power first.
        assert(isequal(polyval([1 0 -1], [0 1 2]), [-1 0 3]));
        """);

    [Fact]
    public Task PolyfitCentresItsPointsWhenAskedForMu() => RunAsserting("""
        [p, ~, mu] = polyfit([1 2 3 4 5], [2.1 3.9 6.2 7.8 10.1], 1);
        assert(abs(mu(1) - 3) < 1e-12);
        assert(abs(mu(2) - sqrt(2.5)) < 1e-12);
        % The centred coefficients only mean anything alongside mu, and polyval takes both.
        assert(abs(polyval(p, 3, [], mu) - 6.02) < 1e-10);
        """);

    [Fact]
    public Task PolyfitSaysWhenThereAreTooFewPoints() => RunAsserting("""
        caught = '';
        try
            polyfit([1 2], [1 2], 5);
        catch err
            caught = err.message;
        end
        assert(contains(caught, '6'));

        needsRecord = '';
        try
            [~, ~] = polyval([1 0], [1 2]);
        catch err
            needsRecord = err.message;
        end
        assert(contains(needsRecord, 'polyfit'));
        """);

    // --- sortrows ---------------------------------------------------------------------------------

    [Fact]
    public Task SortrowsOrdersByWholeColumnsAndKeepsTiesInPlace() => RunAsserting("""
        A = [3 1; 1 2; 3 0; 2 5];
        assert(isequal(sortrows(A), [1 2; 2 5; 3 0; 3 1]));
        assert(isequal(sortrows(A, 2), [3 0; 3 1; 1 2; 2 5]));
        [B, i] = sortrows(A, 1);
        assert(isequal(i, [2; 4; 1; 3]));
        assert(isequal(B(:, 1), [1; 2; 3; 3]));
        % Equal rows keep the order they arrived in, which is what makes a second sort meaningful.
        assert(isequal(sortrows([1 9; 1 8; 0 7], 1), [0 7; 1 9; 1 8]));
        """);

    [Fact]
    public Task SortrowsReadsBothWaysOfSayingDescending() => RunAsserting("""
        A = [3 1; 1 2; 3 0; 2 5];
        assert(isequal(sortrows(A, -1), sortrows(A, 1, 'descend')));
        % One direction per key, or one for all of them.
        assert(isequal(sortrows(A, [1 2], {'ascend', 'descend'}), [1 2; 2 5; 3 1; 3 0]));
        assert(isequal(sortrows(A, [1 2], 'descend'), [3 1; 3 0; 2 5; 1 2]));
        % A missing reading sorts to the back, the same reading of NaN the comparisons take.
        n = sortrows([2; NaN; 1]);
        assert(n(1) == 1 && n(2) == 2 && isnan(n(3)));
        """);

    [Fact]
    public Task SortrowsNamesAColumnThatIsNotThere() => RunAsserting("""
        caught = '';
        try
            sortrows([1 2; 3 4], 7);
        catch err
            caught = err.message;
        end
        assert(contains(caught, 'column'));

        word = '';
        try
            sortrows([1 2; 3 4], 1, 'down');
        catch err
            word = err.message;
        end
        assert(contains(word, 'descend'));
        """);

    // --- histcounts -------------------------------------------------------------------------------

    [Fact]
    public Task HistcountsBinsByCountOrByEdges() => RunAsserting("""
        d = [1 2 2 3 3 3 4 4 4 4];
        [n, e] = histcounts(d, 4);
        assert(isequal(n, [1 2 3 4]));
        % A bin count is a count and not a set of edges, so the edges left over are chosen to be
        % readable rather than to end on the data: 0.9 wide from 0.7, which reaches 4.3 (M123).
        assert(max(abs(e - [0.7 1.6 2.5 3.4 4.3])) < 1e-12);
        [n2, e2] = histcounts(d, [0 2 4 6]);
        assert(isequal(n2, [1 5 4]));
        assert(isequal(e2, [0 2 4 6]));
        % Every value is counted once, which is what the closed last bin is for.
        assert(sum(n) == numel(d) && sum(n2) == numel(d));
        assert(histcounts(3, [0 1 2 3]) * [0; 0; 1] == 1);
        """);

    [Fact]
    public Task HistcountsChoosesRoundBinsWhenItChoosesThemItself() => RunAsserting("""
        % Whole numbers over a short range get a bin each, centred on the values.
        [n, e] = histcounts([1 2 2 3 3 3 4 4 4 4]);
        assert(isequal(n, [1 2 3 4]));
        assert(isequal(e, [0.5 1.5 2.5 3.5 4.5]));
        % A named width starts the bins on a multiple of itself.
        [~, e2] = histcounts([2.3 5.7 8.1], 'BinWidth', 2);
        assert(e2(1) == 2);
        assert(abs(e2(2) - e2(1) - 2) < 1e-12);
        % Named limits are exact: they say where the histogram starts and stops.
        [n3, e3] = histcounts([1 2 2 3 3 3 4 4 4 4], 'BinLimits', [2 3]);
        assert(e3(1) == 2 && e3(end) == 3);
        assert(sum(n3) == 5);
        """);

    [Fact]
    public Task HistcountsCountsInWhateverUnitItWasAskedFor() => RunAsserting("""
        d = [1 2 2 3 3 3 4 4 4 4];
        [n, e] = histcounts(d, 4);
        width = e(2) - e(1);
        assert(abs(sum(histcounts(d, 4, 'Normalization', 'probability')) - 1) < 1e-12);
        assert(abs(sum(histcounts(d, 4, 'Normalization', 'pdf')) * width - 1) < 1e-12);
        cdf = histcounts(d, 4, 'Normalization', 'cdf');
        assert(abs(cdf(end) - 1) < 1e-12);
        cum = histcounts(d, 4, 'Normalization', 'cumcount');
        assert(cum(end) == numel(d));
        density = histcounts(d, 4, 'Normalization', 'countdensity');
        assert(abs(density(1) - n(1) / width) < 1e-12);
        """);

    [Fact]
    public Task HistcountsReportsWhichBinEachValueLandedIn() => RunAsserting("""
        [~, ~, b] = histcounts([0.5 1.5 2.5], [0 1 2 3]);
        assert(isequal(b, [1 2 3]));
        % Zero is the answer for a value outside every bin, which is why bins are numbered from one.
        [~, ~, b2] = histcounts([-5 1.5 99], [0 1 2 3]);
        assert(isequal(b2, [0 2 0]));
        """);

    [Fact]
    public Task HistcountsNamesTheOptionsItKnows() => RunAsserting("""
        caught = '';
        try
            histcounts([1 2 3], 'Normalisation', 'pdf');
        catch err
            caught = err.message;
        end
        assert(contains(caught, 'Normalization'));

        conflict = '';
        try
            histcounts([1 2 3], [0 1 2 3], 'BinWidth', 1);
        catch err
            conflict = err.message;
        end
        assert(contains(conflict, 'edges'));

        way = '';
        try
            histcounts([1 2 3], 'BinMethod', 'freedman');
        catch err
            way = err.message;
        end
        assert(contains(way, 'sturges'));
        """);

    // --- corrcoef and cov -------------------------------------------------------------------------

    [Fact]
    public Task CorrcoefMeasuresHowTwoSetsMoveTogether() => RunAsserting("""
        u = [1 2 3 4 5];
        assert(abs(corrcoef(u, 2 * u)(1, 2) - 1) < 1e-12);
        assert(abs(corrcoef(u, -2 * u)(1, 2) + 1) < 1e-12);
        % Sxy/sqrt(Sxx*Syy) = 19.7/sqrt(10*38.9).
        r = corrcoef(u, [2 4.1 5.9 8.2 9.8]);
        assert(abs(r(1, 2) - 19.7 / sqrt(10 * 38.9)) < 1e-12);
        assert(r(1, 1) == 1 && r(2, 2) == 1);
        assert(isequal(size(corrcoef([1 2; 2 4.1; 3 5.9; 4 8.2; 5 9.8])), [2 2]));
        """);

    [Fact]
    public Task CorrcoefReportsHowLikelyThatCorrelationWasByChance() => RunAsserting("""
        u = [1 2 3 4 5]';
        [r, p, rl, ru] = corrcoef(u, [2 4.1 5.9 8.2 9.8]');
        % A correlation this strong on five points is very unlikely from unrelated data.
        assert(p(1, 2) < 1e-3);
        assert(p(1, 1) == 1);
        % The interval brackets the estimate and stays inside the range a correlation can take.
        assert(rl(1, 2) < r(1, 2) && r(1, 2) < ru(1, 2));
        assert(rl(1, 2) >= -1 && ru(1, 2) <= 1);
        % A wider alpha is a narrower interval.
        [~, ~, wide] = corrcoef(u, [2 4.1 5.9 8.2 9.8]', 'Alpha', 0.5);
        assert(wide(1, 2) > rl(1, 2));
        """);

    [Fact]
    public Task CovIsVarianceOnItsOwnAndACovarianceMatrixOtherwise() => RunAsserting("""
        u = [1 2 3 4 5];
        assert(cov(u) == var(u));
        assert(cov(u, 1) == var(u, 1));
        c = cov(u, 2 * u);
        assert(isequal(size(c), [2 2]));
        assert(abs(c(1, 1) - 2.5) < 1e-12);
        assert(abs(c(1, 2) - 5) < 1e-12);
        % A single observation has no spread under either normalization.
        assert(cov(5) == 0);
        D = [1 2; 2 4.1; 3 5.9];
        assert(abs(cov(D)(2, 2) - 3.81) < 1e-12);
        assert(abs(cov(D)(1, 2) - cov(D)(2, 1)) < 1e-15);
        """);

    [Fact]
    public Task CovAndCorrcoefCanLeaveOutTheMissingReadings() => RunAsserting("""
        E = [1 2; NaN 4; 3 6];
        assert(isnan(cov(E)(1, 1)));
        assert(abs(cov(E, 'omitrows')(1, 1) - 2) < 1e-12);
        assert(abs(cov(E, 'partialrows')(2, 2) - 4) < 1e-12);
        assert(abs(corrcoef(E, 'Rows', 'complete')(1, 2) - 1) < 1e-12);

        caught = '';
        try
            cov([1 2; 3 4], 2);
        catch err
            caught = err.message;
        end
        assert(contains(caught, 'normalization'));
        """);

    // --- rms and bounds ---------------------------------------------------------------------------

    [Fact]
    public Task RmsWalksADimensionTheWayEveryOtherReductionDoes() => RunAsserting("""
        assert(abs(rms([3 4]) - sqrt(12.5)) < 1e-12);
        A = [1 2; 3 4];
        assert(abs(rms(A) - [sqrt(5) sqrt(10)]) < 1e-12);
        assert(abs(rms(A, 2) - [sqrt(2.5); sqrt(12.5)]) < 1e-12);
        assert(abs(rms(A, 'all') - sqrt(7.5)) < 1e-12);
        assert(isnan(rms([1 NaN 3])));
        assert(abs(rms([1 NaN 3], 'omitnan') - sqrt(5)) < 1e-12);
        """);

    [Fact]
    public Task BoundsAsksMinAndMaxTheSameQuestionAtOnce() => RunAsserting("""
        [lo, hi] = bounds([3 1 4 1 5]);
        assert(lo == 1 && hi == 5);
        [lo2, hi2] = bounds([1 2; 3 4]);
        assert(isequal(lo2, [1 2]) && isequal(hi2, [3 4]));
        [lo3, hi3] = bounds([1 2; 3 4], 2);
        assert(isequal(lo3, [1; 3]) && isequal(hi3, [2; 4]));
        [lo4, hi4] = bounds([1 2; 3 4], 'all');
        assert(lo4 == 1 && hi4 == 4);
        % A missing reading is left out by default, exactly as min and max leave it out.
        [lo5, hi5] = bounds([1 NaN 5]);
        assert(lo5 == 1 && hi5 == 5);
        """);
}
