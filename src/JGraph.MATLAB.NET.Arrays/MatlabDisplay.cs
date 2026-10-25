using System.Globalization;
using System.Text;

namespace JGraph.MATLAB.NET.Arrays;

/// <summary>
/// MATLAB's <c>format short</c> display of a number, logical or char array, dense or sparse, as R2025b's
/// <c>disp</c> draws it and an MWArray's <c>ToString</c> answers it (compiler and SDK plan, stage K8;
/// <c>probe_k8_disp</c> over 120 values and <c>k8_client</c>). The rules, as measured:
/// <list type="bullet">
/// <item>Whole numbers below 1000 in fields of 6, below 1e9 in fields of 12; Inf and NaN count as whole.</item>
/// <item>Other values to 4 decimals in fields of 10, under a common <c>1.0e+03 *</c> scale when the largest
/// is 1000 or more or under 0.001; a scalar is <c>1.2346e+05</c> there instead.</item>
/// <item>Integer classes and logicals in fields of the widest number's digits, its sign, and 3.</item>
/// <item>Complex values always to 4 decimals, <c>re + imi</c>; an N-D array page by page under <c>(:,:,k) =</c>.</item>
/// <item>A row wider than the window splits into <c>Columns a through b</c>.</item>
/// </list>
/// JGraph's own <c>disp</c> keeps JGraph's layout; this is the MWArray API's, which clients of R2025b's
/// assemblies read.
/// </summary>
internal static class MatlabDisplay
{
    /// <summary>The window width a ToString lays a wide row out for.</summary>
    internal const int Width = 80;

    /// <summary>One element's text, already padded to the column's field.</summary>
    private sealed record Layout(string[] Header, int Field, Func<int, string> Cell);

    /// <summary>A numeric array: real parts, imaginary parts (null for real), its class.</summary>
    internal static string Numeric(int[] dims, double[] re, double[] im, MWNumericType type)
    {
        if (Count(dims) == 0)
        {
            return "[]";
        }

        return Pages(dims, (offset, rows, cols) => Page(rows, cols, NumericLayout(re, im, type, offset, rows * cols, rows * cols == 1)));
    }

    /// <summary>A logical array.</summary>
    internal static string Logical(int[] dims, bool[] values)
    {
        if (Count(dims) == 0)
        {
            return "[]";
        }

        return Pages(dims, (offset, rows, cols) => Page(rows, cols, new Layout([], 4, i => (values[offset + i] ? "1" : "0").PadLeft(4))));
    }

    /// <summary>A char array: its rows, one per line, as disp shows text.</summary>
    internal static string Char(int[] dims, char[] values)
    {
        if (Count(dims) == 0)
        {
            return string.Empty;
        }

        return Pages(dims, (offset, rows, cols) =>
        {
            var lines = new List<string>(rows);
            for (int r = 0; r < rows; r++)
            {
                var line = new StringBuilder(cols);
                for (int c = 0; c < cols; c++)
                {
                    char ch = values[offset + r + (c * rows)];
                    line.Append(ch == '\0' ? ' ' : ch);
                }

                lines.Add(line.ToString());
            }

            return lines;
        }, text: true);
    }

    /// <summary>A sparse matrix: one line per stored value, <c>(r,c)</c> and the value, or <c>All zero</c>.</summary>
    internal static string Sparse(int rows, int cols, double[] re, double[] im, bool logical)
    {
        var stored = new List<int>();
        for (int i = 0; i < re.Length; i++)
        {
            if (re[i] != 0 || (im is not null && im[i] != 0) || double.IsNaN(re[i]))
            {
                stored.Add(i);
            }
        }

        if (stored.Count == 0)
        {
            return "All zero"; // disp's "   All zero", one line and so trimmed
        }

        double[] keptRe = [.. stored.Select(i => re[i])];
        double[] keptIm = im is null ? null : [.. stored.Select(i => im[i])];
        Layout layout = logical
            ? new Layout([], 4, i => (keptRe[i] != 0 ? "1" : "0").PadLeft(4))
            : NumericLayout(keptRe, keptIm, MWNumericType.Double, 0, keptRe.Length, scalar: false);
        string[] indices = [.. stored.Select(i => $"({(i % rows) + 1},{(i / rows) + 1})")];
        int indexField = Math.Max(8, indices.Max(static s => s.Length) + 2);
        int valueField = 11 + layout.Field - indexField;
        var lines = new List<string>(layout.Header);
        for (int k = 0; k < stored.Count; k++)
        {
            lines.Add(indices[k].PadLeft(indexField) + layout.Cell(k).TrimStart().PadLeft(valueField));
        }

        return lines.Count == 1 ? lines[0].Trim() : string.Join("\n", lines);
    }

