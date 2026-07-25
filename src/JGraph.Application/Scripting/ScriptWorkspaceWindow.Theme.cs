using System.Windows;
using AvalonDock.Themes;
using JGraph.Controls.Themes;

namespace JGraph.Application.Scripting;

/// <summary>
/// The docking frame's half of the application theme. AvalonDock paints its own chrome — pane titles,
/// tab strips, splitters, the auto-hide rail — from its own resource dictionaries and does not read
/// <see cref="ThemeKeys"/>, so the only way to make the frame agree with the panes is to hand it the
/// matching theme of its own.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    /// <summary>
    /// Whether the dark theme is in force. Bound to the theme's own <c>JG.Theme.IsDark</c> flag rather
    /// than pushed by <c>ThemeManager</c>, which keeps the window free of a dependency on it and makes
    /// the live switch work with no wiring at all.
    /// </summary>
    public static readonly DependencyProperty DockThemeIsDarkProperty =
        DependencyProperty.Register(
            nameof(DockThemeIsDark), typeof(bool), typeof(ScriptWorkspaceWindow),
            new FrameworkPropertyMetadata(false, OnDockThemeIsDarkChanged));

    /// <summary>Whether the docking frame is showing its dark chrome.</summary>
    public bool DockThemeIsDark
    {
        get => (bool)GetValue(DockThemeIsDarkProperty);
        set => SetValue(DockThemeIsDarkProperty, value);
    }

    private void InitializeDockTheme() => SetResourceReference(DockThemeIsDarkProperty, ThemeKeys.ThemeIsDark);

    private static void OnDockThemeIsDarkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Reassigning Theme re-templates every live pane. That is the point — but it is also why the
        // switch is done here and only here: doing it twice, or per pane, is what historically dropped
        // floating windows.
        var window = (ScriptWorkspaceWindow)d;
        window.DockManager.Theme = (bool)e.NewValue ? new Vs2013DarkTheme() : new Vs2013LightTheme();
    }
}
