using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// R2025b's automatic tick label angle for a horizontal ruler (open item 126), measured by bisecting
/// the axes' own width over label lengths, letters and font sizes (m126_rot2, m126_rot3). Each
/// label's room is the ruler's whole pixels shared among its ticks. The labels stay level while that
/// room holds the widest label, rounded up to a pixel, and a gap of nine sixteenths of the font's
/// pixel size, rounded up too; past that they turn to 30 degrees while the room is at least two label
/// heights, to 45 while it is at least one, and to 90 beyond. A label's height is the face's ascent
/// and descent at the size, each rounded to a pixel, as R2025b's text extent gives it. The labels are
/// measured in R2025b's face with its pair kerning, a point being 96/72 of a pixel and this build's
/// automatic 11-point axes font R2025b's 10, so the script that reads the angle and the renderer that
/// draws it agree. Room exactly at a threshold keeps the smaller angle, except along a categorical
/// ruler of 8 to 31 categories, where R2025b takes the larger one (m126_rot4, m126_rot5: measured,
/// not understood). A vertical ruler's labels never turn.
/// </summary>
public static class TickLabelTurn
{
    /// <summary>The face this build's axes text takes when nobody chose one.</summary>
    private const string DefaultFace = "Segoe UI";

    /// <summary>
    /// The angle for <paramref name="spacing"/> pixels of room per tick, the widest label, a label's
    /// height and the font's size, all in pixels; <paramref name="tiesTurn"/> when room exactly at a
    /// threshold takes the larger angle.
    /// </summary>
    public static double Angle(double spacing, double widestLabel, double labelHeight, double fontPixels, bool tiesTurn = false)
    {
        if (!(spacing > 0) || double.IsInfinity(spacing) || !(fontPixels > 0))
        {
            return 0; // one tick or none: nothing crowds
        }

        bool Holds(double needed) => tiesTurn ? spacing > needed : spacing >= needed;
        if (Holds(Math.Ceiling(widestLabel) + Math.Ceiling(0.5625 * fontPixels)))
        {
            return 0;
        }

        if (Holds(2 * labelHeight))
        {
            return 30;
        }

        return Holds(labelHeight) ? 45 : 90;
    }

    /// <summary>
    /// The angle for <paramref name="labels"/> at <paramref name="ticks"/> along a ruler starting
    /// <paramref name="startPixels"/> from the figure's edge and <paramref name="lengthPixels"/> long,
    /// showing <paramref name="range"/>, in <paramref name="style"/>, whose size is the axes' automatic
    /// one or one a script wrote; <paramref name="categorical"/> for a ruler of categories.
    /// </summary>
    public static double ForLabels(
        IReadOnlyList<string> labels, IEnumerable<double> ticks, DataRange range, double startPixels, double lengthPixels, bool logarithmic,
        TextStyle style, bool automaticFont, bool categorical = false)
    {
        ArgumentNullException.ThrowIfNull(labels);
        (double widest, double height, double fontPixels) = Measure(labels, style, automaticFont);
        int shown = Shown(ticks, range, logarithmic);
        double spacing = shown < 2 ? double.PositiveInfinity : WholePixels(startPixels, lengthPixels) / shown;
        return Angle(spacing, widest, height, fontPixels, tiesTurn: categorical && shown is >= 8 and <= 31);
    }

    /// <summary>
    /// The widest of <paramref name="labels"/> and a label's height, in pixels, as R2025b's text extents
    /// give them, and the font's pixel size. The rule is R2025b's at its font: this build's automatic
    /// axes font is 11 where R2025b's is 10, and a size a script wrote is R2025b's size as it stands.
    /// The labels are measured in R2025b's face while the axes keeps this build's default one: its
    /// Helvetica, which is Arial here, kerned as R2025b's text extents are (m126_rot3). The height is
    /// the face's ascent and descent, each rounded to a pixel.
    /// </summary>
    public static (double Widest, double Height, double FontPixels) Measure(IReadOnlyList<string> labels, TextStyle style, bool automaticFont)
    {
        ArgumentNullException.ThrowIfNull(labels);
        double fontPixels = style.FontSize * (automaticFont ? 10.0 / 11 : 1) * 96.0 / 72;
        string face = style.FontFamily == DefaultFace ? "Helvetica" : style.FontFamily;
        double widest = 0;
        foreach (string label in labels)
        {
            double width = UiFonts.Measure(label, face, fontPixels, style.Bold, style.Italic).Width
                + UiFonts.Kerning(label, face, fontPixels, style.Bold, style.Italic);
            widest = Math.Max(widest, width);
        }

        (double ascent, double descent) = UiFonts.Reach(face, fontPixels, style.Bold, style.Italic);
        double height = Math.Round(ascent, MidpointRounding.AwayFromZero) + Math.Round(descent, MidpointRounding.AwayFromZero);
        return (widest, height, fontPixels);
    }

    /// <summary>
    /// The room each label has: the ruler's whole pixels shared among the ticks it shows, as R2025b
    /// shares them (m126_rot3: ten bars turn and level at whole pixels of the axes' width, and five or
    /// twenty bars at the same room per tick). Each end of the ruler is snapped to its pixel, so a
    /// ruler 0.775 of a figure 491 pixels wide is 380 of them (m126_rot2). Infinity for fewer than two
    /// ticks.
    /// </summary>
    public static double Spacing(IEnumerable<double> ticks, DataRange range, double startPixels, double lengthPixels, bool logarithmic)
    {
        int shown = Shown(ticks, range, logarithmic);
        return shown < 2 ? double.PositiveInfinity : WholePixels(startPixels, lengthPixels) / shown;
    }

    /// <summary>How many of <paramref name="ticks"/> the ruler shows within <paramref name="range"/>.</summary>
    private static int Shown(IEnumerable<double> ticks, DataRange range, bool logarithmic)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        double low = Math.Min(range.Min, range.Max);
        double high = Math.Max(range.Min, range.Max);
        return ticks.Count(t => double.IsFinite(t) && (!logarithmic || t > 0) && t >= low - (1e-9 * Math.Abs(low)) && t <= high + (1e-9 * Math.Abs(high)));
    }

    /// <summary>The whole pixels between a ruler's ends, each snapped to its pixel.</summary>
    private static double WholePixels(double startPixels, double lengthPixels) =>
        Math.Round(startPixels + lengthPixels, MidpointRounding.AwayFromZero) - Math.Round(startPixels, MidpointRounding.AwayFromZero);
}
