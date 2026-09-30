namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// What the legacy mixins call on their object: the object's own read, write and line methods, which
/// for a serialport are the client's and for a visadev start a VISA read first (LegacyVisa's mixins
/// call <c>readline(obj)</c>, and so reach visalib.Resource's).
/// </summary>
internal interface ILegacyTransport
{
    TransportClient Live(DeviceCall call);

    JgsValue GetProperty(string name, DeviceCall call);

    JgsValue LegacyRead(DeviceCall call);

    JgsValue LegacyReadLine(DeviceCall call);

    JgsValue LegacyWriteRead(DeviceCall call);

    JgsValue LegacyReadBinblock(DeviceCall call);

    void LegacyWrite(DeviceCall call);

    void LegacyWriteLine(DeviceCall call);

    void LegacyWriteBinblock(DeviceCall call);
}

/// <summary>
/// The hidden legacy methods a <c>serialport</c> keeps from the <c>serial</c> object it replaced,
/// transcribed from R2025b's <c>LegacyASCIIMixin</c>, <c>LegacyBinaryMixin</c>,
/// <c>LegacyBinblockMixin</c> and <c>LegacyQueryMixin</c>: each is written in terms of the modern
/// methods (<c>writeline</c>, <c>read</c>, <c>readline</c>, <c>writeread</c>, binblock) and the text
/// functions, as the mixins are.
/// </summary>
internal static class SerialportLegacy
{
    /// <summary>Utility.convertLegacyPrecision: the old precision names and their sizes.</summary>
    private static (string Precision, int Size) LegacyPrecision(JgsValue value, DeviceCall call)
    {
        string name = DeviceChecks.IsText(value) ? DeviceChecks.Text(value) : "";
        return name switch
        {
            "uchar" or "char" or "uint8" => ("uint8", 1),
            "schar" or "int8" => ("int8", 1),
            "int16" or "short" => ("int16", 2),
            "int32" or "int" or "long" => ("int32", 4),
            "uint16" or "ushort" => ("uint16", 2),
            "uint32" or "uint" or "ulong" => ("uint32", 4),
            "single" or "float32" or "float" => ("single", 4),
            "double" or "float64" => ("double", 8),
            _ => throw call.Error("MATLAB:serial:fread:invalidPRECISION", "Invalid PRECISION specified. Type 'instrhelp fread' for more information."),
        };
    }

    private static void SyncNotSupported(DeviceCall call) =>
        JgsBuiltins.Warn(call.Host, "transportlib:legacy:SyncNotSupported", "Setting the mode is not supported. The mode value remains set to 'sync'.");

    /// <summary><c>fprintf(s, cmd)</c>, <c>fprintf(s, format, cmd)</c>, and a trailing mode, which is refused with a warning.</summary>
    public static void Fprintf(ILegacyTransport serial, DeviceCall call)
    {
        TransportClient client = serial.Live(call);
        IReadOnlyList<JgsValue> args = call.Args.Select(TransportClient.Str2Char).ToArray();
        if (args.Count is < 1 or > 3)
        {
            throw call.Error(args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs",
                args.Count < 1 ? "Not enough input arguments." : "Too many input arguments.");
        }

        JgsValue cmd;
        string format;
        switch (args.Count)
        {
            case 1:
                cmd = args[0];
                format = "%s\\n";
                break;
            case 2:
                format = DeviceChecks.IsText(args[0]) ? DeviceChecks.Text(args[0]) : "";
                cmd = args[1];
                if (DeviceChecks.IsText(cmd) && DeviceChecks.Text(cmd).ToLowerInvariant() is "sync" or "async")
                {
                    cmd = args[0];
                    format = "%s\\n";
                    SyncNotSupported(call);
                }

                break;
            default:
                format = DeviceChecks.IsText(args[0]) ? DeviceChecks.Text(args[0]) : "";
                cmd = args[1];
                SyncNotSupported(call);
                break;
        }

        bool isChar = cmd.Type == JgsType.String;
        if (!isChar && DeviceChecks.ClassOf(cmd) != "double")
        {
            throw call.Error("MATLAB:serial:fprintf:invalidCMD", "CMD must be a string or a numeric array.");
        }

        if (isChar && cmd.AsString.Contains("\\n", StringComparison.Ordinal))
        {
            string text = Sprintf(call, cmd.AsString);
            text = client.WriteTerminator.Word switch
            {
                "LF" => text,
                "CR" => text.Replace('\n', '\r'),
                "CR/LF" => text.Replace("\n", "\r\n", StringComparison.Ordinal),
                string other => text.Replace("\n", other, StringComparison.Ordinal),
            };
            cmd = JgsValue.Str(text);
        }

        string formatted = Sprintf(call, format, cmd);
        if (format.EndsWith("\\n", StringComparison.Ordinal))
        {
            serial.LegacyWriteLine(SerialportObject.Retarget(call, [JgsValue.Str(formatted[..^1])]));
        }
        else
        {
            serial.LegacyWrite(SerialportObject.Retarget(call, [JgsValue.Str(formatted), JgsValue.Str("char")]));
        }
    }

