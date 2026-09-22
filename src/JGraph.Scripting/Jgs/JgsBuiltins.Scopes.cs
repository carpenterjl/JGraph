namespace JGraph.Scripting.Jgs;

/// <summary>
/// V3.1 (ADR 0164, M5): the builtins whose body can run script code while it still holds its
/// arguments — a callable it was handed, text it evaluates, callbacks it drains.
/// </summary>
/// <remarks>
/// A call of one of these holds every argument as a counted share for the whole call, so a
/// callback that writes the variable an argument came from detaches that variable and leaves the
/// argument the builtin is still walking alone (appendix A #10, #159). The list is not a judgement
/// call: <c>tools/ownership/audit-ownership.py</c> walks every builtin's body through the helpers it
/// calls to the script entry points and fails the gate unless this list is exactly what it finds,
/// less the assertions below. A call whose arguments include a callable or an object is held for
/// the whole call whether or not its builtin is here (<see cref="Interpreter"/>'s dynamic rule), so a
/// callable-taker registered without a literal name — the <c>ode</c> family — is covered too.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The builtins that may run script code; see the remarks.</summary>
    internal static readonly HashSet<string> ScriptRunningBuiltins = new(StringComparer.Ordinal)
    {
        "accumarray", "arrayfun", "bootci", "bootstrp", "bsxfun", "bvp4c", "bvp5c", "bvpinit", "cellfun",
        "close", "dblquad", "dde23", "ddensd", "ddesd", "decic", "delete", "designfilt", "drawnow", "eval", "evalc",
        "evalin", "ezpolar", "fcnchk", "feval", "fminbnd", "fminsearch", "funm", "fzero", "getframe",
        "ginput", "image", "inline", "inlineeval", "innerintegral", "integral", "integral2", "integral3",
        "jackknife", "legend", "loglog", "mhsample", "ode15i", "odephas2", "odephas3", "odeplot",
        "odextend", "pause", "pdepe", "pulstran", "quad", "quad2d", "quadgk", "quadl", "quadv",
        "regexprep", "run", "semilogx", "semilogy", "slice", "slicesample", "spfun", "splitapply",
        "start", "stop", "str2func", "str2num", "structfun", "triplequad", "uicontextmenu", "uimenu",
        "vectorize", "wait", "waitforbuttonpress",
    };

    // The audit's call graph joins functions by name, so a few builtins it flags reach script code
    // only through a helper that shares a name with one that does, or through a forward to another
    // builtin that runs none. Each is asserted here with its reason; the audit believes these lines
    // and fails if one goes stale.
    //
    // audit: runs no script: colon — BuildRange evaluates a range of PreEvaluated nodes, never script.
    // audit: runs no script: trapz cumtrapz — DataAnalysis' Integrate(name, args, cumulative, …), not the Solvers overload that calls an integrand.
    // audit: runs no script: fitdist makedist metaclass methods properties — reach a Validate/Check pair that shares its name with the arguments-block validator.
    // audit: runs no script: tdfread xptread — a file reader's FieldName helper, not the interpreter's dynamic-field FieldName.
    // audit: runs no script: load — JgsWorkspaceIo.Load reads a MAT-file; the graph joined it to a path loader's lambda by name.
    // audit: runs no script: reverse — forwards to the legacy JGS reverse builtin.
    // audit: runs no script: whitepoint — forwards to the single-output whitepoint builtin it wraps.
    // audit: runs no script: nancov — forwards to the cov builtin.
}
