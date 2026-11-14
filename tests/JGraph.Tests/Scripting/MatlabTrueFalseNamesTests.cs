using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 205 (ADR 0363): <c>true</c> and <c>false</c> are names in the MATLAB dialect, keywords in
/// JGS. What the parser marks a bare mention with, what a fresh session reads it as in each place a
/// binding can come from, and the compiled loop's checks. The parity fixture
/// <c>oi205_true_false_names</c> holds R2025b's answers for the same cases.
/// </summary>
[Collection("JG facade")]
public sealed class MatlabTrueFalseNamesTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-oi205-" + Guid.NewGuid().ToString("N"));

    public MatlabTrueFalseNamesTests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private static IReadOnlyList<Stmt> Matlab(string source) => Parser.Parse(source, "", JgsDialect.Matlab);

    private static BoolLiteral LiteralOf(Stmt statement) => statement switch
    {
        ExprStmt { Expression: AssignExpr { Value: BoolLiteral literal } } => literal,
        ExprStmt { Expression: BoolLiteral literal } => literal,
        _ => throw new InvalidOperationException("no literal in " + statement.GetType().Name),
    };

    private static FnStmt Function(IReadOnlyList<Stmt> program, int index = 0) => Assert.IsType<FnStmt>(program[index]);

    // --- the lexer and the parser ---------------------------------------------------------------

    [Fact]
    public void TheWordsAreNamesInMatlabAndKeywordsInJgs()
    {
        Assert.Equal(TokenType.Identifier, Lexer.Tokenize("true", dialect: JgsDialect.Matlab)[0].Type);
        Assert.Equal(TokenType.Identifier, Lexer.Tokenize("false", dialect: JgsDialect.Matlab)[0].Type);
        Assert.Equal(TokenType.True, Lexer.Tokenize("true")[0].Type);
        Assert.Equal(TokenType.False, Lexer.Tokenize("false")[0].Type);
        Assert.DoesNotContain("true", JgsBuiltinCatalog.MatlabKeywords);
        Assert.Contains("true", JgsBuiltinCatalog.Keywords);

        var jgs = (ExprStmt)Assert.Single(Parser.Parse("true"));
        Assert.Equal(LiteralShadow.None, Assert.IsType<BoolLiteral>(jgs.Expression).Shadow);
        Assert.Throws<JgsSyntaxException>(static () => Parser.Parse("true = 1"));
    }

    [Fact]
    public void AFunctionThatNeverBindsTheWordReadsNoVariable()
    {
        FnStmt f = Function(Matlab("function r = f(x)\nr = true;\nend\n"));
        Assert.Equal(LiteralShadow.File, LiteralOf(f.Body[0]).Shadow);
        Assert.Equal(0, f.LiteralBindings);
    }

    [Theory]
    [InlineData("function r = f(true)\nr = true;\nend\n")]
    [InlineData("function true = f()\ntrue = 3;\nr = true;\nend\n")]
    [InlineData("function r = f()\nr = true;\ntrue = 5;\nend\n")]
    [InlineData("function r = f()\nr = true;\n[a, true] = deal(1, 2);\nend\n")]
    [InlineData("function r = f()\nr = true;\ntrue(2) = 1;\nend\n")]
    [InlineData("function r = f()\nr = true;\nfor true = 1:3\nend\nend\n")]
    [InlineData("function r = f()\nr = true;\nglobal true\nend\n")]
    [InlineData("function r = f()\nr = true;\npersistent true\nend\n")]
    [InlineData("function r = f()\nr = true;\ng = @(true) true;\nend\n")]
    public void AFunctionWhoseTextBindsTheWordReadsTheVariable(string source)
    {
        FnStmt f = Function(Matlab(source));
        Assert.Equal(1, f.LiteralBindings);
        BoolLiteral literal = f.Body.Select(static s => s is ExprStmt { Expression: AssignExpr { Value: BoolLiteral b } } ? b : null)
            .First(static b => b is not null)!;
        Assert.Equal(LiteralShadow.File | LiteralShadow.Variable, literal.Shadow);
    }

    [Fact]
    public void AnAssignedWordIsAVariableTarget()
    {
        var statement = (ExprStmt)Assert.Single(Matlab("true = 1"));
        Assert.Equal("true", Assert.IsType<VariableExpr>(Assert.IsType<AssignExpr>(statement.Expression).Target).Name);

        var multi = Assert.IsType<MultiAssignStmt>(Assert.Single(Matlab("[a, false] = deal(1, 2)")));
        Assert.Equal("false", Assert.IsType<VariableExpr>(multi.Targets[1]).Name);
    }

    [Fact]
    public void OutsideEveryFunctionAVariableMayAnswer()
    {
        Assert.Equal(LiteralShadow.File | LiteralShadow.Variable, LiteralOf(Assert.Single(Matlab("x = false"))).Shadow);
    }

    [Fact]
    public void ANestedFunctionSharesItsParentsBindingWhereverItIsWritten()
    {
        // The parent binds true after the nested function's text: the nested read is still the variable.
        FnStmt parent = Function(Matlab("function r = f()\nr = inner();\n    function v = inner()\n        v = true;\n    end\ntrue = 4;\nend\n"));
        FnStmt inner = parent.Body.OfType<FnStmt>().Single();
        Assert.Equal(1, inner.LiteralBindings);
        Assert.Equal(LiteralShadow.File | LiteralShadow.Variable, LiteralOf(inner.Body[0]).Shadow);
    }

    [Fact]
    public void AFunctionNamedAfterTheWordAnswersItInItsFile()
    {
        IReadOnlyList<Stmt> program = Matlab("function r = f()\nr = true;\nend\n\nfunction v = true()\nv = 42;\nend\n");
        Assert.True((LiteralOf(Function(program).Body[0]).Shadow & LiteralShadow.Function) != 0);
    }

    [Fact]
    public void AClassAttributeStillReadsTheWord()
    {
        var classdef = Assert.IsType<ClassdefStmt>(Assert.Single(Matlab(
            "classdef C\n    properties (Constant = true, Hidden = false)\n        p = 1\n    end\nend\n")));
        Assert.NotNull(classdef);
    }

    // --- a fresh session --------------------------------------------------------------------------

    private async Task<ScriptRunResult> RunFile(string name, string code)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, code);
        var session = Assert.IsType<JgsReplSession>(Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
            .CreateSession(new ScriptContext(_output, static (_, _) => { }, _folder)));
        await using (session)
        {
            return await session.ExecuteFileAsync(code, path, CancellationToken.None);
        }
    }

    private static object? Value(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue;

    [Fact]
    public async Task ALocalFunctionNamedTrueAnswersTheBareWordInAFreshSession()
    {
        ScriptRunResult result = await RunFile("s205local.m", "a = true;\nb = true(2);\n\nfunction v = true(varargin)\nv = 42 + nargin;\nend\n");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(42.0, Value(result, "a"));
        Assert.Equal(43.0, Value(result, "b"));
    }

    [Fact]
    public async Task AScriptReadsWhatItsWorkspaceHolds()
    {
        ScriptRunResult result = await RunFile("s205bind.m",
            "a = true;\ntrue = 3;\nb = true;\nclear true\nc = true;\nfalse = 0;\nd = ~false;\nclear false\n");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(true, Value(result, "a"));
        Assert.Equal(3.0, Value(result, "b"));
        Assert.Equal(true, Value(result, "c"));
        Assert.Equal(true, Value(result, "d"));
    }

    [Fact]
    public async Task AFileNamedTrueTakesTheBareWord()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "shadow"));
        File.WriteAllText(Path.Combine(_folder, "shadow", "true.m"), "function v = true(varargin)\nv = 100 + nargin;\nend\n");
        ScriptRunResult result = await RunFile("s205file.m",
            "before = true;\nold = cd('shadow');\nbare = true;\ncalled = true(2);\ncd(old);\nafter = true;\n");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(true, Value(result, "before"));
        Assert.Equal(100.0, Value(result, "bare"));
        Assert.Equal(101.0, Value(result, "called"));
        Assert.Equal(true, Value(result, "after"));
    }

    // --- the compiled loop --------------------------------------------------------------------------

    private static (string[] Output, bool Success, string? Message, long CompiledRuns) RunWith(bool jit, string code)
    {
        bool previous = JgsLoopJit.Enabled;
        JgsLoopJit.Enabled = jit;
        long before = JgsLoopJit.CompiledRuns;
        try
        {
            JG.Reset();
            var output = new RecordingScriptOutput();
            var context = new ScriptContext(output, static (_, _) => { }, null);
            ScriptRunResult result = JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
            return (output.Normal.ToArray(), result.Success, result.Message, JgsLoopJit.CompiledRuns - before);
        }
        finally
        {
            JgsLoopJit.Enabled = previous;
        }
    }

    private static string[] AssertParity(string code, bool? expectCompiled)
    {
        (string[] jitOut, bool jitOk, string? jitMessage, long compiled) = RunWith(jit: true, code);
        (string[] walkOut, bool walkOk, string? walkMessage, _) = RunWith(jit: false, code);
        Assert.True(jitOk, jitMessage);
        Assert.True(walkOk, walkMessage);
        Assert.Equal(walkOut, jitOut);
        if (expectCompiled is { } expected)
        {
            Assert.Equal(expected, compiled > 0);
        }

        return jitOut;
    }

    [Fact]
    public void ALoopOverTheBuiltinsCompiles()
    {
        string[] output = AssertParity("s = 0;\nfor k = 1:5\n    s = s + true - false;\nend\nfprintf('%g\\n', s);\n", expectCompiled: true);
        Assert.Equal("5", Assert.Single(output).Trim());
    }

    [Fact]
    public void ALoopEnteredWithTheWordBoundWalks()
    {
        string[] output = AssertParity("true = 2;\ns = 0;\nfor k = 1:3\n    s = s + true;\nend\nfprintf('%g\\n', s);\n", expectCompiled: false);
        Assert.Equal("6", Assert.Single(output).Trim());
    }

    [Fact]
    public void AWalkedStatementThatBindsTheWordLeavesTheCompiledLoop()
    {
        // R2025b: 1 + 1 + 10 + 10 (oi205_true_false_names, loop_eval_binds).
        string[] output = AssertParity(
            "s = 0;\nfor k = 1:4\n    s = s + true;\n    if k == 2, eval('true = 10;'); end\nend\nfprintf('%g\\n', s);\n",
            expectCompiled: null);
        Assert.Equal("22", Assert.Single(output).Trim());
    }
}
