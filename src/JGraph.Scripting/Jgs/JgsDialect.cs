namespace JGraph.Scripting.Jgs;

/// <summary>
/// The language variant one run of the JGS pipeline speaks. JGS and MATLAB share the whole lexer →
/// parser → interpreter path; every place the two languages genuinely disagree reads a flag from here
/// instead of branching on a global, so a MATLAB run can never leak its semantics into a JGS one (or
/// the reverse). The two presets — <see cref="Jgs"/> and <see cref="Matlab"/> — are the only shapes
/// the hosts construct; <see cref="JgsWith"/> layers a user's JGS preferences on top of the former.
/// </summary>
/// <param name="Name">The language's name, as it appears in diagnostics.</param>
/// <param name="IndexBase">The index of the first element: 0 for JGS (ADR 0028), 1 for MATLAB.</param>
/// <param name="RequireLet">Whether a first assignment must say <c>let</c> (JGS's typo safety net).</param>
/// <param name="PercentComment">Whether <c>%</c> starts a comment (MATLAB) rather than being modulo (JGS).</param>
/// <param name="QuoteTranspose">Whether <c>'</c> is contextually transpose-or-char-literal, with
/// <c>''</c> escaping instead of backslashes (MATLAB).</param>
/// <param name="CopyOnAssign">Whether assigning an array/cell/struct copies it (MATLAB value semantics)
/// rather than sharing the reference (JGS).</param>
/// <param name="MatlabFunctions">Whether <c>function [a, b] = name(x)</c> declarations, multiple returns,
/// and <c>nargin</c>/<c>varargin</c> are available.</param>
/// <param name="MatlabBlocks">Whether <c>switch</c>/<c>try</c>/<c>global</c>, command syntax
/// (<c>hold on</c>) and <c>...</c> continuations are available.</param>
/// <param name="CellBraceSyntax">Whether <c>{...}</c> builds a cell array and <c>c{i}</c> indexes one.
/// False in JGS, where braces delimit block bodies; cell *values* still exist in both.</param>
/// <param name="FunctionScope">Whether an <c>if</c>/<c>for</c>/<c>while</c> body shares the enclosing
/// scope (MATLAB) rather than getting a child scope of its own (JGS).</param>
internal sealed record JgsDialect(
    string Name,
    int IndexBase,
    bool RequireLet,
    bool PercentComment,
    bool QuoteTranspose,
    bool CopyOnAssign,
    bool MatlabFunctions,
    bool MatlabBlocks,
    bool CellBraceSyntax,
    bool FunctionScope)
{
    /// <summary>JGS as shipped: 0-based, <c>let</c> required, <c>%</c> is modulo, references are shared.</summary>
    public static readonly JgsDialect Jgs = new(
        Name: "JGS",
        IndexBase: 0,
        RequireLet: true,
        PercentComment: false,
        QuoteTranspose: false,
        CopyOnAssign: false,
        MatlabFunctions: false,
        MatlabBlocks: false,
        CellBraceSyntax: false,
        FunctionScope: false);

    /// <summary>
    /// MATLAB semantics, fixed. A <c>.m</c> file must behave the same in JGraph as it does in MATLAB, so
    /// this preset is never adjustable by user settings — only the JGS side is.
    /// </summary>
    public static readonly JgsDialect Matlab = new(
        Name: "MATLAB",
        IndexBase: 1,
        RequireLet: false,
        PercentComment: true,
        QuoteTranspose: true,
        CopyOnAssign: true,
        MatlabFunctions: true,
        MatlabBlocks: true,
        CellBraceSyntax: true,
        FunctionScope: true);

    /// <summary>True when this dialect is MATLAB rather than JGS, for the handful of message flavours.</summary>
    public bool IsMatlab => PercentComment;

    /// <summary>
    /// JGS with the user's language preferences applied. Only the two options a user may change are
    /// taken from <paramref name="options"/>; everything else stays JGS.
    /// </summary>
    public static JgsDialect JgsWith(JgsLanguageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Jgs with { IndexBase = options.IndexBase, RequireLet = options.RequireLet };
    }
}
