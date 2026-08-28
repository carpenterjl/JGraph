namespace JGraph.Scripting.Jgs;

/// <summary>
/// The switch for the hot-loop register compiler (M98). While enabled, a MATLAB <c>for</c> or
/// <c>while</c> whose body works entirely in scalar doubles is compiled once to a register program
/// and executed without the tree walk; while disabled, every loop takes the classic walk. Either way
/// the environment variable <c>JGRAPH_LOOP_JIT=1|0</c> forces the mode, which is also the
/// parity-test lever — the two roads must print byte-identical output.
/// </summary>
internal static class JgsLoopJit
{
    /// <summary>The built-in default before any environment override.</summary>
    public const bool DefaultEnabled = true;

    /// <summary>Whether eligible loops compile to register programs.</summary>
    public static bool Enabled { get; set; } = ReadEnvironmentOverride() ?? DefaultEnabled;

    /// <summary>
    /// How many loops have run compiled in this process — the tests' way of asserting that a script
    /// took (or was refused) the fast path, since a correct fast path is invisible in the output.
    /// </summary>
    internal static long CompiledRuns;

    private static bool? ReadEnvironmentOverride() =>
        Environment.GetEnvironmentVariable("JGRAPH_LOOP_JIT") switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null,
        };
}

/// <summary>The operations a compiled loop is made of. Each reads and writes the double register file.</summary>
internal enum LoopOp : byte
{
    /// <summary>End of the program: the loop completed normally.</summary>
    Halt,

    /// <summary>One statement's step charge — the compiled twin of the <c>Tick()</c> each statement pays.</summary>
    Step,

    /// <summary>One iteration's charge: the cancellation poll plus the loop's own step.</summary>
    IterTick,

    /// <summary>Unconditional jump to <c>Arg</c>.</summary>
    Jump,

    /// <summary>Jump to <c>Arg</c> when <c>regs[A] == 0</c> — false under the same rule as <c>IsTruthy</c> (NaN is true).</summary>
    JumpIfFalse,

    /// <summary>Jump to <c>Arg</c> when <c>regs[A] != 0</c>.</summary>
    JumpIfTrue,

    /// <summary>regs[Dest] = regs[A].</summary>
    Copy,

    /// <summary>Commit a value to a variable slot: regs[Dest] = regs[A], marking it written; B is 1 when the value is a logical.</summary>
    Bind,

    /// <summary>
    /// Commit one variable to another (<c>t = u</c>): the value copies as it is, logical or not, so
    /// the kind rides along from slot A at run time rather than being decided at compile time.
    /// </summary>
    BindVar,

    Add,
    Sub,
    Mul,
    Div,

    /// <summary>regs[Dest] = -regs[A].</summary>
    Neg,

    /// <summary>Guarded power: <c>Math.Pow</c> when <c>PowerStaysReal</c>, else bail <c>Arg</c> — the answer is complex.</summary>
    PowG,

    /// <summary>MATLAB mod (result takes the divisor's sign) — the same arithmetic the builtin maps.</summary>
    Mod,

    /// <summary>MATLAB rem (result takes the dividend's sign).</summary>
    Rem,

    /// <summary>Two-argument scalar min, exactly the fold the wrapped builtin reaches: <c>Math.Min</c>.</summary>
    Min2,

    /// <summary>Two-argument scalar max: <c>Math.Max</c>.</summary>
    Max2,

    Atan2,

    /// <summary>Comparisons: regs[Dest] = 1.0 or 0.0, the double reading of the Bool the walk mints.</summary>
    Lt,
    Le,
    Gt,
    Ge,
    Eq,
    Ne,

    /// <summary>Logical not: regs[Dest] = regs[A] == 0 ? 1 : 0 (the scalar <c>~</c> and <c>!</c>).</summary>
    Not,

    /// <summary>Truthiness as a value: regs[Dest] = regs[A] != 0 ? 1 : 0 (materializes <c>&amp;&amp;</c>/<c>||</c> results).</summary>
    ToBool,

    /// <summary>Scalar <c>&amp;</c>: both nonzero.</summary>
    And,

    /// <summary>Scalar <c>|</c>: either nonzero.</summary>
    Or,

    /// <summary>regs[Dest] = unary kernel B applied to regs[A]; the kernel never leaves the reals.</summary>
    Call1,

    /// <summary>Like <see cref="Call1"/>, but the kernel has a real domain: outside it, bail <c>Arg</c>.</summary>
    Call1G,

    /// <summary>
    /// A nested range head: regs[A..A+2] hold start/step/stop; writes count to regs[A+3] and zero to
    /// the index regs[A+4], replicating <c>EvaluateRange</c>'s count rule exactly. The conditions the
    /// walk answers by throwing (zero step, too many elements) bail to <c>Arg</c> instead, where the
    /// walk throws the identical error.
    /// </summary>
    RangeCount,

    /// <summary>Loop head: when regs[A+4] &gt;= regs[A+3], jump to <c>Arg</c> (the loop is done).</summary>
    ForHead,

    /// <summary>Bind the loop variable: regs[Dest] = start + i*step, the exact element the range would hold.</summary>
    ForBind,

    /// <summary>Back edge: regs[A+4] += 1, jump to <c>Arg</c> (the head).</summary>
    ForNext,
}

/// <summary>One operation of a compiled loop. <c>Arg</c> is a jump target or a bail index by opcode.</summary>
internal readonly struct RegOp(LoopOp code, ushort dest, ushort a, ushort b, int arg)
{
    public readonly LoopOp Code = code;

    public readonly ushort Dest = dest;

    public readonly ushort A = a;

    public readonly ushort B = b;

    public readonly int Arg = arg;
}

