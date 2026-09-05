using System.IO;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The documented forms of the file verbs that M76 added: what <c>fopen</c> can be asked about a
/// file it opened, the shapes and precisions <c>fread</c> and <c>fwrite</c> take, the counts and
/// positions the readers report, and the byte order any of them can be told to use.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The one place a file id is turned into a stream, so one message says it is not open.</summary>
    private static FileStream StreamOf(JGraphScriptGlobals host, string name,
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        int id = Count(name, args, 0, line, col);
        return host.FileFor(id)
            ?? throw new JgsRuntimeException(line, col, $"{name}: {id} is not an open file.");
    }

    private static JGraphScriptGlobals.FileEntry EntryOf(JGraphScriptGlobals host, string name,
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        int id = Count(name, args, 0, line, col);
        return host.OpenFileFor(id)
            ?? throw new JgsRuntimeException(line, col, $"{name}: {id} is not an open file.");
    }

    // --- fopen ------------------------------------------------------------------------------

    /// <summary>
    /// <c>fopen</c> in all of its forms: opening a file, asking which ids are open, and asking an
    /// open id what it is.
    /// </summary>
    private static JgsValue[] Open(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args,
        int wanted, int line, int col)
    {
        ArityRange("fopen", args, 1, 4, line, col);

        // fopen(fid) asks about a file rather than opening one, and the two are told apart by what
        // the first argument is — a number is an id, text is a name.
        if (args[0].Type is JgsType.Number or JgsType.Bool)
        {
            JGraphScriptGlobals.FileEntry entry = EntryOf(host, "fopen", args, line, col);
            return wanted <= 1
                ? [JgsValue.Str(entry.Path)]
                :
                [
                    JgsValue.Str(entry.Path),
                    JgsValue.Str(entry.Permission),
                    JgsValue.Str(entry.MachineFormat),
                    JgsValue.Str(entry.Encoding.WebName.ToUpperInvariant()),
                ];
        }

        string first = Str("fopen", args, 0, line, col);
        if (args.Count == 1 && first.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<int> ids = host.OpenFileIds;
            var numbers = new double[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                numbers[i] = ids[i];
            }

            return [Numbers(numbers)];
        }

        string mode = args.Count >= 2 ? Str("fopen", args, 1, line, col) : "r";
        string format = args.Count >= 3 ? Str("fopen", args, 2, line, col) : string.Empty;
        string encoding = args.Count >= 4 ? Str("fopen", args, 3, line, col) : string.Empty;

        (int id, string error) = host.OpenFile(first, mode, format, encoding);
        return wanted <= 1
            ? [JgsValue.Number(id)]
            : [JgsValue.Number(id), JgsValue.Str(error)];
    }

    // --- fread and fwrite -------------------------------------------------------------------

    /// <summary>
    /// A precision word as MATLAB spells it: a plain class, <c>'*class'</c> to keep the class read,
    /// or <c>'in=&gt;out'</c> to name both sides.
    /// </summary>
    private static (string Source, string Target) PrecisionParts(string name, string precision, int line, int col)
    {
        string text = precision.Trim();
        if (text.StartsWith("bit", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("ubit", StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the bit precisions like '{text}' need a cursor that counts bits rather than " +
                "bytes, which this reader has not got.");
        }

        int arrow = text.IndexOf("=>", StringComparison.Ordinal);
        if (arrow >= 0)
        {
            return (text[..arrow].Trim(), text[(arrow + 2)..].Trim());
        }

        if (text.StartsWith('*'))
        {
            string both = text[1..].Trim();
            return (both, both);
        }

        return (text, "double");
    }

    /// <summary>
    /// <c>fread</c>: how much to read, in what precision, skipping how many bytes between elements,
    /// and in which byte order.
    /// </summary>
    private static JgsValue[] Read(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args,
        int wanted, int line, int col)
    {
        ArityRange("fread", args, 1, 5, line, col);
        JGraphScriptGlobals.FileEntry entry = EntryOf(host, "fread", args, line, col);
        FileStream stream = entry.Stream;

        string precision = "uint8";
        double[]? size = null;
        int skip = 0;
        string format = entry.MachineFormat;

        // Everything after the id is positional but loosely so: a word is a precision or a byte
        // order, a number is a size and then a skip. Reading them by kind rather than by position is
        // what lets fread(fid, 'uint8', 'ieee-be') and fread(fid, 100, 'uint8', 2) both be written.
        bool sawSize = false;
        for (int at = 1; at < args.Count; at++)
        {
            if (IsTextScalar(args[at]))
            {
                string word = Str("fread", args, at, line, col);
                if (IsMachineFormat(word))
                {
                    format = JGraphScriptGlobals.MachineFormatNamed(word);
                }
                else
                {
                    precision = word;
                }

                continue;
            }

            if (!sawSize)
            {
                size = SizeArgument("fread", args, at, line, col);
                sawSize = true;
            }
            else
            {
                skip = Count("fread", args, at, line, col);
            }
        }

        (string source, string target) = PrecisionParts("fread", precision, line, col);
        int width = WidthOf("fread", source, line, col);

        int rows = -1;
        int limit = int.MaxValue;
        if (size is { Length: 1 })
        {
            limit = double.IsInfinity(size[0]) ? int.MaxValue : (int)size[0];
        }
        else if (size is { Length: 2 })
        {
            rows = (int)size[0];
            limit = double.IsInfinity(size[1]) ? int.MaxValue : rows * (int)size[1];
        }

        var values = new List<double>();
        var element = new byte[width];
        while (values.Count < limit)
        {
            int got = stream.Read(element, 0, width);
            if (got < width)
            {
                break;
            }

            values.Add(DecodeValue(Ordered(element, format), source));
            if (skip > 0)
            {
                stream.Seek(System.Math.Min(skip, System.Math.Max(0, stream.Length - stream.Position)),
                    SeekOrigin.Current);
            }
        }

        // MATLAB answers a column, and shapes it into rows when a size said how many.
        JgsValue answer = Numbers([.. values]);
        if (rows > 0 && values.Count > 0)
        {
            int columns = (values.Count + rows - 1) / rows;
            var padded = new double[rows * columns];
            values.CopyTo(padded);
            answer = Numbers(padded);
            answer.Reshape(rows, columns);
        }
        else if (values.Count > 1)
        {
            answer.Reshape(values.Count, 1);
        }

        if (!target.Equals("double", StringComparison.OrdinalIgnoreCase)
            && JgsNumericClasses.Parse(target) is { } numericClass)
        {
            answer = ToNumericClass("fread", numericClass, answer, line, col);
        }

        return wanted <= 1 ? [answer] : [answer, JgsValue.Number(values.Count)];
    }

    /// <summary><c>fwrite</c>, with a precision, a gap between elements, and a byte order.</summary>
    private static JgsValue Write(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("fwrite", args, 2, 5, line, col);
        JGraphScriptGlobals.FileEntry entry = EntryOf(host, "fwrite", args, line, col);
        FileStream stream = entry.Stream;

        string precision = "uint8";
        int skip = 0;
        string format = entry.MachineFormat;

        for (int at = 2; at < args.Count; at++)
        {
            if (IsTextScalar(args[at]))
            {
                string word = Str("fwrite", args, at, line, col);
                if (IsMachineFormat(word))
                {
                    format = JGraphScriptGlobals.MachineFormatNamed(word);
                }
                else
                {
                    precision = word;
                }

                continue;
            }

            skip = Count("fwrite", args, at, line, col);
        }

        (string source, _) = PrecisionParts("fwrite", precision, line, col);
        int width = WidthOf("fwrite", source, line, col);

        // Text writes the characters it stands for, which is what a script saving a header expects
        // and what fwrite(fid, 'hello') meant before it was an error.
        double[] values;
        if (args[1].Type == JgsType.String)
        {
            string text = args[1].AsString;
            values = new double[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                values[i] = text[i];
            }
        }
        else
        {
            values = args[1].Type is JgsType.Number or JgsType.Bool
                ? [args[1].AsNumber]
                : ToDoubles("fwrite", args[1], line, col);
        }

        foreach (double value in values)
        {
            stream.Write(Ordered(EncodeValue(value, source, width), format));
            for (int i = 0; i < skip; i++)
            {
                stream.WriteByte(0);
            }
        }

        return JgsValue.Number(values.Length);
    }

    private static bool IsMachineFormat(string word) => word.ToLowerInvariant()
        is "n" or "native" or "l" or "b" or "s" or "a"
        or "ieee-le" or "ieee-be" or "ieee-le.l64" or "ieee-be.l64";

    /// <summary>
    /// The bytes in the order the file wants them. Everything is encoded little-endian first because
    /// that is what this machine does, so a big-endian file is that reversed.
    /// </summary>
    private static byte[] Ordered(byte[] bytes, string format)
    {
        if (format != "ieee-be" || bytes.Length < 2)
        {
            return bytes;
        }

        var flipped = (byte[])bytes.Clone();
        Array.Reverse(flipped);
        return flipped;
    }

    /// <summary>A <c>sizeA</c> argument: a count, <c>Inf</c>, or a pair of them.</summary>
    private static double[] SizeArgument(string name, IReadOnlyList<JgsValue> args, int at, int line, int col)
    {
        double[] size = FlattenColumnMajor(name, args[at], line, col);
        if (size.Length is 0 or > 2)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a size is one number or two, but got {size.Length}.");
        }

        foreach (double value in size)
        {
            if (value < 0 || (double.IsNaN(value)))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a size is a nonnegative number or Inf, but got {value}.");
            }
        }

        if (size.Length == 2 && double.IsInfinity(size[0]))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: only the second of a size pair may be Inf.");
        }

        return size;
    }

    // --- ferror -----------------------------------------------------------------------------

    /// <summary>
    /// <c>ferror</c>, with the number MATLAB reports beside the message. Every failure here is
    /// already an exception, so a stream still open has by construction nothing to report.
    /// </summary>
    private static JgsValue[] Failure(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args,
        int wanted, int line, int col)
    {
        ArityRange("ferror", args, 1, 2, line, col);
        _ = StreamOf(host, "ferror", args, line, col);
        return wanted <= 1
            ? [JgsValue.Str(string.Empty)]
            : [JgsValue.Str(string.Empty), JgsValue.Number(0)];
    }

    // --- fscanf and textscan ----------------------------------------------------------------

    /// <summary>
    /// <c>fscanf</c>: the whole remainder of the file read under a format, or as much of it as a
    /// size asked for — and the file left exactly where the reading stopped.
    /// </summary>
    private static JgsValue[] ScanFile(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args,
        int wanted, int line, int col)
    {
        ArityRange("fscanf", args, 2, 3, line, col);
        JGraphScriptGlobals.FileEntry entry = EntryOf(host, "fscanf", args, line, col);
        string format = Str("fscanf", args, 1, line, col);

        (int rows, int limit) = args.Count == 3
            ? ScanSize("fscanf", args, 2, line, col)
            : (-1, int.MaxValue);

        (string text, long start) = RemainderOf(entry);
        ScanResult result = Scan(text, format, limit, line, col, "fscanf");

        // Only what the scan actually used is consumed. Reading to the end regardless was what made
        // a bounded fscanf leave the file at EOF and the next read come back empty.
        entry.Stream.Position = start + entry.Encoding.GetByteCount(text[..result.Consumed]);

        return ScanOutputs(result, rows, wanted);
    }

    /// <summary>
    /// <c>textscan</c>: a table read into one cell per conversion, from a file or from a piece of
    /// text, as many times as asked, with the options that describe the layout.
    /// </summary>
    private static JgsValue[] ScanColumns(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args,
        int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col,
                $"textscan expects at least 2 argument(s), but got {args.Count}.");
        }

        string format = Str("textscan", args, 1, line, col);

        // The source is either an open file or a piece of text; a number is an id and text is the
        // thing to read, which is the same rule fopen uses.
        JGraphScriptGlobals.FileEntry? entry = null;
        string body;
        long start = 0;
        if (args[0].Type is JgsType.Number or JgsType.Bool)
        {
            entry = EntryOf(host, "textscan", args, line, col);
            (body, start) = RemainderOf(entry);
        }
        else
        {
            body = Str("textscan", args, 0, line, col);
        }

        int repetitions = int.MaxValue;
        int at = 2;
        if (args.Count > 2 && !IsTextScalar(args[2]))
        {
            double count = Num("textscan", args, 2, line, col);
            repetitions = double.IsInfinity(count) ? int.MaxValue : (int)count;
            at = 3;
        }

        JgsTextScanner.Options options = TextScanOptions(args, at, line, col);
        (List<JgsValue> columns, int consumed) = JgsTextScanner.Scan(
            body, format, repetitions, options, line, col);

        if (entry is not null)
        {
            entry.Stream.Position = start + entry.Encoding.GetByteCount(body[..consumed]);
        }

        JgsValue cell = JgsValue.Cell([.. columns]);
        if (columns.Count > 1)
        {
            cell.Reshape(1, columns.Count);
        }

        return wanted <= 1
            ? [cell]
            : [cell, JgsValue.Number(entry is not null ? entry.Stream.Position : consumed)];
    }

    /// <summary>The trailing name/value pairs <c>textscan</c> takes, and the refusal of the rest.</summary>
    private static JgsTextScanner.Options TextScanOptions(
        IReadOnlyList<JgsValue> args, int at, int line, int col)
    {
        var delimiters = new List<string>();
        int headerLines = 0;
        string whitespace = " \t";
        double empty = double.NaN;
        bool collect = false;

        for (; at + 1 < args.Count; at += 2)
        {
            string name = Str("textscan", args, at, line, col);
            switch (name.ToLowerInvariant())
            {
                case "delimiter":
                    if (args[at + 1].Type == JgsType.Cell)
                    {
                        foreach (JgsValue one in args[at + 1].AsCell)
                        {
                            delimiters.Add(one.Type == JgsType.String ? one.AsString : one.Display());
                        }
                    }
                    else
                    {
                        delimiters.Add(Str("textscan", args, at + 1, line, col));
                    }

                    break;
                case "headerlines":
                    headerLines = Count("textscan", args, at + 1, line, col);
                    break;
                case "whitespace":
                    whitespace = Str("textscan", args, at + 1, line, col);
                    break;
                case "emptyvalue":
                    empty = Num("textscan", args, at + 1, line, col);
                    break;
                case "collectoutput":
                    collect = args[at + 1].IsTruthy;
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"textscan: '{name}' is not an option it reads. It takes Delimiter, " +
                        "HeaderLines, Whitespace, EmptyValue and CollectOutput.");
            }
        }

        if (at < args.Count)
        {
            throw new JgsRuntimeException(line, col,
                $"textscan: '{Str("textscan", args, at, line, col)}' has no value after it.");
        }

        return new JgsTextScanner.Options
        {
            Delimiters = delimiters,
            HeaderLines = headerLines,
            Whitespace = whitespace,
            EmptyValue = empty,
            CollectOutput = collect,
        };
    }

    /// <summary>What is left of a file from where it stands, decoded in its own encoding.</summary>
    private static (string Text, long Start) RemainderOf(JGraphScriptGlobals.FileEntry entry)
    {
        long start = entry.Stream.Position;
        var rest = new byte[System.Math.Max(0, entry.Stream.Length - start)];
        int read = entry.Stream.Read(rest, 0, rest.Length);
        return (entry.Encoding.GetString(rest, 0, read), start);
    }

    // --- the line readers -------------------------------------------------------------------

    /// <summary>
    /// <c>fgetl</c> and <c>fgets</c>, which differ only in whether the terminator comes back, plus
    /// the character count <c>fgets</c> can be limited to and the terminator length either reports.
    /// </summary>
    private static JgsValue[] ReadLine(JGraphScriptGlobals host, string name,
        IReadOnlyList<JgsValue> args, int wanted, bool keepTerminator, int line, int col)
    {
        ArityRange(name, args, 1, name == "fgets" ? 2 : 1, line, col);
        JGraphScriptGlobals.FileEntry entry = EntryOf(host, name, args, line, col);

        int limit = args.Count == 2 ? Count(name, args, 1, line, col) : int.MaxValue;
        string? text = ReadLineFrom(entry.Stream, keepTerminator: true, entry.Encoding, limit);
        if (text is null)
        {
            // MATLAB answers -1 (a number) at end of file, which scripts test with ischar.
            return wanted <= 1 ? [JgsValue.Number(-1)] : [JgsValue.Number(-1), JgsValue.Number(-1)];
        }

        string body = text.TrimEnd('\n').TrimEnd('\r');
        int terminator = text.Length - body.Length;
        JgsValue answer = JgsValue.Str(keepTerminator ? text : body);
        return wanted <= 1 ? [answer] : [answer, JgsValue.Number(terminator)];
    }
}