    /// <summary><c>fwrite(s, data)</c>, <c>(s, data, precision)</c>, and a trailing mode.</summary>
    public static void Fwrite(ILegacyTransport serial, DeviceCall call)
    {
        TransportClient client = serial.Live(call);
        IReadOnlyList<JgsValue> args = call.Args.Select(TransportClient.Str2Char).ToArray();
        if (args.Count is < 1 or > 3)
        {
            throw call.Error(args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs",
                args.Count < 1 ? "Not enough input arguments." : "Too many input arguments.");
        }

        JgsValue data = args[0];
        JgsValue precision = JgsValue.Str("uint8");
        if (args.Count == 2)
        {
            precision = args[1];
            if (DeviceChecks.IsText(precision) && DeviceChecks.Text(precision).ToLowerInvariant() is "sync" or "async")
            {
                SyncNotSupported(call);
                precision = JgsValue.Str("uint8");
            }
        }
        else if (args.Count == 3)
        {
            precision = args[1];
            SyncNotSupported(call);
        }

        if (DeviceChecks.ClassOf(data) is not ("char" or "double" or "single" or "int8" or "int16" or "int32" or "int64"
            or "uint8" or "uint16" or "uint32" or "uint64"))
        {
            throw call.Error("instrument:fwrite:invalidA", "A must be a numeric array or a string.");
        }

        (string name, _) = LegacyPrecision(precision, call);
        serial.LegacyWrite(SerialportObject.Retarget(call, [data, JgsValue.Str(name)]));
    }

    /// <summary><c>[data, count, msg] = fread(s, size, precision)</c>: a column (or the size's matrix) of doubles.</summary>
    public static JgsValue[] Fread(ILegacyTransport serial, DeviceCall call)
    {
        TransportClient client = serial.Live(call);
        IReadOnlyList<JgsValue> args = call.Args.Select(TransportClient.Str2Char).ToArray();
        double bufferSize = DeviceChecks.Numbers(serial.GetProperty("InputBufferSize", call)).First();
        JgsValue countArg = args.Count >= 1 ? args[0] : JgsValue.Number(bufferSize);
        JgsValue precisionArg = args.Count >= 2 ? args[1] : JgsValue.Str("uint8");
        if (args.Count > 2)
        {
            throw call.Error("MATLAB:serial:fread:invalidSyntaxArgv", "Invalid syntax. Type 'instrhelp fread' for more information.");
        }

        (string precision, int size) = LegacyPrecision(precisionArg, call);
        double[] shape = DeviceChecks.Numbers(countArg).ToArray();
        (int rows, int cols) = shape.Length switch
        {
            1 => ((int)shape[0], 1),
            2 => ((int)shape[0], (int)shape[1]),
            _ => throw call.Error("MATLAB:serial:fread:invalidSIZE", "SIZE must be a scalar or a two-element vector."),
        };
        int count = rows * cols;
        if ((double)size * count > bufferSize)
        {
            throw call.Error("instrument:fread:opfailed", "\"SIZE * PRECISION must be less than or equal to InputBufferSize.\"");
        }

        JgsValue data = serial.LegacyRead(SerialportObject.Retarget(call, [JgsValue.Number(count), JgsValue.Str(precision)]));
        string warning = "";
        if (DeviceChecks.Count(data) == 0)
        {
            warning = call.Host.Warnings.LastMessage;
        }
        else
        {
            double[] values = DeviceChecks.Numbers(data).ToArray();
            data = rows > 1 && cols > 1 && values.Length == count
                ? JgsMatrix.FromColumnMajor(values, rows, cols)
                : JgsMatrix.FromColumnMajor(values, values.Length, 1);
        }

        return [data, JgsValue.Number(count), JgsValue.Str(warning)];
    }

