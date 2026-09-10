using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The framing, modulation, quantisation and unit-conversion names of the Signal Processing
/// Toolbox: <c>buffer</c>, <c>framesig</c>, <c>datawrap</c>, <c>seqperiod</c>, <c>shiftdata</c> and
/// its inverse, <c>uencode</c>/<c>udecode</c>, <c>modulate</c>/<c>demod</c>, <c>marcumq</c>, and the
/// four decibel conversions (M132).
/// </summary>
/// <remarks>
/// <para>
/// What these have in common is that they are about the arrangement of a signal rather than about
/// its content: where the frames start, which dimension counts as time, how many bits a sample gets,
/// and which of the two decibel scales a number is on. Each is small; together they are most of what
/// a script does between one transform and the next.
/// </para>
/// <para>
/// <c>shiftdata</c> and <c>unshiftdata</c> are the pair that makes the rest of the toolbox work
/// along an arbitrary dimension: the first brings the wanted dimension to the front and remembers
/// how, and the second puts it back. They are written in terms of the same permutation the
/// interpreter's own <c>permute</c> and <c>shiftdim</c> use, so a script that unshifts what it
/// shifted gets exactly what it started with.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the framing, modulation and conversion names.</summary>
    internal static void RegisterSignalFramingBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("db2mag", (args, line, col) =>
            SignalMap("db2mag", args, line, col, SignalConversions.DecibelsToMagnitude));
        Define("db2pow", (args, line, col) =>
            SignalMap("db2pow", args, line, col, SignalConversions.DecibelsToPower));
        Define("mag2db", (args, line, col) =>
            SignalMap("mag2db", args, line, col, SignalConversions.MagnitudeToDecibels));
        Define("pow2db", (args, line, col) =>
        {
            Arity("pow2db", args, 1, line, col);
            foreach (double v in ToDoubles("pow2db", args[0], line, col))
            {
                if (v < 0)
                {
                    throw new JgsRuntimeException(line, col, "pow2db needs a power that is not negative.");
                }
            }

            return SignalMap("pow2db", args, line, col, SignalConversions.PowerToDecibels);
        });

        Define("buffer", (args, line, col) => BufferSignal(args, 1, line, col)[0],
            (args, wanted, line, col) => BufferSignal(args, wanted, line, col));
        Define("datawrap", WrapData);
        Define("seqperiod", (args, line, col) => SequencePeriod(args, 1, line, col)[0],
            (args, wanted, line, col) => SequencePeriod(args, wanted, line, col));
        Define("shiftdata", (args, line, col) => ShiftData(args, 1, line, col)[0],
            (args, wanted, line, col) => ShiftData(args, wanted, line, col));
        Define("unshiftdata", UnshiftData);
        Define("uencode", EncodeSamples);
        Define("udecode", DecodeSamples);
        Define("marcumq", MarcumFunction);
        Define("modulate", (args, line, col) => ModulateSignal(args, 1, line, col)[0],
            (args, wanted, line, col) => ModulateSignal(args, wanted, line, col));
        Define("demod", (args, line, col) => DemodulateSignal(args, 1, line, col)[0],
            (args, wanted, line, col) => DemodulateSignal(args, wanted, line, col));
        Define("framesig", (args, line, col) => FrameSignal(args, 1, line, col)[0],
            (args, wanted, line, col) => FrameSignal(args, wanted, line, col));
    }

    /// <summary>One elementwise conversion over an argument of any shape.</summary>
    private static JgsValue SignalMap(
        string name, IReadOnlyList<JgsValue> args, int line, int col, Func<double, double> body)
    {
        Arity(name, args, 1, line, col);
        double[] values = ToDoubles(name, args[0], line, col);
        var y = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            y[i] = body(values[i]);
        }

        return SignalShaped(args[0], y);
    }

    /// <summary>Whether an argument is a row of samples rather than a column or a matrix.</summary>
    private static bool SignalIsRow(JgsValue value)
    {
        int[] dims = SizeDims(value);
        return dims.Length == 2 && dims[0] == 1 && dims[1] != 1;
    }

    /// <summary><c>buffer(x, n, p, opt)</c> in all its output counts.</summary>
    private static JgsValue[] BufferSignal(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("buffer", args, 2, 4, line, col);
        double[] x = ToDoubles("buffer", args[0], line, col);
        int[] dims = SizeDims(args[0]);
        if (dims.Length > 2 || (dims.Length == 2 && dims[0] != 1 && dims[1] != 1))
        {
            throw new JgsRuntimeException(line, col, "buffer reads one vector of samples.");
        }

        int n = Count("buffer", args, 1, line, col);
        int p = args.Count >= 3 && !IsEmptyValue(args[2]) ? Count("buffer", args, 2, line, col) : 0;
        double[]? option = null;
        bool nodelay = false;
        if (args.Count >= 4 && !IsEmptyValue(args[3]))
        {
            if (args[3].Type == JgsType.String)
            {
                string word = args[3].AsString;
                if (!word.Equals("nodelay", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"buffer: the only word it takes for its option is 'nodelay', not '{word}'.");
                }

                nodelay = true;
            }
            else
            {
                option = ToDoubles("buffer", args[3], line, col);
            }
        }

        SignalFraming.Buffered framed;
        try
        {
            framed = SignalFraming.Buffer(x, n, p, option, nodelay, wanted >= 2);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"buffer: {ex.Message}");
        }

        JgsValue y = JgsMatrix.FromColumnMajor(framed.Frames, n, framed.Columns);
        if (wanted <= 1)
        {
            return [y];
        }

        bool row = SignalIsRow(args[0]);
        JgsValue leftover = row
            ? JgsMatrix.FromColumnMajor(framed.Leftover, 1, framed.Leftover.Length)
            : JgsMatrix.FromColumnMajor(framed.Leftover, framed.Leftover.Length, 1);
        if (wanted == 2)
        {
            return [y, leftover];
        }

        return [y, leftover, JgsMatrix.FromColumnMajor(framed.Carry, framed.Carry.Length, 1)];
    }

    /// <summary><c>datawrap(x, nfft)</c>.</summary>
    private static JgsValue WrapData(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("datawrap", args, 2, line, col);
        int[] dims = SizeDims(args[0]);
        if (dims.Length > 2 || (dims.Length == 2 && dims[0] != 1 && dims[1] != 1))
        {
            throw new JgsRuntimeException(line, col, "datawrap reads one vector of samples.");
        }

        double[] x = ToDoubles("datawrap", args[0], line, col);
        int nfft = Count("datawrap", args, 1, line, col);
        double[] y = SignalFraming.Wrap(x, nfft);
        return SignalIsRow(args[0])
            ? JgsMatrix.FromColumnMajor(y, 1, y.Length)
            : JgsMatrix.FromColumnMajor(y, y.Length, 1);
    }

    /// <summary><c>seqperiod(x)</c> and <c>seqperiod(x, tol)</c>, down each column.</summary>
    private static JgsValue[] SequencePeriod(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("seqperiod", args, 1, 2, line, col);
        double tolerance = args.Count >= 2 && !IsEmptyValue(args[1])
            ? Num("seqperiod", args, 1, line, col)
            : 1e-10;
        double[] flat = ToDoubles("seqperiod", args[0], line, col);
        int[] dims = SizeDims(args[0]);
        int dim = JgsMatrix.DefaultDim(dims);
        (double[][] slices, _) = JgsMatrix.SlicesAlong(flat, dims, dim);
        int along = dim <= dims.Length ? dims[dim - 1] : 1;

        var periods = new double[slices.Length];
        var repeats = new double[slices.Length];
        for (int s = 0; s < slices.Length; s++)
        {
            periods[s] = SignalFraming.Period(slices[s], tolerance);
            repeats[s] = along / periods[s];
        }

        int[] shape = JgsMatrix.ShapeAlong(dims, dim, 1);
        JgsValue first = JgsMatrix.FromColumnMajorDims(periods, shape);
        return wanted <= 1 ? [first] : [first, JgsMatrix.FromColumnMajorDims(repeats, shape)];
    }

    /// <summary><c>shiftdata(x, dim)</c>: the wanted dimension brought to the front.</summary>
    private static JgsValue[] ShiftData(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("shiftdata", args, 1, 2, line, col);
        if (args.Count < 2 || IsEmptyValue(args[1]))
        {
            JgsValue[] shifted = ShiftDimensions([args[0]], 2, line, col);
            JgsValue moved = shifted[0];
            JgsValue count = shifted.Length > 1 ? shifted[1] : JgsValue.Number(0);
            return wanted switch
            {
                <= 1 => [moved],
                2 => [moved, JgsValue.Array([])],
                _ => [moved, JgsValue.Array([]), count],
            };
        }

        int dim = Count("shiftdata", args, 1, line, col);
        int rank = System.Math.Max(SizeDims(args[0]).Length, dim);
        var order = new double[rank];
        order[0] = dim;
        int at = 1;
        for (int d = 1; d <= rank; d++)
        {
            if (d != dim)
            {
                order[at++] = d;
            }
        }

        JgsValue permuted = Permuted("shiftdata", args[0], order, line, col);
        return wanted switch
        {
            <= 1 => [permuted],
            2 => [permuted, JgsMatrix.FromColumnMajor(order, 1, order.Length)],
            _ => [permuted, JgsMatrix.FromColumnMajor(order, 1, order.Length), JgsValue.Array([])],
        };
    }

    /// <summary><c>unshiftdata(x, perm, nshifts)</c>: the dimension put back where it was.</summary>
    private static JgsValue UnshiftData(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("unshiftdata", args, 3, line, col);
        if (!IsEmptyValue(args[1]))
        {
            double[] order = ToDoubles("unshiftdata", args[1], line, col);
            var inverse = new double[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                int target = (int)order[i];
                if (target < 1 || target > order.Length)
                {
                    throw new JgsRuntimeException(line, col,
                        "unshiftdata's permutation uses each dimension exactly once.");
                }

                inverse[target - 1] = i + 1;
            }

            return Permuted("unshiftdata", args[0], inverse, line, col);
        }

        int shifts = IsEmptyValue(args[2]) ? 0 : Count("unshiftdata", args, 2, line, col);
        return ShiftDimensions([args[0], JgsValue.Number(-shifts)], 1, line, col)[0];
    }

    /// <summary><c>uencode(u, n, v, sign)</c>.</summary>
    private static JgsValue EncodeSamples(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("uencode", args, 2, 4, line, col);
        double[] u = ToDoubles("uencode", args[0], line, col);
        int bits = Count("uencode", args, 1, line, col);
        double peak = args.Count >= 3 && !IsEmptyValue(args[2]) ? Num("uencode", args, 2, line, col) : 1.0;
        bool signed = false;
        if (args.Count >= 4 && !IsEmptyValue(args[3]))
        {
            string word = Str("uencode", args, 3, line, col);
            signed = "signed".StartsWith(word, StringComparison.OrdinalIgnoreCase);
            if (!signed && !"unsigned".StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    $"uencode's code is 'signed' or 'unsigned', not '{word}'.");
            }
        }

        var y = new double[u.Length];
        try
        {
            for (int i = 0; i < u.Length; i++)
            {
                y[i] = SignalConversions.Encode(u[i], bits, peak, signed);
            }
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"uencode: {ex.Message}");
        }

        JgsNumericClass numericClass = bits switch
        {
            <= 8 => signed ? JgsNumericClass.Int8 : JgsNumericClass.UInt8,
            <= 16 => signed ? JgsNumericClass.Int16 : JgsNumericClass.UInt16,
            _ => signed ? JgsNumericClass.Int32 : JgsNumericClass.UInt32,
        };

        return JgsNumericClasses.Stamp(SignalShaped(args[0], y), numericClass);
    }

    /// <summary><c>udecode(u, n, v, overflow)</c>.</summary>
    private static JgsValue DecodeSamples(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("udecode", args, 2, 4, line, col);
        JgsNumericClass given = args[0].NumericClass;
        if (given is not (JgsNumericClass.Int8 or JgsNumericClass.Int16 or JgsNumericClass.Int32
            or JgsNumericClass.UInt8 or JgsNumericClass.UInt16 or JgsNumericClass.UInt32))
        {
            throw new JgsRuntimeException(line, col,
                "udecode reads samples stored as int8, int16, int32, uint8, uint16 or uint32.");
        }

        bool signed = given is JgsNumericClass.Int8 or JgsNumericClass.Int16 or JgsNumericClass.Int32;
        double[] u = ToDoubles("udecode", args[0], line, col);
        int bits = Count("udecode", args, 1, line, col);
        double peak = args.Count >= 3 && !IsEmptyValue(args[2]) ? Num("udecode", args, 2, line, col) : 1.0;
        bool saturate = true;
        if (args.Count >= 4 && !IsEmptyValue(args[3]))
        {
            string word = Str("udecode", args, 3, line, col);
            saturate = "saturate".StartsWith(word, StringComparison.OrdinalIgnoreCase);
            if (!saturate && !"wrap".StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    $"udecode's overflow rule is 'saturate' or 'wrap', not '{word}'.");
            }
        }

        var y = new double[u.Length];
        try
        {
            for (int i = 0; i < u.Length; i++)
            {
                y[i] = SignalConversions.Decode(u[i], bits, peak, signed, saturate);
            }
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"udecode: {ex.Message}");
        }

        return SignalShaped(args[0], y);
    }

    /// <summary><c>marcumq(a, b)</c> and <c>marcumq(a, b, m)</c>, broadcast over its arguments.</summary>
    private static JgsValue MarcumFunction(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("marcumq", args, 2, 3, line, col);
        double[] a = ToDoubles("marcumq", args[0], line, col);
        double[] b = ToDoubles("marcumq", args[1], line, col);
        double[] m = args.Count >= 3 ? ToDoubles("marcumq", args[2], line, col) : [1.0];

        int count = System.Math.Max(a.Length, System.Math.Max(b.Length, m.Length));
        foreach (int length in new[] { a.Length, b.Length, m.Length })
        {
            if (length != 1 && length != count)
            {
                throw new JgsRuntimeException(line, col,
                    "marcumq's arguments are the same size, or single numbers.");
            }
        }

        JgsValue template = a.Length == count ? args[0] : b.Length == count ? args[1] : args[^1];
        var y = new double[count];
        try
        {
            for (int i = 0; i < count; i++)
            {
                y[i] = SignalConversions.MarcumQ(
                    a.Length == 1 ? a[0] : a[i],
                    b.Length == 1 ? b[0] : b[i],
                    m.Length == 1 ? m[0] : m[i]);
            }
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"marcumq: {ex.Message}");
        }

        return SignalShaped(template, y);
    }

    /// <summary>The rows, columns and orientation the modulation names read their signal at.</summary>
    private static (double[] Flat, int Rows, int Columns, bool WasRow) SignalAsColumns(
        string name, JgsValue value, int line, int col)
    {
        double[] flat = ToDoubles(name, value, line, col);
        int[] dims = SizeDims(value);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int columns = dims.Length > 1 ? dims[1] : 1;
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} reads a vector or a matrix of signals.");
        }

        if (rows == 1 && columns != 1)
        {
            return (flat, columns, 1, true);
        }

        return (flat, rows, columns, false);
    }

    /// <summary><c>modulate(x, fc, fs, method, opt)</c>.</summary>
    private static JgsValue[] ModulateSignal(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("modulate", args, 3, 5, line, col);
        (double[] x, int rows, int columns, bool wasRow) =
            SignalAsColumns("modulate", args[0], line, col);
        double carrier = Num("modulate", args, 1, line, col);
        double rate = Num("modulate", args, 2, line, col);
        string method = args.Count >= 4 && !IsEmptyValue(args[3])
            ? Str("modulate", args, 3, line, col).ToLowerInvariant()
            : "am";

        double? option = null;
        string? word = null;
        JgsValue? second = null;
        if (args.Count >= 5 && !IsEmptyValue(args[4]))
        {
            if (args[4].Type == JgsType.String)
            {
                word = args[4].AsString;
            }
            else if (method == "qam")
            {
                second = args[4];
            }
            else
            {
                option = Num("modulate", args, 4, line, col);
            }
        }

        try
        {
            (double[] y, double[] time, int outRows) = method == "qam"
                ? SignalModulation.ModulateQuadrature(
                    x,
                    second is null
                        ? throw new JgsRuntimeException(line, col, "modulate's 'qam' needs a second message.")
                        : SignalAsColumns("modulate", second, line, col).Flat,
                    rows, columns, carrier, rate)
                : SignalModulation.Modulate(x, rows, columns, carrier, rate, method, option, word);

            JgsValue signal = wasRow
                ? JgsMatrix.FromColumnMajor(y, 1, outRows)
                : JgsMatrix.FromColumnMajor(y, outRows, columns);
            if (wanted <= 1)
            {
                return [signal];
            }

            JgsValue axis = wasRow
                ? JgsMatrix.FromColumnMajor(time, 1, time.Length)
                : JgsMatrix.FromColumnMajor(time, time.Length, 1);
            return [signal, axis];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"modulate: {ex.Message}");
        }
    }

    /// <summary><c>demod(y, fc, fs, method, opt)</c>.</summary>
    private static JgsValue[] DemodulateSignal(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("demod", args, 3, 5, line, col);
        (double[] y, int rows, int columns, bool wasRow) = SignalAsColumns("demod", args[0], line, col);
        double carrier = Num("demod", args, 1, line, col);
        double rate = Num("demod", args, 2, line, col);
        string method = args.Count >= 4 && !IsEmptyValue(args[3])
            ? Str("demod", args, 3, line, col).ToLowerInvariant()
            : "am";

        double? option = null;
        string? word = null;
        if (args.Count >= 5 && !IsEmptyValue(args[4]))
        {
            if (args[4].Type == JgsType.String)
            {
                word = args[4].AsString;
            }
            else
            {
                option = Num("demod", args, 4, line, col);
            }
        }

        try
        {
            (double[] message, double[] second, int outRows) =
                SignalModulation.Demodulate(y, rows, columns, carrier, rate, method, option, word);
            JgsValue first = wasRow
                ? JgsMatrix.FromColumnMajor(message, 1, outRows)
                : JgsMatrix.FromColumnMajor(message, outRows, columns);
            if (wanted <= 1)
            {
                return [first];
            }

            JgsValue other = second.Length == 0
                ? JgsValue.Array([])
                : wasRow
                    ? JgsMatrix.FromColumnMajor(second, 1, outRows)
                    : JgsMatrix.FromColumnMajor(second, outRows, columns);
            return [first, other];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"demod: {ex.Message}");
        }
    }

    /// <summary><c>framesig(x, fl, ...)</c> with its name-value options.</summary>
    private static JgsValue[] FrameSignal(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "framesig needs a signal and a frame length.");
        }

        int frameLength = Count("framesig", args, 1, line, col);
        double[]? window = null;
        double[]? initial = null;
        int initialRows = 0;
        int initialIndex = 1;
        bool zeroPad = false;
        int lap = 0;
        bool lapGiven = false;
        int dimension = 0;

        for (int i = 2; i + 1 < args.Count; i += 2)
        {
            string option = Str("framesig", args, i, line, col);
            JgsValue value = args[i + 1];
            switch (option.ToLowerInvariant())
            {
                case "window":
                    window = ToDoubles("framesig", value, line, col);
                    break;
                case "overlaplength":
                    lap = Count("framesig", args, i + 1, line, col);
                    lapGiven = true;
                    break;
                case "underlaplength":
                    if (lapGiven)
                    {
                        throw new JgsRuntimeException(line, col,
                            "framesig takes an overlap or an underlap, not both.");
                    }

                    lap = -Count("framesig", args, i + 1, line, col);
                    lapGiven = true;
                    break;
                case "initialcondition":
                    initial = ToDoubles("framesig", value, line, col);
                    initialRows = SizeDims(value) is { Length: > 0 } d ? d[0] : initial.Length;
                    break;
                case "initialindex":
                    initialIndex = Count("framesig", args, i + 1, line, col);
                    break;
                case "incompleteframerule":
                {
                    string rule = Str("framesig", args, i + 1, line, col);
                    zeroPad = rule.Equals("zeropad", StringComparison.OrdinalIgnoreCase);
                    if (!zeroPad && !rule.Equals("drop", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new JgsRuntimeException(line, col,
                            $"framesig's incomplete-frame rule is 'drop' or 'zeropad', not '{rule}'.");
                    }

                    break;
                }

                case "dimension":
                    dimension = Count("framesig", args, i + 1, line, col);
                    break;
                default:
                    throw new JgsRuntimeException(line, col, $"framesig has no option called '{option}'.");
            }
        }

        if ((args.Count - 2) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "framesig's options come in name and value pairs.");
        }

        JgsValue signal = args[0];
        double[] order = [];
        if (dimension > 0)
        {
            int rank = System.Math.Max(SizeDims(signal).Length, dimension);
            order = new double[rank];
            order[0] = dimension;
            int at = 1;
            for (int d = 1; d <= rank; d++)
            {
                if (d != dimension)
                {
                    order[at++] = d;
                }
            }

            signal = Permuted("framesig", signal, order, line, col);
        }

        (double[] x, int rows, int columns, bool wasRow) = SignalAsColumns("framesig", signal, line, col);
        if (window is not null && window.Length != frameLength)
        {
            throw new JgsRuntimeException(line, col,
                $"framesig's window holds {frameLength} sample(s), to match its frame.");
        }

        if (initial is not null && columns > 1 && initialRows * columns != initial.Length)
        {
            throw new JgsRuntimeException(line, col,
                "framesig's initial condition has one column per channel.");
        }

        SignalFraming.Framed framed;
        try
        {
            framed = SignalFraming.Frame(
                x, rows, columns, frameLength, lap, initial, initial is null ? 0 : initialRows,
                initialIndex, window, zeroPad);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"framesig: {ex.Message}");
        }

        JgsValue frames = columns == 1
            ? JgsMatrix.FromColumnMajor(framed.Frames, frameLength, framed.FrameCount)
            : JgsMatrix.FromColumnMajorDims(framed.Frames, [frameLength, framed.FrameCount, columns]);
        if (wanted <= 1)
        {
            return [frames];
        }

        JgsValue carry = framed.FinalConditionLength == 0
            ? JgsMatrix.FromColumnMajor([], 0, columns)
            : wasRow
                ? JgsMatrix.FromColumnMajor(framed.FinalCondition, 1, framed.FinalConditionLength)
                : JgsMatrix.FromColumnMajor(framed.FinalCondition, framed.FinalConditionLength, columns);
        return wanted == 2
            ? [frames, carry]
            : [frames, carry, JgsValue.Number(framed.FinalIndex)];
    }
}
