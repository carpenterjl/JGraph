using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The matrix divisions over a rectangular system (M140): MATLAB's <c>\</c> answers the
/// <em>basic</em> solution, with a nought in every column its pivoting left out.
/// </summary>
/// <remarks>
/// A rectangular system rarely has one answer. <c>[1 2 3] \ 2</c> has a whole plane of them, and the
/// two conventional ways to pick one disagree: the minimum-norm solution is <c>[1; 2; 3]/7</c> and
/// the basic solution is <c>[0; 0; 2/3]</c>. JGraph answered the first and MATLAB answers the
/// second, so every expectation below is an R2025b answer, generated rather than typed. They are
/// rounded to twelve decimal places on purpose — a pivoted factorization leaves a few ulps of
/// residue in the entries the pivoting did not zero, and the two linear-algebra lanes reach it
/// through different kernels, so exact digits here would assert roundoff rather than the answer.
/// </remarks>
[Collection("JG facade")]
public class MatlabBasicSolutionM140Tests : IDisposable
{
    private RecordingScriptOutput _output = new();

    public MatlabBasicSolutionM140Tests() => JG.Reset();

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
    /// The class, shape and elements of an expression, printed column-major and rounded. The real
    /// and imaginary parts go separately because mat2str prints a <c>+0i</c> where MATLAB prints
    /// none (M137). The <c>+ 0</c> is not idle: rounding a residue of a few ulps down to nought
    /// keeps its sign, and mat2str prints that sign, so without it the expectation would turn on
    /// which side of nought a discarded ulp fell.
    /// </summary>
    private string Shape(string expression) => Run(
        Setup + $"\nv = {expression};\n" +
        "fprintf('%s | %d %d | %s | %s', class(v), size(v, 1), size(v, 2), " +
        "mat2str(round(real(transpose(v(:))), 12) + 0), mat2str(round(imag(transpose(v(:))), 12) + 0));");

    /// <summary>
    /// The warnings a run raised. MATLAB writes a warning to the error stream rather than the
    /// display, and JGraph's <c>warning</c> does the same, so this is not the same text
    /// <see cref="Run"/> returns.
    /// </summary>
    private string Warnings(string code)
    {
        Run(code);
        return _output.ErrorText;
    }

    private const string Setup =
        "W = [1 2 3];\n" +
        "M = [1 2 3; 4 5 6];\n" +
        "D = [1 2 3; 2 4 6];\n" +
        "T = [1 2; 3 4; 5 6];\n" +
        "S = [1 2; 2 4; 3 6];\n" +
        "A = [1 2; 3 4];";

    [Theory]

    // The divergence itself: the answer goes on the column the pivoting ranked first, and nowhere
    // else. Which column that is follows the column lengths, and ties go to the earlier column.
    [InlineData("[1 2 3] \\ [2]", "double | 3 1 | [0 0 0.666666666667] | [0 0 0]")]
    [InlineData("[3 1 2] \\ [6]", "double | 3 1 | [2 0 0] | [0 0 0]")]
    [InlineData("[0 0 3] \\ [6]", "double | 3 1 | [0 0 2] | [0 0 0]")]
    [InlineData("[4 5 6] \\ [1]", "double | 3 1 | [0 0 0.166666666667] | [0 0 0]")]
    [InlineData("[1 -2 3] \\ [1]", "double | 3 1 | [0 0 0.333333333333] | [0 0 0]")]
    [InlineData("[1 2 3 4] \\ [1]", "double | 4 1 | [0 0 0 0.25] | [0 0 0 0]")]
    [InlineData("[3 3 3] \\ 6", "double | 3 1 | [2 0 0] | [0 0 0]")]
    [InlineData("[1 3 3] \\ 6", "double | 3 1 | [0 2 0] | [0 0 0]")]
    [InlineData("[3 3 1] \\ 6", "double | 3 1 | [2 0 0] | [0 0 0]")]
    [InlineData("[-5 1 2] \\ 6", "double | 3 1 | [-1.2 0 0] | [0 0 0]")]
    [InlineData("[1e8 1 1] \\ [1]", "double | 3 1 | [1e-08 0 0] | [0 0 0]")]
    [InlineData("[1 1 1e8] \\ [1]", "double | 3 1 | [0 0 1e-08] | [0 0 0]")]

