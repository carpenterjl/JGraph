namespace JGraph.Scripting.Jgs;

/// <summary>Something a JGS script can call: a built-in or a user-defined <c>fn</c>.</summary>
internal interface IJgsCallable
{
    /// <summary>The name used in diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// Invokes the callable with already-evaluated <paramref name="arguments"/>. The
    /// <paramref name="line"/>/<paramref name="column"/> locate the call site for error reporting.
    /// </summary>
    JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column);
}

/// <summary>
/// Something that can produce several values at once, for MATLAB's <c>[a, b] = f(x)</c>. A callable
/// that does not implement this simply produces its single value.
/// </summary>
internal interface IJgsMultiCallable
{
    /// <summary>
    /// Invokes the callable asking for <paramref name="wanted"/> outputs. It may return fewer (the
    /// caller reports the shortfall) but never more than it can produce. Zero is a statement call
    /// (V9, ADR 0170): the callee sees <c>nargout</c> 0 and hands back the first output it made
    /// anyway - a user function's assigned first output, a builtin's value - which the statement
    /// binds to <c>ans</c>; a callee that made none hands back none, without error.
    /// </summary>
    JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column);
}

/// <summary>
/// How the runtime invokes a callback it holds - a timer's, a listener's, a graphics callback, a
/// function file run as the program: asked for nothing (V9.1, ADR 0170), as MATLAB invokes one,
/// so a callback whose body calls a function with no outputs is not refused for an output it was
/// never asked for. A function argument a builtin evaluates for its value (an integrand, an
/// objective) is asked for one through <see cref="IJgsCallable.Call"/> as before.
/// </summary>
internal static class JgsCallbacks
{
    /// <summary>Invokes <paramref name="callback"/> asked for no outputs.</summary>
    public static void Invoke(IJgsCallable callback, IReadOnlyList<JgsValue> arguments, int line, int column)
    {
        if (callback is IJgsMultiCallable several)
        {
            several.CallMultiple(arguments, 0, line, column);
        }
        else
        {
            callback.Call(arguments, line, column);
        }
    }
}

/// <summary>A built-in function implemented in C#, exposed to scripts by name.</summary>
internal sealed class BuiltinFunction : IJgsCallable, IJgsMultiCallable
{
    private readonly Func<IReadOnlyList<JgsValue>, int, int, JgsValue> _implementation;

