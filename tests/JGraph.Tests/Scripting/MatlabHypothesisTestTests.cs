using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M53 wave F as the scripts call it: the hypothesis tests and the analysis of variance. The numerics
/// are pinned in <see cref="JGraph.Tests.Statistics.HypothesisTestTests"/>; what is tested here is the
/// scripting layer — which output comes first, what shape it has, what the stats structure is called,
/// and what a wrong argument says.
/// </summary>
[Collection("JG facade")]
public class MatlabHypothesisTestTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabHypothesisTestTests() => JG.Reset();

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

    // --- Tests of a mean -------------------------------------------------------------------------------

    /// <summary>
    /// The four outputs, in MathWorks' order, and the three fields the statistics structure carries for
    /// a one-sample t test.
    /// </summary>
    [Fact]
    public Task TheStudentTestAnswersFourThings() => RunAsserting(@"
        x = [5.1 4.9 6.2 5.7 5.5 6.0 5.3 5.8 6.1 5.4];
        [h, p, ci, st] = ttest(x, 5.5);
        assert(islogical(h) && h == 0);
        assert(abs(p - 0.485351) < 1e-5);
        assert(isequal(size(ci), [2 1]));
        assert(ci(1) < mean(x) && ci(2) > mean(x));
        assert(abs(st.tstat - (mean(x) - 5.5) / (std(x) / sqrt(10))) < 1e-12);
        assert(st.df == 9);
        assert(abs(st.sd - std(x)) < 1e-12);
    ");

    /// <summary>
    /// A tail word halves the probability and opens one end of the interval; the level moves the other
    /// end. Both are exercised twice, once each way round.
    /// </summary>
    [Fact]
    public Task TheTailAndTheLevelBothTakeEffect() => RunAsserting(@"
        x = [5.1 4.9 6.2 5.7 5.5 6.0 5.3 5.8 6.1 5.4];
        [~, both] = ttest(x, 5.0);
        [~, right, rci] = ttest(x, 5.0, 'Tail', 'right');
        [~, left, lci] = ttest(x, 5.0, 'Tail', 'left');
        assert(abs(right - both/2) < 1e-12);
        assert(abs(left - (1 - both/2)) < 1e-12);
        assert(isinf(rci(2)) && rci(2) > 0);
        assert(isinf(lci(1)) && lci(1) < 0);

        [~, ~, wide] = ttest(x, 5.5, 'Alpha', 0.01);
        [~, ~, narrow] = ttest(x, 5.5, 'Alpha', 0.10);
        assert(wide(2) - wide(1) > narrow(2) - narrow(1));
    ");

    /// <summary>
    /// <c>ttest(x, y)</c> with a vector second argument is the paired test; with a number it is the
    /// one-sample test. Telling them apart by the argument's shape is the whole rule.
    /// </summary>
    [Fact]
    public Task ASecondSampleMakesThePairedTest() => RunAsserting(@"
        x = [1 2 3 4 5 6];
        y = [1.1 2.3 2.8 4.4 5.2 5.7];
        [~, paired] = ttest(x, y);
        [~, direct] = ttest(x - y, 0);
        assert(abs(paired - direct) < 1e-14);

        [~, single] = ttest(x, 3.5);
        assert(abs(single - 1) < 1e-12);
    ");

    /// <summary>Welch's test differs from the pooled one when the spreads do, and reports both of them.</summary>
    [Fact]
    public Task WelchsTestReportsBothStandardDeviations() => RunAsserting(@"
        x = [5.1 4.9 6.2 5.7 5.5 6.0 5.3 5.8 6.1 5.4];
        y = [6.3 6.1 5.9 6.8 6.5 6.2 6.6 6.0];
        [h, p, ci, st] = ttest2(x, y);
        assert(h == 1);
        assert(abs(st.df - 16) < 1e-12);
        assert(isequal(size(st.sd), [1 1]));

        [h2, p2, ~, w] = ttest2(x, y, 'Vartype', 'unequal');
        assert(h2 == 1);
        assert(abs(w.df - 15.8624) < 1e-3);
        assert(numel(w.sd) == 2);
        assert(abs(w.sd(1) - std(x)) < 1e-12 && abs(w.sd(2) - std(y)) < 1e-12);
        assert(p2 ~= p);
    ");

    /// <summary>The z test names its statistic <c>zval</c> and uses the standard deviation it was given.</summary>
    [Fact]
    public Task TheZTestUsesTheDeviationItWasGiven() => RunAsserting(@"
        x = [5.1 4.9 6.2 5.7 5.5 6.0 5.3 5.8 6.1 5.4];
        [h, p, ci, st] = ztest(x, 5.5, 0.5);
        assert(abs(st.zval - (mean(x) - 5.5) / (0.5 / sqrt(10))) < 1e-12);
        assert(abs(p - 2 * normcdf(-abs(st.zval))) < 1e-12);
        assert(abs((ci(1) + ci(2))/2 - mean(x)) < 1e-12);
    ");

    /// <summary>
    /// A matrix is tested column by column, and every output takes the shape that implies — a row of
    /// decisions, a row of probabilities, and two rows of interval ends.
    /// </summary>
    [Fact]
    public Task AMatrixIsTestedColumnByColumn() => RunAsserting(@"
        M = [1 2; 3 5; 2 4; 4 3; 5 6];
        [h, p, ci, st] = ttest(M, 3);
        assert(isequal(size(h), [1 2]));
        assert(isequal(size(p), [1 2]));
        assert(isequal(size(ci), [2 2]));
        assert(isequal(size(st.tstat), [1 2]));
        assert(abs(p(1) - 1) < 1e-12);
        assert(abs(ci(1,1) - 1.0374) < 1e-3 && abs(ci(2,1) - 4.9626) < 1e-3);

        % Along the other dimension there are five one-row samples of two, so five answers come back.
        [h2, p2] = ttest(M, 3, 'Dim', 2);
        assert(isequal(size(h2), [5 1]));
        assert(numel(p2) == 5);
    ");

    // --- Tests of a variance -----------------------------------------------------------------------------

    /// <summary>The variance test's interval is for the variance, and the ratio test's for the ratio.</summary>
    [Fact]
    public Task TheVarianceTestsIntervalIsForWhatWasTested() => RunAsserting(@"
        x = [5.1 4.9 6.2 5.7 5.5 6.0 5.3 5.8 6.1 5.4];
        y = [6.3 6.1 5.9 6.8 6.5 6.2 6.6 6.0];
        [h, p, ci, st] = vartest(x, 0.25);
        assert(abs(st.chisqstat - 9 * var(x) / 0.25) < 1e-12);
        assert(st.df == 9);
        assert(ci(1) < var(x) && ci(2) > var(x));

        [h2, p2, ci2, st2] = vartest2(x, y);
        assert(abs(st2.fstat - var(x)/var(y)) < 1e-12);
        assert(st2.df1 == 9 && st2.df2 == 7);
        assert(ci2(1) < 1 && ci2(2) > 1);

        [~, ~, ~, back] = vartest2(y, x);
        assert(abs(back.fstat - 1/st2.fstat) < 1e-12);
    ");

    /// <summary>
    /// <c>vartestn</c> leads with the probability and never reports a decision, and each of its five
    /// test types answers a different structure.
    /// </summary>
    [Fact]
    public Task TheSeveralVarianceTestTakesFiveTestTypes() => RunAsserting(@"
        v = [4 5 6 7 8 9 1 2 9];
        g = [1 1 1 2 2 2 3 3 3];
        [p, st] = vartestn(v, g);
        assert(abs(st.df - 2) < 1e-12);
        assert(isfield(st, 'chisqstat'));

        for name = {'LeveneQuadratic', 'LeveneAbsolute', 'BrownForsythe', 'OBrien'}
            [pk, sk] = vartestn(v, g, 'TestType', name{1});
            assert(isfield(sk, 'fstat'));
            assert(isequal(size(sk.df), [1 2]));
            assert(pk >= 0 && pk <= 1);
        end

        % A matrix without a grouping variable compares its own columns.
        X = [1 10; 2 20; 3 30; 4 40];
        pm = vartestn(X, 'Display', 'off');
        assert(pm >= 0 && pm <= 1);
    ");

    // --- Distributional tests ------------------------------------------------------------------------------

    /// <summary>
    /// The Kolmogorov–Smirnov test against the standard normal by default, against a named function,
    /// and against a two-column table — all three ways of saying which distribution.
    /// </summary>
    [Fact]
    public Task TheKolmogorovTestTakesThreeKindsOfDistribution() => RunAsserting(@"
        z = norminv((1:20)/21);
        [h, p, ks, cv] = kstest(z);
        assert(h == 0);
        assert(abs(ks - 1/21) < 1e-12);
        assert(cv > ks);

        [h2, p2, ks2] = kstest(z, 'CDF', @(t) normcdf(t));
        assert(abs(ks2 - ks) < 1e-12);

        grid = (-4:0.01:4)';
        [h3, ~, ks3] = kstest(z, 'CDF', [grid normcdf(grid)]);
        assert(abs(ks3 - ks) < 1e-3);

        [~, pl] = kstest(z, 'Tail', 'larger');
        [~, ps] = kstest(z, 'Tail', 'smaller');
        assert(pl < 1 && ps < 1);
    ");

    /// <summary>Two samples that never overlap have a statistic of one and are rejected.</summary>
    [Fact]
    public Task TheTwoSampleKolmogorovTestSeparatesDisjointSamples() => RunAsserting(@"
        [h, p, ks] = kstest2([1 2 3 4 5], [6 7 8 9 10]);
        assert(h == 1 && abs(ks - 1) < 1e-12 && p < 0.01);

        [h2, p2, ks2] = kstest2([1 2 3 4 5], [1 2 3 4 5]);
        assert(h2 == 0 && ks2 == 0 && abs(p2 - 1) < 1e-12);

        [~, pa] = kstest2([1 2 3], [2 3 4], 'Alpha', 0.2, 'Tail', 'larger');
        assert(pa >= 0 && pa <= 1);
    ");

    /// <summary>
    /// The two composite tests take their family under either of the two names MathWorks spells it, and
    /// the Anderson–Darling one takes two more families than Lilliefors' does.
    /// </summary>
    [Fact]
    public Task TheCompositeFitTestsTakeTheirFamilies() => RunAsserting(@"
        z = norminv((1:30)/31);
        [h, p, st, cv] = lillietest(z);
        assert(h == 0 && st < cv);
        [~, pd] = lillietest(z, 'Distr', 'norm');
        assert(abs(pd - p) < 1e-12);

        e = expinv((1:30)/31, 2);
        [he, pe] = lillietest(e, 'Distr', 'exp');
        assert(he == 0 && pe > 0.05);

        [ha, pa, sa, ca] = adtest(z);
        assert(ha == 0 && sa < ca);
        [~, pn] = adtest(z, 'Distribution', 'norm');
        assert(abs(pn - pa) < 1e-12);

        w = exp(z);
        [~, pw] = adtest(w, 'Distribution', 'logn');
        assert(abs(pw - pa) < 1e-12);
    ");

    /// <summary>The skewness-and-kurtosis test, and its level as a bare second argument.</summary>
    [Fact]
    public Task TheSkewnessKurtosisTestTakesItsLevelPositionally() => RunAsserting(@"
        x = [-2 -1 -1 0 0 0 1 1 2];
        [h, p, st, cv] = jbtest(x);
        assert(h == 0);
        assert(abs(cv - chi2inv(0.95, 2)) < 1e-9);
        [~, ~, ~, strict] = jbtest(x, 0.01);
        assert(strict > cv);
    ");

    /// <summary>
    /// The binned test bins the data itself, accepts bin counts, centres or edges, and reports what it
    /// binned.
    /// </summary>
    [Fact]
    public Task TheBinnedTestReportsWhatItBinned() => RunAsserting(@"
        rng(3);
        d = normrnd(0, 1, 1, 400);
        [h, p, st] = chi2gof(d);
        assert(h == 0);
        assert(numel(st.O) == numel(st.E));
        assert(numel(st.edges) == numel(st.O) + 1);
        assert(abs(sum(st.O) - 400) < 1e-9);
        assert(abs(sum(st.E) - 400) < 1e-6);
        assert(st.df == numel(st.O) - 3);

        [~, ~, few] = chi2gof(d, 'NBins', 6);
        assert(numel(few.O) <= 6);

        [~, ~, edged] = chi2gof(d, 'Edges', [-Inf -2 -1 0 1 2 Inf]);
        assert(numel(edged.O) <= 6);

        [~, ~, own] = chi2gof(d, 'CDF', @(t) normcdf(t, 0, 1), 'NParams', 0);
        assert(own.df == numel(own.O) - 1);
    ");

    /// <summary>
    /// Runs about the median, about a stated reference, and up and down — the three ways
    /// <c>runstest</c> reads its second argument.
    /// </summary>
    [Fact]
    public Task TheRunTestTakesAReferenceOrTheWordUpDown() => RunAsserting(@"
        [h, p, st] = runstest([1 2 3 4 5 6 7 8 9 10]);
        assert(h == 1);
        assert(st.nruns == 2 && st.n1 == 5 && st.n0 == 5);
        assert(abs(p - 2 * 2 / nchoosek(10, 5)) < 1e-12);

        [~, pv, sv] = runstest([1 2 3 4 5 6 7 8 9 10], 5.5);
        assert(sv.nruns == 2 && abs(pv - p) < 1e-12);

        [~, pud, sud] = runstest([1 5 2 6 3 7 4 8 5 9], 'ud');
        assert(sud.nruns > 5);
        assert(pud >= 0 && pud <= 1);

        [~, pa] = runstest([1 2 3 4 5 6 7 8 9 10], [], 'Method', 'approximate');
        assert(pa >= 0 && pa <= 1);
    ");

    // --- Rank tests ------------------------------------------------------------------------------------------

    /// <summary>
    /// The three rank tests of location lead with the probability, which is the opposite of every
    /// parametric test here — and is what MathWorks documents.
    /// </summary>
    [Fact]
    public Task TheRankTestsLeadWithTheProbability() => RunAsserting(@"
        [p, h, st] = ranksum([1 3 5 7 9], [2 4 6 8 10]);
        assert(p > 0.5 && h == 0);
        assert(st.ranksum == 25);

        [pa, ~, sa] = ranksum([1 3 5 7 9], [2 4 6 8 10], 'method', 'approximate');
        assert(isfield(sa, 'zval'));
        assert(abs(pa - p) < 0.05);

        [ps, hs, ss] = signrank([1 3 5 7 9], [2 4 6 8 12]);
        assert(isfield(ss, 'signedrank'));
        assert(ps > 0 && ps < 1);

        [pt, ht, stt] = signtest([1 3 5 7 9], [2 4 6 8 12]);
        assert(abs(pt - 2^-4) < 1e-12);
        assert(stt.sign == 0);

        % A single number stands for a sample of that value repeated.
        [pm, ~] = signtest([1 3 5 7 9], 4);
        assert(pm > 0 && pm <= 1);
    ");

    /// <summary>
    /// The dispersion test leads with the decision, and its alternative reads the opposite tail from the
    /// one the statistic's sign suggests — the tight sample collects the large scores.
    /// </summary>
    [Fact]
    public Task TheDispersionTestLeadsWithTheDecision() => RunAsserting(@"
        tight = [4.9 5.0 5.1 5.0 4.95 5.05];
        loose = [1 3 5 7 9 11];
        [h, p, st] = ansaribradley(tight, loose);
        assert(islogical(h));
        assert(isfield(st, 'W') && isfield(st, 'Wstar'));
        assert(st.Wstar > 0);

        [~, bigger] = ansaribradley(tight, loose, 'tail', 'right');
        [~, smaller] = ansaribradley(tight, loose, 'tail', 'left');
        assert(smaller < bigger);
        assert(abs(bigger + smaller - 1) < 1e-9);
    ");

    // --- Tests about a model ---------------------------------------------------------------------------------

    /// <summary>Fisher's exact test of the tea-tasting table, with its odds ratio and interval.</summary>
    [Fact]
    public Task FishersExactTestAnswersTheTeaTastingTable() => RunAsserting(@"
        [h, p, st] = fishertest([3 1; 1 3]);
        assert(h == 0);
        assert(abs(p - 34/70) < 1e-12);
        assert(abs(st.OddsRatio - 9) < 1e-12);
        assert(numel(st.ConfidenceInterval) == 2);
        assert(st.ConfidenceInterval(1) < 1 && st.ConfidenceInterval(2) > 1);

        [~, pr] = fishertest([3 1; 1 3], 'Tail', 'right');
        assert(abs(pr - 17/70) < 1e-12);
        [hs, ps] = fishertest([8 0; 0 8], 'Alpha', 0.01);
        assert(hs == 1 && ps < 0.01);
    ");

    /// <summary>Bartlett's dimensionality test counts the directions the data really uses.</summary>
    [Fact]
    public Task TheDimensionalityTestCountsRealDirections() => RunAsserting(@"
        rng(11);
        X = [randn(60,1), randn(60,1), randn(60,1)];
        [nd, prob, chi] = barttest(X);
        assert(nd == 0);
        assert(numel(prob) == 2 && numel(chi) == 2);

        % One real direction plus the same small amount of noise in each variable: the leftover
        % variance is then spherical, which is exactly what the test asks about.
        driver = randn(60,1);
        C = [driver + 0.02*randn(60,1), driver + 0.02*randn(60,1), driver + 0.02*randn(60,1)];
        assert(barttest(C, 0.05) == 1);
    ");

    /// <summary>
    /// The Durbin–Watson test on a strongly drifting residual, both ways of computing it, and both ends
    /// of the statistic's range.
    /// </summary>
    [Fact]
    public Task TheSerialCorrelationTestFindsADrift() => RunAsserting(@"
        n = 24;
        Xd = ones(n,1);
        drift = ((1:n)' - 12.5);
        [p, d] = dwtest(drift, Xd);
        assert(d < 0.1 && p < 1e-6);

        [pa, da] = dwtest(drift, Xd, 'Method', 'approximate');
        assert(abs(da - d) < 1e-12);
        assert(pa < 0.01);

        alternating = (-1).^(1:n)';
        [pu, du] = dwtest(alternating, Xd, 'Tail', 'left');
        assert(du > 3.8 && pu < 1e-6);
    ");

    /// <summary>
    /// A linear hypothesis is divided by the rank of its restrictions, so writing the same restriction
    /// three times does not treble the degrees of freedom.
    /// </summary>
    [Fact]
    public Task ALinearHypothesisCountsIndependentRestrictions() => RunAsserting(@"
        [p, F, r] = linhyptest([1; 2], eye(2), [0; 0], eye(2), 10);
        assert(r == 2);
        assert(abs(F - 2.5) < 1e-12);
        assert(abs(p - (1 - fcdf(2.5, 2, 10))) < 1e-12);

        [p2, F2, r2] = linhyptest([1; 2], eye(2), [0; 0; 0], [1 0; 1 0; 1 0], 10);
        assert(r2 == 1 && abs(F2 - 1) < 1e-12);

        % No residual degrees of freedom makes it a chi-square test of the same statistic.
        [p3, F3, r3] = linhyptest([1; 2], eye(2));
        assert(r3 == 2 && abs(p3 - (1 - chi2cdf(5, 2))) < 1e-12);
    ");

    /// <summary>
    /// The sample-size solver answers whichever of the three quantities was left out, and the
    /// two-sample form answers both sample sizes.
    /// </summary>
    [Fact]
    public Task TheSampleSizeSolverAnswersTheMissingQuantity() => RunAsserting(@"
        n = sampsizepwr('t', [100 10], 110, 0.80);
        assert(n == 10);
        pw = sampsizepwr('t', [100 10], 110, [], n);
        assert(pw >= 0.80);
        below = sampsizepwr('t', [100 10], 110, [], n - 1);
        assert(below < 0.80);

        [n1, n2] = sampsizepwr('t2', [100 10], 110, 0.80, [], 'Ratio', 2);
        assert(n2 == 2 * n1);

        nz = sampsizepwr('z', [0 1], 0.5, 0.9);
        nr = sampsizepwr('z', [0 1], 0.5, 0.9, [], 'Tail', 'right');
        assert(nr < nz);

        nv = sampsizepwr('var', 1, 2, 0.8, [], 'Tail', 'right');
        assert(nv > 2);

        alt = sampsizepwr('p', 0.30, [], 0.80, 100);
        assert(alt > 0.30 && alt < 1);
    ");

    // --- Analysis of variance --------------------------------------------------------------------------------

    /// <summary>
    /// The one-way analysis over a matrix's columns: the probability, the table a report would print,
    /// and the structure the comparison works from.
    /// </summary>
    [Fact]
    public Task TheOneWayAnalysisAnswersATableAndAStructure() => RunAsserting(@"
        X = [23 27 31; 25 29 33; 22 26 30; 24 28 32];
        [p, tbl, st] = anova1(X);
        assert(isequal(size(tbl), [4 6]));
        assert(strcmp(tbl{1,1}, 'Source') && strcmp(tbl{2,1}, 'Groups'));
        assert(abs(tbl{2,2} - 128) < 1e-12);
        assert(tbl{2,3} == 2 && tbl{3,3} == 9);
        assert(abs(tbl{2,5} - 38.4) < 1e-10);
        assert(abs(tbl{4,2} - (tbl{2,2} + tbl{3,2})) < 1e-10);
        assert(abs(p - tbl{2,6}) < 1e-15);

        assert(strcmp(st.source, 'anova1'));
        assert(isequal(size(st.means), [1 3]));
        assert(abs(st.means(1) - 23.5) < 1e-12);
        assert(abs(st.s - sqrt(tbl{3,4})) < 1e-12);
        assert(numel(st.gnames) == 3);
    ");

    /// <summary>
    /// A grouping variable cuts a vector into groups, and its labels come back in the structure. The
    /// probability here is exactly a thousandth, which the F distribution with 2 and 6 degrees of
    /// freedom gives in closed form.
    /// </summary>
    [Fact]
    public Task AGroupingVariableCutsAVectorIntoGroups() => RunAsserting(@"
        v = [1 2 3 4 5 6 7 8 9];
        g = {'a','a','a','b','b','b','c','c','c'};
        [p, tbl, st] = anova1(v, g);
        assert(abs(p - 0.001) < 1e-12);
        assert(strcmp(st.gnames{1}, 'a') && strcmp(st.gnames{3}, 'c'));
        assert(isequal(st.n, [3 3 3]));

        % Numeric groups sort ascending and are named by their own values.
        [~, ~, sn] = anova1(v, [3 3 3 1 1 1 2 2 2]);
        assert(strcmp(sn.gnames{1}, '1'));
        assert(abs(sn.means(1) - 5) < 1e-12);
    ");

    /// <summary>
    /// The two-way analysis reports its probabilities in MathWorks' order — columns, rows, interaction —
    /// and drops the interaction line when the grid is not replicated.
    /// </summary>
    [Fact]
    public Task TheTwoWayAnalysisOrdersItsProbabilities() => RunAsserting(@"
        Y = [1 2; 3 4; 5 7; 7 9];
        [p, tbl, st] = anova2(Y, 2);
        assert(numel(p) == 3);
        assert(isequal(size(tbl), [6 6]));
        assert(strcmp(tbl{2,1}, 'Columns') && strcmp(tbl{3,1}, 'Rows'));
        assert(strcmp(tbl{4,1}, 'Interaction'));
        assert(strcmp(st.source, 'anova2'));
        assert(numel(st.colmeans) == 2 && numel(st.rowmeans) == 2);
        assert(st.inter == 1);

        [p1, t1] = anova2(Y);
        assert(numel(p1) == 2);
        assert(isequal(size(t1), [5 6]));
        assert(strcmp(t1{4,1}, 'Error'));
    ");

    /// <summary>
    /// The general analysis with two crossed factors: a balanced design makes the three sums of squares
    /// agree, which is the check that the model is being fitted rather than guessed.
    /// </summary>
    [Fact]
    public Task TheGeneralAnalysisFitsCrossedFactors() => RunAsserting(@"
        y = [52 48 60 55 71 68 74 70]';
        ga = [1 1 1 1 2 2 2 2]';
        gb = [1 1 2 2 1 1 2 2]';
        [p, tbl, st] = anovan(y, {ga, gb}, 'model', 'interaction', 'varnames', {'A','B'});
        assert(numel(p) == 3);
        assert(isequal(size(tbl), [6 6]));
        assert(strcmp(tbl{2,1}, 'A') && strcmp(tbl{4,1}, 'A*B'));
        assert(abs(tbl{2,2} - 578) < 1e-10);
        assert(st.dfe == 4);
        assert(abs(st.mse - 8.25) < 1e-10);
        assert(numel(st.means) == 2 && numel(st.n) == 2);

        % A balanced design gives the same answer whichever order the terms are credited in.
        p1 = anovan(y, {ga, gb}, 'model', 'interaction', 'sstype', 1);
        p2 = anovan(y, {ga, gb}, 'model', 'interaction', 'sstype', 2);
        assert(max(abs(p1 - p)) < 1e-12);
        assert(max(abs(p2 - p)) < 1e-12);

        % A term matrix names the same model as the word does.
        pt = anovan(y, {ga, gb}, 'model', [1 0; 0 1; 1 1]);
        assert(max(abs(pt - p)) < 1e-12);
        pf = anovan(y, {ga, gb}, 'model', 'full');
        assert(max(abs(pf - p)) < 1e-12);
    ");

    /// <summary>
    /// The three sums of squares differ once the design is unbalanced, which is the whole reason there
    /// are three of them.
    /// </summary>
    [Fact]
    public Task TheThreeSumsOfSquaresDifferOnAnUnbalancedDesign() => RunAsserting(@"
        % Cell counts of 3, 1, 1 and 3: not proportional, so the two factors are not orthogonal and
        % what each explains depends on which is credited first.
        y  = [5 7 6 9 8 11 10 14]';
        ga = [1 1 1 1 2 2 2 2]';
        gb = [1 1 1 2 1 2 2 2]';
        p1 = anovan(y, {ga, gb}, 'sstype', 1);
        p2 = anovan(y, {ga, gb}, 'sstype', 2);
        p3 = anovan(y, {ga, gb}, 'sstype', 3);
        assert(abs(p1(1) - p3(1)) > 1e-3);
        assert(abs(p2(1) - p3(1)) < 1e-12);
        assert(abs(p1(2) - p3(2)) < 1e-12);
    ");

    /// <summary>The rank-based analyses answer the same three things, over ranks instead of values.</summary>
    [Fact]
    public Task TheRankAnalysesAnswerRankTables() => RunAsserting(@"
        X = [23 27 31; 25 29 33; 22 26 30; 24 28 32];
        [p, tbl, st] = kruskalwallis(X);
        assert(abs(tbl{2,5} - 9.84615384615385) < 1e-9);
        assert(strcmp(st.source, 'kruskalwallis'));
        assert(isequal(st.meanranks, [2.5 6.5 10.5]));
        assert(st.sumt == 0);
        assert(abs(p - (1 - chi2cdf(tbl{2,5}, 2))) < 1e-12);

        F = [1 2 3; 2 3 1; 3 1 2; 1 3 2];
        [pf, tf, sf] = friedman(F);
        assert(strcmp(sf.source, 'friedman'));
        assert(numel(sf.meanranks) == 3);
        assert(abs(pf - (1 - chi2cdf(tf{2,5}, 2))) < 1e-12);

        % Replicated blocks rank all the observations of a block together.
        R = [1 2; 2 1; 3 4; 4 3];
        pr = friedman(R, 2);
        assert(pr >= 0 && pr <= 1);
    ");

    /// <summary>
    /// The multivariate analysis answers a dimension, and two well-separated clouds need exactly one
    /// direction to tell apart.
    /// </summary>
    [Fact]
    public Task TheMultivariateAnalysisAnswersADimension() => RunAsserting(@"
        Z = [1 2; 1.2 2.1; 0.9 1.8; 5 6; 5.2 6.1; 4.8 5.9];
        gz = [1 1 1 2 2 2];
        [d, p, st] = manova1(Z, gz);
        assert(d == 1);
        assert(numel(p) == 1);
        assert(p(1) < 0.01);
        assert(isequal(size(st.W), [2 2]) && isequal(size(st.B), [2 2]));
        assert(max(max(abs(st.T - (st.W + st.B)))) < 1e-9);
        assert(st.dfW == 4 && st.dfB == 1);
        assert(numel(st.mdist) == 6);
        assert(isequal(size(st.gmdist), [2 2]));
        assert(abs(st.gmdist(1,1)) < 1e-9);
    ");

    // --- Multiple comparison -----------------------------------------------------------------------------------

    /// <summary>
    /// The comparison table is six columns wide — the two groups, the interval, the difference and the
    /// probability — and its five corrections order themselves by how eagerly they reject.
    /// </summary>
    [Fact]
    public Task TheComparisonTableCarriesEveryPair() => RunAsserting(@"
        X = [23 27 31; 25 29 33; 22 26 30; 24 28 32];
        [~, ~, st] = anova1(X);
        [c, m, h, gnames] = multcompare(st);
        assert(isequal(size(c), [3 6]));
        assert(isequal(size(m), [3 2]));
        assert(isempty(h));
        assert(numel(gnames) == 3);
        assert(c(1,1) == 1 && c(1,2) == 2);
        assert(abs(c(1,4) - (st.means(1) - st.means(2))) < 1e-12);
        assert(abs((c(1,3) + c(1,5))/2 - c(1,4)) < 1e-10);
        assert(abs(m(1,1) - st.means(1)) < 1e-12);

        lsd = multcompare(st, 'CType', 'lsd');
        tukey = multcompare(st, 'CType', 'tukey-kramer');
        hsd = multcompare(st, 'CType', 'hsd');
        sidak = multcompare(st, 'CType', 'dunn-sidak');
        bonf = multcompare(st, 'CType', 'bonferroni');
        scheffe = multcompare(st, 'CType', 'scheffe');
        assert(abs(hsd(1,6) - tukey(1,6)) < 1e-12);
        assert(lsd(1,6) < tukey(1,6));
        assert(tukey(1,6) < sidak(1,6));
        assert(sidak(1,6) <= bonf(1,6) + 1e-12);
        assert(bonf(1,6) < scheffe(1,6));

        wide = multcompare(st, 'Alpha', 0.01);
        assert(wide(1,5) - wide(1,3) > c(1,5) - c(1,3));
    ");

    /// <summary>Every analysis that produces a structure can be handed to the comparison.</summary>
    [Fact]
    public Task EveryAnalysisFeedsTheComparison() => RunAsserting(@"
        X = [23 27 31; 25 29 33; 22 26 30; 24 28 32];
        [~, ~, kw] = kruskalwallis(X);
        ck = multcompare(kw);
        assert(isequal(size(ck), [3 6]));

        F = [1 2 3; 2 3 1; 3 1 2; 1 3 2];
        [~, ~, fr] = friedman(F);
        cf = multcompare(fr);
        assert(isequal(size(cf), [3 6]));

        Y = [1 2; 3 4; 5 7; 7 9];
        [~, ~, a2] = anova2(Y, 2);
        cc = multcompare(a2, 'Estimate', 'column');
        cr = multcompare(a2, 'Estimate', 'row');
        assert(isequal(size(cc), [1 6]) && isequal(size(cr), [1 6]));

        y = [52 48 60 55 71 68 74 70]';
        [~, ~, an] = anovan(y, {[1 1 1 1 2 2 2 2]', [1 1 2 2 1 1 2 2]'});
        c1 = multcompare(an, 'Dimension', 1);
        c2 = multcompare(an, 'Dimension', 2);
        assert(isequal(size(c1), [1 6]) && isequal(size(c2), [1 6]));
        assert(abs(c1(1,4) - (an.means{1}(1) - an.means{1}(2))) < 1e-10);
    ");

    // --- What is refused ------------------------------------------------------------------------------------------

    /// <summary>
    /// Every option word that is not a real one names the alternatives, and every argument that cannot
    /// mean what it would have to says which one it is.
    /// </summary>
    [Fact]
    public async Task WrongArgumentsAreRefusedByName()
    {
        Assert.Contains("Tail", await RunExpectingFailure("ttest([1 2 3], 0, 'Tail', 'sideways');"),
            StringComparison.Ordinal);
        Assert.Contains("unknown option", await RunExpectingFailure("ttest([1 2 3], 0, 'Tale', 'both');"),
            StringComparison.Ordinal);
        Assert.Contains("Vartype", await RunExpectingFailure("ttest2([1 2 3], [4 5 6], 'Vartype', 'pooled');"),
            StringComparison.Ordinal);
        Assert.Contains("two observations", await RunExpectingFailure("ttest(5, 0);"), StringComparison.Ordinal);
        Assert.Contains("same size", await RunExpectingFailure("ttest([1 2 3], [1 2]);"), StringComparison.Ordinal);
        Assert.Contains("TestType", await RunExpectingFailure("vartestn([1 2; 3 4], 'TestType', 'Levene');"),
            StringComparison.Ordinal);
        Assert.Contains("two-by-two", await RunExpectingFailure("fishertest([1 2 3; 4 5 6]);"),
            StringComparison.Ordinal);
        Assert.Contains("whole counts", await RunExpectingFailure("fishertest([1.5 2; 3 4]);"),
            StringComparison.Ordinal);
        Assert.Contains("ties", await RunExpectingFailure("ranksum([1 2 2], [3 4], 'method', 'exact');"),
            StringComparison.Ordinal);
        Assert.Contains("sstype", await RunExpectingFailure("anovan([1 2 3 4]', {[1 1 2 2]'}, 'sstype', 4);"),
            StringComparison.Ordinal);
        Assert.Contains("does not know the test", await RunExpectingFailure("sampsizepwr('q', 1, 2, 0.8);"),
            StringComparison.Ordinal);
        Assert.Contains("does not compare", await RunExpectingFailure("multcompare(struct('source', 'nothing'));"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The three arguments that ask for something this build does not do are refused with the reason
    /// rather than accepted and ignored, because a silently ignored tolerance is how a script quietly
    /// gets a different answer than it asked for.
    /// </summary>
    [Fact]
    public async Task WhatIsNotComputedIsRefusedWithItsReason()
    {
        Assert.Contains("simulat", await RunExpectingFailure("lillietest([1 2 3 4 5], 'MCTol', 0.01);"),
            StringComparison.Ordinal);
        Assert.Contains("simulat", await RunExpectingFailure("jbtest([1 2 3 4 5], 0.05, 0.01);"),
            StringComparison.Ordinal);
        Assert.Contains("crossed, fixed and categorical",
            await RunExpectingFailure("anovan([1 2 3 4]', {[1 1 2 2]'}, 'random', 1);"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole surface is registered in the JGS dialect too, and answers the same numbers there —
    /// only the indexing differs, and none of these outputs is an index.
    /// </summary>
    [Fact]
    public void TheTestsAnswerTheSameNumbersInBothDialects()
    {
        ScriptRunResult result = JgsRunner.Run(
            @"
            let x = [5.1, 4.9, 6.2, 5.7, 5.5, 6.0, 5.3, 5.8, 6.1, 5.4]
            let p = ttest(x, 5.5)
            assert(abs(p - 0) < 1e-12)
            let q = ranksum([1, 3, 5, 7, 9], [2, 4, 6, 8, 10])
            assert(q > 0.5)
            let a = anova1([[23, 27, 31], [25, 29, 33], [22, 26, 30], [24, 28, 32]])
            assert(a < 1e-4)
            ",
            new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))),
            default,
            sourceId: "",
            hook: null,
            JgsDialect.Jgs);

        Assert.True(result.Success, result.Message + _output.ErrorText);
    }
}
