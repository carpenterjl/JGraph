using System.IO;
using JGraph.Serialization.Workspace;

namespace JGraph.Application.Services;

/// <summary>
/// Persists the scripting workspace state to <c>%AppData%\JGraph\workspace.json</c> via the
/// versioned <see cref="ScriptWorkspaceStateFormat"/>. All IO failures degrade to "no state" —
/// losing window layout must never break the app.
/// </summary>
public sealed class WorkspaceStateService : IWorkspaceStateService
{
    private readonly string _path;

    /// <summary>Creates the service over the default per-user state file.</summary>
    public WorkspaceStateService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JGraph",
            "workspace.json"))
    {
    }

    /// <summary>Creates the service over an explicit state-file path (used by tests).</summary>
    public WorkspaceStateService(string path) => _path = path;

    /// <inheritdoc />
    public ScriptWorkspaceStateDto? Load()
    {
        try
        {
            return File.Exists(_path)
                ? ScriptWorkspaceStateFormat.Deserialize(File.ReadAllText(_path))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Save(ScriptWorkspaceStateDto state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, ScriptWorkspaceStateFormat.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence: never fail the app over workspace state.
        }
    }
}