    /// <summary>Creates a built-in named <paramref name="name"/> backed by <paramref name="implementation"/>.</summary>
    public BuiltinFunction(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> implementation)
    {
        Name = name;
        _implementation = implementation;

        // Decided from the name at mint time rather than stamped onto the environment afterwards
        // (M63). Several builtins are declared twice — an inner one that does the work and an outer
        // wrapper that adds a second output — and only the wrapper is reachable from the environment.
        // Marking by name reaches both; marking the environment reached the wrapper alone, which is
        // how size("abc") came back 1-by-3 while numel("abc") correctly came back 1.
        KeepsStringArguments = JgsBuiltins.StringAwareBuiltins.Contains(name);
        MintsAnswer = JgsBuiltins.MintingBuiltins.Contains(name);
        RunsScript = JgsBuiltins.ScriptRunningBuiltins.Contains(name);
        ForwardsCallSite = name == "feval";
        if (JgsBuiltinOutputCounts.TryGet(name, out int count, out bool isFile))
        {
            MatlabOutputCount = count;
            IsMatlabFile = isFile;
        }
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// R2025b's <c>nargout</c> for the name (<see cref="JgsBuiltinOutputCounts"/>) when it is
    /// fixed, or null - what <c>nargout('name')</c> answers here (V9.3).
    /// </summary>
    public int? MatlabOutputCount { get; }

    /// <summary>
    /// The most outputs a call may ask of this builtin, or null when unbounded here: the recorded
    /// count when it is zero, or when this builtin has only its single-output body; a builtin with
    /// a multi-output body answers for itself, because R2025b's <c>nargout</c> under-reports a
    /// C built-in's second output (<c>fgets</c> reports 1 and gives two), sees a class constructor
    /// where a toolbox overload answers two (<c>tf</c> of a digitalFilter), and cannot see what
    /// this build gives beyond MATLAB's own (<c>triplot</c>'s coordinates) - the stress suite found
    /// all three. Such a body handing back fewer than asked is the shortfall the multiple
    /// assignment refuses as <c>MATLAB:maxlhs</c>. A call asking for more than the maximum is
    /// refused before the body runs (V9.3, ADR 0170).
    /// </summary>
    public int? MaxOutputs =>
        MatlabOutputCount is int count && (count == 0 || MultiOutput is null) ? count : null;

    /// <summary>
    /// Whether MATLAB keeps this name as a file rather than a built-in, which decides the refusal's
    /// identifier: a file refuses excess outputs as <c>MATLAB:TooManyOutputs</c>, a built-in as
    /// <c>MATLAB:maxlhs</c> (measured, V9).
    /// </summary>
    public bool IsMatlabFile { get; }

    /// <summary>
    /// Whether this builtin hands the interpreter's pending call site on to the callable it invokes
    /// (<c>feval</c> alone: the written <c>feval(f, x)</c> minus <c>f</c>). Every other builtin that
    /// runs script code (<see cref="RunsScript"/>) has no argument syntax for the callee it calls,
    /// so the roads clear the site before its body and <c>inputname</c> in the callback answers ''
    /// (V9.2, ADR 0170; measured for <c>cellfun</c>, <c>arrayfun</c>, <c>structfun</c> and an
    /// <c>ErrorHandler</c>).
    /// </summary>
    public bool ForwardsCallSite { get; }

    /// <summary>
    /// Whether a binding may adopt this builtin's answer rather than take a counted share of it
    /// (V2.2, M2): true only for a name on <see cref="JgsBuiltins.MintingBuiltins"/>, each of which
    /// the ownership audit has shown returns only wrappers it minted.
    /// </summary>
    public bool MintsAnswer { get; }

    /// <summary>
    /// Whether this builtin's body can run script code — call a callable it was handed, evaluate
    /// text, or drain queued callbacks (V3, M5). A call of one holds every argument as a counted
    /// share for the whole call, so a callback's write to a variable leaves the argument the builtin
    /// is still walking alone. True only for a name on <see cref="JgsBuiltins.ScriptRunningBuiltins"/>,
    /// which the ownership audit checks against every body that reaches a script entry point.
    /// </summary>
    public bool RunsScript { get; }

    /// <summary>
    /// Whether a bare call statement binds this built-in's result to <c>ans</c> and echoes it.
    /// MATLAB's <c>figure(1)</c> as a statement prints nothing, while <c>h = figure(1)</c> still hands
    /// back the handle; with no nargout plumbing in the interpreter, this flag is how the two differ.
    /// </summary>
    public bool BindsAnsAsStatement { get; init; } = true;

    /// <summary>
    /// Whether a bare mention of this name means its value rather than the function itself, so
    /// <c>x = eps</c> stores 2.2e-16 and not a handle. MATLAB implements its constants as
    /// zero-argument functions and calls them on sight; the flag opts a builtin into that without
    /// disturbing ordinary names, which stay callable values (<c>f = @sin</c>, <c>sin(x)</c>).
    /// </summary>
    public bool AutoCallsBare { get; init; }

    /// <summary>
    /// Optional multi-output implementation, for MATLAB's <c>[X, Y] = meshgrid(x, y)</c>: given
    /// (arguments, wanted, line, column), produces up to <c>wanted</c> outputs. Null for the many
    /// builtins that only ever produce one value — asking those for more reports a shortfall.
    /// </summary>
    public Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? MultiOutput { get; init; }

    /// <summary>
    /// Whether this builtin is told when its answer is being thrown away — a call written as a
    /// statement reaches <see cref="MultiOutput"/> with <c>wanted</c> zero rather than one.
    /// </summary>
    /// <remarks>
    /// Almost nothing needs this: a function that answers the same thing either way cannot tell the
    /// difference and should not ask. What needs it is the handful of names that <em>draw</em> when
    /// nobody wanted the numbers — <c>ecdf(x)</c> on its own is a plot, and <c>F = ecdf(x)</c> is a
    /// vector — which is a distinction that cannot be made after the fact.
    /// </remarks>
    public bool KnowsWhenDiscarded { get; init; }

    /// <summary>
    /// Whether this builtin takes the output count it was asked for through
    /// <see cref="MultiOutput"/> at every count, zero included, because the count is part of what
    /// it does (V9.1, ADR 0170): <c>feval</c>, <c>cellfun</c>, <c>arrayfun</c> and <c>structfun</c>
    /// hand it on to the callable they invoke, and <c>load</c> binds the loaded names into the
    /// workspace only when asked for none. Unlike <see cref="KnowsWhenDiscarded"/>, a statement
    /// call of one still binds <c>ans</c> to whatever it handed back - <c>feval(@sin, 0);</c> does.
    /// </summary>
    public bool TakesOutputCount { get; init; }

    /// <summary>
    /// Whether this builtin wants its string arguments as they were written (M63). False for nearly
    /// everything, and that is the point: a string scalar arriving at an ordinary builtin is demoted
    /// to the char row it stands for, so <c>title("Speed")</c> and <c>plot(x, y, "LineWidth", 2)</c>
    /// went on working the day <c>"..."</c> stopped being a char, without any of the ~2,500 builtins
    /// being touched.
    /// </summary>
    /// <remarks>
    /// Set it on the names for which the difference between a string and a char <em>is</em> the
    /// answer — <c>class</c>, <c>isstring</c>, <c>ischar</c>, <c>strlength</c>, <c>string</c>,
    /// <c>char</c>, <c>numel</c> and the rest of the size family — and nowhere else. Only a string
    /// <em>scalar</em> is ever demoted; a string array of any other size is not a char row and is
    /// handed over as it is.
    /// </remarks>
    public bool KeepsStringArguments { get; set; }

    /// <inheritdoc />
    public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
        Guarded(() => _implementation(DemoteStringScalars(arguments), line, column), line, column);

    /// <summary>
    /// The body alone, with none of the work <see cref="Call"/> does around it, for a caller that
    /// has already done that work itself.
    /// </summary>
    /// <remarks>
    /// The one caller is the elementwise text wrapper, which runs a builtin once per element of a
    /// string array. Everything <see cref="Call"/> adds — a closure and a delegate for the guard, a
    /// walk over the arguments and an array for the demotion — depends on the arguments beside the
    /// text, and those do not change as the map walks. Done once for the container rather than once
    /// per element, they stop being most of what the call costs.
    /// </remarks>
    internal JgsValue Invoke(IReadOnlyList<JgsValue> arguments, int line, int column) =>
        _implementation(arguments, line, column);

    /// <summary>Runs <paramref name="body"/> under this builtin's own exception translation.</summary>
    internal T Protect<T>(Func<T> body, int line, int column) => Guarded(body, line, column);

    /// <summary>One argument as <see cref="Call"/> would hand it to the body.</summary>
    internal JgsValue Demote(JgsValue value) =>
        !KeepsStringArguments && IsStringScalar(value) ? value.ElementAt(0) : value;

    /// <inheritdoc />
    /// <remarks>
    /// The multi-output body answers a call for several, and a call for none when the builtin
    /// cares about the difference (<see cref="KnowsWhenDiscarded"/>, <see cref="TakesOutputCount"/>);
    /// every other count is the single-output body, whose value a statement call binds to
    /// <c>ans</c> as R2025b binds <c>size(1);</c> and <c>h = @sin; h(1);</c> (measured, V9).
    /// </remarks>
    public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column) =>
        MultiOutput is { } multi && (wanted > 1 || (wanted == 0 && (KnowsWhenDiscarded || TakesOutputCount)))
            ? Guarded(() => multi(DemoteStringScalars(arguments), wanted, line, column), line, column)
            : [Call(arguments, line, column)];

