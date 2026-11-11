using System.Reflection;
using System.Reflection.Emit;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The every-row plan's I2 (ADR 0325): a compiled loop's register program emitted as IL, so the
/// runtime's JIT turns it into machine code with the registers in machine registers and no switch
/// per op. Only a program of scalar ops is emitted (no vector op); everything the register machine
/// hands to the walk - a bail, a walked statement - and the step limit and cancellation leave the
/// emitted method with a code, and the runner's own C# answers them exactly as before.
/// </summary>
/// <remarks>
/// A loop starts on the register machine; once it has taken <see cref="Threshold"/> back edges the
/// runner moves it, registers and all, to the emitted method from the op it was about to run, so a
/// short loop never pays for an emission. Every op emits the arithmetic the register machine's
/// switch applies (the same <see cref="Math"/> calls, the same <see cref="JgsBuiltins.ScalarMod"/>,
/// the same comparisons with NaN on the same side), so the two give the same doubles; the JIT
/// contracts nothing into a fused multiply-add.
/// </remarks>
internal static class JgsLoopNative
{
    /// <summary>Whether long-running compiled loops move to emitted code; <c>JGRAPH_LOOP_NATIVE=1|0</c> forces it.</summary>
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_LOOP_NATIVE") switch
    {
        "0" or "false" => false,
        _ => true,
    };

    /// <summary>
    /// The back edges a loop takes on the register machine before it moves; <c>JGRAPH_LOOP_NATIVE_AFTER=n</c>
    /// sets it (the tests set 1, so every loop with a second iteration runs emitted).
    /// </summary>
    public static int Threshold { get; set; } =
        int.TryParse(Environment.GetEnvironmentVariable("JGRAPH_LOOP_NATIVE_AFTER"), out int after) && after >= 1 ? after : 4096;

    /// <summary>How many times a loop moved to emitted code in this process (the tests' witness).</summary>
    internal static long Moves;

    /// <summary>The marker on a program the emitter declined (it holds a vector op).</summary>
    internal static readonly object NotEmitted = new();

    /// <summary>What the emitted method answers when the loop finished normally.</summary>
    internal const int Halted = -1;

    /// <summary>What it answers when the step limit was passed (steps already charged).</summary>
    internal const int StepLimit = -2;

    /// <summary>What it answers when cancellation was seen.</summary>
    internal const int Cancelled = -3;
}

/// <summary>
/// An emitted loop: runs from op <paramref name="ip"/> until the loop halts or something only the
/// runner can answer, writing the registers and slot flags back to the arrays first. Answers a bail
/// index (zero or more) or one of <see cref="JgsLoopNative"/>'s codes.
/// </summary>
internal delegate int NativeLoop(double[] regs, bool[] written, bool[] logical, bool[] emptied, ref long steps,
    int ip, long maxSteps, CancellationToken cancellation, Func<double, double>[] unary, Func<double, bool>?[] guards);

internal sealed partial class Interpreter
{
    /// <summary>The emitted method for <paramref name="program"/>, emitted on first asking; null when it holds an op the emitter leaves alone.</summary>
    private static NativeLoop? NativeLoopFor(RegisterProgram program)
    {
        program.Native ??= NativeLoopEmitter.TryEmit(program) ?? JgsLoopNative.NotEmitted;
        return program.Native as NativeLoop;
    }

    /// <summary>
    /// The loop from op <paramref name="ip"/> on, by the emitted method: the runner's own bail, spill,
    /// step-limit and cancellation code around it, as <see cref="RunHotLoop{TAccess}"/> has them.
    /// </summary>
    private Completion RunHotLoopNative(RegisterProgram program, NativeLoop native, JgsEnvironment env,
                                        double[] regs, bool[] written, bool[] logical, HotLoopVectors vectors, int ip)
    {
        JgsLoopNative.Moves++;
        long steps = _steps;
        bool regsAuthoritative = true;
        try
        {
            while (true)
            {
                int code = native(regs, written, logical, vectors.Emptied, ref steps, ip, MaxSteps, _cancellationToken,
                    program.Unary, program.UnaryGuard);
                switch (code)
                {
                    case JgsLoopNative.Halted:
                        _steps = steps;
                        SpillHotLoop(program, env, regs, written, logical, vectors);
                        return Completion.Normal;

                    case JgsLoopNative.StepLimit:
                        _steps = steps;
                        throw new JgsRuntimeException(0, 0, StepLimitMessage);

                    case JgsLoopNative.Cancelled:
                        _steps = steps;
                        _cancellationToken.ThrowIfCancellationRequested();
                        break;

                    default:
                        ip = HotLoopBail(program, code, env, regs, written, logical, vectors,
                                         ref steps, ref regsAuthoritative, out Completion? finished);
                        if (finished is { } done)
                        {
                            return done;
                        }

                        break;
                }
            }
        }
        catch
        {
            if (regsAuthoritative)
            {
                _steps = steps;
                SpillHotLoop(program, env, regs, written, logical, vectors);
            }

            throw;
        }
    }

