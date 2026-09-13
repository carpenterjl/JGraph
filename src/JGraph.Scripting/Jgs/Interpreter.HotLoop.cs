using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// ADR 0160 (12c): how the hot-loop runner reads its arrays. The runner is generic over this, so
/// the JIT compiles one body per accessor with the call inlined away — no branch per access.
/// </summary>
internal interface IHotLoopAccess
{
    static abstract ref T At<T>(T[] array, int index);
}

/// <summary>The runtime's own bounds-checked element access.</summary>
internal readonly struct CheckedAccess : IHotLoopAccess
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T At<T>(T[] array, int index) => ref array[index];
}

/// <summary>
/// Element access without the bounds check, on the strength of <see cref="LoopProgramValidator"/>'s
/// proof that every operand of every op of the program is inside the array it names. Only the
/// runner's switch uses it; the bail, reload, spill and deopt paths keep the checked arrays.
/// </summary>
internal readonly struct UncheckedAccess : IHotLoopAccess
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T At<T>(T[] array, int index) =>
        ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), (nint)(uint)index);
}

/// <summary>
/// ADR 0160 (12d): a compiled loop's vector register file — the vector slots' current wrappers
/// (the environment's own objects, or fresh ones the loop has bound and not yet spilled), the
/// temporaries after them, and the builtins the vector calls go through.
/// </summary>
internal sealed class HotLoopVectors(RegisterProgram program)
{
    /// <summary>Vector register i: a slot's wrapper for i below the slot count, a temporary above it.</summary>
    public readonly JgsValue?[] Regs = new JgsValue?[program.VectorRegisterCount];

    /// <summary>Whether the loop has bound or written into the slot since the last reload.</summary>
    public readonly bool[] Written = new bool[program.VectorSlots.Length];

    /// <summary>Whether the slot holds a wrapper the environment does not yet — a spill must bind it.</summary>
    public readonly bool[] Dirty = new bool[program.VectorSlots.Length];

    /// <summary>The builtin each unary kernel index names, as resolved at entry.</summary>
    public readonly BuiltinFunction?[] Kernels = new BuiltinFunction?[program.UnaryNames.Length];