    /// <summary>
    /// Runs a <see cref="KnowsWhenDiscarded"/> builtin with nobody wanting its answer. The interpreter
    /// reaches <see cref="MultiOutput"/> directly for that case, and going through here rather than
    /// calling the delegate is what gives the discarded arm the same string demotion and the same
    /// exception translation every other arm has — without it, <c>dir fix</c> handed <c>dir</c> the
    /// command word as a string array where <c>dir('fix')</c> handed it a char row (M109).
    /// </summary>
    public void CallDiscarded(IReadOnlyList<JgsValue> arguments, int line, int column) =>
        Guarded(() => MultiOutput!(DemoteStringScalars(arguments), 0, line, column), line, column);

    /// <summary>
    /// Runs a builtin's body and turns the argument-shaped exceptions the layers underneath throw
    /// into script errors a script can catch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numeric routines report a bad shape by throwing <see cref="ArgumentException"/> — "QR
    /// factorization here needs at least as many rows as columns" is a sentence written for a person
    /// to read. Nothing between here and <c>Main</c> caught it: the interpreter's <c>try</c> catches
    /// <see cref="JgsRuntimeException"/> alone and the runner catches <see cref="JgsException"/>
    /// alone, so <c>qr</c> of a wide matrix ended the process. A script must not be able to do that,
    /// whatever it passes.
    /// </para>
    /// <para>
    /// Only the four kinds that mean "these arguments are wrong" are translated. A
    /// <see cref="JgsException"/> is already a script error and passes through with the line it was
    /// raised at; a cancellation and a script's own <c>exit</c> are control flow and must reach the
    /// runner that is listening for them; anything else is a defect in this build rather than in the
    /// script, and is left to the runner's own last resort so that it is reported as one.
    /// </para>
    /// </remarks>
    private T Guarded<T>(Func<T> body, int line, int column)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or ArithmeticException or IndexOutOfRangeException or KeyNotFoundException
            or FormatException)
        {
            throw new JgsRuntimeException(line, column, $"{Name}: {MessageOf(ex)}");
        }
    }

    /// <summary>
    /// An exception's message without the parameter name .NET appends to it — "needs a square
    /// matrix. (Parameter 'matrix')" reads as an internal detail to someone writing a script.
    /// </summary>
    private static string MessageOf(Exception ex) =>
        ex is ArgumentException { ParamName: { Length: > 0 } name }
            ? ex.Message.Replace($" (Parameter '{name}')", string.Empty, StringComparison.Ordinal)
            : ex.Message;

    /// <summary>
    /// Replaces every string scalar in <paramref name="arguments"/> with its char row, allocating a
    /// new list only when there is one to replace — which is almost never, so the check costs a walk
    /// over a handful of already-hot values and nothing else.
    /// </summary>
    private IReadOnlyList<JgsValue> DemoteStringScalars(IReadOnlyList<JgsValue> arguments)
    {
        if (KeepsStringArguments)
        {
            return arguments;
        }

        int first = -1;
        for (int i = 0; i < arguments.Count; i++)
        {
            if (IsStringScalar(arguments[i]))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            return arguments;
        }

        var demoted = new JgsValue[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
        {
            demoted[i] = IsStringScalar(arguments[i]) ? arguments[i].ElementAt(0) : arguments[i];
        }

        return demoted;
    }

    /// <summary>Whether <paramref name="value"/> is a 1-by-1 string array — the string scalar.</summary>
    internal static bool IsStringScalar(JgsValue value) => value.IsStringArray && value.ArrayLength == 1;
}

