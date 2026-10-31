using JGraph.Scripting.Designer;
using JGraph.Scripting.Toolstrip;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W8d: what the Code View folds, its Go To list, the App Layout picture's scale, and what keeps the editor's undo.
/// </summary>
[Collection("JG facade")]
public sealed class AppDesignerCodeViewLeftoversTests
{
    private static string[] Lines(string text) => text.Split('\n');

    [Fact]
    public void TheGeneratedBlocksAreClosedByDefaultTheUsersAreNot()
    {
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("folds"), null);
        Assert.Null(document.AddStartup());
        document.InsertUserFunction("private");
        string text = document.CurrentText;
        string[] lines = Lines(text);
        IReadOnlyList<(int First, int Last)> folds = DesignCodeFolds.ClosedByDefault(text, document.Code);
        string[] heads = folds.Select(f => lines[f.First - 1].Trim()).ToArray();

        Assert.Contains("properties (Access = public)", heads);
        Assert.Contains("function createComponents(app)", heads);
        Assert.Contains("function app = folds", heads);
        Assert.Contains("function delete(app)", heads);
        Assert.DoesNotContain(heads, static h => h.StartsWith("function startupFcn", StringComparison.Ordinal));
        Assert.DoesNotContain(heads, static h => h.StartsWith("function func", StringComparison.Ordinal));
        foreach ((int first, int last) in folds)
        {
            Assert.Equal("end", lines[last - 1].Trim());
            Assert.True(last > first);
        }
    }

    [Fact]
    public void AReadOnlyCallbackIsClosedByDefault()
    {
        DesignDocument document = DesignDocument.FromText(DesignResponsive.NewAppText(2, "reflow"), null);
        string text = document.CurrentText;
        string[] lines = Lines(text);
        Assert.Contains(DesignCodeFolds.ClosedByDefault(text, document.Code), f => lines[f.First - 1].Contains(DesignResponsive.Callback, StringComparison.Ordinal));
    }

    [Fact]
    public void APlainTextAppsSectionsAreClosedByDefault()
    {
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("sections"), null);
        string path = Path.Combine(Path.GetTempPath(), "jgw8d_" + Guid.NewGuid().ToString("N"), "sections.m");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            document.SaveAs(path);
            string text = document.CurrentText.Replace("\r\n", "\n", StringComparison.Ordinal);
            string[] lines = Lines(text);
            string[] heads = DesignCodeFolds.ClosedByDefault(text, document.Code).Select(f => lines[f.First - 1].Trim()).ToArray();
            Assert.Contains(heads, static h => h.StartsWith("<Components", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void GoToListsTheClassesFunctionsInCodeUnitOrder()
    {
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("zebra"), null);
        Assert.Null(document.AddStartup());
        document.InsertUserFunction("private");
        IReadOnlyList<string> names = DesignCodeFolds.GoToFunctions(document.Code, document.UserFunctions().Select(static f => f.Name));
        Assert.Equal(["createComponents", "delete", "func", "startupFcn", "zebra"], names);

        IReadOnlyList<ToolstripPopupEntry> entries = DesignToolstrip.GoToEntries(names);
        Assert.Equal("func", DesignToolstrip.GoToFunctionOf(entries[2].Tag));
        Assert.Null(DesignToolstrip.GoToFunctionOf("something.else"));
    }

    [Theory]
    [InlineData(300, 220, 6, 640, 480, 0.4)] // 288 / 640 = .45 across, 208 / 480 = .433 down
    [InlineData(1000, 1000, 6, 640, 480, 1)] // never above 1
    [InlineData(100, 100, 6, 640, 480, 0.4)] // never below .4
    [InlineData(652, 492, 6, 640, 480, 1)]
    public void TheAppLayoutScaleFollowsTheCodeViewsRule(double paneWidth, double paneHeight, double margin, double width, double height, double scale)
    {
        double got = DesignZoom.ThumbnailScale(paneWidth, paneHeight, margin, width, height);
        Assert.True(scale == 0.4 ? got >= 0.4 && got < 0.5 : got == scale, $"{got}");
    }

    [Fact]
    public void AddFunctionAndAddPropertyKeepTheEditorsUndoOnce()
    {
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("app1"), null);
        Assert.False(document.TakeKeepEditorUndo());
        document.InsertUserFunction("private");
        Assert.True(document.TakeKeepEditorUndo());
        Assert.False(document.TakeKeepEditorUndo());
        document.InsertUserProperty("private");
        Assert.True(document.TakeKeepEditorUndo());
        document.SetSingleInstance(true);
        Assert.False(document.TakeKeepEditorUndo());
    }
}
