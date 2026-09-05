using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using JGraph.Controls.Scripting;
using JGraph.Scripting.Workspace;
using JGraph.Serialization.Workspace;

namespace JGraph.Application.Scripting;

/// <summary>
/// The dock layout and session persistence: restoring the previous session on open, the tool-pane
/// registry behind the View menu, and saving workspace, open files, breakpoints, the console
/// language and layout on close.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    /// <summary>
    /// Restores the previous session — workspace root, open tabs, breakpoints, dock layout and window
    /// placement — or, on a first run, seeds and opens a workspace of the shipped examples. Runs once;
    /// later calls are no-ops.
    /// </summary>
    /// <remarks>
    /// Deliberately not in the constructor. As the application shell the window is built by the DI
    /// container, and construction should be cheap: the file and directory work here is what the
    /// splash reports progress against, and it must happen before the window is first shown.
    /// </remarks>
    public void RestoreSession()
    {
        if (_sessionRestored)
        {
            return;
        }

        _sessionRestored = true;
        try
        {
            RestoreState();
        }
        catch (Exception ex)
        {
            // Restoring the previous session is a convenience — never let it break the window.
            SetStatus($"Could not restore the previous session: {ex.Message}");
        }

        if (_workspace is null)
        {
            SeedFirstRunWorkspace();
        }

        if (_documents.Count == 0)
        {
            OpenNewScript(DefaultNewScriptLanguage());
        }

        UpdateCommandStates();
    }

    /// <summary>
    /// First run: copies the shipped examples into the user's documents and opens them as the
    /// workspace, so JGraph starts with something to look at rather than an empty tree. Any failure
    /// (no examples deployed, a read-only or unwritable documents folder) simply leaves the workspace
    /// closed — the blank-script fallback still applies.
    /// </summary>
    private void SeedFirstRunWorkspace()
    {
        if (DefaultScriptDirectory() is { } configured && Directory.Exists(configured))
        {
            OpenWorkspace(configured);
            return;
        }

        try
        {
            string source = Path.Combine(AppContext.BaseDirectory, "examples");
            if (!Directory.Exists(source))
            {
                return;
            }

            string target = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "JGraph", "Examples");
            Directory.CreateDirectory(target);

            IReadOnlyList<ExampleWorkspaceSeeder.SeedFile> plan = ExampleWorkspaceSeeder.Plan(
                Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories), source, target, File.Exists);
            foreach (ExampleWorkspaceSeeder.SeedFile file in plan)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                File.Copy(file.Source, file.Target);
            }

            OpenWorkspace(target);
            string welcome = Path.Combine(target, "example.jgs");
            if (File.Exists(welcome))
            {
                OpenDocument(welcome);
            }

            SetStatus($"Welcome to JGraph — opened the examples in {target}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            SetStatus($"Could not set up the example workspace: {ex.Message}");
        }
    }

    private void RestoreState()
    {
        ScriptWorkspaceStateDto? state = _stateService.Load();
        if (state is null)
        {
            return;
        }

        RestorePlacement(state);
        SelectConsoleLanguage(state.ConsoleLanguage);

        foreach ((string file, List<int> lines) in state.Breakpoints)
        {
            _persistedBreakpoints[file] = lines; // round-tripped for the debugger milestones
        }

        if (state.RootPath is { Length: > 0 } root && Directory.Exists(root))
        {
            OpenWorkspace(root);
        }

        foreach (string file in state.OpenFiles.Where(File.Exists))
        {
            OpenDocument(file);
        }

        if (state.ActiveFile is { Length: > 0 } active)
        {
            DocumentEntry? entry = _documents.FirstOrDefault(d =>
                string.Equals(d.Model.FilePath, active, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
            {
                entry.Document.IsActive = true;
            }
        }

        // A layout written for an arrangement we can no longer express is discarded outright rather
        // than half-restored.
        if (state.DockLayoutXml is { Length: > 0 } layoutXml
            && state.LayoutSchema >= ScriptWorkspaceStateFormat.MinimumCompatibleLayoutSchema)
        {
            TryRestoreLayout(layoutXml);
        }
    }

    /// <summary>
    /// Reopens the shell where it was last closed, but only if that rectangle still lands on a
    /// screen — a window restored onto a monitor that has since been unplugged is invisible, which
    /// reads as a failure to launch.
    /// </summary>
    private void RestorePlacement(ScriptWorkspaceStateDto state)
    {
        if (state is { WindowLeft: { } left, WindowTop: { } top, WindowWidth: > 0, WindowHeight: > 0 })
        {
            var restored = new Rect(left, top, state.WindowWidth!.Value, state.WindowHeight!.Value);
            var screens = new Rect(
                SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (restored.IntersectsWith(screens))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = restored.Left;
                Top = restored.Top;
                Width = restored.Width;
                Height = restored.Height;
            }
        }

        if (string.Equals(state.WindowState, nameof(System.Windows.WindowState.Maximized), StringComparison.Ordinal))
        {
            WindowState = System.Windows.WindowState.Maximized;
        }
    }

    private void TryRestoreLayout(string layoutXml)
    {
        try
        {
            IReadOnlyList<PaneDescriptor> known = PaneCatalog.For(this);
            foreach (LayoutAnchorable anchorable in DockManager.Layout.Descendents().OfType<LayoutAnchorable>().ToList())
            {
                anchorable.Content = null; // detach so the restored layout can re-own the controls
            }

            foreach (DocumentEntry entry in _documents)
            {
                entry.Document.Content = null;
            }

            var serializer = new XmlLayoutSerializer(DockManager);
            serializer.LayoutSerializationCallback += (_, e) =>
            {
                if (known.FirstOrDefault(p => string.Equals(p.ContentId, e.Model.ContentId, StringComparison.Ordinal))
                    is { } pane)
                {
                    e.Content = pane.Content;
                    e.Model.Title = pane.Title; // the catalog owns the caption, not the saved layout
                }
                else if (e.Model is LayoutDocument { ContentId: { Length: > 0 } path } document)
                {
                    DocumentEntry? entry = _documents.FirstOrDefault(d =>
                        string.Equals(d.Model.FilePath, path, StringComparison.OrdinalIgnoreCase));
                    if (entry is not null)
                    {
                        entry.Rebind(document,
                            () => CanCloseDocument(entry),
                            () => _documents.Remove(entry),
                            out ScriptEditorControl editor);
                        e.Content = editor;
                    }
                    else
                    {
                        e.Cancel = true;
                    }
                }
                else
                {
                    e.Cancel = true;
                }
            };

            using var reader = new StringReader(layoutXml);
            serializer.Deserialize(reader);

            // Any restored document not present in the layout would be orphaned; re-add it.
            LayoutDocumentPane? pane = GetDocumentPane();
            if (pane is not null)
            {
                foreach (DocumentEntry entry in _documents.Where(d => d.Document.Content is null).ToList())
                {
                    entry.Document.Content = entry.Editor;
                    if (entry.Document.Parent is null)
                    {
                        pane.Children.Add(entry.Document);
                    }
                }
            }

            // A layout saved by an older build may predate a pane (e.g. Call Stack) — put any
            // missing known pane back so upgrades never lose tool windows. (A pane the user merely
            // hid is still present in the layout, so a deliberate hide is respected.)
            foreach (PaneDescriptor descriptor in known)
            {
                EnsureKnownPane(descriptor);
            }
        }
        catch (Exception ex)
        {
            // A stale or corrupt layout must never break the window; fall back to the default layout.
            AppendConsole($"(Could not restore the window layout: {ex.Message})");
            ReattachDetachedContent();
        }
    }

    /// <summary>
    /// Puts the pane and document controls back after a failed restore. They are detached above so
    /// that the saved layout can re-own them, and nothing else re-attaches them — so a layout file
    /// that fails to deserialize left every tool pane and every script tab as an empty frame, and
    /// the same structure was saved again on exit, which made it permanent.
    /// </summary>
    private void ReattachDetachedContent()
    {
        foreach (PaneDescriptor descriptor in PaneCatalog.For(this))
        {
            if (FindPane(descriptor.ContentId) is { } pane)
            {
                pane.Content ??= descriptor.Content;
            }
            else
            {
                EnsureKnownPane(descriptor);
            }
        }

        foreach (DocumentEntry entry in _documents)
        {
            entry.Document.Content ??= entry.Editor;
        }
    }

    /// <summary>Reshows a tool pane by ContentId: a hidden pane is shown where it last lived, and a
    /// pane missing from the layout entirely is recreated in its default place.</summary>
    private void ShowPane(string contentId)
    {
        LayoutAnchorable? pane = FindPane(contentId);
        if (pane is null && PaneCatalog.For(this)
                .FirstOrDefault(p => string.Equals(p.ContentId, contentId, StringComparison.Ordinal)) is { } descriptor)
        {
            EnsureKnownPane(descriptor);
            pane = FindPane(contentId);
        }

        if (pane is not null)
        {
            pane.Show();
            pane.IsActive = true;
        }
    }

    /// <summary>Finds a pane whether it is docked, floating, or hidden.</summary>
    private LayoutAnchorable? FindPane(string contentId) =>
        DockManager.Layout.Descendents().OfType<LayoutAnchorable>()
            .Concat(DockManager.Layout.Hidden)
            .FirstOrDefault(a => string.Equals(a.ContentId, contentId, StringComparison.Ordinal));

    /// <summary>
    /// Recreates a pane a saved layout does not mention — one added by an upgrade, or one written out
    /// before it existed — docking it on the side it belongs on rather than wherever happens to be
    /// last in the tree.
    /// </summary>
    private void EnsureKnownPane(PaneDescriptor descriptor)
    {
        // Hidden panes count as present — a deliberate hide must not spawn a duplicate.
        if (FindPane(descriptor.ContentId) is not null)
        {
            return;
        }

        var anchorable = new LayoutAnchorable
        {
            ContentId = descriptor.ContentId,
            Title = descriptor.Title,
            Content = descriptor.Content,
            CanClose = false,
        };
        anchorable.AddToLayout(DockManager, descriptor.DefaultSide);
    }

    /// <inheritdoc />
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Every dirty document gets its say before anything shuts down; one Cancel keeps the whole
        // app open. Script-driven exit() bypasses this by pre-approving shutdown — a script that
        // says exit means it, and a batch run must never block on a dialog.
        if (!_shutdownApproved)
        {
            foreach (DocumentEntry entry in _documents.Where(static d => d.Model.IsDirty).ToList())
            {
                if (!ConfirmDiscardOrSave(entry))
                {
                    e.Cancel = true;
                    return;
                }
            }

            _shutdownApproved = true; // the per-tab Closing handlers must not re-prompt during teardown
        }

        // Placement must be read while the window still exists: RestoreBounds is only valid for a
        // live window, and by OnClosed the native window is gone.
        _closingPlacement = (RestoreBounds, WindowState == System.Windows.WindowState.Maximized);
        base.OnClosing(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();

        try
        {
            SaveSession();
        }
        catch (Exception ex)
        {
            // Best-effort persistence, like the service beneath it: losing the layout is a nuisance,
            // but throwing here happens during shutdown, where it takes the whole process down.
            System.Diagnostics.Debug.WriteLine("Could not save the workspace session: " + ex);
        }

        // Sessions own a Python child process, so this is what stops it being orphaned.
        DisposeSessions();
        _workspace?.Dispose();
        _logFile?.Dispose();
        base.OnClosed(e);
    }

    private void SaveSession()
    {
        foreach (DocumentEntry entry in _documents)
        {
            RememberBreakpoints(entry);
        }

        (Rect bounds, bool maximized) = _closingPlacement ?? (RestoreBounds, false);
        var state = new ScriptWorkspaceStateDto
        {
            RootPath = _workspace?.RootPath,
            OpenFiles = _documents
                .Where(d => d.Model.FilePath is not null)
                .Select(d => d.Model.FilePath!)
                .ToList(),
            ActiveFile = ActiveDocument?.Model.FilePath,
            Breakpoints = new Dictionary<string, List<int>>(_persistedBreakpoints),
            DockLayoutXml = SerializeLayout(),
            LayoutSchema = ScriptWorkspaceStateFormat.CurrentLayoutSchema,
            ConsoleLanguage = _consoleLanguage,

            // RestoreBounds, not Left/Top/Width/Height: those report the maximized frame, so a window
            // closed maximized would reopen full-screen-sized but un-maximized on the next un-maximize.
            WindowLeft = bounds.Left,
            WindowTop = bounds.Top,
            WindowWidth = bounds.Width,
            WindowHeight = bounds.Height,
            WindowState = maximized
                ? nameof(System.Windows.WindowState.Maximized)
                : nameof(System.Windows.WindowState.Normal),
        };
        _stateService.Save(state);
    }

    private string? SerializeLayout()
    {
        try
        {
            var serializer = new XmlLayoutSerializer(DockManager);
            using var writer = new StringWriter();
            serializer.Serialize(writer);
            return writer.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
