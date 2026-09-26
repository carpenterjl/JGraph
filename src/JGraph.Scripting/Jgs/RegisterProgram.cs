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

    /// <summary>
    /// How many times a compiled loop handed a statement, a condition or a bound back to the tree
    /// walk in this process — the tests' way of asserting that a shape guard really bailed
    /// (ADR 0160, 12d) rather than the fast path silently computing something else.
    /// </summary>
    internal static long Bails;

    /// <summary>
    /// V8 (ADR 0169): how many times the entry check refused a compiled program in this process.
    /// The check runs after a <c>for</c> loop's bounds, so a bound that rebinds, demotes or clears
    /// a vector the body writes, or shadows a builtin it calls, is refused here and the walk runs
    /// over the evaluated steps — the tests' way of asserting that road was the one taken.
    /// </summary>
    internal static long EntryRefusals;

    /// <summary>
    /// ADR 0160 (12b): a comparison that decides a branch is one op (<c>UnlessLt</c> and its
    /// siblings) and a for loop's back edge is one op (<c>ForStep</c>). Off, every comparison is a
    /// value op and a <c>JumpIfFalse</c>, and the back edge is <c>ForNext</c> to the head.
    /// <c>JGRAPH_LOOP_FUSE=1|0</c> forces it; the measurement lever, and a parity lever.
    /// </summary>
    public static bool Fusion { get; set; } = ReadSwitch("JGRAPH_LOOP_FUSE") ?? true;

    /// <summary>
    /// ADR 0160 (12b): the statements of a straight-line block are charged against the step limit
    /// once, at the block's start, after a precheck that sends a block the limit would interrupt
    /// down a per-statement copy of itself. Off, every statement pays its own <c>Step</c>.
    /// <c>JGRAPH_LOOP_BLOCKS=1|0</c> forces it.
    /// </summary>
    public static bool ChargeBlocks { get; set; } = ReadSwitch("JGRAPH_LOOP_BLOCKS") ?? true;

    /// <summary>
    /// ADR 0160 (12c): the runner reads its register file, op list and kernel tables without the
    /// runtime's bounds checks, on the strength of the validator's proof over
    /// every operand of every op. Off, the checked accessor runs. <c>JGRAPH_LOOP_UNCHECKED=1|0</c>
    /// forces it.
    /// </summary>
    public static bool Unchecked { get; set; } = ReadSwitch("JGRAPH_LOOP_UNCHECKED") ?? true;

    /// <summary>
    /// ADR 0160 (12d): the guarded vector class — a packed real double row or column read by a
    /// scalar index, written by a scalar index, combined elementwise — and the walked statement,
    /// which hands one statement outside the whitelist to the tree walk each time instead of
    /// refusing the loop. Off, both refuse as they did before the item.
    /// <c>JGRAPH_LOOP_VECTORS=1|0</c> forces it.
    /// </summary>
    public static bool Vectors { get; set; } = ReadSwitch("JGRAPH_LOOP_VECTORS") ?? true;

    /// <summary>
    /// <c>JGRAPH_LOOP_TRACE=1</c>: every refusal — the compiler's, with where in the compiler it
    /// refused, and the entry check's, with the variable or builtin that failed it — is written
    /// to standard error, so a loop that walks when it was expected to compile explains itself.
    /// </summary>
    public static bool Trace { get; set; } = ReadSwitch("JGRAPH_LOOP_TRACE") ?? false;

    /// <summary>Writes one refusal line when <see cref="Trace"/> is on.</summary>
    internal static void Refused(string what, Node at)
    {
        if (Trace)
        {
            Console.Error.WriteLine($"loop-jit: ({at.Line},{at.Column}) {what}");
        }
    }

    private static bool? ReadEnvironmentOverride() => ReadSwitch("JGRAPH_LOOP_JIT");

    private static bool? ReadSwitch(string variable) =>
        Environment.GetEnvironmentVariable(variable) switch
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

    /// <summary>
    /// ADR 0160 (12b): the charge of a straight-line block — <c>A</c> statements at once. When
    /// <c>steps + A</c> would pass the limit, jump to <c>Arg</c> instead: a copy of the block that
    /// charges statement by statement, so the limit is reported at the same statement with the same
    /// registers as before. Otherwise charge and fall through.
    /// </summary>
    StepBlock,

    /// <summary>
    /// ADR 0160 (12b): the fused compare-and-branch. Jump to <c>Arg</c> unless
    /// <c>regs[A] &lt; regs[B]</c> — the false side jumps, as <see cref="JumpIfFalse"/> after
    /// <see cref="Lt"/> did, so a NaN operand takes the jump.
    /// </summary>
    UnlessLt,
    UnlessLe,
    UnlessGt,
    UnlessGe,
    UnlessEq,
    UnlessNe,

    /// <summary>
    /// ADR 0160 (12b): the fused for back edge. <c>regs[A+4] += 1</c>; when the loop is done, fall
    /// through to the exit (the very next op); otherwise the head's own three ops in the head's own
    /// order — the cancellation poll, the iteration's step, the bind of the loop variable into
    /// <c>Dest</c> — and jump to <c>Arg</c>, the first op of the body.
    /// </summary>
    ForStep,

    /// <summary>
    /// ADR 0160 (12d): <c>v(i)</c> — regs[Dest] = element regs[B] of vector register A, the index
    /// validated as <c>PackedOps.ToIndex</c> validates it (whole, finite, inside the extent under
    /// base 1); anything else bails <c>Arg</c> and the walk throws its own words.
    /// </summary>
    VLoad,

    /// <summary>
    /// ADR 0160 (12d): <c>x(i) = s</c> — element regs[A] of vector slot Dest becomes regs[B], in
    /// place, as the walk's own element write does; an index outside the extent (the walk grows
    /// the array), a fractional one (the walk throws), or a logical value in slot B (the walk
    /// demotes the array to boxed) bails <c>Arg</c>.
    /// </summary>
    VStore,

    /// <summary>
    /// ADR 0160 (12d): the binary operator of node C applied by the walk's own
    /// <c>ApplyBinary</c> to vector register A and vector register B, the answer a fresh vector in
    /// vector register Dest. An answer that is not a real double row or column (the operands'
    /// shapes expanded, the answer went complex) bails <c>Arg</c>.
    /// </summary>
    VArithVV,

    /// <summary>As <see cref="VArithVV"/> with a scalar on the right: vector A, regs[B].</summary>
    VArithVS,

    /// <summary>As <see cref="VArithVV"/> with a scalar on the left: regs[A], vector B.</summary>
    VArithSV,

    /// <summary>ADR 0160 (12d): unary minus of node C on vector register A into vector register Dest, by the walk's own <c>ApplyUnary</c>.</summary>
    VNeg,

    /// <summary>
    /// ADR 0160 (12d): the builtin of kernel index B (the very function the name resolves to at
    /// entry) called on vector register A at node C, its answer into vector register Dest; an
    /// answer that is not a real double row or column bails <c>Arg</c>.
    /// </summary>
    VCall1,

    /// <summary>
    /// ADR 0160 (12d): whole-variable assignment — vector slot Dest takes vector register A: a
    /// fresh temporary is adopted as the walk adopts an owned answer, and another slot is copied
    /// as <c>CopyForBinding</c> copies it.
    /// </summary>
    VBind,

    /// <summary>
    /// Z2b (ADR 0173): <c>v = v op E</c> or <c>v = E op v</c> for vector slot Dest, the binary
    /// operator of node C. The other operand is vector register A when bit 2 of B is set and
    /// scalar register A otherwise; bit 1 of B says the slot is the left operand. When the slot's
    /// wrapper has one holder, no exposed mark, the answer's shape and no growth slack, the kernel
    /// writes the answer over it and the slot is marked written; otherwise the walk's own
    /// <c>ApplyBinary</c> answers a fresh vector that the slot adopts as <see cref="VBind"/> would.
    /// An answer outside the class bails <c>Arg</c>.
    /// </summary>
    VUpdate,

    /// <summary>
    /// ADR 0160 (12d): a statement outside the whitelist, handed to the walk every time — bail
    /// <c>Arg</c> unconditionally, then resume.
    /// </summary>
    Walk,
}

