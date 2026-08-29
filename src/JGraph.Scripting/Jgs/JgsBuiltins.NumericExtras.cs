namespace JGraph.Scripting.Jgs;

/// <summary>
/// The core numeric names M52 left on its deferred list (M66 wave B): <c>kron</c>, <c>perms</c>,
/// <c>factor</c>, <c>idivide</c> and <c>interp2</c>.
/// </summary>
/// <remarks>
/// Nothing here is deep — these are five separate small holes rather than one missing idea — but each
/// of them is a name a ported script writes without thinking, and a name that is missing stops a
/// script at the line that uses it rather than degrading what it computes.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static void RegisterNumericExtraBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        Define("kron", KroneckerProduct);
        Define("perms", Permutations);
        Define("factor", PrimeFactors);
        Define("idivide", IntegerDivide);
        // interp2 is registered here, where M66 put it, and implemented in
        // JgsBuiltins.Interpolation.Grid.cs, where M101 put the grid reader it shares with interp3
        // and interpn. One name, one registration: a second Define would shadow this one silently.
        Define("interp2", (args, line, col) => SampleGridded("interp2", args, 2, host, line, col));
    }

    // --- kron ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>kron(A, B)</c>: every element of A replaced by that element times the whole of B, giving an
    /// <c>(m·p)</c>-by-<c>(n·q)</c> matrix. The block structure is the definition, and writing it as
    /// blocks rather than as an index formula is what keeps it readable.
    /// </summary>
    private static JgsValue KroneckerProduct(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("kron", args, 2, line, col);
        double[] a = FlattenColumnMajor("kron", args[0], line, col);
        double[] b = FlattenColumnMajor("kron", args[1], line, col);
        int[] da = SizeDims(args[0]);
        int[] db = SizeDims(args[1]);
        if (da.Length > 2 || db.Length > 2)
        {
            throw new JgsRuntimeException(line, col, "kron works on matrices, not on N-D arrays.");
        }

        int m = da[0];
        int n = da.Length > 1 ? da[1] : 1;
        int p = db[0];
        int q = db.Length > 1 ? db[1] : 1;

        int rows = m * p;
        int cols = n * q;
        var result = new double[rows * cols];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < m; i++)
            {
                double scale = a[i + (j * m)];
                for (int l = 0; l < q; l++)
                {
                    for (int k = 0; k < p; k++)
                    {
                        int row = (i * p) + k;
                        int column = (j * q) + l;
                        result[row + (column * rows)] = scale * b[k + (l * p)];
                    }
                }
            }
        }

        return JgsMatrix.FromColumnMajorDims(result, [rows, cols]);
    }

    // --- perms --------------------------------------------------------------------------------

    /// <summary>
    /// <c>perms(v)</c>: every arrangement of the values, one per row, in reverse lexicographic order
    /// of the positions they came from — which is the order MATLAB produces and the reason
    /// <c>perms([1 2 3])</c> starts at <c>3 2 1</c> rather than at <c>1 2 3</c>.
    /// </summary>
    private static JgsValue Permutations(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("perms", args, 1, line, col);
        double[] values = FlattenColumnMajor("perms", args[0], line, col);
        int n = values.Length;

        // 11! rows would be forty million; the answer is a memory exhaustion rather than a result, so
        // it is refused with the size it would have been.
        if (n > 10)
        {
            throw new JgsRuntimeException(line, col,
                $"perms: {n} values have more arrangements than fit in memory; perms goes up to 10.");
        }

        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        var rowsOut = new List<int[]>();
        Permute(order, 0, rowsOut);

        // Lexicographic by position, then reversed: MATLAB's own order.
        rowsOut.Reverse();

        var flat = new double[rowsOut.Count * n];
        for (int r = 0; r < rowsOut.Count; r++)
        {
            for (int c = 0; c < n; c++)
            {
                flat[r + (c * rowsOut.Count)] = values[rowsOut[r][c]];
            }
        }

        return JgsMatrix.FromColumnMajorDims(flat, [rowsOut.Count, n]);
    }

    /// <summary>Every arrangement of <paramref name="order"/> from <paramref name="at"/> onwards, in order.</summary>
    private static void Permute(int[] order, int at, List<int[]> into)
    {
        if (at == order.Length)
        {
            into.Add((int[])order.Clone());
            return;
        }

        // The rotate-back at the end of each step keeps the tail sorted, which is what makes the
        // whole enumeration come out in lexicographic order rather than in swap order.
        for (int i = at; i < order.Length; i++)
        {
            int held = order[i];
            for (int j = i; j > at; j--)
            {
                order[j] = order[j - 1];
            }

            order[at] = held;
            Permute(order, at + 1, into);

            for (int j = at; j < i; j++)
            {
                order[j] = order[j + 1];
            }

            order[i] = held;
        }
    }

    // --- factor -------------------------------------------------------------------------------

    /// <summary>
    /// <c>factor(n)</c>: the prime factors of a positive whole number, smallest first and repeated as
    /// often as they divide. <c>factor(1)</c> is empty, because one is a product of no primes.
    /// </summary>
    private static JgsValue PrimeFactors(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("factor", args, 1, line, col);
        double raw = Num("factor", args, 0, line, col);
        if (raw < 1 || raw != Math.Floor(raw) || !double.IsFinite(raw))
        {
            throw new JgsRuntimeException(line, col,
                $"factor takes a positive whole number, but got {raw}.");
        }

        // Above 2^53 a double no longer counts by ones, so a factorization would be of whatever the
        // nearest representable number happened to be rather than of what was asked.
        if (raw > 9007199254740992d)
        {
            throw new JgsRuntimeException(line, col,
                "factor: numbers this large are not exactly representable, so their factors would not be theirs.");
        }

        long n = (long)raw;
        var factors = new List<double>();
        for (long p = 2; p * p <= n; p += p == 2 ? 1 : 2)
        {
            while (n % p == 0)
            {
                factors.Add(p);
                n /= p;
            }
        }

        if (n > 1)
        {
            factors.Add(n);
        }

        return Numbers([.. factors]);
    }

    // --- idivide ------------------------------------------------------------------------------

    /// <summary>
    /// <c>idivide(a, b, opt)</c>: integer division that says out loud what it does with the remainder,
    /// rather than leaving it to <c>./</c>'s implicit rounding. The default is <c>'fix'</c> — towards
    /// zero — which is the one case where <c>idivide</c> and <c>a ./ b</c> disagree for an integer
    /// class, and the reason the name exists.
    /// </summary>
    private static JgsValue IntegerDivide(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("idivide", args, 2, 3, line, col);
        string how = args.Count == 3
            ? OneWord("idivide", args, 2, line, col, "fix", "floor", "ceil", "round")
            : "fix";

        // The class of the answer is the class of whichever operand carries one, exactly as MATLAB's
        // integer arithmetic decides it.
        JgsNumericClass? target = JgsNumericClasses.Parse(ClassOf(args[0], JgsDialect.Matlab)) is
            { } left and not JgsNumericClass.Double and not JgsNumericClass.Single
            ? left
            : JgsNumericClasses.Parse(ClassOf(args[1], JgsDialect.Matlab)) is
                { } right and not JgsNumericClass.Double and not JgsNumericClass.Single
                ? right
                : null;

        JgsValue quotient = Zip("idivide", args[0], args[1], (a, b) =>
        {
            if (b == 0)
            {
                return 0; // integer division by zero saturates at zero, as MATLAB's does
            }

            double exact = a / b;
            return how switch
            {
                "floor" => Math.Floor(exact),
                "ceil" => Math.Ceiling(exact),
                "round" => Math.Round(exact, MidpointRounding.AwayFromZero),
                _ => Math.Truncate(exact),
            };
        }, line, col);

        return target is { } numericClass
            ? ToNumericClass("idivide", numericClass, quotient, line, col)
            : quotient;
    }
}
