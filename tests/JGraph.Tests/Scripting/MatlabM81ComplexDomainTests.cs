using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M81: the maths family where it leaves the reals.
/// <para>
/// The milestone opened by re-reading a recorded divergence — "<c>sqrt</c>/<c>log</c> stay real-domain
/// and error on complex input" — and finding it false since M42. The first fixture below is the
/// assertion that bullet was about and that nothing had ever made: the behaviour existed for
/// seventeen milestones with no test naming it. The rest are the family that had genuinely never
/// followed <c>sqrt</c> across, and the one operator with a domain.
/// </para>
/// <para>
/// Every expression here was run at the CLI before it was written down, and the branch-cut values are
/// MATLAB R2021b's own, because a principal value is a convention and two libraries can disagree
/// about it while both being right.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabM81ComplexDomainTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM81ComplexDomainTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static string Text(ScriptRunResult result, string name) =>
        Assert.IsType<string>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static void Succeeded(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message);

    /// <summary>
    /// Runs one expression and hands back its real and imaginary parts, which is how a complex answer
    /// is checked without depending on how it prints.
    /// </summary>
    private async Task<(double Real, double Imaginary)> Parts(string expression)
    {
        ScriptRunResult result = await RunMatlab($"z = {expression}; re = real(z); im = imag(z);");
        Succeeded(result);
        return (Number(result, "re"), Number(result, "im"));
    }

    private async Task Answers(string expression, double real, double imaginary)
    {
        (double gotReal, double gotImaginary) = await Parts(expression);
        Assert.Equal(real, gotReal, 9);
        Assert.Equal(imaginary, gotImaginary, 9);
    }

    /// <summary>
    /// Several refusals in a row, one after another. Deliberately not <c>Task.WhenAll</c>: the facade
    /// these scripts run against is one static figure stack, so two scripts at once are two scripts
    /// editing the same figure — which passes alone and fails beside its neighbours.
    /// </summary>
    private async Task RefusesEach(params (string Code, string Fragment)[] cases)
    {
        foreach ((string code, string fragment) in cases)
        {
            ScriptRunResult result = await RunMatlab(code);
            Assert.False(result.Success, $"expected a refusal from: {code}");
            Assert.Contains(fragment, result.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // --- The two the divergence was about ---------------------------------------------------------

    /// <summary>
    /// The assertion the recorded divergence claimed was impossible. M42 made it true and no test ever
    /// said so, which is exactly how a bullet outlives the thing it describes.
    /// </summary>
    [Fact]
    public async Task SqrtAndLogLeaveTheRealsWhenTheirArgumentDoes()
    {
        await Answers("sqrt(-1)", 0, 1);
        await Answers("sqrt(-4)", 0, 2);
        await Answers("log(-1)", 0, Math.PI);
        await Answers("log(-exp(1))", 1, Math.PI);
    }

    /// <summary>And a complex argument, which the same bullet said was an error.</summary>
    [Fact]
    public async Task SqrtAndLogTakeAComplexArgument()
    {
        await Answers("sqrt(1i)", Math.Sqrt(0.5), Math.Sqrt(0.5));
        await Answers("log(1i)", 0, Math.PI / 2);
        await Answers("exp(1i * pi / 2)", 0, 1);
    }

    // --- The family that never followed -----------------------------------------------------------

    [Fact]
    public async Task TheLogarithmsPromoteBelowZero()
    {
        await Answers("log2(-1)", 0, Math.PI / Math.Log(2.0));
        await Answers("log10(-1)", 0, Math.PI / Math.Log(10.0));
        await Answers("log1p(-2)", 0, Math.PI);
        await Answers("log2(-8)", 3, Math.PI / Math.Log(2.0));
    }

    /// <summary>
    /// The inverse trigonometric family outside its domain, against MATLAB's principal values. .NET's
    /// own <c>Complex.Asin</c> answers the conjugate of the first of these, which is why M81 writes
    /// the formula out instead of borrowing it.
    /// </summary>
    [Fact]
    public async Task TheInverseTrigonometryPromotesOutsideItsDomain()
    {
        await Answers("asin(2)", 1.5707963267948966, -1.3169578969248166);
        await Answers("acos(2)", 0, 1.3169578969248166);
        await Answers("asec(0.5)", 0, 1.3169578969248166);
        await Answers("acsc(0.5)", 1.5707963267948966, -1.3169578969248166);
        await Answers("asind(2)", 90, -75.4561292902169);
        await Answers("acosd(2)", 0, 75.4561292902169);
    }

    [Fact]
    public async Task TheInverseHyperbolicsPromoteOutsideTheirDomain()
    {
        await Answers("acosh(0)", 0, 1.5707963267948966);
        await Answers("atanh(2)", 0.5493061443340549, 1.5707963267948966);
        await Answers("acoth(0.5)", 0.5493061443340549, 1.5707963267948966);
        await Answers("asech(2)", 0, 1.0471975511965976);
    }

    /// <summary>
    /// The functions that never leave the reals for a real argument gained a complex definition only
    /// so that a complex <em>argument</em> has somewhere to go. Before this they were refused outright,
    /// which made complex numbers representable but not usable.
    /// </summary>
    [Fact]
    public async Task TheAlwaysRealFamilyTakesAComplexArgument()
    {
        await Answers("cos(1i)", Math.Cosh(1.0), 0);
        await Answers("sin(1i)", 0, Math.Sinh(1.0));
        await Answers("tanh(1i)", 0, Math.Tan(1.0));
        await Answers("sinh(1i)", 0, Math.Sin(1.0));
        await Answers("atan(2i)", 1.5707963267948966, 0.5493061443340549);
        await Answers("asinh(2i)", 1.3169578969248164, 1.5707963267948966);
        await Answers("expm1(1i * pi)", -2, 0);
    }

    /// <summary>The rounding family applies its rule to both parts, which is MATLAB's answer.</summary>
    [Fact]
    public async Task TheRoundingFamilyRoundsBothParts()
    {
        await Answers("floor(1.5 + 2.5i)", 1, 2);
        await Answers("ceil(1.2 + 2.2i)", 2, 3);
        await Answers("round(1.5 + 2.5i)", 2, 3);
        await Answers("fix(-1.7 + 2.7i)", -1, 2);
        await Answers("sign(3 + 4i)", 0.6, 0.8);

        // round(x, n) is declared separately from the round beside floor and ceil, and wins; both
        // needed the complex arm or a complex number reached only the one that refused it.
        await Answers("round(1.234 + 5.678i, 2)", 1.23, 5.68);
    }

    // --- The one operator with a domain -----------------------------------------------------------

    [Fact]
    public async Task ANegativeBaseWithAFractionalPowerLeavesTheReals()
    {
        await Answers("(-8)^0.5", 0, 2.8284271247461903);
        await Answers("power(-8, 0.5)", 0, 2.8284271247461903);
        await Answers("(-8)^(1/3)", 1.0, 1.7320508075688772);
        await Answers("2^(1 + 1i)", 1.5384778027279442, 1.2779225526272695);
    }

    /// <summary>A whole exponent keeps a negative base real, which is why <c>(-8)^2</c> is not complex.</summary>
    [Fact]
    public async Task AWholeExponentKeepsANegativeBaseReal()
    {
        ScriptRunResult result = await RunMatlab(
            "a = (-8)^2; b = (-8)^3; c = 2^0.5; d = isreal((-8)^2); e = isreal((-8)^0.5);");
        Succeeded(result);
        Assert.Equal(64, Number(result, "a"));
        Assert.Equal(-512, Number(result, "b"));
        Assert.Equal(Math.Sqrt(2.0), Number(result, "c"), 12);
        Assert.True(Assert.IsType<bool>(Assert.Single(result.Variables, v => v.Name == "d").RawValue));
        Assert.False(Assert.IsType<bool>(Assert.Single(result.Variables, v => v.Name == "e").RawValue));
    }

    /// <summary>
    /// The packed kernel writes doubles, so it declines the whole array when any pair would promote —
    /// and the boxed path behind it gives the same answers, which is what "the answer does not depend
    /// on which path ran" has to mean.
    /// </summary>
    [Fact]
    public async Task AnArrayPowerPromotesTheElementsThatNeedIt()
    {
        ScriptRunResult result = await RunMatlab(
            "z = [-8 4].^0.5; a = imag(z(1)); b = imag(z(2)); c = real(z(2)); d = isreal([1 4].^0.5);");
        Succeeded(result);
        Assert.Equal(2.8284271247461903, Number(result, "a"), 9);
        Assert.Equal(0, Number(result, "b"));
        Assert.Equal(2, Number(result, "c"));
        Assert.True(Assert.IsType<bool>(Assert.Single(result.Variables, v => v.Name == "d").RawValue));
    }

    // --- The real path is not disturbed -----------------------------------------------------------

    /// <summary>
    /// The whole point of a <c>staysReal</c> predicate is that an argument inside the domain computes
    /// exactly what it did before, on the same flat path. An array with one element outside promotes
    /// as a whole, which is MATLAB's rule.
    /// </summary>
    [Fact]
    public async Task AnArrayInsideTheDomainStaysRealAndOneElementOutsidePromotesItAll()
    {
        ScriptRunResult result = await RunMatlab(
            "a = isreal(sqrt([1 4 9])); b = isreal(sqrt([1 -4 9])); "
            + "c = sqrt([1 4 9]); d = imag(sqrt([1 -4 9]));");
        Succeeded(result);
        Assert.True(Assert.IsType<bool>(Assert.Single(result.Variables, v => v.Name == "a").RawValue));
        Assert.False(Assert.IsType<bool>(Assert.Single(result.Variables, v => v.Name == "b").RawValue));
        Assert.Equal([1.0, 2.0, 3.0], Assert.IsType<double[]>(
            Assert.Single(result.Variables, v => v.Name == "c").RawValue));
        Assert.Equal([0.0, 2.0, 0.0], Assert.IsType<double[]>(
            Assert.Single(result.Variables, v => v.Name == "d").RawValue));
    }

    /// <summary>The identities are the check a reader can make without a table of principal values.</summary>
    [Fact]
    public async Task TheIdentitiesHold()
    {
        ScriptRunResult result = await RunMatlab(
            "a = abs(exp(log(3 + 4i)) - (3 + 4i)); "
            + "b = abs(cos(1i) - cosh(1)); "
            + "c = abs(sin(asin(2)) - 2); "
            + "d = abs(tanh(atanh(2)) - 2); "
            + "e = abs(((-8)^(1/3))^3 + 8); "
            + "f = abs(cosh(acosh(0)));");
        Succeeded(result);
        foreach (string name in new[] { "a", "b", "c", "d", "e", "f" })
        {
            Assert.True(Number(result, name) < 1e-12, $"{name} drifted");
        }
    }

    // --- mat2str, found by probing rather than by counting -----------------------------------------

    /// <summary>
    /// <c>mat2str</c>'s whole contract is that its text reads back as the same value, and it could not
    /// hold a complex one: a complex scalar came back as the bare text <c>[]</c> and a complex array
    /// threw. Every element is written with both parts once any of them is complex, because that is
    /// what makes the text read back as complex.
    /// </summary>
    [Fact]
    public async Task Mat2StrWritesAComplexValueTheWayTheLanguageReadsItBack()
    {
        ScriptRunResult result = await RunMatlab(
            "a = mat2str(sqrt(-1)); b = mat2str([1i 2]); c = mat2str(3 - 4i); d = mat2str([1 2;3 4]);");
        Succeeded(result);
        Assert.Equal("0+1i", Text(result, "a"));
        Assert.Equal("[0+1i 2+0i]", Text(result, "b"));
        Assert.Equal("3-4i", Text(result, "c"));
        Assert.Equal("[1 2;3 4]", Text(result, "d"));
    }

    // --- What still refuses, and why ---------------------------------------------------------------

    /// <summary>
    /// The <c>real*</c> family exists to refuse, and <c>nthroot</c> exists to answer <c>-2</c> where
    /// <c>(-8)^(1/3)</c> is complex. Pinning them beside the promotions is what stops a later wave
    /// widening the seam over the top of three deliberate decisions.
    /// </summary>
    [Fact]
    public async Task TheRealOnlyFamilyStillRefuses()
    {
        await RefusesEach(
            ("x = realsqrt(-1);", "must not be negative"),
            ("x = reallog(-1);", "must not be negative"),
            ("x = realpow(-8, 0.5);", "not a real number"),
            ("x = nthroot(-8, 2);", "no real even root"),
            ("x = mod(1i, 2);", "complex"),
            ("x = rem(1i, 2);", "complex"));

        ScriptRunResult result = await RunMatlab("a = nthroot(-8, 3);");
        Succeeded(result);
        Assert.Equal(-2, Number(result, "a"), 12);
    }
}
