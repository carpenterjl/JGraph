using JGraph.Deploy;

namespace JGraph.Scripting;

/// <summary>
/// The host's compiler apps (compiler and SDK plan, stage K19), behind <c>standaloneApplicationCompiler</c>
/// and its siblings, <c>applicationCompiler</c>, <c>libraryCompiler</c>, <c>productionServerCompiler</c>
/// and <c>deploytool</c>: the app brings its window up; a host without windows (the CLI, a batch run,
/// tests) passes none, and the builtin refuses with the reason. Invoked on the engine's background
/// thread, so a UI host marshals it.
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
}

/// <summary>What a compiler app's window asks the session that opened it for.</summary>
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
