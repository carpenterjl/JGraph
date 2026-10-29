using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JGraph.Controls.Themes;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// The visual designer's canvas (designer plan DS2): an app's figure drawn by the runtime's own
/// renderer and component layer, in design mode - nothing takes a press but the canvas - with App
/// Designer's design gestures over it (<see cref="DesignGestures"/>, which this turns the mouse and
/// keys into and draws the marks of: the selection and its handles, the guides, the rubber band,
/// the container a drop goes into, the grid, and a hidden component faded). Every edit is the
/// <see cref="Document"/>'s, and undoes there. DS3: a library entry chosen for click and draw
/// (<see cref="DrawEntry"/>) is placed or drawn by the next press, one dragged from the library is
/// added where it is dropped, and Delete deletes the selection. DS4: a grid's cells as App Designer
/// shows them, the cells a drop goes into, a grid child's span being resized, the configure button,
/// a grid being configured with its bands and popup, and the context menu. W4: the figure resized
/// by its edges, the anchor of a selection outlined 2 pixels wide, Visualize Tab Order's outline
/// going from component to component, and a library entry dropped with Ctrl held without its label.
/// </summary>
public sealed class DesignCanvas : UserControl
{
    /// <summary>The space around the figure on the canvas.</summary>
    private const double Gutter = 24;

