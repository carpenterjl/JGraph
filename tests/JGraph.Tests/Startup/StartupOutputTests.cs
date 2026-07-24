using JGraph.Scripting;
using JGraph.Scripting.Startup;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Startup;

/// <summary>Covers the `-logfile` sinks: the file writer and the tee that feeds it.</summary>
public class StartupOutputTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-log-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
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
    public void FileOutput_WritesLinesAndTagsErrors()
    {
        string path = Path.Combine(_directory, "run.log");

        using (var log = new FileScriptOutput(path))
        {
            log.WriteLine("hello");
            log.WriteError("boom");
        }

        string text = File.ReadAllText(path);
        Assert.Contains("hello", text);
        Assert.Contains("[error] boom", text);
    }

    [Fact]
    public void FileOutput_IsFlushedAsItGoesSoAnAbortedRunStillHasALog()
    {
        string path = Path.Combine(_directory, "flushed.log");

        var log = new FileScriptOutput(path);
        log.WriteLine("written before any dispose");

        // Read while the writer is still open — the log must already be on disk.
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var text = new StreamReader(reader);
        Assert.Contains("written before any dispose", text.ReadToEnd());
        log.Dispose();
    }

    [Fact]
    public void FileOutput_CreatesMissingDirectories()
    {
        string path = Path.Combine(_directory, "nested", "deeper", "run.log");

        using (var log = new FileScriptOutput(path))
        {
            log.WriteLine("hello");
        }

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Tee_SendsEveryStreamToEverySink()
    {
        var first = new RecordingScriptOutput();
        var second = new RecordingScriptOutput();

        using (var tee = new TeeScriptOutput(first, second))
        {
            tee.Write("a");
            tee.WriteLine("b");
            tee.WriteError("c");
        }

        foreach (RecordingScriptOutput sink in new[] { first, second })
        {
            Assert.Equal("ab\n", sink.NormalText);
            Assert.Equal("c", sink.ErrorText);
        }
    }

    [Fact]
    public void Tee_DisposesTheSinksThatOwnAResource()
    {
        string path = Path.Combine(_directory, "teed.log");
        var console = new RecordingScriptOutput();
        var file = new FileScriptOutput(path);

        using (var tee = new TeeScriptOutput(console, file))
        {
            tee.WriteLine("shared");
        }

        // The file handle must be released — reopening for write proves it.
        using var reopened = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        Assert.Equal("shared\n", console.NormalText);
    }

    [Fact]
    public void HelpText_DocumentsEveryFlagTheParserAccepts()
    {
        string usage = StartupHelp.UsageText;

        foreach (string flag in new[] { "-batch", "-r ", "-logfile", "-showfigures", "-sd", "-help" })
        {
            Assert.Contains(flag, usage);
        }
    }

    [Fact]
    public void FindGuide_LocatesTheHtmlGuideBesideTheExecutable()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "docs"));
        string guide = Path.Combine(_directory, "docs", StartupHelp.GuideFileName);
        File.WriteAllText(guide, "<html></html>");

        Assert.Equal(guide, StartupHelp.FindGuide(_directory));

        string empty = Directory.CreateDirectory(Path.Combine(_directory, "empty")).FullName;
        Assert.Null(StartupHelp.FindGuide(empty));
    }

    [Fact]
    public void ConsoleOutput_IsShared()
    {
        Assert.IsAssignableFrom<IScriptOutput>(ConsoleScriptOutput.Instance);
        Assert.Same(ConsoleScriptOutput.Instance, ConsoleScriptOutput.Instance);
    }
}
