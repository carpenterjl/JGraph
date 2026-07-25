using System.Xml.Linq;
using JGraph.Controls.Themes;
using Xunit;

namespace JGraph.Tests.Theming;

/// <summary>
/// The theme dictionaries define exactly the keys <see cref="ThemeKeys"/> declares, and every theme
/// defines all of them.
/// </summary>
/// <remarks>
/// This is the highest-value test in M32. A key present in Light and missing from Dark is invisible
/// until someone switches themes on that exact screen, where it degrades to a black-on-black label
/// or a resource-not-found trace nobody reads. Comparing the two key sets catches it at build time.
/// </remarks>
public sealed class ThemeKeyParityTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static TheoryData<string> ThemeFiles => new() { "Light.xaml", "Dark.xaml" };

    [Fact]
    public void EveryThemeDefinesExactlyTheDeclaredKeys()
    {
        HashSet<string> declared = [.. ThemeKeys.All];
        foreach (string file in new[] { "Light.xaml", "Dark.xaml" })
        {
            HashSet<string> defined = [.. KeysIn(file)];

            Assert.Empty(declared.Except(defined));  // declared but not themed
            Assert.Empty(defined.Except(declared));  // themed but not declared
        }
    }

    [Fact]
    public void LightAndDarkDefineTheSameKeys()
    {
        Assert.Equal(KeysIn("Light.xaml").Order(), KeysIn("Dark.xaml").Order());
    }

    [Theory]
    [MemberData(nameof(ThemeFiles))]
    public void ThemeCarriesItsOwnIdSentinel(string file)
    {
        // ThemeManager finds the one dictionary to replace by this key. Without it, applying a
        // theme would append a second one and the first would keep winning for half the keys.
        Assert.Contains(ThemeKeys.ThemeId, KeysIn(file));
    }

    [Fact]
    public void SharedStylesDoNotDefineTheSentinel()
    {
        // Shared.xaml is merged beside the theme and must not answer to the sentinel search, or
        // ThemeManager would replace the styles with a theme and lose them.
        Assert.DoesNotContain(ThemeKeys.ThemeId, KeysIn("Shared.xaml"));
    }

    [Theory]
    [MemberData(nameof(ThemeFiles))]
    public void ThemesDefineValuesOnly(string file)
    {
        // Styles belong in Shared.xaml. A Style in a theme dictionary is replaced wholesale on a
        // swap, so a control templated by one theme and not the other silently loses its template.
        Assert.DoesNotContain("Style", Load(file).Root!.Elements().Select(e => e.Name.LocalName));
    }

    private static IEnumerable<string> KeysIn(string file) =>
        Load(file).Root!.Elements()
            .Select(e => e.Attribute(Xaml + "Key")?.Value)
            .OfType<string>();

    private static XDocument Load(string file) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Themes", file));
}
