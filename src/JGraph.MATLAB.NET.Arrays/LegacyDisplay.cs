using System.Text;

namespace JGraph.MATLAB.NET.Arrays;

/// <summary>
/// MATLAB's older display of cells and structs, which R2025b's MWArray API still answers from
/// <c>ToString</c> (compiler and SDK plan, stage K8; <c>k8_client</c>): a cell's elements as
/// <c>[5]</c>, <c>'two'</c> and <c>[2x2 double]</c> in aligned columns four blanks apart, a struct's
/// fields as <c>name: value</c> with the names right-aligned, a string as <c>[1x1 opaque]</c>.
/// </summary>
internal static class LegacyDisplay
{
    private enum Align
    {
        /// <summary>A number in brackets: padded on the left inside them.</summary>
        Number,

        /// <summary>A size and class in brackets or braces: padded on the right inside them.</summary>
        Describe,

        /// <summary>Anything else: padded on the left.</summary>
        Whole,
    }

    private readonly record struct Token(string Text, Align Align, string Open = "", string Close = "");

    /// <summary>A cell array's display.</summary>
    internal static string Cell(MWCellArray cell)
    {
        int[] dims = cell.Dimensions;
        if (cell.NumberOfElements == 0)
        {
            return "[]";
        }

        int rows = dims[0];
        int cols = dims[1];
        int pageSize = rows * cols;
        var lines = new List<string>();
        int pages = cell.NumberOfElements / pageSize;
        for (int p = 0; p < pages; p++)
        {
            if (dims.Length > 2)
            {
                var subscript = new StringBuilder("(:,:");
                int rest = p;
                for (int d = 2; d < dims.Length; d++)
                {
                    subscript.Append(',').Append((rest % dims[d]) + 1);
                    rest /= dims[d];
                }

                lines.Add(subscript + ") = ");
                lines.Add(string.Empty);
            }

            lines.AddRange(CellPage(cell, p * pageSize, rows, cols));
            if (dims.Length > 2)
            {
                lines.Add(string.Empty);
                lines.Add(string.Empty);
            }
        }

        return Finish(lines);
    }

    /// <summary>A struct array's display.</summary>
    internal static string Struct(MWStructArray record)
    {
        int count = record.NumberOfElements;
        IReadOnlyList<string> names = record.Names;
        if (count == 0)
        {
            return "[]";
        }

        if (count > 1)
        {
            string size = string.Join("x", record.Dimensions);
            return names.Count == 0
                ? $"  {size} struct array with no fields."
                : $"  {size} struct array with fields:\n\n" + string.Join("\n", names.Select(static n => "    " + n));
        }

        if (names.Count == 0)
        {
            return "struct with no fields.";
        }

        int width = names.Max(static n => n.Length);
        return Finish([.. names.Select((n, f) => "    " + n.PadLeft(width) + ": " + FieldValue(record.At(0, f)))]);
    }

    private static List<string> CellPage(MWCellArray cell, int offset, int rows, int cols)
    {
        var tokens = new Token[rows, cols];
        var widths = new int[cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                Token token = Element(cell.Cells[offset + r + (c * rows)]);
                tokens[r, c] = token;
                widths[c] = Math.Max(widths[c], token.Open.Length + token.Text.Length + token.Close.Length);
            }
        }

        // Columns as many as fit the window, four blanks before each.
        var groups = new List<(int First, int Last)>();
        int start = 0;
        int length = 0;
        for (int c = 0; c < cols; c++)
        {
            int added = 4 + widths[c];
            if (c > start && length + added > MatlabDisplay.Width - 1)
            {
                groups.Add((start, c - 1));
                start = c;
                length = 0;
            }

            length += added;
        }

        groups.Add((start, cols - 1));
        var lines = new List<string>();
        foreach ((int first, int last) in groups)
        {
            if (groups.Count > 1)
            {
                lines.Add(first == last ? $"  Column {first + 1}"
                    : $"  Columns {first + 1} through {last + 1}");
                lines.Add(string.Empty);
            }

            for (int r = 0; r < rows; r++)
            {
                var line = new StringBuilder();
                for (int c = first; c <= last; c++)
                {
                    line.Append("    ").Append(Padded(tokens[r, c], widths[c]));
                }

                lines.Add(line.ToString());
            }

            if (groups.Count > 1)
            {
                lines.Add(string.Empty);
            }
        }

