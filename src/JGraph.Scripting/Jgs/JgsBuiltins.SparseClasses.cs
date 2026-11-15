using System.Numerics;
using JGraph.Numerics.Sparse;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Sparse matrices of each class R2025b keeps — double, single and logical, real or complex — and
/// the operators ADR 0364 left refused (open item 331, ADR 0369).
/// </summary>
/// <remarks>
/// <para>
/// The class is a tag on the wrapper, not new storage: a logical sparse matrix is a
/// <see cref="CscMatrix"/> of ones wrapped by <see cref="JgsValue.LogicalSparse"/>, and a single one
/// is a matrix whose stored values are rounded to single and whose wrapper carries
/// <see cref="JgsNumericClass.Single"/>. Every verb that answers a fresh <see cref="JgsValue.Sparse"/>
/// therefore answers double, which is R2025b's answer for arithmetic on a logical matrix; the verbs
/// that keep the class (a subscript, a transpose, an assignment, a concatenation, arithmetic with a
/// single) put it back through <see cref="SparseInClassOf"/>.
/// </para>
/// <para>
/// An operator with no sparse kernel answers through the dense engine — the operands read full, the
/// dense operator applied, and the answer stored sparse again where R2025b's is
/// (<see cref="SparseByDense"/>) — so every class, expansion and refusal is the dense road's.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Whether a value is a single sparse matrix.</summary>
    internal static bool IsSingleSparse(JgsValue value) =>
        value.Type == JgsType.Sparse && value.NumericClass == JgsNumericClass.Single;

    /// <summary>A real sparse matrix with each stored value mapped; a zero answer leaves the pattern.</summary>
    private static CscMatrix MapStored(CscMatrix matrix, Func<double, double> map)
    {
        var triplets = new List<(int Row, int Col, double Value)>(matrix.NonZeroCount);
        for (int c = 0; c < matrix.Cols; c++)
        {
            for (int k = matrix.ColumnStarts[c]; k < matrix.ColumnStarts[c + 1]; k++)
            {
                triplets.Add((matrix.RowIndices[k], c, map(matrix.Values[k])));
            }
        }

        return CscMatrix.FromTriplets(matrix.Rows, matrix.Cols, triplets);
    }

    /// <summary>A logical sparse matrix: every stored non-zero is a one.</summary>
    internal static JgsValue LogicalSparseOf(CscMatrix matrix) =>
        JgsValue.LogicalSparse(Array.TrueForAll(matrix.Values, static v => v == 1)
            ? matrix
            : MapStored(matrix, static v => v != 0 ? 1 : 0));

    /// <summary>A single sparse matrix: every stored value rounded to single.</summary>
    internal static JgsValue SingleSparseOf(CscMatrix matrix)
    {
        JgsValue answer = JgsValue.Sparse(MapStored(matrix, static v => (float)v));
        answer.SetNumericClass(JgsNumericClass.Single);
        return answer;
    }

    /// <summary>A single complex sparse matrix, real when nothing imaginary is left unless <paramref name="keepFlag"/>.</summary>
    internal static JgsValue SingleSparseOf(JgsComplexSparse matrix, bool keepFlag)
    {
        JgsValue answer = SparseOfComplex(matrix.Map(static z => new Complex((float)z.Real, (float)z.Imaginary)), keepFlag);
        if (!answer.IsComplexSparse)
        {
            return SingleSparseOf(answer.AsSparse);
        }

        answer.SetNumericClass(JgsNumericClass.Single);
        return answer;
    }

    /// <summary>
    /// <paramref name="answer"/> in the class of <paramref name="like"/>: a logical matrix's subscript,
    /// transpose or assignment is logical, a single one's single. A dense answer (the JGS dialect's
    /// scalar subscript) takes the same class as a dense value.
    /// </summary>
    internal static JgsValue SparseInClassOf(JgsValue answer, JgsValue like, bool keepLogical = true)
    {
        bool logical = keepLogical && like.IsLogicalSparse;
        bool single = IsSingleSparse(like);
        if (!logical && !single)
        {
            return answer;
        }

        if (answer.Type != JgsType.Sparse)
        {
            return logical ? AsMask(answer) : JgsNumericClasses.Stamp(answer, JgsNumericClass.Single);
        }

        if (answer.IsComplexSparse)
        {
            return single ? SingleSparseOf(answer.AsComplexSparse, keepFlag: true) : answer;
        }

        return logical ? LogicalSparseOf(answer.AsSparse) : SingleSparseOf(answer.AsSparse);
    }

    /// <summary>
    /// A dense value stored sparse, its class and complexity kept: what an operator, a reduction or a
    /// function answers where R2025b's answer is sparse.
    /// </summary>
    internal static JgsValue DenseAsSparse(JgsValue dense, string name, int line, int col)
    {
        if (dense.Type == JgsType.Sparse)
        {
            return dense;
        }

        bool logical = IsLogicalValue(dense);
        bool single = dense.NumericClass == JgsNumericClass.Single;
        if (IsComplexDense(dense))
        {
            int[] dims = SizeDims(dense);
            if (dims.Length > 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:sparse:invalidMatrix", "Sparse matrices must be 2-D.");
            }

            JgsComplexSparse complex = JgsComplexSparse.FromColumnMajor(ColumnComplex(name, dense, line, col), dims[0], dims[1]);
            return single ? SingleSparseOf(complex, keepFlag: false) : SparseOfComplex(complex);
        }

        CscMatrix matrix = dense.Type is JgsType.Number or JgsType.Bool
            ? CscMatrix.FromColumnMajor([dense.AsNumber], 1, 1)
            : CscFromDense(name, dense, line, col);
        return logical ? LogicalSparseOf(matrix) : single ? SingleSparseOf(matrix) : JgsValue.Sparse(matrix);
    }

    /// <summary>Whether a value is one element: a scalar, a 1-by-1 array or a 1-by-1 sparse matrix.</summary>
    private static bool IsOneElement(JgsValue value) => value.Type switch
    {
        JgsType.Number or JgsType.Bool or JgsType.Complex => true,
        JgsType.Sparse => value.SparseExtent is (1, 1),
        JgsType.Array => value.ArrayLength == 1 && !value.IsStringArray && !value.IsCharMatrix,
        _ => false,
    };

    /// <summary>The one real number a 1-by-1 real sparse matrix holds, as a number reader takes it.</summary>
    internal static bool TrySparseScalar(JgsValue value, out double number)
    {
        number = 0;
        if (value.Type != JgsType.Sparse || value.IsComplexSparse || value.SparseExtent is not (1, 1))
        {
            return false;
        }

        number = value.AsSparse.At(0, 0);
        return true;
    }

    // --- operators -----------------------------------------------------------------------------

    private static bool IsRelationalOperator(TokenType op) => op is TokenType.EqualEqual or TokenType.BangEqual
        or TokenType.Greater or TokenType.GreaterEqual or TokenType.Less or TokenType.LessEqual;

    private static bool IsSparseArithmetic(TokenType op) => op is TokenType.Plus or TokenType.Minus
        or TokenType.Star or TokenType.Slash or TokenType.Backslash or TokenType.Caret
        or TokenType.DotStar or TokenType.DotSlash or TokenType.DotBackslash or TokenType.DotCaret;

    /// <summary>
    /// Every binary operator with a sparse operand (open item 331, ADR 0369). The sparse kernels
    /// answer what they cover; everything else answers through <paramref name="dense"/>, the dense
    /// operator, over the operands read full. A single operand makes the answer single; a logical one
    /// is a matrix of ones to arithmetic, which answers double.
    /// </summary>
    internal static JgsValue SparseOperator(
        TokenType op, JgsValue left, JgsValue right, Node at, Func<TokenType, JgsValue, JgsValue, JgsValue> dense)
    {
        int line = at.Line;
        int col = at.Column;
        // An integer beside a sparse matrix is refused by every arithmetic, relational and
        // elementwise logical operator (open item 340, probe p7b_a; ADR 0374).
        if ((IsSparseArithmetic(op) || IsRelationalOperator(op) || op is TokenType.Amp or TokenType.Pipe)
            && (left.NumericClass.IsInteger() || right.NumericClass.IsInteger()))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sparseInteger",
                "Sparse integer array arithmetic operations are not supported.");
        }

        if (IsRelationalOperator(op) || op is TokenType.Amp or TokenType.Pipe)
        {
            return SparseLogicalOperator(op, left, right, at, dense);
        }

        if (!IsSparseArithmetic(op))
        {
            return SparseBinary(op, left, right, at); // its own refusal, in its own words
        }

        JgsValue answer = SparseKernelCovers(op, left, right)
            ? SparseBinary(op, left, right, at)
            : SparseByDense(op, left, right, at, dense);

        // A matrix solved against a sparse right-hand side is sparse (R2025b's b_csolve_sp).
        if (op == TokenType.Backslash && left.Type == JgsType.Sparse && right.Type == JgsType.Sparse && answer.Type != JgsType.Sparse)
        {
            answer = DenseAsSparse(answer, "\\", line, col);
        }

        bool single = left.NumericClass == JgsNumericClass.Single || right.NumericClass == JgsNumericClass.Single;
        if (!single)
        {
            return answer;
        }

        if (answer.Type != JgsType.Sparse)
        {
            return answer.NumericClass == JgsNumericClass.Single ? answer : JgsNumericClasses.Stamp(answer, JgsNumericClass.Single);
        }

        return IsSingleSparse(answer) ? answer
            : answer.IsComplexSparse ? SingleSparseOf(answer.AsComplexSparse, keepFlag: true)
            : SingleSparseOf(answer.AsSparse);
    }

    /// <summary>
    /// Whether the sparse kernels (<see cref="SparseBinary"/>) answer this arithmetic: two sparse
    /// matrices of agreeing shape under <c>+ - * .*</c>, a sparse matrix scaled by a dense scalar, a
    /// sum or difference with a dense value, a product with a dense matrix, and a real system solved.
    /// A 1-by-1 sparse matrix beside a larger operand is a scalar, which the dense road reads as one.
    /// </summary>
    private static bool SparseKernelCovers(TokenType op, JgsValue left, JgsValue right)
    {
        bool ls = left.Type == JgsType.Sparse, rs = right.Type == JgsType.Sparse;
        bool oneL = IsOneElement(left), oneR = IsOneElement(right);
        if (ls && rs)
        {
            return oneL == oneR && op is TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.DotStar
                && !(op is TokenType.Plus or TokenType.Minus && left.SparseExtent != right.SparseExtent);
        }

        JgsValue sparse = ls ? left : right;
        JgsValue other = ls ? right : left;
        bool denseNumber = other.Type is JgsType.Number or JgsType.Bool or JgsType.Complex
            || (other.Type == JgsType.Array && !other.IsStringArray && !other.IsCharMatrix && !other.IsTime);
        if (!denseNumber)
        {
            return false;
        }

        bool otherScalar = other.Type is JgsType.Number or JgsType.Bool or JgsType.Complex;
        return op switch
        {
            TokenType.Star or TokenType.DotStar when otherScalar => true,
            TokenType.Slash or TokenType.DotSlash when otherScalar && ls => true,
            // A scalar term is the dense road's: S + 0 is full in R2025b (z_plus0), as S + 1 is.
            TokenType.Plus or TokenType.Minus => !IsOneElement(sparse) && !otherScalar,
            TokenType.Star => !IsOneElement(sparse),
            TokenType.Backslash => ls && !sparse.IsComplexSparse && !IsComplexDense(other) && !IsOneElement(sparse),
            _ => false,
        };
    }

    /// <summary>
    /// An operator the sparse kernels do not cover: the operands read full, the dense operator applied,
    /// and the answer sparse where R2025b's is (<see cref="SparseAnswerWanted"/>).
    /// </summary>
    private static JgsValue SparseByDense(
        TokenType op, JgsValue left, JgsValue right, Node at, Func<TokenType, JgsValue, JgsValue, JgsValue> dense)
    {
        JgsValue a = left.Type == JgsType.Sparse ? SparseDense(left) : left;
        JgsValue b = right.Type == JgsType.Sparse ? SparseDense(right) : right;
        JgsValue answer = dense(op, a, b);
        return SparseAnswerWanted(op, left, right) && answer.Type is JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.Array
            ? DenseAsSparse(answer, OpName(op), at.Line, at.Column)
            : answer;
    }

    /// <summary>
    /// Whether R2025b's answer is sparse (probes p6b_a, p6b_b): a product when either factor is sparse
    /// and one is a scalar, or both are sparse; a quotient when the dividend is; a power when the base
    /// is; a sum or difference when both terms are; a comparison always; <c>&amp;</c> when either side
    /// is; <c>|</c> when both are.
    /// </summary>
    private static bool SparseAnswerWanted(TokenType op, JgsValue left, JgsValue right)
    {
        bool ls = left.Type == JgsType.Sparse, rs = right.Type == JgsType.Sparse;
        bool oneL = IsOneElement(left), oneR = IsOneElement(right);
        return op switch
        {
            TokenType.Plus or TokenType.Minus or TokenType.Pipe => ls && rs,
            TokenType.DotStar or TokenType.Amp => ls || rs,
            TokenType.Star => oneL || oneR ? ls || rs : ls && rs,
            TokenType.DotSlash => ls,
            TokenType.Slash => oneR ? ls : ls && rs,
            TokenType.DotBackslash => rs,
            TokenType.Backslash => oneL ? rs : ls && rs,
            TokenType.DotCaret or TokenType.Caret => ls,
            _ => IsRelationalOperator(op),
        };
    }

    /// <summary>
    /// A comparison, <c>&amp;</c> or <c>|</c> with a sparse operand. A real sparse matrix against a real
    /// scalar walks the stored pattern when zero compares false; everything else reads full.
    /// </summary>
    private static JgsValue SparseLogicalOperator(
        TokenType op, JgsValue left, JgsValue right, Node at, Func<TokenType, JgsValue, JgsValue, JgsValue> dense)
    {
        bool ls = left.Type == JgsType.Sparse;
        JgsValue sparse = ls ? left : right;
        JgsValue other = ls ? right : left;
        if (IsRelationalOperator(op) && left.Type != right.Type && !sparse.IsComplexSparse
            && other.Type is JgsType.Number or JgsType.Bool)
        {
            return SparseCompareScalar(op, sparse.AsSparse, other.AsNumber, ls);
        }

        return SparseByDense(op, left, right, at, dense);
    }

    /// <summary>A real sparse matrix compared with a scalar, as a logical sparse mask.</summary>
    private static JgsValue SparseCompareScalar(TokenType op, CscMatrix matrix, double scalar, bool sparseOnLeft)
    {
        bool Holds(double entry)
        {
            double a = sparseOnLeft ? entry : scalar, b = sparseOnLeft ? scalar : entry;
            return op switch
            {
                TokenType.Greater => a > b,
                TokenType.GreaterEqual => a >= b,
                TokenType.Less => a < b,
                TokenType.LessEqual => a <= b,
                TokenType.EqualEqual => a == b,
                _ => a != b,
            };
        }

        // Where zero itself compares true the answer is mostly ones and is built over every position;
        // otherwise only the stored entries can answer true, so the pattern is walked.
        if (Holds(0))
        {
            double[] flat = matrix.ToColumnMajor();
            for (int i = 0; i < flat.Length; i++)
            {
                flat[i] = Holds(flat[i]) ? 1 : 0;
            }

            return JgsValue.LogicalSparse(CscMatrix.FromColumnMajor(flat, matrix.Rows, matrix.Cols));
        }

        var truths = new List<(int, int, double)>();
        for (int c = 0; c < matrix.Cols; c++)
        {
            for (int k = matrix.ColumnStarts[c]; k < matrix.ColumnStarts[c + 1]; k++)
            {
                if (Holds(matrix.Values[k]))
                {
                    truths.Add((matrix.RowIndices[k], c, 1));
                }
            }
        }

        return JgsValue.LogicalSparse(CscMatrix.FromTriplets(matrix.Rows, matrix.Cols, truths));
    }

    /// <summary>
    /// <c>~S</c> in the MATLAB dialect: a logical sparse matrix, true where S is zero (R2025b's w_not);
    /// a complex operand is R2025b's refusal.
    /// </summary>
    internal static JgsValue SparseNot(JgsValue value, int line, int col)
    {
        if (value.IsComplexSparse)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:Not:operandsNotReal", "Operands must be real.");
        }

        CscMatrix matrix = value.AsSparse;
        double[] flat = matrix.ToColumnMajor();
        for (int i = 0; i < flat.Length; i++)
        {
            if (double.IsNaN(flat[i]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:nologicalnan", "NaN values cannot be converted to logicals.");
            }

            flat[i] = flat[i] == 0 ? 1 : 0;
        }

        return JgsValue.LogicalSparse(CscMatrix.FromColumnMajor(flat, matrix.Rows, matrix.Cols));
    }

    // --- assignment ------------------------------------------------------------------------------

    /// <summary>
    /// <c>S(...) = v</c> for a sparse matrix of any kind (open item 331, ADR 0369): a complex matrix,
    /// or a complex value written into a real one, is written part by part through the real road
    /// (<see cref="SparseAssigned"/>), so every subscript form, growth and deletion is that road's,
    /// and the answer is real when nothing imaginary is left (R2025b's a_c_real). A logical matrix
    /// stays logical, its written values read as truths (a NaN or a complex value refused in
    /// R2025b's words); a single one stays single.
    /// </summary>
    internal static JgsValue SparseAssignedAnyKind(
        JgsValue held, IReadOnlyList<JgsValue?> subscripts, JgsValue rhs, JgsDialect dialect, int line, int col)
    {
        bool deletion = rhs.Type == JgsType.Array && rhs.ArrayLength == 0 && !rhs.IsStringArray;
        bool complexRhs = !deletion && (rhs.IsComplexSparse || rhs.Type == JgsType.Complex
            || (rhs.Type == JgsType.Array && HasComplexElements(rhs)));
        if (held.IsLogicalSparse)
        {
            if (complexRhs)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:nologicalcomplex", "Complex values cannot be converted to logicals.");
            }

            if (!deletion && WrittenNaN(rhs))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:nologicalnan", "NaN values cannot be converted to logicals.");
            }

            return LogicalSparseOf(SparseAssigned(held.AsSparse, subscripts, rhs, dialect, line, col));
        }

        if (!held.IsComplexSparse && !complexRhs)
        {
            return SparseInClassOf(JgsValue.Sparse(SparseAssigned(held.AsSparse, subscripts, rhs, dialect, line, col)), held);
        }

        JgsComplexSparse matrix = ComplexEntriesOf(held);
        (JgsValue realRhs, JgsValue imagRhs) = deletion ? (rhs, rhs) : ComplexPartsOf(rhs, line, col);
        CscMatrix re = SparseAssigned(matrix.MapReal(static v => v.Real), subscripts, realRhs, dialect, line, col);
        CscMatrix im = SparseAssigned(matrix.MapReal(static v => v.Imaginary), subscripts, imagRhs, dialect, line, col);
        JgsValue answer = SparseOfComplex(JgsComplexSparse.FromReal(re)
            .Add(JgsComplexSparse.FromReal(im).Map(static v => v * Complex.ImaginaryOne), 1));
        return IsSingleSparse(held) ? SparseInClassOf(answer, held) : answer;
    }

    /// <summary>A written value's real and imaginary parts, each the shape the value has.</summary>
    private static (JgsValue Real, JgsValue Imag) ComplexPartsOf(JgsValue rhs, int line, int col)
    {
        switch (rhs.Type)
        {
            case JgsType.Complex:
                return (JgsValue.Number(rhs.AsComplex.Real), JgsValue.Number(rhs.AsComplex.Imaginary));
            case JgsType.Number or JgsType.Bool:
                return (rhs, JgsValue.Number(0));
            case JgsType.Sparse:
            {
                JgsComplexSparse entries = ComplexEntriesOf(rhs);
                return (JgsValue.Sparse(entries.MapReal(static v => v.Real)), JgsValue.Sparse(entries.MapReal(static v => v.Imaginary)));
            }

            default:
            {
                int[] dims = SizeDims(rhs);
                Complex[] flat = ColumnComplex("sparse assignment", rhs, line, col);
                return (JgsMatrix.FromColumnMajorDims(Array.ConvertAll(flat, static z => z.Real), dims),
                    JgsMatrix.FromColumnMajorDims(Array.ConvertAll(flat, static z => z.Imaginary), dims));
            }
        }
    }

    /// <summary>Whether a value written into a logical matrix holds a NaN.</summary>
    private static bool WrittenNaN(JgsValue rhs) => rhs.Type switch
    {
        JgsType.Number => double.IsNaN(rhs.AsNumber),
        JgsType.Sparse => !rhs.IsComplexSparse && Array.Exists(rhs.AsSparse.Values, double.IsNaN),
        JgsType.Array when !rhs.IsStringArray && !rhs.IsCharMatrix && !HasComplexElements(rhs) =>
            Array.Exists(ToDoubles("sparse assignment", rhs, 0, 0), double.IsNaN),
        _ => false,
    };

    // --- conversions -----------------------------------------------------------------------------

    /// <summary>
    /// <c>logical(S)</c>: a logical sparse matrix, refused for a complex or NaN entry in R2025b's words.
    /// </summary>
    private static JgsValue SparseToLogical(JgsValue value, int line, int col)
    {
        if (value.IsComplexSparse)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:nologicalcomplex", "Complex values cannot be converted to logicals.");
        }

        CscMatrix matrix = value.AsSparse;
        if (Array.Exists(matrix.Values, double.IsNaN))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:nologicalnan", "NaN values cannot be converted to logicals.");
        }

        return value.IsLogicalSparse ? value : LogicalSparseOf(matrix);
    }

    /// <summary><c>double(S)</c>: the same entries as a double sparse matrix.</summary>
    private static JgsValue SparseToDouble(JgsValue value) =>
        value.IsComplexSparse
            ? JgsValue.ComplexSparse(value.AsComplexSparse)
            : JgsValue.Sparse(value.AsSparse);

    /// <summary><c>single(S)</c>: the entries rounded to single, a single sparse matrix.</summary>
    private static JgsValue SparseToSingle(JgsValue value) =>
        value.IsComplexSparse
            ? SingleSparseOf(value.AsComplexSparse, keepFlag: true)
            : SingleSparseOf(value.AsSparse);

    /// <summary>The class a value is stored in as R2025b's <c>sparse</c> reads it, or a refusal.</summary>
    private static void RequireSparseClass(JgsValue value, bool third, int line, int col)
    {
        bool text = value.Type == JgsType.String || value.IsCharMatrix || value.IsStringArray;
        if (text || value.NumericClass.IsInteger() || value.Type is JgsType.Cell or JgsType.Struct)
        {
            throw third
                ? new JgsRuntimeException(line, col, "MATLAB:sparse:invalidThirdInput", "Third input must be single, double, or logical.")
                : new JgsRuntimeException(line, col, "MATLAB:sparse:invalidMatrix", "Input matrix must be single, double, or logical.");
        }
    }

    /// <summary>
    /// <c>sparse(A)</c> and <c>sparse(i, j, v, ...)</c> answered in the class of the dense input: a
    /// logical input is a logical matrix, a single one single (R2025b's w_logical_sp, w_single).
    /// </summary>
    private static JgsValue SparseInClassOfDense(JgsValue answer, JgsValue input)
    {
        if (answer.Type != JgsType.Sparse)
        {
            return answer;
        }

        if (input.NumericClass == JgsNumericClass.Single)
        {
            return answer.IsComplexSparse ? SingleSparseOf(answer.AsComplexSparse, keepFlag: true) : SingleSparseOf(answer.AsSparse);
        }

        return IsLogicalValue(input) && !answer.IsComplexSparse ? LogicalSparseOf(answer.AsSparse) : answer;
    }

    // --- functions over a sparse argument ---------------------------------------------------------

    /// <summary>
    /// The functions R2025b answers sparse for a sparse argument, applied to each element: the dense
    /// function runs over the stored values (and over zero, to learn whether the pattern survives),
    /// so a large matrix is never expanded unless zero maps to something that is not zero.
    /// </summary>
    private static readonly string[] SparseElementwise =
    [
        "sqrt", "exp", "log", "log2", "log10", "log1p", "expm1",
        "sin", "cos", "tan", "asin", "acos", "atan", "sinh", "cosh", "tanh", "asinh", "acosh", "atanh",
        "floor", "ceil", "round", "fix", "sign", "isnan", "isinf",
        // ADR 0374 (probes p7b_b, p7b_c): the degree forms, the angle conversions and the powers of two.
        "sind", "cosd", "tand", "asind", "acosd", "atand", "rad2deg", "deg2rad", "nextpow2", "pow2", "factorial",
        "sec", "csc", "cot",
    ];

    /// <summary>
    /// The functions R2025b answers sparse for a sparse argument that are not elementwise over one
    /// argument: the arguments read full, the dense function applied, and its first answer stored
    /// sparse (the second of <c>max</c> and <c>min</c>, a position, stays dense).
    /// </summary>
    private static readonly string[] SparseRestored =
    [
        "max", "min", "mod", "rem", "atan2", "tril", "triu", "reshape", "kron", "inv", "repmat",
        "diag", "spones", "spfun",
        // ADR 0374 (probes p7b_a, p7b_b, p7b_c): the statistics, the rearrangements and the
        // functions of one argument whose dense form reads the matrix as values.
        "median", "mode", "std", "var", "sort", "unique", "diff", "circshift", "flip", "fliplr", "flipud",
        "rot90", "permute", "rescale", "normalize", "cummax",
        "cummin", "trapz", "cumtrapz", "angle", "nthroot", "realpow", "gcd", "lcm",
    ];

    /// <summary>
    /// The functions R2025b answers dense for a sparse argument: the arguments read full and the dense
    /// function answers (probe p6b_b: isfinite, hypot, trace, norm and det).
    /// </summary>
    private static readonly string[] SparseDensified =
    [
        "isfinite", "hypot", "trace", "norm", "det", "int2str", "num2str",
        "issorted", "ismember", "polyval", "isprime", // ADR 0374 (probes p7b_a, p7b_b)
    ];

    /// <summary>
    /// Wraps the functions above so a sparse argument, of any class, reaches each as R2025b's does
    /// (open item 331, ADR 0369). Registered after every other wrapper, so it sees the name as the
    /// session does.
    /// </summary>
    private static void WrapSparseFunctions(JgsEnvironment env)
    {
        void Wrap(string name, Func<BuiltinFunction, IReadOnlyList<JgsValue>, int, int, int, JgsValue[]?> sparse) =>
            WrapOverSparse(env, name, sparse);

        static JgsValue[] Full(IReadOnlyList<JgsValue> args) => SparseArgumentsFull(args);

        static JgsValue[] Answer(BuiltinFunction inner, JgsValue[] args, int wanted, int line, int col) =>
            SparseCallAnswers(inner, args, wanted, line, col);

        foreach (string name in SparseElementwise)
        {
            string fn = name;
            Wrap(fn, (inner, args, wanted, line, col) =>
            {
                if (wanted > 1)
                {
                    return null;
                }

                // round(S, n) and the like: the arguments read full, sparse again when S was.
                if (args.Count != 1)
                {
                    JgsValue answer = inner.Call(Full(args), line, col);
                    return [args[0].Type == JgsType.Sparse && answer.Type is JgsType.Number or JgsType.Bool or JgsType.Array
                        ? DenseAsSparse(answer, fn, line, col) : answer];
                }

                return [SparseMapped(fn, inner, args[0], line, col)];
            });
        }

        foreach (string name in SparseRestored)
        {
            string fn = name;
            Wrap(fn, (inner, args, wanted, line, col) =>
            {
                // spfun's function and spones' argument are the matrix's stored entries alone.
                if (fn == "spfun" && args.Count == 2 && args[1].Type == JgsType.Sparse && args[1].IsComplexSparse)
                {
                    return [SparseSpfun(args, line, col)];
                }

                if (fn == "spones" && args.Count == 1 && args[0].IsComplexSparse)
                {
                    return [JgsValue.Sparse(args[0].AsComplexSparse.MapReal(static _ => 1))];
                }

                if (fn is "spfun" or "spones" || (fn is "diag" or "repmat" && args.Count >= 1 && args[0].Type == JgsType.Sparse && !args[0].IsComplexSparse))
                {
                    return null; // the real road answers these itself
                }

                // The answer is sparse when a data argument is: max(S(1, 1), 0) is, and
                // repmat(7, 1, S(1, 1)) is not (R2025b's o_max2, o_repmat).
                int data = fn is "max" or "min" or "mod" or "rem" or "atan2" or "kron" or "gcd" or "lcm" ? 2 : 1;
                bool sparseData = false;
                for (int i = 0; i < Math.Min(data, args.Count); i++)
                {
                    sparseData |= args[i].Type == JgsType.Sparse;
                }

                JgsValue[] answers = Answer(inner, Full(args), wanted, line, col);
                if (sparseData && answers.Length > 0 && answers[0].Type is JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.Array
                    && !answers[0].IsStringArray)
                {
                    answers[0] = DenseAsSparse(answers[0], fn, line, col);
                }

                return answers;
            });
        }

        foreach (string name in SparseDensified)
        {
            Wrap(name, (inner, args, wanted, line, col) => Answer(inner, Full(args), wanted, line, col));
        }

        // mat2str(S) is the call that rebuilds it: sparse(I, J, V, m, n), each list written as mat2str
        // writes a column (R2025b's e_mat2str, e_mat2str_one).
        Wrap("mat2str", (inner, args, wanted, line, col) =>
        {
            if (args.Count == 0 || args[0].Type != JgsType.Sparse)
            {
                return null;
            }

            (int rows, int cols) = args[0].SparseExtent;
            var found = new List<(int Row, int Col, Complex Value)>();
            foreach ((int r, int c, Complex entry) in ComplexEntriesOf(args[0]).Entries())
            {
                found.Add((r + 1, c + 1, entry));
            }

            JgsValue Column(Func<(int Row, int Col, Complex Value), Complex> pick)
            {
                Complex[] values = [.. found.Select(pick)];
                return Array.Exists(values, static z => z.Imaginary != 0)
                    ? ComplexStorage(values, [values.Length, 1])
                    : JgsMatrix.FromColumnMajor(Array.ConvertAll(values, static z => z.Real), values.Length, 1);
            }

            string Text(JgsValue column, bool values)
            {
                JgsValue[] callArgs = values ? [SparseInClassOf(column, args[0]), .. args.Skip(1)] : [column];
                return inner.Call(callArgs, line, col).AsString;
            }

            string rowText = Text(Column(static e => e.Row), false);
            string colText = Text(Column(static e => e.Col), false);
            string valueText = Text(Column(static e => e.Value), true);
            return [JgsValue.Str($"sparse({rowText}, {colText}, {valueText}, {rows}, {cols})")];
        });

        // The orderings read the pattern alone, so a complex matrix is ordered by where it stores
        // entries (R2025b's b_csymrcm).
        foreach (string ordering in new[] { "symrcm", "symamd", "amd", "colperm", "dmperm", "etree", "sprank", "symbfact" })
        {
            Wrap(ordering, (inner, args, wanted, line, col) => args.Count >= 1 && args[0].IsComplexSparse
                ? Answer(inner, [JgsValue.Sparse(args[0].AsComplexSparse.MapReal(static _ => 1)), .. args.Skip(1)], wanted, line, col)
                : null);
        }

        // The class conversions: logical, double and single keep the matrix sparse; an integer class
        // has no sparse storage in R2025b.
        Wrap("logical", (inner, args, wanted, line, col) =>
            args.Count == 1 && args[0].Type == JgsType.Sparse ? [SparseToLogical(args[0], line, col)] : null);
        Wrap("double", (inner, args, wanted, line, col) =>
            args.Count == 1 && args[0].Type == JgsType.Sparse ? [SparseToDouble(args[0])] : null);
        Wrap("single", (inner, args, wanted, line, col) =>
            args.Count == 1 && args[0].Type == JgsType.Sparse ? [SparseToSingle(args[0])] : null);
        foreach (string integer in new[] { "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64" })
        {
            Wrap(integer, (inner, args, wanted, line, col) => args.Count == 1 && args[0].Type == JgsType.Sparse
                ? throw new JgsRuntimeException(line, col, "MATLAB:unimplementedSparseType", "Attempt to convert to unimplemented sparse type")
                : null);
        }

        WrapSparseLeftovers(env); // ADR 0374: the shapes, casts, logical verbs and refusals over a sparse argument
    }

    /// <summary>
    /// An elementwise function of a sparse matrix. Where it maps zero to zero over a real matrix, it
    /// runs over the stored values alone and the pattern is kept; otherwise over the matrix read full.
    /// </summary>
    private static JgsValue SparseMapped(string name, BuiltinFunction inner, JgsValue sparse, int line, int col)
    {
        if (!sparse.IsComplexSparse)
        {
            CscMatrix matrix = sparse.AsSparse;
            JgsValue zero = inner.Call([SparseInClassOf(JgsValue.Number(0), sparse)], line, col);
            if (zero.Type is JgsType.Number or JgsType.Bool && zero.AsNumber == 0)
            {
                JgsValue column = SparseInClassOf(JgsMatrix.FromColumnMajor((double[])matrix.Values.Clone(), matrix.Values.Length, 1), sparse);
                JgsValue mapped = inner.Call([column], line, col);
                bool logical = IsLogicalValue(mapped);
                bool single = mapped.NumericClass == JgsNumericClass.Single;
                Complex[] values = ColumnComplex(name, mapped, line, col);
                var triplets = new List<(int, int, Complex)>(values.Length);
                for (int c = 0; c < matrix.Cols; c++)
                {
                    for (int k = matrix.ColumnStarts[c]; k < matrix.ColumnStarts[c + 1]; k++)
                    {
                        triplets.Add((matrix.RowIndices[k], c, values[k]));
                    }
                }

                JgsValue answer = SparseOfComplex(JgsComplexSparse.FromTriplets(matrix.Rows, matrix.Cols, triplets));
                return logical && !answer.IsComplexSparse ? LogicalSparseOf(answer.AsSparse)
                    : single ? SparseInClassOf(answer, SingleLike)
                    : answer;
            }
        }

        return DenseAsSparse(inner.Call([SparseDense(sparse)], line, col), name, line, col);
    }

    /// <summary>A 1-by-1 single sparse matrix, the template <see cref="SparseInClassOf"/> reads a class from.</summary>
    private static JgsValue SingleLike => SingleSparseOf(CscMatrix.FromTriplets(1, 1, []));

    /// <summary><c>spfun(f, S)</c> of a complex matrix: <c>f</c> over the stored entries alone.</summary>
    private static JgsValue SparseSpfun(IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsComplexSparse matrix = args[1].AsComplexSparse;
        if (args[0].Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, "spfun needs a function handle first.");
        }

        Complex[] stored = matrix.Values;
        JgsValue column = ComplexStorage(stored, [stored.Length, 1]);
        JgsValue mapped = args[0].AsCallable.Call([column], line, col);
        Complex[] values = ColumnComplex("spfun", mapped, line, col);
        if (values.Length != stored.Length)
        {
            throw new JgsRuntimeException(line, col, "spfun's function must answer one value per stored entry.");
        }

        var triplets = new List<(int, int, Complex)>(values.Length);
        int at = 0;
        foreach ((int r, int c, Complex _) in matrix.Entries())
        {
            triplets.Add((r, c, values[at++]));
        }

        return SparseOfComplex(JgsComplexSparse.FromTriplets(matrix.Rows, matrix.Cols, triplets));
    }
}