/// <summary>A user-defined function: its parameters, body, and captured defining environment (a closure).</summary>
internal sealed class UserFunction : IJgsCallable, IJgsMultiCallable
{
    private readonly FnStmt _declaration;
    private readonly JgsEnvironment _closure;
    private readonly Interpreter _interpreter;

    /// <summary>Creates the function over its <paramref name="declaration"/> and captured <paramref name="closure"/>.</summary>
    public UserFunction(FnStmt declaration, JgsEnvironment closure, Interpreter interpreter)
    {
        _declaration = declaration;
        _closure = closure;
        _interpreter = interpreter;
    }

    /// <inheritdoc />
    public string Name => _declaration.Name;

    /// <summary>The <c>fn</c> declaration behind this function — the debugger uses it to refresh a
    /// function's body when its source file is live-edited.</summary>
    public FnStmt Declaration => _declaration;

    /// <summary>The accessor tag a call's frame carries (<see cref="JgsEnvironment.AccessorOf"/>), or null (V6, #27).</summary>
    internal string? AccessorOf { get; init; }

    /// <summary>
    /// Whether the declaration is written inside another function's body - a MATLAB nested
    /// function, whose closure is the frame of the call that hoisted it and whose own frame shares
    /// that workspace (V7, ADR 0168). Decided by the parser's nesting, never by the closure's flag.
    /// </summary>
    internal bool IsNested { get; init; }

    /// <summary>The class this function is a method of, or null - what the unassigned-output refusal names it under (V9).</summary>
    internal string? OwnerClass { get; init; }

