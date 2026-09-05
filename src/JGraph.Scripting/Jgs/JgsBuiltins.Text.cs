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
    /// <summary>Registers the string and regular-expression builtins (M38).</summary>
    private static void RegisterTextBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        RegisterStringSearch(Define, dialect);
        RegisterStringShaping(Define);
        RegisterRegexBuiltins(env, dialect);
        RegisterCharacterClasses(Define);
        RegisterByteViews(Define);
        RegisterScanning(Define, dialect);
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
            return Found(Occurrences(
                Str("strfind", args, 0, line, col), Str("strfind", args, 1, line, col), dialect.IndexBase));
        }, null);

        Define("findstr", (args, line, col) =>
        {
            Arity("findstr", args, 2, line, col);
            string first = Str("findstr", args, 0, line, col);
            string second = Str("findstr", args, 1, line, col);

            // The pre-R2011 spelling, which looks for whichever argument is shorter inside the other.
            return first.Length <= second.Length
                ? Found(Occurrences(second, first, dialect.IndexBase))
                : Found(Occurrences(first, second, dialect.IndexBase));
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

            // Occurrences are counted without overlap — count('aaa', 'aa') is 1 where strfind
            // finds two — and several patterns are counted in one pass over the text rather than
            // added up, so that count('abab', {'ab', 'ba'}) is 2 and not 3. Both measured.
            if (IsOnePattern(args[1], out string onePattern))
            {
                return PerString("count", args[0],
                    text => JgsValue.Number(CountAtOnce(text, [onePattern])), line, col);
            }

            string[] wanted = PatternsOf("count", args, 1, line, col);
            return PerString("count", args[0], text => JgsValue.Number(CountAtOnce(text, wanted)), line, col);
        }, null);

        Define("matches", (args, line, col) =>
        {
            Arity("matches", args, 2, line, col);
            if (IsOnePattern(args[1], out string oneWhole))
            {
                return PerString("matches", args[0],
                    text => JgsValue.Bool(string.Equals(text, oneWhole, StringComparison.Ordinal)),
                    line, col);
            }

            string[] whole = PatternsOf("matches", args, 1, line, col);
            return PerString("matches", args[0],
                text => JgsValue.Bool(Array.Exists(whole, p => string.Equals(text, p, StringComparison.Ordinal))),
                line, col);
        }, null);

        Define("strlength", (args, line, col) =>
        {
            Arity("strlength", args, 1, line, col);
            return PerString("strlength", args[0], text => JgsValue.Number(text.Length), line, col);
        }, null);
    }

    /// <summary>Positions as a row, or the 0-by-0 empty MATLAB answers when there are none.</summary>
    private static JgsValue Found(double[] positions) => positions.Length == 0 ? JgsEmpty.Zero() : Numbers(positions);

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

    /// <summary>
    /// How many times any of <paramref name="patterns"/> occurs in <paramref name="text"/>, in one
    /// left-to-right pass without overlap. An empty pattern occurs at every position, the end included.
    /// </summary>
    private static int CountAtOnce(string text, string[] patterns)
    {
        int count = 0;
        int at = 0;
        while (at <= text.Length)
        {
            int matched = -1;
            for (int p = 0; p < patterns.Length; p++)
            {
                int length = patterns[p].Length;
                if (length == 0 || (at + length <= text.Length && string.CompareOrdinal(text, at, patterns[p], 0, length) == 0))
                {
                    matched = p;
                    break;
                }
            }

            if (matched < 0)
            {
                at++;
                continue;
            }

            count++;
            at += Math.Max(1, patterns[matched].Length);
        }

        return count;
    }

    /// <summary>Applies a per-string function to a string, or to every element of a cell of strings.</summary>
    private static JgsValue PerString(string name, JgsValue value, Func<string, JgsValue> f, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            return f(value.AsString);
        }

        // A string array maps the same way a cell of char does, and keeps its shape (M63) — which is
        // what makes strlength(["a" "bb"]) answer [1 2] rather than one number for the whole thing.
        if (value.IsStringArray)
        {
            JgsValue[] texts = value.BoxedElements();
            var mapped = new JgsValue[texts.Length];
            for (int i = 0; i < texts.Length; i++)
            {
                mapped[i] = f(texts[i].AsString);
            }

            JgsValue answer = JgsValue.Array(mapped);
            answer.TakeShapeOf(value);
            return answer;
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
            if (JgsNumericClasses.Parse(target) is not JgsNumericClass targetClass)
            {
                throw new JgsRuntimeException(line, col, $"typecast: '{target}' is not a numeric class.");
            }

            // The bytes are the bytes of the class the value is *wearing*, not of the double it is
            // stored in. That distinction is the whole of this function: typecast exists to read one
            // number's bits as another class, so reading a single's four bytes as a double's eight
            // answers a question nobody asked. Storage has been doubles since the beginning and the
            // class has been a tag on the wrapper since M97; only this verb never read the tag.
            JgsNumericClass sourceClass = args[0].NumericClass;
            double[] source = args[0].Type is JgsType.Number or JgsType.Bool
                ? [args[0].AsNumber]
                : ToDoubles("typecast", args[0], line, col);

            int stride = WidthOf(sourceClass);
            var bytes = new byte[source.Length * stride];
            for (int i = 0; i < source.Length; i++)
            {
                WriteBytes(source[i], sourceClass, bytes.AsSpan(i * stride, stride));
            }

            JgsValue read = Numbers(Reinterpret("typecast", bytes, target, line, col));

            // A column in is a column out, which is MATLAB's rule and the one thing a flat rewrite
            // of the samples would lose.
            if (args[0].Type == JgsType.Array && args[0].Dims is [_, 1])
            {
                read.ReshapeDims([read.ArrayLength, 1]);
            }

            return JgsNumericClasses.Stamp(read, targetClass);
        }, null);
    }

    /// <summary>How many bytes one element of a numeric class occupies.</summary>
    private static int WidthOf(JgsNumericClass numericClass) => numericClass switch
    {
        JgsNumericClass.Int8 or JgsNumericClass.UInt8 => 1,
        JgsNumericClass.Int16 or JgsNumericClass.UInt16 => 2,
        JgsNumericClass.Int32 or JgsNumericClass.UInt32 or JgsNumericClass.Single => 4,
        _ => 8,
    };

    /// <summary>
    /// One sample laid down as the bytes its class stores it in.
    /// </summary>
    /// <remarks>
    /// The narrowing casts are safe rather than hopeful: a tagged array has already been through
    /// <see cref="JgsNumericClasses.Convert"/>, so its samples are whole and inside the class's range
    /// before they reach here. The one exception is a 64-bit integer past 2^53, which a double cannot
    /// hold in the first place and which no cast here could recover.
    /// </remarks>
    private static void WriteBytes(double value, JgsNumericClass numericClass, Span<byte> destination)
    {
        switch (numericClass)
        {
            case JgsNumericClass.Single:
                BitConverter.TryWriteBytes(destination, (float)value);
                break;
            case JgsNumericClass.Int8:
                destination[0] = unchecked((byte)(sbyte)value);
                break;
            case JgsNumericClass.UInt8:
                destination[0] = (byte)value;
                break;
            case JgsNumericClass.Int16:
                BitConverter.TryWriteBytes(destination, (short)value);
                break;
            case JgsNumericClass.UInt16:
                BitConverter.TryWriteBytes(destination, (ushort)value);
                break;
            case JgsNumericClass.Int32:
                BitConverter.TryWriteBytes(destination, (int)value);
                break;
            case JgsNumericClass.UInt32:
                BitConverter.TryWriteBytes(destination, (uint)value);
                break;
            case JgsNumericClass.Int64:
                BitConverter.TryWriteBytes(destination, (long)value);
                break;
            case JgsNumericClass.UInt64:
                BitConverter.TryWriteBytes(destination, (ulong)value);
                break;
            default:
                BitConverter.TryWriteBytes(destination, value);
                break;
        }
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
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>?> Define, JgsDialect dialect)
    {
        Define("sscanf", (args, line, col) => ScanText(args, 1, dialect, line, col)[0],
            (args, wanted, line, col) => ScanText(args, wanted, dialect, line, col));
    }

    /// <summary>
    /// <c>[A, count, errmsg, nextindex] = sscanf(text, format, size)</c>: the text read under a scanf
    /// format, bounded by a count or an <c>[m n]</c> shape.
    /// </summary>
    private static JgsValue[] ScanText(IReadOnlyList<JgsValue> args, int wanted, JgsDialect dialect,
        int line, int col)
    {
        ArityRange("sscanf", args, 2, 3, line, col);
        string text = Str("sscanf", args, 0, line, col);
        string format = Str("sscanf", args, 1, line, col);
        if (dialect.IsMatlab)
        {
            // MATLAB's quotes keep '\n' as two characters and leave the decoding to the format reader.
            format = UnescapeFormat(format);
        }

        (int rows, int limit) = args.Count == 3
            ? ScanSize("sscanf", args, 2, line, col)
            : (-1, int.MaxValue);
        ScanResult result = Scan(text, format, limit, line, col, "sscanf");
        return ScanOutputs(result, rows, wanted);
    }

    /// <summary>
    /// The size argument of a scan: a count, or <c>[m n]</c> as a row count and an element limit.
    /// Either may be Inf, which is "as many as there are".
    /// </summary>
    private static (int Rows, int Limit) ScanSize(string name, IReadOnlyList<JgsValue> args, int at,
        int line, int col)
    {
        double[] size = SizeArgument(name, args, at, line, col);
        if (size.Length == 1)
        {
            return (-1, double.IsInfinity(size[0]) ? int.MaxValue : (int)size[0]);
        }

        int rows = (int)size[0];
        return (rows, double.IsInfinity(size[1]) ? int.MaxValue : rows * (int)size[1]);
    }

    /// <summary>The four outputs of a scan, as many of them as were asked for.</summary>
    private static JgsValue[] ScanOutputs(ScanResult result, int rows, int wanted)
    {
        JgsValue answer = ShapeScanned(result, rows);
        if (wanted <= 1)
        {
            return [answer];
        }

        JgsValue[] outputs =
        [
            answer,
            JgsValue.Number(result.Count),
            JgsValue.Str(result.Error),
            JgsValue.Number(result.Consumed + 1),
        ];
        return outputs[..System.Math.Min(wanted, outputs.Length)];
    }

    /// <summary>
    /// A scan's answer in the shape MATLAB gives it: text when every stored conversion was a text one,
    /// otherwise a numeric column — or, when <paramref name="rows"/> was asked for, that many rows
    /// filled down each column and padded with zeros to fill the last.
    /// </summary>
    private static JgsValue ShapeScanned(ScanResult result, int rows)
    {
        if (result.Textual)
        {
            return JgsValue.Str(result.Text);
        }

        double[] values = result.Elements.ToArray();
        int columns;
        if (rows > 0)
        {
            columns = (values.Length + rows - 1) / rows;
            if (values.Length != rows * columns)
            {
                Array.Resize(ref values, rows * columns);
            }
        }
        else
        {
            rows = values.Length;
            columns = values.Length == 0 ? 0 : 1;
        }

        JgsValue answer = Numbers(values);
        answer.Reshape(rows, columns);
        return answer;
    }

    /// <summary>One conversion in a scanf format.</summary>
    /// <param name="Kind">The conversion letter, with <c>[</c> standing for a character set.</param>
    /// <param name="Width">The most characters the conversion may read, or 0 for no bound.</param>
    /// <param name="Suppressed">Whether <c>%*</c> asked for the value to be read but not stored.</param>
    /// <param name="Set">For <c>%[...]</c>, the characters listed (ranges expanded).</param>
    /// <param name="Negated">For <c>%[^...]</c>, whether the set is the characters <em>not</em> listed.</param>
    private readonly record struct ScanConversion(char Kind, int Width, bool Suppressed, string Set, bool Negated)
    {
        public bool IsText => Kind is 's' or 'c' or '[';

        /// <summary>Whether the conversion skips leading whitespace, which every one but %c and %[ does.</summary>
        public bool SkipsWhitespace => Kind is not ('c' or '[');
    }

    /// <summary>One piece of a compiled scanf format: a conversion, or literal text to be matched.</summary>
    private readonly record struct ScanPiece(ScanConversion? Conversion, string Literal);

    /// <summary>What one scan produced.</summary>
    /// <param name="Elements">Every stored value in reading order, characters as their codes.</param>
    /// <param name="Text">The stored characters, for a format whose stored conversions are all text.</param>
    /// <param name="Textual">Whether the answer is text rather than numbers.</param>
    /// <param name="Count">How many conversions stored a value: a <c>%s</c> field counts once however long.</param>
    /// <param name="Consumed">How many characters of the input the scan used.</param>
    /// <param name="Error">MATLAB's <c>errmsg</c>: empty, or why the scan stopped early.</param>
    private readonly record struct ScanResult(List<double> Elements, string Text, bool Textual, int Count, int Consumed, string Error);

    /// <summary>What one attempt to match a piece of the format came to.</summary>
    private enum ScanStep
    {
        /// <summary>The piece matched (and stored its value, if it had one).</summary>
        Matched,

        /// <summary>The input ended before the piece could be read: the scan is over, without complaint.</summary>
        EndOfText,

        /// <summary>The input did not look like the piece: the scan is over, and that is an error.</summary>
        Mismatch,
    }

    /// <summary>
    /// Reads values out of <paramref name="text"/> under a scanf format, cycling the format until the
    /// text runs out, it stops matching, or <paramref name="limit"/> elements have been stored — which
    /// is what makes <c>sscanf(s, '%f')</c> read every number in the string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This follows C's scanf as MATLAB does: <c>%*</c> reads a field without storing it, a width
    /// bounds how many characters a conversion may take, <c>%d</c> stops at the first character that is
    /// not a digit, and a literal that is not there ends the scan rather than being skipped over.
    /// </para>
    /// <para>
    /// The answer reports how much of the text it used and how many values it produced. Those two
    /// numbers are what let a file reader put its stream back where the scan stopped instead of at
    /// the end. Before M76 every file scan read the whole remainder of the file whatever the format
    /// matched, so a bounded <c>fscanf</c> left the position wrong and a following <c>fgetl</c> read
    /// nothing.
    /// </para>
    /// </remarks>
    private static ScanResult Scan(string text, string format, int limit, int line, int col, string name)
    {
        List<ScanPiece> pieces = CompileScanFormat(format, name, line, col);

        // The answer is text only when something is stored and everything stored is text; a format
        // that mixes the two answers the character codes beside the numbers, as MATLAB does.
        bool stored = false;
        bool numeric = false;
        foreach (ScanPiece piece in pieces)
        {
            if (piece.Conversion is { Suppressed: false } conversion)
            {
                stored = true;
                numeric |= !conversion.IsText;
            }
        }

        var elements = new List<double>();
        var characters = new StringBuilder();
        int at = 0;
        int count = 0;
        ScanStep step = ScanStep.Matched;

        while (step == ScanStep.Matched && at < text.Length && count < limit)
        {
            int before = at;
            foreach (ScanPiece piece in pieces)
            {
                step = piece.Conversion is { } conversion
                    ? ReadConversion(text, ref at, conversion, ref count, elements, characters)
                    : MatchLiteral(text, ref at, piece.Literal);
                if (step != ScanStep.Matched || count >= limit)
                {
                    break;
                }
            }

            if (at == before)
            {
                break; // the format consumed nothing, so cycling it again never would either
            }
        }

        return new ScanResult(elements, characters.ToString(), stored && !numeric, count, at,
            step == ScanStep.Mismatch ? "Matching failure in format." : string.Empty);
    }

    /// <summary>Splits a scanf format into its conversions and the literal text between them.</summary>
    private static List<ScanPiece> CompileScanFormat(string format, string name, int line, int col)
    {
        var pieces = new List<ScanPiece>();
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                pieces.Add(new ScanPiece(null, literal.ToString()));
                literal.Clear();
            }
        }

        int f = 0;
        while (f < format.Length)
        {
            if (format[f] != '%')
            {
                literal.Append(format[f++]);
                continue;
            }

            f++;
            if (f < format.Length && format[f] == '%')
            {
                literal.Append('%');
                f++;
                continue;
            }

            FlushLiteral();

            bool suppressed = false;
            if (f < format.Length && format[f] == '*')
            {
                suppressed = true;
                f++;
            }

            int width = 0;
            while (f < format.Length && char.IsAsciiDigit(format[f]))
            {
                width = (width * 10) + (format[f++] - '0');
            }

            // A precision means nothing to a reader; MATLAB ignores one rather than objecting.
            if (f < format.Length && format[f] == '.')
            {
                f++;
                while (f < format.Length && char.IsAsciiDigit(format[f]))
                {
                    f++;
                }
            }

            // C's length modifiers: every value is a double here, so they change nothing.
            while (f < format.Length && format[f] is 'l' or 'h' or 'L')
            {
                f++;
            }

            if (f >= format.Length)
            {
                throw new JgsRuntimeException(line, col, $"{name}: the format ends inside a conversion.");
            }

            char kind = format[f++];
            string set = string.Empty;
            bool negated = false;
            switch (kind)
            {
                case '[':
                    if (f < format.Length && format[f] == '^')
                    {
                        negated = true;
                        f++;
                    }

                    int start = f;
                    if (f < format.Length && format[f] == ']')
                    {
                        f++; // a ']' first in the set is a member of it, not its end
                    }

                    while (f < format.Length && format[f] != ']')
                    {
                        f++;
                    }

                    if (f >= format.Length)
                    {
                        throw new JgsRuntimeException(line, col, $"{name}: a '%[' conversion has no closing ']'.");
                    }

                    set = ExpandScanSet(format[start..f]);
                    f++;
                    break;

                case 'd' or 'i' or 'u' or 'o' or 'x' or 'X' or 'f' or 'e' or 'E' or 'g' or 'G' or 's' or 'c':
                    break;

                default:
                    throw new JgsRuntimeException(line, col, $"{name} does not support the conversion '%{kind}'.");
            }

            pieces.Add(new ScanPiece(new ScanConversion(kind, width, suppressed, set, negated), string.Empty));
        }

        FlushLiteral();
        return pieces;
    }

    /// <summary>The characters a <c>%[...]</c> set names, with each <c>a-z</c> range written out.</summary>
    private static string ExpandScanSet(string set)
    {
        var expanded = new StringBuilder(set.Length);
        for (int i = 0; i < set.Length; i++)
        {
            if (i + 2 < set.Length && set[i + 1] == '-' && set[i + 2] >= set[i])
            {
                for (char c = set[i]; c <= set[i + 2]; c++)
                {
                    expanded.Append(c);
                }

                i += 2;
            }
            else
            {
                expanded.Append(set[i]);
            }
        }

        return expanded.ToString();
    }

    /// <summary>
    /// Matches literal format text: whitespace in the format stands for any amount of whitespace in
    /// the input, including none, and anything else has to be there character for character.
    /// </summary>
    private static ScanStep MatchLiteral(string text, ref int at, string literal)
    {
        foreach (char expected in literal)
        {
            if (char.IsWhiteSpace(expected))
            {
                while (at < text.Length && char.IsWhiteSpace(text[at]))
                {
                    at++;
                }

                continue;
            }

            if (at >= text.Length)
            {
                return ScanStep.EndOfText;
            }

            if (text[at] != expected)
            {
                return ScanStep.Mismatch;
            }

            at++;
        }

        return ScanStep.Matched;
    }

    /// <summary>
    /// Reads one conversion at <paramref name="at"/>, storing what it read unless it was suppressed
    /// and counting it once when it did — a <c>%s</c> field is one element however many characters
    /// it holds, which is how MATLAB's <c>count</c> and size limit both count.
    /// </summary>
    private static ScanStep ReadConversion(string text, ref int at, ScanConversion conversion, ref int count,
        List<double> elements, StringBuilder characters)
    {
        if (conversion.SkipsWhitespace)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }
        }

        if (at >= text.Length)
        {
            return ScanStep.EndOfText;
        }

        int end = conversion.Width > 0 ? System.Math.Min(text.Length, at + conversion.Width) : text.Length;
        int start = at;

        if (conversion.IsText)
        {
            switch (conversion.Kind)
            {
                case 's':
                    while (at < end && !char.IsWhiteSpace(text[at]))
                    {
                        at++;
                    }

                    break;

                case 'c':
                    at = conversion.Width > 0 ? end : at + 1;
                    break;

                default:
                    while (at < end && conversion.Set.Contains(text[at]) != conversion.Negated)
                    {
                        at++;
                    }

                    break;
            }

            if (at == start)
            {
                return ScanStep.Mismatch;
            }

            if (!conversion.Suppressed)
            {
                for (int i = start; i < at; i++)
                {
                    elements.Add(text[i]);
                    characters.Append(text[i]);
                }

                count++;
            }

            return ScanStep.Matched;
        }

        double number;
        bool read = conversion.Kind switch
        {
            'd' or 'u' => ReadInteger(text, ref at, end, 10, out number),
            'o' => ReadInteger(text, ref at, end, 8, out number),
            'x' or 'X' => ReadInteger(text, ref at, end, 16, out number),
            'i' => ReadPrefixedInteger(text, ref at, end, out number),
            _ => ReadFloat(text, ref at, end, out number),
        };

        if (!read)
        {
            at = start;
            return ScanStep.Mismatch;
        }

        if (!conversion.Suppressed)
        {
            elements.Add(number);
            count++;
        }

        return ScanStep.Matched;
    }

    /// <summary>
    /// Reads an optionally signed whole number in <paramref name="radix"/> from <paramref name="at"/>
    /// up to <paramref name="end"/>, accepting a <c>0x</c> prefix when the radix is 16.
    /// </summary>
    private static bool ReadInteger(string text, ref int at, int end, int radix, out double value)
    {
        value = 0;
        int cursor = at;
        bool negative = false;
        if (cursor < end && (text[cursor] == '+' || text[cursor] == '-'))
        {
            negative = text[cursor] == '-';
            cursor++;
        }

        if (radix == 16 && cursor + 2 < end && text[cursor] == '0' && text[cursor + 1] is 'x' or 'X'
            && HexDigit(text[cursor + 2]) >= 0)
        {
            cursor += 2;
        }

        int digits = 0;
        while (cursor < end)
        {
            int digit = HexDigit(text[cursor]);
            if (digit < 0 || digit >= radix)
            {
                break;
            }

            value = (value * radix) + digit;
            digits++;
            cursor++;
        }

        if (digits == 0)
        {
            return false;
        }

        at = cursor;
        value = negative ? -value : value;
        return true;
    }

    /// <summary>
    /// <c>%i</c>: a whole number whose base its prefix names — <c>0x</c> for hexadecimal, a leading
    /// <c>0</c> for octal, anything else decimal.
    /// </summary>
    private static bool ReadPrefixedInteger(string text, ref int at, int end, out double value)
    {
        int cursor = at;
        if (cursor < end && (text[cursor] == '+' || text[cursor] == '-'))
        {
            cursor++;
        }

        int radix = 10;
        if (cursor < end && text[cursor] == '0')
        {
            radix = cursor + 1 < end && text[cursor + 1] is 'x' or 'X' ? 16 : 8;
        }

        return ReadInteger(text, ref at, end, radix, out value);
    }

    /// <summary>
    /// Reads a floating-point number from <paramref name="at"/> up to <paramref name="end"/>: a sign,
    /// digits with a point, an exponent — or <c>Inf</c> and <c>NaN</c> in any case, which are what
    /// <c>num2str</c> and <c>sprintf</c> write for those values.
    /// </summary>
    private static bool ReadFloat(string text, ref int at, int end, out double value)
    {
        value = 0;
        int cursor = at;
        bool negative = false;
        if (cursor < end && (text[cursor] == '+' || text[cursor] == '-'))
        {
            negative = text[cursor] == '-';
            cursor++;
        }

        if (end - cursor >= 3)
        {
            string word = text.Substring(cursor, 3);
            if (word.Equals("inf", StringComparison.OrdinalIgnoreCase))
            {
                value = negative ? double.NegativeInfinity : double.PositiveInfinity;
                at = cursor + 3;
                return true;
            }

            if (word.Equals("nan", StringComparison.OrdinalIgnoreCase))
            {
                value = double.NaN;
                at = cursor + 3;
                return true;
            }
        }

        int digits = 0;
        while (cursor < end && char.IsAsciiDigit(text[cursor]))
        {
            cursor++;
            digits++;
        }

        if (cursor < end && text[cursor] == '.')
        {
            cursor++;
            while (cursor < end && char.IsAsciiDigit(text[cursor]))
            {
                cursor++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return false;
        }

        if (cursor < end && text[cursor] is 'e' or 'E')
        {
            int exponent = cursor + 1;
            if (exponent < end && (text[exponent] == '+' || text[exponent] == '-'))
            {
                exponent++;
            }

            if (exponent < end && char.IsAsciiDigit(text[exponent]))
            {
                cursor = exponent;
                while (cursor < end && char.IsAsciiDigit(text[cursor]))
                {
                    cursor++;
                }
            }
        }

        if (!double.TryParse(text[at..cursor], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        at = cursor;
        return true;
    }

    /// <summary>The value of a hexadecimal digit, or -1 for any other character.</summary>
    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };
}
