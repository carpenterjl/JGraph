using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The distribution objects as a script sees them (M53 wave I): what <c>makedist</c> and
/// <c>fitdist</c> build, what the object publishes, what the nine shared names do when handed one,
/// and what each refusal says.
/// </summary>
/// <remarks>
/// Two things are worth pinning beyond the arithmetic. The first is that the nine shared names still
/// do their old job on everything that is not a distribution — the object check stands in front of
/// them rather than replacing them, and a test that only exercised objects would not notice if it had
/// replaced them. The second is that these are value objects: truncating a copy must leave the
/// original where it was.
/// </remarks>
[Collection("JG facade")]
public class MatlabDistributionObjectTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabDistributionObjectTests() => JG.Reset();

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
        Assert.False(result.Success, "the call was expected to be refused.");
        return result.Message + _output.ErrorText;
    }

    /// <summary>Two hundred draws from a known normal, seeded so the fit is the same every run.</summary>
    private const string Fitted = """
        rng(11);
        x = normrnd(5, 2, 200, 1);
        pd = fitdist(x, 'Normal');
        """;

    // --- makedist ---------------------------------------------------------------------------------

    [Fact]
    public Task Makedist_BuildsAnObjectThatPublishesItsParameters() => RunAsserting("""
        pd = makedist('Normal', 'mu', 10, 'sigma', 2);
        assert(strcmp(class(pd), 'prob.NormalDistribution'));
        assert(strcmp(pd.DistributionName, 'Normal'));
        assert(pd.mu == 10 && pd.sigma == 2);
        assert(pd.NumParameters == 2);
        assert(strcmp(pd.ParameterNames{1}, 'mu'));
        assert(strcmp(pd.ParameterNames{2}, 'sigma'));
        assert(strcmp(pd.ParameterDescription{1}, 'Mean'));
        assert(isequal(pd.ParameterValues, [10 2]));
        assert(pd.IsTruncated == 0);
        """);

    [Fact]
    public Task Makedist_FillsInTheDocumentedDefaults() => RunAsserting("""
        assert(makedist('Normal').mu == 0 && makedist('Normal').sigma == 1);
        assert(makedist('Exponential').mu == 1);
        assert(makedist('Poisson').lambda == 1);
        assert(makedist('Uniform').Lower == 0 && makedist('Uniform').Upper == 1);
        assert(makedist('tLocationScale').nu == 5);
        assert(makedist('Stable').alpha == 2);
        """);

    [Fact]
    public Task Makedist_OnItsOwnListsTheNamesItKnows() => RunAsserting("""
        names = makedist;
        assert(iscell(names));
        assert(numel(names) == 28);
        assert(any(strcmp(names, 'Weibull')));
        assert(any(strcmp(names, 'BirnbaumSaunders')));
        """);

    [Theory]
    [InlineData("Beta", "a", 2, "b", 5)]
    [InlineData("Gamma", "a", 3, "b", 2)]
    [InlineData("Weibull", "A", 2, "B", 1.5)]
    [InlineData("Rician", "s", 2, "sigma", 1)]
    [InlineData("Nakagami", "mu", 1.5, "omega", 2)]
    [InlineData("InverseGaussian", "mu", 1, "lambda", 4)]
    [InlineData("Logistic", "mu", 1, "sigma", 2)]
    [InlineData("BirnbaumSaunders", "beta", 2, "gamma", 0.5)]
    [InlineData("HalfNormal", "mu", 0, "sigma", 3)]
    [InlineData("Loguniform", "Lower", 1, "Upper", 100)]
    public Task EveryFamilyInvertsItsOwnDistributionFunction(
        string name, string first, double firstValue, string second, double secondValue) =>
        RunAsserting($"""
            pd = makedist('{name}', '{first}', {firstValue}, '{second}', {secondValue});
            for p = [0.05 0.25 0.5 0.75 0.95]
                x = icdf(pd, p);
                assert(abs(cdf(pd, x) - p) < 1e-6);
            end
            assert(pdf(pd, icdf(pd, 0.5)) > 0);
            """);

    [Fact]
    public Task TheClassNamesConstructDirectly() => RunAsserting("""
        pd = NormalDistribution('mu', 3, 'sigma', 4);
        assert(strcmp(class(pd), 'prob.NormalDistribution'));
        assert(pd.mu == 3);
        w = WeibullDistribution('A', 2, 'B', 1.5);
        assert(abs(mean(w) - mean(makedist('Weibull', 'A', 2, 'B', 1.5))) < 1e-12);
        """);

    [Fact]
    public Task AMultinomialIsBuiltFromItsProbabilities() => RunAsserting("""
        pd = makedist('Multinomial', 'probabilities', [0.2 0.5 0.3]);
        assert(pd.NumParameters == 1);
        assert(abs(pdf(pd, 2) - 0.5) < 1e-12);
        assert(abs(cdf(pd, 2) - 0.7) < 1e-12);
        assert(abs(mean(pd) - 2.1) < 1e-12);
        assert(icdf(pd, 0.5) == 2);

        % unnormalized counts are normalized rather than refused
        q = makedist('Multinomial', 'probabilities', [2 5 3]);
        assert(abs(pdf(q, 2) - 0.5) < 1e-12);
        """);

    [Fact]
    public Task APiecewiseLinearDistributionRisesBetweenItsBreakpoints() => RunAsserting("""
        pd = makedist('PiecewiseLinear', 'x', [0 1 3], 'Fx', [0 0.5 1]);
        assert(abs(cdf(pd, 0.5) - 0.25) < 1e-12);
        assert(abs(cdf(pd, 2) - 0.75) < 1e-12);
        assert(abs(icdf(pd, 0.5) - 1) < 1e-12);
        assert(pd.NumParameters == 0);
        assert(isequal(pd.x, [0 1 3]));
        """);

    // --- the shared names --------------------------------------------------------------------------

    [Fact]
    public Task TheFiveStatisticsAnswerAboutTheDistribution() => RunAsserting("""
        pd = makedist('Normal', 'mu', 10, 'sigma', 2);
        assert(mean(pd) == 10);
        assert(std(pd) == 2);
        assert(var(pd) == 4);
        assert(median(pd) == 10);
        assert(abs(iqr(pd) - 2 * (icdf(pd, 0.75) - 10)) < 1e-12);
        """);

    [Fact]
    public Task TheSameNamesStillAnswerAboutData() => RunAsserting("""
        assert(mean([1 2 3 4]) == 2.5);
        assert(abs(std([1 2 3 4]) - 1.290994448736) < 1e-9);
        assert(abs(var([1 2 3 4]) - 5/3) < 1e-12);
        assert(median([1 2 3 4]) == 2.5);
        assert(iqr([1 2 3 4]) == 2);

        % including the dimension and 'all' forms the reductions were wrapped for
        A = [1 2; 3 4; 5 6];
        assert(isequal(mean(A, 1), [3 4]));
        assert(numel(mean(A, 2)) == 3);
        assert(mean(A, 'all') == 3.5);
        assert(numel(iqr(A, 2)) == 3);

        % and the generic distribution names still take a word
        assert(abs(pdf('Normal', 0, 0, 1) - 0.3989422804) < 1e-9);
        assert(abs(cdf('Normal', 1.96, 0, 1, 'upper') - 0.025) < 1e-4);
        assert(numel(random('Normal', 0, 1, 1, 3)) == 3);
        """);

    [Fact]
    public Task ADrawFromAnObjectIsShapedAndRepeatable() => RunAsserting("""
        pd = makedist('Normal', 'mu', 0, 'sigma', 1);
        rng(3);
        a = random(pd, 2, 5);
        assert(size(a, 1) == 2 && size(a, 2) == 5);
        rng(3);
        b = random(pd, 2, 5);
        assert(isequal(a, b));
        assert(isscalar(random(pd)));
        """);

    [Fact]
    public Task ADensityIsAnsweredElementwiseAndKeepsItsShape() => RunAsserting("""
        pd = makedist('Normal');
        y = pdf(pd, [-1 0 1]);
        assert(numel(y) == 3 && size(y, 1) == 1);
        assert(abs(y(1) - y(3)) < 1e-15);
        M = cdf(pd, [0 1; 2 3]);
        assert(size(M, 1) == 2 && size(M, 2) == 2);
        assert(abs(M(1,1) - 0.5) < 1e-12);
        """);

    // --- truncate ------------------------------------------------------------------------------------

    [Fact]
    public Task TruncateConditionsTheDistributionOnAnInterval() => RunAsserting("""
        pd = makedist('Normal', 'mu', 0, 'sigma', 1);
        t = truncate(pd, -1, 1);
        mass = normcdf(1) - normcdf(-1);
        assert(t.IsTruncated == 1);
        assert(isequal(t.Truncation, [-1 1]));
        assert(abs(pdf(t, 0) * mass - pdf(pd, 0)) < 1e-12);
        assert(cdf(t, -1) == 0 && cdf(t, 1) == 1);
        assert(pdf(t, -2) == 0);
        assert(abs(mean(t)) < 1e-6);
        assert(abs(var(t) - (1 - 2 * normpdf(1) / mass)) < 1e-6);
        """);

    [Fact]
    public Task TruncatingACopyLeavesTheOriginalAlone() => RunAsserting("""
        pd = makedist('Normal');
        copy = pd;
        copy = truncate(copy, 0, 1);
        assert(copy.IsTruncated == 1);
        assert(pd.IsTruncated == 0);
        assert(mean(pd) == 0);
        """);

    [Fact]
    public Task ATruncatedDrawStaysInsideTheInterval() => RunAsserting("""
        rng(5);
        t = truncate(makedist('Normal'), -1, 1);
        d = random(t, 1, 500);
        assert(all(d >= -1 & d <= 1));
        """);

    // --- fitdist -------------------------------------------------------------------------------------

    [Fact]
    public Task FitdistRecoversTheParametersItWasGivenAndKeepsItsData() => RunAsserting(Fitted + """
        assert(abs(pd.mu - mean(x)) < 1e-9);
        assert(abs(pd.sigma - std(x)) < 1e-9);
        assert(numel(pd.InputData.data) == 200);
        assert(abs(pd.mu - 5) < 0.3);
        """);

    [Fact]
    public Task FitdistTakesCensoringAndFrequency() => RunAsserting("""
        x = [1; 2; 3; 4; 5; 6];
        plain = fitdist(x, 'Exponential');
        weighted = fitdist(x, 'Exponential', 'Frequency', [3; 1; 1; 1; 1; 1]);
        censored = fitdist(x, 'Exponential', 'Censoring', [0; 0; 0; 0; 0; 1]);

        % weighting the small observations pulls the mean down; censoring the largest says it is at
        % least six, which pushes the mean up.
        assert(weighted.mu < plain.mu);
        assert(censored.mu > plain.mu);
        """);

    [Fact]
    public Task FitdistGroupsWithBy() => RunAsserting("""
        x = [1; 2; 3; 11; 12; 13];
        g = [1; 1; 1; 2; 2; 2];
        [objs, names] = fitdist(x, 'Normal', 'By', g);
        assert(iscell(objs));
        assert(numel(objs) == 2);
        assert(abs(objs{1}.mu - 2) < 1e-9);
        assert(abs(objs{2}.mu - 12) < 1e-9);
        assert(numel(names) == 2);
        """);

    [Fact]
    public Task AKernelFitSmoothsTheSampleItWasGiven() => RunAsserting("""
        rng(7);
        x = normrnd(0, 1, 300, 1);
        pk = fitdist(x, 'Kernel');
        assert(strcmp(class(pk), 'prob.KernelDistribution'));
        assert(strcmp(pk.Kernel, 'normal'));
        assert(pk.BandWidth > 0);
        assert(pk.NumParameters == 0);
        assert(abs(mean(pk) - mean(x)) < 1e-9);
        assert(abs(cdf(pk, median(x)) - 0.5) < 0.05);

        % a wider kernel and a different shape both change the answer rather than being ignored
        wide = fitdist(x, 'Kernel', 'Width', 2);
        assert(abs(wide.BandWidth - 2) < 1e-12);
        assert(abs(pdf(wide, 0) - pdf(pk, 0)) > 1e-6);
        boxy = fitdist(x, 'Kernel', 'Kernel', 'box');
        assert(strcmp(boxy.Kernel, 'box'));
        """);

    [Fact]
    public Task ABetterFamilyFitsBetter() => RunAsserting("""
        rng(13);
        y = wblrnd(2, 1.5, 300, 1);
        w = fitdist(y, 'Weibull');
        e = fitdist(y, 'Exponential');

        % the data came from a Weibull, and the exponential is the Weibull with its shape held at
        % one — so the two-parameter fit cannot do worse, and on this sample it does better.
        assert(negloglik(w) < negloglik(e));
        assert(abs(w.A - 2) < 0.3);
        assert(abs(w.B - 1.5) < 0.2);
        """);

    // --- paramci and proflik ---------------------------------------------------------------------------

    [Fact]
    public Task ParamciBracketsEachEstimateAndWidensWithConfidence() => RunAsserting(Fitted + """
        ci = paramci(pd);
        assert(size(ci, 1) == 2 && size(ci, 2) == 2);
        assert(ci(1,1) < pd.mu && pd.mu < ci(2,1));
        assert(ci(1,2) < pd.sigma && pd.sigma < ci(2,2));

        wider = paramci(pd, 'Alpha', 0.01);
        assert(wider(1,1) < ci(1,1));
        assert(wider(2,1) > ci(2,1));
        assert(wider(1,2) < ci(1,2));
        """);

    [Fact]
    public Task ProflikWalksOneParameterAndRefitsTheRest() => RunAsserting(Fitted + """
        [ll, p, other, ok] = proflik(pd, 1);
        assert(numel(ll) == numel(p));
        assert(all(ok == 1));
        assert(size(other, 2) == 1);

        % the curve peaks at the estimate and falls away on both sides, which is what makes it a
        % profile rather than a slice: the other parameter is re-fitted at every step.
        [~, at] = max(ll);
        assert(abs(p(at) - pd.mu) < 0.05);
        assert(ll(1) < max(ll) && ll(end) < max(ll));

        % and the range can be named
        [l2, p2] = proflik(pd, 1, 'SetRange', [4.5 5.5]);
        assert(abs(min(p2) - 4.5) < 1e-9);
        assert(abs(max(p2) - 5.5) < 1e-9);
        assert(numel(l2) == numel(p2));
        """);

    [Fact]
    public Task NegloglikCountsTheTruncationItWasGiven() => RunAsserting(Fitted + """
        whole = negloglik(pd);
        kept = truncate(pd, 0, 12);
        assert(negloglik(kept) < whole);
        """);

    // --- refusals ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("makedist('Gaussian')", "is not a distribution with an object")]
    [InlineData("makedist('Normal', 'muu', 1)", "unknown option 'muu'")]
    [InlineData("makedist('Normal', 'sigma', -1)", "must be above zero")]
    [InlineData("makedist('Kernel')", "only data to be fitted")]
    [InlineData("makedist('Multinomial', 'probabilities', [0.5 -0.5])", "non-negative")]
    [InlineData("makedist('PiecewiseLinear', 'x', 1, 'Fx', 1)", "at least two long")]
    [InlineData("makedist('PiecewiseLinear', 'x', [1 0 2], 'Fx', [0 0.5 1])", "increasing breakpoints")]
    [InlineData("truncate(makedist('Normal'), 5, 4)", "upper limit above its lower one")]
    [InlineData("truncate(makedist('Uniform'), 5, 6)", "puts no probability between")]
    [InlineData("truncate(5, 1, 2)", "takes a probability distribution object first")]
    [InlineData("negloglik(makedist('Normal'))", "fitted to data")]
    [InlineData("paramci(makedist('Normal'))", "fitted to data")]
    [InlineData("paramci(fitdist([1;2;3;4;5], 'Normal'), 'Alpha', 2)", "strictly between zero and one")]
    [InlineData("proflik(fitdist([1;2;3;4;5], 'Normal'), 7)", "names none of them")]
    [InlineData("proflik(fitdist([1;2;3;4;5], 'Kernel'), 1)", "no estimated parameter to profile")]
    [InlineData("paramci(fitdist([1;2;3;4;5], 'Kernel'))", "no estimated parameters")]
    [InlineData("mean(makedist('Normal'), 2)", "takes a distribution object on its own")]
    [InlineData("pdf(makedist('Normal'))", "takes one distribution and one array")]
    [InlineData("fitdist([1;2;3], 'Nonesuch')", "is not a distribution with an object")]
    [InlineData("random(makedist('Normal'), 1, 2, 3)", "at most two sizes")]
    public async Task RefusesWhatItCannotHonour(string call, string expected)
    {
        string message = await RunExpectingFailure(call + ";");
        Assert.Contains(expected, message, StringComparison.Ordinal);
    }

    // --- the other dialect --------------------------------------------------------------------------

    [Fact]
    public void TheSameNamesAreReachableFromJgsAsWell()
    {
        var context = new ScriptContext(_output, (_, _) => { });
        ScriptRunResult result = JgsRunner.Run(
            """
            let pd = makedist('Normal', 'mu', 4, 'sigma', 2)
            let t = truncate(pd, 0, 8)
            print(mean(pd))
            print(class(pd))
            print(cdf(t, 0))
            print(cdf(t, 8))
            """,
            context, default, sourceId: "", hook: null, JgsDialect.Jgs);

        // Read back through the verbs rather than through a dot: JGS has no field access, which is a
        // property of that dialect and not of these objects.
        Assert.True(result.Success, result.Message + _output.ErrorText);
        IReadOnlyList<string> lines = _output.NormalLines;
        Assert.Equal(["4", "prob.NormalDistribution", "0", "1"], lines.TakeLast(4));
    }
}
