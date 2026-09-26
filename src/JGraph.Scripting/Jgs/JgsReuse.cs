namespace JGraph.Scripting.Jgs;

/// <summary>
/// The switches for reused arithmetic temporaries (Z2 of the value-ownership plan, ADR 0173):
/// whether an operator may write its answer into a fresh temporary operand (Z2a) and whether a
/// plain <c>v = v op E</c> may write into <c>v</c>'s own payload (Z2b), and from what length.
/// </summary>
/// <remarks>
/// Both roads rest on the kernel alias contract (<c>PackedMath.Binary</c>'s remarks) and on the
/// ownership model: a temporary is reused only when this evaluation alone holds it (M109's
/// <c>OwnsFreshResult</c>), and a variable is updated in place only when its payload has one holder
/// (M1), carries no exposed mark (M6), is not held by a scope (M5) and would be the binding the
/// assignment replaces. Turning the switch off restores the allocating roads and nothing else, so
/// a difference between the two is a defect of the reuse roads by definition — that is what
/// <c>ArithmeticReuseM173Tests</c> and the <c>temporary_reuse</c> fixture compare.
/// <c>JGRAPH_REUSE=0</c> is the kill switch.
/// </remarks>
internal static class JgsReuse
{
    /// <summary>The built-in default before any environment override.</summary>
    public const bool DefaultEnabled = true;

    /// <summary>Whether the reuse roads are open.</summary>
    public static bool Enabled { get; set; } = ReadEnvironmentOverride() ?? DefaultEnabled;

    /// <summary>
    /// The default of <see cref="MinElements"/>: 64K elements (512 KB). Below it a temporary is
    /// cheap and every printed array, every parity fixture and every hand-checked value lives
    /// there, so the allocating roads keep those exactly as they were.
    /// </summary>
    public const int DefaultMinElements = 1 << 16;

    /// <summary>Length at or above which the reuse roads apply. Settable so a test can move it.</summary>
    public static int MinElements { get; set; } = DefaultMinElements;

    /// <summary>
    /// The default of <see cref="InPlaceNoPollElements"/>: 16M elements (128 MB). An in-place
    /// update takes no cancellation poll — a cancelled sweep would leave <c>v</c> half written —
    /// so above this length it takes the allocating road, which polls between grains and leaves
    /// <c>v</c> untouched when cancelled.
    /// </summary>
    public const int DefaultInPlaceNoPollElements = 1 << 24;

    /// <summary>Length above which <c>v = v op E</c> allocates rather than updating in place.</summary>
    public static int InPlaceNoPollElements { get; set; } = DefaultInPlaceNoPollElements;

    /// <summary>
    /// The default of <see cref="InPlaceThreadElements"/> (Z2c): 256K elements. An in-place sweep
    /// reads two arrays and writes one of them, all resident, and at 490K elements its single
    /// thread moves 14 GB/s where four move 26; the reductions and copies that the general
    /// memory-bound threshold governs measured slower at this length, so the threshold moves for
    /// the reuse roads alone.
    /// </summary>
    public const int DefaultInPlaceThreadElements = 1 << 18;

    /// <summary>Length from which an in-place or fused sweep is threaded (<c>JGRAPH_REUSE_THREADS</c>, elements).</summary>
    public static int InPlaceThreadElements { get; set; } = ReadCount("JGRAPH_REUSE_THREADS") ?? DefaultInPlaceThreadElements;

    /// <summary>
    /// The built-in default of <see cref="UninitializedDestinations"/> (Z2e): whether an arithmetic
    /// destination the kernel writes in full is allocated without zeroing. On: the zeroing was a
    /// pass over memory nothing read.
    /// </summary>
    public const bool DefaultUninitializedDestinations = true;

    /// <summary>Whether arithmetic destinations skip zeroing (<c>JGRAPH_REUSE_UNINIT=1|0</c>).</summary>
    public static bool UninitializedDestinations { get; set; } =
        ReadSwitch("JGRAPH_REUSE_UNINIT") ?? DefaultUninitializedDestinations;

    /// <summary>Which operands of a binary operator are fresh temporaries the answer may be written into.</summary>
    [Flags]
    public enum Operands
    {
        /// <summary>Neither: allocate.</summary>
        None = 0,

        /// <summary>The left operand is a fresh temporary.</summary>
        Left = 1,

        /// <summary>The right operand is a fresh temporary.</summary>
        Right = 2,
    }

    /// <summary>The candidates an operator may write into, given which of its operands are fresh.</summary>
    public static Operands Candidates(bool leftFresh, bool rightFresh)
    {
        if (!Enabled)
        {
            return Operands.None;
        }

        return (leftFresh ? Operands.Left : Operands.None) | (rightFresh ? Operands.Right : Operands.None);
    }

    /// <summary>
    /// Whether a plain assignment's right-hand side is <c>v op E</c> or <c>E op v</c> for the name
    /// being assigned, with an operator the in-place road knows: elementwise <c>+ - .* ./</c>, and
    /// <c>* /</c>, which are elementwise exactly when the other operand turns out to be a scalar.
    /// The walk and the loop compiler ask the same question.
    /// </summary>
    public static bool IsUpdateShape(BinaryExpr update, string name) =>
        update.Op is TokenType.Plus or TokenType.Minus or TokenType.DotStar or TokenType.DotSlash
            or TokenType.Star or TokenType.Slash
        && (update.Left is VariableExpr { } left && left.Name == name
            || update.Right is VariableExpr { } right && right.Name == name);

    /// <summary>The candidates after the operands have been swapped (<c>a .\ b</c> is <c>b ./ a</c>).</summary>
    public static Operands Swapped(Operands reuse) =>
        ((reuse & Operands.Left) != 0 ? Operands.Right : Operands.None)
        | ((reuse & Operands.Right) != 0 ? Operands.Left : Operands.None);

    private static bool? ReadEnvironmentOverride() => ReadSwitch("JGRAPH_REUSE");

    private static int? ReadCount(string name) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int asked) && asked > 0 ? asked : null;

    private static bool? ReadSwitch(string name) =>
        Environment.GetEnvironmentVariable(name) switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null,
        };
}
