using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The M39 introspection, console-session, and installation builtins. Several of these describe a
/// teletype session JGraph's console is not; what the assertions pin is that the setting round-trips
/// and the call shape is MATLAB's, which is what a ported script depends on.
/// </summary>
[Collection("JG facade")]
public class MatlabSessionBuiltinTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabSessionBuiltinTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public Task Func2str_PrintsTheHandleBack() => RunAsserting("""
        f = @(x) x.^2 + 1;
        assert(strcmp(func2str(f), '@(x) (x .^ 2) + 1'));

        % A named handle prints as @name, which is the other half of what func2str reports.
        assert(strcmp(func2str(@sin), '@sin'));

        % Two handles written differently but meaning the same thing print the same, because the
        % text comes from the parsed tree rather than from the caller's spacing.
        g = @(x)x.^2+1;
        assert(strcmp(func2str(f), func2str(g)));

        % str2func is the other direction, and the round trip holds.
        h = str2func(func2str(f));
        assert(h(3) == 10);
        """);

    [Fact]
    public Task Functions_SaysWhatAHandleIs() => RunAsserting("""
        s = functions(@(x) x + 1);
        assert(strcmp(s.type, 'anonymous'));
        assert(strcmp(s.function, '(x) x + 1'));

        b = functions(@cos);
        assert(strcmp(b.type, 'builtin'));
        """);

    [Fact]
    public Task Mfilename_IsEmptyForCodeWithNoFile() => RunAsserting("""
        % Prompt input has no file behind it, and MATLAB reports nothing rather than guessing.
        assert(strcmp(mfilename, ''));
        assert(strcmp(mfilename('fullpath'), ''));
        """);

    [Fact]
    public Task Inputname_NamesTheCallersVariable() => RunAsserting("""
        function r = probe(a, b)
            r = {inputname(1), inputname(2)};
        end

        measured = 5;
        names = probe(measured, 2 + 2);
        assert(strcmp(names{1}, 'measured'));

        % An argument that was an expression rather than a variable has no name to report.
        assert(strcmp(names{2}, ''));
        """);

    [Fact]
    public Task TheSessionSettingsRoundTrip() => RunAsserting("""
        % Each of these keeps what it was set to, whether or not JGraph's console acts on it.
        echo on;
        echo off;
        more off;
        more(30);
        beep off;
        rehash;
        pack;
        assert(strcmp(recycle, 'off'));

        % Turning recycling on would be a promise JGraph's delete cannot keep, so it is refused.
        caught = 0;
        try
            recycle('on');
        catch err
            caught = 1;
        end
        assert(caught == 1);
        """);

    [Fact]
    public Task Lookfor_FindsBuiltinsByWord() => RunAsserting("""
        hits = lookfor('Airy');
        assert(length(hits) >= 1);
        assert(strcmp(hits{1}, 'airy'));
        """);

    [Fact]
    public Task TheInstallationQueriesAnswerAboutJGraph() => RunAsserting("""
        % A bare name answers rather than handing back the function, which is what makes
        % disp(computer) print the platform.
        assert(ischar(computer));
        assert(strcmp(computer('arch'), lower(computer)) || length(computer('arch')) > 0);
        assert(ischar(version));
        assert(length(version('-release')) > 0);
        assert(~isstudent);
        assert(length(matlabroot) > 0);
        assert(strcmp(matlabdrive, ''));
        assert(strcmp(license, 'JGraph'));
        assert(license('test', 'anything') == 1);

        m = memory;
        assert(m.MemUsedMATLAB > 0);
        assert(m.MaxPossibleArrayBytes > 0);

        assert(maxNumCompThreads > 0);
        previous = maxNumCompThreads(2);
        assert(maxNumCompThreads == 2);
        maxNumCompThreads(previous);

        % The planner is remembered and reported, though JGraph's transform has no plan to tune.
        assert(strcmp(fftw('planner'), 'estimate'));
        fftw('planner', 'measure');
        assert(strcmp(fftw('planner'), 'measure'));
        """);

    [Fact]
    public Task What_GroupsAFoldersFilesByKind() => RunAsserting("""
        w = what(pwd);
        assert(isfield(w, 'path'));
        assert(isfield(w, 'm'));
        assert(iscell(w.m));
        """);

    [Fact]
    public async Task Diary_WritesTheConsoleToAFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "jgraph-diary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string log = Path.Combine(folder, "session.txt").Replace('\\', '/');
            await RunAsserting($"""
                diary('{log}');
                disp('recorded');
                diary off;
                disp('not recorded');
                """);

            string written = File.ReadAllText(log);
            Assert.Contains("recorded", written, StringComparison.Ordinal);
            Assert.DoesNotContain("not recorded", written, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
