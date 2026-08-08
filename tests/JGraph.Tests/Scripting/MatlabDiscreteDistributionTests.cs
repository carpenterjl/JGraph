using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M53 wave D: the discrete distributions as the scripts call them. The kernels are pinned elsewhere;
/// what is tested here is the scripting layer — that every parameter is required by name, that the
/// arguments expand against each other, that the draws take sizes, and that the three fitters and the
/// multinomial answer in the shapes MathWorks documents.
/// </summary>
[Collection("JG facade")]
public class MatlabDiscreteDistributionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabDiscreteDistributionTests() => JG.Reset();

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
    public Task TheDiscreteFamiliesAnswerThePublishedValues() => RunAsserting(@"
        assert(abs(binopdf(3, 10, 0.3) - 0.266827932) < 1e-9);
        assert(abs(binocdf(3, 10, 0.3) - 0.6496107184) < 1e-9);
        assert(abs(poisspdf(2, 3) - 0.2240418077) < 1e-9);
        assert(abs(geopdf(2, 0.25) - 0.140625) < 1e-12);
        assert(abs(hygepdf(2, 20, 6, 5) - 5460/15504) < 1e-12);
        assert(abs(nbinpdf(3, 2, 0.4) - 0.13824) < 1e-12);
        assert(abs(unidpdf(3, 6) - 1/6) < 1e-12);
        assert(binoinv(0.5, 10, 0.3) == 3);
        assert(poissinv(0.9, 3) == 5);
        assert(unidinv(0.5, 6) == 3);
    ");

    /// <summary>
    /// Not one discrete family documents a default for any of its parameters, so every one of them is
    /// required by name. That is also what makes the arguments after them unambiguously sizes.
    /// </summary>
    [Fact]
    public async Task EveryDiscreteParameterIsRequiredByName()
    {
        Assert.Contains("needs p", await RunExpectingFailure("binopdf(3, 10)"));
        Assert.Contains("needs lambda", await RunExpectingFailure("poisscdf(2)"));
        Assert.Contains("needs n", await RunExpectingFailure("hygepdf(2, 20, 6)"));
        Assert.Contains("needs every parameter", await RunExpectingFailure("binornd(10)"));
    }

    [Fact]
    public Task TheArgumentsExpandAgainstEachOther() => RunAsserting(@"
        y = binopdf(0:10, 10, 0.3);
        assert(isequal(size(y), [1 11]));
        assert(abs(sum(y) - 1) < 1e-12);

        % A column of counts against a row of rates makes a grid, the same way arithmetic would.
        g = poisspdf((0:3)', [1 2 3]);
        assert(isequal(size(g), [4 3]));
        assert(abs(g(1, 1) - exp(-1)) < 1e-12);
        assert(abs(g(3, 2) - poisspdf(2, 2)) < 1e-12);

        % Three parameters expand together, not two at a time.
        h = hygepdf(2, 20, [4 6 8], 5);
        assert(isequal(size(h), [1 3]));
        assert(abs(h(2) - hygepdf(2, 20, 6, 5)) < 1e-12);
    ");

    [Fact]
    public async Task TheSizeMismatchNamesBothSizes()
    {
        string message = await RunExpectingFailure("binopdf([1 2 3], [10 20], 0.3)");
        Assert.Contains("1x3", message);
        Assert.Contains("1x2", message);
    }

    /// <summary>
    /// The upper tail is the one place a discrete distribution function has an answer that
    /// subtracting from one cannot give: far enough out, one minus the distribution function is
    /// exactly zero while the tail itself is still a positive number.
    /// </summary>
    [Fact]
    public Task TheUpperTailKeepsTheFiguresSubtractionWouldLose() => RunAsserting(@"
        assert(abs(binocdf(3, 10, 0.3, 'upper') - 0.3503892816) < 1e-9);
        assert(abs(poisscdf(2, 3, 'upper') - 0.5768099189) < 1e-9);
        assert(abs(geocdf(2, 0.25, 'upper') - 0.75^3) < 1e-12);
        assert(abs(nbincdf(3, 2, 0.4, 'upper') - (1 - nbincdf(3, 2, 0.4))) < 1e-12);

        deep = poisscdf(60, 3, 'upper');
        assert(deep > 0 && deep < 1e-40);
        assert(1 - poisscdf(60, 3) == 0);
    ");

    [Fact]
    public async Task MisspelledOptionsAreRefusedRatherThanIgnored()
    {
        Assert.Contains("upper", await RunExpectingFailure("binocdf(3, 10, 0.3, 'uppr')"));
        Assert.Contains("upper", await RunExpectingFailure("binopdf(3, 10, 0.3, 'upper')"));
    }

    [Fact]
    public Task DrawsTakeSizesAfterTheParameters() => RunAsserting(@"
        rng(11);
        a = binornd(10, 0.3, 2, 3);
        assert(isequal(size(a), [2 3]));
        assert(all(all(a == floor(a))));
        assert(all(all(a >= 0 & a <= 10)));

        b = poissrnd(4, [3 3]);
        assert(isequal(size(b), [3 3]));

        c = unidrnd(6, 1, 500);
        assert(min(c) >= 1 && max(c) <= 6);

        % A vector of parameters draws one number per parameter, with no size argument at all.
        d = geornd([0.2 0.5 0.9]);
        assert(isequal(size(d), [1 3]));

        rng(11);
        assert(isequal(binornd(10, 0.3, 2, 3), a));
    ");

    [Fact]
    public Task TheMomentsComeBackAsTwoOutputs() => RunAsserting(@"
        [m, v] = binostat(10, 0.3);
        assert(abs(m - 3) < 1e-12 && abs(v - 2.1) < 1e-12);

        [m, v] = poisstat(4);
        assert(m == 4 && v == 4);

        [m, v] = hygestat(20, 6, 5);
        assert(abs(m - 1.5) < 1e-12);
        assert(abs(v - 5*0.3*0.7*(15/19)) < 1e-12);

        [m, v] = geostat(0.2);
        assert(abs(m - 4) < 1e-12 && abs(v - 20) < 1e-12);

        [m, v] = unidstat(6);
        assert(abs(m - 3.5) < 1e-12 && abs(v - 35/12) < 1e-12);

        % Elementwise, like everything else here.
        [m, v] = poisstat([1 2 3]);
        assert(isequal(m, [1 2 3]) && isequal(v, [1 2 3]));
    ");

    /// <summary>
    /// <c>binofit</c> is the one fitter whose second argument is data rather than a confidence level,
    /// and the one that answers a row per experiment rather than a column per parameter.
    /// </summary>
    [Fact]
    public Task BinofitTakesItsTrialCountBesideTheData() => RunAsserting(@"
        [phat, pci] = binofit(45, 100);
        assert(abs(phat - 0.45) < 1e-12);
        assert(isequal(size(pci), [1 2]));
        assert(abs(pci(1) - 0.3503) < 1e-4);
        assert(abs(pci(2) - 0.5527) < 1e-4);

        % One row of limits per experiment when several are given at once.
        [p2, c2] = binofit([2 45 98], 100);
        assert(isequal(size(p2), [1 3]));
        assert(isequal(size(c2), [3 2]));
        assert(all(c2(:, 1) <= p2') && all(p2' <= c2(:, 2)));

        % A tighter confidence level gives a wider interval.
        [~, wide] = binofit(45, 100, 0.01);
        assert(wide(1) < pci(1) && wide(2) > pci(2));
    ");

    [Fact]
    public Task PoissfitAndNbinfitAnswerInTheirDocumentedShapes() => RunAsserting(@"
        [lambdahat, lambdaci] = poissfit([1 2 3 4 5]);
        assert(abs(lambdahat - 3) < 1e-12);
        assert(isequal(size(lambdaci), [2 1]));
        assert(lambdaci(1) < 3 && lambdaci(2) > 3);

        rng(3);
        data = nbinrnd(4, 0.35, 1, 3000);
        [phat, pci] = nbinfit(data);
        assert(isequal(size(phat), [1 2]));
        assert(isequal(size(pci), [2 2]));
        assert(abs(phat(1) - 4) < 0.6);
        assert(abs(phat(2) - 0.35) < 0.05);
        assert(all(pci(1, :) <= phat) && all(phat <= pci(2, :)));
    ");

    /// <summary>
    /// Every discrete name is reachable through the generic forms too, because a discrete family is
    /// the same kind of record a continuous one is.
    /// </summary>
    [Fact]
    public Task TheGenericNamesReachTheDiscreteFamilies() => RunAsserting(@"
        assert(abs(pdf('Poisson', 2, 3) - poisspdf(2, 3)) < 1e-15);
        assert(abs(cdf('Binomial', 3, 10, 0.3) - binocdf(3, 10, 0.3)) < 1e-15);
        assert(abs(cdf('Binomial', 3, 10, 0.3, 'upper') - binocdf(3, 10, 0.3, 'upper')) < 1e-15);
        assert(icdf('Discrete Uniform', 0.5, 6) == 3);
        assert(abs(pdf('Negative Binomial', 3, 2, 0.4) - nbinpdf(3, 2, 0.4)) < 1e-15);
        assert(abs(pdf('hypergeometric', 2, 20, 6, 5) - hygepdf(2, 20, 6, 5)) < 1e-15);

        rng(2);
        r = random('Geometric', 0.3, 1, 40);
        assert(isequal(size(r), [1 40]));
        assert(all(r >= 0));
    ");

    [Fact]
    public async Task AnUnknownDistributionNamesTheOnesItKnows()
    {
        string message = await RunExpectingFailure("pdf('Binomal', 3, 10, 0.3)");
        Assert.Contains("Binomial", message);
        Assert.Contains("Poisson", message);
        Assert.Contains("Normal", message);
    }

    /// <summary>
    /// <c>mle</c> reaches the discrete families it can fit, refuses the ones it cannot by name, and
    /// takes the binomial's trial count as an option because that count is not part of the data.
    /// </summary>
    [Fact]
    public async Task MaximumLikelihoodReachesTheDiscreteFamilies()
    {
        await RunAsserting(@"
            assert(abs(mle([0 1 0 2 1 3], 'distribution', 'Poisson') - 7/6) < 1e-12);
            assert(abs(mle([2 3 4], 'distribution', 'Binomial', 'ntrials', 10) - 0.3) < 1e-12);
            assert(abs(mle([0 1 2 3 4], 'distribution', 'Geometric') - 5/15) < 1e-12);

            [phat, pci] = mle([0 1 0 2 1 3], 'distribution', 'Poisson');
            assert(isequal(size(pci), [2 1]));
            assert(pci(1) < phat && phat < pci(2));
        ");

        Assert.Contains("ntrials", await RunExpectingFailure("mle([2 3 4], 'distribution', 'Binomial')"));
        Assert.Contains("Poisson", await RunExpectingFailure("mle([2 3], 'distribution', 'Poisson', 'ntrials', 10)"));
        Assert.Contains("no maximum likelihood fit",
            await RunExpectingFailure("mle([2 3 4], 'distribution', 'Discrete Uniform')"));
    }

    [Fact]
    public Task TheMultinomialTakesWholeRows() => RunAsserting(@"
        assert(abs(mnpdf([1 2 3], [0.2 0.3 0.5]) - 0.135) < 1e-12);

        % One probability per row of counts answers one probability per row.
        y = mnpdf([1 2 3; 3 2 1], [0.2 0.3 0.5]);
        assert(isequal(size(y), [2 1]));
        assert(abs(y(1) - 0.135) < 1e-12);

        % And a row of probabilities per row of counts.
        z = mnpdf([1 1; 2 0], [0.5 0.5; 0.25 0.75]);
        assert(isequal(size(z), [2 1]));
        assert(abs(z(1) - 0.5) < 1e-12);
        assert(abs(z(2) - 0.0625) < 1e-12);

        rng(4);
        r = mnrnd(10, [0.2 0.3 0.5], 6);
        assert(isequal(size(r), [6 3]));
        assert(all(sum(r, 2) == 10));

        one = mnrnd(7, [0.5 0.5]);
        assert(isequal(size(one), [1 2]));
        assert(sum(one) == 7);
    ");

    [Fact]
    public async Task TheMultinomialSaysWhenItsShapesDisagree()
    {
        Assert.Contains("categories", await RunExpectingFailure("mnpdf([1 2 3], [0.5 0.5])"));
        Assert.Contains("row", await RunExpectingFailure("mnpdf([1 2; 2 1; 0 3], [0.5 0.5; 0.4 0.6])"));
    }

    /// <summary>The discrete names are the same names in the JGS dialect.</summary>
    [Fact]
    public void TheDiscreteNamesWorkInJgsToo()
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add((0, figure)), null);
        ScriptRunResult result = JgsRunner.Run(
            "let p = binopdf(3, 10, 0.3)\nassert(abs(p - 0.266827932) < 1e-9)\n",
            context, default, sourceId: "", hook: null, JgsDialect.Jgs);

        Assert.True(result.Success, result.Message + _output.ErrorText);
    }
}
