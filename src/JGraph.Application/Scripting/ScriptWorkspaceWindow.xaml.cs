using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using JGraph.Application.Services;
using JGraph.Scripting;
using JGraph.Scripting.Jgs.Debug;
using JGraph.Scripting.Workspace;

namespace JGraph.Application.Scripting;

/// <summary>
/// The MATLAB-style scripting workspace window: a docking layout with a workspace file tree,
/// multi-tab script editors (language by file extension), an output console pane, and a variables
/// pane showing what the last run defined. Scripts resolve bare file names through the open
/// workspace (script's folder, then the workspace root). Window state — last workspace, open files,
/// and the dock layout — persists between sessions.
/// </summary>
/// <remarks>
/// The implementation is split across partial files by concern, all in this folder:
/// <c>.Files.cs</c> (workspace tree and folder navigation), <c>.Documents.cs</c> (script tabs and
/// saving), <c>.Run.cs</c> (running, startup statements, debugging), <c>.DataViewer.cs</c>
/// (variables drill-in and figures), <c>.Console.cs</c> (the coalesced output console), and
/// <c>.Layout.cs</c> (dock panes and session persistence).
/// </remarks>
public partial class ScriptWorkspaceWindow : Window
{
    private readonly IReadOnlyDictionary<string, IScriptEngine> _engines;
    private readonly IWorkspaceStateService _stateService;
    private readonly IFigureWindowService _figureWindows;
    private readonly ISettingsService? _settings;
    private readonly IOptionsService? _options;
    private readonly ScriptSessionModel _session;
    private readonly AppScriptAudio _audio = new();
    private readonly List<DocumentEntry> _documents = new();
    private readonly Dictionary<string, List<int>> _persistedBreakpoints = new();
    private readonly Dictionary<string, (DateTime WrittenUtc, IReadOnlyList<JGraph.Scripting.Completion.CompletionItem> Items)> _symbolCache =
        new(StringComparer.OrdinalIgnoreCase);
    private ScriptWorkspace? _workspace;
    private System.Threading.CancellationTokenSource? _cts;
    private IScriptOutput? _output;
    private IDisposable? _logFile;
    private JgsDebugSession? _debugSession;
    private bool _restartRequested;
    private bool _sessionRestored;

    /// <summary>Where the window was when it started closing — see <c>OnClosing</c> in the layout part.</summary>
    private (Rect Bounds, bool Maximized)? _closingPlacement;

    // Console output is coalesced: a script (running off the UI thread) can emit output faster than
    // the UI can render it, so writes accumulate in _pendingConsole and are flushed to the TextBox in
    // batches via a single scheduled BeginInvoke. This keeps the interpreter thread from ever blocking
    // on the UI and keeps the UI responsive (and the Stop button clickable) under a print-heavy loop.
    private readonly object _consoleLock = new();
    private readonly StringBuilder _pendingConsole = new();
    private bool _consoleFlushScheduled;
    private long _runOutputLines;   // script-output lines emitted this run (for the runaway-output budget)
    private bool _runOutputTruncated;

    /// <summary>Keep at most this many characters in the console TextBox; older text is trimmed.</summary>
    private const int MaxConsoleChars = 1_000_000;

    /// <summary>Per-run script-output line budget; beyond this, further script lines are dropped.</summary>
    private const long MaxRunOutputLines = 100_000;

    /// <summary>Creates the window over the available engines and persisted state.</summary>
    /// <param name="engines">The script engines to offer, keyed by language.</param>
    /// <param name="stateService">Loads/saves the workspace state between sessions.</param>
    /// <param name="figureWindows">Opens/reuses a numbered figure window for each figure a script shows.</param>
    /// <param name="settings">The user's preferences (default language and script directory), or null.</param>
    /// <param name="options">Opens the Options dialog from the View menu, or null to hide that item.</param>
    public ScriptWorkspaceWindow(
        IReadOnlyList<IScriptEngine> engines,
        IWorkspaceStateService stateService,
        IFigureWindowService figureWindows,
        ISettingsService? settings = null,
        IOptionsService? options = null)
    {
        InitializeComponent();

        _engines = engines.ToDictionary(e => e.Language);
        _stateService = stateService;
        _figureWindows = figureWindows;
        _settings = settings;
        _options = options;
        _session = new ScriptSessionModel(engines.Where(e => e.IsAvailable).Select(e => e.Language));
        _session.StateChanged += (_, _) => Dispatcher.Invoke(UpdateCommandStates);
        DockManager.ActiveContentChanged += (_, _) => UpdateCommandStates(); // Run reflects the active tab
        InitializeDockTheme();
        BindCommands();
        BuildNewScriptMenu(engines);
        BuildConsoleLanguages(engines);
        BuildViewMenu();
        UpdateCommandStates();

        // The previous session is restored by RestoreSession(), not here: construction must stay
        // cheap because the container builds this window, and the restore is what the splash reports.
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

}