    /// <summary>
    /// <c>RangeCount</c>'s arithmetic, the register machine's own: the count, or -1 where the walk
    /// throws (a zero step, too many elements, a count that wrapped) and the op bails.
    /// </summary>
    internal static long NativeRangeCount(double start, double step, double stop)
    {
        if (step == 0)
        {
            return -1;
        }

        long count = 0;
        double ratio = (stop - start) / step;
        if (!double.IsNaN(ratio) && ratio >= 0)
        {
            count = (long)Math.Floor(ratio * (1 + (4 * RangeEpsilon))) + 1;
            long limit = JgsPacking.Enabled ? 250_000_000 : 50_000_000;
            if (count > limit || count < 0)
            {
                return -1;
            }
        }

        return count;
    }

    /// <summary>Emits one <see cref="RegisterProgram"/> as a <see cref="NativeLoop"/>.</summary>
    private static class NativeLoopEmitter
    {
        private static readonly MethodInfo IsCancellationRequested =
            typeof(CancellationToken).GetProperty(nameof(CancellationToken.IsCancellationRequested))!.GetGetMethod()!;

        private static readonly MethodInfo Pow = typeof(JGraph.Numerics.PackedMath).GetMethod(nameof(JGraph.Numerics.PackedMath.Power), [typeof(double), typeof(double)])!;
        private static readonly MethodInfo Min = typeof(Math).GetMethod(nameof(Math.Min), [typeof(double), typeof(double)])!;
        private static readonly MethodInfo Max = typeof(Math).GetMethod(nameof(Math.Max), [typeof(double), typeof(double)])!;
        private static readonly MethodInfo Atan2 = typeof(Math).GetMethod(nameof(Math.Atan2), [typeof(double), typeof(double)])!;

