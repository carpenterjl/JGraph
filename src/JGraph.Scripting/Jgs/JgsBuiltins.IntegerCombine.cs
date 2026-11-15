namespace JGraph.Scripting.Jgs;

/// <summary>
/// R2025b's refusals of an integer class paired with what it does not combine with, under every
/// arithmetic operator (open item 333, ADR 0370).
/// </summary>
/// <remarks>
/// <para>
/// Measured in R2025b 25.2 (lane 6C's probes 2, 4 and 6): an integer combines only with its own
/// class or with a scalar double (a scalar char is its code, so one combines too). A logical or a
/// single, scalar or not, another integer class, a non-scalar double and an empty double are all
/// <c>MATLAB:mixedClasses</c> under <c>+ - .* ./ .\ * / \</c>; a complex partner is
/// <c>MATLAB:complexInts</c> first. The matrix operators ask about their shapes before either:
/// <c>*</c> with two non-scalars is <c>MATLAB:mtimes:integerNotSupported</c>, <c>/</c> with a
/// non-scalar divisor and <c>\</c> with a non-scalar left side are
/// <c>MATLAB:mrdivide:integerLinearAlgebra</c> and <c>MATLAB:mldivide:integerLinearAlgebra</c>.
/// </para>
/// <para>
/// The shape questions are asked of any integer operand, two of one class included:
/// <c>int8([1 2; 3 4]) * int8([1 2; 3 4])</c> and <c>int8([]) \ 2</c> are refused too. The
/// comparisons combine anything, as R2025b's do, and are not asked here. The powers have their own
/// refusals in the interpreter (ADR 0365); this one adds a complex partner and a double array.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    internal const string MixedClassesRefusal =
        "Integers can only be combined with integers of the same class, or scalar doubles.";

    /// <summary>
    /// The identifier and message R2025b refuses an integer pair with under <paramref name="op"/>, or
    /// null when the pair combines (or is not this rule's to judge: a sparse, text, time or other
    /// non-numeric operand keeps whatever its own road says).
    /// </summary>
    internal static (string Id, string Message)? IntegerPairRefusal(TokenType op, JgsValue left, JgsValue right)
    {
        JgsNumericClass l = left.NumericClass;
        JgsNumericClass r = right.NumericClass;
        bool li = l.IsInteger();
        bool ri = r.IsInteger();
        if ((!li && !ri) || !IsPlainNumericOperand(left) || !IsPlainNumericOperand(right))
        {
            return null;
        }

        JgsValue other = li ? right : left;
        bool complex = HasComplexPart(left) || HasComplexPart(right);
        if (op is TokenType.Caret or TokenType.DotCaret)
        {
            // The powers word their own class refusals (ADR 0365); a complex partner and a double
            // array beside an integer are this one's.
            return complex ? ("MATLAB:complexInts", "Complex integer arithmetic is not supported.")
                : !(li && ri) && other.NumericClass == JgsNumericClass.Double && !IsLogicalValue(other) && !IsScalarNumber(other)
                    ? ("MATLAB:mixedClasses", MixedClassesRefusal)
                    : null;
        }

        bool leftScalar = IsScalarNumber(left);
        bool rightScalar = IsScalarNumber(right);
        switch (op)
        {
            case TokenType.Star when !leftScalar && !rightScalar:
                return ("MATLAB:mtimes:integerNotSupported",
                    "MTIMES (*) is not fully supported for integer classes. At least one argument must be scalar.");
            case TokenType.Slash when !rightScalar:
                return ("MATLAB:mrdivide:integerLinearAlgebra",
                    "Linear algebra is not supported for integer data types. To operate on each element of the matrix "
                    + "individually, use RDIVIDE (./) for elementwise right division.");
            case TokenType.Backslash when !leftScalar:
                return ("MATLAB:mldivide:integerLinearAlgebra",
                    "Linear algebra is not supported for integer data types. To operate on each element of the matrix "
                    + "individually, use LDIVIDE (.) for elementwise left division."); // R2025b's own text, backslash lost
        }

        bool mixed = (li && ri && l != r)
            || (!(li && ri) && (other.NumericClass == JgsNumericClass.Single || IsLogicalValue(other) || !IsScalarNumber(other)));
        return complex ? ("MATLAB:complexInts", "Complex integer arithmetic is not supported.")
            : mixed ? ("MATLAB:mixedClasses", MixedClassesRefusal)
            : null;
    }

    /// <summary>A number, logical or complex scalar, or a numeric or logical array: no text, time, category or sparse.</summary>
    private static bool IsPlainNumericOperand(JgsValue value) => value.Type switch
    {
        JgsType.Number or JgsType.Bool or JgsType.Complex => true,
        JgsType.Array => !value.IsStringArray && !value.IsTime && !value.IsCategorical && !value.IsCharMatrix,
        _ => false,
    };

    /// <summary>One element: a bare scalar, or a 1-by-1 array.</summary>
    private static bool IsScalarNumber(JgsValue value) =>
        value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex
        || (value.Type == JgsType.Array && value.ArrayLength == 1);
}
