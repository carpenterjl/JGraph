namespace JGraph.Scripting.Jgs;

/// <summary>
/// Compiles a MATLAB <c>for</c> or <c>while</c> whose body works entirely in scalar doubles into a
/// <see cref="RegisterProgram"/> (M98). The compiler is a whitelist: assignments to plain variables,
/// <c>if</c>/<c>elseif</c>/<c>else</c>, nested loops over ranges, <c>break</c>/<c>continue</c>,
/// arithmetic, comparisons, logic, and calls to the scalar builtins
/// <see cref="JgsBuiltins.TryHotLoopUnary"/> names. Anything else refuses — the loop stays on the
/// tree walk, which is always correct. Every op binds the same arithmetic the walk applies, and the
/// cases a register cannot hold (an answer that leaves the reals, an error the walk would throw)
/// bail back to the walk mid-run rather than being reimplemented.
/// </summary>
internal sealed class LoopCompiler
{
    /// <summary>The registers a for loop's state occupies, in order: start, step, stop, count, index.</summary>
    private const int ForStateSize = 5;

    private readonly List<RegOp> _ops = [];
    private readonly List<LoopSlot> _slots = [];
    private readonly Dictionary<string, int> _slotOf = new(StringComparer.Ordinal);
    private readonly List<double> _constValues = [];
    private readonly Dictionary<double, int> _constOf = [];
    private readonly List<Func<double, double>> _unary = [];
    private readonly List<Func<double, bool>?> _unaryGuard = [];
    private readonly Dictionary<string, int> _unaryIndex = new(StringComparer.Ordinal);
    private readonly HashSet<string> _requiredBuiltins = new(StringComparer.Ordinal);
    private readonly HashSet<string> _calledNames = new(StringComparer.Ordinal);
    private readonly List<LoopBail> _bails = [];
    private readonly List<int> _labels = [];
    private readonly List<(int Op, int Label)> _patches = [];
    private readonly List<(LoopBail Bail, int Resume, int OnTrue, int OnFalse, int BreakAt, int ContinueAt)> _bailLabels = [];
    private readonly List<LoopDeoptFrame> _frames = [];

    private int _constBase;
    private int _frozenConstCount;
    private int _nextReg;
    private int _registerCount;
    private int _outerRegBase = -1;

    private LoopCompiler()
    {
    }

