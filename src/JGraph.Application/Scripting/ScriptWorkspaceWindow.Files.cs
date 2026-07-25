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

    private void RefreshFilesTree()
    {
        FilesTree.Items.Clear();
        if (_workspace is null)
        {
            return;
        }

        var root = new TreeViewItem
        {
            Header = Path.GetFileName(_workspace.RootPath.TrimEnd(Path.DirectorySeparatorChar)),
            IsExpanded = true,
        };
        var refresh = new MenuItem { Header = "Refresh" };
        refresh.Click += (_, _) => RefreshFilesTree();
        root.ContextMenu = new ContextMenu { Items = { refresh } };
        try
        {
            foreach (WorkspaceEntry entry in _workspace.EnumerateAll())
            {
                root.Items.Add(BuildTreeItem(entry));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not read workspace: {ex.Message}");
        }

        FilesTree.Items.Add(root);
    }

    private TreeViewItem BuildTreeItem(WorkspaceEntry entry)
    {
        var item = new TreeViewItem { Header = entry.Name, Tag = entry };
        if (entry.IsDirectory)
        {
            var setRoot = new MenuItem { Header = "Set as workspace root" };
            setRoot.Click += (_, _) => OpenWorkspace(entry.FullPath);
            var refresh = new MenuItem { Header = "Refresh" };
            refresh.Click += (_, _) => RefreshFilesTree();
            item.ContextMenu = new ContextMenu { Items = { setRoot, refresh } };
        }

        foreach (WorkspaceEntry child in entry.Children)
        {
            item.Items.Add(BuildTreeItem(child));
        }

        return item;
    }

    private void OnFilesTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesTree.SelectedItem is not TreeViewItem { Tag: WorkspaceEntry entry })
        {
            return;
        }

        // A folder becomes the new workspace root (MATLAB's Current Folder navigation).
        if (entry.IsDirectory)
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
