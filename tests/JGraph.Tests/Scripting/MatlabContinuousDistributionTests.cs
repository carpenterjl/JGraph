using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M53 wave C: the continuous distributions as the scripts call them. The kernels are pinned
/// elsewhere; what is tested here is everything the scripting layer adds — parameter defaults,
/// expansion against arrays, the size arguments on the draws, the four fitter output shapes, and the
/// generic names that take the distribution as a word.
/// </summary>
[Collection("JG facade")]
public class MatlabContinuousDistributionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabContinuousDistributionTests() => JG.Reset();

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
    public Task DensitiesAndQuantilesAnswerThePublishedValues() => RunAsserting(@"
        assert(abs(normcdf(1.96) - 0.975002104852) < 1e-12);
        assert(abs(norminv(0.975) - 1.959963984540) < 1e-12);
        assert(abs(tinv(0.975, 10) - 2.228138851986) < 1e-11);
        assert(abs(chi2inv(0.95, 3) - 7.814727903251) < 1e-9);
        assert(abs(betacdf(0.5, 2, 3) - 11/16) < 1e-12);
        assert(abs(finv(0.95, 3, 10) - 3.708265) < 1e-6);
    ");

    /// <summary>
    /// Every parameter MathWorks documents a default for is optional, and every one it does not is
    /// required by name. Getting this wrong in the permissive direction is the dangerous one: a
    /// forgotten parameter would silently become a standard normal.
    /// </summary>
    [Fact]
    public Task OmittedParametersTakeTheirDocumentedDefaults() => RunAsserting(@"
        assert(normpdf(0) == normpdf(0, 0, 1));
        assert(exppdf(1) == exppdf(1, 1));
        assert(unifcdf(0.25) == 0.25);
        assert(wblcdf(1) == wblcdf(1, 1, 1));
        assert(raylcdf(1) == raylcdf(1, 1));
        assert(gampdf(1, 2) == gampdf(1, 2, 1));
        assert(evcdf(0) == evcdf(0, 0, 1));
        assert(gpcdf(1, 0.5) == gpcdf(1, 0.5, 1, 0));
    ");

    [Fact]
    public async Task ParametersWithNoDefaultAreRequiredByName()
    {
        string message = await RunExpectingFailure("y = betapdf(0.5);");
        Assert.Contains("betapdf", message, StringComparison.Ordinal);
        Assert.Contains("a", message, StringComparison.Ordinal);

        Assert.Contains("v", await RunExpectingFailure("y = chi2cdf(3);"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The argument and the parameters expand against each other under the same singleton rule the
    /// operators use, and the result keeps the expanded shape rather than being flattened.
    /// </summary>
    [Fact]
    public Task ArgumentsAndParametersExpandAgainstEachOther() => RunAsserting(@"
        y = normpdf([1 2; 3 4], 0, 1);
        assert(isequal(size(y), [2 2]));
        assert(abs(y(2,1) - normpdf(3)) < 1e-15);

        % A row of arguments against a column of parameters is their whole grid.
        g = normcdf([0 1 2], [0; 1], 1);
        assert(isequal(size(g), [2 3]));
        assert(abs(g(2,2) - 0.5) < 1e-15);

        % A vector of parameters with one argument answers one value per parameter.
        p = exppdf(1, [1 2 4]);
        assert(isequal(size(p), [1 3]));
    ");

    [Fact]
    public async Task ArgumentsThatCannotExpandAreRefusedWithBothSizes()
    {
        string message = await RunExpectingFailure("y = normpdf([1 2 3], [1 2], 1);");
        Assert.Contains("1x3", message, StringComparison.Ordinal);
        Assert.Contains("1x2", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The upper tail is a documented option word on the distribution functions and nowhere else, so
    /// a misspelling has to be reported rather than swallowed.
    /// </summary>
    [Fact]
    public async Task TheUpperTailIsAnOptionOnTheDistributionFunctions()
    {
        await RunAsserting(@"
            assert(abs(normcdf(1.96, 0, 1, 'upper') - (1 - normcdf(1.96))) < 1e-15);
            assert(abs(tcdf(2, 10, 'UPPER') - (1 - tcdf(2, 10))) < 1e-14);
            assert(abs(gamcdf(3, 2, 2, 'upper') - (1 - gamcdf(3, 2, 2))) < 1e-14);

            % Far out, subtracting from one would have rounded the answer away entirely.
            assert(normcdf(9, 0, 1, 'upper') > 0);
            assert(normcdf(9, 0, 1, 'upper') < 1e-18);
        ");

        Assert.Contains("uppr", await RunExpectingFailure("y = normcdf(1, 0, 1, 'uppr');"), StringComparison.Ordinal);
        Assert.Contains("upper", await RunExpectingFailure("y = normpdf(1, 0, 1, 'upper');"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The draws take sizes after every parameter, the way <c>rand</c> does — a single size meaning a
    /// square — and a seeded run repeats itself.
    /// </summary>
    [Fact]
    public Task RandomDrawsTakeSizesAndRepeatUnderASeed() => RunAsserting(@"
        rng(11);
        a = normrnd(0, 1, 2, 3);
        rng(11);
        b = normrnd(0, 1, 2, 3);
        assert(isequal(size(a), [2 3]));
        assert(isequal(a, b));

        assert(isequal(size(exprnd(2, 4)), [4 4]));
        assert(isequal(size(wblrnd(1, 2, [2 5])), [2 5]));

        % With no size given the parameters' own shape is the answer's shape.
        assert(isequal(size(normrnd([1 2 3], 1)), [1 3]));
        assert(isequal(size(gamrnd(2, 2)), [1 1]));

        % Every parameter must be given, so what follows them is unambiguously a size.
        c = unifrnd(0, 1, 1, 1000);
        assert(min(c) >= 0 && max(c) <= 1);
    ");

    [Fact]
    public Task MomentsComeBackAsTwoOutputsAndExpand() => RunAsserting(@"
        [m, v] = normstat(3, 2);
        assert(m == 3 && v == 4);

        [m2, v2] = normstat([0 1], 2);
        assert(isequal(m2, [0 1]));
        assert(isequal(v2, [4 4]));

        [gm, gv] = gamstat(2, 3);
        assert(gm == 6 && gv == 18);

        % Moments that do not exist are NaN rather than a number.
        [~, tv] = tstat(1.5);
        assert(isnan(tv));
    ");

    /// <summary>
    /// MathWorks gives the fitters four different output shapes, and a script that unpacks the wrong
    /// one gets numbers in the wrong variables rather than an error, so each shape is asserted.
    /// </summary>
    [Fact]
    public Task EachFitterAnswersInItsOwnDocumentedShape() => RunAsserting(@"
        x = [2 4 4 4 5 5 7 9];

        % normfit: four separate outputs.
        [mu, sg, muci, sgci] = normfit(x);
        assert(abs(mu - 5) < 1e-12);
        assert(abs(sg - sqrt(32/7)) < 1e-12);
        assert(isequal(size(muci), [2 1]));
        assert(muci(1) < mu && mu < muci(2));
        assert(isequal(size(sgci), [2 1]));

        % expfit: one estimate and its interval.
        [mh, mc] = expfit([1 2 3 4 5]);
        assert(abs(mh - 3) < 1e-12);
        assert(isequal(size(mc), [2 1]));
        assert(abs(mc(1) - 1.4646165234) < 1e-8);

        % wblfit and its kind: one row of estimates and a two-row matrix of limits.
        [ph, pc] = wblfit([1.2 2.3 0.8 1.9 2.7 1.1 3.2 0.6]);
        assert(isequal(size(ph), [1 2]));
        assert(isequal(size(pc), [2 2]));
        assert(pc(1,1) < ph(1) && ph(1) < pc(2,1));

        % unifit: the extremes and their one-sided intervals.
        [a, b, aci, bci] = unifit([2 3 5 7 9]);
        assert(a == 2 && b == 9);
        assert(aci(1) < 2 && aci(2) == 2);
        assert(bci(1) == 9 && bci(2) > 9);
    ");

    [Fact]
    public Task FittersTakeAlphaCensoringAndFrequency() => RunAsserting(@"
        x = [2 4 4 4 5 5 7 9];

        % A wider confidence level gives a wider interval.
        [~, ~, narrow] = normfit(x, 0.05);
        [~, ~, wide] = normfit(x, 0.01);
        assert(wide(1) < narrow(1) && narrow(2) < wide(2));

        % A censored observation is only known to be at least that large, so it pulls the mean up.
        plain = expfit([1 2 3 4 5]);
        censored = expfit([1 2 3 4 5], 0.05, [0 0 0 1 1]);
        assert(censored > plain);
        assert(abs(censored - 5) < 1e-4);

        % A frequency vector is a compressed sample, not a weighting.
        compressed = normfit([1 2 3], 0.05, [], [1 3 2]);
        expanded = normfit([1 2 2 2 3 3]);
        assert(abs(compressed - expanded) < 1e-12);
    ");

    [Fact]
    public Task LikelihoodsReportBothTheValueAndThePrecision() => RunAsserting(@"
        nl = normlike([0 1], [0.5 -0.5 1]);
        assert(abs(nl - (3*0.5*log(2*pi) + 0.75)) < 1e-12);

        [nl2, avar] = normlike([0 1], [0.5 -0.5 1]);
        assert(nl2 == nl);
        assert(isequal(size(avar), [2 2]));
        assert(abs(avar(1,2) - avar(2,1)) < 1e-8);

        % The likelihood is smallest at the estimate, which is what maximum likelihood means.
        x = [1.2 2.3 0.8 1.9 2.7 1.1 3.2 0.6];
        ph = wblfit(x);
        best = wbllike(ph, x);
        assert(best < wbllike([ph(1)*1.3 ph(2)], x));
        assert(best < wbllike([ph(1) ph(2)*0.7], x));
    ");

    /// <summary>
    /// The generic names and the dedicated ones are the same code, so they have to answer identically
    /// — and the generic ones accept every spelling of a family's name.
    /// </summary>
    [Fact]
    public Task TheGenericNamesAgreeWithTheDedicatedOnes() => RunAsserting(@"
        assert(pdf('Normal', 0.3, 0, 1) == normpdf(0.3));
        assert(cdf('Weibull', 2, 1, 2) == wblcdf(2, 1, 2));
        assert(icdf('Gamma', 0.5, 2, 2) == gaminv(0.5, 2, 2));
        assert(cdf('Generalized Extreme Value', 1, 0.2, 1.5, 3) == gevcdf(1, 0.2, 1.5, 3));
        assert(cdf('gev', 1, 0.2, 1.5, 3) == gevcdf(1, 0.2, 1.5, 3));
        assert(cdf('normal', 1, 0, 1, 'upper') == normcdf(1, 0, 1, 'upper'));

        rng(3); a = random('Beta', 2, 5, 2, 2);
        rng(3); b = betarnd(2, 5, 2, 2);
        assert(isequal(a, b));
    ");

    [Fact]
    public async Task AnUnknownDistributionNameListsTheKnownOnes()
    {
        string message = await RunExpectingFailure("y = pdf('Gaussianish', 0, 0, 1);");
        Assert.Contains("Gaussianish", message, StringComparison.Ordinal);
        Assert.Contains("Rayleigh", message, StringComparison.Ordinal);
    }

    [Fact]
    public Task MaximumLikelihoodFitsANamedFamilyOrADensityYouSupply() => RunAsserting(@"
        x = [1.1 2.2 0.9 1.5 1.8];
        assert(abs(mle(x, 'distribution', 'exponential') - 1.5) < 1e-9);

        % Without a name it fits a normal, which is the same answer normfit gives.
        p = mle(x);
        assert(abs(p(1) - mean(x)) < 1e-9);

        [ph, pci] = mle(x, 'distribution', 'normal', 'Alpha', 0.01);
        assert(isequal(size(pci), [2 2]));
        assert(pci(1,1) < ph(1) && ph(1) < pci(2,1));

        % A density of your own, given a starting point: the exponential written out by hand.
        own = mle(x, 'pdf', @(d, m) exp(-d./m)./m, 'start', 1);
        assert(abs(own - 1.5) < 1e-4);
    ");

    [Fact]
    public async Task ADensityOfYourOwnNeedsAStartingPoint()
    {
        string message = await RunExpectingFailure("y = mle([1 2 3], 'pdf', @(d, m) exp(-d./m)./m);");
        Assert.Contains("start", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// These names are new in both dialects, so one of them is checked through the JGS runner too —
    /// a distribution has no dialect-dependent reading to get wrong, and this is what says so.
    /// </summary>
    [Fact]
    public void TheDistributionsExistInJgsAsWell()
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add((0, figure)), null);
        ScriptRunResult result = JgsRunner.Run(
            "let p = normcdf(1.96);\nassert(abs(p - 0.975002104852) < 1e-12);",
            context,
            default,
            sourceId: "",
            hook: null,
            JgsDialect.Jgs);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }
}
