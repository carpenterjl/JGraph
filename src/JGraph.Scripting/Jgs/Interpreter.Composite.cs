using JGraph.Data;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// V6 (ADR 0167): composite writes store back. A write whose path passes through a <em>computed</em>
/// level — a table's variable or its <c>Properties</c>, a graphics property, a <c>datetime</c>'s
/// component or property, a dictionary's cell value under braces: where the value read out is
/// built for the read and is not the storage — is get, modify, set: the level's value is read into
/// a scratch slot, the rest of the path is written into the slot by the ordinary roads (so M3, M10,
/// M14 and M16 apply to it as to any variable), and the slot is put back through the level's
/// setter, once; a holder the setter rebuilt is then stored back where it was read from — a
/// variable, a field, a cell element — by <see cref="StoreBack"/>, and a holder that is a
/// reference (a graphics handle) needs no storing.
/// </summary>
/// <remarks>
/// The walk down to the level reads only what a peek can see without running anything: a bound
/// name, a literal field of a scalar struct, a cell slot named by a number already in hand. A path
/// it cannot see along is left to the roads that were there before.
/// </remarks>
internal sealed partial class Interpreter
{
    /// <summary>No script can spell this, so nothing can collide with it.</summary>
    private const string LevelSlot = "level";

    /// <summary>
    /// Runs the write when its path passes through a computed level, and says whether it did.
    /// <paramref name="target"/> has had its parts evaluated already when any could run script
    /// code, so the peeks below evaluate nothing twice (M15).
    /// </summary>
    private bool TryWriteThroughComputedLevel(
        Expr target, AssignExpr assign, JgsValue rhs, bool owned, JgsEnvironment env, out JgsValue result)
    {
        result = rhs;

        // The common write — x(i) = v, s.f = v on something that is not a table — leaves at once.
        Expr? inner = InnerOf(target);
        if (inner is VariableExpr direct)
        {
            if (!LookUp(direct.Name, env, out JgsValue bound) || !IsComputedHolder(bound, target, target))
            {
                return false;
            }
        }

        var chain = new List<Expr>();
        for (Expr? node = target; node is not null; node = InnerOf(node))
        {
            chain.Add(node);
        }

        chain.Reverse();
        if (chain[0] is not VariableExpr root)
        {
            return false;
        }

        // A level that does not exist yet is created by get, modify, set as well (below); a name
        // never bound is the first such level. A cleared global keeps the roads it has (#158).
        bool creates = Dialect.IsMatlab && assign.Op == TokenType.Assign;
        if (!LookUp(root.Name, env, out JgsValue? held))
        {
            if (!creates || env.IsGlobal(root.Name))
            {
                return false;
            }

            held = null;
        }

        for (int k = 0; k + 1 < chain.Count; k++)
        {
            Expr step = chain[k + 1];
            if (held is null || creates)
            {
                if (chain.Count - (k + 2) == 0 && (held is null || !IsComputedHolder(held, step, target)))
                {
                    return false; // a last step on whatever is there is the ordinary roads'
                }

                if (held is null || !IsComputedHolder(held, step, target))
                {
                    switch (ClassifyLevel(held, chain, k + 1, out JgsValue? next, out JgsValue? start))
                    {
                        case LevelKind.Continue:
                            held = next;
                            continue;
                        case LevelKind.Create:
                            result = WriteThroughAbsentLevel(chain, k + 1, held, start, target, assign, rhs, env);
                            return true;
                        default:
                            return false;
                    }
                }
            }

            if (IsComputedHolder(held!, step, target))
            {
                if (step is BraceIndexExpr brace)
                {
                    result = held!.Type == JgsType.Table
                        ? WriteTableBrace(chain[k], held.AsTable, brace, assign, rhs, env)
                        : WriteDictionaryBrace(chain[k], held, brace, target, assign, rhs, env);
                    return true;
                }

                // A whole-value set binds the right-hand side (M2); a partial one hands it to the
                // ordinary roads, which bind what they store.
                JgsValue given = ReferenceEquals(step, target) && !owned && assign.Op == TokenType.Assign
                    ? CopyForBinding(rhs)
                    : rhs;
                result = WriteThroughLevel(chain[k], held!, (MemberExpr)step, target, assign, given, env);
                return true;
            }

            if (k + 2 >= chain.Count || !TryPeekStep(held!, step, out JgsValue peeked))
            {
                return false;
            }

            held = peeked;
        }

        return false;
    }

