using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

[Collection("JG facade")]
public class JgsScriptEngineTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsScriptEngineTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptContext Context(string? workingDirectory = null) => new(_output, (_, figure) => _figures.Add(figure), workingDirectory);

    private Task<ScriptRunResult> Run(string code, CancellationToken cancellationToken = default) =>
        _engine.RunAsync(code, Context(), cancellationToken);

    [Fact]
    public void Engine_IsAlwaysAvailable_AndReportsJgsLanguage()
    {
        Assert.True(_engine.IsAvailable);
        Assert.Equal("JGS", _engine.Language);
    }

    [Fact]
    public async Task RunAsync_BuildsFigureThroughApi_AndDisplaysIt()
    {
        const string code = """
            let x = [0, 1, 2]
            let y = [0, 1, 4]
            plot(x, y, "r-")
            title("From JGS")
            legend("series")
            show()
            """;

        ScriptRunResult result = await Run(code);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.FiguresShown);
        FigureModel figure = Assert.Single(_figures);
        Assert.Single(figure.Axes[0].Plots);
        Assert.Equal("From JGS", figure.Axes[0].Title);
    }

    [Fact]
    public async Task RunAsync_SingleQuotedStrings_AreInterchangeableWithDoubleQuoted()
    {
        // M17: MATLAB-style 'strings' — same value, same escapes, mixable on one line.
        const string code = """
            let a = 'single'
            let b = "single"
            print(a == b)
            print('it\'s', "a \"quote\"", 'tab\there')
            print('x' + "y")
            """;

        ScriptRunResult result = await Run(code);

        Assert.True(result.Success, result.Message);
        Assert.Contains("true", _output.NormalText);
        Assert.Contains("it's a \"quote\" tab\there", _output.NormalText);
        Assert.Contains("xy", _output.NormalText);
    }

    [Fact]
    public async Task RunAsync_UnterminatedSingleQuotedString_FailsWithSyntaxDiagnostic()
    {
        ScriptRunResult result = await Run("let a = 'oops");

        Assert.False(result.Success);
        ScriptDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("Unterminated string", diagnostic.Message);
    }

    [Fact]
    public async Task RunAsync_HoldKeepsPreviousSeries_ForMultiSeriesFigures()
    {
        const string code = """
            let x = linspace(0, 1, 10)
            plot(x, sin(x), "b-")
            hold(true)
            plot(x, cos(x), "r--")
            legend("sine", "cosine")
            show()
            """;

        ScriptRunResult result = await Run(code);

        Assert.True(result.Success, result.Message);
        FigureModel figure = Assert.Single(_figures);
        Assert.Equal(2, figure.Axes[0].Plots.Count);
    }

    [Fact]
    public async Task RunAsync_EmptyOrCommentOnlyScript_Succeeds()
    {
        ScriptRunResult result = await Run("   \n # just a comment\n // and another\n");

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.FiguresShown);
        Assert.Empty(_figures);
    }

    [Fact]
    public async Task RunAsync_VectorizedMath_IsElementWise()
    {
        // linspace(0,1,5)[4] == 1; times 2 == 2.
        const string code = """
            let x = linspace(0, 1, 5)
            let y = x * 2
            print(y[4])
            print(sum([1, 2, 3] * 2))
            """;

        ScriptRunResult result = await Run(code);

        Assert.True(result.Success, result.Message);
        Assert.Contains("2", _output.NormalText);
        Assert.Contains("12", _output.NormalText);
    }

    [Fact]
    public async Task RunAsync_ForLoopAndRange_AccumulateAcrossScopes()
    {
        const string code = """
            let total = 0
            for i in range(1, 5) {
                total = total + i
            }
            print(total)
            """;

        ScriptRunResult result = await Run(code);

        Assert.True(result.Success, result.Message);
        Assert.Contains("10", _output.NormalText); // 1 + 2 + 3 + 4
    }

    [Fact]
    public async Task RunAsync_WhileLoop_WithBreakAndContinue()
    {
        const string code = """
            let i = 0
            let total = 0
            while true {
                i = i + 1
                if i > 10 {
                    break
                }
                if i % 2 == 0 {
                    continue
                }
                total = total + i
            }
            print(total)
            """;

        ScriptRunResult result = await Run(code);

        Assert.True(result.Success, result.Message);
        Assert.Contains("25", _output.NormalText); // 1 + 3 + 5 + 7 + 9
    }

    [Fact]
    public async Task RunAsync_RecursiveFunction_Works()
    {
        const string code = """
            fn fact(n) {
                if n <= 1 {
                    return 1
                }
                return n * fact(n - 1)
            }
            print(fact(5))
            """;

        ScriptRunResult result = await Run(code);

        Assert.True(result.Success, result.Message);
        Assert.Contains("120", _output.NormalText);
    }

    [Fact]
    public async Task RunAsync_Closures_CaptureEnclosingVariables()
    {
        const string code = """
            fn adder(n) {
                fn add(x) {
                    return x + n
                }
                return add
            }
            let add5 = adder(5)
            print(add5(10))
            """;

        ScriptRunResult result = await Run(code);

        Assert.True(result.Success, result.Message);
        Assert.Contains("15", _output.NormalText);
    }

    [Fact]
    public async Task RunAsync_ArrayIndexAssignment_MutatesElement()
    {
        const string code = """
            let a = zeros(3)
            a[1] = 42
            print(a[1])
            print(length(a))
            """;

        ScriptRunResult result = await Run(code);

        Assert.True(result.Success, result.Message);
        Assert.Contains("42", _output.NormalText);
        Assert.Contains("3", _output.NormalText);
    }

    [Fact]
    public async Task RunAsync_LogicalAndComparisonOperators()
    {
        const string code = """
            if 3 > 2 && 1 < 2 {
                print("yes")
            } else {
                print("no")
            }
            """;

        ScriptRunResult result = await Run(code);

        Assert.True(result.Success, result.Message);
        Assert.Contains("yes", _output.NormalText);
        Assert.DoesNotContain("no", _output.NormalText);
    }

    [Fact]
    public async Task RunAsync_StringConcatenation()
    {
        ScriptRunResult result = await Run("""print("n=" + 5)""");

        Assert.True(result.Success, result.Message);
        Assert.Contains("n=5", _output.NormalText);
    }

    [Fact]
    public async Task RunAsync_OnSyntaxError_ReportsDiagnosticWithLocation()
    {
        // Missing closing parenthesis.
        ScriptRunResult result = await Run("""plot([1, 2], [3, 4]""");

        Assert.False(result.Success);
        ScriptDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.True(diagnostic.IsError);
        Assert.Equal(1, diagnostic.Line);
    }

    [Fact]
    public async Task RunAsync_OnUndefinedVariable_ReportsRuntimeErrorWithLocation()
    {
        const string code = """
            let a = 1
            print(missing)
            """;

        ScriptRunResult result = await Run(code);

        Assert.False(result.Success);
        ScriptDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(2, diagnostic.Line); // 'missing' is on line 2
        Assert.Contains("missing", diagnostic.Message);
    }

    [Fact]
    public async Task RunAsync_OnTypeError_FailsGracefully()
    {
        ScriptRunResult result = await Run("""let z = sqrt("hello")""");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains("sqrt", _output.ErrorText);
    }

    [Fact]
    public async Task RunAsync_IndexOutOfRange_FailsGracefully()
    {
        ScriptRunResult result = await Run("""
            let a = [1, 2, 3]
            print(a[5])
            """);

        Assert.False(result.Success);
        Assert.Contains("out of range", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task RunAsync_ReadsCsvRelativeToWorkingDirectory_AndPlotsIt()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"jgraph_jgs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "data.csv"), "x,y\n1,10\n2,20\n3,30");
            const string code = """
                let t = readcsv("data.csv")
                plot(t, "x", "y", "b-")
                let ys = column(t, "y")
                print(sum(ys))
                show()
                """;

            ScriptRunResult result = await _engine.RunAsync(code, Context(dir), CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.FiguresShown);
            Assert.Single(_figures[0].Axes[0].Plots);
            Assert.Contains("60", _output.NormalText); // 10 + 20 + 30
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_HonorsCancellation_EvenInsideATightLoop()
    {
        using var cts = new CancellationTokenSource();
        Task<ScriptRunResult> task = Run("while true { }", cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        ScriptRunResult result = await task;

        Assert.False(result.Success);
    }

    [Fact]
    public async Task RunAsync_WithAlreadyCancelledToken_DoesNotRun()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Run("let a = 1", cts.Token));
    }
}
