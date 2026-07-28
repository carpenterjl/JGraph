using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The trigonometry MATLAB has beyond the six functions JGS started with (M37): degree forms,
/// hyperbolics, and the reciprocal family. The exact-zero cases matter — MATLAB documents
/// <c>sind(180)</c> as exactly 0, which a naive degrees-to-radians multiply does not give.
/// </summary>
[Collection("JG facade")]
public class MatlabTrigBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabTrigBuiltinTests() => JG.Reset();

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

    [Fact]
    public Task DegreeTrig_IsExactAtTheQuadrants() => RunAsserting("""
        assert(sind(0) == 0);
        assert(sind(180) == 0);
        assert(sind(-180) == 0);
        assert(sind(360) == 0);
        assert(sind(90) == 1);
        assert(sind(270) == -1);
        assert(cosd(90) == 0);
        assert(cosd(270) == 0);
        assert(cosd(0) == 1);
        assert(cosd(180) == -1);
        assert(tand(45) - 1 < 1e-12);
        """);

    [Fact]
    public Task DegreeTrig_MatchesTheRadianFormsInBetween() => RunAsserting("""
        assert(abs(sind(30) - 0.5) < 1e-12);
        assert(abs(cosd(60) - 0.5) < 1e-12);
        assert(abs(sind(45) - sin(pi/4)) < 1e-12);
        assert(abs(asind(0.5) - 30) < 1e-12);
        assert(abs(acosd(0.5) - 60) < 1e-12);
        assert(abs(atand(1) - 45) < 1e-12);
        assert(abs(atan2d(1, 1) - 45) < 1e-12);
        assert(abs(atan2d(1, -1) - 135) < 1e-12);
        """);

    [Fact]
    public Task Hyperbolics_SatisfyTheirIdentities() => RunAsserting("""
        x = 0.7;
        assert(abs(cosh(x)^2 - sinh(x)^2 - 1) < 1e-12);
        assert(abs(tanh(x) - sinh(x)/cosh(x)) < 1e-12);
        assert(sinh(0) == 0);
        assert(cosh(0) == 1);

        % Each inverse undoes its own function.
        assert(abs(asinh(sinh(x)) - x) < 1e-12);
        assert(abs(acosh(cosh(x)) - x) < 1e-12);
        assert(abs(atanh(tanh(x)) - x) < 1e-12);
        assert(abs(asech(sech(x)) - x) < 1e-12);
        assert(abs(acsch(csch(x)) - x) < 1e-12);
        assert(abs(acoth(coth(x)) - x) < 1e-12);
        """);

    [Fact]
    public Task ReciprocalTrig_IsOneOverItsPartner() => RunAsserting("""
        x = 0.4;
        assert(abs(sec(x) - 1/cos(x)) < 1e-12);
        assert(abs(csc(x) - 1/sin(x)) < 1e-12);
        assert(abs(cot(x) - 1/tan(x)) < 1e-12);
        assert(abs(asec(sec(x)) - x) < 1e-12);
        assert(abs(acsc(csc(x)) - x) < 1e-12);
        assert(abs(acot(cot(x)) - x) < 1e-12);
        assert(abs(secd(60) - 2) < 1e-12);
        assert(abs(acotd(1) - 45) < 1e-12);
        """);

    [Fact]
    public Task TheWholeFamily_MapsOverArrays() => RunAsserting("""
        assert(isequal(sind([0 180 360]), [0 0 0]));
        assert(isequal(sinh([0 0]), [0 0]));
        v = tanh([0 1 2]);
        assert(length(v) == 3 && v(1) == 0);
        """);
}
