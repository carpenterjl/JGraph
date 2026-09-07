using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The Signal Processing Toolbox's filtering, coefficient conversions and multirate names (M133).
/// </summary>
/// <remarks>
/// The numbers here are R2025b's, recorded by the parity fixture <c>m133_filtering.m</c>. What this
/// class adds beyond the fixture is what a fixture built out of digests and elementwise pins cannot
/// see: the shape each name answers in, which argument it refuses and in what words, and the pairs
/// of names that have to agree with each other — a conversion against its inverse, a lattice against
/// the polynomial it stands for, a rate change up against the same rate change down.
/// </remarks>
[Collection("JG facade")]
public class MatlabSignalM133Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabSignalM133Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private string Refuses(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success, "expected a refusal");
        return result.Message ?? string.Empty;
    }

    // --- the conversions come back to where they started -------------------------------------

    /// <summary>Roots to coefficients and back is the identity, up to the order of the roots.</summary>
    [Fact]
    public void TheRootsOfAPolynomialRebuildIt() =>
        Assert.Equal("1", Run("""
            b = [2 -1 0.5];
            a = [1 0.3 -0.1 0.05];
            [z, p, k] = tf2zp(b, a);
            [n, d] = zp2tf(z, p, k);
            fprintf('%d', max(abs(n - [0 b])) < 1e-12 && max(abs(d - a)) < 1e-12);
            """));

    /// <summary>A cascade multiplied out is the transfer function it was built from.</summary>
    [Fact]
    public void ACascadeMultipliesOutToItsTransferFunction() =>
        Assert.Equal("1", Run("""
            b = [1 -0.5 0.2 0.1];
            a = [1 0.3 -0.1 0.05];
            [sos, g] = tf2sos(b, a);
            [b2, a2] = sos2tf(sos, g);
            fprintf('%d', max(abs(b2 - b)) < 1e-10 && max(abs(a2 - a)) < 1e-10);
            """));

    /// <summary>The state-space form of a filter has the same transfer function.</summary>
    [Fact]
    public void StateSpaceRoundTripsThroughItsTransferFunction() =>
        Assert.Equal("1", Run("""
            b = [1 -0.5 0.2];
            a = [1 0.3 -0.1 0.05];
            [A, B, C, D] = tf2ss(b, a);
            [n, d] = ss2tf(A, B, C, D);
            fprintf('%d', max(abs(n - [0 b])) < 1e-9 && max(abs(d - a)) < 1e-9);
            """));

    /// <summary>A cascade written as a cell array reads back as the same cascade.</summary>
    [Fact]
    public void ACascadeSurvivesBeingWrittenAsCells() =>
        Assert.Equal("1", Run("""
            [sos, g] = tf2sos([1 -0.5 0.2 0.1], [1 0.3 -0.1 0.05]);
            c = sos2cell(sos, g);
            [s2, g2] = cell2sos(c);
            fprintf('%d', max(max(abs(s2 - sos))) < 1e-12 && abs(g2 - g) < 1e-12);
            """));

    /// <summary>
    /// A cascade's two-output form keeps the gain out, and its one-output form puts it in the first
    /// section — which is the same filter and not the same matrix.
    /// </summary>
    [Fact]
    public void OneOutputEmbedsTheGainInTheFirstSection() =>
        Assert.Equal("1 1", Run("""
            z = [0.5; -0.5];
            p = [0.6; 0.7; 0.8];
            [sos, g] = zp2sos(z, p, 3);
            one = zp2sos(z, p, 3);
            same = max(abs(one(1, 1:3) - g * sos(1, 1:3))) < 1e-14;
            rest = max(max(abs(one(2:end, :) - sos(2:end, :)))) < 1e-14;
            fprintf('%d %d', same, rest);
            """));

    /// <summary>
    /// A section with no zeros has the numerator <c>[0 0 1]</c>: the numerator is right-aligned
    /// against the denominator, so an all-pole section is a delay rather than a pass-through.
    /// </summary>
    [Fact]
    public void AnAllPoleSectionsNumeratorIsRightAligned() =>
        Assert.Equal("0 0 1", Run("""
            sos = zp2sos([], [0.5; -0.5], 1);
            fprintf('%g %g %g', sos(1, 1), sos(1, 2), sos(1, 3));
            """));

    /// <summary>The section count is the pole count rounded up, whatever the zeros do.</summary>
    [Theory]
    [InlineData("[]", "[0.5; -0.5]", 1)]
    [InlineData("[0.5]", "[0.6; 0.7; -0.8]", 2)]
    [InlineData("[0.5; -0.5]", "[0.1; 0.2; 0.3; 0.4; 0.5; 0.6]", 3)]
    public void ACascadeHasOneSectionPerPairOfPoles(string zeros, string poles, int sections) =>
        Assert.Equal(sections.ToString(), Run($"""
            sos = zp2sos({zeros}, {poles}, 1);
            fprintf('%d', size(sos, 1));
            """));

    /// <summary>Ordering down is ordering up read backwards, and nothing else.</summary>
    [Fact]
    public void DownIsUpReversed() =>
        Assert.Equal("1", Run("""
            z = [0.5; -0.5; 0.3+0.4i; 0.3-0.4i];
            p = [0.9; -0.7; 0.6; 0.2; 0.1];
            up = zp2sos(z, p, 1, 'up');
            down = zp2sos(z, p, 1, 'down');
            fprintf('%d', isequal(up, flipud(down)));
            """));

    /// <summary>Every conversion answers a six-column matrix, however many sections it has.</summary>
    [Fact]
    public void EveryCascadeHasSixColumns() =>
        Assert.Equal("6|6|6", Run("""
            a = zp2sos([], [0.5; -0.5], 1);
            b = tf2sos([1 -0.5 0.2], [1 0.3 -0.1 0.05]);
            [A, B, C, D] = tf2ss([1 -0.5], [1 0.3 -0.1]);
            c = ss2sos(A, B, C, D);
            fprintf('%d|%d|%d', size(a, 2), size(b, 2), size(c, 2));
            """));

    /// <summary>Both padding forms of <c>eqtflength</c> report the orders they found.</summary>
    [Fact]
    public void PaddingReportsTheOrdersItFound() =>
        Assert.Equal("2 2 1 1|4 4 1 3", Run("""
            [b, a, n, m] = eqtflength([1 2 0 0], [1 0.5]);
            fprintf('%d %d %d %d|', numel(b), numel(a), n, m);
            [b2, a2, n2, m2] = eqtflength([1 2], [1 0.5 0.2 0.1]);
            fprintf('%d %d %d %d', numel(b2), numel(a2), n2, m2);
            """));

    /// <summary>
    /// Stabilising a polynomial changes its magnitude response by a constant and not by a shape:
    /// reflecting a root through the unit circle scales the response by that root magnitude at
    /// every frequency, so the ratio of the two responses is flat.
    /// </summary>
    [Fact]
    public void StabilisingKeepsTheMagnitudeResponse() =>
        Assert.Equal("1 1", Run("""
            a = [1 2 3 4];
            b = polystab(a);
            inside = max(abs(roots(b))) <= 1 + 1e-12;
            w = linspace(0, pi, 64);
            ha = zeros(size(w));
            hb = zeros(size(w));
            for j = 1:numel(w)
                za = 0;
                zb = 0;
                for m = 1:4
                    za = za + a(m) * exp(1i*w(j)*(4-m));
                    zb = zb + b(m) * exp(1i*w(j)*(4-m));
                end
                ha(j) = abs(za);
                hb(j) = abs(zb);
            end
            ratio = ha ./ hb;
            flat = max(abs(ratio - ratio(1))) < 1e-10 * ratio(1);
            fprintf('%d %d', inside, flat);
            """));

    /// <summary>Scaling a polynomial scales its roots by the same factor.</summary>
    [Fact]
    public void ScalingAPolynomialScalesItsRoots() =>
        Assert.Equal("1", Run("""
            a = [1 -1.5 0.56];
            r = sort(roots(a));
            s = sort(roots(polyscale(a, 0.5)));
            fprintf('%d', max(abs(s - 0.5*r)) < 1e-12);
            """));

    /// <summary>The partial fraction of a ratio rebuilds it.</summary>
    [Fact]
    public void APartialFractionRebuildsItsRatio() =>
        Assert.Equal("1", Run("""
            b = [1 2];
            a = [1 -0.5 0.06];
            [r, p, k] = residuez(b, a);
            [b2, a2] = residuez(r, p, k);
            fprintf('%d', max(abs(real(b2) - b)) < 1e-10 && max(abs(real(a2) - a)) < 1e-10);
            """));

    // --- lattices -------------------------------------------------------------------------------

    /// <summary>A lattice and the polynomial it stands for are the same filter.</summary>
    [Fact]
    public void ALatticeRebuildsItsPolynomial() =>
        Assert.Equal("1", Run("""
            b = [1 0.5 0.25 0.125];
            k = tf2latc(b);
            n = latc2tf(k);
            fprintf('%d', max(abs(n - b)) < 1e-12);
            """));

    /// <summary>The ladder form round-trips a filter with both zeros and poles.</summary>
    [Fact]
    public void ALadderRebuildsItsRatio() =>
        Assert.Equal("1", Run("""
            b = [1 -0.5 0.2];
            a = [1 0.3 -0.1 0.05];
            [k, v] = tf2latc(b, a);
            [n, d] = latc2tf(k, v);
            fprintf('%d', max(abs(n(1:3) - b)) < 1e-10 && max(abs(d - a)) < 1e-10);
            """));

    /// <summary>An all-pole lattice's reflection coefficients are the denominator's.</summary>
    [Fact]
    public void AnAllPoleLatticeAnswersTheDenominatorsCoefficients() =>
        Assert.Equal("1", Run("""
            a = [1 0.3 -0.1 0.05];
            k = tf2latc(1, a);
            [n, d] = latc2tf(k, 'allpole');
            fprintf('%d', max(abs(d - a)) < 1e-12);
            """));

    /// <summary>An allpass lattice's numerator is its denominator read backwards.</summary>
    [Fact]
    public void AnAllpassLatticeMirrorsItself() =>
        Assert.Equal("1", Run("""
            [n, d] = latc2tf([0.5 0.25 0.125], 'allpass');
            fprintf('%d', max(abs(n - fliplr(d))) < 1e-14);
            """));

    /// <summary>A minimum-phase reading and a maximum-phase one are each other's mirror.</summary>
    [Fact]
    public void MaximumPhaseIsMinimumPhaseReversed() =>
        Assert.Equal("1", Run("""
            k = [0.5 0.25 0.125];
            lo = latc2tf(k, 'fir');
            hi = latc2tf(k, 'max');
            fprintf('%d', max(abs(hi - fliplr(lo))) < 1e-14);
            """));

    /// <summary>The lattice reading is refused for a filter that has poles.</summary>
    [Fact]
    public void AMinimumPhaseReadingIsRefusedForAFilterWithPoles() =>
        Assert.Contains("no poles", Refuses("tf2latc([1 0.5], [1 0.3], 'min');"));

    /// <summary>A lattice run forwards is the FIR filter its coefficients build.</summary>
    [Fact]
    public void AFeedForwardLatticeIsItsOwnFirFilter() =>
        Assert.Equal("1", Run("""
            k = [0.5 0.25];
            x = sin(2*pi*(0:99)'/17);
            f = latcfilt(k, x);
            b = latc2tf(k);
            fprintf('%d', max(abs(f - filter(b, 1, x))) < 1e-12);
            """));

    /// <summary>A lattice run backwards is the all-pole filter with the same coefficients.</summary>
    [Fact]
    public void AFeedbackLatticeIsItsOwnAllPoleFilter() =>
        Assert.Equal("1", Run("""
            k = [0.5 0.25];
            x = sin(2*pi*(0:99)'/17);
            f = latcfilt(k, 1, x);
            [n, d] = latc2tf(k, 'allpole');
            fprintf('%d', max(abs(f - filter(1, d, x))) < 1e-11);
            """));

    // --- filtering ------------------------------------------------------------------------------

    /// <summary>Zero-phase filtering leaves a symmetric signal symmetric.</summary>
    [Fact]
    public void ZeroPhaseFilteringKeepsSymmetry() =>
        Assert.Equal("1", Run("""
            x = [1:50, 50:-1:1]';
            y = filtfilt([1 1 1]/3, 1, x);
            fprintf('%d', max(abs(y - flipud(y))) < 1e-10);
            """));

    /// <summary>A row of samples comes back as a row.</summary>
    [Fact]
    public void EveryFilterKeepsTheShapeItWasGiven() =>
        Assert.Equal("1 100|100 1|1 100|100 1|1 100|100 1", Run("""
            xr = sin(2*pi*(0:99)/13);
            xc = xr';
            b = [1 1 1]/3;
            names = {};
            s1 = size(filtfilt(b, 1, xr)); s2 = size(filtfilt(b, 1, xc));
            s3 = size(sosfilt([b 1 0 0], xr)); s4 = size(sosfilt([b 1 0 0], xc));
            s5 = size(medfilt1(xr, 5)); s6 = size(medfilt1(xc, 5));
            fprintf('%d %d|%d %d|%d %d|%d %d|%d %d|%d %d', ...
                s1(1), s1(2), s2(1), s2(2), s3(1), s3(2), s4(1), s4(2), s5(1), s5(2), s6(1), s6(2));
            """));

    /// <summary>A cascade filters the same signal a chain of ordinary filters would.</summary>
    [Fact]
    public void ACascadeFiltersLikeItsSectionsInTurn() =>
        Assert.Equal("1", Run("""
            sos = [1 0.5 0.25 1 -0.3 0.1; 1 -1 0.5 1 0.2 -0.05];
            x = sin(2*pi*(0:199)'/23);
            y = sosfilt(sos, x);
            z = filter(sos(2, 1:3), sos(2, 4:6), filter(sos(1, 1:3), sos(1, 4:6), x));
            fprintf('%d', max(abs(y - z)) < 1e-12);
            """));

    /// <summary>Block convolution answers what direct convolution answers.</summary>
    [Fact]
    public void BlockConvolutionAgreesWithTheRecurrence() =>
        Assert.Equal("1", Run("""
            b = [1 2 3 2 1]/9;
            x = sin(2*pi*(0:499)'/31);
            fprintf('%d', max(abs(fftfilt(b, x) - filter(b, 1, x))) < 1e-11);
            """));

    /// <summary>The conditions from a past make a filter continue where it left off.</summary>
    [Fact]
    public void InitialConditionsContinueAFilter() =>
        Assert.Equal("1", Run("""
            b = [1 -0.5 0.2];
            a = [1 0.3 -0.1];
            x = sin(2*pi*(0:99)'/13);
            y = filter(b, a, x);
            zi = filtic(b, a, y(50:-1:1), x(50:-1:1));
            rest = filter(b, a, x(51:end), zi);
            fprintf('%d', max(abs(rest - y(51:end))) < 1e-10);
            """));

    /// <summary>A median filter is unmoved by a single wild sample.</summary>
    [Fact]
    public void AMedianFilterIgnoresOneWildSample() =>
        Assert.Equal("1", Run("""
            x = ones(21, 1);
            x(11) = 1000;
            y = medfilt1(x, 5);
            fprintf('%d', abs(y(11) - 1) < 1e-15);
            """));

    /// <summary>
    /// An even median window looks one further back than forward, and its first answer is the
    /// median of two padded zeros and the first two samples.
    /// </summary>
    [Fact]
    public void AnEvenMedianWindowLeansBackwards() =>
        Assert.Equal("0.5 3.5", Run("""
            x = (1:8)';
            y = medfilt1(x, 4);
            fprintf('%g %g', y(1), y(4));
            """));

    /// <summary>Hampel finds the samples that are put there to be found.</summary>
    [Fact]
    public void HampelFindsThePlantedOutliers() =>
        Assert.Equal("2 1", Run("""
            x = sin(2*pi*(0:199)'/25);
            x(40) = 9;
            x(41) = -9;
            [y, i] = hampel(x, 5, 3);
            fprintf('%d %d', sum(i), max(abs(y(40:41))) < 1);
            """));

    /// <summary>A Savitzky-Golay fit of degree one through a straight line is that line.</summary>
    [Fact]
    public void ALocalFitReproducesAStraightLine() =>
        Assert.Equal("1", Run("""
            x = (1:100)';
            y = sgolayfilt(x, 1, 11);
            fprintf('%d', max(abs(y - x)) < 1e-9);
            """));

    /// <summary>The projection matrix is symmetric and idempotent, which is what a projection is.</summary>
    [Fact]
    public void TheLocalFitsMatrixIsAProjection() =>
        Assert.Equal("1 1", Run("""
            b = sgolay(3, 11);
            fprintf('%d %d', max(max(abs(b - b'))) < 1e-12, max(max(abs(b*b - b))) < 1e-11);
            """));

    /// <summary>An even frame length is refused, because a fit has to have a centre.</summary>
    [Fact]
    public void AnEvenFrameLengthIsRefused() =>
        Assert.Contains("odd", Refuses("sgolay(3, 10);"));

    // --- multirate ------------------------------------------------------------------------------

    /// <summary>Upsampling and downsampling by the same factor is the identity.</summary>
    [Fact]
    public void DownsamplingUndoesUpsampling() =>
        Assert.Equal("1", Run("""
            x = sin(2*pi*(0:99)'/17);
            fprintf('%d', isequal(downsample(upsample(x, 4), 4), x));
            """));

    /// <summary>Upsampling puts the samples where the phase says and zeros everywhere else.</summary>
    [Fact]
    public void UpsamplingPlacesItsSamplesByPhase() =>
        Assert.Equal("0 0 1 0 0 2 0 0 3", Run("""
            y = upsample([1 2 3], 3, 2);
            fprintf('%g %g %g %g %g %g %g %g %g', y);
            """));

    /// <summary>A rate change's length is the formula its documentation gives.</summary>
    [Theory]
    [InlineData("upfirdn(x, h, 3, 2)", 301)]
    [InlineData("upfirdn(x, h, 1, 1)", 204)]
    [InlineData("upfirdn(x, h, 1, 4)", 51)]
    public void ARateChangesLengthIsItsFormula(string call, int length) =>
        Assert.Equal(length.ToString(), Run($"""
            x = sin(2*pi*(0:199)'/17);
            h = [1 2 3 2 1]/9;
            fprintf('%d', numel({call}));
            """));

    /// <summary>Interpolation is exactly as long as the input times the rate.</summary>
    [Fact]
    public void InterpolationIsExactlyAsLongAsItPromises() =>
        Assert.Equal("400 50", Run("""
            x = sin(2*pi*(0:99)'/17);
            fprintf('%d %d', numel(interp(x, 4)), numel(decimate(x, 2)));
            """));

    /// <summary>Interpolation leaves the original samples where they were.</summary>
    [Fact]
    public void InterpolationKeepsTheSamplesItStartedWith() =>
        Assert.Equal("1", Run("""
            x = sin(2*pi*(0:99)'/17);
            y = interp(x, 4);
            fprintf('%d', max(abs(y(1:4:end) - x)) < 1e-9);
            """));

    /// <summary>Resampling by a ratio that reduces to one is the identity.</summary>
    [Fact]
    public void ResamplingByOneChangesNothing() =>
        Assert.Equal("1 1", Run("""
            x = sin(2*pi*(0:99)'/17);
            [y, b] = resample(x, 3, 3);
            fprintf('%d %d', isequal(y, x), isscalar(b));
            """));

    /// <summary>A resampled signal has the length the ratio says, rounded up.</summary>
    [Theory]
    [InlineData(3, 2, 150)]
    [InlineData(5, 7, 72)]
    [InlineData(1, 4, 25)]
    public void ResamplingLengthIsTheRatioRoundedUp(int p, int q, int length) =>
        Assert.Equal(length.ToString(), Run($"""
            x = sin(2*pi*(0:99)'/17);
            fprintf('%d', numel(resample(x, {p}, {q})));
            """));

    /// <summary>
    /// Resampling a constant leaves it constant to the design's own passband ripple, which for the
    /// default Kaiser window is about six parts in ten thousand rather than machine precision.
    /// </summary>
    [Fact]
    public void ResamplingAConstantLeavesItConstant() =>
        Assert.Equal("1 1", Run("""
            x = ones(200, 1);
            y = resample(x, 3, 2);
            worst = max(abs(y(50:250) - 1));
            fprintf('%d %d', worst < 1e-3, worst > 1e-6);
            """));

    /// <summary>Decimation by one is the signal itself.</summary>
    [Fact]
    public void DecimatingByOneChangesNothing() =>
        Assert.Equal("1", Run("""
            x = sin(2*pi*(0:99)'/17);
            fprintf('%d', isequal(decimate(x, 1), x));
            """));

    /// <summary>The two decimation filters are both offered and they are not the same filter.</summary>
    [Fact]
    public void TheTwoDecimationFiltersDiffer() =>
        Assert.Equal("50 50 1", Run("""
            x = sin(2*pi*(0:199)'/17) + 0.2*sin(2*pi*(0:199)'/3);
            a = decimate(x, 4);
            b = decimate(x, 4, 'fir');
            fprintf('%d %d %d', numel(a), numel(b), max(abs(a - b)) > 1e-6);
            """));

    // --- repair ---------------------------------------------------------------------------------

    /// <summary>Gap filling leaves the samples that were there alone.</summary>
    [Fact]
    public void GapFillingLeavesTheGoodSamplesAlone() =>
        Assert.Equal("1 1", Run("""
            x = sin(2*pi*(0:199)'/25);
            g = x;
            g(80:90) = NaN;
            y = fillgaps(g, 40, 6);
            kept = max(abs(y([1:79, 91:200]) - x([1:79, 91:200]))) < 1e-12;
            filled = ~any(isnan(y));
            fprintf('%d %d', kept, filled);
            """));

    /// <summary>An envelope brackets the signal it came from.</summary>
    [Fact]
    public void AnEnvelopeBracketsItsSignal() =>
        Assert.Equal("1 1", Run("""
            x = sin(2*pi*(0:199)'/25) .* (1 + 0.5*sin(2*pi*(0:199)'/97));
            [u, l] = envelope(x, 30, 'rms');
            fprintf('%d %d', numel(u) == 200, all(u >= l));
            """));

    /// <summary>The analytic envelope of a pure tone is its amplitude.</summary>
    [Fact]
    public void TheAnalyticEnvelopeOfAToneIsItsAmplitude() =>
        Assert.Equal("1", Run("""
            n = (0:511)';
            x = 3 * sin(2*pi*n/32);
            u = envelope(x);
            fprintf('%d', max(abs(u(100:400) - 3)) < 1e-6);
            """));

    /// <summary>Each envelope method is offered and each answers a different curve.</summary>
    [Fact]
    public void TheThreeEnvelopeMethodsAllAnswer() =>
        Assert.Equal("200 200 200", Run("""
            x = sin(2*pi*(0:199)'/25) .* (1 + 0.5*sin(2*pi*(0:199)'/97));
            a = envelope(x, 30, 'analytic');
            b = envelope(x, 30, 'rms');
            c = envelope(x, 30, 'peaks');
            fprintf('%d %d %d', numel(a), numel(b), numel(c));
            """));

    /// <summary>An unknown envelope method is refused by name.</summary>
    [Fact]
    public void AnUnknownEnvelopeMethodIsRefused() =>
        Assert.Contains("'analytic'", Refuses("envelope(sin(1:100), 10, 'median');"));

    // --- the refusals ----------------------------------------------------------------------------

    /// <summary>A signal too short for the filter's transients is refused, not truncated.</summary>
    [Fact]
    public void ASignalTooShortForZeroPhaseFilteringIsRefused() =>
        Assert.Contains("more than", Refuses("filtfilt([1 -0.5 0.2], [1 0.3 -0.1 0.05], [1 2 3 4 5]);"));

    /// <summary>A cascade of the wrong width is refused rather than reinterpreted.</summary>
    [Fact]
    public void ACascadeOfTheWrongWidthIsRefused() =>
        Assert.Contains("six columns", Refuses("sosfilt([1 2 3 4 5], [1 2 3]);"));

    /// <summary>More zeros than poles is refused, because such a filter has no state space.</summary>
    [Fact]
    public void MoreZerosThanPolesIsRefused() =>
        Assert.Contains("zeros", Refuses("zp2sos([0.1; 0.2; 0.3], [0.4; 0.5], 1);"));

    /// <summary>The non-uniform resampling form, which takes a time vector, is declined in words.</summary>
    [Fact]
    public void TheNonUniformResamplingFormIsDeclined() =>
        Assert.Contains("time vector", Refuses("resample(sin(1:100), (1:100)/100);"));

    /// <summary>The digitalFilter form of zero-phase filtering is declined in words.</summary>
    [Fact]
    public void TheDigitalFilterFormOfZeroPhaseFilteringIsDeclined() =>
        Assert.Contains("digitalFilter", Refuses("filtfilt([1 2 3], [1 2 3]);"));

    /// <summary>A phase outside the rate change's factor is refused.</summary>
    [Fact]
    public void APhaseOutsideItsFactorIsRefused() =>
        Assert.Contains("phase", Refuses("downsample([1 2 3 4], 2, 5);"));

    /// <summary>The filter-state values carry the two vectors they are named for.</summary>
    [Fact]
    public void FilterStatesCarryTheirTwoVectors() =>
        Assert.Equal("3 2", Run("""
            h = filtstates.dfiir([1 2 3], [4 5]);
            fprintf('%d %d', numel(h.Numerator), numel(h.Denominator));
            """));
}
