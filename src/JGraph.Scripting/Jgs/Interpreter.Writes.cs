namespace JGraph.Scripting.Jgs;

/// <summary>
/// V3b (ADR 0164): the write roads. The order of a write's parts by the target's shape (M16), each
/// subscript evaluated once (M15), a right-hand side or subscript that is the target's own storage
/// (M10), a refused write that changes nothing (M14), and the write-back every rebuilding write
/// ends on, whatever the target's shape.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order (M16).</b> R2025b runs a paren index directly on a variable — <c>x(i) = rhs</c>,
/// <c>x(i, j) = rhs</c>, a dictionary's <c>d(k) = rhs</c> — right-hand side first, then the
/// subscripts, then reads the target. Every other shape runs its subscripts and dynamic names
/// first, left to right down the path, then the right-hand side, then reads the target; an
/// <c>end</c> is taken against the container as it is at that moment. Only a part that can run
/// script code can tell the orders apart, so the parts are pre-evaluated into
/// <see cref="PreEvaluated"/> nodes (<see cref="PrepareTarget"/>) only when one of them is not
/// inert (<see cref="IsInert"/>); the roads then evaluate nothing twice (M15) and resolve the
/// target through its entry after every part has run, never from a wrapper captured before.
/// </para>
/// <para>
/// <b>Cleared globals (#158).</b> A global a subscript or right-hand side cleared is a name still
/// declared <c>global</c> in this frame that the global workspace no longer holds: reading it —
/// for <c>end</c>, or anywhere — is R2025b's "Reference to a cleared variable", and writing it
/// creates it afresh (measured: a global another function re-created in between is written).
/// </para>
/// <para>
/// <b>Overlap (M10).</b> When the right-hand side (or a subscript) is the target's own payload, the
/// write holds a counted share of it for its duration, so the target's first write detaches (M3)
/// and the right-hand side goes on reading what it read. The share is given back in the write's
/// <c>finally</c> like a scope's (M5).
/// </para>
/// <para>
/// <b>Refusal (M14).</b> Every road validates the subscripts, the selection's count against the
/// right-hand side and the conversion before it grows, promotes or demotes the target, so a caught
/// refusal leaves the target as it was. A write mask names the positions of its true entries and
/// may be any length; its extent is its highest true position.
/// </para>
/// </remarks>
internal sealed partial class Interpreter
{
    // ---- M16: the target's shape decides the order --------------------------------------------

    /// <summary>
    /// Whether a target is a paren index directly on a variable — the one shape whose right-hand
    /// side runs before its subscripts.
    /// </summary>
    private static bool IsParenOnVariable(Expr target) =>
        target is CallExpr { Callee: VariableExpr } or IndexExpr { Target: VariableExpr };

    /// <summary>
    /// Whether every subscript and dynamic name along a write target is inert (see
    /// <see cref="IsInert"/>): then the order of the write's parts cannot be observed and the roads
    /// evaluate them where they always did.
    /// </summary>
    private bool TargetIsInert(Expr target, JgsEnvironment env)
    {
        while (true)
        {
            switch (target)
            {
                case MemberExpr member:
                    if (member.Field is null && !IsInert(member.FieldName!, env))
                    {
                        return false;
                    }

                    target = member.Target;
                    continue;
                case BraceIndexExpr brace:
                    if (!AllInert(brace.Indices, env))
                    {
                        return false;
                    }

                    target = brace.Target;
                    continue;
                case CallExpr call:
                    if (!AllInert(call.Arguments, env) || EndReadsThroughObject(call.Callee, call.Arguments, env))
                    {
                        return false;
                    }

                    target = call.Callee;
                    continue;
                case IndexExpr index:
                    if (!AllInert(index.Indices, env) || EndReadsThroughObject(index.Target, index.Indices, env))
                    {
                        return false;
                    }

                    target = index.Target;
                    continue;
                default:
                    return true;
            }
        }
    }

