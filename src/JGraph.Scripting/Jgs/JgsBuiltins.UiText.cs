using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The text verbs of the app-building plan's stage U3: <c>textwrap</c>, which breaks paragraphs into
/// lines that fit a <c>uicontrol</c> or a count of characters, and <c>listfonts</c>. R2025b's argument
/// forms, results and refusals, measured in <c>tools/matlab-checklist/ui-probes/u3</c>
/// (<c>u3_extent</c>, <c>u3_wrap</c>).
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterUiTextBuiltins(JgsEnvironment env)
    {
        env.Builtins.Register("textwrap", JgsValue.Function(new BuiltinFunction(
            "textwrap", static (args, line, col) => TextWrap(args, 1, line, col)[0])
        {
            MultiOutput = TextWrap,
            KeepsStringArguments = true,
        }));
        env.Builtins.Register("listfonts", JgsValue.Function(new BuiltinFunction("listfonts", ListFonts)));
    }

    /// <summary>
    /// <c>[lines, position] = textwrap(h, paragraphs)</c> wraps to the control's width;
    /// <c>textwrap(h, paragraphs, columns)</c> and <c>textwrap(paragraphs, columns)</c> wrap to a
    /// count of characters. <c>position</c> is where the control would sit to hold the lines, in its
    /// own units; without a control it is four zeros.
    /// </summary>
    private static JgsValue[] TextWrap(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (wanted > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:textwrap:InvalidNumberOutputs",
                "TEXTWRAP requires 1 or 2 output arguments.");
        }

        MatlabArity(args, 2, 3, line, col);
        UiControlModel? control = null;
        int at = 0;
        if (args[0].Type == JgsType.Number && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry)
            && entry.Target is UiControlModel named)
        {
            control = named;
            at = 1;
        }
        else if (args[0].Type != JgsType.Cell && ClassOf(args[0], JgsDialect.Matlab) != "string")
        {
            throw new JgsRuntimeException(line, col, "MATLAB:textwrap:InvalidFirstInput",
                "First input argument must be a UIControl object or cell array.");
        }

        JgsValue text = args[at];
        string textKind = ClassOf(text, JgsDialect.Matlab);
        if (text.Type != JgsType.Cell && textKind != "string")
        {
            throw new JgsRuntimeException(line, col, "MATLAB:cellRefFromNonCell",
                "Brace indexing is not supported for variables of this type.");
        }

        var paragraphs = new List<string>();
        if (text.Type == JgsType.Cell)
        {
            foreach (JgsValue element in text.AsCell)
            {
                paragraphs.Add(IsTextScalar(element) || element.Type == JgsType.String
                    ? TextOf(element)
                    : throw new JgsRuntimeException(line, col, "MATLAB:invalidConversion",
                        $"Conversion to cellstr from {ClassOf(element, JgsDialect.Matlab)} is not possible."));
            }
        }
        else
        {
            int count = text.Type == JgsType.Array ? text.ArrayLength : 1;
            for (int i = 0; i < count; i++)
            {
                paragraphs.Add(TextOf(text.Type == JgsType.Array ? text.ElementAt(i) : text));
            }
        }

        int? columns = null;
        if (args.Count > at + 1)
        {
            JgsValue given = args[at + 1];
            if (given.Type == JgsType.Array && !given.IsStringArray && given.ArrayLength > 1
                && JgsNumericClasses.Parse(ClassOf(given, JgsDialect.Matlab)) is not null)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:nonLogicalConditional",
                    "Operands to the logical AND (&&) and OR (||) operators must be convertible to logical scalar values. Use the ANY or ALL functions to reduce operands to logical scalar values.");
            }

            double number = given.Type is JgsType.Number or JgsType.Bool ? given.AsNumber : double.NaN;
            if (!(number >= 1) || number != System.Math.Floor(number) || double.IsInfinity(number))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:textwrap:InvalidColumnsInput",
                    "Number of characters must be a positive integer.");
            }

            columns = (int)number;
        }
        else if (control is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:textwrap:InvalidFirstInput",
                "First input argument must be a UIControl object or cell array.");
        }

        var lines = new List<string>();
        foreach (string paragraph in paragraphs)
        {
            if (columns is { } limit)
            {
                WrapToColumns(paragraph, limit, lines);
            }
            else
            {
                WrapToWidth(paragraph, control!, lines);
            }
        }

        JgsValue wrapped = JgsValue.Cell([.. lines.Select(static wrappedLine => JgsValue.Str(wrappedLine))]);
        wrapped.Reshape(lines.Count, lines.Count == 0 ? 0 : 1);
        JgsValue position = JgsGraphicsProperties.Row(0, 0, 0, 0);
        if (control is not null)
        {
            // Where the control would sit: its corner, and the size of the lines as a text control
            // of its font measures them.
            Size2D size = MeasuredAsText(control, lines).ExtentPixels();
            Rect2D sized = UiUnitConverter.FromPixels(
                new Rect2D(1, 1, size.Width, size.Height), control.Units, control.ReferenceSize());
            position = JgsGraphicsProperties.Row(control.Position.X, control.Position.Y, sized.Width, sized.Height);
        }

        return [wrapped, position];
    }

    /// <summary>A detached text control with another's font, holding the lines to be measured.</summary>
    private static UiControlModel MeasuredAsText(UiControlModel like, IReadOnlyList<string> lines) => new()
    {
        Style = UiControlStyle.Text,
        FontName = like.FontName,
        FontUnits = like.FontUnits,
        FontSize = like.FontSize,
        FontWeight = like.FontWeight,
        FontAngle = like.FontAngle,
        Units = UiUnits.Pixels,
        Position = like.PixelPosition(),
        Text = lines.Count == 0 ? UiText.Empty : new UiText(UiTextForm.Cell, [.. lines]),
    };

    /// <summary>
    /// A paragraph as its words, each with the spaces after it; leading spaces are a word of their
    /// own. The last word is given one space, which is how R2025b counts it (probe <c>u3_wrap</c>).
    /// </summary>
    private static List<string> WrapTokens(string paragraph)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < paragraph.Length)
        {
            int start = i;
            while (i < paragraph.Length && paragraph[i] != ' ')
            {
                i++;
            }

            while (i < paragraph.Length && paragraph[i] == ' ')
            {
                i++;
            }

            tokens.Add(paragraph[start..i]);
        }

        if (tokens.Count > 0 && !tokens[^1].EndsWith(' '))
        {
            tokens[^1] += " ";
        }

        return tokens;
    }

    /// <summary>
    /// R2025b's wrap to a count of characters: words are gathered, each with its spaces, while they
    /// fit; a word longer than the count is cut into pieces of that many characters, a piece of
    /// spaces alone being dropped; and the paragraph's last line loses the spaces at its end. A
    /// paragraph with no words is one space.
    /// </summary>
    private static void WrapToColumns(string paragraph, int columns, List<string> lines)
    {
        if (paragraph.Trim(' ').Length == 0)
        {
            lines.Add(" ");
            return;
        }

        int first = lines.Count;
        string current = string.Empty;
        foreach (string token in WrapTokens(paragraph))
        {
            if (current.Length + token.Length <= columns)
            {
                current += token;
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
                current = string.Empty;
            }

            if (token.Length <= columns)
            {
                current = token;
                continue;
            }

            for (int from = 0; from < token.Length; from += columns)
            {
                string piece = token.Substring(from, System.Math.Min(columns, token.Length - from));
                if (piece.Trim(' ').Length > 0)
                {
                    lines.Add(piece);
                }
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        if (lines.Count > first)
        {
            lines[^1] = lines[^1].TrimEnd(' ');
        }
    }

    /// <summary>
    /// The wrap to a control's width: words are gathered while the line, measured as the control's
    /// font draws it with <c>Extent</c>'s margin, is no wider than the control. A word wider than the
    /// control keeps a line to itself. Lines carry no spaces at their ends.
    /// </summary>
    private static void WrapToWidth(string paragraph, UiControlModel control, List<string> lines)
    {
        if (paragraph.Trim(' ').Length == 0)
        {
            lines.Add(" ");
            return;
        }

        double limit = control.PixelPosition().Width;
        bool Fits(string candidate) => MeasuredAsText(control, [candidate]).ExtentPixels().Width <= limit;
        string current = string.Empty;
        foreach (string token in WrapTokens(paragraph))
        {
            string candidate = (current + token).TrimEnd(' ');
            if (current.Length == 0 || Fits(candidate))
            {
                current += token;
                continue;
            }

            lines.Add(current.TrimEnd(' '));
            current = token;
        }

        if (current.Length > 0)
        {
            lines.Add(current.TrimEnd(' '));
        }
    }

    /// <summary>
    /// <c>listfonts</c>: the machine's font families as a column cell, sorted without regard to case.
    /// <c>listfonts(h)</c> adds the font <c>h</c> names when the machine has no such family; any other
    /// argument is ignored, as R2025b ignores it.
    /// </summary>
    private static JgsValue ListFonts(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        var names = new List<string>(UiFonts.Families());
        if (args.Count == 1 && args[0].Type == JgsType.Number && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry)
            && JgsGraphicsProperties.TryFind(entry.Target, "FontName", out GraphicsProperty font)
            && font.Read(entry) is { } value && IsTextScalar(value))
        {
            string own = TextOf(value);
            if (own.Length > 0 && !names.Contains(own, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(own);
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
        }

        JgsValue list = JgsValue.Cell([.. names.Select(static name => JgsValue.Str(name))]);
        list.Reshape(names.Count, names.Count == 0 ? 0 : 1);
        return list;
    }
}
