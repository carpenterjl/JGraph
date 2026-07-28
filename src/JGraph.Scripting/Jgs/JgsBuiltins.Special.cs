using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The special functions of mathematical physics, forwarded to
/// <see cref="SpecialFunctions"/>: the gamma family, the error functions, the incomplete gamma and
/// beta integrals with their inverses, and the polygamma derivatives.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the special-function builtins (M38).</summary>
    private static void RegisterSpecialFunctionBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void Math1(string name, Func<double, double> f) =>
            Define(name, (args, line, col) => { Arity(name, args, 1, line, col); return MapNumeric(name, args[0], f, line, col); });

        void Math2(string name, Func<double, double, double> f) =>
            Define(name, (args, line, col) => { Arity(name, args, 2, line, col); return Zip(name, args[0], args[1], f, line, col); });

        Math1("erf", SpecialFunctions.Erf);
        Math1("erfc", SpecialFunctions.Erfc);
        Math1("erfcx", SpecialFunctions.ErfcScaled);
        Math1("erfinv", SpecialFunctions.ErfInverse);
        Math1("erfcinv", SpecialFunctions.ErfcInverse);

        Math1("gamma", SpecialFunctions.Gamma);
        Math1("gammaln", SpecialFunctions.LogGamma);
        Math2("beta", SpecialFunctions.Beta);
        Math2("betaln", SpecialFunctions.LogBeta);

        // gammainc and betainc take an optional trailing 'upper'/'lower' word choosing which end of
        // the integral is wanted; everything else about the call is element-wise.
        Define("gammainc", (args, line, col) =>
        {
            ArityRange("gammainc", args, 2, 3, line, col);
            bool upper = UpperTail("gammainc", args, 2, line, col);
            return Zip("gammainc", args[0], args[1],
                (x, a) => upper ? SpecialFunctions.GammaUpper(a, x) : SpecialFunctions.GammaLower(a, x), line, col);
        });

        Define("gammaincinv", (args, line, col) =>
        {
            ArityRange("gammaincinv", args, 2, 3, line, col);
            bool upper = UpperTail("gammaincinv", args, 2, line, col);
            return Zip("gammaincinv", args[0], args[1],
                (y, a) => SpecialFunctions.GammaInverse(a, y, upper), line, col);
        });

        Define("betainc", (args, line, col) =>
        {
            ArityRange("betainc", args, 3, 4, line, col);
            bool upper = UpperTail("betainc", args, 3, line, col);
            double a = Num("betainc", args, 1, line, col);
            double b = Num("betainc", args, 2, line, col);
            return MapNumeric("betainc", args[0],
                x => upper ? 1.0 - SpecialFunctions.BetaRegularized(x, a, b) : SpecialFunctions.BetaRegularized(x, a, b),
                line, col);
        });

        Define("betaincinv", (args, line, col) =>
        {
            ArityRange("betaincinv", args, 3, 4, line, col);
            bool upper = UpperTail("betaincinv", args, 3, line, col);
            double a = Num("betaincinv", args, 1, line, col);
            double b = Num("betaincinv", args, 2, line, col);
            return MapNumeric("betaincinv", args[0],
                y => SpecialFunctions.BetaInverse(upper ? 1.0 - y : y, a, b), line, col);
        });

        Define("psi", (args, line, col) =>
        {
            ArityRange("psi", args, 1, 2, line, col);

            // psi(x) is the digamma function; psi(k, x) is its k-th derivative.
            return args.Count == 1
                ? MapNumeric("psi", args[0], SpecialFunctions.Digamma, line, col)
                : MapNumeric("psi", args[1], x => SpecialFunctions.Polygamma(Count("psi", args, 0, line, col), x), line, col);
        });

        RegisterBesselBuiltins(Define);
    }

    // --- Bessel and Airy ------------------------------------------------------------------------------

    /// <summary>
    /// The cylinder functions (M39). All four take an optional trailing scale flag, which for J and Y
    /// of a real argument is a no-op — MATLAB scales those by e^-|Im z|, and there is no imaginary
    /// part here — but for I and K is the difference between an answer and an overflow.
    /// </summary>
    private static void RegisterBesselBuiltins(Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>> Define)
    {
        void Cylinder(string name, Func<double, double, bool, double> f) =>
            Define(name, (args, line, col) =>
            {
                ArityRange(name, args, 2, 3, line, col);
                bool scaled = ScaleWanted(name, args, 2, line, col);
                return Guarded(name, line, col, () => Zip(name, args[0], args[1], (nu, x) => f(nu, x, scaled), line, col));
            });

        Cylinder("besselj", static (nu, x, _) => BesselFunctions.J(nu, x));
        Cylinder("bessely", static (nu, x, _) => BesselFunctions.Y(nu, x));
        Cylinder("besseli", static (nu, x, scaled) => BesselFunctions.I(nu, x, scaled));
        Cylinder("besselk", static (nu, x, scaled) => BesselFunctions.K(nu, x, scaled));

        Define("besselh", (args, line, col) =>
        {
            ArityRange("besselh", args, 2, 4, line, col);

            // besselh(nu, Z) means the first kind; the three-argument form names it explicitly, and
            // a fourth argument is the scale flag.
            bool named = args.Count >= 3 && args[1].Type is JgsType.Number or JgsType.Bool;
            int kind = named ? Count("besselh", args, 1, line, col) : 1;
            JgsValue z = named ? args[2] : args[1];
            if (kind is not (1 or 2))
            {
                throw new JgsRuntimeException(line, col, $"besselh: the kind must be 1 or 2, not {kind}.");
            }

            return Guarded("besselh", line, col, () => ZipToValue("besselh", args[0], z, (nu, x) =>
            {
                Complex h = BesselFunctions.H(nu, kind, x);
                return JgsValue.ComplexNum(h);
            }, line, col));
        });

        Define("airy", (args, line, col) =>
        {
            ArityRange("airy", args, 1, 3, line, col);

            // airy(Z) is Ai; airy(k, Z) names which of the four is wanted.
            bool named = args.Count >= 2;
            int kind = named ? Count("airy", args, 0, line, col) : 0;
            JgsValue z = named ? args[1] : args[0];
            if (kind is < 0 or > 3)
            {
                throw new JgsRuntimeException(line, col, $"airy: the kind must be 0 (Ai), 1 (Ai'), 2 (Bi), or 3 (Bi'), not {kind}.");
            }

            bool scaled = ScaleWanted("airy", args, 2, line, col);
            return Guarded("airy", line, col, () => MapNumeric("airy", z, x => BesselFunctions.Airy(kind, x, scaled), line, col));
        });
    }

    /// <summary>Reads the optional trailing scale flag the Bessel and Airy builtins accept.</summary>
    private static bool ScaleWanted(string name, IReadOnlyList<JgsValue> args, int index, int line, int col) =>
        index < args.Count && Num(name, args, index, line, col) != 0;

    /// <summary>
    /// Turns the "this argument would make the answer complex" refusal the kernel raises into a
    /// script-level error carrying the call's position, so the message names the line it came from.
    /// </summary>
    private static JgsValue Guarded(string name, int line, int col, Func<JgsValue> body)
    {
        try
        {
            return body();
        }
        catch (ArgumentOutOfRangeException error)
        {
            throw new JgsRuntimeException(line, col, $"{name}: {error.Message.Split(" (Parameter")[0]}");
        }
    }

    /// <summary>
    /// Pairwise elementwise application producing whole values rather than doubles, so a builtin
    /// whose answer is complex — besselh — can broadcast the same way the real ones do.
    /// </summary>
    private static JgsValue ZipToValue(
        string name, JgsValue a, JgsValue b, Func<double, double, JgsValue> f, int line, int col)
    {
        bool aScalar = a.Type is JgsType.Number or JgsType.Bool;
        bool bScalar = b.Type is JgsType.Number or JgsType.Bool;

        if (aScalar && bScalar)
        {
            return f(a.AsNumber, b.AsNumber);
        }

        if (aScalar || bScalar)
        {
            JgsValue[] many = (aScalar ? b : a).BoxedElements();
            var broadcast = new JgsValue[many.Length];
            for (int i = 0; i < many.Length; i++)
            {
                broadcast[i] = aScalar
                    ? ZipToValue(name, a, many[i], f, line, col)
                    : ZipToValue(name, many[i], b, f, line, col);
            }

            return JgsValue.Array(broadcast);
        }

        JgsValue[] xs = a.BoxedElements();
        JgsValue[] ys = b.BoxedElements();
        if (xs.Length != ys.Length)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs arrays of equal length ({xs.Length} and {ys.Length}).");
        }

        var result = new JgsValue[xs.Length];
        for (int i = 0; i < xs.Length; i++)
        {
            result[i] = ZipToValue(name, xs[i], ys[i], f, line, col);
        }

        return JgsValue.Array(result);
    }

    /// <summary>Reads the optional 'upper'/'lower' tail word the incomplete integrals accept.</summary>
    private static bool UpperTail(string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        if (index >= args.Count)
        {
            return false;
        }

        string tail = Str(name, args, index, line, col);
        return tail switch
        {
            "upper" => true,
            "lower" => false,
            _ => throw new JgsRuntimeException(line, col, $"{name}: the tail must be 'lower' or 'upper', not '{tail}'."),
        };
    }
}
