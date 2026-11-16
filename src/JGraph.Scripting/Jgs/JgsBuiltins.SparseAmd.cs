using System.Numerics;
using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>amd</c> in MATLAB code as R2025b answers it (open item 345, ADR 0381): SuiteSparse AMD's
/// ordering (<see cref="SuiteSparseAmd"/>), its control structure and its <c>Info</c> vector, and
/// R2025b's refusals. JGS code keeps the exact minimum-degree ordering it always had.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// <c>p = amd(A)</c>, <c>p = amd(A, opts)</c> and <c>[p, Info] = amd(...)</c> (probes p8b_a,
    /// p8b_b, p8b_c, p8b_d). The first argument is read before the options: a class other than
    /// double, single or logical is <c>needFloatOrLogical</c>, an N-D array <c>needMatrix</c>, a
    /// non-square matrix <c>MATLAB:square</c>. Only the pattern counts; A + Aᵀ is what AMD orders.
    /// </summary>
    private static JgsValue[] AmdOrdering(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("amd", args, 1, 2, line, col);
        if (wanted > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:maxlhs", "Too many output arguments.");
        }

        (int n, int[] ap, int[] ai) = AmdPattern(args[0], line, col);
        (double dense, bool aggressive) = args.Count == 2
            ? AmdControl(args[1], line, col)
            : (SuiteSparseAmd.DefaultDense, true);

        var info = new double[SuiteSparseAmd.InfoLength];
        int[] order = SuiteSparseAmd.Order(n, ap, ai, dense, aggressive, info);

        var numbers = new double[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            numbers[i] = order[i] + 1;
        }

        JgsValue permutation = numbers.Length == 0 ? JgsEmpty.Shaped(1, 0) : Numbers(numbers);
        return wanted >= 2 ? [permutation, JgsMatrix.FromColumnMajor(info, info.Length, 1)] : [permutation];
    }

    /// <summary>The column-compressed pattern of the stored non-zeros, rows sorted, as AMD reads it.</summary>
    private static (int N, int[] Ap, int[] Ai) AmdPattern(JgsValue a, int line, int col)
    {
        if (a.Type == JgsType.Sparse)
        {
            (int rows, int cols) = a.SparseExtent;
            RequireAmdSquare(rows, cols, line, col);
            if (a.IsComplexSparse)
            {
                JgsComplexSparse complex = a.AsComplexSparse;
                return PatternOf(cols, complex.ColumnStarts, complex.RowIndices, k => complex.Values[k] != Complex.Zero);
            }

            CscMatrix matrix = a.AsSparse;
            return PatternOf(cols, matrix.ColumnStarts, matrix.RowIndices, k => matrix.Values[k] != 0);
        }

        bool floatOrLogical = a.Type is JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.Array
            && !a.IsCharMatrix && !a.IsStringArray && !a.IsTime
            && (IsLogicalValue(a) || a.NumericClass is JgsNumericClass.Double or JgsNumericClass.Single);
        if (!floatOrLogical)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:amd:needFloatOrLogical", "First argument must be double, single, or logical.");
        }

        int[] dims = SizeDims(a);
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:amd:needMatrix", "First argument must be a square matrix.");
        }

        RequireAmdSquare(dims[0], dims[1], line, col);
        Complex[] values = a.Type is JgsType.Number or JgsType.Bool ? [new Complex(a.AsNumber, 0)] : ColumnComplex("amd", a, line, col);
        int n = dims[0];
        var ap = new int[n + 1];
        var ai = new List<int>();
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                if (values[c * n + r] != Complex.Zero)
                {
                    ai.Add(r);
                }
            }

            ap[c + 1] = ai.Count;
        }

        return (n, ap, [.. ai]);
    }

    private static (int N, int[] Ap, int[] Ai) PatternOf(int n, int[] starts, int[] rows, Func<int, bool> stored)
    {
        var ap = new int[n + 1];
        var ai = new List<int>(starts[n]);
        for (int c = 0; c < n; c++)
        {
            int from = ai.Count;
            for (int k = starts[c]; k < starts[c + 1]; k++)
            {
                if (stored(k))
                {
                    ai.Add(rows[k]);
                }
            }

            ai.Sort(from, ai.Count - from, null);
            ap[c + 1] = ai.Count;
        }

        return (n, ap, [.. ai]);
    }

    private static void RequireAmdSquare(int rows, int cols, int line, int col)
    {
        if (rows != cols)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:square", "Matrix must be square.");
        }
    }

    /// <summary>
    /// The control <c>amd</c> takes second: a struct (its first element) whose <c>dense</c> must be a
    /// numeric scalar (its real part read) and whose <c>aggressive</c> a numeric or logical scalar,
    /// the field names exact; or a numeric vector read as AMD's Control array, <c>[dense aggressive]</c>
    /// (d_numeric*). Anything else is <c>MATLAB:amd:invalidInput</c>.
    /// </summary>
    private static (double Dense, bool Aggressive) AmdControl(JgsValue opts, int line, int col)
    {
        double dense = SuiteSparseAmd.DefaultDense;
        bool aggressive = true;
        if (opts.Type == JgsType.Struct)
        {
            Dictionary<string, JgsValue> fields = opts.AsStruct;
            if (fields.TryGetValue("dense", out JgsValue? given) && given is not null)
            {
                if (!AmdNumeric(given, allowLogical: false))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:amd:denseNonNumeric", "Parameter dense must be numeric data.");
                }

                dense = AmdScalar(given, "dense", line, col);
            }

            if (fields.TryGetValue("aggressive", out JgsValue? flag) && flag is not null)
            {
                if (!AmdNumeric(flag, allowLogical: true))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:amd:aggressiveNonNumeric", "Parameter aggressive must be numeric data.");
                }

                aggressive = AmdScalar(flag, "aggressive", line, col) != 0;
            }

            return (dense, aggressive);
        }

        if (!AmdNumeric(opts, allowLogical: false))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:amd:invalidInput", "Second argument must be a control structure.");
        }

        Complex[] control = opts.Type is JgsType.Number ? [new Complex(opts.AsNumber, 0)] : ColumnComplex("amd", opts, line, col);
        if (control.Length > 0)
        {
            dense = control[0].Real;
        }

        if (control.Length > 1)
        {
            aggressive = control[1].Real != 0;
        }

        return (dense, aggressive);
    }

    private static bool AmdNumeric(JgsValue value, bool allowLogical) =>
        value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.Array
        && !value.IsCharMatrix && !value.IsStringArray && !value.IsTime
        && (IsLogicalValue(value) ? allowLogical : value.Type != JgsType.Bool);

    private static double AmdScalar(JgsValue value, string name, int line, int col)
    {
        Complex[] values = value.Type is JgsType.Number or JgsType.Bool ? [new Complex(value.AsNumber, 0)] : ColumnComplex("amd", value, line, col);
        if (values.Length != 1)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:amd:{name}Scalar", $"Parameter {name} must be a scalar.");
        }

        return values[0].Real;
    }
}