    // A wider system, several right-hand sides, and a scaled row that changes nothing.
    [InlineData("W \\ [2 5]", "double | 3 2 | [0 0 0.666666666667 0 0 1.666666666667] | [0 0 0 0 0 0]")]
    [InlineData("M \\ [1; 2]", "double | 3 1 | [0 0 0.333333333333] | [0 0 0]")]
    [InlineData("M \\ [1 7; 2 8]", "double | 3 2 | [0 0 0.333333333333 -3 0 3.333333333333] | [0 0 0 0 0 0]")]
    [InlineData("M \\ eye(2)", "double | 3 2 | [-1 0 0.666666666667 0.5 0 -0.166666666667] | [0 0 0 0 0 0]")]
    [InlineData("ones(1, 3) \\ ones(1, 4)", "double | 3 4 | [1 0 0 1 0 0 1 0 0 1 0 0] | [0 0 0 0 0 0 0 0 0 0 0 0]")]
    [InlineData("[2 4 6; 1 2 3] \\ [4; 2]", "double | 3 1 | [0 0 0.666666666667] | [0 0 0]")]
    public void AnUnderdeterminedDivisionTakesTheBasicSolution(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // A bare number is the one-by-one it would be with brackets round it, and a one-row matrix has
    // room for exactly that. This was refused outright before M140.
    [InlineData("[1 2 3] \\ 2", "double | 3 1 | [0 0 0.666666666667] | [0 0 0]")]
    [InlineData("W \\ true", "double | 3 1 | [0 0 0.333333333333] | [0 0 0]")]
    [InlineData("W / 2", "double | 1 3 | [0.5 1 1.5] | [0 0 0]")]
    public void ABareNumberIsTheOneByOneItWouldBeWithBrackets(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // A factorization that runs out of independent columns still has an answer to give, and MATLAB
    // gives it. Every one of these was refused or answered with roundoff before M140.
    [InlineData("D \\ [1; 2]", "double | 3 1 | [0 0 0.333333333333] | [0 0 0]")]
    [InlineData("zeros(1, 3) \\ [1]", "double | 3 1 | [0 0 0] | [0 0 0]")]
    [InlineData("S \\ [1; 2; 3]", "double | 2 1 | [0 0.5] | [0 0]")]
    public void ARankDeficientSystemAnswersRatherThanRefusing(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // MATLAB's own warning text, down to the width it prints the tolerance at, because a script may
    // be reading it. The tolerance is max(m, n) * eps * the largest column length.
    [InlineData("D \\ [1; 2]", "Rank deficient, rank = 1, tol =  4.468561e-15.")]
    [InlineData("zeros(1, 3) \\ [1]", "Rank deficient, rank = 0, tol =  0.000000e+00.")]
    [InlineData("S \\ [1; 2; 3]", "Rank deficient, rank = 1, tol =  4.984889e-15.")]
    public void ARankDeficientSystemSaysSo(string expression, string expected) =>
        Assert.Contains(expected, Warnings(Setup + $"\nv = {expression};"));

    [Theory]

    // A full-rank system says nothing at all, whichever way round its shape is.
    [InlineData("W \\ [2]")]
    [InlineData("M \\ [1; 2]")]
    [InlineData("T \\ [1; 2; 3]")]
    [InlineData("A \\ [1; 2]")]
    public void AFullRankSystemIsSilent(string expression) =>
        Assert.DoesNotContain("Rank deficient", Warnings(Setup + $"\nv = {expression};"));

    [Theory]

    // The other half of M138's claim, which it left standing: a bare number is a one-by-one on the
    // left of a division too. `2 / [1; 2]` is a system with exactly one answer, the 1-by-2 [0 1],
    // and reading it elementwise gave a 2-by-1 of something else entirely.
    [InlineData("2 / [1; 2]", "double | 1 2 | [0 1] | [0 0]")]
    [InlineData("2 / [5]", "double | 1 1 | 0.4 | 0")]
    [InlineData("2 ./ W", "double | 1 3 | [2 1 0.666666666667] | [0 0 0]")]

    // A single operand carries its class through, as it does everywhere else.
    [InlineData("single(W) \\ single(2)", "single | 3 1 | [0 0 0.666666686534882] | [0 0 0]")]
    [InlineData("single(W) \\ 2", "single | 3 1 | [0 0 0.666666686534882] | [0 0 0]")]
    [InlineData("W \\ single(2)", "single | 3 1 | [0 0 0.666666686534882] | [0 0 0]")]
    public void ABareNumberIsAOneByOneOnEitherSide(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // A complex rectangular system used to be refused outright — "the complex least-squares solve
    // is not supported" — and takes the same basic solution the real one does.
    [InlineData("[1i 2 3] \\ [2]", "double | 3 1 | [0 0 0.666666666667] | [0 0 0]")]
    [InlineData("W \\ [2i]", "double | 3 1 | [0 0 0] | [0 0 0.666666666667]")]
    [InlineData("[1+1i 2 3; 4 5 6] \\ [1; 2]", "double | 3 1 | [0 0 0.333333333333] | [0 0 0]")]
    [InlineData("T \\ [1+1i; 2; 3]", "double | 2 1 | [0 0.5] | [-1.333333333333 1.083333333333]")]
    [InlineData("[1+1i 2 3; 2+2i 4 6] \\ [1; 2]", "double | 3 1 | [0 0 0.333333333333] | [0 0 0]")]
    public void AComplexRectangularSystemIsSolvedRatherThanRefused(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // The right division is the same problem transposed, so it takes the same road and needs no
    // rule of its own.
    [InlineData("W / W", "double | 1 1 | 1 | 0")]
    [InlineData("W / M", "double | 1 2 | [1 0] | [0 0]")]
    [InlineData("W / [4 5 6]", "double | 1 1 | 0.415584415584 | 0")]
    public void ARightDivisionTakesTheSameRoad(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // An over-determined system has one least-squares answer, so the pivoting cannot change it —
    // and a square one still goes to the LU factorization it always went to.
    [InlineData("T \\ [1; 2; 3]", "double | 2 1 | [0 0.5] | [0 0]")]
    [InlineData("[1 2; 3 4; 5 7] \\ [1; 2; 3]", "double | 2 1 | [-0.071428571429 0.5] | [0 0]")]
    [InlineData("A \\ [1; 2]", "double | 2 1 | [0 0.5] | [0 0]")]
    [InlineData("A \\ A", "double | 2 2 | [1 0 0 1] | [0 0 0 0]")]
    public void AnOverdeterminedSystemKeepsItsLeastSquaresAnswer(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // An empty operand still has a shape, and the solution's is the matrix's column count by the
    // right-hand side's width.
    [InlineData("zeros(0, 3) \\ zeros(0, 1)", "double | 3 1 | [0 0 0] | [0 0 0]")]
    [InlineData("W \\ zeros(1, 0)", "double | 3 0 | zeros(1,0) | zeros(1,0)")]
    [InlineData("M \\ zeros(2, 0)", "double | 3 0 | zeros(1,0) | zeros(1,0)")]
    public void AnEmptyKeepsItsShape(string expression, string expected) =>
        Assert.Equal(expected, Shape(expression));

    [Theory]

    // The shapes MATLAB refuses are still refused, including the three that look like the ones the
    // widening now allows: a right-hand side has to have as many rows as the matrix, whether it was
    // written with brackets or without.
    [InlineData("A \\ 2")]
    [InlineData("[1; 2] \\ 2")]
    [InlineData("[1 2] / M")]
    [InlineData("W \\ [2; 5]")]

    // A bare number over a matrix asks for an X that does not exist, and is refused rather than
    // read elementwise — the shapes here are the ones `[2] / A` was already refused for.
    [InlineData("2 / W")]
    [InlineData("2 / A")]
    [InlineData("2 / M")]
    [InlineData("2 / eye(2)")]
    [InlineData("2 / zeros(1, 3)")]
    public void AWrongShapeIsStillRefused(string expression) =>
        Assert.NotEmpty(Failure(Setup + $"\nv = {expression};"));
}
