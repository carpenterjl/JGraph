namespace JGraph.Application.Theming;

/// <summary>
/// The set of application themes the user can choose between.
/// </summary>
/// <remarks>
/// Deliberately <em>not</em> plugin-extensible. A plugin-supplied theme means loading third-party
/// BAML straight into <c>Application.Resources</c> with no validation story; the key set is still
/// moving; and <c>JGraph.Plugins</c> targets net8.0, so extending <c>IPlugin</c> with a theme would
/// force <c>net8.0-windows</c> onto the one contract that keeps <c>jgraph.exe</c> headless. The
/// interface exists so the door stays open, not because it is open today.
/// </remarks>
public interface IAppThemeCatalog
{
    /// <summary>The available themes, in the order Options should offer them.</summary>
    IReadOnlyList<AppThemeDescriptor> Themes { get; }

    /// <summary>The theme applied when the user has never chosen one.</summary>
    AppThemeDescriptor Default { get; }

    /// <summary>
    /// The theme with id <paramref name="id"/>, or <see cref="Default"/> when the id is null,
    /// unknown, or names a theme this build no longer ships.
    /// </summary>
    AppThemeDescriptor Resolve(string? id);
}
