using System.IO;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Script-level file I/O (M36): save/load through MAT-files and -ascii text, the fopen family, and
/// fprintf to a file id. Each test gets its own temp folder as the session's working directory.
/// </summary>
[Collection("JG facade")]
public class MatlabFileIoTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("jgraph-fileio-").FullName;

    public MatlabFileIoTests() => JG.Reset();

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(
            _output, (number, figure) => _figures.Add((number, figure)), _folder));

    private async Task RunAsserting(IScriptSession session, string code)
    {
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public async Task SaveThenLoad_RestoresTheWorkspace()
    {
        await using IScriptSession session = NewSession();

        await RunAsserting(session, """
            x = [1 2 3];
            M = magic(3);
            name = 'probe';
            ok = true;
            save('state.mat');
            clear;
            load('state.mat');
            assert(isequal(x, [1 2 3]));
            assert(isequal(M, magic(3)));
            assert(strcmp(name, 'probe'));
            assert(ok);
            """);

        Assert.True(File.Exists(Path.Combine(_folder, "state.mat")));
    }

    [Fact]
    public async Task Save_WithNames_WritesOnlyThose()
    {
        await using IScriptSession session = NewSession();

        await RunAsserting(session, """
            a = 1; b = 2;
            save('some.mat', 'a');
            clear;
            load('some.mat');
            assert(a == 1);
            assert(~ismember('b', fieldnames(load('some.mat'))));
            """);
    }

    [Fact]
    public async Task CommandSyntax_SaveAndLoad_Work()
    {
        await using IScriptSession session = NewSession();

        await RunAsserting(session, """
            x = 42;
            save cmdstate.mat
            clear
            load cmdstate.mat
            assert(x == 42);
            """);
    }

    [Fact]
    public async Task SaveAscii_RoundTripsAMatrixAsText()
    {
        await using IScriptSession session = NewSession();

        await RunAsserting(session, """
            A = [1 2; 3 4];
            save('grid.txt', 'A', '-ascii');
            load('grid.txt');
            assert(isequal(grid, [1 2; 3 4]));
            """);

        string text = File.ReadAllText(Path.Combine(_folder, "grid.txt"));
        Assert.Contains("1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadingAMissingFile_IsAClearError()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await session.ExecuteAsync(
            "load('nothere.mat');", sourceId: "", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("was not found", _output.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FwriteThenFread_RoundTripsBinaryDoubles()
    {
        await using IScriptSession session = NewSession();

        await RunAsserting(session, """
            fid = fopen('samples.bin', 'w');
            assert(fid >= 3);
            n = fwrite(fid, [1.5 -2.5 3.25], 'double');
            assert(n == 3);
            fclose(fid);
            fid = fopen('samples.bin', 'r');
            v = fread(fid, 'double');
            fclose(fid);
            assert(isequal(v, [1.5 -2.5 3.25]));
            """);
    }

    [Fact]
    public async Task Fread_DefaultsToBytes_AndHonorsCount()
    {
        await using IScriptSession session = NewSession();

        await RunAsserting(session, """
            fid = fopen('bytes.bin', 'w');
            fwrite(fid, [65 66 67 68]);
            fclose(fid);
            fid = fopen('bytes.bin', 'r');
            first = fread(fid, 2);
            rest = fread(fid);
            fclose(fid);
            assert(isequal(first, [65 66]));
            assert(isequal(rest, [67 68]));
            """);
    }

    [Fact]
    public async Task FprintfToAFile_AndFgetlBack()
    {
        await using IScriptSession session = NewSession();

        await RunAsserting(session, """
            fid = fopen('log.txt', 'w');
            fprintf(fid, 'line one\n');
            fprintf(fid, 'value %d\n', 42);
            fclose(fid);
            fid = fopen('log.txt', 'r');
            first = fgetl(fid);
            second = fgetl(fid);
            done = fgetl(fid);
            fclose(fid);
            assert(strcmp(first, 'line one'));
            assert(strcmp(second, 'value 42'));
            assert(~ischar(done) && done == -1);
            """);
    }

    [Fact]
    public async Task OpeningAMissingFile_ReturnsMinusOne()
    {
        await using IScriptSession session = NewSession();

        await RunAsserting(session, """
            fid = fopen('missing.bin', 'r');
            assert(fid == -1);
            """);
    }

    [Fact]
    public async Task Image_DisplaysAMatrixAsAPlot()
    {
        await using IScriptSession session = NewSession();

        await RunAsserting(session, "image([1 2; 3 4])");

        Assert.Single(JG.Gca().Plots);
    }
}
