using System.Text;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M53 wave J as a script sees it: the designs, the copulas and samplers, the searchers, the file
/// readers, and the plot verbs.
/// </summary>
/// <remarks>
/// The plot verbs are checked by what they answer and by what they draw into the figure, not by how
/// it looks: a box plot makes a shape per group, a dendrogram makes a link per merge, and a
/// performance curve run for its numbers makes nothing at all. That last one is the distinction this
/// wave taught the interpreter — a call written as a statement is told that nobody wanted its answer.
/// </remarks>
[Collection("JG facade")]
public class MatlabStatisticsWaveJTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "jgraph-wavej-" + Guid.NewGuid().ToString("N"));

    public MatlabStatisticsWaveJTests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not a test failure.
        }
    }

    /// <summary>The temporary folder as a script may write it: the separators doubled.</summary>
    private string Escaped => _folder.Replace("\\", "\\\\");

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

    // --- Designs ------------------------------------------------------------------------------------

    [Fact]
    public Task TheDesignsComeBackAtTheirDocumentedSizes() => RunAsserting("""
        d = fullfact([2 3 2]);
        assert(isequal(size(d), [12 3]));
        assert(d(1,1) == 1 && d(2,1) == 2 && d(2,2) == 1);

        f = ff2n(3);
        assert(isequal(size(f), [8 3]));
        assert(isequal(f(2,:), [0 0 1]));

        assert(isequal(size(bbdesign(3)), [15 3]));
        assert(isequal(size(bbdesign(4, 'center', 1)), [25 4]));
        assert(isequal(size(ccdesign(2)), [13 2]));
        assert(isequal(size(ccdesign(3, 'fraction', 1)), [16 3]));
        assert(abs(max(max(ccdesign(2, 'type', 'faced'))) - 1) < 1e-12);
        assert(abs(max(max(ccdesign(2))) - sqrt(2)) < 1e-9);
        """);

    [Fact]
    public Task AFractionAnswersItsConfoundingAsACellTable() => RunAsserting("""
        [X, conf] = fracfact('a b c abc');
        assert(isequal(size(X), [8 4]));
        assert(strcmp(conf{1,1}, 'Term'));
        assert(strcmp(conf{1,3}, 'Confounding'));

        % Every column is balanced and orthogonal to every other, which is what a fraction is for.
        assert(abs(sum(X(:,4))) < 1e-12);
        assert(abs(sum(X(:,1) .* X(:,2))) < 1e-12);

        g = fracfactgen('a b c d', 3);
        assert(numel(g) == 4);
        assert(strcmp(g{4}, 'abc'));
        """);

    [Fact]
    public async Task ADesignRefusesWhatItCannotDo()
    {
        Assert.Contains("blocksize", await RunExpectingFailure("bbdesign(3, 'blocksize', 5);"));
        Assert.Contains("rng", await RunExpectingFailure("ccdesign(2, 'state', 7);"));
        Assert.Contains("resolution", await RunExpectingFailure("fracfactgen('a b c d e', 3, 4);"));
        Assert.Contains("whole number", await RunExpectingFailure("fullfact([2 2.5]);"));
    }

    [Fact]
    public Task CapabilityAndGageAnswerTheirDocumentedFields() => RunAsserting("""
        x = [9.8 10.1 9.9 10.2 10.0 9.95 10.05 10.1];
        S = capability(x, [9 11]);
        assert(abs(S.mu - mean(x)) < 1e-12);
        assert(abs(S.sigma - std(x)) < 1e-12);
        assert(abs(S.Cpk - min(S.Cpl, S.Cpu)) < 1e-12);
        assert(abs(S.P - (S.Pl + S.Pu)) < 1e-12);

        y = [1 1.1 2 2.1 3 3.2 1.05 1.15 2.05 2.15 3.05 3.25];
        part = [1 1 2 2 3 3 1 1 2 2 3 3];
        op = [1 1 1 1 1 1 2 2 2 2 2 2];
        [sd, tbl, stats] = gagerr(y, {part, op});
        assert(sd >= 0);
        assert(strcmp(tbl{1,1}, 'Gage R&R'));
        assert(abs(stats.total - (stats.gagerr + stats.part)) < 1e-10);
        assert(stats.ndc >= 0);
        """);

    [Fact]
    public Task TheOptionsStructureHoldsWhatItWasGiven() => RunAsserting("""
        o = statset('MaxIter', 500, 'TolFun', 1e-8);
        assert(o.MaxIter == 500);
        assert(strcmp(o.Display, 'off'));
        assert(statget(o, 'MaxIter', 1) == 500);
        assert(statget(o, 'TolX', 0.25) == 0.25);

        p = statset(o, 'MaxIter', 10);
        assert(p.MaxIter == 10 && p.TolFun == 1e-8);
        assert(o.MaxIter == 500);
        """);

    [Fact]
    public async Task AnUnknownSettingNamesTheOnesThereAre()
    {
        string message = await RunExpectingFailure("statset('Maxlter', 5);");
        Assert.Contains("MaxIter", message);
        Assert.Contains("TolFun", message);
    }

    // --- Copulas and samplers -------------------------------------------------------------------------

    [Fact]
    public Task TheCopulaNamesAgreeWithEachOther() => RunAsserting("""
        rng(3);
        u = [0.3 0.4; 0.6 0.7];
        c = copulacdf('Clayton', u, 1.5);
        assert(numel(c) == 2 && all(c > 0) && all(c < 1));

        % The parameter that produces a rank correlation produces it.
        a = copulaparam('Clayton', 0.4);
        assert(abs(copulastat('Clayton', a) - 0.4) < 1e-6);
        assert(abs(copulastat('Gaussian', 0.5) - 2/pi*asin(0.5)) < 1e-12);

        U = copularnd('Frank', 5, 800);
        assert(isequal(size(U), [800 2]));
        assert(all(all(U > 0 & U < 1)));

        ahat = copulafit('Frank', U);
        assert(abs(ahat - 5) < 1.5);

        R = copularnd('Gaussian', [1 0.6; 0.6 1], 800);
        rhohat = copulafit('Gaussian', R);
        assert(abs(rhohat(1,2) - 0.6) < 0.15);
        """);

    [Fact]
    public async Task ACopulaRefusesWhatItCannotDescribe()
    {
        Assert.Contains("bivariate", await RunExpectingFailure(
            "copulacdf('Clayton', [0.2 0.3 0.4], 1.5);"));
        Assert.Contains("not a copula family", await RunExpectingFailure(
            "copulacdf('Wibble', [0.2 0.3], 1.5);"));
        Assert.Contains("positive dependence", await RunExpectingFailure(
            "copulaparam('Clayton', -0.4);"));
        Assert.Contains("degrees of freedom", await RunExpectingFailure(
            "copulacdf('t', [0.2 0.3], 0.5);"));
    }

    [Fact]
    public Task TheDescribedDistributionsDrawWhatTheyWereDescribedAs() => RunAsserting("""
        rng(21);
        [r, type, coefs] = johnsrnd([-1.6 -0.5 0.5 1.6], 4000, 1);
        assert(numel(r) == 4000);
        assert(ischar(type) && numel(coefs) == 4);
        assert(abs(mean(r)) < 0.2);

        [p, ptype] = pearsrnd(4, 2, 0, 3, 3000, 1);
        assert(ptype == 0);
        assert(abs(mean(p) - 4) < 0.25);
        assert(abs(std(p) - 2) < 0.25);
        """);

    [Fact]
    public Task TheChainsFindTheDistributionTheyWereAimedAt() => RunAsserting("""
        rng(31);
        f = @(x) exp(-0.5 * x.^2);
        [s, accept] = mhsample(0, 3000, 'pdf', f, 'proprnd', @(x) x + randn(1,1), 'symmetric', true, 'burnin', 300);
        assert(numel(s) == 3000);
        assert(accept > 0 && accept <= 1);
        assert(abs(mean(s)) < 0.2 && abs(std(s) - 1) < 0.2);

        [t, neval] = slicesample(0, 2000, 'logpdf', @(x) -0.5 * x.^2, 'width', 5);
        assert(numel(t) == 2000 && neval > 2000);
        assert(abs(mean(t)) < 0.2 && abs(std(t) - 1) < 0.2);
        """);

    [Fact]
    public async Task AChainRefusesAnIncompleteDescription()
    {
        Assert.Contains("proprnd", await RunExpectingFailure(
            "mhsample(0, 10, 'pdf', @(x) exp(-x.^2));"));
        Assert.Contains("'pdf' or 'logpdf'", await RunExpectingFailure(
            "slicesample(0, 10, 'width', 2);"));
        Assert.Contains("not both", await RunExpectingFailure(
            "slicesample(0, 10, 'pdf', @(x) 1, 'logpdf', @(x) 0);"));
        Assert.Contains("nchain", await RunExpectingFailure(
            "mhsample(0, 10, 'pdf', @(x) exp(-x.^2), 'proprnd', @(x) x, 'symmetric', true, 'nchain', 3);"));
    }

    [Fact]
    public Task TheCovarianceOfAnEstimateIsAMatrixOfTheRightSize() => RunAsserting("""
        rng(41);
        x = normrnd(3, 2, 400, 1);
        acov = mlecov([3 2], x, 'pdf', @(v, m, s) normpdf(v, m, s));
        assert(isequal(size(acov), [2 2]));
        assert(abs(sqrt(acov(1,1)) - 2/sqrt(400)) < 0.02);
        """);

    // --- Objects held for later -------------------------------------------------------------------------

    [Fact]
    public Task ASearcherIsAnObjectTheNeighbourSearchesAccept() => RunAsserting("""
        X = [1 1; 2 2; 3 3; 10 10];
        ns = createns(X);
        assert(strcmp(class(ns), 'ExhaustiveSearcher'));
        assert(strcmp(class(createns(X, 'NSMethod', 'kdtree')), 'KDTreeSearcher'));
        assert(strcmp(class(KDTreeSearcher(X)), 'KDTreeSearcher'));

        [idx, d] = knnsearch(ns, [2.1 2.1], 'K', 2);
        assert(idx(1) == 2 && idx(2) == 3);
        assert(abs(d(1) - sqrt(0.02)) < 1e-9);

        % The object carries its metric, and the two ways of saying it agree.
        cheb = ExhaustiveSearcher(X, 'Distance', 'chebychev');
        [~, dc] = knnsearch(cheb, [2.4 2.4]);
        [~, dd] = knnsearch(X, [2.4 2.4], 'Distance', 'chebychev');
        assert(abs(dc - dd) < 1e-12);

        near = rangesearch(ns, [1.5 1.5], 1);
        assert(numel(near{1}) == 2);
        """);

    [Fact]
    public Task ThePiecewiseDistributionPublishesItsPieces() => RunAsserting("""
        rng(51);
        x = trnd(3, 800, 1);
        pd = paretotails(x, 0.1, 0.9);
        assert(strcmp(class(pd), 'paretotails'));
        assert(pd.NumObservations == 800);
        assert(isequal(pd.boundary, [0.1 0.9]));
        assert(pd.cutoff(1) < pd.cutoff(2));
        assert(numel(pd.lowerparams) == 2 && numel(pd.upperparams) == 2);
        """);

    [Fact]
    public Task TheEmbeddingKeepsSeparatedThingsApart() => RunAsserting("""
        rng(61);
        X = [randn(40,3); randn(40,3) + 10];
        [Y, loss] = tsne(X, 'Perplexity', 15);
        assert(isequal(size(Y), [80 2]));
        assert(all(all(isfinite(Y))));
        assert(isfinite(loss));

        first = mean(Y(1:40,:));
        second = mean(Y(41:80,:));
        within = mean(sqrt(sum((Y(1:40,:) - first).^2, 2)));
        assert(norm(first - second) > within);
        """);

    [Fact]
    public async Task TheEmbeddingRefusesTheApproximationItDoesNotMake()
    {
        Assert.Contains("exact", await RunExpectingFailure(
            "tsne(randn(20,3), 'Algorithm', 'barneshut');"));
        Assert.Contains("pca", await RunExpectingFailure(
            "tsne(randn(20,5), 'NumPCAComponents', 3);"));
    }

    // --- Files ----------------------------------------------------------------------------------------

    [Fact]
    public Task TheCaseAndTableFilesRoundTrip() => RunAsserting($$"""
        cd('{{Escaped}}');
        names = {'alpha'; 'beta'; 'gamma'};
        casewrite(names, 'cases.txt');
        back = caseread('cases.txt');
        assert(numel(back) == 3);
        assert(strcmp(back{2}, 'beta'));

        data = [1 2; 3 4; 5 6];
        tblwrite(data, {'x','y'}, {'r1','r2','r3'}, 'tbl.txt');
        [d2, v2, c2] = tblread('tbl.txt');
        assert(isequal(d2, data));
        assert(strcmp(strtrim(v2{1}), 'x'));
        assert(strcmp(strtrim(c2{3}), 'r3'));
        """);

    [Fact]
    public Task ATabDelimitedFileBecomesOneFieldPerColumn() => RunAsserting($$"""
        cd('{{Escaped}}');
        fid = fopen('tab.txt', 'w');
        fprintf(fid, 'name\tvalue\n');
        fprintf(fid, 'a\t1\n');
        fprintf(fid, 'b\t2.5\n');
        fclose(fid);

        s = tdfread('tab.txt');
        assert(abs(sum(s.value) - 3.5) < 1e-12);
        assert(numel(s.name) == 2);
        """);

    /// <summary>
    /// A transport file written by hand, so that the reader is tested against the format rather than
    /// against itself. Everything in it is fixed-width and the numbers are in the IBM 360's floating
    /// point, which is the one part of the format that is arithmetic rather than layout.
    /// </summary>
    [Fact]
    public async Task ATransportFileIsReadIntoOneFieldPerVariable()
    {
        string path = Path.Combine(_folder, "sample.xpt");
        File.WriteAllBytes(path, TransportFile());

        await RunAsserting($$"""
            cd('{{Escaped}}');
            s = xptread('sample.xpt');
            assert(numel(s.WEIGHT) == 3);
            assert(abs(s.WEIGHT(1) - 1.5) < 1e-12);
            assert(abs(s.WEIGHT(2) + 2.25) < 1e-12);
            assert(abs(s.WEIGHT(3) - 100) < 1e-12);
            assert(strcmp(strtrim(s.NAME{1}), 'ab'));
            assert(strcmp(strtrim(s.NAME{3}), 'ef'));
            """);
    }

    [Fact]
    public async Task SomethingThatIsNotATransportFileSaysSo()
    {
        string path = Path.Combine(_folder, "not.xpt");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(new string(' ', 1000)));

        string message = await RunExpectingFailure($$"""
            cd('{{Escaped}}');
            xptread('not.xpt');
            """);

        Assert.Contains("version 5 transport file", message);
    }

    private static byte[] TransportFile()
    {
        var bytes = new List<byte>();

        void Record(string text) =>
            bytes.AddRange(Encoding.ASCII.GetBytes(text.PadRight(80).Substring(0, 80)));

        Record("HEADER RECORD*******LIBRARY HEADER RECORD!!!!!!!000000000000000000000000000000");
        Record("SAS     SAS     SASLIB  9.4     X64_10PRO                       01JAN26:00:00:00");
        Record("01JAN26:00:00:00");
        Record("HEADER RECORD*******MEMBER  HEADER RECORD!!!!!!!000000000000000001600000000140  ");
        Record("HEADER RECORD*******DSCRPTR HEADER RECORD!!!!!!!000000000000000000000000000000  ");
        Record("SAS     SAMPLE  SASDATA 9.4     X64_10PRO                       01JAN26:00:00:00");
        Record("01JAN26:00:00:00                                                                ");
        Record("HEADER RECORD*******NAMESTR HEADER RECORD!!!!!!!000000000200000000000000000000  ");

        // Two variables: an eight-byte number and a two-character name.
        bytes.AddRange(Namestr(numeric: true, width: 8, name: "WEIGHT", position: 0));
        bytes.AddRange(Namestr(numeric: false, width: 2, name: "NAME", position: 8));
        while (bytes.Count % 80 != 0)
        {
            bytes.Add(0x20);
        }

        Record("HEADER RECORD*******OBS     HEADER RECORD!!!!!!!000000000000000000000000000000  ");

        foreach ((double weight, string name) in new[] { (1.5, "ab"), (-2.25, "cd"), (100.0, "ef") })
        {
            bytes.AddRange(Ibm(weight));
            bytes.AddRange(Encoding.ASCII.GetBytes(name));
        }

        while (bytes.Count % 80 != 0)
        {
            bytes.Add(0x20);
        }

        return [.. bytes];
    }

    private static byte[] Namestr(bool numeric, int width, string name, int position)
    {
        var field = new byte[140];
        field[1] = (byte)(numeric ? 1 : 2);
        field[4] = (byte)(width >> 8);
        field[5] = (byte)(width & 0xFF);
        Encoding.ASCII.GetBytes(name.PadRight(8)).CopyTo(field, 8);
        Encoding.ASCII.GetBytes(new string(' ', 40)).CopyTo(field, 16);
        field[74] = (byte)(position >> 8);
        field[75] = (byte)(position & 0xFF);
        return field;
    }

    /// <summary>One number in the IBM 360's floating point: a base-sixteen exponent and a fraction.</summary>
    private static byte[] Ibm(double value)
    {
        var field = new byte[8];
        if (value == 0)
        {
            return field;
        }

        bool negative = value < 0;
        double magnitude = Math.Abs(value);
        int exponent = 0;
        while (magnitude >= 1)
        {
            magnitude /= 16;
            exponent++;
        }

        while (magnitude < 1.0 / 16)
        {
            magnitude *= 16;
            exponent--;
        }

        field[0] = (byte)((negative ? 0x80 : 0) | ((exponent + 64) & 0x7F));
        double fraction = magnitude;
        for (int i = 1; i < 8; i++)
        {
            fraction *= 256;
            var whole = (int)Math.Floor(fraction);
            field[i] = (byte)whole;
            fraction -= whole;
        }

        return field;
    }

    // --- Plot verbs ---------------------------------------------------------------------------------

    [Fact]
    public Task TheDistributionPlotsDrawAndAnswerHandles() => RunAsserting("""
        rng(71);
        x = normrnd(0, 1, 120, 1);

        [h, s] = cdfplot(x);
        assert(h > 0);
        assert(abs(s.mean - mean(x)) < 1e-12);
        assert(abs(s.min - min(x)) < 1e-12);

        figure; hh = histfit(x, 10);
        assert(numel(hh) == 2);
        figure; assert(numel(histfit(exprnd(2, 100, 1), 8, 'exponential')) == 2);
        figure; assert(numel(normplot(x)) == 2);
        figure; assert(numel(wblplot(wblrnd(2, 3, 60, 1))) == 2);
        figure; assert(numel(probplot('exponential', exprnd(3, 60, 1))) == 2);
        figure; assert(numel(qqplot(x)) == 2);
        figure; assert(numel(qqplot(x, normrnd(1, 2, 90, 1))) == 2);
        """);

    [Fact]
    public Task ABoxPlotMakesAShapePerGroup() => RunAsserting("""
        x = [1 2 3 4 5 6 7 8 9 40]';
        g = [1 1 1 1 1 2 2 2 2 2]';

        h = boxplot(x, g);
        % Six shapes per group with no outliers, seven when there is one to mark.
        assert(numel(h) == 13);

        figure; assert(numel(boxplot(x, g, 'Notch', 'on', 'Whisker', 3)) == 12);
        figure; assert(numel(boxplot([x x], 'Labels', {'a','b'})) == 14);
        figure; assert(numel(boxplot(x, g, 'Orientation', 'horizontal')) == 13);
        """);

    [Fact]
    public async Task ABoxPlotRefusesTheStylesItDoesNotDraw()
    {
        Assert.Contains("plotstyle", await RunExpectingFailure(
            "boxplot([1 2 3 4], 'PlotStyle', 'compact');"));
        Assert.Contains("one label per group", await RunExpectingFailure(
            "boxplot([1 2 3 4]', [1 1 2 2]', 'Labels', {'only one'});"));
    }

    [Fact]
    public Task TheFittedLinesAndCurvesGoOverWhatIsAlreadyDrawn() => RunAsserting("""
        x = (1:20)';
        y = 3 * x + 1;
        plot(x, y, '.');
        h = lsline;
        assert(h > 0);

        % The fit through an exactly straight set of points is that line, so reading its data back
        % gives the same slope.
        ydata = h.YData;
        xdata = h.XData;
        slope = (ydata(2) - ydata(1)) / (xdata(2) - xdata(1));
        assert(abs(slope - 3) < 1e-8);

        figure; plot(x, y, '.');
        assert(refline(2, 0) > 0);
        assert(refcurve([1 0 0]) > 0);
        """);

    [Fact]
    public Task TheGroupedScatterAndTheMatrixDrawOnePartPerGroup() => RunAsserting("""
        rng(81);
        x = randn(60, 1);
        y = randn(60, 1);
        g = repmat([1;2;3], 20, 1);

        h = gscatter(x, y, g);
        assert(numel(h) == 3);

        figure; [hm, ax, big] = gplotmatrix([x y]);
        assert(numel(hm) == 4);
        assert(isequal(size(ax), [2 2]));
        assert(big > 0);

        figure; hs = scatterhist(x, y);
        assert(numel(hs) == 3);
        """);

    [Fact]
    public Task ADendrogramDrawsOneLinkPerMergeAndSaysWhereTheLeavesWent() => RunAsserting("""
        rng(91);
        Z = linkage(pdist(randn(10, 2)), 'average');
        [h, T, perm] = dendrogram(Z);
        assert(numel(h) == 9);
        assert(numel(T) == 10);
        assert(isequal(sort(perm), 1:10));
        assert(isequal(sort(T)', 1:10));

        % Collapsed to four nodes: three links, and every leaf lands in one of the four.
        figure; [h4, T4] = dendrogram(Z, 4);
        assert(numel(h4) == 3);
        assert(max(T4) == 4 && min(T4) == 1);
        assert(numel(T4) == 10);
        """);

    [Fact]
    public Task TheMultiVariableCurvesDrawOnePerObservation() => RunAsserting("""
        rng(101);
        X = randn(12, 4);
        andrewsplot(X);

        figure; parallelcoords(X, 'Standardize', 'on');
        figure; parallelcoords(X, 'Quantile', 0.25);
        figure; andrewsplot(X, 'Group', repmat([1;2], 6, 1));
        figure; glyphplot(X(1:4,:));
        figure; biplot([0.5 0.5; -0.5 0.5; 0.1 -0.9]);
        """);

    [Fact]
    public async Task TheCurvesRefuseTheFormsTheyDoNotDraw()
    {
        Assert.Contains("face glyph", await RunExpectingFailure(
            "glyphplot(randn(4,3), 'Glyph', 'face');"));
        Assert.Contains("three dimensions", await RunExpectingFailure(
            "biplot(randn(4,3));"));
        Assert.Contains("principal-component", await RunExpectingFailure(
            "parallelcoords(randn(6,3), 'Standardize', 'PCA');"));
    }

    [Fact]
    public Task TheBivariateHistogramCountsEveryObservationOnce() => RunAsserting("""
        rng(111);
        X = [randn(200,1) randn(200,1)];
        [N, C] = hist3(X, [4 5]);
        assert(isequal(size(N), [4 5]));
        assert(sum(sum(N)) == 200);
        assert(numel(C) == 2);
        assert(numel(C{1}) == 4 && numel(C{2}) == 5);

        % With nobody asking for the counts it draws instead.
        figure;
        hist3(X);
        """);

    [Fact]
    public Task ThePerformanceCurveIsRightWhenTheClassifierIsPerfect() => RunAsserting("""
        labels = [0 0 0 1 1 1]';
        scores = [1 2 3 8 9 10]';
        [fpr, tpr, T, auc, opt] = perfcurve(labels, scores, 1);
        assert(abs(auc - 1) < 1e-12);
        assert(isequal(opt, [0 1]));
        assert(numel(fpr) == numel(tpr) && numel(T) == numel(fpr));
        assert(fpr(1) == 0 && tpr(1) == 0);
        assert(abs(fpr(end) - 1) < 1e-12 && abs(tpr(end) - 1) < 1e-12);

        % A classifier that says nothing has an area of a half, whichever way its scores run.
        [~, ~, ~, half] = perfcurve([0 1 0 1]', [1 1 2 2]', 1);
        assert(abs(half - 0.5) < 1e-12);

        % Named labels work the same way.
        [~, ~, ~, named] = perfcurve({'no';'no';'yes';'yes'}, [1;2;3;4], 'yes');
        assert(abs(named - 1) < 1e-12);
        """);

    [Fact]
    public Task TheRemainingDiagnosticsDrawWithoutComplaint() => RunAsserting("""
        rng(121);
        X = [ones(30,1) randn(30,2)];
        y = X * [1; 2; -1] + randn(30,1) * 0.2;
        [b, bint, r, rint] = regress(y, X);

        addedvarplot(X(:,2:3), y, 1, [false true]);
        figure; rcoplot(r, rint);
        figure; [p, h] = capaplot(y, [-4 8]);
        assert(p > 0 && p <= 1 && numel(h) == 3);
        figure; [p2, h2] = normspec([-1 1], 0, 1);
        assert(abs(p2 - (normcdf(1) - normcdf(-1))) < 1e-9);
        assert(numel(h2) == 3);

        resp = randn(18,1);
        fa = repmat([1;2;3], 6, 1);
        fb = repmat([1;1;1;2;2;2], 3, 1);
        figure; maineffectsplot(resp, {fa, fb});
        figure; interactionplot(resp, {fa, fb});
        figure; multivarichart(resp, {fa, fb});

        [B, FitInfo] = lasso(X(:,2:3), y);
        figure; lassoPlot(B);
        figure; lassoPlot(B, FitInfo, 'PlotType', 'Lambda');
        """);

    [Fact]
    public async Task TheLassoPlotRefusesTheCurveItCannotCompute()
    {
        string message = await RunExpectingFailure("""
            X = randn(20, 2);
            y = X * [1; -1] + randn(20,1) * 0.1;
            B = lasso(X, y);
            lassoPlot(B, [], 'PlotType', 'CV');
            """);

        Assert.Contains("cross-validated", message);
    }

    // --- Drawing only when nobody asked ------------------------------------------------------------------

    [Fact]
    public async Task TheEmpiricalCurvesDrawWhenTheirNumbersAreNotWanted()
    {
        // Asked for the numbers: the numbers come back and nothing is drawn.
        await RunAsserting("""
            rng(131);
            x = normrnd(0, 1, 50, 1);
            [f, xi] = ecdf(x);
            assert(numel(f) == numel(xi) && numel(f) > 1);
            [d, pts] = ksdensity(x);
            assert(numel(d) == 100 && numel(pts) == 100);
            """);

        Assert.Empty(JG.Gca().Plots);

        // Not asked: a curve appears. The second replaces the first, because a drawing verb without
        // hold replaces what the axes held — which is the point: these two really did draw.
        await RunAsserting("""
            rng(131);
            x = normrnd(0, 1, 50, 1);
            ecdf(x);
            ksdensity(x);
            """);

        Assert.Single(JG.Gca().Plots);
    }

    [Fact]
    public async Task APerformanceCurveDrawsOnlyWhenNobodyAskedForItsNumbers()
    {
        await RunAsserting("""
            labels = [0 0 1 1]';
            scores = [1 2 3 4]';
            [x, y] = perfcurve(labels, scores, 1);
            assert(numel(x) == numel(y));
            """);

        Assert.Empty(JG.Gca().Plots);

        await RunAsserting("""
            perfcurve([0 0 1 1]', [1 2 3 4]', 1);
            """);

        Assert.Single(JG.Gca().Plots);
    }

    // --- What stess_25 found in wave K ---------------------------------------------------------

    [Fact]
    public Task ASmoothedDensityReportsTheWidthItSmoothedWith() => RunAsserting("""
        rng(3);
        x = randn(1, 200);
        [f, xi, chosen] = ksdensity(x);
        assert(chosen > 0);
        [~, ~, named] = ksdensity(x, 'Bandwidth', 0.5);
        assert(abs(named - 0.5) < 1e-12);
        assert(numel(f) == numel(xi));
        """);

    [Fact]
    public Task APiecewiseDistributionIsADistribution() => RunAsserting("""
        rng(4);
        x = randn(1, 300);
        pd = paretotails(x, 0.1, 0.9);
        assert(abs(cdf(pd, icdf(pd, 0.5)) - 0.5) < 1e-6);
        assert(pdf(pd, 0) > 0);
        assert(numel(cdf(pd, [-1 0 1])) == 3);
        assert(strcmp(class(pd), 'paretotails'));
        """);

    [Fact]
    public Task AskingForOneDrawNeedsNoBracketsAfterTheName() => RunAsserting("""
        rng(5);
        a = rand;
        b = 1 + randn;
        assert(a >= 0 && a <= 1);
        assert(isnumeric(b) && numel(b) == 1);
        """);
}
