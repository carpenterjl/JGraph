using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The matrix operators over a 1-by-1 array (M138): <c>[1]</c> is the same value as <c>1</c>, and
/// MATLAB's <c>*</c>, <c>/</c>, <c>\</c> and <c>^</c> read it as the scalar it is.
/// </summary>
/// <remarks>
/// The bug report was <c>zeros(2, 3) * [1]</c>, which was refused for a dimension mismatch while
/// <c>zeros(2, 3) * 1</c> answered. Which side has to be the 1-by-1 differs per operator and was
/// measured against R2025b rather than reasoned about: <c>A / s</c> divides where <c>s / A</c> still
/// solves and is refused, <c>s \ A</c> divides where <c>A \ s</c> is refused, and <c>A ^ s</c> is a
/// matrix power where <c>s ^ A</c> is neither of those. Every expectation below is an R2025b answer.
/// </remarks>
[Collection("JG facade")]
public class MatlabScalarArrayOperandsM138Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabScalarArrayOperandsM138Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string Run(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    private string Failure(string code)
    {
        _output = new RecordingScriptOutput();
        var context = new ScriptContext(_output, (_, _) => { }, null);
        ScriptRunResult result = JgsRunner.Run(
            code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.False(result.Success, "the statement was expected to be refused: " + _output.NormalText);
        return result.Message ?? _output.ErrorText;
    }

    /// <summary>
    /// The class, shape and elements of an expression, printed column-major. The real and imaginary
    /// parts go separately because mat2str prints a <c>+0i</c> where MATLAB prints none (M137).
    /// </summary>
    private string Shape(string expression) => Run(
        Setup + $"\nv = {expression};\n" +
        "fprintf('%s | %d %d | %s | %s', class(v), size(v, 1), size(v, 2), " +
        "mat2str(real(transpose(v(:)))), mat2str(imag(transpose(v(:)))));");

    private const string Setup =
        "A = [1 2; 3 4];\n" +
        "v = [1 2 3];\n" +
        "c = [2];\n" +
        "z = zeros(2, 3);";

    [Theory]

    // The bug report itself: a 1-by-1 array scales, from either side, exactly as the number does.
    [InlineData("z * [1]", "double | 2 3 | [0 0 0 0 0 0] | [0 0 0 0 0 0]")]
    [InlineData("[1] * z", "double | 2 3 | [0 0 0 0 0 0] | [0 0 0 0 0 0]")]
    [InlineData("z * 1", "double | 2 3 | [0 0 0 0 0 0] | [0 0 0 0 0 0]")]
    [InlineData("A * c", "double | 2 2 | [2 6 4 8] | [0 0 0 0]")]
    [InlineData("c * A", "double | 2 2 | [2 6 4 8] | [0 0 0 0]")]
    [InlineData("v * c", "double | 1 3 | [2 4 6] | [0 0 0]")]
    [InlineData("c * v", "double | 1 3 | [2 4 6] | [0 0 0]")]
    [InlineData("A * [0]", "double | 2 2 | [0 0 0 0] | [0 0 0 0]")]

    // Division takes the scalar on the right, and the backslash takes it on the left.
    [InlineData("A / c", "double | 2 2 | [0.5 1.5 1 2] | [0 0 0 0]")]
    [InlineData("v / c", "double | 1 3 | [0.5 1 1.5] | [0 0 0]")]
    [InlineData("c \\ A", "double | 2 2 | [0.5 1.5 1 2] | [0 0 0 0]")]
    [InlineData("c \\ v", "double | 1 3 | [0.5 1 1.5] | [0 0 0]")]

    // A 1-by-1 array on both sides is a plain scalar operation, and so is a 1-by-1 against a number.
    [InlineData("c * c", "double | 1 1 | 4 | 0")]
    [InlineData("c / c", "double | 1 1 | 1 | 0")]
    [InlineData("c \\ c", "double | 1 1 | 1 | 0")]
    [InlineData("c ^ c", "double | 1 1 | 4 | 0")]
    [InlineData("c * 2", "double | 1 1 | 4 | 0")]
    [InlineData("2 * c", "double | 1 1 | 4 | 0")]
    [InlineData("c / 2", "double | 1 1 | 1 | 0")]
    [InlineData("2 / c", "double | 1 1 | 1 | 0")]
    [InlineData("c \\ 2", "double | 1 1 | 1 | 0")]
    [InlineData("2 \\ c", "double | 1 1 | 1 | 0")]
    [InlineData("c ^ 2", "double | 1 1 | 4 | 0")]
    [InlineData("2 ^ c", "double | 1 1 | 4 | 0")]

    // A 1-by-1 exponent raises a matrix to a power, the way the bare number always has.
    [InlineData("A ^ c", "double | 2 2 | [7 15 10 22] | [0 0 0 0]")]
    [InlineData("A ^ 2", "double | 2 2 | [7 15 10 22] | [0 0 0 0]")]
    [InlineData("A ^ [3]", "double | 2 2 | [37 81 54 118] | [0 0 0 0]")]
    [InlineData("A ^ [true]", "double | 2 2 | [1 3 2 4] | [0 0 0 0]")]

    // The dotted spellings were already elementwise and must stay exactly as they were.
    [InlineData("A .* c", "double | 2 2 | [2 6 4 8] | [0 0 0 0]")]
    [InlineData("A ./ c", "double | 2 2 | [0.5 1.5 1 2] | [0 0 0 0]")]
    [InlineData("c .\\ A", "double | 2 2 | [0.5 1.5 1 2] | [0 0 0 0]")]
    [InlineData("A .^ c", "double | 2 2 | [1 9 4 16] | [0 0 0 0]")]
    public void AOneByOneArrayIsTheScalarItHolds(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // A 1-by-1 array that came out of an index, which is where they mostly come from.
    [InlineData("A * A(1, 1)", "double | 2 2 | [1 3 2 4] | [0 0 0 0]")]
    [InlineData("A(1, 1) * A", "double | 2 2 | [1 3 2 4] | [0 0 0 0]")]
    [InlineData("A(1, 1) \\ A", "double | 2 2 | [1 3 2 4] | [0 0 0 0]")]
    [InlineData("A(2, 2) * A", "double | 2 2 | [4 12 8 16] | [0 0 0 0]")]

    // A 1-by-1 slice of a column keeps carrying the class it was cut from.
    [InlineData("single([1 2 3]) * [2]", "single | 1 3 | [2 4 6] | [0 0 0]")]
    [InlineData("[2] * single([1 2 3])", "single | 1 3 | [2 4 6] | [0 0 0]")]
    [InlineData("A * single([2])", "single | 2 2 | [2 6 4 8] | [0 0 0 0]")]
    [InlineData("A * [true]", "double | 2 2 | [1 3 2 4] | [0 0 0 0]")]
    [InlineData("[true] * A", "double | 2 2 | [1 3 2 4] | [0 0 0 0]")]
    [InlineData("[true false; false true] * [2]", "double | 2 2 | [2 0 0 2] | [0 0 0 0]")]
    public void AScalarOperandKeepsWhatItIs(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // A 1-by-1 complex array scales as its number does, and does not go to the matrix product.
    [InlineData("[1+2i] * A", "double | 2 2 | [1 3 2 4] | [2 6 4 8]")]
    [InlineData("A * [1+2i]", "double | 2 2 | [1 3 2 4] | [2 6 4 8]")]
    [InlineData("A / [1+2i]", "double | 2 2 | [0.2 0.6 0.4 0.8] | [-0.4 -1.2 -0.8 -1.6]")]
    [InlineData("[1+2i] \\ A", "double | 2 2 | [0.2 0.6 0.4 0.8] | [-0.4 -1.2 -0.8 -1.6]")]
    [InlineData("[1+2i] * [1+2i]", "double | 1 1 | -3 | 4")]
    [InlineData("(A + 1i) * c", "double | 2 2 | [2 6 4 8] | [2 2 2 2]")]
    [InlineData("c * (A + 1i)", "double | 2 2 | [2 6 4 8] | [2 2 2 2]")]
    public void AComplexScalarArrayScalesRatherThanMultiplies(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // Scaling reaches past two dimensions, where a matrix product could not follow.
    [InlineData("reshape(1:8, 2, 2, 2) * [2]", "[2 4 6 8 10 12 14 16] | 2 2 2")]
    [InlineData("[2] * reshape(1:8, 2, 2, 2)", "[2 4 6 8 10 12 14 16] | 2 2 2")]
    [InlineData("reshape(1:8, 2, 2, 2) / [2]", "[0.5 1 1.5 2 2.5 3 3.5 4] | 2 2 2")]
    [InlineData("[2] \\ reshape(1:8, 2, 2, 2)", "[0.5 1 1.5 2 2.5 3 3.5 4] | 2 2 2")]
    public void APageStackScalesByAOneByOneArray(string expression, string expected) => Assert.Equal(
        expected,
        Run($"v = {expression};\nd = size(v);\n" +
            "fprintf('%s | %d %d %d', mat2str(transpose(v(:))), d(1), d(2), d(3));"));

    [Theory]

    // An empty keeps its own shape through a scaling, which is not the product's shape rule.
    [InlineData("zeros(0, 3) * [2]", "0 3")]
    [InlineData("[2] * zeros(0, 3)", "0 3")]
    [InlineData("zeros(0, 3) / [2]", "0 3")]
    [InlineData("[2] \\ zeros(0, 3)", "0 3")]
    [InlineData("zeros(2, 0) * [2]", "2 0")]
    public void AnEmptyKeepsItsShapeThroughAScaling(string expression, string expected) =>
        Assert.Equal(expected, Run($"v = {expression};\nfprintf('%d %d', size(v, 1), size(v, 2));"));

    [Theory]

    // The shapes MATLAB still refuses. A 1-by-1 on the wrong side of a division is a system to
    // solve, and its dimensions have to agree like anyone else's.
    [InlineData("A = [1 2; 3 4]; c = [2]; x = c / A;")]
    [InlineData("v = [1 2 3]; c = [2]; x = c / v;")]
    [InlineData("A = [1 2; 3 4]; c = [2]; x = A \\ c;")]
    [InlineData("A = [1 2; 3 4]; v = [1 2 3]; x = A * v;")]
    [InlineData("A = [1 2; 3 4]; v = [1 2 3]; x = A ^ v;")]
    [InlineData("z = zeros(2, 3); c = [2]; x = z ^ c;")]
    public void AWrongShapeIsStillRefused(string code) => Assert.NotEmpty(Failure(code));
}
