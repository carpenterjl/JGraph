using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JGraph.Controls.Themes;
using JGraph.Scripting.Toolstrip;

namespace JGraph.Controls.Toolstrip;

// Alt key tips (R2025b's MnemonicsManager): Alt pressed and released alone shows the tabs' and the
// quick access bar's tips; a tab's tip selects it and shows its controls'; a control's runs it.
public sealed partial class Toolstrip
{
    private readonly ToolstripKeyTips _keyTips = new();
    private readonly Dictionary<string, FrameworkElement> _tipTargets = new(StringComparer.Ordinal);
    private readonly List<ToolstripKeyTipAdorner> _adorners = [];
    private Window? _window;
    private bool _altDown;
    private bool _altClean;

    /// <summary>Which key tips are showing.</summary>
    public ToolstripKeyTipMode KeyTipMode => _keyTips.Mode;

    /// <summary>The key tips showing, with the automation id of what each reaches.</summary>
    public IReadOnlyList<(string Tip, string Target)> ShownKeyTips =>
        [.. _keyTips.Shown.Select(t => (t.Tip, _tipTargets.TryGetValue(t.Target, out FrameworkElement? e) ? System.Windows.Automation.AutomationProperties.GetAutomationId(e) : t.Target))];

    /// <summary>Shows the tabs' and the quick access bar's key tips, as Alt does.</summary>
    public void ShowKeyTips()
    {
        _tipTargets.Clear();
        var tips = new List<ToolstripKeyTip>();
        foreach (TabItem header in _tabs.Items.OfType<TabItem>().Where(h => h.Visibility == Visibility.Visible))
        {
            Target(tips, header, ToolstripKeyTipScope.Tab);
        }

        foreach (FrameworkElement button in _quickAccess.Children.OfType<FrameworkElement>().Where(b => b.Visibility == Visibility.Visible))
        {
            Target(tips, button, ToolstripKeyTipScope.QuickAccess);
        }

        _keyTips.ShowTabs(tips);
        DrawKeyTips();
    }

    /// <summary>Takes a key as if typed while the tips show; returns what it matched.</summary>
    public ToolstripKeyTipMatch TypeKeyTip(char key)
    {
        (ToolstripKeyTipMatch match, ToolstripKeyTip? tip) = _keyTips.Type(key);
        if (match == ToolstripKeyTipMatch.Partial)
        {
            DrawKeyTips();
        }
        else if (match == ToolstripKeyTipMatch.Full && tip is not null)
        {
            Act(tip);
        }

        return match;
    }

    private void HideKeyTips()
    {
        if (_keyTips.Mode != ToolstripKeyTipMode.Off)
        {
            _keyTips.Hide();
        }

        DrawKeyTips();
    }

    private void Target(List<ToolstripKeyTip> tips, FrameworkElement element, ToolstripKeyTipScope scope)
    {
        string tip = ToolstripKeyTipService.GetTip(element);
        if (tip.Length == 0)
        {
            return;
        }

        string key = "t" + _tipTargets.Count.ToString(CultureInfo.InvariantCulture);
        _tipTargets[key] = element;
        tips.Add(new ToolstripKeyTip(tip, key, scope));
    }

    private void ShowControlTips()
    {
        if (_tabs.SelectedItem is not TabItem { Tag: string tag } || !_tabStates.TryGetValue(tag, out TabState? state))
        {
            HideKeyTips();
            return;
        }

        _tipTargets.Clear();
        var tips = new List<ToolstripKeyTip>();
        foreach ((_, _, FrameworkElement element) in state.Tipped)
        {
            if (element.IsVisible && element.IsEnabled)
            {
                Target(tips, element, ToolstripKeyTipScope.Control);
            }
        }

        _keyTips.ShowControls(tips);
        DrawKeyTips();
    }

    private void Act(ToolstripKeyTip tip)
    {
        if (!_tipTargets.TryGetValue(tip.Target, out FrameworkElement? element))
        {
            HideKeyTips();
            return;
        }

        switch (tip.Scope)
        {
            case ToolstripKeyTipScope.Tab:
                _tabs.SelectedItem = element;
                if (_minimized)
                {
                    ShowOverlay();
                }

                // After the tab is laid out and fitted, so collapsed sections show their drop-downs' tips.
                Dispatcher.BeginInvoke(() =>
                {
                    Refit();
                    UpdateLayout();
                    ShowControlTips();
                }, DispatcherPriority.Loaded);
                break;
            default:
                HideKeyTips();
                Activate(element);
                break;
        }
    }

    private static void Activate(FrameworkElement element)
    {
        switch (element)
        {
            case ToolstripPopupButton popup:
                popup.Focus();
                popup.IsChecked = true;
                break;
            case ToolstripPushButton push:
                push.PerformClick();
                break;
            case ToolstripToggleButton toggle:
                toggle.PerformClick();
                break;
            case TextBox box:
                box.Focus();
                box.SelectAll();
                break;
            default:
                element.Focus();
                break;
        }
    }

    private void AttachKeyTips()
    {
        DetachKeyTips();
        _window = Window.GetWindow(this);
        if (_window is null)
        {
            return;
        }

        _window.PreviewKeyDown += OnWindowKeyDown;
        _window.PreviewKeyUp += OnWindowKeyUp;
        _window.PreviewMouseDown += OnWindowMouseDown;
        _window.Deactivated += OnWindowDeactivated;
    }

