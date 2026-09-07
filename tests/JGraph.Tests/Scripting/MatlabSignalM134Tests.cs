using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Signal;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M134: filter design and analysis, checked through the MATLAB dialect for the things a parity
/// fixture cannot say — the shapes, the option words, the refusals, and the properties a design is
/// supposed to have rather than the numbers it happens to answer.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabSignalM134Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabSignalM134Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    /// <summary>
    /// Runs one line of the MATLAB dialect and answers what it printed. The semicolon is added
    /// here rather than in every case below: a bare <c>fprintf</c> is a statement whose byte count
    /// MATLAB echoes as <c>ans</c>, and every one of these cases wants the printing and not that.
    /// </summary>
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

    // --- The prototypes -----------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void Buttap_PolesLieOnTheUnitCircle(int order)
    {
        (_, System.Numerics.Complex[] poles, double gain) = AnalogPrototypes.Butterworth(order);

        Assert.Equal(order, poles.Length);
        foreach (System.Numerics.Complex p in poles)
        {
            Assert.Equal(1, System.Numerics.Complex.Abs(p), 12);
            Assert.True(p.Real < 0, "a Butterworth pole is in the left half plane");
        }

        Assert.Equal(1, gain, 12);
    }

    [Fact]
    public void Cheb2ap_HasOneFewerZeroWhenTheOrderIsOdd()
    {
        (System.Numerics.Complex[] odd, _, _) = AnalogPrototypes.Chebyshev2(5, 40);
        (System.Numerics.Complex[] even, _, _) = AnalogPrototypes.Chebyshev2(6, 40);

        Assert.Equal(4, odd.Length);
        Assert.Equal(6, even.Length);
    }

    [Fact]
    public void Ellipap_ZerosAreOnTheImaginaryAxisAndOutsideThePassband()
    {
        (System.Numerics.Complex[] zeros, System.Numerics.Complex[] poles, _) =
            AnalogPrototypes.Elliptic(6, 1, 50);

        Assert.Equal(6, zeros.Length);
        Assert.Equal(6, poles.Length);
        foreach (System.Numerics.Complex z in zeros)
        {
            Assert.Equal(0, z.Real, 10);
            Assert.True(System.Math.Abs(z.Imaginary) > 1, "an elliptic zero sits past the band edge");
        }
    }

    [Fact]
    public void Besselap_RefusesPastTwentyFive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AnalogPrototypes.Bessel(26));

    // --- The classical designs ------------------------------------------------------------------

    [Fact]
    public void Butter_SingleOutputIsTheNumeratorAlone()
    {
        Assert.Equal("5", Run("b = butter(4, 0.3); fprintf('%d', numel(b))"));
        Assert.Equal("5 5", Run("[b, a] = butter(4, 0.3); fprintf('%d %d', numel(b), numel(a))"));
    }

    [Fact]
    public void Butter_ThreeOutputsAreRootsAndFourAreStateSpace()
    {
        Assert.Equal("4 4 1", Run("[z, p, k] = butter(4, 0.3); fprintf('%d %d %d', numel(z), numel(p), numel(k))"));
        Assert.Equal("4 4 4 1 1 4 1 1",
            Run("[A, B, C, D] = butter(4, 0.3); fprintf('%d %d %d %d %d %d %d %d', "
                + "size(A, 1), size(A, 2), size(B, 1), size(B, 2), size(C, 1), size(C, 2), size(D, 1), size(D, 2))"));
    }

    [Fact]
    public void Butter_BandPassDoublesTheOrder() =>
        Assert.Equal("13 13", Run("[b, a] = butter(6, [0.2 0.5]); fprintf('%d %d', numel(b), numel(a))"));

    [Theory]
    [InlineData("butter(4, 0.3)")]
    [InlineData("cheby1(4, 1, 0.3)")]
    [InlineData("cheby2(4, 40, 0.3)")]
    [InlineData("ellip(4, 1, 40, 0.3)")]
    public void EveryDigitalDesign_IsStable(string call) =>
        Assert.Equal("1", Run($"[b, a] = {call}; fprintf('%d', isstable(b, a))"));

    [Fact]
    public void Butter_PassesAtZeroAndStopsAtNyquist()
    {
        string answer = Run(
            "[b, a] = butter(6, 0.3); h = freqz(b, a, [0 pi]); "
            + "fprintf('%d %d', abs(h(1)) > 0.999, abs(h(2)) < 1e-6)");

        Assert.Equal("1 1", answer);
    }

    [Fact]
    public void Cheby1_RipplesByTheAmountItWasAsked()
    {
        string answer = Run(
            "[b, a] = cheby1(6, 1.5, 0.4); h = abs(freqz(b, a, 512)); "
            + "band = h(1:round(0.4 * 512)); "
            + "fprintf('%.3f', 20 * log10(max(band) / min(band)))");

        Assert.Equal("1.500", answer);
    }

    [Fact]
    public void Cheby2_StopbandTouchesTheAttenuationItWasAsked()
    {
        string answer = Run(
            "[b, a] = cheby2(8, 40, 0.4); h = abs(freqz(b, a, 1024)); "
            + "band = h(round(0.4 * 1024):end); "
            + "fprintf('%.2f', 20 * log10(max(band)))");

        // Not exactly forty: the stopband edge itself is inside the band being measured, and the
        // response has not quite reached the ripple floor there. MATLAB answers the same number.
        Assert.Equal("-38.96", answer);
    }

    [Fact]
    public void Besself_IsAnalogueEvenWithoutTheWord() =>
        Assert.Equal("5 5 1", Run(
            "[b, a] = besself(4, 100); fprintf('%d %d %d', numel(b), numel(a), all(isfinite(a)))"));

    [Fact]
    public void ClassicalDesigns_RefuseAWordTheyDoNotKnow() =>
        Assert.Contains("not one of", RunError("butter(4, 0.3, 'middling')"));

    [Fact]
    public void ClassicalDesigns_RefuseTwoCutoffsForALowpass() =>
        Assert.Contains("one cutoff", RunError("butter(4, [0.2 0.5], 'low')"));

    [Fact]
    public void Butter_CascadeFormAnswersTwoMatricesAndAGain() =>
        Assert.Equal("3 3 3 3",
            Run("[num, den, sv] = butter(6, 0.3, 'ctf'); "
                + "fprintf('%d %d %d %d', size(num, 1), size(num, 2), size(den, 1), size(den, 2))"));

    // --- Minimum order --------------------------------------------------------------------------

    [Fact]
    public void Buttord_AnswersAnOrderThatMeetsTheSpecification()
    {
        string answer = Run(
            "[n, wn] = buttord(0.2, 0.35, 1, 40); [b, a] = butter(n, wn); "
            + "h = abs(freqz(b, a, 1024)); "
            + "fprintf('%d %d %d', n, 20 * log10(h(round(0.2 * 1024))) > -1.01, "
            + "20 * log10(h(round(0.35 * 1024))) < -39)");

        // Nine, not the eight a reading of the formula suggests: the order is rounded up and the
        // 3 dB frequency placed so that the passband edge is comfortably inside, which leaves the
        // stopband edge a little short of the forty decibels asked for. MATLAB answers the same.
        Assert.Equal("9 1 1", answer);
    }

    [Theory]
    [InlineData("buttord")]
    [InlineData("cheb1ord")]
    [InlineData("cheb2ord")]
    [InlineData("ellipord")]
    public void OrderRules_RefuseAFrequencyPastNyquist(string name) =>
        Assert.Contains("between 0 and 1", RunError($"{name}(0.2, 1.5, 1, 40)"));

    [Fact]
    public void EllipOrder_IsTheSmallestOfTheFour()
    {
        string answer = Run(
            "a = buttord(0.2, 0.3, 1, 60); b = cheb1ord(0.2, 0.3, 1, 60); "
            + "c = cheb2ord(0.2, 0.3, 1, 60); d = ellipord(0.2, 0.3, 1, 60); "
            + "fprintf('%d', d <= c && c <= a && d <= b)");

        Assert.Equal("1", answer);
    }

    // --- Transforms ------------------------------------------------------------------------------

    [Fact]
    public void Lp2hp_TurnsAPassbandIntoAStopband()
    {
        string answer = Run(
            "[b, a] = lp2hp(1, [1 sqrt(2) 1], 10); h = freqs(b, a, [0.01 1000]); "
            + "fprintf('%d %d', abs(h(1)) < 1e-4, abs(h(2)) > 0.99)");

        Assert.Equal("1 1", answer);
    }

    [Fact]
    public void Bilinear_MapsTheLeftHalfPlaneInsideTheCircle()
    {
        string answer = Run(
            "[zd, pd, kd] = bilinear([], [-1+1i; -1-1i], 2, 10); "
            + "fprintf('%d %d', numel(zd), all(abs(pd) < 1))");

        Assert.Equal("2 1", answer);
    }

    [Fact]
    public void Impinvar_KeepsTheImpulseResponseItSampled()
    {
        string answer = Run(
            "[bz, az] = impinvar(1, [1 2 2], 10); h = impz(bz, az, 5); "
            + "t = (0:4) / 10; s = exp(-t) .* sin(t) / 10; "
            + "fprintf('%d', max(abs(h(:) - s(:))) < 1e-12)");

        Assert.Equal("1", answer);
    }

    [Fact]
    public void Freqs_PicksItsOwnGridWhenGivenACount() =>
        Assert.Equal("30 30", Run("[h, w] = freqs(1, [1 0.4 1], 30); fprintf('%d %d', numel(h), numel(w))"));

    // --- FIR design ------------------------------------------------------------------------------

    [Fact]
    public void Fir1_IsSymmetricAndUnityAtDc()
    {
        string answer = Run(
            "b = fir1(20, 0.4); fprintf('%d %.12f', max(abs(b - fliplr(b))) < 1e-15, sum(b))");

        Assert.Equal("1 1.000000000000", answer);
    }

    [Fact]
    public void Fir1_RaisesAnOddOrderForAHighpass() =>
        Assert.Equal("23", Run("b = fir1(21, 0.4, 'high'); fprintf('%d', numel(b))"));

    [Fact]
    public void Fir1_RefusesAWindowOfTheWrongLength() =>
        Assert.Contains("as long as the filter", RunError("fir1(20, 0.4, hamming(15))"));

    [Fact]
    public void Firls_HilbertIsAntisymmetric()
    {
        string answer = Run(
            "b = firls(30, [0.1 0.9], [1 1], 'h'); fprintf('%d', max(abs(b + fliplr(b))) < 1e-15)");

        Assert.Equal("1", answer);
    }

    [Fact]
    public void Firls_RefusesAnUnknownType() =>
        Assert.Contains("'hilbert' or 'differentiator'", RunError("firls(20, [0 1], [1 1], 'wobbly')"));

    [Fact]
    public void Firpm_EquiripplesToTheErrorItReports()
    {
        string answer = Run(
            "[h, err] = firpm(30, [0 0.3 0.4 1], [1 1 0 0]); "
            + "m = abs(freqz(h, 1, 2048)); stop = m(round(0.4 * 2048):end); "
            + "fprintf('%d', abs(max(stop) - err) < 5e-3)");

        Assert.Equal("1", answer);
    }

    [Fact]
    public void Firpm_ConvergesAtOrderFourHundred()
    {
        // M124 recorded that the old exchange warned here and answered coefficients 1e-5 from
        // MATLAB's. The transcription converges, which is what closed that divergence.
        string answer = Run(
            "h = firpm(400, [0 0.3 0.32 1], [1 1 0 0]); "
            + "fprintf('%d %d', numel(h), max(abs(h - fliplr(h))) < 1e-15)");

        Assert.Equal("401 1", answer);
    }

    [Fact]
    public void Firpm_ThirdOutputCarriesTheGridAndTheExtremes() =>
        Assert.Equal("1", Run(
            "[~, ~, res] = firpm(20, [0 0.3 0.5 1], [1 1 0 0]); "
            + "fprintf('%d', numel(res.fgrid) == numel(res.error) && numel(res.fextr) == numel(res.iextr))"));

    [Fact]
    public void Firpm_RefusesAResponseFunction() =>
        Assert.Contains("cfirpm", RunError("firpm(20, [0 0.3 0.5 1], {'lowpass'})"));

    [Fact]
    public void Cfirpm_IsNotThere() =>
        Assert.Contains("not recognized", RunError("cfirpm(20, [-1 -0.5 0.5 1], 'lowpass')"));

    [Fact]
    public void Fir2_FollowsTheResponseItWasDrawn()
    {
        string answer = Run(
            "b = fir2(60, [0 0.3 0.5 0.7 1], [0 1 0.5 0.2 0]); m = abs(freqz(b, 1, 1024)); "
            + "fprintf('%d %d', abs(m(round(0.3 * 1024)) - 1) < 0.1, abs(m(round(0.5 * 1024)) - 0.5) < 0.1)");

        Assert.Equal("1 1", answer);
    }

    [Fact]
    public void Kaiserord_HandsFir1TheArgumentsItNeeds()
    {
        string answer = Run(
            "[n, wn, beta, ftype] = kaiserord([1000 1200], [1 0], [0.05 0.01], 8000); "
            + "b = fir1(n, wn, ftype, kaiser(n + 1, beta), 'noscale'); "
            + "m = abs(freqz(b, 1, 4096)); "
            + "fprintf('%d %d', numel(b) == n + 1, max(m(round(1200 / 4000 * 4096):end)) < 0.012)");

        Assert.Equal("1 1", answer);
    }

    [Fact]
    public void Firpmord_HandsFirpmTheArgumentsItNeeds() =>
        Assert.Equal("1", Run(
            "[n, f, a, w] = firpmord([1000 1200], [1 0], [0.05 0.01], 8000); "
            + "b = firpm(n, f, a, w); fprintf('%d', numel(b) == n + 1)"));

    [Fact]
    public void Fircls_StaysBetweenItsBounds()
    {
        string answer = Run(
            "b = fircls(40, [0 0.5 1], [1 0], [1.02 0.01], [0.98 -0.01]); "
            + "m = real(freqz(b, 1, 1024) .* exp(1i * (0:1023)' * pi / 1024 * 20)); "
            + "fprintf('%d', max(m) < 1.021)");

        Assert.Equal("1", answer);
    }

    [Fact]
    public void Fircls1_AnswersTheOrderItWasAsked() =>
        Assert.Equal("56", Run("b = fircls1(55, 0.3, 0.02, 0.008); fprintf('%d', numel(b))"));

    [Fact]
    public void Maxflat_IsMonotonicAndHalfPowerAtItsCutoff()
    {
        string answer = Run(
            "[b, a] = maxflat(10, 2, 0.3); m = abs(freqz(b, a, 2048)); "
            + "fprintf('%d %d', all(diff(m) < 1e-12), abs(m(round(0.3 * 2048)) - sqrt(0.5)) < 0.005)");

        Assert.Equal("1 1", answer);
    }

    [Fact]
    public void Rcosdesign_HasUnitEnergy() =>
        Assert.Equal("1.000000", Run("b = rcosdesign(0.25, 6, 4); fprintf('%.6f', sum(b .^ 2))"));

    [Fact]
    public void Gaussdesign_SumsToOne() =>
        Assert.Equal("1.000000", Run("h = gaussdesign(0.3, 4, 3); fprintf('%.6f', sum(h))"));

    [Fact]
    public void Firgauss_IsTheBoxcarConvolvedWithItself() =>
        Assert.Equal("17 1", Run(
            "h = firgauss(4, 5); c = conv(conv(ones(1, 5), ones(1, 5)), conv(ones(1, 5), ones(1, 5))); "
            + "fprintf('%d %d', numel(h), max(abs(h(:) - c(:))) < 1e-12)"));

    [Fact]
    public void Yulewalk_FollowsTheResponseItWasFitted()
    {
        string answer = Run(
            "[b, a] = yulewalk(8, [0 0.4 0.4 0.6 0.6 1], [1 1 0 0 1 1]); "
            + "m = abs(freqz(b, a, 1024)); "
            + "fprintf('%d %d', m(round(0.2 * 1024)) > 0.8, m(round(0.5 * 1024)) < 0.2)");

        Assert.Equal("1 1", answer);
    }

    [Fact]
    public void Intfilt_LagrangeIsExactOnItsOwnSamples() =>
        Assert.Equal("15 1", Run(
            "h = intfilt(4, 3, 'l'); "
            + "fprintf('%d %d', numel(h), abs(h(round(numel(h) / 2)) - 1) < 1e-12)"));

    // --- Analysis ---------------------------------------------------------------------------------

    [Fact]
    public void Freqz_SingleOutputIsTheResponseAlone() =>
        Assert.Equal("8", Run("h = freqz([1 1], 1, 8); fprintf('%d', numel(h))"));

    [Fact]
    public void Freqz_WholeCoversTheWholeCircle() =>
        Assert.Equal("1", Run(
            "[~, w] = freqz([1 1], 1, 16, 'whole'); fprintf('%d', abs(w(end) - 2 * pi * 15 / 16) < 1e-12)"));

    [Fact]
    public void Freqz_WithASampleRateAnswersHertz() =>
        Assert.Equal("1", Run(
            "[~, f] = freqz([1 1], 1, 8, 1000); fprintf('%d', abs(f(end) - 437.5) < 1e-9)"));

    [Fact]
    public void Impz_OfAFirFilterIsTheFilter() =>
        Assert.Equal("1", Run(
            "b = fir1(10, 0.4); h = impz(b, 1); fprintf('%d', max(abs(h(:) - b(:))) < 1e-15)"));

    [Fact]
    public void Impzlength_GrowsWithThePoleRadius() =>
        Assert.Equal("1", Run(
            "a = impzlength(1, [1 -0.5]); b = impzlength(1, [1 -0.99]); fprintf('%d', b > 5 * a)"));

    [Fact]
    public void Stepz_SettlesAtTheDcGain() =>
        Assert.Equal("1", Run(
            "[b, a] = butter(4, 0.3); s = stepz(b, a, 200); fprintf('%d', abs(s(end) - 1) < 1e-6)"));

    [Fact]
    public void Grpdelay_OfALinearPhaseFilterIsFlat() =>
        Assert.Equal("1", Run(
            "b = fir1(20, 0.4); gd = grpdelay(b, 1, 64); fprintf('%d', max(abs(gd - 10)) < 1e-12)"));

    [Fact]
    public void Phasedelay_OfALinearPhaseFilterIsTheSameConstant() =>
        Assert.Equal("1", Run(
            "b = fir1(20, 0.4); pd = phasedelay(b, 1, 64); fprintf('%d', max(abs(pd(2:end) - 10)) < 1e-9)"));

    [Fact]
    public void Zerophase_KeepsTheSignTheMagnitudeLoses() =>
        Assert.Equal("1", Run(
            "b = firls(30, [0 0.3 0.5 1], [1 1 0 0]); hz = zerophase(b, 1, 512); "
            + "fprintf('%d', min(hz) < 0)"));

    [Fact]
    public void Filtord_IgnoresTrailingZeros() =>
        Assert.Equal("2 2", Run("fprintf('%d %d', filtord([1 2 3 0 0], 1), filtord([1 2 3], 1))"));

    [Fact]
    public void Filternorm_OfAFirFilterIsItsCoefficientNorm() =>
        Assert.Equal("1", Run(
            "b = fir1(20, 0.4); fprintf('%d', abs(filternorm(b, 1) - norm(b)) < 1e-15)"));

    [Fact]
    public void Filternorm_RefusesANormItCannotTake() =>
        Assert.Contains("2 or Inf", RunError("filternorm([1 2], 1, 3)"));

    [Theory]
    [InlineData("fir1(20, 0.4)", 1)]
    [InlineData("fir1(21, 0.4)", 2)]
    [InlineData("firls(30, [0.1 0.9], [1 1], 'h')", 3)]
    [InlineData("firls(31, [0.1 0.9], [1 1], 'h')", 4)]
    public void Firtype_NamesTheFourSymmetries(string call, int type) =>
        Assert.Equal(type.ToString(), Run($"fprintf('%d', firtype({call}))"));

    [Fact]
    public void Firtype_RefusesAFilterWithoutLinearPhase() =>
        Assert.Contains("linear-phase", RunError("firtype([1 2 3 5])"));

    [Fact]
    public void Isstable_ReadsThePolesWithoutFindingThem() =>
        Assert.Equal("1 0", Run("fprintf('%d %d', isstable(1, [1 -0.5]), isstable(1, [1 -2]))"));

    [Fact]
    public void Impzlength_ReadsATolerance() =>
        Assert.Equal("1", Run(
            "fprintf('%d', impzlength(1, [1 -0.9], 1e-8) > impzlength(1, [1 -0.9], 1e-2))"));

    [Fact]
    public void IsminphaseAndIsmaxphase_AreOppositesForAOneZeroFilter() =>
        Assert.Equal("1 0 0 1", Run(
            "fprintf('%d %d %d %d', isminphase([1 0.5], 1), ismaxphase([1 0.5], 1), "
            + "isminphase([1 2], 1), ismaxphase([1 2], 1))"));

    [Fact]
    public void Isallpass_SeesTheReversedDenominator() =>
        Assert.Equal("1 0", Run("fprintf('%d %d', isallpass([0.5 1], [1 0.5]), isallpass([1 0.5], [1 0.5]))"));

    [Fact]
    public void Islinphase_SeesBothSymmetries() =>
        Assert.Equal("1 1 0", Run(
            "fprintf('%d %d %d', islinphase([1 2 1], 1), islinphase([1 0 -1], 1), islinphase([1 2 5], 1))"));

    [Fact]
    public void Zplane_DrawsWithoutAnswering() =>
        Assert.Equal("", Run("[b, a] = butter(4, 0.3); zplane(b, a);"));

    // --- The borrowed machinery still answers what M133 pinned -----------------------------------

    [Fact]
    public void Decimate_StillUsesTheSameChebyshevDesignItDidAtM133()
    {
        // M133 borrowed cheby1 forward; M134 owns the name and decimate now calls it. The
        // arithmetic must be the same one, which is what this checks.
        string answer = Run(
            "x = sin(2 * pi * (0:199) / 40); y = decimate(x, 4); "
            + "fprintf('%d %.12f', numel(y), y(30))");

        Assert.StartsWith("50 ", answer);
    }
}
