using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using JGraph.Application.Services;
using JGraph.Controls.Scripting;
using JGraph.Core.Model;
using JGraph.Data;
using JGraph.Data.Import;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;
using JGraph.Scripting.Workspace;
using JGraph.Serialization.Workspace;
using Microsoft.Win32;

namespace JGraph.Application.Scripting;

/// <summary>
/// The MATLAB-style scripting workspace window: a docking layout with a workspace file tree,
/// multi-tab script editors (language by file extension), an output console pane, and a variables
/// pane showing what the last run defined. Scripts resolve bare file names through the open
/// workspace (script's folder, then the workspace root). Window state — last workspace, open files,
/// and the dock layout — persists between sessions.
/// </summary>
public partial class ScriptWorkspaceWindow : Window
{
    private readonly IReadOnlyDictionary<string, IScriptEngine> _engines;
    private readonly IWorkspaceStateService _stateService;
    private readonly IFigureWindowService _figureWindows;
    private readonly ScriptSessionModel _session;
    private readonly List<DocumentEntry> _documents = new();
    private readonly Dictionary<string, List<int>> _persistedBreakpoints = new();
    private readonly Dictionary<string, (DateTime WrittenUtc, IReadOnlyList<JGraph.Scripting.Completion.CompletionItem> Items)> _symbolCache =
        new(StringComparer.OrdinalIgnoreCase);
    private ScriptWorkspace? _workspace;
    private System.Threading.CancellationTokenSource? _cts;
    private JgsDebugSession? _debugSession;
    private bool _restartRequested;

    /// <summary>Creates the window over the available engines and persisted state.</summary>
    /// <param name="engines">The script engines to offer, keyed by language.</param>
    /// <param name="stateService">Loads/saves the workspace state between sessions.</param>
    /// <param name="figureWindows">Opens/reuses a numbered figure window for each figure a script shows.</param>
    public ScriptWorkspaceWindow(
        IReadOnlyList<IScriptEngine> engines,
        IWorkspaceStateService stateService,
        IFigureWindowService figureWindows)
    {
        InitializeComponent();

        _engines = engines.ToDictionary(e => e.Language);
        _stateService = stateService;
        _figureWindows = figureWindows;
        _session = new ScriptSessionModel(engines.Where(e => e.IsAvailable).Select(e => e.Language));
        _session.StateChanged += (_, _) => Dispatcher.Invoke(UpdateCommandStates);
        DockManager.ActiveContentChanged += (_, _) => UpdateCommandStates(); // Run reflects the active tab

        try
        {
            RestoreState();
        }
        catch (Exception ex)
        {
            // Restoring the previous session is a convenience — never let it break the window.
            SetStatus($"Could not restore the previous session: {ex.Message}");
        }

        if (_documents.Count == 0)
        {
            OpenNewScript();
        }

        UpdateCommandStates();
    }

    // --- Workspace ------------------------------------------------------------------------------

    private void OnOpenWorkspaceClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Open workspace folder" };
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

