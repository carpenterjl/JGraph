using System.Globalization;
using System.Text;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Designer;

/// <summary>
/// Reads and writes the right-hand sides of App Designer's <c>createComponents</c> lines (designer
/// plan DS1), and compares values as MATLAB's <c>isequal</c> does, which is how App Designer
/// decides whether a property is at its default.
/// </summary>
/// <remarks>
/// <para>
/// Writing transcribes R2025b's <c>ComponentCodeGenerator.propertyValueToString</c>: a double or
/// logical through <c>mat2str</c>; a char with its quotes doubled and each line break written
/// <c>['a' newline 'b']</c>; a one-element cell of a char or a number as <c>{'x'}</c> or
/// <c>{%g}</c>; a cell vector with its first element opening the braces and each further one after
/// <c>,</c> (a row) or <c>;</c> (a column) and a space, numbers by <c>%g</c>; an on/off value as
/// <c>'on'</c> or <c>'off'</c>; a date as <c>datetime([y m d])</c> or <c>NaT</c>. Callbacks, other
/// components and images beside the app take the forms App Designer's code model writes.
/// </para>
/// <para>
/// Where R2025b writes something that does not read back as the value, this writes what does, as
/// the project's rule is (correctness before parity): a string with a line break, which R2025b
/// writes with the break dropped (<c>"a" + "b"</c>), is written <c>"a" + newline + "b"</c>; a cell
/// R2025b cannot write - a matrix, or one holding a string or a non-scalar - is written as a cell
/// literal of its items, where R2025b leaves the property's line empty or drops it.
/// </para>
/// <para>
/// Reading takes every form writing makes and nothing else: text it does not recognise is kept as an
/// <see cref="ExpressionValue"/>, written back as it was.
/// </para>
/// </remarks>
public static class DesignValueText
{
    /// <summary>The name the generated code calls the app: <c>app</c>.</summary>
    public const string AppObjectName = "app";

    /// <summary>The variable that holds the app's folder in <c>createComponents</c> when it uses images beside the app.</summary>
    public const string PathToApp = "pathToMLAPP";

    // --- writing ---------------------------------------------------------------------------------

