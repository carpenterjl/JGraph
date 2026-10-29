using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <summary>
/// The designer window's document tabs and its frame (App Designer window plan, stage W2): each
/// app's tab menu as R2025b builds it (<see cref="DesignTabMenu"/>), the prompt to reload an app
/// whose file changed outside, the title as R2025b writes it, the window's place remembered, and
/// the setting that saves every app when the window loses the focus.
/// </summary>
internal sealed partial class AppDesignerWindow
{
    // The folder this window last made the session's current folder: R2025b's tab menu disables
    // Change Current Folder for it. JGraph's session does not report a cd typed at the prompt.
    private string? _currentFolder;

    /// <summary>
    /// Runs a statement in the MATLAB session, for the tab menu's current folder and search path
    /// items (<c>cd</c>, <c>addpath</c>, <c>rmpath</c>). Null when there is no session: the items are
    /// then disabled.
    /// </summary>
    public Func<string, Task>? SessionStatement { get; set; }

    /// <summary>The window's place as "left,top,width,height[,max]": set before it is shown to put it there, read after it closed to keep it.</summary>
    public string? Placement
    {
        get => _placement;
        set
        {
            _placement = value;
            if (value?.Split(',') is [var l, var t, var w, var h, .. var rest]
                && double.TryParse(l, NumberStyles.Float, CultureInfo.InvariantCulture, out double left)
                && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double top)
                && double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out double width)
                && double.TryParse(h, NumberStyles.Float, CultureInfo.InvariantCulture, out double height)
                && width >= 200 && height >= 150
                && new Rect(left, top, width, height).IntersectsWith(new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                (Left, Top, Width, Height) = (left, top, width, height);
                if (rest is ["max"])
                {
                    WindowState = WindowState.Maximized;
                }
            }
        }
    }

    private string? _placement;

    /// <summary>The window's title: R2025b's "App Designer - " and the active app (<see cref="DesignSettings.Title"/>).</summary>
    private void ShowTitle() => Title = Active is { Document.Path: null } app
        ? Settings.Title(app.FileName, app.Document.IsDirty, isNew: true)
        : Settings.Title(Active?.Document.Path, Active?.Document.IsDirty == true);

    /// <summary>Keeps the window's place for <see cref="Placement"/>, while the window still has one.</summary>
    private void RememberPlace()
    {
        Rect bounds = RestoreBounds;
        if (bounds.IsEmpty || bounds.Width <= 0)
        {
            return;
        }

        _placement = string.Join(',', new[] { bounds.Left, bounds.Top, bounds.Width, bounds.Height }.Select(static v => Math.Round(v).ToString(CultureInfo.InvariantCulture)))
            + (WindowState == WindowState.Maximized ? ",max" : string.Empty);
    }

    /// <inheritdoc />
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // "Save changes upon clicking away" (matlab.appdesigner.AutoSaveOnClickAway): every app with
        // a file and changes is saved when the window loses the focus.
        if (!Settings.GetBool(DesignSettings.AutoSaveOnClickAway) || !IsVisible)
        {
            return;
        }

        foreach (AppTab app in Apps.Where(static a => a.Document is { Path: not null, IsDirty: true, CanSave: true }).ToList())
        {
            Save(app);
        }
    }

    /// <summary>The app's tab gets its menu and its file a watch.</summary>
    private void AttachTab(AppTab app)
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) => FillTabMenu(menu, app);
        menu.Items.Add(new MenuItem()); // filled when it opens
        app.Item.ContextMenu = menu;
        Watch(app);
    }

    private void DetachTab(AppTab app)
    {
        if (app.Watcher is { } watcher)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            app.Watcher = null;
        }
    }

    private void FillTabMenu(ContextMenu menu, AppTab app)
    {
        menu.Items.Clear();
        List<TabItem> tabs = [.. _tabs.Items.OfType<TabItem>()];
        int index = tabs.IndexOf(app.Item);
        string title = app.FileName;
        IReadOnlyList<string> path = PathFolders?.Invoke() ?? [];
        foreach (DesignTabMenuItem entry in DesignTabMenu.Items(app.Document.Path, title, index, tabs.Count,
            folder => string.Equals(folder, _currentFolder, StringComparison.OrdinalIgnoreCase),
            folder => path.Any(p => string.Equals(Path.TrimEndingDirectorySeparator(p), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase))))
        {
            if (entry.Command is not { } command)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            bool enabled = entry.Enabled && command switch
            {
                DesignTabMenuCommand.ChangeCurrentFolder or DesignTabMenuCommand.AddToSearchPath or DesignTabMenuCommand.RemoveFromSearchPath => SessionStatement is not null,
                DesignTabMenuCommand.NewApp => DesignActions.IsLive("OPEN_STARTSCREEN_DIALOG"),
                DesignTabMenuCommand.CompareTo => DesignActions.IsLive("OPEN_FILE_COMPARISON_DIALOG"),
                _ => true,
            };
            var item = new MenuItem { Header = entry.Text, IsEnabled = enabled };
            System.Windows.Automation.AutomationProperties.SetAutomationId(item, "AppTab." + command);
            item.Click += (_, _) => RunTabCommand(command, app);
            menu.Items.Add(item);
        }
    }

    private async void RunTabCommand(DesignTabMenuCommand command, AppTab app)
    {
        string? folder = app.Document.Path is { } file ? Path.GetDirectoryName(file) : null;
        switch (command)
        {
            case DesignTabMenuCommand.ChangeCurrentFolder when folder is not null && SessionStatement is { } run:
                await run($"cd('{Quoted(folder)}')").ConfigureAwait(true);
                _currentFolder = folder;
                break;
            case DesignTabMenuCommand.AddToSearchPath when folder is not null && SessionStatement is { } run:
                await run($"addpath('{Quoted(folder)}')").ConfigureAwait(true);
                break;
            case DesignTabMenuCommand.RemoveFromSearchPath when folder is not null && SessionStatement is { } run:
                await run($"rmpath('{Quoted(folder)}')").ConfigureAwait(true);
                break;
            case DesignTabMenuCommand.ShowInExplorer when app.Document.Path is { } shown:
                try
                {
                    using System.Diagnostics.Process? _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{shown}\"") { UseShellExecute = false });
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    _status.Text = $"Could not open Explorer: {ex.Message}";
                }

                break;
            case DesignTabMenuCommand.CopyFullPath when app.Document.Path is { } copied:
                Clipboard.SetText(copied);
                break;
            case DesignTabMenuCommand.NewApp:
                OpenStartPage();
                break;
            case DesignTabMenuCommand.Close:
                CloseTab(app);
                break;
            case DesignTabMenuCommand.CloseAllExcept:
                foreach (AppTab other in Apps.Where(a => !ReferenceEquals(a, app)).ToList())
                {
                    CloseTab(other);
                }

                break;
            case DesignTabMenuCommand.CloseAllToTheRight:
                int at = _tabs.Items.IndexOf(app.Item);
                foreach (AppTab right in Apps.Skip(at + 1).ToList())
                {
                    CloseTab(right);
                }

                break;
            case DesignTabMenuCommand.CloseAll:
                foreach (AppTab any in Apps.ToList())
                {
                    CloseTab(any);
                }

                break;
            case DesignTabMenuCommand.Alphabetize:
                object? selected = _tabs.SelectedItem;
                List<TabItem> ordered = [.. _tabs.Items.OfType<TabItem>().OrderBy(static t => ((AppTab)t.Tag).Header, StringComparer.CurrentCultureIgnoreCase)];
                _tabs.Items.Clear();
                foreach (TabItem tab in ordered)
                {
                    _tabs.Items.Add(tab);
                }

                _tabs.SelectedItem = selected;
                break;
        }

        static string Quoted(string text) => text.Replace("'", "''", StringComparison.Ordinal);
    }

    /// <summary>A save by this window: the change it makes to the file is not one made outside.</summary>
    private void Saving(AppTab app, Action save)
    {
        app.SavedUntil = DateTime.UtcNow.AddSeconds(2);
        try
        {
            save();
        }
        finally
        {
            app.SavedUntil = DateTime.UtcNow.AddSeconds(2);
            Watch(app);
        }

    }

    /// <summary>Watches the app's file (anew after a Save As) for a change made outside.</summary>
    private void Watch(AppTab app)
    {
        DetachTab(app);
        if (app.Document.Path is not { } file || Path.GetDirectoryName(file) is not { Length: > 0 } folder || !Directory.Exists(folder))
        {
            return;
        }

        var watcher = new FileSystemWatcher(folder, Path.GetFileName(file))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        watcher.Changed += (_, _) => Dispatcher.BeginInvoke(() => ChangedOutside(app), DispatcherPriority.Background);
        watcher.Renamed += (_, _) => Dispatcher.BeginInvoke(() => ChangedOutside(app), DispatcherPriority.Background);
        watcher.EnableRaisingEvents = true;
        app.Watcher = watcher;
        app.SeenWrite = File.GetLastWriteTimeUtc(file);
    }

    /// <summary>
    /// The app's file was written by someone else: App Designer asks "{0} has been modified outside of
    /// App Designer. Reload the file?", saying that unsaved changes will be lost when the app has some,
    /// and reopens it from its file on Yes.
    /// </summary>
    private void ChangedOutside(AppTab app)
    {
        if (app.Prompting || DateTime.UtcNow < app.SavedUntil || !_tabs.Items.Contains(app.Item) || app.Document.Path is not { } path || !File.Exists(path))
        {
            return;
        }

        // A write raises several changes; it is asked about once.
        DateTime written = File.GetLastWriteTimeUtc(path);
        if (written == app.SeenWrite)
        {
            return;
        }

        app.SeenWrite = written;
        app.Prompting = true;
        try
        {
            string name = Path.GetFileName(path);
            string message = $"{name} has been modified outside of App Designer.\n\nReload the file?"
                + (app.Document.IsDirty ? "\n\nUnsaved changes will be lost." : string.Empty);
            if (MessageBox.Show(this, message, "App Designer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            int at = _tabs.Items.IndexOf(app.Item);
            _tabs.Items.Remove(app.Item);
            DetachTab(app);
            app.Canvas.Document = null;
            app.CodeView.Document = null;
            app.Document.Dispose();
            if (Open(path) is { } refused && refused != Cancelled)
            {
                _status.Text = refused;
                return;
            }

            // The reopened app keeps its tab's place.
            if (Apps.FirstOrDefault(a => string.Equals(a.Document.Path, path, StringComparison.OrdinalIgnoreCase)) is { } reopened && at >= 0 && at < _tabs.Items.Count)
            {
                _tabs.Items.Remove(reopened.Item);
                _tabs.Items.Insert(at, reopened.Item);
                _tabs.SelectedItem = reopened.Item;
            }
        }
        finally
        {
            app.Prompting = false;
        }
    }
}
