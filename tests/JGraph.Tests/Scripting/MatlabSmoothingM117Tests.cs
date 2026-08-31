using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M117: <c>smoothdata</c>'s shaped-window methods stop rebuilding a fit per output sample, and
/// <c>movmad</c> stops building the window it measures. Every rule that made those answers what
/// they were is pinned here, because each is a place a kernel could quietly disagree with the walk
/// it replaced: where the window sits, how far it reaches at the ends, which readings a missing one
/// spoils, and what a window too small for the question retreats to.
/// </summary>
/// <remarks>
/// The exact claims are the structural ones — a smoother that fits degree <em>d</em> reproduces a
/// polynomial of degree <em>d</em>, a symmetric series smooths to a symmetric answer, a constant
/// smooths to itself. Those hold to the last few places whatever route the arithmetic takes, and
/// they are what an off-by-one window or a reversed kernel breaks outright.
/// </remarks>
[Collection("JG facade")]
public class MatlabSmoothingM117Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSmoothingM117Tests() => JG.Reset();

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
    /// A smoother that fits a polynomial reproduces one of its own degree — the property that says
    /// the kernel is centred where the window is and reaches as far as the window does.
    /// </summary>
    [Fact]
    public Task APolynomialOfTheDegreeFittedComesBackUnchanged() => RunAsserting("""
        x = linspace(-2, 2, 120);
        line = 3 - 0.5 * x;
        curve = 3 - 0.5 * x + 2 * x.^2;
        assert(max(abs(smoothdata(line, 'lowess', 25) - line)) < 1e-8);
        assert(max(abs(smoothdata(curve, 'loess', 25) - curve)) < 1e-8);
        assert(max(abs(smoothdata(curve, 'sgolay', 25) - curve)) < 1e-8);
        % A straight line is not reproduced by a Gaussian average at the ends, where the window is
        % cut short and leans to one side — but it is in the middle, where the window is whole.
        smoothed = smoothdata(line, 'gaussian', 25);
        assert(max(abs(smoothed(20:100) - line(20:100))) < 1e-8);
        """);

    /// <summary>A constant is smoothed to itself by every method, including at the cut-short ends.</summary>
    [Fact]
    public Task AConstantIsSmoothedToItself() => RunAsserting("""
        flat = repmat(7.25, 1, 60);
        for m = {'movmean', 'movmedian', 'gaussian', 'lowess', 'loess', 'rlowess', 'rloess', 'sgolay'}
            got = smoothdata(flat, m{1}, 11);
            assert(max(abs(got - 7.25)) < 1e-9, m{1});
        end
        """);

    /// <summary>
    /// A symmetric series smooths to a symmetric answer, which fails outright if a kernel is
    /// applied the wrong way round — the one mistake that is invisible in the middle of the data.
    /// </summary>
    [Fact]
    public Task ASymmetricSeriesSmoothsSymmetrically() => RunAsserting("""
        half = [1 4 2 8 5 9 3 7 6];
        mirror = [half fliplr(half)];
        for m = {'gaussian', 'lowess', 'loess', 'sgolay', 'movmean', 'movmedian'}
            got = smoothdata(mirror, m{1}, 7);
            assert(max(abs(got - fliplr(got))) < 1e-9, m{1});
        end
        """);

    /// <summary>
    /// A missing reading kept spoils exactly the windows that hold it; a missing reading stepped
    /// over — which is what <c>smoothdata</c> does unless told otherwise — spoils none of them.
    /// </summary>
    [Fact]
    public Task AMissingReadingReachesItsOwnWindowsAndNoOthers() => RunAsserting("""
        x = 1:41;
        x(21) = NaN;
        % How far a gap reaches is how far the window actually looks, which is not always how wide
        % it is: a tricube weight is exactly zero at the outer edge of its window, so a fit that
        % carries those weights never reads the two readings furthest from where it is centred.
        names = {'gaussian', 'sgolay', 'lowess', 'loess'};
        reach = {19:23, 19:23, 20:22, 20:22};
        for k = 1:numel(names)
            kept = smoothdata(x, names{k}, 5, 'includenan');
            spoiled = find(isnan(kept));
            assert(isequal(spoiled(:)', reach{k}), names{k});
            assert(~any(isnan(smoothdata(x, names{k}, 5))), names{k});
        end
        """);

    /// <summary>
    /// Stepping over a missing reading changes the windows that hold it and no others, so those
    /// are walked and the rest are kernelled. Both roads have to answer the same thing, and the
    /// places-of-its-own form — which is always walked — is what says whether they do.
    /// </summary>
    [Fact]
    public Task TheWalkedWindowsAndTheKernelledOnesAgree() => RunAsserting("""
        t = linspace(0, 6, 200);
        x = sin(t) + 0.3 * cos(3 * t);
        holed = x;
        holed(100) = NaN;
        for m = {'gaussian', 'lowess', 'loess', 'sgolay'}
            % The kernel answers most of the series and the walk repairs what the gap reached.
            mixed = smoothdata(holed, m{1}, 11);
            % Places of its own take the walk from end to end, over the very same readings.
            walked = smoothdata(holed, m{1}, 11, 'SamplePoints', 1:200);
            assert(~any(isnan(mixed)), m{1});
            assert(max(abs(mixed - walked)) < 1e-8, m{1});
            % Away from the gap nothing moved at all, since nothing there was walked.
            clean = smoothdata(x, m{1}, 11);
            keep = [1:93 108:200];
            assert(isequal(mixed(keep), clean(keep)), m{1});
        end
        % Missing readings dense enough to reach every window send the whole slice to the walk.
        many = x;
        many(2:2:end) = NaN;
        assert(~any(isnan(smoothdata(many, 'sgolay', 11))));
        """);

    /// <summary>
    /// The robust fits are not the plain ones: a single wild reading moves a lowess fit and barely
    /// moves an rlowess one, which is the whole reason the robust passes exist.
    /// </summary>
    [Fact]
    public Task TheRobustFitsHoldAgainstAWildReading() => RunAsserting("""
        x = linspace(0, 4, 81);
        clean = 2 + 0.5 * x;
        spoiled = clean;
        spoiled(41) = 60;
        plain = smoothdata(spoiled, 'lowess', 21);
        held = smoothdata(spoiled, 'rlowess', 21);
        % Measured at the wild reading's own neighbours, where the pull is strongest.
        assert(max(abs(plain(36:46) - clean(36:46))) > 1);
        assert(max(abs(held(36:46) - clean(36:46))) < max(abs(plain(36:46) - clean(36:46))));
        """);

    /// <summary>The window may still be given as places of its own rather than as a count.</summary>
    [Fact]
    public Task PlacesOfItsOwnStillReachTheSameWindow() => RunAsserting("""
        x = linspace(-2, 2, 60);
        curve = 1 + x + x.^2;
        % Evenly spread places one apart are the same window a plain count asks for.
        byCount = smoothdata(curve, 'loess', 11);
        byPlace = smoothdata(curve, 'loess', 11, 'SamplePoints', 1:60);
        assert(max(abs(byCount - byPlace)) < 1e-8);
        % Unevenly spread places are a different window, and still answer the curve they fit.
        uneven = smoothdata(curve, 'loess', 1, 'SamplePoints', x);
        assert(max(abs(uneven - curve)) < 1e-6);
        """);

    /// <summary>The degree a Savitzky&#8211;Golay fit is asked for is the degree it fits.</summary>
    [Fact]
    public Task TheDegreeAskedForIsTheDegreeFitted() => RunAsserting("""
        x = linspace(-2, 2, 100);
        cubic = 1 - x + 0.5 * x.^2 - 0.25 * x.^3;
        % Degree two cannot follow a cubic; degree three reproduces it.
        assert(max(abs(smoothdata(cubic, 'sgolay', 31, 'Degree', 2) - cubic)) > 1e-4);
        assert(max(abs(smoothdata(cubic, 'sgolay', 31, 'Degree', 3) - cubic)) < 1e-8);
        """);
}
