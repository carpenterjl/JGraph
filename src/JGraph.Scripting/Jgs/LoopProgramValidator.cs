namespace JGraph.Scripting.Jgs;

/// <summary>
/// ADR 0160 (12c): proves, opcode by opcode, that every access a <see cref="RegisterProgram"/> can
/// make while it runs is inside the array it names — so the runner may read its register file,
/// its op list and its kernel tables without the runtime's bounds checks. The validator knows each
/// opcode's real operand domains: which of <c>Dest</c>/<c>A</c>/<c>B</c> are registers, which are
/// variable slots (the <c>written</c> and <c>logical</c> arrays are slot-sized), which are kernel
/// indices, whether <c>Arg</c> is an op index or a bail index, and that a for state block spans
/// <c>A..A+4</c>. Every check is with checked arithmetic. A program that fails is a compiler
/// defect; the compiler refuses the loop rather than run it, and the walk takes over.
/// </summary>
internal static class LoopProgramValidator
{
    /// <summary>Whether every access <paramref name="program"/> can make is provably in bounds.</summary>
    public static bool Validate(RegisterProgram program)
    {
        try
        {
            return Check(program);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool Check(RegisterProgram program)
    {
        RegOp[] ops = program.Ops;
        int registers = program.RegisterCount;
        int slots = program.Slots.Length;
        int kernels = program.Unary.Length;
        int bails = program.Bails.Length;
        int count = ops.Length;
        int vectors = program.VectorRegisterCount;
        int vectorSlots = program.VectorSlots.Length;
        if (count == 0 || registers < 0 || slots > registers || program.UnaryGuard.Length != kernels
            || vectors < 0 || vectorSlots > vectors
            || checked(program.ConstBase + program.Constants.Length) > registers
            || (program.OuterRegBase >= 0 && checked(program.OuterRegBase + 4) >= registers))
        {
            return false;
        }

        for (int ip = 0; ip < count; ip++)
        {
            RegOp op = ops[ip];
            bool fallsThrough = true;
            switch (op.Code)
            {
                case LoopOp.Halt:
                    fallsThrough = false;
                    break;

                case LoopOp.Step:
                case LoopOp.IterTick:
                    break;

                case LoopOp.Jump:
                    fallsThrough = false;
                    if (!OpIndex(op.Arg, count)) return false;
                    break;

                case LoopOp.JumpIfFalse:
                case LoopOp.JumpIfTrue:
                    if (!Reg(op.A, registers) || !OpIndex(op.Arg, count)) return false;
                    break;

                case LoopOp.UnlessLt:
                case LoopOp.UnlessLe:
                case LoopOp.UnlessGt:
                case LoopOp.UnlessGe:
                case LoopOp.UnlessEq:
                case LoopOp.UnlessNe:
                    if (!Reg(op.A, registers) || !Reg(op.B, registers) || !OpIndex(op.Arg, count)) return false;
                    break;

                case LoopOp.StepBlock:
                    if (!OpIndex(op.Arg, count)) return false;
                    break;

                case LoopOp.Copy:
                case LoopOp.Neg:
                case LoopOp.Not:
                case LoopOp.ToBool:
                    if (!Reg(op.Dest, registers) || !Reg(op.A, registers)) return false;
                    break;

                case LoopOp.Bind:
                case LoopOp.BindVar:
                    // Dest is a slot (written/logical are slot-sized); BindVar reads logical[A] too.
                    if (!Reg(op.Dest, slots) || !Reg(op.A, registers)) return false;
                    if (op.Code == LoopOp.BindVar && !Reg(op.A, slots)) return false;
                    break;

                case LoopOp.Add:
                case LoopOp.Sub:
                case LoopOp.Mul:
                case LoopOp.Div:
                case LoopOp.Mod:
                case LoopOp.Rem:
                case LoopOp.Min2:
                case LoopOp.Max2:
                case LoopOp.Atan2:
                case LoopOp.Lt:
                case LoopOp.Le:
                case LoopOp.Gt:
                case LoopOp.Ge:
                case LoopOp.Eq:
                case LoopOp.Ne:
                case LoopOp.And:
                case LoopOp.Or:
                    if (!Reg(op.Dest, registers) || !Reg(op.A, registers) || !Reg(op.B, registers)) return false;
                    break;

                case LoopOp.PowG:
                    if (!Reg(op.Dest, registers) || !Reg(op.A, registers) || !Reg(op.B, registers) || !Reg(op.Arg, bails)) return false;
                    break;

                case LoopOp.Call1:
                    if (!Reg(op.Dest, registers) || !Reg(op.A, registers) || !Reg(op.B, kernels)) return false;
                    break;

                case LoopOp.Call1G:
                    if (!Reg(op.Dest, registers) || !Reg(op.A, registers) || !Reg(op.B, kernels)
                        || program.UnaryGuard[op.B] is null || !Reg(op.Arg, bails)) return false;
                    break;

                case LoopOp.RangeCount:
                    if (!ForState(op.A, registers) || !Reg(op.Arg, bails)) return false;
                    break;

                case LoopOp.ForHead:
                case LoopOp.ForNext:
                    // ForHead's Dest is the loop variable's slot: a zero-trip head marks it (V8).
                    if (!ForState(op.A, registers) || !OpIndex(op.Arg, count)) return false;
                    if (op.Code == LoopOp.ForHead && !Reg(op.Dest, slots)) return false;
                    fallsThrough = op.Code == LoopOp.ForHead;
                    break;

                case LoopOp.ForBind:
                    if (!Reg(op.Dest, slots) || !ForState(op.A, registers)) return false;
                    break;

                case LoopOp.ForStep:
                    if (!Reg(op.Dest, slots) || !ForState(op.A, registers) || !OpIndex(op.Arg, count)) return false;
                    break;

                // ADR 0160 (12d): vector registers, slots, nodes and kernels by opcode.
                case LoopOp.VLoad:
                    if (!Reg(op.Dest, registers) || !Reg(op.A, vectors) || !Reg(op.B, registers) || !Reg(op.Arg, bails)) return false;
                    break;

                case LoopOp.VStore:
                    if (!Reg(op.Dest, vectorSlots) || !Reg(op.A, registers) || !Reg(op.B, registers) || !Reg(op.Arg, bails)) return false;
                    break;

                case LoopOp.VArithVV:
                case LoopOp.VArithVS:
                case LoopOp.VArithSV:
                    if (!Reg(op.Dest, vectors) || !Reg(op.Arg, bails) || !Node<BinaryExpr>(program, op.C)) return false;
                    if (!Reg(op.A, op.Code == LoopOp.VArithSV ? registers : vectors)) return false;
                    if (!Reg(op.B, op.Code == LoopOp.VArithVS ? registers : vectors)) return false;
                    break;

                case LoopOp.VNeg:
                    if (!Reg(op.Dest, vectors) || !Reg(op.A, vectors) || !Reg(op.Arg, bails) || !Node<UnaryExpr>(program, op.C)) return false;
                    break;

                case LoopOp.VCall1:
                    if (!Reg(op.Dest, vectors) || !Reg(op.A, vectors) || !Reg(op.B, kernels) || !Reg(op.B, program.UnaryNames.Length)
                        || !Reg(op.Arg, bails) || !Node<CallExpr>(program, op.C)) return false;
                    break;

                case LoopOp.VBind:
                    if (!Reg(op.Dest, vectorSlots) || !Reg(op.A, vectors)) return false;
                    break;

                case LoopOp.Walk:
                    if (!Reg(op.Arg, bails)) return false;
                    break;

                default:
                    return false; // an opcode the validator does not know is not proved
            }

            if (fallsThrough && checked(ip + 1) >= count)
            {
                return false;
            }
        }

        foreach (LoopBail bail in program.Bails)
        {
            switch (bail.Kind)
            {
                case LoopBailKind.Statement:
                    if (!OpIndex(bail.Resume, count) || !OpIndex(bail.BreakIp, count) || !OpIndex(bail.ContinueIp, count)) return false;
                    break;
                case LoopBailKind.Condition:
                    if (!OpIndex(bail.OnTrue, count) || !OpIndex(bail.OnFalse, count)) return false;
                    break;
                case LoopBailKind.Bound:
                    if (!OpIndex(bail.Resume, count) || !Reg(bail.DestReg, registers)) return false;
                    break;
                default:
                    return false;
            }

            foreach (int slot in bail.Touched)
            {
                if (!Reg(slot, slots)) return false;
            }

            foreach (int slot in bail.TouchedVectors)
            {
                if (!Reg(slot, vectorSlots)) return false;
            }
        }

        return true;
    }

    private static bool Reg(int index, int length) => index >= 0 && index < length;

    private static bool Node<TNode>(RegisterProgram program, int index) where TNode : Node =>
        index >= 0 && index < program.Nodes.Length && program.Nodes[index] is TNode;

    private static bool OpIndex(int index, int count) => index >= 0 && index < count;

    private static bool ForState(int stateBase, int registers) => stateBase >= 0 && checked(stateBase + 4) < registers;
}
