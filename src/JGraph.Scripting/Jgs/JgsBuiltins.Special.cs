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
