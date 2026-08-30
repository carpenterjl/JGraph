using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using JGraph.Api;
using JGraph.Imaging;
using JGraph.Maths;
using JGraph.Numerics;
using JGraph.Signal;
using JGraph.Signal.Rf;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Data;
using JGraph.Objects;

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
    /// <param name="dialect">The run's language variant, or null for <see cref="JgsDialect.Jgs"/>. Builtins that
    /// hand back indices (<c>find</c> and friends) report them in this dialect's index base.</param>
    public static JgsEnvironment CreateGlobals(
        JGraphScriptGlobals host, CancellationToken cancellationToken = default, JgsDialect? dialect = null)
    {
        ArgumentNullException.ThrowIfNull(host);

        dialect ??= JgsDialect.Jgs;
        var env = new JgsEnvironment();

        // One stream for the whole run, so `rng(7)` makes every later draw repeatable rather than
        // just the next one. Sparse used to keep a second, private generator; it shares this now.
        var random = new JgsRandomSource();

        // A new scope is a new console session: numeric display returns to its default precision.
        JgsNumberFormat.Reset();

        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // For verbs that hand back a handle but print nothing as a bare statement (MATLAB `figure(1)`).
        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        // --- Constants -----------------------------------------------------------------------
        env.Declare("pi", JgsValue.Number(System.Math.PI));
        env.Declare("e", JgsValue.Number(System.Math.E));
        env.Declare("inf", JgsValue.Number(double.PositiveInfinity));
        env.Declare("nan", JgsValue.Number(double.NaN));

        // --- Element-wise math ---------------------------------------------------------------
        //
        // Every one of these goes through MathX rather than Math1 (M81): the first three never leave
        // the reals for a real argument and carry a complex definition only so a complex argument has
        // somewhere to go, while asin, acos and log10 promote the moment an argument leaves their
        // domain. The rounding four apply their rule to both parts, which is MATLAB's answer.
        //
        // The trailing PackedMath op is the kernel that computes the real definition over a whole
        // buffer (M92) — the same arithmetic, without a delegate call per element. `round` names none
        // deliberately: MATLAB rounds away from zero and PackedMath.Round is the banker's rule, so
        // the two are different functions that happen to share a name.
        MathX(Define, "sin", System.Math.Sin, Always, Complex.Sin, PackedMath.UnaryOp.Sin);
        MathX(Define, "cos", System.Math.Cos, Always, Complex.Cos, PackedMath.UnaryOp.Cos);
        MathX(Define, "tan", System.Math.Tan, Always, Complex.Tan, PackedMath.UnaryOp.Tan);
        MathX(Define, "asin", System.Math.Asin, InsideUnit, ComplexAsin);
        MathX(Define, "acos", System.Math.Acos, InsideUnit, ComplexAcos);
        MathX(Define, "atan", System.Math.Atan, Always, Complex.Atan);
        MathX(Define, "log10", System.Math.Log10, NonNegative, Complex.Log10, PackedMath.UnaryOp.Log10);
        MathX(Define, "floor", System.Math.Floor, Always, static z => Componentwise(z, System.Math.Floor),
            PackedMath.UnaryOp.Floor);
        MathX(Define, "ceil", System.Math.Ceiling, Always, static z => Componentwise(z, System.Math.Ceiling),
            PackedMath.UnaryOp.Ceiling);
        MathX(Define, "round", RoundAwayFromZero, Always, static z => Componentwise(z, RoundAwayFromZero));
        MathX(Define, "sign", static x => System.Math.Sign(x), Always, ComplexSign);

        // Complex-aware elementwise functions: real input behaves exactly as before, and complex
        // input takes the complex definition (abs = magnitude, angle = phase, conj = conjugate).
        // `plain` is the same answer as `complex` with the box taken off, and only the four whose
        // complex answer is always real can supply one. It is what lets abs(F) of a spectrum stay
        // in its planes: the boxed arm minted a JgsValue per element to discover it was a number.
        void MathC(string name, Func<double, double> real, Func<Complex, JgsValue> complex,
                   PackedMath.UnaryOp? vectorOp = null, Func<Complex, double>? plain = null) =>
            Define(name, (args, line, col) => { Arity(name, args, 1, line, col); return MapComplexAware(name, args[0], real, complex, line, col, vectorOp, plain); });

        MathC("abs", System.Math.Abs, static c => JgsValue.Number(Complex.Abs(c)), PackedMath.UnaryOp.Abs,
            static c => Complex.Abs(c));
        MathC("real", static x => x, static c => JgsValue.Number(c.Real), plain: static c => c.Real);
        MathC("imag", static _ => 0, static c => JgsValue.Number(c.Imaginary), plain: static c => c.Imaginary);
        MathC("angle", static x => x >= 0 ? 0 : System.Math.PI, static c => JgsValue.Number(c.Phase),
            plain: static c => c.Phase);
        MathC("conj", static x => x, static c => JgsValue.ComplexNum(Complex.Conjugate(c)));

        // Complex-producing elementwise functions (M42): real input stays on the flat real fast
        // path as long as the answer is real; sqrt(-4) and log(-1) hand back MATLAB's complex
        // results, and complex input takes the complex definition throughout. M81 moved the
        // registration helper itself into JgsBuiltins.ComplexDomain.cs so the other three files that
        // declare a Math1 of their own could reach it, and widened the family to everything else that
        // leaves the reals.
        MathX(Define, "exp", System.Math.Exp, Always, Complex.Exp, PackedMath.UnaryOp.Exp);
        MathX(Define, "log", System.Math.Log, NonNegative, Complex.Log, PackedMath.UnaryOp.Log);
        MathX(Define, "sqrt", System.Math.Sqrt, NonNegative, Complex.Sqrt, PackedMath.UnaryOp.Sqrt);

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
            OneDimensionalTransform("fft", args, inverse: false, line, col));

        Define("ifft", (args, line, col) =>
            OneDimensionalTransform("ifft", args, inverse: true, line, col));

        Define("fftshift", (args, line, col) =>
        {
            ArityRange("fftshift", args, 1, 2, line, col);
            return Rotated("fftshift", args, forward: true, line, col);
        });

        Define("ifftshift", (args, line, col) =>
        {
            ArityRange("ifftshift", args, 1, 2, line, col);
            return Rotated("ifftshift", args, forward: false, line, col);
        });

        env.Declare("filter", JgsValue.Function(new BuiltinFunction("filter",
            (args, line, col) => FilterAnswer(args, 1, line, col)[0])
        { MultiOutput = FilterAnswer }));

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

        // Four spellings since M87: a number of seconds, a bare wait for a key, and the on/off/query
        // switch. Only the bare one needs a window.
        Define("pause", (args, line, col) => Pause(args, cancellationToken, line, col));
        RegisterWaitingBuiltins(env, cancellationToken);

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

        // Both auto-call on their bare names — 't0 = tic' stores a handle and 't = toc' stores the
        // elapsed seconds, not the functions. A bare 'tic' statement is silent, like MATLAB's.
        env.Declare("tic", JgsValue.Function(new BuiltinFunction("tic", (args, line, col) =>
        {
            Arity("tic", args, 0, line, col);
            double handle = StopwatchTicksNow();
            defaultTicHandle = handle;
            return JgsValue.Number(handle);
        })
        { AutoCallsBare = true, BindsAnsAsStatement = false }));

        env.Declare("toc", JgsValue.Function(new BuiltinFunction("toc", (args, line, col) =>
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
        })
        { AutoCallsBare = true }));

        // The four zero-argument clock readings auto-call on their bare names, like tic and toc above
        // and like every constant since M37. Without it `x = now` bound the *function* rather than the
        // time, so `datestr(now)` — the commonest date line anyone writes — failed complaining that it
        // had been handed a function. M64 found that by probing the surface it was about to build on.
        void DefineClock(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { AutoCallsBare = true }));

        DefineClock("clock", (args, line, col) =>
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

        DefineClock("now", (args, line, col) =>
        {
            Arity("now", args, 0, line, col);
            return JgsValue.Number(DateTime.Now.ToOADate() + matlabDatenumOffset);
        });

        Define("datenum", (args, line, col) =>
        {
            ArityRange("datenum", args, 1, 6, line, col);

            // A datetime is the shortest road to a serial date number, and the one a script that
            // mixes the new type with the old surface actually writes (M64).
            if (args.Count == 1 && args[0].IsDatetime)
            {
                double[] serials = System.Array.ConvertAll(TimeMs(args[0]), JgsTime.ToDatenum);
                return serials.Length == 1 ? JgsValue.Number(serials[0]) : Numbers(serials);
            }

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

            // A datetime formats through its own machinery (M64), so datestr(t) and disp(t) cannot
            // disagree about what a moment looks like.
            if (args.Count >= 1 && args[0].IsDatetime)
            {
                string shape = args.Count >= 2 ? Str("datestr", args, 1, line, col) : JgsTime.DefaultDatetimeFormat;
                var tag = new JgsTimeTag(JgsTimeKind.Datetime, shape);
                double[] moments = TimeMs(args[0]);
                if (moments.Length == 1)
                {
                    return JgsValue.Str(JgsTime.Format(moments[0], tag));
                }

                return PadIntoCharMatrix(System.Array.ConvertAll(moments, ms => JgsTime.Format(ms, tag)));
            }

            double serial = args.Count >= 1
                ? Num("datestr", args, 0, line, col)
                : DateTime.Now.ToOADate() + matlabDatenumOffset;

            double oaDate = serial - matlabDatenumOffset;
            if (double.IsNaN(oaDate) || oaDate < -657435.0 || oaDate > 2958465.99999999)
            {
                throw new JgsRuntimeException(line, col, "datestr: the serial date number is out of range.");
            }

            DateTime moment = DateTime.FromOADate(oaDate);
            // Through the same token translation the datetime branch above uses (M64). Without it the
            // one name read a format two ways: 'uuuu-MM-dd' was a year for a datetime and the four
            // literal letters "uuuu" for a serial number. The translation leaves the .NET tokens this
            // has always accepted alone, so nothing that worked stops working.
            string format = args.Count >= 2
                ? JgsTime.ToNetFormat(Str("datestr", args, 1, line, col))
                : "dd-MMM-yyyy HH:mm:ss";
            try
            {
                return JgsValue.Str(moment.ToString(format, CultureInfo.InvariantCulture));
            }
            catch (FormatException)
            {
                throw new JgsRuntimeException(line, col, $"datestr: '{format}' is not a valid .NET date format string.");
            }
        });

        // datetime itself is registered by RegisterTimeBuiltins (M64), which replaced the placeholder
        // that used to live here and answer with a char row of the current time.
        DefineClock("date", (args, line, col) =>
        {
            Arity("date", args, 0, line, col);
            return JgsValue.Str(DateTime.Now.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture));
        });

        DefineClock("time", (args, line, col) =>
        {
            Arity("time", args, 0, line, col);
            return JgsValue.Number(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
        });

        Define("mod", (args, line, col) =>
        {
            Arity("mod", args, 2, line, col);
            double divisor = Num("mod", args, 1, line, col);
            // MATLAB mod: the result takes the divisor's sign (unlike C's %).
            return MapNumeric("mod", args[0], x => ScalarMod(x, divisor), line, col);
        });

        Define("size", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "size needs a value to measure.");
            }

            int[] dims = SizeDims(args[0]);

            // Dimensions past the value's rank are 1, exactly as in MATLAB.
            double Extent(double dim) => dim >= 1 && dim <= dims.Length ? dims[(int)dim - 1] : 1;

            if (args.Count == 1)
            {
                return JgsValue.Array(Array.ConvertAll(dims, static d => JgsValue.Number(d)));
            }

            // size(A, dim), size(A, [d1 d2]) and size(A, d1, d2) are three spellings of one question,
            // and the answer's shape follows the question's: one number in, one number out.
            var asked = new List<double>();
            for (int i = 1; i < args.Count; i++)
            {
                // A dimension outside the value's rank answers 1 rather than refusing, which is what
                // MATLAB does past the rank and what JGS callers have always got at the low end. The
                // leniency is deliberate: JGS is frozen, so a call that worked must keep working.
                foreach (double dim in NumericVector("size", args[i], line, col))
                {
                    asked.Add(dim);
                }
            }

            return asked.Count == 1
                ? JgsValue.Number(Extent(asked[0]))
                : Numbers([.. asked.Select(Extent)]);
        });

        // height and width are how a MATLAB script asks a table how big it is, and since R2020b
        // they answer for an ordinary array too — the first and second dimensions, nothing more.
        // A table's second dimension is its variable count, which is why size had to learn tables
        // at the same time: numel(T.SomeColumn) was the workaround for both.
        Define("height", (args, line, col) =>
        {
            Arity("height", args, 1, line, col);
            return JgsValue.Number(SizeDims(args[0])[0]);
        });

        Define("width", (args, line, col) =>
        {
            Arity("width", args, 1, line, col);
            int[] dims = SizeDims(args[0]);
            return JgsValue.Number(dims.Length > 1 ? dims[1] : 1);
        });

        Define("isempty", (args, line, col) =>
        {
            Arity("isempty", args, 1, line, col);
            return JgsValue.Bool(args[0].Type switch
            {
                JgsType.Null => true,
                JgsType.Array => args[0].ArrayLength == 0,
                JgsType.Cell => args[0].AsCell.Length == 0,
                // A struct with no fields is still one element, so it is not empty; a 0-by-0 struct
                // array is (M65). Before the type had a size there was nothing else to ask.
                JgsType.Struct => args[0].AsStructArray.Length == 0,
                JgsType.String => args[0].AsString.Length == 0,
                JgsType.Table => args[0].AsTable.RowCount == 0,
                JgsType.Sparse => args[0].AsSparse.Rows == 0 || args[0].AsSparse.Cols == 0,
                _ => false,
            });
        });

        Define("disp", (args, line, col) =>
        {
            Arity("disp", args, 1, line, col);

            // A char matrix shows its rows, one per line, the way MATLAB's disp does and the way a
            // char row here already showed its bare text — the bracketed one-liner Display() gives is
            // for a value inside a larger message, not for the whole of what disp was asked to print.
            host.print(args[0].IsCharMatrix
                ? string.Join(System.Environment.NewLine, args[0].CharMatrixRows())
                : args[0].Display());
            return JgsValue.Null;
        });

        // --- Console and folder commands ------------------------------------------------------
        Define("clc", (args, line, col) =>
        {
            Arity("clc", args, 0, line, col);
            host.ClearOutput();
            return JgsValue.Null;
        });

        Define("format", (args, line, col) =>
        {
            ArityRange("format", args, 0, 1, line, col);
            if (args.Count == 0)
            {
                JgsNumberFormat.Reset();
                return JgsValue.Null;
            }

            string word = Str("format", args, 0, line, col).ToLowerInvariant();
            switch (word)
            {
                case "short" or "shortg":
                    JgsNumberFormat.Current = JgsNumberFormat.Mode.Short;
                    break;
                case "long" or "longg":
                    JgsNumberFormat.Current = JgsNumberFormat.Mode.Long;
                    break;
                case "shorte":
                    JgsNumberFormat.Current = JgsNumberFormat.Mode.ShortE;
                    break;
                case "longe":
                    JgsNumberFormat.Current = JgsNumberFormat.Mode.LongE;
                    break;
                case "compact" or "loose":
                    // The console already writes one line per result and never pads with blank
                    // lines, so the spacing words are accepted and change nothing.
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"format does not recognize '{word}'. Try short, long, shortE, longE, compact, or loose.");
            }

            return JgsValue.Null;
        });

        Define("help", (args, line, col) =>
        {
            ArityRange("help", args, 0, 1, line, col);
            if (args.Count == 0)
            {
                host.print("Type help followed by a name, like: help plot");
                host.print("Functions: " + string.Join(", ",
                    JgsBuiltinCatalog.All.Select(static info => info.Name)));
                return JgsValue.Null;
            }

            string name = Str("help", args, 0, line, col);
            JgsBuiltinInfo? info = JgsBuiltinCatalog.Find(name);
            if (info is not null)
            {
                host.print(info.Signature);
                host.print("  " + info.Summary);
                return JgsValue.Null;
            }

            IReadOnlyList<string> keywords = dialect.IsMatlab
                ? JgsBuiltinCatalog.MatlabKeywords
                : JgsBuiltinCatalog.Keywords;
            if (keywords.Contains(name, StringComparer.Ordinal))
            {
                host.print($"'{name}' is a language keyword.");
                return JgsValue.Null;
            }

            host.print($"No help found for '{name}'.");
            return JgsValue.Null;
        });

        Define("dir", (args, line, col) =>
        {
            if (args.Count > 1)
            {
                throw new JgsRuntimeException(line, col, "dir(pattern) expects at most one argument.");
            }

            return ListDirectory(host, args.Count == 1 ? Str("dir", args, 0, line, col) : string.Empty, line, col);
        });

        // path itself is declared with the search-path builtins in RegisterPathBuiltins — the search
        // path is interpreter state, and the plain working-directory answer that used to live here
        // was shadowed by that declaration in every real session.

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
                // MATLAB: the largest dimension (zero for anything empty), not the element count.
                // The pre-shape nested form keeps its JGS reading — a list's length is its item count.
                JgsType.Array when args[0].ArrayLength == 0 => JgsValue.Number(0),
                JgsType.Array when JgsMatrix.IsNested(args[0]) => JgsValue.Number(args[0].ArrayLength),
                JgsType.Array => JgsValue.Number(JgsMatrix.DimsOf(args[0]).Max()),
                JgsType.Cell => JgsValue.Number(args[0].AsCell.Length),
                JgsType.String => JgsValue.Number(args[0].AsString.Length),
                // A struct is a 1-by-1 struct array (M65), so it has a length like anything else.
                JgsType.Struct => JgsValue.Number(System.Math.Max(args[0].Rows, args[0].Cols)),
                // A scalar is 1-by-1, so its longest dimension is 1 — the same answer size(7) gives.
                JgsType.Number or JgsType.Bool or JgsType.Complex => JgsValue.Number(1),
                _ => throw new JgsRuntimeException(line, col, $"length expects an array, cell, or string, but got a {args[0].TypeName}."),
            };
        });

        Define("sum", (args, line, col) => TryReduceImage("sum", args, line, col, out JgsValue imageSum)
            ? imageSum
            : TryPackedSpan(args, out NumericBuffer packedSum)
                ? JgsValue.Number(PackedMath.Sum(packedSum))
                : Reduce("sum", args, line, col, (acc, v) => acc + v, 0.0));
        Define("mean", (args, line, col) =>
        {
            if (TryReduceImage("mean", args, line, col, out JgsValue imageMean))
            {
                return imageMean;
            }

            if (TryPackedSpan(args, out NumericBuffer packed))
            {
                // An average over nothing is NaN, not a refusal (M96b): mean([]) is NaN in MATLAB,
                // and a script that filtered its data down to nothing depends on getting a number
                // back rather than an error. Every reduction over nothing answers the same way its
                // fold's identity does — 0 for a sum, 1 for a product, NaN where there is no
                // sensible average.
                if (packed.Length == 0)
                {
                    return JgsValue.Number(double.NaN);
                }

                return JgsValue.Number(PackedMath.Sum(packed) / packed.Length);
            }

            double[] values = ArrayOfNumbers("mean", args, line, col);
            if (values.Length == 0)
            {
                return JgsValue.Number(double.NaN);
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
                JgsType.Cell => JgsValue.Number(args[0].AsCell.Length),
                JgsType.String => JgsValue.Number(args[0].AsString.Length),
                JgsType.Image => JgsValue.Number(args[0].AsImage.SampleCount),
                JgsType.Sparse => JgsValue.Number((double)args[0].AsSparse.Rows * args[0].AsSparse.Cols),
                JgsType.Struct => JgsValue.Number(args[0].AsStructArray.Length), // a struct is 1-by-1 (M65)
                JgsType.Number or JgsType.Bool or JgsType.Complex => JgsValue.Number(1), // a scalar is one element
                _ => throw new JgsRuntimeException(line, col, $"numel expects an array, cell, or string, but got a {args[0].TypeName}."),
            };
        });

        // --- Statistics ----------------------------------------------------------------------
        Define("std", (args, line, col) => JgsValue.Number(System.Math.Sqrt(SampleVariance("std", args, line, col))));
        Define("variance", (args, line, col) => JgsValue.Number(SampleVariance("variance", args, line, col)));

        // var is MATLAB's spelling of variance, and it takes the same weight. Both names are here
        // rather than one aliasing the other so the reduction wrapper can find each of them by name.
        Define("var", (args, line, col) => JgsValue.Number(SampleVariance("var", args, line, col)));
        // The median and the mode of nothing are NaN, for the same reason mean([]) is (M96b).
        Define("median", (args, line, col) => EmptyOrNumber(
            ArrayOfNumbers("median", args, line, col), JgsStdlib.Median));
        Define("mode", (args, line, col) => EmptyOrNumber(
            ArrayOfNumbers("mode", args, line, col), JgsStdlib.Mode));

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
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "sort needs an array.");
            }

            ParsedArgs parsed = SortOptions.Parse(args, 1, line, col);
            bool descending = parsed.OneOf("ascend", "ascend", "descend", "asc", "desc") is "descend" or "desc";
            string missing = parsed.Word("MissingPlacement", "auto", "auto", "first", "last");
            string comparison = parsed.Word("ComparisonMethod", "auto", "auto", "real", "abs");
            return JgsValue.Array(
                SortElements(Arr("sort", parsed.Positional, 0, line, col), descending, missing, comparison, line, col)
                ?? throw new JgsRuntimeException(line, col, "sort needs an array of all numbers or all strings."));
        });

        Define("unique", (args, line, col) => UniqueParts(args, dialect, 1, line, col)[0]);

        Define("find", (args, line, col) =>
        {
            ArityRange("find", args, 1, dialect.IsMatlab ? 3 : 2, line, col);
            if (args[0].Type == JgsType.Sparse)
            {
                return SparseFind(args[0].AsSparse, 1, dialect, line, col)[0];
            }

            (int origin, int? wanted, bool fromEnd) = FindLimit("find", args, dialect, line, col);

            if (args[0].Type == JgsType.Array && args[0].IsPacked)
            {
                // Nonzero is truthy for numbers and bools alike (NaN != 0 is true) — same as IsTruthy.
                NumericBuffer haystack = args[0].AsBuffer;
                ReadOnlySpan<double> span = haystack.AsSpan();

                // Unlimited is the form nearly every script writes, and it is the one that can be
                // counted first and filled once (M92): a List of a few million positions spends most
                // of its time doubling and then hands over a copy of itself on the way out.
                if (wanted is null)
                {
                    NumericBuffer positions = JgsPacking.Allocate(PackedMath.CountNonZero(haystack));
                    Span<double> into = positions.AsSpan();
                    int next = 0;
                    for (int i = 0; i < span.Length; i++)
                    {
                        if (span[i] != 0)
                        {
                            into[next++] = i + origin;
                        }
                    }

                    GC.KeepAlive(haystack);
                    return FoundIndices(JgsValue.Packed(positions), args[0]);
                }

                var found = new List<double>();
                for (int i = 0; i < span.Length; i++)
                {
                    if (span[i] != 0)
                    {
                        found.Add(i + origin);
                    }
                }

                return FoundIndices(
                    NumbersCopy(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(
                        Limited(found, wanted, fromEnd))),
                    args[0]);
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

            return FoundIndices(JgsValue.Array([.. Limited(indices, wanted, fromEnd)]), args[0]);
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

            // MATLAB's reverse is a text function — reverse('abc') is 'cba' — and flip is the one
            // for arrays. JGS had only the array form, so text is the addition (M63) and the array
            // form is kept because JGS's surface is frozen and scripts already call it.
            if (args[0].Type == JgsType.String)
            {
                char[] letters = args[0].AsString.ToCharArray();
                System.Array.Reverse(letters);
                return JgsValue.Str(new string(letters));
            }

            var reversed = (JgsValue[])Arr("reverse", args, 0, line, col).Clone();
            System.Array.Reverse(reversed);
            return JgsValue.Array(reversed);
        });

        Define("isnan", (args, line, col) =>
        {
            Arity("isnan", args, 1, line, col);
            return MapToBool("isnan", args[0], static x => double.IsNaN(x), line, col,
                static c => double.IsNaN(c.Real) || double.IsNaN(c.Imaginary));
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
                // MATLAB flattens array arguments and cycles the format over them; JGS stays strict.
                return JgsValue.Str(dialect!.IsMatlab
                    ? JgsSprintf.FormatMatlab(format, args.Skip(1).ToArray())
                    : JgsSprintf.Format(format, args.Skip(1).ToArray()));
            }
            catch (FormatException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }
        });

        DefineSilent("fprintf", (args, line, col) =>
        {
            if (args.Count < 1)
            {
                throw new JgsRuntimeException(line, col, "fprintf expects a format string first.");
            }

            // A leading number is a file id: 1 = the console, 2 = the error console, 3+ = fopen.
            int start = 0;
            int fid = 1;
            if (args[0].Type is JgsType.Number or JgsType.Bool)
            {
                fid = (int)args[0].AsNumber;
                start = 1;
                if (args.Count < 2)
                {
                    throw new JgsRuntimeException(line, col, "fprintf expects a format string after the file id.");
                }
            }

            string format = Str("fprintf", args, start, line, col);
            string text;
            try
            {
                // MATLAB fprintf writes exactly what the format says — the newline comes from the
                // format's own \n, so this goes through the raw (no-newline) output seam. Array
                // arguments flatten and the format cycles under MATLAB, exactly like sprintf.
                text = dialect!.IsMatlab
                    ? JgsSprintf.FormatMatlab(format, args.Skip(start + 1).ToArray())
                    : JgsSprintf.Format(format, args.Skip(start + 1).ToArray());
            }
            catch (FormatException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }

            int written;
            switch (fid)
            {
                case 1:
                    host.WriteOut(text);
                    written = System.Text.Encoding.UTF8.GetByteCount(text);
                    break;
                case 2:
                    host.WriteErr(text);
                    written = System.Text.Encoding.UTF8.GetByteCount(text);
                    break;
                default:
                    JGraphScriptGlobals.FileEntry entry = host.OpenFileFor(fid)
                        ?? throw new JgsRuntimeException(line, col, $"fprintf: {fid} is not an open file.");
                    byte[] bytes = entry.Encoding.GetBytes(text);
                    entry.Stream.Write(bytes, 0, bytes.Length);
                    written = bytes.Length;
                    break;
            }

            // nbytes = fprintf(...) is a documented form; a bare fprintf still prints nothing extra,
            // which is what BindsAnsAsStatement being false is for.
            return JgsValue.Number(written);
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
            ArityRange("split", args, 1, 2, line, col);

            // MATLAB spells two different verbs the same way, and the first argument says which: text
            // splits on a separator, a calendarDuration breaks into its calendar units (M82). Asking
            // here rather than registering a second `split` is the difference between the two
            // meanings sharing a name and one of them quietly replacing the other.
            if (args.Count == 2 && IsCalendarDuration(args[0]))
            {
                return SplitCalendar(args[0], args[1], line, col);
            }

            string[] parts;
            if (args.Count == 1)
            {
                // MATLAB's default: split on runs of whitespace.
                parts = Str("split", args, 0, line, col).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                string separator = Str("split", args, 1, line, col);
                if (separator.Length == 0)
                {
                    throw new JgsRuntimeException(line, col, "split separator must not be empty.");
                }

                parts = Str("split", args, 0, line, col).Split(separator, StringSplitOptions.None);
            }

            var result = new JgsValue[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                result[i] = JgsValue.Str(parts[i]);
            }

            JgsValue column = JgsValue.Array(result);
            if (result.Length > 1 && dialect!.IsMatlab)
            {
                column.Reshape(result.Length, 1); // split answers are columns in MATLAB; JGS keeps the flat list
            }

            return column;
        });

        Define("join", (args, line, col) =>
        {
            ArityRange("join", args, 1, 2, line, col);
            string separator = args.Count == 2 ? Str("join", args, 1, line, col) : " ";

            // A shaped string array joins along its rows (MATLAB's dim 2): one string per row.
            JgsValue input = args[0];
            if (input.Type == JgsType.Array && input.Rows > 1 && input.Cols > 1)
            {
                var rows = new JgsValue[input.Rows];
                for (int r = 0; r < input.Rows; r++)
                {
                    var cells = new string[input.Cols];
                    for (int c = 0; c < input.Cols; c++)
                    {
                        cells[c] = input.ElementAt(r + (c * input.Rows)).Display();
                    }

                    rows[r] = JgsValue.Str(string.Join(separator, cells));
                }

                JgsValue column = JgsValue.Array(rows);
                column.Reshape(rows.Length, 1);
                return column;
            }

            JgsValue[] parts = Arr("join", args, 0, line, col);
            return JgsValue.Str(string.Join(separator, parts.Select(static p => p.Display())));
        });

        // MATLAB spells these with the interior capital, and one canonical spelling beats two.
        Define("startsWith", (args, line, col) =>
        {
            Arity("startsWith", args, 2, line, col);
            return JgsValue.Bool(Str("startsWith", args, 0, line, col).StartsWith(Str("startsWith", args, 1, line, col), StringComparison.Ordinal));
        });

        Define("endsWith", (args, line, col) =>
        {
            Arity("endsWith", args, 2, line, col);
            return JgsValue.Bool(Str("endsWith", args, 0, line, col).EndsWith(Str("endsWith", args, 1, line, col), StringComparison.Ordinal));
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
        DefineSilent("figure", (args, line, col) =>
        {
            // figure(n) selects or makes figure n; a leading number is the only positional argument,
            // and everything after it is name/value. MATLAB writes the whole figure surface this way
            // — figure('Position', [...], 'Name', 'x') — so the pairs are handed to the same property
            // table set() writes through rather than to a hand-kept list of accepted names, which is
            // what makes every property the figure already answers to settable at construction.
            int first = 0;
            int? requested = null;
            if (args.Count > 0 && args[0].Type != JgsType.String)
            {
                requested = Count("figure", args, 0, line, col);
                if (requested < 1)
                {
                    throw new JgsRuntimeException(line, col, "Figure numbers start at 1.");
                }

                first = 1;
            }

            if ((args.Count - first) % 2 != 0)
            {
                throw new JgsRuntimeException(
                    line, col, "figure: every property after the figure number needs a value.");
            }

            if (requested is { } number)
            {
                JG.Figure(number);
            }
            else
            {
                JG.Figure();
            }

            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(JG.CurrentFigure);
            for (int i = first; i < args.Count; i += 2)
            {
                JgsGraphicsProperties.Set(entry, Str("figure", args, i, line, col), args[i + 1], line, col);
            }

            return JgsValue.Number(JG.CurrentFigureNumber);
        });
        DefineSilent("subplot", (args, line, col) =>
        {
            ArityRange("subplot", args, 3, 4, line, col);
            if (args.Count == 4)
            {
                // 'replace' and 'align' name how the panel is made, and this build makes every panel
                // the same way — a fresh axes on an aligned grid. Accepting the two words lets a
                // ported script through; any other word still refuses by name, which is the house
                // style rather than a silent shrug.
                string how = Str("subplot", args, 3, line, col);
                if (!how.Equals("replace", StringComparison.OrdinalIgnoreCase)
                    && !how.Equals("align", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"subplot takes 'replace' or 'align', but got '{how}'.");
                }
            }

            AxesModel axes = JG.Subplot(
                Count("subplot", args, 0, line, col),
                Count("subplot", args, 1, line, col),
                Count("subplot", args, 2, line, col));
            return JgsHandleRegistry.For(axes);
        });

        // --- Tiled layouts (M43, made an object in M80) --------------------------------------------
        //
        // Until M80 this was three integers and a flag in this closure, which is why a script could
        // not name the layout: t.TileSpacing, nexttile(span) and tiledlayout(parent, …) all need a t.
        // The state now lives on the figure, and these two verbs are the doors to it.
        int tiledCursor = 0;
        DefineSilent("tiledlayout", (args, line, col) =>
        {
            (FigureModel figure, IReadOnlyList<JgsValue> rest) = PeelLayoutParent(args);
            var layout = new TiledLayoutModel();

            int given = 0;
            if (rest.Count > 0 && rest[0].Type == JgsType.String
                && !TiledLayoutOptionNames.Contains(rest[0].AsString, StringComparer.OrdinalIgnoreCase))
            {
                // tiledlayout('flow') lets the layout choose its own grid as tiles are asked for. A
                // fixed grid has to be chosen up front here, so 'flow' starts at one tile and
                // nexttile grows it — the same tiles in the same order, laid out once more often.
                string word = Str("tiledlayout", rest, 0, line, col);
                if (!word.Equals("flow", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"tiledlayout takes a row and column count, or 'flow', but got '{word}'.");
                }

                layout.Flow = true;
                given = 1;
            }
            else if (rest.Count >= 2 && rest[0].Type != JgsType.String)
            {
                layout.Rows = Count("tiledlayout", rest, 0, line, col);
                layout.Columns = Count("tiledlayout", rest, 1, line, col);
                given = 2;
            }
            else if (rest.Count == 1)
            {
                throw new JgsRuntimeException(line, col,
                    "tiledlayout takes a row and column count, or 'flow'.");
            }

            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(layout);
            foreach ((string name, JgsValue value) in Pairs("tiledlayout", rest, given, line, col))
            {
                JgsGraphicsProperties.Set(entry, name, value, line, col);
            }

            figure.TiledLayout = layout;
            tiledCursor = 0;
            return JgsHandleRegistry.For(layout);
        });
        // `ax = nexttile` with no brackets is the ordinary way to write it, so a bare name has to be
        // the tile it hands out rather than the function itself — the rule bubblesize follows, and
        // the one this verb did not need until M80 gave it something to hand back.
        env.Declare("nexttile", JgsValue.Function(new BuiltinFunction("nexttile", (args, line, col) =>
        {
            (TiledLayoutModel? named, IReadOnlyList<JgsValue> rest) = PeelLayout(args);
            TiledLayoutModel layout = named ?? CurrentLayout();
            if (named is not null && !ReferenceEquals(named, JG.CurrentFigure.TiledLayout))
            {
                throw new JgsRuntimeException(line, col,
                    "nexttile(t, …) names a layout that is not the current figure's.");
            }

            ArityRange("nexttile", rest, 0, 2, line, col);

            // A span is a pair; a location is one number. Two arguments are always a location and a
            // span, which is the only reading that tells nexttile(2) from nexttile([1 2]).
            int rowSpan = 1;
            int columnSpan = 1;
            int? location = null;
            for (int i = 0; i < rest.Count; i++)
            {
                double[] given = ToDoubles("nexttile", rest[i], line, col);
                if (given.Length == 2 && (i > 0 || rest.Count == 1))
                {
                    rowSpan = WholeNumber("nexttile: span", given[0], line, col);
                    columnSpan = WholeNumber("nexttile: span", given[1], line, col);
                }
                else if (given.Length == 1)
                {
                    location = WholeNumber("nexttile: tilelocation", given[0], line, col);
                }
                else
                {
                    throw new JgsRuntimeException(line, col,
                        "nexttile takes a tile number, a [rows cols] span, or both.");
                }
            }

            if (location is { } asked)
            {
                tiledCursor = asked;
            }
            else if (layout.Flow)
            {
                // A flowing layout never wraps: it grows until it holds the tile just asked for. The
                // fixed grid's wrap would pin the cursor at 1 while a 1-by-1 grid stayed 1-by-1, so
                // four tiles would be one tile asked for four times.
                tiledCursor++;
                layout.GrowToHold(tiledCursor + ((rowSpan * columnSpan) - 1));
            }
            else
            {
                tiledCursor = (tiledCursor % layout.TileCount) + 1;
            }

            AxesModel placed = JG.NewAxes();
            placed.LayoutTile = tiledCursor;
            placed.LayoutRowSpan = rowSpan;
            placed.LayoutColumnSpan = columnSpan;
            layout.Adopt(placed);

            // Every tile already handed out belongs to the grid as it is now rather than as it was
            // when that tile was made. Laying them all out again is what makes a flowing layout flow
            // instead of pile up, and costs nothing on a fixed one.
            layout.Arrange();
            return JgsHandleRegistry.For(placed);
        })
        { BindsAnsAsStatement = false, AutoCallsBare = true }));

        // axis: the aspect/limits words plus the [xmin xmax ymin ymax] vector form.
        Define("axis", (args, line, col) =>
        {
            ArityRange("axis", args, 0, 1, line, col);
            if (args.Count == 1 && args[0].Type == JgsType.String)
            {
                switch (args[0].AsString)
                {
                    case "image" or "equal" or "square":
                        JG.Gca().EqualAspect = true;
                        break;
                    case "normal":
                        JG.Gca().EqualAspect = false;
                        break;
                    // tight and padded are the limit-fitting policies M73 made real; they land on
                    // every ruler so the whole box tightens at once, as MATLAB's word does.
                    case "tight":
                        SetLimitMethod(JG.Gca(), Core.Model.LimitMethod.Tight);
                        break;
                    case "padded":
                        SetLimitMethod(JG.Gca(), Core.Model.LimitMethod.Padded);
                        break;
                    case "auto" or "on" or "off" or "ij" or "xy" or "manual":
                        break; // accepted; auto limits and visible frames are already the defaults

                    // vis3d stops MATLAB's box from being refitted as the camera turns. This
                    // projection refits every frame by design, so the word is accepted and the
                    // divergence is recorded rather than pretended away.
                    case "vis3d":
                        break;
                    default:
                        throw new JgsRuntimeException(line, col, $"axis: unknown option '{args[0].AsString}'.");
                }

                return JgsValue.Null;
            }

            if (args.Count == 1)
            {
                double[] limits = ToDoubles("axis", args[0], line, col);
                if (limits.Length != 4)
                {
                    throw new JgsRuntimeException(line, col, "axis expects [xmin xmax ymin ymax].");
                }

                JG.XLim(limits[0], limits[1]);
                JG.YLim(limits[2], limits[3]);
            }

            return JgsValue.Null;
        });

        // shading: MATLAB drives this through the surface's FaceColor and EdgeColor, and so does
        // JGraph -- 'faceted' is flat faces with grid lines, 'flat' drops the lines, and 'interp'
        // additionally colors each corner and interpolates between them. A mesh keeps its lines
        // whatever the mode, since without them there would be nothing left to draw.
        DefineSilent("shading", (args, line, col) =>
        {
            ArityRange("shading", args, 0, 1, line, col);
            string mode = args.Count == 1 ? args[0].AsString.ToLowerInvariant() : "faceted";
            SurfaceShading shading = mode switch
            {
                "faceted" or "flat" => SurfaceShading.Flat,
                "interp" => SurfaceShading.Interp,
                _ => throw new JgsRuntimeException(line, col, $"shading: unknown mode '{mode}'."),
            };

            foreach (SurfacePlot surface in JG.Gca().Plots.OfType<SurfacePlot>())
            {
                surface.Shading = shading;
                if (surface.Style == SurfaceStyle.Wireframe)
                {
                    continue;
                }

                surface.Style = mode == "faceted" ? SurfaceStyle.FilledWithWireframe : SurfaceStyle.Filled;
            }

            return JgsValue.Null;
        });

        // lighting: MATLAB's per-surface FaceLighting. 'none' opts a surface out; the other two decide
        // whether a facet takes one normal or interpolates its corners'. Nothing shows until a light
        // exists -- as in MATLAB, where a surf is flat colormap color until then.
        DefineSilent("lighting", (args, line, col) =>
        {
            ArityRange("lighting", args, 0, 1, line, col);
            string mode = args.Count == 1 ? args[0].AsString.ToLowerInvariant() : "flat";
            SurfaceLighting lighting = mode switch
            {
                "none" => SurfaceLighting.None,
                "flat" => SurfaceLighting.Flat,

                // MATLAB dropped Phong shading but still takes the word; it maps onto gouraud.
                "gouraud" or "phong" => SurfaceLighting.Gouraud,
                _ => throw new JgsRuntimeException(line, col, $"lighting: unknown mode '{mode}'."),
            };

            // Every lit object in the axes, not every surface: M72 gave patches the same shading, and
            // a verb that reached only one of the two classes is how an isosurface stayed flat while
            // the surf beside it lit up.
            foreach (ILitObject lit in JG.Gca().Plots.OfType<ILitObject>())
            {
                lit.FaceLighting = lighting;
            }

            return JgsValue.Null;
        });

        // material: the five reflectance coefficients, by preset name or as MATLAB's vector.
        DefineSilent("material", (args, line, col) =>
        {
            ArityRange("material", args, 0, 1, line, col);
            LightingModel material;
            if (args.Count == 0 || args[0].Type == JgsType.String)
            {
                string name = args.Count == 0 ? "default" : args[0].AsString;
                if (!LightingModel.TryGetByName(name, out material))
                {
                    throw new JgsRuntimeException(
                        line, col, $"material: unknown preset '{name}'. Use shiny, dull, metal, or default.");
                }
            }
            else
            {
                double[] v = ToDoubles("material", args[0], line, col);
                if (v.Length is < 3 or > 5)
                {
                    throw new JgsRuntimeException(
                        line,
                        col,
                        "material expects [ka kd ks], optionally with the specular exponent and color reflectance.");
                }

                LightingModel fallback = LightingModel.Default;
                material = new LightingModel(
                    v[0],
                    v[1],
                    v[2],
                    v.Length > 3 ? v[3] : fallback.SpecularExponent,
                    v.Length > 4 ? v[4] : fallback.SpecularColorReflectance);
            }

            foreach (ILitObject lit in JG.Gca().Plots.OfType<ILitObject>())
            {
                lit.Material = material;
            }

            return JgsValue.Null;
        });

        DefineSilent("light", OnNamedAxes((args, line, col) =>
        {
            var light = new LightModel();
            ApplyLightOptions("light", light, args, 0, line, col);
            JG.Gca().Lights.Add(light);
            return JgsHandleRegistry.For(light);
        }));

        // lightangle(az, el) places a light on the same spherical convention view() uses, so the two
        // read the same way: azimuth about the vertical axis, elevation toward the viewer.
        DefineSilent("lightangle", (args, line, col) =>
        {
            ArityRange("lightangle", args, 2, 2, line, col);
            double az = NumOf("lightangle: az", args[0], line, col) * System.Math.PI / 180;
            double el = NumOf("lightangle: el", args[1], line, col) * System.Math.PI / 180;
            JG.Gca().Lights.Add(new LightModel
            {
                Name = "Lightangle",
                Position = new Vector3D(
                    System.Math.Cos(el) * System.Math.Sin(az),
                    -System.Math.Cos(el) * System.Math.Cos(az),
                    System.Math.Sin(el)),
            });

            return JgsValue.Null;
        });

        // camlight: a light positioned relative to the camera -- 30 degrees right and up by default,
        // headlight for straight down the view axis. One documented divergence from MATLAB, which
        // freezes the world position at the call and so leaves its highlight behind on the first
        // drag: this light follows the camera, which is the useful reading for a figure you rotate.
        DefineSilent("camlight", (args, line, col) =>
        {
            ArityRange("camlight", args, 0, 3, line, col);
            double az = 30;
            double el = 30;
            var style = LightStyle.Infinite;
            int i = 0;

            if (i < args.Count && args[i].Type != JgsType.String)
            {
                if (args.Count - i < 2)
                {
                    throw new JgsRuntimeException(line, col, "camlight expects camlight(az, el).");
                }

                az = NumOf("camlight: az", args[i], line, col);
                el = NumOf("camlight: el", args[i + 1], line, col);
                i += 2;
            }
            else if (i < args.Count && !IsLightStyleWord(args[i].AsString))
            {
                switch (args[i].AsString.ToLowerInvariant())
                {
                    case "right":
                        break;
                    case "left":
                        az = -30;
                        break;
                    case "headlight":
                        az = 0;
                        el = 0;
                        break;
                    default:
                        throw new JgsRuntimeException(
                            line, col, $"camlight: unknown position '{args[i].AsString}'. Use right, left, or headlight.");
                }

                i++;
            }

            if (i < args.Count)
            {
                style = ParseLightStyle("camlight", args[i], line, col);
                i++;
            }

            if (i != args.Count)
            {
                throw new JgsRuntimeException(line, col, "camlight: too many arguments.");
            }

            JG.Gca().Lights.Add(new LightModel
            {
                Name = "Camlight",
                FollowsCamera = true,
                Style = style,
                Position = CameraLightPosition(az, el),
            });

            return JgsValue.Null;
        });

        DefineSilent("rotate3d", (args, line, col) =>
        {
            ArityRange("rotate3d", args, 0, 1, line, col);
            return JgsValue.Null;
        });

        Define("close", (args, line, col) =>
        {
            JgsValue answer = CloseFigures(host, args, line, col);

            // Closing retires a figure, so every handle into it now names nothing. Letting go of them
            // here is what stops a long console session from holding on to every figure it ever drew.
            JgsHandleRegistry.DropUnreachable();
            return answer;
        });

        JgsValue CloseFigures(JGraphScriptGlobals graphicsHost, IReadOnlyList<JgsValue> args, int line, int col)
        {
            ArityRange("close", args, 0, 2, line, col);

            // A trailing 'force' skips the figure's CloseRequestFcn — close(fig, 'force'),
            // close all force. Without it, a figure that was given one is asked, not closed: the
            // callback closes (closereq, delete) or, by returning without either, keeps the window.
            bool force = false;
            if (args.Count > 0 && args[^1].Type == JgsType.String
                && args[^1].AsString.Equals("force", StringComparison.OrdinalIgnoreCase))
            {
                force = true;
                args = [.. args.Take(args.Count - 1)];
            }

            void CloseOne(int number)
            {
                if (!force
                    && JG.TryGetFigure(number, out FigureModel figure)
                    && JgsHandleRegistry.TryGetEntry(figure, out JgsHandleEntry? entry)
                    && entry.CloseRequestFcn is { Type: JgsType.Function }
                    && JgsCallbackDispatcher.Current is { } dispatcher)
                {
                    dispatcher.FireCloseRequest(figure);
                    return;
                }

                graphicsHost.CloseFigure(number);
            }

            if (args.Count == 0)
            {
                // MATLAB closes the current figure; with none open there is nothing to do.
                if (JG.FigureNumbers.Count > 0)
                {
                    CloseOne(JG.CurrentFigureNumber);
                }

                return JgsValue.Null;
            }

            if (args[0].Type == JgsType.String)
            {
                string what = Str("close", args, 0, line, col);
                if (!what.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"close does not understand '{what}' — use close, close(n), close all, or close all force.");
                }

                foreach (int number in JG.FigureNumbers)
                {
                    CloseOne(number);
                }

                return JgsValue.Null;
            }

            Arity("close", args, 1, line, col);
            int target = Count("close", args, 0, line, col);
            if (!JG.TryGetFigure(target, out _))
            {
                throw new JgsRuntimeException(line, col, $"There is no figure {target} to close.");
            }

            CloseOne(target);
            return JgsValue.Null;
        }

        // The default close a CloseRequestFcn opts back into: deletes the figure whose callback is
        // running, or the current figure when called outside one — never asking CloseRequestFcn
        // again, or a callback whose body is closereq would ask itself forever. AutoCallsBare,
        // because its natural spelling is the bare word — @(src, event) closereq — and a bare name
        // in expression position is otherwise the function rather than a call of it.
        env.Declare("closereq", JgsValue.Function(new BuiltinFunction("closereq", (args, line, col) =>
        {
            Arity("closereq", args, 0, line, col);
            FigureModel? figure = FigureOf(JgsGraphicsCallbackState.CallbackObject);
            int number = figure is not null ? JG.GetFigureNumber(figure) : JG.CurrentFigureNumber;
            if (number > 0)
            {
                host.CloseFigure(number);
                JgsHandleRegistry.DropUnreachable();
            }

            return JgsValue.Null;
        })
        { AutoCallsBare = true, BindsAnsAsStatement = false }));

        Define("clf", (args, line, col) =>
        {
            ArityRange("clf", args, 0, 1, line, col);
            if (args.Count == 0)
            {
                JG.Clf();
                return JgsValue.Null;
            }

            int number = Count("clf", args, 0, line, col);
            if (!JG.TryGetFigure(number, out _))
            {
                throw new JgsRuntimeException(line, col, $"There is no figure {number} to clear.");
            }

            JG.Clf(number);
            return JgsValue.Null;
        });

        // Like gca below, the bare name has to be the answer: findobj(gcf, …) must be handed the
        // figure, not the function that would find it.
        env.Declare("gcf", JgsValue.Function(new BuiltinFunction("gcf", (args, line, col) =>
        {
            Arity("gcf", args, 0, line, col);
            return JgsValue.Number(JG.CurrentFigureNumber);
        })
        { AutoCallsBare = true }));

        // gca creates the figure and axes MATLAB would and hands back a handle on them, so both
        // `gca; xlabel('t')` and `ax = gca; xlabel(ax, 't')` behave the same way. Auto-calling on
        // the bare name is what makes the second form an axes rather than the builtin itself.
        env.Declare("gca", JgsValue.Function(new BuiltinFunction("gca", (args, line, col) =>
        {
            Arity("gca", args, 0, line, col);
            return JgsHandleRegistry.For(JG.Gca());
        })
        { AutoCallsBare = true, BindsAnsAsStatement = false }));

        // Every drawing verb hands back a handle and prints nothing as a bare statement, which is
        // MATLAB's rule: `plot(x, y)` on its own draws, and only `h = plot(x, y)` keeps the handle.
        // Registering them with Define instead would echo `ans = 1000000.5` at every unsuppressed
        // call — which is what `plot` did before M54, the one verb that already returned a handle.
        DefineSilent("plot", (args, line, col) => Plot(args, dialect, line, col));
        DefineSilent("scatter", (args, line, col) => Scatter(args, line, col));
        DefineSilent("stem", OnNamedAxes((args, line, col) => Stem(args, dialect, line, col)));
        DefineSilent("histogram", (args, line, col) => Histogram(args, line, col));
        DefineSilent("errorbar", OnNamedAxes((args, line, col) => ErrorBar(args, line, col)));

        // --- 3D surfaces, contours, and images -----------------------------------------------
        Define("meshgrid", (args, line, col) => Grids("meshgrid", args, line, col));
        Define("ndgrid", (args, line, col) => Grids("ndgrid", args, line, col));

        DefineSilent("surf", OnNamedAxes((args, line, col) => Surface3D("surf", args, line, col,
            (x, y, z) => JG.Surf(x, y, z), z => JG.Surf(z), (x, y, z) => JG.Surf(x, y, z))));
        DefineSilent("mesh", OnNamedAxes((args, line, col) => Surface3D("mesh", args, line, col,
            (x, y, z) => JG.Mesh(x, y, z), z => JG.Mesh(z), (x, y, z) => JG.Mesh(x, y, z))));
        DefineSilent("meshc", OnNamedAxes((args, line, col) => Surface3D("meshc", args, line, col,
            (x, y, z) => JG.MeshC(x, y, z),
            z =>
            {
                SurfacePlot surface = JG.Mesh(z);
                surface.ShowContourBelow = true;
                return surface;
            },
            (x, y, z) => JG.MeshC(x, y, z))));

        // contour, contourf and contour3 are registered by RegisterDecorationBuiltins, which runs
        // later and wins. The pair that stood here was shadowed and never ran — found by M70 when
        // the target-form verifier reported a failure whose message came from the other body.

        DefineSilent("imagesc", OnNamedAxes(
            (args, line, col) => DrawImage("imagesc", args, scaled: true, line, col)));

        DefineSilent("pcolor", OnNamedAxes((args, line, col) =>
        {
            ArityRange("pcolor", args, 1, 3, line, col);
            if (args.Count == 1)
            {
                // pcolor(C) lays the cells out on their own column and row numbers, which is the
                // grid meshgrid would have made. MATLAB documents it; this refused it until M70.
                double[,] cells = Matrix("pcolor", args, 0, line, col);
                return Handle(JG.Pcolor(
                    Ramp1(cells.GetLength(1)), Ramp1(cells.GetLength(0)), cells));
            }

            Arity("pcolor", args, 3, line, col);
            return Handle(JG.Pcolor(
                DoubleArray("pcolor", args, 0, line, col),
                DoubleArray("pcolor", args, 1, line, col),
                Matrix("pcolor", args, 2, line, col)));
        }));

        Define("zlabel", (args, line, col) => { Arity("zlabel", args, 1, line, col); JG.ZLabel(Str("zlabel", args, 0, line, col)); return JgsValue.Null; });
        env.Declare("view", JgsValue.Function(
            new BuiltinFunction("view", OnNamedAxes(View)) { AutoCallsBare = true }));

        // Bare `m = colormap` is the read, the way `x = eps` is a number (M37's AutoCallsBare);
        // the callee position stays exempt, so colormap(jet) still calls.
        env.Declare("colormap", JgsValue.Function(new BuiltinFunction("colormap", (args, line, col) =>
        {
            ArityRange("colormap", args, 0, 2, line, col);

            // A leading handle names what the map belongs to: an axes, or the figure whose axes fall
            // back on it. Without one it is the current axes, which is what it has always been.
            GraphObject? target = null;
            IReadOnlyList<JgsValue> rest = args;
            if (args.Count > 0 && args[0].Type != JgsType.String
                && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry)
                && entry.Target is FigureModel or AxesModel)
            {
                target = entry.Target;
                rest = args.Skip(1).ToList();
            }
            else if (args.Count == 2)
            {
                throw new JgsRuntimeException(line, col,
                    "colormap(target, map) expects a figure or axes handle as its first argument.");
            }

            // No map reads one back, which is what makes rgbplot(colormap) and
            // colormap(flipud(colormap)) mean anything.
            if (rest.Count == 0)
            {
                Colormap current = target switch
                {
                    FigureModel figure => figure.Colormap ?? Colormap.Parula,
                    AxesModel axes => axes.ResolveColormap() ?? Colormap.Parula,
                    _ => JG.CurrentColormap(),
                };
                return ColormapTable(current.Resample(DefaultColormapRows));
            }

            try
            {
                Colormap chosen = rest[0].Type == JgsType.String
                    ? NamedColormap(Str("colormap", rest, 0, line, col), line, col)

                    // An m-by-3 table of components in [0, 1], which is what every generator returns
                    // and so what `colormap(parula(64))` and `colormap(flipud(gray))` hand over.
                    : Colormap.FromRows("custom", Matrix("colormap", rest, 0, line, col));

                switch (target)
                {
                    case FigureModel figure:
                        figure.Colormap = chosen;
                        foreach (AxesModel axes in figure.Axes)
                        {
                            if (axes.Colormap is null)
                            {
                                ReseedPlots(axes);
                            }
                        }

                        break;

                    case AxesModel axes:
                        axes.Colormap = chosen;
                        ReseedPlots(axes);
                        break;

                    default:
                        JG.Colormap(chosen);
                        break;
                }
            }
            catch (ArgumentException ex)
            {
                throw new JgsRuntimeException(line, col, ex.Message);
            }

            return JgsValue.Null;
        })
        { AutoCallsBare = true }));

        // AutoCallsBare because 'h = colorbar;' — no parentheses, and the handle kept — is how MATLAB
        // documents it and how a script reaches the bar to label it. Without it the bare name bound
        // the builtin itself, so h was a function and every later property write refused.
        env.Declare("colorbar", JgsValue.Function(new BuiltinFunction("colorbar", (args, line, col) =>
        {
            ArityRange("colorbar", args, 0, 1, line, col);
            JG.Colorbar(OnOff("colorbar", args, line, col, dialect, () => JG.Gca().Colorbar.Visible));
            return JgsHandleRegistry.For(JG.Gca().Colorbar);
        })
        {
            AutoCallsBare = true,
            BindsAnsAsStatement = false,
        }));

        DefineSilent("semilogy", OnNamedAxes((args, line, col) => Semilog("semilogy", args, line, col, (x, y, s) => JG.SemilogY(x, y, s))));
        DefineSilent("semilogx", OnNamedAxes((args, line, col) => Semilog("semilogx", args, line, col, (x, y, s) => JG.SemilogX(x, y, s))));
        DefineSilent("loglog", OnNamedAxes((args, line, col) => Semilog("loglog", args, line, col, (x, y, s) => JG.LogLog(x, y, s))));

        // Every axes-facing verb accepts a leading axes handle, MATLAB's title(ax, '…') form. The
        // named axes is made current only for the call, so gca does not move (M51).
        void DefineOnAxes(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            Define(name, (args, line, col) =>
            {
                (AxesModel? axes, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
                return OnAxes(axes, () => body(rest, line, col));
            });

        DefineOnAxes("title", (args, line, col) => { Arity("title", args, 1, line, col); JG.Title(Str("title", args, 0, line, col)); return JgsValue.Null; });
        DefineOnAxes("xlabel", (args, line, col) => { Arity("xlabel", args, 1, line, col); JG.XLabel(Str("xlabel", args, 0, line, col)); return JgsValue.Null; });
        DefineOnAxes("ylabel", (args, line, col) => { Arity("ylabel", args, 1, line, col); JG.YLabel(Str("ylabel", args, 0, line, col)); return JgsValue.Null; });

        DefineOnAxes("yyaxis", (args, line, col) =>
        {
            Arity("yyaxis", args, 1, line, col);
            string side = Str("yyaxis", args, 0, line, col);
            bool right = side.Equals("right", StringComparison.OrdinalIgnoreCase);
            if (!right && !side.Equals("left", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, $"yyaxis expects 'left' or 'right', but got '{side}'.");
            }

            JG.YyAxis(right);
            return JgsValue.Null;
        });

        DefineOnAxes("grid", (args, line, col) =>
        {
            ArityRange("grid", args, 0, 1, line, col);

            // 'grid minor' works the minor lines rather than the major ones, and toggles them the way
            // a bare 'grid' toggles the majors — the grid model has carried ShowMinor since it was
            // written, so the word was the only thing missing. MATLAB spells the explicit forms
            // 'minor on' / 'minor off' as two words, which a script writes as grid('minor', 'on').
            if (args.Count == 1 && args[0].Type == JgsType.String
                && args[0].AsString.Equals("minor", StringComparison.OrdinalIgnoreCase))
            {
                GridModel minor = JG.Gca().Grid;
                minor.ShowMinor = !minor.ShowMinor;
                return JgsValue.Null;
            }

            JG.Grid(OnOff("grid", args, line, col, dialect, () => JG.Gca().Grid.ShowMajor));
            return JgsValue.Null;
        });
        DefineOnAxes("hold", (args, line, col) =>
        {
            ArityRange("hold", args, 0, 1, line, col);
            JG.Hold(OnOff("hold", args, line, col, dialect, () => JG.IsHolding));
            return JgsValue.Null;
        });

        DefineSilent("legend", (args, line, col) =>
        {
            (AxesModel? axes, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
            return OnAxes(axes, () => Legend(rest, line, col));
        });

        Define("linkaxes", (args, line, col) =>
        {
            ArityRange("linkaxes", args, 1, 2, line, col);
            var linked = new List<AxesModel>();
            JgsValue handles = args[0];
            int count = handles.Type == JgsType.Array ? handles.ArrayLength : 1;
            for (int i = 0; i < count; i++)
            {
                JgsValue element = handles.Type == JgsType.Array ? handles.ElementAt(i) : handles;
                JgsHandleEntry entry = JgsHandleRegistry.Require(element, line, col);
                if (entry.Target is not AxesModel axes)
                {
                    throw new JgsRuntimeException(line, col, "linkaxes wants handles to axes, such as the ones subplot hands back.");
                }

                linked.Add(axes);
            }

            string which = args.Count == 2 ? Str("linkaxes", args, 1, line, col) : "xy";
            AxisLinkMode mode = which.ToLowerInvariant() switch
            {
                "x" => AxisLinkMode.X,
                "y" => AxisLinkMode.Y,
                "xy" or "both" => AxisLinkMode.Both,
                "off" => AxisLinkMode.Both,
                _ => throw new JgsRuntimeException(line, col, $"linkaxes: '{which}' is not 'x', 'y', or 'xy'."),
            };

            JG.LinkAxes(mode, linked.ToArray());
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
        DefineImagingBuiltins(Define, host, random, dialect);

        // The living-graphics objects (M67) before the MATLAB names, because getpoints answers one
        // coordinate per output and the wrapper that gives it several is declared over there — and a
        // wrapper silently does nothing to a name that is not there yet.
        RegisterLivingGraphicsBuiltins(env);

        // --- MATLAB names (M28) — defined in JgsBuiltins.Matlab.cs ---------------------------
        // Registered last: the multiple-output forms wrap builtins declared above.
        RegisterMatlabBuiltins(env, host, random, dialect);

        // Shape/generation builtins may re-register MATLAB-shaped constructors, and the reduction
        // semantics wrap builtins the two calls above declared — order matters here.
        RegisterShapeBuiltins(env, random, dialect);
        RegisterLinearAlgebraBuiltins(env, dialect);
        RegisterMatrixFunctionBuiltins(env);
        RegisterSparseBuiltins(env, random);
        RegisterSparseOrderingBuiltins(env, dialect);
        RegisterGeneralizedBuiltins(env);
        RegisterDataTypeBuiltins(env);
        RegisterFileIoBuiltins(env, host);
        RegisterElementaryBuiltins(env, dialect);
        RegisterNumericBuiltins(env);
        RegisterSpecialFunctionBuiltins(env);
        RegisterMatrixBuiltins(env);
        RegisterSchurBuiltins(env);
        RegisterTextBuiltins(env, dialect);
        RegisterArrayBuiltins(env, random, dialect);
        RegisterEnvironmentBuiltins(env, host);
        RegisterGeometryBuiltins(env);
        RegisterColorControlBuiltins(env, dialect);
        RegisterCameraBuiltins(env);
        RegisterPrimitive3DBuiltins(env);
        RegisterHandleGraphicsBuiltins(env, host);
        RegisterRulerBuiltins(env);
        RegisterSurfaceVariantBuiltins(env, dialect);
        RegisterGraphics2DBuiltins(env, dialect);
        RegisterPolarBuiltins(env, dialect);
        RegisterMeshPlotBuiltins(env);
        RegisterChart3DBuiltins(env);
        RegisterDensityBuiltins(env);
        RegisterSwarmBuiltins(env);
        RegisterCompositionBuiltins(env);
        RegisterPanelCompositionBuiltins(env);

        // The function plotters draw with the verbs above, so they are declared after all of them.
        RegisterFunctionPlotBuiltins(env);

        // The volume verbs draw with those too, and several of them hand a shape to `patch`.
        RegisterVolumeBuiltins(env, host);

        // The figure-tooling verbs work on what the verbs above drew rather than drawing themselves.
        RegisterFigureToolBuiltins(env, host);
        RegisterFigureStateBuiltins(env);
        RegisterMotionBuiltins(env);

        // The paper verbs come after them because print takes the name over from the console verb
        // declared far above, and the last declaration of a name is the one a script reaches.
        RegisterPrinting(env, host, dialect);

        // After the plotting verbs it re-declares: the titling family gains its text options here,
        // and contour learns to answer with its matrix as well as its handle.
        RegisterDecorationBuiltins(env, dialect);

        // The camera verbs M45 left out, and the appearance commands that predate figure properties.
        RegisterCameraExtraBuiltins(env);
        RegisterLegacyAppearanceBuiltins(env, dialect);

        // After every other define, because three of these replace a name declared above; and before
        // the reductions, so rms is wrapped for a dimension the same way mean is.
        RegisterDataAnalysisBuiltins(env, dialect, host);
        RegisterSetOperations(env, dialect);

        // The preprocessing family (M66) after the data analysis it shares a binning rule with, so
        // discretize and histcounts cannot be given two different opinions about where a bin starts.
        RegisterPreprocessingBuiltins(env, dialect);
        RegisterNumericExtraBuiltins(env, host);

        // The Statistics Toolbox (M53), after the base names it replaces or leans on and before the
        // reductions, so a statistic that reduces columns is wrapped for a dimension exactly once.
        RegisterStatisticsBuiltins(env, host, random, dialect);
        RegisterOptimizeBuiltins(env, host);
        RegisterPolynomialBuiltins(env);
        RegisterInterpolationBuiltins(env, host);
        RegisterMatrixBuilderBuiltins(env, host);
        RegisterCleaningBuiltins(env);
        RegisterGroupingBuiltins(env);
        RegisterDataTrendBuiltins(env);
        RegisterTextPartBuiltins(env);
        RegisterCoordinateBuiltins(env);
        RegisterSpecfunPartBuiltins(env);
        RegisterMatfunBuiltins(env, host, random);
        RegisterMatfunSpectralBuiltins(env, host);
        RegisterDecompositionBuiltins(env, host);

        // After every imaging define, since these wrap builtins declared above.
        RegisterImagingMultiOutputForms(env, host, random, dialect);
        if (dialect.IsMatlab)
        {
            RegisterMatlabReductions(env, dialect);
            RegisterGraphicsNamespace(env);

            // MATLAB's slice cuts a volume; JGS's slice takes a piece of a list. Both keep their own
            // name, which they can only do by the volume one being declared here, over the other, and
            // only for the dialect that means it.
            RegisterVolumeSlice(env);
        }

        // Last of all: the distribution objects put a check in front of nine names declared above,
        // six of which the reductions have just wrapped for a dimension. Anywhere earlier and the
        // object check would sit under that wrapping rather than in front of it.
        RegisterDistributionObjectForms(env, random);

        // Last of all, after every define and every re-declaration. The editing family comes first
        // because its wrappers are what the marking pass must then find: both passes reach into the
        // finished environment, and a mark on a wrapper that was replaced afterwards is a mark on
        // nothing.
        // The time types and the keyed collections (M64) go in after every ordinary define, because
        // they replace placeholders declared above. Both dialects get them: every name here is new
        // but two, so for JGS this is a pure addition — which is what its freeze allows.
        RegisterDataOutBuiltins(Define, host);
        RegisterTimeBuiltins(env);
        RegisterTimePartBuiltins(env);
        RegisterKeyedCollectionBuiltins(env);
        TimeAwareReductions(env);

        // The two that are NOT new are put back as they were for JGS. `seconds` answered with its own
        // argument (M43's stand-in: a duration was its count of seconds) and `datetime` answered with
        // a char row of the current moment. A JGS script that called either got that, and the freeze
        // says it must go on getting it — so the gate is on the two names whose meaning moved, not on
        // the milestone.
        if (!dialect.IsMatlab)
        {
            env.Declare("seconds", JgsValue.Function(new BuiltinFunction("seconds", (args, line, col) =>
            {
                Arity("seconds", args, 1, line, col);
                return MapNumeric("seconds", args[0], static x => x, line, col);
            })));

            env.Declare("datetime", JgsValue.Function(new BuiltinFunction("datetime", (args, line, col) =>
            {
                Arity("datetime", args, 0, line, col);
                return JgsValue.Str(DateTime.Now.ToString("dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            })));
        }

        RegisterStringEditingBuiltins(env);
        RegisterStringArrayBuiltins(env);

        // Last of all, so it wraps whichever wrapper each name ended up with (M105) — the same reason
        // the string-array marks are applied last.
        KeepCharMatrixKind(env);

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

    /// <summary>Option names <c>plot</c> recognizes as trailing name-value pairs (M42). The second
    /// row is the common callback and interaction block (M71), served by the property table rather
    /// than by cases here — one definition of each, however it is spelled into the object.</summary>
    private static readonly HashSet<string> PlotOptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "LineWidth", "Color", "LineStyle", "Marker", "MarkerSize", "DisplayName", "HandleVisibility",
        "MarkerEdgeColor", "MarkerFaceColor", "MarkerIndices", "LineJoin", "AlignVertexCenters",
        "ButtonDownFcn", "CreateFcn", "DeleteFcn", "Interruptible", "BusyAction",
        "Selected", "SelectionHighlight", "HitTest", "PickableParts",
    };

    /// <summary>
    /// <c>legend</c>: either a list of names for the series in order (the old form), or a vector of
    /// line handles saying exactly which series to show and in what order, followed by
    /// <c>'Location', where</c> pairs. Hands back a handle on the legend so a script can go on to
    /// place it or give it a click callback.
    /// </summary>
    private static JgsValue Legend(IReadOnlyList<JgsValue> args, int line, int col)
    {
        var names = new List<string>();
        List<PlotObject>? chosen = null;
        LegendPosition? location = null;

        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.String
                && args[i].AsString.Equals("Location", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Count)
            {
                location = ParseLegendLocation(Str("legend", args, i + 1, line, col), line, col);
                i++;
                continue;
            }

            if (args[i].Type == JgsType.String)
            {
                names.Add(args[i].AsString);
                continue;
            }

            // A cell of char or a string array is a list of names, not a list of handles (M63).
            // Without this, legend({'a', 'b'}) — MATLAB's most common spelling — reached PlotsOf and
            // failed complaining about a figure handle, which is a message about the wrong thing
            // entirely. The string-array half is what a script written since R2016b passes.
            if (TextElementsOf(args[i]) is { Length: > 0 } labels)
            {
                names.AddRange(labels);
                continue;
            }

            chosen ??= [];
            chosen.AddRange(PlotsOf("legend", args[i], line, col));
        }

        LegendModel legend;
        if (chosen is not null)
        {
            legend = JG.Legend(JG.Gca(), chosen);
        }
        else
        {
            JG.Legend(names.ToArray());
            legend = JG.Gca().Legend;
        }

        if (location is { } position)
        {
            legend.Position = position;
        }

        return JgsHandleRegistry.For(legend);
    }

    private static JgsValue Plot(IReadOnlyList<JgsValue> args, JgsDialect dialect, int line, int col)
    {
        (AxesModel? target, args) = PeelAxes(args);
        return OnAxes(target, () => PlotCore(args, dialect, line, col));
    }

    /// <summary>
    /// The body every line-drawing verb shares. <paramref name="verb"/> only names the caller in its
    /// error messages; <paramref name="implicitX"/> is what the verb puts on the other axis when the
    /// call gave values alone, which is the one place <c>plot</c> and <c>polarplot</c> differ — sample
    /// numbers on square paper, angles round a circle.
    /// </summary>
    private static JgsValue PlotCore(
        IReadOnlyList<JgsValue> args,
        JgsDialect dialect,
        int line,
        int col,
        string verb = "plot",
        Func<int, double[]>? implicitX = null)
    {
        (args, List<(string Name, JgsValue Value)> options) = SplitPlotOptions(verb, args, line, col);

        // A time reaching a chart is turned into the number the axis speaks before anything below
        // sees it (M64), and the ruler is told afterwards what those numbers mean. The two halves
        // have to be separate: the drawing pipeline works in doubles from end to end, so the type
        // cannot travel through it — only the axis remembers.
        (args, bool datesAlongX, bool datesAlongY) = ConvertTimesForPlot(args);

        var created = new List<LinePlot>();

        if (args.Count > 0 && args[0].Type == JgsType.Table)
        {
            ArityRange(verb, args, 3, 4, line, col);
            Table table = Tbl(verb, args, 0, line, col);
            string xColumn = Str(verb, args, 1, line, col);
            string yColumn = Str(verb, args, 2, line, col);
            string? spec = args.Count == 4 ? Str(verb, args, 3, line, col) : null;
            created.Add(JG.Plot(table, xColumn, yColumn, spec));
            ApplyPlotOptions(verb, created, options, line, col);
            return HandlesFor(created);
        }

        bool wasHolding = JG.IsHolding;
        try
        {
            switch (args.Count)
            {
                case 1:
                    PlotColumns(created, x: null, args[0], spec: null, dialect, verb, implicitX, line, col);
                    break;
                case 2 when args[1].Type == JgsType.String:
                    PlotColumns(
                        created, x: null, args[0], Str(verb, args, 1, line, col),
                        dialect, verb, implicitX, line, col);
                    break;
                case 2:
                    PlotColumns(created, args[0], args[1], spec: null, dialect, verb, implicitX, line, col);
                    break;
                case 3:
                    PlotColumns(
                        created, args[0], args[1], Str(verb, args, 2, line, col),
                        dialect, verb, implicitX, line, col);
                    break;
                default:
                    // Repeated (x, y[, spec]) groups — plot(t, a, 'b', t, b, 'r--').
                    int i = 0;
                    while (i < args.Count)
                    {
                        // A word where the next group's x should be is a misspelled option, near enough
                        // always: the split above only recognizes the names it knows, so a typo falls
                        // through to here, and saying "expects groups" would send the reader nowhere.
                        if (args[i].Type == JgsType.String)
                        {
                            throw new JgsRuntimeException(line, col,
                                $"{verb}: unknown option '{args[i].AsString}'. Use "
                                + $"{string.Join(", ", PlotOptionNames)}.");
                        }

                        if (i + 1 >= args.Count || args[i].Type != JgsType.Array || args[i + 1].Type != JgsType.Array)
                        {
                            throw new JgsRuntimeException(line, col,
                                $"{verb} expects (y), (x, y[, spec]) groups, "
                                + "or (table, xColumn, yColumn[, spec]).");
                        }

                        JgsValue x = args[i];
                        JgsValue y = args[i + 1];
                        string? groupSpec = null;
                        i += 2;
                        if (i < args.Count && args[i].Type == JgsType.String)
                        {
                            groupSpec = Str(verb, args, i, line, col);
                            if (!IsLineSpecWord(groupSpec))
                            {
                                // The split above only recognizes the option names it knows, so a
                                // misspelling falls through to here and would otherwise be read as a
                                // line spec — which ignores the letters it does not understand, and
                                // draws the chart as though nothing were wrong.
                                throw new JgsRuntimeException(line, col,
                                    $"{verb}: unknown option '{groupSpec}'. Use "
                                    + $"{string.Join(", ", PlotOptionNames)}.");
                            }

                            i++;
                        }

                        PlotColumns(created, x, y, groupSpec, dialect, verb, implicitX, line, col);
                    }

                    break;
            }
        }
        finally
        {
            JG.Hold(wasHolding);
        }

        ApplyPlotOptions(verb, created, options, line, col);

        if ((datesAlongX || datesAlongY) && created.Count > 0)
        {
            AxesModel? axes = created[0].Axes;
            if (datesAlongX)
            {
                axes?.PrimaryXAxis.UseDateTime();
            }

            if (datesAlongY)
            {
                axes?.ActiveYAxis.UseDateTime();
            }
        }

        return HandlesFor(created);
    }

    /// <summary>
    /// Replaces every time argument with the numbers the axis works in, and says which of the two
    /// rulers should be told it is showing dates.
    /// </summary>
    /// <remarks>
    /// A datetime becomes an OLE automation date — the convention
    /// <see cref="JGraph.Core.Model.DateTimeAxis"/> has used since M6, which is exactly why the
    /// storage counts milliseconds from that same epoch: the conversion is one divide and the two
    /// halves of the build cannot drift apart. A duration becomes its number of days, which is the
    /// same divide, so a span plots against a date on the same scale.
    /// <para>
    /// Only the first two positions are read for the ruler, so the repeated-group form
    /// <c>plot(t1, a, t2, b)</c> takes its axis kind from the first group. Every group's numbers are
    /// still converted; it is only the label that comes from the first.
    /// </para>
    /// </remarks>
    private static (IReadOnlyList<JgsValue> Args, bool AlongX, bool AlongY) ConvertTimesForPlot(
        IReadOnlyList<JgsValue> args)
    {
        bool anyTime = false;
        foreach (JgsValue arg in args)
        {
            if (arg.IsTime)
            {
                anyTime = true;
                break;
            }
        }

        if (!anyTime)
        {
            return (args, false, false);
        }

        var converted = new JgsValue[args.Count];
        for (int i = 0; i < args.Count; i++)
        {
            converted[i] = args[i].IsTime ? AxisNumbersFor(args[i]) : args[i];
        }

        // With one argument the values are y and the sample numbers are x; with two or more the
        // first is x. That is the same reading PlotColumns makes of the same arguments.
        bool alongX = args.Count >= 2 && args[0].IsDatetime;
        bool alongY = args.Count == 1 ? args[0].IsDatetime : args.Count >= 2 && args[1].IsDatetime;
        return (converted, alongX, alongY);
    }

    /// <summary>A time as the plain numbers an axis plots: days, from the same epoch.</summary>
    private static JgsValue AxisNumbersFor(JgsValue time)
    {
        double[] source = TimeMs(time);
        var values = new double[source.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = source[i] / JgsTime.MsPerDay;
        }

        JgsValue built = Numbers(values);
        if (time.ArrayLength > 1 || time.IsShaped || time.IsNd)
        {
            built.TakeShapeOf(time);
        }

        return built;
    }

    /// <summary>
    /// The handles for the series a plot call made: one on its own, several as a column, which is
    /// the shape MATLAB gives back and the shape <c>h(i) = plot(…)</c> expects.
    /// </summary>
    private static JgsValue HandlesFor<T>(IReadOnlyList<T> created)
        where T : PlotObject
    {
        if (created.Count == 0)
        {
            return JgsValue.Array([]);
        }

        if (created.Count == 1)
        {
            return JgsHandleRegistry.For(created[0]);
        }

        var handles = new double[created.Count];
        for (int i = 0; i < created.Count; i++)
        {
            handles[i] = JgsHandleRegistry.For(created[i]).AsNumber;
        }

        return JgsMatrix.FromColumnMajor(handles, created.Count, 1);
    }

    /// <summary>
    /// The handle for the one series a drawing verb made. Every verb that draws hands one back, the
    /// way MATLAB's do: without it a script can reach a bar or a surface only by searching for it, and
    /// <c>set(h, …)</c> — the point of the whole milestone — would work on lines alone.
    /// </summary>
    private static JgsValue Handle(PlotObject plot) => JgsHandleRegistry.For(plot);

    /// <summary>
    /// One plot group. A matrix <paramref name="y"/> plots each column as its own series (MATLAB's
    /// rule — <c>plot(t, Y)</c> from ode45 draws every state), holding between series so they share
    /// the axes; a vector is one series.
    /// </summary>
    private static void PlotColumns(
        List<LinePlot> created,
        JgsValue? x,
        JgsValue y,
        string? spec,
        JgsDialect dialect,
        string verb,
        Func<int, double[]>? implicitX,
        int line,
        int col)
    {
        double[] Implicit(int n) => implicitX is null ? ImplicitX(dialect, n) : implicitX(n);
        double[]? xs = x is null ? null : DoubleArray(verb, [x], 0, line, col);

        bool matrixY = y.Type == JgsType.Array
            && JgsMatrix.RowCount(y) > 1 && JgsMatrix.ColCount(y) > 1;
        if (matrixY && (xs is null || xs.Length == JgsMatrix.RowCount(y)))
        {
            int rows = JgsMatrix.RowCount(y);
            int columns = JgsMatrix.ColCount(y);
            for (int c = 0; c < columns; c++)
            {
                var series = new double[rows];
                for (int r = 0; r < rows; r++)
                {
                    JgsValue element = JgsMatrix.At(y, r, c);
                    if (element.Type is not (JgsType.Number or JgsType.Bool))
                    {
                        throw new JgsRuntimeException(line, col,
                            $"{verb} needs numbers, but element ({r}, {c}) was a {element.TypeName}.");
                    }

                    series[r] = element.AsNumber;
                }

                LinePlot column = JG.Plot(xs ?? Implicit(rows), series, spec);
                column.XImplied = xs is null;
                created.Add(column);
                JG.Hold(true);
            }

            return;
        }

        double[] ys = DoubleArray(verb, [y], 0, line, col);
        LinePlot drawn = JG.Plot(xs ?? Implicit(ys.Length), ys, spec);
        drawn.XImplied = xs is null;
        created.Add(drawn);
        JG.Hold(true);
    }

    /// <summary>
    /// Splits trailing name-value option pairs off a plot argument list. Options begin at the first
    /// string matching a recognized option name; everything before it is data and spec strings.
    /// </summary>
    private static (IReadOnlyList<JgsValue> Data, List<(string Name, JgsValue Value)> Options) SplitPlotOptions(
        string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int start = args.Count;
        for (int i = 0; i + 1 < args.Count; i++)
        {
            if (args[i].Type == JgsType.String && PlotOptionNames.Contains(args[i].AsString))
            {
                start = i;
                break;
            }
        }

        if (start == args.Count)
        {
            return (args, []);
        }

        var data = new List<JgsValue>();
        for (int i = 0; i < start; i++)
        {
            data.Add(args[i]);
        }

        var options = new List<(string, JgsValue)>();
        for (int i = start; i < args.Count; i += 2)
        {
            if (args[i].Type != JgsType.String || i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb} options come in name-value pairs, like {verb}(x, y, 'LineWidth', 2).");
            }

            options.Add((args[i].AsString, args[i + 1]));
        }

        return (data, options);
    }

    /// <summary>
    /// Applies recognized name-value options to every series the call created. Color, line style
    /// and marker values ride through <see cref="LineSpec.Parse"/> so option spellings and spec
    /// characters cannot drift apart; unrecognized option names were filtered out before this.
    /// </summary>
    private static void ApplyPlotOptions(
        string verb, List<LinePlot> created, List<(string Name, JgsValue Value)> options, int line, int col)
    {
        // Every auto-coloured series takes a seat in the axes' cycle instead of having its colour
        // written down: the renderer resolves the seat at draw time, so a later colororder retints
        // it, while reading Color off the handle still answers the seat's colour. A non-default
        // LineStyleOrder styles the seat's lap of the palette, but never overrides a linespec.
        foreach (LinePlot plot in created)
        {
            if (plot.Color is not null)
            {
                continue;
            }

            SeriesSlot seat = SeatSeries(plot);
            if (plot.Axes?.LineStyleOrder is not null
                && plot.DashStyle == DashStyle.Solid
                && plot.Marker == MarkerType.None)
            {
                plot.DashStyle = seat.Style.Dash;
                plot.Marker = seat.Style.Marker;

                // What the cycle hands out is not what a script chose, and LineStyleMode is exactly
                // that difference — so the flags the two setters raised come down again here.
                plot.LineStyleManual = false;
                plot.MarkerManual = false;
            }
        }

        foreach ((string name, JgsValue value) in options)
        {
            foreach (LinePlot plot in created)
            {
                switch (name.ToLowerInvariant())
                {
                    case "linewidth":
                        plot.LineWidth = NumOf($"{verb}: LineWidth", value, line, col);
                        break;
                    case "markersize":
                        plot.MarkerSize = NumOf($"{verb}: MarkerSize", value, line, col);
                        break;

                    // The five names M77 added. MarkerFaceColor in particular is the commonest
                    // spelling in MATLAB code and was refused by this verb until then.
                    case "markerfacecolor":
                        plot.MarkerFaceColor = value.Type == JgsType.String
                            && value.AsString.Equals("none", StringComparison.OrdinalIgnoreCase)
                                ? null
                                : OptionColor(value, line, col, verb);
                        break;
                    case "markeredgecolor":
                        plot.MarkerEdgeColor = OptionColor(value, line, col, verb);
                        break;
                    case "markerindices":
                        JgsGraphicsProperties.Set(
                            JgsHandleRegistry.EntryFor(plot), "MarkerIndices", value, line, col);
                        break;
                    case "linejoin":
                        JgsGraphicsProperties.Set(
                            JgsHandleRegistry.EntryFor(plot), "LineJoin", value, line, col);
                        break;
                    case "alignvertexcenters":
                        JgsGraphicsProperties.Set(
                            JgsHandleRegistry.EntryFor(plot), "AlignVertexCenters", value, line, col);
                        break;
                    case "displayname":
                        SetDisplayName(plot, StrOf($"{verb}: DisplayName", value, line, col));
                        break;
                    case "handlevisibility":
                        JgsHandleEntry entry = JgsHandleRegistry.Require(
                            JgsHandleRegistry.For(plot), line, col);
                        entry.HandleVisible = !StrOf($"{verb}: HandleVisibility", value, line, col)
                            .Equals("off", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "color":
                        plot.Color = OptionColor(value, line, col, verb);
                        break;
                    case "linestyle":
                        string style = StrOf($"{verb}: LineStyle", value, line, col);
                        plot.DashStyle = style.Equals("none", StringComparison.OrdinalIgnoreCase)
                            ? plot.DashStyle
                            : LineSpec.Parse(style).Dash ?? plot.DashStyle;
                        break;
                    case "marker":
                        string marker = StrOf($"{verb}: Marker", value, line, col);
                        plot.Marker = marker.Equals("none", StringComparison.OrdinalIgnoreCase)
                            ? JGraph.Core.Drawing.MarkerType.None
                            : LineSpec.Parse(marker).Marker ?? plot.Marker;
                        break;
                    case "buttondownfcn" or "createfcn" or "deletefcn" or "interruptible"
                        or "busyaction" or "selected" or "selectionhighlight" or "hittest"
                        or "pickableparts":
                        // The common block goes through the property table, so the option spelling
                        // and set(h, ...) cannot drift apart.
                        JgsGraphicsProperties.Set(
                            JgsHandleRegistry.Require(JgsHandleRegistry.For(plot), line, col),
                            name, value, line, col);
                        break;
                    default:
                        // Unreachable through SplitPlotOptions, which only collects known names —
                        // but a silent drop here is how a misspelling would go unnoticed.
                        throw new JgsRuntimeException(line, col,
                            $"{verb}: unknown option '{name}'. Use {string.Join(", ", PlotOptionNames)}.");
                }
            }
        }

        // MATLAB's one moment for CreateFcn: given in the creating call, it runs now, after every
        // option is applied, with the new object as gcbo. set(h, 'CreateFcn', f) later only stores.
        if (options.Any(static option => option.Name.Equals("CreateFcn", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (LinePlot plot in created)
            {
                JgsCallbackDispatcher.Current?.FireCreateFcn(plot);
            }
        }
    }

    /// <summary>
    /// Applies MATLAB's <c>'Name', value</c> light options. Position is read in the projection's
    /// normalized cube space (the data box is [-0.5, 0.5] on every axis), not in data units — which is
    /// what stops a surface whose Z spans millions from lighting like a vertical wall.
    /// </summary>
    private static void ApplyLightOptions(
        string verb, LightModel light, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: options come in 'Name', value pairs.");
        }

        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf($"{verb}: option name", args[i], line, col);
            JgsValue value = args[i + 1];
            switch (name.ToLowerInvariant())
            {
                case "position":
                    double[] p = ToDoubles($"{verb}: Position", value, line, col);
                    if (p.Length != 3)
                    {
                        throw new JgsRuntimeException(line, col, $"{verb}: Position is an [x y z] vector.");
                    }

                    light.Position = new Vector3D(p[0], p[1], p[2]);
                    break;
                case "color":
                    light.Color = OptionColor(value, line, col, verb);
                    break;
                case "style":
                    light.Style = ParseLightStyle(verb, value, line, col);
                    break;
                case "visible":
                    light.Visible = !StrOf($"{verb}: Visible", value, line, col)
                        .Equals("off", StringComparison.OrdinalIgnoreCase);
                    break;
                default:
                    throw new JgsRuntimeException(
                        line, col, $"{verb}: unknown option '{name}'. Use Position, Color, Style, or Visible.");
            }
        }
    }

    private static bool IsLightStyleWord(string text) =>
        text.Equals("infinite", StringComparison.OrdinalIgnoreCase)
        || text.Equals("local", StringComparison.OrdinalIgnoreCase);

    private static LightStyle ParseLightStyle(string verb, JgsValue value, int line, int col)
    {
        string style = StrOf($"{verb}: Style", value, line, col);
        return style.ToLowerInvariant() switch
        {
            "infinite" => LightStyle.Infinite,
            "local" => LightStyle.Local,
            _ => throw new JgsRuntimeException(
                line, col, $"{verb}: Style is 'infinite' or 'local', but got '{style}'."),
        };
    }

    /// <summary>A plot option color: a spec letter, a common color name, or an [r g b] triplet in [0, 1].</summary>
    internal static JGraph.Core.Drawing.Color OptionColor(JgsValue value, int line, int col, string what = "plot")
    {
        if (value.Type == JgsType.String)
        {
            string text = value.AsString;
            string letter = text.ToLowerInvariant() switch
            {
                "red" => "r", "green" => "g", "blue" => "b", "cyan" => "c",
                "magenta" => "m", "yellow" => "y", "black" => "k", "white" => "w",
                _ => text,
            };
            if (LineSpec.Parse(letter).Color is { } named)
            {
                return named;
            }

            if (HexColor(text) is { } hex)
            {
                return hex;
            }

            throw new JgsRuntimeException(line, col, $"{what}: unknown color '{text}'.");
        }

        double[] triplet = ToDoubles($"{what}: Color", value, line, col);
        if (triplet.Length != 3)
        {
            throw new JgsRuntimeException(line, col, $"{what}: a numeric Color is an [r g b] triplet in [0, 1].");
        }

        static byte Level(double v) => (byte)System.Math.Round(System.Math.Clamp(v, 0, 1) * 255);
        return JGraph.Core.Drawing.Color.FromRgb(Level(triplet[0]), Level(triplet[1]), Level(triplet[2]));
    }

    /// <summary>
    /// A <c>#RRGGBB</c> or <c>#RGB</c> string as a colour, or null if it is not one. MATLAB accepts
    /// hex wherever it accepts a colour name, and so does every Color option here.
    /// </summary>
    private static JGraph.Core.Drawing.Color? HexColor(string text)
    {
        if (text.Length is not (4 or 7) || text[0] != '#')
        {
            return null;
        }

        string digits = text[1..];
        foreach (char c in digits)
        {
            if (!Uri.IsHexDigit(c))
            {
                return null;
            }
        }

        // #RGB is the short form, each digit doubled, so #0F8 and #00FF88 are the same colour.
        int step = digits.Length / 3;
        byte Channel(int i)
        {
            string part = digits.Substring(i * step, step);
            int value = Convert.ToInt32(part, 16);
            return (byte)(step == 1 ? (value * 17) : value);
        }

        return JGraph.Core.Drawing.Color.FromRgb(Channel(0), Channel(1), Channel(2));
    }

    internal static double NumOf(string name, JgsValue value, int line, int col)
    {
        if (value.Type is not (JgsType.Number or JgsType.Bool))
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a number, but got a {value.TypeName}.");
        }

        return value.AsNumber;
    }

    internal static string StrOf(string name, JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.String)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a string, but got a {value.TypeName}.");
        }

        return value.AsString;
    }

    private static JgsValue XyOrTable(string name, IReadOnlyList<JgsValue> args, int line, int col,
        Func<double[], double[], PlotObject> arrays, Func<Table, string, string, PlotObject> table,
        bool valuesAlone = false)
    {
        if (args.Count > 0 && args[0].Type == JgsType.Table)
        {
            Arity(name, args, 3, line, col);
            return Handle(table(
                Tbl(name, args, 0, line, col), Str(name, args, 1, line, col), Str(name, args, 2, line, col)));
        }

        // bar(y) stands the values at 1, 2, 3, …, which is how a bar chart is most often written.
        if (valuesAlone && args.Count == 1)
        {
            double[] values = DoubleArray(name, args, 0, line, col);
            var positions = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                positions[i] = i + 1;
            }

            return Handle(arrays(positions, values));
        }

        Arity(name, args, 2, line, col);
        return Handle(arrays(
            DoubleArray(name, args, 0, line, col), DoubleArray(name, args, 1, line, col)));
    }

    /// <summary>
    /// <c>scatter(x, y)</c> and everything MATLAB lets follow it — sizes, colours, <c>'filled'</c>, a
    /// marker spec and name/value pairs — on a named axes or the current one. The table form names
    /// the same two channels with variables instead of arrays and then reads exactly alike, which is
    /// what lets <c>scatter(tbl, 'a', 'b', 'filled')</c> mean what it says.
    /// </summary>
    private static JgsValue Scatter(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            (IReadOnlyList<JgsValue> data, ScatterSource? source) =
                PeelScatterTable("scatter", rest, spatial: false, sized: false, line, col);
            return Sourced(ScatterSeries("scatter", data, line, col), source);
        });
    }

    private static readonly OptionSpec StemOptions = new(
        "stem",
        Flags: ["filled"],
        Names:
        [
            "Color", "LineStyle", "LineWidth", "Marker", "MarkerSize", "MarkerEdgeColor",
            "MarkerFaceColor", "BaseValue", "ShowBaseLine", "DisplayName", "HandleVisibility",
        ]);

    /// <summary>
    /// <c>stem(y)</c>, <c>stem(x, y)</c>, either with <c>'filled'</c>, a LineSpec, and the option
    /// tail. Before M77 it took the two vectors and nothing else, so every appearance a script
    /// wanted had to be set afterwards through the handle.
    /// </summary>
    private static JgsValue Stem(IReadOnlyList<JgsValue> args, JgsDialect dialect, int line, int col)
    {
        // A LineSpec has to come off before the options are read: 'r--s' is not an option name, and
        // the parser is right to say so about every word that is not one.
        // It may sit anywhere among the words — stem(x, y, 'filled', 'r--s', 'LineWidth', 2) is
        // ordinary MATLAB.
        (IReadOnlyList<JgsValue> stemRest, string? spec) = PeelSpecFor(StemOptions, args);

        ParsedArgs parsed = StemOptions.Parse(stemRest, 2, line, col);
        IReadOnlyList<JgsValue> positional = parsed.Positional;

        if (positional.Count is < 1 or > 2)
        {
            throw new JgsRuntimeException(line, col, "stem expects (y) or (x, y), with an optional spec.");
        }

        double[] heights = DoubleArray("stem", positional, positional.Count - 1, line, col);
        double[] positions = positional.Count == 2
            ? DoubleArray("stem", positional, 0, line, col)
            : ImplicitX(dialect, heights.Length);

        StemPlot plot = JG.Stem(positions, heights);
        plot.XImplied = positional.Count == 1;
        ApplyStemOptions(plot, spec, parsed, line, col);
        return Handle(plot);
    }

    /// <summary>The spec first, then the named options, so a name always wins over a shorthand.</summary>
    private static void ApplyStemOptions(
        StemPlot plot, string? spec, ParsedArgs parsed, int line, int col)
    {
        if (spec is not null)
        {
            LineSpec drawn = LineSpec.Parse(spec);
            if (drawn.Color is { } color)
            {
                plot.Color = color;
            }

            if (drawn.Dash is { } dash)
            {
                plot.DashStyle = dash;
            }

            if (drawn.Marker is { } marker)
            {
                plot.Marker = marker;
            }
        }

        // MATLAB's 'filled' fills the markers with the stem's own colour.
        if (parsed.Has("filled"))
        {
            plot.MarkerFaceColor = plot.Color ?? PaletteColorFor(plot);
        }

        if (parsed.Named("Color") is { } stemColor)
        {
            plot.Color = OptionColor(stemColor, line, col, "stem");
        }

        if (parsed.Text("LineStyle") is { } dashWord)
        {
            plot.DashStyle = ParseDashWord(dashWord, plot.DashStyle);
        }

        if (parsed.Text("Marker") is { } markerWord)
        {
            plot.Marker = ParseMarkerWord(markerWord, plot.Marker);
        }

        if (parsed.Named("MarkerFaceColor") is { } face)
        {
            plot.MarkerFaceColor = face.Type == JgsType.String
                && face.AsString.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : OptionColor(face, line, col, "stem");
        }

        if (parsed.Named("MarkerEdgeColor") is { } markerEdge)
        {
            plot.MarkerEdgeColor = OptionColor(markerEdge, line, col, "stem");
        }

        plot.LineWidth = parsed.Scalar("LineWidth", plot.LineWidth);
        plot.MarkerSize = parsed.Scalar("MarkerSize", plot.MarkerSize);
        plot.Baseline = parsed.Scalar("BaseValue", plot.Baseline);

        if (parsed.Text("ShowBaseLine") is { } shown)
        {
            plot.ShowBaseLine = !shown.Equals("off", StringComparison.OrdinalIgnoreCase);
        }

        if (parsed.Text("DisplayName") is { } label)
        {
            SetDisplayName(plot, label);
        }

        if (parsed.Text("HandleVisibility") is { } visibility)
        {
            JgsHandleRegistry.EntryFor(plot).HandleVisible =
                !visibility.Equals("off", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static readonly OptionSpec HistogramChartOptions = new(
        "histogram",
        Flags: [],
        Names:
        [
            "BinEdges", "BinCounts", "BinWidth", "BinLimits", "BinMethod", "NumBins", "Normalization",
            "DisplayStyle", "Orientation", "BarWidth", "FaceColor", "EdgeColor", "FaceAlpha",
            "EdgeAlpha", "LineWidth", "LineStyle", "DisplayName", "HandleVisibility",
            "DisplayOrder", "NumDisplayBins", "ShowOthers",
        ]);

    /// <summary>
    /// <c>histogram(x)</c> and everything round it: a bin count, a set of edges, counts taken
    /// somewhere else, a table column, a list of names, and the option tail every other chart verb
    /// has had since M42.
    /// <para>
    /// The bins are chosen by <c>histcounts</c>' own rule — the model calls the same kernel, so the
    /// bars a script draws and the numbers it checks them against are one arithmetic rather than two
    /// that agree by inspection. Before M77 this verb took a count and nothing else, and the ten
    /// equal bins it always cut were the only histogram this build could draw.
    /// </para>
    /// </summary>
    private static JgsValue Histogram(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0 && args[0].Type == JgsType.Table)
        {
            ArityRange("histogram", args, 2, 3, line, col);
            int tableBins = args.Count == 3 ? Count("histogram", args, 2, line, col) : 10;
            return Handle(JG.Histogram(
                Tbl("histogram", args, 0, line, col), Str("histogram", args, 1, line, col), tableBins));
        }

        ParsedArgs parsed = HistogramChartOptions.Parse(args, 2, line, col);
        double[]? givenEdges = parsed.Vector("BinEdges");
        double[]? givenCounts = parsed.Vector("BinCounts");
        double[]? limits = parsed.Vector("BinLimits");
        double? width = parsed.Named("BinWidth") is null ? null : parsed.Scalar("BinWidth", 0);
        int? requested = parsed.Named("NumBins") is null
            ? null
            : (int)System.Math.Round(parsed.Scalar("NumBins", 10));
        string rule = parsed.Word(
            "BinMethod", "auto", "auto", "scott", "sturges", "sqrt", "fd", "integers");

        if (limits is { Length: not 2 })
        {
            throw new JgsRuntimeException(line, col, "histogram: 'BinLimits' takes a [low high] pair.");
        }

        if (width is { } step && (!(step > 0) || !double.IsFinite(step)))
        {
            throw new JgsRuntimeException(line, col, "histogram: 'BinWidth' must be positive.");
        }

        // A list of names is counted by name, and the bars stand on the counting numbers.
        if (parsed.Positional.Count > 0 && parsed.Positional[0].Type is JgsType.Cell)
        {
            string[] names = CellOfNames("histogram", parsed.Positional[0], line, col);
            (string[] distinct, double[] counts) = CountedByName(names);
            return Handle(HistogramTail(
                JG.Histogram(HistogramPlot.FromCategories(distinct, counts)), parsed, line, col));
        }

        double[]? data = null;
        if (givenCounts is not null)
        {
            if (parsed.Positional.Count > 0)
            {
                throw new JgsRuntimeException(line, col,
                    "histogram: 'BinCounts' is the counting already done, so there is no data to count.");
            }

            if (givenEdges is null)
            {
                throw new JgsRuntimeException(line, col,
                    "histogram: 'BinCounts' needs 'BinEdges' to say where the bins are.");
            }
        }
        else
        {
            if (parsed.Positional.Count == 0)
            {
                throw new JgsRuntimeException(line, col,
                    "histogram expects the values to count: histogram(x).");
            }

            data = FlattenColumnMajor("histogram", parsed.Positional[0], line, col);
            if (parsed.Positional.Count == 2)
            {
                JgsValue second = parsed.Positional[1];
                if (second.Type is JgsType.Number or JgsType.Bool)
                {
                    requested = Count("histogram", parsed.Positional, 1, line, col);
                    if (requested < 1)
                    {
                        throw new JgsRuntimeException(line, col, "histogram needs at least one bin.");
                    }
                }
                else
                {
                    givenEdges = ToDoubles("histogram", second, line, col);
                }
            }
        }

        if (givenEdges is { Length: < 2 })
        {
            throw new JgsRuntimeException(line, col, "histogram: bin edges come in twos or more.");
        }

        if (givenEdges is not null && (requested is not null || width is not null || limits is not null))
        {
            throw new JgsRuntimeException(line, col,
                "histogram: bin edges already say where every bin is, so a count, width or limits "
                + "cannot be given too.");
        }

        double[] edges = givenEdges ?? Binning.EdgesFor(data!, requested, width, limits, rule);
        HistogramPlot plot = data is null
            ? HistogramPlot.FromCounts(edges, givenCounts!)
            : new HistogramPlot(data, edges);
        plot.BinMethod = rule;
        if (limits is not null)
        {
            plot.BinLimits = limits;
        }

        return Handle(HistogramTail(JG.Histogram(plot), parsed, line, col));
    }

    /// <summary>How many times each name appears, in the order the names were first seen.</summary>
    private static (string[] Names, double[] Counts) CountedByName(IReadOnlyList<string> names)
    {
        var order = new List<string>();
        var counts = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!counts.ContainsKey(name))
            {
                order.Add(name);
                counts[name] = 0;
            }

            counts[name]++;
        }

        return ([.. order], [.. order.Select(name => counts[name])]);
    }

    /// <summary>The appearance options, applied after the bins are settled.</summary>
    private static HistogramPlot HistogramTail(HistogramPlot plot, ParsedArgs parsed, int line, int col)
    {
        plot.Normalization = parsed.Word(
            "Normalization", "count",
            "count", "countdensity", "cumcount", "probability", "pdf", "cdf") switch
        {
            "countdensity" => HistogramNormalization.CountDensity,
            "cumcount" => HistogramNormalization.Cumulative,
            "probability" => HistogramNormalization.Probability,
            "pdf" => HistogramNormalization.Density,
            "cdf" => HistogramNormalization.CumulativeProbability,
            _ => HistogramNormalization.Count,
        };

        plot.DisplayStyle = parsed.Word("DisplayStyle", "bar", "bar", "stairs") == "stairs"
            ? HistogramDisplayStyle.Stairs
            : HistogramDisplayStyle.Bar;
        plot.Orientation = parsed.Word("Orientation", "vertical", "vertical", "horizontal") == "horizontal"
            ? HistogramOrientation.Horizontal
            : HistogramOrientation.Vertical;

        if (parsed.Named("FaceColor") is { } face)
        {
            plot.FaceColor = OptionColor(face, line, col, "histogram");
        }

        if (parsed.Named("EdgeColor") is { } edge)
        {
            plot.EdgeColor = OptionColor(edge, line, col, "histogram");
        }

        plot.FaceAlpha = parsed.Scalar("FaceAlpha", plot.FaceAlpha);
        plot.EdgeAlpha = parsed.Scalar("EdgeAlpha", plot.EdgeAlpha);
        plot.LineWidth = parsed.Scalar("LineWidth", plot.LineWidth);
        plot.BarWidth = parsed.Scalar("BarWidth", plot.BarWidth);
        plot.NumDisplayBins = (int)System.Math.Round(parsed.Scalar("NumDisplayBins", plot.NumDisplayBins));
        plot.DisplayOrder = parsed.Word("DisplayOrder", "data", "data", "ascend", "descend") switch
        {
            "ascend" => CategoryDisplayOrder.Ascend,
            "descend" => CategoryDisplayOrder.Descend,
            _ => CategoryDisplayOrder.Data,
        };

        if (parsed.Text("ShowOthers") is { } others)
        {
            plot.ShowOthers = others.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        if (parsed.Text("LineStyle") is { } dash)
        {
            plot.LineStyle = ParseDashWord(dash, plot.LineStyle);
        }

        if (parsed.Text("DisplayName") is { } label)
        {
            SetDisplayName(plot, label);
        }

        if (parsed.Text("HandleVisibility") is { } visibility)
        {
            JgsHandleRegistry.EntryFor(plot).HandleVisible =
                !visibility.Equals("off", StringComparison.OrdinalIgnoreCase);
        }

        return plot;
    }

    /// <summary>
    /// <c>errorbar(y, err)</c>, <c>errorbar(x, y, err)</c>, <c>errorbar(x, y, neg, pos)</c>, each with
    /// an optional trailing <c>LineSpec</c>, and the table form.
    /// <para>
    /// Until M70 only the three-argument spelling ran; the other three refused by arity, which M69's
    /// form probe recorded. The asymmetric case needed nothing new below this: <c>ErrorBarPlot</c> has
    /// held a separate low and high array since M6 and the symmetric constructor passes the same one
    /// twice. The horizontal forms are a different matter and are refused by name — see below.
    /// </para>
    /// </summary>
    /// <summary>
    /// Takes the LineSpec out of a call that also has an option tail. It cannot simply be the last
    /// word, or the first word that looks like one: <c>'Marker', 's'</c> puts a perfectly good spec
    /// in the value slot, and peeling that would leave the option without its value. So the walk
    /// steps over each option name together with what follows it, and the spec is the first word
    /// left that spells one.
    /// </summary>
    private static (IReadOnlyList<JgsValue> Remaining, string? Spec) PeelSpecFor(
        OptionSpec spec, IReadOnlyList<JgsValue> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].Type != JgsType.String)
            {
                continue;
            }

            string word = args[i].AsString;
            if (spec.Knows(word))
            {
                if (spec.KnowsName(word))
                {
                    i++;
                }

                continue;
            }

            if (!IsLineSpecWord(word))
            {
                continue;
            }

            return ([.. args.Take(i), .. args.Skip(i + 1)], word);
        }

        return (args, null);
    }

    private static readonly OptionSpec ErrorBarOptions = new(
        "errorbar",
        Flags: ["vertical", "horizontal", "both"],
        Names:
        [
            "CapSize", "Color", "LineStyle", "LineWidth", "Marker", "MarkerSize",
            "MarkerEdgeColor", "MarkerFaceColor", "DisplayName", "HandleVisibility",
        ]);

    /// <summary>The named options, applied after the spec so that a name always wins.</summary>
    private static void ApplyErrorBarOptions(
        ErrorBarPlot plot, ParsedArgs parsed, int line, int col)
    {
        if (parsed.Named("Color") is { } color)
        {
            plot.Color = OptionColor(color, line, col, "errorbar");
        }

        if (parsed.Text("LineStyle") is { } dash)
        {
            plot.DashStyle = ParseDashWord(dash, plot.DashStyle);
        }

        if (parsed.Text("Marker") is { } markerWord)
        {
            plot.Marker = ParseMarkerWord(markerWord, plot.Marker);
        }

        if (parsed.Named("MarkerFaceColor") is { } face)
        {
            plot.MarkerFaceColor = face.Type == JgsType.String
                && face.AsString.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : OptionColor(face, line, col, "errorbar");
        }

        if (parsed.Named("MarkerEdgeColor") is { } edge)
        {
            plot.MarkerEdgeColor = OptionColor(edge, line, col, "errorbar");
        }

        plot.CapSize = parsed.Scalar("CapSize", plot.CapSize);
        plot.LineWidth = parsed.Scalar("LineWidth", plot.LineWidth);
        plot.MarkerSize = parsed.Scalar("MarkerSize", plot.MarkerSize);

        if (parsed.Text("DisplayName") is { } label)
        {
            SetDisplayName(plot, label);
        }

        if (parsed.Text("HandleVisibility") is { } visibility)
        {
            JgsHandleRegistry.EntryFor(plot).HandleVisible =
                !visibility.Equals("off", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static JgsValue ErrorBar(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0 && args[0].Type == JgsType.Table)
        {
            Arity("errorbar", args, 4, line, col);
            return Handle(JG.ErrorBar(Tbl("errorbar", args, 0, line, col), Str("errorbar", args, 1, line, col), Str("errorbar", args, 2, line, col), Str("errorbar", args, 3, line, col)));
        }

        // The LineSpec comes off first: it is a word and not an option name, and the parser is
        // right to refuse every word that is neither. The direction is an option word — it was
        // refused outright until M77, when the sideways whiskers were drawn for the first time.
        (IReadOnlyList<JgsValue> peeled, string? spec) = PeelSpecFor(ErrorBarOptions, args);
        ParsedArgs named = ErrorBarOptions.Parse(peeled, 6, line, col);
        string direction = named.Has("horizontal") ? "horizontal"
            : named.Has("both") ? "both"
            : "vertical";
        var rest = new List<JgsValue>(named.Positional);

        ArityRange("errorbar", rest, 2, 6, line, col);
        double[] first = DoubleArray("errorbar", rest, 0, line, col);
        double[] second = DoubleArray("errorbar", rest, 1, line, col);

        // errorbar(y, err) puts the samples at 1, 2, 3, ... — MATLAB's implicit x, and the same one
        // plot(y) uses. The dialect's index base does not enter into it: these are coordinates.
        (double[] xs, double[] ys, double[] neg, double[] pos) = rest.Count switch
        {
            2 => (Ramp1(first.Length), first, second, second),
            3 => (first, second, DoubleArray("errorbar", rest, 2, line, col),
                  DoubleArray("errorbar", rest, 2, line, col)),
            _ => (first, second, DoubleArray("errorbar", rest, 2, line, col),
                  DoubleArray("errorbar", rest, 3, line, col)),
        };

        // errorbar(x, y, yneg, ypos, xneg, xpos) — the six-vector form, whose last pair reaches
        // sideways. The three-and-four-argument forms mean the direction word instead.
        double[]? left = rest.Count == 6 ? DoubleArray("errorbar", rest, 4, line, col) : null;
        double[]? right = rest.Count == 6 ? DoubleArray("errorbar", rest, 5, line, col) : null;
        if (rest.Count == 5)
        {
            throw new JgsRuntimeException(line, col,
                "errorbar takes the sideways reaches in twos: errorbar(x, y, yneg, ypos, xneg, xpos).");
        }

        if (direction != "vertical" && left is null)
        {
            // 'horizontal' reads the one pair of magnitudes sideways instead of upright; 'both'
            // reads them in both directions, which is what MATLAB draws for a single err vector.
            left = neg;
            right = pos;
            if (direction == "horizontal")
            {
                (neg, pos) = (new double[xs.Length], new double[xs.Length]);
            }
        }

        ErrorBarPlot plot = JG.ErrorBar(xs, ys, neg, pos);
        plot.XImplied = rest.Count == 2;
        if (left is not null)
        {
            plot.ErrorLeft = left;
            plot.ErrorRight = right ?? left;
        }

        if (spec is not null)
        {
            LineSpec parsed = LineSpec.Parse(spec);
            if (parsed.Color is { } color)
            {
                plot.Color = color;
            }

            if (parsed.Dash is { } dash)
            {
                plot.DashStyle = dash;
            }

            if (parsed.Marker is { } marker)
            {
                plot.Marker = marker;
            }

            // 'o' alone is markers with no line, the same rule plot follows.
            if (parsed.Dash is null && parsed.Marker is not null)
            {
                plot.ShowLine = false;
            }
        }

        ApplyErrorBarOptions(plot, named, line, col);
        return Handle(plot);
    }

    /// <summary>
    /// Grows a colour grid to cover a surface that came back larger than the data it was built
    /// from, by repeating the border outwards.
    /// <para>
    /// <c>meshz</c> is the reason this exists: it hangs a skirt off the edge of the surface, so the
    /// grid it draws is two rows and two columns bigger than the <c>Z</c> it was handed, and
    /// <c>meshz(Z, C)</c> would otherwise refuse a colour array of exactly the documented size. The
    /// skirt takes the colour of the edge it hangs from, which is what MATLAB shows and the only
    /// reading that does not invent a value. A grid that is the right size already passes straight
    /// through, so every other verb is untouched.
    /// </para>
    /// </summary>
    private static double[,] SkirtColors(double[,] cData, double[,] z)
    {
        int rows = z.GetLength(0);
        int cols = z.GetLength(1);
        int haveRows = cData.GetLength(0);
        int haveCols = cData.GetLength(1);
        if (haveRows == rows && haveCols == cols)
        {
            return cData;
        }

        // Only a symmetric skirt is understood. Anything else is a size mismatch the model should
        // refuse by name rather than have papered over here.
        int padRows = (rows - haveRows) / 2;
        int padCols = (cols - haveCols) / 2;
        if (padRows < 0 || padCols < 0 || haveRows + (2 * padRows) != rows || haveCols + (2 * padCols) != cols)
        {
            return cData;
        }

        var grown = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            int sr = System.Math.Clamp(r - padRows, 0, haveRows - 1);
            for (int c = 0; c < cols; c++)
            {
                grown[r, c] = cData[sr, System.Math.Clamp(c - padCols, 0, haveCols - 1)];
            }
        }

        return grown;
    }

    /// <summary>
    /// Dispatches surf/mesh/meshc: (z) with a matrix, or (x, y, z) with either grid vectors, a
    /// meshgrid pair, or a genuinely parametric pair.
    /// </summary>
    private static JgsValue Surface3D(string name, IReadOnlyList<JgsValue> args, int line, int col,
        Func<double[], double[], double[,], PlotObject> full,
        Func<double[,], PlotObject> zOnly,
        Func<double[,], double[,], double[,], PlotObject> parametric,
        bool takesColorData = true)
    {
        // MATLAB's surf(Z, C) and surf(X, Y, Z, C) colour the surface by an array of their own
        // rather than by height. The trailing C is peeled here, once, because most verbs built on
        // this dispatcher document it and none of them read it before M70.
        //
        // surfl is the exception, and takesColorData is why it is spelled out rather than assumed:
        // its second argument is the light source's direction, so reading it as colour would take a
        // documented argument and quietly mean something else by it.
        // Every one of these verbs documents a trailing run of name/value pairs — surf(X, Y, Z,
        // 'FaceAlpha', 0.4, 'EdgeColor', 'none') is how a MATLAB script makes a surface translucent,
        // and this dispatcher took no options at all before M72. The names are handed to the same
        // property table set() writes through, so whatever the surface answers to is settable at
        // construction and an unknown name refuses by name. The data arguments are never strings, so
        // the first string is where the pairs begin.
        IReadOnlyList<JgsValue> options = [];
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i].Type != JgsType.String)
            {
                continue;
            }

            if ((args.Count - i) % 2 != 0)
            {
                throw new JgsRuntimeException(
                    line, col, $"{name}: every property after the data needs a value.");
            }

            options = [.. args.Skip(i)];
            args = [.. args.Take(i)];
            break;
        }

        double[,]? cData = null;
        if (takesColorData && args.Count is 2 or 4)
        {
            cData = Matrix(name, args, args.Count - 1, line, col);
            args = [.. args.Take(args.Count - 1)];
        }

        JgsValue Coloured(PlotObject drawn)
        {
            if (cData is not null && drawn is SurfacePlot surface)
            {
                try
                {
                    surface.CData = SkirtColors(cData, surface.Z);
                }
                catch (ArgumentException ex)
                {
                    throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
                }
            }

            JgsValue handle = Handle(drawn);
            if (options.Count > 0)
            {
                JgsHandleEntry entry = JgsHandleRegistry.EntryFor(drawn);
                for (int i = 0; i < options.Count; i += 2)
                {
                    JgsGraphicsProperties.Set(
                        entry, StrOf(name, options[i], line, col), options[i + 1], line, col);
                }
            }

            return handle;
        }

        if (args.Count == 1)
        {
            return Coloured(zOnly(Matrix(name, args, 0, line, col)));
        }

        Arity(name, args, 3, line, col);
        double[,] z = Matrix(name, args, 2, line, col);
        try
        {
            // A full X/Y pair that is really a meshgrid collapses to its generating vectors, which is
            // the rectilinear fast path and what every surface built on `meshgrid` wants. A pair that
            // varies in both directions -- a sphere, a cylinder -- has no generating vectors to
            // collapse to and is carried through per vertex.
            if (IsFullGrid(args[0]) && IsFullGrid(args[1]))
            {
                double[,] xGrid = Matrix(name, args, 0, line, col);
                double[,] yGrid = Matrix(name, args, 1, line, col);
                if (!IsRectilinearGrid(xGrid, yGrid))
                {
                    return Coloured(parametric(xGrid, yGrid, z));
                }
            }

            return Coloured(full(
                GridVector(name, args, 0, firstRow: true, line, col),
                GridVector(name, args, 1, firstRow: false, line, col),
                z));
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    /// <summary>Whether a grid argument arrived as a full matrix rather than a generating vector.</summary>
    private static bool IsFullGrid(JgsValue value) =>
        value.Type == JgsType.Array && value.Rows > 1 && value.Cols > 1;

    /// <summary>
    /// Whether an X/Y pair is what <c>meshgrid</c> produces: X the same down every column and Y the
    /// same across every row, so the pair carries no more information than two vectors. A NaN
    /// anywhere fails the comparison and keeps the grid parametric, which is the safe direction.
    /// </summary>
    private static bool IsRectilinearGrid(double[,] x, double[,] y)
    {
        int rows = x.GetLength(0);
        int cols = x.GetLength(1);
        if (y.GetLength(0) != rows || y.GetLength(1) != cols)
        {
            return false;
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (x[r, c] != x[0, c] || y[r, c] != y[r, 0])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// A grid coordinate argument as its generating vector: meshgrid's full X/Y matrices collapse
    /// to their first row/column (MATLAB accepts either form), a vector passes through.
    /// </summary>
    private static double[] GridVector(string name, IReadOnlyList<JgsValue> args, int index, bool firstRow, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type == JgsType.Array && value.Rows > 1 && value.Cols > 1)
        {
            int count = firstRow ? value.Cols : value.Rows;
            var vector = new double[count];
            for (int i = 0; i < count; i++)
            {
                // Column-major storage: element (r, c) sits at r + c*Rows.
                JgsValue element = value.ElementAt(firstRow ? i * value.Rows : i);
                vector[i] = element.AsNumber;
            }

            return vector;
        }

        return DoubleArray(name, args, index, line, col);
    }

    /// <summary>
    /// Dispatches contour/contourf/contour3. The three differ only in where the geometry ends up:
    /// flat lines, flat bands, or lines lifted to the height of the level each one traces.
    /// </summary>
    private static JgsValue Contour(
        string name, IReadOnlyList<JgsValue> args, int line, int col, bool filled, bool elevated = false)
    {
        ArityRange(name, args, 1, 4, line, col);

        // contour(Z) and contour(Z, levels) index the grid by row and column, the way surf(Z) does.
        // Two arguments cannot be an x and a y, so which form was meant is never in doubt.
        bool gridded = args.Count >= 3;
        int levelSlot = gridded ? 3 : 1;

        double[,] z = Matrix(name, args, gridded ? 2 : 0, line, col);
        double[] x = gridded
            ? GridVector(name, args, 0, firstRow: true, line, col)
            : Counting(z.GetLength(1));
        double[] y = gridded
            ? GridVector(name, args, 1, firstRow: false, line, col)
            : Counting(z.GetLength(0));

        double[]? levels = args.Count > levelSlot
            ? args[levelSlot].Type is JgsType.Number or JgsType.Bool
                ? [args[levelSlot].AsNumber]
                : ToDoubles(name, args[levelSlot], line, col)
            : null;

        // A scalar fourth argument is a level COUNT: n evenly spaced levels across z's range.
        if (levels is { Length: 1 } && levels[0] >= 2 && levels[0] == System.Math.Floor(levels[0]))
        {
            double zMin = double.PositiveInfinity, zMax = double.NegativeInfinity;
            foreach (double v in z)
            {
                if (!double.IsNaN(v))
                {
                    zMin = System.Math.Min(zMin, v);
                    zMax = System.Math.Max(zMax, v);
                }
            }

            int n = (int)levels[0];
            levels = new double[n];
            for (int i = 0; i < n; i++)
            {
                levels[i] = zMin + ((zMax - zMin) * (i + 1) / (n + 1));
            }
        }
        try
        {
            ContourPlot drawn = elevated ? JG.Contour3(x, y, z, levels)
                : filled ? JG.ContourF(x, y, z, levels)
                : JG.Contour(x, y, z, levels);

            // Whether the positions were counted out of the grid is what XDataMode reports, and the
            // one-matrix form is exactly the case where they were.
            drawn.XImplied = !gridded;
            drawn.YImplied = !gridded;
            return Handle(drawn);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, ex.Message);
        }
    }

    /// <summary>1, 2, … n — the coordinates a grid has when a verb was given heights and nothing else.</summary>
    private static double[] Counting(int n)
    {
        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = i + 1;
        }

        return values;
    }

    /// <summary>
    /// The coordinates a drawing verb given heights alone plots them against: MATLAB counts its
    /// samples from 1, and JGS numbers everything from 0.
    /// </summary>
    /// <remarks>
    /// The <see cref="JG"/> facade cannot know which script asked, so its own implicit x has always
    /// been 0-based — which is right for a JGS script and one place to the left for a <c>.m</c> file.
    /// The caller that does know the dialect says so here. <c>bar</c> and <c>area</c> never had the
    /// problem because they build their own coordinates; <c>plot</c>, <c>stem</c> and <c>stairs</c>
    /// let the facade choose, and so drew <c>plot(y)</c> from 0 while <c>bar(y)</c> drew from 1 in
    /// the same figure.
    /// </remarks>
    private static double[] ImplicitX(JgsDialect dialect, int n)
    {
        if (dialect.IsMatlab)
        {
            return Counting(n);
        }

        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = i;
        }

        return values;
    }

    private static JgsValue Semilog(
        string name, IReadOnlyList<JgsValue> args, int line, int col,
        Func<double[], double[], string?, PlotObject> apply)
    {
        ArityRange(name, args, 1, 3, line, col);

        // semilogy(y) counts along the whole numbers, exactly as plot(y) does — the one form of
        // these three that a script most often reaches for, and the one that used to be refused.
        // A trailing line spec still names the style, so semilogy(y, 'r--') is told from
        // semilogx(x, y) by whether the second argument is a word.
        double[] first = DoubleArray(name, args, 0, line, col);
        bool implicitX = args.Count == 1 || (args.Count == 2 && args[1].Type == JgsType.String);
        string? spec = implicitX
            ? args.Count == 2 ? Str(name, args, 1, line, col) : null
            : args.Count == 3 ? Str(name, args, 2, line, col) : null;

        if (implicitX)
        {
            var counted = new double[first.Length];
            for (int i = 0; i < counted.Length; i++)
            {
                counted[i] = i + 1;
            }

            return Handle(apply(counted, first, spec));
        }

        return Handle(apply(first, DoubleArray(name, args, 1, line, col), spec));
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

        if (TryPackedSpan(args, out NumericBuffer packed))
        {
            if (packed.Length == 0)
            {
                throw new JgsRuntimeException(line, col, $"{name} needs at least one value.");
            }

            // Min and Max answer with one of their inputs, so the order they are folded in cannot
            // change the answer — including the two orderings a fold can see, NaN beating everything
            // and negative zero beating positive zero.
            return JgsValue.Number(takeMin ? PackedMath.Min(packed) : PackedMath.Max(packed));
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
        try
        {
            return JgsValue.Table(args.Count == 2
                ? readSkipping(path, Count(name, args, 1, line, col))
                : read(path));
        }
        catch (JGraph.Data.Import.ImportException ex)
        {
            // A missing or malformed file is a script error, not a process crash.
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
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

    /// <summary>
    /// The variance behind <c>std</c>, <c>variance</c> and <c>var</c>, with MATLAB's weight argument:
    /// <c>0</c> (or absent, or <c>[]</c>) divides by n−1, <c>1</c> divides by n, and a vector of
    /// weights gives each observation its own say. <c>std(x, 1)</c> used to be read as "reduce along
    /// dimension 1" one layer up, which is the bug this argument exists to close.
    /// </summary>
    private static double SampleVariance(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 1, 2, line, col);
        double[] values = ArrayOfNumbers(name, [args[0]], line, col);

        // MATLAB answers 0 for one value under either normalization and NaN for none, rather than
        // refusing: a spread over a single reading is a real question with a boring answer.
        if (values.Length == 0)
        {
            return double.NaN;
        }

        if (values.Length == 1)
        {
            return 0;
        }

        if (args.Count < 2 || (args[1].Type == JgsType.Array && args[1].ArrayLength == 0))
        {
            return JgsStdlib.Variance(values);
        }

        if (args[1].Type is JgsType.Number or JgsType.Bool)
        {
            double flag = args[1].AsNumber;
            if (flag == 0)
            {
                return JgsStdlib.Variance(values);
            }

            if (flag != 1)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the weight must be 0 (divide by n-1), 1 (divide by n), or a vector of weights, but was {flag.ToString(CultureInfo.InvariantCulture)}.");
            }

            double mean = values.Sum() / values.Length;
            return values.Sum(v => (v - mean) * (v - mean)) / values.Length;
        }

        double[] weights = ToDoubles(name, args[1], line, col);
        if (weights.Length != values.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the weight vector has {weights.Length} values but the data has {values.Length}.");
        }

        double total = weights.Sum();
        if (total <= 0 || weights.Any(static w => w < 0))
        {
            throw new JgsRuntimeException(line, col, $"{name}: the weights must be non-negative and add up to more than zero.");
        }

        double weightedMean = values.Select((v, i) => v * weights[i]).Sum() / total;
        return values.Select((v, i) => weights[i] * (v - weightedMean) * (v - weightedMean)).Sum() / total;
    }

    /// <summary>A statistic of a list of numbers, or NaN when the list holds none (M96b).</summary>
    private static JgsValue EmptyOrNumber(double[] values, Func<double[], double> statistic) =>
        JgsValue.Number(values.Length == 0 ? double.NaN : statistic(values));

    private static double[] NonEmpty(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        double[] values = ArrayOfNumbers(name, args, line, col);
        if (values.Length == 0)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a non-empty array.");
        }

        return values;
    }

    /// <summary>
    /// find's result takes the orientation MATLAB gives it: a column when the subject is a matrix or
    /// a column, so <c>A(find(A &gt; 5))</c> comes back shaped the way the subject was searched.
    /// </summary>
    private static JgsValue FoundIndices(JgsValue indices, JgsValue subject)
    {
        // Finding nothing still answers with a shape (M96b), and the same rule decides it: a row
        // searched answers a row, everything else a column, and the shapeless 0-by-0 answers itself.
        // The > 1 test below never reached this, so every fruitless search came back a bare 1-by-0.
        if (indices.ArrayLength == 0)
        {
            int rows = JgsMatrix.RowCount(subject);
            int cols = JgsMatrix.ColCount(subject);
            (int foundRows, int foundCols) = rows == 0 && cols == 0 ? (0, 0) : rows == 1 ? (1, 0) : (0, 1);
            indices.Reshape(foundRows, foundCols);
            return indices;
        }

        if (indices.ArrayLength > 1 && (JgsMatrix.IsMatrix(subject) || subject.Cols == 1))
        {
            indices.Reshape(indices.ArrayLength, 1);
        }

        return indices;
    }

    internal static JgsValue MapToBool(string name, JgsValue value, Func<double, bool> test, int line, int col,
        Func<Complex, bool>? complexTest = null)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return JgsValue.Bool(test(value.AsNumber));
        }

        if (value.Type == JgsType.Complex && complexTest is not null)
        {
            return JgsValue.Bool(complexTest(value.AsComplex));
        }

        if (value.Type == JgsType.Array)
        {
            JgsValue[] source = value.BoxedElements();
            var result = new JgsValue[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].Type == JgsType.Array)
                {
                    // Recurse so a matrix (an array of rows) yields a matrix-shaped mask, matching
                    // how MapNumeric treats the same shape.
                    result[i] = MapToBool(name, source[i], test, line, col, complexTest);
                    continue;
                }

                if (source[i].Type == JgsType.Complex && complexTest is not null)
                {
                    result[i] = JgsValue.Bool(complexTest(source[i].AsComplex));
                    continue;
                }

                if (source[i].Type is not (JgsType.Number or JgsType.Bool))
                {
                    throw new JgsRuntimeException(line, col, $"{name} expects numeric array elements, but one was a {source[i].TypeName}.");
                }

                result[i] = JgsValue.Bool(test(source[i].AsNumber));
            }

            return JgsMatrix.Like(value, JgsValue.Array(result));
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

    // ScalarAsArray (M52) — why the four helpers below take a bare number where they ask for an array.
    //
    // A scalar is a 1-by-1 array. Rejecting one made sum(7), cumsum(5), diff(5) and every sibling an
    // error where MATLAB answers, and the error was never a considered decision: it was the guard that
    // stopped a scalar reaching AsArray and returning null. Promoting instead turns errors into
    // answers and cannot change an answer that already existed, because the builtins that mean
    // something different by a scalar branch on the type before they ever reach these helpers — the
    // elementwise max(a, b) form, the image reductions, the scalar constructors. Both dialects get it:
    // refusing a scalar was never part of the JGS surface.

    private static JgsValue[] Arr(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return [value]; // a scalar is a 1-by-1 array — see ScalarAsArray
        }

        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be an array, but got a {value.TypeName}.");
        }

        // Packed inputs materialize a boxed copy here — read-only use, never worse than the
        // all-boxed world; the hot builtins bypass this with packed fast paths.
        return value.BoxedElements();
    }


    private static JgsValue MapNumeric(string name, JgsValue value, Func<double, double> f, int line, int col,
                                       PackedMath.UnaryOp? vectorOp = null,
                                       PackedMath.Rounding? rounding = null)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return JgsValue.Number(f(value.AsNumber));
        }

        if (value.Type == JgsType.Array)
        {
            if (value.IsPacked)
            {
                // Same arithmetic over the flat buffer: bit-identical results, no per-element boxing.
                var dest = JgsPacking.Allocate(value.ArrayLength);
                if (rounding is { } rule)
                {
                    // A numeric class is not one of Math's functions but a rule about the element,
                    // and the kernel that carries it needs no delegate to apply it (M97).
                    PackedMath.Round(value.AsBuffer, dest, rule);
                }
                else
                {
                    ApplyUnary(value.AsBuffer, dest, f, vectorOp);
                }

                return JgsMatrix.Like(value, JgsValue.Packed(dest));
            }

            JgsValue[] source = value.AsArray;
            var result = new JgsValue[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                // Recurse so nested arrays map elementwise: sin(X) works on meshgrid output.
                result[i] = MapNumeric(name, source[i], f, line, col, vectorOp, rounding);
            }

            return JgsMatrix.Like(value, JgsValue.Array(result));
        }

        throw new JgsRuntimeException(line, col, $"{name} expects a number or numeric array, but got a {value.TypeName}.");
    }

    /// <summary>
    /// Pairwise elementwise application over two arrays (atan2(y, x), hypot, besselj, gammainc).
    /// </summary>
    /// <remarks>
    /// The shape rule is <see cref="JgsBroadcast"/>'s, not one of this method's own: two operands of
    /// the same shape are walked side by side, a scalar is spread over the other, and anything else
    /// goes to the expansion engine the elementwise operators use — so <c>atan2</c> of a column and
    /// a row is their outer table, exactly as <c>./</c> of the same two is. Until M106 this compared
    /// <em>lengths</em> and answered <see cref="JgsValue.Packed"/> without a shape, which refused
    /// that call outright and flattened <c>hypot(A, 1)</c> of a matrix into a row.
    /// </remarks>
    private static JgsValue Zip(string name, JgsValue a, JgsValue b, Func<double, double, double> f, int line, int col)
    {
        bool aScalar = a.Type is JgsType.Number or JgsType.Bool;
        bool bScalar = b.Type is JgsType.Number or JgsType.Bool;
        if (aScalar && bScalar)
        {
            return JgsValue.Number(f(a.AsNumber, b.AsNumber));
        }

        bool aArray = a.Type == JgsType.Array;
        bool bArray = b.Type == JgsType.Array;
        if (aArray && bArray && !JgsBroadcast.SameShape(a, b))
        {
            return JgsBroadcast.Map(a, b, name, line, col, (left, right) => Zip(name, left, right, f, line, col));
        }

        // Packed fast paths: the same delegate over flat buffers (atan2 over a million samples
        // without a million boxes), through the kernel rather than a loop here, so the chunking and
        // the buffer-lifetime discipline are the ones every other packed operation gets (M92).
        // Shapes outside these fall through to the boxed recursion.
        if (a.IsPacked && b.IsPacked)
        {
            var dest = JgsPacking.Allocate(a.ArrayLength);
            PackedMath.Zip(a.AsBuffer, b.AsBuffer, dest, f);
            return JgsMatrix.Like(a, JgsValue.Packed(dest));
        }

        if (a.IsPacked && bScalar)
        {
            var dest = JgsPacking.Allocate(a.ArrayLength);
            PackedMath.ZipScalar(a.AsBuffer, b.AsNumber, dest, f);
            return JgsMatrix.Like(a, JgsValue.Packed(dest));
        }

        if (aScalar && b.IsPacked)
        {
            var dest = JgsPacking.Allocate(b.ArrayLength);
            PackedMath.ZipScalar(b.AsBuffer, a.AsNumber, dest, f, scalarOnLeft: true);
            return JgsMatrix.Like(b, JgsValue.Packed(dest));
        }

        if (aArray && bArray)
        {
            JgsValue[] left = a.BoxedElements();
            JgsValue[] right = b.BoxedElements();
            var paired = new JgsValue[left.Length];
            for (int i = 0; i < paired.Length; i++)
            {
                paired[i] = Zip(name, left[i], right[i], f, line, col);
            }

            return JgsMatrix.Like(a, JgsValue.Array(paired));
        }

        if (aArray && bScalar)
        {
            JgsValue[] left = a.BoxedElements();
            var spread = new JgsValue[left.Length];
            for (int i = 0; i < spread.Length; i++)
            {
                spread[i] = Zip(name, left[i], b, f, line, col);
            }

            return JgsMatrix.Like(a, JgsValue.Array(spread));
        }

        if (aScalar && bArray)
        {
            JgsValue[] right = b.BoxedElements();
            var spread = new JgsValue[right.Length];
            for (int i = 0; i < spread.Length; i++)
            {
                spread[i] = Zip(name, a, right[i], f, line, col);
            }

            return JgsMatrix.Like(b, JgsValue.Array(spread));
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
    private static JgsValue MapPackedComplex(JgsPackedComplex source, Func<double, double> real,
        Func<Complex, JgsValue> complex, Func<Complex, double>? plain)
    {
        int count = source.Length;
        var reOut = JgsPacking.Allocate(count);
        Span<double> fromRe = source.Re.AsSpan();
        Span<double> fromIm = source.Im.AsSpan();

        if (plain is not null)
        {
            // Nothing this map can answer has an imaginary part, so nothing is minted to find that
            // out. The spectrum of four million samples used to build four million JgsValues here
            // on its way to abs(F), and reading the planes through AsSpan() once per element was
            // the other half of the price.
            Span<double> into = reOut.AsSpan();
            for (int i = 0; i < count; i++)
            {
                double im = fromIm[i];
                into[i] = im == 0 ? real(fromRe[i]) : plain(new Complex(fromRe[i], im));
            }

            return JgsValue.Packed(reOut);
        }

        var imOut = JgsPacking.Allocate(count);
        Span<double> intoRe = reOut.AsSpan();
        Span<double> intoIm = imOut.AsSpan();
        bool anyImaginary = false;
        for (int i = 0; i < count; i++)
        {
            double re = fromRe[i];
            double im = fromIm[i];
            JgsValue mapped = im == 0 ? JgsValue.Number(real(re)) : complex(new Complex(re, im));
            if (mapped.Type == JgsType.Number)
            {
                intoRe[i] = mapped.AsNumber;
                intoIm[i] = 0;
            }
            else
            {
                Complex written = mapped.AsComplex;
                intoRe[i] = written.Real;
                intoIm[i] = written.Imaginary;
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

    /// <summary>
    /// A packed elementwise map, through the kernel that names the operation when there is one and
    /// the caller's delegate when there is not. Both compute the same answers: an operation reaches
    /// <see cref="PackedMath.UnaryTiered"/> only when its vector form is exact or its scalar form is
    /// the very <see cref="System.Math"/> function the delegate calls (M92).
    /// </summary>
    private static void ApplyUnary(NumericBuffer source, NumericBuffer dest,
                                   Func<double, double> f, PackedMath.UnaryOp? vectorOp)
    {
        if (vectorOp is { } op)
        {
            PackedMath.UnaryTiered(op, source, dest);
        }
        else
        {
            PackedMath.Map(source, dest, f);
        }
    }

    /// <summary>Elementwise map that takes the real path for numbers and the complex path for complex values.</summary>
    private static JgsValue MapComplexAware(string name, JgsValue value, Func<double, double> real, Func<Complex, JgsValue> complex, int line, int col,
                                            PackedMath.UnaryOp? vectorOp = null, Func<Complex, double>? plain = null)
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
                ApplyUnary(value.AsBuffer, dest, real, vectorOp);
                return JgsMatrix.Like(value, JgsValue.Packed(dest));
            }

            if (value.IsPackedComplex)
            {
                // real(F) of a complex matrix is the same matrix's real parts — shape and all.
                return JgsMatrix.Like(value, MapPackedComplex(value.AsPackedComplex, real, complex, plain));
            }

            JgsValue[] source = value.BoxedElements();
            var result = new JgsValue[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = MapComplexAware(name, source[i], real, complex, line, col, vectorOp, plain);
            }

            return JgsMatrix.Like(value, JgsValue.Array(result));
        }

        throw new JgsRuntimeException(line, col, $"{name} expects a number or numeric array, but got a {value.TypeName}.");
    }

    /// <summary>
    /// An elementwise map whose real fast path can bail into complex answers: as long as every
    /// input satisfies <paramref name="staysReal"/> the packed flat path runs; the moment one does
    /// not (sqrt of a negative, log of a negative) the whole array maps through
    /// <paramref name="complexResult"/>, which is exactly MATLAB's promotion rule.
    /// </summary>
    private static JgsValue MapComplexProducing(string name, JgsValue value,
        Func<double, double> fastReal, Func<double, bool> staysReal, Func<Complex, JgsValue> complexResult,
        int line, int col, PackedMath.UnaryOp? vectorOp = null)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            double x = value.AsNumber;
            return staysReal(x) ? JgsValue.Number(fastReal(x)) : complexResult(new Complex(x, 0));
        }

        if (value.Type == JgsType.Complex)
        {
            return complexResult(value.AsComplex);
        }

        if (value.Type == JgsType.Array)
        {
            if (value.IsPacked && value.PackedKind == JgsPackedKind.Number)
            {
                var dest = JgsPacking.Allocate(value.ArrayLength);
                if (TryFlatRealMap(value.AsBuffer, dest, fastReal, staysReal, vectorOp))
                {
                    return JgsMatrix.Like(value, JgsValue.Packed(dest));
                }

                // One element left the reals, so the whole array promotes below and this buffer is
                // storage nobody will read.
                dest.Dispose();
            }

            JgsValue[] source = value.BoxedElements();
            var result = new JgsValue[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = MapComplexProducing(name, source[i], fastReal, staysReal, complexResult, line, col, vectorOp);
            }

            return JgsMatrix.Like(value, JgsValue.Array(result));
        }

        throw new JgsRuntimeException(line, col, $"{name} expects a number or numeric array, but got a {value.TypeName}.");
    }

    /// <summary>
    /// The body of <c>dir</c>: the names in a folder as a cell array of strings, folders suffixed with
    /// the directory separator, sorted ordinally. The bare-name echo of the cell <em>is</em> the
    /// listing, and <c>d = dir('*.m')</c> captures it — builtins have no nargout, so MATLAB's struct
    /// array form is deliberately not attempted. A missing folder yields an empty cell.
    /// </summary>
    private static JgsValue ListDirectory(JGraphScriptGlobals host, string query, int line, int col)
    {
        string directory;
        string pattern;
        string leaf = Path.GetFileName(query);
        if (query.Length == 0)
        {
            directory = ResolveFolder(host, string.Empty);
            pattern = "*";
        }
        else if (leaf.Contains('*', StringComparison.Ordinal) || leaf.Contains('?', StringComparison.Ordinal))
        {
            directory = ResolveFolder(host, Path.GetDirectoryName(query) ?? string.Empty);
            pattern = leaf;
        }
        else
        {
            // A plain name: a folder lists its contents; anything else is matched as a file name.
            string resolved = ResolveFolder(host, query);
            (directory, pattern) = Directory.Exists(resolved)
                ? (resolved, "*")
                : (Path.GetDirectoryName(resolved) ?? resolved, leaf);
        }

        if (!Directory.Exists(directory))
        {
            return JgsValue.Cell(System.Array.Empty<JgsValue>());
        }

        try
        {
            var names = new List<string>();
            foreach (string folder in Directory.EnumerateDirectories(directory, pattern))
            {
                names.Add(Path.GetFileName(folder) + Path.DirectorySeparatorChar);
            }

            foreach (string file in Directory.EnumerateFiles(directory, pattern))
            {
                names.Add(Path.GetFileName(file));
            }

            names.Sort(StringComparer.Ordinal);
            return JgsValue.Cell(names.Select(JgsValue.Str).ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new JgsRuntimeException(line, col, $"dir: cannot list '{directory}': {ex.Message}");
        }
    }

    /// <summary>Anchors a (possibly empty) relative folder to the run's working directory. Patterns
    /// cannot go through the workspace resolver — it probes for existing files.</summary>
    private static string ResolveFolder(JGraphScriptGlobals host, string path)
    {
        string baseDir = host.WorkingDirectory is { Length: > 0 } working
            ? working
            : Directory.GetCurrentDirectory();
        return path.Length == 0 ? baseDir : Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path);
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
    /// What <c>find</c>'s optional arguments mean, which is not the same question in the two dialects.
    /// MATLAB reads <c>find(X, k)</c> as "the first k of them", with <c>find(X, k, 'last')</c> for the
    /// other end. JGS reads the same slot as the index base, the escape hatch ADR 0028 §"find" put
    /// there so a script ported from MATLAB can ask for 1-based answers.
    /// </summary>
    /// <remarks>
    /// These readings cannot both be right, and MATLAB's has to win inside a <c>.m</c> file: a MATLAB
    /// script that wrote <c>find(mask, 1)</c> meant the first match and was silently getting every
    /// match instead, numbered from 1 — which is the same answer whenever there is exactly one, and a
    /// different one the moment there are two. The JGS reading stays put, because a JGS script that
    /// wrote it meant the base and would break.
    /// </remarks>
    private static (int Origin, int? Wanted, bool FromEnd) FindLimit(
        string name, IReadOnlyList<JgsValue> args, JgsDialect dialect, int line, int col)
    {
        if (!dialect.IsMatlab)
        {
            return (args.Count == 2 ? IndexOrigin(name, args, 1, line, col) : dialect.IndexBase, null, false);
        }

        int? wanted = null;
        if (args.Count >= 2)
        {
            wanted = Count(name, args, 1, line, col);
            if (wanted < 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: the number of matches to keep must be zero or more, but was {wanted}.");
            }
        }

        bool fromEnd = false;
        if (args.Count == 3)
        {
            string direction = Str(name, args, 2, line, col);
            fromEnd = direction.Equals("last", StringComparison.OrdinalIgnoreCase)
                || (direction.Equals("first", StringComparison.OrdinalIgnoreCase)
                    ? false
                    : throw new JgsRuntimeException(line, col,
                        $"{name}: the direction must be 'first' or 'last', but got '{direction}'."));
        }

        return (dialect.IndexBase, wanted, fromEnd);
    }

    /// <summary>
    /// The first (or last) <paramref name="wanted"/> of <paramref name="found"/>, in order either way
    /// — MATLAB's <c>find(X, 2, 'last')</c> answers the last two ascending, not reversed.
    /// </summary>
    private static List<T> Limited<T>(List<T> found, int? wanted, bool fromEnd)
    {
        if (wanted is not { } keep || keep >= found.Count)
        {
            return found;
        }

        return fromEnd ? found.GetRange(found.Count - keep, keep) : found.GetRange(0, keep);
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

    /// <summary>The size of a value per dimension, as <c>size</c>, <c>height</c> and <c>width</c> read it.</summary>
    /// <remarks>
    /// N-D arrays report all of theirs, images report height-width-channels, a table reports its rows
    /// and its variable count, and everything else is a plain 2-D value.
    /// </remarks>
    private static int[] SizeDims(JgsValue value) => value.Type switch
    {
        JgsType.Image when value.AsImage.Channels > 1 =>
            [value.AsImage.Height, value.AsImage.Width, value.AsImage.Channels],
        JgsType.Image => [value.AsImage.Height, value.AsImage.Width],

        // A string array reads its own shape and never the shape of what it holds (M63). Without
        // this, size("abc") answered 1-by-3 — the nested-array reading, which took the one string
        // inside for a row of three things — while numel and length both said 1.
        _ when value.IsStringArray => [value.Rows, value.Cols],
        JgsType.Array => JgsMatrix.DimsOf(value),
        // A char row with no characters in it is 0-by-0, which is what '' means in MATLAB and what
        // makes size('') answer [0 0] (M96b). MATLAB does keep a 1-by-0 char — blanks(0) is one —
        // but nothing here can tell the two apart, and '' is overwhelmingly the one scripts write.
        JgsType.String => value.AsString.Length == 0 ? [0, 0] : [1, value.AsString.Length],
        JgsType.Cell => [value.Rows, value.Cols],
        JgsType.Struct => [value.Rows, value.Cols], // a struct is 1-by-1 and a struct array is its shape (M65)
        JgsType.Sparse => [value.AsSparse.Rows, value.AsSparse.Cols],
        JgsType.Table => [value.AsTable.RowCount, value.AsTable.ColumnCount],
        _ => [1, 1],
    };

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

    /// <summary>
    /// Reads a MATLAB-style on/off switch. Command syntax turns <c>hold off</c> into <c>hold("off")</c>,
    /// and every non-empty string is truthy, so these switches must read the word rather than the
    /// truthiness. With no argument MATLAB toggles; JGS keeps its older "bare call turns it on".
    /// </summary>
    private static bool OnOff(
        string name, IReadOnlyList<JgsValue> args, int line, int col, JgsDialect dialect, Func<bool> current)
    {
        if (args.Count == 0)
        {
            return !dialect.MatlabFunctions || !current();
        }

        JgsValue value = args[0];
        if (value.Type == JgsType.String)
        {
            string word = value.AsString;
            if (word.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (word.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new JgsRuntimeException(line, col, $"{name} expects 'on' or 'off', but got '{word}'.");
        }

        return value.IsTruthy;
    }

    private static double[] DoubleArray(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type is not (JgsType.Array or JgsType.Number or JgsType.Bool))
        {
            throw new JgsRuntimeException(line, col, $"{name} expects argument {index + 1} to be an array, but got a {value.TypeName}.");
        }

        return ToDoubles(name, value, line, col);
    }

    /// <summary>
    /// The flat storage behind a one-argument reduction, when the argument already is flat storage
    /// (M92). A packed array — numbers or logicals, both stored as doubles — hands its buffer over
    /// to be read where <see cref="ToDoubles(string, JgsValue, int, int)"/> would have copied the
    /// whole of it first; everything
    /// else answers false and takes the road it always took.
    /// </summary>
    /// <remarks>
    /// The reduction the caller then runs has to be the same fold the boxed path runs, not merely a
    /// fold with the same answer in exact arithmetic: <see cref="PackedMath.Sum"/> is a left fold in
    /// index order for this reason, and min and max are order-free.
    /// </remarks>
    private static bool TryPackedSpan(IReadOnlyList<JgsValue> args, out NumericBuffer buffer)
    {
        if (args.Count == 1 && args[0].Type == JgsType.Array && args[0].IsPacked)
        {
            buffer = args[0].AsBuffer;
            return true;
        }

        buffer = null!;
        return false;
    }

    private static double[] ArrayOfNumbers(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity(name, args, 1, line, col);
        if (args[0].Type is not (JgsType.Array or JgsType.Number or JgsType.Bool))
        {
            throw new JgsRuntimeException(line, col, $"{name} expects an array, but got a {args[0].TypeName}.");
        }

        return ToDoubles(name, args[0], line, col);
    }

    /// <summary>
    /// Converts a JGS matrix — an array of equal-length numeric row arrays, e.g. the output of
    /// <c>meshgrid</c> or <c>zeros(r, c)</c> — to a <c>double[rows, cols]</c>. Ragged rows error.
    /// </summary>
    internal static double[,] Matrix(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects argument {index + 1} to be a matrix (an array of row arrays), but got a {value.TypeName}.");
        }

        if (!JgsMatrix.IsMatrix(value))
        {
            throw new JgsRuntimeException(line, col,
                $"{name} expects argument {index + 1} to be a matrix; build one with meshgrid, zeros(r, c), or a semicolon-rowed literal.");
        }

        // Packed storage goes straight into the rectangle: one pass over the buffer instead of a
        // jagged array built element by element and then copied again (M96b).
        if (value.IsPacked && value.PackedKind is JgsPackedKind.Number or JgsPackedKind.Bool)
        {
            int height = value.Rows;
            int width = value.Cols;
            var packed = new double[height, width];
            Span<double> flat = value.AsBuffer.AsSpan();
            for (int c = 0; c < width; c++)
            {
                int origin = c * height;
                for (int r = 0; r < height; r++)
                {
                    packed[r, c] = flat[origin + r];
                }
            }

            GC.KeepAlive(value);
            return packed;
        }

        double[][] rows = JgsMatrix.ToRows(name, value, line, col);
        int cols = rows.Length == 0 ? 0 : rows[0].Length;
        var result = new double[rows.Length, cols];
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                result[r, c] = rows[r][c];
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
    internal static double[] ToDoubles(string name, JgsValue array, int line, int col)
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

        if (array.Type is JgsType.Number or JgsType.Bool)
        {
            return [array.AsNumber]; // a scalar is a 1-by-1 array — see ScalarAsArray
        }

        if (array.Type != JgsType.Array)
        {
            // Without this a value reaches AsArray, which is null for anything else, and the caller
            // sees a NullReferenceException instead of being told what it actually passed.
            throw new JgsRuntimeException(line, col,
                $"{name} expects an array of numbers, but got a {array.TypeName}.");
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

    /// <summary>Points every ruler of an axes at one limit-fitting policy and refits at once.</summary>
    private static void SetLimitMethod(Core.Model.AxesModel axes, Core.Model.LimitMethod method)
    {
        foreach (Core.Model.AxisModel ruler in axes.XAxes)
        {
            ruler.LimitMethod = method;
        }

        foreach (Core.Model.AxisModel ruler in axes.YAxes)
        {
            ruler.LimitMethod = method;
        }

        axes.ZAxis.LimitMethod = method;
        axes.RAxis.LimitMethod = method;
        axes.RecomputeDataBounds();
    }
}
