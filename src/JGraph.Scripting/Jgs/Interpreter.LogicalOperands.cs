namespace JGraph.Scripting.Jgs;

/// <summary>
/// R2025b's logical conversions in the MATLAB dialect (open items 340 and 341, ADR 0379): what
/// <c>&amp;</c>, <c>|</c>, <c>~</c>, a comparison, an <c>if</c> or <c>while</c> condition and the
/// short-circuit operators refuse, and the logical empty a comparison or logical operator answers.
/// </summary>
/// <remarks>
/// Measured in R2025b 25.2 (lane 7C's probes 2 and 4): a NaN anywhere in an operand of
/// <c>&amp;</c>, <c>|</c>, <c>~</c>, <c>xor</c>, <c>logical</c>, a condition, or an evaluated
/// operand of <c>&amp;&amp;</c> or <c>||</c> is <c>MATLAB:nologicalnan</c> (an empty partner does
/// not save it: <c>NaN &amp; zeros(1, 0)</c> is refused); a complex operand, the zero-imaginary
/// flag included, is <c>MATLAB:math:mustBeReal</c> under <c>&amp;</c> and <c>|</c>,
/// <c>MATLAB:Not:operandsNotReal</c> under <c>~</c> and <c>MATLAB:nologicalcomplex</c> as a
/// condition; a string, cell or struct beside <c>&amp;</c> or <c>|</c> is
/// <c>MATLAB:math:mustBeNumericCharOrLogical</c>; an integer compared by <c>&lt;</c>,
/// <c>&lt;=</c>, <c>&gt;</c> or <c>&gt;=</c> with a complex number is
/// <c>MATLAB:relop:complexIntegers</c> (<c>==</c> and <c>~=</c> answer). Whatever the classes, an
/// empty answer of a comparison or a logical operator is a logical empty of the expanded size
/// (<c>int8(2) &amp; true(0)</c> is a logical 0-by-0, <c>zeros(1, 0, 2) &lt; 1</c> a logical
/// 1-by-0-by-2), in both storages.
/// </remarks>
internal sealed partial class Interpreter
{
    /// <summary>The comparisons and the elementwise logical operators.</summary>
    private static bool IsRelationalOrLogical(TokenType op) => op is TokenType.Amp or TokenType.Pipe
        or TokenType.EqualEqual or TokenType.BangEqual or TokenType.Less or TokenType.LessEqual
        or TokenType.Greater or TokenType.GreaterEqual;

