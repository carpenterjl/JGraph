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
                    JgsValue.Str(JGraphScriptGlobals.MatlabEncodingName(entry.Encoding)), // UTF-16LE, windows-1252 (open item 318)
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
        target = CanonicalPrecision(target); // '*uchar' answers uint8 (open item 318, measured)
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

        // MATLAB code reading 'char' reads characters in the file's encoding, as R2025b does: a UTF-8 é is
        // one element, 'char=>uint8' its code, and a count is of characters (open item 318, ADR 0378,
        // measured). 'uchar', 'schar' and 'uint8=>char' read bytes.
        bool characters = CanonicalPrecision(source) == "char" && JgsRunningDialect.ThreadIsMatlab && skip == 0;
        if (characters)
        {
            foreach (char c in ReadCharacters(entry, limit))
            {
                values.Add(c);
            }
        }

        while (!characters && values.Count < limit)
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

        // A precision that keeps char ('*char', 'uint8=>char') answers characters in the same shape
        // (open item 34, measured: 8-by-1, 1-by-8 and 2-by-2 for the three sizes).
        if (target.Equals("char", StringComparison.OrdinalIgnoreCase))
        {
            int down = rows > 0 && values.Count > 0 ? rows : values.Count;
            int across = down == 0 ? 0 : (values.Count + down - 1) / down;
            if (down <= 1 || across == 0)
            {
                answer = JgsValue.Str(new string([.. values.Select(CharOfCode)]));
            }
            else
            {
                var codes = new double[down * across];
                values.CopyTo(codes);
                var rowTexts = new string[down];
                for (int r = 0; r < down; r++)
                {
                    var row = new char[across];
                    for (int c = 0; c < across; c++)
                    {
                        row[c] = CharOfCode(codes[(c * down) + r]);
                    }

                    rowTexts[r] = new string(row);
                }

                answer = JgsValue.CharMatrix(rowTexts);
            }
        }
        else if (!target.Equals("double", StringComparison.OrdinalIgnoreCase)
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
        bool matlab = JgsRunningDialect.ThreadIsMatlab;

        // A cell or a struct has no bytes (R2025b, measured; open item 318).
        if (matlab && args[1].Type is JgsType.Cell or JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:fwrite:UnsupportedClass",
                $"Cannot write value: unsupported class {ClassOf(args[1], JgsDialect.Matlab)}");
        }

        // fwrite(1, ...) and fwrite(2, ...) write to the console (open item 318, ADR 0378).
        if (matlab && args[0].Type is JgsType.Number or JgsType.Bool && args[0].AsNumber is 0 or 1 or 2)
        {
            return WriteToConsole(host, args, line, col);
        }

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

        double[] values = FwriteValues(args[1], line, col);
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

    /// <summary>
    /// The numbers <c>fwrite</c> writes for <paramref name="data"/>: text writes the characters it stands for
    /// (a char row, or a string scalar in MATLAB code), which is what a script saving a header expects.
    /// </summary>
    private static double[] FwriteValues(JgsValue data, int line, int col)
    {
        if (data.Type == JgsType.String || (data.IsStringArray && data.ArrayLength == 1))
        {
            string text = data.Type == JgsType.String ? data.AsString : TextOf(data);
            var codes = new double[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                codes[i] = text[i];
            }

            return codes;
        }

        return data.Type is JgsType.Number or JgsType.Bool ? [data.AsNumber] : ToDoubles("fwrite", data, line, col);
    }

    /// <summary>
    /// <c>fwrite(1, ...)</c> and <c>fwrite(2, ...)</c> in MATLAB code (open item 318, ADR 0378, measured in
    /// R2025b): the bytes the precision makes of each value, rounded and saturated, written to the console as
    /// one character a byte with every zero byte dropped (<c>[65 0 66]</c> and <c>uint16</c> 65 66 both show
    /// <c>AB</c>); id 2 goes to the error console, and <c>evalc</c> takes both. The answer is the count of
    /// elements. Id 0 and a skip are R2025b's notimplemented refusal.
    /// </summary>
    private static JgsValue WriteToConsole(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int id = (int)args[0].AsNumber;
        string precision = "uint8";
        bool skips = false;
        for (int at = 2; at < args.Count; at++)
        {
            if (IsTextScalar(args[at]))
            {
                string word = TextOf(args[at]);
                if (!IsMachineFormat(word))
                {
                    precision = word;
                }

                continue;
            }

            skips = true;
        }

        if (id == 0 || skips)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:notimplemented_mx",
                "Operation is not implemented for requested file identifier.");
        }

        (string source, _) = PrecisionParts("fwrite", precision, line, col);
        int width = WidthOf("fwrite", source, line, col);
        double[] values = FwriteValues(args[1], line, col);
        var text = new StringBuilder();
        foreach (double value in values)
        {
            foreach (byte b in EncodeValue(value, source, width))
            {
                if (b != 0)
                {
                    text.Append((char)b);
                }
            }
        }

        if (id == 1)
        {
            host.WriteOut(text.ToString());
        }
        else
        {
            host.WriteErrFromCode(text.ToString());
        }

        return JgsValue.Number(values.Length);
    }

    /// <summary>
    /// Up to <paramref name="limit"/> characters from where <paramref name="entry"/>'s stream stands, decoded
    /// in its encoding, the stream left after the bytes they took (open item 318, ADR 0378).
    /// </summary>
    private static string ReadCharacters(JGraphScriptGlobals.FileEntry entry, int limit)
    {
        FileStream stream = entry.Stream;
        long start = stream.Position;
        long remaining = System.Math.Max(0, stream.Length - start);
        bool whole = limit == int.MaxValue || (long)limit * 4 + 8 >= remaining;
        int size = (int)(whole ? remaining : (long)limit * 4 + 8);
        var bytes = new byte[size];
        int read = stream.ReadAtLeast(bytes, size, throwOnEndOfStream: false);
        string text = entry.Encoding.GetString(bytes, 0, read);
        if (text.Length <= limit && whole)
        {
            stream.Position = start + read;
            return text;
        }

        text = text[..System.Math.Min(limit, text.Length)];
        stream.Position = start + entry.Encoding.GetByteCount(text);
        return text;
    }

    /// <summary>A character code as a char: below zero is 0 and above 65535 is 65535, as R2025b's conversion saturates (<c>'int8=&gt;char'</c>, measured).</summary>
    private static char CharOfCode(double code) =>
        double.IsNaN(code) || code <= 0 ? '\0' : code >= char.MaxValue ? char.MaxValue : (char)System.Math.Round(code);

    /// <summary>The class a precision word reads and writes as: <c>uchar</c> is uint8, <c>schar</c> int8, <c>char*1</c> char, <c>short</c> int16, and so on.</summary>
    private static string CanonicalPrecision(string precision) => precision.Trim().ToLowerInvariant() switch
    {
        "uchar" or "unsigned char" => "uint8",
        "schar" or "signed char" or "integer*1" => "int8",
        "char*1" => "char",
        "short" or "integer*2" => "int16",
        "ushort" => "uint16",
        "int" or "long" or "integer*4" => "int32",
        "uint" or "ulong" => "uint32",
        "float" or "float32" or "real*4" => "single",
        "float64" or "real*8" => "double",
        "integer*8" => "int64",
        string other => other,
    };

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

        // A bounded read needs only the stretch of the file its count covers. Decoding the whole
        // remainder on every call made a file read in bounded pieces quadratic: a 1024-profile map
        // read one profile at a time decoded 4.3 GB to parse 8.5 MB. The window starts small and
        // doubles until the scan stops clear of its end, far enough that no token, lookahead or
        // character cut at the edge could have read differently, or until it holds the remainder.
        long start = entry.Stream.Position;
        int window = limit == int.MaxValue ? int.MaxValue : ScanWindowBytes;
        string text;
        ScanResult result;
        while (true)
        {
            (text, bool whole) = WindowOf(entry, start, window);
            result = Scan(text, format, limit, line, col, "fscanf");
            if (whole || result.Consumed + ScanWindowMargin <= text.Length)
            {
                break;
            }

            window = window > int.MaxValue / 2 ? int.MaxValue : window * 2;
        }

        // Only what the scan actually used is consumed. Reading to the end regardless was what made
        // a bounded fscanf leave the file at EOF and the next read come back empty.
        entry.Stream.Position = start + entry.Encoding.GetByteCount(text.AsSpan(0, result.Consumed));

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
        string body = string.Empty;
        long start = 0;
        if (args[0].Type is JgsType.Number or JgsType.Bool)
        {
            entry = EntryOf(host, "textscan", args, line, col);
            start = entry.Stream.Position;
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
        List<JgsValue> columns;
        int consumed;
        if (entry is null)
        {
            (columns, consumed) = JgsTextScanner.Scan(body, format, repetitions, options, line, col);
        }
        else
        {
            // A file read a counted number of records at a time decodes a window, as fscanf does
            // (comments are blanked in place and header lines skipped, so positions in the window are
            // positions in the file). A refusal inside a partial window may be a field cut at its
            // edge, so only the refusal the whole remainder gives is reported.
            int window = repetitions is >= 0 and < int.MaxValue ? ScanWindowBytes : int.MaxValue;
            while (true)
            {
                (body, bool whole) = WindowOf(entry, start, window);
                try
                {
                    (columns, consumed) = JgsTextScanner.Scan(body, format, repetitions, options, line, col);
                }
                catch (JgsRuntimeException) when (!whole)
                {
                    window = window > int.MaxValue / 2 ? int.MaxValue : window * 2;
                    continue;
                }

                if (whole || consumed + ScanWindowMargin <= body.Length)
                {
                    break;
                }

                window = window > int.MaxValue / 2 ? int.MaxValue : window * 2;
            }

            entry.Stream.Position = start + entry.Encoding.GetByteCount(body.AsSpan(0, consumed));
        }

        // A row of columns, which for a format of nothing but skipped fields is the 1-by-0 cell.
        JgsValue cell = JgsValue.Cell([.. columns]);
        cell.Reshape(1, columns.Count);

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
        string whitespace = " \b\t";
        double empty = double.NaN;
        bool collect = false;
        string? endOfLine = null;
        string? commentOpen = null;
        string? commentClose = null;
        bool multipleDelimsAsOne = false;
        var treatAsEmpty = new List<string>();
        bool returnOnError = true;

        string[] TextsOf(string option)
        {
            JgsValue value = args[at + 1];
            string[]? texts = TextElementsOf(value);
            if (texts is null || value.IsCharMatrix)
            {
                throw new JgsRuntimeException(line, col,
                    $"textscan: '{option}' takes text or a cell array of text.");
            }

            return texts;
        }

        for (; at + 1 < args.Count; at += 2)
        {
            string name = Str("textscan", args, at, line, col);
            switch (name.ToLowerInvariant())
            {
                case "delimiter":
                    // A char row names one delimiter per character, a cell one per element: ',;'
                    // splits on either (measured), where {',;'} would split on the pair.
                    if (args[at + 1].Type == JgsType.Cell || args[at + 1].IsStringArray)
                    {
                        foreach (string one in TextsOf("Delimiter"))
                        {
                            delimiters.Add(TranslateEscapes(one));
                        }
                    }
                    else
                    {
                        foreach (char one in TranslateEscapes(Str("textscan", args, at + 1, line, col)))
                        {
                            delimiters.Add(one.ToString());
                        }
                    }

                    break;
                case "headerlines":
                    headerLines = Count("textscan", args, at + 1, line, col);
                    break;
                case "whitespace":
                    whitespace = TranslateEscapes(Str("textscan", args, at + 1, line, col));
                    break;
                case "emptyvalue":
                    empty = Num("textscan", args, at + 1, line, col);
                    break;
                case "collectoutput":
                    collect = args[at + 1].IsTruthy;
                    break;
                case "endofline":
                    endOfLine = TranslateEscapes(Str("textscan", args, at + 1, line, col));
                    break;
                case "commentstyle":
                {
                    string[] marks = TextsOf("CommentStyle");
                    if (marks.Length is not (1 or 2))
                    {
                        throw new JgsRuntimeException(line, col,
                            "textscan: 'CommentStyle' is one marker, or a cell of an opening and a closing one.");
                    }

                    commentOpen = marks[0];
                    commentClose = marks.Length == 2 ? marks[1] : null;
                    break;
                }

                case "multipledelimsasone":
                    multipleDelimsAsOne = args[at + 1].IsTruthy;
                    break;
                case "treatasempty":
                    treatAsEmpty.AddRange(TextsOf("TreatAsEmpty"));
                    break;
                case "returnonerror":
                    returnOnError = args[at + 1].IsTruthy;
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"textscan: '{name}' is not an option it reads. It takes Delimiter, HeaderLines, " +
                        "Whitespace, EmptyValue, CollectOutput, EndOfLine, CommentStyle, MultipleDelimsAsOne, " +
                        "TreatAsEmpty and ReturnOnError.");
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
            EndOfLine = endOfLine,
            CommentOpen = commentOpen,
            CommentClose = commentClose,
            MultipleDelimsAsOne = multipleDelimsAsOne,
            TreatAsEmpty = treatAsEmpty,
            ReturnOnError = returnOnError,
        };
    }

    /// <summary>What is left of a file from where it stands, decoded in its own encoding.</summary>
    /// <summary>The first window a bounded <c>fscanf</c> decodes, in bytes; it doubles when too small.</summary>
    private const int ScanWindowBytes = 1 << 16;

    /// <summary>
    /// How far short of a partial window's end a scan must stop for its answer to be trusted: past any
    /// lookahead a conversion takes and any character a cut through a multi-byte sequence spoils.
    /// </summary>
    private const int ScanWindowMargin = 64;

    /// <summary>
    /// Up to <paramref name="window"/> bytes from <paramref name="start"/>, decoded, and whether they
    /// are all the file has left. The stream is left after what was read; the caller puts it back.
    /// </summary>
    private static (string Text, bool Whole) WindowOf(
        JGraphScriptGlobals.FileEntry entry, long start, int window)
    {
        long remaining = System.Math.Max(0, entry.Stream.Length - start);
        int size = (int)System.Math.Min(remaining, window);
        var bytes = new byte[size];
        entry.Stream.Position = start;
        int read = entry.Stream.ReadAtLeast(bytes, size, throwOnEndOfStream: false);
        return (entry.Encoding.GetString(bytes, 0, read), read >= remaining);
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
