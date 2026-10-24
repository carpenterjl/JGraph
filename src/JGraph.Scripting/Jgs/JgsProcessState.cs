using JGraph.Api;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The part of the engine's state that lives in statics but that R2025b keeps per deployed
/// component (compiler and SDK plan, stage K4): the figure registry, the graphics handles and what
/// they carry (groups, root defaults, detached components, the callback and click history), the
/// display <c>format</c>, and the installed callback dispatcher.
/// </summary>
/// <remarks>
/// <para>
/// <c>record-k4.ps1</c> ran two instances of one R2025b .NET component and a second component in one
/// process (<c>recordings/k4_client.out.txt</c>): a figure one component made was not among the
/// second's, and <c>format long</c> in one left the other's short, while the two instances of one
/// component shared both. Everything else that differed per component (the workspace, globals,
/// persistent variables, the random stream, the warning state, the current folder, user data) is
/// already a session's own in JGraph.
/// </para>
/// <para>
/// The runtime runs every component's call on one engine thread, as R2025b serializes them, and
/// before a call for a component other than the last one it takes the last one's state out with
/// <see cref="Take"/> and puts this one's in with <see cref="Put"/>. Nothing here is safe to swap
/// while a call runs.
/// </para>
/// </remarks>
internal sealed class JgsProcessState
{
    private readonly object _figures;
    private readonly object _handles;
    private readonly (JgsNumberFormat.Mode Current, bool Explicit) _format;
    private readonly JgsCallbackDispatcher? _dispatcher;

    private JgsProcessState(object figures, object handles, (JgsNumberFormat.Mode, bool) format, JgsCallbackDispatcher? dispatcher)
    {
        _figures = figures;
        _handles = handles;
        _format = format;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Moves the state out and leaves the process as a fresh session finds it: no figures, no
    /// handles, the default format, no dispatcher.
    /// </summary>
    public static JgsProcessState Take()
    {
        JgsCallbackDispatcher? dispatcher = JgsCallbackDispatcher.Installed;
        JgsCallbackDispatcher.Install(null);
        object handles = JgsHandleRegistry.TakeState();
        object figures = JG.TakeState();
        return new JgsProcessState(figures, handles, JgsNumberFormat.Take(), dispatcher);
    }

    /// <summary>Puts this state back in place of the process's, which <see cref="Take"/> should have moved out first.</summary>
    public void Put()
    {
        JG.PutState(_figures);
        JgsHandleRegistry.PutState(_handles);
        JgsNumberFormat.Put(_format);
        JgsCallbackDispatcher.Install(_dispatcher);
    }
}
