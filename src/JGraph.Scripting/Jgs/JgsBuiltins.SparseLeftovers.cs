using System.Numerics;
using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A sparse argument, the 1-by-1 sparse matrix a subscript answers included, through the functions
/// ADR 0369 left reading it as a dense array (open item 337, ADR 0374; probes p7b_a, p7b_b, p7b_c).
/// </summary>
/// <remarks>
/// <para>
/// Several of those functions did not refuse a sparse argument: they read it as its stored values
/// or as one element, and answered wrongly (<c>fliplr(S)</c> a row, <c>isscalar(S)</c> true,
/// <c>ismember(s, [1 2 3])</c> false, <c>not(S)</c> one value). The shape questions are answered
/// from the matrix's extent, so a large matrix is never read full for them; the transposes and the
/// signs are the operators' own; the logical verbs read full and answer sparse where the operators
/// do; and the functions R2025b refuses for a sparse argument refuse it in its words.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// Rewraps a builtin so that a call with a sparse argument goes to <paramref name="sparse"/>
    /// first; a null answer from it hands the call to the builtin unchanged.
    /// </summary>
    private static void WrapOverSparse(
        JgsEnvironment env, string name, Func<BuiltinFunction, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]?> sparse)
    {
        if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function
            || existing.AsCallable is not BuiltinFunction inner)
        {
            return;
        }

        env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            AnySparseArgument(args) && sparse(inner, args, 1, line, col) is { Length: > 0 } one ? one[0] : inner.Call(args, line, col))
        {
            MultiOutput = inner.MultiOutput is null ? null : (args, wanted, line, col) =>
                AnySparseArgument(args) && sparse(inner, args, wanted, line, col) is { } some ? some : inner.MultiOutput(args, wanted, line, col),
            KeepsStringArguments = inner.KeepsStringArguments,
            BindsAnsAsStatement = inner.BindsAnsAsStatement,
            AutoCallsBare = inner.AutoCallsBare,
            KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
            TakesOutputCount = inner.TakesOutputCount,
        }));
    }

    private static bool AnySparseArgument(IReadOnlyList<JgsValue> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].Type == JgsType.Sparse)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The arguments with every sparse matrix read full, in its class.</summary>
    private static JgsValue[] SparseArgumentsFull(IReadOnlyList<JgsValue> args)
    {
        var full = new JgsValue[args.Count];
        for (int i = 0; i < full.Length; i++)
        {
            full[i] = args[i].Type == JgsType.Sparse ? SparseDense(args[i]) : args[i];
        }

        return full;
    }

    /// <summary>A builtin's answers to a call, one or <paramref name="wanted"/> of them.</summary>
    private static JgsValue[] SparseCallAnswers(BuiltinFunction inner, JgsValue[] args, int wanted, int line, int col) =>
        wanted <= 1 || inner.MultiOutput is null
            ? [inner.Call(args, line, col)]
            : inner.MultiOutput(args, wanted, line, col);

    /// <summary>
    /// A sparse answer of a uniform <c>arrayfun</c> or <c>cellfun</c> (MATLAB dialect): the first
    /// one is R2025b's <c>SparseNotSupported</c>, a later 1-by-1 one is collected as its number
    /// (probe p7b_c: <c>cellfun(@(x) x, {1, s})</c> answers, <c>{s, 1}</c> refuses). A larger one is
    /// left to the scalar check.
    /// </summary>
    internal static JgsValue UniformSparseAnswer(string verb, JgsValue answer, int index, int line, int col)
    {
        if (!JgsRunningDialect.ThreadIsMatlab)
        {
            return answer;
        }

        if (index == 0)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:SparseNotSupported", "Sparse Arrays are not supported. See SPFUN.");
        }

        return answer.SparseExtent is (1, 1) ? SparseDenseScalar(answer) : answer;
    }

    /// <summary>
    /// <c>full</c> of a sparse matrix, a real 1-by-1 one as the scalar it holds (a number or a
    /// logical, single when the matrix is), as a dense subscript answers one: so what
    /// <c>full(S(i, j))</c> answers goes wherever a dense scalar goes (a uniform <c>cellfun</c>
    /// collects it; c_cellfun_full).
    /// </summary>
    internal static JgsValue SparseDenseScalar(JgsValue sparse)
    {
        JgsValue dense = SparseDense(sparse);
        if (sparse.IsComplexSparse || dense.Type != JgsType.Array || dense.ArrayLength != 1)
        {
            return dense;
        }

        double number = sparse.AsSparse.At(0, 0);
        return sparse.IsLogicalSparse ? JgsValue.Bool(number != 0)
            : IsSingleSparse(sparse) ? JgsNumericClasses.Stamp(JgsValue.Number(number), JgsNumericClass.Single)
            : JgsValue.Number(number);
    }

    /// <summary>
    /// The answer of <c>and</c>, <c>or</c> or <c>xor</c> over the operands read full, in the shape of
    /// the operand it walked: the dense function forms answer a flat row for a matrix (a dense
    /// leftover noted in ADR 0374), which a sparse answer must not inherit.
    /// </summary>
    internal static JgsValue LogicalVerbShaped(JgsValue answer, IReadOnlyList<JgsValue> full)
    {
        foreach (JgsValue operand in full)
        {
            if (operand.Type == JgsType.Array && operand.ArrayLength == answer.ArrayLength && answer.Type == JgsType.Array)
            {
                return JgsMatrix.Like(operand, answer);
            }
        }

        return answer;
    }

    /// <summary>
    /// The class <c>sparse(i, j, v)</c> reads a subscript list in: single, an integer class, or
    /// double for everything else (a logical or a char list included; c_mixed_logical2, c_mixed_char).
    /// </summary>
    private static JgsNumericClass SubscriptClassWord(JgsValue subscripts) =>
        subscripts.NumericClass is JgsNumericClass.Single || subscripts.NumericClass.IsInteger()
            ? subscripts.NumericClass
            : JgsNumericClass.Double;

    /// <summary>
    /// A constructor's dense answer stored sparse in the class of a sparse <c>'like'</c> prototype:
    /// logical, single or double, complex (flag and all) when the prototype is (probes p7b_a, p7b_c).
    /// </summary>
    private static JgsValue SparseLikePrototype(string name, JgsValue built, JgsValue prototype, int line, int col)
    {
        if (name is "rand" or "randn")
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:sparsePrototype", "Sparse input following \"like\" is not supported.");
        }

        int[] dims = SizeDims(built);
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sparse:invalidMatrix", "Sparse matrices must be 2-D.");
        }

        if (prototype.IsComplexSparse)
        {
            Complex[] values = ColumnComplex(name, built, line, col);
            JgsComplexSparse complex = JgsComplexSparse.FromColumnMajor(values, dims[0], dims[1]);
            return IsSingleSparse(prototype) ? SingleSparseOf(complex, keepFlag: true) : JgsValue.ComplexSparse(complex);
        }

        CscMatrix matrix = built.Type is JgsType.Number or JgsType.Bool
            ? CscMatrix.FromColumnMajor([built.AsNumber], 1, 1)
            : CscFromDense(name, built, line, col);
        return prototype.IsLogicalSparse ? LogicalSparseOf(matrix)
            : IsSingleSparse(prototype) ? SingleSparseOf(matrix)
            : JgsValue.Sparse(matrix);
    }

    /// <summary>
    /// The functions that read a sparse argument as a dense array, taught it (open item 337, ADR 0374).
    /// Registered last of the sparse wrappers, so it sees each name as the session does.
    /// </summary>
    private static void WrapSparseLeftovers(JgsEnvironment env)
    {
        void Wrap(string name, Func<BuiltinFunction, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]?> sparse) =>
            WrapOverSparse(env, name, sparse);

        // --- the shape questions, from the extent (a large matrix is never read full for them) ---
        static (int Rows, int Cols)? OneSparse(IReadOnlyList<JgsValue> args) =>
            args.Count == 1 && args[0].Type == JgsType.Sparse ? args[0].SparseExtent : null;

        Wrap("length", (_, args, _, _, _) => OneSparse(args) is (int r, int c)
            ? [JgsValue.Number(r == 0 || c == 0 ? 0 : Math.Max(r, c))] : null);
        Wrap("isrow", (_, args, _, _, _) => OneSparse(args) is (int r, _) ? [JgsValue.Bool(r == 1)] : null);
        Wrap("iscolumn", (_, args, _, _, _) => OneSparse(args) is (_, int c) ? [JgsValue.Bool(c == 1)] : null);
        Wrap("isvector", (_, args, _, _, _) => OneSparse(args) is (int r, int c) ? [JgsValue.Bool(r == 1 || c == 1)] : null);
        Wrap("isscalar", (_, args, _, _, _) => OneSparse(args) is (int r, int c) ? [JgsValue.Bool(r == 1 && c == 1)] : null);

        // --- the transposes and the signs, as the operators answer them ---
        static JgsValue Transposed(JgsValue value, bool conjugate) => value.IsComplexSparse
            ? ComplexSparseTransposed(value, conjugate)
            : SparseInClassOf(JgsValue.Sparse(value.AsSparse.Transpose()), value);

        // (uminus, uplus and xor are the interpreter's operator forms, registered after this; their
        // sparse readings are in RegisterOperatorFunctions.)
        Wrap("transpose", (_, args, _, _, _) => OneSparse(args) is not null ? [Transposed(args[0], false)] : null);
        Wrap("ctranspose", (_, args, _, _, _) => OneSparse(args) is not null ? [Transposed(args[0], true)] : null);

        // --- the logical verbs: sparse where the operator's answer is (~S, S & x when either side
        // is, S | T when both are; probes p7b_b, p7b_c) ---
        Wrap("not", (_, args, _, line, col) => OneSparse(args) is not null ? [SparseNot(args[0], line, col)] : null);
        foreach ((string name, bool either) in new[] { ("and", true), ("or", false) })
        {
            bool eitherSide = either;
            string fn = name;
            Wrap(fn, (inner, args, _, line, col) =>
            {
                if (args.Count != 2)
                {
                    return null;
                }

                bool sparse = eitherSide
                    ? args[0].Type == JgsType.Sparse || args[1].Type == JgsType.Sparse
                    : args[0].Type == JgsType.Sparse && args[1].Type == JgsType.Sparse;
                JgsValue[] full = SparseArgumentsFull(args);
                JgsValue answer = LogicalVerbShaped(inner.Call(full, line, col), full);
                return [sparse ? DenseAsSparse(answer, fn, line, col) : answer];
            });
        }

        // dot is sparse when both vectors are (f_dot, c_dot_c), dense beside a dense one (b_dot_dense).
        Wrap("dot", (inner, args, wanted, line, col) =>
        {
            JgsValue[] answers = SparseCallAnswers(inner, SparseArgumentsFull(args), wanted, line, col);
            if (args.Count >= 2 && args[0].Type == JgsType.Sparse && args[1].Type == JgsType.Sparse && answers.Length > 0)
            {
                answers[0] = DenseAsSparse(answers[0], "dot", line, col);
            }

            return answers;
        });

        // --- the class conversions by name: cast(S, 'single') is single sparse, an integer class
        // has no sparse storage, cast(x, 'like', S) is sparse in S's class, and cast(S, 'like', p)
        // of a dense p is dense (b_cast_*, c_cast_*) ---
        Wrap("cast", (inner, args, _, line, col) =>
        {
            if (args.Count == 2 && args[0].Type == JgsType.Sparse && IsTextScalar(args[1]))
            {
                string target = TextOf(args[1]);
                return target switch
                {
                    "double" => [SparseToDouble(args[0])],
                    "single" => [SparseToSingle(args[0])],
                    "logical" => [SparseToLogical(args[0], line, col)],
                    "int8" or "uint8" or "int16" or "uint16" or "int32" or "uint32" or "int64" or "uint64" =>
                        throw new JgsRuntimeException(line, col, "MATLAB:unimplementedSparseType", "Attempt to convert to unimplemented sparse type"),
                    _ => null,
                };
            }

            if (args.Count == 3 && IsTextScalar(args[1]) && TextOf(args[1]).Equals("like", StringComparison.OrdinalIgnoreCase))
            {
                if (args[2].Type == JgsType.Sparse)
                {
                    JgsValue dense = inner.Call([args[0].Type == JgsType.Sparse ? SparseDense(args[0]) : args[0], args[1], SparseDense(args[2])], line, col);
                    return [SparseLikePrototype("cast", dense, args[2], line, col)];
                }

                if (args[0].Type == JgsType.Sparse && args[2].NumericClass.IsInteger())
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:unimplementedSparseType", "Attempt to convert to unimplemented sparse type");
                }

                return [inner.Call(SparseArgumentsFull(args), line, col)];
            }

            return null;
        });

        // num2cell of a sparse matrix is a cell of sparse matrices in its class (f_num2cell, c_num2cell_*).
        Wrap("num2cell", (inner, args, _, line, col) =>
        {
            if (args.Count == 0 || args[0].Type != JgsType.Sparse)
            {
                return null;
            }

            JgsValue cells = inner.Call(SparseArgumentsFull(args), line, col);
            if (cells.Type != JgsType.Cell)
            {
                return [cells];
            }

            JgsValue[] pieces = cells.AsCell;
            var stored = new JgsValue[pieces.Length];
            for (int i = 0; i < pieces.Length; i++)
            {
                stored[i] = pieces[i].Type is JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.Array && !pieces[i].IsStringArray
                    ? SparseInClassOf(DenseAsSparse(pieces[i], "num2cell", line, col), args[0])
                    : pieces[i];
            }

            return [JgsMatrix.Like(cells, JgsValue.Cell(stored))];
        });

        // --- what R2025b refuses for a sparse argument, in its words ---
        Wrap("string", (_, args, _, line, col) => OneSparse(args) is not null
            ? throw new JgsRuntimeException(line, col, "MATLAB:invalidConversion",
                $"Conversion to string from sparse {(args[0].IsLogicalSparse ? "logical" : IsSingleSparse(args[0]) ? "single" : "double")} is not possible.")
            : null);
        Wrap("char", (_, args, _, line, col) => OneSparse(args) is null ? null
            : args[0].IsComplexSparse
                ? throw new JgsRuntimeException(line, col, "MATLAB:noConversionComplexToChar", "Complex values cannot be converted to chars")
            : args[0].IsLogicalSparse
                ? throw new JgsRuntimeException(line, col, "MATLAB:invalidConversion", "Conversion to char from logical is not possible.")
                : throw new JgsRuntimeException(line, col, "MATLAB:unimplementedSparseType", "Attempt to convert to unimplemented sparse type"));

        foreach ((string name, string id) in new[]
        {
            ("bitand", "MATLAB:Bitand:sparseArgs"), ("bitor", "MATLAB:Bitor:sparseArgs"),
            ("bitxor", "MATLAB:bitxor:sparseArgs"), ("bitshift", "MATLAB:bitshift:sparseArgs"),
        })
        {
            string identifier = id;
            Wrap(name, (_, _, _, line, col) => throw new JgsRuntimeException(line, col, identifier, "Inputs must be full."));
        }

        foreach (string name in new[] { "gamma", "gammaln" })
        {
            string identifier = $"MATLAB:{name}:notFullReal";
            Wrap(name, (_, _, _, line, col) => throw new JgsRuntimeException(line, col, identifier, "Input must be real and full."));
        }

        // histc reads its first argument, then its edges (c_histc_both).
        Wrap("histc", (_, args, _, line, col) =>
            args.Count >= 1 && args[0].Type == JgsType.Sparse
                ? throw new JgsRuntimeException(line, col, "MATLAB:histc:InvalidFirstInput", "First Input must be a real non-sparse numeric array.")
            : args.Count >= 2 && args[1].Type == JgsType.Sparse
                ? throw new JgsRuntimeException(line, col, "MATLAB:histc:InvalidSecondInput", "Second Input must be a real non-sparse numeric array.")
                : null);

        // interp1 refuses sparse sample values first, then sparse coordinates or query points
        // (b_interp1_*, c_interp1_*): interp1(v, xq) or interp1(x, v, xq, ...).
        Wrap("interp1", (_, args, _, line, col) =>
        {
            int numeric = 0;
            while (numeric < args.Count && !IsTextScalar(args[numeric]) && numeric < 3)
            {
                numeric++;
            }

            int valuesAt = numeric >= 3 ? 1 : 0;
            if (valuesAt < args.Count && args[valuesAt].Type == JgsType.Sparse)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:griddedInterpolant:SparseInterpValuesErrId", "Sparse sample values are not supported.");
            }

            for (int i = 0; i < numeric; i++)
            {
                if (args[i].Type == JgsType.Sparse)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:griddedInterpolant:SparseDataPtErrId", "Sparse input coordinates are not supported.");
                }
            }

            return null;
        });
    }
}
