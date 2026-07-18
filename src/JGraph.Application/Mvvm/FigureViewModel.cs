using JGraph.Application.Services;
using JGraph.Controls;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Interaction;
using JGraph.Plugins;

namespace JGraph.Application.Mvvm;

/// <summary>
/// The view model for the figure window. It owns the figure model and the high-level view state
/// (active interaction mode, theme, status text) and exposes commands for the toolbar. Imperative
/// navigation is delegated to an <see cref="IFigureNavigator"/> attached by the view.
/// </summary>
public sealed class FigureViewModel : ObservableObject
{
    private FigureModel _figure;
    private IFigureNavigator? _navigator;
    private InteractionModeKind _activeMode = InteractionModeKind.Pointer;
    private ITheme _currentTheme;
    private string _statusText = "Ready";
    private GraphObject? _selectedObject;
    private bool _showPlotBrowser = true;
    private bool _showInspector = true;

    private readonly IFigureExportService _exportService;
    private readonly IFigureDocumentService _documentService;
    private readonly IDataImportService _importService;
    private readonly IScriptingService _scriptingService;

    public FigureViewModel(
        IFigureFactory figureFactory,
        IFigureExportService exportService,
        IFigureDocumentService documentService,
        IDataImportService importService,
        IScriptingService scriptingService,
        PluginRegistry pluginRegistry)
    {
        ArgumentNullException.ThrowIfNull(figureFactory);
        ArgumentNullException.ThrowIfNull(pluginRegistry);
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _scriptingService = scriptingService ?? throw new ArgumentNullException(nameof(scriptingService));
        _figure = figureFactory.CreateSample();

        AvailableThemes = pluginRegistry.Themes;
        _currentTheme = AvailableThemes.Count > 0 ? AvailableThemes[0] : Theme.Light;

        UndoCommand = new RelayCommand(() => _navigator?.Undo(), () => _navigator?.CanUndo ?? false);
        RedoCommand = new RelayCommand(() => _navigator?.Redo(), () => _navigator?.CanRedo ?? false);
        ResetViewCommand = new RelayCommand(() => _navigator?.ResetView());
        PointerModeCommand = new RelayCommand(() => ActiveMode = InteractionModeKind.Pointer);
        PanModeCommand = new RelayCommand(() => ActiveMode = InteractionModeKind.Pan);
        RectangleZoomCommand = new RelayCommand(() => ActiveMode = InteractionModeKind.RectangleZoom);
        DataTipsCommand = new RelayCommand(() => ActiveMode = InteractionModeKind.DataTips);
        EditModeCommand = new RelayCommand(() => ActiveMode = InteractionModeKind.Edit);
        ExportCommand = new RelayCommand(ExportFigure, () => _navigator is not null);
        CopyImageCommand = new RelayCommand(CopyFigureImage, () => _navigator is not null);
        OpenCommand = new RelayCommand(OpenDocument);
        SaveCommand = new RelayCommand(SaveDocument);
        ImportDataCommand = new RelayCommand(ImportData);
        OpenScriptCommand = new RelayCommand(OpenScript);
        CopyFigureCommand = new RelayCommand(CopyFigureObject);
        PasteFigureCommand = new RelayCommand(PasteFigureObject);

        ApplyTheme();
    }

    /// <summary>The figure being displayed and edited.</summary>
    public FigureModel Figure
    {
        get => _figure;
        set => SetProperty(ref _figure, value);
    }

    /// <summary>The active interaction mode; changing it updates the mode toggle states.</summary>
    public InteractionModeKind ActiveMode
    {
        get => _activeMode;
        set
        {
            if (SetProperty(ref _activeMode, value))
            {
                OnPropertyChanged(nameof(IsPointerMode));
                OnPropertyChanged(nameof(IsPanMode));
                OnPropertyChanged(nameof(IsRectangleZoomMode));
                OnPropertyChanged(nameof(IsDataTipsMode));
                OnPropertyChanged(nameof(IsEditMode));
            }
        }
    }

    public bool IsPointerMode
    {
        get => _activeMode == InteractionModeKind.Pointer;
        set
        {
            if (value)
            {
                ActiveMode = InteractionModeKind.Pointer;
            }
        }
    }

    public bool IsPanMode
    {
        get => _activeMode == InteractionModeKind.Pan;
        set
        {
            if (value)
            {
                ActiveMode = InteractionModeKind.Pan;
            }
        }
    }

    public bool IsRectangleZoomMode
    {
        get => _activeMode == InteractionModeKind.RectangleZoom;
        set
        {
            if (value)
            {
                ActiveMode = InteractionModeKind.RectangleZoom;
            }
        }
    }

    public bool IsDataTipsMode
    {
        get => _activeMode == InteractionModeKind.DataTips;
        set
        {
            if (value)
            {
                ActiveMode = InteractionModeKind.DataTips;
            }
        }
    }

