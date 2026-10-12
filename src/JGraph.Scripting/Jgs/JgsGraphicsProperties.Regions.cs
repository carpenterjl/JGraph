using JGraph.Core.Drawing;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Open item 108: the names R2025b's <c>xregion</c> and <c>yregion</c> answer to, on the band model
/// <see cref="ConstantRegionPlot"/>, refused in R2025b's words (probe_108, probe_108b).
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private const string RegionClass = "matlab.graphics.chart.decoration.ConstantRegion";

    private static ConstantRegionPlot Region(JgsHandleEntry entry) => (ConstantRegionPlot)entry.Target;

    private static void AddConstantRegionBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Value",
            entry => Row(Region(entry).First, Region(entry).Second),
            (entry, value, line, col) =>
            {
                (double first, double second) = RegionValue(value, line, col);
                Region(entry).First = first;
                Region(entry).Second = second;
            });

        // Which ruler the band is constant against: 'x' for xregion, 'y' for yregion.
        AddWordProperty(table, "InterceptAxis",
            entry => Region(entry).Direction == ConstantLineDirection.Vertical ? "x" : "y",
            (entry, word, line, col) => Region(entry).Direction = word switch
            {
                "x" => ConstantLineDirection.Vertical,
                "y" => ConstantLineDirection.Horizontal,
                _ => throw RegionEnum("InterceptAxis", word, "'x' | 'y'", line, col),
            });

        // The fill: R2025b's grey until a colour, or a SeriesIndex, says otherwise; 'none' is no fill.
        Put(table, "FaceColor",
            entry => RegionFace(Region(entry)) is { IsTransparent: true } ? JgsValue.Str("none") : ColorRow(RegionFace(Region(entry))),
            (entry, value, line, col) => Region(entry).FaceColor = JgsBuiltins.OptionColor(value, line, col, "xregion"));
        AddWordProperty(table, "FaceColorMode",
            entry => Region(entry).FaceColor is null ? "auto" : "manual",
            (entry, word, line, col) =>
            {
                ConstantRegionPlot band = Region(entry);
                switch (word)
                {
                    case "auto":
                        band.FaceColor = null;
                        break;
                    case "manual":
                        band.FaceColor ??= RegionFace(band);
                        break;
                    default:
                        throw RegionEnum("FaceColorMode", word, "'auto' | 'manual'", line, col);
                }
            });

        Put(table, "EdgeColor",
            entry => Region(entry).EdgeColor.IsTransparent ? JgsValue.Str("none") : ColorRow(Region(entry).EdgeColor),
            (entry, value, line, col) => Region(entry).EdgeColor = JgsBuiltins.OptionColor(value, line, col, "xregion"));

        Put(table, "FaceAlpha",
            entry => JgsValue.Number(Region(entry).FaceAlpha),
            (entry, value, line, col) => Region(entry).FaceAlpha = RegionAlpha("FaceAlpha", value, line, col));
        Put(table, "EdgeAlpha",
            entry => JgsValue.Number(Region(entry).EdgeAlpha),
            (entry, value, line, col) => Region(entry).EdgeAlpha = RegionAlpha("EdgeAlpha", value, line, col));

        Put(table, "LineWidth",
            entry => JgsValue.Number(Region(entry).LineWidth),
            (entry, value, line, col) => Region(entry).LineWidth = JgsBuiltins.NumOf("LineWidth", value, line, col));
        AddWordProperty(table, "LineStyle",
            entry => JgsBuiltins.DashWord(Region(entry).Dash),
            (entry, word, line, col) => Region(entry).Dash = word switch
            {
                "-" => DashStyle.Solid,
                "--" => DashStyle.Dash,
                ":" => DashStyle.Dot,
                "-." => DashStyle.DashDot,
                "none" => DashStyle.None,
                _ => throw RegionEnum("LineStyle", word, "'-' | '--' | ':' | '-.' | 'none'", line, col),
            });

        AddWordProperty(table, "Layer",
            entry => Region(entry).OnTop ? "top" : "bottom",
            (entry, word, line, col) => Region(entry).OnTop = word switch
            {
                "top" => true,
                "bottom" => false,
                _ => throw RegionEnum("Layer", word, "'bottom' | 'top'", line, col),
            });

        // 'none' (R2025b's default) keeps the grey; a number takes the colour order's entry.
        Put(table, "SeriesIndex",
            entry => Region(entry).SeriesIndex < 0 ? JgsValue.Str("none") : JgsValue.Number(Region(entry).SeriesIndex + 1),
            (entry, value, line, col) =>
            {
                if (value.Type == JgsType.String && value.AsString.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    Region(entry).SeriesIndex = -1;
                    return;
                }

                double seat = Numbers("SeriesIndex", value, 1, line, col)[0];
                if (seat < 1 || seat != System.Math.Floor(seat))
                {
                    throw new JgsRuntimeException(line, col,
                        $"SeriesIndex is a positive whole number or 'none', but got {seat:G6}.");
                }

                Region(entry).SeriesIndex = (int)seat - 1;
            });
    }

    /// <summary>The fill drawn: the colour written, else the colour order's entry for a SeriesIndex, else the grey.</summary>
    private static Color RegionFace(ConstantRegionPlot band) =>
        band.FaceColor
        ?? (band.SeriesIndex >= 0 ? JgsBuiltins.PaletteColorFor(band) : ConstantRegionPlot.DefaultFaceColor);

    /// <summary>
    /// A region's two values: two numbers, in the order written. R2025b refuses text with
    /// <c>InvalidData</c> and any other count with <c>ValueMustBeTwoElements</c>.
    /// </summary>
    internal static (double First, double Second) RegionValue(JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String || value.Type == JgsType.Cell || value.IsStringArray)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:graphics:constantline:InvalidData",
                $"Error setting property 'Value' of class '{RegionClass}'. Value must be one of the following: "
                + "'numeric' | 'datetime' | 'categorical' | 'duration'");
        }

        double[] numbers = JgsBuiltins.ToDoubles("Value", value, line, col);
        if (numbers.Length != 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:graphics:constantline:ValueMustBeTwoElements",
                $"Error setting property 'Value' of class '{RegionClass}'. Value must be a 1x2 vector.");
        }

        return (numbers[0], numbers[1]);
    }

    /// <summary>A region's alpha: a word is not a numeric scalar there, and out of range is R2025b's datatypes refusal.</summary>
    private static double RegionAlpha(string property, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.String or JgsType.Cell)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:datatypes:invalidZeroToOne",
                $"Error setting property '{property}' of class 'ConstantRegion':\nValue not a numeric scalar");
        }

        return JgsBuiltins.CheckedAlpha(property, "ConstantRegion", value, JgsBuiltins.AlphaRefusal.Datatypes, line, col);
    }

    private static JgsRuntimeException RegionEnum(string property, string word, string choices, int line, int col) =>
        new(line, col, "MATLAB:datatypes:InvalidEnumValue",
            $"Error setting property '{property}' of class 'ConstantRegion':\n'{word}' is not a valid value. "
            + $"Use one of these values: {choices}.");
}
