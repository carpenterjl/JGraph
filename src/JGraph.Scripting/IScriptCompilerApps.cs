using JGraph.Deploy;
using JGraph.Deploy.Projects;

namespace JGraph.Scripting;

/// <summary>
/// The host's compiler apps (compiler and SDK plan, stage K19): what <c>standaloneApplicationCompiler</c>
/// and its siblings open from a script. A host with windows (the JGraph app) shows the app; a session
/// without one refuses the call before reaching here.
/// </summary>
public interface IScriptCompilerApps
{
    /// <summary>
    /// Brings up the app for <paramref name="target"/>, or the gallery of apps for null, with
    /// <paramref name="mainFile"/> as its main file or first exported function when given, and
    /// returns once the window is up.
    /// </summary>
    /// <param name="target">The target whose app opens, or null for the gallery.</param>
    /// <param name="mainFile">The full path of a MATLAB file that exists, or null.</param>
    /// <param name="services">What the window asks the session for.</param>
    /// <returns>Null when it opened, else why the app could not open.</returns>
    string? Open(CompilerTarget? target, string? mainFile, CompilerAppServices services);

    /// <summary>
    /// Brings up the Compiler Task Manager for the open project (stage K20): its tasks, with the
    /// app for <paramref name="preferred"/> offered first for a new task, and returns once the
    /// window is up.
    /// </summary>
    /// <param name="project">The open project.</param>
    /// <param name="preferred">The target the command that opened the manager stands for, or null for the gallery.</param>
    /// <param name="services">What the windows ask the session for.</param>
    /// <returns>Null when it opened, else why not.</returns>
    string? OpenTaskManager(MatlabProject project, CompilerTarget? preferred, CompilerAppServices services);

    /// <summary>
    /// Brings up the app window over one task of the open project (stage K21: what a legacy
    /// project upgrades into), and returns once the window is up.
    /// </summary>
    /// <param name="project">The open project.</param>
    /// <param name="taskName">The task's name in the project.</param>
    /// <param name="services">What the window asks the session for.</param>
    /// <returns>Null when it opened, else why not.</returns>
    string? OpenTask(MatlabProject project, string taskName, CompilerAppServices services);
}

/// <summary>What a compiler app's window asks of the session that opened it.</summary>
public sealed class CompilerAppServices
{
    /// <summary>
    /// The dependency analysis of a set of files, as <c>matlab.codetools.requiredFilesAndProducts</c>
    /// answers it for the session's folder and path: the "Files Required ... to Run" list.
    /// </summary>
    public required Func<IReadOnlyList<string>, DependencyReport> Analyze { get; init; }

    /// <summary>The session's current folder, where a relative output folder would land.</summary>
    public required string CurrentFolder { get; init; }
}
