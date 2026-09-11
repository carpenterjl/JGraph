using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// <c>run(path)</c> runs a MATLAB script in its own folder, the way R2025b's <c>run</c> does
/// (measured: <c>pwd</c> inside <c>run('other/s.m')</c> is <c>other</c>; the caller is back where it
/// was afterwards, an error included; a <c>cd</c> the script makes stands; a <c>run</c> inside the
/// script resolves beside it; the stem names the file with or without <c>.m</c>). The folder is
/// what puts a file beside the script on the implicit path, which ADR 0149 left open. A JGS include
/// stays where it was called from.
/// </summary>
[Collection("JG facade")]
public class JgsRunFolderTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-run-folder-" + Guid.NewGuid().ToString("N"));

    public JgsRunFolderTests()
    {
        JG.Reset();
        Directory.CreateDirectory(Path.Combine(_folder, "other"));
        Directory.CreateDirectory(Path.Combine(_folder, "elsewhere"));
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

    private static void AssertFolder(string expected, object? actual) =>
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(expected),
            Path.TrimEndingDirectorySeparator(Assert.IsType<string>(actual)),
            ignoreCase: true);

    private void WriteFile(string relative, string source) =>
        File.WriteAllText(Path.Combine(_folder, relative), source);

    private void WriteOtherFolder()
    {
        WriteFile("other/max.m", "function y = max(varargin)\ny = 'file max';\nend\n");
        WriteFile("other/sib.m", "function y = sib()\ny = 'sibling';\nend\n");
        WriteFile("other/s.m", "S_PWD = pwd;\nS_SIB = sib();\nS_MAX = max('tag');\n");
    }

    [Fact]
    public async Task RunByPath_EntersTheScriptsFolder_AndComesBack()
    {
        WriteOtherFolder();

        ScriptRunResult result = await RunMatlab("""
            run('other/s.m');
            after = pwd;
            try
                gone = sib();
            catch e
                gone = e.message;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        AssertFolder(Path.Combine(_folder, "other"), Value(result, "S_PWD"));
        Assert.Equal("sibling", Value(result, "S_SIB"));
        Assert.Equal("file max", Value(result, "S_MAX")); // the char argument is not a method's, so the file beside the script answers
        AssertFolder(_folder, Value(result, "after"));
        Assert.Contains("sib", Assert.IsType<string>(Value(result, "gone"))); // the folder left with the script
    }

    [Fact]
    public async Task AScriptThatMoves_LeavesTheCallerWhereItWent()
    {
        WriteFile("other/mover.m", "M_PWD = pwd;\ncd('../elsewhere');\n");

        ScriptRunResult result = await RunMatlab("""
            run('other/mover.m');
            after = pwd;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        AssertFolder(Path.Combine(_folder, "other"), Value(result, "M_PWD"));
        AssertFolder(Path.Combine(_folder, "elsewhere"), Value(result, "after"));
    }

    [Fact]
    public async Task AnErrorInsideTheScript_StillComesBack()
    {
        WriteFile("other/failer.m", "F_PWD = pwd;\nerror('t:boom', 'boom');\n");

        ScriptRunResult result = await RunMatlab("""
            try
                run('other/failer.m');
            catch e
                id = e.identifier;
            end
            after = pwd;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("t:boom", Value(result, "id"));
        AssertFolder(Path.Combine(_folder, "other"), Value(result, "F_PWD"));
        AssertFolder(_folder, Value(result, "after"));
    }

    [Fact]
    public async Task ARunInsideTheScript_ResolvesBesideIt()
    {
        WriteOtherFolder();
        WriteFile("other/u.m", "U_PWD = pwd;\n");
        WriteFile("other/t.m", "run('u.m');\nT_SIB = sib();\nT_PWD = pwd;\n");

        ScriptRunResult result = await RunMatlab("""
            run('other/t.m');
            after = pwd;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        AssertFolder(Path.Combine(_folder, "other"), Value(result, "U_PWD"));
        AssertFolder(Path.Combine(_folder, "other"), Value(result, "T_PWD"));
        Assert.Equal("sibling", Value(result, "T_SIB"));
        AssertFolder(_folder, Value(result, "after"));
    }

    [Fact]
    public async Task TheStemNamesTheFile_WithOrWithoutItsExtension()
    {
        WriteFile("local.m", "L_PWD = pwd;\n");
        WriteFile("other/s.m", "S_PWD = pwd;\n");

        ScriptRunResult result = await RunMatlab("""
            run('local');
            run(fullfile(pwd, 'other', 's'));
            after = pwd;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        AssertFolder(_folder, Value(result, "L_PWD"));
        AssertFolder(Path.Combine(_folder, "other"), Value(result, "S_PWD"));
        AssertFolder(_folder, Value(result, "after"));
    }

    [Fact]
    public async Task RunFromAMovedFolder_ResolvesRelativeToIt_AndComesBackThere()
    {
        WriteOtherFolder();

        ScriptRunResult result = await RunMatlab("""
            cd('elsewhere');
            run('../other/s.m');
            after = pwd;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        AssertFolder(Path.Combine(_folder, "other"), Value(result, "S_PWD"));
        AssertFolder(Path.Combine(_folder, "elsewhere"), Value(result, "after"));
    }

    [Fact]
    public async Task AJgsInclude_StaysInTheCallersFolder()
    {
        WriteFile("other/util.jgs", "let where = pwd()\n");

        ScriptRunResult result = await new JgsScriptEngine().RunAsync("""
            run("other/util.jgs")
            let after = pwd()
            """, Context(), default);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        AssertFolder(_folder, Value(result, "where"));
        AssertFolder(_folder, Value(result, "after"));
    }
}
