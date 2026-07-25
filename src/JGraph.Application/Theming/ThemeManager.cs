using System.Windows;
using JGraph.Controls.Themes;

namespace JGraph.Application.Theming;

/// <summary>
/// Applies an application theme by swapping one entry in
/// <see cref="ResourceDictionary.MergedDictionaries"/>, and tells interested code when it changed.
/// </summary>
/// <remarks>
/// <para>
/// Only <c>JGraph.Application</c> owns <c>Application.Resources</c>, so the manager lives here even
/// though the dictionaries it loads live in <c>JGraph.Controls</c> — Controls' own XAML has to
/// reference the keys and cannot reference the application.
/// </para>
/// <para>
/// <see cref="Apply"/> replaces <em>exactly one</em> merged dictionary, found by the
/// <see cref="ThemeKeys.ThemeId"/> sentinel it carries. Clearing and rebuilding the merged set is
/// the obvious alternative and is wrong twice over: it flashes every window as controls fall back
/// to system colours mid-rebuild, and it drops <c>Shared.xaml</c> along with the theme.
/// </para>
/// </remarks>
public sealed class ThemeManager
{
    private readonly IAppThemeCatalog _catalog;
    private readonly ResourceDictionary _target;

    /// <summary>Creates a manager over the running application's resources.</summary>
    /// <param name="catalog">The themes to choose from.</param>
    public ThemeManager(IAppThemeCatalog catalog)
        : this(catalog, System.Windows.Application.Current.Resources)
    {
    }

    /// <summary>Creates a manager over an explicit resource dictionary (used by tests and by hosts
    /// that build their own resource tree).</summary>
    /// <param name="catalog">The themes to choose from.</param>
    /// <param name="target">The dictionary whose merged set holds the theme.</param>
    public ThemeManager(IAppThemeCatalog catalog, ResourceDictionary target)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(target);
        _catalog = catalog;
        _target = target;
        Current = catalog.Default;
    }

    /// <summary>The theme in force.</summary>
    public AppThemeDescriptor Current { get; private set; }

    /// <summary>
    /// Raised after the theme dictionary has been swapped. Everything that cannot follow a
    /// <c>DynamicResource</c> — AvalonDock's chrome, the syntax highlighters, the background
    /// renderers that are not <see cref="FrameworkElement"/>s — refreshes from here.
    /// </summary>
    public event EventHandler? ThemeChanged;

    /// <summary>
    /// Applies the theme with id <paramref name="id"/>, falling back to the catalog default for an
    /// unknown id. Re-applying the theme already in force does nothing.
    /// </summary>
    /// <param name="id">The theme id, typically straight from the user's settings.</param>
    public void Apply(string? id)
    {
        AppThemeDescriptor theme = _catalog.Resolve(id);
        int index = IndexOfThemeDictionary();
        if (index >= 0 && string.Equals(Current.Id, theme.Id, StringComparison.Ordinal))
        {
            return;
        }

        var dictionary = new ResourceDictionary { Source = theme.Source };
        if (index >= 0)
        {
            _target.MergedDictionaries[index] = dictionary;
        }
        else
        {
            // First application: the theme goes in front of Shared.xaml. Order does not matter for
            // DynamicResource lookups, but it keeps the merged set readable in a debugger.
            _target.MergedDictionaries.Insert(0, dictionary);
        }

        Current = theme;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private int IndexOfThemeDictionary()
    {
        for (int i = 0; i < _target.MergedDictionaries.Count; i++)
        {
            // The sentinel is what makes this unambiguous: Shared.xaml is merged alongside the
            // theme and must never define JG.Theme.Id, or the wrong entry gets replaced.
            if (_target.MergedDictionaries[i].Contains(ThemeKeys.ThemeId))
            {
                return i;
            }
        }

        return -1;
    }
}
