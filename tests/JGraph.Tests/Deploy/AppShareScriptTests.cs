using JGraph.Deploy;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// The scripts App Designer's Share windows run (App Designer window plan W12): what R2025b's
/// <c>deployAppDesignerProject</c> builders wrote in the W0 probe <c>w0_share</c>, to the character, and the
/// one correction - App Details' text quoted so that a line break or a double quote no longer breaks the script.
/// </summary>
public class AppShareScriptTests
{
    private const string Share = @"C:\share";
    private const string App = Share + @"\AcceptForm.mlapp";

    private static readonly AppShareDetails Details = new("Accept Form", "2.1", "W0 Probe", "A form that adds or multiplies.", "One line.");

    [Fact]
    public void TheStandaloneScriptIsR2025bsWithoutFilesOrImages()
    {
        string expected = string.Join("\n",
        [
            "buildOpts=compiler.build.StandaloneApplicationOptions(\"C:\\share\\AcceptForm.mlapp\");",
            "buildOpts.OutputDir = \"C:\\share\\release\\build\";",
            "buildOpts.Verbose = true;",
            "",
            "buildOpts.ExecutableVersion = \"2.1\";",
            "buildResult = compiler.build.standaloneWindowsApplication(buildOpts);",
            "",
            "",
            "",
            "packageOpts = compiler.package.InstallerOptions(buildResult);",
            "packageOpts.OutputDir =\"C:\\share\\release\\package\";",
            "packageOpts.Verbose = true;",
            "packageOpts.ApplicationName = \"Accept Form\";",
            "packageOpts.Version = \"2.1\";",
            "packageOpts.AuthorName = \"W0 Probe\";",
            "packageOpts.Summary = \"A form that adds or multiplies.\";",
            "packageOpts.Description = \"One line.\";",
            "compiler.package.installer(buildResult, \"Options\", packageOpts);",
        ]);
        Assert.Equal(expected, AppShareScript.Standalone(App, [], null, null, Details));
    }

    [Fact]
    public void TheStandaloneBuildTakesTheFilesAndImages()
    {
        string expected = string.Join("\n",
        [
            "buildOpts=compiler.build.StandaloneApplicationOptions(\"C:\\share\\AcceptForm.mlapp\");",
            "buildOpts.AdditionalFiles = [\"C:\\share\\data.txt\"];",
            "buildOpts.OutputDir = \"C:\\share\\release\\build\";",
            "buildOpts.Verbose = true;",
            "",
            "buildOpts.ExecutableSplashScreen=\"C:\\share\\splash.png\";",
            "buildOpts.ExecutableIcon=\"C:\\share\\icon.png\";",
            "buildOpts.ExecutableVersion = \"2.1\";",
            "buildResult = compiler.build.standaloneWindowsApplication(buildOpts);",
            "",
            "",
        ]);
        Assert.Equal(expected, AppShareScript.StandaloneBuild(App, [Share + @"\data.txt"], Share + @"\splash.png", Share + @"\icon.png", Details));
        Assert.Contains("buildOpts.AdditionalFiles = [\"C:\\share\\data.txt\", \"C:\\share\\more\"];", AppShareScript.StandaloneBuild(App, [Share + @"\data.txt", Share + @"\more"], null, null, Details));
    }

    [Fact]
    public void TheWebAppScriptIsR2025bs()
    {
        string expected = string.Join("\n",
        [
            "buildOpts=compiler.build.WebAppArchiveOptions(\"C:\\share\\AcceptForm.mlapp\");",
            "buildOpts.AdditionalFiles = [\"C:\\share\\data.txt\"];",
            "buildOpts.OutputDir = \"C:\\share\\release\\build\";",
            "buildOpts.Verbose = true;",
            "",
            "buildResult = compiler.build.webAppArchive(buildOpts);",
            "",
            "",
        ]);
        Assert.Equal(expected, AppShareScript.WebApp(App, [Share + @"\data.txt"]));
    }

    [Fact]
    public void JGraphsRuntimeChoiceAddsOneLineOnlyOffTheDefault()
    {
        Assert.DoesNotContain("RuntimeDelivery", AppShareScript.StandalonePackage(App, Details));
        Assert.Contains("packageOpts.RuntimeDelivery = \"none\";\ncompiler.package.installer", AppShareScript.StandalonePackage(App, Details, RuntimeDeliveryChoice.None));
        Assert.Contains("packageOpts.RuntimeDelivery = \"installer\";", AppShareScript.StandalonePackage(App, Details, RuntimeDeliveryChoice.Installer));
    }

    [Fact]
    public void QuotedMendsWhatBrokeR2025bsScript()
    {
        // R2025b: a line break fails with MATLAB:m_unterminated_string_literal, a double quote with MATLAB:m_missing_operator; an apostrophe is fine.
        Assert.Equal("\"Line one.\" + newline + \"Line two.\"", AppShareScript.Quoted("Line one.\nLine two."));
        Assert.Equal("\"Line one.\" + newline + \"Line two.\"", AppShareScript.Quoted("Line one.\r\nLine two."));
        Assert.Equal("\"He said \"\"hi\"\"\"", AppShareScript.Quoted("He said \"hi\""));
        Assert.Equal("\"it's\"", AppShareScript.Quoted("it's"));
        Assert.Equal("\"\"", AppShareScript.Quoted(""));
        Assert.Contains("packageOpts.Description = \"Line one.\" + newline + \"Line two.\";", AppShareScript.StandalonePackage(App, Details with { Description = "Line one.\nLine two." }));
    }

    [Fact]
    public void TheOutputsGoBesideTheApp()
    {
        Assert.Equal(@"C:\share\release", AppShareScript.OutputFolder(App));
        Assert.Equal(@"C:\share\release\build", AppShareScript.BuildFolder(App));
        Assert.Equal(@"C:\share\release\package", AppShareScript.PackageFolder(App));
    }
}
