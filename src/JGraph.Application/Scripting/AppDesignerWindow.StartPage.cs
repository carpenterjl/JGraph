using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using JGraph.Controls.Designer;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <summary>
/// The Start Page and new apps (App Designer window plan, stage W3). With no app open the window
/// shows the Start Page in place of the panes, as R2025b's window starts on it; with apps open,
/// DESIGNER > New and the tab menu's New App show it as R2025b's "App Designer Start Page" dialog.
/// Its tiles make new apps named as R2025b names them (app1, app2, ...), never saved until Save
/// asks for a file; its examples are JGraph's own, copied out and opened; the recent files are
/// R2025b's list, kept in the settings and listed again under DESIGNER > Open.
/// </summary>
internal sealed partial class AppDesignerWindow
{
    /// <summary>DESIGNER > New: the Start Page (<c>OPEN_STARTSCREEN_DIALOG</c>).</summary>
    public static readonly RoutedCommand ShowStartPage = new(nameof(ShowStartPage), typeof(AppDesignerWindow));

    private readonly DesignNewNames _names = new();
    private readonly DesignStartPageView _startPage = new();
    private Window? _startDialog;

    /// <summary>
    /// The folder JGraph's example apps are copied into, as <c>openExample</c> copies R2025b's into
    /// the user's MATLAB folder: Documents\JGraph\Examples, or the folder <c>JGRAPH_APP_EXAMPLES</c>
    /// names (a window check points it at its scratch folder).
    /// </summary>
    public string ExamplesRoot { get; set; } = Environment.GetEnvironmentVariable("JGRAPH_APP_EXAMPLES") is { Length: > 0 } named
        ? named
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "JGraph", "Examples");

    private void BuildStartPage()
    {
        Wire(_startPage);
        CommandBindings.Add(new CommandBinding(ShowStartPage, (_, _) => OpenStartPage()));
        _toolstrip.PopupPanelProvider = tag => tag == DesignToolstrip.RecentFilesPanel
            ? DesignToolstrip.RecentFilesEntries(Settings.RecentFiles().Files)
            : null;
        _startPage.RecentFiles = Settings.RecentFiles().Files;
    }

    /// <summary>The page's choices: each closes the dialog first when the page is the dialog's.</summary>
    private void Wire(DesignStartPageView page)
    {
        page.NewRequested += (_, kind) => FromPage(() => ShowRefusal(OpenNew(kind)));
        page.OpenRequested += (_, _) => FromPage(PromptOpen);
        page.RecentRequested += (_, path) => FromPage(() => OpenRecent(path));
        page.ExampleRequested += (_, example) => FromPage(() => OpenExample(example));
        page.LinkRequested += (_, link) => OpenHelp(link.GuideAnchor);
    }

    private void FromPage(Action act)
    {
        if (_startDialog is { } dialog)
        {
            _startDialog = null;
            dialog.Close();
            Dispatcher.BeginInvoke(act, DispatcherPriority.Background);
            return;
        }

        act();
    }

    /// <summary>
    /// DESIGNER > New: the Start Page as R2025b's modal dialog over the open apps; with none open the
    /// page is already the window's, and gets the focus.
    /// </summary>
    private void OpenStartPage()
    {
        if (!Apps.Any())
        {
            _startPage.FocusFirstTile();
            return;
        }

        var page = new DesignStartPageView { RecentFiles = Settings.RecentFiles().Files };
        Wire(page);
        var dialog = new Window
        {
            Title = DesignStartPage.Title,
            Content = page,
            Owner = this,
            Width = Math.Min(980, Math.Max(600, ActualWidth - 80)),
            Height = Math.Min(680, Math.Max(420, ActualHeight - 80)),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        dialog.SetResourceReference(BackgroundProperty, JGraph.Controls.Themes.ThemeKeys.Window);
        System.Windows.Automation.AutomationProperties.SetAutomationId(dialog, "StartPageDialog");
        dialog.Loaded += (_, _) => page.FocusFirstTile();
        _startDialog = dialog;
        dialog.ShowDialog();
        _startDialog = null;
    }

    /// <summary>The Start Page in place of the panes while no app is open.</summary>
    private void ShowStartPageInPlace()
    {
        bool none = !Apps.Any();
        _startPage.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        _body.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        _startPage.RecentFiles = Settings.RecentFiles().Files;
    }

    /// <summary>
    /// A new app of <paramref name="kind"/> in a tab of its own, named as R2025b names it and not
    /// saved: Save and Run ask for its file first. A custom component waits for stage W15.
    /// </summary>
    /// <returns>Null when it opened, else why not.</returns>
    public string? OpenNew(DesignNewKind kind)
    {
        DesignStartTile tile = DesignStartPage.Tile(kind);
        if (!tile.IsLive)
        {
            return $"{tile.Text}: JGraph makes this in stage {tile.Stage} of the App Designer window plan.";
        }

        DesignDocument document;
        try
        {
            document = DesignDocument.FromText(DesignStartPage.NewText(kind, _names.Next(kind)), null, PathFolders?.Invoke());
        }
        catch (InvalidDataException ex)
        {
            return ex.Message;
        }

        AddApp(document);
        return null;
    }

    /// <summary>A recent file: opened, or taken off the list when it is not there any more (R2025b removes a file whose open failed for its name).</summary>
    private void OpenRecent(string path)
    {
        if (!File.Exists(path))
        {
            DesignRecentFiles files = Settings.RecentFiles();
            files.Remove(path);
            Settings.SetRecentFiles(files);
            ShowStartPageInPlace();
            MessageBox.Show(this, $"{path} not found.", "App Designer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ShowRefusal(Open(path));
    }

    /// <summary>
    /// An example: copied into <see cref="ExamplesRoot"/> (an existing copy is kept as the user left
    /// it), made the session's current folder as <c>openExample</c> makes its copy's, and opened.
    /// </summary>
    private async void OpenExample(DesignExample example)
    {
        string main;
        try
        {
            main = DesignStartPage.CopyExample(example, ExamplesRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowRefusal($"Could not copy the example {example.Title}: {ex.Message}");
            return;
        }

        ShowRefusal(Open(main));
        if (SessionStatement is { } run && Path.GetDirectoryName(main) is { } folder)
        {
            await run($"cd('{folder.Replace("'", "''", StringComparison.Ordinal)}')").ConfigureAwait(true);
            _currentFolder = folder;
        }
    }

    private void ShowRefusal(string? refused)
    {
        if (refused is not null && refused != Cancelled)
        {
            MessageBox.Show(this, refused, "App Designer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>An app opened or saved goes first in the recent files (<c>RecentlyOpenedFilesManager</c>).</summary>
    private void RememberRecent(DesignDocument document)
    {
        if (document.Path is not { } path)
        {
            return;
        }

        DesignRecentFiles files = Settings.RecentFiles();
        files.Push(new DesignRecentFile(path, document.Design.IsResponsive ? DesignAppType.Responsive : DesignAppType.Standard));
        Settings.SetRecentFiles(files);
        _startPage.RecentFiles = files.Files;
    }
}
