namespace JGraph.Scripting.Jgs;

/// <summary>
/// Leftovers of the indexing work ADR 0354 met in its probe (open item 320, ADR 0366): the colon over
/// characters.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>Whether a colon operand is text: a char row, a char matrix, the 1-by-0 char or a string.</summary>
    private static bool IsColonText(JgsValue value) =>
        value.Type == JgsType.String || value.IsCharMatrix || value.IsStringArray || JgsBuiltins.IsEmptyCharRow(value);

    /// <summary>
    /// A colon with a text operand, in the MATLAB dialect (open item 320, measured): <c>'a':'c'</c> is the
    /// char row <c>'abc'</c>, <c>'a':2:'e'</c> is <c>'ace'</c>, a descending pair the 1-by-0 char. Both ends
    /// must be char (<c>MATLAB:colon:mixedCharOperands</c>), each one character
    /// (<c>MATLAB:colon:operandsNotRealScalar</c>), a double step a whole number
    /// (<c>MATLAB:colon:scalarDoubleMustBeIntegerValued</c>); a string is
    /// <c>MATLAB:colon:inputsMustBeNumericCharLogical</c>.
    /// </summary>
    private static JgsValue CharRange(RangeExpr range, JgsValue startValue, JgsValue stepValue, JgsValue stopValue)
    {
        JgsRuntimeException Refusal(string identifier, string message) => new(range.Line, range.Column, identifier, message);
        if (startValue.IsStringArray || stopValue.IsStringArray || stepValue.IsStringArray)
        {
            throw Refusal("MATLAB:colon:inputsMustBeNumericCharLogical", "Inputs must be numeric, char, or logical.");
        }

        static bool IsChar(JgsValue value) => value.Type == JgsType.String || value.IsCharMatrix || JgsBuiltins.IsEmptyCharRow(value);
        if (!IsChar(startValue) || !IsChar(stopValue))
        {
            throw Refusal("MATLAB:colon:mixedCharOperands", "For colon operator with char operands, first and last operands must be char.");
        }

        string start = JgsBuiltins.IsEmptyCharRow(startValue) ? string.Empty : startValue.IsCharMatrix ? "??" : startValue.AsString;
        string stop = JgsBuiltins.IsEmptyCharRow(stopValue) ? string.Empty : stopValue.IsCharMatrix ? "??" : stopValue.AsString;
        if (start.Length > 1 || stop.Length > 1)
        {
            throw Refusal("MATLAB:colon:operandsNotRealScalar", "Colon operands must be real scalars.");
        }

        double step;
        if (IsChar(stepValue))
        {
            string text = JgsBuiltins.IsEmptyCharRow(stepValue) ? string.Empty : stepValue.IsCharMatrix ? "??" : stepValue.AsString;
            if (text.Length > 1)
            {
                throw Refusal("MATLAB:colon:operandsNotRealScalar", "Colon operands must be real scalars.");
            }

            step = text.Length == 0 ? double.NaN : text[0];
        }
        else
        {
            JgsNumericClass ignored = JgsNumericClass.Double;
            step = RangeBoundValue(stepValue, range.Step ?? range.Start, "step", ref ignored);
            if (double.IsFinite(step) && step != Math.Floor(step))
            {
                throw Refusal("MATLAB:colon:scalarDoubleMustBeIntegerValued", "Double operands interacting with char operands must have integer values.");
            }
        }

        if (start.Length == 0 || stop.Length == 0 || double.IsNaN(step) || step == 0)
        {
            return JgsValue.CharMatrix([string.Empty]);
        }

        long count = RangeCountOf(start[0], step, stop[0]);
        if (count <= 0)
        {
            return JgsValue.CharMatrix([string.Empty]);
        }

        var text2 = new char[count];
        for (long i = 0; i < count; i++)
        {
            text2[i] = (char)(start[0] + (i * step));
        }

        return JgsValue.Str(new string(text2));
    }

    /// <summary>
    /// <c>x(…) = s</c> with a plain struct on the right and a variable that already holds data - a number,
    /// a logical, a char, a cell, the empty <c>[]</c> among them - is R2025b's <c>MATLAB:invalidConversion</c>
    /// naming the variable's class (open item 320, measured: <c>x = []; x(2, 2) = struct('a', 1)</c> is
    /// "Conversion to double from struct is not possible.", on every subscript road). A variable the write
    /// creates starts a struct array, as it did. A field (<c>s.f(2) = t</c>) and a cell's slot named by a
    /// number (<c>c{1}(2) = t</c>) holding data are refused alike (measured); other containers are not
    /// peeked, so their subscripts are never evaluated twice.
    /// </summary>
    private void RefuseStructIntoData(Expr container, TokenType op, JgsValue rhs, Node at, JgsEnvironment env)
    {
        if (!Dialect.IsMatlab || op != TokenType.Assign
            || rhs.Type != JgsType.Struct || rhs.ClassName is not null || JgsBuiltins.IsKeyedCollection(rhs) || JgsBuiltins.IsInterpolant(rhs)
            || PeekWriteContainer(container, env) is not { } held)
        {
            return;
        }

        bool data = !held.IsStringArray && !held.IsTime && !held.IsCategorical && held.ClassName is null
            && (held.Type is JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.String or JgsType.Cell or JgsType.Sparse
                || (held.Type == JgsType.Array && held.AsExternalOrNull() is null));
        if (data)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:invalidConversion",
                $"Conversion to {JgsBuiltins.ClassOf(held, Dialect)} from struct is not possible.");
        }
    }

    /// <summary>
    /// What a write's container holds now, read without evaluating anything: a variable, a literal field of
    /// a scalar struct so reached, or a cell's slot named by one literal number; null otherwise or when absent.
    /// </summary>
    private JgsValue? PeekWriteContainer(Expr container, JgsEnvironment env)
    {
        switch (container)
        {
            case VariableExpr variable:
                return LookUp(variable.Name, env, out JgsValue held) ? held : null;
            case MemberExpr { Field: { } field } member:
                return PeekWriteContainer(member.Target, env) is { Type: JgsType.Struct, ClassName: null, IsStructArray: false } owner
                    && owner.AsStruct.TryGetValue(field, out JgsValue? child) ? child : null;
            case BraceIndexExpr { Indices: [NumberLiteral { IntegerClass: null } slot] } brace:
                return PeekWriteContainer(brace.Target, env) is { Type: JgsType.Cell } cell
                    && slot.Value == Math.Floor(slot.Value) && slot.Value >= 1 && slot.Value <= cell.AsCell.Length
                    ? cell.AsCell[(int)slot.Value - 1] : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// An empty cell or char a field write turns into a struct (MATLAB dialect, open item 320, measured:
    /// <c>x = {}; x.f = 1</c>, <c>st.t = ''; st.t.f = 1</c>), as it does the empty <c>[]</c>.
    /// </summary>
    private bool IsEmptyDotHolder(JgsValue held) =>
        Dialect.IsMatlab && !held.IsStringArray
        && ((held.Type == JgsType.Cell && held.AsCell.Length == 0) || (held.Type == JgsType.String && held.AsString.Length == 0));

    /// <summary>
    /// A field write onto a string, in R2025b's words (open item 320, measured): the write's own field is no
    /// property of the class (<c>MATLAB:noPublicFieldForClass</c>), a field read on the way to a deeper
    /// write no method, property or field (<c>MATLAB:noSuchMethodOrField</c>).
    /// </summary>
    private static JgsRuntimeException StringFieldWriteRefusal(string? field, bool last, Node at) =>
        field is null ? new(at.Line, at.Column, "MATLAB:structAssToNonStruct",
                "Unable to perform assignment because dot indexing is not supported for variables of this type.")
        : last ? new(at.Line, at.Column, "MATLAB:noPublicFieldForClass", $"Unrecognized property '{field}' for class 'string'.")
        : new(at.Line, at.Column, "MATLAB:noSuchMethodOrField", $"Unrecognized method, property, or field '{field}' for class 'string'.");

    /// <summary>
    /// <c>c{i, j, k} = v</c> in the MATLAB dialect names exactly one place, or it is R2025b's
    /// <c>MATLAB:index:expected_one_output_for_assignment</c> naming how many (open item 320, measured:
    /// <c>c{:, 1, 1} = 5</c> on a 2-by-2-by-2 is "Assigning to 2 elements", <c>c{[], 1, 1}</c> to 0). A
    /// place past an edge counts as the one it grows to.
    /// </summary>
    private void RequireOneBracePlace(JgsValue target, BraceIndexExpr brace, JgsEnvironment env)
    {
        if (!Dialect.IsMatlab)
        {
            return;
        }

        int[] extents = SubscriptExtents(target.Dims, brace.Indices.Count);
        long count = 1;
        for (int i = 0; i < extents.Length; i++)
        {
            count *= WritePicks(EvaluateIndexArgument(brace.Indices[i], extents, i, env), extents[i], brace, i + 1).Length;
        }

        if (count != 1)
        {
            throw AssigningToSeveral((int)Math.Min(count, int.MaxValue), brace);
        }
    }
}
