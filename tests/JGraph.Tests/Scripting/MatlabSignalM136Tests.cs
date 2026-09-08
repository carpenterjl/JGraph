using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Signal;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M136: the spectral estimates and the measurements taken off them, checked through the MATLAB
/// dialect for what a parity fixture cannot say — which shapes come back, which options are
/// refused, which names were removed from MATLAB rather than kept, and the properties an estimate
/// is supposed to have rather than the numbers it happens to answer.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabSignalM136Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabSignalM136Tests() => JG.Reset();

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
        "n = 256; t = (0:n-1)'; x = cos(2*pi*0.1*t) + 0.4*sin(2*pi*0.3*t) + 0.02*mod(t,7)/7; ";

    // --- Shapes -----------------------------------------------------------------------------------

    [Fact]
    public void Periodogram_KeepsHalfTheTransformForARealSignal()
    {
        Assert.Equal("129 1", Run(Signal + "fprintf('%d %d', size(periodogram(x, [], 256)))"));
    }

    [Fact]
    public void Periodogram_KeepsTheWholeTransformForAComplexSignal()
    {
        Assert.Equal(
            "256 1",
            Run(Signal + "z = x + 1i*circshift(x, 3); fprintf('%d %d', size(periodogram(z, [], 256)))"));
    }

    [Fact]
    public void Periodogram_RefusesAOneSidedEstimateOfAComplexSignal()
    {
        string message = RunError(Signal + "z = x + 1i*x; periodogram(z, [], 256, 'onesided');");
        Assert.Contains("one-sided", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Periodogram_GivesOneColumnPerChannel()
    {
        Assert.Equal("129 2", Run(Signal + "fprintf('%d %d', size(periodogram([x 2*x], [], 256)))"));
    }

    [Fact]
    public void Welch_DefaultSegmentIsTwoNinthsOfTheSignal()
    {
        // fix(256/4.5) = 56, so the default transform is the next power of two above it, and at
        // least 256 — which is what makes pwelch's default grid the same as periodogram's here.
        Assert.Equal("129 1", Run(Signal + "fprintf('%d %d', size(pwelch(x)))"));
    }

    [Fact]
    public void Welch_RefusesAnOverlapAsLongAsTheSegment()
    {
        string message = RunError(Signal + "pwelch(x, 64, 64);");
        Assert.Contains("overlap", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Coherence_IsBetweenZeroAndOne()
    {
        string answer = Run(
            Signal + "c = mscohere(x, circshift(x, 5), 64, 32, 128); "
            + "fprintf('%d %d', all(c >= -1e-12), all(c <= 1 + 1e-12))");
        Assert.Equal("1 1", answer);
    }

    [Fact]
    public void TransferEstimate_OfASignalWithItselfIsOne()
    {
        string answer = Run(
            Signal + "h = tfestimate(x, x, 64, 32, 128); fprintf('%d', all(abs(h - 1) < 1e-9))");
        Assert.Equal("1", answer);
    }

    // --- The names MATLAB removed ------------------------------------------------------------------

    [Fact]
    public void Psd_WasRemovedFromMatlabAndIsRefusedHere()
    {
        string message = RunError(Signal + "psd(x);");
        Assert.Contains("periodogram", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Spectrum_WasRemovedFromMatlabAndIsRefusedHere()
    {
        string message = RunError(Signal + "spectrum(x);");
        Assert.Contains("removed", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyCrossSpectrum_ReadsItsArgumentsInTheOldOrder()
    {
        // csd's third argument is a transform length, not a window: 64 points give 33 estimates.
        Assert.Equal("33 1", Run(Signal + "fprintf('%d %d', size(csd(x, x, 64)))"));
    }

    [Fact]
    public void LegacySpectrogram_GivesOneColumnPerFrame()
    {
        Assert.Equal(
            "65 13",
            Run(Signal + "fprintf('%d %d', size(specgram(x, 128, 1000, hamming(64), 48)))"));
    }

    // --- The parametric estimates ------------------------------------------------------------------

    [Fact]
    public void Burg_AndYuleWalkerPeakAtDifferentTonesOfTheSameSignal()
    {
        // Not a defect in either: the Yule-Walker fit is over a biased autocorrelation, which
        // smears a short record enough to move which of two tones comes out taller.
        string answer = Run(
            Signal + "[a, f] = pburg(x, 12, 512, 1); [b, ~] = pyulear(x, 12, 512, 1); "
            + "[~, ia] = max(a); [~, ib] = max(b); fprintf('%d %d', ia, ib)");
        Assert.Equal("155 52", answer);
    }

    [Fact]
    public void CorrelationMatrix_HasTheShapeItsMethodAsksFor()
    {
        Assert.Equal("260 5", Run(Signal + "fprintf('%d %d', size(corrmtx(x, 4)))"));
        Assert.Equal("252 5", Run(Signal + "fprintf('%d %d', size(corrmtx(x, 4, 'cov')))"));
        Assert.Equal("504 5", Run(Signal + "fprintf('%d %d', size(corrmtx(x, 4, 'mod')))"));
    }

    [Fact]
    public void CorrelationMatrix_RefusesAnOrderAtTheSignalLength()
    {
        string message = RunError("corrmtx(1:5, 5);");
        Assert.Contains("order", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RootMusic_RefusesAnOddDimensionForARealSignal()
    {
        string message = RunError(Signal + "rootmusic(x, 3);");
        Assert.Contains("even", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Music_FindsTheTwoTonesTheSignalHas()
    {
        string answer = Run(
            Signal + "w = sort(rootmusic(x, 4))/(2*pi); "
            + "fprintf('%d %d', abs(w(3) - 0.1) < 1e-4, abs(w(4) - 0.3) < 1e-4)");
        Assert.Equal("1 1", answer);
    }

    // --- Multitaper and Lomb-Scargle ---------------------------------------------------------------

    [Fact]
    public void Multitaper_TakesSevenSlepianTapersByDefault()
    {
        // Four is the default time-bandwidth product, dpss gives eight sequences, and the last is
        // dropped: seven tapers, which is what the confidence interval's degrees of freedom say.
        string answer = Run(
            Signal + "[p, f, c] = pmtm(x, 4, 256, 1, 'ConfidenceLevel', 0.95); "
            + "fprintf('%.4f', c(64, 2)/p(64))");
        Assert.Equal("2.4872", answer);
    }

    [Fact]
    public void Multitaper_RefusesATaperFamilyItDoesNotKnow()
    {
        string message = RunError(Signal + "pmtm(x, 'Tapers', 'hann');");
        Assert.Contains("sine", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Lomb_ReportsInRadiansWithoutASampleRateAndInHertzWithOne()
    {
        Assert.Equal("1", Run(Signal + "[~, f] = plomb(x); fprintf('%d', f(end) <= pi)"));
        Assert.Equal("1", Run(Signal + "[~, f] = plomb(x, 100); fprintf('%d', f(end) <= 50)"));
    }

    [Fact]
    public void Lomb_RefusesAFrequencyListAndAnOversamplingFactorTogether()
    {
        string message = RunError(Signal + "plomb(x, 100, [1 2 3], 4);");
        Assert.Contains("oversampling", message, StringComparison.OrdinalIgnoreCase);
    }

    // --- The measurements --------------------------------------------------------------------------

    [Fact]
    public void NoiseBandwidth_OfARectangularWindowIsOne()
    {
        Assert.Equal("1", Run("fprintf('%d', abs(enbw(ones(64,1)) - 1) < 1e-12)"));
    }

    [Fact]
    public void BandPower_OverTheWholeBandIsTheSignalsMeanSquare()
    {
        string answer = Run(
            Signal + "fprintf('%d', abs(bandpower(x, 1, [0 0.5]) - bandpower(x)) < 5e-3)");
        Assert.Equal("1", answer);
    }

    [Fact]
    public void MeanFrequency_LiesBetweenTheTwoTones()
    {
        string answer = Run(Signal + "f = meanfreq(x, 1); fprintf('%d %d', f > 0.1, f < 0.3)");
        Assert.Equal("1 1", answer);
    }

    [Fact]
    public void SignalToNoise_TakesTwoSignalsAsSignalAndNoise()
    {
        Assert.Equal("20", Run("fprintf('%.0f', snr(10*ones(1,64), ones(1,64)))"));
    }

    [Fact]
    public void HarmonicMeasures_RefuseASpectrumThatDoesNotStartAtZero()
    {
        string message = RunError("thd([1 2 3], [1 2 3], 'psd');");
        Assert.Contains("one-sided", message, StringComparison.OrdinalIgnoreCase);
    }

    // --- findpeaks and the level measurements -------------------------------------------------------

    [Fact]
    public void FindPeaks_KeepsTheShapeItsSignalArrivedIn()
    {
        Assert.Equal("3 1", Run("fprintf('%d %d', size(findpeaks([0 1 0 2 0 3 0]')))"));
        Assert.Equal("1 3", Run("fprintf('%d %d', size(findpeaks([0 1 0 2 0 3 0])))"));
    }

    [Fact]
    public void FindPeaks_ProminenceIsTheHeightAboveTheHigherOfTheTwoSaddles()
    {
        string answer = Run(
            "[~, ~, ~, p] = findpeaks([0 3 1 4 0]); fprintf('%g %g', p(1), p(2))");
        Assert.Equal("2 4", answer);
    }

    [Fact]
    public void FindPeaks_RefusesASignalShorterThanThreeSamples()
    {
        string message = RunError("findpeaks([1 2]);");
        Assert.Contains("three", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindPeaks_SortsWhenAskedAndCapsAfterSorting()
    {
        Assert.Equal("3 2", Run("fprintf('%g %g', findpeaks([0 1 0 2 0 3 0], 'SortStr', 'descend', 'NPeaks', 2))"));
    }

    [Fact]
    public void ZeroCrossRate_CountsHalfAStepPerSignChange()
    {
        Assert.Equal("0.875", Run("fprintf('%g', zerocrossrate([1 -1 1 -1]))"));
    }

    [Fact]
    public void PeakToRms_OfAConstantIsOne()
    {
        Assert.Equal("1", Run("fprintf('%g', peak2rms([3 3 3 3]))"));
    }

    // --- The bilevel family -------------------------------------------------------------------------

    [Fact]
    public void StateLevels_FindsTheTwoLevelsOfASquareWave()
    {
        string answer = Run(
            "x = [zeros(1,50) ones(1,50) zeros(1,50) ones(1,50)]; l = statelevels(x); "
            + "fprintf('%d %d', abs(l(1)) < 0.02, abs(l(2) - 1) < 0.02)");
        Assert.Equal("1 1", answer);
    }

    [Fact]
    public void PulseWidth_AndPulseSeparationAddUpToThePeriod()
    {
        string answer = Run(
            "x = repmat([zeros(1,20) ones(1,20)], 1, 5); "
            + "fprintf('%d', all(abs(pulsewidth(x) + pulsesep(x) - pulseperiod(x)) < 1e-9))");
        Assert.Equal("1", answer);
    }

    [Fact]
    public void DutyCycle_TakesAWidthAndARepetitionFrequencyToo()
    {
        Assert.Equal("0.2", Run("fprintf('%g', dutycycle(0.002, 100))"));
    }

    [Fact]
    public void DutyCycle_RefusesAProductAboveOne()
    {
        string message = RunError("dutycycle(0.02, 100);");
        Assert.Contains("at most one", message, StringComparison.OrdinalIgnoreCase);
    }

    // --- Alignment and distance ---------------------------------------------------------------------

    [Fact]
    public void FindDelay_IsNegatedWhenTheSignalsAreSwapped()
    {
        string answer = Run(
            "a = sin(2*pi*(0:59)/13); b = [zeros(1,7) a(1:end-7)]; "
            + "fprintf('%g %g', finddelay(a, b), finddelay(b, a))");
        Assert.Equal("7 -7", answer);
    }

    [Fact]
    public void AlignSignals_PadsTheEarlierSignalRatherThanCuttingTheLater()
    {
        Assert.Equal("67 60", Run(
            "a = sin(2*pi*(0:59)/13); b = [zeros(1,7) a(1:end-7)]; "
            + "[p, q] = alignsignals(a, b); fprintf('%d %d', numel(p), numel(q))"));
    }

    [Fact]
    public void DynamicTimeWarping_OfASignalWithItselfIsZeroAlongTheDiagonal()
    {
        Assert.Equal("0 9", Run(
            "x = [1 4 2 8 3 9 1 5 2]; [d, ix] = dtw(x, x); fprintf('%g %d', d, numel(ix))"));
    }

    [Fact]
    public void EditDistance_CountsTheSamplesFurtherApartThanTheTolerance()
    {
        Assert.Equal("0", Run("fprintf('%g', edr([1 2 3], [1.1 2.1 3.1], 0.5))"));
        Assert.Equal("3", Run("fprintf('%g', edr([1 2 3], [9 9 9], 0.5))"));
    }

    [Fact]
    public void FindSignal_ReportsOneSegmentWhenNoCapIsNamed()
    {
        Assert.Equal("1", Run(
            "d = [zeros(1,10) 4 5 6 zeros(1,10)]; fprintf('%d', numel(findsignal(d, [4 5 6])))"));
    }

    [Fact]
    public void CircularConvolution_WrapsWhereTheLinearOneWouldGrow()
    {
        Assert.Equal("8 7 6 9", Run("fprintf('%g %g %g %g', cconv([1 2 3 4], [1 1 1], 4))"));
    }

    [Fact]
    public void ConvolutionMatrix_MultipliesOutToAConvolution()
    {
        string answer = Run(
            "h = [1 2 3]; A = convmtx(h', 4); x = [1 -1 2 0]'; "
            + "fprintf('%d', max(abs(A*x - conv(h, x')')) < 1e-12)");
        Assert.Equal("1", answer);
    }

    // --- Change detection ---------------------------------------------------------------------------

    [Fact]
    public void Cusum_ReportsOnlyTheFirstViolationUnlessAskedForAll()
    {
        string answer = Run(
            "x = [zeros(1,60) 5*ones(1,60)]; "
            + "fprintf('%d %d', numel(cusum(x)), numel(cusum(x, 5, 1, 0, 1, 'all')))");
        string[] parts = answer.Split((char[])[' '], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("1", parts[0]);
        Assert.True(int.Parse(parts[1]) > 1, answer);
    }

    [Fact]
    public void FindChangePoints_FindsTheStepInAStepSignal()
    {
        Assert.Equal("61", Run("x = [zeros(1,60) 5*ones(1,60)]; fprintf('%d', findchangepts(x))"));
    }

    [Fact]
    public void FindChangePoints_RefusesAChangeCountAndAThresholdTogether()
    {
        string message = RunError(
            "findchangepts(1:20, 'MaxNumChanges', 2, 'MinThreshold', 1);");
        Assert.Contains("not both", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindChangePoints_NeedsTwoSamplesPerSegmentForTheVarianceStatistics()
    {
        string message = RunError("findchangepts(1:20, 'Statistic', 'std', 'MinDistance', 1);");
        Assert.Contains("minimum distance", message, StringComparison.OrdinalIgnoreCase);
    }

    // --- The library's own rules --------------------------------------------------------------------

    [Fact]
    public void FrequencyGrid_PinsNyquistExactly()
    {
        double[] grid = SpectralEstimation.FrequencyGrid(8, 1000);
        Assert.Equal(500.0, grid[4]);
        Assert.Equal(875.0, grid[7]);
    }

    [Fact]
    public void FrequencyGrid_StepsEitherSideOfNyquistForAnOddLength()
    {
        double[] grid = SpectralEstimation.FrequencyGrid(7, 1400);
        Assert.Equal(600.0, grid[3], 10);
        Assert.Equal(800.0, grid[4], 10);
    }

    [Fact]
    public void OneSidedLength_KeepsNyquistOnlyWhenThereIsOne()
    {
        Assert.Equal(129, SpectralEstimation.OneSidedLength(256));
        Assert.Equal(128, SpectralEstimation.OneSidedLength(255));
    }
}