    public readonly int SlotCount = program.VectorSlots.Length;
}

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
            program = LoopCompiler.Compile(
                loop,
                name => env.TryGet(name, out JgsValue held) && IsVectorValue(held),
                name => env.TryGet(name, out JgsValue bound) && bound.Type != JgsType.Function);
            _loopPrograms[loop] = program;
        }

        if (program is null)
        {
            return false; // the compiler said why, if asked
        }

        var regs = new double[program.RegisterCount];
        var written = new bool[program.Slots.Length];
        var logical = new bool[program.Slots.Length];
        var vectors = new HotLoopVectors(program);
        for (int i = 0; i < program.Constants.Length; i++)
        {
            regs[program.ConstBase + i] = program.Constants[i];
        }

        if (!TryLoadHotLoopEntry(program, env, regs, logical, vectors))
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
        completion = RunHotLoop(program, env, regs, written, logical, vectors);
        return true;
    }

    /// <summary>
    /// ADR 0160 (12d): whether a value is what a vector slot may hold — a packed real double row or
    /// column with no class, tag or shape the vector ops would not reproduce.
    /// </summary>
    private static bool IsVectorValue(JgsValue value) =>
        value.Type == JgsType.Array && value.IsPacked && value.PackedKind == JgsPackedKind.Number
        && value.NumericClass == JgsNumericClass.Double && value.TimeTag is null
        && !value.IsNd && !value.IsCharMatrix && !value.IsStringArray
        && (value.Rows == 1 || value.Cols == 1);

    /// <summary>
    /// ADR 0160 (12d): an index register as a position into a vector of <paramref name="length"/>,
    /// under the rule <c>PackedOps.ToIndex</c> applies (whole, finite, inside the extent, base 1
    /// — the compiled loop runs in the MATLAB dialect only); -1 when the walk must answer instead.
    /// </summary>
    private static int VectorPosition(double raw, int length)
    {
        if (raw != Math.Floor(raw) || double.IsNaN(raw) || double.IsInfinity(raw))
        {
            return -1;
        }

        double position = raw - 1;
        return position >= 0 && position < length ? (int)position : -1;
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
    private bool TryLoadHotLoopEntry(RegisterProgram program, JgsEnvironment env, double[] regs, bool[] logical, HotLoopVectors vectors)
    {
        LoopSlot[] vslots = program.VectorSlots;
        for (int i = 0; i < vslots.Length; i++)
        {
            LoopSlot slot = vslots[i];
            if (env.IsGlobal(slot.Name))
            {
                return Refused($"'{slot.Name}' is global", program);
            }

            if (!slot.EntryRequired)
            {
                continue;
            }

            if (!env.TryGet(slot.Name, out JgsValue value) || !IsVectorValue(value))
            {
                return Refused($"'{slot.Name}' is not a real double vector at entry", program);
            }

            _ = value.AsBuffer; // compact any growth capacity once, as the walk's first read would
            vectors.Regs[i] = value;
        }

        LoopSlot[] slots = program.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            LoopSlot slot = slots[i];
            if (env.IsGlobal(slot.Name))
            {
                return Refused($"'{slot.Name}' is global", program); // the walk reads it in the global workspace
            }

            if (!slot.EntryRequired)
            {
                continue;
            }

            if (!env.TryGet(slot.Name, out JgsValue value))
            {
                return Refused($"'{slot.Name}' is unbound at entry", program); // the walk reports it, or runs a path file
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
                     && _resolver.CompiledBuiltin(slot.Name, env, bare: true) is BuiltinFunction { AutoCallsBare: true } constant)
            {
                // The walk calls the constant on every mention; one call at entry is the same
                // number every time, which is what qualifies the name for the list.
                JgsValue answer = constant.Call(System.Array.Empty<JgsValue>(), program.Root.Line, program.Root.Column);
                if (answer.Type != JgsType.Number || answer.NumericClass != JgsNumericClass.Double)
                {
                    return Refused($"'{slot.Name}' does not answer a double", program);
                }

                regs[i] = answer.AsNumber;
            }
            else
            {
                return Refused($"'{slot.Name}' is not a real scalar at entry", program);
            }
        }

        foreach (string name in program.RequiredBuiltins)
        {
            // The resolver says whether the built-in is what a call from this loop would reach:
            // anything else — a variable, a global, a local function, a file or private function the
            // dispatch table does not let the built-in beat for the loop's numeric arguments — means
            // the walk does whatever the script arranged.
            if (_resolver.CompiledBuiltin(name, env, bare: false) is null)
            {
                return Refused($"'{name}' no longer resolves to the builtin", program); // the walk does whatever the script arranged
            }
        }

        for (int k = 0; k < program.UnaryNames.Length; k++)
        {
            vectors.Kernels[k] = _resolver.CompiledBuiltin(program.UnaryNames[k], env, bare: false);
        }

        return true;
    }

    private static bool Refused(string what, RegisterProgram program)
    {
        JgsLoopJit.Refused("the entry check refused: " + what, program.Root);
        return false;
    }

    /// <summary>After a walked statement: whether every builtin the program bound still resolves to itself.</summary>
    private bool HotLoopBuiltinsStillResolve(RegisterProgram program, JgsEnvironment env, HotLoopVectors vectors)
    {
        foreach (string name in program.RequiredBuiltins)
        {
            if (_resolver.CompiledBuiltin(name, env, bare: false) is null)
            {
                return false;
            }
        }

        for (int k = 0; k < program.UnaryNames.Length; k++)
        {
            if (!ReferenceEquals(vectors.Kernels[k], _resolver.CompiledBuiltin(program.UnaryNames[k], env, bare: false)))
            {
                return false;
            }
        }

        return true;
    }

    private Completion RunHotLoop(RegisterProgram program, JgsEnvironment env,
                                  double[] regs, bool[] written, bool[] logical, HotLoopVectors vectors) =>
        JgsLoopJit.Unchecked
            ? RunHotLoop<UncheckedAccess>(program, env, regs, written, logical, vectors)
            : RunHotLoop<CheckedAccess>(program, env, regs, written, logical, vectors);

    /// <summary>
    /// The switch itself, over <typeparamref name="TAccess"/>'s reads: the validator proved every
    /// operand of every op in bounds at compile time (ADR 0160, 12c), so the unchecked accessor is
    /// sound here and only here.
    /// </summary>
    private Completion RunHotLoop<TAccess>(RegisterProgram program, JgsEnvironment env,
                                           double[] regs, bool[] written, bool[] logical, HotLoopVectors vectors)
        where TAccess : struct, IHotLoopAccess
    {
        RegOp[] ops = program.Ops;
        JgsValue?[] vregs = vectors.Regs;
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
                ref readonly RegOp op = ref TAccess.At(ops, ip);
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
                        ip = TAccess.At(regs, op.A) == 0 ? op.Arg : ip + 1;
                        break;

                    case LoopOp.JumpIfTrue:
                        ip = TAccess.At(regs, op.A) != 0 ? op.Arg : ip + 1;
                        break;

                    case LoopOp.Copy:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A);
                        ip++;
                        break;

                    case LoopOp.Bind:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A);
                        TAccess.At(written, op.Dest) = true;
                        TAccess.At(logical, op.Dest) = op.B != 0;
                        ip++;
                        break;

                    case LoopOp.BindVar:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A);
                        TAccess.At(written, op.Dest) = true;
                        TAccess.At(logical, op.Dest) = TAccess.At(logical, op.A);
                        ip++;
                        break;

                    case LoopOp.Add:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) + TAccess.At(regs, op.B);
                        ip++;
                        break;

                    case LoopOp.Sub:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) - TAccess.At(regs, op.B);
                        ip++;
                        break;

                    case LoopOp.Mul:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) * TAccess.At(regs, op.B);
                        ip++;
                        break;

                    case LoopOp.Div:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) / TAccess.At(regs, op.B);
                        ip++;
                        break;

                    case LoopOp.Neg:
                        TAccess.At(regs, op.Dest) = -TAccess.At(regs, op.A);
                        ip++;
                        break;

                    case LoopOp.PowG:
                    {
                        double a = TAccess.At(regs, op.A);
                        double b = TAccess.At(regs, op.B);
                        if (JgsBuiltins.PowerStaysReal(a, b))
                        {
                            TAccess.At(regs, op.Dest) = Math.Pow(a, b);
                            ip++;
                            break;
                        }

                        ip = HotLoopBail(program, op.Arg, env, regs, written, logical, vectors,
                                         ref steps, ref regsAuthoritative, out Completion? leftPow);
                        if (leftPow is { } powDone)
                        {
                            return powDone;
                        }

                        break;
                    }

                    case LoopOp.Mod:
                        TAccess.At(regs, op.Dest) = JgsBuiltins.ScalarMod(TAccess.At(regs, op.A), TAccess.At(regs, op.B));
                        ip++;
                        break;

                    case LoopOp.Rem:
                        TAccess.At(regs, op.Dest) = JgsBuiltins.ScalarRem(TAccess.At(regs, op.A), TAccess.At(regs, op.B));
                        ip++;
                        break;

                    case LoopOp.Min2:
                        TAccess.At(regs, op.Dest) = Math.Min(TAccess.At(regs, op.A), TAccess.At(regs, op.B));
                        ip++;
                        break;

                    case LoopOp.Max2:
                        TAccess.At(regs, op.Dest) = Math.Max(TAccess.At(regs, op.A), TAccess.At(regs, op.B));
                        ip++;
                        break;

                    case LoopOp.Atan2:
                        TAccess.At(regs, op.Dest) = Math.Atan2(TAccess.At(regs, op.A), TAccess.At(regs, op.B));
                        ip++;
                        break;

                    case LoopOp.Lt:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) < TAccess.At(regs, op.B) ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Le:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) <= TAccess.At(regs, op.B) ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Gt:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) > TAccess.At(regs, op.B) ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Ge:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) >= TAccess.At(regs, op.B) ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Eq:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) == TAccess.At(regs, op.B) ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Ne:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) == TAccess.At(regs, op.B) ? 0 : 1;
                        ip++;
                        break;

                    case LoopOp.Not:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) == 0 ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.ToBool:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) != 0 ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.And:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) != 0 && TAccess.At(regs, op.B) != 0 ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Or:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) != 0 || TAccess.At(regs, op.B) != 0 ? 1 : 0;
                        ip++;
                        break;

                    case LoopOp.Call1:
                        TAccess.At(regs, op.Dest) = TAccess.At(unary, op.B)(TAccess.At(regs, op.A));
                        ip++;
                        break;

                    case LoopOp.Call1G:
                    {
                        double x = TAccess.At(regs, op.A);
                        if (TAccess.At(guards, op.B)!(x))
                        {
                            TAccess.At(regs, op.Dest) = TAccess.At(unary, op.B)(x);
                            ip++;
                            break;
                        }

                        ip = HotLoopBail(program, op.Arg, env, regs, written, logical, vectors,
                                         ref steps, ref regsAuthoritative, out Completion? leftReal);
                        if (leftReal is { } callDone)
                        {
                            return callDone;
                        }

                        break;
                    }

                    case LoopOp.RangeCount:
                    {
                        double start = TAccess.At(regs, op.A);
                        double step = TAccess.At(regs, op.A + 1);
                        double stop = TAccess.At(regs, op.A + 2);
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
                            TAccess.At(regs, op.A + 3) = count;
                            TAccess.At(regs, op.A + 4) = 0;
                            ip++;
                            break;
                        }

                        // The walk throws for this range; re-run the whole nested loop statement
                        // there so it throws the identical error with the environment current.
                        ip = HotLoopBail(program, op.Arg, env, regs, written, logical, vectors,
                                         ref steps, ref regsAuthoritative, out Completion? badRange);
                        if (badRange is { } rangeDone)
                        {
                            return rangeDone;
                        }

                        break;
                    }

                    case LoopOp.ForHead:
                        ip = TAccess.At(regs, op.A + 4) >= TAccess.At(regs, op.A + 3) ? op.Arg : ip + 1;
                        break;

                    case LoopOp.ForBind:
                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) + (TAccess.At(regs, op.A + 4) * TAccess.At(regs, op.A + 1));
                        TAccess.At(written, op.Dest) = true;
                        TAccess.At(logical, op.Dest) = false;
                        ip++;
                        break;

                    case LoopOp.ForNext:
                        TAccess.At(regs, op.A + 4) += 1;
                        ip = op.Arg;
                        break;

                    case LoopOp.ForStep:
                    {
                        // The back edge fused with the head (ADR 0160, 12b): advance, test, and then
                        // IterTick's poll and charge and ForBind's bind, in that order.
                        double index = TAccess.At(regs, op.A + 4) + 1;
                        TAccess.At(regs, op.A + 4) = index;
                        if (index >= TAccess.At(regs, op.A + 3))
                        {
                            ip++;
                            break;
                        }

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

                        TAccess.At(regs, op.Dest) = TAccess.At(regs, op.A) + (index * TAccess.At(regs, op.A + 1));
                        TAccess.At(written, op.Dest) = true;
                        TAccess.At(logical, op.Dest) = false;
                        ip = op.Arg;
                        break;
                    }

                    case LoopOp.StepBlock:
                        // The block's statements charged at once (ADR 0160, 12b); a block the limit
                        // would interrupt takes its per-statement copy instead, and pays there.
                        if (steps + op.A > MaxSteps)
                        {
                            ip = op.Arg;
                        }
                        else
                        {
                            steps += op.A;
                            ip++;
                        }

                        break;

                    case LoopOp.UnlessLt:
                        ip = TAccess.At(regs, op.A) < TAccess.At(regs, op.B) ? ip + 1 : op.Arg;
                        break;

                    case LoopOp.UnlessLe:
                        ip = TAccess.At(regs, op.A) <= TAccess.At(regs, op.B) ? ip + 1 : op.Arg;
                        break;

                    case LoopOp.UnlessGt:
                        ip = TAccess.At(regs, op.A) > TAccess.At(regs, op.B) ? ip + 1 : op.Arg;
                        break;

                    case LoopOp.UnlessGe:
                        ip = TAccess.At(regs, op.A) >= TAccess.At(regs, op.B) ? ip + 1 : op.Arg;
                        break;

                    case LoopOp.UnlessEq:
                        ip = TAccess.At(regs, op.A) == TAccess.At(regs, op.B) ? ip + 1 : op.Arg;
                        break;

                    case LoopOp.UnlessNe:
                        ip = TAccess.At(regs, op.A) == TAccess.At(regs, op.B) ? op.Arg : ip + 1;
                        break;

                    case LoopOp.VLoad:
                    {
                        // v(i): the element, unboxed; a position the walk would refuse is the walk's.
                        JgsValue vector = vregs[op.A]!;
                        int position = VectorPosition(TAccess.At(regs, op.B), vector.ArrayLength);
                        if (position >= 0)
                        {
                            TAccess.At(regs, op.Dest) = vector.GetPackedNumber(position);
                            ip++;
                            break;
                        }

                        ip = HotLoopBail(program, op.Arg, env, regs, written, logical, vectors,
                                         ref steps, ref regsAuthoritative, out Completion? badRead);
                        if (badRead is { } readDone)
                        {
                            return readDone;
                        }

                        break;
                    }

                    case LoopOp.VStore:
                    {
                        // x(i) = s, in place as the walk writes it. A position outside the extent
                        // (the walk grows the array), a fractional one (the walk throws), or a
                        // logical in a variable (the walk demotes the array) is the walk's.
                        JgsValue vector = vregs[op.Dest]!;
                        int position = VectorPosition(TAccess.At(regs, op.A), vector.ArrayLength);
                        if (position >= 0 && !(op.B < written.Length && TAccess.At(logical, op.B)))
                        {
                            vector.SetPackedNumber(position, TAccess.At(regs, op.B));
                            vectors.Written[op.Dest] = true;
                            ip++;
                            break;
                        }

                        ip = HotLoopBail(program, op.Arg, env, regs, written, logical, vectors,
                                         ref steps, ref regsAuthoritative, out Completion? badWrite);
                        if (badWrite is { } writeDone)
                        {
                            return writeDone;
                        }

                        break;
                    }

                    case LoopOp.VArithVV:
                    case LoopOp.VArithVS:
                    case LoopOp.VArithSV:
                    {
                        // The walk's own operator on the evaluated operands: the bytes are its bytes.
                        // An answer outside the class (shapes expanded, a complex answer) is the walk's.
                        var node = (BinaryExpr)program.Nodes[op.C];
                        JgsValue left = op.Code == LoopOp.VArithSV ? JgsValue.Number(TAccess.At(regs, op.A)) : vregs[op.A]!;
                        JgsValue right = op.Code == LoopOp.VArithVS ? JgsValue.Number(TAccess.At(regs, op.B)) : vregs[op.B]!;
                        JgsValue answer = ApplyBinary(node.Op, left, right, node);
                        if (IsVectorValue(answer))
                        {
                            vregs[op.Dest] = answer;
                            ip++;
                            break;
                        }

                        ip = HotLoopBail(program, op.Arg, env, regs, written, logical, vectors,
                                         ref steps, ref regsAuthoritative, out Completion? leftClass);
                        if (leftClass is { } classDone)
                        {
                            return classDone;
                        }

                        break;
                    }

                    case LoopOp.VNeg:
                    {
                        var node = (UnaryExpr)program.Nodes[op.C];
                        JgsValue answer = ApplyUnary(node, vregs[op.A]!);
                        if (IsVectorValue(answer))
                        {
                            vregs[op.Dest] = answer;
                            ip++;
                            break;
                        }

                        ip = HotLoopBail(program, op.Arg, env, regs, written, logical, vectors,
                                         ref steps, ref regsAuthoritative, out Completion? negDone);
                        if (negDone is { } negFinished)
                        {
                            return negFinished;
                        }

                        break;
                    }

                    case LoopOp.VCall1:
                    {
                        // The builtin itself, as the walk would call it, on the evaluated vector.
                        var call = (CallExpr)program.Nodes[op.C];
                        _pendingCall = call;
                        JgsValue answer = vectors.Kernels[op.B]!.Call([vregs[op.A]!], call.Line, call.Column);
                        if (IsVectorValue(answer))
                        {
                            vregs[op.Dest] = answer;
                            ip++;
                            break;
                        }

                        ip = HotLoopBail(program, op.Arg, env, regs, written, logical, vectors,
                                         ref steps, ref regsAuthoritative, out Completion? callLeft);
                        if (callLeft is { } callFinished)
                        {
                            return callFinished;
                        }

                        break;
                    }

                    case LoopOp.VBind:
                    {
                        // A temporary is adopted as the walk adopts an owned answer; another slot
                        // is copied as CopyForBinding copies it. Either way the slot's wrapper is
                        // new to the environment until the next spill.
                        JgsValue source = vregs[op.A]!;
                        vregs[op.Dest] = op.A < vectors.SlotCount ? CopyForBinding(source) : source;
                        vectors.Written[op.Dest] = true;
                        vectors.Dirty[op.Dest] = true;
                        ip++;
                        break;
                    }

                    case LoopOp.Walk:
                    {
                        ip = HotLoopBail(program, op.Arg, env, regs, written, logical, vectors,
                                         ref steps, ref regsAuthoritative, out Completion? walked);
                        if (walked is { } walkDone)
                        {
                            return walkDone;
                        }

                        break;
                    }

                    case LoopOp.Halt:
                        _steps = steps;
                        SpillHotLoop(program, env, regs, written, logical, vectors);
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
                _steps = steps;
                SpillHotLoop(program, env, regs, written, logical, vectors);
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
                            double[] regs, bool[] written, bool[] logical, HotLoopVectors vectors,
                            ref long steps, ref bool regsAuthoritative, out Completion? finished)
    {
        finished = null;
        JgsLoopJit.Bails++;
        LoopBail bail = program.Bails[bailIndex];
        _steps = steps;
        SpillHotLoop(program, env, regs, written, logical, vectors);
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
                bool unbound = false;
                foreach (int slot in bail.Touched)
                {
                    // The walk just rebound this variable: its register is stale whether or not a
                    // compiled op had ever written it, so the reload must take the environment's
                    // word — and finish by the walk if that word is a value no register can hold,
                    // or if the statement left the name unbound where the program counted on it.
                    written[slot] = true;
                    unbound |= !env.Contains(program.Slots[slot].Name);
                }

                foreach (int slot in bail.TouchedVectors)
                {
                    vectors.Written[slot] = true;
                    unbound |= !env.Contains(program.VectorSlots[slot].Name);
                }

                if (unbound || !TryReloadHotLoop(program, env, regs, written, logical, vectors)
                    || (bail.IsWalk && !HotLoopBuiltinsStillResolve(program, env, vectors)))
                {
                    // Something is no longer a real scalar (the answer went complex, say), or a
                    // walked statement rebound a builtin the program calls: the registers can
                    // never hold it, so the walk finishes the loop from right here.
                    finished = RunHotLoopDeopt(bail, completion, env, regs);
                    steps = _steps;
                    return 0;
                }

                if (completion.Kind == CompletionKind.Return)
                {
                    // A walked statement returned from the enclosing function: the environment is
                    // the truth already, and there is nothing left of the loop to run.
                    finished = completion;
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
                                  double[] regs, bool[] written, bool[] logical, HotLoopVectors vectors)
    {
        LoopSlot[] vslots = program.VectorSlots;
        for (int i = 0; i < vslots.Length; i++)
        {
            if (!env.TryGet(vslots[i].Name, out JgsValue value))
            {
                continue; // still unbound; its register was never trusted
            }

            if (IsVectorValue(value))
            {
                vectors.Regs[i] = value; // the environment's wrapper, grown or rebound or as it was
                vectors.Dirty[i] = false;
            }
            else if (vectors.Written[i] || vslots[i].EntryRequired)
            {
                return false; // the walk turned a vector of the loop into something else
            }
        }

        Array.Clear(vectors.Written);
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
                              double[] regs, bool[] written, bool[] logical, HotLoopVectors vectors)
    {
        LoopSlot[] vslots = program.VectorSlots;
        for (int i = 0; i < vslots.Length; i++)
        {
            if (!vectors.Dirty[i])
            {
                continue; // the environment holds this wrapper already; in-place writes are in it
            }

            JgsValue value = vectors.Regs[i]!;
            if (!env.TryAssign(vslots[i].Name, value))
            {
                env.Declare(vslots[i].Name, value);
            }

            vectors.Dirty[i] = false;
        }

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
