namespace JGraph.Scripting.Jgs;

/// <summary>
/// The colon's class rules in the MATLAB dialect (open item 340, ADR 0379).
/// </summary>
/// <remarks>
/// Measured in R2025b 25.2 (lane 7C's probes 1 and 4, every pair of eighteen operands and the
/// three-operand forms): the operands that are not double (a logical, a single, each integer class,
/// empties included) must all be of one class, or the colon is
/// <c>MATLAB:colon:mixedNonDoubleOperands</c>; that is asked after a single or integer array in
/// front is refused as <c>MATLAB:colon:operandsNotRealScalar</c> and before any other operand's
/// shape, and a complex operand (the zero-imaginary flag included) is
/// <c>operandsNotRealScalar</c> after it. Beside a char last operand a single or integer one is
/// the same mixing; a char first operand with a step that is not char or double is
/// <c>MATLAB:colon:mixedCharOperands</c>. An empty single or integer operand gives the empty range
/// its class (<c>int8([]):3</c> is an int8 1-by-0).
/// </remarks>
internal sealed partial class Interpreter
{
    /// <summary>Refuses the MATLAB colon's class mixtures before the bounds are read.</summary>
    private static void RefuseColonClassMix(RangeExpr range, JgsValue start, JgsValue step, JgsValue stop, bool stepGiven)
    {
        JgsValue[] operands = stepGiven ? [start, step, stop] : [start, stop];
        if (Array.Exists(operands, static v => v.IsStringArray))
        {
            return; // the char road refuses a string in its own words
        }

        JgsRuntimeException Refusal(string identifier, string message) => new(range.Line, range.Column, identifier, message);
        JgsRuntimeException Mixed() => Refusal("MATLAB:colon:mixedNonDoubleOperands",
            "Colon operands must be all the same type, or mixed with real scalar doubles.");
        JgsRuntimeException NotScalar() => Refusal("MATLAB:colon:operandsNotRealScalar", "Colon operands must be real scalars.");
        static bool SingleOrInteger(JgsValue v) => !IsColonText(v) && (v.NumericClass == JgsNumericClass.Single || v.NumericClass.IsInteger());
        static bool ArrayInFront(JgsValue v) => SingleOrInteger(v) && v.Type == JgsType.Array && v.ArrayLength > 1;

        if (IsColonText(start))
        {
            if (stepGiven && !IsColonText(step) && (SingleOrInteger(step) || JgsBuiltins.IsLogicalValue(step)))
            {
                throw Refusal("MATLAB:colon:mixedCharOperands",
                    "For colon operator with char operands, second operand must be char or real scalar double.");
            }

            return;
        }

        if (ArrayInFront(start))
        {
            throw NotScalar();
        }

        if (Array.Exists(operands, IsColonText))
        {
            if (Array.Exists(operands, SingleOrInteger))
            {
                throw Mixed();
            }

            return;
        }

        string? kind = null;
        foreach (JgsValue operand in operands)
        {
            string? own = JgsBuiltins.IsLogicalValue(operand) ? "logical"
                : SingleOrInteger(operand) ? operand.NumericClass.ToString()
                : null;
            if (own is null)
            {
                continue;
            }

            if (kind is not null && kind != own)
            {
                throw Mixed();
            }

            kind = own;
        }

        if (Array.Exists(operands, JgsBuiltins.McAnyComplex))
        {
            throw NotScalar();
        }
    }

    /// <summary>The class an empty single or integer operand gives an otherwise double colon, or double.</summary>
    private static JgsNumericClass ColonEmptyOperandClass(JgsValue start, JgsValue step, JgsValue stop)
    {
        foreach (JgsValue operand in (ReadOnlySpan<JgsValue>)[start, step, stop])
        {
            if (operand.Type == JgsType.Array && operand.ArrayLength == 0 && !operand.IsStringArray
                && operand.NumericClass != JgsNumericClass.Double)
            {
                return operand.NumericClass;
            }
        }

        return JgsNumericClass.Double;
    }
}