    /// <summary>The environment the function closes over: for a nested function, the workspace its handle keeps alive (V10).</summary>
    internal JgsEnvironment Closure => _closure;

    /// <summary>The interpreter the function runs under.</summary>
    internal Interpreter Interpreter => _interpreter;

    /// <summary>
    /// The most outputs a call may ask for: the output list's length, or null for a trailing
    /// <c>varargout</c> and for a JGS <c>fn</c>, whose single value is not a declared output
    /// (V9.3, ADR 0170).
    /// </summary>
    internal int? MaxOutputs
    {
        get
        {
            IReadOnlyList<string> outputs = _declaration.Outputs;
            return _interpreter.Dialect.MatlabFunctions && (outputs.Count == 0 || outputs[^1] != "varargout")
                ? outputs.Count
                : null;
        }
    }

    /// <summary>
    /// The name R2025b's unassigned-output refusal gives this function: <c>Class/method</c> for a
    /// method, <c>file&gt;outer/nested</c> for a nested function, <c>file&gt;name</c> for a local
    /// function, and the bare name for a file's main function (measured, V9).
    /// </summary>
    internal string QualifiedName
    {
        get
        {
            if (OwnerClass is { } owner)
            {
                return $"{owner}/{Name}";
            }

            string stem = Path.GetFileNameWithoutExtension(_interpreter.FileOfCode(_declaration.SourceId));
            if (!IsNested)
            {
                return stem.Length == 0 || string.Equals(stem, Name, StringComparison.Ordinal) ? Name : $"{stem}>{Name}";
            }

            // The parents outward: each nested frame's closure is the frame of the function it is
            // written in, which carries that function's declaration.
            var chain = new List<string> { Name };
            for (JgsEnvironment? frame = _closure; frame?.Function is { } parent; frame = frame.Parent)
            {
                chain.Add(parent.Name);
                if (!frame.IsInsideCall)
                {
                    break;
                }
            }

            chain.Reverse();
            string joined = string.Join('/', chain);
            return stem.Length == 0 ? joined : $"{stem}>{joined}";
        }
    }

