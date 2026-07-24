namespace JGraph.Application.Services;

/// <summary>
/// Holds the user's current <see cref="UserSettings"/> and persists changes. A singleton: everything
/// that reads a preference reads <see cref="Current"/>, and the Options dialog commits through
/// <see cref="Save"/>.
/// </summary>
public interface ISettingsService
{
    /// <summary>The settings in force. Read on each use, so a saved change takes effect without a restart.</summary>
    UserSettings Current { get; }

    /// <summary>Raised after <see cref="Save"/> replaces <see cref="Current"/>, so views can refresh.</summary>
    event EventHandler? Changed;

    /// <summary>Replaces <see cref="Current"/> with <paramref name="settings"/> and writes it to disk.</summary>
    void Save(UserSettings settings);
}