    /// <summary>
    /// Compiles <paramref name="root"/> (a <see cref="ForStmt"/> over a range, or a
    /// <see cref="WhileStmt"/>), or answers null when anything in it is outside the whitelist.
    /// </summary>
    public static RegisterProgram? Compile(Stmt root)
    {
        var compiler = new LoopCompiler();
        try
        {
            return compiler.Build(root);
        }
        catch (RefusedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the statements under <paramref name="program"/>'s root are still the exact objects the
    /// compilation consumed. A debug hook can edit statement lists in place, so a cached program
    /// re-checks before every run and a mismatch recompiles.
    /// </summary>
    public static bool SnapshotMatches(RegisterProgram program)
    {
        var current = new List<Stmt>();
        CollectStatements(program.Root, current);
        if (current.Count != program.Snapshot.Length)
        {
            return false;
        }

        for (int i = 0; i < current.Count; i++)
        {
            if (!ReferenceEquals(current[i], program.Snapshot[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Every statement under <paramref name="node"/> in preorder — the identity the snapshot pins.</summary>
    private static void CollectStatements(Stmt node, List<Stmt> into)
    {
        into.Add(node);
        switch (node)
        {
            case IfStmt ifStmt:
                foreach (Stmt s in ifStmt.Then)
                {
                    CollectStatements(s, into);
                }

                if (ifStmt.Else is not null)
                {
                    foreach (Stmt s in ifStmt.Else)
                    {
                        CollectStatements(s, into);
                    }
                }

                break;
            case ForStmt forStmt:
                foreach (Stmt s in forStmt.Body)
                {
                    CollectStatements(s, into);
                }

                break;
            case WhileStmt whileStmt:
                foreach (Stmt s in whileStmt.Body)
                {
                    CollectStatements(s, into);
                }

                break;
        }
    }

    // --- The two passes -----------------------------------------------------------------------

    private RegisterProgram? Build(Stmt root)
    {
        // Pass 1: walk the whole loop, refuse anything outside the whitelist, allot variable slots
        // in order of first appearance, pool the literal constants, and mark the variables whose
        // pre-loop value some read may see (they must hold plain real scalars at entry).
        Constant(0);
        Constant(1);
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        switch (root)
        {
            case ForStmt forStmt:
                if (forStmt.Iterable is not RangeExpr range)
                {
                    return null; // only a range iterates without materializing; anything else walks
                }

                // The outer bounds are evaluated once by the runner with the full evaluator, so they
                // may be arbitrary expressions; only the range's *shape* is required here. A step-less
                // range still needs the constant 1 (already pooled above).
                _ = range;
                DeclareLoopVariable(forStmt.Variable);
                assigned.Add(forStmt.Variable);
                ScanBlock(forStmt.Body, assigned);
                break;

            case WhileStmt whileStmt:
                ScanExpr(whileStmt.Condition, assigned);
                ScanBlock(whileStmt.Body, [.. assigned]);
                break;

            default:
                return null;
        }

        // A name cannot be both a callee and a variable: the first assignment would shadow the
        // builtin mid-loop, and only the walk gets that right.
        foreach (string name in _calledNames)
        {
            if (_slotOf.ContainsKey(name))
            {
                return null;
            }
        }

        // Pass 2: emit. Registers are variables, then the constant pool, then loop state and
        // scratch; scratch is reclaimed at every statement boundary.
        _constBase = _slots.Count;
        _frozenConstCount = _constValues.Count;
        _nextReg = _constBase + _constValues.Count;
        _registerCount = _nextReg;

        var snapshot = new List<Stmt>();
        CollectStatements(root, snapshot);

        switch (root)
        {
            case ForStmt forStmt:
                EmitForRoot(forStmt);
                break;
            case WhileStmt whileStmt:
                EmitWhileRoot(whileStmt);
                break;
        }

        foreach ((int op, int label) in _patches)
        {
            RegOp old = _ops[op];
            _ops[op] = new RegOp(old.Code, old.Dest, old.A, old.B, _labels[label]);
        }

        foreach ((LoopBail bail, int resume, int onTrue, int onFalse, int breakAt, int continueAt) in _bailLabels)
        {
            bail.Resume = resume >= 0 ? _labels[resume] : -1;
            bail.OnTrue = onTrue >= 0 ? _labels[onTrue] : -1;
            bail.OnFalse = onFalse >= 0 ? _labels[onFalse] : -1;
            bail.BreakIp = breakAt >= 0 ? _labels[breakAt] : -1;
            bail.ContinueIp = continueAt >= 0 ? _labels[continueAt] : -1;
        }

        return new RegisterProgram
        {
            Ops = [.. _ops],
            Slots = [.. _slots],
            Constants = [.. _constValues],
            ConstBase = _constBase,
            RegisterCount = _registerCount,
            Unary = [.. _unary],
            UnaryGuard = [.. _unaryGuard],
            RequiredBuiltins = [.. _requiredBuiltins],
            Bails = [.. _bails],
            OuterRegBase = _outerRegBase,
            Root = root,
            Snapshot = [.. snapshot],
        };
    }

    // --- Pass 1: scan, refuse, and mark entry-required variables --------------------------------

    private sealed class RefusedException : Exception
    {
    }

    private static Exception Refuse() => throw new RefusedException();

    private void ScanBlock(IReadOnlyList<Stmt> statements, HashSet<string> assigned)
    {
        foreach (Stmt statement in statements)
        {
            ScanStatement(statement, assigned);
        }
    }

    private void ScanStatement(Stmt statement, HashSet<string> assigned)
    {
        switch (statement)
        {
            case ExprStmt { Suppressed: true, Expression: AssignExpr { Target: VariableExpr target } assign }
                when assign.Op is TokenType.Assign or TokenType.PlusAssign or TokenType.MinusAssign
                    or TokenType.StarAssign or TokenType.SlashAssign:
                ScanExpr(assign.Value, assigned);
                if (assign.Op != TokenType.Assign)
                {
                    ReadVariable(target.Name, assigned); // the compound form reads before it writes
                }

                DeclareVariable(target.Name);
                assigned.Add(target.Name);
                return;

            case IfStmt ifStmt:
            {
                ScanExpr(ifStmt.Condition, assigned);
                var then = new HashSet<string>(assigned, StringComparer.Ordinal);
                ScanBlock(ifStmt.Then, then);
                if (ifStmt.Else is null)
                {
                    return; // the else path assigns nothing, so the merged set is the incoming one
                }

                var other = new HashSet<string>(assigned, StringComparer.Ordinal);
                ScanBlock(ifStmt.Else, other);
                then.IntersectWith(other);
                assigned.UnionWith(then);
                return;
            }

            case ForStmt { Iterable: RangeExpr nested } forStmt:
            {
                // A nested range's bounds run inside the program, so unlike the root's they must be
                // compilable (and are therefore pure — a bail may re-evaluate them).
                ScanExpr(nested.Start, assigned);
                if (nested.Step is not null)
                {
                    ScanExpr(nested.Step, assigned);
                }

                ScanExpr(nested.Stop, assigned);
                DeclareLoopVariable(forStmt.Variable);
                var body = new HashSet<string>(assigned, StringComparer.Ordinal) { forStmt.Variable };
                ScanBlock(forStmt.Body, body);
                return; // zero iterations are possible, so nothing it assigns is definite after it
            }

            case WhileStmt whileStmt:
            {
                ScanExpr(whileStmt.Condition, assigned);
                var body = new HashSet<string>(assigned, StringComparer.Ordinal);
                ScanBlock(whileStmt.Body, body);
                return;
            }

            case BreakStmt or ContinueStmt:
                return;

            default:
                throw Refuse();
        }
    }

    private void ScanExpr(Expr expression, HashSet<string> assigned)
    {
        switch (expression)
        {
            case NumberLiteral number:
                Constant(number.Value);
                return;

            case BoolLiteral:
                return; // 0 and 1 are already pooled

            case VariableExpr variable:
                ReadVariable(variable.Name, assigned);
                return;

            case UnaryExpr { Op: TokenType.Minus or TokenType.Bang } unary:
                ScanExpr(unary.Operand, assigned);
                return;

            case BinaryExpr binary when IsCompilableBinary(binary.Op):
                ScanExpr(binary.Left, assigned);
                ScanExpr(binary.Right, assigned);
                return;

            case LogicalExpr logical:
                ScanExpr(logical.Left, assigned);
                ScanExpr(logical.Right, assigned);
                return;

            case CallExpr { Callee: VariableExpr callee } call:
                if (call.Arguments.Count == 1 && JgsBuiltins.TryHotLoopUnary(callee.Name, out Func<double, double> kernel, out Func<double, bool>? staysReal))
                {
                    if (!_unaryIndex.ContainsKey(callee.Name))
                    {
                        _unaryIndex[callee.Name] = _unary.Count;
                        _unary.Add(kernel);
                        _unaryGuard.Add(staysReal);
                    }
                }
                else if (call.Arguments.Count != 2 || !JgsBuiltins.IsHotLoopBinary(callee.Name))
                {
                    throw Refuse();
                }

                _calledNames.Add(callee.Name);
                _requiredBuiltins.Add(callee.Name);
                foreach (Expr argument in call.Arguments)
                {
                    ScanExpr(argument, assigned);
                }

                return;

            default:
                throw Refuse();
        }
    }

    private static bool IsCompilableBinary(TokenType op) => op is TokenType.Plus or TokenType.Minus
        or TokenType.Star or TokenType.Slash or TokenType.Caret
        or TokenType.DotStar or TokenType.DotSlash or TokenType.DotCaret
        or TokenType.Backslash or TokenType.DotBackslash
        or TokenType.Less or TokenType.LessEqual or TokenType.Greater or TokenType.GreaterEqual
        or TokenType.EqualEqual or TokenType.BangEqual
        or TokenType.Amp or TokenType.Pipe;

    private void ReadVariable(string name, HashSet<string> assigned)
    {
        int slot = DeclareVariable(name);
        if (!assigned.Contains(name))
        {
            // Some path reaches this read before any compiled write, so the pre-loop binding is what
            // it sees — the entry check requires it to be a plain real scalar (or a constant builtin
            // such as pi mentioned bare) and refuses the fast path otherwise.
            _slots[slot].EntryRequired = true;
        }
    }

    private int DeclareVariable(string name)
    {
        if (!_slotOf.TryGetValue(name, out int slot))
        {
            slot = _slots.Count;
            _slotOf[name] = slot;
            _slots.Add(new LoopSlot(name));
        }

        return slot;
    }

    private void DeclareLoopVariable(string name) => _slots[DeclareVariable(name)].IsLoopVariable = true;

    private int Constant(double value)
    {
        if (!_constOf.TryGetValue(value, out int index))
        {
            index = _constValues.Count;
            _constOf[value] = index;
            _constValues.Add(value);
        }

        return index;
    }

    // --- Pass 2: emit -------------------------------------------------------------------------

    /// <summary>Where a break or continue lands while emitting a loop body.</summary>
    private readonly record struct LoopLabels(int BreakLabel, int ContinueLabel);

    /// <summary>
    /// What a guarded op bails to, created lazily on the first guarded op inside the statement,
    /// condition, or bound it belongs to.
    /// </summary>
    private sealed class BailScope
    {
        public LoopBailKind Kind;
        public Stmt? Statement;
        public Expr? Expression;
        public string What = "";
        public int DestReg;
        public int ResumeLabel = -1;
        public int OnTrueLabel = -1;
        public int OnFalseLabel = -1;
        public LoopLabels Labels;
        public int Id = -1;
    }

    private int EmitOp(LoopOp code, int dest = 0, int a = 0, int b = 0, int arg = 0)
    {
        _ops.Add(new RegOp(code, (ushort)dest, (ushort)a, (ushort)b, arg));
        return _ops.Count - 1;
    }

    private int NewLabel()
    {
        _labels.Add(-1);
        return _labels.Count - 1;
    }

    private void MarkLabel(int label) => _labels[label] = _ops.Count;

    private void EmitJump(LoopOp code, int label, int a = 0)
    {
        _patches.Add((EmitOp(code, a: a, arg: -1), label));
    }

    private int AllocReg()
    {
        int reg = _nextReg++;
        if (_nextReg > _registerCount)
        {
            _registerCount = _nextReg;
        }

        return reg;
    }

    /// <summary>
    /// The register a pooled constant lives in. Pass 1 pools every constant the tree can mention;
    /// one first seen during emit would land its register inside scratch space, so that is a bug
    /// here and not a request.
    /// </summary>
    private int ConstReg(double value)
    {
        int index = Constant(value);
        return index < _frozenConstCount
            ? _constBase + index
            : throw new InvalidOperationException($"Constant {value} was not pooled by the scan pass.");
    }

    private void EmitForRoot(ForStmt root)
    {
        _outerRegBase = _nextReg;
        _nextReg += ForStateSize;
        _registerCount = Math.Max(_registerCount, _nextReg);
        _frames.Add(new LoopDeoptFrame { Block = root.Body, Index = 0, For = root, RegBase = _outerRegBase });
        EmitForLoop(root, _outerRegBase);
        EmitOp(LoopOp.Halt);
    }

    private void EmitWhileRoot(WhileStmt root)
    {
        _frames.Add(new LoopDeoptFrame { Block = root.Body, Index = 0, While = root });
        EmitWhileLoop(root);
        EmitOp(LoopOp.Halt);
    }

    /// <summary>The head, bind, body and back edge of a for loop whose state block is already filled (or primed by the runner).</summary>
    private void EmitForLoop(ForStmt loop, int stateBase)
    {
        int head = NewLabel();
        int next = NewLabel();
        int exit = NewLabel();
        MarkLabel(head);
        EmitJump(LoopOp.ForHead, exit, a: stateBase);
        EmitOp(LoopOp.IterTick);
        EmitOp(LoopOp.ForBind, dest: _slotOf[loop.Variable], a: stateBase);
        EmitBody(loop.Body, new LoopLabels(exit, next));
        MarkLabel(next);
        EmitJump(LoopOp.ForNext, head, a: stateBase);
        MarkLabel(exit);
    }

    private void EmitWhileLoop(WhileStmt loop)
    {
        int head = NewLabel();
        int body = NewLabel();
        int exit = NewLabel();
        MarkLabel(head);
        EmitCondition(loop.Condition, body, exit, new LoopLabels(exit, head));
        MarkLabel(body);
        EmitOp(LoopOp.IterTick);
        EmitBody(loop.Body, new LoopLabels(exit, head));
        EmitJump(LoopOp.Jump, head);
        MarkLabel(exit);
    }

    private void EmitBody(IReadOnlyList<Stmt> statements, LoopLabels labels)
    {
        int level = _frames.Count - 1;
        LoopDeoptFrame enclosing = _frames[level];
        for (int i = 0; i < statements.Count; i++)
        {
            // The frame path records which statement of this block the deeper nesting sits inside,
            // so a bail can finish the block from the right place by the walk.
            _frames[level] = new LoopDeoptFrame
            {
                Block = statements,
                Index = i,
                For = enclosing.For,
                While = enclosing.While,
                RegBase = enclosing.RegBase,
            };
            EmitStatement(statements[i], labels);
        }

        _frames[level] = enclosing;
    }

    private void EmitStatement(Stmt statement, LoopLabels labels)
    {
        int scratch = _nextReg;
        EmitOp(LoopOp.Step);
        switch (statement)
        {
            case ExprStmt { Expression: AssignExpr { Target: VariableExpr target } assign }:
            {
                var scope = new BailScope
                {
                    Kind = LoopBailKind.Statement,
                    Statement = statement,
                    Labels = labels,
                };
                int slot = _slotOf[target.Name];
                if (assign.Op == TokenType.Assign && assign.Value is VariableExpr source)
                {
                    EmitOp(LoopOp.BindVar, dest: slot, a: _slotOf[source.Name]);
                }
                else
                {
                    (int value, bool isLogical) = assign.Op == TokenType.Assign
                        ? EmitExpr(assign.Value, scope)
                        : EmitCompound(assign, slot, scope);
                    EmitOp(LoopOp.Bind, dest: slot, a: value, b: isLogical ? 1 : 0);
                }

                int resume = NewLabel();
                MarkLabel(resume);
                FinishStatementBail(scope, resume, slot);
                break;
            }

            case IfStmt ifStmt:
            {
                int then = NewLabel();
                int otherwise = NewLabel();
                int end = ifStmt.Else is null ? otherwise : NewLabel();
                EmitCondition(ifStmt.Condition, then, otherwise, labels);
                MarkLabel(then);
                EmitNested(ifStmt.Then, labels, forStmt: null, whileStmt: null, regBase: 0);
                if (ifStmt.Else is not null)
                {
                    EmitJump(LoopOp.Jump, end);
                    MarkLabel(otherwise);
                    EmitNested(ifStmt.Else, labels, forStmt: null, whileStmt: null, regBase: 0);
                }

                MarkLabel(end);
                break;
            }

            case ForStmt { Iterable: RangeExpr range } forStmt:
            {
                var scope = new BailScope
                {
                    Kind = LoopBailKind.Statement,
                    Statement = statement,
                    Labels = labels,
                };
                int stateBase = _nextReg;
                _nextReg += ForStateSize;
                _registerCount = Math.Max(_registerCount, _nextReg);
                EmitBound(range.Start, "start", stateBase, labels);
                if (range.Step is null)
                {
                    EmitOp(LoopOp.Copy, dest: stateBase + 1, a: ConstReg(1));
                }
                else
                {
                    EmitBound(range.Step, "step", stateBase + 1, labels);
                }

                EmitBound(range.Stop, "stop", stateBase + 2, labels);
                EmitOp(LoopOp.RangeCount, a: stateBase, arg: EnsureBail(scope));
                EmitNestedLoop(forStmt, stateBase);
                int resume = NewLabel();
                MarkLabel(resume);
                FinishStatementBail(scope, resume, targetSlot: -1);
                break;
            }

            case WhileStmt whileStmt:
                EmitNestedLoop(whileStmt, 0);
                break;

            case BreakStmt:
                EmitJump(LoopOp.Jump, labels.BreakLabel);
                break;

            case ContinueStmt:
                EmitJump(LoopOp.Jump, labels.ContinueLabel);
                break;

            default:
                throw Refuse(); // unreachable: pass 1 vetted every statement
        }

        _nextReg = scratch;
    }

    private void EmitNested(IReadOnlyList<Stmt> block, LoopLabels labels, ForStmt? forStmt, WhileStmt? whileStmt, int regBase)
    {
        _frames.Add(new LoopDeoptFrame { Block = block, Index = 0, For = forStmt, While = whileStmt, RegBase = regBase });
        EmitBody(block, labels);
        _frames.RemoveAt(_frames.Count - 1);
    }

    private void EmitNestedLoop(Stmt loop, int stateBase)
    {
        switch (loop)
        {
            case ForStmt forStmt:
            {
                int head = NewLabel();
                int next = NewLabel();
                int exit = NewLabel();
                MarkLabel(head);
                EmitJump(LoopOp.ForHead, exit, a: stateBase);
                EmitOp(LoopOp.IterTick);
                EmitOp(LoopOp.ForBind, dest: _slotOf[forStmt.Variable], a: stateBase);
                _frames.Add(new LoopDeoptFrame { Block = forStmt.Body, Index = 0, For = forStmt, RegBase = stateBase });
                EmitBody(forStmt.Body, new LoopLabels(exit, next));
                _frames.RemoveAt(_frames.Count - 1);
                MarkLabel(next);
                EmitJump(LoopOp.ForNext, head, a: stateBase);
                MarkLabel(exit);
                break;
            }

            case WhileStmt whileStmt:
            {
                int head = NewLabel();
                int body = NewLabel();
                int exit = NewLabel();
                MarkLabel(head);
                EmitCondition(whileStmt.Condition, body, exit, new LoopLabels(exit, head));
                MarkLabel(body);
                EmitOp(LoopOp.IterTick);
                _frames.Add(new LoopDeoptFrame { Block = whileStmt.Body, Index = 0, While = whileStmt });
                EmitBody(whileStmt.Body, new LoopLabels(exit, head));
                _frames.RemoveAt(_frames.Count - 1);
                EmitJump(LoopOp.Jump, head);
                MarkLabel(exit);
                break;
            }
        }
    }

    /// <summary>
    /// One range bound, deposited into its state register. A guarded op inside it bails to a record
    /// that re-evaluates just this bound by the walk — the walk's own scalar check and error included.
    /// </summary>
    private void EmitBound(Expr bound, string what, int destReg, LoopLabels labels)
    {
        int scratch = _nextReg;
        var scope = new BailScope
        {
            Kind = LoopBailKind.Bound,
            Expression = bound,
            What = what,
            DestReg = destReg,
            Labels = labels,
        };
        (int value, _) = EmitExpr(bound, scope);
        EmitOp(LoopOp.Copy, dest: destReg, a: value);
        int resume = NewLabel();
        MarkLabel(resume);
        if (scope.Id >= 0)
        {
            _bailLabels.Add((_bails[scope.Id], resume, -1, -1, -1, -1));
        }

        _nextReg = scratch;
    }

    /// <summary>
    /// A condition: its value ops, then the branch. False jumps to <paramref name="onFalse"/>; true
    /// falls through, so every caller marks <paramref name="onTrue"/> on the very next op. A guarded
    /// op inside the condition bails to a record that re-evaluates the whole condition by the walk
    /// and branches on its truthiness.
    /// </summary>
    private void EmitCondition(Expr condition, int onTrue, int onFalse, LoopLabels labels)
    {
        _ = labels;
        int scratch = _nextReg;
        var scope = new BailScope
        {
            Kind = LoopBailKind.Condition,
            Expression = condition,
            OnTrueLabel = onTrue,
            OnFalseLabel = onFalse,
        };
        (int value, _) = EmitExpr(condition, scope);
        EmitJump(LoopOp.JumpIfFalse, onFalse, a: value);
        if (scope.Id >= 0)
        {
            _bailLabels.Add((_bails[scope.Id], -1, onTrue, onFalse, -1, -1));
        }

        _nextReg = scratch;
    }

    private void FinishStatementBail(BailScope scope, int resumeLabel, int targetSlot)
    {
        if (scope.Id < 0)
        {
            return;
        }

        LoopBail bail = _bails[scope.Id];
        _bails[scope.Id] = new LoopBail
        {
            Kind = bail.Kind,
            Statement = bail.Statement,
            Expression = bail.Expression,
            What = bail.What,
            DestReg = targetSlot,
            Path = bail.Path,
        };
        _bailLabels.Add((_bails[scope.Id], resumeLabel, -1, -1, scope.Labels.BreakLabel, scope.Labels.ContinueLabel));
    }

    private int EnsureBail(BailScope scope)
    {
        if (scope.Id >= 0)
        {
            return scope.Id;
        }

        scope.Id = _bails.Count;
        _bails.Add(new LoopBail
        {
            Kind = scope.Kind,
            Statement = scope.Statement,
            Expression = scope.Expression,
            What = scope.What,
            DestReg = scope.Kind == LoopBailKind.Bound ? scope.DestReg : -1,
            Path = scope.Kind == LoopBailKind.Statement ? [.. _frames] : [],
        });
        // A statement's resume and break/continue targets are attached when it finishes emitting;
        // a condition's branch targets and a bound's resume are attached by their own emitters.
        return scope.Id;
    }

    // --- Expressions --------------------------------------------------------------------------

    /// <summary>
    /// Emits <paramref name="expression"/>'s ops and answers the register holding its value, plus
    /// whether that value is statically a logical (the Bool the walk would mint) — what spill needs
    /// to rebuild the same JgsValue.
    /// </summary>
    private (int Reg, bool IsLogical) EmitExpr(Expr expression, BailScope bail)
    {
        switch (expression)
        {
            case NumberLiteral number:
                return (ConstReg(number.Value), false);

            case BoolLiteral boolean:
                return (ConstReg(boolean.Value ? 1 : 0), true);

            case VariableExpr variable:
                return (_slotOf[variable.Name], false); // spill kind for a bare read rides on the slot itself

            case UnaryExpr { Op: TokenType.Minus } minus:
            {
                (int operand, _) = EmitExpr(minus.Operand, bail);
                int dest = AllocReg();
                EmitOp(LoopOp.Neg, dest: dest, a: operand);
                return (dest, false);
            }

            case UnaryExpr { Op: TokenType.Bang } not:
            {
                (int operand, _) = EmitExpr(not.Operand, bail);
                int dest = AllocReg();
                EmitOp(LoopOp.Not, dest: dest, a: operand);
                return (dest, true);
            }

            case BinaryExpr binary:
                return EmitBinary(binary, bail);

            case LogicalExpr logical:
                return EmitLogical(logical, bail);

            case CallExpr { Callee: VariableExpr callee } call:
                return EmitCall(callee.Name, call, bail);

            default:
                throw Refuse(); // unreachable: pass 1 vetted every expression
        }
    }

    private (int Reg, bool IsLogical) EmitBinary(BinaryExpr binary, BailScope bail)
    {
        // a .\ b is b ./ a, and for scalars a \ b is b / a too — the swap the walk itself performs.
        (Expr leftExpr, Expr rightExpr) = binary.Op is TokenType.Backslash or TokenType.DotBackslash
            ? (binary.Right, binary.Left)
            : (binary.Left, binary.Right);
        (int left, _) = EmitExpr(leftExpr, bail);
        (int right, _) = EmitExpr(rightExpr, bail);
        int dest = AllocReg();
        switch (binary.Op)
        {
            case TokenType.Plus:
                EmitOp(LoopOp.Add, dest, left, right);
                return (dest, false);
            case TokenType.Minus:
                EmitOp(LoopOp.Sub, dest, left, right);
                return (dest, false);
            case TokenType.Star or TokenType.DotStar:
                EmitOp(LoopOp.Mul, dest, left, right);
                return (dest, false);
            case TokenType.Slash or TokenType.DotSlash or TokenType.Backslash or TokenType.DotBackslash:
                EmitOp(LoopOp.Div, dest, left, right);
                return (dest, false);
            case TokenType.Caret or TokenType.DotCaret:
                EmitOp(LoopOp.PowG, dest, left, right, EnsureBail(bail));
                return (dest, false);
            case TokenType.Less:
                EmitOp(LoopOp.Lt, dest, left, right);
                return (dest, true);
            case TokenType.LessEqual:
                EmitOp(LoopOp.Le, dest, left, right);
                return (dest, true);
            case TokenType.Greater:
                EmitOp(LoopOp.Gt, dest, left, right);
                return (dest, true);
            case TokenType.GreaterEqual:
                EmitOp(LoopOp.Ge, dest, left, right);
                return (dest, true);
            case TokenType.EqualEqual:
                EmitOp(LoopOp.Eq, dest, left, right);
                return (dest, true);
            case TokenType.BangEqual:
                EmitOp(LoopOp.Ne, dest, left, right);
                return (dest, true);
            case TokenType.Amp:
                EmitOp(LoopOp.And, dest, left, right);
                return (dest, true);
            case TokenType.Pipe:
                EmitOp(LoopOp.Or, dest, left, right);
                return (dest, true);
            default:
                throw Refuse();
        }
    }

    private (int Reg, bool IsLogical) EmitLogical(LogicalExpr logical, BailScope bail)
    {
        int dest = AllocReg();
        int shortcut = NewLabel();
        int end = NewLabel();
        (int left, _) = EmitExpr(logical.Left, bail);
        if (logical.Op == TokenType.AmpAmp)
        {
            EmitJump(LoopOp.JumpIfFalse, shortcut, a: left);
            (int right, _) = EmitExpr(logical.Right, bail);
            EmitOp(LoopOp.ToBool, dest: dest, a: right);
            EmitJump(LoopOp.Jump, end);
            MarkLabel(shortcut);
            EmitOp(LoopOp.Copy, dest: dest, a: ConstReg(0));
        }
        else
        {
            EmitJump(LoopOp.JumpIfTrue, shortcut, a: left);
            (int right, _) = EmitExpr(logical.Right, bail);
            EmitOp(LoopOp.ToBool, dest: dest, a: right);
            EmitJump(LoopOp.Jump, end);
            MarkLabel(shortcut);
            EmitOp(LoopOp.Copy, dest: dest, a: ConstReg(1));
        }

        MarkLabel(end);
        return (dest, true);
    }

    private (int Reg, bool IsLogical) EmitCall(string name, CallExpr call, BailScope bail)
    {
        if (call.Arguments.Count == 1)
        {
            int index = _unaryIndex[name];
            (int argument, _) = EmitExpr(call.Arguments[0], bail);
            int dest = AllocReg();
            if (_unaryGuard[index] is null)
            {
                EmitOp(LoopOp.Call1, dest, argument, b: index);
            }
            else
            {
                EmitOp(LoopOp.Call1G, dest, argument, b: index, arg: EnsureBail(bail));
            }

            return (dest, false);
        }

        (int first, _) = EmitExpr(call.Arguments[0], bail);
        (int second, _) = EmitExpr(call.Arguments[1], bail);
        int result = AllocReg();
        LoopOp op = name switch
        {
            "mod" => LoopOp.Mod,
            "rem" => LoopOp.Rem,
            "atan2" => LoopOp.Atan2,
            "min" => LoopOp.Min2,
            "max" => LoopOp.Max2,
            _ => throw Refuse(),
        };
        EmitOp(op, result, first, second);
        return (result, false);
    }

    private (int Reg, bool IsLogical) EmitCompound(AssignExpr assign, int slot, BailScope bail)
    {
        // The walk evaluates the right side first, then reads the target, then combines — the same
        // order here, with the combine being the plain operator the compound form names.
        (int rhs, _) = EmitExpr(assign.Value, bail);
        int dest = AllocReg();
        switch (assign.Op)
        {
            case TokenType.PlusAssign:
                EmitOp(LoopOp.Add, dest, slot, rhs);
                break;
            case TokenType.MinusAssign:
                EmitOp(LoopOp.Sub, dest, slot, rhs);
                break;
            case TokenType.StarAssign:
                EmitOp(LoopOp.Mul, dest, slot, rhs);
                break;
            case TokenType.SlashAssign:
                EmitOp(LoopOp.Div, dest, slot, rhs);
                break;
            default:
                throw Refuse();
        }

        return (dest, false);
    }
}
