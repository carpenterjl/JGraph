namespace JGraph.Scripting.Jgs;

/// <summary>
/// The output count a call asks for, held against what the callee can give (V9.3, ADR 0170). A
/// user function's maximum is its output list (unbounded with a trailing <c>varargout</c>); a
/// builtin's is what R2025b's <c>nargout</c> reports for the name
/// (<see cref="JgsBuiltinOutputCounts"/>, null when variable or not MATLAB's); a bound method's is
/// its method's. Asking for more is refused before the callee runs, with R2025b's identifier: a
/// built-in refuses as <c>MATLAB:maxlhs</c>, a function - a user's, or one MATLAB keeps as a file,
/// like <c>hold</c> - as <c>MATLAB:TooManyOutputs</c>, both "Too many output arguments."
/// </summary>
/// <remarks>
/// When the refusal happens is the road's to decide, and it was measured: a direct call of a user
/// function refuses before its arguments are evaluated (<c>x = none_out(bump())</c> never runs
/// <c>bump</c>), and every other road - a builtin, a handle, <c>feval</c>, an anonymous forward,
/// <c>cellfun</c> - after them. A handle and an anonymous function have no maximum of their own:
/// the handle's target is checked once dispatch has chosen it, and the anonymous body's own call
/// is checked on its own road. Zero outputs are never too many.
/// </remarks>
internal static class JgsOutputDemand
{
    /// <summary>The most outputs a call of <paramref name="callee"/> may ask for, or null when unbounded or unknown.</summary>
    public static int? MaxOutputs(IJgsCallable callee) => callee switch
    {
        UserFunction user => user.MaxOutputs,
        BuiltinFunction builtin => builtin.MaxOutputs,
        BoundMethod bound => MaxOutputs(bound.Method),
        _ => null,
    };

    /// <summary>Refuses a call of <paramref name="callee"/> asked for <paramref name="wanted"/> outputs when that is more than it has.</summary>
    public static void Refuse(IJgsCallable callee, int wanted, int line, int column)
    {
        if (wanted <= 0 || MaxOutputs(callee) is not int max || wanted <= max)
        {
            return;
        }

        bool builtin = Underlying(callee) is BuiltinFunction { IsMatlabFile: false };
        throw new JgsRuntimeException(
            line, column, builtin ? "MATLAB:maxlhs" : "MATLAB:TooManyOutputs", "Too many output arguments.")
        {
            UsingName = builtin ? callee.Name : null,
        };
    }

    private static IJgsCallable Underlying(IJgsCallable callee) =>
        callee is BoundMethod bound ? bound.Method : callee;
}
