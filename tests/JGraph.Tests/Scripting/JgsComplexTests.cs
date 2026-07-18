using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M21b: complex numbers — <c>2i</c> literals, arithmetic promotion, complex-aware builtins
/// (<c>abs</c>/<c>real</c>/<c>imag</c>/<c>conj</c>/<c>angle</c>), display formatting, and the
/// deliberate errors (ordering comparisons, modulo, plotting complex data).
/// </summary>
[Collection("JG facade")]
public class JgsComplexTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsComplexTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    private async Task<string> Eval(string expression)
    {
        _output.Normal.Clear();
        ScriptRunResult result = await Run($"print({expression})");
        Assert.True(result.Success, result.Message);
        return Assert.Single(_output.Normal).TrimEnd('\n');
    }

    [Fact]
    public async Task Literals_AndArithmetic_Promote()
    {
        Assert.Equal("3+4i", await Eval("3 + 4i"));
        Assert.Equal("3-4i", await Eval("3 - 4i"));
        Assert.Equal("2i", await Eval("2i"));
        Assert.Equal("-2i", await Eval("-2i"));
        Assert.Equal("-1", await Eval("1i * 1i"));      // i^2 collapses to a real number
        Assert.Equal("2", await Eval("(1+1i) + (1-1i)")); // zero imaginary normalizes to Number
        Assert.Equal("5+10i", await Eval("5 * (1 + 2i)"));
        Assert.Equal("1i", await Eval("1 / (0 - 1i)")); // 1 / -i = i
    }

    [Fact]
    public async Task ComplexAwareBuiltins_MatchTheirDefinitions()
    {
        Assert.Equal("5", await Eval("abs(3 + 4i)"));
        Assert.Equal("3", await Eval("real(3 + 4i)"));
        Assert.Equal("4", await Eval("imag(3 + 4i)"));
        Assert.Equal("3-4i", await Eval("conj(3 + 4i)"));
        Assert.Equal("3", await Eval("real(conj(3 + 4i))"));
        Assert.Equal("0", await Eval("angle(5)"));
        Assert.Equal("true", await Eval("angle(1i) > 1.57 && angle(1i) < 1.58"));
        Assert.Equal("0", await Eval("imag(7)"));
        Assert.Equal("7", await Eval("conj(7)"));
    }

    [Fact]
    public async Task ComplexArrays_BroadcastElementwise()
    {
        Assert.Equal("[1+1i, 2+1i]", await Eval("[1, 2] + 1i"));
        Assert.Equal("[1, 2]", await Eval("real([1 + 3i, 2 + 4i])"));
        Assert.Equal("[3, 4]", await Eval("imag([1 + 3i, 2 + 4i])"));
        Assert.Equal("[5, 13]", await Eval("abs([3 + 4i, 5 + 12i])"));
    }

    [Fact]
    public async Task Equality_Works_ButOrderingErrors()
    {
        Assert.Equal("true", await Eval("(1 + 2i) == (1 + 2i)"));
        Assert.Equal("true", await Eval("(1 + 2i) ~= (1 + 3i)"));

        ScriptRunResult result = await Run("print(1i < 2i)");
        Assert.False(result.Success);
        Assert.Contains("not defined for complex numbers", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task Modulo_OnComplex_IsAClearError()
    {
        ScriptRunResult result = await Run("print(3i % 2)");

        Assert.False(result.Success);
        Assert.Contains("'%' is not defined for complex", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task PlottingComplexData_TellsTheUserWhatToDo()
    {
        ScriptRunResult result = await Run("plot([1, 2], [1i, 2i])");

        Assert.False(result.Success);
        Assert.Contains("take abs(), real(), or imag() first", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task VariablesPane_ShowsComplexTypeAndDisplay()
    {
        ScriptRunResult result = await Run("let z = 1 + 2i;");

        Assert.True(result.Success, result.Message);
        ScriptVariable z = Assert.Single(result.Variables, v => v.Name == "z");
        Assert.Equal("complex", z.Type);
        Assert.Equal("1+2i", z.DisplayValue);
    }
}
