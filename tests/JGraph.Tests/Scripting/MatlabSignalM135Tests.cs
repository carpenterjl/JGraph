using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Signal;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M135: <c>designfilt</c>, the <c>digitalFilter</c> value and the four one-line filters, checked
/// through the MATLAB dialect for what a parity fixture cannot say — which parameter sets are
/// admitted, which methods each one admits, which combinations are refused, and the properties a
/// designed filter is supposed to have rather than the numbers it happens to answer.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabSignalM135Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabSignalM135Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    /// <summary>
    /// Runs one line of the MATLAB dialect and answers what it printed. The semicolon is added here
    /// rather than in every case below: a bare <c>fprintf</c> is a statement whose byte count the
    /// dialect echoes as <c>ans</c>, and every one of these cases wants the printing and not that.
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

    // --- The table ------------------------------------------------------------------------------

    [Fact]
    public void Table_CarriesEveryResponseTheMilestoneClaims()
    {
        string[] expected =
        [
            "bandpassfir", "bandpassiir", "bandstopfir", "bandstopiir", "differentiatorfir",
            "highpassfir", "highpassiir", "hilbertfir", "lowpassfir", "lowpassiir",
        ];

        foreach (string response in expected)
        {
            Assert.True(FilterSpecification.IsResponse(response), response);
        }
    }

    [Fact]
    public void Table_LowpassIirAdmitsAllFourClassicalMethods()
    {
        IReadOnlyList<string> methods = FilterSpecification.Methods("lowpassiir");

        Assert.Contains("butter", methods);
        Assert.Contains("cheby1", methods);
        Assert.Contains("cheby2", methods);
        Assert.Contains("ellip", methods);
    }

    [Fact]
    public void Table_LowpassFirListsFiveParameterSets()
    {
        IReadOnlyList<string> sets = FilterSpecification.ParameterSets("lowpassfir");

        Assert.Equal(5, sets.Count);
        Assert.Contains("FilterOrder, CutoffFrequency", sets);
        Assert.Contains("PassbandFrequency, StopbandFrequency, PassbandRipple, StopbandAttenuation", sets);
    }

    /// <summary>
    /// The default method is not the first one a set lists. Frequency sampling wins where it is
    /// available, then equiripple, then Butterworth — which is why an unqualified
    /// <c>lowpassfir</c> minimum-order call designs an equiripple filter and not a Kaiser one.
    /// </summary>
    [Fact]
    public void Default_PrefersEquirippleOverKaiserWindow()
    {
        Assert.Equal("equiripple", Run(
            "d = designfilt('lowpassfir','PassbandFrequency',0.25,'StopbandFrequency',0.35,"
            + "'PassbandRipple',0.5,'StopbandAttenuation',60); fprintf('%s', d.DesignMethod)"));
    }

    [Fact]
    public void Default_PrefersButterworthForAMinimumOrderIirDesign()
    {
        Assert.Equal("butter", Run(
            "d = designfilt('lowpassiir','PassbandFrequency',0.25,'StopbandFrequency',0.35,"
            + "'PassbandRipple',0.5,'StopbandAttenuation',60); fprintf('%s', d.DesignMethod)"));
    }

    // --- What a designed filter is ---------------------------------------------------------------

    [Fact]
    public void DigitalFilter_ReportsItsOwnClass()
    {
        Assert.Equal("digitalFilter", Run(
            "d = designfilt('lowpassfir','FilterOrder',20,'CutoffFrequency',0.4); fprintf('%s', class(d))"));
    }

    [Fact]
    public void DigitalFilter_KeepsTheSpecificationItWasDesignedFrom()
    {
        Assert.Equal("0.4 20 lowpass fir 2", Run(
            "d = designfilt('lowpassfir','FilterOrder',20,'CutoffFrequency',0.4); "
            + "fprintf('%g %g %s %s %g', d.CutoffFrequency, d.FilterOrder, d.FrequencyResponse, "
            + "d.ImpulseResponse, d.SampleRate)"));
    }

    /// <summary>
    /// An FIR filter's coefficients are its numerator; an IIR filter's are a section matrix, and the
    /// numerator and denominator are that matrix split in half. That is the whole of the difference
    /// between the two, and every method downstream turns on it.
    /// </summary>
    [Fact]
    public void Coefficients_AreTapsForFirAndSectionsForIir()
    {
        Assert.Equal("1 21", Run(
            "d = designfilt('lowpassfir','FilterOrder',20,'CutoffFrequency',0.4); "
            + "fprintf('%d %d', size(d.Coefficients,1), size(d.Coefficients,2))"));
        Assert.Equal("4 6 4 3", Run(
            "d = designfilt('lowpassiir','FilterOrder',8,'PassbandFrequency',0.3,'PassbandRipple',0.5); "
            + "fprintf('%d %d %d %d', size(d.Coefficients,1), size(d.Coefficients,2), "
            + "size(d.Numerator,1), size(d.Numerator,2))"));
    }

    [Fact]
    public void IsFir_TellsTheTwoApart()
    {
        Assert.Equal("1 0", Run(
            "a = designfilt('lowpassfir','FilterOrder',20,'CutoffFrequency',0.4); "
            + "b = designfilt('lowpassiir','FilterOrder',8,'PassbandFrequency',0.3,'PassbandRipple',0.5); "
            + "fprintf('%d %d', isfir(a), isfir(b))"));
    }

    [Fact]
    public void Precision_IsAlwaysDouble()
    {
        Assert.Equal("1 0", Run(
            "d = designfilt('lowpassfir','FilterOrder',20,'CutoffFrequency',0.4); "
            + "fprintf('%d %d', isdouble(d), issingle(d))"));
    }

    /// <summary>
    /// An IIR filter's predicates are asked of its sections, not of the polynomial they multiply out
    /// to. Eight zeros at −1 leave the expanded numerator with roots a hundredth off the circle, so
    /// the expanded filter is not minimum phase when the cascade plainly is.
    /// </summary>
    [Fact]
    public void MinimumPhase_IsAskedOfTheSectionsRatherThanTheExpandedPolynomial()
    {
        Assert.Equal("1", Run(
            "d = designfilt('lowpassiir','FilterOrder',8,'PassbandFrequency',0.3,'PassbandRipple',0.5); "
            + "fprintf('%d', isminphase(d))"));
    }

    // --- The methods that take one ----------------------------------------------------------------

    [Fact]
    public void Analysis_TakesADesignedFilterInPlaceOfANumerator()
    {
        Assert.Equal("20 21 1 1 1", Run(
            "d = designfilt('lowpassfir','FilterOrder',20,'CutoffFrequency',0.4); "
            + "fprintf('%d %d %d %d %d', filtord(d), impzlength(d), firtype(d), isstable(d), islinphase(d))"));
    }

    [Fact]
    public void Freqz_OnADesignedFilterAnswersOnTheHalfCircle()
    {
        Assert.Equal("8 0 1", Run(
            "d = designfilt('lowpassfir','FilterOrder',20,'CutoffFrequency',0.4); "
            + "[h, w] = freqz(d, 8); fprintf('%d %g %d', numel(h), w(1), w(end) < pi)"));
    }

    [Fact]
    public void Tf_HandsBackTheNumeratorAndDenominator()
    {
        Assert.Equal("9 9", Run(
            "d = designfilt('lowpassiir','FilterOrder',8,'PassbandFrequency',0.3,'PassbandRipple',0.5); "
            + "[b, a] = tf(d); fprintf('%d %d', numel(b), numel(a))"));
    }

    [Fact]
    public void Ss_HandsBackAQuadrupleOfTheFiltersOrder()
    {
        Assert.Equal("8 8 8 1", Run(
            "d = designfilt('lowpassiir','FilterOrder',8,'PassbandFrequency',0.3,'PassbandRipple',0.5); "
            + "[A, B, C, D] = ss(d); fprintf('%d %d %d %d', size(A,1), size(A,2), size(C,2), size(D,1))"));
    }

    [Fact]
    public void Filter_RunsADesignedFilterOverASignal()
    {
        Assert.Equal("24", Run(
            "d = designfilt('lowpassfir','FilterOrder',20,'CutoffFrequency',0.4); "
            + "y = filter(d, (1:24)'); fprintf('%d', numel(y))"));
    }

    [Fact]
    public void Filtfilt_RunsADesignedFilterBothWays()
    {
        Assert.Equal("1", Run(
            "d = designfilt('lowpassiir','FilterOrder',4,'PassbandFrequency',0.3,'PassbandRipple',0.5); "
            + "x = sin(2*pi*(0:199)'/40); y = filtfilt(d, x); fprintf('%d', max(abs(y - x)) < 0.2)"));
    }

    /// <summary>
    /// <c>filternorm</c> is not one of the names a designed filter answers to. MATLAB's own list of
    /// its methods does not carry it, so neither does this one.
    /// </summary>
    [Fact]
    public void Filternorm_DoesNotTakeADesignedFilter()
    {
        string message = RunError(
            "d = designfilt('lowpassfir','FilterOrder',20,'CutoffFrequency',0.4); filternorm(d)");

        Assert.Contains("filternorm", message);
    }

    // --- The refusals -----------------------------------------------------------------------------

    [Fact]
    public void Designfilt_RefusesAResponseItDoesNotKnow()
    {
        Assert.Contains("Filter response is not valid",
            RunError("designfilt('nosuchresponse', 'FilterOrder', 20)"));
    }

    [Fact]
    public void Designfilt_ListsTheValidSetsWhenTooFewParametersAreGiven()
    {
        string message = RunError("designfilt('lowpassfir', 'FilterOrder', 20)");

        Assert.Contains("FilterOrder, CutoffFrequency", message);
        Assert.Contains("valid parameter sets", message);
    }

    [Fact]
    public void Designfilt_RefusesAMethodTheParameterSetDoesNotAdmit()
    {
        string message = RunError(
            "designfilt('lowpassiir','FilterOrder',8,'PassbandFrequency',0.3,'PassbandRipple',0.5,"
            + "'DesignMethod','butter')");

        Assert.Contains("butter", message);
        Assert.Contains("invalid design method", message);
    }

    [Fact]
    public void Designfilt_RefusesARepeatedParameter()
    {
        Assert.Contains("more than once", RunError(
            "designfilt('lowpassfir','FilterOrder',20,'CutoffFrequency',0.3,'CutoffFrequency',0.4)"));
    }

    /// <summary>
    /// A full-band differentiator is a type IV filter and needs an odd order; a banded one is a type
    /// III and needs an even one. Neither parity is a preference: the wrong one puts a zero exactly
    /// where the response is meant to be largest.
    /// </summary>
    [Fact]
    public void Differentiator_HoldsEachSpecificationToItsOwnParity()
    {
        Assert.Contains("odd", RunError("designfilt('differentiatorfir','FilterOrder',20)"));
        Assert.Contains("even", RunError(
            "designfilt('differentiatorfir','FilterOrder',21,'PassbandFrequency',0.5,"
            + "'StopbandFrequency',0.6)"));
    }

    // --- The four one-line filters ------------------------------------------------------------------

    [Fact]
    public void Lowpass_ReturnsASignalOfTheSameLengthAndTheFilterItUsed()
    {
        Assert.Equal("600 digitalFilter fir", Run(
            "x = sin(2*pi*0.03*(0:599)') + sin(2*pi*0.41*(0:599)'); "
            + "[y, d] = lowpass(x, 0.2); fprintf('%d %s %s', numel(y), class(d), d.ImpulseResponse)"));
    }

    /// <summary>
    /// The choice between FIR and IIR is the signal's length, not the caller's taste: an FIR filter
    /// that needs more than half as many taps as the signal has samples is no use, so a short signal
    /// gets an elliptic design instead.
    /// </summary>
    [Fact]
    public void Lowpass_ChoosesAnIirDesignWhenTheSignalIsShort()
    {
        Assert.Equal("iir", Run(
            "[y, d] = lowpass((1:60)', 0.2); fprintf('%s', d.ImpulseResponse)"));
    }

    [Fact]
    public void Lowpass_TakesTheImpulseResponseItIsToldTo()
    {
        Assert.Equal("iir fir", Run(
            "x = sin(2*pi*0.03*(0:599)'); "
            + "[~, a] = lowpass(x, 0.2, 'ImpulseResponse', 'iir'); "
            + "[~, b] = lowpass(x, 0.2, 'ImpulseResponse', 'fir'); "
            + "fprintf('%s %s', a.ImpulseResponse, b.ImpulseResponse)"));
    }

    /// <summary>A steeper transition is a longer filter, which is the whole of what steepness buys.</summary>
    [Fact]
    public void Lowpass_SteepnessLengthensTheFilter()
    {
        Assert.Equal("1", Run(
            "x = sin(2*pi*0.03*(0:1999)'); "
            + "[~, a] = lowpass(x, 0.2, 'Steepness', 0.5); "
            + "[~, b] = lowpass(x, 0.2, 'Steepness', 0.95); "
            + "fprintf('%d', filtord(b) > filtord(a))"));
    }

    [Fact]
    public void Bandpass_TakesTwoFrequenciesAndRefusesOne()
    {
        Assert.Equal("600", Run(
            "x = sin(2*pi*0.03*(0:599)'); [y, ~] = bandpass(x, [0.1 0.3]); fprintf('%d', numel(y))"));
        Assert.Contains("two passband frequencies", RunError(
            "bandpass(sin(2*pi*0.03*(0:599)'), 0.2)"));
    }

    [Fact]
    public void Verbs_FilterEachColumnOfAMatrixOnItsOwn()
    {
        Assert.Equal("600 2", Run(
            "x = sin(2*pi*0.03*(0:599)'); y = lowpass([x, x(end:-1:1)], 0.2); "
            + "fprintf('%d %d', size(y,1), size(y,2))"));
    }

    /// <summary>
    /// A lowpass keeps the tone below its passband edge and loses the one above it, which is the
    /// only thing about these names that is not a design decision. The comparison skips the first
    /// and last two hundred samples: a filter that starts from rest has a transient at each end,
    /// and it is a sixth of the signal's own amplitude.
    /// </summary>
    [Fact]
    public void Lowpass_KeepsTheLowToneAndRemovesTheHighOne()
    {
        Assert.Equal("1 1", Run(
            "n = (0:1999)'; lo = sin(2*pi*0.05*n); hi = sin(2*pi*0.4*n); "
            + "y = lowpass(lo + hi, 0.2); m = 201:1800; "
            + "fprintf('%d %d', max(abs(y(m) - lo(m))) < 0.01, max(abs(y)) < 1.3)"));
    }

    [Fact]
    public void Highpass_KeepsTheHighToneInstead()
    {
        Assert.Equal("1", Run(
            "n = (0:1999)'; lo = sin(2*pi*0.05*n); hi = sin(2*pi*0.4*n); "
            + "y = highpass(lo + hi, 0.25); m = 201:1800; "
            + "fprintf('%d', max(abs(y(m) - hi(m))) < 0.01)"));
    }
}
