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
        // The values as written (open item 111): a column stays a column, and datetimes and durations
        // come back as what they were, from the days the band holds them as.
        Put(table, "Value",
            entry => RegionValueOf(Region(entry)),
            (entry, value, line, col) => WriteRegionValue(Region(entry), value, line, col));

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
            (entry, value, line, col) =>
            {
                Region(entry).FaceColor = JgsBuiltins.OptionColor(value, line, col, "xregion");
                Region(entry).FaceColorManual = true;
            });

        // Back in 'auto' a band keeps the colour it has until a SeriesIndex names one, and takes that
        // one at once when it already does (open item 111, probe_111).
        AddWordProperty(table, "FaceColorMode",
            entry => Region(entry).FaceColorManual ? "manual" : "auto",
            (entry, word, line, col) =>
            {
                ConstantRegionPlot band = Region(entry);
                switch (word)
                {
                    case "auto":
                        band.FaceColorManual = false;
                        if (band.SeriesIndex >= 0)
                        {
                            band.FaceColor = null;
                        }

                        break;
                    case "manual":
                        band.FaceColor ??= RegionFace(band);
                        band.FaceColorManual = true;
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

        // 'none' (R2025b's default) keeps the grey, and so does 0, which reads back 0 (open item 111);
        // a number takes the colour order's entry. Written, it gives an automatic fill up.
        Put(table, "SeriesIndex",
            entry => Region(entry).SeriesIndex switch
            {
                RegionSeriesZero => JgsValue.Number(0),
                < 0 => JgsValue.Str("none"),
                int seat => JgsValue.Number(seat + 1),
            },
            (entry, value, line, col) =>
            {
                ConstantRegionPlot band = Region(entry);
                int index;
                if (value.Type == JgsType.String && value.AsString.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    index = -1;
                }
                else
                {
                    double seat = Numbers("SeriesIndex", value, 1, line, col)[0];
                    if (seat < 0 || seat != System.Math.Floor(seat))
                    {
                        throw new JgsRuntimeException(line, col,
                            $"SeriesIndex is a whole number or 'none', but got {seat:G6}.");
                    }

                    index = seat == 0 ? RegionSeriesZero : (int)seat - 1;
                }

                if (!band.FaceColorManual)
                {
                    band.FaceColor = null;
                }

                band.SeriesIndex = index;
            });
    }

    /// <summary>A band's <c>SeriesIndex</c> written as 0: no colour of the order, like 'none', but read back 0.</summary>
    private const int RegionSeriesZero = -2;

    /// <summary>A band's <c>Value</c>: its two numbers, or the times they hold, in the shape written.</summary>
    private static JgsValue RegionValueOf(ConstantRegionPlot band)
    {
        int[] shape = band.ValueIsColumn ? [2, 1] : [1, 2];
        if (band.ValueKind == RegionValueKind.Number)
        {
            return JgsMatrix.FromColumnMajorDims([band.First, band.Second], shape);
        }

        bool dates = band.ValueKind == RegionValueKind.Datetime;
        var tag = new JgsTimeTag(dates ? JgsTimeKind.Datetime : JgsTimeKind.Duration,
            band.TimeFormat ?? (dates ? JgsTime.DefaultDatetimeFormat : JgsTime.DefaultDurationFormat),
            band.TimeZone, band.TimeFormatSet);
        return JgsMatrix.FromColumnMajorDims([band.First * JgsTime.MsPerDay, band.Second * JgsTime.MsPerDay], shape).MarkTime(tag);
    }

    /// <summary>
    /// Writes a band's <c>Value</c>: two numbers, two datetimes or two durations, kept as they were
    /// written (probe <c>probe_111b</c>). R2025b's property takes another kind than the band had
    /// without asking its ruler; the verbs are the ones that check.
    /// </summary>
    private static void WriteRegionValue(ConstantRegionPlot band, JgsValue value, int line, int col)
    {
        if (value.IsTime)
        {
            double[] ms = JgsBuiltins.TimeMs(value);
            if (ms.Length != 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:graphics:constantline:ValueMustBeTwoElements",
                    $"Error setting property 'Value' of class '{RegionClass}'. Value must be a 1x2 vector.");
            }

            TakeRegionTimes(band, value.TimeTag!);
            band.First = ms[0] / JgsTime.MsPerDay;
            band.Second = ms[1] / JgsTime.MsPerDay;
        }
        else
        {
            (double first, double second) = RegionValue(value, line, col);
            band.ValueKind = RegionValueKind.Number;
            band.First = first;
            band.Second = second;
        }

        band.ValueIsColumn = value.Dims is [2, 1];
    }

    /// <summary>Records the kind and the display of the times a band's values are.</summary>
    internal static void TakeRegionTimes(ConstantRegionPlot band, JgsTimeTag tag)
    {
        band.ValueKind = tag.Kind == JgsTimeKind.Datetime ? RegionValueKind.Datetime : RegionValueKind.Duration;
        band.TimeFormat = tag.Format;
        band.TimeFormatSet = tag.FormatSet;
        band.TimeZone = tag.TimeZone;
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
