namespace JGraph.Scripting.Jgs;

/// <summary>
/// Arrays of external values of one class (device classes plan, stage D10b, ADR 0194): midimsg. Each
/// operation runs the ordinary numeric code on a proxy of positions (<see cref="ExternalArrays"/>) and
/// maps the positions it comes back with to elements. An array is never changed in place: a write
/// builds the new array and rebinds the variable, so a second holder — a cell, a caller — keeps the
/// value it had, as a MATLAB value class's copy does.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary><c>a(i, …)</c>: the elements the subscripts pick, <c>end</c> and masks included.</summary>
    private JgsValue ExternalArrayRead(IJgsExternalArray array, JgsValue whole, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        if (subscripts.Count == 0)
        {
            return whole;
        }

        JgsValue picked = IndexInto(ExternalArrays.Proxy(array.Rows, array.Columns), subscripts, at, env);
        return JgsValue.External(ExternalArrays.Picked(array, picked, at.Line, at.Column));
    }

    /// <summary>
    /// Whether an index write lands on an array of external values: the variable holds one, or holds
    /// nothing yet and one is being stored into it (<c>q(2) = midimsg</c>, which the write conjures an
    /// empty midimsg array for). Storing one into a numeric array is R2025b's refusal: <c>q = [];
    /// q(1) = midimsg</c> cannot convert (probe: midi_msg, into_empty_double).
    /// </summary>
    private bool IsExternalArrayWrite(Expr container, JgsValue rhs, Node at, JgsEnvironment env, out VariableExpr? named, out JgsValue bound)
    {
        named = null;
        bound = JgsValue.Null;
        if (!Dialect.IsMatlab || at is BraceIndexExpr || container is not VariableExpr variable)
        {
            return false;
        }

        bool has = LookUp(variable.Name, env, out bound);
        bool holds = has && bound.AsExternalOrNull() is IJgsExternalArray;
        if (has && !holds && rhs.AsExternalOrNull() is IJgsExternalArray stored
            && bound.Type is JgsType.Array or JgsType.Number or JgsType.Bool && !bound.IsStringArray)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:UnableToConvert",
                $"Unable to perform assignment because value of type '{stored.ClassName}' is not convertible to '{JgsBuiltins.ClassOf(bound, JgsDialect.Matlab)}'.");
        }

        named = variable;
        return holds || (!has && rhs.AsExternalOrNull() is IJgsExternalArray);
    }

    /// <summary><c>a(i, …) = b</c>, <c>a(i) = []</c> and growth, on an array of external values.</summary>
    private JgsValue ExternalArrayWrite(VariableExpr named, JgsValue bound, IReadOnlyList<Expr> subscripts, TokenType op, JgsValue rhs, Node at, JgsEnvironment env)
    {
        IJgsExternalArray left = bound.AsExternalOrNull() as IJgsExternalArray ?? ((IJgsExternalArray)rhs.AsExternal).Build([], 0, 0);
        if (op != TokenType.Assign)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:UndefinedFunction",
                $"Operator '{op}' is not supported for operands of type '{left.ClassName}'.");
        }

        IJgsExternalArray? right = null;
        JgsValue rhsProxy;
        if (ExternalArrays.IsEmptyDouble(rhs))
        {
            rhsProxy = rhs; // a(i) = [] deletes
        }
        else
        {
            right = rhs.AsExternalOrNull() is IJgsExternalArray same && same.ClassName == left.ClassName
                ? same : left.Coerce(rhs, at.Line, at.Column);
            rhsProxy = ExternalArrays.Proxy(right.Rows, right.Columns, ExternalArrays.Count(left));
        }

        Rebind(named.Name, ExternalArrays.Proxy(left.Rows, left.Columns), env);
        try
        {
            IndexWrite(named, subscripts, op, rhsProxy, at, env);
        }
        catch
        {
            Rebind(named.Name, bound, env);
            throw;
        }

        LookUp(named.Name, env, out JgsValue written);
        Rebind(named.Name, JgsValue.External(ExternalArrays.Written(left, right, written, at.Line, at.Column)), env);
        return rhs;
    }

    /// <summary>
    /// <c>a.name = v</c>, <c>a(k).name = v</c> (growing the array when <c>k</c> is past its end) and
    /// <c>c{k}.name = v</c> or <c>s.m.name = v</c> on an array of external values: the one element named
    /// is rebuilt with the property written, and what held the array is written back.
    /// </summary>
    private bool TryAssignToExternalElement(MemberExpr member, JgsValue value, JgsEnvironment env)
    {
        if (!Dialect.IsMatlab)
        {
            return false;
        }

        IReadOnlyList<Expr>? subscripts = null;
        Node pickAt = member;
        VariableExpr? holder = member.Target as VariableExpr;
        switch (member.Target)
        {
            case CallExpr { Callee: VariableExpr callee } call:
                (holder, subscripts, pickAt) = (callee, call.Arguments, call);
                break;
            case IndexExpr { Target: VariableExpr indexed } index:
                (holder, subscripts, pickAt) = (indexed, index.Indices, index);
                break;
            case BraceIndexExpr or MemberExpr:
                return TryAssignToHeldExternal(member, value, env);
        }

        if (holder is null || !LookUp(holder.Name, env, out JgsValue held) || held.AsExternalOrNull() is not IJgsExternalArray array)
        {
            return false;
        }

        int[] positions;
        if (subscripts is null)
        {
            positions = [.. Enumerable.Range(1, ExternalArrays.Count(array))];
        }
        else
        {
            JgsValue picked;
            try
            {
                picked = IndexInto(ExternalArrays.Proxy(array.Rows, array.Columns), subscripts, pickAt, env);
            }
            catch (JgsRuntimeException)
            {
                // z(3).Timestamp = 4 past the end grows z first, as a store of a default element there would.
                ExternalArrayWrite(holder, held, subscripts, TokenType.Assign, JgsValue.External(array.Build([null], 1, 1)), pickAt, env);
                LookUp(holder.Name, env, out held);
                array = (IJgsExternalArray)held.AsExternal;
                picked = IndexInto(ExternalArrays.Proxy(array.Rows, array.Columns), subscripts, pickAt, env);
            }

            positions = ExternalArrays.ReadProxy(picked, pickAt.Line, pickAt.Column).Positions;
        }

        IJgsExternalArray changed = WithOneMember(array, positions, member, value, env);
        Rebind(holder.Name, JgsValue.External(changed), env);
        return true;
    }

    /// <summary><c>c{k}.name = v</c>, <c>s.m.name = v</c>: the array read from where it is held, rebuilt, and written back there.</summary>
    private bool TryAssignToHeldExternal(MemberExpr member, JgsValue value, JgsEnvironment env)
    {
        JgsValue current;
        try
        {
            current = Evaluate(member.Target, env);
        }
        catch (JgsRuntimeException)
        {
            return false; // nothing there yet: the ordinary road creates it
        }

        if (current.AsExternalOrNull() is not IJgsExternalArray array)
        {
            return false;
        }

        IJgsExternalArray changed = WithOneMember(array, [.. Enumerable.Range(1, ExternalArrays.Count(array))], member, value, env);
        JgsValue stored = JgsValue.External(changed);
        _ = member.Target is BraceIndexExpr brace ? AssignToBraceIndex(brace, stored, env) : AssignToMember((MemberExpr)member.Target, stored, env);
        return true;
    }

    private IJgsExternalArray WithOneMember(IJgsExternalArray array, int[] positions, MemberExpr member, JgsValue value, JgsEnvironment env)
    {
        if (positions.Length != 1)
        {
            throw new JgsRuntimeException(member.Line, member.Column, "MATLAB:index:expected_one_output_for_assignment",
                $"Assigning to {positions.Length} elements using a simple assignment statement is not supported. Consider using comma-separated list assignment.");
        }

        return array.WithMember(positions[0] - 1, FieldName(member, env), value, member.Line, member.Column);
    }

    /// <summary>
    /// <c>a.name</c> where one value is wanted: the first element's, every element being asked (so a
    /// name one of them refuses is refused), as <c>v = a.Timestamp</c> takes the first of the list in
    /// R2025b (probe_midi_cs). An empty array has none to give.
    /// </summary>
    private static JgsValue ExternalArrayMember(IJgsExternalArray array, string field, MemberExpr member)
    {
        int count = ExternalArrays.Count(array);
        if (count == 0)
        {
            throw new JgsRuntimeException(member.Line, member.Column, "MATLAB:needMoreRhsOutputs",
                "Insufficient number of outputs from right hand side of equal sign to satisfy assignment.");
        }

        JgsValue first = array.GetMember(0, field, member.Line, member.Column);
        for (int i = 1; i < count; i++)
        {
            array.GetMember(i, field, member.Line, member.Column);
        }

        return first;
    }

    /// <summary>
    /// <c>a.name</c> where a list may go — an argument list, a bracket: one value an element, as
    /// <c>[msgs.Timestamp]</c> reads it. Null when the path does not reach an array of external values.
    /// </summary>
    private JgsValue[]? ExternalArraySpread(MemberExpr member, JgsEnvironment env)
    {
        if (RootName(member.Target) is not { } root || !LookUp(root, env, out JgsValue holder) || holder.AsExternalOrNull() is not IJgsExternalArray)
        {
            return null;
        }

        JgsValue owner = Evaluate(member.Target, env);
        string field = FieldName(member, env);
        if (owner.AsExternalOrNull() is IJgsExternalArray array && ExternalArrays.Count(array) != 1)
        {
            var values = new JgsValue[ExternalArrays.Count(array)];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = array.GetMember(i, field, member.Line, member.Column);
            }

            return values;
        }

        return [MemberOf(owner, field, member, autoCall: true)];
    }

    /// <summary>A bracket whose pieces include an array of external values joins as that class; null otherwise.</summary>
    private JgsValue? JoinExternalArrays(IReadOnlyList<JgsValue[]> rows, Node at)
    {
        if (!Dialect.IsMatlab)
        {
            return null;
        }

        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue piece in row)
            {
                if (piece.AsExternalOrNull() is IJgsExternalArray kind)
                {
                    return JgsValue.External(ExternalArrays.Join(kind, rows, at.Line, at.Column));
                }
            }
        }

        return null;
    }

    /// <summary><c>for x = a</c> over an array of external values: a column of it each pass, as over a matrix.</summary>
    private Completion ExecuteForOverExternalArray(ForStmt statement, IJgsExternalArray array, JgsEnvironment env)
    {
        if (ExternalArrays.Count(array) == 0)
        {
            return BindZeroTripVariable(statement, env);
        }

        for (int column = 0; column < array.Columns; column++)
        {
            var elements = new (IJgsExternalArray, int)?[array.Rows];
            for (int r = 0; r < array.Rows; r++)
            {
                elements[r] = (array, (column * array.Rows) + r);
            }

            Tick();
            JgsEnvironment local = BlockScope(env);
            local.Declare(statement.Variable, JgsValue.External(array.Build(elements, array.Rows, 1)));
            Completion completion = ExecuteBlock(statement.Body, local);
            if (completion.Kind == CompletionKind.Break)
            {
                break;
            }

            if (completion.Kind == CompletionKind.Return)
            {
                return completion;
            }
        }

        return Completion.Normal;
    }
}
