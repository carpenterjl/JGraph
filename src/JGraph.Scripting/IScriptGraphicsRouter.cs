using JGraph.Core.Model;

namespace JGraph.Scripting;

/// <summary>
/// Where a window's report of what its user did runs, when more than one session's figures are on
/// screen (compiler and SDK plan, stage K5). The engine keeps the graphics handles, their callbacks
/// and the callback queue in statics that the JGraph Runtime moves in and out per component; a click
/// on one component's window has to be looked up and queued in that component's state, never in
/// whichever component happens to be live. JGraph's application has one session and installs none,
/// and every report runs where it is made, as before.
/// </summary>
internal interface IScriptGraphicsRouter
{
    /// <summary>
    /// Runs <paramref name="body"/> now, on the calling thread, when <paramref name="figure"/>'s
    /// session is the live one (holding the swap off while it runs), and answers true; otherwise
    /// hands it to that session's thread to run with its state in place, and answers false.
    /// </summary>
    /// <param name="figure">The figure the report is about, or null when the object has none.</param>
    /// <param name="body">The report: a lookup in the handle registry, and what it queues.</param>
    bool Run(FigureModel? figure, Action body);
}
