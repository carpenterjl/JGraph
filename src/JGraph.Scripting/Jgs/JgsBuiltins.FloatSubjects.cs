namespace JGraph.Scripting.Jgs;

/// <summary>
/// The verbs R2025b defines only on floating point, and refuses for a logical, a char or an integer
/// class (open item 299, ADR 0355).
/// </summary>
/// <remarks>
/// <para>
/// Measured rather than recalled: every elementary function was handed <c>true</c>, <c>int32(4)</c>,
/// <c>'a'</c>, <c>single(0.5)</c> and <c>uint8(3)</c> in R2025b. The transcendental family — the
/// roots, exponentials and logarithms, every trigonometric and hyperbolic function in radians and
/// degrees with their inverses, gamma, the error functions, <c>psi</c>, <c>beta</c>, <c>hypot</c>,
/// <c>atan2</c> and <c>det</c> — refuses all four non-floats with one sentence,
/// <c>MATLAB:math:mustBeFloat</c>, and answers the single. The matrix verbs (<c>inv</c>, <c>eig</c>,
/// <c>svd</c>, <c>norm</c>, <c>expm</c>, …) ask the same of their first argument in their own
/// words, and so does the outlier family. The rounding, sign and arithmetic verbs (<c>abs</c>,
/// <c>floor</c>, <c>mod</c>, <c>power</c>, …) answer all five, and are not here.
/// </para>
/// <para>
/// ADR 0125 left the integer half of this as a divergence rather than make a running script stop;
/// the logical half was reported as open item 299, and R2025b's rule is one rule over all three
/// non-floats, so it is implemented as one (ADR 0355). JGS code keeps answering: the running dialect
/// is read at the call.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The verbs whose every argument must be double or single, in R2025b's one sentence.</summary>
    private static readonly string[] MustBeFloatBuiltins =
    [
        "sqrt", "realsqrt", "exp", "expm1", "pow2", "log", "log2", "log10", "log1p", "reallog",
        "sin", "cos", "tan", "sec", "csc", "cot", "asin", "acos", "atan", "asec", "acsc", "acot",
        "sinh", "cosh", "tanh", "sech", "csch", "coth", "asinh", "acosh", "atanh", "asech", "acsch", "acoth",
        "sind", "cosd", "tand", "secd", "cscd", "cotd", "asind", "acosd", "atand", "asecd", "acscd", "acotd",
        "sinpi", "cospi", "gamma", "gammaln", "psi", "beta", "betaln",
        "erf", "erfc", "erfinv", "erfcinv", "erfcx",
        "hypot", "atan2", "atan2d", "det", "rcond", "airy", "besselj",
    ];

    /// <summary>The matrix verbs' refusals of a non-float first argument, each as R2025b words it.</summary>
    private static readonly (string Name, string Id, string Message)[] MatrixInputTypeRefusals =
    [
        ("inv", "MATLAB:inv:inputType", "Invalid data type. Input matrix must be double or single."),
        ("eig", "MATLAB:eig:inputType", "Invalid data type. Input matrix must be double or single."),
        ("lu", "MATLAB:lu:inputType", "Invalid data type. First argument must be double or single."),
        ("qr", "MATLAB:qr:inputType", "Invalid data type. First argument must be double or single."),
        ("chol", "MATLAB:chol:inputType", "Invalid data type. First argument must be a double or single matrix."),
        ("svd", "MATLAB:svd:inputType", "First input must be single or double."),
        ("rank", "MATLAB:svd:inputType", "First input must be single or double."),
        ("pinv", "MATLAB:svd:inputType", "First input must be single or double."),
        ("null", "MATLAB:svd:inputType", "First input must be single or double."),
        ("orth", "MATLAB:svd:inputType", "First input must be single or double."),
        ("cond", "MATLAB:svd:inputType", "First input must be single or double."),
        ("norm", "MATLAB:norm:inputType", "First argument must be single or double."),
        ("rat", "MATLAB:norm:inputType", "First argument must be single or double."),
        ("expm", "MATLAB:expm:inputType", "Input must be single or double matrix."),
        ("sqrtm", "MATLAB:sqrtm:inputType", "Input must be a single or double matrix."),
        ("logm", "MATLAB:logm:inputType", "Input must be a single or double matrix."),
    ];

    /// <summary>
    /// Wraps each floating-only verb so a non-float argument is refused as R2025b refuses it. Applied
    /// after <see cref="CarryNumericClass"/>, so the class wrapper below never sees a refused value.
    /// </summary>
    private static void RefuseNonFloatSubjects(JgsEnvironment env, JgsRunningDialect dialect)
    {
        const string mustBeFloat = "Invalid data type. Argument must be double or single.";
        foreach (string name in MustBeFloatBuiltins)
        {
            GuardSubjects(env, dialect, name, (args, line, col) =>
            {
                foreach (JgsValue arg in args)
                {
                    if (NonFloatClassName(arg) is not null)
                    {
                        throw new JgsRuntimeException(line, col, "MATLAB:math:mustBeFloat", mustBeFloat);
                    }
                }
            });
        }

        foreach (string name in (string[])["deg2rad", "rad2deg"])
        {
            GuardSubjects(env, dialect, name, (args, line, col) =>
            {
                if (args.Count > 0 && NonFloatClassName(args[0]) is not null)
                {
                    throw new JgsRuntimeException(line, col, $"MATLAB:{name}:nonFloatInput", "Input must be single or double.");
                }
            });
        }

        // The matrix verbs ask the same question of their first argument in their own words.
        foreach ((string name, string id, string message) in MatrixInputTypeRefusals)
        {
            GuardSubjects(env, dialect, name, (args, line, col) =>
            {
                if (args.Count > 0 && NonFloatClassName(args[0]) is not null)
                {
                    throw new JgsRuntimeException(line, col, id, message);
                }
            });
        }

        // The outlier family names the type it refused, after validateattributes: a char says only
        // what it expected. filloutliers counts its arguments first (filloutliers(true) is minrhs).
        foreach ((string name, string owner, int least) in (ReadOnlySpan<(string, string, int)>)
                 [("isoutlier", "isoutlier", 1), ("rmoutliers", "isoutlier", 1), ("filloutliers", "filloutliers", 2)])
        {
            GuardSubjects(env, dialect, name, (args, line, col) =>
            {
                if (args.Count < least)
                {
                    return;
                }

                JgsValue subject = args[0];
                string? refused = subject.Type == JgsType.Cell ? "cell" : NonFloatClassName(subject);
                if (refused is null)
                {
                    return;
                }

                string message = "Expected input number 1, A, to be one of these types:\n\nsingle, double, table, timetable"
                    + (refused == "char" ? string.Empty : $"\n\nInstead its type was {refused}.");
                throw new JgsRuntimeException(line, col, $"MATLAB:{owner}:invalidType", message);
            });
        }
    }

    /// <summary>
    /// The class name of a value R2025b's floating-only verbs refuse — <c>logical</c>, <c>char</c> or
    /// an integer class — or null for anything else (a double, a single, and every value that is not
    /// a number at all, which each verb answers in its own words).
    /// </summary>
    private static string? NonFloatClassName(JgsValue value)
    {
        if (value.Type == JgsType.String || value.IsCharMatrix)
        {
            return value.IsStringArray ? null : "char";
        }

        if (value.Type is not (JgsType.Bool or JgsType.Number or JgsType.Complex or JgsType.Array or JgsType.Image))
        {
            return null;
        }

        if (IsLogicalValue(value))
        {
            return "logical";
        }

        return value.NumericClass.IsInteger() ? value.NumericClass.MatlabName() : null;
    }

    /// <summary>Re-registers <paramref name="name"/> with <paramref name="refuse"/> asked first, in MATLAB code only.</summary>
    private static void GuardSubjects(JgsEnvironment env, JgsRunningDialect dialect, string name,
        Action<IReadOnlyList<JgsValue>, int, int> refuse)
    {
        if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function
            || existing.AsCallable is not BuiltinFunction inner)
        {
            return;
        }

        var guarded = new BuiltinFunction(name, (args, line, col) =>
        {
            if (dialect.IsMatlab)
            {
                refuse(args, line, col);
            }

            return inner.Call(args, line, col);
        })
        {
            MultiOutput = inner.MultiOutput is null
                ? null
                : (args, wanted, line, col) =>
                {
                    if (dialect.IsMatlab)
                    {
                        refuse(args, line, col);
                    }

                    return inner.CallMultiple(args, wanted, line, col);
                },
            BindsAnsAsStatement = inner.BindsAnsAsStatement,
            AutoCallsBare = inner.AutoCallsBare,
            KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
            TakesOutputCount = inner.TakesOutputCount,
            ScriptFile = inner.ScriptFile,
        };
        guarded.KeepsStringArguments = inner.KeepsStringArguments;
        env.Builtins.Register(name, JgsValue.Function(guarded));
    }
}