    private void DetachKeyTips()
    {
        if (_window is null)
        {
            return;
        }

        _window.PreviewKeyDown -= OnWindowKeyDown;
        _window.PreviewKeyUp -= OnWindowKeyUp;
        _window.PreviewMouseDown -= OnWindowMouseDown;
        _window.Deactivated -= OnWindowDeactivated;
        _window = null;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt)
        {
            if (!_altDown)
            {
                _altDown = true;
                _altClean = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows)) == 0;
            }

            return;
        }

        if (_altDown)
        {
            _altClean = false;
        }

        if (_keyTips.Mode != ToolstripKeyTipMode.Off)
        {
            if (key == Key.Escape)
            {
                switch (_keyTips.Back())
                {
                    case ToolstripKeyTipMode.Tabs when _keyTips.Mode == ToolstripKeyTipMode.Controls:
                        ShowKeyTips();
                        break;
                    default:
                        DrawKeyTips();
                        break;
                }

                e.Handled = true;
            }
            else if (KeyChar(key) is char c)
            {
                TypeKeyTip(c);
                e.Handled = true;
            }
            else if (key is not (Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl))
            {
                HideKeyTips();
            }

            return;
        }

        // Alt and a letter together reach a tab directly; a key that is no tab's is left to the window.
        if (_altDown && Keyboard.Modifiers == ModifierKeys.Alt && KeyChar(key) is char d && IsVisible && _tabStates.Count > 0)
        {
            ShowKeyTips();
            if (TypeKeyTip(d) == ToolstripKeyTipMatch.None)
            {
                HideKeyTips();
                return;
            }

            _altClean = false;
            e.Handled = true;
        }
    }

    private void OnWindowKeyUp(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.LeftAlt or Key.RightAlt))
        {
            return;
        }

        if (_altDown && _altClean && IsVisible && _tabStates.Count > 0)
        {
            if (_keyTips.Mode == ToolstripKeyTipMode.Off)
            {
                ShowKeyTips();
            }
            else
            {
                HideKeyTips();
            }

            e.Handled = true;
        }

        _altDown = false;
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        _altClean = false;
        HideKeyTips();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _altDown = false;
        HideKeyTips();
    }

    private static char? KeyChar(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (char)('A' + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
        _ => null,
    };

    // One adorner per adorner layer the shown tips' elements sit in: the toolstrip's, and the
    // minimized toolstrip's overlay's.
    private void DrawKeyTips()
    {
        foreach (ToolstripKeyTipAdorner adorner in _adorners)
        {
            AdornerLayer.GetAdornerLayer(adorner.AdornedElement)?.Remove(adorner);
        }

        _adorners.Clear();
        if (_keyTips.Mode == ToolstripKeyTipMode.Off)
        {
            return;
        }

        var groups = new Dictionary<UIElement, List<(string, FrameworkElement)>>();
        foreach (ToolstripKeyTip tip in _keyTips.Shown)
        {
            if (_tipTargets.TryGetValue(tip.Target, out FrameworkElement? element) && element.IsVisible && Decorated(element) is { } root)
            {
                if (!groups.TryGetValue(root, out List<(string, FrameworkElement)>? list))
                {
                    groups[root] = list = [];
                }

                list.Add((tip.Tip, element));
            }
        }

        foreach ((UIElement root, List<(string, FrameworkElement)> tips) in groups)
        {
            if (AdornerLayer.GetAdornerLayer(root) is { } layer)
            {
                var adorner = new ToolstripKeyTipAdorner(root, tips);
                layer.Add(adorner);
                _adorners.Add(adorner);
            }
        }
    }

    private static UIElement? Decorated(DependencyObject? node)
    {
        DependencyObject? child = null;
        while (node is not null)
        {
            if (node is AdornerDecorator decorator)
            {
                return decorator.Child ?? child as UIElement;
            }

            child = node;
            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }
}

/// <summary>Draws key tips as small badges under the controls they reach.</summary>
internal sealed class ToolstripKeyTipAdorner : Adorner
{
    private readonly IReadOnlyList<(string Tip, FrameworkElement Element)> _tips;

    public ToolstripKeyTipAdorner(UIElement adorned, IReadOnlyList<(string Tip, FrameworkElement Element)> tips)
        : base(adorned)
    {
        _tips = tips;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var element = (FrameworkElement)AdornedElement;
        Brush back = element.TryFindResource(ThemeKeys.Text) as Brush ?? Brushes.Black;
        Brush fore = element.TryFindResource(ThemeKeys.Window) as Brush ?? Brushes.White;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach ((string tip, FrameworkElement target) in _tips)
        {
            Point at;
            try
            {
                at = target.TranslatePoint(new Point(0, 0), AdornedElement);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var text = new FormattedText(tip, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, fore, dpi);
            double width = text.Width + 6;
            double height = text.Height + 1;
            // R2025b's placement: centred under a large button or a tab, else 12 pixels in; 5 pixels above the bottom.
            bool centred = target is TabItem || target.ActualHeight > Toolstrip.RowHeight + 4;
            double left = centred ? at.X + target.ActualWidth / 2 - width / 2 : at.X + 12;
            double top = at.Y + target.ActualHeight - 5;
            dc.DrawRoundedRectangle(back, null, new Rect(left, top, width, height), 2, 2);
            dc.DrawText(text, new Point(left + 3, top + 0.5));
        }
    }
}
