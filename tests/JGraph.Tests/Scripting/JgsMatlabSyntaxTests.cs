using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M21b: MATLAB-style syntax — <c>~</c>/<c>~=</c> operator aliases, semicolon separation and echo
/// suppression, colon ranges, 1-based paren indexing with <c>end</c>, MATLAB block syntax, and the
/// echo/<c>ans</c>/auto-call console behaviors.
/// </summary>
[Collection("JG facade")]
public class JgsMatlabSyntaxTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsMatlabSyntaxTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    // --- MATLAB operator aliases ----------------------------------------------------------------

    [Fact]
    public async Task TildeOperators_AliasNotAndNotEqual()
    {
        ScriptRunResult result = await Run("""
            print(~false)
            print(1 ~= 2)
            print(2 ~= 2)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "true\n", "true\n", "false\n" }, _output.Normal);
    }

    [Fact]
    public async Task ElementwiseOperatorSpellings_AliasTheBroadcastingOperators()
    {
        ScriptRunResult result = await Run("""
            let a = [1, 2, 3];
            print(a .* a)
            print(a ./ [2, 2, 2])
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "[1, 4, 9]\n", "[0.5, 1, 1.5]\n" }, _output.Normal);
    }

    // --- Semicolons as separators ---------------------------------------------------------------

    [Fact]
    public async Task Semicolons_StillSeparateStatements_IncludingTrailing()
    {
        ScriptRunResult result = await Run("let x = 1; let y = 2; print(x + y);");

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "3\n" }, _output.Normal);
    }

    // --- Echo, ans, and auto-call ---------------------------------------------------------------

    [Fact]
    public async Task Echo_UnsuppressedStatements_PrintNameEqualsValue()
    {
        ScriptRunResult result = await Run("""
            let a = 5
            let b = 6;
            a = 7
            a + b;
            a + b
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "a = 5\n", "a = 7\n", "ans = 13\n" }, _output.Normal);
    }

    [Fact]
    public async Task Echo_ElementWrite_RedisplaysTheWholeVariable()
    {
        ScriptRunResult result = await Run("""
            let x = [1, 2, 3];
            x(2) = 9
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "x = [1, 9, 3]\n" }, _output.Normal);
    }

    [Fact]
    public async Task Ans_IsAssigned_EvenWhenSuppressed_AndChains()
    {
        ScriptRunResult result = await Run("""
            3 + 4;
            print(ans * 2)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "14\n" }, _output.Normal);
    }

    [Fact]
    public async Task Echo_NullResults_StaySilent()
    {
        // Verbs like title(...) return nothing — no echo, no ans, even without a semicolon.
        ScriptRunResult result = await Run("""
            plot([1, 2], [3, 4]);
            title("quiet")
            """);

        Assert.True(result.Success, result.Message);
        Assert.Empty(_output.Normal);
    }

    [Fact]
    public async Task BareBuiltinName_AutoCallsWithNoArguments()
    {
        // MATLAB command form: 'figure;' creates a figure.
        ScriptRunResult result = await Run("""
            figure;
            plot([1, 2], [3, 4]);
            show;
            """);

        Assert.True(result.Success, result.Message);
        Assert.Single(_figures);
    }

    [Fact]
    public async Task BareVariableName_EchoesItsValue()
    {
        ScriptRunResult result = await Run("""
            let speed = 88;
            speed
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "speed = 88\n" }, _output.Normal);
    }

    [Fact]
    public async Task Echo_LongArrays_AreTruncatedWithACount()
    {
        ScriptRunResult result = await Run("let big = zeros(10000)");

        Assert.True(result.Success, result.Message);
        string line = Assert.Single(_output.Normal);
        Assert.Contains("(10000 elements)", line);
        Assert.True(line.Length < 200, $"echo line is {line.Length} chars");
    }

    // --- Colon ranges ---------------------------------------------------------------------------

    [Fact]
    public async Task ColonRange_TwoPart_IsInclusiveStepOne()
    {
        ScriptRunResult result = await Run("print(2:6)");

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "[2, 3, 4, 5, 6]\n" }, _output.Normal);
    }

    [Fact]
    public async Task ColonRange_FractionalStep_HitsTheMatlabCount()
    {
        // The demo's t = 0:1/fs:3 at fs = 1000 must give exactly 3001 samples.
        ScriptRunResult result = await Run("""
            let fs = 1000;
            let t = 0:1/fs:3;
            print(length(t))
            print(t(end))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("3001", _output.Normal[0].Trim());
        Assert.Equal("3", _output.Normal[1].Trim());
    }

    [Fact]
    public async Task ColonRange_NegativeStep_CountsDown()
    {
        ScriptRunResult result = await Run("print(5:-2:0)");

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "[5, 3, 1]\n" }, _output.Normal);
    }

    [Fact]
    public async Task ColonRange_EmptyWhenDirectionIsWrong()
    {
        ScriptRunResult result = await Run("print(length(5:1))");

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "0\n" }, _output.Normal);
    }

    // --- 1-based paren indexing with end and slices ---------------------------------------------

    [Fact]
    public async Task ParenIndex_End_SelectsTheLastElement()
    {
        ScriptRunResult result = await Run("""
            let a = [10, 20, 30, 40];
            print(a(end))
            print(a(end - 1))
            print(a(2:end))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "40\n", "30\n", "[20, 30, 40]\n" }, _output.Normal);
    }

    [Fact]
    public async Task ParenIndex_Colon_SelectsEverything()
    {
        ScriptRunResult result = await Run("""
            let a = [1, 2, 3];
            print(a(:))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "[1, 2, 3]\n" }, _output.Normal);
    }

    [Fact]
    public async Task SliceWrite_RangeTarget_BroadcastsAScalar()
    {
        // The sound demo's X_comp75(1 : N/8) = 0 pattern.
        ScriptRunResult result = await Run("""
            let x = [1, 2, 3, 4, 5, 6, 7, 8];
            x(1:2) = 0;
            x(end - 1 : end) = 0;
            print(x)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "[0, 0, 3, 4, 5, 6, 0, 0]\n" }, _output.Normal);
    }

    [Fact]
    public async Task SliceWrite_ArrayRhs_MustMatchTheSelectionLength()
    {
        ScriptRunResult result = await Run("""
            let x = [1, 2, 3, 4];
            x(2:3) = [9, 8];
            print(x)
            x(2:3) = [1, 2, 3]
            """);

        Assert.False(result.Success);
        Assert.Equal(new[] { "[1, 9, 8, 4]\n" }, _output.Normal);
        Assert.Contains("3 values into 2 selected", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task ElementWrite_ParenForm_IsOneBased_AndSingleEval()
    {
        ScriptRunResult result = await Run("""
            let calls = 0;
            fn pick() { calls += 1; return 2 }
            let theta = [0, 0, 0];
            theta(pick()) += 5;
            print(theta)
            print(calls)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "[0, 5, 0]\n", "1\n" }, _output.Normal);
    }

    [Fact]
    public async Task End_OutsideAnIndex_IsARuntimeError()
    {
        ScriptRunResult result = await Run("let x = end + 1");

        Assert.False(result.Success);
        Assert.Contains("only valid inside an index", Assert.Single(result.Diagnostics).Message);
    }

    // --- MATLAB block syntax --------------------------------------------------------------------

    [Fact]
    public async Task ForEqualsRange_WithEnd_RunsTheMatlabLoop()
    {
        // The demo's VCO accumulator: theta(k) = theta(k-1) + ...
        ScriptRunResult result = await Run("""
            let theta = zeros(5);
            for k = 2:5
                theta(k) = theta(k-1) + k;
            end
            print(theta)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "[0, 2, 5, 9, 14]\n" }, _output.Normal);
    }

    [Fact]
    public async Task IfElseifElse_SharesOneEnd()
    {
        ScriptRunResult result = await Run("""
            fn grade(score)
                if score >= 90
                    return "A"
                elseif score >= 80
                    return "B"
                else
                    return "C"
                end
            end
            print(grade(95), grade(85), grade(40))
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "A B C\n" }, _output.Normal);
    }

    [Fact]
    public async Task WhileEnd_AndBraceStyle_Coexist()
    {
        ScriptRunResult result = await Run("""
            let n = 0;
            while n < 3
                n += 1;
            end
            while n < 6 { n += 1; }
            print(n)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "6\n" }, _output.Normal);
    }

    [Fact]
    public async Task StrayEnd_IsASyntaxError()
    {
        ScriptRunResult result = await Run("let x = 1\nend");

        Assert.False(result.Success);
        Assert.Contains("Unexpected 'end'", Assert.Single(result.Diagnostics).Message);
    }

    // --- MATLAB operators and literals ----------------------------------------------------------

    [Fact]
    public async Task Power_BindsTighterThanUnaryMinus_AndAssociatesLeft()
    {
        ScriptRunResult result = await Run("""
            print(-2^2)
            print(2^-1)
            print(2^3^2)
            print([1, 2, 3].^2)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "-4\n", "0.5\n", "64\n", "[1, 4, 9]\n" }, _output.Normal);
    }

    [Fact]
    public async Task MatrixRows_ScalarRows_BuildAMatrix()
    {
        ScriptRunResult result = await Run("""
            let m = [1, 2; 3, 4];
            print(m[0])
            print(m[1])
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "[1, 2]\n", "[3, 4]\n" }, _output.Normal);
    }

    [Fact]
    public async Task MatrixRows_ArrayRows_VerticallyConcatenate()
    {
        // The sound demo's x_pad = [audio_sample; zeros(8 - rem8, 1)] pattern.
        ScriptRunResult result = await Run("""
            let a = [1, 2, 3];
            let x = [a; zeros(2, 1)];
            print(x)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "[1, 2, 3, 0, 0]\n" }, _output.Normal);
    }

    [Fact]
    public async Task MatrixRows_RaggedScalarRows_AreARuntimeError()
    {
        ScriptRunResult result = await Run("let m = [1, 2; 3]");

        Assert.False(result.Success);
        Assert.Contains("equal lengths", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task Constants_PiAndFriends_AreDefined()
    {
        ScriptRunResult result = await Run("""
            print(round(pi * 10000))
            print(round(e * 10000))
            print(1 / inf)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "31416\n", "27183\n", "0\n" }, _output.Normal);
    }
}
