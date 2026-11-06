using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The packages and class folders plan, stage P4 (ADR 0310): a class defined by an <c>@Class</c>
/// folder. The parity fixture <c>p4_classfolders</c> pins every measured rule against R2025b (167
/// rows); these tests pin what the fixture's one process cannot see or R2025b does not do — a
/// method file edited after the class was used, a declared signature with no file behind it, a
/// method file that does not parse beside ones that do, and the parser's reading of a command
/// word with parentheses in it.
/// </summary>
[Collection("JG facade")]
public class ClassFolderTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-p4-classfolders-" + Guid.NewGuid().ToString("N"));

    private readonly string _classFolder;

    public ClassFolderTests()
    {
        JG.Reset();
        _classFolder = Path.Combine(_folder, "@P4T");
        Directory.CreateDirectory(_classFolder);
        File.WriteAllText(Path.Combine(_classFolder, "P4T.m"),
            "classdef P4T\n    properties\n        W = 0\n    end\n    methods\n        function o = P4T(w)\n            o.W = w;\n        end\n        r = declared(o)\n        r = nofile(o)\n    end\nend\n");
        File.WriteAllText(Path.Combine(_classFolder, "declared.m"), "function r = declared(o)\nr = o.W * 10;\nend\n");
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private ScriptContext Context() => new(_output, static (_, _) => { }, _folder);

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(code, Context(), default);

    private static object? Value(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue;

    private void WriteMethod(string stem, string text, int secondsLater)
    {
        string path = Path.Combine(_classFolder, stem + ".m");
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(secondsLater));
    }

    /// <summary>
    /// A method file written after the class was used is read at the class's next mention, as a
    /// changed class file is (ADR 0303): the folder is one class, and a changed file in it is a
    /// changed class. R2025b keeps the old body until <c>clear classes</c> (the fixture's
    /// divergence rows record the new-file case).
    /// </summary>
    [Fact]
    public async Task AnEditedMethodFileIsReadAtTheClassesNextMention()
    {
        WriteMethod("twice", "function r = twice(o)\nr = o.W * 2;\nend\n", -4);
        ScriptRunResult first = await RunMatlab("before = twice(P4T(3));");
        Assert.True(first.Success, first.Message + _output.ErrorText);
        Assert.Equal(6.0, Value(first, "before"));

        WriteMethod("twice", "function r = twice(o)\nr = o.W * 20;\nend\n", 4);
        ScriptRunResult second = await RunMatlab("after = twice(P4T(3));\nviaDot = P4T(3).twice();");
        Assert.True(second.Success, second.Message + _output.ErrorText);
        Assert.Equal(60.0, Value(second, "after"));
        Assert.Equal(60.0, Value(second, "viaDot"));
    }

    /// <summary>
    /// A signature line the class folder holds no file for leaves the class loadable: the call of
    /// that method is the refusal, and the declared method beside it works (not measured in R2025b).
    /// </summary>
    [Fact]
    public async Task ASignatureWithoutItsFileRefusesOnlyWhenCalled()
    {
        ScriptRunResult result = await RunMatlab("""
            v = declared(P4T(2));
            try, nofile(P4T(2)); msg = 'none'; catch e, msg = e.message; end
            listed = any(strcmp(methods('P4T'), 'nofile'));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(20.0, Value(result, "v"));
        Assert.Contains("'nofile' of class 'P4T' is declared without a body", (string)Value(result, "msg")!, StringComparison.Ordinal);
        Assert.Equal(true, Value(result, "listed"));
    }

    /// <summary>
    /// A method file that does not parse does not take the class down: the class loads, its other
    /// methods run, and the bad method's call raises the load error in R2025b's "Error: File: …"
    /// form, naming the file (not measured in R2025b, which reports a method file's parse error at
    /// its call too).
    /// </summary>
    [Fact]
    public async Task AMethodFileThatDoesNotParseFailsAtItsOwnCall()
    {
        WriteMethod("bad", "function r = bad(o)\nr = (o.W;\nend\n", 0);
        ScriptRunResult result = await RunMatlab("""
            v = declared(P4T(4));
            try, bad(P4T(4)); msg = 'none'; catch e, msg = e.message; end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(40.0, Value(result, "v"));
        string message = (string)Value(result, "msg")!;
        Assert.StartsWith("Error: File: ", message, StringComparison.Ordinal);
        Assert.Contains("bad.m Line: 2 Column: ", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A command word runs on over spaces and commas inside its parentheses (R2025b reads
    /// <c>which scale(t, 1)</c> as one argument, measured in p4_classfolders); outside them a
    /// space still ends the word.
    /// </summary>
    [Fact]
    public void ACommandWordKeepsItsParenthesesTogether()
    {
        var statement = (ExprStmt)Assert.Single(Parser.Parse("which scale(t, 1)", "", JgsDialect.Matlab));
        var call = (CallExpr)statement.Expression;
        Assert.Equal("scale(t, 1)", ((StringLiteral)Assert.Single(call.Arguments)).Value);

        var plain = (ExprStmt)Assert.Single(Parser.Parse("dbstop in f at 4", "", JgsDialect.Matlab));
        Assert.Equal(4, ((CallExpr)plain.Expression).Arguments.Count);
    }
}
