namespace JGraph.Scripting.Jgs;

/// <summary>
/// M5 (ADR 0164): scopes — C# code that holds an evaluated wrapper while more script code runs.
/// </summary>
/// <remarks>
/// <para>
/// <c>r = gv + bump()</c> reads <c>gv</c>, then calls <c>bump</c>, which writes <c>gv(1) = 7</c>. The
/// left operand is the global entry's own wrapper, and with one holder counted M3's gate lets the
/// write land in place — so the operand the addition is about to read changed under it, and
/// R2025b's answer (the value <c>gv</c> had when it was read) was lost. A scope fixes that the way
/// an entry does: it holds a counted share (M2) instead of the entry's wrapper, so the inner write
/// finds two holders and detaches the entry, and the scope's view is left alone.
/// </para>
/// <para>
/// <b>Release.</b> A scope ends in a <c>finally</c> and gives its count back — unless the share it
/// holds is gone from the payload already (a compaction or a demotion swapped the wrapper's storage,
/// which releases the old payload itself), or it became something a caller still holds: a builtin
/// handed it back (<see cref="ScopeHolds.Keep"/>). Releasing is always safe otherwise, because every
/// entry that stores a wrapper takes a share of its own (M2, V2): a scope's share is never an entry.
/// </para>
/// <para>
/// <b>Inert expressions open no scope.</b> <c>y = x + f(y)</c> must hold <c>x</c>; <c>y = x + k * z</c>
/// need not, because nothing in <c>k * z</c> can run script code. <see cref="IsInert"/> decides:
/// the syntactic half is cached on the node, and the names are checked against what they are bound
/// to when the scope would open, so an object (whose operators and subscripts are methods) or a
/// function handle keeps the scope. Scopes are binding shares in M17's sense, so JGS never opens
/// one: its bindings are reference semantics and hold no count to protect.
/// </para>
/// </remarks>
internal sealed partial class Interpreter
{
    /// <summary>How many scopes have taken a share — the counter the V3 tests read.</summary>
    internal long ScopesOpened { get; private set; }

    /// <summary>The workspace <c>global</c> declarations bind into — for the same tests.</summary>
    internal JgsEnvironment GlobalWorkspace => _globalWorkspace;

    /// <summary>
    /// The holds one scope has taken. A mutable struct passed by reference, so the common case — a
    /// scope that holds one wrapper, or none — allocates nothing beyond the share itself.
    /// </summary>
    internal struct ScopeHolds
    {
        private JgsValue? _first;
        private object? _firstPayload;
        private List<(JgsValue Share, object Payload)>? _more;

        /// <summary>Records a counted share this scope must give back.</summary>
        internal void Add(JgsValue share, object payload)
        {
            if (_first is null)
            {
                _first = share;
                _firstPayload = payload;
                return;
            }

            (_more ??= new List<(JgsValue, object)>(4)).Add((share, payload));
        }

        /// <summary>
        /// A caller keeps this value: if it is one of the shares, it is a holder now and the scope
        /// must not give its count back.
        /// </summary>
        internal void Keep(JgsValue? kept)
        {
            if (kept is null)
            {
                return;
            }

            if (ReferenceEquals(_first, kept))
            {
                _first = null;
                _firstPayload = null;
            }

            if (_more is not null)
            {
                for (int i = 0; i < _more.Count; i++)
                {
                    if (ReferenceEquals(_more[i].Share, kept))
                    {
                        _more[i] = (_more[i].Share, null!);
                    }
                }
            }
        }