        switch (Path.GetExtension(entry.FullPath).ToLowerInvariant())
        {
            case ".csv" or ".tsv" or ".xlsx":
                OpenDataFile(entry.FullPath);
                break;
            case ".graph":
                OpenGraphFile(entry.FullPath);
                break;
            case ".jgs" or ".csx" or ".cs" or ".py" or ".txt" or ".md" or ".json":
                OpenDocument(entry.FullPath);
                break;
            case var extension:
                SetStatus($"No viewer for '{extension}' files.");
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

    // --- Documents ------------------------------------------------------------------------------

    private void OnNewScriptClick(object sender, RoutedEventArgs e) => OpenNewScript();

    private void OpenNewScript()
    {
        var model = new ScriptDocumentModel(path: null, Templates["JGS"]);
        AddDocument(model, activate: true);
    }

    private void OnOpenFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open script",
            Filter = "Scripts (*.jgs;*.csx;*.cs;*.py)|*.jgs;*.csx;*.cs;*.py|All files (*.*)|*.*",
            InitialDirectory = _workspace?.RootPath,
        };
        if (dialog.ShowDialog(this) == true)
        {
            OpenDocument(dialog.FileName);
        }
    }

    private void OpenDocument(string path)
    {
        DocumentEntry? existing = _documents.FirstOrDefault(
            d => string.Equals(d.Model.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Document.IsActive = true;
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not open '{path}': {ex.Message}");
            return;
        }

        AddDocument(new ScriptDocumentModel(path, text), activate: true);
    }

    private DocumentEntry AddDocument(ScriptDocumentModel model, bool activate)
    {
        var editor = new ScriptEditorControl { ScriptLanguage = model.Language, ScriptText = model.Text };
        var document = new LayoutDocument
        {
            Title = model.FileName,
            ContentId = model.FilePath,
            Content = editor,
        };

        var entry = new DocumentEntry(document, editor, model);
        editor.TextChanged += (_, _) =>
        {
            model.SetText(editor.ScriptText);
            entry.Document.Title = model.FileName + (model.IsDirty ? " *" : string.Empty);
        };
        document.Closed += (_, _) =>
        {
            RememberBreakpoints(entry);
            _documents.Remove(entry);
        };

        // Restore this file's persisted breakpoints, and keep the debugger + persistence in sync
        // whenever the user toggles one.
        if (model.FilePath is not null
            && _persistedBreakpoints.TryGetValue(model.FilePath, out List<int>? persisted))
        {
            editor.SetBreakpoints(persisted);
        }

        editor.BreakpointsChanged += (_, _) =>
        {
            RememberBreakpoints(entry);
            _debugSession?.SetBreakpoints(SourceIdOf(entry), entry.Editor.Breakpoints);
        };

        editor.SetNextStatementRequested += (_, line) => RequestSetNextStatement(entry, line);
        editor.CompletionWorkspaceSymbols = () => HarvestWorkspaceSymbols(entry);
        editor.CompletionWorkspaceFiles = () => _workspace is null
            ? Array.Empty<JGraph.Scripting.Completion.WorkspaceFileEntry>()
            : JGraph.Scripting.Completion.PathCompletion.Flatten(_workspace.EnumerateAll());

        // A document opened while a debug run is active (e.g. a run()-included file the debugger just
        // paused in) is executing exactly what is on disk — that text is its live-edit baseline.
        if (_debugSession is not null)
        {
            entry.DebugBaseline = model.Text;
        }

        _documents.Add(entry);
        GetDocumentPane()?.Children.Add(document);
        if (activate)
        {
            document.IsActive = true;
            editor.FocusEditor();
        }

        return entry;
    }

    private void RememberBreakpoints(DocumentEntry entry)
    {
        if (entry.Model.FilePath is not string path)
        {
            return; // unsaved documents keep breakpoints only for the current session
        }

        if (entry.Editor.Breakpoints.Count == 0)
        {
            _persistedBreakpoints.Remove(path);
        }
        else
        {
            _persistedBreakpoints[path] = entry.Editor.Breakpoints.OrderBy(static l => l).ToList();
        }
    }

    private LayoutDocumentPane? GetDocumentPane() =>
        DockManager.Layout.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault();

    private DocumentEntry? ActiveDocument =>
        _documents.FirstOrDefault(d => d.Document.IsActive) ?? _documents.FirstOrDefault();

    // --- Save -----------------------------------------------------------------------------------

    private void OnSaveClick(object sender, RoutedEventArgs e) => SaveActive();

    private void SaveActive()
    {
        DocumentEntry? entry = ActiveDocument;
        if (entry is null)
        {
            return;
        }

        if (entry.Model.FilePath is null)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save script",
                Filter = "JGS script (*.jgs)|*.jgs|C# script (*.csx)|*.csx|Python script (*.py)|*.py|All files (*.*)|*.*",
                InitialDirectory = _workspace?.RootPath,
                FileName = "script.jgs",
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            entry.Model.SetFilePath(dialog.FileName);
            entry.Editor.ScriptLanguage = entry.Model.Language;
            entry.Document.ContentId = dialog.FileName;
        }

        try
        {
            File.WriteAllText(entry.Model.FilePath!, entry.Editor.ScriptText);
            entry.Model.SetText(entry.Editor.ScriptText);
            entry.Model.MarkSaved();
            entry.Document.Title = entry.Model.FileName;
            SetStatus($"Saved {entry.Model.FilePath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Could not save: {ex.Message}");
        }
    }

    // --- Run / Stop -----------------------------------------------------------------------------

    private void OnRunClick(object sender, RoutedEventArgs e) => RunOrContinue();

    private void RunOrContinue()
    {
        if (_session.State == ScriptSessionState.Paused)
        {
            if (TryApplyPendingEdits())
            {
                _debugSession?.Continue();
            }
        }
        else if (_session.State == ScriptSessionState.Idle)
        {
            _ = RunActiveAsync();
        }
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async System.Threading.Tasks.Task RunActiveAsync(DocumentEntry? restartOf = null)
    {
        DocumentEntry? entry = restartOf ?? ActiveDocument;
        if (entry is null)
        {
            return;
        }

        string language = entry.Model.Language;
        if (!_engines.TryGetValue(language, out IScriptEngine? engine) || !engine.IsAvailable)
        {
            SetStatus(language switch
            {
                "Python" when _engines.TryGetValue("Python", out IScriptEngine? py) && !py.IsAvailable
                    => PythonScriptEngine.UnavailableMessage,
                "Text" => $"'{entry.Model.FileName}' is not a runnable script.",
                _ => $"No engine available for {language}.",
            });
            return;
        }

        if (!_session.TryBeginRun(language))
        {
            return;
        }

        AppendConsole($"--- Running {language} script ---");
        SetStatus($"Running {entry.Model.FileName}…");

        string? scriptDirectory = entry.Model.FilePath is null ? null : Path.GetDirectoryName(entry.Model.FilePath);
        ScriptWorkspace? workspace = _workspace;
        Func<string, string>? resolver = workspace is null
            ? null
            : path => workspace.Resolve(path, scriptDirectory);
        var context = new ScriptContext(
            new ConsoleOutput(this), ShowFigureOnUi, scriptDirectory ?? workspace?.RootPath, resolver,
            new AppScriptFigureFiles());

        _cts = new System.Threading.CancellationTokenSource();
        ScriptRunResult result;
        try
        {
            // JGS runs under a debug session (breakpoints, pause, stepping); the hosted engines run plain.
            result = engine is JgsScriptEngine jgs
                ? await RunJgsDebugAsync(jgs, entry, context, _cts.Token)
                : await engine.RunAsync(entry.Editor.ScriptText, context, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            result = ScriptRunResult.Failed("Script run was cancelled.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _debugSession = null;
            ClearExecutionMarkers();
            _session.EndRun();
        }

        VariablesList.ItemsSource = result.Variables;
        CallStackList.ItemsSource = null;
        AppendConsole(result.Success
            ? $"--- Done. {result.FiguresShown} figure(s) displayed. ---"
            : $"--- Failed: {result.Message} ---");
        SetStatus(result.Success
            ? $"Done — {result.FiguresShown} figure(s), {result.Variables.Count} variable(s)."
            : $"Failed: {result.Message}");

        if (_restartRequested)
        {
            // An incompatible live edit chose "restart": rerun the same script with the new code.
            _restartRequested = false;
            AppendConsole("--- Restarting with the edited code ---");
            _ = RunActiveAsync(entry);
        }
    }

    // --- Debugging (JGS) --------------------------------------------------------------------------

    private Task<ScriptRunResult> RunJgsDebugAsync(
        JgsScriptEngine engine, DocumentEntry entry, ScriptContext context, System.Threading.CancellationToken token)
    {
        JgsDebugSession session = engine.CreateDebugSession();
        _debugSession = session;

        // Arm every known breakpoint: the open documents' live sets plus persisted ones for files
        // that are not open (a run()-included script keeps its breakpoints without a tab).
        foreach ((string file, List<int> lines) in _persistedBreakpoints)
        {
            session.SetBreakpoints(file, lines);
        }

        foreach (DocumentEntry document in _documents)
        {
            if (document.Editor.Breakpoints.Count > 0 || document.Model.FilePath is not null)
            {
                session.SetBreakpoints(SourceIdOf(document), document.Editor.Breakpoints);
            }
        }

        session.Paused += OnDebugPaused;
        session.Resumed += OnDebugResumed;

        // Live-edit baselines: what each open document's text is as the run starts. A document whose
        // text later drifts from its baseline has pending edits to apply at the next resume.
        foreach (DocumentEntry document in _documents)
        {
            document.DebugBaseline = document.Editor.ScriptText;
        }

        return session.RunAsync(SourceIdOf(entry), entry.Editor.ScriptText, context, token);
    }

    private static string SourceIdOf(DocumentEntry entry) => entry.Model.FilePath ?? "";

    /// <summary>
    /// Completion symbols for one document from the rest of the workspace: the <c>fn</c>s defined in every
    /// other JGS script. Open documents contribute their live buffer (an unsaved <c>fn</c> completes
    /// immediately); the remaining workspace <c>.jgs</c> files are read from disk through a last-write-time
    /// cache, so the provider stays cheap enough to run on every completion request.
    /// </summary>
    private IReadOnlyList<JGraph.Scripting.Completion.CompletionItem> HarvestWorkspaceSymbols(DocumentEntry current)
    {
        var items = new List<JGraph.Scripting.Completion.CompletionItem>();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (current.Model.FilePath is string ownPath)
        {
            covered.Add(ownPath);
        }

        foreach (DocumentEntry document in _documents)
        {
            if (document == current || document.Model.Language != "JGS")
            {
                continue;
            }

            if (document.Model.FilePath is string path && !covered.Add(path))
            {
                continue;
            }

            items.AddRange(JGraph.Scripting.Jgs.Completion.JgsCompletionEngine.HarvestFunctions(
                document.Editor.ScriptText, document.Model.FileName));
        }

        if (_workspace is null)
        {
            return items;
        }

        foreach (WorkspaceEntry script in _workspace.EnumerateScripts())
        {
            if (!script.FullPath.EndsWith(".jgs", StringComparison.OrdinalIgnoreCase) || !covered.Add(script.FullPath))
            {
                continue;
            }

            try
            {
                DateTime written = File.GetLastWriteTimeUtc(script.FullPath);
                if (!_symbolCache.TryGetValue(script.FullPath, out var cached) || cached.WrittenUtc != written)
                {
                    cached = (written, JGraph.Scripting.Jgs.Completion.JgsCompletionEngine.HarvestFunctions(
                        File.ReadAllText(script.FullPath), script.Name));
                    _symbolCache[script.FullPath] = cached;
                }

                items.AddRange(cached.Items);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable script simply contributes no symbols.
            }
        }

        return items;
    }

    private void OnDebugPaused(object? sender, JgsPausedEventArgs e) =>
        // BeginInvoke, never Invoke: the interpreter thread must reach its gate without waiting on the UI.
        Dispatcher.BeginInvoke(() =>
        {
            JgsDebugSession? session = _debugSession;
            if (session is null)
            {
                return;
            }

            _session.MarkPaused();
            DocumentEntry? entry = FindOrOpenDocument(e.Location.SourceId);
            if (entry is not null)
            {
                entry.Document.IsActive = true;
                entry.Editor.SetCurrentLine(e.Location.Line);
            }

            CallStackList.ItemsSource = e.CallStack;
            try
            {
                VariablesList.ItemsSource = session.GetVariables();
            }
            catch (InvalidOperationException)
            {
                // The run finished between the pause notification and this callback.
            }

            SetStatus($"Paused at line {e.Location.Line} ({e.Reason}).");
        });

    private void OnDebugResumed(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            _session.MarkResumed();
            ClearExecutionMarkers();
            CallStackList.ItemsSource = null;
            if (_session.State == ScriptSessionState.Running)
            {
                SetStatus("Running…");
            }
        });

    private DocumentEntry? FindOrOpenDocument(string sourceId)
    {
        if (sourceId.Length == 0)
        {
            return _documents.FirstOrDefault(d => d.Model.FilePath is null) ?? ActiveDocument;
        }

        DocumentEntry? entry = _documents.FirstOrDefault(d =>
            string.Equals(d.Model.FilePath, sourceId, StringComparison.OrdinalIgnoreCase));
        if (entry is null && File.Exists(sourceId))
        {
            // A run()-included file paused that has no tab yet — open it so the marker has a home.
            OpenDocument(sourceId);
            entry = _documents.FirstOrDefault(d =>
                string.Equals(d.Model.FilePath, sourceId, StringComparison.OrdinalIgnoreCase));
        }

        return entry;
    }

    private void ClearExecutionMarkers()
    {
        foreach (DocumentEntry document in _documents)
        {
            document.Editor.SetCurrentLine(null);
        }
    }

    private void OnPauseClick(object sender, RoutedEventArgs e) => _debugSession?.Pause();

    private void OnStepOverClick(object sender, RoutedEventArgs e) => StepCommand(static s => s.StepOver());

    private void OnStepInClick(object sender, RoutedEventArgs e) => StepCommand(static s => s.StepIn());

    private void OnStepOutClick(object sender, RoutedEventArgs e) => StepCommand(static s => s.StepOut());

    private void StepCommand(Action<JgsDebugSession> step)
    {
        if (_session.CanStep && _debugSession is { } session && TryApplyPendingEdits())
        {
            step(session);
        }
    }

    private void RequestSetNextStatement(DocumentEntry entry, int line)
    {
        if (_session.State != ScriptSessionState.Paused || _debugSession is not { } session)
        {
            return;
        }

        if (!session.TrySetNextStatement(SourceIdOf(entry), line, out string? error))
        {
            SetStatus(error ?? "Could not set the next statement.");
        }
    }

    /// <summary>
    /// Applies any edits made while paused, per document. Compatible edits take effect silently;
    /// an incompatible one asks: restart with the new code, keep debugging the old code, or stay
    /// paused. Returns false when the resume should be cancelled (restart chosen, or stay paused).
    /// </summary>
    private bool TryApplyPendingEdits()
    {
        if (_debugSession is not { IsPaused: true } session)
        {
            return true;
        }

        foreach (DocumentEntry entry in _documents.Where(d => d.Model.Language == "JGS").ToList())
        {
            string text = entry.Editor.ScriptText;
            if (entry.DebugBaseline is null || string.Equals(entry.DebugBaseline, text, StringComparison.Ordinal))
            {
                continue;
            }

            LiveEditResult result;
            try
            {
                result = session.TryApplyEdit(SourceIdOf(entry), text);
            }
            catch (InvalidOperationException)
            {
                return true; // the run ended while we were asking; nothing to apply
            }

            if (result.Applied)
            {
                entry.DebugBaseline = text;
                session.SetBreakpoints(SourceIdOf(entry), entry.Editor.Breakpoints);
                if (result.NewLocation is { } location)
                {
                    FindOrOpenDocument(location.SourceId)?.Editor.SetCurrentLine(location.Line);
                }

                AppendConsole($"(Applied live edit to {entry.Model.FileName}.)");
                continue;
            }

            MessageBoxResult choice = MessageBox.Show(this,
                $"The edit to {entry.Model.FileName} cannot be applied to the paused script: {result.Message}.\n\n" +
                "Yes — stop and restart the run with the new code.\n" +
                "No — keep debugging the old code (the edit applies on the next run).\n" +
                "Cancel — stay paused.",
                "Live edit", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes)
            {
                _restartRequested = true;
                _cts?.Cancel();
                return false;
            }

            if (choice == MessageBoxResult.Cancel)
            {
                return false;
            }

            entry.DebugBaseline = text; // "No": the old code keeps running; stop asking about this edit
        }

        return true;
    }

    // --- Data viewer ------------------------------------------------------------------------------

    private void OnVariablesDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VariablesList.SelectedItem is not ScriptVariable variable)
        {
            return;
        }

        switch (variable.RawValue)
        {
            case Table table:
                ShowInDataViewer(TableGridAdapter.ForTable(table), variable.Name);
                break;
            case double[] array:
                ShowInDataViewer(TableGridAdapter.ForArray(array), variable.Name);
                break;
            default:
                SetStatus($"'{variable.Name}' has no tabular view — only arrays and tables do.");
                break;
        }
    }

    private void OpenDataFile(string path)
    {
        try
        {
            Table table = Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? Table.ReadXlsx(path)
                : Table.ReadCsv(path);
            ShowInDataViewer(TableGridAdapter.ForTable(table), Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImportException)
        {
            SetStatus($"Could not read '{path}': {ex.Message}");
        }
    }

    private void ShowInDataViewer(TableGridAdapter adapter, string name)
    {
        DataViewer.Show(adapter);
        ShowPane("dataviewer");
        SetStatus($"Data Viewer: {name} ({adapter.Title}).");
    }

    private void ShowFigureOnUi(int number, FigureModel figure)
    {
        if (Dispatcher.CheckAccess())
        {
            _figureWindows.ShowScriptFigure(number, figure);
        }
        else
        {
            Dispatcher.Invoke(() => _figureWindows.ShowScriptFigure(number, figure));
        }
    }

    private void UpdateCommandStates()
    {
        // A "Text" tab (or any language without an engine) is viewable but not runnable.
        bool runnable = ActiveDocument is not { } active || _engines.ContainsKey(active.Model.Language);
        RunButton.IsEnabled = runnable && _session.State is ScriptSessionState.Idle or ScriptSessionState.Paused;
        RunButton.Content = _session.State == ScriptSessionState.Paused ? "▶ Continue (F5)" : "▶ Run (F5)";
        StopButton.IsEnabled = _session.CanStop;
        PauseButton.IsEnabled = _session.CanPause && _debugSession is not null;
        StepOverButton.IsEnabled = _session.CanStep;
        StepInButton.IsEnabled = _session.CanStep;
        StepOutButton.IsEnabled = _session.CanStep;
    }

    // --- Console / status -----------------------------------------------------------------------

    private void AppendConsole(string text, bool newline = true)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendConsole(text, newline));
            return;
        }

        ConsoleBox.AppendText(newline ? text + Environment.NewLine : text);
        ConsoleBox.ScrollToEnd();
    }

    private void SetStatus(string text)
    {
        if (Dispatcher.CheckAccess())
        {
            StatusText.Text = text;
        }
        else
        {
            Dispatcher.Invoke(() => StatusText.Text = text);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            _cts?.Cancel();
        }
        else if (e.Key == Key.F5)
        {
            e.Handled = true;
            RunOrContinue();
        }
        else if (e.Key == Key.F9)
        {
            e.Handled = true;
            ActiveDocument?.Editor.ToggleBreakpointAtCaret();
        }
        else if (e.Key == Key.F10)
        {
            e.Handled = true;
            StepCommand(static s => s.StepOver());
        }
        else if (e.Key == Key.F11 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            StepCommand(static s => s.StepOut());
        }
        else if (e.Key == Key.F11)
        {
            e.Handled = true;
            StepCommand(static s => s.StepIn());
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            SaveActive();
        }
    }

    // --- State persistence ----------------------------------------------------------------------

    private void RestoreState()
    {
        ScriptWorkspaceStateDto? state = _stateService.Load();
        if (state is null)
        {
            return;
        }

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

        if (state.DockLayoutXml is { Length: > 0 } layoutXml)
        {
            TryRestoreLayout(layoutXml);
        }
    }

    private void TryRestoreLayout(string layoutXml)
    {
        try
        {
            IReadOnlyDictionary<string, (string Title, object Content)> known = KnownPanes();
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
                if (e.Model.ContentId is string id && known.TryGetValue(id, out (string Title, object Content) pane))
                {
                    e.Content = pane.Content;
                }
                else if (e.Model is LayoutDocument { ContentId: { Length: > 0 } path } document)
                {
                    DocumentEntry? entry = _documents.FirstOrDefault(d =>
                        string.Equals(d.Model.FilePath, path, StringComparison.OrdinalIgnoreCase));
                    if (entry is not null)
                    {
                        entry.Rebind(document, () => _documents.Remove(entry), out ScriptEditorControl editor);
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
            foreach ((string id, (string title, object content)) in known)
            {
                EnsureKnownPane(id, title, content);
            }
        }
        catch (Exception ex)
        {
            // A stale or corrupt layout must never break the window; fall back to the default layout.
            AppendConsole($"(Could not restore the window layout: {ex.Message})");
        }
    }

    /// <summary>The five tool panes by ContentId — the single registry behind layout restore, the
    /// View menu, and pane recreation. A method (not a field) because the XAML-named controls only
    /// exist after InitializeComponent.</summary>
    private IReadOnlyDictionary<string, (string Title, object Content)> KnownPanes() =>
        new Dictionary<string, (string, object)>(StringComparer.Ordinal)
        {
            ["files"] = ("Files", FilesPanel),
            ["console"] = ("Console", ConsoleBox),
            ["variables"] = ("Variables", VariablesList),
            ["callstack"] = ("Call Stack", CallStackList),
            ["dataviewer"] = ("Data Viewer", DataViewer),
        };

    /// <summary>Reshows a tool pane by ContentId: a hidden pane is shown where it last lived, and a
    /// pane missing from the layout entirely is recreated. The View menu's escape hatch.</summary>
    private void ShowPane(string contentId)
    {
        LayoutAnchorable? pane = DockManager.Layout.Descendents().OfType<LayoutAnchorable>()
            .Concat(DockManager.Layout.Hidden)
            .FirstOrDefault(a => string.Equals(a.ContentId, contentId, StringComparison.Ordinal));

        if (pane is null && KnownPanes().TryGetValue(contentId, out (string Title, object Content) entry))
        {
            EnsureKnownPane(contentId, entry.Title, entry.Content);
            pane = DockManager.Layout.Descendents().OfType<LayoutAnchorable>()
                .FirstOrDefault(a => string.Equals(a.ContentId, contentId, StringComparison.Ordinal));
        }

        if (pane is not null)
        {
            pane.Show();
            pane.IsActive = true;
        }
    }

    private void OnViewPaneClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string contentId })
        {
            ShowPane(contentId);
        }
    }

    private void EnsureKnownPane(string contentId, string title, object content)
    {
        // Hidden panes count as present — a deliberate hide must not spawn a duplicate.
        if (DockManager.Layout.Descendents().OfType<LayoutAnchorable>()
            .Concat(DockManager.Layout.Hidden)
            .Any(a => string.Equals(a.ContentId, contentId, StringComparison.Ordinal)))
        {
            return;
        }

        var anchorable = new LayoutAnchorable
        {
            ContentId = contentId,
            Title = title,
            Content = content,
            CanClose = false,
        };
        LayoutAnchorablePane? pane = DockManager.Layout.Descendents().OfType<LayoutAnchorablePane>().LastOrDefault();
        if (pane is not null)
        {
            pane.Children.Add(anchorable);
        }
        else
        {
            anchorable.AddToLayout(DockManager, AnchorableShowStrategy.Right);
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();

        foreach (DocumentEntry entry in _documents)
        {
            RememberBreakpoints(entry);
        }

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
        };
        _stateService.Save(state);

        _workspace?.Dispose();
        base.OnClosed(e);
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

    // --- Templates ------------------------------------------------------------------------------

    internal static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>
    {
        ["C#"] = """
            // JGraph C# script. The JG API is in scope directly: Plot, Title, XLabel, Legend, ...
            // Host helpers: readcsv(path), print(value), show().
            var x = new double[64];
            var y = new double[64];
            for (int i = 0; i < x.Length; i++)
            {
                x[i] = i * 0.1;
                y[i] = Sin(x[i]);
            }

            Plot(x, y, "b-");
            Title("Sine wave");
            XLabel("x");
            YLabel("sin(x)");
            Legend("sin");
            show();
            """,
        ["Python"] = """
            # JGraph Python script. The JG API is available as the JGraph.Api.JG type.
            # Host helpers: readcsv(path), show(); print() writes to the console below.
            import math

            x = [i * 0.1 for i in range(64)]
            y = [math.sin(v) for v in x]

            JG.Plot(x, y, "b-")
            JG.Title("Sine wave")
            JG.XLabel("x")
            JG.YLabel("sin(x)")
            JG.Legend("sin")
            show()
            """,
        ["JGS"] = """
            # JGraph Script (JGS) — a small built-in language.
            # Built-ins mirror the API: plot, title, xlabel, legend, show, plus math like sin/linspace.
            # run("other.jgs") includes another workspace script; readcsv("data.csv") finds workspace files.
            let x = linspace(0, 6.28, 100)
            let y = sin(x)

            plot(x, y, "b-")
            title("Sine wave")
            xlabel("x")
            ylabel("sin(x)")
            legend("sin")
            show()
            """,
    };

    /// <summary>One open document: its dock tab, its editor control, and its UI-free model.</summary>
    private sealed class DocumentEntry
    {
        public DocumentEntry(LayoutDocument document, ScriptEditorControl editor, ScriptDocumentModel model)
        {
            Document = document;
            Editor = editor;
            Model = model;
        }

        public LayoutDocument Document { get; private set; }

        public ScriptEditorControl Editor { get; }

        public ScriptDocumentModel Model { get; }

        /// <summary>The document text the active debug run is executing (or that the last applied live
        /// edit installed) — the reference point for detecting pending edits while paused.</summary>
        public string? DebugBaseline { get; set; }

        /// <summary>Points the entry at the <paramref name="document"/> a layout restore created,
        /// rewiring the close handler the original tab carried.</summary>
        public void Rebind(LayoutDocument document, Action onClosed, out ScriptEditorControl editor)
        {
            Document = document;
            document.Title = Model.FileName;
            document.Closed += (_, _) => onClosed();
            editor = Editor;
        }
    }

    /// <summary>Bridges the engine's <see cref="IScriptOutput"/> onto the window's thread-safe console.</summary>
    private sealed class ConsoleOutput : IScriptOutput
    {
        private readonly ScriptWorkspaceWindow _window;

        public ConsoleOutput(ScriptWorkspaceWindow window) => _window = window;

        public void Write(string text) => _window.AppendConsole(text, newline: false);

        public void WriteLine(string text) => _window.AppendConsole(text);

        public void WriteError(string text) => _window.AppendConsole(text);
    }
}
