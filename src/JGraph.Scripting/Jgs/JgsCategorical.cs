using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a categorical array remembers about itself (open item 115): its category names in their
/// order, whether that order means something (<c>'Ordinal'</c>), and whether the set is closed to a
/// write of a new name (<c>'Protected'</c>, which an ordinal array always is). The elements are an
/// ordinary numeric array underneath, one code a slot counting from one and NaN for
/// <c>&lt;undefined&gt;</c>, so indexing, growth, masks and shapes are the array machinery that was
/// already there, and this tag is the whole of what makes one a categorical — the same arrangement a
/// datetime has with <see cref="JgsTimeTag"/>.
/// </summary>
internal sealed record JgsCategoricalTag(string[] Categories, bool Ordinal, bool Protected)
{
    /// <summary>The code of a name, counting from one, or 0 when the name is not a category.</summary>
    public int CodeOf(string name) => Array.IndexOf(Categories, name) + 1;

    /// <summary>Whether two tags name the same categories in the same order.</summary>
    public bool SameCategories(JgsCategoricalTag other) => Categories.AsSpan().SequenceEqual(other.Categories);
}

/// <summary>
/// The arithmetic behind <c>categorical</c> (open item 115; probe_115 and probe_115b in the batch 19
/// scratch): construction from data, the names and codes of an array, R2025b's display, the merging
/// of two arrays' categories, comparison, and the conversions.
/// </summary>
internal static class JgsCategorical
{
    /// <summary>The word a missing element shows as.</summary>
    public const string Undefined = "<undefined>";

    // --- Codes and names ----------------------------------------------------------------------

    /// <summary>A categorical over <paramref name="codes"/> laid out column-major in the given shape.</summary>
    public static JgsValue FromCodes(double[] codes, int rows, int cols, JgsCategoricalTag tag) =>
        JgsMatrix.FromColumnMajor(codes, rows, cols).MarkCategorical(tag);

    /// <summary>A categorical over <paramref name="codes"/> in the shape of <paramref name="shapeOf"/>.</summary>
    public static JgsValue FromCodesLike(double[] codes, JgsValue shapeOf, JgsCategoricalTag tag)
    {
        JgsValue made = JgsMatrix.FromColumnMajor(codes, shapeOf.Rows, shapeOf.Cols);
        if (shapeOf.IsNd)
        {
            made = JgsMatrix.Like(shapeOf, made);
        }

        return made.MarkCategorical(tag);
    }

    /// <summary>The codes of a categorical, a copy, NaN for an undefined element.</summary>
    public static double[] CodesOf(JgsValue value)
    {
        if (value.Type == JgsType.Number)
        {
            return [value.AsNumber];
        }

        var codes = new double[value.ArrayLength];
        for (int i = 0; i < codes.Length; i++)
        {
            codes[i] = value.ElementAt(i).AsNumber;
        }

        return codes;
    }

    /// <summary>A number (or NaN) as a value a plain array reads it: packed code from a category count.</summary>
    public static string NameOf(JgsCategoricalTag tag, double code) =>
        double.IsNaN(code) || code < 1 || code > tag.Categories.Length ? Undefined : tag.Categories[(int)code - 1];

    /// <summary>Every element's name, in storage order.</summary>
    public static string[] NamesOf(JgsValue value)
    {
        JgsCategoricalTag tag = value.CategoricalTag!;
        double[] codes = CodesOf(value);
        var names = new string[codes.Length];
        for (int i = 0; i < codes.Length; i++)
        {
            names[i] = NameOf(tag, codes[i]);
        }

        return names;
    }

    /// <summary>A one-element categorical from a numeric reading, as a selection out of one is still one.</summary>
    public static JgsValue Wrap(JgsValue numeric, JgsCategoricalTag tag)
    {
        if (numeric.Type is JgsType.Number or JgsType.Bool)
        {
            return JgsValue.Array([JgsValue.Number(numeric.Type == JgsType.Bool ? (numeric.AsBool ? 1 : 0) : numeric.AsNumber)]).MarkCategorical(tag);
        }

        return numeric.Type == JgsType.Array ? numeric.MarkCategorical(tag) : numeric;
    }

    /// <summary>The category names as a cell column, as <c>categories</c> answers.</summary>
    public static JgsValue CategoriesColumn(JgsCategoricalTag tag)
    {
        JgsValue cell = JgsValue.Cell([.. tag.Categories.Select(JgsValue.Str)]);
        cell.Reshape(tag.Categories.Length, tag.Categories.Length == 0 ? 0 : 1);
        return cell;
    }

    // --- Names from data ----------------------------------------------------------------------