/// <summary>
/// One operation of a compiled loop. <c>Arg</c> is a jump target or a bail index by opcode;
/// <c>C</c> is the index of the AST node an op evaluates through the walk's own functions
/// (ADR 0160, 12d), and zero for every other op.
/// </summary>
internal readonly struct RegOp(LoopOp code, ushort dest, ushort a, ushort b, int arg, ushort c = 0)
{
    public readonly LoopOp Code = code;

    public readonly ushort Dest = dest;

    public readonly ushort A = a;

    public readonly ushort B = b;

    public readonly ushort C = c;

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

    /// <summary>
    /// The slots the walked statement may have rebound — an assignment's target, a walked
    /// statement's every assigned name. After the walk runs it, their registers are stale whatever
    /// the program had done with them before, so the reload treats them as written: a value no
    /// register can hold finishes the loop by the walk rather than reading the old register.
    /// </summary>
    public int[] Touched { get; init; } = [];

    /// <summary>The vector slots the walked statement may have rebound (ADR 0160, 12d); see <see cref="Touched"/>.</summary>
    public int[] TouchedVectors { get; init; } = [];

    /// <summary>
    /// Whether this is a walked statement (ADR 0160, 12d): one the program never compiled, so
    /// after the walk runs it every builtin the program bound is checked to still resolve — a
    /// walked statement may have rebound a name in ways a compiled one cannot.
    /// </summary>
    public bool IsWalk { get; init; }
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
    /// The builtin name beside each unary kernel (ADR 0160, 12d): a vector op calls the builtin
    /// itself, resolved at entry, rather than the scalar core.
    /// </summary>
    public string[] UnaryNames { get; init; } = [];

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

    /// <summary>
    /// ADR 0160 (12d): the vector variables, slot by slot; vector register i is vector slot i for
    /// i below the slot count, and a per-statement temporary above it.
    /// </summary>
    public LoopSlot[] VectorSlots { get; init; } = [];

    /// <summary>The vector register file's size: the vector slots and the temporaries after them.</summary>
    public int VectorRegisterCount { get; init; }

    /// <summary>The AST nodes the vector ops evaluate through, by <see cref="RegOp.C"/>.</summary>
    public Node[] Nodes { get; init; } = [];
}
