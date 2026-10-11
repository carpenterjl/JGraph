using System.Globalization;
using System.Text;
using JGraph.Core.Model;
using JGraph.Maths.Ticks;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a script can say about an axis' ticks — where they go, what they read, which way up they are,
/// and how their numbers are written.
/// <para>
/// The tick verbs (<c>xticks</c>, <c>yticklabels</c>, …) and the MATLAB property spellings
/// (<c>XTick</c>, <c>TickValues</c>, …) are two ways of saying the same six things, so both go through
/// here rather than each carrying its own reading of what "manual" means.
/// </para>
/// </summary>
internal static class JgsRulerTicks
{
    // --- Tick values ----------------------------------------------------------------------------

    /// <summary>Where the ticks are: the ones the axis was told to use, or the ones it would draw.</summary>
    public static JgsValue ReadValues(AxisModel ruler)
    {
        IReadOnlyList<double> values = ruler.TickPositions ?? Current(ruler).Select(static t => t.Value).ToArray();
        return JgsGraphicsProperties.Row(values.ToArray());
    }

    public static void WriteValues(string what, AxisModel ruler, JgsValue value, int line, int col)
    {
        if (ModeWord(what, value, line, col) is { } word)
        {
            // 'manual' means "keep what is showing", which is only meaningful as the values themselves:
            // the axis has no memory of a choice it never made.
            ruler.TickPositions = word == "auto"
                ? null
                : Current(ruler).Select(static t => t.Value).ToArray();
            return;
        }

        RefuseHandle(what, value, line, col);
        double[] values = JgsBuiltins.ToDoubles(what, value, line, col);
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] <= values[i - 1])
            {
                throw new JgsRuntimeException(line, col,
                    $"{what}: tick values increase, but {values[i - 1]:G6} is followed by {values[i]:G6}.");
            }
        }

        ruler.TickPositions = values;
    }

    // --- Tick labels ----------------------------------------------------------------------------

    /// <summary>
    /// What the ticks read, as a cell of char — MATLAB's shape for this answer, a column in the
    /// MATLAB dialect as R2025b's is (open item 97, measured).
    /// </summary>
    public static JgsValue ReadLabels(AxisModel ruler)
    {
        IReadOnlyList<string> labels = ruler.TickLabelOverrides ?? Current(ruler).Select(static t => t.Label).ToArray();
        JgsValue cell = JgsValue.Cell(labels.Select(JgsValue.Str).ToArray());
        if (JgsRunningDialect.ThreadIsMatlab && labels.Count > 0)
        {
            cell.Reshape(labels.Count, 1);
        }

        return cell;
    }

    public static void WriteLabels(string what, AxisModel ruler, JgsValue value, int line, int col)
    {
        if (ModeWord(what, value, line, col) is { } word)
        {
            ruler.TickLabelOverrides = word == "auto"
                ? null
                : Current(ruler).Select(static t => t.Label).ToArray();
            return;
        }

        ruler.TickLabelOverrides = LabelWords(what, value, line, col);
    }

    // --- Angle and format -----------------------------------------------------------------------

    public static JgsValue ReadAngle(AxisModel ruler) => JgsValue.Number(ruler.TickLabelAngle);

    public static void WriteAngle(string what, AxisModel ruler, JgsValue value, int line, int col)
    {
        double angle = JgsBuiltins.NumOf(what, value, line, col);
        if (!double.IsFinite(angle))
        {
            throw new JgsRuntimeException(line, col, $"{what}: an angle is a finite number of degrees.");
        }

        ruler.TickLabelAngle = angle;
    }

    /// <summary>
    /// The format the tick labels are written with, in R2025b's spelling: the printf format the axis
    /// holds, a named one as R2025b expands it (<c>'usd'</c> is <c>'$%,.2f'</c>), and <c>'%g'</c> —
    /// <c>'%g°'</c> on a θ ruler — when the labels are automatic (open item 97, probe_97c). A .NET
    /// format a figure saved before then answers as it is.
    /// </summary>
    public static JgsValue ReadFormat(AxisModel ruler) => JgsValue.Str(ruler.TickLabelFormat
        ?? (ruler.Parent is AxesModel owner && ReferenceEquals(owner.ThetaAxis, ruler) ? "%g°" : "%g"));

    /// <summary>The <c>xtickformat</c> family's write: a refusal is said in JGraph's words.</summary>
    public static void WriteFormat(string what, AxisModel ruler, JgsValue value, int line, int col) =>
        ruler.TickLabelFormat = RulerFormat(what, JgsBuiltins.StrOf(what, value, line, col), asProperty: false, line, col);

    /// <summary>A ruler's <c>TickLabelFormat</c> property: a refusal is R2025b's, identifier and words.</summary>
    public static void WriteFormatProperty(AxisModel ruler, JgsValue value, int line, int col) =>
        ruler.TickLabelFormat = RulerFormat("TickLabelFormat", JgsBuiltins.StrOf("TickLabelFormat", value, line, col), asProperty: true, line, col);

    /// <summary>
    /// What a ruler keeps for a format a script wrote: null for <c>'auto'</c>, R2025b's printf
    /// expansion of a named format, and a printf format as it was written once it is one R2025b takes —
    /// a single conversion of f, F, g, G, e, E, d or i, with any text around it.
    /// </summary>
    private static string? RulerFormat(string what, string format, bool asProperty, int line, int col)
    {
        // The named formats are the verb's words; the property takes a printf format only (measured).
        switch (asProperty ? string.Empty : format.ToLowerInvariant())
        {
            case "auto": return null;
            case "usd": return "$%,.2f";
            case "eur": return "\\x20AC%,.2f";
            case "gbp": return "\\x00A3%,.2f";
            case "jpy" or "yen": return "\\x00A5%,d";
            case "degrees": return "%g\\x00B0";
            case "percentage": return "%g%%";
        }

        TickFormatProblem problem = TickFormats.Check(format);
        if (problem == TickFormatProblem.None)
        {
            return format;
        }

        if (asProperty)
        {
            throw PrintfRefusal(problem, "TickLabelFormat", "NumericRuler", line, col);
        }

        _ = ToNetFormat(what, format, line, col); // says what is wrong in JGraph's words
        throw new JgsRuntimeException(line, col,
            $"{what}: '{format}' is not a tick format. It holds one number conversion, such as '%.2f' or 'x = %g m'.");
    }

    /// <summary>
    /// A heatmap's <c>CellLabelFormat</c> as written, once it is a format R2025b takes: one number
    /// conversion with any text around it, and no named format (open item 103, probe_103).
    /// </summary>
    public static string CellFormat(JgsValue value, int line, int col)
    {
        string format = JgsBuiltins.StrOf("CellLabelFormat", value, line, col);
        TickFormatProblem problem = TickFormats.Check(format);
        return problem == TickFormatProblem.None
            ? format
            : throw PrintfRefusal(problem, "CellLabelFormat", "HeatmapChart", line, col);
    }

    /// <summary>R2025b's refusal of a printf format property, identifier and words.</summary>
    private static JgsRuntimeException PrintfRefusal(
        TickFormatProblem problem, string property, string cls, int line, int col)
    {
        string setting = $"Error setting property '{property}' of class '{cls}':\n";
        return problem switch
        {
            TickFormatProblem.UnsupportedType => new JgsRuntimeException(line, col,
                "MATLAB:datatypes:PrintfFormatDataType:UnsupportedType",
                setting + "Format type must be %f, %F, %g, %G, %e, %E, %d or %i."),
            TickFormatProblem.DynamicNumber => new JgsRuntimeException(line, col,
                "MATLAB:datatypes:PrintfFormatDataType:DynamicNumber",
                setting + "Width and precision must be numeric literals."),
            _ => new JgsRuntimeException(line, col, "MATLAB:datatypes:PrintfFormatDataType:InvalidFormat",
                setting + "Value must be a supported tick label format specification. "
                + "For example '%.2f m' formats with 2 digits after the decimal point and appends the suffix ' m'."),
        };
    }

    /// <summary>
    /// Translates MATLAB's tick format — a named one such as 'usd' or 'degrees', or a printf spec such
    /// as '%.2f' with any literal text around it — into a .NET numeric format. Nothing keeps a format
    /// this way any more (open items 97 and 103); it is how the xtickformat family words a refusal.
    /// </summary>
    public static string? ToNetFormat(string what, string format, int line, int col)
    {
        switch (format.ToLowerInvariant())
        {
            case "auto": return null;
            case "usd": return "$#,##0.00";
            case "eur": return "€#,##0.00";
            case "gbp": return "£#,##0.00";
            case "jpy" or "yen": return "¥#,##0";
            case "degrees": return "0.####°";
            case "percentage": return "0.####%";
        }

        int start = format.IndexOf('%');
        if (start < 0)
        {
            throw new JgsRuntimeException(line, col,
                $"{what}: '{format}' is not a tick format. Use a printf spec such as '%.2f', or one of "
                + "auto, usd, eur, gbp, jpy, degrees, percentage.");
        }

        int cursor = start + 1;
        while (cursor < format.Length && "+-# 0123456789.".Contains(format[cursor], StringComparison.Ordinal))
        {
            cursor++;
        }

        if (cursor >= format.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{what}: '{format}' ends in the middle of its conversion. It reads like '%.2f'.");
        }

        string spec = format[(start + 1)..cursor];
        char conversion = format[cursor];
        int precision = spec.IndexOf('.') is var dot && dot >= 0
            ? int.Parse(spec[(dot + 1)..].Length == 0 ? "0" : spec[(dot + 1)..], CultureInfo.InvariantCulture)
            : -1;

        string number = conversion switch
        {
            'f' or 'F' => Digits(precision < 0 ? 2 : precision),
            'e' or 'E' => Digits(precision < 0 ? 4 : precision) + "e+00",
            'g' or 'G' => Optional(precision < 0 ? 4 : precision),
            'd' or 'i' or 'u' => "0",
            _ => throw new JgsRuntimeException(line, col,
                $"{what}: '%{conversion}' is not a number conversion. Use f, e, g, or d."),
        };

        return Literal(format[..start]) + number + Literal(format[(cursor + 1)..]);
    }

    private static string Digits(int count) => count <= 0 ? "0" : "0." + new string('0', count);

    private static string Optional(int count) => count <= 0 ? "0" : "0." + new string('#', count);

    /// <summary>
    /// Quotes the text either side of a conversion so .NET prints it rather than reading it as more
    /// format. A quote inside the text would end the quoting, so it is escaped.
    /// </summary>
    private static string Literal(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var quoted = new StringBuilder("'");
        foreach (char c in text.Replace("%%", "%", StringComparison.Ordinal))
        {
            if (c == '\'')
            {
                quoted.Append('\\');
            }

            quoted.Append(c);
        }

        return quoted.Append('\'').ToString();
    }

    // --- Shared ----------------------------------------------------------------------------------

    /// <summary>
    /// The ticks the axis would draw right now. An auto-scaling axis is fitted first, for the M51
    /// reason: until the layout pass runs, its stored range is the placeholder it was created with, and
    /// ticks generated from that answer about a plot nobody drew.
    /// </summary>
    public static IReadOnlyList<Tick> Current(AxisModel ruler)
    {
        if (ruler.AutoScale && ruler.Parent is AxesModel owner)
        {
            owner.RecomputeDataBounds();
        }

        return TickGenerators.For(ruler)
            .Generate(ruler.Range, ruler.TargetMajorTickCount, ruler.TickLabelFormat)
            .MajorTicks;
    }

    /// <summary>'auto' or 'manual' if that is what was said, null if a value was given instead.</summary>
    internal static string? ModeWord(string what, JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.String)
        {
            return null;
        }

        string word = value.AsString.ToLowerInvariant();
        if (word is "auto" or "manual")
        {
            return word;
        }

        throw new JgsRuntimeException(line, col,
            $"{what}: '{value.AsString}' is neither 'auto' nor 'manual'. Labels come in a cell, such as {{'low', 'high'}}.");
    }

    /// <summary>The text in a cell, a string array, or nothing at all.</summary>
    internal static string[] LabelWords(string what, JgsValue value, int line, int col)
    {
        JgsValue[] elements = value.Type switch
        {
            JgsType.Cell => value.AsCell,
            JgsType.Array => Enumerable.Range(0, value.ArrayLength).Select(value.ElementAt).ToArray(),
            _ => throw new JgsRuntimeException(line, col,
                $"{what}: labels come in a cell, such as {{'low', 'high'}}, or an empty [] to blank them."),
        };

        var words = new string[elements.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i].Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, col,
                    $"{what}: every label is text, but element {i + 1} is not.");
            }

            words[i] = elements[i].AsString;
        }

        return words;
    }

    /// <summary>
    /// A handle is an ordinary number, so a ruler handle passed where tick values belong would quietly
    /// put a tick at a million and a half rather than say what went wrong.
    /// </summary>
    internal static void RefuseHandle(string what, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Number && JgsHandleRegistry.TryGet(value, out JgsHandleEntry? entry))
        {
            throw new JgsRuntimeException(line, col,
                $"{what} aims at an axes, but got a handle to a {entry.TypeName}.");
        }
    }
}
