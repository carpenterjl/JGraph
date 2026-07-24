using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Startup;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Startup;

/// <summary>
/// Covers the shared non-interactive run: exit codes, where relative paths land, and what happens to
/// figures. Both executables go through this, so these are the semantics of `-batch` itself.
/// </summary>
[Collection("JG facade")]
public class BatchRunnerTests : IDisposable
{
    private static readonly IScriptEngine[] Engines = { new JgsScriptEngine() };

    private readonly RecordingScriptOutput _output = new();
    private readonly List<int> _shown = new();
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-batch-" + Guid.NewGuid().ToString("N"))).FullName;

    public BatchRunnerTests() => JG.Reset();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        JG.Reset();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    [Fact]
    public async Task SuccessfulStatement_ReturnsZeroAndWritesItsOutput()
    {
        int code = await RunAsync("let x = 1:10; disp(sum(x))");

        Assert.Equal(StartupExitCodes.Success, code);
        Assert.Contains("55", _output.NormalText);
        Assert.Empty(_output.Errors);
    }

    [Fact]
    public async Task FailedScript_ReturnsOneAndClosesWithASingleSummaryLine()
    {
        int code = await RunAsync("disp(nowhere)");

        Assert.Equal(StartupExitCodes.ScriptError, code);
        // The engine already wrote the diagnostic; the runner adds exactly one closing line.
        Assert.Contains("'nowhere' is not defined", _output.ErrorText);
        Assert.Single(_output.Errors, line => line.StartsWith("jgraph: script failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnrunnableFile_IsAUsageErrorNotAScriptError()
    {
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "hello");

        int code = await RunAsync("notes.txt");

        Assert.Equal(StartupExitCodes.UsageError, code);
        Assert.Contains("not a runnable script", _output.ErrorText);
    }

    [Fact]
    public async Task MissingStartDirectory_IsReportedBeforeAnythingRuns()
    {
        var options = new StartupOptions(
            StartupMode.Batch, "disp(1)", StartDirectory: Path.Combine(_directory, "nope"));

        int code = await BatchRunner.RunAsync(options, Engines, _output, Show);

        Assert.Equal(StartupExitCodes.UsageError, code);
        Assert.Contains("does not exist", _output.ErrorText);
        Assert.Empty(_output.Normal);
    }

    [Fact]
    public async Task Show_ReachesTheHostCallbackSoItCanSuppressOrDisplay()
    {
        int code = await RunAsync("plot([1, 2, 3]); show()");

        Assert.Equal(StartupExitCodes.Success, code);
        Assert.Equal(new[] { 1 }, _shown);
    }

    [Fact]
    public async Task RelativeWrites_LandInTheWorkingDirectory()
    {
        var files = new RecordingFigureFiles();

        int code = await RunAsync("plot([1, 2, 3]); exportfigure('trend.png')", files);

        Assert.Equal(StartupExitCodes.Success, code);
        Assert.Equal(Path.Combine(_directory, "trend.png"), files.Exported);
    }

    [Fact]
    public async Task ScriptRunByPath_FindsFilesSittingBesideIt()
    {
        // The shell's directory wins for writes and for names it can resolve, but a script run by
        // path must still find its own helpers — otherwise no script is portable.
        string scriptDirectory = Directory.CreateDirectory(Path.Combine(_directory, "scripts")).FullName;
        File.WriteAllText(Path.Combine(scriptDirectory, "helper.jgs"), "disp('from the helper')");
        File.WriteAllText(Path.Combine(scriptDirectory, "main.jgs"), "run('helper.jgs')");

        int code = await RunAsync(Path.Combine(scriptDirectory, "main.jgs"));

        Assert.Equal(StartupExitCodes.Success, code);
        Assert.Contains("from the helper", _output.NormalText);
    }

    [Fact]
    public async Task NoEngineForTheLanguage_IsAUsageError()
    {
        File.WriteAllText(Path.Combine(_directory, "script.csx"), "1 + 1");

        // Only the JGS engine is registered here, so a C# script cannot run.
        int code = await RunAsync("script.csx");

        Assert.Equal(StartupExitCodes.UsageError, code);
        Assert.Contains("C#", _output.ErrorText);
    }

    private Task<int> RunAsync(string statement, IScriptFigureFiles? files = null) =>
        BatchRunner.RunAsync(
            new StartupOptions(StartupMode.Batch, statement, StartDirectory: _directory),
            Engines,
            _output,
            Show,
            files);

    private void Show(int number, FigureModel figure)
    {
        Assert.NotNull(figure);
        _shown.Add(number);
    }

    private sealed class RecordingFigureFiles : IScriptFigureFiles
    {
        public string? Exported { get; private set; }

        public string? Saved { get; private set; }

        public void Save(FigureModel figure, string path) => Saved = path;

        public FigureModel Load(string path) => new();

        public void Export(FigureModel figure, string path) => Exported = path;
    }
}
