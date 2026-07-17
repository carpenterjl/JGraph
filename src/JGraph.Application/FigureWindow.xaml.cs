using System.ComponentModel;
using System.Globalization;
using System.Windows;
using JGraph.Application.Mvvm;
using JGraph.Core.Primitives;

namespace JGraph.Application;

/// <summary>
/// The interactive figure window. It hosts the <see cref="Controls.FigureControl"/> and binds it to a
/// <see cref="FigureViewModel"/>: the view model owns state and commands, and this thin code-behind
/// bridges the imperative parts (attaching the navigator, pushing theme/mode to the control, and
/// reporting the cursor position to the status bar).
/// </summary>
public partial class FigureWindow : Window
{
    private readonly FigureViewModel _viewModel;

    /// <summary>The window's view model, for hosts that swap figures in (script figure windows).</summary>
    internal FigureViewModel ViewModel => _viewModel;

    public FigureWindow(FigureViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.AttachNavigator(FigureView);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        FigureView.CursorDataPositionChanged += OnCursorMoved;

        // Share the figure control's selection and undo stack with the side panels, so the edit
        // mode, plot browser, and property inspector all act on the same state.
        Browser.Selection = FigureView.Selection;
        Browser.UndoStack = FigureView.UndoStack;
        Inspector.UndoStack = FigureView.UndoStack;
        FigureView.Selection.SelectionChanged += (_, selected) => _viewModel.SelectedObject = selected;

        FigureView.Theme = _viewModel.CurrentTheme;
        FigureView.ActiveMode = _viewModel.ActiveMode;
        UpdateStatus(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FigureViewModel.ActiveMode):
                FigureView.ActiveMode = _viewModel.ActiveMode;
                UpdateStatus(null);
                break;
            case nameof(FigureViewModel.CurrentTheme):
                FigureView.Theme = _viewModel.CurrentTheme;
                break;
            case nameof(FigureViewModel.SelectedObject):
                UpdateStatus(null);
                break;
        }
    }

    private void OnCursorMoved(object? sender, Point2D? data) => UpdateStatus(data);

    private void UpdateStatus(Point2D? data)
    {
        string position = data is { } p
            ? $"X = {p.X.ToString("G6", CultureInfo.CurrentCulture)}   Y = {p.Y.ToString("G6", CultureInfo.CurrentCulture)}"
            : "—";
        string status = $"Mode: {_viewModel.ActiveMode}    |    {position}";
        if (_viewModel.SelectedObject is { } selected)
        {
            string name = string.IsNullOrEmpty(selected.Name) ? selected.GetType().Name : selected.Name;
            status += $"    |    Selected: {name}";
        }

        _viewModel.StatusText = status;
    }
}