        return lines;
    }

    private static string Padded(Token token, int width)
    {
        int inner = width - token.Open.Length - token.Close.Length;
        return token.Align switch
        {
            Align.Number => token.Open + token.Text.PadLeft(inner) + token.Close,
            Align.Describe => token.Open + token.Text.PadRight(inner) + token.Close,
            _ => (token.Open + token.Text + token.Close).PadLeft(width),
        };
    }

    /// <summary>How an element of a cell is written.</summary>
    private static Token Element(MWArray value)
    {
        string size = string.Join("x", value.Dimensions);
        switch (value)
        {
            case MWNumericArray number:
                return number.NumberOfElements == 0 ? new Token("[]", Align.Whole)
                    : number.NumberOfElements == 1 ? new Token(number[1].ToString(), Align.Number, "[", "]")
                    : new Token($"{size} {MWNumericArray.ClassName(number.NumericType)}", Align.Describe, "[", "]");
            case MWLogicalArray logical:
                return logical.NumberOfElements == 0 ? new Token("[]", Align.Whole)
                    : logical.NumberOfElements == 1 ? new Token(logical[1].ToString(), Align.Number, "[", "]")
                    : new Token($"{size} logical", Align.Describe, "[", "]");
            case MWCharArray text:
                return text.NumberOfElements == 0 ? new Token("''", Align.Whole)
                    : value.Dimensions is [1, _] ? new Token("'" + text + "'", Align.Whole)
                    : new Token($"{size} char", Align.Describe, "[", "]");
            case MWCellArray inner:
                return inner.NumberOfElements == 0 ? new Token("{}", Align.Whole) : new Token($"{size} cell", Align.Describe, "{", "}");
            case MWStructArray:
                return new Token($"{size} struct", Align.Describe, "[", "]");
            default:
                return new Token($"{size} opaque", Align.Describe, "[", "]");
        }
    }

    /// <summary>How a field of a struct is written.</summary>
    private static string FieldValue(MWArray value)
    {
        string size = string.Join("x", value.Dimensions);
        switch (value)
        {
            case MWNumericArray or MWLogicalArray when value.NumberOfElements == 0:
                return "[]";
            case MWNumericArray scalar when value.NumberOfElements == 1:
                return scalar[1].ToString(); // a sparse scalar shows its value (struct.mixed: sp)
            case MWLogicalArray scalar when value.NumberOfElements == 1:
                return scalar[1].ToString();
            case MWNumericArray number when value.Dimensions is [1, _] && !number.IsSparse:
                return Inline(number.NumberOfElements, i => number[i + 1].ToString(), size, MWNumericArray.ClassName(number.NumericType));
            case MWLogicalArray logical when value.Dimensions is [1, _] && !logical.IsSparse:
                return Inline(logical.NumberOfElements, i => logical[i + 1].ToString(), size, "logical");
            case MWNumericArray number:
                return $"[{size} {MWNumericArray.ClassName(number.NumericType)}]";
            case MWLogicalArray:
                return $"[{size} logical]";
            case MWCharArray text:
                return text.NumberOfElements == 0 ? "''" : value.Dimensions is [1, _] ? "'" + text + "'" : $"[{size} char]";
            case MWCellArray cell:
                if (cell.NumberOfElements == 0)
                {
                    return "{}";
                }

                return value.Dimensions is [1, _]
                    ? "{" + string.Join("  ", cell.Cells.Select(static c => { Token t = Element(c); return t.Open + t.Text + t.Close; })) + "}"
                    : $"{{{size} cell}}";
            case MWStructArray:
                return $"[{size} struct]";
            default:
                return $"[{size} opaque]";
        }
    }

    /// <summary>A row inline, <c>[1 2.5000]</c>; one too long for its field is cut short, as R2025b's (<c>struct.mixed</c>: longrow).</summary>
    private static string Inline(int count, Func<int, string> element, string size, string className)
    {
        var text = new StringBuilder("[");
        for (int i = 0; i < count; i++)
        {
            string next = (i > 0 ? " " : "") + element(i);
            if (text.Length + next.Length > 50)
            {
                return text + " . ] (" + size + " " + className + ")";
            }

            text.Append(next);
        }

        return text.Append(']').ToString();
    }

    private static string Finish(List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        return lines.Count == 1 ? lines[0].Trim() : string.Join("\n", lines);
    }
}
