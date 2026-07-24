using System.IO;
using JGraph.Serialization.Settings;

namespace JGraph.Application.Services;

/// <summary>
/// Persists the user's preferences to <c>%AppData%\JGraph\settings.json</c> via the versioned
/// <see cref="UserSettingsFormat"/>. Like the workspace-state service, all IO failures degrade to
/// defaults — a lost preferences file must never break the app.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly string _path;

    /// <summary>Creates the service over the default per-user settings file.</summary>
    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JGraph",
            "settings.json"))
    {
    }

    /// <summary>Creates the service over an explicit settings-file path (used by tests).</summary>
    public SettingsService(string path)
    {
        _path = path;
        Current = Load();
    }

    /// <inheritdoc />
    public UserSettings Current { get; private set; }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = settings;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, UserSettingsFormat.Serialize(settings.ToDto()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence: never fail the app over a settings write.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private UserSettings Load()
    {
        try
        {
            if (File.Exists(_path) && UserSettingsFormat.Deserialize(File.ReadAllText(_path)) is { } dto)
            {
                return UserSettings.FromDto(dto);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through to defaults.
        }

        return new UserSettings();
    }
}
