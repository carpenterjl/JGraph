using JGraph.Scripting.Startup;
using Xunit;

namespace JGraph.Tests.Startup;

/// <summary>
/// Covers the "is this a statement or a file?" rule that both `-batch` and `-r` depend on.
/// </summary>
public class StartupStatementTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-startup-" + Guid.NewGuid().ToString("N"))).FullName;

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
    public void InlineSource_IsJgsAndHasNoSourceFile()
    {
        ResolvedStatement resolved = StartupStatement.Resolve("x = 1:10; disp(sum(x))", _directory);

        Assert.Null(resolved.Error);
        Assert.Equal("JGS", resolved.Language);
        Assert.Equal("x = 1:10; disp(sum(x))", resolved.Code);
        Assert.Null(resolved.SourcePath);
    }

    [Theory]
    [InlineData("analysis.jgs", "JGS")]
    [InlineData("analysis.csx", "C#")]
    [InlineData("analysis.py", "Python")]
    public void ExistingFile_IsReadAndItsExtensionPicksTheLanguage(string name, string language)
    {
        Write(name, "disp(1)");

        ResolvedStatement resolved = StartupStatement.Resolve(name, _directory);

        Assert.Null(resolved.Error);
        Assert.Equal(language, resolved.Language);
        Assert.Equal("disp(1)", resolved.Code);
        Assert.Equal(_directory, resolved.SourceDirectory);
    }

    [Fact]
    public void AbsolutePath_Resolves()
    {
        string path = Write("absolute.jgs", "disp(2)");

        ResolvedStatement resolved = StartupStatement.Resolve(path, "C:\\definitely-not-here");

        Assert.Null(resolved.Error);
        Assert.Equal("disp(2)", resolved.Code);
    }

    [Fact]
    public void NonExistentPathLikeString_FallsBackToJgsSource()
    {
        // "run('missing.jgs')" is valid JGS; only an existing file short-circuits to file mode.
        ResolvedStatement resolved = StartupStatement.Resolve("missing.jgs", _directory);

        Assert.Null(resolved.Error);
        Assert.Equal("JGS", resolved.Language);
        Assert.Null(resolved.SourcePath);
    }

    [Fact]
    public void ExistingFileWithNoEngine_IsAnErrorRatherThanASecondGuess()
    {
        Write("notes.txt", "hello");

        ResolvedStatement resolved = StartupStatement.Resolve("notes.txt", _directory);

        Assert.NotNull(resolved.Error);
        Assert.Contains("not a runnable script", resolved.Error);
    }

    [Fact]
    public void EmptyStatement_IsAnError()
    {
        Assert.NotNull(StartupStatement.Resolve("   ", _directory).Error);
        Assert.NotNull(StartupStatement.Resolve(null, _directory).Error);
    }

    [Fact]
    public void SourceWithPathIllegalCharacters_IsTreatedAsSourceNotAProbeFailure()
    {
        ResolvedStatement resolved = StartupStatement.Resolve("disp(\"a|b\") % <>", _directory);

        Assert.Null(resolved.Error);
        Assert.Equal("JGS", resolved.Language);
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }
}
