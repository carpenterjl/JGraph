using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The regression surface as a script sees it (M53 wave G): every documented output in MathWorks'
/// order, every option word taking effect, and every refusal naming what was wrong.
/// </summary>
/// <remarks>
/// The numbers are pinned by identities rather than by copied constants wherever one exists — a
/// penalized fit at no penalty is least squares, a two-category multinomial fit is a logistic
/// regression, <c>glmval</c> reproduces what <c>glmfit</c> fitted — because those are the checks a
/// plausible-looking wrong implementation fails.
/// </remarks>
[Collection("JG facade")]
public class MatlabRegressionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabRegressionTests() => JG.Reset();

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

    /// <summary>A response that is exactly linear in one predictor, with two others that are not.</summary>
    private const string Sample = """
        x = (1:10)';
        noise = [0.4 -0.3 0.2 -0.5 0.1 0.3 -0.2 0.5 -0.1 0.2]';
        y = 2 + 1.5*x + noise;
        X = [ones(10,1), x];
        """;

    // --- regress -------------------------------------------------------------------------------------

    [Fact]
    public Task Regress_AnswersFiveOutputsInTheDocumentedOrder() => RunAsserting(Sample + """
        [b, bint, r, rint, stats] = regress(y, X);
        assert(numel(b) == 2);
        assert(isequal(size(bint), [2 2]));
        assert(numel(r) == 10);
        assert(isequal(size(rint), [10 2]));
        assert(numel(stats) == 4);

        % Every interval brackets its own coefficient, and the residuals reproduce the response.
        assert(all(bint(:,1) < b) && all(b < bint(:,2)));
        assert(max(abs(y - X*b - r)) < 1e-10);
        assert(stats(1) > 0.99);
        assert(stats(3) < 1e-8);
        assert(abs(stats(4) - sum(r.^2)/8) < 1e-12);
        """);

    [Fact]
    public Task Regress_TheLevelWidensEveryInterval() => RunAsserting(Sample + """
        [~, wide] = regress(y, X, 0.20);
        [~, narrow] = regress(y, X, 0.001);
        assert(all(narrow(:,2) - narrow(:,1) > wide(:,2) - wide(:,1)));
        """);

    [Fact]
    public Task Regress_AResidualIntervalClearOfZeroMarksTheOutlier() => RunAsserting("""
        x = (1:12)';
        y = 3 + 0.5*x;
        y(8) = y(8) + 5;
        [~, ~, ~, rint] = regress(y, [ones(12,1), x]);
        clear_of_zero = (rint(:,1) > 0) | (rint(:,2) < 0);
        assert(sum(clear_of_zero) == 1);
        assert(clear_of_zero(8));
        """);

    [Fact]
    public Task Regress_WithNoInterceptColumn_FitsThroughTheOrigin() => RunAsserting("""
        x = (1:6)';
        b = regress(2*x, x);
        assert(abs(b - 2) < 1e-10);
        """);

    // --- regstats ------------------------------------------------------------------------------------

    [Fact]
    public Task Regstats_AnswersEveryDocumentedFieldFromOneFit() => RunAsserting(Sample + """
        s = regstats(y, x, 'linear');
        assert(numel(s.beta) == 2);
        assert(isequal(size(s.covb), [2 2]));
        assert(isequal(size(s.hatmat), [10 10]));
        assert(isequal(size(s.beta_i), [10 2]));
        assert(isequal(size(s.dfbetas), [10 2]));
        assert(numel(s.cookd) == 10);
        assert(abs(sum(s.leverage) - 2) < 1e-10);
        assert(s.rsquare > s.adjrsquare);
        assert(abs(s.mse - sum(s.r.^2)/8) < 1e-12);
        assert(s.tstat.dfe == 8);
        assert(s.fstat.dfr == 1);
        assert(s.dwstat.dw > 0 && s.dwstat.dw < 4);
        assert(isequal(size(s.Q), [10 2]));
        assert(isequal(size(s.R), [2 2]));
        """);

    [Fact]
    public Task Regstats_ANamedStatisticIsAnsweredOnItsOwn() => RunAsserting(Sample + """
        b = regstats(y, x, 'linear', 'beta');
        assert(numel(b) == 2);

        few = regstats(y, x, 'linear', {'beta', 'mse', 'rsquare'});
        assert(abs(few.mse - regstats(y, x, 'linear', 'mse')) < 1e-12);
        assert(few.rsquare > 0.99);
        """);

    [Fact]
    public Task Regstats_TheModelWordChoosesHowManyColumnsAreFitted() => RunAsserting("""
        P = [1 4; 2 1; 3 9; 4 2; 5 7; 6 3; 7 8; 8 5];
        z = P(:,1) + 0.3*P(:,2) + 0.1*P(:,1).*P(:,2);
        linear = regstats(z, P, 'linear', 'beta');
        interaction = regstats(z, P, 'interaction', 'beta');
        quadratic = regstats(z, P, 'quadratic', 'beta');
        pure = regstats(z, P, 'purequadratic', 'beta');
        assert(numel(linear) == 3);
        assert(numel(interaction) == 4);
        assert(numel(quadratic) == 6);
        assert(numel(pure) == 5);

        % The interaction model reaches this response exactly; the linear one cannot.
        assert(regstats(z, P, 'interaction', 'mse') < 1e-18);
        assert(regstats(z, P, 'linear', 'mse') > 1e-6);
        """);

    // --- leverage, ridge, x2fx, dummyvar ---------------------------------------------------------------

    [Fact]
    public Task Leverage_SumsToTheNumberOfFittedColumns() => RunAsserting("""
        P = [1 4; 2 1; 3 9; 4 2; 5 7; 6 3];
        assert(abs(sum(leverage(P)) - 3) < 1e-10);
        assert(abs(sum(leverage(P, 'quadratic')) - 6) < 1e-10);
        assert(numel(leverage(P)) == 6);
        """);

    [Fact]
    public Task Ridge_ShrinksWithThePenaltyAndRestoresTheOriginalScale() => RunAsserting("""
        P = [1 4; 2 1; 3 9; 4 2; 5 7; 6 3; 7 8; 8 5];
        z = 2 + 3*P(:,1) - P(:,2);
        scaled = ridge(z, P, [0 1 10 500]);
        assert(isequal(size(scaled), [2 4]));
        assert(abs(scaled(1,4)) < abs(scaled(1,1)));
        assert(abs(scaled(2,4)) < abs(scaled(2,1)));

        % Unscaled at no penalty is ordinary least squares, intercept first.
        restored = ridge(z, P, 0, 0);
        ordinary = regress(z, [ones(8,1), P]);
        assert(numel(restored) == 3);
        assert(max(abs(restored - ordinary)) < 1e-8);
        """);

    [Fact]
    public Task X2fx_BuildsTheTermsTheModelNames() => RunAsserting("""
        D = x2fx([2 3; 5 7], 'quadratic');
        assert(isequal(D(1,:), [1 2 3 6 4 9]));
        assert(isequal(size(x2fx([2 3; 5 7])), [2 3]));
        assert(isequal(size(x2fx([2 3; 5 7], 'interaction')), [2 4]));
        assert(isequal(size(x2fx([2 3; 5 7], 'purequadratic')), [2 5]));

        % Written-out exponents: the intercept, a cube, and a cross term.
        E = x2fx([2 3], [0 0; 3 0; 1 2]);
        assert(isequal(E, [1 8 18]));

        % A categorical predictor contributes one indicator per level but the last.
        C = x2fx([1 5; 2 6; 3 7; 1 8], 'linear', 1);
        assert(size(C, 2) == 4);
        """);

    [Fact]
    public Task Dummyvar_MakesOneIndicatorPerLevelOfEveryColumn() => RunAsserting("""
        D = dummyvar([1 2; 3 1; 2 2]);
        assert(isequal(size(D), [3 5]));
        assert(all(sum(D, 2) == 2));
        assert(isequal(D(2,:), [0 0 1 1 0]));
        assert(isequal(size(dummyvar([1; 2; 2; 3])), [4 3]));
        """);

    // --- polyconf and invpred -----------------------------------------------------------------------

    [Fact]
    public Task Polyconf_EvaluatesThePolynomialAndBoundsIt() => RunAsserting("""
        x = (1:8)';
        y = [1; 3; 4; 9; 14; 20; 33; 41];
        [p, S] = polyfit(x, y, 2);
        [yhat, delta] = polyconf(p, x, S);
        assert(numel(yhat) == 8);
        assert(all(delta > 0));
        assert(max(abs(yhat - polyval(p, x))) < 1e-10);

        [~, curve] = polyconf(p, x, S, 'predopt', 'curve');
        [~, together] = polyconf(p, x, S, 'simopt', 'on');
        [~, tighter] = polyconf(p, x, S, 'alpha', 0.5);
        assert(all(curve < delta));
        assert(all(together > delta));
        assert(all(tighter < delta));
        """);

    [Fact]
    public Task Invpred_FindsTheXThatWouldHaveProducedY0() => RunAsserting("""
        x = (1:8)';
        y = 1 + 2.1*x + [0.1 -0.1 0.2 -0.2 0.05 -0.05 0.1 -0.1]';
        [x0, dlo, dhi] = invpred(x, y, 10);
        b = regress(y, [ones(8,1), x]);
        assert(abs(b(1) + b(2)*x0 - 10) < 1e-8);
        assert(dlo > 0 && dhi > 0);

        [~, curveLo, curveHi] = invpred(x, y, 10, 'predopt', 'curve');
        assert(curveLo + curveHi < dlo + dhi);
        """);

    // --- robustfit -----------------------------------------------------------------------------------

    [Fact]
    public Task Robustfit_IgnoresTheOutlierThatDragsAnOrdinaryFit() => RunAsserting("""
        x = (1:10)';
        y = 1 + 2*x;
        y(9) = y(9) + 30;
        ordinary = regress(y, [ones(10,1), x]);
        [b, stats] = robustfit(x, y);
        assert(abs(ordinary(2) - 2) > 0.3);
        assert(abs(b(2) - 2) < 1e-3);
        assert(stats.w(9) < 1e-6);
        assert(all(stats.w([1:8 10]) > 0.99));
        assert(stats.dfe == 8);
        assert(numel(stats.se) == 2);
        assert(isequal(size(stats.covb), [2 2]));
        assert(numel(stats.resid) == 10);
        assert(numel(stats.h) == 10);
        """);

    [Fact]
    public Task Robustfit_TakesAllNineWeightFunctionsAndOlsIsLeastSquares() => RunAsserting("""
        x = (1:12)';
        y = 4 - 0.75*x;
        y(9) = y(9) + 25;
        names = {'andrews', 'bisquare', 'cauchy', 'fair', 'huber', 'logistic', 'talwar', 'welsch'};
        for k = 1:numel(names)
            b = robustfit(x, y, names{k});
            assert(abs(b(2) + 0.75) < 0.25);
        end

        ols = robustfit(x, y, 'ols');
        plain = regress(y, [ones(12,1), x]);
        assert(max(abs(ols - plain)) < 1e-9);
        """);

    [Fact]
    public Task Robustfit_TheTuningConstantAndTheInterceptWordBothTakeEffect() => RunAsserting("""
        x = (1:10)';
        y = 1 + 2*x;
        y(4) = y(4) + 8;
        tight = robustfit(x, y, 'huber', 0.5);
        loose = robustfit(x, y, 'huber', 20);
        assert(abs(tight(2) - 2) < abs(loose(2) - 2));

        through = robustfit(x, y, 'bisquare', [], 'off');
        assert(numel(through) == 1);
        """);

    // --- glmfit and glmval -----------------------------------------------------------------------------

    [Fact]
    public Task Glmfit_WithANormalResponse_IsLeastSquares() => RunAsserting(Sample + """
        [b, dev] = glmfit(x, y, 'normal');
        ordinary = regress(y, X);
        assert(max(abs(b - ordinary)) < 1e-8);
        assert(abs(dev - sum((y - X*ordinary).^2)) < 1e-8);
        """);

    [Fact]
    public Task Glmfit_PoissonWithNoPredictor_AnswersTheLogOfTheMean() => RunAsserting("""
        counts = [2; 5; 3; 8; 4; 6];
        b = glmfit(zeros(6,0), counts, 'poisson');
        assert(abs(b - log(mean(counts))) < 1e-8);
        """);

    [Fact]
    public Task Glmfit_BinomialTakesACountBesideItsTrials() => RunAsserting("""
        xb = [0; 1];
        trials = [10 10]';
        successes = [2; 8];
        [b, dev, stats] = glmfit(xb, [successes trials], 'binomial');
        assert(abs(b(1) - log(0.2/0.8)) < 1e-5);
        assert(abs(b(2) - 2*log(4)) < 1e-5);
        assert(dev < 1e-8);
        assert(~stats.estdisp);
        assert(abs(stats.s - 1) < 1e-12);
        assert(numel(stats.residp) == 2);
        assert(numel(stats.resida) == 2);
        """);

    [Fact]
    public Task Glmfit_EveryLinkAndEveryFamilyIsAccepted() => RunAsserting("""
        xb = [1; 2; 3; 4; 5; 6];
        prop = [0.1; 0.2; 0.4; 0.6; 0.8; 0.9];
        for link = {'logit', 'probit', 'comploglog', 'loglog'}
            b = glmfit(xb, prop, 'binomial', 'link', link{1});
            assert(numel(b) == 2);
        end

        positive = [1.2; 2.1; 3.4; 4.2; 5.9; 6.1];
        assert(numel(glmfit(xb, positive, 'gamma')) == 2);
        assert(numel(glmfit(xb, positive, 'inverse gaussian')) == 2);
        assert(numel(glmfit(xb, positive, 'normal', 'link', 'log')) == 2);
        assert(numel(glmfit(xb, positive, 'normal', 'link', -2)) == 2);
        """);

    [Fact]
    public Task Glmfit_TheOffsetAndTheWeightsAndTheConstantWordAllTakeEffect() => RunAsserting("""
        xb = (1:5)';
        counts = [3; 7; 12; 20; 33];
        exposure = log((1:5)');
        b = glmfit(xb, counts, 'poisson', 'offset', exposure);
        assert(numel(b) == 2);

        weighted = glmfit(xb, counts, 'poisson', 'weights', [1;1;1;1;5]);
        plain = glmfit(xb, counts, 'poisson');
        assert(abs(weighted(2) - plain(2)) > 1e-6);

        through = glmfit(xb, counts, 'poisson', 'constant', 'off');
        assert(numel(through) == 1);

        spread = glmfit(xb, counts, 'poisson', 'estdisp', 'on');
        assert(numel(spread) == 2);
        """);

    [Fact]
    public Task Glmval_ReproducesTheFitAndBendsItsBand() => RunAsserting("""
        xb = (0:4)';
        counts = [1; 3; 4; 9; 14];
        [b, ~, stats] = glmfit(xb, counts, 'poisson');
        yhat = glmval(b, xb, 'log');
        [y2, lo, hi] = glmval(b, xb, 'log', stats);
        assert(max(abs(yhat - y2)) < 1e-12);
        assert(all(lo > 0) && all(hi > 0));

        % The band is symmetric where it is drawn and bent by the link, so the halves differ.
        assert(max(abs(lo - hi)) > 1e-6);

        [~, wide] = glmval(b, xb, 'log', stats, 'confidence', 0.999);
        assert(all(wide > lo));

        [~, together] = glmval(b, xb, 'log', stats, 'simultaneous', 'on');
        assert(all(together > lo));

        counts10 = glmval([log(0.25/0.75); 0], [0; 0], 'logit', 'size', 40);
        assert(max(abs(counts10 - 10)) < 1e-8);
        """);

    // --- stepwisefit ---------------------------------------------------------------------------------

    [Fact]
    public Task Stepwisefit_TakesTheRealPredictorAndLeavesTheRest() => RunAsserting("""
        P = [1 7 2; 2 3 9; 3 8 4; 4 1 7; 5 6 1; 6 2 8; 7 9 3; 8 4 6; 9 5 5; 10 0 2; 11 3 7; 12 6 4];
        z = 5 + 4*P(:,1);
        [b, se, pval, inmodel, stats, nextstep, history] = stepwisefit(P, z);
        assert(inmodel(1));
        assert(~inmodel(2) && ~inmodel(3));
        assert(abs(b(1) - 4) < 1e-6);
        assert(all(se >= 0));
        assert(pval(1) < 1e-6);
        assert(nextstep == 0);
        assert(abs(stats.intercept - 5) < 1e-6);
        assert(stats.df0 == 1);
        assert(stats.dfe == 10);
        assert(isequal(size(stats.xr), [12 3]));
        assert(isequal(size(history.in), [1 3]));
        assert(history.in(1,1) && ~history.in(1,2) && ~history.in(1,3));
        assert(numel(history.rmse) == 1);
        assert(sum(history.df0) == 1);
        """);

    [Fact]
    public Task Stepwisefit_TheEntryLevelAndTheKeptTermsBothTakeEffect() => RunAsserting("""
        P = [1 7; 2 3; 3 8; 4 1; 5 6; 6 2; 7 9; 8 4];
        z = 2*P(:,1) + [0.5 -0.4 0.3 -0.6 0.2 0.4 -0.3 0.5]';
        strict = stepwisefit(P, z, 'penter', 1e-12, 'premove', 1e-6);
        [~, ~, ~, kept] = stepwisefit(P, z, 'keep', [false true]);
        [~, ~, ~, started] = stepwisefit(P, z, 'inmodel', [true false]);
        assert(numel(strict) == 2);
        assert(kept(2));
        assert(started(1));
        """);

    // --- nlinfit and its intervals ---------------------------------------------------------------------

    [Fact]
    public Task Nlinfit_RecoversTheParametersAndReportsFiveOutputs() => RunAsserting("""
        t = (0:0.5:4)';
        y = 2.5*exp(-0.7*t);
        model = @(b, x) b(1)*exp(b(2)*x);
        [beta, R, J, CovB, MSE] = nlinfit(t, y, model, [1; -0.1]);
        assert(abs(beta(1) - 2.5) < 1e-5);
        assert(abs(beta(2) + 0.7) < 1e-5);
        assert(numel(R) == 9);
        assert(isequal(size(J), [9 2]));
        assert(isequal(size(CovB), [2 2]));
        assert(MSE < 1e-20);

        % The parameters come back shaped the way they went in.
        row = nlinfit(t, y, model, [1 -0.1]);
        assert(size(row, 1) == 1 && size(row, 2) == 2);
        """);

    [Fact]
    public Task Nlinfit_TakesWeightsAnOptionsStructureAndARobustWeightFunction() => RunAsserting("""
        t = (0:0.5:4)';
        y = 2.5*exp(-0.7*t);
        model = @(b, x) b(1)*exp(b(2)*x);

        weighted = nlinfit(t, y, model, [1; -0.1], 'Weights', ones(9,1));
        assert(abs(weighted(1) - 2.5) < 1e-5);

        settings = struct('MaxIter', 400, 'TolFun', 1e-14, 'TolX', 1e-14);
        tight = nlinfit(t, y, model, [1; -0.1], settings);
        assert(abs(tight(2) + 0.7) < 1e-6);

        spoiled = y;
        spoiled(3) = spoiled(3) + 5;
        plain = nlinfit(t, spoiled, model, [1; -0.1]);
        robust = nlinfit(t, spoiled, model, [1; -0.1], struct('RobustWgtFun', 'bisquare'));
        assert(abs(plain(1) - 2.5) > 0.5);
        assert(abs(robust(1) - 2.5) < 1e-4);
        assert(abs(robust(2) + 0.7) < 1e-4);
        """);

    [Fact]
    public Task Nlinfit_TheErrorModelChangesWhatIsWeighted() => RunAsserting("""
        t = (1:8)';
        y = 3*exp(0.2*t);
        model = @(b, x) b(1)*exp(b(2)*x);
        constant = nlinfit(t, y, model, [1; 0.1], 'ErrorModel', 'constant');
        proportional = nlinfit(t, y, model, [1; 0.1], 'ErrorModel', 'proportional');
        exponential = nlinfit(t, y, model, [1; 0.1], 'ErrorModel', 'exponential');
        assert(abs(constant(1) - 3) < 1e-4);
        assert(abs(proportional(1) - 3) < 1e-4);
        assert(abs(exponential(2) - 0.2) < 1e-6);
        """);

    [Fact]
    public Task Nlparci_And_Nlpredci_BuildTheirIntervalsFromTheFit() => RunAsserting("""
        t = (0:0.5:4)';
        y = 2.5*exp(-0.7*t) + [0.05 -0.04 0.03 -0.02 0.02 -0.03 0.01 -0.01 0.02]';
        model = @(b, x) b(1)*exp(b(2)*x);
        [beta, R, J, CovB] = nlinfit(t, y, model, [1; -0.1]);

        ci = nlparci(beta, R, 'jacobian', J);
        assert(isequal(size(ci), [2 2]));
        assert(all(ci(:,1) < beta) && all(beta < ci(:,2)));

        fromCovar = nlparci(beta, R, 'covar', CovB);
        assert(max(abs(ci(:) - fromCovar(:))) < 1e-8);

        tighter = nlparci(beta, R, 'jacobian', J, 'alpha', 0.001);
        assert(all(tighter(:,2) - tighter(:,1) > ci(:,2) - ci(:,1)));

        [yp, delta] = nlpredci(model, t, beta, R, 'Jacobian', J);
        assert(max(abs(yp - model(beta, t))) < 1e-12);
        assert(all(delta > 0));

        [~, observed] = nlpredci(model, t, beta, R, 'Jacobian', J, 'predopt', 'observation');
        [~, together] = nlpredci(model, t, beta, R, 'Jacobian', J, 'simopt', 'on');
        assert(all(observed > delta));
        assert(all(together > delta));
        """);

    [Fact]
    public Task Hougen_IsTheDocumentedRateExpression() => RunAsserting("""
        b = [1.25 0.06 0.04 0.11 1.19];
        r = hougen(b, [470 300 10]);
        expected = (1.25*300 - 10/1.19) / (1 + 0.06*470 + 0.04*300 + 0.11*10);
        assert(abs(r - expected) < 1e-10);
        assert(numel(hougen(b, [470 300 10; 285 80 10])) == 2);
        """);

    // --- lasso and lassoglm ---------------------------------------------------------------------------

    [Fact]
    public Task Lasso_WalksTheWholePathFromEverythingToNothing() => RunAsserting("""
        P = [1 5 2; 2 3 9; 3 8 4; 4 1 7; 5 6 1; 6 2 8; 7 9 3; 8 4 6; 9 7 5; 10 0 2];
        z = 2 + 3*P(:,1);
        [B, info] = lasso(P, z, 'NumLambda', 25, 'LambdaRatio', 1e-3);
        assert(isequal(size(B), [3 25]));
        assert(numel(info.Lambda) == 25);
        assert(all(diff(info.Lambda) > 0));
        assert(all(diff(info.DF) <= 0));
        assert(info.DF(25) == 0);
        assert(abs(info.Intercept(25) - mean(z)) < 1e-8);
        assert(info.Alpha == 1);
        assert(numel(info.MSE) == 25);
        """);

    [Fact]
    public Task Lasso_WithNoPenaltyIsLeastSquaresAndAMixingKeepsEverything() => RunAsserting("""
        P = [1 5; 2 3; 3 8; 4 1; 5 6; 6 2; 7 9; 8 4];
        z = 1 + 2*P(:,1) - 0.5*P(:,2);
        [B, info] = lasso(P, z, 'Lambda', 0);
        ordinary = regress(z, [ones(8,1), P]);
        assert(max(abs(B - ordinary(2:3))) < 1e-5);
        assert(abs(info.Intercept - ordinary(1)) < 1e-4);

        ridgeLike = lasso(P, z, 'Alpha', 0.001, 'Lambda', 50);
        assert(all(ridgeLike ~= 0));

        capped = lasso(P, z, 'NumLambda', 30, 'DFmax', 1);
        assert(size(capped, 2) == 30);

        unscaled = lasso(P, z, 'Lambda', 1, 'Standardize', false);
        assert(numel(unscaled) == 2);
        """);

    [Fact]
    public Task Lassoglm_TakesTheFamilyAndReportsADeviance() => RunAsserting("""
        P = [1 2; 2 1; 3 5; 4 3; 5 4; 6 6; 7 2; 8 8];
        counts = [1; 2; 4; 6; 9; 14; 20; 31];
        [B, info] = lassoglm(P, counts, 'poisson', 'NumLambda', 15);
        assert(isequal(size(B), [2 15]));
        assert(info.DF(15) == 0);
        assert(abs(info.Intercept(15) - log(mean(counts))) < 1e-3);
        assert(info.Deviance(1) < info.Deviance(15));

        prop = [0.1; 0.2; 0.4; 0.5; 0.6; 0.7; 0.85; 0.9];
        Bb = lassoglm(P, prop, 'binomial', 'NumLambda', 10, 'Alpha', 0.5);
        assert(size(Bb, 2) == 10);
        """);

    // --- plsregress ------------------------------------------------------------------------------------

    [Fact]
    public Task Plsregress_WithEveryComponentIsLeastSquares() => RunAsserting("""
        P = [1 5 2; 2 3 9; 3 8 4; 4 1 7; 5 6 1; 6 2 8; 7 9 3; 8 4 6; 9 7 5; 10 0 2];
        z = 3 + 2*P(:,1) - P(:,2) + 0.5*P(:,3);
        [XL, YL, XS, YS, BETA, PCTVAR, MSE, stats] = plsregress(P, z, 3);
        assert(isequal(size(XL), [3 3]));
        assert(isequal(size(YL), [1 3]));
        assert(isequal(size(XS), [10 3]));
        assert(isequal(size(YS), [10 3]));
        assert(isequal(size(BETA), [4 1]));
        assert(isequal(size(PCTVAR), [2 3]));
        assert(isequal(size(MSE), [2 4]));

        ordinary = regress(z, [ones(10,1), P]);
        assert(max(abs(BETA - ordinary)) < 1e-6);
        assert(abs(sum(PCTVAR(2,:)) - 1) < 1e-8);
        assert(MSE(2,4) < 1e-18);
        assert(isequal(size(stats.W), [3 3]));
        assert(numel(stats.T2) == 10);
        """);

    [Fact]
    public Task Plsregress_FewerComponentsLeaveMoreOver() => RunAsserting("""
        P = [1 5; 2 3; 3 8; 4 1; 5 6; 6 2; 7 9; 8 4];
        Z = [P(:,1) + P(:,2), P(:,1) - P(:,2)];
        [~, ~, ~, ~, ~, ~, one] = plsregress(P, Z, 1);
        [~, ~, ~, ~, ~, ~, two] = plsregress(P, Z, 2);

        % Each column of the table uses one more component than the last, and both components
        % together reach this response exactly.
        assert(size(one, 2) == 2 && size(two, 2) == 3);
        assert(one(2,1) > one(2,2));
        assert(abs(one(2,2) - two(2,2)) < 1e-12);
        assert(two(2,3) < 1e-18);
        """);

    // --- mnrfit and mnrval ------------------------------------------------------------------------------

    [Fact]
    public Task Mnrfit_WithTwoCategoriesIsALogisticRegression() => RunAsserting("""
        xm = (1:8)';
        counts = [8 2; 7 3; 6 4; 5 5; 4 6; 3 7; 2 8; 1 9];
        [B, dev, stats] = mnrfit(xm, counts);
        trials = sum(counts, 2);
        [b, gdev] = glmfit(xm, [counts(:,1) trials], 'binomial');
        assert(isequal(size(B), [2 1]));
        assert(max(abs(B - b)) < 1e-4);
        assert(abs(dev - gdev) < 1e-4);
        assert(numel(stats.se) == 2);
        assert(stats.p(2) < 0.05);
        """);

    [Fact]
    public Task Mnrfit_TheThreeModelsHaveTheShapesTheyDocument() => RunAsserting("""
        x3 = (1:6)';
        counts = [6 3 1; 5 4 1; 4 4 2; 2 5 3; 1 4 5; 1 2 7];
        nominal = mnrfit(x3, counts);
        ordinal = mnrfit(x3, counts, 'model', 'ordinal');
        stepped = mnrfit(x3, counts, 'model', 'hierarchical');
        assert(isequal(size(nominal), [2 2]));
        assert(numel(ordinal) == 3);
        assert(numel(stepped) == 3);

        % The ordered model's cut points must increase, or a category would have negative probability.
        assert(ordinal(2) > ordinal(1));

        probit = mnrfit(x3, counts, 'model', 'ordinal', 'link', 'probit');
        assert(numel(probit) == 3);
        shared = mnrfit(x3, counts, 'interactions', 'off');
        assert(numel(shared) == 3);
        """);

    [Fact]
    public Task Mnrfit_TakesACategoryPerObservationAsWellAsCounts() => RunAsserting("""
        x = [1; 2; 3; 4; 5; 6; 7; 8];
        which = [1; 1; 1; 2; 2; 2; 3; 3];
        B = mnrfit(x, which);
        assert(isequal(size(B), [2 2]));
        """);

    [Fact]
    public Task Mnrval_AnswersProbabilitiesThatSumToOne() => RunAsserting("""
        x3 = (1:6)';
        counts = [6 3 1; 5 4 1; 4 4 2; 2 5 3; 1 4 5; 1 2 7];
        [B, ~, stats] = mnrfit(x3, counts);
        p = mnrval(B, x3);
        assert(isequal(size(p), [6 3]));
        assert(max(abs(sum(p, 2) - 1)) < 1e-9);

        Bo = mnrfit(x3, counts, 'model', 'ordinal');
        c = mnrval(Bo, x3, 'model', 'ordinal', 'type', 'cumulative');
        assert(isequal(size(c), [6 2]));
        assert(all(c(:,2) >= c(:,1) - 1e-12));
        assert(all(c(:) <= 1 + 1e-12));

        k = mnrval(Bo, x3, 'model', 'ordinal', 'type', 'conditional');
        assert(all(k(:) >= 0) && all(k(:) <= 1 + 1e-12));

        [~, lo, hi] = mnrval(B, x3, stats);
        assert(isequal(size(lo), [6 3]));
        assert(all(lo(:) >= 0) && isequal(lo, hi));
        """);

    // --- mvregress and mvregresslike ---------------------------------------------------------------------

    [Fact]
    public Task Mvregress_WithOneDesignFitsEachResponseAndReportsFiveOutputs() => RunAsserting("""
        D = [ones(6,1) (1:6)'];
        Y = [2.0 5.5; 4.1 5.0; 5.9 4.4; 8.2 4.1; 9.8 3.4; 12.1 3.0];
        [beta, Sigma, E, CovB, logL] = mvregress(D, Y);
        assert(isequal(size(beta), [2 2]));
        assert(isequal(size(Sigma), [2 2]));
        assert(isequal(size(E), [6 2]));
        assert(isequal(size(CovB), [4 4]));

        % A shared design means each response is its own ordinary fit.
        assert(max(abs(beta(:,1) - regress(Y(:,1), D))) < 1e-7);
        assert(max(abs(beta(:,2) - regress(Y(:,2), D))) < 1e-7);
        assert(abs(Sigma(1,2) - sum(E(:,1).*E(:,2))/6) < 1e-9);

        nlogL = mvregresslike(D, Y, beta(:), Sigma);
        assert(abs(nlogL + logL) < 1e-9);
        """);

    [Fact]
    public Task Mvregress_TakesOneDesignPerObservationAsACell() => RunAsserting("""
        Y = [1 2; 3 1; 6 4; 9 3; 13 6];
        designs = cell(5,1);
        for i = 1:5
            designs{i} = [1 i; 1 -i];
        end
        [beta, Sigma] = mvregress(designs, Y);
        assert(numel(beta) == 2);
        assert(isequal(size(Sigma), [2 2]));
        """);

    [Fact]
    public Task Mvregresslike_ReportsBothVarianceFormats() => RunAsserting("""
        D = [ones(5,1) (1:5)'];
        Y = [1 2; 3 1; 6 4; 9 3; 13 6];
        [beta, Sigma] = mvregress(D, Y);
        [~, justBeta] = mvregresslike(D, Y, beta(:), Sigma);
        [~, everything] = mvregresslike(D, Y, beta(:), Sigma, 'ecm', 'varformat', 'full');
        assert(isequal(size(justBeta), [4 4]));
        assert(isequal(size(everything), [7 7]));

        % Nothing beats the maximum likelihood answer.
        nudged = beta(:);
        nudged(1) = nudged(1) + 0.5;
        assert(mvregresslike(D, Y, nudged, Sigma) > mvregresslike(D, Y, beta(:), Sigma));
        """);

    // --- coxphfit ----------------------------------------------------------------------------------------

    [Fact]
    public Task Coxphfit_ReportsFourOutputsAndAClimbingHazard() => RunAsserting("""
        xc = [1;0;1;0;1;0;1;0;1;0];
        tc = (1:10)';
        [b, logl, H, stats] = coxphfit(xc, tc);
        assert(numel(b) == 1);
        assert(logl < 0);
        assert(isequal(size(H), [10 2]));
        assert(all(diff(H(:,1)) > 0));
        assert(all(diff(H(:,2)) > 0));
        assert(numel(stats.se) == 1);
        assert(numel(stats.martres) == 10);
        assert(numel(stats.devres) == 10);
        assert(isequal(size(stats.schres), [10 1]));
        assert(isequal(size(stats.covb), [1 1]));
        """);

    [Fact]
    public Task Coxphfit_FailingFirstRaisesTheHazardAndCensoringDoesNot() => RunAsserting("""
        marker = [1;1;1;1;1;0;0;0;0;0];
        times = (1:10)';
        early = coxphfit(marker, times);
        assert(early > 1);

        % Everyone still going at the end contributes no failure of their own.
        censored = [0;0;0;0;0;1;1;1;1;1];
        [~, ~, H] = coxphfit(marker, times, 'censoring', censored);
        assert(size(H, 1) == 5);
        """);

    [Fact]
    public Task Coxphfit_TheTwoTieRulesAgreeWithoutTiesAndDifferWithThem() => RunAsserting("""
        x = [1;0;1;0;1;0];
        distinct = (1:6)';
        breslow = coxphfit(x, distinct, 'ties', 'breslow');
        efron = coxphfit(x, distinct, 'ties', 'efron');
        assert(abs(breslow - efron) < 1e-6);

        tied = [1;1;2;2;3;3];
        b2 = coxphfit([1;1;1;0;0;0], tied, 'ties', 'breslow');
        e2 = coxphfit([1;1;1;0;0;0], tied, 'ties', 'efron');
        assert(abs(e2) > abs(b2));
        """);

    [Fact]
    public Task Coxphfit_TakesFrequenciesABaselineAndAStartingPoint() => RunAsserting("""
        x = [1;0;1;0;1;0];
        t = (1:6)';
        plain = coxphfit(x, t);
        weighted = coxphfit(x, t, 'frequency', [1;1;1;1;1;3]);
        assert(abs(plain - weighted) > 1e-8);

        [~, ~, H0] = coxphfit(x, t, 'baseline', 0);
        [~, ~, Hm] = coxphfit(x, t);
        assert(abs(H0(end,2) - Hm(end,2)) > 1e-12);

        started = coxphfit(x, t, 'init', 0.5);
        assert(abs(started - plain) < 1e-6);
        """);

    // --- refusals ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("regress(1)", "regress")]
    [InlineData("regstats([1;2;3], [1;2;3], 'cubic')", "cubic")]
    [InlineData("regstats([1;2;3], [1;2;3], 'linear', 'nonsense')", "nonsense")]
    [InlineData("ridge([1;2;3], [1;2;3], -1)", "negative")]
    [InlineData("x2fx([1 2], 'quadratik')", "quadratik")]
    [InlineData("dummyvar([1.5; 2])", "whole numbers")]
    [InlineData("robustfit([1;2;3], [1;2;3], 'bisquared')", "bisquared")]
    [InlineData("glmfit([1;2], [1;2], 'gaussian')", "gaussian")]
    [InlineData("glmfit([1;2], [0.5; 1.5], 'binomial')", "proportion")]
    [InlineData("glmfit([1;2], [1;2], 'poisson', 'link', 'sigmoid')", "sigmoid")]
    [InlineData("stepwisefit([1;2;3], [1;2;3], 'penter', 0.2, 'premove', 0.1)", "for ever")]
    [InlineData("nlinfit([1;2], [1;2], 3, [1])", "function")]
    [InlineData("nlparci([1;2], [1;2;3])", "'covar'")]
    [InlineData("hougen([1 2 3], [1 2 3])", "five parameters")]
    [InlineData("lasso([1;2;3], [1;2;3], 'Alpha', 0)", "mixing")]
    [InlineData("lasso([1;2;3], [1;2;3], 'CV', 5)", "cross-validation")]
    [InlineData("plsregress([1 2; 3 4; 5 7], [1;2;3], 3)", "between 1 and 2")]
    [InlineData("mnrfit([1;2], [1 1; 2 0], 'link', 'probit')", "nominal model")]
    [InlineData("mnrval([1;2], [1;2], 'type', 'marginal')", "'type'")]
    [InlineData("mvregress([1 2; 3 4], [1 2; 3 4; 5 6])", "observations")]
    [InlineData("coxphfit([1;0;1], [1;2;3], 'ties', 'exact')", "'ties'")]
    [InlineData("coxphfit([1;0;1], [1;2;3], 'censoring', [1;0])", "observations")]
    public async Task EveryMisuseIsRefusedByName(string code, string expected) =>
        Assert.Contains(expected, await RunExpectingFailure(code), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Two things this surface does not compute are refused by name rather than answered with
    /// something else under the documented name.
    /// </summary>
    [Fact]
    public async Task WhatIsNotComputedSaysSoRatherThanAnsweringSomethingElse()
    {
        Assert.Contains(
            "cross-validation",
            await RunExpectingFailure("lasso([1;2;3;4], [1;2;3;4], 'CV', 2)"),
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "PredictorNames",
            await RunExpectingFailure("lasso([1;2;3;4], [1;2;3;4], 'PredictorNames', {'a'})"),
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "drawn",
            await RunExpectingFailure("stepwisefit([1;2;3;4], [1;2;3;4], 'display', 'on')"),
            StringComparison.OrdinalIgnoreCase);
    }

    // --- the other dialect ---------------------------------------------------------------------------------

    /// <summary>
    /// The whole surface is registered in both dialects, so it runs under JGS too — with JGS's own
    /// zero-based indexing, which is the only thing that changes.
    /// </summary>
    [Fact]
    public void TheRegressionBuiltinsAreReachableFromJgsAsWell()
    {
        var context = new ScriptContext(_output, (_, _) => { });
        ScriptRunResult result = JgsRunner.Run(
            """
            let d = x2fx([2, 3], 'quadratic')
            print(d[3])
            let h = hougen([1.25, 0.06, 0.04, 0.11, 1.19], [470, 300, 10])
            print(round(h))
            """,
            context,
            default,
            sourceId: "",
            hook: null,
            JgsDialect.Jgs);

        Assert.True(result.Success, result.Message + _output.ErrorText);

        // The cross term of x2fx([2 3], 'quadratic') is 6, and hougen's documented example is 8.67.
        Assert.Contains("6", _output.NormalText, StringComparison.Ordinal);
        Assert.Contains("9", _output.NormalText, StringComparison.Ordinal);
    }
}