    private static readonly Brush GuideBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0x71, 0x0A)));
    private static readonly Brush FixedGuideBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x00, 0x76, 0xA8)));
    private static readonly Brush GridBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)));
    private static readonly Brush BandBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x76, 0xA8)));

    // App Designer's grid look (uigridlayout.css, light | dark theme tokens).
    private static readonly Brush CellLight = Frozen(new SolidColorBrush(Color.FromRgb(0xBF, 0xBF, 0xBF)));
    private static readonly Brush CellDark = Frozen(new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61)));
    private static readonly Brush DropLight = Frozen(new SolidColorBrush(Color.FromArgb(0x99, 0xB4, 0xDE, 0xFF)));
    private static readonly Brush DropDark = Frozen(new SolidColorBrush(Color.FromArgb(0x99, 0x0B, 0x2F, 0x5C)));
    private static readonly Brush EditVeil = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xE6, 0xE6, 0xE6)));
    private static readonly Brush BandFill = Frozen(new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush BandHovered = Frozen(new SolidColorBrush(Color.FromArgb(0x59, 0x88, 0x88, 0x88)));
    private static readonly Brush BandSelected = Frozen(new SolidColorBrush(Color.FromArgb(0x59, 0x54, 0xB6, 0xFF)));
    private static readonly Brush BandSelectedEdge = Frozen(new SolidColorBrush(Color.FromRgb(0x10, 0x42, 0x80)));
    private static readonly Brush TabFill = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)));
    private static readonly Brush FrameLine = Frozen(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)));
    private static readonly Brush DeleteHint = Frozen(new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0x45, 0x3A)));
    private static readonly Brush InsertHint = Frozen(new SolidColorBrush(Color.FromArgb(0x59, 0x00, 0x80, 0x13)));
    private static readonly Brush ButtonFill = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)));
    private static readonly Brush ButtonEdge = Frozen(new SolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6)));
    private static readonly Brush GlyphBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)));
    private static readonly Brush SplitterBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x60, 0x60, 0x60)));
    private static readonly Brush SplitterAlertBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0xD3, 0x2F, 0x2F)));

    private readonly Grid _outer;
    private readonly Grid _surface;
    private readonly FigureControl _figure;
    private readonly UiComponentLayer _layer;
    private readonly Overlay _overlay;
    private readonly DesignGridConfigPopup _popup;

    private DesignDocument? _document;
    private DesignGestures? _gestures;
    private bool _showGrid;
    private bool _snapToGrid = true;
    private double _gridInterval = DesignGeometry.GridInterval;
    private bool _showAlignmentHints = true;
    private bool _drawing;
    private readonly DispatcherTimer _tabOrderTimer = new() { Interval = TimeSpan.FromMilliseconds(DesignGestures.VisualizeStep) };
    private IReadOnlyList<DesignComponent> _tabOrder = [];
    private int _tabOrderAt = -1;

    public DesignCanvas()
    {
        Focusable = true;
        _figure = new FigureControl { IsHitTestVisible = false };
        _layer = new UiComponentLayer { IsHitTestVisible = false, DesignMode = true };
        KeyboardNavigation.SetTabNavigation(_layer, KeyboardNavigationMode.None);
        _surface = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new System.Windows.Thickness(Gutter),
        };
        _surface.Children.Add(_figure);
        _surface.Children.Add(_layer);
        _overlay = new Overlay(this) { AllowDrop = true };
        _popup = new DesignGridConfigPopup(_overlay);
        _popup.Problem += (_, problem) => Problem?.Invoke(this, problem);

        // The popup is a window of its own: it goes with the canvas when another app's tab is shown.
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                ShowPopup();
            }
            else
            {
                _popup.Hide();
            }
        };
        _outer = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _outer.Children.Add(_surface);
        _outer.Children.Add(_overlay);
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _outer,
            Focusable = false,
        };
        scroll.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        Content = scroll;
        _tabOrderTimer.Tick += (_, _) => StepTabOrder();
    }

    /// <summary>Raised when the selection, the design or a gesture's state changed, for the window's status and commands.</summary>
    public event EventHandler? DesignChanged;

    /// <summary>Raised when click and draw ended - a component placed, or Escape - so the library lets its entry go.</summary>
    public event EventHandler? DrawEnded;

    /// <summary>Raised with what a grid's configuring refused (a weight past 100, a size that is no number).</summary>
    public event EventHandler<string>? Problem;

    /// <summary>Raised with an alert App Designer shows in a dialog of its own: a title and a message (Add Label's refusal).</summary>
    public event EventHandler<(string Title, string Message)>? Alerted;

    /// <summary>The library entry the next press places or draws (App Designer's click and draw); null when none.</summary>
    public DesignPaletteEntry? DrawEntry
    {
        get => _gestures?.DrawEntry;
        set
        {
            if (_gestures is { } gestures)
            {
                gestures.DrawEntry = value;
            }
        }
    }

    /// <summary>What the last add from the library refused; null when it went.</summary>
    public string? Refusal => _gestures?.Refusal;

    /// <summary>The app on the canvas.</summary>
    public DesignDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(value, _document))
            {
                return;
            }

            if (_document is not null)
            {
                _gestures?.Cancel();
                _gestures?.EndConfigure();
                _document.Changed -= OnChanged;
                _gestures!.Changed -= OnChanged;
                _gestures.TabOrderShown -= OnTabOrderShown;
                _gestures.Alerted -= OnAlerted;
            }

            StopTabOrder();

            _popup.Hide();

            _document = value;
            _gestures = null;
            _figure.Figure = value?.Runtime.Figure;
            _layer.Bind(value?.Runtime.Figure);
            if (value is not null)
            {
                _gestures = new DesignGestures(value)
                {
                    TabHeadingAt = at => _layer.TabHeadingAt(new Point(at.X, at.Y)) is { } tab ? value.Runtime.ComponentOf(tab) : null,
                    DragThreshold = System.Math.Max(SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance),
                    FigureLeftRoom = Gutter - 2,
                };
                ApplyOptions();
                value.Changed += OnChanged;
                _gestures.Changed += OnChanged;
                _gestures.TabOrderShown += OnTabOrderShown;
                _gestures.Alerted += OnAlerted;
                if (value.Selection.Count == 0)
                {
                    value.Select([]);
                }
            }

            Refresh();
        }
    }

    /// <summary>Whether the grid is drawn, which a move and a resize then snap to.</summary>
    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            _showGrid = value;
            ApplyOptions();
        }
    }

    /// <summary>Whether a shown grid is snapped to (App Designer's default).</summary>
    public bool SnapToGrid
    {
        get => _snapToGrid;
        set
        {
            _snapToGrid = value;
            ApplyOptions();
        }
    }

    /// <summary>The grid's interval, within App Designer's 10 to 50 pixels.</summary>
    public double GridInterval
    {
        get => _gridInterval;
        set
        {
            _gridInterval = DesignGeometry.GridIntervalWithin(value);
            ApplyOptions();
        }
    }

    /// <summary>Whether a move or a resize shows alignment guides and snaps to them.</summary>
    public bool ShowAlignmentHints
    {
        get => _showAlignmentHints;
        set
        {
            _showAlignmentHints = value;
            ApplyOptions();
        }
    }

    private void ApplyOptions()
    {
        if (_gestures is { } gestures)
        {
            gestures.ShowGrid = _showGrid;
            gestures.SnapToGrid = _snapToGrid;
            gestures.GridInterval = _gridInterval;
            gestures.ShowAlignmentHints = _showAlignmentHints;
        }

        _overlay.InvalidateVisual();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _gestures))
        {
            if (_drawing && _gestures!.DrawEntry is null)
            {
                DrawEnded?.Invoke(this, EventArgs.Empty);
            }

            _drawing = _gestures!.DrawEntry is not null;
            _overlay.InvalidateVisual();
            ShowPopup();
            DesignChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // An edit from anywhere - undo, the inspector, the menu - shows in the popup.
        _gestures?.Configuring?.Refresh();
        Refresh();
        ShowPopup();
    }

    /// <summary>The configuring popup, shown for a grid's picked row or column, at the pick, while the canvas is.</summary>
    private void ShowPopup()
    {
        if (IsVisible)
        {
            _popup.Show(_gestures, Gutter, Gutter);
        }
        else
        {
            _popup.Hide();
        }
    }

    /// <summary>Draws the runtime's components as they stand, and the canvas's marks over them.</summary>
    private void Refresh()
    {
        if (_document is { } document)
        {
            Size2D size = document.Runtime.Figure.Size;
            _surface.Width = size.Width;
            _surface.Height = size.Height;
            _surface.Margin = new System.Windows.Thickness(Gutter + (_gestures?.FigureShift ?? 0), Gutter, Gutter, Gutter);
            _outer.Width = size.Width + (2 * Gutter);
            _outer.Height = size.Height + (2 * Gutter);
            _layer.Apply(document.Runtime.TakeFrame());
            _figure.RequestRender();
        }

        _overlay.InvalidateVisual();
        DesignChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cancels a gesture under way: its components go back to where they were.</summary>
    public void CancelGesture() => _gestures?.Cancel();

    /// <summary>Shows <paramref name="order"/>'s tab order: each one outlined in turn, at most <paramref name="limit"/> of them (null for all).</summary>
    public void ShowTabOrder(IReadOnlyList<DesignComponent> order, int? limit = null) => _gestures?.ShowTabOrder(order, limit);

    /// <summary>The component Visualize Tab Order outlines now; null when it shows none.</summary>
    public DesignComponent? TabOrderShown => _tabOrderAt >= 0 && _tabOrderAt < _tabOrder.Count ? _tabOrder[_tabOrderAt] : null;

    // App Designer's animClassOnElements: the class on each element for 500 ms, one after another.
    private void OnTabOrderShown(object? sender, (IReadOnlyList<DesignComponent> Order, int? Limit) shown)
    {
        _tabOrder = shown.Limit is { } limit ? shown.Order.Take(limit).ToArray() : shown.Order;
        _tabOrderAt = 0;
        _tabOrderTimer.Stop();
        _tabOrderTimer.Start();
        _overlay.InvalidateVisual();
        DesignChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnAlerted(object? sender, (string Title, string Message) alert) => Alerted?.Invoke(this, alert);

    private void StepTabOrder()
    {
        if (++_tabOrderAt >= _tabOrder.Count)
        {
            StopTabOrder();
        }

        _overlay.InvalidateVisual();
        DesignChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StopTabOrder()
    {
        _tabOrderTimer.Stop();
        _tabOrder = [];
        _tabOrderAt = -1;
        _overlay.InvalidateVisual();
    }

    // --- input -------------------------------------------------------------------------------------

    private Point2D OnSurface(MouseEventArgs e)
    {
        Point at = e.GetPosition(_overlay);
        return new Point2D(at.X - Gutter, at.Y - Gutter);
    }

    private static DesignModifiers Modifiers() =>
        ((Keyboard.Modifiers & ModifierKeys.Control) != 0 ? DesignModifiers.Control : DesignModifiers.None)
        | ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? DesignModifiers.Shift : DesignModifiers.None)
        | ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? DesignModifiers.Alt : DesignModifiers.None);

    private void OnPressed(MouseButtonEventArgs e)
    {
        Focus();
        DesignButton button = e.ChangedButton switch
        {
            MouseButton.Left => DesignButton.Left,
            MouseButton.Right => DesignButton.Right,
            _ => DesignButton.Middle,
        };
        if (_gestures is not { } gestures)
        {
            return;
        }

        Point2D at = OnSurface(e);

        // A double click on a selected grid configures it (DS4).
        if (button == DesignButton.Left && e.ClickCount == 2 && gestures.DoubleClick(at))
        {
            return;
        }

        if (gestures.Press(at, button, Modifiers()))
        {
            _overlay.CaptureMouse();
        }
        else if (button == DesignButton.Right)
        {
            ShowMenu(gestures, at);
        }
    }

    /// <summary>
    /// The context menu for a right click at <paramref name="at"/> (DS4): each item does what
    /// <see cref="DesignGestures.MenuAt"/> says, and shows the row or column it would add or delete
    /// while it is pointed at.
    /// </summary>
    private void ShowMenu(DesignGestures gestures, Point2D at)
    {
        IReadOnlyList<DesignMenuItem> items = gestures.MenuAt(at);
        if (items.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = _overlay };
        foreach (DesignMenuItem item in items)
        {
            menu.Items.Add(MenuEntry(gestures, item));
        }

        menu.Closed += (_, _) => gestures.ShowHint(null);
        menu.IsOpen = true;
    }

    private object MenuEntry(DesignGestures gestures, DesignMenuItem item)
    {
        if (item.IsSeparator)
        {
            return new Separator();
        }

        var entry = new MenuItem { Header = item.Label, IsEnabled = item.Enabled };
        System.Windows.Automation.AutomationProperties.SetAutomationId(entry, "Menu." + item.Label.Replace(" ", string.Empty, StringComparison.Ordinal));
        if (item.Items is { } children)
        {
            foreach (DesignMenuItem child in children)
            {
                entry.Items.Add(MenuEntry(gestures, child));
            }
        }
        else if (item.Run is { } run)
        {
            entry.Click += (_, _) =>
            {
                gestures.ShowHint(null);
                try
                {
                    run();
                }
                catch (InvalidOperationException ex)
                {
                    // What App Designer's own code would fail on (a gridify it cannot work out) is said, not thrown.
                    Problem?.Invoke(this, ex.Message);
                }
            };
        }

        if (item.Hint is { } hint)
        {
            entry.MouseEnter += (_, _) => gestures.ShowHint(hint);
            entry.MouseLeave += (_, _) => gestures.ShowHint(null);
        }

        return entry;
    }

    private void OnMoved(MouseEventArgs e)
    {
        if (_gestures is not { } gestures)
        {
            return;
        }

        Point2D at = OnSurface(e);
        if (gestures.IsPressed)
        {
            gestures.Drag(at, Modifiers());
            return;
        }

        gestures.Configuring?.Hover(at);

        _overlay.Cursor = gestures.CursorAt(at) switch
        {
            DesignCursor.Move => Cursors.SizeAll,
            DesignCursor.SizeNorthSouth => Cursors.SizeNS,
            DesignCursor.SizeWestEast => Cursors.SizeWE,
            DesignCursor.SizeNorthWestSouthEast => Cursors.SizeNWSE,
            DesignCursor.SizeNorthEastSouthWest => Cursors.SizeNESW,
            DesignCursor.Cross => Cursors.Cross,
            _ => Cursors.Arrow,
        };
    }

    private void OnReleased()
    {
        if (_gestures is { IsPressed: true } gestures)
        {
            gestures.Release();
            _overlay.ReleaseMouseCapture();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        DesignKey? key = e.Key switch
        {
            Key.Left => DesignKey.Left,
            Key.Right => DesignKey.Right,
            Key.Up => DesignKey.Up,
            Key.Down => DesignKey.Down,
            Key.Escape => DesignKey.Escape,
            Key.A when (Keyboard.Modifiers & ModifierKeys.Control) != 0 => DesignKey.SelectAll,
            Key.Delete or Key.Back => DesignKey.Delete,
            Key.G when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) => DesignKey.Ungroup,
            Key.G when Keyboard.Modifiers == ModifierKeys.Control => DesignKey.Group,
            _ => null,
        };
        if (e.Key == Key.Escape && _tabOrderAt >= 0)
        {
            StopTabOrder();
        }

        if (!e.Handled && key is { } pressed && _gestures?.KeyDown(pressed, Modifiers()) == true)
        {
            if (pressed == DesignKey.Escape)
            {
                _overlay.ReleaseMouseCapture();
            }

            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            _gestures?.ArrowUp(Keyboard.IsKeyDown(Key.Left) || Keyboard.IsKeyDown(Key.Right) || Keyboard.IsKeyDown(Key.Up) || Keyboard.IsKeyDown(Key.Down));
        }
    }

    /// <inheritdoc />
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _gestures?.EndKeyMove();
    }

    /// <summary>A library entry dragged over the canvas can be dropped.</summary>
    private void DraggedOver(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DesignPaletteView.EntryFormat) && _gestures is not null ? DragDropEffects.Copy : DragDropEffects.None;
        if (_gestures is { } gestures && _document is { } document
            && e.Data.GetData(DesignPaletteView.EntryFormat) is string name && document.FindPaletteEntry(name) is { } entry)
        {
            // Where it would drop, and every grid's cells, as App Designer's drop hints (DS4).
            Point at = e.GetPosition(_overlay);
            gestures.DragOver(entry, new Point2D(at.X - Gutter, at.Y - Gutter));
        }

        e.Handled = true;
    }

    /// <summary>A library drag left the canvas.</summary>
    private void DragLeft() => _gestures?.DragLeave();

    /// <summary>A library entry dropped is added where it landed.</summary>
    private void Dropped(DragEventArgs e)
    {
        if (_gestures is not { } gestures || _document is not { } document
            || e.Data.GetData(DesignPaletteView.EntryFormat) is not string name || document.FindPaletteEntry(name) is not { } entry)
        {
            return;
        }

        Point at = e.GetPosition(_overlay);
        gestures.Drop(entry, new Point2D(at.X - Gutter, at.Y - Gutter), withoutLabel: (e.KeyStates & DragDropKeyStates.ControlKey) != 0);
        Focus();
        e.Handled = true;
    }

    // --- drawing ---------------------------------------------------------------------------------------

    private void Draw(DrawingContext dc)
    {
        // The overlay takes every press over the canvas, the gutter included.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, _overlay.ActualWidth, _overlay.ActualHeight));
        if (_document is not { } document || _gestures is not { } gestures)
        {
            return;
        }

        Brush accent = TryFindResource(ThemeKeys.Accent) as Brush ?? Brushes.DodgerBlue;
        var outline = new Pen(accent, 1);
        outline.Freeze();
        var dashed = new Pen(accent, 1) { DashStyle = DashStyles.Dash };
        dashed.Freeze();
        dc.PushTransform(new TranslateTransform(Gutter + gestures.FigureShift, Gutter));
        Size2D size = document.Runtime.Figure.Size;

        if (_showGrid)
        {
            var line = new Pen(GridBrush, 1);
            line.Freeze();
            for (double x = _gridInterval; x < size.Width; x += _gridInterval)
            {
                dc.DrawLine(line, new Point(x + 0.5, 0), new Point(x + 0.5, size.Height));
            }

            for (double y = _gridInterval; y < size.Height; y += _gridInterval)
            {
                dc.DrawLine(line, new Point(0, y + 0.5), new Point(size.Width, y + 0.5));
            }
        }

        // A hidden component is drawn faded, as App Designer's canvas draws it (35% opaque).
        JGraph.Core.Drawing.Color back = document.Runtime.Figure.Background;
        var fade = new SolidColorBrush(Color.FromArgb(0xA6, back.R, back.G, back.B));
        fade.Freeze();
        foreach (DesignBox box in document.Boxes)
        {
            Rect seen = Rect.Intersect(Wpf(box.Box), Wpf(box.Clip));
            if (box.Shown && box.Hidden && !seen.IsEmpty)
            {
                dc.DrawRectangle(fade, null, seen);
            }
        }

        if (gestures.DropTarget is { } target)
        {
            var pen = new Pen(accent, 2);
            pen.Freeze();
            dc.DrawRectangle(null, pen, Wpf(document.Runtime.InnerOnSurface(target)));
        }

        DrawGrids(dc, document, gestures, outline, dashed);

        // A responsive app's splitters between its regions (DS4b); the one dragged in the error colour
        // while the regions stop short of the pointer (ad-template-resize-handle-alert).
        (DesignSplitter Splitter, DesignRegionResize Resize)? splitting = gestures.Splitting;
        foreach (DesignSplitter splitter in document.Splitters())
        {
            bool stopped = splitting is { Resize.Stopped: true } s && s.Splitter.Leading == splitter.Leading;
            dc.DrawRectangle(stopped ? SplitterAlertBrush : SplitterBrush, null, Wpf(splitter.Box));
        }

        // A component selected on its own is outlined; a group selected whole has its outline 4
        // pixels outside its box (GROUP_SELECTION_BORDER_OFFSET_PX) and its members marked (open item 150).
        // The anchor of two or more is outlined 2 pixels wide (ad-layout-anchor, W4).
        var anchorPen = new Pen(accent, 2);
        anchorPen.Freeze();
        DesignPick? anchor = document.Anchor;
        foreach (DesignPick pick in document.Picks)
        {
            bool isAnchor = anchor is { } a && ((a.Component is not null && ReferenceEquals(a.Component, pick.Component)) || (a.Group is not null && ReferenceEquals(a.Group, pick.Group)));
            if (pick.Group is { } group)
            {
                if (document.BoxOfGroup(group) is { } gb)
                {
                    dc.DrawRectangle(null, isAnchor ? anchorPen : outline, Inset(new Rect(gb.X - 4, gb.Y - 4, gb.Width + 8, gb.Height + 8)));
                }

                foreach (DesignComponent member in group.AllComponents())
                {
                    if (document.BoxOf(member) is { Shown: true } mb)
                    {
                        dc.DrawRectangle(null, dashed, Inset(Wpf(document.CellBoxOf(member) ?? mb.Box)));
                    }
                }

                continue;
            }

            DesignComponent selected = pick.Component!;
            if (selected.Parent is null)
            {
                dc.DrawRectangle(null, outline, Inset(new Rect(0, 0, size.Width, size.Height)));
            }
            else if (document.BoxOf(selected) is { Shown: true } box)
            {
                // A grid's child is outlined on its cells (DS4).
                Rect r = Wpf(document.CellBoxOf(selected) ?? box.Box);
                dc.DrawRectangle(null, isAnchor ? anchorPen : outline, isAnchor ? new Rect(r.X + 1, r.Y + 1, System.Math.Max(r.Width - 2, 0), System.Math.Max(r.Height - 2, 0)) : Inset(r));
            }
        }

        // Visualize Tab Order: the component focus would be on, outlined 2 pixels wide (ad-tab-order-animation).
        if (TabOrderShown is { } focused && document.BoxOf(focused) is { Shown: true } focusBox)
        {
            var focusPen = new Pen(TryFindResource(ThemeKeys.Accent) as Brush ?? Brushes.DodgerBlue, 2);
            focusPen.Freeze();
            Rect f = Wpf(document.CellBoxOf(focused) ?? focusBox.Box);
            dc.DrawRectangle(null, focusPen, new Rect(f.X - 1, f.Y - 1, f.Width + 2, f.Height + 2));
        }

        foreach (DesignHandle handle in gestures.Handles())
        {
            dc.DrawRectangle(Brushes.White, outline, Inset(Wpf(handle.Box)));
        }

        foreach (DesignComponent banded in gestures.Banded)
        {
            if (document.BoxOf(banded) is { } box)
            {
                dc.DrawRectangle(null, dashed, Inset(Wpf(box.Box)));
            }
        }

        if (gestures.Band is { } band)
        {
            dc.DrawRectangle(BandBrush, dashed, Inset(Wpf(band)));
        }

        if (gestures.Drawn is { } drawn)
        {
            dc.DrawRectangle(BandBrush, outline, Inset(Wpf(drawn)));
        }

        foreach (DesignGuide guide in gestures.Guides)
        {
            var pen = new Pen(guide.Fixed ? FixedGuideBrush : GuideBrush, 1);
            pen.Freeze();
            if (guide.Vertical)
            {
                dc.DrawLine(pen, new Point(guide.Position + 0.5, guide.From), new Point(guide.Position + 0.5, guide.To));
            }
            else
            {
                dc.DrawLine(pen, new Point(guide.From, guide.Position + 0.5), new Point(guide.To, guide.Position + 0.5));
            }
        }

        dc.Pop();
    }

    /// <summary>
    /// The grids' marks (DS4, App Designer's design-time grid): each grid's cells dashed as its mode
    /// says - every grid's during a drag - the cells a drop or a drawing would take filled, a grid
    /// child's span being resized, the configure buttons, and a grid being configured: veiled, its
    /// frame dashed past its corners, its bands, and the row or column a menu item or button would
    /// add or delete.
    /// </summary>
    private void DrawGrids(DrawingContext dc, DesignDocument document, DesignGestures gestures, Pen outline, Pen dashed)
    {
        bool dark = TryFindResource(ThemeKeys.ThemeIsDark) is true;
        var cellPen = new Pen(dark ? CellDark : CellLight, 1) { DashStyle = DashStyles.Dash };
        cellPen.Freeze();
        Brush drop = dark ? DropDark : DropLight;
        IReadOnlyDictionary<DesignComponent, DesignGridMode> modes = gestures.GridModes();
        foreach (DesignBox box in document.Boxes)
        {
            DesignComponent grid = box.Component;
            if (!box.Shown || !document.IsGrid(grid) || document.CellsOf(grid) is not { } cells)
            {
                continue;
            }

            DesignGridMode mode = modes.TryGetValue(grid, out DesignGridMode m) ? m : DesignGridMode.Quiet;
            if (mode == DesignGridMode.Edit)
            {
                continue;
            }

            if (mode != DesignGridMode.Quiet || gestures.ShowsDropHints)
            {
                dc.PushClip(new RectangleGeometry(Wpf(box.Clip)));
                for (int r = 1; r <= cells.Rows; r++)
                {
                    for (int c = 1; c <= cells.Columns; c++)
                    {
                        dc.DrawRectangle(null, cellPen, Inset(Wpf(cells.Cell(r, c))));
                    }
                }

                dc.Pop();
            }

            if (mode == DesignGridMode.Simple && gestures.ConfigureButton(grid) is { } button)
            {
                DrawButton(dc, button, exit: false);
            }
        }

        // The cells a drop or a drawing would take.
        if (gestures.Placement is { CanDrop: true, Container: { } into, Spans: { } spans } && document.CellsOf(into) is { } target)
        {
            foreach (GridSpan span in spans)
            {
                dc.DrawRectangle(drop, null, Wpf(target.SpanRect(span)));
            }
        }

        if (gestures.DrawnCells is { } drawn && document.CellsOf(drawn.Grid) is { } drawnIn)
        {
            foreach (GridSpan span in drawn.Spans)
            {
                dc.DrawRectangle(drop, outline, Inset(Wpf(drawnIn.SpanRect(span))));
            }
        }

        // A grid child being moved shows where it is dragged; one being spanned, its dragged box.
        foreach ((DesignComponent _, Rect2D ghost) in document.GestureGhosts)
        {
            dc.DrawRectangle(BandBrush, dashed, Inset(Wpf(ghost)));
        }

        if (gestures.SpanPreview is { } preview)
        {
            dc.DrawRectangle(BandBrush, outline, Inset(Wpf(preview.Box)));
        }

        if (gestures.LabelSpanPreview is { } labelPreview)
        {
            dc.DrawRectangle(BandBrush, dashed, Inset(Wpf(labelPreview.Box)));
        }

        if (gestures.Configuring is { } configure && document.BoxOf(configure.Grid) is { } configured)
        {
            Rect gridBox = Wpf(configured.Box);
            dc.DrawRectangle(EditVeil, null, gridBox);
            var frame = new Pen(FrameLine, 1) { DashStyle = DashStyles.Dash };
            frame.Freeze();
            double tab = DesignGridConfigure.TabWidth;
            dc.DrawLine(frame, new Point(gridBox.Left - tab, gridBox.Top + 0.5), new Point(gridBox.Right + tab, gridBox.Top + 0.5));
            dc.DrawLine(frame, new Point(gridBox.Left - tab, gridBox.Bottom - 0.5), new Point(gridBox.Right + tab, gridBox.Bottom - 0.5));
            dc.DrawLine(frame, new Point(gridBox.Left + 0.5, gridBox.Top - tab), new Point(gridBox.Left + 0.5, gridBox.Bottom + tab));
            dc.DrawLine(frame, new Point(gridBox.Right - 0.5, gridBox.Top - tab), new Point(gridBox.Right - 0.5, gridBox.Bottom + tab));

            var bandEdge = new Pen(CellLight, 1);
            bandEdge.Freeze();
            var selectedEdge = new Pen(BandSelectedEdge, 1);
            selectedEdge.Freeze();
            IReadOnlyList<DesignGridBand> bands = configure.Bands();

            // The kind beneath first, so that the kind on top and the picked band lie over it.
            foreach (DesignGridBand band in bands.Where(static b => !b.Upper).Concat(bands.Where(static b => b.Upper)).OrderBy(static b => b.Selected))
            {
                Brush fill = band.Selected ? BandSelected : band.Hovered ? BandHovered : BandFill;
                dc.DrawRectangle(fill, band.Selected ? selectedEdge : bandEdge, Inset(Wpf(band.Box)));
                if (!band.Selected && !band.Hovered)
                {
                    // The tabs the band is picked by, outside the grid.
                    Rect b = Wpf(band.Box);
                    if (band.Axis == GridAxis.Row)
                    {
                        dc.DrawRectangle(TabFill, bandEdge, Inset(new Rect(b.Left, b.Top, tab, b.Height)));
                        dc.DrawRectangle(TabFill, bandEdge, Inset(new Rect(b.Right - tab, b.Top, tab, b.Height)));
                    }
                    else
                    {
                        dc.DrawRectangle(TabFill, bandEdge, Inset(new Rect(b.Left, b.Top, b.Width, tab)));
                        dc.DrawRectangle(TabFill, bandEdge, Inset(new Rect(b.Left, b.Bottom - tab, b.Width, tab)));
                    }
                }
            }

            if (gestures.ConfigureButton(configure.Grid) is { } exit)
            {
                DrawButton(dc, exit, exit: true);
            }
        }

        if (gestures.Hint is { } hint && DesignGridConfigure.HintBox(document, hint) is { } hinted)
        {
            dc.DrawRectangle(hint.Insert ? InsertHint : DeleteHint, null, Wpf(hinted));
        }
    }

    /// <summary>A grid's configure button - a grid with a pencil - or, configuring, the button that ends it - a cross.</summary>
    private static void DrawButton(DrawingContext dc, Rect2D at, bool exit)
    {
        var edge = new Pen(ButtonEdge, 1);
        edge.Freeze();
        var glyph = new Pen(GlyphBrush, 1);
        glyph.Freeze();
        Rect box = Inset(Wpf(at));
        dc.DrawRoundedRectangle(ButtonFill, edge, box, 5, 5);
        double x = box.X + 5;
        double y = box.Y + 5;
        double s = box.Width - 10;
        if (exit)
        {
            dc.DrawLine(glyph, new Point(x + 1, y + 1), new Point(x + s - 1, y + s - 1));
            dc.DrawLine(glyph, new Point(x + s - 1, y + 1), new Point(x + 1, y + s - 1));
            return;
        }

        dc.DrawRectangle(null, glyph, new Rect(x, y, s, s));
        dc.DrawLine(glyph, new Point(x + (s / 2), y), new Point(x + (s / 2), y + s));
        dc.DrawLine(glyph, new Point(x, y + (s / 2)), new Point(x + s, y + (s / 2)));
    }

    private static Rect Wpf(Rect2D r) => new(r.X, r.Y, System.Math.Max(0, r.Width), System.Math.Max(0, r.Height));

    /// <summary>A rectangle whose one-pixel outline falls on whole pixels.</summary>
    private static Rect Inset(Rect r) => new(r.X + 0.5, r.Y + 0.5, System.Math.Max(0, r.Width - 1), System.Math.Max(0, r.Height - 1));

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>The element over the figure that takes the canvas's presses and draws its marks.</summary>
    private sealed class Overlay(DesignCanvas owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawingContext) => owner.Draw(drawingContext);

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            owner.OnPressed(e);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            owner.OnMoved(e);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            owner.OnReleased();
            e.Handled = true;
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            owner.DraggedOver(e);
        }

        protected override void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);
            owner.Dropped(e);
        }

        protected override void OnDragLeave(DragEventArgs e)
        {
            base.OnDragLeave(e);
            owner.DragLeft();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (owner._gestures is { IsPressed: true } gestures)
            {
                gestures.Cancel();
            }
        }
    }
}
