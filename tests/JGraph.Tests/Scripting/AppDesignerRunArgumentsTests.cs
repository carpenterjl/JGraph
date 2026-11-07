using System.IO.Compression;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Designer;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The App Designer window plan's stage W8b: an app's argument sets in the app and its file, Run with arguments in a session, an argument's error told from a startup function's,
/// an app in a package, and the thumbnail Run takes written into either format.
/// </summary>
[Collection("JG facade")]
public sealed class AppDesignerRunArgumentsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgw8b_" + Guid.NewGuid().ToString("N"));

    public AppDesignerRunArgumentsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A folder under the temp folder; the next clean takes it.
        }
    }

    private string Saved(string name = "RunApp")
    {
        string path = Path.Combine(_folder, name + ".mlapp");
        JgsMlapp.WriteApp(path, AppDesignerDocument.NewAppText(name));
        return path;
    }

    private static string Pixel()
    {
        return "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
    }

    [Fact]
    public void ANewAppRunsWithNoArguments()
    {
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("app1"), null);
        Assert.Equal([string.Empty], document.RunArguments.All);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void ANewSetModifiesTheAppAndGoesIntoItsModel()
    {
        string path = Saved();
        DesignDocument document = DesignDocument.Open(path);
        document.UseRunArguments("1, 2");
        Assert.True(document.IsDirty);
        document.Save();
        Assert.False(document.IsDirty);
        Assert.Equal(["1, 2", string.Empty], JgsMlapp.ReadRunConfigurations(path));
        Assert.Equal(["1, 2", string.Empty], DesignDocument.Open(path).RunArguments.All);
    }

    [Fact]
    public void TheEmptySetAloneIsNotKept()
    {
        string path = Saved();
        DesignDocument document = DesignDocument.Open(path);
        document.UseRunArguments("1");
        document.Save();
        Assert.NotNull(JgsMlapp.ReadRunConfigurations(path));

        document.RemoveRunArguments("1");
        document.Save();
        Assert.Null(JgsMlapp.ReadRunConfigurations(path));
    }

    [Fact]
    public void TakingASetOutIsOneStep()
    {
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("app1"), null);
        document.UseRunArguments("1");
        document.UseRunArguments("2");
        document.RemoveRunArguments("1");
        Assert.Equal(["2", string.Empty], document.RunArguments.All);

        document.Undo();
        Assert.Equal(["1", "2", string.Empty], document.RunArguments.All); // Undo makes it the most recent
        document.Redo();
        Assert.Equal(["2", string.Empty], document.RunArguments.All);
    }

    [Fact]
    public void AnAppThatTakesNoArgumentsRunsWithNone()
    {
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("app1"), null);
        Assert.Null(document.SetInputParameters("a"));
        document.UseRunArguments("5");
        Assert.Null(document.SetInputParameters(string.Empty));
        Assert.Equal(string.Empty, document.RunArguments.MostRecent);
    }

    [Fact]
    public void APlainTextAppKeepsNoSets()
    {
        string path = Path.Combine(_folder, "PlainRun.m");
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("PlainRun"), null);
        document.SaveAs(path);
        document.UseRunArguments("1");
        Assert.True(document.IsDirty); // the peer node's change marks it, in either format
        document.Save();
        Assert.DoesNotContain("runConfigurations", File.ReadAllText(path));
        Assert.Equal([string.Empty], DesignDocument.Open(path).RunArguments.All);
    }

    [Fact]
    public void TheSetsGoAcrossSaveAs()
    {
        string path = Saved("OneApp");
        DesignDocument document = DesignDocument.Open(path);
        document.UseRunArguments("7");
        document.Save();
        string other = Path.Combine(_folder, "OtherApp.mlapp");
        document.SaveAs(other);
        Assert.Equal(["7", string.Empty], JgsMlapp.ReadRunConfigurations(other));
    }

    [Fact]
    public void AnAppInPackagesIsRunByItsWholeName()
    {
        Assert.Equal(("app", _folder), DesignRun.CommandOf(Path.Combine(_folder, "app.mlapp")));
        Assert.Equal(("one.two.app", _folder), DesignRun.CommandOf(Path.Combine(_folder, "+one", "+two", "app.mlapp")));
    }

    /// <summary>
    /// R2025b's <c>PathUtil.getAppRunCommandFromFileName</c> and <c>getPathToApp</c> over every path
    /// their comment lists and over .mlapp paths, as the probe <c>p6_app</c> measured them (packages
    /// and class folders plan, stage P6, decision PD4).
    /// </summary>
    [Theory]
    [InlineData(@"C:\work\+foo\@bar\bar.m", "foo.bar", @"C:\work")]
    [InlineData(@"C:\work\+foo\+baz\@bar\bar.m", "foo.baz.bar", @"C:\work")]
    [InlineData(@"C:\work\+foo\+baz\@bar\bat.m", "foo.baz.bar.bat", @"C:\work")]
    [InlineData(@"C:\work\+foo\baz.m", "foo.baz", @"C:\work")]
    [InlineData(@"C:\work\@bar\bar.m", "bar", @"C:\work")]
    [InlineData(@"C:\work\@bar\method.m", "bar.method", @"C:\work")]
    [InlineData(@"C:\work\@bar\blah\foo.m", "foo", @"C:\work\@bar\blah")]
    [InlineData(@"C:\+work\blah\foo.m", "foo", @"C:\+work\blah")]
    [InlineData(@"C:\work\foo.m", "foo", @"C:\work")]
    [InlineData(@"C:\work\+foo\bar.mlapp", "foo.bar", @"C:\work")]
    [InlineData(@"C:\work\@foo\foo.mlapp", "foo", @"C:\work")]
    [InlineData(@"C:\work\+foo\+bar\@baz\baz.mlapp", "foo.bar.baz", @"C:\work")]
    [InlineData(@"C:\work\@foo\other.mlapp", "foo.other", @"C:\work")]
    [InlineData(@"C:\work\+foo\private\bar.mlapp", "bar", @"C:\work\+foo\private")]
    [InlineData(@"C:\work\+foo\sub\bar.mlapp", "bar", @"C:\work\+foo\sub")]
    [InlineData(@"C:\work\+\bar.mlapp", ".bar", @"C:\work")]
    [InlineData(@"C:\work\+a+b\c.mlapp", "ab.c", @"C:\work")]
    public void TheRunCommandAndItsFolderAreR2025bs(string file, string command, string folder)
    {
        Assert.Equal((command, folder), DesignRun.CommandOf(file));
    }

    [Fact]
    public void AThumbnailNeedsASaveAndAnAutomaticMode()
    {
        string path = Saved();
        DesignDocument document = DesignDocument.Open(path);
        Assert.False(document.WantsThumbnailCapture); // as opened, HasRunSinceLastSave is true
        document.SetHelpText("Text", string.Empty);
        document.Save();
        Assert.True(document.WantsThumbnailCapture);
        document.RanSinceSave();
        Assert.False(document.WantsThumbnailCapture);

        document.SetHelpText("More", string.Empty);
        document.SetAppDetails(document.AppDetailsValues with { Mode = DesignScreenshotMode.Manual }, null, null);
        document.Save();
        Assert.False(document.WantsThumbnailCapture);
    }

    [Fact]
    public void RunsPictureGoesIntoAPackageWithoutModifyingIt()
    {
        string path = Saved();
        DesignDocument document = DesignDocument.Open(path);
        document.TakeThumbnail(Convert.FromBase64String(Pixel()));
        Assert.False(document.IsDirty);
        using ZipArchive package = ZipFile.OpenRead(path);
        Assert.NotNull(package.GetEntry("metadata/appScreenshot.png"));
    }

    [Fact]
    public void RunsPictureGoesIntoAnAutomaticPlainTextThumbnailAlone()
    {
        string path = Path.Combine(_folder, "PlainShot.m");
        DesignDocument document = DesignDocument.FromText(AppDesignerDocument.NewAppText("PlainShot"), null);
        document.SaveAs(path);
        document.TakeThumbnail(Convert.FromBase64String(Pixel()));
        Assert.Contains("<Thumbnail autoCapture='true'>data:image/jpg;base64,", File.ReadAllText(path));
        Assert.False(document.IsDirty);

        const string manual = "<Thumbnail autoCapture='false'>x</Thumbnail>";
        Assert.Equal(manual, DesignPlainText.WithAutoThumbnail(manual, "data:y"));
    }

    [Fact]
    public async Task RunTakesTheArgumentsAndAnArgumentsErrorIsToldApart()
    {
        JG.Reset();
        var output = new RecordingScriptOutput();
        JgsReplSession runner = Assert.IsType<JgsReplSession>(((IScriptRepl)new MatlabScriptEngine()).CreateSession(new ScriptContext(output, (_, _) => { }, _folder)));
        await using (runner.ConfigureAwait(false))
        {
        string path = WithStartup("ArgApp");

        // The arguments reach the startup function, evaluated in the base workspace.
        ScriptRunResult ran = await runner.RunAppAsync(path, false, "21 * 2", false, CancellationToken.None);
        Assert.True(ran.Success, ran.Message);

        // Evaluating one fails, not in the startup function: the arguments' fault.
        ScriptRunResult undefined = await runner.RunAppAsync(path, false, "notDefinedAnywhere", false, CancellationToken.None);
        Assert.False(undefined.Success);
        Assert.Equal(DesignRunOutcome.ArgumentError, undefined.ErrorIdentifier);

        // Too few: the app's own error, under App Designer's words.
        ScriptRunResult few = await runner.RunAppAsync(path, false, string.Empty, false, CancellationToken.None);
        Assert.False(few.Success);
        Assert.Equal("MATLAB:appdesigner:appdesigner:TooFewAppArgumentsError", few.ErrorIdentifier);
        }
    }

    private string WithStartup(string name)
    {
        string path = Saved(name);
        DesignDocument document = DesignDocument.Open(path);
        Assert.Null(document.SetInputParameters("x"));
        string empty = "        function startupFcn(app, x)\n            \n        end";
        Assert.Contains(empty, document.CurrentText, StringComparison.Ordinal);
        Assert.True(document.ApplyEditedText(document.CurrentText.Replace(empty,
            "        function startupFcn(app, x)\n            app.UIFigure.Name = sprintf('%d', x);\n        end", StringComparison.Ordinal)));
        document.Save();
        return path;
    }
}
