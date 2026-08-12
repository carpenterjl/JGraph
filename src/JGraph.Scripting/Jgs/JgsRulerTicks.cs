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

    /// <summary>What the ticks read, as a cell of char — MATLAB's shape for this answer.</summary>
    public static JgsValue ReadLabels(AxisModel ruler)
    {
        IReadOnlyList<string> labels = ruler.TickLabelOverrides ?? Current(ruler).Select(static t => t.Label).ToArray();
        return JgsValue.Cell(labels.Select(JgsValue.Str).ToArray());
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
    /// The format the tick labels are written with. This answers in the .NET spelling the axis stores,
    /// not the printf one a script may have handed over — a recorded divergence, and the reason is that
    /// one format string is the model's own property, editable in the inspector and saved in the file.
    /// </summary>
    public static JgsValue ReadFormat(AxisModel ruler) => JgsValue.Str(ruler.TickLabelFormat ?? "auto");

    public static void WriteFormat(string what, AxisModel ruler, JgsValue value, int line, int col) =>
        ruler.TickLabelFormat = ToNetFormat(what, JgsBuiltins.StrOf(what, value, line, col), line, col);

    /// <summary>
    /// Translates MATLAB's tick format — a named one such as 'usd' or 'degrees', or a printf spec such
    /// as '%.2f' with any literal text around it — into the .NET numeric format the axis holds.
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
