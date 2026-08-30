using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JGraph.Core.Model;
using JGraph.Scripting.Workspace;

namespace JGraph.Application.Scripting;

/// <summary>
/// The Files pane: the workspace root, its folder tree, and what a double-click opens. A folder
/// re-roots the workspace (MATLAB's Current Folder navigation); a file is dispatched by extension to
/// the data viewer, a figure window, or a script tab.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    private void PromptOpenWorkspace()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Open workspace folder" };
        if (dialog.ShowDialog(this) == true)
        {
            OpenWorkspace(dialog.FolderName);
        }
    }

    private void OpenWorkspace(string rootPath)
    {
        try
        {
            ScriptWorkspace workspace = ScriptWorkspace.Open(rootPath);
            _workspace?.Dispose();
            _workspace = workspace;
            _workspace.Changed += OnWorkspaceChanged;
            AddressBox.Text = _workspace.RootPath;
            RefreshFilesTree();
            // The shell is the main window now, so its title is the app's — name the open workspace
            // in it the way an IDE does.
            Title = $"JGraph — {_workspace.RootPath}";
            SetStatus($"Workspace: {rootPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            SetStatus($"Could not open workspace: {ex.Message}");
        }
    }

    private void OnUpFolderClick(object sender, RoutedEventArgs e)
    {
        if (_workspace is null)
        {
            SetStatus("Open a workspace first.");
            return;
        }

        string? parent = Path.GetDirectoryName(_workspace.RootPath.TrimEnd(Path.DirectorySeparatorChar));
        if (parent is null)
        {
            SetStatus("Already at the drive root.");
            return;
        }

        OpenWorkspace(parent);
    }

    private void OnAddressBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        string typed = AddressBox.Text.Trim();
        if (Directory.Exists(typed))
        {
            OpenWorkspace(typed);
        }
        else
        {
            SetStatus($"Folder not found: '{typed}'.");
            AddressBox.Text = _workspace?.RootPath ?? string.Empty;
        }
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshFilesTree);

    /// <summary>
    /// Stands in for a folder's unread contents. Its presence is what says "this folder has not been
    /// opened yet", and it gives the item an expander chevron without anyone reading the directory.
    /// </summary>
    private static readonly object UnreadFolder = new();

    /// <summary>
    /// Rebuilds the tree, reading only the root and re-opening the folders that were open before.
    /// The root can be any directory the user names — the parent button walks straight up to a user
    /// profile or a drive — so nothing here may be proportional to what is under the root: a folder
    /// is read when it is expanded, and not before.
    /// </summary>
    private void RefreshFilesTree()
    {
        var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectExpandedFolders(FilesTree.Items, open);
        string? selected = (FilesTree.SelectedItem as TreeViewItem)?.Tag is WorkspaceEntry previous
            ? previous.FullPath
            : null;

        FilesTree.Items.Clear();
        if (_workspace is null)
        {
            return;
        }

        var root = new TreeViewItem
        {
            Header = Path.GetFileName(_workspace.RootPath.TrimEnd(Path.DirectorySeparatorChar)),
            Tag = new WorkspaceEntry(_workspace.RootPath, string.Empty, IsDirectory: true, []),
            ContextMenu = FolderMenu(_workspace.RootPath, isRoot: true),
        };
        root.Items.Add(UnreadFolder);
        root.Expanded += OnFolderExpanded;
        FilesTree.Items.Add(root);

        // The root is always open; the rest are opened again only if they were open before, which is
        // what stops a file written by a running script from collapsing the pane under the user.
        root.IsExpanded = true;
        ReopenFolders(root, open, selected);
    }

    private TreeViewItem BuildTreeItem(WorkspaceEntry entry)
    {
        var item = new TreeViewItem { Header = entry.Name, Tag = entry };
        if (entry.IsDirectory)
        {
            item.ContextMenu = FolderMenu(entry.FullPath, isRoot: false);
            item.Items.Add(UnreadFolder);
            item.Expanded += OnFolderExpanded;
        }

        return item;
    }

    private ContextMenu FolderMenu(string path, bool isRoot)
    {
        var menu = new ContextMenu();
        if (!isRoot)
        {
            var setRoot = new MenuItem { Header = "Set as workspace root" };
            setRoot.Click += (_, _) => OpenWorkspace(path);
            menu.Items.Add(setRoot);
        }

        var refresh = new MenuItem { Header = "Refresh" };
        refresh.Click += (_, _) => RefreshFilesTree();
        menu.Items.Add(refresh);
        return menu;
    }

    private void OnFolderExpanded(object sender, RoutedEventArgs e)
    {
        // Expanded bubbles, so a child's expansion arrives at every ancestor as well.
        if (sender is TreeViewItem item && ReferenceEquals(sender, e.OriginalSource))
        {
            ReadFolder(item);
        }
    }

    /// <summary>Replaces a folder's placeholder with its immediate entries, once.</summary>
    private void ReadFolder(TreeViewItem item)
    {
        if (item.Items.Count != 1 || !ReferenceEquals(item.Items[0], UnreadFolder))
        {
            return;
        }

        item.Items.Clear();
        if (_workspace is null || item.Tag is not WorkspaceEntry folder)
        {
            return;
        }

        try
        {
            foreach (WorkspaceEntry child in _workspace.EnumerateChildren(folder.FullPath))
            {
                item.Items.Add(BuildTreeItem(child));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not read '{folder.Name}': {ex.Message}");
        }
    }

    private static void CollectExpandedFolders(ItemCollection items, HashSet<string> open)
    {
        foreach (object child in items)
        {
            if (child is TreeViewItem { IsExpanded: true, Tag: WorkspaceEntry entry } item)
            {
                open.Add(entry.FullPath);
                CollectExpandedFolders(item.Items, open);
            }
        }
    }

    private void ReopenFolders(TreeViewItem item, HashSet<string> open, string? selected)
    {
        if (item.Tag is not WorkspaceEntry entry)
        {
            return;
        }

        if (selected is not null && string.Equals(entry.FullPath, selected, StringComparison.OrdinalIgnoreCase))
        {
            item.IsSelected = true;
        }

        if (!entry.IsDirectory || !open.Contains(entry.FullPath))
        {
            return;
        }

        item.IsExpanded = true;
        ReadFolder(item); // A no-op when setting IsExpanded above already fired Expanded.
        foreach (object child in item.Items)
        {
            if (child is TreeViewItem sub)
            {
                ReopenFolders(sub, open, selected);
            }
        }
    }

    private void OnFilesTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesTree.SelectedItem is not TreeViewItem { Tag: WorkspaceEntry entry })
        {
            return;
        }

        // A folder becomes the new workspace root (MATLAB's Current Folder navigation) — except the
        // root itself, which is already open: re-opening it would rebuild the workspace and its
        // watcher for no change, and the double-click has just collapsed the node, so the rebuild
        // would take every expanded folder with it.
        if (entry.IsDirectory
            && !string.Equals(entry.FullPath, _workspace?.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            OpenWorkspace(entry.FullPath);
            e.Handled = true;
            return;
        }

        switch (WorkspaceFiles.Classify(entry.FullPath))
        {
            case WorkspaceFileKind.Data:
                OpenDataFile(entry.FullPath);
                break;
            case WorkspaceFileKind.Figure:
                OpenGraphFile(entry.FullPath);
                break;
            case WorkspaceFileKind.Document:
                OpenDocument(entry.FullPath);
                break;
            default:
                SetStatus($"No viewer for '{Path.GetExtension(entry.FullPath)}' files.");
                break;
        }

        e.Handled = true;
    }

    /// <summary>Opens a saved <c>.graph</c> figure document as a live numbered figure window.</summary>
    private void OpenGraphFile(string path)
    {
        try
        {
            FigureModel figure = JGraph.Serialization.GraphFormat.Load(path);
            int number = JGraph.Api.JG.RegisterFigure(figure); // Same numbering scripts use.
            ShowFigureOnUi(number, figure);
            SetStatus($"Opened figure '{Path.GetFileName(path)}' as Figure {number}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JGraph.Serialization.GraphFormatException)
        {
            SetStatus($"Could not open '{Path.GetFileName(path)}': {ex.Message}");
        }
    }
}
