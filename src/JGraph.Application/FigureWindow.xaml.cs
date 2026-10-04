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
    private readonly Services.FigureWindowBinding _binding;

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

        // Built here rather than on load, because a script's placement has to be captured before
        // the window lays out — the figure's size is the size the script asked for only until the
        // control writes the viewport's own back into it.
        _binding = new Services.FigureWindowBinding(this, FigureView);
        _binding.TitleChanged += ApplyTitle;
        _binding.ToolBarVisibilityChanged += shown =>
            FigureToolBar.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        // A figure set to Visible 'off' keeps its window but hides it, and 'on' brings it back (U1).
        _binding.FigureVisibilityChanged += shown =>
        {
            if (shown)
            {
                Show();
            }
            else
            {
                Hide();
            }
        };
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.AttachNavigator(FigureView);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        FigureView.CursorDataPositionChanged += OnCursorMoved;

        // The axes toolbar changes the tool from inside the control, so the view model has to hear
        // about it: the mode buttons and the status line both read from it.
        FigureView.ActiveModeChanged += (_, _) => _viewModel.ActiveMode = FigureView.ActiveMode;

        // The window half of the figure's own properties: where it sits, how it is displayed,
        // whether it can be resized, which pointer it shows, whether the toolbar is there. The
        // binding was made in the constructor; now there is a laid-out window to apply it to.
        _binding.Bind(_viewModel.Figure);
        _binding.OnWindowLoaded();
        ApplyTitle();

        // Every press, release, move and wheel turn over the figure, whatever it landed on —
        // MATLAB's WindowButton… family, and the CurrentPoint and SelectionType a callback reads.
        FigureView.PointerActivity += (_, pointer) =>
        {
            switch (pointer.Action)
            {
                case Controls.PointerAction.Moved:
                    ScriptGraphicsCallbacks.NotifyWindowMotion(
                        pointer.Figure, (pointer.Pixel.X, pointer.Pixel.Y));
                    break;

                default:
                    ScriptGraphicsCallbacks.NotifyWindowButton(
                        pointer.Figure,
                        pointer.Action == Controls.PointerAction.Pressed,
                        pointer.Selection,
                        (pointer.Pixel.X, pointer.Pixel.Y));
                    break;
            }
        };

        FigureView.WheelTurned += (_, wheel) =>
            ScriptGraphicsCallbacks.NotifyScrollWheel(wheel.Figure, wheel.Notches);

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

        // A press on the hovering axes toolbar queues that toolbar's SelectionChangedFcn, the same
        // way a legend row queues its ItemHitFcn (M80).
        FigureView.ToolbarButtonPressed += (_, button) =>
        {
            if (button.Parent is AxesToolbarModel toolbar)
            {
                ScriptGraphicsCallbacks.NotifyToolbarSelection(toolbar, button);
            }
        };

        // Its export button asks the window to do what the Export… button does, because exporting is
        // the window's business and the toolbar is only another way to reach it.
        FigureView.ExportRequested += (_, _) =>
        {
            if (_viewModel.ExportCommand.CanExecute(null))
            {
                _viewModel.ExportCommand.Execute(null);
            }
        };

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

    /// <summary>
    /// The window title MATLAB would give this figure: the number unless <c>NumberTitle</c> is off,
    /// then the <c>Name</c> if it has one. The number itself belongs to the service that opened the
    /// window, so it is kept here rather than asked for again.
    /// </summary>
    internal int FigureNumber { get; set; }

    internal void ApplyTitle()
    {
        FigureModel? figure = _viewModel.Figure;
        // A figure with no number of its own (a uifigure) is registered under a key that is not
        // one, and its title never carries it.
        bool hasNumber = FigureNumber > 0 && !JGraph.Api.JG.IsHiddenNumber(FigureNumber) && figure is not { IntegerHandle: false };
        string number = hasNumber ? $"Figure {FigureNumber}" : "JGraph";
        bool numbered = figure is null || (figure.NumberTitle && hasNumber);
        string name = figure?.Name is { Length: > 0 } given && given != "Figure" ? given : string.Empty;

        Title = (numbered, name.Length > 0) switch
        {
            (true, true) => $"{number}: {name}",
            (true, false) => number,
            (false, true) => name,
            _ => string.Empty,
        };
    }

    /// <summary>Rebinds the window's own properties after the view model was given another figure.</summary>
    internal void RebindFigure()
    {
        _binding.Bind(_viewModel.Figure);
        ComponentLayer.Bind(_viewModel.Figure);
        ApplyTitle();
        ApplyPlainness();
    }

    /// <summary>
    /// A <c>uifigure</c>'s window is plain (app-building plan, section G): no plotting toolbar, no
    /// status bar, no plot browser and no inspector — the drawable area and nothing else, so that
    /// the size a script asked for is the size of what it sees.
    /// </summary>
    private void ApplyPlainness()
    {
        if (_viewModel.Figure is not { IsUiFigure: true })
        {
            FigureStatusBar.Visibility = Visibility.Visible;
            return;
        }

        FigureStatusBar.Visibility = Visibility.Collapsed;
        FigureToolBar.Visibility = Visibility.Collapsed;
        _viewModel.ShowPlotBrowser = false;
        _viewModel.ShowInspector = false;
    }

    /// <summary>Whether this window shows <paramref name="figure"/>.</summary>
    internal bool Shows(FigureModel figure) => ReferenceEquals(_viewModel.Figure, figure);

    /// <summary>Applies a frame of this window's figure's components (U1). UI thread.</summary>
    internal void ApplyComponentFrame(UiFrame frame)
    {
        ComponentLayer.Apply(frame);

        // The canvas draws the frame's panels and places the axes in them (U2), so a new frame is
        // a new picture there too.
        FigureView.RequestRender();
    }

    /// <summary>
    /// A press anywhere in the window first commits an edit field that has the keyboard, so its value
    /// reaches the script before the click does — the order a button's callback reading that field
    /// depends on (U1). A press inside the field itself is the user still editing.
    /// </summary>
    protected override void OnPreviewMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ComponentLayer.FocusedComponent is { Style: UiControlStyle.Edit }
            && e.OriginalSource is DependencyObject hit
            && System.Windows.Input.Keyboard.FocusedElement is DependencyObject focused
            && !IsWithin(hit, focused))
        {
            ComponentLayer.CommitFocusedEdit();
        }

        base.OnPreviewMouseDown(e);
    }

    private static bool IsWithin(DependencyObject node, DependencyObject ancestor)
    {
        for (DependencyObject? walk = node; walk is not null;
             walk = walk is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                 ? System.Windows.Media.VisualTreeHelper.GetParent(walk)
                 : LogicalTreeHelper.GetParent(walk))
        {
            if (ReferenceEquals(walk, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The window's bounds on screen, chrome included, for the figure's OuterPosition.</summary>
    internal Rect2D? OuterBoundsOf(FigureModel figure) => _binding.OuterBounds(figure);

    /// <summary>
    /// A press whose character is still to come: the key arrives before the text it types, and the
    /// character a script is told is the one Windows produced (U1) — with the layout, the dead keys
    /// and the input method all applied — not one guessed from the key.
    /// </summary>
    private (string KeyName, List<string> Modifiers, string Guess)? _pendingPress;

    /// <summary>
    /// Keys reach the script before the control's own shortcuts, and are never marked handled, so
    /// Escape, Delete and Ctrl+Z keep working while a KeyPressFcn also hears them.
    /// </summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        FlushPendingPress();
        System.Windows.Input.Key key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (ProducesNoText(key))
        {
            ReportKey(key, pressed: true, CharacterOf(key));
        }
        else
        {
            _pendingPress = (KeyNameOf(key), HeldModifiers(), CharacterOf(key));
        }
    }

    /// <inheritdoc />
    protected override void OnPreviewTextInput(System.Windows.Input.TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (_pendingPress is { } pending)
        {
            _pendingPress = null;
            string text = e.Text.Length > 0 ? e.Text : e.ControlText.Length > 0 ? e.ControlText : e.SystemText;
            Report(pending.KeyName, pending.Modifiers, pressed: true, text);
        }
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyUp(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        FlushPendingPress();
        System.Windows.Input.Key key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        ReportKey(key, pressed: false, CharacterOf(key));
    }

    /// <summary>A press that typed nothing after all is reported with the character guessed from its key.</summary>
    private void FlushPendingPress()
    {
        if (_pendingPress is { } pending)
        {
            _pendingPress = null;
            Report(pending.KeyName, pending.Modifiers, pressed: true, pending.Guess);
        }
    }

    /// <summary>Keys that never produce a character, reported at once rather than held for one.</summary>
    private static bool ProducesNoText(System.Windows.Input.Key key) =>
        key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Up
            or System.Windows.Input.Key.Down or System.Windows.Input.Key.Home or System.Windows.Input.Key.End
            or System.Windows.Input.Key.PageUp or System.Windows.Input.Key.PageDown or System.Windows.Input.Key.Insert
            or System.Windows.Input.Key.Delete or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift
            or System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt
            or System.Windows.Input.Key.RightAlt or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin
            or System.Windows.Input.Key.CapsLock or System.Windows.Input.Key.NumLock or System.Windows.Input.Key.Scroll
            or System.Windows.Input.Key.Apps or System.Windows.Input.Key.PrintScreen or System.Windows.Input.Key.Pause
            or (>= System.Windows.Input.Key.F1 and <= System.Windows.Input.Key.F24);

    private void ReportKey(System.Windows.Input.Key key, bool pressed, string character) =>
        Report(KeyNameOf(key), HeldModifiers(), pressed, character);

    private void Report(string keyName, List<string> modifiers, bool pressed, string character)
    {
        if (_viewModel.Figure is not { } figure)
        {
            return;
        }

        ScriptGraphicsCallbacks.NotifyKey(figure, pressed, character, keyName, modifiers, ComponentLayer.FocusedComponent);
    }

    private static List<string> HeldModifiers()
    {
        var modifiers = new List<string>(3);
        System.Windows.Input.ModifierKeys held = System.Windows.Input.Keyboard.Modifiers;
        if (held.HasFlag(System.Windows.Input.ModifierKeys.Shift))
        {
            modifiers.Add("shift");
        }

        if (held.HasFlag(System.Windows.Input.ModifierKeys.Control))
        {
            modifiers.Add("control");
        }

        if (held.HasFlag(System.Windows.Input.ModifierKeys.Alt))
        {
            modifiers.Add("alt");
        }

        return modifiers;
    }

    /// <summary>The character a key produces, or empty for one that produces none.</summary>
    private static string CharacterOf(System.Windows.Input.Key key)
    {
        bool shift = System.Windows.Input.Keyboard.Modifiers.HasFlag(
            System.Windows.Input.ModifierKeys.Shift);
        if (key is >= System.Windows.Input.Key.A and <= System.Windows.Input.Key.Z)
        {
            char letter = (char)('a' + (key - System.Windows.Input.Key.A));
            return (shift ? char.ToUpperInvariant(letter) : letter).ToString();
        }

        if (key is >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9 && !shift)
        {
            return ((char)('0' + (key - System.Windows.Input.Key.D0))).ToString();
        }

        if (key is >= System.Windows.Input.Key.NumPad0 and <= System.Windows.Input.Key.NumPad9)
        {
            return ((char)('0' + (key - System.Windows.Input.Key.NumPad0))).ToString();
        }

        return key switch
        {
            System.Windows.Input.Key.Space => " ",
            System.Windows.Input.Key.Return => "\r",
            System.Windows.Input.Key.Tab => "\t",
            _ => string.Empty,
        };
    }

    /// <summary>MATLAB's lowercase name for a key, which is its own spelling of what was pressed.</summary>
    private static string KeyNameOf(System.Windows.Input.Key key) => key switch
    {
        System.Windows.Input.Key.Return => "return",
        System.Windows.Input.Key.Escape => "escape",
        System.Windows.Input.Key.Back => "backspace",
        System.Windows.Input.Key.Delete => "delete",
        System.Windows.Input.Key.Left => "leftarrow",
        System.Windows.Input.Key.Right => "rightarrow",
        System.Windows.Input.Key.Up => "uparrow",
        System.Windows.Input.Key.Down => "downarrow",
        System.Windows.Input.Key.PageUp => "pageup",
        System.Windows.Input.Key.PageDown => "pagedown",
        System.Windows.Input.Key.Home => "home",
        System.Windows.Input.Key.End => "end",
        System.Windows.Input.Key.Space => "space",
        System.Windows.Input.Key.Tab => "tab",
        _ => key.ToString().ToLowerInvariant(),
    };

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FigureViewModel.Figure):
                RebindFigure();
                break;
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
