namespace JGraph.Scripting.Jgs;

/// <summary>
/// The base type for every error the JGS language raises — lexing, parsing, and interpretation. Each error
/// records the 1-based <see cref="Line"/> and <see cref="Column"/> in the script it refers to (0 when the
/// location is unknown) so the engine can map it onto a <see cref="ScriptDiagnostic"/>.
/// </summary>
public abstract class JgsException : Exception
{
    /// <summary>Creates the exception at a 1-based <paramref name="line"/>/<paramref name="column"/>.</summary>
    protected JgsException(int line, int column, string message)
        : base(message)
    {
        Line = line;
        Column = column;
    }

    /// <summary>The 1-based line the error refers to, or 0 when unknown.</summary>
    public int Line { get; private set; }

    /// <summary>The 1-based column the error refers to, or 0 when unknown.</summary>
    public int Column { get; private set; }

    /// <summary>
    /// Gives an error raised with no location the location of the statement it failed in (open
    /// item 306): an engine warning raised as an error is thrown where no line is known.
    /// </summary>
    internal void PlaceAt(int line, int column)
    {
        Line = line;
        Column = column;
    }

    /// <summary>
    /// The file <see cref="Line"/> and <see cref="Column"/> count in, or "" when the error belongs to
    /// the source being run (or to no file at all).
    /// </summary>
    /// <remarks>
    /// An error is raised knowing its line and not its file, because the thousands of sites that
    /// raise one see an expression and an expression carries no path. The file is put on as the error
    /// leaves the frame that ran it — a script called by name, a function loaded from the path — and
    /// only the first frame to claim it is believed, since the innermost is where the line is.
    /// Without this a failure in <c>sub.m</c> read as <c>(4,12)</c> with nothing to say that line 4 of
    /// the main script was blameless.
    /// </remarks>
    public string SourceId { get; private set; } = "";

    /// <summary>Names the file the error's location refers to, unless one is already named.</summary>
    internal void AttributeTo(string sourceId)
    {
        if (SourceId.Length == 0 && sourceId.Length > 0)
        {
            SourceId = sourceId;
        }
    }
}

/// <summary>An error raised while lexing or parsing a script (a malformed program).</summary>
public sealed class JgsSyntaxException : JgsException
{
    /// <summary>Creates a syntax error at a 1-based <paramref name="line"/>/<paramref name="column"/>.</summary>
    public JgsSyntaxException(int line, int column, string message)
        : base(line, column, message)
    {
    }

    /// <summary>
    /// MATLAB's identifier for the error, when the refusal is one MATLAB makes with an identifier
    /// of its own (<c>MATLAB:m_missing_operator</c>), so that <c>eval</c> of the text raises it under
    /// that identifier; null for JGraph's own refusals.
    /// </summary>
    public string? Identifier { get; init; }
}

/// <summary>An error raised while interpreting a script (a well-formed program that failed at run time).</summary>
public sealed class JgsRuntimeException : JgsException
{
    /// <summary>Creates a runtime error at a 1-based <paramref name="line"/>/<paramref name="column"/>.</summary>
    public JgsRuntimeException(int line, int column, string message)
        : base(line, column, message)
    {
    }

    /// <summary>Creates a runtime error carrying MATLAB's <c>component:mnemonic</c> identifier.</summary>
    public JgsRuntimeException(int line, int column, string identifier, string message)
        : base(line, column, message)
    {
        Identifier = identifier;
    }

    /// <summary>
    /// The <c>component:mnemonic</c> identifier a script can branch on in a <c>catch</c>, or the empty
    /// string for the errors the runtime raises itself.
    /// </summary>
    /// <remarks>
    /// Empty is the honest answer for a runtime error, and a deliberate one: inventing identifiers for
    /// the interpreter's own messages would mean promising spellings that MATLAB's do not match, and a
    /// script that switched on one would take the wrong branch on real MATLAB. Only what a script
    /// itself raised carries an identifier — see ADR 0062.
    /// </remarks>
    public string Identifier { get; } = string.Empty;

    /// <summary>Whether the debugger's hook has already seen this error at the statement that raised it (W11): it pauses for an error once, at the innermost block.</summary>
    internal bool DebuggerSaw { get; set; }

    /// <summary>
    /// Raised at line 0 by code that knows no location, to take the line of the statement it fails
    /// in (open item 306, ADR 0352): <see cref="JgsBuiltins.Warn(JGraphScriptGlobals, string, string)"/>'s warning raised as an error.
    /// </summary>
    internal bool TakesStatementLine { get; init; }

