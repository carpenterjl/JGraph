using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M53 wave B: the descriptive and robust statistics as the scripts call them — every argument form
/// the documentation lists, on a vector and on a matrix, plus the answers that follow from the
/// convention this mirror chose.
/// </summary>
[Collection("JG facade")]
public class MatlabDescriptiveStatisticsTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabDescriptiveStatisticsTests() => JG.Reset();

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
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success, "expected the call to be refused");
        return result.Message + _output.ErrorText;
    }

    [Fact]
    public Task PercentilesFollowTheMidpointConventionAndTakeADimension() => RunAsserting("""
        assert(abs(prctile([1 2 3 4], 25) - 1.5) < 1e-12);
        assert(isequal(prctile([1 2 3 4], [25 50 75]), [1.5 2.5 3.5]));

        % The probabilities come back in the shape they were asked in.
        assert(isequal(size(prctile([1 2 3 4], [25; 75])), [2 1]));

        A = [1 10; 2 20; 3 30; 4 40];
        assert(isequal(prctile(A, 50), [2.5 25]));
        assert(isequal(size(prctile(A, 50, 2)), [4 1]));
        assert(abs(prctile(A, 50, 'all') - 7) < 1e-12);

        % quantile is the same statistic in probabilities, and a whole number above one asks for that
        % many evenly spaced quantiles instead.
        assert(abs(quantile([1 2 3 4], 0.25) - 1.5) < 1e-12);
        assert(isequal(size(quantile([1 2 3 4], 3)), [1 3]));
        assert(abs(quantile([1 2 3 4], 1) - 4) < 1e-12);
        """);

    [Fact]
    public Task ShapeStatisticsTakeAFlagThenADimension() => RunAsserting("""
        assert(abs(skewness([1 2 3 4 5])) < 1e-12);
        assert(abs(kurtosis([1 2 3 4 5]) - 1.7) < 1e-12);

        x = [1 1 2 6];
        assert(skewness(x, 0) > skewness(x, 1));
        assert(abs(skewness(x, []) - skewness(x)) < 1e-12);

        A = [1 5; 2 6; 3 7; 100 8];
        assert(isequal(size(skewness(A)), [1 2]));
        assert(isequal(size(skewness(A, 1, 2)), [4 1]));
        assert(abs(kurtosis(A, 1, 'all') - kurtosis(A(:))) < 1e-12);

        % moment(x, 1) is zero by construction; moment(x, 2) is the variance over n.
        assert(abs(moment([1 2 3 4], 1)) < 1e-12);
        assert(abs(moment([1 2 3 4], 2) - 1.25) < 1e-12);
        assert(isequal(size(moment(A, 2, 2)), [4 1]));
        """);

    [Fact]
    public Task DeviationAndTrimmingTakeTheirRuleWords() => RunAsserting("""
        assert(abs(mad([1 2 3 4]) - 1) < 1e-12);
        assert(abs(mad([1 2 3 100], 1) - 1) < 1e-12);
        assert(mad([1 2 3 100], 0) > 30);

        A = [1 1; 2 2; 3 3; 4 100];
        assert(isequal(size(mad(A, 1)), [1 2]));
        assert(isequal(size(mad(A, 1, 2)), [4 1]));

        x = [1 2 3 4 100];
        assert(abs(trimmean(x, 40) - 3) < 1e-12);
        assert(abs(trimmean(x, 10) - 22) < 1e-12);
        assert(abs(trimmean(x, 10, 'floor') - 22) < 1e-12);
        assert(abs(trimmean(x, 10, 'weighted') - ((0.75 * 101) + 9) / 4.5) < 1e-12);
        assert(isequal(size(trimmean(A, 50, 'round', 2)), [4 1]));
        """);

    [Fact]
    public Task MeansAndScoresAnswerOnEitherDimension() => RunAsserting("""
        assert(abs(geomean([1 4]) - 2) < 1e-12);
        assert(abs(harmmean([1 4]) - 1.6) < 1e-12);
        assert(abs(geomean([1 NaN 4], 'omitnan') - 2) < 1e-12);
        assert(isnan(geomean([1 NaN 4])));

        A = [1 4; 4 16];
        assert(max(abs(geomean(A) - [2 8])) < 1e-12);
        assert(isequal(size(geomean(A, 2)), [2 1]));

        % range is the MATLAB statistic here, not the JGS sequence builder.
        assert(abs(range([3 1 7]) - 6) < 1e-12);
        assert(abs(range([3 NaN 1 7]) - 6) < 1e-12);
        assert(isequal(range([1 10; 3 40]), [2 30]));

        [z, mu, sigma] = zscore([1 2 3]);
        assert(isequal(z, [-1 0 1]));
        assert(abs(mu - 2) < 1e-12 && abs(sigma - 1) < 1e-12);
        assert(isequal(zscore([5 5 5]), [0 0 0]));

        B = [1 10; 2 20; 3 30];
        assert(isequal(size(zscore(B)), [3 2]));
        assert(isequal(size(zscore(B, 0, 2)), [3 2]));
        assert(abs(zscore([1 2 3], 1) * [1; 0; -1] - -2 * sqrt(1.5)) < 1e-9);
        """);

    [Fact]
    public Task RanksTablesAndGroupsAnswerTheirDocumentedOutputs() => RunAsserting("""
        [r, tieadj] = tiedrank([10 20 20 30]);
        assert(isequal(r, [1 2.5 2.5 4]));
        assert(abs(tieadj - 3) < 1e-12);

        [~, pairs] = tiedrank([10 20 20 30], 1);
        assert(abs(pairs - 1) < 1e-12);
        assert(isequal(tiedrank([1 2 3 4], 0, 1), [1 2 2 1]));

        t = tabulate([1 2 2 4]);
        assert(isequal(size(t), [4 3]));
        assert(isequal(t(:, 2)', [1 2 0 1]));
        assert(abs(t(2, 3) - 50) < 1e-12);

        [tbl, chi2, p, labels] = crosstab([1 1 2 2], [1 2 1 2]);
        assert(isequal(tbl, [1 1; 1 1]));
        assert(abs(chi2) < 1e-12 && abs(p - 1) < 1e-12);
        assert(isequal(size(labels), [2 2]));

        [m, sem, counts, names] = grpstats([1 2 3 4]', [1 1 2 2]');
        assert(isequal(m, [1.5; 3.5]));
        assert(isequal(counts, [2; 2]));
        assert(abs(sem(1) - 0.5) < 1e-12);
        assert(numel(names) == 2);
        """);

    [Fact]
    public Task CorrelationsAnswerInEveryDocumentedSense() => RunAsserting("""
        x = [1 2 3 4]';
        y = [1 4 9 16]';

        assert(abs(corr(x, y) - 25 / sqrt(645)) < 1e-12);

        % A monotone but curved relationship is perfect to Spearman and to Kendall, and only nearly
        % perfect to Pearson — which is the whole reason the three exist.
        assert(abs(corr(x, y, 'type', 'Spearman') - 1) < 1e-12);
        assert(abs(corr(x, y, 'type', 'Kendall') - 1) < 1e-12);

        [rho, pval] = corr([x y]);
        assert(isequal(size(rho), [2 2]));
        assert(abs(rho(1, 1) - 1) < 1e-12);
        assert(pval(1, 2) > 0 && pval(1, 2) < 1);

        % A one-sided test splits the two-sided probability when the correlation points its way.
        [~, both] = corr(x, y);
        [~, right] = corr(x, y, 'tail', 'right');
        assert(abs(right - both / 2) < 1e-12);

        % Missing observations: 'complete' drops the row everywhere, 'pairwise' only where it hurts.
        withGap = [1 2 3 4 NaN]';
        alsoGap = [1 4 9 16 25]';
        assert(isnan(corr(withGap, alsoGap)));
        assert(~isnan(corr(withGap, alsoGap, 'rows', 'complete')));
        assert(~isnan(corr(withGap, alsoGap, 'rows', 'pairwise')));
        """);

    /// <summary>
    /// Two variables built as a common trend plus equal and opposite noise correlate weakly and
    /// positively, because both follow the trend — and perfectly negatively once the trend is held
    /// fixed, because all that is left is the noise. The construction makes the partial correlation
    /// exactly −1, so this pins the residualization rather than merely its sign.
    /// </summary>
    [Fact]
    public Task PartialCorrelationsHoldTheControllingVariablesFixed() => RunAsserting("""
        z = [1 2 3 4]';
        x = [2 1 2 5]';
        y = [0 3 4 3]';

        assert(abs(corr(x, y) - 1 / 9) < 1e-12);
        assert(abs(partialcorr(x, y, z) - -1) < 1e-9);

        [rho, pval] = partialcorr([x y z]);
        assert(isequal(size(rho), [3 3]));
        assert(abs(rho(1, 1) - 1) < 1e-12);
        assert(abs(rho(1, 2) - -1) < 1e-9);
        assert(isequal(size(pval), [3 3]));

        % partialcorri holds the other predictors fixed rather than a named set.
        r = partialcorri(y, [x z]);
        assert(isequal(size(r), [1 2]));
        assert(abs(r(1) - -1) < 1e-9);

        assert(abs(partialcorr(x, y, z, 'type', 'Spearman')) <= 1);
        """);

    [Fact]
    public Task WholeMatrixOperationsConvertAndRepair() => RunAsserting("""
        [R, sigma] = corrcov([4 2; 2 9]);
        assert(isequal(sigma, [2; 3]));
        assert(abs(R(1, 2) - 1 / 3) < 1e-12);
        assert(abs(R(1, 1) - 1) < 1e-12);

        % Higham's example: symmetric with a unit diagonal, but not positive semidefinite.
        A = [1 1 0; 1 1 1; 0 1 1];
        C = nearcorr(A);
        assert(abs(C(1, 1) - 1) < 1e-12 && abs(C(3, 3) - 1) < 1e-12);
        assert(abs(C(1, 2) - C(2, 1)) < 1e-12);
        assert(abs(C(1, 2) - 0.7607) < 1e-3);
        assert(abs(C(1, 3) - 0.1573) < 1e-3);
        assert(min(eig(C)) > -1e-8);
        """);

    [Fact]
    public Task EmpiricalDistributionsStepUpAndSmoothOut() => RunAsserting("""
        [f, x] = ecdf([1 2 3]);
        assert(isequal(x, [1; 1; 2; 3]));
        assert(abs(f(1)) < 1e-12 && abs(f(4) - 1) < 1e-12);
        assert(abs(f(2) - 1/3) < 1e-12);

        % The survivor function is what is left over, and the bounds bracket the curve.
        s = ecdf([1 2 3], 'Function', 'survivor');
        assert(abs(s(1) - 1) < 1e-12 && abs(s(4)) < 1e-12);
        [~, ~, lo, up] = ecdf([1 2 3 4 5]);
        assert(all(lo <= up));

        % A censored observation withholds its event but still counts as having been at risk.
        fc = ecdf([1 2 3], 'Censoring', [0 1 0]);
        assert(fc(end) < 1 + 1e-12 && fc(end) > 0);

        [n, c] = ecdfhist(f, x, 2);
        assert(numel(n) == 2 && numel(c) == 2);
        assert(abs(sum(n .* diff([c(1) - (c(2) - c(1)) / 2, (c(1) + c(2)) / 2, c(2) + (c(2) - c(1)) / 2])) - 1) < 1e-9);
        """);

    [Fact]
    public Task SmoothedDensitiesTakeEveryKernelAndCurve() => RunAsserting("""
        data = [1 2 2 3 3 3 4 4 5];

        [d, xi] = ksdensity(data);
        assert(numel(d) == 100 && numel(xi) == 100);
        assert(all(d >= 0));

        % The density integrates to about one over the grid it was evaluated on.
        area = trapz(xi, d);
        assert(abs(area - 1) < 0.05);

        % Every documented kernel runs, and every one of them is a density.
        for k = {'normal', 'box', 'triangle', 'epanechnikov'}
            dk = ksdensity(data, xi, 'Kernel', k{1});
            assert(all(dk >= 0));
        end

        % The cumulative curve climbs from nothing to one, and the inverse walks back along it.
        cdf = ksdensity(data, xi, 'Function', 'cdf');
        assert(all(diff(cdf) >= -1e-12));
        assert(cdf(1) < 0.05 && cdf(end) > 0.95);
        assert(abs(ksdensity(data, 0.5, 'Function', 'icdf') - median(data)) < 1);

        % A narrower bandwidth makes a spikier estimate, and a bounded support keeps the mass inside.
        narrow = ksdensity(data, 3, 'Bandwidth', 0.1);
        wide = ksdensity(data, 3, 'Bandwidth', 2);
        assert(narrow > wide);
        assert(abs(ksdensity(data, -1, 'Support', 'positive')) < 1e-12);
        assert(numel(ksdensity(data, 'NumPoints', 30)) == 30);
        """);

    [Fact]
    public Task TheLegacyNanNamesAreTheOmitnanForms() => RunAsserting("""
        x = [1 NaN 3];
        assert(abs(nanmean(x) - 2) < 1e-12);
        assert(abs(nansum(x) - 4) < 1e-12);
        assert(abs(nanmax(x) - 3) < 1e-12);
        assert(abs(nanmin(x) - 1) < 1e-12);
        assert(abs(nanmedian(x) - 2) < 1e-12);
        assert(abs(nanstd(x) - sqrt(2)) < 1e-12);
        assert(abs(nanvar(x) - 2) < 1e-12);

        % They inherit the dimension handling of the names they forward to.
        A = [1 NaN; 3 4];
        assert(isequal(nanmean(A), [2 4]));
        assert(isequal(size(nanmean(A, 2)), [2 1]));

        C = nancov([1 2 3 NaN]', [2 4 6 1]');
        assert(abs(C(1, 2) - 2) < 1e-12);
        """);

    [Fact]
    public async Task MisspeltOptionsAreRefusedByName()
    {
        Assert.Contains("'type'", await RunExpectingFailure("corr([1 2 3]', [1 2 3]', 'typ', 'Pearson');"));
        Assert.Contains("Pearson", await RunExpectingFailure("corr([1 2 3]', [1 2 3]', 'type', 'Peason');"));
        Assert.Contains("'round'", await RunExpectingFailure("trimmean([1 2 3], 10, 'flooor');"));
        Assert.Contains("Kernel", await RunExpectingFailure("ksdensity([1 2 3], 'Kernel', 'gaussian');"));
        Assert.Contains("flag", await RunExpectingFailure("skewness([1 2 3], 2);"));
    }

    /// <summary>
    /// The one name where the two dialects answer the same call differently. JGS has meant
    /// <c>range(start, stop, step)</c> since M12 and its surface is frozen, so the statistic is
    /// registered in the MATLAB dialect only.
    /// </summary>
    [Fact]
    public void RangeKeepsItsJgsMeaningInJgs()
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add((0, figure)), null);
        ScriptRunResult result = JgsRunner.Run(
            "let r = range(0, 5);\nassert(length(r) == 5);\nassert(r[0] == 0);",
            context,
            default,
            sourceId: "",
            hook: null,
            JgsDialect.Jgs);

        Assert.True(result.Success, result.Message + _output.ErrorText);
    }
}
