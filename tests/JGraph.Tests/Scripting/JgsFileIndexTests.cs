using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M145, step 3: the function path's file index knows which built-in names a <c>.m</c> file on a
/// search folder claims, without a disk probe per call. Nothing consults that set yet — a call
/// still reaches the built-in until the flip — so these tests read the index itself, from a debug
/// hook that runs between statements: inside a loop body the statement epoch is frozen, which is
/// the only way to tell "seen because the interpreter said it changed the file" from "seen because
/// a new statement re-read the folder time".
/// </summary>
[Collection("JG facade")]
public class JgsFileIndexTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-m145-index-" + Guid.NewGuid().ToString("N"));

    public JgsFileIndexTests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private const string MeanSource = "function y = mean(x)\ny = -1;\nend\n";

    private ScriptContext Context() => new(_output, static (_, _) => { }, _folder);

    private JgsReplSession NewSession() => Assert.IsType<JgsReplSession>(
        Assert.IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine()).CreateSession(Context()));

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(code, Context(), default);

    /// <summary>Runs <paramref name="code"/> as a file in a fresh session with <paramref name="probe"/> attached.</summary>
    private async Task<ScriptRunResult> RunProbed(string code, Probe probe)
    {
        await using JgsReplSession session = NewSession();
        return await session.ExecuteFileAsync(code, sourceId: "", probe, CancellationToken.None);
    }

    private static object? Value(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue;

    private string Escaped(string path) => path.Replace("\\", "\\\\");

    // --- Every writer the interpreter has, inside one statement ---------------------------------

    /// <summary>
    /// Each writing built-in resolves through the host's write seam, which tells the index before
    /// the file lands; so a <c>mean.m</c> written in a loop's pass is in the shadowing set at the
    /// next statement of that same pass, with no statement boundary in between, and gone again
    /// once <c>delete</c> has removed it.
    /// </summary>
    [Fact]
    public async Task EveryWriter_IsSeen_InsideTheStatementThatWrote()
    {
        File.WriteAllText(Path.Combine(_folder, "seed.txt"), MeanSource);
        var probe = new Probe();
        ScriptRunResult result = await RunProbed("""
            k = 0;
            for w = 1:7
                switch w
                    case 1
                        fid = fopen('mean.m', 'w'); fprintf(fid, 'function y = mean(x)\ny = 1;\nend\n'); fclose(fid);
                    case 2
                        writelines(["function y = mean(x)", "y = 1;", "end"], 'mean.m');
                    case 3
                        writematrix([1 2; 3 4], 'mean.m');
                    case 4
                        writetable(array2table([1 2]), 'mean.m');
                    case 5
                        save('mean.m', 'k');
                    case 6
                        copyfile('seed.txt', 'mean.m');
                    case 7
                        copyfile('seed.txt', 'moving.txt');
                        movefile('moving.txt', 'mean.m');
                end
                k = k + 1; % probe: shadowing must hold mean here
                delete('mean.m');
                k = k + 0; % probe: and not here
            end
            """, probe);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(7.0, Value(result, "k"));

        List<Probe.Seen> after = probe.At(line: 20);
        List<Probe.Seen> gone = probe.At(line: 22);
        Assert.Equal(7, after.Count);
        Assert.Equal(7, gone.Count);
        Assert.All(after, seen => Assert.True(seen.ShadowsMean, $"pass {after.IndexOf(seen)} after writing"));
        Assert.All(gone, seen => Assert.False(seen.ShadowsMean, $"pass {gone.IndexOf(seen)} after deleting"));

        // The whole loop is one top-level statement: the epoch never moved, so nothing above was
        // found by re-reading a folder time.
        Assert.Single(after.Concat(gone).Select(seen => seen.Epoch).Distinct());
    }

    [Fact]
    public async Task CreateDeleteAndRename_FromInsideAFunction_AndALoop_AreSeen()
    {
        var probe = new Probe();
        ScriptRunResult result = await RunProbed("""
            for pass = 1:2
                make('mean');
                a = 1; % probe: mean
                movefile('mean.m', 'sum.m');
                b = 1; % probe: sum, not mean
                kill('sum');
                c = 1; % probe: neither
            end
            function make(name)
                writelines(sprintf('function y = %s(x)', name), [name '.m']);
            end
            function kill(name)
                delete([name '.m']);
            end
            """, probe);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.All(probe.At(line: 3), seen => Assert.Equal(new[] { "mean" }, seen.Shadowing));
        Assert.All(probe.At(line: 5), seen => Assert.Equal(new[] { "sum" }, seen.Shadowing));
        Assert.All(probe.At(line: 7), seen => Assert.Empty(seen.Shadowing));
        Assert.Equal(2, probe.At(line: 7).Count);
    }

    /// <summary>
    /// A rebuild is by invalidation as well as by time: the folder's write time is put back to what
    /// it was before the script wrote, and the index still knows about the file, because the write
    /// itself dirtied the folder.
    /// </summary>
    [Fact]
    public async Task AScriptWrite_IsSeen_EvenWhenTheFolderTimeDidNotMove()
    {
        DateTime before = default;
        var probe = new Probe();
        probe.BeforeLine(2, _ => before = Directory.GetLastWriteTimeUtc(_folder));
        probe.BeforeLine(3, _ => Directory.SetLastWriteTimeUtc(_folder, before));
        ScriptRunResult result = await RunProbed("""
            a = 1; % probe: read the folder (and its time) now
            writelines("function y = mean(x)", 'mean.m');
            b = 1; % probe: the time is put back first, then the index is asked
            """, probe);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.False(Assert.Single(probe.At(line: 2)).ShadowsMean);
        Assert.Equal(before, Directory.GetLastWriteTimeUtc(_folder));
        Assert.True(Assert.Single(probe.At(line: 3)).ShadowsMean);
    }

    // --- Changes the interpreter did not make ---------------------------------------------------

    /// <summary>
    /// The recorded divergence: a file another process drops during one long statement is seen one
    /// statement late. The folder time is re-read at most once per top-level statement, and the
    /// loop below is one statement.
    /// </summary>
    [Fact]
    public async Task AnExternalChange_IsSeen_AtTheNextTopLevelStatement()
    {
        var probe = new Probe();
        probe.BeforeLine(3, _ => File.WriteAllText(Path.Combine(_folder, "mean.m"), MeanSource));
        ScriptRunResult result = await RunProbed("""
            for pass = 1:2
                a = 1; % probe: the folder is read (and its time checked) for this statement
                b = 1; % the file is dropped from outside, then probed: not seen
                c = 1; % probe: still not seen inside the same statement
            end
            d = 1; % probe: a new top-level statement sees it
            """, probe);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.False(probe.At(line: 2)[0].ShadowsMean);
        Assert.False(probe.At(line: 3)[0].ShadowsMean);
        Assert.False(probe.At(line: 4)[0].ShadowsMean);
        Assert.True(Assert.Single(probe.At(line: 6)).ShadowsMean);
        Assert.NotEqual(probe.At(line: 4)[0].Epoch, probe.At(line: 6)[0].Epoch);
    }

    [Fact]
    public async Task Rehash_ShowsAnExternalChange_InsideTheStatement()
    {
        var probe = new Probe();
        probe.BeforeLine(3, _ => File.WriteAllText(Path.Combine(_folder, "mean.m"), MeanSource));
        ScriptRunResult result = await RunProbed("""
            for pass = 1:1
                a = 1; % probe: folder read for this statement
                b = 1; % dropped from outside, not seen
                rehash
                c = 1; % probe: seen
            end
            """, probe);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.False(probe.At(line: 3)[0].ShadowsMean);
        Assert.True(probe.At(line: 5)[0].ShadowsMean);
        Assert.Single(probe.All.Where(seen => seen.Line > 1).Select(seen => seen.Epoch).Distinct());
    }

    [Fact]
    public async Task AFolderThatCannotBeRead_KeepsItsFiles_AndWarnsOnce()
    {
        File.WriteAllText(Path.Combine(_folder, "mean.m"), MeanSource);
        var probe = new Probe();
        probe.BeforeLine(3, index => index.EnumerateFiles = folder => throw new UnauthorizedAccessException("no longer yours"));
        ScriptRunResult result = await RunProbed("""
            a = 1; % probe: mean is read from the folder
            rehash
            b = 1; % the folder stops being readable, the index is told to re-read, and is probed
            rehash
            c = 1; % probe: still mean, and no second warning
            """, probe);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.True(probe.At(line: 1)[0].ShadowsMean);
        Assert.True(probe.At(line: 3)[0].ShadowsMean);
        Assert.True(probe.At(line: 5)[0].ShadowsMean);
        Assert.Contains("could not be read", _output.ErrorText);
        Assert.Contains("no longer yours", _output.ErrorText);
        Assert.Equal(1, _output.ErrorText.Split("Warning:").Length - 1);
    }

    /// <summary>
    /// A write that cannot change what a name means — an image, a text file, a .mat — leaves the
    /// index alone, so a loop that writes ten thousand of them does not re-list the folder at
    /// every built-in call once the set is consulted. Only a <c>.m</c> path, or a known folder, dirties.
    /// </summary>
    [Fact]
    public async Task AWriteThatIsNotCode_DoesNotReListTheFolder()
    {
        int listings = 0;
        var probe = new Probe();
        probe.BeforeLine(1, index => index.EnumerateFiles = folder =>
        {
            listings++;
            return Directory.EnumerateFiles(folder, "*.m");
        });
        ScriptRunResult result = await RunProbed("""
            for pass = 1:1
                a = 1; % probe: one listing
                writelines("notes", 'notes.txt');
                b = 1; % probe: still one
                writelines("function y = mean(x)", 'mean.m');
                c = 1; % probe: two, and mean
            end
            """, probe);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(2, listings);
        Assert.False(probe.At(line: 4)[0].ShadowsMean);
        Assert.True(probe.At(line: 6)[0].ShadowsMean);
    }

    // --- What the set holds, and what it never decides -----------------------------------------

    [Fact]
    public async Task OnlyBuiltinNames_AreShadowing_AndTheFolderListsEveryStem()
    {
        File.WriteAllText(Path.Combine(_folder, "mean.m"), MeanSource);
        File.WriteAllText(Path.Combine(_folder, "notabuiltin.m"), "function y = notabuiltin()\ny = 1;\nend\n");
        File.WriteAllText(Path.Combine(_folder, "mean.mat"), "not code");
        File.WriteAllText(Path.Combine(_folder, "max.txt"), "not code");
        var probe = new Probe();
        ScriptRunResult result = await RunProbed("a = 1;", probe);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Probe.Seen seen = Assert.Single(probe.All);
        Assert.Equal(new[] { "mean" }, seen.Shadowing);
        Assert.Equal(new[] { "mean", "notabuiltin" }, probe.Index!.StemsOf(_folder).OrderBy(s => s, StringComparer.Ordinal));
    }

    /// <summary>The miss path: a name that is not a built-in never asks the index, so it is found on the disk at once.</summary>
    [Fact]
    public async Task ANameThatIsNotABuiltin_WrittenAndCalledInOneStatement_Resolves()
    {
        ScriptRunResult result = await RunMatlab("""
            for k = 1:2
                if k == 1
                    fid = fopen('newfn.m', 'w'); fprintf(fid, 'function y = newfn()\ny = 41;\nend\n'); fclose(fid);
                else
                    r = newfn() + 1;
                end
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(42.0, Value(result, "r"));
    }

    [Fact]
    public async Task MovingWithCd_AndAddpath_MovesTheShadowingSetWithTheFolders()
    {
        string sub = Path.Combine(_folder, "sub");
        string lib = Path.Combine(_folder, "lib");
        Directory.CreateDirectory(sub);
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(sub, "mean.m"), MeanSource);
        File.WriteAllText(Path.Combine(lib, "max.m"), "function y = max(x)\ny = -1;\nend\n");
        var probe = new Probe();
        ScriptRunResult result = await RunProbed($"""
            a = 1; % probe: nothing
            cd('sub');
            b = 1; % probe: mean
            cd('..');
            c = 1; % probe: nothing again
            addpath('{Escaped(lib)}');
            d = 1; % probe: max
            rmpath('{Escaped(lib)}');
            e = 1; % probe: nothing
            """, probe);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Empty(probe.At(line: 1)[0].Shadowing);
        Assert.Equal(new[] { "mean" }, probe.At(line: 3)[0].Shadowing);
        Assert.Empty(probe.At(line: 5)[0].Shadowing);
        Assert.Equal(new[] { "max" }, probe.At(line: 7)[0].Shadowing);
        Assert.Empty(probe.At(line: 9)[0].Shadowing);
    }

    [Fact]
    public async Task TheEpoch_CountsTopLevelStatements_AndFreezesInsideOne()
    {
        var probe = new Probe();
        ScriptRunResult result = await RunProbed("""
            a = 1;
            for k = 1:3
                b = k; % probe: one epoch for all three passes
            end
            c = f();
            function y = f()
                y = 1; % probe: the same epoch as the call
            end
            """, probe);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        int[] top = probe.All.Where(seen => seen.Line is 1 or 2 or 5).Select(seen => seen.Epoch).ToArray();
        Assert.Equal(3, top.Distinct().Count());
        Assert.Single(probe.At(line: 3).Select(seen => seen.Epoch).Distinct());
        Assert.Equal(probe.At(line: 5)[0].Epoch, probe.At(line: 7)[0].Epoch);
    }

    // --- A loaded file that goes away ---------------------------------------------------------

    /// <summary>MATLAB's own words for a function it had and cannot read again.</summary>
    [Fact]
    public async Task ALoadedFile_ThatCannotBeReadAgain_ErrorsWithMatlabsWords()
    {
        string path = Path.Combine(_folder, "helper.m");
        File.WriteAllText(path, "function y = helper()\ny = 1;\nend\n");
        FileStream? held = null;
        try
        {
            var probe = new Probe();
            probe.BeforeLine(2, _ =>
            {
                File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));
                held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            });
            ScriptRunResult result = await RunProbed("""
                a = helper();
                b = helper();
                """, probe);

            Assert.False(result.Success);
            Assert.True((result.Message ?? "").Contains($"Previously accessible file \"{path}\" is now inaccessible."), result.Message);
        }
        finally
        {
            held?.Dispose();
        }
    }

    /// <summary>The recorded divergence: a deleted file falls through rather than keeping a dead binding.</summary>
    [Fact]
    public async Task ALoadedFile_ThatIsDeleted_FallsThroughToTheNextCandidate()
    {
        File.WriteAllText(Path.Combine(_folder, "helper.m"), "function y = helper()\ny = 1;\nend\n");
        ScriptRunResult result = await RunMatlab("""
            a = helper();
            delete('helper.m');
            b = helper();
            """);

        Assert.False(result.Success);
        Assert.DoesNotContain("inaccessible", result.Message);
        Assert.Contains("helper", result.Message);
    }

    // --- The probe -----------------------------------------------------------------------------

    /// <summary>
    /// A debug hook that reads the index before each statement. Reading the index is itself what
    /// refreshes it, so a probe placed before a change is also what pins the folder time for that
    /// statement.
    /// </summary>
    private sealed class Probe : IJgsDebugHook
    {
        public sealed record Seen(int Line, int Epoch, string[] Shadowing)
        {
            public bool ShadowsMean => Shadowing.Contains("mean");
        }

        private readonly Dictionary<int, Action<JgsFileIndex>> _before = new();

        public Interpreter? Interpreter { get; private set; }
        public JgsFileIndex? Index => Interpreter?.FunctionPath?.Index;
        public List<Seen> All { get; } = new();

        public List<Seen> At(int line) => All.Where(seen => seen.Line == line).ToList();

        /// <summary>Runs <paramref name="action"/> before the top-level statement on <paramref name="line"/>, before the index is read.</summary>
        public void BeforeLine(int line, Action<JgsFileIndex> action) => _before[line] = action;

        public void RunStarting(Interpreter interpreter, JgsEnvironment globals) => Interpreter = interpreter;

        public int? BeforeStatement(BlockExecution block, int index, JgsEnvironment env, int callDepth)
        {
            // A line number is only unique within the top level: a loaded file has its own line 2.
            Stmt statement = block.Statements[index];
            if (callDepth == 0 && _before.TryGetValue(statement.Line, out Action<JgsFileIndex>? action))
            {
                action(Index!);
            }

            All.Add(new Seen(statement.Line, Interpreter!.StatementEpoch,
                Index!.Shadowing.OrderBy(s => s, StringComparer.Ordinal).ToArray()));
            return null;
        }

        public void EnterBlock(BlockExecution block) { }
        public void ExitBlock() { }
        public void EnterFunction(
            FnStmt declaration, int callLine, JgsEnvironment local, JgsEnvironment callerFrame, string callerFile) { }
        public void ExitFunction() { }
    }
}