    /// <summary>
    /// The exception filter of a block's statement loop: places an error that takes its statement's
    /// line at <paramref name="statement"/>, and never catches.
    /// </summary>
    internal static bool PlaceAtStatement(JgsRuntimeException error, Stmt? statement)
    {
        if (error.TakesStatementLine && error.Line == 0 && error.Frames.Count == 0 && statement is not null)
        {
            error.PlaceAt(statement.Line, statement.Column);
        }

        error.AnchorStatement(statement); // getReport's caret under a call made as a statement (open item 336)
        return false;
    }

    private readonly List<(string Name, string File, int Line)> _frames = [];
    private int _lineForNextFrame = -1;

    /// <summary>The functions this error unwound through, innermost first — what <c>ME.stack</c> reports.</summary>
    public IReadOnlyList<(string Name, string File, int Line)> Frames => _frames;

    /// <summary>
    /// Records that the error escaped <paramref name="name"/>, which was entered from
    /// <paramref name="callLine"/>. Called as each frame unwinds, so the frames arrive innermost-first
    /// and each carries the line that was executing <em>in it</em>: the innermost failed where the
    /// error is, and every outer one failed at the call it was waiting on.
    /// </summary>
    internal void PushFrame(string name, string file, int callLine) =>
        PushFrame(name, file, callLine, PendingLine);

    /// <summary>
    /// <see cref="PushFrame(string, string, int)"/> with the frame's own <paramref name="line"/> given: an
    /// anonymous function made from text is at line 0 whatever ran in it (open item 334, ADR 0368).
    /// </summary>
    internal void PushFrame(string name, string file, int callLine, int line)
    {
        _frames.Add((name, file, line));
        _lineForNextFrame = callLine;
    }

    /// <summary>
    /// The frame the error is leaving is one R2025b's stack leaves out (an anonymous body whose text holds
    /// <c>&gt;%</c>; open item 336, ADR 0378): the caller is reached from <paramref name="callLine"/> with no
    /// frame recorded, and what the body's expressions anchored is forgotten, so the caller's call anchors.
    /// </summary>
    internal void SkipFrame(int callLine)
    {
        if (_frames.Count == 0)
        {
            PlacedOutsideFrame = true; // raised in the body: its place is not the caller's
        }

        _lineForNextFrame = callLine;
        ForgetAnchor();
    }

    /// <summary>
    /// The names R2025b's report writes for the frames, by frame index, where the stack's name alone does
    /// not say it: a class method (<c>pk.C.stat</c>, <c>pk.C/inst</c>, the constructor <c>pk.C</c>; open
    /// item 336, ADR 0378). Null until a frame needs one.
    /// </summary>
    private Dictionary<int, string>? _shownNames;

    /// <summary>Names the frame <see cref="PushFrame(string, string, int, int)"/> recorded last as R2025b's report writes it.</summary>
    internal void ShowLastFrameAs(string shown) =>
        (_shownNames ??= [])[_frames.Count - 1] = shown;

    /// <summary>The report's name for the frame at <paramref name="index"/>, or null when the stack's name says it.</summary>
    internal string? ShownNameAt(int index) =>
        _shownNames is not null && _shownNames.TryGetValue(index, out string? shown) ? shown : null;

    /// <summary>
    /// Where the expression each frame was running when the error left it begins, by frame index: what
    /// R2025b's <c>getReport</c> underlines under each "Error in" line (open item 336, ADR 0378). The
    /// frame an exception filter runs in is the one at <see cref="Frames"/>' count, since every frame
    /// unwound before it was pushed by a catch; the innermost expression's filter runs first and keeps it.
    /// </summary>
    private Dictionary<int, ReportAnchor>? _anchors;

    /// <summary>The anchor recorded for the frame at <paramref name="index"/>, or null.</summary>
    internal ReportAnchor? AnchorAt(int index) =>
        _anchors is not null && _anchors.TryGetValue(index, out ReportAnchor anchor) ? anchor : null;

    /// <summary>
    /// An exception filter around the evaluation of <paramref name="expression"/>, a call or a name called
    /// bare: anchors the running frame at it unless an expression inside it has, and never catches. In the
    /// frame the error was raised in, an error placed inside the call's argument list came from an argument
    /// that anchors nothing itself (a name, an operator), and is anchored where it was raised.
    /// </summary>
    internal bool AnchorAt(Expr expression)
    {
        int index = _frames.Count;
        if (_anchors is not null && _anchors.ContainsKey(index))
        {
            return false;
        }

        (int line, int column) = ReportAnchor.StartOf(expression);
        bool raisedHere = index == 0 && Line > 0 && Column > 0 && !PlacedOutsideFrame
            && (Line != expression.Line || Column > expression.Column);
        (_anchors ??= [])[index] = raisedHere
            ? new ReportAnchor(Line, Column, Chain: false)
            : new ReportAnchor(line, column, Chain: true);
        return false;
    }

