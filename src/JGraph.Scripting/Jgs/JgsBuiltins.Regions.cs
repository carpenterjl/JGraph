using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Open item 108: <c>xregion</c> and <c>yregion</c>, the shaded bands R2025b draws beside <c>xline</c>
/// and <c>yline</c>, read and refused as R2025b reads them (probe_108, probe_108b).
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// <c>xregion(x1, x2)</c> — a band, or one per pair when the two are vectors — and
    /// <c>xregion(xvals)</c>, whose two-row matrix holds a band a column and any other two-column
    /// one a band a row; on a named axes or the current one, then name/value pairs naming any of
    /// the band's properties. Like a constant line it does not clear the axes. One band answers its
    /// handle, several a column of them.
    /// </summary>
    private static JgsValue ConstantRegion(
        string verb, IReadOnlyList<JgsValue> args, int line, int col, bool vertical)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            int dataEnd = 0;
            while (dataEnd < rest.Count && rest[dataEnd].Type != JgsType.String)
            {
                dataEnd++;
            }

            if (dataEnd == 0)
            {
                // Text where the values go is R2025b's wrong-kind refusal; nothing at all, its arity's.
                if (rest.Count > 0 && !RegionOption(rest[0].AsString))
                {
                    JgsGraphicsProperties.RegionValue(rest[0], line, col);
                }

                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
            }

            if (dataEnd > 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
            }

            if ((rest.Count - dataEnd) % 2 != 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:class:BadParamValuePairs",
                    "Invalid parameter/value pair arguments.");
            }

            // Datetimes and durations are banded on the ruler that shows them, as days (open item 111).
            AxesModel axes = JG.Gca();
            AxisModel ruler = vertical ? axes.PrimaryXAxis : axes.ActiveYAxis;
            JgsTimeTag? times = RegionTimes(rest, dataEnd, line, col);
            if (times is not null)
            {
                CheckRegionRuler(axes, ruler, times.Kind, line, col);
                rest = [.. rest.Select((value, i) => i < dataEnd ? AxisNumbersFor(value) : value)];
            }

            List<(double First, double Second)> bands = RegionBands(rest, dataEnd, line, col);

            if (axes.IsPolar)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:handle_graphics:exceptions:HandleGraphicsException",
                    "ConstantRegion cannot be a child of PolarAxes.");
            }

            for (int i = dataEnd; i < rest.Count; i += 2)
            {
                string name = StrOf(verb, rest[i], line, col);
                if (!RegionOption(name))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:hg:InvalidProperty",
                        $"Unrecognized property {name} for class ConstantRegion.");
                }
            }

            if (times?.Kind == JgsTimeKind.Datetime)
            {
                ruler.UseDateTime();
            }
            else if (times is not null)
            {
                ruler.DurationFormat ??= times.Format;
            }

            var made = new List<GraphObject>();
            foreach ((double first, double second) in bands)
            {
                ConstantRegionPlot band = vertical ? JG.XRegion(first, second) : JG.YRegion(first, second);
                if (times is not null)
                {
                    JgsGraphicsProperties.TakeRegionTimes(band, times);
                }

                JgsHandleEntry entry = JgsHandleRegistry.EntryFor(band);
                for (int i = dataEnd; i < rest.Count; i += 2)
                {
                    JgsGraphicsProperties.Set(entry, rest[i].AsString, rest[i + 1], line, col);
                }

                made.Add(band);
            }

            return made.Count == 1 ? Handle((PlotObject)made[0]) : JgsGraphicsProperties.HandleColumn(made);
        });
    }

    /// <summary>Whether a word names a property a band has, which is what its options may set.</summary>
    private static bool RegionOption(string name) =>
        JgsGraphicsProperties.TableFor(typeof(ConstantRegionPlot)).ContainsKey(name);

    /// <summary>The bands the values describe, refused in R2025b's words when they describe none.</summary>
    private static List<(double First, double Second)> RegionBands(
        IReadOnlyList<JgsValue> rest, int dataEnd, int line, int col)
    {
        var bands = new List<(double, double)>();
        if (dataEnd == 2)
        {
            double[] lows = RegionNumbers(rest[0], line, col);
            double[] highs = RegionNumbers(rest[1], line, col);
            if (lows.Length != highs.Length)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:graphics:constantline:UnevenVectors",
                    "Vectors must have same length.");
            }

            for (int i = 0; i < lows.Length; i++)
            {
                bands.Add((lows[i], highs[i]));
            }

            return bands;
        }

        JgsValue values = rest[0];
        RegionNumbers(values, line, col);
        int rows = JgsMatrix.RowCount(values);
        int cols = JgsMatrix.ColCount(values);
        if (rows == 2)
        {
            for (int c = 0; c < cols; c++)
            {
                bands.Add((JgsMatrix.At(values, 0, c).AsNumber, JgsMatrix.At(values, 1, c).AsNumber));
            }
        }
        else if (cols == 2)
        {
            for (int r = 0; r < rows; r++)
            {
                bands.Add((JgsMatrix.At(values, r, 0).AsNumber, JgsMatrix.At(values, r, 1).AsNumber));
            }
        }
        else
        {
            throw new JgsRuntimeException(line, col, "MATLAB:graphics:constantline:TooFewInput",
                "Specify a minimum and maximum value.");
        }

        return bands;
    }

    /// <summary>
    /// The kind of time the values are, or null for numbers. Datetimes go with datetimes only, as
    /// R2025b's concatenation of them says; a duration with anything else is refused the same way.
    /// </summary>
    private static JgsTimeTag? RegionTimes(IReadOnlyList<JgsValue> rest, int dataEnd, int line, int col)
    {
        JgsTimeTag? tag = null;
        bool numbers = false;
        for (int i = 0; i < dataEnd; i++)
        {
            if (rest[i].TimeTag is not { } own)
            {
                numbers = true;
            }
            else if (tag is null)
            {
                tag = own;
            }
            else if (tag.Kind != own.Kind)
            {
                numbers = true;
            }
        }

        if (tag is not null && numbers)
        {
            throw tag.Kind == JgsTimeKind.Datetime
                ? new JgsRuntimeException(line, col, "MATLAB:datetime:cat:InvalidConcatenation",
                    "All inputs must be datetimes or date/time character vectors or date/time strings.")
                : new JgsRuntimeException(line, col,
                    "xregion and yregion take two durations, or two numbers, but not one of each.");
        }

        return tag;
    }

    /// <summary>
    /// Whether the ruler can show a band of times (probe <c>probe_111b</c>): one already showing other
    /// data is refused in R2025b's words; one showing nothing yet is told what it shows.
    /// </summary>
    private static void CheckRegionRuler(AxesModel axes, AxisModel ruler, JgsTimeKind kind, int line, int col)
    {
        bool dates = ruler.Scale == AxisScaleType.DateTime;
        bool spans = ruler.DurationFormat is not null;
        if (dates && kind == JgsTimeKind.Duration)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:graphics:configureAxes:MixingOther",
                "At least one axis dimension is configured for datetime values. To create datetime values, use the datetime function.");
        }

        if (spans && kind == JgsTimeKind.Datetime)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:graphics:configureAxes:MixingOther",
                "At least one axis dimension is configured for duration values. To create duration values, use the duration function.");
        }

        if (!dates && !spans && axes.Plots.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:graphics:configureAxes:Mixing",
                "Data inputs must match the axis configuration. A numeric axis must have numeric data inputs or data inputs which can be converted to double.");
        }
    }

    /// <summary>One argument's numbers: never empty, never text.</summary>
    private static double[] RegionNumbers(JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.String or JgsType.Cell || value.IsStringArray)
        {
            JgsGraphicsProperties.RegionValue(value, line, col); // throws R2025b's InvalidData
        }

        double[] numbers = ToDoubles("xregion", value, line, col);
        if (numbers.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:graphics:constantline:EmptyInputs",
                "The minimum and maximum value cannot be empty.");
        }

        return numbers;
    }
}