/// <summary>One variable of a compiled loop: its name and what the program needs from it at entry.</summary>
internal sealed class LoopSlot(string name)
{
    public string Name { get; } = name;

    /// <summary>
    /// Whether some read may see the variable's pre-loop value, so it must be bound to a plain real
    /// scalar at entry — anything else refuses the fast path before it starts.
    /// </summary>
    public bool EntryRequired { get; set; }

    /// <summary>
    /// Whether the slot is some loop's variable, which the walk binds with <c>Declare</c> rather than
    /// the assign-outward walk — the difference spill must reproduce.
    /// </summary>
    public bool IsLoopVariable { get; set; }
}

/// <summary>Why a compiled op handed control back to the tree walk, and where to pick up after.</summary>
internal enum LoopBailKind : byte
{
    /// <summary>Re-execute one whole statement by the walk (the answer left the reals, or the walk must throw).</summary>
    Statement,

    /// <summary>Re-evaluate one condition expression by the walk and branch on its truthiness.</summary>
    Condition,

    /// <summary>Re-evaluate one range bound by the walk and deposit the number it answers.</summary>
    Bound,
}

/// <summary>
/// One level of the nesting between a compiled loop's root and a bailing statement — enough to finish
/// the loop by the tree walk when a bailed statement leaves a variable the registers cannot hold.
/// </summary>
internal sealed class LoopDeoptFrame
{
    /// <summary>The statement list this level runs (the loop body or an if branch).</summary>
    public required IReadOnlyList<Stmt> Block { get; init; }

    /// <summary>The index within <see cref="Block"/> of the statement the next level is inside.</summary>
    public required int Index { get; init; }

    /// <summary>The for loop whose body <see cref="Block"/> is, or null for an if branch or while body.</summary>
    public ForStmt? For { get; init; }

    /// <summary>The while loop whose body <see cref="Block"/> is, or null.</summary>
    public WhileStmt? While { get; init; }

    /// <summary>For a for level: the register base of its (start, step, stop, count, index) block.</summary>
    public int RegBase { get; init; }
}

/// <summary>Everything a bail needs: what to run by the walk, and where the program resumes.</summary>
internal sealed class LoopBail
{
    public required LoopBailKind Kind { get; init; }

    /// <summary>The statement to re-execute (<see cref="LoopBailKind.Statement"/>).</summary>
    public Stmt? Statement { get; init; }

    /// <summary>The expression to re-evaluate (<see cref="LoopBailKind.Condition"/> and <see cref="LoopBailKind.Bound"/>).</summary>
    public Expr? Expression { get; init; }

    /// <summary>Which range bound the expression is ("start", "step", "stop") — names the walk's own error.</summary>
    public string What { get; init; } = "";

    /// <summary>Where the bound's value lands (<see cref="LoopBailKind.Bound"/>).</summary>
    public int DestReg { get; init; }

    /// <summary>The op index after the statement or bound (normal resume).</summary>
    public int Resume { get; set; }

    /// <summary>Branch targets for a re-evaluated condition.</summary>
    public int OnTrue { get; set; }

    public int OnFalse { get; set; }

    /// <summary>Where a Break completion from the re-executed statement lands (the enclosing loop's exit).</summary>
    public int BreakIp { get; set; }

    /// <summary>Where a Continue completion lands (the enclosing loop's back edge).</summary>
    public int ContinueIp { get; set; }

    /// <summary>The nesting from the root loop down to the bailed statement, outermost first.</summary>
    public LoopDeoptFrame[] Path { get; init; } = [];
}

/// <summary>
/// A loop compiled to a linear program over an unboxed double register file (M98). Registers are laid
/// out variables first, then the constant pool, then loop state and scratch; the program is pure data
/// and carries no interpreter state, so one compilation serves every entry of the loop.
/// </summary>
internal sealed class RegisterProgram
{
    public required RegOp[] Ops { get; init; }

    /// <summary>The variables, slot by slot; slot i is register i.</summary>
    public required LoopSlot[] Slots { get; init; }

    /// <summary>The constant pool, loaded into registers starting at <see cref="ConstBase"/> on entry.</summary>
    public required double[] Constants { get; init; }

    public required int ConstBase { get; init; }

    public required int RegisterCount { get; init; }

    /// <summary>The unary kernels <see cref="LoopOp.Call1"/>/<see cref="LoopOp.Call1G"/> index — the builtins' own scalar cores.</summary>
    public required Func<double, double>[] Unary { get; init; }

    /// <summary>The real-domain guard beside each guarded unary kernel (null for the unguarded).</summary>
    public required Func<double, bool>?[] UnaryGuard { get; init; }

    /// <summary>
    /// Every builtin name the program bound a kernel for. At each entry the name must still resolve
    /// to the builtin of that name — a shadowed or rebound name refuses the fast path, and the walk
    /// does whatever the script arranged.
    /// </summary>
    public required string[] RequiredBuiltins { get; init; }

    /// <summary>The bail table.</summary>
    public required LoopBail[] Bails { get; init; }

    /// <summary>
    /// For a for-loop root: the register base of the outer (start, step, stop, count, index) block,
    /// primed by the runner from the walked bounds. -1 for a while root.
    /// </summary>
    public required int OuterRegBase { get; init; }

    /// <summary>The loop this program was compiled from.</summary>
    public required Stmt Root { get; init; }

    /// <summary>
    /// Every statement the compilation consumed, in preorder. A debug hook may edit statement lists
    /// in place, so each entry re-walks the tree and compares references; any difference recompiles.
    /// </summary>
    public required Stmt[] Snapshot { get; init; }
}
