using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The Signal Processing Toolbox's windows, waveform generators, transforms and conversions (M132).
/// </summary>
/// <remarks>
/// The numbers here are R2025b's, recorded by the parity fixture <c>m132_windows_generators.m</c>.
/// What this class adds beyond the fixture is everything a fixture built out of digests cannot
/// see: the shape each name answers in, which argument it refuses and in what words, and the pairs
/// of names that have to agree with each other — a window at its two flavours, a transform against
/// its inverse, a quantiser against its dequantiser.
/// </remarks>
[Collection("JG facade")]
public class MatlabSignalM132Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabSignalM132Tests() => JG.Reset();

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

    // --- windows -------------------------------------------------------------------------------

    /// <summary>Every window answers a column, whatever else it does.</summary>
    [Fact]
    public void EveryWindowAnswersAColumn() =>
        Assert.Equal("8 1|8 1|8 1|8 1|8 1|8 1|8 1|8 1|8 1|8 1", Run("""
            names = {'hamming', 'hann', 'blackman', 'bartlett', 'triang', ...
                     'kaiser', 'chebwin', 'gausswin', 'tukeywin', 'taylorwin'};
            out = '';
            for k = 1:numel(names)
                s = size(window(names{k}, 8));
                out = [out sprintf('%d %d', s(1), s(2))];
                if k < numel(names)
                    out = [out '|'];
                end
            end
            fprintf('%s', out);
            """));

    /// <summary>
    /// A length of zero is an empty column and a length of one is a bare one, for every window:
    /// the two lengths at which no window has a shape.
    /// </summary>
    [Fact]
    public void TheTwoTrivialLengthsAreTheSameForEveryWindow() =>
        Assert.Equal("0 1 / 1 1 1", Run("""
            s = size(hamming(0));
            fprintf('%d %d / %d %d %g', s(1), s(2), size(kaiser(1), 1), size(kaiser(1), 2), chebwin(1));
            """));

    /// <summary>
    /// The periodic form is the symmetric one at the next length with its repeated endpoint
    /// dropped, which is exactly how MATLAB builds it and is worth stating as an identity.
    /// </summary>
    [Fact]
    public void APeriodicWindowIsTheSymmetricOneOfTheNextLengthWithoutItsLastPoint() =>
        Assert.Equal("0 0 0 0", Run("""
            names = {'hann', 'hamming', 'blackman', 'flattopwin'};
            for k = 1:numel(names)
                a = window(names{k}, 16, 'periodic');
                b = window(names{k}, 17);
                fprintf('%g ', max(abs(a - b(1:16))));
            end
            """).TrimEnd());

    /// <summary>
    /// <c>hanning</c> is not <c>hann</c>. It leaves out the two zero endpoints, so its first
    /// coefficient is not zero and its denominator is one larger.
    /// </summary>
    [Fact]
    public void HanningIsNotHann() =>
        Assert.Equal("0.0000000000 0.0669872981", Run("""
            h = hann(11);
            g = hanning(11);
            fprintf('%.10f %.10f', h(1), g(1));
            """));

    /// <summary>
    /// The raised-cosine windows are symmetric bit for bit, because MATLAB computes one half and
    /// reflects it rather than evaluating the cosine twice.
    /// </summary>
    [Fact]
    public void TheReflectedWindowsAreExactlySymmetric() =>
        Assert.Equal("1 1 1 1", Run("""
            names = {'hann', 'hamming', 'blackman', 'flattopwin'};
            for k = 1:numel(names)
                w = window(names{k}, 65);
                fprintf('%d ', isequal(w, flipud(w)));
            end
            """).TrimEnd());

    /// <summary>A Blackman window starts at exactly zero, which three rounded terms would not.</summary>
    [Fact]
    public void BlackmanIsPinnedToZeroAtItsEnds() =>
        Assert.Equal("0 0", Run("w = blackman(32); fprintf('%g %g', w(1), w(end));"));

    /// <summary>A tapered cosine window with no taper is rectangular and with full taper is Hann.</summary>
    [Fact]
    public void TukeysTwoLimitsAreTheTwoWindowsItInterpolates() =>
        Assert.Equal("1 1", Run("""
            fprintf('%d %d', isequal(tukeywin(24, 0), rectwin(24)), isequal(tukeywin(24, 1), hann(24)));
            """));

    /// <summary>A length that is not whole is rounded rather than refused.</summary>
    [Fact]
    public void AFractionalLengthIsRounded() =>
        Assert.Equal("8 8 9", Run("""
            fprintf('%d %d %d', numel(hamming(8.4)), numel(hamming(7.5)), numel(hamming(8.6)));
            """));

    /// <summary><c>window</c> reaches every one of them, by handle or by name.</summary>
    [Fact]
    public void WindowCallsAnyOfThemAndPassesTheirParametersOn() =>
        Assert.Equal("1 1 1", Run("""
            fprintf('%d %d %d', ...
                isequal(window(@hamming, 16), hamming(16)), ...
                isequal(window(@gausswin, 16, 2.5), gausswin(16, 2.5)), ...
                isequal(window(@taylorwin, 16, 5, -35), taylorwin(16, 5, -35)));
            """));

    /// <summary>A window that does not take a flavour refuses one.</summary>
    [Fact]
    public void APlainWindowRefusesASymmetryFlag() =>
        Assert.Contains("bartlett", Refuses("bartlett(8, 'periodic')"));

    /// <summary>An unknown flavour is named in the refusal rather than silently ignored.</summary>
    [Fact]
    public void AnUnknownSymmetryFlagIsRefused() =>
        Assert.Contains("'symmetric' or 'periodic'", Refuses("hamming(8, 'cyclic')"));

    // --- dpss ----------------------------------------------------------------------------------

    /// <summary>
    /// The Slepian sequences are orthonormal, and the first is the most concentrated: two
    /// properties that hold whatever the sign convention, and that a wrong eigenvector breaks.
    /// </summary>
    [Fact]
    public void TheSlepianSequencesAreOrthonormalAndOrderedByConcentration() =>
        Assert.Equal("1 1 1", Run("""
            [e, v] = dpss(64, 4);
            g = e' * e;
            fprintf('%d %d %d', ...
                max(abs(g(:) - reshape(eye(size(e, 2)), [], 1))) < 1e-10, ...
                all(diff(v) < 0), ...
                v(1) > 0.999);
            """));

    /// <summary>MATLAB's sign convention: an odd sequence has a positive mean, an even one a positive second sample.</summary>
    [Fact]
    public void TheSignConventionIsFixedRatherThanLeftToTheSolver() =>
        Assert.Equal("1 1 1 1", Run("""
            e = dpss(48, 3);
            fprintf('%d %d %d %d', mean(e(:, 1)) > 0, e(2, 2) > 0, mean(e(:, 3)) > 0, e(2, 4) > 0);
            """));

    /// <summary>A pair of indices asks for a slice of the sequence list rather than a prefix of it.</summary>
    [Fact]
    public void AnIndexPairSelectsARangeOfSequences() =>
        Assert.Equal("24 3 1", Run("""
            [e, v] = dpss(24, 3, [2 4]);
            f = dpss(24, 3, 4);
            fprintf('%d %d %d', size(e, 1), size(e, 2), max(abs(e(:, 1) - f(:, 2))) < 1e-10);
            """));

    /// <summary>The interpolated forms read a table from disk that JGraph does not keep, and say so.</summary>
    [Fact]
    public void TheInterpolatedFormsAreRefusedInWords() =>
        Assert.Contains("stored table", Refuses("dpss(64, 4, 'spline')"));

    // --- generators ----------------------------------------------------------------------------

    /// <summary>Every elementwise generator keeps the shape it was handed.</summary>
    [Fact]
    public void TheGeneratorsKeepTheShapeOfTheirTimeAxis() =>
        Assert.Equal("2 3|2 3|2 3|2 3|2 3|1 6|6 1", Run("""
            t = reshape(linspace(0, 1, 6), 2, 3);
            names = {'sinc', 'sawtooth', 'square', 'rectpuls', 'tripuls'};
            out = '';
            for k = 1:numel(names)
                s = size(feval(names{k}, t));
                out = [out sprintf('%d %d|', s(1), s(2))];
            end
            r = size(sinc(linspace(0, 1, 6)));
            c = size(sinc(linspace(0, 1, 6)'));
            fprintf('%s%d %d|%d %d', out, r(1), r(2), c(1), c(2));
            """));

    /// <summary>
    /// A rectangular pulse is closed on the left and open on the right, which is what keeps two
    /// abutting pulses from sharing a sample.
    /// </summary>
    [Fact]
    public void ARectangularPulseOwnsItsLeftEdgeAndNotItsRight() =>
        Assert.Equal("1 0", Run("fprintf('%g %g', rectpuls(-0.5), rectpuls(0.5));"));

    /// <summary>The Dirichlet kernel falls back to a sign where its denominator vanishes.</summary>
    [Fact]
    public void TheDirichletKernelHasAValueAtItsPoles() =>
        Assert.Equal("1 1 -1", Run("""
            fprintf('%g %g %g', diric(0, 7), diric(0, 8), diric(2 * pi, 8));
            """));

    /// <summary>A chirp with no sweep is a plain cosine, which pins the phase convention.</summary>
    [Fact]
    public void AChirpWithNoSweepIsACosineAndItsPhaseIsInDegrees() =>
        Assert.Equal("1 1", Run("""
            t = 0:0.001:0.05;
            a = max(abs(chirp(t, 50, 1, 50) - cos(2 * pi * 50 * t))) < 1e-12;
            b = max(abs(chirp(t, 50, 1, 50, 'linear', 90) - cos(2 * pi * 50 * t + pi / 2))) < 1e-12;
            fprintf('%d %d', a, b);
            """));

    /// <summary>The analytic chirp's real part is the real chirp.</summary>
    [Fact]
    public void TheComplexChirpCarriesTheRealOneAsItsRealPart() =>
        Assert.Equal("1", Run("""
            t = 0:0.001:0.05;
            y = chirp(t, 0, 1, 250, 'linear', 0, 'complex');
            fprintf('%d', max(abs(real(y) - chirp(t, 0, 1, 250))) < 1e-12);
            """));

    /// <summary>A quadratic sweep forced to bend the other way is not the sweep that bends by default.</summary>
    [Fact]
    public void ForcingAQuadraticSweepToBendChangesIt() =>
        Assert.Equal("1", Run("""
            t = 0:0.001:0.5;
            a = chirp(t, 100, 1, 200, 'quadratic', 0, 'convex');
            b = chirp(t, 100, 1, 200, 'quadratic', 0, 'concave');
            fprintf('%d', max(abs(a - b)) > 0.1);
            """));

    /// <summary>Both cutoff forms answer a time rather than a waveform.</summary>
    [Fact]
    public void TheTwoCutoffFormsAnswerADuration() =>
        Assert.Equal("1 1", Run("""
            fprintf('%d %d', isscalar(gauspuls('cutoff', 50e3, 0.6, [], -40)), ...
                isscalar(gmonopuls('cutoff', 2e3)));
            """));

    /// <summary>A pulse train of one pulse at zero delay is that pulse.</summary>
    [Fact]
    public void APulseTrainOfOnePulseIsThePulse() =>
        Assert.Equal("1", Run("""
            t = -1:0.01:1;
            fprintf('%d', max(abs(pulstran(t, 0, 'rectpuls', 0.5) - rectpuls(t, 0.5))) < 1e-15);
            """));

    /// <summary>A second column of delays scales each copy.</summary>
    [Fact]
    public void ASecondColumnOfDelaysScalesEachCopy() =>
        Assert.Equal("1", Run("""
            t = -1:0.01:1;
            a = pulstran(t, [-0.4 2; 0.4 3], 'rectpuls', 0.2);
            b = 2 * rectpuls(t + 0.4, 0.2) + 3 * rectpuls(t - 0.4, 0.2);
            fprintf('%d', max(abs(a - b)) < 1e-15);
            """));

    // --- transforms ----------------------------------------------------------------------------

    /// <summary>The analytic signal's real part is the signal, and its spectrum is one-sided.</summary>
    [Fact]
    public void TheAnalyticSignalKeepsItsRealPartAndDropsItsNegativeFrequencies() =>
        Assert.Equal("1 1", Run("""
            x = cos(2 * pi * 5 * (0:63) / 64) + 0.3 * sin(2 * pi * 11 * (0:63) / 64);
            h = hilbert(x);
            s = fft(h);
            fprintf('%d %d', max(abs(real(h) - x)) < 1e-12, max(abs(s(34:64))) < 1e-10);
            """));

    /// <summary>
    /// A chirp z-transform along the unit circle at the signal's own length is the Fourier
    /// transform, which is the identity that says the spiral is parameterised the right way round.
    /// </summary>
    [Fact]
    public void TheChirpZTransformOnTheUnitCircleIsTheFourierTransform() =>
        Assert.Equal("1", Run("""
            x = (1:16) + 1i * (16:-1:1);
            fprintf('%d', max(abs(czt(x) - fft(x))) < 1e-11);
            """));

    /// <summary>Goertzel's recurrence answers the same bins the transform does.</summary>
    [Fact]
    public void GoertzelAgreesWithTheTransformAtTheBinsItIsAsked() =>
        Assert.Equal("1", Run("""
            x = cos(2 * pi * 3 * (0:31) / 32) + 0.5;
            f = fft(x);
            g = goertzel(x, [1 4 9]);
            fprintf('%d', max(abs(g - f([1 4 9]))) < 1e-10);
            """));

    /// <summary>The Walsh-Hadamard transform undoes itself in each of its three orderings.</summary>
    [Fact]
    public void TheWalshTransformUndoesItselfInEveryOrdering() =>
        Assert.Equal("1 1 1", Run("""
            x = sin((1:32) / 3) + 0.4;
            names = {'sequency', 'hadamard', 'dyadic'};
            for k = 1:numel(names)
                fprintf('%d ', max(abs(ifwht(fwht(x, [], names{k}), [], names{k}) - x)) < 1e-12);
            end
            """).TrimEnd());

    /// <summary>A length that is not a power of two is padded up to one, and a given length is not.</summary>
    [Fact]
    public void TheWalshTransformPadsToAPowerOfTwo() =>
        Assert.Equal("32 64", Run("""
            fprintf('%d %d', numel(fwht(1:20)), numel(fwht(1:20, 64)));
            """));

    /// <summary>A transform length that is not a power of two is refused rather than rounded.</summary>
    [Fact]
    public void AWalshLengthThatIsNotAPowerOfTwoIsRefused() =>
        Assert.Contains("power of two", Refuses("fwht(1:20, 20)"));

    /// <summary>The complex cepstrum and its inverse are a pair, delay and all.</summary>
    [Fact]
    public void TheComplexCepstrumInvertsWithItsDelay() =>
        Assert.Equal("1", Run("""
            x = [1 2 3 4 3 2 1 0.5 0.25 0.125];
            [c, nd] = cceps(x);
            fprintf('%d', max(abs(icceps(c, nd) - x)) < 1e-10);
            """));

    /// <summary>The minimum-phase signal a real cepstrum names has the same magnitude spectrum.</summary>
    [Fact]
    public void TheMinimumPhaseSignalSharesItsMagnitudeSpectrum() =>
        Assert.Equal("1", Run("""
            x = [1 2 3 4 3 2 1 0.5];
            [~, y] = rceps(x);
            fprintf('%d', max(abs(abs(fft(y)) - abs(fft(x)))) < 1e-10);
            """));

    /// <summary>The transform matrix times a signal is the signal's transform.</summary>
    [Fact]
    public void TheTransformMatrixIsTheTransform() =>
        Assert.Equal("1", Run("""
            x = (1:8)';
            fprintf('%d', max(abs(dftmtx(8) * x - fft(x))) < 1e-11);
            """));

    /// <summary>Digit reversal is its own inverse, in every radix.</summary>
    [Fact]
    public void DigitReversalUndoesItself() =>
        Assert.Equal("1 1", Run("""
            x = (1:16)';
            fprintf('%d %d', isequal(bitrevorder(bitrevorder(x)), x), ...
                isequal(digitrevorder(digitrevorder(x, 4), 4), x));
            """));

    /// <summary>A length that is not a power of the radix is refused.</summary>
    [Fact]
    public void DigitReversalRefusesALengthThatIsNotAPowerOfTheRadix() =>
        Assert.Contains("power of 4", Refuses("digitrevorder(1:8, 4)"));

    // --- conversions and framing -----------------------------------------------------------------

    /// <summary>The four decibel conversions are two inverse pairs, ten apart.</summary>
    [Fact]
    public void TheDecibelConversionsAreTwoInversePairs() =>
        Assert.Equal("1 1 1", Run("""
            v = [0.01 0.5 1 2 100];
            fprintf('%d %d %d', ...
                max(abs(db2mag(mag2db(v)) - v)) < 1e-12, ...
                max(abs(db2pow(pow2db(v)) - v)) < 1e-12, ...
                max(abs(mag2db(v) - 2 * pow2db(v))) < 1e-12);
            """));

    /// <summary>A power of exactly one is exactly zero decibels, which the detour through 300 buys.</summary>
    [Fact]
    public void OneIsExactlyZeroDecibels() =>
        Assert.Equal("0 0", Run("fprintf('%g %g', pow2db(1), mag2db(1));"));

    /// <summary>One output pads the last frame; two hand the leftover back untouched.</summary>
    [Fact]
    public void BufferPadsWithOneOutputAndKeepsTheLeftoverWithTwo() =>
        Assert.Equal("4 3 / 4 2 / 1 2", Run("""
            a = size(buffer(1:10, 4));
            [y, z] = buffer(1:10, 4);
            b = size(y);
            c = size(z);
            fprintf('%d %d / %d %d / %d %d', a(1), a(2), b(1), b(2), c(1), c(2));
            """));

    /// <summary>With overlap the first frame is delayed unless the caller says not to.</summary>
    [Fact]
    public void BufferDelaysTheFirstFrameUnlessToldNotTo() =>
        Assert.Equal("0 1", Run("""
            a = buffer(1:10, 4, 1);
            b = buffer(1:10, 4, 1, 'nodelay');
            fprintf('%g %g', a(1, 1), b(1, 1));
            """));

    /// <summary>The third output is what the next call is handed, so a stream can be buffered in pieces.</summary>
    [Fact]
    public void BufferCarriesItsOverlapIntoTheNextCall() =>
        Assert.Equal("1", Run("""
            x = 1:24;
            [y1, ~, o] = buffer(x(1:12), 4, 2);
            y2 = buffer(x(13:24), 4, 2, o);
            whole = buffer(x, 4, 2);
            fprintf('%d', isequal([y1 y2], whole(:, 1:size(y1, 2) + size(y2, 2))));
            """));

    /// <summary>Wrapping is the frames of a buffer added together.</summary>
    [Fact]
    public void WrappingIsBufferingAndAdding() =>
        Assert.Equal("1 1", Run("""
            x = 1:10;
            fprintf('%d %d', isequal(datawrap(x, 4), sum(buffer(x, 4), 2)'), ...
                isequal(size(datawrap(x', 4)), [4 1]));
            """));

    /// <summary>The period of a repeating sequence, and the whole length when nothing repeats.</summary>
    [Fact]
    public void SequencePeriodFindsTheShortestRepeatOrGivesUp() =>
        Assert.Equal("3 1 5", Run("""
            fprintf('%d %d %d', seqperiod([1 2 3 1 2 3 1 2]), seqperiod([4 4 4 4 4]), ...
                seqperiod([1 2 3 4 5]));
            """));

    /// <summary>The tolerance is absolute and defaults to a tenth of a nano.</summary>
    [Fact]
    public void SequencePeriodComparesWithinATolerance() =>
        Assert.Equal("6 3", Run("""
            x = [1 2 3 1 2 3.0000001];
            fprintf('%d %d', seqperiod(x), seqperiod(x, 1e-5));
            """));

    /// <summary>Shifting a dimension to the front and back is the identity.</summary>
    [Fact]
    public void ShiftingADimensionAndBackIsTheIdentity() =>
        Assert.Equal("3 2 4 / 1", Run("""
            x = reshape(1:24, 2, 3, 4);
            [y, perm, nshifts] = shiftdata(x, 2);
            s = size(y);
            fprintf('%d %d %d / %d', s(1), s(2), s(3), isequal(unshiftdata(y, perm, nshifts), x));
            """));

    /// <summary>Quantising and dequantising lands in the middle of the interval, not on the sample.</summary>
    [Fact]
    public void QuantisingAndBackLandsWithinOneStep() =>
        Assert.Equal("1 1", Run("""
            u = -1:0.01:1;
            y = udecode(uencode(u, 8), 8);
            fprintf('%d %d', max(abs(y - u)) < 2 / 255, class(uencode(u, 8)) == "uint8");
            """));

    /// <summary>The word length decides the storage class, and the sign flag decides its signedness.</summary>
    [Fact]
    public void TheWordLengthDecidesTheStorageClass() =>
        Assert.Equal("uint8 uint16 uint32 int8", Run("""
            u = -1:0.5:1;
            fprintf('%s %s %s %s', class(uencode(u, 8)), class(uencode(u, 16)), ...
                class(uencode(u, 32)), class(uencode(u, 8, 1, 'signed')));
            """));

    /// <summary>Dequantising needs an integer class, because the class is where the signedness lives.</summary>
    [Fact]
    public void DequantisingRefusesPlainDoubles() =>
        Assert.Contains("int8", Refuses("udecode([1 2 3], 3)"));

    /// <summary>Marcum's Q is one at zero and falls monotonically.</summary>
    [Fact]
    public void MarcumsQStartsAtOneAndFalls() =>
        Assert.Equal("1 1 1", Run("""
            b = 0:0.5:8;
            q = marcumq(2, b);
            fprintf('%d %d %d', q(1) == 1, all(diff(q) <= 0), q(end) < 1e-4);
            """));

    /// <summary>
    /// Modulating and demodulating recovers the message at half its amplitude, because mixing
    /// against the carrier a second time splits the message between direct current and twice the
    /// carrier and the low-pass keeps only the first half. MATLAB does not put the factor back.
    /// </summary>
    [Fact]
    public void AmplitudeModulationRoundTripsAtHalfAmplitude() =>
        Assert.Equal("1 1", Run("""
            fs = 4000;
            t = (0:1/fs:0.1)';
            x = sin(2 * pi * 15 * t);
            y = demod(modulate(x, 100, fs, 'am'), 100, fs, 'am');
            n = 40:numel(t) - 40;
            fprintf('%d %d', max(abs(y(n) - x(n) / 2)) < 1e-2, max(abs(y(n) - x(n))) > 0.1);
            """));

    /// <summary>The second output is the time axis the modulation was built on.</summary>
    [Fact]
    public void ModulationAnswersItsTimeAxis() =>
        Assert.Equal("1 1", Run("""
            fs = 1000;
            x = (1:50)' / 50;
            [~, t] = modulate(x, 100, fs, 'am');
            fprintf('%d %d', numel(t) == 50, abs(t(end) - 49 / fs) < 1e-15);
            """));

    /// <summary>A carrier at or above half the sampling rate is refused rather than aliased.</summary>
    [Fact]
    public void AnAliasedCarrierIsRefused() =>
        Assert.Contains("half the sampling rate", Refuses("modulate(1:10, 600, 1000, 'am')"));

    /// <summary>Quadrature modulation carries two messages and demodulation gets both back.</summary>
    [Fact]
    public void QuadratureModulationCarriesTwoMessages() =>
        Assert.Equal("1 1", Run("""
            fs = 4000;
            t = (0:1/fs:0.1)';
            a = sin(2 * pi * 15 * t);
            b = 0.5 * sin(2 * pi * 10 * t);
            [p, q] = demod(modulate(a, 100, fs, 'qam', b), 100, fs, 'qam');
            n = 40:numel(t) - 40;
            fprintf('%d %d', max(abs(p(n) - a(n))) < 5e-2, max(abs(q(n) - b(n))) < 5e-2);
            """));

    /// <summary>Framing without overlap is reshaping, and the leftover is dropped by default.</summary>
    [Fact]
    public void FramingWithoutOverlapIsReshapingAndDropsTheRemainder() =>
        Assert.Equal("5 4 / 1", Run("""
            x = (1:22)';
            xw = framesig(x, 5);
            s = size(xw);
            fprintf('%d %d / %d', s(1), s(2), isequal(xw, reshape(x(1:20), 5, 4)));
            """));

    /// <summary>The overlap is counted forwards, unlike buffer's, and the carry continues the stream.</summary>
    [Fact]
    public void FramingCarriesItsFinalConditionForward() =>
        Assert.Equal("5 9 / 4", Run("""
            x = (1:22)';
            [xw, fc] = framesig(x, 5, OverlapLength = 3);
            s = size(xw);
            fprintf('%d %d / %d', s(1), s(2), numel(fc));
            """));

    /// <summary>Padding keeps the last incomplete frame instead of dropping it.</summary>
    [Fact]
    public void PaddingKeepsTheLastIncompleteFrame() =>
        Assert.Equal("4 5", Run("""
            x = (1:18)';
            fprintf('%d %d', size(framesig(x, 4), 2), ...
                size(framesig(x, 4, IncompleteFrameRule = 'zeropad'), 2));
            """));

    /// <summary>A window whose length is not the frame's is refused in words.</summary>
    [Fact]
    public void AFramingWindowMustMatchTheFrame() =>
        Assert.Contains("to match its frame", Refuses("framesig((1:20)', 5, Window = hamming(4))"));

    // --- the colon rule this milestone corrected -------------------------------------------------

    /// <summary>
    /// A range's second half is computed backwards from its last element, which is what MATLAB
    /// does and what keeps a comparison against a boundary landing on the same side.
    /// </summary>
    [Fact]
    public void ARangeIsComputedFromBothEnds() =>
        Assert.Equal("1 1 1", Run("""
            t = 0:0.1:1;
            fprintf('%d %d %d', t(7) == 1 - 4 * 0.1, t(3) == 2 * 0.1, t(7) ~= 6 * 0.1);
            """));

    /// <summary>
    /// A loop over a range written in its own head steps rather than reading the array, so the two
    /// spellings part company from the middle of the range onwards. That is MATLAB's behaviour and
    /// not a rounding accident: <c>for x = 0:0.1:1</c> reaches 0.6 by adding the step six times,
    /// and <c>v = 0:0.1:1</c> reaches it by counting back four steps from one.
    /// </summary>
    [Fact]
    public void ALoopOverARangeStepsWhereTheArrayCountsBack() =>
        Assert.Equal("1 1 1", Run("""
            v = 0:0.1:1;
            s = zeros(1, 11);
            k = 1;
            for x = 0:0.1:1
                s(k) = x;
                k = k + 1;
            end
            fprintf('%d %d %d', isequal(s(1:6), v(1:6)), s(7) ~= v(7), s(7) == 6 * 0.1);
            """));

    /// <summary>A loop over a variable holding a range walks the array, because that is what it is.</summary>
    [Fact]
    public void ALoopOverAStoredRangeWalksTheArray() =>
        Assert.Equal("1", Run("""
            v = 0:0.1:1;
            same = 1;
            k = 1;
            for x = v
                if x ~= v(k)
                    same = 0;
                end
                k = k + 1;
            end
            fprintf('%d', same);
            """));

    /// <summary>Whole-number ranges are untouched by the rule, because both roads are exact there.</summary>
    [Fact]
    public void WholeNumberRangesAreUnchanged() =>
        Assert.Equal("500500 1 100", Run("""
            v = 1:1000;
            fprintf('%d %d %d', sum(v), v(1), numel(1:100));
            """));
}
