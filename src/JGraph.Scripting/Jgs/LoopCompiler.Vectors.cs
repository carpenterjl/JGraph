using System.Collections;
using System.Reflection;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// ADR 0160 (12d): the guarded vector class and the walked statement. A variable is a vector of
/// the loop when the body indexes it by one scalar (<c>v(i)</c>, <c>x(i) = s</c>) or assigns it an
/// elementwise combination of vectors and scalars; its value at entry must be a packed real double
/// row or column, and every combination goes through the walk's own operator and builtin code on
/// already-evaluated operands, so the bytes are the walk's bytes. A statement the whitelist refuses
/// no longer refuses the loop: it becomes a <see cref="LoopOp.Walk"/>, executed by the tree walk
/// each time with the registers spilled before and reloaded after — unless it holds a construct
/// that could change what the compiled program assumed (a <c>global</c>, an <c>eval</c>), which
/// still refuses the loop.
/// </summary>
internal sealed partial class LoopCompiler
{
    /// <summary>The variables inferred to hold vectors, from the body's syntax alone.</summary>
    private readonly HashSet<string> _vectorNames = new(StringComparer.Ordinal);

    /// <summary>The statements the walk runs on the program's behalf.</summary>
    private readonly HashSet<Stmt> _walked = new(ReferenceEqualityComparer.Instance);

    private readonly List<LoopSlot> _vslots = [];
    private readonly Dictionary<string, int> _vslotOf = new(StringComparer.Ordinal);
    private readonly List<Node> _nodes = [];
    private int _nextVReg;
    private int _vregCount;

    /// <summary>
    /// Names a walked statement may not call: each can rebind, unbind or redeclare a variable of
    /// the enclosing workspace by a road the compiled program cannot see, so a loop holding one
    /// stays on the walk entirely.
    /// </summary>
    private static readonly HashSet<string> ForbiddenInWalk = new(StringComparer.Ordinal)
    {
        "clear", "clearvars", "clc", "eval", "evalin", "assignin", "load", "run", "feval", "builtin", "input", "keyboard",
    };

    // --- Kinds: which names are vectors ---------------------------------------------------------

    /// <summary>
    /// Every name the body mentions that holds a vector of the class right now is a vector name
    /// to begin with — except a name the whitelist calls as a builtin, which stays a call (and
    /// refuses at entry, where the name resolves to the variable instead).
    /// </summary>
    private void SeedKinds(Stmt root)
    {
        foreach (string name in MentionedNames(root))
        {
            if (!IsHotLoopCallee(name) && _vectorAtEntry(name))
            {
                _vectorNames.Add(name);
            }
        }
    }

    /// <summary>Every variable name mentioned anywhere under <paramref name="node"/>.</summary>
    private static IEnumerable<string> MentionedNames(object node)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Collect(node);
        return names;

