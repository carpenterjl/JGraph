using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M34: the console commands — <c>clc</c> clears the output sink's display, <c>dir</c> lists a folder
/// and <c>path</c> reports the folder bare names resolve against. M109 turned <c>dir</c>'s answer into
/// MATLAB's struct array and gave <c>ls</c> the same listing as a char matrix.
/// </summary>
[Collection("JG facade")]
public class ConsoleBuiltinTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-dir-" + Guid.NewGuid().ToString("N"));

    public ConsoleBuiltinTests()
    {
        JG.Reset();
        Directory.CreateDirectory(Path.Combine(_folder, "sub"));
        File.WriteAllText(Path.Combine(_folder, "a.m"), "% a");
        File.WriteAllText(Path.Combine(_folder, "b.m"), "% b");
        File.WriteAllText(Path.Combine(_folder, "c.txt"), "c");
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }, _folder), default);

    private Task<ScriptRunResult> RunJgs(string code) =>
        new JgsScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }, _folder), default);

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    private static string Text(ScriptRunResult result, string name) =>
        Assert.IsType<string>(Assert.Single(result.Variables, v => v.Name == name).RawValue);

    [Fact]
    public async Task Clc_ClearsTheOutputSink()
    {
        ScriptRunResult result = await RunMatlab("disp(1)\nclc\n");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(1, _output.ClearCount);
    }

    [Fact]
    public async Task Clc_BareName_AutoCallsInBothDialects()
    {
        Assert.True((await RunMatlab("clc\n")).Success);
        Assert.True((await RunJgs("clc\n")).Success);

        Assert.Equal(2, _output.ClearCount);
    }

    /// <summary>
    /// M109: the idiom that sent this milestone — <c>d(k).name</c> and <c>d(k).bytes</c> over the
    /// struct array MATLAB answers with. Before it, <c>dir</c> answered a cell of names and the loop
    /// died on "'.isdir' needs a struct, but this is a cell."
    /// </summary>
    [Fact]
    public async Task Dir_AnswersMatlabsStructArray_WithTheDocumentedFields()
    {
        ScriptRunResult result = await RunMatlab("""
            d = dir();
            n = numel(d);
            rows = size(d, 1);
            cols = size(d, 2);
            f = fieldnames(d);
            nf = numel(f);
            names = '';
            total = 0;
            folders = 0;
            for k = 1:numel(d)
                names = [names d(k).name ' '];
                if d(k).isdir
                    folders = folders + 1;
                else
                    total = total + d(k).bytes;
                end
            end
            kind = class(d(1).isdir);
            here = double(strcmp(d(1).folder, d(4).folder));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);

        // '.' and '..' are entries like any other, which is why the count is six and not four.
        Assert.Equal(6, Number(result, "n"));
        Assert.Equal(6, Number(result, "rows"));
        Assert.Equal(1, Number(result, "cols"));
        Assert.Equal(6, Number(result, "nf"));
        Assert.Equal(". .. a.m b.m c.txt sub ", Text(result, "names"));
        Assert.Equal(3, Number(result, "folders"));
        Assert.Equal(3 + 3 + 1, Number(result, "total"));
        Assert.Equal("logical", Text(result, "kind"));
        Assert.True(Number(result, "here") != 0);
    }

    [Fact]
    public async Task Dir_FieldsMatchTheFileOnDisk()
    {
        var written = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Unspecified);
        File.SetLastWriteTime(Path.Combine(_folder, "c.txt"), written.AddMilliseconds(777));

        ScriptRunResult result = await RunMatlab("""
            d = dir('c.txt');
            n = numel(d);
            nm = d.name;
            fold = d.folder;
            when = d.date;
            b = d.bytes;
            isd = double(d.isdir);
            serial = d.datenum;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(1, Number(result, "n"));
        Assert.Equal("c.txt", Text(result, "nm"));
        Assert.Equal(_folder, Text(result, "fold"));

        // The date string carries no fraction, so the datenum is the same instant truncated to the
        // whole second — the sub-second 777 ms is dropped by both, not rounded by either.
        Assert.Equal("04-Mar-2026 05:06:07", Text(result, "when"));
        Assert.Equal(1, Number(result, "b"));
        Assert.Equal(0, Number(result, "isd"));
        Assert.Equal(written.ToOADate() + 693960.0, Number(result, "serial"), 9);
    }

    [Fact]
    public async Task Dir_Pattern_FiltersTheListing_AndDropsTheDotEntries()
    {
        ScriptRunResult result = await RunMatlab("""
            d = dir('*.m');
            n = numel(d);
            first = d(1).name;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);

        // '.' and '..' are held against the pattern like any other name, and neither ends in '.m'.
        Assert.Equal(2, Number(result, "n"));
        Assert.Equal("a.m", Text(result, "first"));
    }

    [Fact]
    public async Task Dir_SubfolderName_ListsItsContents()
    {
        File.WriteAllText(Path.Combine(_folder, "sub", "inner.jgs"), "// inner");

        ScriptRunResult result = await RunMatlab("""
            d = dir('sub');
            n = numel(d);
            only = d(3).name;
            where = d(3).folder;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(3, Number(result, "n"));
        Assert.Equal("inner.jgs", Text(result, "only"));
        Assert.Equal(Path.Combine(_folder, "sub"), Text(result, "where"));
    }

    [Fact]
    public async Task Dir_MissingFolder_YieldsAnEmptyStructArrayThatStillHasTheFields()
    {
        ScriptRunResult result = await RunMatlab("""
            d = dir('nosuchfolder/*.m');
            n = numel(d);
            rows = size(d, 1);
            cols = size(d, 2);
            kind = class(d);
            nf = numel(fieldnames(d));
            gone = double(isempty(d));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(0, Number(result, "n"));
        Assert.Equal(0, Number(result, "rows"));
        Assert.Equal(1, Number(result, "cols"));
        Assert.Equal("struct", Text(result, "kind"));
        Assert.Equal(6, Number(result, "nf"));
        Assert.True(Number(result, "gone") != 0);
    }

    [Fact]
    public async Task Dir_Discarded_PrintsTheNamesInsteadOfEchoingTheStruct()
    {
        ScriptRunResult result = await RunMatlab("dir\n");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Contains("a.m", _output.NormalText, StringComparison.Ordinal);
        Assert.Contains("sub", _output.NormalText, StringComparison.Ordinal);
        Assert.DoesNotContain("ans", _output.NormalText, StringComparison.Ordinal);
        Assert.DoesNotContain("isdir", _output.NormalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ls_AnswersTheNamesAsACharMatrix()
    {
        ScriptRunResult result = await RunMatlab("""
            a = ls();
            kind = class(a);
            rows = size(a, 1);
            cols = size(a, 2);
            third = strtrim(a(3, :));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("char", Text(result, "kind"));
        Assert.Equal(6, Number(result, "rows"));

        // Padded to the longest name, which is 'c.txt'.
        Assert.Equal(5, Number(result, "cols"));
        Assert.Equal("a.m", Text(result, "third"));
    }

    [Fact]
    public async Task Ls_NameThatMatchesNothing_AnswersAnEmptyChar()
    {
        ScriptRunResult result = await RunMatlab("""
            a = ls('nosuchthing');
            kind = class(a);
            rows = size(a, 1);
            cols = size(a, 2);
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("char", Text(result, "kind"));
        Assert.Equal(0, Number(result, "rows"));
        Assert.Equal(0, Number(result, "cols"));
    }

    /// <summary>
    /// <c>ls</c> says a name matched nothing only when nobody caught the answer; the caught answer is
    /// an empty char and no message at all. <c>dir</c> is silent either way.
    /// </summary>
    [Fact]
    public async Task Ls_Discarded_SaysTheNameMatchedNothing()
    {
        ScriptRunResult result = await RunMatlab("ls nosuchthing\n");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Contains("'nosuchthing' not found.", _output.NormalText, StringComparison.Ordinal);
    }

    /// <summary>
    /// M109: the discarded arm is reached by the interpreter rather than by
    /// <see cref="JgsCallable"/>'s own call path, and until this milestone it skipped the string
    /// demotion every other arm has — so <c>dir sub</c> handed <c>dir</c> a string array and raised
    /// where <c>dir('sub')</c> listed the folder.
    /// </summary>
    [Fact]
    public async Task Dir_CommandSyntax_ReachesTheSameListingAsTheWrittenOutCall()
    {
        ScriptRunResult result = await RunMatlab("dir sub\n");

        Assert.True(result.Success, result.Message + _output.ErrorText);

        // 'sub' holds only '.' and '..', so its listing names neither of the folder's own files.
        Assert.DoesNotContain("a.m", _output.NormalText, StringComparison.Ordinal);
        Assert.Contains("..", _output.NormalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_AnswersMatlabsFieldSet_InMatlabsOrder()
    {
        File.WriteAllText(Path.Combine(_folder, "d.mat"), "m");
        Directory.CreateDirectory(Path.Combine(_folder, "@Cls"));
        Directory.CreateDirectory(Path.Combine(_folder, "+pkg"));

        ScriptRunResult result = await RunMatlab("""
            w = what();
            f = fieldnames(w);
            joined = strjoin(f', ' ');
            ms = numel(w.m);
            mrows = size(w.m, 1);
            mcols = size(w.m, 2);
            first = w.m{1};
            cls = w.classes{1};
            pkg = w.packages{1};
            mats = numel(w.mat);
            none = numel(w.mex);
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(
            "path m mlapp mlx mat mex mdl slx sfx p classes packages jgs", Text(result, "joined"));
        Assert.Equal(2, Number(result, "ms"));

        // MATLAB's lists are columns.
        Assert.Equal(2, Number(result, "mrows"));
        Assert.Equal(1, Number(result, "mcols"));
        Assert.Equal("a.m", Text(result, "first"));

        // A class and a package folder are reported without the marker that selected them.
        Assert.Equal("Cls", Text(result, "cls"));
        Assert.Equal("pkg", Text(result, "pkg"));
        Assert.Equal(1, Number(result, "mats"));
        Assert.Equal(0, Number(result, "none"));
    }

    [Fact]
    public async Task What_MissingFolder_AnswersAnEmptyStructArray()
    {
        ScriptRunResult result = await RunMatlab("""
            w = what('nosuchfolder');
            gone = double(isempty(w));
            rows = size(w, 1);
            cols = size(w, 2);
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.True(Number(result, "gone") != 0);
        Assert.Equal(0, Number(result, "rows"));
        Assert.Equal(1, Number(result, "cols"));
    }

    /// <summary>
    /// M109: a name that is both a builtin and a folder answers 5, not 7. MATLAB asks what the name
    /// <em>means</em> before it asks what is on the disk, and <c>exist('fix')</c> beside a folder
    /// called <c>fix</c> is the case that shows it.
    /// </summary>
    [Fact]
    public async Task Exist_BuiltinOutranksAFolderOfTheSameName()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "fix"));

        ScriptRunResult result = await RunMatlab("""
            bare = exist('fix');
            asFolder = exist('fix', 'dir');
            plain = exist('sub');
            file = exist('a.m', 'file');
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(5, Number(result, "bare"));

        // Naming the kind still reaches the disk, and a folder that is nobody's builtin is 7.
        Assert.Equal(7, Number(result, "asFolder"));
        Assert.Equal(7, Number(result, "plain"));
        Assert.Equal(2, Number(result, "file"));
    }

    [Fact]
    public async Task Path_ReportsTheWorkingDirectory()
    {
        ScriptRunResult result = await RunMatlab("p = path();");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(_folder, Assert.Single(result.Variables, v => v.Name == "p").RawValue);
    }

    /// <summary>
    /// M62 turned this one around. The old test pinned the refusal — there was no search path, and
    /// saying so beat "not recognized" — and it was right for as long as that was true. Now the
    /// folder joins the path and the refusal is reserved for a folder that is not there, which is the
    /// only thing addpath has left to complain about.
    /// </summary>
    [Fact]
    public async Task Addpath_AddsAFolder_AndRefusesOneThatIsNotThere()
    {
        ScriptRunResult result = await RunMatlab("""
            addpath(pwd());
            listed = double(contains(path(), pwd()));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(1.0, Number(result, "listed"));

        ScriptRunResult missing = await RunMatlab("addpath('no-such-folder-anywhere')");

        Assert.False(missing.Success);
        Assert.Contains("addpath", missing.Message, StringComparison.Ordinal);
        Assert.Contains("no-such-folder-anywhere", missing.Message, StringComparison.Ordinal);
    }
}