    /// <summary>
    /// A number as R2025b names a category made from one: five significant digits, an exponent in
    /// the <c>1e+06</c> spelling, <c>true</c> and <c>false</c> for a logical.
    /// </summary>
    public static string NumberName(double value)
    {
        if (double.IsNaN(value))
        {
            return Undefined;
        }

        if (double.IsPositiveInfinity(value))
        {
            return "Inf";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-Inf";
        }

        if (value == Math.Floor(value) && Math.Abs(value) < 1e5)
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }

        string text = value.ToString("G5", CultureInfo.InvariantCulture);
        int e = text.IndexOf('E');
        if (e < 0)
        {
            return text;
        }

        string mantissa = text[..e];
        int exponent = int.Parse(text[(e + 1)..], CultureInfo.InvariantCulture);
        return mantissa + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>Category names sorted as R2025b sorts them: numbers by value, text by character code.</summary>
    public static string[] SortedNames(IEnumerable<string> names, bool numeric) =>
        numeric
            ? [.. names.Distinct(StringComparer.Ordinal).OrderBy(NumberOfName)]
            : [.. names.Distinct(StringComparer.Ordinal).OrderBy(static n => n, StringComparer.Ordinal)];

    /// <summary>The number a numeric category's name spells, MATLAB's Inf and -Inf included (-Inf sorts first).</summary>
    private static double NumberOfName(string name) => name switch
    {
        "Inf" => double.PositiveInfinity,
        "-Inf" => double.NegativeInfinity,
        "NaN" => double.NaN,
        _ => double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.PositiveInfinity,
    };

    // --- Display ------------------------------------------------------------------------------

    /// <summary>
    /// The rows R2025b's <c>disp</c> prints: each column as wide as its widest name, five spaces
    /// before the first, six between, one after.
    /// </summary>
    public static string Format(JgsValue value)
    {
        JgsCategoricalTag tag = value.CategoricalTag!;
        double[] codes = CodesOf(value);
        int rows = value.Type == JgsType.Number ? 1 : value.Rows;
        int cols = value.Type == JgsType.Number ? 1 : value.Cols;
        if (codes.Length == 0 || rows == 0 || cols == 0)
        {
            return string.Empty;
        }

        var widths = new int[cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                widths[c] = Math.Max(widths[c], NameOf(tag, codes[r + (c * rows)]).Length);
            }
        }

        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            sb.Append("     ");
            for (int c = 0; c < cols; c++)
            {
                if (c > 0)
                {
                    sb.Append("      ");
                }

                sb.Append(NameOf(tag, codes[r + (c * rows)]).PadRight(widths[c]));
            }

