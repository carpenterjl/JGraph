using JGraph.Serialization.Workspace;
using Xunit;

namespace JGraph.Tests.Serialization;

public class ScriptWorkspaceStateFormatTests
{
    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var state = new ScriptWorkspaceStateDto
        {
            RootPath = @"C:\work\signals",
            OpenFiles = { @"C:\work\signals\main.jgs", @"C:\work\signals\lib\util.jgs" },
            ActiveFile = @"C:\work\signals\main.jgs",
            Breakpoints =
            {
                [@"C:\work\signals\main.jgs"] = new List<int> { 3, 12 },
            },
            DockLayoutXml = "<LayoutRoot><RootPanel /></LayoutRoot>",
        };

        string json = ScriptWorkspaceStateFormat.Serialize(state);
        ScriptWorkspaceStateDto? loaded = ScriptWorkspaceStateFormat.Deserialize(json);

        Assert.NotNull(loaded);
        Assert.Equal(state.RootPath, loaded.RootPath);
        Assert.Equal(state.OpenFiles, loaded.OpenFiles);
        Assert.Equal(state.ActiveFile, loaded.ActiveFile);
        Assert.Equal(new[] { 3, 12 }, loaded.Breakpoints[@"C:\work\signals\main.jgs"]);
        Assert.Equal(state.DockLayoutXml, loaded.DockLayoutXml);
        Assert.Equal(ScriptWorkspaceStateFormat.FormatTag, loaded.Format);
        Assert.Equal(ScriptWorkspaceStateFormat.CurrentVersion, loaded.FormatVersion);
    }

    [Fact]
    public void Deserialize_IsForgiving_ReturningNullInsteadOfThrowing()
    {
        Assert.Null(ScriptWorkspaceStateFormat.Deserialize("not json at all"));
        Assert.Null(ScriptWorkspaceStateFormat.Deserialize("{}"));                       // missing tag
        Assert.Null(ScriptWorkspaceStateFormat.Deserialize(
            """{ "format": "something-else", "formatVersion": 1 }"""));                  // wrong tag
        Assert.Null(ScriptWorkspaceStateFormat.Deserialize(
            $$"""{ "format": "{{ScriptWorkspaceStateFormat.FormatTag}}", "formatVersion": 999 }""")); // newer
    }

    [Fact]
    public void Deserialize_AcceptsAMinimalCurrentDocument()
    {
        ScriptWorkspaceStateDto? loaded = ScriptWorkspaceStateFormat.Deserialize(
            $$"""{ "format": "{{ScriptWorkspaceStateFormat.FormatTag}}", "formatVersion": 1 }""");

        Assert.NotNull(loaded);
        Assert.Null(loaded.RootPath);
        Assert.Empty(loaded.OpenFiles);
        Assert.Empty(loaded.Breakpoints);
    }
}
