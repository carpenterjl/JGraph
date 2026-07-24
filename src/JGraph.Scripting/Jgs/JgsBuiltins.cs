using System.Globalization;
using System.IO;
using System.Numerics;
using JGraph.Api;
using JGraph.Imaging;
using JGraph.Numerics;
using JGraph.Signal;
using JGraph.Signal.Rf;
using JGraph.Core.Model;
using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Builds the global environment a JGS script runs in: the built-in functions. They mirror the JGraph
/// functional API — data helpers (<c>linspace</c>, <c>range</c>, element-wise math, reductions), table
/// readers, and the plotting verbs (<c>plot</c>, <c>title</c>, <c>legend</c>, <c>show</c>, …) — bridging to
/// the static <see cref="JG"/> facade and the host's <see cref="JGraphScriptGlobals"/>. This is the only IO
/// surface a JGS script has: there is no file, network, or reflection access beyond the table readers.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Creates the global scope over the run's <paramref name="host"/> helpers, seeded with every built-in.</summary>
    /// <param name="host">The run's host services.</param>
    /// <param name="cancellationToken">The run's cancellation token, so <c>pause(seconds)</c> stays interruptible.</param>
    public static JgsEnvironment CreateGlobals(JGraphScriptGlobals host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        var env = new JgsEnvironment();
        var random = new Random();

        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void Math1(string name, Func<double, double> f) =>
            Define(name, (args, line, col) => { Arity(name, args, 1, line, col); return MapNumeric(name, args[0], f, line, col); });

        // --- Constants -----------------------------------------------------------------------
        env.Declare("pi", JgsValue.Number(System.Math.PI));
        env.Declare("e", JgsValue.Number(System.Math.E));
        env.Declare("inf", JgsValue.Number(double.PositiveInfinity));
        env.Declare("nan", JgsValue.Number(double.NaN));

        // --- Element-wise math ---------------------------------------------------------------
        Math1("sin", System.Math.Sin);
        Math1("cos", System.Math.Cos);
        Math1("tan", System.Math.Tan);
        Math1("asin", System.Math.Asin);
        Math1("acos", System.Math.Acos);
        Math1("atan", System.Math.Atan);
        Math1("exp", System.Math.Exp);
        Math1("log", System.Math.Log);
        Math1("log10", System.Math.Log10);
        Math1("sqrt", System.Math.Sqrt);
        Math1("floor", System.Math.Floor);
        Math1("ceil", System.Math.Ceiling);
        Math1("round", x => System.Math.Round(x, MidpointRounding.AwayFromZero));
        Math1("sign", x => System.Math.Sign(x));

        // Complex-aware elementwise functions: real input behaves exactly as before, and complex
        // input takes the complex definition (abs = magnitude, angle = phase, conj = conjugate).
        void MathC(string name, Func<double, double> real, Func<Complex, JgsValue> complex) =>
            Define(name, (args, line, col) => { Arity(name, args, 1, line, col); return MapComplexAware(name, args[0], real, complex, line, col); });

        MathC("abs", System.Math.Abs, static c => JgsValue.Number(Complex.Abs(c)));
        MathC("real", static x => x, static c => JgsValue.Number(c.Real));
        MathC("imag", static _ => 0, static c => JgsValue.Number(c.Imaginary));
        MathC("angle", static x => x >= 0 ? 0 : System.Math.PI, static c => JgsValue.Number(c.Phase));
        MathC("conj", static x => x, static c => JgsValue.ComplexNum(Complex.Conjugate(c)));

        Define("pow", (args, line, col) =>
        {
            Arity("pow", args, 2, line, col);
            double exponent = Num("pow", args, 1, line, col);
            return MapNumeric("pow", args[0], x => System.Math.Pow(x, exponent), line, col);
        });

        Define("atan2", (args, line, col) =>
        {
            Arity("atan2", args, 2, line, col);
            return Zip("atan2", args[0], args[1], System.Math.Atan2, line, col);
        });

        // --- Array construction --------------------------------------------------------------
        Define("linspace", (args, line, col) =>
        {
            Arity("linspace", args, 3, line, col);
            double start = Num("linspace", args, 0, line, col);
            double stop = Num("linspace", args, 1, line, col);
            int count = Count("linspace", args, 2, line, col);
            if (count < 1)
            {
                throw new JgsRuntimeException(line, col, "linspace needs a count of at least 1.");
            }

            var result = new double[count];
            for (int i = 0; i < count; i++)
            {
                double t = count == 1 ? 0 : (double)i / (count - 1);
                result[i] = start + ((stop - start) * t);
            }

            return Numbers(result);
        });

        Define("range", (args, line, col) =>
        {
            ArityRange("range", args, 2, 3, line, col);
            double start = Num("range", args, 0, line, col);
            double stop = Num("range", args, 1, line, col);
            double step = args.Count == 3 ? Num("range", args, 2, line, col) : 1.0;
            if (step == 0)
            {
                throw new JgsRuntimeException(line, col, "range step must not be zero.");
            }

            var result = new List<double>();
            if (step > 0)
            {
                for (double v = start; v < stop; v += step)
                {
                    result.Add(v);
                }
            }
            else
            {
                for (double v = start; v > stop; v += step)
                {
                    result.Add(v);
                }
            }

            return Numbers(result.ToArray());
        });

        Define("zeros", (args, line, col) => Filled("zeros", args, 0.0, line, col));
        Define("ones", (args, line, col) => Filled("ones", args, 1.0, line, col));

        Define("rand", (args, line, col) =>
        {
            Arity("rand", args, 1, line, col);
            int count = Count("rand", args, 0, line, col);
            var result = new JgsValue[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = JgsValue.Number(random.NextDouble());
            }

            return JgsValue.Array(result);
        });

        // --- DSP and audio -------------------------------------------------------------------
        Define("fft", (args, line, col) =>
        {
            ArityRange("fft", args, 1, 2, line, col);
            Complex[] input = ComplexArray("fft", args, 0, line, col);
            if (args.Count == 2)
            {
                input = PadOrTruncate(input, Count("fft", args, 1, line, col), "fft", line, col);
            }

            return FromComplexArray(JGraph.Signal.Fft.Forward(input));
        });

        Define("ifft", (args, line, col) =>
        {
            ArityRange("ifft", args, 1, 2, line, col);
            Complex[] input = ComplexArray("ifft", args, 0, line, col);
            if (args.Count == 2)
            {
                input = PadOrTruncate(input, Count("ifft", args, 1, line, col), "ifft", line, col);
            }

            return FromComplexArray(JGraph.Signal.Fft.Inverse(input));
        });

        Define("fftshift", (args, line, col) =>
        {
            Arity("fftshift", args, 1, line, col);
            return JgsValue.Array(Rotate(Arr("fftshift", args, 0, line, col), forward: true));
        });

        Define("ifftshift", (args, line, col) =>
        {
            Arity("ifftshift", args, 1, line, col);
            return JgsValue.Array(Rotate(Arr("ifftshift", args, 0, line, col), forward: false));
        });

        Define("filter", (args, line, col) =>
        {
            Arity("filter", args, 3, line, col);
            return Numbers(DigitalFilter.Filter(
                NumericVector("filter", args, 0, line, col),
                NumericVector("filter", args, 1, line, col),
                DoubleArray("filter", args, 2, line, col)));
        });

        Define("freqz", (args, line, col) =>
        {
            ArityRange("freqz", args, 2, 4, line, col);
            int count = args.Count >= 3 ? Count("freqz", args, 2, line, col) : 512;
            double fs = args.Count == 4 ? Num("freqz", args, 3, line, col) : 2; // default: normalized 0..1
            (Complex[] response, double[] frequencies) = DigitalFilter.Freqz(
                NumericVector("freqz", args, 0, line, col),
                NumericVector("freqz", args, 1, line, col),
                count, fs);
            return JgsValue.Array([FromComplexArray(response), Numbers(frequencies)]);
        });

        Define("butter", (args, line, col) =>
        {
            ArityRange("butter", args, 2, 3, line, col);
            int order = Count("butter", args, 0, line, col);
            double[] cutoffs = NumericVector("butter", args, 1, line, col);
            FilterBandType type = args.Count == 3
                ? Str("butter", args, 2, line, col).ToLowerInvariant() switch
                {
                    "low" => FilterBandType.LowPass,
                    "high" => FilterBandType.HighPass,
                    "bandpass" => FilterBandType.BandPass,
                    "stop" => FilterBandType.BandStop,
                    string other => throw new JgsRuntimeException(line, col,
                        $"butter type must be \"low\", \"high\", \"bandpass\", or \"stop\", not \"{other}\"."),
                }
                : cutoffs.Length == 2 ? FilterBandType.BandPass : FilterBandType.LowPass;
            try
            {
                (double[] b, double[] a) = IirDesign.Butterworth(order, cutoffs, type);
                return JgsValue.Array([Numbers(b), Numbers(a)]);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, "butter: " + ex.Message);
            }
        });

        Define("firpm", (args, line, col) =>
        {
            Arity("firpm", args, 3, line, col);
            int order = Count("firpm", args, 0, line, col);
            double[] edges = DoubleArray("firpm", args, 1, line, col);
            double[] amplitudes = DoubleArray("firpm", args, 2, line, col);
            try
            {
                double[] h = FirDesign.Remez(order, edges, amplitudes, out bool converged);
                if (!converged)
                {
                    host.print("firpm: the equiripple exchange did not fully converge; returning the best design found.");
                }

                return Numbers(h);
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, "firpm: " + ex.Message);
            }
        });

        Define("audioread", (args, line, col) =>
        {
            Arity("audioread", args, 1, line, col);
            string path = Str("audioread", args, 0, line, col);
            try
            {
                (double[] samples, int fs) = host.audioread(path);
                return JgsValue.Array([Numbers(samples), JgsValue.Number(fs)]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new JgsRuntimeException(line, col, $"audioread: cannot read '{path}': {ex.Message}");
            }
        });

        Define("sound", (args, line, col) =>
        {
            ArityRange("sound", args, 1, 2, line, col);
            int fs = args.Count == 2 ? Count("sound", args, 1, line, col) : 8192; // MATLAB's default rate
            try
            {
                host.sound(DoubleArray("sound", args, 0, line, col), fs);
            }
            catch (InvalidOperationException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }

            return JgsValue.Null;
        });

        Define("exit", (args, line, col) => Exit("exit", args, line, col));
        Define("quit", (args, line, col) => Exit("quit", args, line, col));

        Define("pause", (args, line, col) =>
        {
            Arity("pause", args, 1, line, col);
            double seconds = Num("pause", args, 0, line, col);
            if (seconds > 0 && !double.IsNaN(seconds))
            {
                // Interruptible: waking on the run's cancellation token keeps Stop responsive.
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(System.Math.Min(seconds, 3600)));
                cancellationToken.ThrowIfCancellationRequested();
            }

            return JgsValue.Null;
        });

        // --- Time & date ---------------------------------------------------------------------
        // A stopwatch handle is the high-resolution tick count taken relative to when these globals were
        // built — small enough to survive a round trip through a JGS number (double) without losing
        // precision. tic starts the default stopwatch and returns a handle; toc reads elapsed seconds
        // from the last bare tic or from a given handle. Dates use MATLAB serial date numbers, which are
        // .NET OLE Automation dates plus a fixed offset (so datenum(1970, 1, 1) == 719529, as in MATLAB).
        long stopwatchBase = System.Diagnostics.Stopwatch.GetTimestamp();
        double stopwatchFrequency = System.Diagnostics.Stopwatch.Frequency;
        double? defaultTicHandle = null;
        const double matlabDatenumOffset = 693960.0;

        double StopwatchTicksNow() => System.Diagnostics.Stopwatch.GetTimestamp() - stopwatchBase;

        double SerialFromComponents(double year, double month, double day, double hour, double minute, double second) =>
            new DateTime((int)System.Math.Clamp(year, 1, 9999), 1, 1)
                .AddMonths((int)month - 1)
                .AddDays(day - 1)
                .AddHours(hour)
                .AddMinutes(minute)
                .AddSeconds(second)
                .ToOADate() + matlabDatenumOffset;

        Define("tic", (args, line, col) =>
        {
            Arity("tic", args, 0, line, col);
            double handle = StopwatchTicksNow();
            defaultTicHandle = handle;
            return JgsValue.Number(handle);
        });

        Define("toc", (args, line, col) =>
        {
            ArityRange("toc", args, 0, 1, line, col);
            double startTicks;
            if (args.Count == 1)
            {
                startTicks = Num("toc", args, 0, line, col);
            }
            else if (defaultTicHandle is double handle)
            {
                startTicks = handle;
            }
            else
            {
                throw new JgsRuntimeException(line, col, "toc: start a timer with tic first.");
            }

            return JgsValue.Number((StopwatchTicksNow() - startTicks) / stopwatchFrequency);
        });

        Define("clock", (args, line, col) =>
        {
            Arity("clock", args, 0, line, col);
            DateTime moment = DateTime.Now;
            return JgsValue.Array(
            [
                JgsValue.Number(moment.Year),
                JgsValue.Number(moment.Month),
                JgsValue.Number(moment.Day),
                JgsValue.Number(moment.Hour),
                JgsValue.Number(moment.Minute),
                JgsValue.Number(moment.Second + (moment.Millisecond / 1000.0)),
            ]);
        });

        Define("now", (args, line, col) =>
        {
            Arity("now", args, 0, line, col);
            return JgsValue.Number(DateTime.Now.ToOADate() + matlabDatenumOffset);
        });

        Define("datenum", (args, line, col) =>
        {
            ArityRange("datenum", args, 1, 6, line, col);
            double[] components;
            if (args.Count == 1 && args[0].Type == JgsType.Array)
            {
                components = ToDoubles("datenum", args[0], line, col);
                if (components.Length is not (3 or 6))
                {
                    throw new JgsRuntimeException(line, col,
                        "datenum: a single vector must have 3 ([year, month, day]) or 6 ([..., hour, minute, second]) elements.");
                }
            }
            else if (args.Count is 3 or 6)
            {
                components = new double[args.Count];
                for (int i = 0; i < args.Count; i++)
                {
                    components[i] = Num("datenum", args, i, line, col);
                }
            }
            else
            {
                throw new JgsRuntimeException(line, col,
                    "datenum expects year, month, day (optionally hour, minute, second), or a single 3- or 6-element vector.");
            }

            double hour = components.Length > 3 ? components[3] : 0;
            double minute = components.Length > 4 ? components[4] : 0;
            double second = components.Length > 5 ? components[5] : 0;
            return JgsValue.Number(SerialFromComponents(components[0], components[1], components[2], hour, minute, second));
        });

        Define("datestr", (args, line, col) =>
        {
            ArityRange("datestr", args, 0, 2, line, col);
            double serial = args.Count >= 1
                ? Num("datestr", args, 0, line, col)
                : DateTime.Now.ToOADate() + matlabDatenumOffset;

            double oaDate = serial - matlabDatenumOffset;
            if (double.IsNaN(oaDate) || oaDate < -657435.0 || oaDate > 2958465.99999999)
            {
                throw new JgsRuntimeException(line, col, "datestr: the serial date number is out of range.");
            }

            DateTime moment = DateTime.FromOADate(oaDate);
            string format = args.Count >= 2 ? Str("datestr", args, 1, line, col) : "dd-MMM-yyyy HH:mm:ss";
            try
            {
                return JgsValue.Str(moment.ToString(format, CultureInfo.InvariantCulture));
            }
            catch (FormatException)
            {
                throw new JgsRuntimeException(line, col, $"datestr: '{format}' is not a valid .NET date format string.");
            }
        });

        Define("datetime", (args, line, col) =>
        {
            Arity("datetime", args, 0, line, col);
            return JgsValue.Str(DateTime.Now.ToString("dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        });

        Define("date", (args, line, col) =>
        {
            Arity("date", args, 0, line, col);
            return JgsValue.Str(DateTime.Now.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture));
        });

        Define("time", (args, line, col) =>
        {
            Arity("time", args, 0, line, col);
            return JgsValue.Number(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
        });

        Define("mod", (args, line, col) =>
        {
            Arity("mod", args, 2, line, col);
            double divisor = Num("mod", args, 1, line, col);
            // MATLAB mod: the result takes the divisor's sign (unlike C's %).
            return MapNumeric("mod", args[0], x => divisor == 0 ? x : x - (System.Math.Floor(x / divisor) * divisor), line, col);
        });

        Define("size", (args, line, col) =>
        {
            ArityRange("size", args, 1, 2, line, col);

            // MATLAB's third dimension: images carry channels, everything else is a plain 2-D value.
            int channels = args[0].Type == JgsType.Image ? args[0].AsImage.Channels : 1;
            (int rows, int cols) = args[0].Type switch
            {
                JgsType.Image => (args[0].AsImage.Height, args[0].AsImage.Width),
                JgsType.Array when args[0].ArrayLength > 0 && args[0].ElementAt(0).Type == JgsType.Array =>
                    (args[0].ArrayLength, args[0].ElementAt(0).ArrayLength),
                JgsType.Array => (1, args[0].ArrayLength),
                JgsType.String => (1, args[0].AsString.Length),
                _ => (1, 1),
            };

            if (args.Count == 2)
            {
                int dim = Count("size", args, 1, line, col);
                // Dimensions past the value's rank are 1, exactly as in MATLAB.
                return JgsValue.Number(dim switch { 1 => rows, 2 => cols, 3 => channels, _ => 1 });
            }

            return channels == 1
                ? JgsValue.Array([JgsValue.Number(rows), JgsValue.Number(cols)])
                : JgsValue.Array([JgsValue.Number(rows), JgsValue.Number(cols), JgsValue.Number(channels)]);
        });

        Define("isempty", (args, line, col) =>
        {
            Arity("isempty", args, 1, line, col);
            return JgsValue.Bool(args[0].Type switch
            {
                JgsType.Null => true,
                JgsType.Array => args[0].ArrayLength == 0,
                JgsType.String => args[0].AsString.Length == 0,
                JgsType.Table => args[0].AsTable.RowCount == 0,
                _ => false,
            });
        });

        Define("disp", (args, line, col) =>
        {
            Arity("disp", args, 1, line, col);
            host.print(args[0].Display());
            return JgsValue.Null;
        });

        // --- RF networks and transmission lines ----------------------------------------------
        // S-parameter networks are carried as tables (freq column, per-pair re/im columns, a
        // constant z0 column) so they flow through the existing table accessors; the math runs on
        // the JGraph.Signal.Rf domain type and converts back.
        Define("sparameters", (args, line, col) =>
        {
            Arity("sparameters", args, 1, line, col);
            string path = host.Resolve(Str("sparameters", args, 0, line, col));
            try
            {
                return JgsValue.Table(NetworkToTable(Touchstone.Read(path), "s"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                throw new JgsRuntimeException(line, col, $"sparameters: cannot read '{path}': {ex.Message}");
            }
        });

        Define("rffreq", (args, line, col) =>
        {
            Arity("rffreq", args, 1, line, col);
            return NumbersCopy(TableSeries.GetNumbers(Tbl("rffreq", args, 0, line, col), "freq"));
        });

        Define("rfparam", (args, line, col) =>
        {
            Arity("rfparam", args, 3, line, col);
            Table table = Tbl("rfparam", args, 0, line, col);
            int i = Count("rfparam", args, 1, line, col);
            int j = Count("rfparam", args, 2, line, col);
            return FromComplexArray(ReadParam(table, i, j, line, col));
        });

        Define("s2z", (args, line, col) => ConvertNetwork("s2z", args, "z", NetworkConversions.SToZ, line, col));
        Define("s2y", (args, line, col) => ConvertNetwork("s2y", args, "y", NetworkConversions.SToY, line, col));
        Define("s2abcd", (args, line, col) => ConvertNetwork("s2abcd", args, "a", NetworkConversions.SToAbcd, line, col));
        Define("z2s", (args, line, col) => ConvertNetwork("z2s", args, "s", NetworkConversions.ZToS, line, col));
        Define("y2s", (args, line, col) => ConvertNetwork("y2s", args, "s", NetworkConversions.YToS, line, col));
        Define("abcd2s", (args, line, col) => ConvertNetwork("abcd2s", args, "s", NetworkConversions.AbcdToS, line, col));

        Define("cascadesparams", (args, line, col) =>
        {
            Arity("cascadesparams", args, 2, line, col);
            SParameterNetwork a = TableToNetwork(Tbl("cascadesparams", args, 0, line, col));
            SParameterNetwork b = TableToNetwork(Tbl("cascadesparams", args, 1, line, col));
            try
            {
                return JgsValue.Table(NetworkToTable(NetworkConversions.Cascade(a, b), "s"));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                throw new JgsRuntimeException(line, col, "cascadesparams: " + ex.Message);
            }
        });

        Define("gammain", (args, line, col) =>
        {
            ArityRange("gammain", args, 1, 2, line, col);
            SParameterNetwork net = TableToNetwork(Tbl("gammain", args, 0, line, col));
            Complex? zl = args.Count == 2 ? ComplexScalar("gammain", args, 1, line, col) : null;
            try
            {
                return FromComplexArray(NetworkConversions.GammaIn(net, zl));
            }
            catch (NotSupportedException ex)
            {
                throw new JgsRuntimeException(line, col, "gammain: " + ex.Message);
            }
        });

        Define("gammaout", (args, line, col) =>
        {
            ArityRange("gammaout", args, 1, 2, line, col);
            SParameterNetwork net = TableToNetwork(Tbl("gammaout", args, 0, line, col));
            Complex? zs = args.Count == 2 ? ComplexScalar("gammaout", args, 1, line, col) : null;
            try
            {
                return FromComplexArray(NetworkConversions.GammaOut(net, zs));
            }
            catch (NotSupportedException ex)
            {
                throw new JgsRuntimeException(line, col, "gammaout: " + ex.Message);
            }
        });

        Define("vswr", (args, line, col) =>
        {
            Arity("vswr", args, 1, line, col);
            return MapComplexAware("vswr", args[0],
                x => (1 + System.Math.Abs(x)) / (1 - System.Math.Abs(x)),
                c => JgsValue.Number((1 + Complex.Abs(c)) / (1 - Complex.Abs(c))), line, col);
        });

        Define("db", (args, line, col) =>
        {
            Arity("db", args, 1, line, col);
            return MapComplexAware("db", args[0],
                x => 20 * System.Math.Log10(System.Math.Abs(x)),
                c => JgsValue.Number(20 * System.Math.Log10(Complex.Abs(c))), line, col);
        });

        Define("rfplot", (args, line, col) => RfPlot(args, line, col));
        Define("smithplot", (args, line, col) => SmithPlot(args, line, col));

        Define("microstrip", (args, line, col) =>
        {
            Arity("microstrip", args, 3, line, col);
            try
            {
                (double z0, double eeff) = TransmissionLine.Microstrip(
                    Num("microstrip", args, 0, line, col),
                    Num("microstrip", args, 1, line, col),
                    Num("microstrip", args, 2, line, col));
                return JgsValue.Array([JgsValue.Number(z0), JgsValue.Number(eeff)]);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new JgsRuntimeException(line, col, "microstrip: " + ex.Message);
            }
        });

        Define("microstripw", (args, line, col) =>
            LineCalc("microstripw", args, line, col, TransmissionLine.MicrostripWidth));
        Define("stripline", (args, line, col) =>
            LineCalc("stripline", args, line, col, TransmissionLine.Stripline));
        Define("striplinew", (args, line, col) =>
            LineCalc("striplinew", args, line, col, TransmissionLine.StriplineWidth));

        Define("wavelength", (args, line, col) =>
        {
            Arity("wavelength", args, 2, line, col);
            double eeff = Num("wavelength", args, 1, line, col);
            return MapNumeric("wavelength", args[0],
                f => TransmissionLine.GuidedWavelength(f, eeff), line, col);
        });

        // --- Reductions and inspection -------------------------------------------------------
        Define("length", (args, line, col) =>
        {
            Arity("length", args, 1, line, col);
            return args[0].Type switch
            {
                JgsType.Array => JgsValue.Number(args[0].ArrayLength),
                JgsType.String => JgsValue.Number(args[0].AsString.Length),
                _ => throw new JgsRuntimeException(line, col, $"length expects an array or string, but got a {args[0].TypeName}."),
            };
        });

        Define("sum", (args, line, col) => TryReduceImage("sum", args, line, col, out JgsValue imageSum)
            ? imageSum
            : Reduce("sum", args, line, col, (acc, v) => acc + v, 0.0));
        Define("mean", (args, line, col) =>
        {
            if (TryReduceImage("mean", args, line, col, out JgsValue imageMean))
            {
                return imageMean;
            }

            double[] values = ArrayOfNumbers("mean", args, line, col);
            if (values.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "mean needs a non-empty array.");
            }

            double total = 0;
            foreach (double v in values)
            {
                total += v;
            }

            return JgsValue.Number(total / values.Length);
        });

        Define("min", (args, line, col) => MinMax("min", args, line, col, takeMin: true));
        Define("max", (args, line, col) => MinMax("max", args, line, col, takeMin: false));

        Define("numel", (args, line, col) =>
        {
            Arity("numel", args, 1, line, col);
            return args[0].Type switch
            {
                JgsType.Array => JgsValue.Number(args[0].ArrayLength),
                JgsType.String => JgsValue.Number(args[0].AsString.Length),
                JgsType.Image => JgsValue.Number(args[0].AsImage.SampleCount),
                _ => throw new JgsRuntimeException(line, col, $"numel expects an array or string, but got a {args[0].TypeName}."),
            };
        });

        // --- Statistics ----------------------------------------------------------------------
        Define("std", (args, line, col) => JgsValue.Number(System.Math.Sqrt(SampleVariance("std", args, line, col))));
        Define("variance", (args, line, col) => JgsValue.Number(SampleVariance("variance", args, line, col)));
        Define("median", (args, line, col) => JgsValue.Number(JgsStdlib.Median(NonEmpty("median", args, line, col))));
        Define("mode", (args, line, col) => JgsValue.Number(JgsStdlib.Mode(NonEmpty("mode", args, line, col))));

        Define("percentile", (args, line, col) =>
        {
            Arity("percentile", args, 2, line, col);
            double p = Num("percentile", args, 1, line, col);
            if (p < 0 || p > 100)
            {
                throw new JgsRuntimeException(line, col, "percentile expects p between 0 and 100.");
            }

            double[] values = DoubleArray("percentile", args, 0, line, col);
            if (values.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "percentile needs a non-empty array.");
            }

            return JgsValue.Number(JgsStdlib.Percentile(values, p));
        });

        Define("cumsum", (args, line, col) => Numbers(JgsStdlib.CumulativeSum(ArrayOfNumbers("cumsum", args, line, col))));
        Define("cumprod", (args, line, col) => Numbers(JgsStdlib.CumulativeProduct(ArrayOfNumbers("cumprod", args, line, col))));
        Define("diff", (args, line, col) => Numbers(JgsStdlib.Differences(ArrayOfNumbers("diff", args, line, col))));

        // --- Array operations ----------------------------------------------------------------
        Define("sort", (args, line, col) =>
        {
            ArityRange("sort", args, 1, 2, line, col);
            bool descending = args.Count == 2 && OrderIsDescending("sort", args, line, col);
            return JgsValue.Array(JgsStdlib.Sort(Arr("sort", args, 0, line, col), descending)
                ?? throw new JgsRuntimeException(line, col, "sort needs an array of all numbers or all strings."));
        });

        Define("unique", (args, line, col) =>
        {
            Arity("unique", args, 1, line, col);
            return JgsValue.Array(JgsStdlib.Unique(Arr("unique", args, 0, line, col))
                ?? throw new JgsRuntimeException(line, col, "unique needs an array of all numbers or all strings."));
        });

        Define("find", (args, line, col) =>
        {
            ArityRange("find", args, 1, 2, line, col);

            // Indices are 0-based like everything else (ADR 0028). find(mask, 1) numbers them from 1
            // instead — the escape hatch for a ported MATLAB script, where 0-based results would be
            // silently off by one rather than erroring.
            int origin = args.Count == 2 ? IndexOrigin("find", args, 1, line, col) : 0;

            if (args[0].Type == JgsType.Array && args[0].IsPacked)
            {
                // Nonzero is truthy for numbers and bools alike (NaN != 0 is true) — same as IsTruthy.
                ReadOnlySpan<double> span = args[0].AsBuffer.AsSpan();
                var found = new List<double>();
                for (int i = 0; i < span.Length; i++)
                {
                    if (span[i] != 0)
                    {
                        found.Add(i + origin);
                    }
                }

                return NumbersCopy(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(found));
            }

            JgsValue[] elements = Arr("find", args, 0, line, col);
            var indices = new List<JgsValue>();
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].IsTruthy)
                {
                    indices.Add(JgsValue.Number(i + origin));
                }
            }

            return JgsValue.Array(indices.ToArray());
        });

        Define("any", (args, line, col) =>
        {
            Arity("any", args, 1, line, col);
            if (args[0].Type == JgsType.Array && args[0].IsPacked)
            {
                ReadOnlySpan<double> span = args[0].AsBuffer.AsSpan();
                foreach (double v in span)
                {
                    if (v != 0)
                    {
                        return JgsValue.True;
                    }
                }

                return JgsValue.False;
            }

            return JgsValue.Bool(System.Array.Exists(Arr("any", args, 0, line, col), static v => v.IsTruthy));
        });

        Define("all", (args, line, col) =>
        {
            Arity("all", args, 1, line, col);
            if (args[0].Type == JgsType.Array && args[0].IsPacked)
            {
                ReadOnlySpan<double> span = args[0].AsBuffer.AsSpan();
                foreach (double v in span)
                {
                    if (v == 0)
                    {
                        return JgsValue.False;
                    }
                }

                return JgsValue.True; // empty is true, matching TrueForAll on an empty array
            }

            return JgsValue.Bool(System.Array.TrueForAll(Arr("all", args, 0, line, col), static v => v.IsTruthy));
        });

        Define("concat", (args, line, col) =>
        {
            if (args.Count < 2)
            {
                throw new JgsRuntimeException(line, col, $"concat expects at least 2 arguments, but got {args.Count}.");
            }

            var joined = new List<JgsValue>();
            foreach (JgsValue arg in args)
            {
                if (arg.Type == JgsType.Array)
                {
                    joined.AddRange(arg.BoxedElements());
                }
                else
                {
                    joined.Add(arg); // A scalar appends as one element: concat(a, 5).
                }
            }

            return JgsValue.Array(joined.ToArray());
        });

        Define("slice", (args, line, col) =>
        {
            ArityRange("slice", args, 2, 3, line, col);
            JgsValue[] source = Arr("slice", args, 0, line, col);
            int start = Count("slice", args, 1, line, col);
            int stop = args.Count == 3 ? Count("slice", args, 2, line, col) : source.Length;
            if (start < 0 || stop < start || stop > source.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"slice range [{start}, {stop}) is invalid for an array of length {source.Length}.");
            }

            var section = new JgsValue[stop - start];
            System.Array.Copy(source, start, section, 0, section.Length);
            return JgsValue.Array(section);
        });

        Define("indexof", (args, line, col) =>
        {
            Arity("indexof", args, 2, line, col);
            JgsValue[] elements = Arr("indexof", args, 0, line, col);
            for (int i = 0; i < elements.Length; i++)
            {
                if (JgsValue.AreEqual(elements[i], args[1]))
                {
                    return JgsValue.Number(i);
                }
            }

            return JgsValue.Number(-1);
        });

        Define("reverse", (args, line, col) =>
        {
            Arity("reverse", args, 1, line, col);
            var reversed = (JgsValue[])Arr("reverse", args, 0, line, col).Clone();
            System.Array.Reverse(reversed);
            return JgsValue.Array(reversed);
        });

        Define("isnan", (args, line, col) =>
        {
            Arity("isnan", args, 1, line, col);
            return MapToBool("isnan", args[0], static x => double.IsNaN(x), line, col);
        });

        Define("isequal", (args, line, col) =>
        {
            Arity("isequal", args, 2, line, col);
            return JgsValue.Bool(JgsStdlib.DeepEquals(args[0], args[1]));
        });

        Define("and", (args, line, col) => Logical2("and", args, line, col, static (a, b) => a && b));
        Define("or", (args, line, col) => Logical2("or", args, line, col, static (a, b) => a || b));
        Define("not", (args, line, col) =>
        {
            Arity("not", args, 1, line, col);
            if (args[0].Type != JgsType.Array)
            {
                return JgsValue.Bool(!args[0].IsTruthy);
            }

            JgsValue[] source = args[0].BoxedElements();
            var flipped = new JgsValue[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                flipped[i] = JgsValue.Bool(!source[i].IsTruthy);
            }

            return JgsValue.Array(flipped);
        });

        // --- Strings -------------------------------------------------------------------------
        Define("sprintf", (args, line, col) =>
        {
            if (args.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "sprintf expects a format string first.");
            }

            string format = Str("sprintf", args, 0, line, col);
            try
            {
                return JgsValue.Str(JgsSprintf.Format(format, args.Skip(1).ToArray()));
            }
            catch (FormatException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        Define("fprintf", (args, line, col) =>
        {
            if (args.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "fprintf expects a format string first.");
            }

            string format = Str("fprintf", args, 0, line, col);
            try
            {
                // MATLAB fprintf writes exactly what the format says — the newline comes from the
                // format's own \n, so this goes through the raw (no-newline) output seam.
                host.WriteOut(JgsSprintf.Format(format, args.Skip(1).ToArray()));
            }
            catch (FormatException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }

            return JgsValue.Null;
        });

        Define("str", (args, line, col) => { Arity("str", args, 1, line, col); return JgsValue.Str(args[0].Display()); });

        Define("num", (args, line, col) =>
        {
            Arity("num", args, 1, line, col);
            // MATLAB str2double: unparseable text is NaN, so bad cells filter with isnan.
            return JgsValue.Number(double.TryParse(Str("num", args, 0, line, col).Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : double.NaN);
        });

        Define("upper", (args, line, col) => { Arity("upper", args, 1, line, col); return JgsValue.Str(Str("upper", args, 0, line, col).ToUpperInvariant()); });
        Define("lower", (args, line, col) => { Arity("lower", args, 1, line, col); return JgsValue.Str(Str("lower", args, 0, line, col).ToLowerInvariant()); });
        Define("trim", (args, line, col) => { Arity("trim", args, 1, line, col); return JgsValue.Str(Str("trim", args, 0, line, col).Trim()); });

        Define("split", (args, line, col) =>
        {
            Arity("split", args, 2, line, col);
            string separator = Str("split", args, 1, line, col);
            if (separator.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "split separator must not be empty.");
            }

            string[] parts = Str("split", args, 0, line, col).Split(separator, StringSplitOptions.None);
            var result = new JgsValue[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                result[i] = JgsValue.Str(parts[i]);
            }

            return JgsValue.Array(result);
        });

        Define("join", (args, line, col) =>
        {
            Arity("join", args, 2, line, col);
            JgsValue[] parts = Arr("join", args, 0, line, col);
            string separator = Str("join", args, 1, line, col);
            return JgsValue.Str(string.Join(separator, parts.Select(static p => p.Display())));
        });

        Define("startswith", (args, line, col) =>
        {
            Arity("startswith", args, 2, line, col);
            return JgsValue.Bool(Str("startswith", args, 0, line, col).StartsWith(Str("startswith", args, 1, line, col), StringComparison.Ordinal));
        });

        Define("endswith", (args, line, col) =>
        {
            Arity("endswith", args, 2, line, col);
            return JgsValue.Bool(Str("endswith", args, 0, line, col).EndsWith(Str("endswith", args, 1, line, col), StringComparison.Ordinal));
        });

        Define("replace", (args, line, col) =>
        {
            Arity("replace", args, 3, line, col);
            string oldText = Str("replace", args, 1, line, col);
            if (oldText.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "replace cannot search for an empty string.");
            }

            return JgsValue.Str(Str("replace", args, 0, line, col).Replace(oldText, Str("replace", args, 2, line, col), StringComparison.Ordinal));
        });

        Define("contains", (args, line, col) =>
        {
            Arity("contains", args, 2, line, col);
            // Polymorphic: substring test on strings, membership test on arrays.
            if (args[0].Type == JgsType.String)
            {
                return JgsValue.Bool(args[0].AsString.Contains(Str("contains", args, 1, line, col), StringComparison.Ordinal));
            }

            JgsValue[] haystack = Arr("contains", args, 0, line, col);
            return JgsValue.Bool(System.Array.Exists(haystack, v => JgsValue.AreEqual(v, args[1])));
        });

        // --- Table access --------------------------------------------------------------------
        Define("readcsv", (args, line, col) => ReadTable("readcsv", args, line, col, host.readcsv, host.readcsv));
        Define("readxlsx", (args, line, col) => ReadTable("readxlsx", args, line, col, host.readxlsx, host.readxlsx));
        Define("readtable", (args, line, col) => ReadTable("readtable", args, line, col, host.readtable, host.readtable));

        Define("colnames", (args, line, col) =>
        {
            Arity("colnames", args, 1, line, col);
            IReadOnlyList<string> names = Tbl("colnames", args, 0, line, col).ColumnNames;
            var result = new JgsValue[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                result[i] = JgsValue.Str(names[i]);
            }

            return JgsValue.Array(result);
        });

        Define("rowcount", (args, line, col) =>
        {
            Arity("rowcount", args, 1, line, col);
            return JgsValue.Number(Tbl("rowcount", args, 0, line, col).RowCount);
        });

        Define("textcolumn", (args, line, col) =>
        {
            Arity("textcolumn", args, 2, line, col);
            Table table = Tbl("textcolumn", args, 0, line, col);
            string name = Str("textcolumn", args, 1, line, col);
            if (!table.TryGetColumn(name, out TableColumn? textColumn))
            {
                throw new JgsRuntimeException(line, col,
                    $"The table has no column named '{name}'. Available: {string.Join(", ", table.ColumnNames)}.");
            }

            var result = new JgsValue[table.RowCount];
            for (int row = 0; row < result.Length; row++)
            {
                result[row] = JgsValue.Str(textColumn.IsMissing(row) ? "" : textColumn.GetText(row));
            }

            return JgsValue.Array(result);
        });

        Define("column", (args, line, col) =>
        {
            Arity("column", args, 2, line, col);
            Table table = Tbl("column", args, 0, line, col);
            string name = Str("column", args, 1, line, col);
            double[] values;
            try
            {
                values = TableSeries.GetNumbers(table, name);
            }
            catch (KeyNotFoundException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }

            // The table column may return its internal storage, so this copies — never adopts.
            return NumbersCopy(values);
        });

        // --- Output --------------------------------------------------------------------------
        Define("print", (args, line, col) =>
        {
            host.print(string.Join(" ", args.Select(a => a.Display())));
            return JgsValue.Null;
        });

        // --- Figure setup and plotting -------------------------------------------------------
        Define("figure", (args, line, col) =>
        {
            ArityRange("figure", args, 0, 1, line, col);
            if (args.Count == 1)
            {
                int number = Count("figure", args, 0, line, col);
                if (number < 1)
                {
                    throw new JgsRuntimeException(line, col, "Figure numbers start at 1.");
                }

                JG.Figure(number);
                return JgsValue.Number(number);
            }

            JG.Figure();
            return JgsValue.Number(JG.CurrentFigureNumber);
        });
        Define("subplot", (args, line, col) =>
        {
            Arity("subplot", args, 3, line, col);
            JG.Subplot(Count("subplot", args, 0, line, col), Count("subplot", args, 1, line, col), Count("subplot", args, 2, line, col));
            return JgsValue.Null;
        });

        Define("plot", (args, line, col) => Plot(args, line, col));
        Define("scatter", (args, line, col) => XyOrTable("scatter", args, line, col,
            (x, y) => JG.Scatter(x, y), (t, xc, yc) => JG.Scatter(t, xc, yc)));
        Define("bar", (args, line, col) => XyOrTable("bar", args, line, col,
            (x, y) => JG.Bar(x, y), (t, xc, yc) => JG.Bar(t, xc, yc)));
        Define("stem", (args, line, col) => Stem(args, line, col));
        Define("histogram", (args, line, col) => Histogram(args, line, col));
        Define("errorbar", (args, line, col) => ErrorBar(args, line, col));

        // --- 3D surfaces, contours, and images -----------------------------------------------
        Define("meshgrid", (args, line, col) =>
        {
            Arity("meshgrid", args, 2, line, col);
            double[] x = DoubleArray("meshgrid", args, 0, line, col);
            double[] y = DoubleArray("meshgrid", args, 1, line, col);

            var xRows = new JgsValue[y.Length];
            var yRows = new JgsValue[y.Length];
            for (int r = 0; r < y.Length; r++)
            {
                var xRow = new JgsValue[x.Length];
                var yRow = new JgsValue[x.Length];
                for (int c = 0; c < x.Length; c++)
                {
                    xRow[c] = JgsValue.Number(x[c]);
                    yRow[c] = JgsValue.Number(y[r]);
                }

                xRows[r] = JgsValue.Array(xRow);
                yRows[r] = JgsValue.Array(yRow);
            }

            return JgsValue.Array([JgsValue.Array(xRows), JgsValue.Array(yRows)]);
        });

        Define("surf", (args, line, col) => Surface3D("surf", args, line, col,
            (x, y, z) => JG.Surf(x, y, z), z => JG.Surf(z)));
        Define("mesh", (args, line, col) => Surface3D("mesh", args, line, col,
            (x, y, z) => JG.Mesh(x, y, z), z => JG.Mesh(z)));
        Define("meshc", (args, line, col) => Surface3D("meshc", args, line, col,
            (x, y, z) => JG.MeshC(x, y, z), z => { JG.Mesh(z).ShowContourBelow = true; }));

        Define("contour", (args, line, col) => Contour("contour", args, line, col, filled: false));
        Define("contourf", (args, line, col) => Contour("contourf", args, line, col, filled: true));

        Define("imagesc", (args, line, col) =>
        {
            Arity("imagesc", args, 1, line, col);
            JG.Image(Matrix("imagesc", args, 0, line, col));
            return JgsValue.Null;
        });

        Define("pcolor", (args, line, col) =>
        {
            Arity("pcolor", args, 3, line, col);
            JG.Pcolor(
                DoubleArray("pcolor", args, 0, line, col),
                DoubleArray("pcolor", args, 1, line, col),
                Matrix("pcolor", args, 2, line, col));
            return JgsValue.Null;
        });

        Define("zlabel", (args, line, col) => { Arity("zlabel", args, 1, line, col); JG.ZLabel(Str("zlabel", args, 0, line, col)); return JgsValue.Null; });
        Define("zlim", (args, line, col) => { Arity("zlim", args, 2, line, col); JG.ZLim(Num("zlim", args, 0, line, col), Num("zlim", args, 1, line, col)); return JgsValue.Null; });
        Define("view", (args, line, col) => { Arity("view", args, 2, line, col); JG.View(Num("view", args, 0, line, col), Num("view", args, 1, line, col)); return JgsValue.Null; });

        Define("colormap", (args, line, col) =>
        {
            Arity("colormap", args, 1, line, col);
            try
            {
                JG.Colormap(Str("colormap", args, 0, line, col));
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }

            return JgsValue.Null;
        });

        Define("colorbar", (args, line, col) =>
        {
            ArityRange("colorbar", args, 0, 1, line, col);
            JG.Colorbar(args.Count == 0 || Truthy(args, 0));
            return JgsValue.Null;
        });

        Define("semilogy", (args, line, col) => Semilog("semilogy", args, line, col, (x, y, s) => JG.SemilogY(x, y, s)));
        Define("semilogx", (args, line, col) => Semilog("semilogx", args, line, col, (x, y, s) => JG.SemilogX(x, y, s)));
        Define("loglog", (args, line, col) => Semilog("loglog", args, line, col, (x, y, s) => JG.LogLog(x, y, s)));

        Define("title", (args, line, col) => { Arity("title", args, 1, line, col); JG.Title(Str("title", args, 0, line, col)); return JgsValue.Null; });
        Define("xlabel", (args, line, col) => { Arity("xlabel", args, 1, line, col); JG.XLabel(Str("xlabel", args, 0, line, col)); return JgsValue.Null; });
        Define("ylabel", (args, line, col) => { Arity("ylabel", args, 1, line, col); JG.YLabel(Str("ylabel", args, 0, line, col)); return JgsValue.Null; });
        Define("xlim", (args, line, col) => { (double lo, double hi) = LimitPair("xlim", args, line, col); JG.XLim(lo, hi); return JgsValue.Null; });
        Define("ylim", (args, line, col) => { (double lo, double hi) = LimitPair("ylim", args, line, col); JG.YLim(lo, hi); return JgsValue.Null; });

        Define("grid", (args, line, col) => { ArityRange("grid", args, 0, 1, line, col); JG.Grid(args.Count == 0 || Truthy(args, 0)); return JgsValue.Null; });
        Define("hold", (args, line, col) => { ArityRange("hold", args, 0, 1, line, col); JG.Hold(args.Count == 0 || Truthy(args, 0)); return JgsValue.Null; });

        Define("legend", (args, line, col) =>
        {
            var names = new string[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                names[i] = Str("legend", args, i, line, col);
            }

            JG.Legend(names);
            return JgsValue.Null;
        });

        Define("show", (args, line, col) =>
        {
            ArityRange("show", args, 0, 1, line, col);
            if (args.Count == 1)
            {
                int number = Count("show", args, 0, line, col);
                if (!JG.TryGetFigure(number, out _))
                {
                    throw new JgsRuntimeException(line, col, $"There is no figure {number} to show.");
                }

                host.show(number);
            }
            else
            {
                host.show();
            }

            return JgsValue.Null;
        });

        // --- Figure files --------------------------------------------------------------------
        Define("savefigure", (args, line, col) => FigureFile("savefigure", args, line, col,
            (path, figure) => host.savefigure(path, figure)));
        Define("exportfigure", (args, line, col) => FigureFile("exportfigure", args, line, col,
            (path, figure) => host.exportfigure(path, figure)));

        Define("loadfigure", (args, line, col) =>
        {
            Arity("loadfigure", args, 1, line, col);
            try
            {
                FigureModel figure = host.loadfigure(Str("loadfigure", args, 0, line, col));
                return JgsValue.Number(JG.GetFigureNumber(figure));
            }
            catch (Exception ex) when (ex is not (JgsException or OperationCanceledException))
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        // --- Image processing (M24) — defined in JgsBuiltins.Imaging.cs ----------------------
        DefineImagingBuiltins(Define, host, random);

        return env;
    }

    /// <summary>Dispatches savefigure/exportfigure: (path) targets the current figure, (path, fig)
    /// a figure by 1-based handle. Host/IO failures become script diagnostics.</summary>
    private static JgsValue FigureFile(string name, IReadOnlyList<JgsValue> args, int line, int col,
        Action<string, FigureModel> apply)
    {
        ArityRange(name, args, 1, 2, line, col);
        string path = Str(name, args, 0, line, col);

        FigureModel figure;
        if (args.Count == 2)
        {
            int number = Count(name, args, 1, line, col);
            if (!JG.TryGetFigure(number, out figure))
            {
                throw new JgsRuntimeException(line, col, $"There is no figure {number}.");
            }
        }
        else
        {
            figure = JG.CurrentFigure;
        }

        try
        {
            apply(path, figure);
        }
        catch (Exception ex) when (ex is not (JgsException or OperationCanceledException))
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        return JgsValue.Null;
    }

    // --- Plotting dispatch -----------------------------------------------------------------------

    private static JgsValue Plot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0 && args[0].Type == JgsType.Table)
        {
            ArityRange("plot", args, 3, 4, line, col);
            Table table = Tbl("plot", args, 0, line, col);
            string xColumn = Str("plot", args, 1, line, col);
            string yColumn = Str("plot", args, 2, line, col);
            string? spec = args.Count == 4 ? Str("plot", args, 3, line, col) : null;
            JG.Plot(table, xColumn, yColumn, spec);
            return JgsValue.Null;
        }

        switch (args.Count)
        {
            case 1:
                JG.Plot(DoubleArray("plot", args, 0, line, col));
                return JgsValue.Null;
            case 2 when args[1].Type == JgsType.String:
                JG.Plot(DoubleArray("plot", args, 0, line, col), Str("plot", args, 1, line, col));
                return JgsValue.Null;
            case 2:
                JG.Plot(DoubleArray("plot", args, 0, line, col), DoubleArray("plot", args, 1, line, col));
                return JgsValue.Null;
            case 3:
                JG.Plot(DoubleArray("plot", args, 0, line, col), DoubleArray("plot", args, 1, line, col), Str("plot", args, 2, line, col));
                return JgsValue.Null;
            default:
                return PlotMultipleSeries(args, line, col);
        }
    }

    /// <summary>
    /// MATLAB multi-series plot: repeated (x, y[, spec]) groups — plot(t, a, 'b', t, b, 'r--').
    /// Later groups are drawn with hold forced on; the caller's hold state is restored afterwards.
    /// </summary>
    private static JgsValue PlotMultipleSeries(IReadOnlyList<JgsValue> args, int line, int col)
    {
        var groups = new List<(double[] X, double[] Y, string? Spec)>();
        int i = 0;
        while (i < args.Count)
        {
            if (i + 1 >= args.Count || args[i].Type != JgsType.Array || args[i + 1].Type != JgsType.Array)
            {
                throw new JgsRuntimeException(line, col,
                    "plot expects (y), (x, y[, spec]) groups, or (table, xColumn, yColumn[, spec]).");
            }

            double[] x = DoubleArray("plot", args, i, line, col);
            double[] y = DoubleArray("plot", args, i + 1, line, col);
            string? spec = null;
            i += 2;
            if (i < args.Count && args[i].Type == JgsType.String)
            {
                spec = Str("plot", args, i, line, col);
                i++;
            }

            groups.Add((x, y, spec));
        }

        bool wasHolding = JG.IsHolding;
        try
        {
            foreach ((double[] x, double[] y, string? spec) in groups)
            {
                JG.Plot(x, y, spec);
                JG.Hold(true);
            }
        }
        finally
        {
            JG.Hold(wasHolding);
        }

        return JgsValue.Null;
    }

    private static JgsValue XyOrTable(string name, IReadOnlyList<JgsValue> args, int line, int col,
        Action<double[], double[]> arrays, Action<Table, string, string> table)
    {
        if (args.Count > 0 && args[0].Type == JgsType.Table)
        {
            Arity(name, args, 3, line, col);
            table(Tbl(name, args, 0, line, col), Str(name, args, 1, line, col), Str(name, args, 2, line, col));
            return JgsValue.Null;
        }

        Arity(name, args, 2, line, col);
        arrays(DoubleArray(name, args, 0, line, col), DoubleArray(name, args, 1, line, col));
        return JgsValue.Null;
    }

    private static JgsValue Stem(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("stem", args, 1, 2, line, col);
        if (args.Count == 1)
        {
            JG.Stem(DoubleArray("stem", args, 0, line, col));
        }
        else
        {
            JG.Stem(DoubleArray("stem", args, 0, line, col), DoubleArray("stem", args, 1, line, col));
        }

        return JgsValue.Null;
    }

    private static JgsValue Histogram(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0 && args[0].Type == JgsType.Table)
        {
            ArityRange("histogram", args, 2, 3, line, col);
            int tableBins = args.Count == 3 ? Count("histogram", args, 2, line, col) : 10;
            JG.Histogram(Tbl("histogram", args, 0, line, col), Str("histogram", args, 1, line, col), tableBins);
            return JgsValue.Null;
        }

        ArityRange("histogram", args, 1, 2, line, col);
        int bins = args.Count == 2 ? Count("histogram", args, 1, line, col) : 10;
        JG.Histogram(DoubleArray("histogram", args, 0, line, col), bins);
        return JgsValue.Null;
    }

    private static JgsValue ErrorBar(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0 && args[0].Type == JgsType.Table)
        {
            Arity("errorbar", args, 4, line, col);
            JG.ErrorBar(Tbl("errorbar", args, 0, line, col), Str("errorbar", args, 1, line, col), Str("errorbar", args, 2, line, col), Str("errorbar", args, 3, line, col));
            return JgsValue.Null;
        }

        Arity("errorbar", args, 3, line, col);
        JG.ErrorBar(DoubleArray("errorbar", args, 0, line, col), DoubleArray("errorbar", args, 1, line, col), DoubleArray("errorbar", args, 2, line, col));
        return JgsValue.Null;
    }

    /// <summary>Dispatches surf/mesh/meshc: (z) with a matrix, or (x, y, z) with grid vectors.</summary>
    private static JgsValue Surface3D(string name, IReadOnlyList<JgsValue> args, int line, int col,
        Action<double[], double[], double[,]> full, Action<double[,]> zOnly)
    {
        if (args.Count == 1)
        {
            zOnly(Matrix(name, args, 0, line, col));
            return JgsValue.Null;
        }

        Arity(name, args, 3, line, col);
        double[] x = DoubleArray(name, args, 0, line, col);
        double[] y = DoubleArray(name, args, 1, line, col);
        double[,] z = Matrix(name, args, 2, line, col);
        try
        {
            full(x, y, z);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        return JgsValue.Null;
    }

    private static JgsValue Contour(string name, IReadOnlyList<JgsValue> args, int line, int col, bool filled)
    {
        ArityRange(name, args, 3, 4, line, col);
        double[] x = DoubleArray(name, args, 0, line, col);
        double[] y = DoubleArray(name, args, 1, line, col);
        double[,] z = Matrix(name, args, 2, line, col);
        double[]? levels = args.Count == 4 ? DoubleArray(name, args, 3, line, col) : null;
        try
        {
            if (filled)
            {
                JG.ContourF(x, y, z, levels);
            }
            else
            {
                JG.Contour(x, y, z, levels);
            }
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }

        return JgsValue.Null;
    }

    private static JgsValue Semilog(string name, IReadOnlyList<JgsValue> args, int line, int col, Action<double[], double[], string?> apply)
    {
        ArityRange(name, args, 2, 3, line, col);
        string? spec = args.Count == 3 ? Str(name, args, 2, line, col) : null;
        apply(DoubleArray(name, args, 0, line, col), DoubleArray(name, args, 1, line, col), spec);
        return JgsValue.Null;
    }

    // --- RF network table glue -------------------------------------------------------------------

    /// <summary>Projects an N-port network onto a table: a <c>freq</c> column, per-pair
    /// <c>{prefix}{i}{j}_re/_im</c> columns (ports 1-based), then a constant <c>z0</c> column.</summary>
    private static Table NetworkToTable(SParameterNetwork net, string prefix)
    {
        int points = net.PointCount;
        var columns = new List<TableColumn> { new NumberColumn("freq", (double[])net.Frequencies.Clone()) };
        for (int i = 0; i < net.Ports; i++)
        {
            for (int j = 0; j < net.Ports; j++)
            {
                var re = new double[points];
                var im = new double[points];
                for (int f = 0; f < points; f++)
                {
                    Complex value = net[f, i, j];
                    re[f] = value.Real;
                    im[f] = value.Imaginary;
                }

                columns.Add(new NumberColumn($"{prefix}{i + 1}{j + 1}_re", re));
                columns.Add(new NumberColumn($"{prefix}{i + 1}{j + 1}_im", im));
            }
        }

        var z0 = new double[points];
        System.Array.Fill(z0, net.ReferenceImpedance);
        columns.Add(new NumberColumn("z0", z0));
        return new Table(columns);
    }

    /// <summary>Rebuilds the network domain type from a network table, discovering the parameter prefix from its columns.</summary>
    private static SParameterNetwork TableToNetwork(Table table)
    {
        double[] frequencies = TableSeries.GetNumbers(table, "freq");
        double referenceImpedance = TableSeries.GetNumbers(table, "z0")[0];
        int ports = (int)System.Math.Round(System.Math.Sqrt((table.ColumnCount - 2) / 2.0));
        string prefix = ParameterPrefix(table);
        int points = frequencies.Length;
        var data = new Complex[points * ports * ports];
        for (int i = 0; i < ports; i++)
        {
            for (int j = 0; j < ports; j++)
            {
                double[] re = TableSeries.GetNumbers(table, $"{prefix}{i + 1}{j + 1}_re");
                double[] im = TableSeries.GetNumbers(table, $"{prefix}{i + 1}{j + 1}_im");
                for (int f = 0; f < points; f++)
                {
                    data[((f * ports) + i) * ports + j] = new Complex(re[f], im[f]);
                }
            }
        }

        return new SParameterNetwork(ports, referenceImpedance, frequencies, data);
    }

    /// <summary>The leading letters of the first parameter column (e.g. "s" from "s11_re"); "s" if none is found.</summary>
    private static string ParameterPrefix(Table table)
    {
        foreach (string name in table.ColumnNames)
        {
            if (name.Equals("freq", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("z0", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int end = 0;
            while (end < name.Length && char.IsLetter(name[end]))
            {
                end++;
            }

            if (end > 0)
            {
                return name[..end];
            }
        }

        return "s";
    }

    /// <summary>Reads the (i, j) parameter across frequency from a network table, with 1-based port numbers.</summary>
    private static Complex[] ReadParam(Table table, int i, int j, int line, int col)
    {
        string prefix = ParameterPrefix(table);
        try
        {
            double[] re = TableSeries.GetNumbers(table, $"{prefix}{i}{j}_re");
            double[] im = TableSeries.GetNumbers(table, $"{prefix}{i}{j}_im");
            var result = new Complex[re.Length];
            for (int f = 0; f < result.Length; f++)
            {
                result[f] = new Complex(re[f], im[f]);
            }

            return result;
        }
        catch (KeyNotFoundException)
        {
            throw new JgsRuntimeException(line, col,
                $"There is no parameter ({i}, {j}) in this network (columns: {string.Join(", ", table.ColumnNames)}).");
        }
    }

    private static JgsValue ConvertNetwork(
        string name, IReadOnlyList<JgsValue> args, string prefix,
        Func<SParameterNetwork, SParameterNetwork> convert, int line, int col)
    {
        Arity(name, args, 1, line, col);
        SParameterNetwork net = TableToNetwork(Tbl(name, args, 0, line, col));
        try
        {
            return JgsValue.Table(NetworkToTable(convert(net), prefix));
        }
        catch (NotSupportedException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: " + ex.Message);
        }
    }

    private static JgsValue LineCalc(
        string name, IReadOnlyList<JgsValue> args, int line, int col, Func<double, double, double, double> calc)
    {
        Arity(name, args, 3, line, col);
        try
        {
            return JgsValue.Number(calc(
                Num(name, args, 0, line, col),
                Num(name, args, 1, line, col),
                Num(name, args, 2, line, col)));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: " + ex.Message);
        }
    }

    /// <summary>Reads a complex-or-real scalar argument (for a load/source impedance).</summary>
    private static Complex ComplexScalar(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        return value.Type switch
        {
            JgsType.Number or JgsType.Bool => new Complex(value.AsNumber, 0),
            JgsType.Complex => value.AsComplex,
            _ => throw new JgsRuntimeException(line, col,
                $"{name} expects argument {index + 1} to be a number or complex value, but got a {value.TypeName}."),
        };
    }

    private static JgsValue RfPlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("rfplot", args, 1, 3, line, col);
        Table table = Tbl("rfplot", args, 0, line, col);
        double[] frequencies = TableSeries.GetNumbers(table, "freq");
        int ports = (int)System.Math.Round(System.Math.Sqrt((table.ColumnCount - 2) / 2.0));

        var pairs = new List<(int I, int J)>();
        if (args.Count == 3)
        {
            pairs.Add((Count("rfplot", args, 1, line, col), Count("rfplot", args, 2, line, col)));
        }
        else
        {
            for (int i = 1; i <= ports; i++)
            {
                for (int j = 1; j <= ports; j++)
                {
                    pairs.Add((i, j));
                }
            }
        }

        bool wasHolding = JG.IsHolding;
        try
        {
            foreach ((int i, int j) in pairs)
            {
                Complex[] parameter = ReadParam(table, i, j, line, col);
                var magnitudeDb = new double[parameter.Length];
                for (int f = 0; f < parameter.Length; f++)
                {
                    magnitudeDb[f] = 20 * System.Math.Log10(Complex.Abs(parameter[f]));
                }

                JG.Plot(frequencies, magnitudeDb).DisplayName = $"S{i}{j}";
                JG.Hold(true);
            }
        }
        finally
        {
            JG.Hold(wasHolding);
        }

        JG.XLabel("Frequency (Hz)");
        JG.YLabel("Magnitude (dB)");
        return JgsValue.Null;
    }

    private static JgsValue SmithPlot(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("smithplot", args, 1, 3, line, col);
        Complex[] gamma;
        if (args[0].Type == JgsType.Table)
        {
            Table table = Tbl("smithplot", args, 0, line, col);
            int i = args.Count >= 2 ? Count("smithplot", args, 1, line, col) : 1;
            int j = args.Count >= 3 ? Count("smithplot", args, 2, line, col) : 1;
            gamma = ReadParam(table, i, j, line, col);
        }
        else
        {
            gamma = ComplexArray("smithplot", args, 0, line, col);
        }

        var re = new double[gamma.Length];
        var im = new double[gamma.Length];
        for (int k = 0; k < gamma.Length; k++)
        {
            re[k] = gamma[k].Real;
            im[k] = gamma[k].Imaginary;
        }

        JG.SmithGamma(re, im);
        return JgsValue.Null;
    }

    private static JgsValue Filled(string name, IReadOnlyList<JgsValue> args, double value, int line, int col)
    {
        ArityRange(name, args, 1, 2, line, col);

        // A size vector spreads into dimensions: zeros(size(t)) with size = [1, n] (or [n]) gives a
        // flat n-vector; [r, c] with both > 1 gives a matrix.
        if (args.Count == 1 && args[0].Type == JgsType.Array)
        {
            double[] dimensions = DoubleArray(name, args, 0, line, col);
            return dimensions.Length switch
            {
                1 => FilledVector((int)dimensions[0], value, name, line, col),
                2 when dimensions[0] <= 1 || dimensions[1] <= 1 =>
                    FilledVector((int)System.Math.Max(dimensions[0] * dimensions[1], 0), value, name, line, col),
                2 => Filled(name, [JgsValue.Number(dimensions[0]), JgsValue.Number(dimensions[1])], value, line, col),
                _ => throw new JgsRuntimeException(line, col, $"{name} supports at most 2 dimensions."),
            };
        }

        int count = Count(name, args, 0, line, col);
        if (count < 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a non-negative count.");
        }

        // Two arguments build a rows x cols matrix (an array of row arrays).
        if (args.Count == 2)
        {
            int cols = Count(name, args, 1, line, col);
            if (cols < 0)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs a non-negative count.");
            }

            var rows = new JgsValue[count];
            for (int r = 0; r < count; r++)
            {
                rows[r] = FilledVector(cols, value, name, line, col);
            }

            return JgsValue.Array(rows);
        }

        return FilledVector(count, value, name, line, col);
    }

    private static JgsValue FilledVector(int count, double value, string name, int line, int col)
    {
        if (count < 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a non-negative count.");
        }

        if (JgsPacking.Enabled)
        {
            var buffer = JgsPacking.Allocate(count);
            PackedMath.FillConstant(buffer, value);
            return JgsValue.Packed(buffer);
        }

        var result = new JgsValue[count];
        JgsValue element = JgsValue.Number(value);
        for (int i = 0; i < count; i++)
        {
            result[i] = element;
        }

        return JgsValue.Array(result);
    }

    private static JgsValue Reduce(string name, IReadOnlyList<JgsValue> args, int line, int col, Func<double, double, double> op, double seed)
    {
        double[] values = ArrayOfNumbers(name, args, line, col);
        double acc = seed;
        foreach (double v in values)
        {
            acc = op(acc, v);
        }

        return JgsValue.Number(acc);
    }

    /// <summary>
    /// The image fast path shared by sum/mean/min/max: a single image argument reduces straight over
    /// the sample span, so a megapixel image never boxes (and never hits im2mat's element cap).
    /// </summary>
    private static bool TryReduceImage(string name, IReadOnlyList<JgsValue> args, int line, int col, out JgsValue result)
    {
        result = JgsValue.Null;
        if (args.Count != 1 || args[0].Type != JgsType.Image)
        {
            return false;
        }

        ReadOnlySpan<double> pixels = args[0].AsImage.Pixels;
        if (pixels.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a non-empty image.");
        }

        double total = 0;
        double lowest = double.PositiveInfinity;
        double highest = double.NegativeInfinity;
        foreach (double v in pixels)
        {
            total += v;
            lowest = System.Math.Min(lowest, v);
            highest = System.Math.Max(highest, v);
        }

        result = JgsValue.Number(name switch
        {
            "sum" => total,
            "mean" => total / pixels.Length,
            "min" => lowest,
            _ => highest,
        });
        return true;
    }

    private static JgsValue MinMax(string name, IReadOnlyList<JgsValue> args, int line, int col, bool takeMin)
    {
        if (TryReduceImage(name, args, line, col, out JgsValue image))
        {
            return image;
        }

        double[] values;
        if (args.Count == 1 && args[0].Type == JgsType.Array)
        {
            values = DoubleArray(name, args, 0, line, col);
        }
        else
        {
            values = new double[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                values[i] = Num(name, args, i, line, col);
            }
        }

        if (values.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least one value.");
        }

        double best = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            best = takeMin ? System.Math.Min(best, values[i]) : System.Math.Max(best, values[i]);
        }

        return JgsValue.Number(best);
    }

    /// <summary>Dispatches a reader builtin: (path) or (path, skiprows) discarding leading junk rows.</summary>
    private static JgsValue ReadTable(string name, IReadOnlyList<JgsValue> args, int line, int col,
        Func<string, Table> read, Func<string, int, Table> readSkipping)
    {
        ArityRange(name, args, 1, 2, line, col);
        string path = Str(name, args, 0, line, col);
        return JgsValue.Table(args.Count == 2
            ? readSkipping(path, Count(name, args, 1, line, col))
            : read(path));
    }

    // --- Stdlib glue -----------------------------------------------------------------------------

    /// <summary>
    /// Wraps a double[] back into a JGS numeric array value. CONTRACT: the caller hands over a
    /// freshly built array — with packing enabled it is adopted as the value's backing storage
    /// without a copy, so a caller that kept writing through its own reference would corrupt the
    /// script's array. Use <see cref="NumbersCopy"/> for data that something else still owns.
    /// </summary>
    private static JgsValue Numbers(double[] values)
    {
        if (JgsPacking.Enabled)
        {
            return JgsValue.Packed(ManagedBuffer.Adopt(values));
        }

        var result = new JgsValue[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = JgsValue.Number(values[i]);
        }

        return JgsValue.Array(result);
    }

    /// <summary>A JGS numeric array copied from <paramref name="values"/> (safe for shared storage).</summary>
    private static JgsValue NumbersCopy(ReadOnlySpan<double> values)
    {
        if (JgsPacking.Enabled)
        {
            var buffer = JgsPacking.Allocate(values.Length);
            values.CopyTo(buffer.AsSpan());
            return JgsValue.Packed(buffer);
        }

        var result = new JgsValue[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = JgsValue.Number(values[i]);
        }

        return JgsValue.Array(result);
    }

    private static double SampleVariance(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        double[] values = ArrayOfNumbers(name, args, line, col);
        if (values.Length < 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs at least 2 values.");
        }

        return JgsStdlib.Variance(values);
    }

    private static double[] NonEmpty(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        double[] values = ArrayOfNumbers(name, args, line, col);
        if (values.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a non-empty array.");
        }

        return values;
    }

    private static bool OrderIsDescending(string name, IReadOnlyList<JgsValue> args, int line, int col) =>
        Str(name, args, 1, line, col) switch
        {
            "asc" => false,
            "desc" => true,
            var other => throw new JgsRuntimeException(line, col, $"{name} order must be \"asc\" or \"desc\", but got \"{other}\"."),
        };

    private static JgsValue MapToBool(string name, JgsValue value, Func<double, bool> test, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return JgsValue.Bool(test(value.AsNumber));
        }

        if (value.Type == JgsType.Array)
        {
            JgsValue[] source = value.BoxedElements();
            var result = new JgsValue[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].Type is not (JgsType.Number or JgsType.Bool))
                {
                    throw new JgsRuntimeException(line, col, $"{name} expects numeric array elements, but one was a {source[i].TypeName}.");
                }

                result[i] = JgsValue.Bool(test(source[i].AsNumber));
            }

            return JgsValue.Array(result);
        }

        throw new JgsRuntimeException(line, col, $"{name} expects a number or numeric array, but got a {value.TypeName}.");
    }

    /// <summary>Element-wise logic over truthiness, broadcasting a scalar across an array.</summary>
    private static JgsValue Logical2(string name, IReadOnlyList<JgsValue> args, int line, int col, Func<bool, bool, bool> op)
    {
        Arity(name, args, 2, line, col);
        JgsValue left = args[0];
        JgsValue right = args[1];

        if (left.Type != JgsType.Array && right.Type != JgsType.Array)
        {
            return JgsValue.Bool(op(left.IsTruthy, right.IsTruthy));
        }

        if (left.Type == JgsType.Array && right.Type == JgsType.Array)
        {
            JgsValue[] a = left.BoxedElements();
            JgsValue[] b = right.BoxedElements();
            if (a.Length != b.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} cannot combine arrays of different lengths ({a.Length} and {b.Length}).");
            }

            var pairwise = new JgsValue[a.Length];
            for (int i = 0; i < pairwise.Length; i++)
            {
                pairwise[i] = JgsValue.Bool(op(a[i].IsTruthy, b[i].IsTruthy));
            }

            return JgsValue.Array(pairwise);
        }

        bool arrayOnLeft = left.Type == JgsType.Array;
        JgsValue[] array = (arrayOnLeft ? left : right).BoxedElements();
        bool scalar = (arrayOnLeft ? right : left).IsTruthy;
        var result = new JgsValue[array.Length];
        for (int i = 0; i < result.Length; i++)
        {
            bool element = array[i].IsTruthy;
            result[i] = JgsValue.Bool(arrayOnLeft ? op(element, scalar) : op(scalar, element));
        }

        return JgsValue.Array(result);
    }

    // --- Argument helpers ------------------------------------------------------------------------

    private static JgsValue[] Arr(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be an array, but got a {value.TypeName}.");
        }

        // Packed inputs materialize a boxed copy here — read-only use, never worse than the
        // all-boxed world; the hot builtins bypass this with packed fast paths.
        return value.BoxedElements();
    }


    private static JgsValue MapNumeric(string name, JgsValue value, Func<double, double> f, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return JgsValue.Number(f(value.AsNumber));
        }

        if (value.Type == JgsType.Array)
        {
            if (value.IsPacked)
            {
                // Same delegate over the flat buffer: bit-identical results, no per-element boxing.
                var dest = JgsPacking.Allocate(value.ArrayLength);
                PackedMath.Map(value.AsBuffer, dest, f);
                return JgsValue.Packed(dest);
            }

            JgsValue[] source = value.AsArray;
            var result = new JgsValue[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                // Recurse so matrices (nested arrays) map elementwise: sin(X) works on meshgrid output.
                result[i] = MapNumeric(name, source[i], f, line, col);
            }

            return JgsValue.Array(result);
        }

        throw new JgsRuntimeException(line, col, $"{name} expects a number or numeric array, but got a {value.TypeName}.");
    }

    /// <summary>Pairwise elementwise application with scalar broadcast (atan2(y, x) over arrays).</summary>
    private static JgsValue Zip(string name, JgsValue a, JgsValue b, Func<double, double, double> f, int line, int col)
    {
        bool aScalar = a.Type is JgsType.Number or JgsType.Bool;
        bool bScalar = b.Type is JgsType.Number or JgsType.Bool;
        if (aScalar && bScalar)
        {
            return JgsValue.Number(f(a.AsNumber, b.AsNumber));
        }

        // Packed fast paths: the same delegate over flat buffers (atan2 over a million samples
        // without a million boxes). Shapes outside these fall through to the boxed recursion.
        if (a.IsPacked && b.IsPacked)
        {
            ReadOnlySpan<double> xs = a.AsBuffer.AsSpan();
            ReadOnlySpan<double> ys = b.AsBuffer.AsSpan();
            if (xs.Length != ys.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} needs arrays of equal length ({xs.Length} and {ys.Length}).");
            }

            var dest = JgsPacking.Allocate(xs.Length);
            Span<double> d = dest.AsSpan();
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = f(xs[i], ys[i]);
            }

            return JgsValue.Packed(dest);
        }

        if (a.IsPacked && bScalar)
        {
            ReadOnlySpan<double> xs = a.AsBuffer.AsSpan();
            double y = b.AsNumber;
            var dest = JgsPacking.Allocate(xs.Length);
            Span<double> d = dest.AsSpan();
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = f(xs[i], y);
            }

            return JgsValue.Packed(dest);
        }

        if (aScalar && b.IsPacked)
        {
            double x = a.AsNumber;
            ReadOnlySpan<double> ys = b.AsBuffer.AsSpan();
            var dest = JgsPacking.Allocate(ys.Length);
            Span<double> d = dest.AsSpan();
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = f(x, ys[i]);
            }

            return JgsValue.Packed(dest);
        }

        if (a.Type == JgsType.Array && b.Type == JgsType.Array)
        {
            JgsValue[] left = a.BoxedElements();
            JgsValue[] right = b.BoxedElements();
            if (left.Length != right.Length)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} needs arrays of equal length ({left.Length} and {right.Length}).");
            }

            var paired = new JgsValue[left.Length];
            for (int i = 0; i < paired.Length; i++)
            {
                paired[i] = Zip(name, left[i], right[i], f, line, col);
            }

            return JgsValue.Array(paired);
        }

        if (a.Type == JgsType.Array && bScalar)
        {
            JgsValue[] left = a.BoxedElements();
            var spread = new JgsValue[left.Length];
            for (int i = 0; i < spread.Length; i++)
            {
                spread[i] = Zip(name, left[i], b, f, line, col);
            }

            return JgsValue.Array(spread);
        }

        if (aScalar && b.Type == JgsType.Array)
        {
            JgsValue[] right = b.BoxedElements();
            var spread = new JgsValue[right.Length];
            for (int i = 0; i < spread.Length; i++)
            {
                spread[i] = Zip(name, a, right[i], f, line, col);
            }

            return JgsValue.Array(spread);
        }

        throw new JgsRuntimeException(line, col,
            $"{name} expects numbers or numeric arrays, but got {a.TypeName} and {b.TypeName}.");
    }

    /// <summary>
    /// The packed-complex arm of <see cref="MapComplexAware"/>: zero-imaginary elements take the
    /// real path (they read as numbers when boxed), the rest take the complex path, and the result
    /// packs as a plain number array when no imaginary parts survive (abs/real/imag/angle) or as a
    /// planar complex array otherwise (conj).
    /// </summary>
    private static JgsValue MapPackedComplex(JgsPackedComplex source, Func<double, double> real, Func<Complex, JgsValue> complex)
    {
        int count = source.Length;
        var reOut = JgsPacking.Allocate(count);
        var imOut = JgsPacking.Allocate(count);
        bool anyImaginary = false;
        for (int i = 0; i < count; i++)
        {
            double re = source.Re.AsSpan()[i];
            double im = source.Im.AsSpan()[i];
            JgsValue mapped = im == 0 ? JgsValue.Number(real(re)) : complex(new Complex(re, im));
            if (mapped.Type == JgsType.Number)
            {
                reOut.AsSpan()[i] = mapped.AsNumber;
                imOut.AsSpan()[i] = 0;
            }
            else
            {
                Complex written = mapped.AsComplex;
                reOut.AsSpan()[i] = written.Real;
                imOut.AsSpan()[i] = written.Imaginary;
                anyImaginary = true;
            }
        }

        if (anyImaginary)
        {
            return JgsValue.PackedComplexArray(new JgsPackedComplex(reOut, imOut));
        }

        imOut.Dispose();
        return JgsValue.Packed(reOut);
    }

    /// <summary>Elementwise map that takes the real path for numbers and the complex path for complex values.</summary>
    private static JgsValue MapComplexAware(string name, JgsValue value, Func<double, double> real, Func<Complex, JgsValue> complex, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return JgsValue.Number(real(value.AsNumber));
        }

        if (value.Type == JgsType.Complex)
        {
            return complex(value.AsComplex);
        }

        if (value.Type == JgsType.Array)
        {
            if (value.IsPacked)
            {
                // Every packed element is real, so only the real path applies — flat and box-free.
                var dest = JgsPacking.Allocate(value.ArrayLength);
                PackedMath.Map(value.AsBuffer, dest, real);
                return JgsValue.Packed(dest);
            }

            if (value.IsPackedComplex)
            {
                return MapPackedComplex(value.AsPackedComplex, real, complex);
            }

            JgsValue[] source = value.BoxedElements();
            var result = new JgsValue[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = MapComplexAware(name, source[i], real, complex, line, col);
            }

            return JgsValue.Array(result);
        }

        throw new JgsRuntimeException(line, col, $"{name} expects a number or numeric array, but got a {value.TypeName}.");
    }

    /// <summary>
    /// The body of <c>exit</c>/<c>quit</c>: never returns. The request travels as an exception so it
    /// unwinds loops and function calls the way a script author expects "stop now" to.
    /// </summary>
    private static JgsValue Exit(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 0, 1, line, col);
        int code = args.Count == 0 ? 0 : (int)Num(name, args, 0, line, col);
        throw new ScriptExitException(code);
    }

    private static void Arity(string name, IReadOnlyList<JgsValue> args, int count, int line, int col)
    {
        if (args.Count != count)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects {count} argument(s), but got {args.Count}.");
        }
    }

    private static void ArityRange(string name, IReadOnlyList<JgsValue> args, int min, int max, int line, int col)
    {
        if (args.Count < min || args.Count > max)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects between {min} and {max} argument(s), but got {args.Count}.");
        }
    }

    /// <summary>
    /// Reads an optional index-base argument: 0 (the JGS default) or 1 (MATLAB numbering). Only these
    /// two are accepted — an arbitrary offset would be a silent way to produce nonsense indices.
    /// </summary>
    internal static int IndexOrigin(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        double raw = Num(name, args, index, line, col);
        if (raw is not (0 or 1))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the index base must be 0 (the default) or 1, not {raw.ToString(CultureInfo.InvariantCulture)}.");
        }

        return (int)raw;
    }

    private static double Num(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type is not (JgsType.Number or JgsType.Bool))
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be a number, but got a {value.TypeName}.");
        }

        return value.AsNumber;
    }

    private static int Count(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        double raw = Num(name, args, index, line, col);
        if (raw != System.Math.Floor(raw) || double.IsNaN(raw) || double.IsInfinity(raw))
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be a whole number.");
        }

        return (int)raw;
    }

    private static string Str(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be a string, but got a {value.TypeName}.");
        }

        return value.AsString;
    }

    private static Table Tbl(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type != JgsType.Table)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be a table, but got a {value.TypeName}.");
        }

        return value.AsTable;
    }

    private static ImageBuffer Img(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type != JgsType.Image)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be an image, but got a {value.TypeName}.");
        }

        return value.AsImage;
    }

    private static bool Truthy(IReadOnlyList<JgsValue> args, int index) => args[index].IsTruthy;

    private static double[] DoubleArray(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be an array, but got a {value.TypeName}.");
        }

        return ToDoubles(name, value, line, col);
    }

    private static double[] ArrayOfNumbers(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity(name, args, 1, line, col);
        if (args[0].Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects an array, but got a {args[0].TypeName}.");
        }

        return ToDoubles(name, args[0], line, col);
    }

    /// <summary>
    /// Converts a JGS matrix — an array of equal-length numeric row arrays, e.g. the output of
    /// <c>meshgrid</c> or <c>zeros(r, c)</c> — to a <c>double[rows, cols]</c>. Ragged rows error.
    /// </summary>
    private static double[,] Matrix(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects argument {index + 1} to be a matrix (an array of row arrays), but got a {value.TypeName}.");
        }

        JgsValue[] rows = value.BoxedElements();
        if (rows.Length == 0 || rows[0].Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects argument {index + 1} to be a matrix (an array of row arrays); build one with meshgrid, zeros(r, c), or nested literals.");
        }

        int cols = rows[0].ArrayLength;
        var result = new double[rows.Length, cols];
        for (int r = 0; r < rows.Length; r++)
        {
            if (rows[r].Type != JgsType.Array)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: matrix row {r} is a {rows[r].TypeName}, not an array.");
            }

            double[] row = ToDoubles(name, rows[r], line, col);
            if (row.Length != cols)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: matrix rows must all be the same length (row 0 has {cols}, row {r} has {row.Length}).");
            }

            for (int c = 0; c < cols; c++)
            {
                result[r, c] = row[c];
            }
        }

        return result;
    }

    /// <summary>xlim/ylim accept (min, max) or a single [min, max] array (MATLAB xlim([a, b])).</summary>
    private static (double Low, double High) LimitPair(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 1 && args[0].Type == JgsType.Array)
        {
            double[] pair = DoubleArray(name, args, 0, line, col);
            if (pair.Length != 2)
            {
                throw new JgsRuntimeException(line, col, $"{name} expects a two-element [min, max] array.");
            }

            return (pair[0], pair[1]);
        }

        Arity(name, args, 2, line, col);
        return (Num(name, args, 0, line, col), Num(name, args, 1, line, col));
    }

    /// <summary>A numeric vector argument: an array of numbers, or a scalar promoted to [x] (filter(h, 1, x)).</summary>
    private static double[] NumericVector(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return [value.AsNumber];
        }

        return DoubleArray(name, args, index, line, col);
    }

    /// <summary>Converts a JGS array to complex samples (numbers, bools, and complex values allowed).</summary>
    private static Complex[] ComplexArray(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type == JgsType.Array && value.IsPacked)
        {
            ReadOnlySpan<double> span = value.AsBuffer.AsSpan();
            var complexes = new Complex[span.Length];
            for (int i = 0; i < span.Length; i++)
            {
                complexes[i] = new Complex(span[i], 0);
            }

            return complexes;
        }

        if (value.Type == JgsType.Array && value.IsPackedComplex)
        {
            JgsPackedComplex planes = value.AsPackedComplex;
            ReadOnlySpan<double> re = planes.Re.AsSpan();
            ReadOnlySpan<double> im = planes.Im.AsSpan();
            var complexes = new Complex[planes.Length];
            for (int i = 0; i < complexes.Length; i++)
            {
                complexes[i] = new Complex(re[i], im[i]);
            }

            return complexes;
        }

        JgsValue[] elements = Arr(name, args, index, line, col);
        var result = new Complex[elements.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            result[i] = elements[i].Type switch
            {
                JgsType.Number or JgsType.Bool => new Complex(elements[i].AsNumber, 0),
                JgsType.Complex => elements[i].AsComplex,
                _ => throw new JgsRuntimeException(line, col,
                    $"{name} expects numeric (or complex) samples, but element {i} was a {elements[i].TypeName}."),
            };
        }

        return result;
    }

    private static JgsValue FromComplexArray(Complex[] values)
    {
        if (JgsPacking.Enabled)
        {
            // All-real results pack as plain number arrays (the boxed form would be all Numbers,
            // via ComplexNum's zero-imaginary normalization); anything else packs planar.
            bool anyImaginary = false;
            foreach (Complex value in values)
            {
                if (value.Imaginary != 0)
                {
                    anyImaginary = true;
                    break;
                }
            }

            var re = JgsPacking.Allocate(values.Length);
            Span<double> reSpan = re.AsSpan();
            if (!anyImaginary)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    reSpan[i] = values[i].Real;
                }

                return JgsValue.Packed(re);
            }

            var im = JgsPacking.Allocate(values.Length);
            Span<double> imSpan = im.AsSpan();
            for (int i = 0; i < values.Length; i++)
            {
                reSpan[i] = values[i].Real;
                imSpan[i] = values[i].Imaginary;
            }

            return JgsValue.PackedComplexArray(new JgsPackedComplex(re, im));
        }

        var result = new JgsValue[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = JgsValue.ComplexNum(values[i]);
        }

        return JgsValue.Array(result);
    }

    private static Complex[] PadOrTruncate(Complex[] input, int length, string name, int line, int col)
    {
        if (length < 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a positive transform length.");
        }

        if (length == input.Length)
        {
            return input;
        }

        var resized = new Complex[length];
        System.Array.Copy(input, resized, System.Math.Min(input.Length, length));
        return resized;
    }

    /// <summary>fftshift (forward: rotate by n−⌈n/2⌉) and ifftshift (its inverse), as new arrays.</summary>
    private static JgsValue[] Rotate(JgsValue[] source, bool forward)
    {
        int n = source.Length;
        if (n == 0)
        {
            return source;
        }

        int shift = forward ? n - ((n + 1) / 2) : (n + 1) / 2;
        var result = new JgsValue[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = source[(i + n - shift) % n];
        }

        return result;
    }

    /// <summary>Numeric unpack of a whole array value: packed buffers bulk-copy, boxed arrays convert per element.</summary>
    private static double[] ToDoubles(string name, JgsValue array, int line, int col)
    {
        if (array.IsPacked)
        {
            return array.AsBuffer.AsSpan().ToArray(); // both kinds are numeric doubles (bools are 0/1)
        }

        if (array.IsPackedComplex)
        {
            // Zero-imaginary elements read as plain numbers, so an all-real spectrum unpacks fine;
            // a truly complex element gets the boxed paths' exact guidance.
            JgsPackedComplex planes = array.AsPackedComplex;
            ReadOnlySpan<double> im = planes.Im.AsSpan();
            for (int i = 0; i < im.Length; i++)
            {
                if (im[i] != 0)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{name} expects an array of numbers, but element {i} was a complex number — take abs(), real(), or imag() first.");
                }
            }

            return planes.Re.AsSpan().ToArray();
        }

        return ToDoubles(name, array.AsArray, line, col);
    }

    private static double[] ToDoubles(string name, JgsValue[] elements, int line, int col)
    {
        // Bools read as 0/1, so a mask is a numeric array: sum(mask) counts its matches.
        var result = new double[elements.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i].Type == JgsType.Complex)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name} expects an array of numbers, but element {i} was a complex number — take abs(), real(), or imag() first.");
            }

            if (elements[i].Type is not (JgsType.Number or JgsType.Bool))
            {
                throw new JgsRuntimeException(line, col, $"{name} expects an array of numbers, but element {i} was a {elements[i].TypeName}.");
            }

            result[i] = elements[i].AsNumber;
        }

        return result;
    }
}
