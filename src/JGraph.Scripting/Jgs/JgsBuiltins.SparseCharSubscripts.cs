using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>sparse</c>'s char arguments in MATLAB code (open item 346, ADR 0381; probe p8b_f): R2025b
/// reads a char subscript list, and a char size, as its character codes
/// (<c>sparse('ab', 'ab', [1 2])</c> is 98-by-98), and a char or string third of three arguments as
/// the class of an all-zero matrix (<c>sparse(2, 3, 'single')</c>).
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The arguments with every char subscript list and size read as codes, or, for
    /// <c>sparse(m, n, typename)</c>, the answer itself. Null when nothing is text.
    /// </summary>
    private static (IReadOnlyList<JgsValue> Args, JgsValue? Answer)? SparseTextArguments(IReadOnlyList<JgsValue> args, int line, int col)
    {
        static bool IsChar(JgsValue value) => value.Type == JgsType.String || value.IsCharMatrix;

        int[] codePositions = args.Count switch
        {
            2 or 3 => [0, 1],
            5 or 6 => [0, 1, 3, 4],
            _ => [],
        };

        bool typename = args.Count == 3 && (args[2].Type == JgsType.String || (args[2].IsStringArray && args[2].ArrayLength == 1));
        if (!typename && !Array.Exists(codePositions, p => IsChar(args[p])))
        {
            return null;
        }

        var read = args.ToArray();
        foreach (int p in codePositions)
        {
            if (IsChar(read[p]))
            {
                read[p] = SparseCharCodes(read[p]);
            }
        }

        // The class name is read before the sizes (c_v, t_vec).
        string word = typename ? (read[2].Type == JgsType.String ? read[2].AsString : read[2].ElementAt(0).AsString) : string.Empty;
        if (typename && word is not ("single" or "double" or "logical"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sparse:invalidTypename",
                "Array type argument must be \"single\", \"double\", or \"logical\".");
        }

        if ((read.Length == 2 || typename) && (!IsOneElement(read[0]) || !IsOneElement(read[1])))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sparse:twoNonScalarInputs",
                "Use sparse(m,n) to preallocate an m-by-n sparse matrix, or sparse(i,j,v) to specify the (i,j) locations of nonzero entries v.");
        }

        if (!typename)
        {
            return (read, null);
        }

        int rows = Count("sparse", read, 0, line, col);
        int cols = Count("sparse", read, 1, line, col);
        CscMatrix empty = CscMatrix.FromTriplets(Math.Max(rows, 0), Math.Max(cols, 0), []);
        JgsValue answer = word switch
        {
            "single" => SingleSparseOf(empty),
            "logical" => LogicalSparseOf(empty),
            _ => JgsValue.Sparse(empty),
        };
        return (read, answer);
    }

    /// <summary>A char row or matrix as the double array of its codes, in its shape.</summary>
    private static JgsValue SparseCharCodes(JgsValue value)
    {
        if (value.Type == JgsType.String)
        {
            string text = value.AsString;
            if (text.Length == 0)
            {
                return JgsMatrix.FromColumnMajorDims([], [0, 0]);
            }

            if (text.Length == 1)
            {
                return JgsValue.Number(text[0]);
            }

            var codes = new double[text.Length];
            for (int i = 0; i < codes.Length; i++)
            {
                codes[i] = text[i];
            }

            return JgsMatrix.FromColumnMajor(codes, 1, codes.Length);
        }

        int[] dims = SizeDims(value);
        var flat = new double[value.ArrayLength];
        for (int i = 0; i < flat.Length; i++)
        {
            flat[i] = value.ElementAt(i).AsNumber;
        }

        return JgsMatrix.FromColumnMajorDims(flat, dims);
    }
}