    /// <summary>How each element of a numeric page is written, and the header (a scale factor) above it.</summary>
    private static Layout NumericLayout(double[] re, double[] im, MWNumericType type, int offset, int count, bool scalar)
    {
        if (type is not (MWNumericType.Double or MWNumericType.Single))
        {
            return IntegerLayout(re, im, offset, count);
        }

        double largest = 0;
        bool whole = im is null;
        for (int i = 0; i < count; i++)
        {
            foreach (double part in im is null ? [re[offset + i]] : new[] { re[offset + i], im[offset + i] })
            {
                if (double.IsFinite(part))
                {
                    largest = Math.Max(largest, Math.Abs(part));
                    whole &= part == Math.Floor(part);
                }
            }
        }

        if (whole && largest < 1e9)
        {
            int field = largest < 1000 ? 6 : 12;
            return new Layout([], field, i => Whole(re[offset + i]).PadLeft(field));
        }

        // 0.001 itself is outside: a scalar of it is 1.0000e-03 (probe_k8_disp: small), 0.0025 is not (scalesmall).
        bool outside = largest >= 1000 || (largest > 0 && largest <= 0.001);
        if (scalar && outside)
        {
            return new Layout([], 0, i => "   " + (im is null
                ? Exponent(re[offset + i])
                : $"{Exponent(re[offset + i])} {(im[offset + i] < 0 || double.IsNegative(im[offset + i]) ? '-' : '+')} {Exponent(Math.Abs(im[offset + i]))}i"));
        }

        double scale = 1;
        string[] header = [];
        if (outside)
        {
            int exponent = (int)Math.Floor(Math.Log10(largest));
            if (largest < 0.001)
            {
                exponent++;
            }

            scale = Math.Pow(10, exponent);
            header = [$"   1.0e{(exponent < 0 ? '-' : '+')}{Math.Abs(exponent):00} *", ""];
        }

        if (im is null)
        {
            // Below 1000 once scaled, so four decimals always fit a field of 10 (fracrow999).
            return new Layout(header, 10, i => Fixed(re[offset + i] / scale).PadLeft(10));
        }

        int width = IntegerDigits(largest / scale) + 5;
        return new Layout(header, width + 3 + 3 + width + 1, i =>
        {
            double imag = im[offset + i];
            string sign = imag < 0 || double.IsNegative(imag) ? "-" : "+";
            return Fixed(re[offset + i] / scale).PadLeft(width + 3) + " " + sign + " " + Fixed(Math.Abs(imag) / scale).PadLeft(width) + "i";
        });
    }

    /// <summary>An integer class: fields of the widest number's digits, a sign where any is negative, and 3.</summary>
    private static Layout IntegerLayout(double[] re, double[] im, int offset, int count)
    {
        int digits = 1;
        bool negative = false;
        for (int i = 0; i < count; i++)
        {
            foreach (double part in im is null ? [re[offset + i]] : new[] { re[offset + i], im[offset + i] })
            {
                digits = Math.Max(digits, Whole(Math.Abs(part)).Length);
                negative |= part < 0;
            }
        }

        int field = digits + (negative ? 1 : 0) + 3;
        if (im is null)
        {
            return new Layout([], field, i => Whole(re[offset + i]).PadLeft(field));
        }

        return new Layout([], field + 3 + field, i =>
        {
            double imag = im[offset + i];
            return Whole(re[offset + i]).PadLeft(field) + " " + (imag < 0 ? "-" : "+") + " " + Whole(Math.Abs(imag)).PadLeft(field - 1) + "i";
        });
    }

