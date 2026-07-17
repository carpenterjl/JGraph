using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The JGS <c>run()</c> include builtin and the post-run variables snapshot, black-box through the
/// engine like the rest of the JGS suite.
/// </summary>
[Collection("JG facade")]
public class JgsWorkspaceRunTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsWorkspaceRunTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"jgraph_run_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Run_IncludesAnotherScript_MakingItsFunctionsAndVariablesAvailable()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "helpers.jgs"), """
                fn twice(n) {
                    return n * 2
                }
                let shared = 10
                """);

            var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), dir);
            ScriptRunResult result = await _engine.RunAsync("""
                run("helpers.jgs")
                print(twice(21))
                print(shared)
                """, context, CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Contains("42", _output.NormalText);
            Assert.Contains("10", _output.NormalText);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Run_ResolvesThroughTheWorkspaceResolver()
    {
        string root = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "lib"));
            File.WriteAllText(Path.Combine(root, "lib", "util.jgs"), "let answer = 42");

            using ScriptWorkspace workspace = ScriptWorkspace.Open(root);
            var context = new ScriptContext(
                _output, (_, figure) => _figures.Add(figure), root, path => workspace.Resolve(path, null));

            // "lib/util.jgs" is relative to the workspace root, not the process directory.
            ScriptRunResult result = await _engine.RunAsync("""
                run("lib/util.jgs")
                print(answer)
                """, context, CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Contains("42", _output.NormalText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Run_CircularInclude_FailsWithAClearError()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.jgs"), """run("b.jgs")""");
            File.WriteAllText(Path.Combine(dir, "b.jgs"), """run("a.jgs")""");

            var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), dir);
            ScriptRunResult result = await _engine.RunAsync("""run("a.jgs")""", context, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("circular", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Run_MissingFile_FailsGracefullyWithLocation()
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure));
        ScriptRunResult result = await _engine.RunAsync("""run("no-such-file.jgs")""", context, CancellationToken.None);

        Assert.False(result.Success);
        ScriptDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(1, diagnostic.Line);
        Assert.Contains("no-such-file.jgs", diagnostic.Message);
    }

    [Fact]
    public async Task RunAsync_SnapshotsUserVariables_AndHidesUntouchedBuiltins()
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure));
        ScriptRunResult result = await _engine.RunAsync("""
            let x = 5
            let name = "hi"
            let a = [1, 2, 3]
            fn f(n) {
                return n
            }
            """, context, CancellationToken.None);

        Assert.True(result.Success, result.Message);

        ScriptVariable x = Assert.Single(result.Variables, v => v.Name == "x");
        Assert.Equal("number", x.Type);
        Assert.Equal(5.0, Assert.IsType<double>(x.RawValue));

        ScriptVariable name = Assert.Single(result.Variables, v => v.Name == "name");
        Assert.Equal("string", name.Type);
        Assert.Equal("hi", name.DisplayValue);

        ScriptVariable a = Assert.Single(result.Variables, v => v.Name == "a");
        Assert.Equal("array", a.Type);
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, Assert.IsType<double[]>(a.RawValue));

        ScriptVariable f = Assert.Single(result.Variables, v => v.Name == "f");
        Assert.Equal("function", f.Type);
        Assert.Null(f.RawValue);

        // Untouched builtins stay out of the snapshot.
        Assert.DoesNotContain(result.Variables, v => v.Name is "sin" or "plot" or "run");
    }

    [Fact]
    public async Task RunAsync_ReboundBuiltin_AppearsInTheSnapshot()
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure));
        ScriptRunResult result = await _engine.RunAsync("let sin = 3", context, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        ScriptVariable sin = Assert.Single(result.Variables, v => v.Name == "sin");
        Assert.Equal("number", sin.Type);
    }
}
