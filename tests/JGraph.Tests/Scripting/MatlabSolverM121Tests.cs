using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The three names the capability probe found missing from the solvers group (M121):
/// <c>integral</c>, <c>quadgk</c> and <c>odeset</c> — with <c>odeget</c> beside the last, and
/// <c>ode45</c> finally able to be told something.
/// </summary>
/// <remarks>
/// Every expectation here was measured against R2024a rather than reasoned about. Where an answer
/// is graded loosely it is because the tolerance asked for is loose: at a relative tolerance of
/// 1e-6 an adaptive integrator promises six figures and not sixteen.
/// </remarks>
[Collection("JG facade")]
public class MatlabSolverM121Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabSolverM121Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    /// <summary>
    /// One script, and only what it printed. The sink is replaced each time rather than read from
    /// the end, because it accumulates across runs and a test that read the whole of it would
    /// silently pass on the previous script's output.
    /// </summary>
    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    private double Value(string expression)
    {
        string text = Run($"fprintf('%.17g\\n', {expression});").Trim();
        return double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Within(double expected, double actual, string what, double relative = 1e-6)
    {
        double allowed = Math.Max(1e-10, relative * Math.Abs(expected));
        Assert.True(
            Math.Abs(actual - expected) <= allowed,
            $"{what}: {actual:R} is {Math.Abs(actual - expected):E2} from {expected:R}, "
            + $"more than the {allowed:E2} asked for");
    }

    [Fact]
    public void IntegralAnswersTheFormsTheCapabilityProbeAsksFor()
    {
        Within(1.0 / 3.0, Value("integral(@(z) z.^2, 0, 1)"), "integral of z^2");
        Within(Math.Sqrt(Math.PI), Value("integral(@(x) exp(-x.^2), -Inf, Inf)"), "the Gaussian");
        Within(2.0, Value("integral(@(x) 1./sqrt(x), 0, 1)"), "an endpoint singularity");
        Within(1.0, Value("integral(@(x) exp(-x), 0, Inf)"), "a half-infinite range");
    }

    [Fact]
    public void IntegralTakesTheOptionsMatlabDocuments()
    {
        Within(1.0 / 3.0, Value("integral(@(x) x.^2, 0, 1, 'RelTol', 1e-12, 'AbsTol', 1e-14)"), "tolerances");
        Within(0.29, Value("integral(@(x) abs(x-0.3), 0, 1, 'Waypoints', 0.3)"), "a waypoint");

        // 'ArrayValued' is how an integrand that cannot take a vector says so — z^2 rather than
        // z.^2 — and without it the same expression is a refusal, not a wrong answer.
        Within(1.0 / 3.0, Value("integral(@(z) z^2, 0, 1, 'ArrayValued', true)"), "one point at a time");
    }

    [Fact]
    public void AnIntegrandThatIsNotVectorisedIsRefusedByName()
    {
        // @(x) 1 answers one number however many points it is given. MATLAB refuses it rather than
        // spreading it across the panel, and so does this: an integrand that is quietly spread is
        // indistinguishable from one that is right, and the caller never learns that the rest of
        // the expression was not vectorised either.
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            "q = integral(@(x) 1, 0, 1);", context, default, sourceId: "", hook: null, JgsDialect.Matlab);

        Assert.False(result.Success);

        // The message has to say what to do about it, and it is MATLAB's own words.
        string said = result.Message + _output.ErrorText;
        Assert.Contains("ArrayValued", said, StringComparison.Ordinal);
        Assert.Contains("same size as the input", said, StringComparison.Ordinal);
    }

    [Fact]
    public void QuadgkAnswersItsErrorBoundAsWellAsItsValue()
    {
        string output = Run("[q, e] = quadgk(@(z) z.^2, 0, 1); fprintf('%.17g %.17g\\n', q, e);");
        string[] parts = output.Trim().Split(' ');

        Within(1.0 / 3.0, double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), "quadgk");

        double bound = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(bound >= 0 && bound < 1e-6, $"the error bound was {bound:R}");
    }

    [Fact]
    public void OdesetHoldsMatlabsTwentyTwoFieldsInMatlabsOrder()
    {
        // A script that walks fieldnames(odeset) sees this order, so it is pinned rather than
        // sorted. It was read off R2024a; nothing about it is derivable.
        string names = Run("f = fieldnames(odeset()); for k = 1:numel(f), fprintf('%s ', f{k}); end");

        Assert.Equal(
            "AbsTol BDF Events InitialStep Jacobian JConstant JPattern Mass MassSingular MaxOrder "
            + "MaxStep NonNegative NormControl OutputFcn OutputSel Refine RelTol Stats Vectorized "
            + "MStateDependence MvPattern InitialSlope",
            names.Trim());
    }

    [Fact]
    public void OdesetSetsWhatItIsToldAndLeavesTheRestEmpty()
    {
        string output = Run("""
            o = odeset('RelTol', 1e-6);
            fprintf('%g %d %d\n', o.RelTol, isempty(o.AbsTol), numel(fieldnames(o)));
            """);

        Assert.Contains("1e-06 1 22", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbbreviatedPropertyNameIsAccepted()
    {
        // MATLAB matches any unique leading portion, ignoring case, and scripts rely on it.
        string output = Run("""
            a = odeset('rel', 1e-3);
            b = odeset('MaxSt', 0.1);
            fprintf('%g %g\n', a.RelTol, b.MaxStep);
            """);

        Assert.Contains("0.001 0.1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OdesetMergesAnEarlierStructureAndThenTheNamesAfterIt()
    {
        string output = Run("""
            a = odeset('RelTol', 1e-6);
            b = odeset(a, 'AbsTol', 1e-8, 'Refine', 8);
            fprintf('%g %g %g\n', b.RelTol, b.AbsTol, b.Refine);
            """);

        Assert.Contains("1e-06 1e-08 8", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OdegetReadsOneSettingOrTheFallbackItIsGiven()
    {
        string output = Run("""
            o = odeset('RelTol', 1e-8);
            fprintf('%g %g\n', odeget(o, 'RelTol'), odeget(o, 'AbsTol', 7));
            """);

        Assert.Contains("1e-08 7", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Ode45ObeysRefineAndTheTolerancesAndMaxStep()
    {
        // Every count here is R2024a's own, and they are counts rather than values because that is
        // what an option changes: how many points come back and how hard the solver worked.
        string output = Run("""
            f = @(t, y) -2 * y;
            [t1, ~] = ode45(f, [0 1], 1);
            [t2, ~] = ode45(f, [0 1], 1, odeset('Refine', 1));
            [t3, ~] = ode45(f, [0 1], 1, odeset('RelTol', 1e-8, 'AbsTol', 1e-10));
            [t4, ~] = ode45(f, [0 1], 1, odeset('MaxStep', 0.01));
            [t5, ~] = ode45(f, [0 1], 1, []);
            fprintf('%d %d %d %d %d\n', numel(t1), numel(t2), numel(t3), numel(t4), numel(t5));
            """);

        Assert.Contains("41 11 101 401 41", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AskingForATighterToleranceActuallyMakesOde45MoreAccurate()
    {
        // The counts above prove the option arrived; this proves it did something. Without it the
        // solver's default 1e-3 leaves about 3e-8 of error in exp(-2).
        string output = Run("""
            f = @(t, y) -2 * y;
            [~, a] = ode45(f, [0 1], 1);
            [~, b] = ode45(f, [0 1], 1, odeset('RelTol', 1e-10, 'AbsTol', 1e-12));
            fprintf('%.17g %.17g\n', abs(a(end) - exp(-2)), abs(b(end) - exp(-2)));
            """);

        string[] parts = output.Trim().Split(' ');
        double loose = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        double tight = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(tight < loose / 100, $"tight {tight:E2} against loose {loose:E2}");
    }

    [Fact]
    public void Ode45WithoutOptionsIsUnchanged()
    {
        // The whole point of the default arm: a call written before M121 must answer what it did.
        // exp(-2) to the accuracy the default tolerance gives, which is R2024a's answer too.
        string output = Run("""
            [t, y] = ode45(@(t, y) -2 * y, [0 1], 1);
            fprintf('%d %.12g\n', numel(t), y(end));
            """);

        Assert.Contains("41 0.135335316718", output, StringComparison.Ordinal);
    }
}
