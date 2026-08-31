using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M118: the rules the smoothing family actually follows, measured off MATLAB rather than off the
/// code that used to implement them.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here was run against MATLAB R2024a before it was written down. That is the
/// point of the file. M116 and M117 both wrote their tests as <em>the fast road answers what the
/// walk answered</em>, which is a real property and worth having — but a walk is only a reference
/// for as long as it is right, and six of these behaviours had been wrong since they were written.
/// A test that mirrors the implementation cannot see that.
/// </para>
/// <para>
/// What the mirror could not see: a Gaussian window whose standard deviation was a quarter of its
/// width instead of a fifth; a fit whose window shrank at the ends instead of sliding back inside
/// the readings; a two-element window silently thrown away and the automatic one used instead; a
/// median that answered with a real reading when the window held a missing one; <c>movmad</c>
/// measuring a mean deviation about a mean rather than a median about a median; and a robust fit
/// weighing each window against itself rather than against the smooth of the whole series.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class MatlabSmoothingM118Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSmoothingM118Tests() => JG.Reset();

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

    /// <summary>
    /// A Gaussian window's standard deviation is a fifth of the window it is given, and smoothing
    /// an impulse hands the weights back so the figure can be read off rather than assumed.
    /// </summary>
    [Fact]
    public Task TheGaussianStandardDeviationIsAFifthOfItsWindow() => RunAsserting("""
        imp = zeros(1, 81);
        imp(41) = 1;
        % Two taps either side of the peak pin sigma: k(c)/k(c+d) = exp(d^2 / 2 sigma^2).
        k = smoothdata(imp, 'gaussian', 21);
        sigma = 2 / sqrt(2 * log(k(41) / k(43)));
        assert(abs(sigma - 21/5) < 1e-9, 'a window given as one number is that number wide');
        % A window given as a pair is as wide as it REACHES, which is one less: the reading it is
        % centred on is counted by neither half.
        k2 = smoothdata(imp, 'gaussian', [10 10]);
        sigma2 = 2 / sqrt(2 * log(k2(41) / k2(43)));
        assert(abs(sigma2 - 20/5) < 1e-9, 'a window given as a pair is as wide as it reaches');
        """);

    /// <summary>
    /// A fit does not let its window shrink at the ends: it reads the width nearest the point. So
    /// a fit of degree <em>d</em> reproduces a polynomial of degree <em>d</em> everywhere,
    /// including at the very first and very last reading, where a weighted average cannot.
    /// </summary>
    [Fact]
    public Task AFitReadsTheWidthNearestThePointAtTheEnds() => RunAsserting("""
        t = 0:59;
        line = 3 - 0.5 * t;
        quad = 3 - 0.5 * t + 0.25 * t.^2;
        assert(max(abs(smoothdata(line, 'lowess', 11) - line)) < 1e-9);
        assert(max(abs(smoothdata(quad, 'loess', 11) - quad)) < 1e-9);
        assert(max(abs(smoothdata(quad, 'sgolay', 11) - quad)) < 1e-9);
        % Unweighted, the whole end is one polynomial -- the same readings and the same weights for
        % every point there -- so it is read at each of them rather than refitted.
        s = smoothdata(quad, 'sgolay', 11);
        assert(abs(s(1) - quad(1)) < 1e-9 && abs(s(end) - quad(end)) < 1e-9);
        % A weighted average is cut short at the ends instead, and so does not reproduce a line.
        g = smoothdata(line, 'gaussian', 11);
        assert(abs(g(1) - line(1)) > 1e-3);
        """);

    /// <summary>
    /// The window may be a <c>[before after]</c> pair, and then it reaches that far each way and
    /// comes back as a pair.
    /// </summary>
    /// <remarks>
    /// A pair is a pair of numbers rather than a number, so a test that admitted only scalars
    /// dropped it without a word and reached for the automatic width instead — which for twenty
    /// readings is a window of two, and answers something entirely different.
    /// </remarks>
    [Fact]
    public Task AWindowMayBeAPairAndComesBackAsOne() => RunAsserting("""
        x = 1:20;
        [b, w] = smoothdata(x, 'movmean', [2 7]);
        assert(isequal(w, [2 7]), 'the window comes back as it was given');
        assert(abs(b(1) - mean(1:8)) < 1e-12);
        assert(abs(b(10) - mean(8:17)) < 1e-12);
        % Nought either way is allowed, and reaches only forwards.
        c = smoothdata(x, 'movmean', [0 6]);
        assert(abs(c(1) - mean(1:7)) < 1e-12);
        % A pair and the scalar that covers the same readings are not the same window: [3 3] holds
        % seven readings and so does 7, but they are told different widths.
        assert(isequal(smoothdata(x, 'movmean', [3 3]), smoothdata(x, 'movmean', 7)));
        """);

    /// <summary>A median of readings with a missing one among them is missing.</summary>
    /// <remarks>
    /// A sort over doubles puts every NaN in front, so reading the middle of the sorted whole
    /// answered with a real reading whenever fewer than half the window was missing.
    /// </remarks>
    [Fact]
    public Task AMedianWithAHoleInItIsAHole() => RunAsserting("""
        assert(isnan(median([10 20 NaN])));
        assert(isnan(median([10 NaN 20 30])));
        assert(median([10 20 NaN], 'omitnan') == 15);
        assert(isequaln(median([1 2; NaN 4; 5 6]), [NaN 4]));
        % And the same of a window slid along a series.
        assert(isequaln(movmedian([10 20 NaN 40 50], 3), [15 NaN NaN NaN 45]));
        assert(isequal(movmedian([10 20 NaN 40 50], 3, 'omitnan'), [15 15 30 45 45]));
        assert(isequaln(smoothdata([10 20 NaN 40 50], 'movmedian', 3, 'includenan'), ...
                        [15 NaN NaN NaN 45]));
        """);

    /// <summary>
    /// <c>movmad</c> is a <em>median</em> absolute deviation about the median, not a mean one
    /// about the mean.
    /// </summary>
    /// <remarks>
    /// The two agree on a window of one or two readings and part company on every larger one,
    /// which is what made the difference easy to miss — and the whole point of the statistic is
    /// that one wild reading barely moves it, which a mean cannot do.
    /// </remarks>
    [Fact]
    public Task TheMovingDeviationIsAMedianAboutAMedian() => RunAsserting("""
        x = [1 2 3 4 100];
        assert(isequal(movmad(x, 3), [0.5 1 1 1 48]));
        assert(isequal(movmad(x, 5), [1 1 1 1 1]));
        % A window of one has nothing to deviate from.
        assert(isequal(movmad(x, 1), zeros(1, 5)));
        % The endpoint rules apply to it as they do to the rest of the family.
        assert(numel(movmad(x, 3, 'Endpoints', 'discard')) == 3);
        filled = movmad(x, 3, 'Endpoints', 'fill');
        assert(all(isnan(filled([1 5]))));
        assert(isequal(size(movmad(x, [2 0])), [1 5]));
        % A missing reading spoils its own windows, and can be stepped over.
        holed = [1 2 NaN 4 5];
        assert(isequal(isnan(movmad(holed, 3)), [false true true true false]));
        assert(~any(isnan(movmad(holed, 3, 'omitnan'))));
        """);

    /// <summary>
    /// A robust fit weighs every reading against the smooth of the <em>whole</em> series, scaled by
    /// one median taken over all of those residuals — not against the window it happens to sit in.
    /// </summary>
    [Fact]
    public Task ARobustFitWeighsAgainstTheWholeSeries() => RunAsserting("""
        t = 0:59;
        y = sin(t / 7);
        y(30) = 20;
        plain = smoothdata(y, 'lowess', 15);
        held = smoothdata(y, 'rlowess', 15);
        clean = sin(t / 7);
        % The wild reading drags a plain fit a long way and a robust one hardly at all. These
        % four are MATLAB R2024a's own numbers, which JGraph now answers to six places.
        assert(abs(max(abs(plain(25:35) - clean(25:35))) - 2.631876) < 1e-5);
        assert(abs(max(abs(held(25:35) - clean(25:35))) - 0.072605) < 1e-5);
        % Far from the wild reading the robust fit still follows the data.
        assert(abs(max(abs(held(5:20) - clean(5:20))) - 0.070979) < 1e-5);
        % Weights read afresh from the tricube each pass rather than piled onto the last: piling
        % them up only ever shrinks them, and the fit walks away from the readings it should hold.
        assert(abs(max(abs(smoothdata(clean, 'rlowess', 15) - clean)) - 0.153926) < 1e-5);
        """);

    /// <summary>
    /// Applying the kernel through the frequency domain answers what applying it one tap at a time
    /// answers, either side of the width where the one road becomes the other.
    /// </summary>
    /// <remarks>
    /// The two are different routes to the same number rather than the same arithmetic, so the
    /// last place moves; nothing else does. A window of a hundred and twenty-seven is applied
    /// directly and one of a hundred and twenty-eight is transformed, so smoothing the same
    /// readings with each and comparing the overlap is what says the transform is centred where
    /// the direct pass is and reaches as far.
    /// </remarks>
    [Fact]
    public Task TheTransformedKernelAnswersWhatTheDirectOneAnswers() => RunAsserting("""
        t = 1:4000;
        x = sin(t / 31) + 0.4 * cos(t / 7) + 0.1 * sin(t / 2.3);
        % A polynomial is reproduced whichever road the kernel took.
        line = 2 - 0.01 * t;
        for w = [127 128 129 511 1024]
            assert(max(abs(smoothdata(line, 'sgolay', w) - line)) < 1e-6, 'sgolay reproduces a line');
        end
        % A constant is smoothed to itself, ends included, on both roads.
        flat = repmat(3.5, 1, 4000);
        for w = [127 128 1024]
            assert(max(abs(smoothdata(flat, 'gaussian', w) - 3.5)) < 1e-9, 'gaussian holds a constant');
        end
        % A symmetric series smooths symmetrically, which a kernel applied backwards would break
        % and which nothing in the middle of the data would reveal.
        half = x(1:2000);
        mirror = [half fliplr(half)];
        for m = {'gaussian', 'sgolay', 'lowess'}
            got = smoothdata(mirror, m{1}, 301);
            assert(max(abs(got - fliplr(got))) < 1e-9, m{1});
        end
        % A missing reading still reaches only the windows that read it: the transform is refused
        % outright when the readings hold anything that is not a number, because it would spread
        % that reading over the whole block it lands in.
        holed = x;
        holed(2000) = NaN;
        spoiled = find(isnan(smoothdata(holed, 'gaussian', 301, 'includenan')));
        assert(spoiled(1) == 1850 && spoiled(end) == 2150, 'a gap reaches its own windows only');
        """);
}
