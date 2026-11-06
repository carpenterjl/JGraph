using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The packages and class folders plan, stage P3 (ADR 0309): <c>import</c> of <c>+package</c> names
/// in the one import list beside the .NET imports. The parity fixture <c>p3_imports</c> pins every
/// measured rule against R2025b (120 rows); these tests pin what the fixture's one process cannot
/// see — the column a refusal points at for a second name on the line, a script's own import list
/// made from its whole file and dropped after its run, and a handle made under an import that
/// outlives the scope it was made in.
/// </summary>
[Collection("JG facade")]
public class JgsImportTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-p3-imports-" + Guid.NewGuid().ToString("N"));

    private readonly string _library;

    public JgsImportTests()
    {
        JG.Reset();
        _library = Path.Combine(_folder, "lib");
        Directory.CreateDirectory(Path.Combine(_library, "+pkg"));
        File.WriteAllText(Path.Combine(_library, "+pkg", "f.m"), "function r = f(x)\nr = x * 10;\nend\n");
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

    private string Escaped(string path) => path.Replace("\\", "\\\\");

    /// <summary>
    /// A refusal R2025b makes when it parses the file points at the name it refuses - the second
    /// name on an import line at its own column - and at the assignment for a variable named like an
    /// explicit import, each under the "Error: File: … Line: … Column: …" heading (measured for the
    /// first name and the assignment; the second name's column follows the same rule).
    /// </summary>
    [Fact]
    public async Task ARefusalPointsAtTheNameOrTheAssignmentItRefuses()
    {
        File.WriteAllText(Path.Combine(_folder, "imp_two.m"), "function r = imp_two()\nimport pkg.f pkg.nosuch\nr = 1;\nend\n");
        File.WriteAllText(Path.Combine(_folder, "imp_clash.m"), "function r = imp_clash()\nimport pkg.f\nx = 1;\nf = 5;\nr = f(2) + x;\nend\n");
        ScriptRunResult result = await RunMatlab($$"""
            addpath('{{Escaped(_library)}}');
            try, imp_two(); twoId = 'none'; twoMsg = ''; catch e, twoId = e.identifier; twoMsg = e.message; end
            try, imp_clash(); clashId = 'none'; clashMsg = ''; catch e, clashId = e.identifier; clashMsg = e.message; end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("MATLAB:mir_illegal_import_argument", Value(result, "twoId"));
        string two = (string)Value(result, "twoMsg")!;
        Assert.StartsWith("Error: File: ", two);
        Assert.Contains("imp_two.m Line: 2 Column: 14\nUnable to find or import 'pkg.nosuch'. Imported names must end with '.*' or be fully qualified.", two);
        Assert.Equal("MATLAB:lang:ImportedFunctionAndVariableHaveSameName", Value(result, "clashId"));
        string clash = (string)Value(result, "clashMsg")!;
        Assert.Contains("imp_clash.m Line: 4 Column: 1\nDeclaring a variable with the same name as the imported function \"f\" is not supported.", clash);
    }

    /// <summary>
    /// A script's imports are read from its whole file before its first line runs, so its first
    /// line lists an import written on its last and a call above the import already resolves; they
    /// are the script's own, so the function that ran the script lists nothing after (measured in
    /// p3_imports for the list after; the first-line listing follows the same rule as a function's).
    /// </summary>
    [Fact]
    public async Task AScriptsImportsAreItsOwnAndHoldFromItsFirstLine()
    {
        File.WriteAllText(Path.Combine(_library, "s_imp.m"), "firstLine = import;\nv = f(4);\nimport pkg.*\n");
        File.WriteAllText(Path.Combine(_folder, "runs_script.m"),
            "function [a, b, c] = runs_script()\ns_imp;\na = firstLine;\nb = v;\nc = import;\nend\n");
        ScriptRunResult result = await RunMatlab($$"""
            addpath('{{Escaped(_library)}}');
            [a, b, c] = runs_script();
            first = strjoin(a', ',');
            value = b;
            after = sprintf('%dx%d', size(c, 1), size(c, 2));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("pkg.*", Value(result, "first"));
        Assert.Equal(40.0, Value(result, "value"));
        Assert.Equal("0x1", Value(result, "after"));
    }

    /// <summary>
    /// A handle made under an import is the handle to the member itself - named in full, naming no
    /// file - and calls it from a caller that imported nothing (measured: func2str 'pkg.f'; the
    /// call from outside follows from the handle holding the member).
    /// </summary>
    [Fact]
    public async Task AHandleMadeUnderAnImportOutlivesItsScope()
    {
        File.WriteAllText(Path.Combine(_folder, "mk_handle.m"), "function h = mk_handle()\nimport pkg.*\nh = @f;\nend\n");
        ScriptRunResult result = await RunMatlab($$"""
            addpath('{{Escaped(_library)}}');
            h = mk_handle();
            name = func2str(h);
            value = h(2);
            file = functions(h).file;
            direct = exist('f');
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("pkg.f", Value(result, "name"));
        Assert.Equal(20.0, Value(result, "value"));
        Assert.Equal("", Value(result, "file"));
        Assert.Equal(0.0, Value(result, "direct"));
    }
}