    /// <summary>
    /// Whether an <c>end</c> among <paramref name="subscripts"/> reads its container through an
    /// object's property, whose get method may run script code (V6, #147): <c>o.q(end) = 8</c> is
    /// then not inert, and its parts run first, the <c>end</c> calling the getter afresh.
    /// </summary>
    private bool EndReadsThroughObject(Expr container, IReadOnlyList<Expr> subscripts, JgsEnvironment env)
    {
        if (!AnyClasses || container is VariableExpr || !AnyMentionsEnd(subscripts))
        {
            return false;
        }

        Expr root = container;
        while (InnerOf(root) is { } inner)
        {
            root = inner;
        }

        return root is VariableExpr name && LookUp(name.Name, env, out JgsValue bound) && bound.Type == JgsType.Object;
    }

    /// <summary>
    /// The target with every subscript and dynamic field name along it evaluated, left to right
    /// from the root, each replaced by a <see cref="PreEvaluated"/> holding its value (a lone
    /// <c>:</c> stays); <c>end</c> is taken against the container as it is when that subscript runs.
    /// Nodes with nothing to evaluate are handed back as they are.
    /// </summary>
    private Expr PrepareTarget(Expr target, JgsEnvironment env)
    {
        switch (target)
        {
            case MemberExpr member:
            {
                Expr inner = PrepareTarget(member.Target, env);
                if (member.Field is not null)
                {
                    return ReferenceEquals(inner, member.Target)
                        ? member
                        : new MemberExpr(inner, member.Field, null) { Line = member.Line, Column = member.Column };
                }

                return new MemberExpr(inner, FieldName(member, env), null) { Line = member.Line, Column = member.Column };
            }

            case BraceIndexExpr brace:
            {
                Expr inner = PrepareTarget(brace.Target, env);
                return new BraceIndexExpr(inner, PreEvaluateSubscripts(inner, brace.Indices, env))
                {
                    Line = brace.Line,
                    Column = brace.Column,
                };
            }

            case CallExpr call:
            {
                Expr inner = PrepareTarget(call.Callee, env);
                return new CallExpr(inner, PreEvaluateSubscripts(inner, call.Arguments, env))
                {
                    Line = call.Line,
                    Column = call.Column,
                };
            }

            case IndexExpr index:
            {
                Expr inner = PrepareTarget(index.Target, env);
                return new IndexExpr(inner, PreEvaluateSubscripts(inner, index.Indices, env))
                {
                    Line = index.Line,
                    Column = index.Column,
                };
            }

            default:
                return target;
        }
    }

    /// <summary>
    /// One level's subscripts, each evaluated exactly once (M15) with <c>end</c> bound to the
    /// container's extents as they are now; the container is read only when a subscript mentions
    /// <c>end</c>, and a container that does not exist yet has every extent zero.
    /// </summary>
    private IReadOnlyList<Expr> PreEvaluateSubscripts(Expr container, IReadOnlyList<Expr> subscripts, JgsEnvironment env)
    {
        if (subscripts.Count == 0)
        {
            return subscripts;
        }

        // The container is read where an end stands, not before the subscript starts: a subscript
        // that runs script code before its end runs it first (o.p(idx_l():end) on a property with
        // a get method is idx;get in R2025b, #147), and every subscript that mentions end reads
        // the container afresh.
        int count = subscripts.Count;
        int[] none = new int[count];
        var prepared = new Expr[count];
        for (int i = 0; i < prepared.Length; i++)
        {
            Expr subscript = subscripts[i];
            if (subscript is AllExpr or PreEvaluated)
            {
                prepared[i] = subscript;
                continue;
            }

            JgsValue value = MentionsEnd(subscript)
                ? EvaluateIndexArgument(subscript, () => WriteExtents(container, count, env), i, env)!
                : EvaluateIndexArgument(subscript, none, i, env)!;
            prepared[i] = new PreEvaluated(value) { Line = subscript.Line, Column = subscript.Column };
        }

        return prepared;
    }

