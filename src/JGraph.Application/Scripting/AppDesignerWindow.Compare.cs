using System.IO;
using System.Windows;
using System.Windows.Input;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <content>
/// Compare To (App Designer window plan W14): the DESIGNER and EDITOR tabs' button and the tab menu's item, R2025b's
/// <c>OpenFileComparisonDialogAction</c> - the app saved first when it is dirty or has no file (<c>SaveAppModelCommand</c>),
/// then <c>comparisons_view_actions/gui/compareTo</c>'s file chooser and <c>compareInDesktop</c>, here JGraph's
/// <see cref="CompareToDialog"/> and <see cref="AppComparisonWindow"/>, one window per pair brought up again.
/// </content>
internal sealed partial class AppDesignerWindow
{
    /// <summary>Compare To: the active app against a file chosen.</summary>
    public static readonly RoutedCommand CompareTo = new(nameof(CompareTo), typeof(AppDesignerWindow));

    private readonly Dictionary<string, AppComparisonWindow> _comparisons = new(StringComparer.OrdinalIgnoreCase);

    private void BindCompareCommands()
    {
        CommandBindings.Add(new CommandBinding(CompareTo, (_, _) => CompareApp(Active), (_, e) => e.CanExecute = Active is not null));
    }

    /// <summary>Compare To for <paramref name="app"/>: saved first, then the dialog, then the Comparison Tool.</summary>
    private void CompareApp(AppTab? app)
    {
        if (app is null)
        {
            return;
        }

        if ((app.Document.IsDirty || app.Document.Path is null) && !SaveBeforeRun(app))
        {
            return;
        }

        string left = app.Document.Path!;
        var dialog = new CompareToDialog(left, Apps.Select(static a => a.Document.Path).OfType<string>()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Chosen is not { } right)
        {
            return;
        }

        if (OpenComparison(left, right) is { } refused)
        {
            _status.Text = refused;
            MessageBox.Show(this, refused, "Compare Files", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Opens the Comparison Tool on <paramref name="left"/> and <paramref name="right"/>, or brings the window for the pair up again.</summary>
    /// <returns>Null when it opened, else why not.</returns>
    public string? OpenComparison(string left, string right)
    {
        ArgumentException.ThrowIfNullOrEmpty(left);
        ArgumentException.ThrowIfNullOrEmpty(right);
        string key = AppComparisonWindow.KeyOf(left, right);
        if (_comparisons.TryGetValue(key, out AppComparisonWindow? open))
        {
            open.Activate();
            return null;
        }

        FileComparison comparison;
        try
        {
            comparison = FileComparison.Compare(left, right);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return $"Unable to compare '{left}' and '{right}'. {ex.Message}";
        }

        var window = new AppComparisonWindow(comparison) { Owner = this };
        window.Closed += (_, _) => _comparisons.Remove(key);
        _comparisons[key] = window;
        window.Show();
        return null;
    }
}
