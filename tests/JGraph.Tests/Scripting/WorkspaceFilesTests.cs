using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

public class WorkspaceFilesTests
{
    [Theory]
    [InlineData("readings.csv")]
    [InlineData("readings.tsv")]
    [InlineData(@"C:\work\Book1.XLSX")]   // extensions are matched case-insensitively
    public void Classify_SendsTabularFilesToTheDataViewer(string path) =>
        Assert.Equal(WorkspaceFileKind.Data, WorkspaceFiles.Classify(path));

    [Fact]
    public void Classify_SendsSavedFiguresToAFigureWindow() =>
        Assert.Equal(WorkspaceFileKind.Figure, WorkspaceFiles.Classify(@"C:\work\trend.graph"));

    [Theory]
    [InlineData("main.jgs")]
    [InlineData("analysis.m")]
    [InlineData("helper.csx")]
    [InlineData("helper.cs")]
    [InlineData("plot.py")]
    [InlineData("notes.txt")]
    [InlineData("README.md")]
    [InlineData("config.json")]
    public void Classify_OpensScriptsAndTextInAnEditorTab(string path) =>
        Assert.Equal(WorkspaceFileKind.Document, WorkspaceFiles.Classify(path));

    [Theory]
    [InlineData("photo.png")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    public void Classify_ReportsAnythingElseAsUnsupported(string path) =>
        Assert.Equal(WorkspaceFileKind.Unsupported, WorkspaceFiles.Classify(path));
}
