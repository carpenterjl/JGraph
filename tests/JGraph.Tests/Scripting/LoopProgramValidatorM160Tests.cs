using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// ADR 0160 (12c): the validator is what makes the unchecked runner sound, so it is tested on
/// programs built by hand — one bad operand of each kind the runner would otherwise read past the
/// end of an array — and on the smallest good ones. Every program the compiler emits is validated
/// on the way out (a failure refuses the loop), so the parity tests cover the positive side.
/// </summary>
public class LoopProgramValidatorM160Tests
{
    private static RegisterProgram Program(RegOp[] ops, int registers = 8, LoopBail[]? bails = null, int vectors = 0, int vectorSlots = 0)
    {
        var root = new ExprStmt(new NumberLiteral(0));
        return new RegisterProgram
        {
            Ops = ops,
            Slots = [new LoopSlot("a"), new LoopSlot("b")],
            Constants = [0, 1],
            ConstBase = 2,
            RegisterCount = registers,
            Unary = [Math.Sin, Math.Sqrt],
            UnaryGuard = [null, static x => x >= 0],
            UnaryNames = ["sin", "sqrt"],
            RequiredBuiltins = ["sin", "sqrt"],
            Bails = bails ?? [],
            OuterRegBase = -1,
            Root = root,
            Snapshot = [root],
            VectorSlots = Enumerable.Range(0, vectorSlots).Select(static i => new LoopSlot("v" + i)).ToArray(),
            VectorRegisterCount = vectors,
            Nodes = [new BinaryExpr(TokenType.Plus, new NumberLiteral(1), new NumberLiteral(2))],
        };
    }

    private static RegOp Op(LoopOp code, int dest = 0, int a = 0, int b = 0, int arg = 0, int c = 0) =>
        new(code, (ushort)dest, (ushort)a, (ushort)b, arg, (ushort)c);

    [Fact]
    public void TheSmallestProgramsProve()
    {
        Assert.True(LoopProgramValidator.Validate(Program([Op(LoopOp.Halt)])));
        Assert.True(LoopProgramValidator.Validate(Program([Op(LoopOp.Add, 4, 0, 1), Op(LoopOp.Bind, 1, 4), Op(LoopOp.Halt)])));
        Assert.True(LoopProgramValidator.Validate(Program([Op(LoopOp.Jump, arg: 0)])));
        Assert.True(LoopProgramValidator.Validate(Program([Op(LoopOp.ForHead, a: 3, arg: 2), Op(LoopOp.ForNext, a: 3, arg: 0), Op(LoopOp.Halt)])));
        Assert.True(LoopProgramValidator.Validate(Program([Op(LoopOp.Call1, 4, 0, 0), Op(LoopOp.Halt)])));
    }