    /// <summary>Whether any subscript mentions <c>end</c> (see <see cref="MentionsEnd"/>).</summary>
    private static bool AnyMentionsEnd(IReadOnlyList<Expr> subscripts)
    {
        for (int i = 0; i < subscripts.Count; i++)
        {
            if (MentionsEnd(subscripts[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether evaluating a subscript can read <c>end</c> — anywhere in it, an argument of a call
    /// included, since <c>x(f(end))</c> binds that <c>end</c> to <c>x</c>. Cached on the node.
    /// </summary>
    private static bool MentionsEnd(Expr subscript)
    {
        if (subscript.EndUse == 0)
        {
            subscript.EndUse = EndShape(subscript) ? Expr.ReadsEnd : Expr.NoEnd;
        }

        return subscript.EndUse == Expr.ReadsEnd;
    }

    private static bool EndShape(Expr expr) => expr switch
    {
        EndExpr => true,
        UnaryExpr unary => EndShape(unary.Operand),
        TransposeExpr transpose => EndShape(transpose.Operand),
        BinaryExpr binary => EndShape(binary.Left) || EndShape(binary.Right),
        LogicalExpr logical => EndShape(logical.Left) || EndShape(logical.Right),
        RangeExpr range => EndShape(range.Start) || (range.Step is not null && EndShape(range.Step)) || EndShape(range.Stop),
        ArrayLiteral array => array.Elements.Any(EndShape),
        MatrixLiteral matrix => matrix.Rows.Any(row => row.Any(EndShape)),
        CellLiteral cell => cell.Rows.Any(row => row.Any(EndShape)),
        CallExpr call => EndShape(call.Callee) || call.Arguments.Any(EndShape),
        IndexExpr index => EndShape(index.Target) || index.Indices.Any(EndShape),
        BraceIndexExpr brace => EndShape(brace.Target) || brace.Indices.Any(EndShape),
        MemberExpr member => EndShape(member.Target) || (member.FieldName is not null && EndShape(member.FieldName)),
        _ => false,
    };

    /// <summary>
    /// The extents <c>end</c> reads for a write with <paramref name="count"/> subscripts on
    /// <paramref name="container"/>: a container that does not exist yet has every extent zero.
    /// </summary>
    private int[] WriteExtents(Expr container, int count, JgsEnvironment env)
    {
        if (!TryReadContainer(container, env, out JgsValue held))
        {
            return new int[count];
        }

        if (held.Type == JgsType.Table && count == 2)
        {
            return [held.AsTable.RowCount, held.AsTable.ColumnCount]; // T{end, end} = v (V6)
        }

        if (held.Type == JgsType.Sparse && count <= 2)
        {
            var sparse = held.AsSparse; // S(end, end) = v, S(end) = v (V6)
            return count == 1 ? [checked(sparse.Rows * sparse.Cols)] : [sparse.Rows, sparse.Cols];
        }

        if (count == 1)
        {
            return [LinearCount(held)];
        }

        if (held.Type == JgsType.Array)
        {
            return SubscriptExtents(JgsMatrix.DimsOf(held), count);
        }

        var extents = new int[count];
        Array.Fill(extents, 1);
        switch (held.Type)
        {
            case JgsType.String:
                extents[1] = held.AsString.Length;
                break;
            case JgsType.Cell or JgsType.Struct:
                extents[0] = held.Rows;
                extents[1] = held.Cols;
                break;
        }

        return extents;
    }

    /// <summary>How many elements a linear subscript counts across a container.</summary>
    private static int LinearCount(JgsValue held) => held.Type switch
    {
        JgsType.Array => held.ArrayLength,
        JgsType.Cell => held.AsCell.Length,
        JgsType.Struct => held.AsStructArray.Length,
        JgsType.String => held.AsString.Length,
        JgsType.Number or JgsType.Bool or JgsType.Complex => 1,
        _ => 0,
    };

    /// <summary>
    /// The read <c>end</c> makes of a write's container. False when the container does not exist
    /// yet — a name never bound, a field the write will create — which counts as empty; a cleared
    /// global refuses (#158), and a dot or brace on a value that has neither refuses in R2025b's
    /// words, because the write would have created the field where the read cannot.
    /// </summary>
    private bool TryReadContainer(Expr container, JgsEnvironment env, out JgsValue value)
    {
        switch (container)
        {
            case VariableExpr variable:
                if (LookUp(variable.Name, env, out value))
                {
                    return true;
                }

                if (env.IsGlobal(variable.Name))
                {
                    throw ClearedVariable(variable.Name, variable);
                }

                return false;

            case MemberExpr { Field: { } field } member:
            {
                if (!TryReadContainer(member.Target, env, out JgsValue owner))
                {
                    value = JgsValue.Null;
                    return false;
                }

                if (owner.Type == JgsType.Struct && !owner.IsStructArray)
                {
                    if (owner.AsStruct.TryGetValue(field, out JgsValue? held))
                    {
                        value = held;
                        return true;
                    }

                    value = JgsValue.Null;
                    return false;
                }

                if (Dialect.IsMatlab && owner.Type is JgsType.Array or JgsType.Cell or JgsType.String
                        or JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.Null
                    && !JgsHandleRegistry.TryGet(owner, out _))
                {
                    throw new JgsRuntimeException(member.Line, member.Column,
                        "Dot indexing is not supported for variables of this type.");
                }

                value = MemberOf(owner, field, member, autoCall: true);
                return true;
            }

            case BraceIndexExpr brace:
            {
                if (!TryReadContainer(brace.Target, env, out JgsValue cell))
                {
                    value = JgsValue.Null;
                    return false;
                }

                if (Dialect.IsMatlab && cell.Type is not (JgsType.Cell or JgsType.Table) && !cell.IsStringArray
                    && cell.ClassName != JgsBuiltins.DictionaryClassName)
                {
                    throw new JgsRuntimeException(brace.Line, brace.Column,
                        "Brace indexing is not supported for variables of this type.");
                }

                value = BraceRead(cell, brace, env);
                return true;
            }

            default:
                value = Evaluate(container, env);
                return true;
        }
    }

    /// <summary>R2025b's refusal for a global this frame declared that <c>clear global</c> has since removed.</summary>
    private static JgsRuntimeException ClearedVariable(string name, Node at) =>
        new(at.Line, at.Column, $"Reference to a cleared variable {name}.");

    /// <summary>
    /// <c>clear global</c>: drops the named globals — every one when none is named — from the
    /// global workspace. A frame that declared one keeps its declaration, which is what makes the
    /// name a cleared variable there rather than an unknown one.
    /// </summary>
    internal void ClearGlobals(IReadOnlyList<string> names)
    {
        IEnumerable<string> cleared = names.Count == 0 ? _globalWorkspace.Locals.Keys.ToList() : names;
        foreach (string name in cleared)
        {
            _globalWorkspace.Forget(name, EmptyPristine);
        }
    }

    // ---- conjuring: the container a write creates ----------------------------------------------

    /// <summary>
    /// The empty a write past the end of nothing starts from: a string array for a string, a cell
    /// for a cell, the shapeless <c>[]</c> otherwise (measured: <c>x(1) = "a"</c> with no
    /// <c>x</c> is a string array where <c>x = []; x(1) = "a"</c> is a double holding NaN).
    /// </summary>
    private static JgsValue EmptyOfKind(JgsValue rhs) => rhs.IsStringArray
        ? JgsValue.StringArray(System.Array.Empty<JgsValue>(), 0, 0)
        : rhs.Type == JgsType.Cell
            ? JgsValue.Cell(System.Array.Empty<JgsValue>())
            : JgsMatrix.FromElements(System.Array.Empty<JgsValue>(), 0, 0);

    /// <summary>
    /// Whether a field write target names a field its struct does not hold yet — or a struct that
    /// does not exist yet — so that <c>s.f(3) = 9</c> creates <c>s.f</c> before indexing into it.
    /// Only the shapes a peek can settle without evaluating anything twice answer true.
    /// </summary>
    private bool IsAbsentField(MemberExpr member, JgsEnvironment env)
    {
        if (member.Field is null)
        {
            return false;
        }

        return member.Target switch
        {
            VariableExpr variable => !LookUp(variable.Name, env, out JgsValue held)
                || LacksField(held, member.Field),
            MemberExpr inner => IsAbsentField(inner, env)
                || (TryPeekField(inner, env, out JgsValue held) && LacksField(held, member.Field)),
            _ => false,
        };
    }

    /// <summary>Whether a value a field write lands in has no such field yet: a struct without it, or the [] a struct starts from.</summary>
    private static bool LacksField(JgsValue owner, string field) =>
        (owner.Type == JgsType.Struct && !owner.IsStructArray && !owner.AsStruct.ContainsKey(field))
        || (owner.Type == JgsType.Array && owner.ArrayLength == 0 && !owner.IsStringArray);

    /// <summary>
    /// Whether a brace-slot write target names a slot its cell does not hold yet — or the <c>[]</c>
    /// a cell starts from — so that <c>c{3}(2) = 9</c> grows <c>c</c> before indexing into the slot.
    /// Only a slot named by a value already in hand is settled, so nothing is evaluated twice.
    /// </summary>
    private bool IsAbsentSlot(BraceIndexExpr brace, JgsEnvironment env)
    {
        if (brace.Indices.Count != 1)
        {
            return false;
        }

        JgsValue owner;
        switch (brace.Target)
        {
            case VariableExpr variable:
                if (!LookUp(variable.Name, env, out owner))
                {
                    return true;
                }

                break;
            case MemberExpr inner:
                if (IsAbsentField(inner, env))
                {
                    return true;
                }

                if (!TryPeekField(inner, env, out owner))
                {
                    return false;
                }

                break;
            default:
                return false;
        }

        if (owner.Type == JgsType.Array && owner.ArrayLength == 0 && !owner.IsStringArray)
        {
            return true;
        }

        if (owner.Type != JgsType.Cell)
        {
            return false;
        }

        double named = brace.Indices[0] switch
        {
            PreEvaluated { Value.Type: JgsType.Number } ready => ready.Value.AsNumber,
            NumberLiteral literal => literal.Value,
            _ => double.NaN,
        };
        return named == Math.Floor(named) && named - Dialect.IndexBase >= owner.AsCell.Length;
    }

    /// <summary>A field read that touches nothing and answers false where it cannot see.</summary>
    private bool TryPeekField(MemberExpr member, JgsEnvironment env, out JgsValue value)
    {
        value = JgsValue.Null;
        if (member.Field is null)
        {
            return false;
        }

        JgsValue owner;
        switch (member.Target)
        {
            case VariableExpr variable:
                if (!LookUp(variable.Name, env, out owner))
                {
                    return false;
                }

                break;
            case MemberExpr inner:
                if (!TryPeekField(inner, env, out owner))
                {
                    return false;
                }

                break;
            default:
                return false;
        }

        if (owner.Type == JgsType.Struct && !owner.IsStructArray && owner.AsStruct.TryGetValue(member.Field, out JgsValue? held))
        {
            value = held;
            return true;
        }

        return false;
    }

    // ---- the write-back ---------------------------------------------------------------------------

    /// <summary>
    /// Where a write that rebuilt its container puts the result: a variable is rebound, a field is
    /// written, a cell element is written — each through its own entry, which is what makes growth
    /// and deletion through <c>s.f(end + 1) = v</c> or <c>c{1}(2) = []</c> land where the write
    /// was aimed.
    /// </summary>
    private void StoreBack(Expr target, JgsValue value, Node at, JgsEnvironment env)
    {
        switch (target)
        {
            case VariableExpr variable:
                Rebind(variable.Name, value, env);
                return;
            case MemberExpr member:
                AssignToMember(member, value, env);
                return;
            case BraceIndexExpr brace:
                AssignToBraceIndex(brace, value, env);
                return;
            default:
                throw new JgsRuntimeException(at.Line, at.Column,
                    "This write rebuilds its target, which needs a variable, a field or a cell element on the left.");
        }
    }

    /// <summary>
    /// Stores a grown container back and hands the road the wrapper to go on writing into: the
    /// grown one for a variable, and for any other target the wrapper its entry now holds — a value
    /// object's property keeps the checked copy its setter made, not the one it was handed, and a
    /// write into the wrong one would be lost.
    /// </summary>
    private JgsValue Stored(Expr target, JgsValue grown, Node at, JgsEnvironment env)
    {
        StoreBack(target, grown, at, env);
        return target is VariableExpr ? grown : EvaluateForWrite(target, env);
    }

    /// <summary>
    /// An index write into a scalar an entry holds - <c>s(2).f(3) = 9</c> where <c>f</c> is a
    /// number (V6, #162). The scalar is the one-by-one array it reads as: it is written in a
    /// scratch slot by the ordinary roads and stored back through the entry once. Writing in place
    /// after a store-back would depend on reading the very wrapper back, and a one-element array
    /// read out of a struct array's element is a selection, not the entry.
    /// </summary>
    private JgsValue AssignIntoScalarEntry(
        Expr target, JgsValue scalar, IReadOnlyList<Expr> subscripts, TokenType op, JgsValue rhs, Node at,
        JgsEnvironment env)
    {
        var scratch = new JgsEnvironment(env);
        scratch.Declare(LevelSlot, OneElementArray(scalar));
        var slot = new VariableExpr(LevelSlot) { Line = at.Line, Column = at.Column };
        JgsValue result = IndexWrite(slot, subscripts, op, rhs, at, scratch);

        scratch.TryGet(LevelSlot, out JgsValue written);
        StoreBack(target, written, at, env);
        return result;
    }

    // ---- M10: a right-hand side or subscript that is the target's own storage ---------------------

    /// <summary>
    /// Holds a counted share of the right-hand side, and of a subscript, when either is the
    /// target's own payload, so the target's first write detaches (M3) and the write goes on
    /// reading what it read. Both dialects: reading what one is writing is wrong in either.
    /// </summary>
    private static void HoldOverlap(JgsValue target, ref JgsValue rhs, JgsValue? index, JgsValue? second, ref ScopeHolds holds)
    {
        if (Overlaps(target, rhs))
        {
            rhs = ShareFor(rhs, ref holds);
        }

        if (index is not null && Overlaps(target, index))
        {
            ShareFor(index, ref holds);
        }

        if (second is not null && Overlaps(target, second))
        {
            ShareFor(second, ref holds);
        }
    }

    private static bool Overlaps(JgsValue target, JgsValue other) =>
        ReferenceEquals(target, other)
        || (other.Type is JgsType.Array or JgsType.Cell or JgsType.Struct && other.SharesStorageWith(target));

    private static JgsValue ShareFor(JgsValue value, ref ScopeHolds holds)
    {
        JgsValue share = JgsValue.Share(value);
        if (!ReferenceEquals(share, value) && share.ScopePayload is { } payload)
        {
            holds.Add(share, payload);
        }

        return share;
    }

    // ---- M14: the write-side subscript rules -------------------------------------------------------

    /// <summary>
    /// The positions a write mask names: its true entries, whatever its length. The extent the
    /// write needs is the highest of them, which is how <c>x(logical([0 0 0 1])) = 9</c> grows a
    /// three-element <c>x</c> (measured; a read mask keeps the read rule).
    /// </summary>
    private static int[] MaskPicks(JgsValue mask)
    {
        int length = mask.ArrayLength;
        var picks = new List<int>();
        if (mask.IsPacked)
        {
            ReadOnlySpan<double> span = mask.AsBuffer.AsSpan();
            for (int i = 0; i < length; i++)
            {
                if (span[i] != 0)
                {
                    picks.Add(i);
                }
            }

            return picks.ToArray();
        }

        for (int i = 0; i < length; i++)
        {
            if (mask.ElementAt(i).AsBool)
            {
                picks.Add(i);
            }
        }

        return picks.ToArray();
    }

    /// <summary>
    /// R2025b's refusal of a subscript below one on the write side: the linear form, or the
    /// positional one for a write with several subscripts.
    /// </summary>
    private JgsRuntimeException BadWriteIndex(int raw, int position, Node at) => Dialect.IsMatlab
        ? new JgsRuntimeException(at.Line, at.Column, position == 0
            ? "Array indices must be positive integers or logical values."
            : $"Index in position {position} is invalid. Array indices must be positive integers or logical values.")
        : new JgsRuntimeException(at.Line, at.Column,
            $"Index {raw} is out of range (indexing is {Dialect.IndexBase}-based).");

    /// <summary>
    /// M14's check of the subscripts a deletion names, before anything is rebuilt: a subscript below
    /// one is refused in R2025b's words rather than as a read past the end.
    /// </summary>
    private void RefuseNonPositive(JgsValue index, int position, Node at)
    {
        if (!Dialect.IsMatlab)
        {
            return;
        }

        if (index.Type == JgsType.Array)
        {
            if (IsLogicalIndex(index))
            {
                return;
            }

            for (int i = 0; i < index.ArrayLength; i++)
            {
                JgsValue element = index.ElementAt(i);
                if (element.Type == JgsType.Number && element.AsNumber < Dialect.IndexBase)
                {
                    throw BadWriteIndex((int)element.AsNumber, position, at);
                }
            }

            return;
        }

        if (index.Type == JgsType.Number && index.AsNumber < Dialect.IndexBase)
        {
            throw BadWriteIndex((int)index.AsNumber, position, at);
        }
    }
}
