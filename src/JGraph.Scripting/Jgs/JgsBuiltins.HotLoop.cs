namespace JGraph.Scripting.Jgs;

/// <summary>
/// The scalar kernels the hot-loop compiler binds (M98). Every entry here is the same code the
/// builtin of that name applies element by element — a delegate to the identical static, or the
/// identical BCL method — so a compiled loop and the tree walk cannot disagree by construction.
/// There is deliberately no second implementation of any of this arithmetic.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>MATLAB <c>mod(x, m)</c> for one element: the result takes the divisor's sign; a zero divisor answers x.</summary>
    internal static double ScalarMod(double x, double m) =>
        m == 0 ? x : x - (System.Math.Floor(x / m) * m);

    /// <summary>MATLAB <c>rem(x, m)</c> for one element: the result takes the dividend's sign; a zero divisor answers NaN.</summary>
    internal static double ScalarRem(double x, double m) =>
        m == 0 ? double.NaN : x - (m * System.Math.Truncate(x / m));

    /// <summary>
    /// The unary scalar kernel a whitelisted builtin applies to a real element, with the predicate
    /// naming its real domain (null when it never leaves the reals). False for any other name: the
    /// compiler refuses the call and the loop stays on the tree walk.
    /// </summary>
    /// <remarks>
    /// Each pair restates a registration in <see cref="CreateGlobals"/>: <c>sin</c> is
    /// <see cref="System.Math.Sin"/> with no domain, <c>sqrt</c> is <see cref="System.Math.Sqrt"/>
    /// inside <see cref="NonNegative"/>, and so on. <c>round</c> binds
    /// <see cref="RoundAwayFromZero"/>, which both of its registrations use for the one-argument
    /// form. Outside the domain the answer is complex, no register can hold it, and the compiled op
    /// hands the whole statement back to the walk.
    /// </remarks>
    internal static bool TryHotLoopUnary(string name, out Func<double, double> kernel, out Func<double, bool>? staysReal)
    {
        (kernel, staysReal) = name switch
        {
            "sin" => ((Func<double, double>)System.Math.Sin, (Func<double, bool>?)null),
            "cos" => (System.Math.Cos, null),
            "tan" => (System.Math.Tan, null),
            "atan" => (System.Math.Atan, null),
            "exp" => (System.Math.Exp, null),
            "abs" => (System.Math.Abs, null),
            "floor" => (System.Math.Floor, null),
            "ceil" => (System.Math.Ceiling, null),
            "round" => (RoundAwayFromZero, null),
            "sqrt" => (System.Math.Sqrt, NonNegative),
            "log" => (System.Math.Log, NonNegative),
            "log10" => (System.Math.Log10, NonNegative),
            "asin" => (System.Math.Asin, InsideUnit),
            "acos" => (System.Math.Acos, InsideUnit),
            _ => (null!, null),
        };

        return kernel is not null;
    }

    /// <summary>
    /// Whether a two-argument call of <paramref name="name"/> on scalars is one of the compiled
    /// binary opcodes. The opcodes bind: <c>mod</c> to <see cref="ScalarMod"/>, <c>rem</c> to
    /// <see cref="ScalarRem"/>, <c>atan2</c> to <see cref="System.Math.Atan2"/>, and <c>min</c>/
    /// <c>max</c> to <see cref="System.Math.Min(double, double)"/>/<see cref="System.Math.Max(double, double)"/> — for two scalars
    /// the reduction wrapper hands straight to the inner fold, which is exactly that call.
    /// </summary>
    internal static bool IsHotLoopBinary(string name) =>
        name is "mod" or "rem" or "atan2" or "min" or "max";

    /// <summary>
    /// The zero-argument constants a bare mention may fold at loop entry: names whose builtin answers
    /// the same number every call. Anything else mentioned bare (<c>rand</c>, a user function on the
    /// path) refuses the fast path — the walk calls it per read, and per read it must stay.
    /// </summary>
    internal static bool IsHotLoopBareConstant(string name) =>
        name is "pi" or "eps" or "Inf" or "inf" or "NaN" or "nan" or "realmax" or "realmin" or "flintmax";
}
