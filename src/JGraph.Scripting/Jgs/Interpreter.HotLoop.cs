namespace JGraph.Scripting.Jgs;

/// <summary>
/// The hot-loop runner (M98): executes a <see cref="RegisterProgram"/> over an unboxed double
/// register file, spilling to the environment exactly where the tree walk would have left its
/// variables — at completion, at a bail, at the step limit, on cancellation. The compiled ops and
/// the walk share every piece of arithmetic, so the two roads print the same bytes; the cases a
/// register cannot hold hand one statement (or one condition) back to the walk and carry on.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>The step-limit error, verbatim from <see cref="Tick"/> — the compiled loop must throw the same words.</summary>
    private const string StepLimitMessage =
        "Step limit exceeded (the script ran too long — check for an infinite loop).";

    /// <summary>The epsilon <c>EvaluateRange</c> counts with; the compiled count must be the same number.</summary>
    private const double RangeEpsilon = 2.220446049250313e-16;

    /// <summary>Compiled loops by their AST node; null records a refusal so it is not retried.</summary>
    private Dictionary<Stmt, RegisterProgram?>? _loopPrograms;

    /// <summary>
    /// Runs <paramref name="loop"/> as a register program when it compiles and its variables qualify.
    /// False means the caller walks the loop as it always has — the compiler refused, a variable is
    /// not a plain real scalar, a name is shadowed, or the fast path is off.
    /// </summary>
    private bool TryExecuteHotLoop(Stmt loop, JgsEnvironment env, out Completion completion)
    {
        completion = Completion.Normal;
        if (!JgsLoopJit.Enabled || _hook is not null
            || !Dialect.IsMatlab || !Dialect.FunctionScope || Dialect.RequireLet)
        {
            return false;
        }

        _loopPrograms ??= [];
        if (!_loopPrograms.TryGetValue(loop, out RegisterProgram? program)
            || (program is not null && !LoopCompiler.SnapshotMatches(program)))
        {
            program = LoopCompiler.Compile(loop);
            _loopPrograms[loop] = program;
        }

        if (program is null)
        {
            return false;
        }

        var regs = new double[program.RegisterCount];
        var written = new bool[program.Slots.Length];
        var logical = new bool[program.Slots.Length];
        for (int i = 0; i < program.Constants.Length; i++)
        {
            regs[program.ConstBase + i] = program.Constants[i];
        }

        if (!TryLoadHotLoopEntry(program, env, regs, logical))
        {
            return false;
        }

        // The root bounds are evaluated here (once, like the walk's own range evaluation, and
        // throwing the walk's own errors). There is one refusal left after them, and only one.
        if (loop is ForStmt forStmt)
        {
            var range = (RangeExpr)forStmt.Iterable;
            JgsNumericClass bound = JgsNumericClass.Double;
            double start = RangeBound(range.Start, "start", env, ref bound);
            double step = range.Step is null ? 1 : RangeBound(range.Step, "step", env, ref bound);
            double stop = RangeBound(range.Stop, "stop", env, ref bound);

            // A register is a double and has nowhere to put a class, so a loop over `int16(1):int16(4)`
            // would bind a double i where the walk binds an int16 — one construct with two answers
            // depending on a threshold nobody can see. The walk takes it instead. Re-reading the three
            // bounds is the price, and it is a small one: reaching here at all takes an explicit
            // conversion written inside the range, and a conversion has nothing to repeat.
            if (bound != JgsNumericClass.Double)
            {
                return false;
            }

            long count = HotLoopRangeCount(start, step, stop, range.Line, range.Column);
            int state = program.OuterRegBase;
            regs[state] = start;
            regs[state + 1] = step;
            regs[state + 2] = stop;
            regs[state + 3] = count;
            regs[state + 4] = 0;
        }

        JgsLoopJit.CompiledRuns++;
        completion = RunHotLoop(program, env, regs, written, logical);
        return true;
    }

    /// <summary>
    /// <c>EvaluateRange</c>'s element count, error for error: a zero step and an over-limit count
    /// throw its exact messages, and the count whose arithmetic the walk overflows on is driven into
    /// the walk's own allocation so it fails the same way.
    /// </summary>
    private long HotLoopRangeCount(double start, double step, double stop, int line, int column)
    {
        if (step == 0)
        {
            throw new JgsRuntimeException(line, column, "A range step must not be zero.");
        }

        double ratio = (stop - start) / step;
        if (double.IsNaN(ratio) || ratio < 0)
        {
            return 0;
        }

        long count = (long)Math.Floor(ratio * (1 + (4 * RangeEpsilon))) + 1;
        long limit = JgsPacking.Enabled ? 250_000_000 : 50_000_000;
        if (count > limit)
        {
            throw new JgsRuntimeException(line, column,
                $"This range would produce {count} elements — too many.");
        }

        if (count < 0)
        {
            // 1:Inf wraps the count negative; the walk then dies building the range, so build it.
            _ = JgsPacking.Enabled
                ? PackedOps.CreateRange(start, step, count, _cancelCheck)
                : JgsValue.Array(new JgsValue[count]);
        }

        return count;
    }

    /// <summary>
    /// Loads the variables whose pre-loop value some read may see, and re-checks every builtin the
    /// program bound. Anything that is not a plain real scalar double (or bool, or a constant
    /// builtin such as <c>pi</c> mentioned bare) refuses the fast path.
    /// </summary>
    private bool TryLoadHotLoopEntry(RegisterProgram program, JgsEnvironment env, double[] regs, bool[] logical)
    {
        LoopSlot[] slots = program.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            LoopSlot slot = slots[i];
            if (env.IsGlobal(slot.Name))
            {
                return false; // a global lives in the global workspace; the walk reads it there
            }

            if (!slot.EntryRequired)
            {
                continue;
            }

            if (!env.TryGet(slot.Name, out JgsValue value))
            {
                return false; // the walk reports the undefined name (or runs a path file) itself
            }

            if (value.Type == JgsType.Number && value.NumericClass == JgsNumericClass.Double)
            {
                regs[i] = value.AsNumber;
            }
            else if (value.Type == JgsType.Bool)
            {
                regs[i] = value.AsNumber;
                logical[i] = true;
            }
            else if (value.Type == JgsType.Function
                     && JgsBuiltins.IsHotLoopBareConstant(slot.Name)
                     && value.AsCallable is BuiltinFunction { AutoCallsBare: true } constant
                     && constant.Name == slot.Name)
            {
                // The walk calls the constant on every mention; one call at entry is the same
                // number every time, which is what qualifies the name for the list.
                JgsValue answer = constant.Call(System.Array.Empty<JgsValue>(), program.Root.Line, program.Root.Column);
                if (answer.Type != JgsType.Number || answer.NumericClass != JgsNumericClass.Double)
                {
                    return false;
                }

                regs[i] = answer.AsNumber;
            }
            else
            {
                return false;
            }
        }

        foreach (string name in program.RequiredBuiltins)
        {
            if (env.IsGlobal(name)
                || !env.TryGet(name, out JgsValue value)
                || value.Type != JgsType.Function
                || value.AsCallable is not BuiltinFunction resolved
                || resolved.Name != name)
            {
                return false; // shadowed or rebound: the walk does whatever the script arranged
            }
        }

        return true;
    }

    private Completion RunHotLoop(RegisterProgram program, JgsEnvironment env,
                                  double[] regs, bool[] written, bool[] logical)
    {
        RegOp[] ops = program.Ops;
        Func<double, double>[] unary = program.Unary;
        Func<double, bool>?[] guards = program.UnaryGuard;
        long steps = _steps;
        int ip = 0;

        // While true, the registers are the freshest state and an escaping exception spills them;
        // while a bail has the walk executing against the environment, the environment is.
        bool regsAuthoritative = true;
        try
        {
            while (true)
            {
                ref readonly RegOp op = ref ops[ip];
                switch (op.Code)
                {
                    case LoopOp.Step:
                        if (++steps > MaxSteps)
                        {
                            _steps = steps;
                            throw new JgsRuntimeException(0, 0, StepLimitMessage);
                        }

                        ip++;
                        break;

                    case LoopOp.IterTick:
                        if (_cancellationToken.IsCancellationRequested)
                        {
                            _steps = steps;
                            _cancellationToken.ThrowIfCancellationRequested();
                        }

                        if (++steps > MaxSteps)
                        {
                            _steps = steps;
                            throw new JgsRuntimeException(0, 0, StepLimitMessage);
                        }

                        ip++;
                        break;

                    case LoopOp.Jump:
                        ip = op.Arg;
                        break;

                    case LoopOp.JumpIfFalse:
                        ip = regs[op.A] == 0 ? op.Arg : ip + 1;
                        break;

                    case LoopOp.JumpIfTrue:
                        ip = regs[op.A] != 0 ? op.Arg : ip + 1;
                        break;

                    case LoopOp.Copy:
                        regs[op.Dest] = regs[op.A];
                        ip++;
                        break;

                    case LoopOp.Bind:
                        regs[op.Dest] = regs[op.A];
                        written[op.Dest] = true;
                        logical[op.Dest] = op.B != 0;
                        ip++;
                        break;

                    case LoopOp.BindVar:
                        regs[op.Dest] = regs[op.A];
                        written[op.Dest] = true;
                        logical[op.Dest] = logical[op.A];
                        ip++;
                        break;

                    case LoopOp.Add:
                        regs[op.Dest] = regs[op.A] + regs[op.B];
                        ip++;
                        break;

                    case LoopOp.Sub:
                        regs[op.Dest] = regs[op.A] - regs[op.B];
                        ip++;
                        break;

                    case LoopOp.Mul:
                        regs[op.Dest] = regs[op.A] * regs[op.B];
                        ip++;
                        break;

                    case LoopOp.Div:
                        regs[op.Dest] = regs[op.A] / regs[op.B];
                        ip++;
                        break;

                    case LoopOp.Neg:
                        regs[op.Dest] = -regs[op.A];
                        ip++;
                        break;

                    case LoopOp.PowG:
                    {
                        double a = regs[op.A];
                        double b = regs[op.B];
                        if (JgsBuiltins.PowerStaysReal(a, b))
                        {
                            regs[op.Dest] = Math.Pow(a, b);
                            ip++;
                            break;
                        }

                        ip = HotLoopBail(program, op.Arg, env, regs, written, logical,
                                         ref steps, ref regsAuthoritative, out Completion? leftPow);
                        if (leftPow is { } powDone)
                        {
                            return powDone;
                        }

                        break;
                    }

                    case LoopOp.Mod:
                        regs[op.Dest] = JgsBuiltins.ScalarMod(regs[op.A], regs[op.B]);
                        ip++;
                        break;

                    case LoopOp.Rem:
                        regs[op.Dest] = JgsBuiltins.ScalarRem(regs[op.A], regs[op.B]);
                        ip++;
                        break;

                    case LoopOp.Min2:
                        regs[op.Dest] = Math.Min(regs[op.A], regs[op.B]);
                        ip++;
                        break;

                    case LoopOp.Max2:
                        regs[op.Dest] = Math.Max(regs[op.A], regs[op.B]);
                        ip++;
                        break;

                    case LoopOp.Atan2:
                        regs[op.Dest] = Math.Atan2(regs[op.A], regs[op.B]);
                        ip++;
                        break;

                    case LoopOp.Lt:
                        regs[op.Dest] = regs[op.A] < regs[op.B] ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Le:
                        regs[op.Dest] = regs[op.A] <= regs[op.B] ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Gt:
                        regs[op.Dest] = regs[op.A] > regs[op.B] ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Ge:
                        regs[op.Dest] = regs[op.A] >= regs[op.B] ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Eq:
                        regs[op.Dest] = regs[op.A] == regs[op.B] ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Ne:
                        regs[op.Dest] = regs[op.A] == regs[op.B] ? 0 : 1;
                        ip++;
                        break;

                    case LoopOp.Not:
                        regs[op.Dest] = regs[op.A] == 0 ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.ToBool:
                        regs[op.Dest] = regs[op.A] != 0 ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.And:
                        regs[op.Dest] = regs[op.A] != 0 && regs[op.B] != 0 ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Or:
                        regs[op.Dest] = regs[op.A] != 0 || regs[op.B] != 0 ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Call1:
                        regs[op.Dest] = unary[op.B](regs[op.A]);
                        ip++;
                        break;

                    case LoopOp.Call1G:
                    {
                        double x = regs[op.A];
                        if (guards[op.B]!(x))
                        {
                            regs[op.Dest] = unary[op.B](x);
                            ip++;
                            break;
                        }

                        ip = HotLoopBail(program, op.Arg, env, regs, written, logical,
                                         ref steps, ref regsAuthoritative, out Completion? leftReal);
                        if (leftReal is { } callDone)
                        {
                            return callDone;
                        }

                        break;
                    }

                    case LoopOp.RangeCount:
                    {
                        double start = regs[op.A];
                        double step = regs[op.A + 1];
                        double stop = regs[op.A + 2];
                        long count = 0;
                        bool refused = step == 0;
                        if (!refused)
                        {
                            double ratio = (stop - start) / step;
                            if (!double.IsNaN(ratio) && ratio >= 0)
                            {
                                count = (long)Math.Floor(ratio * (1 + (4 * RangeEpsilon))) + 1;
                                long limit = JgsPacking.Enabled ? 250_000_000 : 50_000_000;
                                refused = count > limit || count < 0;
                            }
                        }

                        if (!refused)
                        {
                            regs[op.A + 3] = count;
                            regs[op.A + 4] = 0;
                            ip++;
                            break;
                        }

                        // The walk throws for this range; re-run the whole nested loop statement
                        // there so it throws the identical error with the environment current.
                        ip = HotLoopBail(program, op.Arg, env, regs, written, logical,
                                         ref steps, ref regsAuthoritative, out Completion? badRange);
                        if (badRange is { } rangeDone)
                        {
                            return rangeDone;
                        }

                        break;
                    }

                    case LoopOp.ForHead:
                        ip = regs[op.A + 4] >= regs[op.A + 3] ? op.Arg : ip + 1;
                        break;

                    case LoopOp.ForBind:
                        regs[op.Dest] = regs[op.A] + (regs[op.A + 4] * regs[op.A + 1]);
                        written[op.Dest] = true;
                        logical[op.Dest] = false;
                        ip++;
                        break;

                    case LoopOp.ForNext:
                        regs[op.A + 4] += 1;
                        ip = op.Arg;
                        break;

                    case LoopOp.Halt:
                        _steps = steps;
                        SpillHotLoop(program, env, regs, written, logical);
                        return Completion.Normal;

                    default:
                        throw new InvalidOperationException($"Unknown loop op {op.Code}.");
                }
            }
        }
        catch
        {
            // The walk leaves every completed assignment visible when a loop dies mid-flight
            // (cancellation, the step limit); the registers spill so the compiled loop does too.
            // While a bail had the walk executing, the environment is already the truth.
            if (regsAuthoritative)
            {
                SpillHotLoop(program, env, regs, written, logical);
            }

            throw;
        }
    }

    /// <summary>
    /// A compiled op met a case only the walk can finish. Spills, hands the statement (or condition,
    /// or range bound) to the walk, and answers the op index to resume at — or, when the walk left a
    /// value no register can hold, finishes the entire loop by the walk and answers its completion.
    /// </summary>
    private int HotLoopBail(RegisterProgram program, int bailIndex, JgsEnvironment env,
                            double[] regs, bool[] written, bool[] logical,
                            ref long steps, ref bool regsAuthoritative, out Completion? finished)
    {
        finished = null;
        LoopBail bail = program.Bails[bailIndex];
        _steps = steps;
        SpillHotLoop(program, env, regs, written, logical);
        regsAuthoritative = false;

        switch (bail.Kind)
        {
            case LoopBailKind.Condition:
            {
                // Conditions assign nothing, so the environment cannot move: evaluate, branch, resume.
                bool truth = Evaluate(bail.Expression!, env).IsTruthy;
                steps = _steps;
                regsAuthoritative = true;
                return truth ? bail.OnTrue : bail.OnFalse;
            }

            case LoopBailKind.Bound:
            {
                double value = RangeBound(bail.Expression!, bail.What, env);
                regs[bail.DestReg] = value;
                steps = _steps;
                regsAuthoritative = true;
                return bail.Resume;
            }

            default:
            {
                Completion completion = Execute(bail.Statement!, env);
                steps = _steps;
                if (!TryReloadHotLoop(program, env, regs, written, logical))
                {
                    // Something is no longer a real scalar (the answer went complex, say): the
                    // registers can never hold it, so the walk finishes the loop from right here.
                    finished = RunHotLoopDeopt(bail, completion, env, regs);
                    steps = _steps;
                    return 0;
                }

                regsAuthoritative = true;
                return completion.Kind switch
                {
                    CompletionKind.Break => bail.BreakIp,
                    CompletionKind.Continue => bail.ContinueIp,
                    _ => bail.Resume, // Normal; return does not compile, so Return cannot arrive
                };
            }
        }
    }

    /// <summary>
    /// After a bailed statement ran by the walk: pulls every slot's current binding back into its
    /// register. False when a written or entry-loaded variable no longer holds a value a register
    /// can represent — the signal to finish the loop by the walk.
    /// </summary>
    private bool TryReloadHotLoop(RegisterProgram program, JgsEnvironment env,
                                  double[] regs, bool[] written, bool[] logical)
    {
        LoopSlot[] slots = program.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!env.TryGet(slots[i].Name, out JgsValue value))
            {
                continue; // still unbound; its register was never trusted
            }

            if (value.Type == JgsType.Number && value.NumericClass == JgsNumericClass.Double)
            {
                regs[i] = value.AsNumber;
                logical[i] = false;
            }
            else if (value.Type == JgsType.Bool)
            {
                regs[i] = value.AsNumber;
                logical[i] = true;
            }
            else if (written[i])
            {
                return false; // the walk turned a compiled variable into something else
            }
            else if (slots[i].EntryRequired && value.Type != JgsType.Function)
            {
                return false; // an entry variable went exotic (Function is pi-the-builtin, untouched)
            }

            // else: an untouched binding the loop never trusted (an array it has not rebound yet).
        }

        // Everything spilled and reloaded agrees with the environment, so nothing is dirty now.
        Array.Clear(written);
        return true;
    }

    /// <summary>
    /// Writes every dirty variable to the environment the way the walk binds it: a loop variable by
    /// declaration, everything else assigned outward with a declaration as the fallback.
    /// </summary>
    private void SpillHotLoop(RegisterProgram program, JgsEnvironment env,
                              double[] regs, bool[] written, bool[] logical)
    {
        LoopSlot[] slots = program.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!written[i])
            {
                continue;
            }

            JgsValue value = logical[i]
                ? regs[i] != 0 ? JgsValue.True : JgsValue.False
                : JgsValue.Number(regs[i]);
            if (slots[i].IsLoopVariable)
            {
                env.Declare(slots[i].Name, value);
            }
            else if (!env.TryAssign(slots[i].Name, value))
            {
                env.Declare(slots[i].Name, value);
            }
        }
    }

    /// <summary>
    /// Finishes a compiled loop by the walk after a bailed statement left a value the registers
    /// cannot hold: completes the remaining statements of each enclosing block, then each enclosing
    /// loop's remaining iterations, from the innermost level outward — the identical order the walk
    /// itself would have taken from that point.
    /// </summary>
    private Completion RunHotLoopDeopt(LoopBail bail, Completion current, JgsEnvironment env, double[] regs)
    {
        Completion completion = current;
        for (int level = bail.Path.Length - 1; level >= 0; level--)
        {
            LoopDeoptFrame frame = bail.Path[level];
            if (completion.Kind == CompletionKind.Normal)
            {
                for (int i = frame.Index + 1; i < frame.Block.Count && completion.Kind == CompletionKind.Normal; i++)
                {
                    Tick();
                    completion = Execute(frame.Block[i], env);
                }
            }

            if (completion.Kind == CompletionKind.Return)
            {
                return completion;
            }

            if (frame.For is not null)
            {
                if (completion.Kind == CompletionKind.Break)
                {
                    completion = Completion.Normal;
                    continue; // this loop is done; carry on with the level above
                }

                completion = Completion.Normal;
                double start = regs[frame.RegBase];
                double step = regs[frame.RegBase + 1];
                long count = (long)regs[frame.RegBase + 3];
                for (long n = (long)regs[frame.RegBase + 4] + 1; n < count; n++)
                {
                    Tick();
                    env.Declare(frame.For.Variable, JgsValue.Number(start + (n * step)));
                    Completion one = ExecuteBlock(frame.For.Body, env);
                    if (one.Kind == CompletionKind.Break)
                    {
                        break;
                    }

                    if (one.Kind == CompletionKind.Return)
                    {
                        return one;
                    }
                }
            }
            else if (frame.While is not null)
            {
                if (completion.Kind == CompletionKind.Break)
                {
                    completion = Completion.Normal;
                    continue;
                }

                completion = Completion.Normal;
                while (Evaluate(frame.While.Condition, env).IsTruthy)
                {
                    Tick();
                    Completion one = ExecuteBlock(frame.While.Body, env);
                    if (one.Kind == CompletionKind.Break)
                    {
                        break;
                    }

                    if (one.Kind == CompletionKind.Return)
                    {
                        return one;
                    }
                }
            }
            else if (completion.Kind == CompletionKind.Continue || completion.Kind == CompletionKind.Break)
            {
                continue; // an if level passes break/continue outward to the loop that owns it
            }
        }

        return completion;
    }
}