            sb.Append(" \n");
        }

        return sb.ToString();
    }

    /// <summary>The header a bare name's echo shows: the size and class, as R2025b writes them.</summary>
    public static string Header(JgsValue value)
    {
        int rows = value.Type == JgsType.Number ? 1 : value.Rows;
        int cols = value.Type == JgsType.Number ? 1 : value.Cols;
        if (value.Type != JgsType.Number && value.ArrayLength == 0)
        {
            return $"{rows}×{cols} empty categorical array";
        }

        return rows == 1 && cols == 1 ? "categorical" : $"{rows}×{cols} categorical array";
    }

    // --- Categories merged and moved ----------------------------------------------------------

    /// <summary>
    /// The categories two arrays share once joined: the first's in its order, then the second's new
    /// ones in theirs. An ordinal array joins only an ordinal array with the same categories, a
    /// protected one only an array whose categories it already has (R2025b's refusals, probe_115).
    /// </summary>
    public static JgsCategoricalTag Merge(JgsCategoricalTag first, JgsCategoricalTag second, int line, int col)
    {
        if (first.Ordinal != second.Ordinal)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:OrdinalMismatchConcatenate",
                "Unable to concatenate a categorical array that is ordinal with one that is not.");
        }

        if (first.SameCategories(second))
        {
            return first with { Protected = first.Protected || second.Protected };
        }

        if (first.Ordinal)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:OrdinalCategoriesMismatch",
                "Ordinal arrays must have the same categories, including their order.");
        }

        var merged = new List<string>(first.Categories);
        foreach (string name in second.Categories)
        {
            if (!merged.Contains(name, StringComparer.Ordinal))
            {
                merged.Add(name);
            }
        }

        bool firstHoldsAll = second.Categories.All(n => first.Categories.Contains(n, StringComparer.Ordinal));
        bool secondHoldsAll = first.Categories.All(n => second.Categories.Contains(n, StringComparer.Ordinal));
        if ((first.Protected && !firstHoldsAll) || (second.Protected && !secondHoldsAll))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:ProtectedForCombination",
                "One or more of the arrays is categorical and either ordinal or protected, and the arrays do not all have the same sets of categories.  Use ADDCATS to add new categories.");
        }

        return new JgsCategoricalTag([.. merged], false, first.Protected || second.Protected);
    }

    /// <summary>The codes of an array under one tag read in another: a name keeps its identity, a missing name is undefined.</summary>
    public static double[] Remap(double[] codes, JgsCategoricalTag from, JgsCategoricalTag to)
    {
        var map = new double[from.Categories.Length + 1];
        map[0] = double.NaN;
        for (int i = 0; i < from.Categories.Length; i++)
        {
            int code = to.CodeOf(from.Categories[i]);
            map[i + 1] = code == 0 ? double.NaN : code;
        }

        var moved = new double[codes.Length];
        for (int i = 0; i < codes.Length; i++)
        {
            moved[i] = double.IsNaN(codes[i]) ? double.NaN : map[(int)codes[i]];
        }

        return moved;
    }

    /// <summary>
    /// Names written into an array: each becomes its code, a new name becoming a new category at the
    /// end unless the set is protected, and an empty, missing or <c>&lt;undefined&gt;</c> name the
    /// undefined code. The tag that comes back has the categories the write added.
    /// </summary>
    public static (double[] Codes, JgsCategoricalTag Tag) Absorb(JgsCategoricalTag tag, IReadOnlyList<string?> names, int line, int col)
    {
        var categories = new List<string>(tag.Categories);
        var codes = new double[names.Count];
        for (int i = 0; i < names.Count; i++)
        {
            string? name = names[i]?.Trim();
            if (string.IsNullOrEmpty(name) || name == Undefined)
            {
                codes[i] = double.NaN;
                continue;
            }

            int code = categories.IndexOf(name) + 1;
            if (code == 0)
            {
                if (tag.Protected)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:categorical:ProtectedForAssign",
                        $"Cannot add a new category '{name}' to this categorical array because its categories are protected.  Use ADDCATS to add the new category.");
                }

                categories.Add(name);
                code = categories.Count;
            }

            codes[i] = code;
        }

        return (codes, categories.Count == tag.Categories.Length ? tag : tag with { Categories = [.. categories] });
    }

    /// <summary>The names a right-hand side or a bracket piece of text stands for; null for anything that is not text or a categorical.</summary>
    public static string?[]? TextNames(JgsValue value)
    {
        if (value.IsCategorical)
        {
            return NamesOf(value).Select(static n => n == Undefined ? null : n).ToArray();
        }

        if (value.Type == JgsType.String)
        {
            return [JgsBuiltins.IsMissingText(value.AsString) ? null : value.AsString]; // the bare missing writes an undefined
        }

        if (value.IsStringArray)
        {
            var names = new string?[value.ArrayLength];
            for (int i = 0; i < names.Length; i++)
            {
                JgsValue element = value.ElementAt(i);
                names[i] = element.Type == JgsType.String && !JgsBuiltins.IsMissingText(element.AsString) ? element.AsString : null;
            }

            return names;
        }

        if (value.Type == JgsType.Cell && value.AsCell.All(static e => e.Type == JgsType.String))
        {
            return value.AsCell.Select(static e => (string?)e.AsString).ToArray();
        }

        if (value.Type == JgsType.Array && value.ArrayLength == 0)
        {
            return [];
        }

        return null;
    }

    // --- Comparison ---------------------------------------------------------------------------

    /// <summary>
    /// <c>==</c> and <c>~=</c> between a categorical and a categorical or text (name by name, a
    /// scalar expanded), and the four orderings for ordinal arrays with the same categories.
    /// </summary>
    public static JgsValue Compare(TokenType op, JgsValue left, JgsValue right, int line, int col)
    {
        bool ordering = op is TokenType.Less or TokenType.LessEqual or TokenType.Greater or TokenType.GreaterEqual;
        if (op is not (TokenType.EqualEqual or TokenType.BangEqual) && !ordering)
        {
            throw NotNumeric(line, col);
        }

        JgsValue shaped = left.IsCategorical ? left : right;
        if (left.IsCategorical && right.IsCategorical && left.ArrayLength != 1 && right.ArrayLength != 1 && (left.Rows != right.Rows || left.Cols != right.Cols))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:dimagree", "Matrix dimensions must agree.");
        }

        string?[] a = NamesFor(left, right, line, col);
        string?[] b = NamesFor(right, left, line, col);
        bool bothCategorical = left.IsCategorical && right.IsCategorical;
        if (bothCategorical && left.CategoricalTag!.Ordinal && right.CategoricalTag!.Ordinal
            && !left.CategoricalTag!.SameCategories(right.CategoricalTag!))
        {
            // Two ordinal arrays compare only over one category list, whatever the operator.
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:InvalidOrdinalComparison",
                "Ordinal arrays must have the same categories, including their order.");
        }

        if (ordering)
        {
            JgsCategoricalTag tag = shaped.CategoricalTag!;
            if (!tag.Ordinal || (bothCategorical && (!left.CategoricalTag!.Ordinal || !right.CategoricalTag!.Ordinal)))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:categorical:NotOrdinal",
                    "Relational comparisons are not allowed for categorical arrays that are not ordinal.");
            }

            foreach (string? name in (left.IsCategorical ? b : a))
            {
                if (name is not null && tag.CodeOf(name) == 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:categorical:ProtectedForCombination",
                        "One or more of the arrays is categorical and either ordinal or protected, and the arrays do not all have the same sets of categories.  Use ADDCATS to add new categories.");
                }
            }

            int count = Math.Max(a.Length, b.Length);
            var ranks = new JgsValue[count];
            for (int i = 0; i < count; i++)
            {
                string? x = a[a.Length == 1 ? 0 : i];
                string? y = b[b.Length == 1 ? 0 : i];
                int cx = x is null ? 0 : tag.CodeOf(x);
                int cy = y is null ? 0 : tag.CodeOf(y);
                bool answer = cx != 0 && cy != 0 && op switch
                {
                    TokenType.Less => cx < cy,
                    TokenType.LessEqual => cx <= cy,
                    TokenType.Greater => cx > cy,
                    _ => cx >= cy,
                };
                ranks[i] = JgsValue.Bool(answer);
            }

            return Shaped(ranks, shaped, a.Length >= b.Length ? left : right);
        }

        int n = Math.Max(a.Length, b.Length);
        var flags = new JgsValue[n];
        for (int i = 0; i < n; i++)
        {
            string? x = a[a.Length == 1 ? 0 : i];
            string? y = b[b.Length == 1 ? 0 : i];
            bool same = x is not null && y is not null && x == y;
            flags[i] = JgsValue.Bool(op == TokenType.EqualEqual ? same : !same);
        }

        return Shaped(flags, shaped, a.Length >= b.Length ? left : right);
    }

    private static JgsValue Shaped(JgsValue[] flags, JgsValue fallback, JgsValue shapeOf)
    {
        if (flags.Length == 1)
        {
            return flags[0];
        }

        JgsValue source = shapeOf.Type == JgsType.Array && shapeOf.ArrayLength == flags.Length ? shapeOf : fallback;
        return source.Type == JgsType.Array && source.ArrayLength == flags.Length
            ? JgsMatrix.FromElements(flags, source.Rows, source.Cols)
            : JgsMatrix.FromElements(flags, 1, flags.Length);
    }

    private static string?[] NamesFor(JgsValue side, JgsValue other, int line, int col)
    {
        if (side.IsCategorical)
        {
            return NamesOf(side).Select(static n => n == Undefined ? null : n).ToArray();
        }

        string?[]? names = TextNames(side);
        if (names is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:categorical:InvalidComparisonTypes",
                $"Comparison is not defined between categorical and {JgsBuiltins.ClassOf(side, JgsDialect.Matlab)} arrays.");
        }

        if (side.Type == JgsType.Cell && names.Length != 1 && other.IsCategorical && other.ArrayLength != 1 && names.Length != other.ArrayLength)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:dimagree", "Matrix dimensions must agree.");
        }

        return names;
    }

    /// <summary>R2025b's refusal of arithmetic, a logical conversion and the like on a categorical.</summary>
    public static JgsRuntimeException NotNumeric(int line, int col) =>
        new(line, col, "MATLAB:math:mustBeNumericCharOrLogical", "Invalid data type. Argument must be numeric, char, or logical.");

    // --- Conversions --------------------------------------------------------------------------

    /// <summary>The names as a string array in the array's shape, an undefined element the missing string.</summary>
    public static JgsValue ToStringArray(JgsValue value)
    {
        string[] names = NamesOf(value);
        var elements = new JgsValue[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            elements[i] = JgsValue.Str(names[i] == Undefined ? JgsBuiltins.MissingSentinel : names[i]);
        }

        return JgsValue.StringArray(elements, value.Rows, value.Cols);
    }

    /// <summary>The names as a cell of char rows in the array's shape, an undefined element the word.</summary>
    public static JgsValue ToCellstr(JgsValue value)
    {
        string[] names = NamesOf(value);
        JgsValue cell = JgsValue.Cell([.. names.Select(JgsValue.Str)]);
        cell.Reshape(value.Rows, value.Cols);
        return cell;
    }

    /// <summary>The codes as a plain double array in the array's shape, an undefined element NaN.</summary>
    public static JgsValue ToDouble(JgsValue value) =>
        JgsMatrix.FromColumnMajor(CodesOf(value), value.Rows, value.Cols);
}
