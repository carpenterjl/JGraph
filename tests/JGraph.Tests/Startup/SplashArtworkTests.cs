using System.IO;
using JGraph.Scripting.Startup;
using Xunit;

namespace JGraph.Tests.Startup;

public class SplashArtworkTests
{
    private static readonly string AppData = Path.Combine("C:", "users", "me", "AppData", "JGraph");
    private static readonly string ExeDir = Path.Combine("C:", "program files", "JGraph");

    private static Func<string, bool> Present(params string[] paths) =>
        path => paths.Contains(path, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Find_ReturnsNullWhenThereIsNoArtwork() =>
        Assert.Null(SplashArtwork.Find(AppData, ExeDir, _ => false));

    [Fact]
    public void Find_PrefersTheUsersOwnFolderOverTheDeployments()
    {
        // An update that ships new branding must not silently replace the user's chosen image.
        string mine = Path.Combine(AppData, "splash.png");
        string theirs = Path.Combine(ExeDir, "splash.png");

        Assert.Equal(mine, SplashArtwork.Find(AppData, ExeDir, Present(mine, theirs)));
    }

    [Fact]
    public void Find_FallsBackToTheFolderBesideTheExecutable()
    {
        string theirs = Path.Combine(ExeDir, "splash.png");
        Assert.Equal(theirs, SplashArtwork.Find(AppData, ExeDir, Present(theirs)));
    }

    [Fact]
    public void Find_ProbesExtensionsInPreferenceOrder()
    {
        string png = Path.Combine(AppData, "splash.png");
        string bmp = Path.Combine(AppData, "splash.bmp");

        Assert.Equal(png, SplashArtwork.Find(AppData, ExeDir, Present(bmp, png)));
        Assert.Equal(bmp, SplashArtwork.Find(AppData, ExeDir, Present(bmp)));
    }

    [Fact]
    public void Find_TriesEveryDocumentedExtension()
    {
        foreach (string extension in SplashArtwork.Extensions)
        {
            string candidate = Path.Combine(AppData, SplashArtwork.BaseName + extension);
            Assert.Equal(candidate, SplashArtwork.Find(AppData, ExeDir, Present(candidate)));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Find_SkipsADirectoryItWasNotGiven(string? appData)
    {
        // The per-user folder may not exist yet on a first run; that must not stop the probe.
        string theirs = Path.Combine(ExeDir, "splash.png");
        Assert.Equal(theirs, SplashArtwork.Find(appData, ExeDir, Present(theirs)));
    }

    [Fact]
    public void Find_ReturnsNullWhenGivenNoDirectoriesAtAll() =>
        Assert.Null(SplashArtwork.Find(null, null, _ => true));
}