    // ---- levels that do not exist yet (V6, #82, #83) -------------------------------------------------

    private enum LevelKind
    {
        /// <summary>The ordinary roads take it from here.</summary>
        Ordinary,

        /// <summary>The level exists and the walk goes on through it.</summary>
        Continue,

        /// <summary>The level does not exist, and the ordinary roads cannot create it: get, modify, set.</summary>
        Create,
    }

    /// <summary>
    /// What the walk finds at <c>chain[level]</c> on <paramref name="held"/> (null: nothing there).
    /// The ordinary roads already create a field path with an optional final paren
    /// (<c>s.a.b(3) = 9</c>), a cell slot with a final paren (<c>c{3}(2) = 9</c>) and a struct
    /// array's element with one field (<c>s(3).f = 9</c>); everything past that - <c>x.y(3).z</c>,
    /// <c>c{3}.f</c>, <c>s.c{2}(3)</c>, <c>c{2}.a(2).b</c> - is created here, one level at a time.
    /// </summary>
    private LevelKind ClassifyLevel(JgsValue? held, List<Expr> chain, int level, out JgsValue? next, out JgsValue? start)
    {
        next = null;
        start = null;
        Expr step = chain[level];
        int rest = chain.Count - (level + 1);
        bool nothing = held is null
            || (held.Type == JgsType.Array && held.ArrayLength == 0 && !held.IsStringArray && !held.IsTime);
        bool plainStruct = held is { Type: JgsType.Struct, ClassName: null };

        switch (step)
        {
            case MemberExpr { Field: { } field }:
                if (plainStruct && !held!.IsStructArray && held.AsStruct.TryGetValue(field, out JgsValue? child))
                {
                    next = child;
                    return LevelKind.Continue;
                }

                // o.p(6).f = 9: the walk goes on through an object's property (V6), so a level
                // past it is created here and stored back through the property's setter.
                if (held is { Type: JgsType.Object } && held.AsObject.Fields.TryGetValue(field, out JgsValue? property))
                {
                    next = property;
                    return LevelKind.Continue;
                }

                if (nothing || (plainStruct && !held!.IsStructArray))
                {
                    return OrdinaryRoadsCreate(chain, level + 1) ? LevelKind.Ordinary : LevelKind.Create;
                }

                return LevelKind.Ordinary;

            case BraceIndexExpr brace:
                if (!nothing && held!.Type != JgsType.Cell)
                {
                    return LevelKind.Ordinary;
                }

                if (!nothing && TryPeekStep(held!, step, out JgsValue slot)
                    && !(slot.Type == JgsType.Array && slot.ArrayLength == 0 && !slot.IsStringArray))
                {
                    next = slot;
                    return LevelKind.Continue;
                }

                // c{3}(2) = 9 is the ordinary roads' own conjuring, from a name or a field path.
                return rest == 1 && chain[^1] is CallExpr or IndexExpr && brace.Indices.Count == 1
                       && chain[level - 1] is VariableExpr or MemberExpr
                    ? LevelKind.Ordinary
                    : LevelKind.Create;

            case CallExpr or IndexExpr:
            {
                IReadOnlyList<Expr> subscripts = step is CallExpr call ? call.Arguments : ((IndexExpr)step).Indices;

                // ch(1).YData(1) = 42: one handle out of an array of them, on the way to a property (V6).
                if (!nothing && TryPeekHandleElement(held!, subscripts, out JgsValue? handle))
                {
                    next = handle;
                    return LevelKind.Continue;
                }

                if (!nothing && !plainStruct)
                {
                    return LevelKind.Ordinary;
                }

                if (plainStruct && subscripts.Count == 1 && NamedPosition(subscripts[0]) is int position
                    && position >= 0 && position < held!.AsStructArray.Length)
                {
                    next = JgsValue.Struct(held.AsStructArray.Elements[position]); // a peek, never written through
                    return LevelKind.Continue;
                }

                // s(3).f = 9 grows a named struct array on the ordinary road.
                if (rest == 1 && chain[level + 1] is MemberExpr && subscripts.Count == 1 && chain[level - 1] is VariableExpr)
                {
                    return LevelKind.Ordinary;
                }

                // A new element starts with every field the array has, each holding [].
                start = plainStruct ? JgsValue.Struct(held!.AsStructArray.NewElement()) : null;
                return LevelKind.Create;
            }

            default:
                return LevelKind.Ordinary;
        }
    }