    /// <summary><c>[tline, count, msg] = fgetl(s)</c> and <c>fgets(s)</c> (which keeps the terminator).</summary>
    public static JgsValue[] Fgetl(ILegacyTransport serial, DeviceCall call, bool keepTerminator)
    {
        TransportClient client = serial.Live(call);
        JgsValue line = serial.LegacyReadLine(SerialportObject.Retarget(call, []));
        if (DeviceChecks.Count(line) == 0 || !line.IsStringArray)
        {
            return [JgsValue.Str(""), JgsValue.Number(0), JgsValue.Str(call.Host.Warnings.LastMessage)];
        }

        string text = line.ElementAt(0).AsString;
        string terminator = TerminatorText(client.ReadTerminator);
        if (keepTerminator)
        {
            text += terminator;
            return [JgsValue.Str(text), JgsValue.Number(text.Length), JgsValue.Str("")];
        }

        return [JgsValue.Str(text), JgsValue.Number(text.Length + terminator.Length), JgsValue.Str("")];
    }

    private static string TerminatorText(TerminatorSpec terminator) =>
        string.Concat(terminator.Bytes.Select(static b => (char)b));

    /// <summary><c>[A, count, msg] = fscanf(s)</c>, <c>(s, format)</c>, <c>(s, format, size)</c>.</summary>
    public static JgsValue[] Fscanf(ILegacyTransport serial, DeviceCall call)
    {
        TransportClient client = serial.Live(call);
        IReadOnlyList<JgsValue> args = call.Args.Select(TransportClient.Str2Char).ToArray();
        if (args.Count > 2)
        {
            throw call.Error("MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        string format = args.Count >= 1 && DeviceChecks.IsText(args[0]) ? DeviceChecks.Text(args[0]) : "%c";
        JgsValue? size = args.Count == 2 ? args[1] : null;
        string data = ScanSource(serial, client, call, size is not null);
        JgsValue[] scanned = args.Count == 2
            ? Scan(call, data, format, size!)
            : Scan(call, data, format, null);
        JgsValue result = scanned[0];
        double count = scanned.Length > 1 ? scanned[1].AsNumber : 0;
        string error = scanned.Length > 2 && DeviceChecks.IsText(scanned[2]) ? DeviceChecks.Text(scanned[2]) : "";
        if (error.Length > 0 && DeviceChecks.Count(result) == 0)
        {
            result = JgsValue.Str(data);
            count = data.Length;
        }

        return [result, JgsValue.Number(count), JgsValue.Str("")];
    }

    /// <summary>What fscanf scans: a line with its terminator put back, or else what arrives in the timeout.</summary>
    private static string ScanSource(ILegacyTransport serial, TransportClient client, DeviceCall call, bool counted)
    {
        JgsWarningState warnings = call.Host.Warnings;
        string lineWarning = client.Spec.TranslateWarning("ReadlineWarning");
        bool lineOn = warnings.IsOn(lineWarning);
        JgsValue timeout = client.Timeout;
        warnings.Set(lineWarning, false);
        try
        {
            if (counted)
            {
                client.Timeout = JgsValue.Number(0.1);
            }

            JgsValue line = serial.LegacyReadLine(SerialportObject.Retarget(call, []));
            if (line.IsStringArray && DeviceChecks.Count(line) > 0)
            {
                return line.ElementAt(0).AsString + TerminatorText(client.ReadTerminator);
            }

            double bufferSize = DeviceChecks.Numbers(serial.GetProperty("InputBufferSize", call)).First();
            JgsValue rest = serial.LegacyRead(SerialportObject.Retarget(call, [JgsValue.Number(bufferSize), JgsValue.Str("char")]));
            return rest.Type == JgsType.String ? rest.AsString : "";
        }
        finally
        {
            client.Timeout = timeout;
            warnings.Set(lineWarning, lineOn);
        }
    }

    /// <summary><c>[A, count, msg] = scanstr(s)</c>, <c>(s, delimiter)</c>, <c>(s, delimiter, format)</c>.</summary>
    public static JgsValue[] Scanstr(ILegacyTransport serial, DeviceCall call)
    {
        TransportClient client = serial.Live(call);
        IReadOnlyList<JgsValue> args = call.Args.Select(TransportClient.Str2Char).ToArray();
        string delimiters = args.Count >= 1 && args[0].Type == JgsType.String ? args[0].AsString : ",;";
        if (args.Count >= 1 && args[0].Type != JgsType.String)
        {
            throw call.Error("instrument:scanstr:invalidDELIMITER", "DELIMITER must be a string.");
        }

        string data = ScanSource(serial, client, call, counted: false);
        if (data.Length == 0)
        {
            return [JgsValue.Str(""), JgsValue.Number(0), JgsValue.Str("")];
        }

        string[] pieces = data.Split(delimiters.ToCharArray()).Select(static p => p.Trim()).Where(static p => p.Length > 0).ToArray();
        var cells = new JgsValue[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            cells[i] = args.Count < 2 && double.TryParse(pieces[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number)
                ? JgsValue.Number(number)
                : JgsValue.Str(pieces[i]);
        }

        JgsValue column = JgsValue.Cell(cells);
        column.Reshape(cells.Length, cells.Length == 0 ? 0 : 1);
        return [column, JgsValue.Number(data.Length), JgsValue.Str("")];
    }

    /// <summary><c>[out, count, msg] = query(s, cmd)</c>, <c>(s, cmd, wformat, rformat)</c>.</summary>
    public static JgsValue[] Query(ILegacyTransport serial, DeviceCall call)
    {
        TransportClient client = serial.Live(call);
        IReadOnlyList<JgsValue> args = call.Args.Select(TransportClient.Str2Char).ToArray();
        if (args.Count is < 1 or > 3)
        {
            throw call.Error(args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs",
                args.Count < 1 ? "Not enough input arguments." : "Too many input arguments.");
        }

        if (args[0].Type != JgsType.String)
        {
            throw call.Error("instrument:query:invalidCMD", "CMD must be a string.");
        }

        string rformat = args.Count == 3 && DeviceChecks.IsText(args[2]) ? DeviceChecks.Text(args[2]) : "%c";
        JgsValue answer = serial.LegacyWriteRead(SerialportObject.Retarget(call, [args[0]]));
        string data = DeviceChecks.Text(answer) + TerminatorText(client.ReadTerminator);
        JgsValue[] scanned = Scan(call, data, rformat, null);
        string error = scanned.Length > 2 && DeviceChecks.IsText(scanned[2]) ? DeviceChecks.Text(scanned[2]) : "";
        if (error.Length == 0)
        {
            return [scanned[0], scanned.Length > 1 ? scanned[1] : JgsValue.Number(0), JgsValue.Str("")];
        }

        if (call.Wanted != 3)
        {
            JgsBuiltins.Warn(call.Host, "instrument:query:unsuccessfulRead", $"QUERY was unable to parse the data. {error}");
        }

        return [JgsValue.Str(data), JgsValue.Number(data.Length), JgsValue.Str(error)];
    }

    /// <summary><c>[data, count, msg] = binblockread(s)</c>, <c>(s, precision)</c>: a column, and the block's length with its header.</summary>
    public static JgsValue[] BinblockRead(ILegacyTransport serial, DeviceCall call)
    {
        TransportClient client = serial.Live(call);
        IReadOnlyList<JgsValue> args = call.Args.Select(TransportClient.Str2Char).ToArray();
        (string precision, _) = LegacyPrecision(args.Count >= 1 ? args[0] : JgsValue.Str("uchar"), call);
        JgsValue data = serial.LegacyReadBinblock(SerialportObject.Retarget(call, [JgsValue.Str(precision)]));
        double[] values = DeviceChecks.Numbers(data).ToArray();
        int bytes = values.Length;
        static int Digits(double n) => (int)Math.Ceiling(Math.Log10(Math.Max(n + 1, 1)));
        int digits = Digits(bytes);
        int header = 1 + Digits(digits) + digits;
        JgsValue column = values.Length == 0 ? data : JgsMatrix.FromColumnMajor(values, values.Length, 1);
        return [column, JgsValue.Number(bytes + header), JgsValue.Str("")];
    }

    /// <summary><c>binblockwrite(s, data)</c>, <c>(s, data, precision)</c>, <c>(s, data, precision, header)</c>.</summary>
    public static void BinblockWrite(ILegacyTransport serial, DeviceCall call)
    {
        TransportClient client = serial.Live(call);
        IReadOnlyList<JgsValue> args = call.Args.Select(TransportClient.Str2Char).ToArray();
        if (args.Count is < 1 or > 4)
        {
            throw call.Error(args.Count < 1 ? "MATLAB:narginchk:notEnoughInputs" : "MATLAB:narginchk:tooManyInputs",
                args.Count < 1 ? "Not enough input arguments." : "Too many input arguments.");
        }

        JgsValue data = args[0];
        string precision = "uint8";
        string header = "";
        if (args.Count == 2)
        {
            try
            {
                precision = LegacyPrecision(args[1], call).Precision;
            }
            catch (JgsRuntimeException)
            {
                header = DeviceChecks.IsText(args[1]) ? DeviceChecks.Text(args[1]) : "";
            }
        }
        else if (args.Count >= 3)
        {
            precision = LegacyPrecision(args[1], call).Precision;
            header = DeviceChecks.IsText(args[2]) ? DeviceChecks.Text(args[2]) : "";
            if (args.Count == 4)
            {
                JgsBuiltins.Warn(call.Host, "transportlib:legacy:HeaderFormatNotSupported",
                    "Setting the header format is not supported. The specified header format value is ignored.");
            }
        }

        serial.LegacyWriteBinblock(SerialportObject.Retarget(call, [data, JgsValue.Str(precision), JgsValue.Str(header)]));
    }

    // --- the text functions the mixins call ----------------------------------------------------------------

    private static string Sprintf(DeviceCall call, string format, params JgsValue[] values)
    {
        JgsValue answer = CallBuiltin(call, "sprintf", [JgsValue.Str(format), .. values], 1)[0];
        return DeviceChecks.IsText(answer) ? DeviceChecks.Text(answer) : "";
    }

    private static JgsValue[] Scan(DeviceCall call, string data, string format, JgsValue? size) =>
        CallBuiltin(call, "sscanf", size is null ? [JgsValue.Str(data), JgsValue.Str(format)] : [JgsValue.Str(data), JgsValue.Str(format), size], 3);

    private static JgsValue[] CallBuiltin(DeviceCall call, string name, JgsValue[] args, int wanted)
    {
        if (!call.Interpreter.Globals.Builtins.TryGet(name, out JgsValue function) || function.Type != JgsType.Function)
        {
            throw call.Error("MATLAB:UndefinedFunction", $"Unrecognized function or variable '{name}'.");
        }

        IJgsCallable callable = function.AsCallable;
        return callable is IJgsMultiCallable multi
            ? multi.CallMultiple(args, wanted, call.Line, call.Column)
            : [callable.Call(args, call.Line, call.Column)];
    }
}
