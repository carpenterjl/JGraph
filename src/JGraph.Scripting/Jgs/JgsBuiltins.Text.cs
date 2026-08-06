using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The text layer beyond the handful of string helpers JGS started with: searching and comparing,
/// regular expressions, formatted reading, character classification, and the byte-level views
/// (<c>typecast</c>, <c>unicode2native</c>).
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>How long a regular expression may run before it is treated as pathological.</summary>
    private static readonly TimeSpan RegexBudget = TimeSpan.FromSeconds(5);

    /// <summary>Registers the string and regular-expression builtins (M38).</summary>
    private static void RegisterTextBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        RegisterStringSearch(Define, dialect);
        RegisterStringShaping(Define);
        RegisterRegexBuiltins(Define, dialect);
        RegisterCharacterClasses(Define);
        RegisterByteViews(Define);
        RegisterScanning(Define);
    }

    // --- Searching and comparing ------------------------------------------------------------------

    private static void RegisterStringSearch(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>?> Define,
        JgsDialect dialect)
    {
        Define("strfind", (args, line, col) =>
        {
            Arity("strfind", args, 2, line, col);
            return Numbers(Occurrences(
                Str("strfind", args, 0, line, col), Str("strfind", args, 1, line, col), dialect.IndexBase));
        }, null);

        Define("findstr", (args, line, col) =>
        {
            Arity("findstr", args, 2, line, col);
            string first = Str("findstr", args, 0, line, col);
            string second = Str("findstr", args, 1, line, col);

            // The pre-R2011 spelling, which looks for whichever argument is shorter inside the other.
            return first.Length <= second.Length
                ? Numbers(Occurrences(second, first, dialect.IndexBase))
                : Numbers(Occurrences(first, second, dialect.IndexBase));
        }, null);

        void CompareFirst(string name, StringComparison comparison) =>
            Define(name, (args, line, col) =>
            {
                Arity(name, args, 3, line, col);
                string a = Str(name, args, 0, line, col);
                string b = Str(name, args, 1, line, col);
                int n = Count(name, args, 2, line, col);

                // MATLAB is false when either string is shorter than the compared prefix, rather than
                // comparing what is there — a length mismatch is a mismatch.
                return JgsValue.Bool(a.Length >= n && b.Length >= n
                    && string.Compare(a, 0, b, 0, n, comparison) == 0);
            }, null);

        CompareFirst("strncmp", StringComparison.Ordinal);
        CompareFirst("strncmpi", StringComparison.OrdinalIgnoreCase);

        Define("count", (args, line, col) =>
        {
            Arity("count", args, 2, line, col);
            string pattern = Str("count", args, 1, line, col);
            return PerString("count", args[0], text => JgsValue.Number(Occurrences(text, pattern, 0).Length), line, col);
        }, null);

        Define("matches", (args, line, col) =>
        {
            Arity("matches", args, 2, line, col);
            string pattern = Str("matches", args, 1, line, col);
            return PerString("matches", args[0],
                text => JgsValue.Bool(string.Equals(text, pattern, StringComparison.Ordinal)), line, col);
        }, null);

        Define("strlength", (args, line, col) =>
        {
            Arity("strlength", args, 1, line, col);
            return PerString("strlength", args[0], text => JgsValue.Number(text.Length), line, col);
        }, null);
    }

    /// <summary>Every start position of <paramref name="pattern"/> in <paramref name="text"/>, overlapping.</summary>
    private static double[] Occurrences(string text, string pattern, int origin)
    {
        if (pattern.Length == 0)
        {
            return [];
        }

        var found = new List<double>();
        for (int at = text.IndexOf(pattern, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(pattern, at + 1, StringComparison.Ordinal))
        {
            // Overlapping matches count: strfind('aaa', 'aa') is two positions, not one.
            found.Add(at + origin);
        }

        return found.ToArray();
    }

    /// <summary>Applies a per-string function to a string, or to every element of a cell of strings.</summary>
    private static JgsValue PerString(string name, JgsValue value, Func<string, JgsValue> f, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            return f(value.AsString);
        }

        if (value.Type != JgsType.Cell)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a string or a cell of strings, but got a {value.TypeName}.");
        }

        JgsValue[] cells = value.AsCell;
        var results = new JgsValue[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i].Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, col, $"{name}: cell element {i + 1} is a {cells[i].TypeName}, not a string.");
            }

            results[i] = f(cells[i].AsString);
        }

        return JgsValue.Array(results);
    }

    // --- Shaping ----------------------------------------------------------------------------------

    private static void RegisterStringShaping(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>?> Define)
    {
        Define("deblank", (args, line, col) =>
        {
            Arity("deblank", args, 1, line, col);
            return PerString("deblank", args[0], static text => JgsValue.Str(text.TrimEnd()), line, col);
        }, null);

        Define("blanks", (args, line, col) =>
        {
            Arity("blanks", args, 1, line, col);
            return JgsValue.Str(new string(' ', Math.Max(0, Count("blanks", args, 0, line, col))));
        }, null);

        Define("strcat", (args, line, col) =>
        {
            var joined = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                // strcat drops trailing whitespace from each piece — a quirk of its char-matrix
                // origins that scripts nonetheless rely on. [a b] is the concatenation that keeps it.
                joined.Append(Str("strcat", args, i, line, col).TrimEnd());
            }

            return JgsValue.Str(joined.ToString());
        }, null);

        Define("setstr", (args, line, col) =>
        {
            Arity("setstr", args, 1, line, col);
            return CharactersOf("setstr", args[0], line, col);
        }, null);

        // With no string-array type there is nothing for these to convert: text is char either way.
        // They exist so a script written for R2016b onward runs unchanged.
        foreach (string name in new[] { "convertCharsToStrings", "convertStringsToChars", "convertContainedStringsToChars" })
        {
            string captured = name;
            Define(captured, (args, line, col) =>
            {
                Arity(captured, args, 1, line, col);
                return args[0];
            }, null);
        }
    }

    /// <summary>Character codes back to text — the body of <c>char</c> and its legacy name.</summary>
    private static JgsValue CharactersOf(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            return value;
        }

        double[] codes = value.Type is JgsType.Number or JgsType.Bool
            ? [value.AsNumber]
            : ToDoubles(name, value, line, col);
        var text = new StringBuilder(codes.Length);
        foreach (double code in codes)
        {
            text.Append((char)(int)code);
        }

        return JgsValue.Str(text.ToString());
    }

    // --- Regular expressions ----------------------------------------------------------------------

    private static void RegisterRegexBuiltins(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>?> Define,
        JgsDialect dialect)
    {
        void Search(string name, RegexOptions options)
        {
            Define(name,
                (args, line, col) => RegexOutputs(name, args, options, dialect, wanted: 1, line, col)[0],
                (args, wanted, line, col) => RegexOutputs(name, args, options, dialect, wanted, line, col));
        }

        Search("regexp", RegexOptions.None);
        Search("regexpi", RegexOptions.IgnoreCase);

        Define("regexprep", (args, line, col) => ReplaceMatches(args, line, col), null);

        Define("regexptranslate", (args, line, col) =>
        {
            Arity("regexptranslate", args, 2, line, col);
            string mode = Str("regexptranslate", args, 0, line, col);
            string text = Str("regexptranslate", args, 1, line, col);
            return JgsValue.Str(mode switch
            {
                "escape" => Regex.Escape(text),

                // A wildcard pattern is a file glob: * and ? mean what they mean in a shell, and
                // everything else is literal.
                "wildcard" => Regex.Escape(text).Replace("\\*", ".*").Replace("\\?", ".").Replace("\\.", "\\."),
                "flexible" => text,
                _ => throw new JgsRuntimeException(line, col,
                    $"regexptranslate: '{mode}' is not 'escape', 'wildcard', or 'flexible'."),
            });
        }, null);
    }

    /// <summary>
    /// Runs a regular expression and produces the outputs MATLAB's <c>regexp</c> would. Option words
    /// name the outputs and fix their order; with none given the order is MATLAB's default
    /// (start, end, tokenExtents, match, tokens, names, split).
    /// </summary>
    private static JgsValue[] RegexOutputs(
        string name, IReadOnlyList<JgsValue> args, RegexOptions options, JgsDialect dialect,
        int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects at least 2 argument(s), but got {args.Count}.");
        }

        string text = Str(name, args, 0, line, col);
        string pattern = Str(name, args, 1, line, col);

        var requested = new List<string>();
        RegexMode mode = ReadRegexWords(name, args, 2, options, requested, line, col);
        if (requested.Count == 0)
        {
            requested.AddRange(RegexOutputWords);
        }

        // MATLAB ignores a zero-length match unless 'emptymatch' asks for it, which is the opposite
        // of what .NET does with the same expression.
        var matches = new List<Match>();
        foreach (Match match in Compile(name, pattern, mode.Options, line, col).Matches(text))
        {
            if (match.Length > 0 || mode.EmptyMatch)
            {
                matches.Add(match);
            }
        }

        int produced = Math.Min(Math.Max(wanted, 1), requested.Count);
        var outputs = new JgsValue[produced];
        for (int i = 0; i < produced; i++)
        {
            outputs[i] = RegexOutput(requested[i], matches, text, dialect.IndexBase, mode.Once);
        }

        return outputs;
    }

    /// <summary>Builds one named <c>regexp</c> output from the match list.</summary>
    private static JgsValue RegexOutput(string kind, IReadOnlyList<Match> matches, string text, int origin, bool once)
    {
        switch (kind)
        {
            case "start":
                return Once(once, matches.Select(m => JgsValue.Number(m.Index + origin)).ToArray(), JgsValue.Array([]));

            case "end":
                // The end index is the last character of the match, not one past it.
                return Once(once, matches.Select(m => JgsValue.Number(m.Index + m.Length - 1 + origin)).ToArray(), JgsValue.Array([]));

            case "match":
                return Once(once, matches.Select(m => JgsValue.Str(m.Value)).ToArray(), JgsValue.Str(string.Empty), asCell: true);

            case "tokens":
                return Once(once, matches.Select(m => TokensOf(m)).ToArray(), JgsValue.Cell([]), asCell: true);

            case "tokenExtents":
                return Once(once, matches.Select(m => TokenExtentsOf(m, origin)).ToArray(), JgsValue.Array([]), asCell: true);

            case "names":
                return matches.Count == 0 ? JgsValue.EmptyStruct() : NamesOf(matches[0]);

            default:
                return SplitOn(matches, text);
        }
    }

    /// <summary>Wraps a per-match list as MATLAB does: a cell (or array), or the first item for 'once'.</summary>
    private static JgsValue Once(bool once, JgsValue[] items, JgsValue empty, bool asCell = false)
    {
        if (once)
        {
            return items.Length == 0 ? empty : items[0];
        }

        return asCell ? JgsValue.Cell(items) : JgsValue.Array(items);
    }

    private static JgsValue TokensOf(Match match)
    {
        var tokens = new List<JgsValue>();
        for (int g = 1; g < match.Groups.Count; g++)
        {
            tokens.Add(JgsValue.Str(match.Groups[g].Value));
        }

        return JgsValue.Cell(tokens.ToArray());
    }

    private static JgsValue TokenExtentsOf(Match match, int origin)
    {
        var rows = new List<JgsValue>();
        for (int g = 1; g < match.Groups.Count; g++)
        {
            Group group = match.Groups[g];
            rows.Add(JgsValue.Array([
                JgsValue.Number(group.Index + origin),
                JgsValue.Number(group.Index + group.Length - 1 + origin),
            ]));
        }

        return JgsValue.Array(rows.ToArray());
    }

    private static JgsValue NamesOf(Match match)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach (Group group in match.Groups)
        {
            // .NET numbers unnamed groups, so a purely numeric name is a positional group, not a
            // named one, and MATLAB's 'names' output only carries the named ones.
            if (!int.TryParse(group.Name, out _))
            {
                fields[group.Name] = JgsValue.Str(group.Value);
            }
        }

        return JgsValue.Struct(fields);
    }

    private static JgsValue SplitOn(IReadOnlyList<Match> matches, string text)
    {
        var pieces = new List<JgsValue>();
        int at = 0;
        foreach (Match match in matches)
        {
            pieces.Add(JgsValue.Str(text[at..match.Index]));
            at = match.Index + match.Length;
        }

        pieces.Add(JgsValue.Str(text[at..]));
        return JgsValue.Cell(pieces.ToArray());
    }

    /// <summary>Compiles a pattern, turning a bad one into a script diagnostic rather than a crash.</summary>
    private static Regex Compile(string name, string pattern, RegexOptions options, int line, int col)
    {
        try
        {
            return new Regex(pattern, options, RegexBudget);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: '{pattern}' is not a valid regular expression — {ex.Message}");
        }
    }

    // --- Character classification -----------------------------------------------------------------

    private static void RegisterCharacterClasses(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>?> Define)
    {
        Define("isstrprop", (args, line, col) =>
        {
            Arity("isstrprop", args, 2, line, col);
            string category = Str("isstrprop", args, 1, line, col);
            Func<char, bool> test = category switch
            {
                "alpha" => char.IsLetter,
                "alphanum" => char.IsLetterOrDigit,
                "digit" => char.IsDigit,
                "lower" => char.IsLower,
                "upper" => char.IsUpper,
                "punct" => char.IsPunctuation,
                "wspace" => char.IsWhiteSpace,
                "xdigit" => char.IsAsciiHexDigit,
                "cntrl" => char.IsControl,
                "graphic" => static c => !char.IsWhiteSpace(c) && !char.IsControl(c),
                "print" => static c => !char.IsControl(c),
                _ => throw new JgsRuntimeException(line, col, $"isstrprop does not know the category '{category}'."),
            };

            string text = args[0].Type == JgsType.String
                ? args[0].AsString
                : CharactersOf("isstrprop", args[0], line, col).AsString;
            var mask = new JgsValue[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                mask[i] = JgsValue.Bool(test(text[i]));
            }

            return JgsValue.Array(mask);
        }, null);
    }

    // --- Bytes ------------------------------------------------------------------------------------

    private static void RegisterByteViews(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>?> Define)
    {
        Define("unicode2native", (args, line, col) =>
        {
            ArityRange("unicode2native", args, 1, 2, line, col);
            Encoding encoding = EncodingNamed("unicode2native", args, 1, line, col);
            byte[] bytes = encoding.GetBytes(Str("unicode2native", args, 0, line, col));
            var values = new double[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                values[i] = bytes[i];
            }

            return Numbers(values);
        }, null);

        Define("native2unicode", (args, line, col) =>
        {
            ArityRange("native2unicode", args, 1, 2, line, col);
            Encoding encoding = EncodingNamed("native2unicode", args, 1, line, col);
            double[] values = ToDoubles("native2unicode", args[0], line, col);
            var bytes = new byte[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is < 0 or > 255 || values[i] != Math.Floor(values[i]))
                {
                    throw new JgsRuntimeException(line, col, "native2unicode expects whole numbers from 0 to 255.");
                }

                bytes[i] = (byte)values[i];
            }

            return JgsValue.Str(encoding.GetString(bytes));
        }, null);

        Define("typecast", (args, line, col) =>
        {
            Arity("typecast", args, 2, line, col);
            string target = Str("typecast", args, 1, line, col);

            // Every JGraph number is a double, so the source bytes are always a double's eight.
            double[] source = args[0].Type is JgsType.Number or JgsType.Bool
                ? [args[0].AsNumber]
                : ToDoubles("typecast", args[0], line, col);
            var bytes = new byte[source.Length * sizeof(double)];
            for (int i = 0; i < source.Length; i++)
            {
                BitConverter.GetBytes(source[i]).CopyTo(bytes, i * sizeof(double));
            }

            return Numbers(Reinterpret("typecast", bytes, target, line, col));
        }, null);
    }

    /// <summary>Reads bytes back as the values of a named numeric class.</summary>
    private static double[] Reinterpret(string name, byte[] bytes, string target, int line, int col)
    {
        int width = target switch
        {
            "int8" or "uint8" => 1,
            "int16" or "uint16" => 2,
            "int32" or "uint32" or "single" => 4,
            "int64" or "uint64" or "double" => 8,
            _ => throw new JgsRuntimeException(line, col, $"{name}: '{target}' is not a numeric class."),
        };

        if (bytes.Length % width != 0)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: {bytes.Length} bytes do not divide evenly into {target} values.");
        }

        var values = new double[bytes.Length / width];
        for (int i = 0; i < values.Length; i++)
        {
            int at = i * width;
            values[i] = target switch
            {
                "int8" => (sbyte)bytes[at],
                "uint8" => bytes[at],
                "int16" => BitConverter.ToInt16(bytes, at),
                "uint16" => BitConverter.ToUInt16(bytes, at),
                "int32" => BitConverter.ToInt32(bytes, at),
                "uint32" => BitConverter.ToUInt32(bytes, at),
                "int64" => BitConverter.ToInt64(bytes, at),
                "uint64" => BitConverter.ToUInt64(bytes, at),
                "single" => BitConverter.ToSingle(bytes, at),
                _ => BitConverter.ToDouble(bytes, at),
            };
        }

        return values;
    }

    private static Encoding EncodingNamed(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        if (index >= args.Count)
        {
            return Encoding.UTF8;
        }

        string encoding = Str(name, args, index, line, col);
        try
        {
            return Encoding.GetEncoding(encoding);
        }
        catch (ArgumentException)
        {
            throw new JgsRuntimeException(line, col, $"{name}: '{encoding}' is not an encoding this system knows.");
        }
    }

    // --- Formatted reading ------------------------------------------------------------------------

    private static void RegisterScanning(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>?> Define)
    {
        Define("sscanf", (args, line, col) =>
        {
            ArityRange("sscanf", args, 2, 3, line, col);
            string text = Str("sscanf", args, 0, line, col);
            string format = Str("sscanf", args, 1, line, col);
            int limit = args.Count == 3 ? Count("sscanf", args, 2, line, col) : int.MaxValue;
            return Scan(text, format, limit, line, col);
        }, null);
    }

    /// <summary>
    /// Reads values out of <paramref name="text"/> under a scanf format, cycling the format until the
    /// text runs out — which is what makes <c>sscanf(s, '%f')</c> read every number in the string.
    /// </summary>
    private static JgsValue Scan(string text, string format, int limit, int line, int col)
    {
        var values = new List<double>();
        var characters = new StringBuilder();
        bool textual = false;
        int at = 0;

        while (at < text.Length && values.Count + characters.Length < limit)
        {
            int before = at;
            for (int f = 0; f < format.Length && at < text.Length; f++)
            {
                if (format[f] != '%')
                {
                    // Literal text in the format: whitespace matches any run of it, anything else
                    // has to be there.
                    if (char.IsWhiteSpace(format[f]))
                    {
                        while (at < text.Length && char.IsWhiteSpace(text[at]))
                        {
                            at++;
                        }
                    }
                    else if (text[at] == format[f])
                    {
                        at++;
                    }

                    continue;
                }

                // Skip the width and flags: JGraph reads the whole token either way.
                f++;
                while (f < format.Length && (char.IsAsciiDigit(format[f]) || format[f] is '.' or '*' or 'l' or 'h'))
                {
                    f++;
                }

                if (f >= format.Length)
                {
                    break;
                }

                while (at < text.Length && char.IsWhiteSpace(text[at]) && format[f] != 'c')
                {
                    at++;
                }

                if (at >= text.Length)
                {
                    break;
                }

                switch (format[f])
                {
                    case 'd' or 'i' or 'u' or 'f' or 'g' or 'e':
                        if (!ReadNumber(text, ref at, out double number))
                        {
                            at = text.Length;
                            break;
                        }

                        values.Add(number);
                        break;

                    case 'x':
                        int start = at;
                        while (at < text.Length && char.IsAsciiHexDigit(text[at]))
                        {
                            at++;
                        }

                        values.Add(at > start ? Convert.ToInt64(text[start..at], 16) : 0);
                        break;

                    case 's':
                        textual = true;
                        while (at < text.Length && !char.IsWhiteSpace(text[at]))
                        {
                            characters.Append(text[at++]);
                        }

                        break;

                    case 'c':
                        textual = true;
                        characters.Append(text[at++]);
                        break;

                    case '%':
                        if (text[at] == '%')
                        {
                            at++;
                        }

                        break;

                    default:
                        throw new JgsRuntimeException(line, col, $"sscanf does not support the conversion '%{format[f]}'.");
                }
            }

            if (at == before)
            {
                break; // the format consumed nothing, so cycling it again never would either
            }
        }

        return textual ? JgsValue.Str(characters.ToString()) : Numbers(values.ToArray());
    }

    /// <summary>Reads one number from <paramref name="text"/>, advancing past it.</summary>
    private static bool ReadNumber(string text, ref int at, out double value)
    {
        int start = at;
        if (at < text.Length && (text[at] == '+' || text[at] == '-'))
        {
            at++;
        }

        while (at < text.Length && (char.IsAsciiDigit(text[at]) || text[at] == '.'))
        {
            at++;
        }

        if (at < text.Length && (text[at] is 'e' or 'E'))
        {
            int exponent = at + 1;
            if (exponent < text.Length && (text[exponent] == '+' || text[exponent] == '-'))
            {
                exponent++;
            }

            if (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
            {
                at = exponent;
                while (at < text.Length && char.IsAsciiDigit(text[at]))
                {
                    at++;
                }
            }
        }

        return double.TryParse(text[start..at], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
