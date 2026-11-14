using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 305: <c>filter</c> filters a complex call in complex arithmetic, works a single call in
/// single arithmetic, reads integers and logicals as their values, and refuses in R2025b's order and
/// words. Every expectation was recorded from R2025b (probe305, probe305b, probe305c); the parity
/// fixture oi_filter_classes carries the table.
/// </summary>
[Collection("JG facade")]
public class FilterClassesTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public FilterClassesTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static string Text(ScriptRunResult result, string name)
    {
        object? raw = Assert.Single(result.Variables, v => v.Name == name).RawValue;
        return raw switch
        {
            string text => text,
            string[] { Length: 1 } texts => texts[0],
            _ => Assert.IsType<string>(raw),
        };
    }

    [Fact]
    public async Task ASingleFilterIsWorkedInSingleArithmetic()
    {
        // R2025b: y(8) = 2.9062502384185791 and zf(2) = -0.72656255960464478; rounding a double
        // computation once gave 2.90625 and -0.7265625.
        ScriptRunResult result = await RunMatlab(
            "[y, zf] = filter([0.2 0.3], [1 -0.5 0.25], single([1 4 2 8 5 7 3 6])); "
            + "a = sprintf('%.17g', y(8)); b = sprintf('%.17g', zf(2)); c = class(zf); "
            + "d = class(filter([1 2], 1, [1 2], [], single(2)));");
        Assert.True(result.Success, result.Message);
        Assert.Equal("2.9062502384185791", Text(result, "a"));
        Assert.Equal("-0.72656255960464478", Text(result, "b"));
        Assert.Equal("single", Text(result, "c"));
        Assert.Equal("double", Text(result, "d"));
    }

    [Fact]
    public async Task AComplexFilterIsFilteredAndARealAnswerComesBackReal()
    {
        ScriptRunResult result = await RunMatlab(
            "x = [1 4 2 8 5 7 3 6]; [y, zf] = filter([0.2 0.3], [1 -0.5 0.25], x + 1i * x(end:-1:1)); "
            + "a = sprintf('%.17g %.17g', real(zf(2)), imag(zf(2))); "
            + "b = sprintf('%d %d', isreal(filter([0.2 0.3], 1, complex(x))), isreal(y)); "
            + "[~, z2] = filter([1 2], 1, [1 2], 1i); c = sprintf('%d', isreal(z2));");
        Assert.True(result.Success, result.Message);
        Assert.Equal("-0.7265625 -0.3914062499999999", Text(result, "a"));
        Assert.Equal("1 0", Text(result, "b"));
        Assert.Equal("1", Text(result, "c"));
    }

    [Theory]
    [InlineData("filter([1 2; 3 4], 1, 1:8)", "MATLAB:filter:notVectorInput")]
    [InlineData("filter([], 1, 1:8)", "MATLAB:filter:notVectorInput")]
    [InlineData("filter(sparse([1 2]), 1, 1:8)", "MATLAB:filter:sparseInput")]
    [InlineData("filter([1 2], 1, 'abc')", "MATLAB:filter:inputType")]
    [InlineData("filter('ab', 1, sparse(1:8))", "MATLAB:filter:inputType")]
    [InlineData("filter(sparse([1 2]), 'ab', 1:8)", "MATLAB:filter:sparseInput")]
    [InlineData("filter([1 2], [0 1], 1:8)", "MATLAB:filter:firstElementOfDenominatorFilterZero")]
    [InlineData("filter([1 2], [NaN 1], 1:8)", "MATLAB:filter:firstElementOfDenominatorFilterNotFinite")]
    [InlineData("filter([1 2], [0 1], 1:8, [], 0)", "MATLAB:getdimarg:dimensionMustBePositiveInteger")]
    [InlineData("filter([1 2], [0 1], 1:8, [1 2 3])", "MATLAB:filter:firstElementOfDenominatorFilterZero")]
    public async Task ARefusalComesInR2025bsOrder(string call, string identifier)
    {
        ScriptRunResult result = await RunMatlab($"try, {call}; a = 'none'; catch e, a = e.identifier; end");
        Assert.True(result.Success, result.Message);
        Assert.Equal(identifier, Text(result, "a"));
    }

    [Fact]
    public async Task AnEmptyCoefficientAnswersZeros()
    {
        // R2025b: an empty a leaves zf as b's taps on the signal's tail, undivided.
        ScriptRunResult result = await RunMatlab(
            "[y, zf] = filter([1 2 3], zeros(1, 0), [1 4 2 8]); a = mat2str(y); b = mat2str(zf); "
            + "[y2, z2] = filter(zeros(1, 0), [1 0.5], [1 4 2 8], 3); c = mat2str(y2); d = mat2str(z2);");
        Assert.True(result.Success, result.Message);
        Assert.Equal("[0 0 0 0]", Text(result, "a"));
        Assert.Equal("[22;24]", Text(result, "b"));
        Assert.Equal("[0 0 0 0]", Text(result, "c"));
        Assert.Equal("0", Text(result, "d"));
    }
}