    /// <summary>Whether the steps from <paramref name="from"/> are fields with at most a final paren - what the ordinary roads create.</summary>
    private static bool OrdinaryRoadsCreate(List<Expr> chain, int from)
    {
        for (int i = from; i < chain.Count; i++)
        {
            if (chain[i] is MemberExpr { Field: not null })
            {
                continue;
            }

            if (i == chain.Count - 1 && chain[i] is CallExpr or IndexExpr)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>The 0-based position a subscript names when it is a whole number already in hand, or null.</summary>
    private int? NamedPosition(Expr subscript)
    {
        double named = subscript switch
        {
            PreEvaluated { Value.Type: JgsType.Number } ready => ready.Value.AsNumber,
            NumberLiteral literal => literal.Value,
            _ => double.NaN,
        };
        return named == Math.Floor(named) ? (int)named - Dialect.IndexBase : null;
    }

    /// <summary>
    /// Creates <c>chain[level]</c> by get, modify, set: the rest of the path is written into a
    /// scratch slot - which starts from nothing, or from a fresh element of the struct array the
    /// level belongs to - by the ordinary assignment, and the slot is then assigned to the level
    /// itself, which is one step shorter and so ends on a road that exists. Nothing is stored
    /// until the inner write has succeeded (M14).
    /// </summary>
    private JgsValue WriteThroughAbsentLevel(
        List<Expr> chain, int level, JgsValue? holder, JgsValue? start, Expr target, AssignExpr assign, JgsValue rhs,
        JgsEnvironment env)
    {
        Expr at = chain[level];
        var scratch = new JgsEnvironment(env);
        if (start is not null)
        {
            scratch.Declare(LevelSlot, start);
        }

        var slot = new VariableExpr(LevelSlot) { Line = at.Line, Column = at.Column };
        var inner = new AssignExpr(ReplaceNode(target, at, slot), TokenType.Assign,
            new PreEvaluated(rhs) { Line = assign.Value.Line, Column = assign.Value.Column })
        {
            Line = assign.Line,
            Column = assign.Column,
        };
        JgsValue result = EvaluateAssign(inner, scratch);
        if (!scratch.TryGet(LevelSlot, out JgsValue written))
        {
            throw new JgsRuntimeException(at.Line, at.Column, "This write names a level it did not create.");
        }

        // Every element of a struct array has every field: a field the new element gained is
        // given to the rest before the element goes in, as s(3).w = 1 does on the ordinary road.
        if (at is CallExpr or IndexExpr && holder is { Type: JgsType.Struct } && written.Type == JgsType.Struct
            && !written.IsStructArray)
        {
            JgsStructArray owner = EvaluateForWrite(chain[level - 1], env).WritableStructArray();
            foreach (string field in written.AsStruct.Keys)
            {
                owner.EnsureField(field);
            }
        }

        var outer = new AssignExpr(at, TokenType.Assign, new PreEvaluated(written) { Line = at.Line, Column = at.Column })
        {
            Line = assign.Line,
            Column = assign.Column,
        };
        EvaluateAssign(outer, env);
        return result;
    }

    /// <summary>The expression a path node is a step on, or null at the root.</summary>
    private static Expr? InnerOf(Expr node) => node switch
    {
        MemberExpr member => member.Target,
        BraceIndexExpr brace => brace.Target,
        CallExpr call => call.Callee,
        IndexExpr index => index.Target,
        _ => null,
    };

    /// <summary>
    /// Whether <paramref name="step"/> on <paramref name="holder"/> is a computed level: a table's
    /// dot, or its brace when the brace is the whole target (<c>T{r, v} = x</c>); a graphics
    /// handle's property when the write goes on past it (<c>p.YData(2) = 9</c> — the whole-value
    /// set <c>p.YData = y</c> keeps the road it has); a time value's property or component, whole
    /// or in part (<c>e.Day(2) = 15</c>, <c>c{1}.Format = 'yyyy'</c>); a dictionary's brace
    /// (<c>d{"k"}(1) = 9</c>).
    /// </summary>
    private static bool IsComputedHolder(JgsValue holder, Expr step, Expr target)
    {
        if (holder.Type == JgsType.Table)
        {
            return step is MemberExpr || (step is BraceIndexExpr && ReferenceEquals(step, target));
        }

        if (step is BraceIndexExpr)
        {
            return holder.Type == JgsType.Struct && holder.ClassName == JgsBuiltins.DictionaryClassName;
        }

        if (step is not MemberExpr)
        {
            return false;
        }

        if (holder.IsTime)
        {
            return true;
        }

        // A matfile's variable is the file's (V6, #112): m.v = x, m.v(1, 2) = 8 and
        // m.Properties.Writable = true are all get, modify, set on the file or the settings.
        if (JgsBuiltins.IsMatFile(holder))
        {
            return true;
        }

        // A SetObservable property somebody listens to is get, modify, set when the write goes on
        // past it (o.a(2) = 9, o.s.v(2) = 5): the slot is put back through the property's setter,
        // which raises the one PreSet and PostSet R2025b raises (V6, #108). The whole-value set
        // o.a = v keeps the road it has, which raises them itself. An unobserved property is the
        // ordinary roads' as before.
        if (holder.Type == JgsType.Object)
        {
            return !ReferenceEquals(step, target)
                && step is MemberExpr { Field: { } field }
                && holder.AsObject.Class.Property(field) is { Observable: true }
                && holder.AsObject.HasPropertyListener(field);
        }

        return holder.Type == JgsType.Number && !ReferenceEquals(step, target) && JgsHandleRegistry.TryGet(holder, out _);
    }

    /// <summary>
    /// One handle a subscript names out of an array of handles — or the one handle a number is,
    /// under <c>(1)</c> — when the position is a whole number already in hand.
    /// </summary>
    private bool TryPeekHandleElement(JgsValue held, IReadOnlyList<Expr> subscripts, out JgsValue? handle)
    {
        handle = null;
        if (subscripts.Count != 1 || NamedPosition(subscripts[0]) is not int position || position < 0)
        {
            return false;
        }

        if (IsHandleArrayValue(held) && position < held.ArrayLength)
        {
            handle = held.ElementAt(position);
            return true;
        }

        if (position == 0 && held.Type == JgsType.Number && JgsHandleRegistry.TryGet(held, out _))
        {
            handle = held;
            return true;
        }

        return false;
    }

    /// <summary>What one step reads, where a peek can see it without running anything.</summary>
    private bool TryPeekStep(JgsValue held, Expr step, out JgsValue next)
    {
        next = JgsValue.Null;
        switch (step)
        {
            case MemberExpr { Field: { } field }
                when held.Type == JgsType.Struct && !held.IsStructArray:
                if (held.AsStruct.TryGetValue(field, out JgsValue? found))
                {
                    next = found;
                    return true;
                }

                return false;

            case MemberExpr { Field: { } property } when held.Type == JgsType.Object:
                if (held.AsObject.Fields.TryGetValue(property, out JgsValue? holds))
                {
                    next = holds;
                    return true;
                }

                return false;

            case CallExpr { Arguments: var arguments } when TryPeekHandleElement(held, arguments, out JgsValue? ofCall):
                next = ofCall!;
                return true;

            case IndexExpr { Indices: var indices } when TryPeekHandleElement(held, indices, out JgsValue? ofIndex):
                next = ofIndex!;
                return true;

            case BraceIndexExpr { Indices.Count: 1 } brace when held.Type == JgsType.Cell:
            {
                double named = brace.Indices[0] switch
                {
                    PreEvaluated { Value.Type: JgsType.Number } ready => ready.Value.AsNumber,
                    NumberLiteral literal => literal.Value,
                    _ => double.NaN,
                };
                int position = (int)named - Dialect.IndexBase;
                if (named != Math.Floor(named) || position < 0 || position >= held.AsCell.Length)
                {
                    return false;
                }

                next = held.AsCell[position];
                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Get, modify, set at one computed level: <paramref name="level"/> on the holder
    /// <paramref name="holderExpr"/> reads as <paramref name="holder"/>.
    /// </summary>
    private JgsValue WriteThroughLevel(
        Expr holderExpr, JgsValue holder, MemberExpr level, Expr target, AssignExpr assign, JgsValue rhs,
        JgsEnvironment env)
    {
        string field = FieldName(level, env);
        if (holder.Type == JgsType.Object)
        {
            return WriteThroughObservableProperty(holder, field, level, target, assign, rhs, env);
        }

        if (JgsBuiltins.IsMatFile(holder))
        {
            return WriteThroughMatFile(holder, field, level, target, assign, rhs, env);
        }

        if (holder.Type != JgsType.Table)
        {
            return WriteThroughPropertyLevel(holderExpr, holder, field, level, target, assign, rhs, env);
        }

        Table table = holder.AsTable;

        // T.Var = v, T.Properties = p: the level is the whole target.
        if (ReferenceEquals(level, target))
        {
            JgsValue value = assign.Op == TokenType.Assign
                ? rhs
                : ApplyBinary(UnderlyingOp(assign.Op), JgsBuiltins.TableColumnValue(table, field, level.Line, level.Column), rhs, assign);
            StoreBack(holderExpr, JgsValue.Table(SetTableMember(table, field, value, whole: true, level)), assign, env);
            return value;
        }

        // A variable the write names for the first time starts from the empty of its kind, as a
        // field does; a name the table answers to is read out as the value it is.
        bool known = field == "Properties" || table.TryGetColumn(field, out _) || table.RowTimes?.Name == field;
        JgsValue current = known
            ? JgsBuiltins.TableColumnValue(table, field, level.Line, level.Column)
            : EmptyOfKind(rhs);

        // A text variable reads out as a cell, so a string written into one of its rows is wrapped
        // the way MATLAB's own string column would take it.
        if (current.Type == JgsType.Cell && rhs.Type == JgsType.String && assign.Op == TokenType.Assign
            && target is CallExpr or IndexExpr && ReferenceEquals(InnerOf(target), level))
        {
            rhs = JgsValue.Cell(new[] { rhs });
        }

        JgsValue written = WriteIntoSlot(current, level, target, assign, rhs, env, out JgsValue result);
        StoreBack(holderExpr, JgsValue.Table(SetTableMember(table, field, written, whole: false, level)), assign, env);
        return result;
    }

    /// <summary>
    /// Get, modify, set at a property level (V6, #95-#99, #126, #132-#135): a graphics handle's
    /// property, read by its getter and written by its setter, the handle being the storage; or a
    /// time value's property or component, whose set rebuilds the value, stored back where it was
    /// read. The getter runs once here, after the subscripts (with their <c>end</c>s) and the
    /// right-hand side have run - <c>get;rhs;get;set</c> - and the setter once, after the inner
    /// write has succeeded (M14).
    /// </summary>
    private JgsValue WriteThroughPropertyLevel(
        Expr holderExpr, JgsValue holder, string field, MemberExpr level, Expr target, AssignExpr assign, JgsValue rhs,
        JgsEnvironment env)
    {
        JgsHandleEntry? handle = holder.IsTime ? null : JgsHandleRegistry.Require(holder, level.Line, level.Column);
        JgsValue result;
        JgsValue written;
        if (ReferenceEquals(level, target))
        {
            // A whole-value set reads nothing (h.Day = 1 on a duration is the setter's refusal, in
            // its words); a compound one reads the value it combines with.
            written = assign.Op == TokenType.Assign
                ? rhs
                : ApplyBinary(UnderlyingOp(assign.Op), PropertyOf(handle, holder, field, level), rhs, assign);
            result = written;
        }
        else
        {
            JgsValue current = PropertyOf(handle, holder, field, level);
            written = WriteIntoSlot(current, level, target, assign, rhs, env, out result);

            // A property that answered a handle (ax.XAxis.Color(1) = 0.5) is a reference: the inner
            // write landed on the object it names, and there is nothing to set.
            if (handle is not null && written.Type == JgsType.Number && current.Type == JgsType.Number
                && written.AsNumber == current.AsNumber && JgsHandleRegistry.TryGet(current, out _))
            {
                return result;
            }

            // A scalar property read as a one-by-one goes back as the scalar it is (p.LineWidth(1) = 3).
            if (current.Type is JgsType.Number or JgsType.Bool && written.Type == JgsType.Array && written.ArrayLength == 1
                && !written.IsStringArray)
            {
                written = written.ElementAt(0);
            }
        }

        if (handle is not null)
        {
            JgsGraphicsProperties.Set(handle, field, written, level.Line, level.Column);
            return result;
        }

        StoreBack(holderExpr, JgsBuiltins.SetTimeProperty(holder, field, written, level.Line, level.Column), assign, env);
        return result;
    }

    /// <summary>
    /// Get, modify, set at an observable property of a user object (V6, #108): the property's
    /// value goes into the slot as a share, so the slot's first write copies it (M3) and the
    /// object — and every alias of it, the object being a handle — reads as it was until the set;
    /// the set is the ordinary property write, which checks the value against its declaration and
    /// raises the one <c>PreSet</c> and <c>PostSet</c>.
    /// </summary>
    private JgsValue WriteThroughObservableProperty(
        JgsValue holder, string field, MemberExpr level, Expr target, AssignExpr assign, JgsValue rhs, JgsEnvironment env)
    {
        RequireLive(holder.AsObject, level.Line, level.Column);
        if (!holder.AsObject.Fields.TryGetValue(field, out JgsValue? current))
        {
            throw new JgsRuntimeException(level.Line, level.Column,
                $"'{holder.AsObject.Class.Name}' has no property '{field}'.");
        }

        JgsValue written = WriteIntoSlot(current, level, target, assign, rhs, env, out JgsValue result, owned: false);
        AssignToMember(level, written, env);
        return result;
    }

    /// <summary>
    /// Get, modify, set at a matfile's variable (V6, #112): a read-only file refuses before anything
    /// is read (R2025b's order); the variable is decoded from the file, or starts from the empty of
    /// the right-hand side's kind when the file has none; the ordinary roads write the slot; and
    /// the slot goes back into the file. <c>Properties</c> is the same road onto the settings,
    /// whose slot takes a share so a refused value leaves them as they were.
    /// </summary>
    private JgsValue WriteThroughMatFile(
        JgsValue holder, string field, MemberExpr level, Expr target, AssignExpr assign, JgsValue rhs, JgsEnvironment env)
    {
        JgsValue written;
        JgsValue result;
        if (ReferenceEquals(level, target))
        {
            written = assign.Op == TokenType.Assign
                ? rhs
                : ApplyBinary(UnderlyingOp(assign.Op), JgsBuiltins.GetMatFileMember(holder, field, level.Line, level.Column), rhs, assign);
            result = written;
        }
        else
        {
            JgsBuiltins.RequireMatFileWritable(holder, field, level.Line, level.Column);
            bool settings = field == "Properties";
            JgsValue current = settings || JgsBuiltins.MatFileHasVariable(holder, field, level.Line, level.Column)
                ? JgsBuiltins.GetMatFileMember(holder, field, level.Line, level.Column)
                : EmptyOfKind(rhs);
            written = WriteIntoSlot(current, level, target, assign, rhs, env, out result, owned: !settings);
        }

        JgsBuiltins.WriteMatFileVariable(holder, field, written, level.Line, level.Column);
        return result;
    }

    /// <summary>The get half of a property level: the graphics getter, or the time value's property.</summary>
    private static JgsValue PropertyOf(JgsHandleEntry? handle, JgsValue holder, string field, MemberExpr level) =>
        handle is not null
            ? JgsGraphicsProperties.Get(handle, field, level.Line, level.Column)
            : JgsBuiltins.GetTimeProperty(holder, field, level.Line, level.Column);

    /// <summary>
    /// The modify half: <paramref name="current"/> goes into the scratch slot, the target is
    /// rewritten with the slot in the level's place, and the ordinary assignment writes into it.
    /// Hands back what the slot holds afterwards; <paramref name="result"/> is the assignment's value.
    /// A value a getter minted is the slot's own; one read out of storage (<paramref name="owned"/>
    /// false) goes in as a share, so the slot's first write copies it (M3) and the storage - and
    /// every alias of it - is left as it was until the set.
    /// </summary>
    private JgsValue WriteIntoSlot(
        JgsValue current, Expr level, Expr target, AssignExpr assign, JgsValue rhs, JgsEnvironment env, out JgsValue result,
        bool owned = true)
    {
        var scratch = new JgsEnvironment(env);
        JgsValue slotValue = owned ? current : JgsValue.Share(current);
        scratch.Declare(LevelSlot, slotValue);
        var slot = new VariableExpr(LevelSlot) { Line = level.Line, Column = level.Column };
        var rewritten = new AssignExpr(ReplaceNode(target, level, slot), assign.Op,
            new PreEvaluated(rhs) { Line = assign.Value.Line, Column = assign.Value.Column })
        {
            Line = assign.Line,
            Column = assign.Column,
        };
        result = EvaluateAssign(rewritten, scratch);
        scratch.TryGet(LevelSlot, out JgsValue written);
        return written;
    }

    /// <summary>
    /// <c>d{key} = v</c> and <c>d{key}(i) = v</c> on a dictionary whose values are cells (V6,
    /// #136): the brace names the cell's content, which is read out, written and put back as the
    /// one-element cell the entry holds; the rebuilt dictionary is stored back where it was read.
    /// A key the dictionary does not have yet is added, as MATLAB adds it.
    /// </summary>
    private JgsValue WriteDictionaryBrace(
        Expr holderExpr, JgsValue holder, BraceIndexExpr brace, Expr target, AssignExpr assign, JgsValue rhs,
        JgsEnvironment env)
    {
        if (brace.Indices.Count != 1)
        {
            throw new JgsRuntimeException(brace.Line, brace.Column,
                "A dictionary is indexed by one key, as d{key}.");
        }

        JgsValue key = Evaluate(brace.Indices[0], env);
        JgsValue current = JgsBuiltins.TryLookup(holder, key, out JgsValue stored)
            ? JgsBuiltins.DictionaryBraceContent(stored, brace.Line, brace.Column)
            : EmptyOfKind(rhs);

        JgsValue result;
        JgsValue written;
        if (ReferenceEquals(brace, target))
        {
            written = assign.Op == TokenType.Assign ? rhs : ApplyBinary(UnderlyingOp(assign.Op), current, rhs, assign);
            result = written;
        }
        else
        {
            written = WriteIntoSlot(current, brace, target, assign, rhs, env, out result, owned: false);
        }

        // M8: a wrapper of its own over the holder's payload, which Put detaches (M7) - the
        // original, and every other name on it, keeps its entries.
        JgsValue rebuilt = JgsValue.Share(holder);
        JgsBuiltins.Put(rebuilt, key,
            JgsBuiltins.RetainedForEntry(JgsValue.Cell(new[] { written }), Dialect.CopyOnAssign), brace.Line, brace.Column);
        StoreBack(holderExpr, rebuilt, assign, env);
        return result;
    }

    /// <summary>The path <paramref name="target"/> with the node <paramref name="old"/> along it replaced.</summary>
    private static Expr ReplaceNode(Expr target, Expr old, Expr replacement)
    {
        if (ReferenceEquals(target, old))
        {
            return replacement;
        }

        return target switch
        {
            MemberExpr member => new MemberExpr(ReplaceNode(member.Target, old, replacement), member.Field, member.FieldName)
            {
                Line = member.Line,
                Column = member.Column,
            },
            BraceIndexExpr brace => new BraceIndexExpr(ReplaceNode(brace.Target, old, replacement), brace.Indices)
            {
                Line = brace.Line,
                Column = brace.Column,
            },
            CallExpr call => new CallExpr(ReplaceNode(call.Callee, old, replacement), call.Arguments)
            {
                Line = call.Line,
                Column = call.Column,
            },
            IndexExpr index => new IndexExpr(ReplaceNode(index.Target, old, replacement), index.Indices)
            {
                Line = index.Line,
                Column = index.Column,
            },
            _ => target,
        };
    }

    /// <summary>
    /// The set half of a table level: <c>Properties</c> through its checks, the row times under
    /// their own name, a variable replaced, added or — <c>T.Var = []</c>, the whole variable set
    /// to the empty double — removed.
    /// </summary>
    private static Table SetTableMember(Table table, string field, JgsValue value, bool whole, Node at)
    {
        if (field == "Properties")
        {
            return JgsBuiltins.WithTableProperties(table, value, at.Line, at.Column);
        }

        if (whole && value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray
            && table.TryGetColumn(field, out TableColumn doomed) && string.Equals(doomed.Name, field, StringComparison.Ordinal))
        {
            return JgsBuiltins.WithoutColumn(table, field);
        }

        TableColumn column = JgsBuiltins.TableColumnFrom("table", field, value, at.Line, at.Column);
        if (table.RowTimes is { } times && times.Name == field)
        {
            if (column.RowCount != table.RowCount)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    "The row times must have one element for each row of the timetable.");
            }

            return table.WithRowLabels(table.RowNames, column);
        }

        return JgsBuiltins.WithColumn(table, column, grows: !whole, at.Line, at.Column);
    }
}