    /// <inheritdoc />
    public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column)
    {
        JgsValue[] outputs = CallMultiple(arguments, wanted: 1, line, column);
        return outputs.Length > 0 ? outputs[0] : JgsValue.Null;
    }

    /// <inheritdoc />
    public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column)
    {
        IReadOnlyList<string> parameters = _declaration.Parameters;
        bool variadic = parameters.Count > 0 && parameters[^1] == "varargin";
        int fixedCount = variadic ? parameters.Count - 1 : parameters.Count;

        if (arguments.Count > fixedCount && !variadic)
        {
            throw new JgsRuntimeException(line, column,
                $"Function '{Name}' expects {parameters.Count} argument(s) but got {arguments.Count}.");
        }

        // MATLAB lets a caller pass fewer arguments than the header names and reports how many arrived
        // through nargin; JGS requires an exact match.
        if (arguments.Count < fixedCount && !_interpreter.Dialect.MatlabFunctions)
        {
            throw new JgsRuntimeException(line, column,
                $"Function '{Name}' expects {parameters.Count} argument(s) but got {arguments.Count}.");
        }

        // A MATLAB function's frame is its own workspace, and an assignment inside it may not write
        // out to whatever called it; a nested function's frame shares its parent's variables by
        // design, at every depth. The boundary is lexical - a frame is one exactly when its function
        // is not nested in the function whose frame is its closure (V7, ADR 0168). Until V7 it was
        // read off the closure's flag, so it alternated with depth: a function nested two deep was
        // a boundary again and declared a local where R2025b writes the outer variable (#36). JGS is
        // a lexically-scoped language whose closures deliberately write to what they captured, and
        // its surface is frozen - so the boundary is MATLAB's alone.
        var local = new JgsEnvironment(_closure)
        {
            IsCallBoundary = _interpreter.Dialect.MatlabFunctions && !IsNested,
            AccessorOf = AccessorOf,
            Function = _declaration,
        };
        for (int i = 0; i < fixedCount && i < arguments.Count; i++)
        {
            // Arguments are values in MATLAB: writing to a parameter must not reach the caller's array.
            local.Declare(parameters[i], _interpreter.CopyForBinding(arguments[i]));
        }

        if (variadic)
        {
            var rest = new JgsValue[Math.Max(0, arguments.Count - fixedCount)];
            for (int i = 0; i < rest.Length; i++)
            {
                rest[i] = _interpreter.CopyForBinding(arguments[fixedCount + i]);
            }

            local.Declare("varargin", JgsValue.Cell(rest));
        }

        if (_interpreter.Dialect.MatlabFunctions)
        {
            local.Declare("nargin", JgsValue.Number(arguments.Count));
            local.Declare("nargout", JgsValue.Number(wanted));
        }

        JgsValue returned = _interpreter.ExecuteFunctionBody(_declaration, local, line);

        // A JGS 'fn' hands back what it returned; a MATLAB 'function' hands back the values its named
        // outputs hold when it ends.
        if (_declaration.Outputs.Count == 0)
        {
            return returned.Type == JgsType.Null ? System.Array.Empty<JgsValue>() : [returned];
        }

        // A trailing 'varargout' is a cell holding as many outputs as the function chose to make, so
        // the count it can answer is not known from the header the way the named outputs are.
        //
        // Asked for none (a statement call, V9.1), the function still hands back its first output
        // when it assigned one - a named output, or varargout{1} - and nothing, without error, when
        // it did not: R2025b binds `ans` after `two();` and `vo_sets_first();` and leaves it alone
        // after `named_unassigned();` (measured). Asked for n, every one of the n must exist, and a
        // missing one is R2025b's refusal by name (#84): a named output, or `varargout{k}` when the
        // cell is short, or the cell's own name when the function never made it.
        IReadOnlyList<string> outputs = _declaration.Outputs;
        bool variadicOut = outputs[^1] == "varargout";
        int namedCount = variadicOut ? outputs.Count - 1 : outputs.Count;
        bool statement = wanted == 0;
        int produced = statement ? 1 : wanted;
        if (!variadicOut)
        {
            produced = Math.Min(produced, outputs.Count);
        }

        var results = new List<JgsValue>(produced);
        for (int i = 0; i < namedCount && results.Count < produced; i++)
        {
            string output = outputs[i];
            if (!local.TryGet(output, out JgsValue value))
            {
                if (statement)
                {
                    return [];
                }

                throw Unassigned(output, line, column);
            }

            results.Add(value);
        }

        if (variadicOut && results.Count < produced)
        {
            bool made = local.TryGet("varargout", out JgsValue packed) && packed.Type == JgsType.Cell;
            JgsValue[] rest = made ? packed.AsCell : System.Array.Empty<JgsValue>();
            for (int i = 0; i < rest.Length && results.Count < produced; i++)
            {
                results.Add(rest[i]);
            }

            if (results.Count < produced && !statement)
            {
                throw made
                    ? Unassigned($"varargout{{{results.Count - namedCount + 1}}}", line, column)
                    : new JgsRuntimeException(line, column, "MATLAB:unassignedOutputs",
                        "One or more output arguments not assigned during call to \"varargout\".");
            }
        }

        return [.. results];
    }

    private JgsRuntimeException Unassigned(string output, int line, int column) =>
        new(line, column, "MATLAB:unassignedOutputs",
            $"Output argument \"{output}\" (and possibly others) not assigned a value in the execution with \"{QualifiedName}\" function.");
}

/// <summary>
/// A MATLAB anonymous function, <c>@(x) expr</c>. MATLAB captures the values of the free variables when
/// the handle is created, not when it is called, so the environment here is a snapshot: changing a
/// captured variable afterwards does not change what the handle computes.
/// </summary>
internal sealed class AnonymousFunction : IJgsCallable, IJgsMultiCallable
{
    private readonly AnonymousFnExpr _declaration;
    private readonly JgsEnvironment _captured;
    private readonly Interpreter _interpreter;
    private readonly string _file;

    private AnonymousFunction(
        AnonymousFnExpr declaration, JgsEnvironment captured, Interpreter interpreter, string file)
    {
        _declaration = declaration;
        _captured = captured;
        _interpreter = interpreter;
        _file = file;
    }

    /// <inheritdoc />
    public string Name => "@anonymous";

    /// <summary>The expression the handle was written as, which <c>func2str</c> prints back.</summary>
    public AnonymousFnExpr Declaration => _declaration;