    /// <summary>A page's lines: its rows, split into column groups when wider than the window.</summary>
    private static List<string> Page(int rows, int cols, Layout layout)
    {
        var lines = new List<string>(layout.Header);
        int perGroup = layout.Field == 0 ? cols : Math.Max(1, (Width - 1) / layout.Field);
        if (cols <= perGroup)
        {
            for (int r = 0; r < rows; r++)
            {
                var line = new StringBuilder();
                for (int c = 0; c < cols; c++)
                {
                    line.Append(layout.Cell(r + (c * rows)));
                }

                lines.Add(line.ToString());
            }

            return lines;
        }

        for (int first = 0; first < cols; first += perGroup)
        {
            int last = Math.Min(cols, first + perGroup) - 1;
            lines.Add(first == last ? $"  Column {first + 1}"
                : $"  Columns {first + 1} through {last + 1}");
            lines.Add(string.Empty);
            for (int r = 0; r < rows; r++)
            {
                var line = new StringBuilder();
                for (int c = first; c <= last; c++)
                {
                    line.Append(layout.Cell(r + (c * rows)));
                }

                lines.Add(line.ToString());
            }

            lines.Add(string.Empty);
        }

        return lines;
    }

    /// <summary>
    /// The pages of an N-D array, each under <c>(:,:,k) =</c> with blank lines between, as disp writes
    /// them; one page alone stands bare. Leading and trailing blank lines are dropped, and a display of
    /// one line is trimmed, as ToString answers it.
    /// </summary>
    private static string Pages(int[] dims, Func<int, int, int, List<string>> page, bool text = false)
    {
        int rows = dims[0];
        int cols = dims[1];
        int pages = Count(dims) / Math.Max(1, rows * cols);
        var lines = new List<string>();
        if (dims.Length == 2)
        {
            lines.AddRange(page(0, rows, cols));
        }
        else
        {
            for (int p = 0; p < pages; p++)
            {
                var subscript = new StringBuilder("(:,:");
                int rest = p;
                for (int d = 2; d < dims.Length; d++)
                {
                    subscript.Append(',').Append((rest % dims[d]) + 1);
                    rest /= dims[d];
                }

                lines.Add(subscript + ") =");
                lines.Add(string.Empty);
                lines.AddRange(page(p * rows * cols, rows, cols));
                lines.Add(string.Empty);
                lines.Add(string.Empty);
            }
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        return lines.Count == 1 && !text ? lines[0].Trim() : string.Join("\n", lines);
    }

    /// <summary>A scalar's short text as a cell or struct shows it inline: <c>5</c>, <c>2.5000</c>, <c>1.0000 + 2.0000i</c>.</summary>
    internal static string Scalar(double re, double? im, MWNumericType type) =>
        Numeric([1, 1], [re], im is { } i ? [i] : null, type);

    internal static int Count(int[] dims)
    {
        int count = 1;
        foreach (int d in dims)
        {
            count *= d;
        }

        return count;
    }

    private static string Whole(double x) => x switch
    {
        double.PositiveInfinity => "Inf",
        double.NegativeInfinity => "-Inf",
        _ when double.IsNaN(x) => "NaN",
        _ => (x == 0 ? 0 : x).ToString("F0", CultureInfo.InvariantCulture),
    };

    private static string Fixed(double x) => double.IsFinite(x) ? (x == 0 ? 0 : x).ToString("F4", CultureInfo.InvariantCulture) : Whole(x);

    private static string Exponent(double x) => double.IsFinite(x) ? x.ToString("0.0000e+00", CultureInfo.InvariantCulture) : Whole(x);

    private static int IntegerDigits(double magnitude) => magnitude < 10 ? 1 : (int)Math.Floor(Math.Log10(magnitude)) + 1;
}
