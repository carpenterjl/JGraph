namespace JGraph.Scripting.Jgs;

/// <summary>
/// The verbs that answer in the class they were handed (M123).
/// </summary>
/// <remarks>
/// <para>
/// M97 made a numeric class a property of the value and taught the arithmetic operators to combine
/// two of them, so <c>int8(100) + int8(100)</c> saturates and <c>single(1) * 2</c> stays single.
/// What it never did was tell the <em>builtins</em>, and there is no list of them anywhere: every
/// verb mints a fresh wrapper from the numbers it computed, and a fresh wrapper is a double. So
/// <c>class(sort(uint8([3 1 2])))</c> was double, and so was every reduction, every shape verb and
/// every rounding verb — a hundred and some names, each one losing the tag in its own file.
/// </para>
/// <para>
/// The head-to-head report named one of them: <c>class(sum(single([1 2 3])))</c>, under a claim that
/// the integer classes kept their class through the same reduction. They did not. Probing the family
/// rather than the form found that <b>every</b> builtin dropped <b>every</b> class, which is why the
/// answer here is a table and not a fix to <c>sum</c>.
/// </para>
/// <para>
/// MATLAB's own rule was measured rather than recalled — a hundred and thirty expressions evaluated
/// in R2024a against single, int16, uint8, logical and double — and it comes out as two lists.
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Carried.</b> The class survives whatever it is. These verbs select, rearrange, round or
/// combine elements that are already in the class, so the answer is made of the same kind of number
/// the argument was: <c>sort</c>, <c>reshape</c>, <c>max</c>, <c>diff</c>, <c>cumsum</c>,
/// <c>mod</c>, <c>abs</c> and their neighbours.
/// </item>
/// <item>
/// <b>Floating.</b> A single survives and an integer class becomes double. These verbs compute a
/// new quantity rather than choose an old one — a sum can leave the range its terms lived in, a
/// mean is rarely one of its samples — and MATLAB widens to double rather than saturate a result
/// nobody asked to be an integer.
/// </item>
/// </list>
/// <para>
/// Where MATLAB <em>refuses</em> an integer outright (<c>sqrt(int16(4))</c> is an error there and
/// answers 2 here), the name is on the floating list rather than given a new refusal. Introducing a
/// refusal closes a divergence by making a script that runs today stop running, which is the wrong
/// trade for a difference nobody has reported; it is recorded in ADR 0125 instead.
/// </para>
/// <para>
/// The cost of all of this on an untagged double is one enum comparison per call: a value with no
/// class short-circuits before anything is copied, and every benchmark in the suite is doubles.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The verbs whose answer is made of the same kind of number as their argument, so the class
    /// survives whatever it is.
    /// </summary>
    private static readonly string[] ClassCarryingBuiltins =
    [
        // Rounding, sign and the parts of a complex number: each answers a value the class already
        // held, so there is nothing for a wider type to carry.
        "abs", "sign", "fix", "ceil", "floor", "round", "real", "imag", "conj",

        // Shape: the same elements somewhere else.
        "reshape", "permute", "ipermute", "squeeze", "shiftdim", "circshift", "rot90",
        "fliplr", "flipud", "flip", "flipdim", "transpose", "ctranspose", "repmat",
        "horzcat", "vertcat", "cat", "kron", "triu", "tril", "diag",

        // Order and membership: a subset of the elements, or the same set rearranged.
        "sort", "sortrows", "unique", "union", "intersect", "setdiff", "setxor",
        "max", "min", "maxk", "mink", "topkrows", "median", "mode",

        // Running order statistics and differences: every answer is a difference or a choice
        // between elements, both of which MATLAB keeps inside the class and saturates.
        "cummax", "cummin", "cumsum", "cumprod", "cumtrapz", "diff",
        "movmax", "movmin", "movmedian",

        // Division that stays whole. The bit operations sit on their own list below, because they
        // are the one family MATLAB defines for integers and refuses for a single.
        "mod", "rem",

        // Cleaning: what comes back is what went in, minus or plus a sample.
        "fillmissing", "rmmissing", "deal",

        // Sums and products of a fixed, small number of elements, which MATLAB keeps inside the
        // class rather than widening the way it widens a reduction over a whole array.
        "cross", "poly", "nchoosek",

        // Two that answer about the values rather than out of them: a power of two is a count and an
        // unwrapped angle is the angle it was, moved. An overlapping area is measured in whatever
        // the rectangles were measured in.
        "nextpow2", "unwrap", "rectint",
    ];

    /// <summary>
    /// The verbs that keep <c>logical</c>, which is a narrower set than the ones that keep a numeric
    /// class.
    /// </summary>
    /// <remarks>
    /// Every name here chooses or moves elements; none of them does arithmetic. That is the whole of
    /// the difference, and it is MATLAB's: <c>sort</c> of a mask is a mask, while
    /// <c>diff</c>, <c>cumsum</c>, <c>abs</c> and <c>mod</c> of one are doubles, because their answer
    /// is a quantity computed from the flags rather than a rearrangement of them. A logical array is
    /// already stored here as boxed true/false elements — a packed buffer cannot hold one — so
    /// answering in kind costs a rebuild and no more memory than the argument took.
    /// </remarks>
    private static readonly string[] LogicalCarryingBuiltins =
    [
        "reshape", "permute", "ipermute", "squeeze", "shiftdim", "circshift", "rot90",
        "fliplr", "flipud", "flip", "flipdim", "transpose", "ctranspose", "repmat",
        "horzcat", "vertcat", "cat", "triu", "tril", "diag",
        "sort", "sortrows", "unique", "union", "intersect", "setdiff",
        "max", "min", "maxk", "mink", "topkrows", "median", "mode",
        "cummax", "cummin", "movmax", "movmin",
        "fillmissing", "rmmissing", "deal", "nchoosek",
        "bitand", "bitor", "bitxor",
    ];

    /// <summary>
    /// The verbs with more than one subject, so every argument is looked at for the answer's class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everywhere else the subject is the first argument alone, and the rest are sizes, dimensions,
    /// window widths, digit counts and option words. That default is not a simplification: reading
    /// them all is <em>wrong</em>, and wrong in a way that changes answers. <c>round(2.567, int32(1))</c>
    /// asks for one decimal place and gets 2.6 — the int32 is the count of digits, not the kind of
    /// number — and a rule that scans every argument returns an int32 3 instead. A test caught that
    /// one; the same mistake is available in <c>movmean(x, int32(3))</c> and a dozen others where
    /// nothing would have.
    /// </para>
    /// <para>
    /// The names here genuinely take a second array whose class combines with the first: a
    /// concatenation, a two-argument extreme, a set operation, a convolution. <c>filter</c> is on the
    /// list because its data is the <em>third</em> argument, which is the same problem read the other
    /// way round.
    /// </para>
    /// </remarks>
    private static readonly string[] MultiSubjectBuiltins =
    [
        "cat", "horzcat", "vertcat", "kron", "cross",
        "max", "min", "union", "intersect", "setdiff", "setxor",
        "mod", "rem", "idivide", "bitand", "bitor", "bitxor", "bitshift",
        "hypot", "atan2", "dot", "nthroot", "conv", "conv2", "filter",
        "trapz", "cumtrapz", "interp1", "linspace",
    ];

    /// <summary>
    /// The verbs defined only on integers, so an integer class is carried and a single is not.
    /// </summary>
    /// <remarks>
    /// MATLAB refuses these for a single outright. Putting a single tag on their answer would be
    /// worse than the double they answer instead: it would claim a class for a value MATLAB says
    /// cannot have one.
    /// </remarks>
    private static readonly string[] IntegerOnlyBuiltins =
    [
        "idivide", "bitand", "bitor", "bitxor", "bitshift", "bitcmp",
    ];

    /// <summary>
    /// The verbs that compute a new quantity rather than choose an existing one: a single survives
    /// and an integer widens to double.
    /// </summary>
    private static readonly string[] FloatCarryingBuiltins =
    [
        "sum", "prod", "mean", "trapz", "var", "std", "norm", "dot",
        "conv", "conv2", "fft", "ifft", "dct", "idct", "filter", "movmean", "movsum", "movstd", "movvar",
        "rescale", "normalize", "roots", "linspace", "interp1", "interpft",
        "polyder", "polyint", "polyval", "polyvalm", "polyfit", "polyarea",
        "deconv", "convn", "cplxpair", "hypot",
        "det", "inv", "eig",
        "sqrt", "exp", "log", "log2", "log10", "log1p", "expm1", "nthroot", "hypot",
        "sin", "cos", "tan", "asin", "acos", "atan", "atan2", "sec", "csc", "cot",
        "sinh", "cosh", "tanh", "asinh", "acosh", "atanh",
        "erf", "erfc", "gamma",
    ];

    /// <summary>
    /// Wraps both lists so a classed argument produces a classed answer.
    /// </summary>
    /// <param name="env">The environment whose bindings are re-declared.</param>
    private static void CarryNumericClass(JgsEnvironment env)
    {
        foreach (string name in ClassCarryingBuiltins)
        {
            Wrap(env, name, Carry.Whatever);
        }

        foreach (string name in FloatCarryingBuiltins)
        {
            Wrap(env, name, Carry.FloatingOnly);
        }

        foreach (string name in IntegerOnlyBuiltins)
        {
            Wrap(env, name, Carry.IntegerOnly);
        }

        static void Wrap(JgsEnvironment env, string name, Carry carry)
        {
            if (!env.TryGet(name, out JgsValue declared)
                || declared.Type != JgsType.Function
                || declared.AsCallable is not BuiltinFunction inner)
            {
                return;
            }

            // `cat` is the one name whose leading argument is a dimension rather than data, so a
            // classed first argument there would be a coincidence rather than the answer's class.
            int from = name == "cat" ? 1 : 0;
            bool product = name == "cumprod";
            bool accumulates = product || name == "cumsum";
            bool masks = Array.IndexOf(LogicalCarryingBuiltins, name) >= 0;
            int subjects = Array.IndexOf(MultiSubjectBuiltins, name) >= 0 ? int.MaxValue : 1;

            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            {
                JgsNumericClass carried = ClassOf(args, from, subjects, carry);
                if (accumulates && carried.IsInteger()
                    && TrySaturatingScan(args, carried, product, out JgsValue scanned))
                {
                    return scanned;
                }

                return Finish(inner.Call(args, line, col), carried, masks && AllMasks(args, from, subjects));
            })
            {
                // Every flag the inner builtin carried is carried on, not the two that look relevant:
                // a wrapper that forgets one changes how the name is *called* rather than what it
                // answers, and that is the regression nobody sees.
                KeepsStringArguments = inner.KeepsStringArguments,
                BindsAnsAsStatement = inner.BindsAnsAsStatement,
                AutoCallsBare = inner.AutoCallsBare,
                KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
                MultiOutput = inner.MultiOutput is null ? null : (args, wanted, line, col) =>
                {
                    JgsNumericClass carried = ClassOf(args, from, subjects, carry);
                    JgsValue[] outputs = inner.MultiOutput(args, wanted, line, col);

                    // Only the first output. `sort` and `unique` answer positions in the rest, and a
                    // position is a plain number however uint8 the values at it are.
                    if (outputs.Length > 0)
                    {
                        outputs[0] = Finish(outputs[0], carried, masks && AllMasks(args, from, subjects));
                    }

                    return outputs;
                },
            }));
        }
    }

    /// <summary>
    /// The class an answer takes from the arguments: the first one wearing a class that is not
    /// double, reduced to double for a floating verb handed an integer.
    /// </summary>
    /// <remarks>
    /// The first rather than <c>args[0]</c>, because MATLAB combines the operands rather than reading
    /// one of them: <c>max(2, int8(1))</c> is an int8 and so is <c>[1 int8(2)]</c>. Two different
    /// classes among the arguments are already an error by the time this is asked — the inner verb's
    /// own concatenation or arithmetic raises it — so there is nothing here to arbitrate.
    /// </remarks>
    private static JgsNumericClass ClassOf(
        IReadOnlyList<JgsValue> args, int from, int subjects, Carry carry)
    {
        for (int i = from; i < Reach(args, from, subjects); i++)
        {
            JgsNumericClass found = args[i].NumericClass;
            if (found == JgsNumericClass.Double)
            {
                continue;
            }

            return carry switch
            {
                Carry.FloatingOnly when found.IsInteger() => JgsNumericClass.Double,
                Carry.IntegerOnly when !found.IsInteger() => JgsNumericClass.Double,
                _ => found,
            };
        }

        return JgsNumericClass.Double;
    }

    /// <summary>How far along the argument list this verb's subjects run.</summary>
    private static int Reach(IReadOnlyList<JgsValue> args, int from, int subjects) =>
        subjects == int.MaxValue ? args.Count : System.Math.Min(args.Count, from + subjects);

    /// <summary>Which classes a wrapped verb carries.</summary>
    private enum Carry
    {
        /// <summary>Whatever class the argument wore.</summary>
        Whatever,

        /// <summary>A single, but not an integer: the answer is a new quantity, so it widens.</summary>
        FloatingOnly,

        /// <summary>An integer, but not a single: the verb is not defined on floating point.</summary>
        IntegerOnly,
    }

    /// <summary>
    /// Puts <paramref name="numericClass"/> on an answer, rounding and saturating its samples the way
    /// a write into that class would.
    /// </summary>
    /// <remarks>
    /// Tagging alone would be a lie for the verbs that can land between two of the class's values:
    /// <c>median(int16([1 2 3 4]))</c> is 2.5 before it is anything, and MATLAB answers 3.
    /// </remarks>
    private static JgsValue Finish(JgsValue answer, JgsNumericClass numericClass, bool mask)
    {
        if (mask && numericClass == JgsNumericClass.Double)
        {
            return AsMask(answer);
        }

        return JgsNumericClasses.Stamp(answer, numericClass);
    }

    /// <summary>
    /// Whether every value argument is a mask, so the answer is one too.
    /// </summary>
    /// <remarks>
    /// Every, not any: <c>[mask 2]</c> is a double row in MATLAB and so is <c>union(mask, 3)</c>.
    /// Arguments that are not values at all — a dimension, a count, an option word — are skipped, so
    /// <c>circshift(mask, 1)</c> is still a mask.
    /// </remarks>
    private static bool AllMasks(IReadOnlyList<JgsValue> args, int from, int subjects)
    {
        bool sawOne = false;
        int limit = Reach(args, from, subjects);
        for (int i = from; i < limit; i++)
        {
            JgsValue arg = args[i];
            if (arg.Type is JgsType.String or JgsType.Function)
            {
                continue;
            }

            if (IsLogicalValue(arg))
            {
                sawOne = true;
                continue;
            }

            // An empty stands for "nothing to take away" rather than for a double, and a plain
            // number in this position is a size or a count. Neither makes the answer a double; a
            // real array of numbers does.
            if (arg.Type == JgsType.Array && arg.ArrayLength > 0)
            {
                return false;
            }
        }

        return sawOne;
    }

    /// <summary>The same values read back as true and false.</summary>
    private static JgsValue AsMask(JgsValue answer)
    {
        switch (answer.Type)
        {
            case JgsType.Number:
                return JgsValue.Bool(answer.AsNumber != 0);

            case JgsType.Bool:
                return answer;

            case JgsType.Array:
            {
                int length = answer.ArrayLength;
                var flags = new JgsValue[length];
                for (int i = 0; i < length; i++)
                {
                    JgsValue element = answer.ElementAt(i);
                    flags[i] = JgsValue.Bool(
                        element.Type == JgsType.Bool ? element.AsBool : element.AsNumber != 0);
                }

                return JgsMatrix.FromElementsDims(flags, answer.Dims);
            }

            default:
                return answer;
        }
    }

    /// <summary>
    /// <c>cumsum</c> and <c>cumprod</c> of an integer array, saturated at every step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A running total saturates as it runs, not once at the end:
    /// <c>cumsum(int8([100 100 -100]))</c> is 100, 127, 27 in MATLAB, because the third term is taken
    /// off the ceiling the second was pinned to and not off the 200 that never existed. Letting the
    /// double-precision verb finish and stamping its row afterwards answers 100, 127, 100 — a
    /// plausible row that is wrong in its last place and says nothing about being wrong.
    /// </para>
    /// <para>
    /// The scan is done here rather than recovered from the finished row because a product cannot be
    /// unwound: a zero anywhere in it destroys the ratio that would have to be read back out.
    /// </para>
    /// <para>
    /// Only the default dimension is served — the first that is not a singleton, which is what these
    /// two accumulate along when nobody says otherwise. A call that names a dimension falls back to
    /// the plain stamp, which differs only where a partial result leaves the class's range.
    /// </para>
    /// </remarks>
    private static bool TrySaturatingScan(
        IReadOnlyList<JgsValue> args, JgsNumericClass numericClass, bool product, out JgsValue scanned)
    {
        scanned = JgsValue.Number(0);
        if (args.Count != 1 || args[0].Type != JgsType.Array)
        {
            return false;
        }

        int[] dims = args[0].Dims;
        int along = 0;
        while (along < dims.Length - 1 && dims[along] == 1)
        {
            along++;
        }

        int count = dims[along];
        if (count <= 1)
        {
            return false;
        }

        int stride = 1;
        for (int d = 0; d < along; d++)
        {
            stride *= dims[d];
        }

        double[] flat = ToDoubles("cumsum", args[0], 0, 0);
        int block = stride * count;
        for (int start = 0; start < flat.Length; start += block)
        {
            for (int offset = 0; offset < stride; offset++)
            {
                int at = start + offset;
                double running = JgsNumericClasses.Convert(flat[at], numericClass);
                flat[at] = running;
                for (int step = 1; step < count; step++)
                {
                    at += stride;
                    double next = product ? running * flat[at] : running + flat[at];
                    running = JgsNumericClasses.Convert(next, numericClass);
                    flat[at] = running;
                }
            }
        }

        scanned = JgsNumericClasses.Stamp(JgsMatrix.FromColumnMajorDims(flat, dims), numericClass);
        return true;
    }
}