    /// <summary>Writes <paramref name="value"/> as the right-hand side App Designer writes for it.</summary>
    public static string Format(DesignValue value, string objectName = AppObjectName)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            NumericValue n => Mat2Str(n),
            CharValue c => CharText(c.Text),
            StringValue s => StringText(s.Text),
            CellValue cell => CellText(cell, objectName),
            OnOffValue o => o.IsOn ? "'on'" : "'off'",
            DatetimeValue d => DatetimeText(d),
            CallbackValue cb => $"createCallbackFcn({objectName}, @{cb.Method}, true)",
            ComponentsValue refs => refs.Bracketed
                ? "[" + string.Join(", ", refs.CodeNames.Select(n => objectName + "." + n)) + "]"
                : string.Join(", ", refs.CodeNames.Select(n => objectName + "." + n)),
            ImageFileValue image => $"fullfile({PathToApp}, '" + string.Join("', '", image.Parts) + "')",
            ExpressionValue e => e.Text,
            _ => throw new ArgumentException($"A {value.GetType().Name} has no written form.", nameof(value)),
        };
    }

    /// <summary><c>mat2str</c> of a double or logical matrix: fifteen significant digits, <c>[1 2;3 4]</c>.</summary>
    public static string Mat2Str(NumericValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.IsEmpty)
        {
            return value.Rows == 0 && value.Cols == 0
                ? "[]"
                : "zeros(" + value.Rows.ToString(CultureInfo.InvariantCulture) + ","
                    + value.Cols.ToString(CultureInfo.InvariantCulture) + ")";
        }

        string One(double x) => value.IsLogical ? (x != 0 ? "true" : "false") : Number(x, "%.15g");
        if (value.Data.Count == 1)
        {
            return One(value.Data[0]);
        }

        var rows = new List<string>(value.Rows);
        for (int r = 0; r < value.Rows; r++)
        {
            var row = new List<string>(value.Cols);
            for (int c = 0; c < value.Cols; c++)
            {
                row.Add(One(value.Data[r + (c * value.Rows)]));
            }

            rows.Add(string.Join(" ", row));
        }

        return "[" + string.Join(";", rows) + "]";
    }

    /// <summary>A char row as a literal: quotes doubled, each line break written as <c>newline</c> between pieces.</summary>
    public static string CharText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] pieces = text.Replace("'", "''", StringComparison.Ordinal).Split('\n');
        return pieces.Length > 1
            ? "['" + string.Join("' newline '", pieces) + "']"
            : "'" + pieces[0] + "'";
    }

    /// <summary>A string scalar as a literal, each line break written as <c>+ newline +</c> between pieces.</summary>
    public static string StringText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return "\"" + string.Join("\" + newline + \"", text.Split('\n')) + "\"";
    }

    private static string CellText(CellValue cell, string objectName)
    {
        if (cell.Items.Count == 0)
        {
            return "{}";
        }

        bool vector = cell.Rows == 1 || cell.Cols == 1;
        if (cell.Items.Count == 1)
        {
            switch (cell.Items[0])
            {
                case CharValue c:
                    return "{" + CharText(c.Text) + "}";
                case NumericValue n when n.Data.Count == 1:
                    return "{" + Number(n.Data[0], "%g") + "}";
                default:
                    break;
            }
        }
        else if (vector && cell.Items.All(static item => item is CharValue || (item is NumericValue n && n.Data.Count == 1)))
        {
            string separator = cell.Cols == cell.Items.Count ? "," : ";";
            var text = new StringBuilder("{");
            for (int i = 0; i < cell.Items.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(separator).Append(' ');
                }

                text.Append(cell.Items[i] switch
                {
                    CharValue c => CharText(c.Text),
                    NumericValue { IsLogical: true } n => n.Data[0] != 0 ? "true" : "false",
                    NumericValue n => Number(n.Data[0], "%g"),
                    _ => throw new InvalidOperationException(),
                });
            }

            return text.Append('}').ToString();
        }

        // Beyond what R2025b writes: a cell literal of the items' own forms, row by row.
        var rows = new List<string>(cell.Rows);
        for (int r = 0; r < cell.Rows; r++)
        {
            var row = new List<string>(cell.Cols);
            for (int c = 0; c < cell.Cols; c++)
            {
                row.Add(Format(cell.Items[r + (c * cell.Rows)], objectName));
            }

            rows.Add(string.Join(", ", row));
        }

        return "{" + string.Join("; ", rows) + "}";
    }

    private static string DatetimeText(DatetimeValue value)
    {
        static string One(DesignDate? date) => date is { } d
            ? "datetime([" + d.Year.ToString(CultureInfo.InvariantCulture) + " "
                + d.Month.ToString(CultureInfo.InvariantCulture) + " "
                + d.Day.ToString(CultureInfo.InvariantCulture) + "])"
            : "NaT";

        if (value.Items.Count == 1)
        {
            return One(value.Items[0]);
        }

        string spacer = value.Rows > value.Cols ? "; " : " ";
        return "[" + string.Join(spacer, value.Items.Select(One)) + "]";
    }

    private static string Number(double value, string format)
    {
        if (value == 0)
        {
            value = 0; // a negative zero is written as the zero it reads back as
        }

        return JgsSprintf.FormatMatlab(format, [JgsValue.Number(value)]);
    }

    // --- reading ---------------------------------------------------------------------------------

    /// <summary>
    /// Reads a right-hand side App Designer wrote. Text in no form <see cref="Format"/> makes is kept
    /// whole as an <see cref="ExpressionValue"/>.
    /// </summary>
    public static DesignValue Parse(string text, string objectName = AppObjectName)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new Reader(text.Trim(), objectName);
        try
        {
            DesignValue value = reader.Value();
            reader.SkipSpace();
            if (reader.AtEnd)
            {
                return value;
            }
        }
        catch (FormatException)
        {
            // falls through to the text itself
        }

        return new ExpressionValue(text.Trim());
    }

    private sealed class Reader(string text, string objectName)
    {
        private int _at;

        public bool AtEnd => _at >= text.Length;

        public void SkipSpace()
        {
            while (_at < text.Length && text[_at] == ' ')
            {
                _at++;
            }
        }

        private char Peek => _at < text.Length ? text[_at] : '\0';

        private bool Accept(string word)
        {
            if (string.CompareOrdinal(text, _at, word, 0, word.Length) == 0)
            {
                _at += word.Length;
                return true;
            }

            return false;
        }

        private void Expect(string word)
        {
            if (!Accept(word))
            {
                throw new FormatException();
            }
        }

        public DesignValue Value()
        {
            SkipSpace();
            switch (Peek)
            {
                case '\'':
                    return new CharValue(Quoted('\''));
                case '"':
                    return StringValue();
                case '[':
                    return Bracketed();
                case '{':
                    return Cell();
                default:
                    break;
            }

            if (Accept("createCallbackFcn(" + objectName + ", @"))
            {
                string method = Name();
                Expect(", true)");
                return new CallbackValue(method);
            }

            if (Accept("fullfile(" + PathToApp))
            {
                var parts = new List<string>();
                while (Accept(", "))
                {
                    parts.Add(Quoted('\''));
                }

                Expect(")");
                return parts.Count > 0 ? new ImageFileValue(parts) : throw new FormatException();
            }

            if (text.AsSpan(_at).StartsWith("datetime(") || text.AsSpan(_at).StartsWith("NaT"))
            {
                return new DatetimeValue(1, 1, [Date()]);
            }

            if (Accept("zeros("))
            {
                int rows = Count();
                Expect(",");
                int cols = Count();
                Expect(")");
                return new NumericValue(rows, cols, []);
            }

            if (Accept(objectName + "."))
            {
                return new ComponentsValue([Name()], bracketed: false);
            }

            (double number, bool logical) = Scalar();
            return logical ? NumericValue.Logical(number != 0) : NumericValue.Scalar(number);
        }

        private string Quoted(char quote)
        {
            if (Peek != quote)
            {
                throw new FormatException();
            }

            _at++;
            var s = new StringBuilder();
            while (true)
            {
                if (_at >= text.Length)
                {
                    throw new FormatException();
                }

                char ch = text[_at++];
                if (ch == quote)
                {
                    if (Peek == quote)
                    {
                        s.Append(quote);
                        _at++;
                        continue;
                    }

                    return s.ToString();
                }

                s.Append(ch);
            }
        }

        private StringValue StringValue()
        {
            var s = new StringBuilder(Quoted('"'));
            while (true)
            {
                int mark = _at;
                SkipSpace();
                if (Accept("+ newline +"))
                {
                    SkipSpace();
                    s.Append('\n').Append(Quoted('"'));
                    continue;
                }

                _at = mark;
                return new StringValue(s.ToString());
            }
        }

        private DesignValue Bracketed()
        {
            Expect("[");
            SkipSpace();
            if (Accept("]"))
            {
                return new NumericValue(0, 0, []);
            }

            if (Peek == '\'')
            {
                // ['a' newline 'b']: a char row with line breaks
                var s = new StringBuilder(Quoted('\''));
                while (!Accept("]"))
                {
                    Expect(" newline ");
                    s.Append('\n').Append(Quoted('\''));
                }

                return new CharValue(s.ToString());
            }

            if (Peek == 'd' || text.AsSpan(_at).StartsWith("NaT"))
            {
                var dates = new List<DesignDate?> { Date() };
                bool column = false;
                while (!Accept("]"))
                {
                    if (Accept("; "))
                    {
                        column = true;
                    }
                    else
                    {
                        Expect(" ");
                    }

                    dates.Add(Date());
                }

                return column ? new DatetimeValue(dates.Count, 1, dates) : new DatetimeValue(1, dates.Count, dates);
            }

            if (Accept(objectName + "."))
            {
                var names = new List<string> { Name() };
                while (!Accept("]"))
                {
                    Expect(", " + objectName + ".");
                    names.Add(Name());
                }

                return new ComponentsValue(names, bracketed: true);
            }

            // A numeric or logical matrix as mat2str writes it, or as a person might: a comma or
            // spaces between elements, a semicolon and any spaces between rows.
            var rows = new List<List<(double, bool)>> { new() };
            while (true)
            {
                rows[^1].Add(Scalar());
                int mark = _at;
                SkipSpace();
                if (Accept("]"))
                {
                    break;
                }

                if (Accept(";"))
                {
                    SkipSpace();
                    rows.Add([]);
                }
                else if (Accept(","))
                {
                    SkipSpace();
                }
                else if (_at == mark)
                {
                    throw new FormatException();
                }
            }

            int cols = rows[0].Count;
            if (rows.Any(r => r.Count != cols))
            {
                throw new FormatException();
            }

            bool logical = rows.All(static r => r.All(static e => e.Item2));
            if (!logical && rows.Any(static r => r.Any(static e => e.Item2)))
            {
                throw new FormatException(); // true beside a number is not what mat2str writes
            }

            var data = new double[rows.Count * cols];
            for (int r = 0; r < rows.Count; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    data[r + (c * rows.Count)] = rows[r][c].Item1;
                }
            }

            return new NumericValue(rows.Count, cols, data, logical);
        }

        private CellValue Cell()
        {
            Expect("{");
            if (Accept("}"))
            {
                return new CellValue(0, 0, []);
            }

            var items = new List<DesignValue> { CellItem() };
            char? separator = null;
            while (!Accept("}"))
            {
                char next = Peek;
                if (next is not (',' or ';') || (separator is { } s && s != next))
                {
                    throw new FormatException();
                }

                separator = next;
                _at++;
                Expect(" ");
                items.Add(CellItem());
            }

            return separator == ';' ? new CellValue(items.Count, 1, items) : new CellValue(1, items.Count, items);
        }

        private DesignValue CellItem()
        {
            // A number in a cell is written by %g; true and false are words.
            if (Peek is '\'' or '[' or '"' or '{')
            {
                return Value();
            }

            (double number, bool logical) = Scalar();
            return logical ? NumericValue.Logical(number != 0) : NumericValue.Scalar(number);
        }

        private DesignDate? Date()
        {
            if (Accept("NaT"))
            {
                return null;
            }

            Expect("datetime([");
            int year = Count();
            Expect(" ");
            int month = Count();
            Expect(" ");
            int day = Count();
            Expect("])");
            return new DesignDate(year, month, day);
        }

        private (double Value, bool Logical) Scalar()
        {
            if (Accept("true"))
            {
                return (1, true);
            }

            if (Accept("false"))
            {
                return (0, true);
            }

            int start = _at;
            if (Peek is '-' or '+')
            {
                _at++;
            }

            if (Accept("Inf"))
            {
                return (text[start] == '-' ? double.NegativeInfinity : double.PositiveInfinity, false);
            }

            if (Accept("NaN"))
            {
                return (double.NaN, false);
            }

            int digits = _at;
            while (_at < text.Length && (char.IsAsciiDigit(text[_at]) || text[_at] == '.'))
            {
                _at++;
            }

            if (_at == digits)
            {
                throw new FormatException();
            }

            if (Peek is 'e' or 'E')
            {
                _at++;
                if (Peek is '-' or '+')
                {
                    _at++;
                }

                int exponent = _at;
                while (_at < text.Length && char.IsAsciiDigit(text[_at]))
                {
                    _at++;
                }

                if (_at == exponent)
                {
                    throw new FormatException();
                }
            }

            return double.TryParse(text.AsSpan(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? (value, false)
                : throw new FormatException();
        }

        private int Count()
        {
            (double value, bool logical) = Scalar();
            return !logical && value >= 0 && value == Math.Floor(value) && value <= int.MaxValue
                ? (int)value
                : throw new FormatException();
        }

        private string Name()
        {
            int start = _at;
            while (_at < text.Length && (char.IsAsciiLetterOrDigit(text[_at]) || text[_at] == '_'))
            {
                _at++;
            }

            return _at > start && char.IsAsciiLetter(text[start]) ? text[start.._at] : throw new FormatException();
        }
    }

    // --- comparing -------------------------------------------------------------------------------

    /// <summary>
    /// Whether two values are equal as MATLAB's <c>isequal</c> says, with <c>NaN</c> and <c>NaT</c>
    /// equal to themselves when <paramref name="nanEqual"/> (<c>isequaln</c>). A char and a string of
    /// the same text are equal; an on/off value equals the char <c>'on'</c> or <c>'off'</c> and the
    /// logical of its state. A value kept as text equals nothing.
    /// </summary>
    public static bool IsEqual(DesignValue a, DesignValue b, bool nanEqual = false)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a is OnOffValue onA)
        {
            a = b is CharValue or StringValue ? new CharValue(onA.IsOn ? "on" : "off") : NumericValue.Logical(onA.IsOn);
        }

        if (b is OnOffValue onB)
        {
            b = a is CharValue or StringValue ? new CharValue(onB.IsOn ? "on" : "off") : NumericValue.Logical(onB.IsOn);
        }

        switch (a, b)
        {
            case (NumericValue x, NumericValue y):
                return x.Rows == y.Rows && x.Cols == y.Cols
                    && x.Data.Zip(y.Data).All(p => p.First == p.Second || (nanEqual && double.IsNaN(p.First) && double.IsNaN(p.Second)));
            case (CharValue or StringValue, CharValue or StringValue):
                return string.Equals(TextOf(a), TextOf(b), StringComparison.Ordinal);
            case (CellValue x, CellValue y):
                return x.Rows == y.Rows && x.Cols == y.Cols && x.Items.Zip(y.Items).All(p => IsEqual(p.First, p.Second, nanEqual));
            case (DatetimeValue x, DatetimeValue y):
                return x.Rows == y.Rows && x.Cols == y.Cols
                    && x.Items.Zip(y.Items).All(p => p.First is null ? nanEqual && p.Second is null : p.First == p.Second);
            case (CallbackValue x, CallbackValue y):
                return x.Method == y.Method;
            case (ComponentsValue x, ComponentsValue y):
                return x.CodeNames.SequenceEqual(y.CodeNames);
            case (ImageFileValue x, ImageFileValue y):
                return x.Parts.SequenceEqual(y.Parts);
            default:
                return false;
        }
    }

    private static string TextOf(DesignValue value) => value switch
    {
        CharValue c => c.Text,
        StringValue s => s.Text,
        _ => throw new InvalidOperationException(),
    };
}
