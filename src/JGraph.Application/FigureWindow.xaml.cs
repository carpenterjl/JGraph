using System.ComponentModel;
using System.Globalization;
using System.Windows;
using JGraph.Application.Mvvm;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting;

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

    /// <summary>
    /// True once a close has been decided — by a script (<c>closereq</c>, <c>delete</c>, a
    /// <c>close</c> whose CloseRequestFcn already ran) or by force. Only an unapproved close, which
    /// is to say the title bar's own button, consults the figure's <c>CloseRequestFcn</c>.
    /// </summary>
    internal bool CloseApproved { get; set; }

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

        // Clicking a legend entry queues the script callback the legend was given, if any — MATLAB's
        // ItemHitFcn. The callback runs on the script thread at its next safe point; a figure with
        // no script behind it simply has nothing to queue.
        FigureView.LegendRowClicked += (_, clicked) =>
            ScriptGraphicsCallbacks.NotifyLegendItemHit(clicked.Axes, clicked.Plot);

        // A real viewport resize was already written to the model; the figure's SizeChangedFcn, if
        // any, hears about it once per settled size.
        FigureView.ViewportSizeChanged += (_, _) =>
        {
            if (_viewModel.Figure is { } resized)
            {
                ScriptGraphicsCallbacks.NotifySizeChanged(resized);
            }
        };

        // Every press is reported with what it landed on — MATLAB's ButtonDownFcn, and the state
        // behind gco. The scripting layer sorts out whose callback the click is.
        FigureView.ObjectClicked += (_, clicked) => ScriptGraphicsCallbacks.NotifyButtonDown(
            clicked.Figure,
            clicked.Hit.Target,
            clicked.Hit.Axes,
            clicked.Hit.DataPoint is { } point ? (point.X, point.Y) : null,
            clicked.Button switch
            {
                Interaction.PointerButton.Middle => 2,
                Interaction.PointerButton.Right => 3,
                _ => 1,
            });

        // A right-click on an object that was given a uicontextmenu shows that menu and nothing
        // else — MATLAB's substitution rule. The menu's own opening callback rides the queue.
        FigureView.ScriptContextMenuProvider = (hit, pixel) =>
        {
            GraphObject? target = hit.Target ?? _viewModel.Figure;
            if (target is null || ScriptGraphicsCallbacks.ResolveContextMenu(target) is not { } menu)
            {
                return null;
            }

            ScriptGraphicsCallbacks.NotifyContextMenuOpening(menu, target, (pixel.X, pixel.Y));
            return ScriptMenuItems(menu.Items);
        };

        FigureView.Theme = _viewModel.CurrentTheme;
        FigureView.ActiveMode = _viewModel.ActiveMode;
        UpdateStatus(null);
    }

    /// <summary>A scripted menu's entries as the UI-free items the control renders — separators
    /// above the entries that asked for one, submenus recursed, picks queued for the script.</summary>
    private static List<JGraph.Interaction.ContextMenuItem> ScriptMenuItems(
        IEnumerable<MenuItemModel> items)
    {
        var built = new List<JGraph.Interaction.ContextMenuItem>();
        foreach (MenuItemModel item in items)
        {
            if (!item.Visible)
            {
                continue;
            }

            if (item.Separator && built.Count > 0)
            {
                built.Add(JGraph.Interaction.ContextMenuItem.Separator);
            }

            MenuItemModel picked = item;
            built.Add(new JGraph.Interaction.ContextMenuItem(
                item.Text,
                item.Items.Count > 0 ? null : () => ScriptGraphicsCallbacks.NotifyMenuSelected(picked),
                IsChecked: item.Checked,
                IsEnabled: item.Enable,
                Children: item.Items.Count > 0 ? ScriptMenuItems(item.Items) : null));
        }

        return built;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // The X button, when the figure was given a CloseRequestFcn: the close is cancelled and
        // the request queued for the script thread — the callback closes (closereq, delete) or, by
        // doing neither, keeps the window. The interpreter never runs on this thread. A second
        // click while the first request is still undelivered is taken as insistence and closes
        // outright — the documented escape when a wedged script never drains its queue.
        if (CloseApproved || e.Cancel || _viewModel.Figure is not { } figure
            || !ScriptGraphicsCallbacks.HasCallback(figure, GraphicsEventKind.CloseRequest))
        {
            return;
        }

        if (ScriptEventQueue.IsPending(GraphicsEventKind.CloseRequest, figure))
        {
            return;
        }

        e.Cancel = true;
        ScriptEventQueue.Enqueue(new GraphicsEvent(GraphicsEventKind.CloseRequest, figure));
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