    /// <summary>
    /// The statement loop's half of <see cref="AnchorAt(Expr)"/>: a call written as a statement, or the call
    /// an assignment's right side is, anchors a frame whose expressions anchored nothing on the way out.
    /// </summary>
    internal void AnchorStatement(Stmt? statement)
    {
        Expr? called = statement switch
        {
            ExprStmt { Expression: CallExpr or VariableExpr } bare => bare.Expression,
            ExprStmt { Expression: AssignExpr { Value: CallExpr or VariableExpr } assign } => assign.Value,
            MultiAssignStmt multi => multi.Call,
            _ => null,
        };

        if (called is not null)
        {
            AnchorAt(called);
        }
    }

    /// <summary>
    /// The anchor of the running frame is forgotten: the expression that set it was eval'd text, whose
    /// columns are not the file's, or a frame R2025b leaves out (<see cref="SkipFrame"/>).
    /// </summary>
    internal void ForgetAnchor() => _anchors?.Remove(_frames.Count);

    /// <summary>
    /// <c>eval</c>'s exception filter: an error leaving eval'd text forgets what the text anchored, so the
    /// <c>eval</c> call anchors the frame (R2025b underlines <c>eval(...)</c>), and an error raised in the text
    /// itself keeps its frame among the "Error in" lines, as R2025b's report does (measured). Never catches.
    /// </summary>
    internal bool LeavesEvalText()
    {
        ForgetAnchor();
        if (_frames.Count == 0)
        {
            ShowsRaisingFrame = true;
            PlacedOutsideFrame = true;
        }

        return false;
    }

    /// <summary>
    /// The error's place is not in the frame the report starts from - it was raised in eval'd text, whose
    /// columns count in the text, or in a body <see cref="SkipFrame"/> left out - so no anchor is taken from it.
    /// </summary>
    internal bool PlacedOutsideFrame { get; private set; }

    /// <summary>
    /// The error was raised in eval'd text in the frame that ran the <c>eval</c>: R2025b's report lists that
    /// frame under "Error in" although its header names it too (open item 336, measured).
    /// </summary>
    internal bool ShowsRaisingFrame { get; private set; }

    /// <summary>
    /// The line that was running in the frame the error has reached but not yet left: its own line
    /// before any frame unwound, and after that the call the last unwound frame was entered from.
    /// </summary>
    internal int PendingLine => _lineForNextFrame < 0 ? Line : _lineForNextFrame;

    /// <summary>
    /// The exception value a script threw (V6, ADR 0167): <c>throw</c>, <c>rethrow</c> and
    /// <c>throwAsCaller</c> carry the whole MException — its causes with it — so the
    /// <c>catch</c> that ends the unwind hands back that value rather than one rebuilt from an
    /// identifier and a message. Null for an error the runtime or <c>error(...)</c> raised.
    /// </summary>
    internal object? Carried { get; init; }

    /// <summary><c>rethrow</c>: the carried exception keeps the stack it already has.</summary>
    internal bool KeepsStack { get; init; }

    /// <summary>
    /// The built-in whose call this error refused before it ran (V9, ADR 0170: a call asked for more
    /// outputs than <c>disp</c> or <c>error</c> has), which unwinds through no frame of its own. An
    /// <c>ErrorHandler</c>'s record puts it under R2025b's "Error using &lt;name&gt;" header the way
    /// a frame's name would go; the message itself stays what <c>ME.message</c> reports.
    /// </summary>
    internal string? UsingName { get; init; }

    /// <summary><c>throwAsCaller</c>: the stack leaves out the frame that called it.</summary>
    internal bool DropsThrowingFrame { get; init; }

    /// <summary>
    /// Raised by <c>error</c>, <c>throw</c>, <c>rethrow</c> or <c>throwAsCaller</c> rather than refused
    /// by the runtime: R2025b's <c>getReport(ME, 'basic')</c> heads such an error with the frame it
    /// came from, and a runtime refusal (an index past the end) with nothing (open item 13).
    /// </summary>
    internal bool Raised { get; init; }

    /// <summary>
    /// This error as one its function raised itself, which is how R2025b reports an arguments
    /// block's refusal: "Error using k9nv (line 1)" (stage K9). One that already carries frames, or
    /// was raised, is left as it is.
    /// </summary>
    internal JgsRuntimeException AsRaised() =>
        Raised || Frames.Count > 0 ? this : new JgsRuntimeException(Line, Column, Identifier, Message) { Raised = true };
}
