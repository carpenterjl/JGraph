using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <content>
/// What the canvas draws for a figure's menus, toolbars and context menus (App Designer window plan,
/// stage W5d): the menu bar and toolbars above the figure's content, the list under a selected menu with
/// its +s, a dragged menu, tool or toolbar with its marker, and the Context Menus tray at the bottom of
/// the view - its tab, its title, an entry for each context menu, one opened to be edited - with what a
/// context menu's drag says and shows on the components it could go to.
/// </content>
public sealed partial class DesignCanvas
{
    /// <summary>The tray's tab, over the tray (<c>ad-canvas-tray-btn</c>).</summary>
    private const double TrayTabHeight = 20;

    private const double TrayTabWidth = 86;

    /// <summary>The room kept each side of a tray the view is too narrow for, so that its edges and the scroll bar show.</summary>
    private const double TrayMargin = 8;

    /// <summary>How far the tray's tab is dragged before it resizes rather than toggles (<c>_onDragMove</c>).</summary>
    private const double TrayDragStart = 5;

    private const double BarFontSize = 12;

    private static readonly Typeface BarFace = new("Segoe UI");
    private static readonly Brush BarLight = Frozen(new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4)));
    private static readonly Brush BarDark = Frozen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)));
    private static readonly Brush BarEdge = Frozen(new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)));
    private static readonly Brush RowLine = Frozen(new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)));
    private static readonly Brush TextLight = Frozen(new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21)));
    private static readonly Brush TextDark = Frozen(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));
    private static readonly Brush HoldsFill = Frozen(new SolidColorBrush(Color.FromRgb(0xA3, 0xDB, 0xFF)));
    private static readonly Brush SameHint = Frozen(new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0x45, 0x3A)));
    private static readonly Brush AssignFill = Frozen(new SolidColorBrush(Color.FromArgb(0x59, 0xA3, 0xDB, 0xFF)));
    private static readonly Brush SuccessBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x13)));
    private static readonly Brush ErrorBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)));
    private static readonly Brush InfoBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x00, 0x76, 0xA8)));

    private readonly Dictionary<UiImage, BitmapSource> _icons = [];
    private readonly Canvas _trayEditorHost = new() { IsHitTestVisible = true };
    private TrayView? _trayView;
    private DragLayer? _dragLayer;
    private Point _dragPointer;
    private double _trayBody = DesignGestures.TrayHeight;
    private Point? _trayTabFrom;
    private bool _trayResizing;
    private double _trayTabLastY;

    /// <summary>The height of the bars above the figure's content.</summary>
    private double BarsHeight => _gestures?.Bars.Height ?? 0;

    /// <summary>How far down the figure's content stands on the canvas: the gutter and the bars above it.</summary>
    private double Top => Gutter + BarsHeight;

    private void BuildTray(Grid host)
    {
        _trayView = new TrayView(this)
        {
            Width = DesignGestures.TrayWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            AllowDrop = true,
            Visibility = Visibility.Collapsed,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_trayView, "Canvas.ContextMenuTray");
        host.Children.Add(_trayView);
        _dragLayer = new DragLayer(this) { IsHitTestVisible = false };
        host.Children.Add(_dragLayer);
        host.Children.Add(_trayEditorHost);
        SizeChanged += (_, _) => SyncTrayView();
    }

    private double MeasureBarText(string text) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BarFace, BarFontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;

    private void OnBarMenuOpened(object? sender, (IReadOnlyList<DesignMenuItem> Items, Point2D At) menu)
    {
        if (_gestures is { } gestures)
        {
            ShowMenu(gestures, menu.Items, menu.At);
        }
    }

    /// <summary>Shows the tray as the gestures have it: gone with no context menu, down to its tab when closed.</summary>
    private void SyncTrayView()
    {
        if (_trayView is not { } tray)
        {
            return;
        }

        bool shown = _gestures?.TrayShown == true;
        tray.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        // A view narrower than the tray has it narrower too, so that none of it is cut off.
        double room = ActualWidth > 0 ? System.Math.Min(DesignGestures.TrayWidth, ActualWidth - (2 * TrayMargin)) : DesignGestures.TrayWidth;
        tray.Width = room;
        if (_gestures is { } gestures)
        {
            gestures.TrayAreaWidth = room;
        }

        tray.Height = TrayTabHeight + (_gestures?.TrayOpen == true ? _trayBody : 0);
        tray.Refresh();
        _dragLayer?.InvalidateVisual();
    }

    /// <summary>Whether the tray shows, and open.</summary>
    public bool IsTrayOpen => _gestures is { TrayShown: true, TrayOpen: true };

    // --- the bars --------------------------------------------------------------------------------------

    private void DrawBars(DrawingContext dc, DesignGestures gestures, Brush accent, Pen outline, Pen dashed)
    {
        DesignBarLayout bars = gestures.Bars;
        if (bars.Places.Count > 0)
        {
            DrawBarPlaces(dc, bars.Places, accent, outline);
        }

        if (!gestures.BarDragInTray)
        {
            DrawBarDrag(dc, gestures, accent, dashed);
        }

        // A context menu's drag: the component it would go to hinted, and those that have it already (DragAssignHighlighter).
        if (gestures.AssignDrag is { } drag)
        {
            foreach (DesignComponent holder in drag.Assigned)
            {
                if (BoxOfTarget(holder) is { } held)
                {
                    var hold = new Pen(accent, 1);
                    hold.Freeze();
                    dc.DrawRectangle(null, hold, Inset(new Rect(held.X - 3, held.Y - 3, held.Width + 6, held.Height + 6)));
                    DrawCheck(dc, new Rect(held.Right - 17, held.Y - 8, 12, 12));
                }
            }

            if (drag.Target is { } target && BoxOfTarget(target) is { } box)
            {
                Rect r = new(box.X - 2, box.Y - 2, box.Width + 4, box.Height + 4);
                var edge = new Pen(accent, 2) { DashStyle = DashStyles.Dash };
                edge.Freeze();
                if (drag.Hint == DesignAssignHint.Same)
                {
                    dc.DrawRectangle(SameHint, null, r);
                }
                else
                {
                    dc.DrawRectangle(AssignFill, edge, r);
                }
            }
        }
    }

    private Rect? BoxOfTarget(DesignComponent component)
    {
        if (_document is not { } document)
        {
            return null;
        }

        if (component.Parent is null)
        {
            Size2D size = document.Runtime.Figure.Size;
            return new Rect(0, 0, size.Width, size.Height);
        }

        return document.BoxOf(component) is { Shown: true } shown ? Wpf(shown.Box) : null;
    }

    private void DrawBarPlaces(DrawingContext dc, IReadOnlyList<DesignBarPlace> places, Brush accent, Pen outline)
    {
        bool dark = TryFindResource(Themes.ThemeKeys.ThemeIsDark) is true;
        Brush face = dark ? BarDark : BarLight;
        Brush ink = dark ? TextDark : TextLight;
        var edge = new Pen(BarEdge, 1);
        edge.Freeze();
        var line = new Pen(RowLine, 1);
        line.Freeze();
        var selectedFill = new SolidColorBrush(accent is SolidColorBrush solid ? Color.FromArgb(0x60, solid.Color.R, solid.Color.G, solid.Color.B) : Color.FromArgb(0x60, 0x1E, 0x90, 0xFF));
        selectedFill.Freeze();
        foreach (DesignBarPlace place in places)
        {
            Rect box = Wpf(place.Box);
            bool faded = place.Hidden;
            if (faded)
            {
                dc.PushOpacity(0.35);
            }

            switch (place.Kind)
            {
                case DesignBarPlaceKind.MenuBar:
                    dc.DrawRectangle(face, null, box);
                    dc.DrawLine(edge, new Point(box.Left, box.Bottom - 0.5), new Point(box.Right, box.Bottom - 0.5));
                    break;
                case DesignBarPlaceKind.Toolbar:
                    dc.DrawRectangle(face, null, box);
                    dc.DrawLine(edge, new Point(box.Left, box.Bottom - 0.5), new Point(box.Right, box.Bottom - 0.5));
                    if (place.Selected)
                    {
                        dc.DrawRectangle(null, outline, Inset(box));
                    }

                    break;
                case DesignBarPlaceKind.Menu:
                    DrawMenuRow(dc, place, box, face, ink, line, selectedFill, outline);
                    break;
                case DesignBarPlaceKind.Tool:
                    DrawTool(dc, place, box, ink, line, selectedFill, outline);
                    break;
                case DesignBarPlaceKind.AddChild or DesignBarPlaceKind.AddRight:
                    DrawRoundPlus(dc, box);
                    break;
                case DesignBarPlaceKind.AddTopLevel:
                    Glyph(dc, "+", box, ink, center: true);
                    break;
                case DesignBarPlaceKind.ToolbarPlus:
                    Glyph(dc, "+", box, ink, center: true);
                    break;
                case DesignBarPlaceKind.ToolbarArrow:
                    Glyph(dc, "▾", box, ink, center: true);
                    break;
            }

            if (faded)
            {
                dc.Pop();
            }
        }
    }

    private void DrawMenuRow(DrawingContext dc, DesignBarPlace place, Rect box, Brush face, Brush ink, Pen line, Brush selectedFill, Pen outline)
    {
        dc.DrawRectangle(place.InList ? (face == BarDark ? BarDark : Brushes.White) : face, place.InList ? line : null, place.InList ? Inset(box) : box);
        if (place.Separator)
        {
            dc.DrawLine(line, new Point(box.Left, box.Top + 0.5), new Point(box.Right, box.Top + 0.5));
        }

        Brush text = place.Disabled ? BarEdge : ink;
        if (place.InList)
        {
            if (place.Checked)
            {
                Glyph(dc, "✓", new Rect(box.X + DesignBarLayout.RowPadding - 4, box.Y, 18, box.Height), text, center: true);
            }

            Glyph(dc, place.Text, new Rect(box.X + DesignBarLayout.RowPadding + 18, box.Y, box.Width, box.Height), text, center: false);
            if (place.Shortcut.Length > 0)
            {
                double w = MeasureBarText(place.Shortcut);
                Glyph(dc, place.Shortcut, new Rect(box.Right - w - 14, box.Y, w, box.Height), text, center: false);
            }

            if (place.HasItems)
            {
                Glyph(dc, "▸", new Rect(box.Right - 14, box.Y, 14, box.Height), text, center: true);
            }
        }
        else
        {
            Glyph(dc, place.Text, box, text, center: true);
        }

        // Selected: filled and outlined (ad-selected); holding what is selected, marked (ad-child-menu-selected).
        if (place.Selected)
        {
            dc.DrawRectangle(selectedFill, outline, Inset(box));
        }
        else if (place.Holds)
        {
            dc.PushOpacity(0.5);
            dc.DrawRectangle(HoldsFill, null, box);
            dc.Pop();
        }
    }

    private void DrawTool(DrawingContext dc, DesignBarPlace place, Rect box, Brush ink, Pen line, Brush selectedFill, Pen outline)
    {
        if (place.Separator)
        {
            // gbtToolSeparator: 1 pixel wide, 22 high, in a margin of 2.
            dc.DrawLine(line, new Point(box.Left - 4.5, box.Top), new Point(box.Left - 4.5, box.Bottom));
        }

        if (place.Checked)
        {
            dc.DrawRectangle(HoldsFill, null, box);
        }

        Rect icon = new(box.X + 3, box.Y + 3, 16, 16);
        if (_document?.Runtime.ObjectOf(place.Component) is UiToolModel { Picture: { } picture })
        {
            if (!_icons.TryGetValue(picture, out BitmapSource? bitmap))
            {
                bitmap = BitmapSource.Create(picture.Width, picture.Height, 96, 96, PixelFormats.Bgra32, null, picture.Bgra, picture.Width * 4);
                bitmap.Freeze();
                _icons[picture] = bitmap;
            }

            if (place.Disabled)
            {
                dc.PushOpacity(0.5);
            }

            dc.DrawImage(bitmap, icon);
            if (place.Disabled)
            {
                dc.Pop();
            }
        }
        else
        {
            // A tool with no icon of its own: a faint square where one would go, so that it can be seen.
            dc.DrawRectangle(null, line, Inset(icon));
        }

        if (place.Selected)
        {
            dc.DrawRectangle(selectedFill, outline, Inset(box));
        }
    }

    private static void DrawCheck(DrawingContext dc, Rect box)
    {
        var pen = new Pen(SuccessBrush, 1.5);
        pen.Freeze();
        dc.DrawLine(pen, new Point(box.X + 2, box.Y + 6), new Point(box.X + 5, box.Y + 9));
        dc.DrawLine(pen, new Point(box.X + 5, box.Y + 9), new Point(box.X + 10, box.Y + 3));
    }

    // float-toolbar: an 18-pixel round button with a plus.
    private static void DrawRoundPlus(DrawingContext dc, Rect box)
    {
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), 1);
        edge.Freeze();
        dc.DrawEllipse(PlusFace, edge, new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2)), (box.Width / 2) - 0.5, (box.Height / 2) - 0.5);
        var stroke = new Pen(PlusGlyph, 1.5);
        stroke.Freeze();
        double cx = box.X + (box.Width / 2);
        double cy = box.Y + (box.Height / 2);
        dc.DrawLine(stroke, new Point(cx - 4, cy), new Point(cx + 4, cy));
        dc.DrawLine(stroke, new Point(cx, cy - 4), new Point(cx, cy + 4));
    }

    private void Glyph(DrawingContext dc, string text, Rect box, Brush ink, bool center)
    {
        if (text.Length == 0)
        {
            return;
        }

        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BarFace, BarFontSize, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxLineCount = 1,
        };
        double x = center ? box.X + ((box.Width - formatted.WidthIncludingTrailingWhitespace) / 2) : box.X;
        dc.DrawText(formatted, new Point(x, box.Y + ((box.Height - formatted.Height) / 2)));
    }

    private void DrawBarDrag(DrawingContext dc, DesignGestures gestures, Brush accent, Pen dashed)
    {
        if (gestures.BarDragged is { } dragged)
        {
            dc.DrawRectangle(HoldsFill, dashed, Inset(Wpf(dragged)));
        }

        if (gestures.BarDrop is { } drop)
        {
            var marker = new Pen(accent, 2);
            marker.Freeze();
            Rect b = Wpf(drop.Box);
            if (drop.Across)
            {
                double x = drop.After ? b.Right - 1 : b.Left + 1;
                dc.DrawLine(marker, new Point(x, b.Top - 3), new Point(x, b.Bottom + 3));
            }
            else
            {
                double y = drop.After ? b.Bottom - 1 : b.Top + 1;
                dc.DrawLine(marker, new Point(b.Left - 3, y), new Point(b.Right + 3, y));
            }
        }
    }

    // --- the tray --------------------------------------------------------------------------------------

    private void DrawTray(DrawingContext dc, double width, double height)
    {
        if (_gestures is not { } gestures)
        {
            return;
        }

        bool dark = TryFindResource(Themes.ThemeKeys.ThemeIsDark) is true;
        Brush face = dark ? BarDark : BarLight;
        Brush ink = dark ? TextDark : TextLight;
        var edge = new Pen(BarEdge, 1);
        edge.Freeze();
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        // The tab, its arrow up to open and down to close.
        Rect tab = new((width - TrayTabWidth) / 2, 0, TrayTabWidth, TrayTabHeight);
        dc.DrawRoundedRectangle(face, edge, Inset(tab), 8, 8);
        Glyph(dc, gestures.TrayOpen ? "⌄" : "⌃", tab, ink, center: true);
        if (!gestures.TrayOpen)
        {
            return;
        }

        Rect body = new(0, TrayTabHeight, width, height - TrayTabHeight);
        dc.DrawRoundedRectangle(face, edge, Inset(body), 6, 6);
        dc.DrawLine(edge, new Point(0, TrayTabHeight + DesignGestures.TrayTitle - 0.5), new Point(width, TrayTabHeight + DesignGestures.TrayTitle - 0.5));
        Glyph(dc, "Context Menus", new Rect(10, TrayTabHeight, width - 20, DesignGestures.TrayTitle), ink, center: false);

        dc.PushClip(new RectangleGeometry(body));
        dc.PushTransform(new TranslateTransform(0, TrayTabHeight));
        Brush accent = TryFindResource(Themes.ThemeKeys.Accent) as Brush ?? Brushes.DodgerBlue;
        var outline = new Pen(accent, 1);
        outline.Freeze();
        var dashed = new Pen(accent, 1) { DashStyle = DashStyles.Dash };
        dashed.Freeze();
        if (gestures.TrayGoBack is { } back)
        {
            Glyph(dc, "<  " + back.Text, Wpf(back.Box), ink, center: false);
        }

        IReadOnlyList<DesignBarPlace> places = gestures.TrayPlaces();
        if (gestures.TrayEditing is not null)
        {
            DrawBarPlaces(dc, places, accent, outline);
        }
        else
        {
            foreach (DesignBarPlace entry in places)
            {
                DrawTrayEntry(dc, entry, face, ink, edge, accent);
            }
        }

        if (gestures.BarDragInTray)
        {
            DrawBarDrag(dc, gestures, accent, dashed);
        }

        dc.Pop();
        dc.Pop();
    }

    private void DrawTrayEntry(DrawingContext dc, DesignBarPlace entry, Brush face, Brush ink, Pen edge, Brush accent)
    {
        Rect box = Wpf(entry.Box);

        // Its name in italics and how many it is assigned to, a tick by the number or a warning by none (ad-contextmenu-entry-badge).
        var name = new FormattedText(entry.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(BarFace.FontFamily, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal), 11, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxLineCount = 1,
            MaxTextWidth = System.Math.Max(1, box.Width - 30),
            Trimming = TextTrimming.CharacterEllipsis,
        };
        dc.DrawText(name, new Point(box.X, box.Y));
        string count = entry.Assignments.ToString(CultureInfo.InvariantCulture);
        Rect badge = new(box.Right - 26, box.Y, 12, 12);
        if (entry.Assignments > 0)
        {
            DrawCheck(dc, badge);
        }
        else
        {
            Glyph(dc, "⚠", badge, InfoBrush, center: true);
        }

        Glyph(dc, count, new Rect(box.Right - 12, box.Y, 12, 12), ink, center: true);

        // The first of its items, as the 96 pixels show them.
        Rect body = new(box.X, box.Y + DesignGestures.TrayEntryHeader, box.Width, DesignGestures.TrayEntryBody);
        dc.DrawRectangle(Brushes.White, edge, Inset(body));
        dc.PushClip(new RectangleGeometry(body));
        double y = body.Y;
        foreach (string item in entry.Items)
        {
            if (y >= body.Bottom)
            {
                break;
            }

            Glyph(dc, item, new Rect(body.X + DesignBarLayout.RowPadding, y, body.Width - DesignBarLayout.RowPadding, DesignBarLayout.RowHeight), TextLight, center: false);
            y += DesignBarLayout.RowHeight;
        }

        dc.Pop();
        if (entry.Selected)
        {
            var pen = new Pen(accent, 2);
            pen.Freeze();
            dc.DrawRectangle(null, pen, body);
        }
    }

    private Point2D OnTray(Point at) => new(at.X, at.Y - TrayTabHeight);

    private void TrayPressed(MouseButtonEventArgs e)
    {
        if (_trayView is not { } tray || _gestures is not { } gestures)
        {
            return;
        }

        Focus();
        Point at = e.GetPosition(tray);
        _dragPointer = e.GetPosition((UIElement)Content);
        bool onTab = at.Y < TrayTabHeight;
        bool onTitle = gestures.TrayOpen && at.Y < TrayTabHeight + DesignGestures.TrayTitle;
        if (e.ChangedButton == MouseButton.Left && (onTab || onTitle))
        {
            // The tab and the title: a click opens or closes the tray, a drag resizes it (_onDragBegin).
            _trayTabFrom = at;
            _trayResizing = false;
            _trayTabLastY = e.GetPosition((UIElement)Content).Y;
            tray.CaptureMouse();
            return;
        }

        DesignButton button = e.ChangedButton switch
        {
            MouseButton.Left => DesignButton.Left,
            MouseButton.Right => DesignButton.Right,
            _ => DesignButton.Middle,
        };
        Point2D place = OnTray(at);
        if (button == DesignButton.Left && e.ClickCount == 2 && gestures.DoubleClickTray(place))
        {
            return;
        }

        if (gestures.PressTray(place, button, Modifiers()))
        {
            tray.CaptureMouse();
        }
        else if (button == DesignButton.Right)
        {
            ShowMenu(gestures, gestures.TrayMenu(), null);
        }
    }

    private void TrayMoved(MouseEventArgs e)
    {
        if (_trayView is not { } tray || _gestures is not { } gestures)
        {
            return;
        }

        var host = (UIElement)Content;
        _dragPointer = e.GetPosition(host);
        if (_trayTabFrom is not null)
        {
            double y = _dragPointer.Y;
            double moved = y - _trayTabLastY;
            if (_trayResizing || System.Math.Abs(moved) >= TrayDragStart)
            {
                _trayResizing = true;
                gestures.TrayOpen = true;
                _trayBody = System.Math.Clamp(_trayBody - moved, 0, System.Math.Max(0, ActualHeight - TrayTabHeight - 23));
                _trayTabLastY = y;
                SyncTrayView();
            }

            return;
        }

        if (gestures.IsPressed)
        {
            Point at = e.GetPosition(tray);
            Point onOverlay = e.GetPosition(_overlay);
            bool over = at.X >= 0 && at.X <= tray.ActualWidth && at.Y >= 0 && at.Y <= tray.ActualHeight;
            gestures.DragTray(OnTray(at), new Point2D(onOverlay.X - Gutter, onOverlay.Y - Top), over, Modifiers());
            tray.Refresh();
            _dragLayer?.InvalidateVisual();
        }
        else if (tray.IsMouseOver)
        {
            UpdateTrayTip(OnTray(e.GetPosition(tray)));
        }
    }

    // What an entry's heading says it is assigned to (_populateTooltip).
    private void UpdateTrayTip(Point2D at)
    {
        if (_trayView is not { } tray || _gestures is not { } gestures || _document is not { } document)
        {
            return;
        }

        DesignBarPlace? place = gestures.TrayEditing is null ? gestures.TrayPlaces().FirstOrDefault(p => p.Box.Contains(at)) : null;
        if (place is null || at.Y > place.Box.Y + DesignGestures.TrayEntryHeader)
        {
            tray.ToolTip = null;
            return;
        }

        IReadOnlyList<DesignComponent> assigned = document.AssignmentsOf(place.Component);
        tray.ToolTip = assigned.Count > 0
            ? "Assigned to:\n" + string.Join('\n', assigned.Select(static c => "app." + c.CodeName))
            : "Drag to component to assign";
    }

    private void TrayReleased(MouseButtonEventArgs e)
    {
        if (_trayView is not { } tray || _gestures is not { } gestures)
        {
            return;
        }

        if (_trayTabFrom is not null)
        {
            _trayTabFrom = null;
            tray.ReleaseMouseCapture();
            if (_trayResizing)
            {
                _trayResizing = false;
                if (_trayBody <= 150)
                {
                    // Dragged small, it closes, and opens again at a size to work in (_onDragEnd).
                    _trayBody = 151;
                    gestures.TrayOpen = false;
                }
            }
            else
            {
                gestures.TrayOpen = !gestures.TrayOpen;
            }

            SyncTrayView();
            e.Handled = true;
            return;
        }

        if (gestures.IsPressed)
        {
            gestures.Release();
            tray.ReleaseMouseCapture();
        }
    }

    private void TrayDraggedOver(DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (_gestures is { } gestures && _document is { } document
            && e.Data.GetData(DesignPaletteView.EntryFormat) is string name && document.FindPaletteEntry(name) is { } entry)
        {
            _dragPointer = e.GetPosition((UIElement)Content);
            if (gestures.DragOverTray(entry))
            {
                e.Effects = DragDropEffects.Copy;
            }

            _dragLayer?.InvalidateVisual();
        }

        e.Handled = true;
    }

    private void TrayDropped(DragEventArgs e)
    {
        if (_gestures is { } gestures && _document is { } document
            && e.Data.GetData(DesignPaletteView.EntryFormat) is string name && document.FindPaletteEntry(name) is { } entry)
        {
            gestures.DropOnTray(entry);
            Focus();
            e.Handled = true;
        }
    }

    /// <summary>What a context menu's drag says beside the pointer (<c>ContextMenuDragTooltip</c>), over everything.</summary>
    private void DrawAssignTip(DrawingContext dc)
    {
        if (_gestures?.AssignDrag is not { } drag)
        {
            return;
        }

        Brush ring = drag.Hint switch
        {
            DesignAssignHint.Valid or DesignAssignHint.Tray => SuccessBrush,
            DesignAssignHint.Invalid => ErrorBrush,
            _ => InfoBrush,
        };
        var pen = new Pen(ring, 1);
        pen.Freeze();
        var text = new FormattedText(drag.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BarFace, BarFontSize, TextLight, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        Rect box = new(_dragPointer.X + 10, _dragPointer.Y + 10, text.WidthIncludingTrailingWhitespace + 10, 20);
        dc.DrawRoundedRectangle(Brushes.White, pen, box, 2, 2);
        dc.DrawText(text, new Point(box.X + 5, box.Y + ((box.Height - text.Height) / 2)));
    }

    /// <summary>The tray: drawn from what the gestures give, and taking its own presses.</summary>
    private sealed class TrayView(DesignCanvas owner) : FrameworkElement
    {
        public void Refresh()
        {
            InvalidateVisual();
            System.Windows.Automation.AutomationProperties.SetName(this, owner._document is { } document
                ? "Context Menus: " + string.Join(", ", document.ContextMenus.Select(c => $"{c.CodeName} ({document.AssignmentsOf(c).Count})"))
                : "Context Menus");
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

        protected override void OnRender(DrawingContext drawingContext) => owner.DrawTray(drawingContext, ActualWidth, ActualHeight);

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            owner.TrayPressed(e);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            owner.TrayMoved(e);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            owner.TrayReleased(e);
            e.Handled = true;
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            owner.TrayDraggedOver(e);
        }

        protected override void OnDrop(DragEventArgs e)
        {
            base.OnDrop(e);
            owner.TrayDropped(e);
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

            owner._trayTabFrom = null;
        }
    }

    /// <summary>Over the whole view, what a context menu's drag says beside the pointer; it takes no presses.</summary>
    private sealed class DragLayer(DesignCanvas owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawingContext) => owner.DrawAssignTip(drawingContext);
    }
}
