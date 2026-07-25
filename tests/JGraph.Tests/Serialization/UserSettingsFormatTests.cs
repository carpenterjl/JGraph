using JGraph.Serialization.Settings;
using Xunit;

namespace JGraph.Tests.Serialization;

/// <summary>
/// M29.T1: the user-settings format. It round-trips every field and, most importantly, loads
/// forgivingly — a malformed, mistagged, or newer file falls back to defaults rather than failing the
/// application at startup.
/// </summary>
public class UserSettingsFormatTests
{
    [Fact]
    public void RoundTripsEveryField()
    {
        var settings = new UserSettingsDto
        {
            JgsOptionalLet = true,
            JgsIndexBase = 1,
            DefaultScriptDirectory = @"C:\work\scripts",
            DefaultFigureTheme = "Dark",
            DisabledPlugins = { "Acme.Plugins.Noisy", "Acme.Plugins.Legacy" },
            DefaultNewScriptLanguage = "MATLAB",
            AppTheme = "dark",
            LinkFigureThemeToAppTheme = true,
        };

        UserSettingsDto? loaded = UserSettingsFormat.Deserialize(UserSettingsFormat.Serialize(settings));

        Assert.NotNull(loaded);
        Assert.True(loaded.JgsOptionalLet);
        Assert.Equal(1, loaded.JgsIndexBase);
        Assert.Equal(@"C:\work\scripts", loaded.DefaultScriptDirectory);
        Assert.Equal("Dark", loaded.DefaultFigureTheme);
        Assert.Equal(new[] { "Acme.Plugins.Noisy", "Acme.Plugins.Legacy" }, loaded.DisabledPlugins);
        Assert.Equal("MATLAB", loaded.DefaultNewScriptLanguage);
        Assert.Equal("dark", loaded.AppTheme);
        Assert.True(loaded.LinkFigureThemeToAppTheme);
    }

    [Fact]
    public void AppearanceDefaultsToLightAndAnUnlinkedFigureTheme()
    {
        // M32 added these two fields without bumping CurrentVersion — additive members take their
        // initializers, so a settings file written before M32 must still load with the app on its
        // default theme and the figure link off. Bumping instead would make new files unreadable by
        // an older build and silently reset every preference on a downgrade.
        UserSettingsDto? loaded = UserSettingsFormat.Deserialize(
            "{ \"format\": \"jgraph-settings\", \"formatVersion\": 1, \"defaultFigureTheme\": \"IEEE\" }");

        Assert.NotNull(loaded);
        Assert.Equal("IEEE", loaded.DefaultFigureTheme);
        Assert.Null(loaded.AppTheme);
        Assert.False(loaded.LinkFigureThemeToAppTheme);
    }

    [Fact]
    public void SerializeStampsTheTagAndVersion()
    {
        string json = UserSettingsFormat.Serialize(new UserSettingsDto());

        Assert.Contains(UserSettingsFormat.FormatTag, json, StringComparison.Ordinal);
        Assert.Contains("\"formatVersion\": 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultsAreTheShippedLanguage()
    {
        UserSettingsDto? loaded = UserSettingsFormat.Deserialize(UserSettingsFormat.Serialize(new UserSettingsDto()));

        Assert.NotNull(loaded);
        Assert.False(loaded.JgsOptionalLet);
        Assert.Equal(0, loaded.JgsIndexBase);
        Assert.Null(loaded.DefaultScriptDirectory);
        Assert.Empty(loaded.DisabledPlugins);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ \"format\": \"something-else\", \"formatVersion\": 1 }")]
    [InlineData("{ \"format\": \"jgraph-settings\", \"formatVersion\": 999 }")]
    public void MalformedOrForeignOrNewer_LoadsAsNull(string json)
    {
        Assert.Null(UserSettingsFormat.Deserialize(json));
    }

    [Fact]
    public void AnOlderFileMissingFields_LoadsWithDefaultsForThem()
    {
        // A v1 file that only set one preference: everything else must fall back cleanly.
        UserSettingsDto? loaded = UserSettingsFormat.Deserialize(
            "{ \"format\": \"jgraph-settings\", \"formatVersion\": 1, \"jgsIndexBase\": 1 }");

        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.JgsIndexBase);
        Assert.False(loaded.JgsOptionalLet);
        Assert.NotNull(loaded.DisabledPlugins); // the collection is never null
    }
}
