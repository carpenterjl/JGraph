using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M53 wave J, the file half: the six readers and writers the toolbox carries for the plain-text data
/// files that predate the table type, and the one binary transport format among them.
/// </summary>
/// <remarks>
/// <para>
/// These are the toolbox's own file names rather than the base language's, and each has a shape the
/// base readers do not: a case name file is one name per line with no delimiter at all; a tab-delimited
/// data file carries a row of variable names and a column of case names around the numbers; and a
/// transport file is a fixed-format binary in a floating-point format no machine has used since the
/// 1970s.
/// </para>
/// <para>
/// All six take a path the same way every other reader in JGraph does, which means a relative name
/// lands beside the running script rather than wherever the process happens to be.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the toolbox's own file readers and writers.</summary>
    private static void RegisterStatisticsFileBuiltins(JgsEnvironment env, JGraphScriptGlobals? host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0]) { MultiOutput = both }));

        Define("caseread", (args, line, col) => ReadCaseNames(host, args, line, col));
        Define("casewrite", (args, line, col) => WriteCaseNames(host, args, line, col));
        DefineBoth("tblread", (args, wanted, line, col) => ReadDataFile(host, args, wanted, line, col));
        Define("tblwrite", (args, line, col) => WriteDataFile(host, args, line, col));
        Define("tdfread", (args, line, col) => ReadTabDelimited(host, args, line, col));
        Define("xptread", (args, line, col) => ReadTransportFile(host, args, line, col));
    }

    /// <summary>A path as the running script means it, which is beside itself when it is relative.</summary>
    private static string StatsPath(JGraphScriptGlobals? host, string path, bool forWriting) =>
        host is null ? path : forWriting ? host.ResolveForWrite(path) : host.Resolve(path);

    // --- Case names ---------------------------------------------------------------------------------

    /// <summary><c>names = caseread(file)</c>: one name per line, as a cell of char rows.</summary>
    private static JgsValue ReadCaseNames(
        JGraphScriptGlobals? host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("caseread", args, 1, 1, line, col);
        string path = StatsPath(host, Str("caseread", args, 0, line, col), forWriting: false);
        string[] lines = ReadLines("caseread", path, line, col);

        var cells = new JgsValue[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            cells[i] = JgsValue.Str(lines[i].TrimEnd());
        }

        JgsValue names = JgsValue.Cell(cells);
        names.ReshapeDims([lines.Length, 1]);
        return names;
    }

    /// <summary><c>casewrite(names, file)</c>: the same file written back.</summary>
    private static JgsValue WriteCaseNames(
        JGraphScriptGlobals? host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("casewrite", args, 2, 2, line, col);
        string[] names = TextElements("casewrite", args[0], line, col);
        string path = StatsPath(host, Str("casewrite", args, 1, line, col), forWriting: true);

        var text = new StringBuilder();
        foreach (string name in names)
        {
            text.Append(name).Append('\n');
        }

        WriteText("casewrite", path, text.ToString(), line, col);
        return JgsValue.Null;
    }

    // --- The tabular pair ---------------------------------------------------------------------------

    /// <summary>
    /// <c>[data, varnames, casenames] = tblread(file)</c>: a file with a row of variable names, a column
    /// of case names, and numbers in the rectangle they enclose.
    /// </summary>
    private static JgsValue[] ReadDataFile(
        JGraphScriptGlobals? host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("tblread", args, 1, 2, line, col);
        string path = StatsPath(host, Str("tblread", args, 0, line, col), forWriting: false);
        char delimiter = args.Count > 1
            ? DelimiterWord("tblread", Str("tblread", args, 1, line, col), line, col)
            : ' ';

        string[] lines = ReadLines("tblread", path, line, col);
        if (lines.Length < 2)
        {
            throw new JgsRuntimeException(line, col,
                "tblread: the file needs a row of variable names and at least one row of data.");
        }

        string[] variables = Split(lines[0], delimiter);
        var caseNames = new List<string>();
        var rows = new List<double[]>();

        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0)
            {
                continue;
            }

            string[] fields = Split(lines[i], delimiter);
            if (fields.Length != variables.Length + 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"tblread: row {i + 1} has {fields.Length} fields where the header wants "
                    + $"{variables.Length + 1} — a case name and one number per variable.");
            }

            caseNames.Add(fields[0]);
            var values = new double[variables.Length];
            for (int j = 0; j < variables.Length; j++)
            {
                if (!double.TryParse(fields[j + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[j]))
                {
                    values[j] = double.NaN;
                }
            }

            rows.Add(values);
        }

        var flat = new double[rows.Count * variables.Length];
        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < variables.Length; c++)
            {
                flat[r + (c * rows.Count)] = rows[r][c];
            }
        }

        return Outputs(
            wanted,
            JgsMatrix.FromColumnMajor(flat, rows.Count, variables.Length),
            CharacterMatrix(variables),
            CharacterMatrix([.. caseNames]));
    }

    /// <summary><c>tblwrite(data, varnames, casenames, file)</c>: the same file written back.</summary>
    private static JgsValue WriteDataFile(
        JGraphScriptGlobals? host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("tblwrite", args, 4, 5, line, col);
        double[,] data = AsRectangle("tblwrite", args[0], line, col);
        string[] variables = TextElements("tblwrite", args[1], line, col);
        string[] caseNames = TextElements("tblwrite", args[2], line, col);
        string path = StatsPath(host, Str("tblwrite", args, 3, line, col), forWriting: true);
        char delimiter = args.Count > 4
            ? DelimiterWord("tblwrite", Str("tblwrite", args, 4, line, col), line, col)
            : ' ';

        int rows = data.GetLength(0);
        int columns = data.GetLength(1);
        if (variables.Length != columns || caseNames.Length != rows)
        {
            throw new JgsRuntimeException(line, col,
                "tblwrite: one variable name per column and one case name per row.");
        }

        var text = new StringBuilder();
        text.Append(string.Join(delimiter, variables)).Append('\n');
        for (int r = 0; r < rows; r++)
        {
            text.Append(caseNames[r]);
            for (int c = 0; c < columns; c++)
            {
                text.Append(delimiter).Append(data[r, c].ToString("G", CultureInfo.InvariantCulture));
            }

            text.Append('\n');
        }

        WriteText("tblwrite", path, text.ToString(), line, col);
        return JgsValue.Null;
    }

    /// <summary>
    /// <c>s = tdfread(file)</c>: a tab-delimited file read into a structure with one field per column,
    /// numeric where every value in the column parsed as a number and text where any did not.
    /// </summary>
    private static JgsValue ReadTabDelimited(
        JGraphScriptGlobals? host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("tdfread", args, 1, 2, line, col);
        string path = StatsPath(host, Str("tdfread", args, 0, line, col), forWriting: false);
        char delimiter = args.Count > 1
            ? DelimiterWord("tdfread", Str("tdfread", args, 1, line, col), line, col)
            : '\t';

        string[] lines = ReadLines("tdfread", path, line, col);
        if (lines.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "tdfread: the file is empty.");
        }

        string[] headers = Split(lines[0], delimiter);
        var columns = new List<string>[headers.Length];
        for (int i = 0; i < headers.Length; i++)
        {
            columns[i] = [];
        }

        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0)
            {
                continue;
            }

            string[] fields = Split(lines[i], delimiter);
            for (int j = 0; j < headers.Length; j++)
            {
                columns[j].Add(j < fields.Length ? fields[j] : string.Empty);
            }
        }

        var built = new List<(string Name, JgsValue Value)>();
        for (int j = 0; j < headers.Length; j++)
        {
            // A column is numeric only when the whole of it is: one word in a column of numbers makes
            // the column text, because half a column of numbers is not a variable.
            bool numeric = columns[j].Count > 0;
            var values = new double[columns[j].Count];
            for (int r = 0; r < columns[j].Count && numeric; r++)
            {
                numeric = double.TryParse(
                    columns[j][r], NumberStyles.Float, CultureInfo.InvariantCulture, out values[r]);
            }

            built.Add((
                FieldName(headers[j], j),
                numeric
                    ? JgsMatrix.FromColumnMajor(values, values.Length, 1)
                    : CharacterMatrix([.. columns[j]])));
        }

        return Structure([.. built]);
    }

    /// <summary>A header turned into something that can be a field name.</summary>
    private static string FieldName(string header, int index)
    {
        var name = new StringBuilder();
        foreach (char letter in header.Trim())
        {
            name.Append(char.IsLetterOrDigit(letter) || letter == '_' ? letter : '_');
        }

        if (name.Length == 0 || !char.IsLetter(name[0]))
        {
            name.Insert(0, "Var" + (index + 1).ToString(CultureInfo.InvariantCulture) + "_");
        }

        return name.ToString();
    }

    // --- The transport file -------------------------------------------------------------------------

    /// <summary>
    /// <c>s = xptread(file)</c>: a SAS transport file, read into a structure with one field per variable.
    /// </summary>
    /// <remarks>
    /// The format is a fixed-record binary from the days of the IBM 360, and its numbers are in that
    /// machine's floating-point format rather than the one every processor since has used: a
    /// seven-bit exponent of <em>sixteen</em>, and a fraction that is not normalized to an implied
    /// leading one. Converting it is therefore an unpacking rather than a reinterpretation, and it is
    /// the only part of reading one of these files that is not simply slicing fixed-width fields.
    /// </remarks>
    private static JgsValue ReadTransportFile(
        JGraphScriptGlobals? host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("xptread", args, 1, 2, line, col);
        string path = StatsPath(host, Str("xptread", args, 0, line, col), forWriting: false);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new JgsRuntimeException(line, col, $"xptread: cannot read '{path}': {ex.Message}");
        }

        const int Record = 80;
        if (bytes.Length < Record * 8 || Ascii(bytes, 0, 27) != "HEADER RECORD*******LIBRARY")
        {
            throw new JgsRuntimeException(line, col,
                "xptread: this is not a version 5 transport file — it does not begin with a library header record.");
        }

        // Records 1 to 7 are the library and member headers; the variable descriptors begin after the
        // namestr header record, which announces how many of them there are.
        int at = Record * 7;
        if (Ascii(bytes, at, 20) != "HEADER RECORD*******")
        {
            throw new JgsRuntimeException(line, col, "xptread: the variable descriptor header is missing.");
        }

        int count = int.Parse(Ascii(bytes, at + 54, 4).Trim(), CultureInfo.InvariantCulture);
        at += Record;

        var names = new string[count];
        var numeric = new bool[count];
        var widths = new int[count];
        var offsets = new int[count];

        int position = 0;
        for (int i = 0; i < count; i++)
        {
            int start = at + (i * 140);
            if (start + 140 > bytes.Length)
            {
                throw new JgsRuntimeException(line, col, "xptread: the file ends inside a variable descriptor.");
            }

            numeric[i] = ReadShort(bytes, start) == 1;
            widths[i] = ReadShort(bytes, start + 4);
            names[i] = Ascii(bytes, start + 8, 8).Trim();
            offsets[i] = position;
            position += widths[i];
        }

        int stride = position;
        at += (int)(Math.Ceiling(count * 140.0 / Record) * Record);
        if (at + Record > bytes.Length || Ascii(bytes, at, 20) != "HEADER RECORD*******")
        {
            throw new JgsRuntimeException(line, col, "xptread: the observation header is missing.");
        }

        at += Record;

        var text = new List<string>[count];
        var values = new List<double>[count];
        for (int i = 0; i < count; i++)
        {
            text[i] = [];
            values[i] = [];
        }

        for (int start = at; start + stride <= bytes.Length; start += stride)
        {
            // The last block is padded with blanks to fill its record, and a row of blanks is padding
            // rather than an observation of nothing.
            bool blank = true;
            for (int b = 0; b < stride && blank; b++)
            {
                blank = bytes[start + b] is 0x20 or 0x00;
            }

            if (blank)
            {
                break;
            }

            for (int i = 0; i < count; i++)
            {
                int field = start + offsets[i];
                if (numeric[i])
                {
                    values[i].Add(IbmDouble(bytes, field, widths[i]));
                }
                else
                {
                    text[i].Add(Ascii(bytes, field, widths[i]).TrimEnd());
                }
            }
        }

        var fields = new List<(string Name, JgsValue Value)>();
        for (int i = 0; i < count; i++)
        {
            fields.Add((
                FieldName(names[i], i),
                numeric[i]
                    ? JgsMatrix.FromColumnMajor([.. values[i]], values[i].Count, 1)
                    : CharacterMatrix([.. text[i]])));
        }

        return Structure([.. fields]);
    }

    /// <summary>One IBM 360 floating-point number, of one to eight bytes, as a double.</summary>
    private static double IbmDouble(byte[] bytes, int at, int width)
    {
        if (at + width > bytes.Length || width < 1)
        {
            return double.NaN;
        }

        bool negative = (bytes[at] & 0x80) != 0;
        int exponent = bytes[at] & 0x7F;

        double fraction = 0;
        for (int i = 1; i < width; i++)
        {
            fraction = (fraction * 256) + bytes[at + i];
        }

        if (fraction == 0)
        {
            // A missing value is a single byte naming which kind of missing it is, and every one of
            // them reads as not-a-number here because that is the only kind JGraph has.
            return bytes[at] is 0x2E or >= 0x41 and <= 0x5A && width > 0 && AllZero(bytes, at + 1, width - 1)
                ? double.NaN
                : 0;
        }

        // The exponent is excess-64 and counts powers of sixteen; the fraction is the bytes after the
        // first, read as an integer and divided back down by however many of them there were.
        double scale = Math.Pow(16, exponent - 64) / Math.Pow(256, width - 1);
        double answer = fraction * scale;
        return negative ? -answer : answer;
    }

    private static bool AllZero(byte[] bytes, int at, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (at + i >= bytes.Length || bytes[at + i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int ReadShort(byte[] bytes, int at) =>
        at + 1 < bytes.Length ? (bytes[at] << 8) | bytes[at + 1] : 0;

    private static string Ascii(byte[] bytes, int at, int length)
    {
        int available = Math.Max(Math.Min(length, bytes.Length - at), 0);
        return Encoding.ASCII.GetString(bytes, at, available);
    }

    // --- Shared small pieces ------------------------------------------------------------------------

    private static string[] ReadLines(string name, string path, int line, int col)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new JgsRuntimeException(line, col, $"{name}: cannot read '{path}': {ex.Message}");
        }
    }

    private static void WriteText(string name, string path, string text, int line, int col)
    {
        try
        {
            File.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new JgsRuntimeException(line, col, $"{name}: cannot write '{path}': {ex.Message}");
        }
    }

    private static char DelimiterWord(string name, string word, int line, int col) =>
        word.ToLowerInvariant() switch
        {
            "space" or " " => ' ',
            "tab" or "\t" => '\t',
            "comma" or "," => ',',
            "semi" or ";" => ';',
            "bar" or "|" => '|',
            _ => throw new JgsRuntimeException(line, col,
                $"{name}: '{word}' is not a delimiter. The delimiters are 'space', 'tab', 'comma', 'semi' and 'bar'."),
        };

    /// <summary>Splits a line on a delimiter, treating a run of spaces as one.</summary>
    private static string[] Split(string text, char delimiter) =>
        delimiter == ' '
            ? text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.ConvertAll(text.Split(delimiter), static field => field.Trim());

    /// <summary>
    /// A set of names as MATLAB's own char matrix: one row each, blank-padded to the longest. These
    /// names answer in that shape rather than as a cell because that is what the readers document, and
    /// what the writers beside them take back.
    /// </summary>
    private static JgsValue CharacterMatrix(string[] names)
    {
        if (names.Length == 0)
        {
            return JgsValue.Array([]);
        }

        var cells = new JgsValue[names.Length];
        int width = 0;
        foreach (string name in names)
        {
            width = Math.Max(width, name.Length);
        }

        for (int i = 0; i < names.Length; i++)
        {
            cells[i] = JgsValue.Str(names[i].PadRight(width));
        }

        JgsValue rows = JgsValue.Cell(cells);
        rows.ReshapeDims([names.Length, 1]);
        return rows;
    }
}