        void Collect(object current)
        {
            if (current is VariableExpr variable)
            {
                names.Add(variable.Name);
            }

            foreach (object child in ChildrenOf(current))
            {
                Collect(child);
            }
        }
    }

    /// <summary>
    /// Marks every name the body treats as a vector: the base of a one-scalar paren index, read or
    /// written, and the target of a whole assignment whose right side is vector-valued. Iterates to
    /// a fixed point, since <c>y = x + 1</c> makes <c>y</c> a vector only once <c>x</c> is one.
    /// </summary>
    private void InferKinds(Stmt root)
    {
        bool changed;
        do
        {
            changed = false;
            switch (root)
            {
                case ForStmt forStmt:
                    changed = InferKinds(forStmt.Body);
                    break;
                case WhileStmt whileStmt:
                    changed = InferKinds(whileStmt.Body);
                    break;
            }
        }
        while (changed);
    }

    private bool InferKinds(IReadOnlyList<Stmt> statements)
    {
        bool changed = false;
        foreach (Stmt statement in statements)
        {
            switch (statement)
            {
                case ExprStmt { Expression: AssignExpr { Target: VariableExpr target, Op: TokenType.Assign } assign }:
                    if (IsVectorExpr(assign.Value))
                    {
                        changed |= _vectorNames.Add(target.Name);
                    }
                    else
                    {
                        changed |= MarkIndexReads(assign.Value);
                    }

                    break;

                case ExprStmt { Expression: AssignExpr { Target: CallExpr { Callee: VariableExpr indexed, Arguments.Count: 1 } paren } assign }:
                    if (IsIndexable(indexed.Name) && !IsVectorExpr(paren.Arguments[0]))
                    {
                        changed |= _vectorNames.Add(indexed.Name);
                    }

                    changed |= MarkIndexReads(paren.Arguments[0]);
                    changed |= MarkIndexReads(assign.Value);
                    break;

                case ExprStmt expr:
                    changed |= MarkIndexReads(expr.Expression);
                    break;

                case IfStmt ifStmt:
                    changed |= MarkIndexReads(ifStmt.Condition);
                    changed |= InferKinds(ifStmt.Then);
                    if (ifStmt.Else is not null)
                    {
                        changed |= InferKinds(ifStmt.Else);
                    }

                    break;

                case ForStmt { Iterable: RangeExpr range } forStmt:
                    changed |= MarkIndexReads(range.Start);
                    if (range.Step is not null)
                    {
                        changed |= MarkIndexReads(range.Step);
                    }

                    changed |= MarkIndexReads(range.Stop);
                    changed |= InferKinds(forStmt.Body);
                    break;

                case WhileStmt whileStmt:
                    changed |= MarkIndexReads(whileStmt.Condition);
                    changed |= InferKinds(whileStmt.Body);
                    break;
            }
        }

        return changed;
    }

    /// <summary>Marks the base of every <c>name(scalar)</c> read inside <paramref name="expression"/> as a vector.</summary>
    private bool MarkIndexReads(Expr expression)
    {
        bool changed = false;
        switch (expression)
        {
            case CallExpr { Callee: VariableExpr callee } call:
                if (call.Arguments.Count == 1 && IsIndexable(callee.Name) && !IsVectorExpr(call.Arguments[0])
                    && call.Arguments[0] is not (RangeExpr or AllExpr or EndExpr))
                {
                    changed |= _vectorNames.Add(callee.Name);
                }

                foreach (Expr argument in call.Arguments)
                {
                    changed |= MarkIndexReads(argument);
                }

                break;
            case UnaryExpr unary:
                changed |= MarkIndexReads(unary.Operand);
                break;
            case BinaryExpr binary:
                changed |= MarkIndexReads(binary.Left);
                changed |= MarkIndexReads(binary.Right);
                break;
            case LogicalExpr logical:
                changed |= MarkIndexReads(logical.Left);
                changed |= MarkIndexReads(logical.Right);
                break;
        }

        return changed;
    }

    private static bool IsHotLoopCallee(string name) =>
        JgsBuiltins.TryHotLoopUnary(name, out _, out _) || JgsBuiltins.IsHotLoopBinary(name);

    /// <summary>
    /// Whether <c>name(i)</c> may be an element read: the name is not one the whitelist calls, and
    /// it is bound to a variable as the loop is entered — a vector the loop indexes exists before
    /// the loop, while <c>toc(t)</c> or <c>myfun(k)</c> names a function and the statement walks.
    /// A vector the loop itself creates by a whole assignment is a vector name by that assignment.
    /// </summary>
    private bool IsIndexable(string name) =>
        !IsHotLoopCallee(name) && (_variableAtEntry(name) || _vectorNames.Contains(name));

    /// <summary>Whether <paramref name="expression"/> is vector-valued under the current kinds — the syntactic rule, not a proof.</summary>
    private bool IsVectorExpr(Expr expression) => expression switch
    {
        VariableExpr variable => _vectorNames.Contains(variable.Name),
        UnaryExpr { Op: TokenType.Minus } minus => IsVectorExpr(minus.Operand),
        // Two vectors under a matrix operator are an inner product or a matrix, neither of the class.
        BinaryExpr { Op: TokenType.Star or TokenType.Slash or TokenType.Caret or TokenType.Backslash } matrix =>
            IsVectorExpr(matrix.Left) != IsVectorExpr(matrix.Right),
        BinaryExpr binary when IsVectorArithmetic(binary.Op) => IsVectorExpr(binary.Left) || IsVectorExpr(binary.Right),
        CallExpr { Callee: VariableExpr callee, Arguments.Count: 1 } call
            when JgsBuiltins.TryHotLoopUnary(callee.Name, out _, out _) => IsVectorExpr(call.Arguments[0]),
        _ => false,
    };

    private static bool IsVectorArithmetic(TokenType op) => op is TokenType.Plus or TokenType.Minus
        or TokenType.Star or TokenType.Slash or TokenType.Caret or TokenType.Backslash
        or TokenType.DotStar or TokenType.DotSlash or TokenType.DotCaret or TokenType.DotBackslash;

    /// <summary>
    /// Whether an assignment's right side is a logical by its syntax — a comparison, a logical
    /// operator, a <c>~</c>, <c>true</c>/<c>false</c>. The walk demotes a packed array to boxed
    /// when a logical is written into one element, which no vector op reproduces; such a
    /// statement walks. A bare variable is decided at run time by <see cref="LoopOp.VStore"/>.
    /// </summary>
    private static bool IsSyntacticallyLogical(Expr expression) => expression switch
    {
        BoolLiteral => true,
        LogicalExpr => true,
        UnaryExpr { Op: TokenType.Bang } => true,
        BinaryExpr binary => binary.Op is TokenType.Less or TokenType.LessEqual or TokenType.Greater
            or TokenType.GreaterEqual or TokenType.EqualEqual or TokenType.BangEqual or TokenType.Amp or TokenType.Pipe,
        _ => false,
    };

    // --- Scan: vector slots -----------------------------------------------------------------------

    private int DeclareVector(string name)
    {
        if (_slotOf.ContainsKey(name))
        {
            throw Refuse(); // one name, two kinds: the walk sorts it out
        }

        if (!_vslotOf.TryGetValue(name, out int slot))
        {
            slot = _vslots.Count;
            _vslotOf[name] = slot;
            _vslots.Add(new LoopSlot(name));
        }

        return slot;
    }

    private void ReadVector(string name, HashSet<string> assigned)
    {
        int slot = DeclareVector(name);
        if (!assigned.Contains(name))
        {
            _vslots[slot].EntryRequired = true;
        }
    }

    /// <summary>The scan of a vector-valued expression: every operand a vector slot, a scalar, or a combination of them.</summary>
    private void ScanVectorExpr(Expr expression, HashSet<string> assigned)
    {
        switch (expression)
        {
            case VariableExpr variable when _vectorNames.Contains(variable.Name):
                ReadVector(variable.Name, assigned);
                return;

            case UnaryExpr { Op: TokenType.Minus } minus:
                ScanVectorExpr(minus.Operand, assigned);
                return;

            case BinaryExpr binary when IsVectorArithmetic(binary.Op):
            {
                bool leftVector = IsVectorExpr(binary.Left);
                bool rightVector = IsVectorExpr(binary.Right);
                if (!leftVector && !rightVector)
                {
                    throw Refuse();
                }

                // Two vectors under a matrix operator are a product or a division the class does
                // not hold (a row times a column is a scalar); the walk keeps them.
                if (leftVector && rightVector
                    && binary.Op is TokenType.Star or TokenType.Slash or TokenType.Caret or TokenType.Backslash)
                {
                    throw Refuse();
                }

                if (leftVector)
                {
                    ScanVectorExpr(binary.Left, assigned);
                }
                else
                {
                    ScanExpr(binary.Left, assigned);
                }

                if (rightVector)
                {
                    ScanVectorExpr(binary.Right, assigned);
                }
                else
                {
                    ScanExpr(binary.Right, assigned);
                }

                return;
            }

            case CallExpr { Callee: VariableExpr callee, Arguments.Count: 1 } call
                when JgsBuiltins.TryHotLoopUnary(callee.Name, out Func<double, double> kernel, out Func<double, bool>? staysReal):
                if (!_unaryIndex.ContainsKey(callee.Name))
                {
                    _unaryIndex[callee.Name] = _unary.Count;
                    _unary.Add(kernel);
                    _unaryGuard.Add(staysReal);
                    _unaryNames.Add(callee.Name);
                }

                _calledNames.Add(callee.Name);
                _requiredBuiltins.Add(callee.Name);
                ScanVectorExpr(call.Arguments[0], assigned);
                return;

            default:
                throw Refuse();
        }
    }

    // --- Scan: the walked statement -------------------------------------------------------------

    /// <summary>What a refused scan rolls back to, so a walked statement leaves no slot, constant or kernel behind.</summary>
    private readonly record struct ScanMark(
        int Slots, int VectorSlots, int Constants, int Unary,
        bool[] EntryRequired, bool[] LoopVariables, bool[] VectorEntryRequired,
        string[] RequiredBuiltins, string[] CalledNames);

    private ScanMark Mark() => new(
        _slots.Count, _vslots.Count, _constValues.Count, _unary.Count,
        _slots.Select(static s => s.EntryRequired).ToArray(),
        _slots.Select(static s => s.IsLoopVariable).ToArray(),
        _vslots.Select(static s => s.EntryRequired).ToArray(),
        [.. _requiredBuiltins], [.. _calledNames]);

    private void Rollback(ScanMark mark)
    {
        for (int i = _slots.Count - 1; i >= mark.Slots; i--)
        {
            _slotOf.Remove(_slots[i].Name);
            _slots.RemoveAt(i);
        }

        for (int i = _vslots.Count - 1; i >= mark.VectorSlots; i--)
        {
            _vslotOf.Remove(_vslots[i].Name);
            _vslots.RemoveAt(i);
        }

        for (int i = _constValues.Count - 1; i >= mark.Constants; i--)
        {
            _constOf.Remove(_constValues[i]);
            _constValues.RemoveAt(i);
        }

        for (int i = _unary.Count - 1; i >= mark.Unary; i--)
        {
            _unaryIndex.Remove(_unaryNames[i]);
            _unaryNames.RemoveAt(i);
            _unary.RemoveAt(i);
            _unaryGuard.RemoveAt(i);
        }

        for (int i = 0; i < mark.Slots; i++)
        {
            _slots[i].EntryRequired = mark.EntryRequired[i];
            _slots[i].IsLoopVariable = mark.LoopVariables[i];
        }

        for (int i = 0; i < mark.VectorSlots; i++)
        {
            _vslots[i].EntryRequired = mark.VectorEntryRequired[i];
        }

        _requiredBuiltins.Clear();
        _requiredBuiltins.UnionWith(mark.RequiredBuiltins);
        _calledNames.Clear();
        _calledNames.UnionWith(mark.CalledNames);
    }

    /// <summary>
    /// A nested loop whose every statement is walked compiles nothing of its own; refusing it here
    /// makes the whole nested loop one walked statement, run by the walk as it always was (ADR
    /// 0160: measured as a loss otherwise, a spill and a reload per iteration for nothing).
    /// </summary>
    private void RefuseIfNothingCompiles(IReadOnlyList<Stmt> body)
    {
        if (JgsLoopJit.Vectors && body.All(_walked.Contains))
        {
            throw Refuse();
        }
    }

    /// <summary>
    /// How many statements under <paramref name="root"/> the program runs itself — every
    /// statement of the body and its nested blocks that is not walked, the nesting statements
    /// (an <c>if</c>, a nested loop) included.
    /// </summary>
    private int CompiledStatementCount(Stmt root)
    {
        int count = 0;
        Count(root switch
        {
            ForStmt loop => loop.Body,
            WhileStmt loop => loop.Body,
            _ => [],
        });
        return count;

        void Count(IReadOnlyList<Stmt> statements)
        {
            foreach (Stmt statement in statements)
            {
                if (_walked.Contains(statement))
                {
                    continue;
                }

                count++;
                switch (statement)
                {
                    case IfStmt ifStmt:
                        Count(ifStmt.Then);
                        if (ifStmt.Else is not null)
                        {
                            Count(ifStmt.Else);
                        }

                        break;
                    case ForStmt nested:
                        Count(nested.Body);
                        break;
                    case WhileStmt nested:
                        Count(nested.Body);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Whether the walk may run <paramref name="statement"/> on the program's behalf: nothing in
    /// it declares a global or persistent, defines a function, or calls a name that can reach into
    /// the workspace by a road the program cannot see.
    /// </summary>
    private static bool CanWalk(Stmt statement) => !ContainsForbidden(statement);

    private static bool ContainsForbidden(object node)
    {
        switch (node)
        {
            case GlobalStmt or PersistentStmt or FnStmt or ClassdefStmt or ArgumentsStmt:
                return true;
            case CallExpr { Callee: VariableExpr callee } when ForbiddenInWalk.Contains(callee.Name):
                return true;
            case VariableExpr bare when ForbiddenInWalk.Contains(bare.Name):
                return true;
        }

        foreach (object child in ChildrenOf(node))
        {
            if (ContainsForbidden(child))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly Dictionary<Type, PropertyInfo[]> ChildProperties = [];

    /// <summary>
    /// Every syntax object under <paramref name="node"/>: each public property that holds a node,
    /// a case arm, or a list of them (lists of lists included). Reflection, cached per type, so a
    /// node shape added later is visited without this file knowing it.
    /// </summary>
    private static IEnumerable<object> ChildrenOf(object node)
    {
        PropertyInfo[] properties;
        lock (ChildProperties)
        {
            if (!ChildProperties.TryGetValue(node.GetType(), out properties!))
            {
                properties = node.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(static p => p.GetIndexParameters().Length == 0 && p.PropertyType != typeof(string))
                    .ToArray();
                ChildProperties[node.GetType()] = properties;
            }
        }

        foreach (PropertyInfo property in properties)
        {
            object? value = property.GetValue(node);
            foreach (object child in Flatten(value))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<object> Flatten(object? value)
    {
        switch (value)
        {
            case null or string:
                yield break;
            case Node or SwitchCase:
                yield return value;
                yield break;
            case IEnumerable items:
                foreach (object? item in items)
                {
                    foreach (object child in Flatten(item))
                    {
                        yield return child;
                    }
                }

                yield break;
        }
    }

    // --- Emit -----------------------------------------------------------------------------------

    private int AllocVReg()
    {
        int reg = _nextVReg++;
        if (_nextVReg > _vregCount)
        {
            _vregCount = _nextVReg;
        }

        return reg;
    }

    private int NodeIndex(Node node)
    {
        if (_nodes.Count >= ushort.MaxValue)
        {
            throw Refuse();
        }

        _nodes.Add(node);
        return _nodes.Count - 1;
    }

    /// <summary>Emits a vector-valued expression and answers the vector register holding its value.</summary>
    private int EmitVectorExpr(Expr expression, BailScope bail)
    {
        switch (expression)
        {
            case VariableExpr variable:
                return _vslotOf[variable.Name];

            case UnaryExpr { Op: TokenType.Minus } minus:
            {
                int operand = EmitVectorExpr(minus.Operand, bail);
                int dest = AllocVReg();
                EmitOp(LoopOp.VNeg, dest: dest, a: operand, arg: EnsureBail(bail), c: NodeIndex(minus));
                return dest;
            }

            case BinaryExpr binary:
            {
                bool leftVector = IsVectorExpr(binary.Left);
                bool rightVector = IsVectorExpr(binary.Right);
                int left = leftVector ? EmitVectorExpr(binary.Left, bail) : EmitExpr(binary.Left, bail).Reg;
                int right = rightVector ? EmitVectorExpr(binary.Right, bail) : EmitExpr(binary.Right, bail).Reg;
                int dest = AllocVReg();
                LoopOp op = leftVector && rightVector ? LoopOp.VArithVV : leftVector ? LoopOp.VArithVS : LoopOp.VArithSV;
                EmitOp(op, dest: dest, a: left, b: right, arg: EnsureBail(bail), c: NodeIndex(binary));
                return dest;
            }

            case CallExpr { Callee: VariableExpr callee } call:
            {
                int operand = EmitVectorExpr(call.Arguments[0], bail);
                int dest = AllocVReg();
                EmitOp(LoopOp.VCall1, dest: dest, a: operand, b: _unaryIndex[callee.Name], arg: EnsureBail(bail), c: NodeIndex(call));
                return dest;
            }

            default:
                throw Refuse(); // unreachable: the scan vetted every vector expression
        }
    }

    /// <summary>A statement the walk runs: spill, execute, reload, resume — every time.</summary>
    private void EmitWalk(Stmt statement, LoopLabels labels)
    {
        var scope = new BailScope
        {
            Kind = LoopBailKind.Statement,
            Statement = statement,
            Labels = labels,
        };
        EmitOp(LoopOp.Walk, arg: EnsureBail(scope));
        int resume = NewLabel();
        MarkLabel(resume);
        var touched = new List<int>();
        var touchedVectors = new List<int>();
        foreach (string name in AssignedNames(statement))
        {
            if (_slotOf.TryGetValue(name, out int slot))
            {
                touched.Add(slot);
            }
            else if (_vslotOf.TryGetValue(name, out int vslot))
            {
                touchedVectors.Add(vslot);
            }
        }

        FinishStatementBail(scope, resume, [.. touched], [.. touchedVectors], walked: true);
    }

    /// <summary>
    /// Every name a statement may assign, by its syntax: the root of each assignment target, each
    /// multi-assignment target, each nested loop's variable, a try's error variable. An
    /// over-approximation — a conditional assignment inside counts — which is the safe direction.
    /// </summary>
    private static IEnumerable<string> AssignedNames(Stmt statement)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Collect(statement);
        return names;

        void Collect(object node)
        {
            switch (node)
            {
                case AssignExpr assign when RootName(assign.Target) is { } target:
                    names.Add(target);
                    break;
                case IncDecExpr incDec when RootName(incDec.Target) is { } target:
                    names.Add(target);
                    break;
                case MultiAssignStmt multi:
                    foreach (Expr? target in multi.Targets)
                    {
                        if (target is not null && RootName(target) is { } name)
                        {
                            names.Add(name);
                        }
                    }

                    break;
                case ForStmt loop:
                    names.Add(loop.Variable);
                    break;
                case TryStmt { ErrorVariable: { } error }:
                    names.Add(error);
                    break;
            }

            foreach (object child in ChildrenOf(node))
            {
                Collect(child);
            }
        }
    }

    /// <summary>The variable an assignment target ultimately writes: <c>x</c> for <c>x</c>, <c>x(i)</c>, <c>x{i}</c>, <c>x.f(i).g</c>.</summary>
    private static string? RootName(Expr target) => target switch
    {
        VariableExpr variable => variable.Name,
        CallExpr call => RootName(call.Callee),
        IndexExpr index => RootName(index.Target),
        BraceIndexExpr brace => RootName(brace.Target),
        MemberExpr member => RootName(member.Target),
        _ => null,
    };
}