    [Fact]
    public void AnEmptyProgramOrOneThatFallsOffTheEndDoesNot()
    {
        Assert.False(LoopProgramValidator.Validate(Program([])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.Add, 4, 0, 1)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.Step)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.ForHead, a: 3, arg: 1), Op(LoopOp.IterTick)])));
    }

    [Fact]
    public void AJumpPastTheEndDoesNot()
    {
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.Jump, arg: 1)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.JumpIfFalse, a: 0, arg: -1), Op(LoopOp.Halt)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.UnlessLt, a: 0, b: 1, arg: 2), Op(LoopOp.Halt)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.StepBlock, a: 2, arg: 7), Op(LoopOp.Halt)])));
    }

    [Fact]
    public void ARegisterOperandAtOrPastTheFileDoesNot()
    {
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.Add, 8, 0, 1), Op(LoopOp.Halt)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.Add, 4, 8, 1), Op(LoopOp.Halt)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.Neg, 4, 9), Op(LoopOp.Halt)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.UnlessGe, a: 0, b: 8, arg: 1), Op(LoopOp.Halt)])));
    }

    [Fact]
    public void ASlotOperandPastTheSlotsDoesNot()
    {
        // written[] and logical[] are slot-sized: a Bind into register 4 is a valid register and an
        // invalid slot.
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.Bind, 4, 0), Op(LoopOp.Halt)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.BindVar, 0, 4), Op(LoopOp.Halt)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.ForBind, 2, 3), Op(LoopOp.Halt)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.ForStep, 2, 3, arg: 0), Op(LoopOp.Halt)])));
    }

    [Fact]
    public void AForStateBlockThatRunsPastTheFileDoesNot()
    {
        Assert.True(LoopProgramValidator.Validate(Program([Op(LoopOp.ForHead, a: 3, arg: 1), Op(LoopOp.Halt)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.ForHead, a: 4, arg: 1), Op(LoopOp.Halt)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.RangeCount, a: 4, arg: 0), Op(LoopOp.Halt)],
            bails: [new LoopBail { Kind = LoopBailKind.Statement, Resume = 1, BreakIp = 1, ContinueIp = 1 }])));
    }

    [Fact]
    public void AKernelIndexPastTheTableOrAnUnguardedGuardedCallDoesNot()
    {
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.Call1, 4, 0, 2), Op(LoopOp.Halt)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.Call1G, 4, 0, 0, arg: 0), Op(LoopOp.Halt)],
            bails: [new LoopBail { Kind = LoopBailKind.Statement, Resume = 1, BreakIp = 1, ContinueIp = 1 }])));
        Assert.True(LoopProgramValidator.Validate(Program([Op(LoopOp.Call1G, 4, 0, 1, arg: 0), Op(LoopOp.Halt)],
            bails: [new LoopBail { Kind = LoopBailKind.Statement, Resume = 1, BreakIp = 1, ContinueIp = 1 }])));
    }

    [Fact]
    public void ABailIndexOrABailTargetOutsideTheProgramDoesNot()
    {
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.PowG, 4, 0, 1, arg: 0), Op(LoopOp.Halt)])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.PowG, 4, 0, 1, arg: 0), Op(LoopOp.Halt)],
            bails: [new LoopBail { Kind = LoopBailKind.Statement, Resume = -1, BreakIp = 1, ContinueIp = 1 }])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.PowG, 4, 0, 1, arg: 0), Op(LoopOp.Halt)],
            bails: [new LoopBail { Kind = LoopBailKind.Condition, OnTrue = 1, OnFalse = 2 }])));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.PowG, 4, 0, 1, arg: 0), Op(LoopOp.Halt)],
            bails: [new LoopBail { Kind = LoopBailKind.Statement, Resume = 1, BreakIp = 1, ContinueIp = 1, Touched = [2] }])));
    }

    [Fact]
    public void AVectorOperandPastTheVectorFileDoesNot()
    {
        LoopBail[] bail = [new LoopBail { Kind = LoopBailKind.Statement, Resume = 1, BreakIp = 1, ContinueIp = 1 }];
        Assert.True(LoopProgramValidator.Validate(Program([Op(LoopOp.VLoad, 4, 0, 1, arg: 0), Op(LoopOp.Halt)], bails: bail, vectors: 2, vectorSlots: 1)));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.VLoad, 4, 2, 1, arg: 0), Op(LoopOp.Halt)], bails: bail, vectors: 2, vectorSlots: 1)));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.VStore, 1, 0, 1, arg: 0), Op(LoopOp.Halt)], bails: bail, vectors: 2, vectorSlots: 1)));
        Assert.True(LoopProgramValidator.Validate(Program([Op(LoopOp.VArithVV, 1, 0, 0, arg: 0, c: 0), Op(LoopOp.Halt)], bails: bail, vectors: 2, vectorSlots: 1)));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.VArithVV, 1, 0, 0, arg: 0, c: 1), Op(LoopOp.Halt)], bails: bail, vectors: 2, vectorSlots: 1)));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.VNeg, 1, 0, arg: 0, c: 0), Op(LoopOp.Halt)], bails: bail, vectors: 2, vectorSlots: 1)));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.VBind, 1, 0), Op(LoopOp.Halt)], vectors: 2, vectorSlots: 1)));
        Assert.False(LoopProgramValidator.Validate(Program([Op(LoopOp.Walk, arg: 1), Op(LoopOp.Halt)], bails: bail)));
    }

    [Fact]
    public void AnOpcodeTheValidatorDoesNotKnowDoesNot() =>
        Assert.False(LoopProgramValidator.Validate(Program([Op((LoopOp)200), Op(LoopOp.Halt)])));
}
