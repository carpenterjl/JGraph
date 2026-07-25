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
            LayoutSchema = ScriptWorkspaceStateFormat.CurrentLayoutSchema,
            WindowLeft = 120,
            WindowTop = 60,
            WindowWidth = 1280,
            WindowHeight = 800,
            WindowState = "Maximized",
        };

        string json = ScriptWorkspaceStateFormat.Serialize(state);
        ScriptWorkspaceStateDto? loaded = ScriptWorkspaceStateFormat.Deserialize(json);

        Assert.NotNull(loaded);
        Assert.Equal(state.RootPath, loaded.RootPath);
        Assert.Equal(ScriptWorkspaceStateFormat.CurrentLayoutSchema, loaded.LayoutSchema);
        Assert.Equal(120, loaded.WindowLeft);
        Assert.Equal(60, loaded.WindowTop);
        Assert.Equal(1280, loaded.WindowWidth);
        Assert.Equal(800, loaded.WindowHeight);
        Assert.Equal("Maximized", loaded.WindowState);
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

    [Fact]
    public void Deserialize_LoadsStateWrittenBeforeTheWindowFieldsExisted()
    {
        // A file from a build that predates M30 has no layoutSchema or window placement. It must
        // still load, and its layout must still be considered restorable — the panes it describes
        // have not changed, so throwing the user's arrangement away would be gratuitous.
        ScriptWorkspaceStateDto? loaded = ScriptWorkspaceStateFormat.Deserialize(
            $$"""
            {
              "format": "{{ScriptWorkspaceStateFormat.FormatTag}}",
              "formatVersion": 1,
              "rootPath": "C:\\work",
              "dockLayoutXml": "<LayoutRoot />"
            }
            """);

        Assert.NotNull(loaded);
        Assert.Equal(@"C:\work", loaded.RootPath);
        Assert.Equal(0, loaded.LayoutSchema);
        Assert.True(loaded.LayoutSchema >= ScriptWorkspaceStateFormat.MinimumCompatibleLayoutSchema);
        Assert.Null(loaded.WindowLeft);
        Assert.Null(loaded.WindowState);
    }

    [Fact]
    public void Deserialize_IgnoresFieldsAddedByANewerBuild()
    {
        // The reverse direction: an additive change must not need a version bump, so an unknown
        // member is skipped rather than rejected.
        ScriptWorkspaceStateDto? loaded = ScriptWorkspaceStateFormat.Deserialize(
            $$"""
            {
              "format": "{{ScriptWorkspaceStateFormat.FormatTag}}",
              "formatVersion": 1,
              "rootPath": "C:\\work",
              "somethingFromTheFuture": { "nested": [1, 2, 3] }
            }
            """);

        Assert.NotNull(loaded);
        Assert.Equal(@"C:\work", loaded.RootPath);
    }
}
