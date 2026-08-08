using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M53 wave E as the scripts call it: the multivariate distributions, the samplers and the three
/// resampling verbs. The numerics are pinned elsewhere; what is tested here is the scripting layer —
/// how the data is read, which shape comes back, what a seed promises, and what a wrong argument says.
/// </summary>
[Collection("JG facade")]
public class MatlabMultivariateSamplingTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabMultivariateSamplingTests() => JG.Reset();

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

    // --- The multivariate distributions --------------------------------------------------------------

    [Fact]
    public Task TheMultivariateFamiliesAnswerTheirClosedForms() => RunAsserting(@"
        R = [1 0.5; 0.5 1];
        assert(abs(mvnpdf([0 0], [0 0], R) - 1/(2*pi*sqrt(0.75))) < 1e-12);
        assert(abs(mvncdf([0 0], [0 0], R) - (0.25 + asin(0.5)/(2*pi))) < 1e-10);
        assert(abs(mvtcdf([0 0], R, 5) - (0.25 + asin(0.5)/(2*pi))) < 1e-9);
        assert(abs(mvnpdf([1 -3], [0 0], [4 0; 0 9]) - normpdf(1,0,2)*normpdf(-3,0,3)) < 1e-15);
        assert(abs(mvncdf([1 1 1], [0 0 0], eye(3)) - normcdf(1)^3) < 1e-9);
        assert(abs(mvtpdf([0 0], R, 5) - 2.5/(5*pi*sqrt(0.75))) < 1e-12);
    ");

    /// <summary>
    /// A row is one point in as many dimensions as it is long; a column is that many points in one
    /// dimension. Reading it the other way round is the classic mistake, so both are pinned.
    /// </summary>
    [Fact]
    public Task RowsArePointsAndColumnsAreObservations() => RunAsserting(@"
        one = mvnpdf([0 0]);
        assert(isscalar(one));
        many = mvnpdf([0; 1; 2]);
        assert(isequal(size(many), [3 1]));
        assert(abs(many(1) - normpdf(0)) < 1e-15);
        assert(abs(many(3) - normpdf(2)) < 1e-15);
        block = mvnpdf([1 2; 3 4], [0 0], eye(2));
        assert(isequal(size(block), [2 1]));
        assert(abs(block(1) - exp(-2.5)/(2*pi)) < 1e-15);
    ");

    /// <summary>
    /// The mean may be one row shared by every point or one row each, and the covariance may be a full
    /// matrix or a row of variances standing for the diagonal.
    /// </summary>
    [Fact]
    public Task MeansAndVariancesExpandTheDocumentedWays() => RunAsserting(@"
        X = [1 2; 3 4; 5 6];
        shared = mvnpdf(X, [0 0], [4 9]);
        full = mvnpdf(X, [0 0], [4 0; 0 9]);
        assert(max(abs(shared - full)) < 1e-15);
        each = mvnpdf(X, X, eye(2));
        assert(max(abs(each - 1/(2*pi))) < 1e-15);
        assert(abs(mvnpdf([0 0], 0, 1) - 1/(2*pi)) < 1e-15);
    ");

    /// <summary>
    /// The four-argument form is a box, and the box between minus infinity and a point is the same
    /// number the three-argument form gives.
    /// </summary>
    [Fact]
    public Task TheFourArgumentFormIsABox() => RunAsserting(@"
        S = [2 0.8; 0.8 1.5];
        below = mvncdf([1 2], [0 0], S);
        box = mvncdf([-Inf -Inf], [1 2], [0 0], S);
        assert(abs(below - box) < 1e-12);
        inside = mvncdf([-1 -1], [1 1], [0 0], eye(2));
        assert(abs(inside - (2*normcdf(1) - 1)^2) < 1e-12);
        [p, err] = mvncdf([0 0 0], [1 1 1], [0 0 0], eye(3));
        assert(abs(p - (normcdf(1) - 0.5)^3) < 1e-9);
        assert(err >= 0 && err < 1e-8);
    ");

    /// <summary>
    /// Letting a variable run to infinity leaves the distribution of the rest — the marginal identity,
    /// which is what a scaling variable applied on the wrong side would break.
    /// </summary>
    [Fact]
    public Task AFreeVariableLeavesTheRestAlone() => RunAsserting(@"
        assert(abs(mvncdf([0.7 Inf], [0 0], [1 0.6; 0.6 1]) - normcdf(0.7)) < 1e-10);
        assert(abs(mvtcdf([1.4 Inf], [1 0.35; 0.35 1], 6) - tcdf(1.4, 6)) < 1e-8);
    ");

    /// <summary>
    /// An options structure is what MathWorks passes tolerances in. The rule here is deterministic and
    /// has none, so the argument is accepted and changes nothing.
    /// </summary>
    [Fact]
    public Task AnOptionsStructureIsAcceptedAndIgnored() => RunAsserting(@"
        o.TolFun = 1e-10;
        plain = mvncdf([0.5 0.5], [0 0], eye(2));
        withOptions = mvncdf([0.5 0.5], [0 0], eye(2), o);
        assert(plain == withOptions);
    ");

    [Fact]
    public Task TheCovarianceFactorReproducesItsCovariance() => RunAsserting(@"
        S = [4 1 0.5; 1 3 0.25; 0.5 0.25 2];
        [T, num] = cholcov(S);
        assert(num == 3);
        assert(max(max(abs(T'*T - S))) < 1e-12);

        Q = [1 0 1; 0 1 1; 1 1 2];
        [T2, num2] = cholcov(Q);
        assert(num2 == 2);
        assert(isequal(size(T2), [2 3]));
        assert(max(max(abs(T2'*T2 - Q))) < 1e-12);

        % Semi-definite is refused when only the definite factorization was asked for, and a matrix
        % with a negative eigenvalue is refused either way.
        [T3, num3] = cholcov(Q, 0);
        assert(isempty(T3) && num3 == -1);
        [T4, num4] = cholcov([1 2; 2 1]);
        assert(isempty(T4) && num4 == -1);
    ");

    // --- Draws ----------------------------------------------------------------------------------------

    [Fact]
    public Task TheMultivariateDrawsTakeTheirSizesAndRepeatUnderASeed() => RunAsserting(@"
        rng(21);
        A = mvnrnd([1 2], [1 0.3; 0.3 2], 40);
        assert(isequal(size(A), [40 2]));
        rng(21);
        B = mvnrnd([1 2], [1 0.3; 0.3 2], 40);
        assert(isequal(A, B));
        rng(22);
        C = mvnrnd([1 2], [1 0.3; 0.3 2], 40);
        assert(~isequal(A, C));

        assert(isequal(size(mvnrnd([0 0 0], eye(3))), [1 3]));
        assert(isequal(size(mvtrnd([1 0.5; 0.5 1], 4, 7)), [7 2]));

        W = wishrnd(eye(3), 12);
        assert(isequal(size(W), [3 3]));
        assert(max(max(abs(W - W'))) < 1e-10);
        [W2, D] = wishrnd(eye(3), 12);
        assert(isequal(size(D), [3 3]));
        assert(isequal(size(W2), [3 3]));
        assert(isequal(size(iwishrnd(eye(3), 12)), [3 3]));
    ");

    /// <summary>Forty thousand draws carry the mean and covariance they were asked for.</summary>
    [Fact]
    public Task DrawsCarryTheMomentsTheyWereAskedFor() => RunAsserting(@"
        rng(2026);
        R = mvnrnd([3 -1], [4 1.2; 1.2 1], 40000);
        m = mean(R);
        assert(abs(m(1) - 3) < 0.05);
        assert(abs(m(2) + 1) < 0.05);
        C = cov(R);
        assert(abs(C(1,1) - 4) < 0.1);
        assert(abs(C(1,2) - 1.2) < 0.1);
        assert(abs(C(2,2) - 1) < 0.05);
    ");

    /// <summary>
    /// A Wishart draw has mean <c>df·sigma</c>, which is the property that says the factor was applied
    /// on the right side and not its transpose.
    /// </summary>
    [Fact]
    public Task AWishartDrawHasTheRightMean() => RunAsserting(@"
        rng(808);
        S = [2 0.5; 0.5 1];
        total = zeros(2, 2);
        for k = 1:3000
            total = total + wishrnd(S, 6);
        end
        average = total / 3000;
        assert(max(max(abs(average - 6*S))) < 0.5);
    ");

    // --- Kernel density ---------------------------------------------------------------------------------

    [Fact]
    public Task TheMultivariateKernelDensityIsAProductOfItsMarginals() => RunAsserting(@"
        x = [0 0; 1 1; 2 0; 0 2];
        f = mvksdensity(x, [1 1; 0 0], 'Bandwidth', [1 1]);
        assert(isequal(size(f), [2 1]));
        assert(all(f > 0));

        % One observation and a normal kernel: the estimate is the product of two normal densities.
        one = mvksdensity([0 0], [0.5 -0.5], 'Bandwidth', [1 2]);
        assert(abs(one - normpdf(0.5)*normpdf(-0.25)/2) < 1e-14);

        % A box kernel is flat inside one bandwidth and exactly zero outside it.
        near = mvksdensity([0 0], [0.5 0.5], 'Bandwidth', [1 1], 'Kernel', 'box');
        far = mvksdensity([0 0], [2 0], 'Bandwidth', [1 1], 'Kernel', 'box');
        assert(abs(near - 0.25) < 1e-14);
        assert(far == 0);

        % Weights count observations, so doubling one is the same as listing it twice.
        weighted = mvksdensity([0 0; 1 1], [0.5 0.5], 'Bandwidth', [1 1], 'Weights', [2 1]);
        listed = mvksdensity([0 0; 0 0; 1 1], [0.5 0.5], 'Bandwidth', [1 1]);
        assert(abs(weighted - listed) < 1e-14);
    ");

    // --- Sampling -----------------------------------------------------------------------------------

    [Fact]
    public Task SamplingTakesItsOptionsAndKeepsItsOrientation() => RunAsserting(@"
        rng(3);
        all5 = sort(randsample(5, 5)');
        assert(isequal(all5, 1:5));
        assert(isequal(size(randsample(9, 4)), [4 1]));
        assert(isequal(size(randsample([10 20 30], 2)), [1 2]));
        assert(isequal(size(randsample([10; 20; 30], 2)), [2 1]));

        drawn = randsample([10 20 30], 30, true);
        assert(all(ismember(drawn, [10 20 30])));

        % A weight of zero is never drawn, with replacement or without.
        weighted = randsample(4, 200, true, [1 0 1 1]);
        assert(~any(weighted == 2));
    ");

    [Fact]
    public Task DataSampleTakesRowsOrColumnsAndReportsWhichOnesItTook() => RunAsserting(@"
        rng(4);
        M = magic(4);
        [rows, idx] = datasample(M, 3);
        assert(isequal(size(rows), [3 4]));
        assert(numel(idx) == 3);
        assert(isequal(rows(1,:), M(idx(1),:)));

        columns = datasample(M, 2, 2);
        assert(isequal(size(columns), [4 2]));

        % Without replacement nothing repeats, and a vector is sampled along the way it runs.
        [taken, where] = datasample(1:6, 6, 'Replace', false);
        assert(isequal(sort(taken), 1:6));
        assert(isequal(sort(where), 1:6));
        assert(isequal(size(taken), [1 6]));
    ");

    [Fact]
    public Task GammaVariatesTakeTheirShapeAndSizes() => RunAsserting(@"
        rng(6);
        assert(isscalar(randg));
        assert(isequal(size(randg(2, 3, 4)), [3 4]));
        assert(isequal(size(randg([1 2 3])), [1 3]));
        big = randg(4, 1, 20000);
        assert(abs(mean(big) - 4) < 0.15);
        assert(abs(var(big) - 4) < 0.4);
        assert(all(big > 0));
    ");

    [Fact]
    public Task LatinDesignsFillEveryStratumAndTakeTheirOptions() => RunAsserting(@"
        rng(9);
        D = lhsdesign(5, 2, 'smooth', 'off');
        assert(isequal(size(D), [5 2]));
        assert(max(abs(sort(D(:,1))' - [0.1 0.3 0.5 0.7 0.9])) < 1e-12);
        assert(max(abs(sort(D(:,2))' - [0.1 0.3 0.5 0.7 0.9])) < 1e-12);

        S = lhsdesign(8, 3, 'criterion', 'maximin', 'iterations', 10);
        assert(isequal(size(S), [8 3]));
        assert(all(all(S > 0 & S < 1)));

        [X, Z] = lhsnorm([1 5], [1 0.5; 0.5 2], 200);
        assert(isequal(size(X), [200 2]));
        assert(isequal(size(Z), [200 2]));
        assert(abs(mean(X(:,1)) - 1) < 0.15);
        assert(abs(mean(X(:,2)) - 5) < 0.25);

        % Stratification means every decile of the first variable is occupied exactly once in ten.
        strata = floor(normcdf(X(:,1), 1, 1) * 200);
        assert(numel(unique(strata)) == 200);
    ");

    // --- Resampling ------------------------------------------------------------------------------------

    [Fact]
    public Task TheBootstrapResamplesRowsAndReportsWhichOnes() => RunAsserting(@"
        rng(11);
        data = (1:20)';
        bs = bootstrp(200, @mean, data);
        assert(isequal(size(bs), [200 1]));
        assert(abs(mean(bs) - 10.5) < 0.5);

        [~, sam] = bootstrp(15, @mean, data);
        assert(isequal(size(sam), [20 15]));
        assert(all(all(sam >= 1 & sam <= 20)));

        % Several data arguments are re-indexed by the same rows, so pairs stay paired: a statistic of
        % (x - y) over two identical columns is zero on every single resample.
        pairs = bootstrp(30, @(a, b) mean(a - b), data, data);
        assert(max(abs(pairs)) < 1e-12);

        % A statistic that answers several numbers gets a column each.
        wide = bootstrp(25, @(v) [mean(v) std(v)], data);
        assert(isequal(size(wide), [25 2]));
    ");

    [Fact]
    public Task TheBootstrapIntervalTakesBothSpellingsAndEveryType() => RunAsserting(@"
        rng(13);
        data = (1:30)';
        ci = bootci(300, @mean, data);
        assert(isequal(size(ci), [2 1]));
        assert(ci(1) < 15.5 && ci(2) > 15.5);

        gathered = bootci(300, {@mean, data}, 'alpha', 0.1, 'type', 'per');
        assert(isequal(size(gathered), [2 1]));
        assert(gathered(1) < gathered(2));

        for kind = {'norm', 'per', 'cper', 'bca'}
            interval = bootci(200, @mean, data, 'type', kind{1});
            assert(isequal(size(interval), [2 1]));
            assert(interval(1) < interval(2));
        end

        % A wider confidence level really is wider.
        narrow = bootci(400, @mean, data, 'alpha', 0.5, 'type', 'per');
        wide = bootci(400, @mean, data, 'alpha', 0.01, 'type', 'per');
        assert((wide(2) - wide(1)) > (narrow(2) - narrow(1)));

        % A statistic of several numbers gets a column each.
        both = bootci(200, @(v) [mean(v) median(v)], data, 'type', 'per');
        assert(isequal(size(both), [2 2]));
    ");

    /// <summary>
    /// The jackknife has a closed form for the mean — leaving out x moves it by (mean − x)/(n − 1) —
    /// so every one of its rows can be written out rather than merely bounded.
    /// </summary>
    [Fact]
    public Task TheJackknifeLeavesOutEachObservationInTurn() => RunAsserting(@"
        data = [2 4 6 8 10]';
        jk = jackknife(@mean, data);
        assert(isequal(size(jk), [5 1]));
        n = 5;
        for k = 1:n
            expected = (sum(data) - data(k)) / (n - 1);
            assert(abs(jk(k) - expected) < 1e-12);
        end
        assert(abs(mean(jk) - mean(data)) < 1e-12);
    ");

    [Fact]
    public Task CombinationsAreListedOnePerRow() => RunAsserting(@"
        c = combnk(1:4, 2);
        assert(isequal(size(c), [6 2]));
        assert(isequal(c(1,:), [1 2]));
        assert(isequal(c(end,:), [3 4]));
        assert(isequal(size(combnk(1:6, 3)), [20 3]));
        assert(isempty(combnk(1:3, 5)));
    ");

    // --- What is refused ----------------------------------------------------------------------------

    [Fact]
    public async Task WrongArgumentsAreRefusedByName()
    {
        Assert.Contains("more than 5 variables",
            await RunExpectingFailure("mvncdf(zeros(1,6), zeros(1,6), eye(6))"));
        Assert.Contains("more than 4 variables",
            await RunExpectingFailure("mvtcdf(zeros(1,5), eye(5), 4)"));
        Assert.Contains("the mean has 3 variables",
            await RunExpectingFailure("mvnpdf([1 2], [1 2 3], eye(2))"));
        Assert.Contains("positive definite",
            await RunExpectingFailure("mvnpdf([0 0], [0 0], [1 2; 2 1])"));
        Assert.Contains("needs 'Bandwidth'",
            await RunExpectingFailure("mvksdensity([1 2; 3 4], [1 2])"));
        Assert.Contains("'epanechnikov'",
            await RunExpectingFailure("mvksdensity([1 2; 3 4], [1 2], 'Bandwidth', 1, 'Kernel', 'gauss')"));
        Assert.Contains("'maximin'",
            await RunExpectingFailure("lhsdesign(4, 2, 'criterion', 'maximum')"));
        Assert.Contains("more than the population",
            await RunExpectingFailure("randsample(3, 5)"));
        Assert.Contains("more than the population",
            await RunExpectingFailure("datasample([1 2 3], 5, 'Replace', false)"));
        Assert.Contains("degrees of freedom",
            await RunExpectingFailure("wishrnd(eye(4), 2.5)"));
        Assert.Contains("observations",
            await RunExpectingFailure("bootstrp(10, @mean, (1:5)', (1:6)')"));
    }

    /// <summary>
    /// The studentized interval needs a bootstrap inside every bootstrap. It is refused with the
    /// reason and the alternatives rather than quietly given as one of the others.
    /// </summary>
    [Fact]
    public async Task TheStudentizedIntervalIsRefusedWithItsReason()
    {
        string message = await RunExpectingFailure("bootci(50, @mean, (1:8)', 'type', 'stud')");
        Assert.Contains("bootstrap inside every bootstrap", message);
        Assert.Contains("'bca'", message);
    }

    /// <summary>
    /// The whole surface reaches the JGS dialect too, where the only difference is that an index
    /// output starts at zero.
    /// </summary>
    [Fact]
    public void TheWaveReachesTheJgsDialect()
    {
        var context = new ScriptContext(_output, (number, figure) => _figures.Add((number, figure)));
        ScriptRunResult result = JgsRunner.Run(
            @"
            rng(5)
            let p = mvncdf([0, 0], [0, 0], [[1, 0.5], [0.5, 1]])
            assert(abs(p - (0.25 + asin(0.5)/(2*pi))) < 1e-10)
            let d = lhsdesign(4, 2)
            assert(isequal(size(d), [4, 2]))
            let picks = datasample([10, 20, 30, 40], 4, 'Replace', false)
            assert(isequal(sort(picks), [10, 20, 30, 40]))
            ",
            context,
            default,
            sourceId: "",
            hook: null,
            JgsDialect.Jgs);

        Assert.True(result.Success, result.Message + _output.ErrorText);
    }
}