        private static readonly MethodInfo PowerStaysReal =
            typeof(JgsBuiltins).GetMethod(nameof(JgsBuiltins.PowerStaysReal), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        private static readonly MethodInfo ScalarMod =
            typeof(JgsBuiltins).GetMethod(nameof(JgsBuiltins.ScalarMod), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        private static readonly MethodInfo ScalarRem =
            typeof(JgsBuiltins).GetMethod(nameof(JgsBuiltins.ScalarRem), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        private static readonly MethodInfo RangeCount =
            typeof(Interpreter).GetMethod(nameof(NativeRangeCount), BindingFlags.Static | BindingFlags.NonPublic)!;

        private static readonly MethodInfo UnaryInvoke = typeof(Func<double, double>).GetMethod("Invoke")!;
        private static readonly MethodInfo GuardInvoke = typeof(Func<double, bool>).GetMethod("Invoke")!;

        // Argument positions of NativeLoop.
        private const short ArgRegs = 0;
        private const short ArgWritten = 1;
        private const short ArgLogical = 2;
        private const short ArgEmptied = 3;
        private const short ArgSteps = 4;
        private const short ArgIp = 5;
        private const short ArgMaxSteps = 6;
        private const short ArgCancellation = 7;
        private const short ArgUnary = 8;
        private const short ArgGuards = 9;

        public static NativeLoop? TryEmit(RegisterProgram program)
        {
            RegOp[] ops = program.Ops;
            if (ops.Length == 0 || Array.TrueForAll(ops, static op => op.Code != LoopOp.Halt))
            {
                return null;
            }

            foreach (RegOp op in ops)
            {
                if (op.Code is LoopOp.VLoad or LoopOp.VStore or LoopOp.VArithVV or LoopOp.VArithVS or LoopOp.VArithSV
                    or LoopOp.VNeg or LoopOp.VCall1 or LoopOp.VBind or LoopOp.VUpdate)
                {
                    return null; // a vector op: the register machine keeps the loop
                }
            }

            try
            {
                return Emit(program);
            }
            catch (Exception failure) when (failure is InvalidOperationException or IndexOutOfRangeException
                                            or ArgumentException or NotSupportedException)
            {
                JgsLoopJit.Refused("the loop emitter declined: " + failure.Message, program.Root);
                return null;
            }
        }

        private static NativeLoop Emit(RegisterProgram program)
        {
            RegOp[] ops = program.Ops;

            // A constant register no op writes is its number, which the JIT may then fold.
            int registers = program.RegisterCount;
            var writtenRegs = new bool[registers];
            foreach (RegOp op in ops)
            {
                foreach (int dest in Destinations(op))
                {
                    writtenRegs[dest] = true;
                }
            }

            var method = new DynamicMethod("JgsLoop", typeof(int),
                [typeof(double[]), typeof(bool[]), typeof(bool[]), typeof(bool[]), typeof(long).MakeByRefType(),
                 typeof(int), typeof(long), typeof(CancellationToken), typeof(Func<double, double>[]), typeof(Func<double, bool>?[])],
                typeof(Interpreter).Module, skipVisibility: true);
            ILGenerator il = method.GetILGenerator();
            var emit = new Emitter(il, program, writtenRegs);
            emit.Body();
            return (NativeLoop)method.CreateDelegate(typeof(NativeLoop));
        }

        /// <summary>Every register an op writes, so the emitter knows which constants stay constant.</summary>
        private static IEnumerable<int> Destinations(RegOp op)
        {
            switch (op.Code)
            {
                case LoopOp.Copy or LoopOp.Bind or LoopOp.BindVar or LoopOp.Add or LoopOp.Sub or LoopOp.Mul
                    or LoopOp.Div or LoopOp.Neg or LoopOp.PowG or LoopOp.Mod or LoopOp.Rem or LoopOp.Min2
                    or LoopOp.Max2 or LoopOp.Atan2 or LoopOp.Lt or LoopOp.Le or LoopOp.Gt or LoopOp.Ge or LoopOp.Eq
                    or LoopOp.Ne or LoopOp.Not or LoopOp.ToBool or LoopOp.And or LoopOp.Or or LoopOp.Call1
                    or LoopOp.Call1G or LoopOp.ForBind or LoopOp.ForStep:
                    yield return op.Dest;
                    break;
            }

            switch (op.Code)
            {
                case LoopOp.RangeCount:
                    yield return op.A + 3;
                    yield return op.A + 4;
                    break;
                case LoopOp.ForNext or LoopOp.ForStep:
                    yield return op.A + 4;
                    break;
            }
        }

        private sealed class Emitter(ILGenerator il, RegisterProgram program, bool[] writtenRegs)
        {
            private readonly RegOp[] _ops = program.Ops;
            private readonly int _slots = program.Slots.Length;
            private LocalBuilder[] _regs = [];
            private LocalBuilder[] _written = [];
            private LocalBuilder[] _logical = [];
            private LocalBuilder[] _emptied = [];
            private LocalBuilder _steps = null!;
            private LocalBuilder _code = null!;
            private Label[] _at = [];
            private Label _exit;

            public void Body()
            {
                int registers = program.RegisterCount;
                _regs = new LocalBuilder[registers];
                for (int i = 0; i < registers; i++)
                {
                    _regs[i] = il.DeclareLocal(typeof(double));
                }

                _written = new LocalBuilder[_slots];
                _logical = new LocalBuilder[_slots];
                _emptied = new LocalBuilder[_slots];
                for (int i = 0; i < _slots; i++)
                {
                    _written[i] = il.DeclareLocal(typeof(bool));
                    _logical[i] = il.DeclareLocal(typeof(bool));
                    _emptied[i] = il.DeclareLocal(typeof(bool));
                }

                _steps = il.DeclareLocal(typeof(long));
                _code = il.DeclareLocal(typeof(int));
                _exit = il.DefineLabel();
                _at = new Label[_ops.Length];
                for (int i = 0; i < _ops.Length; i++)
                {
                    _at[i] = il.DefineLabel();
                }

                // Entry: the arrays into locals, then to the op the runner names.
                for (int i = 0; i < registers; i++)
                {
                    if (IsConstant(i))
                    {
                        continue;
                    }

                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldelem_R8);
                    il.Emit(OpCodes.Stloc, _regs[i]);
                }

                for (int i = 0; i < _slots; i++)
                {
                    LoadFlag(ArgWritten, i, _written[i]);
                    LoadFlag(ArgLogical, i, _logical[i]);
                    LoadFlag(ArgEmptied, i, _emptied[i]);
                }

                il.Emit(OpCodes.Ldarg, ArgSteps);
                il.Emit(OpCodes.Ldind_I8);
                il.Emit(OpCodes.Stloc, _steps);
                il.Emit(OpCodes.Ldarg, ArgIp);
                il.Emit(OpCodes.Switch, _at);
                Exit(JgsLoopNative.Halted); // an ip outside the program: nothing to run

                for (int ip = 0; ip < _ops.Length; ip++)
                {
                    il.MarkLabel(_at[ip]);
                    Op(ip, _ops[ip]);
                }

                // The register machine never runs off the end of a program (the validator's proof);
                // should the last op fall through, leave as the halt would rather than into the exit's code.
                Exit(JgsLoopNative.Halted);

                // Exit: the locals back into the arrays, the steps through the reference, the code.
                il.MarkLabel(_exit);
                for (int i = 0; i < registers; i++)
                {
                    if (!writtenRegs[i])
                    {
                        continue; // never written: the array holds it already
                    }

                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldloc, _regs[i]);
                    il.Emit(OpCodes.Stelem_R8);
                }

                for (int i = 0; i < _slots; i++)
                {
                    StoreFlag(ArgWritten, i, _written[i]);
                    StoreFlag(ArgLogical, i, _logical[i]);
                    StoreFlag(ArgEmptied, i, _emptied[i]);
                }

                il.Emit(OpCodes.Ldarg, ArgSteps);
                il.Emit(OpCodes.Ldloc, _steps);
                il.Emit(OpCodes.Stind_I8);
                il.Emit(OpCodes.Ldloc, _code);
                il.Emit(OpCodes.Ret);
            }

            private bool IsConstant(int register) =>
                !writtenRegs[register] && register >= program.ConstBase
                && register < program.ConstBase + program.Constants.Length;

            private void LoadFlag(short array, int slot, LocalBuilder local)
            {
                il.Emit(OpCodes.Ldarg, array);
                il.Emit(OpCodes.Ldc_I4, slot);
                il.Emit(OpCodes.Ldelem_U1);
                il.Emit(OpCodes.Stloc, local);
            }

            private void StoreFlag(short array, int slot, LocalBuilder local)
            {
                il.Emit(OpCodes.Ldarg, array);
                il.Emit(OpCodes.Ldc_I4, slot);
                il.Emit(OpCodes.Ldloc, local);
                il.Emit(OpCodes.Stelem_I1);
            }

            private void Load(int register)
            {
                if (IsConstant(register))
                {
                    il.Emit(OpCodes.Ldc_R8, program.Constants[register - program.ConstBase]);
                }
                else
                {
                    il.Emit(OpCodes.Ldloc, _regs[register]);
                }
            }

            private void Store(int register) => il.Emit(OpCodes.Stloc, _regs[register]);

            private void SetFlag(LocalBuilder[] flags, int slot, bool value)
            {
                il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stloc, flags[slot]);
            }

            /// <summary>Leaves through the exit block with <paramref name="code"/>.</summary>
            private void Exit(int code)
            {
                il.Emit(OpCodes.Ldc_I4, code);
                il.Emit(OpCodes.Stloc, _code);
                il.Emit(OpCodes.Br, _exit);
            }

            /// <summary>Charges one step; past the limit, leaves with the step-limit code.</summary>
            private void Charge()
            {
                Label within = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, _steps);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Conv_I8);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Stloc, _steps);
                il.Emit(OpCodes.Ldarg, ArgMaxSteps);
                il.Emit(OpCodes.Ble, within);
                Exit(JgsLoopNative.StepLimit);
                il.MarkLabel(within);
            }

            /// <summary>Polls the cancellation; when it is requested, leaves with the cancellation code.</summary>
            private void Poll()
            {
                Label running = il.DefineLabel();
                il.Emit(OpCodes.Ldarga, ArgCancellation);
                il.Emit(OpCodes.Call, IsCancellationRequested);
                il.Emit(OpCodes.Brfalse, running);
                Exit(JgsLoopNative.Cancelled);
                il.MarkLabel(running);
            }

            /// <summary>Calls unary kernel <paramref name="index"/> on the value on the stack: directly when it is a static method, else through its delegate.</summary>
            private void CallUnary(int index, LocalBuilder argument)
            {
                Func<double, double> kernel = program.Unary[index];
                if (kernel.Target is null && kernel.Method.IsStatic)
                {
                    il.Emit(OpCodes.Ldloc, argument);
                    il.Emit(OpCodes.Call, kernel.Method);
                    return;
                }

                il.Emit(OpCodes.Ldarg, ArgUnary);
                il.Emit(OpCodes.Ldc_I4, index);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldloc, argument);
                il.Emit(OpCodes.Callvirt, UnaryInvoke);
            }

            private void CallGuard(int index, LocalBuilder argument)
            {
                Func<double, bool> guard = program.UnaryGuard[index]!;
                if (guard.Target is null && guard.Method.IsStatic)
                {
                    il.Emit(OpCodes.Ldloc, argument);
                    il.Emit(OpCodes.Call, guard.Method);
                    return;
                }

                il.Emit(OpCodes.Ldarg, ArgGuards);
                il.Emit(OpCodes.Ldc_I4, index);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Ldloc, argument);
                il.Emit(OpCodes.Callvirt, GuardInvoke);
            }

            private void Binary(RegOp op, OpCode code)
            {
                Load(op.A);
                Load(op.B);
                il.Emit(code);
                Store(op.Dest);
            }

            private void BinaryCall(RegOp op, MethodInfo method)
            {
                Load(op.A);
                Load(op.B);
                il.Emit(OpCodes.Call, method);
                Store(op.Dest);
            }

            /// <summary>regs[Dest] = the 0/1 int on the stack as a double.</summary>
            private void StoreTruth(RegOp op)
            {
                il.Emit(OpCodes.Conv_R8);
                Store(op.Dest);
            }

            /// <summary>The 0/1 int: whether the register is not zero (NaN is not zero).</summary>
            private void NotZero(int register)
            {
                Load(register);
                il.Emit(OpCodes.Ldc_R8, 0.0);
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ceq);
            }

            private void Op(int ip, RegOp op)
            {
                switch (op.Code)
                {
                    case LoopOp.Halt:
                        Exit(JgsLoopNative.Halted);
                        break;

                    case LoopOp.Step:
                        Charge();
                        break;

                    case LoopOp.IterTick:
                        Poll();
                        Charge();
                        break;

                    case LoopOp.Jump:
                        il.Emit(OpCodes.Br, _at[op.Arg]);
                        break;

                    case LoopOp.JumpIfFalse:
                        Load(op.A);
                        il.Emit(OpCodes.Ldc_R8, 0.0);
                        il.Emit(OpCodes.Beq, _at[op.Arg]); // == 0 jumps; NaN falls through
                        break;

                    case LoopOp.JumpIfTrue:
                        Load(op.A);
                        il.Emit(OpCodes.Ldc_R8, 0.0);
                        il.Emit(OpCodes.Bne_Un, _at[op.Arg]); // != 0 jumps; NaN jumps
                        break;

                    case LoopOp.Copy:
                        Load(op.A);
                        Store(op.Dest);
                        break;

                    case LoopOp.Bind:
                        Load(op.A);
                        Store(op.Dest);
                        SetFlag(_written, op.Dest, true);
                        SetFlag(_logical, op.Dest, op.B != 0);
                        SetFlag(_emptied, op.Dest, false);
                        break;

                    case LoopOp.BindVar:
                        Load(op.A);
                        Store(op.Dest);
                        SetFlag(_written, op.Dest, true);
                        il.Emit(OpCodes.Ldloc, _logical[op.A]);
                        il.Emit(OpCodes.Stloc, _logical[op.Dest]);
                        SetFlag(_emptied, op.Dest, false);
                        break;

                    case LoopOp.Add:
                        Binary(op, OpCodes.Add);
                        break;
                    case LoopOp.Sub:
                        Binary(op, OpCodes.Sub);
                        break;
                    case LoopOp.Mul:
                        Binary(op, OpCodes.Mul);
                        break;
                    case LoopOp.Div:
                        Binary(op, OpCodes.Div);
                        break;

                    case LoopOp.Neg:
                        Load(op.A);
                        il.Emit(OpCodes.Neg);
                        Store(op.Dest);
                        break;

                    case LoopOp.PowG:
                    {
                        Label real = il.DefineLabel();
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Call, PowerStaysReal);
                        il.Emit(OpCodes.Brtrue, real);
                        Exit(op.Arg);
                        il.MarkLabel(real);
                        BinaryCall(op, Pow);
                        break;
                    }

                    case LoopOp.Mod:
                        BinaryCall(op, ScalarMod);
                        break;
                    case LoopOp.Rem:
                        BinaryCall(op, ScalarRem);
                        break;
                    case LoopOp.Min2:
                        BinaryCall(op, Min);
                        break;
                    case LoopOp.Max2:
                        BinaryCall(op, Max);
                        break;
                    case LoopOp.Atan2:
                        BinaryCall(op, Atan2);
                        break;

                    case LoopOp.Lt:
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Clt);
                        StoreTruth(op);
                        break;
                    case LoopOp.Le:
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Cgt_Un); // a > b or unordered
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ceq);
                        StoreTruth(op);
                        break;
                    case LoopOp.Gt:
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Cgt);
                        StoreTruth(op);
                        break;
                    case LoopOp.Ge:
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Clt_Un); // a < b or unordered
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ceq);
                        StoreTruth(op);
                        break;
                    case LoopOp.Eq:
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Ceq);
                        StoreTruth(op);
                        break;
                    case LoopOp.Ne:
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Ceq);
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Ceq);
                        StoreTruth(op);
                        break;

                    case LoopOp.Not:
                        Load(op.A);
                        il.Emit(OpCodes.Ldc_R8, 0.0);
                        il.Emit(OpCodes.Ceq);
                        StoreTruth(op);
                        break;

                    case LoopOp.ToBool:
                        NotZero(op.A);
                        StoreTruth(op);
                        break;

                    case LoopOp.And:
                        NotZero(op.A);
                        NotZero(op.B);
                        il.Emit(OpCodes.And);
                        StoreTruth(op);
                        break;

                    case LoopOp.Or:
                        NotZero(op.A);
                        NotZero(op.B);
                        il.Emit(OpCodes.Or);
                        StoreTruth(op);
                        break;

                    case LoopOp.Call1:
                    {
                        LocalBuilder x = il.DeclareLocal(typeof(double));
                        Load(op.A);
                        il.Emit(OpCodes.Stloc, x);
                        CallUnary(op.B, x);
                        Store(op.Dest);
                        break;
                    }

                    case LoopOp.Call1G:
                    {
                        LocalBuilder x = il.DeclareLocal(typeof(double));
                        Label inside = il.DefineLabel();
                        Load(op.A);
                        il.Emit(OpCodes.Stloc, x);
                        CallGuard(op.B, x);
                        il.Emit(OpCodes.Brtrue, inside);
                        Exit(op.Arg);
                        il.MarkLabel(inside);
                        CallUnary(op.B, x);
                        Store(op.Dest);
                        break;
                    }

                    case LoopOp.RangeCount:
                    {
                        LocalBuilder count = il.DeclareLocal(typeof(long));
                        Label counted = il.DefineLabel();
                        Load(op.A);
                        Load(op.A + 1);
                        Load(op.A + 2);
                        il.Emit(OpCodes.Call, RangeCount);
                        il.Emit(OpCodes.Dup);
                        il.Emit(OpCodes.Stloc, count);
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Conv_I8);
                        il.Emit(OpCodes.Bge, counted);
                        Exit(op.Arg);
                        il.MarkLabel(counted);
                        il.Emit(OpCodes.Ldloc, count);
                        il.Emit(OpCodes.Conv_R8);
                        Store(op.A + 3);
                        il.Emit(OpCodes.Ldc_R8, 0.0);
                        Store(op.A + 4);
                        break;
                    }

                    case LoopOp.ForHead:
                    {
                        Label done = il.DefineLabel();
                        Label notFirst = il.DefineLabel();
                        Load(op.A + 4);
                        Load(op.A + 3);
                        il.Emit(OpCodes.Bge_Un, done); // not (index < count): NaN is done, as there
                        il.Emit(OpCodes.Br, _at[ip + 1]);
                        il.MarkLabel(done);
                        Load(op.A + 4);
                        il.Emit(OpCodes.Ldc_R8, 0.0);
                        il.Emit(OpCodes.Bne_Un, notFirst);
                        SetFlag(_emptied, op.Dest, true);
                        SetFlag(_written, op.Dest, true);
                        il.MarkLabel(notFirst);
                        il.Emit(OpCodes.Br, _at[op.Arg]);
                        break;
                    }

                    case LoopOp.ForBind:
                        Load(op.A);
                        Load(op.A + 4);
                        Load(op.A + 1);
                        il.Emit(OpCodes.Mul);
                        il.Emit(OpCodes.Add);
                        Store(op.Dest);
                        SetFlag(_written, op.Dest, true);
                        SetFlag(_logical, op.Dest, false);
                        SetFlag(_emptied, op.Dest, false);
                        break;

                    case LoopOp.ForNext:
                        Load(op.A + 4);
                        il.Emit(OpCodes.Ldc_R8, 1.0);
                        il.Emit(OpCodes.Add);
                        Store(op.A + 4);
                        il.Emit(OpCodes.Br, _at[op.Arg]);
                        break;

                    case LoopOp.ForStep:
                    {
                        Label more = il.DefineLabel();
                        LocalBuilder index = il.DeclareLocal(typeof(double));
                        Load(op.A + 4);
                        il.Emit(OpCodes.Ldc_R8, 1.0);
                        il.Emit(OpCodes.Add);
                        il.Emit(OpCodes.Dup);
                        il.Emit(OpCodes.Stloc, index);
                        Store(op.A + 4);
                        il.Emit(OpCodes.Ldloc, index);
                        Load(op.A + 3);
                        il.Emit(OpCodes.Blt_Un, more); // not (index >= count): NaN goes round, as there
                        il.Emit(OpCodes.Br, _at[ip + 1]);
                        il.MarkLabel(more);
                        Poll();
                        Charge();
                        Load(op.A);
                        il.Emit(OpCodes.Ldloc, index);
                        Load(op.A + 1);
                        il.Emit(OpCodes.Mul);
                        il.Emit(OpCodes.Add);
                        Store(op.Dest);
                        SetFlag(_written, op.Dest, true);
                        SetFlag(_logical, op.Dest, false);
                        SetFlag(_emptied, op.Dest, false);
                        il.Emit(OpCodes.Br, _at[op.Arg]);
                        break;
                    }

                    case LoopOp.StepBlock:
                    {
                        Label charge = il.DefineLabel();
                        il.Emit(OpCodes.Ldloc, _steps);
                        il.Emit(OpCodes.Ldc_I4, op.A);
                        il.Emit(OpCodes.Conv_I8);
                        il.Emit(OpCodes.Add);
                        il.Emit(OpCodes.Ldarg, ArgMaxSteps);
                        il.Emit(OpCodes.Ble, charge);
                        il.Emit(OpCodes.Br, _at[op.Arg]);
                        il.MarkLabel(charge);
                        il.Emit(OpCodes.Ldloc, _steps);
                        il.Emit(OpCodes.Ldc_I4, op.A);
                        il.Emit(OpCodes.Conv_I8);
                        il.Emit(OpCodes.Add);
                        il.Emit(OpCodes.Stloc, _steps);
                        break;
                    }

                    // The fused compare-and-branch jumps on the false side, NaN included.
                    case LoopOp.UnlessLt:
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Bge_Un, _at[op.Arg]);
                        break;
                    case LoopOp.UnlessLe:
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Bgt_Un, _at[op.Arg]);
                        break;
                    case LoopOp.UnlessGt:
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Ble_Un, _at[op.Arg]);
                        break;
                    case LoopOp.UnlessGe:
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Blt_Un, _at[op.Arg]);
                        break;
                    case LoopOp.UnlessEq:
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Bne_Un, _at[op.Arg]);
                        break;
                    case LoopOp.UnlessNe:
                        Load(op.A);
                        Load(op.B);
                        il.Emit(OpCodes.Beq, _at[op.Arg]);
                        break;

                    case LoopOp.Walk:
                        Exit(op.Arg);
                        break;

                    default:
                        throw new InvalidOperationException($"The loop emitter has no {op.Code}.");
                }
            }
        }
    }
}
