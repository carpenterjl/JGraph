using JGraph.Serialization.Workspace;

namespace JGraph.Application.Services;

/// <summary>
/// Loads and saves the scripting workspace's persisted state (last root folder, open files,
/// breakpoints, docking layout) between application sessions.
/// </summary>
public interface IWorkspaceStateService
{
    /// <summary>Loads the persisted state, or null when there is none (or it is unreadable).</summary>
    ScriptWorkspaceStateDto? Load();

    /// <summary>Persists <paramref name="state"/>. Failures are swallowed — state is a convenience.</summary>
    void Save(ScriptWorkspaceStateDto state);
}
