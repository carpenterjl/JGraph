using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Signal;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M137: the time-frequency transforms, the modelling conversions and the vibration names, checked
/// through the MATLAB dialect for what a parity fixture cannot say — which shapes come back, which
/// options are refused and why, and the properties a transform is supposed to have rather than the
/// numbers it happens to answer.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabSignalM137Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabSignalM137Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code + ";", context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.TrimEnd('\n', '\r', ' ');
    }

    private string RunError(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success, "expected a refusal");
        return result.Message ?? string.Empty;
    }

    private const string Signal =
        "n = 512; fs = 1000; t = (0:n-1)'/fs; " +
        "x = cos(2*pi*80*t) + 0.5*sin(2*pi*230*t) + 0.02*mod((0:n-1)',7)/7; ";

    private const string Two = Signal + "y = 0.7*cos(2*pi*80*t + 0.4) + 0.02*mod((0:n-1)',5)/5; ";

    // --- The short-time transform ------------------------------------------------------------------

    [Fact]
    public void Spectrogram_KeepsOneSideOfARealSignal()
    {
        string got = Run(Signal + "s = spectrogram(x, hamming(128), 64, 256, fs); fprintf('%d %d', size(s))");
        Assert.Equal("129 7", got);
    }

    [Fact]
    public void Spectrogram_KeepsBothSidesOfAComplexSignal()
    {
        string got = Run(Signal
            + "z = x + 1i*circshift(x, 7); s = spectrogram(z, hamming(128), 64, 256, fs); fprintf('%d %d', size(s))");
        Assert.Equal("256 7", got);
    }

    [Fact]
    public void Spectrogram_TimesAreTheCentresOfTheFrames()
    {
        string got = Run(Signal
            + "[~, ~, tt] = spectrogram(x, hamming(128), 64, 256, fs); fprintf('%.6f %.6f', tt(1), tt(2)-tt(1))");
        Assert.Equal("0.064000 0.064000", got);
    }

    [Fact]
    public void Spectrogram_DownRowsTransposesEveryOutput()
    {
        string got = Run(Signal
            + "[s, f, tt] = spectrogram(x, hamming(128), 64, 256, fs, 'OutputTimeDimension', 'downrows'); "
            + "fprintf('%d %d %d %d %d', size(s), numel(f), size(tt))");
        Assert.Equal("7 129 129 7 1", got);
    }

    [Fact]
    public void Spectrogram_AFrequencyVectorMakesTheEstimateTwoSided()
    {
        string got = Run(Signal
            + "s = spectrogram(x, hamming(128), 64, [50 80 120]', fs); fprintf('%d %d', size(s))");
        Assert.Equal("3 7", got);
    }

    [Fact]
    public void Spectrogram_AThresholdOnlyEverRemovesEnergy()
    {
        string got = Run(Signal
            + "[~, ~, ~, p] = spectrogram(x, hamming(128), 64, 256, fs); "
            + "[~, ~, ~, q] = spectrogram(x, hamming(128), 64, 256, fs, 'MinThreshold', -40); "
            + "fprintf('%d %d', nnz(q) < nnz(p), all(all(q == 0 | q == p)))");
        Assert.Equal("1 1", got);
    }

    [Fact]
    public void Spectrogram_RefusesAWindowLongerThanTheSignal()
    {
        string got = RunError("spectrogram(1:10, hamming(64), 32, 64, 100);");
        Assert.Contains("window no longer than the signal", got);
    }

    [Fact]
    public void Stft_DefaultsToACentredTwoSidedTransform()
    {
        string got = Run(Signal + "[s, f] = stft(x, fs); fprintf('%d %d %d', size(s), f(1) < 0)");
        Assert.Equal("128 13 1", got);
    }

    [Fact]
    public void Stft_OneSidedKeepsHalfTheRowsAndNoNegativeFrequency()
    {
        string got = Run(Signal
            + "[s, f] = stft(x, fs, 'FrequencyRange', 'onesided'); fprintf('%d %d', size(s, 1), all(f >= 0))");
        Assert.Equal("65 1", got);
    }

    [Fact]
    public void Stft_RefusesAOneSidedTransformOfAComplexSignal()
    {
        string got = RunError(Signal + "stft(x + 1i*x, fs, 'FrequencyRange', 'onesided');");
        Assert.Contains("no one-sided transform for a complex signal", got);
    }

    [Fact]
    public void Stft_RefusesBothARangeAndACentredFlag()
    {
        string got = RunError(Signal + "stft(x, fs, 'FrequencyRange', 'onesided', 'Centered', true);");
        Assert.Contains("either 'FrequencyRange' or 'Centered'", got);
    }

    [Fact]
    public void Stft_AMultichannelSignalComesBackAsOnePagePerChannel()
    {
        string got = Run(Signal + "s = stft([x x], fs); fprintf('%d %d %d', size(s))");
        Assert.Equal("128 13 2", got);
    }

    [Fact]
    public void Istft_ReconstructsARealSignalAsReal()
    {
        string got = Run(Signal
            + "w = hann(128, 'periodic'); s = stft(x, fs, 'Window', w, 'OverlapLength', 96); "
            + "xr = istft(s, fs, 'Window', w, 'OverlapLength', 96); fprintf('%d', isreal(xr))");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Istft_TheRoundTripIsExactAwayFromTheEdges()
    {
        string got = Run(Signal
            + "w = hann(128, 'periodic'); s = stft(x, fs, 'Window', w, 'OverlapLength', 96); "
            + "xr = istft(s, fs, 'Window', w, 'OverlapLength', 96); "
            + "fprintf('%d', max(abs(xr(129:end-128) - x(129:numel(xr)-128))) < 1e-10)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Istft_RefusesAMapWithTheWrongNumberOfRows()
    {
        string got = RunError("istft(zeros(64, 5), 100, 'Window', hann(128, 'periodic'));");
        Assert.Contains("128 frequency rows", got);
    }

    [Fact]
    public void Iscola_AHannWindowAtThreeQuartersOverlapSatisfiesTheCondition()
    {
        string got = Run("fprintf('%d %d', iscola(hann(128, 'periodic'), 96), iscola(hamming(127), 60))");
        Assert.Equal("1 0", got);
    }

    [Fact]
    public void Xspectrogram_ReportsTheMagnitudeOfTheCrossSpectrum()
    {
        string got = Run(Two
            + "[s, ~, ~, c] = xspectrogram(x, y, hamming(128), 64, 256, fs); "
            + "fprintf('%d %d', isreal(s), max(max(abs(s - abs(c)))) < 1e-12)");
        Assert.Equal("1 1", got);
    }

    [Fact]
    public void Stftmag2sig_RefusesTheTwoMethodsThisBuildDoesNotHave()
    {
        string got = RunError(
            "stftmag2sig(ones(128, 4), 128, 'Window', hann(128, 'periodic'), 'Method', 'legla');");
        Assert.Contains("'legla' and 'gd' are not written", got);
    }

    // --- Synchrosqueezing --------------------------------------------------------------------------

    [Fact]
    public void Fsst_GivesOneColumnPerSampleAndHalfTheRows()
    {
        string got = Run(Signal + "s = fsst(x, fs, kaiser(64, 10)); fprintf('%d %d', size(s))");
        Assert.Equal("33 512", got);
    }

    [Fact]
    public void Fsst_AComplexSignalKeepsBothSidesCentred()
    {
        string got = Run(Signal
            + "[s, f] = fsst(x + 1i*circshift(x, 5), fs, kaiser(64, 10)); "
            + "fprintf('%d %d', size(s, 1), f(1) < 0)");
        Assert.Equal("64 1", got);
    }

    [Fact]
    public void Ifsst_InvertsWhatFsstProduced()
    {
        string got = Run(Signal
            + "w = kaiser(64, 10); s = fsst(x, fs, w); "
            + "fprintf('%d', max(abs(ifsst(s, w) - x)) < 1e-8)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Tfridge_TheLinearIndexAgreesWithTheRowIndex()
    {
        string got = Run(Signal
            + "w = kaiser(64, 10); [s, f] = fsst(x, fs, w); [~, ir, lr] = tfridge(s, f); "
            + "fprintf('%d', all(lr == ir + (0:size(s,2)-1)'*size(s,1)))");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Tfridge_APenaltyMakesTheRidgeNoLessSmooth()
    {
        string got = Run(Signal
            + "w = kaiser(64, 10); [s, f] = fsst(x, fs, w); "
            + "a = tfridge(s, f); b = tfridge(s, f, 5); "
            + "fprintf('%d', sum(abs(diff(b))) <= sum(abs(diff(a))))");
        Assert.Equal("1", got);
    }

    // --- The descriptors ---------------------------------------------------------------------------

    [Fact]
    public void SpectralFlatness_IsBetweenZeroAndOne()
    {
        string got = Run(Signal + "fl = spectralFlatness(x, fs); fprintf('%d', all(fl > 0 & fl <= 1))");
        Assert.Equal("1", got);
    }

    [Fact]
    public void SpectralEntropy_IsOneForAFlatSpectrumAndSmallForATone()
    {
        string got = Run(Signal
            + "flat = spectralEntropy(ones(64, 1), (0:63)'); "
            + "tone = spectralEntropy([1; zeros(63, 1)], (0:63)'); "
            + "fprintf('%.6f %d', flat, tone < 0.01)");
        Assert.Equal("1.000000 1", got);
    }

    [Fact]
    public void SpectralCrest_IsThePeakOverTheMean()
    {
        string got = Run(Signal
            + "[c, p, m] = spectralCrest(x, fs); fprintf('%d', max(abs(c - p./m)) < 1e-12)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void SpectralKurtosis_RefusesAConfidenceLevelWhenItIsScaled()
    {
        string got = RunError(Signal + "spectralKurtosis(x, fs, 'ConfidenceLevel', 0.9);");
        Assert.Contains("only when 'Scaled' is false", got);
    }

    [Fact]
    public void SpectralKurtosis_UnscaledReportsOneNumberPerFrequency()
    {
        string got = Run(Signal
            + "[k, ~, ~, ~, f] = spectralKurtosis(x, fs, 'Window', hamming(128), 'OverlapLength', 64, "
            + "'Scaled', false); fprintf('%d %d', numel(k), numel(k) == numel(f))");
        Assert.Equal("65 1", got);
    }

    [Fact]
    public void Kurtogram_HasTwoRowsPerLevelAndThreeColumnsPerBinaryBand()
    {
        string got = Run("x = sin(2*pi*0.05*(0:4095)') + 0.02*mod((0:4095)', 13)/13; "
            + "kg = kurtogram(x, 1000, 3); fprintf('%d %d', size(kg))");
        Assert.Equal("6 24", got);
    }

    [Fact]
    public void Kurtogram_TheChosenBandLiesUnderNyquist()
    {
        string got = Run("x = sin(2*pi*0.05*(0:4095)') + 0.02*mod((0:4095)', 13)/13; "
            + "[~, ~, ~, fc, ~, bw] = kurtogram(x, 1000); fprintf('%d', fc + bw/2 <= 500 + 1e-9)");
        Assert.Equal("1", got);
    }

    // --- Modelling ---------------------------------------------------------------------------------

    [Fact]
    public void Levinson_TheOrderIsCappedAtTheAutocorrelationsLength()
    {
        string got = Run("r = [4 2 1]'; a = levinson(r, 9); fprintf('%d', numel(a))");
        Assert.Equal("3", got);
    }

    [Fact]
    public void Levinson_AndRlevinsonAreInverses()
    {
        string got = Run("r = [4 2.5 1.2 0.4]'; [a, e] = levinson(r); "
            + "fprintf('%d', max(abs(rlevinson(a, e) - r)) < 1e-10)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Ac2poly_AndPoly2acAreInverses()
    {
        string got = Run("r = [4 2.5 1.2 0.4]'; [a, e] = ac2poly(r); "
            + "fprintf('%d', max(abs(poly2ac(a, e) - r)) < 1e-10)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Rc2poly_AndPoly2rcAreInverses()
    {
        string got = Run("k = [-0.6 0.3 -0.1]'; [a, e] = rc2poly(k, 4); "
            + "fprintf('%d', max(abs(poly2rc(a, e) - k)) < 1e-10)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Rc2is_AndIs2rcAreInverses()
    {
        string got = Run("k = [-0.6 0.3 -0.1]'; "
            + "fprintf('%d %d', max(abs(is2rc(rc2is(k)) - k)) < 1e-12, "
            + "max(abs(lar2rc(rc2lar(k)) - k)) < 1e-12)");
        Assert.Equal("1 1", got);
    }

    [Fact]
    public void Rc2is_RefusesACoefficientOutsideTheUnitInterval()
    {
        string got = RunError("rc2is([0.5 1.5]);");
        Assert.Contains("strictly between -1 and 1", got);
    }

    [Fact]
    public void Poly2lsf_AndLsf2polyAreInverses()
    {
        string got = Run("a = [1 -1.3 0.85 -0.2]; lsf = poly2lsf(a); "
            + "fprintf('%d', max(abs(lsf2poly(lsf) - a)) < 1e-10)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Poly2lsf_RefusesAnUnstablePolynomial()
    {
        string got = RunError("poly2lsf([1 -2.5 1.5]);");
        Assert.Contains("inside the unit circle", got);
    }

    [Fact]
    public void Lsf2poly_RefusesAFrequencyOutsideTheHalfCircle()
    {
        string got = RunError("lsf2poly([0.5 4]);");
        Assert.Contains("between 0 and pi", got);
    }

    [Fact]
    public void Schurrc_AgreesWithTheLevinsonRecursionsReflectionCoefficients()
    {
        string got = Run("r = [4 2.5 1.2 0.4]'; [~, ~, k] = levinson(r); "
            + "fprintf('%d', max(abs(schurrc(r) - k)) < 1e-10)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Arburg_ReportsOnePolynomialPerChannelAsARow()
    {
        string got = Run(Signal + "[a, e, k] = arburg([x x], 4); fprintf('%d %d %d %d %d %d', size(a), size(e), size(k))");
        Assert.Equal("2 5 1 2 4 2", got);
    }

    [Fact]
    public void Arcov_HasNoReflectionCoefficientsToGive()
    {
        string got = RunError(Signal + "[a, e, k] = arcov(x, 4);");
        Assert.Contains("no reflection coefficients", got);
    }

    [Fact]
    public void Arburg_RefusesAnOrderTheRecordCannotSupport()
    {
        string got = RunError("arburg([1 2 3], 5);");
        Assert.Contains("at least 6 samples", got);
    }

    [Fact]
    public void Lpc_FitsTheSpectrumItWasGivenRatherThanTheFilterBehindIt()
    {
        // Two tones driving an all-pole filter are not white, so the prediction polynomial that
        // whitens the result is not the filter's own. MATLAB says the same thing on the same
        // signal, and the point of the test is that this build agrees about what lpc estimates.
        string got = Run("a0 = [1 -1.3 0.85 -0.2]; "
            + "u = cos(2*pi*0.07*(0:2047)') + 0.4*sin(2*pi*0.19*(0:2047)'); "
            + "x = filter(1, a0, u); a = lpc(x, 3); "
            + "fprintf('%d %d', max(abs(a - a0)) > 0.5, "
            + "max(abs(roots(a))) < 1)");
        Assert.Equal("1 1", got);
    }

    [Fact]
    public void Prony_MatchesTheImpulseResponseItWasGiven()
    {
        string got = Run("h = filter([1 0.4 -0.2], [1 -0.9 0.3], [1 zeros(1, 63)]); "
            + "[b, a] = prony(h, 2, 2); "
            + "fprintf('%d', max(abs(filter(b, a, [1 zeros(1, 63)]) - h)) < 1e-10)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Stmcb_MatchesTheImpulseResponseItWasGiven()
    {
        string got = Run("h = filter([1 0.4 -0.2], [1 -0.9 0.3], [1 zeros(1, 63)]); "
            + "[b, a] = stmcb(h, 2, 2); "
            + "fprintf('%d', max(abs(filter(b, a, [1 zeros(1, 63)]) - h)) < 1e-8)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Invfreqz_RecoversAFilterFromItsOwnResponse()
    {
        string got = Run("[b0, a0] = butter(4, 0.3); [h, w] = freqz(b0, a0, 128); "
            + "[b, a] = invfreqz(h, w, 4, 4); "
            + "fprintf('%d', max(abs(freqz(b, a, 128) - h)) < 1e-8)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Invfreqs_RecoversAnAnalogueFilterFromItsOwnResponse()
    {
        string got = Run("[b0, a0] = besself(3, 1); w = logspace(-1, 1, 64)'; "
            + "h = freqs(b0, a0, w); [b, a] = invfreqs(h, w, 2, 3); "
            + "fprintf('%d', max(abs(freqs(b, a, w) - h)) < 1e-6)");
        Assert.Equal("1", got);
    }

    // --- Modal analysis ----------------------------------------------------------------------------

    [Fact]
    public void Modalfrf_HasOnePageOfResponsesPerInput()
    {
        string got = Run(Two + "[frf, f] = modalfrf(x, y, fs, hann(256), 128); "
            + "fprintf('%d %d %d %d', size(frf, 1), size(frf, 2), size(frf, 3), numel(f))");
        Assert.Equal("129 1 1 129", got);
    }

    [Fact]
    public void Modalfrf_RefusesTheSubspaceEstimatorThisBuildDoesNotHave()
    {
        string got = RunError(Two + "modalfrf(x, y, fs, hann(256), 128, 'Estimator', 'subspace');");
        Assert.Contains("no subspace estimator", got);
    }

    [Fact]
    public void Modalfit_RefusesTheRationalFitThisBuildDoesNotHave()
    {
        string got = RunError(Two + "[frf, f] = modalfrf(x, y, fs, hann(256), 128); "
            + "modalfit(frf, f, fs, 2, 'FitMethod', 'lsrf');");
        Assert.Contains("Control System Toolbox", got);
    }

    [Fact]
    public void Modalfit_ADampedPoleHasANonNegativeNaturalFrequency()
    {
        string got = Run(Two + "[frf, f] = modalfrf(x, y, fs, hann(256), 128); "
            + "[fn, dr] = modalfit(frf, f, fs, 2); "
            + "fprintf('%d', all(isnan(fn) | fn >= 0))");
        Assert.Equal("1", got);
    }

    // --- Vibration ---------------------------------------------------------------------------------

    [Fact]
    public void Rainflow_CountsHalfAndWholeCyclesOnly()
    {
        string got = Run("x = 3*sin(2*pi*0.013*(0:1999)') + 1.2*sin(2*pi*0.057*(0:1999)'); "
            + "c = rainflow(x, 500); fprintf('%d %d', size(c, 2), all(c(:,1) == 0.5 | c(:,1) == 1))");
        Assert.Equal("5 1", got);
    }

    [Fact]
    public void Rainflow_TheMatrixCountsHalfACycleForEachHalf()
    {
        string got = Run("x = 3*sin(2*pi*0.013*(0:1999)') + 1.2*sin(2*pi*0.057*(0:1999)'); "
            + "[c, rm] = rainflow(x, 500); fprintf('%d', abs(sum(rm(:)) - sum(c(:,1))) < 1e-9)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Rainflow_RefusesARecordTooShortToHaveACycle()
    {
        string got = RunError("rainflow([1 2]);");
        Assert.Contains("at least three samples", got);
    }

    [Fact]
    public void Envspectrum_ReportsAOneSidedSpectrumOfTheEnvelope()
    {
        string got = Run("n = 1024; x = sin(2*pi*0.2*(0:n-1)').*(1 + 0.5*sin(2*pi*0.01*(0:n-1)')); "
            + "[sp, f, env] = envspectrum(x, 1000); "
            + "fprintf('%d %d %d', numel(sp), numel(f), numel(env))");
        Assert.Equal("513 513 1024", got);
    }

    [Fact]
    public void Envspectrum_RefusesABandAboveNyquist()
    {
        string got = RunError("envspectrum(sin(0.1*(0:1023)'), 1000, 'Band', [100 900]);");
        Assert.Contains("below the Nyquist frequency", got);
    }

    [Fact]
    public void Tsa_AveragesAwayWhatIsNotLockedToTheShaft()
    {
        string got = Run("fs = 1000; n = 4000; t = (0:n-1)'/fs; f0 = 5; "
            + "x = sin(2*pi*f0*t) + 0.5*sin(2*pi*7.3*t); "
            + "ta = tsa(x, fs, (0:1/f0:t(end))'); "
            + "fprintf('%d', max(abs(ta - sin(2*pi*(0:numel(ta)-1)'/numel(ta)))) < 0.2)");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Tsa_RefusesFewerPulsesThanRotations()
    {
        string got = RunError("tsa(sin(0.1*(0:999)'), 1000, [0 0.5]', 'NumRotations', 4);");
        Assert.Contains("rotations' worth of pulse times", got);
    }

    [Fact]
    public void Tachorpm_RecoversAConstantSpeedFromItsOwnPulses()
    {
        // Both fits track a constant speed to within a revolution a minute out of six hundred.
        // The straight-line one agrees with MATLAB to the last bit; the least-squares spline lands
        // a few parts in a hundred thousand away from MATLAB's, which ADR 0141 records.
        string got = Run("fs = 2000; n = 4000; t = (0:n-1)'/fs; "
            + "p = 2*pi*10*t; tach = double(mod(p, 2*pi) < 0.6); "
            + "r = tachorpm(tach, fs, 'FitType', 'linear'); "
            + "s = tachorpm(tach, fs); "
            + "fprintf('%d %d', max(abs(r(200:end-200) - 600)) < 2, "
            + "max(abs(s(200:end-200) - 600)) < 2)");
        Assert.Equal("1 1", got);
    }

    [Fact]
    public void Rpmordermap_KeepsOrdersUpToWhatTheFastestSpeedAllows()
    {
        string got = Run("fs = 2000; n = 6000; t = (0:n-1)'/fs; rpm = 1200 + 900*t; "
            + "p = 2*pi*cumsum(rpm/60)/fs; x = sin(p) + 0.5*sin(2*p); "
            + "[~, o] = rpmordermap(x, fs, rpm); "
            + "fprintf('%d', o(end-1) <= fs/(2*max(rpm/60)))");
        Assert.Equal("1", got);
    }

    [Fact]
    public void Ordertrack_PicksTheNearestOrderInTheMap()
    {
        string got = Run("fs = 2000; n = 6000; t = (0:n-1)'/fs; rpm = 1200 + 900*t; "
            + "p = 2*pi*cumsum(rpm/60)/fs; x = sin(p) + 0.5*sin(2*p); "
            + "[m, o, r, tt] = rpmordermap(x, fs, rpm); "
            + "mag = ordertrack(m, o, r, tt, [1 2]); "
            + "fprintf('%d %d', size(mag, 1), mean(mag(1,:)) > mean(mag(2,:)))");
        Assert.Equal("2 1", got);
    }

    [Fact]
    public void Ordertrack_RefusesTheVoldKalmanOptions()
    {
        string got = RunError("fs = 1000; t = (0:999)'/fs; rpm = 600 + 300*t; "
            + "ordertrack(sin(2*pi*10*t), fs, rpm, 1, 'Bandwidth', 20);");
        Assert.Contains("no Vold-Kalman filter", got);
    }

    [Fact]
    public void Orderwaveform_ExtractsTheOrderItWasAskedFor()
    {
        string got = Run("fs = 1000; n = 1500; t = (0:n-1)'/fs; rpm = 600 + 400*t; "
            + "p = 2*pi*cumsum(rpm/60)/fs; x = sin(p) + 0.6*sin(2*p); "
            + "w = orderwaveform(x, fs, rpm, 1); "
            + "fprintf('%d %d', numel(w), max(abs(w(200:end-200) - sin(p(200:end-200)))) < 0.1)");
        Assert.Equal("1500 1", got);
    }

    [Fact]
    public void Orderspectrum_PeaksAtTheOrdersThatAreThere()
    {
        string got = Run("fs = 2000; n = 6000; t = (0:n-1)'/fs; rpm = 1200 + 900*t; "
            + "p = 2*pi*cumsum(rpm/60)/fs; x = sin(p) + 0.5*sin(2*p); "
            + "[sp, o] = orderspectrum(x, fs, rpm); [~, i1] = max(sp); "
            + "fprintf('%d', abs(o(i1) - 1) < 0.2)");
        Assert.Equal("1", got);
    }

    // --- The two-signal estimates M136 left without their MIMO forms -------------------------------

    [Fact]
    public void Tfestimate_MimoGivesOnePagePerInput()
    {
        string got = Run(Two
            + "h = tfestimate([x y], [y x], hamming(128), 64, 256, 'mimo'); fprintf('%d %d %d', size(h))");
        Assert.Equal("129 2 2", got);
    }

    [Fact]
    public void Mscohere_MimoGivesOneColumnPerOutputAndStaysInTheUnitInterval()
    {
        string got = Run(Two
            + "c = mscohere([x y], [y x], hamming(128), 64, 256, 'mimo'); "
            + "fprintf('%d %d %d', size(c, 1), size(c, 2), all(all(c >= -1e-9 & c <= 1 + 1e-9)))");
        Assert.Equal("129 2 1", got);
    }

    [Fact]
    public void Tfestimate_RefusesAnUnknownEstimator()
    {
        string got = RunError(Two + "tfestimate(x, y, hamming(128), 64, 256, 'Estimator', 'h3');");
        Assert.Contains("'H1' or 'H2'", got);
    }

    // --- The library's own rules --------------------------------------------------------------------

    [Fact]
    public void ShortTimeTransforms_TheFramesAreCentredOnTheirOwnTimes()
    {
        var x = new System.Numerics.Complex[100];
        (System.Numerics.Complex[][] frames, double[] times) =
            ShortTimeTransforms.Columns(x, 10, 5, 100);
        Assert.Equal(19, frames.Length);
        Assert.Equal(0.05, times[0], 12);
        Assert.Equal(0.10, times[1], 12);
    }

    [Fact]
    public void ShortTimeTransforms_ARectangularWindowAtHalfOverlapAddsToAConstant()
    {
        double[] window = new double[16];
        Array.Fill(window, 1.0);
        (bool constant, _, _) = ShortTimeTransforms.ConstantOverlapAdd(
            window, 8, OverlapAddMethod.Ola);
        Assert.True(constant);
    }

    [Fact]
    public void RainflowCounting_TheTurningPointsAlwaysKeepBothEnds()
    {
        double[] x = [0, 1, 0, 2, 0, 1, 0];
        int[] turning = RainflowCounting.Extrema(x);
        Assert.Equal(0, turning[0]);
        Assert.Equal(x.Length - 1, turning[^1]);
    }

    [Fact]
    public void LinearPrediction_TheTwoStepsUndoOneAnother()
    {
        System.Numerics.Complex[] a = [1, -0.6, 0.2];
        (System.Numerics.Complex[] down, double error, System.Numerics.Complex k) =
            LinearPrediction.StepDown(a, 1.0);
        (System.Numerics.Complex[] up, double back) = LinearPrediction.StepUp(down, k, error);
        Assert.Equal(a.Length, up.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].Real, up[i].Real, 12);
        }

        Assert.Equal(1.0, back, 12);
    }

    [Fact]
    public void ModalAnalysis_APoleAndItsFrequencyAndDampingAgree()
    {
        System.Numerics.Complex pole = ModalAnalysis.FromNaturalFrequency(120.0, 0.02);
        (double natural, double damping) = ModalAnalysis.ToNaturalFrequency(pole);
        Assert.Equal(120.0, natural, 9);
        Assert.Equal(0.02, damping, 12);
    }
}