    public bool IsEditMode
    {
        get => _activeMode == InteractionModeKind.Edit;
        set
        {
            if (value)
            {
                ActiveMode = InteractionModeKind.Edit;
            }
        }
    }

    /// <summary>The object selected for editing (shown in the inspector), or null.</summary>
    public GraphObject? SelectedObject
    {
        get => _selectedObject;
        set => SetProperty(ref _selectedObject, value);
    }

    /// <summary>Whether the plot browser panel is shown.</summary>
    public bool ShowPlotBrowser
    {
        get => _showPlotBrowser;
        set => SetProperty(ref _showPlotBrowser, value);
    }

    /// <summary>Whether the property inspector panel is shown.</summary>
    public bool ShowInspector
    {
        get => _showInspector;
        set => SetProperty(ref _showInspector, value);
    }

    /// <summary>The themes offered in the theme selector, supplied by the plugin registry.</summary>
    public IReadOnlyList<ITheme> AvailableThemes { get; }

    /// <summary>The active theme; setting it restyles the current figure.</summary>
    public ITheme CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (value is not null && SetProperty(ref _currentTheme, value))
            {
                ApplyTheme();
            }
        }
    }

    /// <summary>Status-bar text (cursor position, active mode).</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public RelayCommand UndoCommand { get; }

    public RelayCommand RedoCommand { get; }

    public RelayCommand ResetViewCommand { get; }

    public RelayCommand PointerModeCommand { get; }

    public RelayCommand PanModeCommand { get; }

    public RelayCommand RectangleZoomCommand { get; }

    public RelayCommand DataTipsCommand { get; }

    public RelayCommand EditModeCommand { get; }

    public RelayCommand ExportCommand { get; }

    public RelayCommand CopyImageCommand { get; }

    public RelayCommand OpenCommand { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand ImportDataCommand { get; }

    public RelayCommand OpenScriptCommand { get; }

    public RelayCommand CopyFigureCommand { get; }

    public RelayCommand PasteFigureCommand { get; }

    /// <summary>Attaches the navigation surface provided by the view.</summary>
    public void AttachNavigator(IFigureNavigator navigator)
    {
        if (_navigator is not null)
        {
            _navigator.NavigationStateChanged -= OnNavigationStateChanged;
        }

        _navigator = navigator;
        _navigator.NavigationStateChanged += OnNavigationStateChanged;
        RefreshNavigationCommands();
        ExportCommand.RaiseCanExecuteChanged();
        CopyImageCommand.RaiseCanExecuteChanged();
    }

    private void ExportFigure()
    {
        if (_navigator is null)
        {
            return;
        }

        string? path = _exportService.ExportInteractive(_figure, _navigator.ViewportSize, CurrentTheme);
        if (path is not null)
        {
            StatusText = $"Exported to {path}";
        }
    }

    private void CopyFigureImage()
    {
        if (_navigator is null)
        {
            return;
        }

        StatusText = _exportService.CopyImage(_figure, _navigator.ViewportSize, CurrentTheme)
            ? "Figure image copied to the clipboard"
            : "Clipboard is in use by another application — try again";
    }

    private void SaveDocument()
    {
        string? path = _documentService.Save(_figure);
        if (path is not null)
        {
            StatusText = $"Saved to {path}";
        }
    }

    private void OpenDocument()
    {
        FigureModel? opened = _documentService.Open();
        if (opened is not null)
        {
            SwapFigure(opened, "Opened figure");
        }
    }

    private void ImportData()
    {
        FigureModel? result = _importService.Import(_figure);
        if (result is null)
        {
            return;
        }

        if (ReferenceEquals(result, _figure))
        {
            StatusText = "Imported data into the current axes";
        }
        else
        {
            SwapFigure(result, "Imported data into a new figure");
        }
    }

    private void OpenScript() => _scriptingService.OpenEditor();

    /// <summary>Displays a figure handed to this window from outside (a script figure window),
    /// re-applying the current theme.</summary>
    public void DisplayFigure(FigureModel figure, string status) => SwapFigure(figure, status);

    private void CopyFigureObject() =>
        StatusText = _documentService.CopyFigure(_figure)
            ? "Figure copied to the clipboard"
            : "Clipboard is in use by another application — try again";

    private void PasteFigureObject()
    {
        if (_documentService.TryPasteFigure(out FigureModel? pasted) && pasted is not null)
        {
            SwapFigure(pasted, "Pasted figure from the clipboard");
        }
        else
        {
            StatusText = "No JGraph figure on the clipboard";
        }
    }

    /// <summary>Replaces the displayed figure (from open/paste), re-applying the current theme.</summary>
    private void SwapFigure(FigureModel figure, string status)
    {
        CurrentTheme.Apply(figure);
        SelectedObject = null;
        Figure = figure;
        StatusText = status;
    }

    private void OnNavigationStateChanged(object? sender, EventArgs e) => RefreshNavigationCommands();

    private void RefreshNavigationCommands()
    {
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
    }

    private void ApplyTheme() => CurrentTheme.Apply(_figure);
}
