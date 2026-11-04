using System.IO;
using System.Windows;
using JGraph.Scripting;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <content>
/// The Comparison Tool in the shell (App Designer window plan W14): the door <c>visdiff</c> comes through without an
/// output, and File &gt; Compare To… for the active file (R2025b's editor saves and asks for the other file; here
/// <see cref="CompareToDialog"/>), each opening an <see cref="AppComparisonWindow"/>, one per pair.
/// </content>
public partial class ScriptWorkspaceWindow
{
    private readonly Dictionary<string, AppComparisonWindow> _comparisonWindows = new(StringComparer.OrdinalIgnoreCase);
    private IScriptComparisonTool? _comparisonHost;

    /// <summary>The door <c>visdiff</c> comes through.</summary>
    private IScriptComparisonTool ComparisonDoor => _comparisonHost ??= new ComparisonHost(this);

    /// <summary>File &gt; Compare To…: the active file, saved first, against a file chosen.</summary>
    private void CompareActiveTo()
    {
        if (ActiveDocument is not { Model.FilePath: { } path } entry)
        {
            return;
        }

        if (entry.Model.IsDirty && !TrySave(entry))
        {
            return;
        }

        var dialog = new CompareToDialog(path, _documents.Select(static d => d.Model.FilePath).OfType<string>()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Chosen is not { } other)
        {
            return;
        }

        if (OpenComparison(path, other) is { } refused)
        {
            SetStatus(refused);
            MessageBox.Show(this, refused, "Compare Files", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Opens the Comparison Tool on two files, or brings the window for the pair up again.</summary>
    /// <returns>Null when it opened, else why not.</returns>
    private string? OpenComparison(string left, string right)
    {
        string key = AppComparisonWindow.KeyOf(left, right);
        if (_comparisonWindows.TryGetValue(key, out AppComparisonWindow? open))
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
        window.Closed += (_, _) => _comparisonWindows.Remove(key);
        window.Saved += saved => SetStatus($"The Comparison Tool saved {saved}.");
        _comparisonWindows[key] = window;
        window.Show();
        SetStatus($"Comparing {Path.GetFileName(left)} with {Path.GetFileName(right)}.");
        return null;
    }

    /// <summary>What <c>visdiff</c> reaches from a script: the Comparison Tool, opened on the UI thread.</summary>
    private sealed class ComparisonHost(ScriptWorkspaceWindow window) : IScriptComparisonTool
    {
        public string? Compare(string leftPath, string rightPath) => window.Dispatcher.Invoke(() => window.OpenComparison(leftPath, rightPath));
    }
}