    /// <summary>
    /// The file the handle was made in, which is where its body's names come from however far the
    /// handle travels: <c>@(x) helper(x)</c> returned from <c>maker.m</c> keeps <c>maker.m</c>'s
    /// <c>helper</c>.
    /// </summary>
    public string File => _file;

    /// <summary>The handle as written, which is how the debugger names the body's frame; printed once.</summary>
    internal string Text => _text ??= AstPrinter.Print(_declaration);

    private string? _text;

    /// <summary>The snapshot the handle captured — the workspace its body runs over (V10 releases it with the last holder).</summary>
    internal JgsEnvironment Captured => _captured;

    /// <summary>The interpreter the handle runs under.</summary>
    internal Interpreter Interpreter => _interpreter;

    /// <summary>V10 (ADR 0171): how many entries hold this handle, exactly, when its snapshot holds something tracked.</summary>
    internal int Exact;

    /// <summary>V10: whether the snapshot captured something with an exact lifetime, so this handle's holders are counted.</summary>
    internal bool Tracked { get; private set; }

    /// <summary>V10: whether the snapshot's holds have been released — once, with the last holder.</summary>
    internal bool Released;

    /// <summary>
    /// The variables the handle captured when it was made, by name — the snapshot's own entries,
    /// less the function bindings it took for the body's local names. What <c>save</c> writes as
    /// the handle's workspace (V6, #113).
    /// </summary>
    internal IEnumerable<(string Name, JgsValue Value)> CapturedVariables
    {
        get
        {
            foreach ((string name, JgsValue value) in _captured.Locals)
            {
                if (!_captured.IsFunctionBinding(name))
                {
                    yield return (name, value);
                }
            }
        }
    }

    /// <summary>Creates the handle, snapshotting every name its body refers to that is not a parameter.</summary>
    public static AnonymousFunction Create(AnonymousFnExpr declaration, JgsEnvironment defining, Interpreter interpreter)
    {
        // The snapshot is a static workspace: a name it captured can be changed and a new one cannot
        // be added, which is what assignin into it from a function the body called runs into.
        var snapshot = new JgsEnvironment(defining) { IsStaticWorkspace = true };
        foreach (string name in FreeNames(declaration))
        {
            if (defining.TryGetScope(name, out JgsEnvironment? scope, out JgsValue value))
            {
                // A built-in is not captured in the MATLAB dialect: the body's walk reaches the
                // layer when the handle is called, and the folders are asked then — R2025b answers
                // `@() max({1})` with the max.m of the current folder at the call, not at the
                // creation (M145). Capturing it here would make it a local function of the body.
                if (scope.IsBuiltinLayer && interpreter.Dialect.IsMatlab)
                {
                    continue;
                }

                if (defining.IsFunctionBinding(name))
                {
                    snapshot.DeclareFunction(name, value);
                }
                else
                {
                    // M2 (appendix A #1, #24, #101): the snapshot is an entry, so it holds a
                    // wrapper of its own over the captured payload. Without the share the handle
                    // read the defining workspace's later writes — `f = @() v; v(1) = 7; f()`
                    // answered 7 — and a nested function writing `v` through the shared workspace
                    // reached the snapshot the same way.
                    snapshot.Declare(name, interpreter.CopyForBinding(value));
                }
            }
        }

        // V10: a snapshot holding a handle, a tracked container or another such handle keeps it
        // alive for as long as this handle is held, so the handle's own holders are counted.
        return new AnonymousFunction(declaration, snapshot, interpreter, interpreter.CurrentFile)
        {
            Tracked = snapshot.NeedsRelease,
        };
    }