        /// <summary>Whether <paramref name="value"/> is one of this scope's shares already.</summary>
        internal readonly bool Holds(JgsValue value)
        {
            if (ReferenceEquals(_first, value))
            {
                return true;
            }

            if (_more is not null)
            {
                foreach ((JgsValue share, _) in _more)
                {
                    if (ReferenceEquals(share, value))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Gives back every count this scope still holds.</summary>
        internal void Release()
        {
            if (_first is not null)
            {
                _first.ReleaseScopeShare(_firstPayload!);
                _first = null;
                _firstPayload = null;
            }

            if (_more is not null)
            {
                foreach ((JgsValue share, object payload) in _more)
                {
                    if (payload is not null)
                    {
                        share.ReleaseScopeShare(payload);
                    }
                }

                _more = null;
            }
        }
    }

    /// <summary>
    /// The value a scope holds in place of <paramref name="value"/>: a counted share when the
    /// dialect copies on assignment and the value's payload can be written in place, and the value
    /// itself otherwise — a scalar, a string, a function, a handle object or a <c>containers.Map</c>
    /// (whose identity is its semantics), or anything at all in JGS.
    /// </summary>
    private JgsValue Hold(JgsValue value, ref ScopeHolds holds)
    {
        if (!Dialect.CopyOnAssign || !IsHoldable(value))
        {
            return value;
        }

        JgsValue share = JgsValue.Share(value);
        if (ReferenceEquals(share, value) || share.ScopePayload is not { } payload)
        {
            return value;
        }

        ScopesOpened++;
        holds.Add(share, payload);
        return share;
    }

    /// <summary>
    /// Calls <paramref name="callee"/>, holding every argument as a counted share for the whole call
    /// when the callee may run script code while it still reads them (M5's whole-call scope).
    /// </summary>
    /// <remarks>
    /// A user function, an anonymous function, a method and a constructor bind their parameters
    /// through <see cref="CopyForBinding"/> before their bodies run, so each parameter is already a
    /// holder and no scope is needed. A builtin is held when it is on
    /// <see cref="JgsBuiltins.ScriptRunningBuiltins"/>, or when an argument is a callable (it may
    /// be called) or an object (an overload may run) — the dynamic half of the rule, which also
    /// covers a callable-taker registered without a name the audit can read.
    /// </remarks>
    private JgsValue CallHeld(IJgsCallable callee, JgsValue[] arguments, int line, int column)
    {
        if (!NeedsWholeCallScope(callee, arguments))
        {
            return callee.Call(arguments, line, column);
        }

        var holds = new ScopeHolds();
        try
        {
            for (int i = 0; i < arguments.Length; i++)
            {
                arguments[i] = Hold(arguments[i], ref holds);
            }

            JgsValue answer = callee.Call(arguments, line, column);
            holds.Keep(answer);
            return answer;
        }
        finally
        {
            holds.Release();
        }
    }

    /// <summary><see cref="CallHeld"/> asking for <paramref name="wanted"/> outputs.</summary>
    private JgsValue[] CallMultipleHeld(IJgsCallable callee, JgsValue[] arguments, int wanted, int line, int column)
    {
        var holds = new ScopeHolds();
        try
        {
            HoldForWholeCall(callee, arguments, ref holds);
            JgsValue[] answers = callee is IJgsMultiCallable several
                ? several.CallMultiple(arguments, wanted, line, column)
                : [callee.Call(arguments, line, column)];
            foreach (JgsValue answer in answers)
            {
                holds.Keep(answer);
            }

            return answers;
        }
        finally
        {
            holds.Release();
        }
    }

    /// <summary>
    /// <see cref="CallHeld"/> for a road that runs the call itself (a multiple-output call, a
    /// discarded one): holds the arguments in <paramref name="holds"/>, which the caller releases.
    /// </summary>
    private void HoldForWholeCall(IJgsCallable callee, JgsValue[] arguments, ref ScopeHolds holds)
    {
        if (NeedsWholeCallScope(callee, arguments))
        {
            for (int i = 0; i < arguments.Length; i++)
            {
                arguments[i] = Hold(arguments[i], ref holds);
            }
        }
    }

    /// <summary>Whether a call of <paramref name="callee"/> needs M5's whole-call scope.</summary>
    private bool NeedsWholeCallScope(IJgsCallable callee, IReadOnlyList<JgsValue> arguments)
    {
        if (!Dialect.CopyOnAssign || callee is UserFunction or AnonymousFunction or BoundMethod)
        {
            return false;
        }

        if (callee is NamedHandle { Captured: UserFunction })
        {
            return false;
        }

        if (callee is BuiltinFunction { RunsScript: true } or NamedHandle { Captured: BuiltinFunction { RunsScript: true } })
        {
            return AnyHoldable(arguments);
        }

        bool holdable = false;
        bool dynamic = false;
        foreach (JgsValue argument in arguments)
        {
            holdable |= IsHoldable(argument);
            dynamic |= argument.Type is JgsType.Function or JgsType.Object;
        }

        return holdable && dynamic;
    }

    private static bool AnyHoldable(IReadOnlyList<JgsValue> values)
    {
        foreach (JgsValue value in values)
        {
            if (IsHoldable(value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The rows of a bracket or cell literal, each spread, with M5's literal scope: every element
    /// already evaluated is held as a share while a later element runs script code, until the
    /// literal is built from them. The caller releases <paramref name="holds"/> once it is.
    /// </summary>
    private List<JgsValue[]> EvaluateRows(
        IReadOnlyList<IReadOnlyList<Expr>> rows, JgsEnvironment env, ref ScopeHolds holds)
    {
        var built = new List<JgsValue[]>(rows.Count);
        foreach (IReadOnlyList<Expr> row in rows)
        {
            // Everything in the rows before this one is held if anything in this row may run.
            if (built.Count > 0 && Dialect.CopyOnAssign && !AllInert(row, env))
            {
                foreach (JgsValue[] earlier in built)
                {
                    for (int i = 0; i < earlier.Length; i++)
                    {
                        if (IsHoldable(earlier[i]) && !holds.Holds(earlier[i]))
                        {
                            earlier[i] = Hold(earlier[i], ref holds);
                        }
                    }
                }
            }

            built.Add(EvaluateAll(row, env, ref holds));
        }

        return built;
    }

    /// <summary>Whether a scope would take a share of this value (see <see cref="Hold"/>).</summary>
    private static bool IsHoldable(JgsValue value) => value.Type switch
    {
        JgsType.Array or JgsType.Cell => true,
        JgsType.Struct => !JgsBuiltins.IsHandleClass(value),
        JgsType.Object => !value.AsObject.Class.IsHandle,
        _ => false,
    };

    /// <summary>
    /// Whether evaluating <paramref name="expr"/> can run script code — the question every scope asks
    /// about what is still to be evaluated before it takes a share.
    /// </summary>
    private bool IsInert(Expr expr, JgsEnvironment env)
    {
        if (expr.ScopeNames is null)
        {
            var names = new List<string>();
            expr.ScopeShape = InertShape(expr, names) ? Expr.InertShaped : Expr.MayRunScript;
            expr.ScopeNames = names.Count == 0 ? [] : [.. names.Distinct(StringComparer.Ordinal)];
        }

        if (expr.ScopeShape != Expr.InertShaped)
        {
            return false;
        }

        foreach (string name in expr.ScopeNames)
        {
            if (!IsInertName(name, env))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every expression in a list is inert (see <see cref="IsInert"/>).</summary>
    private bool AllInert(IReadOnlyList<Expr> exprs, JgsEnvironment env)
    {
        for (int i = 0; i < exprs.Count; i++)
        {
            if (!IsInert(exprs[i], env))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a name mentioned in an inert-shaped expression reads data or calls a built-in that
    /// runs no script: a variable bound to a number, a logical, text, an array, a cell or a struct
    /// that is not a handle, or a built-in constant or function whose body reaches no script entry.
    /// An object's operators, subscripts and properties can all be methods, and a handle is a call
    /// waiting to happen, so either keeps the scope; so does a name nothing holds.
    /// </summary>
    private bool IsInertName(string name, JgsEnvironment env)
    {
        if (LookUp(name, env, out JgsValue bound) && bound.Type != JgsType.Function)
        {
            return bound.Type switch
            {
                JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.String
                    or JgsType.Array or JgsType.Cell => true,
                JgsType.Struct => !JgsBuiltins.IsHandleClass(bound),
                _ => false,
            };
        }

        Resolution resolved = _resolver.Value(name, env);
        return resolved.Found
            && resolved.Layer is ResolutionLayer.Builtin or ResolutionLayer.BuiltinMethod
            && resolved.Value.Type == JgsType.Function
            && resolved.Value.AsCallable is BuiltinFunction { RunsScript: false };
    }

    /// <summary>
    /// The syntactic half of <see cref="IsInert"/>: a literal, <c>:</c>, <c>end</c>, a name, an
    /// operator, a range or a bracket over inert parts, or a subscript, brace or field read of a name
    /// with inert subscripts — each name collected for the binding check.
    /// </summary>
    private static bool InertShape(Expr expr, List<string> names)
    {
        switch (expr)
        {
            case NumberLiteral or StringLiteral or BoolLiteral or ComplexLiteral or EndExpr or AllExpr:
                return true;
            case VariableExpr variable:
                names.Add(variable.Name);
                return true;
            case UnaryExpr unary:
                return InertShape(unary.Operand, names);
            case TransposeExpr transpose:
                return InertShape(transpose.Operand, names);
            case BinaryExpr binary:
                return InertShape(binary.Left, names) && InertShape(binary.Right, names);
            case LogicalExpr logical:
                return InertShape(logical.Left, names) && InertShape(logical.Right, names);
            case RangeExpr range:
                return InertShape(range.Start, names)
                    && (range.Step is null || InertShape(range.Step, names))
                    && InertShape(range.Stop, names);
            case ArrayLiteral array:
                return AllInertShaped(array.Elements, names);
            case MatrixLiteral matrix:
                return matrix.Rows.All(row => AllInertShaped(row, names));
            case CellLiteral cell:
                return cell.Rows.All(row => AllInertShaped(row, names));
            case CallExpr { Callee: VariableExpr callee } call:
                names.Add(callee.Name);
                return AllInertShaped(call.Arguments, names);
            case IndexExpr { Target: VariableExpr target } index:
                names.Add(target.Name);
                return AllInertShaped(index.Indices, names);
            case BraceIndexExpr { Target: VariableExpr target } brace:
                names.Add(target.Name);
                return AllInertShaped(brace.Indices, names);
            case MemberExpr { Field: not null } member:
                return InertShape(member.Target, names);
            default:
                return false;
        }
    }

    private static bool AllInertShaped(IReadOnlyList<Expr> exprs, List<string> names)
    {
        foreach (Expr expr in exprs)
        {
            if (!InertShape(expr, names))
            {
                return false;
            }
        }

        return true;
    }
}
