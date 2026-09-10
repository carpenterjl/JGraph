using JGraph.Signal;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The twenty-one window names of the Signal Processing Toolbox, and <c>window</c>, which calls any
/// of them by handle (M132).
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic is in <see cref="SignalWindows"/> and <see cref="DiscreteProlate"/>; what lives
/// here is the grammar, and the grammar is not uniform. Seven of the windows take a
/// <c>'symmetric'</c> or <c>'periodic'</c> flag and the rest do not, five take a shape parameter of
/// their own, and <c>dpss</c> takes two numbers, an optional index or index pair, an interpolation
/// method and a trace flag. Trying to serve all of that from one parser would produce a function
/// that accepts <c>bartlett(8, 'periodic')</c>, which MATLAB does not.
/// </para>
/// <para>
/// Every one of them answers a column, and every one of them treats a length of zero as an empty
/// column and a length of one as a bare one — the two lengths at which no window has a shape.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The windows that take nothing but a length.</summary>
    private static readonly string[] PlainWindowNames =
        ["barthannwin", "bartlett", "bohmanwin", "boxcar", "parzenwin", "rectwin", "triang"];

    /// <summary>The windows that take a symmetric-or-periodic flag.</summary>
    private static readonly string[] FlaggedWindowNames =
        ["blackman", "blackmanharris", "flattopwin", "hamming", "hann", "hanning", "nuttallwin"];

    /// <summary>Registers every window name and <c>window</c> itself.</summary>
    internal static void RegisterSignalWindowBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)));

        foreach (string name in PlainWindowNames)
        {
            string captured = name;
            Define(captured, (args, line, col) => PlainWindow(captured, args, line, col));
        }

        foreach (string name in FlaggedWindowNames)
        {
            string captured = name;
            Define(captured, (args, line, col) => FlaggedWindow(captured, args, line, col));
        }

        Define("kaiser", (args, line, col) => ShapedWindow("kaiser", args, line, col));
        Define("chebwin", (args, line, col) => ShapedWindow("chebwin", args, line, col));
        Define("gausswin", (args, line, col) => ShapedWindow("gausswin", args, line, col));
        Define("tukeywin", (args, line, col) => ShapedWindow("tukeywin", args, line, col));
        Define("taylorwin", (args, line, col) => ShapedWindow("taylorwin", args, line, col));

        env.Builtins.Register("dpss", JgsValue.Function(new BuiltinFunction(
            "dpss", (args, line, col) => Slepian(args, 1, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => Slepian(args, wanted, line, col),
        }));

        env.Builtins.Register("window", JgsValue.Function(new BuiltinFunction(
            "window", NamedWindow)));
    }

    /// <summary>Whether <paramref name="name"/> is one of the windows <c>window</c> can call.</summary>
    private static bool IsWindowName(string name) =>
        Array.IndexOf(PlainWindowNames, name) >= 0
        || Array.IndexOf(FlaggedWindowNames, name) >= 0
        || name is "kaiser" or "chebwin" or "gausswin" or "tukeywin" or "taylorwin";

    /// <summary><c>window(@wname, n, ...)</c>: the window named by a handle or a string.</summary>
    private static JgsValue NamedWindow(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("window", args, 2, 5, line, col);
        string name = args[0].Type switch
        {
            JgsType.Function => args[0].AsCallable.Name,
            JgsType.String => args[0].AsString,
            _ => throw new JgsRuntimeException(line, col,
                "window's first argument names a window, as a handle or as text."),
        };

        if (!IsWindowName(name))
        {
            throw new JgsRuntimeException(line, col, $"window does not know a window called '{name}'.");
        }

        var rest = new List<JgsValue>();
        for (int i = 1; i < args.Count; i++)
        {
            rest.Add(args[i]);
        }

        if (Array.IndexOf(PlainWindowNames, name) >= 0)
        {
            return PlainWindow(name, rest, line, col);
        }

        return Array.IndexOf(FlaggedWindowNames, name) >= 0
            ? FlaggedWindow(name, rest, line, col)
            : ShapedWindow(name, rest, line, col);
    }

    /// <summary>The window length, rounded the way MATLAB rounds one that is not whole.</summary>
    private static int WindowLength(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (IsEmptyValue(args[0]))
        {
            return 0;
        }

        double given = Num(name, args, 0, line, col);
        if (given < 0 || double.IsNaN(given) || double.IsInfinity(given))
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: a window length is a finite whole number that is not negative.");
        }

        return (int)System.Math.Round(given, MidpointRounding.AwayFromZero);
    }

    /// <summary>A column of window coefficients as a script value.</summary>
    private static JgsValue WindowColumn(double[] w) =>
        w.Length == 0 ? JgsMatrix.FromColumnMajor(w, 0, 1) : JgsMatrix.FromColumnMajor(w, w.Length, 1);

    /// <summary>The windows that take a length and nothing else.</summary>
    private static JgsValue PlainWindow(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 1, 1, line, col);
        int n = WindowLength(name, args, line, col);
        double[] w = name switch
        {
            "barthannwin" => SignalWindows.BartlettHann(n),
            "bartlett" => SignalWindows.Bartlett(n),
            "bohmanwin" => SignalWindows.Bohman(n),
            "parzenwin" => SignalWindows.Parzen(n),
            "triang" => SignalWindows.Triangular(n),
            _ => SignalWindows.Rectangular(n),
        };

        return WindowColumn(w);
    }

    /// <summary>The windows that take a symmetric-or-periodic flag.</summary>
    private static JgsValue FlaggedWindow(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 1, 2, line, col);
        int n = WindowLength(name, args, line, col);
        bool periodic = false;
        if (args.Count == 2 && !IsEmptyValue(args[1]))
        {
            string flag = Str(name, args, 1, line, col);
            periodic = flag.Equals("periodic", StringComparison.OrdinalIgnoreCase);
            if (!periodic && !flag.Equals("symmetric", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: a window is 'symmetric' or 'periodic', not '{flag}'.");
            }
        }

        double[] w = name switch
        {
            "blackman" => SignalWindows.Blackman(n, periodic),
            "blackmanharris" => SignalWindows.BlackmanHarris(n, periodic),
            "flattopwin" => SignalWindows.FlatTop(n, periodic),
            "hamming" => SignalWindows.Hamming(n, periodic),
            "hann" => SignalWindows.Hann(n, periodic),
            "hanning" => SignalWindows.Hanning(n, periodic),
            _ => SignalWindows.Nuttall(n, periodic),
        };

        return WindowColumn(w);
    }

    /// <summary>The windows that take a shape parameter of their own.</summary>
    private static JgsValue ShapedWindow(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 1, name == "taylorwin" ? 3 : 2, line, col);
        int n = WindowLength(name, args, line, col);
        bool given = args.Count >= 2 && !IsEmptyValue(args[1]);
        try
        {
            double[] w = name switch
            {
                "kaiser" => SignalWindows.Kaiser(n, given ? Num(name, args, 1, line, col) : 0.5),
                "chebwin" => SignalWindows.Chebyshev(n, given ? Num(name, args, 1, line, col) : 100.0),
                "gausswin" => SignalWindows.Gaussian(n, given ? Num(name, args, 1, line, col) : 2.5),
                "tukeywin" => SignalWindows.Tukey(n, given ? Num(name, args, 1, line, col) : 0.5),
                _ => SignalWindows.Taylor(
                    n,
                    given ? Count(name, args, 1, line, col) : 4,
                    args.Count >= 3 && !IsEmptyValue(args[2]) ? Num(name, args, 2, line, col) : -30.0),
            };

            return WindowColumn(w);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {ex.Message}");
        }
    }

    /// <summary><c>dpss(N, NW)</c> and the rest of its grammar.</summary>
    private static JgsValue[] Slepian(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("dpss", args, 2, 6, line, col);
        int n = Count("dpss", args, 0, line, col);
        double nw = Num("dpss", args, 1, line, col);

        int first = 1;
        int last = System.Math.Max(1, System.Math.Min(
            (int)System.Math.Round(2 * nw, MidpointRounding.AwayFromZero), n));
        string method = "calc";
        for (int i = 2; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.String)
            {
                string word = args[i].AsString.ToLowerInvariant();
                if (word is "calc" or "spline" or "linear")
                {
                    method = word;
                    continue;
                }

                if (word is "trace" or "notrace")
                {
                    continue;
                }

                throw new JgsRuntimeException(line, col,
                    $"dpss: '{args[i].AsString}' is not one of 'calc', 'spline', 'linear' or 'trace'.");
            }

            if (i == 2)
            {
                double[] k = NumericVector("dpss", args, 2, line, col);
                if (k.Length == 1)
                {
                    last = (int)k[0];
                }
                else if (k.Length == 2)
                {
                    first = (int)k[0];
                    last = (int)k[1];
                }
                else
                {
                    throw new JgsRuntimeException(line, col, "dpss's K is a number or a pair of them.");
                }
            }
        }

        if (method != "calc")
        {
            // The interpolated forms read a stored table of sequences from disk. There is no such
            // table here, and computing the sequences directly is both available and better.
            throw new JgsRuntimeException(line, col,
                $"dpss: '{method}' interpolates from a stored table of sequences, which JGraph does not keep; "
                + "ask for 'calc'.");
        }

        try
        {
            (double[,] sequences, double[] concentrations) = DiscreteProlate.Compute(n, nw, first, last);
            int columns = concentrations.Length;
            var flat = new double[n * columns];
            for (int c = 0; c < columns; c++)
            {
                for (int r = 0; r < n; r++)
                {
                    flat[(c * n) + r] = sequences[r, c];
                }
            }

            JgsValue e = JgsMatrix.FromColumnMajor(flat, n, columns);
            if (wanted <= 1)
            {
                return [e];
            }

            return [e, JgsMatrix.FromColumnMajor(concentrations, columns, 1)];
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"dpss: {ex.Message}");
        }
    }
}
