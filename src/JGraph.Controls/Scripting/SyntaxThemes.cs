using System.IO;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace JGraph.Controls.Scripting;

/// <summary>
/// Resolves the syntax-highlighting definition to use for a language under the theme in force.
/// </summary>
/// <remarks>
/// <para>
/// JGraph authors the JGS and MATLAB definitions, so those simply exist twice — once per
/// <see cref="SyntaxPalette"/> — and the right one is looked up by name.
/// </para>
/// <para>
/// C# and Python come from AvalonEdit and are tuned for a white background: their keyword blue is
/// <c>#0000FF</c>, which on a near-black editor is a colour you can see but cannot read.
/// <see cref="EnsureReadable"/> fixes those in place, by rule rather than by a hand-maintained colour
/// table: any named colour that fails WCAG AA against the editor background is blended toward the
/// background's opposite until it passes, keeping its hue. Originals are kept so switching back
/// restores exactly what AvalonEdit shipped.
/// </para>
/// </remarks>
internal static class SyntaxThemes
{
    private const double MinimumContrast = 4.5;

    private static readonly Dictionary<HighlightingColor, HighlightingBrush?> Originals = new();

    static SyntaxThemes()
    {
        // Both palettes are registered up front. The light ones own the file extensions, so anything
        // that resolves a definition from a file name (rather than from us) still gets a valid one.
        Register(JgsSyntax.Name, JgsSyntax.Xshd(JgsSyntax.Name, SyntaxPalette.Light), ".jgs");
        Register(MatlabSyntax.Name, MatlabSyntax.Xshd(MatlabSyntax.Name, SyntaxPalette.Light), ".m");

        string jgsDark = DarkNameOf(JgsSyntax.Name);
        string matlabDark = DarkNameOf(MatlabSyntax.Name);
        Register(jgsDark, JgsSyntax.Xshd(jgsDark, SyntaxPalette.Dark));
        Register(matlabDark, MatlabSyntax.Xshd(matlabDark, SyntaxPalette.Dark));
    }

    /// <summary>
    /// The definition to show <paramref name="language"/> in, or null when nothing is registered for
    /// it (a plain text document, say).
    /// </summary>
    /// <param name="language">The engine's language name — "JGS", "MATLAB", "C#", "Python".</param>
    /// <param name="dark">Whether the dark theme is in force.</param>
    /// <param name="editorBackground">The editor's background, used to test contrast.</param>
    public static IHighlightingDefinition? Resolve(string language, bool dark, Color editorBackground)
    {
        if (dark && HighlightingManager.Instance.GetDefinition(DarkNameOf(language)) is { } ours)
        {
            return ours;
        }

        IHighlightingDefinition? definition = HighlightingManager.Instance.GetDefinition(language);
        if (definition is not null)
        {
            EnsureReadable(definition, editorBackground);
        }

        return definition;
    }

    private static string DarkNameOf(string language) => language + " (Dark)";

    private static void Register(string name, string xshd, params string[] extensions)
    {
        using var reader = XmlReader.Create(new StringReader(xshd));
        IHighlightingDefinition definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        HighlightingManager.Instance.RegisterHighlighting(name, extensions, definition);
    }

    private static void EnsureReadable(IHighlightingDefinition definition, Color background)
    {
        foreach (HighlightingColor color in definition.NamedHighlightingColors)
        {
            if (!Originals.TryGetValue(color, out HighlightingBrush? original))
            {
                original = color.Foreground;
                Originals[color] = original;
            }

            // Always start from what the definition shipped with, so switching themes back and forth
            // cannot accumulate adjustments.
            color.Foreground = original;
            if (ColorOf(original) is not Color ink || Contrast(ink, background) >= MinimumContrast)
            {
                continue;
            }

            color.Foreground = new SimpleHighlightingBrush(Readable(ink, background));
        }
    }

    private static Color? ColorOf(HighlightingBrush? brush)
    {
        try
        {
            return brush?.GetColor(null!);
        }
        catch (Exception ex) when (ex is NullReferenceException or NotSupportedException)
        {
            // A brush that needs a live render context (AvalonEdit's system-colour brush) cannot be
            // sampled here. Leaving it alone is the right answer: it already tracks the OS theme.
            return null;
        }
    }

    private static Color Readable(Color ink, Color background)
    {
        Color target = Luminance(background) < 0.5 ? Colors.White : Colors.Black;
        for (double t = 0.05; t < 1.0; t += 0.05)
        {
            Color candidate = Mix(ink, target, t);
            if (Contrast(candidate, background) >= MinimumContrast)
            {
                return candidate;
            }
        }

        return target;
    }

    private static Color Mix(Color from, Color to, double t) => Color.FromRgb(
        (byte)System.Math.Round((from.R * (1 - t)) + (to.R * t)),
        (byte)System.Math.Round((from.G * (1 - t)) + (to.G * t)),
        (byte)System.Math.Round((from.B * (1 - t)) + (to.B * t)));

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        return (System.Math.Max(la, lb) + 0.05) / (System.Math.Min(la, lb) + 0.05);
    }

    /// <summary>Relative luminance, per WCAG 2.1.</summary>
    private static double Luminance(Color c) =>
        (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

    private static double Channel(byte value)
    {
        double v = value / 255.0;
        return v <= 0.03928 ? v / 12.92 : System.Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