    /// <summary>
    /// The MATLAB dialect's refusals of a comparison or logical operator's operands, and its answer
    /// when an operand is empty (a logical empty of the expanded size); null when the operator goes
    /// on to compute. Asked after a char operand has become its codes.
    /// </summary>
    private static JgsValue? MatlabLogicalOperands(TokenType op, JgsValue left, JgsValue right, Node at)
    {
        if (op is TokenType.Amp or TokenType.Pipe)
        {
            if (left.IsStringArray || right.IsStringArray || left.Type is JgsType.Cell or JgsType.Struct
                || right.Type is JgsType.Cell or JgsType.Struct)
            {
                throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:math:mustBeNumericCharOrLogical",
                    "Invalid data type. Argument must be numeric, char, or logical.");
            }

            if ((left.Type != JgsType.Sparse && JgsBuiltins.McAnyComplex(left))
                || (right.Type != JgsType.Sparse && JgsBuiltins.McAnyComplex(right)))
            {
                throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:math:mustBeReal", "Argument must be real.");
            }

            if (JgsBuiltins.McAnyNaN(left) || JgsBuiltins.McAnyNaN(right))
            {
                throw JgsBuiltins.NoLogicalNaN(at.Line, at.Column);
            }
        }
        else if (op is TokenType.Less or TokenType.LessEqual or TokenType.Greater or TokenType.GreaterEqual
            && ((left.NumericClass.IsInteger() && JgsBuiltins.McAnyComplex(right))
                || (right.NumericClass.IsInteger() && JgsBuiltins.McAnyComplex(left))))
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:relop:complexIntegers",
                "Comparison operations on complex integers are not supported.");
        }

        // Only an empty array makes an empty answer; a number or a non-empty array computes.
        if (!(left.Type == JgsType.Array && left.ArrayLength == 0) && !(right.Type == JgsType.Array && right.ArrayLength == 0))
        {
            return null;
        }

        if (!IsPlainLogicalOperand(left) || !IsPlainLogicalOperand(right))
        {
            return null;
        }

        int[] sizeLeft = JgsBuiltins.SizeDims(left);
        int[] sizeRight = JgsBuiltins.SizeDims(right);
        if (!sizeLeft.Contains(0) && !sizeRight.Contains(0))
        {
            return null;
        }

        int rank = Math.Max(sizeLeft.Length, sizeRight.Length);
        var size = new int[rank];
        for (int d = 0; d < rank; d++)
        {
            int a = d < sizeLeft.Length ? sizeLeft[d] : 1;
            int b = d < sizeRight.Length ? sizeRight[d] : 1;
            if (a != b && a != 1 && b != 1)
            {
                return null; // the size refusal is the operator's
            }

            size[d] = a == 1 ? b : a;
        }

        return JgsBuiltins.McLogicalAnswer([], size);
    }

    /// <summary>A number, logical or complex value, or a numeric or logical array: what the empty rule above may answer for.</summary>
    private static bool IsPlainLogicalOperand(JgsValue value) => value.Type switch
    {
        JgsType.Number or JgsType.Bool or JgsType.Complex => true,
        JgsType.Array => !value.IsStringArray && !value.IsTime && !value.IsCategorical,
        _ => false,
    };

    /// <summary>
    /// MATLAB's <c>~x</c> refusals and its empty (open items 340, 341): a complex operand is
    /// <c>MATLAB:Not:operandsNotReal</c>, a NaN <c>MATLAB:nologicalnan</c>, an empty a logical empty
    /// of its size. Null when the operator goes on to compute. Asked after a char is its codes.
    /// </summary>
    private static JgsValue? MatlabNotOperand(JgsValue operand, int line, int column)
    {
        if (operand.Type != JgsType.Sparse && JgsBuiltins.McAnyComplex(operand))
        {
            throw new JgsRuntimeException(line, column, "MATLAB:Not:operandsNotReal", "Operands must be real.");
        }

        if (JgsBuiltins.McAnyNaN(operand))
        {
            throw JgsBuiltins.NoLogicalNaN(line, column);
        }

        return operand.Type == JgsType.Array && IsPlainLogicalOperand(operand) && operand.ArrayLength == 0
            ? JgsBuiltins.McLogicalAnswer([], JgsBuiltins.SizeDims(operand))
            : null;
    }

    /// <summary>
    /// <c>not(x)</c> in the MATLAB dialect: the operator <c>~x</c>, char codes, refusals and all.
    /// </summary>
    internal JgsValue ApplyMatlabNot(JgsValue operand, int line, int column)
    {
        if (IsCharOperand(operand))
        {
            operand = CharCodes(operand);
        }

        if (MatlabNotOperand(operand, line, column) is { } early)
        {
            return early;
        }

        if (operand.Type == JgsType.Array)
        {
            return JgsBuiltins.MapToBool("~", operand, static x => x == 0, line, column,
                kernel: JGraph.Numerics.PackedMath.MaskTest.IsZero);
        }

        return operand.Type == JgsType.Sparse
            ? JgsBuiltins.SparseNot(operand, line, column)
            : JgsValue.Bool(!operand.IsTruthy);
    }

    /// <summary>
    /// <c>xor(a, b)</c> in the MATLAB dialect: a string, cell or struct is
    /// <c>MATLAB:math:mustBeNumericCharOrLogical</c>, a complex part <c>MATLAB:math:mustBeReal</c>, a
    /// NaN <c>MATLAB:nologicalnan</c>; otherwise <c>(a ~= 0) ~= (b ~= 0)</c>, which expands, refuses
    /// sizes and answers a logical empty as the operators do.
    /// </summary>
    internal JgsValue ApplyMatlabXor(JgsValue left, JgsValue right, int line, int column)
    {
        if (left.IsStringArray || right.IsStringArray || left.Type is JgsType.Cell or JgsType.Struct
            || right.Type is JgsType.Cell or JgsType.Struct)
        {
            throw new JgsRuntimeException(line, column, "MATLAB:math:mustBeNumericCharOrLogical",
                "Invalid data type. Argument must be numeric, char, or logical.");
        }

        if (JgsBuiltins.McAnyComplex(left) || JgsBuiltins.McAnyComplex(right))
        {
            throw new JgsRuntimeException(line, column, "MATLAB:math:mustBeReal", "Argument must be real.");
        }

        if (JgsBuiltins.McAnyNaN(left) || JgsBuiltins.McAnyNaN(right))
        {
            throw JgsBuiltins.NoLogicalNaN(line, column);
        }

        JgsValue zero = JgsValue.Number(0);
        return ApplyOperator(TokenType.BangEqual,
            ApplyOperator(TokenType.BangEqual, left, zero, line, column),
            ApplyOperator(TokenType.BangEqual, right, zero, line, column), line, column);
    }

    /// <summary>
    /// Whether a condition holds. In the MATLAB dialect a complex condition is
    /// <c>MATLAB:nologicalcomplex</c> and a NaN anywhere in it <c>MATLAB:nologicalnan</c>, as R2025b
    /// converts a condition to logical (open item 341); JGS reads truth as it always has.
    /// </summary>
    private bool ConditionTruth(JgsValue condition, Node at)
    {
        if (Dialect.IsMatlab)
        {
            RefuseLogicalConversion(condition, at);
            if (condition.Type != JgsType.Sparse && JgsBuiltins.HasComplexPart(condition))
            {
                throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:nologicalcomplex",
                    "Complex values cannot be converted to logicals.");
            }

            if (JgsBuiltins.McAnyNaN(condition))
            {
                throw JgsBuiltins.NoLogicalNaN(at.Line, at.Column);
            }
        }

        return condition.IsTruthy;
    }

    /// <summary>An evaluated operand of <c>&amp;&amp;</c> or <c>||</c>: R2025b's conversion in the MATLAB dialect (open items 341, 351).</summary>
    private bool ShortCircuitTruth(JgsValue operand, Node at)
    {
        if (Dialect.IsMatlab)
        {
            return MatlabShortCircuitTruth(operand, at);
        }

        return operand.IsTruthy;
    }

    /// <summary>
    /// An operand of <c>&amp;&amp;</c> or <c>||</c> as R2025b converts it (open item 351, ADR 0383), in
    /// this order: a cell, string, struct, function handle, datetime, duration or categorical is
    /// <c>MATLAB:invalidConversion</c>; a complex value, flagged or empty included,
    /// <c>MATLAB:nologicalcomplex</c>; a NaN <c>MATLAB:nologicalnan</c>; anything but one element
    /// (an empty, a row, a two-character char) <c>MATLAB:nonLogicalConditional</c>. A char is its code
    /// (<c>char(0) &amp;&amp; 1</c> is false). An object is left to its own truth.
    /// </summary>
    private static bool MatlabShortCircuitTruth(JgsValue operand, Node at)
    {
        RefuseLogicalConversion(operand, at);
        if (operand.Type is JgsType.Object or JgsType.External)
        {
            return operand.IsTruthy;
        }

        if (operand.Type != JgsType.Sparse && (JgsBuiltins.McAnyComplex(operand) || JgsBuiltins.HasComplexPart(operand)))
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:nologicalcomplex",
                "Complex values cannot be converted to logicals.");
        }

        if (JgsBuiltins.McAnyNaN(operand))
        {
            throw JgsBuiltins.NoLogicalNaN(at.Line, at.Column);
        }

        int[] size = JgsBuiltins.SizeDims(operand);
        if (Array.Exists(size, static d => d != 1))
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:nonLogicalConditional",
                "Operands to the logical AND (&&) and OR (||) operators must be convertible to logical scalar values. "
                + "Use the ANY or ALL functions to reduce operands to logical scalar values.");
        }

        return operand.Type == JgsType.String && !operand.IsCharMatrix
            ? operand.AsString[0] != '\0'
            : operand.IsTruthy;
    }

    /// <summary>
    /// R2025b's refusal of a value no conversion to logical exists for, as a condition or an operand
    /// of <c>&amp;&amp;</c> or <c>||</c> (open item 351, ADR 0383): a cell, string, struct, function
    /// handle, datetime, duration or categorical is <c>MATLAB:invalidConversion</c>.
    /// </summary>
    private static void RefuseLogicalConversion(JgsValue value, Node at)
    {
        string? unconvertible = value.Type switch
        {
            JgsType.Cell => "cell",
            JgsType.Struct => "struct",
            JgsType.Function => "function_handle",
            _ when value.IsStringArray => "string",
            _ when value.IsDatetime => "datetime",
            _ when value.IsDuration => "duration",
            _ when value.IsCategorical => "categorical",
            _ => null,
        };
        if (unconvertible is not null)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:invalidConversion",
                $"Conversion to logical from {unconvertible} is not possible.");
        }
    }
}
