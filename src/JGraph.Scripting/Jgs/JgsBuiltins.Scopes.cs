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
        "jackknife", "load", "loglog", "mhsample", "notify", "ode15i", "odephas2", "odephas3", "odeplot",
        "odextend", "pause", "pdepe", "pulstran", "quad", "quad2d", "quadgk", "quadl", "quadv",
        "regexprep", "rowfun", "run", "semilogx", "semilogy", "slice", "slicesample", "spfun", "splitapply",
        "start", "stop", "str2func", "str2num", "structfun", "triplequad", "uibuttongroup", "uicontextmenu", "uicontrol", "uimenu", "uipanel",
        "uipushtool", "uitab", "uitabgroup", "uitable", "uitoggletool", "uitoolbar",
        "varfun", "vectorize", "wait", "waitforbuttonpress",

        // U4: the waits deliver callbacks while they block, the dialogs that wait do so through
        // them, and the others delete or replace figures, which runs their DeleteFcns.
        "errordlg", "helpdlg", "inputdlg", "listdlg", "msgbox", "questdlg", "uiload", "uiopen", "uisave",
        "uiwait", "waitbar", "waitfor", "warndlg",

        // U5: a component's maker runs its CreateFcn, uiconfirm delivers callbacks while it waits,
        // and the dialogs over a figure run a CloseFcn when they are replaced.
        "uialert", "uibutton", "uicheckbox", "uiconfirm", "uidropdown", "uieditfield", "uigridlayout",
        "uihyperlink", "uiimage", "uilabel", "uilistbox", "uiradiobutton", "uislider", "uispinner",
        "uitextarea", "uitogglebutton",

        // U9: the makers run a CreateFcn.
        "uicolorpicker", "uidatepicker", "uigauge", "uiknob", "uilamp", "uiswitch", "uitree", "uitreenode",

        // U9b.
        "uihtml",

        // Open item 80: uifigure and uiaxes run a CreateFcn named among their options. dialog
        // keeps one without running it, as R2025b does (measured), so it is not here.
        "uiaxes", "uifigure",

        // U11: a MATLAB figure file's objects run their CreateFcns as it opens, and a GUIDE app's
        // main function runs its opening, output and callback functions.
        "gui_mainfcn", "hgload", "openfig",

        // Open item 38, found once the audit read method groups and stopped misreading a lambda
        // argument as a declaration: a property's get and set methods and PostSet listeners; a
        // CreateFcn named among a plot's options; the function plotters' and the fitters' and
        // tests' callables; a wait that pumps callbacks; a prototype function; an app's callback.
        "get", "set", "plot", "stairs", "polar", "polarplot", "subplot",
        "ezcontour", "ezcontourf", "ezmesh", "ezmeshc", "ezplot", "ezplot3", "ezsurf", "ezsurfc",
        "fcontour", "fimplicit", "fimplicit3", "fmesh", "fplot", "fplot3", "fsurf",
        "groupfilter", "grouptransform", "kstest", "nlinfit", "nlpredci",
        "midiid", "loadlibrary", "executeCallback",
    };

    // The audit's call graph joins functions by name, so a few builtins it flags reach script code
    // only through a helper that shares a name with one that does, or through a forward to another
    // builtin that runs none. Each is asserted here with its reason; the audit believes these lines
    // and fails if one goes stale.
    //
    // audit: runs no script: colon — BuildRange evaluates a range of PreEvaluated nodes, never script.
    // audit: runs no script: trapz cumtrapz — DataAnalysis' Integrate(name, args, cumulative, …), not the Solvers overload that calls an integrand.
    // audit: runs no script: fitdist makedist metaclass methods properties — reach a Validate/Check pair that shares its name with the arguments-block validator.
    // audit: runs no script: events — lists a class's declared event names; it joins the graph through NamedClass, the same name-sharing road as properties and methods.
    // audit: runs no script: tdfread xptread — a file reader's FieldName helper, not the interpreter's dynamic-field FieldName.
    // audit: runs no script: reverse — forwards to the legacy JGS reverse builtin.
    // audit: runs no script: whitepoint — forwards to the single-output whitepoint builtin it wraps.
    // audit: runs no script: nancov — forwards to the cov builtin.
    // audit: runs no script: isprop — lists a class's property names through NamedClass, the same name-sharing road as properties.
    // audit: runs no script: box colorbar grid hold hidden — OnOff's Of reads a word; its Read shares a name with TransportClient.Read, which drains a device's timers.
    // audit: runs no script: makehgtform quiver quiver3 ellipj — Spread's Read, the same name-sharing road to TransportClient.Read.
    // audit: runs no script: fread fgetl fgets — read a file entry; Read, ReadLine and TakeLine share names with TransportClient's, which a device's own fread method reaches as a method.
    // audit: runs no script: fwrite writematrix writecell writetable print — their Write shares a name with the interpreter's Execute road.
    // audit: runs no script: hggroup hgtransform — Group's All and Build share names with the property tables' Build.
    // audit: runs no script: isvalid — TryLibBuiltin's road to a C library value's members shares WriteMember and MemberOf with the interpreter's.
    // audit: runs no script: libfunctions — forwards to the methods builtin, which runs none.
    // audit: runs no script: libpointer — Adopt's Apply shares a name with the .NET operators' Apply.
    // audit: runs no script: calllib — process.Call is the library host's request over its pipe, not a callable.
    // audit: runs no script: qrupdate — Update's NameOf shares MemberOf with the interpreter's member read.
    // audit: runs no script: uminus uplus — an object's overload is the dynamic rule's; ApplyBinary's Compare shares MemberOf with the member read.
    // audit: runs no script: copyobj — its Copy shares a name with matlab.mixin.Copyable's copy, which calls copyElement.
    //
    // And the other way: a listed builtin whose road to script code the graph cannot follow.
    //
    // audit: runs script: openfig hgload — JgsFigFile.Build fires each object's CreateFcn and calls the makers by name; Build is a name the graph does not follow on a receiver.
}