    /// <inheritdoc />
    public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column)
    {
        JgsValue[] outputs = CallMultiple(arguments, wanted: 1, line, column);
        return outputs.Length > 0 ? outputs[0] : JgsValue.Null;
    }

    /// <summary>
    /// Invokes the handle asking for several outputs. A handle is a wrapper, not a function with
    /// outputs of its own, so the count passes through to whatever the body calls: that is what makes
    /// <c>[a, b] = f(x)</c> behave the same for <c>@minmax</c> and for <c>@(x) minmax(x)</c>, and it
    /// is what lets <c>cellfun</c> ask each element for two answers.
    /// </summary>
    public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column)
    {
        // A trailing 'varargin' collects whatever the fixed parameters did not take, exactly as it does
        // in a function file; the parameters before it must still all arrive.
        IReadOnlyList<string> parameters = _declaration.Parameters;
        bool variadic = parameters.Count > 0 && parameters[^1] == "varargin";
        int fixedCount = variadic ? parameters.Count - 1 : parameters.Count;

        if (arguments.Count < fixedCount || (!variadic && arguments.Count > fixedCount))
        {
            throw new JgsRuntimeException(line, column,
                $"This anonymous function expects {parameters.Count} argument(s) but got {arguments.Count}.");
        }

        var local = new JgsEnvironment(_captured) { IsStaticWorkspace = true };
        for (int i = 0; i < fixedCount; i++)
        {
            local.Declare(parameters[i], _interpreter.CopyForBinding(arguments[i]));
        }

        if (variadic)
        {
            var rest = new JgsValue[arguments.Count - fixedCount];
            for (int i = 0; i < rest.Length; i++)
            {
                rest[i] = _interpreter.CopyForBinding(arguments[fixedCount + i]);
            }

            local.Declare("varargin", JgsValue.Cell(rest));
        }

        // MATLAB answers an anonymous function's own arity from inside its body, in preference to the
        // nargin of whatever function defined it.
        if (_interpreter.Dialect.MatlabFunctions)
        {
            local.Declare("nargin", JgsValue.Number(arguments.Count));
        }

        // The body runs as a context of its own — its workspace as the current frame, so a function
        // it calls sees that workspace as its caller, and its file as the current file — and the
        // invoker's pair comes back afterwards. The parameters it bound are released with the
        // frame (V10), at the invoker's next statement boundary.
        try
        {
            return _interpreter.EvaluateForOutputsInContext(_declaration.Body, wanted, local, _file, Text, line);
        }
        finally
        {
            _interpreter.Lifetimes.FrameExited(local);
        }
    }

    /// <summary>Every identifier the body mentions apart from the parameters.</summary>
    private static IEnumerable<string> FreeNames(AnonymousFnExpr declaration)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Walk(declaration.Body, names);
        names.ExceptWith(declaration.Parameters);
        return names;
    }

    private static void Walk(Expr? expression, HashSet<string> names)
    {
        switch (expression)
        {
            case null:
                return;
            case VariableExpr variable:
                names.Add(variable.Name);
                return;
            case FunctionHandleExpr handle:
                names.Add(handle.Name);
                return;
            case UnaryExpr unary:
                Walk(unary.Operand, names);
                return;
            case TransposeExpr transpose:
                Walk(transpose.Operand, names);
                return;
            case BinaryExpr binary:
                Walk(binary.Left, names);
                Walk(binary.Right, names);
                return;
            case LogicalExpr logical:
                Walk(logical.Left, names);
                Walk(logical.Right, names);
                return;
            case RangeExpr range:
                Walk(range.Start, names);
                Walk(range.Step, names);
                Walk(range.Stop, names);
                return;
            case CallExpr call:
                Walk(call.Callee, names);
                WalkAll(call.Arguments, names);
                return;
            case IndexExpr index:
                Walk(index.Target, names);
                WalkAll(index.Indices, names);
                return;
            case BraceIndexExpr brace:
                Walk(brace.Target, names);
                WalkAll(brace.Indices, names);
                return;
            case MemberExpr member:
                Walk(member.Target, names);
                Walk(member.FieldName, names);
                return;
            case ArrayLiteral array:
                WalkAll(array.Elements, names);
                return;
            case MatrixLiteral matrix:
                foreach (IReadOnlyList<Expr> row in matrix.Rows)
                {
                    WalkAll(row, names);
                }

                return;
            case CellLiteral cell:
                foreach (IReadOnlyList<Expr> row in cell.Rows)
                {
                    WalkAll(row, names);
                }

                return;
            case AssignExpr assign:
                Walk(assign.Target, names);
                Walk(assign.Value, names);
                return;
            case AnonymousFnExpr nested:
                Walk(nested.Body, names);
                names.ExceptWith(nested.Parameters);
                return;
            default:
                return; // literals and 'end'/':' refer to nothing
        }
    }

    private static void WalkAll(IReadOnlyList<Expr> expressions, HashSet<string> names)
    {
        foreach (Expr expression in expressions)
        {
            Walk(expression, names);
        }
    }
}
