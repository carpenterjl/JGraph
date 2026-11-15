namespace JGraph.Scripting.Jgs;

/// <summary>
/// The verbs that keep or refuse the zero-imaginary complex flag in R2025b, and the numeric refusals
/// ADR 0365 left (open items 330 and 333, ADR 0370).
/// </summary>
/// <remarks>
/// <para>
/// Measured in R2025b 25.2 (lane 6C's probes 1, 2 and 7). <c>sort</c>, <c>unique</c> and
/// <c>cell2mat</c> (the last in <c>RegisterBracketVerbs</c>, which holds the bracket) keep
/// <c>complex(x, 0)</c> complex, and a flagged array sorts as the complex array it is: by
/// magnitude, then by angle, so <c>sort(complex([-3 1 -1 2], 0))</c> is
/// <c>[1 -1 2 -3]</c>. <c>logical</c> of any complex value, flagged or not, is
/// <c>MATLAB:nologicalcomplex</c>. A string part of <c>complex</c> is refused as a non-numeric one.
/// <c>gammaln</c> and <c>psi</c> refuse a negative argument, double or single.
/// </para>
/// <para>
/// Each is a wrapper round the verb as it stands, asked only in MATLAB code; JGS code keeps its
/// answers. A refusal of the class (<c>MATLAB:math:mustBeFloat</c>) still comes first: a non-float
/// argument is handed to the verb untouched.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the wrappers; called after every other wrapper of these names.</summary>
    private static void WrapComplexFlagVerbs(JgsEnvironment env, JgsRunningDialect dialect)
    {
        void Wrap(string name, Func<BuiltinFunction, IReadOnlyList<JgsValue>, int, int, JgsValue?>? single,
            Func<BuiltinFunction, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]?>? multi, bool keepsStrings = false)
        {
            if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function
                || existing.AsCallable is not BuiltinFunction inner)
            {
                return;
            }

            var wrapped = new BuiltinFunction(name, (args, line, col) =>
                (dialect.IsMatlab && single is not null ? single(inner, args, line, col) : null) ?? inner.Call(args, line, col))
            {
                MultiOutput = inner.MultiOutput is null
                    ? null
                    : (args, wanted, line, col) =>
                        (dialect.IsMatlab && multi is not null ? multi(inner, args, wanted, line, col) : null)
                        ?? inner.CallMultiple(args, wanted, line, col),
                BindsAnsAsStatement = inner.BindsAnsAsStatement,
                AutoCallsBare = inner.AutoCallsBare,
                KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
                TakesOutputCount = inner.TakesOutputCount,
                ScriptFile = inner.ScriptFile,
            };
            wrapped.KeepsStringArguments = keepsStrings || inner.KeepsStringArguments;
            env.Builtins.Register(name, JgsValue.Function(wrapped));
        }

        // sort: a flagged array sorts by magnitude and then angle, which is the real 'abs' order,
        // and the sorted values keep the flag; the index output is the verb's.
        Wrap("sort",
            (inner, args, line, col) => FlaggedFirst(args) ? Reflagged(inner.Call(SortArgumentsOfFlagged(args), line, col)) : null,
            (inner, args, wanted, line, col) =>
            {
                if (!FlaggedFirst(args))
                {
                    return null;
                }

                JgsValue[] answers = inner.CallMultiple(SortArgumentsOfFlagged(args), wanted, line, col);
                answers[0] = Reflagged(answers[0]);
                return answers;
            });

        Wrap("unique",
            (inner, args, line, col) => FlaggedFirst(args) ? UniqueOfFlagged(inner, args, 1, line, col)[0] : null,
            (inner, args, wanted, line, col) => FlaggedFirst(args) ? UniqueOfFlagged(inner, args, wanted, line, col) : null);

        Wrap("logical", (_, args, line, col) =>
            args.Count == 1 && args[0].Type != JgsType.Sparse && HasComplexPart(args[0])
                ? throw new JgsRuntimeException(line, col, "MATLAB:nologicalcomplex", "Complex values cannot be converted to logicals.")
                : null, null);

        // complex refuses a string part as R2025b does; the verb reads char codes, so the string has
        // to be seen before it is demoted to char.
        Wrap("complex", (_, args, line, col) =>
        {
            if (args.Count >= 1 && args[0].IsStringArray)
            {
                throw args.Count == 1
                    ? new JgsRuntimeException(line, col, "MATLAB:complex:expectsNumeric", "First input must be numeric.")
                    : new JgsRuntimeException(line, col, "MATLAB:complex:realPartInputNonNumericOrNonReal",
                        "Input for real part must be a real numeric value.");
            }

            if (args.Count == 2 && args[1].IsStringArray)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:complex:imagPartInputNonNumericOrNonReal",
                    "Input for imaginary part must be a real numeric value.");
            }

            return null;
        }, null, keepsStrings: true);

        // gammaln: real and full, and not negative; psi: real, and not negative in its last argument.
        Wrap("gammaln", (_, args, line, col) =>
        {
            if (args.Count == 1 && NonFloatClassName(args[0]) is null)
            {
                if (HasComplexPart(args[0]) || args[0].Type == JgsType.Sparse)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:gammaln:notFullReal", "Input must be real and full.");
                }

                if (AnyNegative("gammaln", args[0], line, col))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:gammaln:negativeVal", "Input must be nonnegative.");
                }
            }

            return null;
        }, null);

        Wrap("psi", (_, args, line, col) =>
        {
            if (args.Count is 1 or 2 && args.All(static a => NonFloatClassName(a) is null))
            {
                if (args.Count == 2 && args[0].Type == JgsType.Number && (args[0].AsNumber < 0
                    || args[0].AsNumber != Math.Floor(args[0].AsNumber) || args[0].AsNumber >= int.MaxValue))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:psi:validOrder",
                        "Order must be nonnegative integer value smaller than 2^31-1.");
                }

                JgsValue x = args[^1];
                if (HasComplexPart(x))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:psi:notReal", "Input must be real.");
                }

                if (AnyNegative("psi", x, line, col))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:psi:negativeX", "X must be nonnegative.");
                }
            }

            return null;
        }, null);
    }

    private static bool FlaggedFirst(IReadOnlyList<JgsValue> args) => args.Count > 0 && IsZeroImagFlagged(args[0]);

    /// <summary>Whether a real value holds an element below zero (-Inf included; -0 and NaN are not).</summary>
    private static bool AnyNegative(string name, JgsValue value, int line, int col)
    {
        if (value.Type is JgsType.Number or JgsType.Bool)
        {
            return value.AsNumber < 0;
        }

        if (value.Type != JgsType.Array || value.ArrayLength == 0 || value.IsPackedComplex)
        {
            return false;
        }

        return Array.Exists(FlattenColumnMajor(name, value, line, col), static v => v < 0);
    }

    /// <summary>
    /// A real double or single answer as the flagged complex array it was computed for: every
    /// imaginary part zero, the shape and class kept. Anything else is handed back as it came.
    /// </summary>
    internal static JgsValue Reflagged(JgsValue answer)
    {
        if (!IsRealDoubleOrSingle(answer))
        {
            return answer;
        }

        double[] values = answer.Type == JgsType.Number ? [answer.AsNumber] : FlattenColumnMajor("complex", answer, 0, 0);
        return ZeroImagFlaggedOf(values, answer.Type == JgsType.Number ? [1, 1] : SizeDims(answer), answer.NumericClass);
    }

    /// <summary>
    /// <c>x .^ 1</c> of a flagged array: R2025b hands the array back complex (and a scalar back real),
    /// where any other exponent drops the flag.
    /// </summary>
    internal static JgsValue FlaggedToFirstPower(JgsValue flagged) => Reflagged(RealPlaneOfZeroImag(flagged));

    /// <summary>
    /// <c>sort</c>'s arguments with a flagged first one read as its real plane in the complex order,
    /// which for numbers with no imaginary part is the <c>'abs'</c> comparison; a script that named a
    /// comparison keeps its own.
    /// </summary>
    private static JgsValue[] SortArgumentsOfFlagged(IReadOnlyList<JgsValue> args)
    {
        bool named = false;
        var rewritten = new List<JgsValue>(args.Count + 2) { RealPlaneOfZeroImag(args[0]) };
        for (int i = 1; i < args.Count; i++)
        {
            rewritten.Add(args[i]);
            named |= IsTextScalar(args[i]) && string.Equals(TextOf(args[i]), "ComparisonMethod", StringComparison.OrdinalIgnoreCase);
        }

        if (!named && args[0].ArrayLength > 0)
        {
            rewritten.Add(JgsValue.Str("ComparisonMethod"));
            rewritten.Add(JgsValue.Str("abs"));
        }

        return [.. rewritten];
    }

    /// <summary>
    /// <c>unique</c> of a flagged array. Its sorted answer is in the complex order (magnitude, then
    /// angle), which the verb's real order is not, so the values are put in that order first and
    /// the verb asked for them as they first appear; the index outputs are then read back through
    /// the order. Every other form (<c>'stable'</c>, <c>'rows'</c>) does not depend on the order and
    /// is the verb's over the real plane, with the values flagged again.
    /// </summary>
    private static JgsValue[] UniqueOfFlagged(BuiltinFunction unique, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        JgsValue real = RealPlaneOfZeroImag(args[0]);
        bool sorted = true;
        bool last = false;
        for (int i = 1; i < args.Count; i++)
        {
            string option = IsTextScalar(args[i]) ? TextOf(args[i]).ToLowerInvariant() : string.Empty;
            sorted &= option is "sorted" or "first" or "last";
            last |= option == "last";
        }

        double[] values = real.Type == JgsType.Number ? [real.AsNumber] : FlattenColumnMajor("unique", real, line, col);
        if (!sorted || values.Length < 2)
        {
            JgsValue[] plain = unique.CallMultiple([real, .. args.Skip(1)], Math.Max(wanted, 1), line, col);
            plain[0] = Reflagged(plain[0]);
            return plain;
        }

        // The complex order of numbers with no imaginary part: magnitude, then +x before -x, NaN last.
        int[] order = Enumerable.Range(0, values.Length).ToArray();
        static double Angle(double v) => double.IsNegative(v) && v != 0 ? Math.PI : 0;
        Array.Sort(order, (p, q) =>
        {
            double a = values[p], b = values[q];
            int byNaN = double.IsNaN(a).CompareTo(double.IsNaN(b));
            if (byNaN != 0 || double.IsNaN(a))
            {
                return byNaN != 0 ? byNaN : p.CompareTo(q);
            }

            int byMagnitude = Math.Abs(a).CompareTo(Math.Abs(b));
            int byAngle = Angle(a).CompareTo(Angle(b));
            return byMagnitude != 0 ? byMagnitude : byAngle != 0 ? byAngle : p.CompareTo(q);
        });

        double[] ordered = Array.ConvertAll(order, i => values[i] + 0.0); // -0 is 0 here, as R2025b's unique has it
        JgsValue orderedValue = JgsMatrix.FromColumnMajorDims(ordered, SizeDims(real));
        orderedValue.SetNumericClass(real.NumericClass);
        JgsValue[] answers = unique.CallMultiple([orderedValue, JgsValue.Str("stable")], 3, line, col);

        // ic in the ordered array, read back to each element's own position; ia from it, first or last.
        double[] orderedGroups = FlattenColumnMajor("unique", answers[2], line, col);
        var groups = new double[values.Length];
        for (int k = 0; k < order.Length; k++)
        {
            groups[order[k]] = orderedGroups[k];
        }

        int count = answers[1].ArrayLength;
        var picks = new double[count];
        for (int i = 0; i < groups.Length; i++)
        {
            int g = (int)groups[i] - 1;
            if (picks[g] == 0 || last)
            {
                picks[g] = i + 1;
            }
        }

        JgsValue ia = JgsMatrix.FromColumnMajor(picks, count, 1);
        JgsValue ic = JgsMatrix.FromColumnMajor(groups, groups.Length, 1);
        JgsValue[] result = [Reflagged(answers[0]), ia, ic];
        return result[..Math.Max(wanted, 1)];
    }
}
